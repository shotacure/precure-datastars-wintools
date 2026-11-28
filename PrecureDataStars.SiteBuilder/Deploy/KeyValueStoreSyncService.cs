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
/// viewer-request の CloudFront Function（コードはリポジトリの外で管理する）が、この KeyValueStore を引いて 301 を返す。
/// </para>
/// <list type="bullet">
///   <item><description>同期先は App.config の <c>CloudFrontKeyValueStoreArn</c>。空なら何もしない。</description></item>
///   <item><description>キーは ASCII だけにする（<see cref="ToStoreKey(string)"/>）。Function の側も同じ形にしてから引く。
///     名前に濁点・半濁点などがある行は、合成前の形（NFD）のキーも並べて入れる。</description></item>
///   <item><description>KeyValueStore の今の中身を一覧し、増えた・値が変わったキーを書き、表から消えたキーを消す。
///     ドライランでは件数を出すだけで書かない。</description></item>
///   <item><description>中身の一覧（50 キーずつの読み出し）は、前に同期を確かめたときから何も変わっていなければ飛ばす。
///     確かめたときの「手元の転送表の印（SHA-256）」と「KeyValueStore の版の印（ETag）と件数」を、出力の隣の
///     <c>….kvs-sync.json</c>（出力の外なので上げない）に残し、次の同期では KeyValueStore の版の印と件数だけを 1 回で問い合わせて、
///     3 つとも同じなら読まない。どれかが違う（表が変わった・KeyValueStore が手で直された）か、控えが無い・読めないときは全部読む。
///     控えを残すのは、全部読んで差分が無かったとき（ドライランでも）と、書き終えたときだけ。</description></item>
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

    /// <summary>前に同期を確かめたときの控え（<see cref="RecordPathFor"/>）。</summary>
    private sealed class SyncRecord
    {
        public string Arn { get; set; } = "";
        public string TableHash { get; set; } = "";
        public string ETag { get; set; } = "";
        public long ItemCount { get; set; } = -1;
    }

    /// <summary>同期の控えのパス（出力の隣。出力のフォルダ名の後ろに <c>.kvs-sync.json</c> を付けたファイル）。</summary>
    public static string RecordPathFor(string outputDirectory)
        => outputDirectory.TrimEnd('/', '\\') + ".kvs-sync.json";

    private static SyncRecord? LoadRecord(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<SyncRecord>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private void SaveRecord(string path, SyncRecord record)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(record));
        }
        catch (Exception ex)
        {
            // 残せなくても、次の同期で全部読むだけ。
            _logger.Warn($"転送表の同期の控えを書けませんでした（次の同期は全部読みます）: {ex.Message}");
        }
    }

    /// <summary>KeyValueStore に入れるキーと値の組の印（同期先の ARN も含める）。</summary>
    private static string TableHashOf(string arn, SortedDictionary<string, string> desired)
    {
        var sb = new StringBuilder(arn).Append('\n');
        foreach (var (k, v) in desired) sb.Append(k).Append('\t').Append(v).Append('\n');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

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

        // 前に同期を確かめたときから、手元の表も KeyValueStore も変わっていなければ、中身を読まずに終える。
        string recordPath = RecordPathFor(_config.OutputDirectory);
        string tableHash = TableHashOf(arn, desired);
        var describe = await client.DescribeKeyValueStoreAsync(new DescribeKeyValueStoreRequest { KvsARN = arn }, ct).ConfigureAwait(false);
        long storeCount = (long?)describe.ItemCount ?? -1;
        var record = LoadRecord(recordPath);
        if (record is not null
            && string.Equals(record.Arn, arn, StringComparison.Ordinal)
            && string.Equals(record.TableHash, tableHash, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(describe.ETag) && string.Equals(record.ETag, describe.ETag, StringComparison.Ordinal)
            && storeCount >= 0 && record.ItemCount == storeCount)
        {
            _logger.Info($"Redirect store   : 転送表 {source.Count} 件（キー {desired.Count} 件）/ 前の同期から転送表も KeyValueStore も変わっていないので、中身を読まずに終えます。");
            return;
        }

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
        if (puts.Count == 0 && deletes.Count == 0)
        {
            SaveRecord(recordPath, new SyncRecord { Arn = arn, TableHash = tableHash, ETag = describe.ETag ?? "", ItemCount = storeCount });
            return;
        }

        if (_config.Deploy.DryRun)
        {
            _logger.Info("Redirect store   : DRY-RUN のため KeyValueStore は書き換えません。");
            return;
        }

        // 書き換えは 50 キーずつ。応答の ETag を次の回の IfMatch に渡す。
        var etag = describe.ETag;
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
        // 書いた後の版の印と件数を控える（件数の反映が遅れて次の問い合わせと合わなければ、次の同期で 1 度全部読んで控え直す）。
        SaveRecord(recordPath, new SyncRecord { Arn = arn, TableHash = tableHash, ETag = etag ?? "", ItemCount = desired.Count });
    }
}
