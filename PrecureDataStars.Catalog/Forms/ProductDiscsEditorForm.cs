#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品・ディスク統合管理フォーム。
/// <para>
/// 旧 <c>ProductsEditorForm</c> と <c>DiscsEditorForm</c> のうち、商品＋ディスクの編集機能を
/// 1 画面に統合したもの。トラック編集は <see cref="TracksEditorForm"/> に分離されている。
/// </para>
/// <para>
/// 商品詳細の流通系は社名マスタ紐付け 2 行（レーベル → 販売元）に集約している。
/// 発売元／販売元／レーベルは product_companies への ID 紐付けで持ち、UI に
/// 自由入力欄は持たない。紐付け先 ID は picker（<see
/// cref="ProductCompanyPickerDialog"/>）で選び、表示名（ReadOnly TextBox）と内部 ID（Tag）の
/// 2 表現で管理する。
/// </para>
/// </summary>
public partial class ProductDiscsEditorForm : Form
{
    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly ProductCompaniesRepository _productCompaniesRepo;

    private List<Product> _products = new();
    private List<Disc> _discs = new();
    private List<ProductKind> _productKinds = new();
    // 商品社名マスタのキャッシュ（picker の選択結果を表示名に変換する用途で使う）
    private List<ProductCompany> _productCompanies = new();

