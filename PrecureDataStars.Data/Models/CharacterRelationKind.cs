namespace PrecureDataStars.Data.Models;

/// <summary>
/// character_relation_kinds テーブルに対応するマスタモデル（PK: relation_code、v1.2.4 新設）。
/// <para>
/// キャラクター続柄マスタ。「自分（character_id）から見た相手（related_character_id）の続柄」を
/// 表すコードを保持する。<see cref="CharacterFamilyRelation"/> から FK で参照される。
/// </para>
/// <para>
/// 初期投入される 13 種：FATHER / MOTHER / BROTHER_OLDER / BROTHER_YOUNGER /
/// SISTER_OLDER / SISTER_YOUNGER / GRANDFATHER / GRANDMOTHER / UNCLE / AUNT /
/// COUSIN / PET / OTHER_FAMILY。プリキュア作品で頻出する続柄を網羅。
/// 業務側で必要があれば後から追加・並べ替え可能。
/// </para>
/// </summary>
public sealed class CharacterRelationKind
{
    /// <summary>続柄コード（PK、英数 + アンダースコア。例: "FATHER", "BROTHER_OLDER"）。</summary>
    public string RelationCode { get; set; } = "";

    /// <summary>日本語表示名（必須。例: "父", "兄"）。</summary>
    public string NameJa { get; set; } = "";

    /// <summary>英語表示名（任意。例: "Father", "Older Brother"）。</summary>
    public string? NameEn { get; set; }

    /// <summary>表示順（10 単位飛び番、1 始まり）。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
