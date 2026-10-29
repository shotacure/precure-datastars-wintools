using System.Text.Json;

namespace PrecureDataStars.DeezerIsrcSync;

/// <summary>
/// Deezer の公開 API（<c>https://api.deezer.com/</c>、認証なし）の読み取り専用クライアント。
/// <para>
/// API の上限は 5 秒あたり 50 リクエストなので、リクエストの間を <see cref="MinInterval"/> 空ける。
/// 上限に当たったとき（エラーコード 4）は少し待って同じリクエストをやり直す。
/// Deezer は失敗も HTTP 200 で返し、本文の <c>error</c> に種類を入れる。
/// 見つからないとき（エラーコード 800）は null を返す。
/// </para>
/// </summary>
internal sealed class DeezerApiClient : IDisposable
{
    private const string BaseUrl = "https://api.deezer.com/";

    /// <summary>リクエストの最小間隔（5 秒あたり 50 リクエストの上限に収める）。</summary>
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(120);

    /// <summary>上限に当たったときの待ち時間。</summary>
    private static readonly TimeSpan QuotaWait = TimeSpan.FromSeconds(6);

    private const int MaxQuotaRetries = 5;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private DateTime _lastRequestAt = DateTime.MinValue;

    /// <summary>これまでに送ったリクエストの数（結果の表示用）。</summary>
    public int RequestCount { get; private set; }

    public DeezerApiClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PrecureDataStars.DeezerIsrcSync");
    }

    /// <summary>ISRC から曲を引き、その曲が入っているアルバムの id を返す。見つからなければ null。</summary>
    public async Task<long?> FindAlbumIdByIsrcAsync(string isrc, CancellationToken ct = default)
    {
        using var doc = await GetAsync($"track/isrc:{Uri.EscapeDataString(isrc)}", ct).ConfigureAwait(false);
        if (doc is null) return null;
        return doc.RootElement.TryGetProperty("album", out var album) && album.TryGetProperty("id", out var id)
            ? id.GetInt64()
            : null;
    }

    /// <summary>UPC（JAN）からアルバムを引き、その id を返す。見つからなければ null。</summary>
    public async Task<long?> FindAlbumIdByUpcAsync(string upc, CancellationToken ct = default)
    {
        using var doc = await GetAsync($"album/upc:{Uri.EscapeDataString(upc)}", ct).ConfigureAwait(false);
        if (doc is null) return null;
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetInt64() : null;
    }

    /// <summary>アルバムの題・UPC と曲目（ISRC・何枚目・何曲目）を取る。<c>next</c> をたどって全曲を集める。アルバムが無ければ null。</summary>
    public async Task<DeezerAlbum?> GetAlbumAsync(long albumId, CancellationToken ct = default)
    {
        string title, upc;
        using (var doc = await GetAsync($"album/{albumId}", ct).ConfigureAwait(false))
        {
            if (doc is null) return null;
            title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            upc = doc.RootElement.TryGetProperty("upc", out var u) ? u.GetString() ?? "" : "";
        }

        var tracks = new List<DeezerTrack>();
        string? path = $"album/{albumId}/tracks?limit=100";
        while (path is not null)
        {
            using var page = await GetAsync(path, ct).ConfigureAwait(false);
            if (page is null) break;
            if (page.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var tr in data.EnumerateArray())
                {
                    tracks.Add(new DeezerTrack(
                        tr.TryGetProperty("isrc", out var isrc) ? isrc.GetString() : null,
                        tr.TryGetProperty("disk_number", out var disk) ? disk.GetInt32() : 1,
                        tr.TryGetProperty("track_position", out var pos) ? pos.GetInt32() : 0,
                        tr.TryGetProperty("title", out var tt) ? tt.GetString() ?? "" : ""));
                }
            }
            path = page.RootElement.TryGetProperty("next", out var next) && next.GetString() is { } n && n.StartsWith(BaseUrl, StringComparison.Ordinal)
                ? n[BaseUrl.Length..]
                : null;
        }
        return new DeezerAlbum(albumId, title, upc, tracks);
    }

    /// <summary>
    /// 1 リクエストを送り、本文を JSON で返す。見つからない（エラーコード 800）は null。
    /// 上限（エラーコード 4）は待ってやり直し、そのほかのエラーは例外にする。
    /// </summary>
    private async Task<JsonDocument?> GetAsync(string path, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            var wait = _lastRequestAt + MinInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
            _lastRequestAt = DateTime.UtcNow;
            RequestCount++;

            using var res = await _http.GetAsync(BaseUrl + path, ct).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();
            var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return doc;

            int code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
            string message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
            doc.Dispose();
            if (code == 800) return null;
            if (code == 4 && attempt < MaxQuotaRetries)
            {
                await Task.Delay(QuotaWait, ct).ConfigureAwait(false);
                continue;
            }
            throw new InvalidOperationException($"Deezer API のエラー（{path}）：{code} {message}");
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Deezer のアルバム 1 枚（曲目つき）。</summary>
internal sealed record DeezerAlbum(long AlbumId, string Title, string Upc, IReadOnlyList<DeezerTrack> Tracks);

/// <summary>Deezer のアルバムの 1 曲。<see cref="DiskNumber"/> と <see cref="TrackPosition"/> は 1 始まり。</summary>
internal sealed record DeezerTrack(string? Isrc, int DiskNumber, int TrackPosition, string Title);
