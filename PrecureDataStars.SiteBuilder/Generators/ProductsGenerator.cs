using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 商品索引（<c>/products/</c>）と商品詳細（<c>/products/{product_catalog_no}/</c>）の生成（v1.3.0 タスク追加）。
/// <para>
/// 商品（products）→ ディスク（discs）→ トラック（tracks）の 3 階層を 1 ページに集約する。
/// トラック内容種別（SONG / BGM / DRAMA / RADIO / JINGLE / CHAPTER / OTHER）に応じて、
/// 表示する曲名・演者の解決経路を切り替える：
/// </para>
/// <list type="bullet">
///   <item><description>SONG: <c>tracks.song_recording_id</c> → <c>song_recordings</c> + <c>songs</c> から
///     曲名と歌唱者を引く。サイズ・パートのバリアントは <c>SongSizeVariants</c> / <c>SongPartVariants</c> から。</description></item>
///   <item><description>BGM: <c>tracks.bgm_series_id</c> + <c>tracks.bgm_m_no_detail</c> → <c>bgm_cues</c> から
///     M 番号 + メニュー名を引く。仮 M 番号フラグが立っている行は番号を「(番号不明)」に置換。</description></item>
///   <item><description>DRAMA / RADIO / JINGLE / CHAPTER / OTHER: <c>tracks.track_title_override</c> をそのまま表示。</description></item>
/// </list>
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

        var productKindMap = productKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var discKindMap = discKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var trackKindMap = trackContentKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var sizeVariantMap = sizeVariants.ToDictionary(k => k.VariantCode, StringComparer.Ordinal);
        var partVariantMap = partVariants.ToDictionary(k => k.VariantCode, StringComparer.Ordinal);
        var songMap = allSongs.ToDictionary(s => s.SongId);
        var recordingMap = allRecordings.ToDictionary(r => r.SongRecordingId);
        var discsByProduct = allDiscs
            .GroupBy(d => d.ProductCatalogNo)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DiscNoInSet ?? 1u).ToList(), StringComparer.Ordinal);

        // BGM 解決用の (series_id, m_no_detail) → BgmCue マップ。
        // シリーズごとに重複なくロードするため、tracks を眺めて参照されている (series_id) のみ引く。
        var bgmCueMap = new Dictionary<(int seriesId, string mNoDetail), BgmCue>();

        // 索引ページ。
        GenerateIndex(allProducts, productKindMap);

        // 詳細ページ。
        foreach (var p in allProducts)
        {
            await GenerateDetailAsync(
                p, discsByProduct, productKindMap, discKindMap, trackKindMap,
                sizeVariantMap, partVariantMap, songMap, recordingMap, bgmCueMap, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"products: {allProducts.Count + 1} ページ");
    }

    /// <summary><c>/products/</c>（商品索引）。発売日昇順、商品種別でセクション分け。</summary>
    private void GenerateIndex(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap)
    {
        var sections = products
            .GroupBy(p => p.ProductKindCode)
            .Select(g => new
            {
                KindCode = g.Key,
                KindLabel = productKindMap.TryGetValue(g.Key, out var pk) ? pk.NameJa : g.Key,
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
            .Select(s => new ProductKindSection
            {
                KindLabel = s.KindLabel,
                Members = s.Members
            })
            .ToList();

        var content = new ProductsIndexModel
        {
            Sections = sections,
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
        CancellationToken ct)
    {
        var discs = discsByProduct.TryGetValue(product.ProductCatalogNo, out var lst)
            ? lst
            : new List<Disc>();

        // 各ディスクのトラックを引く。BGM 参照があれば bgmCueMap にロード。
        var discViews = new List<DiscView>();
        foreach (var disc in discs)
        {
            var tracks = await _tracksRepo.GetByCatalogNoAsync(disc.CatalogNo, ct).ConfigureAwait(false);

            // このディスクで参照されているシリーズの BgmCue 群を一括ロード（未取得シリーズのみ）。
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

            // トラック行を組み立て。
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
                Manufacturer = product.Manufacturer ?? "",
                Distributor = product.Distributor ?? "",
                Label = product.Label ?? "",
                AmazonAsin = product.AmazonAsin ?? "",
                AppleAlbumId = product.AppleAlbumId ?? "",
                SpotifyAlbumId = product.SpotifyAlbumId ?? "",
                Notes = product.Notes ?? ""
            },
            Discs = discViews
        };
        // 商品詳細の構造化データ。音楽系の商品種別なら MusicAlbum、それ以外は Product として埋め込む。
        // ProductKind コードに "MUSIC" / "CD" / "SOUNDTRACK" 系が含まれているかで簡易判定する。
        string baseUrl = _ctx.Config.BaseUrl;
        string productUrl = PathUtil.ProductUrl(product.ProductCatalogNo);
        bool isMusicAlbum =
            product.ProductKindCode.Contains("MUSIC", StringComparison.OrdinalIgnoreCase) ||
            product.ProductKindCode.Contains("CD", StringComparison.OrdinalIgnoreCase) ||
            product.ProductKindCode.Contains("SOUNDTRACK", StringComparison.OrdinalIgnoreCase);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = isMusicAlbum ? "MusicAlbum" : "Product",
            ["name"] = product.Title,
            ["sku"] = product.ProductCatalogNo,
            ["datePublished"] = product.ReleaseDate.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja"
        };
        if (!string.IsNullOrEmpty(product.TitleEn)) jsonLdDict["alternateName"] = product.TitleEn;
        if (!string.IsNullOrEmpty(product.Manufacturer)) jsonLdDict["manufacturer"] = product.Manufacturer;
        if (!string.IsNullOrEmpty(product.Label) && isMusicAlbum) jsonLdDict["recordLabel"] = product.Label;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + productUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = product.Title,
            MetaDescription = $"{product.Title}（{productKindLabel}）の収録内容。",
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
        string subTitle = ""; // バリアント / 演者 / M 番号などの補助情報。
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
                // DRAMA / RADIO / JINGLE / CHAPTER / OTHER 等。
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

    private sealed class ProductsIndexModel
    {
        public IReadOnlyList<ProductKindSection> Sections { get; set; } = Array.Empty<ProductKindSection>();
        public int TotalCount { get; set; }
    }

    private sealed class ProductKindSection
    {
        public string KindLabel { get; set; } = "";
        public IReadOnlyList<ProductIndexRow> Members { get; set; } = Array.Empty<ProductIndexRow>();
    }

    private sealed class ProductIndexRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public byte DiscCount { get; set; }
    }

    private sealed class ProductDetailModel
    {
        public ProductView Product { get; set; } = new();
        public IReadOnlyList<DiscView> Discs { get; set; } = Array.Empty<DiscView>();
    }

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
        public string Manufacturer { get; set; } = "";
        public string Distributor { get; set; } = "";
        public string Label { get; set; } = "";
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
