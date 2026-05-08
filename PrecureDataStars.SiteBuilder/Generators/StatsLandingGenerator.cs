using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>/stats/</c> ランディングページの生成（v1.3.0 後半追加）。
/// <para>
/// 統計セクション全体の入口。役職別ランキング（<c>/stats/roles/</c>）、声優ランキング（<c>/stats/voice-cast/</c>）、
/// サブタイトル統計（<c>/stats/subtitles/</c>）、エピソード尺統計（<c>/stats/episodes/</c>）の 4 大セクションへの
/// リンクを並べる索引ページ。
/// </para>
/// </summary>
public sealed class StatsLandingGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public StatsLandingGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating stats landing");

        var content = new ContentModel();
        var layout = new LayoutModel
        {
            PageTitle = "統計",
            MetaDescription = "プリキュア全シリーズの役職別関与・声優出演・サブタイトル文字・エピソード尺など、各種集計をまとめた統計セクションです。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/", "stats-landing", "stats-landing.sbn", content, layout);
        _ctx.Logger.Success("/stats/");
    }

    /// <summary>テンプレ用モデル（内容は固定リンク群、現状フィールド無し）。</summary>
    private sealed class ContentModel { }
}
