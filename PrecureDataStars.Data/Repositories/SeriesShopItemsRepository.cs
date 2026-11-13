using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_shop_items テーブル（作品ごとの販売中の商品）のリポジトリ。
/// 行そのもの（作品・ASIN・商品名など）は SQL で入れる。ここが持つ書き込みは、
/// Amazon に確かめた結果の書き戻し（<see cref="UpdateCheckResultAsync"/>）だけ。
/// </summary>
public sealed class SeriesShopItemsRepository : RepositoryBase
{
    /// <summary><see cref="SeriesShopItemsRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesShopItemsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          shop_item_id        AS ShopItemId,
          series_id           AS SeriesId,
          asin                AS Asin,
          title               AS Title,
          media_label         AS MediaLabel,
          product_catalog_no  AS ProductCatalogNo,
          release_date        AS ReleaseDate,
          display_order       AS DisplayOrder,
          is_enabled          AS IsEnabled,
          is_buyable          AS IsBuyable,
          availability_type   AS AvailabilityType,
          image_url           AS ImageUrl,
          checked_at          AS CheckedAt,
          created_at          AS CreatedAt,
          updated_at          AS UpdatedAt,
          created_by          AS CreatedBy,
          updated_by          AS UpdatedBy
        """;

    /// <summary>出す設定の行（is_enabled = 1）を、並び順 → 行の ID の順に取得する。</summary>
    public async Task<IReadOnlyList<SeriesShopItem>> GetEnabledAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_shop_items
            WHERE is_enabled = 1
            ORDER BY display_order, shop_item_id;
            """;

        return await QueryListAsync<SeriesShopItem>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Amazon に確かめた結果を、同じ ASIN の行すべてに書き戻す（同じ商品を複数の作品に載せていても 1 回で済む）。
    /// </summary>
    /// <param name="asin">確かめた ASIN。</param>
    /// <param name="isBuyable">売られているか。</param>
    /// <param name="availabilityType">Amazon が返した在庫の種別。出品が無いときは null。</param>
    /// <param name="imageUrl">商品画像の URL。返らなかったときは null（保存していた URL も消す）。</param>
    /// <param name="checkedAt">確かめた時刻。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task UpdateCheckResultAsync(
        string asin, bool isBuyable, string? availabilityType, string? imageUrl, DateTime checkedAt, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE series_shop_items
               SET is_buyable        = @isBuyable,
                   availability_type = @availabilityType,
                   image_url         = @imageUrl,
                   checked_at        = @checkedAt,
                   updated_by        = 'amazon_sync'
             WHERE asin = @asin;
            """;

        await ExecuteAsync(sql, new { asin, isBuyable, availabilityType, imageUrl, checkedAt }, ct).ConfigureAwait(false);
    }
}
