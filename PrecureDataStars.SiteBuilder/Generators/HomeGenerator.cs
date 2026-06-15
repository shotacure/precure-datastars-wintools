using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// 「今後の放送予定 / 最新エピソード / 音楽商品の発売予定 / 新着の音楽商品」はビルド時生成のまま残すが、件数は控えめに。
/// 新着の音楽商品は「ジャケット画像ありの発売済みを新しい順に 9 点」（画像未着の商品はスキップして繰り上げ）。
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

    /// <summary>新着商品：ジャケット画像ありの発売済み商品を新しい順に何点出すか。</summary>
    private const int LatestProductsMax = 9;

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

        // 商品カード（ホームの「発売予定」「新着」セクション）にシリーズ名を出すため、
        // 全ディスクを 1 回引いて product_catalog_no でグループ化したマップを作る。
        // 個別商品ごとに DB を叩くと N+1 になるため、ホーム生成では事前に全件まとめてロードする。
        var discsRepo = new DiscsRepository(_factory);
        var allDiscs = await discsRepo.GetByProductReleaseOrderAsync(ct).ConfigureAwait(false);
        var discsByProductCatalogNo = allDiscs
            .GroupBy(d => d.ProductCatalogNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Disc>)g.ToList(), StringComparer.Ordinal);

        // Amazon アソシエイトタグ（?tag= 付与用）。未設定なら空文字でリンクは出すが tag は付かない。
        string amazonTag = _ctx.Config.AmazonAssociateTag ?? "";

        var todayDate = DateOnly.FromDateTime(buildAt);

        var latestEpisodeSections = BuildLatestEpisodeSections(allEpisodes, buildAt);
        var upcomingEpisodeSections = BuildUpcomingEpisodeSections(allEpisodes, buildAt);
        var latestProducts = BuildLatestProducts(allProducts, productKindMap, discsByProductCatalogNo, _ctx.SeriesById, amazonTag, todayDate);
        var upcomingProducts = BuildUpcomingProducts(allProducts, productKindMap, discsByProductCatalogNo, _ctx.SeriesById, amazonTag, todayDate);
        var dbStats = await BuildDbStatsAsync(allEpisodes.Count, ct).ConfigureAwait(false);

        // キャラクター・クリエーターのデータ充足率（暫定表記。テスト・本番とも表示）。
        var dataSufficiencyLabel = await BuildDataSufficiencyLabelAsync(ct).ConfigureAwait(false);

        // 本放送フォーマットの調査完了率（暫定表記。テスト・本番とも表示）。
        var broadcastFormatLabel = await BuildBroadcastFormatLabelAsync(ct).ConfigureAwait(false);

        // 記念日 / 今月のカレンダー JS 用データ。エピソード放送日に加えて、映画公開日・
        // キャラクター誕生日・人物誕生日を 1 つの配列に種別タグ k 付きで埋め込む。
        // クライアント側（anniversaries.js / calendar.js）が「閲覧日」基準で抽出・描画する。
        // データ量を抑えるためプロパティ名は短縮形（参考：search-index.json と同じ方針）。
        var anniversaryJson = await BuildCalendarDataJsonAsync(allEpisodes, ct).ConfigureAwait(false);

        var content = new HomeContentModel
        {
            SiteName = _ctx.Config.SiteName,
            // 本番モードでは DB 統計ボックスのうちプリキュア・キャラクター・クリエーターを隠す
            // （データが揃いきるまでの暫定措置。ヘッダナビの ProductionHiddenNavUrls と歩調を合わせる）。
            IsProductionMode = _ctx.Config.IsProductionMode,
            // 最終ビルド表記は「○○年○○月○○日現在 『○○プリキュア』第n話時点
            BuildLabel = BuildBuildLabel(_ctx.LatestAiredTvEpisode),
            DataSufficiencyLabel = dataSufficiencyLabel,
            BroadcastFormatLabel = broadcastFormatLabel,
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
            ["description"] = "歴代プリキュアシリーズのエピソード・音楽・スタッフ・キャラクターを横断的に閲覧できる、個人運営の非公式ファンデータベースです。",
            ["url"] = string.IsNullOrEmpty(baseUrl) ? null : baseUrl + "/",
            ["inLanguage"] = "ja"
        });

        var layout = new LayoutModel
        {
            // SEO：ホームの <title>・og:title / twitter:title に日本語キーワードを載せる。
            // 表示用の見出しには使われない（home.sbn の h1 は SiteName 固定・パンくず無し）ため、
            // 可視表示は precure-datastars のままで、検索・シェア時のタイトルだけが日本語になる。
            // <title> は "{PageTitle} | {SiteName}" 形式なので「プリキュアまるごとデータベース | precure-datastars」になる。
            PageTitle = "プリキュアまるごとデータベース",
            MetaDescription = "歴代プリキュアの全話リスト、主題歌・劇伴、スタッフ・声優、キャラクターまで。「好き」を深掘りするための情報をファンの手で集めた、個人運営の非公式ファンデータベースです。",
            Breadcrumbs = Array.Empty<BreadcrumbItem>(),
            OgType = "website",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite("/", "home", "home.sbn", content, layout);
        _ctx.Logger.Success("/");
    }

    /// <summary>
    /// トップに出す「キャラクター・クリエーターのデータ充足率」ラベルを組み立てる。
    /// 充足率 ＝ (OP・ED 両方のクレジットが揃っている最後の TV 話の通算話数) ÷
    /// (パートが入力されている最後の TV 話の通算話数)。フロンティア（最後に揃っている回）の
    /// シリーズ名・話数も添える。クレジット入力が現在に追いつくまでの暫定表記で、テスト・本番とも表示する
    /// （OP/ED のスキップは現在付近でのみ起こり、フロンティアは過去にあるため厳密考慮は不要）。
    /// 値が取れない場合は空文字を返す（ラベル非表示）。
    /// </summary>
    private async Task<string> BuildDataSufficiencyLabelAsync(CancellationToken ct)
    {
        // OP・ED 両方のクレジットが揃っている TV 話のうち、通算話数が最大（＝放送順で最後）の 1 件。
        const string sqlFrontier = @"
SELECT s.title AS Title, e.series_ep_no AS Ep, e.total_ep_no AS TotalEp
FROM episodes e JOIN series s ON s.series_id = e.series_id
WHERE s.kind_code = 'TV' AND e.is_deleted = 0 AND e.total_ep_no IS NOT NULL
  AND EXISTS(SELECT 1 FROM credits c WHERE c.episode_id = e.episode_id AND c.scope_kind = 'EPISODE' AND c.is_deleted = 0 AND c.credit_kind = 'OP')
  AND EXISTS(SELECT 1 FROM credits c WHERE c.episode_id = e.episode_id AND c.scope_kind = 'EPISODE' AND c.is_deleted = 0 AND c.credit_kind = 'ED')
ORDER BY e.total_ep_no DESC
LIMIT 1;";

        // パートが入力されている（＝放送済の）TV 話のうち、通算話数の最大値。
        const string sqlDenominator = @"
SELECT MAX(e.total_ep_no)
FROM episodes e JOIN series s ON s.series_id = e.series_id
WHERE s.kind_code = 'TV' AND e.is_deleted = 0 AND e.total_ep_no IS NOT NULL
  AND EXISTS(SELECT 1 FROM episode_parts p WHERE p.episode_id = e.episode_id);";

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var frontier = await conn.QueryFirstOrDefaultAsync<FrontierRow>(
            new CommandDefinition(sqlFrontier, cancellationToken: ct)).ConfigureAwait(false);
        int? denominator = await conn.ExecuteScalarAsync<int?>(
            new CommandDefinition(sqlDenominator, cancellationToken: ct)).ConfigureAwait(false);

        if (frontier is null || denominator is not int den || den <= 0) return "";

        double pct = (double)frontier.TotalEp / den * 100.0;
        return $"（キャラクターおよびクリエーターのデータ充足率：{pct:0.00}%『{frontier.Title}』第{frontier.Ep}話まで）";
    }

    /// <summary>充足率フロンティア（OP・ED 揃いの最終話）の行。</summary>
    private sealed class FrontierRow
    {
        public string Title { get; set; } = "";
        public int Ep { get; set; }
        public int TotalEp { get; set; }
    }

    /// <summary>
    /// トップに出す「本放送フォーマットの調査完了率」ラベルを組み立てる。
    /// 調査完了率 ＝ (パートが入力されている話のうち「（本放送フォーマットは現在調査中です）」が
    /// 出ない話数) ÷ (パートが入力されている総話数)。「調査中」表記はいずれかのパート備考
    /// （episode_parts.notes）に「【本放送未確認】」を含む話で出るため、それを 1 つも含まない話を
    /// 完了扱いとする。あわせて調査中の最古話（放送日が最も古い未確認話）のシリーズ名・話数を添える。
    /// 本放送フォーマット調査が現在に追いつくまでの暫定表記で、テスト・本番とも表示する。
    /// 値が取れない場合は空文字を返す（ラベル非表示）。
    /// </summary>
    private async Task<string> BuildBroadcastFormatLabelAsync(CancellationToken ct)
    {
        // パートが入力されている話の総数（分母）と、そのうち「【本放送未確認】」マーカーを
        // 含むパートを 1 つでも持つ話（＝調査中表記が出る話）の数を 1 クエリで集計する。
        // BIGINT で受けるため SIGNED に明示キャストする。
        const string sqlCounts = @"
SELECT
  CAST(COUNT(*) AS SIGNED) AS Total,
  CAST(SUM(CASE WHEN EXISTS(SELECT 1 FROM episode_parts p
                            WHERE p.episode_id = e.episode_id AND p.notes LIKE '%【本放送未確認】%')
                THEN 1 ELSE 0 END) AS SIGNED) AS UnderInvestigation
FROM episodes e
WHERE e.is_deleted = 0
  AND EXISTS(SELECT 1 FROM episode_parts p WHERE p.episode_id = e.episode_id);";

        // 調査中（【本放送未確認】を含む）話のうち、放送日が最も古い 1 件（＝調査フロンティア）。
        const string sqlOldest = @"
SELECT s.title AS Title, e.series_ep_no AS Ep
FROM episodes e JOIN series s ON s.series_id = e.series_id
WHERE e.is_deleted = 0
  AND EXISTS(SELECT 1 FROM episode_parts p
             WHERE p.episode_id = e.episode_id AND p.notes LIKE '%【本放送未確認】%')
ORDER BY e.on_air_at ASC, e.episode_id ASC
LIMIT 1;";

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var counts = await conn.QueryFirstOrDefaultAsync<BroadcastFormatCountsRow>(
            new CommandDefinition(sqlCounts, cancellationToken: ct)).ConfigureAwait(false);

        if (counts is null || counts.Total <= 0) return "";

        long investigated = counts.Total - counts.UnderInvestigation;
        double pct = (double)investigated / counts.Total * 100.0;

        // 調査中の話が無ければ（＝100% 完了）、調査中フロンティアの併記は省く。
        if (counts.UnderInvestigation <= 0)
            return $"（本放送フォーマットの調査完了率：{pct:0.00}%）";

        var oldest = await conn.QueryFirstOrDefaultAsync<BroadcastFormatFrontierRow>(
            new CommandDefinition(sqlOldest, cancellationToken: ct)).ConfigureAwait(false);
        if (oldest is null)
            return $"（本放送フォーマットの調査完了率：{pct:0.00}%）";

        return $"（本放送フォーマットの調査完了率：{pct:0.00}% 調査中：『{oldest.Title}』第{oldest.Ep}話）";
    }

    /// <summary>本放送フォーマット調査の件数集計（分母＝パート有り総話数、調査中話数）。</summary>
    private sealed class BroadcastFormatCountsRow
    {
        public long Total { get; set; }
        public long UnderInvestigation { get; set; }
    }

    /// <summary>本放送フォーマット調査の最古フロンティア（放送日が最も古い調査中話）の行。</summary>
    private sealed class BroadcastFormatFrontierRow
    {
        public string Title { get; set; } = "";
        public int Ep { get; set; }
    }

    /// <summary>「最新エピソード」をシリーズ単位の episodes-index-section リストに組み立てる。</summary>
    private IReadOnlyList<HomeEpisodeSection> BuildLatestEpisodeSections(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, DateTime today)
    {
        var picked = allEpisodes
            .Where(x => x.Episode.OnAirAt <= today)
            .OrderByDescending(x => x.Episode.OnAirAt)
            .Take(LatestEpisodesMax)
            .ToList();
        // シリーズ単位にまとめ、セクション順は各シリーズ内の最大放送日降順
        return picked
            .GroupBy(x => x.Series.SeriesId)
            .Select(g => BuildHomeEpisodeSection(
                g.OrderByDescending(x => x.Episode.OnAirAt).ToList()))
            .OrderByDescending(sec => sec.SortKeyTicks)
            .ToList();
    }

    /// <summary>「今後の放送予定」をシリーズ単位の episodes-index-section リストに組み立てる。</summary>
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
        IReadOnlyDictionary<string, IReadOnlyList<Disc>> discsByProductCatalogNo,
        IReadOnlyDictionary<int, Series> seriesById,
        string amazonTag,
        DateOnly today)
    {
        // 発売済み商品を新しい順に 9 点並べる。本セクションはジャケット画像を見せるのが
        // 主目的のため、画像が揃っていない商品（CoverImageUrl が空）はスキップして次の候補を繰り上げる。
        return products
            .Where(p => DateOnly.FromDateTime(p.ReleaseDate) <= today && !string.IsNullOrEmpty(p.CoverImageUrl))
            .OrderByDescending(p => p.ReleaseDate)
            .Take(LatestProductsMax)
            .Select(p => ToProductRow(p, productKindMap, discsByProductCatalogNo, seriesById, amazonTag, today))
            .ToList();
    }

    private static IReadOnlyList<ProductRow> BuildUpcomingProducts(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        IReadOnlyDictionary<string, IReadOnlyList<Disc>> discsByProductCatalogNo,
        IReadOnlyDictionary<int, Series> seriesById,
        string amazonTag,
        DateOnly today)
    {
        return products
            .Where(p => DateOnly.FromDateTime(p.ReleaseDate) > today)
            .OrderBy(p => p.ReleaseDate)
            .Take(UpcomingProductsMax)
            .Select(p => ToProductRow(p, productKindMap, discsByProductCatalogNo, seriesById, amazonTag, today))
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
    ///   <item><description>音楽商品は「N 点 M 枚」表記。点数は products、枚数は discs を別々にカウントし、それぞれ独立した整数プロパティとしてテンプレへ供給する。</description></item>
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
            // 「N 点 M 枚」をホームの DB 統計セル内で他の統計セル（曲・劇伴 等）と同じ「数値＋ラベル」
            // ペアの並びとして見せたいので、点数（products）と枚数（discs）はそれぞれ独立した
            // 整数プロパティとしてテンプレへ渡す。テンプレ側で home-db-stats-value/-label ペアを
            // 2 組並べる構造に組み立てる。
            MusicProductsCount = productsCount,
            MusicDiscsCount = discsCount,
            PersonsCount = personsCount,
            CompaniesCount = companiesCount
        };
    }

    /// <summary>最終ビルド表記文字列を組み立てる。 LatestAiredTvEpisode あり → 「YYYY年M月D日現在 『○○プリキュア』第n話時点の情報を表示しています」 （日付は当該エピソードの <see cref="Episode.OnAirAt"/> ベース。サイト共通の <see cref="Utilities.StatsCoverageLabel"/> と挙動を統一）。 LatestAiredTvEpisode なし（クリーン DB 等） → 空文字を返してテンプレ側で非表示にする。 時刻部分は付けない方針（変更概要 D の指示文に時刻表記が無いため、日単位までの粒度）。 「ビルド日付」は内部進行管理であってユーザー向け情報ではないため一切表に出さない。</summary>
    private static string BuildBuildLabel((Series Series, Episode Episode)? latest)
    {
        if (latest is null) return string.Empty;
        var (series, episode) = latest.Value;
        var oa = episode.OnAirAt;
        string datePart = $"{oa.Year}年{oa.Month}月{oa.Day}日現在";
        return $"{datePart} 『{series.Title}』第{episode.SeriesEpNo}話時点の情報を表示しています";
    }

    /// <summary>記念日（今日の記念日）と「今月のカレンダー」JS 用の統合 JSON を生成する。</summary>
    /// プロパティ名は容量削減のため短縮形。共通: k(種別), m(月), d(日)。
    ///   ep: y(放送年) st(シリーズ名) ss(slug) ts(シリーズ略称) sy(開始年) en(話数) et(サブタイトル) eu(URL)
    ///   mv: y(公開年) ts(シリーズ略称) st ss su(シリーズ URL) sy
    ///   cb: cn(正式名称) pn(変身前名義/カレンダー表示名) cu(詳細 URL) kc/kf/kb(バッジ色) st ss su sy
    ///   pb: pn(氏名) pu(人物 URL) by(生年。PUBLIC かつ判明時のみ。それ以外は省略)
    private async Task<string> BuildCalendarDataJsonAsync(
        IReadOnlyList<EpisodeWithSeries> allEpisodes, CancellationToken ct)
    {
        var items = new List<object>();

        // ── シリーズごとの最終話番号 ──
        // 「最終回」の定義は series.episodes（マスタの総話数）が示す回。
        // episodes テーブルへの登録進度や EndDate の有無には依存させない
        // （マスタが先行宣言した総話数で最終話判定する。総話数未設定のシリーズは
        // 最終話マーカーを持たない）。
        var lastEpNoBySeries = new Dictionary<int, int>();
        foreach (var s in _ctx.Series)
        {
            if (s.Episodes is ushort total && total > 0)
                lastEpNoBySeries[s.SeriesId] = total;
        }

        // ── エピソード（今日の記念日 + カレンダー）──
        foreach (var x in allEpisodes.OrderBy(x => x.Episode.OnAirAt))
        {
            bool isFirst = x.Episode.SeriesEpNo == 1;
            bool isLast = lastEpNoBySeries.TryGetValue(x.Series.SeriesId, out var lastNo)
                          && x.Episode.SeriesEpNo == lastNo;
            items.Add(new
            {
                k = "ep",
                y = x.Episode.OnAirAt.Year,
                m = x.Episode.OnAirAt.Month,
                d = x.Episode.OnAirAt.Day,
                st = x.Series.Title,
                ss = x.Series.Slug,
                // カレンダーのコンパクト表示用にシリーズ略称も持たせる（無ければ正式名にフォールバック）。
                ts = string.IsNullOrEmpty(x.Series.TitleShort) ? x.Series.Title : x.Series.TitleShort,
                sy = x.Series.StartDate.Year,
                en = x.Episode.SeriesEpNo,
                et = x.Episode.TitleText,
                eu = PathUtil.EpisodeUrl(x.Series.Slug, x.Episode.SeriesEpNo),
                // 1 話／最終話のみフラグを立てる（false のときは JSON へ書き出さない）。
                ef = isFirst ? (bool?)true : null,
                el = isLast ? (bool?)true : null
            });
        }

        // ── 映画公開日（MOVIE / SPRING のみ。カレンダー専用。title_short のみ表示）──
        foreach (var s in _ctx.Series
                     .Where(s => string.Equals(s.KindCode, "MOVIE", StringComparison.Ordinal)
                              || string.Equals(s.KindCode, "SPRING", StringComparison.Ordinal))
                     .OrderBy(s => s.StartDate))
        {
            items.Add(new
            {
                k = "mv",
                y = s.StartDate.Year,
                m = s.StartDate.Month,
                d = s.StartDate.Day,
                ts = string.IsNullOrEmpty(s.TitleShort) ? s.Title : s.TitleShort,
                st = s.Title,
                ss = s.Slug,
                su = PathUtil.SeriesUrl(s.Slug),
                sy = s.StartDate.Year
            });
        }

        // ── 誕生日解決用のマスタを 1 度だけロード ──
        var characters = await new CharactersRepository(_factory)
            .GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var persons = await new PersonsRepository(_factory)
            .GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var precures = await new PrecuresRepository(_factory)
            .GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var charAliases = await new CharacterAliasesRepository(_factory)
            .GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var seriesPrecures = await new SeriesPrecuresRepository(_factory)
            .GetAllAsync(ct).ConfigureAwait(false);

        var aliasById = charAliases.ToDictionary(a => a.AliasId);
        // character_id → 代表 precure（最小 precure_id を採って決定的に）。
        var precureByCharacter = new Dictionary<int, Precure>();
        foreach (var pr in precures.OrderBy(pr => pr.PrecureId))
        {
            if (aliasById.TryGetValue(pr.TransformAliasId, out var ta)
                && !precureByCharacter.ContainsKey(ta.CharacterId))
            {
                precureByCharacter[ta.CharacterId] = pr;
            }
        }
        // precure_id → 代表シリーズ（series_precures のうち放送開始が最も早いもの）。
        var seriesByPrecure = new Dictionary<int, Series>();
        foreach (var sp in seriesPrecures)
        {
            if (!_ctx.SeriesById.TryGetValue(sp.SeriesId, out var s)) continue;
            if (seriesByPrecure.TryGetValue(sp.PrecureId, out var cur) && cur.StartDate <= s.StartDate) continue;
            seriesByPrecure[sp.PrecureId] = s;
        }

        // ── キャラクター誕生日（PRECURE / ALLY、月日が揃っているもの）──
        foreach (var c in characters)
        {
            if (!(string.Equals(c.CharacterKind, "PRECURE", StringComparison.Ordinal)
                  || string.Equals(c.CharacterKind, "ALLY", StringComparison.Ordinal))) continue;
            if (c.BirthMonth is not byte cm || c.BirthDay is not byte cd) continue;

            // 既定は正式名称（precure 紐付けの無い ALLY 等）。
            string preName = c.Name;
            string keyColor = "";
            string url = PathUtil.CharacterUrl(c.CharacterId);
            Series? repSeries = null;

            if (precureByCharacter.TryGetValue(c.CharacterId, out var pr))
            {
                keyColor = pr.KeyColor ?? "";
                // プリキュア詳細ページは廃止済みで /characters/{character_id}/ がプリキュア詳細を兼ねる。
                // カレンダーの誕生日チップの遷移先も同 URL に統一する（既に url は CharacterUrl で初期化済み）。
                // カレンダーのプリキュア誕生日は変身前名義で表示する。
                if (aliasById.TryGetValue(pr.PreTransformAliasId, out var preA)
                    && !string.IsNullOrEmpty(preA.Name))
                {
                    preName = preA.Name;
                }
                if (seriesByPrecure.TryGetValue(pr.PrecureId, out var sps)) repSeries = sps;
            }

            var (bg, fg, bd) = BadgeColors(keyColor);
            items.Add(new
            {
                k = "cb",
                m = (int)cm,
                d = (int)cd,
                cn = c.Name,
                pn = preName,
                cu = url,
                kc = bg,
                kf = fg,
                kb = bd,
                st = repSeries?.Title ?? "",
                ss = repSeries?.Slug ?? "",
                su = repSeries is null ? "" : PathUtil.SeriesUrl(repSeries.Slug),
                sy = repSeries?.StartDate.Year ?? 0
            });
        }

        // ── 人物誕生日（生年は PUBLIC かつ判明時のみ by に載せる）──
        foreach (var pe in persons)
        {
            if (pe.BirthMonth is not byte pm || pe.BirthDay is not byte pd) continue;
            int? by = (string.Equals(pe.BirthYearVisibility, "PUBLIC", StringComparison.Ordinal)
                       && pe.BirthYear.HasValue) ? (int?)pe.BirthYear.Value : null;
            items.Add(new
            {
                k = "pb",
                m = (int)pm,
                d = (int)pd,
                pn = pe.FullName,
                pu = PathUtil.PersonUrl(pe.PersonId),
                by
            });
        }

        var options = new JsonSerializerOptions
        {
            // 日本語文字をそのまま出す（\uXXXX エスケープを避けて容量削減）。
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = false,
            // 非該当フィールド（人物誕生日の by など）が null のときは出力しない。
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(items, options);
    }

    /// <summary>キーカラー（<c>#RRGGBB</c>）から (地色, 文字色, ボーダー色) を求める。 文字色は地色の相対輝度（WCAG 2.x）から暗/明グレーを自動選択。 空文字・不正値のときは 3 値とも空文字（JS 側で中立バッジにフォールバック）。</summary>
    private static (string Bg, string Fg, string Bd) BadgeColors(string keyColor)
    {
        if (string.IsNullOrEmpty(keyColor) || keyColor.Length != 7 || keyColor[0] != '#')
            return ("", "", "");
        int r, g, b;
        try
        {
            r = Convert.ToInt32(keyColor.Substring(1, 2), 16);
            g = Convert.ToInt32(keyColor.Substring(3, 2), 16);
            b = Convert.ToInt32(keyColor.Substring(5, 2), 16);
        }
        catch (FormatException) { return ("", "", ""); }
        static double Lin(int ch)
        {
            double v = ch / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        double lum = 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        bool dark = lum > 0.179;
        return (keyColor,
                dark ? "#1a1a1a" : "#f5f5f5",
                dark ? "rgba(0, 0, 0, 0.22)" : "rgba(255, 255, 255, 0.30)");
    }

    // DTO 変換ヘルパ

    /// <summary>
    /// <see cref="Product"/> をホームの「発売予定」「新着」カードグリッド用 DTO に変換する。
    /// 単純な日付・タイトル列に加え、ジャケット画像・購入導線（Amazon CD/デジタル）・
    /// シリーズ名（複数所属時は「複数シリーズ」表記）・税込価格・「予約受付中」/「発売中」/「発売まで N 日」の
    /// 状態バッジを計算済みの文字列として詰める。SiteBuilder ビルド時点での状態を焼き込むため、
    /// 閲覧時刻と「発売まで N 日」がずれる可能性があるが、ホームは毎日ビルドしている運用なので許容する。
    /// </summary>
    private static ProductRow ToProductRow(
        Product p,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        IReadOnlyDictionary<string, IReadOnlyList<Disc>> discsByProductCatalogNo,
        IReadOnlyDictionary<int, Series> seriesById,
        string amazonTag,
        DateOnly today)
    {
        // 外部プラットフォームへのリンク。各 ID があるときだけ URL を組み立てる。
        // ProductsGenerator の商品詳細ページと同じ規約：Amazon は ?tag= 付与でアフィリエイト計測対象。
        string amazonCdUrl = "";
        if (!string.IsNullOrEmpty(p.AmazonAsinCd))
        {
            amazonCdUrl = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(p.AmazonAsinCd);
            if (amazonTag.Length > 0) amazonCdUrl += "?tag=" + Uri.EscapeDataString(amazonTag);
        }
        string amazonDigitalUrl = "";
        if (!string.IsNullOrEmpty(p.AmazonAsinDigital))
        {
            amazonDigitalUrl = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(p.AmazonAsinDigital);
            if (amazonTag.Length > 0) amazonDigitalUrl += "?tag=" + Uri.EscapeDataString(amazonTag);
        }
        // シリーズ名は商品の所属ディスク群を辿って解決する。
        // 全ディスクが同一シリーズに紐付けば当該シリーズの Title を、複数シリーズに跨れば「複数シリーズ」を、
        // ディスクが 1 枚もシリーズ紐付けを持たなければ空文字を出す。
        // 商品種別と並列で表示する短いラベルとして使うため、ここでは title_short を使わず Title（フル）に
        // 統一する（site-wide policy: title_short は出力に出さない）。
        string seriesLabel = ResolveSeriesLabel(p, discsByProductCatalogNo, seriesById);

        // 税込価格表示（カンマ区切り）。null や 0 のときは空文字でカードに行ごと出さない。
        string priceIncTax = (p.PriceIncTax is int v && v > 0) ? v.ToString("N0") : "";

        // 状態バッジと「発売まで N 日」表示。
        // 未来=「予約受付中」+「発売まで N 日」（DaysUntilLabel）。
        // 過去 0〜7 日=「発売中」（直近で目立たせる）。それ以前は空（バッジを出さない）。
        var releaseDateOnly = DateOnly.FromDateTime(p.ReleaseDate);
        int diffDays = releaseDateOnly.DayNumber - today.DayNumber;
        string releaseStatusLabel = "";
        string daysUntilLabel = "";
        if (diffDays > 0)
        {
            releaseStatusLabel = "予約受付中";
            daysUntilLabel = diffDays == 1 ? "明日発売" : $"発売まであと {diffDays} 日";
        }
        else if (diffDays >= -7)
        {
            releaseStatusLabel = diffDays == 0 ? "本日発売" : "発売中";
        }

        return new ProductRow
        {
            ProductCatalogNo = p.ProductCatalogNo,
            Title = p.Title,
            ReleaseDate = JpDateFormat.Date(p.ReleaseDate),
            ProductKindLabel = productKindMap.TryGetValue(p.ProductKindCode, out var pk) ? pk.NameJa : p.ProductKindCode,
            ProductUrl = PathUtil.ProductUrl(p.ProductCatalogNo),
            CoverImageUrl = p.CoverImageUrl ?? "",
            AmazonCdUrl = amazonCdUrl,
            AmazonDigitalUrl = amazonDigitalUrl,
            SeriesLabel = seriesLabel,
            PriceIncTax = priceIncTax,
            ReleaseStatusLabel = releaseStatusLabel,
            DaysUntilLabel = daysUntilLabel,
        };
    }

    /// <summary>
    /// 商品の所属ディスク群を辿って、カード上に出す 1 行のシリーズ表記を解決する。
    /// 全ディスクが同一シリーズなら当該シリーズの <see cref="Series.Title"/>、複数シリーズに跨るなら
    /// 「複数シリーズ」、ディスクが 1 枚もシリーズ紐付けを持たない（全 NULL）なら空文字を返す。
    /// 論理削除済みディスクは <see cref="DiscsRepository.GetByProductReleaseOrderAsync"/> 側で除外済み。
    /// </summary>
    private static string ResolveSeriesLabel(
        Product p,
        IReadOnlyDictionary<string, IReadOnlyList<Disc>> discsByProductCatalogNo,
        IReadOnlyDictionary<int, Series> seriesById)
    {
        if (!discsByProductCatalogNo.TryGetValue(p.ProductCatalogNo, out var discs) || discs.Count == 0)
            return "";

        var uniqueSeriesIds = discs.Where(d => d.SeriesId.HasValue)
                                   .Select(d => d.SeriesId!.Value)
                                   .Distinct()
                                   .ToList();
        if (uniqueSeriesIds.Count == 0) return "";
        if (uniqueSeriesIds.Count >= 2) return "複数シリーズ";
        return seriesById.TryGetValue(uniqueSeriesIds[0], out var s) ? s.Title : "";
    }

    // テンプレ用 DTO 群

    private sealed class EpisodeWithSeries
    {
        public Episode Episode { get; set; } = null!;
        public Series Series { get; set; } = null!;
    }

    private sealed class HomeContentModel
    {
        public string SiteName { get; set; } = "";
        /// <summary>本番モードかどうか。true のとき DB 統計ボックスのうちプリキュア・キャラクター・
        /// クリエーターをテンプレ側で非表示にする（データが揃いきるまでの暫定措置）。</summary>
        public bool IsProductionMode { get; set; }
        /// <summary>最終ビルド表記の表示文字列（導入）。 「YYYY年M月D日現在 『○○プリキュア』第n話時点の情報を表示しています」のような 完成形を C# 側で組み立てて流し込む。</summary>
        public string BuildLabel { get; set; } = "";
        /// <summary>キャラクター・クリエーターのデータ充足率の表示文字列（暫定表記）。
        /// 空文字なら非表示。BuildLabel の直下に赤字で出す。</summary>
        public string DataSufficiencyLabel { get; set; } = "";
        /// <summary>本放送フォーマットの調査完了率の表示文字列（暫定表記）。
        /// 空文字なら非表示。DataSufficiencyLabel の直下に赤字で出す。</summary>
        public string BroadcastFormatLabel { get; set; } = "";
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

    /// <summary>ホームの「今後の放送予定」「最新エピソード」を /episodes/ と同一の episodes-index-section 構造で描画するための 1 シリーズ分のセクション。</summary>
    private sealed class HomeEpisodeSection
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズ開始年の西暦 4 桁（見出しに薄色括弧で添える）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>セクション（シリーズ）の並べ替え用キー。呼び出し側で並び確定済みの 先頭エピソード放送日の Ticks。最新=降順／今後=昇順でそのまま使う。 テンプレには出さない内部用。</summary>
        public long SortKeyTicks { get; set; }
        public IReadOnlyList<HomeEpisodeRow> Episodes { get; set; } = Array.Empty<HomeEpisodeRow>();
    }

    /// <summary>ホームのエピソードセクション内 1 行。/episodes/ の EpisodesIndexRow と同等の 表示項目（第N話・放送日・サブタイトル・5 役職スタッフ HTML 断片）を持つ。</summary>
    private sealed class HomeEpisodeRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        /// <summary>ルビ付きサブタイトル HTML（DB の <c>episodes.title_rich_html</c> 素通し）。 /episodes/ ランディングと同一仕様で、非空ならテンプレ側でこれを優先表示し、 空なら <see cref="TitleText"/> のエスケープ平文をフォールバックする。</summary>
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

    /// <summary>ホームのカードグリッド用 1 商品ぶんの表示 DTO。 単純な日付・タイトルに加え、ジャケット画像・購入導線（Amazon CD/デジタル）・ シリーズ表記・税込価格・状態バッジ（「予約受付中」「発売中」「本日発売」）と 「発売まで N 日」表示を計算済みの文字列として保持する。 ビルド時点での状態を焼き込む（ホームは毎日ビルド前提）。</summary>
    private sealed class ProductRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string ProductKindLabel { get; set; } = "";
        public string ProductUrl { get; set; } = "";
        /// <summary>ジャケット画像 URL（提供元 CDN ホットリンク。空ならカードでは画像枠を出さずに「No Image」ラベルに置換）。</summary>
        public string CoverImageUrl { get; set; } = "";
        /// <summary>Amazon 商品リンク（CD 物理パッケージ向け。アソシエイトタグ付き。ASIN 未設定なら空）。</summary>
        public string AmazonCdUrl { get; set; } = "";
        /// <summary>Amazon 商品リンク（デジタル音源向け。アソシエイトタグ付き。ASIN 未設定なら空）。</summary>
        public string AmazonDigitalUrl { get; set; } = "";
        /// <summary>シリーズ表記（単一なら <see cref="Series.Title"/>、複数なら「複数シリーズ」、未紐付けなら空）。</summary>
        public string SeriesLabel { get; set; } = "";
        /// <summary>税込価格の表示文字列（カンマ区切り）。未設定なら空。</summary>
        public string PriceIncTax { get; set; } = "";
        /// <summary>状態バッジ表記（「予約受付中」「本日発売」「発売中」、または空）。</summary>
        public string ReleaseStatusLabel { get; set; } = "";
        /// <summary>発売予定の商品にだけ立つ「発売まで N 日」文字列。 発売済み or 発売日同日のときは空文字でカードに行ごと出さない。</summary>
        public string DaysUntilLabel { get; set; } = "";
    }

    /// <summary>
    /// テンプレ側で表示するデータベース統計モデル（11 項目化）。
    /// <list type="bullet">
    ///   <item><description>シリーズは TV / 映画（親作品のみ）/ スピンオフ の 3 種に分離。</description></item>
    ///   <item><description>「歌」は <c>song_recordings</c> 行数（楽曲のレコーディング単位、サイズ・パート違い別カウント）。</description></item>
    ///   <item><description>「劇伴」は <c>bgm_cues</c> 行数（仮 M 番号も含む）。</description></item>
    ///   <item><description>「音楽商品」は <c>products</c>（点数）と <c>discs</c>（枚数）を独立した整数値として保持し、テンプレ側で「数値＋ラベル」ペアを 2 組並べて「N 点 M 枚」を表示する。</description></item>
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
        /// <summary>音楽商品の点数（<c>products</c> 件数）。ホーム DB 統計セルでは「点」ラベルと組で表示する。</summary>
        public int MusicProductsCount { get; set; }
        /// <summary>音楽商品の枚数（<c>discs</c> 件数）。ホーム DB 統計セルでは「枚」ラベルと組で表示する。</summary>
        public int MusicDiscsCount { get; set; }
        public int PersonsCount { get; set; }
        public int CompaniesCount { get; set; }

        /// <summary>「クリエーター」集計値。人物（PersonsCount）と企業・団体（CompaniesCount）を 合算したもの。トップの DB 統計ボックスでは両者を別々に出さず、この合算値で 「クリエーター」1 項目として表示し、リンク先は /creators/ ランディングにする。 個別の人物数・企業数は他用途のため PersonsCount / CompaniesCount として保持する。</summary>
        public int CreatorsCount => PersonsCount + CompaniesCount;
    }

}

/// <summary><c>/about/</c> の生成。</summary>
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
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
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
