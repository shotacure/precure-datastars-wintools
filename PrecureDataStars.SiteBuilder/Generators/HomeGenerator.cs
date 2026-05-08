using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サイトトップ <c>/</c> の生成（v1.3.0 大幅拡充）。
/// <para>
/// シリーズ一覧に加えて、ビルド時点のデータベース状態を反映する複数の動的セクションを表示する。
/// 静的サイト生成のため「今日」はビルド実行日で固定される。毎日定期ビルドする運用で、
/// 「今日の記念日」「今週の記念日」「次回予告」「間もなく発売」セクションが日々更新される想定。
/// </para>
/// <para>
/// 各セクションは該当データが 0 件のときは丸ごと非表示（テンプレ側で件数 0 のセクションは描画しない）。
/// 該当データが多すぎるセクションは適度な件数で打ち切る。
/// </para>
/// </summary>
public sealed class HomeGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    /// <summary>最新エピソードに表示する最大件数。</summary>
    private const int LatestEpisodesMax = 8;

    /// <summary>次回予告（未来放送）に表示する最大件数。</summary>
    private const int UpcomingEpisodesMax = 5;

    /// <summary>記念日一覧の各セクションで表示する最大件数。同月日のエピソード数が多い年もあるため広めに。</summary>
    private const int AnniversaryMax = 20;

    /// <summary>新着商品に表示する最大件数。</summary>
    private const int LatestProductsMax = 8;

    /// <summary>間もなく発売に表示する最大件数。</summary>
    private const int UpcomingProductsMax = 8;

    /// <summary>今週の記念日の対象範囲（今日を含む直近 N 日間）。</summary>
    private const int ThisWeekDays = 7;

    public HomeGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
    }

    /// <summary>
    /// トップページを 1 枚生成する。
    /// </summary>
    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating home");

        // 「今日」はビルド実行日で固定。記念日系・次回予告・間もなく発売の判定基準。
        // JST 運用前提（DB の OnAirAt も JST、release_date は DATE のためタイムゾーン無関係）。
        var today = DateTime.Now;
        var todayDate = DateOnly.FromDateTime(today);

        // 全エピソードをフラット化（シリーズ ID も持たせる）。
        var allEpisodes = new List<EpisodeWithSeries>();
        foreach (var (sid, eps) in _ctx.EpisodesBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            foreach (var e in eps)
            {
                allEpisodes.Add(new EpisodeWithSeries
                {
                    Episode = e,
                    Series = s
                });
            }
        }

        // 商品をロード（新着＋間もなく発売の判定用）。
        var productsRepo = new ProductsRepository(_factory);
        var allProducts = (await productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var productKindsRepo = new ProductKindsRepository(_factory);
        var productKinds = (await productKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var productKindMap = productKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);

        // 各セクションを構築。
        var latestEpisodes = BuildLatestEpisodes(allEpisodes, today);
        var upcomingEpisodes = BuildUpcomingEpisodes(allEpisodes, today);
        var todayAnniversary = BuildTodayAnniversary(allEpisodes, today);
        var thisWeekAnniversary = BuildThisWeekAnniversary(allEpisodes, today, todayAnniversary);
        var latestProducts = BuildLatestProducts(allProducts, productKindMap, todayDate);
        var upcomingProducts = BuildUpcomingProducts(allProducts, productKindMap, todayDate);
        var dbStats = await BuildDbStatsAsync(allEpisodes.Count, ct).ConfigureAwait(false);

        // シリーズ一覧（既存ロジック維持。ホーム末尾に表示）。
        var seriesView = _ctx.Series
            .Where(s => s.KindCode == "TV")
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .Select(s => new SeriesListItem
            {
                Slug = s.Slug,
                Title = s.Title,
                TitleShort = s.TitleShort ?? "",
                Period = FormatPeriodJp(s.StartDate, s.EndDate),
                EpisodesLabel = s.Episodes.HasValue ? $"全 {s.Episodes.Value} 話" : ""
            })
            .ToList();

        var content = new HomeContentModel
        {
            SiteName = _ctx.Config.SiteName,
            BuildDateLabel = $"{today.Year}年{today.Month}月{today.Day}日",
            LatestEpisodes = latestEpisodes,
            UpcomingEpisodes = upcomingEpisodes,
            TodayAnniversary = todayAnniversary,
            ThisWeekAnniversary = thisWeekAnniversary,
            LatestProducts = latestProducts,
            UpcomingProducts = upcomingProducts,
            DbStats = dbStats,
            Series = seriesView
        };

        // ホームページの構造化データは Schema.org の WebSite 型。
        string baseUrl = _ctx.Config.BaseUrl;
        var jsonLd = JsonLdBuilder.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "WebSite",
            ["name"] = _ctx.Config.SiteName,
            ["description"] = "プリキュアシリーズのエピソード・音楽・スタッフ・キャラクターを横断的に閲覧できる非公式データベース。",
            ["url"] = string.IsNullOrEmpty(baseUrl) ? null : baseUrl + "/",
            ["inLanguage"] = "ja"
        });

        var layout = new LayoutModel
        {
            PageTitle = "",  // トップなのでサイト名のみ表示にする
            MetaDescription = "プリキュアシリーズのエピソード・音楽・スタッフ・キャラクターを横断的に閲覧できる非公式データベース。",
            Breadcrumbs = Array.Empty<BreadcrumbItem>(),
            OgType = "website",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite("/", "home", "home.sbn", content, layout);
        _ctx.Logger.Success("/");
    }

    // ────────────────────────────────────────────────────────────────────
    // セクション構築ロジック
    // ────────────────────────────────────────────────────────────────────

    /// <summary>最新エピソード：ビルド時刻以前で OnAirAt が新しいもの上位 <see cref="LatestEpisodesMax"/> 件。</summary>
    private static IReadOnlyList<EpisodeRow> BuildLatestEpisodes(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, DateTime today)
    {
        return allEpisodes
            .Where(x => x.Episode.OnAirAt <= today)
            .OrderByDescending(x => x.Episode.OnAirAt)
            .Take(LatestEpisodesMax)
            .Select(ToEpisodeRow)
            .ToList();
    }

    /// <summary>次回予告：ビルド時刻以降で OnAirAt が古いもの順に <see cref="UpcomingEpisodesMax"/> 件。</summary>
    private static IReadOnlyList<EpisodeRow> BuildUpcomingEpisodes(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, DateTime today)
    {
        return allEpisodes
            .Where(x => x.Episode.OnAirAt > today)
            .OrderBy(x => x.Episode.OnAirAt)
            .Take(UpcomingEpisodesMax)
            .Select(ToEpisodeRow)
            .ToList();
    }

    /// <summary>
    /// 今日の記念日：今日と同じ月日に放送されたエピソード（過去年）。
    /// 何年前の放送かを「N 年前」形式で併記する。
    /// </summary>
    private static IReadOnlyList<AnniversaryRow> BuildTodayAnniversary(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, DateTime today)
    {
        return allEpisodes
            .Where(x => x.Episode.OnAirAt.Month == today.Month
                     && x.Episode.OnAirAt.Day == today.Day
                     && x.Episode.OnAirAt.Date < today.Date)
            .OrderByDescending(x => x.Episode.OnAirAt)
            .Take(AnniversaryMax)
            .Select(x =>
            {
                int yearsAgo = today.Year - x.Episode.OnAirAt.Year;
                return ToAnniversaryRow(x, $"{yearsAgo}年前");
            })
            .ToList();
    }

    /// <summary>
    /// 今週の記念日：今日を起点に過去 6 日間の月日に該当するエピソード。
    /// 「今日の記念日」と重複するレコードは除外する。「N 日前・M 年前」形式のラベルを併記。
    /// </summary>
    private static IReadOnlyList<AnniversaryRow> BuildThisWeekAnniversary(
        IReadOnlyList<EpisodeWithSeries> allEpisodes,
        DateTime today,
        IReadOnlyList<AnniversaryRow> todayAnniversary)
    {
        // 「今日の記念日」に既に含まれるエピソード ID は除外。
        var excluded = todayAnniversary.Select(x => x.EpisodeId).ToHashSet();

        // 今日からさかのぼる 6 日間の (Month, Day) ペア + 「何日前か」のラベルを用意。
        // 「今日」の月日は todayAnniversary でカバー済みなので除外。
        var monthDayLabels = new Dictionary<(int month, int day), (int daysAgo, string label)>();
        for (int back = 1; back < ThisWeekDays; back++)
        {
            var d = today.AddDays(-back);
            string label = back == 1 ? "昨日" : $"{back}日前";
            monthDayLabels[(d.Month, d.Day)] = (back, label);
        }

        return allEpisodes
            .Where(x => !excluded.Contains(x.Episode.EpisodeId))
            .Where(x => monthDayLabels.ContainsKey((x.Episode.OnAirAt.Month, x.Episode.OnAirAt.Day))
                     && x.Episode.OnAirAt.Date < today.Date)
            .OrderBy(x => monthDayLabels[(x.Episode.OnAirAt.Month, x.Episode.OnAirAt.Day)].daysAgo)
            .ThenByDescending(x => x.Episode.OnAirAt)
            .Take(AnniversaryMax)
            .Select(x =>
            {
                int yearsAgo = today.Year - x.Episode.OnAirAt.Year;
                var (_, dayLabel) = monthDayLabels[(x.Episode.OnAirAt.Month, x.Episode.OnAirAt.Day)];
                return ToAnniversaryRow(x, $"{dayLabel}・{yearsAgo}年前");
            })
            .ToList();
    }

    /// <summary>新着商品：今日以前の release_date で新しいもの上位 <see cref="LatestProductsMax"/> 件。</summary>
    private static IReadOnlyList<ProductRow> BuildLatestProducts(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        DateOnly today)
    {
        return products
            .Where(p => DateOnly.FromDateTime(p.ReleaseDate) <= today)
            .OrderByDescending(p => p.ReleaseDate)
            .Take(LatestProductsMax)
            .Select(p => ToProductRow(p, productKindMap))
            .ToList();
    }

    /// <summary>間もなく発売：今日より後の release_date で古いもの順に <see cref="UpcomingProductsMax"/> 件。</summary>
    private static IReadOnlyList<ProductRow> BuildUpcomingProducts(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        DateOnly today)
    {
        return products
            .Where(p => DateOnly.FromDateTime(p.ReleaseDate) > today)
            .OrderBy(p => p.ReleaseDate)
            .Take(UpcomingProductsMax)
            .Select(p => ToProductRow(p, productKindMap))
            .ToList();
    }

    /// <summary>
    /// データベース統計：シリーズ・エピソード・人物・楽曲などの件数。
    /// サイト規模の指標となる数字を 7 種出す。
    /// </summary>
    private async Task<DbStatsModel> BuildDbStatsAsync(int episodeCount, CancellationToken ct)
    {
        // 件数取得用の API がないので GetAllAsync で件数を数える。
        // SiteBuilder の起動時 1 回限りなので許容。
        var personsRepo = new PersonsRepository(_factory);
        var companiesRepo = new CompaniesRepository(_factory);
        var charactersRepo = new CharactersRepository(_factory);
        var songsRepo = new SongsRepository(_factory);
        var productsRepo = new ProductsRepository(_factory);

        int personsCount = (await personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int companiesCount = (await companiesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int charactersCount = (await charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int songsCount = (await songsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int productsCount = (await productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;

        return new DbStatsModel
        {
            SeriesCount = _ctx.Series.Count,
            EpisodeCount = episodeCount,
            PersonsCount = personsCount,
            CompaniesCount = companiesCount,
            CharactersCount = charactersCount,
            SongsCount = songsCount,
            ProductsCount = productsCount
        };
    }

    // ────────────────────────────────────────────────────────────────────
    // DTO 変換ヘルパ
    // ────────────────────────────────────────────────────────────────────

    private static EpisodeRow ToEpisodeRow(EpisodeWithSeries x) => new()
    {
        EpisodeId = x.Episode.EpisodeId,
        SeriesTitle = x.Series.Title,
        SeriesSlug = x.Series.Slug,
        SeriesEpNo = x.Episode.SeriesEpNo,
        TitleText = x.Episode.TitleText,
        OnAirDate = FormatJpDate(x.Episode.OnAirAt),
        EpisodeUrl = PathUtil.EpisodeUrl(x.Series.Slug, x.Episode.SeriesEpNo)
    };

    private static AnniversaryRow ToAnniversaryRow(EpisodeWithSeries x, string anniversaryLabel) => new()
    {
        EpisodeId = x.Episode.EpisodeId,
        SeriesTitle = x.Series.Title,
        SeriesSlug = x.Series.Slug,
        SeriesEpNo = x.Episode.SeriesEpNo,
        TitleText = x.Episode.TitleText,
        OnAirDate = FormatJpDate(x.Episode.OnAirAt),
        AnniversaryLabel = anniversaryLabel,
        EpisodeUrl = PathUtil.EpisodeUrl(x.Series.Slug, x.Episode.SeriesEpNo)
    };

    private static ProductRow ToProductRow(Product p, IReadOnlyDictionary<string, ProductKind> productKindMap) => new()
    {
        ProductCatalogNo = p.ProductCatalogNo,
        Title = p.Title,
        ReleaseDate = FormatJpDateOnly(p.ReleaseDate),
        ProductKindLabel = productKindMap.TryGetValue(p.ProductKindCode, out var pk) ? pk.NameJa : p.ProductKindCode,
        ProductUrl = PathUtil.ProductUrl(p.ProductCatalogNo)
    };

    /// <summary>
    /// 放送日を「2024年5月8日（水）」フォーマットで返す（曜日付き）。
    /// 同月日マッチで「何曜日に放送されたか」を見せたいので曜日を併記する。
    /// </summary>
    private static string FormatJpDate(DateTime dt)
    {
        string dayOfWeek = dt.DayOfWeek switch
        {
            DayOfWeek.Sunday => "日",
            DayOfWeek.Monday => "月",
            DayOfWeek.Tuesday => "火",
            DayOfWeek.Wednesday => "水",
            DayOfWeek.Thursday => "木",
            DayOfWeek.Friday => "金",
            DayOfWeek.Saturday => "土",
            _ => "?"
        };
        return $"{dt.Year}年{dt.Month}月{dt.Day}日（{dayOfWeek}）";
    }

    /// <summary>商品発売日のフォーマット（曜日無し、シンプルに）。</summary>
    private static string FormatJpDateOnly(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    /// <summary>放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。</summary>
    private static string FormatPeriodJp(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        return startStr;
    }

    // ────────────────────────────────────────────────────────────────────
    // テンプレ用 DTO 群
    // ────────────────────────────────────────────────────────────────────

    /// <summary>エピソードとその所属シリーズを束ねた中間構造体。</summary>
    private sealed class EpisodeWithSeries
    {
        public Episode Episode { get; set; } = null!;
        public Series Series { get; set; } = null!;
    }

    /// <summary>home.sbn のモデル。</summary>
    private sealed class HomeContentModel
    {
        public string SiteName { get; set; } = "";
        public string BuildDateLabel { get; set; } = "";
        public IReadOnlyList<EpisodeRow> LatestEpisodes { get; set; } = Array.Empty<EpisodeRow>();
        public IReadOnlyList<EpisodeRow> UpcomingEpisodes { get; set; } = Array.Empty<EpisodeRow>();
        public IReadOnlyList<AnniversaryRow> TodayAnniversary { get; set; } = Array.Empty<AnniversaryRow>();
        public IReadOnlyList<AnniversaryRow> ThisWeekAnniversary { get; set; } = Array.Empty<AnniversaryRow>();
        public IReadOnlyList<ProductRow> LatestProducts { get; set; } = Array.Empty<ProductRow>();
        public IReadOnlyList<ProductRow> UpcomingProducts { get; set; } = Array.Empty<ProductRow>();
        public DbStatsModel DbStats { get; set; } = new();
        public IReadOnlyList<SeriesListItem> Series { get; set; } = Array.Empty<SeriesListItem>();
    }

    private sealed class EpisodeRow
    {
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
    }

    private sealed class AnniversaryRow
    {
        public int EpisodeId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        /// <summary>何年前 / 何日前を示すラベル（例: "20年前"、"昨日・10年前"）。</summary>
        public string AnniversaryLabel { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
    }

    private sealed class ProductRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string ProductKindLabel { get; set; } = "";
        public string ProductUrl { get; set; } = "";
    }

    /// <summary>データベース統計セクションのモデル。</summary>
    private sealed class DbStatsModel
    {
        public int SeriesCount { get; set; }
        public int EpisodeCount { get; set; }
        public int PersonsCount { get; set; }
        public int CompaniesCount { get; set; }
        public int CharactersCount { get; set; }
        public int SongsCount { get; set; }
        public int ProductsCount { get; set; }
    }

    /// <summary>シリーズリスト 1 行分。</summary>
    private sealed class SeriesListItem
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleShort { get; set; } = "";
        public string Period { get; set; } = "";
        public string EpisodesLabel { get; set; } = "";
    }
}

/// <summary>
/// <c>/about/</c> の生成。
/// </summary>
public sealed class AboutGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public AboutGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating about");

        // about テンプレは現状 SiteName しか参照しないので空モデルで十分。
        var content = new AboutContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "このサイトについて",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "このサイトについて", Url = "" }
            }
        };

        _page.RenderAndWrite("/about/", "about", "about.sbn", content, layout);
        _ctx.Logger.Success("/about/");
    }

    private sealed class AboutContentModel
    {
        public string SiteName { get; set; } = "";
    }
}
