using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PrecureDataStars.EpisodeLinkSync;

/// <summary>ページの確かめの結果。</summary>
internal enum ProbeResult
{
    /// <summary>ページがあり、その回のページだと確かめられた。</summary>
    Found,

    /// <summary>まだ出ていない（トップへの転送・404・その回の話数が本文に無い）。</summary>
    NotYet,

    /// <summary>確かめられなかった（断られた・つながらない・間隔を広げて待っている間）。</summary>
    Unknown
}

/// <summary>YouTube のチャンネルの新着一覧（RSS）の 1 本。</summary>
internal sealed record FeedVideo(string VideoId, string Title)
{
    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>
/// 公式のページと YouTube の新着一覧を見に行く HTTP の窓口。
/// <para>
/// 転送は追わない（東映アニメーションのサイトは、まだ無い回のページをトップへ転送する）。
/// 断られたとき（429・5xx）とつながらないときは、その相手（ホスト）だけ間隔を広げ（10 秒から倍々に最長 5 分）、
/// 広げている間はリクエストを送らずに <see cref="ProbeResult.Unknown"/> を返す。うまく取れたら間隔を戻す。
/// </para>
/// </summary>
internal sealed class WebProbe : IDisposable
{
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";

    private readonly HttpClient _http;
    private readonly Action<string> _log;
    private readonly Dictionary<string, (DateTime Until, TimeSpan Backoff)> _backoff = new(StringComparer.OrdinalIgnoreCase);

    public WebProbe(Action<string> log)
    {
        _log = log;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        })
        { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>
    /// 各話のページがあるかを確かめる。200 で返り、本文に「第{episodeNo}話」（空白・全角は問わない）があれば
    /// <see cref="ProbeResult.Found"/>。
    /// </summary>
    public async Task<ProbeResult> ProbeEpisodePageAsync(string url, int episodeNo, CancellationToken ct)
    {
        var (status, body) = await GetAsync(url, ct).ConfigureAwait(false);
        if (status is null) return ProbeResult.Unknown;
        if (status != HttpStatusCode.OK || body is null) return ProbeResult.NotYet;
        string flat = Regex.Replace(body.Normalize(NormalizationForm.FormKC), @"\s+", "");
        return flat.Contains($"第{episodeNo.ToString(CultureInfo.InvariantCulture)}話", StringComparison.Ordinal)
            ? ProbeResult.Found : ProbeResult.NotYet;
    }

    /// <summary>YouTube のチャンネルの新着一覧（RSS、新しい順に 15 本ほど）を読む。読めなければ null。</summary>
    public async Task<IReadOnlyList<FeedVideo>?> ReadChannelFeedAsync(string channelId, CancellationToken ct)
    {
        string url = $"https://www.youtube.com/feeds/videos.xml?channel_id={Uri.EscapeDataString(channelId)}";
        var (status, body) = await GetAsync(url, ct).ConfigureAwait(false);
        if (status != HttpStatusCode.OK || body is null) return null;
        try
        {
            return XDocument.Parse(body).Root?.Elements(Atom + "entry")
                .Select(e => new FeedVideo((string?)e.Element(Yt + "videoId") ?? "", (string?)e.Element(Atom + "title") ?? ""))
                .Where(v => v.VideoId.Length > 0)
                .ToList() ?? [];
        }
        catch (System.Xml.XmlException ex)
        {
            _log($"YouTube の新着一覧を読めませんでした：{ex.Message}");
            return null;
        }
    }

    /// <summary>GET する。確かめられなかったとき（間隔を広げている間・断られた・つながらない）は状態を null で返す。</summary>
    private async Task<(HttpStatusCode? Status, string? Body)> GetAsync(string url, CancellationToken ct)
    {
        string host = new Uri(url).Host;
        if (_backoff.TryGetValue(host, out var b) && DateTime.Now < b.Until) return (null, null);

        try
        {
            using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
            int code = (int)res.StatusCode;
            if (code == 429 || code >= 500)
            {
                Backoff(host, $"HTTP {code}");
                return (null, null);
            }
            _backoff.Remove(host);
            string? body = res.StatusCode == HttpStatusCode.OK
                ? await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false)
                : null;
            return (res.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Backoff(host, ex.Message);
            return (null, null);
        }
    }

    private void Backoff(string host, string reason)
    {
        var next = _backoff.TryGetValue(host, out var b) ? b.Backoff * 2 : FirstBackoff;
        if (next > MaxBackoff) next = MaxBackoff;
        _backoff[host] = (DateTime.Now + next, next);
        _log($"{host} に断られたかつながりませんでした（{reason}）。{next.TotalSeconds:0} 秒あけてから見に行きます。");
    }

    public void Dispose() => _http.Dispose();
}
