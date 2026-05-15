#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// トラック管理フォームのレイアウト定義。
/// <para>
/// 3 ペイン構成:
/// <list type="bullet">
///   <item>上: ディスク一覧（所属商品の発売日昇順。検索バー付き）</item>
///   <item>下左: 選択ディスクのトラック一覧（sub_order=0 親行のみ）</item>
///   <item>下右: トラック詳細編集パネル。種別に応じて SONG / BGM / OTHER のサブパネルを切り替える</item>
/// </list>
/// SONG / BGM ともに、検索テキストを打ちながら候補リストに一致行が並び、選択で確定という
/// オートコンプリート形式で曲・キューを指定する。
/// </para>
/// </summary>
partial class TracksEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // 検索バー（ディスク用）
    private Panel pnlSearch = null!;
    private Label lblSearch = null!;
    private TextBox txtSearchDiscs = null!;
    private Button btnSearch = null!;
    private Button btnReload = null!;

    // 分割
    private SplitContainer splitMain = null!;      // 上 ディスク / 下 トラック系
    private SplitContainer splitTrackArea = null!; // 下左 トラック一覧 / 下右 トラック詳細

    private DataGridView gridDiscs = null!;
    private Label lblTracks = null!;
    private DataGridView gridTracks = null!;

    // トラック詳細
    private Panel pnlTrackDetail = null!;
    private NumericUpDown numTrackNo = null!;
    private ComboBox cboContentKind = null!;
    private TextBox txtTrackTitleOverride = null!;
    private Label lblTitleOverride = null!;
    private NumericUpDown numStartLba = null!;
    private NumericUpDown numLengthFrames = null!;
    private TextBox txtIsrc = null!;
    private TextBox txtCdTextTitle = null!;
    private TextBox txtCdTextPerformer = null!;
    private CheckBox chkIsData = null!;
    private CheckBox chkPreEmphasis = null!;
    private CheckBox chkCopyOk = null!;
    private TextBox txtTrackNotes = null!;

    // SONG サブパネル
    private Panel pnlSongLink = null!;
    private Label lblSongSearch = null!;
    private TextBox txtSongSearch = null!;
    private ListBox lstSongCandidates = null!;
    private Label lblSongSelected = null!;
    private Label lblSongRecording = null!;
    private ComboBox cboSongRecording = null!;
    private Label lblSongSize = null!;
    private ComboBox cboSongSize = null!;
    private Label lblSongPart = null!;
    private ComboBox cboSongPart = null!;

    // BGM サブパネル
    private Panel pnlBgmLink = null!;
    private Label lblBgmSeries = null!;
    private ComboBox cboBgmSeries = null!;
    private Label lblBgmSearch = null!;
    private TextBox txtBgmSearch = null!;
    private CheckBox chkBgmIncludeTemp = null!;
    private ListBox lstBgmCandidates = null!;
    private Label lblBgmSelected = null!;

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
        lblSearch = new Label();
        txtSearchDiscs = new TextBox();
        btnSearch = new Button();
        btnReload = new Button();

        splitMain = new SplitContainer();
        splitTrackArea = new SplitContainer();

        gridDiscs = new DataGridView();
        lblTracks = new Label();
        gridTracks = new DataGridView();

        pnlTrackDetail = new Panel();
        numTrackNo = new NumericUpDown();
        cboContentKind = new ComboBox();
        txtTrackTitleOverride = new TextBox();
        lblTitleOverride = new Label();
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
        lblSongSearch = new Label();
        txtSongSearch = new TextBox();
        lstSongCandidates = new ListBox();
        lblSongSelected = new Label();
        lblSongRecording = new Label();
        cboSongRecording = new ComboBox();
        lblSongSize = new Label();
        cboSongSize = new ComboBox();
        lblSongPart = new Label();
        cboSongPart = new ComboBox();

        pnlBgmLink = new Panel();
        lblBgmSeries = new Label();
        cboBgmSeries = new ComboBox();
        lblBgmSearch = new Label();
        txtBgmSearch = new TextBox();
        chkBgmIncludeTemp = new CheckBox();
        lstBgmCandidates = new ListBox();
        lblBgmSelected = new Label();

        btnTrackNew = new Button();
        btnTrackSave = new Button();
        btnTrackDelete = new Button();

        // ── 検索バー（最上段） ──
        pnlSearch.Dock = DockStyle.Top;
        pnlSearch.Height = 40;
        lblSearch.Text = "ディスク検索:";
        lblSearch.Location = new Point(8, 12);
        lblSearch.Size = new Size(90, 20);
        txtSearchDiscs.Location = new Point(100, 8);
        txtSearchDiscs.Size = new Size(260, 23);
        btnSearch.Text = "検索"; btnSearch.Location = new Point(366, 8); btnSearch.Size = new Size(80, 25);
        btnReload.Text = "再読込"; btnReload.Location = new Point(452, 8); btnReload.Size = new Size(80, 25);
        pnlSearch.Controls.AddRange(new Control[] { lblSearch, txtSearchDiscs, btnSearch, btnReload });

        // ── splitMain: 上 ディスク / 下 トラック系 ──
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Horizontal;
        splitMain.SplitterDistance = 280;
        splitMain.SplitterWidth = 6;

        // 上: ディスク一覧
        gridDiscs.Dock = DockStyle.Fill;
        gridDiscs.AllowUserToAddRows = false;
        gridDiscs.AllowUserToDeleteRows = false;
        gridDiscs.ReadOnly = true;
        gridDiscs.RowHeadersVisible = false;
        gridDiscs.MultiSelect = false;
        gridDiscs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridDiscs.AutoGenerateColumns = false;
        splitMain.Panel1.Controls.Add(gridDiscs);

        // 下: splitTrackArea
        splitTrackArea.Dock = DockStyle.Fill;
        splitTrackArea.SplitterDistance = 560;
        splitTrackArea.SplitterWidth = 6;
        splitMain.Panel2.Controls.Add(splitTrackArea);

        // 下左: トラック一覧 + ヘッダラベル
        lblTracks.Text = "トラック一覧（ディスクを選択してください）";
        lblTracks.Dock = DockStyle.Top;
        lblTracks.Height = 24;
        lblTracks.Padding = new Padding(6, 4, 0, 0);
        lblTracks.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);

        gridTracks.Dock = DockStyle.Fill;
        gridTracks.AllowUserToAddRows = false;
        gridTracks.AllowUserToDeleteRows = false;
        gridTracks.ReadOnly = true;
        gridTracks.RowHeadersVisible = false;
        gridTracks.MultiSelect = false;
        gridTracks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridTracks.AutoGenerateColumns = false;
        splitTrackArea.Panel1.Controls.Add(gridTracks);
        splitTrackArea.Panel1.Controls.Add(lblTracks);

        // 下右: トラック詳細
        pnlTrackDetail.Dock = DockStyle.Fill;
        pnlTrackDetail.AutoScroll = true;
        splitTrackArea.Panel2.Controls.Add(pnlTrackDetail);

        // 共通: 行高
        const int lw = 110, fw = 260, rh = 28;
        int ty = 8;
        AddRow(pnlTrackDetail, "トラック番号", numTrackNo, ty, lw, fw); numTrackNo.Minimum = 0; numTrackNo.Maximum = 99; ty += rh;
        AddRow(pnlTrackDetail, "種別", cboContentKind, ty, lw, fw); cboContentKind.DropDownStyle = ComboBoxStyle.DropDownList; ty += rh;

        lblTitleOverride.Text = "タイトル上書き";
        lblTitleOverride.Location = new Point(8, ty + 4);
        lblTitleOverride.Size = new Size(lw, 20);
        txtTrackTitleOverride.Location = new Point(12 + lw, ty);
        txtTrackTitleOverride.Size = new Size(fw, 23);
        pnlTrackDetail.Controls.Add(lblTitleOverride);
        pnlTrackDetail.Controls.Add(txtTrackTitleOverride);
        ty += rh;

        // SONG サブパネル
        pnlSongLink.Location = new Point(8, ty);
        pnlSongLink.Size = new Size(lw + fw + 4, 360);
        pnlSongLink.BorderStyle = BorderStyle.FixedSingle;

        int sy = 6;
        lblSongSearch.Text = "曲名・作詞作曲で検索:";
        lblSongSearch.Location = new Point(8, sy + 4); lblSongSearch.Size = new Size(lw + 40, 20);
        txtSongSearch.Location = new Point(8, sy + 24); txtSongSearch.Size = new Size(lw + fw - 20, 23);
        sy += 52;

        lstSongCandidates.Location = new Point(8, sy);
        lstSongCandidates.Size = new Size(lw + fw - 20, 120);
        lstSongCandidates.IntegralHeight = false;
        sy += 130;

        lblSongSelected.Text = "(曲未選択)";
        lblSongSelected.Location = new Point(8, sy); lblSongSelected.Size = new Size(lw + fw - 20, 40);
        lblSongSelected.AutoSize = false;
        lblSongSelected.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
        sy += 48;

        lblSongRecording.Text = "歌唱者バージョン:";
        lblSongRecording.Location = new Point(8, sy + 4); lblSongRecording.Size = new Size(lw, 20);
        cboSongRecording.Location = new Point(8 + lw, sy);
        cboSongRecording.Size = new Size(fw - 20, 23);
        cboSongRecording.DropDownStyle = ComboBoxStyle.DropDownList;
        sy += rh;

        lblSongSize.Text = "サイズ種別:";
        lblSongSize.Location = new Point(8, sy + 4); lblSongSize.Size = new Size(lw, 20);
        cboSongSize.Location = new Point(8 + lw, sy); cboSongSize.Size = new Size(fw - 20, 23);
        cboSongSize.DropDownStyle = ComboBoxStyle.DropDownList;
        sy += rh;

        lblSongPart.Text = "パート種別:";
        lblSongPart.Location = new Point(8, sy + 4); lblSongPart.Size = new Size(lw, 20);
        cboSongPart.Location = new Point(8 + lw, sy); cboSongPart.Size = new Size(fw - 20, 23);
        cboSongPart.DropDownStyle = ComboBoxStyle.DropDownList;

        pnlSongLink.Controls.AddRange(new Control[] {
            lblSongSearch, txtSongSearch, lstSongCandidates,
            lblSongSelected, lblSongRecording, cboSongRecording,
            lblSongSize, cboSongSize, lblSongPart, cboSongPart });
        pnlTrackDetail.Controls.Add(pnlSongLink);

        // BGM サブパネル（SONG と同じ起点に重ねる）
        pnlBgmLink.Location = pnlSongLink.Location;
        pnlBgmLink.Size = pnlSongLink.Size;
        pnlBgmLink.BorderStyle = BorderStyle.FixedSingle;

        int by = 6;
        lblBgmSeries.Text = "シリーズ:";
        lblBgmSeries.Location = new Point(8, by + 4); lblBgmSeries.Size = new Size(lw, 20);
        cboBgmSeries.Location = new Point(8 + lw, by); cboBgmSeries.Size = new Size(fw - 20, 23);
        cboBgmSeries.DropDownStyle = ComboBoxStyle.DropDownList;
        by += rh;

        lblBgmSearch.Text = "M番号・メニュー名で検索:";
        lblBgmSearch.Location = new Point(8, by + 4); lblBgmSearch.Size = new Size(lw + 40, 20);
        txtBgmSearch.Location = new Point(8, by + 24); txtBgmSearch.Size = new Size(lw + fw - 20, 23);
        by += 52;

        chkBgmIncludeTemp.Text = "仮番号を候補に含める";
        chkBgmIncludeTemp.Location = new Point(8, by);
        chkBgmIncludeTemp.Size = new Size(lw + fw - 20, 22);
        by += 26;

        lstBgmCandidates.Location = new Point(8, by);
        lstBgmCandidates.Size = new Size(lw + fw - 20, 140);
        lstBgmCandidates.IntegralHeight = false;
        by += 150;

        lblBgmSelected.Text = "(劇伴未選択)";
        lblBgmSelected.Location = new Point(8, by); lblBgmSelected.Size = new Size(lw + fw - 20, 40);
        lblBgmSelected.AutoSize = false;
        lblBgmSelected.ForeColor = System.Drawing.SystemColors.ControlDarkDark;

        pnlBgmLink.Controls.AddRange(new Control[] {
            lblBgmSeries, cboBgmSeries,
            lblBgmSearch, txtBgmSearch, chkBgmIncludeTemp,
            lstBgmCandidates, lblBgmSelected });
        pnlTrackDetail.Controls.Add(pnlBgmLink);

        ty += pnlSongLink.Height + 8;

        // その他の物理情報
        AddRow(pnlTrackDetail, "StartLBA", numStartLba, ty, lw, fw); numStartLba.Maximum = 999999999; ty += rh;
        AddRow(pnlTrackDetail, "尺(フレーム)", numLengthFrames, ty, lw, fw); numLengthFrames.Maximum = 999999999; ty += rh;
        AddRow(pnlTrackDetail, "ISRC", txtIsrc, ty, lw, fw); ty += rh;
        AddRow(pnlTrackDetail, "CD-Text Title", txtCdTextTitle, ty, lw, fw); ty += rh;
        AddRow(pnlTrackDetail, "CD-Text Performer", txtCdTextPerformer, ty, lw, fw); ty += rh;

        chkIsData.Text = "データトラック";
        chkIsData.Location = new Point(12 + lw, ty); chkIsData.Size = new Size(120, 22);
        chkPreEmphasis.Text = "プリエンファシス";
        chkPreEmphasis.Location = new Point(12 + lw + 130, ty); chkPreEmphasis.Size = new Size(130, 22);
        chkCopyOk.Text = "コピー可";
        chkCopyOk.Location = new Point(12 + lw + 270, ty); chkCopyOk.Size = new Size(80, 22);
        pnlTrackDetail.Controls.AddRange(new Control[] { chkIsData, chkPreEmphasis, chkCopyOk });
        ty += rh;

        var lblNotes = new Label { Text = "備考", Location = new Point(8, ty + 4), Size = new Size(lw, 20) };
        txtTrackNotes.Location = new Point(12 + lw, ty); txtTrackNotes.Size = new Size(fw, 50); txtTrackNotes.Multiline = true;
        pnlTrackDetail.Controls.Add(lblNotes);
        pnlTrackDetail.Controls.Add(txtTrackNotes);

        // ボタン行
        int btnX = 12 + lw + fw + 16;
        btnTrackNew.Text = "新規"; btnTrackNew.Location = new Point(btnX, 8); btnTrackNew.Size = new Size(80, 28);
        btnTrackSave.Text = "保存"; btnTrackSave.Location = new Point(btnX, 40); btnTrackSave.Size = new Size(80, 28);
        btnTrackDelete.Text = "削除"; btnTrackDelete.Location = new Point(btnX, 72); btnTrackDelete.Size = new Size(80, 28);
        pnlTrackDetail.Controls.AddRange(new Control[] { btnTrackNew, btnTrackSave, btnTrackDelete });

        // Form
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1400, 880);
        Controls.Add(splitMain);
        Controls.Add(pnlSearch);
        Name = "TracksEditorForm";
        Text = "トラック管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>ラベル + コントロールを行として配置する。</summary>
    private static void AddRow(Panel panel, string label, Control control, int y, int labelW, int fieldW)
    {
        var lbl = new Label { Text = label, Location = new Point(8, y + 4), Size = new Size(labelW, 20) };
        control.Location = new Point(12 + labelW, y);
        control.Size = new Size(fieldW, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}