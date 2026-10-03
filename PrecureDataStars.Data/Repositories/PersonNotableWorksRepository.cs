using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>person_notable_works テーブル（人物のプリキュア以外の代表作）の CRUD リポジトリ。行は物理削除する。</summary>
public sealed class PersonNotableWorksRepository : RepositoryBase
{
    public PersonNotableWorksRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          work_id                  AS WorkId,
          person_id                AS PersonId,
          display_order            AS DisplayOrder,
          work_title               AS WorkTitle,
          role_label               AS RoleLabel,
          year_from                AS YearFrom,
          year_to                  AS YearTo,
          official_url             AS OfficialUrl,
          official_url_is_archive  AS OfficialUrlIsArchive,
          source_url               AS SourceUrl,
          notes                    AS Notes,
          created_at               AS CreatedAt,
          updated_at               AS UpdatedAt,
          created_by               AS CreatedBy,
          updated_by               AS UpdatedBy
        """;

    /// <summary>並び順の ORDER BY（並び順 → 始まりの年（無しは後ろ）→ work_id）。</summary>
    private const string OrderBy = "ORDER BY person_id, display_order, year_from IS NULL, year_from, work_id";

    /// <summary>全件取得（人物 → 並び順）。</summary>
    public async Task<IReadOnlyList<PersonNotableWork>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_notable_works
            {OrderBy};
            """;

        return await QueryListAsync<PersonNotableWork>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定の人物の代表作を並び順で取得する。</summary>
    public async Task<IReadOnlyList<PersonNotableWork>> GetByPersonAsync(int personId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_notable_works
            WHERE person_id = @personId
            {OrderBy};
            """;

        return await QueryListAsync<PersonNotableWork>(sql, new { personId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。AUTO_INCREMENT の work_id を返す。</summary>
    public async Task<int> InsertAsync(PersonNotableWork work, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO person_notable_works
              (person_id, display_order, work_title, role_label, year_from, year_to,
               official_url, official_url_is_archive, source_url, notes, created_by, updated_by)
            VALUES
              (@PersonId, @DisplayOrder, @WorkTitle, @RoleLabel, @YearFrom, @YearTo,
               @OfficialUrl, @OfficialUrlIsArchive, @SourceUrl, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        return await ExecuteScalarAsync<int>(sql, work, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(PersonNotableWork work, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE person_notable_works SET
              display_order            = @DisplayOrder,
              work_title               = @WorkTitle,
              role_label               = @RoleLabel,
              year_from                = @YearFrom,
              year_to                  = @YearTo,
              official_url             = @OfficialUrl,
              official_url_is_archive  = @OfficialUrlIsArchive,
              source_url               = @SourceUrl,
              notes                    = @Notes,
              updated_by               = @UpdatedBy
            WHERE work_id = @WorkId;
            """;

        await ExecuteAsync(sql, work, ct).ConfigureAwait(false);
    }

    /// <summary>物理削除。</summary>
    public async Task DeleteAsync(int workId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM person_notable_works WHERE work_id = @workId;";
        await ExecuteAsync(sql, new { workId }, ct).ConfigureAwait(false);
    }
}
