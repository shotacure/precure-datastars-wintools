using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>series_role_format_overrides テーブル（シリーズ × 役職 × 期間で書式テンプレを上書き）の CRUD リポジトリ。 書式解決の優先順は (1) 当該シリーズ × 役職 × 該当期間の本テーブル → (2) roles.default_format_template → (3) 単純連結。本リポジトリは (1) のレコードを管理する。</summary>
public sealed class SeriesRoleFormatOverridesRepository : RepositoryBase
{
    public SeriesRoleFormatOverridesRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          series_id        AS SeriesId,
          role_code        AS RoleCode,
          valid_from       AS ValidFrom,
          valid_to         AS ValidTo,
          format_template  AS FormatTemplate,
          notes            AS Notes,
          created_at       AS CreatedAt,
          updated_at       AS UpdatedAt,
          created_by       AS CreatedBy,
          updated_by       AS UpdatedBy
        """;

    /// <summary>指定シリーズの上書き行を取得する（role_code / valid_from 昇順）。</summary>
    public async Task<IReadOnlyList<SeriesRoleFormatOverride>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_role_format_overrides
            WHERE series_id = @seriesId
            ORDER BY role_code, valid_from;
            """;

        return await QueryListAsync<SeriesRoleFormatOverride>(sql, new { seriesId }, ct).ConfigureAwait(false);
    }

    /// <summary>指定 (series, role, valid_from) で 1 件を取得する。</summary>
    public async Task<SeriesRoleFormatOverride?> GetByKeyAsync(int seriesId, string roleCode, DateTime validFrom, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_role_format_overrides
            WHERE series_id = @seriesId AND role_code = @roleCode AND valid_from = @validFrom
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<SeriesRoleFormatOverride>(sql, new { seriesId, roleCode, validFrom }, ct).ConfigureAwait(false);
    }

    /// <summary>UPSERT（PK は series_id, role_code, valid_from の 3 列）。</summary>
    public async Task UpsertAsync(SeriesRoleFormatOverride row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO series_role_format_overrides
              (series_id, role_code, valid_from, valid_to, format_template,
               notes, created_by, updated_by)
            VALUES
              (@SeriesId, @RoleCode, @ValidFrom, @ValidTo, @FormatTemplate,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              valid_to        = VALUES(valid_to),
              format_template = VALUES(format_template),
              notes           = VALUES(notes),
              updated_by      = VALUES(updated_by);
            """;

        await ExecuteAsync(sql, row, ct).ConfigureAwait(false);
    }

    /// <summary>指定 (series, role, valid_from) の上書き行を削除する。</summary>
    public async Task DeleteAsync(int seriesId, string roleCode, DateTime validFrom, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM series_role_format_overrides
            WHERE series_id = @SeriesId AND role_code = @RoleCode AND valid_from = @ValidFrom;
            """;

        await ExecuteAsync(sql, new { SeriesId = seriesId, RoleCode = roleCode, ValidFrom = validFrom }, ct).ConfigureAwait(false);
    }
}
