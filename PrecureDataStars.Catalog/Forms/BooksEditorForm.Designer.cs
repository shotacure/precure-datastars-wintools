#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class BooksEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private SplitContainer splitMain = null!;

    // ── 左ペイン：検索 + 一覧 ──
    private Panel pnlListHeader = null!;
    private TextBox txtFilter = null!;
    private Button btnReload = null!;
    private DataGridView gridItems = null!;

    // ── 右ペイン：タブ 3 枚 ──
    // 書籍は項目数が多いので 1 面に詰め込まず、「基本情報 / 所属 / クレジット」に分ける。
    private TabControl tabDetail = null!;
    private TabPage tabBasic = null!;
    private TabPage tabBelonging = null!;
    private TabPage tabCredits = null!;

    // 基本情報タブ
    private Panel pnlBasic = null!;
    private NumericUpDown numId = null!;
    private TextBox txtTitle = null!;
    private TextBox txtTitleKana = null!;
    private TextBox txtTitleEn = null!;
    private ComboBox cmbPublisher = null!;
    private DateTimePicker dtRelease = null!;
    private CheckBox chkHasKindleDate = null!;
    private DateTimePicker dtReleaseKindle = null!;
    private TextBox txtIsbn13 = null!;
    private NumericUpDown numPageCount = null!;
    private TextBox txtTrimSize = null!;
    private TextBox txtBindingText = null!;
    private NumericUpDown numPriceExTax = null!;
    private NumericUpDown numPriceIncTax = null!;
    private NumericUpDown numPriceKindle = null!;
    private CheckBox chkHasPrint = null!;
    private CheckBox chkHasKindle = null!;
    private TextBox txtAsinPrint = null!;
    private TextBox txtAsinKindle = null!;
    private Button btnAmazonSearch = null!;
    private ComboBox cmbCoverSource = null!;
    private CheckBox chkCoverShowBoth = null!;
    private TextBox txtOfficialUrl = null!;
    private TextBox txtNotes = null!;

    // 所属タブ（シリーズ・ジャンル）
    private Panel pnlBelonging = null!;
    private CheckedListBox clbSeries = null!;
    private CheckedListBox clbGenres = null!;
    private ComboBox cmbPrimaryGenre = null!;

    // クレジットタブ
    private Panel pnlCredits = null!;
    private DataGridView gridCredits = null!;
    private Button btnCreditAdd = null!;
    private Button btnCreditRemove = null!;

    // 共通ボタン列
    private Panel pnlButtons = null!;
    private Button btnNew = null!;
    private Button btnSave = null!;
    private Button btnDelete = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// レイアウト初期化。左に書籍一覧（絞り込みボックス付き）、右にタブ 3 枚の詳細ペインを置く。
    /// SplitContainer の SplitterDistance / PanelXMinSize は Designer 時点では
    /// 既定幅（150）に対して評価されて弾かれるため、実値は Form.Load 側で設定する
    /// （ProductCompaniesEditorForm と同じ流儀）。
    /// </summary>
    private void InitializeComponent()
    {
        splitMain = new SplitContainer();
        pnlListHeader = new Panel();
        txtFilter = new TextBox();
        btnReload = new Button();
        gridItems = new DataGridView();

        tabDetail = new TabControl();
        tabBasic = new TabPage();
        tabBelonging = new TabPage();
        tabCredits = new TabPage();
        pnlBasic = new Panel();
        pnlBelonging = new Panel();
        pnlCredits = new Panel();
        pnlButtons = new Panel();

        numId = new NumericUpDown();
        txtTitle = new TextBox();
        txtTitleKana = new TextBox();
        txtTitleEn = new TextBox();
        cmbPublisher = new ComboBox();
        dtRelease = new DateTimePicker();
        chkHasKindleDate = new CheckBox();
        dtReleaseKindle = new DateTimePicker();
        txtIsbn13 = new TextBox();
        numPageCount = new NumericUpDown();
        txtTrimSize = new TextBox();
        txtBindingText = new TextBox();
        numPriceExTax = new NumericUpDown();
        numPriceIncTax = new NumericUpDown();
        numPriceKindle = new NumericUpDown();
        chkHasPrint = new CheckBox();
        chkHasKindle = new CheckBox();
        txtAsinPrint = new TextBox();
        txtAsinKindle = new TextBox();
        btnAmazonSearch = new Button();
        cmbCoverSource = new ComboBox();
        chkCoverShowBoth = new CheckBox();
        txtOfficialUrl = new TextBox();
        txtNotes = new TextBox();

        clbSeries = new CheckedListBox();
        clbGenres = new CheckedListBox();
        cmbPrimaryGenre = new ComboBox();

        gridCredits = new DataGridView();
        btnCreditAdd = new Button();
        btnCreditRemove = new Button();

        btnNew = new Button();
        btnSave = new Button();
        btnDelete = new Button();

        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Vertical;
        splitMain.SplitterWidth = 6;
        splitMain.SplitterDistance = 100;

        // ── 左ペイン ──
        pnlListHeader.Dock = DockStyle.Top;
        pnlListHeader.Height = 32;

        txtFilter.Location = new Point(8, 5);
        txtFilter.Size = new Size(220, 23);
        txtFilter.PlaceholderText = "書名・ISBN で絞り込み";
        pnlListHeader.Controls.Add(txtFilter);

        btnReload.Text = "再読込";
        btnReload.Location = new Point(236, 4);
        btnReload.Size = new Size(80, 24);
        pnlListHeader.Controls.Add(btnReload);

        gridItems.Dock = DockStyle.Fill;
        gridItems.AllowUserToAddRows = false;
        gridItems.AllowUserToDeleteRows = false;
        gridItems.ReadOnly = true;
        gridItems.RowHeadersVisible = false;
        gridItems.MultiSelect = false;
        gridItems.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridItems.AutoGenerateColumns = false;
        gridItems.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);

        splitMain.Panel1.Controls.Add(gridItems);
        splitMain.Panel1.Controls.Add(pnlListHeader);

        // ── 右ペイン ──
        pnlButtons.Dock = DockStyle.Bottom;
        pnlButtons.Height = 40;
        btnNew.Text = "新規";  btnNew.Size = new Size(80, 28);  btnNew.Location = new Point(8, 6);
        btnSave.Text = "保存"; btnSave.Size = new Size(80, 28); btnSave.Location = new Point(96, 6);
        btnDelete.Text = "削除"; btnDelete.Size = new Size(80, 28); btnDelete.Location = new Point(184, 6);
        pnlButtons.Controls.AddRange(new Control[] { btnNew, btnSave, btnDelete });

        tabDetail.Dock = DockStyle.Fill;
        tabBasic.Text = "基本情報";
        tabBelonging.Text = "シリーズ・ジャンル";
        tabCredits.Text = "クレジット";

        // ── 基本情報タブ ──
        pnlBasic.Dock = DockStyle.Fill;
        pnlBasic.AutoScroll = true;

        const int labelW = 110;
        const int fieldX = 22 + labelW;
        const int fieldW = 300;
        const int rowH = 28;
        int y = 8;

        AddRow(pnlBasic, "ID", numId, y, labelW, fieldW); y += rowH;
        numId.Minimum = 0; numId.Maximum = int.MaxValue; numId.ReadOnly = true;
        numId.Increment = 0; numId.BackColor = SystemColors.Control;

        AddRow(pnlBasic, "書名", txtTitle, y, labelW, fieldW); y += rowH;
        txtTitle.MaxLength = 255;
        AddRow(pnlBasic, "読み", txtTitleKana, y, labelW, fieldW); y += rowH;
        txtTitleKana.MaxLength = 255;
        AddRow(pnlBasic, "英題", txtTitleEn, y, labelW, fieldW); y += rowH;
        txtTitleEn.MaxLength = 255;

        AddRow(pnlBasic, "出版社", cmbPublisher, y, labelW, fieldW); y += rowH;
        cmbPublisher.DropDownStyle = ComboBoxStyle.DropDownList;

        AddRow(pnlBasic, "発売日", dtRelease, y, labelW, fieldW); y += rowH;
        dtRelease.Format = DateTimePickerFormat.Short;

        // Kindle 配信日は「紙と別日のときだけ入れる」任意項目なので、チェックで有効化する。
        chkHasKindleDate.Text = "Kindle 配信日を別に持つ";
        chkHasKindleDate.Location = new Point(fieldX, y + 4);
        chkHasKindleDate.AutoSize = true;
        pnlBasic.Controls.Add(chkHasKindleDate);
        y += rowH;
        AddRow(pnlBasic, "Kindle 配信日", dtReleaseKindle, y, labelW, fieldW); y += rowH;
        dtReleaseKindle.Format = DateTimePickerFormat.Short;
        dtReleaseKindle.Enabled = false;

        AddRow(pnlBasic, "ISBN13", txtIsbn13, y, labelW, fieldW); y += rowH;
        txtIsbn13.MaxLength = 13;

        AddRow(pnlBasic, "ページ数", numPageCount, y, labelW, 120); y += rowH;
        numPageCount.Minimum = 0; numPageCount.Maximum = 65535;

        AddRow(pnlBasic, "判型", txtTrimSize, y, labelW, fieldW); y += rowH;
        txtTrimSize.MaxLength = 32;
        AddRow(pnlBasic, "装丁(Amazon)", txtBindingText, y, labelW, fieldW); y += rowH;
        txtBindingText.MaxLength = 64;

        // 価格は 3 種。紙の定価は Amazon から取り込まない方針なので、ここが唯一の入力口になる。
        AddRow(pnlBasic, "紙 税抜", numPriceExTax, y, labelW, 120); y += rowH;
        numPriceExTax.Minimum = 0; numPriceExTax.Maximum = 1000000;
        AddRow(pnlBasic, "紙 税込", numPriceIncTax, y, labelW, 120); y += rowH;
        numPriceIncTax.Minimum = 0; numPriceIncTax.Maximum = 1000000;
        AddRow(pnlBasic, "Kindle 税込", numPriceKindle, y, labelW, 120); y += rowH;
        numPriceKindle.Minimum = 0; numPriceKindle.Maximum = 1000000;

        chkHasPrint.Text = "紙版あり";
        chkHasPrint.Location = new Point(fieldX, y + 4);
        chkHasPrint.AutoSize = true;
        pnlBasic.Controls.Add(chkHasPrint);
        chkHasKindle.Text = "Kindle 版あり";
        chkHasKindle.Location = new Point(fieldX + 110, y + 4);
        chkHasKindle.AutoSize = true;
        pnlBasic.Controls.Add(chkHasKindle);
        y += rowH;

        AddRow(pnlBasic, "紙 ASIN", txtAsinPrint, y, labelW, 160);
        btnAmazonSearch.Text = "Amazon 検索...";
        btnAmazonSearch.Location = new Point(fieldX + 168, y - 1);
        btnAmazonSearch.Size = new Size(120, 25);
        pnlBasic.Controls.Add(btnAmazonSearch);
        y += rowH;
        txtAsinPrint.MaxLength = 16;

        AddRow(pnlBasic, "Kindle ASIN", txtAsinKindle, y, labelW, 160); y += rowH;
        txtAsinKindle.MaxLength = 16;

        AddRow(pnlBasic, "代表書影", cmbCoverSource, y, labelW, 160); y += rowH;
        cmbCoverSource.DropDownStyle = ComboBoxStyle.DropDownList;

        chkCoverShowBoth.Text = "詳細ページで紙・Kindle 両方の書影を並べる";
        chkCoverShowBoth.Location = new Point(fieldX, y + 4);
        chkCoverShowBoth.AutoSize = true;
        pnlBasic.Controls.Add(chkCoverShowBoth);
        y += rowH;

        AddRow(pnlBasic, "公式 URL", txtOfficialUrl, y, labelW, fieldW); y += rowH;
        txtOfficialUrl.MaxLength = 1024;

        var lblNotes = new Label { Text = "備考", Location = new Point(18, y + 4), Size = new Size(labelW, 20) };
        txtNotes.Location = new Point(fieldX, y);
        txtNotes.Size = new Size(fieldW, 70);
        txtNotes.Multiline = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        pnlBasic.Controls.Add(lblNotes);
        pnlBasic.Controls.Add(txtNotes);

        tabBasic.Controls.Add(pnlBasic);

        // ── シリーズ・ジャンルタブ ──
        // どちらも多対多なのでチェックリストで持つ。代表ジャンルは索引カードのバッジに出る 1 件。
        pnlBelonging.Dock = DockStyle.Fill;
        pnlBelonging.Padding = new Padding(12);

        var lblSeries = new Label { Text = "シリーズ（チェックなし＝オールスターズ・横断）", Location = new Point(12, 10), AutoSize = true };
        clbSeries.Location = new Point(12, 32);
        clbSeries.Size = new Size(320, 300);
        clbSeries.CheckOnClick = true;

        var lblGenres = new Label { Text = "ジャンル", Location = new Point(350, 10), AutoSize = true };
        clbGenres.Location = new Point(350, 32);
        clbGenres.Size = new Size(240, 240);
        clbGenres.CheckOnClick = true;

        var lblPrimary = new Label { Text = "代表ジャンル", Location = new Point(350, 282), AutoSize = true };
        cmbPrimaryGenre.Location = new Point(350, 302);
        cmbPrimaryGenre.Size = new Size(240, 23);
        cmbPrimaryGenre.DropDownStyle = ComboBoxStyle.DropDownList;

        pnlBelonging.Controls.AddRange(new Control[] { lblSeries, clbSeries, lblGenres, clbGenres, lblPrimary, cmbPrimaryGenre });
        tabBelonging.Controls.Add(pnlBelonging);

        // ── クレジットタブ ──
        // 役職 + （名義 ID または表記テキスト）の行を直接編集する。名義 ID 未入力の行は
        // フリーテキスト扱いになり、サイト側ではリンクなしで表示される。
        pnlCredits.Dock = DockStyle.Fill;

        var pnlCreditButtons = new Panel { Dock = DockStyle.Top, Height = 36 };
        btnCreditAdd.Text = "行を追加"; btnCreditAdd.Location = new Point(8, 5); btnCreditAdd.Size = new Size(90, 26);
        btnCreditRemove.Text = "行を削除"; btnCreditRemove.Location = new Point(104, 5); btnCreditRemove.Size = new Size(90, 26);
        pnlCreditButtons.Controls.AddRange(new Control[] { btnCreditAdd, btnCreditRemove });

        gridCredits.Dock = DockStyle.Fill;
        gridCredits.AllowUserToAddRows = false;
        gridCredits.AllowUserToDeleteRows = false;
        gridCredits.RowHeadersVisible = false;
        gridCredits.MultiSelect = false;
        gridCredits.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridCredits.AutoGenerateColumns = false;
        gridCredits.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);

        pnlCredits.Controls.Add(gridCredits);
        pnlCredits.Controls.Add(pnlCreditButtons);
        tabCredits.Controls.Add(pnlCredits);

        tabDetail.TabPages.AddRange(new[] { tabBasic, tabBelonging, tabCredits });

        splitMain.Panel2.Controls.Add(tabDetail);
        splitMain.Panel2.Controls.Add(pnlButtons);

        // ── Form ──
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1180, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(splitMain);
        Name = "BooksEditorForm";
        Text = "書籍管理 - Catalog";
    }

    /// <summary>
    /// ラベル + 入力コントロールを指定 y 座標の行として配置する。
    /// 他のエディタフォームと同じく、ラベル左端を x=18 に、入力欄左端を x=22+labelW に置く。
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
