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
/// </list>
/// <remarks>
/// 運用:
///   1. db/migrations/v1.1.0_add_music_catalog.sql を流してスキーマ作成
///   2. App.config に LegacyServer / TargetMySql を設定
///   3. dotnet run -- --dry-run で件数確認
///   4. --dry-run なしで実行
///
/// オプション:
///   --dry-run
///       DB 書き込みを行わず、件数サマリーだけ出す。
///   --series-map-override=path.csv
///       自動マッピングで拾えない旧 series.id を手動で上書きする。
///       形式: legacy_id,new_series_id
///             20080205,9
/// </remarks>
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
            Console.Error.WriteLine("App.config.sample を参考に作成できます。");
            return 2;
        }

        Console.WriteLine("=== PrecureDataStars LegacyImport ===");
        Console.WriteLine($"dry-run     : {dryRun}");
        Console.WriteLine($"legacy      : {RedactPassword(legacyCs)}");
        Console.WriteLine($"target mysql: {RedactPassword(targetCs)}");
        if (seriesOverridePath is not null)
            Console.WriteLine($"series override : {seriesOverridePath}");
        Console.WriteLine();

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

            // ステップ 1: series マッピング構築（旧 YYYYMMDD → 新 series.start_date で突合）
            var seriesMap = await BuildSeriesMapAsync(legacy, seriesRepo, seriesOverridePath, report);

            // ステップ 1.5: 新 series_id → (kind_code, start_date) の補助辞書
            // 後期主題歌シングル / 映画 OST の判定でディスク作成時に使う
            var seriesInfoMap = await BuildSeriesInfoMapAsync(seriesRepo);

            // ステップ 2: songs の二階層展開（旧 song_id → 新 song_recording_id マップを返す）
            var songRecMap = await MigrateSongsAsync(legacy, songsRepo, songRecRepo, seriesMap, report, dryRun);

            // ステップ 3: musics → bgm_cues（1 テーブル統合モデル） + bgm_sessions
            //   辞書の値は「その cue を一意に指す (series_id, m_no_detail) 2 列タプル」
            var bgmCueMap = await MigrateMusicsAsync(legacy, bgmSessionsRepo, bgmCuesRepo, seriesMap, report, dryRun);

            // ステップ 4: discs → products + discs（set_with でグルーピング）
            var discCatalogMap = await MigrateDiscsAsync(legacy, productsRepo, discsRepo, seriesMap, seriesInfoMap, report, dryRun);

            // ステップ 5: tracks
            await MigrateTracksAsync(legacy, tracksRepo, discCatalogMap, songRecMap, bgmCueMap, report, dryRun);

            // ステップ 6: サマリー
            Console.WriteLine();
            Console.WriteLine("=== 移行サマリー ===");
            Console.WriteLine($"series mapped      : {report.SeriesMapped}");
            Console.WriteLine($"series unmapped    : {report.SeriesUnmapped}");
            Console.WriteLine($"products created   : {report.ProductsCreated}");
            Console.WriteLine($"discs created      : {report.DiscsCreated}");
            Console.WriteLine($"tracks created     : {report.TracksCreated}");
            Console.WriteLine($"songs created      : {report.SongsCreated}");
            Console.WriteLine($"song_recordings    : {report.SongRecordingsCreated}");
            Console.WriteLine($"bgm_sessions       : {report.BgmSessionsCreated}");
            Console.WriteLine($"bgm_cues created   : {report.BgmCuesCreated}");
            Console.WriteLine($"warnings           : {report.Warnings.Count}");
            foreach (var w in report.Warnings.Take(30))
                Console.WriteLine($"  - {w}");
            if (report.Warnings.Count > 30)
                Console.WriteLine($"  (他 {report.Warnings.Count - 30} 件省略)");

            if (dryRun)
                Console.WriteLine("\n[dry-run] DB 書き込みは行われていません。");

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

    /// <summary>
    /// 旧 series.id (YYYYMMDD 8 桁文字列) と 新 series.start_date の突合で自動マッピングを構築する。
    /// ハードコードされたエイリアス（スピンオフ → 本体）と、CSV 上書きも適用する。
    /// </summary>
    private static async Task<Dictionary<string, int>> BuildSeriesMapAsync(
        SqlConnection legacy,
        SeriesRepository seriesRepo,
        string? overridePath,
        Report report)
    {
        var newSeriesList = await seriesRepo.GetAllAsync();
        var byStartDate = newSeriesList
            .GroupBy(s => s.StartDate)
            .ToDictionary(g => g.Key, g => g.First().SeriesId);

        var legacyRows = (await legacy.QueryAsync<LegacySeriesRow>(
            "SELECT id AS Id, title AS Title FROM series")).ToList();

        var map = new Dictionary<string, int>(StringComparer.Ordinal);

        // 固定エイリアス: 旧 20080205 (Yes!5GoGo! スピンオフ CLUB ココ&ナッツ) → 20080203 (本体)
        var legacyAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["20080205"] = "20080203",
        };

        foreach (var row in legacyRows)
        {
            string resolveKey = legacyAliases.TryGetValue(row.Id, out var aliasedTo) ? aliasedTo : row.Id;

            if (!DateOnly.TryParseExact(resolveKey, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var legacyDate))
            {
                report.Warnings.Add($"series id='{row.Id}' が YYYYMMDD として解釈できません");
                report.SeriesUnmapped++;
                continue;
            }

            if (byStartDate.TryGetValue(legacyDate, out int newId))
            {
                map[row.Id] = newId;
                report.SeriesMapped++;
            }
            else
            {
                report.Warnings.Add($"series 未マッピング: 旧 id={row.Id} ({row.Title}), 解決日={legacyDate:yyyy-MM-dd}");
                report.SeriesUnmapped++;
            }
        }

        // CSV 上書き（任意、ユーザー提供）
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            int overrideCount = 0;
            foreach (var line in File.ReadAllLines(overridePath))
            {
                var s = line.Trim();
                if (string.IsNullOrEmpty(s) || s.StartsWith("#")) continue;
                var parts = s.Split(',', 2);
                if (parts.Length != 2) continue;
                if (int.TryParse(parts[1].Trim(), out int newId))
                {
                    map[parts[0].Trim()] = newId;
                    overrideCount++;
                }
            }
            Console.WriteLine($"series map override: {overrideCount} 件適用");
        }

        Console.WriteLine($"series map built: {map.Count} 件 (unmapped {report.SeriesUnmapped} 件)");
        return map;
    }

    /// <summary>
    /// 新 series_id → (kind_code, start_date) を引くための補助辞書を作る。
    /// <para>
    /// 商品種別の細分判定（後期主題歌シングル／映画 OST）で、
    /// 所属シリーズの種別と放送開始日を参照するために使う。
    /// </para>
    /// </summary>
    private static async Task<Dictionary<int, SeriesInfo>> BuildSeriesInfoMapAsync(SeriesRepository seriesRepo)
    {
        var list = await seriesRepo.GetAllAsync();
        return list.ToDictionary(
            s => s.SeriesId,
            s => new SeriesInfo(s.KindCode ?? "", s.StartDate));
    }

    /// <summary>新 series の種別判定用の最小情報。</summary>
    private readonly record struct SeriesInfo(string KindCode, DateOnly StartDate);

    // =========================================================================
    // ステップ 2: songs 二階層展開（派生 title を variant_label に保存）
    // =========================================================================

    /// <summary>
    /// 旧 songs を新 songs + song_recordings に展開する。
    /// <para>
    /// 旧 songs.music_class は「メロディが同じ曲の中で最も若い id」を指すため、
    /// id == music_class の行を新 songs として作り、派生行は新 song_recordings として
    /// メロディ親の songs.song_id 配下に紐付ける。
    /// </para>
    /// <para>
    /// 派生行の title が親 title と異なる場合は、その差分を残すため variant_label に派生 title を保存する。
    /// これにより「DANZEN! ふたりはプリキュア」（親）と「DANZEN! ふたりはプリキュア Ver. MaxHeart」（派生）を
    /// GUI 上で区別できる。
    /// </para>
    /// </summary>
    /// <returns>旧 songs.id → 新 song_recording_id の辞書（全 songs 行）。</returns>
    /// <summary>
    /// 旧 songs テーブルを新 songs / song_recordings に展開する。
    /// <para>
    /// 新設計では songs は「メロディ + アレンジ」単位（＝旧 arrange_class 単位）となった。
    /// 旧 songs.id == arrange_class な行を新 songs の 1 レコードにマップし、
    /// 旧 songs.id != arrange_class な行（歌手違い）は同じ arrange_class 新 songs 配下の
    /// song_recordings として展開する。arrange_class 親行自身も「標準歌手」として 1 件 recording を作る。
    /// </para>
    /// <para>
    /// songs.title は旧 title をそのまま使う（派生アレンジの独自タイトルはこれで表現される）。
    /// songs.arranger_name は旧 songs.arranger_name を移行する。
    /// song_recordings.variant_label は旧 title と新 songs.title が異なる場合のみ保存（歌唱者バリエーションの補助ラベル）。
    /// </para>
    /// <para>
    /// 戻り値: 旧 songs.id → 新 song_recordings.song_recording_id のマップ。
    /// tracks 移行時に旧 song_id から新 song_recording_id を引き当てるために使う。
    /// </para>
    /// </summary>
    private static async Task<Dictionary<int, int>> MigrateSongsAsync(
        SqlConnection legacy,
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        Dictionary<string, int> seriesMap,
        Report report,
        bool dryRun)
    {
        const string sql = @"
            SELECT
              id                  AS OldSongId,
              music_class         AS MusicClass,
              arrange_class       AS ArrangeClass,
              series_id           AS SeriesId,
              title               AS Title,
              title_kana          AS TitleKana,
              singer_name         AS SingerName,
              singer_name_kana    AS SingerNameKana,
              lyricist_name       AS LyricistName,
              lyricist_name_kana  AS LyricistNameKana,
              composer_name       AS ComposerName,
              composer_name_kana  AS ComposerNameKana,
              arranger_name       AS ArrangerName,
              arranger_name_kana  AS ArrangerNameKana,
              memo                AS Memo
            FROM songs
            ORDER BY id;";

        var rows = (await legacy.QueryAsync<LegacySong>(sql)).ToList();

        // 旧 songs.id (arrange_class == id のものだけ) → 新 songs.song_id のマップ
        var arrangeClassToNewSongId = new Dictionary<int, int>();
        // 旧 arrange_class → 新 songs.title のマップ（派生歌手行の variant_label 判定に使う）
        var arrangeTitle = new Dictionary<int, string?>();
        // 旧 songs.id → 新 song_recordings.song_recording_id のマップ（tracks から引くのに使う）
        var oldToNewSongRecId = new Dictionary<int, int>();

        // --- 先にアレンジ親 (id == arrange_class) を新 songs として登録 ---
        // 同じ music_class + arrange_class のレコード群は 1 つのアレンジ版として集約される。
        foreach (var row in rows.Where(r => r.OldSongId == r.ArrangeClass))
        {
            int? newSeriesId = ResolveSeries(row.SeriesId, seriesMap);

            var song = new Song
            {
                Title = row.Title ?? "",
                TitleKana = NullIfEmpty(row.TitleKana),
                SeriesId = newSeriesId,
                OriginalLyricistName = NullIfEmpty(row.LyricistName),
                OriginalLyricistNameKana = NullIfEmpty(row.LyricistNameKana),
                OriginalComposerName = NullIfEmpty(row.ComposerName),
                OriginalComposerNameKana = NullIfEmpty(row.ComposerNameKana),
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

        // --- 全 songs について song_recordings を 1 件ずつ作成 ---
        // 親とする新 song_id は「この行の arrange_class」を使って引く（music_class ではない）。
        foreach (var row in rows)
        {
            if (!arrangeClassToNewSongId.TryGetValue(row.ArrangeClass, out int parentNewSongId))
            {
                report.Warnings.Add($"songs.id={row.OldSongId}: arrange_class={row.ArrangeClass} が新 songs として見つかりません (スキップ)");
                continue;
            }

            // variant_label の解決:
            //  - アレンジ親 (id == arrange_class) 行そのもの → 標準歌手録音。variant_label は NULL
            //  - 派生歌手行の title がアレンジ親 title と異なる → 派生 title を variant_label に保存
            //    （例: 親 "DANZEN! ふたりはプリキュア Ver. MaxHeart" に対して
            //     派生 "DANZEN! ふたりはプリキュア Ver. MaxHeart (五條真由美)" など）
            //  - 派生歌手行の title がアレンジ親と同じ → NULL（歌手違いは singer_name で識別）
            string? variantLabel = null;
            bool isArrangeParent = row.OldSongId == row.ArrangeClass;
            if (!isArrangeParent)
            {
                arrangeTitle.TryGetValue(row.ArrangeClass, out string? parentT);
                if (!string.IsNullOrWhiteSpace(row.Title) && !string.Equals(row.Title, parentT, StringComparison.Ordinal))
                {
                    variantLabel = row.Title;
                }
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
    // ステップ 3: musics → bgm_sessions + bgm_cues（ターン C の 1 テーブル統合モデル）
    // =========================================================================

    /// <summary>
    /// BGM cue を一意に指す複合キー (series_id, m_no_detail)。
    /// tracks の BGM 参照解決に使う（ターン C 以降、rec_session は PK から外れセッション属性になったため 2 列キーで足りる）。
    /// </summary>
    private readonly record struct BgmCueKey(int SeriesId, string MNoDetail);

    /// <summary>
    /// <summary>
    /// 旧 musics を新 bgm_cues（1 テーブル統合モデル）+ bgm_sessions に展開する。
    /// <para>
    /// v1.1.0 ターン C で劇伴モデルを再設計した。旧 musics は 1 行 = 1 音源の素直な構造だったので、
    /// 新 bgm_cues にほぼ 1 対 1 で移行する。録音セッションだけは別マスタ bgm_sessions 側に切り出す:
    /// シリーズごとに distinct な rec_session 文字列を 0（=""）から順に採番し、
    /// bgm_sessions に初期投入した上で bgm_cues.session_no に番号を書き込む。
    /// </para>
    /// <para>
    /// 旧 m_no_detail / m_no_class はそのまま新 bgm_cues の同名列に保存。
    /// PK は (series_id, m_no_detail)。
    /// </para>
    /// </summary>
    /// <returns>
    /// 旧 (series_id, m_no_detail) → 新 BGM cue の複合キー の辞書。
    /// tracks 移行時に tracks.bgm_series_id / bgm_m_no_detail に入れるために使う。
    /// </returns>
    private static async Task<Dictionary<(string SeriesId, string MNoDetail), BgmCueKey>> MigrateMusicsAsync(
        SqlConnection legacy,
        BgmSessionsRepository bgmSessionsRepo,
        BgmCuesRepository bgmCuesRepo,
        Dictionary<string, int> seriesMap,
        Report report,
        bool dryRun)
    {
        const string sql = @"
            SELECT
              series_id        AS SeriesId,
              m_no_detail      AS MNoDetail,
              rec_session      AS RecSession,
              m_no_class       AS MNoClass,
              menu             AS Menu,
              composer_name    AS ComposerName,
              arranger_name    AS ArrangerName,
              memo             AS Memo
            FROM musics
            ORDER BY series_id, m_no_detail;";

        var rows = (await legacy.QueryAsync<LegacyMusic>(sql)).ToList();
        var map = new Dictionary<(string, string), BgmCueKey>();

        // v1.1.1 以降の採番 A 案: シリーズごとに distinct な rec_session 文字列を拾い、1 から順に採番する。
        // シリーズ内にセッションが 1 つしか無くても session_no=1 を付ける。
        // rec_session が空文字 "" の行は「名前なしセッション」として 1 件採番（session_name="(未設定)"）する。
        // 旧設計の session_no=0「未設定既定」は廃止。
        // sessionNoMap[(newSeriesId, rec_session_string)] = session_no
        var sessionNoMap = new Dictionary<(int SeriesId, string RecSession), byte>();

        foreach (var seriesGroup in rows.GroupBy(r => r.SeriesId))
        {
            int? newSeriesId = ResolveSeries(seriesGroup.Key, seriesMap);
            if (newSeriesId is null) continue; // 後段のログで拾う

            // このシリーズで出現する rec_session を distinct で拾う（空文字も 1 つの値として含める）
            var distinctSessions = seriesGroup
                .Select(r => r.RecSession ?? "")
                .Distinct(StringComparer.Ordinal)
                // 空文字を先頭に、以降は文字列昇順で安定採番する
                .OrderBy(s => s.Length == 0 ? 0 : 1)
                .ThenBy(s => s, StringComparer.Ordinal)
                .ToList();

            foreach (var recSessionName in distinctSessions)
            {
                // 空文字の場合は「(未設定)」を session_name として保存する。
                // 後から BgmCuesEditor / MastersEditor で実名に書き換える運用。
                string sessionName = recSessionName.Length == 0 ? "(未設定)" : recSessionName;

                byte no;
                if (!dryRun)
                {
                    no = await bgmSessionsRepo.InsertNextAsync(newSeriesId.Value, sessionName, null, "legacy-import");
                }
                else
                {
                    // dry-run では採番をこちら側で模擬（シリーズ内の次の空き番）
                    byte used = sessionNoMap.Where(kv => kv.Key.SeriesId == newSeriesId.Value).Select(kv => kv.Value).DefaultIfEmpty((byte)0).Max();
                    no = (byte)(used + 1);
                }
                sessionNoMap[(newSeriesId.Value, recSessionName)] = no;
                report.BgmSessionsCreated++;
            }
        }

        // 各 musics 行を新 bgm_cues に 1 件ずつ UPSERT する
        foreach (var row in rows)
        {
            int? newSeriesId = ResolveSeries(row.SeriesId, seriesMap);
            if (newSeriesId is null)
            {
                report.Warnings.Add($"musics: series_id={row.SeriesId} が未マッピングのためスキップ ({row.MNoDetail})");
                continue;
            }

            string recSession = row.RecSession ?? "";
            if (!sessionNoMap.TryGetValue((newSeriesId.Value, recSession), out byte sessionNo))
            {
                // ここに来るのは理論上シリーズマッピング失敗のみ
                report.Warnings.Add($"musics: session マッピング失敗 (series={row.SeriesId}, rec_session='{recSession}')");
                continue;
            }

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

            // tracks 移行で (series_id, m_no_detail) をキーに引けるようマップを返す
            map[(row.SeriesId, row.MNoDetail)] = new BgmCueKey(newSeriesId.Value, row.MNoDetail);
        }

        return map;
    }

    // =========================================================================
    // ステップ 4: discs → products + discs（set_with グルーピング）
    // =========================================================================

    /// <summary>
    /// 旧 discs を 新 products + 新 discs に展開する。
    /// <para>
    /// set_with の設計:
    /// 親行は set_with IS NULL。子行は set_with に親の旧 id を格納している。
    /// 同じ set_with 値を共有する行を「1 商品 N 枚組」として集約する。
    /// </para>
    /// <para>
    /// 代表品番 = 親 disc の旧 part_no。子 disc は disc_no_in_set=2,3... (旧 id 昇順) で同商品に紐付ける。
    /// 孤児子（set_with が指す親が欠損）は単独扱いにフォールバック。
    /// </para>
    /// </summary>
    /// <returns>旧 discs.id → 新 catalog_no の辞書（tracks 移行用）。</returns>
    private static async Task<Dictionary<int, string>> MigrateDiscsAsync(
        SqlConnection legacy,
        ProductsRepository productsRepo,
        DiscsRepository discsRepo,
        Dictionary<string, int> seriesMap,
        Dictionary<int, SeriesInfo> seriesInfoMap,
        Report report,
        bool dryRun)
    {
        const string sql = @"
            SELECT
              id            AS OldDiscId,
              title         AS Title,
              title_short   AS TitleShort,
              series_id     AS SeriesId,
              class         AS Class,
              release_date  AS ReleaseDate,
              part_no       AS PartNo,
              set_with      AS SetWith,
              price_ex_tax  AS PriceExTax,
              manufacturer  AS Manufacturer,
              distributer   AS Distributer,
              memo          AS Memo,
              amazon_album  AS AmazonAlbum,
              apple_album   AS AppleAlbum
            FROM discs
            ORDER BY id;";

        var rows = (await legacy.QueryAsync<LegacyDisc>(sql)).ToList();
        var byOldId = rows.ToDictionary(r => r.OldDiscId);
        var catalogMap = new Dictionary<int, string>();

        // --- グルーピング ---
        // 親行 (set_with IS NULL) は自分自身を親とするグループを作る。
        // 子行 (set_with NOT NULL) は set_with が指す親のグループに加わる。
        // 親が見つからない子は単独商品扱いにフォールバック（警告を出す）。
        var groups = new Dictionary<int, List<LegacyDisc>>(); // 親 id → [親, 子1, 子2 ...]（旧 id 昇順）

        // 最初に親行を全部登録
        foreach (var row in rows.Where(r => !r.SetWith.HasValue))
        {
            groups[row.OldDiscId] = new List<LegacyDisc> { row };
        }
        // 次に子行を親に追加
        foreach (var row in rows.Where(r => r.SetWith.HasValue))
        {
            int parentId = row.SetWith!.Value;
            if (groups.TryGetValue(parentId, out var list))
            {
                list.Add(row);
            }
            else
            {
                // 親が欠損 → 警告の上、単独グループにフォールバック
                report.Warnings.Add($"discs: id={row.OldDiscId} の set_with={parentId} に対応する親が見つからず、単独商品として扱います");
                groups[row.OldDiscId] = new List<LegacyDisc> { row };
            }
        }

        // --- グループごとに 1 product + N disc を作る ---
        foreach (var group in groups.Values)
        {
            // 各グループ内を旧 id 昇順で並べる（親が先頭、子が後続）
            var discsInGroup = group.OrderBy(d => d.OldDiscId).ToList();
            var head = discsInGroup[0];

            // 商品メタ情報は親 disc から取る（商品単位の情報）
            int? newSeriesId = ResolveSeries(head.SeriesId, seriesMap);

            // 所属シリーズの (kind_code, start_date) を引く。オールスターズ等でマップに無ければ null。
            SeriesInfo? sInfo = newSeriesId.HasValue && seriesInfoMap.TryGetValue(newSeriesId.Value, out var si)
                ? si
                : null;

            string productKindCode = MapDiscClassToProductKind(head.Class, sInfo, head.ReleaseDate);

            // 備考は旧 memo のみを保存する。旧 DB 再引き当て用のメタ情報（product_head_disc_id 等）は
            // 新 DB 側の product_catalog_no / disc_no_in_set で完全に復元できるため保存しない。
            string? productNotes = NullIfEmpty(head.Memo);

            var product = new Product
            {
                ProductCatalogNo = head.PartNo, // 代表品番 = 親ディスクの品番
                Title = head.Title ?? head.PartNo,
                TitleShort = NullIfEmpty(head.TitleShort),
                ProductKindCode = productKindCode,
                SeriesId = newSeriesId,
                ReleaseDate = head.ReleaseDate ?? DateTime.MinValue,
                PriceExTax = head.PriceExTax,
                PriceIncTax = null,
                DiscCount = (byte)Math.Max(1, Math.Min(255, discsInGroup.Count)),
                Manufacturer = NullIfEmpty(head.Manufacturer),
                Distributor = NullIfEmpty(head.Distributer), // typo: 旧列名 "distributer"
                AmazonAsin = NullIfEmpty(head.AmazonAlbum),
                AppleAlbumId = NullIfEmpty(head.AppleAlbum),
                Notes = productNotes,
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };

            if (!dryRun) await productsRepo.InsertAsync(product);
            report.ProductsCreated++;

            // グループ内の各 disc を新 discs に展開
            // 1 枚物 (グループサイズ 1) は disc_no_in_set=NULL、複数枚組は 1,2,3...
            for (int i = 0; i < discsInGroup.Count; i++)
            {
                var src = discsInGroup[i];
                uint? discNoInSet = discsInGroup.Count == 1 ? (uint?)null : (uint)(i + 1);

                var disc = new Disc
                {
                    CatalogNo = src.PartNo,
                    ProductCatalogNo = head.PartNo, // 代表品番は全ディスクで共通
                    Title = src.Title,
                    TitleShort = NullIfEmpty(src.TitleShort),
                    DiscNoInSet = discNoInSet,
                    MediaFormat = "CD",
                    // 備考は旧 memo のみを保存する（旧 disc_id / set_with は disc_no_in_set と product_catalog_no で復元可）
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

    /// <summary>
    /// 旧 discs.class 3 文字コードを新 product_kinds コードにマップする。
    /// <para>
    /// OES（主題歌シングル）と OST（サウンドトラック）は所属シリーズと発売日に応じてさらに細分する:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     OES × 新 series.kind_code=TV × 発売日 >= 放送開始年の 6/1
    ///     → THEME_SINGLE_LATE（後期主題歌シングル）
    ///   </item>
    ///   <item>それ以外の OES → THEME_SINGLE（主題歌シングル）</item>
    ///   <item>OST × 新 series.kind_code ∈ {MOVIE, SPRING} → OST_MOVIE（映画オリジナル・サウンドトラック）</item>
    ///   <item>それ以外の OST → OST（オリジナル・サウンドトラック）</item>
    /// </list>
    /// </summary>
    private static string MapDiscClassToProductKind(string? cls, SeriesInfo? series, DateTime? releaseDate) => cls switch
    {
        "Drm" => "DRAMA",          // ドラマ
        "ImA" => "CHARA_ALBUM",    // キャラクターアルバム
        "ImS" => "CHARA_SINGLE",   // キャラクターシングル
        "Liv" => "LIVE_ALBUM",     // ライブアルバム
        "Nov" => "LIVE_NOVELTY",   // ライブ特典スペシャルCD
        "OES" => IsLateThemeSingle(series, releaseDate) ? "THEME_SINGLE_LATE" : "THEME_SINGLE",
        "OST" => IsMovieSeries(series) ? "OST_MOVIE" : "OST",
        "Rdo" => "RADIO",          // ラジオ
        "TUp" => "TIE_UP",         // タイアップアーティスト
        "VoA" => "VOCAL_ALBUM",    // ボーカルアルバム
        "VoB" => "VOCAL_BEST",     // ボーカルベスト
        _     => "OTHER",
    };

    /// <summary>
    /// 後期主題歌シングルの判定。
    /// 新 series.kind_code='TV' で、発売日が放送開始年の 6/1 以降であれば true。
    /// </summary>
    private static bool IsLateThemeSingle(SeriesInfo? series, DateTime? releaseDate)
    {
        if (series is not { } s) return false;
        if (!string.Equals(s.KindCode, "TV", StringComparison.Ordinal)) return false;
        if (releaseDate is not { } rd) return false;
        // Series.StartDate は DateOnly、releaseDate は DateTime。DateOnly 基準で比較する。
        var thresholdDate = new DateOnly(s.StartDate.Year, 6, 1);
        var releaseDateOnly = DateOnly.FromDateTime(rd);
        return releaseDateOnly >= thresholdDate;
    }

    /// <summary>
    /// 映画オリジナル・サウンドトラックの判定。
    /// 新 series.kind_code が MOVIE（秋映画）または SPRING（春映画）のいずれかなら true。
    /// MOVIE_SHORT（秋映画併映）は対象外。
    /// </summary>
    private static bool IsMovieSeries(SeriesInfo? series)
    {
        if (series is not { } s) return false;
        return s.KindCode is "MOVIE" or "SPRING";
    }

    // =========================================================================
    // ステップ 5: tracks（track_title_override 常時保存）
    // =========================================================================

    /// <summary>
    /// 旧 tracks を新 tracks に移行する。
    /// <para>
    /// track_class → content_kind_code、song_id → song_recording_id、
    /// series_id + m_no_detail → bgm_series_id + bgm_m_no_detail
    /// でそれぞれ解決する（ターン C の 1 テーブル統合により BGM 参照は 2 列）。
    /// </para>
    /// <para>
    /// 旧 tracks.track_title は、SONG/BGM/その他を問わず、非空なら
    /// 新 tracks.track_title_override に常に保存する（収録盤固有の表記を尊重）。
    /// </para>
    /// </summary>
    private static async Task MigrateTracksAsync(
        SqlConnection legacy,
        TracksRepository tracksRepo,
        Dictionary<int, string> discCatalogMap,
        Dictionary<int, int> songRecMap,
        Dictionary<(string, string), BgmCueKey> bgmCueMap,
        Report report,
        bool dryRun)
    {
        const string sql = @"
            SELECT
              disc_id       AS OldDiscId,
              track_no      AS TrackNo,
              track_class   AS TrackClass,
              song_id       AS OldSongId,
              song_type     AS SongType,
              song_size     AS SongSize,
              series_id     AS SeriesId,
              m_no_detail   AS MNoDetail,
              track_title   AS TrackTitle,
              memo          AS Memo
            FROM tracks
            ORDER BY disc_id, track_no;";

        var rows = (await legacy.QueryAsync<LegacyTrack>(sql)).ToList();

        foreach (var row in rows)
        {
            if (!discCatalogMap.TryGetValue(row.OldDiscId, out var catalogNo))
            {
                report.Warnings.Add($"tracks: disc_id={row.OldDiscId} の catalog_no が見つからず、track #{row.TrackNo} をスキップ");
                continue;
            }

            string contentKind = MapTrackClassToContentKind(row.TrackClass);

            int? songRecId = null;
            int? bgmSeriesId = null;
            string? bgmMNoDetail = null;

            // SONG 系: song_id → song_recording_id を引く
            if (contentKind == "SONG" && row.OldSongId.HasValue)
            {
                if (songRecMap.TryGetValue(row.OldSongId.Value, out int srId))
                {
                    songRecId = srId;
                }
                else
                {
                    report.Warnings.Add($"tracks: disc_id={row.OldDiscId} #{row.TrackNo} の song_id={row.OldSongId} が未マップ");
                }
            }

            // BGM: (series_id, m_no_detail) → 新 bgm_cues の 2 列キーを引く
            // ターン C の再設計で rec_session は bgm_cues の属性となったため、
            // tracks 側は (series_id, m_no_detail) のみで cue を一意に特定できる。
            if (contentKind == "BGM" && !string.IsNullOrWhiteSpace(row.SeriesId)
                && !string.IsNullOrWhiteSpace(row.MNoDetail))
            {
                if (bgmCueMap.TryGetValue((row.SeriesId, row.MNoDetail), out var k))
                {
                    (bgmSeriesId, bgmMNoDetail) = (k.SeriesId, k.MNoDetail);
                }
                else
                {
                    report.Warnings.Add($"tracks: disc_id={row.OldDiscId} #{row.TrackNo} の BGM (series={row.SeriesId}, m_no={row.MNoDetail}) が未マップ");
                }
            }

            // track_title_override: 旧 track_title があれば常に保持（SONG/BGM でも）
            // これにより同じ音源でもディスクごとに異なる表記を失わない。
            string? titleOverride = NullIfEmpty(row.TrackTitle);

            // 備考は旧 memo のみを新 notes に保存する。
            // 旧 song_type / song_size は下記で専用列（song_part_variant_code / song_size_variant_code）
            // に格納するため、notes には残さない。
            string? notes = NullIfEmpty(row.Memo);

            // 旧 song_type → song_part_variants.variant_code
            //   NULL は「通常歌入り（VOCAL）」として記録する。SONG トラックの既定値。
            // 旧 song_size → song_size_variants.variant_code
            //   マップに無い値は OTHER にフォールバックし警告を出す。
            string? sizeVariant = null;
            string? partVariant = null;
            if (contentKind == "SONG")
            {
                partVariant = MapSongTypeToPartVariant(row.SongType);
                if (!string.IsNullOrWhiteSpace(row.SongSize))
                {
                    sizeVariant = MapSongSizeToSizeVariant(row.SongSize);
                    if (sizeVariant == "OTHER")
                    {
                        report.Warnings.Add($"tracks: disc_id={row.OldDiscId} #{row.TrackNo} の song_size='{row.SongSize}' がマスタ未登録のため OTHER にフォールバック");
                    }
                }
            }

            // 整合性フォールバック:
            //   トラック BEFORE INSERT トリガーで "SONG なのに song_recording_id NULL" は弾かれるため、
            //   recording を解決できなかった場合は OTHER に格下げする（title_override は既に保持済み）。
            if (contentKind == "SONG" && songRecId is null)
            {
                report.Warnings.Add($"tracks: disc_id={row.OldDiscId} #{row.TrackNo} を SONG から OTHER に格下げ (recording 未特定)");
                contentKind = "OTHER";
                sizeVariant = null;
                partVariant = null;
            }
            if (contentKind == "BGM" && bgmSeriesId is null)
            {
                report.Warnings.Add($"tracks: disc_id={row.OldDiscId} #{row.TrackNo} を BGM から OTHER に格下げ (cue 未特定)");
                contentKind = "OTHER";
            }

            var t = new Track
            {
                CatalogNo = catalogNo,
                TrackNo = (byte)row.TrackNo,
                SubOrder = 0, // 旧 DB は 1 トラック = 1 曲の前提のため、全て親行 (sub_order=0) として登録する。
                              // メドレーや BGM 前後半分割は移行後に Catalog GUI で sub_order=1,2,... を追加する運用。
                ContentKindCode = contentKind,
                SongRecordingId = songRecId,
                SongSizeVariantCode = sizeVariant,
                SongPartVariantCode = partVariant,
                BgmSeriesId = bgmSeriesId,
                BgmMNoDetail = bgmMNoDetail,
                TrackTitleOverride = titleOverride,
                Notes = notes,
                CreatedBy = "legacy-import",
                UpdatedBy = "legacy-import"
            };

            if (!dryRun)
            {
                try
                {
                    await tracksRepo.UpsertAsync(t);
                }
                catch (Exception ex)
                {
                    report.Warnings.Add($"tracks upsert 失敗 ({catalogNo}/#{row.TrackNo}): {ex.Message}");
                    continue;
                }
            }
            report.TracksCreated++;
        }
    }

    /// <summary>
    /// 旧 tracks.track_class (BGM/Drama/Live/Other/Radio/TieUp/Vocal) を
    /// 新 track_content_kinds コードにマップする。
    /// <para>
    /// Vocal は songs マスタに登録された歌として扱うため SONG へマップし、song_recording_id を解決する。
    /// Live / TieUp は旧 DB の運用上 songs マスタに登録せず、音源としての出自区分だけを保持していた
    /// トラック群のため、それぞれ専用コード LIVE / TIE_UP にマップする。LIVE / TIE_UP は録音参照を
    /// 持たず、tracks 側のトリガー（SONG 時に song_recording_id 必須）にも抵触しない。
    /// </para>
    /// </summary>
    private static string MapTrackClassToContentKind(string? cls) => cls switch
    {
        "BGM"   => "BGM",
        "Drama" => "DRAMA",
        "Radio" => "RADIO",
        "Vocal" => "SONG",     // songs マスタ登録済みの歌。song_recording_id を紐付ける
        "Live"  => "LIVE",     // ライブ音源。songs には登録せず音源区分のみ保持
        "TieUp" => "TIE_UP",   // タイアップ音源。同上
        "Other" => "OTHER",
        _       => "OTHER",
    };

    /// <summary>
    /// 旧 tracks.song_type を新 song_part_variants.variant_code にマップする。
    /// <para>
    /// NULL は「通常歌入り (VOCAL)」として扱う。未知の値は "OTHER" にフォールバック。
    /// </para>
    /// </summary>
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

    /// <summary>
    /// 旧 tracks.song_size を新 song_size_variants.variant_code にマップする。
    /// <para>
    /// ローマ数字 (Ⅰ〜Ⅴ) の正確なマッチを行う。未知の値は "OTHER" にフォールバック。
    /// </para>
    /// </summary>
    private static string MapSongSizeToSizeVariant(string songSize) => songSize switch
    {
        "Full"            => "FULL",
        // 旧 "TV" は新コードでも "TV"（v1.1.1 で "TV_SIZE" から "TV" に改称）
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

    /// <summary>旧 series.id (8桁) を新 series_id に解決する。空・未マップなら null（オールスターズ扱い）。</summary>
    private static int? ResolveSeries(string? legacySeriesId, Dictionary<string, int> map)
    {
        if (string.IsNullOrWhiteSpace(legacySeriesId)) return null;
        return map.TryGetValue(legacySeriesId, out int id) ? id : null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>接続文字列のパスワード部分を隠す（ログ出力時の安全のため）。</summary>
    private static string RedactPassword(string cs)
    {
        var parts = cs.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            var kv = parts[i].Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            if (key.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
                || key.Equals("Password", StringComparison.OrdinalIgnoreCase))
            {
                parts[i] = $"{key}=***";
            }
        }
        return string.Join(';', parts);
    }

    // =========================================================================
    // 旧テーブル DTO
    // =========================================================================

    private sealed class LegacySeriesRow
    {
        public string Id { get; set; } = "";
        public string? Title { get; set; }
    }

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
        public string? Distributer { get; set; } // 旧 DB の列名は "distributer"（typo）
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

    /// <summary>移行サマリー用のレポート。</summary>
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