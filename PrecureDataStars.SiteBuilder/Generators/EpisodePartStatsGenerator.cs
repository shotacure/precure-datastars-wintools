using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// エピソード尺・CM 入り時刻統計のページ群を生成するジェネレータ
/// （v1.3.0 ブラッシュアップ続編で 8 ページ構成に再編）。
/// <para>
/// 1 ページ 1 ランキング厳守の方針で、旧 4 ページ（part-a-length / part-b-length 統合 / cm-time / by-series）を
/// 7 詳細ページ + 1 ランディングに分解した。
/// </para>
/// <list type="bullet">
///   <item><description>A パート尺 長い順 / 短い順</description></item>
///   <item><description>B パート尺 長い順 / 短い順</description></item>
///   <item><description>中 CM 入り時刻 早い順 / 遅い順</description></item>
///   <item><description>シリーズ × パート別 平均/最短/最長（series-summary）</description></item>
/// </list>
/// <para>
/// シリーズサマリーページの平均値表記は <c>1:30.22</c> の小数部分のみフォントサイズ 80% で
/// 表示する（テンプレ側で <c>&lt;span class="micro-fraction"&gt;</c> でラップ）。
/// </para>
/// </summary>
public sealed class EpisodePartStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly EpisodePartStatsRepository _repo;

    /// <summary>1 ページあたりの最大件数（TOP 100）。</summary>
    private const int Limit = 100;

    /// <summary>番組開始基準時刻（08:30:00）。中 CM 入り時刻の絶対時刻表示で使用。</summary>
    private static readonly TimeSpan ProgramStart = new TimeSpan(8, 30, 0);

    /// <summary>
    /// 当ジェネレータが生成する全ページに付与するカバレッジラベル
    /// （「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記）。
    /// エピソード尺統計は「パート情報が登録済みの最新 TV エピソード」を判定軸とする。
    /// <see cref="GenerateAsync"/> 開始時に算出して全 RenderAndWrite 呼び出しで使い回す。
    /// </summary>
    private string _coverageLabel = "";

    public EpisodePartStatsGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _repo = new EpisodePartStatsRepository(factory);
    }

    /// <summary>
    /// シリーズ slug から開始年（西暦 4 桁文字列）を引き当てる。テンプレ側のテーブル列
    /// 「年度」用（v1.3.0 stage22 後段で追加）。
    /// <c>BuildContext.SeriesIdBySlug</c> → <c>SeriesById</c> の 2 段引きで解決し、
    /// 解決失敗（マスタ未登録の異常系）は空文字を返す。
    /// </summary>
    private string ResolveStartYearLabel(string seriesSlug)
        => _ctx.SeriesIdBySlug.TryGetValue(seriesSlug, out var sid)
            && _ctx.SeriesById.TryGetValue(sid, out var sObj)
            ? sObj.StartDate.Year.ToString()
            : "";

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating episode part stats");

        // パート情報を持つ episode_id 集合を 1 クエリで取得し、最新 TV エピソードを判定する。
        var episodeIdsWithParts = (await _repo.GetEpisodeIdsWithPartsAsync(ct).ConfigureAwait(false)).ToHashSet();
        var latest = StatsCoverageLabel.FindLatestTvEpisodeWithParts(_ctx, episodeIdsWithParts);
        _coverageLabel = StatsCoverageLabel.Build(latest);

        // 索引
        GenerateIndex();

        // パート尺ランキング A/B × 長短 = 4 ページ
        await GeneratePartLengthAsync(ct, "PART_A", "A パート", ascending: false).ConfigureAwait(false);
        await GeneratePartLengthAsync(ct, "PART_A", "A パート", ascending: true).ConfigureAwait(false);
        await GeneratePartLengthAsync(ct, "PART_B", "B パート", ascending: false).ConfigureAwait(false);
        await GeneratePartLengthAsync(ct, "PART_B", "B パート", ascending: true).ConfigureAwait(false);

        // アバンタイトル尺 × 長短 = 2 ページ + アバンスキップ回 1 ページ（v1.3.0 ブラッシュアップ続編で追加）
        await GenerateAvantLengthAsync(ct, ascending: false).ConfigureAwait(false);
        await GenerateAvantLengthAsync(ct, ascending: true).ConfigureAwait(false);
        await GenerateAvantSkippedAsync(ct).ConfigureAwait(false);

        // 中 CM 入り時刻 × 早遅 = 2 ページ
        await GenerateMidCmAsync(ct, ascending: true).ConfigureAwait(false);
        await GenerateMidCmAsync(ct, ascending: false).ConfigureAwait(false);

        // シリーズ × パート別 = 1 ページ
        await GenerateSeriesSummaryAsync(ct).ConfigureAwait(false);

        _ctx.Logger.Success("episode parts: 11 ページ");
    }

    // ──────────────────────────────────────────────────────
    // 索引
    // ──────────────────────────────────────────────────────

    private void GenerateIndex()
    {
        var layout = new LayoutModel
        {
            PageTitle = "エピソード尺統計",
            MetaDescription = "プリキュア全シリーズの本編パート尺・中 CM 入り時刻を集計した統計。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "エピソード尺統計", Url = "" }
            }
        };
        _page.RenderAndWrite("/stats/episodes/", "stats", "stats-episodes-index.sbn", new { CoverageLabel = _coverageLabel }, layout);
    }

    // ──────────────────────────────────────────────────────
    // パート尺
    // ──────────────────────────────────────────────────────

    /// <summary>A / B パート尺ランキングを 1 ページ生成。</summary>
    private async Task GeneratePartLengthAsync(CancellationToken ct, string partType, string partLabel, bool ascending)
    {
        var rows = await _repo.GetPartLengthRankingAsync(partType, ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            // v1.3.0 stage22 後段：表に「年度」列を独立表示するため、シリーズ列の直後で読まれる西暦 4 桁を詰める。
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.SeriesEpNo,
            r.TitleText,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
            // 「m:ss」表記。秒は整数表示（小数があれば切り捨て）。
            LengthLabel = FormatMmSs(r.LengthSeconds)
        }).ToList();

        // URL スラッグ：part-a / part-b、longest / shortest
        string partSlug = partType.ToLowerInvariant().Replace("part_", "part-");  // part-a / part-b
        string orderSlug = ascending ? "shortest" : "longest";
        string orderLabel = ascending ? "短い順" : "長い順";
        string url = $"/stats/episodes/{partSlug}/{orderSlug}/";
        string templateName = $"stats-episodes-{partSlug}-{orderSlug}.sbn";

        var layout = MakeLayout($"{partLabel}尺 {orderLabel} TOP 100", $"{partLabel}尺 {orderLabel}");
        _page.RenderAndWrite(url, "stats", templateName, new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    // ──────────────────────────────────────────────────────
    // アバンタイトル（v1.3.0 ブラッシュアップ続編で追加）
    // ──────────────────────────────────────────────────────

    /// <summary>
    /// アバンタイトル尺ランキング 1 ページ。長短は ascending で切替。
    /// 内部では PART_A / PART_B と同じ <see cref="EpisodePartStatsRepository.GetPartLengthRankingAsync"/>
    /// を partType="AVANT" で呼ぶ薄いラッパ。
    /// </summary>
    private async Task GenerateAvantLengthAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetPartLengthRankingAsync("AVANT", ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.SeriesEpNo,
            r.TitleText,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
            LengthLabel = FormatMmSs(r.LengthSeconds)
        }).ToList();

        string orderSlug = ascending ? "shortest" : "longest";
        string orderLabel = ascending ? "短い順" : "長い順";
        string url = $"/stats/episodes/avant/{orderSlug}/";
        string templateName = $"stats-episodes-avant-{orderSlug}.sbn";

        var layout = MakeLayout($"アバンタイトル尺 {orderLabel} TOP 100", $"アバンタイトル尺 {orderLabel}");
        _page.RenderAndWrite(url, "stats", templateName, new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    /// <summary>
    /// アバンタイトルが設定されていないエピソード（アバンスキップ回）の一覧を放映順に全件出力する。
    /// 件数制限なし（アバンスキップ回はそんなに多くないはずなので TOP 100 で打ち切らない）。
    /// </summary>
    private async Task GenerateAvantSkippedAsync(CancellationToken ct)
    {
        var rows = await _repo.GetEpisodesWithoutPartAsync("AVANT", ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.SeriesEpNo,
            r.TitleText,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo)
        }).ToList();

        var layout = MakeLayout("アバンタイトルスキップ回", "アバンスキップ回");
        _page.RenderAndWrite("/stats/episodes/avant/skipped/", "stats", "stats-episodes-avant-skipped.sbn", new { Rows = view, CoverageLabel = _coverageLabel }, layout);
    }

    // ──────────────────────────────────────────────────────
    // 中 CM 入り時刻
    // ──────────────────────────────────────────────────────

    private async Task GenerateMidCmAsync(CancellationToken ct, bool ascending)
    {
        var rows = await _repo.GetCmTimeRankingAsync(ascending, Limit, ct).ConfigureAwait(false);
        var view = rows.Select(r => new
        {
            r.Rank,
            r.SeriesTitle,
            SeriesStartYearLabel = ResolveStartYearLabel(r.SeriesSlug),
            r.SeriesEpNo,
            r.TitleText,
            EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
            // v1.3.0 ブラッシュアップ続編：絶対時刻のみテンプレに渡す。表記は「h:mm:ss」（先頭時の零埋め無し）。
            // 経過時間（番組開始からの相対 m:ss）は絶対時刻から自明に読み取れるため、表示列を撤廃した。
            CmEnterTimeLabel = FormatAbsoluteTime(r.Cm2OffsetSeconds)
        }).ToList();

        string slug = ascending ? "earliest" : "latest";
        string label = ascending ? "早い順" : "遅い順";
        string url = $"/stats/episodes/midcm/{slug}/";
        var content = new
        {
            Rows = view,
            CoverageLabel = _coverageLabel
        };
        var layout = MakeLayout($"中 CM 入り {label} TOP 100", $"中 CM 入り {label}");
        _page.RenderAndWrite(url, "stats", $"stats-episodes-midcm-{slug}.sbn", content, layout);
    }

    // ──────────────────────────────────────────────────────
    // シリーズ × パート別 平均/最短/最長
    // ──────────────────────────────────────────────────────

    private async Task GenerateSeriesSummaryAsync(CancellationToken ct)
    {
        var rows = await _repo.GetPartAveragesBySeriesAsync(ct).ConfigureAwait(false);

        // シリーズ ID 単位にグルーピング。シリーズ内はパート display_order 昇順。
        // v1.3.0 ブラッシュアップ続編：シリーズの並びは SeriesId 昇順（放送順に対応する）固定とし、
        // タイトル文字列順（50 音順・コード順）には並べ替えない。
        // v1.3.0 stage22 後段：略称（series.title_short）は生成・UI ともに使わない。タイトル列は
        // クエリ側から渡る正式タイトル（series.title）一本。見出しの隣に薄色括弧で開始年を添える
        // 仕様のため、_ctx.SeriesById から StartDate.Year を引き当てて SeriesStartYearLabel を詰める。
        var groups = rows
            .GroupBy(r => new { r.SeriesId, r.SeriesTitle, r.SeriesSlug })
            .Select(g => new
            {
                g.Key.SeriesId,
                g.Key.SeriesTitle,
                SeriesUrl = PathUtil.SeriesUrl(g.Key.SeriesSlug),
                SeriesStartYearLabel = _ctx.SeriesById.TryGetValue(g.Key.SeriesId, out var sObj)
                    ? sObj.StartDate.Year.ToString()
                    : "",
                Parts = g.OrderBy(p => p.PartDisplayOrder ?? byte.MaxValue)
                         .Select(p =>
                         {
                             // 平均値の小数部分を分離して micro-fraction 表記に渡す。
                             // p.AvgSeconds は秒数（小数あり）。「1:30.22」のように m:ss.fraction で返したいので
                             // 整数部 = m:ss（秒は整数桁）、小数部 = .fraction（2 桁）。
                             var (intPart, fracPart) = SplitMmSsFraction(p.AvgSeconds);
                             return new
                             {
                                 p.PartLabel,
                                 p.OccurrenceCount,
                                 AvgIntegerPart = intPart,
                                 AvgFractionPart = fracPart,
                                 MinLabel = FormatMmSs(p.MinSeconds),
                                 MaxLabel = FormatMmSs(p.MaxSeconds)
                             };
                         })
                         .ToList()
            })
            .OrderBy(x => x.SeriesId)
            .ToList();

        var content = new { Groups = groups, CoverageLabel = _coverageLabel };
        var layout = MakeLayout("シリーズ × パート別 平均/最短/最長", "シリーズ × パート別");
        _page.RenderAndWrite("/stats/episodes/series-summary/", "stats", "stats-episodes-series-summary.sbn", content, layout);
    }

    // ──────────────────────────────────────────────────────
    // 整形ヘルパー
    // ──────────────────────────────────────────────────────

    /// <summary>秒数を「m:ss」形式に整形。負数や 0 もそのまま処理。</summary>
    private static string FormatMmSs(double seconds)
    {
        int total = (int)Math.Round(seconds);
        int min = total / 60;
        int sec = total % 60;
        return $"{min}:{sec:D2}";
    }

    /// <summary>
    /// 平均値秒数を「整数部 (m:ss) と小数部 (.22)」に分離して返す。
    /// テンプレ側で小数部のみ <c>&lt;span class="micro-fraction"&gt;</c> でラップして縮小表示するため。
    /// 小数 2 桁固定（v1.3.0 ブラッシュアップ続編で 3 桁から 2 桁に短縮）。
    /// 例: 90.5 秒 → ("1:30", ".50")、123.456 秒 → ("2:03", ".46")。
    /// </summary>
    private static (string IntegerPart, string FractionPart) SplitMmSsFraction(double seconds)
    {
        int totalIntSeconds = (int)Math.Floor(seconds);
        int min = totalIntSeconds / 60;
        int sec = totalIntSeconds % 60;
        double fraction = seconds - totalIntSeconds;
        // 小数 2 桁。0.00 のときは「.00」と表示（micro-fraction でも残す）。
        string fracStr = "." + ((int)Math.Round(fraction * 100)).ToString("D2");
        return ($"{min}:{sec:D2}", fracStr);
    }

    /// <summary>
    /// 番組開始（08:30:00）から経過 N 秒の絶対時刻を「h:mm:ss」表記で返す。
    /// 時の部分は零埋めしない（v1.3.0 ブラッシュアップ続編：HH:MM:SS から h:mm:ss に変更）。
    /// 例：番組開始から 38 分 30 秒経過 → 9:08:30。
    /// </summary>
    private static string FormatAbsoluteTime(double offsetSeconds)
    {
        var t = ProgramStart + TimeSpan.FromSeconds(offsetSeconds);
        return $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    /// <summary>エピソード尺統計の各詳細ページ用の標準レイアウトを生成する。</summary>
    private static LayoutModel MakeLayout(string pageTitle, string breadcrumbLabel)
    {
        return new LayoutModel
        {
            PageTitle = pageTitle,
            MetaDescription = pageTitle + "(プリキュア全シリーズのエピソード尺統計)。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "エピソード尺統計", Url = "/stats/episodes/" },
                new BreadcrumbItem { Label = breadcrumbLabel, Url = "" }
            }
        };
    }
}
