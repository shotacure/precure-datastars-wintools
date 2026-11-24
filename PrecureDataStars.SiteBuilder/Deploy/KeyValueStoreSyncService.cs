using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.CloudFrontKeyValueStore;
using Amazon.CloudFrontKeyValueStore.Model;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Deploy;

/// <summary>
/// 旧 URL の転送表を、CloudFront の KeyValueStore へ差分で同期するサービス。
/// <para>
/// 転送表は、ビルドがサイト出力の <c>_edge/legacy-redirects.json</c>（<see cref="LegacyRedirectMapWriter"/>）に書き出したものを読む。
/// 直前のドライランの出力をそのまま上げるデプロイ（作り直さない）でも、同じ表を使える。
/// viewer-request の CloudFront Function（<c>scripts/cloudfront/viewer-request.js</c>）が、この KeyValueStore を引いて 301 を返す。
/// </para>
/// <list type="bullet">
///   <item><description>同期先は App.config の <c>CloudFrontKeyValueStoreArn</c>。空なら何もしない。</description></item>
///   <item><description>キーは ASCII だけにする（<see cref="ToStoreKey(string)"/>）。Function の側も同じ形にしてから引く。
///     名前に濁点・半濁点などがある行は、合成前の形（NFD）のキーも並べて入れる。</description></item>
///   <item><description>KeyValueStore の今の中身を一覧し、増えた・値が変わったキーを書き、表から消えたキーを消す。
///     ドライランでは件数を出すだけで書かない。</description></item>
///   <item><description>ピンポイントビルド（<c>--page</c>）の出力には転送表の全体が無いことがあるので、同期しない。</description></item>
/// </list>
/// 認証は <see cref="BuildConfig.AwsProfile"/> の名前付きプロファイル。KeyValueStore の API は署名に SigV4A を使うため、
/// <c>AWSSDK.Extensions.CrtIntegration</c> を参照に入れている。
/// </summary>
public sealed class KeyValueStoreSyncService
{
    /// <summary>1 回の <c>UpdateKeys</c> で書き換えられるキーの数の上限。</summary>
    private const int UpdateBatchSize = 50;

    /// <summary>KeyValueStore のキーの長さの上限（バイト）。</summary>
    private const int MaxKeyBytes = 512;

    /// <summary>KeyValueStore の値の長さの上限（バイト）。</summary>
    private const int MaxValueBytes = 1024;

    private readonly BuildConfig _config;
    private readonly BuildLogger _logger;

