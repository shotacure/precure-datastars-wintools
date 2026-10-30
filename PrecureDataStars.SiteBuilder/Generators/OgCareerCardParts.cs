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
                TvOp = g.Count(i => string.Equals(i.CreditKind, "OP", StringComparison.Ordinal) && !ctx.IsMovieKindSeries(i.SeriesId)),
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

    /// <summary>カードに並べる作品行の上限。これを超える分は「ほか n 作品」の 1 行にまとめる。</summary>
    private const int WorksMaxLines = 4;

    /// <summary>
    /// 年表を出さないカードの中段に並べる「関わった作品」。作品ごとに 1 行で、値は「『作品名』 範囲」
    /// （TV は話数の範囲、全話なら「全話」、シリーズ全体のクレジットは「シリーズ全体」、映画は作品名だけ）。
    /// 作品が <see cref="WorksMaxLines"/> より多ければ、参加の多い作品（TV は話数、映画は 1 本）から上限まで選び、
    /// 残りを「ほか n 作品」にまとめる。並びは選んだ作品を放送開始・公開の早い順に。
    /// 年表が意味を持たないほど参加の少ない人物・団体でも、どの作品のどの話に関わったかは一目で伝わる。
    /// 声の出演は、その作品で演じた役を話数の多い順に 2 つまで「〇〇役」として添える（話数だけ並べても誰を演じたかが伝わらないため）。
    /// </summary>
    public static IReadOnlyList<OgCardFactLine> BuildWorksLines(BuildContext ctx, IReadOnlyList<Involvement> involvements)
    {
        var works = new List<(DateOnly Start, int Count, string Text)>();
        foreach (var g in involvements.GroupBy(i => i.SeriesId))
        {
            if (!ctx.SeriesById.TryGetValue(g.Key, out var series)) continue;
            string roles = VoiceRolesSuffix(ctx, g);
            if (ctx.IsMovieKindSeries(g.Key))
            {
                works.Add((series.StartDate, 1, $"『{series.Title}』{roles}"));
                continue;
            }

            var episodeNos = new HashSet<int>();
            bool hasSeriesScope = false;
            foreach (var inv in g)
            {
                if (inv.EpisodeId is int episodeId)
                {
                    var ep = ctx.LookupEpisode(g.Key, episodeId);
                    if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
                }
                else
                {
                    hasSeriesScope = true;
                }
            }
            var allEpNos = ctx.EpisodesBySeries.TryGetValue(g.Key, out var allEps) ? allEps.Select(e => e.SeriesEpNo).ToList() : new List<int>();
            bool isAll = allEpNos.Count > 0 && episodeNos.SetEquals(allEpNos);
            string range = episodeNos.Count == 0
                ? (hasSeriesScope ? "シリーズ全体" : "")
                : isAll ? "全話" : EpisodeRangeCompressor.Compress(episodeNos);
            int count = episodeNos.Count > 0 ? episodeNos.Count : 1;
            works.Add((series.StartDate, count, $"『{series.Title}』" + (range.Length > 0 ? " " + range : "") + roles));
        }

        // 上限を超えるときだけ参加の多い順に絞り、選んだ作品は時系列に戻す。
        var shown = works.Count > WorksMaxLines
            ? works.OrderByDescending(w => w.Count).ThenBy(w => w.Start).Take(WorksMaxLines).ToList()
            : works;
        var lines = shown.OrderBy(w => w.Start).Select(w => new OgCardFactLine("", w.Text)).ToList();
        if (works.Count > shown.Count)
            lines.Add(new OgCardFactLine("", $"ほか{works.Count - shown.Count}作品"));
        return lines;
    }

    /// <summary>作品行に添える役。1 作品ぶんの関与のうち声の出演（CHARACTER_VOICE）の役名を、出た話の多い順に 2 つまで。</summary>
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
