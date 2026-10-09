using System.Globalization;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 「年表」タブの線表（担当・出演・参加の移り変わり）を組み立てる。役職詳細・声の出演一覧・歌唱一覧・
/// 作詞作曲編曲や音楽の役職詳細で共通に使う。
/// <list type="bullet">
///   <item><description>横軸は時期。最初の TV シリーズの放送開始から、クレジットを収録した最新の TV の話・映画まで
///     （歌唱一覧のように、それより後の歌まで描くページでは最後の歌まで伸ばす）。TV シリーズ 1 作を 1 本の帯にし、
///     帯の左端に放送開始年の目盛りを置く。</description></item>
///   <item><description>行は人物・企業/団体・キャラクター。参加（TV の 1 話・映画 1 本・歌 1 曲・劇伴の録音回 1 回・盤 1 点）を日付順に並べ、
///     隣り合う参加の差が <see cref="RoleTimelineRules.MaxGapDays"/> 日以内ならつないで「続けて参加した期間」とする。
///     載せる行はページごとの決まり（<see cref="RoleTimelineRules"/>）で決める。「1 年間に 4 回」は、どこでもよい
///     連続する <see cref="RoleTimelineRules.WindowDays"/> 日（52 週）のあいだに参加（TV の話数・映画の本数）が 4 件以上あること
///     （シリーズや暦年の区切りによらず、期間をずらして数える）。
///     <list type="number">
///       <item><description>役職詳細：役職を問わずオープニングのクレジット（TV・映画とも）に出たことがある（メインスタッフ）か、
///         その役職を 1 年間に 4 回以上担当したことがある。続けて担当した期間は 13 週以内の間隔でつなぐ。</description></item>
///       <item><description>声の出演：1 年間に 4 回以上出演したことがある。続けて出演した期間は 4 週以内の間隔でつなぐ。</description></item>
///       <item><description>作詞・作曲・編曲の役職詳細：1 年間に 2 曲以上担当したことがある。</description></item>
///       <item><description>歌唱・音楽の役職詳細：ページに載る行をすべて載せる。</description></item>
///     </list>
///     描ける参加のある候補はすべて行にする。役職詳細・声の出演・作詞作曲編曲で候補が <see cref="RoleTimelineRules.ShowAllUpTo"/>（10）を
///     超えるページは、決まりを満たす行を「主な方」とし、それが 10 に満たなければ残りから参加の多い順（同じ数なら最初の参加の早い順 →
///     その話のクレジットで先に出ている順）に 10 まで補う。初めは主な方だけを見せ、「主な方のみ」のスイッチを切るとすべての行を
///     本来の並びの位置に見せる（role-timeline.js）。候補が 10 以下のページはスイッチを出さず全員を見せる。
///     行には単発の参加も含めてすべての参加を描く。</description></item>
///   <item><description>並びは、最初の参加の日付の早い順。同じ日に始めた行は、団体を先に置き、その中で最後の参加の
///     日付の早い順（最初に抜けた順）、それも同じなら呼び出し側が渡す並びのキー（役職詳細・声の出演はその話のクレジットで
///     上に出ている順）→ 名前。</description></item>
///   <item><description>描くものは、続けて参加した期間（2 件以上つながったものの細線）・TV の話（同じシリーズで話数が
///     続く間はひと続きの帯。1 話は放送日から <see cref="EpisodeSpanDays"/> 日の幅）・映画（公開日の点）・
///     歌・劇伴（初めて収められた盤の発売日の点。日付の決め方は <see cref="MusicTimelineDates"/>）・盤（発売日の四角）。</description></item>
///   <item><description>役職詳細では、ページの役職と関連（段階・並列）でつながった役職の担当（<see cref="RoleTimelineEntity.RelatedRoles"/>）も、
///     主の帯の下に細い帯（映画は白抜きの輪）で重ねる。色は役職ごとで、関連の種類（前段階・並列・後段階）の色相を基準に、
///     同じ種類の役職どうしは色相を回して分ける（<see cref="AssignRelatedColors"/>）。主の帯と同じ回は隠れ、主の役職の無い回だけが
///     見える。凡例は色と役職名（役職詳細へのリンク）だけを出す。関連する役職の担当は、行の並び・主な方の判定・続けて参加した期間には使わない。</description></item>
/// </list>
/// 軸は同じ期間のページで共通なので、インスタンスを 1 つ作ってページ（役職）ごとに <see cref="Build"/> を呼ぶ。
/// </summary>
internal sealed class RoleTimelineBuilder
{
    /// <summary>TV の 1 話が線表上で占める幅（日）。週 1 回の放送枠ぶん。</summary>
    private const int EpisodeSpanDays = 7;

    /// <summary>
    /// 年の目盛りの文字を、5 の倍数の年以外も出せる帯の数の上限。
    /// これを超えると狭い画面（768px 以下）で 5 の倍数の年だけに、<see cref="AllTickBandsMaxWide"/> を超えると常に 5 の倍数の年だけにする。
    /// </summary>
    private const int AllTickBandsMaxNarrow = 10;

    /// <summary>広い画面でも年の目盛りの文字を全部出せる帯の数の上限。</summary>
    private const int AllTickBandsMaxWide = 22;

    private readonly BuildContext _ctx;
    private readonly DateOnly _axisStart;
    private readonly int _axisDays;
    private readonly IReadOnlyList<RoleTimelineBand> _bands;
    private readonly IReadOnlyList<RoleTimelineTick> _ticks;
    private readonly string _tickDensityClass;

