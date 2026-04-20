using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.LegacyImport;

/// <summary>
/// 旧 SQL Server 版 PrecureDataStars から新 MySQL 版への移行コンソールツール。
/// <para>
/// 旧テーブル: series / discs / tracks / songs / musics
/// 新テーブル: products / discs / tracks / songs / song_recordings / bgm_sessions / bgm_cues
/// </para>
/// <para>
/// 本バージョンでは以下の設計方針を採用:
/// </para>
/// <list type="number">
///   <item>
///     <b>product_catalog_no 方式</b>:
///     products の主キーは代表品番（1 枚物は唯一のディスクの catalog_no、複数枚組は 1 枚目の catalog_no）。
///   </item>
///   <item>
///     <b>set_with グルーピング</b>:
///     旧 discs.set_with で同じ値を持つ行は同一商品として集約。
///     set_with IS NULL のものは親 disc、set_with=親の旧 id のものは子 disc。
///     グループのうち最若 id を代表として products を 1 件作り、
///     disc_no_in_set=1 (親), 2, 3... (子を旧 id 昇順) で新 discs に展開する。
///   </item>
///   <item>
///     <b>songs: メロディ + アレンジ単位</b>:
///     新 songs は旧 songs.arrange_class 単位で 1 行作る。歌唱者違いは song_recordings に展開。
///     派生歌手行の title が親 arrange_class 行と異なる場合は variant_label に派生 title を保存。
///   </item>
///   <item>
///     <b>track_title_override の常時保存</b>:
///     旧 tracks.track_title が非空なら、SONG/BGM/その他問わず新 tracks.track_title_override に保存する。
///     同じ音源でもディスクによって異なる表記で収録されることがあるため、収録盤固有の表記を尊重する。
///   </item>
///   <item>
///     <b>BGM 1 テーブルモデル</b>:
///     新 bgm_cues は 1 行 = 1 音源。PK (series_id, m_no_detail)、session_no 属性で bgm_sessions を参照。
///     旧 musics の distinct な rec_session をシリーズごとに 1, 2, ... と採番して bgm_sessions に投入し、
///     空文字 rec_session は session_no=0（既定「未設定」）にマップする。
///     旧 musics.m_no_detail / m_no_class はそのまま新 bgm_cues の同名列に保存（枝番分解は行わない）。
///   </item>
///   <item>
///     <b>v1.1.1: series_id の所在変更</b>:
///     新 DB ではシリーズ所属は products ではなく discs 側の属性となった。
///     旧 discs.series_id の値はグループ内の全新 discs に同じ値としてセットする
///     （旧 DB は 1 商品 = 1 シリーズの前提だったため情報損失なし）。商品 (products) には series_id を持たせない。
///   </item>
/// </list>
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool dryRun = args.Contains("--dry-run");
        string? seriesOverridePath = args
            .FirstOrDefault(a => a.StartsWith("--series-map-override=", StringComparison.Ordinal))
            ?.Substring("--series-map-override=".Length);

        var legacyCs = ConfigurationManager.ConnectionStrings["LegacyServer"]?.ConnectionString;
        var targetCs = ConfigurationManager.ConnectionStrings["TargetMySql"]?.ConnectionString;
        if (string.IsNullOrWhiteSpace(legacyCs) || string.IsNullOrWhiteSpace(targetCs))
        {
            Console.Error.WriteLine("App.config に LegacyServer / TargetMySql 接続文字列を設定してください。");
            return 2;
        }

        Console.WriteLine("=== PrecureDataStars LegacyImport ===");
        Console.WriteLine($"dry-run     : {dryRun}");

        await using var legacy = new SqlConnection(legacyCs);
        await legacy.OpenAsync();

        var factory = new MySqlConnectionFactory(new DbConfig(targetCs));
        var productsRepo = new ProductsRepository(factory);
        var discsRepo = new DiscsRepository(factory);
        var tracksRepo = new TracksRepository(factory);
        var songsRepo = new SongsRepository(factory);
        var songRecRepo = new SongRecordingsRepository(factory);
        var bgmSessionsRepo = new BgmSessionsRepository(factory);
        var bgmCuesRepo = new BgmCuesRepository(factory);
        var seriesRepo = new SeriesRepository(factory);

        try
        {
            var report = new Report();

            // ステップ 1: series マッピング構築
            var seriesMap = await BuildSeriesMapAsync(legacy, seriesRepo, seriesOverridePath, report);

            // ステップ 1.5: 新 series_id → (kind_code, start_date) の補助辞書
            var seriesInfoMap = await BuildSeriesInfoMapAsync(seriesRepo);

            // ステップ 2: songs の二階層展開
            var songRecMap = await MigrateSongsAsync(legacy, songsRepo, songRecRepo, seriesMap, report, dryRun);

            // ステップ 3: musics → bgm_cues + bgm_sessions
            var bgmCueMap = await MigrateMusicsAsync(legacy, bgmSessionsRepo, bgmCuesRepo, seriesMap, report, dryRun);

            // ステップ 4: discs → products + discs
            var discCatalogMap = await MigrateDiscsAsync(legacy, productsRepo, discsRepo, seriesMap, seriesInfoMap, report, dryRun);

            // ステップ 5: tracks
            await MigrateTracksAsync(legacy, tracksRepo, discCatalogMap, songRecMap, bgmCueMap, report, dryRun);

            Console.WriteLine();
            Console.WriteLine("=== 移行サマリー ===");
            Console.WriteLine($"series mapped      : {report.SeriesMapped}");
            Console.WriteLine($"products created   : {report.ProductsCreated}");
            Console.WriteLine($"discs created      : {report.DiscsCreated}");
            Console.WriteLine($"tracks created     : {report.TracksCreated}");
            Console.WriteLine($"songs created      : {report.SongsCreated}");
            Console.WriteLine($"song_recordings    : {report.SongRecordingsCreated}");
            Console.WriteLine($"bgm_sessions       : {report.BgmSessionsCreated}");
            Console.WriteLine($"bgm_cues created   : {report.BgmCuesCreated}");
            foreach (var w in report.Warnings.Take(30))
                Console.WriteLine($"  - {w}");

            if (dryRun) Console.WriteLine("\n[dry-run] DB 書き込みは行われていません。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex}");
            return 1;
        }
    }

    // ========================================================================= 
    // ステップ 1: series マッピング 
    // ========================================================================= 

    private static async Task<Dictionary<string, int>> BuildSeriesMapAsync(
        SqlConnection legacy, SeriesRepository seriesRepo, string? overridePath, Report report)
    {
        var newSeriesList = await seriesRepo.GetAllAsync();
        var byStartDate = newSeriesList.GroupBy(s => s.StartDate).ToDictionary(g => g.Key, g => g.First().SeriesId);
        var legacyRows = (await legacy.QueryAsync<LegacySeriesRow>("SELECT id AS Id, title AS Title FROM series")).ToList();
        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        var legacyAliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["20080205"] = "20080203" };

        foreach (var row in legacyRows)
        {
            string resolveKey = legacyAliases.TryGetValue(row.Id, out var aliasedTo) ? aliasedTo : row.Id;
            if (!DateOnly.TryParseExact(resolveKey, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var legacyDate))
            {
                report.Warnings.Add($"series id='{row.Id}' が YYYYMMDD として解釈できません");
                report.SeriesUnmapped++;
                continue;
            }
            if (byStartDate.TryGetValue(legacyDate, out int newId)) { map[row.Id] = newId; report.SeriesMapped++; }
            else { report.Warnings.Add($"series 未マッピング: 旧 id={row.Id}"); report.SeriesUnmapped++; }
        }

        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            foreach (var line in File.ReadAllLines(overridePath))
            {
                var s = line.Trim();
                if (string.IsNullOrEmpty(s) || s.StartsWith("#")) continue;
                var parts = s.Split(',', 2);
                if (parts.Length != 2) continue;
                if (int.TryParse(parts[1].Trim(), out int newId)) map[parts[0].Trim()] = newId;
            }
        }
        return map;
    }

    private static async Task<Dictionary<int, SeriesInfo>> BuildSeriesInfoMapAsync(SeriesRepository seriesRepo)
    {
        var list = await seriesRepo.GetAllAsync();
        return list.ToDictionary(s => s.SeriesId, s => new SeriesInfo(s.KindCode ?? "", s.StartDate));
    }

    /// <summary>新 series の種別判定用の最小情報。</summary>
    private readonly record struct SeriesInfo(string KindCode, DateOnly StartDate);

    // ========================================================================= 
    // ステップ 2: songs 二階層展開 
    // ========================================================================= 

    private static async Task<Dictionary<int, int>> MigrateSongsAsync(
        SqlConnection legacy, SongsRepository songsRepo, SongRecordingsRepository songRecRepo,
        Dictionary<string, int> seriesMap, Report report, bool dryRun)
    {
        const string sql = @"
            SELECT id AS OldSongId, music_class AS MusicClass, arrange_class AS ArrangeClass,
                   series_id AS SeriesId, title AS Title, title_kana AS TitleKana,
                   singer_name AS SingerName, singer_name_kana AS SingerNameKana,
                   lyricist_name AS LyricistName, lyricist_name_kana AS LyricistNameKana,
                   composer_name AS ComposerName, composer_name_kana AS ComposerNameKana,
                   arranger_name AS ArrangerName, arranger_name_kana AS ArrangerNameKana,
                   memo AS Memo
            FROM songs ORDER BY id;";

        var rows = (await legacy.QueryAsync<LegacySong>(sql)).ToList();
        var arrangeClassToNewSongId = new Dictionary<int, int>();
        var arrangeTitle = new Dictionary<int, string?>();
        var oldToNewSongRecId = new Dictionary<int, int>();

        foreach (var row in rows.Where(r => r.OldSongId == r.ArrangeClass))
        {
            int? newSeriesId = ResolveSeries(row.SeriesId, seriesMap);
            var song = new Song
            {
                Title = row.Title ?? "",
                TitleKana = NullIfEmpty(row.TitleKana),
                SeriesId = newSeriesId,
                LyricistName = NullIfEmpty(row.LyricistName),
                LyricistNameKana = NullIfEmpty(row.LyricistNameKana),
                ComposerName = NullIfEmpty(row.ComposerName),
                ComposerNameKana = NullIfEmpty(row.ComposerNameKana),
                ArrangerName = NullIfEmpty(row.ArrangerName),
                ArrangerNameKana = NullIfEmpty(row.ArrangerNameKana),
                Notes = NullIfEmpty(row.Memo),
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };
            int newSongId = 0;
            if (!dryRun) newSongId = await songsRepo.InsertAsync(song);
            report.SongsCreated++;
            arrangeClassToNewSongId[row.OldSongId] = newSongId;
            arrangeTitle[row.OldSongId] = row.Title;
        }

        foreach (var row in rows)
        {
            if (!arrangeClassToNewSongId.TryGetValue(row.ArrangeClass, out int parentNewSongId)) continue;
            string? variantLabel = null;
            if (row.OldSongId != row.ArrangeClass)
            {
                arrangeTitle.TryGetValue(row.ArrangeClass, out string? parentT);
                if (!string.IsNullOrWhiteSpace(row.Title) && !string.Equals(row.Title, parentT, StringComparison.Ordinal))
                    variantLabel = row.Title;
            }
            var rec = new SongRecording
            {
                SongId = parentNewSongId,
                SingerName = NullIfEmpty(row.SingerName),
                SingerNameKana = NullIfEmpty(row.SingerNameKana),
                VariantLabel = variantLabel,
                Notes = NullIfEmpty(row.Memo),
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };
            int newRecId = 0;
            if (!dryRun) newRecId = await songRecRepo.InsertAsync(rec);
            report.SongRecordingsCreated++;
            oldToNewSongRecId[row.OldSongId] = newRecId;
        }
        return oldToNewSongRecId;
    }

    // ========================================================================= 
    // ステップ 3: musics → bgm_sessions + bgm_cues 
    // ========================================================================= 

    private readonly record struct BgmCueKey(int SeriesId, string MNoDetail);

    private static async Task<Dictionary<(string SeriesId, string MNoDetail), BgmCueKey>> MigrateMusicsAsync(
        SqlConnection legacy, BgmSessionsRepository bgmSessionsRepo, BgmCuesRepository bgmCuesRepo,
        Dictionary<string, int> seriesMap, Report report, bool dryRun)
    {
        const string sql = @"
            SELECT series_id AS SeriesId, m_no_detail AS MNoDetail, rec_session AS RecSession,
                   m_no_class AS MNoClass, menu AS Menu, composer_name AS ComposerName,
                   arranger_name AS ArrangerName, memo AS Memo
            FROM musics ORDER BY series_id, m_no_detail;";

        var rows = (await legacy.QueryAsync<LegacyMusic>(sql)).ToList();
        var map = new Dictionary<(string, string), BgmCueKey>();
        var sessionNoMap = new Dictionary<(int SeriesId, string RecSession), byte>();

        foreach (var seriesGroup in rows.GroupBy(r => r.SeriesId))
        {
            int? newSeriesId = ResolveSeries(seriesGroup.Key, seriesMap);
            if (newSeriesId is null) continue;
            var distinctSessions = seriesGroup.Select(r => r.RecSession ?? "").Distinct(StringComparer.Ordinal)
                .OrderBy(s => s.Length == 0 ? 0 : 1).ThenBy(s => s, StringComparer.Ordinal).ToList();
            foreach (var recSessionName in distinctSessions)
            {
                string sessionName = recSessionName.Length == 0 ? "(未設定)" : recSessionName;
                byte no;
                if (!dryRun) no = await bgmSessionsRepo.InsertNextAsync(newSeriesId.Value, sessionName, null, "legacy-import");
                else { byte used = sessionNoMap.Where(kv => kv.Key.SeriesId == newSeriesId.Value).Select(kv => kv.Value).DefaultIfEmpty((byte)0).Max(); no = (byte)(used + 1); }
                sessionNoMap[(newSeriesId.Value, recSessionName)] = no;
                report.BgmSessionsCreated++;
            }
        }

        foreach (var row in rows)
        {
            int? newSeriesId = ResolveSeries(row.SeriesId, seriesMap);
            if (newSeriesId is null) continue;
            string recSession = row.RecSession ?? "";
            if (!sessionNoMap.TryGetValue((newSeriesId.Value, recSession), out byte sessionNo)) continue;
            var cue = new BgmCue
            {
                SeriesId = newSeriesId.Value,
                MNoDetail = row.MNoDetail,
                SessionNo = sessionNo,
                MNoClass = NullIfEmpty(row.MNoClass),
                MenuTitle = NullIfEmpty(row.Menu),
                ComposerName = NullIfEmpty(row.ComposerName),
                ArrangerName = NullIfEmpty(row.ArrangerName),
                Notes = NullIfEmpty(row.Memo),
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };
            if (!dryRun) await bgmCuesRepo.UpsertAsync(cue);
            report.BgmCuesCreated++;
            map[(row.SeriesId, row.MNoDetail)] = new BgmCueKey(newSeriesId.Value, row.MNoDetail);
        }
        return map;
    }

    // ========================================================================= 
    // ステップ 4: discs → products + discs 
    // ========================================================================= 

    /// <summary>
    /// 旧 discs を 新 products + 新 discs に展開する。
    /// <para>
    /// v1.1.1 仕様: 旧 discs.series_id はグループ内の全新 discs の series_id に同じ値でセットする。
    /// 新 products には series_id を載せない（列そのものが v1.1.1 で撤去された）。旧 DB は 1 商品 =
    /// 1 シリーズの前提だったため、この単純コピーで情報損失は発生しない。
    /// </para>
    /// </summary>
    private static async Task<Dictionary<int, string>> MigrateDiscsAsync(
        SqlConnection legacy, ProductsRepository productsRepo, DiscsRepository discsRepo,
        Dictionary<string, int> seriesMap, Dictionary<int, SeriesInfo> seriesInfoMap,
        Report report, bool dryRun)
    {
        const string sql = @"
            SELECT id AS OldDiscId, title AS Title, title_short AS TitleShort, series_id AS SeriesId,
                   class AS Class, release_date AS ReleaseDate, part_no AS PartNo, set_with AS SetWith,
                   price_ex_tax AS PriceExTax, manufacturer AS Manufacturer, distributer AS Distributer,
                   memo AS Memo, amazon_album AS AmazonAlbum, apple_album AS AppleAlbum
            FROM discs ORDER BY id;";

        var rows = (await legacy.QueryAsync<LegacyDisc>(sql)).ToList();
        var catalogMap = new Dictionary<int, string>();
        var groups = new Dictionary<int, List<LegacyDisc>>();

        foreach (var row in rows.Where(r => !r.SetWith.HasValue))
            groups[row.OldDiscId] = new List<LegacyDisc> { row };
        foreach (var row in rows.Where(r => r.SetWith.HasValue))
        {
            int parentId = row.SetWith!.Value;
            if (groups.TryGetValue(parentId, out var list)) list.Add(row);
            else { report.Warnings.Add($"discs: id={row.OldDiscId} の親欠損"); groups[row.OldDiscId] = new List<LegacyDisc> { row }; }
        }

        foreach (var group in groups.Values)
        {
            var discsInGroup = group.OrderBy(d => d.OldDiscId).ToList();
            var head = discsInGroup[0];

            // 商品メタ情報は親 disc から取る（商品単位の情報）
            // 旧 DB のシリーズ所属は商品単位で管理されていたため、グループ代表行の series_id を採用する。
            // 新 DB ではこの値は product ではなく各 disc 側の SeriesId に乗せ替える（下の for ループ内）。
            int? newSeriesId = ResolveSeries(head.SeriesId, seriesMap);
            SeriesInfo? sInfo = newSeriesId.HasValue && seriesInfoMap.TryGetValue(newSeriesId.Value, out var si) ? si : null;
            string productKindCode = MapDiscClassToProductKind(head.Class, sInfo, head.ReleaseDate);

            // v1.1.1: products には series_id を載せない（列自体が撤去された）。
            var product = new Product
            {
                ProductCatalogNo = head.PartNo,
                Title = head.Title ?? head.PartNo,
                TitleShort = NullIfEmpty(head.TitleShort),
                ProductKindCode = productKindCode,
                ReleaseDate = head.ReleaseDate ?? DateTime.MinValue,
                PriceExTax = head.PriceExTax,
                PriceIncTax = null,
                DiscCount = (byte)Math.Max(1, Math.Min(255, discsInGroup.Count)),
                Manufacturer = NullIfEmpty(head.Manufacturer),
                Distributor = NullIfEmpty(head.Distributer),
                AmazonAsin = NullIfEmpty(head.AmazonAlbum),
                AppleAlbumId = NullIfEmpty(head.AppleAlbum),
                Notes = NullIfEmpty(head.Memo),
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };
            if (!dryRun) await productsRepo.InsertAsync(product);
            report.ProductsCreated++;

            // v1.1.1: 各 disc の SeriesId にグループ代表の series_id を乗せる
            //         （旧 DB は 1 商品 = 1 シリーズが前提のため、グループ内一律同値で情報損失なし）。
            for (int i = 0; i < discsInGroup.Count; i++)
            {
                var src = discsInGroup[i];
                uint? discNoInSet = discsInGroup.Count == 1 ? (uint?)null : (uint)(i + 1);
                var disc = new Disc
                {
                    CatalogNo = src.PartNo,
                    ProductCatalogNo = head.PartNo,
                    Title = src.Title,
                    TitleShort = NullIfEmpty(src.TitleShort),
                    SeriesId = newSeriesId, // v1.1.1: シリーズ所属は disc 側の属性となった
                    DiscNoInSet = discNoInSet,
                    MediaFormat = "CD",
                    Notes = null,
                    CreatedBy = "legacy-import",
                    UpdatedBy = "legacy-import"
                };
                if (!dryRun) await discsRepo.UpsertAsync(disc);
                report.DiscsCreated++;
                catalogMap[src.OldDiscId] = src.PartNo;
            }
        }
        return catalogMap;
    }

    private static string MapDiscClassToProductKind(string? cls, SeriesInfo? series, DateTime? releaseDate) => cls switch
    {
        "Drm" => "DRAMA",
        "ImA" => "CHARA_ALBUM",
        "ImS" => "CHARA_SINGLE",
        "Liv" => "LIVE_ALBUM",
        "Nov" => "LIVE_NOVELTY",
        "OES" => IsLateThemeSingle(series, releaseDate) ? "THEME_SINGLE_LATE" : "THEME_SINGLE",
        "OST" => IsMovieSeries(series) ? "OST_MOVIE" : "OST",
        "Rdo" => "RADIO",
        "TUp" => "TIE_UP",
        "VoA" => "VOCAL_ALBUM",
        "VoB" => "VOCAL_BEST",
        _     => "OTHER",
    };

    private static bool IsLateThemeSingle(SeriesInfo? series, DateTime? releaseDate)
    {
        if (series is not { } s) return false;
        if (!string.Equals(s.KindCode, "TV", StringComparison.Ordinal)) return false;
        if (releaseDate is not { } rd) return false;
        var thresholdDate = new DateOnly(s.StartDate.Year, 6, 1);
        var releaseDateOnly = DateOnly.FromDateTime(rd);
        return releaseDateOnly >= thresholdDate;
    }

    private static bool IsMovieSeries(SeriesInfo? series)
    {
        if (series is not { } s) return false;
        return s.KindCode is "MOVIE" or "SPRING";
    }

    // ========================================================================= 
    // ステップ 5: tracks 
    // ========================================================================= 

    private static async Task MigrateTracksAsync(
        SqlConnection legacy, TracksRepository tracksRepo,
        Dictionary<int, string> discCatalogMap, Dictionary<int, int> songRecMap,
        Dictionary<(string, string), BgmCueKey> bgmCueMap, Report report, bool dryRun)
    {
        const string sql = @"
            SELECT disc_id AS OldDiscId, track_no AS TrackNo, track_class AS TrackClass,
                   song_id AS OldSongId, song_type AS SongType, song_size AS SongSize,
                   series_id AS SeriesId, m_no_detail AS MNoDetail,
                   track_title AS TrackTitle, memo AS Memo
            FROM tracks ORDER BY disc_id, track_no;";

        var rows = (await legacy.QueryAsync<LegacyTrack>(sql)).ToList();
        foreach (var row in rows)
        {
            if (!discCatalogMap.TryGetValue(row.OldDiscId, out var catalogNo)) continue;
            string contentKind = MapTrackClassToContentKind(row.TrackClass);
            int? songRecId = null;
            int? bgmSeriesId = null;
            string? bgmMNoDetail = null;

            if (contentKind == "SONG" && row.OldSongId.HasValue && songRecMap.TryGetValue(row.OldSongId.Value, out int srId))
                songRecId = srId;
            if (contentKind == "BGM" && !string.IsNullOrWhiteSpace(row.SeriesId) && !string.IsNullOrWhiteSpace(row.MNoDetail)
                && bgmCueMap.TryGetValue((row.SeriesId, row.MNoDetail), out var k))
                (bgmSeriesId, bgmMNoDetail) = (k.SeriesId, k.MNoDetail);

            string? sizeVariant = null;
            string? partVariant = null;
            if (contentKind == "SONG")
            {
                partVariant = MapSongTypeToPartVariant(row.SongType);
                if (!string.IsNullOrWhiteSpace(row.SongSize)) sizeVariant = MapSongSizeToSizeVariant(row.SongSize);
            }
            if (contentKind == "SONG" && songRecId is null) { contentKind = "OTHER"; sizeVariant = null; partVariant = null; }
            if (contentKind == "BGM" && bgmSeriesId is null) contentKind = "OTHER";

            var t = new Track
            {
                CatalogNo = catalogNo,
                TrackNo = (byte)row.TrackNo,
                SubOrder = 0,
                ContentKindCode = contentKind,
                SongRecordingId = songRecId,
                SongSizeVariantCode = sizeVariant,
                SongPartVariantCode = partVariant,
                BgmSeriesId = bgmSeriesId,
                BgmMNoDetail = bgmMNoDetail,
                TrackTitleOverride = NullIfEmpty(row.TrackTitle),
                Notes = NullIfEmpty(row.Memo),
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };
            if (!dryRun)
            {
                try { await tracksRepo.UpsertAsync(t); }
                catch (Exception ex) { report.Warnings.Add($"tracks upsert 失敗: {ex.Message}"); continue; }
            }
            report.TracksCreated++;
        }
    }

    private static string MapTrackClassToContentKind(string? cls) => cls switch
    {
        "BGM"   => "BGM",
        "Drama" => "DRAMA",
        "Radio" => "RADIO",
        "Vocal" => "SONG",
        "Live"  => "LIVE",
        "TieUp" => "TIE_UP",
        "Other" => "OTHER",
        _       => "OTHER",
    };

    private static string MapSongTypeToPartVariant(string? songType)
    {
        if (string.IsNullOrWhiteSpace(songType)) return "VOCAL";
        return songType switch
        {
            "Inst"            => "INST",
            "Inst+Str"        => "INST_STR",
            "Inst+Guide"      => "INST_GUIDE",
            "Inst+Cho"        => "INST_CHO",
            "Inst+Cho+Guide"  => "INST_CHO_GUIDE",
            "Inst+PartVo"     => "INST_PART_VO",
            _                 => "OTHER",
        };
    }

    private static string MapSongSizeToSizeVariant(string songSize) => songSize switch
    {
        "Full"            => "FULL",
        "TV"              => "TV",
        "TV1番"           => "TV_V1",
        "TV2番"           => "TV_V2",
        "TV Type.Ⅰ"      => "TV_TYPE_I",
        "TV Type.Ⅱ"      => "TV_TYPE_II",
        "TV Type.Ⅲ"      => "TV_TYPE_III",
        "TV Type.Ⅳ"      => "TV_TYPE_IV",
        "TV Type.Ⅴ"      => "TV_TYPE_V",
        "Short"           => "SHORT",
        "Movie"           => "MOVIE",
        "LIVE Edit Ver."  => "LIVE_EDIT",
        "Mov.1"           => "MOV_1",
        "Mov.3"           => "MOV_3",
        _                 => "OTHER",
    };

    // ========================================================================= 
    // ヘルパー 
    // ========================================================================= 

    private static int? ResolveSeries(string? legacySeriesId, Dictionary<string, int> map)
    {
        if (string.IsNullOrWhiteSpace(legacySeriesId)) return null;
        return map.TryGetValue(legacySeriesId, out int id) ? id : null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ========================================================================= 
    // 旧テーブル DTO 
    // ========================================================================= 

    private sealed class LegacySeriesRow { public string Id { get; set; } = ""; public string? Title { get; set; } }
    private sealed class LegacyDisc
    {
        public int OldDiscId { get; set; }
        public string? Title { get; set; }
        public string? TitleShort { get; set; }
        public string? SeriesId { get; set; }
        public string? Class { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string PartNo { get; set; } = "";
        public int? SetWith { get; set; }
        public int? PriceExTax { get; set; }
        public string? Manufacturer { get; set; }
        public string? Distributer { get; set; }
        public string? Memo { get; set; }
        public string? AmazonAlbum { get; set; }
        public string? AppleAlbum { get; set; }
    }
    private sealed class LegacySong
    {
        public int OldSongId { get; set; }
        public int MusicClass { get; set; }
        public int ArrangeClass { get; set; }
        public string? SeriesId { get; set; }
        public string? Title { get; set; }
        public string? TitleKana { get; set; }
        public string? SingerName { get; set; }
        public string? SingerNameKana { get; set; }
        public string? LyricistName { get; set; }
        public string? LyricistNameKana { get; set; }
        public string? ComposerName { get; set; }
        public string? ComposerNameKana { get; set; }
        public string? ArrangerName { get; set; }
        public string? ArrangerNameKana { get; set; }
        public string? Memo { get; set; }
    }
    private sealed class LegacyMusic
    {
        public string SeriesId { get; set; } = "";
        public string MNoDetail { get; set; } = "";
        public string? RecSession { get; set; }
        public string? MNoClass { get; set; }
        public string? Menu { get; set; }
        public string? ComposerName { get; set; }
        public string? ArrangerName { get; set; }
        public string? Memo { get; set; }
    }
    private sealed class LegacyTrack
    {
        public int OldDiscId { get; set; }
        public int TrackNo { get; set; }
        public string? TrackClass { get; set; }
        public int? OldSongId { get; set; }
        public string? SongType { get; set; }
        public string? SongSize { get; set; }
        public string? SeriesId { get; set; }
        public string? MNoDetail { get; set; }
        public string? TrackTitle { get; set; }
        public string? Memo { get; set; }
    }
    private sealed class Report
    {
        public int SeriesMapped { get; set; }
        public int SeriesUnmapped { get; set; }
        public int ProductsCreated { get; set; }
        public int DiscsCreated { get; set; }
        public int TracksCreated { get; set; }
        public int SongsCreated { get; set; }
        public int SongRecordingsCreated { get; set; }
        public int BgmSessionsCreated { get; set; }
        public int BgmCuesCreated { get; set; }
        public List<string> Warnings { get; } = new();
    }
}