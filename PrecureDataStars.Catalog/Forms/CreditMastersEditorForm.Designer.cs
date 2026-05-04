#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット系マスタ管理フォーム（v1.2.0 新設）。
/// <para>
/// 9 タブ構成: 人物 / 企業（屋号・ロゴ含む）/ キャラクター（名義含む）/
/// 声優キャスティング / 役職 / シリーズ書式上書き / エピソード主題歌 /
/// シリーズ種別（v1.2.0 列追加）/ パート種別（v1.2.0 列追加）。
/// </para>
/// <para>
/// 本ファイルは Designer 部（コントロール宣言と <see cref="InitializeComponent"/> による
/// レイアウト構築）。実ロジックは <c>CreditMastersEditorForm.cs</c> 側の partial で実装する。
/// クレジット本体（カード／ブロック／エントリ）の編集 UI は v1.2.1 で別途追加予定。
/// </para>
/// </summary>
partial class CreditMastersEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private TabControl tabControl = null!;

    // 9 タブ
    private TabPage tabPersons = null!;
    private TabPage tabCompanies = null!;
    private TabPage tabCharacters = null!;
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
    private NumericUpDown numEtsInsertSeq = null!;
    private NumericUpDown numEtsSongRecordingId = null!;
    private NumericUpDown numEtsLabelCompanyAliasId = null!;
    private CheckBox chkEtsLabelNull = null!;
    private TextBox txtEtsNotes = null!;
    private Button btnSaveEts = null!;
    private Button btnDeleteEts = null!;

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
        tabCompanies = new TabPage { Text = "企業" };
        tabCharacters = new TabPage { Text = "キャラクター" };
        tabVoiceCastings = new TabPage { Text = "声優キャスティング" };
        tabRoles = new TabPage { Text = "役職" };
        tabRoleOverrides = new TabPage { Text = "シリーズ書式上書き" };
        tabEpisodeThemeSongs = new TabPage { Text = "エピソード主題歌" };
        tabSeriesKinds = new TabPage { Text = "シリーズ種別" };
        tabPartTypes = new TabPage { Text = "パート種別" };

        BuildPersonsTab();
        BuildCompaniesTab();
        BuildCharactersTab();
        BuildVoiceCastingsTab();
        BuildRolesTab();
        BuildRoleOverridesTab();
        BuildEpisodeThemeSongsTab();
        BuildSeriesKindsTab();
        BuildPartTypesTab();

        tabControl.Dock = DockStyle.Fill;
        tabControl.TabPages.AddRange(new TabPage[]
        {
            tabPersons, tabCompanies, tabCharacters, tabVoiceCastings,
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
        // 検索結果ラベルは person_id の右側に置いて選択補助を行う
        lblVcPersonName.Location = new Point(258, 22);
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
        numEtsInsertSeq = new NumericUpDown { Maximum = 255 };
        numEtsSongRecordingId = new NumericUpDown { Maximum = 9_999_999 };
        numEtsLabelCompanyAliasId = new NumericUpDown { Maximum = 9_999_999 };
        chkEtsLabelNull = new CheckBox { Text = "未指定" };
        txtEtsNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "種別",                  cboEtsThemeKind,           18,  18, inputWidth: 120);
        AddLabeledControl(pnl, "通番（INSERT のみ）",  numEtsInsertSeq,           18,  50, inputWidth: 80);
        AddLabeledControl(pnl, "song_recording_id",    numEtsSongRecordingId,     18,  82, inputWidth: 110);
        AddLabeledControl(pnl, "label company_alias_id", numEtsLabelCompanyAliasId, 18, 114, inputWidth: 110);
        chkEtsLabelNull.Location = new Point(258, 117);
        chkEtsLabelNull.Size = new Size(70, 23);
        pnl.Controls.Add(chkEtsLabelNull);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtEtsNotes.Location = new Point(132, 146);
        txtEtsNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtEtsNotes);

        btnSaveEts = new Button { Text = "保存 / 更新", Location = new Point(620,  18), Size = new Size(140, 28) };
        btnDeleteEts = new Button { Text = "選択行を削除", Location = new Point(620,  50), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSaveEts, btnDeleteEts });

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
}
