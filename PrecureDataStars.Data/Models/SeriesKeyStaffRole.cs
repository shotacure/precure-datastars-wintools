namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_key_staff_roles テーブルに対応するエンティティモデル（PK: series_id, role_code）。
/// 作品ごとのメインスタッフとして出す役職と、その並び順・バッジの色を持つ。
/// サイトのシリーズ一覧のカードのバッジと、作品詳細のメインスタッフ（と作品詳細の OGP カード）は、
/// この行のある役職だけを <see cref="DisplayOrder"/> の順に <see cref="BadgeColor"/> の色で出す。
/// 行の無い作品にはメインスタッフを出さない。
/// </summary>
public sealed class SeriesKeyStaffRole
{
    /// <summary>作品 ID（PK 構成、→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>役職コード（PK 構成、→ roles.role_code）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>作品の中の並び順（小さいほど先）。</summary>
    public byte DisplayOrder { get; set; }

    /// <summary>バッジの色（<c>#rrggbb</c> の小文字）。バッジの地・枠・字の色はこの色から作る。</summary>
    public string BadgeColor { get; set; } = "";

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
