namespace PrecureDataStars.Data.Models;

/// <summary>
/// products テーブルに対応するエンティティモデル（PK: product_catalog_no）。
/// 商品（＝販売単位）を表す。価格・発売日・販売元などの「1 回の買い物で 1 つに決まる」情報は
/// すべて本テーブルに格納する。物理ディスクは discs テーブルに分離され、1 対多で紐づく。
/// 主キーは「代表品番」(<see cref="ProductCatalogNo"/>)。
/// 1 枚物は唯一のディスクの catalog_no、複数枚組は 1 枚目のディスクの catalog_no を採用する。
/// シリーズ所属 (series_id) は商品ではなく <see cref="Disc"/> 側に持つ。
/// 商品としては「どのシリーズに属するか」ではなく、構成ディスクのシリーズ集合で評価される
/// （実運用上はほぼ全ディスクが同じシリーズだが、シリーズ合同盤や特典で混在させられる）。
/// 商品の発売元（label）／販売元（distributor）は <see cref="LabelProductCompanyId"/> /
/// <see cref="DistributorProductCompanyId"/> による <c>product_companies</c> への
/// 構造化 ID 紐付けで表現する。新規商品作成時の既定社は
/// <see cref="ProductCompany.IsDefaultLabel"/> / <see cref="ProductCompany.IsDefaultDistributor"/>
/// フラグで指定する。
/// </summary>
public sealed class Product
{
    // ── 主キー ──

    /// <summary>商品の主キー（代表品番。1 枚目ディスクの catalog_no）。</summary>
    public string ProductCatalogNo { get; set; } = "";

    // ── 表題 ──

    /// <summary>商品タイトル（販売名）。</summary>
    public string Title { get; set; } = "";

    /// <summary>商品タイトルの略称（任意）。</summary>
    public string? TitleShort { get; set; }

    /// <summary>英語タイトル（任意）。</summary>
    public string? TitleEn { get; set; }

    // ── 関連 ──

    /// <summary>商品種別コード（→ product_kinds）。</summary>
    public string ProductKindCode { get; set; } = "";

    // ── 販売情報 ──

    /// <summary>発売日。</summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>税抜価格（円）。</summary>
    public int? PriceExTax { get; set; }

    /// <summary>税込価格（円）。</summary>
    public int? PriceIncTax { get; set; }

    /// <summary>セット内のディスク枚数（1 以上）。</summary>
    public byte DiscCount { get; set; } = 1;

    /// <summary>レーベルとして紐付ける社名マスタ（→ product_companies）の ID。 表示時は <see cref="ProductCompany.NameJa"/> を引いて使う。</summary>
    public int? LabelProductCompanyId { get; set; }

    /// <summary>販売元として紐付ける社名マスタ（→ product_companies）の ID。</summary>
    public int? DistributorProductCompanyId { get; set; }

    // ── 外部プラットフォーム ID ──
    // ASIN は同一商品でも物理パッケージ（CD/BD/DVD）とデジタル音源（Amazon Music の MP3 アルバム）で
    // 別の値が割り当てられるため、列を分けて両方を保持する。商品詳細ページでは双方をそれぞれの
    // 「Amazon (CD)」「Amazon (デジタル)」リンクとして並列表示する。

    /// <summary>Amazon の物理パッケージ商品 ASIN（CD/BD/DVD など物理メディア向け）。</summary>
    public string? AmazonAsinCd { get; set; }

    /// <summary>Amazon のデジタル音源商品 ASIN（Amazon Music の MP3 アルバム向け）。</summary>
    public string? AmazonAsinDigital { get; set; }

    /// <summary>Apple Music のアルバム ID。</summary>
    public string? AppleAlbumId { get; set; }

    /// <summary>Spotify のアルバム ID。</summary>
    public string? SpotifyAlbumId { get; set; }

    // ── ジャケット画像キャッシュ ──

    /// <summary>ジャケット画像の URL（提供元 CDN を直接参照するホットリンク運用。画像実体は保存しない）。 PA-API 由来の場合は <c>m.media-amazon.com</c> 系の URL、iTunes Lookup 由来の場合は Apple CDN URL を保持する。未取得は NULL。</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>ジャケット画像の取得元コード。取り得る値は <c>amazon_cd</c>（PA-API・CD ASIN から取得）／<c>amazon_digital</c>（PA-API・デジタル ASIN から取得）／<c>apple</c>（iTunes Lookup 由来）。未取得は NULL。</summary>
    public string? CoverImageSource { get; set; }

    /// <summary>ジャケット画像 URL の取得日時。再取得（鮮度判定）に使う。未取得は NULL。</summary>
    public DateTime? CoverImageFetchedAt { get; set; }

    // ── 備考 ──

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    /// <summary>作成日時。</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>更新日時。</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>作成ユーザー。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>更新ユーザー。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}
