using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_card_roles テーブル（カード内に登場する役職）の CRUD リポジトリ。
/// <para>
/// レイアウト位置は (tier, group_in_tier, order_in_group) の 3 列で表現:
/// <list type="bullet">
///   <item><description>tier=1（上段）/ 2（下段）</description></item>
///   <item><description>group_in_tier: 同 tier 内のサブグループ番号（1 始まり）</description></item>
///   <item><description>order_in_group: 同 (card_id, tier, group_in_tier) グループ内の左右順（1 始まり）</description></item>
/// </list>
/// UNIQUE は <c>(card_id, tier, group_in_tier, order_in_group)</c> の 4 列複合。
/// role_code は NULL 可（ブランクロール = ロゴ単独表示の枠）。
/// </para>
/// </summary>
public sealed class CreditCardRolesRepository
{
    private readonly IConnectionFactory _factory;

    public CreditCardRolesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          card_role_id    AS CardRoleId,
          card_id         AS CardId,
          role_code       AS RoleCode,
          tier            AS Tier,
          group_in_tier   AS GroupInTier,
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

    /// <summary>
    /// 指定カードに紐付く役職一覧を取得する。
    /// 並び順は <c>tier → group_in_tier → order_in_group</c> 昇順で、UI 側の
    /// 「Tier ノード → Group ノード → Role ノード」の階層構築にそのまま使える順序。
    /// </summary>
    public async Task<IReadOnlyList<CreditCardRole>> GetByCardAsync(int cardId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_card_roles
            WHERE card_id = @cardId
            ORDER BY tier, group_in_tier, order_in_group;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditCardRole>(new CommandDefinition(sql, new { cardId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の card_role_id を返す。</summary>
    public async Task<int> InsertAsync(CreditCardRole row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_card_roles
              (card_id, role_code, tier, group_in_tier, order_in_group, notes, created_by, updated_by)
            VALUES
              (@CardId, @RoleCode, @Tier, @GroupInTier, @OrderInGroup, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditCardRole row, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_card_roles SET
              card_id         = @CardId,
              role_code       = @RoleCode,
              tier            = @Tier,
              group_in_tier   = @GroupInTier,
              order_in_group  = @OrderInGroup,
              notes           = @Notes,
              updated_by      = @UpdatedBy
            WHERE card_role_id = @CardRoleId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>削除（物理削除。子テーブルは ON DELETE CASCADE で連動削除）。</summary>
    public async Task DeleteAsync(int cardRoleId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_card_roles WHERE card_role_id = @CardRoleId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CardRoleId = cardRoleId }, cancellationToken: ct));
    }

    /// <summary>
    /// 役職群について (card_id, tier, group_in_tier, order_in_group) を一括再設定する
    /// （v1.2.0 工程 B-2 追加 → 工程 E で 4 列対応に拡張）。
    /// <para>
    /// UNIQUE 制約 (card_id, tier, group_in_tier, order_in_group) との一時的衝突を
    /// 避けるため、対象行に一意な退避値（tier=200, group_in_tier=200, order_in_group=200,201,...）を
    /// いったん割り当ててから、本来の値で再採番する 2 段階方式。
    /// </para>
    /// <para>
    /// 同 group 内の単純な並べ替えだけでなく、別 tier / 別 group / 別 card への乗り換え
    /// （v1.2.0 工程 E の自由乗り換え DnD）にも対応するように、updates の各エントリで
    /// cardId / tier / groupInTier / orderInGroup の全てを指定可能。
    /// </para>
    /// </summary>
    public async Task BulkUpdateSeqAsync(
        IEnumerable<(int cardRoleId, int cardId, byte tier, byte groupInTier, byte orderInGroup)> updates,
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
            // 1 段階目：退避値（tier=200, group_in_tier=200, order_in_group=200,201,...）
            // tier=200 / group_in_tier=200 は通常運用では未使用なので衝突しない。
            // card_id は退避中も衝突しないように意図的に変更しない（card_id × tier=200 で UNIQUE は成立）。
            int i = 0;
            foreach (var u in list)
            {
                byte tempOrder = (byte)(200 + i);
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_card_roles SET tier = 200, group_in_tier = 200, order_in_group = @TempOrder WHERE card_role_id = @CardRoleId;",
                    new { TempOrder = tempOrder, CardRoleId = u.cardRoleId },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値（cardId / tier / groupInTier / orderInGroup）で再設定
            foreach (var u in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE credit_card_roles SET
                      card_id         = @CardId,
                      tier            = @Tier,
                      group_in_tier   = @GroupInTier,
                      order_in_group  = @OrderInGroup
                    WHERE card_role_id = @CardRoleId;
                    """,
                    new
                    {
                        CardId = u.cardId,
                        Tier = u.tier,
                        GroupInTier = u.groupInTier,
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
