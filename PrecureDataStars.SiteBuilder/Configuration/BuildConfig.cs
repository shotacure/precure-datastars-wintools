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

    private BuildConfig(string connectionString, string outputDirectory, string baseUrl, string siteName)
    {
        ConnectionString = connectionString;
        OutputDirectory = outputDirectory;
        BaseUrl = baseUrl;
        SiteName = siteName;
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

        return new BuildConfig(cs, outputDir, baseUrl, siteName);
    }
}
