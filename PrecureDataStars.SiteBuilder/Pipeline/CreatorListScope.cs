using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クリエイターの一覧（役職詳細・スタッフ一覧・声の出演一覧）に載せるクレジットの範囲。
/// レギュラーの TV シリーズ・映画（秋映画・春映画・併映の短編）・オトナシリーズ（大人向けスピンオフ）のクレジットを集計し、
/// 単発のイベント映像と、オトナシリーズ以外のスピンオフのクレジットは入れない（作品詳細と人物・企業/団体・キャラクターの詳細には出る）。
/// 一覧のページでは、TV・映画・オトナシリーズのどれを見せるかを閲覧者がボタンで切り替える。
/// 歌唱・音楽制作は楽曲・音楽クレジットを直接集計する作品横断の枠組みなので、この範囲の対象外。
/// </summary>
public static class CreatorListScope
{
    /// <summary>一覧に載せるシリーズの種類（series.kind_code）。EVENT と SPIN-OFF / SHORT は含めない。</summary>
    public static readonly IReadOnlySet<string> SeriesKinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "TV", "MOVIE", "SPRING", "MOVIE_SHORT", "OTONA",
    };

    /// <summary>指定シリーズのクレジットを一覧の集計に入れるか。未登録のシリーズは入れない。</summary>
    public static bool Includes(BuildContext ctx, int seriesId)
        => ctx.SeriesById.TryGetValue(seriesId, out var s) && SeriesKinds.Contains(s.KindCode);

    /// <summary>
    /// 役職詳細ページ（/creators/roles/{code}/）を持たない役職（系譜の代表の role_code）を求める。役職マスタのすべての役職のうち、
    /// ページを持つ役職（下の 3 つのどれか）に当たらないものを返す。これらの役職名はクレジットや人物ページでリンクにしない。
    /// <list type="number">
    ///   <item><description>一覧の範囲（<see cref="SeriesKinds"/>）のクレジットに、人物（人物の行を持つ名義）・企業/団体・ロゴの名義で出てくる役職。
    ///     役職詳細の行は人物と企業/団体だけから作るので、文字だけ（「制作・著作」の著作権者の表記など）・曲だけ・ユニットの名義だけの役職は
    ///     ページを持たない。単発のイベント映像と、オトナシリーズ以外のスピンオフだけで使う役職も持たない。</description></item>
    ///   <item><description>音楽クレジットに、人物（人物の行を持つ名義）・企業/団体の名義で出てくる役職（音楽の役職詳細を持つ）。</description></item>
    ///   <item><description>楽曲の作家の役職（作詞・作曲・編曲。楽曲のクレジットから集計した役職詳細を持つ）。</description></item>
    /// </list>
    /// </summary>
    public static IReadOnlySet<string> RolesWithoutPage(BuildContext ctx, CreditInvolvementIndex index, RoleSuccessorResolver resolver)
    {
        string Rep(string code)
        {
            string rep = resolver.GetRepresentative(code);
            return string.IsNullOrEmpty(rep) ? code : rep;
        }

        // 人物の行を持つ名義（ユニットなど人物の無い名義の関与は、役職詳細の行にならないので数えない）。
        var personAliasIds = ctx.AliasIdsByPerson.Values.SelectMany(ids => ids).ToHashSet();
        var scoped = index.RestrictToSeries(sid => Includes(ctx, sid));
        var withPage = scoped.ByPersonAlias.Where(kv => personAliasIds.Contains(kv.Key)).SelectMany(kv => kv.Value)
            .Concat(scoped.ByCompanyAlias.Values.SelectMany(list => list))
            .Concat(scoped.ByLogo.Values.SelectMany(list => list))
            .Where(inv => !string.IsNullOrEmpty(inv.RoleCode))
            .Select(inv => Rep(inv.RoleCode))
            .ToHashSet(StringComparer.Ordinal);
        // 音楽クレジットの役職は音楽の役職詳細を持つので、ページありとして扱う。音楽の役職詳細の行も人物と企業/団体だけから作るので、
        // 人物（人物の行を持つ名義）か企業/団体の名義の行がある役職だけを数える（ユニットの名義だけの役職はページを持たない）。
        foreach (var mc in ctx.MusicCredits.BySong.Values.Concat(ctx.MusicCredits.ByRecording.Values)
                     .Concat(ctx.MusicCredits.BySession.Values).Concat(ctx.MusicCredits.ByProduct.Values).SelectMany(list => list))
        {
            bool byEntity = (mc.PersonAliasId is int pa && personAliasIds.Contains(pa))
                || (mc.CompanyAliasId is int ca && ctx.CompanyAliasById.TryGetValue(ca, out var alias) && alias.CompanyId > 0);
            if (byEntity) withPage.Add(Rep(mc.RoleCode));
        }
        // 楽曲の作家の役職は、楽曲のクレジットから集計した役職詳細を持つ。
        foreach (var code in new[] { SongCreditRoles.Lyrics, SongCreditRoles.Composition, SongCreditRoles.Arrangement, SongCreditRoles.MedleyArrangement })
            withPage.Add(Rep(code));

        return ctx.RoleByCode.Keys.Select(Rep).Where(rep => !withPage.Contains(rep)).ToHashSet(StringComparer.Ordinal);
    }

}
