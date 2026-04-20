using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// product_kinds テーブル（商品種別マスタ）の読み取りリポジトリ。
/// </summary>
public sealed class ProductKindsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="ProductKindsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public ProductKindsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// product_kinds を全件取得する（display_order 昇順 → kind_code 昇順）。
    /// display_order が NULL の場合は 255 として末尾に配置される。
    /// </summary>
    public async Task<IReadOnlyList<ProductKind>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              kind_code     AS KindCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM product_kinds
            ORDER BY COALESCE(display_order, 255), kind_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<ProductKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// product_kinds マスタを UPSERT する（MastersEditor から利用）。
    /// </summary>
    public async Task UpsertAsync(ProductKind kind, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO product_kinds (kind_code, name_ja, name_en, display_order, created_by, updated_by)
            VALUES (@KindCode, @NameJa, @NameEn, @DisplayOrder, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja = VALUES(name_ja),
              name_en = VALUES(name_en),
              display_order = VALUES(display_order),
              updated_by = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, kind, cancellationToken: ct));
    }

    /// <summary>指定コードのマスタを削除する。</summary>
    public async Task DeleteAsync(string kindCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM product_kinds WHERE kind_code = @KindCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { KindCode = kindCode }, cancellationToken: ct));
    }
}