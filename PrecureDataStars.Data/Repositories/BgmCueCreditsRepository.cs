using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cue_credits テーブル（劇伴の作家連名）の CRUD リポジトリ。
/// 1 cue（series_id + m_no_detail）に対して、credit_role（roles マスタの role_code、典型値は
/// COMPOSITION / ARRANGEMENT）ごとに連名を順序付き（credit_seq）で持つ。
/// credit_role の型は varchar(32)（値は COMPOSITION/ARRANGEMENT 等）。
/// Dapper が直接 string で扱うため、本リポジトリに enum⇔文字列変換ヘルパは持たない。
/// 行は名義の行（entry_kind=PERSON）と名義を持たない文字だけの行（TEXT）。名義の行は歌の作家連名と同じ所属を持てる。
/// 書き込みでは、所属の自由記述が空白だけなら NULL、所属の出し方が空なら既定（NAME_WITH_COMPANY）、
/// 文字の行の表記は前後の空白を除いて保存する。
/// </summary>
public sealed class BgmCueCreditsRepository : RepositoryBase
{
    public BgmCueCreditsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          series_id            AS SeriesId,
          m_no_detail          AS MNoDetail,
          credit_role          AS CreditRole,
          credit_seq           AS CreditSeq,
          entry_kind           AS EntryKind,
          person_alias_id      AS PersonAliasId,
          raw_text             AS RawText,
          preceding_separator  AS PrecedingSeparator,
          affiliation_company_alias_id AS AffiliationCompanyAliasId,
          affiliation_text     AS AffiliationText,
          affiliation_display  AS AffiliationDisplay,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy
        """;

    /// <summary>指定 cue の全クレジット行を (role, seq) 順で取得する。 役の並び順は COMPOSITION → ARRANGEMENT を優先（劇伴の慣習順）、それ以外は role_code 昇順。</summary>
    public async Task<IReadOnlyList<BgmCueCredit>> GetByCueAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        // 劇伴の慣習順を FIELD でソートに乗せる。並びに無い役職は末尾（FIELD は未マッチで 0 を返すので、0 かどうかを先に並べる）。
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_credits
            WHERE series_id = @seriesId AND m_no_detail = @m
            ORDER BY FIELD(credit_role,'COMPOSITION','ARRANGEMENT') = 0, FIELD(credit_role,'COMPOSITION','ARRANGEMENT'), credit_role, credit_seq;
            """;

