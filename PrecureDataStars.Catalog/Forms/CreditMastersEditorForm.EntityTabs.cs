using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジット系マスタ管理フォームの人物タブ／企業タブ／キャラクタータブ群。（タブ単位の partial 分割。ロジックは本体と共通の部分クラス）</summary>
public partial class CreditMastersEditorForm
{
    // 人物タブ

    /// <summary>誕生日入力欄にモデル値（生年・公開可否・月・日）を流し込む。</summary>
    private static void LoadBirthdayControls(
        NumericUpDown nudYear, CheckBox chkUnknown, ComboBox cboVis,
        ComboBox cboMonth, ComboBox cboDay,
        ushort? birthYear, string birthYearVisibility, byte? birthMonth, byte? birthDay)
    {
        if (birthYear.HasValue)
        {
            chkUnknown.Checked = false;
            nudYear.Enabled = true;
            decimal v = birthYear.Value;
            if (v < nudYear.Minimum) v = nudYear.Minimum;
            if (v > nudYear.Maximum) v = nudYear.Maximum;
            nudYear.Value = v;
        }
        else
        {
            chkUnknown.Checked = true;
            nudYear.Enabled = false;
        }
        // 公開可否：'PRIVATE' のみ index 1（非公開）、それ以外は既定の公開。
        cboVis.SelectedIndex =
            string.Equals(birthYearVisibility, "PRIVATE", StringComparison.Ordinal) ? 1 : 0;
        // 月／日：index 0 が「(未)」= NULL。値ありは index = 値。
        cboMonth.SelectedIndex = birthMonth.HasValue ? birthMonth.Value : 0;
        cboDay.SelectedIndex = birthDay.HasValue ? birthDay.Value : 0;
    }

    /// <summary>誕生日入力欄からモデル値（生年・公開可否・月・日）を読み出す。</summary>
    private static (ushort? Year, string Visibility, byte? Month, byte? Day) ReadBirthdayControls(
        NumericUpDown nudYear, CheckBox chkUnknown, ComboBox cboVis,
        ComboBox cboMonth, ComboBox cboDay)
    {
        ushort? year = chkUnknown.Checked ? (ushort?)null : (ushort)nudYear.Value;
        string vis = cboVis.SelectedIndex == 1 ? "PRIVATE" : "PUBLIC";
        byte? month = cboMonth.SelectedIndex > 0 ? (byte)cboMonth.SelectedIndex : null;
        byte? day = cboDay.SelectedIndex > 0 ? (byte)cboDay.SelectedIndex : null;
        return (year, vis, month, day);
    }

    private void OnPersonRowSelected()
    {
        if (gridPersons.CurrentRow?.DataBoundItem is Person p)
        {
            txtPFamily.Text = p.FamilyName ?? "";
            txtPGiven.Text = p.GivenName ?? "";
            txtPFullName.Text = p.FullName;
            txtPFullNameKana.Text = p.FullNameKana ?? "";
            txtPNameEn.Text = p.NameEn ?? "";
            LoadBirthdayControls(nudPBirthYear, chkPBirthYearUnknown, cboPBirthYearVis,
                cboPBirthMonth, cboPBirthDay,
                p.BirthYear, p.BirthYearVisibility, p.BirthMonth, p.BirthDay);
            txtPNotes.Text = p.Notes ?? "";
            txtPOfficialUrl.Text = p.OfficialUrl ?? "";
            txtPXUrl.Text = p.XUrl ?? "";
            txtPInstagramUrl.Text = p.InstagramUrl ?? "";
            txtPYoutubeUrl.Text = p.YoutubeUrl ?? "";
            txtPWikipediaUrl.Text = p.WikipediaUrl ?? "";
        }
    }

    private void ClearPersonForm()
    {
        gridPersons.ClearSelection();
        txtPFamily.Text = ""; txtPGiven.Text = "";
        txtPFullName.Text = ""; txtPFullNameKana.Text = "";
        txtPNameEn.Text = ""; txtPNotes.Text = "";
        txtPOfficialUrl.Text = ""; txtPXUrl.Text = "";
        txtPInstagramUrl.Text = ""; txtPYoutubeUrl.Text = "";
        txtPWikipediaUrl.Text = "";
        LoadBirthdayControls(nudPBirthYear, chkPBirthYearUnknown, cboPBirthYearVis,
            cboPBirthMonth, cboPBirthDay, null, "PUBLIC", null, null);
    }

