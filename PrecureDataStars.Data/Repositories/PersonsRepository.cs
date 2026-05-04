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
    ///   <item><description><c>persons</c> に新規行 INSERT → person_id 取得</description></item>
    ///   <item><description><c>person_aliases</c> に同名で新規行 INSERT → alias_id 取得</description></item>
    ///   <item><description><c>person_alias_persons</c> に <c>(alias_id, person_id, person_seq=1)</c> を INSERT</description></item>
    /// </list>
    /// 戻り値は新規 alias_id（呼び出し側はこれを credit_block_entries.person_alias_id に直接セットできる）。
    /// </para>
    /// <para>
    /// 共同名義（複数人で 1 名義）はこのメソッドでは扱わない。共同名義が必要なケースは
    /// CreditMastersEditorForm の「人物名義」タブから別途作成する運用とする。
    /// </para>
    /// </summary>
    /// <param name="fullName">人物本体の氏名（必須、persons.full_name と person_aliases.name の両方に使う）。</param>
    /// <param name="fullNameKana">かな（任意、両表に流す）。</param>
    /// <param name="nameEn">英名（任意、persons.name_en に流す）。</param>
    /// <param name="notes">備考（任意、persons.notes に流す）。</param>
    /// <param name="createdBy">監査用の更新者（呼び出し側で <c>Environment.UserName</c> 等を渡す）。</param>
    /// <returns>新規作成された person_aliases.alias_id。</returns>
    public async Task<int> QuickAddWithSingleAliasAsync(
        string fullName,
        string? fullNameKana,
        string? nameEn,
        string? notes,
        string? createdBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("fullName は必須です。", nameof(fullName));

        const string sqlInsertPerson = """
            INSERT INTO persons (full_name, full_name_kana, name_en, notes, created_by, updated_by)
            VALUES (@FullName, @FullNameKana, @NameEn, @Notes, @CreatedBy, @UpdatedBy);
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
            // STEP 1: persons
            int personId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                sqlInsertPerson,
                new
                {
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
}
