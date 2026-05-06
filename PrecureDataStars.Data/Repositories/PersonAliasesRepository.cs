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

    // ─────────────────────────────────────────────────────────
    //  v1.2.1 名寄せ機能：付け替え（Reassign）と改名（Rename）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 名寄せ「人物名義の付け替え」を 1 トランザクションで実行する（v1.2.1 追加）。
    /// <para>
    /// 指定の alias を、別人物（<paramref name="newPersonId"/>）に紐付け直す。
    /// 人物本体（<c>persons</c>）の表示名には一切手を加えず、結合だけを動かす。
    /// </para>
    /// <para>
    /// person 系の特殊事情：alias と person の結合は中間テーブル
    /// <c>person_alias_persons</c> 経由なので、付け替えは「中間表の現行行をすべて削除 →
    /// 新 person で 1 行だけ INSERT し直す」という手順を取る。共同名義（複数人で 1 名義、
    /// 中間表が 2 行以上ある状態）も付け替え時には単独名義（1 行）に集約される
    /// 設計とした。共同名義の名寄せは複雑なので、共同名義タブからの個別編集に委ねる。
    /// </para>
    /// <para>
    /// 切り離されて孤立した（=有効な alias を 1 つも持たなくなった）旧人物は
    /// 自動で論理削除する。
    /// </para>
    /// </summary>
    /// <param name="aliasId">付け替え対象の alias_id。</param>
    /// <param name="newPersonId">新しい紐付け先 person_id。</param>
    /// <param name="updatedBy">監査列に記録する更新者。</param>
    public async Task ReassignToPersonAsync(int aliasId, int newPersonId, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: alias の存在チェック（is_deleted=0 のみ対象）
            int? aliasExists = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT alias_id FROM person_aliases WHERE alias_id = @AliasId AND is_deleted = 0;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            if (aliasExists is null)
                throw new InvalidOperationException($"alias_id={aliasId} の有効な行が見つかりません。");

            // STEP 2: 現行で alias に紐付いている person_id 群を取得（孤立判定に使う）
            var oldPersonIds = (await conn.QueryAsync<int>(new CommandDefinition(
                "SELECT person_id FROM person_alias_persons WHERE alias_id = @AliasId;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct))).ToList();

            // 既に新 person しか紐付いていない場合は no-op
            if (oldPersonIds.Count == 1 && oldPersonIds[0] == newPersonId)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            // STEP 3: 中間表の現行行を全削除し、新 person で 1 行だけ作り直す
            //   共同名義（中間表 2 行以上）も結果として単独名義に集約される。
            //   person_alias_persons には is_deleted 列が無いので物理削除でよい。
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM person_alias_persons WHERE alias_id = @AliasId;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO person_alias_persons (alias_id, person_id, person_seq) VALUES (@AliasId, @NewPersonId, 1);",
                new { AliasId = aliasId, NewPersonId = newPersonId },
                transaction: tx, cancellationToken: ct));

            // STEP 4: alias 行の更新者列も touch しておく（変更履歴のため）
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE person_aliases SET updated_by = @UpdatedBy WHERE alias_id = @AliasId;",
                new { UpdatedBy = updatedBy, AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            // STEP 5: 旧 person 群が孤立したか（有効な alias を 1 つも持たなくなったか）確認し、
            //   孤立していたら論理削除する。新 person 自体は当然そのまま残す。
            foreach (var oldPersonId in oldPersonIds.Where(pid => pid != newPersonId))
            {
                int remaining = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM person_alias_persons pap
                    INNER JOIN person_aliases pa ON pa.alias_id = pap.alias_id
                    WHERE pap.person_id = @OldPersonId AND pa.is_deleted = 0;
                    """,
                    new { OldPersonId = oldPersonId },
                    transaction: tx, cancellationToken: ct));

                if (remaining == 0)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "UPDATE persons SET is_deleted = 1, updated_by = @UpdatedBy WHERE person_id = @OldPersonId;",
                        new { UpdatedBy = updatedBy, OldPersonId = oldPersonId },
                        transaction: tx, cancellationToken: ct));
                }
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
    /// 名寄せ「人物名義の改名」を 1 トランザクションで実行する（v1.2.1 追加）。
    /// <para>
    /// 旧 alias は名前を変えずにそのまま残し、新しい name / name_kana を持つ <b>新 alias を INSERT</b>。
    /// 旧 alias.successor_alias_id = 新 alias_id、新 alias.predecessor_alias_id = 旧 alias_id を張って、
    /// 改名前後をスキーマ上の自参照 FK で永続的にリンクする。
    /// 中間表 person_alias_persons は新 alias で旧 alias と同じ person_id を再ひもづけする
    /// （共同名義なら全 person_id を引き継ぐ。person_seq は元の値を保持）。
    /// </para>
    /// <para>
    /// <paramref name="syncParentPerson"/> が true かつ旧 alias が単独名義（中間表 1 行）の場合のみ、
    /// 親人物（<c>persons</c>）の <c>full_name</c> / <c>full_name_kana</c> も新表記で上書きする。
    /// 共同名義の場合は「どの人物に同期するか曖昧」になるので親側は触らない。
    /// </para>
    /// <para>
    /// 紐付け先人物は変更しない。別人物へ繋ぎ変えたい場合は
    /// <see cref="ReassignToPersonAsync"/> を使う。
    /// </para>
    /// <para>
    /// 戻り値は新規作成された alias_id（呼び出し元で UI 上の選択を新 alias に切り替えるなどに使う）。
    /// 旧 alias は <c>is_deleted</c> も <c>0</c> のまま残し、履歴として参照可能にする。
    /// </para>
    /// </summary>
    /// <param name="aliasId">改名対象の旧 alias_id。</param>
    /// <param name="newName">新しい name（必須）。</param>
    /// <param name="newNameKana">新しい name_kana（NULL 可）。</param>
    /// <param name="syncParentPerson">親人物の表示名も同期するなら true（共同名義の場合は無視される）。</param>
    /// <param name="updatedBy">監査列に記録する更新者。</param>
    /// <returns>新規作成された alias_id。</returns>
    public async Task<int> RenameAsync(
        int aliasId, string newName, string? newNameKana,
        bool syncParentPerson, string? updatedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName は必須です。", nameof(newName));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: 旧 alias の現在値を取得（存在チェック兼）
            var oldAlias = await conn.QuerySingleOrDefaultAsync<(int AliasId, string? Notes)?>(new CommandDefinition(
                "SELECT alias_id AS AliasId, notes AS Notes FROM person_aliases WHERE alias_id = @AliasId AND is_deleted = 0;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            if (oldAlias is null)
                throw new InvalidOperationException($"alias_id={aliasId} の有効な行が見つかりません。");

            // STEP 2: 新 alias を INSERT（predecessor_alias_id を旧に向ける）
            int newAliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO person_aliases
                  (name, name_kana, predecessor_alias_id, successor_alias_id, notes, created_by, updated_by)
                VALUES
                  (@Name, @NameKana, @PredecessorAliasId, NULL, @Notes, @CreatedBy, @UpdatedBy);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    Name = newName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(newNameKana) ? null : newNameKana.Trim(),
                    PredecessorAliasId = aliasId,
                    Notes = (string?)null,
                    CreatedBy = updatedBy,
                    UpdatedBy = updatedBy
                },
                transaction: tx, cancellationToken: ct));

            // STEP 3: 旧 alias.successor_alias_id を新 alias に向ける
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE person_aliases
                SET successor_alias_id = @NewAliasId,
                    updated_by = @UpdatedBy
                WHERE alias_id = @OldAliasId;
                """,
                new { NewAliasId = newAliasId, UpdatedBy = updatedBy, OldAliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            // STEP 4: 中間表 person_alias_persons：旧 alias の person 群を新 alias にコピー
            //   共同名義（複数行）も person_seq を含めてそのまま複製する。
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO person_alias_persons (alias_id, person_id, person_seq)
                SELECT @NewAliasId, person_id, person_seq
                FROM person_alias_persons
                WHERE alias_id = @OldAliasId;
                """,
                new { NewAliasId = newAliasId, OldAliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            // STEP 5: 親人物の表示名を同期する場合（単独名義のときだけ）
            if (syncParentPerson)
            {
                var personIds = (await conn.QueryAsync<int>(new CommandDefinition(
                    "SELECT person_id FROM person_alias_persons WHERE alias_id = @AliasId;",
                    new { AliasId = aliasId },
                    transaction: tx, cancellationToken: ct))).ToList();

                if (personIds.Count == 1)
                {
                    // persons.full_name / persons.full_name_kana を上書き。
                    // family_name / given_name は alias 改名と機械的にリンクしないので触らない
                    // （姓・名分割の意味が変わるケースが多いため、必要なら別途人物管理画面で手当て）。
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        UPDATE persons
                        SET full_name = @NewName,
                            full_name_kana = @NewNameKana,
                            updated_by = @UpdatedBy
                        WHERE person_id = @PersonId;
                        """,
                        new
                        {
                            NewName = newName.Trim(),
                            NewNameKana = string.IsNullOrWhiteSpace(newNameKana) ? null : newNameKana.Trim(),
                            UpdatedBy = updatedBy,
                            PersonId = personIds[0]
                        },
                        transaction: tx, cancellationToken: ct));
                }
                // else: 共同名義（2 行以上）なら親同期は黙ってスキップ（呼び出し側ダイアログで
                // 事前に「共同名義は親同期不可」のメッセージを出す想定）。
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return newAliasId;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
