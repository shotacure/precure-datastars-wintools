using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_cards テーブル（クレジット内のカード）の CRUD リポジトリ。
/// </summary>
public sealed class CreditCardsRepository
{
    private readonly IConnectionFactory _factory;

    public CreditCardsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          card_id     AS CardId,
          credit_id   AS CreditId,
          card_seq    AS CardSeq,
          notes       AS Notes,
          created_at  AS CreatedAt,
          updated_at  AS UpdatedAt,
          created_by  AS CreatedBy,
          updated_by  AS UpdatedBy
        """;

    /// <summary>主キー（card_id）で 1 件取得する。</summary>
    public async Task<CreditCard?> GetByIdAsync(int cardId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_cards
            WHERE card_id = @cardId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CreditCard>(
            new CommandDefinition(sql, new { cardId }, cancellationToken: ct));
    }

    /// <summary>指定クレジットに紐付くカード一覧を取得する（card_seq 昇順）。</summary>
    public async Task<IReadOnlyList<CreditCard>> GetByCreditAsync(int creditId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_cards
            WHERE credit_id = @creditId
            ORDER BY card_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditCard>(new CommandDefinition(sql, new { creditId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 新規作成（Card 1 行 + 配下に Tier 1 + Group 1 を自動投入）。
    /// AUTO_INCREMENT の card_id を返す。1 トランザクションで実行する
    /// （v1.2.0 工程 G で自動投入動作を追加。それ以前はカード作成だけだったため、
    ///  ユーザーが「+ 役職」を押す前にレイアウト構造を整えるためのボタン操作が無く、
    ///  「+ Tier」「+ Group」の操作なしには役職追加先が用意されていなかった）。
    /// </summary>
    public async Task<int> InsertAsync(CreditCard card, CancellationToken ct = default)
    {
        const string sqlCard = """
            INSERT INTO credit_cards
              (credit_id, card_seq, notes, created_by, updated_by)
            VALUES
              (@CreditId, @CardSeq, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        const string sqlTier = """
            INSERT INTO credit_card_tiers (card_id, tier_no, created_by, updated_by)
            VALUES (@CardId, 1, @CreatedBy, @UpdatedBy);
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
            int newCardId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlCard, card,
                transaction: tx, cancellationToken: ct));
            int newTierId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlTier,
                new { CardId = newCardId, CreatedBy = card.CreatedBy, UpdatedBy = card.UpdatedBy },
                transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(sqlGroup,
                new { CardTierId = newTierId, CreatedBy = card.CreatedBy, UpdatedBy = card.UpdatedBy },
                transaction: tx, cancellationToken: ct));
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return newCardId;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditCard card, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_cards SET
              credit_id  = @CreditId,
              card_seq   = @CardSeq,
              notes      = @Notes,
              updated_by = @UpdatedBy
            WHERE card_id = @CardId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, card, cancellationToken: ct));
    }

    /// <summary>削除（物理削除。子テーブルは ON DELETE CASCADE で連動削除）。</summary>
    public async Task DeleteAsync(int cardId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_cards WHERE card_id = @CardId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CardId = cardId }, cancellationToken: ct));
    }

    /// <summary>
    /// 同一 credit_id 内のカード群について card_seq を一括再設定する
    /// （v1.2.0 工程 B-2 追加。↑↓ ボタンと TreeView DnD の両方から呼ばれる）。
    /// <para>
    /// UNIQUE 制約 (credit_id, card_seq) との一時的衝突を避けるため、各対象行に一意な退避値
    /// （200 から 1 ずつ）をいったん割り当ててから、本来の値で再採番する 2 段階方式。
    /// card_seq は tinyint unsigned (0–255) なので、対象行数 50 程度までは退避値が範囲を
    /// 超えない（呼び出し側もそれ以上のカード数は想定していない）。
    /// </para>
    /// </summary>
    public async Task BulkUpdateSeqAsync(
        int creditId,
        IEnumerable<(int cardId, byte cardSeq)> updates,
        CancellationToken ct = default)
    {
        if (updates is null) throw new ArgumentNullException(nameof(updates));
        var list = updates.ToList();
        if (list.Count == 0) return;
        if (list.Count > 50)
            throw new ArgumentException("BulkUpdateSeqAsync: 1 クレジットあたり 50 カードを超える並べ替えは想定していません。", nameof(updates));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1 段階目：対象行に一意な退避値（200, 201, 202, ...）を割り当てて UNIQUE 衝突を回避
            int i = 0;
            foreach (var u in list)
            {
                byte tempVal = (byte)(200 + i);
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_cards SET card_seq = @TempVal WHERE card_id = @CardId;",
                    new { TempVal = tempVal, CardId = u.cardId },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値で再採番
            foreach (var u in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_cards SET card_seq = @CardSeq WHERE card_id = @CardId;",
                    new { CardSeq = u.cardSeq, CardId = u.cardId },
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