    /// <summary>
    /// 軸（期間・シリーズの帯・年の目盛り）を確定させる。収録しているクレジットが無ければ軸を持たず、<see cref="Build"/> は常に null を返す。
    /// </summary>
    /// <param name="ctx">ビルド全体の共有データ。</param>
    /// <param name="extendTo">軸の終わりをこの日まで伸ばす（歌唱一覧で、クレジットの収録範囲より後の歌まで描くとき）。null なら伸ばさない。</param>
    public RoleTimelineBuilder(BuildContext ctx, DateOnly? extendTo = null)
    {
        _ctx = ctx;
        _bands = Array.Empty<RoleTimelineBand>();
        _ticks = Array.Empty<RoleTimelineTick>();
        _tickDensityClass = "";

        if (ctx.CreditCoverageEpisode is not { } coverage) return;

        // 軸の終わり：クレジットを収録した最新の TV の話の放送枠の終わり。それより後に公開された映画の
        // クレジットを収録していれば、その公開日まで伸ばす。
        var axisEnd = coverage.Episode.OnAirDate.AddDays(EpisodeSpanDays);
        foreach (var sid in ctx.CreditsBySeries.Keys)
        {
            if (!ctx.IsMovieKindSeries(sid) || !ctx.SeriesById.TryGetValue(sid, out var mv)) continue;
            var mvEnd = mv.StartDate.AddDays(1);
            if (mvEnd > axisEnd) axisEnd = mvEnd;
        }
        if (extendTo is DateOnly ext && ext > axisEnd) axisEnd = ext;

        var tvSeries = ctx.SeriesById.Values
            .Where(s => string.Equals(s.KindCode, "TV", StringComparison.Ordinal) && s.StartDate < axisEnd)
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .ToList();
        if (tvSeries.Count == 0) return;

        _axisStart = tvSeries[0].StartDate;
        _axisDays = axisEnd.DayNumber - _axisStart.DayNumber;
        if (_axisDays <= 0) return;

        var bands = new List<RoleTimelineBand>(tvSeries.Count);
        var ticks = new List<RoleTimelineTick>(tvSeries.Count);
        bool manyBands = tvSeries.Count > AllTickBandsMaxNarrow;
        for (int i = 0; i < tvSeries.Count; i++)
        {
            var s = tvSeries[i];
            var bandEnd = i + 1 < tvSeries.Count ? tvSeries[i + 1].StartDate : axisEnd;
            if (bandEnd > axisEnd) bandEnd = axisEnd;
            bands.Add(new RoleTimelineBand
            {
                Left = Pct(s.StartDate),
                Width = PctWidth(s.StartDate, bandEnd),
                IsAlt = i % 2 == 1
            });
            // 軸の右端で幅の足りない帯（収録が始まったばかりのシリーズ）は、目盛りの文字がはみ出すので出さない。
            bool showLabel = (bandEnd.DayNumber - s.StartDate.DayNumber) >= 120;
            ticks.Add(new RoleTimelineTick
            {
                Left = Pct(s.StartDate),
                YearLabel = s.StartDate.Year.ToString(CultureInfo.InvariantCulture),
                SeriesTitle = s.Title,
                SeriesUrl = PathUtil.SeriesUrl(s.Slug),
                ShowLabel = showLabel,
                IsMinor = manyBands && s.StartDate.Year % 5 != 0
            });
        }
        _bands = bands;
        _ticks = ticks;
        _tickDensityClass = tvSeries.Count > AllTickBandsMaxWide ? "rtl-ticks-sparse"
            : manyBands ? "rtl-ticks-medium"
            : "";
    }

