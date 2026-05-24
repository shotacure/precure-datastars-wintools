using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 企業屋号の入力収集ダイアログ。
/// ステージD で「保存ボタンまで DB に書かない」原則に揃えるため、ダイアログ自身は DB に
/// 一切書き込まない。OK で閉じたとき、入力値と選択モードが公開プロパティに保持されるので、
/// 呼び出し側が <c>CreditDraftSession.PendingCompanyAliases</c> に積む。
/// 2 つのモードを切替で扱う:
/// <list type="bullet">
///   <item><description>
///     モード A：既存の企業に屋号だけ追加する。親企業を <see cref="CompanyPickerDialog"/> で選び、
///     <see cref="ResultAttachToExistingCompanyId"/> にその company_id を入れる。
///   </description></item>
///   <item><description>
///     モード B：企業ごと新規作成する。<see cref="ResultCompanyName"/> を埋めて返す。
///   </description></item>
/// </list>
/// </summary>
public partial class QuickAddCompanyAliasDialog : Form
{
    private readonly CompaniesRepository _companiesRepo;

    /// <summary>モード A で選択された親企業 ID（null の場合は未選択）。</summary>
    private int? _pickedCompanyId;

    /// <summary>OK 確定時、モード A（既存企業に alias 追加）なら親 company_id。
    /// モード B（新規企業）なら null。</summary>
    public int? ResultAttachToExistingCompanyId { get; private set; }

    /// <summary>OK 確定時、入力された alias 名（必須、屋号）。</summary>
    public string ResultAliasName { get; private set; } = "";

    /// <summary>OK 確定時、入力された alias かな。空なら null。</summary>
    public string? ResultAliasKana { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規企業の companies.name。
    /// モード A（attach）の場合は null。</summary>
    public string? ResultCompanyName { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規企業の companies.name_kana。</summary>
    public string? ResultCompanyNameKana { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規企業の companies.name_en。</summary>
    public string? ResultCompanyNameEn { get; private set; }

    public QuickAddCompanyAliasDialog(CompaniesRepository companiesRepo)
    {
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        InitializeComponent();

        rbModeExisting.CheckedChanged   += (_, __) => UpdateMode();
        rbModeNewCompany.CheckedChanged += (_, __) => UpdateMode();
        btnPickParentCompany.Click      += async (_, __) => await OnPickParentCompanyAsync();
        btnOk.Click                     += (_, __) => OnOk();

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

    /// <summary>OK ボタン処理：選択モードに応じて入力値を Result プロパティに格納してダイアログを閉じる。
    /// DB 投入は行わない。</summary>
    private void OnOk()
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

            ResultAttachToExistingCompanyId = _pickedCompanyId.Value;
            ResultAliasName = aliasName;
            ResultAliasKana = string.IsNullOrWhiteSpace(txtExistingAliasKana.Text) ? null : txtExistingAliasKana.Text.Trim();
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

            ResultAttachToExistingCompanyId = null;
            ResultAliasName = aliasName;
            ResultAliasKana = string.IsNullOrWhiteSpace(txtNewAliasKana.Text) ? null : txtNewAliasKana.Text.Trim();
            ResultCompanyName     = companyName;
            ResultCompanyNameKana = string.IsNullOrWhiteSpace(txtNewCompanyKana.Text) ? null : txtNewCompanyKana.Text.Trim();
            ResultCompanyNameEn   = string.IsNullOrWhiteSpace(txtNewCompanyEn.Text)   ? null : txtNewCompanyEn.Text.Trim();
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