    public KeyValueStoreSyncService(BuildConfig config, BuildLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 転送表のキー（<c>/people/旧名</c> のような、末尾スラッシュ無しでデコード済みの旧パス）を、KeyValueStore のキーにする。
    /// 最後の区切りより後ろ（名前・数字・コード）をパーセントエンコードして、ASCII だけの文字列にする。
    /// 英数字と <c>- . _ ~</c> 以外をすべて <c>%XX</c>（大文字）にする形で、Function の側も同じ結果になるよう組む。
    /// </summary>
    public static string ToStoreKey(string fromPath) => ToStoreKey(fromPath, NormalizationForm.FormC);

    /// <summary>
    /// <see cref="ToStoreKey(string)"/> の、合成前の形（NFD。濁点・半濁点が分かれた形）で作ったキー。
    /// Function のランタイムは文字の正規化ができないので、NFD のまま届いた旧 URL も引けるよう、
    /// NFC のキーと形が違うものだけ同じ転送先で並べて持つ。
    /// </summary>
    public static string ToDecomposedStoreKey(string fromPath) => ToStoreKey(fromPath, NormalizationForm.FormD);

    private static string ToStoreKey(string fromPath, NormalizationForm form)
    {
        int slash = fromPath.LastIndexOf('/');
        return slash < 0
            ? Uri.EscapeDataString(fromPath.Normalize(form))
            : fromPath[..(slash + 1)] + Uri.EscapeDataString(fromPath[(slash + 1)..].Normalize(form));
    }

    /// <summary>転送表を KeyValueStore へ同期する。同期先が未設定・表が無い・ピンポイントビルドのときは何もしない。</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var arn = _config.CloudFrontKeyValueStoreArn;
        if (string.IsNullOrWhiteSpace(arn)) return;

        if (!string.IsNullOrEmpty(_config.PageFilter))
        {
            _logger.Info("Redirect store   : ピンポイントモードのため、転送表の同期はスキップします。");
            return;
        }

        var mapPath = Path.Combine(_config.OutputDirectory,
            LegacyRedirectMapWriter.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(mapPath))
        {
            _logger.Warn($"転送表が見つからないため、KeyValueStore の同期をスキップしました: {mapPath}");
            return;
        }

        // 手元の転送表を KeyValueStore のキーの形にする。上限を超える行は書かずに知らせる。
        var source = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(mapPath, ct).ConfigureAwait(false))
                     ?? new Dictionary<string, string>();
        var desired = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (from, to) in source)
        {
            // NFC のキーと、形が違うときだけ NFD のキーも同じ転送先で入れる。
            foreach (var key in new[] { ToStoreKey(from), ToDecomposedStoreKey(from) }.Distinct(StringComparer.Ordinal))
            {
                if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes || Encoding.UTF8.GetByteCount(to) > MaxValueBytes)
                {
                    _logger.Warn($"転送表の行が KeyValueStore の上限を超えるため、同期しません: {from}");
                    continue;
                }
                desired[key] = to;
            }
        }

        var credentials = S3DeployService.ResolveCredentials(_config.AwsProfile);
        using var client = credentials is null
            ? new AmazonCloudFrontKeyValueStoreClient(RegionEndpoint.USEast1)
            : new AmazonCloudFrontKeyValueStoreClient(credentials, RegionEndpoint.USEast1);

        // いまの中身を全件読む。
        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        string? next = null;
        do
        {
            var page = await client.ListKeysAsync(new ListKeysRequest { KvsARN = arn, NextToken = next, MaxResults = 50 }, ct).ConfigureAwait(false);
            foreach (var item in page.Items ?? new List<ListKeysResponseListItem>())
                current[item.Key] = item.Value;
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));

        var puts = desired
            .Where(kv => !current.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal))
            .Select(kv => new PutKeyRequestListItem { Key = kv.Key, Value = kv.Value })
            .ToList();
        var deletes = current.Keys
            .Where(k => !desired.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new DeleteKeyRequestListItem { Key = k })
            .ToList();

        _logger.Info($"Redirect store   : 転送表 {source.Count} 件（キー {desired.Count} 件）/ 書く {puts.Count} / 消す {deletes.Count} / 変わらず {desired.Count - puts.Count}");
        if (puts.Count == 0 && deletes.Count == 0) return;

        if (_config.Deploy.DryRun)
        {
            _logger.Info("Redirect store   : DRY-RUN のため KeyValueStore は書き換えません。");
            return;
        }

        // 書き換えは 50 キーずつ。応答の ETag を次の回の IfMatch に渡す。
        var etag = (await client.DescribeKeyValueStoreAsync(new DescribeKeyValueStoreRequest { KvsARN = arn }, ct).ConfigureAwait(false)).ETag;
        int p = 0, d = 0;
        while (p < puts.Count || d < deletes.Count)
        {
            var putBatch = puts.Skip(p).Take(UpdateBatchSize).ToList();
            var deleteBatch = deletes.Skip(d).Take(UpdateBatchSize - putBatch.Count).ToList();
            var resp = await client.UpdateKeysAsync(new UpdateKeysRequest
            {
                KvsARN = arn,
                IfMatch = etag,
                Puts = putBatch,
                Deletes = deleteBatch,
            }, ct).ConfigureAwait(false);
            etag = resp.ETag;
            p += putBatch.Count;
            d += deleteBatch.Count;
        }
        _logger.Success($"転送表を KeyValueStore へ同期しました（書いた {puts.Count} / 消した {deletes.Count}）。");
    }
}
