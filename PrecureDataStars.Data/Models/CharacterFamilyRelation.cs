namespace PrecureDataStars.Data.Models;

/// <summary>
/// character_family_relations テーブルに対応するエンティティモデル
/// （複合 PK: character_id + related_character_id + relation_code、v1.2.4 新設）。
/// <para>
/// キャラクター ⇄ キャラクターの家族関係を表す中間表（汎用）。
/// プリキュアに限らず、敵キャラの兄弟関係や、サブキャラのペット関係などにも使える。
/// </para>
/// <para>
/// 1 行が「<see cref="CharacterId"/>（自分）から見た <see cref="RelatedCharacterId"/>（相手）の
/// 続柄が <see cref="RelationCode"/>」を表す。双方向で完全表現するときは、
/// A→B（FATHER）と B→A（SON 相当）の 2 行を別途立てる運用（自動補完はトリガでは行わず、
/// UI 側の明示操作に委ねる設計）。
/// </para>
/// <para>
/// 自分自身（character_id == related_character_id）の関係は CHECK 制約 ck_cfr_no_self で禁止。
/// </para>
/// </summary>
public sealed class CharacterFamilyRelation
{
    /// <summary>自分のキャラクター ID（→ characters.character_id）。複合 PK の 1 つ目。</summary>
    public int CharacterId { get; set; }

    /// <summary>関連キャラクター ID（→ characters.character_id）。複合 PK の 2 つ目。</summary>
    public int RelatedCharacterId { get; set; }

    /// <summary>続柄コード（→ character_relation_kinds.relation_code）。複合 PK の 3 つ目。</summary>
    public string RelationCode { get; set; } = "";

    /// <summary>表示順（character_id 単位。同じ character_id 配下で家族リストの並び順を制御）。</summary>
    public byte? DisplayOrder { get; set; }

    /// <summary>備考（個別エピソードでの登場注釈など）。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
