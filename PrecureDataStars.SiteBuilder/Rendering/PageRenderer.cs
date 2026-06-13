using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>2 段階レンダリング（コンテンツテンプレ → レイアウト）の共通フロー。</summary>
public sealed class PageRenderer
{
    private readonly ScribanRenderer _renderer;
    private readonly BuildConfig _config;
    private readonly BuildSummary _summary;

    /// <summary>
    /// 進捗バー連携先。null のときは進捗通知は行わず、従来どおりサマリのみを更新する。
    /// 各 <c>RenderAndWrite</c> 系メソッドでページを 1 件書き出すたびに
    /// <see cref="ProgressReporter.PageWritten"/> を呼び、二段プログレスバー（セクション内 + 全体）の
    /// セクション内カウンタを進める。
    /// </summary>
    private readonly ProgressReporter? _reporter;

    /// <summary>本ビルドで出力した URL パス一式（先頭スラッシュ付き、末尾スラッシュ付き）。 SeoGenerator が sitemap.xml を構築する際に参照する。書き込み順を保つため List で保持し、 重複防止のため <see cref="HashSet{T}"/> で同時管理する（同一 URL を 2 回 RenderAndWrite した場合は 後者で上書き = 重複を排除する）。</summary>
    private readonly List<WrittenPage> _writtenPages = new();
    private readonly HashSet<string> _writtenPathSet = new(StringComparer.Ordinal);

    /// <summary>フッタの著作権表記用「年」文字列のキャッシュ。 ビルド起動時に <see cref="BuildConfig.PublishedYear"/> と現在年から 1 度だけ組み立てて、 全ページの <see cref="LayoutModel.CopyrightYears"/> に同じ値を流し込む。</summary>
    private readonly string _copyrightYears;

    /// <summary>JSON-LD 出力用の共通シリアライザオプション。 日本語をそのまま出して容量を抑えつつ、HTML 埋め込み時の <c>&lt;</c> 等を エスケープ漏れさせないために <see cref="JavaScriptEncoder"/> を使う。</summary>
    private static readonly JsonSerializerOptions JsonLdSerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>SNS シェアボタンに既定で乗せるハッシュタグ列。 X / Twitter のシェア URL クエリ <c>hashtags=</c> はカンマ区切りで複数指定できる仕様のため、 その形式に合わせて保持する。</summary>
    private const string DefaultShareHashtags = "プリキュア,プリキュアデータベース";

    public PageRenderer(ScribanRenderer renderer, BuildConfig config, BuildSummary summary, ProgressReporter? reporter = null)
    {
        _renderer = renderer;
        _config = config;
        _summary = summary;
        _reporter = reporter;
        _copyrightYears = BuildCopyrightYearsString(config.PublishedYear, DateTime.Now.Year);
    }

    /// <summary>本ビルドで出力した HTML ページ一覧（書き込み順）。SeoGenerator が sitemap.xml を構築する際に参照。</summary>
    public IReadOnlyList<WrittenPage> WrittenPages => _writtenPages;

