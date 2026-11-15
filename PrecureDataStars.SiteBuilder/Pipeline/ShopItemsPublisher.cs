using System.Security.Cryptography;
using Amazon;
using Amazon.CloudFront;
using Amazon.CloudFront.Model;
using Amazon.S3;
using Amazon.S3.Model;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Deploy;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 販売中の商品のファイル（<see cref="ShopItemsFile"/>）だけを本番に上げ直す起動方法（<c>--publish-shop-items</c>）。
/// サイトは作らない。DB の行から全ファイルを組み立て、本番のファイルと中身が違うものだけを S3 に上げて、
/// CloudFront のそのパスのキャッシュを消す。どれも同じなら何もしない。本番にあって今回作らなかったファイルは消さない
/// （次の全体のデプロイが片付ける）。
/// 定時の点検（<c>scripts/scheduled-shop-check.ps1</c>）が、Amazon に確かめた直後に呼ぶ。
/// </summary>
internal static class ShopItemsPublisher
{
    /// <summary>ファイルを上げ直す。<paramref name="dryRun"/> なら、上げ直すファイルを表示するだけ。</summary>
    public static async Task<int> RunAsync(bool dryRun)
    {
        var config = BuildConfig.FromAppConfig(isProductionMode: true, DeployRuntimeOptions.None, pageFilter: "");
        if (string.IsNullOrWhiteSpace(config.AwsS3Bucket) || string.IsNullOrWhiteSpace(config.AwsRegion))
            throw new InvalidOperationException("App.config の AwsS3Bucket / AwsRegion が未設定です。");

        var factory = MySqlConnectionFactory.FromConnectionString(config.ConnectionString);
        var repo = new SeriesShopItemsRepository(factory);
        var rows = await repo.GetEnabledAsync().ConfigureAwait(false);
        var products = await repo.GetProductCandidatesAsync().ConfigureAwait(false);
        var files = ShopItemsFile.BuildFiles(rows, products, config.AmazonAssociateTag ?? "");
        Console.WriteLine($"販売中の商品: 枠のファイル {files.Count} 個（手で入れた行 {rows.Count} 件・候補の音楽商品 {products.Count} 件）。");

        var region = RegionEndpoint.GetBySystemName(config.AwsRegion);
        var credentials = S3DeployService.ResolveCredentials(config.AwsProfile);
        using var s3 = credentials is null ? new AmazonS3Client(region) : new AmazonS3Client(credentials, region);

        // 本番のファイルの ETag（1 回の PUT で上げたファイルは MD5）を、フォルダの一覧でまとめて引く。
        var etags = new Dictionary<string, string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var list = await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = config.AwsS3Bucket,
                Prefix = ShopItemsFile.DirectoryName + "/",
                ContinuationToken = token,
            }).ConfigureAwait(false);
            foreach (var o in list.S3Objects ?? new List<S3Object>())
                etags[o.Key] = (o.ETag ?? "").Trim('"').ToLowerInvariant();
            token = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (token != null);

        var changed = new List<(string Key, byte[] Bytes)>();
        foreach (var (key, json) in files)
        {
            var bytes = ShopItemsFile.ToBytes(json);
            string md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
            if (!etags.TryGetValue(key, out var etag) || !string.Equals(etag, md5, StringComparison.Ordinal))
                changed.Add((key, bytes));
        }
        if (changed.Count == 0)
        {
            Console.WriteLine("本番のファイルと同じです。上げ直しません。");
            return 0;
        }
        foreach (var (key, _) in changed) Console.WriteLine($"  本番と違う: {key}");
        if (dryRun)
        {
            Console.WriteLine($"本番のファイルと違います（{changed.Count} 個。--dry-run のため上げ直しません）。");
            return 0;
        }

        foreach (var (key, bytes) in changed)
        {
            using var stream = new MemoryStream(bytes);
            var req = new PutObjectRequest
            {
                BucketName = config.AwsS3Bucket,
                Key = key,
                InputStream = stream,
                ContentType = S3DeployService.GuessContentType(key),
            };
            req.Headers.CacheControl = S3DeployService.CacheControlFor(key);
            await s3.PutObjectAsync(req).ConfigureAwait(false);
        }
        Console.WriteLine($"上げ直しました: {changed.Count} 個");

        if (string.IsNullOrWhiteSpace(config.CloudFrontDistributionId))
        {
            Console.WriteLine("CloudFrontDistributionId が未設定のため、キャッシュは消していません。");
            return 0;
        }
        // 変わったファイルが多いときは、フォルダごと 1 本で消す。
        var paths = changed.Count > 20
            ? new List<string> { "/" + ShopItemsFile.DirectoryName + "/*" }
            : changed.Select(c => "/" + c.Key).ToList();
        using var cloudFront = credentials is null
            ? new AmazonCloudFrontClient(RegionEndpoint.USEast1)
            : new AmazonCloudFrontClient(credentials, RegionEndpoint.USEast1);
        var resp = await cloudFront.CreateInvalidationAsync(new CreateInvalidationRequest
        {
            DistributionId = config.CloudFrontDistributionId,
            InvalidationBatch = new InvalidationBatch
            {
                CallerReference = Guid.NewGuid().ToString("N"),
                Paths = new Paths { Quantity = paths.Count, Items = paths },
            },
        }).ConfigureAwait(false);
        Console.WriteLine($"キャッシュを消しました: {resp.Invalidation?.Id}");
        return 0;
    }
}
