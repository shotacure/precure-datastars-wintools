using System.Security.Cryptography;
using Amazon;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using Amazon.S3;
using Amazon.S3.Model;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Deploy;

/// <summary>
/// 本番ビルド成果物（<see cref="BuildConfig.OutputDirectory"/> 配下）を S3 バケットへ
/// 「差分のみ」同期し、変更パスに対して CloudFront のキャッシュ無効化を発行するサービス。
/// <list type="bullet">
///   <item>変更判定は S3 の ETag（単発 PUT では MD5 と一致）とローカルファイルの MD5 比較で行い、
///     内容が変わったファイルだけをアップロードする（全 3,000 件規模の再アップを避ける）。</item>
///   <item>生成物に存在しない S3 オブジェクトは orphan として削除する（保護接頭辞は除外）。</item>
///   <item>Content-Type / Cache-Control を拡張子から付与する。</item>
///   <item>変更パスだけを invalidation する（件数が多ければ <c>/*</c> 1 本にフォールバック）。</item>
/// </list>
/// 認証は <see cref="BuildConfig.AwsProfile"/> の名前付きプロファイル（鍵はリポジトリ外の
/// <c>~/.aws/credentials</c>）を使う。<c>--dry-run</c> では一切変更せず計画のみ表示し、
/// 削除がある場合は実行前に対話確認する（<c>--yes</c> で省略）。
/// </summary>
public sealed class S3DeployService
{
    private readonly BuildConfig _config;
    private readonly BuildLogger _logger;

    /// <summary>
    /// invalidation を <c>/*</c> に倒す変更パス数の閾値。これを超えたら個別指定をやめて
    /// ワイルドカード 1 本（課金上 1 パス・最安）にフォールバックする。少数のときは個別指定で
    /// エッジキャッシュをできるだけ温存する。
    /// </summary>
    private const int InvalidationWildcardThreshold = 100;

    /// <summary>並列アップロードの同時実行数。S3 への PUT は CPU ではなくリクエスト往復律速のため、
    /// 同時数を上げると（ローカルの上り帯域が底になるまで）ほぼ線形に短縮する。S3 はこの程度の
    /// 同時 PUT を容易に捌くため、フル更新（数千ファイル）の所要時間を詰める狙いで高めに取る。</summary>
    private const int UploadConcurrency = 64;

    /// <summary>
    /// S3 の一覧をフォルダごとに分けて同時に引く深さ（<see cref="ListPrefixAsync"/>）。1 ならルートの直下のフォルダごとに分ける。
    /// 1 回の往復は 0.1〜0.4 秒で、深く分けると要求が数千に増えて詰まり、かえって遅い。
    /// </summary>
    private const int ListSplitDepth = 1;

