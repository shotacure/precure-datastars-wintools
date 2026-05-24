using System.Diagnostics;
using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Data;
using PrecureDataStars.SiteBuilder.Generators;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>SiteBuilder の全体オーケストレータ。 設定ロード → DB 接続初期化 → 共有データロード → 静的アセットコピー → 各 Generator 起動 → サマリ出力、までを順番に実行する。 例外は呼び出し元（<see cref="Program"/>）にそのまま伝播する。</summary>
public sealed class SiteBuilderPipeline
{
    /// <summary>1 回のフルビルドを実行する。</summary>
    public async Task RunAsync(BuildConfig config, CancellationToken ct = default)
    {
        // 二段プログレスバー（セクション内 + 全体）の表示器。
        // Logger と PageRenderer の両方に渡し、通常ログ出力時のバー退避と
        // ページ書き出し時のセクション内カウンタ更新を一元化する。
        // using で確実に Finish 経由でタイマ停止・バー消去を行う。
        using var reporter = new ProgressReporter();

        var logger = new BuildLogger(reporter);
        var summary = new BuildSummary();
        var stopwatch = Stopwatch.StartNew();

        logger.Section("SiteBuilder Start");
        logger.Info($"Output directory : {config.OutputDirectory}");
        logger.Info($"Base URL         : {(string.IsNullOrEmpty(config.BaseUrl) ? "(none)" : config.BaseUrl)}");
        logger.Info($"Site name        : {config.SiteName}");

        // 出力ディレクトリは存在しなければ作る。既存ファイルは消さない方針
        // （差分ビルド前提でローカル運用する想定。最初のうちは手動で out/site/ を消すこと）。
        Directory.CreateDirectory(config.OutputDirectory);

        // DB 接続。各 Generator は本ファクトリを使い回す。
        // 進捗の事前予想件数算出（COUNT クエリ）でも本ファクトリを使うため、先に確保する。
        var factory = new MySqlConnectionFactory(new DbConfig(config.ConnectionString));

        // 22 セクション分の進捗枠を事前登録する。
        // ページ書き出し件数の予想は ctx 構築前に SQL の COUNT(*) で軽く取得する
        // （DB の統計から即返るため数十ミリ秒で完了）。事前に予想できないセクションは null
        // にしておき、当該 Generator が完了した時点で実数で確定する。
        var expectedCounts = await ComputeExpectedCountsAsync(factory, ct).ConfigureAwait(false);
        reporter.RegisterSections(BuildSectionPlan(expectedCounts));

        // 静的アセット（site.css 等）を出力ルートにコピー。
        reporter.BeginSection("static_assets");
        CopyStaticAssets(config, logger);
        reporter.PageWritten();
        reporter.EndSection();

        // 共有データロード ＋ クレジット逆引きインデックス／役職系譜／カバレッジラベルの算出までを
        // 1 セクションにまとめる（いずれもページ書き出しではなく前処理のため）。
        reporter.BeginSection("data_load");

        // 共有データロード。
        var ctx = await SiteDataLoader.LoadAsync(config, logger, summary, factory, ct).ConfigureAwait(false);

        // テンプレ → ページ書き出しヘルパー。
        // 進捗バーへのページ書き出し通知も PageRenderer 経由で発火するため、reporter を渡す。
        var renderer = new ScribanRenderer();
        var pageRenderer = new PageRenderer(renderer, config, summary, reporter);

        // スタッフ表示用の人物リンク解決ヘルパ。
        var staffLinkResolver = await StaffNameLinkResolver.CreateAsync(factory, ct).ConfigureAwait(false);

        // 人物・企業・プリキュアページは「クレジット階層を 1 度全走査して逆引きインデックスを作る」コストが高いので、
        // 両ジェネレータでインデックスを共有する。シリーズ・エピソードと違ってクレジットへの依存が
        // インデックス全件読みになるため、1 ビルドあたり 1 回に限定する。
        // シリーズ詳細の主要スタッフ表でも CreditInvolvementIndex を使うため、
        // 構築タイミングはホーム・About 直後（SeriesGenerator より前）。
        var involvementIndex = await CreditInvolvementIndex.BuildAsync(ctx, factory, ct).ConfigureAwait(false);

        // 役職系譜（role_successions）を読んで Resolver を構築する。
        // CreatorsGenerator がクラスタ統合集計（役職詳細・スタッフ一覧）を行うために必要。
        // SeriesGenerator / EpisodeGenerator のスタッフバッジ系譜解決にも共有する。読み込みは 1 ビルド 1 回限り。
        var roleSuccessorResolver = await BuildRoleSuccessorResolverAsync(factory, ct).ConfigureAwait(false);

        // クレジット横断のカバレッジラベルをここで 1 回だけ算出して BuildContext に詰める。
        // プリキュア・キャラ・人物・企業・団体・シリーズ・エピソードの各詳細／索引ページから参照され、
        // 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」をサイト全体共通で表示する。
        // CreditInvolvementIndex 構築後でなければ算出できないので、ここがタイミング上の最早地点。
        {
            var creditEpisodeIds = StatsCoverageLabel.CollectEpisodeIdsWithCredits(involvementIndex);
            var latestCreditEpisode = StatsCoverageLabel.FindLatestTvEpisodeWithCredits(ctx, creditEpisodeIds);
            ctx.CreditCoverageLabel = StatsCoverageLabel.Build(latestCreditEpisode);
        }

        reporter.PageWritten();
        reporter.EndSection();

        // 各 Generator 起動。
        // SeriesGenerator を先に走らせる。GenerateAsync 完了後、エピソード単位 staff サマリの
        // memoize 結果を GetEpisodeStaffSummaries() 経由で取り出し、ホーム（「今後の放送予定」
        // 「最新エピソード」を /episodes/ と同じ episodes-index-section 構造で描画する）と
        // /episodes/ ランディングの両方へ渡す。HomeGenerator がスタッフバッジ段を出すために
        // SeriesGenerator より後に実行する必要があるため、起動順を SeriesGenerator → Home に変更した。
        reporter.BeginSection("series");
        var seriesGenerator = new SeriesGenerator(ctx, pageRenderer, factory, staffLinkResolver, involvementIndex, roleSuccessorResolver);
        await seriesGenerator.GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("episodes");
        await new EpisodeGenerator(ctx, pageRenderer, factory, staffLinkResolver, roleSuccessorResolver).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("home");
        await new HomeGenerator(ctx, pageRenderer, factory, seriesGenerator.GetEpisodeStaffSummaries()).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("about");
        new AboutGenerator(ctx, pageRenderer).Generate();
        reporter.EndSection();

        // 法律・運営情報系の補助ページ群。
        // プライバシーポリシー・免責事項・お問い合わせの 3 ページを生成し、
        // ヘッダナビ／フッタからのリンク導線で各ページに到達できるようにする。
        // ホーム / About と同じく DB 依存の無いシンプルな静的ページのため、本タイミングで実行。
        reporter.BeginSection("policy");
        new PolicyPagesGenerator(ctx, config, pageRenderer).Generate();
        reporter.EndSection();

        // 404 ページ。出力ルート直下に /404.html を書き出す特例ジェネレータ。
        // sitemap.xml には載らない（PageRenderer.RenderAndWriteToOutputFile が _writtenPages に
        // 登録しない仕様）。S3 / CloudFront などの ErrorDocument 設定で利用する前提。
        // DB 依存が無いので About/Policy 群と並べて、ここで実行する。
        reporter.BeginSection("not_found");
        new NotFoundGenerator(ctx, pageRenderer).Generate();
        reporter.EndSection();

        // エピソード一覧ランディング /episodes/。
        // 全 TV シリーズのエピソードをシリーズ別セクションで折り畳み一覧化する単一ページ。
        // ホームのデータベース統計セクション「エピソード」ボックスのリンク先として機能する。
        // EpisodeGenerator が全エピソード詳細を書き終えた後に実行することで、
        // 内部リンクの妥当性(生成済みエピソード URL を指す)を担保する。
        // SeriesGenerator から memoize 済みのエピソード staff サマリ辞書を渡す。
        reporter.BeginSection("episodes_index");
        new EpisodesIndexGenerator(ctx, pageRenderer, seriesGenerator.GetEpisodeStaffSummaries()).Generate();
        reporter.EndSection();

        reporter.BeginSection("persons");
        await new PersonsGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("companies");
        await new CompaniesGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // プリキュア・キャラクター系ページは ByCharacterAlias 逆引き（CHARACTER_VOICE エントリ経由）に
        // 依存するため、CreditInvolvementIndex 構築後に走らせる。
        reporter.BeginSection("precures");
        await new PrecuresGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("characters");
        await new CharactersGenerator(ctx, pageRenderer, factory, involvementIndex).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // 商品・楽曲ページ（音楽カタログ系）。CreditInvolvementIndex には依存しないので順序自由。
        reporter.BeginSection("products");
        await new ProductsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // 楽曲詳細で構造化クレジット（song_credits /
        // song_recording_singers）の名義リンク化と役職リンク化を行うため、StaffNameLinkResolver と
        // RoleSuccessorResolver を共有渡し。EpisodeGenerator / SeriesGenerator と同じ流儀。
        reporter.BeginSection("songs");
        await new SongsGenerator(ctx, pageRenderer, factory, staffLinkResolver, roleSuccessorResolver).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // 音楽カテゴリのランディング + 劇伴ページ群。
        // /songs/（楽曲）の生成後に走らせて、/music/ ランディングから両方へ誘導できるようにする。
        reporter.BeginSection("music");
        await new MusicGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // クリエーター系ページ（ランディング + スタッフ + 役職詳細 + 声の出演）。
        // CreditInvolvementIndex の集約結果に依存するため、人物・企業・プリキュア系より後ろで実行する。
        reporter.BeginSection("creators");
        await new CreatorsGenerator(ctx, pageRenderer, factory, involvementIndex, roleSuccessorResolver).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // 統計セクションのランディング + サブタイトル統計 + エピソード尺統計。
        // /stats/ ランディングはサブタイトル統計とエピソード尺統計の 2 系統を束ねる
        // （クレジット関連の担当話数・声の出演は /creators/ 配下）。
        // StatsLandingGenerator は async + IConnectionFactory 受け取り
        // （エピソード尺軸ラベルでパート情報集合をクエリするため）。
        reporter.BeginSection("stats_landing");
        await new StatsLandingGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("subtitle_stats");
        await new SubtitleStatsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        reporter.BeginSection("episode_part_stats");
        await new EpisodePartStatsGenerator(ctx, pageRenderer, factory).GenerateAsync(ct).ConfigureAwait(false);
        reporter.EndSection();

        // サイト内検索の静的 JSON インデックス。
        // クライアント側 JS による全文検索を成立させるためのインデックスファイル。
        // 本ジェネレータは SeoGenerator の前に走らせる（SEO は最終工程としたいため）。
        // ページ書き出しではないため、ダミーで PageWritten を 1 回呼んで完了扱いにする。
        reporter.BeginSection("search_index");
        await new SearchIndexGenerator(ctx, config, factory).GenerateAsync(ct).ConfigureAwait(false);
        reporter.PageWritten();
        reporter.EndSection();

        // SEO 関連ファイル（sitemap.xml / robots.txt / ads.txt）。
        // 全ページの書き出しが完了した後に PageRenderer.WrittenPages を引いて URL リストを構築するため、
        // パイプラインの最後に実行する。ads.txt 出力と robots.txt 強化を追加。
        // こちらも HTML ページ書き出しではないので、ダミーで PageWritten を 1 回呼ぶ。
        reporter.BeginSection("seo");
        await new SeoGenerator(ctx, config, pageRenderer).GenerateAsync(ct).ConfigureAwait(false);
        reporter.PageWritten();
        reporter.EndSection();

        // ここでプログレスバーを片付けてから最終サマリを出す。
        reporter.Finish();

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
    /// 進捗バーに登録する 22 セクション分のプランを、与えられた予想ページ数辞書から組み立てる。
    /// 辞書に未登録のセクションは Expected = null（= 未確定）で登録し、当該セクションが
    /// EndSection 時に Completed を Expected に確定させる。
    /// </summary>
    private static IEnumerable<(string Id, string Label, int? Expected)> BuildSectionPlan(IReadOnlyDictionary<string, int> expected)
    {
        int? Get(string key) => expected.TryGetValue(key, out var v) ? v : (int?)null;

        yield return ("static_assets",      "静的アセット",     1);
        yield return ("data_load",          "データ読み込み",   1);
        yield return ("series",             "シリーズ",         Get("series"));
        yield return ("episodes",           "エピソード",       Get("episodes"));
        yield return ("home",               "ホーム",           1);
        yield return ("about",              "About",            1);
        yield return ("policy",             "規約ページ",       3);
        yield return ("not_found",          "404",              1);
        yield return ("episodes_index",     "エピソード索引",   1);
        yield return ("persons",            "人物",             Get("persons"));
        yield return ("companies",          "企業",             Get("companies"));
        yield return ("precures",           "プリキュア",       Get("precures"));
        yield return ("characters",         "キャラクター",     Get("characters"));
        yield return ("products",           "商品",             Get("products"));
        yield return ("songs",              "楽曲",             Get("songs"));
        yield return ("music",              "音楽・劇伴",       null);
        yield return ("creators",           "クリエーター",     null);
        yield return ("stats_landing",      "統計ランディング", 1);
        yield return ("subtitle_stats",     "字幕統計",         null);
        yield return ("episode_part_stats", "パート尺統計",     null);
        yield return ("search_index",       "検索索引",         1);
        yield return ("seo",                "SEO ファイル",     1);
    }

    /// <summary>
    /// 進捗バーの母数計算用に、各セクションのページ書き出し予想件数を軽量 SQL で取得する。
    /// 各テーブルの <c>is_deleted = 0</c> 件数に、索引ページ 1 件分を加算する。
    /// 実際の Generator が更に細かいフィルタや派生ページを出す場合は誤差が出るが、進捗バーの
    /// 母数としては十分な精度。事前算出できない（music / creators / subtitle_stats / episode_part_stats）
    /// は本辞書に含めず、当該 Generator の EndSection 時に実数で母数を確定させる。
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, int>> ComputeExpectedCountsAsync(
        IConnectionFactory factory,
        CancellationToken ct)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);

        // シリーズ・エピソードはマスタ系で必ず存在する前提。索引 1 ページを加算する。
        int seriesCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM series WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["series"] = seriesCount + 1;

        int episodesCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM episodes WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["episodes"] = episodesCount;

        int personsCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM persons WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["persons"] = personsCount + 1;

        int companiesCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM companies WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["companies"] = companiesCount + 1;

        int precuresCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM precures WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["precures"] = precuresCount + 1;

        int charactersCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM characters WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["characters"] = charactersCount + 1;

        int productsCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM products WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["products"] = productsCount + 1;

        int songsCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT COUNT(*) FROM songs WHERE is_deleted = 0", cancellationToken: ct))
            .ConfigureAwait(false);
        result["songs"] = songsCount + 1;

        return result;
    }

    /// <summary><c>wwwroot/</c> 配下を出力ルートに丸ごとコピーする。</summary>
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

    /// <summary>役職マスタと役職系譜を読み込んで RoleSuccessorResolver を構築する。</summary>
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
