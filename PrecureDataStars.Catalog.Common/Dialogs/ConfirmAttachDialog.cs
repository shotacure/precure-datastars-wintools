using System.Data;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Dialogs;

/// <summary>
/// 既存商品への追加ディスク登録の最終確認ダイアログ。
/// <para>
/// CDAnalyzer / BDAnalyzer の <see cref="DiscMatchDialog"/> で「選択したディスクの商品に追加」を選んだ後、
/// 商品名と所属ディスクの確認、シリーズの継承／上書きだけを担う簡易ダイアログ。
/// 商品検索の手順は <see cref="DiscMatchDialog"/> のグリッド選択でユーザーが既に済ませている前提のため、
/// このダイアログでは商品検索 UI を持たない。
/// </para>
/// <para>
/// 戻り値:
/// <list type="bullet">
///   <item><see cref="OverrideSeriesId"/>: シリーズコンボで選ばれた series_id（NULL = オールスターズ）。
///     継承選択時は <see cref="InheritedSeriesId"/> と同値</item>
///   <item><see cref="InheritedDiscTitle"/>: 既存ディスクから推定された継承タイトル候補（参考表示用）</item>
///   <item><see cref="SuggestedCatalogNo"/>: 商品配下の品番昇順末尾を +1 した次の品番候補</item>
/// </list>
/// </para>
/// </summary>
public partial class ConfirmAttachDialog : Form
{
    private readonly Product _product;
    private readonly IReadOnlyList<Disc> _existingDiscs;
    private readonly SeriesRepository _seriesRepo;

    /// <summary>シリーズコンボで明示的に選ばれた series_id（NULL = オールスターズ）。</summary>
    public int? OverrideSeriesId { get; private set; }

    /// <summary>既存ディスクから推定されたシリーズ ID（参考表示用）。</summary>
    public int? InheritedSeriesId { get; private set; }

    /// <summary>既存ディスクから継承するタイトル（先頭ディスクの非空 Title）。</summary>
    public string? InheritedDiscTitle { get; private set; }

    /// <summary>次の品番候補（品番昇順末尾 +1）。テキストボックスの初期値として使用される。</summary>
    public string? SuggestedCatalogNo { get; private set; }

    /// <summary>
    /// 「追加して登録」確定時にユーザーが入力した新ディスクの品番（
    /// 旧 PromptCatalogNo を吸収し、本ダイアログで完結させたためのプロパティ）。
    /// 空欄のままでは確定できない（<see cref="BtnAttach_Click"/> でブロック）。
    /// </summary>
    public string? CatalogNo { get; private set; }

    private List<SeriesItem> _seriesItems = new();

    /// <summary>
    /// <see cref="ConfirmAttachDialog"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="product">追加先となる既存商品。</param>
    /// <param name="existingDiscs">商品の所属ディスク群（事前取得済み）。</param>
    /// <param name="seriesRepo">シリーズリポジトリ。</param>
    public ConfirmAttachDialog(Product product, IReadOnlyList<Disc> existingDiscs, SeriesRepository seriesRepo)
    {
        _product = product ?? throw new ArgumentNullException(nameof(product));
        _existingDiscs = existingDiscs ?? throw new ArgumentNullException(nameof(existingDiscs));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();

        Load += async (_, __) => await InitAsync();
        // ダイアログ表示直後に品番テキストボックスへフォーカス＋全選択。
        // 候補値が入っている場合は Enter 一発（または桁修正）で確定できるようにする。
        Shown += (_, __) =>
        {
            if (!string.IsNullOrEmpty(txtCatalogNo.Text))
            {
                txtCatalogNo.Focus();
                txtCatalogNo.SelectAll();
            }
        };
        btnAttach.Click += BtnAttach_Click;
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

        // Enter キーは通常 AcceptButton にバインドされるため AcceptButton を btnAttach に。
        AcceptButton = btnAttach;
        CancelButton = btnCancel;
    }

