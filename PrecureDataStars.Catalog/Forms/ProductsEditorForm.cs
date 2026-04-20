using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品管理エディタ。
/// <para>
/// 左: 商品一覧グリッド、右: 商品詳細フォーム。
/// 下段: 選択中商品に所属するディスクの一覧を表示する（参照用。ディスク本体の編集は DiscsEditor）。
/// </para>
/// <para>
/// v1.1.1 より商品はシリーズ所属を持たない（シリーズは Disc 側に属する）。本フォームの詳細欄から
/// シリーズコンボは撤去され、所属ディスク一覧側で個別にシリーズを確認・編集する運用になる。
/// </para>
/// </summary>
public partial class ProductsEditorForm : Form
{
    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly ProductKindsRepository _productKindsRepo;
    // v1.1.1: 商品側でシリーズを選ばなくなったため SeriesRepository の注入は不要になった。
    //         ディスクのシリーズ編集は DiscsEditorForm に担当を移す。

    // 表示中の商品一覧（並び替え対応のため List で保持）
    private System.Collections.Generic.List<Product> _products = new();

    public ProductsEditorForm(
        ProductsRepository productsRepo,
        DiscsRepository discsRepo,
        ProductKindsRepository productKindsRepo)
    {
        _productsRepo = productsRepo;
        _discsRepo = discsRepo;
        _productKindsRepo = productKindsRepo;

        InitializeComponent();
        Load += async (_, __) => await InitAsync();

        // 商品一覧の選択変更 → 詳細欄にバインド
        gridProducts.SelectionChanged += async (_, __) => await OnSelectionChangedAsync();

        btnNew.Click += (_, __) => ClearForm();
        btnSave.Click += async (_, __) => await SaveAsync();
        btnDelete.Click += async (_, __) => await DeleteAsync();
        btnReload.Click += async (_, __) => await ReloadAsync();
    }

