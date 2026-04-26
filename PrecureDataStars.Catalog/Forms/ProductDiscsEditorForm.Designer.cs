#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品・ディスク管理フォームのレイアウト定義（v1.1.3 新設、v1.1.4 でレイアウト刷新）。
/// <para>
/// 上下 2 段構成（v1.1.4 改）:
/// <list type="bullet">
///   <item>上段（商品エリア）: 左 60% に商品一覧、右 40% に商品詳細エディタ</item>
///   <item>下段（ディスクエリア、高さ 400 px 固定）: 左 60% に所属ディスク一覧、右 40% にディスク詳細エディタ</item>
/// </list>
/// 下段の固定高さ 400 px は「所属ディスク 10 行表示」と「ディスク詳細エディタ全フィールドの表示」の
/// 必要量のうち大きい方（後者）+ 余裕で算出。残りの縦領域はすべて上段（商品エリア）に割り当てられる。
/// 左右 60:40 の比率は SplitContainer の SizeChanged ハンドラ（実装側 .cs で接続）で常時維持される。
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

    // 分割コンテナ（v1.1.4 でレイアウト刷新）
    // splitMain は上下分割（上=商品エリア / 下=ディスクエリア、下部高さは Panel2 を 400 px で固定）
    // splitProduct は商品エリアの左右分割（左=商品一覧 / 右=商品詳細）
    // splitDisc はディスクエリアの左右分割（左=所属ディスク一覧 / 右=ディスク詳細）
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

        // ── splitMain: 上 商品エリア / 下 ディスクエリア（v1.1.4 改、上下分割） ──
        // FixedPanel = Panel2 で下段（ディスクエリア）の高さを 400 px に固定し、
        // 残りの縦方向領域はすべて上段（商品エリア）に割り当てる。
        // 下段の 400 px は「所属ディスク 10 行表示（≈ 264 px）」と「ディスク詳細エディタ
        // 全フィールド表示（≈ 366 px）」のうち大きい方 + 余裕の値。
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Horizontal;
        splitMain.FixedPanel = FixedPanel.Panel2;
        splitMain.SplitterWidth = 6;
        // SplitterDistance は実装側 .cs の Form.Load で「Height - 400 - SplitterWidth」に再設定される。
        // ここでは SplitContainer のデフォルトサイズ制約（Panel1MinSize=25, Panel2MinSize=25）に
        // 触れない安全な小さい値を入れておく。Designer 段階ではコンテナがまだ親に追加されていないため、
        // 大きな値を直接代入すると ArgumentOutOfRangeException を起こす可能性がある。
        splitMain.SplitterDistance = 100;

        // 商品エリア（上段）— splitProduct: 左 商品一覧 / 右 商品詳細（左 60% / 右 40%）
        splitProduct.Dock = DockStyle.Fill;
        splitProduct.Orientation = Orientation.Vertical;
        splitProduct.FixedPanel = FixedPanel.None;
        splitProduct.SplitterWidth = 6;
        // 60:40 の比率は実装側 .cs の SizeChanged ハンドラで都度 (Width × 0.6) に再計算される。
        // Designer 段階ではコンテナサイズがまだ確定していないので、安全な小さい値で初期化する。
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

        // 商品詳細フィールドを 2 列 12 行程度で配置。
        // v1.1.4 改: ラベル左端を x=18、入力欄左端を x=22+labelW に置くことで、パネル左端から
        // 10 px 程度の余白を確保する（閲覧 UI の pnlBody.Padding と同程度のゆとり）。
        // 入力欄は Anchor を付けず（Top|Left のみ）、初期幅は最小値として配置する。
        // パネル幅変更時の動的レイアウトは実装側 .cs の LayoutProductDetailPanel() で
        // 入力欄の Width とボタン群の Location.X を都度明示計算する方式に変更。
        // Anchor=Right + AutoScroll=true の組み合わせで起きるレイアウト循環バグを避けるための措置。
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
        // 税込価格行は数値入力 + 自動計算ボタンの組み合わせ。
        // v1.1.4 改: 両者の Anchor は付けず、実装側 .cs の LayoutProductDetailPanel() で
        // 「numPriceInc は固定幅 170、btnAutoTax はその直後」となるよう都度再配置する。
        AddRow(pnlProductDetail, "税込価格", numPriceInc, py, labelW, 170);
        numPriceInc.Minimum = 0; numPriceInc.Maximum = 999999;
        btnAutoTax.Text = "自動計算";
        btnAutoTax.Location = new Point(22 + labelW + 174, py);
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

        var lblNotes = new Label { Text = "備考", Location = new Point(18, py + 4), Size = new Size(labelW, 20) };
        txtNotes.Location = new Point(22 + labelW, py);
        txtNotes.Size = new Size(fieldW, 56);
        // v1.1.4 改: 備考も Anchor を付けず、実装側 .cs で動的に Width を再計算する。
        txtNotes.Multiline = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        pnlProductDetail.Controls.Add(lblNotes);
        pnlProductDetail.Controls.Add(txtNotes);

        // ボタン列（詳細パネル右端）。
        // v1.1.4 改: Anchor は付けず、実装側 .cs の LayoutProductDetailPanel() で
        // パネル幅から都度 Location.X を計算して再配置する。
        // ここの Location.X は Designer プレビュー用の便宜値（実時には書き換えられる）。
        btnProductNew.Text = "新規"; btnProductNew.Size = new Size(80, 28);
        btnProductSave.Text = "保存"; btnProductSave.Size = new Size(80, 28);
        btnProductDelete.Text = "削除"; btnProductDelete.Size = new Size(80, 28);
        btnProductNew.Location = new Point(22 + labelW + fieldW + 16, 8);
        btnProductSave.Location = new Point(22 + labelW + fieldW + 16, 40);
        btnProductDelete.Location = new Point(22 + labelW + fieldW + 16, 72);
        pnlProductDetail.Controls.AddRange(new Control[] { btnProductNew, btnProductSave, btnProductDelete });

        // ── 下段: splitDisc ディスク一覧 / ディスク詳細（v1.1.4 改、左右 60:40） ──
        splitDisc.Dock = DockStyle.Fill;
        splitDisc.Orientation = Orientation.Vertical;
        splitDisc.FixedPanel = FixedPanel.None;
        splitDisc.SplitterWidth = 6;
        // 60:40 の比率は実装側 .cs の SizeChanged ハンドラで都度 (Width × 0.6) に再計算される。
        // Designer 段階ではコンテナサイズがまだ確定していないので、安全な小さい値で初期化する。
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

        // v1.1.4 改: ラベル左端 x=18、入力欄左端 x=22+dLabelW で外周余白を確保。
        // 入力欄は Anchor を付けず、初期幅は最小値として配置。動的レイアウトは
        // 実装側 .cs の LayoutDiscDetailPanel() で都度計算する。
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
        // v1.1.4 改: 備考も Anchor を付けず、実装側 .cs で動的に Width を再計算する。
        txtDiscNotes.Multiline = true;
        txtDiscNotes.ScrollBars = ScrollBars.Vertical;
        pnlDiscDetail.Controls.Add(lblDiscNotes);
        pnlDiscDetail.Controls.Add(txtDiscNotes);

        // ボタン列（v1.1.4 改: Anchor は付けず、実装側 .cs で都度再配置）
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
        // v1.1.4 改: 一般的なディスプレイ（1366×768 のノート PC を含む）に収まるサイズに縮小。
        // 商品エディタの全フィールドは縦 484 px 必要だが、上段に確保できるのは
        // 820 - 40(検索バー) - 6(splitter) - 400(下段) = 374 px のため、
        // pnlProductDetail.AutoScroll = true で縦スクロールを許容する設計。
        ClientSize = new Size(1200, 820);
        // 親フォームよりも大きくなる場合に CenterParent では画面外にはみ出るため、
        // 画面中央基準でセンタリングする。
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(splitMain);
        Controls.Add(pnlSearch);
        Name = "ProductDiscsEditorForm";
        Text = "商品・ディスク管理 - Catalog";
    }

    /// <summary>
    /// ラベル + 入力コントロールを指定 y 座標の行として配置する。
    /// v1.1.4 改: ラベル左端を x=18 に、入力欄左端を x=22+labelW に置き、パネル左端からの
    /// 視覚的な余白（≈ 10 px）を確保する。Anchor は付けず Top|Left のデフォルトのまま。
    /// パネル幅変更時の動的レイアウトは実装側 .cs の <c>LayoutProductDetailPanel</c> /
    /// <c>LayoutDiscDetailPanel</c> が入力欄の Width とボタン群の Location を都度計算する方式
    /// （Anchor=Right と AutoScroll=true の組み合わせで起きる WinForms のレイアウト循環バグを避けるため）。
    /// </summary>
    private static void AddRow(Panel panel, string label, Control control, int y, int labelW, int fieldW)
    {
        var lbl = new Label { Text = label, Location = new Point(18, y + 4), Size = new Size(labelW, 20) };
        control.Location = new Point(22 + labelW, y);
        control.Size = new Size(fieldW, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}