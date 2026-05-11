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
/// <para>
/// v1.3.0 ブラッシュアップ stage 20 確定版で、旧フリーテキスト 3 行（manufacturer / label /
/// distributor）は撤去された。代わりに <see cref="ProductCompaniesRepository.GetDefaultLabelAsync"/>
/// / <see cref="ProductCompaniesRepository.GetDefaultDistributorAsync"/> で既定フラグ社を
/// 取得し、ReadOnly TextBox に表示する。実 ID はフィールドに保持して OK 時に
/// <see cref="Product.LabelProductCompanyId"/> / <see cref="Product.DistributorProductCompanyId"/>
/// にセットする。
/// </para>
/// <para>
/// 既定社を変更したい場合は、商品作成後に「商品・ディスク管理」フォームの picker で
/// 個別商品ごとに紐付け先を差し替える運用とする（複数商品に対する一括変更はマスタ側で既定
/// フラグを別社に立て直す手で対応）。
/// </para>
/// </summary>
public partial class NewProductDialog : Form
{
    private readonly ProductKindsRepository _kindsRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly ProductCompaniesRepository _productCompaniesRepo;

    /// <summary>作成された商品（Cancel 時は null）。product_catalog_no は呼び出し側で disc.CatalogNo からセットされる想定。</summary>
    public Product? Result { get; private set; }

    /// <summary>
    /// 選択されたシリーズ ID（Cancel 時は未設定）。OK 時のみ値が入り、NULL はオールスターズ扱い。
    /// v1.1.1 以降、本プロパティを読んで disc.SeriesId に設定するのが呼び出し側の責務。
    /// </summary>
    public int? SelectedSeriesId { get; private set; }

    // v1.3.0 stage20 確定版：マスタから取得した既定社 ID。Load 時に InitCombosAsync で埋めて、
    // BtnOk_Click で Result にコピーする。マスタ未登録なら null のまま（Product 側の FK は NULL）。
    private int? _defaultLabelId;
    private int? _defaultDistributorId;

    /// <summary>
    /// <see cref="NewProductDialog"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="kindsRepo">商品種別マスタリポジトリ。</param>
    /// <param name="seriesRepo">シリーズリポジトリ（NULL=オールスターズを含むコンボ用）。</param>
    /// <param name="productCompaniesRepo">商品社名マスタリポジトリ（既定社の取得に使う）。</param>
    /// <param name="initialTitle">初期タイトル（CD-Text アルバム名などから引き継ぐ）。</param>
    public NewProductDialog(
        ProductKindsRepository kindsRepo,
        SeriesRepository seriesRepo,
        ProductCompaniesRepository productCompaniesRepo,
        string? initialTitle = null)
    {
        _kindsRepo = kindsRepo ?? throw new ArgumentNullException(nameof(kindsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _productCompaniesRepo = productCompaniesRepo ?? throw new ArgumentNullException(nameof(productCompaniesRepo));

        InitializeComponent();

        if (!string.IsNullOrWhiteSpace(initialTitle))
            txtTitle.Text = initialTitle;

        // 発売日は本日を初期値とする
        dtReleaseDate.Value = DateTime.Today;
        // ディスク枚数 1 を初期値
        numDiscCount.Value = 1;

        // v1.1.5 → v1.3.0 stage20 確定版：旧 txtManufacturer / txtLabel / txtDistributor の既定値
        // 設定（"MARV" / "SMS"）は撤去。代わりに InitCombosAsync で product_companies の
        // is_default_label / is_default_distributor フラグから既定社を取得して表示する。

        // v1.1.5: 税抜価格 / 発売日 → 税込価格を自動計算する連動
        txtPriceEx.TextChanged += (_, __) => RecalculateIncTax();
        dtReleaseDate.ValueChanged += (_, __) => RecalculateIncTax();

        Load += async (_, __) => await InitCombosAsync();

        btnOk.Click += BtnOk_Click;
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    /// <summary>
    /// 入力中の税抜価格と発売日に対応する消費税率から税込価格を計算し、
    /// 読み取り専用の <c>txtPriceInc</c> に反映する。端数処理は切り捨て。
    /// </summary>
    private void RecalculateIncTax()
    {
        if (!int.TryParse(txtPriceEx.Text, out int priceEx) || priceEx < 0)
        {
            txtPriceInc.Text = "";
            return;
        }

        decimal rate = GetConsumptionTaxRate(dtReleaseDate.Value.Date);
        decimal raw = priceEx * (1m + rate);
        int priceInc = (int)Math.Floor(raw);
        txtPriceInc.Text = priceInc.ToString();
    }

    /// <summary>
    /// 指定日に有効だった日本の標準消費税率を返す。軽減税率は本ダイアログの取り扱う商品
    /// （音楽・映像パッケージ）には該当しないため考慮しない。
    /// </summary>
    private static decimal GetConsumptionTaxRate(DateTime releaseDate)
    {
        var d = releaseDate.Date;
        if (d >= new DateTime(2019, 10, 1)) return 0.10m;
        if (d >= new DateTime(2014, 4, 1))  return 0.08m;
        if (d >= new DateTime(1997, 4, 1))  return 0.05m;
        if (d >= new DateTime(1989, 4, 1))  return 0.03m;
        return 0.00m;
    }

    /// <summary>
    /// シリーズ・商品種別コンボの初期化に加え、v1.3.0 stage20 確定版で既定フラグ社の取得・表示も行う。
    /// 既定が未設定なら ReadOnly テキストに「(未設定)」を出し、内部 ID は null のまま。
    /// </summary>
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

            // v1.3.0 stage20 確定版：既定社 2 つを取得して表示・内部 ID に保持
            var defaultLabel = await _productCompaniesRepo.GetDefaultLabelAsync();
            _defaultLabelId = defaultLabel?.ProductCompanyId;
            txtDefaultLabel.Text = defaultLabel?.NameJa ?? "(未設定)";

            var defaultDistributor = await _productCompaniesRepo.GetDefaultDistributorAsync();
            _defaultDistributorId = defaultDistributor?.ProductCompanyId;
            txtDefaultDistributor.Text = defaultDistributor?.NameJa ?? "(未設定)";
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

        // v1.1.5: 価格欄は TextBox なので int.TryParse で読み取る
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

        // v1.3.0 stage20 確定版：旧フリーテキスト 3 行は撤去済み。社名マスタ ID は
        // InitCombosAsync で取得した既定値をそのままセット。
        // ユーザーが商品作成後に個別商品ごとに変更したければ、商品エディタの picker で差し替える。
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
            LabelProductCompanyId = _defaultLabelId,
            DistributorProductCompanyId = _defaultDistributorId,
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
