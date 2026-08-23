#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.AmazonPaApi;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 商品名から Creators API SearchItems で 2 系統を並列検索し、候補から ASIN と画像 URL を選んで
/// 呼び出し側に返すダイアログ。左右どちらの系統を何で検索するか（音楽の CD / デジタル、
/// 書籍の紙 / Kindle）は <see cref="AmazonSearchMode"/> が決め、本ダイアログは骨格だけを持つ。
/// <para>
/// 表示するすべての画像は <c>m.media-amazon.com</c> 系の URL から都度 HTTP 取得し、
/// 表示完了後は <see cref="ImageList"/> のメモリ上にだけ保持される（ローカル永続化はしない）。
/// 選択結果として返す URL も Amazon CDN の文字列そのままで、規約上の「ホットリンク運用」を遵守する。
/// </para>
/// 採用する画像は <see cref="AmazonSearchMode.PreferRightForCover"/> が指す側の選択を優先し、
/// 無ければもう一方から採って <see cref="SelectedCoverImageUrl"/> /
/// <see cref="SelectedCoverImageSource"/> に格納される。
/// </summary>
public partial class AmazonProductSearchDialog : Form
{
    private readonly PaApiClient _paApi;
    private readonly string _initialKeyword;

    /// <summary>検索対象カテゴリと表示文言の設定。既定は音楽商品（CD / デジタル）。</summary>
    private readonly AmazonSearchMode _mode;

    // 画像のサムネ取得用に dialog 単位で共有する HttpClient。Dispose で破棄。
    // Amazon CDN（m.media-amazon.com）はデフォルトの .NET HttpClient UA を弾くことがあるため、
    // 一般的なブラウザ UA を載せておく（画像配信のホットリンクは規約上許容されている）。
    private readonly HttpClient _imageHttp = CreateImageHttpClient();

