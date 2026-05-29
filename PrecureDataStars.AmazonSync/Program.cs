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
/// products テーブルから ASIN を持つ商品を抽出し、Creators API GetItems を叩いて
/// ジャケット画像 URL（m.media-amazon.com 系）を <c>cover_image_url</c> に書き戻すバッチ。
/// 取得元コードは CD ASIN 由来なら <c>amazon_cd</c>、デジタル ASIN 由来なら <c>amazon_digital</c>。
/// <para>
/// CLI 引数：
/// <list type="bullet">
///   <item><c>--all</c>: 鮮度に関わらず全件強制再取得</item>
///   <item><c>--upgrade-cd-to-digital</c>: cover_image_source='amazon_cd'（CD 由来）かつデジタル ASIN を持つ商品に限り、デジタル由来へ差し替える</item>
///   <item><c>--asin B0XXXXXXXX</c>: 単一 ASIN だけテスト取得（DB 更新なし・診断表示のみ）</item>
///   <item><c>--dry-run</c>: DB 更新せず取得結果だけ表示</item>
///   <item>引数なし: 鮮度切れ（未取得 or 90 日以上前）のみ取得</item>
/// </list>
/// </para>
/// Creators API のレート制限（1 TPS）を順守するため、各リクエストの間に 1100 ms の sleep を入れる。
/// </summary>
public static class Program
{
    /// <summary>鮮度判定の閾値（日数）。これより古い／未取得を再取得対象とする。</summary>
    private const int StaleDays = 90;

