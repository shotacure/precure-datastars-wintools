using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
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
    /// フッタの著作権表記用「年」文字列のキャッシュ。
    /// ビルド起動時に <see cref="BuildConfig.PublishedYear"/> と現在年から 1 度だけ組み立てて、
    /// 全ページの <see cref="LayoutModel.CopyrightYears"/> に同じ値を流し込む。
    /// </summary>
    private readonly string _copyrightYears;

    /// <summary>
    /// JSON-LD 出力用の共通シリアライザオプション。
    /// 日本語をそのまま出して容量を抑えつつ、HTML 埋め込み時の <c>&lt;</c> 等を
    /// エスケープ漏れさせないために <see cref="JavaScriptEncoder"/> を使う。
    /// </summary>
    private static readonly JsonSerializerOptions JsonLdSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// SNS シェアボタンに既定で乗せるハッシュタグ列。
    /// X / Twitter のシェア URL クエリ <c>hashtags=</c> はカンマ区切りで複数指定できる仕様のため、
    /// その形式に合わせて保持する。
    /// </summary>
    private const string DefaultShareHashtags = "プリキュア,プリキュアデータベース";

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
        // og:image が個別ページで指定されていなければ、サイト共通の既定 OGP 画像で補う
        //。既定画像が設定されていれば Twitter カードが summary_large_image となる。
        if (string.IsNullOrEmpty(layoutMeta.OgImage) && !string.IsNullOrEmpty(_config.DefaultOgImage))
            layoutMeta.OgImage = _config.DefaultOgImage;
        // GA4 / Search Console は config から自動補完（layoutMeta 側では指定不要）。
        if (string.IsNullOrEmpty(layoutMeta.Ga4MeasurementId))
            layoutMeta.Ga4MeasurementId = _config.Ga4MeasurementId;
        if (string.IsNullOrEmpty(layoutMeta.GoogleSiteVerification))
            layoutMeta.GoogleSiteVerification = _config.GoogleSiteVerification;
        // AdSense クライアント ID も同様に config から自動補完。
        if (string.IsNullOrEmpty(layoutMeta.GoogleAdSenseClientId))
            layoutMeta.GoogleAdSenseClientId = _config.GoogleAdSenseClientId;
        // 著作権表記年。コンストラクタで算出済みの値を毎ページ同じ内容で詰める。
        // Generator から個別指定する想定は無いが、もし明示設定があればそちらを優先する。
        if (string.IsNullOrEmpty(layoutMeta.CopyrightYears))
            layoutMeta.CopyrightYears = _copyrightYears;

        // SNS シェア機能。BaseUrl が空ならシェア対象 URL が組み立てられないため、
        // ShareUrl は空文字のままにして _layout.sbn 側で _share-buttons.sbn 表示を抑制する運用。
        if (string.IsNullOrEmpty(layoutMeta.ShareUrl) && !string.IsNullOrEmpty(layoutMeta.BaseUrl))
            layoutMeta.ShareUrl = layoutMeta.BaseUrl + layoutMeta.CanonicalPath;
        if (string.IsNullOrEmpty(layoutMeta.ShareText))
            layoutMeta.ShareText = BuildShareText(layoutMeta.PageTitle, layoutMeta.SiteName);
        if (string.IsNullOrEmpty(layoutMeta.ShareHashtags))
            layoutMeta.ShareHashtags = DefaultShareHashtags;

        // パンくず由来の BreadcrumbList 構造化データを自動生成。
        // 既存の Breadcrumbs（表示用配列）から 1 度だけ JSON-LD 文字列を組み立て、
        // _layout.sbn が <script type="application/ld+json"> として 2 本目を出力する。
        // BaseUrl が空 or パンくず未設定なら出力をスキップ。
        if (string.IsNullOrEmpty(layoutMeta.BreadcrumbJsonLd))
            layoutMeta.BreadcrumbJsonLd = BuildBreadcrumbJsonLd(layoutMeta.Breadcrumbs, layoutMeta.BaseUrl);

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
    /// 出力ファイル名を直接指定してページを書き出す。
    /// 末尾スラッシュ URL 規約から外れる例外ページ（<c>/404.html</c> 等）のために設けた特別ルート。
    /// 通常ページと違い、本メソッドで書き出したページは <see cref="WrittenPages"/> に記録されず、
    /// したがって <c>sitemap.xml</c> にも掲載されない（404 は SERP インデックス対象外であるべきため）。
    /// レイアウトメタの自動補完（SiteName / BaseUrl / OgType / シェアテキスト等）は通常ページと同等に行う。
    /// </summary>
    /// <param name="outputFileName">
    /// 出力ファイル名（拡張子含む、スラッシュ無し）。例: <c>"404.html"</c>。
    /// 出力ルート直下に書き出す。サブディレクトリへの配置は本オーバーロードでは未サポート。
    /// </param>
    /// <param name="canonicalPath">
    /// 当該ページの URL パス（<c>"/404.html"</c> など）。canonical / og:url の生成と
    /// シェアボタン用 URL の組み立てに使う。書き出し先パスとは独立。
    /// </param>
    /// <param name="section">サマリ用セクションラベル。</param>
    /// <param name="contentTemplate">コンテンツテンプレ名。</param>
    /// <param name="contentModel">コンテンツテンプレに渡すモデル。</param>
    /// <param name="layoutMeta">レイアウト共通メタ。</param>
    public void RenderAndWriteToOutputFile(
        string outputFileName,
        string canonicalPath,
        string section,
        string contentTemplate,
        object contentModel,
        LayoutModel layoutMeta)
    {
        var contentHtml = _renderer.Render(contentTemplate, contentModel);

        // 通常 RenderAndWrite と同じレイアウトメタ補完処理。本メソッドは特例ページ専用で
        // 呼び出し頻度も極端に低い（ビルド 1 回につき 404 の 1 回のみ等）ため、
        // 共通ヘルパに括り出さず補完処理をインラインで並べて置く。
        if (string.IsNullOrEmpty(layoutMeta.SiteName))
            layoutMeta.SiteName = _config.SiteName;
        if (string.IsNullOrEmpty(layoutMeta.BaseUrl))
            layoutMeta.BaseUrl = _config.BaseUrl;
        if (string.IsNullOrEmpty(layoutMeta.CanonicalPath))
            layoutMeta.CanonicalPath = canonicalPath;
        if (string.IsNullOrEmpty(layoutMeta.OgType))
            layoutMeta.OgType = "website";
        if (string.IsNullOrEmpty(layoutMeta.OgImage) && !string.IsNullOrEmpty(_config.DefaultOgImage))
            layoutMeta.OgImage = _config.DefaultOgImage;
        if (string.IsNullOrEmpty(layoutMeta.Ga4MeasurementId))
            layoutMeta.Ga4MeasurementId = _config.Ga4MeasurementId;
        if (string.IsNullOrEmpty(layoutMeta.GoogleSiteVerification))
            layoutMeta.GoogleSiteVerification = _config.GoogleSiteVerification;
        if (string.IsNullOrEmpty(layoutMeta.GoogleAdSenseClientId))
            layoutMeta.GoogleAdSenseClientId = _config.GoogleAdSenseClientId;
        if (string.IsNullOrEmpty(layoutMeta.CopyrightYears))
            layoutMeta.CopyrightYears = _copyrightYears;
        // 404 ページは「シェアして広めたい性格のページ」ではないが、_layout.sbn が _share-buttons.sbn を
        // 無条件 include するため、ShareUrl を空文字のままにしてパーシャル側で出力ごとスキップさせる
        // 仕様にあわせる（ShareText / ShareHashtags も既定で空文字のままにしておく）。
        // ※ シェアボタンを出したい特例ページがあれば、呼び出し側で ShareUrl 等を明示すること。
        // BreadcrumbList JSON-LD は通常通り組み立てる（パンくず未指定なら出力されない）。
        if (string.IsNullOrEmpty(layoutMeta.BreadcrumbJsonLd))
            layoutMeta.BreadcrumbJsonLd = BuildBreadcrumbJsonLd(layoutMeta.Breadcrumbs, layoutMeta.BaseUrl);

        layoutMeta.Content = contentHtml;

        var pageHtml = _renderer.Render("_layout.sbn", layoutMeta);

        var outputFile = Path.Combine(_config.OutputDirectory, outputFileName);
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, pageHtml);

        _summary.IncrementPage(section);
        // 注意：sitemap.xml に載せないため _writtenPages への登録は行わない。
    }

    /// <summary>
    /// フッタ著作権表記用の「年」文字列を組み立てる。
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

    /// <summary>
    /// SNS シェア用の本文テキストを組み立てる。
    /// 「ページタイトル | サイト名」を 1 行目に置き、サイト URL は別途
    /// シェア URL クエリ <c>url=</c> として渡るため本文には含めない（重複を避ける）。
    /// PageTitle が空のときはサイト名のみを返す。
    /// </summary>
    private static string BuildShareText(string pageTitle, string siteName)
    {
        if (string.IsNullOrEmpty(pageTitle))
            return siteName;
        return $"{pageTitle} | {siteName}";
    }

    /// <summary>
    /// パンくず配列から Schema.org の <c>BreadcrumbList</c> 構造化データを JSON 文字列で組み立てる。
    /// <list type="bullet">
    ///   <item>パンくずが 0 件のときは空文字を返す（出力しない）。</item>
    ///   <item><see cref="BreadcrumbItem.Url"/> が空（自分自身）の項目は、絶対 URL に
    ///     <paramref name="baseUrl"/> + <see cref="LayoutModel.CanonicalPath"/> を埋める形ではなく、
    ///     <c>item</c> を省略する（パンくず最終項目は URL 不要）。それ以外は <paramref name="baseUrl"/> と
    ///     結合して絶対 URL 化する。<paramref name="baseUrl"/> が空のときは相対パスのまま渡す。</item>
    /// </list>
    /// </summary>
    private static string BuildBreadcrumbJsonLd(IReadOnlyList<BreadcrumbItem> crumbs, string baseUrl)
    {
        if (crumbs is null || crumbs.Count == 0) return "";

        var list = new List<BreadcrumbListItemJson>(crumbs.Count);
        for (int i = 0; i < crumbs.Count; i++)
        {
            var c = crumbs[i];
            string? itemUrl = null;
            if (!string.IsNullOrEmpty(c.Url))
            {
                itemUrl = string.IsNullOrEmpty(baseUrl) ? c.Url : baseUrl + c.Url;
            }
            list.Add(new BreadcrumbListItemJson
            {
                Type = "ListItem",
                Position = i + 1,
                Name = c.Label,
                Item = itemUrl
            });
        }

        var payload = new BreadcrumbListJson
        {
            Context = "https://schema.org",
            Type = "BreadcrumbList",
            ItemListElement = list
        };
        return JsonSerializer.Serialize(payload, JsonLdSerializerOptions);
    }

    /// <summary>BreadcrumbList JSON-LD のルート要素。</summary>
    private sealed class BreadcrumbListJson
    {
        [JsonPropertyName("@context")] public string Context { get; set; } = "";
        [JsonPropertyName("@type")] public string Type { get; set; } = "";
        [JsonPropertyName("itemListElement")] public List<BreadcrumbListItemJson> ItemListElement { get; set; } = new();
    }

    /// <summary>BreadcrumbList の 1 項目。<see cref="Item"/> は null のときシリアライズ対象から外れる。</summary>
    private sealed class BreadcrumbListItemJson
    {
        [JsonPropertyName("@type")] public string Type { get; set; } = "";
        [JsonPropertyName("position")] public int Position { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("item")] public string? Item { get; set; }
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