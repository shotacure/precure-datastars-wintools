using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// Tier 配下のグループの Draft 表現（<c>credit_card_groups</c> 1 行に対応、導入）。
/// </summary>
public sealed class DraftGroup : DraftBase
{
    /// <summary>所属する Tier（親オブジェクトへの参照）。FK の解決に必要。</summary>
    public DraftTier Parent { get; init; } = null!;

    /// <summary>実体データ。card_tier_id は保存時に親の RealId を使う。</summary>
    public CreditCardGroup Entity { get; init; } = new();

    /// <summary>配下の役職（順序保持リスト）。</summary>
    public List<DraftRole> Roles { get; } = new();
}