using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジットエントリの Draft 表現（<c>credit_block_entries</c> 1 行に対応、v1.2.0 工程 H-8 で導入）。
/// <para>
/// メモリ上の編集対象。実体プロパティ（entry_kind, person_alias_id 等）は既存の
/// <see cref="CreditBlockEntry"/> をそのまま埋め込んで保持する。
/// </para>
/// </summary>
public sealed class DraftEntry : DraftBase
{
    /// <summary>所属するブロック（親オブジェクトへの参照）。FK の解決に必要。</summary>
    public DraftBlock Parent { get; init; } = null!;

    /// <summary>実体データ（DB 列マッピング用 POCO）。block_id は保存時に親の RealId を使う。</summary>
    public CreditBlockEntry Entity { get; init; } = new();
}
