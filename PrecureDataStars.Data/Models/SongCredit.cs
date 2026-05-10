
namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_credits テーブルに対応するエンティティモデル
/// （複合 PK: song_id + credit_role + credit_seq、v1.2.3 追加 / v1.3.0 ブラッシュアップ続編で型変更）。
/// <para>
/// 1 曲（<see cref="Song"/>）に対する作家連名（作詞 / 作曲 / 編曲）を順序付きで保持する。
/// 既存の <see cref="Song.LyricistName"/> 等のフリーテキスト列は温存しており、
/// 本テーブルに該当役の行が無い曲では従来通りフリーテキストが表示に使われる
/// （ただしフォールバック処理は SiteBuilder 側で当面実装しない、stage 16 のスコープ判断）。
/// </para>
/// <para>
/// <see cref="PrecedingSeparator"/> は seq>=2 の行で「前 seq との区切り文字」を保持する
/// （例: "・" "＆" "、" " / " " with "）。初出盤の表記をそのまま再現する目的。
/// seq=1 の行では NULL。
/// </para>
/// <para>
/// v1.3.0 ブラッシュアップ続編で <see cref="CreditRole"/> の型を <c>SongCreditRole</c>
/// enum から <c>string</c>（roles.role_code を参照する varchar(32)）に変更した。
/// 役職識別を roles マスタに統一する方針に基づき、3 値固定の enum
/// （LYRICIST/COMPOSER/ARRANGER）を廃止して任意の roles.role_code を受け入れる形にした。
/// 既存値はマイグレーションで LYRICS / COMPOSITION / ARRANGEMENT にリネーム済み。
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
/// song_credits.credit_role の典型値を表す定数群
/// （v1.3.0 ブラッシュアップ続編で <c>SongCreditRole</c> enum から差し替え）。
/// <para>
/// 旧 enum は 3 値固定だったが、現在は roles マスタの任意の role_code を受け入れる
/// ため <c>string</c> 型のフィールドに変更されている。本クラスはコード上で文字列リテラル
/// "LYRICS" 等を散在させないための定数提供場所。
/// </para>
/// <para>
/// 旧 enum のメンバ名と DB 値のマッピング：
/// <list type="bullet">
///   <item>旧 LYRICIST → <see cref="Lyrics"/> ("LYRICS")</item>
///   <item>旧 COMPOSER → <see cref="Composition"/> ("COMPOSITION")</item>
///   <item>旧 ARRANGER → <see cref="Arrangement"/> ("ARRANGEMENT")</item>
/// </list>
/// </para>
/// </summary>
public static class SongCreditRoles
{
    /// <summary>作詞（旧 LYRICIST）。</summary>
    public const string Lyrics = "LYRICS";
    /// <summary>作曲（旧 COMPOSER）。</summary>
    public const string Composition = "COMPOSITION";
    /// <summary>編曲（旧 ARRANGER）。</summary>
    public const string Arrangement = "ARRANGEMENT";
}
