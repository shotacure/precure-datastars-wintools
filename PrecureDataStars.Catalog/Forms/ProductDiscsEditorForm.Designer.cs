#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品・ディスク管理フォームのレイアウト定義（v1.1.3 新設）。
/// <para>
/// 3 ペイン構成:
/// <list type="bullet">
///   <item>左: 商品一覧（発売日昇順 / 代表品番昇順、翻訳値表示）</item>
///   <item>右上: 商品詳細（代表品番・発売日・価格・ディスク枚数・種別・タイトル等）</item>
///   <item>右下: 所属ディスク一覧 + 選択ディスク詳細</item>
/// </list>
/// ウインドウ全体は左:右＝360:残り、右側は上下 splitRight で分割する。
/// </para>
/// </summary>
partial class ProductDiscsEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // 検索バー
    private Panel pnlSearch = null!;
    private Label lblSearch = null!;
    private TextBox txtSearch = null!;
    private Button btnSearch = null!;
    private Button btnReload = null!;

    // 分割コンテナ
    private SplitContainer splitMain = null!;   // 左:商品一覧 / 右:詳細群
    private SplitContainer splitRight = null!;  // 右上:商品詳細 / 右下:所属ディスク
    private SplitContainer splitDisc = null!;   // 右下左:ディスク一覧 / 右下右:ディスク詳細

    // 商品一覧
    private DataGridView gridProducts = null!;

    // 商品詳細
    private Panel pnlProductDetail = null!;
    private TextBox txtProductCatalogNo = null!;
    private TextBox txtTitle = null!;
    private TextBox txtTitleShort = null!;
    private TextBox txtTitleEn = null!;
    private ComboBox cboKind = null!;
    private DateTimePicker dtRelease = null!;
    private NumericUpDown numPriceEx = null!;
    private NumericUpDown numPriceInc = null!;
    private Button btnAutoTax = null!;
    private NumericUpDown numDiscCount = null!;
    private TextBox txtManufacturer = null!;
    private TextBox txtDistributor = null!;
    private TextBox txtLabel = null!;
    private TextBox txtAsin = null!;
    private TextBox txtApple = null!;
    private TextBox txtSpotify = null!;
    private TextBox txtNotes = null!;
    private Button btnProductNew = null!;
    private Button btnProductSave = null!;
    private Button btnProductDelete = null!;

    // 所属ディスク（右下）
    private Label lblDiscs = null!;
    private DataGridView gridDiscs = null!;

    // ディスク詳細
    private Panel pnlDiscDetail = null!;
    private TextBox txtCatalogNo = null!;
    private TextBox txtDiscTitle = null!;
    private TextBox txtDiscTitleShort = null!;
    private TextBox txtDiscTitleEn = null!;
    private ComboBox cboDiscSeries = null!;
    private NumericUpDown numDiscNoInSet = null!;
    private ComboBox cboDiscKind = null!;
    private ComboBox cboMediaFormat = null!;
    private TextBox txtMcn = null!;
    private NumericUpDown numTotalTracks = null!;
    private TextBox txtVolumeLabel = null!;
    private TextBox txtDiscNotes = null!;
    private Button btnDiscNew = null!;
    private Button btnDiscSave = null!;
    private Button btnDiscDelete = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        pnlSearch = new Panel();
        lblSearch = new Label();
        txtSearch = new TextBox();
        btnSearch = new Button();
        btnReload = new Button();

        splitMain = new SplitContainer();
        splitRight = new SplitContainer();
        splitDisc = new SplitContainer();

        gridProducts = new DataGridView();

        pnlProductDetail = new Panel();
        txtProductCatalogNo = new TextBox();
        txtTitle = new TextBox();
        txtTitleShort = new TextBox();
        txtTitleEn = new TextBox();
        cboKind = new ComboBox();
        dtRelease = new DateTimePicker();
        numPriceEx = new NumericUpDown();
        numPriceInc = new NumericUpDown();
        btnAutoTax = new Button();
        numDiscCount = new NumericUpDown();
        txtManufacturer = new TextBox();
        txtDistributor = new TextBox();
        txtLabel = new TextBox();
        txtAsin = new TextBox();
        txtApple = new TextBox();
        txtSpotify = new TextBox();
        txtNotes = new TextBox();
        btnProductNew = new Button();
        btnProductSave = new Button();
        btnProductDelete = new Button();

        lblDiscs = new Label();
        gridDiscs = new DataGridView();

        pnlDiscDetail = new Panel();
        txtCatalogNo = new TextBox();
        txtDiscTitle = new TextBox();
        txtDiscTitleShort = new TextBox();
        txtDiscTitleEn = new TextBox();
        cboDiscSeries = new ComboBox();
        numDiscNoInSet = new NumericUpDown();
        cboDiscKind = new ComboBox();
        cboMediaFormat = new ComboBox();
        txtMcn = new TextBox();
        numTotalTracks = new NumericUpDown();
        txtVolumeLabel = new TextBox();
        txtDiscNotes = new TextBox();
        btnDiscNew = new Button();
        btnDiscSave = new Button();
        btnDiscDelete = new Button();

        // ── 検索バー（最上段） ──
        pnlSearch.Dock = DockStyle.Top;
        pnlSearch.Height = 40;
        lblSearch.Text = "検索:";
        lblSearch.Location = new Point(8, 12);
        lblSearch.Size = new Size(40, 20);
        txtSearch.Location = new Point(50, 8);
        txtSearch.Size = new Size(260, 23);
        btnSearch.Text = "検索";
        btnSearch.Location = new Point(316, 8);
        btnSearch.Size = new Size(80, 25);
        btnReload.Text = "再読込";
        btnReload.Location = new Point(402, 8);
        btnReload.Size = new Size(80, 25);
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnSearch, btnReload });

        // ── splitMain: 左 商品一覧 / 右 詳細群 ──
        splitMain.Dock = DockStyle.Fill;
        splitMain.SplitterDistance = 360;
        splitMain.SplitterWidth = 6;

        // 商品一覧（左）
        gridProducts.Dock = DockStyle.Fill;
        gridProducts.AllowUserToAddRows = false;
        gridProducts.AllowUserToDeleteRows = false;
        gridProducts.ReadOnly = true;
        gridProducts.RowHeadersVisible = false;
        gridProducts.MultiSelect = false;
        gridProducts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridProducts.AutoGenerateColumns = false;
        gridProducts.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);
        splitMain.Panel1.Controls.Add(gridProducts);

        // ── splitRight: 右上 商品詳細 / 右下 ディスク群 ──
        splitRight.Dock = DockStyle.Fill;
        splitRight.Orientation = Orientation.Horizontal;
        splitRight.SplitterDistance = 420;
        splitRight.SplitterWidth = 6;
        splitMain.Panel2.Controls.Add(splitRight);

        // 右上: 商品詳細
        pnlProductDetail.Dock = DockStyle.Fill;
        pnlProductDetail.AutoScroll = true;
        splitRight.Panel1.Controls.Add(pnlProductDetail);

        // 商品詳細フィールドを 2 列 12 行程度で配置
        const int labelW = 100, fieldW = 260, rowH = 28;
        int py = 8;
        AddRow(pnlProductDetail, "代表品番", txtProductCatalogNo, py, labelW, fieldW); py += rowH;
        txtProductCatalogNo.MaxLength = 32;
        AddRow(pnlProductDetail, "タイトル", txtTitle, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "略称", txtTitleShort, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "英語タイトル", txtTitleEn, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "商品種別", cboKind, py, labelW, fieldW); py += rowH;
        cboKind.DropDownStyle = ComboBoxStyle.DropDownList;
        AddRow(pnlProductDetail, "発売日", dtRelease, py, labelW, fieldW); py += rowH;
        dtRelease.Format = DateTimePickerFormat.Short;
        AddRow(pnlProductDetail, "税抜価格", numPriceEx, py, labelW, fieldW); py += rowH;
        numPriceEx.Minimum = 0; numPriceEx.Maximum = 999999;
        // 税込価格行にはテキストの直後に「自動計算」ボタンを並べる
        AddRow(pnlProductDetail, "税込価格", numPriceInc, py, labelW, fieldW - 90);
        numPriceInc.Minimum = 0; numPriceInc.Maximum = 999999;
        btnAutoTax.Text = "自動計算";
        btnAutoTax.Location = new Point(12 + labelW + (fieldW - 86), py);
        btnAutoTax.Size = new Size(80, 23);
        pnlProductDetail.Controls.Add(btnAutoTax);
        py += rowH;
        AddRow(pnlProductDetail, "ディスク枚数", numDiscCount, py, labelW, fieldW); py += rowH;
        numDiscCount.Minimum = 1; numDiscCount.Maximum = 20;
        AddRow(pnlProductDetail, "発売元", txtManufacturer, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "販売元", txtDistributor, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "レーベル", txtLabel, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "Amazon ASIN", txtAsin, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "Apple Album ID", txtApple, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "Spotify Album ID", txtSpotify, py, labelW, fieldW); py += rowH;

        var lblNotes = new Label { Text = "備考", Location = new Point(8, py + 4), Size = new Size(labelW, 20) };
        txtNotes.Location = new Point(12 + labelW, py);
        txtNotes.Size = new Size(fieldW, 56);
        txtNotes.Multiline = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        pnlProductDetail.Controls.Add(lblNotes);
        pnlProductDetail.Controls.Add(txtNotes);

        // ボタン列（詳細パネル右端）
        int btnX = 12 + labelW + fieldW + 16;
        btnProductNew.Text = "新規"; btnProductNew.Location = new Point(btnX, 8); btnProductNew.Size = new Size(80, 28);
        btnProductSave.Text = "保存"; btnProductSave.Location = new Point(btnX, 40); btnProductSave.Size = new Size(80, 28);
        btnProductDelete.Text = "削除"; btnProductDelete.Location = new Point(btnX, 72); btnProductDelete.Size = new Size(80, 28);
        pnlProductDetail.Controls.AddRange(new Control[] { btnProductNew, btnProductSave, btnProductDelete });

        // ── 右下: splitDisc ディスク一覧 / ディスク詳細 ──
        splitDisc.Dock = DockStyle.Fill;
        splitDisc.SplitterDistance = 440;
        splitDisc.SplitterWidth = 6;
        splitRight.Panel2.Controls.Add(splitDisc);

        // ディスク一覧（右下左）
        lblDiscs.Text = "所属ディスク一覧";
        lblDiscs.Dock = DockStyle.Top;
        lblDiscs.Height = 22;
        lblDiscs.Padding = new Padding(6, 4, 0, 0);
        lblDiscs.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);
        gridDiscs.Dock = DockStyle.Fill;
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.RowHeadersVisible = false;
        gridDiscs.MultiSelect = false;
        gridDiscs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridDiscs.AutoGenerateColumns = false;
        splitDisc.Panel1.Controls.Add(gridDiscs);
        splitDisc.Panel1.Controls.Add(lblDiscs);

        // ディスク詳細（右下右）
        pnlDiscDetail.Dock = DockStyle.Fill;
        pnlDiscDetail.AutoScroll = true;
        splitDisc.Panel2.Controls.Add(pnlDiscDetail);

        const int dLabelW = 100, dFieldW = 240, dRowH = 28;
        int dy = 8;
        AddRow(pnlDiscDetail, "品番", txtCatalogNo, dy, dLabelW, dFieldW); dy += dRowH;
        txtCatalogNo.MaxLength = 32;
        AddRow(pnlDiscDetail, "組内番号", numDiscNoInSet, dy, dLabelW, dFieldW); dy += dRowH;
        numDiscNoInSet.Minimum = 0; numDiscNoInSet.Maximum = 99;
        AddRow(pnlDiscDetail, "ディスクタイトル", txtDiscTitle, dy, dLabelW, dFieldW); dy += dRowH;
        AddRow(pnlDiscDetail, "略称", txtDiscTitleShort, dy, dLabelW, dFieldW); dy += dRowH;
        AddRow(pnlDiscDetail, "英語タイトル", txtDiscTitleEn, dy, dLabelW, dFieldW); dy += dRowH;
        AddRow(pnlDiscDetail, "シリーズ", cboDiscSeries, dy, dLabelW, dFieldW); dy += dRowH;
        cboDiscSeries.DropDownStyle = ComboBoxStyle.DropDownList;
        AddRow(pnlDiscDetail, "ディスク種別", cboDiscKind, dy, dLabelW, dFieldW); dy += dRowH;
        cboDiscKind.DropDownStyle = ComboBoxStyle.DropDownList;
        AddRow(pnlDiscDetail, "メディア", cboMediaFormat, dy, dLabelW, dFieldW); dy += dRowH;
        cboMediaFormat.DropDownStyle = ComboBoxStyle.DropDownList;
        AddRow(pnlDiscDetail, "MCN", txtMcn, dy, dLabelW, dFieldW); dy += dRowH;
        AddRow(pnlDiscDetail, "総トラック数", numTotalTracks, dy, dLabelW, dFieldW); dy += dRowH;
        numTotalTracks.Minimum = 0; numTotalTracks.Maximum = 99;
        AddRow(pnlDiscDetail, "ボリュームラベル", txtVolumeLabel, dy, dLabelW, dFieldW); dy += dRowH;

        var lblDiscNotes = new Label { Text = "備考", Location = new Point(8, dy + 4), Size = new Size(dLabelW, 20) };
        txtDiscNotes.Location = new Point(12 + dLabelW, dy);
        txtDiscNotes.Size = new Size(dFieldW, 50);
        txtDiscNotes.Multiline = true;
        txtDiscNotes.ScrollBars = ScrollBars.Vertical;
        pnlDiscDetail.Controls.Add(lblDiscNotes);
        pnlDiscDetail.Controls.Add(txtDiscNotes);

        int dBtnX = 12 + dLabelW + dFieldW + 16;
        btnDiscNew.Text = "新規"; btnDiscNew.Location = new Point(dBtnX, 8); btnDiscNew.Size = new Size(80, 28);
        btnDiscSave.Text = "保存"; btnDiscSave.Location = new Point(dBtnX, 40); btnDiscSave.Size = new Size(80, 28);
        btnDiscDelete.Text = "削除"; btnDiscDelete.Location = new Point(dBtnX, 72); btnDiscDelete.Size = new Size(80, 28);
        pnlDiscDetail.Controls.AddRange(new Control[] { btnDiscNew, btnDiscSave, btnDiscDelete });

        // ── Form ──
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1400, 880);
        Controls.Add(splitMain);
        Controls.Add(pnlSearch);
        Name = "ProductDiscsEditorForm";
        Text = "商品・ディスク管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>ラベル + コントロールを指定 y 座標の行として配置する。</summary>
    private static void AddRow(Panel panel, string label, Control control, int y, int labelW, int fieldW)
    {
        var lbl = new Label { Text = label, Location = new Point(8, y + 4), Size = new Size(labelW, 20) };
        control.Location = new Point(12 + labelW, y);
        control.Size = new Size(fieldW, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}
