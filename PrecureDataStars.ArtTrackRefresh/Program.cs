using System.Configuration;
using PrecureDataStars.Catalog.Common.Services;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.ArtTrackRefresh;

/// <summary>
/// 配信音源（YouTube アートトラック）の定期の再確認ツールのエントリポイント。
/// <para>
/// 割り当て済みの全動画を YouTube Data API で問い合わせ直し、埋め込み可否・地域制限・確認時刻を更新する。
/// 消えた動画は割り当てを外し、変化のあった商品は取り込み状態を <c>AMBIGUOUS</c> に戻す
/// （中身は <see cref="ArtTrackRefreshService"/>）。サイトは確認から
/// <see cref="ArtTrackPlayability.RecheckDays"/> 日を過ぎた音源に再生ボタンを出さないので、
/// 本番デプロイの前に必ず流す。
/// </para>
/// </summary>
/// <remarks>
/// 使い方:
/// <code>
///   ArtTrackRefresh [--dry-run] [--allow-mass-removal]
/// </code>
/// <c>--dry-run</c> は DB へ書かずに結果だけを表示する。<c>--allow-mass-removal</c> は、消えた動画が
/// 多すぎて止まったときに、中身を確かめたうえで書き込みを通すためのもの。
/// 終了コード: 0 = 正常 / 1 = 実行時エラー・消えた動画が多すぎて止めた / 2 = 引数・設定の誤り。
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        bool dryRun = false, allowMassRemoval = false;
        foreach (var a in args)
        {
            switch (a)
            {
                case "--dry-run": dryRun = true; break;
                case "--allow-mass-removal": allowMassRemoval = true; break;
                default:
                    Console.Error.WriteLine($"ERROR: 不明な引数です: {a}");
                    Console.Error.WriteLine("使い方: ArtTrackRefresh [--dry-run] [--allow-mass-removal]");
                    return 2;
            }
        }

        var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            Console.Error.WriteLine("ERROR: App.config に DatastarsMySql 接続文字列が設定されていません。");
            return 2;
        }
        // App.config.sample のプレースホルダをそのままコピーした状態も未設定として扱う。
        string? apiKey = ConfigurationManager.AppSettings["YouTube.ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "YOUR_YOUTUBE_DATA_API_KEY")
        {
            Console.Error.WriteLine("ERROR: App.config に YouTube.ApiKey が設定されていません。");
            return 2;
        }

        try
        {
            var factory = MySqlConnectionFactory.FromConnectionString(cs);
            using var api = new YouTubeDataApiClient(apiKey);
            var service = new ArtTrackRefreshService(api, new TracksRepository(factory), new ProductsRepository(factory));

            Console.WriteLine($"配信音源の再確認{(dryRun ? "（書き込まない）" : "")}");
            var result = await service.RefreshAsync(dryRun, allowMassRemoval);
            Report(result);

            if (result.StoppedForMassRemoval)
            {
                Console.Error.WriteLine(
                    $"ERROR: 消えた動画が多すぎるため、書き込まずに止めました（上限 {ArtTrackRefreshService.MassRemovalThreshold(result.VideoCount)} 件）。"
                    + "API の不調でないことを確かめたら --allow-mass-removal を付けて流し直してください。");
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    /// <summary>結果を表示する。変化のあったトラックは種類ごとに全件並べる。</summary>
    private static void Report(ArtTrackRefreshResult result)
    {
        Console.WriteLine($"  トラック {result.TrackCount} 件 / 動画 {result.VideoCount} 件を確かめました。");

        foreach (var group in result.Changes.GroupBy(c => c.Kind).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  {Label(group.Key)}：{group.Count()} 件");
            foreach (var c in group)
            {
                string sub = c.SubOrder > 0 ? $"-{c.SubOrder}" : "";
                Console.WriteLine($"    - {c.ProductCatalogNo} / {c.CatalogNo}-Tr.{c.TrackNo}{sub}  {c.VideoId}");
            }
        }
        if (result.Changes.Count == 0) Console.WriteLine("  変化はありませんでした。");

        if (result.ProductsMarkedAmbiguous.Count > 0)
        {
            Console.WriteLine(
                $"  取り込み状態を AMBIGUOUS に{(result.Written ? "戻しました" : "戻します")}：{string.Join(", ", result.ProductsMarkedAmbiguous)}");
        }
        Console.WriteLine(result.Written ? "  DB へ書き込みました。" : "  DB へは書き込んでいません。");
    }

    private static string Label(ArtTrackRefreshChangeKind kind) => kind switch
    {
        ArtTrackRefreshChangeKind.Removed => "消えた動画（割り当てを外す）",
        ArtTrackRefreshChangeKind.NoLongerEmbeddable => "埋め込みが許可されなくなった",
        ArtTrackRefreshChangeKind.NowEmbeddable => "埋め込みが許可されるようになった",
        ArtTrackRefreshChangeKind.BlockedInJapan => "日本で再生できなくなった",
        ArtTrackRefreshChangeKind.Unblocked => "日本での地域制限が外れた",
        _ => kind.ToString()
    };
}
