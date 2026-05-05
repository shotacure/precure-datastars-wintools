using PrecureDataStars.Catalog.Forms.Dialogs;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// ブロック（<c>credit_role_blocks</c>）プロパティ編集パネル（v1.2.0 工程 H 補修で新設）。
/// <para>
/// クレジット編集画面の右ペインに、エントリ編集パネル（<see cref="EntryEditorPanel"/>）と
/// 並ぶ形で配置される。ツリーで Block ノードが選択されたときに表示され、ブロックの
/// 各種プロパティ（<c>col_count</c> / <c>leading_company_alias_id</c> / <c>block_seq</c> / <c>notes</c>）を編集する。
/// </para>
/// <para>
/// 行数は v1.2.0 工程 H 補修でテーブルから撤去されたため、本パネルでも編集対象には含めない。
/// </para>
/// </summary>
public sealed partial class BlockEditorPanel : UserControl
{
    private CreditRoleBlocksRepository? _blocksRepo;
    private CompanyAliasesRepository? _companyAliasesRepo;
    private CompaniesRepository? _companiesRepo;
    private LookupCache? _lookupCache;

    private CreditRoleBlock? _currentBlock;
    private bool _isUpdatingFields;

    /// <summary>ブロックプロパティが保存された後に発火（呼び出し元のツリー再構築をトリガするため）。</summary>
    public event EventHandler? BlockSaved;

    public BlockEditorPanel()
    {
        InitializeComponent();

        // ボタン結線
        btnSave.Click += async (_, __) => await OnSaveAsync();
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
    /// 親フォームから依存性を流し込む。<see cref="EntryEditorPanel.Initialize"/> と同様の DI 経路。
    /// </summary>
    internal void Initialize(
        CreditRoleBlocksRepository blocksRepo,
        CompanyAliasesRepository companyAliasesRepo,
        CompaniesRepository companiesRepo,
        LookupCache lookupCache)
    {
        _blocksRepo = blocksRepo ?? throw new ArgumentNullException(nameof(blocksRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _lookupCache = lookupCache ?? throw new ArgumentNullException(nameof(lookupCache));
    }

    /// <summary>編集対象のブロックを読み込んでフィールドに反映する。</summary>
    public async Task LoadBlockAsync(CreditRoleBlock block)
    {
        _currentBlock = block ?? throw new ArgumentNullException(nameof(block));
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
        _currentBlock = null;
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

    private async Task OnSaveAsync()
    {
        if (_blocksRepo is null || _currentBlock is null) return;
        try
        {
            // 入力値を current ブロックに反映して UpdateAsync
            _currentBlock.ColCount = (byte)numColCount.Value;
            _currentBlock.BlockSeq = (byte)numBlockSeq.Value;
            _currentBlock.LeadingCompanyAliasId = chkLeadingCompanyNull.Checked
                ? (int?)null
                : (int)numLeadingCompanyAliasId.Value;
            _currentBlock.Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text;
            _currentBlock.UpdatedBy = Environment.UserName;

            await _blocksRepo.UpdateAsync(_currentBlock);
            BlockSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
