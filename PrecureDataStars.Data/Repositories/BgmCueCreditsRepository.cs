using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cue_credits テーブル（劇伴の作家連名）の CRUD リポジトリ（v1.2.3 追加）。
/// <para>
/// 1 cue（series_id + m_no_detail）に対して、credit_role（COMPOSER / ARRANGER）ごとに
/// 連名を順序付き（credit_seq）で持つ。
/// </para>
/// </summary>
public sealed class BgmCueCreditsRepository
{
    private readonly IConnectionFactory _factory;

    public BgmCueCreditsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          series_id            AS SeriesId,
          m_no_detail          AS MNoDetail,
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

    private sealed class Row
    {
        public int SeriesId { get; set; }
        public string MNoDetail { get; set; } = "";
        public string CreditRoleStr { get; set; } = "";
        public byte CreditSeq { get; set; }
        public int PersonAliasId { get; set; }
        public string? PrecedingSeparator { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        public BgmCueCredit ToModel() => new()
        {
            SeriesId = SeriesId,
            MNoDetail = MNoDetail,
            CreditRole = CreditRoleStr == "ARRANGER" ? BgmCueCreditRole.Arranger : BgmCueCreditRole.Composer,
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

    private static string RoleToDb(BgmCueCreditRole r) => r switch
    {
        BgmCueCreditRole.Composer => "COMPOSER",
        BgmCueCreditRole.Arranger => "ARRANGER",
        _ => throw new ArgumentOutOfRangeException(nameof(r))
    };

    /// <summary>指定 cue の全クレジット行を (role, seq) 順で取得する。</summary>
    public async Task<IReadOnlyList<BgmCueCredit>> GetByCueAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_credits
            WHERE series_id = @seriesId AND m_no_detail = @m
            ORDER BY FIELD(credit_role,'COMPOSER','ARRANGER'), credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { seriesId, m = mNoDetail }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>指定 cue・役の連名行を seq 順で取得する。</summary>
    public async Task<IReadOnlyList<BgmCueCredit>> GetByCueAndRoleAsync(int seriesId, string mNoDetail, BgmCueCreditRole role, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_credits
            WHERE series_id = @seriesId AND m_no_detail = @m AND credit_role = @role
            ORDER BY credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { seriesId, m = mNoDetail, role = RoleToDb(role) }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>
    /// 指定 cue・役の表示文字列を返す（v1.2.3）。連名は preceding_separator で連結。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int seriesId, string mNoDetail, BgmCueCreditRole role, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              bcc.credit_seq                                             AS Seq,
              bcc.preceding_separator                                    AS Sep,
              COALESCE(NULLIF(pa.display_text_override, ''), pa.name)    AS DisplayName
            FROM bgm_cue_credits bcc
            JOIN person_aliases pa ON pa.alias_id = bcc.person_alias_id
            WHERE bcc.series_id   = @seriesId
              AND bcc.m_no_detail = @m
              AND bcc.credit_role = @role
            ORDER BY bcc.credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<(byte Seq, string? Sep, string DisplayName)>(
            new CommandDefinition(sql, new { seriesId, m = mNoDetail, role = RoleToDb(role) }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(rows[i].Sep ?? "");
            sb.Append(rows[i].DisplayName);
        }
        return sb.ToString();
    }

    /// <summary>1 行追加。</summary>
    public async Task InsertAsync(BgmCueCredit c, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO bgm_cue_credits
              (series_id, m_no_detail, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
            VALUES
              (@SeriesId, @MNoDetail, @RoleStr, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            c.SeriesId,
            c.MNoDetail,
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
    public async Task UpdateAsync(BgmCueCredit c, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE bgm_cue_credits SET
              person_alias_id     = @PersonAliasId,
              preceding_separator = @PrecedingSeparator,
              notes               = @Notes,
              updated_by          = @UpdatedBy
            WHERE series_id    = @SeriesId
              AND m_no_detail  = @MNoDetail
              AND credit_role  = @RoleStr
              AND credit_seq   = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            c.SeriesId,
            c.MNoDetail,
            RoleStr = RoleToDb(c.CreditRole),
            c.CreditSeq,
            c.PersonAliasId,
            c.PrecedingSeparator,
            c.Notes,
            c.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int seriesId, string mNoDetail, BgmCueCreditRole role, byte creditSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM bgm_cue_credits
            WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail
              AND credit_role = @RoleStr AND credit_seq = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            SeriesId = seriesId,
            MNoDetail = mNoDetail,
            RoleStr = RoleToDb(role),
            CreditSeq = creditSeq
        }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定 cue・役の連名行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int seriesId, string mNoDetail, BgmCueCreditRole role, IReadOnlyList<BgmCueCredit> credits, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bgm_cue_credits WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail AND credit_role = @RoleStr;",
                new { SeriesId = seriesId, MNoDetail = mNoDetail, RoleStr = RoleToDb(role) },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var c in credits)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO bgm_cue_credits
                      (series_id, m_no_detail, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
                    VALUES
                      (@SeriesId, @MNoDetail, @RoleStr, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SeriesId = seriesId,
                        MNoDetail = mNoDetail,
                        RoleStr = RoleToDb(role),
                        CreditSeq = seq,
                        c.PersonAliasId,
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
