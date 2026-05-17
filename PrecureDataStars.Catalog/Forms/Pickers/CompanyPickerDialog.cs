using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 企業（<c>companies</c>）の検索・選択ダイアログ。
/// <para>
/// キーワードを <see cref="CompaniesRepository.SearchAsync"/> で検索し、ListView に結果を表示する。
/// </para>
/// </summary>
public partial class CompanyPickerDialog : Form
{
    private readonly CompaniesRepository _repo;
    private readonly SearchDebouncer _debouncer;

    /// <summary>選択結果の企業 ID。</summary>
    public int? SelectedId { get; private set; }

    /// <summary>新しいインスタンスを生成する。</summary>
    public CompanyPickerDialog(CompaniesRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        InitializeComponent();
        _debouncer = new SearchDebouncer(200, async () => await ReloadAsync());

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
            var rows = await _repo.SearchAsync(txtKeyword.Text.Trim(), 100);
            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            foreach (var c in rows)
            {
                var item = new ListViewItem(new[]
                {
                    c.CompanyId.ToString(),
                    c.Name ?? "",
                    c.NameKana ?? "",
                    c.NameEn ?? ""
                })
                { Tag = c };
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
        if (lvResults.SelectedItems[0].Tag is Company c) SelectedId = c.CompanyId;
    }
}