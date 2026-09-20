using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.Catalog.Common.Services;

/// <summary>
/// YouTube Data API v3 の薄いクライアント。配信音源（アートトラック）の取り込みに必要な
/// 2 つの読み取り操作だけを扱う。
/// <list type="bullet">
///   <item><see cref="GetPlaylistItemsAsync"/>：アルバムのプレイリストを全件展開する（50 件 1 ユニット）。</item>
///   <item><see cref="GetEmbeddableAsync"/>：動画の埋め込み可否を取得する（50 件 1 ユニット）。</item>
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

    /// <summary>動画の埋め込み可否（<c>status.embeddable</c>）をまとめて取得する。 権利者が外部サイトでの再生を許可していない音源を掲載しないための判定に使う。 応答に含まれない動画 ID（削除・非公開など）は戻り値に現れない。</summary>
    /// <param name="videoIds">対象の動画 ID。50 件ずつに分割して問い合わせる。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyDictionary<string, bool>> GetEmbeddableAsync(
        IEnumerable<string> videoIds, CancellationToken ct = default)
    {
        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        var distinct = videoIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();

        for (int offset = 0; offset < distinct.Count; offset += PageSize)
        {
            var chunk = distinct.Skip(offset).Take(PageSize);
            string url = $"{BaseUrl}videos?part=status&id={Uri.EscapeDataString(string.Join(",", chunk))}"
                       + $"&key={Uri.EscapeDataString(_apiKey)}";

            using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("items", out var items)) continue;

            foreach (var item in items.EnumerateArray())
            {
                string id = item.TryGetProperty("id", out var idEl) ? (idEl.GetString() ?? "") : "";
                if (id.Length == 0) continue;

                bool embeddable = item.TryGetProperty("status", out var st)
                    && st.TryGetProperty("embeddable", out var em)
                    && em.ValueKind == JsonValueKind.True;

                map[id] = embeddable;
            }
        }

        return map;
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
