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
          entry_seq                      AS EntrySeq,
          entry_kind                     AS EntryKind,
          person_alias_id                AS PersonAliasId,
          character_alias_id             AS CharacterAliasId,
          raw_character_text             AS RawCharacterText,
          company_alias_id               AS CompanyAliasId,
          logo_id                        AS LogoId,
          song_recording_id              AS SongRecordingId,
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

    /// <summary>指定ブロックに紐付くエントリ一覧を取得する（entry_seq 昇順）。</summary>
    public async Task<IReadOnlyList<CreditBlockEntry>> GetByBlockAsync(int blockId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credit_block_entries
            WHERE block_id = @blockId
            ORDER BY entry_seq;
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
              (block_id, entry_seq, entry_kind,
               person_alias_id, character_alias_id, raw_character_text,
               company_alias_id, logo_id, song_recording_id, raw_text,
               affiliation_company_alias_id, affiliation_text,
               parallel_with_entry_id, notes, created_by, updated_by)
            VALUES
              (@BlockId, @EntrySeq, @EntryKind,
               @PersonAliasId, @CharacterAliasId, @RawCharacterText,
               @CompanyAliasId, @LogoId, @SongRecordingId, @RawText,
               @AffiliationCompanyAliasId, @AffiliationText,
               @ParallelWithEntryId, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, entry, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(CreditBlockEntry entry, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credit_block_entries SET
              block_id                      = @BlockId,
              entry_seq                     = @EntrySeq,
              entry_kind                    = @EntryKind,
              person_alias_id               = @PersonAliasId,
              character_alias_id            = @CharacterAliasId,
              raw_character_text            = @RawCharacterText,
              company_alias_id              = @CompanyAliasId,
              logo_id                       = @LogoId,
              song_recording_id             = @SongRecordingId,
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
}
