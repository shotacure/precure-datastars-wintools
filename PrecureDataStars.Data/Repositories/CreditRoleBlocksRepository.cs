using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_role_blocks テーブル（役職下のブロック）の CRUD リポジトリ。
/// </summary>
public sealed class CreditRoleBlocksRepository
{
    private readonly IConnectionFactory _factory;

    public CreditRoleBlocksRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          block_id                   AS BlockId,
          card_role_id               AS CardRoleId,
          block_seq                  AS BlockSeq,
          `rows`                     AS Rows,
          cols                       AS Cols,
          leading_company_alias_id   AS LeadingCompanyAliasId,
          notes                      AS Notes,
          created_at                 AS CreatedAt,
          updated_at                 AS UpdatedAt,
          created_by                 AS CreatedBy,
          updated_by                 AS UpdatedBy
        """;

    /// <summary>主キー（block_id）で 1 件取得する。</summary>
    public async Task<CreditRoleBlock?> GetByIdAsync(int blockId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_role_blocks
            WHERE block_id = @blockId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CreditRoleBlock>(
            new CommandDefinition(sql, new { blockId }, cancellationToken: ct));
    }

    /// <summary>指定 card_role に紐付くブロック一覧を取得する（block_seq 昇順）。</summary>
    public async Task<IReadOnlyList<CreditRoleBlock>> GetByCardRoleAsync(int cardRoleId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_role_blocks
            WHERE card_role_id = @cardRoleId
            ORDER BY block_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditRoleBlock>(new CommandDefinition(sql, new { cardRoleId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の block_id を返す。</summary>
    public async Task<int> InsertAsync(CreditRoleBlock block, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_role_blocks
              (card_role_id, block_seq, `rows`, cols, leading_company_alias_id,
               notes, created_by, updated_by)
            VALUES
              (@CardRoleId, @BlockSeq, @Rows, @Cols, @LeadingCompanyAliasId,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, block, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditRoleBlock block, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_role_blocks SET
              card_role_id              = @CardRoleId,
              block_seq                 = @BlockSeq,
              `rows`                    = @Rows,
              cols                      = @Cols,
              leading_company_alias_id  = @LeadingCompanyAliasId,
              notes                     = @Notes,
              updated_by                = @UpdatedBy
            WHERE block_id = @BlockId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, block, cancellationToken: ct));
    }

    /// <summary>削除（物理削除。子テーブルは ON DELETE CASCADE で連動削除）。</summary>
    public async Task DeleteAsync(int blockId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_role_blocks WHERE block_id = @BlockId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { BlockId = blockId }, cancellationToken: ct));
    }

    /// <summary>
    /// 同一 card_role_id 内のブロック群について block_seq を一括再設定する
    /// （v1.2.0 工程 B-2 追加）。UNIQUE 制約 (card_role_id, block_seq) との
    /// 一時的衝突を避けるため、対象行に退避値（200, 201, ...）をいったん割り当ててから、
    /// 本来の値で再採番する 2 段階方式。
    /// </summary>
    public async Task BulkUpdateSeqAsync(
        int cardRoleId,
        IEnumerable<(int blockId, byte blockSeq)> updates,
        CancellationToken ct = default)
    {
        if (updates is null) throw new ArgumentNullException(nameof(updates));
        var list = updates.ToList();
        if (list.Count == 0) return;
        if (list.Count > 50)
            throw new ArgumentException("BulkUpdateSeqAsync: 1 役職あたり 50 ブロックを超える並べ替えは想定していません。", nameof(updates));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1 段階目：退避値（200, 201, 202, ...）
            int i = 0;
            foreach (var u in list)
            {
                byte tempVal = (byte)(200 + i);
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_role_blocks SET block_seq = @TempVal WHERE block_id = @BlockId;",
                    new { TempVal = tempVal, BlockId = u.blockId },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値で再採番
            foreach (var u in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_role_blocks SET block_seq = @BlockSeq WHERE block_id = @BlockId;",
                    new { BlockSeq = u.blockSeq, BlockId = u.blockId },
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
