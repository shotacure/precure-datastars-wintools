using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cue_credits テーブル（劇伴の作家連名）の CRUD リポジトリ。
/// <para>
/// 1 cue（series_id + m_no_detail）に対して、credit_role（roles マスタの role_code、典型値は
/// COMPOSITION / ARRANGEMENT）ごとに連名を順序付き（credit_seq）で持つ。
/// </para>
/// <para>
/// credit_role の型は varchar(32)（値は COMPOSITION/ARRANGEMENT 等）。
/// Dapper が直接 string で扱うため、本リポジトリに enum⇔文字列変換ヘルパは持たない。
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
          credit_role          AS CreditRole,
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
    /// 指定 cue の全クレジット行を (role, seq) 順で取得する。
    /// 役の並び順は COMPOSITION → ARRANGEMENT を優先（劇伴の慣習順）、それ以外は role_code 昇順。
    /// </summary>
    public async Task<IReadOnlyList<BgmCueCredit>> GetByCueAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        // 劇伴の慣習順を FIELD でソートに乗せる。並びに無い役職は末尾。
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_credits
            WHERE series_id = @seriesId AND m_no_detail = @m
            ORDER BY FIELD(credit_role,'COMPOSITION','ARRANGEMENT'), credit_role, credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCueCredit>(new CommandDefinition(sql, new { seriesId, m = mNoDetail }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定 cue・役の連名行を seq 順で取得する。</summary>
    public async Task<IReadOnlyList<BgmCueCredit>> GetByCueAndRoleAsync(int seriesId, string mNoDetail, string role, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_credits
            WHERE series_id = @seriesId AND m_no_detail = @m AND credit_role = @role
            ORDER BY credit_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCueCredit>(new CommandDefinition(sql, new { seriesId, m = mNoDetail, role }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定 cue・役の表示文字列を返す。連名は preceding_separator で連結。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int seriesId, string mNoDetail, string role, CancellationToken ct = default)
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
            new CommandDefinition(sql, new { seriesId, m = mNoDetail, role }, cancellationToken: ct))).ToList();
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
              (@SeriesId, @MNoDetail, @CreditRole, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, c, cancellationToken: ct));
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
              AND credit_role  = @CreditRole
              AND credit_seq   = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, c, cancellationToken: ct));
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int seriesId, string mNoDetail, string role, byte creditSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM bgm_cue_credits
            WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail
              AND credit_role = @Role AND credit_seq = @CreditSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            SeriesId = seriesId,
            MNoDetail = mNoDetail,
            Role = role,
            CreditSeq = creditSeq
        }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定 cue・役の連名行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int seriesId, string mNoDetail, string role, IReadOnlyList<BgmCueCredit> credits, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bgm_cue_credits WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail AND credit_role = @Role;",
                new { SeriesId = seriesId, MNoDetail = mNoDetail, Role = role },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var c in credits)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO bgm_cue_credits
                      (series_id, m_no_detail, credit_role, credit_seq, person_alias_id, preceding_separator, notes, created_by, updated_by)
                    VALUES
                      (@SeriesId, @MNoDetail, @Role, @CreditSeq, @PersonAliasId, @PrecedingSeparator, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SeriesId = seriesId,
                        MNoDetail = mNoDetail,
                        Role = role,
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
