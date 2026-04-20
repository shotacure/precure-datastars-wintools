#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class ProductsEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private SplitContainer splitMain = null!;
    private DataGridView gridProducts = null!;
    private SplitContainer splitRight = null!;

    // 詳細フォーム（上段）
    private Panel pnlDetail = null!;
    private TextBox txtProductCatalogNo = null!;
    private TextBox txtTitle = null!;
    private TextBox txtTitleShort = null!;
    private TextBox txtTitleEn = null!;
    private ComboBox cboKind = null!;
    // v1.1.1: cboSeries は撤去（シリーズは Disc 側の属性に移設された）
    private DateTimePicker dtRelease = null!;
    private NumericUpDown numPriceEx = null!;
    private NumericUpDown numPriceInc = null!;
    private NumericUpDown numDiscCount = null!;
    private TextBox txtManufacturer = null!;
    private TextBox txtDistributor = null!;
    private TextBox txtLabel = null!;
    private TextBox txtAsin = null!;
    private TextBox txtApple = null!;
    private TextBox txtSpotify = null!;
    private TextBox txtNotes = null!;

    private Button btnNew = null!;
    private Button btnSave = null!;
    private Button btnDelete = null!;
    private Button btnReload = null!;

    // 所属ディスク一覧（下段）
    private DataGridView gridDiscs = null!;
    private Label lblDiscs = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        splitMain = new SplitContainer();
        gridProducts = new DataGridView();
        splitRight = new SplitContainer();
        pnlDetail = new Panel();
        txtProductCatalogNo = new TextBox();
        txtTitle = new TextBox();
        txtTitleShort = new TextBox();
        txtTitleEn = new TextBox();
        cboKind = new ComboBox();
        dtRelease = new DateTimePicker();
        numPriceEx = new NumericUpDown();
        numPriceInc = new NumericUpDown();
        numDiscCount = new NumericUpDown();
        txtManufacturer = new TextBox();
        txtDistributor = new TextBox();
        txtLabel = new TextBox();
        txtAsin = new TextBox();
        txtApple = new TextBox();
        txtSpotify = new TextBox();
        txtNotes = new TextBox();
        btnNew = new Button();
        btnSave = new Button();
        btnDelete = new Button();
        btnReload = new Button();
        gridDiscs = new DataGridView();
        lblDiscs = new Label();

        // splitMain (左: 商品一覧 / 右: 詳細+所属ディスク)
        splitMain.Dock = DockStyle.Fill;
        splitMain.SplitterDistance = 480;

        // 左: 商品一覧
        gridProducts.Dock = DockStyle.Fill;
        gridProducts.AllowUserToAddRows = false;
        gridProducts.AllowUserToDeleteRows = false;
        gridProducts.ReadOnly = true;
        gridProducts.MultiSelect = false;
        gridProducts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridProducts.RowHeadersVisible = false;
        gridProducts.AutoGenerateColumns = true;
        splitMain.Panel1.Controls.Add(gridProducts);

        // 右: splitRight (上: 詳細 / 下: 所属ディスク)
        splitRight.Dock = DockStyle.Fill;
        splitRight.Orientation = Orientation.Horizontal;
        splitRight.SplitterDistance = 380;
        splitMain.Panel2.Controls.Add(splitRight);

        // pnlDetail (上段)
        pnlDetail.Dock = DockStyle.Fill;
        pnlDetail.AutoScroll = true;
        splitRight.Panel1.Controls.Add(pnlDetail);

        // 詳細フォーム配置（2 カラム）：共通定数を以下に定義し、実座標は AddRow ローカル関数と
        // 下部の Notes/ボタン配置で rowH * rowIndex の形で計算する。
        // v1.1.1: シリーズ行を撤去したため、以降の行インデックスが 1 つずつ繰り上がっている。
        const int labelW = 100;
        const int fieldW = 200;
        const int rowH = 28;

        AddRow("代表品番", txtProductCatalogNo, 0);
        txtProductCatalogNo.MaxLength = 32;
        AddRow("タイトル", txtTitle, 1);
        AddRow("略称", txtTitleShort, 2);
        AddRow("英語タイトル", txtTitleEn, 3);
        AddRow("商品種別", cboKind, 4);
        cboKind.DropDownStyle = ComboBoxStyle.DropDownList;
        // v1.1.1: シリーズ行を撤去（Disc 側で設定する）
        AddRow("発売日", dtRelease, 5);
        dtRelease.Format = DateTimePickerFormat.Short;
        AddRow("税抜価格", numPriceEx, 6);
        numPriceEx.Minimum = 0; numPriceEx.Maximum = 999999;
        AddRow("税込価格", numPriceInc, 7);
        numPriceInc.Minimum = 0; numPriceInc.Maximum = 999999;
        AddRow("ディスク枚数", numDiscCount, 8);
        numDiscCount.Minimum = 1; numDiscCount.Maximum = 20;
        AddRow("発売元", txtManufacturer, 9);
        AddRow("販売元", txtDistributor, 10);
        AddRow("レーベル", txtLabel, 11);
        AddRow("Amazon ASIN", txtAsin, 12);
        AddRow("Apple Album ID", txtApple, 13);
        AddRow("Spotify Album ID", txtSpotify, 14);

        // Notes（広め）
        // v1.1.1: 上段の行数が 1 減ったため、Notes 配置の rowIndex も 16 → 15 へ詰める。
        var lblNotes = new Label { Text = "備考", Location = new Point(8, 8 + 15 * rowH), Size = new Size(labelW, 20) };
        txtNotes.Location = new Point(12 + labelW, 8 + 15 * rowH);
        txtNotes.Size = new Size(fieldW + 120, 60);
        txtNotes.Multiline = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        pnlDetail.Controls.Add(lblNotes);
        pnlDetail.Controls.Add(txtNotes);

        // ボタン行（右上）
        btnNew.Text = "新規"; btnNew.Location = new Point(12 + labelW + fieldW + 130, 8); btnNew.Size = new Size(80, 28);
        btnSave.Text = "保存"; btnSave.Location = new Point(12 + labelW + fieldW + 130, 40); btnSave.Size = new Size(80, 28);
        btnDelete.Text = "削除"; btnDelete.Location = new Point(12 + labelW + fieldW + 130, 72); btnDelete.Size = new Size(80, 28);
        btnReload.Text = "再読込"; btnReload.Location = new Point(12 + labelW + fieldW + 130, 104); btnReload.Size = new Size(80, 28);
        pnlDetail.Controls.AddRange(new Control[] { btnNew, btnSave, btnDelete, btnReload });

        // 下段: 所属ディスク
        lblDiscs.Text = "所属ディスク一覧（参照表示のみ。編集はディスク管理画面で）";
        lblDiscs.Dock = DockStyle.Top;
        lblDiscs.Height = 20;
        gridDiscs.Dock = DockStyle.Fill;
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.RowHeadersVisible = false;
        splitRight.Panel2.Controls.Add(gridDiscs);
        splitRight.Panel2.Controls.Add(lblDiscs);

        // Form
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1240, 720);
        Controls.Add(splitMain);
        Name = "ProductsEditorForm";
        Text = "商品管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
        return;

        // ローカル関数: ラベル + コントロールを pnlDetail に配置
        void AddRow(string label, Control control, int rowIndex)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(8, 8 + rowIndex * rowH + 4),
                Size = new Size(labelW, 20)
            };
            control.Location = new Point(12 + labelW, 8 + rowIndex * rowH);
            control.Size = new Size(fieldW, 23);
            pnlDetail.Controls.Add(lbl);
            pnlDetail.Controls.Add(control);
        }
    }
}
