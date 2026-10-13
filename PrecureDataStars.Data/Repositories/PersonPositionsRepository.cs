using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>person_positions テーブル（人物の役職履歴）の CRUD リポジトリ。行は物理削除する。</summary>
public sealed class PersonPositionsRepository : RepositoryBase
{
    public PersonPositionsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          position_id     AS PositionId,
          person_id       AS PersonId,
          company_id      AS CompanyId,
          display_order   AS DisplayOrder,
          effective_date  AS EffectiveDate,
          position_text   AS PositionText,
          source_url      AS SourceUrl,
          notes           AS Notes,
          created_at      AS CreatedAt,
          updated_at      AS UpdatedAt,
          created_by      AS CreatedBy,
          updated_by      AS UpdatedBy
        """;

    /// <summary>並び順の ORDER BY（並び順 → 異動日（不明は後ろ）→ position_id）。</summary>
    private const string OrderBy = "ORDER BY person_id, display_order, effective_date IS NULL, effective_date, position_id";

    /// <summary>全件取得（人物 → 並び順）。</summary>
    public async Task<IReadOnlyList<PersonPosition>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_positions
            {OrderBy};
            """;

        return await QueryListAsync<PersonPosition>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定の人物の役職履歴を並び順で取得する。</summary>
    public async Task<IReadOnlyList<PersonPosition>> GetByPersonAsync(int personId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_positions
            WHERE person_id = @personId
            {OrderBy};
            """;

        return await QueryListAsync<PersonPosition>(sql, new { personId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。AUTO_INCREMENT の position_id を返す。</summary>
    public async Task<int> InsertAsync(PersonPosition position, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO person_positions
              (person_id, company_id, display_order, effective_date, position_text, source_url, notes, created_by, updated_by)
            VALUES
              (@PersonId, @CompanyId, @DisplayOrder, @EffectiveDate, @PositionText, @SourceUrl, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        return await ExecuteScalarAsync<int>(sql, position, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(PersonPosition position, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE person_positions SET
              company_id      = @CompanyId,
              display_order   = @DisplayOrder,
              effective_date  = @EffectiveDate,
              position_text   = @PositionText,
              source_url      = @SourceUrl,
              notes           = @Notes,
              updated_by      = @UpdatedBy
            WHERE position_id = @PositionId;
            """;

        await ExecuteAsync(sql, position, ct).ConfigureAwait(false);
    }

    /// <summary>物理削除。</summary>
    public async Task DeleteAsync(int positionId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM person_positions WHERE position_id = @positionId;";
        await ExecuteAsync(sql, new { positionId }, ct).ConfigureAwait(false);
    }
}
