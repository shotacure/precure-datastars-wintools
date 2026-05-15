using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Pickers;

/// <summary>
/// 商品社名マスタ（<c>product_companies</c>）の検索・選択ダイアログ。
/// <para>
/// 商品の発売元（label）／販売元（distributor）として紐付ける社名を ID 単位で
/// 選ばせるためのピッカー。入力ボックスはデバウンスされ、最終キーストロークから
/// 200ms 後に <see cref="ProductCompaniesRepository.SearchAsync"/> でキーワード
/// 部分一致検索が走る（和名・かな・英名のいずれかにマッチ）。
/// </para>
/// <para>
/// クレジット系の名義／屋号とは独立した、商品メタ情報専用の社名マスタを扱うので、
/// <see cref="PersonAliasPickerDialog"/> / <see cref="CompanyAliasPickerDialog"/> とは
/// ペアにせず、本ピッカー単独で動作する。
/// </para>
/// </summary>
public partial class ProductCompanyPickerDialog : Form
{
    private readonly ProductCompaniesRepository _repo;
    private readonly SearchDebouncer _debouncer;

    /// <summary>
    /// 選択結果の <c>product_company_id</c>。<see cref="DialogResult.OK"/> でクローズされた場合に値を持つ。
    /// </summary>
    public int? SelectedId { get; private set; }

    /// <summary>
    /// 新しいインスタンスを生成する。検索リポジトリは DI で受け取る。
    /// </summary>
    /// <param name="repo">商品社名マスタリポジトリ。</param>
    public ProductCompanyPickerDialog(ProductCompaniesRepository repo)
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

    /// <summary>
    /// 検索結果リストを再構築する。キーワードが空のときは全件を引いて表示する
    /// （件数が多くなる想定でも、社名マスタ自体は数十〜数百件規模なので許容可）。
    /// </summary>
    private async Task ReloadAsync()
    {
        try
        {
            string kw = txtKeyword.Text.Trim();
            System.Collections.Generic.IReadOnlyList<ProductCompany> rows = string.IsNullOrEmpty(kw)
                ? await _repo.GetAllAsync()
                : await _repo.SearchAsync(kw);

            lvResults.BeginUpdate();
            lvResults.Items.Clear();
            foreach (var c in rows)
            {
                var item = new ListViewItem(new[]
                {
                    c.ProductCompanyId.ToString(),
                    c.NameJa ?? "",
                    c.NameKana ?? "",
                    c.NameEn ?? ""
                })
                { Tag = c };
                lvResults.Items.Add(item);
            }
            lvResults.EndUpdate();
            lblHitCount.Text = $"{rows.Count} 件";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "検索エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>行ダブルクリックで OK 確定。</summary>
    private void OnDoubleClick()
    {
        if (lvResults.SelectedItems.Count == 0) return;
        OnOkClick();
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>OK ボタン押下：選択行から ID を確定する。</summary>
    private void OnOkClick()
    {
        if (lvResults.SelectedItems.Count == 0)
        {
            DialogResult = DialogResult.None;
            MessageBox.Show(this, "行を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (lvResults.SelectedItems[0].Tag is ProductCompany c) SelectedId = c.ProductCompanyId;
    }
}