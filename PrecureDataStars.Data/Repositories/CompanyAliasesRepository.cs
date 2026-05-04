using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// company_aliases テーブル（企業名義／屋号マスタ）の CRUD リポジトリ。
/// <para>
/// 1 企業に複数 alias が紐付き、屋号変更時は <c>predecessor_alias_id</c> /
/// <c>successor_alias_id</c> でリンクする（自参照 FK）。分社化等で別 company に
/// またがるリンクも自参照 FK のため同じ 2 列で表現できる。
/// </para>
/// </summary>
public sealed class CompanyAliasesRepository
{
    private readonly IConnectionFactory _factory;

    public CompanyAliasesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          alias_id              AS AliasId,
          company_id            AS CompanyId,
          name                  AS Name,
          name_kana             AS NameKana,
          predecessor_alias_id  AS PredecessorAliasId,
          successor_alias_id    AS SuccessorAliasId,
          valid_from            AS ValidFrom,
          valid_to              AS ValidTo,
          notes                 AS Notes,
          created_at            AS CreatedAt,
          updated_at            AS UpdatedAt,
          created_by            AS CreatedBy,
          updated_by            AS UpdatedBy,
          is_deleted            AS IsDeleted
        """;

    /// <summary>全件取得（alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<CompanyAlias>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CompanyAlias>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（alias_id）で 1 件取得する。</summary>
    public async Task<CompanyAlias?> GetByIdAsync(int aliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            WHERE alias_id = @aliasId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CompanyAlias>(
            new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
    }

    /// <summary>指定企業に紐付くすべての名義を取得する（alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<CompanyAlias>> GetByCompanyAsync(int companyId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            WHERE company_id = @companyId AND is_deleted = 0
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CompanyAlias>(new CommandDefinition(sql, new { companyId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>name / name_kana への部分一致で検索する。</summary>
    public async Task<IReadOnlyList<CompanyAlias>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<CompanyAlias>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            WHERE is_deleted = 0
              AND (name LIKE @kw OR name_kana LIKE @kw)
            ORDER BY name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CompanyAlias>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の alias_id を返す。</summary>
    public async Task<int> InsertAsync(CompanyAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO company_aliases
              (company_id, name, name_kana, predecessor_alias_id, successor_alias_id,
               valid_from, valid_to, notes, created_by, updated_by)
            VALUES
              (@CompanyId, @Name, @NameKana, @PredecessorAliasId, @SuccessorAliasId,
               @ValidFrom, @ValidTo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CompanyAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE company_aliases SET
              company_id            = @CompanyId,
              name                  = @Name,
              name_kana             = @NameKana,
              predecessor_alias_id  = @PredecessorAliasId,
              successor_alias_id    = @SuccessorAliasId,
              valid_from            = @ValidFrom,
              valid_to              = @ValidTo,
              notes                 = @Notes,
              updated_by            = @UpdatedBy,
              is_deleted            = @IsDeleted
            WHERE alias_id = @AliasId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int aliasId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE company_aliases SET is_deleted = 1, updated_by = @UpdatedBy WHERE alias_id = @AliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
