using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// ロゴの入力収集ダイアログ。
/// ステージD で「保存ボタンまで DB に書かない」原則に揃えるため、ダイアログ自身は DB に
/// 一切書き込まない。OK で閉じたとき、入力値が公開プロパティに保持されるので、
/// 呼び出し側が <c>CreditDraftSession.PendingLogos</c> に積む。
/// <see cref="ResultCompanyAliasId"/> は親屋号 ID で、ここでは正の実 ID（既存マスタの屋号）のみ
/// 受け取れる設計（Pending CompanyAlias を親に指定するケースは現状の UI 経路では発生しない）。
/// </summary>
public partial class QuickAddLogoDialog : Form
{
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    private int? _pickedAliasId;

    /// <summary>OK 確定時、選択した親屋号の company_aliases.alias_id（正の実 ID）。</summary>
    public int ResultCompanyAliasId { get; private set; }

    /// <summary>OK 確定時、入力された CI バージョンラベル（必須）。</summary>
    public string ResultCiVersionLabel { get; private set; } = "";

    /// <summary>OK 確定時、入力された有効開始日（チェック OFF なら null）。</summary>
    public DateTime? ResultValidFrom { get; private set; }

    /// <summary>OK 確定時、入力された有効終了日（チェック OFF なら null）。</summary>
    public DateTime? ResultValidTo { get; private set; }

    /// <summary>OK 確定時、入力された説明。空なら null。</summary>
    public string? ResultDescription { get; private set; }

    public QuickAddLogoDialog(CompanyAliasesRepository companyAliasesRepo)
    {
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        InitializeComponent();

        btnPickParentAlias.Click += async (_, __) => await OnPickParentAliasAsync();
        btnOk.Click              += (_, __) => OnOk();
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

    /// <summary>OK ボタン処理：必須チェック後、入力値を Result プロパティに格納してダイアログを閉じる。
    /// DB 投入は行わない。</summary>
    private void OnOk()
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

        ResultCompanyAliasId = _pickedAliasId.Value;
        ResultCiVersionLabel = ciLabel;
        ResultValidFrom = chkValidFromEnabled.Checked ? dtpValidFrom.Value.Date : (DateTime?)null;
        ResultValidTo   = chkValidToEnabled.Checked   ? dtpValidTo.Value.Date   : (DateTime?)null;
        ResultDescription = string.IsNullOrWhiteSpace(txtDescription.Text) ? null : txtDescription.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }
}
