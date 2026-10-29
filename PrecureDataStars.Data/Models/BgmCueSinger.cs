namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_cue_singers テーブルに対応するエンティティモデル
/// （複合 PK: series_id + m_no_detail + role_code + singer_seq）。
/// 1 劇伴音源（<see cref="BgmCue"/>）の歌い手（スキャットなど、劇伴の中で声を出す人・キャラ）の連名を順序付きで保持する。
/// 列の意味は歌の録音の歌唱者（<see cref="SongRecordingSinger"/>）と同じで、
/// <see cref="BillingKind"/> = PERSON は人物名義、CHARACTER_WITH_CV はキャラ名義 ＋ 声優名義（「キャラ(CV:声優)」）。
/// 表示や参加者の展開は歌の録音の歌唱者と共通の処理で行うため、<see cref="ToSingerRow"/> で歌唱者行の形に写して渡す。
/// </summary>
public sealed class BgmCueSinger
{
    /// <summary>所属シリーズ ID（→ bgm_cues.series_id、複合 PK 第 1 列）。</summary>
    public int SeriesId { get; set; }

    /// <summary>M 番号詳細（→ bgm_cues.m_no_detail、複合 PK 第 2 列）。</summary>
    public string MNoDetail { get; set; } = "";

    /// <summary>役職コード（→ roles.role_code、複合 PK 第 3 列）。劇伴では <c>SCAT</c>（スキャット）か <c>VOCALS</c>（歌）。</summary>
    public string RoleCode { get; set; } = SongRecordingSingerRoles.Vocals;

    /// <summary>連名表示順（複合 PK 第 4 列、1 始まり）。役職コードごとに独立採番。</summary>
    public byte SingerSeq { get; set; }

    /// <summary>歌唱クレジット種別（PERSON / CHARACTER_WITH_CV）。</summary>
    public SingerBillingKind BillingKind { get; set; }

    /// <summary>主名義（PERSON）：人物名義参照。</summary>
    public int? PersonAliasId { get; set; }

    /// <summary>主名義（CHARACTER_WITH_CV）：キャラクター名義参照。</summary>
    public int? CharacterAliasId { get; set; }

    /// <summary>CV（声優）の人物名義参照（CHARACTER_WITH_CV のとき必須）。</summary>
    public int? VoicePersonAliasId { get; set; }

    /// <summary>スラッシュ並列の相方（PERSON 主名義側）。最大 1 個。</summary>
    public int? SlashPersonAliasId { get; set; }

    /// <summary>スラッシュ並列の相方（CHARACTER_WITH_CV 主名義側）。最大 1 個。</summary>
    public int? SlashCharacterAliasId { get; set; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>所属表記（任意）。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>PERSON 行の名義がユニット名義のとき、表示でメンバーを括弧書きで展開するか。</summary>
    public bool ExpandUnitMembers { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// 歌の録音の歌唱者行（<see cref="SongRecordingSinger"/>）の形に写す。
    /// 表示（キャラ(CV:声優)・スラッシュ・ユニットのメンバー展開・所属）と参加者の展開を歌唱者と共通の処理に通すために使う。
    /// 録音 ID は持たないので 0。
    /// </summary>
    public SongRecordingSinger ToSingerRow() => new()
    {
        SongRecordingId = 0,
        RoleCode = RoleCode,
        SingerSeq = SingerSeq,
        BillingKind = BillingKind,
        PersonAliasId = PersonAliasId,
        CharacterAliasId = CharacterAliasId,
        VoicePersonAliasId = VoicePersonAliasId,
        SlashPersonAliasId = SlashPersonAliasId,
        SlashCharacterAliasId = SlashCharacterAliasId,
        PrecedingSeparator = PrecedingSeparator,
        AffiliationText = AffiliationText,
        ExpandUnitMembers = ExpandUnitMembers,
        Notes = Notes
    };

    /// <summary>歌唱者行（Catalog の歌唱者の編集ダイアログの結果）から、指定の劇伴音源・役職の行を作る。</summary>
    public static BgmCueSinger FromSingerRow(int seriesId, string mNoDetail, string roleCode, SongRecordingSinger s) => new()
    {
        SeriesId = seriesId,
        MNoDetail = mNoDetail,
        RoleCode = roleCode,
        SingerSeq = s.SingerSeq,
        BillingKind = s.BillingKind,
        PersonAliasId = s.PersonAliasId,
        CharacterAliasId = s.CharacterAliasId,
        VoicePersonAliasId = s.VoicePersonAliasId,
        SlashPersonAliasId = s.SlashPersonAliasId,
        SlashCharacterAliasId = s.SlashCharacterAliasId,
        PrecedingSeparator = s.PrecedingSeparator,
        AffiliationText = s.AffiliationText,
        ExpandUnitMembers = s.ExpandUnitMembers,
        Notes = s.Notes
    };
}
