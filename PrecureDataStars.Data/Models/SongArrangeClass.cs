namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_arrange_classes テーブルに対応するマスタモデル（PK: class_code）。
/// <para>
/// 曲のアレンジ種別（オリジナル／アコースティック／オーケストラ／リミックス／カバー 等）を定義する。
/// </para>
/// </summary>
public sealed class SongArrangeClass
{
    /// <summary>アレンジ種別コード（PK、例: "ORIGINAL", "ACOUSTIC", "REMIX"）。</summary>
    public string ClassCode { get; set; } = "";

    /// <summary>日本語表示名。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>画面表示順（小さい値が先頭。UNIQUE）。</summary>
    public byte? DisplayOrder { get; set; }
}