    public S3DeployService(BuildConfig config, BuildLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 差分同期 ＋ invalidation を実行する。戻り値は「本番がこのビルドの出力と一致した」か
    /// （反映した・もともと差分が無かったときは true、dry-run・確認で中止したときは false）。
    /// </summary>
    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        _logger.Section("Deploy to S3 + CloudFront");

        var bucket = _config.AwsS3Bucket;
        var regionName = _config.AwsRegion;
        if (string.IsNullOrWhiteSpace(bucket))
            throw new InvalidOperationException("AwsS3Bucket が未設定です。App.config の appSettings に AwsS3Bucket を設定してください。");
        if (string.IsNullOrWhiteSpace(regionName))
            throw new InvalidOperationException("AwsRegion が未設定です。App.config の appSettings に AwsRegion（例: ap-northeast-1）を設定してください。");

        var outputDir = _config.OutputDirectory;
        if (!Directory.Exists(outputDir))
            throw new InvalidOperationException($"出力ディレクトリが見つかりません: {outputDir}");

        var region = RegionEndpoint.GetBySystemName(regionName);
        var credentials = ResolveCredentials(_config.AwsProfile);

        // S3 はバケットと同じリージョン、CloudFront はグローバルサービスのため us-east-1 を慣例採用。
        using var s3 = credentials is null
            ? new AmazonS3Client(region)
            : new AmazonS3Client(credentials, region);
        using var cloudFront = credentials is null
            ? new AmazonCloudFrontClient(RegionEndpoint.USEast1)
            : new AmazonCloudFrontClient(credentials, RegionEndpoint.USEast1);

        _logger.Info($"Bucket           : {bucket} ({regionName})");
        _logger.Info($"Profile          : {(string.IsNullOrWhiteSpace(_config.AwsProfile) ? "(default chain)" : _config.AwsProfile)}");
        _logger.Info($"Distribution     : {(string.IsNullOrWhiteSpace(_config.CloudFrontDistributionId) ? "(invalidation skip)" : _config.CloudFrontDistributionId)}");
        _logger.Info($"Mode             : {(_config.Deploy.DryRun ? "DRY-RUN（無変更）" : "LIVE")}");

        // 1) S3 側の現状（key → ETag）の全件と、ローカル成果物の MD5 を同時に求める（どちらも待ちが主で互いに独立）。
        var listWatch = System.Diagnostics.Stopwatch.StartNew();
        var listing = ListAllObjectsAsync(s3, bucket, ct).ContinueWith(t => { listWatch.Stop(); return t; }, ct).Unwrap();
        var md5Watch = System.Diagnostics.Stopwatch.StartNew();
        var localMd5 = await Task.Run(() => ComputeLocalMd5(outputDir), ct).ConfigureAwait(false);
        md5Watch.Stop();
        var s3Etags = await listing.ConfigureAwait(false);
        _logger.Info($"Compare          : S3 の一覧 {s3Etags.Count} 件 {listWatch.Elapsed.TotalSeconds:0.0} 秒 / 手元の MD5 {localMd5.Count} 件 {md5Watch.Elapsed.TotalSeconds:0.0} 秒（同時に進める）");

        // 2) アップロード対象（新規 or 内容変化）を抽出。並びはローカルのパスの順（ログと計画を毎回同じ順にする）。
        var localKeys = new HashSet<string>(StringComparer.Ordinal);
        var toUpload = new List<(string Key, string LocalPath)>();
        foreach (var (key, path, md5) in localMd5)
        {
            localKeys.Add(key);
            // ETag が無い（新規）／マルチパート（'-' を含み MD5 と比較不能）／不一致ならアップロード。
            if (!s3Etags.TryGetValue(key, out var etag) || etag.Contains('-') || !string.Equals(etag, md5, StringComparison.Ordinal))
                toUpload.Add((key, path));
        }

        // 3) ローカルに存在しない S3 キーは orphan。保護接頭辞に該当するものは除外。
        var protectedPrefixes = _config.AwsDeployProtectedPrefixes;
        var toDelete = s3Etags.Keys
            .Where(k => !localKeys.Contains(k))
            .Where(k => !IsProtected(k, protectedPrefixes))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        // ピンポイントビルド（--page）では出力が部分的なので、「ローカルに無い＝S3 から消す」が成立しない。
        // 既存ページを誤って削除しないよう、削除はまるごとスキップしてアップロードのみ行う。
        if (!string.IsNullOrEmpty(_config.PageFilter) && toDelete.Count > 0)
        {
            _logger.Info($"ピンポイントモード（--page {_config.PageFilter}）のため、削除候補 {toDelete.Count} 件はスキップします。");
            toDelete = new List<string>();
        }

        var unchanged = localKeys.Count - toUpload.Count;

        // 4) 計画表示。
        _logger.Info($"Plan             : upload {toUpload.Count} / delete {toDelete.Count} / unchanged {unchanged}");
        if (toDelete.Count > 0)
        {
            _logger.Info("Delete (orphan) targets:");
            foreach (var k in toDelete)
                _logger.Info($"  - {k}");
        }

        if (toUpload.Count == 0 && toDelete.Count == 0)
        {
            _logger.Success("S3 は最新です（差分なし）。invalidation も不要。");
            return true;
        }

        // 5) dry-run はここで終了（S3 / CloudFront を一切変更しない）。
        if (_config.Deploy.DryRun)
        {
            _logger.Success("DRY-RUN のため変更は行いませんでした。");
            return false;
        }

        // 6) 破壊的操作（削除）がある場合は対話確認。--yes で省略。
        if (toDelete.Count > 0 && !_config.Deploy.SkipConfirm)
        {
            Console.Write($"本番バケット '{bucket}' に反映します（アップロード {toUpload.Count} / 削除 {toDelete.Count}）。続行しますか? [y/N]: ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warn("デプロイを中止しました（確認で N）。");
                return false;
            }
        }

        // 7) アップロード（並列）。
        if (toUpload.Count > 0)
        {
            await UploadAllAsync(s3, bucket, toUpload, ct).ConfigureAwait(false);
            _logger.Success($"アップロード完了: {toUpload.Count} ファイル");
        }

        // 8) orphan 削除（最大 1000 件/バッチ）。
        if (toDelete.Count > 0)
        {
            await DeleteAllAsync(s3, bucket, toDelete, ct).ConfigureAwait(false);
            _logger.Success($"削除完了: {toDelete.Count} オブジェクト");
        }

        // 9) CloudFront invalidation（変更パスのみ。多ければ /* にフォールバック）。
        await InvalidateAsync(cloudFront, toUpload.Select(u => u.Key).Concat(toDelete), ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>名前付きプロファイルから資格情報を解決する。プロファイル未指定なら null を返し、
    /// 呼び出し側で SDK 既定の資格情報解決チェーンに委ねる。</summary>
    private static AWSCredentials? ResolveCredentials(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            return null;

        var chain = new CredentialProfileStoreChain();
        if (!chain.TryGetAWSCredentials(profile, out var creds))
            throw new InvalidOperationException(
                $"AWS プロファイル '{profile}' が見つかりません。" +
                $"`aws configure --profile {profile}` で作成してください（鍵は ~/.aws/credentials に保存されます）。");
        return creds;
    }

    /// <summary>
    /// バケット内の全オブジェクトを列挙し、key → 正規化 ETag の辞書を返す。
    /// 1 回の一覧は 1,000 件までで、続きは前の応答の印が要るため、1 本の列挙では往復を順に待つしかない。
    /// そこで接頭辞（「/」で区切ったフォルダ）ごとに分けて同時に列挙する（<see cref="ListPrefixAsync"/>）。
    /// </summary>
    private static async Task<Dictionary<string, string>> ListAllObjectsAsync(IAmazonS3 s3, string bucket, CancellationToken ct)
    {
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        await ListPrefixAsync(s3, bucket, "", depth: 0, result, ct).ConfigureAwait(false);
        return new Dictionary<string, string>(result, StringComparer.Ordinal);
    }

    /// <summary>
    /// 接頭辞 <paramref name="prefix"/> の下を列挙して <paramref name="result"/> に足す。上から <see cref="ListSplitDepth"/> 段までは、
    /// まず「/」区切りで 1 段だけ引き、1 回の応答に収まったら（その段の直下のファイルと子のフォルダが 1,000 件未満）、
    /// 子のフォルダをそれぞれ同時に同じ要領で引く。それより深い段と、収まらない段は、区切らずにその接頭辞の下を全件順に引く。
    /// </summary>
    private static async Task ListPrefixAsync(
        IAmazonS3 s3, string bucket, string prefix, int depth,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> result, CancellationToken ct)
    {
        if (depth >= ListSplitDepth)
        {
            await ListFlatAsync(s3, bucket, prefix, result, ct).ConfigureAwait(false);
            return;
        }
        var level = await s3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = bucket, Prefix = prefix, Delimiter = "/" }, ct).ConfigureAwait(false);
        if (level.IsTruncated ?? false)
        {
            await ListFlatAsync(s3, bucket, prefix, result, ct).ConfigureAwait(false);
            return;
        }

        if (level.S3Objects is not null)
            foreach (var o in level.S3Objects)
                result[o.Key] = NormalizeEtag(o.ETag);
        var children = level.CommonPrefixes ?? new List<string>();
        await Task.WhenAll(children.Select(child => ListPrefixAsync(s3, bucket, child, depth + 1, result, ct))).ConfigureAwait(false);
    }

