using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット編集フォームの右ペインで使う、エントリ編集用 UserControl
/// （v1.2.0 工程 B-3 追加）。
/// <para>
/// エントリ種別ごとに編集パネルを切り替え、人物・キャラ・企業・ロゴ・歌・テキストの
/// いずれかとして 1 件のエントリを編集する。種別ラジオは既存エントリ編集時には無効化され
/// （種別変更は別エントリとしての追加扱いになるため）、新規追加モードでのみ自由選択できる。
/// </para>
/// <para>
/// 工程 B-3a 段階では、以下の動作までを通す:
///   ・TEXT エントリ（raw_text のみ）の追加・編集・保存・削除
///   ・SONG エントリ（song_recording_id を直接入力）の追加・編集・保存・削除
///   ・PERSON / CHARACTER_VOICE / COMPANY / LOGO は ID 直接入力での保存可（ピッカー結線は B-3b）
///   ・「+ 新規...」によるマスタ自動投入は B-3c で結線
///   ・本放送限定フラグの設定
///   ・entry_seq / notes 編集
/// </para>
/// </summary>
public sealed partial class EntryEditorPanel : UserControl
{
    private CreditBlockEntriesRepository? _entriesRepo;
    private LookupCache? _lookupCache;

    // v1.2.0 工程 B-3b 追加：ピッカーで使う参照系リポジトリ
    private PersonAliasesRepository? _personAliasesRepo;
    private CompanyAliasesRepository? _companyAliasesRepo;
    private CharacterAliasesRepository? _characterAliasesRepo;
    private LogosRepository? _logosRepo;
    private SongRecordingsRepository? _songRecRepo;

    // v1.2.0 工程 B-3c 追加：QuickAdd ダイアログでマスタ自動投入に使うリポジトリ
    private PersonsRepository? _personsRepo;
    private CompaniesRepository? _companiesRepo;

    // v1.2.0 工程 F 追加：キャラ名義 QuickAdd 用のリポジトリ
    private CharactersRepository? _charactersRepo;
    private CharacterKindsRepository? _characterKindsRepo;

    /// <summary>編集中エントリ（null = 新規追加モード）。</summary>
    private CreditBlockEntry? _editing;

    /// <summary>新規追加モード時の追加先 block_id。</summary>
    private int? _newBlockId;

    /// <summary>新規追加モード時の初期 entry_seq（同 block_id × 同 is_broadcast_only グループの max+1）。</summary>
    private ushort _newSeq;

    /// <summary>保存／削除完了時に親フォーム（CreditEditorForm）へ通知してツリーを再構築させるイベント。</summary>
    public event EventHandler? EntrySaved;

    /// <summary>削除完了時に親フォームへ通知するイベント。</summary>
    public event EventHandler? EntryDeleted;

    public EntryEditorPanel()
    {
        InitializeComponent();
        WireRadios();
        btnSave.Click   += async (_, __) => await OnSaveAsync();
        btnDelete.Click += async (_, __) => await OnDeleteAsync();

        // v1.2.0 工程 B-3b 追加：6 個の検索ピッカーボタンを結線
        btnPersonAliasPick.Click          += (_, __) => OnPickPersonAlias();
        btnAffiliationCompanyPick.Click   += (_, __) => OnPickAffiliationCompany();
        btnCharacterAliasPick.Click       += (_, __) => OnPickCharacterAlias();
        btnVoicePersonAliasPick.Click     += (_, __) => OnPickVoicePersonAlias();
        btnCompanyAliasPick.Click         += (_, __) => OnPickCompanyAlias();
        btnLogoPick.Click                 += (_, __) => OnPickLogo();
        btnSongRecordingPick.Click        += (_, __) => OnPickSongRecording();

        // v1.2.0 工程 B-3c 追加：4 個の「+ 新規...」ボタン（QuickAdd ダイアログ）を結線
        btnPersonAliasNew.Click           += (_, __) => OnNewPersonAlias();
        btnVoicePersonAliasNew.Click      += (_, __) => OnNewVoicePersonAlias();
        btnCompanyAliasNew.Click          += (_, __) => OnNewCompanyAlias();
        btnLogoNew.Click                  += (_, __) => OnNewLogo();

        // v1.2.0 工程 F 追加：CHARACTER_VOICE 種別の「+ 新規キャラ名義...」ボタンを結線
        btnCharacterAliasNew.Click        += (_, __) => OnNewCharacterAlias();
    }

