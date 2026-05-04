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

    /// <summary>
    /// 「企業 1 件 = 屋号 1 件」の組を 1 トランザクションで一括投入する
    /// （v1.2.0 工程 B-3c 追加。クレジット編集中に「マスタにまだ無い企業」を即座に追加する用途）。
    /// <para>
    /// 内部処理:
    /// <list type="number">
    ///   <item><description><c>companies</c> に新規行 INSERT → company_id 取得</description></item>
    ///   <item><description><c>company_aliases</c> に同 company_id で屋号行 INSERT → alias_id 取得</description></item>
    /// </list>
    /// 戻り値は新規 alias_id（呼び出し側はこれを credit_block_entries.company_alias_id 等に
    /// 直接セットできる）。
    /// </para>
    /// <para>
    /// 「既存企業に屋号だけ追加」したいケースは本メソッドでは扱わない。その場合は
    /// 呼び出し側で <see cref="CompanyAliasesRepository.InsertAsync"/> を直接呼ぶこと。
    /// </para>
    /// </summary>
    /// <param name="companyName">企業の正式名称（必須、companies.name に流す）。</param>
    /// <param name="companyNameKana">企業のかな（任意）。</param>
    /// <param name="companyNameEn">企業の英名（任意）。</param>
    /// <param name="aliasName">屋号名（必須、companies.name と同じでも別でも可）。</param>
    /// <param name="aliasNameKana">屋号のかな（任意）。</param>
    /// <param name="createdBy">監査用の更新者。</param>
    /// <returns>新規作成された company_aliases.alias_id。</returns>
    public async Task<int> QuickAddWithSingleAliasAsync(
        string companyName,
        string? companyNameKana,
        string? companyNameEn,
        string aliasName,
        string? aliasNameKana,
        string? createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(companyName))
            throw new ArgumentException("companyName は必須です。", nameof(companyName));
        if (string.IsNullOrWhiteSpace(aliasName))
            throw new ArgumentException("aliasName は必須です。", nameof(aliasName));

        const string sqlInsertCompany = """
            INSERT INTO companies (name, name_kana, name_en, created_by, updated_by)
            VALUES (@Name, @NameKana, @NameEn, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertAlias = """
            INSERT INTO company_aliases (company_id, name, name_kana, created_by, updated_by)
            VALUES (@CompanyId, @Name, @NameKana, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: companies
            int companyId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertCompany,
                new
                {
                    Name = companyName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(companyNameKana) ? null : companyNameKana.Trim(),
                    NameEn = string.IsNullOrWhiteSpace(companyNameEn) ? null : companyNameEn.Trim(),
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            // STEP 2: company_aliases
            int aliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertAlias,
                new
                {
                    CompanyId = companyId,
                    Name = aliasName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(aliasNameKana) ? null : aliasNameKana.Trim(),
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return aliasId;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
