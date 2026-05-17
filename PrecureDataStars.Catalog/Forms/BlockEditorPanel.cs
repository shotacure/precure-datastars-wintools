using PrecureDataStars.Catalog.Forms.Dialogs;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// ブロック（<c>credit_role_blocks</c>）プロパティ編集パネル（Draft 経由で編集する）。
/// <para>
/// クレジット編集画面の右ペインに、エントリ編集パネル（<see cref="EntryEditorPanel"/>）と
/// 並ぶ形で配置される。ツリーで Block ノードが選択されたときに表示され、ブロックの
/// 各種プロパティ（<c>col_count</c> / <c>leading_company_alias_id</c> / <c>block_seq</c> / <c>notes</c>）を編集する。
/// </para>
/// <para>
/// から：操作対象は <see cref="DraftBlock"/>（メモリ上の編集セッション）。
/// 「適用」ボタンでメモリ上の Draft.Entity に値を反映し <see cref="DraftBase.MarkModified"/> を呼ぶだけで、
/// DB への書き込みはクレジット編集画面下部の「💾 保存」ボタンで一括実行される。
/// </para>
/// </summary>
public sealed partial class BlockEditorPanel : UserControl
{
    private CompanyAliasesRepository? _companyAliasesRepo;
    private CompaniesRepository? _companiesRepo;
    private LookupCache? _lookupCache;

    private DraftBlock? _currentDraft;
    private bool _isUpdatingFields;

    /// <summary>
    /// ブロックプロパティが Draft に反映された後に発火（呼び出し元のツリー再構築をトリガするため）。
    /// 従来の <see cref="EventHandler"/> から <see cref="Func{Task}"/> に変更。
    /// 購読側のツリー再構築（async）が完了するまで「適用」処理が return しないようにすることで、
    /// 「適用ボタンを押しても画面更新が見えない」現象（async void イベントハンドラの継続が
    /// メッセージポンプ回るまで保留される問題）を回避する。
    /// </summary>
    public Func<Task>? BlockSaved;

    public BlockEditorPanel()
    {
        InitializeComponent();

        // ボタン結線
        btnSave.Click += async (_, __) => await OnApplyAsync();
        btnPickLeadingCompany.Click += (_, __) => OnPickLeadingCompany();
        btnNewLeadingCompany.Click += (_, __) => OnNewLeadingCompany();
        chkLeadingCompanyNull.CheckedChanged += (_, __) =>
        {
            if (_isUpdatingFields) return;
            numLeadingCompanyAliasId.Enabled = !chkLeadingCompanyNull.Checked;
            btnPickLeadingCompany.Enabled = !chkLeadingCompanyNull.Checked;
            // 「未指定」ON のとき値を 0 にしておく
            if (chkLeadingCompanyNull.Checked) numLeadingCompanyAliasId.Value = 0;
            // プレビューラベルの更新
            _ = RefreshLeadingCompanyPreviewAsync();
        };
        numLeadingCompanyAliasId.ValueChanged += (_, __) =>
        {
            if (_isUpdatingFields) return;
            _ = RefreshLeadingCompanyPreviewAsync();
        };
    }

    /// <summary>
    /// 親フォームから依存性を流し込む。
    /// Block の更新は Draft 経由で行うようになったため、CreditRoleBlocksRepository は不要。
    /// LookupCache と屋号系 Repository（ピッカー / QuickAdd 用）のみ残る。
    /// </summary>
    internal void Initialize(
        CompanyAliasesRepository companyAliasesRepo,
        CompaniesRepository companiesRepo,
        LookupCache lookupCache)
    {
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _lookupCache = lookupCache ?? throw new ArgumentNullException(nameof(lookupCache));
    }

    /// <summary>編集対象の Draft ブロックを読み込んでフィールドに反映する。</summary>
    public async Task LoadBlockAsync(DraftBlock draft)
    {
        _currentDraft = draft ?? throw new ArgumentNullException(nameof(draft));
        var block = draft.Entity;
        _isUpdatingFields = true;
        try
        {
            numColCount.Value = block.ColCount;
            numBlockSeq.Value = block.BlockSeq;
            txtNotes.Text = block.Notes ?? "";
            if (block.LeadingCompanyAliasId is int leadId)
            {
                numLeadingCompanyAliasId.Value = leadId;
                chkLeadingCompanyNull.Checked = false;
                numLeadingCompanyAliasId.Enabled = true;
                btnPickLeadingCompany.Enabled = true;
            }
            else
            {
                numLeadingCompanyAliasId.Value = 0;
                chkLeadingCompanyNull.Checked = true;
                numLeadingCompanyAliasId.Enabled = false;
                btnPickLeadingCompany.Enabled = false;
            }
            SetEnabled(true);
        }
        finally { _isUpdatingFields = false; }
        await RefreshLeadingCompanyPreviewAsync();
    }

