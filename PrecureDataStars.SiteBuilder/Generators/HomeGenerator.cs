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
/// サイトトップ <c>/</c> の生成。
/// <para>
/// 主な変更点：
/// <list type="bullet">
///   <item><description>「今日の記念日」はビルド時計算をやめて全エピソードの放送日（年月日）を
///     JSON として埋め込み、クライアント側 JavaScript で「今日」を動的に判定して描画する方式に変更。
///     ビルド日と閲覧日がズレても、サイトを開いた瞬間の「今日」で記念日が出る。</description></item>
///   <item><description>「最終ビルド」表記を「○○年○○月○○日現在 『○○プリキュア』第n話時点の情報を表示しています」
///     形式で表示する。基準点は <see cref="BuildContext.LatestAiredTvEpisode"/>（全 TV シリーズを横断した
///     最新放送済話）。LatestAiredTvEpisode が null のときはプリキュア部分を省略。</description></item>
///   <item><description>データベース統計セクションはコンパクト表示（横並び 1 行）、項目は 11 個。
///     TV シリーズ／映画／スピンオフ を分離し、プリキュア人数・歌（song_recordings 単位）・
///     劇伴（bgm_cues 単位）・音楽商品「N点M枚」を追加。映画の作数は親作品（ParentSeriesId が
///     null のもの）のみカウント。</description></item>
///   <item><description>「このサイトの特徴」セクションは削除。</description></item>
///   <item><description>「TV シリーズ」一覧表は削除し、シリーズ一覧へのリンク 1 行に集約。</description></item>
///   <item><description>「今週の記念日」セクションは持たない、
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

    // SeriesGenerator が memoize した「episode_id → EpisodeStaffSummary」の参照。
    // 「今後の放送予定」「最新エピソード」を /episodes/ と同一の episodes-index-section
    // 構造（スタッフバッジ段つき）で描画するために使う。クレジット添付対象
    // （credit_attach_to=EPISODE）のシリーズ配下エピソードのみ詰まっている。
    private readonly IReadOnlyDictionary<int, EpisodeStaffSummary> _episodeStaffByIdCache;

    /// <summary>最新エピソード：ビルド済みの直近放送を上位 N 件。</summary>
    private const int LatestEpisodesMax = 6;

    /// <summary>次回予告：今日以降の N 件。</summary>
    private const int UpcomingEpisodesMax = 4;

    /// <summary>新着商品：直近発売された N 件。</summary>
    private const int LatestProductsMax = 6;

    /// <summary>間もなく発売：今日以降の N 件。</summary>
    private const int UpcomingProductsMax = 6;

    public HomeGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        IReadOnlyDictionary<int, EpisodeStaffSummary> episodeStaffByIdCache)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _episodeStaffByIdCache = episodeStaffByIdCache;
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
            // 子作品（'MOVIE_SHORT'）は単独詳細ページを生成しないので、配下のエピソードは
            // ホームのリンク対象から除外。SPIN-OFF / OTONA / SHORT / EVENT は単独ページがあるので含める。
            if (SeriesClassifier.IsMovieShortChild(s)) continue;
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

        var latestEpisodeSections = BuildLatestEpisodeSections(allEpisodes, buildAt);
        var upcomingEpisodeSections = BuildUpcomingEpisodeSections(allEpisodes, buildAt);
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
            // 最終ビルド表記は「○○年○○月○○日現在 『○○プリキュア』第n話時点
            // の情報を表示しています」形式。基準点は LatestAiredTvEpisode（全 TV シリーズを横断した
            // 最新放送済話）。該当が無いとき（クリーン DB 等）はプリキュア部分を省略する。
            BuildLabel = BuildBuildLabel(buildAt, _ctx.LatestAiredTvEpisode),
            LatestEpisodeSections = latestEpisodeSections,
            UpcomingEpisodeSections = upcomingEpisodeSections,
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
    /// 「最新エピソード」をシリーズ単位の episodes-index-section リストに組み立てる。
    /// <para>
    /// エピソードは放送日降順で上位 <see cref="LatestEpisodesMax"/> 件を抽出し、
    /// それをシリーズ単位にグルーピングする。各シリーズ内のエピソードは放送日降順、
    /// セクション（シリーズ）の並びは「各シリーズ内の最大放送日（=そのシリーズで最も新しい話）」
    /// の降順。表示構造は /episodes/ ランディングと同一の episodes-index-section
    /// （見出し + ep-row + スタッフバッジ段）で、サブタイトルは共通の太字 .ep-row-title。
    /// </para>
    /// </summary>
    private IReadOnlyList<HomeEpisodeSection> BuildLatestEpisodeSections(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, DateTime today)
    {
        var picked = allEpisodes
            .Where(x => x.Episode.OnAirAt <= today)
            .OrderByDescending(x => x.Episode.OnAirAt)
            .Take(LatestEpisodesMax)
            .ToList();
        // シリーズ単位にまとめ、セクション順は各シリーズ内の最大放送日降順
        // （= 表示中のエピソードのうち最も新しい話を持つシリーズが先頭）。
        // セクション内のエピソードは放送日降順。
        return picked
            .GroupBy(x => x.Series.SeriesId)
            .Select(g => BuildHomeEpisodeSection(
                g.OrderByDescending(x => x.Episode.OnAirAt).ToList()))
            .OrderByDescending(sec => sec.SortKeyTicks)
            .ToList();
    }

    /// <summary>
    /// 「今後の放送予定」をシリーズ単位の episodes-index-section リストに組み立てる。
    /// <para>
    /// エピソードは放送日昇順で直近 <see cref="UpcomingEpisodesMax"/> 件を抽出し、
    /// シリーズ単位にグルーピングする。各シリーズ内のエピソードは放送日昇順、
    /// セクション（シリーズ）の並びは「各シリーズ内の最小放送日（=そのシリーズで最も早い予定）」
    /// の昇順。表示構造・意匠は「最新エピソード」と同じ episodes-index-section。
    /// </para>
    /// </summary>
    private IReadOnlyList<HomeEpisodeSection> BuildUpcomingEpisodeSections(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, DateTime today)
    {
        var picked = allEpisodes
            .Where(x => x.Episode.OnAirAt > today)
            .OrderBy(x => x.Episode.OnAirAt)
            .Take(UpcomingEpisodesMax)
            .ToList();
        // セクション順は各シリーズ内の最小放送日昇順、セクション内エピソードは放送日昇順。
        return picked
            .GroupBy(x => x.Series.SeriesId)
            .Select(g =>
            {
                var rows = g.OrderBy(x => x.Episode.OnAirAt).ToList();
                var sec = BuildHomeEpisodeSection(rows);
                // 昇順ソート用キーは「シリーズ内の最小放送日」。BuildHomeEpisodeSection は
                // 先頭行の放送日を SortKeyTicks に入れるが、昇順グループでは先頭=最小なので
                // そのまま昇順ソートに使える。
                return sec;
            })
            .OrderBy(sec => sec.SortKeyTicks)
            .ToList();
    }

    /// <summary>
    /// シリーズ単位のエピソード群（並び順は呼び出し側で確定済み）から
    /// 1 つの <see cref="HomeEpisodeSection"/> を組み立てる共通ヘルパ。
    /// 各エピソードに <see cref="_episodeStaffByIdCache"/> のスタッフサマリを引き当て、
    /// /episodes/ と同じ 5 役職バッジ段つきの行を作る。日付は密表示用に
    /// 「2024.2.4」形式（<see cref="JpDateFormat.DotDate"/>）へ統一する。
    /// </summary>
    private HomeEpisodeSection BuildHomeEpisodeSection(IReadOnlyList<EpisodeWithSeries> rowsInOrder)
    {
        var first = rowsInOrder[0];
        var series = first.Series;
        var rows = rowsInOrder.Select(x =>
        {
            // クレジット未登録などでキャッシュに無いエピソードは全フィールド空のサマリ。
            var staff = _episodeStaffByIdCache.TryGetValue(x.Episode.EpisodeId, out var ss)
                ? ss
                : new EpisodeStaffSummary();
            return new HomeEpisodeRow
            {
                SeriesEpNo = x.Episode.SeriesEpNo,
                TitleText = x.Episode.TitleText,
                // /episodes/ ランディングと同一仕様：ルビ付きサブタイトル HTML が
                // あればそれを優先表示する（テンプレ側で空判定）。/episodes/ と同じく
                // ここでは <br> 除去等の加工はせず DB 値をそのまま流す。
                TitleRichHtml = x.Episode.TitleRichHtml ?? "",
                OnAirDate = JpDateFormat.DotDate(x.Episode.OnAirAt),
                EpisodeUrl = PathUtil.EpisodeUrl(series.Slug, x.Episode.SeriesEpNo),
                Screenplay = staff.Screenplay,
                Storyboard = staff.Storyboard,
                EpisodeDirector = staff.EpisodeDirector,
                AnimationDirector = staff.AnimationDirector,
                ArtDirector = staff.ArtDirector,
                StoryboardDirectorMerged = staff.StoryboardDirectorMerged
            };
        }).ToList();

        return new HomeEpisodeSection
        {
            SeriesSlug = series.Slug,
            SeriesTitle = series.Title,
            SeriesStartYearLabel = series.StartDate.Year.ToString(),
            // セクション並べ替え用キー：呼び出し側で並び確定済みの先頭行の放送日。
            // 「最新」では降順グループの先頭=そのシリーズ最大放送日、
            // 「今後」では昇順グループの先頭=そのシリーズ最小放送日になる。
            SortKeyTicks = first.Episode.OnAirAt.Ticks,
            Episodes = rows
        };
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
    /// 項目数は 11 個。
    /// <list type="bullet">
    ///   <item><description>TV シリーズ・映画（親作品のみ）・スピンオフを別カウントに分離。</description></item>
    ///   <item><description>プリキュア人数を <see cref="PrecuresRepository"/> から追加取得。</description></item>
    ///   <item><description>歌は <see cref="SongRecordingsRepository"/> ベース（楽曲のレコーディング単位）。
    ///     <see cref="SongsRepository"/> ベースは使わない。</description></item>
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
        // 映画系の親作品は 'MOVIE'（秋映画）と 'SPRING'（春映画）の 2 種
        // （セクション仕様：'MOVIE_SHORT' は子作品なので
        // カウントから除外、parent_series_id の有無は問わない）。
        // スピンオフ系は第 3 弾で 4 種別に細分化し、ホーム統計では合計件数を 1 ボックスで表示する：
        // 'OTONA'（大人向け）・'SHORT'（ショートアニメ）・'EVENT'（イベント）・'SPIN-OFF'（狭義のスピンオフ）。
        int tvSeriesCount = _ctx.Series.Count(s =>
            string.Equals(s.KindCode, "TV", StringComparison.Ordinal));
        int movieSeriesCount = _ctx.Series.Count(s =>
            string.Equals(s.KindCode, "MOVIE",  StringComparison.Ordinal)
         || string.Equals(s.KindCode, "SPRING", StringComparison.Ordinal));
        int spinOffSeriesCount = _ctx.Series.Count(s =>
            string.Equals(s.KindCode, "OTONA",    StringComparison.Ordinal)
         || string.Equals(s.KindCode, "SHORT",    StringComparison.Ordinal)
         || string.Equals(s.KindCode, "EVENT",    StringComparison.Ordinal)
         || string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal));

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
                // D-3：記念日は ep-row 単位で各行の上に「n年前　シリーズ (放送年度)」を出す。
                // 放送年度＝シリーズ開始年。スタッフバッジは容量・JS 規模の都合で載せない。
                sy = x.Series.StartDate.Year,
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

    private static ProductRow ToProductRow(Product p, IReadOnlyDictionary<string, ProductKind> productKindMap) => new()
    {
        ProductCatalogNo = p.ProductCatalogNo,
        Title = p.Title,
        ReleaseDate = JpDateFormat.Date(p.ReleaseDate),
        ProductKindLabel = productKindMap.TryGetValue(p.ProductKindCode, out var pk) ? pk.NameJa : p.ProductKindCode,
        ProductUrl = PathUtil.ProductUrl(p.ProductCatalogNo)
    };

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
        /// 最終ビルド表記の表示文字列（導入）。
        /// 「YYYY年M月D日現在 『○○プリキュア』第n話時点の情報を表示しています」のような
        /// 完成形を C# 側で組み立てて流し込む。
        /// </summary>
        public string BuildLabel { get; set; } = "";
        /// <summary>「最新エピソード」をシリーズ単位の episodes-index-section リストで保持。</summary>
        public IReadOnlyList<HomeEpisodeSection> LatestEpisodeSections { get; set; } = Array.Empty<HomeEpisodeSection>();
        /// <summary>「今後の放送予定」をシリーズ単位の episodes-index-section リストで保持。</summary>
        public IReadOnlyList<HomeEpisodeSection> UpcomingEpisodeSections { get; set; } = Array.Empty<HomeEpisodeSection>();
        public IReadOnlyList<ProductRow> LatestProducts { get; set; } = Array.Empty<ProductRow>();
        public IReadOnlyList<ProductRow> UpcomingProducts { get; set; } = Array.Empty<ProductRow>();
        public DbStatsModel DbStats { get; set; } = new();
        /// <summary>記念日 JS 用の全エピソード放送日 JSON（短縮プロパティ）。</summary>
        public string AnniversaryJson { get; set; } = "[]";
    }

    /// <summary>
    /// ホームの「今後の放送予定」「最新エピソード」を /episodes/ と同一の
    /// episodes-index-section 構造で描画するための 1 シリーズ分のセクション。
    /// </summary>
    private sealed class HomeEpisodeSection
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズ開始年の西暦 4 桁（見出しに薄色括弧で添える）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>
        /// セクション（シリーズ）の並べ替え用キー。呼び出し側で並び確定済みの
        /// 先頭エピソード放送日の Ticks。最新=降順／今後=昇順でそのまま使う。
        /// テンプレには出さない内部用。
        /// </summary>
        public long SortKeyTicks { get; set; }
        public IReadOnlyList<HomeEpisodeRow> Episodes { get; set; } = Array.Empty<HomeEpisodeRow>();
    }

    /// <summary>
    /// ホームのエピソードセクション内 1 行。/episodes/ の EpisodesIndexRow と同等の
    /// 表示項目（第N話・放送日・サブタイトル・5 役職スタッフ HTML 断片）を持つ。
    /// </summary>
    private sealed class HomeEpisodeRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        /// <summary>
        /// ルビ付きサブタイトル HTML（DB の <c>episodes.title_rich_html</c> 素通し）。
        /// /episodes/ ランディングと同一仕様で、非空ならテンプレ側でこれを優先表示し、
        /// 空なら <see cref="TitleText"/> のエスケープ平文をフォールバックする。
        /// </summary>
        public string TitleRichHtml { get; set; } = "";
        /// <summary>放送日（密表示用「2024.2.4」形式に統一）。</summary>
        public string OnAirDate { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
        /// <summary>絵コンテ＝演出が同一人物のとき true（テンプレ側で 2 バッジ統合）。</summary>
        public bool StoryboardDirectorMerged { get; set; }
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
    /// テンプレ側で表示するデータベース統計モデル（11 項目化）。
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
        /// <summary>シリーズ放送年度（開始年・西暦 4 桁）。記念日行の「シリーズ (放送年度)」表示用。</summary>
        public int sy { get; set; }
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
