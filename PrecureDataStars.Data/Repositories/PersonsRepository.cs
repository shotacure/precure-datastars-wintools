
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

    /// <summary>
    /// 「人物 1 名 = 名義 1 件」の組を 1 トランザクションで一括投入する
    /// （v1.2.0 工程 B-3c 追加。クレジット編集中に「マスタにまだ無い人物」を即座に追加する用途）。
    /// <para>
    /// 内部処理:
    /// <list type="number">
    ///   <item><description><c>persons</c> に新規行 INSERT → person_id 取得（family_name / given_name も併せて格納）</description></item>
    ///   <item><description><c>person_aliases</c> に同名で新規行 INSERT → alias_id 取得</description></item>
    ///   <item><description><c>person_alias_persons</c> に <c>(alias_id, person_id, person_seq=1)</c> を INSERT</description></item>
    /// </list>
    /// 戻り値は新規 alias_id（呼び出し側はこれを credit_block_entries.person_alias_id に直接セットできる）。
    /// </para>
    /// <para>
    /// 共同名義（複数人で 1 名義）はこのメソッドでは扱わない。共同名義が必要なケースは
    /// CreditMastersEditorForm の「人物名義」タブから別途作成する運用とする。
    /// </para>
    /// <para>
    /// v1.2.1 で <paramref name="familyName"/> / <paramref name="givenName"/> 引数を追加。
    /// 呼び出し側で姓・名を分離して渡せる場合は persons.family_name / persons.given_name にも
    /// 値が入るようになり、検索や並び替えで使えるようになる（NULL 許容なので未入力も OK）。
    /// </para>
    /// </summary>
    /// <param name="fullName">人物本体の氏名（必須、persons.full_name と person_aliases.name の両方に使う）。</param>
    /// <param name="fullNameKana">かな（任意、両表に流す）。</param>
    /// <param name="familyName">姓（任意、persons.family_name に流す。v1.2.1 追加）。</param>
    /// <param name="givenName">名（任意、persons.given_name に流す。v1.2.1 追加）。</param>
    /// <param name="nameEn">英名（任意、persons.name_en に流す）。</param>
    /// <param name="notes">備考（任意、persons.notes に流す）。</param>
    /// <param name="createdBy">監査用の更新者（呼び出し側で <c>Environment.UserName</c> 等を渡す）。</param>
    /// <returns>新規作成された person_aliases.alias_id。</returns>
    public async Task<int> QuickAddWithSingleAliasAsync(
        string fullName,
        string? fullNameKana,
        string? familyName,
        string? givenName,
        string? nameEn,
        string? notes,
        string? createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("fullName は必須です。", nameof(fullName));

        // v1.2.1: family_name / given_name 列にも値を流すよう INSERT を拡張。
        const string sqlInsertPerson = """
            INSERT INTO persons (family_name, given_name, full_name, full_name_kana, name_en, notes, created_by, updated_by)
            VALUES (@FamilyName, @GivenName, @FullName, @FullNameKana, @NameEn, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertAlias = """
            INSERT INTO person_aliases (name, name_kana, notes, created_by, updated_by)
            VALUES (@Name, @NameKana, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertLink = """
            INSERT INTO person_alias_persons (alias_id, person_id, person_seq)
            VALUES (@AliasId, @PersonId, 1);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: persons（v1.2.1 で姓・名分離保存に対応）
            int personId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertPerson,
                new
                {
                    FamilyName = string.IsNullOrWhiteSpace(familyName) ? null : familyName.Trim(),
                    GivenName  = string.IsNullOrWhiteSpace(givenName)  ? null : givenName.Trim(),
                    FullName = fullName.Trim(),
                    FullNameKana = string.IsNullOrWhiteSpace(fullNameKana) ? null : fullNameKana.Trim(),
                    NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim(),
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            // STEP 2: person_aliases（名義名は人物の氏名と同じものを最初の名義として登録）
            int aliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertAlias,
                new
                {
                    Name = fullName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(fullNameKana) ? null : fullNameKana.Trim(),
                    Notes = (string?)null,
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            // STEP 3: person_alias_persons の中間表
            await conn.ExecuteAsync(new CommandDefinition(
                sqlInsertLink,
                new { AliasId = aliasId, PersonId = personId },
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

    /// <summary>
    /// 指定 alias_id の主人物（person_alias_persons.person_seq=1）の person_id を返す
    /// （v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// 名義 picker 経由で「この名義の人物 = 既存人物」を解決して、別名義の追加先を確定するために使う。
    /// 主人物が登録されていない（中間表に行が無い）名義に対しては null を返す。
    /// </summary>
    /// <param name="aliasId">対象の名義 ID（→ person_aliases.alias_id）。</param>
    /// <returns>主人物の person_id、無ければ null。</returns>
    public async Task<int?> GetMainPersonIdForAliasAsync(int aliasId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT person_id
            FROM person_alias_persons
            WHERE alias_id = @AliasId
            ORDER BY person_seq
            LIMIT 1;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        // QuerySingleOrDefaultAsync<int?> は person_alias_persons に行が無いとき null を返す。
        return await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(sql, new { AliasId = aliasId }, cancellationToken: ct));
    }

    /// <summary>
    /// 既存名義（<paramref name="aliasId"/>）を既存人物（<paramref name="personId"/>）に紐付ける
    /// （v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// person_alias_persons の中間表に 1 行 INSERT する。person_seq の既定値は 1（主人物として登録）。
    /// 既に同じ (alias_id, person_id) の行がある場合は何もしない（PK 衝突回避のため INSERT IGNORE）。
    /// </summary>
    /// <param name="aliasId">紐付ける名義 ID。</param>
    /// <param name="personId">紐付ける人物 ID。</param>
    /// <param name="personSeq">person_alias_persons.person_seq。1=主人物（既定）、2 以上=共同名義の連名。</param>
    public async Task LinkAliasToPersonAsync(int aliasId, int personId, byte personSeq = 1, CancellationToken ct = default)
    {
        const string sql = """
            INSERT IGNORE INTO person_alias_persons (alias_id, person_id, person_seq)
            VALUES (@AliasId, @PersonId, @PersonSeq);
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId, PersonId = personId, PersonSeq = personSeq }, cancellationToken: ct));
    }

    /// <summary>
    /// 新規人物 + 新規名義 1 件を 1 トランザクションで登録する
    /// （v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// <para>
    /// 既存の <see cref="QuickAddWithSingleAliasAsync"/> は「人物の氏名 = 最初の名義」という仮定が
    /// 強く、未マッチング名義の登録（人物氏名と alias.name が異なる）に対応しきれない。
    /// 本メソッドはその制約を取り払い、人物の姓・名・フル氏名と、alias 側の name / kana / en /
    /// display_text_override を独立に指定できる形にしている。
    /// </para>
    /// <para>
    /// 1 トランザクション内で：persons → person_aliases → person_alias_persons の 3 INSERT を実行し、
    /// 途中で例外が発生すれば全てロールバックされる（孤児 alias / 孤児 person が残らない）。
    /// </para>
    /// </summary>
    /// <param name="aliasName">作成する名義の name 列。未マッチングの対象テキストをそのまま渡す想定。</param>
    /// <param name="aliasKana">名義のかな（任意）。</param>
    /// <param name="aliasEn">名義の英語名（任意）。</param>
    /// <param name="aliasDisplayOverride">名義の表示上書き（任意）。</param>
    /// <param name="fullName">人物の full_name 列（必須）。aliasName と同じでも別でもよい。</param>
    /// <param name="fullNameKana">人物のフル氏名かな（任意）。</param>
    /// <param name="familyName">人物の family_name 列（任意、英文クレジット用に重要）。</param>
    /// <param name="givenName">人物の given_name 列（任意、同上）。</param>
    /// <param name="nameEn">人物の name_en 列（任意）。</param>
    /// <param name="notes">人物の notes（任意）。</param>
    /// <param name="createdBy">監査用更新者。</param>
    /// <returns>新規作成された person_aliases.alias_id。</returns>
    public async Task<int> AddPersonWithAliasAsync(
        string aliasName,
        string? aliasKana,
        string? aliasEn,
        string? aliasDisplayOverride,
        string fullName,
        string? fullNameKana,
        string? familyName,
        string? givenName,
        string? nameEn,
        string? notes,
        string? createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aliasName))
            throw new ArgumentException("aliasName は必須です。", nameof(aliasName));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("fullName は必須です。", nameof(fullName));

        const string sqlInsertPerson = """
            INSERT INTO persons (family_name, given_name, full_name, full_name_kana, name_en, notes, created_by, updated_by)
            VALUES (@FamilyName, @GivenName, @FullName, @FullNameKana, @NameEn, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertAlias = """
            INSERT INTO person_aliases
              (name, name_kana, name_en, display_text_override, notes, created_by, updated_by)
            VALUES
              (@Name, @NameKana, @NameEn, @DisplayTextOverride, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertLink = """
            INSERT INTO person_alias_persons (alias_id, person_id, person_seq)
            VALUES (@AliasId, @PersonId, 1);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int personId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertPerson,
                new
                {
                    FamilyName = string.IsNullOrWhiteSpace(familyName) ? null : familyName.Trim(),
                    GivenName  = string.IsNullOrWhiteSpace(givenName)  ? null : givenName.Trim(),
                    FullName = fullName.Trim(),
                    FullNameKana = string.IsNullOrWhiteSpace(fullNameKana) ? null : fullNameKana.Trim(),
                    NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim(),
                    Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            int aliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertAlias,
                new
                {
                    Name = aliasName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(aliasKana) ? null : aliasKana.Trim(),
                    NameEn = string.IsNullOrWhiteSpace(aliasEn) ? null : aliasEn.Trim(),
                    DisplayTextOverride = string.IsNullOrWhiteSpace(aliasDisplayOverride) ? null : aliasDisplayOverride.Trim(),
                    Notes = (string?)null,
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                sqlInsertLink,
                new { AliasId = aliasId, PersonId = personId },
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

    /// <summary>
    /// 既存人物に新規名義を 1 件追加する（v1.3.0 ブラッシュアップ stage 16 Phase 3 で追加）。
    /// person_aliases INSERT と person_alias_persons INSERT を 1 トランザクションで実行し、
    /// 途中で例外が発生すれば全てロールバックされる（孤児 alias が残らない）。
    /// </summary>
    /// <param name="personId">紐付け先の既存人物 ID。</param>
    /// <param name="aliasName">作成する名義の name 列。</param>
    /// <param name="aliasKana">名義のかな（任意）。</param>
    /// <param name="aliasEn">名義の英語名（任意）。</param>
    /// <param name="aliasDisplayOverride">名義の表示上書き（任意）。</param>
    /// <param name="createdBy">監査用更新者。</param>
    /// <returns>新規作成された person_aliases.alias_id。</returns>
    public async Task<int> AddAliasToExistingPersonAsync(
        int personId,
        string aliasName,
        string? aliasKana,
        string? aliasEn,
        string? aliasDisplayOverride,
        string? createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aliasName))
            throw new ArgumentException("aliasName は必須です。", nameof(aliasName));

        const string sqlInsertAlias = """
            INSERT INTO person_aliases
              (name, name_kana, name_en, display_text_override, notes, created_by, updated_by)
            VALUES
              (@Name, @NameKana, @NameEn, @DisplayTextOverride, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlInsertLink = """
            INSERT INTO person_alias_persons (alias_id, person_id, person_seq)
            VALUES (@AliasId, @PersonId, 1);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int aliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertAlias,
                new
                {
                    Name = aliasName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(aliasKana) ? null : aliasKana.Trim(),
                    NameEn = string.IsNullOrWhiteSpace(aliasEn) ? null : aliasEn.Trim(),
                    DisplayTextOverride = string.IsNullOrWhiteSpace(aliasDisplayOverride) ? null : aliasDisplayOverride.Trim(),
                    Notes = (string?)null,
                    CreatedBy = createdBy,
                    UpdatedBy = createdBy
                },
                transaction: tx, cancellationToken: ct));

            await conn.ExecuteAsync(new CommandDefinition(
                sqlInsertLink,
                new { AliasId = aliasId, PersonId = personId },
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
