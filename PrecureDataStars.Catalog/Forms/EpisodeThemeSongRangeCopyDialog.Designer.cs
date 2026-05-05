namespace PrecureDataStars.Catalog.Forms;

partial class EpisodeThemeSongRangeCopyDialog
{
    private System.ComponentModel.IContainer components = null!;

    // ─── コピー元グループ ───
    private GroupBox grpSrc = null!;
    private Label lblSrcSeries = null!;
    private ComboBox cboSrcSeries = null!;
    private Label lblSrcEpisode = null!;
    private ComboBox cboSrcEpisode = null!;

    // ─── コピー先（範囲指定）グループ ───
    private GroupBox grpDst = null!;
    private Label lblDstSeries = null!;
    private ComboBox cboDstSeries = null!;
    private Label lblDstFrom = null!;
    private NumericUpDown numDstFrom = null!;
    private Label lblDstTo = null!;
    private NumericUpDown numDstTo = null!;
    private Label lblDstHint = null!;

    // ─── オプショングループ ───
    private GroupBox grpOptions = null!;
    private CheckBox chkOverwrite = null!;
    private CheckBox chkIncludeBroadcastOnly = null!;

    // ─── プレビュー欄 ───
    private Label lblPreview = null!;
    private TextBox txtPreview = null!;

    // ─── ボタン ───
    private Button btnExecute = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) { components.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        SuspendLayout();
        Text = "エピソード主題歌の範囲コピー";
        ClientSize = new Size(620, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(8);

        // ── コピー元 ──
        grpSrc = new GroupBox
        {
            Text = "コピー元エピソード",
            Location = new Point(8, 8), Size = new Size(600, 80)
        };
        lblSrcSeries = new Label { Text = "シリーズ:", Location = new Point(12, 24), Size = new Size(80, 20) };
        cboSrcSeries = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(96, 22), Size = new Size(490, 23)
        };
        lblSrcEpisode = new Label { Text = "エピソード:", Location = new Point(12, 50), Size = new Size(80, 20) };
        cboSrcEpisode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(96, 48), Size = new Size(490, 23)
        };
        grpSrc.Controls.Add(lblSrcSeries);
        grpSrc.Controls.Add(cboSrcSeries);
        grpSrc.Controls.Add(lblSrcEpisode);
        grpSrc.Controls.Add(cboSrcEpisode);

        // ── コピー先（範囲指定）──
        grpDst = new GroupBox
        {
            Text = "コピー先（範囲指定）",
            Location = new Point(8, 96), Size = new Size(600, 110)
        };
        lblDstSeries = new Label { Text = "シリーズ:", Location = new Point(12, 24), Size = new Size(80, 20) };
        cboDstSeries = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(96, 22), Size = new Size(490, 23)
        };
        lblDstFrom = new Label { Text = "第", Location = new Point(96, 56), Size = new Size(20, 20) };
        numDstFrom = new NumericUpDown
        {
            Minimum = 1, Maximum = 999, Value = 2,
            Location = new Point(118, 54), Size = new Size(60, 23)
        };
        lblDstTo = new Label { Text = "話 ～ 第", Location = new Point(184, 56), Size = new Size(60, 20) };
        numDstTo = new NumericUpDown
        {
            Minimum = 1, Maximum = 999, Value = 49,
            Location = new Point(248, 54), Size = new Size(60, 23)
        };
        lblDstHint = new Label
        {
            Text = "話（series_ep_no で指定）",
            Location = new Point(314, 56), Size = new Size(280, 20),
            ForeColor = SystemColors.GrayText
        };
        grpDst.Controls.Add(lblDstSeries);
        grpDst.Controls.Add(cboDstSeries);
        grpDst.Controls.Add(lblDstFrom);
        grpDst.Controls.Add(numDstFrom);
        grpDst.Controls.Add(lblDstTo);
        grpDst.Controls.Add(numDstTo);
        grpDst.Controls.Add(lblDstHint);

        // ── オプション ──
        grpOptions = new GroupBox
        {
            Text = "オプション",
            Location = new Point(8, 214), Size = new Size(600, 80)
        };
        chkOverwrite = new CheckBox
        {
            Text = "既存のコピー先主題歌を上書きする（既定 OFF）",
            Location = new Point(12, 24), Size = new Size(580, 22)
        };
        chkIncludeBroadcastOnly = new CheckBox
        {
            Text = "本放送限定行（is_broadcast_only=1）も同時にコピー",
            Location = new Point(12, 50), Size = new Size(580, 22),
            Checked = true
        };
        grpOptions.Controls.Add(chkOverwrite);
        grpOptions.Controls.Add(chkIncludeBroadcastOnly);

        // ── プレビュー ──
        lblPreview = new Label { Text = "コピー対象プレビュー:", Location = new Point(8, 304), Size = new Size(160, 20) };
        txtPreview = new TextBox
        {
            Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(8, 326), Size = new Size(600, 220),
            Font = new Font(FontFamily.GenericMonospace, 9f),
            BackColor = SystemColors.Window
        };

        // ── ボタン ──
        btnExecute = new Button
        {
            Text = "実行",
            Location = new Point(384, 558), Size = new Size(100, 32)
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(496, 558), Size = new Size(112, 32)
        };

        Controls.AddRange(new Control[]
        {
            grpSrc, grpDst, grpOptions, lblPreview, txtPreview, btnExecute, btnCancel
        });

        AcceptButton = btnExecute;
        CancelButton = btnCancel;

        Name = "EpisodeThemeSongRangeCopyDialog";
        ResumeLayout(false);
    }
}
