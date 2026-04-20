namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_size_variants テーブルに対応するマスタモデル（PK: variant_code）。
/// <para>
/// 曲のサイズ種別（フルサイズ／TVサイズ／ショート／メドレー 等）を定義する。
/// </para>
/// </summary>
public sealed class SongSizeVariant
{
    /// <summary>サイズ種別コード（PK、例: "FULL", "TV_SIZE", "SHORT"）。</summary>
    public string VariantCode { get; set; } = "";

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