using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット 1 件の Draft 表現（<c>credits</c> 1 行に対応、導入）。
/// <para>
/// 編集セッションのルートに位置し、配下の Card / Tier / Group / Role / Block / Entry を
/// オブジェクト参照で抱える。クレジット自体のプロパティ（presentation, part_type, notes 等）も
/// <see cref="Entity"/> 経由で編集対象。
/// </para>
/// <para>
/// 「他話からコピー」機能で新規作成された Draft クレジットは <see cref="DraftBase.State"/> が
/// <see cref="DraftState.Added"/>、配下の全行も <see cref="DraftState.Added"/> となる。
/// </para>
/// </summary>
public sealed class DraftCredit : DraftBase
{
    /// <summary>実体データ（DB 列マッピング用 POCO）。</summary>
    public Credit Entity { get; init; } = new();

    /// <summary>配下のカード（順序保持リスト）。</summary>
    public List<DraftCard> Cards { get; } = new();
}