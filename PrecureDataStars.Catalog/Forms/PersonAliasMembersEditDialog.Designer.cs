#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class PersonAliasMembersEditDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblHeader = null!;
    private DataGridView gridMembers = null!;

    private GroupBox grpDetail = null!;
    private Label lblSelectedSeq = null!;
    private Label lblSeqValue = null!;

    private Label lblKind = null!;
    private ComboBox cboKind = null!;

    private Label lblMember = null!;
    private TextBox txtMemberDisplay = null!;
    private Button btnPickPerson = null!;
    private Button btnPickCharacter = null!;

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
        ClientSize = new Size(880, 540);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 460);
        Name = "PersonAliasMembersEditDialog";
        Text = "ユニット名義メンバー管理";

        lblHeader = new Label
        {
            Location = new Point(8, 8),
            Size = new Size(864, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "メンバーは PERSON（人物名義）または CHARACTER（キャラ名義）。" +
                   "PERSON メンバーが既にユニットだったり、このユニット自身が他ユニットのメンバーになっている場合は、" +
                   "DB トリガーで保存時に拒否されます（ネスト不可）。",
            ForeColor = Color.DimGray
        };

        gridMembers = new DataGridView
        {
            Location = new Point(8, 48),
            Size = new Size(864, 280),
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
        gridMembers.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "colSeq",     HeaderText = "Seq",   Width = 60,  DataPropertyName = "Seq" },
            new DataGridViewTextBoxColumn { Name = "colKind",    HeaderText = "種別",  Width = 110, DataPropertyName = "KindLabel" },
            new DataGridViewTextBoxColumn { Name = "colMember",  HeaderText = "メンバー", Width = 420, DataPropertyName = "MemberDisplay" },
            new DataGridViewTextBoxColumn { Name = "colNotes",   HeaderText = "備考",  Width = 240, DataPropertyName = "Notes" }
        });

        grpDetail = new GroupBox
        {
            Text = "選択行の詳細",
            Location = new Point(8, 336),
            Size = new Size(864, 144),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        lblSelectedSeq = new Label { Text = "Seq:",   Location = new Point(12, 28), AutoSize = true };
        lblSeqValue    = new Label { Text = "(新規)", Location = new Point(56, 28), AutoSize = true, ForeColor = Color.DimGray };

        lblKind = new Label { Text = "種別:", Location = new Point(120, 28), AutoSize = true };
        cboKind = new ComboBox
        {
            Location = new Point(160, 24),
            Size = new Size(160, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cboKind.Items.Add("PERSON");
        cboKind.Items.Add("CHARACTER");

        lblMember = new Label { Text = "メンバー:", Location = new Point(12, 64), AutoSize = true };
        txtMemberDisplay = new TextBox
        {
            Location = new Point(120, 60),
            Size = new Size(440, 23),
            ReadOnly = true,
            BackColor = SystemColors.Window
        };
        btnPickPerson    = new Button { Text = "PERSON 選択...",    Location = new Point(568, 59), Size = new Size(120, 25) };
        btnPickCharacter = new Button { Text = "CHARACTER 選択...", Location = new Point(696, 59), Size = new Size(120, 25) };

        lblNotes = new Label { Text = "備考:", Location = new Point(12, 96), AutoSize = true };
        txtNotes = new TextBox
        {
            Location = new Point(120, 92),
            Size = new Size(560, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        btnAdd    = new Button { Text = "+ 追加", Location = new Point(700, 22), Size = new Size(80, 25) };
        btnApply  = new Button { Text = "✓ 反映", Location = new Point(700, 50), Size = new Size(80, 25) };
        btnDelete = new Button { Text = "✕ 削除", Location = new Point(700, 78), Size = new Size(80, 25) };
        btnUp     = new Button { Text = "↑ 上",  Location = new Point(700, 106), Size = new Size(38, 25) };
        btnDown   = new Button { Text = "↓ 下",  Location = new Point(742, 106), Size = new Size(38, 25) };

        grpDetail.Controls.AddRange(new Control[]
        {
            lblSelectedSeq, lblSeqValue,
            lblKind, cboKind,
            lblMember, txtMemberDisplay, btnPickPerson, btnPickCharacter,
            lblNotes, txtNotes,
            btnAdd, btnApply, btnDelete, btnUp, btnDown
        });

        btnOk     = new Button { Text = "OK",        Location = new Point(704, 488), Size = new Size(80, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK };
        btnCancel = new Button { Text = "キャンセル", Location = new Point(792, 488), Size = new Size(80, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        Controls.Add(lblHeader);
        Controls.Add(gridMembers);
        Controls.Add(grpDetail);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}