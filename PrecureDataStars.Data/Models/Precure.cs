namespace PrecureDataStars.Data.Models;

/// <summary>
/// precures テーブルに対応するエンティティモデル（PK: precure_id）。
/// <para>
/// プリキュア本体マスタ。1 行 = 1 プリキュア。
/// 名義は <see cref="CharacterAlias"/> を参照する 4 本の FK で表現する：
/// 変身前 (<see cref="PreTransformAliasId"/>) と変身後 (<see cref="TransformAliasId"/>) は必須、
/// 変身後 2 (<see cref="Transform2AliasId"/>) と別形態 (<see cref="AltFormAliasId"/>) は任意。
/// </para>
/// <para>
/// 4 alias が指す character_id は同一でなければならない（「レギュラープリキュアで変身前後で
/// 別キャラになる者はいない」という業務ルール）。整合性は MySQL の BEFORE INSERT/UPDATE
/// トリガ tr_precures_check_character_bi / _bu で SIGNAL によって担保される。
/// </para>
/// <para>
/// 誕生日は <see cref="BirthMonth"/> + <see cref="BirthDay"/> の 2 列に正規化保持。
/// 「m月d日」「Month d」の和文・英文表示は GUI 側で生成する。
/// </para>
/// <para>
/// 肌色は HSL（H 0-360 / S 0-100 / L 0-100）と RGB（R/G/B 0-255）の両方を持つ。
/// 運用安定までは GUI 側で「HSL から復元した色」「RGB から復元した色」を並べて表示し、
/// 両者の整合性（CIE76 ΔE）を画面上で目視確認する設計。
/// </para>
/// </summary>
public sealed class Precure
{
    /// <summary>プリキュアの主キー（AUTO_INCREMENT）。</summary>
    public int PrecureId { get; set; }

    /// <summary>変身前の名義 ID（→ character_aliases.alias_id、必須）。</summary>
    public int PreTransformAliasId { get; set; }

    /// <summary>変身後の名義 ID（→ character_aliases.alias_id、必須）。</summary>
    public int TransformAliasId { get; set; }

    /// <summary>変身後の名前 2（強化形態など。→ character_aliases.alias_id、任意）。</summary>
    public int? Transform2AliasId { get; set; }

    /// <summary>別形態の名前（→ character_aliases.alias_id、任意）。</summary>
    public int? AltFormAliasId { get; set; }

    /// <summary>誕生月（1-12、任意）。</summary>
    public byte? BirthMonth { get; set; }

    /// <summary>誕生日（1-31、任意）。</summary>
    public byte? BirthDay { get; set; }

    /// <summary>標準担当声優の人物 ID（→ persons.person_id、任意）。</summary>
    public int? VoiceActorPersonId { get; set; }

    /// <summary>肌色 H 値（0-360、任意）。</summary>
    public ushort? SkinColorH { get; set; }

    /// <summary>肌色 S 値（0-100、任意）。</summary>
    public byte? SkinColorS { get; set; }

    /// <summary>肌色 L 値（0-100、任意）。</summary>
    public byte? SkinColorL { get; set; }

    /// <summary>肌色 R 値（0-255、任意）。</summary>
    public byte? SkinColorR { get; set; }

    /// <summary>肌色 G 値（0-255、任意）。</summary>
    public byte? SkinColorG { get; set; }

    /// <summary>肌色 B 値（0-255、任意）。</summary>
    public byte? SkinColorB { get; set; }

    /// <summary>通学先の学校名（任意）。</summary>
    public string? School { get; set; }

    /// <summary>クラス（任意。例: "2年2組"、"中等部 1-A"）。</summary>
    public string? SchoolClass { get; set; }

    /// <summary>家業（任意）。</summary>
    public string? FamilyBusiness { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>論理削除フラグ。</summary>
    public bool IsDeleted { get; set; }
}