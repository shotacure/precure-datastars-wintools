using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// キャラクターの本来の作品が、声の出演でいちばん早い作品と違う場合を見分ける。
/// <para>
/// 出身の作品は、声の出演で登場した作品のうち放送・公開の始まりがいちばん早いものにしている。
/// TV シリーズは映画より先に始まり、次の TV シリーズより先に始まるので、次の 2 つの形のキャラクターは
/// そのままだと顔を出しただけの TV シリーズのキャラクターになってしまう。当てはまるキャラクターは、本来の作品を別に返す。
/// </para>
/// <list type="bullet">
///   <item><description>最終話バトンタッチ（<see cref="CharacterHomeKind.FinalEpisodeBaton"/>）：次の TV シリーズのプリキュアやパートナーの妖精が、
///   前の TV シリーズの最終話に顔を出す。いちばん早い作品が TV シリーズで、その作品での登場が最終話の 1 話だけ、
///   かつ次に始まる TV シリーズの第 1 話に登場しているとき、次の TV シリーズを本来の作品とする。</description></item>
///   <item><description>映画タイアップ（<see cref="CharacterHomeKind.MovieTieIn"/>）：映画のキャラクターが、公開に合わせて TV シリーズに顔を出す。
///   いちばん早い作品が TV シリーズで、その作品での登場が 1 話だけ、かつその回の放送の前後
///   <see cref="MovieTieInWindowDays"/> 日以内に公開された映画に登場しているとき、その映画を本来の作品とする。</description></item>
/// </list>
/// <para>
/// 本来の作品の側のクレジット（次の TV シリーズの第 1 話・映画）が入るまでは当てはまらず、顔を出した TV シリーズのキャラクターとして扱う。
/// 「初登場」の話（<see cref="FirstAppearanceResolver"/>）は事実なので、この判定では変えない。
/// </para>
/// </summary>
public static class CharacterHomeResolver
{
    /// <summary>TV シリーズの種別コード（<see cref="Series.KindCode"/>）。</summary>
    private const string TvKindCode = "TV";

    /// <summary>映画（単独の長編）の種別コード（<see cref="Series.KindCode"/>）。</summary>
    private const string MovieKindCode = "MOVIE";

    /// <summary>
    /// 映画タイアップと見なす、TV の回の放送日と映画の公開日の間の日数（前後とも）。
    /// 公開のひと月ほど前の回に顔を出す例が入り、同じ年の離れた時期に 1 話だけ出た TV のキャラクターを拾わない幅にしている。
    /// </summary>
    public const int MovieTieInWindowDays = 42;

    /// <summary>
    /// 声の出演の関与の列から、本来の作品が別にあるかを求める。当てはまらなければ null。
    /// </summary>
    public static CharacterHome? Resolve(BuildContext ctx, IEnumerable<Involvement> voiceCastInvolvements)
        => Resolve(ctx, voiceCastInvolvements.Select(i => (i.SeriesId, i.EpisodeId)));

    /// <summary>
    /// 声の出演での登場（作品と話の組。話を持たないクレジットは話が null）の列から、本来の作品が別にあるかを求める。
    /// 当てはまらなければ null。
    /// </summary>
    public static CharacterHome? Resolve(BuildContext ctx, IEnumerable<(int SeriesId, int? EpisodeId)> appearances)
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

        // その作品での登場が 1 話だけ（どちらの形にも共通の条件）。
        var inFrom = list.Where(a => a.SeriesId == from.SeriesId).ToList();
        if (inFrom.Count != 1 || inFrom[0].EpisodeId is not int fromEpisodeId) return null;
        if (ctx.LookupEpisode(from.SeriesId, fromEpisodeId) is not { } fromEpisode) return null;

        return ResolveFinalEpisodeBaton(ctx, list, from, fromEpisode)
            ?? ResolveMovieTieIn(ctx, list, from, fromEpisode);
    }

    /// <summary>最終話バトンタッチ：顔を出した回が最終話で、次に始まる TV シリーズの第 1 話に登場している。</summary>
    private static CharacterHome? ResolveFinalEpisodeBaton(
        BuildContext ctx, List<(int SeriesId, int? EpisodeId)> list, Series from, Episode fromEpisode)
    {
        if (!ctx.EpisodesBySeries.TryGetValue(from.SeriesId, out var fromEpisodes) || fromEpisodes.Count == 0) return null;
        if (fromEpisode.SeriesEpNo != fromEpisodes.Max(e => e.SeriesEpNo)) return null;

        // その次に始まる TV シリーズ。
        var home = ctx.SeriesById.Values
            .Where(s => string.Equals(s.KindCode, TvKindCode, StringComparison.Ordinal) && s.StartDate > from.StartDate)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .FirstOrDefault();
        if (home is null) return null;

        foreach (var a in list)
        {
            if (a.SeriesId != home.SeriesId || a.EpisodeId is not int episodeId) continue;
            if (ctx.LookupEpisode(home.SeriesId, episodeId) is { SeriesEpNo: 1 })
                return new CharacterHome(CharacterHomeKind.FinalEpisodeBaton, from.SeriesId, fromEpisode.EpisodeId, home.SeriesId, episodeId);
        }
        return null;
    }

    /// <summary>映画タイアップ：顔を出した回の放送の前後 <see cref="MovieTieInWindowDays"/> 日以内に公開された映画に登場している。</summary>
    private static CharacterHome? ResolveMovieTieIn(
        BuildContext ctx, List<(int SeriesId, int? EpisodeId)> list, Series from, Episode fromEpisode)
    {
        var airedOn = DateOnly.FromDateTime(fromEpisode.OnAirAt);
        // 登場した映画のうち、公開日が幅に入るもの。複数あれば公開の早いものを本来の作品とする。
        var movie = list
            .Where(a => a.SeriesId != from.SeriesId)
            .Select(a => ctx.SeriesById[a.SeriesId])
            .Where(s => string.Equals(s.KindCode, MovieKindCode, StringComparison.Ordinal)
                        && Math.Abs(s.StartDate.DayNumber - airedOn.DayNumber) <= MovieTieInWindowDays)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .FirstOrDefault();
        return movie is null
            ? null
            : new CharacterHome(CharacterHomeKind.MovieTieIn, from.SeriesId, fromEpisode.EpisodeId, movie.SeriesId, null);
    }
}

/// <summary>本来の作品が、声の出演でいちばん早い作品と違う形の種類。</summary>
public enum CharacterHomeKind
{
    /// <summary>最終話バトンタッチ（前の TV シリーズの最終話に顔を出した、次の TV シリーズのキャラクター）。</summary>
    FinalEpisodeBaton,
    /// <summary>映画タイアップ（映画の公開に合わせて TV シリーズに 1 話だけ顔を出した、映画のキャラクター）。</summary>
    MovieTieIn,
}

/// <summary>
/// 本来の作品が別にあるキャラクターの中身。<paramref name="FromSeriesId"/>・<paramref name="FromEpisodeId"/> は顔を出した TV シリーズとその回、
/// <paramref name="HomeSeriesId"/> は本来の作品、<paramref name="HomeEpisodeId"/> は本来の作品での置き場の話
/// （最終話バトンタッチは第 1 話。映画は話を持たないので null）。
/// </summary>
public sealed record CharacterHome(
    CharacterHomeKind Kind, int FromSeriesId, int FromEpisodeId, int HomeSeriesId, int? HomeEpisodeId);
