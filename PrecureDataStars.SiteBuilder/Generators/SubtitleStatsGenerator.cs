using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サブタイトル文字統計ページ群（v1.3.0 後半追加）。
/// <para>
/// <c>/stats/subtitles/</c> 配下の 5 ページを生成する：
/// </para>
/// <list type="bullet">
///   <item><description><c>/stats/subtitles/</c> — 索引（5 ページへのリンク + 概要数値）</description></item>
///   <item><description><c>/stats/subtitles/char-ranking/</c> — 全文字 + 漢字のみの 2 タブ TOP 100</description></item>
///   <item><description><c>/stats/subtitles/length-ranking/</c> — 文字数 多い順 / 少ない順 の 2 タブ TOP 100</description></item>
///   <item><description><c>/stats/subtitles/kanji-ratio/</c> — 漢字率 TOP 100</description></item>
///   <item><description><c>/stats/subtitles/by-series/</c> — シリーズ別集計（文字種別比率 + 記号 + TOP5 文字）</description></item>
/// </list>
/// </summary>
public sealed class SubtitleStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly SubtitleStatsRepository _repo;

    /// <summary>ランキングの上限件数。</summary>
    private const int RankingLimit = 100;

    /// <summary>シリーズ別文字 TOP-N の N。</summary>
    private const int TopCharsPerSeries = 5;

    public SubtitleStatsGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _repo = new SubtitleStatsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating subtitle stats");

        await GenerateIndexAsync(ct).ConfigureAwait(false);
        await GenerateCharRankingAsync(ct).ConfigureAwait(false);
        await GenerateLengthRankingAsync(ct).ConfigureAwait(false);
        await GenerateKanjiRatioAsync(ct).ConfigureAwait(false);
        await GenerateBySeriesAsync(ct).ConfigureAwait(false);
    }

    // ──────────────────────────────────────────────────────
    // /stats/subtitles/ — 索引
    // ──────────────────────────────────────────────────────

    private async Task GenerateIndexAsync(CancellationToken ct)
    {
        var content = new IndexModel();
        var layout = new LayoutModel
        {
            PageTitle = "サブタイトル統計",
            MetaDescription = "プリキュア全シリーズのサブタイトルから文字種別・文字数・漢字率などを集計した統計ページ群です。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/subtitles/", "subtitle-stats-index", "stats-subtitles-index.sbn", content, layout);
        _ctx.Logger.Success("/stats/subtitles/");
        await Task.CompletedTask;
    }

    // ──────────────────────────────────────────────────────
    // /stats/subtitles/char-ranking/
    // ──────────────────────────────────────────────────────

    private async Task GenerateCharRankingAsync(CancellationToken ct)
    {
        // 全文字と漢字限定を 2 つ並列に問い合わせて 1 ページに同居させる。
        var allChars = await _repo.GetCharRankingAllAsync(RankingLimit, ct).ConfigureAwait(false);
        var kanjiOnly = await _repo.GetCharRankingKanjiAsync(RankingLimit, ct).ConfigureAwait(false);

        var content = new CharRankingModel
        {
            AllChars = allChars.Select(r => new CharRankingRow
            {
                Rank = r.Rank, Char = r.Char, TotalCount = r.TotalCount
            }).ToList(),
            KanjiChars = kanjiOnly.Select(r => new CharRankingRow
            {
                Rank = r.Rank, Char = r.Char, TotalCount = r.TotalCount
            }).ToList()
        };

        var layout = new LayoutModel
        {
            PageTitle = "サブタイトル使用文字ランキング",
            MetaDescription = "プリキュア全シリーズのサブタイトル中で使用された文字の出現回数ランキング（TOP 100）。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "/stats/subtitles/" },
                new BreadcrumbItem { Label = "使用文字ランキング", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/subtitles/char-ranking/", "subtitle-char-ranking",
            "stats-subtitles-char-ranking.sbn", content, layout);
        _ctx.Logger.Success("/stats/subtitles/char-ranking/");
    }

    // ──────────────────────────────────────────────────────
    // /stats/subtitles/length-ranking/
    // ──────────────────────────────────────────────────────

    private async Task GenerateLengthRankingAsync(CancellationToken ct)
    {
        var longest = await _repo.GetTitleLengthRankingAsync(ascending: false, RankingLimit, ct).ConfigureAwait(false);
        var shortest = await _repo.GetTitleLengthRankingAsync(ascending: true, RankingLimit, ct).ConfigureAwait(false);

        var content = new EpisodeRankingModel
        {
            ValueUnit = "文字",
            Descending = longest.Select(ToEpisodeStatRowView).ToList(),
            Ascending = shortest.Select(ToEpisodeStatRowView).ToList()
        };

        var layout = new LayoutModel
        {
            PageTitle = "サブタイトル文字数ランキング",
            MetaDescription = "プリキュア全シリーズのサブタイトル文字数（空白除く）ランキング。多い順・少ない順それぞれ TOP 100。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "/stats/subtitles/" },
                new BreadcrumbItem { Label = "文字数ランキング", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/subtitles/length-ranking/", "subtitle-length-ranking",
            "stats-subtitles-length-ranking.sbn", content, layout);
        _ctx.Logger.Success("/stats/subtitles/length-ranking/");
    }

    // ──────────────────────────────────────────────────────
    // /stats/subtitles/kanji-ratio/
    // ──────────────────────────────────────────────────────

    private async Task GenerateKanjiRatioAsync(CancellationToken ct)
    {
        var rows = await _repo.GetKanjiRatioRankingAsync(RankingLimit, ct).ConfigureAwait(false);

        var content = new KanjiRatioModel
        {
            Rows = rows.Select(r => new KanjiRatioRowView
            {
                Rank = r.Rank,
                EpisodeId = r.EpisodeId,
                SeriesTitle = r.SeriesTitle,
                SeriesEpNo = r.SeriesEpNo,
                TitleText = r.TitleText,
                EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
                KanjiCount = r.KanjiCount,
                TotalCount = r.TotalCount,
                RatioPercent = r.Ratio * 100.0
            }).ToList()
        };

        var layout = new LayoutModel
        {
            PageTitle = "サブタイトル漢字率ランキング",
            MetaDescription = "プリキュア全シリーズのサブタイトル中で漢字が占める比率ランキング（TOP 100）。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "/stats/subtitles/" },
                new BreadcrumbItem { Label = "漢字率ランキング", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/subtitles/kanji-ratio/", "subtitle-kanji-ratio",
            "stats-subtitles-kanji-ratio.sbn", content, layout);
        _ctx.Logger.Success("/stats/subtitles/kanji-ratio/");
    }

    // ──────────────────────────────────────────────────────
    // /stats/subtitles/by-series/
    // ──────────────────────────────────────────────────────

    private async Task GenerateBySeriesAsync(CancellationToken ct)
    {
        var charTypes = await _repo.GetCharTypeBreakdownBySeriesAsync(ct).ConfigureAwait(false);
        var symbols = await _repo.GetSymbolCountsBySeriesAsync(ct).ConfigureAwait(false);
        var topChars = await _repo.GetTopCharsBySeriesAsync(TopCharsPerSeries, ct).ConfigureAwait(false);

        // シリーズ ID をキーに 3 つの集計を join。
        var topCharsBySeries = topChars
            .GroupBy(r => r.SeriesId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Rank).ThenBy(r => r.Char, StringComparer.Ordinal).ToList());
        var symbolsBySeries = symbols.ToDictionary(r => r.SeriesId);

        var rows = new List<SeriesStatsRowView>();
        foreach (var ct1 in charTypes)
        {
            var view = new SeriesStatsRowView
            {
                SeriesTitle = ct1.SeriesTitle,
                SeriesUrl = $"/series/{ct1.SeriesSlug}/",
                Kanji = ct1.Kanji,
                Hiragana = ct1.Hiragana,
                Katakana = ct1.Katakana,
                Latin = ct1.Latin,
                Digits = ct1.Digits,
                TotalCount = ct1.TotalCount,
                KanjiPercent = ct1.TotalCount > 0 ? (double)ct1.Kanji / ct1.TotalCount * 100.0 : 0,
                HiraganaPercent = ct1.TotalCount > 0 ? (double)ct1.Hiragana / ct1.TotalCount * 100.0 : 0,
                KatakanaPercent = ct1.TotalCount > 0 ? (double)ct1.Katakana / ct1.TotalCount * 100.0 : 0,
                LatinPercent = ct1.TotalCount > 0 ? (double)ct1.Latin / ct1.TotalCount * 100.0 : 0,
                DigitsPercent = ct1.TotalCount > 0 ? (double)ct1.Digits / ct1.TotalCount * 100.0 : 0
            };

            // 記号集計をマッピング。null は 0 扱い。
            if (symbolsBySeries.TryGetValue(ct1.SeriesId, out var sym))
            {
                view.Symbols = new SeriesSymbolView
                {
                    Exclamation = sym.Exclamation, Question = sym.Question,
                    MiddleDot = sym.MiddleDot, Tilde = sym.Tilde, Ampersand = sym.Ampersand,
                    ParenOpen = sym.ParenOpen, ParenClose = sym.ParenClose,
                    Ellipsis = sym.Ellipsis, Comma = sym.Comma, Note = sym.Note,
                    Star = sym.Star, HeartOutline = sym.HeartOutline,
                    Period = sym.Period, HeartFilled = sym.HeartFilled,
                    BracketOpen = sym.BracketOpen, BracketClose = sym.BracketClose
                };
            }

            // TOP-N 文字。
            if (topCharsBySeries.TryGetValue(ct1.SeriesId, out var chars))
            {
                view.TopChars = chars.Select(c => new SeriesTopCharView
                {
                    Rank = c.Rank, Char = c.Char, Total = c.Total
                }).ToList();
            }

            rows.Add(view);
        }

        var content = new BySeriesModel { Rows = rows };

        var layout = new LayoutModel
        {
            PageTitle = "シリーズ別サブタイトル統計",
            MetaDescription = "プリキュア各シリーズのサブタイトル文字種別比率・記号出現回数・TOP5 文字を一覧する集計ページ。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "サブタイトル統計", Url = "/stats/subtitles/" },
                new BreadcrumbItem { Label = "シリーズ別", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/subtitles/by-series/", "subtitle-by-series",
            "stats-subtitles-by-series.sbn", content, layout);
        _ctx.Logger.Success("/stats/subtitles/by-series/");
    }

    // ──────────────────────────────────────────────────────
    // 共通ヘルパ
    // ──────────────────────────────────────────────────────

    private static EpisodeStatRowView ToEpisodeStatRowView(SubtitleStatsRepository.EpisodeStatRow r) => new()
    {
        Rank = r.Rank,
        EpisodeId = r.EpisodeId,
        SeriesTitle = r.SeriesTitle,
        SeriesEpNo = r.SeriesEpNo,
        TitleText = r.TitleText,
        EpisodeUrl = PathUtil.EpisodeUrl(r.SeriesSlug, r.SeriesEpNo),
        Value = r.Value
    };

    // ──────────────────────────────────────────────────────
    // テンプレ用 DTO 群
    // ──────────────────────────────────────────────────────

    /// <summary>索引ページのモデル（現状は値なしの単純なマーカー）。</summary>
    private sealed class IndexModel { }

    private sealed class CharRankingModel
    {
        public IReadOnlyList<CharRankingRow> AllChars { get; set; } = Array.Empty<CharRankingRow>();
        public IReadOnlyList<CharRankingRow> KanjiChars { get; set; } = Array.Empty<CharRankingRow>();
    }

    private sealed class CharRankingRow
    {
        public int Rank { get; set; }
        public string Char { get; set; } = "";
        public long TotalCount { get; set; }
    }

    private sealed class EpisodeRankingModel
    {
        public string ValueUnit { get; set; } = "";
        public IReadOnlyList<EpisodeStatRowView> Descending { get; set; } = Array.Empty<EpisodeStatRowView>();
        public IReadOnlyList<EpisodeStatRowView> Ascending { get; set; } = Array.Empty<EpisodeStatRowView>();
    }

    private sealed class EpisodeStatRowView
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public long Value { get; set; }
    }

    private sealed class KanjiRatioModel
    {
        public IReadOnlyList<KanjiRatioRowView> Rows { get; set; } = Array.Empty<KanjiRatioRowView>();
    }

    private sealed class KanjiRatioRowView
    {
        public int Rank { get; set; }
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public long KanjiCount { get; set; }
        public long TotalCount { get; set; }
        public double RatioPercent { get; set; }
    }

    private sealed class BySeriesModel
    {
        public IReadOnlyList<SeriesStatsRowView> Rows { get; set; } = Array.Empty<SeriesStatsRowView>();
    }

    private sealed class SeriesStatsRowView
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public long Kanji { get; set; }
        public long Hiragana { get; set; }
        public long Katakana { get; set; }
        public long Latin { get; set; }
        public long Digits { get; set; }
        public long TotalCount { get; set; }
        public double KanjiPercent { get; set; }
        public double HiraganaPercent { get; set; }
        public double KatakanaPercent { get; set; }
        public double LatinPercent { get; set; }
        public double DigitsPercent { get; set; }
        public SeriesSymbolView Symbols { get; set; } = new();
        public IReadOnlyList<SeriesTopCharView> TopChars { get; set; } = Array.Empty<SeriesTopCharView>();
    }

    private sealed class SeriesSymbolView
    {
        public long Exclamation { get; set; }
        public long Question { get; set; }
        public long MiddleDot { get; set; }
        public long Tilde { get; set; }
        public long Ampersand { get; set; }
        public long ParenOpen { get; set; }
        public long ParenClose { get; set; }
        public long Ellipsis { get; set; }
        public long Comma { get; set; }
        public long Note { get; set; }
        public long Star { get; set; }
        public long HeartOutline { get; set; }
        public long Period { get; set; }
        public long HeartFilled { get; set; }
        public long BracketOpen { get; set; }
        public long BracketClose { get; set; }
    }

    private sealed class SeriesTopCharView
    {
        public int Rank { get; set; }
        public string Char { get; set; } = "";
        public long Total { get; set; }
    }
}
