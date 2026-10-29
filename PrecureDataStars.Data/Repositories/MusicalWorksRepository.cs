using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// musical_works テーブル（曲（著作物）。歌でも劇伴でもない JASRAC 登録の曲）の CRUD リポジトリ。
/// 作家は <see cref="MusicalWorkCreditsRepository"/>、劇伴の曲からの参照は <see cref="BgmCueSourcePartsRepository"/> が扱う。
/// </summary>
public sealed class MusicalWorksRepository : RepositoryBase
{
    public MusicalWorksRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          work_id              AS WorkId,
          title                AS Title,
          title_kana           AS TitleKana,
          jasrac_work_code     AS JasracWorkCode,
          iswc                 AS Iswc,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy,
          is_deleted           AS IsDeleted
        """;

    /// <summary>全件を取得する（work_id 昇順）。SiteBuilder の起動時の全件ロード用。</summary>
    public async Task<IReadOnlyList<MusicalWork>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM musical_works
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY work_id;
            """;
        return await QueryListAsync<MusicalWork>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>work_id で 1 件取得する。</summary>
    public async Task<MusicalWork?> GetByIdAsync(int workId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM musical_works
            WHERE work_id = @workId
            LIMIT 1;
            """;
        return await QuerySingleOrDefaultAsync<MusicalWork>(sql, new { workId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。AUTO_INCREMENT の work_id を返す。</summary>
    public async Task<int> InsertAsync(MusicalWork work, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO musical_works
              (title, title_kana, jasrac_work_code, iswc, notes, created_by, updated_by)
            VALUES
              (@Title, @TitleKana, @JasracWorkCode, @Iswc, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await ExecuteScalarAsync<int>(sql, work, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(MusicalWork work, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE musical_works SET
              title             = @Title,
              title_kana        = @TitleKana,
              jasrac_work_code  = @JasracWorkCode,
              iswc              = @Iswc,
              notes             = @Notes,
              updated_by        = @UpdatedBy,
              is_deleted        = @IsDeleted
            WHERE work_id = @WorkId;
            """;
        await ExecuteAsync(sql, work, ct).ConfigureAwait(false);
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int workId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE musical_works SET is_deleted = 1, updated_by = @UpdatedBy WHERE work_id = @WorkId;";
        await ExecuteAsync(sql, new { WorkId = workId, UpdatedBy = updatedBy }, ct).ConfigureAwait(false);
    }
}
