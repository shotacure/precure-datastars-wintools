#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class MastersEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private TabControl tabControl = null!;

    // 7 タブ（product_kinds / disc_kinds / track_content_kinds /
    //        song_music_classes / song_size_variants / song_part_variants /
    //        bgm_sessions）
    private TabPage tabProductKinds = null!;
    private TabPage tabDiscKinds = null!;
    private TabPage tabTrackContentKinds = null!;
    private TabPage tabSongMusicClasses = null!;
    private TabPage tabSongSizeVariants = null!;
    private TabPage tabSongPartVariants = null!;
    private TabPage tabBgmSessions = null!;

    // 各タブのグリッドと入力欄
    private DataGridView gridProductKinds = null!;
    private TextBox txtPkCode = null!;
    private TextBox txtPkNameJa = null!;
    private TextBox txtPkNameEn = null!;
    private NumericUpDown numPkOrder = null!;
    private Button btnSaveProductKind = null!;
    private Button btnDeleteProductKind = null!;

    private DataGridView gridDiscKinds = null!;
    private TextBox txtDkCode = null!;
    private TextBox txtDkNameJa = null!;
    private TextBox txtDkNameEn = null!;
    private NumericUpDown numDkOrder = null!;
    private Button btnSaveDiscKind = null!;
    private Button btnDeleteDiscKind = null!;

    private DataGridView gridTrackContentKinds = null!;
    private TextBox txtTcCode = null!;
    private TextBox txtTcNameJa = null!;
    private TextBox txtTcNameEn = null!;
    private NumericUpDown numTcOrder = null!;
    private Button btnSaveTrackContentKind = null!;
    private Button btnDeleteTrackContentKind = null!;

    private DataGridView gridSongMusicClasses = null!;
    private TextBox txtSmcCode = null!;
    private TextBox txtSmcNameJa = null!;
    private TextBox txtSmcNameEn = null!;
    private NumericUpDown numSmcOrder = null!;
    private Button btnSaveSongMusicClass = null!;
    private Button btnDeleteSongMusicClass = null!;

    private DataGridView gridSongSizeVariants = null!;
    private TextBox txtSsvCode = null!;
    private TextBox txtSsvNameJa = null!;
    private TextBox txtSsvNameEn = null!;
    private NumericUpDown numSsvOrder = null!;
    private Button btnSaveSongSizeVariant = null!;
    private Button btnDeleteSongSizeVariant = null!;

    private DataGridView gridSongPartVariants = null!;
    private TextBox txtSpvCode = null!;
    private TextBox txtSpvNameJa = null!;
    private TextBox txtSpvNameEn = null!;
    private NumericUpDown numSpvOrder = null!;
    private Button btnSaveSongPartVariant = null!;
    private Button btnDeleteSongPartVariant = null!;

    // bgm_sessions タブ（シリーズ × セッション）専用コントロール
    private ComboBox cboBgmSessionSeries = null!;
    private DataGridView gridBgmSessions = null!;
    private NumericUpDown numBgmSessionNo = null!;  // 表示用（session_no。編集不可で既存行の番号を示す）
    private TextBox txtBgmSessionName = null!;
    private TextBox txtBgmSessionNotes = null!;
    private Button btnAddBgmSession = null!;
    private Button btnSaveBgmSession = null!;
    private Button btnDeleteBgmSession = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 1 タブ分の UI（グリッド + 入力欄 + 保存/削除ボタン）を共通手順で作成する。
    /// </summary>
    private static void BuildTab(
        TabPage page, DataGridView grid,
        TextBox txtCode, TextBox txtJa, TextBox txtEn, NumericUpDown numOrder,
        Button btnSave, Button btnDelete)
    {
        // グリッド（上半分）
        grid.Dock = DockStyle.Top;
        grid.Height = 360;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // 入力欄（下半分）
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var lblC = new Label { Text = "コード", Location = new Point(8, 12), Size = new Size(90, 20) };
        txtCode.Location = new Point(100, 10);
        txtCode.Size = new Size(200, 23);

        var lblJ = new Label { Text = "名称(日)", Location = new Point(8, 42), Size = new Size(90, 20) };
        txtJa.Location = new Point(100, 40);
        txtJa.Size = new Size(200, 23);

        var lblE = new Label { Text = "名称(英)", Location = new Point(8, 72), Size = new Size(90, 20) };
        txtEn.Location = new Point(100, 70);
        txtEn.Size = new Size(200, 23);

        var lblO = new Label { Text = "表示順", Location = new Point(8, 102), Size = new Size(90, 20) };
        numOrder.Location = new Point(100, 100);
        numOrder.Size = new Size(80, 23);
        numOrder.Minimum = 0;
        numOrder.Maximum = 255;

        btnSave.Text = "保存 / 更新";
        btnSave.Location = new Point(320, 10);
        btnSave.Size = new Size(110, 28);

        btnDelete.Text = "選択行を削除";
        btnDelete.Location = new Point(320, 42);
        btnDelete.Size = new Size(110, 28);

        pnl.Controls.AddRange(new Control[] { lblC, txtCode, lblJ, txtJa, lblE, txtEn, lblO, numOrder, btnSave, btnDelete });
        page.Controls.Add(pnl);
        page.Controls.Add(grid);

        // 行選択 → 入力欄に反映
        grid.SelectionChanged += (_, __) =>
        {
            if (grid.CurrentRow is null) return;
            // 1 列目がコード、2 列目が日本語名、3 列目が英語名、4 列目が表示順
            // マスタごとに列名が異なるが、常に 4 列で並ぶ前提
            var c = grid.CurrentRow.Cells[0].Value?.ToString() ?? "";
            var j = grid.CurrentRow.Cells[1].Value?.ToString() ?? "";
            var e = grid.CurrentRow.Cells[2].Value?.ToString() ?? "";
            var o = grid.CurrentRow.Cells[3].Value;
            txtCode.Text = c;
            txtJa.Text = j;
            txtEn.Text = e;
            numOrder.Value = o is null ? 0 : System.Convert.ToInt32(o);
        };
    }

    private void InitializeComponent()
    {
        tabControl = new TabControl();
        tabProductKinds = new TabPage { Text = "商品種別" };
        tabDiscKinds = new TabPage { Text = "ディスク種別" };
        tabTrackContentKinds = new TabPage { Text = "トラック内容" };
        tabSongMusicClasses = new TabPage { Text = "曲・音楽種別" };
        tabSongSizeVariants = new TabPage { Text = "曲・サイズ種別" };
        tabSongPartVariants = new TabPage { Text = "曲・パート種別" };
        tabBgmSessions = new TabPage { Text = "劇伴・セッション" };

        // インスタンス生成
        gridProductKinds = new DataGridView();
        txtPkCode = new TextBox(); txtPkNameJa = new TextBox(); txtPkNameEn = new TextBox();
        numPkOrder = new NumericUpDown();
        btnSaveProductKind = new Button(); btnDeleteProductKind = new Button();

        gridDiscKinds = new DataGridView();
        txtDkCode = new TextBox(); txtDkNameJa = new TextBox(); txtDkNameEn = new TextBox();
        numDkOrder = new NumericUpDown();
        btnSaveDiscKind = new Button(); btnDeleteDiscKind = new Button();

        gridTrackContentKinds = new DataGridView();
        txtTcCode = new TextBox(); txtTcNameJa = new TextBox(); txtTcNameEn = new TextBox();
        numTcOrder = new NumericUpDown();
        btnSaveTrackContentKind = new Button(); btnDeleteTrackContentKind = new Button();

        gridSongMusicClasses = new DataGridView();
        txtSmcCode = new TextBox(); txtSmcNameJa = new TextBox(); txtSmcNameEn = new TextBox();
        numSmcOrder = new NumericUpDown();
        btnSaveSongMusicClass = new Button(); btnDeleteSongMusicClass = new Button();

        gridSongSizeVariants = new DataGridView();
        txtSsvCode = new TextBox(); txtSsvNameJa = new TextBox(); txtSsvNameEn = new TextBox();
        numSsvOrder = new NumericUpDown();
        btnSaveSongSizeVariant = new Button(); btnDeleteSongSizeVariant = new Button();

        gridSongPartVariants = new DataGridView();
        txtSpvCode = new TextBox(); txtSpvNameJa = new TextBox(); txtSpvNameEn = new TextBox();
        numSpvOrder = new NumericUpDown();
        btnSaveSongPartVariant = new Button(); btnDeleteSongPartVariant = new Button();

        // bgm_sessions タブ用
        cboBgmSessionSeries = new ComboBox();
        gridBgmSessions = new DataGridView();
        numBgmSessionNo = new NumericUpDown();
        txtBgmSessionName = new TextBox();
        txtBgmSessionNotes = new TextBox();
        btnAddBgmSession = new Button();
        btnSaveBgmSession = new Button();
        btnDeleteBgmSession = new Button();

        // 各タブを共通手順で構築
        BuildTab(tabProductKinds, gridProductKinds, txtPkCode, txtPkNameJa, txtPkNameEn, numPkOrder, btnSaveProductKind, btnDeleteProductKind);
        BuildTab(tabDiscKinds, gridDiscKinds, txtDkCode, txtDkNameJa, txtDkNameEn, numDkOrder, btnSaveDiscKind, btnDeleteDiscKind);
        BuildTab(tabTrackContentKinds, gridTrackContentKinds, txtTcCode, txtTcNameJa, txtTcNameEn, numTcOrder, btnSaveTrackContentKind, btnDeleteTrackContentKind);
        BuildTab(tabSongMusicClasses, gridSongMusicClasses, txtSmcCode, txtSmcNameJa, txtSmcNameEn, numSmcOrder, btnSaveSongMusicClass, btnDeleteSongMusicClass);
        BuildTab(tabSongSizeVariants, gridSongSizeVariants, txtSsvCode, txtSsvNameJa, txtSsvNameEn, numSsvOrder, btnSaveSongSizeVariant, btnDeleteSongSizeVariant);
        BuildTab(tabSongPartVariants, gridSongPartVariants, txtSpvCode, txtSpvNameJa, txtSpvNameEn, numSpvOrder, btnSaveSongPartVariant, btnDeleteSongPartVariant);

        // bgm_sessions タブ（独自レイアウト：シリーズ選択コンボ + セッション一覧 + 編集フォーム）
        BuildBgmSessionsTab();

        // ボタンイベントは本体（MastersEditorForm.cs）のメソッドに接続
        btnSaveProductKind.Click += btnSaveProductKind_Click;
        btnDeleteProductKind.Click += btnDeleteProductKind_Click;
        btnSaveDiscKind.Click += btnSaveDiscKind_Click;
        btnDeleteDiscKind.Click += btnDeleteDiscKind_Click;
        btnSaveTrackContentKind.Click += btnSaveTrackContentKind_Click;
        btnDeleteTrackContentKind.Click += btnDeleteTrackContentKind_Click;
        btnSaveSongMusicClass.Click += btnSaveSongMusicClass_Click;
        btnDeleteSongMusicClass.Click += btnDeleteSongMusicClass_Click;
        btnSaveSongSizeVariant.Click += btnSaveSongSizeVariant_Click;
        btnDeleteSongSizeVariant.Click += btnDeleteSongSizeVariant_Click;
        btnSaveSongPartVariant.Click += btnSaveSongPartVariant_Click;
        btnDeleteSongPartVariant.Click += btnDeleteSongPartVariant_Click;
        // bgm_sessions タブのボタンハンドラは本体（MastersEditorForm.cs）のコンストラクタ内で接続する。

        // tabControl にタブを追加
        tabControl.Dock = DockStyle.Fill;
        tabControl.TabPages.AddRange(new TabPage[]
        {
            tabProductKinds, tabDiscKinds, tabTrackContentKinds,
            tabSongMusicClasses, tabSongSizeVariants, tabSongPartVariants,
            tabBgmSessions
        });

        // フォーム全体
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 560);
        Controls.Add(tabControl);
        Name = "MastersEditorForm";
        Text = "マスタ管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>
    /// bgm_sessions タブを構築する。他のマスタタブと異なり、シリーズ ID 選択コンボが必要で、
    /// PK が 2 列 (series_id, session_no) なので共通 BuildTab は使えず、専用配置する。
    /// </summary>
    private void BuildBgmSessionsTab()
    {
        // 上部：シリーズ選択
        var lblSeries = new Label { Text = "シリーズ", Location = new Point(8, 12), Size = new Size(70, 20) };
        cboBgmSessionSeries.Location = new Point(80, 8);
        cboBgmSessionSeries.Size = new Size(320, 23);
        cboBgmSessionSeries.DropDownStyle = ComboBoxStyle.DropDownList;

        // グリッド：選択シリーズのセッション一覧
        gridBgmSessions.Location = new Point(8, 40);
        gridBgmSessions.Size = new Size(860, 320);
        gridBgmSessions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        gridBgmSessions.AllowUserToAddRows = false;
        gridBgmSessions.AllowUserToDeleteRows = false;
        gridBgmSessions.ReadOnly = true;
        gridBgmSessions.MultiSelect = false;
        gridBgmSessions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridBgmSessions.RowHeadersVisible = false;
        gridBgmSessions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // 下段：編集フォーム
        int fy = 370;
        var lblNo = new Label { Text = "No（参照）", Location = new Point(8, fy + 4), Size = new Size(100, 20) };
        numBgmSessionNo.Location = new Point(110, fy);
        numBgmSessionNo.Size = new Size(80, 23);
        numBgmSessionNo.ReadOnly = true;
        numBgmSessionNo.Enabled = false;
        numBgmSessionNo.Maximum = 255;

        var lblName = new Label { Text = "セッション名", Location = new Point(8, fy + 34), Size = new Size(100, 20) };
        txtBgmSessionName.Location = new Point(110, fy + 30);
        txtBgmSessionName.Size = new Size(360, 23);

        var lblNotes = new Label { Text = "備考", Location = new Point(8, fy + 64), Size = new Size(100, 20) };
        txtBgmSessionNotes.Location = new Point(110, fy + 60);
        txtBgmSessionNotes.Size = new Size(360, 60);
        txtBgmSessionNotes.Multiline = true;

        btnAddBgmSession.Text = "新規追加";
        btnAddBgmSession.Location = new Point(490, fy);
        btnAddBgmSession.Size = new Size(110, 28);

        btnSaveBgmSession.Text = "保存 / 更新";
        btnSaveBgmSession.Location = new Point(490, fy + 32);
        btnSaveBgmSession.Size = new Size(110, 28);

        btnDeleteBgmSession.Text = "選択行を削除";
        btnDeleteBgmSession.Location = new Point(490, fy + 64);
        btnDeleteBgmSession.Size = new Size(110, 28);

        tabBgmSessions.Controls.AddRange(new Control[]
        {
            lblSeries, cboBgmSessionSeries,
            gridBgmSessions,
            lblNo, numBgmSessionNo,
            lblName, txtBgmSessionName,
            lblNotes, txtBgmSessionNotes,
            btnAddBgmSession, btnSaveBgmSession, btnDeleteBgmSession
        });
    }
}
