namespace PrecureDataStars.Data.Models;

/// <summary>credit_card_tiers テーブルに対応するエンティティモデル（PK: card_tier_id）。</summary>
public sealed class CreditCardTier
{
    /// <summary>Tier の主キー（AUTO_INCREMENT）。</summary>
    public int CardTierId { get; set; }

    /// <summary>所属するカード ID（→ credit_cards.card_id）。</summary>
    public int CardId { get; set; }

    /// <summary>段組番号（1=上段、2=下段）。</summary>
    public byte TierNo { get; set; } = 1;

    /// <summary>備考。</summary>
    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