    /// <summary>
    /// 親フォームから依存性を流し込む。コンストラクタで DI できないのは、
    /// このコントロールを Designer に置く都合（パラメータなしコンストラクタ必須）から。
    /// LookupCache が internal なので本メソッドの可視性も internal で揃えている。
    /// v1.2.0 工程 B-3b でピッカー用のマスタリポジトリ 5 本を追加引数で受け取るように拡張。
    /// v1.2.0 工程 B-3c で QuickAdd ダイアログ用のリポジトリ 2 本を更に追加。
    /// v1.2.0 工程 F で キャラ名義 QuickAdd 用のリポジトリ 2 本を更に追加。
    /// </summary>
    internal void Initialize(
        CreditBlockEntriesRepository entriesRepo,
        LookupCache lookupCache,
        PersonAliasesRepository personAliasesRepo,
        CompanyAliasesRepository companyAliasesRepo,
        CharacterAliasesRepository characterAliasesRepo,
        LogosRepository logosRepo,
        SongRecordingsRepository songRecRepo,
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        CharacterKindsRepository characterKindsRepo)
    {
        _entriesRepo          = entriesRepo          ?? throw new ArgumentNullException(nameof(entriesRepo));
        _lookupCache          = lookupCache          ?? throw new ArgumentNullException(nameof(lookupCache));
        _personAliasesRepo    = personAliasesRepo    ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _companyAliasesRepo   = companyAliasesRepo   ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _logosRepo            = logosRepo            ?? throw new ArgumentNullException(nameof(logosRepo));
        _songRecRepo          = songRecRepo          ?? throw new ArgumentNullException(nameof(songRecRepo));
        _personsRepo          = personsRepo          ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo        = companiesRepo        ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo       = charactersRepo       ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterKindsRepo   = characterKindsRepo   ?? throw new ArgumentNullException(nameof(characterKindsRepo));
    }

    /// <summary>パネルを「編集対象なし」状態に戻す（保存・削除ボタン無効化、入力欄クリア）。</summary>
    public void ClearAndDisable()
    {
        _editing = null;
        _newBlockId = null;
        _newSeq = 1;
        ClearAllFieldValues();
        SetMode(EditMode.None);
    }

    /// <summary>
    /// 既存エントリの編集モードに切り替える。種別ラジオは固定（変更不可）になる。
    /// </summary>
    public async Task LoadForEditAsync(CreditBlockEntry entry)
    {
        if (_lookupCache is null) throw new InvalidOperationException("Initialize() を先に呼んでください。");
        _editing = entry;
        _newBlockId = null;

        // 種別ラジオを該当のものにセット（イベント発火は SetMode 後に Visible 切替で吸収）
        SelectKindRadio(entry.EntryKind);
        ShowKindPanel(entry.EntryKind);

        // 各種別パネルへ既存値をロード
        numPersonAliasId.Value             = entry.PersonAliasId            ?? 0;
        numCharacterAliasId.Value          = entry.CharacterAliasId         ?? 0;
        txtRawCharacterText.Text           = entry.RawCharacterText         ?? "";
        // CHARACTER_VOICE 用の声優は person_alias_id を共用しているので分けて持たない（B-3 設計通り）
        numVoicePersonAliasId.Value        = entry.PersonAliasId            ?? 0;
        numCompanyAliasId.Value            = entry.CompanyAliasId           ?? 0;
        numLogoId.Value                    = entry.LogoId                   ?? 0;
        numSongRecordingId.Value           = entry.SongRecordingId          ?? 0;
        txtRawText.Text                    = entry.RawText                  ?? "";
        numAffiliationCompanyAliasId.Value = entry.AffiliationCompanyAliasId ?? 0;
        txtAffiliationText.Text            = entry.AffiliationText          ?? "";

        // 共通属性
        chkIsBroadcastOnly.Checked = entry.IsBroadcastOnly;
        numEntrySeq.Value          = entry.EntrySeq;
        txtNotes.Text              = entry.Notes ?? "";

        // 既存エントリは種別変更不可
        SetMode(EditMode.Edit);

        // プレビュー文字列を更新
        await RefreshPreviewsAsync();
    }

