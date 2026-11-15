using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 人物・企業の OGP カード（プロフィール組み）で共通に使う部品。
/// 関与の一覧（<see cref="Involvement"/>）から、透かしに出す主な役職と、関わった作品の行を組み立てる。
/// 人物にも企業・団体にも同じ決め方を使うので、どちらのカードでも「何をしてきたか」が同じ読み方で伝わる。
/// </summary>
internal static class OgCareerCardParts
{
    /// <summary>
    /// 透かしに出す主な役職。TV のオープニングにクレジットされた役職（複数なら担当話数の多いもの）を最優先し、
    /// 無ければ映画のオープニングの役職、どちらも無ければ担当話数がいちばん多い役職。
    /// 本編の役職が無く声の出演だけなら「声の出演」。
    /// </summary>
    public static string ResolveMainRoleLabel(BuildContext ctx, IReadOnlyList<Involvement> involvements)
    {
        var byRole = involvements
            .Where(i => !i.IsVoiceCast && !string.IsNullOrEmpty(i.RoleCode))
            .GroupBy(i => i.RoleCode, StringComparer.Ordinal)
            .Select(g => new
            {
                Code = g.Key,
                TvOp = g.Count(i => string.Equals(i.CreditKind, "OP", StringComparison.Ordinal) && ctx.IsMainlineTvSeries(i.SeriesId)),
                MovieOp = g.Count(i => string.Equals(i.CreditKind, "OP", StringComparison.Ordinal) && ctx.IsMovieKindSeries(i.SeriesId)),
                Total = g.Count()
            })
            .ToList();

        var pick = byRole.Where(r => r.TvOp > 0).OrderByDescending(r => r.TvOp).ThenByDescending(r => r.Total).FirstOrDefault()
            ?? byRole.Where(r => r.MovieOp > 0).OrderByDescending(r => r.MovieOp).ThenByDescending(r => r.Total).FirstOrDefault()
            ?? byRole.OrderByDescending(r => r.Total).FirstOrDefault();
        if (pick is not null)
            return ctx.RoleByCode.TryGetValue(pick.Code, out var role) ? role.NameJa : pick.Code;
        return involvements.Any(i => i.IsVoiceCast) ? "声の出演" : "";
    }

    /// <summary>1 つの項目（役職・声の出演）に渡す作品の数の上限。これより多ければ、描画の側が末尾に「ほか」と添える。</summary>
    public const int GroupItemsMax = 12;

    /// <summary>作品に話を添える上限。その作品で担当した話がこれ以下のときだけ、作品名の後ろに話を書く。</summary>
    private const int EpisodeRangeMaxEpisodes = 3;