    /// <summary>コンテンツテンプレを <paramref name="contentModel"/> で 1 度レンダリング → レイアウトに包んでファイル保存する。</summary>
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
        var pageHtml = RenderToHtml(urlPath, contentTemplate, contentModel, layoutMeta);
        WriteRendered(urlPath, section, pageHtml);
    }

    /// <summary>
    /// 通常ページ 1 件分の最終 HTML（レイアウト適用済み）を組み立てて文字列で返す。
    /// <see cref="RenderAndWrite"/> の前半（レンダリング）だけを切り出したメソッドで、
    /// ファイル書き出し・サマリ更新・sitemap 記録などの共有状態には一切触れない。
    /// 触れるのは引数の <paramref name="layoutMeta"/>（ページ私有のインスタンス前提）と
    /// スレッドセーフな <see cref="ScribanRenderer"/> のみのため、ページレンダリングの
    /// 並列実行フェーズから複数スレッドで同時に呼び出せる。
    /// 並列化するジェネレータは本メソッドで HTML を並列生成したのち、
    /// <see cref="WriteRendered"/> を元のページ順で逐次呼び出して書き出す
    /// （sitemap.xml の URL 並び＝記録順の決定論を保つため）。
    /// </summary>
    public string RenderToHtml(
        string urlPath,
        string contentTemplate,
        object contentModel,
        LayoutModel layoutMeta)
    {
        // コンテンツ部分を先にレンダリング → そのまま HTML 文字列として layout の Content に詰める。
        var contentHtml = _renderer.Render(contentTemplate, contentModel);

        // 共通項（SiteName / BaseUrl / OGP / GA4 / AdSense / 著作権年 / パンくず JSON-LD）を
        // config から補完する。CanonicalPath が未指定なら本ページの urlPath を充てる。
        ApplyCommonLayoutDefaults(layoutMeta, urlPath);

        // SNS シェア機能。BaseUrl が空ならシェア対象 URL が組み立てられないため、
        // ShareUrl は空文字のままにして _layout.sbn 側で _share-buttons.sbn 表示を抑制する運用。
        // 本処理は通常ページ専用で、特例ページ（404 等）では意図的に空のままにするため
        // 共通ヘルパには含めず、このメソッド側にのみ置く。
        // SuppressShareButtons 指定ページ（運営情報系）はシェア対象外として ShareUrl を組み立てない。
        if (string.IsNullOrEmpty(layoutMeta.ShareUrl) && !string.IsNullOrEmpty(layoutMeta.BaseUrl) && !layoutMeta.SuppressShareButtons)
            layoutMeta.ShareUrl = layoutMeta.BaseUrl + layoutMeta.CanonicalPath;
        if (string.IsNullOrEmpty(layoutMeta.ShareText))
            layoutMeta.ShareText = BuildShareText(layoutMeta.PageTitle, layoutMeta.SiteName);
        if (string.IsNullOrEmpty(layoutMeta.ShareHashtags))
            layoutMeta.ShareHashtags = DefaultShareHashtags;

        // Amazon 由来要素の有無をコンテンツ HTML から自動検出し、フッタ注記の出し分けに使う。
        // アフィリエイト参加表明はアソシエイトタグ付き URL（?tag= / &tag=）を含むページだけが対象
        // （免責事項のように本文で Amazon に言及しただけのページでは発火させない）。
        layoutMeta.HasAmazonAffiliateLinks = AmazonAffiliateLinkRegex.IsMatch(contentHtml);
        layoutMeta.HasAmazonImages = contentHtml.Contains("media-amazon.com", StringComparison.Ordinal);
        // YouTube 公式動画の埋め込み（エピソード詳細の次回予告等）があるページにも同様の権利注記を出す。
        layoutMeta.HasYoutubeEmbeds = contentHtml.Contains("youtube.com/embed/", StringComparison.Ordinal);

        layoutMeta.Content = contentHtml;

        return _renderer.Render("_layout.sbn", layoutMeta);
    }

    /// <summary>アソシエイトタグ付き Amazon リンクの検出用正規表現（href 値内の ?tag= / &amp;tag= を要求）。</summary>
    private static readonly System.Text.RegularExpressions.Regex AmazonAffiliateLinkRegex =
        new(@"amazon\.co\.jp/[^""']*[?&]tag=", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// レンダリング済みの最終 HTML をファイルへ書き出し、サマリ・進捗・sitemap 記録を更新する。
    /// <see cref="RenderAndWrite"/> の後半（書き出し）だけを切り出したメソッド。
    /// <see cref="_writtenPages"/> 等の共有状態を更新するため、並列実行フェーズからは呼ばず、
    /// 必ず元のページ順での逐次実行コンテキストから呼ぶこと（sitemap.xml の並びが
    /// ビルドごとに揺れない決定論を担保する）。
    /// </summary>
    public void WriteRendered(string urlPath, string section, string pageHtml)
    {
        var outputFile = PathUtil.ToOutputFilePath(_config.OutputDirectory, urlPath);
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, pageHtml);

        RecordWritten(urlPath, section);
    }

    /// <summary>
    /// 通常ページ 1 件分をレンダリングしてファイルへ書き出すところまでを行い、サマリ・進捗・
    /// sitemap 記録は行わない。書き出し先パスはページごとに互いに異なるため、本メソッドは
    /// ページレンダリングの並列実行フェーズから複数スレッドで同時に呼び出せる
    /// （ファイル作成はウイルススキャン等で 1 件あたりの待ちが意外と大きく、並列化の効果が出る）。
    /// 呼び出し側は全ページ完了後に <see cref="RecordWritten"/> を元のページ順で逐次呼び出して
    /// 記録を確定させること。
    /// </summary>
    public void RenderAndWriteFile(
        string urlPath,
        string contentTemplate,
        object contentModel,
        LayoutModel layoutMeta)
    {
        var pageHtml = RenderToHtml(urlPath, contentTemplate, contentModel, layoutMeta);
        var outputFile = PathUtil.ToOutputFilePath(_config.OutputDirectory, urlPath);
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, pageHtml);
    }

    /// <summary>
    /// 書き出し済みページ 1 件分のサマリ・進捗・sitemap 記録を更新する。
    /// <see cref="_writtenPages"/> 等の共有状態を更新するため、並列実行フェーズからは呼ばず、
    /// 必ず元のページ順での逐次実行コンテキストから呼ぶこと。
    /// </summary>
    public void RecordWritten(string urlPath, string section)
    {
        _summary.IncrementPage(section);
        _reporter?.PageWritten();

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

        // 通常 RenderAndWrite と同じレイアウトメタ補完処理を共通ヘルパで実施する。
        // CanonicalPath は書き出し先ファイル名とは独立に、引数の canonicalPath を充てる。
        ApplyCommonLayoutDefaults(layoutMeta, canonicalPath);
        // 404 ページは「シェアして広めたい性格のページ」ではないが、_layout.sbn が _share-buttons.sbn を
        // 無条件 include するため、ShareUrl を空文字のままにしてパーシャル側で出力ごとスキップさせる
        // 仕様にあわせる（ShareText / ShareHashtags も既定で空文字のままにしておく）。
        // ※ シェアボタンを出したい特例ページがあれば、呼び出し側で ShareUrl 等を明示すること。
        // BreadcrumbList JSON-LD は共通ヘルパ内で通常通り組み立てる（パンくず未指定なら出力されない）。

        layoutMeta.Content = contentHtml;

        var pageHtml = _renderer.Render("_layout.sbn", layoutMeta);

        var outputFile = Path.Combine(_config.OutputDirectory, outputFileName);
        PathUtil.EnsureParentDirectory(outputFile);
        File.WriteAllText(outputFile, pageHtml);

        _summary.IncrementPage(section);
        _reporter?.PageWritten();
        // 注意：sitemap.xml に載せないため _writtenPages への登録は行わない。
    }

    /// <summary>
    /// レイアウト共通メタの自動補完。<see cref="RenderAndWrite"/> と
    /// <see cref="RenderAndWriteToOutputFile"/> の双方から呼ぶ共通処理。
    /// 補完対象は SiteName / BaseUrl / CanonicalPath / OgType / OgImage /
    /// Ga4MeasurementId / GoogleSiteVerification / GoogleAdSenseClientId /
    /// CopyrightYears と、パンくず由来の BreadcrumbList JSON-LD。いずれも
    /// 呼び出し側で未指定（空）のプロパティだけを config 値や算出値で埋める
    /// （明示設定があればそれを優先）。
    /// SNS シェア系（ShareUrl / ShareText / ShareHashtags）は呼び出し側ごとに
    /// 扱いが異なる（特例ページではあえて空のままにする）ため、本メソッドには
    /// 含めず各呼び出し側に委ねる。BreadcrumbList JSON-LD の入力（Breadcrumbs /
    /// BaseUrl）はシェア系処理の影響を受けないため、シェア系より先に組み立てても
    /// 出力は同一となる。
    /// </summary>
    /// <param name="layoutMeta">補完対象のレイアウトメタ。空のプロパティのみ埋める。</param>
    /// <param name="canonicalPath">
    /// canonical / og:url に用いる当該ページの URL パス。
    /// 書き出し先ファイルパスとは独立（特例ページでは別指定になり得る）。
    /// </param>
    private void ApplyCommonLayoutDefaults(LayoutModel layoutMeta, string canonicalPath)
    {
        // SiteName / BaseUrl / CanonicalPath は呼び出し側未指定でも config から補える。
        if (string.IsNullOrEmpty(layoutMeta.SiteName))
            layoutMeta.SiteName = _config.SiteName;
        if (string.IsNullOrEmpty(layoutMeta.BaseUrl))
            layoutMeta.BaseUrl = _config.BaseUrl;
        if (string.IsNullOrEmpty(layoutMeta.CanonicalPath))
            layoutMeta.CanonicalPath = canonicalPath;
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

        // パンくず由来の BreadcrumbList 構造化データを自動生成。
        if (string.IsNullOrEmpty(layoutMeta.BreadcrumbJsonLd))
            layoutMeta.BreadcrumbJsonLd = BuildBreadcrumbJsonLd(layoutMeta.Breadcrumbs, layoutMeta.BaseUrl);

        // グローバルナビの項目列。現在ページの URL パスから「いまどのセクションに居るか」を解決して
        // IsActive を立てる（ヘッダナビ・モバイルオーバーレイの双方で現在地ハイライトに使う）。
        if (layoutMeta.NavItems.Count == 0)
            layoutMeta.NavItems = BuildNavItems(canonicalPath);
    }

    /// <summary>グローバルナビの定義（ラベルと URL の並び）。ヘッダ・モバイルオーバーレイ共通。</summary>
    private static readonly (string Label, string Url)[] GlobalNavDefinition =
    {
        ("シリーズ", "/series/"),
        ("プリキュア", "/precures/"),
        ("キャラクター", "/characters/"),
        ("音楽", "/music/"),
        ("クリエーター", "/creators/"),
        ("統計", "/stats/"),
        ("読み物", "/articles/"),
        ("このサイトについて", "/about/"),
    };

    /// <summary>
    /// 現在ページの URL パスからグローバルナビ項目列を組み立て、所属セクションの項目に IsActive を立てる。
    /// 所属の解決はナビに出ていない URL も配下として扱う：楽曲・商品・劇伴は「音楽」、
    /// 人物・企業は「クリエーター」、エピソード詳細はシリーズ配下なので「シリーズ」。
    /// どのセクションにも属さないページ（ホーム・運営情報など）は全項目非アクティブ。
    /// </summary>
    private static IReadOnlyList<NavItem> BuildNavItems(string urlPath)
    {
        string activeUrl = "";
        if (urlPath.StartsWith("/series/", StringComparison.Ordinal)) activeUrl = "/series/";
        else if (urlPath.StartsWith("/precures/", StringComparison.Ordinal)) activeUrl = "/precures/";
        else if (urlPath.StartsWith("/characters/", StringComparison.Ordinal)) activeUrl = "/characters/";
        else if (urlPath.StartsWith("/music/", StringComparison.Ordinal)
              || urlPath.StartsWith("/songs/", StringComparison.Ordinal)
              || urlPath.StartsWith("/products/", StringComparison.Ordinal)
              || urlPath.StartsWith("/bgms/", StringComparison.Ordinal)) activeUrl = "/music/";
        else if (urlPath.StartsWith("/creators/", StringComparison.Ordinal)
              || urlPath.StartsWith("/persons/", StringComparison.Ordinal)
              || urlPath.StartsWith("/companies/", StringComparison.Ordinal)) activeUrl = "/creators/";
        else if (urlPath.StartsWith("/stats/", StringComparison.Ordinal)) activeUrl = "/stats/";
        else if (urlPath.StartsWith("/articles/", StringComparison.Ordinal)) activeUrl = "/articles/";
        else if (urlPath.StartsWith("/about/", StringComparison.Ordinal)) activeUrl = "/about/";

        var items = new List<NavItem>(GlobalNavDefinition.Length);
        foreach (var (label, url) in GlobalNavDefinition)
        {
            items.Add(new NavItem { Label = label, Url = url, IsActive = url == activeUrl });
        }
        return items;
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

    /// <summary>SNS シェア用の本文テキストを組み立てる。 「ページタイトル | サイト名」を 1 行目に置き、サイト URL は別途 シェア URL クエリ <c>url=</c> として渡るため本文には含めない（重複を避ける）。 PageTitle が空のときはサイト名のみを返す。</summary>
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

/// <summary>ビルドで出力した 1 ページの記録（sitemap.xml 生成用）。</summary>
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