    /// <summary>
    /// 新規追加モードに切り替える。種別ラジオは選択可能、初期値は PERSON。
    /// </summary>
    public void LoadForNew(int blockId, bool isBroadcastOnly, ushort newSeq)
    {
        _editing = null;
        _newBlockId = blockId;
        _newSeq = newSeq;

        ClearAllFieldValues();
        rbKindPerson.Checked = true;
        ShowKindPanel("PERSON");
        chkIsBroadcastOnly.Checked = isBroadcastOnly;
        numEntrySeq.Value = newSeq;

        SetMode(EditMode.New);
    }

    // ────────────────────────────────────────────────────────────
    // 内部ヘルパ
    // ────────────────────────────────────────────────────────────

    private enum EditMode { None, New, Edit }

    /// <summary>UI の有効／無効状態を編集モードに合わせて切り替える。</summary>
    private void SetMode(EditMode mode)
    {
        bool isActive = (mode != EditMode.None);

        // 種別ラジオは新規モードでのみ Enabled
        bool kindEditable = (mode == EditMode.New);
        rbKindPerson.Enabled = rbKindCharacterVoice.Enabled = rbKindCompany.Enabled =
            rbKindLogo.Enabled = rbKindSong.Enabled = rbKindText.Enabled = kindEditable;
        lblKindNotice.Text = (mode == EditMode.Edit)
            ? "（既存エントリの種別は変更できません。種別変更は削除→新規追加で行ってください）"
            : "";
        lblKindNotice.AutoSize = true;

        // 入力フィールドは編集中・新規中のみ有効
        SetFieldEditableRecursive(pnlKindHost, isActive);
        chkIsBroadcastOnly.Enabled = isActive;
        numEntrySeq.Enabled = isActive;
        txtNotes.Enabled = isActive;

        // v1.2.0 工程 B-3b 追加：ピッカーボタン 6 個（+ 所属屋号ピッカー）も編集中のみ有効
        btnPersonAliasPick.Enabled         = isActive;
        btnAffiliationCompanyPick.Enabled  = isActive;
        btnCharacterAliasPick.Enabled      = isActive;
        btnVoicePersonAliasPick.Enabled    = isActive;
        btnCompanyAliasPick.Enabled        = isActive;
        btnLogoPick.Enabled                = isActive;
        btnSongRecordingPick.Enabled       = isActive;
        // v1.2.0 工程 B-3c 追加：「+ 新規...」ボタン 4 個も編集中のみ有効
        btnPersonAliasNew.Enabled          = isActive;
        btnVoicePersonAliasNew.Enabled     = isActive;
        btnCompanyAliasNew.Enabled         = isActive;
        btnLogoNew.Enabled                 = isActive;
        // v1.2.0 工程 F 追加：キャラ名義の「+ 新規...」も編集中のみ有効
        btnCharacterAliasNew.Enabled       = isActive;

        // 保存・削除ボタン
        btnSave.Enabled   = isActive;
        btnDelete.Enabled = (mode == EditMode.Edit);

        // ステータス
        lblStatus.Text = mode switch
        {
            EditMode.None => "(ツリーで Block を選んで「+ エントリ」、または既存 Entry を選択してください)",
            EditMode.New  => "新規エントリ追加モード（保存で DB に INSERT されます）",
            EditMode.Edit => $"既存エントリ編集中（entry_id={_editing?.EntryId}）",
            _ => ""
        };
    }

