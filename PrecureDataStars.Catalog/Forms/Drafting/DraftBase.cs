namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>Draft オブジェクトの共通基底（導入）。</summary>
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

    /// <summary>状態を Modified にマークする（既に Added / Deleted の場合は変更しない）。 既存行を編集したときに呼ぶ便宜メソッド。</summary>
    public void MarkModified()
    {
        if (State == DraftState.Unchanged) State = DraftState.Modified;
    }

    /// <summary>状態を Deleted にマークする（Added だった場合は呼び出し元で Draft 自体をリストから取り除く設計）。</summary>
    public void MarkDeleted()
    {
        State = DraftState.Deleted;
    }
}
