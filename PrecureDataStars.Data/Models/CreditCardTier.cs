namespace PrecureDataStars.Data.Models;

/// <summary>credit_card_tiers テーブルに対応するエンティティモデル（PK: card_tier_id）。</summary>
public sealed class CreditCardTier
{
    /// <summary>
    /// 1 カード内で使える段組番号の上限（DB の ck_card_tier_no と同じ値）。
    /// 2 列に並んだ役職の左の列・右の列と、その下の中央に置かれた役職のように、
    /// 横位置の違う 3 つのまとまりを持つカードがあるため 3 段まで持てる。
    /// </summary>
    public const byte MaxTierNo = 3;

    /// <summary>Tier の主キー（AUTO_INCREMENT）。</summary>
    public int CardTierId { get; set; }

    /// <summary>所属するカード ID（→ credit_cards.card_id）。</summary>
    public int CardId { get; set; }

    /// <summary>段組番号（1 始まり、<see cref="MaxTierNo"/> まで）。</summary>
    public byte TierNo { get; set; } = 1;

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
