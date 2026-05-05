using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// person_aliases テーブル（人物名義マスタ）の CRUD リポジトリ。
/// <para>
/// 1 人物に複数 alias が紐付き、改名時は <c>predecessor_alias_id</c> /
/// <c>successor_alias_id</c> でリンクする（自参照 FK）。alias と person の結び付けは
/// 中間テーブル <c>person_alias_persons</c> を扱う <see cref="PersonAliasPersonsRepository"/> で管理する。
/// </para>
/// </summary>
public sealed class PersonAliasesRepository
{
    private readonly IConnectionFactory _factory;

    public PersonAliasesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          alias_id              AS AliasId,
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
    public async Task<IReadOnlyList<PersonAlias>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_aliases
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAlias>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（alias_id）で 1 件取得する。</summary>
    public async Task<PersonAlias?> GetByIdAsync(int aliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_aliases
            WHERE alias_id = @aliasId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<PersonAlias>(
            new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
    }

    /// <summary>指定人物に紐付くすべての名義を取得する（中間テーブル経由、alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<PersonAlias>> GetByPersonAsync(int personId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              pa.alias_id              AS AliasId,
              pa.name                  AS Name,
              pa.name_kana             AS NameKana,
              pa.predecessor_alias_id  AS PredecessorAliasId,
              pa.successor_alias_id    AS SuccessorAliasId,
              pa.valid_from            AS ValidFrom,
              pa.valid_to              AS ValidTo,
              pa.notes                 AS Notes,
              pa.created_at            AS CreatedAt,
              pa.updated_at            AS UpdatedAt,
              pa.created_by            AS CreatedBy,
              pa.updated_by            AS UpdatedBy,
              pa.is_deleted            AS IsDeleted
            FROM person_aliases pa
            INNER JOIN person_alias_persons pap ON pap.alias_id = pa.alias_id
            WHERE pap.person_id = @personId
              AND pa.is_deleted = 0
            ORDER BY pa.alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAlias>(new CommandDefinition(sql, new { personId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>名義（name / name_kana）への部分一致で検索する。</summary>
    public async Task<IReadOnlyList<PersonAlias>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<PersonAlias>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM person_aliases
            WHERE is_deleted = 0
              AND (name LIKE @kw OR name_kana LIKE @kw)
            ORDER BY name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAlias>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の alias_id を返す。</summary>
    public async Task<int> InsertAsync(PersonAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO person_aliases
              (name, name_kana, predecessor_alias_id, successor_alias_id,
               valid_from, valid_to, notes, created_by, updated_by)
            VALUES
              (@Name, @NameKana, @PredecessorAliasId, @SuccessorAliasId,
               @ValidFrom, @ValidTo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(PersonAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE person_aliases SET
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
        const string sql = "UPDATE person_aliases SET is_deleted = 1, updated_by = @UpdatedBy WHERE alias_id = @AliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
