namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_credits テーブルに対応するエンティティモデル
/// （複合 PK: song_id + credit_role + credit_seq、v1.2.3 追加）。
/// <para>
/// 1 曲（<see cref="Song"/>）に対する作家連名（作詞 / 作曲 / 編曲）を順序付きで保持する。
/// 既存の <see cref="Song.LyricistName"/> 等のフリーテキスト列は温存しており、
/// 本テーブルに該当役の行が無い曲では従来通りフリーテキストが表示に使われる。
/// </para>
/// <para>
/// <see cref="PrecedingSeparator"/> は seq>=2 の行で「前 seq との区切り文字」を保持する
/// （例: "・" "＆" "、" " / " " with "）。初出盤の表記をそのまま再現する目的。
/// seq=1 の行では NULL。
/// </para>
/// </summary>
public sealed class SongCredit
{
    /// <summary>対象曲 ID（→ songs.song_id）。</summary>
    public int SongId { get; set; }

    /// <summary>クレジット役（LYRICIST / COMPOSER / ARRANGER）。</summary>
    public SongCreditRole CreditRole { get; set; }

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
/// <see cref="SongCredit.CreditRole"/> の種別（v1.2.3 追加）。
/// 文字列表現は DB の ENUM 値と一致させる。
/// </summary>
public enum SongCreditRole
{
    /// <summary>作詞（songs.lyricist_name に相当）。</summary>
    Lyricist = 0,
    /// <summary>作曲（songs.composer_name に相当）。</summary>
    Composer = 1,
    /// <summary>編曲（songs.arranger_name に相当）。</summary>
    Arranger = 2
}
