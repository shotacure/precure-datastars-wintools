
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サイトトップ <c>/</c> の生成（v1.3.0 ブラッシュアップ続編）。
/// <para>
/// 主な変更点：
/// <list type="bullet">
///   <item><description>「今日の記念日」はビルド時計算をやめて全エピソードの放送日（年月日）を
///     JSON として埋め込み、クライアント側 JavaScript で「今日」を動的に判定して描画する方式に変更。
///     ビルド日と閲覧日がズレても、サイトを開いた瞬間の「今日」で記念日が出る。</description></item>
///   <item><description>「最終ビルド」表記を「○○年○○月○○日現在 『○○プリキュア』第n話時点の情報を表示しています」
///     形式に改修。基準点は <see cref="BuildContext.LatestAiredTvEpisode"/>（全 TV シリーズを横断した
///     最新放送済話）。LatestAiredTvEpisode が null のときはプリキュア部分を省略。</description></item>
///   <item><description>データベース統計セクションをコンパクト化（横並び 1 行）、項目を 11 個に拡張。
///     TV シリーズ／映画／スピンオフ を分離し、プリキュア人数・歌（song_recordings 単位）・
///     劇伴（bgm_cues 単位）・音楽商品「N点M枚」を追加。映画の作数は親作品（ParentSeriesId が
///     null のもの）のみカウント。</description></item>
///   <item><description>「このサイトの特徴」セクションは削除。</description></item>
///   <item><description>「TV シリーズ」一覧表は削除し、シリーズ一覧へのリンク 1 行に集約。</description></item>
///   <item><description>v1.3.0 ブラッシュアップ続編：「今週の記念日」セクションを削除（テンプレ側のみ）、
///     「次回予告」→「今後の放送予定」、「間もなく発売」→「音楽商品の発売予定」、
///     「新着商品」→「新着の音楽商品」のリネーム。商品はそもそも音楽商品のみ登録運用なので
///     データソースは無変更（文言だけ「音楽商品」を明示）。</description></item>
/// </list>
/// </para>
/// <para>
/// 「今後の放送予定 / 最新エピソード / 音楽商品の発売予定 / 新着の音楽商品」はビルド時生成のまま残すが、件数は控えめに。
/// </para>
/// </summary>
public sealed class HomeGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    /// <summary>最新エピソード：ビルド済みの直近放送を上位 N 件。</summary>
    private const int LatestEpisodesMax = 6;

    /// <summary>次回予告：今日以降の N 件。</summary>
    private const int UpcomingEpisodesMax = 4;

    /// <summary>新着商品：直近発売された N 件。</summary>
    private const int LatestProductsMax = 6;

    /// <summary>間もなく発売：今日以降の N 件。</summary>
    private const int UpcomingProductsMax = 6;

    public HomeGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating home");

        // ビルド日時（最終ビルド表示用、および「次回予告 / 最新」セクションの判定基準）。
        // 「今日の記念日」だけは JS で再判定するため、ビルド日に縛られない。
        var buildAt = DateTime.Now;

        // 全エピソードをフラット化。
        var allEpisodes = new List<EpisodeWithSeries>();
        foreach (var (sid, eps) in _ctx.EpisodesBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            // 子作品は単独詳細ページを生成しないので、配下のエピソードはホームのリンク対象から除外。
            // SPIN-OFF は親を持っても単独ページがあるので含める。
            if (IsChildOfMovie(s)) continue;
            foreach (var e in eps)
            {
                allEpisodes.Add(new EpisodeWithSeries { Episode = e, Series = s });
            }
        }

        // 商品をロード（新着＋間もなく発売の判定用）。
        var productsRepo = new ProductsRepository(_factory);
        var allProducts = (await productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var productKindsRepo = new ProductKindsRepository(_factory);
        var productKinds = (await productKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var productKindMap = productKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);

        var todayDate = DateOnly.FromDateTime(buildAt);

        var latestEpisodes = BuildLatestEpisodes(allEpisodes, buildAt);
        var upcomingEpisodes = BuildUpcomingEpisodes(allEpisodes, buildAt);
        var latestProducts = BuildLatestProducts(allProducts, productKindMap, todayDate);
        var upcomingProducts = BuildUpcomingProducts(allProducts, productKindMap, todayDate);
        var dbStats = await BuildDbStatsAsync(allEpisodes.Count, ct).ConfigureAwait(false);

        // 記念日 JS 用：全エピソードの放送日 (year, month, day) とエピソード参照情報を JSON 化。
        // クライアント側で「今日」と月日が一致するエピソードを抽出して描画する。
        // データ量を抑えるためプロパティ名は短縮形（参考：search-index.json と同じ方針）。
        var anniversaryJson = BuildAnniversaryJson(allEpisodes);

        var content = new HomeContentModel
        {
            SiteName = _ctx.Config.SiteName,
            // v1.3.0 ブラッシュアップ続編：最終ビルド表記を「○○年○○月○○日現在 『○○プリキュア』第n話時点
            // の情報を表示しています」形式に改修。基準点は LatestAiredTvEpisode（全 TV シリーズを横断した
            // 最新放送済話）。該当が無いとき（クリーン DB 等）はプリキュア部分を省略する。
            BuildLabel = BuildBuildLabel(buildAt, _ctx.LatestAiredTvEpisode),
            LatestEpisodes = latestEpisodes,
            UpcomingEpisodes = upcomingEpisodes,
            LatestProducts = latestProducts,
            UpcomingProducts = upcomingProducts,
            DbStats = dbStats,
            AnniversaryJson = anniversaryJson
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
            PageTitle = "",
            MetaDescription = "プリキュアシリーズのエピソード・音楽・スタッフ・キャラクターを横断的に閲覧できる非公式データベース。",
            Breadcrumbs = Array.Empty<BreadcrumbItem>(),
            OgType = "website",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite("/", "home", "home.sbn", content, layout);
        _ctx.Logger.Success("/");
    }

    /// <summary>
    /// 子作品判定：親シリーズが存在し、かつ自分が SPIN-OFF ではない場合は子作品扱い。
    /// SeriesGenerator.IsChildOfMovie と同じロジック。
    /// </summary>
    private static bool IsChildOfMovie(Series s)
    {
        if (!s.ParentSeriesId.HasValue) return false;
        if (string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal)) return false;
        return true;
    }

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
    /// v1.3.0 ブラッシュアップ続編：項目数を 11 個に拡張。
    /// <list type="bullet">
    ///   <item><description>TV シリーズ・映画（親作品のみ）・スピンオフを別カウントに分離。</description></item>
    ///   <item><description>プリキュア人数を <see cref="PrecuresRepository"/> から追加取得。</description></item>
    ///   <item><description>歌は <see cref="SongRecordingsRepository"/> ベース（楽曲のレコーディング単位）。
    ///     旧実装の <see cref="SongsRepository"/> ベースは廃止。</description></item>
    ///   <item><description>劇伴件数は bgm_cues の COUNT(*) を SQL で直接取得（is_deleted = 0）。</description></item>
    ///   <item><description>音楽商品は「N点M枚」表記。点数は products、枚数は discs を別々にカウント。</description></item>
    /// </list>
    /// </summary>
    private async Task<DbStatsModel> BuildDbStatsAsync(int episodeCount, CancellationToken ct)
    {
        var personsRepo = new PersonsRepository(_factory);
        var companiesRepo = new CompaniesRepository(_factory);
        var charactersRepo = new CharactersRepository(_factory);
        var precuresRepo = new PrecuresRepository(_factory);
        var songRecordingsRepo = new SongRecordingsRepository(_factory);
        var productsRepo = new ProductsRepository(_factory);

        int personsCount = (await personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int companiesCount = (await companiesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int charactersCount = (await charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int precuresCount = (await precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int songsCount = (await songRecordingsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;
        int productsCount = (await productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).Count;

        // bgm_cues / discs は Repository に GetAllAsync が無いため SQL で直接 COUNT(*)。
        // 仮 M 番号を含めて全件カウントする運用方針（変更概要 D 記載通り）。
        int bgmsCount;
        int discsCount;
        await using (var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false))
        {
            bgmsCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM bgm_cues WHERE is_deleted = 0;",
                cancellationToken: ct)).ConfigureAwait(false);
            discsCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM discs WHERE is_deleted = 0;",
                cancellationToken: ct)).ConfigureAwait(false);
        }

        // シリーズ種別ごとのカウント。
        // 映画系は MOVIE / MOVIE_SHORT / SPRING の 3 種類があるが、いずれも親作品（ParentSeriesId が
        // null）のみカウントする。子作品（併映短編で親が居るケース等）は単独ページが存在せず、件数も
        // ダブルカウントになるため。SPIN-OFF は独立した作品扱いで親を持っても表示するので、
        // ParentSeriesId は問わず KindCode のみで判定する。
        int tvSeriesCount = _ctx.Series.Count(s =>
            string.Equals(s.KindCode, "TV", StringComparison.Ordinal));
        int movieSeriesCount = _ctx.Series.Count(s =>
            (string.Equals(s.KindCode, "MOVIE", StringComparison.Ordinal)
             || string.Equals(s.KindCode, "MOVIE_SHORT", StringComparison.Ordinal)
             || string.Equals(s.KindCode, "SPRING", StringComparison.Ordinal))
            && s.ParentSeriesId == null);
        int spinOffSeriesCount = _ctx.Series.Count(s =>
            string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal));

        return new DbStatsModel
        {
            TvSeriesCount = tvSeriesCount,
            MovieSeriesCount = movieSeriesCount,
            SpinOffSeriesCount = spinOffSeriesCount,
            EpisodeCount = episodeCount,
            PrecuresCount = precuresCount,
            CharactersCount = charactersCount,
            SongsCount = songsCount,
            BgmsCount = bgmsCount,
            // 「N点M枚」を 1 セルにまとめて出すため事前整形（テンプレ側で再組立しないで済むように）。
            MusicProductsLabel = $"{productsCount}点 {discsCount}枚",
            PersonsCount = personsCount,
            CompaniesCount = companiesCount
        };
    }

    /// <summary>
    /// 最終ビルド表記文字列を組み立てる。
    /// <para>
    /// LatestAiredTvEpisode あり → 「YYYY年M月D日現在 『○○プリキュア』第n話時点の情報を表示しています」
    /// </para>
    /// <para>
    /// LatestAiredTvEpisode なし（クリーン DB 等） → 「YYYY年M月D日現在の情報を表示しています」
    /// </para>
    /// 時刻部分は付けない方針（変更概要 D の指示文に時刻表記が無いため、日単位までの粒度）。
    /// </summary>
    private static string BuildBuildLabel(DateTime buildAt, (Series Series, Episode Episode)? latest)
    {
        string datePart = $"{buildAt.Year}年{buildAt.Month}月{buildAt.Day}日現在";
        if (latest is null) return $"{datePart}の情報を表示しています";
        var (series, episode) = latest.Value;
        return $"{datePart} 『{series.Title}』第{episode.SeriesEpNo}話時点の情報を表示しています";
    }

    /// <summary>
    /// 記念日 JS 用の JSON を生成する。各エピソードの放送日 (年月日) と表示に必要な属性のみを
    /// 配列として埋め込む。クライアント側 anniversaries.js が「今日」と月日比較してフィルタする。
    /// </summary>
    /// <remarks>
    /// JSON のプロパティ名は短縮（容量削減）：
    ///   y = year, m = month, d = day,
    ///   st = series title, ss = series slug,
    ///   en = episode no, et = episode title text, eu = episode url
    /// </remarks>
    private static string BuildAnniversaryJson(IReadOnlyList<EpisodeWithSeries> allEpisodes)
    {
        var items = allEpisodes
            .OrderBy(x => x.Episode.OnAirAt)
            .Select(x => new AnniversaryJsonItem
            {
                y = x.Episode.OnAirAt.Year,
                m = x.Episode.OnAirAt.Month,
                d = x.Episode.OnAirAt.Day,
                st = x.Series.Title,
                ss = x.Series.Slug,
                en = x.Episode.SeriesEpNo,
                et = x.Episode.TitleText,
                eu = PathUtil.EpisodeUrl(x.Series.Slug, x.Episode.SeriesEpNo)
            })
            .ToList();

        var options = new JsonSerializerOptions
        {
            // 日本語文字をそのまま出す（\uXXXX エスケープを避けて容量削減）。
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = false
        };
        return JsonSerializer.Serialize(items, options);
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

    private static ProductRow ToProductRow(Product p, IReadOnlyDictionary<string, ProductKind> productKindMap) => new()
    {
        ProductCatalogNo = p.ProductCatalogNo,
        Title = p.Title,
        ReleaseDate = FormatJpDateOnly(p.ReleaseDate),
        ProductKindLabel = productKindMap.TryGetValue(p.ProductKindCode, out var pk) ? pk.NameJa : p.ProductKindCode,
        ProductUrl = PathUtil.ProductUrl(p.ProductCatalogNo)
    };

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

    private static string FormatJpDateOnly(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    // ────────────────────────────────────────────────────────────────────
    // テンプレ用 DTO 群
    // ────────────────────────────────────────────────────────────────────

    private sealed class EpisodeWithSeries
    {
        public Episode Episode { get; set; } = null!;
        public Series Series { get; set; } = null!;
    }

    private sealed class HomeContentModel
    {
        public string SiteName { get; set; } = "";
        /// <summary>
        /// 最終ビルド表記の表示文字列（v1.3.0 ブラッシュアップ続編で導入）。
        /// 「YYYY年M月D日現在 『○○プリキュア』第n話時点の情報を表示しています」のような
        /// 完成形を C# 側で組み立てて流し込む。
        /// </summary>
        public string BuildLabel { get; set; } = "";
        public IReadOnlyList<EpisodeRow> LatestEpisodes { get; set; } = Array.Empty<EpisodeRow>();
        public IReadOnlyList<EpisodeRow> UpcomingEpisodes { get; set; } = Array.Empty<EpisodeRow>();
        public IReadOnlyList<ProductRow> LatestProducts { get; set; } = Array.Empty<ProductRow>();
        public IReadOnlyList<ProductRow> UpcomingProducts { get; set; } = Array.Empty<ProductRow>();
        public DbStatsModel DbStats { get; set; } = new();
        /// <summary>記念日 JS 用の全エピソード放送日 JSON（短縮プロパティ）。</summary>
        public string AnniversaryJson { get; set; } = "[]";
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

    private sealed class ProductRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string ProductKindLabel { get; set; } = "";
        public string ProductUrl { get; set; } = "";
    }

    /// <summary>
    /// テンプレ側で表示するデータベース統計モデル（v1.3.0 ブラッシュアップ続編で 11 項目化）。
    /// <list type="bullet">
    ///   <item><description>シリーズは TV / 映画（親作品のみ）/ スピンオフ の 3 種に分離。</description></item>
    ///   <item><description>「歌」は <c>song_recordings</c> 行数（楽曲のレコーディング単位、サイズ・パート違い別カウント）。</description></item>
    ///   <item><description>「劇伴」は <c>bgm_cues</c> 行数（仮 M 番号も含む）。</description></item>
    ///   <item><description>「音楽商品」は <c>products</c>（点数）と <c>discs</c>（枚数）を「N点M枚」表記で 1 セルに集約。</description></item>
    /// </list>
    /// </summary>
    private sealed class DbStatsModel
    {
        public int TvSeriesCount { get; set; }
        public int MovieSeriesCount { get; set; }
        public int SpinOffSeriesCount { get; set; }
        public int EpisodeCount { get; set; }
        public int PrecuresCount { get; set; }
        public int CharactersCount { get; set; }
        public int SongsCount { get; set; }
        public int BgmsCount { get; set; }
        /// <summary>「N点M枚」整形済み文字列（テンプレ側で再組立しなくて済むように）。</summary>
        public string MusicProductsLabel { get; set; } = "";
        public int PersonsCount { get; set; }
        public int CompaniesCount { get; set; }
    }

    /// <summary>記念日 JSON の 1 件分（プロパティ名は容量節約のため短縮形）。</summary>
    private sealed class AnniversaryJsonItem
    {
        public int y { get; set; }
        public int m { get; set; }
        public int d { get; set; }
        public string st { get; set; } = "";
        public string ss { get; set; } = "";
        public int en { get; set; }
        public string et { get; set; } = "";
        public string eu { get; set; } = "";
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
