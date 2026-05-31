#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Common.Services;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 商品・ディスク統合管理フォーム。
/// 旧 <c>ProductsEditorForm</c> と <c>DiscsEditorForm</c> のうち、商品＋ディスクの編集機能を
/// 1 画面に統合したもの。トラック編集は <see cref="TracksEditorForm"/> に分離されている。
/// 商品詳細の流通系は社名マスタ紐付け 2 行（レーベル → 販売元）に集約している。
/// 発売元／販売元／レーベルは product_companies への ID 紐付けで持ち、UI に
/// 自由入力欄は持たない。紐付け先 ID は picker（<see
/// cref="ProductCompanyPickerDialog"/>）で選び、表示名（ReadOnly TextBox）と内部 ID（Tag）の
/// 2 表現で管理する。
/// </summary>
public partial class ProductDiscsEditorForm : Form
{
    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly ProductCompaniesRepository _productCompaniesRepo;
    // ディスクの追加・削除・保存後に商品配下の組内番号（disc_no_in_set）と
    // products.disc_count を正規化するため、共通サービスを 1 度だけ組み立てて再利用する。
    // 「単品なら NULL、複数枚なら 1..N」の不変条件を全経路で揃える窓口。
    private readonly DiscRegistrationService _registrationService;

    private List<Product> _products = new();
    private List<Disc> _discs = new();
    private List<ProductKind> _productKinds = new();
    // 商品社名マスタのキャッシュ（picker の選択結果を表示名に変換する用途で使う）
    private List<ProductCompany> _productCompanies = new();

    // 現在フォームに表示中の商品（ジャケット代表選択の即時保存に使う）。新規/未選択時は null。
    private Product? _currentProduct;
    // ジャケット選択 UI（ラジオ/チェック）をプログラムから設定している最中は true。
    // CheckedChanged の自動保存ハンドラが、バインド中の値設定で誤発火しないようにするガード。
    private bool _isLoadingCover;
    // ジャケットプレビューの世代カウンタ。商品を素早く切り替えたとき、古い非同期ロードが
    // 後から完了して別商品の PictureBox を上書きしないよう、ロード開始時の世代と照合する。
    private int _coverPreviewGen;
    // ジャケットプレビュー画像取得用の HttpClient。Amazon CDN がデフォルト UA を弾くことがあるためブラウザ UA を載せる。
    private readonly HttpClient _coverHttp = CreateCoverHttpClient();