    /// <summary>
    /// カードの中段に並べる「何をしてきたか」。役職ごとに 1 項目で、見出しが役職、値がその役職で関わった作品の並び
    /// （「シリーズディレクター　『ふたりはプリキュア』『ふたりはプリキュアMax Heart』」）。声の出演も 1 項目にまとめ、
    /// 作品ごとに演じた役を添える（「声の出演　『ドキドキ！プリキュア』剣崎真琴役・キュアソード役、…」）。
    /// 作品ごとに役職を繰り返すより、同じ役職の作品が 1 項目に収まる分だけ多くの仕事が載る。
    /// <list type="bullet">
    ///   <item>項目の並び：メインスタッフかオープニングの役職（と声の出演）→ ほかの役職。同じ段の中は担当した話の多い順 → 役職の表示順。</item>
    ///   <item>項目の中の作品の並び：主に担当した TV の作品（作品全体のクレジットか、半分以上の話）を放送開始の早い順 →
    ///     それ以外（一部の話だけの TV の作品と映画）を担当した話の多い順（映画は 1 と数える。同じなら早い順）。
    ///     TV で <see cref="EpisodeRangeMaxEpisodes"/> 話以下の担当は話も添える（「『Yes！プリキュア5』#43」）。</item>
    ///   <item>入りきらない作品・項目は描画の側が後ろから落とす（作品は「ほか」と添える）。</item>
    /// </list>
    /// 担当した話数・本数は出さない。役職を足した延べの数では仕事の大きさが読めず、クレジットの入力が進むたびに変わるため。
    /// </summary>
    /// <param name="excludeSongRoles">
    /// 主題歌・挿入歌の役職（歌い手がオープニングに載るもの）を項目にしない。曲の項目を別に並べる人物のカードで、同じ仕事が二重に出ないようにする。
    /// </param>
    public static IReadOnlyList<OgCardFactLine> BuildCareerLines(BuildContext ctx, IReadOnlyList<Involvement> involvements, bool excludeSongRoles = false)
    {
        var groups = new List<(int Tier, int Count, int Order, OgCardFactLine Line)>();

        var staff = involvements
            .Where(i => !i.IsVoiceCast && !string.IsNullOrEmpty(i.RoleCode) && !(excludeSongRoles && IsSongRole(i.RoleCode)))
            .GroupBy(i => i.RoleCode, StringComparer.Ordinal);
        foreach (var roleGroup in staff)
        {
            var works = new List<(int Rank, int Count, DateOnly Start, string Text)>();
            int tier = 1;
            foreach (var g in roleGroup.GroupBy(i => i.SeriesId))
            {
                if (!ctx.SeriesById.TryGetValue(g.Key, out var series)) continue;
                if (RoleWeight(ctx, g.Key, roleGroup.Key, g) < OtherRoleWeightBase) tier = 0;
                var (rank, count, range) = EpisodeSpan(ctx, g.Key, g);
                works.Add((rank, count, series.StartDate, $"『{series.Title}』{range}"));
            }
            if (works.Count == 0) continue;
            var role = ctx.RoleByCode.TryGetValue(roleGroup.Key, out var r) ? r : null;
            groups.Add((tier, works.Sum(w => w.Count), role?.DisplayOrder ?? ushort.MaxValue,
                new OgCardFactLine(role?.NameJa ?? roleGroup.Key, "")
                {
                    LabelColorHex = OgRolePalette.ColorFor(roleGroup.Key),
                    Items = OrderedItems(works),
                    ItemsHaveMore = works.Count > GroupItemsMax
                }));
        }

        var voiceWorks = new List<(int Rank, int Count, DateOnly Start, string Text)>();
        foreach (var g in involvements.Where(i => i.IsVoiceCast).GroupBy(i => i.SeriesId))
        {
            if (!ctx.SeriesById.TryGetValue(g.Key, out var series)) continue;
            var (rank, count, _) = EpisodeSpan(ctx, g.Key, g);
            voiceWorks.Add((rank, count, series.StartDate, $"『{series.Title}』{VoiceRolesSuffix(ctx, g).TrimStart()}"));
        }
        if (voiceWorks.Count > 0)
        {
            groups.Add((0, voiceWorks.Sum(w => w.Count), 0,
                new OgCardFactLine("声の出演", "")
                {
                    LabelColorHex = OgCardColors.VoiceActor,
                    Items = OrderedItems(voiceWorks),
                    ItemsHaveMore = voiceWorks.Count > GroupItemsMax,
                    // 役名で終わる項目が続くので、作品の切れ目に読点を挟む。
                    ItemsSeparator = "、"
                }));
        }

        return groups
            .OrderBy(x => x.Tier).ThenByDescending(x => x.Count).ThenBy(x => x.Order)
            .Select(x => x.Line)
            .ToList();

        // 主に担当した作品（Rank 0）は放送開始の早い順。それ以外は話の多い順（同じなら早い順）。
        static IReadOnlyList<string> OrderedItems(List<(int Rank, int Count, DateOnly Start, string Text)> works) =>
            works.OrderBy(w => w.Rank).ThenByDescending(w => w.Rank == 0 ? 0 : w.Count).ThenBy(w => w.Start)
                .Take(GroupItemsMax).Select(w => w.Text).ToList();
    }

    /// <summary>
    /// 1 作品ぶんの関与から、並べる段（0：主に担当した TV の作品〔作品全体のクレジットか、半分以上の話〕、1：それ以外）、
    /// 担当した量（TV は話の数、映画・作品全体のクレジットは 1）、作品名の後ろに添える話（「 #3, 15」）を返す。
    /// 話を添えるのは、TV で <see cref="EpisodeRangeMaxEpisodes"/> 話以下を担当したときだけ（全話・作品全体のクレジット・映画には添えない）。
    /// </summary>
    private static (int Rank, int Count, string Range) EpisodeSpan(BuildContext ctx, int seriesId, IEnumerable<Involvement> seriesInvolvements)
    {
        if (ctx.IsMovieKindSeries(seriesId)) return (1, 1, "");
        var episodeNos = new HashSet<int>();
        bool hasSeriesScope = false;
        foreach (var inv in seriesInvolvements)
        {
            if (inv.EpisodeId is int episodeId)
            {
                var ep = ctx.LookupEpisode(seriesId, episodeId);
                if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
            }
            else
            {
                hasSeriesScope = true;
            }
        }
        if (episodeNos.Count == 0) return (0, 1, "");
        int total = ctx.EpisodesBySeries.TryGetValue(seriesId, out var all) ? all.Count : 0;
        bool major = hasSeriesScope || (total > 0 && episodeNos.Count * 2 >= total);
        bool showRange = !hasSeriesScope && episodeNos.Count <= EpisodeRangeMaxEpisodes && episodeNos.Count != total;
        return (major ? 0 : 1, episodeNos.Count, showRange ? " " + EpisodeRangeCompressor.Compress(episodeNos) : "");
    }

