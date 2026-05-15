#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class PersonAliasCreditsEditDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblHeader = null!;
    private DataGridView gridLines = null!;

    private GroupBox grpDetail = null!;
    private Label lblSelectedSeq = null!;
    private Label lblSeqValue = null!;
    private Label lblAlias = null!;
    private TextBox txtAliasDisplay = null!;
    private Button btnPickAlias = null!;
    private Label lblSeparator = null!;
    private TextBox txtSeparator = null!;
    private Label lblSepHint = null!;
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
        ClientSize = new Size(880, 560);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 480);
        Name = "PersonAliasCreditsEditDialog";
        Text = "作家クレジット編集";

        // ヘッダ説明ラベル
        lblHeader = new Label
        {
            Location = new Point(8, 8),
            Size = new Size(864, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "連名は seq=1 から順に並べてください。seq>=2 の行に区切り文字（・/＆/、/ / / with 等）を指定すると、" +
                   "前の名義との間に表示されます。",
            ForeColor = Color.DimGray
        };

        // 連名行グリッド
        gridLines = new DataGridView
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
        gridLines.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "colSeq",      HeaderText = "Seq",         Width = 60,  DataPropertyName = "Seq" },
            new DataGridViewTextBoxColumn { Name = "colAlias",    HeaderText = "名義",         Width = 360, DataPropertyName = "AliasDisplay" },
            new DataGridViewTextBoxColumn { Name = "colSep",      HeaderText = "区切り",       Width = 100, DataPropertyName = "Separator" },
            new DataGridViewTextBoxColumn { Name = "colNotes",    HeaderText = "備考",         Width = 220, DataPropertyName = "Notes" },
            new DataGridViewTextBoxColumn { Name = "colAliasId",  HeaderText = "alias_id",     Width = 80,  DataPropertyName = "AliasId", Visible = false }
        });

        // 詳細編集 GroupBox（選択行 or 新規行を編集）
        grpDetail = new GroupBox
        {
            Text = "選択行の詳細",
            Location = new Point(8, 336),
            Size = new Size(864, 156),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        lblSelectedSeq = new Label { Text = "Seq:",       Location = new Point(12, 28), AutoSize = true };
        lblSeqValue    = new Label { Text = "(新規)",     Location = new Point(56, 28), AutoSize = true, ForeColor = Color.DimGray };

        lblAlias = new Label { Text = "名義:",            Location = new Point(120, 28), AutoSize = true };
        txtAliasDisplay = new TextBox
        {
            Location = new Point(160, 24),
            Size = new Size(440, 23),
            ReadOnly = true,
            BackColor = SystemColors.Window
        };
        btnPickAlias = new Button { Text = "選択...", Location = new Point(608, 23), Size = new Size(72, 25) };

        lblSeparator = new Label { Text = "区切り(seq>=2):", Location = new Point(12, 64), AutoSize = true };
        txtSeparator = new TextBox { Location = new Point(120, 60), Size = new Size(80, 23), MaxLength = 8 };
        lblSepHint   = new Label
        {
            Text = "例:  ・   ＆   、   ` / `   ` with `（前後の半角SPは表記通り含める）",
            Location = new Point(208, 64),
            AutoSize = true,
            ForeColor = Color.DimGray
        };

        lblNotes = new Label { Text = "備考:", Location = new Point(12, 96), AutoSize = true };
        txtNotes = new TextBox
        {
            Location = new Point(120, 92),
            Size = new Size(560, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        btnAdd    = new Button { Text = "+ 追加",  Location = new Point(700, 22), Size = new Size(80, 25) };
        btnApply  = new Button { Text = "✓ 反映",  Location = new Point(700, 50), Size = new Size(80, 25) };
        btnDelete = new Button { Text = "✕ 削除",  Location = new Point(700, 78), Size = new Size(80, 25) };
        btnUp     = new Button { Text = "↑ 上",   Location = new Point(700, 106), Size = new Size(38, 25) };
        btnDown   = new Button { Text = "↓ 下",   Location = new Point(742, 106), Size = new Size(38, 25) };

        grpDetail.Controls.AddRange(new Control[]
        {
            lblSelectedSeq, lblSeqValue,
            lblAlias, txtAliasDisplay, btnPickAlias,
            lblSeparator, txtSeparator, lblSepHint,
            lblNotes, txtNotes,
            btnAdd, btnApply, btnDelete, btnUp, btnDown
        });

        btnOk     = new Button { Text = "OK",     Location = new Point(704, 504), Size = new Size(80, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK };
        btnCancel = new Button { Text = "キャンセル", Location = new Point(792, 504), Size = new Size(80, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

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