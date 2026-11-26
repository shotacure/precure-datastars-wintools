using System.Linq;
using System.Text;
using System.Xml;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// SEO 関連の補助ファイルを出力するジェネレータ。
/// 全 HTML ページの生成が完了した後にパイプラインの最後で走り、下記ファイルを書き出す：
/// <list type="bullet">
///   <item><description><c>/sitemap.xml</c> — <see cref="PageRenderer.WrittenPages"/> から
///     全ページの URL を引き、<c>&lt;urlset&gt;</c> 形式で列挙。lastmod は各ページ出力の内容ハッシュを
///     永続マニフェストと突き合わせ、内容が実際に変わったビルドの時刻だけを採用する（不変ページは前回値を
///     据え置く。<see cref="SitemapLastmodManifest"/> 参照）。priority は
///     セクション種別から導出（ホーム=1.0、シリーズ・エピソード=0.8、それ以外=0.5）。</description></item>
///   <item><description><c>/robots.txt</c> — すべてのクローラへの全許可 + sitemap.xml への参照。
///     名乗って取りに来るクローラ（検索エンジン・AI 検索・AI の学習・SEO 解析・SNS のプレビューなど）は、
///     量や目的で分けずにどれも通す。個別の <c>Disallow</c> や <c>Crawl-delay</c> は付けない。</description></item>
/// </list>
/// BaseUrl が空の場合は sitemap.xml の <c>&lt;loc&gt;</c> 値が組み立てられないため、
/// 警告ログを出力したうえで sitemap.xml は生成せず robots.txt のみ書き出す（クロールを許可するか
/// 禁止するかはクローラ任せ）。
/// 注意：<c>robots.txt</c> は紳士協定であり、悪意のあるリクエストを止める手段ではない。
/// 正体を偽ったり探りを入れたりするリクエストは、配信の側（CDN）で断る。
/// </summary>
public sealed class SeoGenerator
{
    private readonly BuildContext _ctx;
    private readonly BuildConfig _config;
    private readonly PageRenderer _pageRenderer;
    private readonly OutputWriter _output;

    public SeoGenerator(BuildContext ctx, BuildConfig config, PageRenderer pageRenderer, OutputWriter output)
    {
        _ctx = ctx;
        _config = config;
        _pageRenderer = pageRenderer;
        _output = output;
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
        WriteIndexNowKeyFile();

        _ctx.Logger.Success($"SEO: sitemap.xml ({_pageRenderer.WrittenPages.Count} URL) + robots.txt");
        return Task.CompletedTask;
    }

    /// <summary><c>/sitemap.xml</c> を書き出す。XmlWriter 経由で書き出すことで、URL に &amp; や記号が含まれていても 正しく XML エスケープされる（手書き連結だとエスケープ漏れが起きやすいため）。</summary>
    private void WriteSitemapXml()
    {
        // lastmod は「ページ内容が実際に変わったビルドの時刻」を採用する。全ページ書き出し後の本フェーズで
        // 各出力ファイルの内容ハッシュを永続マニフェストと突き合わせ、変化したページだけ当該ビルド時刻へ進める
        // （不変ページは前回の lastmod を据え置く）。これで「実際は数ファイルしか変わっていないのに全 URL の
        // lastmod が毎ビルド更新される」状態を避け、Google への再クロール優先度シグナルを信頼できるものにする。
        string buildTimeIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var pages = _pageRenderer.WrittenPages
            .Select(p => (p.UrlPath, PathUtil.ToOutputFilePath(_config.OutputDirectory, p.UrlPath)))
            .ToList();
        var manifest = new SitemapLastmodManifest(
            SitemapLastmodManifest.DeriveManifestPath(_config.OutputDirectory));
        // 印は各ページを書く時に取ってあるので（PageRenderer.StableHashOf）、出力のファイルを読み返さない（取っていないページだけ読む）。
        // 差分だけを出すビルドでは、控えを進めない（次の本当のビルドで、変わったページの更新日が進むように）。
        // 「中身が変わったか」は、ほかの回のデータで毎週変わる部分（<!--volatile--> の囲み）と、CSS・JS・画像の指し先のような
        // 見た目と速さの直しで変わる部分を伏せて比べる（StableContentHash）。
        (string Current, string Legacy) HashesOf(string urlPath, string file)
            => _pageRenderer.StableHashOf(urlPath)
               ?? (StableContentHash.FromFile(file),
                   StableContentHash.LegacyFromFile(file, StableContentHash.MayHaveMarkers(urlPath), _output.Md5Of));
        var lastmod = manifest.Resolve(pages, buildTimeIso, HashesOf, StableContentHash.Version, save: !_config.DiffOnly);
        _ctx.Logger.Info(
            $"sitemap lastmod: 据え置き {lastmod.Unchanged} / 更新 {lastmod.Changed} / 新規 {lastmod.Added}"
            + (lastmod.Rebaselined
                ? $"（判定のしかたが版 {lastmod.PreviousVersion} → {StableContentHash.Version} に変わったので、前の版のしかたで比べ、控えの印を取り直した）"
                : ""));

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            OmitXmlDeclaration = false
        };

