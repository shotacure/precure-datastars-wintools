using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// character_aliases テーブル（キャラクター名義マスタ）の CRUD リポジトリ。
/// <para>
/// 1 キャラクターに複数 alias が紐付き、表記揺れを記録する。
/// 例: "美墨なぎさ" / "キュアブラック" / "ブラック" 等。
/// </para>
/// </summary>
public sealed class CharacterAliasesRepository
{
    private readonly IConnectionFactory _factory;

    public CharacterAliasesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          alias_id      AS AliasId,
          character_id  AS CharacterId,
          name          AS Name,
          name_kana     AS NameKana,
          valid_from    AS ValidFrom,
          valid_to      AS ValidTo,
          notes         AS Notes,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy,
          is_deleted    AS IsDeleted
        """;

    /// <summary>全件取得（alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<CharacterAlias>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_aliases
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterAlias>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（alias_id）で 1 件取得する。</summary>
    public async Task<CharacterAlias?> GetByIdAsync(int aliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_aliases
            WHERE alias_id = @aliasId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CharacterAlias>(
            new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
    }

    /// <summary>指定キャラクターに紐付くすべての名義を取得する（alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<CharacterAlias>> GetByCharacterAsync(int characterId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM character_aliases
            WHERE character_id = @characterId AND is_deleted = 0
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterAlias>(new CommandDefinition(sql, new { characterId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>name / name_kana への部分一致で検索する。</summary>
    public async Task<IReadOnlyList<CharacterAlias>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<CharacterAlias>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM character_aliases
            WHERE is_deleted = 0
              AND (name LIKE @kw OR name_kana LIKE @kw)
            ORDER BY name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CharacterAlias>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の alias_id を返す。</summary>
    public async Task<int> InsertAsync(CharacterAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO character_aliases
              (character_id, name, name_kana, valid_from, valid_to,
               notes, created_by, updated_by)
            VALUES
              (@CharacterId, @Name, @NameKana, @ValidFrom, @ValidTo,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CharacterAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE character_aliases SET
              character_id  = @CharacterId,
              name          = @Name,
              name_kana     = @NameKana,
              valid_from    = @ValidFrom,
              valid_to      = @ValidTo,
              notes         = @Notes,
              updated_by    = @UpdatedBy,
              is_deleted    = @IsDeleted
            WHERE alias_id = @AliasId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int aliasId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE character_aliases SET is_deleted = 1, updated_by = @UpdatedBy WHERE alias_id = @AliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}
