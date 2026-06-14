using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>/stats/</c> ランディングページの生成。
/// 統計セクション全体の入口。サブタイトル統計（<c>/stats/subtitles/</c>）、エピソード尺統計
/// （<c>/stats/episodes/</c>）の 2 大セクションへのリンクを並べる索引ページ。
/// 役職別ランキング（<c>/creators/roles/</c>）・声優ランキング（<c>/creators/voice-cast/</c>）は
/// 「クリエーター」セクションへ移設済みで <see cref="CreatorsGenerator"/> が生成するため、本ランディングは
/// 関与系を扱わない。
/// 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」
/// 表記（カバレッジラベル）を 2 セクション（サブタイトル統計 / エピソード尺統計）の各 h2 直下に
/// 個別表示する。2 つの統計はそれぞれ最終断面が異なるため、ページ上部に 1 つだけ表示する形では不正確になる。
/// <list type="bullet">
///   <item><description>サブタイトル統計：サブタイトル本文が登録済みの最新 TV エピソード</description></item>
///   <item><description>エピソード尺統計：パート情報が登録済みの最新 TV エピソード</description></item>
/// </list>
/// </summary>
public sealed class StatsLandingGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly EpisodePartStatsRepository _epRepo;

    public StatsLandingGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _epRepo = new EpisodePartStatsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating stats landing");

        // 2 軸のカバレッジラベルを揃える。
        // クレジット関連（役職別の担当話数・声の出演）は /creators/ で扱うため、本ランディングでは
        // サブタイトル軸とエピソード尺軸の 2 つを個別に算出してカードに添える。
        var episodeIdsWithParts = (await _epRepo.GetEpisodeIdsWithPartsAsync(ct).ConfigureAwait(false)).ToHashSet();
        var latestSubtitle = StatsCoverageLabel.FindLatestTvEpisodeWithSubtitle(_ctx);
        var latestParts    = StatsCoverageLabel.FindLatestTvEpisodeWithParts(_ctx, episodeIdsWithParts);

        var content = new ContentModel
        {
            SubtitleCoverageLabel = StatsCoverageLabel.Build(latestSubtitle),
            EpisodeCoverageLabel  = StatsCoverageLabel.Build(latestParts)
        };
        var layout = new LayoutModel
        {
            PageTitle = "統計",
            MetaDescription = "サブタイトルの漢字率や文字数、本編パートの尺やアバンの長さまで。歴代プリキュア全レギュラー TV シリーズの全エピソードを数字で読み解く統計集です。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "" }
            }
        };

        _page.RenderAndWrite("/stats/", "stats-landing", "stats-landing.sbn", content, layout);
        _ctx.Logger.Success("/stats/");
    }

    /// <summary>テンプレ用モデル。リンク群はテンプレ側でカード型に静的に並べているため、 データプロパティは 2 セクション（サブタイトル統計／エピソード尺統計）それぞれのカバレッジラベルのみ。</summary>
    private sealed class ContentModel
    {
        /// <summary>サブタイトル統計カードに添えるカバレッジラベル。</summary>
        public string SubtitleCoverageLabel { get; set; } = "";
        /// <summary>エピソード尺統計カードに添えるカバレッジラベル。</summary>
        public string EpisodeCoverageLabel { get; set; } = "";
    }
}