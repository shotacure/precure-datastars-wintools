using System.Diagnostics;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
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

        // 役職系譜（role_successions）を読んで Resolver を構築する
        // （v1.3.0 ブラッシュアップ続編で追加）。
        // RolesStatsGenerator がクラスタ統合集計を行うために必要。読み込みは 1 ビルド 1 回限り。
        var roleSuccessorResolver = await BuildRoleSuccessorResolverAsync(factory, ct).ConfigureAwait(false);

        // クレジット横断のカバレッジラベルをここで 1 回だけ算出して BuildContext に詰める
        // （v1.3.0 ブラッシュアップ続編で追加）。
        // プリキュア・キャラ・人物・企業・団体・シリーズ・エピソードの各詳細／索引ページから参照され、
        // 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」をサイト全体共通で表示する。
        // CreditInvolvementIndex 構築後でなければ算出できないので、ここがタイミング上の最早地点。
        {
            var creditEpisodeIds = StatsCoverageLabel.CollectEpisodeIdsWithCredits(involvementIndex);
            var latestCreditEpisode = StatsCoverageLabel.FindLatestTvEpisodeWithCredits(ctx, creditEpisodeIds);
            ctx.CreditCoverageLabel = StatsCoverageLabel.Build(latestCreditEpisode);
        }

        // 各 Generator 起動。
        await new HomeGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        new AboutGenerator(ctx, pageRenderer).Generate();

        // 法律・運営情報系の補助ページ群（v1.3.1 追加）。
        // プライバシーポリシー・免責事項・お問い合わせの 3 ページを生成し、
        // ヘッダナビ／フッタからのリンク導線で各ページに到達できるようにする。
        // ホーム / About と同じく DB 依存の無いシンプルな静的ページのため、本タイミングで実行。
        new PolicyPagesGenerator(ctx, config, pageRenderer).Generate();

        // 404 ページ（v1.3.1 stage3 追加）。出力ルート直下に /404.html を書き出す特例ジェネレータ。
        // sitemap.xml には載らない（PageRenderer.RenderAndWriteToOutputFile が _writtenPages に
        // 登録しない仕様）。S3 / CloudFront などの ErrorDocument 設定で利用する前提。
        // DB 依存が無いので About/Policy 群と並べて、ここで実行する。
        new NotFoundGenerator(ctx, pageRenderer).Generate();

        // v1.3.0 続編 第 N+3 弾：SeriesGenerator のインスタンスを変数保持する。
        // GenerateAsync 完了後、エピソード単位 staff サマリの memoize 結果を
        // GetEpisodeStaffSummaries() 経由で取り出し、EpisodesIndexGenerator に渡す
        // （/episodes/ ランディングのスタッフ段表示用）。
        var seriesGenerator = new SeriesGenerator(ctx, pageRenderer, factory, staffLinkResolver, involvementIndex, roleSuccessorResolver);
        await seriesGenerator.GenerateAsync(ct).ConfigureAwait(false);
        await new EpisodeGenerator(ctx, pageRenderer, factory, staffLinkResolver, roleSuccessorResolver).GenerateAsync(ct).ConfigureAwait(false);

        // エピソード一覧ランディング /episodes/（v1.3.0 公開直前のデザイン整理で新設）。
        // 全 TV シリーズのエピソードをシリーズ別セクションで折り畳み一覧化する単一ページ。
        // ホームのデータベース統計セクション「エピソード」ボックスのリンク先として機能する。
        // EpisodeGenerator が全エピソード詳細を書き終えた後に実行することで、
        // 内部リンクの妥当性(生成済みエピソード URL を指す)を担保する。
        // v1.3.0 続編 第 N+3 弾：SeriesGenerator から memoize 済みのエピソード staff サマリ辞書を渡す。
        new EpisodesIndexGenerator(ctx, pageRenderer, seriesGenerator.GetEpisodeStaffSummaries()).Generate();

        await new PersonsGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        await new CompaniesGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        // プリキュア・キャラクター系ページは ByCharacterAlias 逆引き（CHARACTER_VOICE エントリ経由）に
        // 依存するため、CreditInvolvementIndex 構築後に走らせる。
        await new PrecuresGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        await new CharactersGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);

        // 商品・楽曲ページ（音楽カタログ系）。CreditInvolvementIndex には依存しないので順序自由。
        await new ProductsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        // v1.3.0 公開直前のデザイン整理 第 N 弾：楽曲詳細で構造化クレジット（song_credits /
        // song_recording_singers）の名義リンク化と役職リンク化を行うため、StaffNameLinkResolver と
        // RoleSuccessorResolver を共有渡し。EpisodeGenerator / SeriesGenerator と同じ流儀。
        await new SongsGenerator(ctx, pageRenderer, factory, staffLinkResolver, roleSuccessorResolver).GenerateAsync(ct).ConfigureAwait(false);

        // 音楽カテゴリのランディング + 劇伴ページ群（v1.3.0 ブラッシュアップ続編で新設）。
        // /songs/（楽曲）の生成後に走らせて、/music/ ランディングから両方へ誘導できるようにする。
        await new MusicGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);

        // 統計系ページ（役職別ランキング + 声優ランキング）。
        // CreditInvolvementIndex の集約結果に依存するため、人物・企業・プリキュア系より後ろで実行。
        await new RolesStatsGenerator(ctx, pageRenderer, factory, involvementIndex, roleSuccessorResolver).GenerateAsync(ct).ConfigureAwait(false);
        await new VoiceCastStatsGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);

        // 統計セクションのランディング + サブタイトル統計 + エピソード尺統計（v1.3.0 後半追加）。
        // RolesStatsGenerator / VoiceCastStatsGenerator は /stats/roles/ と /stats/voice-cast/ 配下を作るので、
        // /stats/ ランディングページ自体は両者の後で別途生成する（既存ジェネレータを壊さない方針）。
        // v1.3.0 ブラッシュアップ続編：StatsLandingGenerator は async + IConnectionFactory 受け取り
        // （エピソード尺軸ラベルでパート情報集合をクエリするため）。クレジット軸は ctx.CreditCoverageLabel から拾う。
        await new StatsLandingGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        await new SubtitleStatsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        await new EpisodePartStatsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);

        // サイト内検索の静的 JSON インデックス（v1.3.0 後半追加）。
        // クライアント側 JS による全文検索を成立させるためのインデックスファイル。
        // 本ジェネレータは SeoGenerator の前に走らせる（SEO は最終工程としたいため）。
        await new SearchIndexGenerator(ctx, config, factory).GenerateAsync(ct).ConfigureAwait(false);

        // SEO 関連ファイル（sitemap.xml / robots.txt / ads.txt）。
        // 全ページの書き出しが完了した後に PageRenderer.WrittenPages を引いて URL リストを構築するため、
        // パイプラインの最後に実行する。v1.3.1 で ads.txt 出力と robots.txt 強化を追加。
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

    /// <summary>
    /// 役職マスタと役職系譜を読み込んで <see cref="RoleSuccessorResolver"/> を構築する
    /// （v1.3.0 ブラッシュアップ続編で追加）。
    /// 系譜（role_successions）は分裂・併合を含む多対多関係なので、Resolver はマスタと系譜の
    /// 両方から無向グラフを組み立ててクラスタ（連結成分）を割り出す。
    /// </summary>
    private static async Task<RoleSuccessorResolver> BuildRoleSuccessorResolverAsync(
        IConnectionFactory factory,
        CancellationToken ct)
    {
        var rolesRepo = new RolesRepository(factory);
        var successionsRepo = new RoleSuccessionsRepository(factory);
        var roles = await rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var successions = await successionsRepo.GetAllAsync(ct).ConfigureAwait(false);
        return new RoleSuccessorResolver(roles, successions);
    }
}