    /// <summary>
    /// pnlKindHost 配下の入力コントロールに対して再帰的に Enabled を設定する。
    /// 種別パネル切替後でも Enabled 状態が一貫するよう、Visible とは独立に管理する。
    /// </summary>
    private void SetFieldEditableRecursive(Control parent, bool enabled)
    {
        foreach (Control c in parent.Controls)
        {
            if (c is NumericUpDown || c is TextBox)
            {
                c.Enabled = enabled;
            }
            else if (c is Panel p)
            {
                SetFieldEditableRecursive(p, enabled);
            }
            // Button は B-3a では常時無効（ピッカー B-3b、+ 新規 B-3c で結線）なので触らない
        }
    }

    /// <summary>種別ラジオの結線：選択変更時に対応パネルを切り替えるだけ。</summary>
    private void WireRadios()
    {
        rbKindPerson.CheckedChanged         += (_, __) => { if (rbKindPerson.Checked)         ShowKindPanel("PERSON"); };
        rbKindCharacterVoice.CheckedChanged += (_, __) => { if (rbKindCharacterVoice.Checked) ShowKindPanel("CHARACTER_VOICE"); };
        rbKindCompany.CheckedChanged        += (_, __) => { if (rbKindCompany.Checked)        ShowKindPanel("COMPANY"); };
        rbKindLogo.CheckedChanged           += (_, __) => { if (rbKindLogo.Checked)           ShowKindPanel("LOGO"); };
        rbKindSong.CheckedChanged           += (_, __) => { if (rbKindSong.Checked)           ShowKindPanel("SONG"); };
        rbKindText.CheckedChanged           += (_, __) => { if (rbKindText.Checked)           ShowKindPanel("TEXT"); };
    }

    /// <summary>指定された entry_kind に対応するパネルだけを Visible=true にする。</summary>
    private void ShowKindPanel(string kind)
    {
        pnlPerson.Visible          = (kind == "PERSON");
        pnlCharacterVoice.Visible  = (kind == "CHARACTER_VOICE");
        pnlCompany.Visible         = (kind == "COMPANY");
        pnlLogo.Visible            = (kind == "LOGO");
        pnlSong.Visible            = (kind == "SONG");
        pnlText.Visible            = (kind == "TEXT");
    }

    /// <summary>指定 entry_kind に対応するラジオを Checked にする（CheckedChanged 経由で表示も切替）。</summary>
    private void SelectKindRadio(string kind)
    {
        switch (kind)
        {
            case "PERSON":          rbKindPerson.Checked = true;         break;
            case "CHARACTER_VOICE": rbKindCharacterVoice.Checked = true; break;
            case "COMPANY":         rbKindCompany.Checked = true;        break;
            case "LOGO":            rbKindLogo.Checked = true;           break;
            case "SONG":            rbKindSong.Checked = true;           break;
            case "TEXT":            rbKindText.Checked = true;           break;
        }
    }

    /// <summary>選択中の種別ラジオに対応する entry_kind 文字列を返す。</summary>
    private string GetSelectedKind()
    {
        if (rbKindPerson.Checked)         return "PERSON";
        if (rbKindCharacterVoice.Checked) return "CHARACTER_VOICE";
        if (rbKindCompany.Checked)        return "COMPANY";
        if (rbKindLogo.Checked)           return "LOGO";
        if (rbKindSong.Checked)           return "SONG";
        return "TEXT";
    }

