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
        splitMain = new SplitContainer();
        gridDiscs = new DataGridView();
        lblTracks = new Label();
        gridTracks = new DataGridView();

        // ── ツールバー（上段の最上部固定帯） ──
        pnlToolbar.Dock = DockStyle.Top;
        pnlToolbar.Height = 36;
        pnlToolbar.Padding = new Padding(8, 6, 8, 4);

        lblSearch.Text = "検索:";
        lblSearch.AutoSize = true;
        lblSearch.Location = new Point(8, 10);

        txtSearch.Location = new Point(52, 6);
        txtSearch.Size = new Size(240, 24);
        txtSearch.PlaceholderText = "品番 / タイトル / シリーズ名";

        lblSeries.Text = "シリーズ:";
        lblSeries.AutoSize = true;
        lblSeries.Location = new Point(304, 10);

        cboSeries.Location = new Point(368, 6);
        cboSeries.Size = new Size(220, 24);
        cboSeries.DropDownStyle = ComboBoxStyle.DropDownList;

        btnReload.Text = "再読込";
        btnReload.Location = new Point(600, 5);
        btnReload.Size = new Size(80, 26);

        lblCount.Text = "";
        lblCount.AutoSize = true;
        lblCount.Location = new Point(696, 10);
        lblCount.ForeColor = Color.Gray;

        pnlToolbar.Controls.AddRange(new Control[] { lblSearch, txtSearch, lblSeries, cboSeries, btnReload, lblCount });

        // ── 上下 2 ペイン ──
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Horizontal;
        splitMain.SplitterDistance = 320;

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
        lblTracks.Height = 22;
        lblTracks.Padding = new Padding(8, 4, 0, 0);
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

        // ── Form ──
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1100, 680);
        // 追加順注意: ツールバーが上、分割が残り全域
        Controls.Add(splitMain);
        Controls.Add(pnlToolbar);
        Name = "DiscBrowserForm";
        Text = "ディスク・トラック閲覧 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }
}
