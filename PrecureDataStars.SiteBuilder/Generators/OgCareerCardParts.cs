using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 人物・企業の OGP カード（プロフィール組み）で共通に使う部品。
/// 関与の一覧（<see cref="Involvement"/>）から、透かしに出す主な役職と、関わった期間の年表を組み立てる。
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

    /// <summary>
    /// 関わった期間の年表。作品ごとに、クレジットされた最初の話から最後の話までを 1 区間にし、
    /// その作品でいちばん多い役職の色（役職バッジと同じ）で塗る。映画は公開日の点。
    /// </summary>
    public static IReadOnlyList<OgCardTimelineSegment> BuildCareerTimeline(BuildContext ctx, IReadOnlyList<Involvement> involvements, string fallbackColor)
    {
        var segments = new List<OgCardTimelineSegment>();
        foreach (var g in involvements.GroupBy(i => i.SeriesId))
        {
            if (!ctx.SeriesById.TryGetValue(g.Key, out var series)) continue;
            string color = DominantRoleColor(g, fallbackColor);

            if (ctx.IsMovieKindSeries(g.Key))
            {
                segments.Add(new OgCardTimelineSegment(series.StartDate, series.StartDate, color));
                continue;
            }

            DateTime? first = null, last = null;
            foreach (var inv in g)
            {
                if (inv.EpisodeId is not int episodeId) continue;
                var ep = ctx.LookupEpisode(g.Key, episodeId);
                if (ep is null) continue;
                if (first is null || ep.OnAirAt < first) first = ep.OnAirAt;
                if (last is null || ep.OnAirAt > last) last = ep.OnAirAt;
            }
            if (first is null || last is null) continue;
            // 終わりは最後の話の放送週いっぱいまで伸ばす（1 話だけでも点ではなく短い帯になる）。
            segments.Add(new OgCardTimelineSegment(DateOnly.FromDateTime(first.Value), DateOnly.FromDateTime(last.Value).AddDays(7), color));
        }
        return segments.OrderBy(s => s.Start).ToList();
    }

    /// <summary>
    /// サイトの年表（役職詳細・声の出演一覧の線表）に載る人物・団体か。載らない人にカードの年表を出しても、
    /// 単発の点が 1 つ置かれるだけで何を表すのか伝わらないため、年表を出すかどうかをこれで決める。
    /// 本編の役職は役職詳細と同じ決まり（系譜でまとめた役職ごとに、オープニングに出たか・1 年間に 4 回以上か）、
    /// 声の出演は声の出演一覧と同じ決まり（1 年間に 4 回以上）。
    /// 年表は候補が少ないページでは全員を載せるので、CreatorsGenerator が記録した「年表に載った行」
    /// （<see cref="BuildContext.SiteTimelineEntityUrls"/>）があればそれで判定し、無いときだけ決まりで判定する。
    /// </summary>
    public static bool AppearsInSiteTimeline(BuildContext ctx, RoleSuccessorResolver resolver, IReadOnlyList<Involvement> involvements, string entityUrl)
    {
        if (ctx.SiteTimelineEntityUrls is { } recorded) return recorded.Contains(entityUrl);

        var byRole = involvements
            .Where(i => !i.IsVoiceCast && !string.IsNullOrEmpty(i.RoleCode))
            .GroupBy(i => resolver.GetRepresentative(i.RoleCode), StringComparer.Ordinal);
        foreach (var g in byRole)
        {
            bool hasOpening = g.Any(i => string.Equals(i.CreditKind, "OP", StringComparison.Ordinal));
            if (RoleTimelineBuilder.Qualifies(ParticipationDates(ctx, g), hasOpening, RoleTimelineRules.Staff)) return true;
        }

        var voice = involvements.Where(i => i.IsVoiceCast).ToList();
        return voice.Count > 0 && RoleTimelineBuilder.Qualifies(ParticipationDates(ctx, voice), false, RoleTimelineRules.VoiceCast);
    }

    /// <summary>カードに並べる作品行の上限。これを超える分は「ほか n 作品」の 1 行にまとめる。</summary>
    private const int WorksMaxLines = 4;

    /// <summary>
    /// 年表を出さないカードの中段に並べる「関わった作品」。作品ごとに 1 行で、値は「『作品名』 範囲」
    /// （TV は話数の範囲、全話なら「全話」、シリーズ全体のクレジットは「シリーズ全体」、映画は作品名だけ）。
    /// 作品が <see cref="WorksMaxLines"/> より多ければ、参加の多い作品（TV は話数、映画は 1 本）から上限まで選び、
    /// 残りを「ほか n 作品」にまとめる。並びは選んだ作品を放送開始・公開の早い順に。
    /// 年表が意味を持たないほど参加の少ない人物・団体でも、どの作品のどの話に関わったかは一目で伝わる。
    /// </summary>
    public static IReadOnlyList<OgCardFactLine> BuildWorksLines(BuildContext ctx, IReadOnlyList<Involvement> involvements)
    {
        var works = new List<(DateOnly Start, int Count, string Text)>();
        foreach (var g in involvements.GroupBy(i => i.SeriesId))
        {
            if (!ctx.SeriesById.TryGetValue(g.Key, out var series)) continue;
            if (ctx.IsMovieKindSeries(g.Key))
            {
                works.Add((series.StartDate, 1, $"『{series.Title}』"));
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
            works.Add((series.StartDate, count, $"『{series.Title}』" + (range.Length > 0 ? " " + range : "")));
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

    /// <summary>参加の日付（TV の話は放送日、映画は公開日）。同じ話・同じ映画は 1 件に畳む。</summary>
    private static IEnumerable<DateOnly> ParticipationDates(BuildContext ctx, IEnumerable<Involvement> involvements)
    {
        var seen = new HashSet<(int SeriesId, int EpisodeId)>();
        foreach (var inv in involvements)
        {
            if (ctx.IsMovieKindSeries(inv.SeriesId))
            {
                if (seen.Add((inv.SeriesId, 0)) && ctx.SeriesById.TryGetValue(inv.SeriesId, out var series))
                    yield return series.StartDate;
                continue;
            }
            if (inv.EpisodeId is not int episodeId || !seen.Add((inv.SeriesId, episodeId))) continue;
            var ep = ctx.LookupEpisode(inv.SeriesId, episodeId);
            if (ep is not null) yield return ep.OnAirDate;
        }
    }

    /// <summary>作品の中でいちばん多い役職の色。役職バッジに色の無い役職や声の出演だけの作品は、声優なら緑、それ以外は色帯の色。</summary>
    public static string DominantRoleColor(IEnumerable<Involvement> involvements, string fallbackColor)
    {
        var top = involvements
            .Where(i => !i.IsVoiceCast && !string.IsNullOrEmpty(i.RoleCode))
            .GroupBy(i => i.RoleCode, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (top is null) return involvements.Any(i => i.IsVoiceCast) ? OgCardColors.VoiceActor : fallbackColor;
        string color = OgRolePalette.ColorFor(top.Key);
        return string.IsNullOrEmpty(color) ? fallbackColor : color;
    }
}
