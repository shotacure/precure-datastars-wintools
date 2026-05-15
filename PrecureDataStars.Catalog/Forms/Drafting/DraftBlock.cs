using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// ブロックの Draft 表現（<c>credit_role_blocks</c> 1 行に対応、導入）。
/// </summary>
public sealed class DraftBlock : DraftBase
{
    /// <summary>所属する役職（親オブジェクトへの参照）。FK の解決に必要。</summary>
    public DraftRole Parent { get; init; } = null!;

    /// <summary>実体データ。card_role_id は保存時に親の RealId を使う。</summary>
    public CreditRoleBlock Entity { get; init; } = new();

    /// <summary>配下のエントリ（順序保持リスト）。</summary>
    public List<DraftEntry> Entries { get; } = new();
}