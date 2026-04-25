namespace PrecureDataStars.Catalog.Common.Dialogs;

partial class AttachToProductDialog
{
    private System.ComponentModel.IContainer components = null!;

    // 検索バー
    private Label lblSearchTitle = null!;
    private TextBox txtKeyword = null!;
    private Button btnSearch = null!;
    private Label lblSearchInfo = null!;

    // 商品候補グリッド
    private DataGridView gridProducts = null!;

    // 選択商品の所属ディスクプレビュー
    private Label lblExistingDiscs = null!;
    private DataGridView gridDiscs = null!;

    // 新ディスクの追加情報入力欄（v1.1.3: 組内番号は自動再採番のため UI 撤去。シリーズ上書きのみ残す）
    private Label lblNewDisc = null!;
    private Label lblSeriesOverride = null!;
    private ComboBox cboSeriesOverride = null!;
    private Label lblSeriesNote = null!;

    // アクションボタン
    private Button btnAttach = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblSearchTitle = new Label();
        txtKeyword = new TextBox();
        btnSearch = new Button();
        lblSearchInfo = new Label();
        gridProducts = new DataGridView();
        lblExistingDiscs = new Label();
        gridDiscs = new DataGridView();
        lblNewDisc = new Label();
        lblSeriesOverride = new Label();
        cboSeriesOverride = new ComboBox();
        lblSeriesNote = new Label();
        btnAttach = new Button();
        btnCancel = new Button();

        SuspendLayout();

        // 検索バー
        lblSearchTitle.Location = new Point(12, 12);
        lblSearchTitle.Size = new Size(260, 20);
        lblSearchTitle.Text = "既存商品を検索 (品番 / タイトル):";

        txtKeyword.Location = new Point(12, 34);
        txtKeyword.Size = new Size(580, 23);

        btnSearch.Location = new Point(600, 33);
        btnSearch.Size = new Size(80, 25);
        btnSearch.Text = "検索";

        lblSearchInfo.Location = new Point(690, 38);
        lblSearchInfo.Size = new Size(82, 20);
        lblSearchInfo.Text = "";

        // 商品候補グリッド
        gridProducts.Location = new Point(12, 64);
        gridProducts.Size = new Size(760, 200);
        gridProducts.AllowUserToAddRows = false;
        gridProducts.AllowUserToDeleteRows = false;
        gridProducts.RowHeadersVisible = false;
        gridProducts.MultiSelect = false;
        gridProducts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        // 既存ディスクプレビュー
        lblExistingDiscs.Location = new Point(12, 274);
        lblExistingDiscs.Size = new Size(400, 20);
        lblExistingDiscs.Text = "選択商品の所属ディスク（プレビュー）:";

        gridDiscs.Location = new Point(12, 296);
        gridDiscs.Size = new Size(760, 130);
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.RowHeadersVisible = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        // 新ディスク追加情報
        lblNewDisc.Location = new Point(12, 436);
        lblNewDisc.Size = new Size(400, 20);
        lblNewDisc.Text = "新規ディスクの追加情報:";
        lblNewDisc.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

        // v1.1.3: 組内番号は自動再採番のため UI を出さない（呼び出し時に商品配下の全ディスクを
        // 品番順で 1,2,3,... と振り直す。歯抜けや 1 始まりでない既存データもこの操作で整列する）。
        lblSeriesOverride.Location = new Point(12, 462);
        lblSeriesOverride.Size = new Size(70, 20);
        lblSeriesOverride.Text = "シリーズ:";

        cboSeriesOverride.Location = new Point(84, 459);
        cboSeriesOverride.Size = new Size(280, 23);
        cboSeriesOverride.DropDownStyle = ComboBoxStyle.DropDownList;

        lblSeriesNote.Location = new Point(372, 462);
        lblSeriesNote.Size = new Size(400, 20);
        lblSeriesNote.Text = "（既存ディスクから継承）";
        lblSeriesNote.ForeColor = SystemColors.ControlDarkDark;

        // アクションボタン
        btnAttach.Location = new Point(490, 502);
        btnAttach.Size = new Size(180, 32);
        btnAttach.Text = "選択商品に追加して登録";

        btnCancel.Location = new Point(680, 502);
        btnCancel.Size = new Size(92, 32);
        btnCancel.Text = "キャンセル";

        // Form
        ClientSize = new Size(784, 549);
        Controls.AddRange(new Control[]
        {
            lblSearchTitle, txtKeyword, btnSearch, lblSearchInfo,
            gridProducts,
            lblExistingDiscs, gridDiscs,
            lblNewDisc,
            lblSeriesOverride, cboSeriesOverride, lblSeriesNote,
            btnAttach, btnCancel
        });
        Text = "既存商品に追加ディスクとして登録";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        ResumeLayout(false);
        PerformLayout();
    }
}
