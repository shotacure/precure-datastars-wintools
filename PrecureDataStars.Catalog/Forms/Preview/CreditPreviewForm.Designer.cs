using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Preview;

partial class CreditPreviewForm
{
    private IContainer components = null!;

    private WebBrowser webBrowser = null!;

    private void InitializeComponent()
    {
        components = new Container();

        Text = "クレジットプレビュー";
        StartPosition = FormStartPosition.Manual;
        // 画面右上付近に出す（クレジット編集画面の隣に並びやすい位置）
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Width = 800;
        Height = 900;
        Location = new Point(Math.Max(0, screen.Right - Width - 16), Math.Max(0, screen.Top + 16));
        MinimumSize = new Size(400, 300);

        webBrowser = new WebBrowser
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true,
            // 「<a href> でナビゲートしようとして外部 IE が立ち上がる」のを抑止する用途は今回は無いので既定のまま。
            AllowWebBrowserDrop = false,
            IsWebBrowserContextMenuEnabled = true,
        };
        Controls.Add(webBrowser);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }
}