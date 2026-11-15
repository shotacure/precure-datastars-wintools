using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PrecureDataStars.Catalog.Common.Services;

namespace PrecureDataStars.EpisodeLinkSync;

/// <summary>ページの確かめの結果。</summary>
public enum ProbeResult
{
    /// <summary>ページがあり、その回のページだと確かめられた。</summary>
    Found,

    /// <summary>まだ出ていない（トップへの転送・404・その回の話数が本文に無い）。</summary>
    NotYet,

    /// <summary>確かめられなかった（断られた・つながらない・間隔を広げて待っている間）。</summary>
    Unknown
}

/// <summary>YouTube のチャンネルのアップロードの再生リストの 1 本。</summary>
public sealed record FeedVideo(string VideoId, string Title)
{
    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";
}

/// <summary>
/// 公式のページと YouTube の新着を見に行く窓口。
/// <para>
/// 転送は追わない（東映アニメーションのサイトは、まだ無い回のページをトップへ転送する）。
/// 断られたとき（429・5xx）とつながらないときは、その相手（ホスト）だけ間隔を広げ（10 秒から倍々に最長 5 分）、
/// 広げている間はリクエストを送らずに <see cref="ProbeResult.Unknown"/> を返す。うまく取れたら間隔を戻す。
/// </para>
/// <para>
/// YouTube は Data API v3 だけで見る（チャンネルのアップロードの再生リストの先頭の 1 ページ。1 回 1 ユニット）。
/// 429・5xx・つながらないときは上と同じく間隔を広げる。それ以外の断り（403 のクォータ超過・キーの誤りなど）は
/// 待っても直らないので、この実行の間はもう YouTube を見ない（<see cref="YouTubeStoppedReason"/>）。
/// </para>
/// </summary>
public sealed class WebProbe : IDisposable
{
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    /// <summary>新着として読む本数（アップロードの再生リストの先頭から）。</summary>
    private const int UploadsHeadSize = 15;

    /// <summary>YouTube Data API の相手。間隔を広げるときの鍵にも使う。</summary>
    private const string YouTubeApiHost = "www.googleapis.com";

    private readonly HttpClient _http;
    private readonly YouTubeDataApiClient _youtube;
    private readonly Action<string> _log;
    private readonly Dictionary<string, (DateTime Until, TimeSpan Backoff)> _backoff = new(StringComparer.OrdinalIgnoreCase);

    public WebProbe(YouTubeDataApiClient youtube, Action<string> log)
    {
        _youtube = youtube;
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

    /// <summary>YouTube を最後に読めた時刻。まだ読めていなければ null。</summary>
    public DateTime? YouTubeLastReadAt { get; private set; }

    /// <summary>待っても直らない断りで YouTube を見るのをやめたときの理由。見ている間は null。</summary>
    public string? YouTubeStoppedReason { get; private set; }

    /// <summary>
    /// YouTube のチャンネルの新着（アップロードの再生リストの先頭 15 本、新しい順）を Data API で読む。
    /// 読めなければ null（間隔を広げている間・断られた・つながらない・見るのをやめた後）。
    /// </summary>
    public async Task<IReadOnlyList<FeedVideo>?> ReadChannelUploadsAsync(string channelId, CancellationToken ct)
    {
        if (YouTubeStoppedReason is not null) return null;
        if (_backoff.TryGetValue(YouTubeApiHost, out var b) && DateTime.Now < b.Until) return null;

        // チャンネル ID の頭の「UC」を「UU」にしたものが、そのチャンネルのアップロードの再生リスト。
        string uploads = "UU" + channelId[2..];
        try
        {
            var items = await _youtube.GetPlaylistHeadAsync(uploads, UploadsHeadSize, ct).ConfigureAwait(false);
            _backoff.Remove(YouTubeApiHost);
            YouTubeLastReadAt = DateTime.Now;
            return items.Select(v => new FeedVideo(v.VideoId, v.Title)).ToList();
        }
        catch (YouTubeDataApiException ex) when ((int)ex.StatusCode == 429 || (int)ex.StatusCode >= 500)
        {
            Backoff(YouTubeApiHost, $"HTTP {(int)ex.StatusCode}");
            return null;
        }
        catch (YouTubeDataApiException ex)
        {
            YouTubeStoppedReason = ex.Message;
            _log($"YouTube Data API に断られたので、この実行の間は YouTube を見ません：{ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Backoff(YouTubeApiHost, ex.Message);
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