    /// <summary>主題歌・挿入歌の役職（歌い手がオープニングに載るもの）か。</summary>
    private static bool IsSongRole(string roleCode) =>
        roleCode.StartsWith("THEME_SONG", StringComparison.Ordinal) || roleCode.StartsWith("INSERT_SONG", StringComparison.Ordinal);

    /// <summary>
    /// 主な役職（その作品のメインスタッフに入る役職か、オープニングにクレジットされた役職）を 1 つでも持つか。
    /// 声の出演と、主題歌・挿入歌の役職（歌い手がオープニングに載るもの）は数えない。
    /// 人物のカードで、本人の名義で歌った人を歌手として見せるか、スタッフとして見せるかを決めるのに使う。
    /// </summary>
    public static bool HasPrincipalStaffRole(BuildContext ctx, IReadOnlyList<Involvement> involvements)
    {
        return involvements
            .Where(i => !i.IsVoiceCast && !string.IsNullOrEmpty(i.RoleCode) && !IsSongRole(i.RoleCode))
            .GroupBy(i => (i.SeriesId, i.RoleCode))
            .Any(g => RoleWeight(ctx, g.Key.SeriesId, g.Key.RoleCode, g) < OtherRoleWeightBase);
    }

    /// <summary>メインスタッフにもオープニングにも入らない役職の重さの起点（<see cref="RoleWeight"/>）。</summary>
    private const int OtherRoleWeightBase = 200000;

    /// <summary>
    /// 1 作品での役職の重さ（小さいほど重い）。その作品のメインスタッフに入る役職はメインスタッフの並び順（0〜）、
    /// オープニングにクレジットされた役職は 100000 + 役職の表示順、それ以外は 200000 + 役職の表示順。
    /// 役職の表示順はクレジットの並びで、映画の監督のように主な役職が後ろに来ることがあるため、メインスタッフの表を先に見る。
    /// </summary>
    private static int RoleWeight(BuildContext ctx, int seriesId, string roleCode, IEnumerable<Involvement> roleInvolvements)
    {
        if (ctx.KeyStaffRolesBySeries.TryGetValue(seriesId, out var keyRoles))
        {
            var key = keyRoles.FirstOrDefault(k => string.Equals(k.RoleCode, roleCode, StringComparison.Ordinal));
            if (key is not null) return key.DisplayOrder;
        }
        int order = ctx.RoleByCode.TryGetValue(roleCode, out var role) && role.DisplayOrder is ushort d ? d : ushort.MaxValue;
        bool op = roleInvolvements.Any(i => string.Equals(i.CreditKind, "OP", StringComparison.Ordinal));
        return (op ? 100000 : OtherRoleWeightBase) + order;
    }

    /// <summary>声の出演の作品に添える役。1 作品ぶんの関与のうち声の出演（CHARACTER_VOICE）の役名を、出た話の多い順に 2 つまで。</summary>
    private const int VoiceRolesMax = 2;

    /// <summary>
    /// 1 作品ぶんの関与から、声の出演の役を「 〇〇役」「 〇〇役・△△役」「 〇〇役・△△役ほか」の形で返す。
    /// 役名はクレジットされたキャラクターの名義（苗字の無い名義はフルネームの名義に寄せる）。声の出演が無ければ空文字。
    /// </summary>
    private static string VoiceRolesSuffix(BuildContext ctx, IEnumerable<Involvement> seriesInvolvements)
    {
        var names = seriesInvolvements
            .Where(i => i.IsVoiceCast && i.CharacterAliasId is int)
            .Select(i => ctx.CharacterAliasById.TryGetValue(i.CharacterAliasId!.Value, out var ca) ? ctx.CharacterAliasNames.DisplayName(ca) : "")
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.Ordinal)
            .OrderByDescending(x => x.Count())
            .Select(x => x.Key)
            .ToList();
        if (names.Count == 0) return "";
        string joined = string.Join("・", names.Take(VoiceRolesMax).Select(n => n + "役"));
        return " " + joined + (names.Count > VoiceRolesMax ? "ほか" : "");
    }
}
