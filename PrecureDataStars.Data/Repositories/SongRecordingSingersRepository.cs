using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_recording_singers テーブル（歌唱者連名）の CRUD リポジトリ。
/// <para>
/// 1 録音（song_recording_id）に対して、歌唱者を順序付き（singer_seq）で持つ。
/// billing_kind は PERSON / CHARACTER_WITH_CV の 2 値、スラッシュ並列の相方は
/// slash_*_alias_id 列で表現する（最大 1 個）。
/// </para>
/// <para>
/// role_code 列を持つ。録音に紐付く役職を
/// 「歌（VOCALS、既定）」だけでなく「コーラス（CHORUS）」等まで表すため、
/// roles マスタへの FK を持たせる方針。PK は (song_recording_id, role_code, singer_seq)
/// の 3 列複合に変更。それに伴い <see cref="ReplaceAllAsync"/> の役割が
/// 「録音 1 件分すべて差し替え」から「指定録音 + 役職の連名行を差し替え」に変更され、
/// メソッド名も <see cref="ReplaceAllByRoleAsync"/> にリネームされた。
/// </para>
/// <para>
/// enum⇔文字列変換（KindToDb）は BillingKind に対しては引き続き必要
/// （こちらは enum のまま、roles マスタとは独立した PERSON / CHARACTER_WITH_CV の概念）。
/// </para>
/// </summary>
public sealed class SongRecordingSingersRepository
{
    private readonly IConnectionFactory _factory;

