namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 共通レイアウト <c>_layout.sbn</c> に渡すモデル。
/// <para>
/// 各 Generator は本文を「コンテンツテンプレート」でレンダリングして HTML 文字列を作り、
/// それを <see cref="Content"/> に詰めて <c>_layout.sbn</c> をレンダリングする 2 段構え。
/// Scriban には Razor の <c>@RenderBody()</c> 相当の仕組みがないため、
/// このパターンが最もシンプル。
/// </para>
/// </summary>
public sealed class LayoutModel
{
    /// <summary>サイト名（ヘッダおよび <c>&lt;title&gt;</c> サフィックス）。</summary>
    public string SiteName { get; set; } = "";

    /// <summary>絶対 URL 構築用ベース URL。空文字なら canonical 出力をスキップ。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>当ページの canonical パス（例 "/series/precure/"）。</summary>
    public string CanonicalPath { get; set; } = "";

    /// <summary><c>&lt;title&gt;</c> のプレフィックス。空ならサイト名のみ。</summary>
    public string PageTitle { get; set; } = "";

    /// <summary>meta description。空なら出力しない。</summary>
    public string MetaDescription { get; set; } = "";

    /// <summary>パンくずリスト。空なら出力しない。</summary>
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; set; } = Array.Empty<BreadcrumbItem>();

    /// <summary>本文 HTML（Generator がコンテンツテンプレで先にレンダリングした結果）。</summary>
    public string Content { get; set; } = "";
}

/// <summary>パンくずの 1 項目。</summary>
public sealed class BreadcrumbItem
{
    /// <summary>表示文字列。</summary>
    public string Label { get; set; } = "";

    /// <summary>リンク先 URL。最終項目（自分自身）では空文字にしておく。</summary>
    public string Url { get; set; } = "";
}
