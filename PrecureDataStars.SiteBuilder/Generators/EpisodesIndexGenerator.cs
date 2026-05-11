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
/// v1.3.0 続編 第 N+3 弾：各エピソード行を「メイン段 + スタッフ段」の 2 段構成に拡張。
/// メイン段は従来通り「第N話 + サブタイトル + 放送日」（放送日は <c>2024.2.4</c> 形式に短縮）、
/// スタッフ段にはエピソード詳細と同じ 5 役職（脚本・絵コンテ・演出・作画監督・美術）を
/// 色付きバッジ + 人物名リンク（既に <c>StaffNameLinkResolver</c> で <c>&lt;a&gt;</c> 化済み）の形で並べる。
/// 旧来サブタイトル右隣に余白だった領域を埋めて情報量を増やす。
/// </para>
/// <para>
/// スタッフ情報の抽出は <see cref="SeriesGenerator.ExtractStaffSummaryAsync"/> が
/// シリーズ詳細ページ生成中に既に実施しているため、その memoize 結果
/// （<see cref="SeriesGenerator.GetEpisodeStaffSummaries"/>）をパイプライン経由で受け取る。
/// クレジット階層への再走査を避けて全エピソード分のサマリを得る。
/// </para>
/// <para>
/// ホームのデータベース統計セクションで「エピソード」ボックスをクリックしたときの遷移先がこのページ。
/// </para>
/// </summary>
public sealed class EpisodesIndexGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    // v1.3.0 続編 第 N+3 弾：SeriesGenerator が memoize した「episode_id → EpisodeStaffSummary」の参照。
    // パイプラインから渡される（SeriesGenerator.GenerateAsync 完了後の状態）。
    // クレジット添付対象（credit_attach_to=EPISODE）のシリーズ配下エピソードのみ詰まっている。
    private readonly IReadOnlyDictionary<int, EpisodeStaffSummary> _episodeStaffByIdCache;

    public EpisodesIndexGenerator(
        BuildContext ctx,
        PageRenderer page,
        IReadOnlyDictionary<int, EpisodeStaffSummary> episodeStaffByIdCache)
    {
        _ctx = ctx;
        _page = page;
        _episodeStaffByIdCache = episodeStaffByIdCache;
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
                .Select(e =>
                {
                    // SeriesGenerator が memoize した EpisodeStaffSummary を引き当てる。
                    // クレジット未登録などキャッシュに無いエピソードでは全フィールドが空文字のサマリを使う。
                    var staff = _episodeStaffByIdCache.TryGetValue(e.EpisodeId, out var ss)
                        ? ss
                        : new EpisodeStaffSummary();
                    return new EpisodesIndexRow
                    {
                        SeriesEpNo = e.SeriesEpNo,
                        TitleText = e.TitleText,
                        TitleRichHtml = e.TitleRichHtml ?? "",
                        // v1.3.0 続編 第 N+3 弾：密表示用に「2024.2.4」形式へ短縮（年.月.日、月日は 0 詰めしない）。
                        OnAirDate = FormatCompactDate(e.OnAirAt),
                        EpisodeUrl = PathUtil.EpisodeUrl(s.Slug, e.SeriesEpNo),
                        Screenplay        = staff.Screenplay,
                        Storyboard        = staff.Storyboard,
                        EpisodeDirector   = staff.EpisodeDirector,
                        AnimationDirector = staff.AnimationDirector,
                        ArtDirector       = staff.ArtDirector,
                        StoryboardDirectorMerged = staff.StoryboardDirectorMerged
                    };
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

    /// <summary>
    /// 放送日を「2024.2.4」形式で返す（v1.3.0 続編 第 N+3 弾で追加）。
    /// /episodes/ 行のスタッフ段との同居を踏まえ密表示用に縮める。月日は 0 詰めしない。
    /// </summary>
    private static string FormatCompactDate(DateTime dt)
        => $"{dt.Year}.{dt.Month}.{dt.Day}";

    // ─── テンプレ用 DTO 群 ───

    private sealed class EpisodesIndexModel
    {
        public IReadOnlyList<EpisodesIndexSection> Sections { get; set; } = Array.Empty<EpisodesIndexSection>();
        public int TotalSeriesCount { get; set; }
        public int TotalEpisodeCount { get; set; }
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class EpisodesIndexSection
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesStartYearLabel { get; set; } = "";
        public string Period { get; set; } = "";
        public string TotalEpisodesLabel { get; set; } = "";
        public IReadOnlyList<EpisodesIndexRow> Episodes { get; set; } = Array.Empty<EpisodesIndexRow>();
    }

    private sealed class EpisodesIndexRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string TitleRichHtml { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";

        // v1.3.0 続編 第 N+3 弾：エピソード詳細の 5 役職スタッフサマリ（HTML 断片、PERSON は <a> リンク済み）。
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
        /// <summary>絵コンテ＝演出（同一エントリ集合）のとき true。テンプレ側で 2 バッジ並びにまとめる。</summary>
        public bool StoryboardDirectorMerged { get; set; }
    }
}
