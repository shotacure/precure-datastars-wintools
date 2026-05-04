namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_role_format_overrides テーブルに対応するエンティティモデル（PK: series_id, role_code, valid_from）。
/// <para>
/// シリーズ × 役職 × 期間で、当該役職のクレジット書式テンプレを上書きする。
/// たとえば SERIAL ロールで作品ごとの「漫画／コミックス連載」表記の差を吸収するために使う。
/// 同一 (series, role) でシリーズ途中の表記変更も許容するため、PK に
/// <see cref="ValidFrom"/> を含む。<see cref="ValidFrom"/> は NOT NULL（DB 既定 '1900-01-01'）で
/// 「期間境界なし」の場合は既定値を使う運用。
/// </para>
/// <para>
/// 書式解決の優先順は:
/// (1) 当該シリーズ × 役職 × 該当期間の本テーブル行の <see cref="FormatTemplate"/>
/// (2) 役職マスタの <c>roles.default_format_template</c>
/// (3) どちらも無ければ単純連結。
/// </para>
/// </summary>
public sealed class SeriesRoleFormatOverride
{
    /// <summary>シリーズ ID（PK 構成、→ series.series_id）。</summary>
    public int SeriesId { get; set; }

    /// <summary>役職コード（PK 構成、→ roles.role_code）。</summary>
    public string RoleCode { get; set; } = "";

    /// <summary>有効開始日（PK 構成、NOT NULL、既定 '1900-01-01'）。</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>有効終了日（任意）。</summary>
    public DateTime? ValidTo { get; set; }

    /// <summary>書式テンプレ（必須、例: "漫画・{name}"）。</summary>
    public string FormatTemplate { get; set; } = "";

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
