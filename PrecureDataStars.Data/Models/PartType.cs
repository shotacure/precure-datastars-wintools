namespace PrecureDataStars.Data.Models;

/// <summary>part_types テーブルに対応するマスタモデル（PK: part_type）。</summary>
public sealed class PartType
{
    /// <summary>パート種別コード（PK、例: "AVANT", "PART_A"）。</summary>
    public string PartTypeCode { get; set; } = "";

    /// <summary>日本語表示名（例: "アバンタイトル"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順序（TINYINT UNSIGNED、UNIQUE）。小さいほど先頭に表示。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>当該パート種別が「規定で OP/ED クレジットを伴う」かを宣言する区分。</summary>
    public string? DefaultCreditKind { get; set; }

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}
