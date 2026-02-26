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
}
