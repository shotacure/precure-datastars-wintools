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

    public PageRenderer(ScribanRenderer renderer, BuildConfig config, BuildSummary summary)
    {
        _renderer = renderer;
        _config = config;
        _summary = summary;
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
