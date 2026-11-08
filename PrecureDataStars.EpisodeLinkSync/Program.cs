using System.Configuration;
using System.Globalization;
using PrecureDataStars.Catalog.Common.Services;
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
/// YouTube は Data API v3 で見るので、App.config の <c>YouTube.ApiKey</c> が要る（Catalog・ArtTrackRefresh と同じキー）。
/// 見続ける間は、待っているものを 1 行で出して書き換え続ける（<see cref="Status"/>。ログには残さない）。
/// <c>--date</c> は見続ける回の「今日」を差し替える試すとき用の引数で、<c>--dry-run</c> と一緒のときだけ使える（過ぎた日なら 1 回見て終わる）。
/// 終了コード: 0 = 正常 / 3 = 知らせることがある（最後に一覧を出す）/ 1 = 実行時エラー / 2 = 引数・設定の誤り。
/// </remarks>
internal static class Program
{
    private const string Usage = "使い方: EpisodeLinkSync [--watch [--until HH:mm] [--interval 秒] [--date yyyy-MM-dd]] [--dry-run]";

    /// <summary>
    /// 出力が画面でない（scripts/scheduled-episode-links.ps1 が受けている）とき、書き換える 1 行をこの頭で 1 行として出す。
    /// 受ける側はこの頭の行をログに残さず、窓のいちばん下で書き換える。
    /// </summary>
    public const string StatusPrefix = "[status] ";

    /// <summary>書き換える 1 行の頭で回す印（動いていることが分かるように）。</summary>
    private static readonly char[] Spinner = ['|', '/', '-', '\\'];
    private static int _spin;
    private static bool _statusShown;

    /// <summary>行を消して頭へ戻す制御の並び（ESC [2K と CR）。</summary>
    private const string ClearLine = "\u001b[2K\r";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        bool watch = false, dryRun = false;
        TimeOnly until = new(12, 0);
        double intervalSec = 5;
        // 見続ける回の「今日」。--date は試すとき用で、--dry-run と一緒のときだけ受け付ける（過ぎた日なら 1 回見て終わる）。
        DateTime today = DateTime.Today;
        bool dateGiven = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--watch": watch = true; break;
                case "--dry-run": dryRun = true; break;
                case "--date" when i + 1 < args.Length && DateTime.TryParseExact(args[i + 1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d):
                    today = d; dateGiven = true; i++; break;
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

        if (dateGiven && !dryRun)
        {
            Console.Error.WriteLine("ERROR: --date は --dry-run と一緒のときだけ使えます。");
            return 2;
        }

        var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            Console.Error.WriteLine("ERROR: App.config に DatastarsMySql 接続文字列が設定されていません。");
            return 2;
        }
        string? apiKey = ConfigurationManager.AppSettings["YouTube.ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_YOUTUBE_DATA_API_KEY")
        {
            Console.Error.WriteLine("ERROR: App.config に YouTube.ApiKey が設定されていません。");
            return 2;
        }

        try
        {
            var factory = MySqlConnectionFactory.FromConnectionString(cs);
            using var youtube = new YouTubeDataApiClient(apiKey);
            using var web = new WebProbe(youtube, Log);
            var service = new EpisodeLinkSyncService(new EpisodesRepository(factory), new SeriesRepository(factory), web, dryRun, Log, Status);

            Log($"放送後のリンクの取り込み{(watch ? "（見続ける）" : "")}{(dryRun ? "（書かない）" : "")}");
            if (watch)
                await service.RunWatchAsync(today, today + until.ToTimeSpan(), TimeSpan.FromSeconds(intervalSec), CancellationToken.None);
            else
                await service.RunOnceAsync(CancellationToken.None);

            EndStatus();
            Log($"入れた件数：{service.WrittenCount}");
            if (service.Notices.Count == 0) return 0;
            Console.WriteLine();
            Console.WriteLine($"知らせること：{service.Notices.Count} 件");
            foreach (var n in service.Notices) Console.WriteLine("  - " + n);
            return 3;
        }
        catch (Exception ex)
        {
            EndStatus();
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    private static void Log(string message)
    {
        EndStatus();
        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}");
    }

    /// <summary>
    /// 待っている間の 1 行を出す。画面に直接出しているときは、いちばん下の行を消して書き直す（改行しない）。
    /// 出力を受けられているときは <see cref="StatusPrefix"/> を頭に付けて 1 行で出し、書き換えは受ける側に任せる。
    /// </summary>
    private static void Status(string message)
    {
        _spin = (_spin + 1) % Spinner.Length;
        string line = $"{Spinner[_spin]} {DateTime.Now:HH:mm:ss}  {message}";
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(StatusPrefix + line);
            return;
        }
        Console.Write(ClearLine + line);
        _statusShown = true;
    }

    /// <summary>画面に書き換えの 1 行が出ていれば消し、次の行をその位置から書けるようにする。</summary>
    private static void EndStatus()
    {
        if (!_statusShown) return;
        Console.Write(ClearLine);
        _statusShown = false;
    }
}
