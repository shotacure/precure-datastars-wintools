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
/// 全 HTML ページの生成が完了した後にパイプラインの最後で走り、下記ファイルを書き出す：
/// </para>
/// <list type="bullet">
///   <item><description><c>/sitemap.xml</c> — <see cref="PageRenderer.WrittenPages"/> から
///     全ページの URL を引き、<c>&lt;urlset&gt;</c> 形式で列挙。lastmod は当該ビルド時刻、priority は
///     セクション種別から導出（ホーム=1.0、シリーズ・エピソード=0.8、それ以外=0.5）。</description></item>
///   <item><description><c>/robots.txt</c> — 通常クローラへの全許可 + sitemap.xml への参照に加え、
///     リソース消費の大きい外部クローラ（AI 学習スクレイパ系・バックリンク収集系）への
///     個別 Disallow を v1.3.1 で追加。主要検索エンジンには控えめな <c>Crawl-delay</c> を付ける。</description></item>
///   <item><description><c>/ads.txt</c> — Google AdSense のパブリッシャー ID が設定されているときに
///     IAB 標準形式の 1 行（DIRECT 関係）を出力する（v1.3.1 追加）。</description></item>
/// </list>
/// <para>
/// BaseUrl が空の場合は sitemap.xml の <c>&lt;loc&gt;</c> 値が組み立てられないため、
/// 警告ログを出力したうえで sitemap.xml は生成せず robots.txt のみ書き出す（クロールを許可するか
/// 禁止するかはクローラ任せ）。
/// </para>
/// <para>
/// 注意：本ジェネレータが出力する <c>robots.txt</c> はあくまで紳士協定であり、悪意のあるクローラを
/// 強制的に止める手段ではない。実運用での流量制限は CDN（Cloudflare 等）や WAF のレートリミット
/// 機能で別途行うことを前提とする。
/// </para>
/// </summary>
public sealed class SeoGenerator
{
    private readonly BuildContext _ctx;
    private readonly BuildConfig _config;
    private readonly PageRenderer _pageRenderer;

    /// <summary>
    /// <c>robots.txt</c> で個別に <c>Disallow: /</c> を当てる外部クローラの User-Agent 一覧（v1.3.1 追加）。
    /// <list type="bullet">
    ///   <item>前半：AI 学習・LLM データ収集系（GPTBot / ClaudeBot 等）。本サイトのコンテンツは
    ///     ファン個人の収集物であり、商用 AI モデル学習への無断利用は意図しないため明示拒否する。</item>
    ///   <item>後半：バックリンク・SEO 解析系（AhrefsBot / SemrushBot / MJ12bot 等）。サイトの解析データを
    ///     商用販売する目的のクローラで、本サイトに対するメリットが無くサーバ負荷だけがかかるため拒否。</item>
    /// </list>
    /// 紳士協定であり強制力は無いが、最初のリクエスト前に robots.txt を読む行儀の良いクローラには有効。
    /// </summary>
    private static readonly string[] BlockedCrawlerUserAgents = new[]
    {
        // ── AI 学習・LLM データ収集系 ──
        "GPTBot",
        "ChatGPT-User",
        "OAI-SearchBot",
        "ClaudeBot",
        "anthropic-ai",
        "Claude-Web",
        "CCBot",
        "Google-Extended",
        "Applebot-Extended",
        "Bytespider",
        "FacebookBot",
        "Diffbot",
        "Omgilibot",
        "Omgili",
        "PerplexityBot",
        "cohere-ai",
        // ── バックリンク・SEO 解析系 ──
        "AhrefsBot",
        "SemrushBot",
        "MJ12bot",
        "DotBot",
        "PetalBot",
        "BLEXBot",
        "DataForSeoBot",
        "SeznamBot",
        "rogerbot",
        "AspiegelBot",
    };

    /// <summary>
    /// 主要検索エンジン向けに付ける <c>Crawl-delay</c> 値（秒、v1.3.1 追加）。
    /// Google 公式は Crawl-delay を無視するが、Bing 等は尊重する。
    /// 10 秒であれば検索順位への悪影響は無く、過剰なクロールを抑制する効果が見込める範囲。
    /// </summary>
    private const int MainCrawlerCrawlDelaySeconds = 10;

