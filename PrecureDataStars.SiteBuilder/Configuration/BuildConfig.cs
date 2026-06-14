using System.Configuration;

namespace PrecureDataStars.SiteBuilder.Configuration;

/// <summary>SiteBuilder の実行時設定を保持する不変オブジェクト。 App.config の <c>connectionStrings</c> および <c>appSettings</c> から値を読み出し、 パイプライン全体で共有する設定を組み立てる。値の取り方とデフォルト処理を 1 か所に集約することで、Generator 側からは設定の出所を意識せずに済むようにする。</summary>
public sealed class BuildConfig
{
    /// <summary>MySQL 接続文字列（必須）。</summary>
    public string ConnectionString { get; }

    /// <summary>出力先ディレクトリの絶対パス。</summary>
    public string OutputDirectory { get; }

    /// <summary>読み物（記事 Markdown）原稿ディレクトリの絶対パス。App.config の <c>ArticlesContentDir</c>。
    /// コードと分離した「コンテンツ資産」の置き場で、ビルド時にここを読んで <c>/articles/</c> を生成する
    /// （exe にはバンドルしない）。空文字なら記事 0 件として継続する。</summary>
    public string ArticlesContentDir { get; }

    /// <summary>サイトのベース URL（末尾スラッシュなし）。空文字の場合は相対 URL 運用。</summary>
    public string BaseUrl { get; }

    /// <summary>サイト表示名（ヘッダ・タイトル等で使う）。</summary>
    public string SiteName { get; }

    /// <summary>Google Analytics 4 のメジャメント ID（例: <c>G-XXXXXXXXXX</c>）。 空文字の場合は GA4 トラッキングコードを <c>&lt;head&gt;</c> に埋め込まない。</summary>
    public string Ga4MeasurementId { get; }

    /// <summary>Google Search Console の所有権確認用トークン （&lt;meta name="google-site-verification" content="..."&gt; の content 値）。</summary>
    public string GoogleSiteVerification { get; }

    /// <summary>Google AdSense のパブリッシャー ID（例: ca-pub-1234567890123456）。</summary>
    public string GoogleAdSenseClientId { get; }

    /// <summary>サイトの公開（初公開）年。</summary>
    public int PublishedYear { get; }

    /// <summary>OGP の og:image として使うサイト共通既定画像の絶対 URL。</summary>
    public string DefaultOgImage { get; }

    /// <summary>Amazon アソシエイトのトラッキング ID（例: yourtag-22）。</summary>
    public string AmazonAssociateTag { get; }

    /// <summary>本番モードかどうか。コマンドライン引数 <c>--production</c> 指定時のみ true。
    /// テストモード（既定）では出力先が <c>SiteOutputDirTest</c> に切り替わり、
    /// <see cref="Ga4MeasurementId"/> / <see cref="GoogleAdSenseClientId"/> が空文字に正規化されて
    /// GA4 タグ・AdSense タグ・ads.txt の出力経路ごと止まる
    /// （ID 自体は App.config に常設したまま、起動引数だけで公開ビルドに移行できる）。</summary>
    public bool IsProductionMode { get; }

    /// <summary>デプロイ先 S3 バケット名（例: <c>your-bucket-name</c>）。App.config の <c>AwsS3Bucket</c>。
    /// デプロイ未指定時は空文字でも構わない（<c>--deploy</c> 指定時のみ必須）。</summary>
    public string AwsS3Bucket { get; }

    /// <summary>S3 バケットのリージョン（例: <c>ap-northeast-1</c>）。App.config の <c>AwsRegion</c>。</summary>
    public string AwsRegion { get; }

    /// <summary>認証に使う AWS 名前付きプロファイル名（例: <c>my-deploy-profile</c>）。App.config の <c>AwsProfile</c>。
    /// 空文字の場合は SDK 既定の資格情報解決チェーン（環境変数 / default プロファイル等）に委ねる。</summary>
    public string AwsProfile { get; }

    /// <summary>キャッシュ削除対象の CloudFront Distribution ID（例: <c>EXXXXXXXXXXXXX</c>）。
    /// App.config の <c>CloudFrontDistributionId</c>。空文字なら invalidation をスキップする。</summary>
    public string CloudFrontDistributionId { get; }

