using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット編集フォームの右ペインで使う、エントリ編集用 UserControl。
/// <para>
/// エントリ種別ごとに編集パネルを切り替え、人物・キャラ・企業・ロゴ・テキストの
/// いずれかとして 1 件のエントリを編集する。種別ラジオは既存エントリ編集時には無効化され
/// （種別変更は別エントリとしての追加扱いになるため）、新規追加モードでのみ自由選択できる。
/// </para>
/// <para>
/// 本パネルは以下の動作を提供する:
///   ・TEXT エントリ（raw_text のみ）の追加・編集・保存・削除
///   ・PERSON / CHARACTER_VOICE / COMPANY / LOGO は ID 直接入力での保存可（ピッカー結線は B-3b）
///   ・「+ 新規...」によるマスタ自動投入は B-3c で結線
///   ・本放送限定フラグの設定
///   ・entry_seq / notes 編集
/// </para>
/// <para>
/// 主題歌は episode_theme_songs を真実の源泉とし、
/// クレジット画面では役職レベルでテンプレ展開時に楽曲を取得・表示する運用に切り替え。
/// </para>
/// </summary>
public sealed partial class EntryEditorPanel : UserControl
{
    private CreditBlockEntriesRepository? _entriesRepo;
    private LookupCache? _lookupCache;

    // ピッカーで使う参照系リポジトリ
    private PersonAliasesRepository? _personAliasesRepo;
    private CompanyAliasesRepository? _companyAliasesRepo;
    private CharacterAliasesRepository? _characterAliasesRepo;
    private LogosRepository? _logosRepo;

    // QuickAdd ダイアログでマスタ自動投入に使うリポジトリ
    private PersonsRepository? _personsRepo;
    private CompaniesRepository? _companiesRepo;

    // キャラ名義 QuickAdd 用のリポジトリ
    private CharactersRepository? _charactersRepo;
    private CharacterKindsRepository? _characterKindsRepo;

    /// <summary>編集中エントリ（null = 新規追加モード）。</summary>
    private CreditBlockEntry? _editing;

    /// <summary>編集中の DraftEntry 本体。null = 新規追加モード。</summary>
    private DraftEntry? _currentDraft;

    /// <summary>
    /// 親フォームから渡される CreditDraftSession 参照。
    /// 新規 DraftEntry の Temp ID を払い出すために必要。
    /// 親フォーム側で SetSession で更新する（クレジット切替時に新セッションに差し替えられる）。
    /// </summary>
    private CreditDraftSession? _session;

    /// <summary>新規追加モード時の追加先 block_id（既存仕様、現在は参考値）。</summary>
    private int? _newBlockId;

    /// <summary>
    /// 新規追加モード時の追加先 DraftBlock。
    /// 保存時にこのブロックの Entries リストに新 DraftEntry を Added で積み込む。
    /// </summary>
    private DraftBlock? _newParentBlock;

    /// <summary>新規追加モード時の初期 entry_seq（同 block_id × 同 is_broadcast_only グループの max+1）。</summary>
    private ushort _newSeq;

    /// <summary>
    /// 保存完了時に親フォーム（CreditEditorForm）へ通知してツリーを再構築させるイベント。
    /// 終盤で <see cref="EventHandler"/> から <see cref="Func{Task}"/> に変更。
    /// async void 風のイベントハンドラ continuation が UI メッセージポンプ待ちで保留されるのを避け、
    /// 購読側のツリー再構築を「保存」処理側で確実に await できるようにする。
    /// </summary>
    public Func<Task>? EntrySaved;

    /// <summary>削除完了時に親フォームへ通知するイベント（同じ理由で Func&lt;Task&gt; 型）。</summary>
    public Func<Task>? EntryDeleted;

