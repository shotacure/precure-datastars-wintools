using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 商品索引（<c>/products/</c>）と商品詳細（<c>/products/{product_catalog_no}/</c>）の生成。
/// 商品（products）→ ディスク（discs）→ トラック（tracks）の 3 階層を 1 ページに集約する。
/// 商品のレーベル名・販売元名は完全に
/// <c>product_companies</c> マスタ ID で表現する。フリーテキストのレーベル列は持たず、
/// 本ジェネレータにフリーテキストフォールバック分岐は無い。<see cref="ResolveCompanyName"/> は単純に
/// productCompanyMap から <c>NameJa</c> を引くだけで、未登録 ID（マスタが論理削除された
/// 等の異常系）は空文字を返す。
/// 商品索引のセクション分けは 2 系統。
/// 「ジャンル別（<c>product_kinds.display_order</c> 順）」と
/// 「シリーズ別」セクションを生成し、テンプレ側のタブ UI で切り替えられる
/// （既定はシリーズ別）。シリーズ別の分類規則は <see cref="ClassifyProductIntoSeriesBucket"/>
/// を参照：商品の全ディスクが同一シリーズに紐付くなら当該シリーズ、複数シリーズ混在
/// なら「複数シリーズ」、ディスクに 1 件もシリーズ紐付けがなければ「その他」。
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
    // SONG 録音が劇伴としても扱われる「両性」の N:M 紐付けを保持する中間テーブル。
    // 商品詳細トラックカードの SONG 分岐で、当該録音が劇伴 cue にも紐付いていれば
    // 歌のクレジット行下に「シリーズ略記 + Mナンバー [メニュー]」行を追加し、
    // 円バッジ色を SONG 赤 + BGM 緑の斜め分割塗りに切り替える。
    // 劇伴詳細 /bgms/{slug}/ の収録盤リストも、この中間テーブル経由で SONG トラックを拾う
    // （SQL 側の UNION ALL で実現、本リポジトリは商品詳細側のみで使用）。
    private readonly SongRecordingBgmAssignmentsRepository _songRecordingBgmAssignmentsRepo;
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    // 商品社名マスタ。id 紐付けが立っている社名のみ表示・JSON-LD に採用する。
    private readonly ProductCompaniesRepository _productCompaniesRepo;
    // 構造化クレジット系：商品詳細のトラック行で「歌の作詞・作曲・編曲・歌バッジ + 名義リンク」
    // 「劇伴の作曲・編曲バッジ + 名義リンク」を組み立てるために必要。
    // 楽曲詳細（SongsGenerator）と同じソースを引いて、商品詳細でも同型の表示にする。
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;
    private readonly BgmCueCreditsRepository _bgmCueCreditsRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // 名義 → 人物 ID 解決のための中間テーブル（person_alias_persons）。alias_id → person_id の
    // 単純な lookup マップを GenerateAsync 冒頭で作る用。
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;

    // 共通クレジット HTML 組立ヘルパー（GenerateAsync 冒頭でマスタを引いた直後に初期化される）。
    // null 許容なのは「初期化前にトラック行生成が走らない」設計だが、念のためアクセス時 ! を付ける。
    private TrackCreditHtmlBuilder? _creditHtml;

    // 単一シリーズに紐付かない商品（=ディスクで複数シリーズに分かれる／全 NULL／ディスク未登録）は
    // 商品種別 product_kinds.display_order 順のサブセクションへ再分配する（BuildSeriesSections 参照）。

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
        _songRecordingBgmAssignmentsRepo = new SongRecordingBgmAssignmentsRepository(factory);
        _productKindsRepo = new ProductKindsRepository(factory);
        _discKindsRepo = new DiscKindsRepository(factory);
        _trackContentKindsRepo = new TrackContentKindsRepository(factory);
        _songSizeVariantsRepo = new SongSizeVariantsRepository(factory);
        _songPartVariantsRepo = new SongPartVariantsRepository(factory);
        _productCompaniesRepo = new ProductCompaniesRepository(factory);
        _songCreditsRepo = new SongCreditsRepository(factory);
        _songRecordingSingersRepo = new SongRecordingSingersRepository(factory);
        _bgmCueCreditsRepo = new BgmCueCreditsRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
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
        // 構造化クレジット解決用：役職マスタと名義マスタ（人物・キャラクター）を全件ロードして
        // ID 直引きで使えるようにする。商品詳細のトラック行で「歌の作詞・作曲・編曲・歌バッジ + 名義リンク」
        // 「劇伴の作曲・編曲バッジ + 名義リンク」を組み立てる際の lookup として使う。
        var allRoles = (await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(false, ct).ConfigureAwait(false)).ToList();
        // 名義 → 人物 ID lookup。person_alias_persons 中間テーブルを全件取って、共同名義
        // （1 alias に複数 person）の場合は person_seq 最小値を採用する単純マップに圧縮する。
        // 通常は 1 alias = 1 人物のため共同名義は稀。
        var allPersonAliasPersons = (await _personAliasPersonsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var personIdByAliasId = allPersonAliasPersons
            .GroupBy(x => x.AliasId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.PersonSeq).First().PersonId);

        var productKindMap = productKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var discKindMap = discKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var trackKindMap = trackContentKinds.ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var sizeVariantMap = sizeVariants.ToDictionary(k => k.VariantCode, StringComparer.Ordinal);
        var partVariantMap = partVariants.ToDictionary(k => k.VariantCode, StringComparer.Ordinal);
        var songMap = allSongs.ToDictionary(s => s.SongId);
        var recordingMap = allRecordings.ToDictionary(r => r.SongRecordingId);
        var productCompanyMap = allProductCompanies.ToDictionary(pc => pc.ProductCompanyId);
        var roleMap = allRoles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        // PersonAlias / CharacterAlias の PK は AliasId。
        var personAliasMap = allPersonAliases.ToDictionary(a => a.AliasId);
        var characterAliasMap = allCharacterAliases.ToDictionary(a => a.AliasId);

        // 共通クレジット HTML 組立ヘルパーを初期化。歌・録音・劇伴のクレジット行は SiteDataLoader が
        // 事前展開した BuildContext の辞書を直接受け取り、本クラス／ヘルパーは生成中に DB を叩かない。
        _creditHtml = new TrackCreditHtmlBuilder(
            personAliasMap, characterAliasMap, personIdByAliasId, roleMap,
            _ctx.SongCreditsBySong, _ctx.SingersByRecording, _ctx.BgmCueCreditsByCue);

        var discsByProduct = allDiscs
            .GroupBy(d => d.ProductCatalogNo)
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DiscNoInSet ?? 1u).ToList(), StringComparer.Ordinal);

        // BGM 解決用の (series_id, m_no_detail) → BgmCue マップ。
        var bgmCueMap = new Dictionary<(int seriesId, string mNoDetail), BgmCue>();

        // SONG 録音が劇伴としても扱われる紐付け（song_recording_bgm_assignments）。
        // 中間テーブルを一括ロードして song_recording_id → 紐付く全行のリスト に変換しておく。
        // 商品詳細トラック生成時に各 SONG トラックの recording_id から逆引きし、tracks 側 part_code と
        // 突き合わせて適用行をフィルタする（NULL の中間テーブル行は「パート問わず適用される既定行」
        // としてどのトラックにも当たる）。
        // テーブル全体の行数は両性扱いされる録音数 × パート × cue 数で実用上は数十〜数百行程度を想定。
        var allAssignments = (await _songRecordingBgmAssignmentsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var bgmAssignmentsByRecordingId = allAssignments
            .GroupBy(a => a.SongRecordingId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<(string PartCode, int SeriesId, string MNoDetail)>)g
                    .Select(a => (a.SongPartVariantCode, a.BgmSeriesId, a.BgmMNoDetail))
                    .ToList());

        // 索引ページ。シリーズ別タブも生成するため discsByProduct を渡す。
        GenerateIndex(allProducts, productKindMap, discsByProduct);

        // 詳細ページ。
        foreach (var p in allProducts)
        {
            GenerateDetail(
                p, discsByProduct, productKindMap, discKindMap, trackKindMap,
                sizeVariantMap, partVariantMap, songMap, recordingMap, bgmCueMap,
                bgmAssignmentsByRecordingId,
                productCompanyMap);
        }

        _ctx.Logger.Success($"products: {allProducts.Count + 1} ページ");
    }

    /// <summary>/products/（商品索引）。</summary>
    private void GenerateIndex(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct)
    {
        var kindSections = BuildKindSections(products, productKindMap, discsByProduct);
        var seriesSections = BuildSeriesSections(products, discsByProduct, productKindMap);

        var content = new ProductsIndexModel
        {
            KindSections = kindSections,
            SeriesSections = seriesSections,
            TotalCount = products.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュア音楽商品(CD/配信)",
            MetaDescription = "歴代すべてのプリキュア音楽商品(CD/配信)。ほしいものは見つかりましたか？",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽商品(CD/配信)", Url = "" }
            }
        };
        _page.RenderAndWrite("/products/", "products", "products-index.sbn", content, layout);
    }

    /// <summary>ジャンル別セクション（商品種別 = <c>product_kinds</c>）。 <c>display_order</c> 昇順でセクションを並べ、各セクション内は発売日昇順・代表品番昇順。</summary>
    private static List<ProductIndexSection> BuildKindSections(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, ProductKind> productKindMap,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct)
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
                           .Select(p => BuildProductIndexRow(p, discsByProduct, productKindMap))
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
    /// 商品索引の 1 行（カード）を組み立てる共通ヘルパー。シリーズ別／ジャンル別の両セクションから使う。
    /// 楽曲索引と同じ「横長カード 1 行 = 1 商品」の意匠を Generator 側で完全に組み立てる：
    /// <list type="bullet">
    ///   <item><c>ReleaseDateShort</c>：発売日を「2004.2.1」形式（年.月.日、月日はゼロパディングしない）に整形。</item>
    ///   <item><c>CatalogNoRange</c>：1 枚商品なら <c>products.product_catalog_no</c> をそのまま、
    ///     複数枚商品なら所属ディスクの <c>catalog_no</c> 群から <c>disc_no_in_set</c> 昇順で
    ///     筆頭・最終を取り、両者の共通 prefix を残してサフィックス差分を「〜N」で連結する
    ///     （例：MJCD-20019 と MJCD-20021 → "MJCD-20019〜21"）。共通 prefix を取れない or
    ///     ディスク未登録の場合は単一品番のままにフォールバック。</item>
    ///   <item><c>PriceIncTaxLabel</c>：「税込 ¥3,300」形式（カンマ区切り、円記号は ASCII で表現、
    ///     「税込」プレフィックスで税抜価格との混同を防ぐ）。
    ///     <c>PriceIncTax</c> が null なら空文字。</item>
    ///   <item><c>DiscCountLabel</c>：複数枚商品（<c>DiscCount &gt; 1</c>）のみ「{N}枚組」。1 枚なら空文字。</item>
    ///   <item><c>ProductKindLabel</c> / <c>BadgeClassSuffix</c>：商品種別マスタを引いた表示ラベルと、
    ///     CSS クラス末尾（<c>tolowerinvariant + _→-</c>）。CSS は固定マッピングで色を当てる。
    ///     マスタ未登録の場合はラベルにコード生値、クラス末尾は空文字。</item>
    /// </list>
    /// </summary>
    private static ProductIndexRow BuildProductIndexRow(
        Product p,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct,
        IReadOnlyDictionary<string, ProductKind> productKindMap)
    {
        // 短縮発売日「2004.2.1」（ゼロパディングなし）。
        string releaseShort = $"{p.ReleaseDate.Year}.{p.ReleaseDate.Month}.{p.ReleaseDate.Day}";

        // 品番レンジ。複数枚は所属ディスクの DiscNoInSet 昇順で筆頭・最終を解決する。
        string catalogRange;
        if (p.DiscCount > 1
            && discsByProduct.TryGetValue(p.ProductCatalogNo, out var discs)
            && discs != null && discs.Count > 0)
        {
            var ordered = discs
                .OrderBy(d => d.DiscNoInSet ?? uint.MaxValue)
                .ThenBy(d => d.CatalogNo, StringComparer.Ordinal)
                .ToList();
            string first = ordered[0].CatalogNo;
            string last  = ordered[ordered.Count - 1].CatalogNo;
            catalogRange = BuildCatalogRangeLabel(first, last);
        }
        else
        {
            catalogRange = p.ProductCatalogNo;
        }

        // 税込価格ラベル。null なら空文字（テンプレ側で出ない）。
        // 「税込」プレフィックスを付けて、税抜価格と一目で区別できるようにする
        // （商品によって税抜のみ／税込のみ／両方を持つケースが混在するため、表示時に明示）。
        string priceLabel = p.PriceIncTax.HasValue
            ? $"税込 ¥{p.PriceIncTax.Value:N0}"
            : "";

        // 「n枚組」ラベル：1 枚商品では出さない（カードを綺麗に保つため）。
        string discCountLabel = p.DiscCount > 1 ? $"{p.DiscCount}枚組" : "";

        // 種別ラベルと CSS クラス末尾。
        string kindLabel = productKindMap.TryGetValue(p.ProductKindCode, out var pk)
            ? pk.NameJa : p.ProductKindCode;
        string badgeSuffix = string.IsNullOrEmpty(p.ProductKindCode)
            ? ""
            : p.ProductKindCode.ToLowerInvariant().Replace('_', '-');

        return new ProductIndexRow
        {
            ProductCatalogNo = p.ProductCatalogNo,
            Title = p.Title,
            // 後方互換のため日本語フォーマットも残す（カードでは ReleaseDateShort を使う）。
            ReleaseDate = JpDateFormat.Date(p.ReleaseDate),
            ReleaseDateShort = releaseShort,
            ReleaseDateRaw = p.ReleaseDate,
            DiscCount = p.DiscCount,
            CatalogNoRange = catalogRange,
            PriceIncTaxLabel = priceLabel,
            DiscCountLabel = discCountLabel,
            ProductKindLabel = kindLabel,
            BadgeClassSuffix = badgeSuffix,
            CoverImageUrl = p.CoverImageUrl ?? ""
        };
    }

    /// <summary>
    /// 複数枚商品の品番レンジ表記を組み立てる。
    /// 筆頭・最終の品番から「共通 prefix の最長一致部分」を取り、後ろの差分だけを「〜」で連結する。
    /// 共通 prefix が取れない or first==last の場合は first をそのまま返す
    /// （例：first=MJCD-20019、last=MJCD-20021 → "MJCD-20019〜21"）。
    /// 末尾差分が空文字になる（first と last が同一）ケースは first をそのまま返す。
    /// </summary>
    private static string BuildCatalogRangeLabel(string first, string last)
    {
        if (string.IsNullOrEmpty(first)) return last ?? "";
        if (string.IsNullOrEmpty(last)) return first;
        if (string.Equals(first, last, StringComparison.Ordinal)) return first;

        // 共通 prefix の最長一致を求める。
        int common = 0;
        int max = Math.Min(first.Length, last.Length);
        while (common < max && first[common] == last[common]) common++;

        // 共通 prefix が無い、または末尾差分が空ならフォールバック。
        if (common == 0) return $"{first}〜{last}";
        string suffix = last.Substring(common);
        if (string.IsNullOrEmpty(suffix)) return first;

        return $"{first}〜{suffix}";
    }

    /// <summary>
    /// シリーズ別セクション。
    /// <para>仕様：</para>
    /// <list type="bullet">
    ///   <item>商品の全ディスクが同一の非 NULL シリーズに紐付くなら、そのシリーズのセクションへ入れる
    ///     （シリーズ並び順は <c>Series.StartDate</c> 昇順）。</item>
    ///   <item>「単一のシリーズに紐付かない」商品（=ディスクで複数シリーズに分かれる／ディスクで
    ///     1 件もシリーズ紐付けが無い／ディスクが未登録）はシリーズセクション末尾に「商品種別」別の
    ///     サブセクションとして並べる。並び順は <c>product_kinds.display_order</c> 昇順、
    ///     見出しは <c>product_kinds.name_ja</c>。</item>
    /// </list>
    /// 「単一シリーズに紐付かない」群は「複数シリーズ」「その他」を一括にせず、商品種別別の
    /// サブセクションへ再分配する（バリエーション商品が「その他」に雑に押し込まれることを避けるため）。
    /// </summary>
    private List<ProductIndexSection> BuildSeriesSections(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<string, List<Disc>> discsByProduct,
        IReadOnlyDictionary<string, ProductKind> productKindMap)
    {
        // バケットキー：シリーズ ID（int）。「単一シリーズ非紐付け」群は別途、商品種別コード単位で集めて
        // 後段で展開する（一体型「その他」バケットは設けない）。
        var bucketSeries        = new Dictionary<int, List<ProductIndexRow>>();
        var bucketNonSingleByKind = new Dictionary<string, List<(Product Product, ProductIndexRow Row)>>();

        foreach (var p in products)
        {
            discsByProduct.TryGetValue(p.ProductCatalogNo, out var discs);
            var row = BuildProductIndexRow(p, discsByProduct, productKindMap);

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
                case SeriesBucketKind.Other:
                default:
                    // 非単一シリーズ群は商品種別ごとに振り分け。
                    // ProductKindCode は商品の必須属性、マスタ未登録の異常系は空キーとして集約しておく。
                    string kindKey = p.ProductKindCode ?? "";
                    if (!bucketNonSingleByKind.TryGetValue(kindKey, out var listForKind))
                    {
                        listForKind = new List<(Product, ProductIndexRow)>();
                        bucketNonSingleByKind[kindKey] = listForKind;
                    }
                    listForKind.Add((p, row));
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
        // 異常系）シリーズはバケット末尾扱いで非単一シリーズ群の手前に置く（実運用ではまず無い）。
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

        // 非単一シリーズ群：商品種別ごとにサブセクション化し、product_kinds.display_order 昇順で並べる。
        // 見出しは「{NameJa}」（リンクなし）。シリーズ年情報は持たないので空文字。
        // マスタ未登録の種別コードや空文字キーは display_order 不明として末尾扱い、見出しに raw コードを出す。
        var kindOrderedNonSingle = bucketNonSingleByKind
            .Select(kv =>
            {
                bool found = !string.IsNullOrEmpty(kv.Key) && productKindMap.TryGetValue(kv.Key, out var pk);
                ProductKind? matchedKind = null;
                if (found) productKindMap.TryGetValue(kv.Key, out matchedKind);
                return new
                {
                    KindCode = kv.Key,
                    KindMaster = matchedKind,
                    Members = kv.Value
                };
            })
            .OrderBy(x => x.KindMaster?.DisplayOrder ?? int.MaxValue)
            .ThenBy(x => x.KindCode, StringComparer.Ordinal)
            .ToList();

        foreach (var x in kindOrderedNonSingle)
        {
            string label = x.KindMaster != null
                ? x.KindMaster.NameJa
                : (string.IsNullOrEmpty(x.KindCode) ? "(種別未設定)" : $"product_kind#{x.KindCode}");
            sections.Add(new ProductIndexSection
            {
                Label = label,
                SeriesLink = "",
                SeriesStartYearLabel = "",
                Members = SortRows(x.Members.Select(t => t.Row))
            });
        }

        return sections;
    }

    /// <summary>商品のディスク集合から、シリーズ別タブにおける所属バケットを決定する。</summary>
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
    private void GenerateDetail(
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
        IReadOnlyDictionary<int, IReadOnlyList<(string PartCode, int SeriesId, string MNoDetail)>> bgmAssignmentsByRecordingId,
        IReadOnlyDictionary<int, ProductCompany> productCompanyMap)
    {
        var discs = discsByProduct.TryGetValue(product.ProductCatalogNo, out var lst)
            ? lst
            : new List<Disc>();

        var discViews = new List<DiscView>();
        foreach (var disc in discs)
        {
            // ディスクの全トラックは BuildContext で catalog_no 別に事前展開済み（SiteDataLoader が
            // 全件取得して GroupBy(CatalogNo) 済み）。本ループでの DB アクセスは発生しない。
            var tracks = _ctx.TracksByCatalogNo.TryGetValue(disc.CatalogNo, out var trk)
                ? trk
                : (IReadOnlyList<Track>)Array.Empty<Track>();

            // BGM cue マップへの追加ロード：(a) BGM トラック本体の bgm_series_id 経由 と
            // (b) SONG トラックが song_recording_bgm_assignments 経由で参照する series_id の両方をカバーする。
            // 全 cue は BuildContext.BgmCuesBySeries で事前展開済みのため、本ループでも DB アクセスは
            // 発生せず、必要なシリーズの cue を辞書から取り出して (series_id, m_no_detail) キーの
            // ローカルマップに追加するのみ。
            var seriesIdsToLoad = new HashSet<int>();
            foreach (var t in tracks)
            {
                if (t.BgmSeriesId is int bsid) seriesIdsToLoad.Add(bsid);
                if (t.SongRecordingId is int rid
                    && bgmAssignmentsByRecordingId.TryGetValue(rid, out var assigns))
                {
                    foreach (var a in assigns) seriesIdsToLoad.Add(a.SeriesId);
                }
            }
            foreach (var sid in seriesIdsToLoad)
            {
                bool seriesAlreadyLoaded = bgmCueMap.Keys.Any(k => k.seriesId == sid);
                if (!seriesAlreadyLoaded
                    && _ctx.BgmCuesBySeries.TryGetValue(sid, out var cues))
                {
                    foreach (var cue in cues) bgmCueMap[(cue.SeriesId, cue.MNoDetail)] = cue;
                }
            }

            // ディスク内の劇伴起源シリーズ集合を集める。BGM トラック本体（content_kind_code='BGM'）に加え、
            // SONG トラックが song_recording_bgm_assignments 経由で参照しているシリーズ ID も含める
            // （SONG 両性トラックも「ディスク内に複数シリーズ起源の劇伴が同居しているか」の判定対象）。
            // 集合サイズが 2 以上なら各劇伴行の M ナンバー先頭にシリーズ略記を付けて出典シリーズを識別できるようにする
            // （映画オールスターズ系のサウンドトラックで M03 が複数シリーズに別々に存在するため、
            // 略記が無いとどのシリーズ起源か即座に区別できない実害がある）。
            // 略記には series.title_short を使用する。プロジェクト方針として title_short は
            // 通常 UI では使わないが、本用途は「劇伴 M ナンバーの起源シリーズ識別」という
            // 限定的な補助プレフィックスのため、ディスク内文脈での例外として許容する
            // （title_short が空のシリーズはフォールバックで title 全文を使う）。
            var bgmSeriesIdsInDisc = new HashSet<int>();
            foreach (var t in tracks)
            {
                if (string.Equals(t.ContentKindCode, "BGM", StringComparison.Ordinal)
                    && t.BgmSeriesId is int sid)
                {
                    bgmSeriesIdsInDisc.Add(sid);
                }
                if (string.Equals(t.ContentKindCode, "SONG", StringComparison.Ordinal)
                    && t.SongRecordingId is int rid
                    && bgmAssignmentsByRecordingId.TryGetValue(rid, out var assigns))
                {
                    foreach (var a in assigns) bgmSeriesIdsInDisc.Add(a.SeriesId);
                }
            }
            // series_id → 略記プレフィックス（末尾に半角空白を付けた状態でテンプレ側に渡す）。
            // プレフィックス出力条件は次の OR：
            //   (a) ディスク内に複数の劇伴起源シリーズが同居している（bgmSeriesIdsInDisc.Count >= 2）
            //   (b) 当該劇伴シリーズが、ディスク自身のシリーズ（disc.series_id）と異なる
            // (b) は単一シリーズの劇伴しか含まないディスクであっても、その劇伴がディスクのシリーズと
            // 違う場合（=「ディスクは映画 X のサウンドトラックなのに、劇伴トラックは TV シリーズ Y 由来」など
            // 出典シリーズの食い違いがあるディスク）には略記を出すべき、というルール。
            // 略記には series.title_short を使用する。プロジェクト方針として title_short は通常 UI では
            // 使わないが、本用途は「劇伴 M ナンバーの起源シリーズ識別」という限定的な補助プレフィックス
            // のため、ディスク内文脈での例外として許容する（title_short が空のシリーズは title 全文を使う）。
            var bgmSeriesPrefixMap = new Dictionary<int, string>();
            bool multipleSeriesInDisc = bgmSeriesIdsInDisc.Count >= 2;
            int? discSeriesId = disc.SeriesId;  // ディスク自身のシリーズ ID（NULL ならディスク無紐付け）
            foreach (var sid in bgmSeriesIdsInDisc)
            {
                bool differsFromDiscSeries = discSeriesId is not int dsid || dsid != sid;
                if (multipleSeriesInDisc || differsFromDiscSeries)
                {
                    if (_ctx.SeriesById.TryGetValue(sid, out var bgmSeries))
                    {
                        string shortLabel = !string.IsNullOrEmpty(bgmSeries.TitleShort)
                            ? bgmSeries.TitleShort!
                            : bgmSeries.Title;
                        bgmSeriesPrefixMap[sid] = shortLabel;
                    }
                }
            }

            // BuildTrackRow は SONG ／ BGM のクレジット取得（song_credits / song_recording_singers /
            // bgm_cue_credits）を BuildContext 由来の辞書 lookup で同期完結させる。
            var trackRows = new List<TrackRow>(tracks.Count);
            foreach (var t in tracks.OrderBy(t => t.TrackNo).ThenBy(t => t.SubOrder))
            {
                var row = BuildTrackRow(
                    t, trackKindMap, sizeVariantMap, partVariantMap, songMap, recordingMap, bgmCueMap,
                    bgmSeriesPrefixMap, bgmAssignmentsByRecordingId);
                trackRows.Add(row);
            }

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
                Heading = BuildDiscHeading(product.DiscCount, disc.DiscNoInSet, product.Title, disc.Title ?? ""),
                MediaFormat = disc.MediaFormat,
                Mcn = disc.Mcn ?? "",
                DiscKindLabel = discKindLabel,
                SeriesLink = seriesLink,
                SeriesTitle = seriesTitle,
                Tracks = trackRows
            });
        }

        // 商品 JAN: 所属ディスクのうち CD 系（CD / CD_ROM）で MCN を取得済みの先頭ディスクの MCN。
        // JAN は市販品では複数ディスクで共通のため商品単位で 1 つ持てば足りる。DVD/BD のみの商品は空。
        string productJan = discViews
            .FirstOrDefault(dv => (dv.MediaFormat == "CD" || dv.MediaFormat == "CD_ROM") && dv.Mcn != "")
            ?.Mcn ?? "";

        string productKindLabel = productKindMap.TryGetValue(product.ProductKindCode, out var pk) ? pk.NameJa : product.ProductKindCode;

        // レーベル・販売元の表示文字列は構造化 ID から解決する一本道。
        // ID が NULL またはマスタ未登録なら空文字。フリーテキスト列はもう存在しない。
        string labelText       = ResolveCompanyName(product.LabelProductCompanyId,       productCompanyMap);
        string distributorText = ResolveCompanyName(product.DistributorProductCompanyId, productCompanyMap);

        // 商品の総収録時間：所属ディスクの total_length（CD-DA は frames=1/75 秒、BD/DVD は ms）を
        // ms に揃えて合算する。1 枚も尺取得済みでない商品は空表示にする（テンプレ側で行ごと非表示）。
        double totalLengthMs = 0;
        bool hasAnyLength = false;
        foreach (var d in discs)
        {
            if (d.TotalLengthFrames.HasValue)
            {
                totalLengthMs += d.TotalLengthFrames.Value * 1000.0 / 75.0;
                hasAnyLength = true;
            }
            else if (d.TotalLengthMs.HasValue)
            {
                totalLengthMs += d.TotalLengthMs.Value;
                hasAnyLength = true;
            }
        }
        string totalLengthLabel = "";
        string totalLengthFraction = "";
        if (hasAnyLength)
        {
            (totalLengthLabel, totalLengthFraction) = SplitTotalLengthMs(totalLengthMs);
        }

        // 外部プラットフォームへのリンク。各 ID があるときだけ URL を組み立てる。
        // Amazon は物理（CD/BD/DVD）／デジタル（Amazon Music の MP3 アルバム）で
        // 別 ASIN が割り当てられるため、両方を並列に持って商品ページで両ボタンを並べる。
        string tag = _ctx.Config.AmazonAssociateTag;
        string amazonAsinCd = product.AmazonAsinCd ?? "";
        string amazonCdUrl = "";
        if (amazonAsinCd.Length > 0)
        {
            amazonCdUrl = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(amazonAsinCd);
            if (tag.Length > 0)
                amazonCdUrl += "?tag=" + Uri.EscapeDataString(tag);
        }

        string amazonAsinDigital = product.AmazonAsinDigital ?? "";
        string amazonDigitalUrl = "";
        if (amazonAsinDigital.Length > 0)
        {
            amazonDigitalUrl = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(amazonAsinDigital);
            if (tag.Length > 0)
                amazonDigitalUrl += "?tag=" + Uri.EscapeDataString(tag);
        }

        // ジャケット画像。primary は代表（CoverImageUrl 計算プロパティ）。
        // 「詳細で両方表示」フラグが立ち、CD/デジタル両 URL があって互いに異なるときだけ、
        // もう片方を secondary に詰めてテンプレで 2 枚並べる（同一／片方だけなら secondary は空＝1 枚）。
        string coverPrimaryUrl = product.CoverImageUrl ?? "";
        string coverSecondaryUrl = "";
        if (product.CoverImageShowBoth)
        {
            string cdCover = product.CoverImageUrlCd ?? "";
            string digitalCover = product.CoverImageUrlDigital ?? "";
            if (cdCover.Length > 0 && digitalCover.Length > 0
                && !string.Equals(cdCover, digitalCover, StringComparison.Ordinal))
            {
                coverSecondaryUrl = string.Equals(coverPrimaryUrl, cdCover, StringComparison.Ordinal)
                    ? digitalCover
                    : cdCover;
            }
        }

        var content = new ProductDetailModel
        {
            Product = new ProductView
            {
                ProductCatalogNo = product.ProductCatalogNo,
                Title = product.Title,
                TitleEn = product.TitleEn ?? "",
                ProductKindLabel = productKindLabel,
                ReleaseDate = JpDateFormat.Date(product.ReleaseDate),
                PriceIncTax = product.PriceIncTax?.ToString("N0") ?? "",
                PriceExTax = product.PriceExTax?.ToString("N0") ?? "",
                DiscCount = product.DiscCount,
                LabelText = labelText,
                DistributorText = distributorText,
                TotalLengthLabel = totalLengthLabel,
                TotalLengthFraction = totalLengthFraction,
                Jan = productJan,
                // ASIN は物理／デジタルの 2 値で持ち、それぞれのリンクとセットで保持する。
                AmazonAsinCd = product.AmazonAsinCd ?? "",
                AmazonAsinDigital = product.AmazonAsinDigital ?? "",
                CoverImageUrl = coverPrimaryUrl,
                // 詳細ページで CD/デジタル両方を並べるときの 2 枚目（代表と異なる方）。空なら 1 枚表示。
                CoverImageSecondaryUrl = coverSecondaryUrl,
                // attribution 文言の出し分け（テンプレ側で `amazon_cd` / `amazon_digital` の
                // 2 値で分岐させる）。未取得は空文字で、テンプレ側はその場合 attribution 行ごと出さない。
                CoverImageSource = product.CoverImageSource ?? "",
                AmazonCdUrl = amazonCdUrl,
                AmazonDigitalUrl = amazonDigitalUrl,
                Notes = product.Notes ?? "",
                OfficialUrl = product.OfficialUrl ?? ""
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
        // 商品 JAN が 13 桁の数字（= JAN/EAN-13 = schema.org の GTIN-13）なら gtin13 を出力する。
        // JAN は複数ディスクで共通のため、複数枚組 BOX でも商品単位で一意に定まる。
        if (productJan.Length == 13 && productJan.All(char.IsDigit))
        {
            jsonLdDict["gtin13"] = productJan;
        }
        // schema.org の offers として Amazon の物理／デジタル 2 リンクを配列で出力する。
        // どちらかしか登録されていない場合は片方だけの配列、両方未登録なら offers キー自体を出さない。
        // schema.org/Offer の最小要素 (@type / url / availability) を入れ、リッチリザルトの
        // 「購入リンク」候補に乗せやすくする。アフィリエイトタグは url に既に含まれている。
        var offers = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrEmpty(amazonCdUrl))
        {
            offers.Add(new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["url"] = amazonCdUrl,
                ["availability"] = "https://schema.org/InStock",
                ["category"] = "Physical"
            });
        }
        if (!string.IsNullOrEmpty(amazonDigitalUrl))
        {
            offers.Add(new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["url"] = amazonDigitalUrl,
                ["availability"] = "https://schema.org/InStock",
                ["category"] = "Digital"
            });
        }
        if (offers.Count > 0)
        {
            jsonLdDict["offers"] = offers;
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
                new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽商品(CD/配信)", Url = "/products/" },
                new BreadcrumbItem { Label = product.Title, Url = "" }
            },
            OgType = "website",
            JsonLd = jsonLd
        };
        _page.RenderAndWrite(productUrl, "products", "products-detail.sbn", content, layout);
    }

    /// <summary>商品詳細ページの &lt;meta name="description"&gt; 用説明文を実データから組み立てる。</summary>
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

    /// <summary>商品社名 ID から社名（和名）を引く（構造化 ID で解決する）。 ID が NULL、マスタ未登録、論理削除済みのいずれかの場合は空文字を返す （フリーテキストフォールバックは存在しない）。</summary>
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

    /// <summary>
    /// 1 トラックを表示用 DTO に変換する（非同期化＋構造化クレジット解決を内包）。
    /// 表示は ContentKindCode で 3 系統に分岐する：
    /// <list type="bullet">
    ///   <item><b>SONG</b>：タイトルは「variant_label 優先、無ければ親曲 title」を採用してリンク化。
    ///     右にサイズ・パートのバッジ（VOCAL = 既定として非表示、楽曲詳細と同型）。
    ///     下段にクレジット行：作詞・作曲・編曲・歌のバッジ + 各名義 HTML を空白区切りで連結。</item>
    ///   <item><b>BGM</b>：タイトルは「メニュー表記」を採用。下段に「Mナンバー [メニュー]」と
    ///     bgm_cue_credits の役職バッジ + 名義 HTML を組み立てる。</item>
    ///   <item><b>その他（DRAMA 等）</b>：タイトルは track_title_override 単体、下段は空。</item>
    /// </list>
    /// 名義は person_aliases / character_aliases の構造化 ID を解決して /persons/{id}/ や
    /// /characters/{id}/ へのリンク化を行う（楽曲詳細と同じソース）。
    /// </summary>
    private TrackRow BuildTrackRow(
        Track t,
        IReadOnlyDictionary<string, TrackContentKind> trackKindMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<int, Song> songMap,
        IReadOnlyDictionary<int, SongRecording> recordingMap,
        IReadOnlyDictionary<(int seriesId, string mNoDetail), BgmCue> bgmCueMap,
        IReadOnlyDictionary<int, string> bgmSeriesPrefixMap,
        IReadOnlyDictionary<int, IReadOnlyList<(string PartCode, int SeriesId, string MNoDetail)>> bgmAssignmentsByRecordingId)
    {
        string contentKindLabel = trackKindMap.TryGetValue(t.ContentKindCode, out var ck) ? ck.NameJa : t.ContentKindCode;
        string title = "";
        string titleHtml = "";
        string metaLineHtml = "";
        string kindBadgesHtml = "";
        // SONG トラックがあわせて劇伴としても扱われる場合に true（song_recording_bgm_assignments
        // 経由で当該録音 ID に 1 件以上の cue 紐付けがある）。テンプレ側でトラックカードの
        // 円バッジ装飾を SONG 赤 + BGM 緑の斜め分割塗りに切り替えるフラグとして使う。
        bool hasBgmAssignments = false;
        // SONG 録音の BGM 紐付けがある場合の、独立メタ行 HTML。歌の役職クレジット行（.track-meta-line）
        // とは別の `<div class="track-meta-line">` としてテンプレ側で grid 兄弟に積む構造のため、
        // ここでは紐付け行の中身 HTML 文字列だけを別変数に切り出して保持する。
        // 紐付けが無い場合は空文字、テンプレ側でも要素自体を出さない。
        string bgmAssignmentMetaLineHtml = "";
        // NEXT トラックの「原曲: 元曲タイトル」行。NEXT 以外は空。
        string originalSongMetaLineHtml = "";
        string songLink = "";

        switch (t.ContentKindCode)
        {
            case "SONG":
                if (t.SongRecordingId is int rid && recordingMap.TryGetValue(rid, out var rec)
                    && songMap.TryGetValue(rec.SongId, out var song))
                {
                    // タイトル：variant_label 優先、無ければ song.title。track_title_override は手動上書き用に維持。
                    string displayTitle = !string.IsNullOrEmpty(t.TrackTitleOverride)
                        ? t.TrackTitleOverride!
                        : (!string.IsNullOrEmpty(rec.VariantLabel) ? rec.VariantLabel! : song.Title);
                    title = displayTitle;
                    songLink = PathUtil.SongUrl(song.SongId);
                    // タイトル本文は <a class="products-tracks-card-title-link"> で包む。
                    // CSS 側でこの a 要素に position:absolute の透明オーバーレイ (::before) を当てて
                    // カード全域に拡張し、カードのどこをクリックしても歌詳細に飛ぶ B 型構造を実現する。
                    // カード内の他のリンク（名義リンク・track-bgm-cuelink など）は z-index でオーバーレイの
                    // 上に乗せて独立クリック可能にする（CSS 側で position:relative + z-index 設定）。
                    // この a の中身は .track-title-text span だけ（バッジ等は a の外、titleline 直下の兄弟）。
                    titleHtml = $"<a class=\"products-tracks-card-title-link\" href=\"{HtmlEscape(songLink)}\"><span class=\"track-title-text\">{HtmlEscape(displayTitle)}</span></a>";

                    // サイズ・パートバッジ：楽曲詳細と同じ意匠（淡い緑＝サイズ、淡い青＝パート）。
                    // パート「VOCAL（歌入り）」は録音物の既定状態としてバッジ非表示。
                    var badgeSb = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(t.SongSizeVariantCode)
                        && sizeVariantMap.TryGetValue(t.SongSizeVariantCode!, out var sv))
                    {
                        badgeSb.Append("<span class=\"recording-tracks-kind-badge recording-tracks-kind-size\">")
                               .Append(HtmlEscape(sv.NameJa))
                               .Append("</span>");
                    }
                    if (!string.IsNullOrEmpty(t.SongPartVariantCode)
                        && !string.Equals(t.SongPartVariantCode, "VOCAL", StringComparison.Ordinal)
                        && partVariantMap.TryGetValue(t.SongPartVariantCode!, out var pv))
                    {
                        badgeSb.Append("<span class=\"recording-tracks-kind-badge recording-tracks-kind-part\">")
                               .Append(HtmlEscape(pv.NameJa))
                               .Append("</span>");
                    }
                    kindBadgesHtml = badgeSb.ToString();

                    // クレジット行：作詞・作曲・編曲（song_credits）+ 歌（song_recording_singers）。
                    // 役職コード順に名義 HTML を組み立てて TrackCreditHtmlBuilder.BuildMergedRoleSegmentsHtml に
                    // 渡し、隣接する役職で名義 HTML が完全一致するなら「[作詞][作曲] 名義」のように
                    // バッジを並べて名義を 1 回だけ出す統合処理を任せる
                    // （フリーテキストの「EFFY」連続や、構造化由来の同一 alias 連続などを自動でマージ。
                    //  構造化由来とフリーテキストが混在するケースは HTML 文字列が一致しないので
                    //  正しくマージ対象外）。空文字の役職セグメントは BuildMergedRoleSegmentsHtml 側で
                    //  自動的に除外される。
                    string lyricsHtml = BuildSongCreditNamesHtml(song, "LYRICS");
                    string compositionHtml = BuildSongCreditNamesHtml(song, "COMPOSITION");
                    string arrangementHtml = BuildSongCreditNamesHtml(song, "ARRANGEMENT");
                    string vocalsHtml = BuildRecordingSingersHtml(rec);
                    string chorusHtml = BuildRecordingChorusHtml(rec);
                    metaLineHtml = _creditHtml!.BuildMergedRoleSegmentsHtml(new[]
                    {
                        ("LYRICS",         "作詞",   lyricsHtml),
                        ("COMPOSITION",    "作曲",   compositionHtml),
                        ("ARRANGEMENT",    "編曲",   arrangementHtml),
                        ("VOCALS",         "歌",     vocalsHtml),
                        ("BACKING_VOCALS", "コーラス", chorusHtml),
                    });

                    // この録音が「劇伴としても扱う」紐付け（song_recording_bgm_assignments）を
                    // 持つ場合、歌の役職クレジット行とは別の独立メタ行として「シリーズ略記 + Mナンバー [メニュー]」
                    // を BGM 分岐と同じ書式で出す。`.track-meta-line` は flex コンテナのため
                    // <br> による改行が効かない（flex アイテムとして無視される）ため、
                    // 役職クレジット行とは別の `<div class="track-meta-line">` として bgmAssignmentMetaLineHtml に
                    // 切り出し、テンプレ側で grid 兄弟として縦に積む構造を採る。
                    // 複数 cue に紐付くケースでは同一メタ行内に並べる（同じ「BGM 紐付け」セクションなので
                    // 内部の各 cue は flex の自然折り返しに任せる。複数 cue が必要に応じて wrap される）。
                    // シリーズ略記の出力可否は bgmSeriesPrefixMap に従う（ディスク内多シリーズ時、
                    // またはディスクシリーズと異なる場合に出る）。
                    // 「Mナンバー [メニュー]」の塊は劇伴詳細 /bgms/{slug}/#cue-{m_no_detail} へのリンクで包む。
                    //
                    // パートフィルタ：中間テーブル行のパートコードが当該トラックの song_part_variant_code と
                    // 一致するか、または中間テーブル行が sentinel '_ANY'（パート区別なく適用）のとき、
                    // 当該紐付けはこのトラックに適用される。
                    // tracks 側 song_part_variant_code が NULL（パート未登録）のトラックは
                    // 中間テーブルとマッチしない（NULL を許容しない方針）。
                    if (bgmAssignmentsByRecordingId.TryGetValue(rid, out var assignListRaw) && assignListRaw.Count > 0)
                    {
                        var applicableAssigns = assignListRaw
                            .Where(a => string.Equals(a.PartCode, "_ANY", StringComparison.Ordinal)
                                || string.Equals(a.PartCode, t.SongPartVariantCode, StringComparison.Ordinal))
                            .ToList();
                        if (applicableAssigns.Count > 0)
                        {
                            hasBgmAssignments = true;
                            var assignSb = new System.Text.StringBuilder();
                            bool first = true;
                            foreach (var assign in applicableAssigns
                                .OrderBy(a => a.SeriesId).ThenBy(a => a.MNoDetail, StringComparer.Ordinal))
                            {
                                int assignSeriesId = assign.SeriesId;
                                string assignMNo = assign.MNoDetail;
                                // 紐付け先 cue を bgmCueMap から解決。マップにない場合は当該紐付けを行を出さずスキップ
                                // （データ不整合の防御。通常は disc 走査の preload で全シリーズ分が乗っている）。
                                if (!bgmCueMap.TryGetValue((assignSeriesId, assignMNo), out var assignCue))
                                    continue;

                                string assignMNoLabel = assignCue.IsTempMNo ? "(Mナンバー不明)" : assignCue.MNoDetail;

                                string assignAnchorUrl = "";
                                if (_ctx.SeriesById.TryGetValue(assignCue.SeriesId, out var assignBgmSeries))
                                {
                                    assignAnchorUrl = PathUtil.BgmCueAnchorUrl(assignBgmSeries.Slug, assignCue.MNoDetail);
                                }

                                // 同一メタ行内の cue と cue の区切り（同行内に複数 cue が並ぶ場合、
                                // 内側の各 cue の塊を flex 自然折り返しさせるための半角空白）。
                                // 1 個目の cue は何も区切らない。
                                if (!first) assignSb.Append(' ');
                                first = false;

                                if (bgmSeriesPrefixMap.TryGetValue(assignCue.SeriesId, out var assignSeriesShort)
                                    && !string.IsNullOrEmpty(assignSeriesShort))
                                {
                                    assignSb.Append("<span class=\"track-bgm-series\">")
                                            .Append(HtmlEscape(assignSeriesShort))
                                            .Append("</span> ");
                                }
                                var assignMnoMenuSb = new System.Text.StringBuilder();
                                assignMnoMenuSb.Append("<span class=\"track-bgm-mno\">")
                                               .Append(HtmlEscape(assignMNoLabel))
                                               .Append("</span>");
                                if (!string.IsNullOrEmpty(assignCue.MenuTitle))
                                {
                                    assignMnoMenuSb.Append("<span class=\"track-bgm-menu muted\"> [")
                                                   .Append(HtmlEscape(assignCue.MenuTitle!))
                                                   .Append("]</span>");
                                }
                                if (string.IsNullOrEmpty(assignAnchorUrl))
                                {
                                    assignSb.Append(assignMnoMenuSb);
                                }
                                else
                                {
                                    assignSb.Append("<a class=\"track-bgm-cuelink\" href=\"")
                                            .Append(HtmlEscape(assignAnchorUrl))
                                            .Append("\">")
                                            .Append(assignMnoMenuSb)
                                            .Append("</a>");
                                }

                                // 作曲・編曲クレジットはここでは出さない：当該トラックは SONG なので
                                // 同カード内の歌の役職クレジット行（metaLineHtml）に 作詞/作曲/編曲/歌 の
                                // 構造化クレジットが既に並んでおり、BGM 紐付け行で重ねて出すと冗長になる。
                                // 仕様確認済み（歌として作編曲が出ているので BGM 紐付け側では不要）。
                            }
                            bgmAssignmentMetaLineHtml = assignSb.ToString();
                        }
                    }
                }
                else
                {
                    title = t.TrackTitleOverride ?? "(歌情報未登録)";
                    // 歌情報が解決できない場合はリンクなしの平文。テンプレ側のタイトル本文セレクタが
                    // 揃うよう、他分岐と同じ .track-title-text ラッパで包む。
                    titleHtml = $"<span class=\"track-title-text\">{HtmlEscape(title)}</span>";
                }
                break;

            case "BGM":
                if (t.BgmSeriesId is int bsid && t.BgmMNoDetail is string mnd
                    && bgmCueMap.TryGetValue((bsid, mnd), out var cue))
                {
                    string mNoLabel = cue.IsTempMNo ? "(Mナンバー不明)" : cue.MNoDetail;
                    string menuTitle = !string.IsNullOrEmpty(t.TrackTitleOverride)
                        ? t.TrackTitleOverride!
                        : (cue.MenuTitle ?? "(タイトル未登録)");
                    title = menuTitle;

                    // 劇伴詳細ページの該当 cue 行へのアンカー URL を組み立てる。
                    // cue.SeriesId からシリーズ slug を解決して /bgms/{slug}/#cue-{m_no} 形式に。
                    // シリーズが解決できない異常データの場合は anchorUrl=空 で「リンク無しの平文」に
                    // 自動フォールバックする。
                    string anchorUrl = "";
                    if (_ctx.SeriesById.TryGetValue(cue.SeriesId, out var bgmSeries))
                    {
                        anchorUrl = PathUtil.BgmCueAnchorUrl(bgmSeries.Slug, cue.MNoDetail);
                    }

                    // 上段（タイトル）：アンカーリンクが解決できれば .products-tracks-card-title-link 化して
                    // カード全域オーバーレイで劇伴詳細 cue 行へジャンプさせる（SONG カードと同じ B 型構造）。
                    // 解決できない場合（cue.SeriesId からシリーズ slug が引けない異常データ）は
                    // リンクなし平文にフォールバック（カードホバー反応も発生しない）。
                    titleHtml = string.IsNullOrEmpty(anchorUrl)
                        ? $"<span class=\"track-title-text\">{HtmlEscape(menuTitle)}</span>"
                        : $"<a class=\"products-tracks-card-title-link\" href=\"{HtmlEscape(anchorUrl)}\"><span class=\"track-title-text\">{HtmlEscape(menuTitle)}</span></a>";

                    // メタ行：「{シリーズ略記} {M番号} [{メニュー名}]」をリーディングとして出し、続けて
                    // bgm_cue_credits の構造化クレジット（COMPOSITION / ARRANGEMENT）を役職バッジ + 名義で展開。
                    // 構造化エントリが無い cue は bgm_cues.composer_name / arranger_name のフリーテキストへ
                    // フォールバックしてバッジ + プレーン文字列名義（リンクなし）を組み立てる。
                    // メニュー名は title と重複する場合もあるが、劇伴の慣習として「Mナンバー [メニュー]」の
                    // 形式が業界標準（クレジット表記同様）のため、敢えて [] 付きで再掲する。
                    // シリーズ略記は bgmSeriesPrefixMap に当該 series_id のエントリがある場合のみ
                    // 出力する（=ディスク内に複数シリーズ起源の劇伴が同居している、またはディスク自身の
                    // シリーズと当該劇伴シリーズが異なる場合）。
                    // 「Mナンバー [メニュー]」自体はリンク化しない：カード全体オーバーレイの遷移先と
                    // 同じ /bgms/{slug}/#cue-... なので、独立リンク化しても情報的に冗長で、視覚的に
                    // 「ホバー時下線」が並ぶとカード全体反応とぶつかってチラつくため。
                    var metaSb = new System.Text.StringBuilder();
                    if (bgmSeriesPrefixMap.TryGetValue(cue.SeriesId, out var bgmSeriesShort)
                        && !string.IsNullOrEmpty(bgmSeriesShort))
                    {
                        metaSb.Append("<span class=\"track-bgm-series\">")
                              .Append(HtmlEscape(bgmSeriesShort))
                              .Append("</span> ");
                    }
                    metaSb.Append("<span class=\"track-bgm-mno\">").Append(HtmlEscape(mNoLabel)).Append("</span>");
                    if (!string.IsNullOrEmpty(cue.MenuTitle))
                    {
                        metaSb.Append("<span class=\"track-bgm-menu muted\"> [").Append(HtmlEscape(cue.MenuTitle!)).Append("]</span>");
                    }
                    // 劇伴の構造化クレジット（作曲・編曲）。無ければフリーテキストへフォールバック。
                    string bgmCreditsHtml = BuildBgmCueCreditsSegments(cue.SeriesId, cue.MNoDetail);
                    if (string.IsNullOrEmpty(bgmCreditsHtml))
                    {
                        bgmCreditsHtml = BuildBgmFreetextCreditsHtml(cue);
                    }
                    if (!string.IsNullOrEmpty(bgmCreditsHtml))
                    {
                        metaSb.Append(" <span class=\"track-credit-list\">").Append(bgmCreditsHtml).Append("</span>");
                    }
                    metaLineHtml = metaSb.ToString();
                }
                else
                {
                    title = t.TrackTitleOverride ?? "(劇伴情報未登録)";
                    titleHtml = $"<span class=\"track-title-text\">{HtmlEscape(title)}</span>";
                }
                break;

            case "NEXT":
                // 次回予告：tracks 側のスキーマ制約として
                // (song_recording_id NOT NULL + song_size_variant_code='NEXT' + song_part_variant_code='INST')
                // のセットが必須（trg_tracks_bi/bu_fk_consistency で強制）。
                //
                // 表示仕様:
                //   タイトル：track_title_override（例「次回もキュアット解決!」）。カード全体を歌詳細リンクで
                //     被せるため、SONG と同じ products-tracks-card-title-link オーバーレイで包む。
                //   バッジ：サイズ「次回予告」だけ出す（パート＝INST 固定は表示しない）。サイズバッジは
                //     視覚上「予告色」として青系（.recording-tracks-kind-next）で塗り、当該歌詳細への
                //     独立リンクとしても機能させる（カード全体クリックとは別のクリック手段として残す）。
                //   原曲行：役職行の上に「原曲: 元曲タイトル」を muted で出す。元曲タイトルは
                //     variant_label 優先、無ければ song.title。
                //   クレジット行：劇伴 (BGM) 準拠で「作曲」「編曲」のみ役職バッジ + 名義を出す。
                //     名義リンクは song_credits の構造化エントリがある場合のみ /persons/{id}/ にリンク、
                //     構造化が無い場合はフリーテキスト平文（リンク・下線無し）。同名義の作曲・編曲は
                //     BuildMergedRoleSegmentsHtml で「[作曲][編曲] 名義」と自動マージされる。
                if (t.SongRecordingId is int nrid
                    && recordingMap.TryGetValue(nrid, out var nrec)
                    && songMap.TryGetValue(nrec.SongId, out var nsong))
                {
                    title = t.TrackTitleOverride ?? "";
                    songLink = PathUtil.SongUrl(nsong.SongId);
                    titleHtml = $"<a class=\"products-tracks-card-title-link\" href=\"{HtmlEscape(songLink)}\"><span class=\"track-title-text\">{HtmlEscape(title)}</span></a>";

                    // サイズバッジ「次回予告」をブルー系の独立リンクとして出す。パート（INST）は出さない。
                    if (!string.IsNullOrEmpty(t.SongSizeVariantCode)
                        && sizeVariantMap.TryGetValue(t.SongSizeVariantCode!, out var nsv))
                    {
                        kindBadgesHtml = $"<a class=\"recording-tracks-kind-badge recording-tracks-kind-next\" href=\"{HtmlEscape(songLink)}\">{HtmlEscape(nsv.NameJa)}</a>";
                    }

                    // 原曲タイトルを「原曲: ...」行として組み立て（役職行の上に出る）。
                    // 元曲タイトルは variant_label 優先、無ければ song.title。
                    string sourceTitle = !string.IsNullOrEmpty(nrec.VariantLabel) ? nrec.VariantLabel! : nsong.Title;
                    originalSongMetaLineHtml =
                        $"<span class=\"track-next-source-label muted\">原曲:</span> "
                        + $"<span class=\"track-next-source-title\">{HtmlEscape(sourceTitle)}</span>";

                    string nextCompositionHtml = BuildSongCreditNamesHtml(nsong, "COMPOSITION");
                    string nextArrangementHtml = BuildSongCreditNamesHtml(nsong, "ARRANGEMENT");
                    metaLineHtml = _creditHtml!.BuildMergedRoleSegmentsHtml(new[]
                    {
                        ("COMPOSITION", "作曲", nextCompositionHtml),
                        ("ARRANGEMENT", "編曲", nextArrangementHtml),
                    });
                }
                else
                {
                    // recording 解決に失敗した NEXT（マスタ整備中の暫定状態想定）：タイトル平文のみ。
                    title = t.TrackTitleOverride ?? "";
                    titleHtml = $"<span class=\"track-title-text\">{HtmlEscape(title)}</span>";
                }
                break;

            default:
                title = t.TrackTitleOverride ?? "";
                titleHtml = $"<span class=\"track-title-text\">{HtmlEscape(title)}</span>";
                break;
        }

        var (lenInt, lenFrac) = SplitLength(t.LengthFrames);

        return new TrackRow
        {
            TrackNo = t.TrackNo,
            SubOrder = t.SubOrder,
            ContentKindCode = t.ContentKindCode,
            ContentKindLabel = contentKindLabel,
            Title = title,
            TitleHtml = titleHtml,
            KindBadgesHtml = kindBadgesHtml,
            MetaLineHtml = metaLineHtml,
            BgmAssignmentMetaLineHtml = bgmAssignmentMetaLineHtml,
            OriginalSongMetaLineHtml = originalSongMetaLineHtml,
            LengthLabel = lenInt,
            LengthFraction = lenFrac,
            Isrc = t.Isrc ?? "",
            SongLink = songLink,
            HasBgmAssignments = hasBgmAssignments
        };
    }

    /// <summary>歌の構造化クレジットを TrackCreditHtmlBuilder 経由で取得する薄いラッパー。 実体は BuildContext 由来の辞書 lookup なので同期完結する。</summary>
    private string BuildSongCreditNamesHtml(Song song, string roleCode)
        => _creditHtml!.BuildSongCreditNamesHtml(song, roleCode);

    /// <summary>録音の歌唱者連名を TrackCreditHtmlBuilder 経由で取得する薄いラッパー。 実体は BuildContext 由来の辞書 lookup なので同期完結する。</summary>
    private string BuildRecordingSingersHtml(SongRecording rec)
        => _creditHtml!.BuildRecordingVocalistsHtml(rec);

    /// <summary>録音のコーラス（BACKING_VOCALS）連名を TrackCreditHtmlBuilder 経由で取得する薄いラッパー。</summary>
    private string BuildRecordingChorusHtml(SongRecording rec)
        => _creditHtml!.BuildRecordingChorusHtml(rec);

    /// <summary>劇伴クレジット（役職別バッジ+名義の列）を TrackCreditHtmlBuilder 経由で取得する薄いラッパー。 実体は BuildContext 由来の辞書 lookup なので同期完結する。</summary>
    private string BuildBgmCueCreditsSegments(int seriesId, string mNoDetail)
        => _creditHtml!.BuildBgmCueCreditsSegmentsHtml(seriesId, mNoDetail);

    /// <summary>
    /// 劇伴 cue のフリーテキストクレジット（<c>bgm_cues.composer_name</c> / <c>arranger_name</c>）から
    /// バッジ + プレーン文字列名義（リンクなし）の HTML を組み立てる。
    /// 構造化エントリ <c>bgm_cue_credits</c> が無い cue のフォールバック表現として使う。
    /// <para>
    /// 同一名義の作曲・編曲はバッジを並べて 1 セグメントに統合する
    /// （<c>[作曲][編曲] 佐藤 直紀</c> の形）。これは構造化エントリ版
    /// <see cref="TrackCreditHtmlBuilder.BuildBgmCueCreditsSegmentsHtmlAsync"/> と同じ
    /// 視覚規則に揃えるためのもの。
    /// </para>
    /// <para>
    /// 両方空文字なら空文字を返す。
    /// </para>
    /// </summary>
    private string BuildBgmFreetextCreditsHtml(BgmCue cue)
    {
        string composer = cue.ComposerName?.Trim() ?? "";
        string arranger = cue.ArrangerName?.Trim() ?? "";
        if (composer.Length == 0 && arranger.Length == 0) return "";

        var sb = new System.Text.StringBuilder();

        // 構造化エントリ版と揃った視覚規則として、composer と arranger が同一文字列の場合は
        // バッジを並べて名義を 1 回だけ出す統合表示にする。
        if (composer.Length > 0 && arranger.Length > 0
            && string.Equals(composer, arranger, StringComparison.Ordinal))
        {
            sb.Append("<span class=\"track-credit-segment\">")
              .Append(BuildRoleBadgeSpan("COMPOSITION", "作曲"))
              .Append(BuildRoleBadgeSpan("ARRANGEMENT", "編曲"))
              .Append("<span class=\"track-credit-names\">")
              .Append(HtmlEscape(composer))
              .Append("</span></span>");
            return sb.ToString();
        }

        // 同一文字列でない or 片方のみ。それぞれ別セグメントで出す。
        if (composer.Length > 0)
        {
            sb.Append("<span class=\"track-credit-segment\">")
              .Append(BuildRoleBadgeSpan("COMPOSITION", "作曲"))
              .Append("<span class=\"track-credit-names\">")
              .Append(HtmlEscape(composer))
              .Append("</span></span>");
        }
        if (arranger.Length > 0)
        {
            sb.Append("<span class=\"track-credit-segment\">")
              .Append(BuildRoleBadgeSpan("ARRANGEMENT", "編曲"))
              .Append("<span class=\"track-credit-names\">")
              .Append(HtmlEscape(arranger))
              .Append("</span></span>");
        }
        return sb.ToString();
    }

    /// <summary>役職バッジ単体の HTML を組み立てる
    /// （<c>.role-badge.role-badge-sm[data-role-code]</c> 規約準拠）。</summary>
    private static string BuildRoleBadgeSpan(string roleCode, string label)
        => $"<span class=\"role-badge role-badge-sm\" data-role-code=\"{HtmlEscape(roleCode)}\">{HtmlEscape(label)}</span>";

    /// <summary>HTML エスケープ（テンプレに渡す前に Generator 側で済ませる用）。 既存呼出箇所からの利用継続のため本クラスにも残置。本体は <see cref="TrackCreditHtmlBuilder.Escape"/>。</summary>
    private static string HtmlEscape(string s) => TrackCreditHtmlBuilder.Escape(s);

    /// <summary>
    /// frames（1/75 秒単位）を「整数部 (m:ss) と小数部 (.ff)」に分離する。
    /// /stats/episodes/series-summary/ の平均尺表記（整数部 + micro-fraction の小数 2 桁）に揃える。
    /// NULL は両方空文字。端数が四捨五入で .100 に繰り上がる場合は秒へ繰り上げ（分桁も連動）。
    /// 例: 1607 frames → ("0:21", ".43")、NULL → ("", "")。
    /// </summary>
    private static (string IntegerPart, string FractionPart) SplitLength(uint? frames)
    {
        if (!frames.HasValue) return ("", "");
        double seconds = frames.Value / 75.0;
        int intSeconds = (int)Math.Floor(seconds);
        int frac2 = (int)Math.Round((seconds - intSeconds) * 100.0);
        // 端数が 100 に繰り上がったら 1 秒へ繰り上げる（.100 のような誤表記を防ぐ）。
        if (frac2 >= 100)
        {
            intSeconds += 1;
            frac2 = 0;
        }
        int min = intSeconds / 60;
        int sec = intSeconds % 60;
        return ($"{min}:{sec:D2}", "." + frac2.ToString("D2"));
    }

    /// <summary>
    /// 商品の総収録時間（ms 単位の double）を「m分ss秒」整数部 と「.cc」小数 2 桁 (centiseconds) に分離する。
    /// 商品詳細ページ基本情報の「収録時間」行で、トラック尺と同じ micro-fraction（淡色 + 小フォント）の
    /// 整数部 + 小数部の 2 段で表示するための整形。
    /// 入力は所属ディスクの <c>total_length_frames</c>（CD-DA、1/75 秒）と
    /// <c>total_length_ms</c>（BD/DVD、ミリ秒）を ms に揃えて合算した値。
    /// 入力が 0（=全ディスクで尺取得済みなしと同義）の場合は呼出側で空判定して本メソッドを呼ばない想定。
    /// 端数が四捨五入で .100 に繰り上がる場合は秒へ繰り上げ（分桁も連動）。
    /// </summary>
    private static (string Label, string Fraction) SplitTotalLengthMs(double totalMs)
    {
        int totalCs = (int)Math.Round(totalMs / 10.0);  // centiseconds
        int totalSec = totalCs / 100;
        int cs = totalCs % 100;
        // 端数が 100 に繰り上がる経路は Math.Round の挙動上発生し得ないが、念のため上限ガード。
        if (cs >= 100)
        {
            totalSec += 1;
            cs = 0;
        }
        int min = totalSec / 60;
        int sec = totalSec % 60;
        return ($"{min}分{sec:D2}秒", "." + cs.ToString("D2"));
    }

    // ─── テンプレ用 DTO 群 ───

    /// <summary>商品索引テンプレに渡すルートモデル。</summary>
    private sealed class ProductsIndexModel
    {
        /// <summary>ジャンル別（<c>product_kinds.display_order</c> 順）セクション。</summary>
        public IReadOnlyList<ProductIndexSection> KindSections { get; set; } = Array.Empty<ProductIndexSection>();
        /// <summary>シリーズ別（<c>Series.StartDate</c> 順 + 単一シリーズに紐付かない商品の <c>product_kinds.display_order</c> 順サブセクション）セクション。</summary>
        public IReadOnlyList<ProductIndexSection> SeriesSections { get; set; } = Array.Empty<ProductIndexSection>();
        /// <summary>商品の総件数（タブ問わず同一）。</summary>
        public int TotalCount { get; set; }
    }

    /// <summary>商品索引の 1 セクション。</summary>
    private sealed class ProductIndexSection
    {
        /// <summary>セクション見出しテキスト。シリーズセクションではシリーズタイトル、 単一シリーズに紐付かない商品の種別別サブセクションでは <c>product_kinds.name_ja</c>。 略記は使わず常に正式タイトルを入れる。</summary>
        public string Label { get; set; } = "";
        /// <summary>セクション見出しに張るリンク URL（シリーズ詳細ページ）。リンク不要なら空文字。</summary>
        public string SeriesLink { get; set; } = "";
        /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。シリーズ非紐付けセクションでは空文字。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>所属商品の行リスト（発売日昇順・代表品番昇順）。</summary>
        public IReadOnlyList<ProductIndexRow> Members { get; set; } = Array.Empty<ProductIndexRow>();
    }

    /// <summary>商品索引の 1 行。タブ問わず共通の表示 DTO。 <see cref="ReleaseDateRaw"/> は内部ソート専用（テンプレからは <see cref="ReleaseDate"/> や <see cref="ReleaseDateShort"/> を参照）。</summary>
    private sealed class ProductIndexRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>発売日の日本語フォーマット文字列「2004年2月18日」（旧テーブル表示の遺産、当面残す）。</summary>
        public string ReleaseDate { get; set; } = "";
        /// <summary>発売日の短縮形「2004.2.1」（年.月.日、月日はゼロパディングしない）。カード表示用。</summary>
        public string ReleaseDateShort { get; set; } = "";
        public byte DiscCount { get; set; }
        /// <summary>並べ替え専用の発売日（DateTime 原値）。テンプレからは参照しない。</summary>
        public DateTime ReleaseDateRaw { get; set; }
        /// <summary>
        /// 品番レンジ表記。1 枚商品なら <see cref="ProductCatalogNo"/> と同値、複数枚商品なら
        /// 筆頭・最終ディスクの <c>catalog_no</c> から共通 prefix + 差分サフィックスを取って
        /// 「MJCD-20019〜21」形式に整形済み。
        /// </summary>
        public string CatalogNoRange { get; set; } = "";
        /// <summary>税込価格の表示用ラベル（"税込 ¥3,300" 形式。「税込」プレフィックス付きで税抜価格との混同を防ぐ）。価格未設定なら空文字。</summary>
        public string PriceIncTaxLabel { get; set; } = "";
        /// <summary>枚数表示「{N}枚組」。1 枚商品では空文字（カード意匠を綺麗に保つため、出さない）。</summary>
        public string DiscCountLabel { get; set; } = "";
        /// <summary>商品種別の表示用ラベル（<c>product_kinds.name_ja</c>）。マスタ未登録時は生コード値。</summary>
        public string ProductKindLabel { get; set; } = "";
        /// <summary>
        /// 商品種別バッジの CSS クラス末尾（<c>product_kind_code</c> を <c>tolowerinvariant + _→-</c> 変換）。
        /// テンプレ側で <c>.products-card-badge.products-card-kind-{ここ}</c> として固定マッピングで着色する。
        /// マスタ未登録（コード空）時は空文字。
        /// </summary>
        public string BadgeClassSuffix { get; set; } = "";
        /// <summary>ジャケット画像 URL（Amazon CDN ホットリンク）。空ならカード左端のサムネ枠はグレーのプレースホルダ表示にする。</summary>
        public string CoverImageUrl { get; set; } = "";
    }

    /// <summary>シリーズ別タブで商品が割り振られるバケットの種類。</summary>
    private enum SeriesBucketKind
    {
        /// <summary>商品の全ディスクが同一の非 NULL <c>series_id</c> に紐付く。</summary>
        SingleSeries,
        /// <summary>商品のディスクで <c>series_id</c> が複数種類（NULL 混在を含む）に分かれる。</summary>
        MultiSeries,
        /// <summary>商品のディスクに 1 件も <c>series_id</c> 紐付けが無い、またはディスクが 0 件。</summary>
        Other
    }

    /// <summary>シリーズ別タブでのバケット分類結果。</summary>
    private readonly record struct SeriesBucket(SeriesBucketKind Kind, int? SeriesId);

    private sealed class ProductDetailModel
    {
        public ProductView Product { get; set; } = new();
        public IReadOnlyList<DiscView> Discs { get; set; } = Array.Empty<DiscView>();
    }

    /// <summary>商品詳細テンプレ用の表示 DTO。 レーベル・販売元は構造化 ID から解決した文字列のみを保持する （社名は構造化 ID で解決する）。</summary>
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
        /// <summary>
        /// 商品の総収録時間「m分ss秒」整数部。所属ディスクの <c>total_length_frames</c>（CD-DA、1/75 秒）と
        /// <c>total_length_ms</c>（BD/DVD、ミリ秒）を ms に揃えて合算してから整形した値。
        /// 全ディスクで尺取得済みが 1 件も無い商品は空文字 → テンプレ側で行ごと非表示。
        /// </summary>
        public string TotalLengthLabel { get; set; } = "";
        /// <summary>商品の総収録時間の小数部「.cc」（centiseconds、2 桁）。
        /// トラック尺の <c>LengthFraction</c> と同じ <c>.micro-fraction</c> 表記でテンプレに出す。</summary>
        public string TotalLengthFraction { get; set; } = "";
        /// <summary>JAN（= 所属ディスクの MCN。複数ディスクで共通の前提）。CD を含まない商品は空。</summary>
        public string Jan { get; set; } = "";
        // Amazon は物理（CD/BD/DVD）／デジタル（Amazon Music の MP3 アルバム）の 2 系統を持つ。
        // どちらか片方だけが登録されているケースも普通にあり得るため、空文字は「未登録」を意味する。
        public string AmazonAsinCd { get; set; } = "";
        public string AmazonAsinDigital { get; set; } = "";
        /// <summary>ジャケット画像 URL（代表。Amazon CDN ホットリンク。空なら画像ブロックを出さない）。</summary>
        public string CoverImageUrl { get; set; } = "";
        /// <summary>商品詳細で 2 枚並べるときの 2 枚目の URL（代表と異なる方）。 「両方表示」フラグ＋両 URL あり＋互いに異なる場合のみ非空。空なら詳細も 1 枚表示。</summary>
        public string CoverImageSecondaryUrl { get; set; } = "";
        /// <summary>ジャケット画像の取得元コード（<c>amazon_cd</c> / <c>amazon_digital</c>）。 商品詳細テンプレでジャケット画像直下の attribution 文言を分岐させるために使う。 未取得（CoverImageUrl も空）の場合は空文字。</summary>
        public string CoverImageSource { get; set; } = "";
        /// <summary>Amazon 商品リンク（物理パッケージ向け。アフィリエイトタグ付き。ASIN 未設定なら空）。</summary>
        public string AmazonCdUrl { get; set; } = "";
        /// <summary>Amazon 商品リンク（デジタル音源向け。アフィリエイトタグ付き。ASIN 未設定なら空）。</summary>
        public string AmazonDigitalUrl { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>音楽商品の公式ページ URL（任意）。詳細ページ末尾の「外部リンク」セクションでアイコン付きリンクとして出す。</summary>
        public string OfficialUrl { get; set; } = "";
    }

    /// <summary>
    /// ディスクセクションの h2 に出す見出し文字列を組み立てる。
    /// <list type="bullet">
    ///   <item>単一枚商品（<paramref name="discCount"/> ≤ 1）→ 固定文言「トラックリスト」</item>
    ///   <item>複数枚商品で <paramref name="discTitle"/> が <paramref name="productTitle"/> で始まる →
    ///     プレフィックス部分を取り除いて先頭空白を詰めた残りを採用</item>
    ///   <item>残りが空になる（ディスク名が商品名と完全一致／ディスク名未登録）→
    ///     <paramref name="discNoInSet"/> があれば「Disc {N}」、無ければ「ディスク」</item>
    /// </list>
    /// </summary>
    private static string BuildDiscHeading(int discCount, uint? discNoInSet, string productTitle, string discTitle)
    {
        if (discCount <= 1)
            return "トラックリスト";

        string diff = discTitle ?? "";
        if (!string.IsNullOrEmpty(productTitle)
            && !string.IsNullOrEmpty(diff)
            && diff.StartsWith(productTitle, StringComparison.Ordinal))
        {
            diff = diff.Substring(productTitle.Length).TrimStart();
        }

        if (string.IsNullOrEmpty(diff))
        {
            diff = discNoInSet is uint n ? $"Disc {n}" : "ディスク";
        }
        return diff;
    }

    private sealed class DiscView
    {
        public string CatalogNo { get; set; } = "";
        public uint? DiscNoInSet { get; set; }
        public string Title { get; set; } = "";
        /// <summary>
        /// ディスク見出し（h2）に出す文字列。
        /// 単一枚商品では <c>「トラックリスト」</c>（固定文言）。複数枚商品では <c>Title</c> から
        /// 商品名のプレフィックス部分を取り除いて先頭空白を詰めた残りを採用する。残りが空になる
        /// （ディスク名が商品名と完全一致／ディスク名未登録）場合は <c>「Disc {N}」</c>（連番のみ）にフォールバックする。
        /// </summary>
        public string Heading { get; set; } = "";
        public string MediaFormat { get; set; } = "";
        /// <summary>MCN（= JAN/EAN-13 バーコード相当の 13 桁数字）。CD 系のみ値を持つ。未取得は空。</summary>
        public string Mcn { get; set; } = "";
        public string DiscKindLabel { get; set; } = "";
        public string SeriesLink { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public IReadOnlyList<TrackRow> Tracks { get; set; } = Array.Empty<TrackRow>();
    }

    private sealed class TrackRow
    {
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        /// <summary>トラックの ISRC（12 文字英数字）。未取得は空。No. セルのツールチップに使用。</summary>
        public string Isrc { get; set; } = "";
        /// <summary>コンテンツ種別コード（SONG / BGM / DRAMA 等）。テンプレ側での細かい分岐用に保持するが、 表示分岐は Generator 側で完成 HTML に焼き込むため、テンプレでは原則使わない。</summary>
        public string ContentKindCode { get; set; } = "";
        public string ContentKindLabel { get; set; } = "";
        /// <summary>トラックタイトル（プレーン文字列、JSON-LD・meta description 等の構造化用途で使う）。</summary>
        public string Title { get; set; } = "";
        /// <summary>
        /// タイトル列の上段に出す HTML。
        /// 歌：variant_label / 曲名のいずれかを楽曲詳細ページへのリンクで包んだ HTML、
        /// 劇伴：メニュー表記の HTML（リンクなし）、
        /// その他：タイトル平文 HTML。
        /// </summary>
        public string TitleHtml { get; set; } = "";
        /// <summary>
        /// 歌トラックの「タイトル右に並ぶサイズ・パートのバッジ群」HTML。
        /// 楽曲詳細と同じ意匠（淡い緑＝サイズ、淡い青＝パート、VOCAL バッジは出さない）。
        /// 歌以外は空文字（テンプレ側で出ない）。
        /// </summary>
        public string KindBadgesHtml { get; set; } = "";
        /// <summary>
        /// タイトル列の下段に出すメタ行 HTML。
        /// 歌：作詞・作曲・編曲・歌の役職バッジ + 名義リンクを連結、
        /// 劇伴：Mナンバー + メニュー表記 + 役職バッジ + 名義リンク、
        /// その他：空（メタ行を出さない）。
        /// </summary>
        public string MetaLineHtml { get; set; } = "";
        /// <summary>
        /// SONG 録音が <c>song_recording_bgm_assignments</c> 経由で劇伴 cue にも紐付いている場合の
        /// 「シリーズ略記 + Mナンバー [メニュー]」表記 HTML（BGM 分岐と同じ書式）。
        /// テンプレ側で <see cref="MetaLineHtml"/>（歌の役職クレジット行）とは別の独立
        /// `<div class="track-meta-line">` として grid 兄弟に積むことで、強制的に改行された
        /// 「下の行」として表示される（<c>.track-meta-line</c> は flex コンテナのため
        /// 単一要素内での <c>&lt;br&gt;</c> 改行が効かないことへの対策）。
        /// 紐付けが無い場合・SONG 以外のトラックでは空文字。
        /// </summary>
        public string BgmAssignmentMetaLineHtml { get; set; } = "";
        /// <summary>
        /// NEXT トラックで「原曲: 元曲タイトル」を表示するための独立メタ行 HTML。
        /// テンプレ側で <see cref="MetaLineHtml"/>（作曲・編曲の役職行）の<b>上</b>に
        /// 別の <c>&lt;div class="track-meta-line"&gt;</c> として grid 兄弟に積むことで、
        /// 「タイトル(badges) → 原曲行 → 役職行」の縦並びを形成する。
        /// NEXT 以外のトラックや、recording 解決に失敗した NEXT トラックでは空文字。
        /// </summary>
        public string OriginalSongMetaLineHtml { get; set; } = "";
        public string LengthLabel { get; set; } = "";
        /// <summary>尺の小数部「.ff」（2 桁、micro-fraction 表記用）。尺なしは空。</summary>
        public string LengthFraction { get; set; } = "";
        /// <summary>SONG のときの楽曲詳細ページへのリンク（JSON-LD 構造化データから参照するため残す。テンプレ表示では <see cref="TitleHtml"/> 側に既に埋め込まれている）。</summary>
        public string SongLink { get; set; } = "";
        /// <summary>
        /// SONG トラックが <c>song_recording_bgm_assignments</c> 経由で劇伴 cue にも紐付いている場合に true。
        /// テンプレ側でトラックカード円バッジの装飾を SONG 赤 + BGM 緑の斜め分割塗りに切り替えるフラグ。
        /// SONG 以外のトラック（BGM / DRAMA / その他）では常に false。
        /// </summary>
        public bool HasBgmAssignments { get; set; }
    }
}
