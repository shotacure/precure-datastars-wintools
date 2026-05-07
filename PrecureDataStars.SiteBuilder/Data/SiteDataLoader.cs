using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Data;

/// <summary>
/// パイプライン開始時に走る初期データロード。
/// <para>
/// シリーズ・エピソード・パート種別マスタなど、複数 Generator 間で共有される
/// 「変動の少ないテーブル」を 1 回だけクエリしてメモリに載せる。
/// 個別ページ固有の集計（クレジット階層、文字統計、偏差値など）はそれぞれの
/// Generator が必要に応じて追加で問い合わせる方針。
/// </para>
/// </summary>
public static class SiteDataLoader
{
    /// <summary>
    /// 接続ファクトリと設定から <see cref="BuildContext"/> を構築する。
    /// </summary>
    public static async Task<BuildContext> LoadAsync(
        BuildConfig config,
        BuildLogger logger,
        BuildSummary summary,
        IConnectionFactory factory,
        CancellationToken ct = default)
    {
        logger.Section("Loading shared data");

        var seriesRepo = new SeriesRepository(factory);
        var episodesRepo = new EpisodesRepository(factory);
        var partTypesRepo = new PartTypesRepository(factory);
        var seriesKindsRepo = new SeriesKindsRepository(factory);

        // シリーズ：論理削除済を除く全件。GetAllAsync は start_date, series_id 順で返す。
        var seriesAll = await seriesRepo.GetAllAsync(ct).ConfigureAwait(false);
        logger.Info($"series: {seriesAll.Count} 行");

        // パート種別：display_order 順で表示するため取得しておく。
        var partTypes = await partTypesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var partTypeByCode = partTypes.ToDictionary(p => p.PartTypeCode, p => p, StringComparer.Ordinal);
        logger.Info($"part_types: {partTypes.Count} 行");

        // シリーズ種別マスタ：シリーズページのバッジ等に使う想定。
        var seriesKinds = await seriesKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var seriesKindByCode = seriesKinds.ToDictionary(k => k.KindCode, k => k, StringComparer.Ordinal);

        // エピソード：シリーズごとに分けて辞書化。
        var episodesBySeries = new Dictionary<int, IReadOnlyList<Episode>>();
        var totalEpisodes = 0;
        foreach (var s in seriesAll)
        {
            var eps = await episodesRepo.GetBySeriesAsync(s.SeriesId, ct).ConfigureAwait(false);
            episodesBySeries[s.SeriesId] = eps;
            totalEpisodes += eps.Count;
        }
        logger.Info($"episodes: {totalEpisodes} 行（{seriesAll.Count} シリーズに分散）");

        // slug および series_id 索引：Generator が相互リンクを引きやすくするため事前構築。
        var seriesIdBySlug = seriesAll
            .Where(s => !string.IsNullOrEmpty(s.Slug))
            .ToDictionary(s => s.Slug, s => s.SeriesId, StringComparer.Ordinal);
        var seriesById = seriesAll.ToDictionary(s => s.SeriesId, s => s);

        return new BuildContext
        {
            Config = config,
            Logger = logger,
            Summary = summary,
            Series = seriesAll,
            EpisodesBySeries = episodesBySeries,
            PartTypeByCode = partTypeByCode,
            SeriesKindByCode = seriesKindByCode,
            SeriesIdBySlug = seriesIdBySlug,
            SeriesById = seriesById
        };
    }
}
