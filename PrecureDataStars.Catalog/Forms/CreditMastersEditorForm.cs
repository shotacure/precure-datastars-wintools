using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Data.Text;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット系マスタ管理フォーム。
/// 15 タブ構成: プリキュア（先頭・新設）／人物／人物名義／企業／企業屋号／
/// ロゴ／キャラクター／キャラクター名義／キャラクター続柄（新設）／家族関係（新設）／
/// 役職／役職テンプレート／エピソード主題歌／シリーズ種別／パート種別を管理する。
/// 声優キャスティングは「ノンクレを除いてクレジットされている事実 = キャスティング」という
/// 業務ルールに基づき、credit_block_entries の CHARACTER_VOICE エントリで一元管理する
/// （専用タブは持たない）。
/// 操作流儀は既存 <see cref="MastersEditorForm"/> と同様に「DataGridView バインド +
/// 編集パネル + 新規 / 保存・更新 / 削除」のボタン構成で統一している。
/// 監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）は <see cref="HideAuditColumns"/>
/// により全グリッドで自動非表示化。
/// プリキュア／キャラクター続柄／家族関係の 3 タブの実装は本ファイルではなく
/// <c>CreditMastersEditorForm.PrecureTabs.cs</c>（partial）に分離している。
/// </summary>
public partial class CreditMastersEditorForm : Form
{
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly RolesRepository _rolesRepo;
    // 役職書式は RoleTemplatesRepository / CreditKindsRepository で扱う。
    // RoleTemplatesRepository（既定とシリーズ別を統合）に置き換え。
    private readonly RoleTemplatesRepository _roleTemplatesRepo;
    // クレジット種別マスタの CRUD 用。
    private readonly CreditKindsRepository _creditKindsRepo;
    private readonly EpisodeThemeSongsRepository _episodeThemeSongsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;
    private readonly PartTypesRepository _partTypesRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    // マスタ補完（名義・屋号・ロゴ）用リポジトリ
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // 歌録音ピッカー用
    private readonly SongRecordingsRepository _songRecordingsRepo;
    // キャラクター区分マスタ
    private readonly CharacterKindsRepository _characterKindsRepo;

    // ユニットメンバー管理用リポジトリ（人物名義タブから「ユニットメンバー編集...」ボタンで使用）
    private readonly PersonAliasMembersRepository _personAliasMembersRepo;

    // プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
    private readonly PrecuresRepository _precuresRepo;
    private readonly CharacterRelationKindsRepository _characterRelationKindsRepo;
    private readonly CharacterFamilyRelationsRepository _characterFamilyRelationsRepo;

    // 役職系譜（多対多）を編集するためのリポジトリ。
    // 役職タブの [系譜...] ボタン（Designer.cs 側で正規定義）から本リポジトリを使うダイアログが開く。
    private readonly RoleSuccessionsRepository _roleSuccessionsRepo;