    /// <summary>編集対象を解除してパネルを無効化する（ブロック以外のノードが選択されたとき）。</summary>
    public void ClearAndDisable()
    {
        _currentDraft = null;
        _isUpdatingFields = true;
        try
        {
            numColCount.Value = 1;
            numBlockSeq.Value = 1;
            numLeadingCompanyAliasId.Value = 0;
            chkLeadingCompanyNull.Checked = true;
            txtNotes.Text = "";
            lblLeadingCompanyPreview.Text = "(プレビュー: 屋号 ID を入力すると表示されます)";
            SetEnabled(false);
        }
        finally { _isUpdatingFields = false; }
    }

    private void SetEnabled(bool enabled)
    {
        numColCount.Enabled = enabled;
        numBlockSeq.Enabled = enabled;
        numLeadingCompanyAliasId.Enabled = enabled && !chkLeadingCompanyNull.Checked;
        chkLeadingCompanyNull.Enabled = enabled;
        btnPickLeadingCompany.Enabled = enabled && !chkLeadingCompanyNull.Checked;
        btnNewLeadingCompany.Enabled = enabled;
        txtNotes.Enabled = enabled;
        btnSave.Enabled = enabled;
    }

    private async Task RefreshLeadingCompanyPreviewAsync()
    {
        if (_lookupCache is null) return;
        if (chkLeadingCompanyNull.Checked || numLeadingCompanyAliasId.Value <= 0)
        {
            lblLeadingCompanyPreview.Text = "(屋号未指定)";
            return;
        }
        var name = await _lookupCache.LookupCompanyAliasNameAsync((int)numLeadingCompanyAliasId.Value);
        lblLeadingCompanyPreview.Text = name ?? "(未解決の屋号 ID)";
    }

    /// <summary>
    /// 「適用」ボタン押下時：UI フィールドの値を Draft.Entity に書き戻し、Draft を Modified にマーク。
    /// DB 書き込みはクレジット編集画面の「💾 保存」ボタンで一括実行される。
    /// </summary>
    /// <remarks>
    /// <see cref="BlockSaved"/> は <see cref="Func{Task}"/> 型なので、購読側のツリー再構築（async）を
    /// 確実に await して完了させる。EventHandler 経由で fire-and-forget にすると、async void 風の
    /// continuation が UI メッセージポンプ待ちで保留されて画面に反映されない問題が起きるため、
    /// このシグネチャで Draft 経由の編集に対応する。
    /// </remarks>
    private async Task OnApplyAsync()
    {
        if (_currentDraft is null) return;
        try
        {
            // NumericUpDown はキー入力中の表示値が Value プロパティに反映されない（フォーカスが離れて
            // 初めて確定する）仕様のため、適用前に明示的に値を確定させる。
            // ValidateChildren を呼ぶことで配下コントロールの Validating イベントが走り、
            // NumericUpDown 等のテキスト入力中の値が Value プロパティへコミットされる。
            ValidateChildren();

            // 入力値を Draft.Entity に反映する。Draft の Modified マークは Added 状態のときには
            // 何もしない（既に新規追加扱いなので、保存時に INSERT で全部書かれる）。
            _currentDraft.Entity.ColCount = (byte)numColCount.Value;
            _currentDraft.Entity.BlockSeq = (byte)numBlockSeq.Value;
            _currentDraft.Entity.LeadingCompanyAliasId = chkLeadingCompanyNull.Checked
                ? (int?)null
                : (int)numLeadingCompanyAliasId.Value;
            _currentDraft.Entity.Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text;
            _currentDraft.Entity.UpdatedBy = Environment.UserName;
            _currentDraft.MarkModified();

            // 購読側のツリー再構築を await して完了させる。
            if (BlockSaved is not null) await BlockSaved.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "適用エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnPickLeadingCompany()
    {
        if (_companyAliasesRepo is null) return;
        using var dlg = new CompanyAliasPickerDialog(_companyAliasesRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
        {
            chkLeadingCompanyNull.Checked = false;
            numLeadingCompanyAliasId.Enabled = true;
            btnPickLeadingCompany.Enabled = true;
            numLeadingCompanyAliasId.Value = dlg.SelectedId.Value;
            _ = RefreshLeadingCompanyPreviewAsync();
        }
    }

    private void OnNewLeadingCompany()
    {
        if (_companyAliasesRepo is null || _companiesRepo is null || _lookupCache is null) return;
        using var dlg = new QuickAddCompanyAliasDialog(_companiesRepo, _companyAliasesRepo);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedAliasId.HasValue)
        {
            // QuickAdd 後に LookupCache を破棄して新 ID の名前解決を確実にする。
            _lookupCache.InvalidateCompanyAlias(dlg.SelectedAliasId.Value);
            chkLeadingCompanyNull.Checked = false;
            numLeadingCompanyAliasId.Enabled = true;
            btnPickLeadingCompany.Enabled = true;
            numLeadingCompanyAliasId.Value = dlg.SelectedAliasId.Value;
            _ = RefreshLeadingCompanyPreviewAsync();
        }
    }
}