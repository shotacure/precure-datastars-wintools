#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class DiscsEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // 検索バー
    private Panel pnlSearch = null!;
    private TextBox txtSearch = null!;
    private Button btnSearch = null!;
    private Label lblSearch = null!;

    // 左右スプリット
    private SplitContainer splitMain = null!;
    private DataGridView gridDiscs = null!;
    private SplitContainer splitRight = null!;

    // ディスク詳細
    private Panel pnlDiscDetail = null!;
    private TextBox txtCatalogNo = null!;
    private TextBox txtProductCatalogNo = null!;
    private TextBox txtDiscTitle = null!;
    private TextBox txtDiscTitleShort = null!;
    private TextBox txtDiscTitleEn = null!;
    private NumericUpDown numDiscNoInSet = null!;
    private ComboBox cboDiscKind = null!;
    private TextBox txtMediaFormat = null!;
    private TextBox txtMcn = null!;
    private NumericUpDown numTotalTracks = null!;
    private TextBox txtVolumeLabel = null!;
    private TextBox txtDiscNotes = null!;
    private Button btnDiscSave = null!;
    private Button btnDiscDelete = null!;
    private Button btnDiscReload = null!;

    // トラック一覧 + 詳細
    private SplitContainer splitTracks = null!;
    private DataGridView gridTracks = null!;
    private Panel pnlTrackDetail = null!;
    private NumericUpDown numTrackNo = null!;
    private ComboBox cboContentKind = null!;
    private Label lblTitleOverride = null!;
    private TextBox txtTrackTitleOverride = null!;
    private NumericUpDown numStartLba = null!;
    private NumericUpDown numLengthFrames = null!;
    private TextBox txtIsrc = null!;
    private TextBox txtCdTextTitle = null!;
    private TextBox txtCdTextPerformer = null!;
    private CheckBox chkIsData = null!;
    private CheckBox chkPreEmphasis = null!;
    private CheckBox chkCopyOk = null!;
    private TextBox txtTrackNotes = null!;
    // SONG リンクパネル
    private Panel pnlSongLink = null!;
    private ComboBox cboSongParent = null!;
    private ComboBox cboSongRecording = null!;
    // BGM リンクパネル
    private Panel pnlBgmLink = null!;
    private ComboBox cboBgmCue = null!;
    // 歌トラックのサイズ／パート種別
    private Label lblSongSize = null!;
    private ComboBox cboSongSize = null!;
    private Label lblSongPart = null!;
    private ComboBox cboSongPart = null!;
    private Button btnTrackNew = null!;
    private Button btnTrackSave = null!;
    private Button btnTrackDelete = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        pnlSearch = new Panel();
        txtSearch = new TextBox();
        btnSearch = new Button();
        lblSearch = new Label();

        splitMain = new SplitContainer();
        gridDiscs = new DataGridView();
        splitRight = new SplitContainer();

        pnlDiscDetail = new Panel();
        txtCatalogNo = new TextBox();
        txtProductCatalogNo = new TextBox();
        txtDiscTitle = new TextBox(); txtDiscTitleShort = new TextBox(); txtDiscTitleEn = new TextBox();
        numDiscNoInSet = new NumericUpDown();
        cboDiscKind = new ComboBox();
        txtMediaFormat = new TextBox();
        txtMcn = new TextBox();
        numTotalTracks = new NumericUpDown();
        txtVolumeLabel = new TextBox();
        txtDiscNotes = new TextBox();
        btnDiscSave = new Button(); btnDiscDelete = new Button(); btnDiscReload = new Button();

        splitTracks = new SplitContainer();
        gridTracks = new DataGridView();
        pnlTrackDetail = new Panel();
        numTrackNo = new NumericUpDown();
        cboContentKind = new ComboBox();
        lblTitleOverride = new Label();
        txtTrackTitleOverride = new TextBox();
        numStartLba = new NumericUpDown();
        numLengthFrames = new NumericUpDown();
        txtIsrc = new TextBox();
        txtCdTextTitle = new TextBox();
        txtCdTextPerformer = new TextBox();
        chkIsData = new CheckBox();
        chkPreEmphasis = new CheckBox();
        chkCopyOk = new CheckBox();
        txtTrackNotes = new TextBox();
        pnlSongLink = new Panel();
        cboSongParent = new ComboBox();
        cboSongRecording = new ComboBox();
        pnlBgmLink = new Panel();
        cboBgmCue = new ComboBox();
        lblSongSize = new Label();
        cboSongSize = new ComboBox();
        lblSongPart = new Label();
        cboSongPart = new ComboBox();
        btnTrackNew = new Button(); btnTrackSave = new Button(); btnTrackDelete = new Button();

        // 検索バー
        pnlSearch.Dock = DockStyle.Top;
        pnlSearch.Height = 40;
        lblSearch.Text = "検索：";
        lblSearch.Location = new Point(8, 12);
        lblSearch.Size = new Size(50, 20);
        txtSearch.Location = new Point(60, 8);
        txtSearch.Size = new Size(250, 23);
        btnSearch.Text = "検索 / 再読込";
        btnSearch.Location = new Point(320, 8);
        btnSearch.Size = new Size(120, 25);
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearch, btnSearch });

        // splitMain: 左=ディスク一覧 / 右=詳細+トラック
        splitMain.Dock = DockStyle.Fill;
        splitMain.SplitterDistance = 420;

        gridDiscs.Dock = DockStyle.Fill;
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.MultiSelect = false;
        gridDiscs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridDiscs.RowHeadersVisible = false;
        splitMain.Panel1.Controls.Add(gridDiscs);

        // splitRight: 上=ディスク詳細 / 下=トラック
        splitRight.Dock = DockStyle.Fill;
        splitRight.Orientation = Orientation.Horizontal;
        splitRight.SplitterDistance = 340;
        splitMain.Panel2.Controls.Add(splitRight);

        // ディスク詳細
        pnlDiscDetail.Dock = DockStyle.Fill;
        pnlDiscDetail.AutoScroll = true;
        splitRight.Panel1.Controls.Add(pnlDiscDetail);

        int y = 8;
        const int rh = 28;
        AddRow(pnlDiscDetail, "品番", txtCatalogNo, y); y += rh;
        AddRow(pnlDiscDetail, "代表品番", txtProductCatalogNo, y); txtProductCatalogNo.MaxLength = 32; y += rh;
        AddRow(pnlDiscDetail, "タイトル", txtDiscTitle, y); y += rh;
        AddRow(pnlDiscDetail, "略称", txtDiscTitleShort, y); y += rh;
        AddRow(pnlDiscDetail, "英語タイトル", txtDiscTitleEn, y); y += rh;
        AddRow(pnlDiscDetail, "組中位置", numDiscNoInSet, y); numDiscNoInSet.Maximum = 20; y += rh;
        AddRow(pnlDiscDetail, "ディスク種別", cboDiscKind, y); y += rh;
        AddRow(pnlDiscDetail, "メディア種別", txtMediaFormat, y); y += rh;
        AddRow(pnlDiscDetail, "MCN", txtMcn, y); y += rh;
        AddRow(pnlDiscDetail, "総トラック数", numTotalTracks, y); numTotalTracks.Maximum = 99; y += rh;
        AddRow(pnlDiscDetail, "ボリュームラベル", txtVolumeLabel, y); y += rh;
        var lblDN = new Label { Text = "備考", Location = new Point(8, y + 4), Size = new Size(110, 20) };
        txtDiscNotes.Location = new Point(120, y); txtDiscNotes.Size = new Size(260, 50); txtDiscNotes.Multiline = true;
        pnlDiscDetail.Controls.Add(lblDN); pnlDiscDetail.Controls.Add(txtDiscNotes);

        btnDiscSave.Text = "保存"; btnDiscSave.Location = new Point(400, 8); btnDiscSave.Size = new Size(80, 28);
        btnDiscDelete.Text = "削除"; btnDiscDelete.Location = new Point(400, 40); btnDiscDelete.Size = new Size(80, 28);
        btnDiscReload.Text = "再読込"; btnDiscReload.Location = new Point(400, 72); btnDiscReload.Size = new Size(80, 28);
        pnlDiscDetail.Controls.AddRange(new Control[] { btnDiscSave, btnDiscDelete, btnDiscReload });

        // トラックセクション (splitTracks)
        splitTracks.Dock = DockStyle.Fill;
        splitTracks.SplitterDistance = 540;
        splitRight.Panel2.Controls.Add(splitTracks);

        gridTracks.Dock = DockStyle.Fill;
        gridTracks.AllowUserToAddRows = false;
        gridTracks.AllowUserToDeleteRows = false;
        gridTracks.ReadOnly = true;
        gridTracks.MultiSelect = false;
        gridTracks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridTracks.RowHeadersVisible = false;
        splitTracks.Panel1.Controls.Add(gridTracks);

        pnlTrackDetail.Dock = DockStyle.Fill;
        pnlTrackDetail.AutoScroll = true;
        splitTracks.Panel2.Controls.Add(pnlTrackDetail);

        int ty = 8;
        AddRow(pnlTrackDetail, "トラック番号", numTrackNo, ty); numTrackNo.Maximum = 99; ty += rh;
        AddRow(pnlTrackDetail, "種別", cboContentKind, ty); ty += rh;

        // TitleOverride (DRAMA/RADIO/OTHER 用)
        lblTitleOverride.Text = "タイトル上書き";
        lblTitleOverride.Location = new Point(8, ty + 4);
        lblTitleOverride.Size = new Size(110, 20);
        txtTrackTitleOverride.Location = new Point(120, ty);
        txtTrackTitleOverride.Size = new Size(260, 23);
        pnlTrackDetail.Controls.Add(lblTitleOverride);
        pnlTrackDetail.Controls.Add(txtTrackTitleOverride);
        ty += rh;

        // SONG リンク
        pnlSongLink.Location = new Point(120, ty);
        pnlSongLink.Size = new Size(260, 60);
        pnlSongLink.BorderStyle = BorderStyle.FixedSingle;
        var lblSp = new Label { Text = "親曲", Location = new Point(4, 6), Size = new Size(50, 20) };
        cboSongParent.Location = new Point(58, 4);
        cboSongParent.Size = new Size(198, 23);
        var lblSr = new Label { Text = "録音", Location = new Point(4, 32), Size = new Size(50, 20) };
        cboSongRecording.Location = new Point(58, 30);
        cboSongRecording.Size = new Size(198, 23);
        pnlSongLink.Controls.AddRange(new Control[] { lblSp, cboSongParent, lblSr, cboSongRecording });
        var lblSong = new Label { Text = "SONG 紐付け", Location = new Point(8, ty + 4), Size = new Size(110, 20) };
        pnlTrackDetail.Controls.Add(lblSong);
        pnlTrackDetail.Controls.Add(pnlSongLink);

        // BGM リンク（SONG と同位置：排他で表示）
        // ターン C の 1 テーブル統合により、cue 選択コンボ 1 つで紐付け完結（録音コンボは廃止）。
        pnlBgmLink.Location = new Point(120, ty);
        pnlBgmLink.Size = new Size(260, 34);
        pnlBgmLink.BorderStyle = BorderStyle.FixedSingle;
        var lblBc = new Label { Text = "キュー", Location = new Point(4, 6), Size = new Size(50, 20) };
        cboBgmCue.Location = new Point(58, 4);
        cboBgmCue.Size = new Size(198, 23);
        pnlBgmLink.Controls.AddRange(new Control[] { lblBc, cboBgmCue });
        var lblBgm = new Label { Text = "BGM 紐付け", Location = new Point(8, ty + 4), Size = new Size(110, 20) };
        pnlTrackDetail.Controls.Add(lblBgm);
        pnlTrackDetail.Controls.Add(pnlBgmLink);
        ty += 44;

        // 歌トラックのサイズ種別 / パート種別（SONG のときだけ UpdateTrackLinkPanelVisibility で表示）
        lblSongSize.Text = "サイズ種別";
        lblSongSize.Location = new Point(8, ty + 4);
        lblSongSize.Size = new Size(110, 20);
        cboSongSize.Location = new Point(120, ty);
        cboSongSize.Size = new Size(260, 23);
        cboSongSize.DropDownStyle = ComboBoxStyle.DropDownList;
        pnlTrackDetail.Controls.Add(lblSongSize);
        pnlTrackDetail.Controls.Add(cboSongSize);
        ty += rh;

        lblSongPart.Text = "パート種別";
        lblSongPart.Location = new Point(8, ty + 4);
        lblSongPart.Size = new Size(110, 20);
        cboSongPart.Location = new Point(120, ty);
        cboSongPart.Size = new Size(260, 23);
        cboSongPart.DropDownStyle = ComboBoxStyle.DropDownList;
        pnlTrackDetail.Controls.Add(lblSongPart);
        pnlTrackDetail.Controls.Add(cboSongPart);
        ty += rh;

        AddRow(pnlTrackDetail, "開始 LBA", numStartLba, ty); numStartLba.Maximum = int.MaxValue; ty += rh;
        AddRow(pnlTrackDetail, "尺 (frames)", numLengthFrames, ty); numLengthFrames.Maximum = int.MaxValue; ty += rh;
        AddRow(pnlTrackDetail, "ISRC", txtIsrc, ty); ty += rh;
        AddRow(pnlTrackDetail, "CD-Text Title", txtCdTextTitle, ty); ty += rh;
        AddRow(pnlTrackDetail, "CD-Text Performer", txtCdTextPerformer, ty); ty += rh;

        chkIsData.Text = "データトラック";
        chkIsData.Location = new Point(120, ty);
        chkIsData.Size = new Size(120, 23);
        chkPreEmphasis.Text = "Pre-emphasis";
        chkPreEmphasis.Location = new Point(240, ty);
        chkPreEmphasis.Size = new Size(120, 23);
        pnlTrackDetail.Controls.Add(chkIsData);
        pnlTrackDetail.Controls.Add(chkPreEmphasis);
        ty += rh;
        chkCopyOk.Text = "コピー可";
        chkCopyOk.Location = new Point(120, ty);
        chkCopyOk.Size = new Size(120, 23);
        pnlTrackDetail.Controls.Add(chkCopyOk);
        ty += rh;

        var lblTN = new Label { Text = "備考", Location = new Point(8, ty + 4), Size = new Size(110, 20) };
        txtTrackNotes.Location = new Point(120, ty); txtTrackNotes.Size = new Size(260, 50); txtTrackNotes.Multiline = true;
        pnlTrackDetail.Controls.Add(lblTN); pnlTrackDetail.Controls.Add(txtTrackNotes);

        btnTrackNew.Text = "新規"; btnTrackNew.Location = new Point(400, 8); btnTrackNew.Size = new Size(80, 28);
        btnTrackSave.Text = "保存"; btnTrackSave.Location = new Point(400, 40); btnTrackSave.Size = new Size(80, 28);
        btnTrackDelete.Text = "削除"; btnTrackDelete.Location = new Point(400, 72); btnTrackDelete.Size = new Size(80, 28);
        pnlTrackDetail.Controls.AddRange(new Control[] { btnTrackNew, btnTrackSave, btnTrackDelete });

        // Form
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1400, 900);
        Controls.Add(splitMain);
        Controls.Add(pnlSearch);
        Name = "DiscsEditorForm";
        Text = "ディスク／トラック管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>ラベル + コントロールの 1 行を指定 y 座標に配置する。</summary>
    private static void AddRow(Panel panel, string label, Control control, int y)
    {
        var lbl = new Label { Text = label, Location = new Point(8, y + 4), Size = new Size(110, 20) };
        control.Location = new Point(120, y);
        control.Size = new Size(260, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}