    /// <summary>クレジット系マスタ管理フォームを生成する。Program.cs の DI で各リポジトリを受け取る。</summary>
    public CreditMastersEditorForm(
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        RolesRepository rolesRepo,
        // 役職書式は RoleTemplatesRepository + CreditKindsRepository で扱う。
        RoleTemplatesRepository roleTemplatesRepo,
        CreditKindsRepository creditKindsRepo,
        EpisodeThemeSongsRepository episodeThemeSongsRepo,
        SeriesKindsRepository seriesKindsRepo,
        PartTypesRepository partTypesRepo,
        SeriesRepository seriesRepo,
        EpisodesRepository episodesRepo,
        // マスタ補完（名義・屋号・ロゴ）
        PersonAliasesRepository personAliasesRepo,
        PersonAliasPersonsRepository personAliasPersonsRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        // 歌録音ピッカー
        SongRecordingsRepository songRecordingsRepo,
        // キャラクター区分マスタ
        CharacterKindsRepository characterKindsRepo,
        // ユニットメンバー管理
        PersonAliasMembersRepository personAliasMembersRepo,
        // プリキュア本体マスタ・キャラクター続柄マスタ・家族関係（汎用）
        PrecuresRepository precuresRepo,
        CharacterRelationKindsRepository characterRelationKindsRepo,
        CharacterFamilyRelationsRepository characterFamilyRelationsRepo,
        // 役職系譜（多対多）リポジトリ
        RoleSuccessionsRepository roleSuccessionsRepo)
    {
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
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

        // プリキュア本体・キャラクター続柄・家族関係（汎用）
        _precuresRepo = precuresRepo ?? throw new ArgumentNullException(nameof(precuresRepo));
        _characterRelationKindsRepo = characterRelationKindsRepo ?? throw new ArgumentNullException(nameof(characterRelationKindsRepo));
        _characterFamilyRelationsRepo = characterFamilyRelationsRepo ?? throw new ArgumentNullException(nameof(characterFamilyRelationsRepo));

        // 役職系譜
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
        // プリキュア／続柄／家族関係の 3 グリッドを扱う。
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
        chkOvToNull.CheckedChanged += (_, __) => dtOvTo.Enabled = !chkOvToNull.Checked;
        // 名義・屋号・ロゴタブの「未指定」チェック連動
        chkPaFromNull.CheckedChanged += (_, __) => dtPaFrom.Enabled = !chkPaFromNull.Checked;
        chkPaToNull.CheckedChanged += (_, __) => dtPaTo.Enabled = !chkPaToNull.Checked;
        chkCaFromNull.CheckedChanged += (_, __) => dtCaFrom.Enabled = !chkCaFromNull.Checked;
        chkCaToNull.CheckedChanged += (_, __) => dtCaTo.Enabled = !chkCaToNull.Checked;
        chkLgFromNull.CheckedChanged += (_, __) => dtLgFrom.Enabled = !chkLgFromNull.Checked;
        chkLgToNull.CheckedChanged += (_, __) => dtLgTo.Enabled = !chkLgToNull.Checked;

        // 行選択 → 編集パネル反映
        gridPersons.SelectionChanged += (_, __) => OnPersonRowSelected();
        gridCompanies.SelectionChanged += (_, __) => OnCompanyRowSelected();
        gridCharacters.SelectionChanged += (_, __) => OnCharacterRowSelected();
        gridRoles.SelectionChanged += (_, __) => OnRoleRowSelected();
        gridRoleOverrides.SelectionChanged += (_, __) => OnRoleOverrideRowSelected();
        gridEpisodeThemeSongs.SelectionChanged += (_, __) => OnEpisodeThemeSongRowSelected();
        gridSeriesKinds.SelectionChanged += (_, __) => OnSeriesKindRowSelected();
        gridPartTypes.SelectionChanged += (_, __) => OnPartTypeRowSelected();
        // 名義・屋号・ロゴタブ
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

        // 声優キャスティング関連のクリック結線（cboVcCharacter / numVcPersonId /
        // 代わりに WirePrecureTabsEvents() でプリキュア系 3 タブのイベントをまとめて結線する
        // （実装は CreditMastersEditorForm.PrecureTabs.cs）。
        WirePrecureTabsEvents();

        btnSaveRole.Click += async (_, __) => await SaveRoleAsync();
        btnDeleteRole.Click += async (_, __) => await DeleteRoleAsync();

        cboOvSeries.SelectedIndexChanged += async (_, __) => await ReloadRoleOverridesAsync();
        btnSaveOverride.Click += async (_, __) => await SaveRoleOverrideAsync();
        btnDeleteOverride.Click += async (_, __) => await DeleteRoleOverrideAsync();
        // 「+ 新規追加」ボタンを結線。Designer 側で Name="btnNewOverride" を付与しているので
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
        // 追加：他話からのコピー
        btnCopyEts.Click += async (_, __) => await OpenEtsCopyDialogAsync();
        // 範囲コピーボタンを EpisodeThemeSongRangeCopyDialog に結線。
        btnRangeCopyEts.Click += async (_, __) => await OpenEtsRangeCopyDialogAsync();

        // マスタ役職タブの DnD（display_order 並べ替え）
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

        // マスタ主題歌タブの DnD（INSERT 行のみ insert_seq 並べ替え）
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

        // 人物名義タブ
        cboPaPerson.SelectedIndexChanged += async (_, __) => await ReloadPersonAliasesAsync();
        btnNewPersonAlias.Click += (_, __) => ClearPersonAliasForm();
        btnSavePersonAlias.Click += async (_, __) => await SavePersonAliasAsync();
        btnDeletePersonAlias.Click += async (_, __) => await DeletePersonAliasAsync();
        btnAddJointPerson.Click += async (_, __) => await AddJointPersonAsync();
        btnRemoveJointPerson.Click += async (_, __) => await RemoveJointPersonAsync();
        // 名寄せ機能：選択中の人物名義に対する付け替え／改名ハンドラ
        btnReassignPersonAlias.Click += async (_, __) => await OnReassignPersonAliasClickAsync();
        btnRenamePersonAlias.Click += async (_, __) => await OnRenamePersonAliasClickAsync();
        // ユニットメンバー編集ボタン（PersonAliasMembersEditDialog を開く）
        btnPaEditMembers.Click += async (_, __) => await OnEditPersonAliasMembersAsync();

        // 企業屋号タブ
        cboCaCompany.SelectedIndexChanged += async (_, __) => await ReloadCompanyAliasesAsync();
        btnNewCompanyAlias.Click += (_, __) => ClearCompanyAliasForm();
        btnSaveCompanyAlias.Click += async (_, __) => await SaveCompanyAliasAsync();
        btnDeleteCompanyAlias.Click += async (_, __) => await DeleteCompanyAliasAsync();
        // 名寄せ機能：選択中の企業屋号に対する付け替え／改名ハンドラ
        btnReassignCompanyAlias.Click += async (_, __) => await OnReassignCompanyAliasClickAsync();
        btnRenameCompanyAlias.Click += async (_, __) => await OnRenameCompanyAliasClickAsync();

        // ロゴタブ
        cboLgCompany.SelectedIndexChanged += async (_, __) => await ReloadLgCompanyAliasComboAsync();
        cboLgCompanyAlias.SelectedIndexChanged += async (_, __) => await ReloadLogosAsync();
        btnNewLogo.Click += (_, __) => ClearLogoForm();
        btnSaveLogo.Click += async (_, __) => await SaveLogoAsync();
        btnDeleteLogo.Click += async (_, __) => await DeleteLogoAsync();

        // キャラクター名義タブ
        cboCaaCharacter.SelectedIndexChanged += async (_, __) => await ReloadCharacterAliasesAsync();
        btnNewCharacterAlias.Click += (_, __) => ClearCharacterAliasForm();
        btnSaveCharacterAlias.Click += async (_, __) => await SaveCharacterAliasAsync();
        btnDeleteCharacterAlias.Click += async (_, __) => await DeleteCharacterAliasAsync();
        // 名寄せ機能：選択中のキャラ名義に対する付け替え／改名ハンドラ
        btnReassignCharacterAlias.Click += async (_, __) => await OnReassignCharacterAliasClickAsync();
        btnRenameCharacterAlias.Click += async (_, __) => await OnRenameCharacterAliasClickAsync();

        // 各タブの「検索...」ボタンにピッカーダイアログを結線
        btnPickEtsSongRecordingId.Click += (_, __) => OpenSongRecordingPicker(numEtsSongRecordingId);
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

        // [系譜…] ボタン（Designer.cs 側で正規定義）の Click ハンドラを購読。
        // ボタン自体の生成は Designer.cs 側で行われているので、ここではイベント購読のみ。
        btnEditRoleSuccessions.Click += async (_, _) => await OnEditRoleSuccessionsClickAsync();
    }

