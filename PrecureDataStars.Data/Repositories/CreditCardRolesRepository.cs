using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_card_roles テーブル（カード内に登場する役職）の CRUD リポジトリ。
/// <para>
/// tier=1（上段）/ 2（下段）+ order_in_tier でカード内のレイアウト位置を保持。
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
          order_in_tier   AS OrderInTier,
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

    /// <summary>指定カードに紐付く役職一覧を取得する（tier → order_in_tier 昇順）。</summary>
    public async Task<IReadOnlyList<CreditCardRole>> GetByCardAsync(int cardId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_card_roles
            WHERE card_id = @cardId
            ORDER BY tier, order_in_tier;
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
              (card_id, role_code, tier, order_in_tier, notes, created_by, updated_by)
            VALUES
              (@CardId, @RoleCode, @Tier, @OrderInTier, @Notes, @CreatedBy, @UpdatedBy);
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
              card_id        = @CardId,
              role_code      = @RoleCode,
              tier           = @Tier,
              order_in_tier  = @OrderInTier,
              notes          = @Notes,
              updated_by     = @UpdatedBy
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
}
