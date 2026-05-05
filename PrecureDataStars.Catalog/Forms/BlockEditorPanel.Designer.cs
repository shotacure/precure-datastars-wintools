namespace PrecureDataStars.Catalog.Forms;

partial class BlockEditorPanel
{
    private System.ComponentModel.IContainer components = null!;

    // ── プロパティ編集用コントロール群（v1.2.0 工程 H 補修で新設） ──
    private Label lblHeader = null!;
    private Label lblColCountCaption = null!;
    private NumericUpDown numColCount = null!;
    private Label lblColCountHelp = null!;
    private Label lblBlockSeqCaption = null!;
    private NumericUpDown numBlockSeq = null!;
    private Label lblLeadingCompanyCaption = null!;
    private NumericUpDown numLeadingCompanyAliasId = null!;
    private Button btnPickLeadingCompany = null!;
    private Button btnNewLeadingCompany = null!;
    private CheckBox chkLeadingCompanyNull = null!;
    private Label lblLeadingCompanyPreview = null!;
    private Label lblNotesCaption = null!;
    private TextBox txtNotes = null!;
    private Button btnSave = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) { components.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        SuspendLayout();
        Dock = DockStyle.Fill;
        Padding = new Padding(8);
        AutoScroll = true;

        // ── ヘッダラベル ──
        lblHeader = new Label
        {
            Text = "ブロックプロパティ",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Location = new Point(8, 8),
            Size = new Size(330, 24)
        };

        // ── col_count（ブロック内エントリの横カラム数） ──
        lblColCountCaption = new Label
        {
            Text = "カラム数 (col_count):",
            Location = new Point(8, 40), Size = new Size(150, 20)
        };
        numColCount = new NumericUpDown
        {
            Minimum = 1, Maximum = 10, Value = 1,
            Location = new Point(160, 38), Size = new Size(60, 23)
        };
        lblColCountHelp = new Label
        {
            Text = "（1=縦並び、2 以上で横カラム）",
            Location = new Point(226, 40), Size = new Size(220, 20),
            ForeColor = SystemColors.GrayText
        };

        // ── block_seq（役職内のブロック表示順） ──
        lblBlockSeqCaption = new Label
        {
            Text = "ブロック表示順 (block_seq):",
            Location = new Point(8, 70), Size = new Size(150, 20)
        };
        numBlockSeq = new NumericUpDown
        {
            Minimum = 1, Maximum = 50, Value = 1,
            Location = new Point(160, 68), Size = new Size(60, 23)
        };

        // ── leading_company_alias_id（ブロック先頭に出す企業屋号） ──
        lblLeadingCompanyCaption = new Label
        {
            Text = "先頭企業屋号 ID:",
            Location = new Point(8, 102), Size = new Size(150, 20)
        };
        numLeadingCompanyAliasId = new NumericUpDown
        {
            Minimum = 0, Maximum = 9_999_999, Value = 0,
            Location = new Point(160, 100), Size = new Size(80, 23)
        };
        btnPickLeadingCompany = new Button
        {
            Text = "検索...",
            Location = new Point(246, 99), Size = new Size(70, 25)
        };
        btnNewLeadingCompany = new Button
        {
            Text = "+ 新規屋号...",
            Location = new Point(322, 99), Size = new Size(110, 25)
        };
        chkLeadingCompanyNull = new CheckBox
        {
            Text = "未指定",
            Location = new Point(160, 128), Size = new Size(80, 20),
            Checked = true
        };
        lblLeadingCompanyPreview = new Label
        {
            Text = "(プレビュー: 屋号 ID を入力すると表示されます)",
            Location = new Point(8, 152), Size = new Size(440, 22),
            ForeColor = SystemColors.GrayText,
            BorderStyle = BorderStyle.FixedSingle
        };

        // ── 備考（ブロックの notes）──
        lblNotesCaption = new Label
        {
            Text = "備考:",
            Location = new Point(8, 184), Size = new Size(80, 20)
        };
        txtNotes = new TextBox
        {
            Location = new Point(8, 206), Size = new Size(440, 80),
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // ── 適用ボタン（v1.2.0 工程 H-8 ターン 5：旧「保存」から改名） ──
        // 押下でメモリ上の Draft.Entity に値を反映する。DB への書き込みは中央ペイン下の
        // 「💾 保存」ボタンで一括実行されるため、混同を避けて「適用」ラベルとする。
        btnSave = new Button
        {
            Text = "適用",
            Location = new Point(8, 296), Size = new Size(120, 30)
        };

        Controls.AddRange(new Control[]
        {
            lblHeader,
            lblColCountCaption, numColCount, lblColCountHelp,
            lblBlockSeqCaption, numBlockSeq,
            lblLeadingCompanyCaption, numLeadingCompanyAliasId, btnPickLeadingCompany, btnNewLeadingCompany,
            chkLeadingCompanyNull, lblLeadingCompanyPreview,
            lblNotesCaption, txtNotes,
            btnSave
        });

        Name = "BlockEditorPanel";
        ResumeLayout(false);
    }
}