    /// <summary>
    /// 1 ページ（役職）分の線表を組み立てる。載せる行が 1 つも無ければ null（テンプレは年表タブを出さない）。
    /// </summary>
    /// <param name="entities">線表に載せる候補（人物・企業/団体・キャラクター）。</param>
    /// <param name="rules">続けて参加したとみなす間隔・載せる決まり・凡例の言い回し。</param>
    public RoleTimelineModel? Build(IEnumerable<RoleTimelineEntity> entities, RoleTimelineRules rules)
    {
        if (_axisDays <= 0) return null;

        // 描ける参加のある候補はすべて行にする。その数が ShowAllUpTo を超えるページでは、決まりを満たす行と、
        // それが ShowAllUpTo に満たないときに参加の多い順で補った行を「主な方」とし、ほかの行は「主な方のみ」の
        // スイッチを切ったときだけ見せる。
        var entityList = entities.ToList();
        var relatedColors = AssignRelatedColors(entityList);
        var rows = new List<BuiltRow>();
        foreach (var e in entityList)
        {
            var built = BuildRow(e, rules, relatedColors);
            if (built is not null) rows.Add(built);
        }
        if (rows.Count == 0) return null;

        bool filterable = !rules.IncludeAll && rows.Count > rules.ShowAllUpTo;
        if (filterable)
        {
            foreach (var r in rows) r.Row.IsMain = r.Qualifies;
            int lack = rules.ShowAllUpTo - rows.Count(r => r.Qualifies);
            if (lack > 0)
            {
                // 補う順：参加の多い順 → 最初の参加の早い順 → 同じ話ならクレジットで先に出ている順 → 名前。
                foreach (var r in rows.Where(r => !r.Qualifies)
                             .OrderByDescending(r => r.CreditCount)
                             .ThenBy(r => r.First)
                             .ThenBy(r => r.FirstPos)
                             .ThenBy(r => r.Row.EntityName, StringComparer.Ordinal)
                             .Take(lack))
                    r.Row.IsMain = true;
            }
        }

        var ordered = rows
            .OrderBy(r => r.First)
            .ThenBy(r => string.Equals(r.Row.EntityKind, "company", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(r => r.Last)
            .ThenBy(r => r.FirstPos)
            .ThenBy(r => r.Row.EntityName, StringComparer.Ordinal)
            .ToList();

        var legend = new List<RoleTimelineLegendItem>();
        if (ordered.Any(r => r.Row.Segments.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "tv", Label = $"{rules.Verb}した話（TV）" });
        if (ordered.Any(r => r.Row.Movies.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "movie", Label = "映画" });
        if (ordered.Any(r => r.Row.Songs.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "song", Label = "歌（初めて盤に収められた日）" });
        if (ordered.Any(r => r.Row.Bgms.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "bgm", Label = "劇伴（初めて盤に収められた日）" });
        if (ordered.Any(r => r.Row.Products.Count > 0))
            legend.Add(new RoleTimelineLegendItem { Kind = "disc", Label = "盤（発売日）" });
        // 関連する役職は、前段階 → 並列 → 後段階の順、同じ種類の中は役職マスタの表示順に、役職ごとの色と役職名だけを出す。
        // 見本の形は描いた印に合わせる（TV の話があれば細い帯、映画があれば白抜きの輪。両方あれば並べる）。
        foreach (var g in ordered
                     .SelectMany(r => r.RelatedRoles)
                     .GroupBy(x => x.RoleCode, StringComparer.Ordinal)
                     .Select(g => (Role: g.First(), Items: g.ToList()))
                     .OrderBy(x => x.Role.Lane)
                     .ThenBy(x => x.Role.SortOrder)
                     .ThenBy(x => x.Role.RoleCode, StringComparer.Ordinal))
        {
            bool hasTv = g.Items.Any(x => x.Episodes.Any(ep => ep.EpisodeId != 0));
            bool hasMovie = g.Items.Any(x => x.MovieSeriesIds.Count > 0);
            legend.Add(new RoleTimelineLegendItem
            {
                Kind = hasTv ? "rel" : "rel-movie",
                ExtraKind = hasTv && hasMovie ? "rel-movie" : "",
                Color = relatedColors.TryGetValue(g.Role.RoleCode, out var c) ? c : "",
                Label = g.Role.RoleName,
                Url = PathUtil.CreatorsRoleUrl(g.Role.RoleCode)
            });
        }
        legend.Add(new RoleTimelineLegendItem { Kind = "span", Label = $"続けて{rules.Verb}した期間" });

        return new RoleTimelineModel
        {
            // 絞り込みの条件はページの種類ごとの決まりをそのまま書く（登録状況で変わる出し分けはしない）。
            IsFilterable = filterable,
            FilterTip = filterable ? rules.FilterTip : Array.Empty<string>(),
            MainCount = rows.Count(r => r.Row.IsMain),
            Legend = legend,
            Bands = _bands,
            Ticks = _ticks,
            TickDensityClass = _tickDensityClass,
            Rows = ordered.Select(r => r.Row).ToList()
        };
    }

    /// <summary>組み立てた 1 行と、並び・主な方の選び出しに使う値。</summary>
    /// <param name="Qualifies">ページの決まり（<see cref="RoleTimelineRules"/>）を満たすか。</param>
    /// <param name="CreditCount">描ける参加の数（TV の話数・映画の本数・歌の曲数など）。主な方を補う順に使う。</param>
    /// <param name="RelatedRoles">行に重ねた関連する役職（凡例に出す分）。</param>
    private sealed record BuiltRow(RoleTimelineRow Row, DateOnly First, DateOnly Last, long FirstPos, int CreditCount, bool Qualifies,
        IReadOnlyList<RoleTimelineRelatedRole> RelatedRoles);

    /// <summary>
    /// 関連する役職の色を、ページに出る役職ごとに決める（role_code → CSS の色）。関連の種類ごとの基準の色（前段階＝若葉、並列＝ティール、
    /// 後段階＝藤色。いずれも主の青より淡い）から、同じ種類の役職どうしは色相を <see cref="RelatedHueStep"/> 度ずつ回して分ける
    /// （役職マスタの表示順に並べ、基準の色相を中心に左右へ振る。種類ごとの振れ幅は <see cref="RelatedHueSpread"/> 度まで）。
    /// 描ける担当（話の付いた TV の担当か映画）の無い役職には色を割り当てない。
    /// </summary>
    private Dictionary<string, string> AssignRelatedColors(IEnumerable<RoleTimelineEntity> entities)
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var byLane in entities
                     .SelectMany(e => e.RelatedRoles)
                     .Where(x => x.Episodes.Any(ep => ep.EpisodeId != 0) || x.MovieSeriesIds.Count > 0)
                     .GroupBy(x => x.Lane))
        {
            var roles = byLane
                .GroupBy(x => x.RoleCode, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.RoleCode, StringComparer.Ordinal)
                .ToList();
            var (hue, sat, light) = byLane.Key switch
            {
                RoleRelationLane.Before => (86.0, 50, 72),
                RoleRelationLane.Parallel => (182.0, 45, 70),
                _ => (262.0, 50, 80)
            };
            double step = roles.Count > 1 ? Math.Min(RelatedHueStep, RelatedHueSpread / (roles.Count - 1)) : 0;
            for (int i = 0; i < roles.Count; i++)
            {
                double h = hue + (i - (roles.Count - 1) / 2.0) * step;
                colors[roles[i].RoleCode] = FormattableString.Invariant($"hsl({Math.Round((h % 360 + 360) % 360)}, {sat}%, {light}%)");
            }
        }
        return colors;
    }

    /// <summary>同じ種類の関連する役職どうしの色相の差（度）。</summary>
    private const double RelatedHueStep = 36;

    /// <summary>同じ種類の関連する役職の色相の振れ幅の上限（度）。ほかの種類の色相に届かないようにする。</summary>
    private const double RelatedHueSpread = 72;

