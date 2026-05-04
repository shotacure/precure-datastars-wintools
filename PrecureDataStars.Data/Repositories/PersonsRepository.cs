using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// persons テーブル（人物マスタ）の CRUD リポジトリ。
/// <para>
/// 表記揺れや改名の扱いは <see cref="PersonAliasesRepository"/> 側、
/// alias と person の結び付けは <see cref="PersonAliasPersonsRepository"/> 側で行う。
/// </para>
/// </summary>
public sealed class PersonsRepository
{
    private readonly IConnectionFactory _factory;

    public PersonsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          person_id        AS PersonId,
          family_name      AS FamilyName,
          given_name       AS GivenName,
          full_name        AS FullName,
          full_name_kana   AS FullNameKana,
          name_en          AS NameEn,
          notes            AS Notes,
          created_at       AS CreatedAt,
          updated_at       AS UpdatedAt,
          created_by       AS CreatedBy,
          updated_by       AS UpdatedBy,
          is_deleted       AS IsDeleted
        """;

    /// <summary>全件取得（person_id 昇順）。</summary>
    public async Task<IReadOnlyList<Person>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM persons
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY person_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Person>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（person_id）で 1 件取得する。</summary>
    public async Task<Person?> GetByIdAsync(int personId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM persons
            WHERE person_id = @personId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Person>(
            new CommandDefinition(sql, new { personId }, cancellationToken: ct));
    }

    /// <summary>full_name / full_name_kana への部分一致で検索する（人物選択 UI から使用）。</summary>
    public async Task<IReadOnlyList<Person>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<Person>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM persons
            WHERE is_deleted = 0
              AND (full_name LIKE @kw OR full_name_kana LIKE @kw OR name_en LIKE @kw)
            ORDER BY full_name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Person>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の person_id を返す。</summary>
    public async Task<int> InsertAsync(Person person, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO persons
              (family_name, given_name, full_name, full_name_kana, name_en,
               notes, created_by, updated_by)
            VALUES
              (@FamilyName, @GivenName, @FullName, @FullNameKana, @NameEn,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, person, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Person person, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE persons SET
              family_name      = @FamilyName,
              given_name       = @GivenName,
              full_name        = @FullName,
              full_name_kana   = @FullNameKana,
              name_en          = @NameEn,
              notes            = @Notes,
              updated_by       = @UpdatedBy,
              is_deleted       = @IsDeleted
            WHERE person_id = @PersonId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, person, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int personId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE persons SET is_deleted = 1, updated_by = @UpdatedBy WHERE person_id = @PersonId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { PersonId = personId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
