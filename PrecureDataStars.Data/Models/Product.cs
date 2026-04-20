namespace PrecureDataStars.Data.Models;

/// <summary>
/// products テーブルに対応するエンティティモデル（PK: product_catalog_no）。
/// <para>
/// 商品（＝販売単位）を表す。価格・発売日・販売元などの「1 回の買い物で 1 つに決まる」情報は
/// すべて本テーブルに格納する。物理ディスクは discs テーブルに分離され、1 対多で紐づく。
/// </para>
/// <para>
/// 主キーは「代表品番」(<see cref="ProductCatalogNo"/>)。
/// 1 枚物は唯一のディスクの catalog_no、複数枚組は 1 枚目のディスクの catalog_no を採用する。
/// </para>
/// <remarks>
/// v1.1.1 よりシリーズ所属 (series_id) は <see cref="Disc"/> 側の属性に移設した。
/// 商品としては「どのシリーズに属するか」ではなく、構成ディスクのシリーズ集合で評価される
/// （実運用上はほぼ全ディスクが同じシリーズだが、シリーズ合同盤や特典で混在させられる）。
/// </remarks>
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

    /// <summary>発売元（レコード会社等）。</summary>
    public string? Manufacturer { get; set; }

    /// <summary>販売元（流通会社）。</summary>
    public string? Distributor { get; set; }

    /// <summary>レーベル名。</summary>
    public string? Label { get; set; }

    // ── 外部プラットフォーム ID ──

    /// <summary>Amazon 商品 ASIN。</summary>
    public string? AmazonAsin { get; set; }

    /// <summary>Apple Music のアルバム ID。</summary>
    public string? AppleAlbumId { get; set; }

    /// <summary>Spotify のアルバム ID。</summary>
    public string? SpotifyAlbumId { get; set; }

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
