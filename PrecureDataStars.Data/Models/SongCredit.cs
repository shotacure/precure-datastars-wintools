namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_credits テーブルに対応するエンティティモデル
/// （複合 PK: song_id + credit_role + credit_seq）。
/// <para>
/// 1 曲（<see cref="Song"/>）に対する作家連名（作詞 / 作曲 / 編曲）を順序付きで保持する。
/// <see cref="Song.LyricistName"/> 等のフリーテキスト列は温存しており、
/// 本テーブルに該当役の行が無い曲ではフリーテキストが表示に使われる
/// （ただしフォールバック処理は SiteBuilder 側では実装しない）。
/// </para>
/// <para>
/// <see cref="PrecedingSeparator"/> は seq>=2 の行で「前 seq との区切り文字」を保持する
/// （例: "・" "＆" "、" " / " " with "）。初出盤の表記をそのまま再現する目的。
/// seq=1 の行では NULL。
/// </para>
/// <para>
/// <see cref="CreditRole"/> は <c>string</c>（roles.role_code を参照する varchar(32)）。
/// 役職識別を roles マスタに統一する方針に基づき、任意の roles.role_code を受け入れる。
/// 主題歌系の典型値は LYRICS / COMPOSITION / ARRANGEMENT。
/// </para>
/// </summary>
public sealed class SongCredit
{
    /// <summary>対象曲 ID（→ songs.song_id）。</summary>
    public int SongId { get; set; }

    /// <summary>
    /// クレジット役の役職コード（→ roles.role_code）。
    /// 主題歌系の典型値は LYRICS / COMPOSITION / ARRANGEMENT。
    /// </summary>
    public string CreditRole { get; set; } = "";

    /// <summary>同役内での連名表示順（1 始まり）。</summary>
    public byte CreditSeq { get; set; }

    /// <summary>名義参照（→ person_aliases.alias_id）。</summary>
    public int PersonAliasId { get; set; }

    /// <summary>seq>=2 の行で、前の seq との区切り文字。seq=1 では NULL。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// song_credits.credit_role の典型値を表す定数群。
/// <para>
/// credit_role は roles マスタの任意の role_code を受け入れる <c>string</c> 型。
/// 本クラスはコード上で文字列リテラル "LYRICS" 等を散在させないための定数提供場所。
/// </para>
/// </summary>
public static class SongCreditRoles
{
    /// <summary>作詞。</summary>
    public const string Lyrics = "LYRICS";
    /// <summary>作曲。</summary>
    public const string Composition = "COMPOSITION";
    /// <summary>編曲。</summary>
    public const string Arrangement = "ARRANGEMENT";
}