    private async Task SavePersonAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtPFullName.Text))
            { MessageBox.Show(this, "フルネームは必須です。"); return; }

            // かな（full_name_kana）が入っていて英語（name_en）が空のとき、
            if (!IsBlank(txtPFullNameKana.Text) && IsBlank(txtPNameEn.Text))
            {
                string? enFill = ResolveEnFill(
                    txtPNameEn.Text, sourceEn: null,
                    name: txtPFullNameKana.Text.Trim(), out string enSrc);
                if (enFill is not null
                    && ConfirmAutoFill(new[]
                    {
                        new FillCandidate("英語表記 (name_en)", enFill, enSrc),
                    }))
                {
                    txtPNameEn.Text = enFill;
                }
            }

            // 選択行が無い、または「新規」直後はインサート、それ以外は選択行 ID をキーに更新
            if (gridPersons.CurrentRow?.DataBoundItem is Person current && current.PersonId > 0
                && gridPersons.SelectedRows.Count > 0)
            {
                current.FamilyName = NullIfEmpty(txtPFamily.Text);
                current.GivenName = NullIfEmpty(txtPGiven.Text);
                current.FullName = txtPFullName.Text.Trim();
                current.FullNameKana = NullIfEmpty(txtPFullNameKana.Text);
                current.NameEn = NullIfEmpty(txtPNameEn.Text);
                var pbd = ReadBirthdayControls(nudPBirthYear, chkPBirthYearUnknown,
                    cboPBirthYearVis, cboPBirthMonth, cboPBirthDay);
                current.BirthYear = pbd.Year;
                current.BirthYearVisibility = pbd.Visibility;
                current.BirthMonth = pbd.Month;
                current.BirthDay = pbd.Day;
                current.Notes = NullIfEmpty(txtPNotes.Text);
                current.OfficialUrl = NullIfEmpty(txtPOfficialUrl.Text);
                current.XUrl = NullIfEmpty(txtPXUrl.Text);
                current.InstagramUrl = NullIfEmpty(txtPInstagramUrl.Text);
                current.YoutubeUrl = NullIfEmpty(txtPYoutubeUrl.Text);
                current.WikipediaUrl = NullIfEmpty(txtPWikipediaUrl.Text);
                current.UpdatedBy = Environment.UserName;
                await _personsRepo.UpdateAsync(current);
            }
            else
            {
                var pbd = ReadBirthdayControls(nudPBirthYear, chkPBirthYearUnknown,
                    cboPBirthYearVis, cboPBirthMonth, cboPBirthDay);
                var p = new Person
                {
                    FamilyName = NullIfEmpty(txtPFamily.Text),
                    GivenName = NullIfEmpty(txtPGiven.Text),
                    FullName = txtPFullName.Text.Trim(),
                    FullNameKana = NullIfEmpty(txtPFullNameKana.Text),
                    NameEn = NullIfEmpty(txtPNameEn.Text),
                    BirthYear = pbd.Year,
                    BirthYearVisibility = pbd.Visibility,
                    BirthMonth = pbd.Month,
                    BirthDay = pbd.Day,
                    Notes = NullIfEmpty(txtPNotes.Text),
                    OfficialUrl = NullIfEmpty(txtPOfficialUrl.Text),
                    XUrl = NullIfEmpty(txtPXUrl.Text),
                    InstagramUrl = NullIfEmpty(txtPInstagramUrl.Text),
                    YoutubeUrl = NullIfEmpty(txtPYoutubeUrl.Text),
                    WikipediaUrl = NullIfEmpty(txtPWikipediaUrl.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _personsRepo.InsertAsync(p);
            }

            gridPersons.DataSource = (await _personsRepo.GetAllAsync()).ToList();
            // 人物名義タブの人物コンボも追随更新
            cboPaPerson.DataSource = (await _personsRepo.GetAllAsync())
                .Select(x => new IdLabel<int>(x.PersonId, $"#{x.PersonId}  {x.FullName}")).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    // 企業タブ

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
            txtCOfficialUrl.Text = c.OfficialUrl ?? "";
            txtCXUrl.Text = c.XUrl ?? "";
            txtCInstagramUrl.Text = c.InstagramUrl ?? "";
            txtCYoutubeUrl.Text = c.YoutubeUrl ?? "";
            txtCWikipediaUrl.Text = c.WikipediaUrl ?? "";
        }
    }

    private void ClearCompanyForm()
    {
        gridCompanies.ClearSelection();
        txtCName.Text = ""; txtCNameKana.Text = ""; txtCNameEn.Text = "";
        chkCFoundedNull.Checked = true; chkCDissolvedNull.Checked = true;
        txtCNotes.Text = "";
        txtCOfficialUrl.Text = ""; txtCXUrl.Text = "";
        txtCInstagramUrl.Text = ""; txtCYoutubeUrl.Text = "";
        txtCWikipediaUrl.Text = "";
    }

    private async Task SaveCompanyAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtCName.Text))
            { MessageBox.Show(this, "正式名称は必須です。"); return; }

            // かな（name_kana）が入っていて英語（name_en）が空のとき、
            // パスポート式ローマ字で name_en を補完する（確認のうえ入力欄へ反映）。
            if (!IsBlank(txtCNameKana.Text) && IsBlank(txtCNameEn.Text))
            {
                string? enFill = ResolveEnFill(
                    txtCNameEn.Text, sourceEn: null,
                    name: txtCNameKana.Text.Trim(), out string enSrc);
                if (enFill is not null
                    && ConfirmAutoFill(new[]
                    {
                        new FillCandidate("英語表記 (name_en)", enFill, enSrc),
                    }))
                {
                    txtCNameEn.Text = enFill;
                }
            }

            if (gridCompanies.CurrentRow?.DataBoundItem is Company current && current.CompanyId > 0
                && gridCompanies.SelectedRows.Count > 0)
            {
                current.Name = txtCName.Text.Trim();
                current.NameKana = NullIfEmpty(txtCNameKana.Text);
                current.NameEn = NullIfEmpty(txtCNameEn.Text);
                current.FoundedDate = chkCFoundedNull.Checked ? null : dtCFounded.Value.Date;
                current.DissolvedDate = chkCDissolvedNull.Checked ? null : dtCDissolved.Value.Date;
                current.Notes = NullIfEmpty(txtCNotes.Text);
                current.OfficialUrl = NullIfEmpty(txtCOfficialUrl.Text);
                current.XUrl = NullIfEmpty(txtCXUrl.Text);
                current.InstagramUrl = NullIfEmpty(txtCInstagramUrl.Text);
                current.YoutubeUrl = NullIfEmpty(txtCYoutubeUrl.Text);
                current.WikipediaUrl = NullIfEmpty(txtCWikipediaUrl.Text);
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
                    OfficialUrl = NullIfEmpty(txtCOfficialUrl.Text),
                    XUrl = NullIfEmpty(txtCXUrl.Text),
                    InstagramUrl = NullIfEmpty(txtCInstagramUrl.Text),
                    YoutubeUrl = NullIfEmpty(txtCYoutubeUrl.Text),
                    WikipediaUrl = NullIfEmpty(txtCWikipediaUrl.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _companiesRepo.InsertAsync(c);
            }
            gridCompanies.DataSource = (await _companiesRepo.GetAllAsync()).ToList();
            // 企業屋号タブ・ロゴタブの企業コンボも追随更新
            var refreshedCompanies = (await _companiesRepo.GetAllAsync())
                .Select(x => new IdLabel<int>(x.CompanyId, $"#{x.CompanyId}  {x.Name}")).ToList();
            cboCaCompany.DataSource = refreshedCompanies;
            cboLgCompany.DataSource = refreshedCompanies
                .Select(x => new IdLabel<int>(x.Id, x.Label)).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    // キャラクタータブ

    /// <summary>区分コンボにバインドする項目クラス。 CharacterKindsRepository.GetAllAsync() の結果を「コード — 表示名」形式で表示する。</summary>
    private sealed class CharacterKindComboItem
    {
        /// <summary>選択値の実体（ValueMember="KindCode"）。</summary>
        public string KindCode { get; init; } = "";
        /// <summary>表示用文字列（DisplayMember="Display"、例: "MAIN — メイン"）。</summary>
        public string Display { get; init; } = "";
    }

    /// <summary>区分コンボにキャラクター区分マスタをバインドする。 キャラクター区分はマスタ管理のため、DataSource バインドで供給する。 起動時とキャラクター区分マスタの編集後に呼び出される。</summary>
    private async Task BindCharacterKindComboAsync()
    {
        var kinds = await _characterKindsRepo.GetAllAsync().ConfigureAwait(true);
        // CharacterKind モデルの主キープロパティ名は CharacterKindCode（character_kinds.character_kind 列の C# 名）。
        // プロパティ名で参照する（DataGridView の列名と一致させる）。
        cboChKind.DataSource = kinds
            .Select(k => new CharacterKindComboItem
            {
                KindCode = k.CharacterKindCode,
                Display = string.IsNullOrEmpty(k.NameJa) ? k.CharacterKindCode : $"{k.CharacterKindCode} — {k.NameJa}"
            })
            .ToList();
    }

    /// <summary>現在の区分コンボの選択値（KindCode 文字列）を取得する。SelectedValue が string になっているはず （ValueMember 設定により）。何らかの理由で取れない場合は既定値 "MAIN" を返す。</summary>
    private string GetSelectedCharacterKindCode()
    {
        if (cboChKind.SelectedValue is string s && !string.IsNullOrEmpty(s)) return s;
        if (cboChKind.SelectedItem is CharacterKindComboItem item && !string.IsNullOrEmpty(item.KindCode)) return item.KindCode;
        return "MAIN";
    }

    /// <summary>指定の KindCode を区分コンボの選択にする（マッチが無ければ無選択）。</summary>
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
            txtChNameEn.Text = c.NameEn ?? "";
            // マスタバインド方式のため SelectedValue 経由でセットする。
            SetCharacterKindComboValue(c.CharacterKind);
            LoadBirthdayControls(nudChBirthYear, chkChBirthYearUnknown, cboChBirthYearVis,
                cboChBirthMonth, cboChBirthDay,
                c.BirthYear, c.BirthYearVisibility, c.BirthMonth, c.BirthDay);
            txtChNotes.Text = c.Notes ?? "";
            txtChOfficialUrl.Text = c.OfficialUrl ?? "";
            txtChWikipediaUrl.Text = c.WikipediaUrl ?? "";
        }
    }

    private void ClearCharacterForm()
    {
        gridCharacters.ClearSelection();
        txtChName.Text = ""; txtChNameKana.Text = ""; txtChNameEn.Text = "";
        // ハードコードの "MAIN" 文字列セットから、マスタコード経由のセットに変更。
        SetCharacterKindComboValue("MAIN");
        LoadBirthdayControls(nudChBirthYear, chkChBirthYearUnknown, cboChBirthYearVis,
            cboChBirthMonth, cboChBirthDay, null, "PUBLIC", null, null);
        txtChNotes.Text = "";
        txtChOfficialUrl.Text = ""; txtChWikipediaUrl.Text = "";
    }

    private async Task SaveCharacterAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtChName.Text))
            { MessageBox.Show(this, "名前は必須です。"); return; }

            // かな（name_kana）が入っていて英語（name_en）が空のとき、
            // パスポート式ローマ字で name_en を補完する（確認のうえ入力欄へ反映）。
            if (!IsBlank(txtChNameKana.Text) && IsBlank(txtChNameEn.Text))
            {
                string? enFill = ResolveEnFill(
                    txtChNameEn.Text, sourceEn: null,
                    name: txtChNameKana.Text.Trim(), out string enSrc);
                if (enFill is not null
                    && ConfirmAutoFill(new[]
                    {
                        new FillCandidate("英語表記 (name_en)", enFill, enSrc),
                    }))
                {
                    txtChNameEn.Text = enFill;
                }
            }

            // マスタバインド方式に合わせて SelectedValue を取得。
            var kind = GetSelectedCharacterKindCode();

            if (gridCharacters.CurrentRow?.DataBoundItem is Character current && current.CharacterId > 0
                && gridCharacters.SelectedRows.Count > 0)
            {
                current.Name = txtChName.Text.Trim();
                current.NameKana = NullIfEmpty(txtChNameKana.Text);
                current.NameEn = NullIfEmpty(txtChNameEn.Text);
                current.CharacterKind = kind;
                var cbd = ReadBirthdayControls(nudChBirthYear, chkChBirthYearUnknown,
                    cboChBirthYearVis, cboChBirthMonth, cboChBirthDay);
                current.BirthYear = cbd.Year;
                current.BirthYearVisibility = cbd.Visibility;
                current.BirthMonth = cbd.Month;
                current.BirthDay = cbd.Day;
                current.Notes = NullIfEmpty(txtChNotes.Text);
                current.OfficialUrl = NullIfEmpty(txtChOfficialUrl.Text);
                current.WikipediaUrl = NullIfEmpty(txtChWikipediaUrl.Text);
                current.UpdatedBy = Environment.UserName;
                await _charactersRepo.UpdateAsync(current);
            }
            else
            {
                var cbd = ReadBirthdayControls(nudChBirthYear, chkChBirthYearUnknown,
                    cboChBirthYearVis, cboChBirthMonth, cboChBirthDay);
                var c = new Character
                {
                    Name = txtChName.Text.Trim(),
                    NameKana = NullIfEmpty(txtChNameKana.Text),
                    NameEn = NullIfEmpty(txtChNameEn.Text),
                    CharacterKind = kind,
                    BirthYear = cbd.Year,
                    BirthYearVisibility = cbd.Visibility,
                    BirthMonth = cbd.Month,
                    BirthDay = cbd.Day,
                    Notes = NullIfEmpty(txtChNotes.Text),
                    OfficialUrl = NullIfEmpty(txtChOfficialUrl.Text),
                    WikipediaUrl = NullIfEmpty(txtChWikipediaUrl.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _charactersRepo.InsertAsync(c);
            }
            gridCharacters.DataSource = (await _charactersRepo.GetAllAsync()).ToList();
            // キャラクター名義タブのキャラコンボも追随更新
            cboCaaCharacter.DataSource = (await _charactersRepo.GetAllAsync())
                .Select(x => new IdLabel<int>(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
            // プリキュアタブの「変身前後の名義コンボ」と家族関係タブの「自分／相手キャラコンボ」も再ロード
            await RefreshPrecureTabComboSourcesAsync().ConfigureAwait(true);
            await RefreshCharacterFamilyTabComboSourcesAsync().ConfigureAwait(true);
        }
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }
}
