namespace PrecureDataStars.Data.Models;

/// <summary>
/// 「販売中の商品」の枠の候補になる音楽商品 1 件（作品 × 商品）。
/// 作品に結び付く盤（<c>discs.series_id</c>）を持ち、CD の ASIN がある商品を、作品ごとに 1 行で表す
/// （複数の作品の盤を持つ商品は、作品ごとに 1 行ずつ出る）。販売の状態は <c>product_shop_states</c> から引く。
/// </summary>
public sealed class ShopProductCandidate
{
    /// <summary>作品 ID（盤の <c>series_id</c>）。</summary>
    public int SeriesId { get; set; }

    /// <summary>商品の代表品番。</summary>
    public string ProductCatalogNo { get; set; } = "";

    /// <summary>商品名。</summary>
    public string Title { get; set; } = "";

    /// <summary>商品の種別（<c>product_kinds.kind_code</c>。ボーカルベスト・サントラなどの見分けに使う）。</summary>
    public string KindCode { get; set; } = "";

    /// <summary>発売日。</summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>CD の ASIN。</summary>
    public string Asin { get; set; } = "";

    /// <summary>ジャケット画像の URL（商品の代表の画像。無ければ null）。</summary>
    public string? ImageUrl { get; set; }

    /// <summary>売られているか（true＝候補にする・false＝しない・null＝まだ確かめていない）。</summary>
    public bool? IsBuyable { get; set; }

    /// <summary>Amazon が返した在庫の種別（IN_STOCK・PREORDER など）。</summary>
    public string? AvailabilityType { get; set; }
}
