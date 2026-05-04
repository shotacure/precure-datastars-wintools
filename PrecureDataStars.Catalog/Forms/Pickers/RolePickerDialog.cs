using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 役職コードを選ぶピッカーダイアログ（v1.2.0 工程 B-2 追加）。
/// <para>
/// 全役職を <see cref="RolesRepository.GetAllAsync"/> で取得しメモリ保持しておき、
/// 検索ボックスへの入力 200ms デバウンス後に role_code または name_ja の部分一致で
/// ListView を絞り込む。OK で <see cref="SelectedRole"/> プロパティに選択中の
/// <see cref="Role"/> を返す。
/// </para>
/// </summary>
public partial class RolePickerDialog : Form
{
    private readonly RolesRepository _rolesRepo;
    private readonly SearchDebouncer _debouncer;

    /// <summary>初回ロードでメモリに保持する全役職（フィルタ用）。</summary>
    private List<Role> _all = new();

    /// <summary>OK で選ばれた役職（キャンセル時は null）。</summary>
    public Role? SelectedRole { get; private set; }

    public RolePickerDialog(RolesRepository rolesRepo)
    {
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        InitializeComponent();

        // SearchDebouncer のコンストラクタ第 2 引数にコールバックを渡し、
        // テキスト変更時は引数なしの Trigger() でタイマーをリセットする
        // （v1.2.0 工程 C で導入された API シグネチャに合わせる）。
        _debouncer = new SearchDebouncer(200, ApplyFilter);
        txtSearch.TextChanged += (_, __) => _debouncer.Trigger();

        listResults.DoubleClick += (_, __) =>
        {
            if (listResults.SelectedItems.Count > 0)
            {
                CommitSelection();
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        btnOk.Click += (_, __) =>
        {
            // OK 押下時の選択コミット（CancelButton 経由のキャンセルは Result が null のまま）
            if (listResults.SelectedItems.Count > 0)
            {
                CommitSelection();
            }
            else
            {
                DialogResult = DialogResult.None;
                MessageBox.Show(this, "役職を選択してください。");
            }
        };

        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>初回ロード：全役職を取得してメモリ保持し、ListView に流し込む。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            _all = (await _rolesRepo.GetAllAsync()).ToList();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 検索文字列で _all をフィルタして ListView を再構築する。
    /// 部分一致は role_code / name_ja の両方を見る（大文字小文字無視）。
    /// </summary>
    private void ApplyFilter()
    {
        string keyword = (txtSearch.Text ?? "").Trim();
        IEnumerable<Role> filtered = _all;
        if (keyword.Length > 0)
        {
            string kw = keyword.ToLowerInvariant();
            filtered = _all.Where(r =>
                (r.RoleCode?.ToLowerInvariant().Contains(kw) ?? false) ||
                (r.NameJa?.ToLowerInvariant().Contains(kw) ?? false));
        }

        listResults.BeginUpdate();
        try
        {
            listResults.Items.Clear();
            foreach (var r in filtered.OrderBy(x => x.RoleCode))
            {
                var item = new ListViewItem(r.RoleCode) { Tag = r };
                item.SubItems.Add(r.NameJa ?? "");
                item.SubItems.Add(r.RoleFormatKind ?? "");
                listResults.Items.Add(item);
            }
        }
        finally
        {
            listResults.EndUpdate();
        }
        lblStatus.Text = $"{listResults.Items.Count} 件 / 全 {_all.Count} 件";
    }

    /// <summary>選択中の ListViewItem を <see cref="SelectedRole"/> に確定する。</summary>
    private void CommitSelection()
    {
        if (listResults.SelectedItems.Count > 0 &&
            listResults.SelectedItems[0].Tag is Role role)
        {
            SelectedRole = role;
        }
    }
}
