using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder;

/// <summary>
/// SiteBuilder のエントリポイント。
/// <para>
/// App.config から設定を読み出し、<see cref="SiteBuilderPipeline"/> を 1 回起動して終了する。
/// 引数なしの単純実行のみ対応（差分ビルド・部分再生成等は将来課題）。
/// </para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var config = BuildConfig.FromAppConfig();
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