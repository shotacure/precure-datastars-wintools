using System.Diagnostics;
using PrecureDataStars.Data.Db;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Data;
using PrecureDataStars.SiteBuilder.Generators;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

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

        // スタッフ表示用の人物リンク解決ヘルパ（v1.3.0 追加）。
        // person_alias_persons 全件を 1 度だけロードして alias_id → person_id 逆引き辞書を作る。
        // SeriesGenerator のエピソード一覧表と EpisodeGenerator のスタッフセクションで共有する。
        var staffLinkResolver = await StaffNameLinkResolver.CreateAsync(factory, ct).ConfigureAwait(false);

        // 人物・企業・プリキュアページは「クレジット階層を 1 度全走査して逆引きインデックスを作る」コストが高いので、
        // 両ジェネレータでインデックスを共有する。シリーズ・エピソードと違ってクレジットへの依存が
        // インデックス全件読みになるため、1 ビルドあたり 1 回に限定する。
        // v1.3.0 後半でシリーズ詳細の主要スタッフ表でも CreditInvolvementIndex を使うようにしたため、
        // 構築タイミングをホーム・About 直後（SeriesGenerator より前）に移動。
        var involvementIndex = await CreditInvolvementIndex.BuildAsync(ctx, factory, ct).ConfigureAwait(false);

        // 各 Generator 起動。
        await new HomeGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        new AboutGenerator(ctx, pageRenderer).Generate();
        await new SeriesGenerator(ctx, pageRenderer, factory, staffLinkResolver, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        await new EpisodeGenerator(ctx, pageRenderer, factory, staffLinkResolver).GenerateAsync(ct).ConfigureAwait(false);

        await new PersonsGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        await new CompaniesGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        // プリキュア・キャラクター系ページは ByCharacterAlias 逆引き（CHARACTER_VOICE エントリ経由）に
        // 依存するため、CreditInvolvementIndex 構築後に走らせる。
        await new PrecuresGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        await new CharactersGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);

        // 商品・楽曲ページ（音楽カタログ系）。CreditInvolvementIndex には依存しないので順序自由。
        await new ProductsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        await new SongsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);

        // 統計系ページ（役職別ランキング + 声優ランキング）。
        // CreditInvolvementIndex の集約結果に依存するため、人物・企業・プリキュア系より後ろで実行。
        await new RolesStatsGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        await new VoiceCastStatsGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);

        // 統計セクションのランディング + サブタイトル統計 + エピソード尺統計（v1.3.0 後半追加）。
        // RolesStatsGenerator / VoiceCastStatsGenerator は /stats/roles/ と /stats/voice-cast/ 配下を作るので、
        // /stats/ ランディングページ自体は両者の後で別途生成する（既存ジェネレータを壊さない方針）。
        new StatsLandingGenerator(ctx, pageRenderer).Generate();
        await new SubtitleStatsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        await new EpisodePartStatsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);

        // サイト内検索の静的 JSON インデックス（v1.3.0 後半追加）。
        // クライアント側 JS による全文検索を成立させるためのインデックスファイル。
        // 本ジェネレータは SeoGenerator の前に走らせる（SEO は最終工程としたいため）。
        await new SearchIndexGenerator(ctx, config, factory).GenerateAsync(ct).ConfigureAwait(false);

        // SEO 関連ファイル（sitemap.xml / robots.txt）。
        // 全ページの書き出しが完了した後に PageRenderer.WrittenPages を引いて URL リストを構築するため、
        // パイプラインの最後に実行する。
        await new SeoGenerator(ctx, config, pageRenderer).GenerateAsync(ct).ConfigureAwait(false);

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
