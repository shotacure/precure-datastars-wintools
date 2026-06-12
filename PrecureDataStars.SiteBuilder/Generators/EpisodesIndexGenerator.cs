using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>/episodes/</c> エピソード一覧ランディングページのジェネレータ。
/// 全 TV シリーズのエピソードをシリーズ別のセクション区切りで一覧表示する単一ページ。
/// 各シリーズセクションは <c>&lt;details&gt;</c> で折り畳み可能とし、初期状態ではいずれも閉じる
/// （描画コスト・スクロール量の抑制）。各エピソード行は「メイン段 + スタッフ段」の 2 段構成。
/// メイン段は「第N話 + サブタイトル + 放送日」（放送日は <c>2024.2.4</c> 形式）、
/// スタッフ段にはエピソード詳細と同じ 5 役職（脚本・絵コンテ・演出・作画監督・美術）を
/// 色付きバッジ + 人物名リンク（<c>StaffNameLinkResolver</c> で <c>&lt;a&gt;</c> 化済み）の形で並べる。
/// スタッフ情報の抽出は <see cref="SeriesGenerator.ExtractStaffSummaryAsync"/> がシリーズ詳細ページ
/// 生成中に実施した memoize 結果（<see cref="SeriesGenerator.GetEpisodeStaffSummaries"/>）を
/// パイプライン経由で受け取り、クレジット階層への再走査を避ける。
/// ホームのデータベース統計セクション「エピソード」ボックスからの遷移先。
/// </summary>
public sealed class EpisodesIndexGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    // SeriesGenerator が memoize した「episode_id → EpisodeStaffSummary」の参照。
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
                        // 密表示用に「2024.2.4」形式へ短縮（年.月.日、月日は 0 詰めしない）。
                        OnAirDate = JpDateFormat.DotDate(e.OnAirAt),
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

            // /episodes/ は TV シリーズのみ対象なので、放送中（EndDate=null）は
            // 「2025年2月2日 〜」と「〜」止めで放送継続中を示す。
            // 「（見込）」注記はビルド時点で終了していない（EndDate が未来 or NULL）かつ
            // 総話数マスタ値があるシリーズに付ける。終了済みシリーズは実話数とのギャップが
            // あっても確定扱いで注記なし（データ入力残と見込みは別問題なので一緒にしない）。
            // 総話数マスタ値が無い継続中（end_date=null）の TV シリーズはまだ確定できないので
            // 「N 話」ラベル自体を出さず空文字にする。
            var today = DateOnly.FromDateTime(DateTime.Now);
            bool estimated = s.Episodes.HasValue
                && (!s.EndDate.HasValue || s.EndDate.Value > today);
            string totalLabel;
            if (s.Episodes.HasValue) totalLabel = $"全 {s.Episodes.Value} 話";
            else if (!s.EndDate.HasValue) totalLabel = "";
            else totalLabel = $"{rows.Count} 話";
            sections.Add(new EpisodesIndexSection
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                // 後段：シリーズ summary 行に薄色括弧で添える西暦 4 桁。
                SeriesStartYearLabel = s.StartDate.Year.ToString(),
                Period = JpDateFormat.PeriodOrOngoing(s.StartDate, s.EndDate),
                PeriodEstimateNote = (estimated && s.EndDate.HasValue) ? "（見込）" : "",
                TotalEpisodesLabel = totalLabel,
                TotalEpisodesEstimateNote = (estimated && s.Episodes.HasValue) ? "（見込）" : "",
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
            PageTitle = "歴代プリキュアTVエピソード",
            MetaDescription = "第 1 話から最新話まで、歴代プリキュアの全レギュラーTVシリーズの各話を一覧にまとめました。サブタイトル・放送日のほか、脚本・絵コンテ・演出・作画監督・美術の担当スタッフまでたどれます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュアTVエピソード", Url = "" }
            }
        };

        _page.RenderAndWrite("/episodes/", "episodes", "episodes-index.sbn", content, layout);
        _ctx.Logger.Success($"episodes index: {sections.Count} シリーズ / {totalEpisodes} エピソード");
    }

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
        /// <summary>放送期間の見込み注記（「（見込）」または空文字）。テンプレ側で nowrap span に入れる。</summary>
        public string PeriodEstimateNote { get; set; } = "";
        public string TotalEpisodesLabel { get; set; } = "";
        /// <summary>総話数の見込み注記（「（見込）」または空文字）。テンプレ側で nowrap span に入れる。</summary>
        public string TotalEpisodesEstimateNote { get; set; } = "";
        public IReadOnlyList<EpisodesIndexRow> Episodes { get; set; } = Array.Empty<EpisodesIndexRow>();
    }

    private sealed class EpisodesIndexRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string TitleRichHtml { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";

        // エピソード詳細の 5 役職スタッフサマリ（HTML 断片、PERSON は <a> リンク済み）。
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
        /// <summary>絵コンテ＝演出（同一エントリ集合）のとき true。テンプレ側で 2 バッジ並びにまとめる。</summary>
        public bool StoryboardDirectorMerged { get; set; }
    }
}