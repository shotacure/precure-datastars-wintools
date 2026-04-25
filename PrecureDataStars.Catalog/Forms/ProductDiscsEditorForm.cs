#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品・ディスク統合管理フォーム（v1.1.3 新設）。
/// <para>
/// 旧 <c>ProductsEditorForm</c> と <c>DiscsEditorForm</c> のうち、商品＋ディスクの編集機能を
/// 1 画面に統合したもの。トラック編集は <see cref="TracksEditorForm"/> に分離されている。
/// </para>
/// <para>
/// 画面構成:
/// <list type="bullet">
///   <item>左: 商品一覧（発売日昇順・同日は品番昇順、翻訳値のみ表示）</item>
///   <item>右上: 商品詳細（代表品番・発売日・価格・種別・ディスク枚数・流通情報など）</item>
///   <item>右下: 所属ディスク一覧 + 選択ディスクの詳細編集</item>
/// </list>
/// 税込価格の横にある「自動計算」ボタンで、発売日と税抜価格から税込価格を切り捨てで算出する。
/// </para>
/// </summary>
public partial class ProductDiscsEditorForm : Form
{
    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly SeriesRepository _seriesRepo;

    // 表示中の商品一覧（検索フィルタ適用後）
    private List<Product> _products = new();
    // 選択中商品の所属ディスク
    private List<Disc> _discs = new();
    // マスタキャッシュ
    private List<ProductKind> _productKinds = new();

    public ProductDiscsEditorForm(
        ProductsRepository productsRepo,
        DiscsRepository discsRepo,
        ProductKindsRepository productKindsRepo,
        DiscKindsRepository discKindsRepo,
        SeriesRepository seriesRepo)
    {
        _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _productKindsRepo = productKindsRepo ?? throw new ArgumentNullException(nameof(productKindsRepo));
        _discKindsRepo = discKindsRepo ?? throw new ArgumentNullException(nameof(discKindsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();
        SetupGridColumns();

        Load += async (_, __) => await InitAsync();

        gridProducts.SelectionChanged += async (_, __) => await OnProductSelectedAsync();
        gridDiscs.SelectionChanged += (_, __) => OnDiscSelected();

        btnSearch.Click += (_, __) => ApplyFilter();
        btnReload.Click += async (_, __) => await ReloadProductsAsync();
        txtSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFilter(); } };

        btnProductNew.Click += (_, __) => ClearProductForm();
        btnProductSave.Click += async (_, __) => await SaveProductAsync();
        btnProductDelete.Click += async (_, __) => await DeleteProductAsync();
        btnAutoTax.Click += (_, __) => AutoCalcTaxInclusive();

        btnDiscNew.Click += (_, __) => ClearDiscFormForNew();
        btnDiscSave.Click += async (_, __) => await SaveDiscAsync();
        btnDiscDelete.Click += async (_, __) => await DeleteDiscAsync();
    }

    // =========================================================================
    // グリッド列定義（翻訳値だけを表示し、コード列は出さない）
    // =========================================================================

