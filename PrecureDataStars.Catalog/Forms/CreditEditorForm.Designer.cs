#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class CreditEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // ───────────── ルート構造 ─────────────
    // 5 ペイン横並び化（左 | テキスト | プレビュー | ツリー | 警告）。
    // ステージ B-1a で旧「左 / 中央 / プレビュー / 右」4 ペインから再構成。
    // テキスト編集が SSoT で、ツリー・プレビュー・警告はそこからの派生表示。
    // SplitContainer は 2 ペイン分割なので 4 段にネスト：
    //   splitMain        ← 左 | 残り
    //   splitText        ← テキスト | 残り
    //   splitPreview     ← プレビュー | 残り
    //   splitTreeWarn    ← ツリー | 警告
    private SplitContainer splitMain = null!;            // 左 | (テキスト+プレビュー+ツリー+警告)
    private SplitContainer splitText = null!;            // テキスト | (プレビュー+ツリー+警告)
    private SplitContainer splitPreview = null!;         // プレビュー | (ツリー+警告)
    private SplitContainer splitTreeWarn = null!;        // ツリー | 警告

    // ───────────── テキスト編集ペイン（Stage 1a 新設） ─────────────
    // クレジット 1 件の全構造をテキスト書式で表示・編集する。SSoT（Single Source of Truth）。
    // クレジット選択時に Encoder で逆翻訳した内容が初期表示され、編集 → デバウンス → パース →
    // Draft 全置換 → ツリー/プレビュー反映 のパイプラインが Stage 1b で接続される。
    private Panel pnlText = null!;
    private Panel pnlTextHeader = null!;          // 上部ツールバー（見出し + 保存・取消ボタン）
    private Label lblTextHeader = null!;
    private TextBox txtBulkText = null!;

    // ───────────── 警告ペイン（Stage 1c で ListView 本実装、Stage 2 で機能強化） ─────────────
    private Panel pnlWarnings = null!;
    private Panel pnlWarningsHeader = null!;        // 上部ヘッダ：見出し + フィルタチェック
    private Label lblWarningsHeader = null!;
    private CheckBox chkFilterBlock = null!;        // 🔥 Block 表示 ON/OFF（Stage 2）
    private CheckBox chkFilterWarning = null!;      // ⚠ Warning 表示 ON/OFF（Stage 2）
    private CheckBox chkFilterInfo = null!;         // ⓘ Info 表示 ON/OFF（Stage 2）
    private ListView lvWarnings = null!;

    // ───────────── プレビューペイン（常時表示化） ─────────────
    // 中央ペインと右ペインの間に WebBrowser を埋め込み、Draft 編集にリアルタイム追従させる。
    private Panel pnlPreview = null!;
    private Label lblPreviewHeader = null!;
    private WebBrowser webPreview = null!;

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
    private Button btnCreditUp = null!;          // クレジット並べ替え（上へ）
    private Button btnCreditDown = null!;        // クレジット並べ替え（下へ）
    private Button btnNewCredit = null!;        // B-1 では無効、B-2 で有効化
    private Button btnCopyCredit = null!;       // 話数コピー
    // プレビューは常時表示の埋め込みペインで行うため、ボタンによる起動は持たない。

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
    private Button btnBulkInput = null!;

    // ───────────── 中央ペイン：構造ツリー ─────────────
    private Panel pnlCenter = null!;
    private TreeView treeStructure = null!;

    // ───────────── ステータスバー（Stage 1c でフォーム最下段に移設） ─────────────
    // 旧 Label 版（pnlCenter.Top）からフォーム最下段の StatusStrip に変更。
    // ToolStripStatusLabel は Text / BackColor を持つので、本体 cs 側の lblStatusBar.Text /
    // lblStatusBar.BackColor 系の参照はそのまま動く（型を Label → ToolStripStatusLabel に変えただけ）。
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel lblStatusBar = null!;
    private Panel pnlTreeButtons = null!;        // ツリー操作ボタン群、B-1 では全て無効
    private Button btnAddCard = null!;
    private Button btnAddTier = null!;           // 追加
    private Button btnAddGroup = null!;          // 追加
    private Button btnAddRole = null!;
    private Button btnAddBlock = null!;
    private Button btnAddEntry = null!;
    private Button btnMoveUp = null!;
    private Button btnMoveDown = null!;
    private Button btnDeleteNode = null!;
    // で追加：Draft セッションの保存・取消ボタン。
    // 中央ペイン下部のボタン群の 2 段目に配置される。
    private Button btnSaveDraft = null!;
    private Button btnCancelDraft = null!;

    // ───────────── 右ペイン：エントリ編集（UserControl 化） ─────────────
    // エントリ編集は種別ごとの動的編集 UI を持つ EntryEditorPanel UserControl を
    // 1 個だけ Dock=Fill で配置する。エントリ編集モードと新規追加モードはパネル側で管理する。
    // ブロック選択時用の BlockEditorPanel UserControl も同じ pnlRight に
    // 重ねて配置し、選択ノードの種別によって Visible を切り替える。
    // 上位ノード（Card / Tier / Group / Role）選択時用の NodePropertiesEditorPanel も
    // 同じ pnlRight にスタックされる。3 種のエディタはいずれも Dock=Fill で重なっており、
    // OnTreeNodeSelected で Visible を 1 つだけ true にする運用。
    private Panel pnlRight = null!;
    private EntryEditorPanel entryEditor = null!;
    private BlockEditorPanel blockEditor = null!;
    private NodePropertiesEditorPanel nodePropsEditor = null!;

    // ツリー右クリックメニュー。「📝 一括入力で編集...」項目を持ち、
    // 選択ノードの種別に応じてスコープを切り出して CreditBulkInputDialog を ReplaceScope モードで起動する。
    private ContextMenuStrip treeContextMenu = null!;
    private ToolStripMenuItem mnuBulkEditScope = null!;

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
        // Stage 1a で 5 ペイン化（左 + テキスト + プレビュー + ツリー + 警告）に伴いサイズ拡大。
        // 左 320 + テキスト 560 + プレビュー 720 + ツリー 480 + 警告 320 + スプリッタ 4 本 ≒ 2420。
        ClientSize = new Size(2440, 880);
        Name = "CreditEditorForm";
        Text = "クレジット編集";
        StartPosition = FormStartPosition.CenterParent;
        // 最小サイズも 5 ペイン構成に合わせて拡大：左 280 + テキスト 360 + プレビュー 520 + ツリー 320 + 警告 240 ≒ 1740
        MinimumSize = new Size(1760, 700);

        // ============================================================
        // SplitContainer ルート（4 段ネスト化）
        // ============================================================
        // SplitContainer.Panel*MinSize は、SplitContainer 自身の Width / Height が
        // 確定してからでないと安全に反映できない（既定 Width=150 のままで Panel2MinSize=400 等を
        // 設定すると SplitterDistance が負値になり InvalidOperationException）。そのため:
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
        splitText = new SplitContainer
        {
            Dock = DockStyle.Fill,
            // テキスト編集ペインは入力主役なのでまとまった幅を確保したいが、ユーザーが
            // プレビュー / ツリーを優先したい場合の調整も許す。FixedPanel は指定しない。
        };
        splitPreview = new SplitContainer
        {
            Dock = DockStyle.Fill,
            // プレビューは可変、右半（ツリー + 警告）も可変。
        };
        splitTreeWarn = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2   // 警告ペイン幅を固定気味にして、ツリー側を伸ばす
        };

        // ============================================================
        // 各ペインの中身
        // ============================================================
        BuildLeftPane();
        BuildCenterPane();            // 中身は Tree 主体、pnlTreeButtons は内部で Controls.Add しない方針に変更
                                      // ※ btnSaveDraft / btnCancelDraft の new もここで行われるため、これらを参照する BuildTextPane より先に呼ぶ
        BuildTextPane();              // Stage 1a 新設、Stage 1b で btnSaveDraft / btnCancelDraft を pnlTextHeader に移植
        BuildPreviewPane();
        BuildWarningsPane();          // Stage 1a 新設（プレースホルダ）
        BuildLegacyRightInitOnly();   // 旧 entryEditor / blockEditor / nodePropsEditor を初期化だけする（Controls には乗せない、Stage 3 で撤去）

        // 配置：Panel への Add → 親フォームへの Add の順
        splitMain.Panel1.Controls.Add(pnlLeft);
        splitMain.Panel2.Controls.Add(splitText);
        splitText.Panel1.Controls.Add(pnlText);
        splitText.Panel2.Controls.Add(splitPreview);
        splitPreview.Panel1.Controls.Add(pnlPreview);
        splitPreview.Panel2.Controls.Add(splitTreeWarn);
        splitTreeWarn.Panel1.Controls.Add(pnlCenter);
        splitTreeWarn.Panel2.Controls.Add(pnlWarnings);

        // ── ステータスバー（フォーム最下段、Stage 1c で移設） ──
        // 「現在編集中: ...」とパースエラー表示をフォーム下端の StatusStrip に集約する。
        // Controls.Add の順序：先に StatusStrip（Dock=Bottom）→ 後で splitMain（Dock=Fill）
        // とすることで、splitMain が StatusStrip の上に収まる正しい Z-order になる。
        BuildStatusBar();
        Controls.Add(statusStrip);
        Controls.Add(splitMain);

        // ここで各 SplitContainer の Width が ClientSize に追従して確定するので、
        // PerformLayout でレイアウトを強制実行してから Panel*MinSize を安全に設定できる。
        ResumeLayout(performLayout: false);
        PerformLayout();

        // Panel*MinSize 設定（Width 確定後に行うことで例外を防ぐ）
        // 5 ペイン構成：左 280、テキスト 360、プレビュー 520、ツリー 320、警告 240 を最小として確保。
        splitMain.Panel1MinSize = 280;
        splitMain.Panel2MinSize = 1440;          // テキスト 360 + プレビュー 520 + ツリー 320 + 警告 240 ≒ 1440
        splitText.Panel1MinSize = 360;           // テキスト編集の最小幅
        splitText.Panel2MinSize = 1080;          // プレビュー 520 + ツリー 320 + 警告 240 ≒ 1080
        splitPreview.Panel1MinSize = 520;        // プレビューペイン最小
        splitPreview.Panel2MinSize = 560;        // ツリー 320 + 警告 240 ≒ 560
        splitTreeWarn.Panel1MinSize = 320;       // ツリーペイン最小
        splitTreeWarn.Panel2MinSize = 240;       // 警告ペイン最小
    }

    // ============================================================
    // 左ペイン
    // ============================================================
    private void BuildLeftPane()
    {
        pnlLeft = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // ── スコープ＋クレジット選択 ──
        // HTML プレビューは常時表示の埋め込みペインに移行したため、
        // grpScope の高さは 360、grpCreditProps の開始位置は
        // 368。ボタンは「新規クレジット」「話数コピー」の 2 個のみ。
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

        // 再修正：本放送限定はクレジット単位ではなくエントリ単位で扱うため、
        // 左ペインに「本放送限定行も表示」チェックボックスは持たない。クレジット ListBox は
        // 常に scope_kind と series_id / episode_id だけで絞り込む。

        lblCreditList = new Label { Text = "クレジット", Location = new Point(12, 170), Size = new Size(80, 20) };
        lstCredits = new ListBox
        {
            Location = new Point(12, 192),
            Size = new Size(248, 122),
            // 右端には ↑↓ 並べ替えボタンを置くため、リストは Right アンカーを
            // 付けず固定幅にする（Right を付けると GroupBox 幅に追従して
            // 伸び、ボタンを覆い隠してしまう）。縦方向のみ追従。
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        // クレジット並べ替え（明示順序 credit_seq を ↑↓ で入れ替える）。
        // クレジット階層下位（カード/役職/ブロック）の TreeView 並べ替えと
        // 同じ操作感を、最上位の credits にも与える。リスト右脇に縦並び。
        // リストが固定幅なのでボタンも Left アンカーで隣に固定表示する。
        btnCreditUp = new Button
        {
            Text = "↑",
            Location = new Point(264, 192),
            Size = new Size(28, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Enabled = false
        };
        btnCreditDown = new Button
        {
            Text = "↓",
            Location = new Point(264, 224),
            Size = new Size(28, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Enabled = false
        };
        btnNewCredit = new Button
        {
            Text = "新規クレジット...",
            Location = new Point(12, 322),
            Size = new Size(140, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false   // B-2 で有効化
        };
        // クレジット話数コピーボタン。
        // 現在選択中のクレジットを別シリーズ／別エピソードへ丸ごと複製するための起動口。
        btnCopyCredit = new Button
        {
            Text = "📋 話数コピー...",
            Location = new Point(160, 322),
            Size = new Size(132, 26),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Enabled = false
        };

        grpScope.Controls.AddRange(new Control[]
        {
            lblScopeKind, rbScopeSeries, rbScopeEpisode,
            lblSeries, cboSeries, lblEpisode, cboEpisode,
            lblCreditList, lstCredits, btnCreditUp, btnCreditDown,
            btnNewCredit, btnCopyCredit
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
            // grpCreditProps が Anchor=Top|Bottom で縦に伸びるため、
            // ボタンを Bottom 基準にすると画面下端に追いやられて見えなくなる。Top 基準に固定して
            // 「備考」テキストエリア直下（Y=220）に常駐させる。
            Location = new Point(12, 220),
            Size = new Size(140, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        btnDeleteCredit = new Button
        {
            Text = "クレジット削除",
            Location = new Point(160, 220),
            Size = new Size(132, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        // クレジット一括入力ダイアログを開くボタン。
        // 選択中のクレジットに対して、テキストでまとめて役職／エントリ群を流し込む用途。
        btnBulkInput = new Button
        {
            Text = "📝 クレジット一括入力...",
            Location = new Point(12, 252),
            Size = new Size(280, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Enabled = false   // クレジット選択後に有効化
        };

        grpCreditProps.Controls.AddRange(new Control[]
        {
            lblPresentation, rbPresentationCards, rbPresentationRoll,
            lblPartType, cboPartType,
            lblCreditNotes, txtCreditNotes,
            btnSaveCreditProps, btnDeleteCredit,
            btnBulkInput
        });

        pnlLeft.Controls.AddRange(new Control[] { grpScope, grpCreditProps });
    }

    // ============================================================
    // 中央ペイン
    // ============================================================
    private void BuildCenterPane()
    {
        pnlCenter = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // 旧：ここに lblStatusBar (Label) を BuildCenterPane 内で初期化していたが、Stage 1c で
        // フォーム最下段の StatusStrip + ToolStripStatusLabel に移設したため撤去。
        // 実体の初期化は InitializeComponent 末尾の BuildStatusBar() で行う。

        treeStructure = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowPlusMinus = true,
            ShowRootLines = true,
            // TreeView ドラッグ＆ドロップ対応。
            // 同階層内のみドロップ許可は本体側の DragOver/DragDrop イベントで判定する。
            AllowDrop = true
        };

        // ツリー右クリックメニュー。
        // 単一項目「📝 一括入力で編集...」のみのシンプルな構成。
        // 表示時の有効/無効判定は CreditEditorForm.cs 側の Opening ハンドラで行い、
        // 選択ノードが対応スコープ（クレジット直下 / Card / Tier / Group / CardRole）の場合のみ有効化する。
        treeContextMenu = new ContextMenuStrip();
        mnuBulkEditScope = new ToolStripMenuItem
        {
            Text = "📝 一括入力で編集...",
            ToolTipText = "選択中のスコープ（クレジット全体／カード／ティア／グループ／役職）の中身を一括入力フォーマットで書き換えます。",
        };
        treeContextMenu.Items.Add(mnuBulkEditScope);
        treeStructure.ContextMenuStrip = treeContextMenu;

        pnlTreeButtons = new Panel
        {
            Dock = DockStyle.Bottom,
            // で 40 → 72 に拡大（2 段目の保存・取消ボタン用スペース）。
            Height = 72,
            BorderStyle = BorderStyle.FixedSingle
        };
        btnAddCard   = new Button { Text = "+ カード",     Location = new Point(8,   6), Size = new Size(80, 26), Enabled = false };
        // 「+ Tier」「+ Group」ボタン。
        // Card 選択時に新 Tier、Card / Tier / Group / Role 選択時に新 Group を作る。
        // どちらもブランクで作られ、Tier 作成時には Group 1 が、Group 作成時には空のままで生やす。
        btnAddTier   = new Button { Text = "+ Tier",       Location = new Point(92,  6), Size = new Size(70, 26), Enabled = false };
        btnAddGroup  = new Button { Text = "+ Group",      Location = new Point(166, 6), Size = new Size(74, 26), Enabled = false };
        btnAddRole   = new Button { Text = "+ 役職",       Location = new Point(244, 6), Size = new Size(80, 26), Enabled = false };
        btnAddBlock  = new Button { Text = "+ ブロック",   Location = new Point(328, 6), Size = new Size(90, 26), Enabled = false };
        btnAddEntry  = new Button { Text = "+ エントリ",   Location = new Point(422, 6), Size = new Size(90, 26), Enabled = false };
        btnMoveUp    = new Button { Text = "↑ 上へ",        Location = new Point(522, 6), Size = new Size(64, 26), Enabled = false };
        btnMoveDown  = new Button { Text = "↓ 下へ",        Location = new Point(590, 6), Size = new Size(64, 26), Enabled = false };
        btnDeleteNode = new Button { Text = "× 削除",       Location = new Point(658, 6), Size = new Size(64, 26), Enabled = false };

        // で追加：Draft セッションの保存・取消ボタン。
        // 中央ペインのツリー操作ボタンとは別系統の操作（Draft 全体の確定 / 破棄）なので、
        // 2 段目に視覚的に分離して配置する。保存ボタンは未保存変更があるときのみ Enabled。
        btnSaveDraft   = new Button { Text = "💾 保存", Location = new Point(8,  38), Size = new Size(120, 26), Enabled = false };
        btnCancelDraft = new Button { Text = "✖ 取消", Location = new Point(132, 38), Size = new Size(120, 26), Enabled = false };

        pnlTreeButtons.Controls.AddRange(new Control[]
        {
            btnAddCard, btnAddTier, btnAddGroup, btnAddRole, btnAddBlock, btnAddEntry,
            btnMoveUp, btnMoveDown, btnDeleteNode,
            btnSaveDraft, btnCancelDraft
        });

        // Stage 1a: ツリーは表示専用化のため pnlTreeButtons は中央ペインに置かない。
        // Stage 1c: ステータスバーもフォーム最下段の StatusStrip に移したため pnlCenter.Top には乗らない。
        //   pnlCenter.Controls.Add(pnlTreeButtons);   // ← Stage 1a で外した
        //   pnlCenter.Controls.Add(lblStatusBar);     // ← Stage 1c で外した
        // Tree だけを残す（ツリーペインは純粋にツリー表示専用）。
        pnlCenter.Controls.Add(treeStructure);    // Fill
    }

    // ============================================================
    // テキスト編集ペイン（Stage 1a 新設）
    // ============================================================
    /// <summary>クレジット 1 件をテキスト書式で表示・編集する SSoT ペイン。
    /// 上部に「見出し + 保存ボタン + 取消ボタン」のツールバーを置き、下に編集 TextBox を Dock=Fill で配置する。
    /// 保存・取消ボタンは旧 pnlTreeButtons から移動してきたもの（イベント結線は本体 cs 側で既に張ってある）。</summary>
    private void BuildTextPane()
    {
        pnlText = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // ── 上部ツールバー：見出し（左寄せ） + 保存・取消（右寄せ） ──
        pnlTextHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30
        };

        lblTextHeader = new Label
        {
            Text = "📝 テキスト編集",
            Dock = DockStyle.Left,
            Width = 160,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold)
        };

        // 保存・取消ボタンは Stage 1a で pnlTreeButtons ごと画面から外したものを、ここに移植する。
        // Dock=Right はあとに Add した順で内側（左寄り）に重なる。
        // → btnCancelDraft を先に Add（右端）、btnSaveDraft を後に Add（その左隣）。
        // ボタンの実体（new）は旧 BuildCenterPane で既に行われている。
        btnCancelDraft.Dock = DockStyle.Right;
        btnCancelDraft.Width = 100;
        btnCancelDraft.Location = default; // Dock 配置なので Location は無視される
        btnSaveDraft.Dock = DockStyle.Right;
        btnSaveDraft.Width = 110;
        btnSaveDraft.Location = default;

        pnlTextHeader.Controls.Add(btnCancelDraft);
        pnlTextHeader.Controls.Add(btnSaveDraft);
        pnlTextHeader.Controls.Add(lblTextHeader);

        txtBulkText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            AcceptsTab = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 10F),
            // Stage 1b で初期化処理が接続され、クレジット選択時に Encoder 経由でテキストが流し込まれる。
            // 起動直後はクレジット未選択なので空文字。
            Text = ""
        };

        // Dock=Fill の TextBox を先に Add、次に Dock=Top のヘッダを Add する順序で、
        // TextBox がヘッダの下から始まるレイアウトになる（WinForms の Z-order 仕様）。
        pnlText.Controls.Add(txtBulkText);
        pnlText.Controls.Add(pnlTextHeader);
    }

    // ============================================================
    // 警告ペイン（Stage 1a プレースホルダ、Stage 1c で本実装）
    // ============================================================
    /// <summary>パース警告と誤字候補警告を一覧表示するペイン。
    /// Stage 1c で ListView による 3 列詳細表示、Stage 2 で件数バッジ / フィルタ / 重複グルーピング /
    /// クリック→該当行ジャンプ を追加。</summary>
    private void BuildWarningsPane()
    {
        pnlWarnings = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // ── 上部ヘッダ：見出し（左寄せ） + 重要度フィルタチェック 3 個（右寄せ） ──
        pnlWarningsHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30
        };

        lblWarningsHeader = new Label
        {
            // Stage 2: 件数バッジを末尾に付けて表示する。空のうちは「⚠ 警告」のまま。
            Text = "⚠ 警告",
            Dock = DockStyle.Left,
            Width = 120,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold)
        };

        // フィルタチェック：Block / Warning / Info の 3 個。既定は全 ON。
        // Dock=Right は「あとに Add したものが内側（左寄り）」になる仕様なので、
        // 見た目の左→右の並びを ⚠ Warning / ⓘ Info / 🔥 Block にしたい場合は、
        // 内側から外側へ：Info → Warning → Block の順に Add する。
        chkFilterInfo = new CheckBox
        {
            Text = "ⓘ",
            Dock = DockStyle.Right,
            Width = 44,
            Checked = true,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Standard
        };
        chkFilterWarning = new CheckBox
        {
            Text = "⚠",
            Dock = DockStyle.Right,
            Width = 44,
            Checked = true,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Standard
        };
        chkFilterBlock = new CheckBox
        {
            Text = "🔥",
            Dock = DockStyle.Right,
            Width = 44,
            Checked = true,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Standard
        };

        pnlWarningsHeader.Controls.Add(chkFilterInfo);
        pnlWarningsHeader.Controls.Add(chkFilterWarning);
        pnlWarningsHeader.Controls.Add(chkFilterBlock);
        pnlWarningsHeader.Controls.Add(lblWarningsHeader);

        lvWarnings = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            GridLines = false,
            MultiSelect = false,
            ShowItemToolTips = true,
        };
        // 列構成：行番号 / 重要度 / メッセージ
        lvWarnings.Columns.Add("行", 48, HorizontalAlignment.Right);
        lvWarnings.Columns.Add("種別", 56, HorizontalAlignment.Center);
        lvWarnings.Columns.Add("メッセージ", 420, HorizontalAlignment.Left);

        pnlWarnings.Controls.Add(lvWarnings);
        pnlWarnings.Controls.Add(pnlWarningsHeader);
    }

    // ============================================================
    // ステータスバー（Stage 1c で BuildCenterPane から独立、フォーム最下段に配置）
    // ============================================================
    /// <summary>フォーム最下段の StatusStrip を構築する。<see cref="lblStatusBar"/>（ToolStripStatusLabel）が
    /// 「現在編集中: シリーズ X / クレジット種別 (CARDS) ★ 未保存の変更あり ⚠ パースエラー: …」を
    /// 全部 1 つに連結して表示する。本体 cs 側からは旧 Label 時代と同じ <c>lblStatusBar.Text</c> /
    /// <c>lblStatusBar.BackColor</c> 経由で更新する（API シグネチャ互換）。</summary>
    private void BuildStatusBar()
    {
        statusStrip = new StatusStrip
        {
            Dock = DockStyle.Bottom,
            // SizingGrip は MDI 風の右下リサイズグリップ。常時表示フォームでは不要なので隠す。
            SizingGrip = false,
            BackColor = SystemColors.Info
        };

        lblStatusBar = new ToolStripStatusLabel
        {
            Text = "現在編集中: （クレジット未選択）",
            // Spring=true で残り幅をすべて占有 → 1 個のラベルでフォーム幅を使い切る。
            Spring = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            BackColor = SystemColors.Info,
            // ToolStripItem には BorderStyle がない。StatusStrip 上端の境界線で十分視認できる。
        };

        statusStrip.Items.Add(lblStatusBar);
    }

    // ============================================================
    // 旧右ペインの初期化のみ（Stage 1a で画面から外し、Stage 3 で撤去予定）
    // ============================================================
    /// <summary>旧 4 ペイン時代の <c>EntryEditorPanel</c> / <c>BlockEditorPanel</c> / <c>NodePropertiesEditorPanel</c> を
    /// new するだけのメソッド。Controls には乗せない（画面に出ない）。
    /// 本体 cs 側がこれらのフィールドを参照する箇所（Initialize, SetSession, ClearAndDisable 等）が
    /// まだ残っているため null 参照を避けるための過渡期措置。Stage 3 で関連コードと一緒に撤去する。</summary>
    private void BuildLegacyRightInitOnly()
    {
        pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        entryEditor = new EntryEditorPanel { Dock = DockStyle.Fill, Visible = false };
        blockEditor = new BlockEditorPanel { Dock = DockStyle.Fill, Visible = false };
        nodePropsEditor = new NodePropertiesEditorPanel { Dock = DockStyle.Fill, Visible = false };

        // Stage 1a: 旧 pnlRight は画面に出さない（splitMain.Panel2 が新 splitText を持つため、
        // pnlRight も entryEditor 等もどの SplitContainer.Panel にも乗らない）。
        // フィールドだけ初期化されたまま放置し、本体 cs からの参照経路は維持する。
    }

    // ============================================================
    // 右ペイン
    // ============================================================
    private void BuildRightPane()
    {
        pnlRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        // 導入：エントリ編集 UI を持つ専用 UserControl。
        // CreditEditorForm 側からは Initialize / LoadForEditAsync / LoadForNew / ClearAndDisable
        // を呼び、保存・削除・追加完了は EntrySaved / EntryDeleted イベント経由で受け取る。
        entryEditor = new EntryEditorPanel
        {
            Dock = DockStyle.Fill,
            Visible = true
        };

        // 導入：ブロックプロパティ編集 UI を持つ専用 UserControl。
        // entryEditor と同じ pnlRight にスタックされ、選択ノード種別によって Visible が
        // トグルされる。両者は同時には表示されない（どちらかだけ表示、またはどちらも非表示）。
        blockEditor = new BlockEditorPanel
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        // Card / Tier / Group / Role 選択時の Notes 編集 UserControl。
        // entryEditor / blockEditor と同じ pnlRight にスタックされ、選択ノード種別に応じて
        // Visible を切り替える。3 つのうち最も「上位スコープ」を扱うので最後に Add し、
        // ZOrder 上は前面寄りに置く（pnlRight 上に Visible=true で乗せたとき他のエディタを覆う）。
        nodePropsEditor = new NodePropertiesEditorPanel
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        pnlRight.Controls.Add(nodePropsEditor);
        pnlRight.Controls.Add(blockEditor);
        pnlRight.Controls.Add(entryEditor);
    }

    // ============================================================
    // プレビューペイン（常時表示化）
    // ============================================================
    /// <summary>
    /// 中央ペインと右ペインの間に挟まる常時表示プレビューペイン。
    /// <para>
    /// 上部に小さな見出しラベル「🌐 ライブプレビュー」、その下に <see cref="WebBrowser"/> を Dock=Fill で
    /// 配置するシンプルな構成。クレジット切替・Draft 編集・保存・取消のたびに本体 cs 側の
    /// <c>RefreshPreviewAsync</c> から再描画される。
    /// </para>
    /// <para>
    /// レイアウト方針：「ボタンや WebBrowser を枠ギリギリに寄せない」というユーザーフィードバックを反映し、
    /// パネル全体に <c>Padding = 8</c>、ヘッダ Label と WebBrowser の間にも縦 4px の余白を確保している。
    /// </para>
    /// </summary>
    private void BuildPreviewPane()
    {
        pnlPreview = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        lblPreviewHeader = new Label
        {
            Text = "🌐 ライブプレビュー",
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold),
            // ヘッダと WebBrowser の間に薄い境界線を描いて区切る
            BorderStyle = BorderStyle.None
        };

        webPreview = new WebBrowser
        {
            Dock = DockStyle.Fill,
            ScriptErrorsSuppressed = true,
            AllowWebBrowserDrop = false,
            IsWebBrowserContextMenuEnabled = true
        };

        // Dock=Fill の WebBrowser を先に Add、次に Dock=Top のヘッダを Add する順序にすると、
        // WebBrowser がヘッダの下から始まるレイアウトになる（WinForms の Z-order 仕様）。
        pnlPreview.Controls.Add(webPreview);
        pnlPreview.Controls.Add(lblPreviewHeader);
    }
}