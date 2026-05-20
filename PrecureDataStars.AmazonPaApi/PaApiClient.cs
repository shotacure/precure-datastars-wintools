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
/// Amazon Product Advertising API 5.0 (PA-API) のクライアント。
/// <para>
/// 提供メソッドは 2 つ：
/// <list type="bullet">
///   <item><see cref="GetItemAsync"/>: ASIN 1 件指定で商品メタ情報を取得（鮮度更新バッチ用）</item>
///   <item><see cref="SearchItemsAsync"/>: キーワード検索で複数候補を取得（Catalog の検索ダイアログ用）</item>
/// </list>
/// </para>
/// PA-API のレート制限（標準 1 TPS / 8640 TPD）は呼び出し側で順守する。
/// 内部では HttpClient を共有し、AWS Signature V4 署名は <see cref="AwsV4Signer"/> に委譲する。
/// 画像 URL（<c>m.media-amazon.com</c> 系）は文字列のまま返却し、ローカル保存は行わない（規約遵守）。
/// </summary>
public sealed class PaApiClient
{
    private static readonly HttpClient _http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _partnerTag;
    private readonly string _host;       // 例: webservices.amazon.co.jp
    private readonly string _region;     // 例: us-west-2
    private readonly string _marketplace; // 例: www.amazon.co.jp

    /// <summary><see cref="PaApiClient"/> の新しいインスタンスを生成する。</summary>
    /// <param name="accessKey">PA-API のアクセスキー。</param>
    /// <param name="secretKey">PA-API のシークレットキー。</param>
    /// <param name="partnerTag">アソシエイトタグ（例 <c>yourtag-22</c>）。リクエストに必須。</param>
    /// <param name="marketplace">マーケットプレイスのドメイン（例 <c>www.amazon.co.jp</c>）。</param>
    /// <param name="host">PA-API ホスト名（日本: <c>webservices.amazon.co.jp</c>）。</param>
    /// <param name="region">AWS リージョン（日本: <c>us-west-2</c>）。</param>
    public PaApiClient(string accessKey, string secretKey, string partnerTag,
                       string marketplace = "www.amazon.co.jp",
                       string host = "webservices.amazon.co.jp",
                       string region = "us-west-2")
    {
        _accessKey = accessKey ?? throw new ArgumentNullException(nameof(accessKey));
        _secretKey = secretKey ?? throw new ArgumentNullException(nameof(secretKey));
        _partnerTag = partnerTag ?? throw new ArgumentNullException(nameof(partnerTag));
        _marketplace = marketplace;
        _host = host;
        _region = region;
    }

