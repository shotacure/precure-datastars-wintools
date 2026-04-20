using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Dialogs;

/// <summary>
/// 新規商品作成ダイアログ：CDAnalyzer/BDAnalyzer の新規登録時、または
/// Catalog GUI の商品編集から呼び出される共通ダイアログ。
/// </summary>
public partial class NewProductDialog : Form
{
    private readonly ProductKindsRepository _kindsRepo;
    private readonly SeriesRepository _seriesRepo;

    /// <summary>作成された商品（Cancel 時は null）。product_catalog_no は呼び出し側で disc.CatalogNo からセットされる想定。</summary>
    public Product? Result { get; private set; }

    /// <summary>
    /// <see cref="NewProductDialog"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="kindsRepo">商品種別マスタリポジトリ。</param>
    /// <param name="seriesRepo">シリーズリポジトリ（NULL=オールスターズを含むコンボ用）。</param>
    /// <param name="initialTitle">初期タイトル（CD-Text アルバム名などから引き継ぐ）。</param>
    public NewProductDialog(ProductKindsRepository kindsRepo, SeriesRepository seriesRepo, string? initialTitle = null)
    {
        _kindsRepo = kindsRepo ?? throw new ArgumentNullException(nameof(kindsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(initialTitle))
            txtTitle.Text = initialTitle;

        // 発売日は本日を初期値とする
        dtReleaseDate.Value = DateTime.Today;
        // ディスク枚数 1 を初期値
        numDiscCount.Value = 1;

        Load += async (_, __) => await InitCombosAsync();

        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    // シリーズ・商品種別コンボの初期化
    private async Task InitCombosAsync()
    {
        try
        {
            // シリーズ一覧（先頭にオールスターズ扱いの NULL 項目を追加）
            var seriesAll = await _seriesRepo.GetAllAsync();
            var seriesItems = new List<SeriesItem>
            {
                new SeriesItem { SeriesId = null, Display = "(オールスターズ)" }
            };
            seriesItems.AddRange(seriesAll.Select(s => new SeriesItem
            {
                SeriesId = s.SeriesId,
                Display = $"[{s.SeriesId}] {s.TitleShort ?? s.Title}"
            }));
            cboSeries.DataSource = seriesItems;
            cboSeries.DisplayMember = nameof(SeriesItem.Display);
            cboSeries.ValueMember = nameof(SeriesItem.SeriesId);

            // 商品種別
            var kinds = await _kindsRepo.GetAllAsync();
            cboKind.DataSource = kinds.ToList();
            cboKind.DisplayMember = nameof(ProductKind.NameJa);
            cboKind.ValueMember = nameof(ProductKind.KindCode);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "マスタ取得エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // 決定：入力値を Product オブジェクトに詰めて戻す
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtTitle.Text))
        {
            MessageBox.Show(this, "タイトルを入力してください。", "入力", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (cboKind.SelectedValue is not string kindCode)
        {
            MessageBox.Show(this, "商品種別を選択してください。", "入力", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Result = new Product
        {
            Title = txtTitle.Text.Trim(),
            TitleShort = StringOrNull(txtTitleShort.Text),
            TitleEn = StringOrNull(txtTitleEn.Text),
            SeriesId = cboSeries.SelectedValue as int?,
            ProductKindCode = kindCode,
            ReleaseDate = dtReleaseDate.Value.Date,
            PriceExTax = (int)numPriceEx.Value == 0 ? null : (int)numPriceEx.Value,
            PriceIncTax = (int)numPriceInc.Value == 0 ? null : (int)numPriceInc.Value,
            DiscCount = (byte)numDiscCount.Value,
            Manufacturer = StringOrNull(txtManufacturer.Text),
            Distributor = StringOrNull(txtDistributor.Text),
            Label = StringOrNull(txtLabel.Text),
            AmazonAsin = StringOrNull(txtAsin.Text),
            AppleAlbumId = StringOrNull(txtAppleId.Text),
            SpotifyAlbumId = StringOrNull(txtSpotifyId.Text),
            Notes = StringOrNull(txtNotes.Text),
            CreatedBy = Environment.UserName,
            UpdatedBy = Environment.UserName
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    // 空白文字を NULL として扱うヘルパ
    private static string? StringOrNull(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // シリーズコンボ用の DTO
    private sealed class SeriesItem
    {
        public int? SeriesId { get; set; }
        public string Display { get; set; } = "";
    }
}
