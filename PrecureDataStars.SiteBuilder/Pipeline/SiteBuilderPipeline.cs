using System.Diagnostics;
using PrecureDataStars.Data.Db;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Data;
using PrecureDataStars.SiteBuilder.Generators;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// SiteBuilder の全体オーケストレータ。
/// <para>
/// 設定ロード → DB 接続初期化 → 共有データロード → 静的アセットコピー →
/// 各 Generator 起動 → サマリ出力、までを順番に実行する。
/// 例外は呼び出し元（<see cref="Program"/>）にそのまま伝播する。
/// </para>
/// </summary>
public sealed class SiteBuilderPipeline
{
    /// <summary>
    /// 1 回のフルビルドを実行する。
    /// </summary>
    public async Task RunAsync(BuildConfig config, CancellationToken ct = default)
    {
        var logger = new BuildLogger();
        var summary = new BuildSummary();
        var stopwatch = Stopwatch.StartNew();

        logger.Section("SiteBuilder Start");
        logger.Info($"Output directory : {config.OutputDirectory}");
        logger.Info($"Base URL         : {(string.IsNullOrEmpty(config.BaseUrl) ? "(none)" : config.BaseUrl)}");
        logger.Info($"Site name        : {config.SiteName}");

        // 出力ディレクトリは存在しなければ作る。既存ファイルは消さない方針
        // （差分ビルド前提でローカル運用する想定。最初のうちは手動で out/site/ を消すこと）。
        Directory.CreateDirectory(config.OutputDirectory);

        // 静的アセット（site.css 等）を出力ルートにコピー。
        CopyStaticAssets(config, logger);

        // DB 接続。各 Generator は本ファクトリを使い回す。
        var factory = new MySqlConnectionFactory(new DbConfig(config.ConnectionString));

        // 共有データロード。
        var ctx = await SiteDataLoader.LoadAsync(config, logger, summary, factory, ct).ConfigureAwait(false);

        // テンプレ → ページ書き出しヘルパー。
        var renderer = new ScribanRenderer();
        var pageRenderer = new PageRenderer(renderer, config, summary);

        // 各 Generator 起動（タスク 1〜3 範囲）。
        new HomeGenerator(ctx, pageRenderer).Generate();
        new AboutGenerator(ctx, pageRenderer).Generate();
        // SeriesGenerator は v1.3.0 fix9 でエピソード一覧にスタッフ情報を付与するため非同期化された。
        await new SeriesGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        await new EpisodeGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);

        // サマリ表示。
        stopwatch.Stop();
        logger.Section("Build Summary");
        logger.Info($"Pages generated  : {summary.PagesGenerated}");
        foreach (var (section, count) in summary.PagesBySection.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            logger.Info($"  {section,-12} : {count}");
        logger.Info($"Warnings         : {logger.WarningCount}");
        logger.Info($"Elapsed          : {stopwatch.Elapsed.TotalSeconds:0.0} sec");
    }

    /// <summary>
    /// <c>wwwroot/</c> 配下を出力ルートに丸ごとコピーする。
    /// </summary>
    private static void CopyStaticAssets(BuildConfig config, BuildLogger logger)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!Directory.Exists(src))
        {
            logger.Warn($"wwwroot ディレクトリが見つかりません: {src}");
            return;
        }

        // 単純な再帰コピー。少数のアセットのみ想定なので素朴な実装。
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(config.OutputDirectory, rel));
        }
        var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(src, file);
            var dst = Path.Combine(config.OutputDirectory, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, overwrite: true);
        }
        logger.Info($"static assets copied: {files.Length} files");
    }
}
