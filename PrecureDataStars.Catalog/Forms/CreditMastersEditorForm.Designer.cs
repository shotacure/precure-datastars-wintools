#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット系マスタ管理フォーム（v1.2.0 新設、v1.2.4 でタブ構成を再編）。
/// <para>
/// v1.2.4 タブ構成（15 タブ）：プリキュア（先頭・新設）／人物／人物名義／企業／企業屋号／
/// ロゴ／キャラクター／キャラクター名義／キャラクター続柄（新設）／家族関係（新設）／
/// 役職／役職テンプレート／エピソード主題歌／シリーズ種別／パート種別。
/// 声優キャスティングタブは v1.2.4 で撤去（ノンクレ除いてクレジットされている事実が
/// キャスティング、という業務ルールに統一し、credit_block_entries に一元化）。
/// </para>
/// <para>
/// 本ファイルは Designer 部（コントロール宣言と <see cref="InitializeComponent"/> による
/// レイアウト構築）。実ロジックは <c>CreditMastersEditorForm.cs</c> 側の partial で実装する。
/// プリキュア／続柄／家族関係の 3 タブは <c>CreditMastersEditorForm.PrecureTabs.cs</c> に
/// 分離している（v1.2.4）。
/// </para>
/// </summary>
partial class CreditMastersEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private TabControl tabControl = null!;

    // v1.2.4: 15 タブ構成（先頭にプリキュア、声優キャスティングを撤去、続柄／家族関係を追加）
    private TabPage tabPrecures = null!;              // v1.2.4 追加：プリキュア（先頭タブ）
    private TabPage tabPersons = null!;
    private TabPage tabPersonAliases = null!;        // v1.2.0 工程 A 追加：人物名義（person_aliases + person_alias_persons）
    private TabPage tabCompanies = null!;
    private TabPage tabCompanyAliases = null!;       // v1.2.0 工程 A 追加：企業屋号（company_aliases）
    private TabPage tabLogos = null!;                // v1.2.0 工程 A 追加：ロゴ（logos）
    private TabPage tabCharacters = null!;
    private TabPage tabCharacterAliases = null!;     // v1.2.0 工程 A 追加：キャラクター名義（character_aliases）
    private TabPage tabCharacterRelationKinds = null!; // v1.2.4 追加：キャラクター続柄マスタ
    private TabPage tabCharacterFamilyRelations = null!; // v1.2.4 追加：家族関係（汎用、プリキュア以外でも使える）
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
    private TextBox txtChNameEn = null!;       // v1.2.4 追加：英語表記
    private ComboBox cboChKind = null!;
    private TextBox txtChNotes = null!;
    private Button btnNewCharacter = null!;
    private Button btnSaveCharacter = null!;
    private Button btnDeleteCharacter = null!;

    // ─────────────── 声優キャスティングタブのフィールド群は v1.2.4 で撤去 ───────────────
    // character_voice_castings テーブル自体を v1.2.4 で廃止したため、関連 UI 群
    // （cboVcCharacter / gridVoiceCastings / numVcPersonId / lblVcPersonName /
    //  cboVcKind / dtVcFrom / chkVcFromNull / dtVcTo / chkVcToNull / txtVcNotes /
    //  btnNewVoiceCasting / btnSaveVoiceCasting / btnDeleteVoiceCasting / btnPickVcPersonId）
    // も全廃。声優のキャスティング情報は credit_block_entries（CHARACTER_VOICE エントリ）に
    // 統合された。

    // ─────────────── 役職タブ ───────────────
    private DataGridView gridRoles = null!;
    private TextBox txtRoleCode = null!;
    private TextBox txtRoleNameJa = null!;
    private TextBox txtRoleNameEn = null!;
    private ComboBox cboRoleFormatKind = null!;
    // v1.2.0 工程 H-10：txtRoleFormatTemplate は撤去（書式は role_templates テーブルで管理）。
    private NumericUpDown numRoleDisplayOrder = null!;
    private Button btnSaveRole = null!;
    private Button btnDeleteRole = null!;

    // ─────────────── 役職テンプレートタブ（旧：シリーズ書式上書き、v1.2.0 H-10 で転換） ───────────────
    private ComboBox cboOvSeries = null!;          // 上部の役職フィルタコンボ（フィールド名は転用）
    private DataGridView gridRoleOverrides = null!;
    private ComboBox cboOvRole = null!;
    // v1.2.0 工程 H-10：詳細パネルのシリーズ選択コンボ（「（既定 / 全シリーズ）」または特定シリーズ）
    private ComboBox cboOvTemplateSeries = null!;
    // v1.2.0 工程 H-10：valid_from / valid_to / chkOvToNull は廃止（フィールドはコンパイル維持のため残す）
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
    private Button btnRangeCopyEts = null!;               // v1.2.0 工程 H-8 追加：範囲コピーダイアログ起動

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
    private TextBox txtPaNameEn = null!;       // v1.2.4 追加：英語表記
    private NumericUpDown numPaPredecessor = null!;  // 前任名義 ID（自参照、改名前リンク）
    private NumericUpDown numPaSuccessor = null!;    // 後任名義 ID（自参照、改名後リンク）
    private DateTimePicker dtPaFrom = null!;
    private CheckBox chkPaFromNull = null!;
    private DateTimePicker dtPaTo = null!;
    private CheckBox chkPaToNull = null!;
    private TextBox txtPaNotes = null!;
    // v1.2.3 追加：表示上書きテキスト（ユニット名義などで定形外表示が必要なときに使う）と
    //              ユニットメンバー編集ダイアログ起動ボタン
    private TextBox txtPaDisplayOverride = null!;
    private Button btnPaEditMembers = null!;
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
    private TextBox txtCaNameEn = null!;       // v1.2.4 追加：英語表記
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
    private TextBox txtCaaNameEn = null!;      // v1.2.4 追加：英語表記
    // v1.2.1: dtCaaFrom / chkCaaFromNull / dtCaaTo / chkCaaToNull は撤去。
    // character_aliases.valid_from / valid_to 列を削除したため UI 入力欄も不要になった。
    private TextBox txtCaaNotes = null!;
    private Button btnNewCharacterAlias = null!;
    private Button btnSaveCharacterAlias = null!;
    private Button btnDeleteCharacterAlias = null!;
    // v1.2.1 名寄せ機能：選択中のキャラ名義を別キャラに付け替え／改名するボタン
    private Button btnReassignCharacterAlias = null!;
    private Button btnRenameCharacterAlias = null!;

    // v1.2.1 名寄せ機能：人物名義タブ（PA）にも同様の付け替え／改名ボタンを追加
    private Button btnReassignPersonAlias = null!;
    private Button btnRenamePersonAlias = null!;

    // v1.2.1 名寄せ機能：企業屋号タブ（CA）にも同様の付け替え／改名ボタンを追加
    private Button btnReassignCompanyAlias = null!;
    private Button btnRenameCompanyAlias = null!;

    // ─────────────── ピッカー呼び出しボタン（v1.2.0 工程 C 追加） ───────────────
    // 既存の数値直入力欄の隣に「検索...」ボタンを配置し、押下でピッカーダイアログを開く構成。
    // 数値直入力自体も維持しているので、ID が手元にあるなら直入力でも従来通り操作できる。
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
        // v1.2.4: タブ生成。先頭にプリキュア、声優キャスティングを撤去、続柄／家族関係を追加。
        tabPrecures = new TabPage { Text = "プリキュア" };
        tabPersons = new TabPage { Text = "人物" };
        tabPersonAliases = new TabPage { Text = "人物名義" };
        tabCompanies = new TabPage { Text = "企業" };
        tabCompanyAliases = new TabPage { Text = "企業屋号" };
        tabLogos = new TabPage { Text = "ロゴ" };
        tabCharacters = new TabPage { Text = "キャラクター" };
        tabCharacterAliases = new TabPage { Text = "キャラクター名義" };
        tabCharacterRelationKinds = new TabPage { Text = "キャラクター続柄" };
        tabCharacterFamilyRelations = new TabPage { Text = "家族関係" };
        tabRoles = new TabPage { Text = "役職" };
        tabRoleOverrides = new TabPage { Text = "役職テンプレート" };
        tabEpisodeThemeSongs = new TabPage { Text = "エピソード主題歌" };
        tabSeriesKinds = new TabPage { Text = "シリーズ種別" };
        tabPartTypes = new TabPage { Text = "パート種別" };

        // v1.2.4: 各タブの内容構築。BuildPrecuresTab / BuildCharacterRelationKindsTab /
        // BuildCharacterFamilyRelationsTab は CreditMastersEditorForm.PrecureTabs.cs（partial）に定義。
        BuildPrecuresTab();
        BuildPersonsTab();
        BuildPersonAliasesTab();
        BuildCompaniesTab();
        BuildCompanyAliasesTab();
        BuildLogosTab();
        BuildCharactersTab();
        BuildCharacterAliasesTab();
        BuildCharacterRelationKindsTab();
        BuildCharacterFamilyRelationsTab();
        BuildRolesTab();
        BuildRoleOverridesTab();
        BuildEpisodeThemeSongsTab();
        BuildSeriesKindsTab();
        BuildPartTypesTab();

        tabControl.Dock = DockStyle.Fill;
        // v1.2.4 タブ並び：プリキュアを先頭に置き、その後ろに既存マスタ群（親→名義→新マスタの順）。
        // 「親マスタ → その名義」を隣接させる構成は v1.2.0 工程 A から踏襲。続柄／家族関係は
        // キャラクター名義の直後に置いて、キャラ系の編集動線をひと続きにする。
        tabControl.TabPages.AddRange(new TabPage[]
        {
            tabPrecures,
            tabPersons, tabPersonAliases,
            tabCompanies, tabCompanyAliases, tabLogos,
            tabCharacters, tabCharacterAliases,
            tabCharacterRelationKinds, tabCharacterFamilyRelations,
            tabRoles, tabRoleOverrides, tabEpisodeThemeSongs,
            tabSeriesKinds, tabPartTypes
        });

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        // v1.2.4: プリキュアタブの内容（変身名義 4 本＋肌色ピッカー＋家族グリッド）が
        // 1100x720 ではキツキツになるため、両方向に拡張（横は SkinColorPicker 560 +
        // 一覧グリッド + 余白を考慮、縦は家族グリッドの確保を考慮）。
        ClientSize = new Size(1500, 850);
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
        txtChName = new TextBox(); txtChNameKana = new TextBox(); txtChNameEn = new TextBox();
        // v1.2.1: 旧コードでは "MAIN/SUPPORT/GUEST/MOB/OTHER" の文字列を Items に直書きしていたが、
        // v1.2.0 工程 F で character_kind がマスタ化されたので、Designer ではハードコードしない
        // （実体は .cs 側 BindCharacterKindCombo() で CharacterKindsRepository.GetAllAsync() の結果を
        //  DataSource にバインドする）。DropDownStyle と表示文字列の調整だけ行う。
        cboChKind = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            DisplayMember = "Display",   // BindCharacterKindCombo() でバインドする項目クラスのプロパティ
            ValueMember = "KindCode",
        };
        txtChNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "名前",     txtChName,     18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "名前(かな)", txtChNameKana, 18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "英語表記", txtChNameEn,   18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "区分",     cboChKind,    18, 114, inputWidth: 160);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtChNotes.Location = new Point(132, 146);
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
        // v1.2.0 工程 H-10：書式テンプレは role_templates テーブルへ移管。
        // 役職タブからは「既定テンプレ」入力欄を撤去し、専用の「役職テンプレート」タブで編集する。
        numRoleDisplayOrder = new NumericUpDown { Maximum = 65535 };

        AddLabeledControl(pnl, "コード",       txtRoleCode,           18,  18, inputWidth: 200);
        AddLabeledControl(pnl, "名称(日)",     txtRoleNameJa,         18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "名称(英)",     txtRoleNameEn,         18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "書式区分",     cboRoleFormatKind,     18, 114, inputWidth: 200);
        AddLabeledControl(pnl, "表示順",       numRoleDisplayOrder,   18, 146, inputWidth: 100);

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
    /// <summary>
    /// 「役職テンプレート」タブ（v1.2.0 工程 H-10 で「シリーズ書式上書き」から転換）。
    /// <para>
    /// 上部の役職コンボで役職を選択 → そのテンプレ一覧（既定 + シリーズ別）が中段グリッドに出る。
    /// 下段の詳細パネルで `format_template` を **複数行 TextBox** で編集できる（テンプレは改行を含むため）。
    /// シリーズコンボの「（既定 / 全シリーズ）」を選ぶと series_id=NULL の既定テンプレ、特定シリーズを
    /// 選ぶとそのシリーズ専用テンプレを編集する。
    /// </para>
    /// </summary>
    private void BuildRoleOverridesTab()
    {
        // ─────────────────────────────────────────────────────────────
        // 役職テンプレートタブ（v1.2.0 工程 H-13 で再設計）
        // ─────────────────────────────────────────────────────────────
        // 旧設計では「上部フィルタ」と「下部詳細パネル」の役割分担が不明瞭で、保存ボタンも
        // パネル右上で見落としやすかった。本工程で以下のシンプル構成に再設計：
        //   (a) ヘッダ説明文（このタブの使い方を 2 行で説明）
        //   (b) 上部の役職コンボ 1 個（フィルタ兼編集対象、cboOvSeries の役割をここで担う）
        //   (c) 一覧グリッド（同役職のテンプレ全部 = 既定 + 各シリーズ別）
        //   (d) 操作ボタン 3 個（+ 新規追加 / 💾 保存 / 🗑 削除）をグリッドの下に横並びで配置
        //   (e) 詳細編集パネル（シリーズ・書式テンプレ・備考）
        // 旧フィールド名（cboOvSeries, gridRoleOverrides, cboOvRole, cboOvTemplateSeries,
        //   txtOvFormatTemplate, txtOvNotes, btnSaveOverride, btnDeleteOverride, dtOvFrom,
        //   dtOvTo, chkOvToNull）はソース全体での参照箇所が多いため、フィールド名は維持しつつ
        //   配置とラベルを変更している。dtOvFrom / dtOvTo / chkOvToNull は H-10 で論理的に廃止
        //   済みなので、コンパイル維持のためフィールドだけ生成して画面には追加しない。
        tabRoleOverrides.Padding = new Padding(8);

        // ── (a) ヘッダ説明 ──
        // 上端 8px の Padding 内に、2 行の説明文を出す。色は控えめなグレー、サイズも本文より少し小さめ。
        var lblHelp = new Label
        {
            Text = "役職テンプレートは「（既定 / 全シリーズ）」と「シリーズ別」の 2 段階で持てます。"
                 + "レンダリング時は (役職, シリーズ) → (役職, 既定) の優先順で解決されます。",
            Location = new Point(18, 8),
            Size = new Size(1040, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = Color.DimGray,
            Font = new Font("Yu Gothic UI", 9F),
            AutoSize = false
        };

        // ── (b) 上部の役職コンボ（フィルタ兼編集対象） ──
        // 「役職フィルタ」というラベルだったところを「役職」に統一。
        var lblRole = new Label { Text = "役職", Location = new Point(18, 56), Size = new Size(60, 20) };
        cboOvSeries = new ComboBox  // 旧フィールド名を流用しつつ、実体は役職コンボ
        {
            Location = new Point(82, 52),
            Size = new Size(360, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        // ── (c) 一覧グリッド ──
        // ヘッダ説明 + 役職コンボの分だけ Y 座標を下げる。
        gridRoleOverrides = new DataGridView
        {
            Location = new Point(18, 86),
            Size = new Size(1040, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        ConfigureListGrid(gridRoleOverrides);

        // ── (d) 操作ボタン 3 個をグリッド直下に横並び ──
        // 「+ 新規追加」「💾 保存」「🗑 削除」を左から並べる。Anchor=Top で上端固定。
        // 旧設計では btnSaveOverride / btnDeleteOverride を詳細パネル右上に配置していたが、
        // 画面幅が狭いと右に隠れて見えなかった。グリッド直下なら必ず視認できる。
        var btnNewOverride = new Button
        {
            Text = "+ 新規追加",
            Location = new Point(18, 296),
            Size = new Size(120, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            Name = "btnNewOverride"
        };
        btnSaveOverride = new Button
        {
            Text = "💾 保存 / 更新",
            Location = new Point(148, 296),
            Size = new Size(140, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        btnDeleteOverride = new Button
        {
            Text = "🗑 選択行を削除",
            Location = new Point(298, 296),
            Size = new Size(140, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };

        // ── (e) 詳細編集パネル（グリッド + ボタンの下、Y=336 から） ──
        var pnl = new Panel
        {
            Location = new Point(0, 336),
            Size = new Size(1080, 320),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            Padding = new Padding(18)
        };

        cboOvRole = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
        // v1.2.0 工程 H-10：valid_from / valid_to は廃止。フィールドだけ残してパネル外に置く。
        dtOvFrom = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Visible = false };
        dtOvTo = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Visible = false };
        chkOvToNull = new CheckBox { Text = "未指定", Visible = false };

        // 書式テンプレは改行を含む複数行入力に対応（v1.2.0 工程 H-10）。
        txtOvFormatTemplate = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Font = new Font("Consolas", 10F),
        };
        txtOvNotes = new TextBox { Multiline = true };

        // シリーズ選択用の専用コンボ（v1.2.0 工程 H-10）。
        // 「（既定 / 全シリーズ）」エントリ + 全シリーズエントリを混在させて選ぶ。
        cboOvTemplateSeries = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

        // 旧パネルにあった「役職」コンボは詳細パネル外（上部の cboOvSeries）に統一したため、
        // ここではシリーズ・書式テンプレ・備考だけを配置する。役職は上のコンボに従う。
        AddLabeledControl(pnl, "シリーズ", cboOvTemplateSeries, 18, 18, inputWidth: 360);

        var lblFmt = new Label { Text = "書式テンプレ", Location = new Point(18, 54), Size = new Size(110, 20) };
        txtOvFormatTemplate.Location = new Point(132, 50);
        txtOvFormatTemplate.Size = new Size(900, 160);
        txtOvFormatTemplate.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnl.Controls.Add(lblFmt); pnl.Controls.Add(txtOvFormatTemplate);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 220), Size = new Size(110, 20) };
        txtOvNotes.Location = new Point(132, 216);
        txtOvNotes.Size = new Size(900, 60);
        txtOvNotes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtOvNotes);

        // cboOvRole は使わないが他コードからの参照維持のためパネル外（不可視）に置く
        pnl.Controls.Add(cboOvRole);

        tabRoleOverrides.Controls.AddRange(new Control[]
        {
            lblHelp,
            lblRole, cboOvSeries,
            gridRoleOverrides,
            btnNewOverride, btnSaveOverride, btnDeleteOverride,
            pnl
        });
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
        // v1.2.0 工程 H-8 追加：範囲コピーボタン。EpisodeThemeSongRangeCopyDialog を起動する。
        // 1 話の主題歌を 2 話〜49 話に一括投入する用途を担う。
        btnRangeCopyEts = new Button { Text = "範囲コピー...", Location = new Point(620, 116), Size = new Size(140, 28) };
        pnl.Controls.AddRange(new Control[] { btnSaveEts, btnDeleteEts, btnCopyEts, btnRangeCopyEts });

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
        txtPaNameEn = new TextBox();
        numPaPredecessor = new NumericUpDown { Maximum = 9_999_999 };
        numPaSuccessor = new NumericUpDown { Maximum = 9_999_999 };
        dtPaFrom = new DateTimePicker(); chkPaFromNull = new CheckBox();
        dtPaTo = new DateTimePicker(); chkPaToNull = new CheckBox();
        txtPaNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "名義名",        txtPaName,        18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "名義名(かな)",  txtPaNameKana,    18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "英語表記",      txtPaNameEn,      18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "前任名義 ID",   numPaPredecessor, 18, 114, inputWidth: 110);
        // v1.2.0 工程 C: 前任名義 ID の右側に「検索...」ボタン
        btnPickPaPredecessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 113),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickPaPredecessor);

        AddLabeledControl(pnl, "後任名義 ID",   numPaSuccessor,   18, 146, inputWidth: 110);
        // v1.2.0 工程 C: 後任名義 ID の右側に「検索...」ボタン
        btnPickPaSuccessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 145),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickPaSuccessor);

        AddDateWithNull(pnl,  "有効開始日",     dtPaFrom, chkPaFromNull, 18, 178);
        AddDateWithNull(pnl,  "有効終了日",     dtPaTo,   chkPaToNull,   18, 210);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 246), Size = new Size(110, 20) };
        txtPaNotes.Location = new Point(132, 242);
        txtPaNotes.Size = new Size(450, 60);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtPaNotes);

        // v1.2.3 追加：display_text_override 入力欄。
        // ユニット名義などで定形外の長い表示文字列が必要なケース用。空のままなら name が表示に使われる。
        var lblPaOverride = new Label
        {
            Text = "表示上書き",
            Location = new Point(18, 314),
            Size = new Size(110, 20)
        };
        var lblPaOverrideHint = new Label
        {
            Text = "（空のまま=name を使用。例: \"プリキュアシンガーズ+1(五條真由美、池田 彩、…)\"）",
            Location = new Point(132, 336),
            Size = new Size(450, 16),
            ForeColor = Color.DimGray
        };
        txtPaDisplayOverride = new TextBox
        {
            Location = new Point(132, 310),
            Size = new Size(450, 23),
            MaxLength = 1024
        };
        pnl.Controls.AddRange(new Control[] { lblPaOverride, txtPaDisplayOverride, lblPaOverrideHint });

        // v1.2.3 追加：ユニットメンバー編集ボタン。
        // PersonAliasMembersEditDialog を開き、当該名義の構成メンバーを管理する。
        btnPaEditMembers = new Button
        {
            Text = "ユニットメンバー編集...",
            Location = new Point(132, 358),
            Size = new Size(180, 25)
        };
        pnl.Controls.Add(btnPaEditMembers);

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
        // v1.2.1: 名寄せ機能用の 2 ボタンを追加（既存ボタン群の右側に配置）。
        btnReassignPersonAlias = new Button { Text = "別人物に付け替え...", Location = new Point(770, 220), Size = new Size(180, 28) };
        btnRenamePersonAlias = new Button { Text = "この名義で改名...",     Location = new Point(770, 252), Size = new Size(180, 28) };
        pnl.Controls.AddRange(new Control[] {
            btnNewPersonAlias, btnSavePersonAlias, btnDeletePersonAlias,
            btnReassignPersonAlias, btnRenamePersonAlias
        });

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
        txtCaNameEn = new TextBox();
        numCaPredecessor = new NumericUpDown { Maximum = 9_999_999 };
        numCaSuccessor = new NumericUpDown { Maximum = 9_999_999 };
        dtCaFrom = new DateTimePicker(); chkCaFromNull = new CheckBox();
        dtCaTo = new DateTimePicker(); chkCaToNull = new CheckBox();
        txtCaNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "屋号名",        txtCaName,        18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "屋号名(かな)",  txtCaNameKana,    18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "英語表記",      txtCaNameEn,      18,  82, inputWidth: 320);
        AddLabeledControl(pnl, "前任屋号 ID",   numCaPredecessor, 18, 114, inputWidth: 110);
        // v1.2.0 工程 C: 前任屋号 ID の右側に「検索...」ボタン
        btnPickCaPredecessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 113),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickCaPredecessor);

        AddLabeledControl(pnl, "後任屋号 ID",   numCaSuccessor,   18, 146, inputWidth: 110);
        // v1.2.0 工程 C: 後任屋号 ID の右側に「検索...」ボタン
        btnPickCaSuccessor = new Button
        {
            Text = "検索...",
            Location = new Point(252, 145),
            Size = new Size(70, 25)
        };
        pnl.Controls.Add(btnPickCaSuccessor);

        AddDateWithNull(pnl,  "有効開始日",     dtCaFrom, chkCaFromNull, 18, 178);
        AddDateWithNull(pnl,  "有効終了日",     dtCaTo,   chkCaToNull,   18, 210);

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 246), Size = new Size(110, 20) };
        txtCaNotes.Location = new Point(132, 242);
        txtCaNotes.Size = new Size(450, 60);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtCaNotes);

        btnNewCompanyAlias = new Button { Text = "新規",       Location = new Point(620, 18), Size = new Size(140, 28) };
        btnSaveCompanyAlias = new Button { Text = "保存 / 更新", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnDeleteCompanyAlias = new Button { Text = "選択行を削除", Location = new Point(620, 82), Size = new Size(140, 28) };
        // v1.2.1: 名寄せ機能用の 2 ボタンを追加（既存ボタン群の下に配置）。
        btnReassignCompanyAlias = new Button { Text = "別企業に付け替え...", Location = new Point(620, 122), Size = new Size(180, 28) };
        btnRenameCompanyAlias = new Button { Text = "この屋号で改名...",     Location = new Point(620, 154), Size = new Size(180, 28) };
        pnl.Controls.AddRange(new Control[] {
            btnNewCompanyAlias, btnSaveCompanyAlias, btnDeleteCompanyAlias,
            btnReassignCompanyAlias, btnRenameCompanyAlias
        });

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
        txtCaaNameEn = new TextBox();
        // v1.2.1: 有効期間 (valid_from / valid_to) の DateTimePicker / CheckBox を撤去。
        // character_aliases から該当列を物理削除したのに合わせ、UI 側からも入力欄を取り除いた。
        // v1.2.4: 英語表記 (name_en) を追加。空いていた Y=82 行に置く。備考は Y=150 のまま据え置き。
        txtCaaNotes = new TextBox { Multiline = true };

        AddLabeledControl(pnl, "名義名",        txtCaaName,     18,  18, inputWidth: 320);
        AddLabeledControl(pnl, "名義名(かな)",  txtCaaNameKana, 18,  50, inputWidth: 320);
        AddLabeledControl(pnl, "英語表記",      txtCaaNameEn,   18,  82, inputWidth: 320);
        // v1.2.1: AddDateWithNull の呼び出しは撤去。

        var lblNotes = new Label { Text = "備考", Location = new Point(18, 150), Size = new Size(110, 20) };
        txtCaaNotes.Location = new Point(132, 146);
        txtCaaNotes.Size = new Size(450, 80);
        pnl.Controls.Add(lblNotes); pnl.Controls.Add(txtCaaNotes);

        btnNewCharacterAlias = new Button { Text = "新規",       Location = new Point(620, 18), Size = new Size(140, 28) };
        btnSaveCharacterAlias = new Button { Text = "保存 / 更新", Location = new Point(620, 50), Size = new Size(140, 28) };
        btnDeleteCharacterAlias = new Button { Text = "選択行を削除", Location = new Point(620, 82), Size = new Size(140, 28) };
        // v1.2.1: 名寄せ機能用の 2 ボタンを追加。
        btnReassignCharacterAlias = new Button { Text = "別キャラに付け替え...", Location = new Point(620, 122), Size = new Size(180, 28) };
        btnRenameCharacterAlias = new Button { Text = "この名義で改名...",      Location = new Point(620, 154), Size = new Size(180, 28) };
        pnl.Controls.AddRange(new Control[] {
            btnNewCharacterAlias, btnSaveCharacterAlias, btnDeleteCharacterAlias,
            btnReassignCharacterAlias, btnRenameCharacterAlias
        });

        tabCharacterAliases.Controls.AddRange(new Control[]
        {
            lblChar, cboCaaCharacter, gridCharacterAliases, pnl
        });
    }
}
