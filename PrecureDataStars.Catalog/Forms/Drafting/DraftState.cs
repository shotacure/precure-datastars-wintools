namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// Draft オブジェクト（クレジット編集セッション内のメモリ上行）の状態（v1.2.0 工程 H-8 で導入）。
/// <para>
/// クレジット編集を「即時 DB 反映」から「メモリ上で編集 → 保存ボタンで一括確定」に切り替える
/// 仕組みの一部。各 Draft 行はこの状態フラグを持ち、保存時に状態に応じて INSERT / UPDATE / DELETE が発行される。
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Unchanged"/>: DB と完全一致（読み込んだまま、編集なし）。保存時は何もしない。</description></item>
///   <item><description><see cref="Modified"/>: 既存行を編集した。保存時に UPDATE。</description></item>
///   <item><description><see cref="Added"/>: メモリ上で新規作成した（DB にまだ存在しない）。保存時に INSERT、戻りの自動採番 ID で更新する。</description></item>
///   <item><description><see cref="Deleted"/>: 削除マーク（保存時に DELETE）。Added だった行が削除された場合は単に Draft から消す（DB 操作不要）。</description></item>
/// </list>
/// </summary>
public enum DraftState
{
    /// <summary>DB と完全一致。保存時は何もしない。</summary>
    Unchanged,

    /// <summary>既存行を編集（UPDATE 対象）。</summary>
    Modified,

    /// <summary>メモリ上の新規行（INSERT 対象、まだ DB に行が無い）。</summary>
    Added,

    /// <summary>削除マーク（DELETE 対象）。</summary>
    Deleted
}
