using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// products テーブル（商品）の CRUD リポジトリ。
/// <para>
/// 商品（販売単位）の一覧取得・検索・追加・更新・論理削除を提供する。
/// 主キーは代表品番（product_catalog_no）。1 枚物は唯一のディスクの品番、
/// 複数枚組は 1 枚目のディスクの品番を採用する。
/// is_deleted=1 の行は既定で除外される。
/// </para>
/// </summary>
public sealed class ProductsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="ProductsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public ProductsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // SELECT 列は SQL 側で一致させる。Title_* 等の null と SeriesId null に注意。
    private const string SelectColumns = """
          product_catalog_no AS ProductCatalogNo,
          title              AS Title,
          title_short        AS TitleShort,
          title_en           AS TitleEn,
          series_id          AS SeriesId,
          product_kind_code  AS ProductKindCode,
          release_date       AS ReleaseDate,
          price_ex_tax       AS PriceExTax,
          price_inc_tax      AS PriceIncTax,
          disc_count         AS DiscCount,
          manufacturer       AS Manufacturer,
          distributor        AS Distributor,
          label              AS Label,
          amazon_asin        AS AmazonAsin,
          apple_album_id     AS AppleAlbumId,
          spotify_album_id   AS SpotifyAlbumId,
          notes              AS Notes,
          created_at         AS CreatedAt,
          updated_at         AS UpdatedAt,
          created_by         AS CreatedBy,
          updated_by         AS UpdatedBy,
          is_deleted         AS IsDeleted
        """;

    /// <summary>
    /// 全商品を取得する（発売日降順、同一日内は代表品番昇順）。
    /// </summary>
    /// <param name="includeDeleted">true の場合、論理削除済みも含める。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<Product>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        // 論理削除フィルタを動的に切替
        string sql = $"""
            SELECT {SelectColumns}
            FROM products
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY release_date DESC, product_catalog_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Product>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// シリーズ ID で商品を絞り込んで取得する（NULL 指定でオールスターズ商品のみ）。
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetBySeriesAsync(int? seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM products
            WHERE is_deleted = 0
              AND {(seriesId.HasValue ? "series_id = @seriesId" : "series_id IS NULL")}
            ORDER BY release_date DESC, product_catalog_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Product>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>代表品番で 1 件取得する。</summary>
    public async Task<Product?> GetByCatalogNoAsync(string productCatalogNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM products
            WHERE product_catalog_no = @productCatalogNo
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Product>(
            new CommandDefinition(sql, new { productCatalogNo }, cancellationToken: ct));
    }

    /// <summary>
    /// タイトル部分一致で検索（DiscMatchDialog の手動検索から利用）。
    /// </summary>
    public async Task<IReadOnlyList<Product>> SearchByTitleAsync(string keyword, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM products
            WHERE is_deleted = 0
              AND (title LIKE @kw OR title_short LIKE @kw OR title_en LIKE @kw)
            ORDER BY release_date DESC, product_catalog_no
            LIMIT 200;
            """;

        var param = new { kw = $"%{keyword}%" };
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Product>(new CommandDefinition(sql, param, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 商品を新規作成する。product_catalog_no（代表品番）は呼び出し側で設定しておく必要がある。
    /// </summary>
    public async Task InsertAsync(Product product, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO products
              (product_catalog_no,
               title, title_short, title_en, series_id, product_kind_code, release_date,
               price_ex_tax, price_inc_tax, disc_count,
               manufacturer, distributor, label,
               amazon_asin, apple_album_id, spotify_album_id,
               notes, created_by, updated_by)
            VALUES
              (@ProductCatalogNo,
               @Title, @TitleShort, @TitleEn, @SeriesId, @ProductKindCode, @ReleaseDate,
               @PriceExTax, @PriceIncTax, @DiscCount,
               @Manufacturer, @Distributor, @Label,
               @AmazonAsin, @AppleAlbumId, @SpotifyAlbumId,
               @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, product, cancellationToken: ct));
    }

    /// <summary>商品情報を更新する（product_catalog_no で UPDATE）。</summary>
    public async Task UpdateAsync(Product product, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE products SET
              title             = @Title,
              title_short       = @TitleShort,
              title_en          = @TitleEn,
              series_id         = @SeriesId,
              product_kind_code = @ProductKindCode,
              release_date      = @ReleaseDate,
              price_ex_tax      = @PriceExTax,
              price_inc_tax     = @PriceIncTax,
              disc_count        = @DiscCount,
              manufacturer      = @Manufacturer,
              distributor       = @Distributor,
              label             = @Label,
              amazon_asin       = @AmazonAsin,
              apple_album_id    = @AppleAlbumId,
              spotify_album_id  = @SpotifyAlbumId,
              notes             = @Notes,
              updated_by        = @UpdatedBy,
              is_deleted        = @IsDeleted
            WHERE product_catalog_no = @ProductCatalogNo;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, product, cancellationToken: ct));
    }

    /// <summary>論理削除（is_deleted=1）。</summary>
    public async Task SoftDeleteAsync(string productCatalogNo, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE products SET is_deleted = 1, updated_by = @UpdatedBy WHERE product_catalog_no = @ProductCatalogNo;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { ProductCatalogNo = productCatalogNo, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
