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
/// 商品名から PA-API SearchItems で CD（Music）／デジタル（DigitalMusic）の両系統を並列検索し、
/// 候補から ASIN と画像 URL を選んで呼び出し側に返すダイアログ。
/// <para>
/// 表示するすべての画像は <c>m.media-amazon.com</c> 系の URL から都度 HTTP 取得し、
/// 表示完了後は <see cref="ImageList"/> のメモリ上にだけ保持される（ローカル永続化はしない）。
/// 選択結果として返す URL も Amazon CDN の文字列そのままで、規約上の「ホットリンク運用」を遵守する。
/// </para>
/// 採用する画像は「CD 側の選択を優先、無ければデジタル側」のロジックで決まり、
/// <see cref="SelectedCoverImageUrl"/> / <see cref="SelectedCoverImageSource"/> に格納される。
/// </summary>
public partial class AmazonProductSearchDialog : Form
{
    private readonly PaApiClient _paApi;
    private readonly string _initialKeyword;

    // 画像のサムネ取得用に dialog 単位で共有する HttpClient。Dispose で破棄。
    private readonly HttpClient _imageHttp = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // 検索結果のキャッシュ（タグ経由でも持つが、選択 ASIN 解決のために辞書側も保持）。
    private readonly Dictionary<string, PaItem> _cdResultsByAsin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PaItem> _digitalResultsByAsin = new(StringComparer.Ordinal);

    // 確定済みの選択結果（OK 押下時に呼び出し側が読む）。
    /// <summary>選択された CD 側 ASIN（未選択時は空文字）。</summary>
    public string SelectedCdAsin { get; private set; } = "";
    /// <summary>選択されたデジタル側 ASIN（未選択時は空文字）。</summary>
    public string SelectedDigitalAsin { get; private set; } = "";
    /// <summary>採用するジャケット画像 URL（CD 側選択を優先、未選択ならデジタル側、両方未選択なら空文字）。</summary>
    public string? SelectedCoverImageUrl { get; private set; }
    /// <summary>採用する画像の取得元コード（<c>amazon_cd</c> / <c>amazon_digital</c>）。未選択時は null。</summary>
    public string? SelectedCoverImageSource { get; private set; }

    // PA-API 失敗時のレスポンス本文を含む最新の長文エラーメッセージ。
    // lblStatus クリックで MessageBox に展開してユーザに見せる（自動でクリップボードにもコピー）。
    // 403 Forbidden の原因切り分け（アソシエイト売上要件未達・キー誤り・PartnerTag 不整合 等）には
    // 短い ex.Message ではなく Amazon が返す詳細 JSON が必須になるため、本フィールドで保持する。
    private string? _lastErrorDetail;

