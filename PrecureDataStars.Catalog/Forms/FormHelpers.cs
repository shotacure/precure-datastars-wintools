namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 各エディタフォーム・ダイアログで共通のダイアログ表示・文字列整形・グリッド整形の定型ヘルパ。
/// 各フォームが private ヘルパとして同一実装を重複保持していたものを単一定義へ集約した。
/// ShowError / Confirm は <see cref="IWin32Window"/>（= Form 自身）をオーナーに取る拡張メソッドで、
/// フォーム内からは <c>this.ShowError(ex)</c> / <c>this.Confirm(msg)</c> の形で呼ぶ。
/// </summary>
internal static class FormHelpers
{
    /// <summary>例外をエラーダイアログで通知する共通ハンドラ。</summary>
    public static void ShowError(this IWin32Window owner, Exception ex)
        => MessageBox.Show(owner, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>Yes/No 確認ダイアログ。</summary>
    public static DialogResult Confirm(this IWin32Window owner, string msg)
        => MessageBox.Show(owner, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    /// <summary>空白のみ・空文字を NULL に変換する（非空なら前後空白を除去して返す）。
    /// 前後空白を保持したいケース（クレジット系マスタ管理の非 Trim 版）には使わないこと。</summary>
    public static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>DataGridView から監査・削除・長文メモ系の列 （CreatedAt / UpdatedAt / CreatedBy / UpdatedBy / IsDeleted / Notes）を非表示にする。 バインド済みグリッドに対して即時実行する（列が未生成の場合は何もしない）。</summary>
    public static void HideMetaColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (col.Name is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy" or "IsDeleted" or "Notes")
                col.Visible = false;
        }
    }
}