    private static HttpClient CreateCoverHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        return http;
    }

    public ProductDiscsEditorForm(
        ProductsRepository productsRepo,
        DiscsRepository discsRepo,
        TracksRepository tracksRepo,
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
        _registrationService = new DiscRegistrationService(
            _discsRepo,
            _productsRepo,
            tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo)));

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
        btnFetchCover.Click += async (_, __) => await FetchCoverImagesAsync();
        // Creators API SearchItems で商品名から CD / デジタル両方の ASIN とジャケット画像 URL を
        // 同時取得するダイアログ。選択結果は ASIN 2 欄と cover_image_* に反映される。
        btnAmazonSearch.Click += async (_, __) => await OpenAmazonSearchDialogAsync();
        btnAutoTax.Click += (_, __) => AutoCalcTaxInclusive();

        // ジャケット代表選択 / 両方表示の変更は即時 DB 保存（バインド中は _isLoadingCover で抑止）。
        rbCoverCd.CheckedChanged += async (_, __) => await OnCoverSelectionChangedAsync();
        rbCoverDigital.CheckedChanged += async (_, __) => await OnCoverSelectionChangedAsync();
        chkCoverShowBoth.CheckedChanged += async (_, __) => await OnCoverSelectionChangedAsync();
        // プレビュー用 HttpClient はフォーム破棄時に解放。
        FormClosed += (_, __) => _coverHttp.Dispose();

        // 社名マスタ紐付けボタン群のハンドラ。
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

    /// <summary>起動直後のレイアウト初期化。スプリッタを所定の比率/固定値に整え、詳細パネルの動的レイアウトも一度走らせる。</summary>
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

    /// <summary>商品詳細パネル内の入力欄とボタン群を、現在のパネル幅に合わせて動的に再配置する。</summary>
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
        // ASIN 行は物理（_cd）／デジタル（_digital）の 2 行に分割した。CD 行右端に併置する
        // 「検索...」ボタンは、CD 行の入力欄末尾と整列するように動的に再計算する。
        Control[] generalFields =
        {
            txtProductCatalogNo, txtTitle, txtTitleShort, txtTitleEn,
            cboKind, dtRelease, numPriceEx,
            // numPriceInc は特例（下で別処理）
            numDiscCount,
            txtAsinCd, txtAsinDigital, txtNotes, txtOfficialUrl
        };
        foreach (var c in generalFields)
        {
            c.Width = fieldW;
            c.Location = new Point(fieldX, c.Location.Y);
        }

        // Amazon ASIN (CD) 行に併置する「検索...」ボタンは入力欄右端に寄せて再配置する。
        // フィールド全幅を使い切ると検索ボタンが見切れるため、テキスト幅を縮めて検索ボタンを内側に置く。
        const int amazonSearchBtnW = 60;
        const int amazonSearchBtnGap = 4;
        if (txtAsinCd.Width > amazonSearchBtnW + amazonSearchBtnGap + 40)
        {
            int asinCdY = txtAsinCd.Location.Y;
            int newAsinCdW = fieldW - amazonSearchBtnW - amazonSearchBtnGap;
            txtAsinCd.Width = newAsinCdW;
            btnAmazonSearch.Location = new Point(fieldX + newAsinCdW + amazonSearchBtnGap, asinCdY);
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

    /// <summary>ディスク詳細パネル内の入力欄とボタン群を動的に再配置する。</summary>
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

    // グリッド列定義

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

    // 初期化

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

    // 検索フィルタ

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

    // 商品選択・編集

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

    /// <summary>ディスクグリッドを再バインドし、先頭行を明示選択してディスク詳細フォームに反映する。 SelectionChanged のタイミング依存を排除するため、ヘルパに集約。</summary>
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
        _currentProduct = p;
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
        // ASIN は物理（_cd）／デジタル（_digital）を独立にバインドする。
        txtAsinCd.Text = p.AmazonAsinCd ?? "";
        txtAsinDigital.Text = p.AmazonAsinDigital ?? "";
        txtNotes.Text = p.Notes ?? "";
        txtOfficialUrl.Text = p.OfficialUrl ?? "";
        BindCoverSelectionToForm(p);
    }

    /// <summary>ジャケット選択 UI（CD/デジタルのプレビュー・代表ラジオ・両方表示チェック）を商品状態にバインドする。 ラジオ/チェックはプログラム設定中に CheckedChanged が誤発火しないよう <see cref="_isLoadingCover"/> でガードする。 プレビュー画像は URL から非同期ロードする（世代カウンタで商品切替時の競合を防ぐ）。</summary>
    private void BindCoverSelectionToForm(Product p)
    {
        _isLoadingCover = true;
        try
        {
            bool hasCd = !string.IsNullOrWhiteSpace(p.CoverImageUrlCd);
            bool hasDigital = !string.IsNullOrWhiteSpace(p.CoverImageUrlDigital);

            // 代表ラジオ：明示選択を反映。未選択(NULL)なら実効代表（デジタル優先→CD）に合わせる。
            rbCoverCd.Enabled = hasCd;
            rbCoverDigital.Enabled = hasDigital;
            string effective = p.CoverImageSource switch
            {
                "amazon_cd" => "amazon_cd",
                "amazon_digital" => "amazon_digital",
                _ => hasDigital ? "amazon_digital" : (hasCd ? "amazon_cd" : "")
            };
            rbCoverCd.Checked = effective == "amazon_cd";
            rbCoverDigital.Checked = effective == "amazon_digital";

            // 「両方表示」は CD/デジタル両方の画像があるときだけ意味を持つ。
            chkCoverShowBoth.Enabled = hasCd && hasDigital;
            chkCoverShowBoth.Checked = p.CoverImageShowBoth;
        }
        finally
        {
            _isLoadingCover = false;
        }

        // プレビュー画像を非同期ロード（世代を進めて古いロードの上書きを防ぐ）。
        int gen = ++_coverPreviewGen;
        _ = LoadCoverPreviewAsync(picCoverCd, p.CoverImageUrlCd, gen);
        _ = LoadCoverPreviewAsync(picCoverDigital, p.CoverImageUrlDigital, gen);
    }

    /// <summary>指定 PictureBox にジャケットプレビューを非同期ロードする。 URL が空なら画像をクリア。<paramref name="gen"/> がロード完了時の最新世代と異なる場合は破棄（商品切替の競合防止）。</summary>
    private async Task LoadCoverPreviewAsync(PictureBox pic, string? url, int gen)
    {
        // まず現在の画像をクリア（古い商品の画像が残らないように）。
        var prev = pic.Image;
        pic.Image = null;
        prev?.Dispose();
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bytes = await _coverHttp.GetByteArrayAsync(url).ConfigureAwait(true);
            if (gen != _coverPreviewGen || IsDisposed) return;
            using var ms = new MemoryStream(bytes);
            using var tmp = Image.FromStream(ms);
            // ms / tmp とは独立したコピーを PictureBox に持たせる（ストリーム寿命依存を断つ）。
            pic.Image = new Bitmap(tmp);
        }
        catch
        {
            // 取得失敗時はプレビューなし（機能には影響しない）。
        }
    }

    /// <summary>ジャケット代表ラジオ / 両方表示チェックの変更を即時 DB 保存する。 バインド中（<see cref="_isLoadingCover"/>）や新規・未選択（catalog_no 空）のときは何もしない。</summary>
    private async Task OnCoverSelectionChangedAsync()
    {
        if (_isLoadingCover) return;
        if (_currentProduct is null || string.IsNullOrEmpty(_currentProduct.ProductCatalogNo)) return;
        try
        {
            string? source = rbCoverCd.Checked ? "amazon_cd"
                : rbCoverDigital.Checked ? "amazon_digital"
                : null;
            bool showBoth = chkCoverShowBoth.Checked;
            await _productsRepo.UpdateCoverImageSelectionAsync(_currentProduct.ProductCatalogNo, source, showBoth);
            // メモリ上の商品にも反映（再バインド時のちらつき防止）。
            _currentProduct.CoverImageSource = source;
            _currentProduct.CoverImageShowBoth = showBoth;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>商品社名 ID から社名マスタを引いて、表示テキスト（社名・和）と Tag（ID）を設定する。 ID が NULL、または対応する社名がキャッシュに見つからない場合は未紐付け表示にフォールバック。</summary>
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
        _currentProduct = null;
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
        // ASIN 2 欄ともクリア
        txtAsinCd.Text = "";
        txtAsinDigital.Text = "";
        txtNotes.Text = "";
        txtOfficialUrl.Text = "";

        // ジャケット選択 UI をリセット（プレビュー画像も破棄）。
        _isLoadingCover = true;
        try
        {
            rbCoverCd.Checked = false;
            rbCoverDigital.Checked = false;
            chkCoverShowBoth.Checked = false;
            rbCoverCd.Enabled = rbCoverDigital.Enabled = chkCoverShowBoth.Enabled = false;
        }
        finally { _isLoadingCover = false; }
        _coverPreviewGen++;
        var prevCd = picCoverCd.Image; picCoverCd.Image = null; prevCd?.Dispose();
        var prevDigital = picCoverDigital.Image; picCoverDigital.Image = null; prevDigital?.Dispose();
    }

    /// <summary>「選択...」ボタンの共通ハンドラ。</summary>
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

    /// <summary>発売日と税抜価格から税込価格（切り捨て）を算出する。消費税率の区切り日は日本標準。</summary>
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
            // ASIN は物理（CD/BD/DVD）／デジタル（Amazon Music の MP3 アルバム）を独立に保持。
            // 入力が空文字なら NULL に丸める（DB 制約上 NULL 許容のため）。
            AmazonAsinCd = NullIfEmpty(txtAsinCd.Text),
            AmazonAsinDigital = NullIfEmpty(txtAsinDigital.Text),
            Notes = NullIfEmpty(txtNotes.Text),
            OfficialUrl = NullIfEmpty(txtOfficialUrl.Text),
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

    /// <summary>
    /// ジャケット画像を取得して DB にキャッシュする（手動操作）。
    /// 取得元の優先順位は <c>amazon_digital</c> → <c>amazon_cd</c>。
    /// デジタル（Amazon Music）のジャケットは事業者（レーベル）がアップした正規画像である一方、
    /// CD（特に廃盤）は素人撮影の写真が出品画像として載っているリスクがあるため、デジタルを優先する。
    /// デジタル ASIN（amazon_asin_digital）／物理 ASIN（amazon_asin_cd）が登録されていれば
    /// Creators API GetItems で画像 URL を引く。対象は「画像 URL 未取得」の商品のみ
    /// （鮮度更新ではなく未取得補完）。Creators API のレート制限（1 TPS）に合わせて
    /// 1 件 1.1 秒の間隔で叩く。静的サイトのビルドとは分離した運用：ここで DB に溜め、
    /// SiteBuilder は DB の URL を読むだけ。
    /// </summary>
    private async Task FetchCoverImagesAsync()
    {
        if (Confirm("ASIN があり画像未取得の商品について、ジャケット画像 URL を取得します。\n（優先順位: Amazon デジタル → Amazon CD）\n続行しますか？") != DialogResult.Yes)
            return;

        btnFetchCover.Enabled = false;
        try
        {
            // 画像未取得の商品から、Amazon ASIN を持つものを抽出。
            var all = await _productsRepo.GetAllAsync();
            var targets = all
                .Where(p => string.IsNullOrWhiteSpace(p.CoverImageUrl)
                         && (!string.IsNullOrWhiteSpace(p.AmazonAsinCd)
                          || !string.IsNullOrWhiteSpace(p.AmazonAsinDigital)))
                .ToList();

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "取得対象（ASIN あり・画像未取得）の商品はありません。");
                return;
            }

            // Creators API クライアントは App.config に PaApi.* キーが揃っているときのみ起動する。
            var paApi = PrecureDataStars.AmazonPaApi.PaApiClientFactory.TryCreateFromAppConfig();
            if (paApi == null)
            {
                MessageBox.Show(this,
                    "ジャケット画像取得を使うには App.config に Creators API のキー（PaApi.CredentialId / PaApi.CredentialSecret / PaApi.CredentialVersion / PaApi.PartnerTag）を設定してください。",
                    "Creators API 未設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int ok = 0, miss = 0;
            foreach (var prod in targets)
            {
                // CD・デジタル両系統を取得して両列に保存する（CD とデジタルでジャケットが
                // 異なる場合があるため、両方を保持して後から切り替えられるようにする）。
                string? digitalUrl = null;
                string? cdUrl = null;

                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinDigital))
                {
                    var item = await paApi.GetItemAsync(prod.AmazonAsinDigital!, CancellationToken.None);
                    if (item?.LargeImageUrl is { Length: > 0 } u1) digitalUrl = u1;
                    // Creators API レート制限（1 TPS）順守のため最低 1.1 秒待機。
                    await Task.Delay(1100);
                }
                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinCd))
                {
                    var item = await paApi.GetItemAsync(prod.AmazonAsinCd!, CancellationToken.None);
                    if (item?.LargeImageUrl is { Length: > 0 } u2) cdUrl = u2;
                    await Task.Delay(1100);
                }

                if (digitalUrl != null || cdUrl != null)
                {
                    // 採用ソース：既存の明示選択を尊重しつつ、未選択／選択先が空ならデジタル→CD の既定優先。
                    string? source =
                        (prod.CoverImageSource == "amazon_digital" && digitalUrl != null) ? "amazon_digital" :
                        (prod.CoverImageSource == "amazon_cd" && cdUrl != null) ? "amazon_cd" :
                        (digitalUrl != null ? "amazon_digital" : "amazon_cd");
                    await _productsRepo.UpdateCoverImagesAsync(
                        prod.ProductCatalogNo, cdUrl, digitalUrl, source, DateTime.Now);
                    ok++;
                }
                else
                {
                    miss++;
                }
            }

            await ReloadProductsAsync();
            MessageBox.Show(this,
                $"ジャケット画像の取得が完了しました。\n取得成功: {ok} 件 / 該当なし・失敗: {miss} 件");
        }
        catch (Exception ex) { ShowError(ex); }
        finally { btnFetchCover.Enabled = true; }
    }

    /// <summary>
    /// Creators API SearchItems を使って、現在編集中の商品名から CD（SearchIndex=Music）と
    /// デジタル音源（SearchIndex=DigitalMusic）の両系統を検索し、左右 2 列のダイアログで
    /// 候補から ASIN と画像 URL を選択する。OK で確定されたら、選択された CD ASIN を
    /// <c>txtAsinCd</c>、デジタル ASIN を <c>txtAsinDigital</c> に流し込む。
    /// CD・デジタル両系統の画像 URL を両列へ保存し、代表（表示採用）はダイアログ側の
    /// デジタル優先ロジックで決めて <see cref="ProductsRepository.UpdateCoverImagesAsync"/> に渡す。
    /// Creators API キーが App.config に未設定の場合はその旨を案内して中断する。
    /// </summary>
    private async Task OpenAmazonSearchDialogAsync()
    {
        // Creators API キー未設定の環境では機能を提供しない（既存ツールと挙動を揃える）。
        var paApi = PrecureDataStars.AmazonPaApi.PaApiClientFactory.TryCreateFromAppConfig();
        if (paApi == null)
        {
            MessageBox.Show(this,
                "Amazon 検索を使うには App.config に Creators API のキー（PaApi.CredentialId / PaApi.CredentialSecret / PaApi.CredentialVersion / PaApi.PartnerTag）を設定してください。",
                "Creators API 未設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 検索キーワードの初期値は商品名（編集中ならその場の入力、未保存なら空も可）。
        string initialKeyword = txtTitle.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(initialKeyword))
        {
            MessageBox.Show(this, "先に商品タイトルを入力してください。", "情報",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            btnAmazonSearch.Enabled = false;
            using var dlg = new Dialogs.AmazonProductSearchDialog(paApi, initialKeyword);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            // 選択結果を反映。空文字の場合は既存値を上書きしない（ユーザーが片方だけ採用したいケースに備える）。
            if (!string.IsNullOrWhiteSpace(dlg.SelectedCdAsin))
                txtAsinCd.Text = dlg.SelectedCdAsin;
            if (!string.IsNullOrWhiteSpace(dlg.SelectedDigitalAsin))
                txtAsinDigital.Text = dlg.SelectedDigitalAsin;

            // 画像 URL が返ってきたら products テーブルに直書きする（保存ボタンを待たない、Cover 専用 UPDATE）。
            // CD・デジタル両系統の画像を両列に保存し、代表は dlg.SelectedCoverImageSource（デジタル優先）。
            // 編集中の他項目は影響を受けない（UpdateCoverImagesAsync は cover_image_* だけを触る設計）。
            if (gridProducts.CurrentRow?.DataBoundItem is ProductRow pr
                && (!string.IsNullOrWhiteSpace(dlg.SelectedCdImageUrl)
                    || !string.IsNullOrWhiteSpace(dlg.SelectedDigitalImageUrl)))
            {
                await _productsRepo.UpdateCoverImagesAsync(
                    pr.Inner.ProductCatalogNo,
                    dlg.SelectedCdImageUrl,
                    dlg.SelectedDigitalImageUrl,
                    dlg.SelectedCoverImageSource,
                    DateTime.Now);
                await ReloadProductsAsync();
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { btnAmazonSearch.Enabled = true; }
    }

    // ディスク選択・編集

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

    /// <summary>新規ディスク登録用にフォームを初期化する。</summary>
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
            // ディスク本体の保存直後に「単品なら NULL、複数枚なら 1..N」と disc_count を整える。
            // フォームの numDiscNoInSet 入力（auto-fill で 1 が入る等）に関係なく不変条件を保証する。
            await _registrationService.NormalizeDiscNumberingAsync(
                pr.Inner.ProductCatalogNo, Environment.UserName);
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
            // 論理削除した直後に残ったディスク群の組内番号を整える（2 枚→1 枚で残る側を NULL に戻す等）。
            // products.disc_count も同時に整合させる。商品が選択されていない経路では何もしない。
            string? productCatalogNo = d.ProductCatalogNo;
            await _discsRepo.SoftDeleteAsync(d.CatalogNo, Environment.UserName);
            if (!string.IsNullOrWhiteSpace(productCatalogNo))
            {
                await _registrationService.NormalizeDiscNumberingAsync(
                    productCatalogNo, Environment.UserName);
            }
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

    // ヘルパ

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>商品グリッド表示用の翻訳済みラッパ。元 Product と商品種別マスタから、表示用の文字列を組み立てて保持する。</summary>
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