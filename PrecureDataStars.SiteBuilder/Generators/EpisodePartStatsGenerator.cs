using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// エピソード尺・CM 時刻統計ページ群（v1.3.0 後半追加）。
/// <para>
/// <c>/stats/episodes/</c> 配下の 5 ページを生成する：
/// </para>
/// <list type="bullet">
///   <item><description><c>/stats/episodes/</c> — 索引</description></item>
///   <item><description><c>/stats/episodes/part-a-length/</c> — A パート 長い順 / 短い順 の 2 タブ TOP 100</description></item>
///   <item><description><c>/stats/episodes/part-b-length/</c> — B パート 長い順 / 短い順 の 2 タブ TOP 100</description></item>
///   <item><description><c>/stats/episodes/cm-time/</c> — CM 入り 早い順 / 遅い順 の 2 タブ TOP 100</description></item>
///   <item><description><c>/stats/episodes/by-series/</c> — シリーズ × パート別の平均/最短/最長尺</description></item>
/// </list>
/// </summary>
public sealed class EpisodePartStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly EpisodePartStatsRepository _repo;

    /// <summary>ランキングの上限件数。</summary>
    private const int RankingLimit = 100;

    /// <summary>CM 入り時刻の起点（番組開始時刻）。日曜朝 8:30 起点で固定。</summary>
    private static readonly TimeSpan ProgramStartTime = new(8, 30, 0);

    public EpisodePartStatsGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _repo = new EpisodePartStatsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating episode part stats");

        await GenerateIndexAsync(ct).ConfigureAwait(false);
        await GeneratePartLengthRankingAsync("PART_A", "Aパート", "/stats/episodes/part-a-length/", "stats-episodes-part-a-length", ct).ConfigureAwait(false);
        await GeneratePartLengthRankingAsync("PART_B", "Bパート", "/stats/episodes/part-b-length/", "stats-episodes-part-b-length", ct).ConfigureAwait(false);
        await GenerateCmTimeAsync(ct).ConfigureAwait(false);
        await GenerateBySeriesAsync(ct).ConfigureAwait(false);
    }

    // ──────────────────────────────────────────────────────
    // 索引
    // ──────────────────────────────────────────────────────

    private async Task GenerateIndexAsync(CancellationToken ct)
    {
        var content = new IndexModel();
        var layout = new LayoutModel
        {
            PageTitle = "エピソード尺統計",
            MetaDescription = "プリキュア全シリーズのエピソード尺・パート尺・CM 入り時刻を集計した統計ページ群です。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "エピソード尺統計", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/episodes/", "episode-stats-index", "stats-episodes-index.sbn", content, layout);
        _ctx.Logger.Success("/stats/episodes/");
        await Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────
    // A/B パート尺ランキング（パート種別を引数で切替）
    // ──────────────────────────────────────────────────────

    private async Task GeneratePartLengthRankingAsync(
        string partType, string partLabel, string url, string templateBaseName, CancellationToken ct)
    {
        var longest = await _repo.GetPartLengthRankingAsync(partType, ascending: false, RankingLimit, ct).ConfigureAwait(false);
        var shortest = await _repo.GetPartLengthRankingAsync(partType, ascending: true, RankingLimit, ct).ConfigureAwait(false);

        var content = new PartLengthRankingModel
        {
            PartLabel = partLabel,
            Descending = longest.Select(ToPartLengthRowView).ToList(),
            Ascending = shortest.Select(ToPartLengthRowView).ToList()
        };

        var layout = new LayoutModel
        {
            PageTitle = $"{partLabel}尺ランキング",
            MetaDescription = $"プリキュア全シリーズの本編 {partLabel} の尺ランキング。長い順・短い順それぞれ TOP {RankingLimit}。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "エピソード尺統計", Url = "/stats/episodes/" },
                new BreadcrumbItem { Label = $"{partLabel}尺", Url = "" }
            }
        };

        _page.RenderAndWrite(url, templateBaseName, "stats-episodes-part-length.sbn", content, layout);
        _ctx.Logger.Success(url);
    }

    // ──────────────────────────────────────────────────────
    // CM 入り時刻ランキング
    // ──────────────────────────────────────────────────────

    private async Task GenerateCmTimeAsync(CancellationToken ct)
    {
        var earliest = await _repo.GetCmTimeRankingAsync(ascending: true, RankingLimit, ct).ConfigureAwait(false);
        var latest = await _repo.GetCmTimeRankingAsync(ascending: false, RankingLimit, ct).ConfigureAwait(false);

        var content = new CmTimeRankingModel
        {
            ProgramStartLabel = $"{ProgramStartTime:hh\\:mm\\:ss}",
            Earliest = earliest.Select(ToCmTimeRowView).ToList(),
            Latest = latest.Select(ToCmTimeRowView).ToList()
        };

        var layout = new LayoutModel
        {
            PageTitle = "中 CM 入り時刻ランキング",
            MetaDescription = $"プリキュア本編の中 CM 入り時刻ランキング（番組開始 {ProgramStartTime:hh\\:mm} 起点）。早い順・遅い順それぞれ TOP {RankingLimit}。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "エピソード尺統計", Url = "/stats/episodes/" },
                new BreadcrumbItem { Label = "中 CM 入り時刻", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/episodes/cm-time/", "episode-cm-time", "stats-episodes-cm-time.sbn", content, layout);
        _ctx.Logger.Success("/stats/episodes/cm-time/");
    }

    // ──────────────────────────────────────────────────────
    // シリーズ別パート平均尺
    // ──────────────────────────────────────────────────────

    private async Task GenerateBySeriesAsync(CancellationToken ct)
    {
        var rows = await _repo.GetPartAveragesBySeriesAsync(ct).ConfigureAwait(false);

        // (series_id, part_display_order) のグループに整形。同じシリーズの行をまとめてからテンプレに渡す。
        var grouped = rows
            .GroupBy(r => new { r.SeriesId, r.SeriesTitle, r.SeriesSlug })
            .Select(g => new SeriesAvgGroup
            {
                SeriesTitle = g.Key.SeriesTitle,
                SeriesUrl = $"/series/{g.Key.SeriesSlug}/",
                Parts = g.OrderBy(r => r.PartDisplayOrder ?? byte.MaxValue)
                         .Select(r => new SeriesAvgPartRow
                         {
                             PartLabel = r.PartLabel,
                             OccurrenceCount = r.OccurrenceCount,
                             AvgSeconds = r.AvgSeconds,
                             AvgLabel = FormatSeconds(r.AvgSeconds),
                             MinSeconds = r.MinSeconds,
                             MinLabel = FormatSeconds(r.MinSeconds),
                             MaxSeconds = r.MaxSeconds,
                             MaxLabel = FormatSeconds(r.MaxSeconds)
                         })
                         .ToList()
            })
            .ToList();

        var content = new BySeriesModel { Groups = grouped };

        var layout = new LayoutModel
        {
            PageTitle = "シリーズ別パート尺",
            MetaDescription = "プリキュア各シリーズのパート種別別の平均/最短/最長尺の集計。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "エピソード尺統計", Url = "/stats/episodes/" },
                new BreadcrumbItem { Label = "シリーズ別", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/episodes/by-series/", "episode-by-series",
            "stats-episodes-by-series.sbn", content, layout);
        _ctx.Logger.Success("/stats/episodes/by-series/");
    }

    // ──────────────────────────────────────────────────────
    // 共通ヘルパ
    // ──────────────────────────────────────────────────────

    private static PartLengthRowView ToPartLengthRowView(EpisodePartStatsRepository.EpisodePartLengthRow r) => new()
    {
        Rank = r.Rank,
        EpisodeId = r.EpisodeId,
        SeriesTitle = r.SeriesTitle,
        SeriesEpNo = r.SeriesEpNo,
        TitleText = r.TitleText,
        EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
        LengthSeconds = r.LengthSeconds,
        LengthLabel = FormatSecondsAsMinSec(r.LengthSeconds)
    };

    private static CmTimeRowView ToCmTimeRowView(EpisodePartStatsRepository.CmTimeRow r) => new()
    {
        Rank = r.Rank,
        EpisodeId = r.EpisodeId,
        SeriesTitle = r.SeriesTitle,
        SeriesEpNo = r.SeriesEpNo,
        TitleText = r.TitleText,
        EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
        Cm2OffsetSeconds = r.Cm2OffsetSeconds,
        CmEnterTimeLabel = FormatCmEnterTime(r.Cm2OffsetSeconds),
        CmOffsetLabel = FormatSecondsAsMinSec(r.Cm2OffsetSeconds)
    };

    /// <summary>秒数を「m分ss秒」表記に整形（例: 12分34秒）。NULL/負値は空文字。</summary>
    private static string FormatSecondsAsMinSec(double seconds)
    {
        if (seconds <= 0) return "";
        int total = (int)Math.Round(seconds);
        int min = total / 60;
        int sec = total % 60;
        return $"{min}分{sec:00}秒";
    }

    /// <summary>秒数を「mm:ss」表記に整形（パート平均などに使う）。</summary>
    private static string FormatSeconds(double seconds)
    {
        if (seconds <= 0) return "";
        int total = (int)Math.Round(seconds);
        int min = total / 60;
        int sec = total % 60;
        return $"{min:00}:{sec:00}";
    }

    /// <summary>CM 入りオフセット秒数を、番組開始（08:30:00）起点の絶対時刻に変換して「HH:mm:ss」表記で返す。</summary>
    private static string FormatCmEnterTime(double offsetSeconds)
    {
        var ts = ProgramStartTime + TimeSpan.FromSeconds(Math.Round(offsetSeconds));
        return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
    }

    // ──────────────────────────────────────────────────────
    // テンプレ用 DTO 群
    // ──────────────────────────────────────────────────────

    private sealed class IndexModel { }

    private sealed class PartLengthRankingModel
    {
        public string PartLabel { get; set; } = "";
        public IReadOnlyList<PartLengthRowView> Descending { get; set; } = Array.Empty<PartLengthRowView>();
        public IReadOnlyList<PartLengthRowView> Ascending { get; set; } = Array.Empty<PartLengthRowView>();
    }

    private sealed class PartLengthRowView
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public double LengthSeconds { get; set; }
        public string LengthLabel { get; set; } = "";
    }

    private sealed class CmTimeRankingModel
    {
        public string ProgramStartLabel { get; set; } = "";
        public IReadOnlyList<CmTimeRowView> Earliest { get; set; } = Array.Empty<CmTimeRowView>();
        public IReadOnlyList<CmTimeRowView> Latest { get; set; } = Array.Empty<CmTimeRowView>();
    }

    private sealed class CmTimeRowView
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public double Cm2OffsetSeconds { get; set; }
        /// <summary>番組開始 08:30:00 起点の絶対時刻表記（例: "08:42:15"）。</summary>
        public string CmEnterTimeLabel { get; set; } = "";
        /// <summary>番組開始からの経過時間（例: "12分15秒"）。</summary>
        public string CmOffsetLabel { get; set; } = "";
    }

    private sealed class BySeriesModel
    {
        public IReadOnlyList<SeriesAvgGroup> Groups { get; set; } = Array.Empty<SeriesAvgGroup>();
    }

    private sealed class SeriesAvgGroup
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public IReadOnlyList<SeriesAvgPartRow> Parts { get; set; } = Array.Empty<SeriesAvgPartRow>();
    }

    private sealed class SeriesAvgPartRow
    {
        public string PartLabel { get; set; } = "";
        public long OccurrenceCount { get; set; }
        public double AvgSeconds { get; set; }
        public string AvgLabel { get; set; } = "";
        public double MinSeconds { get; set; }
        public string MinLabel { get; set; } = "";
        public double MaxSeconds { get; set; }
        public string MaxLabel { get; set; } = "";
    }
}