    /// <summary>
    /// 1 エンティティの参加を日付順に並べて「続けて参加した期間」に区切り、描く行を作る。描ける参加が無いものは null。
    /// </summary>
    /// <param name="relatedColors">関連する役職の色（<see cref="AssignRelatedColors"/>）。</param>
    private BuiltRow? BuildRow(RoleTimelineEntity e, RoleTimelineRules rules, IReadOnlyDictionary<string, string> relatedColors)
    {
        var credits = new List<Credit>(e.Episodes.Count + e.MovieSeriesIds.Count + e.Songs.Count + e.Bgms.Count + e.Products.Count);
        foreach (var (sid, eid) in e.Episodes)
        {
            // シリーズ全体に付いた TV 系のクレジット（話の無いもの）は日付を持たないので描かない。
            if (eid == 0 || !_ctx.EpisodeById.TryGetValue(eid, out var ep)) continue;
            credits.Add(new Credit(ep.OnAirDate, CreditKind.Tv, sid, ep.SeriesEpNo, ""));
        }
        foreach (var sid in e.MovieSeriesIds)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            credits.Add(new Credit(s.StartDate, CreditKind.Movie, sid, 0, ""));
        }
        foreach (var p in e.Songs) credits.Add(new Credit(p.Date, CreditKind.Song, 0, 0, p.Title));
        foreach (var p in e.Bgms) credits.Add(new Credit(p.Date, CreditKind.Bgm, 0, 0, p.Title));
        foreach (var p in e.Products) credits.Add(new Credit(p.Date, CreditKind.Product, 0, 0, p.Title));
        if (credits.Count == 0) return null;
        credits.Sort((a, b) =>
        {
            int c = a.Date.CompareTo(b.Date);
            if (c != 0) return c;
            c = a.SeriesId.CompareTo(b.SeriesId);
            return c != 0 ? c : a.EpNo.CompareTo(b.EpNo);
        });

        // 隣り合う参加の日付の差が MaxGapDays 以内なら同じ期間（単発の参加は 1 件だけの期間になる）。
        var chains = new List<List<Credit>>();
        var cur = new List<Credit> { credits[0] };
        for (int i = 1; i < credits.Count; i++)
        {
            if (credits[i].Date.DayNumber - cur[^1].Date.DayNumber <= rules.MaxGapDays)
            {
                cur.Add(credits[i]);
            }
            else
            {
                chains.Add(cur);
                cur = new List<Credit> { credits[i] };
            }
        }
        chains.Add(cur);

        // 期間の終わり：最後の参加の放送枠の終わり（映画・歌・劇伴・盤は日付の翌日）。
        static DateOnly ChainEnd(List<Credit> chain)
            => chain.Max(c => c.Kind == CreditKind.Tv ? c.Date.AddDays(EpisodeSpanDays) : c.Date.AddDays(1));
        bool qualifies = rules.IncludeAll
            || (rules.IncludeOpeningCredit && e.HasOpeningCredit)
            || MaxCreditsInWindow(credits, rules.WindowDays) >= rules.MinCreditsInWindow;

        var spans = new List<RoleTimelineMark>(chains.Count);
        var segments = new List<RoleTimelineMark>();
        var movies = new List<RoleTimelineMark>();
        var songs = new List<RoleTimelineMark>();
        var bgms = new List<RoleTimelineMark>();
        var products = new List<RoleTimelineMark>();
        var periodLabels = new List<string>(chains.Count);
        var tvEpNos = new Dictionary<int, SortedSet<int>>();
        var movieIds = new HashSet<int>();

        foreach (var chain in chains)
        {
            var start = chain[0].Date;
            var end = ChainEnd(chain);
            // 細線は 2 件以上つながった期間だけに引く（単発の参加は帯・点だけ）。
            if (chain.Count >= 2)
                spans.Add(new RoleTimelineMark { Left = Pct(start), Width = PctWidth(start, end) });
            periodLabels.Add(PeriodLabel(start, chain[^1].Date));

            // TV の話は、同じシリーズで話数が続く間をひと続きの帯にまとめる。
            foreach (var bySeries in chain.Where(c => c.Kind == CreditKind.Tv).GroupBy(c => c.SeriesId))
            {
                var eps = bySeries.OrderBy(c => c.EpNo).ToList();
                int runStart = 0;
                for (int i = 1; i <= eps.Count; i++)
                {
                    if (i < eps.Count && eps[i].EpNo == eps[i - 1].EpNo + 1) continue;
                    var segStart = eps[runStart].Date;
                    var segEnd = eps[i - 1].Date.AddDays(EpisodeSpanDays);
                    segments.Add(new RoleTimelineMark { Left = Pct(segStart), Width = PctWidth(segStart, segEnd) });
                    runStart = i;
                }
                if (!tvEpNos.TryGetValue(bySeries.Key, out var set))
                {
                    set = new SortedSet<int>();
                    tvEpNos[bySeries.Key] = set;
                }
                foreach (var c in eps) set.Add(c.EpNo);
            }
            foreach (var c in chain.Where(c => c.Kind == CreditKind.Movie))
            {
                movies.Add(new RoleTimelineMark { Left = Pct(c.Date) });
                movieIds.Add(c.SeriesId);
            }
            // 歌・劇伴・盤は、同じ日のものを 1 つの印にまとめる。
            foreach (var d in chain.Where(c => c.Kind == CreditKind.Song).Select(c => c.Date).Distinct())
                songs.Add(new RoleTimelineMark { Left = Pct(d) });
            foreach (var d in chain.Where(c => c.Kind == CreditKind.Bgm).Select(c => c.Date).Distinct())
                bgms.Add(new RoleTimelineMark { Left = Pct(d) });
            foreach (var d in chain.Where(c => c.Kind == CreditKind.Product).Select(c => c.Date).Distinct())
                products.Add(new RoleTimelineMark { Left = Pct(d) });
        }

