using System.Configuration;

namespace PrecureDataStars.SiteBuilder.Configuration;

/// <summary>
/// SiteBuilder の実行時設定を保持する不変オブジェクト。
/// <para>
/// App.config の <c>connectionStrings</c> および <c>appSettings</c> から値を読み出し、
/// パイプライン全体で共有する設定を組み立てる。値の取り方とデフォルト処理を
/// 1 か所に集約することで、Generator 側からは設定の出所を意識せずに済むようにする。
/// </para>
/// </summary>
public sealed class BuildConfig
{
    /// <summary>MySQL 接続文字列（必須）。</summary>
    public string ConnectionString { get; }

    /// <summary>出力先ディレクトリの絶対パス。</summary>
    public string OutputDirectory { get; }

    /// <summary>サイトのベース URL（末尾スラッシュなし）。空文字の場合は相対 URL 運用。</summary>
    public string BaseUrl { get; }

    /// <summary>サイト表示名（ヘッダ・タイトル等で使う）。</summary>
    public string SiteName { get; }

    /// <summary>
    /// Google Analytics 4 のメジャメント ID（例: <c>G-XXXXXXXXXX</c>）。
    /// 空文字の場合は GA4 トラッキングコードを <c>&lt;head&gt;</c> に埋め込まない。
    /// </summary>
    public string Ga4MeasurementId { get; }

    /// <summary>
    /// Google Search Console の所有権確認用トークン
    /// （<c>&lt;meta name="google-site-verification" content="..."&gt;</c> の content 値）。
    /// 空文字の場合は確認用メタタグを <c>&lt;head&gt;</c> に埋め込まない。
    /// </summary>
    public string GoogleSiteVerification { get; }

    /// <summary>
    /// Google AdSense のパブリッシャー ID（例: <c>ca-pub-1234567890123456</c>）。
    /// 設定されていれば自動広告（Auto Ads）モードで <c>&lt;head&gt;</c> に AdSense ローダスクリプトを
    /// 埋め込む。Google が自動的にページ内の最適な位置に広告を配置する。
    /// 空文字の場合は AdSense スクリプトを一切埋め込まない。
    /// </summary>
    public string GoogleAdSenseClientId { get; }

    private BuildConfig(
        string connectionString,
        string outputDirectory,
        string baseUrl,
        string siteName,
        string ga4MeasurementId,
        string googleSiteVerification,
        string googleAdSenseClientId)
    {
        ConnectionString = connectionString;
        OutputDirectory = outputDirectory;
        BaseUrl = baseUrl;
        SiteName = siteName;
        Ga4MeasurementId = ga4MeasurementId;
        GoogleSiteVerification = googleSiteVerification;
        GoogleAdSenseClientId = googleAdSenseClientId;
    }

    /// <summary>
    /// App.config から設定を読み出して <see cref="BuildConfig"/> を構築する。
    /// </summary>
    /// <returns>構築済み設定。</returns>
    /// <exception cref="InvalidOperationException">必須項目（接続文字列）が未設定の場合。</exception>
    public static BuildConfig FromAppConfig()
    {
        // 接続文字列は既存ツール群と同じ "DatastarsMySql" 名で統一
        var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString
            ?? throw new InvalidOperationException(
                "Connection string 'DatastarsMySql' not found. " +
                "App.config の <connectionStrings> に定義してください。");

        // 出力先：未指定時は実行ファイル直下 out/site/。明示指定時は絶対パス化して使う。
        var rawOutput = ConfigurationManager.AppSettings["SiteOutputDir"];
        var outputDir = string.IsNullOrWhiteSpace(rawOutput)
            ? Path.Combine(AppContext.BaseDirectory, "out", "site")
            : Path.GetFullPath(rawOutput);

        // ベース URL は末尾スラッシュを除去して保持する（後段でパスと結合する際の重複回避のため）。
        var rawBase = ConfigurationManager.AppSettings["SiteBaseUrl"] ?? "";
        var baseUrl = rawBase.TrimEnd('/');

        var siteName = ConfigurationManager.AppSettings["SiteName"];
        if (string.IsNullOrWhiteSpace(siteName))
            siteName = "precure-datastars";

        // SEO/アナリティクス設定（v1.3.0 追加）。未設定時は空文字として保持し、
        // _layout.sbn 側で空判定して埋め込み有無を切り替える運用。
        var ga4 = ConfigurationManager.AppSettings["Ga4MeasurementId"] ?? "";
        var gsv = ConfigurationManager.AppSettings["GoogleSiteVerification"] ?? "";
        // AdSense 自動広告（v1.3.0 後半追加）。設定されていれば head に AdSense ローダスクリプトを
        // 埋め込んで Google の自動広告を有効化する。
        var ads = ConfigurationManager.AppSettings["GoogleAdSenseClientId"] ?? "";

        return new BuildConfig(cs, outputDir, baseUrl, siteName, ga4.Trim(), gsv.Trim(), ads.Trim());
    }
}
