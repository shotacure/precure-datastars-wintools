
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット系マスタ管理フォーム（v1.2.0 新設、v1.2.4 でタブ構成を再編）。
/// v1.2.4 で 15 タブ構成: プリキュア（先頭・新設）／人物／人物名義／企業／企業屋号／
/// ロゴ／キャラクター／キャラクター名義／キャラクター続柄（新設）／家族関係（新設）／
/// 役職／役職テンプレート／エピソード主題歌／シリーズ種別／パート種別を管理する。
/// 声優キャスティングタブは v1.2.4 で撤去（業務ルール上、ノンクレ除いてクレジットされている
/// 事実 = キャスティングとし、credit_block_entries の CHARACTER_VOICE エントリに統合した）。
/// <para>
/// 操作流儀は既存 <see cref="MastersEditorForm"/> と同様に「DataGridView バインド +
/// 編集パネル + 新規 / 保存・更新 / 削除」のボタン構成で統一している。
/// 監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）は <see cref="HideAuditColumns"/>
/// により全グリッドで自動非表示化。
/// </para>
/// <para>
/// プリキュア／キャラクター続柄／家族関係の 3 タブの実装は本ファイルではなく
/// <c>CreditMastersEditorForm.PrecureTabs.cs</c>（partial）に分離している（v1.2.4）。
/// </para>
/// </summary>
public partial class CreditMastersEditorForm : Form
{
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CharactersRepository _charactersRepo;
    // v1.2.4: 声優キャスティング専用テーブルを廃止。リポジトリも撤去した。
    private readonly RolesRepository _rolesRepo;
    // v1.2.0 工程 H-10：旧 SeriesRoleFormatOverridesRepository は廃止し、
    // RoleTemplatesRepository（既定とシリーズ別を統合）に置き換え。
    private readonly RoleTemplatesRepository _roleTemplatesRepo;
    // v1.2.0 工程 H-10：クレジット種別マスタの CRUD 用。
    private readonly CreditKindsRepository _creditKindsRepo;
    private readonly EpisodeThemeSongsRepository _episodeThemeSongsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;
    private readonly PartTypesRepository _partTypesRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    // v1.2.0 工程 A 追加：マスタ補完（名義・屋号・ロゴ）用リポジトリ
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // v1.2.0 工程 C 追加：歌録音ピッカー用
    private readonly SongRecordingsRepository _songRecordingsRepo;
    // v1.2.0 工程 F 追加：キャラクター区分マスタ
    private readonly CharacterKindsRepository _characterKindsRepo;

    // v1.2.3 追加：ユニットメンバー管理用リポジトリ（人物名義タブから「ユニットメンバー編集...」ボタンで使用）
    private readonly PersonAliasMembersRepository _personAliasMembersRepo;

    // v1.2.4 追加：プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
    private readonly PrecuresRepository _precuresRepo;
    private readonly CharacterRelationKindsRepository _characterRelationKindsRepo;
    private readonly CharacterFamilyRelationsRepository _characterFamilyRelationsRepo;

    // v1.3.0 ブラッシュアップ続編：役職系譜（多対多）を編集するためのリポジトリ。
    // 役職タブの [系譜...] ボタン（Designer.cs 側で正規定義）から本リポジトリを使うダイアログが開く。
    private readonly RoleSuccessionsRepository _roleSuccessionsRepo;

