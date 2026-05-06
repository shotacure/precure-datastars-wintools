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

    /// <summary>
    /// A/B 併記の継続行フラグ（v1.2.2 追加、DB 非永続）。
    /// <para>
    /// 一括入力で行頭 <c>&amp; </c> プレフィクスが付いていたエントリに対して true がセットされる。
    /// 保存フェーズ（<c>CreditSaveService</c>）が同一ブロック内の直前エントリの実 ID を引いて
    /// <see cref="CreditBlockEntry.ParallelWithEntryId"/> に書き込むまでの間、Draft 上で
    /// 「直前エントリと A/B 併記関係を結ぶ」という意図を保持するための一時フィールド。
    /// </para>
    /// <para>
    /// 永続化後はリセットされる（<see cref="ParallelWithEntryId"/> の値そのものは <see cref="Entity"/> 側に
    /// 反映されているため、再ロード時には不要）。<c>RealId</c> 同様、保存ロジック以外は
    /// このフラグの値を直接書き換えない方針。
    /// </para>
    /// </summary>
    public bool RequestParallelWithPrevious { get; set; }
}
