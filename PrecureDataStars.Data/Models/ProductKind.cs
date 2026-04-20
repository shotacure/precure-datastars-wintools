namespace PrecureDataStars.Data.Models;

/// <summary>
/// product_kinds テーブルに対応するマスタモデル（PK: kind_code）。
/// <para>
/// 商品種別（シングル／アルバム／サウンドトラック／ドラマCD 等）を定義する。
/// </para>
/// </summary>
public sealed class ProductKind
{
    /// <summary>商品種別コード（PK、例: "SINGLE", "ALBUM", "OST_BGM"）。</summary>
    public string KindCode { get; set; } = "";

    /// <summary>日本語表示名（例: "シングル"）。</summary>
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