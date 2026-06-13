using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder;

/// <summary>SiteBuilder のエントリポイント。
/// 引数なし＝テストモード（テスト用ディレクトリへ、GA4 / AdSense / ads.txt なしで生成）。
/// <c>--production</c> 指定時のみ本番モード（本番ディレクトリへ全出力込みで生成）。
/// 本番モードへの S3 同期・CloudFront キャッシュ削除などの AWS 連携は次バージョンで載せる想定。</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // ビルドモードはコマンドライン引数で決める（App.config では決めない）。
            // 既定はテストモード：うっかり普通に起動しても本番ディレクトリ・本番タグには触れない。
            bool isProduction = false;
            foreach (var a in args)
            {
                if (string.Equals(a, "--production", StringComparison.OrdinalIgnoreCase))
                {
                    isProduction = true;
                }
                else
                {
                    Console.Error.WriteLine($"不明な引数: {a}");
                    Console.Error.WriteLine("使い方: PrecureDataStars.SiteBuilder [--production]");
                    Console.Error.WriteLine("  引数なし     : テストモード（SiteOutputDirTest へ、GA4 / AdSense / ads.txt なし）");
                    Console.Error.WriteLine("  --production : 本番モード（SiteOutputDir へ、GA4 / AdSense / ads.txt あり）");
                    return 2;
                }
            }

            var config = BuildConfig.FromAppConfig(isProduction);
            var pipeline = new SiteBuilderPipeline();
            await pipeline.RunAsync(config).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("!!! SiteBuilder 失敗 !!!");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
