using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 出力ルート直下に <c>/404.html</c> を書き出す特例ジェネレータ。
/// <para>
/// 通常ページとは異なり、末尾スラッシュ URL 規約から外れる <c>.html</c> ファイル直書きとなる。
/// 静的ホスティング（AWS S3 + CloudFront、Cloudflare Pages 等）の側で「404 応答時に
/// 本ファイルを返す」設定（ErrorDocument / Custom Error Response）を別途設定して
/// 機能させる前提とする（本ツールはファイルを置くだけ）。
/// </para>
/// <para>
/// 404 ページの目的は下記の 3 点：
/// </para>
/// <list type="bullet">
///   <item>「リンク切れで来訪したユーザに、サイトの存在自体は健在であることを示す」</item>
///   <item>「主要セクションへの導線を提供して、目的のページに辿り着く別経路を提供する」</item>
///   <item>「サイトの健全性評価（AdSense / Search Console）で 404 のハンドリングが甘いと見なされないよう、
///     しっかり作り込んだエラーページを用意する」</item>
/// </list>
/// <para>
/// 本ジェネレータが書き出すページは <c>sitemap.xml</c> には登録されない（404 は SERP インデックス
/// 対象外であるべきため、<see cref="PageRenderer.RenderAndWriteToOutputFile"/> 経由で
/// 別ルートに切り分けている）。
/// </para>
/// </summary>
public sealed class NotFoundGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public NotFoundGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating 404 page");

        var content = new NotFoundContentModel
        {
            SiteName = _ctx.Config.SiteName
        };
        var layout = new LayoutModel
        {
            PageTitle = "ページが見つかりませんでした",
            // 404 ページは検索インデックス対象外で、SNS シェア対象でもない。ただし
            // 「リンク切れに気付いたユーザがダイレクトに到達する」ページなので、
            // MetaDescription は事務的に「サイト健在の合図 + 主要セクションへの誘導が
            // 載っている旨」を簡潔に記す。
            MetaDescription = $"{_ctx.Config.SiteName} — お探しのページが見つかりませんでした。サイトトップ、シリーズ一覧、検索からお探しください。"
            // Breadcrumbs はあえて空。404 にパンくずは意味がない（経路が壊れている前提のため）。
            // OgType は PageRenderer 側で既定値 "website" が補完される。
            // JsonLd も付けない（404 を構造化データで主張する意味は無い）。
        };

        _page.RenderAndWriteToOutputFile(
            outputFileName: "404.html",
            canonicalPath: "/404.html",
            section: "other",
            contentTemplate: "not-found.sbn",
            contentModel: content,
            layoutMeta: layout);

        _ctx.Logger.Success("/404.html");
    }

    /// <summary>404 ページのテンプレに渡すコンテンツモデル。サイト名のみで足りる。</summary>
    private sealed class NotFoundContentModel
    {
        public string SiteName { get; set; } = "";
    }
}