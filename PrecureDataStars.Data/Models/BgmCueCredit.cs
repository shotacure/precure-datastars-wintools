namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_cue_credits テーブルに対応するエンティティモデル
/// （複合 PK: series_id + m_no_detail + credit_role + credit_seq、 / 型変更）。
/// 1 劇伴音源（<see cref="BgmCue"/>）に対する作家連名（作曲 / 編曲）を順序付きで保持する。
/// 既存の <see cref="BgmCue.ComposerName"/> / <see cref="BgmCue.ArrangerName"/> フリーテキスト列は
/// 温存しており、本テーブルに該当役の行が無い cue では従来通りフリーテキストが表示に使われる
/// （フォールバック処理は SiteBuilder 側では実装しない）。
/// 親側の bgm_cues は (series_id, m_no_detail) 複合 PK のため、本テーブルもそれを含む 4 列複合 PK。
/// <see cref="CreditRole"/> の型を <c>BgmCueCreditRole</c>
/// 役職は <c>string</c>（roles.role_code を参照する varchar(32)）で保持する。
/// 既存値はマイグレーションで COMPOSITION / ARRANGEMENT にリネーム済み。
/// 行は名義の行（<see cref="EntryKind"/>=PERSON）か、名義を持たない文字だけの行（TEXT、「不詳」など人物にできない作家の表記）。
/// 名義の行は歌の作家連名（<see cref="SongCredit"/>）と同じく所属（会社名義または自由記述）と所属の出し方を持てる。
/// entry_kind と名義・文字・所属の列の整合性は DB のトリガーで担保する
/// （PERSON は名義必須で文字なし、TEXT は文字必須で名義・所属なし）。
/// </summary>
public sealed class BgmCueCredit : IAffiliatedPersonCredit
{
    /// <summary>所属シリーズ ID（→ bgm_cues.series_id、複合 PK 第 1 列）。</summary>
    public int SeriesId { get; set; }

    /// <summary>M 番号詳細（→ bgm_cues.m_no_detail、複合 PK 第 2 列）。</summary>
    public string MNoDetail { get; set; } = "";

    /// <summary>クレジット役の役職コード（→ roles.role_code）。 劇伴の典型値は COMPOSITION / ARRANGEMENT。</summary>
    public string CreditRole { get; set; } = "";

    /// <summary>同役内での連名表示順（1 始まり）。</summary>
    public byte CreditSeq { get; set; }

    /// <summary>行の種類（<see cref="BgmCueCreditEntryKinds"/> の値）。既定は名義の行 <c>PERSON</c>。</summary>
    public string EntryKind { get; set; } = BgmCueCreditEntryKinds.Person;

    /// <summary>名義参照（→ person_aliases.alias_id）。PERSON の行で必須、TEXT の行では null。</summary>
    public int? PersonAliasId { get; set; }

    /// <summary>TEXT の行の表記（「不詳」など）。PERSON の行では null。</summary>
    public string? RawText { get; set; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>
    /// 名義に添える所属の会社名義（→ company_aliases.alias_id）。サントラに「福富雅之(Nostalgic Orchestra)」のように
    /// 作家の所属が印刷されているときに持つ。TEXT の行では null。出し方は歌の作家連名と同じ。
    /// </summary>
    public int? AffiliationCompanyAliasId { get; set; }

    /// <summary>会社マスタに無い所属の自由記述。<see cref="AffiliationCompanyAliasId"/> があればそちらを使う。TEXT の行では null。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>
    /// 所属の出し方（<see cref="SongCreditAffiliationDisplay"/> の値）。既定は名義のあとに所属を添える <c>NAME_WITH_COMPANY</c>。
    /// <c>COMPANY_ONLY</c> は名義を出さず所属の会社名だけ、<c>NAME_ONLY</c> は所属を持ったまま名義だけ。
    /// </summary>
    public string AffiliationDisplay { get; set; } = SongCreditAffiliationDisplay.NameWithCompany;

    /// <summary>名義を持たない文字だけの行か（<see cref="EntryKind"/> が TEXT）。</summary>
    public bool IsText => string.Equals(EntryKind, BgmCueCreditEntryKinds.Text, StringComparison.Ordinal);

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>bgm_cue_credits.entry_kind の値（行の種類）。</summary>
public static class BgmCueCreditEntryKinds
{
    /// <summary>名義（person_alias_id）の行。</summary>
    public const string Person = "PERSON";
    /// <summary>名義を持たない文字だけの行（raw_text。「不詳」「イギリス民謡」など、人物にできない作家の表記）。</summary>
    public const string Text = "TEXT";
}

/// <summary>bgm_cue_credits.credit_role の典型値を表す定数群。 credit_role は roles マスタの任意の role_code を受け入れる <c>string</c> 型。 本クラスはコード上で文字列リテラル "COMPOSITION" 等を散在させないための定数提供場所。</summary>
public static class BgmCueCreditRoles
{
    /// <summary>作曲。</summary>
    public const string Composition = "COMPOSITION";
    /// <summary>編曲。</summary>
    public const string Arrangement = "ARRANGEMENT";
}
