using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>credit_kinds テーブル（クレジット種別マスタ）の CRUD リポジトリ。</summary>
public sealed class CreditKindsRepository : RepositoryBase
{
    public CreditKindsRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>credit_kinds を全件取得する（display_order 昇順、tie breaker は kind_code）。</summary>
    public async Task<IReadOnlyList<CreditKind>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              kind_code     AS KindCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              notes         AS Notes,
              created_at    AS CreatedAt,
              updated_at    AS UpdatedAt,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM credit_kinds
            ORDER BY display_order, kind_code;
            """;

        return await QueryListAsync<CreditKind>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>kind_code で 1 件取得（無ければ null）。</summary>
    public async Task<CreditKind?> GetByCodeAsync(string kindCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              kind_code     AS KindCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              notes         AS Notes,
              created_at    AS CreatedAt,
              updated_at    AS UpdatedAt,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM credit_kinds
            WHERE kind_code = @kindCode;
            """;
        return await QuerySingleOrDefaultAsync<CreditKind>(sql, new { kindCode }, ct).ConfigureAwait(false);
    }

    /// <summary>UPSERT（kind_code 衝突時は他カラムを更新）。</summary>
    public async Task UpsertAsync(CreditKind k, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_kinds
              (kind_code, name_ja, name_en, display_order, notes, created_by, updated_by)
            VALUES
              (@KindCode, @NameJa, @NameEn, @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja       = VALUES(name_ja),
              name_en       = VALUES(name_en),
              display_order = VALUES(display_order),
              notes         = VALUES(notes),
              updated_by    = VALUES(updated_by);
            """;
        await ExecuteAsync(sql, k, ct).ConfigureAwait(false);
    }

    /// <summary>削除（FK 参照されている場合は失敗、credits.credit_kind の RESTRICT で防御）。</summary>
    public async Task DeleteAsync(string kindCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_kinds WHERE kind_code = @kindCode;";
        await ExecuteAsync(sql, new { kindCode }, ct).ConfigureAwait(false);
    }
}
