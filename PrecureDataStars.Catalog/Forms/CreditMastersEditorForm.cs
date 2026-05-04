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
/// クレジット系マスタ管理フォーム（v1.2.0 新設）。13 タブ構成で人物・人物名義・企業・企業屋号・
/// ロゴ・キャラクター・キャラクター名義・声優キャスティング・役職・シリーズ別書式上書き・
/// エピソード主題歌、および シリーズ種別 / パート種別マスタ（v1.2.0 列追加分）を管理する。
/// <para>
/// 操作流儀は既存 <see cref="MastersEditorForm"/> と同様に「DataGridView バインド +
/// 編集パネル + 新規 / 保存・更新 / 削除」のボタン構成で統一している。
/// 監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）は <see cref="HideAuditColumns"/>
/// により全グリッドで自動非表示化。
/// </para>
/// <para>
/// クレジット本体（<c>credits</c> / <c>credit_cards</c> / <c>credit_card_roles</c> /
/// <c>credit_role_blocks</c> / <c>credit_block_entries</c>）の編集 UI は v1.2.0 の後続工程で
/// 別途追加予定。本フォームではマスタ群の整備までを担当する。
/// </para>
/// </summary>
public partial class CreditMastersEditorForm : Form
{
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterVoiceCastingsRepository _voiceCastingsRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly SeriesRoleFormatOverridesRepository _roleOverridesRepo;
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

