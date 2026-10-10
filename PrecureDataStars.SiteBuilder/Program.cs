using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder;

/// <summary>SiteBuilder のエントリポイント。
/// <c>--test</c> でテストモード（テスト用ディレクトリへ、GA4 / AdSense / ads.txt なしで生成）、
/// <c>--production</c> で本番モード（本番ディレクトリへ全出力込みで生成）。
/// どちらも指定しないときは、端末からの対話実行なら 1 回だけテスト／本番／本番＋デプロイを聞く（Enter = テスト）。
/// スクリプトやパイプ経由（標準入力か標準出力がリダイレクト）なら聞かずにテストモード。
/// <c>--production --deploy</c> でビルド後に S3 へ差分同期＋CloudFront キャッシュ削除まで実行する。
/// <c>--dry-run</c> は変更計画のみ表示（無変更）、<c>--yes</c> は削除前確認の省略。
/// <c>--refresh-telop</c> はサブタイトルのテロップ画像を、<c>--refresh-og</c> は OGP カード画像を、作り置きを使わずに描き直す
/// （<c>--page</c> と併用すれば対象のページだけ）。
/// <c>--og-cards &lt;一覧.json&gt; &lt;出力先&gt;</c> は、DB を使わずに一覧のカードだけを同じ描き方で描く（precure.news 用。<see cref="OgCardBatch"/>）。</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            // 出力は常に UTF-8（モードの問い合わせ・ログとも）。ProgressReporter も同じ調整をするが、
            // 問い合わせはその前に出るのでここでも行う。
            ProgressReporter.TrySetUtf8Console();

            // 他のサイトの OGP カードだけを描く起動方法。ビルドの引数とは混ぜない。
            if (args.Length > 0 && string.Equals(args[0], "--og-cards", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 3)
                {
                    PrintUsage();
                    return 2;
                }
                return OgCardBatch.Run(args[1], args[2]);
            }

            // ビルドモード・デプロイ意図はコマンドライン引数で決める（App.config では決めない）。
            // 既定はテストモード：うっかり普通に起動しても本番ディレクトリ・本番タグには触れない。
            bool isProduction = false;
            bool isTest = false;
            bool deploy = false;
            bool dryRun = false;
            bool skipConfirm = false;
            bool refreshTelop = false;
            bool refreshOg = false;
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
                else if (string.Equals(a, "--test", StringComparison.OrdinalIgnoreCase))
                    isTest = true;
                else if (string.Equals(a, "--deploy", StringComparison.OrdinalIgnoreCase))
                    deploy = true;
                else if (string.Equals(a, "--dry-run", StringComparison.OrdinalIgnoreCase))
                    dryRun = true;
                else if (string.Equals(a, "--yes", StringComparison.OrdinalIgnoreCase))
                    skipConfirm = true;
                else if (string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase))
                    expectPageValue = true;
                else if (string.Equals(a, "--refresh-telop", StringComparison.OrdinalIgnoreCase))
                    refreshTelop = true;
                else if (string.Equals(a, "--refresh-og", StringComparison.OrdinalIgnoreCase))
                    refreshOg = true;
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
            if (isProduction && isTest)
            {
                Console.Error.WriteLine("--production と --test は同時に指定できません。");
                PrintUsage();
                return 2;
            }

            // モードを指定しなかったときは、端末からの対話実行に限って 1 回だけ聞く
            // （--deploy だけ付いているときは下の「--production 必須」のエラーに任せる）。
            // 「本番＋デプロイ」は --production --deploy と同じ（削除があればデプロイの前に一覧を出して y/N で確かめる）。
            if (!isProduction && !isTest && !deploy)
            {
                var mode = AskModeInteractively();
                isProduction = mode != InteractiveMode.Test;
                deploy = mode == InteractiveMode.ProductionDeploy;
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

            var config = BuildConfig.FromAppConfig(isProduction, deployOptions, pageFilter, refreshTelop, refreshOg);
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

    /// <summary>対話実行で選ぶビルドのモード。</summary>
    private enum InteractiveMode
    {
        /// <summary>テストモード（SiteOutputDirTest へ）。</summary>
        Test,
        /// <summary>本番ビルドだけ（SiteOutputDir へ書き出し、S3 には上げない）。</summary>
        Production,
        /// <summary>本番ビルドのあと S3 へ差分同期＋CloudFront キャッシュ削除（<c>--production --deploy</c> と同じ）。</summary>
        ProductionDeploy,
    }

    /// <summary>
    /// モード未指定のときの問い合わせ。標準入力・標準出力がどちらも端末（リダイレクトされていない）のときだけ
    /// 「テスト／本番／本番＋デプロイ」を 1 回聞く。P なら本番ビルドだけ、D なら本番ビルドのあとデプロイまで。
    /// Enter だけ・それ以外の入力・端末でない（スクリプトやパイプ経由）ときはテストモード。
    /// うっかり本番ディレクトリや本番バケットへ書かない側に倒す。
    /// </summary>
    private static InteractiveMode AskModeInteractively()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected) return InteractiveMode.Test;
        Console.Write("ビルドのモードを選んでください  [T] テスト（Enter） / [P] 本番ビルドだけ / [D] 本番ビルド＋デプロイ : ");
        var answer = Console.ReadLine()?.Trim();
        if (string.Equals(answer, "p", StringComparison.OrdinalIgnoreCase)) return InteractiveMode.Production;
        if (string.Equals(answer, "d", StringComparison.OrdinalIgnoreCase)) return InteractiveMode.ProductionDeploy;
        return InteractiveMode.Test;
    }

    /// <summary>使い方の表示。引数エラー時に共通で出す。</summary>
    private static void PrintUsage()
    {
        Console.Error.WriteLine("使い方: PrecureDataStars.SiteBuilder [--test | --production] [--page <path>] [--refresh-telop] [--refresh-og] [--deploy [--dry-run] [--yes]]");
        Console.Error.WriteLine("  引数なし     : 端末からの対話実行ならテスト／本番ビルドだけ／本番ビルド＋デプロイを 1 回聞く（Enter = テスト）。");
        Console.Error.WriteLine("                 デプロイを選ぶと --production --deploy と同じ（削除があれば一覧を出して y/N で確かめる）。スクリプトやパイプ経由ならテストモード");
        Console.Error.WriteLine("  --test       : テストモード（SiteOutputDirTest へ、GA4 / AdSense / ads.txt なし）。問い合わせを出さない");
        Console.Error.WriteLine("  --production : 本番モード（SiteOutputDir へ、GA4 / AdSense / ads.txt あり）");
        Console.Error.WriteLine("  --page <path>: ピンポイントビルド。URL パスに <path> を含むページだけを生成（例: /privacy/）。");
        Console.Error.WriteLine("                 sitemap / 検索インデックスは再生成せず、--deploy 時も削除は行わない（部分生成の安全策）。");
        Console.Error.WriteLine("  --refresh-telop : サブタイトルのテロップ画像を作り置きを使わずに描き直す（--page と併用すれば対象の話だけ）");
        Console.Error.WriteLine("  --refresh-og    : OGP カード画像を作り置きを使わずに描き直す（--page と併用すれば対象のページだけ）");
        Console.Error.WriteLine("  --deploy     : 本番ビルド後に S3 へ差分同期＋CloudFront キャッシュ削除（--production 必須）");
        Console.Error.WriteLine("  --dry-run    : デプロイ計画のみ表示（S3 / CloudFront を変更しない。--deploy と併用）");
        Console.Error.WriteLine("  --yes        : 削除前の確認をスキップ（--deploy と併用）");
        Console.Error.WriteLine("別の使い方: PrecureDataStars.SiteBuilder --og-cards <一覧.json> <出力先>");
        Console.Error.WriteLine("  一覧のカードだけを、このサイトと同じ描き方・書体で PNG にする（DB を使わない。precure.news 用）");
    }
}