    /// <summary>
    /// 起動時の初期化: 商品情報の表示、所属ディスクのバインド、シリーズコンボの構築、
    /// 継承タイトル / 次の品番候補の事前計算を行う。
    /// </summary>
    private async Task InitAsync()
    {
        try
        {
            // 商品情報のヘッダ表示
            lblProductCatalogNo.Text = _product.ProductCatalogNo;
            lblProductTitle.Text = _product.Title ?? "(無題)";
            lblProductReleaseDate.Text = _product.ReleaseDate.ToString("yyyy-MM-dd");
            lblProductDiscCount.Text = $"{_product.DiscCount} 枚";

            // 所属ディスクのプレビュー
            BindExistingDiscs(_existingDiscs);

            // 継承タイトル候補（先頭ディスクの非空 Title）
            string? firstTitle = _existingDiscs.FirstOrDefault()?.Title;
            InheritedDiscTitle = string.IsNullOrWhiteSpace(firstTitle) ? null : firstTitle;

            // 次の品番候補（品番昇順末尾 +1。プリキュア BD/DVD/CD は単純文字列ソートで自然順と一致）
            string? lastCatalogNo = _existingDiscs
                .Select(d => d.CatalogNo)
                .OrderBy(c => c, StringComparer.Ordinal)
                .LastOrDefault();
            SuggestedCatalogNo = IncrementCatalogNoSuffix(lastCatalogNo);

            // 候補をテキストボックスに流し込む。実際の全選択は後段の Shown イベント
            // （InitializeComponent → Load → Shown の順で発火する）で行う方が確実。
            // Load 段階で SelectAll しても表示後にカーソル位置が戻ってしまうケースがある。
            if (!string.IsNullOrEmpty(SuggestedCatalogNo))
            {
                txtCatalogNo.Text = SuggestedCatalogNo;
            }

            // 継承シリーズ候補（先頭ディスクの series_id）
            InheritedSeriesId = _existingDiscs.FirstOrDefault()?.SeriesId;

            // シリーズコンボ初期化
            var allSeries = await _seriesRepo.GetAllAsync();
            // sentinel: -2=継承（既定）, -1=NULL（オールスターズ）, それ以外=series_id
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

            UpdateSeriesNoteLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "初期化エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>所属ディスクのプレビュー一覧をバインドする。</summary>
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
            return;
        }
        var inheritedItem = _seriesItems.FirstOrDefault(i => i.Sentinel == InheritedSeriesId.Value);
        string label = inheritedItem is null ? $"id={InheritedSeriesId}" : inheritedItem.Label;
        lblSeriesNote.Text = $"（継承元: {label}）";
    }

    /// <summary>「追加して登録」押下: 品番入力チェック → シリーズ採用値を確定して閉じる。</summary>
    private void BtnAttach_Click(object? sender, EventArgs e)
    {
        // 品番は必須。空欄なら確定をブロックして TextBox にフォーカスを戻す。
        var catalogNoInput = txtCatalogNo.Text?.Trim() ?? "";
        if (catalogNoInput.Length == 0)
        {
            MessageBox.Show(this, "新ディスクの品番を入力してください。", "入力",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            txtCatalogNo.Focus();
            return;
        }
        CatalogNo = catalogNoInput;

        if (cboSeriesOverride.SelectedItem is SeriesItem sel)
        {
            OverrideSeriesId = sel.Sentinel switch
            {
                -2 => InheritedSeriesId, // 継承
                -1 => null,              // NULL（オールスターズ）
                _ => sel.Sentinel        // 任意の series_id
            };
        }
        else
        {
            OverrideSeriesId = InheritedSeriesId;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// 品番末尾の連続数字部を +1 した文字列を返す。
    /// <para>
    /// 例:
    /// <list type="bullet">
    ///   <item><c>KICA-1234</c> → <c>KICA-1235</c></item>
    ///   <item><c>KICA-9999</c> → <c>KICA-10000</c></item>
    ///   <item><c>BIBA-12345A</c> → <c>BIBA-12345A</c>（末尾が数字でないため変更しない）</item>
    ///   <item>NULL/空 → NULL</item>
    /// </list>
    /// 桁数を維持してゼロパディング（"007" → "008"）。桁が増える場合（999→1000 等）は素直にあふれる。
    /// </para>
    /// </summary>
    private static string? IncrementCatalogNoSuffix(string? catalogNo)
    {
        if (string.IsNullOrEmpty(catalogNo)) return null;

        int i = catalogNo.Length;
        while (i > 0 && char.IsDigit(catalogNo[i - 1])) i--;
        if (i == catalogNo.Length) return catalogNo;

        string prefix = catalogNo.Substring(0, i);
        string digits = catalogNo.Substring(i);

        if (!long.TryParse(digits, out long num)) return catalogNo;
        long next = num + 1;

        string nextStr = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (nextStr.Length < digits.Length)
        {
            nextStr = nextStr.PadLeft(digits.Length, '0');
        }
        return prefix + nextStr;
    }

    /// <summary>シリーズコンボの内部項目。Sentinel: -2=継承, -1=NULL, それ以外=series_id。</summary>
    private sealed record SeriesItem(int Sentinel, string Label);
}