    /// <summary>すべての入力フィールドを既定値にリセットする。</summary>
    private void ClearAllFieldValues()
    {
        numPersonAliasId.Value = 0;
        numCharacterAliasId.Value = 0;
        txtRawCharacterText.Text = "";
        numVoicePersonAliasId.Value = 0;
        numCompanyAliasId.Value = 0;
        numLogoId.Value = 0;
        numSongRecordingId.Value = 0;
        txtRawText.Text = "";
        numAffiliationCompanyAliasId.Value = 0;
        txtAffiliationText.Text = "";
        chkIsBroadcastOnly.Checked = false;
        numEntrySeq.Value = 1;
        txtNotes.Text = "";

        lblPersonPreview.Text       = "(プレビュー: 名義 ID を入力すると表示されます)";
        lblCharacterPreview.Text    = "(キャラ プレビュー)";
        lblVoicePreview.Text        = "(声優 プレビュー)";
        lblCompanyPreview.Text      = "(企業屋号 プレビュー)";
        lblLogoPreview.Text         = "(ロゴ プレビュー)";
        lblSongPreview.Text         = "(歌録音 プレビュー)";
    }

    /// <summary>各種別パネルの ID から、マスタ参照してプレビュー文字列を更新する。</summary>
    private async Task RefreshPreviewsAsync()
    {
        if (_lookupCache is null) return;
        // 各種別ごとに、ID が入っていればプレビュー解決を試みる。失敗時は "(未解決)" 等を表示。
        if (numPersonAliasId.Value > 0)
        {
            var name = await _lookupCache.LookupPersonAliasNameAsync((int)numPersonAliasId.Value);
            lblPersonPreview.Text = name ?? "(未解決の名義 ID)";
        }
        if (numCharacterAliasId.Value > 0)
        {
            var name = await _lookupCache.LookupCharacterAliasNameAsync((int)numCharacterAliasId.Value);
            lblCharacterPreview.Text = name ?? "(未解決のキャラ名義 ID)";
        }
        if (numVoicePersonAliasId.Value > 0)
        {
            var name = await _lookupCache.LookupPersonAliasNameAsync((int)numVoicePersonAliasId.Value);
            lblVoicePreview.Text = name ?? "(未解決の声優名義 ID)";
        }
        if (numCompanyAliasId.Value > 0)
        {
            var name = await _lookupCache.LookupCompanyAliasNameAsync((int)numCompanyAliasId.Value);
            lblCompanyPreview.Text = name ?? "(未解決の企業屋号 ID)";
        }
        if (numLogoId.Value > 0)
        {
            var name = await _lookupCache.LookupLogoNameAsync((int)numLogoId.Value);
            lblLogoPreview.Text = name ?? "(未解決のロゴ ID)";
        }
        if (numSongRecordingId.Value > 0)
        {
            var name = await _lookupCache.LookupSongRecordingNameAsync((int)numSongRecordingId.Value);
            lblSongPreview.Text = name ?? "(未解決の歌録音 ID)";
        }
    }

