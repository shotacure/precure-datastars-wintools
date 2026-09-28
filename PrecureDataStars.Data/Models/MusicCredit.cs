namespace PrecureDataStars.Data.Models;

/// <summary>
/// music_credits テーブルに対応するエンティティモデル（PK: music_credit_id）。
/// 音盤のブックレットに載る音楽クレジット（演奏・コーラス等 / レコーディング / 音盤製作）の 1 名義を表す。
/// 紐付け先は <see cref="TargetKind"/> で決まり、対応する列だけが埋まる：
/// <list type="bullet">
///   <item><description>SONG … <see cref="SongId"/>（伴奏の演奏者など、曲に共通のクレジット）</description></item>
///   <item><description>SONG_RECORDING … <see cref="SongRecordingId"/>（カバー等、録音ごとに違うクレジット）</description></item>
///   <item><description>BGM_SESSION … <see cref="BgmSeriesId"/> + <see cref="BgmSessionNo"/>（劇伴の録音セッション）</description></item>
///   <item><description>PRODUCT … <see cref="ProductCatalogNo"/>（音盤製作のスタッフ、録音の割り振りが決まらない録音側スタッフ）</description></item>
/// </list>
/// 役職（<see cref="RoleCode"/>）の区分は <see cref="Role.MusicCreditGroup"/> で決まる。
/// 盤の印刷表記は名義と別に持つ：名前の表記違い（ローマ字・大文字小文字・誤記）は <see cref="PrintedText"/>、
/// 役職の表記違い（Guiter / Condu 等）は <see cref="RoleLabelText"/>、編成の注記（Tp.3 / 86443 等）は <see cref="EnsembleNote"/>。
/// </summary>
public sealed class MusicCredit
{
    public int MusicCreditId { get; set; }

    /// <summary>紐付け先の種類（<see cref="MusicCreditTargetKinds"/>）。</summary>
    public string TargetKind { get; set; } = "";

    public int? SongId { get; set; }
    public int? SongRecordingId { get; set; }
    public int? BgmSeriesId { get; set; }
    public byte? BgmSessionNo { get; set; }
    public string? ProductCatalogNo { get; set; }

    /// <summary>役職コード（→ roles.role_code）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>紐付け先の中での表示順（1 始まり、盤の並び）。</summary>
    public ushort CreditSeq { get; set; }

    /// <summary>名義の種類（PERSON / CHARACTER / COMPANY / TEXT）。</summary>
    public string EntryKind { get; set; } = "";

    public int? PersonAliasId { get; set; }
    public int? CharacterAliasId { get; set; }
    public int? CompanyAliasId { get; set; }

    /// <summary>TEXT のときの表記。</summary>
    public string? RawText { get; set; }

    /// <summary>盤の印刷表記が名義の表記と違うときの、印刷どおりの表記。</summary>
    public string? PrintedText { get; set; }

    /// <summary><see cref="PrintedText"/> が誤記なら true。</summary>
    public bool IsMisprint { get; set; }

    /// <summary>盤の役職の印刷表記。null なら役職名で出す。</summary>
    public string? RoleLabelText { get; set; }

    /// <summary>編成の注記（Tp.3 / 86443 / ×8 など）。</summary>
    public string? EnsembleNote { get; set; }

    /// <summary>所属の屋号（→ company_aliases.alias_id）。</summary>
    public int? AffiliationCompanyAliasId { get; set; }

    /// <summary>所属の自由記述（屋号が名義マスタに無いとき）。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>直前の名義との区切り（／ , & 、 など）。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>根拠にした盤（→ products.product_catalog_no）。</summary>
    public string? SourceProductCatalogNo { get; set; }

    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>music_credits.target_kind の値。</summary>
public static class MusicCreditTargetKinds
{
    public const string Song = "SONG";
    public const string SongRecording = "SONG_RECORDING";
    public const string BgmSession = "BGM_SESSION";
    public const string Product = "PRODUCT";
}
