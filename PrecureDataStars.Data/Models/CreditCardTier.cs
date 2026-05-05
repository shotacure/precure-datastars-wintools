namespace PrecureDataStars.Data.Models;

/// <summary>
/// credit_card_tiers テーブルに対応するエンティティモデル（PK: card_tier_id）。
/// <para>
/// カード内の Tier（段組）1 つ = 1 行。<see cref="TierNo"/>=1（上段）／2（下段）。
/// v1.2.0 工程 G で追加。それ以前は credit_card_roles 内の <c>tier</c> 列で表現していたが、
/// 「ブランク Tier（役職ゼロの空 Tier）」を表現できないという問題があったため、独立テーブル化した。
/// カードが新規作成されると <see cref="TierNo"/>=1 が 1 行自動投入される運用
/// （CreditCardsRepository.InsertAsync で実装）。
/// </para>
/// </summary>
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
