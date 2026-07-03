using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジット系マスタ管理フォームの人物名義タブ／企業屋号タブ／ロゴタブ／キャラクター名義タブ群。（タブ単位の partial 分割。ロジックは本体と共通の部分クラス）</summary>
public partial class CreditMastersEditorForm
{
    // 人物名義タブ

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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択行 → 編集パネル反映 + 共同名義リストの更新を非同期で行う。</summary>
    private async Task OnPersonAliasRowSelectedAsync()
    {
        if (gridPersonAliases.CurrentRow?.DataBoundItem is PersonAlias a)
        {
            txtPaName.Text = a.Name;
            txtPaNameKana.Text = a.NameKana ?? "";
            txtPaNameEn.Text = a.NameEn ?? "";
            // display_text_override を読み込む
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
        txtPaName.Text = ""; txtPaNameKana.Text = ""; txtPaNameEn.Text = "";
        // display_text_override も初期化
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
        catch (Exception ex) { this.ShowError(ex); }
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

            // 名義の kana / en が空のとき、紐づく人物からコピー補完する。
            // kana は人物側 full_name_kana があればコピー（無ければ補完しない）。
            // en は人物側 name_en 優先、空なら名義名のローマ字へフォールバック。
            if (IsBlank(txtPaNameKana.Text) || IsBlank(txtPaNameEn.Text))
            {
                var srcPerson = await _personsRepo.GetByIdAsync(personId);
                var cands = new List<FillCandidate>();
                string? kanaFill = null, enFill = null;

                if (IsBlank(txtPaNameKana.Text) && srcPerson is not null
                    && !IsBlank(srcPerson.FullNameKana))
                {
                    kanaFill = srcPerson.FullNameKana!.Trim();
                    cands.Add(new FillCandidate("かな (name_kana)", kanaFill, "人物からコピー"));
                }

                if (IsBlank(txtPaNameEn.Text))
                {
                    enFill = ResolveEnFill(
                        txtPaNameEn.Text, srcPerson?.NameEn,
                        name: txtPaName.Text.Trim(), out string enSrc);
                    if (enFill is not null)
                        cands.Add(new FillCandidate("英語表記 (name_en)", enFill, enSrc));
                }

                if (cands.Count > 0 && ConfirmAutoFill(cands))
                {
                    if (kanaFill is not null) txtPaNameKana.Text = kanaFill;
                    if (enFill is not null) txtPaNameEn.Text = enFill;
                }
            }

            int? pred = numPaPredecessor.Value > 0 ? (int)numPaPredecessor.Value : null;
            int? succ = numPaSuccessor.Value > 0 ? (int)numPaSuccessor.Value : null;

            if (gridPersonAliases.CurrentRow?.DataBoundItem is PersonAlias current
                && current.AliasId > 0 && gridPersonAliases.SelectedRows.Count > 0)
            {
                // 既存名義の更新（中間表は触らない。共同名義の追加・解除は専用ボタンで行う）
                current.Name = txtPaName.Text.Trim();
                current.NameKana = NullIfEmpty(txtPaNameKana.Text);
                current.NameEn = NullIfEmpty(txtPaNameEn.Text);
                // display_text_override の保存
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
                    NameEn = NullIfEmpty(txtPaNameEn.Text),
                    // display_text_override の保存
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
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>共同名義リストの 1 行表示用の (PersonId, ラベル) ペア。</summary>
    private sealed class JointPersonItem
    {
        public int PersonId { get; }
        public string Label { get; }
        public JointPersonItem(int personId, string label) { PersonId = personId; Label = label; }
        public override string ToString() => Label;
    }

    // 企業屋号タブ

    /// <summary>選択中企業に紐づく屋号一覧を読み直す。</summary>
    private async Task ReloadCompanyAliasesAsync()
    {
        try
        {
            if (cboCaCompany.SelectedValue is not int companyId) return;
            gridCompanyAliases.DataSource = (await _companyAliasesRepo.GetByCompanyAsync(companyId)).ToList();
            ClearCompanyAliasForm();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private void OnCompanyAliasRowSelected()
    {
        if (gridCompanyAliases.CurrentRow?.DataBoundItem is CompanyAlias a)
        {
            txtCaName.Text = a.Name;
            txtCaNameKana.Text = a.NameKana ?? "";
            txtCaNameEn.Text = a.NameEn ?? "";
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
        txtCaName.Text = ""; txtCaNameKana.Text = ""; txtCaNameEn.Text = "";
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

            // 屋号の kana / en が空のとき、紐づく企業からコピー補完する。
            // kana は企業側 name_kana があればコピー（無ければ補完しない）。
            // en は企業側 name_en 優先、空なら屋号名のローマ字へフォールバック。
            if (IsBlank(txtCaNameKana.Text) || IsBlank(txtCaNameEn.Text))
            {
                var srcCompany = await _companiesRepo.GetByIdAsync(companyId);
                var cands = new List<FillCandidate>();
                string? kanaFill = null, enFill = null;

                if (IsBlank(txtCaNameKana.Text) && srcCompany is not null
                    && !IsBlank(srcCompany.NameKana))
                {
                    kanaFill = srcCompany.NameKana!.Trim();
                    cands.Add(new FillCandidate("かな (name_kana)", kanaFill, "企業からコピー"));
                }

                if (IsBlank(txtCaNameEn.Text))
                {
                    enFill = ResolveEnFill(
                        txtCaNameEn.Text, srcCompany?.NameEn,
                        name: txtCaName.Text.Trim(), out string enSrc);
                    if (enFill is not null)
                        cands.Add(new FillCandidate("英語表記 (name_en)", enFill, enSrc));
                }

                if (cands.Count > 0 && ConfirmAutoFill(cands))
                {
                    if (kanaFill is not null) txtCaNameKana.Text = kanaFill;
                    if (enFill is not null) txtCaNameEn.Text = enFill;
                }
            }

            int? pred = numCaPredecessor.Value > 0 ? (int)numCaPredecessor.Value : null;
            int? succ = numCaSuccessor.Value > 0 ? (int)numCaSuccessor.Value : null;

            if (gridCompanyAliases.CurrentRow?.DataBoundItem is CompanyAlias current
                && current.AliasId > 0 && gridCompanyAliases.SelectedRows.Count > 0)
            {
                current.CompanyId = companyId;
                current.Name = txtCaName.Text.Trim();
                current.NameKana = NullIfEmpty(txtCaNameKana.Text);
                current.NameEn = NullIfEmpty(txtCaNameEn.Text);
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
                    NameEn = NullIfEmpty(txtCaNameEn.Text),
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
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    // ロゴタブ

    /// <summary>企業選択に連動して屋号コンボを再構築する。</summary>
    private async Task ReloadLgCompanyAliasComboAsync()
    {
        try
        {
            if (cboLgCompany.SelectedValue is not int companyId)
            {
                cboLgCompanyAlias.DataSource = new List<IdLabel<int>>();
                gridLogos.DataSource = new List<Logo>();
                return;
            }
            var aliases = await _companyAliasesRepo.GetByCompanyAsync(companyId);
            cboLgCompanyAlias.DisplayMember = "Label";
            cboLgCompanyAlias.ValueMember = "Id";
            cboLgCompanyAlias.DataSource = aliases
                .Select(a => new IdLabel<int>(a.AliasId, $"#{a.AliasId}  {a.Name}"))
                .ToList();
            if (aliases.Count > 0) await ReloadLogosAsync();
            else gridLogos.DataSource = new List<Logo>();
        }
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    // キャラクター名義タブ

    /// <summary>選択中キャラクターに紐づく名義一覧を読み直す。</summary>
    private async Task ReloadCharacterAliasesAsync()
    {
        try
        {
            if (cboCaaCharacter.SelectedValue is not int characterId) return;
            gridCharacterAliases.DataSource = (await _characterAliasesRepo.GetByCharacterAsync(characterId)).ToList();
            ClearCharacterAliasForm();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private void OnCharacterAliasRowSelected()
    {
        if (gridCharacterAliases.CurrentRow?.DataBoundItem is CharacterAlias a)
        {
            txtCaaName.Text = a.Name;
            txtCaaNameKana.Text = a.NameKana ?? "";
            txtCaaNameEn.Text = a.NameEn ?? "";
            txtCaaNotes.Text = a.Notes ?? "";
        }
    }

    private void ClearCharacterAliasForm()
    {
        gridCharacterAliases.ClearSelection();
        txtCaaName.Text = ""; txtCaaNameKana.Text = ""; txtCaaNameEn.Text = "";
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

            // 名義の kana / en が空のとき、紐づくキャラクターからコピー補完する。
            // kana はキャラ側 name_kana があればコピー（無ければ補完しない）。
            // en はキャラ側 name_en 優先、空なら名義名のローマ字へフォールバック。
            if (IsBlank(txtCaaNameKana.Text) || IsBlank(txtCaaNameEn.Text))
            {
                var srcChar = await _charactersRepo.GetByIdAsync(characterId);
                var cands = new List<FillCandidate>();
                string? kanaFill = null, enFill = null;

                if (IsBlank(txtCaaNameKana.Text) && srcChar is not null
                    && !IsBlank(srcChar.NameKana))
                {
                    kanaFill = srcChar.NameKana!.Trim();
                    cands.Add(new FillCandidate("かな (name_kana)", kanaFill, "キャラクターからコピー"));
                }

                if (IsBlank(txtCaaNameEn.Text))
                {
                    enFill = ResolveEnFill(
                        txtCaaNameEn.Text, srcChar?.NameEn,
                        name: txtCaaName.Text.Trim(), out string enSrc);
                    if (enFill is not null)
                        cands.Add(new FillCandidate("英語表記 (name_en)", enFill, enSrc));
                }

                if (cands.Count > 0 && ConfirmAutoFill(cands))
                {
                    if (kanaFill is not null) txtCaaNameKana.Text = kanaFill;
                    if (enFill is not null) txtCaaNameEn.Text = enFill;
                }
            }

            if (gridCharacterAliases.CurrentRow?.DataBoundItem is CharacterAlias current
                && current.AliasId > 0 && gridCharacterAliases.SelectedRows.Count > 0)
            {
                current.CharacterId = characterId;
                current.Name = txtCaaName.Text.Trim();
                current.NameKana = NullIfEmpty(txtCaaNameKana.Text);
                current.NameEn = NullIfEmpty(txtCaaNameEn.Text);
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
                    NameEn = NullIfEmpty(txtCaaNameEn.Text),
                    Notes = NullIfEmpty(txtCaaNotes.Text),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                };
                await _characterAliasesRepo.InsertAsync(a);
            }
            await ReloadCharacterAliasesAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    // 名寄せ機能：付け替え／改名のクリックハンドラ

    /// <summary>選択中の人物名義を別人物に付け替える。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択中の人物名義を改名する。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    //  ユニットメンバー編集

    /// <summary>「ユニットメンバー編集...」ボタンのハンドラ。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択中の企業屋号を別企業に付け替える。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択中の企業屋号を改名する。</summary>
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
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択中のキャラ名義を別キャラに付け替える。</summary>
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
                    .Select(x => new IdLabel<int>(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
                await ReloadCharacterAliasesAsync();
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>選択中のキャラ名義を改名する。</summary>
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
                    .Select(x => new IdLabel<int>(x.CharacterId, $"#{x.CharacterId}  {x.Name}")).ToList();
                await ReloadCharacterAliasesAsync();
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>人物名義タブの上部「親人物」コンボを再構築する。 既存の <see cref="LoadAllAsync"/> 内のロジックと同じ流れで人物リストを再投入する。</summary>
    private async Task ReloadPersonsForAliasTabAsync()
    {
        var persons = await _personsRepo.GetAllAsync();
        cboPaPerson.DataSource = persons
            .Select(p => new IdLabel<int>(p.PersonId, $"#{p.PersonId}  {p.FullName}"))
            .ToList();
    }

    /// <summary>企業屋号タブの上部「親企業」コンボを再構築する。</summary>
    private async Task ReloadCompaniesForAliasTabAsync()
    {
        var companies = await _companiesRepo.GetAllAsync();
        cboCaCompany.DataSource = companies
            .Select(c => new IdLabel<int>(c.CompanyId, $"#{c.CompanyId}  {c.Name}"))
            .ToList();
    }
}
