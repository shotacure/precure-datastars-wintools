#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    private MenuStrip menuStrip = null!;
    private ToolStripMenuItem mnuFile = null!;
    private ToolStripMenuItem mnuExit = null!;
    private ToolStripMenuItem mnuEdit = null!;
    private ToolStripMenuItem mnuBrowse = null!;
    // v1.1.3: 商品管理 / ディスク・トラック管理 を 商品・ディスク管理 / トラック管理 に再編
    private ToolStripMenuItem mnuProductDiscs = null!;
    private ToolStripMenuItem mnuTracks = null!;
    private ToolStripMenuItem mnuSongs = null!;
    private ToolStripMenuItem mnuBgm = null!;
    private ToolStripMenuItem mnuMasters = null!;
    private Label lblWelcome = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        menuStrip = new MenuStrip();
        mnuFile = new ToolStripMenuItem();
        mnuExit = new ToolStripMenuItem();
        mnuEdit = new ToolStripMenuItem();
        mnuBrowse = new ToolStripMenuItem();
        // v1.1.3: 商品管理 / ディスク・トラック管理 を 商品・ディスク管理 / トラック管理 に再編
        mnuProductDiscs = new ToolStripMenuItem();
        mnuTracks = new ToolStripMenuItem();
        mnuSongs = new ToolStripMenuItem();
        mnuBgm = new ToolStripMenuItem();
        mnuMasters = new ToolStripMenuItem();
        lblWelcome = new Label();

        // menuStrip
        menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuEdit });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";

        // mnuFile
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuExit });
        mnuFile.Text = "ファイル(&F)";

        // mnuExit
        mnuExit.Text = "終了(&X)";
        mnuExit.Click += (_, __) => Close();

        // mnuEdit
        mnuEdit.DropDownItems.AddRange(new ToolStripItem[] { mnuBrowse, new ToolStripSeparator(), mnuProductDiscs, mnuTracks, mnuSongs, mnuBgm, mnuMasters });
        mnuEdit.Text = "編集(&E)";

        mnuBrowse.Text = "ディスク・トラック閲覧...";
        mnuBrowse.Click += mnuBrowse_Click;

        // v1.1.3: 商品・ディスク管理（商品編集＋所属ディスク編集を 1 画面）
        mnuProductDiscs.Text = "商品・ディスク管理...";
        mnuProductDiscs.Click += mnuProductDiscs_Click;

        // v1.1.3: トラック管理（SONG/BGM オートコンプリート付きのトラック編集専用）
        mnuTracks.Text = "トラック管理...";
        mnuTracks.Click += mnuTracks_Click;

        mnuSongs.Text = "歌マスタ管理...";
        mnuSongs.Click += mnuSongs_Click;

        mnuBgm.Text = "劇伴マスタ管理...";
        mnuBgm.Click += mnuBgm_Click;

        mnuMasters.Text = "マスタ管理...";
        mnuMasters.Click += mnuMasters_Click;

        // lblWelcome
        lblWelcome.Dock = DockStyle.Fill;
        lblWelcome.Text = "PrecureDataStars.Catalog\n\n音楽・映像カタログ管理 GUI\n\n上のメニューから編集対象を選択してください。";
        lblWelcome.TextAlign = ContentAlignment.MiddleCenter;
        lblWelcome.Font = new Font("Segoe UI", 11F);

        // MainForm
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(640, 360);
        Controls.Add(lblWelcome);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
        Name = "MainForm";
        Text = "Precure Catalog (PrecureDataStars)";
        StartPosition = FormStartPosition.CenterScreen;
    }
}