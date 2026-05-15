#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// ディスク・トラック閲覧フォーム（読み取り専用）のレイアウト定義。
/// 上段: 検索／絞り込みツールバー + ディスク一覧
/// 下段: トラック一覧（選択ディスク配下）
/// </summary>
partial class DiscBrowserForm
{
    private System.ComponentModel.IContainer? components = null;

    // ツールバー
    private Panel pnlToolbar = null!;
    private Label lblSearch = null!;
    private TextBox txtSearch = null!;
    private Label lblSeries = null!;
    private ComboBox cboSeries = null!;
    private Button btnReload = null!;
    private Label lblCount = null!;

    // グリッド群を囲む外周パネル。Padding で画面端からの余白を確保し、
    //         「テーブルがウインドウの際々に接している」窮屈さを緩和する。
    private Panel pnlBody = null!;

    // 上下分割
    private SplitContainer splitMain = null!;

    // ディスク一覧と付随
    private DataGridView gridDiscs = null!;

    // トラック一覧と付随
    private Label lblTracks = null!;
    private DataGridView gridTracks = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        pnlToolbar = new Panel();
        lblSearch = new Label();
        txtSearch = new TextBox();
        lblSeries = new Label();
        cboSeries = new ComboBox();
        btnReload = new Button();
        lblCount = new Label();
        pnlBody = new Panel();
        splitMain = new SplitContainer();
        gridDiscs = new DataGridView();
        lblTracks = new Label();
        gridTracks = new DataGridView();

        // ── ツールバー（上段の最上部固定帯） ──
        pnlToolbar.Dock = DockStyle.Top;
        pnlToolbar.Height = 40;
        // 左右に少し多めの余白を持たせ、グリッドのある Body 側の余白と揃える
        pnlToolbar.Padding = new Padding(10, 6, 10, 4);

        lblSearch.Text = "検索:";
        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(10, 12);

        txtSearch.Location = new Point(54, 8);
        txtSearch.Size = new Size(240, 24);
        txtSearch.PlaceholderText = "品番 / タイトル / シリーズ名";

        lblSeries.Text = "シリーズ:";
        lblSeries.AutoSize = true;
        lblSeries.Location = new Point(306, 12);

        cboSeries.Location = new Point(370, 8);
        cboSeries.Size = new Size(220, 24);
        cboSeries.DropDownStyle = ComboBoxStyle.DropDownList;

        btnReload.Text = "再読込";
        btnReload.Location = new Point(602, 7);
        btnReload.Size = new Size(80, 26);

        lblCount.Text = "";
        lblCount.AutoSize = true;
        lblCount.Location = new Point(698, 12);
        lblCount.ForeColor = Color.Gray;

        pnlToolbar.Controls.AddRange(new Control[] { lblSearch, txtSearch, lblSeries, cboSeries, btnReload, lblCount });

        // ── Body パネル ──
        // グリッド群を包み、外周に余白を与えるためのコンテナ。Dock=Fill で残余を占有し、
        // Padding でウインドウ端との間に空白を作る。Top=4 は pnlToolbar のすぐ下に少し隙間を置くため。
        pnlBody.Dock = DockStyle.Fill;
        pnlBody.Padding = new Padding(10, 4, 10, 10);

        // ── 上下 2 ペイン ──
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Horizontal;
        // SplitterDistance は実装側 .cs のコンストラクタで RecenterSplitter() を介して
        // 上下半々（実時の Height の半分）に再設定されるため、ここでは SplitContainer の
        // デフォルトサイズ制約（Panel1MinSize=25, Panel2MinSize=25）に触れない安全な小さい値を入れておく。
        splitMain.SplitterDistance = 100;
        // 分割バー自体にも少し幅を持たせ、上下ペインの間にも視覚的な区切りを入れる
        splitMain.SplitterWidth = 6;

        // ── ディスク一覧（上段） ──
        gridDiscs.Dock = DockStyle.Fill;
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.RowHeadersVisible = false;
        gridDiscs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridDiscs.MultiSelect = false;
        gridDiscs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        gridDiscs.AutoGenerateColumns = false;
        gridDiscs.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);
        splitMain.Panel1.Controls.Add(gridDiscs);

        // ── トラック一覧（下段） ──
        lblTracks.Text = "トラック一覧（ディスクを選択してください）";
        lblTracks.Dock = DockStyle.Top;
        // ラベルと下のグリッドの間にも視覚的な間をあけるため、高さを少し増やす
        lblTracks.Height = 26;
        lblTracks.Padding = new Padding(2, 6, 0, 4);
        lblTracks.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);

        gridTracks.Dock = DockStyle.Fill;
        gridTracks.AllowUserToAddRows = false;
        gridTracks.AllowUserToDeleteRows = false;
        gridTracks.ReadOnly = true;
        gridTracks.RowHeadersVisible = false;
        gridTracks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridTracks.MultiSelect = false;
        gridTracks.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        gridTracks.AutoGenerateColumns = false;
        gridTracks.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);
        splitMain.Panel2.Controls.Add(gridTracks);
        splitMain.Panel2.Controls.Add(lblTracks);

        // Body パネルに SplitContainer を載せ、Body を Form 直下の Fill エリアに載せる
        pnlBody.Controls.Add(splitMain);

        // ── Form ──
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1180, 700);
        // 追加順注意: ツールバーが上、Body が残り全域
        Controls.Add(pnlBody);
        Controls.Add(pnlToolbar);
        Name = "DiscBrowserForm";
        Text = "ディスク・トラック閲覧 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }
}