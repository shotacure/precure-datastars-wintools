using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// ロゴの即時追加ダイアログ（v1.2.0 工程 B-3c 追加）。
/// <para>
/// 親屋号（company_aliases）を <see cref="CompanyAliasPickerDialog"/> で選び、
/// CI バージョンラベル（必須）+ 有効期間（任意）+ 説明（任意）を入力して
/// <see cref="LogosRepository.InsertAsync"/> で 1 行投入する。
/// </para>
/// <para>
/// 屋号がまだ存在しないケースは扱わない（一旦キャンセルして
/// <see cref="QuickAddCompanyAliasDialog"/> で先に作る運用）。
/// </para>
/// </summary>
public partial class QuickAddLogoDialog : Form
{
    private readonly LogosRepository _logosRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    private int? _pickedAliasId;

    /// <summary>登録成功時の新規 logos.logo_id。キャンセル時は null。</summary>
    public int? SelectedLogoId { get; private set; }

    public QuickAddLogoDialog(LogosRepository logosRepo, CompanyAliasesRepository companyAliasesRepo)
    {
        _logosRepo          = logosRepo          ?? throw new ArgumentNullException(nameof(logosRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        InitializeComponent();

        btnPickParentAlias.Click += async (_, __) => await OnPickParentAliasAsync();
        btnOk.Click              += async (_, __) => await OnOkAsync();
    }

    /// <summary>「選択...」ボタン押下：CompanyAliasPickerDialog を開いて屋号を選ぶ。</summary>
    private async Task OnPickParentAliasAsync()
    {
        try
        {
            using var picker = new CompanyAliasPickerDialog(_companyAliasesRepo, scopeCompanyId: null);
            if (picker.ShowDialog(this) != DialogResult.OK || !picker.SelectedId.HasValue) return;
            _pickedAliasId = picker.SelectedId.Value;
            var ca = await _companyAliasesRepo.GetByIdAsync(_pickedAliasId.Value);
            lblParentAliasValue.Text = ca is null
                ? $"alias_id={_pickedAliasId} (未解決)"
                : $"#{ca.AliasId}  {ca.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>登録ボタン処理：必須チェック後に LogosRepository.InsertAsync を呼ぶ。</summary>
    private async Task OnOkAsync()
    {
        try
        {
            if (_pickedAliasId is null)
            {
                MessageBox.Show(this, "屋号を選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string ciLabel = (txtCiVersionLabel.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ciLabel))
            {
                MessageBox.Show(this, "CI バージョンラベルは必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtCiVersionLabel.Focus();
                return;
            }

            var logo = new Logo
            {
                CompanyAliasId = _pickedAliasId.Value,
                CiVersionLabel = ciLabel,
                ValidFrom = chkValidFromEnabled.Checked ? dtpValidFrom.Value.Date : null,
                ValidTo   = chkValidToEnabled.Checked   ? dtpValidTo.Value.Date   : null,
                Description = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim(),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };

            int logoId = await _logosRepo.InsertAsync(logo);
            SelectedLogoId = logoId;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
