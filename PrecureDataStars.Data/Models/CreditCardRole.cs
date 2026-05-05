namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_card_roles テーブルに対応するエンティティモデル（PK: card_role_id）。
/// <para>
/// カード内に登場する役職 1 つ = 1 行。レイアウト位置は所属する Group（<see cref="CardGroupId"/>）と
/// グループ内左右順（<see cref="OrderInGroup"/>）で表現する。
/// v1.2.0 工程 G でモデルを大幅刷新：
/// </para>
/// <list type="bullet">
///   <item><description>旧構成: <c>(card_id, tier, group_in_tier, order_in_group)</c> の 4 列複合キー</description></item>
///   <item><description>新構成: <see cref="CardGroupId"/>（→ credit_card_groups.card_group_id）の FK 1 本 + <see cref="OrderInGroup"/></description></item>
/// </list>
/// <para>
/// Card / Tier / Group の階層関係は FK チェーンで一意に決まる
/// （card_role → card_group → card_tier → card）。
/// 同一 (card_group_id, order_in_group) は UNIQUE。
/// </para>
/// <para>
/// <see cref="RoleCode"/> を NULL にできるのは「ブランクロール」用途。
/// 役職ラベルを伴わないロゴ単独表示の枠などに使う。
/// </para>
/// </summary>
public sealed class CreditCardRole
{
    /// <summary>カード内役職の主キー（AUTO_INCREMENT）。</summary>
    public int CardRoleId { get; set; }

    /// <summary>所属する Group ID（→ credit_card_groups.card_group_id）。</summary>
    public int CardGroupId { get; set; }

    /// <summary>役職コード（→ roles.role_code、ブランクロール時は NULL）。</summary>
    public string? RoleCode { get; set; }

    /// <summary>同 Group 内での左右順（1 始まり）。</summary>
    public byte OrderInGroup { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