        return await QueryListAsync<BgmCueCredit>(sql, new { seriesId, m = mNoDetail }, ct).ConfigureAwait(false);
    }

    /// <summary>bgm_cue_credits テーブルの全行を取得する。 SiteBuilder の ProductsGenerator が「BGM トラックごとに <see cref="GetByCueAsync"/> を 順次呼ぶ」N+1 パターンを排除するため、1 度の SQL で全件メモリに載せて、 呼び出し側で (series_id, m_no_detail) 単位にグルーピングする用途で使う。 並びは <see cref="GetByCueAsync"/> と同じく劇伴慣習順（COMPOSITION → ARRANGEMENT → その他 role_code 昇順）を維持する。</summary>
    public async Task<IReadOnlyList<BgmCueCredit>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_credits
            ORDER BY series_id, m_no_detail, FIELD(credit_role,'COMPOSITION','ARRANGEMENT') = 0, FIELD(credit_role,'COMPOSITION','ARRANGEMENT'), credit_role, credit_seq;
            """;

        return await QueryListAsync<BgmCueCredit>(sql, ct: ct).ConfigureAwait(false);
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

        return await QueryListAsync<BgmCueCredit>(sql, new { seriesId, m = mNoDetail, role }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定 cue・役の表示文字列を返す。連名は preceding_separator で連結。
    /// 所属は歌の作家連名（<see cref="SongCreditsRepository.GetDisplayStringAsync"/>）と同じ畳み方で、出し方（affiliation_display）に従い
    /// 名義のあとに「（所属）」を添えるか、所属の会社名だけを出すか（同じ所属が続く連名は 1 回だけ）、名義だけを出す。
    /// 名義を持たない文字だけの行（TEXT）は raw_text をそのまま出す。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int seriesId, string mNoDetail, string role, CancellationToken ct = default)
    {
        // 名義の行は alias 表示名（display_text_override 優先）、文字の行は raw_text。
        // 名義を持たない行も落とさないよう person_aliases は LEFT JOIN する。所属は会社名義を優先し、無ければ自由記述。
        const string sql = """
            SELECT
              bcc.credit_seq                                             AS Seq,
              bcc.preceding_separator                                    AS Sep,
              CASE WHEN bcc.entry_kind = 'TEXT' THEN COALESCE(bcc.raw_text, '')
                   ELSE COALESCE(NULLIF(pa.display_text_override, ''), pa.name, '') END AS DisplayName,
              COALESCE(ca.name, NULLIF(bcc.affiliation_text, ''))        AS Affiliation,
              bcc.affiliation_display                                    AS Display
            FROM bgm_cue_credits bcc
            LEFT JOIN person_aliases pa ON pa.alias_id = bcc.person_alias_id
            LEFT JOIN company_aliases ca ON ca.alias_id = bcc.affiliation_company_alias_id
            WHERE bcc.series_id   = @seriesId
              AND bcc.m_no_detail = @m
              AND bcc.credit_role = @role
            ORDER BY bcc.credit_seq;
            """;

        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<(byte Seq, string? Sep, string DisplayName, string? Affiliation, string Display)>(
            new CommandDefinition(sql, new { seriesId, m = mNoDetail, role }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        return AffiliatedCreditDisplayText.Build(rows
            .Select(r => new AffiliatedCreditDisplayText.Row(r.Sep, r.DisplayName, r.Affiliation, r.Display))
            .ToList());
    }

    /// <summary>1 行追加。</summary>
    public async Task InsertAsync(BgmCueCredit c, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO bgm_cue_credits
              (series_id, m_no_detail, credit_role, credit_seq, entry_kind, person_alias_id, raw_text, preceding_separator,
               affiliation_company_alias_id, affiliation_text, affiliation_display, notes, created_by, updated_by)
            VALUES
              (@SeriesId, @MNoDetail, @CreditRole, @CreditSeq, @EntryKind, @PersonAliasId, @RawText, @PrecedingSeparator,
               @AffiliationCompanyAliasId, @AffiliationText, @AffiliationDisplay, @Notes, @CreatedBy, @UpdatedBy);
            """;

        await ExecuteAsync(sql, ToWriteParams(c, c.SeriesId, c.MNoDetail, c.CreditRole, c.CreditSeq, c.PrecedingSeparator, c.CreatedBy, c.UpdatedBy), ct).ConfigureAwait(false);
    }

    /// <summary>1 行更新。</summary>
    public async Task UpdateAsync(BgmCueCredit c, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE bgm_cue_credits SET
              entry_kind          = @EntryKind,
              person_alias_id     = @PersonAliasId,
              raw_text            = @RawText,
              preceding_separator = @PrecedingSeparator,
              affiliation_company_alias_id = @AffiliationCompanyAliasId,
              affiliation_text    = @AffiliationText,
              affiliation_display = @AffiliationDisplay,
              notes               = @Notes,
              updated_by          = @UpdatedBy
            WHERE series_id    = @SeriesId
              AND m_no_detail  = @MNoDetail
              AND credit_role  = @CreditRole
              AND credit_seq   = @CreditSeq;
            """;

        await ExecuteAsync(sql, ToWriteParams(c, c.SeriesId, c.MNoDetail, c.CreditRole, c.CreditSeq, c.PrecedingSeparator, c.CreatedBy, c.UpdatedBy), ct).ConfigureAwait(false);
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int seriesId, string mNoDetail, string role, byte creditSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM bgm_cue_credits
            WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail
              AND credit_role = @Role AND credit_seq = @CreditSeq;
            """;

        await ExecuteAsync(sql, new
        {
            SeriesId = seriesId,
            MNoDetail = mNoDetail,
            Role = role,
            CreditSeq = creditSeq
        }, ct).ConfigureAwait(false);
    }

    /// <summary>指定 cue・役の連名行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。 1 トランザクションで実行する。</summary>
    public async Task ReplaceAllByRoleAsync(int seriesId, string mNoDetail, string role, IReadOnlyList<BgmCueCredit> credits, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
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
                      (series_id, m_no_detail, credit_role, credit_seq, entry_kind, person_alias_id, raw_text, preceding_separator,
                       affiliation_company_alias_id, affiliation_text, affiliation_display, notes, created_by, updated_by)
                    VALUES
                      (@SeriesId, @MNoDetail, @CreditRole, @CreditSeq, @EntryKind, @PersonAliasId, @RawText, @PrecedingSeparator,
                       @AffiliationCompanyAliasId, @AffiliationText, @AffiliationDisplay, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    ToWriteParams(c, seriesId, mNoDetail, role, seq, seq == 1 ? null : c.PrecedingSeparator, updatedBy, updatedBy),
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

    /// <summary>
    /// 書き込み用のパラメータを組む。行の種類に応じて列をそろえる（名義の行は文字を NULL、文字の行は名義・所属を NULL にし、
    /// 表記の前後の空白を除く）。所属の自由記述が空白だけなら NULL、所属の出し方が空なら既定の名義＋所属にする。
    /// </summary>
    private static object ToWriteParams(BgmCueCredit c, int seriesId, string mNoDetail, string role, byte creditSeq,
        string? precedingSeparator, string? createdBy, string? updatedBy)
    {
        bool isText = c.IsText;
        return new
        {
            SeriesId = seriesId,
            MNoDetail = mNoDetail,
            CreditRole = role,
            CreditSeq = creditSeq,
            EntryKind = isText ? BgmCueCreditEntryKinds.Text : BgmCueCreditEntryKinds.Person,
            PersonAliasId = isText ? null : c.PersonAliasId,
            RawText = isText ? c.RawText?.Trim() : null,
            PrecedingSeparator = precedingSeparator,
            AffiliationCompanyAliasId = isText ? null : c.AffiliationCompanyAliasId,
            AffiliationText = isText || string.IsNullOrWhiteSpace(c.AffiliationText) ? null : c.AffiliationText,
            AffiliationDisplay = string.IsNullOrEmpty(c.AffiliationDisplay) ? SongCreditAffiliationDisplay.NameWithCompany : c.AffiliationDisplay,
            c.Notes,
            CreatedBy = createdBy,
            UpdatedBy = updatedBy
        };
    }
}
