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

    // ── ジャケット画像キャッシュ ──

    /// <summary>CD ASIN から取得したジャケット画像 URL（Amazon CDN ホットリンク、画像実体は保存しない）。未取得は NULL。 CD とデジタルでジャケットが異なる場合があるため両系統を保持し、表示に使う方は <see cref="CoverImageSource"/> で選ぶ。</summary>
    public string? CoverImageUrlCd { get; set; }

    /// <summary>デジタル ASIN から取得したジャケット画像 URL（Amazon CDN ホットリンク、画像実体は保存しない）。未取得は NULL。</summary>
    public string? CoverImageUrlDigital { get; set; }

    /// <summary>ジャケット画像の取得元コード＝表示に採用するソース（代表）の明示選択。 取り得る値は <c>amazon_cd</c>（<see cref="CoverImageUrlCd"/> を使う）／<c>amazon_digital</c>（<see cref="CoverImageUrlDigital"/> を使う）。未選択は NULL。 一覧・ホーム・収録盤サムネは常にこの代表 1 枚を使う。</summary>
    public string? CoverImageSource { get; set; }

    /// <summary>商品詳細ページで CD・デジタル両方のジャケットを並べて表示するか（true=両方 / false=代表 1 枚）。 両 URL が揃っていて互いに異なる場合のみ「両方」が実際に効く（同一／片方だけなら 1 枚）。 一覧・ホーム・収録盤サムネには影響しない（常に代表 1 枚）。</summary>
    public bool CoverImageShowBoth { get; set; }

    /// <summary>表示に使う実効ジャケット画像 URL。<see cref="CoverImageSource"/> が指すソースの URL を返す計算プロパティ（DB 列ではない）。 採用ソースが未選択／該当 URL が無い場合は、選択を尊重しつつデジタル→CD の順でフォールバックする（どちらも無ければ null）。</summary>
    public string? CoverImageUrl =>
        CoverImageSource switch
        {
            "amazon_cd" => CoverImageUrlCd ?? CoverImageUrlDigital,
            "amazon_digital" => CoverImageUrlDigital ?? CoverImageUrlCd,
            _ => CoverImageUrlDigital ?? CoverImageUrlCd,
        };

    /// <summary>ジャケット画像 URL の取得日時。再取得（鮮度判定）に使う。未取得は NULL。</summary>
    public DateTime? CoverImageFetchedAt { get; set; }

    // ── 備考 ──

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 外部リンク（詳細ページの末尾「外部リンク」セクションに出る） ──

    /// <summary>音楽商品の公式ページ URL（任意）。詳細ページにアイコン付きで表示。</summary>
    public string? OfficialUrl { get; set; }

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
