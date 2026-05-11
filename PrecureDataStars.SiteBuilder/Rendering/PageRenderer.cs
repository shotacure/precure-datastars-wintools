using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 2 段階レンダリング（コンテンツテンプレ → レイアウト）の共通フロー。
/// <para>
/// 各 Generator は (templateName, contentModel, layoutMeta) を渡すだけで、
/// ファイル書き出しまで一貫して扱える。
/// </para>
/// </summary>
public sealed class PageRenderer
{
    private readonly ScribanRenderer _renderer;
    private readonly BuildConfig _config;
    private readonly BuildSummary _summary;

    /// <summary>
    /// 本ビルドで出力した URL パス一式（先頭スラッシュ付き、末尾スラッシュ付き）。
    /// SeoGenerator が sitemap.xml を構築する際に参照する。書き込み順を保つため List で保持し、
    /// 重複防止のため <see cref="HashSet{T}"/> で同時管理する（同一 URL を 2 回 RenderAndWrite した場合は
    /// 後者で上書き = 重複を排除する）。
    /// </summary>
    private readonly List<WrittenPage> _writtenPages = new();
    private readonly HashSet<string> _writtenPathSet = new(StringComparer.Ordinal);

    /// <summary>
    /// フッタの著作権表記用「年」文字列のキャッシュ（v1.3.0 続編 追加）。
    /// ビルド起動時に <see cref="BuildConfig.PublishedYear"/> と現在年から 1 度だけ組み立てて、
    /// 全ページの <see cref="LayoutModel.CopyrightYears"/> に同じ値を流し込む。
    /// </summary>
    private readonly string _copyrightYears;

    public PageRenderer(ScribanRenderer renderer, BuildConfig config, BuildSummary summary)
    {
        _renderer = renderer;
        _config = config;
        _summary = summary;
        _copyrightYears = BuildCopyrightYearsString(config.PublishedYear, DateTime.Now.Year);
    }

    /// <summary>
    /// 本ビルドで出力した HTML ページ一覧（書き込み順）。SeoGenerator が sitemap.xml を構築する際に参照。
    /// </summary>
    public IReadOnlyList<WrittenPage> WrittenPages => _writtenPages;

    /// <summary>
    /// コンテンツテンプレを <paramref name="contentModel"/> で 1 度レンダリング → レイアウトに包んでファイル保存する。
    /// </summary>
    /// <param name="urlPath">サイト内 URL パス（先頭スラッシュ付き、末尾スラッシュ付き）。</param>
    /// <param name="section">サマリ用セクションラベル（"home" / "series" / "episodes" 等）。</param>
    /// <param name="contentTemplate">コンテンツテンプレ名（例 "home.sbn"）。</param>
    /// <param name="contentModel">コンテンツテンプレに渡すモデル。</param>
    /// <param name="layoutMeta">パンくず・タイトル等の共通メタ。Content は本メソッドが上書きする。</param>
    public void RenderAndWrite(
        string urlPath,
        string section,
        string contentTemplate,
        object contentModel,
        LayoutModel layoutMeta)
    {
        // コンテンツ部分を先にレンダリング → そのまま HTML 文字列として layout の Content に詰める。
        var contentHtml = _renderer.Render(contentTemplate, contentModel);

        // 共通項を上書きし、Content には先ほどの HTML を入れる。
        // SiteName / BaseUrl / CanonicalPath は呼び出し側未指定でも config から補える。
        if (string.IsNullOrEmpty(layoutMeta.SiteName))
            layoutMeta.SiteName = _config.SiteName;
        if (string.IsNullOrEmpty(layoutMeta.BaseUrl))
            layoutMeta.BaseUrl = _config.BaseUrl;
        if (string.IsNullOrEmpty(layoutMeta.CanonicalPath))
            layoutMeta.CanonicalPath = urlPath;
        // OGP の og:type が呼び出し側未指定なら "website" を既定に。
        if (string.IsNullOrEmpty(layoutMeta.OgType))
            layoutMeta.OgType = "website";
        // GA4 / Search Console は config から自動補完（layoutMeta 側では指定不要）。
        if (string.IsNullOrEmpty(layoutMeta.Ga4MeasurementId))
            layoutMeta.Ga4MeasurementId = _config.Ga4MeasurementId;
        if (string.IsNullOrEmpty(layoutMeta.GoogleSiteVerification))
            layoutMeta.GoogleSiteVerification = _config.GoogleSiteVerification;
        // AdSense クライアント ID も同様に config から自動補完。
        if (string.IsNullOrEmpty(layoutMeta.GoogleAdSenseClientId))
            layoutMeta.GoogleAdSenseClientId = _config.GoogleAdSenseClientId;
        // 著作権表記年（v1.3.0 続編 追加）。コンストラクタで算出済みの値を毎ページ同じ内容で詰める。
        // Generator から個別指定する想定は無いが、もし明示設定があればそちらを優先する。
        if (string.IsNullOrEmpty(layoutMeta.CopyrightYears))
            layoutMeta.CopyrightYears = _copyrightYears;
        layoutMeta.Content = contentHtml;

        var pageHtml = _renderer.Render("_layout.sbn", layoutMeta);

        var outputFile = PathUtil.ToOutputFilePath(_config.OutputDirectory, urlPath);
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, pageHtml);

        _summary.IncrementPage(section);

        // sitemap.xml 生成用に URL を記録（重複は無視）。
        if (_writtenPathSet.Add(urlPath))
        {
            _writtenPages.Add(new WrittenPage
            {
                UrlPath = urlPath,
                Section = section,
                LastModified = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// フッタ著作権表記用の「年」文字列を組み立てる（v1.3.0 続編 追加）。
    /// <list type="bullet">
    ///   <item>公開年と現在年が同じ → <c>"2026"</c> のような単年表記。</item>
    ///   <item>現在年が公開年より後 → <c>"2026-2027"</c> のような期間表記。</item>
    ///   <item>現在年が公開年より前（極端なシステム時刻ずれ）→ 公開年のみで安全側に倒す。</item>
    /// </list>
    /// </summary>
    private static string BuildCopyrightYearsString(int publishedYear, int currentYear)
    {
        if (currentYear <= publishedYear)
        {
            return publishedYear.ToString("D4");
        }
        return $"{publishedYear:D4}-{currentYear:D4}";
    }
}

/// <summary>
/// ビルドで出力した 1 ページの記録（sitemap.xml 生成用）。
/// </summary>
public sealed class WrittenPage
{
    /// <summary>サイト内 URL パス（先頭スラッシュ付き、末尾スラッシュ付き）。</summary>
    public string UrlPath { get; set; } = "";

    /// <summary>BuildSummary のセクション名（"home" / "series" / "stats" 等）。
    /// sitemap.xml の priority 設定に使う。</summary>
    public string Section { get; set; } = "";

    /// <summary>このページが書き込まれた時刻（UTC）。sitemap.xml の <c>&lt;lastmod&gt;</c> に使う。</summary>
    public DateTime LastModified { get; set; }
}
