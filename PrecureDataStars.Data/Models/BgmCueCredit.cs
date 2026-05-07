namespace PrecureDataStars.Data.Models;

/// <summary>
/// bgm_cue_credits テーブルに対応するエンティティモデル
/// （複合 PK: series_id + m_no_detail + credit_role + credit_seq、v1.2.3 追加）。
/// <para>
/// 1 劇伴音源（<see cref="BgmCue"/>）に対する作家連名（作曲 / 編曲）を順序付きで保持する。
/// 既存の <see cref="BgmCue.ComposerName"/> / <see cref="BgmCue.ArrangerName"/> フリーテキスト列は
/// 温存しており、本テーブルに該当役の行が無い cue では従来通りフリーテキストが表示に使われる。
/// </para>
/// <para>
/// 親側の bgm_cues は (series_id, m_no_detail) 複合 PK のため、本テーブルもそれを含む 4 列複合 PK。
/// </para>
/// </summary>
public sealed class BgmCueCredit
{
    /// <summary>所属シリーズ ID（→ bgm_cues.series_id、複合 PK 第 1 列）。</summary>
    public int SeriesId { get; set; }

    /// <summary>M 番号詳細（→ bgm_cues.m_no_detail、複合 PK 第 2 列）。</summary>
    public string MNoDetail { get; set; } = "";

    /// <summary>クレジット役（COMPOSER / ARRANGER）。</summary>
    public BgmCueCreditRole CreditRole { get; set; }

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
/// <see cref="BgmCueCredit.CreditRole"/> の種別（v1.2.3 追加）。
/// 文字列表現は DB の ENUM 値と一致させる。
/// </summary>
public enum BgmCueCreditRole
{
    /// <summary>作曲（bgm_cues.composer_name に相当）。</summary>
    Composer = 0,
    /// <summary>編曲（bgm_cues.arranger_name に相当）。</summary>
    Arranger = 1
}
