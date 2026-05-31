using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>person_aliases テーブル（人物名義マスタ）の CRUD リポジトリ。</summary>
public sealed class PersonAliasesRepository
{
    private readonly IConnectionFactory _factory;

    public PersonAliasesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // display_text_override 列を SELECT に追加。
    // ユニット名義などで定形外の表示文字列が要るケース用。NULL のときは name を使う。
    private const string SelectColumns = """
          alias_id               AS AliasId,
          name                   AS Name,
          name_kana              AS NameKana,
          name_en                AS NameEn,
          display_text_override  AS DisplayTextOverride,
          predecessor_alias_id   AS PredecessorAliasId,
          successor_alias_id     AS SuccessorAliasId,
          valid_from             AS ValidFrom,
          valid_to               AS ValidTo,
          notes                  AS Notes,
          created_at             AS CreatedAt,
          updated_at             AS UpdatedAt,
          created_by             AS CreatedBy,
          updated_by             AS UpdatedBy,
          is_deleted             AS IsDeleted
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
              pa.name_en               AS NameEn,
              pa.display_text_override AS DisplayTextOverride,
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

    /// <summary>クレジット編集の右クリック入力補助「入力途中に一致」セクション専用：
    /// <c>name</c> / <c>name_kana</c> / <c>display_text_override</c> に前方一致するものを優先し、
    /// 同 keyword で前方一致 0 件のときだけ部分一致にフォールバックする。
    /// 半角/全角スペースは比較前に除去（「成田 良美」「成田良美」の揺れ吸収）。
    /// <c>name LIKE 'kw%'</c> を ORDER BY の先頭に置くことで、前方一致 → 部分一致の順で並ぶ。</summary>
    public async Task<IReadOnlyList<PersonAlias>> SearchByPrefixThenContainsAsync(string keyword, int limit = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<PersonAlias>();

        // 比較用に空白を除去した keyword。SQL 側は REPLACE で同様に正規化して LIKE 比較。
        var normalized = keyword.Replace(" ", string.Empty).Replace("　", string.Empty);
        if (normalized.Length == 0) return Array.Empty<PersonAlias>();

        // 前方一致は「先頭にマッチ = 1」、それ以外（部分一致）は 0 として並び替える。
        // name のスペース除去版 / display_text_override / name_kana の 3 軸で比較。
        string sql = $"""
            SELECT {SelectColumns}
            FROM person_aliases
            WHERE is_deleted = 0
              AND (
                    REPLACE(REPLACE(name, ' ', ''), '　', '') LIKE @containsPattern
                 OR REPLACE(REPLACE(IFNULL(display_text_override, ''), ' ', ''), '　', '') LIKE @containsPattern
                 OR REPLACE(REPLACE(IFNULL(name_kana, ''), ' ', ''), '　', '') LIKE @containsPattern
              )
            ORDER BY
              CASE
                WHEN REPLACE(REPLACE(name, ' ', ''), '　', '') LIKE @prefixPattern THEN 0
                WHEN REPLACE(REPLACE(IFNULL(display_text_override, ''), ' ', ''), '　', '') LIKE @prefixPattern THEN 0
                WHEN REPLACE(REPLACE(IFNULL(name_kana, ''), ' ', ''), '　', '') LIKE @prefixPattern THEN 1
                ELSE 2
              END,
              CHAR_LENGTH(name) ASC,
              alias_id ASC
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAlias>(new CommandDefinition(
            sql,
            new { prefixPattern = normalized + "%", containsPattern = "%" + normalized + "%", limit },
            cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の alias_id を返す。</summary>
    public async Task<int> InsertAsync(PersonAlias alias, CancellationToken ct = default)
    {
        // display_text_override 列を INSERT に含める。
        // name_en 列を追加。
        const string sql = """
            INSERT INTO person_aliases
              (name, name_kana, name_en, display_text_override, predecessor_alias_id, successor_alias_id,
               valid_from, valid_to, notes, created_by, updated_by)
            VALUES
              (@Name, @NameKana, @NameEn, @DisplayTextOverride, @PredecessorAliasId, @SuccessorAliasId,
               @ValidFrom, @ValidTo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(PersonAlias alias, CancellationToken ct = default)
    {
        // display_text_override 列を UPDATE に含める。
        // name_en 列を追加。
        const string sql = """
            UPDATE person_aliases SET
              name                   = @Name,
              name_kana              = @NameKana,
              name_en                = @NameEn,
              display_text_override  = @DisplayTextOverride,
              predecessor_alias_id   = @PredecessorAliasId,
              successor_alias_id     = @SuccessorAliasId,
              valid_from             = @ValidFrom,
              valid_to               = @ValidTo,
              notes                  = @Notes,
              updated_by             = @UpdatedBy,
              is_deleted             = @IsDeleted
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

    //  名寄せ機能：付け替え（Reassign）と改名（Rename）

    /// <summary>
    /// 名寄せ「人物名義の付け替え」を 1 トランザクションで実行する。
    /// 指定の alias を、別人物（<paramref name="newPersonId"/>）に紐付け直す。
    /// 人物本体（<c>persons</c>）の表示名には一切手を加えず、結合だけを動かす。
    /// person 系の特殊事情：alias と person の結合は中間テーブル
    /// <c>person_alias_persons</c> 経由なので、付け替えは「中間表の現行行をすべて削除 →
    /// 新 person で 1 行だけ INSERT し直す」という手順を取る。共同名義（複数人で 1 名義、
    /// 中間表が 2 行以上ある状態）も付け替え時には単独名義（1 行）に集約される
    /// 設計。共同名義の名寄せは複雑なので、共同名義タブからの個別編集に委ねる。
    /// 切り離されて孤立した（=有効な alias を 1 つも持たなくなった）旧人物は
    /// 自動で論理削除する。
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
    /// 名寄せ「人物名義の改名」を 1 トランザクションで実行する。
    /// 旧 alias は名前を変えずにそのまま残し、新しい name / name_kana を持つ <b>新 alias を INSERT</b>。
    /// 旧 alias.successor_alias_id = 新 alias_id、新 alias.predecessor_alias_id = 旧 alias_id を張って、
    /// 改名前後をスキーマ上の自参照 FK で永続的にリンクする。
    /// 中間表 person_alias_persons は新 alias で旧 alias と同じ person_id を再ひもづけする
    /// （共同名義なら全 person_id を引き継ぐ。person_seq は元の値を保持）。
    /// <paramref name="syncParentPerson"/> が true かつ旧 alias が単独名義（中間表 1 行）の場合のみ、
    /// 親人物（<c>persons</c>）の <c>full_name</c> / <c>full_name_kana</c> も新表記で上書きする。
    /// 共同名義の場合は「どの人物に同期するか曖昧」になるので親側は触らない。
    /// 紐付け先人物は変更しない。別人物へ繋ぎ変えたい場合は
    /// <see cref="ReassignToPersonAsync"/> を使う。
    /// 戻り値は新規作成された alias_id（呼び出し元で UI 上の選択を新 alias に切り替えるなどに使う）。
    /// 旧 alias は <c>is_deleted</c> も <c>0</c> のまま残し、履歴として参照可能にする。
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

    //  表示名解決（display_text_override 優先）

    /// <summary>指定 alias の表示用文字列を返す。 <c>display_text_override</c> が非空ならそれを、そうでなければ <c>name</c> を返す。 alias が存在しない場合は空文字を返す（呼び出し側で必要に応じて代替表記を出す想定）。 音楽系クレジット表示・移行ツール・テンプレ展開（ThemeSongsHandler）で共通に使う。</summary>
    public async Task<string> GetDisplayNameAsync(int aliasId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COALESCE(NULLIF(display_text_override, ''), name)
            FROM person_aliases
            WHERE alias_id = @aliasId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var result = await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
        return result ?? "";
    }

    /// <summary>
    /// 指定 <paramref name="aliasId"/> に紐付く person_id（PersonSeq=1 の主人物）が、
    /// 過去にクレジットされた <c>role_code</c> 集合を取得する。
    /// Bulk Apply の「未登録役職警告」で、同姓同名の別人 / 役職転向の検出ヒントとして使う。
    /// 共有名義（1 alias → 複数 person）のケースは PersonSeq 最小の主人物のみを対象に絞る。
    /// </summary>
    /// <param name="aliasId">基準となる person_alias_id。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>過去にクレジットされた役職コードのリスト（重複なし）。 該当 alias が紐付け無し or person 配下にクレジット履歴が無い場合は空リスト。</returns>
    public async Task<IReadOnlyList<string>> GetCreditedRoleCodesByPersonOfAliasAsync(int aliasId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT cr.role_code
            FROM credit_block_entries cbe
            JOIN credit_role_blocks crb ON crb.block_id = cbe.block_id
            JOIN credit_card_roles cr   ON cr.card_role_id = crb.card_role_id
            JOIN person_alias_persons pap ON pap.alias_id = cbe.person_alias_id
            WHERE pap.person_id = (
                SELECT person_id FROM person_alias_persons
                WHERE alias_id = @aliasId
                ORDER BY person_seq LIMIT 1
            )
              AND cr.role_code IS NOT NULL;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>名義（<c>name</c> または <c>display_text_override</c>）の完全一致で検索する。
    /// 名寄せ・移行ツールが「フリーテキストと一致する alias」を引くのに使う。
    /// 加えて、姓名の間のスペース有無による表記揺れ（例：入力「本名陽子」⇄ DB「本名 陽子」）を吸収するため、
    /// 半角・全角スペースを除去した上での完全一致もヒット扱いとする
    /// （結果が複数返るとき、呼び出し側で <c>name = @name</c> の厳密一致を優先するのが望ましい）。
    /// ひらがな⇔カタカナの揺れは別物として扱う設計のため、<c>name_kana</c> は比較対象に含めない。</summary>
    public async Task<IReadOnlyList<PersonAlias>> FindByExactNameAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return Array.Empty<PersonAlias>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM person_aliases
            WHERE is_deleted = 0
              AND (name = @name
                   OR display_text_override = @name
                   OR REPLACE(REPLACE(name, ' ', ''), '　', '')
                      = REPLACE(REPLACE(@name, ' ', ''), '　', ''))
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PersonAlias>(new CommandDefinition(sql, new { name }, cancellationToken: ct));
        return rows.ToList();
    }
}
