using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サブタイトル統計のページ群を生成するジェネレータ。
/// 1 ページ 1 ランキング厳守の方針で、16 詳細ページ + 1 ランディングの 17 ページ構成。
/// シリーズ単位の集計は TV のみ対象（series.kind_code = 'TV'）で、スピンオフ・映画は除外する。
/// 集計の元データは <c>episodes.title_char_stats</c>（JSON 列）に保存されている文字種別カウンタ。
/// 全クエリは <see cref="SubtitleStatsRepository"/> 経由で SQL を発行する。
/// </summary>
public sealed class SubtitleStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly SubtitleStatsRepository _repo;

    /// <summary>1 ページあたりの最大件数（TOP 100）。</summary>
    private const int Limit = 100;

    /// <summary>当ジェネレータが生成する全ページに付与するカバレッジラベル （「『○○プリキュア』第N話(YYYY.M.D)時点」表記）。</summary>
    private string _coverageLabel = "";

    public SubtitleStatsGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _repo = new SubtitleStatsRepository(factory, ctx.HiddenEpisodeIds.ToList());
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating subtitle stats");

        // カバレッジラベルを先に算出。BuildContext のシリーズ・エピソードを走査して
        // サブタイトル本文が登録済みの最新 TV エピソードを 1 件特定する。
        var latest = StatsCoverageLabel.FindLatestTvEpisodeWithSubtitle(_ctx);
        _coverageLabel = StatsCoverageLabel.BuildSubtitle(latest, _ctx.BuildStartedAt);

        // ── 索引 ──
        GenerateIndex();

        // ── 使用文字 ──
        await GenerateCharsAllAsync(ct).ConfigureAwait(false);
        await GenerateCharsKanjiAsync(ct).ConfigureAwait(false);
        await GenerateSymbolsByFirstAppearAsync(ct).ConfigureAwait(false);

        // ── 文字数（エピソード単位のみ。シリーズ単位は「シリーズ別」グループへ移動した） ──
        await GenerateLengthEpisodeAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateLengthEpisodeAsync(ct, ascending: true).ConfigureAwait(false);

        // ── 漢字率（エピソード単位のみ。シリーズ単位は「シリーズ別」グループへ移動した） ──
        await GenerateKanjiRateEpisodeAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateKanjiRateEpisodeAsync(ct, ascending: true).ConfigureAwait(false);

        // ── 記号率（エピソード単位のみ。シリーズ単位は「シリーズ別」グループへ移動した） ──
        await GenerateSymbolRateEpisodeAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateSymbolRateEpisodeAsync(ct, ascending: true).ConfigureAwait(false);

        // ── シリーズ別 6 ページ（「その他」から「シリーズ別」にリネームし統合） ──
        await GenerateAvgLengthBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateKanjiRateBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateSymbolRateBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateCharTypesBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateSymbolsBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateTopCharsBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateTopKanjiBySeriesAsync(ct).ConfigureAwait(false);

        _ctx.Logger.Success("subtitles: 17 ページ");
    }

    // 索引

    /// <summary>
    /// 率（%）を分子・分母から 1 回で求める。集計クエリの Ratio は MySQL の整数どうしの割り算で小数第 4 位に丸められて
    /// いるため、それを 100 倍してから表示で小数第 1 位に丸めると、2 回丸めて 0.1 ずれることがある（6÷11＝54.545…% が 54.6%）。
    /// 分母が 0 のときは 0。
    /// </summary>
    private static double RatioPercent(long numerator, long denominator)
        => denominator > 0 ? numerator * 100.0 / denominator : 0.0;

    private void GenerateIndex()
    {
        var layout = new LayoutModel
        {
            PageTitle = "歴代サブタイトル統計",
            OgCard = new OgCardSpec(Kicker: "統計", Title: "歴代サブタイトル統計")
            {
                MetaLeft = _coverageLabel,
                // ページの冒頭文（stats-subtitles-index.sbn の .lead）と同じ文。
                Subtitle = "サブタイトルの文字の統計"
            },
            MetaDescription = "歴代プリキュアのサブタイトルの文字の統計",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "歴代サブタイトル統計", Url = "" }
            }
        };
        // テンプレ側はモデル不要（index は完全静的）。空オブジェクトを渡す。
        _page.RenderAndWrite("/stats/subtitles/", "stats", "stats-subtitles-index.sbn", new { CoverageLabel = _coverageLabel }, layout);
    }

    // 使用文字

    private async Task GenerateCharsAllAsync(CancellationToken ct)
    {
        var rows = await _repo.GetCharRankingAllAsync(Limit, ct).ConfigureAwait(false);
        var content = await BuildCharRankingContentAsync(rows, kanjiGrades: false, ct).ConfigureAwait(false);
        var layout = MakeLayout("使用文字 TOP 100（全文字）", "全文字");
        _page.RenderAndWrite("/stats/subtitles/chars/all/", "stats", "stats-subtitles-chars-all.sbn", content, layout);
    }

    private async Task GenerateCharsKanjiAsync(CancellationToken ct)
    {
        var rows = await _repo.GetCharRankingKanjiAsync(Limit, ct).ConfigureAwait(false);
        var content = await BuildCharRankingContentAsync(rows, kanjiGrades: true, ct).ConfigureAwait(false);
        var layout = MakeLayout("使用文字 TOP 100（漢字限定）", "漢字限定");
        _page.RenderAndWrite("/stats/subtitles/chars/kanji/", "stats", "stats-subtitles-chars-kanji.sbn", content, layout);
    }

    /// <summary>作品 × 字の出現回数（使用文字の 2 ページで共有する。最初に使うときに 1 回だけ引く）。</summary>
    private IReadOnlyDictionary<(int SeriesId, string Char), long>? _seriesCharCounts;

    /// <summary>
    /// 使用文字のランキング（全文字・漢字限定）のページのモデルを組む。頭に字の一覧図（回数が多い字ほど大きい札）、
    /// 下に順位の行。行には、本線の TV シリーズを放送順に並べた作品ごとの使われ方のマスと、いちばん多く使われた作品を添える。
    /// </summary>
    /// <param name="kanjiGrades">
    /// 札と字の色を、習う学年・中学以降の常用漢字・常用漢字でない字で分けるか（漢字限定のページ）。false なら字の種類で分ける。
    /// </param>
    private async Task<object> BuildCharRankingContentAsync(
        IReadOnlyList<SubtitleStatsRepository.CharRankingRow> rows, bool kanjiGrades, CancellationToken ct)
    {
        var (series, counts) = await LoadSeriesCharCountsAsync(ct).ConfigureAwait(false);
        var view = StatsCharRows.Build(rows.Select(r => new StatsCharInput(r.Rank, r.TotalCount, r.Char)), series, counts,
            kanjiGrades ? KanjiGrades.ClassOf : null);
        var legend = kanjiGrades
            ? KanjiGrades.Legend
            : new[]
            {
                ("ct-kanji", "漢字"), ("ct-hiragana", "ひらがな"), ("ct-katakana", "カタカナ"),
                ("ct-latin", "英字"), ("ct-digits", "数字"), ("ct-symbols", "記号")
            };
        return new
        {
            Rows = view,
            // 作品ごとのマスの列の見出し（放送開始年の下 2 桁）。
            SeriesYears = series.Select(s => (s.Year % 100).ToString("00")).ToList(),
            // 色の凡例：一覧に出てくる区分だけ出す。
            Legend = legend.Where(l => view.Any(v => v.TypeClass == l.Item1))
                .Select(l => new { Class = l.Item1, Label = l.Item2 }).ToList(),
            CoverageLabel = _coverageLabel
        };
    }

    /// <summary>作品ごとの使われ方の列の材料：本線の TV シリーズ（放送順）と、作品 × 字の出現回数。</summary>
    private async Task<(List<StatsCharSeries> Series, IReadOnlyDictionary<(int SeriesId, string Char), long> Counts)> LoadSeriesCharCountsAsync(CancellationToken ct)
    {
        _seriesCharCounts ??= (await _repo.GetSeriesCharCellsAsync(ct).ConfigureAwait(false))
            .GroupBy(c => (c.SeriesId, c.Char))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Cnt));
        var series = _ctx.SeriesById.Values
            .Where(s => _ctx.IsMainlineTvSeries(s.SeriesId))
            .OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId)
            .Select(s => new StatsCharSeries(s.SeriesId, s.Title, s.StartDate.Year))
            .ToList();
        return (series, _seriesCharCounts);
    }

    private async Task GenerateSymbolsByFirstAppearAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSymbolsByFirstAppearAsync(ct).ConfigureAwait(false);
        var (series, counts) = await LoadSeriesCharCountsAsync(ct).ConfigureAwait(false);
        // 初使用が早い順（リポジトリの順序）を保ったまま、使用文字 TOP100 と同じ大グリフ＋出現回数
        // パネル＋初使用エピソード（シリーズ・第N話(放送日)・ルビ付きサブタイトル）の脱テーブル行へ。
        var view = StatsSymbolRows.Build(_ctx, rows.Select(r => new StatsSymbolInput(
            r.Char, r.TotalCount, r.FirstSeriesSlug, r.FirstSeriesEpNo, r.FirstSeriesTitle,
            _ctx.StartYearLabelBySlug(r.FirstSeriesSlug), r.FirstBroadcastDate, r.FirstTitleText)), series, counts);
        // 頭の図：記号を初使用の年ごとにまとめる（年の早い順。年の中は初使用の早い順のまま）。
        var years = view.GroupBy(v => v.FirstYear)
            .OrderBy(g => g.Key)
            .Select(g => new { Year = g.Key > 0 ? g.Key.ToString() : "", Symbols = g.ToList() })
            .ToList();
        var content = new
        {
            Rows = view,
            Years = years,
            CoverageLabel = _coverageLabel
        };
        var layout = MakeLayout("記号出現回数・初使用エピソード", "記号出現回数・初使用エピソード");
        _page.RenderAndWrite("/stats/subtitles/chars/symbols-order/", "stats", "stats-subtitles-chars-symbols-order.sbn", content, layout);
    }

    // 文字数

    private async Task GenerateLengthEpisodeAsync(CancellationToken ct, bool ascending)
    {
        // 全部の話の文字数を 1 回で引き、上位 100 件（100 位が同点なら全件）を行に、全体を分布の図にする。
        var all = await _repo.GetTitleLengthRankingAsync(ascending, int.MaxValue, ct).ConfigureAwait(false);
        var ranked = all.Where(r => r.Rank <= Limit).ToList();
        // 題名の下に 1 字 1 マスを並べる。全部のマスを塗るので、マスの列の長さが文字数になる。
        var rows = StatsEpisodeRows.Build(_ctx, ranked.Select(r => new StatsEpisodeInput(
            r.SeriesSlug, r.SeriesEpNo, r.SeriesTitle, _ctx.StartYearLabelBySlug(r.SeriesSlug),
            true, r.Rank, r.Value.ToString(), r.TitleText)
        {
            ValueUnit = "文字",
            Mark = StatsTitleMark.All
        }));
        string slug = ascending ? "least" : "most";
        string label = ascending ? "少ない順" : "多い順";
        string url = $"/stats/subtitles/length/episode/{slug}/";
        string chart = StatsRankingView.CountHistogram(
            all.Select(r => (double)r.Value).ToList(), ranked.Select(r => (double)r.Value).ToList(), all.Where(r => r.Rank <= 10).Select(r => (double)r.Value).ToList(), descending: !ascending, "",
            "プリキュア TV シリーズ全話のサブタイトルの文字数の分布。TOP 100 の話を濃い色で示す");
        var view = StatsRankingView.Build(_ctx, rows, _coverageLabel, "文字数",
            chartSvg: chart,
            chartHeading: $"全 {all.Count:N0} 話の分布",
            chartNote: $"サブタイトルの文字数ごとの話数です。平均は {(all.Count > 0 ? all.Average(r => (double)r.Value) : 0):0.0} 文字。濃い色が TOP 100 の話です。",
            subtitleStats: true);
        var layout = MakeLayout($"エピソード単位 文字数 {label} TOP 100", $"文字数 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-length-episode-{slug}.sbn", view, layout);
    }

    /// <summary>
    /// エピソード単位の率（漢字率・記号率）のランキング 1 ページぶんのモデルを組む。行の題名は、数えた字（漢字・記号）に色を付け、
    /// 題名の下に 1 字 1 マス（数えた字のマスだけ塗る）と内訳を添える。全部の話の率を分布の図にする。
    /// </summary>
    /// <param name="all">全部の話の行（順位の昇順）。</param>
    /// <param name="valueName">率の名前（「漢字率」など）。</param>
    /// <param name="numeratorName">分子の名前（「漢字」など）。</param>
    private StatsRankingView BuildRatioEpisodeView(
        IReadOnlyList<SubtitleStatsRepository.EpisodeRatioRow> all, bool ascending, StatsTitleMark mark, string valueName, string numeratorName)
    {
        var ranked = all.Where(r => r.Rank <= Limit).ToList();
        // 指標値は率の百分率（小数 1 桁＋%）。内訳は集計と同じ数（分子 / 空白を除いた全文字数）。
        var rows = StatsEpisodeRows.Build(_ctx, ranked.Select(r => new StatsEpisodeInput(
            r.SeriesSlug, r.SeriesEpNo, r.SeriesTitle, _ctx.StartYearLabelBySlug(r.SeriesSlug),
            true, r.Rank, RatioPercent(r.KanjiCount, r.TotalCount).ToString("0.0") + "%", r.TitleText)
        {
            ValueUnit = "%",
            Mark = mark,
            DetailLabel = $"{numeratorName} {r.KanjiCount} / 全 {r.TotalCount} 字"
        }));
        string chart = StatsRankingView.PercentHistogram(
            all.Select(r => RatioPercent(r.KanjiCount, r.TotalCount)).ToList(),
            ranked.Select(r => RatioPercent(r.KanjiCount, r.TotalCount)).ToList(),
            all.Where(r => r.Rank <= 10).Select(r => RatioPercent(r.KanjiCount, r.TotalCount)).ToList(), descending: !ascending,
            $"プリキュア TV シリーズ全話のサブタイトルの{valueName}の分布。TOP 100 の話を濃い色で示す");
        return StatsRankingView.Build(_ctx, rows, _coverageLabel, valueName,
            chartSvg: chart,
            chartHeading: $"全 {all.Count:N0} 話の分布",
            chartNote: $"{valueName}を 5% きざみで数えた話数です。濃い色が TOP 100 の話です。",
            subtitleStats: true);
    }

    /// <summary>シリーズ別 平均文字数（多い順）。 「シリーズ単位 多い順 / 少ない順」の 2 ページから多い順 1 ページに集約し、 「シリーズ別」グループの 1 つとして再配置。少ない順は同じテーブルの逆順なので削除。</summary>
    private async Task GenerateAvgLengthBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSeriesAverageLengthAsync(ascending: false, Limit, ct).ConfigureAwait(false);
        // 順位の行：値は平均文字数（小数 1 桁）、内訳は話数。
        var view = BuildSeriesRankRows(rows.Select(r => new SeriesRankInput(
            r.Rank, r.SeriesSlug, r.SeriesTitle, r.Average, r.Average.ToString("0.0"), $"{r.EpisodeCount} 話")));

        // 冒頭の箱ひげ図：各話の文字数を作品ごとに束ね、放送開始の早い順に並べる。
        var lengths = await _repo.GetTitleLengthsBySeriesAsync(ct).ConfigureAwait(false);
        var boxes = lengths
            .GroupBy(l => l.SeriesId)
            .Where(g => _ctx.SeriesById.ContainsKey(g.Key))
            .Select(g => (Series: _ctx.SeriesById[g.Key], Values: g.Select(l => l.Len).ToList()))
            .OrderBy(x => x.Series.StartDate)
            .Select(x => new StatsBoxPlotItem(x.Series.StartDate.Year, x.Series.Title, x.Values))
            .ToList();
        string chart = StatsSeriesCharts.BuildBoxPlot(boxes, "文字",
            "TV シリーズごとのサブタイトルの文字数の箱ひげ図。放送開始年の順に、最小値・最大値・四分位範囲・中央値・平均値・最頻値を表す");

        var layout = MakeLayout("シリーズ別 平均文字数", "シリーズ別 平均文字数");
        _page.RenderAndWrite("/stats/subtitles/avg-length-by-series/", "stats", "stats-subtitles-avg-length-by-series.sbn", new { Rows = view, ChartSvg = chart, CoverageLabel = _coverageLabel }, layout);
    }

    /// <summary>シリーズ別の順位のページ（平均文字数・漢字率・記号率）の 1 行ぶんの材料。</summary>
    /// <param name="Rank">順位（同点は同じ順位）。</param>
    /// <param name="Value">値（横棒の長さを決める）。</param>
    /// <param name="ValueLabel">値の表記（「16.3」「21.3%」など）。</param>
    /// <param name="DetailLabel">値の内訳（「49 話」「漢字 312 / 総文字数 901」など）。</param>
    private readonly record struct SeriesRankInput(int Rank, string SeriesSlug, string SeriesTitle, double Value, string ValueLabel, string DetailLabel);

    /// <summary>
    /// シリーズ別の順位のページの行を組み立てる（順位の昇順で渡す）。エピソード単位のランキングと同じく、
    /// 同じ順位が続く 2 件目からは順位を空にする。横棒は、いちばん大きい値を 100% とした長さ。
    /// </summary>
    private List<object> BuildSeriesRankRows(IEnumerable<SeriesRankInput> items)
    {
        var list = items.ToList();
        double max = list.Count > 0 ? list.Max(i => i.Value) : 0.0;
        var rows = new List<object>(list.Count);
        int? prevRank = null;
        foreach (var it in list)
        {
            rows.Add(new
            {
                RankLabel = prevRank == it.Rank ? "" : it.Rank.ToString(),
                it.ValueLabel,
                it.SeriesTitle,
                SeriesUrl = PathUtil.SeriesUrl(it.SeriesSlug),
                SeriesStartYearLabel = _ctx.StartYearLabelBySlug(it.SeriesSlug),
                BarPercent = (max > 0 ? it.Value * 100.0 / max : 0.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                it.DetailLabel
            });
            prevRank = it.Rank;
        }
        return rows;
    }

    /// <summary>
    /// シリーズ別の率（漢字率・記号率）のページの、順位の行と冒頭の折れ線グラフを組み立てる。
    /// 率は分子・分母から求め直す（<see cref="RatioPercent"/>）。グラフは放送開始の早い順。
    /// </summary>
    /// <param name="valueName">率の名前（「漢字率」など）。</param>
    /// <param name="numeratorName">分子の名前（「漢字」など）。</param>
    private (List<object> Rows, string ChartSvg) BuildSeriesRatioView(
        IReadOnlyList<SubtitleStatsRepository.SeriesRatioRow> rows, string valueName, string numeratorName)
    {
        string Detail(SubtitleStatsRepository.SeriesRatioRow r) => $"{numeratorName} {r.KanjiCount:N0} / 総文字数 {r.TotalCount:N0}";

        var view = BuildSeriesRankRows(rows.Select(r =>
        {
            double pct = RatioPercent(r.KanjiCount, r.TotalCount);
            return new SeriesRankInput(r.Rank, r.SeriesSlug, r.SeriesTitle, pct, pct.ToString("0.0") + "%", Detail(r));
        }));

        var points = rows
            .Where(r => _ctx.SeriesById.ContainsKey(r.SeriesId))
            .Select(r => (Series: _ctx.SeriesById[r.SeriesId], Row: r))
            .OrderBy(x => x.Series.StartDate)
            .Select(x => new StatsLineChartItem(
                x.Series.StartDate.Year, x.Series.Title, RatioPercent(x.Row.KanjiCount, x.Row.TotalCount), Detail(x.Row)))
            .ToList();
        string chart = StatsSeriesCharts.BuildLineChart(points, valueName, "%",
            $"TV シリーズごとのサブタイトルの{valueName}の折れ線グラフ。放送開始年の順に並べている");

        return (view, chart);
    }

    // 漢字率

    private async Task GenerateKanjiRateEpisodeAsync(CancellationToken ct, bool ascending)
    {
        var all = await _repo.GetKanjiRateEpisodeAsync(ascending, int.MaxValue, ct).ConfigureAwait(false);
        var view = BuildRatioEpisodeView(all, ascending, StatsTitleMark.Kanji, "漢字率", "漢字");
        string slug = ascending ? "least" : "most";
        string label = ascending ? "低い順" : "高い順";
        string url = $"/stats/subtitles/kanji-rate/episode/{slug}/";
        var layout = MakeLayout($"エピソード単位 漢字率 {label} TOP 100", $"漢字率 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-kanji-rate-episode-{slug}.sbn", view, layout);
    }

    /// <summary>シリーズ別 漢字率（高い順）。 「シリーズ単位 高い順 / 低い順」の 2 ページから高い順 1 ページに集約し、 「シリーズ別」グループの 1 つとして再配置。低い順は同じテーブルの逆順なので削除。</summary>
    private async Task GenerateKanjiRateBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetKanjiRateSeriesAsync(ascending: false, Limit, ct).ConfigureAwait(false);
        var (view, chart) = BuildSeriesRatioView(rows, "漢字率", "漢字");
        var layout = MakeLayout("シリーズ別 漢字率", "シリーズ別 漢字率");
        _page.RenderAndWrite("/stats/subtitles/kanji-rate-by-series/", "stats", "stats-subtitles-kanji-rate-by-series.sbn", new { Rows = view, ChartSvg = chart, CoverageLabel = _coverageLabel }, layout);
    }

    // 記号率

    private async Task GenerateSymbolRateEpisodeAsync(CancellationToken ct, bool ascending)
    {
        var all = await _repo.GetSymbolRateEpisodeAsync(ascending, int.MaxValue, ct).ConfigureAwait(false);
        var view = BuildRatioEpisodeView(all, ascending, StatsTitleMark.Symbol, "記号率", "記号");
        string slug = ascending ? "least" : "most";
        string label = ascending ? "低い順" : "高い順";
        string url = $"/stats/subtitles/symbol-rate/episode/{slug}/";
        var layout = MakeLayout($"エピソード単位 記号率 {label} TOP 100", $"記号率 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-symbol-rate-episode-{slug}.sbn", view, layout);
    }

    /// <summary>シリーズ別 記号率（高い順）。 「シリーズ単位 高い順 / 低い順」の 2 ページから高い順 1 ページに集約し、 「シリーズ別」グループの 1 つとして再配置。低い順は同じテーブルの逆順なので削除。</summary>
    private async Task GenerateSymbolRateBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSymbolRateSeriesAsync(ascending: false, Limit, ct).ConfigureAwait(false);
        var (view, chart) = BuildSeriesRatioView(rows, "記号率", "記号");
        var layout = MakeLayout("シリーズ別 記号率", "シリーズ別 記号率");
        _page.RenderAndWrite("/stats/subtitles/symbol-rate-by-series/", "stats", "stats-subtitles-symbol-rate-by-series.sbn", new { Rows = view, ChartSvg = chart, CoverageLabel = _coverageLabel }, layout);
    }

    // シリーズ別 集計表 3 ページ（分割、平均文字数・漢字率・記号率の 3 ページと並ぶ）

    /// <summary>シリーズ別 文字種別比率（漢字 / ひらがな / カタカナ / 英字 / 数字）。</summary>
    private async Task GenerateCharTypesBySeriesAsync(CancellationToken ct)
    {
        var raw = await _repo.GetCharTypeBreakdownBySeriesAsync(ct).ConfigureAwait(false);

        // 文字種別比率行の整形（カウント値からパーセント表記を派生させる）。
        var view = raw.Select(r => new
        {
            r.SeriesTitle,
            SeriesStartYearLabel = _ctx.StartYearLabelBySlug(r.SeriesSlug),
            r.Kanji,
            r.Hiragana,
            r.Katakana,
            r.Latin,
            r.Digits,
            r.Symbols,
            r.TotalCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            KanjiPercent    = r.TotalCount > 0 ? r.Kanji    * 100.0 / r.TotalCount : 0.0,
            HiraganaPercent = r.TotalCount > 0 ? r.Hiragana * 100.0 / r.TotalCount : 0.0,
            KatakanaPercent = r.TotalCount > 0 ? r.Katakana * 100.0 / r.TotalCount : 0.0,
            LatinPercent    = r.TotalCount > 0 ? r.Latin    * 100.0 / r.TotalCount : 0.0,
            DigitsPercent   = r.TotalCount > 0 ? r.Digits   * 100.0 / r.TotalCount : 0.0,
            SymbolsPercent  = r.TotalCount > 0 ? r.Symbols  * 100.0 / r.TotalCount : 0.0,
        }).ToList();

        var content = new { Rows = view, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("シリーズ別 文字種別比率", "シリーズ別 文字種別比率");
        _page.RenderAndWrite("/stats/subtitles/char-types-by-series/", "stats", "stats-subtitles-char-types-by-series.sbn", content, layout);
    }

    /// <summary>
    /// シリーズ別 記号出現回数。
    /// 記号集計の方針:
    /// <list type="number">
    ///   <item><description>
    ///     <see cref="SubtitleStatsRepository.GetSymbolsByFirstAppearAsync"/> で全記号を「初出が早い順」で取得し、
    ///     これを列順序の正典とする（初出 = 放送日 → episode_id の昇順）。
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="SubtitleStatsRepository.GetSeriesSymbolCellsAsync"/> でシリーズ × 記号のフラットセル群を取得し、
    ///     <c>(SeriesId, Char) → Cnt</c> の辞書に物理化する。
    ///   </description></item>
    ///   <item><description>
    ///     シリーズ単位に、上で確定した記号配列と同じ順で並んだセル値配列を組み立ててテンプレに渡す。
    ///   </description></item>
    /// </list>
    /// シリーズの並びは <c>SeriesId</c> 昇順（既存セル取得 SQL の <c>ORDER BY</c> をそのまま踏襲）。
    /// 「記号」の判定は <see cref="SubtitleStatsRepository.GetSymbolsByFirstAppearAsync"/> と同じ
    /// REGEXP（漢字・々・ひらがな・カタカナ・英字・数字・空白でない文字）に揃えてあるので、
    /// 列ヘッダと本体セルで「ある記号は出るのに別の記号は出ない」のような取りこぼしは起きない。
    /// </summary>
    private async Task GenerateSymbolsBySeriesAsync(CancellationToken ct)
    {
        // 1) 列順序の正典：全記号を初出順で取得。
        //    SQL 側で ORDER BY FirstBroadcastDate, FirstEpisodeId, Char 済みなので、ここでは並び替え不要。
        var symbolsInOrder = await _repo.GetSymbolsByFirstAppearAsync(ct).ConfigureAwait(false);
        var symbolCount = symbolsInOrder.Count;
        // テンプレで使う列ヘッダ用の生文字列配列（重複は SQL レベルで除去済み）。
        var symbolChars = symbolsInOrder.Select(s => s.Char).ToList();

        // 2) シリーズ × 記号のフラットセルを取得し、(SeriesId, Char) キーの辞書に物理化。
        //    Char キーは Ordinal 比較（utf8mb4_bin と整合）で、サロゲートペア絵文字なども確実に同一視できる。
        var cells = await _repo.GetSeriesSymbolCellsAsync(ct).ConfigureAwait(false);
        var cellMap = cells.ToDictionary(
            c => (c.SeriesId, c.Char),
            c => c.Cnt);
        long maxCell = cells.Count > 0 ? cells.Max(c => c.Cnt) : 0;

        // 3) シリーズごとに、列順序と同じ並びでセル値配列を組み立てる。
        //    シリーズ並びは cells を SeriesId 昇順でグルーピングして拾う
        //    （SeriesSlug でメタ情報を引き当て、ResolveStartYearLabel で年度ラベルを解決する）。
        var seriesView = cells
            .GroupBy(c => new { c.SeriesId, c.SeriesTitle, c.SeriesSlug })
            .OrderBy(g => g.Key.SeriesId)
            .Select(g => new
            {
                g.Key.SeriesTitle,
                SeriesStartYearLabel = _ctx.StartYearLabelBySlug(g.Key.SeriesSlug),
                // 列順序（symbolChars）と同じインデックス対応で、当該シリーズの当該記号カウントを並べる。
                // セルが無い（=その記号が一度も使われていない）ケースは 0。
                // セルは回数と、回数に応じた地色の濃さ（表でいちばん多い回数を最も濃くする。0 回は塗らない）。
                Cells = symbolChars
                    .Select(ch => cellMap.TryGetValue((g.Key.SeriesId, ch), out var v) ? v : 0L)
                    .Select(n => new { N = n, Shade = CellShade(n, maxCell) })
                    .ToList()
            })
            .ToList();

        var content = new
        {
            Symbols = symbolChars,
            SymbolCount = symbolCount,
            Rows = seriesView,
            CoverageLabel = _coverageLabel
        };
        // ページタイトルは列数を動的に埋め込む（「シリーズ別 記号 23 種出現回数」など）。
        // パンくず用の短縮ラベルは固定（「シリーズ別 記号出現回数」）で、列数の変化が断片化したリンクテキストにならないようにする。
        var layout = MakeLayout($"シリーズ別 記号 {symbolCount} 種出現回数", "シリーズ別 記号出現回数");
        _page.RenderAndWrite("/stats/subtitles/symbols-by-series/", "stats", "stats-subtitles-symbols-by-series.sbn", content, layout);
    }

    /// <summary>
    /// 記号の出現回数の表の、セルの地色の濃さ（アクセント色の不透明度。style 属性用）。少ない回数でも色が見えるよう、
    /// 回数の比の平方根で濃くする。0 回は空文字（塗らない）。
    /// </summary>
    private static string CellShade(long n, long max)
        => n <= 0 || max <= 0
            ? ""
            : (0.08 + 0.52 * Math.Sqrt(n / (double)max)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>シリーズ別 TOP5 文字（DENSE_RANK で「同点同順、次は連番」、各シリーズ TOP5）。</summary>
    private async Task GenerateTopCharsBySeriesAsync(CancellationToken ct)
    {
        var raw = await _repo.GetTopCharsBySeriesAsync(5, ct).ConfigureAwait(false);

        // シリーズごとにネスト構造に整形。シリーズ並びは SeriesId 昇順固定。
        // 後段：「年度」列を独立表示するため、_ctx.SeriesById から StartDate.Year を引き当てて文字列で詰める。
        var view = raw
            .GroupBy(r => new { r.SeriesId, r.SeriesTitle, r.SeriesSlug })
            .OrderBy(g => g.Key.SeriesId)
            .Select(g => new
            {
                g.Key.SeriesTitle,
                SeriesStartYearLabel = _ctx.SeriesById.TryGetValue(g.Key.SeriesId, out var sObj)
                    ? sObj.StartDate.Year.ToString()
                    : "",
                TopChars = g.Select(c => new { c.Char, c.Total, c.Rank }).ToList()
            })
            .ToList();

        var content = new { Rows = view, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("シリーズ別 TOP5 文字", "シリーズ別 TOP5 文字");
        _page.RenderAndWrite("/stats/subtitles/top-chars-by-series/", "stats", "stats-subtitles-top-chars-by-series.sbn", content, layout);
    }

    /// <summary>
    /// シリーズ別 漢字 TOP5。<see cref="GenerateTopCharsBySeriesAsync"/> と同一の整形ロジックで、
    /// 集計対象を漢字（＋繰り返し記号「々」）限定にしたもの（リポジトリ側の漢字フィルタで実現）。
    /// </summary>
    private async Task GenerateTopKanjiBySeriesAsync(CancellationToken ct)
    {
        var raw = await _repo.GetTopKanjiBySeriesAsync(5, ct).ConfigureAwait(false);

        // シリーズごとにネスト構造に整形。シリーズ並びは SeriesId 昇順固定。
        // 後段：「年度」列を独立表示するため、_ctx.SeriesById から StartDate.Year を引き当てて文字列で詰める。
        var view = raw
            .GroupBy(r => new { r.SeriesId, r.SeriesTitle, r.SeriesSlug })
            .OrderBy(g => g.Key.SeriesId)
            .Select(g => new
            {
                g.Key.SeriesTitle,
                SeriesStartYearLabel = _ctx.SeriesById.TryGetValue(g.Key.SeriesId, out var sObj)
                    ? sObj.StartDate.Year.ToString()
                    : "",
                TopChars = g.Select(c => new { c.Char, c.Total, c.Rank }).ToList()
            })
            .ToList();

        var content = new { Rows = view, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("シリーズ別 TOP5 漢字", "シリーズ別 TOP5 漢字");
        _page.RenderAndWrite("/stats/subtitles/top-kanji-by-series/", "stats", "stats-subtitles-top-kanji-by-series.sbn", content, layout);
    }

    // ヘルパー

    /// <summary>サブタイトル統計の各詳細ページ用の標準レイアウトを生成する。</summary>
    private static LayoutModel MakeLayout(string pageTitle, string breadcrumbLabel)
        => StatsPageLayout.Make(
            pageTitle, "。プリキュア全シリーズのサブタイトルを対象にした統計です。",
            "歴代サブタイトル統計", "/stats/subtitles/", breadcrumbLabel);
}