    public SongRecordingSingersRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          song_recording_id          AS SongRecordingId,
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
          notes                      AS Notes,
          created_at                 AS CreatedAt,
          updated_at                 AS UpdatedAt,
          created_by                 AS CreatedBy,
          updated_by                 AS UpdatedBy
        """;

    /// <summary>SQL 戻り値受け取り用 DTO。BillingKind の ENUM 文字列を string で受けてから enum に変換する。</summary>
    private sealed class Row
    {
        public int SongRecordingId { get; set; }
        public string RoleCode { get; set; } = "VOCALS";
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

    /// <summary>
    /// 指定録音の全歌唱者行を (role_code, seq) 順で取得する。
    /// 役の並び順は VOCALS → CHORUS を優先（歌唱の慣習順）、それ以外は role_code 昇順。
    /// </summary>
    public async Task<IReadOnlyList<SongRecordingSinger>> GetByRecordingAsync(int songRecordingId, CancellationToken ct = default)
    {
        // VOCALS が先、CHORUS が次、その他は末尾の慣習順。
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_recording_singers
            WHERE song_recording_id = @id
            ORDER BY FIELD(role_code,'VOCALS','CHORUS'), role_code, singer_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { id = songRecordingId }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>指定録音・役職の歌唱者連名行を seq 順で取得する。</summary>
    public async Task<IReadOnlyList<SongRecordingSinger>> GetByRecordingAndRoleAsync(int songRecordingId, string roleCode, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_recording_singers
            WHERE song_recording_id = @id AND role_code = @role
            ORDER BY singer_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new { id = songRecordingId, role = roleCode }, cancellationToken: ct));
        return rows.Select(r => r.ToModel()).ToList();
    }

    /// <summary>
    /// 指定録音の歌唱者表示文字列を返す。
    /// 行が無ければ空文字。各行ごとに以下の規則で組み立て、行間は preceding_separator で連結する:
    /// <list type="bullet">
    ///   <item>PERSON: 「主名義 ／ 相方名義」（スラッシュ相方があれば「 / 」連結）</item>
    ///   <item>CHARACTER_WITH_CV: 「キャラ ／ 相方キャラ (CV: 声優)」</item>
    /// </list>
    /// 表示名は person_aliases.display_text_override が非空ならそちらを優先する。
    /// <para>
    /// role_code 引数で特定の役職（VOCALS / CHORUS 等）
    /// の連名のみを取り出して文字列化できる。引数省略時（null）は VOCALS のみを対象にする
    /// （既存の GetDisplayString 呼び出しに対する後方互換）。
    /// </para>
    /// </summary>
    public async Task<string> GetDisplayStringAsync(int songRecordingId, string? roleCode = null, CancellationToken ct = default)
    {
        // role_code 未指定時は VOCALS（既定の歌役職）を対象にする。
        string targetRole = roleCode ?? SongRecordingSingerRoles.Vocals;

        // 必要な alias 表示名 5 種を LEFT JOIN で同時取得：
        // 主名義（PERSON のとき） / 主キャラ（CHARACTER_WITH_CV のとき） / CV 名義 /
        // スラッシュ相方（人物側 / キャラ側）。
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
              AND srs.role_code         = @role
            ORDER BY srs.singer_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<DisplayRow>(
            new CommandDefinition(sql, new { id = songRecordingId, role = targetRole }, cancellationToken: ct))).ToList();
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

    /// <summary>
    /// 指定録音・指定役職の連名行を表示 HTML 文字列に整形して返す。
    /// <para>
    /// 名義要素はすべて <paramref name="lookup"/> 経由でリンク化済み HTML 断片を取得し、
    /// 区切り記号・"(CV:"・"/"・所属表記などの固定テキストは適切に HtmlEncode しつつ連結する。
    /// SiteBuilder 側 <see cref="ILookupCache"/> 実装ではリンク付き <c>&lt;a&gt;</c> が、
    /// Catalog 側ではプレーンな HtmlEncode 済みテキストが返るため、出力先に応じて
    /// リンクあり／なしが自動的に切り替わる。
    /// </para>
    /// <para>
    /// 役職コード省略時（null）は VOCALS のみを対象にする（<see cref="GetDisplayStringAsync"/> と同様）。
    /// 行が無ければ空文字を返す。
    /// </para>
    /// </summary>
    /// <param name="songRecordingId">録音 ID。</param>
    /// <param name="roleCode">役職コード（null/省略時は VOCALS）。</param>
    /// <param name="lookup">名義解決インターフェース。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>連名 HTML 文字列。0 件なら空文字。</returns>
    public async Task<string> GetDisplayHtmlAsync(
        int songRecordingId,
        string? roleCode,
        ILookupCache lookup,
        CancellationToken ct = default)
    {
        string targetRole = roleCode ?? SongRecordingSingerRoles.Vocals;

        // 連名行を seq 順で取得。billing_kind と各 alias_id、affiliation_text を集める。
        // 名義の表示名は lookup 側で解決するため、ここでは JOIN しない（lookup のキャッシュを最大活用）。
        const string sql = """
            SELECT
              srs.singer_seq               AS Seq,
              srs.billing_kind             AS Kind,
              srs.preceding_separator      AS Sep,
              srs.affiliation_text         AS Aff,
              srs.person_alias_id          AS PersonAliasId,
              srs.character_alias_id       AS CharacterAliasId,
              srs.voice_person_alias_id    AS VoicePersonAliasId,
              srs.slash_person_alias_id    AS SlashPersonAliasId,
              srs.slash_character_alias_id AS SlashCharacterAliasId
            FROM song_recording_singers srs
            WHERE srs.song_recording_id = @id
              AND srs.role_code         = @role
            ORDER BY srs.singer_seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<HtmlRow>(
            new CommandDefinition(sql, new { id = songRecordingId, role = targetRole }, cancellationToken: ct))).ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (i > 0)
            {
                // 区切り記号は HtmlEncode してから挿入。典型値は短い記号 ("、" "／" ", " 等) で
                // エンコードしても視覚的にはそのまま読める。
                sb.Append(System.Net.WebUtility.HtmlEncode(r.Sep ?? ""));
            }

            if (r.Kind == "PERSON")
            {
                // 個人名義（必要なら / 連結のスラッシュ相方も人物 alias）。
                if (r.PersonAliasId is int pid)
                {
                    var html = await lookup.LookupPersonAliasHtmlAsync(pid).ConfigureAwait(false);
                    sb.Append(html ?? "");
                }
                if (r.SlashPersonAliasId is int spid)
                {
                    sb.Append(" / ");
                    var html = await lookup.LookupPersonAliasHtmlAsync(spid).ConfigureAwait(false);
                    sb.Append(html ?? "");
                }
            }
            else // CHARACTER_WITH_CV
            {
                if (r.CharacterAliasId is int cid)
                {
                    var html = await lookup.LookupCharacterAliasHtmlAsync(cid).ConfigureAwait(false);
                    sb.Append(html ?? "");
                }
                if (r.SlashCharacterAliasId is int scid)
                {
                    sb.Append(" / ");
                    var html = await lookup.LookupCharacterAliasHtmlAsync(scid).ConfigureAwait(false);
                    sb.Append(html ?? "");
                }
                if (r.VoicePersonAliasId is int vpid)
                {
                    // 「(CV:◯◯)」の形式。CV 名義は person_alias なので人物リンク化を使う。
                    sb.Append("(CV:");
                    var html = await lookup.LookupPersonAliasHtmlAsync(vpid).ConfigureAwait(false);
                    sb.Append(html ?? "");
                    sb.Append(')');
                }
            }

            if (!string.IsNullOrEmpty(r.Aff))
            {
                // affiliation_text は所属（"（プロダクション◯◯）" のような）のフリーテキスト。
                // 半角スペース + HtmlEncode で安全に連結する。
                sb.Append(' ').Append(System.Net.WebUtility.HtmlEncode(r.Aff));
            }
        }
        return sb.ToString();
    }

    /// <summary>HTML 版 GetDisplayHtmlAsync 用の SQL 戻り値受け取り DTO。</summary>
    private sealed class HtmlRow
    {
        public byte Seq { get; set; }
        public string Kind { get; set; } = "";
        public string? Sep { get; set; }
        public string? Aff { get; set; }
        public int? PersonAliasId { get; set; }
        public int? CharacterAliasId { get; set; }
        public int? VoicePersonAliasId { get; set; }
        public int? SlashPersonAliasId { get; set; }
        public int? SlashCharacterAliasId { get; set; }
    }

    /// <summary>1 行追加（呼び出し側で role_code と singer_seq を採番済みの前提）。</summary>
    public async Task InsertAsync(SongRecordingSinger s, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_recording_singers
              (song_recording_id, role_code, singer_seq, billing_kind,
               person_alias_id, character_alias_id, voice_person_alias_id,
               slash_person_alias_id, slash_character_alias_id,
               preceding_separator, affiliation_text, notes,
               created_by, updated_by)
            VALUES
              (@SongRecordingId, @RoleCode, @SingerSeq, @KindStr,
               @PersonAliasId, @CharacterAliasId, @VoicePersonAliasId,
               @SlashPersonAliasId, @SlashCharacterAliasId,
               @PrecedingSeparator, @AffiliationText, @Notes,
               @CreatedBy, @UpdatedBy);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            s.SongRecordingId,
            s.RoleCode,
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
              AND role_code         = @RoleCode
              AND singer_seq        = @SingerSeq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            s.SongRecordingId,
            s.RoleCode,
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
    public async Task DeleteAsync(int songRecordingId, string roleCode, byte singerSeq, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM song_recording_singers
            WHERE song_recording_id = @id AND role_code = @role AND singer_seq = @seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { id = songRecordingId, role = roleCode, seq = singerSeq }, cancellationToken: ct));
    }

    /// <summary>
    /// 指定録音・役職の歌唱者行を丸ごと差し替える（既存全削除 → 新セットを seq 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// <para>
    /// role_code 引数を追加。指定された role_code 配下の連名のみを
    /// 削除・再構築する（他の role_code の行はそのまま残る）。
    /// </para>
    /// </summary>
    public async Task ReplaceAllByRoleAsync(int songRecordingId, string roleCode, IReadOnlyList<SongRecordingSinger> singers, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM song_recording_singers WHERE song_recording_id = @id AND role_code = @role;",
                new { id = songRecordingId, role = roleCode }, transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var s in singers)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO song_recording_singers
                      (song_recording_id, role_code, singer_seq, billing_kind,
                       person_alias_id, character_alias_id, voice_person_alias_id,
                       slash_person_alias_id, slash_character_alias_id,
                       preceding_separator, affiliation_text, notes,
                       created_by, updated_by)
                    VALUES
                      (@SongRecordingId, @RoleCode, @SingerSeq, @KindStr,
                       @PersonAliasId, @CharacterAliasId, @VoicePersonAliasId,
                       @SlashPersonAliasId, @SlashCharacterAliasId,
                       @PrecedingSeparator, @AffiliationText, @Notes,
                       @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SongRecordingId = songRecordingId,
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
