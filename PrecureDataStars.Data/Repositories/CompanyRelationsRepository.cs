using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>company_relations テーブル（企業・団体どうしの関係）の CRUD リポジトリ。行は物理削除する。</summary>
public sealed class CompanyRelationsRepository : RepositoryBase
{
    public CompanyRelationsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          relation_id      AS RelationId,
          from_company_id  AS FromCompanyId,
          to_company_id    AS ToCompanyId,
          relation_kind    AS RelationKind,
          relation_label   AS RelationLabel,
          valid_from       AS ValidFrom,
          valid_to         AS ValidTo,
          notes            AS Notes,
          created_at       AS CreatedAt,
          updated_at       AS UpdatedAt,
          created_by       AS CreatedBy,
          updated_by       AS UpdatedBy
        """;

    /// <summary>全件取得（relation_id 昇順）。</summary>
    public async Task<IReadOnlyList<CompanyRelation>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_relations
            ORDER BY relation_id;
            """;

        return await QueryListAsync<CompanyRelation>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定の団体が起点・終点のどちらかになっている関係を取得する（開始日 → relation_id 順）。</summary>
    public async Task<IReadOnlyList<CompanyRelation>> GetByCompanyAsync(int companyId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_relations
            WHERE from_company_id = @companyId OR to_company_id = @companyId
            ORDER BY valid_from IS NULL, valid_from, relation_id;
            """;

        return await QueryListAsync<CompanyRelation>(sql, new { companyId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。AUTO_INCREMENT の relation_id を返す。</summary>
    public async Task<int> InsertAsync(CompanyRelation relation, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO company_relations
              (from_company_id, to_company_id, relation_kind, relation_label,
               valid_from, valid_to, notes, created_by, updated_by)
            VALUES
              (@FromCompanyId, @ToCompanyId, @RelationKind, @RelationLabel,
               @ValidFrom, @ValidTo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        return await ExecuteScalarAsync<int>(sql, relation, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CompanyRelation relation, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE company_relations SET
              from_company_id = @FromCompanyId,
              to_company_id   = @ToCompanyId,
              relation_kind   = @RelationKind,
              relation_label  = @RelationLabel,
              valid_from      = @ValidFrom,
              valid_to        = @ValidTo,
              notes           = @Notes,
              updated_by      = @UpdatedBy
            WHERE relation_id = @RelationId;
            """;

        await ExecuteAsync(sql, relation, ct).ConfigureAwait(false);
    }

    /// <summary>物理削除。</summary>
    public async Task DeleteAsync(int relationId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM company_relations WHERE relation_id = @relationId;";
        await ExecuteAsync(sql, new { relationId }, ct).ConfigureAwait(false);
    }
}