        var outputFile = Path.Combine(_config.OutputDirectory, "sitemap.xml");

        // いったんメモリに組み立ててから書く（中身が前回と同じなら書かない）。
        using var buffer = new MemoryStream();
        using var writer = XmlWriter.Create(buffer, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

        foreach (var page in _pageRenderer.WrittenPages)
        {
            writer.WriteStartElement("url");
            writer.WriteElementString("loc", _config.BaseUrl + page.UrlPath);
            // lastmod は ISO-8601（W3C Datetime）形式。秒単位までで十分。マニフェスト解決値を使う
            // （万一マップに無ければ当該ビルド時刻でフォールバック）。
            writer.WriteElementString("lastmod",
                lastmod.LastmodByUrl.TryGetValue(page.UrlPath, out var lm) ? lm : buildTimeIso);
            writer.WriteElementString("changefreq", DeriveChangeFreq(page.Section));
            writer.WriteElementString("priority", DerivePriority(page.UrlPath, page.Section).ToString("F1"));
            writer.WriteEndElement(); // </url>
        }

        writer.WriteEndElement(); // </urlset>
        writer.WriteEndDocument();
        writer.Flush();
        _output.WriteBytes(outputFile, buffer.ToArray());
    }

    /// <summary>セクション種別から changefreq を導出。サイト構成に合わせた控えめな更新頻度を返す。</summary>
    private static string DeriveChangeFreq(string section) => section switch
    {
        "home" => "weekly",
        "series" => "weekly",      // 新エピソード追加で更新される
        "episodes" => "monthly",
        "stats" => "weekly",        // ランキングは新エピソードで動く
        _ => "monthly"
    };

    /// <summary>URL パスとセクション種別から priority を導出。 ホーム > シリーズ詳細・エピソード詳細 > 各種詳細ページ > 索引・統計、の順に優先度を付ける。</summary>
    private static double DerivePriority(string urlPath, string section)
    {
        if (urlPath == "/") return 1.0;

        // シリーズ詳細・エピソード詳細はサイトの中核なので高め。
        if (section == "series") return 0.8;
        if (section == "episodes") return 0.8;

        // 詳細ページ（プリキュア・キャラ・人物・企業・商品・楽曲）は中程度。
        if (urlPath.StartsWith("/precures/") && urlPath.Length > "/precures/".Length) return 0.7;
        if (urlPath.StartsWith("/characters/") && urlPath.Length > "/characters/".Length) return 0.7;
        if (urlPath.StartsWith("/people/") && urlPath.Length > "/people/".Length) return 0.6;
        if (urlPath.StartsWith("/companies/") && urlPath.Length > "/companies/".Length) return 0.6;
        if (urlPath.StartsWith("/products/") && urlPath.Length > "/products/".Length) return 0.6;
        if (urlPath.StartsWith("/songs/") && urlPath.Length > "/songs/".Length) return 0.6;

        // 索引ページ・統計ページは低め。
        return 0.5;
    }

    /// <summary>
    /// IndexNow の鍵のファイル（<c>/{鍵}.txt</c>。中身は鍵そのもの）を書き出す。IndexNow の受け手は、送られた鍵がこの場所に
    /// 置かれているかでサイトの持ち主を確かめる。鍵が未設定なら書かない。
    /// </summary>
    private void WriteIndexNowKeyFile()
    {
        if (string.IsNullOrEmpty(_config.IndexNowKey)) return;
        _output.WriteText(Path.Combine(_config.OutputDirectory, _config.IndexNowKey + ".txt"), _config.IndexNowKey);
    }

    /// <summary>/robots.txt を書き出す。すべてのクローラに全体を許可し、sitemap.xml の場所を添える。</summary>
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

        _output.WriteText(Path.Combine(_config.OutputDirectory, "robots.txt"), sb.ToString());
    }
}
