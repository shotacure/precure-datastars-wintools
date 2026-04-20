namespace PrecureDataStars.Data.Models;

/// <summary>
/// disc_kinds テーブルに対応するマスタモデル（PK: kind_code）。
/// <para>
/// ディスクの用途種別（本編／特典／カラオケ／インストゥルメンタル 等）を定義する。
/// 物理フォーマット（CD/BD/DVD）ではなく、ディスク役割を表すマスタ。
/// </para>
/// </summary>
public sealed class DiscKind
{
    /// <summary>ディスク用途種別コード（PK、例: "MAIN", "BONUS", "KARAOKE"）。</summary>
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