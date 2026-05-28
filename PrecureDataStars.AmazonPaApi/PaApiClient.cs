#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.AmazonPaApi;

/// <summary>
/// Amazon Creators API のクライアント。<c>https://creatorsapi.amazon/catalog/v1/*</c> に OAuth 2.0 で
/// 認証して商品情報を取得する。価格情報は OffersV2 リソースから抽出する。
/// <para>
/// 提供メソッドは 2 つ：
/// <list type="bullet">
///   <item><see cref="GetItemAsync"/>: ASIN 1 件指定で商品メタ情報を取得（鮮度更新バッチ用）</item>
///   <item><see cref="SearchItemsAsync"/>: キーワード検索で複数候補を取得（Catalog の検索ダイアログ用）</item>
/// </list>
/// </para>
/// <para>
/// 認証は <see cref="OAuth2TokenProvider"/> 経由でアクセストークンを取得し、
/// <c>Authorization: Bearer &lt;token&gt;[, Version &lt;ver&gt;]</c> ヘッダを付与する
/// （v2.x クレデンシャルは Version を併記、v3.x は Bearer のみ）。
/// マーケットプレイスは <c>x-marketplace</c> ヘッダとリクエストボディ <c>marketplace</c> の両方に乗せる。
/// 画像 URL（<c>m.media-amazon.com</c> 系）は文字列のまま返却し、ローカル保存は行わない（規約遵守）。
/// </para>
/// </summary>
public sealed class PaApiClient
{
    private const string ApiBaseUrl = "https://creatorsapi.amazon";

    private readonly HttpClient _http;
    private readonly OAuth2TokenProvider _tokenProvider;
    private readonly string _partnerTag;
    private readonly string _marketplace;

    /// <summary>直近の Creators API 成功レスポンス本文（生 JSON）。 診断用途：UI / コンソールでパース後の DTO に画像 URL が乗ってこないケースで、 「API が images.primary.* を返していないのか / 別キー構造に変わっているのか」を切り分けるために 呼び出し側から覗ける。レスポンス全体を保持するためそれなりのサイズになり得る。</summary>
    public string? LastRawResponseJson { get; private set; }

    /// <summary>共通リクエストリソース集合。Resources 配列のキャメルケース指定は Creators API 仕様準拠。</summary>
    private static readonly string[] StandardResources = new[]
    {
        "images.primary.medium",
        "images.primary.large",
        "itemInfo.title",
        "itemInfo.byLineInfo",
        "itemInfo.productInfo",
        "offersV2.listings.price",
    };

