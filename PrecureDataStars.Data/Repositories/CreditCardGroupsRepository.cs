using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>credit_card_groups テーブル（Tier 内の Group サブグループ）の CRUD リポジトリ。</summary>
public sealed class CreditCardGroupsRepository : RepositoryBase
{
    public CreditCardGroupsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          card_group_id  AS CardGroupId,
          card_tier_id   AS CardTierId,
          group_no       AS GroupNo,
          notes          AS Notes,
          created_at     AS CreatedAt,
          updated_at     AS UpdatedAt,
          created_by     AS CreatedBy,
          updated_by     AS UpdatedBy
        """;

    /// <summary>主キーで 1 件取得。</summary>
    public async Task<CreditCardGroup?> GetByIdAsync(int cardGroupId, CancellationToken ct = default)
    {
        string sql = $"SELECT {SelectColumns} FROM credit_card_groups WHERE card_group_id = @cardGroupId LIMIT 1;";
        return await QuerySingleOrDefaultAsync<CreditCardGroup>(sql, new { cardGroupId }, ct).ConfigureAwait(false);
    }

    /// <summary>指定 Tier 配下の Group 一覧（group_no 昇順）。</summary>
    public async Task<IReadOnlyList<CreditCardGroup>> GetByTierAsync(int cardTierId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns} FROM credit_card_groups
            WHERE card_tier_id = @cardTierId ORDER BY group_no;
            """;
        return await QueryListAsync<CreditCardGroup>(sql, new { cardTierId }, ct).ConfigureAwait(false);
    }

    /// <summary>credit_card_groups テーブルの全行を取得する。 <see cref="CreditTreeIndex"/> 構築用。並びは card_tier_id, group_no 昇順。</summary>
    public async Task<IReadOnlyList<CreditCardGroup>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns} FROM credit_card_groups
            ORDER BY card_tier_id, group_no;
            """;
        return await QueryListAsync<CreditCardGroup>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定カード配下の全 Group 一覧（Tier をまたぐ）。 Tier をまたいだ DnD のときに「カード全体の Group マップ」が必要になるためのヘルパ。</summary>
    public async Task<IReadOnlyList<CreditCardGroup>> GetByCardAsync(int cardId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns.Replace("card_group_id  AS CardGroupId,", "g.card_group_id AS CardGroupId,")}
            FROM credit_card_groups g
            JOIN credit_card_tiers  t ON t.card_tier_id = g.card_tier_id
            WHERE t.card_id = @cardId
            ORDER BY t.tier_no, g.group_no;
            """;
        // 上記の Replace は SelectColumns を g.* で参照させるための簡易書き換え。
        // ただしシンプルに新しい SQL を書いた方が安全なので、以下を使う:
        string sqlSafe = """
            SELECT
              g.card_group_id  AS CardGroupId,
              g.card_tier_id   AS CardTierId,
              g.group_no       AS GroupNo,
              g.notes          AS Notes,
              g.created_at     AS CreatedAt,
              g.updated_at     AS UpdatedAt,
              g.created_by     AS CreatedBy,
              g.updated_by     AS UpdatedBy
            FROM credit_card_groups g
            JOIN credit_card_tiers  t ON t.card_tier_id = g.card_tier_id
            WHERE t.card_id = @cardId
            ORDER BY t.tier_no, g.group_no;
            """;
        return await QueryListAsync<CreditCardGroup>(sqlSafe, new { cardId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。戻り値は新規 card_group_id。</summary>
    public async Task<int> InsertAsync(CreditCardGroup group, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_card_groups (card_tier_id, group_no, notes, created_by, updated_by)
            VALUES (@CardTierId, @GroupNo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await ExecuteScalarAsync<int>(sql, group, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditCardGroup group, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_card_groups SET
              card_tier_id = @CardTierId,
              group_no     = @GroupNo,
              notes        = @Notes,
              updated_by   = @UpdatedBy
            WHERE card_group_id = @CardGroupId;
            """;
        await ExecuteAsync(sql, group, ct).ConfigureAwait(false);
    }

    /// <summary>削除（CASCADE で Role / Block / Entry が連動削除される）。</summary>
    public async Task DeleteAsync(int cardGroupId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_card_groups WHERE card_group_id = @CardGroupId;";
        await ExecuteAsync(sql, new { CardGroupId = cardGroupId }, ct).ConfigureAwait(false);
    }
}