        // 内訳：TV・映画はシリーズの放送・公開順に「📺 シリーズ名 #1～49（添え書き）」「🎥 映画名（添え書き）」、
        // 歌・劇伴・盤は年ごとに「🎵 2004年 曲名、曲名」「🎼 2004年 作品名（録音回）」「💿 2004年 商品名」で 1 行ずつ。
        var works = new List<(DateOnly Sort, string Text)>();
        foreach (var sid in tvEpNos.Keys.Concat(movieIds).Distinct())
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            string note = e.SeriesNotes is not null && e.SeriesNotes.TryGetValue(sid, out var n) && n != "" ? $"（{n}）" : "";
            works.Add((s.StartDate, tvEpNos.TryGetValue(sid, out var nos)
                ? $"📺 {s.Title} {EpisodeRangeCompressor.Compress(nos)}{note}"
                : $"🎥 {s.Title}{note}"));
        }
        foreach (var (kind, icon, order) in new[] { (CreditKind.Song, "🎵", 0), (CreditKind.Bgm, "🎼", 1), (CreditKind.Product, "💿", 2) })
        {
            foreach (var byYear in credits.Where(c => c.Kind == kind).GroupBy(c => c.Date.Year))
            {
                var titles = byYear.Select(c => c.Title).Where(t => t != "").Distinct(StringComparer.Ordinal);
                // 同じ年の中では歌 → 劇伴 → 盤の順に並べる。
                works.Add((new DateOnly(byYear.Key, 1, 1).AddDays(order), $"{icon} {byYear.Key}年 {string.Join("、", titles)}"));
            }
        }

        // 関連する役職の担当：役職ごとに、TV の話は同じシリーズで話数が続く間をひと続きの細い帯に、映画は白抜きの輪にする（色は役職ごと）。
        // 内訳には主の担当の後ろに「〔役職名〕📺 シリーズ名 #…」「〔役職名〕🎥 映画名」を役職ごとに足す。
        var related = new List<RoleTimelineRelatedMark>();
        var drawnRelated = new List<RoleTimelineRelatedRole>();
        var relatedWorks = new List<string>();
        foreach (var rel in e.RelatedRoles
                     .OrderBy(x => x.Lane)
                     .ThenBy(x => x.SortOrder)
                     .ThenBy(x => x.RoleCode, StringComparer.Ordinal))
        {
            if (!relatedColors.TryGetValue(rel.RoleCode, out var color)) continue;
            var relEps = new Dictionary<int, SortedDictionary<int, DateOnly>>();
            foreach (var (sid, eid) in rel.Episodes)
            {
                if (eid == 0 || !_ctx.EpisodeById.TryGetValue(eid, out var ep)) continue;
                if (!relEps.TryGetValue(sid, out var bySeries))
                {
                    bySeries = new SortedDictionary<int, DateOnly>();
                    relEps[sid] = bySeries;
                }
                bySeries[ep.SeriesEpNo] = ep.OnAirDate;
            }
            var relMovies = rel.MovieSeriesIds.Where(_ctx.SeriesById.ContainsKey).ToList();
            if (relEps.Count == 0 && relMovies.Count == 0) continue;
            drawnRelated.Add(rel);

            var relLines = new List<(DateOnly Sort, string Text)>();
            foreach (var (sid, bySeries) in relEps)
            {
                var eps = bySeries.ToList();
                int runStart = 0;
                for (int i = 1; i <= eps.Count; i++)
                {
                    if (i < eps.Count && eps[i].Key == eps[i - 1].Key + 1) continue;
                    var segStart = eps[runStart].Value;
                    var segEnd = eps[i - 1].Value.AddDays(EpisodeSpanDays);
                    related.Add(new RoleTimelineRelatedMark { Color = color, Left = Pct(segStart), Width = PctWidth(segStart, segEnd) });
                    runStart = i;
                }
                if (_ctx.SeriesById.TryGetValue(sid, out var s))
                    relLines.Add((s.StartDate, $"〔{rel.RoleName}〕📺 {s.Title} {EpisodeRangeCompressor.Compress(bySeries.Keys)}"));
            }
            foreach (var sid in relMovies)
            {
                var s = _ctx.SeriesById[sid];
                related.Add(new RoleTimelineRelatedMark { Color = color, Left = Pct(s.StartDate), IsMovie = true });
                relLines.Add((s.StartDate, $"〔{rel.RoleName}〕🎥 {s.Title}"));
            }
            relatedWorks.AddRange(relLines.OrderBy(l => l.Sort).Select(l => l.Text));
        }

        var row = new RoleTimelineRow
        {
            EntityKind = e.EntityKind,
            EntityName = e.EntityName,
            EntitySubLabel = e.EntitySubLabel,
            EntityUrl = e.EntityUrl,
            Spans = spans,
            Segments = segments,
            Movies = movies,
            Songs = songs,
            Bgms = bgms,
            Products = products,
            Related = related,
            PeriodLabel = string.Join("、", periodLabels),
            WorksText = string.Join("\n", works.OrderBy(w => w.Sort).Select(w => w.Text).Concat(relatedWorks))
        };
        return new BuiltRow(row, chains[0][0].Date, chains[^1][^1].Date, e.FirstSortPos, credits.Count, qualifies, drawnRelated);
    }

    /// <summary>
    /// 連続する <paramref name="windowDays"/> 日のあいだに入る参加の数の最大（日付順に並んだ <paramref name="credits"/> を
    /// 尺取りで数える。最初と最後の参加の日付の差が <paramref name="windowDays"/> 日以内なら同じ期間に入る）。
    /// </summary>
    /// <summary>
    /// 参加の日付の並びが、線表に載せる決まり（<paramref name="rules"/>）を満たすか。<see cref="BuildRow"/> の判定と同じ。
    /// OGP カードのように線表そのものは組まず、「サイトの年表に載る人か」だけを知りたいときに使う。
    /// </summary>
    public static bool Qualifies(IEnumerable<DateOnly> dates, bool hasOpeningCredit, RoleTimelineRules rules)
    {
        if (rules.IncludeAll) return true;
        if (rules.IncludeOpeningCredit && hasOpeningCredit) return true;
        var sorted = dates.OrderBy(d => d.DayNumber).ToList();
        int best = 0;
        for (int i = 0, j = 0; i < sorted.Count; i++)
        {
            while (sorted[i].DayNumber - sorted[j].DayNumber > rules.WindowDays) j++;
            best = Math.Max(best, i - j + 1);
        }
        return best >= rules.MinCreditsInWindow;
    }

    private static int MaxCreditsInWindow(List<Credit> credits, int windowDays)
    {
        int best = 0;
        for (int i = 0, j = 0; i < credits.Count; i++)
        {
            while (credits[i].Date.DayNumber - credits[j].Date.DayNumber > windowDays) j++;
            best = Math.Max(best, i - j + 1);
        }
        return best;
    }

    /// <summary>
    /// 歌・劇伴・盤の参加のうち最も遅い日の翌日（軸を伸ばす日。<see cref="RoleTimelineBuilder(BuildContext, DateOnly?)"/> の extendTo に渡す）。
    /// 歌・劇伴・盤の参加が無ければ null。
    /// </summary>
    public static DateOnly? ExtendToFor(IEnumerable<RoleTimelineEntity> entities)
    {
        DateOnly? last = null;
        foreach (var e in entities)
            foreach (var p in e.Songs.Concat(e.Bgms).Concat(e.Products))
                if (last is null || p.Date > last) last = p.Date;
        return last?.AddDays(1);
    }

    /// <summary>参加期間の表記（「2004年2月〜2005年1月」。同じ月なら「2004年2月」）。</summary>
    private static string PeriodLabel(DateOnly first, DateOnly last)
    {
        string a = $"{first.Year}年{first.Month}月";
        string b = $"{last.Year}年{last.Month}月";
        return a == b ? a : $"{a}〜{b}";
    }

    /// <summary>日付の軸上の位置（軸の左端からの百分率、小数 2 桁の文字列）。軸の外は端に寄せる。</summary>
    private string Pct(DateOnly d) => Num(Ratio(d) * 100);

    /// <summary>期間の軸上の幅（百分率、小数 2 桁の文字列）。</summary>
    private string PctWidth(DateOnly start, DateOnly end) => Num(Math.Max(0, Ratio(end) - Ratio(start)) * 100);

    private double Ratio(DateOnly d)
        => Math.Clamp((double)(d.DayNumber - _axisStart.DayNumber) / _axisDays, 0, 1);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private enum CreditKind { Tv, Movie, Song, Bgm, Product }

    /// <summary>描く単位の参加 1 件（TV の 1 話・映画 1 本・歌 1 曲・劇伴の録音回 1 回・盤 1 点）。</summary>
    private readonly record struct Credit(DateOnly Date, CreditKind Kind, int SeriesId, int EpNo, string Title);
}

