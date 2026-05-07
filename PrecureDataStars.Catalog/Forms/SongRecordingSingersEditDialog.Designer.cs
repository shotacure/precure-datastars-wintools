#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class SongRecordingSingersEditDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblHeader = null!;
    private DataGridView gridLines = null!;

    private GroupBox grpDetail = null!;
    private Label lblSelectedSeq = null!;
    private Label lblSeqValue = null!;

    private Label lblKind = null!;
    private ComboBox cboKind = null!;

    // PERSON 系
    private Label lblPerson = null!;
    private TextBox txtPersonDisplay = null!;
    private Button btnPickPerson = null!;
    private Label lblSlashPerson = null!;
    private TextBox txtSlashPersonDisplay = null!;
    private Button btnPickSlashPerson = null!;
    private Button btnClearSlashPerson = null!;

    // CHARACTER_WITH_CV 系
    private Label lblCharacter = null!;
    private TextBox txtCharacterDisplay = null!;
    private Button btnPickCharacter = null!;
    private Label lblVoice = null!;
    private TextBox txtVoiceDisplay = null!;
    private Button btnPickVoice = null!;
    private Label lblSlashCharacter = null!;
    private TextBox txtSlashCharacterDisplay = null!;
    private Button btnPickSlashCharacter = null!;
    private Button btnClearSlashCharacter = null!;

    // 共通
    private Label lblSeparator = null!;
    private TextBox txtSeparator = null!;
    private Label lblSepHint = null!;
    private Label lblAffiliation = null!;
    private TextBox txtAffiliation = null!;
    private Label lblAffHint = null!;
    private Label lblNotes = null!;
    private TextBox txtNotes = null!;

    private Button btnAdd = null!;
    private Button btnApply = null!;
    private Button btnDelete = null!;
    private Button btnUp = null!;
    private Button btnDown = null!;

    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(960, 760);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 700);
        Name = "SongRecordingSingersEditDialog";
        Text = "歌唱者クレジット編集";

        lblHeader = new Label
        {
            Location = new Point(8, 8),
            Size = new Size(944, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "歌唱者を seq=1 から順に並べてください。billing_kind が PERSON のときは「主名義」と任意のスラッシュ相方（人物）、" +
                   "CHARACTER_WITH_CV のときは「主キャラ」と「CV」（必須）と任意のスラッシュ相方（キャラ）を指定します。",
            ForeColor = Color.DimGray
        };

        gridLines = new DataGridView
        {
            Location = new Point(8, 48),
            Size = new Size(944, 240),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        gridLines.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "colSeq",     HeaderText = "Seq",        Width = 50,  DataPropertyName = "Seq" },
            new DataGridViewTextBoxColumn { Name = "colKind",    HeaderText = "Kind",       Width = 130, DataPropertyName = "KindLabel" },
            new DataGridViewTextBoxColumn { Name = "colDisplay", HeaderText = "表示テキスト", Width = 540, DataPropertyName = "FullDisplay" },
            new DataGridViewTextBoxColumn { Name = "colSep",     HeaderText = "区切り",      Width = 90,  DataPropertyName = "Separator" },
            new DataGridViewTextBoxColumn { Name = "colAff",     HeaderText = "所属",        Width = 120, DataPropertyName = "AffiliationText" }
        });

        grpDetail = new GroupBox
        {
            Text = "選択行の詳細",
            Location = new Point(8, 296),
            Size = new Size(944, 396),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        lblSelectedSeq = new Label { Text = "Seq:",   Location = new Point(12, 28), AutoSize = true };
        lblSeqValue    = new Label { Text = "(新規)", Location = new Point(56, 28), AutoSize = true, ForeColor = Color.DimGray };

        lblKind = new Label { Text = "種別:", Location = new Point(120, 28), AutoSize = true };
        cboKind = new ComboBox
        {
            Location = new Point(160, 24),
            Size = new Size(200, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cboKind.Items.Add("PERSON");
        cboKind.Items.Add("CHARACTER_WITH_CV");

        // PERSON 系
        lblPerson = new Label { Text = "主名義(PERSON):", Location = new Point(12, 64), AutoSize = true };
        txtPersonDisplay = new TextBox { Location = new Point(160, 60), Size = new Size(440, 23), ReadOnly = true, BackColor = SystemColors.Window };
        btnPickPerson = new Button { Text = "選択...", Location = new Point(608, 59), Size = new Size(72, 25) };

        lblSlashPerson = new Label { Text = "スラッシュ相方:", Location = new Point(12, 96), AutoSize = true };
        txtSlashPersonDisplay = new TextBox { Location = new Point(160, 92), Size = new Size(440, 23), ReadOnly = true, BackColor = SystemColors.Window };
        btnPickSlashPerson = new Button { Text = "選択...", Location = new Point(608, 91), Size = new Size(72, 25) };
        btnClearSlashPerson = new Button { Text = "クリア", Location = new Point(688, 91), Size = new Size(64, 25) };

        // CHARACTER_WITH_CV 系
        lblCharacter = new Label { Text = "主キャラ:", Location = new Point(12, 144), AutoSize = true };
        txtCharacterDisplay = new TextBox { Location = new Point(160, 140), Size = new Size(440, 23), ReadOnly = true, BackColor = SystemColors.Window };
        btnPickCharacter = new Button { Text = "選択...", Location = new Point(608, 139), Size = new Size(72, 25) };

        lblVoice = new Label { Text = "CV(声優):", Location = new Point(12, 176), AutoSize = true };
        txtVoiceDisplay = new TextBox { Location = new Point(160, 172), Size = new Size(440, 23), ReadOnly = true, BackColor = SystemColors.Window };
        btnPickVoice = new Button { Text = "選択...", Location = new Point(608, 171), Size = new Size(72, 25) };

        lblSlashCharacter = new Label { Text = "スラッシュ相方:", Location = new Point(12, 208), AutoSize = true };
        txtSlashCharacterDisplay = new TextBox { Location = new Point(160, 204), Size = new Size(440, 23), ReadOnly = true, BackColor = SystemColors.Window };
        btnPickSlashCharacter = new Button { Text = "選択...", Location = new Point(608, 203), Size = new Size(72, 25) };
        btnClearSlashCharacter = new Button { Text = "クリア", Location = new Point(688, 203), Size = new Size(64, 25) };

        // 共通
        lblSeparator = new Label { Text = "区切り(seq>=2):", Location = new Point(12, 248), AutoSize = true };
        txtSeparator = new TextBox { Location = new Point(160, 244), Size = new Size(80, 23), MaxLength = 8 };
        lblSepHint   = new Label
        {
            Text = "例:  ＆   、   ` with `（前後のSPは表記通り）",
            Location = new Point(248, 248),
            AutoSize = true,
            ForeColor = Color.DimGray
        };

        lblAffiliation = new Label { Text = "所属表記:", Location = new Point(12, 280), AutoSize = true };
        txtAffiliation = new TextBox { Location = new Point(160, 276), Size = new Size(440, 23), MaxLength = 64 };
        lblAffHint = new Label
        {
            Text = "例: 「with ヤング・フレッシュ」など、CV/スラッシュ枠で表現できない補助テキスト",
            Location = new Point(608, 280),
            AutoSize = true,
            ForeColor = Color.DimGray
        };

        lblNotes = new Label { Text = "備考:", Location = new Point(12, 312), AutoSize = true };
        txtNotes = new TextBox
        {
            Location = new Point(160, 308),
            Size = new Size(672, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        btnAdd    = new Button { Text = "+ 追加", Location = new Point(840, 22), Size = new Size(80, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnApply  = new Button { Text = "✓ 反映", Location = new Point(840, 50), Size = new Size(80, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnDelete = new Button { Text = "✕ 削除", Location = new Point(840, 78), Size = new Size(80, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnUp     = new Button { Text = "↑ 上",  Location = new Point(840, 106), Size = new Size(38, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnDown   = new Button { Text = "↓ 下",  Location = new Point(882, 106), Size = new Size(38, 25), Anchor = AnchorStyles.Top | AnchorStyles.Right };

        grpDetail.Controls.AddRange(new Control[]
        {
            lblSelectedSeq, lblSeqValue,
            lblKind, cboKind,
            lblPerson, txtPersonDisplay, btnPickPerson,
            lblSlashPerson, txtSlashPersonDisplay, btnPickSlashPerson, btnClearSlashPerson,
            lblCharacter, txtCharacterDisplay, btnPickCharacter,
            lblVoice, txtVoiceDisplay, btnPickVoice,
            lblSlashCharacter, txtSlashCharacterDisplay, btnPickSlashCharacter, btnClearSlashCharacter,
            lblSeparator, txtSeparator, lblSepHint,
            lblAffiliation, txtAffiliation, lblAffHint,
            lblNotes, txtNotes,
            btnAdd, btnApply, btnDelete, btnUp, btnDown
        });

        btnOk     = new Button { Text = "OK",        Location = new Point(784, 704), Size = new Size(80, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK };
        btnCancel = new Button { Text = "キャンセル", Location = new Point(872, 704), Size = new Size(80, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        Controls.Add(lblHeader);
        Controls.Add(gridLines);
        Controls.Add(grpDetail);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}
