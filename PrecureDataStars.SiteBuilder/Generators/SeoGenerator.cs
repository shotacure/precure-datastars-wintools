using System.Text;
using System.Xml;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// SEO 関連の補助ファイルを出力するジェネレータ（v1.3.0 タスク追加）。
/// <para>
/// 全 HTML ページの生成が完了した後にパイプラインの最後で走り、下記 2 ファイルを書き出す：
/// </para>
/// <list type="bullet">
///   <item><description><c>/sitemap.xml</c> — <see cref="PageRenderer.WrittenPages"/> から
///     全ページの URL を引き、<c>&lt;urlset&gt;</c> 形式で列挙。lastmod は当該ビルド時刻、priority は
///     セクション種別から導出（ホーム=1.0、シリーズ・エピソード=0.8、それ以外=0.5）。</description></item>
///   <item><description><c>/robots.txt</c> — クローラ許可 + sitemap.xml への参照のみのシンプルな構成。</description></item>
/// </list>
/// <para>
/// BaseUrl が空の場合は sitemap.xml の <c>&lt;loc&gt;</c> 値が組み立てられないため、
/// 警告ログを出力したうえで sitemap.xml は生成せず robots.txt のみ書き出す（クロールを許可するか
/// 禁止するかはクローラ任せ）。
/// </para>
/// </summary>
public sealed class SeoGenerator
{
    private readonly BuildContext _ctx;
    private readonly BuildConfig _config;
    private readonly PageRenderer _pageRenderer;

    public SeoGenerator(BuildContext ctx, BuildConfig config, PageRenderer pageRenderer)
    {
        _ctx = ctx;
        _config = config;
        _pageRenderer = pageRenderer;
    }

    public Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating SEO files");

        if (string.IsNullOrEmpty(_config.BaseUrl))
        {
            _ctx.Logger.Info("BaseUrl が未設定のため sitemap.xml の生成をスキップします（robots.txt のみ書き出し）。");
            WriteRobotsTxt(includeSitemap: false);
            return Task.CompletedTask;
        }

        WriteSitemapXml();
        WriteRobotsTxt(includeSitemap: true);

        _ctx.Logger.Success($"SEO: sitemap.xml ({_pageRenderer.WrittenPages.Count} URL) + robots.txt");
        return Task.CompletedTask;
    }

    /// <summary>
    /// <c>/sitemap.xml</c> を書き出す。XmlWriter 経由で書き出すことで、URL に &amp; や記号が含まれていても
    /// 正しく XML エスケープされる（手書き連結だとエスケープ漏れが起きやすいため）。
    /// </summary>
    private void WriteSitemapXml()
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false
        };

        var outputFile = Path.Combine(_config.OutputDirectory, "sitemap.xml");
        PathUtil.EnsureParentDirectory(outputFile);

        using var writer = XmlWriter.Create(outputFile, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

        foreach (var page in _pageRenderer.WrittenPages)
        {
            writer.WriteStartElement("url");
            writer.WriteElementString("loc", _config.BaseUrl + page.UrlPath);
            // lastmod は ISO-8601（W3C Datetime）形式。秒単位までで十分。
            writer.WriteElementString("lastmod", page.LastModified.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteElementString("changefreq", DeriveChangeFreq(page.Section));
            writer.WriteElementString("priority", DerivePriority(page.UrlPath, page.Section).ToString("F1"));
            writer.WriteEndElement(); // </url>
        }

        writer.WriteEndElement(); // </urlset>
        writer.WriteEndDocument();
    }

    /// <summary>
    /// セクション種別から changefreq を導出。サイト構成に合わせた控えめな更新頻度を返す。
    /// </summary>
    private static string DeriveChangeFreq(string section) => section switch
    {
        "home" => "weekly",
        "series" => "weekly",      // 新エピソード追加で更新される
        "episodes" => "monthly",
        "stats" => "weekly",        // ランキングは新エピソードで動く
        _ => "monthly"
    };

    /// <summary>
    /// URL パスとセクション種別から priority を導出。
    /// ホーム > シリーズ詳細・エピソード詳細 > 各種詳細ページ > 索引・統計、の順に優先度を付ける。
    /// </summary>
    private static double DerivePriority(string urlPath, string section)
    {
        if (urlPath == "/") return 1.0;

        // シリーズ詳細・エピソード詳細はサイトの中核なので高め。
        if (section == "series") return 0.8;
        if (section == "episodes") return 0.8;

        // 詳細ページ（プリキュア・キャラ・人物・企業・商品・楽曲）は中程度。
        if (urlPath.StartsWith("/precures/") && urlPath.Length > "/precures/".Length) return 0.7;
        if (urlPath.StartsWith("/characters/") && urlPath.Length > "/characters/".Length) return 0.7;
        if (urlPath.StartsWith("/persons/") && urlPath.Length > "/persons/".Length) return 0.6;
        if (urlPath.StartsWith("/companies/") && urlPath.Length > "/companies/".Length) return 0.6;
        if (urlPath.StartsWith("/products/") && urlPath.Length > "/products/".Length) return 0.6;
        if (urlPath.StartsWith("/songs/") && urlPath.Length > "/songs/".Length) return 0.6;

        // 索引ページ・統計ページは低め。
        return 0.5;
    }

    /// <summary>
    /// <c>/robots.txt</c> を書き出す。クロール許可 + sitemap.xml への参照のみのシンプル構成。
    /// </summary>
    /// <param name="includeSitemap">true なら sitemap.xml への参照行を含める。BaseUrl 未設定時は false を渡す。</param>
    private void WriteRobotsTxt(bool includeSitemap)
    {
        var sb = new StringBuilder();
        sb.AppendLine("User-agent: *");
        sb.AppendLine("Allow: /");
        sb.AppendLine();
        if (includeSitemap)
        {
            sb.AppendLine($"Sitemap: {_config.BaseUrl}/sitemap.xml");
        }

        var outputFile = Path.Combine(_config.OutputDirectory, "robots.txt");
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, sb.ToString());
    }
}
