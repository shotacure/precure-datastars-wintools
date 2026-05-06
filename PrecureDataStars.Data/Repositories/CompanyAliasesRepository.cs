using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// company_aliases テーブル（企業名義／屋号マスタ）の CRUD リポジトリ。
/// <para>
/// 1 企業に複数 alias が紐付き、屋号変更時は <c>predecessor_alias_id</c> /
/// <c>successor_alias_id</c> でリンクする（自参照 FK）。分社化等で別 company に
/// またがるリンクも自参照 FK のため同じ 2 列で表現できる。
/// </para>
/// </summary>
public sealed class CompanyAliasesRepository
{
    private readonly IConnectionFactory _factory;

    public CompanyAliasesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          alias_id              AS AliasId,
          company_id            AS CompanyId,
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
    public async Task<IReadOnlyList<CompanyAlias>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CompanyAlias>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー（alias_id）で 1 件取得する。</summary>
    public async Task<CompanyAlias?> GetByIdAsync(int aliasId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            WHERE alias_id = @aliasId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CompanyAlias>(
            new CommandDefinition(sql, new { aliasId }, cancellationToken: ct));
    }

    /// <summary>指定企業に紐付くすべての名義を取得する（alias_id 昇順）。</summary>
    public async Task<IReadOnlyList<CompanyAlias>> GetByCompanyAsync(int companyId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            WHERE company_id = @companyId AND is_deleted = 0
            ORDER BY alias_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CompanyAlias>(new CommandDefinition(sql, new { companyId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>name / name_kana への部分一致で検索する。</summary>
    public async Task<IReadOnlyList<CompanyAlias>> SearchAsync(string keyword, int limit = 100, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return Array.Empty<CompanyAlias>();

        string sql = $"""
            SELECT {SelectColumns}
            FROM company_aliases
            WHERE is_deleted = 0
              AND (name LIKE @kw OR name_kana LIKE @kw)
            ORDER BY name
            LIMIT @limit;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CompanyAlias>(new CommandDefinition(
            sql, new { kw = $"%{keyword}%", limit }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の alias_id を返す。</summary>
    public async Task<int> InsertAsync(CompanyAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO company_aliases
              (company_id, name, name_kana, predecessor_alias_id, successor_alias_id,
               valid_from, valid_to, notes, created_by, updated_by)
            VALUES
              (@CompanyId, @Name, @NameKana, @PredecessorAliasId, @SuccessorAliasId,
               @ValidFrom, @ValidTo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, alias, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CompanyAlias alias, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE company_aliases SET
              company_id            = @CompanyId,
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
        const string sql = "UPDATE company_aliases SET is_deleted = 1, updated_by = @UpdatedBy WHERE alias_id = @AliasId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { AliasId = aliasId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }

    // ─────────────────────────────────────────────────────────
    //  v1.2.1 名寄せ機能：付け替え（Reassign）と改名（Rename）
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 名寄せ「企業屋号の付け替え」を 1 トランザクションで実行する（v1.2.1 追加）。
    /// <para>
    /// 指定の alias を、別企業（<paramref name="newCompanyId"/>）に紐付け直す。
    /// 親企業（<c>companies</c>）の表示名には一切手を加えず、結合だけを動かす。
    /// 切り離されて孤立した（=有効な alias を 1 つも持たなくなった）旧企業は自動で論理削除する。
    /// </para>
    /// </summary>
    /// <param name="aliasId">付け替え対象の alias_id。</param>
    /// <param name="newCompanyId">新しい紐付け先 company_id。</param>
    /// <param name="updatedBy">監査列に記録する更新者。</param>
    public async Task ReassignToCompanyAsync(int aliasId, int newCompanyId, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: 現在の親 company_id を取得（孤立判定に使う）
            int? oldCompanyId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT company_id FROM company_aliases WHERE alias_id = @AliasId AND is_deleted = 0;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            if (oldCompanyId is null)
                throw new InvalidOperationException($"alias_id={aliasId} の有効な行が見つかりません。");

            if (oldCompanyId.Value == newCompanyId)
            {
                // 同じ企業への「付け替え」は no-op。誤操作防止のため明示的にコミットして終了。
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return;
            }

            // STEP 2: alias の company_id を新しい値に更新
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE company_aliases SET company_id = @NewCompanyId, updated_by = @UpdatedBy WHERE alias_id = @AliasId;",
                new { NewCompanyId = newCompanyId, UpdatedBy = updatedBy, AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            // STEP 3: 旧企業が孤立したか確認
            int remaining = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM company_aliases WHERE company_id = @OldCompanyId AND is_deleted = 0;",
                new { OldCompanyId = oldCompanyId.Value },
                transaction: tx, cancellationToken: ct));

            if (remaining == 0)
            {
                // 旧企業を論理削除。
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE companies SET is_deleted = 1, updated_by = @UpdatedBy WHERE company_id = @OldCompanyId;",
                    new { UpdatedBy = updatedBy, OldCompanyId = oldCompanyId.Value },
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
    /// 名寄せ「企業屋号の改名」を 1 トランザクションで実行する（v1.2.1 追加）。
    /// <para>
    /// 旧 alias は名前を変えずにそのまま残し、新しい name / name_kana を持つ <b>新 alias を INSERT</b>。
    /// 旧 alias.successor_alias_id = 新、新 alias.predecessor_alias_id = 旧 を張って、
    /// 屋号変更の前後をスキーマ上の自参照 FK で永続的にリンクする
    /// （旧屋号と新屋号の両方をクレジットから参照可能なまま、履歴も追える）。
    /// 新 alias の company_id は旧 alias と同じ company に紐付ける。
    /// </para>
    /// <para>
    /// <paramref name="syncParentCompany"/> が true の場合、親企業の
    /// <c>companies.name</c> / <c>companies.name_kana</c> も新表記で上書きする
    /// （「企業本体の表記そのものを直したい」用途）。
    /// </para>
    /// <para>
    /// 紐付け先企業は変更しない。別企業へ繋ぎ変えたい場合は <see cref="ReassignToCompanyAsync"/> を使う。
    /// </para>
    /// <para>
    /// 戻り値は新規作成された alias_id。旧 alias は <c>is_deleted = 0</c> のまま残す。
    /// </para>
    /// </summary>
    /// <param name="aliasId">改名対象の旧 alias_id。</param>
    /// <param name="newName">新しい name（必須）。</param>
    /// <param name="newNameKana">新しい name_kana（NULL 可）。</param>
    /// <param name="syncParentCompany">親企業の表示名も同期するなら true。</param>
    /// <param name="updatedBy">監査列に記録する更新者。</param>
    /// <returns>新規作成された alias_id。</returns>
    public async Task<int> RenameAsync(
        int aliasId, string newName, string? newNameKana,
        bool syncParentCompany, string? updatedBy,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("newName は必須です。", nameof(newName));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // STEP 1: 旧 alias の現在値を取得（存在チェック兼、company_id を控える）
            int? companyId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT company_id FROM company_aliases WHERE alias_id = @AliasId AND is_deleted = 0;",
                new { AliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            if (companyId is null)
                throw new InvalidOperationException($"alias_id={aliasId} の有効な行が見つかりません。");

            // STEP 2: 新 alias を INSERT（同じ company_id、predecessor を旧に向ける）
            int newAliasId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO company_aliases
                  (company_id, name, name_kana, predecessor_alias_id, successor_alias_id,
                   notes, created_by, updated_by)
                VALUES
                  (@CompanyId, @Name, @NameKana, @PredecessorAliasId, NULL,
                   @Notes, @CreatedBy, @UpdatedBy);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    CompanyId = companyId.Value,
                    Name = newName.Trim(),
                    NameKana = string.IsNullOrWhiteSpace(newNameKana) ? null : newNameKana.Trim(),
                    PredecessorAliasId = aliasId,
                    Notes = (string?)null,
                    CreatedBy = updatedBy,
                    UpdatedBy = updatedBy
                },
                transaction: tx, cancellationToken: ct));

            // STEP 3: 旧 alias.successor_alias_id を新に向ける
            await conn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE company_aliases
                SET successor_alias_id = @NewAliasId,
                    updated_by = @UpdatedBy
                WHERE alias_id = @OldAliasId;
                """,
                new { NewAliasId = newAliasId, UpdatedBy = updatedBy, OldAliasId = aliasId },
                transaction: tx, cancellationToken: ct));

            // STEP 4: 親企業の表示名も同期する場合
            if (syncParentCompany)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE companies
                    SET name = @NewName,
                        name_kana = @NewNameKana,
                        updated_by = @UpdatedBy
                    WHERE company_id = @CompanyId;
                    """,
                    new
                    {
                        NewName = newName.Trim(),
                        NewNameKana = string.IsNullOrWhiteSpace(newNameKana) ? null : newNameKana.Trim(),
                        UpdatedBy = updatedBy,
                        CompanyId = companyId.Value
                    },
                    transaction: tx, cancellationToken: ct));
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
