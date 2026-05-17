#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品・ディスク管理フォームのレイアウト定義。
/// <para>
/// 上下 2 段構成:
/// <list type="bullet">
///   <item>上段（商品エリア）: 左 60% に商品一覧、右 40% に商品詳細エディタ</item>
///   <item>下段（ディスクエリア、高さ 400 px 固定）: 左 60% に所属ディスク一覧、右 40% にディスク詳細エディタ</item>
/// </list>
/// </para>
/// <para>
/// 商品詳細パネルの流通系フィールドは
/// 「レーベル（社名マスタ）」「販売元（社名マスタ）」の 2 行で構成する。
/// 発売元／販売元／レーベルは社名マスタ（product_companies）への ID 紐付けで持ち、
/// UI に自由入力欄は持たない。
/// 各社名行は [ReadOnly 表示テキスト] + [選択...] + [解除] の 3 コントロールで構成し、
/// 「選択...」で <see cref="Pickers.ProductCompanyPickerDialog"/> を起動する。
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
    private SplitContainer splitMain = null!;
    private SplitContainer splitProduct = null!;
    private SplitContainer splitDisc = null!;

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

    // 流通系は社名マスタ紐付け 2 行のみ。
    // 表示順は「レーベル → 販売元」で統一する（DB の列順とも合致）。
    // 各行は [ReadOnly 表示テキスト] + [選択...] + [解除] の 3 コントロール構成。
    private TextBox txtLabelCompanyName = null!;
    private Button btnLabelCompanyPick = null!;
    private Button btnLabelCompanyClear = null!;
    private TextBox txtDistributorCompanyName = null!;
    private Button btnDistributorCompanyPick = null!;
    private Button btnDistributorCompanyClear = null!;

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
        splitProduct = new SplitContainer();
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
        // 社名マスタ紐付け 2 行ぶんのコントロール群
        txtLabelCompanyName = new TextBox();
        btnLabelCompanyPick = new Button();
        btnLabelCompanyClear = new Button();
        txtDistributorCompanyName = new TextBox();
        btnDistributorCompanyPick = new Button();
        btnDistributorCompanyClear = new Button();
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

        // ── splitMain: 上 商品エリア / 下 ディスクエリア ──
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Horizontal;
        splitMain.FixedPanel = FixedPanel.Panel2;
        splitMain.SplitterWidth = 6;
        splitMain.SplitterDistance = 100;

        // 商品エリア（上段）
        splitProduct.Dock = DockStyle.Fill;
        splitProduct.Orientation = Orientation.Vertical;
        splitProduct.FixedPanel = FixedPanel.None;
        splitProduct.SplitterWidth = 6;
        splitProduct.SplitterDistance = 100;
        splitMain.Panel1.Controls.Add(splitProduct);

        // 商品一覧（上段左）
        gridProducts.Dock = DockStyle.Fill;
        gridProducts.AllowUserToAddRows = false;
        gridProducts.AllowUserToDeleteRows = false;
        gridProducts.ReadOnly = true;
        gridProducts.RowHeadersVisible = false;
        gridProducts.MultiSelect = false;
        gridProducts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridProducts.AutoGenerateColumns = false;
        gridProducts.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);
        splitProduct.Panel1.Controls.Add(gridProducts);

        // 商品詳細（上段右）
        pnlProductDetail.Dock = DockStyle.Fill;
        pnlProductDetail.AutoScroll = true;
        splitProduct.Panel2.Controls.Add(pnlProductDetail);

        // 商品詳細フィールドの配置。
        // 
        // 「レーベル（屋号）」「販売元（屋号）」の社名マスタ紐付け行 2 つに集約。
        const int labelW = 100, fieldW = 220, rowH = 28;
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
        AddRow(pnlProductDetail, "税込価格", numPriceInc, py, labelW, 170);
        numPriceInc.Minimum = 0; numPriceInc.Maximum = 999999;
        btnAutoTax.Text = "自動計算";
        btnAutoTax.Location = new Point(22 + labelW + 174, py);
        btnAutoTax.Size = new Size(80, 23);
        pnlProductDetail.Controls.Add(btnAutoTax);
        py += rowH;
        AddRow(pnlProductDetail, "ディスク枚数", numDiscCount, py, labelW, fieldW); py += rowH;
        numDiscCount.Minimum = 1; numDiscCount.Maximum = 20;

        // 流通系 2 行（レーベル → 販売元の順）。
        // 各行は [ReadOnly 表示テキスト] + [選択...] + [解除] の 3 コントロール。
        // 実装側 .cs の LayoutProductDetailPanel() で fieldW から ReadOnly テキスト幅と
        // ボタン位置を都度再計算する。
        AddCompanyRow(pnlProductDetail, "レーベル",
            txtLabelCompanyName, btnLabelCompanyPick, btnLabelCompanyClear, py, labelW, fieldW);
        py += rowH;
        AddCompanyRow(pnlProductDetail, "販売元",
            txtDistributorCompanyName, btnDistributorCompanyPick, btnDistributorCompanyClear, py, labelW, fieldW);
        py += rowH;

        AddRow(pnlProductDetail, "Amazon ASIN", txtAsin, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "Apple Album ID", txtApple, py, labelW, fieldW); py += rowH;
        AddRow(pnlProductDetail, "Spotify Album ID", txtSpotify, py, labelW, fieldW); py += rowH;

        var lblNotes = new Label { Text = "備考", Location = new Point(18, py + 4), Size = new Size(labelW, 20) };
        txtNotes.Location = new Point(22 + labelW, py);
        txtNotes.Size = new Size(fieldW, 56);
        txtNotes.Multiline = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        pnlProductDetail.Controls.Add(lblNotes);
        pnlProductDetail.Controls.Add(txtNotes);

        // ボタン列（詳細パネル右端）。
        btnProductNew.Text = "新規"; btnProductNew.Size = new Size(80, 28);
        btnProductSave.Text = "保存"; btnProductSave.Size = new Size(80, 28);
        btnProductDelete.Text = "削除"; btnProductDelete.Size = new Size(80, 28);
        btnProductNew.Location = new Point(22 + labelW + fieldW + 16, 8);
        btnProductSave.Location = new Point(22 + labelW + fieldW + 16, 40);
        btnProductDelete.Location = new Point(22 + labelW + fieldW + 16, 72);
        pnlProductDetail.Controls.AddRange(new Control[] { btnProductNew, btnProductSave, btnProductDelete });

        // ── 下段: splitDisc ディスク一覧 / ディスク詳細 ──
        splitDisc.Dock = DockStyle.Fill;
        splitDisc.Orientation = Orientation.Vertical;
        splitDisc.FixedPanel = FixedPanel.None;
        splitDisc.SplitterWidth = 6;
        splitDisc.SplitterDistance = 100;
        splitMain.Panel2.Controls.Add(splitDisc);

        // ディスク一覧（下段左）
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

        // ディスク詳細（下段右）
        pnlDiscDetail.Dock = DockStyle.Fill;
        pnlDiscDetail.AutoScroll = true;
        splitDisc.Panel2.Controls.Add(pnlDiscDetail);

        const int dLabelW = 100, dFieldW = 220, dRowH = 28;
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

        var lblDiscNotes = new Label { Text = "備考", Location = new Point(18, dy + 4), Size = new Size(dLabelW, 20) };
        txtDiscNotes.Location = new Point(22 + dLabelW, dy);
        txtDiscNotes.Size = new Size(dFieldW, 50);
        txtDiscNotes.Multiline = true;
        txtDiscNotes.ScrollBars = ScrollBars.Vertical;
        pnlDiscDetail.Controls.Add(lblDiscNotes);
        pnlDiscDetail.Controls.Add(txtDiscNotes);

        btnDiscNew.Text = "新規"; btnDiscNew.Size = new Size(80, 28);
        btnDiscSave.Text = "保存"; btnDiscSave.Size = new Size(80, 28);
        btnDiscDelete.Text = "削除"; btnDiscDelete.Size = new Size(80, 28);
        btnDiscNew.Location = new Point(22 + dLabelW + dFieldW + 16, 8);
        btnDiscSave.Location = new Point(22 + dLabelW + dFieldW + 16, 40);
        btnDiscDelete.Location = new Point(22 + dLabelW + dFieldW + 16, 72);
        pnlDiscDetail.Controls.AddRange(new Control[] { btnDiscNew, btnDiscSave, btnDiscDelete });

        // ── Form ──
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        // 旧フリーテキスト 3 行（発売元 + 販売元 + レーベル）を
        // 「社名屋号」2 行構成のため、縦高さは 820 で十分。
        ClientSize = new Size(1200, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(splitMain);
        Controls.Add(pnlSearch);
        Name = "ProductDiscsEditorForm";
        Text = "商品・ディスク管理 - Catalog";
    }

    /// <summary>
    /// ラベル + 入力コントロールを指定 y 座標の行として配置する。
    /// </summary>
    private static void AddRow(Panel panel, string label, Control control, int y, int labelW, int fieldW)
    {
        var lbl = new Label { Text = label, Location = new Point(18, y + 4), Size = new Size(labelW, 20) };
        control.Location = new Point(22 + labelW, y);
        control.Size = new Size(fieldW, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }

    /// <summary>
    /// 社名マスタ紐付け行を 1 行追加する。
    /// [表示名 ReadOnly TextBox] + [選択...] + [解除] の 3 コントロールで構成する。
    /// 表示名 TextBox はクリック編集禁止（実値は <c>Tag</c> に保持する int? の ID）。
    /// 動的幅は <c>LayoutProductDetailPanel</c> で再計算される。
    /// </summary>
    private static void AddCompanyRow(Panel panel, string label,
        TextBox txtName, Button btnPick, Button btnClear,
        int y, int labelW, int fieldW)
    {
        var lbl = new Label { Text = label, Location = new Point(18, y + 4), Size = new Size(labelW, 20) };

        // 表示名 ReadOnly TextBox（実値は Tag に int? として保持。NULL=未紐付け）
        txtName.Location = new Point(22 + labelW, y);
        txtName.Size = new Size(fieldW - 116, 23);  // ボタン 2 つ分（48 + 4 + 60）= 112 + 余白 = 116 を引いた幅
        txtName.ReadOnly = true;
        txtName.BackColor = SystemColors.Control;
        txtName.Tag = null;  // 未紐付け状態

        btnPick.Text = "選択...";
        btnPick.Location = new Point(22 + labelW + (fieldW - 116) + 4, y);
        btnPick.Size = new Size(60, 23);

        btnClear.Text = "解除";
        btnClear.Location = new Point(22 + labelW + (fieldW - 116) + 4 + 60 + 4, y);
        btnClear.Size = new Size(48, 23);

        panel.Controls.Add(lbl);
        panel.Controls.Add(txtName);
        panel.Controls.Add(btnPick);
        panel.Controls.Add(btnClear);
    }
}