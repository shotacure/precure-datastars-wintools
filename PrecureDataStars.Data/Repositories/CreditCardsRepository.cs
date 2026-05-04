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

    /// <summary>新規作成。AUTO_INCREMENT の card_id を返す。</summary>
    public async Task<int> InsertAsync(CreditCard card, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_cards
              (credit_id, card_seq, notes, created_by, updated_by)
            VALUES
              (@CreditId, @CardSeq, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, card, cancellationToken: ct));
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
}
