
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>/stats/</c> ランディングページの生成（v1.3.0 後半追加）。
/// <para>
/// 統計セクション全体の入口。役職別ランキング（<c>/stats/roles/</c>）、声優ランキング（<c>/stats/voice-cast/</c>）、
/// サブタイトル統計（<c>/stats/subtitles/</c>）、エピソード尺統計（<c>/stats/episodes/</c>）の 4 大セクションへの
/// リンクを並べる索引ページ。
/// </para>
/// <para>
/// v1.3.0 ブラッシュアップ続編で「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」
/// 表記（カバレッジラベル）を 3 セクション（関与統計 / サブタイトル統計 / エピソード尺統計）の各 h2 直下に
/// 個別表示する。3 つの統計はそれぞれ最終断面が異なるため、ページ上部に 1 つだけ表示する形では不正確になる。
/// </para>
/// <list type="bullet">
///   <item><description>関与統計：クレジットが登録済みの最新 TV エピソード</description></item>
///   <item><description>サブタイトル統計：サブタイトル本文が登録済みの最新 TV エピソード</description></item>
///   <item><description>エピソード尺統計：パート情報が登録済みの最新 TV エピソード</description></item>
/// </list>
/// </summary>
public sealed class StatsLandingGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _creditIndex;
    private readonly EpisodePartStatsRepository _epRepo;

    public StatsLandingGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex creditIndex)
    {
        _ctx = ctx;
        _page = page;
        _creditIndex = creditIndex;
        _epRepo = new EpisodePartStatsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating stats landing");

        // 3 軸それぞれの最新 TV エピソードを判定してカバレッジラベルを生成する。
        // エピソード尺軸ではパート情報を持つ episode_id 集合をクエリで取得、
        // クレジット軸では既存の CreditInvolvementIndex から episode_id 集合を抽出する。
        var episodeIdsWithParts = (await _epRepo.GetEpisodeIdsWithPartsAsync(ct).ConfigureAwait(false)).ToHashSet();
        var episodeIdsWithCredits = StatsCoverageLabel.CollectEpisodeIdsWithCredits(_creditIndex);

        var latestCredit   = StatsCoverageLabel.FindLatestTvEpisodeWithCredits(_ctx, episodeIdsWithCredits);
        var latestSubtitle = StatsCoverageLabel.FindLatestTvEpisodeWithSubtitle(_ctx);
        var latestParts    = StatsCoverageLabel.FindLatestTvEpisodeWithParts(_ctx, episodeIdsWithParts);

        var content = new ContentModel
        {
            CreditCoverageLabel   = StatsCoverageLabel.Build(latestCredit),
            SubtitleCoverageLabel = StatsCoverageLabel.Build(latestSubtitle),
            EpisodeCoverageLabel  = StatsCoverageLabel.Build(latestParts)
        };
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

    /// <summary>
    /// テンプレ用モデル。リンク群はテンプレ側で静的に並べているため
    /// データプロパティは 3 セクションそれぞれのカバレッジラベル。
    /// </summary>
    private sealed class ContentModel
    {
        /// <summary>関与統計セクション h2 直下に表示するカバレッジラベル。</summary>
        public string CreditCoverageLabel { get; set; } = "";
        /// <summary>サブタイトル統計セクション h2 直下に表示するカバレッジラベル。</summary>
        public string SubtitleCoverageLabel { get; set; } = "";
        /// <summary>エピソード尺統計セクション h2 直下に表示するカバレッジラベル。</summary>
        public string EpisodeCoverageLabel { get; set; } = "";
    }
}
