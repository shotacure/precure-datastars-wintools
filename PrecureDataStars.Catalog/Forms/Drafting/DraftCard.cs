using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット内のカードの Draft 表現（<c>credit_cards</c> 1 行に対応、v1.2.0 工程 H-8 で導入）。
/// </summary>
public sealed class DraftCard : DraftBase
{
    /// <summary>所属するクレジット（親オブジェクトへの参照）。FK の解決に必要。</summary>
    public DraftCredit Parent { get; init; } = null!;

    /// <summary>実体データ。credit_id は保存時に親の RealId を使う。</summary>
    public CreditCard Entity { get; init; } = new();

    /// <summary>配下の Tier（順序保持リスト）。</summary>
    public List<DraftTier> Tiers { get; } = new();
}