    public ProductDiscsEditorForm(
        ProductsRepository productsRepo,
        DiscsRepository discsRepo,
        ProductKindsRepository productKindsRepo,
        DiscKindsRepository discKindsRepo,
        SeriesRepository seriesRepo,
        ProductCompaniesRepository productCompaniesRepo)
    {
        _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _productKindsRepo = productKindsRepo ?? throw new ArgumentNullException(nameof(productKindsRepo));
        _discKindsRepo = discKindsRepo ?? throw new ArgumentNullException(nameof(discKindsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _productCompaniesRepo = productCompaniesRepo ?? throw new ArgumentNullException(nameof(productCompaniesRepo));

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

        // 社名マスタ紐付けボタン群のハンドラ。
        // 「選択...」は ProductCompanyPickerDialog を開いて選択結果を ReadOnly テキストに反映、
        // 「解除」は紐付けを NULL に戻す（Tag = null、Text = 空）。
        btnLabelCompanyPick.Click        += (_, __) => OnPickCompanyClick(txtLabelCompanyName);
        btnLabelCompanyClear.Click       += (_, __) => OnClearCompanyClick(txtLabelCompanyName);
        btnDistributorCompanyPick.Click  += (_, __) => OnPickCompanyClick(txtDistributorCompanyName);
        btnDistributorCompanyClear.Click += (_, __) => OnClearCompanyClick(txtDistributorCompanyName);

        btnDiscNew.Click += (_, __) => ClearDiscFormForNew();
        btnDiscSave.Click += async (_, __) => await SaveDiscAsync();
        btnDiscDelete.Click += async (_, __) => await DeleteDiscAsync();

        // 動的レイアウトロジック
        Load += (_, __) => InitializeLayout();
        splitProduct.SizeChanged += (_, __) => Apply60To40(splitProduct);
        splitDisc.SizeChanged += (_, __) => Apply60To40(splitDisc);
        pnlProductDetail.SizeChanged += (_, __) => LayoutProductDetailPanel();
        pnlDiscDetail.SizeChanged += (_, __) => LayoutDiscDetailPanel();
    }

    /// <summary>
    /// 起動直後のレイアウト初期化。スプリッタを所定の比率/固定値に整え、詳細パネルの動的レイアウトも一度走らせる。
    /// </summary>
    private void InitializeLayout()
    {
        const int bottomFixedHeight = 400;
        int top = splitMain.Height - bottomFixedHeight - splitMain.SplitterWidth;
        if (top < splitMain.Panel1MinSize) top = splitMain.Panel1MinSize;
        splitMain.SplitterDistance = top;

        Apply60To40(splitProduct);
        Apply60To40(splitDisc);

        LayoutProductDetailPanel();
        LayoutDiscDetailPanel();
    }

    /// <summary>左右分割の SplitContainer を 60:40 に整える。</summary>
    private static void Apply60To40(SplitContainer split)
    {
        int usable = split.Width - split.SplitterWidth;
        if (usable < 2) return;
        int target = (int)(usable * 0.6);
        int min = split.Panel1MinSize;
        int max = Math.Max(min, usable - split.Panel2MinSize);
        if (target < min) target = min;
        else if (target > max) target = max;
        split.SplitterDistance = target;
    }

    /// <summary>
    /// 商品詳細パネル内の入力欄とボタン群を、現在のパネル幅に合わせて動的に再配置する。
    /// </summary>
    private void LayoutProductDetailPanel()
    {
        int innerW = pnlProductDetail.ClientSize.Width;
        if (innerW <= 0) return;

        const int btnW = 80;
        const int btnRightMargin = 10;
        int btnX = innerW - btnW - btnRightMargin;

        const int labelW = 100;
        const int leftMargin = 22;
        const int fieldGap = 16;
        int fieldX = leftMargin + labelW;
        int fieldEndX = btnX - fieldGap;
        int fieldW = fieldEndX - fieldX;
        if (fieldW < 100) fieldW = 100;

        // 通常入力欄（社名紐付け行と税込価格行は別処理）。
        Control[] generalFields =
        {
            txtProductCatalogNo, txtTitle, txtTitleShort, txtTitleEn,
            cboKind, dtRelease, numPriceEx,
            // numPriceInc は特例（下で別処理）
            numDiscCount,
            txtAsin, txtApple, txtSpotify, txtNotes
        };
        foreach (var c in generalFields)
        {
            c.Width = fieldW;
            c.Location = new Point(fieldX, c.Location.Y);
        }

        // 税込価格行の特例
        const int priceIncW = 170;
        numPriceInc.Width = priceIncW;
        numPriceInc.Location = new Point(fieldX, numPriceInc.Location.Y);
        btnAutoTax.Location = new Point(numPriceInc.Right + 6, numPriceInc.Location.Y);

        // 社名マスタ紐付け行：1 行 = [ReadOnly 表示テキスト] [選択...60px] [解除 48px]
        const int pickBtnW = 60;
        const int clearBtnW = 48;
        const int companyBtnGap = 4;
        const int companyRowReserved = pickBtnW + companyBtnGap + clearBtnW + companyBtnGap; // = 116
        int nameW = fieldW - companyRowReserved;
        if (nameW < 60) nameW = 60;

        void LayoutCompanyRow(TextBox txtName, Button btnPick, Button btnClear)
        {
            int y = txtName.Location.Y;
            txtName.Location = new Point(fieldX, y);
            txtName.Width = nameW;
            btnPick.Location  = new Point(fieldX + nameW + companyBtnGap, y);
            btnClear.Location = new Point(fieldX + nameW + companyBtnGap + pickBtnW + companyBtnGap, y);
        }
        LayoutCompanyRow(txtLabelCompanyName, btnLabelCompanyPick, btnLabelCompanyClear);
        LayoutCompanyRow(txtDistributorCompanyName, btnDistributorCompanyPick, btnDistributorCompanyClear);

        // ボタン列（新規・保存・削除）
        btnProductNew.Location = new Point(btnX, 8);
        btnProductSave.Location = new Point(btnX, 40);
        btnProductDelete.Location = new Point(btnX, 72);
    }

    /// <summary>
    /// ディスク詳細パネル内の入力欄とボタン群を動的に再配置する。
    /// </summary>
    private void LayoutDiscDetailPanel()
    {
        int innerW = pnlDiscDetail.ClientSize.Width;
        if (innerW <= 0) return;

        const int btnW = 80;
        const int btnRightMargin = 10;
        int btnX = innerW - btnW - btnRightMargin;

        const int dLabelW = 100;
        const int leftMargin = 22;
        const int fieldGap = 16;
        int fieldX = leftMargin + dLabelW;
        int fieldEndX = btnX - fieldGap;
        int fieldW = fieldEndX - fieldX;
        if (fieldW < 100) fieldW = 100;

        Control[] discFields =
        {
            txtCatalogNo, numDiscNoInSet, txtDiscTitle, txtDiscTitleShort, txtDiscTitleEn,
            cboDiscSeries, cboDiscKind, cboMediaFormat, txtMcn, numTotalTracks,
            txtVolumeLabel, txtDiscNotes
        };
        foreach (var c in discFields)
        {
            c.Width = fieldW;
            c.Location = new Point(fieldX, c.Location.Y);
        }

        btnDiscNew.Location = new Point(btnX, 8);
        btnDiscSave.Location = new Point(btnX, 40);
        btnDiscDelete.Location = new Point(btnX, 72);
    }

    // =========================================================================
    // グリッド列定義
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
            _productKinds = (await _productKindsRepo.GetAllAsync()).ToList();
            cboKind.DisplayMember = nameof(ProductKind.NameJa);
            cboKind.ValueMember = nameof(ProductKind.KindCode);
            cboKind.DataSource = _productKinds;

            var discKinds = (await _discKindsRepo.GetAllAsync()).ToList();
            cboDiscKind.DisplayMember = nameof(DiscKind.NameJa);
            cboDiscKind.ValueMember = nameof(DiscKind.KindCode);
            cboDiscKind.DataSource = discKinds;

            var allSeries = (await _seriesRepo.GetAllAsync()).ToList();
            var seriesItems = new List<SeriesItem> { new SeriesItem(null, "(オールスターズ)") };
            foreach (var s in allSeries)
            {
                seriesItems.Add(new SeriesItem(s.SeriesId, $"[{s.SeriesId}] {s.TitleShort ?? s.Title}"));
            }
            cboDiscSeries.DisplayMember = nameof(SeriesItem.Label);
            cboDiscSeries.ValueMember = nameof(SeriesItem.Id);
            cboDiscSeries.DataSource = seriesItems;

            cboMediaFormat.Items.Clear();
            cboMediaFormat.Items.AddRange(new object[] { "CD", "CD_ROM", "DVD", "BD", "DL", "OTHER" });
            cboMediaFormat.SelectedItem = "CD";

            // 商品社名マスタもキャッシュしておく（picker から戻ってきた ID を名前解決するため）
            _productCompanies = (await _productCompaniesRepo.GetAllAsync()).ToList();

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
            RebindDiscGrid();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// ディスクグリッドを再バインドし、先頭行を明示選択してディスク詳細フォームに反映する。
    /// SelectionChanged のタイミング依存を排除するため、ヘルパに集約。
    /// </summary>
    private void RebindDiscGrid()
    {
        gridDiscs.DataSource = null;
        gridDiscs.DataSource = _discs;
        if (_discs.Count > 0)
        {
            gridDiscs.ClearSelection();
            gridDiscs.Rows[0].Selected = true;
            gridDiscs.CurrentCell = gridDiscs.Rows[0].Cells[0];
        }
        OnDiscSelected();
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
        // 社名マスタ紐付け状態を Tag/Text に展開
        BindCompanyToRow(txtLabelCompanyName, p.LabelProductCompanyId);
        BindCompanyToRow(txtDistributorCompanyName, p.DistributorProductCompanyId);
        txtAsin.Text = p.AmazonAsin ?? "";
        txtApple.Text = p.AppleAlbumId ?? "";
        txtSpotify.Text = p.SpotifyAlbumId ?? "";
        txtNotes.Text = p.Notes ?? "";
    }

    /// <summary>
    /// 商品社名 ID から社名マスタを引いて、表示テキスト（社名・和）と Tag（ID）を設定する。
    /// ID が NULL、または対応する社名がキャッシュに見つからない場合は未紐付け表示にフォールバック。
    /// </summary>
    private void BindCompanyToRow(TextBox txtName, int? productCompanyId)
    {
        if (productCompanyId is int id)
        {
            var hit = _productCompanies.FirstOrDefault(c => c.ProductCompanyId == id);
            if (hit != null)
            {
                txtName.Tag = id;
                txtName.Text = hit.NameJa;
                return;
            }
            // 論理削除済みや別スコープから持ち込まれた ID のケース：ID 自体は維持して可視化する。
            txtName.Tag = id;
            txtName.Text = $"(社名 ID {id} ※マスタ未取得)";
            return;
        }
        txtName.Tag = null;
        txtName.Text = "";
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
        BindCompanyToRow(txtLabelCompanyName, null);
        BindCompanyToRow(txtDistributorCompanyName, null);
        txtAsin.Text = "";
        txtApple.Text = "";
        txtSpotify.Text = "";
        txtNotes.Text = "";
    }

    /// <summary>
    /// 「選択...」ボタンの共通ハンドラ。<see cref="ProductCompanyPickerDialog"/> を開き、選択結果を
    /// ReadOnly テキスト（表示名）と Tag（ID）に反映する。キャンセル時は何もしない。
    /// </summary>
    private void OnPickCompanyClick(TextBox txtName)
    {
        using var dlg = new ProductCompanyPickerDialog(_productCompaniesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        if (dlg.SelectedId is not int id) return;

        // picker のあと、新規登録分が混じっている可能性に備えてキャッシュを再読込しておく
        try
        {
            _productCompanies = _productCompaniesRepo.GetAllAsync().GetAwaiter().GetResult().ToList();
        }
        catch
        {
            // 失敗してもキャッシュは旧データのまま動作継続
        }
        BindCompanyToRow(txtName, id);
    }

    /// <summary>「解除」ボタンの共通ハンドラ。紐付け（Tag）を NULL に戻す。</summary>
    private void OnClearCompanyClick(TextBox txtName)
    {
        BindCompanyToRow(txtName, null);
    }

    /// <summary>税込価格の「自動計算」ボタン。</summary>
    private void AutoCalcTaxInclusive()
    {
        int priceEx = (int)numPriceEx.Value;
        if (priceEx <= 0)
        {
            MessageBox.Show(this, "税抜価格を先に入力してください。", "情報",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        numPriceInc.Value = ComputeTaxInclusive(priceEx, dtRelease.Value.Date);
    }

    /// <summary>
    /// 発売日と税抜価格から税込価格（切り捨て）を算出する。消費税率の区切り日は日本標準。
    /// </summary>
    private static int ComputeTaxInclusive(int priceEx, DateTime releaseDate)
    {
        double rate;
        if (releaseDate < new DateTime(1989, 4, 1)) rate = 0.00;
        else if (releaseDate < new DateTime(1997, 4, 1)) rate = 0.03;
        else if (releaseDate < new DateTime(2014, 4, 1)) rate = 0.05;
        else if (releaseDate < new DateTime(2019, 10, 1)) rate = 0.08;
        else rate = 0.10;
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
            ReleaseDate = dtRelease.Value.Date,
            PriceExTax = numPriceEx.Value == 0 ? null : (int)numPriceEx.Value,
            PriceIncTax = numPriceInc.Value == 0 ? null : (int)numPriceInc.Value,
            DiscCount = (byte)Math.Max(1, (int)numDiscCount.Value),
            // 社名マスタ紐付け ID（Tag に保持）を取り出す
            LabelProductCompanyId = txtLabelCompanyName.Tag as int?,
            DistributorProductCompanyId = txtDistributorCompanyName.Tag as int?,
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
    /// 新規ディスク登録用にフォームを初期化する。
    /// </summary>
    private void ClearDiscFormForNew()
    {
        ClearDiscForm();
        if (gridProducts.CurrentRow?.DataBoundItem is ProductRow pr)
        {
            txtDiscTitle.Text = pr.Inner.Title;
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

            // 既存レコードがあれば、フォームで持っていない物理情報・CD-Text・計算済み識別子等を引き継ぐ。
            var existingDisc = await _discsRepo.GetByCatalogNoAsync(d.CatalogNo);
            if (existingDisc is not null)
            {
                d.TotalLengthFrames     = existingDisc.TotalLengthFrames;
                d.TotalLengthMs         = existingDisc.TotalLengthMs;
                d.NumChapters           = existingDisc.NumChapters;
                d.CdTextAlbumTitle      = existingDisc.CdTextAlbumTitle;
                d.CdTextAlbumPerformer  = existingDisc.CdTextAlbumPerformer;
                d.CdTextAlbumSongwriter = existingDisc.CdTextAlbumSongwriter;
                d.CdTextAlbumComposer   = existingDisc.CdTextAlbumComposer;
                d.CdTextAlbumArranger   = existingDisc.CdTextAlbumArranger;
                d.CdTextAlbumMessage    = existingDisc.CdTextAlbumMessage;
                d.CdTextDiscId          = existingDisc.CdTextDiscId;
                d.CdTextGenre           = existingDisc.CdTextGenre;
                d.CddbDiscId            = existingDisc.CddbDiscId;
                d.MusicbrainzDiscId     = existingDisc.MusicbrainzDiscId;
                d.LastReadAt            = existingDisc.LastReadAt;
                d.CreatedBy = existingDisc.CreatedBy ?? d.CreatedBy;
            }

            await _discsRepo.UpsertAsync(d);
            MessageBox.Show(this, $"ディスク [{d.CatalogNo}] を保存しました。");

            _discs = (await _discsRepo.GetByProductCatalogNoAsync(pr.Inner.ProductCatalogNo)).ToList();
            RebindDiscGrid();
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
                RebindDiscGrid();
            }
            else
            {
                ClearDiscForm();
            }
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
    /// </summary>
    private sealed class ProductRow
    {
        public Product Inner { get; }
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