using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.Catalog.Common.Services;

/// <summary>
/// YouTube Data API v3 の薄いクライアント。配信音源（アートトラック）の取り込みに必要な
/// 2 つの読み取り操作だけを扱う。
/// <list type="bullet">
///   <item><see cref="GetPlaylistItemsAsync"/>：アルバムのプレイリストを全件展開する（50 件 1 ユニット）。</item>
///   <item><see cref="GetVideoDetailsAsync"/>：動画の埋め込み可否・地域制限・再生時間を取得する（50 件 1 ユニット）。</item>
/// </list>
/// <para>
/// 高コストな <c>search.list</c>（1 回 100 ユニット）は使わない。自動生成のアルバムプレイリスト
/// （<c>OLAK5uy_...</c>）は Data API の検索対象に含まれず、そもそも探索手段として成立しないため、
/// プレイリスト ID は人がプレイリストページの URL を貼って与える前提とする。
/// </para>
/// <para>
/// 日次クォータは既定 10,000 ユニット。本クライアントが使う 2 操作はいずれも 50 件 1 ユニットなので、
/// 全商品（400 点弱）を取り込んでも消費は十数ユニットに収まる。
/// </para>
/// </summary>
public sealed class YouTubeDataApiClient : IDisposable
{
    private const string BaseUrl = "https://www.googleapis.com/youtube/v3/";
    /// <summary>playlistItems / videos とも 1 リクエストの上限は 50 件。</summary>
    private const int PageSize = 50;
    /// <summary>
    /// <see cref="GetVideoDetailsAsync"/> で同時に送る要求の数。1 要求の往復待ちが大半なので、50 件ずつの要求を
    /// この本数まで同時に送る。消費するユニット数は送り方によらず要求の数（50 件ごとに 1）のまま。
    /// </summary>
    private const int VideoDetailsConcurrency = 8;

    private readonly HttpClient _http;
    private readonly string _apiKey;

    /// <summary><see cref="YouTubeDataApiClient"/> の新しいインスタンスを生成する。</summary>
    /// <param name="apiKey">Google Cloud で発行した API キー（YouTube Data API v3 を有効化したもの）。</param>
    public YouTubeDataApiClient(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("YouTube Data API のキーが設定されていません。", nameof(apiKey));

        _apiKey = apiKey;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>プレイリストの収録動画を並び順のまま全件取得する。 ページングは内部で処理し、position 昇順に整列して返す。 複数枚組のアルバムは YouTube 側で 1 本のプレイリストに平坦化されているため、 返る並びがそのままディスク 1 枚目から順の通し並びになる。</summary>
    /// <param name="playlistId">プレイリスト ID（<c>OLAK5uy_...</c>）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<YouTubePlaylistVideo>> GetPlaylistItemsAsync(
        string playlistId, CancellationToken ct = default)
    {
        var results = new List<YouTubePlaylistVideo>();
        string? pageToken = null;

        do
        {
            string url = $"{BaseUrl}playlistItems?part=snippet&maxResults={PageSize}"
                       + $"&playlistId={Uri.EscapeDataString(playlistId)}&key={Uri.EscapeDataString(_apiKey)}"
                       + (pageToken is null ? "" : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("snippet", out var sn)) continue;

                    string videoId = sn.TryGetProperty("resourceId", out var res)
                        && res.TryGetProperty("videoId", out var vid) ? (vid.GetString() ?? "") : "";
                    if (videoId.Length == 0) continue;

                    results.Add(new YouTubePlaylistVideo
                    {
                        Position = sn.TryGetProperty("position", out var pos) ? pos.GetInt32() : results.Count,
                        VideoId = videoId,
                        Title = sn.TryGetProperty("title", out var ti) ? (ti.GetString() ?? "") : ""
                    });
                }
            }

