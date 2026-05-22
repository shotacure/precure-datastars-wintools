#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrecureDataStars.AmazonPaApi;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.AmazonSync;

/// <summary>
/// products テーブルから ASIN を持つ商品を抽出し、PA-API GetItems を叩いて
/// ジャケット画像 URL（m.media-amazon.com 系）を <c>cover_image_url</c> に書き戻すバッチ。
/// 取得元コードは CD ASIN 由来なら <c>amazon_cd</c>、デジタル ASIN 由来なら <c>amazon_digital</c>。
/// <para>
/// CLI 引数：
/// <list type="bullet">
///   <item><c>--all</c>: 鮮度に関わらず全件強制再取得</item>
///   <item><c>--asin B0XXXXXXXX</c>: 単一 ASIN だけテスト取得（DB 更新あり）</item>
///   <item><c>--dry-run</c>: DB 更新せず取得結果だけ表示</item>
///   <item>引数なし: 鮮度切れ（未取得 or 90 日以上前）のみ取得</item>
/// </list>
/// </para>
/// PA-API のレート制限（1 TPS）を順守するため、各リクエストの間に 1100 ms の sleep を入れる。
/// </summary>
public static class Program
{
    /// <summary>鮮度判定の閾値（日数）。これより古い／未取得を再取得対象とする。</summary>
    private const int StaleDays = 90;

    /// <summary>PA-API レート制限（1 TPS）順守のためのリクエスト間隔ミリ秒。</summary>
    private const int RateLimitDelayMs = 1100;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            // CLI 引数のパース
            bool all = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
            bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
            string? singleAsin = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--asin", StringComparison.OrdinalIgnoreCase))
                {
                    singleAsin = args[i + 1];
                    break;
                }
            }

            // PA-API クライアント（App.config のキーから）
            var paApi = PaApiClientFactory.TryCreateFromAppConfig();
            if (paApi == null)
            {
                Console.Error.WriteLine("ERROR: App.config に PaApi.AccessKey / PaApi.SecretKey / PaApi.PartnerTag が設定されていません。");
                return 2;
            }

            // 単一 ASIN モード（接続不要のテスト用途）
            if (!string.IsNullOrWhiteSpace(singleAsin))
            {
                Console.WriteLine($"単一 ASIN テスト取得: {singleAsin}");
                var item = await paApi.GetItemAsync(singleAsin!, CancellationToken.None);
                if (item == null)
                {
                    Console.WriteLine("該当商品なし、または応答が空でした。");
                    return 0;
                }
                Console.WriteLine($"  ASIN          : {item.Asin}");
                Console.WriteLine($"  Title         : {item.Title}");
                Console.WriteLine($"  ByLine        : {item.ByLine}");
                Console.WriteLine($"  PriceDisplay  : {item.PriceDisplay}");
                Console.WriteLine($"  ReleaseDate   : {item.ReleaseDate}");
                Console.WriteLine($"  LargeImageUrl : {item.LargeImageUrl}");
                Console.WriteLine($"  MediumImageUrl: {item.MediumImageUrl}");
                return 0;
            }

            // 通常モード：DB 経由でバッチ取得
            string connStr = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString ?? "";
            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.Error.WriteLine("ERROR: App.config に DatastarsMySql 接続文字列が設定されていません。");
                return 2;
            }

            // MySqlConnectionFactory は接続文字列を内包する DbConfig を受け取る設計のため、
            // 生の接続文字列をそのまま渡さず DbConfig に包んで渡す。
            var factory = new MySqlConnectionFactory(new DbConfig(connStr));
            var repo = new ProductsRepository(factory);
            var all_products = await repo.GetAllAsync();

            // 対象抽出：ASIN を 1 つでも持っていて、（全件モード or 鮮度切れ）の商品。
            var threshold = DateTime.Now.AddDays(-StaleDays);
            var targets = all_products
                .Where(p =>
                    (!string.IsNullOrWhiteSpace(p.AmazonAsinCd) || !string.IsNullOrWhiteSpace(p.AmazonAsinDigital))
                    && (all
                        || string.IsNullOrWhiteSpace(p.CoverImageUrl)
                        || p.CoverImageFetchedAt is null
                        || p.CoverImageFetchedAt < threshold))
                .ToList();

            Console.WriteLine($"対象商品: {targets.Count} 件 ({(all ? "全件強制" : $"鮮度切れ {StaleDays} 日以上")})");
            if (dryRun) Console.WriteLine("(dry-run モード：DB 更新は行いません)");

            int ok = 0, miss = 0;
            int idx = 0;
            foreach (var prod in targets)
            {
                idx++;
                Console.Write($"[{idx}/{targets.Count}] {prod.ProductCatalogNo,-20} ... ");

                string? imageUrl = null;
                string? source = null;

                // 物理（CD）優先 → デジタル の順で画像 URL を取りに行く。最初に取れたものを採用する。
                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinCd))
                {
                    try
                    {
                        var item = await paApi.GetItemAsync(prod.AmazonAsinCd!, CancellationToken.None);
                        if (item?.LargeImageUrl is { Length: > 0 } u) { imageUrl = u; source = "amazon_cd"; }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ! CD ASIN 取得失敗: {ex.Message}");
                    }
                    await Task.Delay(RateLimitDelayMs);
                }
                if (imageUrl is null && !string.IsNullOrWhiteSpace(prod.AmazonAsinDigital))
                {
                    try
                    {
                        var item = await paApi.GetItemAsync(prod.AmazonAsinDigital!, CancellationToken.None);
                        if (item?.LargeImageUrl is { Length: > 0 } u) { imageUrl = u; source = "amazon_digital"; }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ! Digital ASIN 取得失敗: {ex.Message}");
                    }
                    await Task.Delay(RateLimitDelayMs);
                }

                if (imageUrl != null && source != null)
                {
                    if (!dryRun)
                    {
                        await repo.UpdateCoverImageAsync(prod.ProductCatalogNo, imageUrl, source, DateTime.Now);
                    }
                    ok++;
                    Console.WriteLine($"OK ({source})");
                }
                else
                {
                    miss++;
                    Console.WriteLine("該当画像なし");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"完了: 取得成功 {ok} 件 / 失敗・該当なし {miss} 件");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex);
            return 1;
        }
    }
}
