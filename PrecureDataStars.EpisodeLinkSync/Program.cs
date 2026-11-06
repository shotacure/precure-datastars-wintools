using System.Configuration;
using System.Globalization;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.EpisodeLinkSync;

/// <summary>
/// 放送後のリンクの自動取り込みツールのエントリポイント（中身は <see cref="EpisodeLinkSyncService"/>）。
/// </summary>
/// <remarks>
/// 使い方:
/// <code>
///   EpisodeLinkSync [--dry-run]                                    # 1 回だけ見て回る（毎日の分）
///   EpisodeLinkSync --watch [--until HH:mm] [--interval 秒] [--dry-run]  # 今日の回のあらすじと次の回の予告が出るまで見続ける（日曜の分）
/// </code>
/// <c>--watch</c> の既定は 12:00 まで 5 秒おき。<c>--dry-run</c> は DB へ書かずに見つけたものを表示する。
/// 終了コード: 0 = 正常 / 3 = 知らせることがある（最後に一覧を出す）/ 1 = 実行時エラー / 2 = 引数・設定の誤り。
/// </remarks>
internal static class Program
{
    private const string Usage = "使い方: EpisodeLinkSync [--watch [--until HH:mm] [--interval 秒]] [--dry-run]";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        bool watch = false, dryRun = false;
        TimeOnly until = new(12, 0);
        double intervalSec = 5;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--watch": watch = true; break;
                case "--dry-run": dryRun = true; break;
                case "--until" when i + 1 < args.Length && TimeOnly.TryParseExact(args[i + 1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var u):
                    until = u; i++; break;
                case "--interval" when i + 1 < args.Length && double.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s >= 1:
                    intervalSec = s; i++; break;
                default:
                    Console.Error.WriteLine($"ERROR: 不明な引数です: {args[i]}");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            Console.Error.WriteLine("ERROR: App.config に DatastarsMySql 接続文字列が設定されていません。");
            return 2;
        }

        try
        {
            var factory = MySqlConnectionFactory.FromConnectionString(cs);
            using var web = new WebProbe(Log);
            var service = new EpisodeLinkSyncService(new EpisodesRepository(factory), new SeriesRepository(factory), web, dryRun, Log);

            Log($"放送後のリンクの取り込み{(watch ? "（見続ける）" : "")}{(dryRun ? "（書かない）" : "")}");
            if (watch)
                await service.RunWatchAsync(DateTime.Today + until.ToTimeSpan(), TimeSpan.FromSeconds(intervalSec), CancellationToken.None);
            else
                await service.RunOnceAsync(CancellationToken.None);

            Log($"入れた件数：{service.WrittenCount}");
            if (service.Notices.Count == 0) return 0;
            Console.WriteLine();
            Console.WriteLine($"知らせること：{service.Notices.Count} 件");
            foreach (var n in service.Notices) Console.WriteLine("  - " + n);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static void Log(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}");
}