    private static HttpClient CreateImageHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        return http;
    }

    // 検索結果のキャッシュ（タグ経由でも持つが、選択 ASIN 解決のために辞書側も保持）。
    private readonly Dictionary<string, PaItem> _leftResultsByAsin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PaItem> _rightResultsByAsin = new(StringComparer.Ordinal);

    // 確定済みの選択結果（OK 押下時に呼び出し側が読む）。
    /// <summary>左系統で選択された ASIN（未選択時は空文字）。音楽なら CD、書籍なら紙。</summary>
    public string SelectedLeftAsin { get; private set; } = "";
    /// <summary>右系統で選択された ASIN（未選択時は空文字）。音楽ならデジタル、書籍なら Kindle。</summary>
    public string SelectedRightAsin { get; private set; } = "";
    /// <summary>採用する画像 URL（モードの優先側を先に見て、無ければもう一方。両方未選択なら null）。</summary>
    public string? SelectedCoverImageUrl { get; private set; }
    /// <summary>採用する画像の取得元コード（代表）。モードが与える値（<c>amazon_cd</c> / <c>amazon_digital</c> / <c>amazon_print</c> / <c>amazon_kindle</c>）。未選択時は null。</summary>
    public string? SelectedCoverImageSource { get; private set; }
    /// <summary>左系統で選択した商品の画像 URL（左側の列に保存する用。未選択／画像なしは null）。</summary>
    public string? SelectedLeftImageUrl { get; private set; }
    /// <summary>右系統で選択した商品の画像 URL（右側の列に保存する用。未選択／画像なしは null）。</summary>
    public string? SelectedRightImageUrl { get; private set; }

    // Creators API 失敗時のレスポンス本文を含む最新の長文エラーメッセージ。
    // lblStatus クリックで MessageBox に展開してユーザに見せる（自動でクリップボードにもコピー）。
    // 403 Forbidden の原因切り分け（アソシエイト売上要件未達・キー誤り・PartnerTag 不整合 等）には
    // 短い ex.Message ではなく Amazon が返す詳細 JSON が必須になるため、本フィールドで保持する。
    private string? _lastErrorDetail;

    // 直近検索の成功時パース結果ダンプ。lblStatus クリックで MessageBox に出す。
    // 画像 URL がパースで拾えているか / API が画像 URL を返していないか の切り分けに使う。
    private string? _lastDiagnosticsDetail;

    /// <summary><see cref="AmazonProductSearchDialog"/> の新しいインスタンスを生成する。</summary>
    /// <param name="paApi">Creators API クライアント（呼び出し側が App.config から構築済みのもの）。</param>
    /// <param name="initialKeyword">初期検索キーワード（商品名など）。</param>
    /// <param name="mode">検索対象カテゴリと表示文言。null なら音楽商品（CD / デジタル）。</param>
    public AmazonProductSearchDialog(PaApiClient paApi, string initialKeyword, AmazonSearchMode? mode = null)
    {
        _paApi = paApi ?? throw new ArgumentNullException(nameof(paApi));
        _initialKeyword = initialKeyword ?? "";
        _mode = mode ?? AmazonSearchMode.Music();

        InitializeComponent();

        // 検索対象と文言はモード側が決める。Designer は左右 2 ペインの骨格だけを組む。
        lblLeftHeader.Text = _mode.LeftHeader;
        lblRightHeader.Text = _mode.RightHeader;
        lblStatus.Text = $"（検索キーワードを入力して「検索」を押してください。{_mode.LeftShortLabel} / {_mode.RightShortLabel} 両系統を並列に検索します）";

        txtKeyword.Text = _initialKeyword;
        btnSearch.Click += async (_, __) => await DoSearchAsync();
        btnOk.Click += (_, __) => { ConfirmSelection(); DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

        // ListView の選択変更で「選択中」ラベルを更新する。
        lvLeft.SelectedIndexChanged += (_, __) => OnSideSelectionChanged(lvLeft, lblLeftSelected, _mode.LeftShortLabel);
        lvRight.SelectedIndexChanged += (_, __) => OnSideSelectionChanged(lvRight, lblRightSelected, _mode.RightShortLabel);

        // 起動直後に 1 回検索を投げる（初期キーワードがあれば）。
        Load += async (_, __) =>
        {
            if (!string.IsNullOrWhiteSpace(_initialKeyword))
                await DoSearchAsync();
        };

        FormClosed += (_, __) => _imageHttp.Dispose();

        // ステータスバーをクリックすると、最新の Creators API エラー本文（HTTP ステータス + Amazon が返す JSON）を
        // MessageBox で全文表示する。同時にクリップボードへ自動コピーして、トラブル時のサポート連絡に貼り付け
        // やすくする。エラーが未発生（_lastErrorDetail==null）のときはクリックしても何も起きない。
        lblStatus.Cursor = Cursors.Hand;
        lblStatus.Click += (_, __) =>
        {
            // エラー詳細があればそれを優先表示。無ければ成功時の診断ダンプ（パース結果一覧）を出す。
            string? text = !string.IsNullOrEmpty(_lastErrorDetail) ? _lastErrorDetail : _lastDiagnosticsDetail;
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { /* クリップボード失敗は致命的でないため無視 */ }
            MessageBox.Show(this,
                text,
                "Creators API 詳細（自動でクリップボードへコピー済み）",
                MessageBoxButtons.OK,
                string.IsNullOrEmpty(_lastErrorDetail) ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        };
    }

    /// <summary>検索を Creators API に投げ、左右の ListView に結果を流し込む。 Creators API のレート制限（1 TPS）順守のため、左 → 右の 2 リクエスト間に 1100ms スリープを挟む。</summary>
    private async Task DoSearchAsync()
    {
        string kw = txtKeyword.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(kw))
        {
            MessageBox.Show(this, "検索キーワードを入力してください。", "情報",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        btnSearch.Enabled = false;
        lblStatus.Text = "検索中…";
        try
        {
            // 結果クリア
            _leftResultsByAsin.Clear();
            _rightResultsByAsin.Clear();
            lvLeft.Items.Clear();
            lvRight.Items.Clear();
            imgList.Images.Clear();

            // 1) 左系統（モードが指定する SearchIndex）
            IReadOnlyList<PaItem> leftItems;
            try
            {
                leftItems = await _paApi.SearchItemsAsync(kw, _mode.LeftIndex, itemCount: 10, CancellationToken.None, _mode.ResourceSet);
            }
            catch (Exception ex)
            {
                leftItems = Array.Empty<PaItem>();
                // PaApiClient は ex.Message に HTTP ステータスと Amazon が返す JSON 本文を改行区切りで載せる。
                // ステータスバーは画面が狭いので 1 行（ex.Message の最初の改行まで）に圧縮し、全文は
                // _lastErrorDetail に退避してクリック時に MessageBox 展開する。
                _lastErrorDetail = ex.ToString();
                lblStatus.Text = $"{_mode.LeftShortLabel} 検索失敗（クリックで詳細）: " + FirstLine(ex.Message);
                lblStatus.ForeColor = Color.Firebrick;
            }
            await Task.Delay(1100);

            // 2) 右系統（モードが指定する SearchIndex）
            IReadOnlyList<PaItem> rightItems;
            try
            {
                rightItems = await _paApi.SearchItemsAsync(kw, _mode.RightIndex, itemCount: 10, CancellationToken.None, _mode.ResourceSet);
            }
            catch (Exception ex)
            {
                rightItems = Array.Empty<PaItem>();
                // 既に左系統で _lastErrorDetail が埋まっている場合は、最新のエラー（右系統）で上書きする。
                // ユーザがクリックで詳細を呼び出すときに見たいのは「最後に起きたエラー」の本文。
                _lastErrorDetail = ex.ToString();
                lblStatus.Text = $"{_mode.RightShortLabel} 検索失敗（クリックで詳細）: " + FirstLine(ex.Message);
                lblStatus.ForeColor = Color.Firebrick;
            }

            // ListView に流し込む（画像は後追いで取得）。
            // 画像 URL を持つアイテム数を診断のため数えてステータスバーに出す。
            int leftWithImage = leftItems.Count(x => !string.IsNullOrWhiteSpace(x.LargeImageUrl) || !string.IsNullOrWhiteSpace(x.MediumImageUrl));
            int rightWithImage = rightItems.Count(x => !string.IsNullOrWhiteSpace(x.LargeImageUrl) || !string.IsNullOrWhiteSpace(x.MediumImageUrl));

            // 診断ダンプ作成（lblStatus クリックで詳細を見るための材料）。
            // 各アイテムの ASIN / Title / MediumImageUrl / LargeImageUrl を 1 行ずつ並べる。
            _lastDiagnosticsDetail = BuildDiagnosticsDump(leftItems, rightItems);

            await PopulateAsync(lvLeft, leftItems, _leftResultsByAsin);
            await PopulateAsync(lvRight, rightItems, _rightResultsByAsin);

            // 両方とも 1 件以上取れたら正常時の見た目に戻す（赤字解除＋詳細クリア）。
            // 片方でも失敗していれば、その失敗時に設定した赤字＋詳細クリック表示をそのまま保持する。
            if (leftItems.Count > 0 || rightItems.Count > 0)
            {
                if (leftItems.Count > 0 && rightItems.Count > 0)
                {
                    _lastErrorDetail = null;
                    lblStatus.ForeColor = SystemColors.ControlText;
                }
                lblStatus.Text = $"検索完了: {_mode.LeftShortLabel}={leftItems.Count} 件(画像URL={leftWithImage}) / {_mode.RightShortLabel}={rightItems.Count} 件(画像URL={rightWithImage})";
            }
        }
        finally
        {
            btnSearch.Enabled = true;
        }
    }

    /// <summary>例外メッセージの最初の 1 行だけを返すヘルパ。 PaApiClient は HTTP ステータス行 + Amazon 詳細 JSON を改行で連結して載せてくるため、 ステータスバー（狭い）には最初の 1 行だけを出して、フル本文は <c>_lastErrorDetail</c> 経由で 後から MessageBox に展開する。</summary>
    private static string FirstLine(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        int nl = s.IndexOfAny(new[] { '\r', '\n' });
        return nl < 0 ? s : s.Substring(0, nl);
    }

    /// <summary>検索結果の診断ダンプを組み立てる。 各アイテムの ASIN / タイトル / 価格表示 / 中・大画像 URL を 1 アイテム 5 行で並べ、 lblStatus クリック時に MessageBox で表示してパースの可視化に使う。</summary>
    private string BuildDiagnosticsDump(IReadOnlyList<PaItem> leftItems, IReadOnlyList<PaItem> rightItems)
    {
        var sb = new System.Text.StringBuilder();
        void Dump(string side, IReadOnlyList<PaItem> items)
        {
            sb.Append("====== ").Append(side).Append(" (").Append(items.Count).AppendLine(" 件) ======");
            if (items.Count == 0)
            {
                sb.AppendLine("(該当なし)");
                return;
            }
            int i = 0;
            foreach (var it in items)
            {
                sb.Append('[').Append(++i).Append("] ASIN=").AppendLine(it.Asin);
                sb.Append("    Title=").AppendLine(it.Title ?? "");
                sb.Append("    Price=").AppendLine(it.PriceDisplay ?? "(none)");
                sb.Append("    Medium=").AppendLine(string.IsNullOrEmpty(it.MediumImageUrl) ? "(none)" : it.MediumImageUrl);
                sb.Append("    Large=").AppendLine(string.IsNullOrEmpty(it.LargeImageUrl) ? "(none)" : it.LargeImageUrl);
            }
        }
        Dump(_mode.LeftHeader, leftItems);
        sb.AppendLine();
        Dump(_mode.RightHeader, rightItems);
        return sb.ToString();
    }

    /// <summary>
    /// 検索結果を ListView に流し込み、各項目のサムネを HTTP で取得して ImageList に登録する。
    /// 画像取得失敗時はアイコンなしで項目だけ出す（操作は引き続き可能）。
    /// </summary>
    private async Task PopulateAsync(ListView lv, IReadOnlyList<PaItem> items, Dictionary<string, PaItem> map)
    {
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Asin)) continue;
            map[it.Asin] = it;

            // 行表示は「タイトル / 価格・発売日 / ASIN」の 3 段（Tile ビュー）。
            var lvi = new ListViewItem(string.IsNullOrWhiteSpace(it.Title) ? it.Asin : it.Title)
            {
                Tag = it.Asin
            };
            lvi.SubItems.Add(
                (it.PriceDisplay ?? "") + (string.IsNullOrWhiteSpace(it.ReleaseDate) ? "" : "  /  " + it.ReleaseDate));
            lvi.SubItems.Add("ASIN: " + it.Asin);
            lv.Items.Add(lvi);
        }

        // 画像取得は並列で。失敗しても他項目に影響しないよう個別 try-catch。
        // ImageList を 128x128 で持っているため、解像度の高い LargeImageUrl を優先採用し、
        // 取得できないときだけ MediumImageUrl にフォールバックする。
        var tasks = new List<Task>();
        foreach (ListViewItem lvi in lv.Items)
        {
            if (lvi.Tag is not string asin) continue;
            if (!map.TryGetValue(asin, out var it)) continue;
            string? url = !string.IsNullOrWhiteSpace(it.LargeImageUrl)
                ? it.LargeImageUrl
                : it.MediumImageUrl;
            if (string.IsNullOrWhiteSpace(url)) continue;
            tasks.Add(Task.Run(async () =>
            {
                MemoryStream? ms = null;
                Image? img = null;
                try
                {
                    var bytes = await _imageHttp.GetByteArrayAsync(url!);
                    // Image.FromStream は MemoryStream の生存を要求するため、ここでは using しない。
                    // 同様に Image 本体も BeginInvoke 経由だと ImageList.Add 前に Dispose されてしまうため、
                    // 同期 Invoke で UI スレッドに渡し切り、Add 完了後に finally で破棄する。
                    // ImageList.Images.Add(string, Image) は内部で bitmap データをコピーするため、
                    // Add 後に元 Image を破棄しても ImageList 側のサムネは保持される。
                    ms = new MemoryStream(bytes);
                    img = Image.FromStream(ms);

                    if (IsHandleCreated)
                    {
                        Invoke(new Action(() =>
                        {
                            try
                            {
                                if (!imgList.Images.ContainsKey(asin))
                                    imgList.Images.Add(asin, img);
                                lvi.ImageKey = asin;
                                lv.RedrawItems(lvi.Index, lvi.Index, false);
                            }
                            catch
                            {
                                // 描画系の競合は無視（画像なし表示にフォールバック）。
                            }
                        }));
                    }
                }
                catch
                {
                    // 画像取得失敗時はサムネなしで表示継続。
                }
                finally
                {
                    img?.Dispose();
                    ms?.Dispose();
                }
            }));
        }
        // 画像取得は同期的に await しても良いが、検索結果の表示自体は先に出したいため、
        // ここでは Promise.all 的にバックグラウンド完了させて UI 操作はすぐ開放する。
        await Task.Yield();
        _ = Task.WhenAll(tasks);
    }

    /// <summary>左右各 ListView の選択が変わったときに「選択中」ラベルを更新する。</summary>
    private void OnSideSelectionChanged(ListView lv, Label lbl, string sideName)
    {
        if (lv.SelectedItems.Count == 0)
        {
            lbl.Text = "選択中: なし";
            return;
        }
        var lvi = lv.SelectedItems[0];
        if (lvi.Tag is string asin)
        {
            lbl.Text = $"選択中 [{sideName}]: {asin}  / {lvi.Text}";
        }
    }

    /// <summary>OK 押下時に、左右の現在選択を <see cref="SelectedLeftAsin"/> 等のプロパティに転記する。 画像はデジタル側選択を優先、なければ CD 側、両方未選択なら空文字／null のままにする。 デジタル（Amazon Music）のジャケットは事業者アップの正規画像が確実な一方、 CD（特に廃盤）は素人写真が出品画像として載るリスクがあるためデジタルを優先する。</summary>
    private void ConfirmSelection()
    {
        // 左系統の ASIN と画像 URL を転記。
        if (lvLeft.SelectedItems.Count > 0 && lvLeft.SelectedItems[0].Tag is string leftAsin)
        {
            SelectedLeftAsin = leftAsin;
            if (_leftResultsByAsin.TryGetValue(leftAsin, out var leftItem)
                && !string.IsNullOrWhiteSpace(leftItem.LargeImageUrl))
            {
                SelectedLeftImageUrl = leftItem.LargeImageUrl;
            }
        }

        // 右系統の ASIN と画像 URL を転記。
        if (lvRight.SelectedItems.Count > 0 && lvRight.SelectedItems[0].Tag is string rightAsin)
        {
            SelectedRightAsin = rightAsin;
            if (_rightResultsByAsin.TryGetValue(rightAsin, out var rightItem)
                && !string.IsNullOrWhiteSpace(rightItem.LargeImageUrl))
            {
                SelectedRightImageUrl = rightItem.LargeImageUrl;
            }
        }

        // 代表（表示採用）はモードの優先側から。両系統の画像 URL は別途両列に保存する。
        string? firstUrl  = _mode.PreferRightForCover ? SelectedRightImageUrl : SelectedLeftImageUrl;
        string  firstCode = _mode.PreferRightForCover ? _mode.RightSourceCode : _mode.LeftSourceCode;
        string? otherUrl  = _mode.PreferRightForCover ? SelectedLeftImageUrl : SelectedRightImageUrl;
        string  otherCode = _mode.PreferRightForCover ? _mode.LeftSourceCode : _mode.RightSourceCode;

        if (!string.IsNullOrEmpty(firstUrl))
        {
            SelectedCoverImageUrl = firstUrl;
            SelectedCoverImageSource = firstCode;
        }
        else if (!string.IsNullOrEmpty(otherUrl))
        {
            SelectedCoverImageUrl = otherUrl;
            SelectedCoverImageSource = otherCode;
        }
    }
}
