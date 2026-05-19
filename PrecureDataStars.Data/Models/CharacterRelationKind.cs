namespace PrecureDataStars.Data.Models;

/// <summary>character_relation_kinds テーブルに対応するマスタモデル（PK: relation_code）。</summary>
public sealed class CharacterRelationKind
{
    /// <summary>続柄コード（PK、英数 + アンダースコア。例: "FATHER", "BROTHER_OLDER"）。</summary>
    public string RelationCode { get; set; } = "";

    /// <summary>日本語表示名（必須。例: "父", "兄"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意。例: "Father", "Older Brother"）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順（10 単位飛び番、1 始まり）。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