    /// <summary>
    /// クレジット系マスタ管理フォームを生成する。Program.cs の DI で各リポジトリを受け取る。
    /// </summary>
    public CreditMastersEditorForm(
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        CharacterVoiceCastingsRepository voiceCastingsRepo,
        RolesRepository rolesRepo,
        SeriesRoleFormatOverridesRepository roleOverridesRepo,
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
        CharacterAliasesRepository characterAliasesRepo)
    {
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        _voiceCastingsRepo = voiceCastingsRepo ?? throw new ArgumentNullException(nameof(voiceCastingsRepo));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _roleOverridesRepo = roleOverridesRepo ?? throw new ArgumentNullException(nameof(roleOverridesRepo));
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

        InitializeComponent();

        // 全グリッドの監査列を自動非表示にする（DataBindingComplete のたびに Visible=false）
        HideAuditColumns(gridPersons);
        HideAuditColumns(gridPersonAliases);
        HideAuditColumns(gridCompanies);
        HideAuditColumns(gridCompanyAliases);
        HideAuditColumns(gridLogos);
        HideAuditColumns(gridCharacters);
        HideAuditColumns(gridCharacterAliases);
        HideAuditColumns(gridVoiceCastings);
        HideAuditColumns(gridRoles);
        HideAuditColumns(gridRoleOverrides);
        HideAuditColumns(gridEpisodeThemeSongs);
        HideAuditColumns(gridSeriesKinds);
        HideAuditColumns(gridPartTypes);

        // 「未指定」チェックでピッカー無効化を連動させる
        chkCFoundedNull.CheckedChanged += (_, __) => dtCFounded.Enabled = !chkCFoundedNull.Checked;
        chkCDissolvedNull.CheckedChanged += (_, __) => dtCDissolved.Enabled = !chkCDissolvedNull.Checked;
        chkVcFromNull.CheckedChanged += (_, __) => dtVcFrom.Enabled = !chkVcFromNull.Checked;
        chkVcToNull.CheckedChanged += (_, __) => dtVcTo.Enabled = !chkVcToNull.Checked;
        chkOvToNull.CheckedChanged += (_, __) => dtOvTo.Enabled = !chkOvToNull.Checked;
        chkEtsLabelNull.CheckedChanged += (_, __) => numEtsLabelCompanyAliasId.Enabled = !chkEtsLabelNull.Checked;
        // v1.2.0 工程 A: 名義・屋号・ロゴタブの「未指定」チェック連動
        chkPaFromNull.CheckedChanged += (_, __) => dtPaFrom.Enabled = !chkPaFromNull.Checked;
        chkPaToNull.CheckedChanged += (_, __) => dtPaTo.Enabled = !chkPaToNull.Checked;
        chkCaFromNull.CheckedChanged += (_, __) => dtCaFrom.Enabled = !chkCaFromNull.Checked;
        chkCaToNull.CheckedChanged += (_, __) => dtCaTo.Enabled = !chkCaToNull.Checked;
        chkLgFromNull.CheckedChanged += (_, __) => dtLgFrom.Enabled = !chkLgFromNull.Checked;
        chkLgToNull.CheckedChanged += (_, __) => dtLgTo.Enabled = !chkLgToNull.Checked;
        chkCaaFromNull.CheckedChanged += (_, __) => dtCaaFrom.Enabled = !chkCaaFromNull.Checked;
        chkCaaToNull.CheckedChanged += (_, __) => dtCaaTo.Enabled = !chkCaaToNull.Checked;

        // 行選択 → 編集パネル反映
        gridPersons.SelectionChanged += (_, __) => OnPersonRowSelected();
        gridCompanies.SelectionChanged += (_, __) => OnCompanyRowSelected();
        gridCharacters.SelectionChanged += (_, __) => OnCharacterRowSelected();
        gridVoiceCastings.SelectionChanged += (_, __) => OnVoiceCastingRowSelected();
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

        cboVcCharacter.SelectedIndexChanged += async (_, __) => await ReloadVoiceCastingsAsync();
        numVcPersonId.ValueChanged += async (_, __) => await ResolveVoicePersonNameAsync();
        btnNewVoiceCasting.Click += (_, __) => ClearVoiceCastingForm();
        btnSaveVoiceCasting.Click += async (_, __) => await SaveVoiceCastingAsync();
        btnDeleteVoiceCasting.Click += async (_, __) => await DeleteVoiceCastingAsync();

        btnSaveRole.Click += async (_, __) => await SaveRoleAsync();
        btnDeleteRole.Click += async (_, __) => await DeleteRoleAsync();

        cboOvSeries.SelectedIndexChanged += async (_, __) => await ReloadRoleOverridesAsync();
        btnSaveOverride.Click += async (_, __) => await SaveRoleOverrideAsync();
        btnDeleteOverride.Click += async (_, __) => await DeleteRoleOverrideAsync();

        cboEtsSeries.SelectedIndexChanged += async (_, __) => await ReloadEpisodesForEtsAsync();
        cboEtsEpisode.SelectedIndexChanged += async (_, __) => await ReloadEpisodeThemeSongsAsync();
        btnSaveEts.Click += async (_, __) => await SaveEpisodeThemeSongAsync();
        btnDeleteEts.Click += async (_, __) => await DeleteEpisodeThemeSongAsync();

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

        // v1.2.0 工程 A: 企業屋号タブ
        cboCaCompany.SelectedIndexChanged += async (_, __) => await ReloadCompanyAliasesAsync();
        btnNewCompanyAlias.Click += (_, __) => ClearCompanyAliasForm();
        btnSaveCompanyAlias.Click += async (_, __) => await SaveCompanyAliasAsync();
        btnDeleteCompanyAlias.Click += async (_, __) => await DeleteCompanyAliasAsync();

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

        Load += async (_, __) => await LoadAllAsync();
    }