    /// <summary><see cref="AmazonProductSearchDialog"/> の新しいインスタンスを生成する。</summary>
    /// <param name="paApi">PA-API クライアント（呼び出し側が App.config から構築済みのもの）。</param>
    /// <param name="initialKeyword">初期検索キーワード（商品名など）。</param>
    public AmazonProductSearchDialog(PaApiClient paApi, string initialKeyword)
    {
        _paApi = paApi ?? throw new ArgumentNullException(nameof(paApi));
        _initialKeyword = initialKeyword ?? "";

        InitializeComponent();

        txtKeyword.Text = _initialKeyword;
        btnSearch.Click += async (_, __) => await DoSearchAsync();
        btnOk.Click += (_, __) => { ConfirmSelection(); DialogResult = DialogResult.OK; Close(); };
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

        // ListView の選択変更で「選択中」ラベルを更新する。
        lvCd.SelectedIndexChanged += (_, __) => OnSideSelectionChanged(lvCd, lblCdSelected, "CD");
        lvDigital.SelectedIndexChanged += (_, __) => OnSideSelectionChanged(lvDigital, lblDigitalSelected, "デジタル");

        // 起動直後に 1 回検索を投げる（初期キーワードがあれば）。
        Load += async (_, __) =>
        {
            if (!string.IsNullOrWhiteSpace(_initialKeyword))
                await DoSearchAsync();
        };

        FormClosed += (_, __) => _imageHttp.Dispose();

        // ステータスバーをクリックすると、最新の PA-API エラー本文（HTTP ステータス + Amazon が返す JSON）を
        // MessageBox で全文表示する。同時にクリップボードへ自動コピーして、トラブル時のサポート連絡に貼り付け
        // やすくする。エラーが未発生（_lastErrorDetail==null）のときはクリックしても何も起きない。
        lblStatus.Cursor = Cursors.Hand;
        lblStatus.Click += (_, __) =>
        {
            if (string.IsNullOrEmpty(_lastErrorDetail)) return;
            try { Clipboard.SetText(_lastErrorDetail); } catch { /* クリップボード失敗は致命的でないため無視 */ }
            MessageBox.Show(this,
                _lastErrorDetail,
                "PA-API エラー詳細（自動でクリップボードへコピー済み）",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
    }

    /// <summary>検索を PA-API に投げ、左右の ListView に結果を流し込む。 PA-API のレート制限（1 TPS）順守のため、CD → デジタルの 2 リクエスト間に 1100ms スリープを挟む。</summary>
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
            _cdResultsByAsin.Clear();
            _digitalResultsByAsin.Clear();
            lvCd.Items.Clear();
            lvDigital.Items.Clear();
            imgList.Images.Clear();

            // 1) CD（SearchIndex=Music）
            IReadOnlyList<PaItem> cdItems;
            try
            {
                cdItems = await _paApi.SearchItemsAsync(kw, PaSearchIndex.Music, itemCount: 10, CancellationToken.None);
            }
            catch (Exception ex)
            {
                cdItems = Array.Empty<PaItem>();
                // PaApiClient は ex.Message に HTTP ステータスと Amazon が返す JSON 本文を改行区切りで載せる。
                // ステータスバーは画面が狭いので 1 行（ex.Message の最初の改行まで）に圧縮し、全文は
                // _lastErrorDetail に退避してクリック時に MessageBox 展開する。
                _lastErrorDetail = ex.ToString();
                lblStatus.Text = "CD 検索失敗（クリックで詳細）: " + FirstLine(ex.Message);
                lblStatus.ForeColor = Color.Firebrick;
            }
            await Task.Delay(1100);

            // 2) デジタル（SearchIndex=DigitalMusic）
            IReadOnlyList<PaItem> digitalItems;
            try
            {
                digitalItems = await _paApi.SearchItemsAsync(kw, PaSearchIndex.DigitalMusic, itemCount: 10, CancellationToken.None);
            }
            catch (Exception ex)
            {
                digitalItems = Array.Empty<PaItem>();
                // 既に CD 側で _lastErrorDetail が埋まっている場合は、最新のエラー（デジタル側）で上書きする。
                // ユーザがクリックで詳細を呼び出すときに見たいのは「最後に起きたエラー」の本文。
                _lastErrorDetail = ex.ToString();
                lblStatus.Text = "デジタル検索失敗（クリックで詳細）: " + FirstLine(ex.Message);
                lblStatus.ForeColor = Color.Firebrick;
            }

            // ListView に流し込む（画像は後追いで取得）。
            await PopulateAsync(lvCd, cdItems, _cdResultsByAsin);
            await PopulateAsync(lvDigital, digitalItems, _digitalResultsByAsin);

            // 両方とも 1 件以上取れたら正常時の見た目に戻す（赤字解除＋詳細クリア）。
            // 片方でも失敗していれば、その失敗時に設定した赤字＋詳細クリック表示をそのまま保持する。
            if (cdItems.Count > 0 || digitalItems.Count > 0)
            {
                if (cdItems.Count > 0 && digitalItems.Count > 0)
                {
                    _lastErrorDetail = null;
                    lblStatus.ForeColor = SystemColors.ControlText;
                }
                lblStatus.Text = $"検索完了: CD={cdItems.Count} 件 / デジタル={digitalItems.Count} 件 ／ 候補をクリックして選択してください";
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
        var tasks = new List<Task>();
        foreach (ListViewItem lvi in lv.Items)
        {
            if (lvi.Tag is not string asin) continue;
            if (!map.TryGetValue(asin, out var it)) continue;
            if (string.IsNullOrWhiteSpace(it.MediumImageUrl)) continue;
            string url = it.MediumImageUrl;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var bytes = await _imageHttp.GetByteArrayAsync(url);
                    using var ms = new MemoryStream(bytes);
                    using var img = Image.FromStream(ms);
                    // UI スレッドで ImageList と ListViewItem を更新する。
                    BeginInvoke(new Action(() =>
                    {
                        // ImageList のキーに ASIN を使えるよう Add(string, Image) を使用。
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
                catch
                {
                    // 画像取得失敗時はサムネなしで表示継続。
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

    /// <summary>OK 押下時に、左右の現在選択を <see cref="SelectedCdAsin"/> 等のプロパティに転記する。 画像は CD 側選択を優先、なければデジタル側、両方未選択なら空文字／null のままにする。</summary>
    private void ConfirmSelection()
    {
        // CD 側
        if (lvCd.SelectedItems.Count > 0 && lvCd.SelectedItems[0].Tag is string cdAsin)
        {
            SelectedCdAsin = cdAsin;
            if (_cdResultsByAsin.TryGetValue(cdAsin, out var cdItem)
                && !string.IsNullOrWhiteSpace(cdItem.LargeImageUrl))
            {
                SelectedCoverImageUrl = cdItem.LargeImageUrl;
                SelectedCoverImageSource = "amazon_cd";
            }
        }

        // デジタル側（CD 側で画像が確定していなければデジタル側を採用）
        if (lvDigital.SelectedItems.Count > 0 && lvDigital.SelectedItems[0].Tag is string dAsin)
        {
            SelectedDigitalAsin = dAsin;
            if (string.IsNullOrEmpty(SelectedCoverImageUrl)
                && _digitalResultsByAsin.TryGetValue(dAsin, out var dItem)
                && !string.IsNullOrWhiteSpace(dItem.LargeImageUrl))
            {
                SelectedCoverImageUrl = dItem.LargeImageUrl;
                SelectedCoverImageSource = "amazon_digital";
            }
        }
    }
}
