using System.Data;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Dialogs;

/// <summary>
/// 既存商品に追加ディスクとして登録するための商品検索・選択ダイアログ（v1.1.3 追加）。
/// <para>
/// CDAnalyzer / BDAnalyzer の <see cref="DiscMatchDialog"/> で「既存商品に追加ディスクとして登録」
/// を選んだ後に開かれる。商品をキーワードで検索し、選択商品の所属ディスクをプレビューしながら、
/// 新規ディスクの組内番号（<c>disc_no_in_set</c>）の採番値を確認・上書きする。
/// </para>
/// <para>
/// シリーズは既定で「既存ディスクから継承」（一覧の先頭ディスクの <c>series_id</c> を採用）。
/// 必要に応じて <see cref="cboSeriesOverride"/> で別シリーズに上書きできる。
/// </para>
/// <para>
/// 戻り値:
/// <list type="bullet">
///   <item><see cref="SelectedProduct"/>: 選択された商品（OK 時のみ非 null）</item>
///   <item><see cref="OverrideSeriesId"/>: シリーズコンボで選ばれた series_id（NULL は「(オールスターズ)」、未選択時は <see cref="InheritedSeriesId"/> と同値）</item>
///   <item><see cref="InheritedSeriesId"/>: 商品選択時に既存ディスクから推定されたシリーズ ID（参考表示用）</item>
/// </list>
/// 組内番号 (<c>disc_no_in_set</c>) はユーザーに選ばせず、登録時に呼び出し先の
/// <see cref="DiscRegistrationService.AttachDiscToExistingProductAsync"/> が品番順に自動再採番する。
/// </para>
/// </summary>
public partial class AttachToProductDialog : Form
{
    private readonly ProductsRepository _productsRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly SeriesRepository _seriesRepo;

    /// <summary>選択された商品（Cancel 時は null）。</summary>
    public Product? SelectedProduct { get; private set; }

    /// <summary>シリーズコンボで明示的に選ばれた series_id（NULL = オールスターズ）。</summary>
    public int? OverrideSeriesId { get; private set; }

    /// <summary>既存ディスクから推定されたシリーズ ID（参考表示用）。</summary>
    public int? InheritedSeriesId { get; private set; }

    // 直近の検索結果リスト（行選択時に Product インスタンスを引き当てるため保持）
    private IReadOnlyList<Product> _searchHits = Array.Empty<Product>();
    // 選択中商品の所属ディスク（プレビュー＆採番計算用）
    private IReadOnlyList<Disc> _selectedProductDiscs = Array.Empty<Disc>();
    // シリーズコンボの選択肢
    private List<SeriesItem> _seriesItems = new();

