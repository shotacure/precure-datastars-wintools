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
/// <para>
/// v1.1.1 より商品はシリーズ所属を持たない（series_id は <see cref="DiscsRepository"/> 側）。
/// シリーズ別の商品絞り込みが必要な場合は、ディスク側でシリーズ一致する商品代表品番の集合を
/// 集めて二段階でクエリする運用に変更する。
/// </para>
/// <para>
/// v1.1.3 より <see cref="GetAllAsync"/> の既定並び順を「発売日昇順・同日内は代表品番昇順」に
/// 変更した（データ入力時に時系列で埋めていきやすいため）。従来の降順並びが必要な照合系処理
/// （DiscMatchDialog など）向けに、旧順序の <see cref="GetAllDescAsync"/> を別途残している。
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

    // SELECT 列は SQL 側で一致させる。Title_* 等の null に注意。
    // v1.1.1: series_id は discs 側へ移設したため、本 SELECT には含めない。
    private const string SelectColumns = """
          product_catalog_no AS ProductCatalogNo,
          title              AS Title,
          title_short        AS TitleShort,
          title_en           AS TitleEn,
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
    /// 全商品を取得する（発売日昇順、同一日内は代表品番昇順）。
    /// v1.1.3 より既定の並び順を「時系列昇順（古い順）」に統一。
    /// 商品・ディスク管理フォームで過去から順に入力していく運用に合わせたもの。
    /// </summary>
    /// <param name="includeDeleted">true の場合、論理削除済みも含める。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<Product>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM products
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY release_date ASC, product_catalog_no ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Product>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 全商品を発売日降順（新しい順）で取得する。
    /// 新着優先で一覧したい照合系 UI（DiscMatchDialog の既存候補補助など）向け。
    /// v1.1.2 以前の <see cref="GetAllAsync"/> の挙動を保つための互換メソッド。
    /// </summary>
    public async Task<IReadOnlyList<Product>> GetAllDescAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM products
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY release_date DESC, product_catalog_no ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Product>(new CommandDefinition(sql, cancellationToken: ct));
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
    /// 並びは発売日降順（新着が先頭に来る方が照合の体感が良い）。
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
        // v1.1.1: series_id は商品から撤去済み。INSERT 列からも除外。
        const string sql = """
            INSERT INTO products
              (product_catalog_no,
               title, title_short, title_en, product_kind_code, release_date,
               price_ex_tax, price_inc_tax, disc_count,
               manufacturer, distributor, label,
               amazon_asin, apple_album_id, spotify_album_id,
               notes, created_by, updated_by)
            VALUES
              (@ProductCatalogNo,
               @Title, @TitleShort, @TitleEn, @ProductKindCode, @ReleaseDate,
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
        // v1.1.1: series_id は商品から撤去済み。UPDATE 列からも除外。
        const string sql = """
            UPDATE products SET
              title             = @Title,
              title_short       = @TitleShort,
              title_en          = @TitleEn,
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