    private void SetupGridColumns()
    {
        // ── 商品一覧 ──
        gridProducts.Columns.Clear();
        gridProducts.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "ReleaseDate", HeaderText = "発売日",
                DataPropertyName = nameof(Product.ReleaseDate), Width = 95,
                DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd" } },
            new DataGridViewTextBoxColumn { Name = "ProductCatalogNo", HeaderText = "品番",
                DataPropertyName = nameof(Product.ProductCatalogNo), Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "タイトル",
                DataPropertyName = nameof(Product.Title),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "KindDisplay", HeaderText = "種別",
                DataPropertyName = nameof(ProductRow.KindDisplay), Width = 90 },
            new DataGridViewTextBoxColumn { Name = "PriceInc", HeaderText = "税込",
                DataPropertyName = nameof(ProductRow.PriceIncDisplay), Width = 70,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "DiscCountDisplay", HeaderText = "枚数",
                DataPropertyName = nameof(ProductRow.DiscCountDisplay), Width = 50,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } }
        });

        // ── 所属ディスク一覧 ──
        gridDiscs.Columns.Clear();
        gridDiscs.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "DiscNoInSet", HeaderText = "#",
                DataPropertyName = nameof(Disc.DiscNoInSet), Width = 40,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "CatalogNo", HeaderText = "品番",
                DataPropertyName = nameof(Disc.CatalogNo), Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "ディスクタイトル",
                DataPropertyName = nameof(Disc.Title),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "MediaFormat", HeaderText = "メディア",
                DataPropertyName = nameof(Disc.MediaFormat), Width = 70 }
        });
    }

    // =========================================================================
    // 初期化
    // =========================================================================

    private async Task InitAsync()
    {
        try
        {
            // 商品種別マスタ
            _productKinds = (await _productKindsRepo.GetAllAsync()).ToList();
            cboKind.DisplayMember = nameof(ProductKind.NameJa);
            cboKind.ValueMember = nameof(ProductKind.KindCode);
            cboKind.DataSource = _productKinds;

            // ディスク種別マスタ
            var discKinds = (await _discKindsRepo.GetAllAsync()).ToList();
            cboDiscKind.DisplayMember = nameof(DiscKind.NameJa);
            cboDiscKind.ValueMember = nameof(DiscKind.KindCode);
            cboDiscKind.DataSource = discKinds;

            // シリーズマスタ（ディスク側：先頭にオールスターズ(NULL)を差し込む）
            var allSeries = (await _seriesRepo.GetAllAsync()).ToList();
            var seriesItems = new List<SeriesItem> { new SeriesItem(null, "(オールスターズ)") };
            foreach (var s in allSeries)
            {
                seriesItems.Add(new SeriesItem(s.SeriesId, $"[{s.SeriesId}] {s.TitleShort ?? s.Title}"));
            }
            cboDiscSeries.DisplayMember = nameof(SeriesItem.Label);
            cboDiscSeries.ValueMember = nameof(SeriesItem.Id);
            cboDiscSeries.DataSource = seriesItems;

            // メディアフォーマット（discs.media_format の ENUM 値を固定リストで持つ）
            cboMediaFormat.Items.Clear();
            cboMediaFormat.Items.AddRange(new object[] { "CD", "CD_ROM", "DVD", "BD", "DL", "OTHER" });
            cboMediaFormat.SelectedItem = "CD";

            await ReloadProductsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>商品一覧を DB から取得し直して画面へ反映する。</summary>
    private async Task ReloadProductsAsync()
    {
        try
        {
            _products = (await _productsRepo.GetAllAsync()).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // 検索フィルタ
    // =========================================================================

    /// <summary>検索ボックスに応じて商品グリッドを絞り込む（品番 / タイトル / 略称 / 英語タイトル の部分一致）。</summary>
    private void ApplyFilter()
    {
        string kw = txtSearch.Text.Trim();
        IEnumerable<Product> q = _products;
        if (!string.IsNullOrEmpty(kw))
        {
            q = q.Where(p =>
                (p.ProductCatalogNo?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Title?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.TitleShort?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.TitleEn?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // 翻訳値用ラッパに詰め替えてバインド
        var rows = q.Select(p => new ProductRow(p, _productKinds)).ToList();
        gridProducts.DataSource = null;
        gridProducts.DataSource = rows;
    }

    // =========================================================================
    // 商品選択・編集
    // =========================================================================

    private async Task OnProductSelectedAsync()
    {
        if (gridProducts.CurrentRow?.DataBoundItem is not ProductRow pr)
        {
            ClearProductForm();
            _discs.Clear();
            gridDiscs.DataSource = null;
            ClearDiscForm();
            return;
        }
        BindProductToForm(pr.Inner);

        try
        {
            _discs = (await _discsRepo.GetByProductCatalogNoAsync(pr.Inner.ProductCatalogNo)).ToList();
            gridDiscs.DataSource = null;
            gridDiscs.DataSource = _discs;
            ClearDiscForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void BindProductToForm(Product p)
    {
        txtProductCatalogNo.Text = p.ProductCatalogNo;
        txtProductCatalogNo.ReadOnly = !string.IsNullOrEmpty(p.ProductCatalogNo);
        txtTitle.Text = p.Title;
        txtTitleShort.Text = p.TitleShort ?? "";
        txtTitleEn.Text = p.TitleEn ?? "";
        cboKind.SelectedValue = p.ProductKindCode;
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

    private void ClearProductForm()
    {
        txtProductCatalogNo.Text = "";
        txtProductCatalogNo.ReadOnly = false;
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
    }

    /// <summary>
    /// 税込価格の「自動計算」ボタン。発売日と税抜価格から日本の消費税率を適用して税込を算出し、
    /// 切り捨てで <see cref="numPriceInc"/> に反映する（音楽・映像ソフト業界の慣例に合わせる）。
    /// </summary>
    private void AutoCalcTaxInclusive()
    {
        int priceEx = (int)numPriceEx.Value;
        if (priceEx <= 0)
        {
            MessageBox.Show(this, "税抜価格を先に入力してください。", "情報",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // dtRelease.Value.Date で時刻成分をクリアした DateTime を ComputeTaxInclusive に渡す。
        // Product.ReleaseDate と型を揃え、税率判定境界を日付単位で比較する。
        numPriceInc.Value = ComputeTaxInclusive(priceEx, dtRelease.Value.Date);
    }

    /// <summary>
    /// 発売日と税抜価格から税込価格（切り捨て）を算出する。
    /// 消費税率の区切り日:
    /// <list type="bullet">
    ///   <item>〜 1989-03-31: 0% （消費税導入前）</item>
    ///   <item>1989-04-01 〜 1997-03-31: 3%</item>
    ///   <item>1997-04-01 〜 2014-03-31: 5%</item>
    ///   <item>2014-04-01 〜 2019-09-30: 8%</item>
    ///   <item>2019-10-01 〜: 10%</item>
    /// </list>
    /// 引数は <see cref="DateTime"/>。<see cref="Product.ReleaseDate"/> 自体が DateTime のためそのまま渡せる。
    /// 比較は日付単位（時刻成分を含めても境界判定の結果は同じだが、呼び出し側で <c>.Date</c> 済みを想定）。
    /// </summary>
    private static int ComputeTaxInclusive(int priceEx, DateTime releaseDate)
    {
        double rate;
        if (releaseDate < new DateTime(1989, 4, 1)) rate = 0.00;
        else if (releaseDate < new DateTime(1997, 4, 1)) rate = 0.03;
        else if (releaseDate < new DateTime(2014, 4, 1)) rate = 0.05;
        else if (releaseDate < new DateTime(2019, 10, 1)) rate = 0.08;
        else rate = 0.10;
        // FLOOR 相当：業界慣例の切り捨てに揃える
        return (int)Math.Floor(priceEx * (1.0 + rate));
    }

    private Product BuildProductFromForm()
    {
        return new Product
        {
            ProductCatalogNo = txtProductCatalogNo.Text.Trim(),
            Title = txtTitle.Text.Trim(),
            TitleShort = NullIfEmpty(txtTitleShort.Text),
            TitleEn = NullIfEmpty(txtTitleEn.Text),
            ProductKindCode = cboKind.SelectedValue?.ToString() ?? "OTHER",
            // dtRelease.Value.Date は時刻成分 00:00:00 に正規化された DateTime。
            // Product.ReleaseDate は DateTime 型なのでそのまま代入する。
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

    private async Task SaveProductAsync()
    {
        try
        {
            var p = BuildProductFromForm();
            if (string.IsNullOrWhiteSpace(p.ProductCatalogNo)) { MessageBox.Show(this, "代表品番は必須です。"); return; }
            if (string.IsNullOrWhiteSpace(p.Title)) { MessageBox.Show(this, "タイトルは必須です。"); return; }

            // 税込が空で税抜がある場合、自動計算して埋める（ユーザが明示的に 0 にしたい場合は
            // 価格を 0 のまま保存する = NULL で保存される挙動を維持）
            if ((p.PriceIncTax is null or 0) && p.PriceExTax is int ex && ex > 0)
            {
                p.PriceIncTax = ComputeTaxInclusive(ex, p.ReleaseDate);
                numPriceInc.Value = p.PriceIncTax.Value;
            }

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
            await ReloadProductsAsync();
            // 保存直後のフォーカスは上書き保存した行に戻しておきたい
            SelectProductRow(p.ProductCatalogNo);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>指定品番の行を商品グリッドで選択し直す。</summary>
    private void SelectProductRow(string catalogNo)
    {
        for (int i = 0; i < gridProducts.Rows.Count; i++)
        {
            if (gridProducts.Rows[i].DataBoundItem is ProductRow pr &&
                string.Equals(pr.Inner.ProductCatalogNo, catalogNo, StringComparison.Ordinal))
            {
                gridProducts.ClearSelection();
                gridProducts.Rows[i].Selected = true;
                gridProducts.CurrentCell = gridProducts.Rows[i].Cells[0];
                return;
            }
        }
    }

    private async Task DeleteProductAsync()
    {
        if (gridProducts.CurrentRow?.DataBoundItem is not ProductRow pr) return;
        if (Confirm($"商品 [{pr.Inner.Title}] を論理削除しますか？\n所属ディスクは残ります（ディスク側でも必要に応じて削除してください）。") != DialogResult.Yes) return;
        try
        {
            await _productsRepo.SoftDeleteAsync(pr.Inner.ProductCatalogNo, Environment.UserName);
            await ReloadProductsAsync();
            ClearProductForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // ディスク選択・編集
    // =========================================================================

    private void OnDiscSelected()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc d)
        {
            ClearDiscForm();
            return;
        }
        BindDiscToForm(d);
    }

    private void BindDiscToForm(Disc d)
    {
        txtCatalogNo.Text = d.CatalogNo;
        txtCatalogNo.ReadOnly = !string.IsNullOrEmpty(d.CatalogNo);
        txtDiscTitle.Text = d.Title ?? "";
        txtDiscTitleShort.Text = d.TitleShort ?? "";
        txtDiscTitleEn.Text = d.TitleEn ?? "";
        SelectDiscSeriesComboValue(d.SeriesId);
        numDiscNoInSet.Value = d.DiscNoInSet ?? 0;
        cboDiscKind.SelectedValue = d.DiscKindCode ?? "";
        cboMediaFormat.SelectedItem = d.MediaFormat ?? "CD";
        txtMcn.Text = d.Mcn ?? "";
        numTotalTracks.Value = d.TotalTracks ?? 0;
        txtVolumeLabel.Text = d.VolumeLabel ?? "";
        txtDiscNotes.Text = d.Notes ?? "";
    }

    private void ClearDiscForm()
    {
        txtCatalogNo.Text = "";
        txtCatalogNo.ReadOnly = false;
        txtDiscTitle.Text = "";
        txtDiscTitleShort.Text = "";
        txtDiscTitleEn.Text = "";
        if (cboDiscSeries.Items.Count > 0) cboDiscSeries.SelectedIndex = 0;
        numDiscNoInSet.Value = 0;
        if (cboDiscKind.Items.Count > 0) cboDiscKind.SelectedIndex = 0;
        cboMediaFormat.SelectedItem = "CD";
        txtMcn.Text = "";
        numTotalTracks.Value = 0;
        txtVolumeLabel.Text = "";
        txtDiscNotes.Text = "";
    }

    /// <summary>
    /// 新規ディスク登録用にフォームを初期化する。親商品のタイトルを初期タイトルとして入れておき、
    /// 組内番号は既存ディスク数 + 1 を暫定値として入れる（複数枚組の追加入力を速くする狙い）。
    /// </summary>
    private void ClearDiscFormForNew()
    {
        ClearDiscForm();
        if (gridProducts.CurrentRow?.DataBoundItem is ProductRow pr)
        {
            txtDiscTitle.Text = pr.Inner.Title; // 単品のデフォルトは商品タイトル
            numDiscNoInSet.Value = Math.Min(numDiscNoInSet.Maximum, _discs.Count + 1);
        }
    }

    private void SelectDiscSeriesComboValue(int? seriesId)
    {
        if (cboDiscSeries.Items.Count == 0) return;
        foreach (var item in cboDiscSeries.Items)
        {
            if (item is SeriesItem si && si.Id == seriesId)
            {
                cboDiscSeries.SelectedItem = si;
                return;
            }
        }
        cboDiscSeries.SelectedIndex = 0;
    }

    private async Task SaveDiscAsync()
    {
        if (gridProducts.CurrentRow?.DataBoundItem is not ProductRow pr)
        {
            MessageBox.Show(this, "先に親となる商品を選択してください。"); return;
        }
        try
        {
            var d = new Disc
            {
                CatalogNo = txtCatalogNo.Text.Trim(),
                ProductCatalogNo = pr.Inner.ProductCatalogNo,
                Title = NullIfEmpty(txtDiscTitle.Text),
                TitleShort = NullIfEmpty(txtDiscTitleShort.Text),
                TitleEn = NullIfEmpty(txtDiscTitleEn.Text),
                SeriesId = (cboDiscSeries.SelectedItem as SeriesItem)?.Id,
                DiscNoInSet = numDiscNoInSet.Value == 0 ? null : (uint)numDiscNoInSet.Value,
                DiscKindCode = cboDiscKind.SelectedValue?.ToString(),
                MediaFormat = (cboMediaFormat.SelectedItem?.ToString()) ?? "CD",
                Mcn = NullIfEmpty(txtMcn.Text),
                TotalTracks = numTotalTracks.Value == 0 ? null : (byte)numTotalTracks.Value,
                VolumeLabel = NullIfEmpty(txtVolumeLabel.Text),
                Notes = NullIfEmpty(txtDiscNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrWhiteSpace(d.CatalogNo)) { MessageBox.Show(this, "品番は必須です。"); return; }
            if (string.IsNullOrWhiteSpace(d.ProductCatalogNo)) { MessageBox.Show(this, "商品が選択されていません。"); return; }

            await _discsRepo.UpsertAsync(d);
            MessageBox.Show(this, $"ディスク [{d.CatalogNo}] を保存しました。");

            _discs = (await _discsRepo.GetByProductCatalogNoAsync(pr.Inner.ProductCatalogNo)).ToList();
            gridDiscs.DataSource = null;
            gridDiscs.DataSource = _discs;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteDiscAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc d) return;
        if (Confirm($"ディスク [{d.CatalogNo}] を論理削除しますか？") != DialogResult.Yes) return;
        try
        {
            await _discsRepo.SoftDeleteAsync(d.CatalogNo, Environment.UserName);
            if (gridProducts.CurrentRow?.DataBoundItem is ProductRow pr)
            {
                _discs = (await _discsRepo.GetByProductCatalogNoAsync(pr.Inner.ProductCatalogNo)).ToList();
                gridDiscs.DataSource = null;
                gridDiscs.DataSource = _discs;
            }
            ClearDiscForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // ヘルパ
    // =========================================================================

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>
    /// 商品グリッド表示用の翻訳済みラッパ。元 Product と商品種別マスタから、表示用の文字列を組み立てて保持する。
    /// Dapper マッピング対象外（UI 表示専用）。
    /// </summary>
    private sealed class ProductRow
    {
        public Product Inner { get; }
        // Product.ReleaseDate と同じ DateTime 型をそのまま見せる。グリッドの DataPropertyName でこのプロパティを参照し、
        // セルスタイルの Format = "yyyy-MM-dd" で日付部のみ表示する。
        public DateTime ReleaseDate => Inner.ReleaseDate;
        public string ProductCatalogNo => Inner.ProductCatalogNo;
        public string Title => Inner.Title;
        public string KindDisplay { get; }
        public string PriceIncDisplay { get; }
        public string DiscCountDisplay { get; }

        public ProductRow(Product p, IReadOnlyList<ProductKind> kinds)
        {
            Inner = p;
            var kind = kinds.FirstOrDefault(k =>
                string.Equals(k.KindCode, p.ProductKindCode, StringComparison.Ordinal));
            KindDisplay = kind?.NameJa ?? p.ProductKindCode ?? "";
            PriceIncDisplay = p.PriceIncTax.HasValue
                ? p.PriceIncTax.Value.ToString("N0", CultureInfo.InvariantCulture)
                : "";
            DiscCountDisplay = p.DiscCount <= 0 ? "" : p.DiscCount.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>ディスクシリーズコンボの項目型。Id=null はオールスターズ。</summary>
    private sealed record SeriesItem(int? Id, string Label);
}
