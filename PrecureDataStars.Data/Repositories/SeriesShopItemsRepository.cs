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

    /// <summary>
    /// 枠の候補になる音楽商品を、作品 × 商品の行で取得する（作品 ID → 発売日 → 品番の順）。
    /// 作品に結び付く盤を持ち、CD の ASIN がある商品が対象。販売の状態（<c>product_shop_states</c>）は、
    /// まだ確かめていない商品では null になる。画像は商品の代表の画像（採用ソースの側、無ければもう片方）。
    /// </summary>
    public async Task<IReadOnlyList<ShopProductCandidate>> GetProductCandidatesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT
                   d.series_id            AS SeriesId,
                   p.product_catalog_no   AS ProductCatalogNo,
                   p.title                AS Title,
                   p.product_kind_code    AS KindCode,
                   p.release_date         AS ReleaseDate,
                   p.amazon_asin_cd       AS Asin,
                   CASE p.cover_image_source
                     WHEN 'amazon_cd' THEN COALESCE(p.cover_image_url_cd, p.cover_image_url_digital)
                     ELSE COALESCE(p.cover_image_url_digital, p.cover_image_url_cd)
                   END                    AS ImageUrl,
                   s.is_buyable           AS IsBuyable,
                   s.availability_type    AS AvailabilityType
              FROM products p
              JOIN discs d ON d.product_catalog_no = p.product_catalog_no
              LEFT JOIN product_shop_states s ON s.product_catalog_no = p.product_catalog_no
             WHERE p.is_deleted = 0
               AND p.amazon_asin_cd IS NOT NULL AND p.amazon_asin_cd <> ''
               AND d.series_id IS NOT NULL
             ORDER BY d.series_id, p.release_date, p.product_catalog_no;
            """;

        return await QueryListAsync<ShopProductCandidate>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 音楽商品の販売の状態を書く（行が無ければ作り、あれば上書きする）。同じ CD の ASIN を持つ商品すべてに書く。
    /// </summary>
    /// <param name="asin">確かめた CD の ASIN。</param>
    /// <param name="isBuyable">売られているか。</param>
    /// <param name="availabilityType">Amazon が返した在庫の種別。出品が無いときは null。</param>
    /// <param name="checkedAt">確かめた時刻。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task UpsertProductStateAsync(
        string asin, bool isBuyable, string? availabilityType, DateTime checkedAt, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO product_shop_states (product_catalog_no, is_buyable, availability_type, checked_at)
            SELECT p.product_catalog_no, @isBuyable, @availabilityType, @checkedAt
              FROM products p
             WHERE p.amazon_asin_cd = @asin AND p.is_deleted = 0
            ON DUPLICATE KEY UPDATE
                   is_buyable        = VALUES(is_buyable),
                   availability_type = VALUES(availability_type),
                   checked_at        = VALUES(checked_at);
            """;

        await ExecuteAsync(sql, new { asin, isBuyable, availabilityType, checkedAt }, ct).ConfigureAwait(false);
    }
}