    /// <summary>デプロイの「削除（orphan 掃除）」対象から除外するキー接頭辞の一覧。
    /// App.config の <c>AwsDeployProtectedPrefixes</c>（カンマ区切り）。手動配置物を守るための安全弁で、
    /// 既定は空（生成物に無い S3 オブジェクトはすべて削除候補）。</summary>
    public IReadOnlyList<string> AwsDeployProtectedPrefixes { get; }

    /// <summary>デプロイ実行時の意図（<c>--deploy</c> / <c>--dry-run</c> / <c>--yes</c> 由来）。</summary>
    public DeployRuntimeOptions Deploy { get; }

    /// <summary>ピンポイントビルドのページフィルタ（<c>--page &lt;path&gt;</c> 由来）。空文字なら全ページ生成。
    /// 非空なら、URL パスに本値を含むページだけを書き出し、それ以外はレンダリングごとスキップする
    /// （単発・並列とも <see cref="Rendering.PageRenderer"/> の書き出しメソッドで弾く）。あわせて
    /// sitemap / 検索インデックスの再生成を抑止し、デプロイ時は orphan 削除も行わない（部分生成の安全策）。</summary>
    public string PageFilter { get; }

    private BuildConfig(
        string connectionString,
        string outputDirectory,
        string articlesContentDir,
        string baseUrl,
        string siteName,
        string ga4MeasurementId,
        string googleSiteVerification,
        string googleAdSenseClientId,
        int publishedYear,
        string defaultOgImage,
        string amazonAssociateTag,
        bool isProductionMode,
        string awsS3Bucket,
        string awsRegion,
        string awsProfile,
        string cloudFrontDistributionId,
        IReadOnlyList<string> awsDeployProtectedPrefixes,
        DeployRuntimeOptions deploy,
        string pageFilter)
    {
        ConnectionString = connectionString;
        OutputDirectory = outputDirectory;
        ArticlesContentDir = articlesContentDir;
        BaseUrl = baseUrl;
        SiteName = siteName;
        Ga4MeasurementId = ga4MeasurementId;
        GoogleSiteVerification = googleSiteVerification;
        GoogleAdSenseClientId = googleAdSenseClientId;
        PublishedYear = publishedYear;
        DefaultOgImage = defaultOgImage;
        AmazonAssociateTag = amazonAssociateTag;
        IsProductionMode = isProductionMode;
        AwsS3Bucket = awsS3Bucket;
        AwsRegion = awsRegion;
        AwsProfile = awsProfile;
        CloudFrontDistributionId = cloudFrontDistributionId;
        AwsDeployProtectedPrefixes = awsDeployProtectedPrefixes;
        Deploy = deploy;
        PageFilter = pageFilter;
    }

