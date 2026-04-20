#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class SongsEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // 検索バー（最上段）
    private Panel pnlSearch = null!;
    private Label lblSearch = null!;
    private TextBox txtSearch = null!;
    private Label lblSeriesFilter = null!;
    private ComboBox cboSeriesFilter = null!;
    private Label lblMusicClassFilter = null!;
    private ComboBox cboMusicClassFilter = null!;
    private Button btnSearch = null!;

    private SplitContainer splitMain = null!;  // 上段: 曲, 下段: 録音 + 収録トラック
    private SplitContainer splitSong = null!;  // 上段内: 左 曲一覧, 右 曲詳細
    private SplitContainer splitRecOuter = null!;  // 下段内: 左 録音（一覧＋詳細）, 右 収録トラック
    private SplitContainer splitRec = null!;   // 下段左内: 左 録音一覧, 右 録音詳細

    private DataGridView gridSongs = null!;
    private Panel pnlSongDetail = null!;
    private NumericUpDown numSongId = null!;
    private TextBox txtTitle = null!;
    private TextBox txtTitleKana = null!;
    private ComboBox cboMusicClass = null!;
    private ComboBox cboSeries = null!;
    private TextBox txtLyricist = null!;
    private TextBox txtLyricistKana = null!;
    private TextBox txtComposer = null!;
    private TextBox txtComposerKana = null!;
    private TextBox txtArranger = null!;
    private TextBox txtArrangerKana = null!;
    private TextBox txtSongNotes = null!;
    private Button btnSongNew = null!;
    private Button btnSongSave = null!;
    private Button btnSongDelete = null!;

    private DataGridView gridRecordings = null!;
    private Panel pnlRecDetail = null!;
    private NumericUpDown numRecId = null!;
    private TextBox txtSinger = null!;
    private TextBox txtSingerKana = null!;
    private TextBox txtVariantLabel = null!;
    private TextBox txtRecNotes = null!;
    private Button btnRecNew = null!;
    private Button btnRecSave = null!;
    private Button btnRecDelete = null!;

    // 右下：選択中の歌唱者バージョンの収録ディスク・トラック一覧
    private Label lblRecTracks = null!;
    private DataGridView gridRecTracks = null!;

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
        lblSeriesFilter = new Label();
        cboSeriesFilter = new ComboBox();
        lblMusicClassFilter = new Label();
        cboMusicClassFilter = new ComboBox();
        btnSearch = new Button();

        splitMain = new SplitContainer();
        splitSong = new SplitContainer();
        splitRecOuter = new SplitContainer();
        splitRec = new SplitContainer();

        gridSongs = new DataGridView();
        pnlSongDetail = new Panel();
        numSongId = new NumericUpDown();
        txtTitle = new TextBox();
        txtTitleKana = new TextBox();
        cboMusicClass = new ComboBox();
        cboSeries = new ComboBox();
        txtLyricist = new TextBox(); txtLyricistKana = new TextBox();
        txtComposer = new TextBox(); txtComposerKana = new TextBox();
        txtArranger = new TextBox(); txtArrangerKana = new TextBox();
        txtSongNotes = new TextBox();
        btnSongNew = new Button(); btnSongSave = new Button(); btnSongDelete = new Button();

        gridRecordings = new DataGridView();
        pnlRecDetail = new Panel();
        numRecId = new NumericUpDown();
        txtSinger = new TextBox(); txtSingerKana = new TextBox();
        txtVariantLabel = new TextBox();
        txtRecNotes = new TextBox();
        btnRecNew = new Button(); btnRecSave = new Button(); btnRecDelete = new Button();

        lblRecTracks = new Label();
        gridRecTracks = new DataGridView();

        // 検索バー
        pnlSearch.Dock = DockStyle.Top;
        pnlSearch.Height = 40;
        lblSearch.Text = "検索:";
        lblSearch.Location = new Point(8, 12);
        lblSearch.Size = new Size(40, 20);
        txtSearch.Location = new Point(50, 8);
        txtSearch.Size = new Size(220, 23);
        lblSeriesFilter.Text = "シリーズ:";
        lblSeriesFilter.Location = new Point(280, 12);
        lblSeriesFilter.Size = new Size(62, 20);
        cboSeriesFilter.Location = new Point(344, 8);
        cboSeriesFilter.Size = new Size(200, 23);
        cboSeriesFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        lblMusicClassFilter.Text = "音楽種別:";
        lblMusicClassFilter.Location = new Point(554, 12);
        lblMusicClassFilter.Size = new Size(66, 20);
        cboMusicClassFilter.Location = new Point(622, 8);
        cboMusicClassFilter.Size = new Size(140, 23);
        cboMusicClassFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        btnSearch.Text = "検索";
        btnSearch.Location = new Point(772, 8);
        btnSearch.Size = new Size(80, 25);
        pnlSearch.Controls.AddRange(new Control[] {
            lblSearch, txtSearch,
            lblSeriesFilter, cboSeriesFilter,
            lblMusicClassFilter, cboMusicClassFilter,
            btnSearch });

        // レイアウト: splitMain (上下) → 上段 splitSong (左右), 下段 splitRecOuter (左右)
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Horizontal;
        splitMain.SplitterDistance = 360;
        splitSong.Dock = DockStyle.Fill;
        splitSong.SplitterDistance = 680;
        splitRecOuter.Dock = DockStyle.Fill;
        splitRecOuter.SplitterDistance = 700;
        splitRec.Dock = DockStyle.Fill;
        splitRec.SplitterDistance = 380;
        splitMain.Panel1.Controls.Add(splitSong);
        splitMain.Panel2.Controls.Add(splitRecOuter);
        splitRecOuter.Panel1.Controls.Add(splitRec);

        // 曲一覧
        gridSongs.Dock = DockStyle.Fill;
        gridSongs.AllowUserToAddRows = false;
        gridSongs.AllowUserToDeleteRows = false;
        gridSongs.ReadOnly = true;
        gridSongs.MultiSelect = false;
        gridSongs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridSongs.RowHeadersVisible = false;
        splitSong.Panel1.Controls.Add(gridSongs);

        // 曲詳細
        pnlSongDetail.Dock = DockStyle.Fill;
        pnlSongDetail.AutoScroll = true;
        splitSong.Panel2.Controls.Add(pnlSongDetail);

        const int lw = 100, fw = 200, rh = 28;
        int sy = 8;
        AddSongRow(pnlSongDetail, "ID", numSongId, sy); numSongId.ReadOnly = true; numSongId.Enabled = false; numSongId.Maximum = int.MaxValue; sy += rh;
        AddSongRow(pnlSongDetail, "タイトル", txtTitle, sy); sy += rh;
        AddSongRow(pnlSongDetail, "タイトル(かな)", txtTitleKana, sy); sy += rh;
        AddSongRow(pnlSongDetail, "音楽種別", cboMusicClass, sy); cboMusicClass.DropDownStyle = ComboBoxStyle.DropDownList; sy += rh;
        AddSongRow(pnlSongDetail, "シリーズ", cboSeries, sy); cboSeries.DropDownStyle = ComboBoxStyle.DropDownList; sy += rh;
        AddSongRow(pnlSongDetail, "作詞者", txtLyricist, sy); sy += rh;
        AddSongRow(pnlSongDetail, "作詞者(かな)", txtLyricistKana, sy); sy += rh;
        AddSongRow(pnlSongDetail, "作曲者", txtComposer, sy); sy += rh;
        AddSongRow(pnlSongDetail, "作曲者(かな)", txtComposerKana, sy); sy += rh;
        AddSongRow(pnlSongDetail, "編曲者", txtArranger, sy); sy += rh;
        AddSongRow(pnlSongDetail, "編曲者(かな)", txtArrangerKana, sy); sy += rh;
        var lblNote = new Label { Text = "備考", Location = new Point(8, sy + 4), Size = new Size(lw, 20) };
        txtSongNotes.Location = new Point(12 + lw, sy); txtSongNotes.Size = new Size(fw + 80, 60); txtSongNotes.Multiline = true;
        pnlSongDetail.Controls.Add(lblNote); pnlSongDetail.Controls.Add(txtSongNotes);

        btnSongNew.Text = "新規"; btnSongNew.Location = new Point(12 + lw + fw + 100, 8); btnSongNew.Size = new Size(80, 28);
        btnSongSave.Text = "保存"; btnSongSave.Location = new Point(12 + lw + fw + 100, 40); btnSongSave.Size = new Size(80, 28);
        btnSongDelete.Text = "削除"; btnSongDelete.Location = new Point(12 + lw + fw + 100, 72); btnSongDelete.Size = new Size(80, 28);
        pnlSongDetail.Controls.AddRange(new Control[] { btnSongNew, btnSongSave, btnSongDelete });

        // 録音一覧
        gridRecordings.Dock = DockStyle.Fill;
        gridRecordings.AllowUserToAddRows = false;
        gridRecordings.AllowUserToDeleteRows = false;
        gridRecordings.ReadOnly = true;
        gridRecordings.MultiSelect = false;
        gridRecordings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridRecordings.RowHeadersVisible = false;
        splitRec.Panel1.Controls.Add(gridRecordings);

        // 録音詳細
        pnlRecDetail.Dock = DockStyle.Fill;
        pnlRecDetail.AutoScroll = true;
        splitRec.Panel2.Controls.Add(pnlRecDetail);

        int ry = 8;
        AddSongRow(pnlRecDetail, "ID", numRecId, ry); numRecId.ReadOnly = true; numRecId.Enabled = false; numRecId.Maximum = int.MaxValue; ry += rh;
        AddSongRow(pnlRecDetail, "歌唱者", txtSinger, ry); ry += rh;
        AddSongRow(pnlRecDetail, "歌唱者(かな)", txtSingerKana, ry); ry += rh;
        AddSongRow(pnlRecDetail, "ラベル", txtVariantLabel, ry); ry += rh;
        var lblRecNote = new Label { Text = "備考", Location = new Point(8, ry + 4), Size = new Size(lw, 20) };
        txtRecNotes.Location = new Point(12 + lw, ry); txtRecNotes.Size = new Size(fw + 80, 60); txtRecNotes.Multiline = true;
        pnlRecDetail.Controls.Add(lblRecNote); pnlRecDetail.Controls.Add(txtRecNotes);

        btnRecNew.Text = "新規"; btnRecNew.Location = new Point(12 + lw + fw + 100, 8); btnRecNew.Size = new Size(80, 28);
        btnRecSave.Text = "保存"; btnRecSave.Location = new Point(12 + lw + fw + 100, 40); btnRecSave.Size = new Size(80, 28);
        btnRecDelete.Text = "削除"; btnRecDelete.Location = new Point(12 + lw + fw + 100, 72); btnRecDelete.Size = new Size(80, 28);
        pnlRecDetail.Controls.AddRange(new Control[] { btnRecNew, btnRecSave, btnRecDelete });

        // 右下：収録ディスク・トラック一覧
        lblRecTracks.Text = "このバージョンの収録ディスク・トラック";
        lblRecTracks.Dock = DockStyle.Top;
        lblRecTracks.Height = 22;
        lblRecTracks.Padding = new Padding(6, 4, 0, 0);
        gridRecTracks.Dock = DockStyle.Fill;
        gridRecTracks.AllowUserToAddRows = false;
        gridRecTracks.AllowUserToDeleteRows = false;
        gridRecTracks.ReadOnly = true;
        gridRecTracks.MultiSelect = false;
        gridRecTracks.RowHeadersVisible = false;
        gridRecTracks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        splitRecOuter.Panel2.Controls.Add(gridRecTracks);
        splitRecOuter.Panel2.Controls.Add(lblRecTracks);

        // Form（検索バー → splitMain の順）
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 820);
        Controls.Add(splitMain);
        Controls.Add(pnlSearch);
        Name = "SongsEditorForm";
        Text = "歌マスタ管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>ラベル + コントロールの 1 行を指定 y 座標に配置する。</summary>
    private static void AddSongRow(Panel panel, string label, Control control, int y)
    {
        const int lw = 100, fw = 200;
        var lbl = new Label { Text = label, Location = new Point(8, y + 4), Size = new Size(lw, 20) };
        control.Location = new Point(12 + lw, y);
        control.Size = new Size(fw, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}
