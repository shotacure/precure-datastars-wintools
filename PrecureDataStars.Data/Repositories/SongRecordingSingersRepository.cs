using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_recording_singers テーブル（歌唱者連名）の CRUD リポジトリ（v1.2.3 追加）。
/// <para>
/// 1 録音（song_recording_id）に対して、歌唱者を順序付き（singer_seq）で持つ。
/// billing_kind は PERSON / CHARACTER_WITH_CV の 2 値、スラッシュ並列の相方は
/// slash_*_alias_id 列で表現する（最大 1 個）。
/// </para>
/// </summary>
public sealed class SongRecordingSingersRepository
{
    private readonly IConnectionFactory _factory;

    public SongRecordingSingersRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          song_recording_id          AS SongRecordingId,
          singer_seq                 AS SingerSeq,
          billing_kind               AS BillingKindStr,
          person_alias_id            AS PersonAliasId,
          character_alias_id         AS CharacterAliasId,
          voice_person_alias_id      AS VoicePersonAliasId,
          slash_person_alias_id      AS SlashPersonAliasId,
          slash_character_alias_id   AS SlashCharacterAliasId,
          preceding_separator        AS PrecedingSeparator,
          affiliation_text           AS AffiliationText,
          notes                      AS Notes,
          created_at                 AS CreatedAt,
          updated_at                 AS UpdatedAt,
          created_by                 AS CreatedBy,
          updated_by                 AS UpdatedBy
        """;

    /// <summary>SQL 戻り値受け取り用 DTO。ENUM 文字列を string で受けてから enum に変換する。</summary>
    private sealed class Row
    {
        public int SongRecordingId { get; set; }
        public byte SingerSeq { get; set; }
        public string BillingKindStr { get; set; } = "";
        public int? PersonAliasId { get; set; }
        public int? CharacterAliasId { get; set; }
        public int? VoicePersonAliasId { get; set; }
        public int? SlashPersonAliasId { get; set; }
        public int? SlashCharacterAliasId { get; set; }
        public string? PrecedingSeparator { get; set; }
        public string? AffiliationText { get; set; }
        public string? Notes { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public string? UpdatedBy { get; set; }

        public SongRecordingSinger ToModel() => new()
        {
            SongRecordingId = SongRecordingId,
            SingerSeq = SingerSeq,
            BillingKind = BillingKindStr == "CHARACTER_WITH_CV" ? SingerBillingKind.CharacterWithCv : SingerBillingKind.Person,
            PersonAliasId = PersonAliasId,
            CharacterAliasId = CharacterAliasId,
            VoicePersonAliasId = VoicePersonAliasId,
            SlashPersonAliasId = SlashPersonAliasId,
            SlashCharacterAliasId = SlashCharacterAliasId,
            PrecedingSeparator = PrecedingSeparator,
            AffiliationText = AffiliationText,
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

    /// <summary>指定録音の全歌唱者行を seq 順で取得する。</summary>
    public async Task<IReadOnlyList<SongRecordingSinger>> GetByRecordingAsync(int songRecordingId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_recording_singers
            WHERE song_recording_id = @id
            ORDER BY singer_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { id = songRecordingId }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>
    /// 指定録音の歌唱者表示文字列を返す（v1.2.3）。
    /// 行が無ければ空文字。各行ごとに以下の規則で組み立て、行間は preceding_separator で連結する:
    /// <list type="bullet">
    ///   <item>PERSON: 「主名義 ／ 相方名義」（スラッシュ相方があれば「 / 」連結）</item>
    ///   <item>CHARACTER_WITH_CV: 「キャラ ／ 相方キャラ (CV: 声優)」</item>
    /// </list>
    /// 表示名は person_aliases.display_text_override が非空ならそちらを優先する。
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int songRecordingId, CancellationToken ct = default)
    {
        // 必要な alias 表示名 5 種を LEFT JOIN で同時取得：
        // 主名義（PERSON のとき） / 主キャラ（CHARACTER_WITH_CV のとき） / CV 名義 /
        // スラッシュ相方（人物側 / キャラ側）。
        // person_aliases だけが v1.2.3 で display_text_override 列を持つ。
        // character_aliases は当面 name のみを使う（必要になれば次バージョンで追加検討）。
        const string sql = """
            SELECT
              srs.singer_seq                                              AS Seq,
              srs.billing_kind                                            AS Kind,
              srs.preceding_separator                                     AS Sep,
              srs.affiliation_text                                        AS Aff,
              COALESCE(NULLIF(pa.display_text_override, ''),  pa.name)    AS PersonName,
              ca.name                                                     AS CharacterName,
              COALESCE(NULLIF(pav.display_text_override, ''), pav.name)   AS VoiceName,
              COALESCE(NULLIF(spa.display_text_override, ''), spa.name)   AS SlashPersonName,
              sca.name                                                    AS SlashCharacterName
            FROM song_recording_singers srs
            LEFT JOIN person_aliases    pa  ON pa.alias_id  = srs.person_alias_id
            LEFT JOIN character_aliases ca  ON ca.alias_id  = srs.character_alias_id
            LEFT JOIN person_aliases    pav ON pav.alias_id = srs.voice_person_alias_id
            LEFT JOIN person_aliases    spa ON spa.alias_id = srs.slash_person_alias_id
            LEFT JOIN character_aliases sca ON sca.alias_id = srs.slash_character_alias_id
            WHERE srs.song_recording_id = @id
            ORDER BY srs.singer_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<DisplayRow>(
            new CommandDefinition(sql, new { id = songRecordingId }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0) sb.Append(r.Sep ?? "");

            if (r.Kind == "PERSON")
            {
                // 個人名義のスラッシュ並列は実用上ほぼ無いが、構造上許容しているため対応。
                sb.Append(r.PersonName ?? "");
                if (!string.IsNullOrEmpty(r.SlashPersonName))
                    sb.Append(" / ").Append(r.SlashPersonName);
            }
            else // CHARACTER_WITH_CV
            {
                sb.Append(r.CharacterName ?? "");
                if (!string.IsNullOrEmpty(r.SlashCharacterName))
                    sb.Append(" / ").Append(r.SlashCharacterName);
                if (!string.IsNullOrEmpty(r.VoiceName))
                    sb.Append("(CV:").Append(r.VoiceName).Append(')');
            }

            if (!string.IsNullOrEmpty(r.Aff))
                sb.Append(' ').Append(r.Aff);
        }
        return sb.ToString();
    }

    private sealed class DisplayRow
    {
        public byte Seq { get; set; }
        public string Kind { get; set; } = "";
        public string? Sep { get; set; }
        public string? Aff { get; set; }
        public string? PersonName { get; set; }
        public string? CharacterName { get; set; }
        public string? VoiceName { get; set; }
        public string? SlashPersonName { get; set; }
        public string? SlashCharacterName { get; set; }
    }

    /// <summary>1 行追加（呼び出し側で singer_seq を採番済みの前提）。</summary>
    public async Task InsertAsync(SongRecordingSinger s, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_recording_singers
              (song_recording_id, singer_seq, billing_kind,
               person_alias_id, character_alias_id, voice_person_alias_id,
               slash_person_alias_id, slash_character_alias_id,
               preceding_separator, affiliation_text, notes,
               created_by, updated_by)
            VALUES
              (@SongRecordingId, @SingerSeq, @KindStr,
               @PersonAliasId, @CharacterAliasId, @VoicePersonAliasId,
               @SlashPersonAliasId, @SlashCharacterAliasId,
               @PrecedingSeparator, @AffiliationText, @Notes,
               @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            s.SongRecordingId,
            s.SingerSeq,
            KindStr = KindToDb(s.BillingKind),
            s.PersonAliasId,
            s.CharacterAliasId,
            s.VoicePersonAliasId,
            s.SlashPersonAliasId,
            s.SlashCharacterAliasId,
            s.PrecedingSeparator,
            s.AffiliationText,
            s.Notes,
            s.CreatedBy,
            s.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>1 行更新。</summary>
    public async Task UpdateAsync(SongRecordingSinger s, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE song_recording_singers SET
              billing_kind             = @KindStr,
              person_alias_id          = @PersonAliasId,
              character_alias_id       = @CharacterAliasId,
              voice_person_alias_id    = @VoicePersonAliasId,
              slash_person_alias_id    = @SlashPersonAliasId,
              slash_character_alias_id = @SlashCharacterAliasId,
              preceding_separator      = @PrecedingSeparator,
              affiliation_text         = @AffiliationText,
              notes                    = @Notes,
              updated_by               = @UpdatedBy
            WHERE song_recording_id = @SongRecordingId
              AND singer_seq        = @SingerSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            s.SongRecordingId,
            s.SingerSeq,
            KindStr = KindToDb(s.BillingKind),
            s.PersonAliasId,
            s.CharacterAliasId,
            s.VoicePersonAliasId,
            s.SlashPersonAliasId,
            s.SlashCharacterAliasId,
            s.PrecedingSeparator,
            s.AffiliationText,
            s.Notes,
            s.UpdatedBy
        }, cancellationToken: ct));
    }

    /// <summary>1 行削除。</summary>
    public async Task DeleteAsync(int songRecordingId, byte singerSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM song_recording_singers
            WHERE song_recording_id = @id AND singer_seq = @seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id = songRecordingId, seq = singerSeq }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定録音の歌唱者行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllAsync(int songRecordingId, IReadOnlyList<SongRecordingSinger> singers, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM song_recording_singers WHERE song_recording_id = @id;",
                new { id = songRecordingId }, transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var s in singers)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO song_recording_singers
                      (song_recording_id, singer_seq, billing_kind,
                       person_alias_id, character_alias_id, voice_person_alias_id,
                       slash_person_alias_id, slash_character_alias_id,
                       preceding_separator, affiliation_text, notes,
                       created_by, updated_by)
                    VALUES
                      (@SongRecordingId, @SingerSeq, @KindStr,
                       @PersonAliasId, @CharacterAliasId, @VoicePersonAliasId,
                       @SlashPersonAliasId, @SlashCharacterAliasId,
                       @PrecedingSeparator, @AffiliationText, @Notes,
                       @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SongRecordingId = songRecordingId,
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
