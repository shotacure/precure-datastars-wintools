namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_music_classes テーブルに対応するマスタモデル（PK: class_code）。
/// <para>
/// 曲の音楽的分類（OP／ED／挿入歌／キャラクターソング 等）を定義する。
/// </para>
/// </summary>
public sealed class SongMusicClass
{
    /// <summary>音楽種別コード（PK、例: "OP", "ED", "CHARA"）。</summary>
    public string ClassCode { get; set; } = "";

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