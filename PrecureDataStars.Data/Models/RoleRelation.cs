namespace PrecureDataStars.Data.Models;

/// <summary>
/// role_relations テーブルに対応するモデル（複合 PK: from_role_code + to_role_code）。
/// 別の役職どうしの関連を 1 行 1 関係で持つ。
/// <list type="bullet">
///   <item><description><see cref="RoleRelationKinds.StepUp"/>（段階）：<see cref="FromRoleCode"/> が前段階、
///     <see cref="ToRoleCode"/> が後段階（例：演出助手 → 演出）。向きを持つ。</description></item>
///   <item><description><see cref="RoleRelationKinds.Parallel"/>（並列）：同じ段階で並んで担う役職（例：絵コンテ ⇔ 演出）。
///     向きを持たないので、role_code の序数比較で小さいほうを <see cref="FromRoleCode"/> に置く。</description></item>
/// </list>
/// 役職の系譜（<see cref="RoleSuccession"/>。同じ役職の名前の移り変わりで、集計を 1 つにまとめる）とは別物で、
/// こちらは集計を分けたまま、人物の歩みと役職詳細の年表に使う。
/// 自己ループ（from = to）は <see cref="Repositories.RoleRelationsRepository"/> の UpsertAsync 入口で弾く。
/// </summary>
public sealed class RoleRelation
{
    /// <summary>段階なら前段階の役職コード。並列なら role_code の小さいほう。</summary>
    public string FromRoleCode { get; set; } = "";

    /// <summary>段階なら後段階の役職コード。並列なら role_code の大きいほう。</summary>
    public string ToRoleCode { get; set; } = "";

    /// <summary>関係の種類（<see cref="RoleRelationKinds"/> の値）。</summary>
    public string RelationKind { get; set; } = RoleRelationKinds.StepUp;

    /// <summary>備考（関係を登録した根拠など）。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary><see cref="RoleRelation.RelationKind"/> の値。</summary>
public static class RoleRelationKinds
{
    /// <summary>段階（from が前段階、to が後段階）。</summary>
    public const string StepUp = "STEP_UP";

    /// <summary>並列（向きなし）。</summary>
    public const string Parallel = "PARALLEL";
}
