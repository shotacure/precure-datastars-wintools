using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credit_block_entries テーブル（ブロック内のエントリ）の CRUD リポジトリ。
/// <para>
/// entry_kind に応じて参照先カラムが切り替わる（PERSON / CHARACTER_VOICE / COMPANY /
/// LOGO / SONG / TEXT）。整合性は DB 側トリガー
/// <c>trg_credit_block_entries_b{i,u}_consistency</c> で保証される。
/// 不正な組み合わせの INSERT/UPDATE は SQLSTATE 45000 で弾かれる。
/// </para>
/// </summary>
public sealed class CreditBlockEntriesRepository
{
    private readonly IConnectionFactory _factory;

    public CreditBlockEntriesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          entry_id                       AS EntryId,
          block_id                       AS BlockId,
          is_broadcast_only              AS IsBroadcastOnly,
          entry_seq                      AS EntrySeq,
          entry_kind                     AS EntryKind,
          person_alias_id                AS PersonAliasId,
          character_alias_id             AS CharacterAliasId,
          raw_character_text             AS RawCharacterText,
          company_alias_id               AS CompanyAliasId,
          logo_id                        AS LogoId,
          raw_text                       AS RawText,
          affiliation_company_alias_id   AS AffiliationCompanyAliasId,
          affiliation_text               AS AffiliationText,
          parallel_with_entry_id         AS ParallelWithEntryId,
          notes                          AS Notes,
          created_at                     AS CreatedAt,
          updated_at                     AS UpdatedAt,
          created_by                     AS CreatedBy,
          updated_by                     AS UpdatedBy
        """;

    /// <summary>主キー（entry_id）で 1 件取得する。</summary>
    public async Task<CreditBlockEntry?> GetByIdAsync(int entryId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_block_entries
            WHERE entry_id = @entryId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CreditBlockEntry>(
            new CommandDefinition(sql, new { entryId }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定ブロックに紐付くエントリ一覧を取得する。
    /// is_broadcast_only → entry_seq 昇順で返すため、円盤・配信用 (フラグ 0) と
    /// 本放送用 (フラグ 1) が連続して並ぶ。クライアント側はこの結果を見て、
    /// 同 (block_id, entry_seq) に並立する 0/1 ペアから「本放送かどうか」で
    /// 表示行を選択する。
    /// </summary>
    public async Task<IReadOnlyList<CreditBlockEntry>> GetByBlockAsync(int blockId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_block_entries
            WHERE block_id = @blockId
            ORDER BY is_broadcast_only, entry_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<CreditBlockEntry>(new CommandDefinition(sql, new { blockId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の entry_id を返す。</summary>
    public async Task<int> InsertAsync(CreditBlockEntry entry, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credit_block_entries
              (block_id, is_broadcast_only, entry_seq, entry_kind,
               person_alias_id, character_alias_id, raw_character_text,
               company_alias_id, logo_id, raw_text,
               affiliation_company_alias_id, affiliation_text,
               parallel_with_entry_id, notes, created_by, updated_by)
            VALUES
              (@BlockId, @IsBroadcastOnly, @EntrySeq, @EntryKind,
               @PersonAliasId, @CharacterAliasId, @RawCharacterText,
               @CompanyAliasId, @LogoId, @RawText,
               @AffiliationCompanyAliasId, @AffiliationText,
               @ParallelWithEntryId, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, entry, cancellationToken: ct));
    }

    /// <summary>更新。is_broadcast_only も含めて差し替える。</summary>
    public async Task UpdateAsync(CreditBlockEntry entry, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_block_entries SET
              block_id                      = @BlockId,
              is_broadcast_only             = @IsBroadcastOnly,
              entry_seq                     = @EntrySeq,
              entry_kind                    = @EntryKind,
              person_alias_id               = @PersonAliasId,
              character_alias_id            = @CharacterAliasId,
              raw_character_text            = @RawCharacterText,
              company_alias_id              = @CompanyAliasId,
              logo_id                       = @LogoId,
              raw_text                      = @RawText,
              affiliation_company_alias_id  = @AffiliationCompanyAliasId,
              affiliation_text              = @AffiliationText,
              parallel_with_entry_id        = @ParallelWithEntryId,
              notes                         = @Notes,
              updated_by                    = @UpdatedBy
            WHERE entry_id = @EntryId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, entry, cancellationToken: ct));
    }

    /// <summary>削除（物理削除）。</summary>
    public async Task DeleteAsync(int entryId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM credit_block_entries WHERE entry_id = @EntryId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { EntryId = entryId }, cancellationToken: ct));
    }

    /// <summary>
    /// 同一 (block_id, is_broadcast_only) グループ内のエントリ群について entry_seq を
    /// 一括再設定する（v1.2.0 工程 B-3 追加。↑↓ ボタンと TreeView DnD の両方から呼ばれる）。
    /// <para>
    /// UNIQUE 制約 (block_id, is_broadcast_only, entry_seq) との一時的衝突を避けるため、
    /// 各対象行に一意な退避値 (30000, 30001, ...) をいったん割り当ててから、
    /// 本来の値で再採番する 2 段階方式。entry_seq は smallint unsigned (0-65535) なので
    /// 退避値 30000 系は十分な逃がし幅。対象行数は呼び出し側で 50 行までを保証する。
    /// </para>
    /// <para>
    /// 並べ替えは「同 block_id × 同 is_broadcast_only」のグループ内に閉じる。
    /// フラグ 0 行とフラグ 1 行は別グループとして扱い、グループ間の並べ替えは UI 側で
    /// ブロックする（is_broadcast_only=0/1 で「対」になる行ペアの対応関係を壊さないため）。
    /// </para>
    /// </summary>
    public async Task BulkUpdateSeqAsync(
        int blockId,
        bool isBroadcastOnly,
        IEnumerable<(int entryId, ushort entrySeq)> updates,
        CancellationToken ct = default)
    {
        if (updates is null) throw new ArgumentNullException(nameof(updates));
        var list = updates.ToList();
        if (list.Count == 0) return;
        if (list.Count > 50)
            throw new ArgumentException("BulkUpdateSeqAsync: 1 ブロック / 1 フラグあたり 50 エントリを超える並べ替えは想定していません。", nameof(updates));

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1 段階目：退避値（30000, 30001, ...）を割り当てて UNIQUE 衝突を回避
            int i = 0;
            foreach (var u in list)
            {
                ushort tempVal = (ushort)(30000 + i);
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_block_entries SET entry_seq = @TempVal WHERE entry_id = @EntryId;",
                    new { TempVal = tempVal, EntryId = u.entryId },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値で再採番
            foreach (var u in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_block_entries SET entry_seq = @EntrySeq WHERE entry_id = @EntryId;",
                    new { EntrySeq = u.entrySeq, EntryId = u.entryId },
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