/// <summary>線表に載せる行の決まりと、凡例・説明文の言い回し（ページごと）。</summary>
/// <param name="MaxGapDays">続けて参加したとみなす（細線でつなぐ）、隣り合う参加どうしの日付の差の上限（日）。描き方だけに使い、載せる決まりには使わない。</param>
/// <param name="WindowDays">載せる決まりの「一定期間」（日）。連続するこの日数のあいだの参加を数える。</param>
/// <param name="MinCreditsInWindow">一定期間（<paramref name="WindowDays"/>）のあいだに要る参加の数の下限。</param>
/// <param name="IncludeOpeningCredit">オープニングのクレジットに出たことがあれば回数によらず載せるか。</param>
/// <param name="IncludeAll">決まりによらず候補をすべて載せるか。</param>
/// <param name="ShowAllUpTo">描ける参加のある候補がこの数以下なら、決まりによらずすべて載せる（<paramref name="IncludeAll"/> のページでは使わない）。</param>
/// <param name="Verb">凡例の動詞（「担当」「出演」「参加」）。</param>
/// <param name="FilterTip">
/// 「主な方のみ」のスイッチの説明の吹き出しに出す、主な方の条件（1 要素 1 行）。全員を載せるページは空。ページの種類ごとに固定で、
/// 登録状況（どの人が載ったか・誰がオープニングに出ているか）では変えない。
/// </param>
internal sealed record RoleTimelineRules(
    int MaxGapDays, int WindowDays, int MinCreditsInWindow, bool IncludeOpeningCredit, bool IncludeAll, int ShowAllUpTo, string Verb,
    IReadOnlyList<string> FilterTip)
{
    /// <summary>全員を載せる決まりのページの条件（スイッチを出さない）。</summary>
    private static readonly IReadOnlyList<string> NoFilter = Array.Empty<string>();

    /// <summary>決まりで絞るページでも全員を載せる候補の数の上限（主な方を補う数でもある）。</summary>
    public const int DefaultShowAllUpTo = 10;

    /// <summary>役職詳細：メインスタッフ（役職を問わずオープニングに出た）と、1 年間（52 週）に 4 回以上担当したスタッフ（細線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules Staff = new(
        91, WindowDays: 364, MinCreditsInWindow: 4, IncludeOpeningCredit: true, IncludeAll: false, DefaultShowAllUpTo, "担当",
        new[]
        {
            "次のいずれかに当たる方",
            "・オープニングにクレジットされたことがある",
            "・52週のあいだに4回以上担当した（TVの話数・映画の本数）",
            $"{DefaultShowAllUpTo}名に満たないときは担当回数の多い方から補います",
            "（同じ回数ならクレジットが早い方、同じ話なら先に出ている方）"
        });

    /// <summary>声の出演：1 年間（52 週）に 4 回以上出演した声優（細線は 4 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules VoiceCast = new(
        28, WindowDays: 364, MinCreditsInWindow: 4, IncludeOpeningCredit: false, IncludeAll: false, DefaultShowAllUpTo, "出演",
        new[]
        {
            "52週のあいだに4回以上出演した方（TVの話数・映画の本数）",
            $"{DefaultShowAllUpTo}名に満たないときは出演回数の多い方から補います",
            "（同じ回数ならクレジットが早い方、同じ話なら先に出ている方）"
        });

    /// <summary>作詞・作曲・編曲の役職詳細：1 年間（52 週）に 2 曲以上担当した人物（細線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules SongWriter = new(
        91, WindowDays: 364, MinCreditsInWindow: 2, IncludeOpeningCredit: false, IncludeAll: false, DefaultShowAllUpTo, "担当",
        new[]
        {
            "52週のあいだに2曲以上担当した方",
            $"{DefaultShowAllUpTo}名に満たないときは担当曲数の多い方から補います",
            "（同じ曲数なら担当が早い方）"
        });

    /// <summary>音楽の役職詳細（演奏など）：一覧に載る人物・団体すべて（細線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules MusicRole = new(
        91, WindowDays: 0, MinCreditsInWindow: 0, IncludeOpeningCredit: false, IncludeAll: true, ShowAllUpTo: 0, "担当", NoFilter);

    /// <summary>歌唱：一覧に載る歌手・キャラクターすべて（続けて参加した期間の線は 13 週以内の間隔でつなぐ）。</summary>
    public static readonly RoleTimelineRules Singers = new(
        91, WindowDays: 0, MinCreditsInWindow: 0, IncludeOpeningCredit: false, IncludeAll: true, ShowAllUpTo: 0, "参加", NoFilter);
}

