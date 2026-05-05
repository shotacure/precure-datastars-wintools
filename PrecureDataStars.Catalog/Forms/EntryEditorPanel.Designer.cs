#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class EntryEditorPanel
{
    private System.ComponentModel.IContainer? components = null;

    // ─── Step 1: 種別ラジオ群 ───
    private GroupBox grpKind = null!;
    private RadioButton rbKindPerson = null!;
    private RadioButton rbKindCharacterVoice = null!;
    private RadioButton rbKindCompany = null!;
    private RadioButton rbKindLogo = null!;
    // rbKindSong は v1.2.0 工程 H で撤去（SONG エントリ種別を物理削除）。
    private RadioButton rbKindText = null!;
    private Label lblKindNotice = null!;

    // ─── Step 2: 種別別パネル群（同位置に配置、Visible で切替） ───
    private Panel pnlKindHost = null!;

    // PERSON
    private Panel pnlPerson = null!;
    private Label lblPersonAliasIdCaption = null!;
    private NumericUpDown numPersonAliasId = null!;
    private Button btnPersonAliasPick = null!;       // B-3b で結線
    private Button btnPersonAliasNew = null!;        // B-3c で結線
    private Label lblPersonPreview = null!;
    private Label lblAffiliationCompanyCaption = null!;
    private NumericUpDown numAffiliationCompanyAliasId = null!;
    private Button btnAffiliationCompanyPick = null!;// B-3b で結線
    private Label lblAffiliationTextCaption = null!;
    private TextBox txtAffiliationText = null!;

    // CHARACTER_VOICE
    private Panel pnlCharacterVoice = null!;
    private Label lblCharacterAliasIdCaption = null!;
    private NumericUpDown numCharacterAliasId = null!;
    private Button btnCharacterAliasPick = null!;    // B-3b で結線
    private Button btnCharacterAliasNew = null!;     // 工程 F で結線
    private Label lblCharacterRawTextCaption = null!;
    private TextBox txtRawCharacterText = null!;
    private Label lblCharacterPreview = null!;
    private Label lblVoicePersonAliasIdCaption = null!;
    private NumericUpDown numVoicePersonAliasId = null!;
    private Button btnVoicePersonAliasPick = null!;  // B-3b で結線
    private Button btnVoicePersonAliasNew = null!;   // B-3c で結線
    private Label lblVoicePreview = null!;

    // COMPANY
    private Panel pnlCompany = null!;
    private Label lblCompanyAliasIdCaption = null!;
    private NumericUpDown numCompanyAliasId = null!;
    private Button btnCompanyAliasPick = null!;      // B-3b で結線
    private Button btnCompanyAliasNew = null!;       // B-3c で結線
    private Label lblCompanyPreview = null!;

    // LOGO
    private Panel pnlLogo = null!;
    private Label lblLogoIdCaption = null!;
    private NumericUpDown numLogoId = null!;
    private Button btnLogoPick = null!;              // B-3b で結線
    private Button btnLogoNew = null!;               // B-3c で結線
    private Label lblLogoPreview = null!;

    // SONG パネルは v1.2.0 工程 H で完全撤去（主題歌は episode_theme_songs から
    // 役職レベルでテンプレ展開する運用に切り替えたため、エントリ単位で持たない）。

    // TEXT
    private Panel pnlText = null!;
    private Label lblRawTextCaption = null!;
    private TextBox txtRawText = null!;

    // ─── 共通属性 ───
    private GroupBox grpCommon = null!;
    private CheckBox chkIsBroadcastOnly = null!;
    private Label lblEntrySeqCaption = null!;
    private NumericUpDown numEntrySeq = null!;
    private Label lblNotesCaption = null!;
    private TextBox txtNotes = null!;

    // ─── アクション ───
    private Button btnSave = null!;
    private Button btnDelete = null!;
    private Label lblStatus = null!;

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
        Size = new Size(360, 600);
        Name = "EntryEditorPanel";

        // ============================================================
        // Step 1: 種別ラジオ
        // ============================================================
        grpKind = new GroupBox
        {
            Text = "Step 1: エントリ種別",
            Location = new Point(4, 4),
            Size = new Size(350, 168),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        rbKindPerson         = new RadioButton { Text = "人物名義 (PERSON)",                    Location = new Point(12,  20), Size = new Size(280, 20), Checked = true };
        rbKindCharacterVoice = new RadioButton { Text = "キャラクター × 声優ペア (CHARACTER_VOICE)", Location = new Point(12,  44), Size = new Size(320, 20) };
        rbKindCompany        = new RadioButton { Text = "企業屋号 (COMPANY)",                    Location = new Point(12,  68), Size = new Size(280, 20) };
        rbKindLogo           = new RadioButton { Text = "ロゴ (LOGO)",                            Location = new Point(12,  92), Size = new Size(280, 20) };
        // SONG ラジオは v1.2.0 工程 H で撤去（物理削除）。テキストの位置を SONG が居た 116 に詰める。
        rbKindText           = new RadioButton { Text = "フリーテキスト (TEXT)",                  Location = new Point(12, 116), Size = new Size(280, 20) };
        lblKindNotice = new Label
        {
            Location = new Point(12, 138),
            Size = new Size(330, 1),
            Text = ""
        };
        grpKind.Controls.AddRange(new Control[]
        {
            rbKindPerson, rbKindCharacterVoice, rbKindCompany, rbKindLogo, rbKindText, lblKindNotice
        });

        // ============================================================
        // Step 2: 種別別パネル群（pnlKindHost が共通の置き場、各 pnlXxx は同位置に重ねて Visible で切替）
        // ============================================================
        pnlKindHost = new Panel
        {
            Location = new Point(4, 178),
            Size = new Size(350, 195),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BorderStyle = BorderStyle.FixedSingle
        };

        // ── PERSON パネル ──
        pnlPerson = new Panel { Dock = DockStyle.Fill, Visible = true };
        lblPersonAliasIdCaption = new Label { Text = "人物名義 ID:", Location = new Point(8, 8), Size = new Size(80, 20) };
        numPersonAliasId  = new NumericUpDown { Location = new Point(92,   6), Size = new Size(80, 23), Maximum = 9_999_999 };
        btnPersonAliasPick = new Button { Text = "検索...", Location = new Point(178,   5), Size = new Size(64, 25), Enabled = false }; // B-3b
        btnPersonAliasNew  = new Button { Text = "+ 新規...", Location = new Point(248,   5), Size = new Size(80, 25), Enabled = false }; // B-3c
        lblPersonPreview = new Label
        {
            Location = new Point(8, 36), Size = new Size(330, 36),
            Text = "(プレビュー: 名義 ID を入力すると表示されます)",
            ForeColor = SystemColors.GrayText
        };
        lblAffiliationCompanyCaption = new Label { Text = "所属屋号 ID:", Location = new Point(8, 80), Size = new Size(80, 20) };
        numAffiliationCompanyAliasId = new NumericUpDown { Location = new Point(92, 78), Size = new Size(80, 23), Maximum = 9_999_999 };
        btnAffiliationCompanyPick    = new Button { Text = "検索...", Location = new Point(178, 77), Size = new Size(64, 25), Enabled = false }; // B-3b
        lblAffiliationTextCaption    = new Label { Text = "または所属テキスト:", Location = new Point(8, 110), Size = new Size(120, 20) };
        txtAffiliationText           = new TextBox { Location = new Point(132, 108), Size = new Size(206, 23) };
        pnlPerson.Controls.AddRange(new Control[]
        {
            lblPersonAliasIdCaption, numPersonAliasId, btnPersonAliasPick, btnPersonAliasNew, lblPersonPreview,
            lblAffiliationCompanyCaption, numAffiliationCompanyAliasId, btnAffiliationCompanyPick,
            lblAffiliationTextCaption, txtAffiliationText
        });

        // ── CHARACTER_VOICE パネル ──
        pnlCharacterVoice = new Panel { Dock = DockStyle.Fill, Visible = false };
        lblCharacterAliasIdCaption = new Label { Text = "キャラ名義 ID:", Location = new Point(8, 8), Size = new Size(95, 20) };
        numCharacterAliasId        = new NumericUpDown { Location = new Point(108, 6), Size = new Size(80, 23), Maximum = 9_999_999 };
        btnCharacterAliasPick      = new Button { Text = "検索...", Location = new Point(194, 5), Size = new Size(64, 25), Enabled = false };
        // 工程 F 追加：「+ 新規キャラ名義...」ボタン。QuickAddCharacterAliasDialog をモード切替で開く。
        btnCharacterAliasNew       = new Button { Text = "+ 新規...", Location = new Point(264, 5), Size = new Size(80, 25), Enabled = false };
        lblCharacterRawTextCaption = new Label { Text = "または直接テキスト:", Location = new Point(8, 36), Size = new Size(120, 20) };
        txtRawCharacterText        = new TextBox { Location = new Point(132, 34), Size = new Size(206, 23) };
        lblCharacterPreview        = new Label
        {
            Location = new Point(8, 60), Size = new Size(330, 20),
            Text = "(キャラ プレビュー)", ForeColor = SystemColors.GrayText
        };
        lblVoicePersonAliasIdCaption = new Label { Text = "声優名義 ID:", Location = new Point(8, 90), Size = new Size(85, 20) };
        numVoicePersonAliasId        = new NumericUpDown { Location = new Point(98, 88), Size = new Size(80, 23), Maximum = 9_999_999 };
        btnVoicePersonAliasPick      = new Button { Text = "検索...", Location = new Point(184, 87), Size = new Size(64, 25), Enabled = false };
        btnVoicePersonAliasNew       = new Button { Text = "+ 新規...", Location = new Point(254, 87), Size = new Size(80, 25), Enabled = false };
        lblVoicePreview              = new Label
        {
            Location = new Point(8, 118), Size = new Size(330, 36),
            Text = "(声優 プレビュー)", ForeColor = SystemColors.GrayText
        };
        pnlCharacterVoice.Controls.AddRange(new Control[]
        {
            lblCharacterAliasIdCaption, numCharacterAliasId, btnCharacterAliasPick, btnCharacterAliasNew,
            lblCharacterRawTextCaption, txtRawCharacterText, lblCharacterPreview,
            lblVoicePersonAliasIdCaption, numVoicePersonAliasId, btnVoicePersonAliasPick, btnVoicePersonAliasNew, lblVoicePreview
        });

        // ── COMPANY パネル ──
        pnlCompany = new Panel { Dock = DockStyle.Fill, Visible = false };
        lblCompanyAliasIdCaption = new Label { Text = "企業屋号 ID:", Location = new Point(8, 8), Size = new Size(80, 20) };
        numCompanyAliasId        = new NumericUpDown { Location = new Point(92, 6), Size = new Size(80, 23), Maximum = 9_999_999 };
        btnCompanyAliasPick      = new Button { Text = "検索...", Location = new Point(178, 5), Size = new Size(64, 25), Enabled = false };
        btnCompanyAliasNew       = new Button { Text = "+ 新規...", Location = new Point(248, 5), Size = new Size(80, 25), Enabled = false };
        lblCompanyPreview        = new Label
        {
            Location = new Point(8, 36), Size = new Size(330, 60),
            Text = "(企業屋号 プレビュー)", ForeColor = SystemColors.GrayText
        };
        pnlCompany.Controls.AddRange(new Control[]
        {
            lblCompanyAliasIdCaption, numCompanyAliasId, btnCompanyAliasPick, btnCompanyAliasNew, lblCompanyPreview
        });

        // ── LOGO パネル ──
        pnlLogo = new Panel { Dock = DockStyle.Fill, Visible = false };
        lblLogoIdCaption = new Label { Text = "ロゴ ID:", Location = new Point(8, 8), Size = new Size(60, 20) };
        numLogoId        = new NumericUpDown { Location = new Point(72, 6), Size = new Size(80, 23), Maximum = 9_999_999 };
        btnLogoPick      = new Button { Text = "検索...", Location = new Point(158, 5), Size = new Size(64, 25), Enabled = false };
        btnLogoNew       = new Button { Text = "+ 新規...", Location = new Point(228, 5), Size = new Size(80, 25), Enabled = false };
        lblLogoPreview   = new Label
        {
            Location = new Point(8, 36), Size = new Size(330, 60),
            Text = "(ロゴ プレビュー)", ForeColor = SystemColors.GrayText
        };
        pnlLogo.Controls.AddRange(new Control[]
        {
            lblLogoIdCaption, numLogoId, btnLogoPick, btnLogoNew, lblLogoPreview
        });

        // SONG パネルは v1.2.0 工程 H で完全撤去（クレジットでは楽曲を持たず、
        // 主題歌役職の表示時に episode_theme_songs から動的に取得する運用へ）。

        // ── TEXT パネル ──
        pnlText = new Panel { Dock = DockStyle.Fill, Visible = false };
        lblRawTextCaption = new Label
        {
            Text = "テキスト（マスタに紐付かない自由文）:",
            Location = new Point(8, 8), Size = new Size(330, 20)
        };
        txtRawText = new TextBox
        {
            Location = new Point(8, 32), Size = new Size(330, 130),
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        pnlText.Controls.AddRange(new Control[] { lblRawTextCaption, txtRawText });

        pnlKindHost.Controls.AddRange(new Control[]
        {
            pnlPerson, pnlCharacterVoice, pnlCompany, pnlLogo, pnlText
        });

        // ============================================================
        // 共通属性
        // ============================================================
        grpCommon = new GroupBox
        {
            Text = "共通属性",
            Location = new Point(4, 380),
            Size = new Size(350, 130),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        chkIsBroadcastOnly = new CheckBox
        {
            Text = "本放送限定エントリ（既定 OFF = 円盤・配信用）",
            Location = new Point(12, 22),
            Size = new Size(330, 22)
        };
        lblEntrySeqCaption = new Label { Text = "ブロック内表示順 (entry_seq):", Location = new Point(12, 50), Size = new Size(170, 20) };
        numEntrySeq        = new NumericUpDown { Location = new Point(186, 48), Size = new Size(70, 23), Minimum = 1, Maximum = 65_000, Value = 1 };
        lblNotesCaption    = new Label { Text = "備考:", Location = new Point(12, 78), Size = new Size(40, 20) };
        txtNotes           = new TextBox
        {
            Location = new Point(56, 76),
            Size = new Size(286, 46),
            Multiline = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        grpCommon.Controls.AddRange(new Control[]
        {
            chkIsBroadcastOnly, lblEntrySeqCaption, numEntrySeq, lblNotesCaption, txtNotes
        });

        // ============================================================
        // アクションボタン群
        // ============================================================
        btnSave = new Button
        {
            Text = "保存",
            Location = new Point(4, 520),
            Size = new Size(100, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Enabled = false
        };
        btnDelete = new Button
        {
            Text = "削除",
            Location = new Point(110, 520),
            Size = new Size(100, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Enabled = false
        };
        lblStatus = new Label
        {
            Location = new Point(4, 558),
            Size = new Size(350, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = "(ツリーで Block を選んで「+ エントリ」、または既存 Entry を選択してください)",
            ForeColor = SystemColors.GrayText
        };

        Controls.AddRange(new Control[]
        {
            grpKind, pnlKindHost, grpCommon, btnSave, btnDelete, lblStatus
        });

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}