    public EntryEditorPanel()
    {
        InitializeComponent();
        WireRadios();
        btnSave.Click   += async (_, __) => await OnSaveAsync();
        btnDelete.Click += async (_, __) => await OnDeleteAsync();

        // 6 個の検索ピッカーボタンを結線
        btnPersonAliasPick.Click          += (_, __) => OnPickPersonAlias();
        btnAffiliationCompanyPick.Click   += (_, __) => OnPickAffiliationCompany();
        btnCharacterAliasPick.Click       += (_, __) => OnPickCharacterAlias();
        btnVoicePersonAliasPick.Click     += (_, __) => OnPickVoicePersonAlias();
        btnCompanyAliasPick.Click         += (_, __) => OnPickCompanyAlias();
        btnLogoPick.Click                 += (_, __) => OnPickLogo();

        // 4 個の「+ 新規...」ボタン（QuickAdd ダイアログ）を結線
        btnPersonAliasNew.Click           += (_, __) => OnNewPersonAlias();
        btnVoicePersonAliasNew.Click      += (_, __) => OnNewVoicePersonAlias();
        btnCompanyAliasNew.Click          += (_, __) => OnNewCompanyAlias();
        btnLogoNew.Click                  += (_, __) => OnNewLogo();

        // CHARACTER_VOICE 種別の「+ 新規キャラ名義...」ボタンを結線
        btnCharacterAliasNew.Click        += (_, __) => OnNewCharacterAlias();
    }

    /// <summary>
    /// 親フォームから依存性を流し込む。コンストラクタで DI できないのは、
    /// このコントロールを Designer に置く都合（パラメータなしコンストラクタ必須）から。
    /// LookupCache が internal なので本メソッドの可視性も internal で揃えている。
    /// ピッカー用のマスタリポジトリ 5 本を追加引数で受け取る。
    /// QuickAdd ダイアログ用のリポジトリ 2 本を更に追加。
    /// </summary>
    internal void Initialize(
        CreditBlockEntriesRepository entriesRepo,
        LookupCache lookupCache,
        PersonAliasesRepository personAliasesRepo,
        CompanyAliasesRepository companyAliasesRepo,
        CharacterAliasesRepository characterAliasesRepo,
        LogosRepository logosRepo,
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
        _personsRepo          = personsRepo          ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo        = companiesRepo        ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo       = charactersRepo       ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterKindsRepo   = characterKindsRepo   ?? throw new ArgumentNullException(nameof(characterKindsRepo));
    }

    /// <summary>パネルを「編集対象なし」状態に戻す（保存・削除ボタン無効化、入力欄クリア）。</summary>
    public void ClearAndDisable()
    {
        _editing = null;
        _currentDraft = null;
        _newBlockId = null;
        _newParentBlock = null;
        _newSeq = 1;
        ClearAllFieldValues();
        SetMode(EditMode.None);
    }

