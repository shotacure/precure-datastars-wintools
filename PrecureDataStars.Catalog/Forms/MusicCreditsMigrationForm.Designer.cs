
#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class MusicCreditsMigrationForm
{
    private System.ComponentModel.IContainer? components = null;

    // ─── 上部ペイン：検索条件 ───
    private GroupBox grpCriteria = null!;
    private Label lblAlias = null!;
    private TextBox txtAliasDisplay = null!;
    private Button btnPickAlias = null!;
    private Label lblAliasIdCaption = null!;
    private Label lblAliasIdValue = null!;

    // ─── v1.3.0 ブラッシュアップ stage 16 Phase 3：未マッチング名義からの新規 alias 登録 UI ───
    // 3 棚（song_recordings.singer_name 等）に出てくるテキストのうち、
    // person_aliases に未登録のものをドロップダウンで選んで「新規登録...」ボタンで
    // person_alias 作成ダイアログを開けるようにする。これで運用の入口を自動化する。
    private Label lblUnmatched = null!;
    private ComboBox cboUnmatched = null!;
    private Button btnRegisterFromUnmatched = null!;
    private Button btnReloadUnmatched = null!;

    private Label lblSeries = null!;
    private ComboBox cboSeriesFilter = null!;

    private Label lblScopeColumns = null!;
    private CheckBox chkLyricist = null!;
    private CheckBox chkComposer = null!;
    private CheckBox chkArranger = null!;
    private CheckBox chkSinger = null!;
    private CheckBox chkBgmComposer = null!;
    private CheckBox chkBgmArranger = null!;

    private Button btnSearch = null!;

    // ─── 中央ペイン：検索結果グリッド ───
    private DataGridView gridMatches = null!;

    // ─── 下部ペイン：操作ボタン ───
    private Button btnSelectAll = null!;
    private Button btnDeselectAll = null!;
    private Button btnMigrate = null!;
    private Label lblStatus = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        // フォーム全体
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 720);
        Name = "MusicCreditsMigrationForm";
        Text = "音楽クレジット名寄せ移行 (v1.2.3)";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1100, 600);

        // ─── 上部：grpCriteria（条件指定 GroupBox）───
        // v1.3.0 ブラッシュアップ stage 16 Phase 3：未マッチング名義行を 1 段追加するため
        // 高さを 152 → 184 に拡張。下段の gridMatches と操作ボタンも 32px ずつ下に移動する。
        grpCriteria = new GroupBox
        {
            Text = "移行対象の条件",
            Location = new Point(8, 8),
            Size = new Size(1264, 184),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // ─── v1.3.0 ブラッシュアップ stage 16 Phase 3：未マッチング名義ドロップダウン行（Y=24）───
        // 既存の「対象名義」行は Y=56 に下げ、上段に未マッチング名義行を新設する。
        lblUnmatched = new Label
        {
            Text = "未マッチング:",
            Location = new Point(12, 28),
            AutoSize = true
        };
        cboUnmatched = new ComboBox
        {
            Location = new Point(96, 24),
            Size = new Size(440, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            // 候補件数が多くなるためドロップダウン高さを広めに。
            IntegralHeight = false,
            DropDownHeight = 320
            // AutoCompleteMode / AutoCompleteSource は DropDownList と組み合わせ不可
            // （NotSupportedException で実行時に弾かれる）。DropDownList では文字キー押下で
            // 先頭一致のリストジャンプが標準動作するため、AutoComplete を有効化する必要は無い。
        };
        btnRegisterFromUnmatched = new Button
        {
            Text = "選択中の名義を新規 alias として登録...",
            Location = new Point(544, 23),
            Size = new Size(260, 25)
        };
        btnReloadUnmatched = new Button
        {
            Text = "再読込",
            Location = new Point(812, 23),
            Size = new Size(64, 25)
        };

        lblAlias = new Label
        {
            Text = "対象名義:",
            Location = new Point(12, 60),
            AutoSize = true
        };
        txtAliasDisplay = new TextBox
        {
            Location = new Point(96, 56),
            Size = new Size(360, 23),
            ReadOnly = true,
            BackColor = SystemColors.Window
        };
        btnPickAlias = new Button
        {
            Text = "選択...",
            Location = new Point(464, 55),
            Size = new Size(72, 25)
        };
        lblAliasIdCaption = new Label
        {
            Text = "alias_id:",
            Location = new Point(548, 60),
            AutoSize = true
        };
        lblAliasIdValue = new Label
        {
            Text = "(未選択)",
            Location = new Point(608, 60),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        lblSeries = new Label
        {
            Text = "シリーズ:",
            Location = new Point(12, 92),
            AutoSize = true
        };
        cboSeriesFilter = new ComboBox
        {
            Location = new Point(96, 88),
            Size = new Size(300, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        lblScopeColumns = new Label
        {
            Text = "対象列:",
            Location = new Point(12, 124),
            AutoSize = true
        };
        chkLyricist     = new CheckBox { Text = "歌:作詞",       Location = new Point(96,  122), AutoSize = true, Checked = true };
        chkComposer     = new CheckBox { Text = "歌:作曲",       Location = new Point(180, 122), AutoSize = true, Checked = true };
        chkArranger     = new CheckBox { Text = "歌:編曲",       Location = new Point(264, 122), AutoSize = true, Checked = true };
        chkSinger       = new CheckBox { Text = "歌:歌唱",       Location = new Point(348, 122), AutoSize = true, Checked = true };
        chkBgmComposer  = new CheckBox { Text = "BGM:作曲",      Location = new Point(432, 122), AutoSize = true, Checked = true };
        chkBgmArranger  = new CheckBox { Text = "BGM:編曲",      Location = new Point(528, 122), AutoSize = true, Checked = true };

        btnSearch = new Button
        {
            Text = "exact match で検索",
            Location = new Point(12, 148),
            Size = new Size(180, 28)
        };

        grpCriteria.Controls.AddRange(new Control[]
        {
            // 新規追加：未マッチング名義行
            lblUnmatched, cboUnmatched, btnRegisterFromUnmatched, btnReloadUnmatched,
            // 既存：対象名義 / シリーズ / 対象列 / 検索
            lblAlias, txtAliasDisplay, btnPickAlias, lblAliasIdCaption, lblAliasIdValue,
            lblSeries, cboSeriesFilter,
            lblScopeColumns, chkLyricist, chkComposer, chkArranger, chkSinger, chkBgmComposer, chkBgmArranger,
            btnSearch
        });

        // ─── 中央：gridMatches（検索結果）───
        // grpCriteria が 1 行増えた分（32px）下げる：168 → 200。
        gridMatches = new DataGridView
        {
            Location = new Point(8, 200),
            Size = new Size(1264, 472),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        gridMatches.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewCheckBoxColumn { Name = "colChecked",  HeaderText = "✓",       Width = 40,  DataPropertyName = "Checked" },
            new DataGridViewTextBoxColumn  { Name = "colKind",     HeaderText = "種別",     Width = 110, DataPropertyName = "KindLabel", ReadOnly = true },
            new DataGridViewTextBoxColumn  { Name = "colId",       HeaderText = "ID",       Width = 80,  DataPropertyName = "IdDisplay", ReadOnly = true },
            new DataGridViewTextBoxColumn  { Name = "colTitle",    HeaderText = "タイトル", Width = 360, DataPropertyName = "Title",     ReadOnly = true },
            new DataGridViewTextBoxColumn  { Name = "colColumn",   HeaderText = "列",       Width = 80,  DataPropertyName = "ColumnLabel", ReadOnly = true },
            new DataGridViewTextBoxColumn  { Name = "colCurrent",  HeaderText = "現フリーテキスト", Width = 240, DataPropertyName = "CurrentText", ReadOnly = true },
            new DataGridViewTextBoxColumn  { Name = "colAfter",    HeaderText = "移行後表示",       Width = 240, DataPropertyName = "AfterText",   ReadOnly = true }
        });

        // ─── 下部ボタン群 ───
        btnSelectAll = new Button
        {
            Text = "全選択",
            Location = new Point(8, 684),
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        btnDeselectAll = new Button
        {
            Text = "全解除",
            Location = new Point(96, 684),
            Size = new Size(80, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        btnMigrate = new Button
        {
            Text = "選択行を構造化テーブルに反映",
            Location = new Point(184, 684),
            Size = new Size(240, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        lblStatus = new Label
        {
            Text = "対象名義を選択し、対象列にチェックを入れて「検索」してください。",
            Location = new Point(432, 690),
            Size = new Size(840, 22),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AutoEllipsis = true,
            ForeColor = Color.DimGray
        };

        Controls.Add(grpCriteria);
        Controls.Add(gridMatches);
        Controls.Add(btnSelectAll);
        Controls.Add(btnDeselectAll);
        Controls.Add(btnMigrate);
        Controls.Add(lblStatus);

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}