    // ────────────────────────────────────────────────────────────
    // 保存／削除
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// 保存ボタン処理：新規モードなら INSERT、編集モードなら UPDATE。
    /// 種別別に必須フィールドを検証してから DB を叩く。
    /// </summary>
    private async Task OnSaveAsync()
    {
        try
        {
            if (_entriesRepo is null) return;

            string kind = GetSelectedKind();
            var (ok, error) = ValidateForKind(kind);
            if (!ok)
            {
                MessageBox.Show(this, error, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var entry = BuildEntryFromForm(kind);
            if (_editing is null)
            {
                // 新規 INSERT
                if (_newBlockId is null) { MessageBox.Show(this, "追加先 block_id が未設定です。"); return; }
                entry.BlockId = _newBlockId.Value;
                int newId = await _entriesRepo.InsertAsync(entry);
                _editing = await _entriesRepo.GetByIdAsync(newId);
                if (_editing is not null)
                {
                    SetMode(EditMode.Edit);
                }
            }
            else
            {
                // 既存 UPDATE
                entry.EntryId = _editing.EntryId;
                entry.BlockId = _editing.BlockId;
                await _entriesRepo.UpdateAsync(entry);
                _editing = await _entriesRepo.GetByIdAsync(entry.EntryId);
            }

            await RefreshPreviewsAsync();
            EntrySaved?.Invoke(this, EventArgs.Empty);
        }
        catch (MySqlConnector.MySqlException mex) when (mex.Number == 1062)
        {
            MessageBox.Show(this,
                "同じ (block_id, is_broadcast_only, entry_seq) の組み合わせのエントリが既に存在します。\n" +
                "entry_seq を変えて再度試してください。",
                "重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>削除ボタン処理：物理削除（確認あり）。</summary>
    private async Task OnDeleteAsync()
    {
        try
        {
            if (_entriesRepo is null || _editing is null) return;
            if (MessageBox.Show(this,
                $"エントリ #{_editing.EntryId} ({_editing.EntryKind}) を削除します。",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            await _entriesRepo.DeleteAsync(_editing.EntryId);
            ClearAndDisable();
            EntryDeleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 種別別の必須フィールド検証。entry_kind と各参照列の整合性は
    /// DB トリガー <c>trg_credit_block_entries_*_consistency</c> でも担保されるが、
    /// クライアント側でも事前チェックして分かりやすいエラーメッセージを出す。
    /// </summary>
    private (bool ok, string error) ValidateForKind(string kind)
    {
        switch (kind)
        {
            case "PERSON":
                if (numPersonAliasId.Value <= 0) return (false, "PERSON エントリには person_alias_id が必須です。");
                break;
            case "CHARACTER_VOICE":
                if (numCharacterAliasId.Value <= 0 && string.IsNullOrWhiteSpace(txtRawCharacterText.Text))
                    return (false, "CHARACTER_VOICE エントリには キャラ名義 ID または 直接テキストのいずれかが必須です。");
                if (numVoicePersonAliasId.Value <= 0)
                    return (false, "CHARACTER_VOICE エントリには 声優の person_alias_id が必須です。");
                break;
            case "COMPANY":
                if (numCompanyAliasId.Value <= 0) return (false, "COMPANY エントリには company_alias_id が必須です。");
                break;
            case "LOGO":
                if (numLogoId.Value <= 0) return (false, "LOGO エントリには logo_id が必須です。");
                break;
            case "SONG":
                if (numSongRecordingId.Value <= 0) return (false, "SONG エントリには song_recording_id が必須です。");
                break;
            case "TEXT":
                if (string.IsNullOrWhiteSpace(txtRawText.Text))
                    return (false, "TEXT エントリには raw_text が必須です。");
                break;
        }
        return (true, "");
    }

    /// <summary>
    /// 入力フォームから <see cref="CreditBlockEntry"/> を組み立てる。
    /// 種別に応じて関係ない参照列は null になる（DB トリガーが許容）。
    /// </summary>
    private CreditBlockEntry BuildEntryFromForm(string kind)
    {
        var e = new CreditBlockEntry
        {
            EntryKind = kind,
            IsBroadcastOnly = chkIsBroadcastOnly.Checked,
            EntrySeq = (ushort)numEntrySeq.Value,
            Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text.Trim(),
            CreatedBy = Environment.UserName,
            UpdatedBy = Environment.UserName
        };
        switch (kind)
        {
            case "PERSON":
                e.PersonAliasId = (int)numPersonAliasId.Value;
                e.AffiliationCompanyAliasId = numAffiliationCompanyAliasId.Value > 0 ? (int)numAffiliationCompanyAliasId.Value : null;
                e.AffiliationText           = string.IsNullOrWhiteSpace(txtAffiliationText.Text) ? null : txtAffiliationText.Text.Trim();
                break;
            case "CHARACTER_VOICE":
                e.CharacterAliasId = numCharacterAliasId.Value > 0 ? (int)numCharacterAliasId.Value : null;
                e.RawCharacterText = string.IsNullOrWhiteSpace(txtRawCharacterText.Text) ? null : txtRawCharacterText.Text.Trim();
                e.PersonAliasId    = (int)numVoicePersonAliasId.Value;  // 声優は person_alias_id を共用
                break;
            case "COMPANY":
                e.CompanyAliasId = (int)numCompanyAliasId.Value;
                break;
            case "LOGO":
                e.LogoId = (int)numLogoId.Value;
                break;
            case "SONG":
                e.SongRecordingId = (int)numSongRecordingId.Value;
                break;
            case "TEXT":
                e.RawText = txtRawText.Text.Trim();
                break;
        }
        return e;
    }

    // ────────────────────────────────────────────────────────────
    // ピッカー結線（v1.2.0 工程 B-3b 追加）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// PERSON 種別の名義 ID 検索ピッカー。
    /// PersonAliasPickerDialog で全人物名義を検索し、選択された alias_id をフィールドにセット。
    /// scope_person_id は null（全名義から検索）で起動する。
    /// </summary>
    private void OnPickPersonAlias()
    {
        if (_personAliasesRepo is null) return;
        using var dlg = new Pickers.PersonAliasPickerDialog(_personAliasesRepo, scopePersonId: null);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numPersonAliasId.Value = dlg.SelectedId.Value;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>
    /// PERSON 種別の所属屋号 ID 検索ピッカー。
    /// scope_company_id は null（全屋号から検索）で起動する。
    /// </summary>
    private void OnPickAffiliationCompany()
    {
        if (_companyAliasesRepo is null) return;
        using var dlg = new Pickers.CompanyAliasPickerDialog(_companyAliasesRepo, scopeCompanyId: null);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numAffiliationCompanyAliasId.Value = dlg.SelectedId.Value;
        }
    }

    /// <summary>
    /// CHARACTER_VOICE 種別のキャラ名義 ID 検索ピッカー。
    /// 工程 B-3b で新設した CharacterAliasPickerDialog を使う。
    /// </summary>
    private void OnPickCharacterAlias()
    {
        if (_characterAliasesRepo is null) return;
        using var dlg = new Pickers.CharacterAliasPickerDialog(_characterAliasesRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numCharacterAliasId.Value = dlg.SelectedId.Value;
            // キャラ名義を選んだ場合は直接テキストはクリア（同じエントリで両方は持たない設計）
            txtRawCharacterText.Text = "";
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>
    /// CHARACTER_VOICE 種別の声優人物名義 ID 検索ピッカー。
    /// PERSON 用と同じ PersonAliasPickerDialog を共用。
    /// </summary>
    private void OnPickVoicePersonAlias()
    {
        if (_personAliasesRepo is null) return;
        using var dlg = new Pickers.PersonAliasPickerDialog(_personAliasesRepo, scopePersonId: null);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numVoicePersonAliasId.Value = dlg.SelectedId.Value;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>COMPANY 種別の屋号 ID 検索ピッカー。</summary>
    private void OnPickCompanyAlias()
    {
        if (_companyAliasesRepo is null) return;
        using var dlg = new Pickers.CompanyAliasPickerDialog(_companyAliasesRepo, scopeCompanyId: null);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numCompanyAliasId.Value = dlg.SelectedId.Value;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>
    /// LOGO 種別のロゴ ID 検索ピッカー。
    /// 工程 B-3b で新設した LogoPickerDialog を使う。屋号名も併せて表示できるよう
    /// CompanyAliasesRepository も渡す。
    /// </summary>
    private void OnPickLogo()
    {
        if (_logosRepo is null || _companyAliasesRepo is null) return;
        using var dlg = new Pickers.LogoPickerDialog(_logosRepo, _companyAliasesRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numLogoId.Value = dlg.SelectedId.Value;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>SONG 種別の歌録音 ID 検索ピッカー。既存 SongRecordingPickerDialog を使う。</summary>
    private void OnPickSongRecording()
    {
        if (_songRecRepo is null) return;
        using var dlg = new Pickers.SongRecordingPickerDialog(_songRecRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            numSongRecordingId.Value = dlg.SelectedId.Value;
            _ = RefreshPreviewsAsync();
        }
    }

    // ────────────────────────────────────────────────────────────
    // QuickAdd マスタ自動投入結線（v1.2.0 工程 B-3c 追加）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// PERSON 種別の「+ 新規...」ボタン処理：QuickAddPersonDialog で人物 1 名 + 名義 1 件を即時投入。
    /// 完了後、新 alias_id を numPersonAliasId にセット、LookupCache の対応キャッシュを破棄、
    /// プレビューを再描画する。
    /// </summary>
    private void OnNewPersonAlias()
    {
        if (_personsRepo is null || _lookupCache is null) return;
        using var dlg = new Dialogs.QuickAddPersonDialog(_personsRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedAliasId.HasValue)
        {
            int newId = dlg.SelectedAliasId.Value;
            _lookupCache.InvalidatePersonAlias(newId);
            numPersonAliasId.Value = newId;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>CHARACTER_VOICE の声優側「+ 新規...」処理。PERSON 用と同じ QuickAddPersonDialog を共用。</summary>
    private void OnNewVoicePersonAlias()
    {
        if (_personsRepo is null || _lookupCache is null) return;
        using var dlg = new Dialogs.QuickAddPersonDialog(_personsRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedAliasId.HasValue)
        {
            int newId = dlg.SelectedAliasId.Value;
            _lookupCache.InvalidatePersonAlias(newId);
            numVoicePersonAliasId.Value = newId;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>
    /// COMPANY 種別の「+ 新規...」ボタン処理：QuickAddCompanyAliasDialog で
    /// 「既存企業に屋号追加」または「企業ごと新規作成」のどちらかで投入。
    /// 完了後、新 alias_id を numCompanyAliasId にセット、LookupCache の対応キャッシュを破棄、
    /// プレビューを再描画する。
    /// </summary>
    private void OnNewCompanyAlias()
    {
        if (_companiesRepo is null || _companyAliasesRepo is null || _lookupCache is null) return;
        using var dlg = new Dialogs.QuickAddCompanyAliasDialog(_companiesRepo, _companyAliasesRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedAliasId.HasValue)
        {
            int newId = dlg.SelectedAliasId.Value;
            _lookupCache.InvalidateCompanyAlias(newId);
            numCompanyAliasId.Value = newId;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>
    /// LOGO 種別の「+ 新規...」ボタン処理：QuickAddLogoDialog で 1 行投入。
    /// 完了後、新 logo_id を numLogoId にセット、LookupCache の対応キャッシュを破棄、
    /// プレビューを再描画する。
    /// </summary>
    private void OnNewLogo()
    {
        if (_logosRepo is null || _companyAliasesRepo is null || _lookupCache is null) return;
        using var dlg = new Dialogs.QuickAddLogoDialog(_logosRepo, _companyAliasesRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedLogoId.HasValue)
        {
            int newId = dlg.SelectedLogoId.Value;
            _lookupCache.InvalidateLogo(newId);
            numLogoId.Value = newId;
            _ = RefreshPreviewsAsync();
        }
    }

    /// <summary>
    /// CHARACTER_VOICE 種別のキャラ名義「+ 新規...」ボタン処理（v1.2.0 工程 F 追加）。
    /// QuickAddCharacterAliasDialog で「既存キャラに名義追加」または「キャラごと新規作成」のどちらかで投入。
    /// 完了後、新 alias_id を numCharacterAliasId にセット、LookupCache の対応キャッシュを破棄、
    /// プレビューを再描画する。
    /// </summary>
    private void OnNewCharacterAlias()
    {
        if (_charactersRepo is null || _characterAliasesRepo is null
            || _characterKindsRepo is null || _lookupCache is null) return;
        using var dlg = new Dialogs.QuickAddCharacterAliasDialog(
            _charactersRepo, _characterAliasesRepo, _characterKindsRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedAliasId.HasValue)
        {
            int newId = dlg.SelectedAliasId.Value;
            _lookupCache.InvalidateCharacterAlias(newId);
            numCharacterAliasId.Value = newId;
            // 新規キャラ追加直後はキャラ名義 ID 入力に対応するよう、フリーテキスト欄はクリア
            txtRawCharacterText.Text = "";
            _ = RefreshPreviewsAsync();
        }
    }
}
