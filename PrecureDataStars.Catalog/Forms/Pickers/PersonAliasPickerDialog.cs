using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 人物名義（<c>person_aliases</c>）の検索・選択ダイアログ（v1.2.0 工程 C 追加）。
/// <para>
/// オプション引数 <paramref name="scopePersonId"/> に値を渡した場合、その人物に紐づく
/// 名義のみが対象となる（人物名義タブの前任／後任名義を選ぶ場面で使う）。
/// 指定しない場合（NULL）は全名義を <see cref="PersonAliasesRepository.SearchAsync"/> で
/// キーワード検索する。
/// </para>
/// </summary>
public partial class PersonAliasPickerDialog : Form
{
    private readonly PersonAliasesRepository _repo;
    private readonly int? _scopePersonId;
    private readonly SearchDebouncer _debouncer;

    /// <summary>選択結果の alias_id。</summary>
    public int? SelectedId { get; private set; }

    /// <summary>
    /// 新しいインスタンスを生成する。
    /// </summary>
    /// <param name="repo">名義リポジトリ。</param>
    /// <param name="scopePersonId">指定すると当該人物配下のみで絞り込んで検索する。null なら全件対象。</param>
    public PersonAliasPickerDialog(PersonAliasesRepository repo, int? scopePersonId = null)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _scopePersonId = scopePersonId;
        InitializeComponent();
        _debouncer = new SearchDebouncer(200, async () => await ReloadAsync());

        if (_scopePersonId.HasValue)
        {
            lblScopeInfo.Text = $"※ person_id = {_scopePersonId.Value} の名義のみを表示しています";
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
            // スコープ指定時は GetByPersonAsync で対象人物の名義を取得し、キーワードでクライアント側絞り込み。
            // 件数が多くないため許容できるパフォーマンス。
            // 非スコープ時は SearchAsync の全件横断検索を使う。
            string kw = txtKeyword.Text.Trim();
            System.Collections.Generic.IReadOnlyList<PersonAlias> rows;
            if (_scopePersonId.HasValue)
            {
                var all = await _repo.GetByPersonAsync(_scopePersonId.Value);
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
        if (lvResults.SelectedItems[0].Tag is PersonAlias a) SelectedId = a.AliasId;
    }
}
