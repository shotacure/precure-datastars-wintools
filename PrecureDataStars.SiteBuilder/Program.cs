using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder;

/// <summary>SiteBuilder のエントリポイント。
/// 引数なし＝テストモード（テスト用ディレクトリへ、GA4 / AdSense / ads.txt なしで生成）。
/// <c>--production</c> 指定時のみ本番モード（本番ディレクトリへ全出力込みで生成）。
/// <c>--production --deploy</c> でビルド後に S3 へ差分同期＋CloudFront キャッシュ削除まで実行する。
/// <c>--dry-run</c> は変更計画のみ表示（無変更）、<c>--yes</c> は削除前確認の省略。</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // ビルドモード・デプロイ意図はコマンドライン引数で決める（App.config では決めない）。
            // 既定はテストモード：うっかり普通に起動しても本番ディレクトリ・本番タグには触れない。
            bool isProduction = false;
            bool deploy = false;
            bool dryRun = false;
            bool skipConfirm = false;
            string pageFilter = "";
            bool expectPageValue = false;
            foreach (var a in args)
            {
                if (expectPageValue)
                {
                    // 直前の "--page" に続く値（生成対象のページ URL パス断片。例: /privacy/）。
                    pageFilter = a;
                    expectPageValue = false;
                }
                else if (string.Equals(a, "--production", StringComparison.OrdinalIgnoreCase))
                    isProduction = true;
                else if (string.Equals(a, "--deploy", StringComparison.OrdinalIgnoreCase))
                    deploy = true;
                else if (string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase))
                    dryRun = true;
                else if (string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase))
                    skipConfirm = true;
                else if (string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase))
                    expectPageValue = true;
                else
                {
                    Console.Error.WriteLine($"不明な引数: {a}");
                    PrintUsage();
                    return 2;
                }
            }
            if (expectPageValue)
            {
                Console.Error.WriteLine("--page にはページの URL パス（例: /privacy/）を指定してください。");
                PrintUsage();
                return 2;
            }

            // デプロイは本番ビルドからのみ許可する（テスト出力を本番バケットへ流す事故を構造的に防ぐ）。
            if (deploy && !isProduction)
            {
                Console.Error.WriteLine("--deploy は --production と併用してください（テスト出力はデプロイできません）。");
                PrintUsage();
                return 2;
            }
            // --dry-run / --yes は --deploy のときだけ意味を持つ。単独指定は誤用なので弾く。
            if ((dryRun || skipConfirm) && !deploy)
            {
                Console.Error.WriteLine("--dry-run / --yes は --deploy と併用してください。");
                PrintUsage();
                return 2;
            }

            var deployOptions = deploy
                ? new DeployRuntimeOptions(Requested: true, DryRun: dryRun, SkipConfirm: skipConfirm)
                : DeployRuntimeOptions.None;

            var config = BuildConfig.FromAppConfig(isProduction, deployOptions, pageFilter);
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

    /// <summary>使い方の表示。引数エラー時に共通で出す。</summary>
    private static void PrintUsage()
    {
        Console.Error.WriteLine("使い方: PrecureDataStars.SiteBuilder [--production] [--page <path>] [--deploy [--dry-run] [--yes]]");
        Console.Error.WriteLine("  引数なし     : テストモード（SiteOutputDirTest へ、GA4 / AdSense / ads.txt なし）");
        Console.Error.WriteLine("  --production : 本番モード（SiteOutputDir へ、GA4 / AdSense / ads.txt あり）");
        Console.Error.WriteLine("  --page <path>: ピンポイントビルド。URL パスに <path> を含むページだけを生成（例: /privacy/）。");
        Console.Error.WriteLine("                 sitemap / 検索インデックスは再生成せず、--deploy 時も削除は行わない（部分生成の安全策）。");
        Console.Error.WriteLine("  --deploy     : 本番ビルド後に S3 へ差分同期＋CloudFront キャッシュ削除（--production 必須）");
        Console.Error.WriteLine("  --dry-run    : デプロイ計画のみ表示（S3 / CloudFront を変更しない。--deploy と併用）");
        Console.Error.WriteLine("  --yes        : 削除前の確認をスキップ（--deploy と併用）");
    }
}
