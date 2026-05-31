using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Controls;

/// <summary>
/// .NET 9 WinForms の <see cref="TreeView"/> が WM_DESTROY 受信時の UIA プロバイダ解放で
/// <see cref="System.NullReferenceException"/> を吐くバグ (dotnet/winforms#13075 系) を、
/// WM_DESTROY 専用に try/catch で握り潰すだけのサブクラス。
/// <para>
/// 症状：頻繁な Nodes.Clear → AddRange サイクル（クレジット編集ツリーの再構築）で、
/// <see cref="System.Windows.Forms.TreeView.ReleaseUiaProvider"/> 内の
/// <c>_uiaProvider</c> アクセスが null 参照を起こし、WinForms の ThreadException ダイアログ
/// （「このコントロールで実行されている動作は、間違ったスレッドから呼び出されています」）に化けてしまう。
/// </para>
/// <para>
/// 対処方針：UIA 解放処理は破棄シーケンスの一部であり、握り潰しても以降のユーザー操作・データ整合性に
/// 影響が出ないため、ここでだけ吸収する。本来は .NET 10 で修正される dotnet/winforms 側の問題なので
/// 暫定対処。NRE 以外の例外はそのまま再 throw。
/// </para>
/// </summary>
internal sealed class SafeTreeView : TreeView
{
    private const int WM_DESTROY = 0x0002;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DESTROY)
        {
            try
            {
                base.WndProc(ref m);
            }
            catch (System.NullReferenceException)
            {
                // .NET 9 TreeView.ReleaseUiaProvider 内の UIA プロバイダ null 参照を握り潰す。
                // 破棄シーケンスの一部なのでアプリ動作には影響しない。
            }
        }
        else
        {
            base.WndProc(ref m);
        }
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        catch (System.NullReferenceException)
        {
            // .NET 9 TreeView.Dispose → UnhookNodes 内の内部状態 null 参照を握り潰す。
            // フォームの閉じ際に Dispose 連鎖の途中で吐かれるとフォーム閉鎖シーケンスが
            // 例外で中断され、MainForm 側の RunChildModal 呼び出し直後に WinForms の
            // ThreadException ダイアログが上がってしまうため、ここで吸収する。
            // dotnet/winforms 側で .NET 10 修正待ちの暫定対応。
        }
    }
}
