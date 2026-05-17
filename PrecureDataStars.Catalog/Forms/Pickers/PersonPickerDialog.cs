using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 人物（<c>persons</c>）の検索・選択ダイアログ。
/// <para>
/// キーワードを <see cref="PersonsRepository.SearchAsync"/> で検索し、ListView に結果を表示する。
/// 入力はデバウンスされ、最終キーストロークから 200ms 後に検索が走る。
/// </para>
/// <para>
/// 利用例:
/// <code>
/// using var dlg = new PersonPickerDialog(personsRepo);
/// if (dlg.ShowDialog(this) == DialogResult.OK) {
///     int personId = dlg.SelectedId!.Value;
/// }
/// </code>
/// </para>
/// </summary>
public partial class PersonPickerDialog : Form
{
    private readonly PersonsRepository _repo;
    private readonly SearchDebouncer _debouncer;

    /// <summary>
    /// 選択結果の人物 ID。<see cref="DialogResult.OK"/> でクローズされた場合に値を持つ。
    /// </summary>
    public int? SelectedId { get; private set; }

    /// <summary>
    /// 新しいインスタンスを生成する。検索リポジトリは DI で受け取る。
    /// </summary>
    public PersonPickerDialog(PersonsRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        InitializeComponent();

        _debouncer = new SearchDebouncer(200, async () => await ReloadAsync());

        txtKeyword.TextChanged += (_, __) => _debouncer.Trigger();
        lvResults.DoubleClick += (_, __) => OnDoubleClick();
        btnOk.Click += (_, __) => OnOkClick();
        FormClosed += (_, __) => _debouncer.Dispose();

        // 起動直後は空キーワードで全件相当を表示
        Load += async (_, __) => await ReloadAsync();
    }

    /// <summary>
    /// 現在のキーワードで検索結果を再取得し、ListView に反映する。
    /// </summary>
    private async Task ReloadAsync()
    {
        try
        {
            var rows = await _repo.SearchAsync(txtKeyword.Text.Trim(), 100);
            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            foreach (var p in rows)
            {
                var item = new ListViewItem(new[]
                {
                    p.PersonId.ToString(),
                    p.FullName ?? "",
                    p.FullNameKana ?? "",
                    p.NameEn ?? ""
                })
                {
                    Tag = p
                };
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

    /// <summary>
    /// ListView の行ダブルクリック時の処理。OK を押したのと同等の挙動とする。
    /// </summary>
    private void OnDoubleClick()
    {
        if (lvResults.SelectedItems.Count == 0) return;
        OnOkClick();
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// OK ボタンクリック時：選択行から PersonId を取り出して <see cref="SelectedId"/> に格納する。
    /// 行が選択されていない場合は <see cref="DialogResult"/> を <see cref="DialogResult.None"/> に
    /// 戻して閉じない。
    /// </summary>
    private void OnOkClick()
    {
        if (lvResults.SelectedItems.Count == 0)
        {
            DialogResult = DialogResult.None;
            MessageBox.Show(this, "行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (lvResults.SelectedItems[0].Tag is Person p)
        {
            SelectedId = p.PersonId;
        }
    }
}