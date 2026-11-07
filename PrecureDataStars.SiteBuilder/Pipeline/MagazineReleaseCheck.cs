using System.Configuration;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 今日（日本時間）がアニメ雑誌の号の発売日かを確かめる起動方法（<c>--magazine-release-today</c>）。
/// 発売日の 0:00 に、その号で明らかになる回を早く見る設定の閲覧者に出すため、定時デプロイ
/// （<c>scripts/scheduled-deploy.ps1 -MagazineReleaseOnly</c>）が毎日 0:00 に呼び、発売日のときだけビルドして公開する。
/// 発売日なら号を表示して終了コード 0、そうでなければ <see cref="NotReleaseDayExitCode"/>。サイトは作らない。
/// </summary>
internal static class MagazineReleaseCheck
{
    /// <summary>今日が発売日でないときの終了コード（失敗の 1・引数の誤りの 2 と区別する）。</summary>
    public const int NotReleaseDayExitCode = 3;

    public static async Task<int> RunAsync()
    {
        var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString
            ?? throw new InvalidOperationException("App.config に接続文字列 'DatastarsMySql' がありません。");
        var factory = MySqlConnectionFactory.FromConnectionString(cs);
        var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date;
        var issues = await new MagazineIssuesRepository(factory).GetAllAsync().ConfigureAwait(false);
        var released = issues.Where(i => i.ReleaseDate.Date == today).ToList();
        if (released.Count == 0)
        {
            Console.WriteLine($"{today:yyyy-MM-dd} に発売の号はありません。");
            return NotReleaseDayExitCode;
        }
        foreach (var issue in released) Console.WriteLine($"{today:yyyy-MM-dd} は {issue.IssueLabel} の発売日です。");
        return 0;
    }
}