    /// <summary>
    /// 既存エントリの編集モードに切り替える。種別ラジオは固定（変更不可）になる。
    /// で <see cref="DraftEntry"/> を受け取る形にシグネチャ変更。
    /// </summary>
    public async Task LoadForEditAsync(DraftEntry draft)
    {
        if (_lookupCache is null) throw new InvalidOperationException("Initialize() を先に呼んでください。");
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        _currentDraft = draft;
        _editing = draft.Entity;
        _newBlockId = null;
        _newParentBlock = null;
        var entry = draft.Entity;

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
    /// で <see cref="DraftBlock"/> を受け取る形にシグネチャ変更。
    /// </summary>
    public void LoadForNew(DraftBlock parentBlock, bool isBroadcastOnly, ushort newSeq)
    {
        if (parentBlock is null) throw new ArgumentNullException(nameof(parentBlock));
        _editing = null;
        _currentDraft = null;
        _newParentBlock = parentBlock;
        _newBlockId = parentBlock.RealId;  // 参考値（保存時には親 Draft の RealId が使われる）
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

        bool kindEditable = (mode == EditMode.New);
        rbKindPerson.Enabled = rbKindCharacterVoice.Enabled = rbKindCompany.Enabled =
            rbKindLogo.Enabled = rbKindText.Enabled = kindEditable;
        lblKindNotice.Text = (mode == EditMode.Edit)
            ? "（既存エントリの種別は変更できません。種別変更は削除→新規追加で行ってください）"
            : "";
        lblKindNotice.AutoSize = true;

        // 入力フィールドは編集中・新規中のみ有効
        SetFieldEditableRecursive(pnlKindHost, isActive);
        chkIsBroadcastOnly.Enabled = isActive;
        numEntrySeq.Enabled = isActive;
        txtNotes.Enabled = isActive;

        // ピッカーボタン 6 個（+ 所属屋号ピッカー）も編集中のみ有効
        btnPersonAliasPick.Enabled         = isActive;
        btnAffiliationCompanyPick.Enabled  = isActive;
        btnCharacterAliasPick.Enabled      = isActive;
        btnVoicePersonAliasPick.Enabled    = isActive;
        btnCompanyAliasPick.Enabled        = isActive;
        btnLogoPick.Enabled                = isActive;
        // 「+ 新規...」ボタン 4 個も編集中のみ有効
        btnPersonAliasNew.Enabled          = isActive;
        btnVoicePersonAliasNew.Enabled     = isActive;
        btnCompanyAliasNew.Enabled         = isActive;
        btnLogoNew.Enabled                 = isActive;
        // キャラ名義の「+ 新規...」も編集中のみ有効
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
        rbKindText.CheckedChanged           += (_, __) => { if (rbKindText.Checked)           ShowKindPanel("TEXT"); };
    }

    /// <summary>指定された entry_kind に対応するパネルだけを Visible=true にする。</summary>
    private void ShowKindPanel(string kind)
    {
        pnlPerson.Visible          = (kind == "PERSON");
        pnlCharacterVoice.Visible  = (kind == "CHARACTER_VOICE");
        pnlCompany.Visible         = (kind == "COMPANY");
        pnlLogo.Visible            = (kind == "LOGO");
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
            string kind = GetSelectedKind();
            var (ok, error) = ValidateForKind(kind);
            if (!ok)
            {
                MessageBox.Show(this, error, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var entry = BuildEntryFromForm(kind);

            if (_currentDraft is not null)
            {
                // ─── 既存エントリの編集（Modified） ───
                // フォーム入力値を Draft.Entity に上書きする。block_id / entry_id は維持する
                // （既存行を別ブロックに移動するのは DnD の仕事で、ここでは扱わない）。
                var d = _currentDraft.Entity;
                d.IsBroadcastOnly = entry.IsBroadcastOnly;
                d.EntrySeq        = entry.EntrySeq;
                d.EntryKind       = entry.EntryKind;
                d.PersonAliasId   = entry.PersonAliasId;
                d.CharacterAliasId = entry.CharacterAliasId;
                d.RawCharacterText = entry.RawCharacterText;
                d.CompanyAliasId  = entry.CompanyAliasId;
                d.LogoId          = entry.LogoId;
                d.RawText         = entry.RawText;
                d.AffiliationCompanyAliasId = entry.AffiliationCompanyAliasId;
                d.AffiliationText = entry.AffiliationText;
                d.Notes           = entry.Notes;
                d.UpdatedBy       = Environment.UserName;
                _currentDraft.MarkModified();
            }
            else if (_newParentBlock is not null)
            {
                // ─── 新規追加（Added） ───
                // 親 DraftBlock の Entries リストに新しい DraftEntry を追加。
                // BlockId は保存時に親の RealId で上書きされるが、参考値として親の RealId を入れる
                // （無ければ 0、保存時に CreditSaveService が上書き）。
                entry.BlockId = _newParentBlock.RealId ?? 0;
                entry.CreatedBy ??= Environment.UserName;
                entry.UpdatedBy = Environment.UserName;

                // セッションを参照して Temp ID を払い出す（_session は親フォームから SetSession で渡される）。
                if (_session is null)
                {
                    MessageBox.Show(this, "Draft セッションが未設定です（内部エラー）。", "内部エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var newDraft = new DraftEntry
                {
                    RealId = null,
                    TempId = _session.AllocateTempId(),
                    State = DraftState.Added,
                    Parent = _newParentBlock,
                    Entity = entry
                };
                _newParentBlock.Entries.Add(newDraft);

                // 編集モードに切り替えて、引き続き同じエントリを編集できるようにする
                _currentDraft = newDraft;
                _editing = newDraft.Entity;
                _newParentBlock = null;
                SetMode(EditMode.Edit);
            }
            else
            {
                MessageBox.Show(this, "保存対象がありません。", "確認");
                return;
            }

            await RefreshPreviewsAsync();
            // EntrySaved は Func<Task>? なので await して購読側のツリー再構築を確実に完了させる。
            if (EntrySaved is not null) await EntrySaved.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 親フォームから現在の Draft セッション参照を流し込む。
    /// クレジット切替や保存時の再ロードでセッションが新しくなるため、その都度更新する必要がある。
    /// </summary>
    internal void SetSession(CreditDraftSession session)
    {
        _session = session;
    }

    /// <summary>削除ボタン処理：Draft 上で削除マーク。</summary>
    /// <remarks>
    /// <see cref="EntryDeleted"/> は <see cref="Func{Task}"/> 型なので、購読側のツリー再構築（async）を
    /// 確実に await する。EventHandler 経由で fire-and-forget にすると continuation が UI メッセージポンプ待ちで
    /// 保留されるため、ここでは async + await の形にする。
    /// </remarks>
    private async Task OnDeleteAsync()
    {
        try
        {
            if (_currentDraft is null) return;
            if (_session is null)
            {
                MessageBox.Show(this, "Draft セッションが未設定です（内部エラー）。", "内部エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            string entryLabel = _currentDraft.RealId.HasValue
                ? $"#{_currentDraft.RealId.Value}"
                : "(未保存の新規行)";
            if (MessageBox.Show(this,
                $"エントリ {entryLabel} ({_currentDraft.Entity.EntryKind}) を削除します（保存ボタン押下時に確定）。",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            // ─── Draft 上で削除マーク ───
            // Added 状態（DB に未保存）→ 親 Block の Entries から取り除くだけ
            // Modified / Unchanged → 親 Block から取り外して Deleted バケットへ退避（保存時に DELETE）
            if (_currentDraft.State == DraftState.Added)
            {
                _currentDraft.Parent.Entries.Remove(_currentDraft);
            }
            else
            {
                _currentDraft.MarkDeleted();
                _currentDraft.Parent.Entries.Remove(_currentDraft);
                _session.DeletedEntries.Add(_currentDraft);
            }
            ClearAndDisable();
            // EntryDeleted は Func<Task>? なので await して購読側のツリー再構築を確実に完了させる。
            if (EntryDeleted is not null) await EntryDeleted.Invoke();
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
        // NumericUpDown はキー入力中に Value プロパティが
        // 確定しないため、保存前に明示的に値をコミットする。
        // ValidateChildren を呼ぶことで配下コントロールの Validating イベントが走り、
        // NumericUpDown のテキスト入力中の値が Value プロパティへコミットされる。
        ValidateChildren();

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
            case "TEXT":
                e.RawText = txtRawText.Text.Trim();
                break;
        }
        return e;
    }

    // ────────────────────────────────────────────────────────────
    // ピッカー結線
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
    /// CharacterAliasPickerDialog を使う。
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
    /// LogoPickerDialog を使う。屋号名も併せて表示できるよう
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


    // ────────────────────────────────────────────────────────────
    // QuickAdd マスタ自動投入結線
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
    /// CHARACTER_VOICE 種別のキャラ名義「+ 新規...」ボタン処理。
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