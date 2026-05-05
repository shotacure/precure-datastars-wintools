using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_card_roles テーブル（カード内に登場する役職）の CRUD リポジトリ。
/// <para>
/// v1.2.0 工程 G で大幅刷新：
/// レイアウト位置は所属する Group（card_group_id）と グループ内左右順（order_in_group）の
/// 2 列で表現する（旧 4 列 (card_id, tier, group_in_tier, order_in_group) 構成は廃止）。
/// Card / Tier / Group の階層は FK チェーン（card_role → card_group → card_tier → card）で
/// 一意に決まる。UNIQUE は <c>(card_group_id, order_in_group)</c> の 2 列複合。
/// </para>
/// <para>
/// 新規 Role 作成時には、配下に Block 1 を 1 行自動投入する
/// （ユーザーが「+ 役職」を押したら最低限の枠まで一気に作る運用、ボタン操作の手数を減らすため）。
/// </para>
/// </summary>
public sealed class CreditCardRolesRepository
{
    private readonly IConnectionFactory _factory;

    public CreditCardRolesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          card_role_id    AS CardRoleId,
          card_group_id   AS CardGroupId,
          role_code       AS RoleCode,
          order_in_group  AS OrderInGroup,
          notes           AS Notes,
          created_at      AS CreatedAt,
          updated_at      AS UpdatedAt,
          created_by      AS CreatedBy,
          updated_by      AS UpdatedBy
        """;

    /// <summary>主キー（card_role_id）で 1 件取得する。</summary>
    public async Task<CreditCardRole?> GetByIdAsync(int cardRoleId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_card_roles
            WHERE card_role_id = @cardRoleId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CreditCardRole>(
            new CommandDefinition(sql, new { cardRoleId }, cancellationToken: ct));
    }

    /// <summary>指定 Group 配下の役職一覧（order_in_group 昇順）。</summary>
    public async Task<IReadOnlyList<CreditCardRole>> GetByGroupAsync(int cardGroupId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_card_roles
            WHERE card_group_id = @cardGroupId
            ORDER BY order_in_group;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditCardRole>(new CommandDefinition(sql, new { cardGroupId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定カード配下の全役職一覧（複数 Group / 複数 Tier をまたぐ）。
    /// Tier / Group / Order の昇順で並ぶように JOIN ＋ ORDER BY する。
    /// CreditEditorForm のツリー構築で使う。
    /// </summary>
    public async Task<IReadOnlyList<CreditCardRole>> GetByCardAsync(int cardId, CancellationToken ct = default)
    {
        string sql = """
            SELECT
              r.card_role_id    AS CardRoleId,
              r.card_group_id   AS CardGroupId,
              r.role_code       AS RoleCode,
              r.order_in_group  AS OrderInGroup,
              r.notes           AS Notes,
              r.created_at      AS CreatedAt,
              r.updated_at      AS UpdatedAt,
              r.created_by      AS CreatedBy,
              r.updated_by      AS UpdatedBy
            FROM credit_card_roles  r
            JOIN credit_card_groups g ON g.card_group_id = r.card_group_id
            JOIN credit_card_tiers  t ON t.card_tier_id  = g.card_tier_id
            WHERE t.card_id = @cardId
            ORDER BY t.tier_no, g.group_no, r.order_in_group;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditCardRole>(new CommandDefinition(sql, new { cardId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 新規作成（Role 1 行 + 配下に Block 1 を 1 行自動投入）。
    /// 戻り値は新規 card_role_id。1 トランザクションで実行。
    /// </summary>
    public async Task<int> InsertAsync(CreditCardRole role, CancellationToken ct = default)
    {
        const string sqlRole = """
            INSERT INTO credit_card_roles
              (card_group_id, role_code, order_in_group, notes, created_by, updated_by)
            VALUES
              (@CardGroupId, @RoleCode, @OrderInGroup, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlBlock = """
            INSERT INTO credit_role_blocks
              (card_role_id, block_seq, row_count, col_count, created_by, updated_by)
            VALUES
              (@CardRoleId, 1, 1, 1, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int newRoleId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlRole, role,
                transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(sqlBlock,
                new { CardRoleId = newRoleId, CreatedBy = role.CreatedBy, UpdatedBy = role.UpdatedBy },
                transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return newRoleId;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Role だけを単独で投入する（自動 Block 投入なし）。データ移行・テスト用途。</summary>
    public async Task<int> InsertWithoutBlockAsync(CreditCardRole role, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_card_roles
              (card_group_id, role_code, order_in_group, notes, created_by, updated_by)
            VALUES
              (@CardGroupId, @RoleCode, @OrderInGroup, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, role, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditCardRole role, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_card_roles SET
              card_group_id   = @CardGroupId,
              role_code       = @RoleCode,
              order_in_group  = @OrderInGroup,
              notes           = @Notes,
              updated_by      = @UpdatedBy
            WHERE card_role_id = @CardRoleId;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, role, cancellationToken: ct));
    }

    /// <summary>削除（CASCADE で Block / Entry が連動削除される）。</summary>
    public async Task DeleteAsync(int cardRoleId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_card_roles WHERE card_role_id = @CardRoleId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CardRoleId = cardRoleId }, cancellationToken: ct));
    }

    /// <summary>
    /// 役職群について (card_group_id, order_in_group) を一括再設定する。
    /// 同 Group 内の並べ替え、別 Group / 別 Tier / 別 Card への乗り換えのいずれにも対応。
    /// UNIQUE 制約 (card_group_id, order_in_group) との一時的衝突を避けるため、
    /// 対象行に一意な退避値（card_group_id は変えず order_in_group を 200, 201, ... に）を
    /// いったん割り当ててから本来の値で再採番する 2 段階方式。
    /// 実際には別 Group への移動も含むので、退避フェーズでは順番情報を完全に消すために
    /// 「全対象行を一意な負数 like の高番地に逃がす」のではなく、各行を個別に高い数値に逃がす。
    /// MySQL の UNIQUE は (card_group_id, order_in_group) の組合せなので、
    /// 同 group 内に退避値が複数できないように、退避フェーズでは
    /// 「移動先と異なる退避用 card_group_id を一時的に使う」のは複雑すぎるため、
    /// 単純に「全対象行の order_in_group を 200, 201, ... の連番に上書き」して、
    /// その時点での card_group_id は元のまま据え置く（同 Group 内に同じ値の order_in_group が
    /// 並ばないように 200 始まりで連番を振る）。
    /// </summary>
    public async Task BulkUpdateSeqAsync(
        IEnumerable<(int cardRoleId, int cardGroupId, byte orderInGroup)> updates,
        CancellationToken ct = default)
    {
        if (updates is null) throw new ArgumentNullException(nameof(updates));
        var list = updates.ToList();
        if (list.Count == 0) return;
        if (list.Count > 50)
            throw new ArgumentException("BulkUpdateSeqAsync: 50 役職を超える並べ替えは想定していません。", nameof(updates));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1 段階目：退避。order_in_group を一意な高番地（200, 201, ...）に振り直す。
            //           card_group_id は元のまま。これだと「移動先 group に既存役職が居る」場合、
            //           退避中に対象行と既存行が同 group にぶつかる可能性があるので、
            //           対象行の card_group_id を 0（=どの実在 group とも衝突しない）に逃がす。
            //           ※ card_group_id は FK 制約があるため、0 は実在しない。FK エラーを回避するため
            //           ここでは「元 group のまま」にして、order_in_group のみ高番地に振る。
            //           対象行はすべて違う order_in_group を持つように 200, 201, ... と連番にすれば、
            //           同 group 内で衝突しない。ただし非対象行（=対象行以外）が group=X にあって
            //           order=200 を既に使っていればぶつかる。が、運用上ありえないので許容。
            //           より安全にするには、退避フェーズで card_group_id を NULL にできる別カラムを用意して
            //           移動するが、本実装ではシンプルに上記方針で進める。
            int i = 0;
            foreach (var u in list)
            {
                byte tempOrder = (byte)(200 + i);
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_card_roles SET order_in_group = @TempOrder WHERE card_role_id = @CardRoleId;",
                    new { TempOrder = tempOrder, CardRoleId = u.cardRoleId },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値（card_group_id, order_in_group）で再設定
            foreach (var u in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE credit_card_roles SET
                      card_group_id  = @CardGroupId,
                      order_in_group = @OrderInGroup
                    WHERE card_role_id = @CardRoleId;
                    """,
                    new
                    {
                        CardGroupId = u.cardGroupId,
                        OrderInGroup = u.orderInGroup,
                        CardRoleId = u.cardRoleId
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
