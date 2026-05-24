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
    // 商品管理 / ディスク・トラック管理 を 商品・ディスク管理 / トラック管理 に再編
    private ToolStripMenuItem mnuProductDiscs = null!;
    private ToolStripMenuItem mnuTracks = null!;
    private ToolStripMenuItem mnuSongs = null!;
    private ToolStripMenuItem mnuBgm = null!;
    // 映画 BGM リスト管理（movie_bgm_cues。bgm_cues とは別概念の映画専用）
    private ToolStripMenuItem mnuMovieBgm = null!;
    private ToolStripMenuItem mnuMasters = null!;
    // クレジット系マスタ管理（人物/企業/キャラ/役職/声優キャスティング/書式上書き/エピソード主題歌）
    private ToolStripMenuItem mnuCreditMasters = null!;
    // クレジット本体編集（カード／役職／ブロック／エントリの 3 ペイン編集フォーム）
    private ToolStripMenuItem mnuCreditEditor = null!;
    // 商品社名マスタ管理（クレジット非依存・商品メタ専用）
    private ToolStripMenuItem mnuProductCompanies = null!;
    // 音楽名寄せセンター（フリーテキスト → 構造化エントリへの一括移行ツール、撤去前提）
    private ToolStripMenuItem mnuMusicNameResolution = null!;
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
        // 商品管理 / ディスク・トラック管理 を 商品・ディスク管理 / トラック管理 に再編
        mnuProductDiscs = new ToolStripMenuItem();
        mnuTracks = new ToolStripMenuItem();
        mnuSongs = new ToolStripMenuItem();
        mnuBgm = new ToolStripMenuItem();
        mnuMovieBgm = new ToolStripMenuItem();
        mnuMasters = new ToolStripMenuItem();
        // クレジット系マスタ管理メニューのインスタンス化
        mnuCreditMasters = new ToolStripMenuItem();
        // クレジット本体編集メニュー
        mnuCreditEditor = new ToolStripMenuItem();
        // 商品社名マスタ管理メニュー
        mnuProductCompanies = new ToolStripMenuItem();
        // 音楽名寄せセンター
        mnuMusicNameResolution = new ToolStripMenuItem();
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

        // mnuEdit（クレジット名寄せ移行ツールは Stage 3 で撤去済み）
        mnuEdit.DropDownItems.AddRange(new ToolStripItem[]
        {
            mnuBrowse, new ToolStripSeparator(),
            mnuProductDiscs, mnuProductCompanies, mnuTracks, mnuSongs, mnuBgm, mnuMovieBgm, mnuMasters,
            new ToolStripSeparator(),
            mnuCreditMasters, mnuCreditEditor,
            new ToolStripSeparator(),
            mnuMusicNameResolution
        });
        mnuEdit.Text = "編集(&E)";

        mnuBrowse.Text = "ディスク・トラック閲覧...";
        mnuBrowse.Click += mnuBrowse_Click;

        // 商品・ディスク管理（商品編集＋所属ディスク編集を 1 画面）
        mnuProductDiscs.Text = "商品・ディスク管理...";
        mnuProductDiscs.Click += mnuProductDiscs_Click;

        // 商品社名マスタ管理（クレジット非依存・商品メタ専用）
        mnuProductCompanies.Text = "商品社名マスタ管理...";
        mnuProductCompanies.Click += mnuProductCompanies_Click;

        // トラック管理（SONG/BGM オートコンプリート付きのトラック編集専用）
        mnuTracks.Text = "トラック管理...";
        mnuTracks.Click += mnuTracks_Click;

        mnuSongs.Text = "歌マスタ管理...";
        mnuSongs.Click += mnuSongs_Click;

        mnuBgm.Text = "劇伴マスタ管理...";
        mnuBgm.Click += mnuBgm_Click;
        mnuMovieBgm.Text = "映画 BGM リスト管理...";
        mnuMovieBgm.Click += mnuMovieBgm_Click;

        mnuMasters.Text = "マスタ管理...";
        mnuMasters.Click += mnuMasters_Click;

        // クレジット系マスタ（人物/企業/キャラクター/役職/声優キャスティング/書式上書き/エピソード主題歌）
        mnuCreditMasters.Text = "クレジット系マスタ管理...";
        mnuCreditMasters.Click += mnuCreditMasters_Click;

        // クレジット本体編集（5 ペイン構造編集フォーム）
        mnuCreditEditor.Text = "クレジット編集...";
        mnuCreditEditor.Click += mnuCreditEditor_Click;

        // 音楽名寄せセンター（撤去前提のフリーテキスト移行ツール）
        mnuMusicNameResolution.Text = "音楽名寄せセンター...";
        mnuMusicNameResolution.Click += mnuMusicNameResolution_Click;

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
