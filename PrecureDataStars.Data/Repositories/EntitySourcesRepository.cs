using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>entity_sources テーブル（人物・企業のプロフィールの出典）の CRUD リポジトリ。行は物理削除する。supports（SET 型）は文字列のまま読み書きする。</summary>
public sealed class EntitySourcesRepository : RepositoryBase
{
    public EntitySourcesRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          source_id      AS SourceId,
          person_id      AS PersonId,
          company_id     AS CompanyId,
          display_order  AS DisplayOrder,
          supports       AS Supports,
          page_title     AS PageTitle,
          section_title  AS SectionTitle,
          publisher      AS Publisher,
          url            AS Url,
          checked_on     AS CheckedOn,
          notes          AS Notes,
          created_at     AS CreatedAt,
          updated_at     AS UpdatedAt,
          created_by     AS CreatedBy,
          updated_by     AS UpdatedBy
        """;

    /// <summary>並び順の ORDER BY（並び順 → source_id）。</summary>
    private const string OrderBy = "ORDER BY display_order, source_id";

    /// <summary>全件取得（並び順）。サイトの生成で人物・企業ごとに振り分けて使う。</summary>
    public async Task<IReadOnlyList<EntitySource>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM entity_sources
            {OrderBy};
            """;

        return await QueryListAsync<EntitySource>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定の人物の出典を並び順で取得する。</summary>
    public async Task<IReadOnlyList<EntitySource>> GetByPersonAsync(int personId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM entity_sources
            WHERE person_id = @personId
            {OrderBy};
            """;

        return await QueryListAsync<EntitySource>(sql, new { personId }, ct).ConfigureAwait(false);
    }

    /// <summary>指定の企業の出典を並び順で取得する。</summary>
    public async Task<IReadOnlyList<EntitySource>> GetByCompanyAsync(int companyId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM entity_sources
            WHERE company_id = @companyId
            {OrderBy};
            """;

        return await QueryListAsync<EntitySource>(sql, new { companyId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。AUTO_INCREMENT の source_id を返す。</summary>
    public async Task<int> InsertAsync(EntitySource source, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO entity_sources
              (person_id, company_id, display_order, supports, page_title, section_title, publisher,
               url, checked_on, notes, created_by, updated_by)
            VALUES
              (@PersonId, @CompanyId, @DisplayOrder, @Supports, @PageTitle, @SectionTitle, @Publisher,
               @Url, @CheckedOn, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        return await ExecuteScalarAsync<int>(sql, source, ct).ConfigureAwait(false);
    }

    /// <summary>更新（対象の人物・企業は変えない）。</summary>
    public async Task UpdateAsync(EntitySource source, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE entity_sources SET
              display_order  = @DisplayOrder,
              supports       = @Supports,
              page_title     = @PageTitle,
              section_title  = @SectionTitle,
              publisher      = @Publisher,
              url            = @Url,
              checked_on     = @CheckedOn,
              notes          = @Notes,
              updated_by     = @UpdatedBy
            WHERE source_id = @SourceId;
            """;

        await ExecuteAsync(sql, source, ct).ConfigureAwait(false);
    }

    /// <summary>物理削除。</summary>
    public async Task DeleteAsync(int sourceId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM entity_sources WHERE source_id = @sourceId;";
        await ExecuteAsync(sql, new { sourceId }, ct).ConfigureAwait(false);
    }
}
