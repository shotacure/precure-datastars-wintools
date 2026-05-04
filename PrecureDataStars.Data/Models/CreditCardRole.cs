namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_card_roles テーブルに対応するエンティティモデル（PK: card_role_id）。
/// <para>
/// カード内に登場する役職 1 つ = 1 行。<see cref="Tier"/> = 1（上段）/ 2（下段）+
/// <see cref="OrderInTier"/> でカード内のレイアウト位置を保持する。
/// 横一列だけで構成されるカードは <see cref="Tier"/>=1 のみが立ち、上下 2 段組カードでは
/// 1 と 2 が並ぶ。同一 (card_id, tier, order_in_tier) は UNIQUE。
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

    /// <summary>段内の表示順（1 始まり）。</summary>
    public byte OrderInTier { get; set; }

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    // ── 監査 ──

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
