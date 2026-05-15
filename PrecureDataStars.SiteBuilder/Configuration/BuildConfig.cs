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

    /// <summary>
    /// サイトの公開（初公開）年。フッタの著作権表記に使用する
    /// （v1.3.0 続編 で追加）。
    /// 例: <c>2026</c> が設定されており、現在年が同じ <c>2026</c> なら表記は
    /// 「© 2026 Shota (SHOWTIME).」となり、現在年が <c>2027</c> 以降になったら
    /// 「© 2026-2027 Shota (SHOWTIME).」のような期間表記に切り替わる。
    /// App.config 未指定時の既定値は <c>2026</c>。
    /// </summary>
    public int PublishedYear { get; }

    /// <summary>
    /// OGP の <c>og:image</c> として使うサイト共通既定画像の絶対 URL（v1.3.1 追加）。
    /// Generator 側で個別ページ専用画像を <see cref="Rendering.LayoutModel.OgImage"/> に明示指定しなかった場合、
    /// 本値が <see cref="Rendering.PageRenderer"/> 経由で自動補完される。
    /// 本値が設定されている全ページは Twitter カードが <c>summary_large_image</c> 形式となり、
    /// SNS でのリンクプレビューが大きなカードで表示される。
    /// 推奨画像サイズは 1200×630 ピクセル。空文字なら従来通り画像未設定として扱う。
    /// </summary>
    public string DefaultOgImage { get; }

    private BuildConfig(
        string connectionString,
        string outputDirectory,
        string baseUrl,
        string siteName,
        string ga4MeasurementId,
        string googleSiteVerification,
        string googleAdSenseClientId,
        int publishedYear,
        string defaultOgImage)
    {
        ConnectionString = connectionString;
        OutputDirectory = outputDirectory;
        BaseUrl = baseUrl;
        SiteName = siteName;
        Ga4MeasurementId = ga4MeasurementId;
        GoogleSiteVerification = googleSiteVerification;
        GoogleAdSenseClientId = googleAdSenseClientId;
        PublishedYear = publishedYear;
        DefaultOgImage = defaultOgImage;
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

        // 公開年（v1.3.0 続編 追加）。App.config の SitePublishedYear で上書き可能。
        // 値が無いまたは不正な場合は 2026 を既定値として採用する。
        // フッタ著作権表記の「公開年〜現在年」期間判定に使う。
        int publishedYear = 2026;
        var rawPublished = ConfigurationManager.AppSettings["SitePublishedYear"];
        if (!string.IsNullOrWhiteSpace(rawPublished)
            && int.TryParse(rawPublished, out var py)
            && py >= 1900 && py <= 9999)
        {
            publishedYear = py;
        }

        // OGP 既定画像（v1.3.1 追加）。絶対 URL でない値はそのまま渡るが、og:image は
        // 仕様上絶対 URL が必須のため、運用上は https:// で始まる URL を設定すること。
        // 未設定時は空文字で運用（og:image を出さない＝Twitter カードは summary 小）。
        var defaultOg = (ConfigurationManager.AppSettings["DefaultOgImage"] ?? "").Trim();

        return new BuildConfig(
            cs, outputDir, baseUrl, siteName,
            ga4.Trim(), gsv.Trim(), ads.Trim(), publishedYear,
            defaultOg);
    }
}
