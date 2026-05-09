
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サブタイトル統計のページ群を生成するジェネレータ
/// （v1.3.0 ブラッシュアップ続編で 17 ページ構成に再編）。
/// <para>
/// 1 ページ 1 ランキング厳守の方針で、旧 4 ページ（char-ranking / length-ranking / kanji-ratio / by-series）を
/// 16 詳細ページ + 1 ランディングに分解した。シリーズ単位の集計は TV のみ対象（series.kind_code = 'TV'）で、
/// スピンオフ・映画は除外する。
/// </para>
/// <para>
/// 集計の元データは <c>episodes.title_char_stats</c>（JSON 列）に保存されている文字種別カウンタ。
/// 全クエリは <see cref="SubtitleStatsRepository"/> 経由で SQL を発行する。
/// </para>
/// </summary>
public sealed class SubtitleStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly SubtitleStatsRepository _repo;

    /// <summary>1 ページあたりの最大件数（TOP 100）。</summary>
    private const int Limit = 100;

    public SubtitleStatsGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _repo = new SubtitleStatsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating subtitle stats");

        // ── 索引 ──
        GenerateIndex();

        // ── 使用文字 ──
        await GenerateCharsAllAsync(ct).ConfigureAwait(false);
        await GenerateCharsKanjiAsync(ct).ConfigureAwait(false);
        await GenerateSymbolsByFirstAppearAsync(ct).ConfigureAwait(false);

        // ── 文字数（エピソード単位 / シリーズ単位） ──
        await GenerateLengthEpisodeAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateLengthEpisodeAsync(ct, ascending: true).ConfigureAwait(false);
        await GenerateLengthSeriesAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateLengthSeriesAsync(ct, ascending: true).ConfigureAwait(false);

        // ── 漢字率（エピソード単位 / シリーズ単位） ──
        await GenerateKanjiRateEpisodeAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateKanjiRateEpisodeAsync(ct, ascending: true).ConfigureAwait(false);
        await GenerateKanjiRateSeriesAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateKanjiRateSeriesAsync(ct, ascending: true).ConfigureAwait(false);

        // ── 記号率（エピソード単位 / シリーズ単位） ──
        await GenerateSymbolRateEpisodeAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateSymbolRateEpisodeAsync(ct, ascending: true).ConfigureAwait(false);
        await GenerateSymbolRateSeriesAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateSymbolRateSeriesAsync(ct, ascending: true).ConfigureAwait(false);

        // ── シリーズ別 文字種別比率 + 16 種記号 + TOP5 文字 ──
        await GenerateSeriesBreakdownAsync(ct).ConfigureAwait(false);

        _ctx.Logger.Success("subtitles: 17 ページ");
    }

    // ──────────────────────────────────────────────────────
    // 索引
    // ──────────────────────────────────────────────────────

    private void GenerateIndex()
    {
        var layout = new LayoutModel
        {
            PageTitle = "サブタイトル統計",
            MetaDescription = "プリキュア全シリーズのサブタイトルから文字種別・文字数・漢字率・記号率などを集計した統計。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "" }
            }
        };
        // テンプレ側はモデル不要（index は完全静的）。空オブジェクトを渡す。
        _page.RenderAndWrite("/stats/subtitles/", "stats", "stats-subtitles-index.sbn", new { }, layout);
    }

    // ──────────────────────────────────────────────────────
    // 使用文字
    // ──────────────────────────────────────────────────────

    private async Task GenerateCharsAllAsync(CancellationToken ct)
    {
        var rows = await _repo.GetCharRankingAllAsync(Limit, ct).ConfigureAwait(false);
        var content = new { Rows = rows };
        var layout = MakeLayout("使用文字 TOP 100（全文字）", "全文字");
        _page.RenderAndWrite("/stats/subtitles/chars/all/", "stats", "stats-subtitles-chars-all.sbn", content, layout);
    }

    private async Task GenerateCharsKanjiAsync(CancellationToken ct)
    {
        var rows = await _repo.GetCharRankingKanjiAsync(Limit, ct).ConfigureAwait(false);
        var content = new { Rows = rows };
        var layout = MakeLayout("使用文字 TOP 100（漢字限定）", "漢字限定");
        _page.RenderAndWrite("/stats/subtitles/chars/kanji/", "stats", "stats-subtitles-chars-kanji.sbn", content, layout);
    }

    private async Task GenerateSymbolsByFirstAppearAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSymbolsByFirstAppearAsync(ct).ConfigureAwait(false);
        // 放送日表示用のラベルを事前整形
        var view = rows.Select(r => new
        {
            r.Char,
            r.TotalCount,
            FirstBroadcastDateLabel = r.FirstBroadcastDate.HasValue
                ? $"{r.FirstBroadcastDate.Value.Year}年{r.FirstBroadcastDate.Value.Month}月{r.FirstBroadcastDate.Value.Day}日"
                : ""
        }).ToList();
        var content = new { Rows = view };
        var layout = MakeLayout("記号出現回数(放送日順での初出現順、全件)", "記号 初出現順");
        _page.RenderAndWrite("/stats/subtitles/chars/symbols-order/", "stats", "stats-subtitles-chars-symbols-order.sbn", content, layout);
    }

    // ──────────────────────────────────────────────────────
    // 文字数
    // ──────────────────────────────────────────────────────

    private async Task GenerateLengthEpisodeAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetTitleLengthRankingAsync(ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            r.SeriesEpNo,
            r.TitleText,
            r.Value,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo)
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "少ない順" : "多い順";
        string url = $"/stats/subtitles/length/episode/{slug}/";
        var layout = MakeLayout($"エピソード単位 文字数 {label} TOP 100", $"文字数 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-length-episode-{slug}.sbn", new { Rows = view }, layout);
    }

    private async Task GenerateLengthSeriesAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetSeriesAverageLengthAsync(ascending, Limit, ct).ConfigureAwait(false);
        // シリーズ URL 解決と平均値ラベル整形（小数 1 桁）
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            r.EpisodeCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            AverageLabel = r.Average.ToString("0.0")
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "少ない順" : "多い順";
        string url = $"/stats/subtitles/length/series/{slug}/";
        var layout = MakeLayout($"シリーズ単位 平均文字数 {label} TOP 100", $"平均文字数 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-length-series-{slug}.sbn", new { Rows = view }, layout);
    }

    // ──────────────────────────────────────────────────────
    // 漢字率
    // ──────────────────────────────────────────────────────

    private async Task GenerateKanjiRateEpisodeAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetKanjiRateEpisodeAsync(ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            r.SeriesEpNo,
            r.TitleText,
            r.KanjiCount,
            r.TotalCount,
            // テンプレ側で math.format "0.0" するためにパーセント値（0〜100）を渡す
            RatioPercent = r.Ratio * 100.0,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo)
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "低い順" : "高い順";
        string url = $"/stats/subtitles/kanji-rate/episode/{slug}/";
        var layout = MakeLayout($"エピソード単位 漢字率 {label} TOP 100", $"漢字率 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-kanji-rate-episode-{slug}.sbn", new { Rows = view }, layout);
    }

    private async Task GenerateKanjiRateSeriesAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetKanjiRateSeriesAsync(ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            r.KanjiCount,
            r.TotalCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            RatioPercent = r.Ratio * 100.0
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "低い順" : "高い順";
        string url = $"/stats/subtitles/kanji-rate/series/{slug}/";
        var layout = MakeLayout($"シリーズ単位 漢字率 {label} TOP 100", $"シリーズ漢字率 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-kanji-rate-series-{slug}.sbn", new { Rows = view }, layout);
    }

    // ──────────────────────────────────────────────────────
    // 記号率
    // ──────────────────────────────────────────────────────

    private async Task GenerateSymbolRateEpisodeAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetSymbolRateEpisodeAsync(ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            r.SeriesEpNo,
            r.TitleText,
            // 漢字率テンプレと共用するため、KanjiCount に記号件数（Repository 側で SymbolCount を流用済み）を入れる
            r.KanjiCount,
            r.TotalCount,
            RatioPercent = r.Ratio * 100.0,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo)
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "低い順" : "高い順";
        string url = $"/stats/subtitles/symbol-rate/episode/{slug}/";
        var layout = MakeLayout($"エピソード単位 記号率 {label} TOP 100", $"記号率 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-symbol-rate-episode-{slug}.sbn", new { Rows = view }, layout);
    }

    private async Task GenerateSymbolRateSeriesAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetSymbolRateSeriesAsync(ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            r.KanjiCount,
            r.TotalCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            RatioPercent = r.Ratio * 100.0
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "低い順" : "高い順";
        string url = $"/stats/subtitles/symbol-rate/series/{slug}/";
        var layout = MakeLayout($"シリーズ単位 記号率 {label} TOP 100", $"シリーズ記号率 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-symbol-rate-series-{slug}.sbn", new { Rows = view }, layout);
    }

    // ──────────────────────────────────────────────────────
    // シリーズ別 集計表
    // ──────────────────────────────────────────────────────

    private async Task GenerateSeriesBreakdownAsync(CancellationToken ct)
    {
        var charTypeRaw = await _repo.GetCharTypeBreakdownBySeriesAsync(ct).ConfigureAwait(false);
        var symbolRaw = await _repo.GetSymbolCountsBySeriesAsync(ct).ConfigureAwait(false);
        var topCharsRaw = await _repo.GetTopCharsBySeriesAsync(5, ct).ConfigureAwait(false);

        // 文字種別比率行の整形（旧 by-series ページのロジックを移植）。
        var charTypeView = charTypeRaw.Select(r => new
        {
            r.SeriesTitle,
            r.Kanji,
            r.Hiragana,
            r.Katakana,
            r.Latin,
            r.Digits,
            r.TotalCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            KanjiPercent    = r.TotalCount > 0 ? r.Kanji    * 100.0 / r.TotalCount : 0.0,
            HiraganaPercent = r.TotalCount > 0 ? r.Hiragana * 100.0 / r.TotalCount : 0.0,
            KatakanaPercent = r.TotalCount > 0 ? r.Katakana * 100.0 / r.TotalCount : 0.0,
            LatinPercent    = r.TotalCount > 0 ? r.Latin    * 100.0 / r.TotalCount : 0.0,
            DigitsPercent   = r.TotalCount > 0 ? r.Digits   * 100.0 / r.TotalCount : 0.0,
        }).ToList();

        // 記号 16 種は Symbols プロパティを 1 つの構造体ライクに渡す。
        var symbolView = symbolRaw.Select(r => new
        {
            r.SeriesTitle,
            Symbols = r
        }).ToList();

        // TOP5 文字はシリーズ別にネスト構造にする。
        var topCharView = topCharsRaw
            .GroupBy(r => new { r.SeriesId, r.SeriesTitle, r.SeriesSlug })
            .OrderBy(g => g.Key.SeriesId)
            .Select(g => new
            {
                g.Key.SeriesTitle,
                TopChars = g.Select(c => new { c.Char, c.Total, c.Rank }).ToList()
            })
            .ToList();

        var content = new
        {
            CharTypeRows = charTypeView,
            SymbolRows   = symbolView,
            TopCharRows  = topCharView
        };
        var layout = MakeLayout("シリーズ別文字種別比率 + 16 種記号出現回数 + TOP5 文字", "シリーズ別集計");
        _page.RenderAndWrite("/stats/subtitles/series-breakdown/", "stats", "stats-subtitles-series-breakdown.sbn", content, layout);
    }

    // ──────────────────────────────────────────────────────
    // ヘルパー
    // ──────────────────────────────────────────────────────

    /// <summary>サブタイトル統計の各詳細ページ用の標準レイアウトを生成する。</summary>
    private static LayoutModel MakeLayout(string pageTitle, string breadcrumbLabel)
    {
        return new LayoutModel
        {
            PageTitle = pageTitle,
            MetaDescription = pageTitle + "(プリキュア全シリーズのサブタイトル統計)。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "/stats/subtitles/" },
                new BreadcrumbItem { Label = breadcrumbLabel, Url = "" }
            }
        };
    }
}
