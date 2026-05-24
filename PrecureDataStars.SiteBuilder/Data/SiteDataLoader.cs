using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Data;

/// <summary>パイプライン開始時に走る初期データロード。 シリーズ・エピソード・パート種別マスタなど、複数 Generator 間で共有される 「変動の少ないテーブル」を 1 回だけクエリしてメモリに載せる。 個別ページ固有の集計（クレジット階層、文字統計、偏差値など）はそれぞれの Generator が必要に応じて追加で問い合わせる方針。</summary>
public static class SiteDataLoader
{
    /// <summary>接続ファクトリと設定から <see cref="BuildContext"/> を構築する。</summary>
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
        var episodePartsRepo = new EpisodePartsRepository(factory);
        var tracksRepo = new TracksRepository(factory);
        var songCreditsRepo = new SongCreditsRepository(factory);
        var songRecordingSingersRepo = new SongRecordingSingersRepository(factory);
        var bgmCuesRepo = new BgmCuesRepository(factory);
        var bgmCueCreditsRepo = new BgmCueCreditsRepository(factory);
        var creditCardsRepo = new CreditCardsRepository(factory);
        var creditCardTiersRepo = new CreditCardTiersRepository(factory);
        var creditCardGroupsRepo = new CreditCardGroupsRepository(factory);
        var creditCardRolesRepo = new CreditCardRolesRepository(factory);
        var creditRoleBlocksRepo = new CreditRoleBlocksRepository(factory);
        var creditBlockEntriesRepo = new CreditBlockEntriesRepository(factory);

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
        // episode_id → Episode のフラット索引。CreditTreeRenderer の EPISODE スコープから
        // series_id を逆引きする等、episode_id 単独参照の需要を辞書 1 段で済ませる。
        var episodeById = episodesBySeries.Values
            .SelectMany(eps => eps)
            .ToDictionary(e => e.EpisodeId);

        // 直近放送 TV エピソードの算出。
        var nowAtBuild = DateTime.Now;
        (Series Series, Episode Episode)? latestAired = null;
        DateTime latestSoFar = DateTime.MinValue;
        foreach (var s in seriesAll)
        {
            if (!string.Equals(s.KindCode, "TV", StringComparison.Ordinal)) continue;
            if (!episodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            foreach (var e in eps)
            {
                if (e.OnAirAt > nowAtBuild) continue;
                if (e.OnAirAt > latestSoFar)
                {
                    latestSoFar = e.OnAirAt;
                    latestAired = (s, e);
                }
            }
        }
        if (latestAired is { } la)
        {
            logger.Info($"latest aired TV episode: 『{la.Series.Title}』第{la.Episode.SeriesEpNo}話 ({la.Episode.OnAirAt:yyyy-MM-dd})");
        }

        // 全エピソード分のパート尺偏差値・順位を 1 度だけ算出する。
        // EpisodeGenerator が per-page で全件 CTE 集計を繰り返さないよう、
        // 結果を episode_id 単位の辞書に格納してビルドコンテキストで共有する。
        var partLengthStatsByEpisode = await episodePartsRepo.GetAllPartLengthStatsAsync(ct).ConfigureAwait(false);
        logger.Info($"part_length_stats: {partLengthStatsByEpisode.Count} エピソード分を事前計算");

        // サブタイトル文字統計の事前展開（DB アクセスなし、ロード済み episodes の title_char_stats JSON を C# 側でパース）。
        // TitleCharInfoRenderer がページごとに JSON_CONTAINS_PATH 全表走査を文字数分繰り返さないよう、
        // 文字キー → 出現エピソード一覧（TotalEpNo 昇順）の辞書を構築してビルドコンテキストで共有する。
        var allEpisodesEnumerable = episodesBySeries.Values.SelectMany(eps => eps);
        var titleCharIndex = TitleCharIndex.Build(allEpisodesEnumerable, seriesById);
        logger.Info($"title_char_index: {titleCharIndex.ByChar.Count} 文字 / {titleCharIndex.CharsByEpisode.Count} エピソードを事前展開");

        // 商品・楽曲・劇伴の各ジェネレータが共通で必要とする「テーブル全件 → ID 単位辞書」を一括構築。
        // SongsGenerator / MusicGenerator / ProductsGenerator がそれぞれ自前で GetAllAsync を呼んで
        // 辞書化していたのを SiteDataLoader に集約し、生成中の per-disc / per-track DB 往復を排除する。
        var tracksByCatalogNo = (await tracksRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.CatalogNo, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Track>)g.ToList(), StringComparer.Ordinal);
        logger.Info($"tracks: {tracksByCatalogNo.Count} catalog_no 分");

        var songCreditsBySong = (await songCreditsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.SongId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SongCredit>)g.ToList());
        logger.Info($"song_credits: {songCreditsBySong.Count} 曲分");

        var singersByRecording = (await songRecordingSingersRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(s => s.SongRecordingId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SongRecordingSinger>)g.ToList());
        logger.Info($"song_recording_singers: {singersByRecording.Count} 録音分");

        var bgmCuesBySeries = (await bgmCuesRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(c => c.SeriesId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BgmCue>)g.ToList());
        logger.Info($"bgm_cues: {bgmCuesBySeries.Count} シリーズ分");

        var bgmCueCreditsByCue = (await bgmCueCreditsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(c => (c.SeriesId, c.MNoDetail))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BgmCueCredit>)g.ToList());
        logger.Info($"bgm_cue_credits: {bgmCueCreditsByCue.Count} cue 分");

        // クレジット階層 6 段を 1 度だけ全件取得し、credit_id 単位でネスト化したスナップショットを構築。
        // CreditTreeRenderer がページ生成中に階層別の GetBy*Async を発火しないようにするため、本処理を
        // ビルド開始時に 1 度だけ走らせて BuildContext 経由で全 Generator から参照できるようにする。
        var creditTreeIndex = await CreditTreeIndex.BuildAsync(
            creditCardsRepo, creditCardTiersRepo, creditCardGroupsRepo,
            creditCardRolesRepo, creditRoleBlocksRepo, creditBlockEntriesRepo, ct).ConfigureAwait(false);
        logger.Info($"credit_tree: {creditTreeIndex.CardsByCreditId.Count} クレジット分を事前展開");

        return new BuildContext
        {
            Config = config,
            Logger = logger,
            Summary = summary,
            Series = seriesAll,
            EpisodesBySeries = episodesBySeries,
            EpisodeById = episodeById,
            PartTypeByCode = partTypeByCode,
            SeriesKindByCode = seriesKindByCode,
            SeriesIdBySlug = seriesIdBySlug,
            SeriesById = seriesById,
            LatestAiredTvEpisode = latestAired,
            PartLengthStatsByEpisode = partLengthStatsByEpisode,
            TitleCharIndex = titleCharIndex,
            TracksByCatalogNo = tracksByCatalogNo,
            SongCreditsBySong = songCreditsBySong,
            SingersByRecording = singersByRecording,
            BgmCuesBySeries = bgmCuesBySeries,
            BgmCueCreditsByCue = bgmCueCreditsByCue,
            CreditTree = creditTreeIndex
        };
    }
}