    /// <summary><see cref="PaApiClient"/> の新しいインスタンスを生成する。</summary>
    /// <param name="http">HTTP クライアント（呼出側で共有する想定）。Factory 経由で構築されるとき同一の <see cref="HttpClient"/> が
    /// <see cref="OAuth2TokenProvider"/> と本クラスで共有される。</param>
    /// <param name="tokenProvider">OAuth トークン管理。クレデンシャルバージョンも本オブジェクト経由で参照する。</param>
    /// <param name="partnerTag">アソシエイトタグ（例 <c>yourtag-22</c>）。リクエストに必須。</param>
    /// <param name="marketplace">マーケットプレイスドメイン（例 <c>www.amazon.co.jp</c>）。
    /// <c>x-marketplace</c> ヘッダとリクエストボディの両方に乗せる。</param>
    public PaApiClient(HttpClient http, OAuth2TokenProvider tokenProvider, string partnerTag, string marketplace)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _partnerTag = partnerTag ?? throw new ArgumentNullException(nameof(partnerTag));
        _marketplace = marketplace ?? throw new ArgumentNullException(nameof(marketplace));
    }

    /// <summary>
    /// ASIN を 1 件指定して Creators API GetItems を叩き、商品メタ情報を取得する。
    /// 取得できなければ null を返す。例外（HTTP 失敗・JSON 構造異常）は呼び出し側に伝播。
    /// レスポンスからは ImageURL（Large/Medium）・Title・ByLineInfo・OffersV2 の Price を抽出する。
    /// </summary>
    public async Task<PaItem?> GetItemAsync(string asin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(asin)) return null;
        var items = await GetItemsAsync(new[] { asin }, ct).ConfigureAwait(false);
        return items.FirstOrDefault();
    }

    /// <summary>
    /// 複数 ASIN を一度に指定して Creators API GetItems を叩く。最大 10 件。
    /// レート制限の節約のため、複数 ASIN がある場合はこのメソッドでまとめて発射するのが推奨。
    /// </summary>
    public async Task<IReadOnlyList<PaItem>> GetItemsAsync(IReadOnlyList<string> asins, CancellationToken ct = default)
    {
        if (asins == null || asins.Count == 0) return Array.Empty<PaItem>();
        if (asins.Count > 10) throw new ArgumentException("Creators API GetItems は 1 回に最大 10 ASIN まで。", nameof(asins));

        // lowerCamelCase フィールドで Creators API へ。partnerType は本 API では送らない仕様。
        var body = new
        {
            itemIds = asins,
            itemIdType = "ASIN",
            marketplace = _marketplace,
            partnerTag = _partnerTag,
            resources = StandardResources,
        };
        string responseJson = await PostAsync("/catalog/v1/getItems", body, ct).ConfigureAwait(false);

        // GetItems の top key は "itemsResult" → items[]（実レスポンス確認済み。SearchItems の "searchResult" とキー名が違う点に注意）。
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("itemsResult", out var topEl)
            || !topEl.TryGetProperty("items", out var itemsEl)
            || itemsEl.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PaItem>();
        }

        var result = new List<PaItem>();
        foreach (var item in itemsEl.EnumerateArray())
        {
            result.Add(ParseItem(item));
        }
        return result;
    }

    /// <summary>
    /// キーワード検索を Creators API SearchItems で実行し、最大 <paramref name="itemCount"/> 件の候補を返す。
    /// <paramref name="searchIndex"/> で物理音楽（Music）／デジタル音源（DigitalMusic）を切り替える。
    /// </summary>
    /// <param name="keywords">検索キーワード（商品名など）。</param>
    /// <param name="searchIndex">検索対象カテゴリ。</param>
    /// <param name="itemCount">取得件数（1〜10、API 仕様の上限内）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<PaItem>> SearchItemsAsync(
        string keywords,
        PaSearchIndex searchIndex,
        int itemCount = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keywords)) return Array.Empty<PaItem>();
        if (itemCount < 1) itemCount = 1;
        if (itemCount > 10) itemCount = 10;

        // SearchIndex は文字列で渡す。Music = 物理 CD、DigitalMusic = MP3 配信音源。
        string indexName = searchIndex switch
        {
            PaSearchIndex.Music => "Music",
            PaSearchIndex.DigitalMusic => "DigitalMusic",
            _ => "All",
        };

        var body = new
        {
            keywords = keywords,
            searchIndex = indexName,
            itemCount = itemCount,
            marketplace = _marketplace,
            partnerTag = _partnerTag,
            resources = StandardResources,
        };
        string responseJson = await PostAsync("/catalog/v1/searchItems", body, ct).ConfigureAwait(false);

        // SearchItems の top key は "searchResult" → items[]（GetItems の "itemResults" とキー名が違う点に注意）。
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("searchResult", out var topEl)
            || !topEl.TryGetProperty("items", out var itemsEl)
            || itemsEl.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<PaItem>();
        }

        var result = new List<PaItem>();
        foreach (var item in itemsEl.EnumerateArray())
        {
            result.Add(ParseItem(item));
        }
        return result;
    }

    /// <summary>レート制限（429）/ 一時的なサーバ過負荷（503）に対する最大リトライ回数。</summary>
    private const int MaxRateLimitRetries = 5;

    /// <summary>
    /// 共通 POST。<see cref="OAuth2TokenProvider"/> からアクセストークンを取得し、
    /// <c>Authorization</c> / <c>x-marketplace</c> ヘッダと JSON ボディを乗せて送信する。
    /// HTTP 429（Too Many Requests）/ 503（Service Unavailable）の場合はスキップせず、
    /// <c>Retry-After</c> ヘッダ（あれば）または指数バックオフ（2,4,8,16,30 秒）で待ってから
    /// 最大 <see cref="MaxRateLimitRetries"/> 回リトライする。それ以外の HTTP エラー、または
    /// リトライ上限に達した場合はレスポンス本文を含む例外で投げ直す。
    /// </summary>
    private async Task<string> PostAsync(string path, object body, CancellationToken ct)
    {
        string token = await _tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
        string jsonBody = JsonSerializer.Serialize(body);
        // v2.x: "Bearer <token>, Version <ver>"、v3.x: "Bearer <token>" のみ。
        string authValue = _tokenProvider.IsV3Credential
            ? $"Bearer {token}"
            : $"Bearer {token}, Version {_tokenProvider.CredentialVersion}";

        for (int attempt = 0; ; attempt++)
        {
            // HttpRequestMessage は使い回せないため、試行ごとに新規生成する。
            using var req = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + path);
            req.Headers.TryAddWithoutValidation("Authorization", authValue);
            req.Headers.TryAddWithoutValidation("x-marketplace", _marketplace);
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            string respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                LastRawResponseJson = respBody;
                return respBody;
            }

            int status = (int)resp.StatusCode;
            bool retriable = status == 429 || status == 503;
            if (retriable && attempt < MaxRateLimitRetries)
            {
                // Retry-After ヘッダ（秒数）を尊重。無ければ指数バックオフ（2,4,8,16,30 秒で頭打ち）。
                TimeSpan wait;
                if (resp.Headers.RetryAfter?.Delta is TimeSpan ra && ra > TimeSpan.Zero)
                {
                    wait = ra;
                }
                else
                {
                    int seconds = Math.Min(30, (int)Math.Pow(2, attempt + 1));
                    wait = TimeSpan.FromSeconds(seconds);
                }
                await Task.Delay(wait, ct).ConfigureAwait(false);
                continue;
            }

            throw new InvalidOperationException(
                $"Creators API request failed: HTTP {status} {resp.ReasonPhrase} (path={path}{(retriable ? $", {MaxRateLimitRetries} 回リトライ後も失敗" : "")})\n{respBody}");
        }
    }

    /// <summary>
    /// Amazon の「画像はありません（No Image Available）」プレースホルダ画像の画像 ID 群。
    /// これらの URL は実ジャケットではないため、取り込み対象から除外する（カバー画像として保存しない）。
    /// 例: <c>https://m.media-amazon.com/images/I/01MKUOLsA5L._SL500_.gif</c>
    /// </summary>
    private static readonly string[] PlaceholderImageIds = new[]
    {
        "01MKUOLsA5L",
    };

    /// <summary>画像 URL が Amazon のプレースホルダ（画像なし）に該当する場合は null を、 そうでなければそのまま返す。空・null もそのまま返す。</summary>
    private static string? NullIfPlaceholder(string? url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        foreach (var id in PlaceholderImageIds)
        {
            if (url.Contains(id, StringComparison.Ordinal)) return null;
        }
        return url;
    }

    /// <summary>
    /// レスポンスの 1 商品要素を <see cref="PaItem"/> に変換する。欠落フィールドは null/空のまま。
    /// Creators API レスポンスは lowerCamelCase で、画像 URL は <c>images.primary.{small|medium|large}.url</c>、
    /// 価格は <c>offersV2.listings[0].price.money.displayAmount</c>（OffersV2 構造）。
    /// </summary>
    private static PaItem ParseItem(JsonElement item)
    {
        var p = new PaItem
        {
            Asin = item.TryGetProperty("asin", out var asinEl) ? asinEl.GetString() ?? "" : "",
            DetailPageUrl = item.TryGetProperty("detailPageURL", out var dpu) ? dpu.GetString() : null,
        };

        // 画像 URL: images.primary.medium.url / images.primary.large.url
        if (item.TryGetProperty("images", out var imagesEl)
            && imagesEl.ValueKind == JsonValueKind.Object
            && imagesEl.TryGetProperty("primary", out var primary)
            && primary.ValueKind == JsonValueKind.Object)
        {
            if (primary.TryGetProperty("medium", out var mediumEl)
                && mediumEl.ValueKind == JsonValueKind.Object
                && mediumEl.TryGetProperty("url", out var mediumUrl))
            {
                p.MediumImageUrl = NullIfPlaceholder(mediumUrl.GetString());
            }
            if (primary.TryGetProperty("large", out var largeEl)
                && largeEl.ValueKind == JsonValueKind.Object
                && largeEl.TryGetProperty("url", out var largeUrl))
            {
                p.LargeImageUrl = NullIfPlaceholder(largeUrl.GetString());
            }
        }

        // itemInfo.title.displayValue / byLineInfo.contributors[0].name / productInfo.releaseDate.displayValue
        if (item.TryGetProperty("itemInfo", out var info)
            && info.ValueKind == JsonValueKind.Object)
        {
            if (info.TryGetProperty("title", out var titleEl)
                && titleEl.ValueKind == JsonValueKind.Object
                && titleEl.TryGetProperty("displayValue", out var titleVal))
            {
                p.Title = titleVal.GetString() ?? "";
            }
            if (info.TryGetProperty("byLineInfo", out var byLine)
                && byLine.ValueKind == JsonValueKind.Object
                && byLine.TryGetProperty("contributors", out var contribs)
                && contribs.ValueKind == JsonValueKind.Array
                && contribs.GetArrayLength() > 0)
            {
                var first = contribs[0];
                if (first.TryGetProperty("name", out var nameEl))
                    p.ByLine = nameEl.GetString();
            }
            if (info.TryGetProperty("productInfo", out var prodInfo)
                && prodInfo.ValueKind == JsonValueKind.Object
                && prodInfo.TryGetProperty("releaseDate", out var rdEl)
                && rdEl.ValueKind == JsonValueKind.Object
                && rdEl.TryGetProperty("displayValue", out var rdVal))
            {
                p.ReleaseDate = rdVal.GetString();
            }
        }

        // offersV2.listings[0].price.money.displayAmount（OffersV2 構造、display 文字列「¥3,300」がそのまま入る）。
        if (item.TryGetProperty("offersV2", out var offers)
            && offers.ValueKind == JsonValueKind.Object
            && offers.TryGetProperty("listings", out var listings)
            && listings.ValueKind == JsonValueKind.Array
            && listings.GetArrayLength() > 0)
        {
            var listing0 = listings[0];
            if (listing0.TryGetProperty("price", out var priceEl)
                && priceEl.ValueKind == JsonValueKind.Object
                && priceEl.TryGetProperty("money", out var moneyEl)
                && moneyEl.ValueKind == JsonValueKind.Object
                && moneyEl.TryGetProperty("displayAmount", out var displayAmt))
            {
                p.PriceDisplay = displayAmt.GetString();
            }
        }

        return p;
    }
}
