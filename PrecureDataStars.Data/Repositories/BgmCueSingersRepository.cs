using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cue_singers テーブル（劇伴の歌い手の連名）の CRUD リポジトリ。
/// 1 劇伴音源（series_id + m_no_detail）に対して、役職（SCAT / VOCALS など）ごとに歌い手を順序付き（singer_seq）で持つ。
/// 列の意味は song_recording_singers と同じ（<see cref="SongRecordingSingersRepository"/>）。
/// </summary>
public sealed class BgmCueSingersRepository : RepositoryBase
{
    public BgmCueSingersRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          series_id                  AS SeriesId,
          m_no_detail                AS MNoDetail,
          role_code                  AS RoleCode,
          singer_seq                 AS SingerSeq,
          billing_kind               AS BillingKindStr,
          person_alias_id            AS PersonAliasId,
          character_alias_id         AS CharacterAliasId,
          voice_person_alias_id      AS VoicePersonAliasId,
          slash_person_alias_id      AS SlashPersonAliasId,
          slash_character_alias_id   AS SlashCharacterAliasId,
          preceding_separator        AS PrecedingSeparator,
          affiliation_text           AS AffiliationText,
          expand_unit_members        AS ExpandUnitMembers,
          notes                      AS Notes,
          created_at                 AS CreatedAt,
          updated_at                 AS UpdatedAt,
          created_by                 AS CreatedBy,
          updated_by                 AS UpdatedBy
        """;

    // 役職の並び：歌い手の慣習順（VOCALS → SCAT → BACKING_VOCALS）、並びに無い役職は末尾。
    // FIELD は未マッチで 0 を返すので、0 かどうかを先に並べる。
    private const string RoleOrder = "FIELD(role_code,'VOCALS','SCAT','BACKING_VOCALS') = 0, FIELD(role_code,'VOCALS','SCAT','BACKING_VOCALS'), role_code";

    /// <summary>SQL 戻り値受け取り用 DTO。BillingKind の ENUM 文字列を string で受けてから enum に変換する。</summary>
    private sealed class Row
    {
        public int SeriesId { get; set; }
        public string MNoDetail { get; set; } = "";
        public string RoleCode { get; set; } = SongRecordingSingerRoles.Vocals;
        public byte SingerSeq { get; set; }
        public string BillingKindStr { get; set; } = "";
        public int? PersonAliasId { get; set; }
        public int? CharacterAliasId { get; set; }
        public int? VoicePersonAliasId { get; set; }
        public int? SlashPersonAliasId { get; set; }
        public int? SlashCharacterAliasId { get; set; }
        public string? PrecedingSeparator { get; set; }
        public string? AffiliationText { get; set; }
        public bool ExpandUnitMembers { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        public BgmCueSinger ToModel() => new()
        {
            SeriesId = SeriesId,
            MNoDetail = MNoDetail,
            RoleCode = RoleCode,
            SingerSeq = SingerSeq,
            BillingKind = BillingKindStr == "CHARACTER_WITH_CV" ? SingerBillingKind.CharacterWithCv : SingerBillingKind.Person,
            PersonAliasId = PersonAliasId,
            CharacterAliasId = CharacterAliasId,
            VoicePersonAliasId = VoicePersonAliasId,
            SlashPersonAliasId = SlashPersonAliasId,
            SlashCharacterAliasId = SlashCharacterAliasId,
            PrecedingSeparator = PrecedingSeparator,
            AffiliationText = AffiliationText,
            ExpandUnitMembers = ExpandUnitMembers,
            Notes = Notes,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CreatedBy = CreatedBy,
            UpdatedBy = UpdatedBy
        };
    }

    private static string KindToDb(SingerBillingKind k) => k switch
    {
        SingerBillingKind.Person => "PERSON",
        SingerBillingKind.CharacterWithCv => "CHARACTER_WITH_CV",
        _ => throw new ArgumentOutOfRangeException(nameof(k))
    };

    /// <summary>bgm_cue_singers テーブルの全行を取得する（SiteBuilder が 1 度で読み、(series_id, m_no_detail) 単位にまとめる）。</summary>
    public async Task<IReadOnlyList<BgmCueSinger>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_singers
            ORDER BY series_id, m_no_detail, {RoleOrder}, singer_seq;
            """;

        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>指定劇伴音源の全歌い手行を (役職, seq) 順で取得する。</summary>
    public async Task<IReadOnlyList<BgmCueSinger>> GetByCueAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_singers
            WHERE series_id = @seriesId AND m_no_detail = @m
            ORDER BY {RoleOrder}, singer_seq;
            """;

        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { seriesId, m = mNoDetail }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>
    /// 指定劇伴音源の歌い手の表示文字列を返す（Catalog の画面の表示用）。役職ごとに「役職名：連名」を並べ、役職の間は「 ／ 」でつなぐ。
    /// 連名の組み立ては <see cref="SongRecordingSingersRepository.GetDisplayStringAsync"/> と同じ（PERSON は名義、
    /// CHARACTER_WITH_CV は「キャラ(CV:声優)」、行間は preceding_separator）。行が無ければ空文字。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT
              bcs.role_code                                               AS RoleCode,
              r.name_ja                                                   AS RoleName,
              bcs.billing_kind                                            AS Kind,
              bcs.preceding_separator                                     AS Sep,
              bcs.affiliation_text                                        AS Aff,
              COALESCE(NULLIF(pa.display_text_override, ''),  pa.name)    AS PersonName,
              ca.name                                                     AS CharacterName,
              COALESCE(NULLIF(pav.display_text_override, ''), pav.name)   AS VoiceName,
              COALESCE(NULLIF(spa.display_text_override, ''), spa.name)   AS SlashPersonName,
              sca.name                                                    AS SlashCharacterName
            FROM bgm_cue_singers bcs
            LEFT JOIN roles             r   ON r.role_code  = bcs.role_code
            LEFT JOIN person_aliases    pa  ON pa.alias_id  = bcs.person_alias_id
            LEFT JOIN character_aliases ca  ON ca.alias_id  = bcs.character_alias_id
            LEFT JOIN person_aliases    pav ON pav.alias_id = bcs.voice_person_alias_id
            LEFT JOIN person_aliases    spa ON spa.alias_id = bcs.slash_person_alias_id
            LEFT JOIN character_aliases sca ON sca.alias_id = bcs.slash_character_alias_id
            WHERE bcs.series_id = @seriesId AND bcs.m_no_detail = @m
            ORDER BY FIELD(bcs.role_code,'VOCALS','SCAT','BACKING_VOCALS') = 0, FIELD(bcs.role_code,'VOCALS','SCAT','BACKING_VOCALS'), bcs.role_code, bcs.singer_seq;
            """;

        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<DisplayRow>(
            new CommandDefinition(sql, new { seriesId, m = mNoDetail }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        string? currentRole = null;
        foreach (var r in rows)
        {
            if (r.RoleCode != currentRole)
            {
                if (currentRole is not null) sb.Append(" ／ ");
                sb.Append(r.RoleName ?? r.RoleCode).Append('：');
                currentRole = r.RoleCode;
            }
            else
            {
                sb.Append(r.Sep ?? "");
            }

            if (r.Kind == "PERSON")
            {
                sb.Append(r.PersonName ?? "");
                if (!string.IsNullOrEmpty(r.SlashPersonName)) sb.Append(" / ").Append(r.SlashPersonName);
            }
            else // CHARACTER_WITH_CV
            {
                sb.Append(r.CharacterName ?? "");
                if (!string.IsNullOrEmpty(r.SlashCharacterName)) sb.Append(" / ").Append(r.SlashCharacterName);
                if (!string.IsNullOrEmpty(r.VoiceName)) sb.Append("(CV:").Append(r.VoiceName).Append(')');
            }

            if (!string.IsNullOrEmpty(r.Aff)) sb.Append(' ').Append(r.Aff);
        }
        return sb.ToString();
    }

    private sealed class DisplayRow
    {
        public string RoleCode { get; set; } = "";
        public string? RoleName { get; set; }
        public string Kind { get; set; } = "";
        public string? Sep { get; set; }
        public string? Aff { get; set; }
        public string? PersonName { get; set; }
        public string? CharacterName { get; set; }
        public string? VoiceName { get; set; }
        public string? SlashPersonName { get; set; }
        public string? SlashCharacterName { get; set; }
    }

    /// <summary>
    /// 指定劇伴音源・役職の歌い手行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。指定された役職の行だけを削除・再構築する（他の役職の行はそのまま残る）。
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int seriesId, string mNoDetail, string roleCode, IReadOnlyList<BgmCueSinger> singers, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bgm_cue_singers WHERE series_id = @seriesId AND m_no_detail = @m AND role_code = @role;",
                new { seriesId, m = mNoDetail, role = roleCode }, transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var s in singers)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO bgm_cue_singers
                      (series_id, m_no_detail, role_code, singer_seq, billing_kind,
                       person_alias_id, character_alias_id, voice_person_alias_id,
                       slash_person_alias_id, slash_character_alias_id,
                       preceding_separator, affiliation_text, expand_unit_members, notes,
                       created_by, updated_by)
                    VALUES
                      (@SeriesId, @MNoDetail, @RoleCode, @SingerSeq, @KindStr,
                       @PersonAliasId, @CharacterAliasId, @VoicePersonAliasId,
                       @SlashPersonAliasId, @SlashCharacterAliasId,
                       @PrecedingSeparator, @AffiliationText, @ExpandUnitMembers, @Notes,
                       @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SeriesId = seriesId,
                        MNoDetail = mNoDetail,
                        RoleCode = roleCode,
                        SingerSeq = seq,
                        KindStr = KindToDb(s.BillingKind),
                        s.PersonAliasId,
                        s.CharacterAliasId,
                        s.VoicePersonAliasId,
                        s.SlashPersonAliasId,
                        s.SlashCharacterAliasId,
                        // seq=1 では区切りなし
                        PrecedingSeparator = seq == 1 ? null : s.PrecedingSeparator,
                        s.AffiliationText,
                        s.ExpandUnitMembers,
                        s.Notes,
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
