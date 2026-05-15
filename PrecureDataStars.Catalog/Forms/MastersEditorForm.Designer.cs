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
    // 6 つのマスタタブ（bgm_sessions を除く）はいずれも 4 ボタン構成（新規 / 保存・更新 / 削除 / 並べ替えを反映）。
    private DataGridView gridProductKinds = null!;
    private TextBox txtPkCode = null!;
    private TextBox txtPkNameJa = null!;
    private TextBox txtPkNameEn = null!;
    private NumericUpDown numPkOrder = null!;
    private Button btnNewProductKind = null!;
    private Button btnSaveProductKind = null!;
    private Button btnDeleteProductKind = null!;
    private Button btnApplyOrderProductKind = null!;

    private DataGridView gridDiscKinds = null!;
    private TextBox txtDkCode = null!;
    private TextBox txtDkNameJa = null!;
    private TextBox txtDkNameEn = null!;
    private NumericUpDown numDkOrder = null!;
    private Button btnNewDiscKind = null!;
    private Button btnSaveDiscKind = null!;
    private Button btnDeleteDiscKind = null!;
    private Button btnApplyOrderDiscKind = null!;

    private DataGridView gridTrackContentKinds = null!;
    private TextBox txtTcCode = null!;
    private TextBox txtTcNameJa = null!;
    private TextBox txtTcNameEn = null!;
    private NumericUpDown numTcOrder = null!;
    private Button btnNewTrackContentKind = null!;
    private Button btnSaveTrackContentKind = null!;
    private Button btnDeleteTrackContentKind = null!;
    private Button btnApplyOrderTrackContentKind = null!;

    private DataGridView gridSongMusicClasses = null!;
    private TextBox txtSmcCode = null!;
    private TextBox txtSmcNameJa = null!;
    private TextBox txtSmcNameEn = null!;
    private NumericUpDown numSmcOrder = null!;
    private Button btnNewSongMusicClass = null!;
    private Button btnSaveSongMusicClass = null!;
    private Button btnDeleteSongMusicClass = null!;
    private Button btnApplyOrderSongMusicClass = null!;

    private DataGridView gridSongSizeVariants = null!;
    private TextBox txtSsvCode = null!;
    private TextBox txtSsvNameJa = null!;
    private TextBox txtSsvNameEn = null!;
    private NumericUpDown numSsvOrder = null!;
    private Button btnNewSongSizeVariant = null!;
    private Button btnSaveSongSizeVariant = null!;
    private Button btnDeleteSongSizeVariant = null!;
    private Button btnApplyOrderSongSizeVariant = null!;

    private DataGridView gridSongPartVariants = null!;
    private TextBox txtSpvCode = null!;
    private TextBox txtSpvNameJa = null!;
    private TextBox txtSpvNameEn = null!;
    private NumericUpDown numSpvOrder = null!;
    private Button btnNewSongPartVariant = null!;
    private Button btnSaveSongPartVariant = null!;
    private Button btnDeleteSongPartVariant = null!;
    private Button btnApplyOrderSongPartVariant = null!;

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
    /// 1 タブ分の UI（グリッド + 入力欄 + 4 ボタン）を共通手順で作成する。
    /// <para>
    /// ボタンは 4 つ（新規 / 保存・更新 / 選択行を削除 / 並べ替えを反映）で、
    /// 「新規追加」と「既存行の更新」の操作を明確に分ける。TabPage と入力欄パネルには
    /// 余白を持たせ、コントロールがパネル端に密着しないようにする（閲覧 UI の pnlBody.Padding と
    /// 同程度のゆとり）。グリッドの行ドラッグ&ドロップによる並べ替えと、その結果を一斉反映する
    /// 「並べ替えを反映」ボタンの実体ロジックは本体（MastersEditorForm.cs）側で接続する。
    /// </para>
    /// </summary>
    private static void BuildTab(
        TabPage page, DataGridView grid,
        TextBox txtCode, TextBox txtJa, TextBox txtEn, NumericUpDown numOrder,
        Button btnNew, Button btnSave, Button btnDelete, Button btnApplyOrder)
    {
        // TabPage 自体に外周余白を入れて、グリッドと入力欄パネルがタブ枠に密着しないようにする
        page.Padding = new Padding(8);

        // グリッド（上半分）
        grid.Dock = DockStyle.Top;
        grid.Height = 320;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        // 行ドラッグ&ドロップを受け入れるため AllowDrop を有効化。
        // 実際のドラッグ開始/移動/ドロップ処理は本体（MastersEditorForm.cs）で接続する。
        grid.AllowDrop = true;

        // 入力欄パネル（下半分）。Padding は 18 で左右上下にゆとりを確保。
        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };

        // 入力欄: ラベル左端 x=18、入力欄左端 x=118 で、パネル左端から ≈10 px の視覚余白を確保。
        // 各行の Y は 18, 50, 82, 114（行間 32 px）で、4 行分のボタン列と高さを揃える。
        var lblC = new Label { Text = "コード", Location = new Point(18, 22), Size = new Size(90, 20) };
        txtCode.Location = new Point(118, 18);
        txtCode.Size = new Size(200, 23);

        var lblJ = new Label { Text = "名称(日)", Location = new Point(18, 54), Size = new Size(90, 20) };
        txtJa.Location = new Point(118, 50);
        txtJa.Size = new Size(200, 23);

        var lblE = new Label { Text = "名称(英)", Location = new Point(18, 86), Size = new Size(90, 20) };
        txtEn.Location = new Point(118, 82);
        txtEn.Size = new Size(200, 23);

        var lblO = new Label { Text = "表示順", Location = new Point(18, 118), Size = new Size(90, 20) };
        numOrder.Location = new Point(118, 114);
        numOrder.Size = new Size(80, 23);
        numOrder.Minimum = 0;
        numOrder.Maximum = 255;

        // ボタン列（右側、4 ボタン縦並び）。幅 130 で「並べ替えを反映」が収まるサイズ。
        // 入力欄列の右端 318 + ギャップ 22 = 340 を起点とする。
        const int btnX = 340;
        const int btnW = 130;
        const int btnH = 28;

        btnNew.Text = "新規";
        btnNew.Location = new Point(btnX, 18);
        btnNew.Size = new Size(btnW, btnH);

        btnSave.Text = "保存 / 更新";
        btnSave.Location = new Point(btnX, 50);
        btnSave.Size = new Size(btnW, btnH);

        btnDelete.Text = "選択行を削除";
        btnDelete.Location = new Point(btnX, 82);
        btnDelete.Size = new Size(btnW, btnH);

        btnApplyOrder.Text = "並べ替えを反映";
        btnApplyOrder.Location = new Point(btnX, 114);
        btnApplyOrder.Size = new Size(btnW, btnH);

        pnl.Controls.AddRange(new Control[]
        {
            lblC, txtCode, lblJ, txtJa, lblE, txtEn, lblO, numOrder,
            btnNew, btnSave, btnDelete, btnApplyOrder
        });
        page.Controls.Add(pnl);
        page.Controls.Add(grid);

        // 行選択 → 入力欄に反映。
        // 1 列目がコード、2 列目が日本語名、3 列目が英語名、4 列目が表示順という前提
        // （マスタごとに列名が異なるが常に 4 列で並ぶ）。CreatedAt/UpdatedAt 等の監査列は
        // 本体（MastersEditorForm.cs）側で DataBindingComplete 時に Visible=false にされる。
        grid.SelectionChanged += (_, __) =>
        {
            if (grid.CurrentRow is null) return;
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
        btnNewProductKind = new Button();
        btnSaveProductKind = new Button();
        btnDeleteProductKind = new Button();
        btnApplyOrderProductKind = new Button();

        gridDiscKinds = new DataGridView();
        txtDkCode = new TextBox(); txtDkNameJa = new TextBox(); txtDkNameEn = new TextBox();
        numDkOrder = new NumericUpDown();
        btnNewDiscKind = new Button();
        btnSaveDiscKind = new Button();
        btnDeleteDiscKind = new Button();
        btnApplyOrderDiscKind = new Button();

        gridTrackContentKinds = new DataGridView();
        txtTcCode = new TextBox(); txtTcNameJa = new TextBox(); txtTcNameEn = new TextBox();
        numTcOrder = new NumericUpDown();
        btnNewTrackContentKind = new Button();
        btnSaveTrackContentKind = new Button();
        btnDeleteTrackContentKind = new Button();
        btnApplyOrderTrackContentKind = new Button();

        gridSongMusicClasses = new DataGridView();
        txtSmcCode = new TextBox(); txtSmcNameJa = new TextBox(); txtSmcNameEn = new TextBox();
        numSmcOrder = new NumericUpDown();
        btnNewSongMusicClass = new Button();
        btnSaveSongMusicClass = new Button();
        btnDeleteSongMusicClass = new Button();
        btnApplyOrderSongMusicClass = new Button();

        gridSongSizeVariants = new DataGridView();
        txtSsvCode = new TextBox(); txtSsvNameJa = new TextBox(); txtSsvNameEn = new TextBox();
        numSsvOrder = new NumericUpDown();
        btnNewSongSizeVariant = new Button();
        btnSaveSongSizeVariant = new Button();
        btnDeleteSongSizeVariant = new Button();
        btnApplyOrderSongSizeVariant = new Button();

        gridSongPartVariants = new DataGridView();
        txtSpvCode = new TextBox(); txtSpvNameJa = new TextBox(); txtSpvNameEn = new TextBox();
        numSpvOrder = new NumericUpDown();
        btnNewSongPartVariant = new Button();
        btnSaveSongPartVariant = new Button();
        btnDeleteSongPartVariant = new Button();
        btnApplyOrderSongPartVariant = new Button();

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
        BuildTab(tabProductKinds, gridProductKinds, txtPkCode, txtPkNameJa, txtPkNameEn, numPkOrder,
            btnNewProductKind, btnSaveProductKind, btnDeleteProductKind, btnApplyOrderProductKind);
        BuildTab(tabDiscKinds, gridDiscKinds, txtDkCode, txtDkNameJa, txtDkNameEn, numDkOrder,
            btnNewDiscKind, btnSaveDiscKind, btnDeleteDiscKind, btnApplyOrderDiscKind);
        BuildTab(tabTrackContentKinds, gridTrackContentKinds, txtTcCode, txtTcNameJa, txtTcNameEn, numTcOrder,
            btnNewTrackContentKind, btnSaveTrackContentKind, btnDeleteTrackContentKind, btnApplyOrderTrackContentKind);
        BuildTab(tabSongMusicClasses, gridSongMusicClasses, txtSmcCode, txtSmcNameJa, txtSmcNameEn, numSmcOrder,
            btnNewSongMusicClass, btnSaveSongMusicClass, btnDeleteSongMusicClass, btnApplyOrderSongMusicClass);
        BuildTab(tabSongSizeVariants, gridSongSizeVariants, txtSsvCode, txtSsvNameJa, txtSsvNameEn, numSsvOrder,
            btnNewSongSizeVariant, btnSaveSongSizeVariant, btnDeleteSongSizeVariant, btnApplyOrderSongSizeVariant);
        BuildTab(tabSongPartVariants, gridSongPartVariants, txtSpvCode, txtSpvNameJa, txtSpvNameEn, numSpvOrder,
            btnNewSongPartVariant, btnSaveSongPartVariant, btnDeleteSongPartVariant, btnApplyOrderSongPartVariant);

        // bgm_sessions タブ（独自レイアウト：シリーズ選択コンボ + セッション一覧 + 編集フォーム）
        BuildBgmSessionsTab();

        // ボタンイベントは本体（MastersEditorForm.cs）のメソッドに接続。
        // 改: 「新規」「並べ替えを反映」のクリックハンドラも追加。
        btnNewProductKind.Click += btnNewProductKind_Click;
        btnSaveProductKind.Click += btnSaveProductKind_Click;
        btnDeleteProductKind.Click += btnDeleteProductKind_Click;
        btnApplyOrderProductKind.Click += btnApplyOrderProductKind_Click;
        btnNewDiscKind.Click += btnNewDiscKind_Click;
        btnSaveDiscKind.Click += btnSaveDiscKind_Click;
        btnDeleteDiscKind.Click += btnDeleteDiscKind_Click;
        btnApplyOrderDiscKind.Click += btnApplyOrderDiscKind_Click;
        btnNewTrackContentKind.Click += btnNewTrackContentKind_Click;
        btnSaveTrackContentKind.Click += btnSaveTrackContentKind_Click;
        btnDeleteTrackContentKind.Click += btnDeleteTrackContentKind_Click;
        btnApplyOrderTrackContentKind.Click += btnApplyOrderTrackContentKind_Click;
        btnNewSongMusicClass.Click += btnNewSongMusicClass_Click;
        btnSaveSongMusicClass.Click += btnSaveSongMusicClass_Click;
        btnDeleteSongMusicClass.Click += btnDeleteSongMusicClass_Click;
        btnApplyOrderSongMusicClass.Click += btnApplyOrderSongMusicClass_Click;
        btnNewSongSizeVariant.Click += btnNewSongSizeVariant_Click;
        btnSaveSongSizeVariant.Click += btnSaveSongSizeVariant_Click;
        btnDeleteSongSizeVariant.Click += btnDeleteSongSizeVariant_Click;
        btnApplyOrderSongSizeVariant.Click += btnApplyOrderSongSizeVariant_Click;
        btnNewSongPartVariant.Click += btnNewSongPartVariant_Click;
        btnSaveSongPartVariant.Click += btnSaveSongPartVariant_Click;
        btnDeleteSongPartVariant.Click += btnDeleteSongPartVariant_Click;
        btnApplyOrderSongPartVariant.Click += btnApplyOrderSongPartVariant_Click;
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
        ClientSize = new Size(1000, 680);
        Controls.Add(tabControl);
        Name = "MastersEditorForm";
        Text = "マスタ管理 - Catalog";
        // 親フォームよりも大きい場合に CenterParent では画面外にはみ出るため、
        // 画面中央基準でセンタリングする。
        StartPosition = FormStartPosition.CenterScreen;
    }

    /// <summary>
    /// bgm_sessions タブを構築する。他のマスタタブと異なり、シリーズ ID 選択コンボが必要で、
    /// PK が 2 列 (series_id, session_no) なので共通 BuildTab は使えず、専用配置する。
    /// <para>
    /// 改: TabPage に外周余白（Padding=8）を追加し、各コントロール座標も +10 シフトして
    /// パネル端との視覚的余白を確保。session_no が表示順を兼ねるため、行ドラッグ&ドロップによる
    /// 並べ替え機能はこのタブには適用しない。
    /// </para>
    /// </summary>
    private void BuildBgmSessionsTab()
    {
        // TabPage 自体に外周余白を入れて、コントロールがタブ枠に密着しないようにする
        tabBgmSessions.Padding = new Padding(8);

        // 上部：シリーズ選択
        var lblSeries = new Label { Text = "シリーズ", Location = new Point(18, 22), Size = new Size(70, 20) };
        cboBgmSessionSeries.Location = new Point(90, 18);
        cboBgmSessionSeries.Size = new Size(320, 23);
        cboBgmSessionSeries.DropDownStyle = ComboBoxStyle.DropDownList;

        // グリッド：選択シリーズのセッション一覧
        gridBgmSessions.Location = new Point(18, 50);
        gridBgmSessions.Size = new Size(940, 320);
        gridBgmSessions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        gridBgmSessions.AllowUserToAddRows = false;
        gridBgmSessions.AllowUserToDeleteRows = false;
        gridBgmSessions.ReadOnly = true;
        gridBgmSessions.MultiSelect = false;
        gridBgmSessions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridBgmSessions.RowHeadersVisible = false;
        gridBgmSessions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // 下段：編集フォーム
        int fy = 380;
        var lblNo = new Label { Text = "No（参照）", Location = new Point(18, fy + 4), Size = new Size(100, 20) };
        numBgmSessionNo.Location = new Point(120, fy);
        numBgmSessionNo.Size = new Size(80, 23);
        numBgmSessionNo.ReadOnly = true;
        numBgmSessionNo.Enabled = false;
        numBgmSessionNo.Maximum = 255;

        var lblName = new Label { Text = "セッション名", Location = new Point(18, fy + 34), Size = new Size(100, 20) };
        txtBgmSessionName.Location = new Point(120, fy + 30);
        txtBgmSessionName.Size = new Size(360, 23);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, fy + 64), Size = new Size(100, 20) };
        txtBgmSessionNotes.Location = new Point(120, fy + 60);
        txtBgmSessionNotes.Size = new Size(360, 60);
        txtBgmSessionNotes.Multiline = true;

        btnAddBgmSession.Text = "新規追加";
        btnAddBgmSession.Location = new Point(500, fy);
        btnAddBgmSession.Size = new Size(130, 28);

        btnSaveBgmSession.Text = "保存 / 更新";
        btnSaveBgmSession.Location = new Point(500, fy + 32);
        btnSaveBgmSession.Size = new Size(130, 28);

        btnDeleteBgmSession.Text = "選択行を削除";
        btnDeleteBgmSession.Location = new Point(500, fy + 64);
        btnDeleteBgmSession.Size = new Size(130, 28);

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
