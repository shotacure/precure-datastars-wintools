#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class BgmCuesEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // 検索・フィルタバー（最上段）
    private Panel pnlSearch = null!;
    private Label lblSeriesFilter = null!;
    private ComboBox cboSeriesFilter = null!;
    private Label lblSessionFilter = null!;
    private ComboBox cboSessionFilter = null!;
    private Label lblSearch = null!;
    private TextBox txtSearch = null!;
    private Button btnSearch = null!;

    // 上下 2 段 (上: 一覧＋詳細, 下: 収録トラック)
    private SplitContainer splitOuter = null!;
    private SplitContainer splitCue = null!;

    private DataGridView gridCues = null!;
    private Panel pnlCueDetail = null!;

    // 編集フィールド
    private ComboBox cboSeries = null!;
    private ComboBox cboSession = null!;
    private TextBox txtMNoDetail = null!;
    private TextBox txtMNoClass = null!;
    private TextBox txtMenuTitle = null!;
    private TextBox txtComposer = null!;
    private TextBox txtComposerKana = null!;
    private TextBox txtArranger = null!;
    private TextBox txtArrangerKana = null!;
    private NumericUpDown numLength = null!;
    private TextBox txtNotes = null!;
    // v1.1.3: 仮 M 番号フラグのチェックボックスと採番補助ボタン
    private CheckBox chkIsTempMNo = null!;
    private Button btnAssignTempNo = null!;
    private Button btnCueNew = null!;
    private Button btnCueSave = null!;
    private Button btnCueDelete = null!;
    // v1.1.3: CSV 取り込みボタン（最上段の検索パネルに追加）
    private Button btnImportCsv = null!;

    // 下段：収録トラック一覧
    private Label lblCueTracks = null!;
    private DataGridView gridCueTracks = null!;

    // v1.2.3: 構造化クレジット（bgm_cue_credits）の概要表示と編集起動
    // 既存フリーテキスト欄（txtComposer / txtArranger）はそのまま残し、本グループの値が
    // 非空ならテンプレ展開で優先表示される旨をラベルで案内する。
    private GroupBox grpStructCredits = null!;
    private Label lblStructHint = null!;
    private Label lblStructComposer = null!;
    private Label lblStructComposerValue = null!;
    private Button btnEditStructComposer = null!;
    private Label lblStructArranger = null!;
    private Label lblStructArrangerValue = null!;
    private Button btnEditStructArranger = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        pnlSearch = new Panel();
        lblSeriesFilter = new Label();
        cboSeriesFilter = new ComboBox();
        lblSessionFilter = new Label();
        cboSessionFilter = new ComboBox();
        lblSearch = new Label();
        txtSearch = new TextBox();
        btnSearch = new Button();

        splitOuter = new SplitContainer();
        splitCue = new SplitContainer();

        gridCues = new DataGridView();
        pnlCueDetail = new Panel();

        cboSeries = new ComboBox();
        cboSession = new ComboBox();
        txtMNoDetail = new TextBox();
        txtMNoClass = new TextBox();
        txtMenuTitle = new TextBox();
        txtComposer = new TextBox(); txtComposerKana = new TextBox();
        txtArranger = new TextBox(); txtArrangerKana = new TextBox();
        numLength = new NumericUpDown();
        txtNotes = new TextBox();
        // v1.1.3: 仮 M 番号フラグ＋採番ボタン＋CSV 取り込みボタン
        chkIsTempMNo = new CheckBox();
        btnAssignTempNo = new Button();
        btnImportCsv = new Button();
        btnCueNew = new Button(); btnCueSave = new Button(); btnCueDelete = new Button();

        lblCueTracks = new Label();
        gridCueTracks = new DataGridView();

        // 検索バー
        pnlSearch.Dock = DockStyle.Top;
        pnlSearch.Height = 40;
        lblSeriesFilter.Text = "シリーズ:";
        lblSeriesFilter.Location = new Point(8, 12);
        lblSeriesFilter.Size = new Size(62, 20);
        cboSeriesFilter.Location = new Point(72, 8);
        cboSeriesFilter.Size = new Size(220, 23);
        cboSeriesFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        lblSessionFilter.Text = "セッション:";
        lblSessionFilter.Location = new Point(302, 12);
        lblSessionFilter.Size = new Size(70, 20);
        cboSessionFilter.Location = new Point(374, 8);
        cboSessionFilter.Size = new Size(180, 23);
        cboSessionFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        lblSearch.Text = "検索:";
        lblSearch.Location = new Point(564, 12);
        lblSearch.Size = new Size(40, 20);
        txtSearch.Location = new Point(606, 8);
        txtSearch.Size = new Size(240, 23);
        btnSearch.Text = "検索";
        btnSearch.Location = new Point(856, 8);
        btnSearch.Size = new Size(80, 25);
        // v1.1.3: CSV 取り込みボタン（検索ボタンの右）
        btnImportCsv.Text = "CSV取り込み...";
        btnImportCsv.Location = new Point(946, 8);
        btnImportCsv.Size = new Size(110, 25);
        pnlSearch.Controls.AddRange(new Control[] {
            lblSeriesFilter, cboSeriesFilter,
            lblSessionFilter, cboSessionFilter,
            lblSearch, txtSearch, btnSearch, btnImportCsv });

        // splitOuter: 上 cue（一覧+詳細）, 下 収録トラック
        splitOuter.Dock = DockStyle.Fill;
        splitOuter.Orientation = Orientation.Horizontal;
        splitOuter.SplitterDistance = 440;

        // splitCue: 左 cue 一覧, 右 cue 詳細
        splitCue.Dock = DockStyle.Fill;
        splitCue.SplitterDistance = 680;
        splitOuter.Panel1.Controls.Add(splitCue);

        // cue 一覧
        gridCues.Dock = DockStyle.Fill;
        gridCues.AllowUserToAddRows = false;
        gridCues.AllowUserToDeleteRows = false;
        gridCues.ReadOnly = true;
        gridCues.MultiSelect = false;
        gridCues.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridCues.RowHeadersVisible = false;
        splitCue.Panel1.Controls.Add(gridCues);

        // cue 詳細
        pnlCueDetail.Dock = DockStyle.Fill;
        pnlCueDetail.AutoScroll = true;
        splitCue.Panel2.Controls.Add(pnlCueDetail);

        const int lw = 110, fw = 260, rh = 28;
        int y = 8;
        AddRow(pnlCueDetail, "シリーズ", cboSeries, y); cboSeries.DropDownStyle = ComboBoxStyle.DropDownList; y += rh;
        AddRow(pnlCueDetail, "セッション", cboSession, y); cboSession.DropDownStyle = ComboBoxStyle.DropDownList; y += rh;
        AddRow(pnlCueDetail, "M番号詳細", txtMNoDetail, y); y += rh;
        AddRow(pnlCueDetail, "M番号分類", txtMNoClass, y); y += rh;
        AddRow(pnlCueDetail, "メニュー名", txtMenuTitle, y); y += rh;
        AddRow(pnlCueDetail, "作曲者", txtComposer, y); y += rh;
        AddRow(pnlCueDetail, "作曲者(かな)", txtComposerKana, y); y += rh;
        AddRow(pnlCueDetail, "編曲者", txtArranger, y); y += rh;
        AddRow(pnlCueDetail, "編曲者(かな)", txtArrangerKana, y); y += rh;
        AddRow(pnlCueDetail, "尺(秒)", numLength, y); numLength.Maximum = 9999; y += rh;

        // v1.1.3: 仮 M 番号フラグの表示（左側にチェックボックス、右側に「仮番号を採番」ボタン）
        chkIsTempMNo.Text = "仮 M 番号（内部管理用。閲覧側では番号不明として扱う）";
        chkIsTempMNo.Location = new Point(12 + lw, y);
        chkIsTempMNo.Size = new Size(fw + 40, 22);
        pnlCueDetail.Controls.Add(chkIsTempMNo);
        y += 26;

        btnAssignTempNo.Text = "仮番号を採番";
        btnAssignTempNo.Location = new Point(12 + lw, y);
        btnAssignTempNo.Size = new Size(140, 26);
        pnlCueDetail.Controls.Add(btnAssignTempNo);
        y += 32;

        var lblNote = new Label { Text = "備考", Location = new Point(8, y + 4), Size = new Size(lw, 20) };
        txtNotes.Location = new Point(12 + lw, y); txtNotes.Size = new Size(fw, 60); txtNotes.Multiline = true;
        pnlCueDetail.Controls.Add(lblNote); pnlCueDetail.Controls.Add(txtNotes);

        btnCueNew.Text = "新規"; btnCueNew.Location = new Point(12 + lw + fw + 16, 8); btnCueNew.Size = new Size(80, 28);
        btnCueSave.Text = "保存"; btnCueSave.Location = new Point(12 + lw + fw + 16, 40); btnCueSave.Size = new Size(80, 28);
        btnCueDelete.Text = "削除"; btnCueDelete.Location = new Point(12 + lw + fw + 16, 72); btnCueDelete.Size = new Size(80, 28);
        pnlCueDetail.Controls.AddRange(new Control[] { btnCueNew, btnCueSave, btnCueDelete });

        // v1.2.3: 構造化クレジット GroupBox を cue 詳細パネル下部に配置
        grpStructCredits = new GroupBox();
        lblStructHint = new Label();
        lblStructComposer = new Label(); lblStructComposerValue = new Label(); btnEditStructComposer = new Button();
        lblStructArranger = new Label(); lblStructArrangerValue = new Label(); btnEditStructArranger = new Button();

        grpStructCredits.Text = "構造化クレジット（bgm_cue_credits、ある場合は表示優先）";
        grpStructCredits.Location = new Point(8, y + 70);
        grpStructCredits.Size = new Size(12 + lw + fw + 8, 116);
        lblStructHint.Text = "連名表記はこちらで構築します。空のままならフリーテキスト欄が表示に使われます。";
        lblStructHint.Location = new Point(12, 22); lblStructHint.Size = new Size(360, 16);
        lblStructHint.ForeColor = Color.DimGray;

        const int sCol1 = 12, sCol2 = 80, sBtnW = 64, sLnH = 28;
        int sRowY = 44;
        lblStructComposer.Text = "作曲:";
        lblStructComposer.Location = new Point(sCol1, sRowY + 4); lblStructComposer.Size = new Size(56, 18);
        lblStructComposerValue.Location = new Point(sCol2, sRowY + 4); lblStructComposerValue.Size = new Size(216, 18);
        lblStructComposerValue.AutoEllipsis = true; lblStructComposerValue.Text = "(未設定)"; lblStructComposerValue.ForeColor = Color.DimGray;
        btnEditStructComposer.Text = "編集..."; btnEditStructComposer.Location = new Point(sCol2 + 220, sRowY); btnEditStructComposer.Size = new Size(sBtnW, 24);
        sRowY += sLnH;
        lblStructArranger.Text = "編曲:";
        lblStructArranger.Location = new Point(sCol1, sRowY + 4); lblStructArranger.Size = new Size(56, 18);
        lblStructArrangerValue.Location = new Point(sCol2, sRowY + 4); lblStructArrangerValue.Size = new Size(216, 18);
        lblStructArrangerValue.AutoEllipsis = true; lblStructArrangerValue.Text = "(未設定)"; lblStructArrangerValue.ForeColor = Color.DimGray;
        btnEditStructArranger.Text = "編集..."; btnEditStructArranger.Location = new Point(sCol2 + 220, sRowY); btnEditStructArranger.Size = new Size(sBtnW, 24);

        grpStructCredits.Controls.AddRange(new Control[]
        {
            lblStructHint,
            lblStructComposer, lblStructComposerValue, btnEditStructComposer,
            lblStructArranger, lblStructArrangerValue, btnEditStructArranger
        });
        pnlCueDetail.Controls.Add(grpStructCredits);

        // 下段: 収録ディスク・トラック一覧
        lblCueTracks.Text = "この劇伴 cue の収録ディスク・トラック";
        lblCueTracks.Dock = DockStyle.Top;
        lblCueTracks.Height = 22;
        lblCueTracks.Padding = new Padding(6, 4, 0, 0);
        gridCueTracks.Dock = DockStyle.Fill;
        gridCueTracks.AllowUserToAddRows = false;
        gridCueTracks.AllowUserToDeleteRows = false;
        gridCueTracks.ReadOnly = true;
        gridCueTracks.MultiSelect = false;
        gridCueTracks.RowHeadersVisible = false;
        gridCueTracks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        splitOuter.Panel2.Controls.Add(gridCueTracks);
        splitOuter.Panel2.Controls.Add(lblCueTracks);

        // フォーム
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 820);
        Controls.Add(splitOuter);
        Controls.Add(pnlSearch);
        Name = "BgmCuesEditorForm";
        Text = "劇伴マスタ管理 - Catalog";
        StartPosition = FormStartPosition.CenterParent;
    }

    /// <summary>ラベル + コントロールの 1 行を指定 y 座標に配置する。</summary>
    private static void AddRow(Panel panel, string label, Control control, int y)
    {
        const int lw = 110, fw = 260;
        var lbl = new Label { Text = label, Location = new Point(8, y + 4), Size = new Size(lw, 20) };
        control.Location = new Point(12 + lw, y);
        control.Size = new Size(fw, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}