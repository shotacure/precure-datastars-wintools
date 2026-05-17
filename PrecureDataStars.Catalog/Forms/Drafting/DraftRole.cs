using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// グループ配下の役職（CardRole）の Draft 表現（<c>credit_card_roles</c> 1 行に対応、導入）。
/// </summary>
public sealed class DraftRole : DraftBase
{
    /// <summary>所属するグループ（親オブジェクトへの参照）。FK の解決に必要。</summary>
    public DraftGroup Parent { get; init; } = null!;

    /// <summary>実体データ。card_group_id は保存時に親の RealId を使う。</summary>
    public CreditCardRole Entity { get; init; } = new();

    /// <summary>配下のブロック（順序保持リスト）。</summary>
    public List<DraftBlock> Blocks { get; } = new();
}