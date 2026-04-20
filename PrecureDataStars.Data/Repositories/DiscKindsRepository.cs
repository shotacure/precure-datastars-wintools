using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// disc_kinds テーブル（ディスク用途種別マスタ）の読み取りリポジトリ。
/// </summary>
public sealed class DiscKindsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="DiscKindsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public DiscKindsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// disc_kinds を全件取得する（display_order 昇順）。
    /// </summary>
    public async Task<IReadOnlyList<DiscKind>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              kind_code     AS KindCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM disc_kinds
            ORDER BY COALESCE(display_order, 255), kind_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<DiscKind>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>UPSERT（MastersEditor から利用）。</summary>
    public async Task UpsertAsync(DiscKind kind, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO disc_kinds (kind_code, name_ja, name_en, display_order, created_by, updated_by)
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
        const string sql = "DELETE FROM disc_kinds WHERE kind_code = @KindCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { KindCode = kindCode }, cancellationToken: ct));
    }
}