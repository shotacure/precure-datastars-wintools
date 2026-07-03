using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>統計セクション配下ページの <see cref="LayoutModel"/> 構築ヘルパ。 SubtitleStatsGenerator / EpisodePartStatsGenerator が同一骨格で個別に保持していた 「ホーム → 統計 → セクション → 当該ページ」パンくず付きレイアウト構築を単一定義へ集約したもの。</summary>
public static class StatsPageLayout
{
    /// <summary>統計ページ共通の <see cref="LayoutModel"/>（タイトル・メタ説明・4 段パンくず）を組み立てる。</summary>
    /// <param name="pageTitle">ページタイトル（メタ説明の先頭にも使う）。</param>
    /// <param name="metaDescriptionSuffix">メタ説明のうち <paramref name="pageTitle"/> に続ける固定文（統計セクションごとの説明文）。</param>
    /// <param name="sectionLabel">3 段目パンくず（統計セクション索引）のラベル。例:「歴代サブタイトル統計」。</param>
    /// <param name="sectionUrl">3 段目パンくずの URL。例: <c>/stats/subtitles/</c>。</param>
    /// <param name="breadcrumbLabel">末尾パンくず（当該ページ、リンクなし）のラベル。</param>
    public static LayoutModel Make(
        string pageTitle,
        string metaDescriptionSuffix,
        string sectionLabel,
        string sectionUrl,
        string breadcrumbLabel)
    {
        return new LayoutModel
        {
            PageTitle = pageTitle,
            MetaDescription = pageTitle + metaDescriptionSuffix,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = sectionLabel, Url = sectionUrl },
                new BreadcrumbItem { Label = breadcrumbLabel, Url = "" }
            }
        };
    }
}
