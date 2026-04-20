namespace PrecureDataStars.Data.Models;

/// <summary>
/// song_part_variants テーブルに対応するマスタモデル（PK: variant_code）。
/// <para>
/// 曲のパート種別（通常歌入り／カラオケ／コーラス入り／ガイドメロディ入り 等）を定義する。
/// 1 トラックは (song_recording_id, size_variant_code, part_variant_code) の 3 軸で一意に特定される。
/// </para>
/// </summary>
public sealed class SongPartVariant
{
    /// <summary>パート種別コード（PK、例: "VOCAL", "INST", "INST_CHO"）。</summary>
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