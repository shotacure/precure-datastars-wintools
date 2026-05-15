using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 商品索引（<c>/products/</c>）と商品詳細（<c>/products/{product_catalog_no}/</c>）の生成。
/// <para>
/// 商品（products）→ ディスク（discs）→ トラック（tracks）の 3 階層を 1 ページに集約する。
/// </para>
/// <para>
/// 商品のレーベル名・販売元名は完全に
/// <c>product_companies</c> マスタ ID で表現する。フリーテキストのレーベル列は持たず、
/// 本ジェネレータにフリーテキストフォールバック分岐は無い。<see cref="ResolveCompanyName"/> は単純に
/// productCompanyMap から <c>NameJa</c> を引くだけで、未登録 ID（マスタが論理削除された
/// 等の異常系）は空文字を返す。
/// </para>
/// <para>
/// 商品索引のセクション分けは 2 系統。
/// 「ジャンル別（<c>product_kinds.display_order</c> 順）」と
/// 「シリーズ別」セクションを生成し、テンプレ側のタブ UI で切り替えられる
/// （既定はシリーズ別）。シリーズ別の分類規則は <see cref="ClassifyProductIntoSeriesBucket"/>
/// を参照：商品の全ディスクが同一シリーズに紐付くなら当該シリーズ、複数シリーズ混在
/// なら「複数シリーズ」、ディスクに 1 件もシリーズ紐付けがなければ「その他」。
/// </para>
/// </summary>
public sealed class ProductsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecordingsRepo;
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    // 商品社名マスタ。id 紐付けが立っている社名のみ表示・JSON-LD に採用する。
    private readonly ProductCompaniesRepository _productCompaniesRepo;

    /// <summary>
    /// シリーズ別セクションでの「複数シリーズ」バケット名。商品内のディスクで series_id
    /// が複数種類（NULL 混在を含む）に分かれる場合に使う見出し。テンプレ側でも参照される。
    /// </summary>
    private const string MultiSeriesBucketLabel = "複数シリーズ";

    /// <summary>
    /// シリーズ別セクションでの「その他」バケット名。商品の全ディスクが series_id=NULL
    /// （あるいはディスク自体が未登録）の場合に使う見出し。テンプレ側でも参照される。
    /// </summary>
    private const string OtherSeriesBucketLabel = "その他";

    public ProductsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;

        _productsRepo = new ProductsRepository(factory);
        _discsRepo = new DiscsRepository(factory);
        _tracksRepo = new TracksRepository(factory);
        _songsRepo = new SongsRepository(factory);
        _songRecordingsRepo = new SongRecordingsRepository(factory);
        _bgmCuesRepo = new BgmCuesRepository(factory);
        _productKindsRepo = new ProductKindsRepository(factory);
        _discKindsRepo = new DiscKindsRepository(factory);
        _trackContentKindsRepo = new TrackContentKindsRepository(factory);
        _songSizeVariantsRepo = new SongSizeVariantsRepository(factory);
        _songPartVariantsRepo = new SongPartVariantsRepository(factory);
        _productCompaniesRepo = new ProductCompaniesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating products");

        // マスタ全件をメモリにロードしてディクショナリ化。
        var allProducts = (await _productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allDiscs = (await _discsRepo.GetByProductReleaseOrderAsync(ct).ConfigureAwait(false)).ToList();
        var allSongs = (await _songsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allRecordings = (await _songRecordingsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var productKinds = (await _productKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var discKinds = (await _discKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var trackContentKinds = (await _trackContentKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var sizeVariants = (await _songSizeVariantsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var partVariants = (await _songPartVariantsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        // 商品社名マスタを id → 社名 のマップとしてロード（論理削除済みも含める：FK 紐付けが
        // ある商品で表示崩れしないよう、削除済みも引けるようにしておく）。
        var allProductCompanies = (await _productCompaniesRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false)).ToList();

        var productKindMap = productKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var discKindMap = discKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var trackKindMap = trackContentKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var sizeVariantMap = sizeVariants.ToDictionary(k => k.VariantCode, StringComparer.Ordinal);
        var partVariantMap = partVariants.ToDictionary(k => k.VariantCode, StringComparer.Ordinal);
        var songMap = allSongs.ToDictionary(s => s.SongId);
        var recordingMap = allRecordings.ToDictionary(r => r.SongRecordingId);
        var productCompanyMap = allProductCompanies.ToDictionary(pc => pc.ProductCompanyId);
        var discsByProduct = allDiscs
            .GroupBy(d => d.ProductCatalogNo)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DiscNoInSet ?? 1u).ToList(), StringComparer.Ordinal);

        // BGM 解決用の (series_id, m_no_detail) → BgmCue マップ。
        var bgmCueMap = new Dictionary<(int seriesId, string mNoDetail), BgmCue>();

        // 索引ページ。シリーズ別タブも生成するため discsByProduct を渡す。
        GenerateIndex(allProducts, productKindMap, discsByProduct);

        // 詳細ページ。
        foreach (var p in allProducts)
        {
            await GenerateDetailAsync(
                p, discsByProduct, productKindMap, discKindMap, trackKindMap,
                sizeVariantMap, partVariantMap, songMap, recordingMap, bgmCueMap,
                productCompanyMap, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"products: {allProducts.Count + 1} ページ");
    }

    /// <summary>
    /// <c>/products/</c>（商品索引）。
    /// <para>
    /// 索引ページはタブ切替式の 2 系統セクションを内包する：
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>シリーズ別（既定タブ）</b>：商品の全ディスクの <c>series_id</c> を集めて
    ///     「単一シリーズ」「複数シリーズ」「その他」に振り分け。シリーズセクションは
    ///     <c>Series.StartDate</c> 昇順、その後ろに「複数シリーズ」「その他」の順で並ぶ。</description></item>
    ///   <item><description><b>ジャンル別</b>：従来通り <c>product_kinds.display_order</c> 昇順で
    ///     セクション分け。</description></item>
    /// </list>
    /// <para>
    /// 各セクション内は発売日昇順・代表品番昇順（両系統で共通）。
    /// </para>
    /// </summary>
    private void GenerateIndex(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct)
    {
        var kindSections = BuildKindSections(products, productKindMap);
        var seriesSections = BuildSeriesSections(products, discsByProduct);

        var content = new ProductsIndexModel
        {
            KindSections = kindSections,
            SeriesSections = seriesSections,
            TotalCount = products.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "商品一覧",
            MetaDescription = "プリキュアシリーズに関連する商品（CD / Blu-ray / DVD 等）の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "商品", Url = "" }
            }
        };
        _page.RenderAndWrite("/products/", "products", "products-index.sbn", content, layout);
    }

    /// <summary>
    /// ジャンル別セクション（商品種別 = <c>product_kinds</c>）。
    /// <c>display_order</c> 昇順でセクションを並べ、各セクション内は発売日昇順・代表品番昇順。
    /// </summary>
    private static List<ProductIndexSection> BuildKindSections(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap)
    {
        return products
            .GroupBy(p => p.ProductKindCode)
            .Select(g => new
            {
                KindCode = g.Key,
                Label = productKindMap.TryGetValue(g.Key, out var pk) ? pk.NameJa : g.Key,
                Order = productKindMap.TryGetValue(g.Key, out var pk2) ? (pk2.DisplayOrder ?? byte.MaxValue) : byte.MaxValue,
                Members = g.OrderBy(p => p.ReleaseDate)
                           .ThenBy(p => p.ProductCatalogNo, StringComparer.Ordinal)
                           .Select(p => new ProductIndexRow
                           {
                               ProductCatalogNo = p.ProductCatalogNo,
                               Title = p.Title,
                               ReleaseDate = FormatJpDate(p.ReleaseDate),
                               DiscCount = p.DiscCount
                           })
                           .ToList()
            })
            .OrderBy(s => s.Order)
            .ThenBy(s => s.KindCode, StringComparer.Ordinal)
            .Select(s => new ProductIndexSection
            {
                Label = s.Label,
                SeriesLink = "",
                Members = s.Members
            })
            .ToList();
    }

    /// <summary>
    /// シリーズ別セクション。商品ごとに <see cref="ClassifyProductIntoSeriesBucket"/> で
    /// 「単一シリーズ ID」「<see cref="MultiSeriesBucketLabel"/>」「<see cref="OtherSeriesBucketLabel"/>」の
    /// いずれかに振り分け、シリーズセクションは <c>Series.StartDate</c> 昇順、その後ろに
    /// 「複数シリーズ」「その他」の順で並べる。
    /// </summary>
    private List<ProductIndexSection> BuildSeriesSections(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct)
    {
        // バケットキー：シリーズ ID（int）/「複数シリーズ」/「その他」の 3 系統を 1 ディクショナリで扱う。
        // 値は商品行リスト。後段でセクション化する。
        var bucketSeries   = new Dictionary<int, List<ProductIndexRow>>();
        var bucketMulti    = new List<ProductIndexRow>();
        var bucketOther    = new List<ProductIndexRow>();

        foreach (var p in products)
        {
            discsByProduct.TryGetValue(p.ProductCatalogNo, out var discs);
            var row = new ProductIndexRow
            {
                ProductCatalogNo = p.ProductCatalogNo,
                Title = p.Title,
                ReleaseDate = FormatJpDate(p.ReleaseDate),
                DiscCount = p.DiscCount,
                // タイブレーク用に元の DateTime も握っておく。
                ReleaseDateRaw = p.ReleaseDate
            };

            var bucket = ClassifyProductIntoSeriesBucket(discs);
            switch (bucket.Kind)
            {
                case SeriesBucketKind.SingleSeries:
                    if (!bucketSeries.TryGetValue(bucket.SeriesId!.Value, out var listForSeries))
                    {
                        listForSeries = new List<ProductIndexRow>();
                        bucketSeries[bucket.SeriesId.Value] = listForSeries;
                    }
                    listForSeries.Add(row);
                    break;
                case SeriesBucketKind.MultiSeries:
                    bucketMulti.Add(row);
                    break;
                case SeriesBucketKind.Other:
                default:
                    bucketOther.Add(row);
                    break;
            }
        }

        // 各バケット内のソート：発売日昇順・代表品番昇順（kind 別と統一）。
        static List<ProductIndexRow> SortRows(IEnumerable<ProductIndexRow> rows)
            => rows.OrderBy(r => r.ReleaseDateRaw)
                   .ThenBy(r => r.ProductCatalogNo, StringComparer.Ordinal)
                   .ToList();

        var sections = new List<ProductIndexSection>();

        // シリーズセクション：Series.StartDate 昇順。SeriesById に見つからない（マスタ未登録の
        // 異常系）シリーズはバケット末尾扱いで「複数シリーズ」の手前に置く（実運用ではまず無い）。
        var seriesOrdered = bucketSeries
            .Select(kv => new
            {
                SeriesId = kv.Key,
                Series = _ctx.SeriesById.TryGetValue(kv.Key, out var s) ? s : null,
                Members = kv.Value
            })
            .OrderBy(x => x.Series?.StartDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.SeriesId)
            .ToList();

        foreach (var x in seriesOrdered)
        {
            // 後段：略称（series.title_short）は生成・UI ともに一切使わない。
            // セクション見出しは常に正式タイトル（series.title）一本。
            var label = x.Series != null
                ? x.Series.Title
                : $"series#{x.SeriesId}";
            var seriesLink = x.Series != null ? PathUtil.SeriesUrl(x.Series.Slug) : "";
            var yearLabel  = x.Series != null ? x.Series.StartDate.Year.ToString() : "";

            sections.Add(new ProductIndexSection
            {
                Label = label,
                SeriesLink = seriesLink,
                SeriesStartYearLabel = yearLabel,
                Members = SortRows(x.Members)
            });
        }

        // 「複数シリーズ」セクション：該当商品があれば追加。
        if (bucketMulti.Count > 0)
        {
            sections.Add(new ProductIndexSection
            {
                Label = MultiSeriesBucketLabel,
                SeriesLink = "",
                Members = SortRows(bucketMulti)
            });
        }

        // 「その他」セクション：該当商品があれば追加。
        if (bucketOther.Count > 0)
        {
            sections.Add(new ProductIndexSection
            {
                Label = OtherSeriesBucketLabel,
                SeriesLink = "",
                Members = SortRows(bucketOther)
            });
        }

        return sections;
    }

    /// <summary>
    /// 商品のディスク集合から、シリーズ別タブにおける所属バケットを決定する。
    /// <list type="bullet">
    ///   <item><description>discs の <c>series_id</c> が全て同一の非 NULL 値
    ///     → <see cref="SeriesBucketKind.SingleSeries"/>（そのシリーズ ID）</description></item>
    ///   <item><description>discs の <c>series_id</c> が複数種類（NULL 混在を含む）
    ///     → <see cref="SeriesBucketKind.MultiSeries"/>（「複数シリーズ」）</description></item>
    ///   <item><description>discs が 0 件、または全ディスクが <c>series_id</c>=NULL
    ///     → <see cref="SeriesBucketKind.Other"/>（「その他」）</description></item>
    /// </list>
    /// </summary>
    private static SeriesBucket ClassifyProductIntoSeriesBucket(List<Disc>? discs)
    {
        // ディスク未登録 → その他。
        if (discs is null || discs.Count == 0)
            return new SeriesBucket(SeriesBucketKind.Other, null);

        // 全ディスクの series_id 集合（NULL は別カウント）を取る。
        bool hasNull = false;
        var seriesIds = new HashSet<int>();
        foreach (var d in discs)
        {
            if (d.SeriesId is int sid) seriesIds.Add(sid);
            else hasNull = true;
        }

        // 全 NULL → その他。
        if (seriesIds.Count == 0)
            return new SeriesBucket(SeriesBucketKind.Other, null);

        // 単一の非 NULL シリーズで NULL 混在なし → そのシリーズ。
        if (seriesIds.Count == 1 && !hasNull)
            return new SeriesBucket(SeriesBucketKind.SingleSeries, seriesIds.First());

        // それ以外（複数の異なるシリーズ、または一部 NULL 混在）→ 複数シリーズ。
        return new SeriesBucket(SeriesBucketKind.MultiSeries, null);
    }

    /// <summary>商品詳細：基本情報 + ディスク一覧 + 各ディスクの収録トラック。</summary>
    private async Task GenerateDetailAsync(
        Product product,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        IReadOnlyDictionary<string, DiscKind> discKindMap,
        IReadOnlyDictionary<string, TrackContentKind> trackKindMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<int, Song> songMap,
        IReadOnlyDictionary<int, SongRecording> recordingMap,
        Dictionary<(int seriesId, string mNoDetail), BgmCue> bgmCueMap,
        IReadOnlyDictionary<int, ProductCompany> productCompanyMap,
        CancellationToken ct)
    {
        var discs = discsByProduct.TryGetValue(product.ProductCatalogNo, out var lst)
            ? lst
            : new List<Disc>();

        var discViews = new List<DiscView>();
        foreach (var disc in discs)
        {
            var tracks = await _tracksRepo.GetByCatalogNoAsync(disc.CatalogNo, ct).ConfigureAwait(false);

            foreach (var t in tracks.Where(x => x.BgmSeriesId.HasValue))
            {
                int sid = t.BgmSeriesId!.Value;
                bool seriesAlreadyLoaded = bgmCueMap.Keys.Any(k => k.seriesId == sid);
                if (!seriesAlreadyLoaded)
                {
                    var cues = await _bgmCuesRepo.GetBySeriesAsync(sid, ct).ConfigureAwait(false);
                    foreach (var cue in cues) bgmCueMap[(cue.SeriesId, cue.MNoDetail)] = cue;
                }
            }

            var trackRows = tracks
                .OrderBy(t => t.TrackNo)
                .ThenBy(t => t.SubOrder)
                .Select(t => BuildTrackRow(t, trackKindMap, sizeVariantMap, partVariantMap, songMap, recordingMap, bgmCueMap))
                .ToList();

            string discKindLabel = (disc.DiscKindCode != null && discKindMap.TryGetValue(disc.DiscKindCode, out var dk))
                ? dk.NameJa : "";
            string seriesLink = "";
            string seriesTitle = "";
            if (disc.SeriesId is int sId && _ctx.SeriesById.TryGetValue(sId, out var s))
            {
                seriesLink = PathUtil.SeriesUrl(s.Slug);
                seriesTitle = s.Title;
            }

            discViews.Add(new DiscView
            {
                CatalogNo = disc.CatalogNo,
                DiscNoInSet = disc.DiscNoInSet,
                Title = disc.Title ?? "",
                MediaFormat = disc.MediaFormat,
                DiscKindLabel = discKindLabel,
                SeriesLink = seriesLink,
                SeriesTitle = seriesTitle,
                Tracks = trackRows
            });
        }

        string productKindLabel = productKindMap.TryGetValue(product.ProductKindCode, out var pk) ? pk.NameJa : product.ProductKindCode;

        // レーベル・販売元の表示文字列は構造化 ID から解決する一本道。
        // ID が NULL またはマスタ未登録なら空文字。フリーテキスト列はもう存在しない。
        string labelText       = ResolveCompanyName(product.LabelProductCompanyId,       productCompanyMap);
        string distributorText = ResolveCompanyName(product.DistributorProductCompanyId, productCompanyMap);

        var content = new ProductDetailModel
        {
            Product = new ProductView
            {
                ProductCatalogNo = product.ProductCatalogNo,
                Title = product.Title,
                TitleEn = product.TitleEn ?? "",
                ProductKindLabel = productKindLabel,
                ReleaseDate = FormatJpDate(product.ReleaseDate),
                PriceIncTax = product.PriceIncTax?.ToString("N0") ?? "",
                PriceExTax = product.PriceExTax?.ToString("N0") ?? "",
                DiscCount = product.DiscCount,
                LabelText = labelText,
                DistributorText = distributorText,
                AmazonAsin = product.AmazonAsin ?? "",
                AppleAlbumId = product.AppleAlbumId ?? "",
                SpotifyAlbumId = product.SpotifyAlbumId ?? "",
                Notes = product.Notes ?? ""
            },
            Discs = discViews
        };

        // 構造化データ。音楽系の商品種別なら MusicAlbum、それ以外は Product。
        // description / numberOfTracks を追加して、検索結果のリッチスニペット候補要素を増やす。
        string baseUrl = _ctx.Config.BaseUrl;
        string productUrl = PathUtil.ProductUrl(product.ProductCatalogNo);
        bool isMusicAlbum =
            product.ProductKindCode.Contains("MUSIC", StringComparison.OrdinalIgnoreCase) ||
            product.ProductKindCode.Contains("CD", StringComparison.OrdinalIgnoreCase) ||
            product.ProductKindCode.Contains("SOUNDTRACK", StringComparison.OrdinalIgnoreCase);

        // MetaDescription を実データから動的構築する。
        // 「『{タイトル}』({YYYY年M月D日}発売、{ProductKindLabel})。{N枚組、}{発売元:Label、}収録{N}曲。」を骨格に、
        // 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。
        int totalTracks = discViews.Sum(d => d.Tracks?.Count ?? 0);
        var metaDescription = BuildProductMetaDescription(
            product: product,
            productKindLabel: productKindLabel,
            labelText: labelText,
            totalTracks: totalTracks);

        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = isMusicAlbum ? "MusicAlbum" : "Product",
            ["name"] = product.Title,
            ["description"] = metaDescription,
            ["sku"] = product.ProductCatalogNo,
            ["datePublished"] = product.ReleaseDate.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja"
        };
        if (!string.IsNullOrEmpty(product.TitleEn)) jsonLdDict["alternateName"] = product.TitleEn;
        // 音楽商品の場合はトラック数を MusicAlbum.numberOfTracks に乗せる。
        // Product 型では本プロパティが定義されていないため、isMusicAlbum=false のときは出さない。
        if (isMusicAlbum && totalTracks > 0)
        {
            jsonLdDict["numberOfTracks"] = totalTracks;
        }
        // recordLabel は構造化 ID から Organization オブジェクトを組み立てる。
        // 未紐付け or マスタ未登録なら recordLabel 自体を出力しない。
        if (isMusicAlbum && product.LabelProductCompanyId is int labelId
            && productCompanyMap.TryGetValue(labelId, out var labelPc) && !labelPc.IsDeleted)
        {
            var org = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = labelPc.NameJa
            };
            if (!string.IsNullOrEmpty(labelPc.NameEn)) org["alternateName"] = labelPc.NameEn;
            jsonLdDict["recordLabel"] = org;
        }
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + productUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = product.Title,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "商品", Url = "/products/" },
                new BreadcrumbItem { Label = product.Title, Url = "" }
            },
            OgType = "website",
            JsonLd = jsonLd
        };
        _page.RenderAndWrite(productUrl, "products", "products-detail.sbn", content, layout);
    }

    /// <summary>
    /// 商品詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// <para>
    /// 構成：「『{商品タイトル}』({YYYY年M月D日}発売、{ProductKindLabel})。{N枚組、}{発売元:Label、}収録{N}曲。」を骨格に、
    /// 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。トラック数 0（未登録の状態）の場合は
    /// 「収録N曲」セグメントを省略する。
    /// </para>
    /// </summary>
    private static string BuildProductMetaDescription(
        Product product,
        string productKindLabel,
        string labelText,
        int totalTracks)
    {
        const int targetMaxChars = 140;
        var sb = new System.Text.StringBuilder();

        // ① 『タイトル』(YYYY年M月D日発売、{ProductKindLabel})
        sb.Append('『').Append(product.Title).Append("』(")
          .Append(product.ReleaseDate.ToString("yyyy年M月d日")).Append("発売");
        if (!string.IsNullOrWhiteSpace(productKindLabel))
        {
            sb.Append('、').Append(productKindLabel);
        }
        sb.Append(")。");

        // ② N枚組（複数枚組のときのみ、DiscCount は非 nullable で既定値 1）
        if (product.DiscCount > 1)
        {
            var fragment = $"{product.DiscCount}枚組。";
            if (sb.Length + fragment.Length <= targetMaxChars) sb.Append(fragment);
        }

        // ③ 発売元（あれば）
        if (!string.IsNullOrWhiteSpace(labelText))
        {
            var fragment = $"発売元:{labelText}。";
            if (sb.Length + fragment.Length <= targetMaxChars) sb.Append(fragment);
        }

        // ④ 収録曲数（音楽系商品はトラック数を、それ以外でも >0 なら出す）
        if (totalTracks > 0)
        {
            var fragment = $"収録{totalTracks}曲。";
            if (sb.Length + fragment.Length <= targetMaxChars) sb.Append(fragment);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 商品社名 ID から社名（和名）を引く（構造化 ID で解決する）。
    /// ID が NULL、マスタ未登録、論理削除済みのいずれかの場合は空文字を返す
    /// （フリーテキストフォールバックは存在しない）。
    /// </summary>
    private static string ResolveCompanyName(
        int? productCompanyId,
        IReadOnlyDictionary<int, ProductCompany> productCompanyMap)
    {
        if (productCompanyId is int id
            && productCompanyMap.TryGetValue(id, out var pc)
            && !pc.IsDeleted
            && !string.IsNullOrEmpty(pc.NameJa))
        {
            return pc.NameJa;
        }
        return "";
    }

    /// <summary>1 トラックを表示用 DTO に変換。</summary>
    private TrackRow BuildTrackRow(
        Track t,
        IReadOnlyDictionary<string, TrackContentKind> trackKindMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<int, Song> songMap,
        IReadOnlyDictionary<int, SongRecording> recordingMap,
        IReadOnlyDictionary<(int seriesId, string mNoDetail), BgmCue> bgmCueMap)
    {
        string contentKindLabel = trackKindMap.TryGetValue(t.ContentKindCode, out var ck) ? ck.NameJa : t.ContentKindCode;
        string title = "";
        string subTitle = "";
        string songLink = "";
        int? songId = null;

        switch (t.ContentKindCode)
        {
            case "SONG":
                if (t.SongRecordingId is int rid && recordingMap.TryGetValue(rid, out var rec)
                    && songMap.TryGetValue(rec.SongId, out var song))
                {
                    title = !string.IsNullOrEmpty(t.TrackTitleOverride) ? t.TrackTitleOverride! : song.Title;
                    songId = song.SongId;
                    songLink = PathUtil.SongUrl(song.SongId);
                    var subParts = new List<string>();
                    if (!string.IsNullOrEmpty(rec.SingerName)) subParts.Add(rec.SingerName!);
                    if (!string.IsNullOrEmpty(t.SongSizeVariantCode)
                        && sizeVariantMap.TryGetValue(t.SongSizeVariantCode!, out var sv))
                        subParts.Add(sv.NameJa);
                    if (!string.IsNullOrEmpty(t.SongPartVariantCode)
                        && partVariantMap.TryGetValue(t.SongPartVariantCode!, out var pv))
                        subParts.Add(pv.NameJa);
                    if (!string.IsNullOrEmpty(rec.VariantLabel)) subParts.Add(rec.VariantLabel!);
                    subTitle = string.Join(" / ", subParts);
                }
                else
                {
                    title = t.TrackTitleOverride ?? "(歌情報未登録)";
                }
                break;

            case "BGM":
                if (t.BgmSeriesId is int bsid && t.BgmMNoDetail is string mnd
                    && bgmCueMap.TryGetValue((bsid, mnd), out var cue))
                {
                    string mNoLabel = cue.IsTempMNo ? "(番号不明)" : cue.MNoDetail;
                    title = !string.IsNullOrEmpty(t.TrackTitleOverride)
                        ? t.TrackTitleOverride!
                        : (cue.MenuTitle ?? "(タイトル未登録)");
                    var subParts = new List<string> { mNoLabel };
                    if (!string.IsNullOrEmpty(cue.ComposerName)) subParts.Add($"作曲: {cue.ComposerName}");
                    subTitle = string.Join(" / ", subParts);
                }
                else
                {
                    title = t.TrackTitleOverride ?? "(劇伴情報未登録)";
                }
                break;

            default:
                title = t.TrackTitleOverride ?? "";
                break;
        }

        return new TrackRow
        {
            TrackNo = t.TrackNo,
            SubOrder = t.SubOrder,
            ContentKindLabel = contentKindLabel,
            Title = title,
            SubTitle = subTitle,
            LengthLabel = FormatLength(t.LengthFrames),
            SongLink = songLink
        };
    }

    /// <summary>frames（1/75 秒単位）を「m:ss」表記に整形。NULL は空文字。</summary>
    private static string FormatLength(uint? frames)
    {
        if (!frames.HasValue) return "";
        uint totalSeconds = frames.Value / 75;
        uint min = totalSeconds / 60;
        uint sec = totalSeconds % 60;
        return $"{min}:{sec:00}";
    }

    private static string FormatJpDate(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    // ─── テンプレ用 DTO 群 ───

    /// <summary>
    /// 商品索引テンプレに渡すルートモデル。
    /// 「シリーズ別」「ジャンル別」の 2 系統セクションを併存させる構造に変更。
    /// テンプレ側はタブ UI でこの 2 系統を切り替えて表示する（既定タブは <c>SeriesSections</c>）。
    /// </summary>
    private sealed class ProductsIndexModel
    {
        /// <summary>ジャンル別（<c>product_kinds.display_order</c> 順）セクション。</summary>
        public IReadOnlyList<ProductIndexSection> KindSections { get; set; } = Array.Empty<ProductIndexSection>();
        /// <summary>シリーズ別（<c>Series.StartDate</c> 順 + 「複数シリーズ」「その他」）セクション。</summary>
        public IReadOnlyList<ProductIndexSection> SeriesSections { get; set; } = Array.Empty<ProductIndexSection>();
        /// <summary>商品の総件数（タブ問わず同一）。</summary>
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// 商品索引の 1 セクション。ジャンル別とシリーズ別の両系統で共用する汎用 DTO。
    /// シリーズ別セクションのときは <see cref="SeriesLink"/> にシリーズ詳細ページ URL を入れる
    /// （「複数シリーズ」「その他」「ジャンル別」のセクションでは空文字）。
    /// 後段：シリーズ別セクションでは <see cref="SeriesStartYearLabel"/> に
    /// 「2004」のような西暦 4 桁文字列を入れ、見出し内に薄色括弧で添える（「複数シリーズ」
    /// 「その他」「ジャンル別」のセクションは空文字）。
    /// </summary>
    private sealed class ProductIndexSection
    {
        /// <summary>セクション見出しテキスト（シリーズ名・ジャンル名・「複数シリーズ」「その他」など）。略記は使わず常に正式タイトルを入れる。</summary>
        public string Label { get; set; } = "";
        /// <summary>セクション見出しに張るリンク URL（シリーズ詳細ページ）。リンク不要なら空文字。</summary>
        public string SeriesLink { get; set; } = "";
        /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。シリーズ非紐付けセクションでは空文字。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>所属商品の行リスト（発売日昇順・代表品番昇順）。</summary>
        public IReadOnlyList<ProductIndexRow> Members { get; set; } = Array.Empty<ProductIndexRow>();
    }

    /// <summary>
    /// 商品索引の 1 行。タブ問わず共通の表示 DTO。
    /// <see cref="ReleaseDateRaw"/> は内部ソート専用（テンプレからは <see cref="ReleaseDate"/> を参照）。
    /// </summary>
    private sealed class ProductIndexRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public byte DiscCount { get; set; }
        /// <summary>並べ替え専用の発売日（DateTime 原値）。テンプレからは参照しない。</summary>
        public DateTime ReleaseDateRaw { get; set; }
    }

    /// <summary>
    /// シリーズ別タブで商品が割り振られるバケットの種類。
    /// </summary>
    private enum SeriesBucketKind
    {
        /// <summary>商品の全ディスクが同一の非 NULL <c>series_id</c> に紐付く。</summary>
        SingleSeries,
        /// <summary>商品のディスクで <c>series_id</c> が複数種類（NULL 混在を含む）に分かれる。</summary>
        MultiSeries,
        /// <summary>商品のディスクに 1 件も <c>series_id</c> 紐付けが無い、またはディスクが 0 件。</summary>
        Other
    }

    /// <summary>
    /// シリーズ別タブでのバケット分類結果。<see cref="Kind"/> が
    /// <see cref="SeriesBucketKind.SingleSeries"/> の時のみ <see cref="SeriesId"/> が非 NULL。
    /// </summary>
    private readonly record struct SeriesBucket(SeriesBucketKind Kind, int? SeriesId);

    private sealed class ProductDetailModel
    {
        public ProductView Product { get; set; } = new();
        public IReadOnlyList<DiscView> Discs { get; set; } = Array.Empty<DiscView>();
    }

    /// <summary>
    /// 商品詳細テンプレ用の表示 DTO。
    /// レーベル・販売元は構造化 ID から解決した文字列のみを保持する
    /// （社名は構造化 ID で解決する）。
    /// </summary>
    private sealed class ProductView
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleEn { get; set; } = "";
        public string ProductKindLabel { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string PriceIncTax { get; set; } = "";
        public string PriceExTax { get; set; } = "";
        public byte DiscCount { get; set; }
        /// <summary>レーベル（社名マスタ和名）。未紐付け時は空文字。</summary>
        public string LabelText { get; set; } = "";
        /// <summary>販売元（社名マスタ和名）。未紐付け時は空文字。</summary>
        public string DistributorText { get; set; } = "";
        public string AmazonAsin { get; set; } = "";
        public string AppleAlbumId { get; set; } = "";
        public string SpotifyAlbumId { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class DiscView
    {
        public string CatalogNo { get; set; } = "";
        public uint? DiscNoInSet { get; set; }
        public string Title { get; set; } = "";
        public string MediaFormat { get; set; } = "";
        public string DiscKindLabel { get; set; } = "";
        public string SeriesLink { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public IReadOnlyList<TrackRow> Tracks { get; set; } = Array.Empty<TrackRow>();
    }

    private sealed class TrackRow
    {
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        public string ContentKindLabel { get; set; } = "";
        public string Title { get; set; } = "";
        public string SubTitle { get; set; } = "";
        public string LengthLabel { get; set; } = "";
        /// <summary>SONG のときの楽曲詳細ページへのリンク（楽曲詳細生成時のみ有効、それ以外は空）。</summary>
        public string SongLink { get; set; } = "";
    }
}
