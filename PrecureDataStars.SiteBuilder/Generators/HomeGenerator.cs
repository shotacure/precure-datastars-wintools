using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// サイトトップ <c>/</c> の生成。シリーズ一覧と説明文を載せる。
/// </summary>
public sealed class HomeGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public HomeGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    /// <summary>
    /// トップページを 1 枚生成する。
    /// </summary>
    public void Generate()
    {
        _ctx.Logger.Section("Generating home");

        // ホームには TV シリーズだけを年代順で表示する（映画・スピンオフは「シリーズ一覧」に集約）。
        // 日付は日本語フォーマット「2004年2月1日」で渡す。
        var seriesView = _ctx.Series
            .Where(s => s.KindCode == "TV")
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .Select(s => new SeriesListItem
            {
                Slug = s.Slug,
                Title = s.Title,
                TitleShort = s.TitleShort ?? "",
                Period = FormatPeriodJp(s.StartDate, s.EndDate),
                EpisodesLabel = s.Episodes.HasValue ? $"全 {s.Episodes.Value} 話" : ""
            })
            .ToList();

        var content = new HomeContentModel
        {
            SiteName = _ctx.Config.SiteName,
            Series = seriesView
        };

        var layout = new LayoutModel
        {
            PageTitle = "",  // トップなのでサイト名のみ表示にする
            MetaDescription = "プリキュアシリーズのエピソード・音楽・スタッフ・キャラクターを横断的に閲覧できる非公式データベース。",
            Breadcrumbs = Array.Empty<BreadcrumbItem>()
        };

        _page.RenderAndWrite("/", "home", "home.sbn", content, layout);
        _ctx.Logger.Success("/");
    }

    /// <summary>home.sbn のモデル。</summary>
    private sealed class HomeContentModel
    {
        public string SiteName { get; set; } = "";
        public IReadOnlyList<SeriesListItem> Series { get; set; } = Array.Empty<SeriesListItem>();
    }

    /// <summary>シリーズリスト 1 行分。</summary>
    private sealed class SeriesListItem
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleShort { get; set; } = "";
        public string Period { get; set; } = "";
        public string EpisodesLabel { get; set; } = "";
    }

    /// <summary>放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。</summary>
    private static string FormatPeriodJp(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        return startStr;
    }
}

/// <summary>
/// <c>/about/</c> の生成。
/// </summary>
public sealed class AboutGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public AboutGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating about");

        // about テンプレは現状 SiteName しか参照しないので空モデルで十分。
        var content = new AboutContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "このサイトについて",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "このサイトについて", Url = "" }
            }
        };

        _page.RenderAndWrite("/about/", "about", "about.sbn", content, layout);
        _ctx.Logger.Success("/about/");
    }

    private sealed class AboutContentModel
    {
        public string SiteName { get; set; } = "";
    }
}
