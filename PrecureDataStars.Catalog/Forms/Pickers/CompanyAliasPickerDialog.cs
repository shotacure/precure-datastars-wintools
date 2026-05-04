using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 企業屋号（<c>company_aliases</c>）の検索・選択ダイアログ（v1.2.0 工程 C 追加）。
/// <para>
/// オプション引数 <paramref name="scopeCompanyId"/> に値を渡した場合、その企業に紐づく
/// 屋号のみが対象となる（企業屋号タブの前任／後任屋号を選ぶ場面で使う）。
/// 指定しない場合（NULL）は全屋号を <see cref="CompanyAliasesRepository.SearchAsync"/> で
/// キーワード検索する（エピソード主題歌タブのレーベル指定ではこちらを使う）。
/// </para>
/// </summary>
public partial class CompanyAliasPickerDialog : Form
{
    private readonly CompanyAliasesRepository _repo;
    private readonly int? _scopeCompanyId;
    private readonly SearchDebouncer _debouncer;

    /// <summary>選択結果の alias_id。</summary>
    public int? SelectedId { get; private set; }

    public CompanyAliasPickerDialog(CompanyAliasesRepository repo, int? scopeCompanyId = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _scopeCompanyId = scopeCompanyId;
        InitializeComponent();
        _debouncer = new SearchDebouncer(200, async () => await ReloadAsync());

        if (_scopeCompanyId.HasValue)
        {
            lblScopeInfo.Text = $"※ company_id = {_scopeCompanyId.Value} の屋号のみを表示しています";
            lblScopeInfo.Visible = true;
        }

        txtKeyword.TextChanged += (_, __) => _debouncer.Trigger();
        lvResults.DoubleClick += (_, __) => OnDoubleClick();
        btnOk.Click += (_, __) => OnOkClick();
        FormClosed += (_, __) => _debouncer.Dispose();
        Load += async (_, __) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            string kw = txtKeyword.Text.Trim();
            System.Collections.Generic.IReadOnlyList<CompanyAlias> rows;
            if (_scopeCompanyId.HasValue)
            {
                var all = await _repo.GetByCompanyAsync(_scopeCompanyId.Value);
                rows = string.IsNullOrEmpty(kw)
                    ? all
                    : all.Where(a =>
                        (a.Name ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase)
                        || (a.NameKana ?? "").Contains(kw, StringComparison.OrdinalIgnoreCase))
                      .ToList();
            }
            else
            {
                rows = await _repo.SearchAsync(kw, 100);
            }

            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            foreach (var a in rows)
            {
                var item = new ListViewItem(new[]
                {
                    a.AliasId.ToString(),
                    a.Name ?? "",
                    a.NameKana ?? "",
                    a.ValidFrom?.ToString("yyyy-MM-dd") ?? "",
                    a.ValidTo?.ToString("yyyy-MM-dd") ?? ""
                })
                { Tag = a };
                lvResults.Items.Add(item);
            }
            lvResults.EndUpdate();
            lblHitCount.Text = $"{rows.Count} 件見つかりました（最大 100 件表示）";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "検索エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnDoubleClick()
    {
        if (lvResults.SelectedItems.Count == 0) return;
        OnOkClick();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnOkClick()
    {
        if (lvResults.SelectedItems.Count == 0)
        {
            DialogResult = DialogResult.None;
            MessageBox.Show(this, "行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (lvResults.SelectedItems[0].Tag is CompanyAlias a) SelectedId = a.AliasId;
    }
}
