using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>クレジット 1 件の Draft 表現（credits 1 行に対応、導入）。</summary>
public sealed class DraftCredit : DraftBase
{
    /// <summary>実体データ（DB 列マッピング用 POCO）。</summary>
    public Credit Entity { get; init; } = new();

    /// <summary>配下のカード（順序保持リスト）。</summary>
    public List<DraftCard> Cards { get; } = new();
}
