namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_shop_items テーブルに対応するエンティティモデル（PK: shop_item_id）。
/// 作品ごとの「販売中の商品」。サイトは、その作品を主とするページ（作品詳細・各話の詳細）とトップページに、
/// <see cref="IsEnabled"/> が立っていて <see cref="IsBuyable"/> が true の行だけを小さな枠で出す。
/// <see cref="IsBuyable"/>・<see cref="AvailabilityType"/>・<see cref="ImageUrl"/>・<see cref="CheckedAt"/> は
/// AmazonSync（--shop-check）が Creators API で確かめて書き戻す。
/// </summary>
public sealed class SeriesShopItem
{
    /// <summary>行の ID（PK）。</summary>
    public int ShopItemId { get; set; }

    /// <summary>作品 ID（→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>Amazon の ASIN。</summary>
    public string Asin { get; set; } = "";

    /// <summary>枠に出す商品名。</summary>
    public string Title { get; set; } = "";

    /// <summary>媒体の札（Blu-ray・CD など）。</summary>
    public string MediaLabel { get; set; } = "";

    /// <summary>音楽商品なら、その商品の代表品番（→ products.product_catalog_no）。商品詳細へのリンクに使う。</summary>
    public string? ProductCatalogNo { get; set; }

    /// <summary>発売日。予約受付中の行に添える。</summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>並び順（小さいほど先）。</summary>
    public ushort DisplayOrder { get; set; }

    /// <summary>false にすると、売られていても出さない。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>売られているか（true＝出す・false＝出さない・null＝まだ確かめていない）。</summary>
    public bool? IsBuyable { get; set; }

    /// <summary>Amazon が返した在庫の種別（IN_STOCK・PREORDER など）。</summary>
    public string? AvailabilityType { get; set; }

    /// <summary>商品画像の URL（Amazon のもの。確かめるたびに取り直す）。</summary>
    public string? ImageUrl { get; set; }

    /// <summary>最後に確かめた時刻。</summary>
    public DateTime? CheckedAt { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
