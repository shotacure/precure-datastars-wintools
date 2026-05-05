namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// Draft オブジェクトの共通基底（v1.2.0 工程 H-8 で導入）。
/// <para>
/// 各 Draft 行は「DB 由来の実 ID（<see cref="RealId"/>、未保存なら null）」と「メモリ上の Temp ID
/// （<see cref="TempId"/>、CreditDraftSession から払い出される負の整数）」の両方を持ち、
/// <see cref="CurrentId"/> でどちらか有効な方を返す（ツリー操作の参照キーとして使う）。
/// </para>
/// <para>
/// 既存行を読み込んだ Draft は <c>RealId = DB 値、TempId = 払い出し済み負数、State = Unchanged</c>。
/// メモリ上で新規作成した Draft は <c>RealId = null、TempId = 払い出し済み負数、State = Added</c>。
/// 保存時に Added 行は INSERT され、戻りの自動採番 ID が <see cref="RealId"/> に書き込まれる。
/// </para>
/// </summary>
public abstract class DraftBase
{
    /// <summary>DB 上の実 ID（既存行なら値あり、未保存なら null）。</summary>
    public int? RealId { get; set; }

    /// <summary>メモリ上の一意 ID（負数、CreditDraftSession から払い出される）。</summary>
    public int TempId { get; init; }

    /// <summary>ツリー操作の参照キーとして使う ID（実 ID があればそれ、なければ Temp ID）。</summary>
    public int CurrentId => RealId ?? TempId;

    /// <summary>Draft の状態フラグ。</summary>
    public DraftState State { get; set; } = DraftState.Unchanged;

    /// <summary>
    /// 状態を Modified にマークする（既に Added / Deleted の場合は変更しない）。
    /// 既存行を編集したときに呼ぶ便宜メソッド。
    /// </summary>
    public void MarkModified()
    {
        if (State == DraftState.Unchanged) State = DraftState.Modified;
    }

    /// <summary>
    /// 状態を Deleted にマークする（Added だった場合は呼び出し元で Draft 自体をリストから取り除く設計）。
    /// </summary>
    public void MarkDeleted()
    {
        State = DraftState.Deleted;
    }
}
