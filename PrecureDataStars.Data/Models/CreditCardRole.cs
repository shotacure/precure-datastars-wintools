namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_card_roles テーブルに対応するエンティティモデル（PK: card_role_id）。
/// <para>
/// カード内に登場する役職 1 つ = 1 行。レイアウト位置は以下の 3 列で表現する:
/// <list type="bullet">
///   <item><description><see cref="Tier"/>=1（上段）/ 2（下段）。横一列のカードは Tier=1 のみが立つ。</description></item>
///   <item><description><see cref="GroupInTier"/>: 同 tier 内のサブグループ番号（1 始まり）。
///   同 tier 内で役職同士が視覚的にサブグループを成すケース（例：[美術監督・色彩設計] と
///   [撮影監督・撮影助手] が同 tier 内で別塊として表示される）を表現する。
///   サブグループが 1 個しかない（従来通りの）カードは <see cref="GroupInTier"/>=1 だけを使う。</description></item>
///   <item><description><see cref="OrderInGroup"/>: グループ内の左右順（1 始まり）。
///   v1.2.0 工程 E で <c>order_in_tier</c> から改名。意味は「同 (card_id, tier, group_in_tier) 内の左右順」。</description></item>
/// </list>
/// 同一 (card_id, tier, group_in_tier, order_in_group) は UNIQUE。
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

    /// <summary>所属するカード ID（→ credit_cards.card_id）。</summary>
    public int CardId { get; set; }

    /// <summary>役職コード（→ roles.role_code、ブランクロール時は NULL）。</summary>
    public string? RoleCode { get; set; }

    /// <summary>段組位置（1 = 上段、2 = 下段）。既定 1。</summary>
    public byte Tier { get; set; } = 1;

    /// <summary>
    /// 同 tier 内のサブグループ番号（1 始まり）。
    /// 視覚的なサブグルーピングが無い場合は 1 を使う。
    /// </summary>
    public byte GroupInTier { get; set; } = 1;

    /// <summary>同 (card_id, tier, group_in_tier) グループ内での左右順（1 始まり）。</summary>
    public byte OrderInGroup { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
