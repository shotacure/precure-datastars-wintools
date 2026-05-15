using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// カード配下の Tier の Draft 表現（<c>credit_card_tiers</c> 1 行に対応、導入）。
/// </summary>
public sealed class DraftTier : DraftBase
{
    /// <summary>所属するカード（親オブジェクトへの参照）。FK の解決に必要。</summary>
    public DraftCard Parent { get; init; } = null!;

    /// <summary>実体データ。card_id は保存時に親の RealId を使う。</summary>
    public CreditCardTier Entity { get; init; } = new();

    /// <summary>配下のグループ（順序保持リスト）。</summary>
    public List<DraftGroup> Groups { get; } = new();
}