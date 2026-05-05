using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// logos テーブル（企業ロゴマスタ）の CRUD リポジトリ。
/// <para>
/// 屋号（<c>company_alias</c>）配下に CI バージョン違いのロゴが紐付く構造。
/// (<c>company_alias_id</c>, <c>ci_version_label</c>) は UNIQUE。
/// </para>
/// </summary>
public sealed class LogosRepository
{
    private readonly IConnectionFactory _factory;

    public LogosRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          logo_id           AS LogoId,
          company_alias_id  AS CompanyAliasId,
          ci_version_label  AS CiVersionLabel,
          valid_from        AS ValidFrom,
          valid_to          AS ValidTo,
          description       AS Description,
          notes             AS Notes,
          created_at        AS CreatedAt,
          updated_at        AS UpdatedAt,
          created_by        AS CreatedBy,
          updated_by        AS UpdatedBy,
          is_deleted        AS IsDeleted
        """;

    /// <summary>全件取得（logo_id 昇順）。</summary>
    public async Task<IReadOnlyList<Logo>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM logos
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY logo_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Logo>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（logo_id）で 1 件取得する。</summary>
    public async Task<Logo?> GetByIdAsync(int logoId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM logos
            WHERE logo_id = @logoId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Logo>(
            new CommandDefinition(sql, new { logoId }, cancellationToken: ct));
    }

    /// <summary>指定企業名義に紐付くロゴ一覧を取得する。</summary>
    public async Task<IReadOnlyList<Logo>> GetByCompanyAliasAsync(int companyAliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM logos
            WHERE company_alias_id = @companyAliasId AND is_deleted = 0
            ORDER BY ci_version_label;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Logo>(new CommandDefinition(sql, new { companyAliasId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の logo_id を返す。</summary>
    public async Task<int> InsertAsync(Logo logo, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO logos
              (company_alias_id, ci_version_label, valid_from, valid_to,
               description, notes, created_by, updated_by)
            VALUES
              (@CompanyAliasId, @CiVersionLabel, @ValidFrom, @ValidTo,
               @Description, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, logo, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Logo logo, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE logos SET
              company_alias_id  = @CompanyAliasId,
              ci_version_label  = @CiVersionLabel,
              valid_from        = @ValidFrom,
              valid_to          = @ValidTo,
              description       = @Description,
              notes             = @Notes,
              updated_by        = @UpdatedBy,
              is_deleted        = @IsDeleted
            WHERE logo_id = @LogoId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, logo, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int logoId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE logos SET is_deleted = 1, updated_by = @UpdatedBy WHERE logo_id = @LogoId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { LogoId = logoId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