/// <summary>線表に載せる候補 1 つ分（<see cref="RoleTimelineBuilder.Build"/> の入力）。</summary>
internal sealed class RoleTimelineEntity
{
    /// <summary>"person" / "company" / "singer" / "character" / "unit"（行頭のアイコンと、絞り込みの種類）。</summary>
    public required string EntityKind { get; init; }
    /// <summary>行に出す名前（人物は表示名義、企業は最後に使われた屋号、キャラクターは一覧と同じ名義）。</summary>
    public required string EntityName { get; init; }
    /// <summary>内訳で名前の下に添える 1 行（キャラクターの「CV: 声優」など）。無ければ空文字。</summary>
    public string EntitySubLabel { get; init; } = "";
    /// <summary>名前のリンク先（人物・企業・キャラクター詳細）。</summary>
    public required string EntityUrl { get; init; }
    /// <summary>
    /// 最初と最後の参加の日付が同じ行どうしの並びのキー（小さいほうが先）。役職詳細・声の出演は初めてクレジットされた話の中での
    /// クレジットの位置（その話のクレジットで上に出ている順）、歌唱・作詞作曲編曲は初参加の録音、音楽の役職詳細は最初の担当先の日付。
    /// </summary>
    public long FirstSortPos { get; init; }
    /// <summary>役職を問わずオープニングのクレジットに出たことがあるか（役職詳細のメインスタッフの判定）。</summary>
    public bool HasOpeningCredit { get; init; }
    /// <summary>参加した TV 系の話 (series_id, episode_id)。</summary>
    public IReadOnlyCollection<(int SeriesId, int EpisodeId)> Episodes { get; init; } = Array.Empty<(int, int)>();
    /// <summary>参加した映画系のシリーズ。</summary>
    public IReadOnlyCollection<int> MovieSeriesIds { get; init; } = Array.Empty<int>();
    /// <summary>参加した歌（初めて盤に収められた日と曲名）。</summary>
    public IReadOnlyCollection<RoleTimelinePoint> Songs { get; init; } = Array.Empty<RoleTimelinePoint>();
    /// <summary>参加した劇伴の録音回（初めて盤に収められた日と「作品名（録音回）」）。</summary>
    public IReadOnlyCollection<RoleTimelinePoint> Bgms { get; init; } = Array.Empty<RoleTimelinePoint>();
    /// <summary>参加した盤（発売日と商品名）。</summary>
    public IReadOnlyCollection<RoleTimelinePoint> Products { get; init; } = Array.Empty<RoleTimelinePoint>();
    /// <summary>内訳でシリーズ・映画の後ろに括弧で添える文（series_id → 文。声優の演じたキャラなど）。</summary>
    public IReadOnlyDictionary<int, string>? SeriesNotes { get; init; }
    /// <summary>
    /// 役職詳細で、ページの役職と関連（段階・並列）でつながった役職の担当。主の帯の下に重ねて描くだけで、
    /// 行の並び・主な方の判定には使わない。役職詳細以外のページでは空。
    /// </summary>
    public IReadOnlyList<RoleTimelineRelatedRole> RelatedRoles { get; init; } = Array.Empty<RoleTimelineRelatedRole>();
}

/// <summary>線表の行に重ねる関連する役職 1 つの担当（<see cref="RoleTimelineEntity.RelatedRoles"/>）。</summary>
internal sealed class RoleTimelineRelatedRole
{
    /// <summary>ページの役職から見た関連の種類（帯の色相の基準）。</summary>
    public RoleRelationLane Lane { get; init; }
    /// <summary>役職（系譜の代表 role_code）。凡例のリンク先に使う。</summary>
    public required string RoleCode { get; init; }
    /// <summary>役職名（凡例と内訳に出す）。</summary>
    public required string RoleName { get; init; }
    /// <summary>役職マスタの表示順（凡例・内訳・色の割り当てで同じ種類の役職を並べる順）。</summary>
    public int SortOrder { get; init; }
    /// <summary>担当した TV 系の話 (series_id, episode_id)。</summary>
    public IReadOnlyCollection<(int SeriesId, int EpisodeId)> Episodes { get; init; } = Array.Empty<(int, int)>();
    /// <summary>担当した映画系のシリーズ。</summary>
    public IReadOnlyCollection<int> MovieSeriesIds { get; init; } = Array.Empty<int>();
}

/// <summary>線表に点で描く参加 1 件（歌・劇伴・盤の日付と、内訳に出す名前）。</summary>
internal readonly record struct RoleTimelinePoint(DateOnly Date, string Title);

