namespace PrecureDataStars.Catalog.Common.Dialogs;

partial class NewProductDialog
{
    private System.ComponentModel.IContainer components = null!;

    private Label lblTitle = null!;
    private TextBox txtTitle = null!;
    private Label lblTitleShort = null!;
    private TextBox txtTitleShort = null!;
    private Label lblTitleEn = null!;
    private TextBox txtTitleEn = null!;
    private Label lblSeries = null!;
    private ComboBox cboSeries = null!;
    private Label lblKind = null!;
    private ComboBox cboKind = null!;
    private Label lblReleaseDate = null!;
    private DateTimePicker dtReleaseDate = null!;
    private Label lblPriceEx = null!;
    // v1.1.5: NumericUpDown から TextBox に変更。価格は数千円単位なので上下ボタンでの入力は非実用的、
    // かつ税抜価格を入れたら税込価格をその場で自動計算して表示する設計のため、税抜は TextBox 入力、
    // 税込は ReadOnly TextBox の自動表示にする。
    private TextBox txtPriceEx = null!;
    private Label lblPriceInc = null!;
    /// <summary>
    /// 税込価格表示用の読み取り専用 TextBox（v1.1.5）。
    /// <see cref="NewProductDialog"/> 側で <see cref="txtPriceEx"/> および <c>dtReleaseDate</c> の値変化に
    /// 連動して、発売日に対応する消費税率を適用した結果が自動入力される。
    /// </summary>
    private TextBox txtPriceInc = null!;
    private Label lblDiscCount = null!;
    private NumericUpDown numDiscCount = null!;
    private Label lblManufacturer = null!;
    private TextBox txtManufacturer = null!;
    private Label lblDistributor = null!;
    private TextBox txtDistributor = null!;
    private Label lblLabel = null!;
    private TextBox txtLabel = null!;
    private Label lblAsin = null!;
    private TextBox txtAsin = null!;
    private Label lblAppleId = null!;
    private TextBox txtAppleId = null!;
    private Label lblSpotifyId = null!;
    private TextBox txtSpotifyId = null!;
    private Label lblNotes = null!;
    private TextBox txtNotes = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // 全コントロールを new
        lblTitle = new Label(); txtTitle = new TextBox();
        lblTitleShort = new Label(); txtTitleShort = new TextBox();
        lblTitleEn = new Label(); txtTitleEn = new TextBox();
        lblSeries = new Label(); cboSeries = new ComboBox();
        lblKind = new Label(); cboKind = new ComboBox();
        lblReleaseDate = new Label(); dtReleaseDate = new DateTimePicker();
        lblPriceEx = new Label(); txtPriceEx = new TextBox();
        lblPriceInc = new Label(); txtPriceInc = new TextBox();
        lblDiscCount = new Label(); numDiscCount = new NumericUpDown();
        lblManufacturer = new Label(); txtManufacturer = new TextBox();
        lblDistributor = new Label(); txtDistributor = new TextBox();
        lblLabel = new Label(); txtLabel = new TextBox();
        lblAsin = new Label(); txtAsin = new TextBox();
        lblAppleId = new Label(); txtAppleId = new TextBox();
        lblSpotifyId = new Label(); txtSpotifyId = new TextBox();
        lblNotes = new Label(); txtNotes = new TextBox();
        btnOk = new Button(); btnCancel = new Button();

        SuspendLayout();

        // レイアウト用ヘルパ的にオフセットを段階的に管理
        int y = 12;
        int labelW = 100;
        int ctrlX = 120;
        int ctrlW = 420;
        int rowH = 28;

        void PlaceLabel(Label lbl, string text, int yy) { lbl.Location = new Point(12, yy + 3); lbl.Size = new Size(labelW, 22); lbl.Text = text; }
        void PlaceCtrl(Control c, int yy, int w) { c.Location = new Point(ctrlX, yy); c.Size = new Size(w, 23); }