    /// <summary>
    /// <see cref="AttachToProductDialog"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="productsRepo">商品リポジトリ。</param>
    /// <param name="discsRepo">ディスクリポジトリ。</param>
    /// <param name="seriesRepo">シリーズリポジトリ。</param>
    public AttachToProductDialog(ProductsRepository productsRepo, DiscsRepository discsRepo, SeriesRepository seriesRepo)
    {
        _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();

        Load += async (_, __) => await InitAsync();
        btnSearch.Click += async (_, __) => await DoSearchAsync();
        txtKeyword.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await DoSearchAsync();
            }
        };
        gridProducts.SelectionChanged += async (_, __) => await OnProductSelectedAsync();
        btnAttach.Click += BtnAttach_Click;
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    /// <summary>シリーズコンボの初期化。先頭は「(継承)」を入れて、既存ディスク由来の値を採用する旨を示す。</summary>
    private async Task InitAsync()
    {
        try
        {
            var allSeries = await _seriesRepo.GetAllAsync();
            // 「(継承)」: -2 を sentinel に使う（NULL=オールスターズ -1 との衝突回避）
            _seriesItems = new List<SeriesItem>
            {
                new(-2, "(既存ディスクから継承)"),
                new(-1, "(オールスターズ)"),
            };
            foreach (var s in allSeries.OrderBy(x => x.StartDate))
            {
                // 編集系のため短縮名優先
                string name = !string.IsNullOrWhiteSpace(s.TitleShort) ? s.TitleShort! : s.Title;
                _seriesItems.Add(new SeriesItem(s.SeriesId, name));
            }
            cboSeriesOverride.DisplayMember = nameof(SeriesItem.Label);
            cboSeriesOverride.ValueMember = nameof(SeriesItem.Sentinel);
            cboSeriesOverride.DataSource = _seriesItems;
            cboSeriesOverride.SelectedIndex = 0; // 既定で「継承」
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "初期化エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>キーワード（品番 or タイトル部分一致）で商品を検索しグリッドへ流す。</summary>
    private async Task DoSearchAsync()
    {
        var kw = txtKeyword.Text?.Trim() ?? "";
        if (kw.Length == 0) return;
        try
        {
            // 既存リポジトリの SearchByTitleAsync は title / title_short / title_en 横断 LIKE で 200 件まで返す。
            // 品番だけでヒットさせたい場合は GetByCatalogNoAsync を併用してマージする。
            var byTitle = await _productsRepo.SearchByTitleAsync(kw);
            var hits = byTitle.ToList();
            if (!hits.Any(p => string.Equals(p.ProductCatalogNo, kw, StringComparison.OrdinalIgnoreCase)))
            {
                var byCatalog = await _productsRepo.GetByCatalogNoAsync(kw);
                if (byCatalog is not null) hits.Insert(0, byCatalog);
            }
            _searchHits = hits;
            BindProducts(hits);
            lblSearchInfo.Text = $"{hits.Count} 件";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "検索エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>商品リストを DataTable 経由でグリッドへバインドする。</summary>
    private void BindProducts(IReadOnlyList<Product> products)
    {
        var table = new DataTable();
        table.Columns.Add("ProductCatalogNo", typeof(string));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("ReleaseDate", typeof(string));
        table.Columns.Add("DiscCount", typeof(string));

        foreach (var p in products)
        {
            table.Rows.Add(
                p.ProductCatalogNo,
                p.Title ?? "(無題)",
                p.ReleaseDate.ToString("yyyy-MM-dd"),
                p.DiscCount.ToString());
        }
        gridProducts.DataSource = table;
        gridProducts.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        gridProducts.Columns[0].HeaderText = "代表品番";
        gridProducts.Columns[1].HeaderText = "タイトル";
        gridProducts.Columns[2].HeaderText = "発売日";
        gridProducts.Columns[3].HeaderText = "枚数";
    }

    /// <summary>商品選択時、所属ディスクをプレビューし、組内番号の既定値とシリーズ継承候補を更新する。</summary>
    private async Task OnProductSelectedAsync()
    {
        if (gridProducts.SelectedRows.Count == 0)
        {
            _selectedProductDiscs = Array.Empty<Disc>();
            BindExistingDiscs(_selectedProductDiscs);
            InheritedSeriesId = null;
            UpdateSeriesNoteLabel();
            return;
        }
        var catalogNo = gridProducts.SelectedRows[0].Cells["ProductCatalogNo"].Value?.ToString() ?? "";
        var product = _searchHits.FirstOrDefault(p => string.Equals(p.ProductCatalogNo, catalogNo, StringComparison.Ordinal));
        if (product is null) return;

        try
        {
            _selectedProductDiscs = await _discsRepo.GetByProductCatalogNoAsync(catalogNo);
            BindExistingDiscs(_selectedProductDiscs);

            // v1.1.3: 組内番号は AttachDiscToExistingProductAsync 側で品番順に自動再採番するため、
            // ここでは何も計算しない（UI からも撤去済み）。

            // シリーズ継承候補: 1 枚目（disc_no_in_set 昇順、NULL は末尾 = リポジトリの並び）の series_id を採用
            InheritedSeriesId = _selectedProductDiscs.FirstOrDefault()?.SeriesId;
            UpdateSeriesNoteLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "ディスク取得エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>既存ディスクのプレビュー一覧を組み立てる。</summary>
    private void BindExistingDiscs(IReadOnlyList<Disc> discs)
    {
        var table = new DataTable();
        table.Columns.Add("DiscNoInSet", typeof(string));
        table.Columns.Add("CatalogNo", typeof(string));
        table.Columns.Add("Title", typeof(string));
        table.Columns.Add("MediaFormat", typeof(string));
        table.Columns.Add("SeriesId", typeof(string));

        foreach (var d in discs)
        {
            table.Rows.Add(
                d.DiscNoInSet?.ToString() ?? "—",
                d.CatalogNo,
                d.Title ?? d.CdTextAlbumTitle ?? d.VolumeLabel ?? "(無題)",
                d.MediaFormat ?? "—",
                d.SeriesId?.ToString() ?? "—");
        }
        gridDiscs.DataSource = table;
        gridDiscs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
        gridDiscs.Columns[0].HeaderText = "組内#";
        gridDiscs.Columns[1].HeaderText = "品番";
        gridDiscs.Columns[2].HeaderText = "タイトル";
        gridDiscs.Columns[3].HeaderText = "メディア";
        gridDiscs.Columns[4].HeaderText = "series_id";
    }

    /// <summary>シリーズ右側ラベルの注記を更新する（継承元の表示）。</summary>
    private void UpdateSeriesNoteLabel()
    {
        if (InheritedSeriesId is null)
        {
            lblSeriesNote.Text = "（継承元なし。オールスターズ扱い）";
        }
        else
        {
            var inheritedItem = _seriesItems.FirstOrDefault(i => i.Sentinel == InheritedSeriesId.Value);
            string label = inheritedItem is null ? $"id={InheritedSeriesId}" : inheritedItem.Label;
            lblSeriesNote.Text = $"（継承元: {label}）";
        }
    }

    /// <summary>「選択商品に追加して登録」押下: SelectedProduct と各種採番値を確定して閉じる。</summary>
    private void BtnAttach_Click(object? sender, EventArgs e)
    {
        if (gridProducts.SelectedRows.Count == 0)
        {
            MessageBox.Show(this, "商品を選択してください。", "選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var catalogNo = gridProducts.SelectedRows[0].Cells["ProductCatalogNo"].Value?.ToString() ?? "";
        var product = _searchHits.FirstOrDefault(p => string.Equals(p.ProductCatalogNo, catalogNo, StringComparison.Ordinal));
        if (product is null) return;

        SelectedProduct = product;
        // v1.1.3: 組内番号は呼び出し先 (DiscRegistrationService.AttachDiscToExistingProductAsync) で
        // 品番順に自動再採番される。ダイアログでは値を保持しない。

        // シリーズコンボの sentinel を解釈:
        //   -2: 継承（InheritedSeriesId をそのまま採用）
        //   -1: NULL（オールスターズ）
        //   その他: その値が series_id
        if (cboSeriesOverride.SelectedItem is SeriesItem sel)
        {
            OverrideSeriesId = sel.Sentinel switch
            {
                -2 => InheritedSeriesId,
                -1 => null,
                _ => sel.Sentinel
            };
        }
        else
        {
            OverrideSeriesId = InheritedSeriesId;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>シリーズコンボの内部項目。Sentinel: -2=継承, -1=NULL, それ以外=series_id。</summary>
    private sealed record SeriesItem(int Sentinel, string Label);
}
