
using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// roles テーブル（クレジット内の役職マスタ）の CRUD リポジトリ。
/// <para>
/// v1.3.0 で <c>successor_role_code</c> 列を追加していたが、役職の系譜は分裂・併合を
/// 含む多対多の関係であり 1 対 1 のカラムでは表現できないため、v1.3.0 ブラッシュアップ続編で
/// 列を撤去し、関係テーブル <c>role_successions</c>（<see cref="RoleSuccessionsRepository"/>）に
/// 系譜情報を移管した。本リポジトリは roles 本体の CRUD のみを担当する。
/// </para>
/// </summary>
public sealed class RolesRepository
{
    private readonly IConnectionFactory _factory;

    public RolesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>全件取得（display_order 昇順、NULL は末尾）。</summary>
    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              role_code                AS RoleCode,
              name_ja                  AS NameJa,
              name_en                  AS NameEn,
              role_format_kind         AS RoleFormatKind,
              display_order            AS DisplayOrder,
              hide_role_name_in_credit AS HideRoleNameInCredit,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM roles
            ORDER BY COALESCE(display_order, 65535), role_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Role>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>役職コードで 1 件取得する。</summary>
    public async Task<Role?> GetByCodeAsync(string roleCode, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              role_code                AS RoleCode,
              name_ja                  AS NameJa,
              name_en                  AS NameEn,
              role_format_kind         AS RoleFormatKind,
              display_order            AS DisplayOrder,
              hide_role_name_in_credit AS HideRoleNameInCredit,
              notes                    AS Notes,
              created_at               AS CreatedAt,
              updated_at               AS UpdatedAt,
              created_by               AS CreatedBy,
              updated_by               AS UpdatedBy
            FROM roles
            WHERE role_code = @roleCode
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Role>(
            new CommandDefinition(sql, new { roleCode }, cancellationToken: ct));
    }

    /// <summary>UPSERT（マスタ管理 UI から利用）。</summary>
    public async Task UpsertAsync(Role role, CancellationToken ct = default)
    {
        // 系譜（role_successions）は別 Repository で管理するため UPSERT 列に含めない。
        const string sql = """
            INSERT INTO roles
              (role_code, name_ja, name_en, role_format_kind,
               display_order, hide_role_name_in_credit, notes, created_by, updated_by)
            VALUES
              (@RoleCode, @NameJa, @NameEn, @RoleFormatKind,
               @DisplayOrder, @HideRoleNameInCredit, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja                  = VALUES(name_ja),
              name_en                  = VALUES(name_en),
              role_format_kind         = VALUES(role_format_kind),
              display_order            = VALUES(display_order),
              hide_role_name_in_credit = VALUES(hide_role_name_in_credit),
              notes                    = VALUES(notes),
              updated_by               = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, role, cancellationToken: ct));
    }

    /// <summary>指定役職コードを削除する（参照されていない場合のみ成功）。</summary>
    public async Task DeleteAsync(string roleCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM roles WHERE role_code = @RoleCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { RoleCode = roleCode }, cancellationToken: ct));
    }

    /// <summary>
    /// 役職一覧の <c>display_order</c> を一括再採番する（v1.2.0 工程 D 追加）。
    /// マスタ役職タブの DnD 並べ替え後に呼び出され、表示順を 10 単位の飛び番
    /// （10, 20, 30, ...）で正規化する。
    /// <para>
    /// <c>roles.display_order</c> は UNIQUE 制約を持たないため、退避値経由の 2 段階更新は不要。
    /// 1 トランザクションで順次 UPDATE すれば PK 衝突は起こらない。
    /// 飛び番にする理由は、後から DB を直接編集して間に役職を挟みたいケースで
    /// <c>display_order=15</c> のような値を間に入れられるようにするため。
    /// アプリ側の DnD 並べ替えのたびに 10 単位で再正規化される運用を想定。
    /// </para>
    /// </summary>
    /// <param name="orderedRoleCodes">
    /// 並べ替え後の役職コード列（先頭が display_order=10、次が 20、...）。
    /// </param>
    public async Task BulkUpdateDisplayOrderAsync(
        IEnumerable<string> orderedRoleCodes,
        CancellationToken ct = default)
    {
        if (orderedRoleCodes is null) throw new ArgumentNullException(nameof(orderedRoleCodes));
        var list = orderedRoleCodes.ToList();
        if (list.Count == 0) return;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int order = 10;
            foreach (var code in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE roles SET display_order = @DisplayOrder WHERE role_code = @RoleCode;",
                    new { DisplayOrder = order, RoleCode = code },
                    transaction: tx, cancellationToken: ct));
                order += 10;
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
