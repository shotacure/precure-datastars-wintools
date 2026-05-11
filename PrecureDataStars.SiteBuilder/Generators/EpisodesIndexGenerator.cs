using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>/episodes/</c> エピソード一覧ランディングページのジェネレータ（v1.3.0 公開直前のデザイン整理で新設）。
/// <para>
/// 全 TV シリーズのエピソードをシリーズ別のセクション区切りで一覧表示する単一ページ。
/// シリーズが多数あり総エピソード数も 1000 を超えるため、各シリーズセクションは
/// <c>&lt;details&gt;</c> で折り畳み可能とし、初期状態ではいずれも閉じる方針（描画コスト・スクロール量の抑制）。
/// </para>
/// <para>
/// 各セクション内の行は「話数 + サブタイトル + 放送日」のシンプルな縦リスト（シリーズ詳細のスタッフ群は含めない）。
/// このランディングページからは話数の概観と各話詳細への動線提供を主目的とし、
/// 詳細なスタッフ情報を確認したい場合はシリーズ詳細・エピソード詳細へ流す導線とする。
/// </para>
/// <para>
/// ホームのデータベース統計セクションで「エピソード」ボックスをクリックしたときの遷移先がこのページ。
/// </para>
/// </summary>
public sealed class EpisodesIndexGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public EpisodesIndexGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating episodes index");

        // TV シリーズに限定して、放送開始順に並べる。
        // 映画系・スピンオフのエピソードは episodes テーブルに入れない運用なので、ここでは TV のみ取り扱う。
        var tvSeries = _ctx.Series
            .Where(s => string.Equals(s.KindCode, "TV", StringComparison.Ordinal))
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .ToList();

        int totalEpisodes = 0;
        var sections = new List<EpisodesIndexSection>();
        foreach (var s in tvSeries)
        {
            if (!_ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps) || eps.Count == 0)
                continue;

            var rows = eps
                .OrderBy(e => e.SeriesEpNo)
                .Select(e => new EpisodesIndexRow
                {
                    SeriesEpNo = e.SeriesEpNo,
                    TitleText = e.TitleText,
                    TitleRichHtml = e.TitleRichHtml ?? "",
                    OnAirDate = FormatJpDate(e.OnAirAt),
                    EpisodeUrl = PathUtil.EpisodeUrl(s.Slug, e.SeriesEpNo)
                })
                .ToList();

            totalEpisodes += rows.Count;

            sections.Add(new EpisodesIndexSection
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                // v1.3.0 stage22 後段：シリーズ summary 行に薄色括弧で添える西暦 4 桁。
                SeriesStartYearLabel = s.StartDate.Year.ToString(),
                Period = FormatPeriod(s.StartDate, s.EndDate),
                TotalEpisodesLabel = s.Episodes.HasValue ? $"全 {s.Episodes.Value} 話" : $"{rows.Count} 話",
                Episodes = rows
            });
        }

        var content = new EpisodesIndexModel
        {
            Sections = sections,
            TotalSeriesCount = sections.Count,
            TotalEpisodeCount = totalEpisodes,
            CoverageLabel = _ctx.CreditCoverageLabel
        };

        var layout = new LayoutModel
        {
            PageTitle = "エピソード一覧",
            MetaDescription = "プリキュアシリーズの全エピソードをシリーズ別に一覧できるランディングページ。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "エピソード一覧", Url = "" }
            }
        };

        _page.RenderAndWrite("/episodes/", "episodes", "episodes-index.sbn", content, layout);
        _ctx.Logger.Success($"episodes index: {sections.Count} シリーズ / {totalEpisodes} エピソード");
    }

    /// <summary>放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。</summary>
    private static string FormatPeriod(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        return startStr;
    }

    /// <summary>放送日を「2004年2月1日（日）」で返す。</summary>
    private static string FormatJpDate(DateTime dt)
    {
        string dayOfWeek = dt.DayOfWeek switch
        {
            DayOfWeek.Sunday    => "日",
            DayOfWeek.Monday    => "月",
            DayOfWeek.Tuesday   => "火",
            DayOfWeek.Wednesday => "水",
            DayOfWeek.Thursday  => "木",
            DayOfWeek.Friday    => "金",
            DayOfWeek.Saturday  => "土",
            _ => "?"
        };
        return $"{dt.Year}年{dt.Month}月{dt.Day}日（{dayOfWeek}）";
    }

    // ─── テンプレ用 DTO 群 ───

    /// <summary>テンプレ全体のモデル。各シリーズセクションを内包する。</summary>
    private sealed class EpisodesIndexModel
    {
        public IReadOnlyList<EpisodesIndexSection> Sections { get; set; } = Array.Empty<EpisodesIndexSection>();
        public int TotalSeriesCount { get; set; }
        public int TotalEpisodeCount { get; set; }
        /// <summary>クレジット横断カバレッジラベル（lead 段落末尾に表示）。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>シリーズ単位のエピソードセクション。details で折り畳み可能な単位。</summary>
    private sealed class EpisodesIndexSection
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        /// <summary>
        /// シリーズ開始年の西暦 4 桁文字列（例: "2004"）。v1.3.0 stage22 後段で追加。
        /// summary 行のシリーズタイトル直後に薄色括弧で添える用途。略称（title_short）は使わない。
        /// </summary>
        public string SeriesStartYearLabel { get; set; } = "";
        public string Period { get; set; } = "";
        public string TotalEpisodesLabel { get; set; } = "";
        public IReadOnlyList<EpisodesIndexRow> Episodes { get; set; } = Array.Empty<EpisodesIndexRow>();
    }

    /// <summary>エピソード 1 行。話数・サブタイトル・放送日・詳細リンクを持つ。</summary>
    private sealed class EpisodesIndexRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        /// <summary>ルビ付きサブタイトル HTML（あればこちらを優先表示）。</summary>
        public string TitleRichHtml { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
    }
}