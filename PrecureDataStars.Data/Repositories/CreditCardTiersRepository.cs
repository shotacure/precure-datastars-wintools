using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_card_tiers テーブル（カード内の Tier 段組）の CRUD リポジトリ。
/// <para>
/// 新規 Tier 作成時には、配下に Group 1 を 1 行自動投入する
/// （ユーザーが「+ Tier」を押したらブランク Tier + ブランク Group まで一気に作る運用、
///  ボタン操作の手数を減らすため）。
/// </para>
/// </summary>
public sealed class CreditCardTiersRepository
{
    private readonly IConnectionFactory _factory;

    public CreditCardTiersRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          card_tier_id   AS CardTierId,
          card_id        AS CardId,
          tier_no        AS TierNo,
          notes          AS Notes,
          created_at     AS CreatedAt,
          updated_at     AS UpdatedAt,
          created_by     AS CreatedBy,
          updated_by     AS UpdatedBy
        """;

    /// <summary>主キーで 1 件取得。</summary>
    public async Task<CreditCardTier?> GetByIdAsync(int cardTierId, CancellationToken ct = default)
    {
        string sql = $"SELECT {SelectColumns} FROM credit_card_tiers WHERE card_tier_id = @cardTierId LIMIT 1;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CreditCardTier>(
            new CommandDefinition(sql, new { cardTierId }, cancellationToken: ct));
    }

    /// <summary>指定カードの Tier 一覧（tier_no 昇順）。</summary>
    public async Task<IReadOnlyList<CreditCardTier>> GetByCardAsync(int cardId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns} FROM credit_card_tiers
            WHERE card_id = @cardId ORDER BY tier_no;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditCardTier>(new CommandDefinition(sql, new { cardId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 新規作成（Tier 1 行 + 配下に Group 1 を 1 行自動投入）。
    /// 戻り値は新規 card_tier_id。1 トランザクションで実行。
    /// </summary>
    public async Task<int> InsertAsync(CreditCardTier tier, CancellationToken ct = default)
    {
        const string sqlTier = """
            INSERT INTO credit_card_tiers (card_id, tier_no, notes, created_by, updated_by)
            VALUES (@CardId, @TierNo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlGroup = """
            INSERT INTO credit_card_groups (card_tier_id, group_no, created_by, updated_by)
            VALUES (@CardTierId, 1, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            int newTierId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlTier, tier,
                transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(sqlGroup,
                new { CardTierId = newTierId, CreatedBy = tier.CreatedBy, UpdatedBy = tier.UpdatedBy },
                transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return newTierId;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Tier だけを単独で投入する（自動 Group 投入なし）。
    /// データ移行・テスト用途。通常は InsertAsync を使う。
    /// </summary>
    public async Task<int> InsertWithoutGroupAsync(CreditCardTier tier, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_card_tiers (card_id, tier_no, notes, created_by, updated_by)
            VALUES (@CardId, @TierNo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, tier, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditCardTier tier, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_card_tiers SET
              card_id    = @CardId,
              tier_no    = @TierNo,
              notes      = @Notes,
              updated_by = @UpdatedBy
            WHERE card_tier_id = @CardTierId;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, tier, cancellationToken: ct));
    }

    /// <summary>削除（CASCADE で Group / Role / Block / Entry が連動削除される）。</summary>
    public async Task DeleteAsync(int cardTierId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_card_tiers WHERE card_tier_id = @CardTierId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CardTierId = cardTierId }, cancellationToken: ct));
    }
}