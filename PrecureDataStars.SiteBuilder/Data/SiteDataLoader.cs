using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Data;

/// <summary>
/// パイプライン開始時に走る初期データロード。
/// 複数 Generator 間で共有される全テーブルを 1 度だけクエリしてメモリに載せ、
/// 各 Generator が必要なときに辞書 lookup でアクセスできる形に整える。
/// 全 SELECT は順次 await で発火する：MySQL は単一サーバ I/O のため、<see cref="Task.WhenAll(System.Threading.Tasks.Task[])"/>
/// で並列発火しても I/O 帯域が頭打ちになり、接続オーバーヘッドだけが累積して逆効果になることを
/// 計測で確認済み（13.3s → 13.9s に悪化）。順次取得の方が DB 側で安定するため本方針を採る。
/// </summary>
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
        var episodeThemeSongsRepo = new EpisodeThemeSongsRepository(factory);
        var episodeUsesRepo = new EpisodeUsesRepository(factory);
        var songCreditsRepo = new SongCreditsRepository(factory);
        var songRecordingSingersRepo = new SongRecordingSingersRepository(factory);
        var bgmCuesRepo = new BgmCuesRepository(factory);
        var bgmCueCreditsRepo = new BgmCueCreditsRepository(factory);
        var creditsRepo = new CreditsRepository(factory);
        var creditCardsRepo = new CreditCardsRepository(factory);
        var creditCardTiersRepo = new CreditCardTiersRepository(factory);
        var creditCardGroupsRepo = new CreditCardGroupsRepository(factory);
        var creditCardRolesRepo = new CreditCardRolesRepository(factory);
        var creditRoleBlocksRepo = new CreditRoleBlocksRepository(factory);
        var creditBlockEntriesRepo = new CreditBlockEntriesRepository(factory);
        var personAliasesRepo = new PersonAliasesRepository(factory);
        var characterAliasesRepo = new CharacterAliasesRepository(factory);
        var companyAliasesRepo = new CompanyAliasesRepository(factory);
        var logosRepo = new LogosRepository(factory);
        var songsRepo = new SongsRepository(factory);
        var songRecordingsRepo = new SongRecordingsRepository(factory);
        var songMusicClassesRepo = new SongMusicClassesRepository(factory);
        var rolesRepo = new RolesRepository(factory);
        var roleTemplatesRepo = new RoleTemplatesRepository(factory);
        var familyRepo = new CharacterFamilyRelationsRepository(factory);
        var personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);

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

        // 全エピソードを 1 度の SELECT で取得し、series_id でグルーピングして辞書化する。
        // 旧版は seriesAll の foreach で per-series GetBySeriesAsync を 60+ 回発火する N+1 だった。
        // GetAllAsync は (series_id, series_ep_no) 昇順で返すため、GroupBy 結果はそのまま per-series 取得と
        // 同等の並び順になる。
        var allEpisodes = await episodesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var episodesBySeries = allEpisodes
            .GroupBy(e => e.SeriesId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Episode>)g.ToList());
        logger.Info($"episodes: {allEpisodes.Count} 行（{episodesBySeries.Count} シリーズに分散）");

        // slug および series_id 索引：Generator が相互リンクを引きやすくするため事前構築。
        var seriesIdBySlug = seriesAll
            .Where(s => !string.IsNullOrEmpty(s.Slug))
            .ToDictionary(s => s.Slug, s => s.SeriesId, StringComparer.Ordinal);
        var seriesById = seriesAll.ToDictionary(s => s.SeriesId, s => s);
        // episode_id → Episode のフラット索引。CreditTreeRenderer の EPISODE スコープから
        // series_id を逆引きする等、episode_id 単独参照の需要を辞書 1 段で済ませる。
        var episodeById = allEpisodes.ToDictionary(e => e.EpisodeId);

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
        var titleCharIndex = TitleCharIndex.Build(allEpisodes, seriesById);
        logger.Info($"title_char_index: {titleCharIndex.ByChar.Count} 文字 / {titleCharIndex.CharsByEpisode.Count} エピソードを事前展開");

        // エピソード詳細ページが per-page で引いていた 3 テーブル（パート / 主題歌 / 使用音声）を
        // 全件ロードして episode_id 単位の辞書に事前グルーピングする。各 GetAllAsync の ORDER BY は
        // (episode_id, per-id 取得時と同じ並び) になっており、GroupBy 結果は per-id 取得と同一順を保つ。
        var episodePartsByEpisode = (await episodePartsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.EpisodeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EpisodePart>)g.ToList());
        var themeSongsByEpisode = (await episodeThemeSongsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.EpisodeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EpisodeThemeSong>)g.ToList());
        var episodeUsesByEpisode = (await episodeUsesRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(u => u.EpisodeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EpisodeUse>)g.ToList());
        logger.Info($"episode_parts: {episodePartsByEpisode.Count} ep / episode_theme_songs: {themeSongsByEpisode.Count} ep / episode_uses: {episodeUsesByEpisode.Count} ep");

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

        // 全 credits 行を 1 度の SELECT で取得して episode_id / series_id 単位でグルーピング。
        // SeriesGenerator の per-episode 6 階層 DB ループの起点 GetByEpisodeAsync を撲滅するため、
        // および EpisodeGenerator が per-page で同じ呼び出しを発火していた経路も同時に置き換える前提。
        var allCredits = await creditsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var creditsByEpisode = allCredits
            .Where(c => c.EpisodeId.HasValue)
            .GroupBy(c => c.EpisodeId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Credit>)g.ToList());
        var creditsBySeries = allCredits
            .Where(c => string.Equals(c.ScopeKind, "SERIES", StringComparison.Ordinal) && c.SeriesId.HasValue)
            .GroupBy(c => c.SeriesId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Credit>)g.ToList());
        logger.Info($"credits: episode_scope={creditsByEpisode.Count} ep, series_scope={creditsBySeries.Count} series");

        // マスタ・準マスタテーブルの ID→モデル辞書群。LookupCache と各ジェネレータが per-id GetByIdAsync で
        // 初回 DB 引きしていた経路を撲滅するため、起動時に全件ロードして辞書化する。
        // 論理削除されたものも含めて辞書に乗せる方針：クレジット履歴等で削除済み alias を参照しているケースが
        // 存在するため、解決経路を生かして表示崩れを避ける（PersonAliases / CharacterAliases / CompanyAliases /
        // Logos / SongRecordings は includeDeleted=true でロード）。Songs はフリーテキスト経路も含めて表示するため
        // 同様に削除済み込みで辞書化。
        var personAliasById = (await personAliasesRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false))
            .ToDictionary(a => a.AliasId);
        var characterAliasById = (await characterAliasesRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false))
            .ToDictionary(a => a.AliasId);
        var companyAliasById = (await companyAliasesRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false))
            .ToDictionary(a => a.AliasId);
        var logoById = (await logosRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false))
            .ToDictionary(l => l.LogoId);
        var songById = (await songsRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false))
            .ToDictionary(s => s.SongId);
        var songRecordingById = (await songRecordingsRepo.GetAllAsync(includeDeleted: true, ct).ConfigureAwait(false))
            .ToDictionary(r => r.SongRecordingId);
        var musicClassByCode = (await songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(c => c.ClassCode, StringComparer.Ordinal);
        logger.Info($"alias / song masters: person={personAliasById.Count}, character={characterAliasById.Count}, company={companyAliasById.Count}, logo={logoById.Count}, song={songById.Count}, song_recording={songRecordingById.Count}");

        // 録音単位の楽曲リンク URL（楽曲詳細ページの id="recording-{N}" アンカーに対応）。
        // 楽曲詳細の録音セクションは「非削除録音を song_recording_id 昇順」で並べて for.index+1 を id に振るため、
        // ここでも同条件で並べ、先頭（筆頭録音）はページ先頭 URL、2 番目以降は #recording-{N} を割り当てる。
        var songRecordingAnchorUrlById = new Dictionary<int, string>();
        foreach (var grp in songRecordingById.Values.Where(r => !r.IsDeleted).GroupBy(r => r.SongId))
        {
            int idx = 0;
            foreach (var rec in grp.OrderBy(r => r.SongRecordingId))
            {
                idx++;
                songRecordingAnchorUrlById[rec.SongRecordingId] =
                    idx == 1 ? PathUtil.SongUrl(grp.Key) : $"{PathUtil.SongUrl(grp.Key)}#recording-{idx}";
            }
        }

        // 役職マスタと役職テンプレ。role_templates は (role_code, series_id) 解決を C# 側でやるための
        // 専用 Resolver でラップ（CreditTreeRenderer の per-sibling-role 引きを辞書 lookup に置き換える）。
        var roleByCode = (await rolesRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        var allRoleTemplates = await roleTemplatesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var roleTemplateResolver = new RoleTemplateResolver(allRoleTemplates);
        logger.Info($"role masters: roles={roleByCode.Count}, role_templates={allRoleTemplates.Count}");

        // 家族関係を character_id 単位でグルーピング。CharactersGenerator / PrecuresGenerator の
        // per-character GetByCharacterAsync を撲滅する。
        var familyRelationsByCharacter = (await familyRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(r => r.CharacterId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CharacterFamilyRelation>)g.ToList());

        // person_id → alias_id 群の逆引き辞書。PersonsGenerator / CreatorsGenerator が
        // 個別に同じ辞書を構築していた処理を SiteDataLoader に集約する。
        var aliasIdsByPerson = (await personAliasPersonsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.PersonId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g.OrderBy(l => l.AliasId).Select(l => l.AliasId).ToList());
        logger.Info($"family={familyRelationsByCharacter.Count} char / alias_persons={aliasIdsByPerson.Count} person");

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
            EpisodePartsByEpisode = episodePartsByEpisode,
            ThemeSongsByEpisode = themeSongsByEpisode,
            EpisodeUsesByEpisode = episodeUsesByEpisode,
            TracksByCatalogNo = tracksByCatalogNo,
            SongCreditsBySong = songCreditsBySong,
            SingersByRecording = singersByRecording,
            BgmCuesBySeries = bgmCuesBySeries,
            BgmCueCreditsByCue = bgmCueCreditsByCue,
            CreditTree = creditTreeIndex,
            CreditsByEpisode = creditsByEpisode,
            CreditsBySeries = creditsBySeries,
            PersonAliasById = personAliasById,
            CharacterAliasById = characterAliasById,
            CompanyAliasById = companyAliasById,
            LogoById = logoById,
            SongById = songById,
            SongRecordingById = songRecordingById,
            SongRecordingAnchorUrlById = songRecordingAnchorUrlById,
            MusicClassByCode = musicClassByCode,
            RoleByCode = roleByCode,
            RoleTemplateResolver = roleTemplateResolver,
            FamilyRelationsByCharacter = familyRelationsByCharacter,
            AliasIdsByPerson = aliasIdsByPerson
        };
    }
}
