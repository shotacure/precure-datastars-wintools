namespace PrecureDataStars.Catalog.Common.Dialogs;

partial class ConfirmAttachDialog
{
    private System.ComponentModel.IContainer components = null!;

    // ヘッダ
    private Label lblHeader = null!;

    // 商品情報（読み取り専用ラベル）
    private Label lblProductCatalogNoLabel = null!;
    private Label lblProductCatalogNo = null!;
    private Label lblProductTitleLabel = null!;
    private Label lblProductTitle = null!;
    private Label lblProductReleaseDateLabel = null!;
    private Label lblProductReleaseDate = null!;
    private Label lblProductDiscCountLabel = null!;
    private Label lblProductDiscCount = null!;

    // 所属ディスクのプレビュー
    private Label lblExistingDiscs = null!;
    private DataGridView gridDiscs = null!;

    // シリーズ選択
    private Label lblSeriesOverride = null!;
    private ComboBox cboSeriesOverride = null!;
    private Label lblSeriesNote = null!;

    // 品番入力
    private Label lblCatalogNo = null!;
    private TextBox txtCatalogNo = null!;
    private Label lblCatalogNoHint = null!;

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
        lblHeader = new Label();
        lblProductCatalogNoLabel = new Label();
        lblProductCatalogNo = new Label();
        lblProductTitleLabel = new Label();
        lblProductTitle = new Label();
        lblProductReleaseDateLabel = new Label();
        lblProductReleaseDate = new Label();
        lblProductDiscCountLabel = new Label();
        lblProductDiscCount = new Label();
        lblExistingDiscs = new Label();
        gridDiscs = new DataGridView();
        lblSeriesOverride = new Label();
        cboSeriesOverride = new ComboBox();
        lblSeriesNote = new Label();
        lblCatalogNo = new Label();
        txtCatalogNo = new TextBox();
        lblCatalogNoHint = new Label();
        btnAttach = new Button();
        btnCancel = new Button();

        SuspendLayout();

        // ヘッダ
        lblHeader.Location = new Point(12, 10);
        lblHeader.Size = new Size(560, 22);
        lblHeader.Text = "以下の商品にディスクを追加します。";
        lblHeader.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

        // 商品情報行 1: 代表品番
        lblProductCatalogNoLabel.Location = new Point(12, 44);
        lblProductCatalogNoLabel.Size = new Size(80, 20);
        lblProductCatalogNoLabel.Text = "代表品番:";

        lblProductCatalogNo.Location = new Point(96, 44);
        lblProductCatalogNo.Size = new Size(180, 20);
        lblProductCatalogNo.Text = "";

        // 商品情報行 1 右: 発売日
        lblProductReleaseDateLabel.Location = new Point(290, 44);
        lblProductReleaseDateLabel.Size = new Size(70, 20);
        lblProductReleaseDateLabel.Text = "発売日:";

        lblProductReleaseDate.Location = new Point(364, 44);
        lblProductReleaseDate.Size = new Size(110, 20);
        lblProductReleaseDate.Text = "";

        // 商品情報行 1 末尾: 現在の枚数
        lblProductDiscCountLabel.Location = new Point(488, 44);
        lblProductDiscCountLabel.Size = new Size(60, 20);
        lblProductDiscCountLabel.Text = "現在 :";

        lblProductDiscCount.Location = new Point(550, 44);
        lblProductDiscCount.Size = new Size(50, 20);
        lblProductDiscCount.Text = "";

        // 商品情報行 2: タイトル
        lblProductTitleLabel.Location = new Point(12, 68);
        lblProductTitleLabel.Size = new Size(80, 20);
        lblProductTitleLabel.Text = "タイトル:";

        lblProductTitle.Location = new Point(96, 68);
        lblProductTitle.Size = new Size(564, 20);
        lblProductTitle.Text = "";

        // 所属ディスクのプレビュー
        lblExistingDiscs.Location = new Point(12, 100);
        lblExistingDiscs.Size = new Size(400, 20);
        lblExistingDiscs.Text = "選択商品の所属ディスク（プレビュー）:";

        gridDiscs.Location = new Point(12, 122);
        gridDiscs.Size = new Size(648, 180);
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.RowHeadersVisible = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        // シリーズ選択行
        lblSeriesOverride.Location = new Point(12, 314);
        lblSeriesOverride.Size = new Size(70, 20);
        lblSeriesOverride.Text = "シリーズ:";

        cboSeriesOverride.Location = new Point(84, 311);
        cboSeriesOverride.Size = new Size(280, 23);
        cboSeriesOverride.DropDownStyle = ComboBoxStyle.DropDownList;

        lblSeriesNote.Location = new Point(372, 314);
        lblSeriesNote.Size = new Size(288, 20);
        lblSeriesNote.Text = "（既存ディスクから継承）";
        lblSeriesNote.ForeColor = SystemColors.ControlDarkDark;

        // 品番入力行（旧 PromptCatalogNo の機能をこのダイアログ内に取り込み）
        // SuggestedCatalogNo を初期値として入れ、フォーム表示時に全選択状態にする
        // （実装は ConfirmAttachDialog.cs の Load ハンドラ）。
        lblCatalogNo.Location = new Point(12, 348);
        lblCatalogNo.Size = new Size(96, 20);
        lblCatalogNo.Text = "新ディスク品番:";

        txtCatalogNo.Location = new Point(110, 345);
        txtCatalogNo.Size = new Size(254, 23);

        lblCatalogNoHint.Location = new Point(372, 348);
        lblCatalogNoHint.Size = new Size(288, 20);
        lblCatalogNoHint.Text = "（既存最後尾の品番を +1 した候補）";
        lblCatalogNoHint.ForeColor = SystemColors.ControlDarkDark;

        // ボタン
        btnAttach.Location = new Point(440, 392);
        btnAttach.Size = new Size(140, 32);
        btnAttach.Text = "追加して登録";

        btnCancel.Location = new Point(588, 392);
        btnCancel.Size = new Size(72, 32);
        btnCancel.Text = "キャンセル";

        // Form
        ClientSize = new Size(672, 439);
        Controls.AddRange(new Control[]
        {
            lblHeader,
            lblProductCatalogNoLabel, lblProductCatalogNo,
            lblProductReleaseDateLabel, lblProductReleaseDate,
            lblProductDiscCountLabel, lblProductDiscCount,
            lblProductTitleLabel, lblProductTitle,
            lblExistingDiscs, gridDiscs,
            lblSeriesOverride, cboSeriesOverride, lblSeriesNote,
            lblCatalogNo, txtCatalogNo, lblCatalogNoHint,
            btnAttach, btnCancel
        });
        Text = "選択した商品に追加";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        ResumeLayout(false);
        PerformLayout();
    }
}