    /// <summary>接頭辞の下を区切らずに全件、続きの印をたどって順に引く。</summary>
    private static async Task ListFlatAsync(
        IAmazonS3 s3, string bucket, string prefix,
        System.Collections.Concurrent.ConcurrentDictionary<string, string> result, CancellationToken ct)
    {
        string? token = null;
        do
        {
            var resp = await s3.ListObjectsV2Async(
                new ListObjectsV2Request { BucketName = bucket, Prefix = prefix, ContinuationToken = token }, ct).ConfigureAwait(false);
            if (resp.S3Objects is not null)
                foreach (var o in resp.S3Objects)
                    result[o.Key] = NormalizeEtag(o.ETag);
            token = (resp.IsTruncated ?? false) ? resp.NextContinuationToken : null;
        }
        while (token is not null);
    }

    /// <summary>
    /// 出力ディレクトリの全ファイルの (S3 のキー, パス, MD5) を、パスの順に返す。MD5 は論理コア数で並列に求める。
    /// </summary>
    private static List<(string Key, string Path, string Md5)> ComputeLocalMd5(string outputDir)
    {
        var paths = Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories).ToArray();
        var md5s = new string[paths.Length];
        Parallel.For(0, paths.Length, i => md5s[i] = ComputeMd5Hex(paths[i]));
        return paths
            .Select((path, i) => (Path.GetRelativePath(outputDir, path).Replace('\\', '/'), path, md5s[i]))
            .ToList();
    }

    /// <summary>アップロード対象を並列で PutObject する。</summary>
    private static async Task UploadAllAsync(
        IAmazonS3 s3, string bucket, IReadOnlyList<(string Key, string LocalPath)> items, CancellationToken ct)
    {
        await Parallel.ForEachAsync(
            items,
            new ParallelOptions { MaxDegreeOfParallelism = UploadConcurrency, CancellationToken = ct },
            async (item, c) =>
            {
                var req = new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = item.Key,
                    FilePath = item.LocalPath,
                    ContentType = GuessContentType(item.Key),
                };
                req.Headers.CacheControl = CacheControlFor(item.Key);
                await s3.PutObjectAsync(req, c).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }

    /// <summary>orphan を最大 1000 件ずつまとめて削除する。</summary>
    private static async Task DeleteAllAsync(IAmazonS3 s3, string bucket, IReadOnlyList<string> keys, CancellationToken ct)
    {
        const int batchSize = 1000;
        for (int i = 0; i < keys.Count; i += batchSize)
        {
            var batch = keys.Skip(i).Take(batchSize).Select(k => new KeyVersion { Key = k }).ToList();
            await s3.DeleteObjectsAsync(
                new DeleteObjectsRequest { BucketName = bucket, Objects = batch }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>変更キー集合から invalidation パスを組み立てて発行する。</summary>
    private async Task InvalidateAsync(IAmazonCloudFront cloudFront, IEnumerable<string> changedKeys, CancellationToken ct)
    {
        var distributionId = _config.CloudFrontDistributionId;
        if (string.IsNullOrWhiteSpace(distributionId))
        {
            _logger.Warn("CloudFrontDistributionId が未設定のため invalidation をスキップしました。");
            return;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in changedKeys)
            foreach (var p in InvalidationPathsForKey(key))
                paths.Add(p);

        if (paths.Count == 0)
            return;

        // 変更パスが多いときはワイルドカード 1 本に倒す（課金は 1 パス・全エッジ更新）。
        List<string> items;
        if (paths.Count > InvalidationWildcardThreshold)
        {
            items = new List<string> { "/*" };
            _logger.Info($"Invalidation     : {paths.Count} 件 → /* にフォールバック");
        }
        else
        {
            items = paths.OrderBy(p => p, StringComparer.Ordinal).ToList();
            _logger.Info($"Invalidation     : {items.Count} パス");
        }

        var resp = await cloudFront.CreateInvalidationAsync(new CreateInvalidationRequest
        {
            DistributionId = distributionId,
            InvalidationBatch = new InvalidationBatch
            {
                // 一意であればよい（再送防止用の冪等キー）。
                CallerReference = Guid.NewGuid().ToString("N"),
                Paths = new Paths { Quantity = items.Count, Items = items },
            },
        }, ct).ConfigureAwait(false);

        _logger.Success($"invalidation 発行: {resp.Invalidation?.Id} ({resp.Invalidation?.Status})");
    }

    /// <summary>1 つのキーに対応する invalidation パスを返す。
    /// CloudFront Function が末尾スラッシュ／拡張子なしを <c>index.html</c> に書き換えるため、
    /// <c>foo/index.html</c> は実体の <c>/foo/index.html</c> に加えてディレクトリ形 <c>/foo/</c> も対象にする。</summary>
    private static IEnumerable<string> InvalidationPathsForKey(string key)
    {
        // 名前ベースのキー（people/高橋任治/index.html）は非 ASCII を含むため、パスはパーセントエンコードして渡す。
        key = Utilities.PathUtil.EncodePath(key);
        yield return "/" + key;

        const string index = "index.html";
        if (string.Equals(key, index, StringComparison.Ordinal))
        {
            yield return "/";
        }
        else if (key.EndsWith("/" + index, StringComparison.Ordinal))
        {
            // "foo/index.html" → "/foo/"
            yield return "/" + key[..^index.Length];
        }
    }

    /// <summary>ETag のダブルクオートを除去し小文字化する（MD5 16 進と比較するため）。</summary>
    private static string NormalizeEtag(string? etag) => (etag ?? "").Trim('"').ToLowerInvariant();

    /// <summary>ファイルの MD5 を小文字 16 進で返す。</summary>
    private static string ComputeMd5Hex(string path)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(path);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>キーが保護接頭辞のいずれかに前方一致するか。</summary>
    private static bool IsProtected(string key, IReadOnlyList<string> protectedPrefixes)
    {
        for (int i = 0; i < protectedPrefixes.Count; i++)
            if (key.StartsWith(protectedPrefixes[i], StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>拡張子から Content-Type を推定する。テキスト系は charset=utf-8 を付ける。</summary>
    private static string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".pdf" => "application/pdf",
            ".webmanifest" => "application/manifest+json",
            ".map" => "application/json",
            _ => "application/octet-stream",
        };
    }

    /// <summary>拡張子に応じた Cache-Control を返す。
    /// HTML は短命（更新を早く反映）、sitemap / robots / 検索インデックス等も短命寄り、
    /// その他の静的アセット（css / js / 画像 / フォント）は 1 日。固定ファイル名のため、
    /// 更新はデプロイ時 invalidation でエッジへ押し出す。</summary>
    private static string CacheControlFor(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        if (ext == ".html")
            return "public, max-age=60";
        if (ext is ".xml" or ".txt" or ".json")
            return "public, max-age=300";
        return "public, max-age=86400";
    }
}