            pageToken = root.TryGetProperty("nextPageToken", out var np) ? np.GetString() : null;
        }
        while (!string.IsNullOrEmpty(pageToken));

        // position は API 側で歯抜けになることがないが、ページ跨ぎの順序を保証するため明示的に整列する。
        return results.OrderBy(v => v.Position).ToList();
    }

    /// <summary>動画の埋め込み可否（<c>status.embeddable</c>）と再生時間（<c>contentDetails.duration</c>）を まとめて取得する。埋め込み可否は権利者が外部サイトでの再生を許可していない音源を掲載しない判定に、 再生時間はトラックとの対応付けに使う。地域制限は日本で再生できない音源を見分けるのに使う。 応答に含まれない動画 ID（削除・非公開など）は戻り値に現れない。</summary>
    /// <param name="videoIds">対象の動画 ID。50 件ずつに分割し、<see cref="VideoDetailsConcurrency"/> 本まで同時に問い合わせる。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyDictionary<string, YouTubeVideoDetail>> GetVideoDetailsAsync(
        IEnumerable<string> videoIds, CancellationToken ct = default)
    {
        var distinct = videoIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
        var chunks = distinct.Chunk(PageSize).ToList();
        var partials = new IReadOnlyList<KeyValuePair<string, YouTubeVideoDetail>>[chunks.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, chunks.Count),
            new ParallelOptions { MaxDegreeOfParallelism = VideoDetailsConcurrency, CancellationToken = ct },
            async (i, token) => partials[i] = await GetVideoDetailsChunkAsync(chunks[i], token).ConfigureAwait(false))
            .ConfigureAwait(false);

        // 要求ごとの結果を、分割した順に 1 つの表へまとめる（同じ ID は後の要求の値で上書き。distinct 済みなので重ならない）。
        var map = new Dictionary<string, YouTubeVideoDetail>(StringComparer.Ordinal);
        foreach (var part in partials)
            foreach (var (id, detail) in part) map[id] = detail;
        return map;
    }

    /// <summary><see cref="GetVideoDetailsAsync"/> の 1 要求ぶん（50 件まで）を問い合わせ、応答に含まれた動画の詳細を返す。</summary>
    private async Task<IReadOnlyList<KeyValuePair<string, YouTubeVideoDetail>>> GetVideoDetailsChunkAsync(
        IEnumerable<string> chunk, CancellationToken ct)
    {
        var found = new List<KeyValuePair<string, YouTubeVideoDetail>>();
        string url = $"{BaseUrl}videos?part=status,contentDetails&id={Uri.EscapeDataString(string.Join(",", chunk))}"
                   + $"&key={Uri.EscapeDataString(_apiKey)}";

        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
        if (!doc.RootElement.TryGetProperty("items", out var items)) return found;

        foreach (var item in items.EnumerateArray())
        {
            string id = item.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
            if (id.Length == 0) continue;

            bool embeddable = item.TryGetProperty("status", out var st)
                && st.TryGetProperty("embeddable", out var em)
                && em.ValueKind == JsonValueKind.True;

            // 処理が終わっていない・却下された動画は、応答には現れても再生できない。
            bool processed = !(item.TryGetProperty("status", out var st2)
                && st2.TryGetProperty("uploadStatus", out var us)
                && !string.Equals(us.GetString(), "processed", StringComparison.Ordinal));

            int? seconds = null;
            bool blockedInJapan = false;
            if (item.TryGetProperty("contentDetails", out var cd))
            {
                if (cd.TryGetProperty("duration", out var du))
                    seconds = ParseIso8601Seconds(du.GetString());
                if (cd.TryGetProperty("regionRestriction", out var rr))
                    blockedInJapan = IsBlockedIn(rr, RegionJapan);
            }

            found.Add(new(id, new YouTubeVideoDetail
            {
                Embeddable = embeddable,
                DurationSeconds = seconds,
                BlockedInJapan = blockedInJapan,
                Processed = processed
            }));
        }

        return found;
    }

    /// <summary>サイトの閲覧者がいる地域（ISO 3166-1 alpha-2）。地域制限はこの地域で再生できるかだけを見る。</summary>
    private const string RegionJapan = "JP";

    /// <summary><c>contentDetails.regionRestriction</c> が指定地域での再生を止めているか。 <c>allowed</c>（許可リスト）があればそこに無い地域が、<c>blocked</c>（拒否リスト）があればそこに載る地域が 再生できない。どちらも無ければ制限なし。</summary>
    internal static bool IsBlockedIn(JsonElement regionRestriction, string region)
    {
        if (regionRestriction.TryGetProperty("allowed", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
            return !allowed.EnumerateArray().Any(e => string.Equals(e.GetString(), region, StringComparison.OrdinalIgnoreCase));
        if (regionRestriction.TryGetProperty("blocked", out var blocked) && blocked.ValueKind == JsonValueKind.Array)
            return blocked.EnumerateArray().Any(e => string.Equals(e.GetString(), region, StringComparison.OrdinalIgnoreCase));
        return false;
    }

    /// <summary>ISO 8601 の期間表記（<c>PT4M13S</c> 形式）を秒に直す。 アートトラックは時間単位に達しないが、念のため時・分・秒の 3 単位を読む。 解釈できない値は null を返し、対応付けでは尺を使わない扱いにする。</summary>
    internal static int? ParseIso8601Seconds(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return null;

        var m = Regex.Match(duration, @"^P(?:\d+D)?T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$");
        if (!m.Success) return null;

        int h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        int mi = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        int s = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        int total = h * 3600 + mi * 60 + s;
        return total > 0 ? total : null;
    }

    /// <summary>API を叩いて JSON を返す。エラー応答は本文のメッセージを添えて例外にする。</summary>
    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var res = await _http.GetAsync(url, ct).ConfigureAwait(false);
        string body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            // API のエラー本文は { "error": { "code": ..., "message": ... } } 形式。
            // クォータ超過（403 quotaExceeded）が最も起こりやすいため、原文をそのまま見せる。
            string detail = body;
            try
            {
                using var errDoc = JsonDocument.Parse(body);
                if (errDoc.RootElement.TryGetProperty("error", out var err)
                    && err.TryGetProperty("message", out var msg))
                {
                    detail = msg.GetString() ?? body;
                }
            }
            catch (JsonException) { /* 解析できなければ本文をそのまま使う */ }

            throw new YouTubeDataApiException(res.StatusCode, detail);
        }

        return JsonDocument.Parse(body);
    }

    /// <summary>内部の <see cref="HttpClient"/> を破棄する。</summary>
    public void Dispose() => _http.Dispose();
}

