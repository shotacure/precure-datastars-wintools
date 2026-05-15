namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_kinds テーブルに対応するマスタモデル（PK: kind_code）。
/// <para>
/// クレジット種別マスタ。<c>credits.credit_kind</c> および
/// <c>part_types.default_credit_kind</c> が参照する。
/// 表示名（オープニングクレジット／エンディングクレジット）の国際化や表示順を
/// 管理可能にするためマスタテーブル化されている。
/// </para>
/// <para>
/// 既定でシードされる行: <c>('OP','オープニングクレジット','Opening Credits',10)</c>,
/// <c>('ED','エンディングクレジット','Ending Credits',20)</c>。
/// </para>
/// </summary>
public sealed class CreditKind
{
    /// <summary>クレジット種別コード（PK、例: "OP", "ED"）。</summary>
    public string KindCode { get; set; } = "";

    /// <summary>日本語表示名（例: "オープニングクレジット"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意、例: "Opening Credits"）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順（小さい値ほど先頭）。</summary>
    public ushort DisplayOrder { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}