namespace PrecureDataStars.Data.Models;

/// <summary>track_content_kinds テーブルに対応するマスタモデル（PK: kind_code）。</summary>
public sealed class TrackContentKind
{
    /// <summary>トラック内容コード（PK）。</summary>
    public string KindCode { get; set; } = "";

    /// <summary>日本語表示名。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>画面表示順（小さい値が先頭。UNIQUE）。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}