    /// <summary>
    /// 全タブの初期データを 1 度に読み込む。コンボの選択肢初期化もここで行う。
    /// </summary>
    private async Task LoadAllAsync()
    {
        try
        {
            // 個別マスタ
            gridPersons.DataSource = (await _personsRepo.GetAllAsync()).ToList();
            gridCompanies.DataSource = (await _companiesRepo.GetAllAsync()).ToList();
            gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();
            gridSeriesKinds.DataSource = (await _seriesKindsRepo.GetAllAsync()).ToList();
            gridPartTypes.DataSource = (await _partTypesRepo.GetAllAsync()).ToList();

            // 声優キャスティングタブ：キャラクターコンボへバインド
            var characters = await _charactersRepo.GetAllAsync();
            cboVcCharacter.DisplayMember = "Label";
            cboVcCharacter.ValueMember = "Id";
            cboVcCharacter.DataSource = characters
                .Select(c => new IdLabel(c.CharacterId, $"#{c.CharacterId}  {c.Name}"))
                .ToList();
            if (characters.Count > 0) await ReloadVoiceCastingsAsync();

            // シリーズ書式上書きタブ：シリーズ・役職コンボへバインド
            var series = await _seriesRepo.GetAllAsync();
            var seriesItems = series.Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}")).ToList();
            cboOvSeries.DisplayMember = "Label";
            cboOvSeries.ValueMember = "Id";
            cboOvSeries.DataSource = seriesItems;

            cboOvRole.DisplayMember = "Label";
            cboOvRole.ValueMember = "Id";
            // 役職コンボは role_code を ID 代わりに利用（string）
            cboOvRole.DataSource = (await _rolesRepo.GetAllAsync())
                .Select(r => new CodeLabel(r.RoleCode, $"{r.RoleCode}  {r.NameJa}"))
                .ToList();
            if (seriesItems.Count > 0) await ReloadRoleOverridesAsync();

            // エピソード主題歌タブ：シリーズコンボへバインド（エピソードはシリーズ選択後に絞り込み）
            cboEtsSeries.DisplayMember = "Label";
            cboEtsSeries.ValueMember = "Id";
            cboEtsSeries.DataSource = seriesItems.Select(x => new IdLabel(x.Id, x.Label)).ToList();
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

    private void OnCharacterRowSelected()
    {
        if (gridCharacters.CurrentRow?.DataBoundItem is Character c)
        {
            txtChName.Text = c.Name;
            txtChNameKana.Text = c.NameKana ?? "";
            cboChKind.SelectedItem = c.CharacterKind;
            txtChNotes.Text = c.Notes ?? "";
        }
    }

    private void ClearCharacterForm()
    {
        gridCharacters.ClearSelection();
        txtChName.Text = ""; txtChNameKana.Text = "";
        cboChKind.SelectedItem = "MAIN";
        txtChNotes.Text = "";
    }

    private async Task SaveCharacterAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtChName.Text))
            { MessageBox.Show(this, "名前は必須です。"); return; }
            var kind = (cboChKind.SelectedItem as string) ?? "MAIN";

            if (gridCharacters.CurrentRow?.DataBoundItem is Character current && current.CharacterId > 0
                && gridCharacters.SelectedRows.Count > 0)
            {
                current.Name = txtChName.Text.Trim();
                current.NameKana = NullIfEmpty(txtChNameKana.Text);
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
                    CharacterKind = kind,
                    Notes = NullIfEmpty(txtChNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _charactersRepo.InsertAsync(c);
            }
            gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
            // 声優キャスティングタブのキャラコンボも更新
            cboVcCharacter.DataSource = (await _charactersRepo.GetAllAsync())
                .Select(x => new IdLabel(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
            // v1.2.0 工程 A: キャラクター名義タブのキャラコンボも追随更新
            cboCaaCharacter.DataSource = (await _charactersRepo.GetAllAsync())
                .Select(x => new IdLabel(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
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
    // 声優キャスティングタブ
    // ────────────────────────────────────────────────────────────────────

    private async Task ReloadVoiceCastingsAsync()
    {
        try
        {
            if (cboVcCharacter.SelectedValue is not int characterId) return;
            gridVoiceCastings.DataSource = (await _voiceCastingsRepo.GetByCharacterAsync(characterId)).ToList();
            ClearVoiceCastingForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnVoiceCastingRowSelected()
    {
        if (gridVoiceCastings.CurrentRow?.DataBoundItem is CharacterVoiceCasting vc)
        {
            numVcPersonId.Value = vc.PersonId;
            cboVcKind.SelectedItem = vc.CastingKind;
            SetDateOrNull(dtVcFrom, chkVcFromNull, vc.ValidFrom);
            SetDateOrNull(dtVcTo, chkVcToNull, vc.ValidTo);
            txtVcNotes.Text = vc.Notes ?? "";
        }
    }

    private void ClearVoiceCastingForm()
    {
        gridVoiceCastings.ClearSelection();
        numVcPersonId.Value = 0;
        lblVcPersonName.Text = "";
        cboVcKind.SelectedItem = "REGULAR";
        chkVcFromNull.Checked = true; chkVcToNull.Checked = true;
        txtVcNotes.Text = "";
    }

    /// <summary>person_id 入力欄が変わるたびに、人物名をラベルに表示する補助。</summary>
    private async Task ResolveVoicePersonNameAsync()
    {
        try
        {
            int id = (int)numVcPersonId.Value;
            if (id <= 0) { lblVcPersonName.Text = ""; return; }
            var p = await _personsRepo.GetByIdAsync(id);
            lblVcPersonName.Text = p is null ? "(該当なし)" : $"→ {p.FullName}";
        }
        catch { lblVcPersonName.Text = ""; }
    }

    private async Task SaveVoiceCastingAsync()
    {
        try
        {
            if (cboVcCharacter.SelectedValue is not int characterId)
            { MessageBox.Show(this, "キャラクターを選択してください。"); return; }
            int personId = (int)numVcPersonId.Value;
            if (personId <= 0)
            { MessageBox.Show(this, "声優の person_id を入力してください。"); return; }
            string kind = (cboVcKind.SelectedItem as string) ?? "REGULAR";

            if (gridVoiceCastings.CurrentRow?.DataBoundItem is CharacterVoiceCasting current
                && current.CastingId > 0 && gridVoiceCastings.SelectedRows.Count > 0)
            {
                current.CharacterId = characterId;
                current.PersonId = personId;
                current.CastingKind = kind;
                current.ValidFrom = chkVcFromNull.Checked ? null : dtVcFrom.Value.Date;
                current.ValidTo = chkVcToNull.Checked ? null : dtVcTo.Value.Date;
                current.Notes = NullIfEmpty(txtVcNotes.Text);
                current.UpdatedBy = Environment.UserName;
                await _voiceCastingsRepo.UpdateAsync(current);
            }
            else
            {
                var vc = new CharacterVoiceCasting
                {
                    CharacterId = characterId,
                    PersonId = personId,
                    CastingKind = kind,
                    ValidFrom = chkVcFromNull.Checked ? null : dtVcFrom.Value.Date,
                    ValidTo = chkVcToNull.Checked ? null : dtVcTo.Value.Date,
                    Notes = NullIfEmpty(txtVcNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _voiceCastingsRepo.InsertAsync(vc);
            }
            await ReloadVoiceCastingsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteVoiceCastingAsync()
    {
        try
        {
            if (gridVoiceCastings.CurrentRow?.DataBoundItem is not CharacterVoiceCasting vc)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"キャスティング #{vc.CastingId} を論理削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _voiceCastingsRepo.SoftDeleteAsync(vc.CastingId, Environment.UserName);
            await ReloadVoiceCastingsAsync();
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
            txtRoleFormatTemplate.Text = r.DefaultFormatTemplate ?? "";
            numRoleDisplayOrder.Value = r.DisplayOrder ?? 0;
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
                DefaultFormatTemplate = NullIfEmpty(txtRoleFormatTemplate.Text),
                DisplayOrder = order,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _rolesRepo.UpsertAsync(r);
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();

            // シリーズ書式上書きタブの役職コンボも追随
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
    // シリーズ書式上書きタブ
    // ────────────────────────────────────────────────────────────────────

    private async Task ReloadRoleOverridesAsync()
    {
        try
        {
            if (cboOvSeries.SelectedValue is not int seriesId) return;
            gridRoleOverrides.DataSource = (await _roleOverridesRepo.GetBySeriesAsync(seriesId)).ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnRoleOverrideRowSelected()
    {
        if (gridRoleOverrides.CurrentRow?.DataBoundItem is SeriesRoleFormatOverride o)
        {
            cboOvRole.SelectedValue = o.RoleCode;
            dtOvFrom.Value = o.ValidFrom;
            SetDateOrNull(dtOvTo, chkOvToNull, o.ValidTo);
            txtOvFormatTemplate.Text = o.FormatTemplate;
            txtOvNotes.Text = o.Notes ?? "";
        }
    }

    private async Task SaveRoleOverrideAsync()
    {
        try
        {
            if (cboOvSeries.SelectedValue is not int seriesId)
            { MessageBox.Show(this, "シリーズを選択してください。"); return; }
            if (cboOvRole.SelectedValue is not string roleCode || string.IsNullOrEmpty(roleCode))
            { MessageBox.Show(this, "役職を選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtOvFormatTemplate.Text))
            { MessageBox.Show(this, "書式テンプレは必須です。"); return; }

            var o = new SeriesRoleFormatOverride
            {
                SeriesId = seriesId,
                RoleCode = roleCode,
                ValidFrom = dtOvFrom.Value.Date,
                ValidTo = chkOvToNull.Checked ? null : dtOvTo.Value.Date,
                FormatTemplate = txtOvFormatTemplate.Text.Trim(),
                Notes = NullIfEmpty(txtOvNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _roleOverridesRepo.UpsertAsync(o);
            await ReloadRoleOverridesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteRoleOverrideAsync()
    {
        try
        {
            if (gridRoleOverrides.CurrentRow?.DataBoundItem is not SeriesRoleFormatOverride o)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this,
                $"({o.SeriesId}, {o.RoleCode}, {o.ValidFrom:yyyy-MM-dd}) を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _roleOverridesRepo.DeleteAsync(o.SeriesId, o.RoleCode, o.ValidFrom);
            await ReloadRoleOverridesAsync();
        }
        catch (Exception ex) { ShowError(ex); }
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
            cboEtsThemeKind.SelectedItem = t.ThemeKind;
            numEtsInsertSeq.Value = t.InsertSeq;
            numEtsSongRecordingId.Value = t.SongRecordingId;
            if (t.LabelCompanyAliasId.HasValue)
            {
                numEtsLabelCompanyAliasId.Value = t.LabelCompanyAliasId.Value;
                chkEtsLabelNull.Checked = false;
                numEtsLabelCompanyAliasId.Enabled = true;
            }
            else
            {
                chkEtsLabelNull.Checked = true;
                numEtsLabelCompanyAliasId.Enabled = false;
            }
            txtEtsNotes.Text = t.Notes ?? "";
        }
    }

    private async Task SaveEpisodeThemeSongAsync()
    {
        try
        {
            if (cboEtsEpisode.SelectedValue is not int episodeId)
            { MessageBox.Show(this, "エピソードを選択してください。"); return; }
            string themeKind = (cboEtsThemeKind.SelectedItem as string) ?? "OP";
            byte insertSeq = (byte)numEtsInsertSeq.Value;
            // OP / ED は insert_seq=0 強制、INSERT は >=1
            if (themeKind != "INSERT") insertSeq = 0;
            else if (insertSeq < 1) insertSeq = 1;
            int songRecordingId = (int)numEtsSongRecordingId.Value;
            if (songRecordingId <= 0)
            { MessageBox.Show(this, "song_recording_id を指定してください。"); return; }

            var t = new EpisodeThemeSong
            {
                EpisodeId = episodeId,
                ThemeKind = themeKind,
                InsertSeq = insertSeq,
                SongRecordingId = songRecordingId,
                LabelCompanyAliasId = chkEtsLabelNull.Checked ? null : (int)numEtsLabelCompanyAliasId.Value,
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
            if (MessageBox.Show(this,
                $"エピソード#{t.EpisodeId} {t.ThemeKind}#{t.InsertSeq} を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _episodeThemeSongsRepo.DeleteAsync(t.EpisodeId, t.ThemeKind, t.InsertSeq);
            await ReloadEpisodeThemeSongsAsync();
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
        txtPaName.Text = ""; txtPaNameKana.Text = "";
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
        txtCaName.Text = ""; txtCaNameKana.Text = "";
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
            SetDateOrNull(dtCaaFrom, chkCaaFromNull, a.ValidFrom);
            SetDateOrNull(dtCaaTo, chkCaaToNull, a.ValidTo);
            txtCaaNotes.Text = a.Notes ?? "";
        }
    }

    private void ClearCharacterAliasForm()
    {
        gridCharacterAliases.ClearSelection();
        txtCaaName.Text = ""; txtCaaNameKana.Text = "";
        chkCaaFromNull.Checked = true; chkCaaToNull.Checked = true;
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
                current.ValidFrom = chkCaaFromNull.Checked ? null : dtCaaFrom.Value.Date;
                current.ValidTo = chkCaaToNull.Checked ? null : dtCaaTo.Value.Date;
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
                    ValidFrom = chkCaaFromNull.Checked ? null : dtCaaFrom.Value.Date,
                    ValidTo = chkCaaToNull.Checked ? null : dtCaaTo.Value.Date,
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
}
