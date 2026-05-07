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
/// <para>
/// v1.2.1 で <c>valid_from</c> / <c>valid_to</c> 列を撤去（マイグレーション
/// <c>v1.2.1_drop_character_aliases_valid_dates.sql</c> 参照）。
/// 同じく v1.2.1 で名寄せ機能（<see cref="ReassignToCharacterAsync"/> /
/// <see cref="RenameAsync"/>）を追加した。
/// </para>
/// </summary>
public sealed class CharacterAliasesRepository
{
    private readonly IConnectionFactory _factory;

    public CharacterAliasesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // v1.2.1: valid_from / valid_to を SELECT 列から撤去。
    private const string SelectColumns = """
          alias_id      AS AliasId,
          character_id  AS CharacterId,
          name          AS Name,
          name_kana     AS NameKana,
          name_en       AS NameEn,
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
        // v1.2.1: valid_from / valid_to を INSERT 列から撤去。
        // v1.2.4: name_en 列を追加。
        const string sql = """
            INSERT INTO character_aliases
              (character_id, name, name_kana, name_en, notes, created_by, updated_by)
            VALUES
              (@CharacterId, @Name, @NameKana, @NameEn, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CharacterAlias alias, CancellationToken ct = default)
    {
        // v1.2.1: valid_from / valid_to を UPDATE 列から撤去。
        // v1.2.4: name_en 列を追加。
        const string sql = """
            UPDATE character_aliases SET
              character_id  = @CharacterId,
              name          = @Name,
              name_kana     = @NameKana,
              name_en       = @NameEn,
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

    // ─────────────────────────────────────────────────────────
    //  v1.2.1 名寄せ機能：付け替え（Reassign）と改名（Rename）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 名寄せ「名義の付け替え」を 1 トランザクションで実行する（v1.2.1 追加）。
    /// <para>
    /// 指定の alias を、別キャラクター（<paramref name="newCharacterId"/>）に紐付け直す。
    /// 親キャラクターの表示名（characters.name 等）には一切手を加えず、結合だけを動かす。
    /// 切り離されて孤立した（=有効な alias を 1 つも持たなくなった）旧キャラクターは
    /// 自動で論理削除する。
    /// </para>
    /// <para>
    /// 改名（親キャラの表示名も合わせて上書き）が必要な場合は <see cref="RenameAsync"/> を使う。
    /// </para>
    /// </summary>
    /// <param name="aliasId">付け替え対象の alias_id。</param>
    /// <param name="newCharacterId">新しい紐付け先 character_id。</param>
    /// <param name="updatedBy">監査列に記録する更新者。</param>
    public async Task ReassignToCharacterAsync(int aliasId, int newCharacterId, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: 現在の親 character_id を取得（孤立判定に使う）
            int? oldCharacterId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT character_id FROM character_aliases WHERE alias_id = @AliasId AND is_deleted = 0;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            if (oldCharacterId is null)
                throw new InvalidOperationException($"alias_id={aliasId} の有効な行が見つかりません。");

            if (oldCharacterId.Value == newCharacterId)
            {
                // 同じキャラへの「付け替え」は no-op。誤操作防止のため明示的にコミットして終了。
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            // STEP 2: alias の character_id を新しい値に更新
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE character_aliases SET character_id = @NewCharacterId, updated_by = @UpdatedBy WHERE alias_id = @AliasId;",
                new { NewCharacterId = newCharacterId, UpdatedBy = updatedBy, AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            // STEP 3: 旧キャラクターが孤立したか（有効な alias を 1 つも持たなくなったか）を確認
            int remainingAliases = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM character_aliases WHERE character_id = @OldCharacterId AND is_deleted = 0;",
                new { OldCharacterId = oldCharacterId.Value },
                transaction: tx, cancellationToken: ct));

            if (remainingAliases == 0)
            {
                // 旧キャラクターを論理削除（characters.is_deleted = 1）。
                // 物理削除は CASCADE FK が複数あって影響範囲が読み切れないので避ける。
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE characters SET is_deleted = 1, updated_by = @UpdatedBy WHERE character_id = @OldCharacterId;",
                    new { UpdatedBy = updatedBy, OldCharacterId = oldCharacterId.Value },
                    transaction: tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 名寄せ「名義の改名」を 1 トランザクションで実行する（v1.2.1 追加）。
    /// <para>
    /// 指定の alias の <c>name</c> / <c>name_kana</c> を新しい表記に <b>そのまま上書き</b>する。
    /// <paramref name="syncParentCharacter"/> が true の場合、親キャラクターの
    /// <c>characters.name</c> / <c>characters.name_kana</c> も同じ値で上書きする
    /// （「キャラ本体の表記そのものを直したい」用途）。
    /// </para>
    /// <para>
    /// 人物名義（person_aliases）／企業屋号（company_aliases）と異なり、
    /// character_aliases には <c>predecessor_alias_id</c> / <c>successor_alias_id</c> が無い。
    /// キャラ名義は表記揺れを別 alias 行として並存させる運用で十分機能するという
    /// v1.2.0 工程 H 時点の判断による。本メソッドは旧 alias を物理的に書き換えるだけで、
    /// 履歴チェーンは残さない。
    /// </para>
    /// <para>
    /// 紐付け先キャラクターは変更しない。別キャラへ繋ぎ変えたい場合は
    /// <see cref="ReassignToCharacterAsync"/> を使う。
    /// </para>
    /// </summary>
    /// <param name="aliasId">改名対象の alias_id。</param>
    /// <param name="newName">新しい name（必須）。</param>
    /// <param name="newNameKana">新しい name_kana（NULL 可）。</param>
    /// <param name="syncParentCharacter">親キャラクターの表示名も同期するなら true。</param>
    /// <param name="updatedBy">監査列に記録する更新者。</param>
    public async Task RenameAsync(
        int aliasId, string newName, string? newNameKana,
        bool syncParentCharacter, string? updatedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName は必須です。", nameof(newName));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: 現在の親 character_id を取得（同期用）
            int? characterId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT character_id FROM character_aliases WHERE alias_id = @AliasId AND is_deleted = 0;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            if (characterId is null)
                throw new InvalidOperationException($"alias_id={aliasId} の有効な行が見つかりません。");

            // STEP 2: alias の name / name_kana を更新
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE character_aliases
                SET name = @NewName,
                    name_kana = @NewNameKana,
                    updated_by = @UpdatedBy
                WHERE alias_id = @AliasId;
                """,
                new
                {
                    NewName = newName.Trim(),
                    NewNameKana = string.IsNullOrWhiteSpace(newNameKana) ? null : newNameKana.Trim(),
                    UpdatedBy = updatedBy,
                    AliasId = aliasId
                },
                transaction: tx, cancellationToken: ct));

            // STEP 3: 親キャラクターの表示名も同期する場合
            if (syncParentCharacter)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE characters
                    SET name = @NewName,
                        name_kana = @NewNameKana,
                        updated_by = @UpdatedBy
                    WHERE character_id = @CharacterId;
                    """,
                    new
                    {
                        NewName = newName.Trim(),
                        NewNameKana = string.IsNullOrWhiteSpace(newNameKana) ? null : newNameKana.Trim(),
                        UpdatedBy = updatedBy,
                        CharacterId = characterId.Value
                    },
                    transaction: tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
