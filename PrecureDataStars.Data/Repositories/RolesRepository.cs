using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// roles テーブル（クレジット内の役職マスタ）の CRUD リポジトリ。
/// </summary>
public sealed class RolesRepository
{
    private readonly IConnectionFactory _factory;

    public RolesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>全件取得（display_order 昇順、NULL は末尾）。</summary>
    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              role_code                AS RoleCode,
              name_ja                  AS NameJa,
              name_en                  AS NameEn,
              role_format_kind         AS RoleFormatKind,
              default_format_template  AS DefaultFormatTemplate,
              display_order            AS DisplayOrder,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM roles
            ORDER BY COALESCE(display_order, 65535), role_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Role>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>役職コードで 1 件取得する。</summary>
    public async Task<Role?> GetByCodeAsync(string roleCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              role_code                AS RoleCode,
              name_ja                  AS NameJa,
              name_en                  AS NameEn,
              role_format_kind         AS RoleFormatKind,
              default_format_template  AS DefaultFormatTemplate,
              display_order            AS DisplayOrder,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM roles
            WHERE role_code = @roleCode
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Role>(
            new CommandDefinition(sql, new { roleCode }, cancellationToken: ct));
    }

    /// <summary>UPSERT（マスタ管理 UI から利用）。</summary>
    public async Task UpsertAsync(Role role, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO roles
              (role_code, name_ja, name_en, role_format_kind, default_format_template,
               display_order, notes, created_by, updated_by)
            VALUES
              (@RoleCode, @NameJa, @NameEn, @RoleFormatKind, @DefaultFormatTemplate,
               @DisplayOrder, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja                  = VALUES(name_ja),
              name_en                  = VALUES(name_en),
              role_format_kind         = VALUES(role_format_kind),
              default_format_template  = VALUES(default_format_template),
              display_order            = VALUES(display_order),
              notes                    = VALUES(notes),
              updated_by               = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, role, cancellationToken: ct));
    }

    /// <summary>指定役職コードを削除する（参照されていない場合のみ成功）。</summary>
    public async Task DeleteAsync(string roleCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM roles WHERE role_code = @RoleCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { RoleCode = roleCode }, cancellationToken: ct));
    }
}
