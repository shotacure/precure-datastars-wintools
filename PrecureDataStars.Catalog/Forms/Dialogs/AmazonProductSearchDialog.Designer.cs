#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// Amazon 商品検索ダイアログのレイアウト定義（コードビハインド）。
/// 上段に検索キーワード入力＋検索ボタン、下段を 2 列に分けて
/// 左に物理（CD）の候補一覧、右にデジタル音源の候補一覧を並べる。
/// </summary>
partial class AmazonProductSearchDialog
{
    private System.ComponentModel.IContainer? components = null;

    // 上段：検索バー
    private Panel pnlSearchBar = null!;
    private Label lblKeyword = null!;
    private TextBox txtKeyword = null!;
    private Button btnSearch = null!;
    private Label lblStatus = null!;

    // 下段：左右分割（CD / デジタル）
    private SplitContainer splitResults = null!;

    // 左：CD 候補リスト
    private Panel pnlLeftSide = null!;
    private Label lblLeftHeader = null!;
    private ListView lvLeft = null!;
    private Label lblLeftSelected = null!;

    // 右：デジタル候補リスト
    private Panel pnlRightSide = null!;
    private Label lblRightHeader = null!;
    private ListView lvRight = null!;
    private Label lblRightSelected = null!;

    // 共通：候補リストにサムネを出すための ImageList
    // ListView の SmallImageList / LargeImageList に bind し、Creators API から取得した
    // 画像を 128x128 にスケールして登録する。CD のジャケットアートでパッケージを判別できるサイズを確保する。
    private ImageList imgList = null!;

    // 最下段：OK / キャンセル
    private Panel pnlButtons = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // 全体
        Text = "Amazon 商品検索 (Creators API)";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 600);
        MinimumSize = new Size(800, 500);
        MaximizeBox = true;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // ── 上段：検索バー ──
        pnlSearchBar = new Panel { Dock = DockStyle.Top, Height = 70, Padding = new Padding(8) };
        lblKeyword = new Label { Text = "検索キーワード:", Location = new Point(8, 12), Size = new Size(110, 22) };
        txtKeyword = new TextBox { Location = new Point(120, 8), Size = new Size(560, 23) };
        btnSearch = new Button { Text = "検索", Location = new Point(688, 7), Size = new Size(80, 25) };
        lblStatus = new Label
        {
            Location = new Point(120, 38),
            Size = new Size(700, 22),
            ForeColor = SystemColors.GrayText,
            Text = "（検索キーワードを入力して「検索」を押してください。CD / デジタル両系統を並列に検索します）"
        };
        pnlSearchBar.Controls.AddRange(new Control[] { lblKeyword, txtKeyword, btnSearch, lblStatus });

        // ── 中段：候補リストの ImageList（サムネ用） ──
        imgList = new ImageList
        {
            ImageSize = new Size(128, 128),
            ColorDepth = ColorDepth.Depth32Bit
        };

        // ── 中段：左右分割 ──
        splitResults = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
        };

        // 左パネル（CD）
        pnlLeftSide = new Panel { Dock = DockStyle.Fill };
        lblLeftHeader = new Label
        {
            Text = "CD (物理パッケージ)",
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(8, 4, 0, 0),
            Font = new Font("Yu Gothic UI", 10F, FontStyle.Bold),
            BackColor = Color.FromArgb(240, 244, 248)
        };
        lvLeft = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Tile,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            TileSize = new Size(460, 150),
            SmallImageList = imgList,
            LargeImageList = imgList,
        };
        lblLeftSelected = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Padding = new Padding(8, 4, 0, 0),
            ForeColor = SystemColors.GrayText,
            Text = "選択中: なし"
        };
        pnlLeftSide.Controls.Add(lvLeft);
        pnlLeftSide.Controls.Add(lblLeftSelected);
        pnlLeftSide.Controls.Add(lblLeftHeader);

        // 右パネル（デジタル）
        pnlRightSide = new Panel { Dock = DockStyle.Fill };
        lblRightHeader = new Label
        {
            Text = "デジタル (Amazon Music)",
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(8, 4, 0, 0),
            Font = new Font("Yu Gothic UI", 10F, FontStyle.Bold),
            BackColor = Color.FromArgb(244, 240, 248)
        };
        lvRight = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Tile,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            TileSize = new Size(460, 150),
            SmallImageList = imgList,
            LargeImageList = imgList,
        };
        lblRightSelected = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            Padding = new Padding(8, 4, 0, 0),
            ForeColor = SystemColors.GrayText,
            Text = "選択中: なし"
        };
        pnlRightSide.Controls.Add(lvRight);
        pnlRightSide.Controls.Add(lblRightSelected);
        pnlRightSide.Controls.Add(lblRightHeader);

        splitResults.Panel1.Controls.Add(pnlLeftSide);
        splitResults.Panel2.Controls.Add(pnlRightSide);

        // ── 最下段：OK / キャンセル ──
        pnlButtons = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
        btnOk = new Button { Text = "OK", Size = new Size(90, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnCancel = new Button { Text = "キャンセル", Size = new Size(90, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        // Anchor=Right の見栄えのため Location は後で動的計算
        btnOk.Location = new Point(ClientSize.Width - 200, 8);
        btnCancel.Location = new Point(ClientSize.Width - 102, 8);
        pnlButtons.Controls.AddRange(new Control[] { btnOk, btnCancel });
        pnlButtons.Resize += (_, __) =>
        {
            btnOk.Location = new Point(pnlButtons.ClientSize.Width - 200, 8);
            btnCancel.Location = new Point(pnlButtons.ClientSize.Width - 102, 8);
        };

        Controls.Add(splitResults);   // Fill は先に追加（後ろに重なる順）
        Controls.Add(pnlButtons);     // Bottom
        Controls.Add(pnlSearchBar);   // Top

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(false);
        PerformLayout();
    }
}