/// <summary>線表の表示モデル。位置・幅は軸の左端からの百分率（小数 2 桁の文字列）。</summary>
internal sealed class RoleTimelineModel
{
    /// <summary>「主な方のみ」のスイッチを出すか（候補が <see cref="RoleTimelineRules.ShowAllUpTo"/> を超える、決まりで絞るページ）。</summary>
    public bool IsFilterable { get; set; }
    /// <summary>スイッチの説明の吹き出しに出す、主な方の条件（1 要素 1 行）。</summary>
    public IReadOnlyList<string> FilterTip { get; set; } = Array.Empty<string>();
    /// <summary>主な方の行の数（スイッチの横の「14 / 38名」の左。右は <see cref="Rows"/> の数）。</summary>
    public int MainCount { get; set; }
    /// <summary>凡例（描いた印の種類だけ）。</summary>
    public IReadOnlyList<RoleTimelineLegendItem> Legend { get; set; } = Array.Empty<RoleTimelineLegendItem>();
    /// <summary>TV シリーズの帯（交互に薄く塗る）。</summary>
    public IReadOnlyList<RoleTimelineBand> Bands { get; set; } = Array.Empty<RoleTimelineBand>();
    /// <summary>年の目盛り（帯ごとに 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineTick> Ticks { get; set; } = Array.Empty<RoleTimelineTick>();
    /// <summary>帯が多いときに目盛りの文字を間引く CSS クラス（"rtl-ticks-medium" / "rtl-ticks-sparse" / 空）。</summary>
    public string TickDensityClass { get; set; } = "";
    /// <summary>行（並びは参加の始まり → 終わりの早い順）。</summary>
    public IReadOnlyList<RoleTimelineRow> Rows { get; set; } = Array.Empty<RoleTimelineRow>();
}

/// <summary>
/// 凡例 1 項目。Kind は "tv" / "movie" / "song" / "bgm" / "disc" / "span"、関連する役職は "rel"（TV の話の細い帯）か
/// "rel-movie"（映画の白抜きの輪）（印の見本の CSS クラス）。関連する役職の見本の色は <see cref="Color"/>。
/// </summary>
internal sealed class RoleTimelineLegendItem
{
    public string Kind { get; set; } = "";
    /// <summary>
    /// 2 つ目の見本の CSS クラスの接尾（関連する役職で TV の話と映画の両方を描いたとき、帯の見本に続けて映画の輪の見本
    /// "rel-movie" を並べる）。無ければ空文字。
    /// </summary>
    public string ExtraKind { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>関連する役職の見本の色（CSS の色）。それ以外の項目では空文字。</summary>
    public string Color { get; set; } = "";
    /// <summary>ラベルのリンク先（関連する役職の役職詳細）。無ければ空文字。</summary>
    public string Url { get; set; } = "";
}

/// <summary>線表の TV シリーズの帯 1 本。</summary>
internal sealed class RoleTimelineBand
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
    /// <summary>交互に薄く塗る側の帯か。</summary>
    public bool IsAlt { get; set; }
}

/// <summary>線表の年の目盛り 1 つ（TV シリーズの放送開始の位置）。</summary>
internal sealed class RoleTimelineTick
{
    public string Left { get; set; } = "";
    public string YearLabel { get; set; } = "";
    /// <summary>シリーズの正式タイトル（目盛りの title 属性に出す）。</summary>
    public string SeriesTitle { get; set; } = "";
    public string SeriesUrl { get; set; } = "";
    /// <summary>年の文字を出すか（帯の幅が足りない軸の右端では出さない）。</summary>
    public bool ShowLabel { get; set; }
    /// <summary>帯が多いとき、狭い画面で文字を間引く年（5 の倍数以外）か。</summary>
    public bool IsMinor { get; set; }
}

/// <summary>線表の 1 行（人物・企業/団体・キャラクター 1 つ）。</summary>
internal sealed class RoleTimelineRow
{
    public string EntityKind { get; set; } = "";
    public string EntityName { get; set; } = "";
    /// <summary>内訳で名前の下に添える 1 行（無ければ空文字）。</summary>
    public string EntitySubLabel { get; set; } = "";
    public string EntityUrl { get; set; } = "";
    /// <summary>主な方の行か（「主な方のみ」のスイッチが入っているときに見せる行。スイッチの無いページではすべて true）。</summary>
    public bool IsMain { get; set; } = true;
    /// <summary>続けて参加した期間（2 件以上つながったもの）の細線。</summary>
    public IReadOnlyList<RoleTimelineMark> Spans { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>TV の話の帯（話数が続く間はひと続き）。</summary>
    public IReadOnlyList<RoleTimelineMark> Segments { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>映画の点（<see cref="RoleTimelineMark.Width"/> は使わない）。</summary>
    public IReadOnlyList<RoleTimelineMark> Movies { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>歌の点（同じ日に出た曲は 1 つ。<see cref="RoleTimelineMark.Width"/> は使わない）。</summary>
    public IReadOnlyList<RoleTimelineMark> Songs { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>劇伴の点（同じ日のものは 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineMark> Bgms { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>盤の四角（同じ日のものは 1 つ）。</summary>
    public IReadOnlyList<RoleTimelineMark> Products { get; set; } = Array.Empty<RoleTimelineMark>();
    /// <summary>関連する役職の担当（主の帯の下に重ねる細い帯・白抜きの輪）。</summary>
    public IReadOnlyList<RoleTimelineRelatedMark> Related { get; set; } = Array.Empty<RoleTimelineRelatedMark>();
    /// <summary>内訳の参加期間（「2004年2月〜2005年1月」を「、」でつないだもの）。</summary>
    public string PeriodLabel { get; set; } = "";
    /// <summary>内訳の作品ごとの参加（改行でつないだもの）。</summary>
    public string WorksText { get; set; } = "";
}

/// <summary>線表上の印 1 つの位置と幅。</summary>
internal sealed class RoleTimelineMark
{
    public string Left { get; set; } = "";
    public string Width { get; set; } = "";
}

/// <summary>関連する役職の担当の印 1 つ（TV の話の細い帯、または映画の白抜きの輪）。</summary>
internal sealed class RoleTimelineRelatedMark
{
    /// <summary>役職の色（CSS の色。<see cref="RoleTimelineBuilder"/> が役職ごとに割り当てる）。</summary>
    public string Color { get; set; } = "";
    public string Left { get; set; } = "";
    /// <summary>帯の幅（映画の輪では使わない）。</summary>
    public string Width { get; set; } = "";
    /// <summary>映画の輪か。</summary>
    public bool IsMovie { get; set; }
}
