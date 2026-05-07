using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_credits テーブル（歌の作家連名）の CRUD リポジトリ（v1.2.3 追加）。
/// <para>
/// 1 曲（song_id）に対して、credit_role（LYRICIST / COMPOSER / ARRANGER）ごとに
/// 連名を順序付き（credit_seq）で持つ。<see cref="GetDisplayStringAsync"/> は
/// 役単位で連名行を結合し、表示用の 1 行文字列を返す。
/// </para>
/// </summary>
public sealed class SongCreditsRepository
{
    private readonly IConnectionFactory _factory;

    public SongCreditsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          song_id              AS SongId,
          credit_role          AS CreditRoleStr,
          credit_seq           AS CreditSeq,
          person_alias_id      AS PersonAliasId,
          preceding_separator  AS PrecedingSeparator,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy
        """;

    /// <summary>
    /// SQL 戻り値受け取り用の中間 DTO。Dapper は ENUM 文字列を直接 enum にマップしづらいため
    /// 一旦文字列で受けて変換する。
    /// </summary>
    private sealed class Row
    {
        public int SongId { get; set; }
        public string CreditRoleStr { get; set; } = "";
        public byte CreditSeq { get; set; }
        public int PersonAliasId { get; set; }
        public string? PrecedingSeparator { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        public SongCredit ToModel() => new()
        {
            SongId = SongId,
            CreditRole = CreditRoleStr switch
            {
                "LYRICIST" => SongCreditRole.Lyricist,
                "COMPOSER" => SongCreditRole.Composer,
                "ARRANGER" => SongCreditRole.Arranger,
                _ => SongCreditRole.Lyricist
            },
            CreditSeq = CreditSeq,
            PersonAliasId = PersonAliasId,
            PrecedingSeparator = PrecedingSeparator,
            Notes = Notes,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CreatedBy = CreatedBy,
            UpdatedBy = UpdatedBy
        };
    }

    private static string RoleToDb(SongCreditRole r) => r switch
    {
        SongCreditRole.Lyricist => "LYRICIST",
        SongCreditRole.Composer => "COMPOSER",
        SongCreditRole.Arranger => "ARRANGER",
        _ => throw new ArgumentOutOfRangeException(nameof(r))
    };

    /// <summary>指定曲の全クレジット行を (role, seq) 順で取得する。</summary>
    public async Task<IReadOnlyList<SongCredit>> GetBySongAsync(int songId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_credits
            WHERE song_id = @songId
            ORDER BY FIELD(credit_role,'LYRICIST','COMPOSER','ARRANGER'), credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { songId }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>指定曲・役の連名行を seq 順で取得する。</summary>
    public async Task<IReadOnlyList<SongCredit>> GetBySongAndRoleAsync(int songId, SongCreditRole role, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_credits
            WHERE song_id = @songId AND credit_role = @role
            ORDER BY credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { songId, role = RoleToDb(role) }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>
    /// 指定曲・役の表示文字列を返す（v1.2.3）。
    /// 行が無ければ空文字。1 行なら名義の表示名そのまま、2 行以上なら
    /// preceding_separator で連結する。表示名は person_aliases.display_text_override が
    /// あればそちらを、なければ name を使う。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int songId, SongCreditRole role, CancellationToken ct = default)
    {
        // 連名と alias 表示名（display_text_override 優先）を 1 ショットで JOIN 取得する。
        const string sql = """
            SELECT
              sc.credit_seq                                              AS Seq,
              sc.preceding_separator                                     AS Sep,
              COALESCE(NULLIF(pa.display_text_override, ''), pa.name)    AS DisplayName
            FROM song_credits sc
            JOIN person_aliases pa ON pa.alias_id = sc.person_alias_id
            WHERE sc.song_id = @songId
              AND sc.credit_role = @role
            ORDER BY sc.credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<(byte Seq, string? Sep, string DisplayName)>(
            new CommandDefinition(sql, new { songId, role = RoleToDb(role) }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        // seq=1 は区切りなしで先頭、それ以降は preceding_separator + 名前を連結。
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(rows[i].Sep ?? "");
            sb.Append(rows[i].DisplayName);
        }
        return sb.ToString();
    }

    /// <summary>1 行追加（呼び出し側で credit_seq を採番済みの前提）。</summary>
    public async Task InsertAsync(SongCredit c, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_credits
              (song_id, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
            VALUES
              (@SongId, @RoleStr, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            c.SongId,
            RoleStr = RoleToDb(c.CreditRole),
            c.CreditSeq,
            c.PersonAliasId,
            c.PrecedingSeparator,
            c.Notes,
            c.CreatedBy,
            c.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>1 行更新。</summary>
    public async Task UpdateAsync(SongCredit c, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE song_credits SET
              person_alias_id     = @PersonAliasId,
              preceding_separator = @PrecedingSeparator,
              notes               = @Notes,
              updated_by          = @UpdatedBy
            WHERE song_id     = @SongId
              AND credit_role = @RoleStr
              AND credit_seq  = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            c.SongId,
            RoleStr = RoleToDb(c.CreditRole),
            c.CreditSeq,
            c.PersonAliasId,
            c.PrecedingSeparator,
            c.Notes,
            c.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int songId, SongCreditRole role, byte creditSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM song_credits
            WHERE song_id = @SongId AND credit_role = @RoleStr AND credit_seq = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { SongId = songId, RoleStr = RoleToDb(role), CreditSeq = creditSeq }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定曲・役の連名行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int songId, SongCreditRole role, IReadOnlyList<SongCredit> credits, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM song_credits WHERE song_id = @SongId AND credit_role = @RoleStr;",
                new { SongId = songId, RoleStr = RoleToDb(role) },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var c in credits)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO song_credits
                      (song_id, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
                    VALUES
                      (@SongId, @RoleStr, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SongId = songId,
                        RoleStr = RoleToDb(role),
                        CreditSeq = seq,
                        c.PersonAliasId,
                        // seq=1 では preceding_separator は強制 NULL（CHECK にはしていないが整合性維持のため）
                        PrecedingSeparator = seq == 1 ? null : c.PrecedingSeparator,
                        c.Notes,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy
                    },
                    transaction: tx, cancellationToken: ct));
                seq++;
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