    /// <summary>
    /// ASIN を 1 件指定して PA-API GetItems を叩き、商品メタ情報を取得する。
    /// 取得できなければ null を返す。例外（HTTP 失敗・JSON 構造異常）は呼び出し側に伝播。
    /// レスポンスからは ImageURL（Large/Medium）・Title・ByLineInfo・Offers の Price を抽出する。
    /// </summary>
    public async Task<PaItem?> GetItemAsync(string asin, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(asin)) return null;
        var items = await GetItemsAsync(new[] { asin }, ct).ConfigureAwait(false);
        return items.FirstOrDefault();
    }

    /// <summary>
    /// 複数 ASIN を一度に指定して PA-API GetItems を叩く。最大 10 件。
    /// レート制限（1 TPS）の節約のため、複数 ASIN がある場合はこのメソッドでまとめて発射するのが推奨。
    /// </summary>
    public async Task<IReadOnlyList<PaItem>> GetItemsAsync(IReadOnlyList<string> asins, CancellationToken ct = default)
    {
        if (asins == null || asins.Count == 0) return Array.Empty<PaItem>();
        if (asins.Count > 10) throw new ArgumentException("PA-API GetItems は 1 回に最大 10 ASIN まで。", nameof(asins));

        // リクエスト本文の組み立て。Resources は画像と最小限のメタ情報のみ要求する（権限・速度の都合）。
        var body = new
        {
            ItemIds = asins,
            PartnerTag = _partnerTag,
            PartnerType = "Associates",
            Marketplace = _marketplace,
            Resources = new[]
            {
                "Images.Primary.Medium",
                "Images.Primary.Large",
                "ItemInfo.Title",
                "ItemInfo.ByLineInfo",
                "ItemInfo.ProductInfo",
                "Offers.Listings.Price",
            }
        };
        string payload = JsonSerializer.Serialize(body);
        string responseJson = await PostSignedAsync(
            "/paapi5/getitems",
            "com.amazon.paapi5.v1.ProductAdvertisingAPIv1.GetItems",
            payload, ct).ConfigureAwait(false);

        // ItemsResult.Items 配下をパースして DTO に詰める。
        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("ItemsResult", out var itemsResult) ||
            !itemsResult.TryGetProperty("Items", out var itemsEl) ||
            itemsEl.ValueKind != JsonValueKind.Array)
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
    /// キーワード検索を PA-API SearchItems で実行し、最大 <paramref name="itemCount"/> 件の候補を返す。
    /// <paramref name="searchIndex"/> で物理音楽（Music）／デジタル音源（DigitalMusic）を切り替える。
    /// </summary>
    /// <param name="keywords">検索キーワード（商品名など）。</param>
    /// <param name="searchIndex">検索対象カテゴリ。</param>
    /// <param name="itemCount">取得件数（1〜10、PA-API 仕様の上限内）。</param>
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

        // PA-API の SearchIndex 名は文字列で渡す。Music = 物理 CD、DigitalMusic = MP3 配信音源。
        string indexName = searchIndex switch
        {
            PaSearchIndex.Music => "Music",
            PaSearchIndex.DigitalMusic => "DigitalMusic",
            _ => "All",
        };

        var body = new
        {
            Keywords = keywords,
            SearchIndex = indexName,
            ItemCount = itemCount,
            PartnerTag = _partnerTag,
            PartnerType = "Associates",
            Marketplace = _marketplace,
            Resources = new[]
            {
                "Images.Primary.Medium",
                "Images.Primary.Large",
                "ItemInfo.Title",
                "ItemInfo.ByLineInfo",
                "ItemInfo.ProductInfo",
                "Offers.Listings.Price",
            }
        };
        string payload = JsonSerializer.Serialize(body);
        string responseJson = await PostSignedAsync(
            "/paapi5/searchitems",
            "com.amazon.paapi5.v1.ProductAdvertisingAPIv1.SearchItems",
            payload, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(responseJson);
        if (!doc.RootElement.TryGetProperty("SearchResult", out var searchResult) ||
            !searchResult.TryGetProperty("Items", out var itemsEl) ||
            itemsEl.ValueKind != JsonValueKind.Array)
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
    /// 共通の署名付き POST。Authorization / x-amz-date / x-amz-target 等を <see cref="AwsV4Signer"/> から
    /// 受け取って HTTP リクエストヘッダに振り分ける。HTTP 失敗時はステータスと本文を含む例外を投げる。
    /// </summary>
    private async Task<string> PostSignedAsync(string path, string target, string payload, CancellationToken ct)
    {
        var headers = AwsV4Signer.SignPostRequest(
            host: _host,
            region: _region,
            path: path,
            target: target,
            payload: payload,
            accessKey: _accessKey,
            secretKey: _secretKey,
            utcNow: DateTime.UtcNow);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{_host}{path}")
        {
            Content = new StringContent(payload, Encoding.UTF8)
        };
        // Content-Type / Content-Encoding は Content 側に、それ以外は Request 側に振り分ける。
        // HttpClient の規約上、Content 系ヘッダを Headers 側に直接 add すると例外になる。
        req.Content.Headers.Remove("Content-Type");
        req.Content.Headers.TryAddWithoutValidation("Content-Type", headers["content-type"]);
        req.Content.Headers.TryAddWithoutValidation("Content-Encoding", headers["content-encoding"]);
        req.Headers.TryAddWithoutValidation("x-amz-date", headers["x-amz-date"]);
        req.Headers.TryAddWithoutValidation("x-amz-target", headers["x-amz-target"]);
        req.Headers.TryAddWithoutValidation("Authorization", headers["Authorization"]);
        // Host は HttpClient が自動付与するため明示は不要（明示すると例外になる場合あり）。

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        string respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"PA-API request failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{respBody}");
        }
        return respBody;
    }

    /// <summary>レスポンスの 1 商品要素を <see cref="PaItem"/> に変換する。欠落フィールドは null/空のまま。</summary>
    private static PaItem ParseItem(JsonElement item)
    {
        var p = new PaItem
        {
            Asin = item.TryGetProperty("ASIN", out var asinEl) ? asinEl.GetString() ?? "" : "",
            DetailPageUrl = item.TryGetProperty("DetailPageURL", out var dpu) ? dpu.GetString() : null,
        };

        // 画像 URL（Primary.Medium / Primary.Large）
        if (item.TryGetProperty("Images", out var imagesEl)
            && imagesEl.TryGetProperty("Primary", out var primary))
        {
            if (primary.TryGetProperty("Medium", out var mediumEl)
                && mediumEl.TryGetProperty("URL", out var mediumUrl))
            {
                p.MediumImageUrl = mediumUrl.GetString();
            }
            if (primary.TryGetProperty("Large", out var largeEl)
                && largeEl.TryGetProperty("URL", out var largeUrl))
            {
                p.LargeImageUrl = largeUrl.GetString();
            }
        }

        // ItemInfo.Title.DisplayValue / ByLineInfo.Contributors[0].Name / ProductInfo.ReleaseDate
        if (item.TryGetProperty("ItemInfo", out var info))
        {
            if (info.TryGetProperty("Title", out var titleEl)
                && titleEl.TryGetProperty("DisplayValue", out var titleVal))
            {
                p.Title = titleVal.GetString() ?? "";
            }
            if (info.TryGetProperty("ByLineInfo", out var byLine)
                && byLine.TryGetProperty("Contributors", out var contribs)
                && contribs.ValueKind == JsonValueKind.Array
                && contribs.GetArrayLength() > 0)
            {
                var first = contribs[0];
                if (first.TryGetProperty("Name", out var nameEl))
                    p.ByLine = nameEl.GetString();
            }
            if (info.TryGetProperty("ProductInfo", out var prodInfo)
                && prodInfo.TryGetProperty("ReleaseDate", out var rdEl)
                && rdEl.TryGetProperty("DisplayValue", out var rdVal))
            {
                p.ReleaseDate = rdVal.GetString();
            }
        }

        // Offers.Listings[0].Price.DisplayAmount（「¥3,300」のような表示文字列がそのまま入る）
        if (item.TryGetProperty("Offers", out var offers)
            && offers.TryGetProperty("Listings", out var listings)
            && listings.ValueKind == JsonValueKind.Array
            && listings.GetArrayLength() > 0)
        {
            var listing0 = listings[0];
            if (listing0.TryGetProperty("Price", out var priceEl)
                && priceEl.TryGetProperty("DisplayAmount", out var priceVal))
            {
                p.PriceDisplay = priceVal.GetString();
            }
        }

        return p;
    }
}
