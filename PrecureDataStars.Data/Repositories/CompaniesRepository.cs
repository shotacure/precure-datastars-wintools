using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// companies テーブル（企業マスタ）の CRUD リポジトリ。
/// <para>
/// 屋号変更や社名変更は <see cref="CompanyAliasesRepository"/> 側で扱い、
/// 本リポジトリは「企業」一意の器を管理する。分社化等で別企業として扱う場合は
/// 新規レコードを立て、屋号側の前後リンクで系譜を辿る運用を想定。
/// </para>
/// </summary>
public sealed class CompaniesRepository
{
    private readonly IConnectionFactory _factory;

    public CompaniesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          company_id      AS CompanyId,
          name            AS Name,
          name_kana       AS NameKana,
          name_en         AS NameEn,
          founded_date    AS FoundedDate,
          dissolved_date  AS DissolvedDate,
          notes           AS Notes,
          created_at      AS CreatedAt,
          updated_at      AS UpdatedAt,
          created_by      AS CreatedBy,
          updated_by      AS UpdatedBy,
          is_deleted      AS IsDeleted
        """;

    /// <summary>全件取得（company_id 昇順）。</summary>
    public async Task<IReadOnlyList<Company>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM companies
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY company_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Company>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（company_id）で 1 件取得する。</summary>
    public async Task<Company?> GetByIdAsync(int companyId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM companies
            WHERE company_id = @companyId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Company>(
            new CommandDefinition(sql, new { companyId }, cancellationToken: ct));
    }

    /// <summary>name / name_kana / name_en への部分一致で検索する。</summary>
    public async Task<IReadOnlyList<Company>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<Company>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM companies
            WHERE is_deleted = 0
              AND (name LIKE @kw OR name_kana LIKE @kw OR name_en LIKE @kw)
            ORDER BY name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Company>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の company_id を返す。</summary>
    public async Task<int> InsertAsync(Company company, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO companies
              (name, name_kana, name_en, founded_date, dissolved_date,
               notes, created_by, updated_by)
            VALUES
              (@Name, @NameKana, @NameEn, @FoundedDate, @DissolvedDate,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, company, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Company company, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE companies SET
              name            = @Name,
              name_kana       = @NameKana,
              name_en         = @NameEn,
              founded_date    = @FoundedDate,
              dissolved_date  = @DissolvedDate,
              notes           = @Notes,
              updated_by      = @UpdatedBy,
              is_deleted      = @IsDeleted
            WHERE company_id = @CompanyId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, company, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int companyId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE companies SET is_deleted = 1, updated_by = @UpdatedBy WHERE company_id = @CompanyId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CompanyId = companyId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