    /// <summary>初期ロード: マスタ＋商品一覧を取得してバインドする。</summary>
    private async Task InitAsync()
    {
        try
        {
            // 商品種別コンボ
            var kinds = await _productKindsRepo.GetAllAsync();
            cboKind.DisplayMember = nameof(ProductKind.NameJa);
            cboKind.ValueMember = nameof(ProductKind.KindCode);
            cboKind.DataSource = kinds.ToList();

            // v1.1.1: シリーズコンボはディスク側に移譲されたためここでは初期化しない

            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>商品一覧を再取得してグリッドにバインドする。</summary>
    private async Task ReloadAsync()
    {
        _products = (await _productsRepo.GetAllAsync()).ToList();
        gridProducts.DataSource = _products;
        // 見やすさのため、不要な監査列は列幅を狭く
        foreach (DataGridViewColumn c in gridProducts.Columns)
        {
            if (c.Name is nameof(Product.CreatedAt) or nameof(Product.UpdatedAt)
                or nameof(Product.CreatedBy) or nameof(Product.UpdatedBy)
                or nameof(Product.Notes))
                c.Visible = false;
        }
    }

    /// <summary>選択変更ハンドラ：詳細バインド + 所属ディスク一覧の取得。</summary>
    private async Task OnSelectionChangedAsync()
    {
        if (gridProducts.CurrentRow?.DataBoundItem is not Product p)
        {
            ClearForm();
            gridDiscs.DataSource = null;
            return;
        }
        BindToForm(p);
        try
        {
            var discs = await _discsRepo.GetByProductCatalogNoAsync(p.ProductCatalogNo);
            gridDiscs.DataSource = discs.ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>商品オブジェクトをフォームにバインドする。</summary>
    private void BindToForm(Product p)
    {
        txtProductCatalogNo.Text = p.ProductCatalogNo;
        // 既存商品は代表品番を後から変更できないようにしておく（紐付く discs の整合性維持のため）
        txtProductCatalogNo.ReadOnly = !string.IsNullOrEmpty(p.ProductCatalogNo);
        txtTitle.Text = p.Title;
        txtTitleShort.Text = p.TitleShort ?? "";
        txtTitleEn.Text = p.TitleEn ?? "";
        cboKind.SelectedValue = p.ProductKindCode;
        // v1.1.1: シリーズは所属ディスク一覧側で確認・編集する
        dtRelease.Value = p.ReleaseDate == default ? DateTime.Today : p.ReleaseDate;
        numPriceEx.Value = p.PriceExTax ?? 0;
        numPriceInc.Value = p.PriceIncTax ?? 0;
        numDiscCount.Value = p.DiscCount <= 0 ? 1 : p.DiscCount;
        txtManufacturer.Text = p.Manufacturer ?? "";
        txtDistributor.Text = p.Distributor ?? "";
        txtLabel.Text = p.Label ?? "";
        txtAsin.Text = p.AmazonAsin ?? "";
        txtApple.Text = p.AppleAlbumId ?? "";
        txtSpotify.Text = p.SpotifyAlbumId ?? "";
        txtNotes.Text = p.Notes ?? "";
    }

    /// <summary>フォームをクリアして新規入力モードにする。</summary>
    private void ClearForm()
    {
        txtProductCatalogNo.Text = "";
        txtProductCatalogNo.ReadOnly = false; // 新規は代表品番を入力してもらう
        txtTitle.Text = "";
        txtTitleShort.Text = "";
        txtTitleEn.Text = "";
        if (cboKind.Items.Count > 0) cboKind.SelectedIndex = 0;
        dtRelease.Value = DateTime.Today;
        numPriceEx.Value = 0;
        numPriceInc.Value = 0;
        numDiscCount.Value = 1;
        txtManufacturer.Text = "";
        txtDistributor.Text = "";
        txtLabel.Text = "";
        txtAsin.Text = "";
        txtApple.Text = "";
        txtSpotify.Text = "";
        txtNotes.Text = "";
        gridDiscs.DataSource = null;
    }

    /// <summary>フォーム内容を Product に変換する。</summary>
    private Product BuildFromForm()
    {
        // v1.1.1: SeriesId は Product プロパティから撤去済みのためここでも設定しない
        return new Product
        {
            ProductCatalogNo = txtProductCatalogNo.Text.Trim(),
            Title = txtTitle.Text.Trim(),
            TitleShort = NullIfEmpty(txtTitleShort.Text),
            TitleEn = NullIfEmpty(txtTitleEn.Text),
            ProductKindCode = cboKind.SelectedValue?.ToString() ?? "OTHER",
            ReleaseDate = dtRelease.Value.Date,
            PriceExTax = numPriceEx.Value == 0 ? null : (int)numPriceEx.Value,
            PriceIncTax = numPriceInc.Value == 0 ? null : (int)numPriceInc.Value,
            DiscCount = (byte)Math.Max(1, (int)numDiscCount.Value),
            Manufacturer = NullIfEmpty(txtManufacturer.Text),
            Distributor = NullIfEmpty(txtDistributor.Text),
            Label = NullIfEmpty(txtLabel.Text),
            AmazonAsin = NullIfEmpty(txtAsin.Text),
            AppleAlbumId = NullIfEmpty(txtApple.Text),
            SpotifyAlbumId = NullIfEmpty(txtSpotify.Text),
            Notes = NullIfEmpty(txtNotes.Text),
            CreatedBy = Environment.UserName,
            UpdatedBy = Environment.UserName
        };
    }

    /// <summary>
    /// 保存：代表品番が新規（DB に存在しない）なら INSERT、既存なら UPDATE。
    /// 代表品番の入力欄は既存選択時 ReadOnly にしているため、既存行ではキーが変わらない前提。
    /// </summary>
    private async Task SaveAsync()
    {
        try
        {
            var p = BuildFromForm();
            if (string.IsNullOrWhiteSpace(p.ProductCatalogNo)) { MessageBox.Show(this, "代表品番は必須です。"); return; }
            if (string.IsNullOrWhiteSpace(p.Title)) { MessageBox.Show(this, "タイトルは必須です。"); return; }

            // 代表品番で既存を検索して、あれば UPDATE、なければ INSERT
            var existing = await _productsRepo.GetByCatalogNoAsync(p.ProductCatalogNo);
            if (existing is null)
            {
                await _productsRepo.InsertAsync(p);
                MessageBox.Show(this, $"新規商品 [{p.ProductCatalogNo}] を登録しました。");
            }
            else
            {
                await _productsRepo.UpdateAsync(p);
                MessageBox.Show(this, $"商品 [{p.ProductCatalogNo}] を更新しました。");
            }
            await ReloadAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>削除：選択商品を論理削除する。</summary>
    private async Task DeleteAsync()
    {
        if (gridProducts.CurrentRow?.DataBoundItem is not Product p) return;
        if (MessageBox.Show(this, $"商品 [{p.Title}] を削除しますか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        try
        {
            await _productsRepo.SoftDeleteAsync(p.ProductCatalogNo, Environment.UserName);
            await ReloadAsync();
            ClearForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== ユーティリティ =====

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