    /// <summary>
    /// AdSense の <c>ads.txt</c> に書く Google 公式の関係識別子（v1.3.1 追加）。
    /// IAB 仕様で固定値の TAG-ID 部分。AdSense のパブリッシャ全員が共通で使う。
    /// </summary>
    private const string GoogleAdSenseAdsTxtTagId = "f08c47fec0942fa0";

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
            WriteAdsTxtIfConfigured();
            return Task.CompletedTask;
        }

        WriteSitemapXml();
        WriteRobotsTxt(includeSitemap: true);
        WriteAdsTxtIfConfigured();

        _ctx.Logger.Success($"SEO: sitemap.xml ({_pageRenderer.WrittenPages.Count} URL) + robots.txt"
            + (string.IsNullOrEmpty(_config.GoogleAdSenseClientId) ? "" : " + ads.txt"));
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
    /// <c>/robots.txt</c> を書き出す（v1.3.1 で内容を拡張）。
    /// 構成は下記 3 ブロックの順で出力する：
    /// <list type="number">
    ///   <item><description>個別の悪質クローラに対する <c>Disallow: /</c> 群（AI 学習・SEO 解析系）。</description></item>
    ///   <item><description>主要検索エンジン向けの <c>Crawl-delay</c> 設定。</description></item>
    ///   <item><description>その他全クローラへの <c>Allow: /</c> と sitemap.xml への参照。</description></item>
    /// </list>
    /// </summary>
    /// <param name="includeSitemap">true なら sitemap.xml への参照行を含める。BaseUrl 未設定時は false を渡す。</param>
    private void WriteRobotsTxt(bool includeSitemap)
    {
        var sb = new StringBuilder();

        // 個別ブロックの説明コメント。robots.txt はコメント許容なので、運用者にも意図が伝わるよう付記。
        sb.AppendLine("# precure-datastars robots.txt");
        sb.AppendLine("# 個別 User-agent ブロックは AI 学習・SEO 解析系の高負荷クローラに対する明示拒否です。");
        sb.AppendLine("# 紳士協定であり強制力はありません（実流量制御は別途 CDN / WAF 側で行います）。");
        sb.AppendLine();

        // ブロック 1：個別クローラの拒否（1 UA あたり 2 行：User-agent と Disallow）。
        foreach (var ua in BlockedCrawlerUserAgents)
        {
            sb.AppendLine($"User-agent: {ua}");
            sb.AppendLine("Disallow: /");
            sb.AppendLine();
        }

        // ブロック 2：主要検索エンジンへの Crawl-delay（Google は無視するが、Bing 等は尊重）。
        sb.AppendLine($"User-agent: Bingbot");
        sb.AppendLine($"Crawl-delay: {MainCrawlerCrawlDelaySeconds}");
        sb.AppendLine();
        sb.AppendLine($"User-agent: Slurp");
        sb.AppendLine($"Crawl-delay: {MainCrawlerCrawlDelaySeconds}");
        sb.AppendLine();
        sb.AppendLine($"User-agent: DuckDuckBot");
        sb.AppendLine($"Crawl-delay: {MainCrawlerCrawlDelaySeconds}");
        sb.AppendLine();

        // ブロック 3：その他すべて許可 + サイトマップ参照。
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

    /// <summary>
    /// <c>/ads.txt</c> を書き出す（v1.3.1 追加）。
    /// <para>
    /// IAB の Authorized Digital Sellers 仕様に従い、Google AdSense のパブリッシャー ID が
    /// 設定されているときだけ「<c>google.com, pub-XXXXXXXXXXXXXXXX, DIRECT, f08c47fec0942fa0</c>」の
    /// 標準 1 行を出力する。AdSense クライアント ID は App.config の <c>GoogleAdSenseClientId</c> 値
    /// （<c>ca-pub-XXXXXXXXXXXXXXXX</c> 形式）から接頭辞 <c>ca-</c> を除いた <c>pub-XXXXXXXXXXXXXXXX</c>
    /// 部分を sellers ID として使う。
    /// </para>
    /// <para>
    /// 未設定時は ads.txt を生成しない。設定があっても <c>pub-</c> で始まる形に正規化できない値の場合は
    /// 警告を出して書き出しを見送る（不正な ads.txt は AdSense 審査で警告対象となるため）。
    /// </para>
    /// </summary>
    private void WriteAdsTxtIfConfigured()
    {
        var clientId = _config.GoogleAdSenseClientId;
        if (string.IsNullOrEmpty(clientId)) return;

        // "ca-pub-XXXX..." → "pub-XXXX..." に正規化（既に "pub-" で始まる入力にも対応）。
        string sellersId;
        if (clientId.StartsWith("ca-pub-", StringComparison.OrdinalIgnoreCase))
        {
            sellersId = clientId.Substring("ca-".Length);
        }
        else if (clientId.StartsWith("pub-", StringComparison.OrdinalIgnoreCase))
        {
            sellersId = clientId;
        }
        else
        {
            _ctx.Logger.Warn(
                $"GoogleAdSenseClientId='{clientId}' が pub-/ca-pub- 形式ではないため ads.txt の生成をスキップします。");
            return;
        }

        var sb = new StringBuilder();
        // 1 行目はコメント（運用者向け）。空行を挟むと一部のクローラが警告するため挟まない。
        sb.AppendLine("# precure-datastars ads.txt");
        sb.AppendLine($"google.com, {sellersId}, DIRECT, {GoogleAdSenseAdsTxtTagId}");

        var outputFile = Path.Combine(_config.OutputDirectory, "ads.txt");
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, sb.ToString());
    }
}
