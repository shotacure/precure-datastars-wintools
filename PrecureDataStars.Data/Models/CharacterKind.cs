namespace PrecureDataStars.Data.Models;

/// <summary>character_kinds テーブルに対応するエンティティモデル（PK: character_kind）。</summary>
public sealed class CharacterKind
{
    /// <summary>区分コード（PK、英数 + アンダースコア）。</summary>
    public string CharacterKindCode { get; set; } = "";

    /// <summary>日本語表示名（必須）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
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