/// <summary>プレイリスト内の動画 1 件。</summary>
public sealed class YouTubePlaylistVideo
{
    /// <summary>プレイリスト内の 0 始まり位置。</summary>
    public int Position { get; set; }
    public string VideoId { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>動画 1 件の付随情報（埋め込み可否と再生時間）。</summary>
public sealed class YouTubeVideoDetail
{
    /// <summary>外部サイトでの埋め込み再生が許可されているか。</summary>
    public bool Embeddable { get; set; }

    /// <summary>再生時間（秒）。取得できなければ null。</summary>
    public int? DurationSeconds { get; set; }

    /// <summary>地域制限で日本では再生できないか。</summary>
    public bool BlockedInJapan { get; set; }

    /// <summary>アップロードの処理が済んでいるか（<c>status.uploadStatus = processed</c>）。 却下・処理中などの動画は応答に現れても再生できない。</summary>
    public bool Processed { get; set; } = true;
}

/// <summary>YouTube Data API がエラー応答を返したことを表す例外。</summary>
public sealed class YouTubeDataApiException : Exception
{
    /// <summary>HTTP ステータスコード。403 はクォータ超過・キーの権限不足が多い。</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary><see cref="YouTubeDataApiException"/> の新しいインスタンスを生成する。</summary>
    public YouTubeDataApiException(HttpStatusCode statusCode, string message)
        : base($"YouTube Data API エラー ({(int)statusCode}): {message}")
    {
        StatusCode = statusCode;
    }
}
