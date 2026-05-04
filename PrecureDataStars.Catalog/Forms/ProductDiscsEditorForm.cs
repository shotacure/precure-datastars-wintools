#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
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

        // v1.1.4: レイアウト追従ロジック（v1.1.4 改で動的レイアウトに刷新）。
        // - splitMain は FixedPanel = Panel2 で下段（ディスクエリア）を 400 px に固定する。
        //   Form.Load で Height - 400 - SplitterWidth を SplitterDistance に設定し、以後は自動維持。
        // - splitProduct / splitDisc は左右 60:40 の比率を SizeChanged で都度 (Width × 0.6) に再計算。
        // - 詳細パネル内の入力欄／ボタンは Anchor を使わず、LayoutProductDetailPanel /
        //   LayoutDiscDetailPanel で都度 Width と Location を明示計算して再配置する。
        //   AutoScroll = true の Panel における Anchor = Right の WinForms 既知のレイアウト循環バグ
        //   （DisplayRectangle.Right を参照する Anchor=Right コントロールと AutoScrollMinSize 計算の
        //    相互再計算でフォームが右に伸び続ける現象）を完全に回避するため、Anchor を使わない方式とした。
        Load += (_, __) => InitializeLayout();
        splitProduct.SizeChanged += (_, __) => Apply60To40(splitProduct);
        splitDisc.SizeChanged += (_, __) => Apply60To40(splitDisc);
        pnlProductDetail.SizeChanged += (_, __) => LayoutProductDetailPanel();
        pnlDiscDetail.SizeChanged += (_, __) => LayoutDiscDetailPanel();
    }

    /// <summary>
    /// 起動直後のレイアウト初期化。
    /// スプリッタを所定の比率/固定値に整え、詳細パネルの動的レイアウトも一度走らせる。
    /// </summary>
    private void InitializeLayout()
    {
        // splitMain の SplitterDistance は「Panel1 の高さ」を意味する。
        // Panel2 を 400 px にしたいので、SplitterDistance = (splitMain.Height - 400 - SplitterWidth)。
        const int bottomFixedHeight = 400;
        int top = splitMain.Height - bottomFixedHeight - splitMain.SplitterWidth;
        if (top < splitMain.Panel1MinSize) top = splitMain.Panel1MinSize;
        splitMain.SplitterDistance = top;

        Apply60To40(splitProduct);
        Apply60To40(splitDisc);

        LayoutProductDetailPanel();
        LayoutDiscDetailPanel();
    }

    /// <summary>
    /// 左右分割の SplitContainer を 60:40（左 60% / 右 40%）に整える。
    /// 利用可能幅がスプリッタ幅以下の極端な状況ではクランプして例外を防ぐ。
    /// </summary>
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
    /// Anchor = Right を使わず明示計算する方式（AutoScroll = true との循環バグを回避）。
    /// 余白設計: 左側 18 px（ラベル左端）、右側 10 px（ボタン右端からパネル端まで）。
    /// </summary>
    private void LayoutProductDetailPanel()
    {
        int innerW = pnlProductDetail.ClientSize.Width;
        if (innerW <= 0) return;

        // ボタン列はパネル右端から「ボタン幅 80 + 余白 10」の位置に並べる
        const int btnW = 80;
        const int btnRightMargin = 10;
        int btnX = innerW - btnW - btnRightMargin;

        // 入力欄の右端は、ボタン左端の左にさらに 16 px 余白を取った位置
        const int labelW = 100;
        const int leftMargin = 22;       // ラベル(18) + 4 px ギャップ + ラベル幅 = 入力欄左端
        const int fieldGap = 16;         // 入力欄右端とボタン左端の余白
        int fieldX = leftMargin + labelW;
        int fieldEndX = btnX - fieldGap;
        int fieldW = fieldEndX - fieldX;
        // 最小幅を保証（極端な縮小時にゼロ以下にならないように）
        if (fieldW < 100) fieldW = 100;

        // 通常入力欄は全部同じ幅で配置。Y 座標は Designer で確定済みなのでそのまま。
        // 税込価格行の numPriceInc は固定 170 px、btnAutoTax はその直後、という特例だけ別処理。
        Control[] generalFields =
        {
            txtProductCatalogNo, txtTitle, txtTitleShort, txtTitleEn,
            cboKind, dtRelease, numPriceEx,
            // numPriceInc は特例（下で別処理）
            numDiscCount, txtManufacturer, txtDistributor, txtLabel,
            txtAsin, txtApple, txtSpotify, txtNotes
        };
        foreach (var c in generalFields)
        {
            c.Width = fieldW;
            c.Location = new Point(fieldX, c.Location.Y);
        }

        // 税込価格行の特例: numPriceInc は固定幅 170 px、btnAutoTax はその右隣
        const int priceIncW = 170;
        numPriceInc.Width = priceIncW;
        numPriceInc.Location = new Point(fieldX, numPriceInc.Location.Y);
        btnAutoTax.Location = new Point(numPriceInc.Right + 6, numPriceInc.Location.Y);

        // ボタン列（新規・保存・削除）
        btnProductNew.Location = new Point(btnX, 8);
        btnProductSave.Location = new Point(btnX, 40);
        btnProductDelete.Location = new Point(btnX, 72);
    }

    /// <summary>
    /// ディスク詳細パネル内の入力欄とボタン群を、現在のパネル幅に合わせて動的に再配置する。
    /// 余白設計は LayoutProductDetailPanel と同じ。
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
            // v1.1.5: 1 枚物商品でディスク詳細フォームが空のままになる不具合を回避するため、
            //         グリッド再バインドと先頭行選択・詳細フォーム反映をヘルパに集約して
            //         SelectionChanged の発火タイミングに依存しない経路に揃える。
            RebindDiscGrid();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// <see cref="gridDiscs"/> を <see cref="_discs"/> で再バインドし、
    /// 先頭行を明示選択してディスク詳細フォームに反映する。
    /// <para>
    /// DataGridView の <c>SelectionChanged</c> イベントは、新旧 DataSource の現在行 index が
    /// どちらも 0 のまま（特に行数が 0→1, 1→1, N→1 のケース）だと発火しない場合がある。
    /// SelectionChanged 任せの実装では、ディスクが 1 枚しか無い商品を選択した際に
    /// </para>
    /// <list type="number">
    ///   <item><c>OnDiscSelected</c> が呼ばれない</item>
    ///   <item>ユーザはディスクリストに別行が無いので再選択トリガを引けず、
    ///         ディスク詳細フォームが永久に空のまま</item>
    /// </list>
    /// <para>
    /// という症状になる。本ヘルパは <c>ClearSelection</c> + <c>Selected = true</c> +
    /// <c>CurrentCell</c> 設定で先頭行を明示的に選択状態にしたうえで、
    /// <see cref="OnDiscSelected"/> を直接呼び出すことで、行数 0/1/N のすべてで
    /// 一貫した詳細フォーム更新を保証する。
    /// </para>
    /// <para>
    /// 副次効果として、保存・削除直後の自動再バインド経路でも先頭ディスクが
    /// 詳細フォームに自動表示されるようになる（従来は空欄のままでユーザが行を
    /// 再クリックする必要があった）。
    /// </para>
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
        // 0 件のときは OnDiscSelected が CurrentRow == null を見て ClearDiscForm を呼ぶので、
        // この経路に集約することで「DataSource 再代入後の詳細フォーム反映」を 1 箇所で完結させる。
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

            // DiscsRepository.UpsertAsync は INSERT ... ON DUPLICATE KEY UPDATE で全列を VALUES() で書き戻す。
            // 本フォームはタイトル・組内番号・種別・MCN・備考等のメタ情報のみを編集する画面であり、
            // CDAnalyzer / BDAnalyzer が読み取った物理情報や CD-Text 等は触れない。フォーム側で値を持っていない
            // 物理系カラムをそのまま UPSERT すると NULL で上書きされて消えてしまうため、既存レコードを
            // 引き直してフォーム編集対象外のフィールドを引き継ぐ。
            //
            // 引き継ぐフィールド（編集 UI を持たない列群）:
            //   - 物理尺/構造系     : TotalLengthFrames, TotalLengthMs, NumChapters
            //   - CD-Text           : CdTextAlbumTitle / Performer / Songwriter / Composer / Arranger / Message / DiscId / Genre
            //   - 計算済み識別子    : CddbDiscId, MusicbrainzDiscId
            //   - 最終読み取り日時  : LastReadAt
            // 既存レコードが無い（新規追加）ケースは保全対象が無いのでそのまま UPSERT に進む。
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
                // 監査列のうち CreatedBy は新規 INSERT のときだけ意味を持つ。既存レコードの初出ユーザーを
                // 守るため、本フローでは UpdatedBy のみを今回の操作者で上書きする運用に揃える。
                d.CreatedBy = existingDisc.CreatedBy ?? d.CreatedBy;
            }

            await _discsRepo.UpsertAsync(d);
            MessageBox.Show(this, $"ディスク [{d.CatalogNo}] を保存しました。");

            _discs = (await _discsRepo.GetByProductCatalogNoAsync(pr.Inner.ProductCatalogNo)).ToList();
            // v1.1.5: 1 枚物商品でディスク詳細フォームが空のままになる不具合の修正に併せ、
            //         保存後の再バインド経路もヘルパに集約する。副次効果として、保存後に
            //         先頭ディスクが詳細フォームに自動表示されるようになる。
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
                // v1.1.5: 削除後の再バインドもヘルパに統一。残ったディスクの先頭が
                //         自動で詳細フォームに反映される（残数 0 のときは ClearDiscForm 相当に倒れる）。
                RebindDiscGrid();
            }
            else
            {
                // 商品が選択されていない（通常の操作経路では到達しない）場合は、
                // 旧来通り詳細フォームのみクリアする。
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
