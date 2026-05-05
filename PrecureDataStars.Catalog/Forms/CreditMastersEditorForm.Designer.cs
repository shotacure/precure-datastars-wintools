#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット系マスタ管理フォーム（v1.2.0 新設）。
/// <para>
/// 13 タブ構成: 人物 / 人物名義 / 企業 / 企業屋号 / ロゴ / キャラクター / キャラクター名義 /
/// 声優キャスティング / 役職 / シリーズ書式上書き / エピソード主題歌 /
/// シリーズ種別（v1.2.0 列追加）/ パート種別（v1.2.0 列追加）。
/// </para>
/// <para>
/// 本ファイルは Designer 部（コントロール宣言と <see cref="InitializeComponent"/> による
/// レイアウト構築）。実ロジックは <c>CreditMastersEditorForm.cs</c> 側の partial で実装する。
/// クレジット本体（カード／ブロック／エントリ）の編集 UI は v1.2.0 の後続工程で別途追加予定。
/// </para>
/// </summary>
partial class CreditMastersEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private TabControl tabControl = null!;

    // 13 タブ
    private TabPage tabPersons = null!;
    private TabPage tabPersonAliases = null!;        // v1.2.0 工程 A 追加：人物名義（person_aliases + person_alias_persons）
    private TabPage tabCompanies = null!;
    private TabPage tabCompanyAliases = null!;       // v1.2.0 工程 A 追加：企業屋号（company_aliases）
    private TabPage tabLogos = null!;                // v1.2.0 工程 A 追加：ロゴ（logos）
    private TabPage tabCharacters = null!;
    private TabPage tabCharacterAliases = null!;     // v1.2.0 工程 A 追加：キャラクター名義（character_aliases）
    private TabPage tabVoiceCastings = null!;
    private TabPage tabRoles = null!;
    private TabPage tabRoleOverrides = null!;
    private TabPage tabEpisodeThemeSongs = null!;
    private TabPage tabSeriesKinds = null!;
    private TabPage tabPartTypes = null!;

    // ─────────────── 人物タブ ───────────────
    private DataGridView gridPersons = null!;
    private TextBox txtPFamily = null!;
    private TextBox txtPGiven = null!;
    private TextBox txtPFullName = null!;
    private TextBox txtPFullNameKana = null!;
    private TextBox txtPNameEn = null!;
    private TextBox txtPNotes = null!;
    private Button btnNewPerson = null!;
    private Button btnSavePerson = null!;
    private Button btnDeletePerson = null!;

    // ─────────────── 企業タブ ───────────────
    private DataGridView gridCompanies = null!;
    private TextBox txtCName = null!;
    private TextBox txtCNameKana = null!;
    private TextBox txtCNameEn = null!;
    private DateTimePicker dtCFounded = null!;
    private CheckBox chkCFoundedNull = null!;
    private DateTimePicker dtCDissolved = null!;
    private CheckBox chkCDissolvedNull = null!;
    private TextBox txtCNotes = null!;
    private Button btnNewCompany = null!;
    private Button btnSaveCompany = null!;
    private Button btnDeleteCompany = null!;

    // ─────────────── キャラクタータブ ───────────────
    private DataGridView gridCharacters = null!;
    private TextBox txtChName = null!;
    private TextBox txtChNameKana = null!;
    private ComboBox cboChKind = null!;
    private TextBox txtChNotes = null!;
    private Button btnNewCharacter = null!;
    private Button btnSaveCharacter = null!;
    private Button btnDeleteCharacter = null!;

    // ─────────────── 声優キャスティングタブ ───────────────
    private ComboBox cboVcCharacter = null!;
    private DataGridView gridVoiceCastings = null!;
    private NumericUpDown numVcPersonId = null!;
    private Label lblVcPersonName = null!;
    private ComboBox cboVcKind = null!;
    private DateTimePicker dtVcFrom = null!;
    private CheckBox chkVcFromNull = null!;
    private DateTimePicker dtVcTo = null!;
    private CheckBox chkVcToNull = null!;
    private TextBox txtVcNotes = null!;
    private Button btnNewVoiceCasting = null!;
    private Button btnSaveVoiceCasting = null!;
    private Button btnDeleteVoiceCasting = null!;

    // ─────────────── 役職タブ ───────────────
    private DataGridView gridRoles = null!;
    private TextBox txtRoleCode = null!;
    private TextBox txtRoleNameJa = null!;
    private TextBox txtRoleNameEn = null!;
    private ComboBox cboRoleFormatKind = null!;
    private TextBox txtRoleFormatTemplate = null!;
    private NumericUpDown numRoleDisplayOrder = null!;
    private Button btnSaveRole = null!;
    private Button btnDeleteRole = null!;

    // ─────────────── シリーズ書式上書きタブ ───────────────
    private ComboBox cboOvSeries = null!;
    private DataGridView gridRoleOverrides = null!;
    private ComboBox cboOvRole = null!;
    private DateTimePicker dtOvFrom = null!;
    private DateTimePicker dtOvTo = null!;
    private CheckBox chkOvToNull = null!;
    private TextBox txtOvFormatTemplate = null!;
    private TextBox txtOvNotes = null!;
    private Button btnSaveOverride = null!;
    private Button btnDeleteOverride = null!;

    // ─────────────── エピソード主題歌タブ ───────────────
    private ComboBox cboEtsSeries = null!;
    private ComboBox cboEtsEpisode = null!;
    private DataGridView gridEpisodeThemeSongs = null!;
    private ComboBox cboEtsThemeKind = null!;
    private CheckBox chkEtsBroadcastOnly = null!;        // v1.2.0 工程 B' 追加：本放送限定フラグ（true=例外行）
    private NumericUpDown numEtsInsertSeq = null!;
    private NumericUpDown numEtsSongRecordingId = null!;
    // numEtsLabelCompanyAliasId / chkEtsLabelNull は v1.2.0 工程 H 補修で撤去済み
    // （episode_theme_songs.label_company_alias_id 列を物理削除）。
    private TextBox txtEtsNotes = null!;
    private Button btnSaveEts = null!;
    private Button btnDeleteEts = null!;
    private Button btnCopyEts = null!;                    // v1.2.0 工程 B' 追加：他話からコピーダイアログ起動

    // ─────────────── シリーズ種別タブ ───────────────
    private DataGridView gridSeriesKinds = null!;
    private TextBox txtSkCode = null!;
    private TextBox txtSkNameJa = null!;
    private TextBox txtSkNameEn = null!;
    private ComboBox cboSkAttachTo = null!;
    private Button btnSaveSeriesKind = null!;
    private Button btnDeleteSeriesKind = null!;

    // ─────────────── パート種別タブ ───────────────
    private DataGridView gridPartTypes = null!;
    private TextBox txtPtCode = null!;
    private TextBox txtPtNameJa = null!;
    private TextBox txtPtNameEn = null!;
    private NumericUpDown numPtDisplayOrder = null!;
    private ComboBox cboPtDefaultCreditKind = null!;
    private Button btnSavePartType = null!;
    private Button btnDeletePartType = null!;

    // ─────────────── 人物名義タブ（v1.2.0 工程 A 追加） ───────────────
    private ComboBox cboPaPerson = null!;            // 人物選択コンボ
    private DataGridView gridPersonAliases = null!;
    private TextBox txtPaName = null!;
    private TextBox txtPaNameKana = null!;
    private NumericUpDown numPaPredecessor = null!;  // 前任名義 ID（自参照、改名前リンク）
    private NumericUpDown numPaSuccessor = null!;    // 後任名義 ID（自参照、改名後リンク）
    private DateTimePicker dtPaFrom = null!;
    private CheckBox chkPaFromNull = null!;
    private DateTimePicker dtPaTo = null!;
    private CheckBox chkPaToNull = null!;
    private TextBox txtPaNotes = null!;
    // 共同名義人物リスト（中間表 person_alias_persons の編集 UI）
    private ListBox lstPaJointPersons = null!;
    private NumericUpDown numPaJointPersonId = null!;
    private Button btnAddJointPerson = null!;
    private Button btnRemoveJointPerson = null!;
    private Button btnNewPersonAlias = null!;
    private Button btnSavePersonAlias = null!;
    private Button btnDeletePersonAlias = null!;

    // ─────────────── 企業屋号タブ（v1.2.0 工程 A 追加） ───────────────
    private ComboBox cboCaCompany = null!;           // 企業選択コンボ
    private DataGridView gridCompanyAliases = null!;
    private TextBox txtCaName = null!;
    private TextBox txtCaNameKana = null!;
    private NumericUpDown numCaPredecessor = null!;  // 前任屋号 ID（自参照、改名・分社化前リンク）
    private NumericUpDown numCaSuccessor = null!;    // 後任屋号 ID（自参照、改名・分社化後リンク）
    private DateTimePicker dtCaFrom = null!;
    private CheckBox chkCaFromNull = null!;
    private DateTimePicker dtCaTo = null!;
    private CheckBox chkCaToNull = null!;
    private TextBox txtCaNotes = null!;
    private Button btnNewCompanyAlias = null!;
    private Button btnSaveCompanyAlias = null!;
    private Button btnDeleteCompanyAlias = null!;

    // ─────────────── ロゴタブ（v1.2.0 工程 A 追加） ───────────────
    private ComboBox cboLgCompany = null!;           // 企業選択コンボ（屋号コンボの絞り込み元）
    private ComboBox cboLgCompanyAlias = null!;      // 屋号選択コンボ（企業選択に連動）
    private DataGridView gridLogos = null!;
    private TextBox txtLgCiVersion = null!;          // CI バージョンラベル（必須）
    private DateTimePicker dtLgFrom = null!;
    private CheckBox chkLgFromNull = null!;
    private DateTimePicker dtLgTo = null!;
    private CheckBox chkLgToNull = null!;
    private TextBox txtLgDescription = null!;        // 概要説明（255 文字まで）
    private TextBox txtLgNotes = null!;
    private Button btnNewLogo = null!;
    private Button btnSaveLogo = null!;
    private Button btnDeleteLogo = null!;

    // ─────────────── キャラクター名義タブ（v1.2.0 工程 A 追加） ───────────────
    private ComboBox cboCaaCharacter = null!;        // キャラクター選択コンボ
    private DataGridView gridCharacterAliases = null!;
    private TextBox txtCaaName = null!;
    private TextBox txtCaaNameKana = null!;
    private DateTimePicker dtCaaFrom = null!;
    private CheckBox chkCaaFromNull = null!;
    private DateTimePicker dtCaaTo = null!;
    private CheckBox chkCaaToNull = null!;
    private TextBox txtCaaNotes = null!;
    private Button btnNewCharacterAlias = null!;
    private Button btnSaveCharacterAlias = null!;
    private Button btnDeleteCharacterAlias = null!;

    // ─────────────── ピッカー呼び出しボタン（v1.2.0 工程 C 追加） ───────────────
    // 既存の数値直入力欄の隣に「検索...」ボタンを配置し、押下でピッカーダイアログを開く構成。
    // 数値直入力自体も維持しているので、ID が手元にあるなら直入力でも従来通り操作できる。
    private Button btnPickVcPersonId = null!;             // 声優キャスティングタブ：person_id
    private Button btnPickEtsSongRecordingId = null!;     // エピソード主題歌タブ：song_recording_id
    // btnPickEtsLabelCompanyAliasId は v1.2.0 工程 H 補修で撤去済み（label_company_alias_id 列を物理削除）。
    private Button btnPickPaPredecessor = null!;          // 人物名義タブ：predecessor_alias_id
    private Button btnPickPaSuccessor = null!;            // 人物名義タブ：successor_alias_id
    private Button btnPickPaJointPersonId = null!;        // 人物名義タブ：共同名義 person_id
    private Button btnPickCaPredecessor = null!;          // 企業屋号タブ：predecessor_alias_id
    private Button btnPickCaSuccessor = null!;            // 企業屋号タブ：successor_alias_id

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// グリッドの共通設定（読み取り専用・行選択モード・列自動幅・監査列非表示の前提）。
    /// </summary>
    private static void ConfigureListGrid(DataGridView grid)
    {
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
    }

    /// <summary>
    /// ラベル + 入力欄の組を生成して、指定の親パネルに配置するヘルパ。
    /// 入力欄のサイズは呼び出し側で個別に上書き可能。
    /// </summary>
    private static void AddLabeledControl(Panel panel, string labelText, Control input, int x, int y, int labelWidth = 110, int inputWidth = 220)
    {
        var lbl = new Label { Text = labelText, Location = new Point(x, y + 4), Size = new Size(labelWidth, 20) };
        input.Location = new Point(x + labelWidth + 4, y);
        input.Size = new Size(inputWidth, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(input);
    }

    /// <summary>
    /// 「日付 + NULL チェック」のコンボを配置するヘルパ。チェック時はピッカーを無効化する想定で
    /// 本体側で値を読み出す（Enabled の連動は呼び出し側で行う）。
    /// </summary>
    private static void AddDateWithNull(Panel panel, string labelText, DateTimePicker picker, CheckBox nullCheck, int x, int y, int labelWidth = 110)
    {
        var lbl = new Label { Text = labelText, Location = new Point(x, y + 4), Size = new Size(labelWidth, 20) };
        picker.Location = new Point(x + labelWidth + 4, y);
        picker.Size = new Size(150, 23);
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "yyyy-MM-dd";
        nullCheck.Text = "未指定";
        nullCheck.Location = new Point(x + labelWidth + 4 + 156, y + 3);
        nullCheck.Size = new Size(70, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(picker);
        panel.Controls.Add(nullCheck);
    }

    private void InitializeComponent()
    {
        tabControl = new TabControl();
        tabPersons = new TabPage { Text = "人物" };
        tabPersonAliases = new TabPage { Text = "人物名義" };
        tabCompanies = new TabPage { Text = "企業" };
        tabCompanyAliases = new TabPage { Text = "企業屋号" };
        tabLogos = new TabPage { Text = "ロゴ" };
        tabCharacters = new TabPage { Text = "キャラクター" };
        tabCharacterAliases = new TabPage { Text = "キャラクター名義" };
        tabVoiceCastings = new TabPage { Text = "声優キャスティング" };
        tabRoles = new TabPage { Text = "役職" };
        tabRoleOverrides = new TabPage { Text = "シリーズ書式上書き" };
        tabEpisodeThemeSongs = new TabPage { Text = "エピソード主題歌" };
        tabSeriesKinds = new TabPage { Text = "シリーズ種別" };
        tabPartTypes = new TabPage { Text = "パート種別" };

        BuildPersonsTab();
        BuildPersonAliasesTab();
        BuildCompaniesTab();
        BuildCompanyAliasesTab();
        BuildLogosTab();
        BuildCharactersTab();
        BuildCharacterAliasesTab();
        BuildVoiceCastingsTab();
        BuildRolesTab();
        BuildRoleOverridesTab();
        BuildEpisodeThemeSongsTab();
        BuildSeriesKindsTab();
        BuildPartTypesTab();

        tabControl.Dock = DockStyle.Fill;
        // タブ並びは「親マスタ → その名義」を隣接させる構成（v1.2.0 工程 A）。
        // 人物→人物名義、企業→企業屋号→ロゴ、キャラクター→キャラクター名義の順に並ぶ。
        tabControl.TabPages.AddRange(new TabPage[]
        {
            tabPersons, tabPersonAliases,
            tabCompanies, tabCompanyAliases, tabLogos,
            tabCharacters, tabCharacterAliases,
            tabVoiceCastings,
            tabRoles, tabRoleOverrides, tabEpisodeThemeSongs,
            tabSeriesKinds, tabPartTypes
        });

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1100, 720);
        Controls.Add(tabControl);
        Name = "CreditMastersEditorForm";
        Text = "クレジット系マスタ管理 - Catalog";
        StartPosition = FormStartPosition.CenterScreen;
    }

    // ====================================================================
    // 人物タブ
    // ====================================================================
    private void BuildPersonsTab()
    {
        tabPersons.Padding = new Padding(8);
        gridPersons = new DataGridView { Dock = DockStyle.Top, Height = 340 };
        ConfigureListGrid(gridPersons);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtPFamily = new TextBox(); txtPGiven = new TextBox();
        txtPFullName = new TextBox(); txtPFullNameKana = new TextBox();
        txtPNameEn = new TextBox(); txtPNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "姓",            txtPFamily,       18,  18);
        AddLabeledControl(pnl, "名",            txtPGiven,        18,  50);
        AddLabeledControl(pnl, "フルネーム",    txtPFullName,     18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "フルネーム(かな)", txtPFullNameKana, 18, 114, inputWidth: 320);
        AddLabeledControl(pnl, "英語表記",      txtPNameEn,       18, 146, inputWidth: 320);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 182), Size = new Size(110, 20) };
        txtPNotes.Location = new Point(132, 178);
        txtPNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtPNotes);

        btnNewPerson = new Button { Text = "新規",       Location = new Point(620,  18), Size = new Size(140, 28) };
        btnSavePerson = new Button { Text = "保存 / 更新", Location = new Point(620,  50), Size = new Size(140, 28) };
        btnDeletePerson = new Button { Text = "選択行を削除", Location = new Point(620,  82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewPerson, btnSavePerson, btnDeletePerson });

        tabPersons.Controls.Add(pnl);
        tabPersons.Controls.Add(gridPersons);
    }

    // ====================================================================
    // 企業タブ
    // ====================================================================
    private void BuildCompaniesTab()
    {
        tabCompanies.Padding = new Padding(8);
        gridCompanies = new DataGridView { Dock = DockStyle.Top, Height = 340 };
        ConfigureListGrid(gridCompanies);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtCName = new TextBox(); txtCNameKana = new TextBox(); txtCNameEn = new TextBox();
        dtCFounded = new DateTimePicker(); chkCFoundedNull = new CheckBox();
        dtCDissolved = new DateTimePicker(); chkCDissolvedNull = new CheckBox();
        txtCNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "正式名称",     txtCName,     18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "正式名称(かな)", txtCNameKana, 18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "英語表記",     txtCNameEn,   18,  82, inputWidth: 320);
        AddDateWithNull(pnl,  "設立日",        dtCFounded, chkCFoundedNull,   18, 114);
        AddDateWithNull(pnl,  "解散日",        dtCDissolved, chkCDissolvedNull, 18, 146);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 182), Size = new Size(110, 20) };
        txtCNotes.Location = new Point(132, 178);
        txtCNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtCNotes);

        btnNewCompany = new Button { Text = "新規",       Location = new Point(620,  18), Size = new Size(140, 28) };
        btnSaveCompany = new Button { Text = "保存 / 更新", Location = new Point(620,  50), Size = new Size(140, 28) };
        btnDeleteCompany = new Button { Text = "選択行を削除", Location = new Point(620,  82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewCompany, btnSaveCompany, btnDeleteCompany });

        tabCompanies.Controls.Add(pnl);
        tabCompanies.Controls.Add(gridCompanies);
    }

    // ====================================================================
    // キャラクタータブ
    // ====================================================================
    private void BuildCharactersTab()
    {
        tabCharacters.Padding = new Padding(8);
        gridCharacters = new DataGridView { Dock = DockStyle.Top, Height = 340 };
        ConfigureListGrid(gridCharacters);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtChName = new TextBox(); txtChNameKana = new TextBox();
        cboChKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboChKind.Items.AddRange(new object[] { "MAIN", "SUPPORT", "GUEST", "MOB", "OTHER" });
        txtChNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "名前",     txtChName,     18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "名前(かな)", txtChNameKana, 18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "区分",     cboChKind,    18,  82, inputWidth: 160);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 118), Size = new Size(110, 20) };
        txtChNotes.Location = new Point(132, 114);
        txtChNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtChNotes);

        btnNewCharacter = new Button { Text = "新規",       Location = new Point(620,  18), Size = new Size(140, 28) };
        btnSaveCharacter = new Button { Text = "保存 / 更新", Location = new Point(620,  50), Size = new Size(140, 28) };
        btnDeleteCharacter = new Button { Text = "選択行を削除", Location = new Point(620,  82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewCharacter, btnSaveCharacter, btnDeleteCharacter });

        tabCharacters.Controls.Add(pnl);
        tabCharacters.Controls.Add(gridCharacters);
    }

    // ====================================================================
    // 声優キャスティングタブ
    // ====================================================================
    private void BuildVoiceCastingsTab()
    {
        tabVoiceCastings.Padding = new Padding(8);

        var lblChar = new Label { Text = "キャラクター", Location = new Point(18, 22), Size = new Size(100, 20) };
        cboVcCharacter = new ComboBox
        {
            Location = new Point(122, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        gridVoiceCastings = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridVoiceCastings);

        var pnl = new Panel
        {
            Location = new Point(0, 340),
            Size = new Size(1080, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        numVcPersonId = new NumericUpDown { Maximum = 9_999_999 };
        lblVcPersonName = new Label { ForeColor = SystemColors.GrayText, Size = new Size(280, 20) };
        cboVcKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboVcKind.Items.AddRange(new object[] { "REGULAR", "SUBSTITUTE", "TEMPORARY", "MOB" });
        dtVcFrom = new DateTimePicker(); chkVcFromNull = new CheckBox();
        dtVcTo = new DateTimePicker(); chkVcToNull = new CheckBox();
        txtVcNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "声優 person_id", numVcPersonId, 18,  18, inputWidth: 110);
        // v1.2.0 工程 C: person_id の右側に「検索...」ボタンを追加。本体側で PersonPickerDialog を起動する。
        btnPickVcPersonId = new Button
        {
            Text = "検索...",
            Location = new Point(252, 17),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickVcPersonId);
        // 検索結果ラベルは person_id の右側に置いて選択補助を行う
        lblVcPersonName.Location = new Point(330, 22);
        pnl.Controls.Add(lblVcPersonName);

        AddLabeledControl(pnl, "種別",          cboVcKind,    18,  50, inputWidth: 160);
        AddDateWithNull(pnl,  "開始日",        dtVcFrom, chkVcFromNull, 18,  82);
        AddDateWithNull(pnl,  "終了日",        dtVcTo,   chkVcToNull,   18, 114);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtVcNotes.Location = new Point(132, 146);
        txtVcNotes.Size = new Size(450, 60);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtVcNotes);

        btnNewVoiceCasting = new Button { Text = "新規",       Location = new Point(620,  18), Size = new Size(140, 28) };
        btnSaveVoiceCasting = new Button { Text = "保存 / 更新", Location = new Point(620,  50), Size = new Size(140, 28) };
        btnDeleteVoiceCasting = new Button { Text = "選択行を削除", Location = new Point(620,  82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewVoiceCasting, btnSaveVoiceCasting, btnDeleteVoiceCasting });

        tabVoiceCastings.Controls.AddRange(new Control[] { lblChar, cboVcCharacter, gridVoiceCastings, pnl });
    }

    // ====================================================================
    // 役職タブ
    // ====================================================================
    private void BuildRolesTab()
    {
        tabRoles.Padding = new Padding(8);
        gridRoles = new DataGridView { Dock = DockStyle.Top, Height = 340 };
        ConfigureListGrid(gridRoles);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtRoleCode = new TextBox(); txtRoleNameJa = new TextBox(); txtRoleNameEn = new TextBox();
        cboRoleFormatKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboRoleFormatKind.Items.AddRange(new object[]
        {
            "NORMAL", "SERIAL", "THEME_SONG", "VOICE_CAST", "COMPANY_ONLY", "LOGO_ONLY"
        });
        txtRoleFormatTemplate = new TextBox();
        numRoleDisplayOrder = new NumericUpDown { Maximum = 65535 };

        AddLabeledControl(pnl, "コード",       txtRoleCode,           18,  18, inputWidth: 200);
        AddLabeledControl(pnl, "名称(日)",     txtRoleNameJa,         18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "名称(英)",     txtRoleNameEn,         18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "書式区分",     cboRoleFormatKind,     18, 114, inputWidth: 200);
        AddLabeledControl(pnl, "既定テンプレ", txtRoleFormatTemplate, 18, 146, inputWidth: 320);
        AddLabeledControl(pnl, "表示順",       numRoleDisplayOrder,   18, 178, inputWidth: 100);

        // 役職は role_code が PK の単一マスタのため、UPSERT 1 ボタンと削除のみ。
        btnSaveRole = new Button { Text = "保存 / 更新", Location = new Point(620,  18), Size = new Size(140, 28) };
        btnDeleteRole = new Button { Text = "選択行を削除", Location = new Point(620,  50), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSaveRole, btnDeleteRole });

        tabRoles.Controls.Add(pnl);
        tabRoles.Controls.Add(gridRoles);
    }

    // ====================================================================
    // シリーズ書式上書きタブ
    // ====================================================================
    private void BuildRoleOverridesTab()
    {
        tabRoleOverrides.Padding = new Padding(8);

        var lblSeries = new Label { Text = "シリーズ", Location = new Point(18, 22), Size = new Size(100, 20) };
        cboOvSeries = new ComboBox
        {
            Location = new Point(122, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        gridRoleOverrides = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridRoleOverrides);

        var pnl = new Panel
        {
            Location = new Point(0, 340),
            Size = new Size(1080, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        cboOvRole = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        dtOvFrom = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
        dtOvTo = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
        chkOvToNull = new CheckBox { Text = "未指定" };
        txtOvFormatTemplate = new TextBox();
        txtOvNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "役職",         cboOvRole,           18,  18, inputWidth: 220);
        AddLabeledControl(pnl, "有効開始日",   dtOvFrom,            18,  50, inputWidth: 150);
        AddDateWithNull(pnl,  "有効終了日",    dtOvTo, chkOvToNull, 18,  82);
        AddLabeledControl(pnl, "書式テンプレ", txtOvFormatTemplate, 18, 114, inputWidth: 450);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtOvNotes.Location = new Point(132, 146);
        txtOvNotes.Size = new Size(450, 60);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtOvNotes);

        // PK は (series_id, role_code, valid_from) の 3 列複合のため UPSERT のみ。新規も同じボタンで処理。
        btnSaveOverride = new Button { Text = "保存 / 更新", Location = new Point(620,  18), Size = new Size(140, 28) };
        btnDeleteOverride = new Button { Text = "選択行を削除", Location = new Point(620,  50), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSaveOverride, btnDeleteOverride });

        tabRoleOverrides.Controls.AddRange(new Control[] { lblSeries, cboOvSeries, gridRoleOverrides, pnl });
    }

    // ====================================================================
    // エピソード主題歌タブ
    // ====================================================================
    private void BuildEpisodeThemeSongsTab()
    {
        tabEpisodeThemeSongs.Padding = new Padding(8);

        var lblSeries = new Label { Text = "シリーズ", Location = new Point(18, 22), Size = new Size(80, 20) };
        cboEtsSeries = new ComboBox
        {
            Location = new Point(102, 18), Size = new Size(280, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        var lblEp = new Label { Text = "エピソード", Location = new Point(400, 22), Size = new Size(80, 20) };
        cboEtsEpisode = new ComboBox
        {
            Location = new Point(484, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        gridEpisodeThemeSongs = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 240),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridEpisodeThemeSongs);

        var pnl = new Panel
        {
            Location = new Point(0, 300),
            Size = new Size(1080, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        cboEtsThemeKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboEtsThemeKind.Items.AddRange(new object[] { "OP", "ED", "INSERT" });
        // v1.2.0 工程 B' 追加：本放送限定フラグのチェックボックス。
        // 既定は OFF（=0、本放送・Blu-ray・配信ともに同じ主題歌を表す既定行）。
        // ON（=1）にすると、同じ episode + theme_kind の既定行と並立する本放送限定の例外行になる。
        chkEtsBroadcastOnly = new CheckBox
        {
            Text = "本放送限定（既定 OFF = 全媒体共通）",
            Checked = false
        };
        numEtsInsertSeq = new NumericUpDown { Maximum = 255 };
        numEtsSongRecordingId = new NumericUpDown { Maximum = 9_999_999 };
        // v1.2.0 工程 H 補修：numEtsLabelCompanyAliasId / chkEtsLabelNull は撤去済み。
        txtEtsNotes = new TextBox { Multiline = true };

        // v1.2.0 工程 B': 編集パネルの先頭に本放送限定チェックを配置（種別より上）。
        // これは PK の一部なので、ユーザーは「どの行（既定 / 本放送限定）を編集しているか」を
        // 最初に意識する流れにする。
        AddLabeledControl(pnl, "本放送フラグ",          chkEtsBroadcastOnly,       18,  18, inputWidth: 280);
        AddLabeledControl(pnl, "種別",                  cboEtsThemeKind,           18,  50, inputWidth: 120);
        AddLabeledControl(pnl, "通番（INSERT のみ）",  numEtsInsertSeq,           18,  82, inputWidth: 80);
        AddLabeledControl(pnl, "song_recording_id",    numEtsSongRecordingId,     18, 114, inputWidth: 110);
        // v1.2.0 工程 C: song_recording_id 右側に「検索...」ボタン
        btnPickEtsSongRecordingId = new Button
        {
            Text = "検索...",
            Location = new Point(252, 113),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickEtsSongRecordingId);

        // v1.2.0 工程 H 補修：旧「label company_alias_id」入力行を撤去し、備考欄を上に詰めた。
        // レーベル名はクレジット側の COMPANY エントリで持つ運用に整理（episode_theme_songs は楽曲の事実だけ）。
        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtEtsNotes.Location = new Point(132, 146);
        txtEtsNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtEtsNotes);

        btnSaveEts = new Button { Text = "保存 / 更新", Location = new Point(620,  18), Size = new Size(140, 28) };
        btnDeleteEts = new Button { Text = "選択行を削除", Location = new Point(620,  50), Size = new Size(140, 28) };
        // v1.2.0 工程 B' 追加：他話からのコピーボタン。EpisodeThemeSongCopyDialog を起動する。
        btnCopyEts = new Button { Text = "他話からコピー...", Location = new Point(620, 82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSaveEts, btnDeleteEts, btnCopyEts });

        tabEpisodeThemeSongs.Controls.AddRange(new Control[]
        {
            lblSeries, cboEtsSeries, lblEp, cboEtsEpisode, gridEpisodeThemeSongs, pnl
        });
    }

    // ====================================================================
    // シリーズ種別タブ
    // ====================================================================
    private void BuildSeriesKindsTab()
    {
        tabSeriesKinds.Padding = new Padding(8);
        gridSeriesKinds = new DataGridView { Dock = DockStyle.Top, Height = 320 };
        ConfigureListGrid(gridSeriesKinds);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtSkCode = new TextBox(); txtSkNameJa = new TextBox(); txtSkNameEn = new TextBox();
        cboSkAttachTo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        cboSkAttachTo.Items.AddRange(new object[] { "EPISODE", "SERIES" });

        AddLabeledControl(pnl, "コード",                txtSkCode,     18,  18, inputWidth: 220);
        AddLabeledControl(pnl, "名称(日)",              txtSkNameJa,   18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "名称(英)",              txtSkNameEn,   18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "クレジット紐付け先",    cboSkAttachTo, 18, 114, inputWidth: 160);

        btnSaveSeriesKind = new Button { Text = "保存 / 更新", Location = new Point(620,  18), Size = new Size(140, 28) };
        btnDeleteSeriesKind = new Button { Text = "選択行を削除", Location = new Point(620,  50), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSaveSeriesKind, btnDeleteSeriesKind });

        tabSeriesKinds.Controls.Add(pnl);
        tabSeriesKinds.Controls.Add(gridSeriesKinds);
    }

    // ====================================================================
    // パート種別タブ
    // ====================================================================
    private void BuildPartTypesTab()
    {
        tabPartTypes.Padding = new Padding(8);
        gridPartTypes = new DataGridView { Dock = DockStyle.Top, Height = 320 };
        ConfigureListGrid(gridPartTypes);

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        txtPtCode = new TextBox(); txtPtNameJa = new TextBox(); txtPtNameEn = new TextBox();
        numPtDisplayOrder = new NumericUpDown { Maximum = 255 };
        cboPtDefaultCreditKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        // 空欄項目で NULL を表現（"" を選択 = default_credit_kind = NULL）
        cboPtDefaultCreditKind.Items.AddRange(new object[] { "", "OP", "ED" });

        AddLabeledControl(pnl, "コード",         txtPtCode,             18,  18, inputWidth: 220);
        AddLabeledControl(pnl, "名称(日)",       txtPtNameJa,           18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "名称(英)",       txtPtNameEn,           18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "表示順",         numPtDisplayOrder,     18, 114, inputWidth: 100);
        AddLabeledControl(pnl, "規定クレジット種別", cboPtDefaultCreditKind, 18, 146, inputWidth: 120);

        btnSavePartType = new Button { Text = "保存 / 更新", Location = new Point(620,  18), Size = new Size(140, 28) };
        btnDeletePartType = new Button { Text = "選択行を削除", Location = new Point(620,  50), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSavePartType, btnDeletePartType });

        tabPartTypes.Controls.Add(pnl);
        tabPartTypes.Controls.Add(gridPartTypes);
    }

    // ====================================================================
    // 人物名義タブ（v1.2.0 工程 A 追加）
    // ====================================================================
    /// <summary>
    /// 人物名義タブを構築する。上部に人物選択コンボ、中段に選択人物の名義一覧、
    /// 下段の編集パネルで名義の追加・編集・削除と、共同名義人物リスト
    /// （中間表 person_alias_persons）の編集を行う。
    /// 共同名義は通常 1 人だけだが、共同ペンネーム等の稀ケースのために中間表で多対多に対応する。
    /// </summary>
    private void BuildPersonAliasesTab()
    {
        tabPersonAliases.Padding = new Padding(8);

        // 上部：人物選択コンボ
        var lblPerson = new Label { Text = "人物", Location = new Point(18, 22), Size = new Size(60, 20) };
        cboPaPerson = new ComboBox
        {
            Location = new Point(82, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        // 中段：選択人物に紐づく名義一覧
        gridPersonAliases = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 250),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridPersonAliases);

        // 下段：編集パネル
        var pnl = new Panel
        {
            Location = new Point(0, 310),
            Size = new Size(1080, 380),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        txtPaName = new TextBox();
        txtPaNameKana = new TextBox();
        numPaPredecessor = new NumericUpDown { Maximum = 9_999_999 };
        numPaSuccessor = new NumericUpDown { Maximum = 9_999_999 };
        dtPaFrom = new DateTimePicker(); chkPaFromNull = new CheckBox();
        dtPaTo = new DateTimePicker(); chkPaToNull = new CheckBox();
        txtPaNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "名義名",        txtPaName,        18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "名義名(かな)",  txtPaNameKana,    18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "前任名義 ID",   numPaPredecessor, 18,  82, inputWidth: 110);
        // v1.2.0 工程 C: 前任名義 ID の右側に「検索...」ボタン
        btnPickPaPredecessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 81),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickPaPredecessor);

        AddLabeledControl(pnl, "後任名義 ID",   numPaSuccessor,   18, 114, inputWidth: 110);
        // v1.2.0 工程 C: 後任名義 ID の右側に「検索...」ボタン
        btnPickPaSuccessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 113),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickPaSuccessor);

        AddDateWithNull(pnl,  "有効開始日",     dtPaFrom, chkPaFromNull, 18, 146);
        AddDateWithNull(pnl,  "有効終了日",     dtPaTo,   chkPaToNull,   18, 178);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 214), Size = new Size(110, 20) };
        txtPaNotes.Location = new Point(132, 210);
        txtPaNotes.Size = new Size(450, 60);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtPaNotes);

        // 共同名義人物リスト（中間表）の編集 UI。通常は単独名義なので隅にコンパクトに置く。
        var lblJoint = new Label
        {
            Text = "共同名義人物（通常は選択中の 1 人のみ）",
            Location = new Point(620, 18),
            Size = new Size(280, 20)
        };
        lstPaJointPersons = new ListBox
        {
            Location = new Point(620, 42),
            Size = new Size(280, 100)
        };
        var lblAddJoint = new Label
        {
            Text = "追加 person_id",
            Location = new Point(620, 150),
            Size = new Size(110, 20)
        };
        numPaJointPersonId = new NumericUpDown
        {
            Location = new Point(736, 146),
            Size = new Size(80, 23),
            Maximum = 9_999_999
        };
        // v1.2.0 工程 C: 共同名義 person_id の追加用に「検索...」ボタンを横に追加
        btnPickPaJointPersonId = new Button
        {
            Text = "検索...",
            Location = new Point(820, 145),
            Size = new Size(60, 25)
        };
        btnAddJointPerson = new Button
        {
            Text = "追加",
            Location = new Point(884, 145),
            Size = new Size(40, 25)
        };
        btnRemoveJointPerson = new Button
        {
            Text = "選択を解除",
            Location = new Point(620, 178),
            Size = new Size(110, 25)
        };
        pnl.Controls.AddRange(new Control[]
        {
            lblJoint, lstPaJointPersons,
            lblAddJoint, numPaJointPersonId, btnPickPaJointPersonId, btnAddJointPerson, btnRemoveJointPerson
        });

        // メインボタン列
        btnNewPersonAlias = new Button { Text = "新規",       Location = new Point(620, 220), Size = new Size(140, 28) };
        btnSavePersonAlias = new Button { Text = "保存 / 更新", Location = new Point(620, 252), Size = new Size(140, 28) };
        btnDeletePersonAlias = new Button { Text = "選択行を削除", Location = new Point(620, 284), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewPersonAlias, btnSavePersonAlias, btnDeletePersonAlias });

        tabPersonAliases.Controls.AddRange(new Control[]
        {
            lblPerson, cboPaPerson, gridPersonAliases, pnl
        });
    }

    // ====================================================================
    // 企業屋号タブ（v1.2.0 工程 A 追加）
    // ====================================================================
    /// <summary>
    /// 企業屋号タブを構築する。屋号は時期によって変わる場合があり、改名・分社化の前後を
    /// predecessor / successor で自参照リンクする。共同名義のような中間表は持たない（屋号は
    /// 単一企業に従属するため）。
    /// </summary>
    private void BuildCompanyAliasesTab()
    {
        tabCompanyAliases.Padding = new Padding(8);

        var lblCompany = new Label { Text = "企業", Location = new Point(18, 22), Size = new Size(60, 20) };
        cboCaCompany = new ComboBox
        {
            Location = new Point(82, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        gridCompanyAliases = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridCompanyAliases);

        var pnl = new Panel
        {
            Location = new Point(0, 340),
            Size = new Size(1080, 350),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        txtCaName = new TextBox();
        txtCaNameKana = new TextBox();
        numCaPredecessor = new NumericUpDown { Maximum = 9_999_999 };
        numCaSuccessor = new NumericUpDown { Maximum = 9_999_999 };
        dtCaFrom = new DateTimePicker(); chkCaFromNull = new CheckBox();
        dtCaTo = new DateTimePicker(); chkCaToNull = new CheckBox();
        txtCaNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "屋号名",        txtCaName,        18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "屋号名(かな)",  txtCaNameKana,    18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "前任屋号 ID",   numCaPredecessor, 18,  82, inputWidth: 110);
        // v1.2.0 工程 C: 前任屋号 ID の右側に「検索...」ボタン
        btnPickCaPredecessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 81),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickCaPredecessor);

        AddLabeledControl(pnl, "後任屋号 ID",   numCaSuccessor,   18, 114, inputWidth: 110);
        // v1.2.0 工程 C: 後任屋号 ID の右側に「検索...」ボタン
        btnPickCaSuccessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 113),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickCaSuccessor);

        AddDateWithNull(pnl,  "有効開始日",     dtCaFrom, chkCaFromNull, 18, 146);
        AddDateWithNull(pnl,  "有効終了日",     dtCaTo,   chkCaToNull,   18, 178);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 214), Size = new Size(110, 20) };
        txtCaNotes.Location = new Point(132, 210);
        txtCaNotes.Size = new Size(450, 60);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtCaNotes);

        btnNewCompanyAlias = new Button { Text = "新規",       Location = new Point(620, 18), Size = new Size(140, 28) };
        btnSaveCompanyAlias = new Button { Text = "保存 / 更新", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnDeleteCompanyAlias = new Button { Text = "選択行を削除", Location = new Point(620, 82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewCompanyAlias, btnSaveCompanyAlias, btnDeleteCompanyAlias });

        tabCompanyAliases.Controls.AddRange(new Control[]
        {
            lblCompany, cboCaCompany, gridCompanyAliases, pnl
        });
    }

    // ====================================================================
    // ロゴタブ（v1.2.0 工程 A 追加）
    // ====================================================================
    /// <summary>
    /// ロゴタブを構築する。企業選択 → 屋号選択（連動）→ ロゴ一覧の 2 段絞り込み。
    /// 1 つの屋号に対して CI バージョンごとに複数のロゴを保持できる
    /// （UNIQUE は (company_alias_id, ci_version_label)）。
    /// </summary>
    private void BuildLogosTab()
    {
        tabLogos.Padding = new Padding(8);

        var lblCompany = new Label { Text = "企業", Location = new Point(18, 22), Size = new Size(60, 20) };
        cboLgCompany = new ComboBox
        {
            Location = new Point(82, 18), Size = new Size(280, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        var lblAlias = new Label { Text = "屋号", Location = new Point(380, 22), Size = new Size(60, 20) };
        cboLgCompanyAlias = new ComboBox
        {
            Location = new Point(444, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        gridLogos = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 250),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridLogos);

        var pnl = new Panel
        {
            Location = new Point(0, 310),
            Size = new Size(1080, 380),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        txtLgCiVersion = new TextBox();
        dtLgFrom = new DateTimePicker(); chkLgFromNull = new CheckBox();
        dtLgTo = new DateTimePicker(); chkLgToNull = new CheckBox();
        txtLgDescription = new TextBox();
        txtLgNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "CI バージョン", txtLgCiVersion,    18,  18, inputWidth: 320);
        AddDateWithNull(pnl,  "有効開始日",     dtLgFrom, chkLgFromNull, 18,  50);
        AddDateWithNull(pnl,  "有効終了日",     dtLgTo,   chkLgToNull,   18,  82);
        AddLabeledControl(pnl, "概要説明",      txtLgDescription, 18, 114, inputWidth: 450);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtLgNotes.Location = new Point(132, 146);
        txtLgNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtLgNotes);

        btnNewLogo = new Button { Text = "新規",       Location = new Point(620, 18), Size = new Size(140, 28) };
        btnSaveLogo = new Button { Text = "保存 / 更新", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnDeleteLogo = new Button { Text = "選択行を削除", Location = new Point(620, 82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewLogo, btnSaveLogo, btnDeleteLogo });

        tabLogos.Controls.AddRange(new Control[]
        {
            lblCompany, cboLgCompany, lblAlias, cboLgCompanyAlias, gridLogos, pnl
        });
    }

    // ====================================================================
    // キャラクター名義タブ（v1.2.0 工程 A 追加）
    // ====================================================================
    /// <summary>
    /// キャラクター名義タブを構築する。キャラ選択 → 名義一覧。キャラクター名義には
    /// 前後リンクや共同名義の概念は無い（同一キャラでも話数によって "なぎさ" / "キュアブラック" /
    /// "ブラック" と表記が変わるだけなので、各表記を期間付きで列挙するシンプル構造）。
    /// </summary>
    private void BuildCharacterAliasesTab()
    {
        tabCharacterAliases.Padding = new Padding(8);

        var lblChar = new Label { Text = "キャラクター", Location = new Point(18, 22), Size = new Size(100, 20) };
        cboCaaCharacter = new ComboBox
        {
            Location = new Point(122, 18), Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        gridCharacterAliases = new DataGridView
        {
            Location = new Point(18, 50),
            Size = new Size(1040, 280),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridCharacterAliases);

        var pnl = new Panel
        {
            Location = new Point(0, 340),
            Size = new Size(1080, 350),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        txtCaaName = new TextBox();
        txtCaaNameKana = new TextBox();
        dtCaaFrom = new DateTimePicker(); chkCaaFromNull = new CheckBox();
        dtCaaTo = new DateTimePicker(); chkCaaToNull = new CheckBox();
        txtCaaNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "名義名",        txtCaaName,     18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "名義名(かな)",  txtCaaNameKana, 18,  50, inputWidth: 320);
        AddDateWithNull(pnl,  "有効開始日",     dtCaaFrom, chkCaaFromNull, 18,  82);
        AddDateWithNull(pnl,  "有効終了日",     dtCaaTo,   chkCaaToNull,   18, 114);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtCaaNotes.Location = new Point(132, 146);
        txtCaaNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtCaaNotes);

        btnNewCharacterAlias = new Button { Text = "新規",       Location = new Point(620, 18), Size = new Size(140, 28) };
        btnSaveCharacterAlias = new Button { Text = "保存 / 更新", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnDeleteCharacterAlias = new Button { Text = "選択行を削除", Location = new Point(620, 82), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnNewCharacterAlias, btnSaveCharacterAlias, btnDeleteCharacterAlias });

        tabCharacterAliases.Controls.AddRange(new Control[]
        {
            lblChar, cboCaaCharacter, gridCharacterAliases, pnl
        });
    }
}
