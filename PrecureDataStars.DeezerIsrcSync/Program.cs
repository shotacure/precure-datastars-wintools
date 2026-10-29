using System.Configuration;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.DeezerIsrcSync;

/// <summary>
/// 配信版の ISRC の取り込みツールのエントリポイント。
/// <para>
/// CD のトラックがある商品ごとに Deezer の配信アルバムを特定し、「何枚目・何曲目」で突き合わせて
/// <c>tracks.digital_isrc</c> / <c>digital_isrc_checked_at</c> と <c>products.deezer_album_id</c> を書く
/// （中身は <see cref="DeezerIsrcSyncService"/>）。決められなかった商品は書かずに一覧に出す。
/// </para>
/// </summary>
/// <remarks>
/// 使い方:
/// <code>
///   DeezerIsrcSync [--dry-run] [--product &lt;代表品番&gt;]
/// </code>
/// <c>--dry-run</c> は DB へ書かずに結果だけを表示する。<c>--product</c> は商品 1 点だけを照らす。
/// 終了コード: 0 = 正常 / 1 = 実行時エラー / 2 = 引数・設定の誤り。
/// </remarks>
internal static class Program
{
    private const string Usage = "使い方: DeezerIsrcSync [--dry-run] [--product <代表品番>]";

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        bool dryRun = false;
        string? product = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dry-run": dryRun = true; break;
                case "--product" when i + 1 < args.Length: product = args[++i]; break;
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
            using var api = new DeezerApiClient();
            var service = new DeezerIsrcSyncService(api, new TracksRepository(factory), new ProductsRepository(factory));

            Console.WriteLine($"配信版の ISRC の取り込み{(dryRun ? "（書き込まない）" : "")}");
            var result = await service.SyncAsync(product, dryRun, PrintProgress);
            Report(result);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex.Message);
            return 1;
        }
    }

    /// <summary>商品 1 点ごとの結果を 1 行で出す。</summary>
    private static void PrintProgress(DeezerIsrcProductOutcome o)
    {
        string head = $"  {o.ProductCatalogNo,-14} {Label(o.Status)}";
        Console.WriteLine(o.Status == DeezerIsrcStatus.Matched
            ? $"{head}  album {o.AlbumId}（{o.Source}・CD の ISRC {o.Overlap}/{o.CdIsrcCount}・CD と違う {o.Differences.Count} 曲・変わる {o.ChangedCount} 曲）"
            : $"{head}  {o.Note}");
    }

    /// <summary>まとめを出す。CD と違う番号の盤は曲ごとに、決められなかった盤は題も添えて全件並べる。</summary>
    private static void Report(DeezerIsrcSyncResult result)
    {
        Console.WriteLine();
        Console.WriteLine($"商品 {result.Products.Count} 点を照らしました（Deezer へのリクエスト {result.RequestCount} 件）。");
        foreach (var group in result.Products.GroupBy(p => p.Status).OrderBy(g => g.Key))
            Console.WriteLine($"  {Label(group.Key)}：{group.Count()} 点");

        PrintDifferences("配信版の ISRC が CD と違う盤", result.Products.Where(p => p.Status == DeezerIsrcStatus.Matched && p.Differences.Count > 0).ToList());

        var moved = result.Products.Where(p => p.Status == DeezerIsrcStatus.Matched && p.PreviousAlbumId is long prev && prev != p.AlbumId).ToList();
        if (moved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"前回と違うアルバムに決まった盤：{moved.Count} 点");
            foreach (var p in moved) Console.WriteLine($"  {p.ProductCatalogNo}  album {p.PreviousAlbumId} → {p.AlbumId}");
        }

        var unresolved = result.Products.Where(p => p.Status != DeezerIsrcStatus.Matched).ToList();
        if (unresolved.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"配信のアルバムが決まらなかった盤：{unresolved.Count} 点");
            foreach (var p in unresolved)
                Console.WriteLine($"  {p.ProductCatalogNo}「{p.ProductTitle}」 {Label(p.Status)}{(p.Note.Length > 0 ? "  " + p.Note : "")}");
        }

        Console.WriteLine();
        Console.WriteLine(result.Written ? "DB へ書き込みました。" : "DB へは書き込んでいません。");
    }

    /// <summary>盤ごとに、CD と番号の違うトラックを並べる。</summary>
    private static void PrintDifferences(string heading, List<DeezerIsrcProductOutcome> products)
    {
        if (products.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine($"{heading}：{products.Count} 点");
        foreach (var p in products)
        {
            Console.WriteLine($"  {p.ProductCatalogNo}「{p.ProductTitle}」 album {p.AlbumId}「{p.AlbumTitle}」");
            foreach (var d in p.Differences)
                Console.WriteLine($"    - {d.CatalogNo}-Tr.{d.TrackNo}  CD {d.CdIsrc ?? "（なし）"} → 配信 {d.DigitalIsrc}  {d.DigitalTitle}");
        }
    }

    private static string Label(DeezerIsrcStatus status) => status switch
    {
        DeezerIsrcStatus.Matched => "一致",
        DeezerIsrcStatus.NotFound => "見つからない",
        DeezerIsrcStatus.LayoutMismatch => "曲数・並びが違う",
        DeezerIsrcStatus.OtherProductsAlbum => "ほかの盤のアルバム",
        DeezerIsrcStatus.Ambiguous => "候補が割れた",
        _ => status.ToString()
    };
}
