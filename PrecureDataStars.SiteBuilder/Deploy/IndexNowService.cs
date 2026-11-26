using System.Net.Http.Json;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Deploy;

/// <summary>
/// 本番デプロイで新しく出た・書き換わったページの URL を IndexNow（<c>https://api.indexnow.org/indexnow</c>。
/// Bing・Yandex などが受け、受け手どうしで共有する）へ送る。
/// <list type="bullet">
///   <item>送るのは、上げたファイルのうちページ（<c>…/index.html</c>）で、このビルドでサイトマップの更新日が進んだもの
///   （<see cref="SitemapLastmodManifest.LoadChangedList"/>。ほかの回のデータで毎週変わる部分だけが変わったページは送らない）。
///   更新日の一覧が無いときは、上げたページをすべて対象にする。URL は <c>{BaseUrl}/{フォルダ}/</c> の形にする。</item>
///   <item>新しく出たページを先に、続けて書き換わったページを、上限（<see cref="BuildConfig.IndexNowMaxUrls"/>）まで送る。</item>
///   <item>鍵（<see cref="BuildConfig.IndexNowKey"/>）が未設定なら何もしない。鍵のファイル（<c>/{鍵}.txt</c>）は SeoGenerator が書き出す。</item>
///   <item>送れなかったときは警告にとどめ、デプロイの結果は変えない（次のデプロイで書き換わったページはまた送られる）。</item>
/// </list>
/// </summary>
public sealed class IndexNowService
{
    private const string Endpoint = "https://api.indexnow.org/indexnow";

    /// <summary>送らないページのフォルダ（サイトの部品の置き場で、検索に載せるページではないもの）。</summary>
    private static readonly string[] SkippedPrefixes = { "_edge/", "og-gallery/" };

    private readonly BuildConfig _config;
    private readonly BuildLogger _logger;

    public IndexNowService(BuildConfig config, BuildLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>上げたファイル（S3 のキーと、新しく出たものか）から、送るページの URL を選んで送る。</summary>
    public async Task RunAsync(IReadOnlyList<(string Key, bool IsNew)> uploaded, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_config.IndexNowKey) || string.IsNullOrEmpty(_config.BaseUrl)) return;

        var changed = SitemapLastmodManifest.LoadChangedList(_config.OutputDirectory);
        var urls = SelectUrls(uploaded, changed, _config.BaseUrl, _config.IndexNowMaxUrls, out int pageCount);
        if (urls.Count == 0)
        {
            _logger.Info("IndexNow         : 送るページなし");
            return;
        }

        var host = new Uri(_config.BaseUrl).Host;
        var body = new
        {
            host,
            key = _config.IndexNowKey,
            keyLocation = $"{_config.BaseUrl}/{_config.IndexNowKey}.txt",
            urlList = urls,
        };

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var res = await http.PostAsJsonAsync(Endpoint, body, ct).ConfigureAwait(false);
            // 200（受け付けた）と 202（受け付けたが、鍵の確かめはこれから）が成功。
            if ((int)res.StatusCode is 200 or 202)
            {
                string capped = urls.Count < pageCount ? $"（送る候補 {pageCount} 件のうち、新しく出たページを先に上限まで）" : "";
                _logger.Info($"IndexNow         : {urls.Count} 件を送りました{capped}（HTTP {(int)res.StatusCode}）");
            }
            else
            {
                var text = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.Warn($"IndexNow へ送れませんでした（HTTP {(int)res.StatusCode}）: {Shorten(text)}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.Warn($"IndexNow へ送れませんでした: {ex.Message}");
        }
    }

    /// <summary>
    /// 上げたファイルから、送るページの URL を選ぶ。<paramref name="changed"/>（このビルドで更新日が進んだページ）があれば、
    /// それに載っているページだけにする。新しく出たページ → 書き換わったページの順（それぞれアップロードの順）で、
    /// 上限まで。<paramref name="pageCount"/> には上限で切る前のページの数を返す。
    /// </summary>
    internal static List<string> SelectUrls(
        IReadOnlyList<(string Key, bool IsNew)> uploaded, ChangedPages? changed, string baseUrl, int maxUrls, out int pageCount)
    {
        var changedSet = changed is null ? null : new HashSet<string>(changed.Added.Concat(changed.Changed), StringComparer.Ordinal);
        var pages = uploaded
            .Where(u => u.Key == "index.html" || u.Key.EndsWith("/index.html", StringComparison.Ordinal))
            .Where(u => !SkippedPrefixes.Any(p => u.Key.StartsWith(p, StringComparison.Ordinal)))
            .Where(u => changedSet is null || changedSet.Contains(ToUrlPath(u.Key)))
            .ToList();
        pageCount = pages.Count;
        return pages
            .OrderBy(u => u.IsNew ? 0 : 1)   // 安定な並べ替えなので、同じ段の中はアップロードの順のまま
            .Take(maxUrls)
            .Select(u => ToUrl(baseUrl, u.Key))
            .ToList();
    }

    /// <summary>S3 のキー（<c>people/名前/index.html</c>）をサイトマップと同じ URL パス（<c>/people/名前/</c>。エンコードしない）にする。</summary>
    private static string ToUrlPath(string key)
        => key == "index.html" ? "/" : "/" + key[..^"index.html".Length];

    /// <summary>S3 のキー（<c>people/名前/index.html</c>）をページの URL（<c>https://precure.tv/people/%E5%90%8D…/</c>）にする。</summary>
    private static string ToUrl(string baseUrl, string key)
    {
        var dir = key.Length > "index.html".Length ? key[..^"/index.html".Length] : "";
        if (dir.Length == 0) return baseUrl + "/";
        var encoded = string.Join("/", dir.Split('/').Select(Uri.EscapeDataString));
        return $"{baseUrl}/{encoded}/";
    }

    private static string Shorten(string text) => text.Length <= 200 ? text : text[..200] + "…";
}
