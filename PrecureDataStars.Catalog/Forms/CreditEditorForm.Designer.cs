#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class CreditEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // ───────────── ルート構造 ─────────────
    private SplitContainer splitMain = null!;          // 左 | (中央+右)
    private SplitContainer splitCenterRight = null!;   // 中央 | 右

    // ───────────── 左ペイン：クレジット選択 ─────────────
    private Panel pnlLeft = null!;
    private GroupBox grpScope = null!;
    private Label lblScopeKind = null!;
    private RadioButton rbScopeSeries = null!;
    private RadioButton rbScopeEpisode = null!;
    private Label lblSeries = null!;
    private ComboBox cboSeries = null!;
    private Label lblEpisode = null!;
    private ComboBox cboEpisode = null!;
    private CheckBox chkShowBroadcastOnly = null!;     // v1.2.0 工程 B' 仕様変更：本放送限定行を含めて表示するか
    private Label lblCreditList = null!;
    private ListBox lstCredits = null!;
    private Button btnNewCredit = null!;        // B-1 では無効、B-2 で有効化

    // 左ペイン：選択中クレジットのプロパティ（B-1 では read-only 表示）
    private GroupBox grpCreditProps = null!;
    private Label lblPresentation = null!;
    private RadioButton rbPresentationCards = null!;
    private RadioButton rbPresentationRoll = null!;
    private Label lblPartType = null!;
    private ComboBox cboPartType = null!;
    private Label lblCreditNotes = null!;
    private TextBox txtCreditNotes = null!;
    private Button btnSaveCreditProps = null!;  // B-1 では無効
    private Button btnDeleteCredit = null!;     // B-1 では無効

    // ───────────── 中央ペイン：構造ツリー ─────────────
    private Panel pnlCenter = null!;
    private Label lblStatusBar = null!;          // 「現在編集中: …」
    private TreeView treeStructure = null!;
    private Panel pnlTreeButtons = null!;        // ツリー操作ボタン群、B-1 では全て無効
    private Button btnAddCard = null!;
    private Button btnAddRole = null!;
    private Button btnAddBlock = null!;
    private Button btnAddEntry = null!;
    private Button btnMoveUp = null!;
    private Button btnMoveDown = null!;
    private Button btnDeleteNode = null!;

    // ───────────── 右ペイン：エントリ編集 ─────────────
    private Panel pnlRight = null!;
    private GroupBox grpEntry = null!;
    private Label lblEntryKindCaption = null!;   // 「種別:」固定ラベル
    private Label lblEntryKind = null!;          // 現在のエントリ種別表示
    private Label lblEntryPreviewCaption = null!; // 「内容:」固定ラベル
    private TextBox txtEntryPreview = null!;     // プレビュー文字列（読み取り専用、B-1）
    private Label lblNoticeB1 = null!;           // 「編集機能は工程 B-2 以降で追加」案内
    private Button btnSaveEntry = null!;         // B-1 では無効
    private Button btnDeleteEntry = null!;       // B-1 では無効

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // ============================================================
        // フォーム自体
        // ============================================================
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 800);
        Name = "CreditEditorForm";
        Text = "クレジット編集 (v1.2.0 工程 B-1：表示のみ)";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1100, 700);

        // ============================================================
        // SplitContainer ルート
        // ============================================================
        splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 320
        };
        splitCenterRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = 600
        };

        // ============================================================
        // 左ペインの中身
        // ============================================================
        BuildLeftPane();
        BuildCenterPane();
        BuildRightPane();

        // 配置
        splitMain.Panel1.Controls.Add(pnlLeft);
        splitMain.Panel2.Controls.Add(splitCenterRight);
        splitCenterRight.Panel1.Controls.Add(pnlCenter);
        splitCenterRight.Panel2.Controls.Add(pnlRight);

        Controls.Add(splitMain);
    }

    // ============================================================
    // 左ペイン
    // ============================================================
    private void BuildLeftPane()
    {
        pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // ── スコープ＋クレジット選択 ──
        grpScope = new GroupBox
        {
            Text = "対象クレジットの選択",
            Location = new Point(0, 0),
            Size = new Size(304, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        lblScopeKind = new Label { Text = "スコープ", Location = new Point(12, 24), Size = new Size(80, 20) };
        rbScopeSeries  = new RadioButton { Text = "SERIES",  Location = new Point(96, 22), Size = new Size(80, 22) };
        rbScopeEpisode = new RadioButton { Text = "EPISODE", Location = new Point(180, 22), Size = new Size(90, 22), Checked = true };

        lblSeries = new Label { Text = "シリーズ", Location = new Point(12, 56), Size = new Size(80, 20) };
        cboSeries = new ComboBox
        {
            Location = new Point(12, 78),
            Size = new Size(280, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        lblEpisode = new Label { Text = "エピソード", Location = new Point(12, 110), Size = new Size(80, 20) };
        cboEpisode = new ComboBox
        {
            Location = new Point(12, 132),
            Size = new Size(280, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        // v1.2.0 工程 B' 仕様変更：リリース文脈コンボをやめ、本放送限定行も含めて表示するか
        // のチェックボックスにする。OFF（既定）= 全媒体共通行（フラグ 0）のみ表示。
        // ON = 本放送限定行（フラグ 1）も併せて表示。
        chkShowBroadcastOnly = new CheckBox
        {
            Text = "本放送限定行も表示",
            Location = new Point(12, 166),
            Size = new Size(280, 22),
            Checked = false
        };

        lblCreditList = new Label { Text = "クレジット", Location = new Point(12, 196), Size = new Size(80, 20) };
        lstCredits = new ListBox
        {
            Location = new Point(12, 218),
            Size = new Size(280, 104),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        btnNewCredit = new Button
        {
            Text = "新規クレジット...",
            Location = new Point(12, 326),
            Size = new Size(140, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false   // B-2 で有効化
        };

        grpScope.Controls.AddRange(new Control[]
        {
            lblScopeKind, rbScopeSeries, rbScopeEpisode,
            lblSeries, cboSeries, lblEpisode, cboEpisode,
            chkShowBroadcastOnly,
            lblCreditList, lstCredits, btnNewCredit
        });

        // ── 選択中クレジットのプロパティ ──
        grpCreditProps = new GroupBox
        {
            Text = "選択中クレジットのプロパティ",
            Location = new Point(0, 368),
            Size = new Size(304, 290),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        lblPresentation = new Label { Text = "presentation", Location = new Point(12, 24), Size = new Size(110, 20) };
        rbPresentationCards = new RadioButton { Text = "CARDS", Location = new Point(120, 22), Size = new Size(70, 22), Checked = true };
        rbPresentationRoll  = new RadioButton { Text = "ROLL",  Location = new Point(196, 22), Size = new Size(70, 22) };

        lblPartType = new Label { Text = "part_type", Location = new Point(12, 56), Size = new Size(110, 20) };
        cboPartType = new ComboBox
        {
            Location = new Point(120, 52),
            Size = new Size(160, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        lblCreditNotes = new Label { Text = "備考", Location = new Point(12, 88), Size = new Size(80, 20) };
        txtCreditNotes = new TextBox
        {
            Location = new Point(12, 110),
            Size = new Size(280, 100),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true
        };

        btnSaveCreditProps = new Button
        {
            Text = "プロパティ保存",
            Location = new Point(12, 220),
            Size = new Size(140, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false   // B-2 で有効化
        };
        btnDeleteCredit = new Button
        {
            Text = "クレジット削除",
            Location = new Point(160, 220),
            Size = new Size(132, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false
        };

        grpCreditProps.Controls.AddRange(new Control[]
        {
            lblPresentation, rbPresentationCards, rbPresentationRoll,
            lblPartType, cboPartType,
            lblCreditNotes, txtCreditNotes,
            btnSaveCreditProps, btnDeleteCredit
        });

        pnlLeft.Controls.AddRange(new Control[] { grpScope, grpCreditProps });
    }

    // ============================================================
    // 中央ペイン
    // ============================================================
    private void BuildCenterPane()
    {
        pnlCenter = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        lblStatusBar = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            BackColor = SystemColors.Info,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "現在編集中: （クレジット未選択）"
        };

        treeStructure = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true
        };

        pnlTreeButtons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            BorderStyle = BorderStyle.FixedSingle
        };
        btnAddCard   = new Button { Text = "+ カード",     Location = new Point(8,  6), Size = new Size(80, 26), Enabled = false };
        btnAddRole   = new Button { Text = "+ 役職",       Location = new Point(92, 6), Size = new Size(80, 26), Enabled = false };
        btnAddBlock  = new Button { Text = "+ ブロック",   Location = new Point(176, 6), Size = new Size(90, 26), Enabled = false };
        btnAddEntry  = new Button { Text = "+ エントリ",   Location = new Point(270, 6), Size = new Size(90, 26), Enabled = false };
        btnMoveUp    = new Button { Text = "↑ 上へ",        Location = new Point(380, 6), Size = new Size(70, 26), Enabled = false };
        btnMoveDown  = new Button { Text = "↓ 下へ",        Location = new Point(454, 6), Size = new Size(70, 26), Enabled = false };
        btnDeleteNode = new Button { Text = "× 削除",       Location = new Point(528, 6), Size = new Size(70, 26), Enabled = false };

        pnlTreeButtons.Controls.AddRange(new Control[]
        {
            btnAddCard, btnAddRole, btnAddBlock, btnAddEntry,
            btnMoveUp, btnMoveDown, btnDeleteNode
        });

        // 重ね順注意：Bottom → Top → Fill の順で Add すると Fill が中身に収まる
        pnlCenter.Controls.Add(treeStructure);    // Fill 用：先に追加
        pnlCenter.Controls.Add(pnlTreeButtons);   // Bottom
        pnlCenter.Controls.Add(lblStatusBar);     // Top
    }

    // ============================================================
    // 右ペイン
    // ============================================================
    private void BuildRightPane()
    {
        pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        grpEntry = new GroupBox
        {
            Text = "選択中エントリ",
            Dock = DockStyle.Fill
        };

        lblEntryKindCaption = new Label { Text = "種別:", Location = new Point(12, 24), Size = new Size(50, 20) };
        lblEntryKind = new Label
        {
            Location = new Point(64, 24),
            Size = new Size(260, 20),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = "（未選択）"
        };

        lblEntryPreviewCaption = new Label { Text = "内容:", Location = new Point(12, 54), Size = new Size(50, 20) };
        txtEntryPreview = new TextBox
        {
            Location = new Point(12, 76),
            Size = new Size(330, 120),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        lblNoticeB1 = new Label
        {
            Location = new Point(12, 210),
            Size = new Size(330, 100),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = SystemColors.GrayText,
            Text =
                "工程 B-1：表示のみ\n\n" +
                "エントリの編集 UI と「+ 新規...」のマスタ自動投入機能は、" +
                "工程 B-3 で別途追加されます。\n\n" +
                "工程 B-2 では構造ツリーの追加・並べ替え・削除が " +
                "ツリーボタンと DnD の両方で可能になります。"
        };

        btnSaveEntry = new Button
        {
            Text = "保存",
            Location = new Point(12, 320),
            Size = new Size(100, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false
        };
        btnDeleteEntry = new Button
        {
            Text = "削除",
            Location = new Point(120, 320),
            Size = new Size(100, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false
        };

        grpEntry.Controls.AddRange(new Control[]
        {
            lblEntryKindCaption, lblEntryKind,
            lblEntryPreviewCaption, txtEntryPreview,
            lblNoticeB1,
            btnSaveEntry, btnDeleteEntry
        });

        pnlRight.Controls.Add(grpEntry);
    }
}
