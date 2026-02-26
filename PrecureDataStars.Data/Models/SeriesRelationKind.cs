namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_relation_kinds テーブルに対応するマスタモデル（PK: relation_code）。
/// <para>
/// シリーズ間の親子関係の種別を定義する。
/// 例: "COFEATURE"（併映）、"SEGMENT"（分割放送の一区画）等。
/// </para>
/// </summary>
public sealed class SeriesRelationKind
{
    /// <summary>関係種別コード（PK、例: "COFEATURE", "SEGMENT"）。</summary>
    public string RelationCode { get; set; } = "";

    /// <summary>日本語表示名（例: "併映"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }
}
