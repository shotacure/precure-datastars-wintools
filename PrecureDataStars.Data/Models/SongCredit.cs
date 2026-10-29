namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_credits テーブルに対応するエンティティモデル
/// （複合 PK: song_id + credit_role + credit_seq）。
/// 1 曲（<see cref="Song"/>）に対する作家連名（作詞 / 作曲 / 編曲）を順序付きで保持する。
/// <see cref="Song.LyricistName"/> 等のフリーテキスト列は温存しており、
/// 本テーブルに該当役の行が無い曲ではフリーテキストが表示に使われる
/// （ただしフォールバック処理は SiteBuilder 側では実装しない）。
/// <see cref="PrecedingSeparator"/> は seq>=2 の行で「前 seq との区切り文字」を保持する
/// （例: "・" "＆" "、" " / " " with "）。初出盤の表記をそのまま再現する目的。
/// seq=1 の行では NULL。
/// <see cref="CreditRole"/> は <c>string</c>（roles.role_code を参照する varchar(32)）。
/// 役職識別を roles マスタに統一する方針に基づき、任意の roles.role_code を受け入れる。
/// 主題歌系の典型値は LYRICS / COMPOSITION / ARRANGEMENT。
/// </summary>
public sealed class SongCredit
{
    /// <summary>対象曲 ID（→ songs.song_id）。</summary>
    public int SongId { get; set; }

    /// <summary>クレジット役の役職コード（→ roles.role_code）。 主題歌系の典型値は LYRICS / COMPOSITION / ARRANGEMENT。</summary>
    public string CreditRole { get; set; } = "";

    /// <summary>同役内での連名表示順（1 始まり）。</summary>
    public byte CreditSeq { get; set; }

    /// <summary>名義参照（→ person_aliases.alias_id）。</summary>
    public int PersonAliasId { get; set; }

    /// <summary>
    /// 盤に印刷された役職の表記（「原詞」など）。役職名（roles.name_ja）と違うときだけ入れる。
    /// その役職の連名のうち <see cref="CreditSeq"/> がいちばん小さい行の値を使う（<see cref="SongCreditRoles.LabelTextOf"/>）。
    /// 曲・盤のトラック・主題歌の行など作家を出す所の表記に使い、集計は役職のまま。
    /// </summary>
    public string? RoleLabelText { get; set; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>
    /// 名義に添える所属の会社名義（→ company_aliases.alias_id）。盤に「岡部啓一(MONACA)」のように
    /// 作家の所属が印刷されているときに持つ。サイトでは名義のあとに「（MONACA）」の形で添え、企業詳細へリンクする。
    /// </summary>
    public int? AffiliationCompanyAliasId { get; set; }

    /// <summary>会社マスタに無い所属の自由記述。<see cref="AffiliationCompanyAliasId"/> があればそちらを使う。</summary>
    public string? AffiliationText { get; set; }

    /// <summary>
    /// 所属の出し方（<see cref="SongCreditAffiliationDisplay"/> の値）。既定は名義のあとに所属を添える <c>NAME_WITH_COMPANY</c>。
    /// <c>COMPANY_ONLY</c> は名義を出さず所属の会社名だけ（盤に「Nostalgic Orchestra」とだけ載っているとき）、
    /// <c>NAME_ONLY</c> は所属を持ったまま名義だけ（盤には名前だけだが実質はその団体の仕事のとき）。
    /// どれもデータは人物＋所属で持ち、人物の集計・企業詳細の所属スタッフは同じに効く。
    /// </summary>
    public string AffiliationDisplay { get; set; } = SongCreditAffiliationDisplay.NameWithCompany;

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>song_credits.affiliation_display の値（所属の出し方）。</summary>
public static class SongCreditAffiliationDisplay
{
    /// <summary>名義のあとに所属を添える（「藤本 記子（Nostalgic Orchestra）」。既定）。</summary>
    public const string NameWithCompany = "NAME_WITH_COMPANY";
    /// <summary>名義を出さず所属の会社名だけを出す（「Nostalgic Orchestra」）。所属が無い行は名義に戻す。</summary>
    public const string CompanyOnly = "COMPANY_ONLY";
    /// <summary>所属を持ったまま名義だけを出す（「藤本 記子」）。</summary>
    public const string NameOnly = "NAME_ONLY";
}

/// <summary>song_credits.credit_role の典型値を表す定数群。 credit_role は roles マスタの任意の role_code を受け入れる <c>string</c> 型。 本クラスはコード上で文字列リテラル "LYRICS" 等を散在させないための定数提供場所。</summary>
public static class SongCreditRoles
{
    /// <summary>作詞。</summary>
    public const string Lyrics = "LYRICS";
    /// <summary>作曲。</summary>
    public const string Composition = "COMPOSITION";
    /// <summary>編曲。</summary>
    public const string Arrangement = "ARRANGEMENT";
    /// <summary>メドレー編曲（メドレーの曲の全体の編曲。編曲とは別に集計する）。</summary>
    public const string MedleyArrangement = "MEDLEY_ARRANGEMENT";
    /// <summary>英訳（日本語の詞を英語に訳した人。画面では「英訳詞」と出ることがある）。</summary>
    public const string LyricsTranslation = "LYRICS_TRANSLATION";

    /// <summary>
    /// 曲の連名行から、指定役職の盤の表記（<see cref="SongCredit.RoleLabelText"/>）を返す。
    /// その役職の行のうち credit_seq がいちばん小さい行の値で、空なら null（呼び出し側で役職名を使う）。
    /// </summary>
    public static string? LabelTextOf(IEnumerable<SongCredit> rows, string roleCode)
        => rows.Where(c => string.Equals(c.CreditRole, roleCode, StringComparison.Ordinal))
               .OrderBy(c => c.CreditSeq)
               .Select(c => c.RoleLabelText)
               .FirstOrDefault() is { Length: > 0 } label ? label : null;
}