        PlaceLabel(lblTitle, "タイトル*", y); PlaceCtrl(txtTitle, y, ctrlW); y += rowH;
        PlaceLabel(lblTitleShort, "略称", y); PlaceCtrl(txtTitleShort, y, 240); y += rowH;
        PlaceLabel(lblTitleEn, "英語タイトル", y); PlaceCtrl(txtTitleEn, y, ctrlW); y += rowH;
        PlaceLabel(lblSeries, "シリーズ", y); PlaceCtrl(cboSeries, y, 300); y += rowH;
        PlaceLabel(lblKind, "商品種別*", y); PlaceCtrl(cboKind, y, 240); y += rowH;
        PlaceLabel(lblReleaseDate, "発売日*", y); PlaceCtrl(dtReleaseDate, y, 180); y += rowH;
        PlaceLabel(lblPriceEx, "税抜価格", y); PlaceCtrl(txtPriceEx, y, 120); y += rowH;
        PlaceLabel(lblPriceInc, "税込価格", y); PlaceCtrl(txtPriceInc, y, 120); y += rowH;
        PlaceLabel(lblDiscCount, "枚数*", y); PlaceCtrl(numDiscCount, y, 80); y += rowH;
        PlaceLabel(lblManufacturer, "発売元", y); PlaceCtrl(txtManufacturer, y, 240); y += rowH;
        PlaceLabel(lblDistributor, "販売元", y); PlaceCtrl(txtDistributor, y, 240); y += rowH;
        PlaceLabel(lblLabel, "レーベル", y); PlaceCtrl(txtLabel, y, 240); y += rowH;
        PlaceLabel(lblAsin, "Amazon ASIN", y); PlaceCtrl(txtAsin, y, 160); y += rowH;
        PlaceLabel(lblAppleId, "Apple Album ID", y); PlaceCtrl(txtAppleId, y, 240); y += rowH;
        PlaceLabel(lblSpotifyId, "Spotify ID", y); PlaceCtrl(txtSpotifyId, y, 240); y += rowH;
        PlaceLabel(lblNotes, "備考", y); txtNotes.Location = new Point(ctrlX, y); txtNotes.Size = new Size(ctrlW, 48); txtNotes.Multiline = true; y += 56;

        // 価格 TextBox（v1.1.5）の初期設定:
        //   - 数値入力を促すため右寄せ表示
        //   - 税込側は自動計算結果のみを表示する読み取り専用フィールド
        //     （ReadOnly + 背景色 ControlLight で「触れない欄」であることを視覚化）
        txtPriceEx.TextAlign = HorizontalAlignment.Right;
        txtPriceInc.TextAlign = HorizontalAlignment.Right;
        txtPriceInc.ReadOnly = true;
        txtPriceInc.BackColor = SystemColors.ControlLight;

        // Numeric の範囲（残るのは枚数のみ）
        numDiscCount.Maximum = 32; numDiscCount.Minimum = 1;

        // ボタン
        btnOk.Location = new Point(360, y + 8); btnOk.Size = new Size(90, 30); btnOk.Text = "OK";
        btnCancel.Location = new Point(458, y + 8); btnCancel.Size = new Size(90, 30); btnCancel.Text = "キャンセル";

        ClientSize = new Size(564, y + 52);
        Controls.AddRange(new Control[]
        {
            lblTitle, txtTitle, lblTitleShort, txtTitleShort, lblTitleEn, txtTitleEn,
            lblSeries, cboSeries, lblKind, cboKind, lblReleaseDate, dtReleaseDate,
            lblPriceEx, txtPriceEx, lblPriceInc, txtPriceInc, lblDiscCount, numDiscCount,
            lblManufacturer, txtManufacturer, lblDistributor, txtDistributor,
            lblLabel, txtLabel, lblAsin, txtAsin, lblAppleId, txtAppleId, lblSpotifyId, txtSpotifyId,
            lblNotes, txtNotes, btnOk, btnCancel
        });

        Text = "新規商品作成";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(false);
        PerformLayout();
    }
}
