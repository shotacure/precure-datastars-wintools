using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// characters テーブル（キャラクターマスタ）の CRUD リポジトリ。
/// <para>
/// 全プリキュアを通じて統一管理（series_id は持たない）。シリーズをまたいで
/// 再登場するキャラは同一 character_id を共有する。表記揺れは
/// <see cref="CharacterAliasesRepository"/> 側で管理する。
/// </para>
/// </summary>
public sealed class CharactersRepository
{
    private readonly IConnectionFactory _factory;

    public CharactersRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          character_id    AS CharacterId,
          name            AS Name,
          name_kana       AS NameKana,
          character_kind  AS CharacterKind,
          notes           AS Notes,
          created_at      AS CreatedAt,
          updated_at      AS UpdatedAt,
          created_by      AS CreatedBy,
          updated_by      AS UpdatedBy,
          is_deleted      AS IsDeleted
        """;

    /// <summary>全件取得（character_id 昇順）。</summary>
    public async Task<IReadOnlyList<Character>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM characters
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY character_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Character>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（character_id）で 1 件取得する。</summary>
    public async Task<Character?> GetByIdAsync(int characterId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM characters
            WHERE character_id = @characterId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Character>(
            new CommandDefinition(sql, new { characterId }, cancellationToken: ct));
    }

    /// <summary>name / name_kana への部分一致で検索する。</summary>
    public async Task<IReadOnlyList<Character>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<Character>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM characters
            WHERE is_deleted = 0
              AND (name LIKE @kw OR name_kana LIKE @kw)
            ORDER BY name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Character>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の character_id を返す。</summary>
    public async Task<int> InsertAsync(Character character, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO characters
              (name, name_kana, character_kind, notes, created_by, updated_by)
            VALUES
              (@Name, @NameKana, @CharacterKind, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, character, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Character character, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE characters SET
              name            = @Name,
              name_kana       = @NameKana,
              character_kind  = @CharacterKind,
              notes           = @Notes,
              updated_by      = @UpdatedBy,
              is_deleted      = @IsDeleted
            WHERE character_id = @CharacterId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, character, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int characterId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE characters SET is_deleted = 1, updated_by = @UpdatedBy WHERE character_id = @CharacterId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CharacterId = characterId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }

    /// <summary>
    /// 「キャラ 1 体 = 名義 1 件」の組を 1 トランザクションで一括投入する
    /// （v1.2.0 工程 F 追加。クレジット編集中の CHARACTER_VOICE エントリで「マスタにまだ無いキャラ」を即座に追加する用途）。
    /// <para>
    /// 内部処理:
    /// <list type="number">
    ///   <item><description><c>characters</c> に新規行 INSERT → character_id 取得</description></item>
    ///   <item><description><c>character_aliases</c> に同名で新規行 INSERT → alias_id 取得</description></item>
    /// </list>
    /// 戻り値は新規 alias_id（呼び出し側はこれを credit_block_entries.character_alias_id に直接セットできる）。
    /// </para>
    /// </summary>
    /// <param name="characterName">キャラクター本体の名前（必須、characters.name と character_aliases.name の両方に使う）。</param>
    /// <param name="characterNameKana">かな（任意、両表に流す）。</param>
    /// <param name="characterKindCode">区分コード（character_kinds.character_kind を参照、必須）。</param>
    /// <param name="notes">備考（任意、characters.notes に流す）。</param>
    /// <param name="createdBy">監査用の更新者。</param>
    /// <returns>新規作成された character_aliases.alias_id。</returns>
    public async Task<int> QuickAddWithSingleAliasAsync(
        string characterName,
        string? characterNameKana,
        string characterKindCode,
        string? notes,
        string? createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            throw new ArgumentException("characterName は必須です。", nameof(characterName));
        if (string.IsNullOrWhiteSpace(characterKindCode))
            throw new ArgumentException("characterKindCode は必須です。", nameof(characterKindCode));

        const string sqlInsertChar = """
            INSERT INTO characters (name, name_kana, character_kind, notes, created_by, updated_by)
            VALUES (@Name, @NameKana, @CharacterKind, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertAlias = """
            INSERT INTO character_aliases (character_id, name, name_kana, created_by, updated_by)
            VALUES (@CharacterId, @Name, @NameKana, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: characters
            int characterId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertChar,
                new
                {
                    Name = characterName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(characterNameKana) ? null : characterNameKana.Trim(),
                    CharacterKind = characterKindCode.Trim(),
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            // STEP 2: character_aliases（キャラ本体の名前と同じ名義を最初の名義として登録）
            int aliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertAlias,
                new
                {
                    CharacterId = characterId,
                    Name = characterName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(characterNameKana) ? null : characterNameKana.Trim(),
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
