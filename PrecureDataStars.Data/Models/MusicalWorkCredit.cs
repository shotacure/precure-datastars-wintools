namespace PrecureDataStars.Data.Models;

/// <summary>
/// musical_work_credits テーブルに対応するエンティティモデル
/// （複合 PK: work_id + credit_role + credit_seq）。
/// 曲（著作物、<see cref="MusicalWork"/>）の作家の連名を、役職ごとに順序付きで持つ。
/// 役職は歌の作家（<see cref="SongCredit"/>）と同じ役職コード（<see cref="SongCreditRoles"/> の
/// LYRICS / COMPOSITION / ARRANGEMENT など、roles.role_code を参照する文字列）。
/// </summary>
public sealed class MusicalWorkCredit
{
    /// <summary>曲（著作物）（→ musical_works.work_id、複合 PK 第 1 列）。</summary>
    public int WorkId { get; set; }

    /// <summary>役職コード（→ roles.role_code）。典型値は LYRICS / COMPOSITION / ARRANGEMENT。</summary>
    public string CreditRole { get; set; } = "";

    /// <summary>同じ役職の中の連名の順（1 始まり）。</summary>
    public byte CreditSeq { get; set; }

    /// <summary>名義（→ person_aliases.alias_id）。</summary>
    public int PersonAliasId { get; set; }

    /// <summary>seq&gt;=2 の行で、前の名義との区切り文字。seq=1 では NULL（空なら「、」で区切る）。</summary>
    public string? PrecedingSeparator { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
