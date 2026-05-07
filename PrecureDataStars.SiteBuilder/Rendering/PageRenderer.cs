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

    public PageRenderer(ScribanRenderer renderer, BuildConfig config, BuildSummary summary)
    {
        _renderer = renderer;
        _config = config;
        _summary = summary;
    }

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
        layoutMeta.Content = contentHtml;

        var pageHtml = _renderer.Render("_layout.sbn", layoutMeta);

        var outputFile = PathUtil.ToOutputFilePath(_config.OutputDirectory, urlPath);
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, pageHtml);

        _summary.IncrementPage(section);
    }
}
