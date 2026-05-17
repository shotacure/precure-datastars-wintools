#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class EpisodeThemeSongCopyDialog
{
    private System.ComponentModel.IContainer? components = null;

    // [1] コピー元
    private GroupBox grpSource = null!;
    private Label lblSrcSeries = null!;
    private ComboBox cboSrcSeries = null!;
    private Label lblSrcEpisode = null!;
    private ComboBox cboSrcEpisode = null!;
    private Label lblSrcFlagCaption = null!;
    private CheckBox chkSrcLoadCommon = null!;        // 全媒体共通行（フラグ 0）を読み込む
    private CheckBox chkSrcLoadBroadcastOnly = null!; // 本放送限定行（フラグ 1）を読み込む
    private Button btnLoadSrc = null!;
    private Label lblSrcStatus = null!;

    // [2] コピー先
    private GroupBox grpTarget = null!;
    private Label lblTgtSeries = null!;
    private ComboBox cboTgtSeries = null!;
    private Label lblTgtEpFrom = null!;
    private ComboBox cboTgtEpFrom = null!;
    private Label lblTgtEpTo = null!;
    private ComboBox cboTgtEpTo = null!;
    private Label lblTgtFlagOverrideCaption = null!;
    private RadioButton rbTgtKeepSrcFlag = null!;       // コピー元のフラグをそのまま使う
    private RadioButton rbTgtForceCommon = null!;       // 全行を全媒体共通（=0）に強制
    private RadioButton rbTgtForceBroadcastOnly = null!;// 全行を本放送限定（=1）に強制
    private Button btnGeneratePreview = null!;
    private Label lblTgtStatus = null!;

    // [3] プレビュー
    private GroupBox grpPreview = null!;
    private DataGridView gridPreview = null!;
    private Button btnRemoveSelected = null!;
    private Label lblPreviewStatus = null!;
    private Button btnSaveAll = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // ───────────── [1] コピー元 ─────────────
        grpSource = new GroupBox
        {
            Text = "[1] コピー元",
            Location = new Point(12, 12),
            Size = new Size(996, 130),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        lblSrcSeries  = new Label { Text = "シリーズ",       Location = new Point(14, 28), Size = new Size(70, 20) };
        cboSrcSeries  = new ComboBox { Location = new Point(86, 24), Size = new Size(280, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        lblSrcEpisode = new Label { Text = "エピソード",     Location = new Point(380, 28), Size = new Size(80, 20) };
        cboSrcEpisode = new ComboBox { Location = new Point(462, 24), Size = new Size(360, 23), DropDownStyle = ComboBoxStyle.DropDownList };

        // 本放送フラグの読み込み対象（チェックボックス 2 つで両方／片方を選べる）
        lblSrcFlagCaption = new Label
        {
            Text = "読み込む行:",
            Location = new Point(14, 64),
            Size = new Size(80, 20)
        };
        chkSrcLoadCommon = new CheckBox
        {
            Text = "全媒体共通行（既定 / フラグ 0）",
            Location = new Point(96, 62),
            Size = new Size(240, 22),
            Checked = true
        };
        chkSrcLoadBroadcastOnly = new CheckBox
        {
            Text = "本放送限定行（フラグ 1）",
            Location = new Point(346, 62),
            Size = new Size(220, 22),
            Checked = false
        };

        btnLoadSrc    = new Button { Text = "コピー元を読み込み", Location = new Point(14, 92), Size = new Size(160, 27) };
        lblSrcStatus  = new Label { Location = new Point(184, 96), Size = new Size(800, 20), Text = "" };
        grpSource.Controls.AddRange(new Control[]
        {
            lblSrcSeries, cboSrcSeries, lblSrcEpisode, cboSrcEpisode,
            lblSrcFlagCaption, chkSrcLoadCommon, chkSrcLoadBroadcastOnly,
            btnLoadSrc, lblSrcStatus
        });

        // ───────────── [2] コピー先 ─────────────
        grpTarget = new GroupBox
        {
            Text = "[2] コピー先",
            Location = new Point(12, 150),
            Size = new Size(996, 130),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        lblTgtSeries  = new Label { Text = "シリーズ", Location = new Point(14, 28), Size = new Size(70, 20) };
        cboTgtSeries  = new ComboBox { Location = new Point(86, 24), Size = new Size(280, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        lblTgtEpFrom  = new Label { Text = "話数 from", Location = new Point(380, 28), Size = new Size(70, 20) };
        cboTgtEpFrom  = new ComboBox { Location = new Point(452, 24), Size = new Size(220, 23), DropDownStyle = ComboBoxStyle.DropDownList };
        lblTgtEpTo    = new Label { Text = "to", Location = new Point(682, 28), Size = new Size(20, 20) };
        cboTgtEpTo    = new ComboBox { Location = new Point(704, 24), Size = new Size(220, 23), DropDownStyle = ComboBoxStyle.DropDownList };

        lblTgtFlagOverrideCaption = new Label
        {
            Text = "本放送フラグ:",
            Location = new Point(14, 64),
            Size = new Size(90, 20)
        };
        rbTgtKeepSrcFlag = new RadioButton
        {
            Text = "コピー元と同じ（推奨）",
            Location = new Point(106, 62),
            Size = new Size(180, 22),
            Checked = true
        };
        rbTgtForceCommon = new RadioButton
        {
            Text = "全行を全媒体共通(=0)に",
            Location = new Point(296, 62),
            Size = new Size(200, 22)
        };
        rbTgtForceBroadcastOnly = new RadioButton
        {
            Text = "全行を本放送限定(=1)に",
            Location = new Point(506, 62),
            Size = new Size(200, 22)
        };

        btnGeneratePreview = new Button { Text = "プレビュー生成", Location = new Point(14, 92), Size = new Size(160, 27) };
        lblTgtStatus  = new Label { Location = new Point(184, 96), Size = new Size(800, 20), Text = "" };
        grpTarget.Controls.AddRange(new Control[]
        {
            lblTgtSeries, cboTgtSeries, lblTgtEpFrom, cboTgtEpFrom, lblTgtEpTo, cboTgtEpTo,
            lblTgtFlagOverrideCaption, rbTgtKeepSrcFlag, rbTgtForceCommon, rbTgtForceBroadcastOnly,
            btnGeneratePreview, lblTgtStatus
        });

        // ───────────── [3] プレビュー ─────────────
        grpPreview = new GroupBox
        {
            Text = "[3] プレビュー（保存前のステージング。編集・行削除可。「すべて保存」で初めて DB に書き込まれます）",
            Location = new Point(12, 288),
            Size = new Size(996, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        gridPreview = new DataGridView
        {
            Location = new Point(14, 24),
            Size = new Size(968, 240),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        btnRemoveSelected = new Button { Text = "選択行を除外", Location = new Point(14, 274), Size = new Size(140, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        lblPreviewStatus  = new Label { Location = new Point(170, 280), Size = new Size(700, 20), Text = "", Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        grpPreview.Controls.AddRange(new Control[] { gridPreview, btnRemoveSelected, lblPreviewStatus });

        // ───────────── 下部ボタン ─────────────
        btnSaveAll = new Button
        {
            Text = "すべて保存",
            Location = new Point(782, 622),
            Size = new Size(110, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(898, 622),
            Size = new Size(110, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1020, 666);
        Controls.AddRange(new Control[] { grpSource, grpTarget, grpPreview, btnSaveAll, btnCancel });
        Name = "EpisodeThemeSongCopyDialog";
        Text = "エピソード主題歌 — 他話からコピー";
        StartPosition = FormStartPosition.CenterParent;
        CancelButton = btnCancel;
        MinimumSize = new Size(900, 600);
    }
}