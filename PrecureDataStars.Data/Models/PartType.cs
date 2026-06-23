namespace PrecureDataStars.Data.Models;

/// <summary>part_types テーブルに対応するマスタモデル（PK: part_type）。</summary>
public sealed class PartType
{
    /// <summary>パート種別コード（PK、例: "AVANT", "PART_A"）。</summary>
    public string PartTypeCode { get; set; } = "";

    /// <summary>日本語表示名（例: "アバンタイトル"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順序（TINYINT UNSIGNED、UNIQUE）。小さいほど先頭に表示。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>当該パート種別が「規定で OP/ED クレジットを伴う」かを宣言する区分。</summary>
    public string? DefaultCreditKind { get; set; }

    /// <summary>当該パート種別が「同一エピソード内に 1 回までしか出現しない」かを宣言する。
    /// true=1 話 1 回もの（OP/ED/A・B・C パートなど。重複入力を保存時に拒否する）。
    /// false=同一話に複数回出現してよい（映画予告・各種告知など）。</summary>
    public bool SingletonPerEpisode { get; set; } = true;

    /// <summary>レコード作成者（監査用）。</summary>
    public string? CreatedBy { get; set; }

    /// <summary>レコード最終更新者（監査用）。</summary>
    public string? UpdatedBy { get; set; }
}
