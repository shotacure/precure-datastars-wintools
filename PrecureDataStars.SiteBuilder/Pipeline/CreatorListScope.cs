using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クリエイターの一覧（役職詳細・スタッフ一覧・声の出演一覧）に載せるクレジットの範囲。
/// レギュラーの TV シリーズと映画（秋映画・春映画・併映の短編）のクレジットだけを集計し、
/// 単発のイベント映像とスピンオフのクレジットは入れない（作品詳細と人物・企業/団体・キャラクターの詳細には出る）。
/// 歌唱・音楽制作は楽曲・音楽クレジットを直接集計する作品横断の枠組みなので、この範囲の対象外。
/// </summary>
public static class CreatorListScope
{
    /// <summary>一覧に載せるシリーズの種類（series.kind_code）。EVENT と SPIN-OFF / OTONA / SHORT は含めない。</summary>
    public static readonly IReadOnlySet<string> SeriesKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "TV", "MOVIE", "SPRING", "MOVIE_SHORT",
    };

    /// <summary>指定シリーズのクレジットを一覧の集計に入れるか。未登録のシリーズは入れない。</summary>
    public static bool Includes(BuildContext ctx, int seriesId)
        => ctx.SeriesById.TryGetValue(seriesId, out var s) && SeriesKinds.Contains(s.KindCode);

    /// <summary>
    /// 役職詳細ページ（/creators/roles/{code}/）を持たない役職（系譜の代表の role_code）を求める。
    /// クレジットには出てくるが、一覧の範囲（<see cref="SeriesKinds"/>）のクレジットにも音楽クレジットにも出てこない役職が当たる
    /// （単発のイベント映像・スピンオフだけで使う役職）。これらの役職名はクレジットや人物ページでリンクにしない。
    /// </summary>
    public static IReadOnlySet<string> RolesWithoutPage(BuildContext ctx, CreditInvolvementIndex index, RoleSuccessorResolver resolver)
    {
        static IEnumerable<string> RoleCodes(CreditInvolvementIndex idx)
            => idx.ByPersonAlias.Values.Concat(idx.ByCompanyAlias.Values).Concat(idx.ByLogo.Values)
                .SelectMany(list => list).Select(inv => inv.RoleCode);

        string Rep(string code)
        {
            string rep = resolver.GetRepresentative(code);
            return string.IsNullOrEmpty(rep) ? code : rep;
        }

        var scoped = index.RestrictToSeries(sid => Includes(ctx, sid));
        var withPage = RoleCodes(scoped).Select(Rep).ToHashSet(StringComparer.Ordinal);
        // 音楽クレジットの役職は音楽の役職詳細を持つので、ページありとして扱う。
        foreach (var mc in ctx.MusicCredits.BySong.Values.Concat(ctx.MusicCredits.ByRecording.Values)
                     .Concat(ctx.MusicCredits.BySession.Values).Concat(ctx.MusicCredits.ByProduct.Values).SelectMany(list => list))
        {
            withPage.Add(Rep(mc.RoleCode));
        }

        return RoleCodes(index).Select(Rep).Where(rep => !withPage.Contains(rep)).ToHashSet(StringComparer.Ordinal);
    }
}
