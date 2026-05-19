using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder;

/// <summary>SiteBuilder のエントリポイント。</summary>
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
