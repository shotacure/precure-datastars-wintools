using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Dialogs;

/// <summary>
/// 新規商品作成ダイアログ：CDAnalyzer/BDAnalyzer の新規登録時、または
/// Catalog GUI の商品編集から呼び出される共通ダイアログ。
/// <para>
/// v1.1.1 よりシリーズ所属は Disc 側の属性となったため、本ダイアログではシリーズコンボを
/// 残しつつも、その値は <see cref="Result"/>（Product）ではなく
/// <see cref="SelectedSeriesId"/> プロパティに格納する。呼び出し側は作成するディスクに
/// <c>disc.SeriesId = pdlg.SelectedSeriesId;</c> として設定すること。
/// </para>
/// </summary>
public partial class NewProductDialog : Form
{
    private readonly ProductKindsRepository _kindsRepo;
    private readonly SeriesRepository _seriesRepo;

    /// <summary>作成された商品（Cancel 時は null）。product_catalog_no は呼び出し側で disc.CatalogNo からセットされる想定。</summary>
    public Product? Result { get; private set; }

    /// <summary>
    /// 選択されたシリーズ ID（Cancel 時は未設定）。OK 時のみ値が入り、NULL はオールスターズ扱い。
    /// v1.1.1 以降、本プロパティを読んで disc.SeriesId に設定するのが呼び出し側の責務。
    /// </summary>
    public int? SelectedSeriesId { get; private set; }

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

        // v1.1.5: 発売元・販売元の既定値。
        // 該当しないリリースに当たった場合はユーザがその場で書き換える運用。
        txtManufacturer.Text = "MARV";
        txtDistributor.Text = "SMS";

        // v1.1.5: 税抜価格 / 発売日 → 税込価格を自動計算する連動を仕掛ける。
        // 税率はリリース日に対応する日本の標準消費税率（消費税法改正の歴史を反映）。
        // 入力された税抜価格が空または不正値のときは税込価格欄も空に戻す。
        txtPriceEx.TextChanged += (_, __) => RecalculateIncTax();
        dtReleaseDate.ValueChanged += (_, __) => RecalculateIncTax();

        Load += async (_, __) => await InitCombosAsync();

        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    /// <summary>
    /// 入力中の税抜価格と発売日に対応する消費税率から税込価格を計算し、
    /// 読み取り専用の <c>txtPriceInc</c> に反映する。
    /// 端数処理は切り捨て（<see cref="Math.Floor(decimal)"/>）。
    /// </summary>
    private void RecalculateIncTax()
    {
        // 税抜が読めなければ税込側もクリア
        if (!int.TryParse(txtPriceEx.Text, out int priceEx) || priceEx < 0)
        {
            txtPriceInc.Text = "";
            return;
        }

        decimal rate = GetConsumptionTaxRate(dtReleaseDate.Value.Date);
        // 税込 = floor(税抜 × (1 + 税率))。decimal で計算してから int に丸める
        decimal raw = priceEx * (1m + rate);
        int priceInc = (int)Math.Floor(raw);
        txtPriceInc.Text = priceInc.ToString();
    }

    /// <summary>
    /// 指定日に有効だった日本の標準消費税率を返す。
    /// 軽減税率（食品・新聞）は本ダイアログの取り扱う商品（音楽・映像パッケージ）には該当しないため考慮しない。
    /// </summary>
    /// <remarks>
    /// 適用境界:
    /// <list type="bullet">
    ///   <item>1989-04-01: 消費税導入、3%</item>
    ///   <item>1997-04-01: 5% に引き上げ</item>
    ///   <item>2014-04-01: 8% に引き上げ</item>
    ///   <item>2019-10-01: 10% に引き上げ（現行）</item>
    /// </list>
    /// 1989-04-01 より前の発売日（消費税導入前のリリース）は税率 0% を返す。
    /// </remarks>
    /// <param name="releaseDate">商品の発売日（時刻部分は無視され、日付のみで判定される）。</param>
    /// <returns>消費税率（小数表現。例: 10% は <c>0.10m</c>）。</returns>
    private static decimal GetConsumptionTaxRate(DateTime releaseDate)
    {
        var d = releaseDate.Date;
        if (d >= new DateTime(2019, 10, 1)) return 0.10m;
        if (d >= new DateTime(2014, 4, 1))  return 0.08m;
        if (d >= new DateTime(1997, 4, 1))  return 0.05m;
        if (d >= new DateTime(1989, 4, 1))  return 0.03m;
        return 0.00m;
    }

    // シリーズ・商品種別コンボの初期化
    private async Task InitCombosAsync()
    {
        try
        {
            // シリーズ一覧（先頭にオールスターズ扱いの NULL 項目を追加）
            // v1.1.1: この値は Product ではなく作成されるディスク側に適用される
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

        // v1.1.5: 価格欄は TextBox に変更されたので int.TryParse で読み取る。
        // 空欄は NULL（価格不明扱い）、非数や負値は入力エラーとして停止する。
        // 税込側は自動計算による表示のため、空のときは「税抜が未入力 = 税込も NULL」と整合する。
        int? priceEx = null;
        if (!string.IsNullOrWhiteSpace(txtPriceEx.Text))
        {
            if (!int.TryParse(txtPriceEx.Text, out int v) || v < 0)
            {
                MessageBox.Show(this, "税抜価格は 0 以上の整数で入力してください。", "入力", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtPriceEx.Focus();
                return;
            }
            priceEx = v;
        }
        int? priceInc = null;
        if (!string.IsNullOrWhiteSpace(txtPriceInc.Text)
            && int.TryParse(txtPriceInc.Text, out int vinc) && vinc >= 0)
        {
            priceInc = vinc;
        }

        // v1.1.1: シリーズ ID は Product に載せず、SelectedSeriesId プロパティに分離して返す
        SelectedSeriesId = cboSeries.SelectedValue as int?;

        Result = new Product
        {
            Title = txtTitle.Text.Trim(),
            TitleShort = StringOrNull(txtTitleShort.Text),
            TitleEn = StringOrNull(txtTitleEn.Text),
            ProductKindCode = kindCode,
            ReleaseDate = dtReleaseDate.Value.Date,
            PriceExTax = priceEx,
            PriceIncTax = priceInc,
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