    /// <summary>全タブの初期データを 1 度に読み込む。コンボの選択肢初期化もここで行う。</summary>
    private async Task LoadAllAsync()
    {
        try
        {
            // キャラクター区分コンボをマスタからバインド（旧コードはハードコードだった）。
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
            // cboOvSeries はこのタブでは役職コンボとして使う（フィールド名は他参照箇所の都合で維持）。
            // このコンボは「役職フィルタ」用途。フィールド名は他参照箇所の都合で
            // cboOvSeries のまま流用しているが、実体は役職コンボ（DataSource = 役職リスト、
            // ValueMember = role_code: string）。下部の cboOvRole は詳細編集パネル側の役職セレクタ。
            // 詳細パネル側のシリーズ選択は cboOvTemplateSeries が担う。
            var rolesForOv = (await _rolesRepo.GetAllAsync())
                .Select(r => new IdLabel<string>(r.RoleCode, $"{r.RoleCode}  {r.NameJa}"))
                .ToList();
            cboOvSeries.DisplayMember = "Label";
            cboOvSeries.ValueMember = "Id";
            cboOvSeries.DataSource = rolesForOv;

            // 詳細編集パネル下部の役職コンボには同じ役職リストをバインド
            cboOvRole.DisplayMember = "Label";
            cboOvRole.ValueMember = "Id";
            cboOvRole.DataSource = (await _rolesRepo.GetAllAsync())
                .Select(r => new IdLabel<string>(r.RoleCode, $"{r.RoleCode}  {r.NameJa}"))
                .ToList();

            // 詳細編集パネル下部のシリーズコンボ（cboOvTemplateSeries）には
            // 「（既定 / 全シリーズ）」の選択肢 + 全シリーズをバインドする。
            // ID=null（既定）と ID=シリーズID の混在を扱うため、IdLabel<int?> を使う。
            var allSeries = await _seriesRepo.GetAllAsync();
            var templateSeriesItems = new List<IdLabel<int?>>
            {
                new IdLabel<int?>(null, "（既定 / 全シリーズ）")
            };
            templateSeriesItems.AddRange(
                allSeries.Select(s => new IdLabel<int?>(s.SeriesId, $"#{s.SeriesId}  {s.Title}")));
            cboOvTemplateSeries.DisplayMember = "Label";
            cboOvTemplateSeries.ValueMember = "Id";
            cboOvTemplateSeries.DataSource = templateSeriesItems;

            if (rolesForOv.Count > 0) await ReloadRoleOverridesAsync();

            // エピソード主題歌タブ：シリーズコンボへバインド（エピソードはシリーズ選択後に絞り込み）
            var seriesItems = allSeries.Select(s => new IdLabel<int>(s.SeriesId, $"#{s.SeriesId}  {s.Title}")).ToList();
            cboEtsSeries.DisplayMember = "Label";
            cboEtsSeries.ValueMember = "Id";
            cboEtsSeries.DataSource = seriesItems.Select(x => new IdLabel<int>(x.Id, x.Label)).ToList();
            // 編集パネル既定値（本放送フラグは OFF、種別は OP）
            chkEtsBroadcastOnly.Checked = false;
            cboEtsThemeKind.SelectedItem = "OP";
            if (seriesItems.Count > 0) await ReloadEpisodesForEtsAsync();

            // 人物名義タブのコンボ初期化（人物リスト）
            var persons = await _personsRepo.GetAllAsync();
            cboPaPerson.DisplayMember = "Label";
            cboPaPerson.ValueMember = "Id";
            cboPaPerson.DataSource = persons
                .Select(p => new IdLabel<int>(p.PersonId, $"#{p.PersonId}  {p.FullName}"))
                .ToList();
            if (persons.Count > 0) await ReloadPersonAliasesAsync();

            // 企業屋号タブのコンボ初期化（企業リスト）
            var companies = await _companiesRepo.GetAllAsync();
            var companyItems = companies
                .Select(c => new IdLabel<int>(c.CompanyId, $"#{c.CompanyId}  {c.Name}"))
                .ToList();
            cboCaCompany.DisplayMember = "Label";
            cboCaCompany.ValueMember = "Id";
            cboCaCompany.DataSource = companyItems;
            if (companies.Count > 0) await ReloadCompanyAliasesAsync();

            // ロゴタブのコンボ初期化（企業リスト→屋号は連動取得）
            cboLgCompany.DisplayMember = "Label";
            cboLgCompany.ValueMember = "Id";
            cboLgCompany.DataSource = companyItems
                .Select(x => new IdLabel<int>(x.Id, x.Label)).ToList();
            if (companies.Count > 0) await ReloadLgCompanyAliasComboAsync();

            // キャラクター名義タブのコンボ初期化（キャラリスト）
            cboCaaCharacter.DisplayMember = "Label";
            cboCaaCharacter.ValueMember = "Id";
            cboCaaCharacter.DataSource = characters
                .Select(c => new IdLabel<int>(c.CharacterId, $"#{c.CharacterId}  {c.Name}"))
                .ToList();
            if (characters.Count > 0) await ReloadCharacterAliasesAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    // ヘルパー

    /// <summary>グリッドの監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）を データバインド完了時に自動的に非表示にする（既存 MastersEditorForm と同方針）。</summary>
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


    /// <summary>空文字列を NULL に変換するヘルパ。</summary>
    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

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

    // ピッカー呼び出しヘルパ

    /// <summary>人物ピッカーを開き、選択された person_id を NumericUpDown に反映する。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>歌録音ピッカーを開き、選択された song_recording_id を NumericUpDown に反映する。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>人物名義ピッカーを開き、選択された alias_id を NumericUpDown に反映する。 scope を指定すると当該人物配下に絞り込む。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>企業屋号ピッカーを開き、選択された alias_id を NumericUpDown に反映する。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    // かな・英語表記の自動補完（登録・変更時フック）
    //
    // 各 Save 系メソッドが保存対象を確定したあと、リポジトリへ渡す直前に呼ぶ。
    // 空欄に補完できる候補があれば内容を MessageBox で提示して確認をとり、
    // OK のときだけ対象オブジェクトのプロパティへ補完値を代入する（Cancel なら
    // 入力値のまま通常保存）。実際の永続化は既存の Update/Insert がそのまま行う。
    //
    // ローマ字化はパスポート式の共有ロジック KanaRomanizer に委譲する。
    // kana は読みを機械推定できないため「補完元（人物・企業・親キャラ）に値がある
    // 場合のコピー」のみ行い、無ければ補完しない（捏造しない）。en は補完元優先、
    // 空なら名称のかな表記からローマ字フォールバックする。

    /// <summary>補完候補 1 件分（列名・現値・補完予定値）。確認ダイアログの本文生成に使う。</summary>
    private readonly struct FillCandidate
    {
        public FillCandidate(string label, string newValue, string source)
        {
            Label = label;
            NewValue = newValue;
            Source = source;
        }

        public string Label { get; }
        public string NewValue { get; }
        public string Source { get; }
    }

    /// <summary>補完候補リストを確認ダイアログにかけ、ユーザーが承認したら true を返す。 候補が空なら何も訊かず false（補完不要）を返す。</summary>
    private bool ConfirmAutoFill(IReadOnlyList<FillCandidate> candidates)
    {
        if (candidates.Count == 0) return false;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("以下の項目を自動補完します。よろしいですか？");
        sb.AppendLine();
        foreach (var c in candidates)
        {
            sb.AppendLine($"・{c.Label}： {c.NewValue}   （{c.Source}）");
        }

        return MessageBox.Show(this, sb.ToString(), "かな・英語の自動補完",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
    }

    /// <summary>文字列が未入力（null・空・空白のみ）かどうか。</summary>
    private static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s);

    /// <summary>en 補完値を決める。優先順は「補完元 en（sourceEn）」→「name のローマ字化」。 どちらも得られなければ null。<paramref name="source"/> に出所説明を返す。</summary>
    private static string? ResolveEnFill(string? currentEn, string? sourceEn,
        string name, out string source)
    {
        source = string.Empty;
        if (!IsBlank(currentEn)) return null; // 既に値あり

        if (!IsBlank(sourceEn))
        {
            source = "補完元からコピー";
            return sourceEn!.Trim();
        }

        if (KanaRomanizer.TryRomanize(name, out string ro, out _))
        {
            source = "ローマ字（パスポート式）";
            return ro;
        }

        return null;
    }
}
