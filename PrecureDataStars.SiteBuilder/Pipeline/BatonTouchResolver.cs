using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 「最終話バトンタッチ」のキャラクターを見分け、本来の作品を求める。
/// <para>
/// 次の TV シリーズのプリキュアやパートナーの妖精は、前の TV シリーズの最終話に顔を出すことがある。
/// 声の出演でいちばん早い作品を出身にすると、こうしたキャラクターが前の作品のキャラクターになってしまうので、
/// 次の 3 つをすべて満たすキャラクターは、次の TV シリーズを本来の作品とする。
/// </para>
/// <list type="bullet">
///   <item><description>声の出演でいちばん早い作品が TV シリーズで、その作品での登場が最終話の 1 話だけ</description></item>
///   <item><description>その次に始まる TV シリーズがある</description></item>
///   <item><description>次の TV シリーズの第 1 話に登場している</description></item>
/// </list>
/// <para>
/// 次の TV シリーズの第 1 話のクレジットが入るまでは当てはまらず、前の作品のキャラクターとして扱う。
/// 「初登場」の話（<see cref="FirstAppearanceResolver"/>）は事実なので、この判定では変えない。
/// </para>
/// </summary>
public static class BatonTouchResolver
{
    /// <summary>TV シリーズの種別コード（<see cref="Series.KindCode"/>）。</summary>
    private const string TvKindCode = "TV";

    /// <summary>
    /// 声の出演の関与の列から、最終話バトンタッチかどうかを求める。当てはまらなければ null。
    /// </summary>
    public static BatonTouch? Resolve(BuildContext ctx, IEnumerable<Involvement> voiceCastInvolvements)
        => Resolve(ctx, voiceCastInvolvements.Select(i => (i.SeriesId, i.EpisodeId)));

    /// <summary>
    /// 声の出演での登場（作品と話の組。話を持たないクレジットは話が null）の列から、最終話バトンタッチかどうかを求める。
    /// 当てはまらなければ null。
    /// </summary>
    public static BatonTouch? Resolve(BuildContext ctx, IEnumerable<(int SeriesId, int? EpisodeId)> appearances)
    {
        var list = appearances.Where(a => ctx.SeriesById.ContainsKey(a.SeriesId)).Distinct().ToList();
        if (list.Count == 0) return null;

        // 声の出演でいちばん早い作品（放送・公開の始まり → series_id の順）。
        var from = list
            .Select(a => ctx.SeriesById[a.SeriesId])
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .First();
        if (!string.Equals(from.KindCode, TvKindCode, StringComparison.Ordinal)) return null;

        // その作品での登場が最終話の 1 話だけ。
        var inFrom = list.Where(a => a.SeriesId == from.SeriesId).ToList();
        if (inFrom.Count != 1 || inFrom[0].EpisodeId is not int fromEpisodeId) return null;
        if (ctx.LookupEpisode(from.SeriesId, fromEpisodeId) is not { } fromEpisode) return null;
        if (!ctx.EpisodesBySeries.TryGetValue(from.SeriesId, out var fromEpisodes) || fromEpisodes.Count == 0) return null;
        if (fromEpisode.SeriesEpNo != fromEpisodes.Max(e => e.SeriesEpNo)) return null;

        // その次に始まる TV シリーズ。
        var home = ctx.SeriesById.Values
            .Where(s => string.Equals(s.KindCode, TvKindCode, StringComparison.Ordinal) && s.StartDate > from.StartDate)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .FirstOrDefault();
        if (home is null) return null;

        // 次の TV シリーズの第 1 話に登場している。
        foreach (var a in list)
        {
            if (a.SeriesId != home.SeriesId || a.EpisodeId is not int episodeId) continue;
            if (ctx.LookupEpisode(home.SeriesId, episodeId) is { SeriesEpNo: 1 })
                return new BatonTouch(from.SeriesId, fromEpisodeId, home.SeriesId, episodeId);
        }
        return null;
    }
}

/// <summary>
/// 最終話バトンタッチの中身。<paramref name="FromSeriesId"/>・<paramref name="FromEpisodeId"/> は顔を出した前の作品とその最終話、
/// <paramref name="HomeSeriesId"/>・<paramref name="HomeFirstEpisodeId"/> は本来の作品とその第 1 話。
/// </summary>
public sealed record BatonTouch(int FromSeriesId, int FromEpisodeId, int HomeSeriesId, int HomeFirstEpisodeId);
