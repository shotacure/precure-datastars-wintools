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
/// サイトは作らない。DB の行からファイルを組み立て、本番のファイルと中身が違うときだけ S3 に上げて、
/// CloudFront のそのパスのキャッシュを消す。同じなら何もしない。
/// 定時の点検（<c>scripts/scheduled-shop-check.ps1</c>）が、Amazon に確かめた直後に呼ぶ。
/// </summary>
internal static class ShopItemsPublisher
{
    /// <summary>ファイルを上げ直す。<paramref name="dryRun"/> なら、上げ直すかどうかを表示するだけ。</summary>
    public static async Task<int> RunAsync(bool dryRun)
    {
        var config = BuildConfig.FromAppConfig(isProductionMode: true, DeployRuntimeOptions.None, pageFilter: "");
        if (string.IsNullOrWhiteSpace(config.AwsS3Bucket) || string.IsNullOrWhiteSpace(config.AwsRegion))
            throw new InvalidOperationException("App.config の AwsS3Bucket / AwsRegion が未設定です。");

        var factory = MySqlConnectionFactory.FromConnectionString(config.ConnectionString);
        var rows = await new SeriesShopItemsRepository(factory).GetEnabledAsync().ConfigureAwait(false);
        var bytes = ShopItemsFile.BuildBytes(rows, config.AmazonAssociateTag ?? "");
        string md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        int shown = rows.Count(r => r.IsBuyable == true);
        Console.WriteLine($"販売中の商品: 出す設定 {rows.Count} 件のうち、売られている {shown} 件。");

        var region = RegionEndpoint.GetBySystemName(config.AwsRegion);
        var credentials = S3DeployService.ResolveCredentials(config.AwsProfile);
        using var s3 = credentials is null ? new AmazonS3Client(region) : new AmazonS3Client(credentials, region);

        // 本番のファイルの ETag（1 回の PUT で上げたファイルは MD5）と比べる。
        string? currentEtag = null;
        try
        {
            var meta = await s3.GetObjectMetadataAsync(config.AwsS3Bucket, ShopItemsFile.FileName).ConfigureAwait(false);
            currentEtag = (meta.ETag ?? "").Trim('"').ToLowerInvariant();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // まだ本番に無い（最初の 1 回）。
        }
        if (string.Equals(currentEtag, md5, StringComparison.Ordinal))
        {
            Console.WriteLine("本番のファイルと同じです。上げ直しません。");
            return 0;
        }
        if (dryRun)
        {
            Console.WriteLine("本番のファイルと違います（--dry-run のため上げ直しません）。");
            return 0;
        }

        using (var stream = new MemoryStream(bytes))
        {
            var req = new PutObjectRequest
            {
                BucketName = config.AwsS3Bucket,
                Key = ShopItemsFile.FileName,
                InputStream = stream,
                ContentType = S3DeployService.GuessContentType(ShopItemsFile.FileName),
            };
            req.Headers.CacheControl = S3DeployService.CacheControlFor(ShopItemsFile.FileName);
            await s3.PutObjectAsync(req).ConfigureAwait(false);
        }
        Console.WriteLine($"上げ直しました: {ShopItemsFile.FileName}");

        if (string.IsNullOrWhiteSpace(config.CloudFrontDistributionId))
        {
            Console.WriteLine("CloudFrontDistributionId が未設定のため、キャッシュは消していません。");
            return 0;
        }
        using var cloudFront = credentials is null
            ? new AmazonCloudFrontClient(RegionEndpoint.USEast1)
            : new AmazonCloudFrontClient(credentials, RegionEndpoint.USEast1);
        var resp = await cloudFront.CreateInvalidationAsync(new CreateInvalidationRequest
        {
            DistributionId = config.CloudFrontDistributionId,
            InvalidationBatch = new InvalidationBatch
            {
                CallerReference = Guid.NewGuid().ToString("N"),
                Paths = new Paths { Quantity = 1, Items = new List<string> { "/" + ShopItemsFile.FileName } },
            },
        }).ConfigureAwait(false);
        Console.WriteLine($"キャッシュを消しました: {resp.Invalidation?.Id}");
        return 0;
    }
}