    /// <summary>
    /// クレジット系マスタ管理フォームを生成する。Program.cs の DI で各リポジトリを受け取る。
    /// </summary>
    public CreditMastersEditorForm(
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        // v1.2.4: 声優キャスティング専用リポジトリは撤去（character_voice_castings 廃止）。
        RolesRepository rolesRepo,
        // v1.2.0 工程 H-10：旧 SeriesRoleFormatOverridesRepository → RoleTemplatesRepository + CreditKindsRepository に置換。
        RoleTemplatesRepository roleTemplatesRepo,
        CreditKindsRepository creditKindsRepo,
        EpisodeThemeSongsRepository episodeThemeSongsRepo,
        SeriesKindsRepository seriesKindsRepo,
        PartTypesRepository partTypesRepo,
        SeriesRepository seriesRepo,
        EpisodesRepository episodesRepo,
        // v1.2.0 工程 A 追加：マスタ補完（名義・屋号・ロゴ）
        PersonAliasesRepository personAliasesRepo,
        PersonAliasPersonsRepository personAliasPersonsRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        // v1.2.0 工程 C 追加：歌録音ピッカー
        SongRecordingsRepository songRecordingsRepo,
        // v1.2.0 工程 F 追加：キャラクター区分マスタ
        CharacterKindsRepository characterKindsRepo,
        // v1.2.3 追加：ユニットメンバー管理
        PersonAliasMembersRepository personAliasMembersRepo,
        // v1.2.4 追加：プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
        PrecuresRepository precuresRepo,
        CharacterRelationKindsRepository characterRelationKindsRepo,
        CharacterFamilyRelationsRepository characterFamilyRelationsRepo,
        // v1.3.0 ブラッシュアップ続編：役職系譜（多対多）リポジトリ
        RoleSuccessionsRepository roleSuccessionsRepo)
    {
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        // v1.2.4: 声優キャスティングリポジトリの保持は不要（廃止）。
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _roleTemplatesRepo = roleTemplatesRepo ?? throw new ArgumentNullException(nameof(roleTemplatesRepo));
        _creditKindsRepo = creditKindsRepo ?? throw new ArgumentNullException(nameof(creditKindsRepo));
        _episodeThemeSongsRepo = episodeThemeSongsRepo ?? throw new ArgumentNullException(nameof(episodeThemeSongsRepo));
        _seriesKindsRepo = seriesKindsRepo ?? throw new ArgumentNullException(nameof(seriesKindsRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _personAliasPersonsRepo = personAliasPersonsRepo ?? throw new ArgumentNullException(nameof(personAliasPersonsRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _logosRepo = logosRepo ?? throw new ArgumentNullException(nameof(logosRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _songRecordingsRepo = songRecordingsRepo ?? throw new ArgumentNullException(nameof(songRecordingsRepo));
        _characterKindsRepo = characterKindsRepo ?? throw new ArgumentNullException(nameof(characterKindsRepo));
        _personAliasMembersRepo = personAliasMembersRepo ?? throw new ArgumentNullException(nameof(personAliasMembersRepo));

        // v1.2.4 追加：プリキュア本体・キャラクター続柄・家族関係（汎用）
        _precuresRepo = precuresRepo ?? throw new ArgumentNullException(nameof(precuresRepo));
        _characterRelationKindsRepo = characterRelationKindsRepo ?? throw new ArgumentNullException(nameof(characterRelationKindsRepo));
        _characterFamilyRelationsRepo = characterFamilyRelationsRepo ?? throw new ArgumentNullException(nameof(characterFamilyRelationsRepo));

        // v1.3.0 ブラッシュアップ続編：役職系譜
        _roleSuccessionsRepo = roleSuccessionsRepo ?? throw new ArgumentNullException(nameof(roleSuccessionsRepo));

        InitializeComponent();

        // 全グリッドの監査列を自動非表示にする（DataBindingComplete のたびに Visible=false）
        HideAuditColumns(gridPersons);
        HideAuditColumns(gridPersonAliases);
        HideAuditColumns(gridCompanies);
        HideAuditColumns(gridCompanyAliases);
        HideAuditColumns(gridLogos);
        HideAuditColumns(gridCharacters);
        HideAuditColumns(gridCharacterAliases);
        // v1.2.4: gridVoiceCastings 撤去。代わりにプリキュア／続柄／家族関係の 3 グリッドを追加。
        HideAuditColumns(gridPrecures);
        HideAuditColumns(gridCharacterRelationKinds);
        HideAuditColumns(gridCharacterFamilyRelations);
        HideAuditColumns(gridRoles);
        HideAuditColumns(gridRoleOverrides);
        HideAuditColumns(gridEpisodeThemeSongs);
        HideAuditColumns(gridSeriesKinds);
        HideAuditColumns(gridPartTypes);

        // 「未指定」チェックでピッカー無効化を連動させる
        chkCFoundedNull.CheckedChanged += (_, __) => dtCFounded.Enabled = !chkCFoundedNull.Checked;
        chkCDissolvedNull.CheckedChanged += (_, __) => dtCDissolved.Enabled = !chkCDissolvedNull.Checked;
        // v1.2.4: 声優キャスティング撤去に伴い chkVcFromNull / chkVcToNull の連動も撤去。
        chkOvToNull.CheckedChanged += (_, __) => dtOvTo.Enabled = !chkOvToNull.Checked;
        // chkEtsLabelNull / numEtsLabelCompanyAliasId は v1.2.0 工程 H 補修で撤去済み
        // （episode_theme_songs.label_company_alias_id 列を物理削除した）。
        // v1.2.0 工程 A: 名義・屋号・ロゴタブの「未指定」チェック連動
        chkPaFromNull.CheckedChanged += (_, __) => dtPaFrom.Enabled = !chkPaFromNull.Checked;
        chkPaToNull.CheckedChanged += (_, __) => dtPaTo.Enabled = !chkPaToNull.Checked;
        chkCaFromNull.CheckedChanged += (_, __) => dtCaFrom.Enabled = !chkCaFromNull.Checked;
        chkCaToNull.CheckedChanged += (_, __) => dtCaTo.Enabled = !chkCaToNull.Checked;
        chkLgFromNull.CheckedChanged += (_, __) => dtLgFrom.Enabled = !chkLgFromNull.Checked;
        chkLgToNull.CheckedChanged += (_, __) => dtLgTo.Enabled = !chkLgToNull.Checked;
        // v1.2.1: chkCaaFromNull / chkCaaToNull は撤去済み（character_aliases.valid_from/to 列削除に伴う UI 撤去）。

        // 行選択 → 編集パネル反映
        gridPersons.SelectionChanged += (_, __) => OnPersonRowSelected();
        gridCompanies.SelectionChanged += (_, __) => OnCompanyRowSelected();
        gridCharacters.SelectionChanged += (_, __) => OnCharacterRowSelected();
        // v1.2.4: gridVoiceCastings.SelectionChanged は撤去。
        gridRoles.SelectionChanged += (_, __) => OnRoleRowSelected();
        gridRoleOverrides.SelectionChanged += (_, __) => OnRoleOverrideRowSelected();
        gridEpisodeThemeSongs.SelectionChanged += (_, __) => OnEpisodeThemeSongRowSelected();
        gridSeriesKinds.SelectionChanged += (_, __) => OnSeriesKindRowSelected();
        gridPartTypes.SelectionChanged += (_, __) => OnPartTypeRowSelected();
        // v1.2.0 工程 A: 名義・屋号・ロゴタブ
        gridPersonAliases.SelectionChanged += async (_, __) => await OnPersonAliasRowSelectedAsync();
        gridCompanyAliases.SelectionChanged += (_, __) => OnCompanyAliasRowSelected();
        gridLogos.SelectionChanged += (_, __) => OnLogoRowSelected();
        gridCharacterAliases.SelectionChanged += (_, __) => OnCharacterAliasRowSelected();

        // ボタン
        btnNewPerson.Click += (_, __) => ClearPersonForm();
        btnSavePerson.Click += async (_, __) => await SavePersonAsync();
        btnDeletePerson.Click += async (_, __) => await DeletePersonAsync();

        btnNewCompany.Click += (_, __) => ClearCompanyForm();
        btnSaveCompany.Click += async (_, __) => await SaveCompanyAsync();
        btnDeleteCompany.Click += async (_, __) => await DeleteCompanyAsync();

        btnNewCharacter.Click += (_, __) => ClearCharacterForm();
        btnSaveCharacter.Click += async (_, __) => await SaveCharacterAsync();
        btnDeleteCharacter.Click += async (_, __) => await DeleteCharacterAsync();

        // v1.2.4: 声優キャスティング関連のクリック結線（cboVcCharacter / numVcPersonId /
        // btnNewVoiceCasting / btnSaveVoiceCasting / btnDeleteVoiceCasting）はすべて撤去。
        // 代わりに WirePrecureTabsEvents() で v1.2.4 新規 3 タブのイベントをまとめて結線する
        // （実装は CreditMastersEditorForm.PrecureTabs.cs）。
        WirePrecureTabsEvents();

        btnSaveRole.Click += async (_, __) => await SaveRoleAsync();
        btnDeleteRole.Click += async (_, __) => await DeleteRoleAsync();

        cboOvSeries.SelectedIndexChanged += async (_, __) => await ReloadRoleOverridesAsync();
        btnSaveOverride.Click += async (_, __) => await SaveRoleOverrideAsync();
        btnDeleteOverride.Click += async (_, __) => await DeleteRoleOverrideAsync();
        // v1.2.0 工程 H-13：「+ 新規追加」ボタンを結線。Designer 側で Name="btnNewOverride" を付与しているので
        // tabRoleOverrides 配下から名前で検索して取り出し、Click イベントを結線する。
        // フィールドとして宣言しないことでフィールド一覧の肥大化を抑える狙い。
        var btnNewOverride = tabRoleOverrides.Controls.Find("btnNewOverride", searchAllChildren: true).FirstOrDefault() as Button;
        if (btnNewOverride is not null)
        {
            btnNewOverride.Click += (_, __) => OnNewRoleOverride();
        }

        cboEtsSeries.SelectedIndexChanged += async (_, __) => await ReloadEpisodesForEtsAsync();
        cboEtsEpisode.SelectedIndexChanged += async (_, __) => await ReloadEpisodeThemeSongsAsync();
        btnSaveEts.Click += async (_, __) => await SaveEpisodeThemeSongAsync();
        btnDeleteEts.Click += async (_, __) => await DeleteEpisodeThemeSongAsync();
        // v1.2.0 工程 B' 追加：他話からのコピー
        btnCopyEts.Click += async (_, __) => await OpenEtsCopyDialogAsync();
        // v1.2.0 工程 H-8 追加：範囲コピーボタンを EpisodeThemeSongRangeCopyDialog に結線。
        btnRangeCopyEts.Click += async (_, __) => await OpenEtsRangeCopyDialogAsync();

        // v1.2.0 工程 D 追加：マスタ役職タブの DnD（display_order 並べ替え）
        // DataGridView は AllowDrop / 行ヘッダドラッグの両方を有効化してから、
        // MouseDown / MouseMove / DragEnter / DragOver / DragDrop の 5 イベントで
        // 並べ替え動作を組み立てる。これは WinForms 標準の「行 DnD」が無いための
        // 自前実装で、CreditEditorForm の TreeView DnD と同じ思想。
        gridRoles.AllowDrop = true;
        gridRoles.MouseDown  += GridRoles_MouseDown;
        gridRoles.MouseMove  += GridRoles_MouseMove;
        gridRoles.DragEnter  += GridRoles_DragEnter;
        gridRoles.DragOver   += GridRoles_DragOver;
        gridRoles.DragDrop   += async (s, e) => await GridRoles_DragDropAsync(s, e);

        // v1.2.0 工程 D 追加：マスタ主題歌タブの DnD（INSERT 行のみ insert_seq 並べ替え）
        gridEpisodeThemeSongs.AllowDrop = true;
        gridEpisodeThemeSongs.MouseDown  += GridEts_MouseDown;
        gridEpisodeThemeSongs.MouseMove  += GridEts_MouseMove;
        gridEpisodeThemeSongs.DragEnter  += GridEts_DragEnter;
        gridEpisodeThemeSongs.DragOver   += GridEts_DragOver;
        gridEpisodeThemeSongs.DragDrop   += async (s, e) => await GridEts_DragDropAsync(s, e);

        btnSaveSeriesKind.Click += async (_, __) => await SaveSeriesKindAsync();
        btnDeleteSeriesKind.Click += async (_, __) => await DeleteSeriesKindAsync();

        btnSavePartType.Click += async (_, __) => await SavePartTypeAsync();
        btnDeletePartType.Click += async (_, __) => await DeletePartTypeAsync();

        // v1.2.0 工程 A: 人物名義タブ
        cboPaPerson.SelectedIndexChanged += async (_, __) => await ReloadPersonAliasesAsync();
        btnNewPersonAlias.Click += (_, __) => ClearPersonAliasForm();
        btnSavePersonAlias.Click += async (_, __) => await SavePersonAliasAsync();
        btnDeletePersonAlias.Click += async (_, __) => await DeletePersonAliasAsync();
        btnAddJointPerson.Click += async (_, __) => await AddJointPersonAsync();
        btnRemoveJointPerson.Click += async (_, __) => await RemoveJointPersonAsync();
        // v1.2.1 名寄せ機能：選択中の人物名義に対する付け替え／改名ハンドラ
        btnReassignPersonAlias.Click += async (_, __) => await OnReassignPersonAliasClickAsync();
        btnRenamePersonAlias.Click += async (_, __) => await OnRenamePersonAliasClickAsync();
        // v1.2.3 追加：ユニットメンバー編集ボタン（PersonAliasMembersEditDialog を開く）
        btnPaEditMembers.Click += async (_, __) => await OnEditPersonAliasMembersAsync();

        // v1.2.0 工程 A: 企業屋号タブ
        cboCaCompany.SelectedIndexChanged += async (_, __) => await ReloadCompanyAliasesAsync();
        btnNewCompanyAlias.Click += (_, __) => ClearCompanyAliasForm();
        btnSaveCompanyAlias.Click += async (_, __) => await SaveCompanyAliasAsync();
        btnDeleteCompanyAlias.Click += async (_, __) => await DeleteCompanyAliasAsync();
        // v1.2.1 名寄せ機能：選択中の企業屋号に対する付け替え／改名ハンドラ
        btnReassignCompanyAlias.Click += async (_, __) => await OnReassignCompanyAliasClickAsync();
        btnRenameCompanyAlias.Click += async (_, __) => await OnRenameCompanyAliasClickAsync();

        // v1.2.0 工程 A: ロゴタブ
        cboLgCompany.SelectedIndexChanged += async (_, __) => await ReloadLgCompanyAliasComboAsync();
        cboLgCompanyAlias.SelectedIndexChanged += async (_, __) => await ReloadLogosAsync();
        btnNewLogo.Click += (_, __) => ClearLogoForm();
        btnSaveLogo.Click += async (_, __) => await SaveLogoAsync();
        btnDeleteLogo.Click += async (_, __) => await DeleteLogoAsync();

        // v1.2.0 工程 A: キャラクター名義タブ
        cboCaaCharacter.SelectedIndexChanged += async (_, __) => await ReloadCharacterAliasesAsync();
        btnNewCharacterAlias.Click += (_, __) => ClearCharacterAliasForm();
        btnSaveCharacterAlias.Click += async (_, __) => await SaveCharacterAliasAsync();
        btnDeleteCharacterAlias.Click += async (_, __) => await DeleteCharacterAliasAsync();
        // v1.2.1 名寄せ機能：選択中のキャラ名義に対する付け替え／改名ハンドラ
        btnReassignCharacterAlias.Click += async (_, __) => await OnReassignCharacterAliasClickAsync();
        btnRenameCharacterAlias.Click += async (_, __) => await OnRenameCharacterAliasClickAsync();

        // v1.2.0 工程 C: 各タブの「検索...」ボタンにピッカーダイアログを結線
        // v1.2.4: btnPickVcPersonId（声優キャスティングタブ用）は撤去。
        btnPickEtsSongRecordingId.Click += (_, __) => OpenSongRecordingPicker(numEtsSongRecordingId);
        // btnPickEtsLabelCompanyAliasId は v1.2.0 工程 H 補修で撤去済み（label_company_alias_id 列の物理削除）。
        // 人物名義タブ：前任／後任は「同じ人物配下のみ」、共同名義 person_id は人物全体
        btnPickPaPredecessor.Click += (_, __) => OpenPersonAliasPicker(
            numPaPredecessor,
            scopePersonId: cboPaPerson.SelectedValue is int pid1 ? pid1 : null);
        btnPickPaSuccessor.Click += (_, __) => OpenPersonAliasPicker(
            numPaSuccessor,
            scopePersonId: cboPaPerson.SelectedValue is int pid2 ? pid2 : null);
        btnPickPaJointPersonId.Click += (_, __) => OpenPersonPicker(numPaJointPersonId);
        // 企業屋号タブ：前任／後任は「同じ企業配下のみ」
        btnPickCaPredecessor.Click += (_, __) => OpenCompanyAliasPicker(
            numCaPredecessor,
            scopeCompanyId: cboCaCompany.SelectedValue is int cid1 ? cid1 : null);
        btnPickCaSuccessor.Click += (_, __) => OpenCompanyAliasPicker(
            numCaSuccessor,
            scopeCompanyId: cboCaCompany.SelectedValue is int cid2 ? cid2 : null);

        Load += async (_, __) => await LoadAllAsync();

        // v1.3.0 ブラッシュアップ続編：[系譜…] ボタン（Designer.cs 側で正規定義）の Click ハンドラを購読。
        // ボタン自体の生成は Designer.cs 側で行われているので、ここではイベント購読のみ。
        btnEditRoleSuccessions.Click += async (_, _) => await OnEditRoleSuccessionsClickAsync();
    }

    /// <summary>
    /// 全タブの初期データを 1 度に読み込む。コンボの選択肢初期化もここで行う。
    /// </summary>
    private async Task LoadAllAsync()
    {
        try
        {
            // v1.2.1: キャラクター区分コンボをマスタからバインド（旧コードはハードコードだった）。
            // gridCharacters のバインドより前に実行することで、行選択時の OnCharacterRowSelected が
            // 適切に既存値を選択できるようにする。
            await BindCharacterKindComboAsync().ConfigureAwait(true);

            // 個別マスタ
            gridPersons.DataSource = (await _personsRepo.GetAllAsync()).ToList();
            gridCompanies.DataSource = (await _companiesRepo.GetAllAsync()).ToList();
            gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();
            gridSeriesKinds.DataSource = (await _seriesKindsRepo.GetAllAsync()).ToList();
            gridPartTypes.DataSource = (await _partTypesRepo.GetAllAsync()).ToList();

            // v1.2.4: 声優キャスティングタブの初期化（cboVcCharacter / ReloadVoiceCastingsAsync）は撤去。
            // 代わりにプリキュア／続柄／家族関係の 3 タブを初期化する
            // （LoadPrecuresTabAsync / LoadCharacterRelationKindsTabAsync /
            //  LoadCharacterFamilyRelationsTabAsync は CreditMastersEditorForm.PrecureTabs.cs に定義）。
            // characters はこの後の他タブ（キャラクター名義タブ等）でも再利用するため
            // ここで取得しておく。
            var characters = await _charactersRepo.GetAllAsync();
            await LoadPrecuresTabAsync().ConfigureAwait(true);
            await LoadCharacterRelationKindsTabAsync().ConfigureAwait(true);
            await LoadCharacterFamilyRelationsTabAsync(characters).ConfigureAwait(true);

            // 役職テンプレートタブ：上部の「役職フィルタ」コンボには役職一覧をバインドする。
            // v1.2.0 工程 H-12 修正：旧来 cboOvSeries はシリーズコンボとして使われていたが、
            // H-10 の役職テンプレ統合化に伴い「役職フィルタ」用途に変わった。フィールド名は互換のため
            // cboOvSeries のまま流用しているが、実体は役職コンボ（DataSource = 役職リスト、
            // ValueMember = role_code: string）。下部の cboOvRole は詳細編集パネル側の役職セレクタ。
            // 詳細パネル側のシリーズ選択は cboOvTemplateSeries が担う（H-10 で新設）。
            var rolesForOv = (await _rolesRepo.GetAllAsync())
                .Select(r => new CodeLabel(r.RoleCode, $"{r.RoleCode}  {r.NameJa}"))
                .ToList();
            cboOvSeries.DisplayMember = "Label";
            cboOvSeries.ValueMember = "Id";
            cboOvSeries.DataSource = rolesForOv;

            // 詳細編集パネル下部の役職コンボには同じ役職リストをバインド
            cboOvRole.DisplayMember = "Label";
            cboOvRole.ValueMember = "Id";
            cboOvRole.DataSource = (await _rolesRepo.GetAllAsync())
                .Select(r => new CodeLabel(r.RoleCode, $"{r.RoleCode}  {r.NameJa}"))
                .ToList();

            // 詳細編集パネル下部のシリーズコンボ（cboOvTemplateSeries）には
            // 「（既定 / 全シリーズ）」の選択肢 + 全シリーズをバインドする。
            // ID=null（既定）と ID=シリーズID の混在を扱うため、IdLabelNullable を使う。
            var allSeries = await _seriesRepo.GetAllAsync();
            var templateSeriesItems = new List<IdLabelNullable>
            {
                new IdLabelNullable(null, "（既定 / 全シリーズ）")
            };
            templateSeriesItems.AddRange(
                allSeries.Select(s => new IdLabelNullable(s.SeriesId, $"#{s.SeriesId}  {s.Title}")));
            cboOvTemplateSeries.DisplayMember = "Label";
            cboOvTemplateSeries.ValueMember = "Id";
            cboOvTemplateSeries.DataSource = templateSeriesItems;

            if (rolesForOv.Count > 0) await ReloadRoleOverridesAsync();

            // エピソード主題歌タブ：シリーズコンボへバインド（エピソードはシリーズ選択後に絞り込み）
            // v1.2.0 工程 H-12 修正：旧来このタブ用 seriesItems は cboOvSeries 用に作っていたが、
            // 役職テンプレタブ側のコンボが役職用に変わったので、ここで改めて allSeries から作り直す。
            var seriesItems = allSeries.Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}")).ToList();
            cboEtsSeries.DisplayMember = "Label";
            cboEtsSeries.ValueMember = "Id";
            cboEtsSeries.DataSource = seriesItems.Select(x => new IdLabel(x.Id, x.Label)).ToList();
            // v1.2.0 工程 B': 編集パネル既定値（本放送フラグは OFF、種別は OP）
            chkEtsBroadcastOnly.Checked = false;
            cboEtsThemeKind.SelectedItem = "OP";
            if (seriesItems.Count > 0) await ReloadEpisodesForEtsAsync();

            // v1.2.0 工程 A: 人物名義タブのコンボ初期化（人物リスト）
            var persons = await _personsRepo.GetAllAsync();
            cboPaPerson.DisplayMember = "Label";
            cboPaPerson.ValueMember = "Id";
            cboPaPerson.DataSource = persons
                .Select(p => new IdLabel(p.PersonId, $"#{p.PersonId}  {p.FullName}"))
                .ToList();
            if (persons.Count > 0) await ReloadPersonAliasesAsync();

            // v1.2.0 工程 A: 企業屋号タブのコンボ初期化（企業リスト）
            var companies = await _companiesRepo.GetAllAsync();
            var companyItems = companies
                .Select(c => new IdLabel(c.CompanyId, $"#{c.CompanyId}  {c.Name}"))
                .ToList();
            cboCaCompany.DisplayMember = "Label";
            cboCaCompany.ValueMember = "Id";
            cboCaCompany.DataSource = companyItems;
            if (companies.Count > 0) await ReloadCompanyAliasesAsync();

            // v1.2.0 工程 A: ロゴタブのコンボ初期化（企業リスト→屋号は連動取得）
            cboLgCompany.DisplayMember = "Label";
            cboLgCompany.ValueMember = "Id";
            cboLgCompany.DataSource = companyItems
                .Select(x => new IdLabel(x.Id, x.Label)).ToList();
            if (companies.Count > 0) await ReloadLgCompanyAliasComboAsync();

            // v1.2.0 工程 A: キャラクター名義タブのコンボ初期化（キャラリスト）
            cboCaaCharacter.DisplayMember = "Label";
            cboCaaCharacter.ValueMember = "Id";
            cboCaaCharacter.DataSource = characters
                .Select(c => new IdLabel(c.CharacterId, $"#{c.CharacterId}  {c.Name}"))
                .ToList();
            if (characters.Count > 0) await ReloadCharacterAliasesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // ヘルパー
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// グリッドの監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）を
    /// データバインド完了時に自動的に非表示にする（既存 MastersEditorForm と同方針）。
    /// </summary>
    private static void HideAuditColumns(DataGridView grid)
    {
        grid.DataBindingComplete += (_, __) =>
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                var n = col.DataPropertyName;
                if (n is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy")
                {
                    col.Visible = false;
                }
            }
        };
    }

    /// <summary>例外をユーザーに通知する共通ハンドラ。</summary>
    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>空文字列を NULL に変換するヘルパ。</summary>
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>コンボ用の (int Id, string Label) ペア。</summary>
    private sealed class IdLabel
    {
        public int Id { get; }
        public string Label { get; }
        public IdLabel(int id, string label) { Id = id; Label = label; }
    }

    /// <summary>コンボ用の (string Code, string Label) ペア。</summary>
    private sealed class CodeLabel
    {
        public string Id { get; }
        public string Label { get; }
        public CodeLabel(string code, string label) { Id = code; Label = label; }
    }

    /// <summary>
    /// コンボ用の (int? Id, string Label) ペア。役職テンプレートタブの「（既定 / 全シリーズ）」エントリ
    /// （Id=null）と特定シリーズエントリ（Id=int）を同じ DataSource に混在させるために使う
    /// （v1.2.0 工程 H-10 / H-12 で導入）。
    /// </summary>
    private sealed class IdLabelNullable
    {
        public int? Id { get; }
        public string Label { get; }
        public IdLabelNullable(int? id, string label) { Id = id; Label = label; }
    }

    // ────────────────────────────────────────────────────────────────────
    // 人物タブ
    // ────────────────────────────────────────────────────────────────────

    private void OnPersonRowSelected()
    {
        if (gridPersons.CurrentRow?.DataBoundItem is Person p)
        {
            txtPFamily.Text = p.FamilyName ?? "";
            txtPGiven.Text = p.GivenName ?? "";
            txtPFullName.Text = p.FullName;
            txtPFullNameKana.Text = p.FullNameKana ?? "";
            txtPNameEn.Text = p.NameEn ?? "";
            txtPNotes.Text = p.Notes ?? "";
        }
    }

    private void ClearPersonForm()
    {
        gridPersons.ClearSelection();
        txtPFamily.Text = ""; txtPGiven.Text = "";
        txtPFullName.Text = ""; txtPFullNameKana.Text = "";
        txtPNameEn.Text = ""; txtPNotes.Text = "";
    }

    private async Task SavePersonAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtPFullName.Text))
            { MessageBox.Show(this, "フルネームは必須です。"); return; }

            // 選択行が無い、または「新規」直後はインサート、それ以外は選択行 ID をキーに更新
            if (gridPersons.CurrentRow?.DataBoundItem is Person current && current.PersonId > 0
                && gridPersons.SelectedRows.Count > 0)
            {
                current.FamilyName = NullIfEmpty(txtPFamily.Text);
                current.GivenName = NullIfEmpty(txtPGiven.Text);
                current.FullName = txtPFullName.Text.Trim();
                current.FullNameKana = NullIfEmpty(txtPFullNameKana.Text);
                current.NameEn = NullIfEmpty(txtPNameEn.Text);
                current.Notes = NullIfEmpty(txtPNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _personsRepo.UpdateAsync(current);
            }
            else
            {
                var p = new Person
                {
                    FamilyName = NullIfEmpty(txtPFamily.Text),
                    GivenName = NullIfEmpty(txtPGiven.Text),
                    FullName = txtPFullName.Text.Trim(),
                    FullNameKana = NullIfEmpty(txtPFullNameKana.Text),
                    NameEn = NullIfEmpty(txtPNameEn.Text),
                    Notes = NullIfEmpty(txtPNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _personsRepo.InsertAsync(p);
            }

            gridPersons.DataSource = (await _personsRepo.GetAllAsync()).ToList();
            // v1.2.0 工程 A: 人物名義タブの人物コンボも追随更新
            cboPaPerson.DataSource = (await _personsRepo.GetAllAsync())
                .Select(x => new IdLabel(x.PersonId, $"#{x.PersonId}  {x.FullName}")).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeletePersonAsync()
    {
        try
        {
            if (gridPersons.CurrentRow?.DataBoundItem is not Person p)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"人物 #{p.PersonId} {p.FullName} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _personsRepo.SoftDeleteAsync(p.PersonId, Environment.UserName);
            gridPersons.DataSource = (await _personsRepo.GetAllAsync()).ToList();
            ClearPersonForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // 企業タブ
    // ────────────────────────────────────────────────────────────────────

    private void OnCompanyRowSelected()
    {
        if (gridCompanies.CurrentRow?.DataBoundItem is Company c)
        {
            txtCName.Text = c.Name;
            txtCNameKana.Text = c.NameKana ?? "";
            txtCNameEn.Text = c.NameEn ?? "";
            SetDateOrNull(dtCFounded, chkCFoundedNull, c.FoundedDate);
            SetDateOrNull(dtCDissolved, chkCDissolvedNull, c.DissolvedDate);
            txtCNotes.Text = c.Notes ?? "";
        }
    }

    private void ClearCompanyForm()
    {
        gridCompanies.ClearSelection();
        txtCName.Text = ""; txtCNameKana.Text = ""; txtCNameEn.Text = "";
        chkCFoundedNull.Checked = true; chkCDissolvedNull.Checked = true;
        txtCNotes.Text = "";
    }

    private async Task SaveCompanyAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtCName.Text))
            { MessageBox.Show(this, "正式名称は必須です。"); return; }

            if (gridCompanies.CurrentRow?.DataBoundItem is Company current && current.CompanyId > 0
                && gridCompanies.SelectedRows.Count > 0)
            {
                current.Name = txtCName.Text.Trim();
                current.NameKana = NullIfEmpty(txtCNameKana.Text);
                current.NameEn = NullIfEmpty(txtCNameEn.Text);
                current.FoundedDate = chkCFoundedNull.Checked ? null : dtCFounded.Value.Date;
                current.DissolvedDate = chkCDissolvedNull.Checked ? null : dtCDissolved.Value.Date;
                current.Notes = NullIfEmpty(txtCNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _companiesRepo.UpdateAsync(current);
            }
            else
            {
                var c = new Company
                {
                    Name = txtCName.Text.Trim(),
                    NameKana = NullIfEmpty(txtCNameKana.Text),
                    NameEn = NullIfEmpty(txtCNameEn.Text),
                    FoundedDate = chkCFoundedNull.Checked ? null : dtCFounded.Value.Date,
                    DissolvedDate = chkCDissolvedNull.Checked ? null : dtCDissolved.Value.Date,
                    Notes = NullIfEmpty(txtCNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _companiesRepo.InsertAsync(c);
            }
            gridCompanies.DataSource = (await _companiesRepo.GetAllAsync()).ToList();
            // v1.2.0 工程 A: 企業屋号タブ・ロゴタブの企業コンボも追随更新
            var refreshedCompanies = (await _companiesRepo.GetAllAsync())
                .Select(x => new IdLabel(x.CompanyId, $"#{x.CompanyId}  {x.Name}")).ToList();
            cboCaCompany.DataSource = refreshedCompanies;
            cboLgCompany.DataSource = refreshedCompanies
                .Select(x => new IdLabel(x.Id, x.Label)).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteCompanyAsync()
    {
        try
        {
            if (gridCompanies.CurrentRow?.DataBoundItem is not Company c)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"企業 #{c.CompanyId} {c.Name} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _companiesRepo.SoftDeleteAsync(c.CompanyId, Environment.UserName);
            gridCompanies.DataSource = (await _companiesRepo.GetAllAsync()).ToList();
            ClearCompanyForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>DateTimePicker と「未指定」チェックの値を nullable date から復元する。</summary>
    private static void SetDateOrNull(DateTimePicker picker, CheckBox nullCheck, DateTime? value)
    {
        if (value.HasValue)
        {
            picker.Value = value.Value;
            nullCheck.Checked = false;
            picker.Enabled = true;
        }
        else
        {
            nullCheck.Checked = true;
            picker.Enabled = false;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // キャラクタータブ
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 区分コンボにバインドする項目クラス（v1.2.1）。
    /// CharacterKindsRepository.GetAllAsync() の結果を「コード — 表示名」形式で表示する。
    /// </summary>
    private sealed class CharacterKindComboItem
    {
        /// <summary>選択値の実体（ValueMember="KindCode"）。</summary>
        public string KindCode { get; init; } = "";
        /// <summary>表示用文字列（DisplayMember="Display"、例: "MAIN — メイン"）。</summary>
        public string Display { get; init; } = "";
    }

    /// <summary>
    /// 区分コンボにキャラクター区分マスタをバインドする（v1.2.1）。
    /// 旧コードでは "MAIN/SUPPORT/GUEST/MOB/OTHER" の文字列リテラルをハードコードしていたが、
    /// v1.2.0 工程 F でマスタ化されたのに合わせて DataSource バインドに切り替えた。
    /// 起動時とキャラクター区分マスタの編集後に呼び出される。
    /// </summary>
    private async Task BindCharacterKindComboAsync()
    {
        var kinds = await _characterKindsRepo.GetAllAsync().ConfigureAwait(true);
        // CharacterKind モデルの主キープロパティ名は CharacterKindCode（character_kinds.character_kind 列の C# 名）。
        // 当初実装で誤って "KindCode" と書いてビルドエラーになったため、正しいプロパティ名で参照する。
        cboChKind.DataSource = kinds
            .Select(k => new CharacterKindComboItem
            {
                KindCode = k.CharacterKindCode,
                Display = string.IsNullOrEmpty(k.NameJa) ? k.CharacterKindCode : $"{k.CharacterKindCode} — {k.NameJa}"
            })
            .ToList();
    }

    /// <summary>
    /// 現在の区分コンボの選択値（KindCode 文字列）を取得する。SelectedValue が string になっているはず
    /// （ValueMember 設定により）。何らかの理由で取れない場合は既定値 "MAIN" を返す。
    /// </summary>
    private string GetSelectedCharacterKindCode()
    {
        if (cboChKind.SelectedValue is string s && !string.IsNullOrEmpty(s)) return s;
        if (cboChKind.SelectedItem is CharacterKindComboItem item && !string.IsNullOrEmpty(item.KindCode)) return item.KindCode;
        return "MAIN";
    }

    /// <summary>
    /// 指定の KindCode を区分コンボの選択にする（マッチが無ければ無選択）。
    /// </summary>
    private void SetCharacterKindComboValue(string? kindCode)
    {
        if (string.IsNullOrEmpty(kindCode))
        {
            cboChKind.SelectedIndex = -1;
            return;
        }
        // SelectedValue で素直にセットできるはず（ValueMember="KindCode"）。
        cboChKind.SelectedValue = kindCode;
    }

    private void OnCharacterRowSelected()
    {
        if (gridCharacters.CurrentRow?.DataBoundItem is Character c)
        {
            txtChName.Text = c.Name;
            txtChNameKana.Text = c.NameKana ?? "";
            txtChNameEn.Text = c.NameEn ?? "";   // v1.2.4 追加
            // v1.2.1: マスタバインド方式に変更したので SelectedValue 経由でセット。
            SetCharacterKindComboValue(c.CharacterKind);
            txtChNotes.Text = c.Notes ?? "";
        }
    }

    private void ClearCharacterForm()
    {
        gridCharacters.ClearSelection();
        txtChName.Text = ""; txtChNameKana.Text = ""; txtChNameEn.Text = "";   // v1.2.4 追加
        // v1.2.1: ハードコードの "MAIN" 文字列セットから、マスタコード経由のセットに変更。
        SetCharacterKindComboValue("MAIN");
        txtChNotes.Text = "";
    }

    private async Task SaveCharacterAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtChName.Text))
            { MessageBox.Show(this, "名前は必須です。"); return; }
            // v1.2.1: マスタバインド方式に合わせて SelectedValue を取得。
            var kind = GetSelectedCharacterKindCode();

            if (gridCharacters.CurrentRow?.DataBoundItem is Character current && current.CharacterId > 0
                && gridCharacters.SelectedRows.Count > 0)
            {
                current.Name = txtChName.Text.Trim();
                current.NameKana = NullIfEmpty(txtChNameKana.Text);
                current.NameEn = NullIfEmpty(txtChNameEn.Text);   // v1.2.4 追加
                current.CharacterKind = kind;
                current.Notes = NullIfEmpty(txtChNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _charactersRepo.UpdateAsync(current);
            }
            else
            {
                var c = new Character
                {
                    Name = txtChName.Text.Trim(),
                    NameKana = NullIfEmpty(txtChNameKana.Text),
                    NameEn = NullIfEmpty(txtChNameEn.Text),   // v1.2.4 追加
                    CharacterKind = kind,
                    Notes = NullIfEmpty(txtChNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _charactersRepo.InsertAsync(c);
            }
            gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
            // v1.2.4: 声優キャスティングタブの cboVcCharacter は撤去済み。
            // v1.2.0 工程 A: キャラクター名義タブのキャラコンボも追随更新
            cboCaaCharacter.DataSource = (await _charactersRepo.GetAllAsync())
                .Select(x => new IdLabel(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
            // v1.2.4 追加：プリキュアタブの「変身前後の名義コンボ」と家族関係タブの「自分／相手キャラコンボ」も再ロード
            await RefreshPrecureTabComboSourcesAsync().ConfigureAwait(true);
            await RefreshCharacterFamilyTabComboSourcesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteCharacterAsync()
    {
        try
        {
            if (gridCharacters.CurrentRow?.DataBoundItem is not Character c)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"キャラクター #{c.CharacterId} {c.Name} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _charactersRepo.SoftDeleteAsync(c.CharacterId, Environment.UserName);
            gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
            ClearCharacterForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // 役職タブ
    // ────────────────────────────────────────────────────────────────────

    private void OnRoleRowSelected()
    {
        if (gridRoles.CurrentRow?.DataBoundItem is Role r)
        {
            txtRoleCode.Text = r.RoleCode;
            txtRoleNameJa.Text = r.NameJa;
            txtRoleNameEn.Text = r.NameEn ?? "";
            cboRoleFormatKind.SelectedItem = r.RoleFormatKind;
            // v1.2.0 工程 H-10：書式テンプレは「役職テンプレート」タブで編集する。
            numRoleDisplayOrder.Value = r.DisplayOrder ?? 0;
            // v1.3.0 ブラッシュアップ stage 16 Phase 4：役職名非表示フラグの取り込み。
            chkRoleHideRoleNameInCredit.Checked = (r.HideRoleNameInCredit == 1);

            // v1.3.0 ブラッシュアップ続編：[系譜…] ボタン（Designer.cs 側）の活性化と
            // 編集対象の更新。タグに現在行の役職コード／名称を入れる。
            btnEditRoleSuccessions.Tag = (RoleCode: r.RoleCode, RoleNameJa: r.NameJa);
            btnEditRoleSuccessions.Enabled = !string.IsNullOrWhiteSpace(r.RoleCode);
        }
    }

    /// <summary>
    /// [系譜...] ボタン（Designer.cs 側で正規定義）のクリックハンドラ。
    /// 現在編集中の役職を中心に <see cref="RoleSuccessionsEditorDialog"/> を開く。
    /// ダイアログ内で追加・削除した結果は即時 DB 反映されるが、本フォーム側のグリッドは
    /// role_code 自体は変えないので再描画不要。
    /// </summary>
    private async Task OnEditRoleSuccessionsClickAsync()
    {
        if (btnEditRoleSuccessions.Tag is not ValueTuple<string, string> tagTuple)
        {
            // フォールバック：Tag が未設定または型が想定外なら現在選択行から再取得を試みる。
            if (gridRoles.CurrentRow?.DataBoundItem is not Role r) return;
            tagTuple = (r.RoleCode, r.NameJa);
        }

        var (roleCode, roleNameJa) = tagTuple;
        if (string.IsNullOrWhiteSpace(roleCode)) return;

        try
        {
            using var dlg = new Forms.Dialogs.RoleSuccessionsEditorDialog(
                _rolesRepo, _roleSuccessionsRepo, roleCode, roleNameJa);
            dlg.ShowDialog(this);
            // 系譜は roles 本体には影響しないのでグリッド再描画は不要。
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task SaveRoleAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtRoleCode.Text))
            { MessageBox.Show(this, "コードは必須です。"); return; }
            if (string.IsNullOrWhiteSpace(txtRoleNameJa.Text))
            { MessageBox.Show(this, "名称(日)は必須です。"); return; }

            ushort? order = numRoleDisplayOrder.Value > 0 ? (ushort)numRoleDisplayOrder.Value : null;

            var r = new Role
            {
                RoleCode = txtRoleCode.Text.Trim(),
                NameJa = txtRoleNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtRoleNameEn.Text),
                RoleFormatKind = (cboRoleFormatKind.SelectedItem as string) ?? "NORMAL",
                // v1.2.0 工程 H-10：DefaultFormatTemplate プロパティは撤去された。
                DisplayOrder = order,
                // v1.3.0 ブラッシュアップ stage 16 Phase 4：チェック状態を 0/1 に変換して永続化。
                HideRoleNameInCredit = chkRoleHideRoleNameInCredit.Checked ? (byte)1 : (byte)0,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _rolesRepo.UpsertAsync(r);
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();

            // 「役職テンプレート」タブの役職コンボも追随
            cboOvRole.DataSource = (await _rolesRepo.GetAllAsync())
                .Select(x => new CodeLabel(x.RoleCode, $"{x.RoleCode}  {x.NameJa}"))
                .ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteRoleAsync()
    {
        try
        {
            if (gridRoles.CurrentRow?.DataBoundItem is not Role r)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"役職 {r.RoleCode} を削除しますか？（参照されている場合は失敗します）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _rolesRepo.DeleteAsync(r.RoleCode);
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // 役職テンプレートタブ（v1.2.0 工程 H-10：旧「シリーズ書式上書き」を転換）
    // 旧フィールド名 (cboOvSeries / gridRoleOverrides / cboOvRole / txtOvFormatTemplate /
    //                btnSaveOverride / btnDeleteOverride / etc) は流用。中身は role_templates テーブル用。
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 上部の役職コンボ（cboOvSeries にフィールド名を流用）の選択変更時、または初回ロード時に
    /// role_templates から該当役職の全テンプレ（既定 + シリーズ別）をグリッドへロードする
    /// （v1.2.0 工程 H-13 で再設計）。
    /// <para>
    /// SelectedValue 経由は DataSource バインドのタイミング次第で null や型不一致になり得るため、
    /// SelectedItem を直接 CodeLabel にキャストして取得する方式に変更。
    /// </para>
    /// </summary>
    private async Task ReloadRoleOverridesAsync()
    {
        try
        {
            if (cboOvSeries.SelectedItem is not CodeLabel sel || string.IsNullOrEmpty(sel.Id))
            {
                gridRoleOverrides.DataSource = null;
                return;
            }
            string roleCode = sel.Id;
            var rows = await _roleTemplatesRepo.GetByRoleAsync(roleCode);
            // 表示用 DTO（series 名付き）に変換してグリッドへ
            var seriesNameMap = (await _seriesRepo.GetAllAsync()).ToDictionary(s => s.SeriesId, s => s.Title);
            var rowsView = rows.Select(t => new RoleTemplateRow
            {
                TemplateId = t.TemplateId,
                RoleCode = t.RoleCode,
                SeriesId = t.SeriesId,
                SeriesLabel = t.SeriesId.HasValue
                    ? (seriesNameMap.TryGetValue(t.SeriesId.Value, out var nm) ? $"#{t.SeriesId} {nm}" : $"#{t.SeriesId}")
                    : "（既定 / 全シリーズ）",
                FormatTemplate = t.FormatTemplate,
                Notes = t.Notes
            }).ToList();
            gridRoleOverrides.DataSource = rowsView;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// グリッドで行が選択されたら詳細パネルにロード。
    /// v1.2.0 工程 H-12 修正：cboOvTemplateSeries の DataSource は IdLabelNullable（Id は int? 型）に
    /// 切り替えたため、SelectedValue にも int? を渡す。row.SeriesId が null（既定行）なら null を
    /// 渡せば「（既定 / 全シリーズ）」が選ばれる。
    /// </summary>
    /// <summary>
    /// グリッドで行が選択されたら詳細パネルにロード（v1.2.0 工程 H-13 で簡素化）。
    /// 役職コンボ（cboOvRole / cboOvSeries）には触らない：上部の cboOvSeries が現在の編集対象役職を
    /// 表しており、グリッドの全行はその役職に属する。よって行選択ではシリーズコンボとテンプレ・備考
    /// だけを更新する。
    /// </summary>
    private void OnRoleOverrideRowSelected()
    {
        if (gridRoleOverrides.CurrentRow?.DataBoundItem is RoleTemplateRow row)
        {
            // 既定行は SelectedIndex=0、特定シリーズ行は SelectedValue で対応する int を指定。
            if (row.SeriesId is int sid)
            {
                cboOvTemplateSeries.SelectedValue = (int?)sid;
            }
            else
            {
                cboOvTemplateSeries.SelectedIndex = 0; // 「（既定 / 全シリーズ）」エントリ
            }
            // v1.2.0 工程 H-14：DB 由来文字列の改行コードを Windows 形式 (\r\n) に正規化してから TextBox にセット。
            // TextBox は内部的に \r\n 改行が前提のコントロールで、\n 単独の文字列を Text プロパティにセットすると
            // 改行が反映されず 1 行表示になることがある。逆に、ユーザーが Enter で打った改行は \r\n となるため
            // 保存時はそのまま MySQL TEXT 列に格納される。両方向で改行が崩れないよう、表示時に正規化する。
            string fmtForDisplay = (row.FormatTemplate ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            txtOvFormatTemplate.Text = fmtForDisplay;
            txtOvNotes.Text = row.Notes ?? "";
        }
    }

    /// <summary>
    /// 「+ 新規追加」ボタン：詳細パネルをクリアして、新規作成モードにする
    /// （v1.2.0 工程 H-13 で導入）。役職は上部の cboOvSeries の選択中のものをそのまま使う設計のため、
    /// ここでは触らない。シリーズは「（既定 / 全シリーズ）」を初期選択とし、ユーザーが必要なら
    /// 特定シリーズに変更する。
    /// </summary>
    private void OnNewRoleOverride()
    {
        // グリッド選択を解除（既存行を上書きしないように）
        gridRoleOverrides.ClearSelection();
        if (cboOvTemplateSeries.Items.Count > 0) cboOvTemplateSeries.SelectedIndex = 0;
        txtOvFormatTemplate.Clear();
        txtOvNotes.Clear();
        txtOvFormatTemplate.Focus();
    }

    /// <summary>
    /// 「💾 保存 / 更新」ボタン：詳細パネルの値で role_templates を UPSERT する。
    /// v1.2.0 工程 H-13 修正：役職は上部の cboOvSeries（フィルタ兼編集対象）から取得するように変更。
    /// 旧 cboOvRole は使わない（フィールドは互換のため残置、Visible=false）。
    /// </summary>
    private async Task SaveRoleOverrideAsync()
    {
        try
        {
            // 役職は上部のコンボから取得（cboOvSeries は実体は役職コンボ）
            if (cboOvSeries.SelectedItem is not CodeLabel roleSel || string.IsNullOrEmpty(roleSel.Id))
            { MessageBox.Show(this, "上部の「役職」コンボから役職を選択してください。"); return; }
            string roleCode = roleSel.Id;

            if (string.IsNullOrWhiteSpace(txtOvFormatTemplate.Text))
            { MessageBox.Show(this, "書式テンプレは必須です。"); return; }

            // SelectedItem を辿って Id (int?) を取得（SelectedValue 経由だと型変換問題が出るため）。
            int? seriesId = null;
            if (cboOvTemplateSeries.SelectedItem is IdLabelNullable item) seriesId = item.Id;

            var t = new RoleTemplate
            {
                RoleCode = roleCode,
                SeriesId = seriesId,
                FormatTemplate = txtOvFormatTemplate.Text,  // 改行を保持するため Trim しない
                Notes = NullIfEmpty(txtOvNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _roleTemplatesRepo.UpsertAsync(t);
            await ReloadRoleOverridesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「🗑 選択行を削除」ボタン：グリッドの選択行のテンプレを物理削除。
    /// </summary>
    private async Task DeleteRoleOverrideAsync()
    {
        try
        {
            if (gridRoleOverrides.CurrentRow?.DataBoundItem is not RoleTemplateRow row)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            string label = row.SeriesId.HasValue ? $"({row.RoleCode}, series_id={row.SeriesId})" : $"({row.RoleCode}, 既定)";
            if (MessageBox.Show(this,
                $"{label} のテンプレを削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _roleTemplatesRepo.DeleteAsync(row.TemplateId);
            await ReloadRoleOverridesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「役職テンプレート」タブの DataGridView 表示用 DTO（series_id を解決済みのラベルとして持つ）。
    /// </summary>
    private sealed class RoleTemplateRow
    {
        public int TemplateId { get; set; }
        public string RoleCode { get; set; } = "";
        public int? SeriesId { get; set; }
        public string SeriesLabel { get; set; } = "";
        public string FormatTemplate { get; set; } = "";
        public string? Notes { get; set; }
    }

    // ────────────────────────────────────────────────────────────────────
    // エピソード主題歌タブ
    // ────────────────────────────────────────────────────────────────────

    private async Task ReloadEpisodesForEtsAsync()
    {
        try
        {
            if (cboEtsSeries.SelectedValue is not int seriesId) return;
            var eps = await _episodesRepo.GetBySeriesAsync(seriesId);
            cboEtsEpisode.DisplayMember = "Label";
            cboEtsEpisode.ValueMember = "Id";
            cboEtsEpisode.DataSource = eps
                .Select(e => new IdLabel(e.EpisodeId, $"#{e.TotalEpNo ?? 0}  {e.TitleText}"))
                .ToList();
            if (eps.Count > 0) await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task ReloadEpisodeThemeSongsAsync()
    {
        try
        {
            if (cboEtsEpisode.SelectedValue is not int episodeId) return;
            gridEpisodeThemeSongs.DataSource = (await _episodeThemeSongsRepo.GetByEpisodeAsync(episodeId)).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnEpisodeThemeSongRowSelected()
    {
        if (gridEpisodeThemeSongs.CurrentRow?.DataBoundItem is EpisodeThemeSong t)
        {
            // v1.2.0 工程 B': 行選択時に本放送限定フラグもチェックボックスに反映
            chkEtsBroadcastOnly.Checked = t.IsBroadcastOnly;
            cboEtsThemeKind.SelectedItem = t.ThemeKind;
            numEtsInsertSeq.Value = t.Seq;
            numEtsSongRecordingId.Value = t.SongRecordingId;
            // v1.2.0 工程 H 補修：LabelCompanyAliasId の load 処理は撤去（列を物理削除した）。
            txtEtsNotes.Text = t.Notes ?? "";
        }
    }

    private async Task SaveEpisodeThemeSongAsync()
    {
        try
        {
            if (cboEtsEpisode.SelectedValue is not int episodeId)
            { MessageBox.Show(this, "エピソードを選択してください。"); return; }
            // v1.2.0 工程 B': 本放送限定フラグはチェックボックスから取得
            bool isBroadcastOnly = chkEtsBroadcastOnly.Checked;
            string themeKind = (cboEtsThemeKind.SelectedItem as string) ?? "OP";
            byte seq = (byte)numEtsInsertSeq.Value;
            // v1.3.0：旧仕様で「OP/ED は insert_seq=0、INSERT は >=1」だった制約は撤廃。
            // 新仕様の seq は OP/ED/INSERT 区別なくエピソード内の劇中順（1, 2, 3, ...）を表す。
            // 0 が来た場合のみ最小値 1 にフォールバック（PK 重複を避ける程度のガード）。
            if (seq < 1) seq = 1;
            int songRecordingId = (int)numEtsSongRecordingId.Value;
            if (songRecordingId <= 0)
            { MessageBox.Show(this, "song_recording_id を指定してください。"); return; }

            var t = new EpisodeThemeSong
            {
                EpisodeId = episodeId,
                IsBroadcastOnly = isBroadcastOnly,
                ThemeKind = themeKind,
                Seq = seq,
                SongRecordingId = songRecordingId,
                // LabelCompanyAliasId は v1.2.0 工程 H 補修で撤去済み（列ごと物理削除）。
                Notes = NullIfEmpty(txtEtsNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _episodeThemeSongsRepo.UpsertAsync(t);
            await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteEpisodeThemeSongAsync()
    {
        try
        {
            if (gridEpisodeThemeSongs.CurrentRow?.DataBoundItem is not EpisodeThemeSong t)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            string flagLabel = t.IsBroadcastOnly ? "[本放送限定]" : "[全媒体共通]";
            if (MessageBox.Show(this,
                $"エピソード#{t.EpisodeId} {flagLabel} {t.ThemeKind}#{t.Seq} を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            // v1.2.0 工程 B': PK が 4 列に変わったので is_broadcast_only も渡す
            await _episodeThemeSongsRepo.DeleteAsync(t.EpisodeId, t.IsBroadcastOnly, t.ThemeKind, t.Seq);
            await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// v1.2.0 工程 B' 追加：他話からのコピーダイアログを開く。
    /// ダイアログ側はプレビュー段階では DB 書き込みを行わず、「すべて保存」ボタンで初めて
    /// <see cref="EpisodeThemeSongsRepository.BulkUpsertAsync"/> をトランザクションで呼ぶ。
    /// </summary>
    private async Task OpenEtsCopyDialogAsync()
    {
        try
        {
            using var dlg = new EpisodeThemeSongCopyDialog(
                _episodeThemeSongsRepo, _seriesRepo, _episodesRepo);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // 保存が走った後はグリッドを最新化（現在表示中のエピソードと同じ場合は変化が見える）
                await ReloadEpisodeThemeSongsAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 範囲コピーダイアログを起動する（v1.2.0 工程 H-8 で新設）。
    /// 1 話の主題歌を「シリーズ内の連続話数範囲（series_ep_no ベース）」の各エピソードに
    /// 一括投入する用途。例：1 話の OP / ED を 2 話〜49 話に同じ内容で流し込む、等。
    /// </summary>
    private async Task OpenEtsRangeCopyDialogAsync()
    {
        try
        {
            using var dlg = new EpisodeThemeSongRangeCopyDialog(
                _episodeThemeSongsRepo, _episodesRepo, _seriesRepo);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // 範囲コピー実行後、現在表示中のエピソードがコピー範囲内に含まれていれば
                // 値が更新されている可能性があるため、グリッドを最新化する。
                await ReloadEpisodeThemeSongsAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // シリーズ種別タブ
    // ────────────────────────────────────────────────────────────────────

    private void OnSeriesKindRowSelected()
    {
        if (gridSeriesKinds.CurrentRow?.DataBoundItem is SeriesKind k)
        {
            txtSkCode.Text = k.KindCode;
            txtSkNameJa.Text = k.NameJa;
            txtSkNameEn.Text = k.NameEn ?? "";
            cboSkAttachTo.SelectedItem = k.CreditAttachTo;
        }
    }

    private async Task SaveSeriesKindAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtSkCode.Text))
            { MessageBox.Show(this, "コードは必須です。"); return; }
            var k = new SeriesKind
            {
                KindCode = txtSkCode.Text.Trim(),
                NameJa = txtSkNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtSkNameEn.Text),
                CreditAttachTo = (cboSkAttachTo.SelectedItem as string) ?? "EPISODE",
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _seriesKindsRepo.UpsertAsync(k);
            gridSeriesKinds.DataSource = (await _seriesKindsRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteSeriesKindAsync()
    {
        try
        {
            if (gridSeriesKinds.CurrentRow?.DataBoundItem is not SeriesKind k)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"シリーズ種別 {k.KindCode} を削除しますか？（参照中なら失敗）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _seriesKindsRepo.DeleteAsync(k.KindCode);
            gridSeriesKinds.DataSource = (await _seriesKindsRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // パート種別タブ
    // ────────────────────────────────────────────────────────────────────

    private void OnPartTypeRowSelected()
    {
        if (gridPartTypes.CurrentRow?.DataBoundItem is PartType p)
        {
            txtPtCode.Text = p.PartTypeCode;
            txtPtNameJa.Text = p.NameJa;
            txtPtNameEn.Text = p.NameEn ?? "";
            numPtDisplayOrder.Value = p.DisplayOrder ?? 0;
            cboPtDefaultCreditKind.SelectedItem = p.DefaultCreditKind ?? "";
        }
    }

    private async Task SavePartTypeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtPtCode.Text))
            { MessageBox.Show(this, "コードは必須です。"); return; }
            string? defaultKind = cboPtDefaultCreditKind.SelectedItem as string;
            if (string.IsNullOrEmpty(defaultKind)) defaultKind = null;

            var pt = new PartType
            {
                PartTypeCode = txtPtCode.Text.Trim(),
                NameJa = txtPtNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtPtNameEn.Text),
                DisplayOrder = numPtDisplayOrder.Value > 0 ? (byte)numPtDisplayOrder.Value : null,
                DefaultCreditKind = defaultKind,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _partTypesRepo.UpsertAsync(pt);
            gridPartTypes.DataSource = (await _partTypesRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeletePartTypeAsync()
    {
        try
        {
            if (gridPartTypes.CurrentRow?.DataBoundItem is not PartType p)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"パート種別 {p.PartTypeCode} を削除しますか？（参照中なら失敗）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _partTypesRepo.DeleteAsync(p.PartTypeCode);
            gridPartTypes.DataSource = (await _partTypesRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // 人物名義タブ（v1.2.0 工程 A 追加）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>選択中人物に紐づく名義一覧を読み直し、編集パネルを初期化する。</summary>
    private async Task ReloadPersonAliasesAsync()
    {
        try
        {
            if (cboPaPerson.SelectedValue is not int personId) return;
            // PersonAliasesRepository.GetByPersonAsync は中間表 person_alias_persons を JOIN して
            // 当該人物に紐づく alias 一覧を返す（リポジトリ側の責務）。
            gridPersonAliases.DataSource = (await _personAliasesRepo.GetByPersonAsync(personId)).ToList();
            ClearPersonAliasForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択行 → 編集パネル反映 + 共同名義リストの更新を非同期で行う。</summary>
    private async Task OnPersonAliasRowSelectedAsync()
    {
        if (gridPersonAliases.CurrentRow?.DataBoundItem is PersonAlias a)
        {
            txtPaName.Text = a.Name;
            txtPaNameKana.Text = a.NameKana ?? "";
            txtPaNameEn.Text = a.NameEn ?? "";   // v1.2.4 追加
            // v1.2.3 追加：display_text_override を読み込む
            txtPaDisplayOverride.Text = a.DisplayTextOverride ?? "";
            numPaPredecessor.Value = a.PredecessorAliasId ?? 0;
            numPaSuccessor.Value = a.SuccessorAliasId ?? 0;
            SetDateOrNull(dtPaFrom, chkPaFromNull, a.ValidFrom);
            SetDateOrNull(dtPaTo, chkPaToNull, a.ValidTo);
            txtPaNotes.Text = a.Notes ?? "";

            await ReloadJointPersonsAsync(a.AliasId);
        }
    }

    /// <summary>編集パネルを初期状態に戻す。共同名義リストもクリア。</summary>
    private void ClearPersonAliasForm()
    {
        gridPersonAliases.ClearSelection();
        txtPaName.Text = ""; txtPaNameKana.Text = ""; txtPaNameEn.Text = "";   // v1.2.4 追加
        // v1.2.3 追加：display_text_override も初期化
        txtPaDisplayOverride.Text = "";
        numPaPredecessor.Value = 0; numPaSuccessor.Value = 0;
        chkPaFromNull.Checked = true; chkPaToNull.Checked = true;
        txtPaNotes.Text = "";
        lstPaJointPersons.Items.Clear();
        numPaJointPersonId.Value = 0;
    }

    /// <summary>共同名義リスト（中間表 person_alias_persons）を再表示する。</summary>
    private async Task ReloadJointPersonsAsync(int aliasId)
    {
        try
        {
            lstPaJointPersons.Items.Clear();
            var rels = await _personAliasPersonsRepo.GetByAliasAsync(aliasId);
            foreach (var r in rels)
            {
                // 個別に GetByIdAsync を呼んで人物名を取得する（共同名義は通常 1 人だけなのでコスト無視）
                var p = await _personsRepo.GetByIdAsync(r.PersonId);
                var label = p is null
                    ? $"#{r.PersonId} (該当なし)  seq={r.PersonSeq}"
                    : $"#{r.PersonId}  {p.FullName}  seq={r.PersonSeq}";
                lstPaJointPersons.Items.Add(new JointPersonItem(r.PersonId, label));
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>名義の新規追加または更新。新規の場合は中間表に主人物を seq=1 で自動投入する。</summary>
    private async Task SavePersonAliasAsync()
    {
        try
        {
            if (cboPaPerson.SelectedValue is not int personId)
            { MessageBox.Show(this, "人物を選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtPaName.Text))
            { MessageBox.Show(this, "名義名は必須です。"); return; }

            int? pred = numPaPredecessor.Value > 0 ? (int)numPaPredecessor.Value : null;
            int? succ = numPaSuccessor.Value > 0 ? (int)numPaSuccessor.Value : null;

            if (gridPersonAliases.CurrentRow?.DataBoundItem is PersonAlias current
                && current.AliasId > 0 && gridPersonAliases.SelectedRows.Count > 0)
            {
                // 既存名義の更新（中間表は触らない。共同名義の追加・解除は専用ボタンで行う）
                current.Name = txtPaName.Text.Trim();
                current.NameKana = NullIfEmpty(txtPaNameKana.Text);
                current.NameEn = NullIfEmpty(txtPaNameEn.Text);   // v1.2.4 追加
                // v1.2.3 追加：display_text_override の保存
                current.DisplayTextOverride = NullIfEmpty(txtPaDisplayOverride.Text);
                current.PredecessorAliasId = pred;
                current.SuccessorAliasId = succ;
                current.ValidFrom = chkPaFromNull.Checked ? null : dtPaFrom.Value.Date;
                current.ValidTo = chkPaToNull.Checked ? null : dtPaTo.Value.Date;
                current.Notes = NullIfEmpty(txtPaNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _personAliasesRepo.UpdateAsync(current);
            }
            else
            {
                // 新規名義の挿入。InsertAsync 戻り値の AliasId を中間表へ反映する。
                var a = new PersonAlias
                {
                    Name = txtPaName.Text.Trim(),
                    NameKana = NullIfEmpty(txtPaNameKana.Text),
                    NameEn = NullIfEmpty(txtPaNameEn.Text),   // v1.2.4 追加
                    // v1.2.3 追加：display_text_override の保存
                    DisplayTextOverride = NullIfEmpty(txtPaDisplayOverride.Text),
                    PredecessorAliasId = pred,
                    SuccessorAliasId = succ,
                    ValidFrom = chkPaFromNull.Checked ? null : dtPaFrom.Value.Date,
                    ValidTo = chkPaToNull.Checked ? null : dtPaTo.Value.Date,
                    Notes = NullIfEmpty(txtPaNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                int newAliasId = await _personAliasesRepo.InsertAsync(a);

                // 主人物との紐付けを seq=1 で中間表に登録（共同名義の追加は別ボタンから行う）
                await _personAliasPersonsRepo.UpsertAsync(new PersonAliasPerson
                {
                    AliasId = newAliasId,
                    PersonId = personId,
                    PersonSeq = 1
                });
            }
            await ReloadPersonAliasesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中の名義を論理削除する（中間表は ON DELETE で連鎖削除されないため注意：
    /// 名義側を SoftDelete するのみ）。</summary>
    private async Task DeletePersonAliasAsync()
    {
        try
        {
            if (gridPersonAliases.CurrentRow?.DataBoundItem is not PersonAlias a)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"名義 #{a.AliasId} {a.Name} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _personAliasesRepo.SoftDeleteAsync(a.AliasId, Environment.UserName);
            await ReloadPersonAliasesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>共同名義人物を中間表に追加する。person_seq は既存最大値 + 1 で自動採番。</summary>
    private async Task AddJointPersonAsync()
    {
        try
        {
            if (gridPersonAliases.CurrentRow?.DataBoundItem is not PersonAlias a)
            { MessageBox.Show(this, "対象の名義行を選択してください。"); return; }
            int newPersonId = (int)numPaJointPersonId.Value;
            if (newPersonId <= 0)
            { MessageBox.Show(this, "追加する person_id を入力してください。"); return; }
            // 該当人物の存在チェック（不存在なら FK 違反になるが、事前案内のため）
            var p = await _personsRepo.GetByIdAsync(newPersonId);
            if (p is null)
            { MessageBox.Show(this, $"person_id={newPersonId} は存在しません。"); return; }

            // 既存中間表から最大 seq を取得して + 1（既に同 person_id が居る場合は UPSERT になり seq が更新される）
            var existing = await _personAliasPersonsRepo.GetByAliasAsync(a.AliasId);
            byte nextSeq = (byte)(existing.Count == 0 ? 1 : existing.Max(x => x.PersonSeq) + 1);
            // 既に当該 person_id が中間表に居る場合はその seq を保つ
            var found = existing.FirstOrDefault(x => x.PersonId == newPersonId);
            if (found is not null) nextSeq = found.PersonSeq;

            await _personAliasPersonsRepo.UpsertAsync(new PersonAliasPerson
            {
                AliasId = a.AliasId,
                PersonId = newPersonId,
                PersonSeq = nextSeq
            });
            await ReloadJointPersonsAsync(a.AliasId);
            numPaJointPersonId.Value = 0;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中の共同名義人物を中間表から外す。最後の 1 人を解除しようとした場合は警告。</summary>
    private async Task RemoveJointPersonAsync()
    {
        try
        {
            if (gridPersonAliases.CurrentRow?.DataBoundItem is not PersonAlias a)
            { MessageBox.Show(this, "対象の名義行を選択してください。"); return; }
            if (lstPaJointPersons.SelectedItem is not JointPersonItem item)
            { MessageBox.Show(this, "解除する人物を共同名義リストから選択してください。"); return; }
            if (lstPaJointPersons.Items.Count <= 1)
            { MessageBox.Show(this, "最後の 1 人は解除できません（名義そのものを削除してください）。"); return; }

            await _personAliasPersonsRepo.DeleteAsync(a.AliasId, item.PersonId);
            await ReloadJointPersonsAsync(a.AliasId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>共同名義リストの 1 行表示用の (PersonId, ラベル) ペア。</summary>
    private sealed class JointPersonItem
    {
        public int PersonId { get; }
        public string Label { get; }
        public JointPersonItem(int personId, string label) { PersonId = personId; Label = label; }
        public override string ToString() => Label;
    }

    // ────────────────────────────────────────────────────────────────────
    // 企業屋号タブ（v1.2.0 工程 A 追加）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>選択中企業に紐づく屋号一覧を読み直す。</summary>
    private async Task ReloadCompanyAliasesAsync()
    {
        try
        {
            if (cboCaCompany.SelectedValue is not int companyId) return;
            gridCompanyAliases.DataSource = (await _companyAliasesRepo.GetByCompanyAsync(companyId)).ToList();
            ClearCompanyAliasForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnCompanyAliasRowSelected()
    {
        if (gridCompanyAliases.CurrentRow?.DataBoundItem is CompanyAlias a)
        {
            txtCaName.Text = a.Name;
            txtCaNameKana.Text = a.NameKana ?? "";
            txtCaNameEn.Text = a.NameEn ?? "";   // v1.2.4 追加
            numCaPredecessor.Value = a.PredecessorAliasId ?? 0;
            numCaSuccessor.Value = a.SuccessorAliasId ?? 0;
            SetDateOrNull(dtCaFrom, chkCaFromNull, a.ValidFrom);
            SetDateOrNull(dtCaTo, chkCaToNull, a.ValidTo);
            txtCaNotes.Text = a.Notes ?? "";
        }
    }

    private void ClearCompanyAliasForm()
    {
        gridCompanyAliases.ClearSelection();
        txtCaName.Text = ""; txtCaNameKana.Text = ""; txtCaNameEn.Text = "";   // v1.2.4 追加
        numCaPredecessor.Value = 0; numCaSuccessor.Value = 0;
        chkCaFromNull.Checked = true; chkCaToNull.Checked = true;
        txtCaNotes.Text = "";
    }

    private async Task SaveCompanyAliasAsync()
    {
        try
        {
            if (cboCaCompany.SelectedValue is not int companyId)
            { MessageBox.Show(this, "企業を選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtCaName.Text))
            { MessageBox.Show(this, "屋号名は必須です。"); return; }

            int? pred = numCaPredecessor.Value > 0 ? (int)numCaPredecessor.Value : null;
            int? succ = numCaSuccessor.Value > 0 ? (int)numCaSuccessor.Value : null;

            if (gridCompanyAliases.CurrentRow?.DataBoundItem is CompanyAlias current
                && current.AliasId > 0 && gridCompanyAliases.SelectedRows.Count > 0)
            {
                current.CompanyId = companyId;
                current.Name = txtCaName.Text.Trim();
                current.NameKana = NullIfEmpty(txtCaNameKana.Text);
                current.NameEn = NullIfEmpty(txtCaNameEn.Text);   // v1.2.4 追加
                current.PredecessorAliasId = pred;
                current.SuccessorAliasId = succ;
                current.ValidFrom = chkCaFromNull.Checked ? null : dtCaFrom.Value.Date;
                current.ValidTo = chkCaToNull.Checked ? null : dtCaTo.Value.Date;
                current.Notes = NullIfEmpty(txtCaNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _companyAliasesRepo.UpdateAsync(current);
            }
            else
            {
                var a = new CompanyAlias
                {
                    CompanyId = companyId,
                    Name = txtCaName.Text.Trim(),
                    NameKana = NullIfEmpty(txtCaNameKana.Text),
                    NameEn = NullIfEmpty(txtCaNameEn.Text),   // v1.2.4 追加
                    PredecessorAliasId = pred,
                    SuccessorAliasId = succ,
                    ValidFrom = chkCaFromNull.Checked ? null : dtCaFrom.Value.Date,
                    ValidTo = chkCaToNull.Checked ? null : dtCaTo.Value.Date,
                    Notes = NullIfEmpty(txtCaNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _companyAliasesRepo.InsertAsync(a);
            }
            await ReloadCompanyAliasesAsync();
            // ロゴタブの屋号コンボも追随更新（同企業を見ている場合に新屋号が即座に選べるように）
            await ReloadLgCompanyAliasComboAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteCompanyAliasAsync()
    {
        try
        {
            if (gridCompanyAliases.CurrentRow?.DataBoundItem is not CompanyAlias a)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"屋号 #{a.AliasId} {a.Name} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _companyAliasesRepo.SoftDeleteAsync(a.AliasId, Environment.UserName);
            await ReloadCompanyAliasesAsync();
            await ReloadLgCompanyAliasComboAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // ロゴタブ（v1.2.0 工程 A 追加）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>企業選択に連動して屋号コンボを再構築する。</summary>
    private async Task ReloadLgCompanyAliasComboAsync()
    {
        try
        {
            if (cboLgCompany.SelectedValue is not int companyId)
            {
                cboLgCompanyAlias.DataSource = new List<IdLabel>();
                gridLogos.DataSource = new List<Logo>();
                return;
            }
            var aliases = await _companyAliasesRepo.GetByCompanyAsync(companyId);
            cboLgCompanyAlias.DisplayMember = "Label";
            cboLgCompanyAlias.ValueMember = "Id";
            cboLgCompanyAlias.DataSource = aliases
                .Select(a => new IdLabel(a.AliasId, $"#{a.AliasId}  {a.Name}"))
                .ToList();
            if (aliases.Count > 0) await ReloadLogosAsync();
            else gridLogos.DataSource = new List<Logo>();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中屋号配下のロゴ一覧を読み直す。</summary>
    private async Task ReloadLogosAsync()
    {
        try
        {
            if (cboLgCompanyAlias.SelectedValue is not int companyAliasId) return;
            gridLogos.DataSource = (await _logosRepo.GetByCompanyAliasAsync(companyAliasId)).ToList();
            ClearLogoForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnLogoRowSelected()
    {
        if (gridLogos.CurrentRow?.DataBoundItem is Logo l)
        {
            txtLgCiVersion.Text = l.CiVersionLabel;
            SetDateOrNull(dtLgFrom, chkLgFromNull, l.ValidFrom);
            SetDateOrNull(dtLgTo, chkLgToNull, l.ValidTo);
            txtLgDescription.Text = l.Description ?? "";
            txtLgNotes.Text = l.Notes ?? "";
        }
    }

    private void ClearLogoForm()
    {
        gridLogos.ClearSelection();
        txtLgCiVersion.Text = "";
        chkLgFromNull.Checked = true; chkLgToNull.Checked = true;
        txtLgDescription.Text = ""; txtLgNotes.Text = "";
    }

    private async Task SaveLogoAsync()
    {
        try
        {
            if (cboLgCompanyAlias.SelectedValue is not int companyAliasId)
            { MessageBox.Show(this, "屋号を選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtLgCiVersion.Text))
            { MessageBox.Show(this, "CI バージョンラベルは必須です。"); return; }

            if (gridLogos.CurrentRow?.DataBoundItem is Logo current
                && current.LogoId > 0 && gridLogos.SelectedRows.Count > 0)
            {
                current.CompanyAliasId = companyAliasId;
                current.CiVersionLabel = txtLgCiVersion.Text.Trim();
                current.ValidFrom = chkLgFromNull.Checked ? null : dtLgFrom.Value.Date;
                current.ValidTo = chkLgToNull.Checked ? null : dtLgTo.Value.Date;
                current.Description = NullIfEmpty(txtLgDescription.Text);
                current.Notes = NullIfEmpty(txtLgNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _logosRepo.UpdateAsync(current);
            }
            else
            {
                var l = new Logo
                {
                    CompanyAliasId = companyAliasId,
                    CiVersionLabel = txtLgCiVersion.Text.Trim(),
                    ValidFrom = chkLgFromNull.Checked ? null : dtLgFrom.Value.Date,
                    ValidTo = chkLgToNull.Checked ? null : dtLgTo.Value.Date,
                    Description = NullIfEmpty(txtLgDescription.Text),
                    Notes = NullIfEmpty(txtLgNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _logosRepo.InsertAsync(l);
            }
            await ReloadLogosAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteLogoAsync()
    {
        try
        {
            if (gridLogos.CurrentRow?.DataBoundItem is not Logo l)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"ロゴ #{l.LogoId} {l.CiVersionLabel} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _logosRepo.SoftDeleteAsync(l.LogoId, Environment.UserName);
            await ReloadLogosAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // キャラクター名義タブ（v1.2.0 工程 A 追加）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>選択中キャラクターに紐づく名義一覧を読み直す。</summary>
    private async Task ReloadCharacterAliasesAsync()
    {
        try
        {
            if (cboCaaCharacter.SelectedValue is not int characterId) return;
            gridCharacterAliases.DataSource = (await _characterAliasesRepo.GetByCharacterAsync(characterId)).ToList();
            ClearCharacterAliasForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnCharacterAliasRowSelected()
    {
        if (gridCharacterAliases.CurrentRow?.DataBoundItem is CharacterAlias a)
        {
            txtCaaName.Text = a.Name;
            txtCaaNameKana.Text = a.NameKana ?? "";
            txtCaaNameEn.Text = a.NameEn ?? "";   // v1.2.4 追加
            // v1.2.1: ValidFrom / ValidTo はモデルから撤去済み。UI セットも撤去。
            txtCaaNotes.Text = a.Notes ?? "";
        }
    }

    private void ClearCharacterAliasForm()
    {
        gridCharacterAliases.ClearSelection();
        txtCaaName.Text = ""; txtCaaNameKana.Text = ""; txtCaaNameEn.Text = "";   // v1.2.4 追加
        // v1.2.1: ValidFrom / ValidTo の CheckBox は撤去済み。
        txtCaaNotes.Text = "";
    }

    private async Task SaveCharacterAliasAsync()
    {
        try
        {
            if (cboCaaCharacter.SelectedValue is not int characterId)
            { MessageBox.Show(this, "キャラクターを選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtCaaName.Text))
            { MessageBox.Show(this, "名義名は必須です。"); return; }

            if (gridCharacterAliases.CurrentRow?.DataBoundItem is CharacterAlias current
                && current.AliasId > 0 && gridCharacterAliases.SelectedRows.Count > 0)
            {
                current.CharacterId = characterId;
                current.Name = txtCaaName.Text.Trim();
                current.NameKana = NullIfEmpty(txtCaaNameKana.Text);
                current.NameEn = NullIfEmpty(txtCaaNameEn.Text);   // v1.2.4 追加
                // v1.2.1: ValidFrom / ValidTo の代入は撤去済み。
                current.Notes = NullIfEmpty(txtCaaNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _characterAliasesRepo.UpdateAsync(current);
            }
            else
            {
                var a = new CharacterAlias
                {
                    CharacterId = characterId,
                    Name = txtCaaName.Text.Trim(),
                    NameKana = NullIfEmpty(txtCaaNameKana.Text),
                    NameEn = NullIfEmpty(txtCaaNameEn.Text),   // v1.2.4 追加
                    // v1.2.1: ValidFrom / ValidTo の初期化は撤去済み。
                    Notes = NullIfEmpty(txtCaaNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _characterAliasesRepo.InsertAsync(a);
            }
            await ReloadCharacterAliasesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteCharacterAliasAsync()
    {
        try
        {
            if (gridCharacterAliases.CurrentRow?.DataBoundItem is not CharacterAlias a)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"名義 #{a.AliasId} {a.Name} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _characterAliasesRepo.SoftDeleteAsync(a.AliasId, Environment.UserName);
            await ReloadCharacterAliasesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────────────
    // ピッカー呼び出しヘルパ（v1.2.0 工程 C 追加）
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 人物ピッカーを開き、選択された person_id を NumericUpDown に反映する。
    /// </summary>
    private void OpenPersonPicker(NumericUpDown target)
    {
        try
        {
            using var dlg = new Pickers.PersonPickerDialog(_personsRepo);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
            {
                target.Value = dlg.SelectedId.Value;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 歌録音ピッカーを開き、選択された song_recording_id を NumericUpDown に反映する。
    /// </summary>
    private void OpenSongRecordingPicker(NumericUpDown target)
    {
        try
        {
            // 本フォームには SongRecordings リポジトリへの参照を持たせていないため、
            // クレジット系マスタフォームの DI に追加する必要がある（コンストラクタへの追加と
            // _songRecordingsRepo フィールドの保持は別途行う）。
            using var dlg = new Pickers.SongRecordingPickerDialog(_songRecordingsRepo);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
            {
                target.Value = dlg.SelectedId.Value;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 人物名義ピッカーを開き、選択された alias_id を NumericUpDown に反映する。
    /// scope を指定すると当該人物配下に絞り込む。
    /// </summary>
    private void OpenPersonAliasPicker(NumericUpDown target, int? scopePersonId)
    {
        try
        {
            using var dlg = new Pickers.PersonAliasPickerDialog(_personAliasesRepo, scopePersonId);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
            {
                target.Value = dlg.SelectedId.Value;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 企業屋号ピッカーを開き、選択された alias_id を NumericUpDown に反映する。
    /// scope を指定すると当該企業配下に絞り込む。
    /// onSelected が渡された場合は値設定後に呼び出される（NULL チェック解除等の連動用）。
    /// </summary>
    private void OpenCompanyAliasPicker(NumericUpDown target, int? scopeCompanyId, Action? onSelected = null)
    {
        try
        {
            using var dlg = new Pickers.CompanyAliasPickerDialog(_companyAliasesRepo, scopeCompanyId);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
            {
                target.Value = dlg.SelectedId.Value;
                onSelected?.Invoke();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────
    // 役職タブの DnD（v1.2.0 工程 D 追加）
    // ────────────────────────────────────────────────────────────
    // WinForms の DataGridView は標準で「行 DnD」を持たないため、
    // 行ヘッダのマウスダウン位置を記録 → ドラッグ閾値超過で DoDragDrop 起動 →
    // ターゲット行を HitTest で判定 → ドロップ位置（その行の上 or 下）を Y 座標で判別 →
    // 並び順を組み替えて RolesRepository.BulkUpdateDisplayOrderAsync で永続化、
    // という 5 段階を自前で実装する。

    /// <summary>役職タブ DnD：マウスダウン時のセル位置（行ヘッダか否か）を記録する。</summary>
    private Rectangle _rolesDragBoxFromMouseDown = Rectangle.Empty;
    private int _rolesDragSourceIndex = -1;

    private void GridRoles_MouseDown(object? sender, MouseEventArgs e)
    {
        var hit = gridRoles.HitTest(e.X, e.Y);
        // 行ヘッダ列クリックのみドラッグ開始候補とする（セルクリックは編集動作と区別）
        if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0)
        {
            Size dragSize = SystemInformation.DragSize;
            _rolesDragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
            _rolesDragSourceIndex = hit.RowIndex;
        }
        else
        {
            _rolesDragBoxFromMouseDown = Rectangle.Empty;
            _rolesDragSourceIndex = -1;
        }
    }

    private void GridRoles_MouseMove(object? sender, MouseEventArgs e)
    {
        // ドラッグ閾値を超えるまでは何もしない（クリックとドラッグの誤判定回避）
        if ((e.Button & MouseButtons.Left) == MouseButtons.Left
            && _rolesDragBoxFromMouseDown != Rectangle.Empty
            && !_rolesDragBoxFromMouseDown.Contains(e.X, e.Y)
            && _rolesDragSourceIndex >= 0)
        {
            gridRoles.DoDragDrop(_rolesDragSourceIndex, DragDropEffects.Move);
        }
    }

    private void GridRoles_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetDataPresent(typeof(int)))
            e.Effect = DragDropEffects.Move;
        else
            e.Effect = DragDropEffects.None;
    }

    private void GridRoles_DragOver(object? sender, DragEventArgs e)
    {
        // 役職タブはグループ判定が無いので、行ヘッダ・セル領域内なら常に許可
        var p = gridRoles.PointToClient(new Point(e.X, e.Y));
        var hit = gridRoles.HitTest(p.X, p.Y);
        e.Effect = (hit.RowIndex >= 0) ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>役職タブ DnD：ドロップ時に並べ替えを実行し DB へ反映する。</summary>
    private async Task GridRoles_DragDropAsync(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data is null || !e.Data.GetDataPresent(typeof(int))) return;
            int sourceIndex = (int)e.Data.GetData(typeof(int))!;
            if (sourceIndex < 0) return;

            var p = gridRoles.PointToClient(new Point(e.X, e.Y));
            var hit = gridRoles.HitTest(p.X, p.Y);
            if (hit.RowIndex < 0) return;
            int targetIndex = hit.RowIndex;
            if (targetIndex == sourceIndex) return;

            // ターゲット行の上半分にドロップ → その上に挿入、下半分 → その下に挿入
            var rowRect = gridRoles.GetRowDisplayRectangle(targetIndex, true);
            bool dropAbove = p.Y < rowRect.Top + rowRect.Height / 2;

            // 現在の DataSource を List<Role> として取得し、順序を組み替える
            if (gridRoles.DataSource is not List<Role> rows)
            {
                MessageBox.Show(this, "役職一覧の取得に失敗しました（DataSource が想定外）。",
                    "DnD エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var working = rows.ToList();
            var src = working[sourceIndex];
            working.RemoveAt(sourceIndex);

            // RemoveAt 後はインデックスがずれるので調整：
            //   ・src を targetIndex より「前」から取ってきた場合、targetIndex は 1 つ小さくなる
            int adjustedTarget = (sourceIndex < targetIndex) ? targetIndex - 1 : targetIndex;
            int insertAt = dropAbove ? adjustedTarget : adjustedTarget + 1;
            // 範囲安全クランプ
            if (insertAt < 0) insertAt = 0;
            if (insertAt > working.Count) insertAt = working.Count;
            working.Insert(insertAt, src);

            // DB へ display_order の再採番（10, 20, 30, ...）を反映
            await _rolesRepo.BulkUpdateDisplayOrderAsync(working.Select(r => r.RoleCode));

            // 画面を再ロードして確定状態を表示
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();
            HideAuditColumns(gridRoles);
            // 移動後の行を選択状態に保つ（ベストエフォート）
            for (int i = 0; i < gridRoles.Rows.Count; i++)
            {
                if (gridRoles.Rows[i].DataBoundItem is Role rr && rr.RoleCode == src.RoleCode)
                {
                    gridRoles.ClearSelection();
                    gridRoles.Rows[i].Selected = true;
                    gridRoles.CurrentCell = gridRoles.Rows[i].Cells[0];
                    break;
                }
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            _rolesDragBoxFromMouseDown = Rectangle.Empty;
            _rolesDragSourceIndex = -1;
        }
    }

    // ────────────────────────────────────────────────────────────
    // 主題歌タブの DnD（v1.2.0 工程 D 追加）
    // ────────────────────────────────────────────────────────────
    // 同 (episode_id, is_broadcast_only, theme_kind='INSERT') グループ内のみ並べ替え可。
    // OP/ED 行は CHECK 制約 (ck_ets_op_ed_no_insert_seq) により insert_seq=0 固定で
    // 各グループに 1 行しか存在しないため、ドラッグ・ドロップとも対象外として扱う。

    private Rectangle _etsDragBoxFromMouseDown = Rectangle.Empty;
    private int _etsDragSourceIndex = -1;

    private void GridEts_MouseDown(object? sender, MouseEventArgs e)
    {
        var hit = gridEpisodeThemeSongs.HitTest(e.X, e.Y);
        if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0
            && gridEpisodeThemeSongs.Rows[hit.RowIndex].DataBoundItem is EpisodeThemeSong t
            && t.ThemeKind == "INSERT")
        {
            // INSERT 行のみ DnD 対象
            Size dragSize = SystemInformation.DragSize;
            _etsDragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
            _etsDragSourceIndex = hit.RowIndex;
        }
        else
        {
            _etsDragBoxFromMouseDown = Rectangle.Empty;
            _etsDragSourceIndex = -1;
        }
    }

    private void GridEts_MouseMove(object? sender, MouseEventArgs e)
    {
        if ((e.Button & MouseButtons.Left) == MouseButtons.Left
            && _etsDragBoxFromMouseDown != Rectangle.Empty
            && !_etsDragBoxFromMouseDown.Contains(e.X, e.Y)
            && _etsDragSourceIndex >= 0)
        {
            gridEpisodeThemeSongs.DoDragDrop(_etsDragSourceIndex, DragDropEffects.Move);
        }
    }

    private void GridEts_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetDataPresent(typeof(int)))
            e.Effect = DragDropEffects.Move;
        else
            e.Effect = DragDropEffects.None;
    }

    /// <summary>
    /// 主題歌タブ DnD のドラッグオーバ判定。
    /// ターゲット行が同じ (episode_id, is_broadcast_only, theme_kind='INSERT') グループに
    /// 属する場合のみドロップ可能とし、それ以外は <see cref="DragDropEffects.None"/> にする。
    /// </summary>
    private void GridEts_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        if (e.Data is null || !e.Data.GetDataPresent(typeof(int))) return;
        int sourceIndex = (int)e.Data.GetData(typeof(int))!;
        if (sourceIndex < 0 || sourceIndex >= gridEpisodeThemeSongs.Rows.Count) return;

        var p = gridEpisodeThemeSongs.PointToClient(new Point(e.X, e.Y));
        var hit = gridEpisodeThemeSongs.HitTest(p.X, p.Y);
        if (hit.RowIndex < 0) return;
        if (gridEpisodeThemeSongs.Rows[sourceIndex].DataBoundItem is not EpisodeThemeSong src) return;
        if (gridEpisodeThemeSongs.Rows[hit.RowIndex].DataBoundItem is not EpisodeThemeSong tgt) return;

        // 同グループ判定：episode_id / is_broadcast_only / theme_kind='INSERT' が一致
        if (src.EpisodeId == tgt.EpisodeId
            && src.IsBroadcastOnly == tgt.IsBroadcastOnly
            && src.ThemeKind == "INSERT" && tgt.ThemeKind == "INSERT")
        {
            e.Effect = DragDropEffects.Move;
        }
    }

    /// <summary>主題歌タブ DnD のドロップ処理。</summary>
    private async Task GridEts_DragDropAsync(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data is null || !e.Data.GetDataPresent(typeof(int))) return;
            int sourceIndex = (int)e.Data.GetData(typeof(int))!;
            if (sourceIndex < 0) return;

            var p = gridEpisodeThemeSongs.PointToClient(new Point(e.X, e.Y));
            var hit = gridEpisodeThemeSongs.HitTest(p.X, p.Y);
            if (hit.RowIndex < 0) return;
            int targetIndex = hit.RowIndex;
            if (targetIndex == sourceIndex) return;

            if (gridEpisodeThemeSongs.Rows[sourceIndex].DataBoundItem is not EpisodeThemeSong src) return;
            if (gridEpisodeThemeSongs.Rows[targetIndex].DataBoundItem is not EpisodeThemeSong tgt) return;
            if (src.EpisodeId != tgt.EpisodeId
                || src.IsBroadcastOnly != tgt.IsBroadcastOnly
                || src.ThemeKind != "INSERT" || tgt.ThemeKind != "INSERT")
                return;

            var rowRect = gridEpisodeThemeSongs.GetRowDisplayRectangle(targetIndex, true);
            bool dropAbove = p.Y < rowRect.Top + rowRect.Height / 2;

            // 全件 DataSource から、対象グループ（INSERT のみ）の行を取り出して順序を組み替える
            if (gridEpisodeThemeSongs.DataSource is not List<EpisodeThemeSong> all)
            {
                MessageBox.Show(this, "主題歌一覧の取得に失敗しました（DataSource が想定外）。",
                    "DnD エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var sameGroup = all
                .Where(x => x.EpisodeId == src.EpisodeId
                         && x.IsBroadcastOnly == src.IsBroadcastOnly
                         && x.ThemeKind == "INSERT")
                .OrderBy(x => x.Seq)
                .ToList();
            int srcIdxInGroup = sameGroup.FindIndex(x => x.Seq == src.Seq);
            int tgtIdxInGroup = sameGroup.FindIndex(x => x.Seq == tgt.Seq);
            if (srcIdxInGroup < 0 || tgtIdxInGroup < 0) return;

            var srcEntity = sameGroup[srcIdxInGroup];
            sameGroup.RemoveAt(srcIdxInGroup);
            int adjustedTarget = (srcIdxInGroup < tgtIdxInGroup) ? tgtIdxInGroup - 1 : tgtIdxInGroup;
            int insertAt = dropAbove ? adjustedTarget : adjustedTarget + 1;
            if (insertAt < 0) insertAt = 0;
            if (insertAt > sameGroup.Count) insertAt = sameGroup.Count;
            sameGroup.Insert(insertAt, srcEntity);

            // DB 反映：当該グループのみ DELETE → 新順序で INSERT
            await SeqReorderHelper.ReorderEpisodeThemeSongsAsync(
                _episodeThemeSongsRepo, src.EpisodeId, src.IsBroadcastOnly, sameGroup);

            // 画面再ロード（既存メソッドを再利用）
            await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            _etsDragBoxFromMouseDown = Rectangle.Empty;
            _etsDragSourceIndex = -1;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // v1.2.1 名寄せ機能：付け替え／改名のクリックハンドラ
    //   人物名義 / 企業屋号 / キャラクター名義の 3 タブそれぞれに
    //   「別○○に付け替え...」と「この名義で改名...」を提供する。
    // ────────────────────────────────────────────────────────────────────

    /// <summary>選択中の人物名義を別人物に付け替える（v1.2.1）。</summary>
    private async Task OnReassignPersonAliasClickAsync()
    {
        try
        {
            if (gridPersonAliases.CurrentRow?.DataBoundItem is not PersonAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "付け替える人物名義をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string currentParentLabel = cboPaPerson.Text;
            using var dlg = new Dialogs.AliasReassignDialog(
                a.AliasId, a.Name ?? "", currentParentLabel,
                _personAliasesRepo, _personsRepo);

            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Reassigned)
            {
                // 付け替え後はグリッドが古い person の alias 一覧を表示しているので、
                // 親人物コンボの選択を新人物にしてリロード、はせず、シンプルに人物リストを再構築。
                await ReloadPersonsForAliasTabAsync();
                await ReloadPersonAliasesAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中の人物名義を改名する（v1.2.1）。</summary>
    private async Task OnRenamePersonAliasClickAsync()
    {
        try
        {
            if (gridPersonAliases.CurrentRow?.DataBoundItem is not PersonAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "改名する人物名義をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new Dialogs.AliasRenameDialog(
                a.AliasId, a.Name ?? "", a.NameKana, _personAliasesRepo);

            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Renamed)
            {
                // 新 alias が生成されている。同じ親人物の alias リストとしてリロード。
                await ReloadPersonsForAliasTabAsync();
                await ReloadPersonAliasesAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ──────────────────────────────────────────────────────────────────
    //  v1.2.3 追加：ユニットメンバー編集
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 「ユニットメンバー編集...」ボタンのハンドラ。
    /// 選択中の人物名義を「ユニット」と見なして、その構成メンバーを
    /// <see cref="PersonAliasMembersEditDialog"/> で編集し、OK で
    /// <see cref="PersonAliasMembersRepository.ReplaceAllAsync"/> 一括保存する。
    /// </summary>
    private async Task OnEditPersonAliasMembersAsync()
    {
        try
        {
            if (gridPersonAliases.CurrentRow?.DataBoundItem is not PersonAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "メンバーを編集するユニット名義をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 既存メンバーを取得し、ダイアログ用 DTO に変換する。
            // 表示名は member_kind に応じて person_aliases / character_aliases の表示名を解決。
            var existing = await _personAliasMembersRepo.GetByParentAsync(a.AliasId);
            var initial = new List<PersonAliasMembersEditDialog.MemberDto>();
            foreach (var m in existing)
            {
                string display;
                if (m.MemberKind == PersonAliasMemberKind.Person && m.MemberPersonAliasId.HasValue)
                    display = await _personAliasesRepo.GetDisplayNameAsync(m.MemberPersonAliasId.Value);
                else if (m.MemberKind == PersonAliasMemberKind.Character && m.MemberCharacterAliasId.HasValue)
                    display = (await _characterAliasesRepo.GetByIdAsync(m.MemberCharacterAliasId.Value))?.Name ?? "(該当なし)";
                else
                    display = "(該当なし)";

                initial.Add(new PersonAliasMembersEditDialog.MemberDto
                {
                    MemberKind = m.MemberKind,
                    MemberPersonAliasId = m.MemberPersonAliasId,
                    MemberCharacterAliasId = m.MemberCharacterAliasId,
                    MemberDisplay = display,
                    Notes = m.Notes
                });
            }

            using var dlg = new PersonAliasMembersEditDialog(
                a.AliasId, initial, _personAliasesRepo, _characterAliasesRepo);
            dlg.Text = $"ユニット名義メンバー管理（alias_id={a.AliasId} / {a.GetDisplayName()}）";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            // 編集結果を PersonAliasMember モデルに変換し、ReplaceAllAsync で一括保存。
            // ネスト禁止は DB トリガーが保証するため、違反時は例外で差し戻す。
            var newMembers = dlg.ResultMembers.Select((m, i) => new PersonAliasMember
            {
                ParentAliasId = a.AliasId,
                MemberSeq = (byte)(i + 1),
                MemberKind = m.MemberKind,
                MemberPersonAliasId = m.MemberPersonAliasId,
                MemberCharacterAliasId = m.MemberCharacterAliasId,
                Notes = m.Notes
            }).ToList();

            await _personAliasMembersRepo.ReplaceAllAsync(a.AliasId, newMembers, Environment.UserName);
            MessageBox.Show(this, $"{newMembers.Count} 件のメンバーを保存しました。",
                "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中の企業屋号を別企業に付け替える（v1.2.1）。</summary>
    private async Task OnReassignCompanyAliasClickAsync()
    {
        try
        {
            if (gridCompanyAliases.CurrentRow?.DataBoundItem is not CompanyAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "付け替える企業屋号をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string currentParentLabel = cboCaCompany.Text;
            using var dlg = new Dialogs.AliasReassignDialog(
                a.AliasId, a.Name ?? "", currentParentLabel,
                _companyAliasesRepo, _companiesRepo);

            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Reassigned)
            {
                await ReloadCompaniesForAliasTabAsync();
                await ReloadCompanyAliasesAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中の企業屋号を改名する（v1.2.1）。</summary>
    private async Task OnRenameCompanyAliasClickAsync()
    {
        try
        {
            if (gridCompanyAliases.CurrentRow?.DataBoundItem is not CompanyAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "改名する企業屋号をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new Dialogs.AliasRenameDialog(
                a.AliasId, a.Name ?? "", a.NameKana, _companyAliasesRepo);

            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Renamed)
            {
                await ReloadCompaniesForAliasTabAsync();
                await ReloadCompanyAliasesAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中のキャラ名義を別キャラに付け替える（v1.2.1）。</summary>
    private async Task OnReassignCharacterAliasClickAsync()
    {
        try
        {
            if (gridCharacterAliases.CurrentRow?.DataBoundItem is not CharacterAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "付け替えるキャラ名義をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string currentParentLabel = cboCaaCharacter.Text;
            using var dlg = new Dialogs.AliasReassignDialog(
                a.AliasId, a.Name ?? "", currentParentLabel,
                _characterAliasesRepo, _charactersRepo);

            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Reassigned)
            {
                // キャラタブもリロード（孤立キャラが論理削除された可能性があるため）
                gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
                cboCaaCharacter.DataSource = (await _charactersRepo.GetAllAsync())
                    .Select(x => new IdLabel(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
                await ReloadCharacterAliasesAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中のキャラ名義を改名する（v1.2.1）。</summary>
    private async Task OnRenameCharacterAliasClickAsync()
    {
        try
        {
            if (gridCharacterAliases.CurrentRow?.DataBoundItem is not CharacterAlias a || a.AliasId <= 0)
            {
                MessageBox.Show(this, "改名するキャラ名義をグリッドで選択してください。",
                    "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new Dialogs.AliasRenameDialog(
                a.AliasId, a.Name ?? "", a.NameKana, _characterAliasesRepo);

            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Renamed)
            {
                // キャラ本体の表示名を同期した可能性があるので、キャラ一覧もリロードする。
                gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
                cboCaaCharacter.DataSource = (await _charactersRepo.GetAllAsync())
                    .Select(x => new IdLabel(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
                await ReloadCharacterAliasesAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 人物名義タブの上部「親人物」コンボを再構築する（v1.2.1 名寄せ後のリフレッシュに使用）。
    /// 既存の <see cref="LoadAllAsync"/> 内のロジックと同じ流れで人物リストを再投入する。
    /// </summary>
    private async Task ReloadPersonsForAliasTabAsync()
    {
        var persons = await _personsRepo.GetAllAsync();
        cboPaPerson.DataSource = persons
            .Select(p => new IdLabel(p.PersonId, $"#{p.PersonId}  {p.FullName}"))
            .ToList();
    }

    /// <summary>
    /// 企業屋号タブの上部「親企業」コンボを再構築する（v1.2.1 名寄せ後のリフレッシュに使用）。
    /// </summary>
    private async Task ReloadCompaniesForAliasTabAsync()
    {
        var companies = await _companiesRepo.GetAllAsync();
        cboCaCompany.DataSource = companies
            .Select(c => new IdLabel(c.CompanyId, $"#{c.CompanyId}  {c.Name}"))
            .ToList();
    }
}
