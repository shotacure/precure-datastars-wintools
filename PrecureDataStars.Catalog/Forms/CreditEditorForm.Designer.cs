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

    // ───────────── 右ペイン：エントリ編集（v1.2.0 工程 B-3 で UserControl 化） ─────────────
    // 旧 B-1/B-2 の grpEntry / lblEntryKind / txtEntryPreview / lblNoticeB1 / btnSaveEntry /
    // btnDeleteEntry は撤去し、種別ごとの動的編集 UI を持つ EntryEditorPanel UserControl を
    // 1 個だけ Dock=Fill で配置する。エントリ編集モードと新規追加モードはパネル側で管理する。
    private Panel pnlRight = null!;
    private EntryEditorPanel entryEditor = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // SuspendLayout / ResumeLayout で初期化中の中間レイアウト計算を抑止する。
        // これにより SplitContainer のサイズが ClientSize に追従する前に
        // Panel*MinSize を設定して例外を起こす事故を確実に防げる。
        SuspendLayout();

        // ============================================================
        // フォーム自体
        // ============================================================
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        // v1.2.0 工程 B-2 修正：3 ペインが窮屈にならない初期サイズ。
        // 左 320 + 中央 600 + スプリッタ 8 + 右 380 = 1308px を確保（余裕を見て 1320 設定）
        ClientSize = new Size(1320, 820);
        Name = "CreditEditorForm";
        Text = "クレジット編集 (v1.2.0 工程 B-3：エントリ編集)";
        StartPosition = FormStartPosition.CenterParent;
        // フォーム最小サイズ：左 280 + 中央 600 + 右 340 + スプリッタ 2 本 ≒ 1230 を確保
        MinimumSize = new Size(1240, 650);

        // ============================================================
        // SplitContainer ルート
        // ============================================================
        // SplitContainer.Panel*MinSize は、SplitContainer 自身の Width / Height が
        // 確定してからでないと安全に反映できない。SplitContainer 既定の Width は 150px で、
        // 例えばその状態で Panel2MinSize=340 を初期化子で設定すると、内部で
        // SplitterDistance を「Width − Panel2MinSize = 150 − 340 = -190」へ動かそうとして
        // InvalidOperationException が発生する。そのため、ここでは:
        //   ・初期化子では Dock と FixedPanel だけを設定
        //   ・Controls.Add でフォームに追加し PerformLayout で Width を確定
        //   ・そのあとに Panel*MinSize を設定
        //   ・SplitterDistance は本体 cs の OnLoadAsync 冒頭で動的計算
        // という順序にしている。
        splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1
        };
        splitCenterRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2
        };

        // ============================================================
        // 左ペインの中身
        // ============================================================
        BuildLeftPane();
        BuildCenterPane();
        BuildRightPane();

        // 配置：Panel への Add → 親フォームへの Add の順
        splitMain.Panel1.Controls.Add(pnlLeft);
        splitMain.Panel2.Controls.Add(splitCenterRight);
        splitCenterRight.Panel1.Controls.Add(pnlCenter);
        splitCenterRight.Panel2.Controls.Add(pnlRight);

        Controls.Add(splitMain);

        // ここで splitMain と splitCenterRight の Width が ClientSize に追従して確定するので、
        // PerformLayout でレイアウトを強制実行してから Panel*MinSize を安全に設定できる。
        ResumeLayout(performLayout: false);
        PerformLayout();

        // Panel*MinSize 設定（Width 確定後に行うことで例外を防ぐ）
        // 中央ペイン Panel1 の最小幅 600 は、下部 7 ボタンの右端
        // （btnDeleteNode の X=528 + Width=70 = 598）を確保する最小値。
        splitMain.Panel1MinSize = 280;
        splitMain.Panel2MinSize = 720;
        splitCenterRight.Panel1MinSize = 600;
        splitCenterRight.Panel2MinSize = 340;
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

        // v1.2.0 工程 B' 再修正：本放送限定はクレジット単位ではなくエントリ単位で扱うため、
        // 左ペインの「本放送限定行も表示」チェックボックスは撤去。クレジット ListBox は
        // 常に scope_kind と series_id / episode_id だけで絞り込む。

        lblCreditList = new Label { Text = "クレジット", Location = new Point(12, 170), Size = new Size(80, 20) };
        lstCredits = new ListBox
        {
            Location = new Point(12, 192),
            Size = new Size(280, 130),
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
            ShowRootLines = true,
            // v1.2.0 工程 B-2 追加：TreeView ドラッグ＆ドロップ対応。
            // 同階層内のみドロップ許可は本体側の DragOver/DragDrop イベントで判定する。
            AllowDrop = true
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

        // v1.2.0 工程 B-3 で導入：エントリ編集 UI を持つ専用 UserControl。
        // CreditEditorForm 側からは Initialize / LoadForEditAsync / LoadForNew / ClearAndDisable
        // を呼び、保存・削除・追加完了は EntrySaved / EntryDeleted イベント経由で受け取る。
        entryEditor = new EntryEditorPanel
        {
            Dock = DockStyle.Fill
        };

        pnlRight.Controls.Add(entryEditor);
    }
}
