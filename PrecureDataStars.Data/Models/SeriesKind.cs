namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_kinds テーブルに対応するマスタモデル（PK: kind_code）。
/// <para>
/// シリーズの作品種別（TV シリーズ / 劇場版 / OVA / 配信短編 等）を定義する。
/// </para>
/// </summary>
public sealed class SeriesKind
{
    /// <summary>種別コード（PK、例: "TV", "MOVIE", "OVA"）。</summary>
    public string KindCode { get; set; } = "";

    /// <summary>日本語表示名（例: "テレビシリーズ"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// クレジットの紐付け先を宣言する区分。
    /// <para>
    /// "SERIES"  ... 当該シリーズ種別ではクレジットがシリーズ単位で 1 セット（映画系を想定）。<br/>
    /// "EPISODE" ... 当該シリーズ種別ではクレジットがエピソードごとに 1 セット（TV シリーズを想定）。
    /// </para>
    /// 既定値は "EPISODE"。映画系（MOVIE / MOVIE_SHORT / SPRING）は "SERIES"。
    /// </summary>
    public string CreditAttachTo { get; set; } = "EPISODE";

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}