    /// <summary>Creators API レート制限（1 TPS）順守のためのリクエスト間隔ミリ秒。</summary>
    private const int RateLimitDelayMs = 1100;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            // CLI 引数のパース
            bool all = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
            bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
            // 既存の cover_image_source='amazon_cd'（CD 由来）の画像を、デジタル ASIN があるものに限って
            // デジタル由来へ差し替えるモード。CD（特に廃盤）は素人写真リスクがあるため、
            // 事業者アップが確実なデジタル画像へ「格上げ」する用途。
            bool upgradeCdToDigital = args.Any(a => a.Equals("--upgrade-cd-to-digital", StringComparison.OrdinalIgnoreCase));
            string? singleAsin = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--asin", StringComparison.OrdinalIgnoreCase))
                {
                    singleAsin = args[i + 1];
                    break;
                }
            }

            // Creators API クライアント（App.config のキーから）
            var paApi = PaApiClientFactory.TryCreateFromAppConfig();
            if (paApi == null)
            {
                Console.Error.WriteLine("ERROR: App.config に PaApi.CredentialId / PaApi.CredentialSecret / PaApi.CredentialVersion / PaApi.PartnerTag が設定されていません。");
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
                    // GetItems のレスポンス構造（topkey "itemResults" → items[]）が想定と違うと
                    // パースで弾かれて item==null になる。生 JSON を吐いて実際のキー構造を確認する。
                    if (!string.IsNullOrEmpty(paApi.LastRawResponseJson))
                    {
                        Console.WriteLine();
                        Console.WriteLine("--- 生レスポンス JSON（診断用ダンプ） ---");
                        Console.WriteLine(paApi.LastRawResponseJson);
                    }
                    else
                    {
                        Console.WriteLine("(LastRawResponseJson も空 = HTTP 自体が失敗、または応答本文なし)");
                    }
                    return 0;
                }
                Console.WriteLine($"  ASIN          : {item.Asin}");
                Console.WriteLine($"  Title         : {item.Title}");
                Console.WriteLine($"  ByLine        : {item.ByLine}");
                Console.WriteLine($"  PriceDisplay  : {item.PriceDisplay}");
                Console.WriteLine($"  ReleaseDate   : {item.ReleaseDate}");
                Console.WriteLine($"  LargeImageUrl : {item.LargeImageUrl}");
                Console.WriteLine($"  MediumImageUrl: {item.MediumImageUrl}");
                // 画像 URL が空のとき、API レスポンス側の構造（images.primary.* が来ているかどうか）を
                // 切り分けたいケースが頻発するため、診断用に生 JSON を末尾に丸ごと吐く。
                if (string.IsNullOrEmpty(item.LargeImageUrl) && string.IsNullOrEmpty(item.MediumImageUrl)
                    && !string.IsNullOrEmpty(paApi.LastRawResponseJson))
                {
                    Console.WriteLine();
                    Console.WriteLine("--- 画像 URL が空のため、生レスポンス JSON を診断用にダンプします ---");
                    Console.WriteLine(paApi.LastRawResponseJson);
                }
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

            // 対象抽出。
            var threshold = DateTime.Now.AddDays(-StaleDays);
            List<Product> targets;
            string modeLabel;
            if (upgradeCdToDigital)
            {
                // CD 由来カバーかつデジタル ASIN を持つものだけ。デジタル画像へ差し替える。
                targets = all_products
                    .Where(p => string.Equals(p.CoverImageSource, "amazon_cd", StringComparison.Ordinal)
                             && !string.IsNullOrWhiteSpace(p.AmazonAsinDigital))
                    .ToList();
                modeLabel = "CD 由来 → デジタル差し替え";
            }
            else
            {
                // ASIN を 1 つでも持っていて、（全件モード or 未取得 or 鮮度切れ）の商品。
                // 「未取得」は CD/デジタルを別々に判定する：ASIN がある側の cover 列が空なら対象とする。
                // これにより「片方だけ取れている中途半端な状態」（例：デジタルだけ入って CD 列が空）も
                // 自動的に再取得対象になり、両列が揃う（CoverImageUrl 計算プロパティ任せだと
                // 片方でも非空なら未取得扱いにならずスキップされてしまう問題への対処）。
                targets = all_products
                    .Where(p =>
                    {
                        bool hasCdAsin = !string.IsNullOrWhiteSpace(p.AmazonAsinCd);
                        bool hasDigitalAsin = !string.IsNullOrWhiteSpace(p.AmazonAsinDigital);
                        if (!hasCdAsin && !hasDigitalAsin) return false;
                        bool missingCover =
                            (hasCdAsin && string.IsNullOrWhiteSpace(p.CoverImageUrlCd))
                            || (hasDigitalAsin && string.IsNullOrWhiteSpace(p.CoverImageUrlDigital));
                        return all
                            || missingCover
                            || p.CoverImageFetchedAt is null
                            || p.CoverImageFetchedAt < threshold;
                    })
                    .ToList();
                modeLabel = all ? "全件強制" : $"未取得・鮮度切れ {StaleDays} 日以上";
            }

            Console.WriteLine($"対象商品: {targets.Count} 件 ({modeLabel})");
            if (dryRun) Console.WriteLine("(dry-run モード：DB 更新は行いません)");

            int ok = 0, miss = 0;
            int idx = 0;
            foreach (var prod in targets)
            {
                idx++;
                Console.Write($"[{idx}/{targets.Count}] {prod.ProductCatalogNo,-20} ... ");

                // CD・デジタル両系統を取得して両列に保存する（CD とデジタルでジャケットが
                // 異なる場合があるため、片方を採用しても両方を保持して後から切り替えられるようにする）。
                string? digitalUrl = null;
                string? cdUrl = null;

                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinDigital))
                {
                    try
                    {
                        var item = await paApi.GetItemAsync(prod.AmazonAsinDigital!, CancellationToken.None);
                        if (item?.LargeImageUrl is { Length: > 0 } u) digitalUrl = u;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ! Digital ASIN 取得失敗: {ex.Message}");
                    }
                    await Task.Delay(RateLimitDelayMs);
                }
                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinCd))
                {
                    try
                    {
                        var item = await paApi.GetItemAsync(prod.AmazonAsinCd!, CancellationToken.None);
                        if (item?.LargeImageUrl is { Length: > 0 } u) cdUrl = u;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ! CD ASIN 取得失敗: {ex.Message}");
                    }
                    await Task.Delay(RateLimitDelayMs);
                }

                if (digitalUrl != null || cdUrl != null)
                {
                    // 採用ソース決定：upgrade モードはデジタルへ格上げ、それ以外は既存の明示選択を尊重しつつ
                    // 未選択／選択先が空ならデジタル→CD の既定優先（DecideCoverSource 参照）。
                    string? source = DecideCoverSource(prod.CoverImageSource, cdUrl, digitalUrl, upgradeCdToDigital);
                    if (!dryRun)
                    {
                        await repo.UpdateCoverImagesAsync(prod.ProductCatalogNo, cdUrl, digitalUrl, source, DateTime.Now);
                    }
                    ok++;
                    Console.WriteLine($"OK (CD={(cdUrl != null ? "有" : "無")} / デジタル={(digitalUrl != null ? "有" : "無")} / 採用={source ?? "なし"})");
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

    /// <summary>
    /// 表示に採用するジャケット画像ソースを決める。
    /// <list type="bullet">
    ///   <item><paramref name="upgrade"/>（--upgrade-cd-to-digital）が true：デジタルがあれば <c>amazon_digital</c> へ格上げ、無ければ CD。</item>
    ///   <item>既存の明示選択（<paramref name="existing"/> が <c>amazon_cd</c> / <c>amazon_digital</c>）があり、その URL が取れていれば尊重する。</item>
    ///   <item>それ以外（未選択、または選択先の URL が空）はデジタル → CD の既定優先。</item>
    ///   <item>両方とも URL が無ければ null（未選択）。</item>
    /// </list>
    /// 既定優先がデジタルのため、「人が CD へ切り替えた」選択だけが実質的に尊重される
    /// （digital 選択は既定と一致するので区別不要）。
    /// </summary>
    private static string? DecideCoverSource(string? existing, string? cdUrl, string? digitalUrl, bool upgrade)
    {
        bool hasCd = !string.IsNullOrEmpty(cdUrl);
        bool hasDigital = !string.IsNullOrEmpty(digitalUrl);
        if (!hasCd && !hasDigital) return null;
        if (upgrade) return hasDigital ? "amazon_digital" : "amazon_cd";
        if (existing == "amazon_digital" && hasDigital) return "amazon_digital";
        if (existing == "amazon_cd" && hasCd) return "amazon_cd";
        return hasDigital ? "amazon_digital" : "amazon_cd";
    }
}
