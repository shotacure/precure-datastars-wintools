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

    /// <summary>
    /// 当ジェネレータが生成する全ページに付与するカバレッジラベル
    /// （「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記）。
    /// サブタイトル統計は「サブタイトル本文が登録済みの最新 TV エピソード」を判定軸とする。
    /// <see cref="GenerateAsync"/> 開始時に算出して全 RenderAndWrite 呼び出しで使い回す。
    /// </summary>
    private string _coverageLabel = "";

    public SubtitleStatsGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _repo = new SubtitleStatsRepository(factory);
    }

    /// <summary>
    /// シリーズ slug から開始年（西暦 4 桁文字列）を引き当てる。テンプレ側のテーブル列
    /// 「年度」（または「初出年」）用（v1.3.0 stage22 後段で追加）。
    /// </summary>
    private string ResolveStartYearLabel(string seriesSlug)
        => _ctx.SeriesIdBySlug.TryGetValue(seriesSlug, out var sid)
            && _ctx.SeriesById.TryGetValue(sid, out var sObj)
            ? sObj.StartDate.Year.ToString()
            : "";

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating subtitle stats");

        // カバレッジラベルを先に算出。BuildContext のシリーズ・エピソードを走査して
        // サブタイトル本文が登録済みの最新 TV エピソードを 1 件特定する。
        var latest = StatsCoverageLabel.FindLatestTvEpisodeWithSubtitle(_ctx);
        _coverageLabel = StatsCoverageLabel.Build(latest);

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

        // ── シリーズ別 6 ページ（v1.3.0 ブラッシュアップ続編で「その他」から「シリーズ別」にリネームし統合） ──
        // 旧来は「シリーズ単位 多い順 / 少ない順」「高い順 / 低い順」に分かれていたが、
        // 内容が同じテーブルの逆順なので「多い順 / 高い順」だけ残し、ラベルを「シリーズ別 ○○」に統一した。
        await GenerateAvgLengthBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateKanjiRateBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateSymbolRateBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateCharTypesBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateSymbolsBySeriesAsync(ct).ConfigureAwait(false);
        await GenerateTopCharsBySeriesAsync(ct).ConfigureAwait(false);

        _ctx.Logger.Success("subtitles: 16 ページ");
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
        _page.RenderAndWrite("/stats/subtitles/", "stats", "stats-subtitles-index.sbn", new { CoverageLabel = _coverageLabel }, layout);
    }

    // ──────────────────────────────────────────────────────
    // 使用文字
    // ──────────────────────────────────────────────────────

    private async Task GenerateCharsAllAsync(CancellationToken ct)
    {
        var rows = await _repo.GetCharRankingAllAsync(Limit, ct).ConfigureAwait(false);
        var content = new { Rows = rows, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("使用文字 TOP 100（全文字）", "全文字");
        _page.RenderAndWrite("/stats/subtitles/chars/all/", "stats", "stats-subtitles-chars-all.sbn", content, layout);
    }

    private async Task GenerateCharsKanjiAsync(CancellationToken ct)
    {
        var rows = await _repo.GetCharRankingKanjiAsync(Limit, ct).ConfigureAwait(false);
        var content = new { Rows = rows, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("使用文字 TOP 100（漢字限定）", "漢字限定");
        _page.RenderAndWrite("/stats/subtitles/chars/kanji/", "stats", "stats-subtitles-chars-kanji.sbn", content, layout);
    }

    private async Task GenerateSymbolsByFirstAppearAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSymbolsByFirstAppearAsync(ct).ConfigureAwait(false);
        // 表示用：初使用エピソードのシリーズ・話数・サブタイトル・放送日を「初使用」グループ列の各セルに展開。
        var view = rows.Select(r => new
        {
            r.Char,
            r.TotalCount,
            FirstSeriesTitle = r.FirstSeriesTitle,
            // v1.3.0 stage22 後段：「初出年」列用。
            FirstSeriesStartYearLabel = ResolveStartYearLabel(r.FirstSeriesSlug),
            FirstSeriesEpNo  = r.FirstSeriesEpNo,
            FirstTitleText   = r.FirstTitleText,
            FirstEpisodeUrl  = PathUtil.EpisodeUrl(r.FirstSeriesSlug, r.FirstSeriesEpNo),
            FirstBroadcastDateLabel = r.FirstBroadcastDate.HasValue
                ? $"{r.FirstBroadcastDate.Value.Year}年{r.FirstBroadcastDate.Value.Month}月{r.FirstBroadcastDate.Value.Day}日"
                : ""
        }).ToList();
        var content = new { Rows = view, CoverageLabel = _coverageLabel };
        // v1.3.0 ブラッシュアップ続編：ページタイトルを簡潔に「記号出現回数」のみに。
        var layout = MakeLayout("記号出現回数", "記号出現回数");
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
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.SeriesEpNo,
            r.TitleText,
            r.Value,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo)
        }).ToList();
        string slug = ascending ? "least" : "most";
        string label = ascending ? "少ない順" : "多い順";
        string url = $"/stats/subtitles/length/episode/{slug}/";
        var layout = MakeLayout($"エピソード単位 文字数 {label} TOP 100", $"文字数 {label}");
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-length-episode-{slug}.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    /// <summary>
    /// シリーズ別 平均文字数（多い順）。
    /// v1.3.0 ブラッシュアップ続編で「シリーズ単位 多い順 / 少ない順」の 2 ページから多い順 1 ページに集約し、
    /// 「シリーズ別」グループの 1 つとして再配置。少ない順は同じテーブルの逆順なので削除。
    /// </summary>
    private async Task GenerateAvgLengthBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSeriesAverageLengthAsync(ascending: false, Limit, ct).ConfigureAwait(false);
        // シリーズ URL 解決と平均値ラベル整形（小数 1 桁）
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.EpisodeCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            AverageLabel = r.Average.ToString("0.0")
        }).ToList();
        var layout = MakeLayout("シリーズ別 平均文字数", "シリーズ別 平均文字数");
        _page.RenderAndWrite("/stats/subtitles/avg-length-by-series/", "stats", "stats-subtitles-avg-length-by-series.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
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
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
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
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-kanji-rate-episode-{slug}.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    /// <summary>
    /// シリーズ別 漢字率（高い順）。
    /// v1.3.0 ブラッシュアップ続編で「シリーズ単位 高い順 / 低い順」の 2 ページから高い順 1 ページに集約し、
    /// 「シリーズ別」グループの 1 つとして再配置。低い順は同じテーブルの逆順なので削除。
    /// </summary>
    private async Task GenerateKanjiRateBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetKanjiRateSeriesAsync(ascending: false, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.KanjiCount,
            r.TotalCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            RatioPercent = r.Ratio * 100.0
        }).ToList();
        var layout = MakeLayout("シリーズ別 漢字率", "シリーズ別 漢字率");
        _page.RenderAndWrite("/stats/subtitles/kanji-rate-by-series/", "stats", "stats-subtitles-kanji-rate-by-series.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
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
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
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
        _page.RenderAndWrite(url, "stats", $"stats-subtitles-symbol-rate-episode-{slug}.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    /// <summary>
    /// シリーズ別 記号率（高い順）。
    /// v1.3.0 ブラッシュアップ続編で「シリーズ単位 高い順 / 低い順」の 2 ページから高い順 1 ページに集約し、
    /// 「シリーズ別」グループの 1 つとして再配置。低い順は同じテーブルの逆順なので削除。
    /// </summary>
    private async Task GenerateSymbolRateBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetSymbolRateSeriesAsync(ascending: false, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.KanjiCount,
            r.TotalCount,
            SeriesUrl = PathUtil.SeriesUrl(r.SeriesSlug),
            RatioPercent = r.Ratio * 100.0
        }).ToList();
        var layout = MakeLayout("シリーズ別 記号率", "シリーズ別 記号率");
        _page.RenderAndWrite("/stats/subtitles/symbol-rate-by-series/", "stats", "stats-subtitles-symbol-rate-by-series.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    // ──────────────────────────────────────────────────────
    // シリーズ別 集計表 3 ページ（v1.3.0 ブラッシュアップ続編で分割、平均文字数・漢字率・記号率の 3 ページと並ぶ）
    // ──────────────────────────────────────────────────────

    /// <summary>シリーズ別 文字種別比率（漢字 / ひらがな / カタカナ / 英字 / 数字）。</summary>
    private async Task GenerateCharTypesBySeriesAsync(CancellationToken ct)
    {
        var raw = await _repo.GetCharTypeBreakdownBySeriesAsync(ct).ConfigureAwait(false);

        // 文字種別比率行の整形（カウント値からパーセント表記を派生させる）。
        var view = raw.Select(r => new
        {
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
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

        var content = new { Rows = view, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("シリーズ別 文字種別比率", "シリーズ別 文字種別比率");
        _page.RenderAndWrite("/stats/subtitles/char-types-by-series/", "stats", "stats-subtitles-char-types-by-series.sbn", content, layout);
    }

    /// <summary>シリーズ別 記号 16 種出現回数。</summary>
    private async Task GenerateSymbolsBySeriesAsync(CancellationToken ct)
    {
        var raw = await _repo.GetSymbolCountsBySeriesAsync(ct).ConfigureAwait(false);

        // 記号 16 種は Symbols プロパティを 1 つの構造体ライクに渡す（テンプレ側で r.Symbols.Exclamation 等で参照）。
        var view = raw.Select(r => new
        {
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            Symbols = r
        }).ToList();

        var content = new { Rows = view, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("シリーズ別 記号 16 種出現回数", "シリーズ別 記号出現回数");
        _page.RenderAndWrite("/stats/subtitles/symbols-by-series/", "stats", "stats-subtitles-symbols-by-series.sbn", content, layout);
    }

    /// <summary>シリーズ別 TOP5 文字（DENSE_RANK で「同点同順、次は連番」、各シリーズ TOP5）。</summary>
    private async Task GenerateTopCharsBySeriesAsync(CancellationToken ct)
    {
        var raw = await _repo.GetTopCharsBySeriesAsync(5, ct).ConfigureAwait(false);

        // シリーズごとにネスト構造に整形。シリーズ並びは SeriesId 昇順固定。
        // v1.3.0 stage22 後段：「年度」列を独立表示するため、_ctx.SeriesById から StartDate.Year を引き当てて文字列で詰める。
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