    /// <summary>App.config から設定を読み出して <see cref="BuildConfig"/> を構築する。</summary>
    /// <param name="isProductionMode">本番モードかどうか（コマンドライン引数 <c>--production</c> 由来）。
    /// 出力先ディレクトリの選択と、計測・広告系（GA4 / AdSense / ads.txt）の出力可否を決める。</param>
    /// <param name="deploy">デプロイ実行時オプション（<c>--deploy</c> / <c>--dry-run</c> / <c>--yes</c> 由来）。
    /// 未指定なら <see cref="DeployRuntimeOptions.None"/> を渡す。</param>
    /// <returns>構築済み設定。</returns>
    /// <exception cref="InvalidOperationException">必須項目（接続文字列）が未設定の場合。</exception>
    public static BuildConfig FromAppConfig(bool isProductionMode, DeployRuntimeOptions deploy, string pageFilter)
    {
        // 接続文字列は既存ツール群と同じ "DatastarsMySql" 名で統一
        var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString
            ?? throw new InvalidOperationException(
                "Connection string 'DatastarsMySql' not found. " +
                "App.config の <connectionStrings> に定義してください。");

        // 出力先はモードで切り替える。本番は SiteOutputDir、テストは SiteOutputDirTest を使い、
        // 未指定時はそれぞれ実行ファイル直下 out/site/・out/site-test/ にフォールバックする
        // （テストモードが誤って本番ディレクトリへ書き込むことはない）。明示指定時は絶対パス化して使う。
        var rawOutput = isProductionMode
            ? ConfigurationManager.AppSettings["SiteOutputDir"]
            : ConfigurationManager.AppSettings["SiteOutputDirTest"];
        var outputDir = string.IsNullOrWhiteSpace(rawOutput)
            ? Path.Combine(AppContext.BaseDirectory, "out", isProductionMode ? "site" : "site-test")
            : Path.GetFullPath(rawOutput);

        // 読み物原稿ディレクトリ。コードと分離した外部の置き場（リポジトリ直下 content/articles 等）を
        // 絶対パスで指す。未設定なら空文字＝記事 0 件として継続する（ArticlesGenerator 側で警告）。
        var rawArticles = ConfigurationManager.AppSettings["ArticlesContentDir"];
        var articlesDir = string.IsNullOrWhiteSpace(rawArticles) ? "" : Path.GetFullPath(rawArticles);

        // ベース URL は末尾スラッシュを除去して保持する（後段でパスと結合する際の重複回避のため）。
        var rawBase = ConfigurationManager.AppSettings["SiteBaseUrl"] ?? "";
        var baseUrl = rawBase.TrimEnd('/');

        var siteName = ConfigurationManager.AppSettings["SiteName"];
        if (string.IsNullOrWhiteSpace(siteName))
            siteName = "precure-datastars";

        // SEO/アナリティクス設定。未設定時は空文字として保持し、
        // _layout.sbn 側で空判定して埋め込み有無を切り替える運用。
        var ga4 = ConfigurationManager.AppSettings["Ga4MeasurementId"] ?? "";
        var gsv = ConfigurationManager.AppSettings["GoogleSiteVerification"] ?? "";
        // AdSense 自動広告。設定されていれば head に AdSense ローダスクリプトを
        // 埋め込んで Google の自動広告を有効化する。
        var ads = ConfigurationManager.AppSettings["GoogleAdSenseClientId"] ?? "";

        // 公開年。App.config の SitePublishedYear で上書き可能。
        int publishedYear = 2026;
        var rawPublished = ConfigurationManager.AppSettings["SitePublishedYear"];
        if (!string.IsNullOrWhiteSpace(rawPublished)
            && int.TryParse(rawPublished, out var py)
            && py >= 1900 && py <= 9999)
        {
            publishedYear = py;
        }

        // OGP 既定画像。絶対 URL でない値はそのまま渡るが、og:image は
        // 仕様上絶対 URL が必須のため、運用上は https:// で始まる URL を設定すること。
        // 未設定時は空文字で運用（og:image を出さない＝Twitter カードは summary 小）。
        var defaultOg = (ConfigurationManager.AppSettings["DefaultOgImage"] ?? "").Trim();

        // Amazon アソシエイトのトラッキング ID。未設定時は空文字で運用し、
        // 商品詳細の Amazon リンクは tag なしで出力する（リンク自体は出す）。
        var amazonTag = (ConfigurationManager.AppSettings["AmazonAssociateTag"] ?? "").Trim();

        // テストモードでは GA4 / AdSense の ID を空に正規化して、タグ・ads.txt の出力経路ごと止める。
        // ID は App.config に常設したまま運用できる（公開ビルドのたびに値をよける必要がない）。
        var effectiveGa4 = isProductionMode ? ga4.Trim() : "";
        var effectiveAds = isProductionMode ? ads.Trim() : "";

        // AWS デプロイ先設定。値の必須チェックは「実際に --deploy が指定されたとき」に
        // S3DeployService 側で行う（生成だけしたいケースで AWS 設定を強制しないため）。
        var awsBucket = (ConfigurationManager.AppSettings["AwsS3Bucket"] ?? "").Trim();
        var awsRegion = (ConfigurationManager.AppSettings["AwsRegion"] ?? "").Trim();
        var awsProfile = (ConfigurationManager.AppSettings["AwsProfile"] ?? "").Trim();
        var cfDist = (ConfigurationManager.AppSettings["CloudFrontDistributionId"] ?? "").Trim();

        // 削除保護の接頭辞リスト（カンマ区切り）。空要素は除外する。
        var rawProtected = ConfigurationManager.AppSettings["AwsDeployProtectedPrefixes"] ?? "";
        var protectedPrefixes = rawProtected
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return new BuildConfig(
            cs, outputDir, articlesDir, baseUrl, siteName,
            effectiveGa4, gsv.Trim(), effectiveAds, publishedYear,
            defaultOg, amazonTag, isProductionMode,
            awsBucket, awsRegion, awsProfile, cfDist, protectedPrefixes, deploy,
            pageFilter ?? "");
    }
}
