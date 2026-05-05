using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 企業屋号の即時追加ダイアログ（v1.2.0 工程 B-3c 追加）。
/// <para>
/// 2 つのモードを切替で扱う:
/// <list type="bullet">
///   <item><description>
///     モード A：既存の企業に屋号だけ追加する。親企業を <see cref="CompanyPickerDialog"/> で選び、
///     <see cref="CompanyAliasesRepository.InsertAsync"/> を 1 回呼ぶだけ。
///   </description></item>
///   <item><description>
///     モード B：企業ごと新規作成する。<see cref="CompaniesRepository.QuickAddWithSingleAliasAsync"/>
///     で companies + company_aliases を 1 トランザクションで投入する。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// OK 完了後、新規 company_aliases.alias_id が <see cref="SelectedAliasId"/> にセットされる。
/// </para>
/// </summary>
public partial class QuickAddCompanyAliasDialog : Form
{
    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    /// <summary>モード A で選択された親企業 ID（null の場合は未選択）。</summary>
    private int? _pickedCompanyId;

    /// <summary>登録成功時の新規 company_aliases.alias_id。キャンセル時は null。</summary>
    public int? SelectedAliasId { get; private set; }

    public QuickAddCompanyAliasDialog(
        CompaniesRepository companiesRepo,
        CompanyAliasesRepository companyAliasesRepo)
    {
        _companiesRepo      = companiesRepo      ?? throw new ArgumentNullException(nameof(companiesRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        InitializeComponent();

        rbModeExisting.CheckedChanged   += (_, __) => UpdateMode();
        rbModeNewCompany.CheckedChanged += (_, __) => UpdateMode();
        btnPickParentCompany.Click      += async (_, __) => await OnPickParentCompanyAsync();
        btnOk.Click                     += async (_, __) => await OnOkAsync();

        UpdateMode();
    }

    /// <summary>モード切替に応じて 2 つの Panel の Visible を入れ替える。</summary>
    private void UpdateMode()
    {
        pnlExisting.Visible   = rbModeExisting.Checked;
        pnlNewCompany.Visible = rbModeNewCompany.Checked;
    }

    /// <summary>「選択...」ボタン押下：CompanyPickerDialog を開いて親企業を選ぶ。</summary>
    private async Task OnPickParentCompanyAsync()
    {
        try
        {
            using var picker = new CompanyPickerDialog(_companiesRepo);
            if (picker.ShowDialog(this) != DialogResult.OK || !picker.SelectedId.HasValue) return;
            _pickedCompanyId = picker.SelectedId.Value;
            // 表示用に企業名を引いて見せる
            var c = await _companiesRepo.GetByIdAsync(_pickedCompanyId.Value);
            lblParentCompanyValue.Text = c is null
                ? $"company_id={_pickedCompanyId} (未解決)"
                : $"#{c.CompanyId}  {c.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>登録ボタン処理：選択モードに応じて適切なリポジトリ呼び出しを実行。</summary>
    private async Task OnOkAsync()
    {
        try
        {
            if (rbModeExisting.Checked)
            {
                if (_pickedCompanyId is null)
                {
                    MessageBox.Show(this, "親企業を選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string aliasName = (txtExistingAliasName.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(aliasName))
                {
                    MessageBox.Show(this, "屋号名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtExistingAliasName.Focus();
                    return;
                }

                int aliasId = await _companyAliasesRepo.InsertAsync(new CompanyAlias
                {
                    CompanyId = _pickedCompanyId.Value,
                    Name = aliasName,
                    NameKana = string.IsNullOrWhiteSpace(txtExistingAliasKana.Text) ? null : txtExistingAliasKana.Text.Trim(),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                });
                SelectedAliasId = aliasId;
            }
            else
            {
                string companyName = (txtNewCompanyName.Text ?? "").Trim();
                string aliasName   = (txtNewAliasName.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(companyName))
                {
                    MessageBox.Show(this, "企業名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtNewCompanyName.Focus();
                    return;
                }
                if (string.IsNullOrWhiteSpace(aliasName))
                {
                    MessageBox.Show(this, "屋号名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtNewAliasName.Focus();
                    return;
                }

                int aliasId = await _companiesRepo.QuickAddWithSingleAliasAsync(
                    companyName,
                    string.IsNullOrWhiteSpace(txtNewCompanyKana.Text) ? null : txtNewCompanyKana.Text.Trim(),
                    string.IsNullOrWhiteSpace(txtNewCompanyEn.Text)   ? null : txtNewCompanyEn.Text.Trim(),
                    aliasName,
                    string.IsNullOrWhiteSpace(txtNewAliasKana.Text)   ? null : txtNewAliasKana.Text.Trim(),
                    Environment.UserName);
                SelectedAliasId = aliasId;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
