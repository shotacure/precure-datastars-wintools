using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.CsvImport;

/// <summary>
/// 歌マスタ（songs）用の CSV 取り込みサービス。
/// <para>
/// 期待する CSV ヘッダ（UTF-8、カンマ区切り、ヘッダ行必須）:
/// <code>
/// title,title_kana,music_class_code,series_title_short,lyricist_name,lyricist_name_kana,composer_name,composer_name_kana,arranger_name,arranger_name_kana,notes
/// </code>
/// </para>
/// <para>
/// <list type="bullet">
///   <item><c>title</c> は必須。空行はスキップ</item>
///   <item><c>series_title_short</c> は <c>series.title_short</c> を優先し、マッチしなければ
///     <c>series.title</c> を部分一致で探す。見つからない場合は <c>series_id=NULL</c>（オールスターズ扱い）で登録</item>
///   <item><c>music_class_code</c> は <c>song_music_classes.class_code</c> にヒットしなければ NULL</item>
///   <item>既存行判定は <c>(title, series_id, arranger_name)</c> の等価で行う（簡易キー）。
///     同一キーが既にあれば更新、なければ新規追加</item>
/// </list>
/// </para>
/// </summary>
public sealed class SongCsvImportService
{
    private readonly SongsRepository _songsRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly SongMusicClassesRepository _musicClassesRepo;

    public SongCsvImportService(
        SongsRepository songsRepo,
        SeriesRepository seriesRepo,
        SongMusicClassesRepository musicClassesRepo)
    {
        _songsRepo = songsRepo ?? throw new ArgumentNullException(nameof(songsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _musicClassesRepo = musicClassesRepo ?? throw new ArgumentNullException(nameof(musicClassesRepo));
    }

    /// <summary>CSV インポート 1 件の処理結果。</summary>
    public sealed record Result(int Inserted, int Updated, int Skipped, IReadOnlyList<string> Warnings);

    /// <summary>
    /// 指定パスの CSV を取り込む。引数 <paramref name="dryRun"/> が true の場合、件数とサマリだけ
    /// 返して DB には一切書き込まない（事前確認用）。
    /// </summary>
    public async Task<Result> ImportAsync(string csvPath, string operatorName, bool dryRun = false, CancellationToken ct = default)
    {
        var (_, rows) = SimpleCsvReader.ReadFile(csvPath);
        var warnings = new List<string>();
        int inserted = 0, updated = 0, skipped = 0;

        // マスタ参照用に事前ロード（CSV サイズが中規模でも線形探索で足りる）
        var allSeries = (await _seriesRepo.GetAllAsync(ct)).ToList();
        var allMusicClasses = (await _musicClassesRepo.GetAllAsync(ct)).ToList();
        var existingSongs = (await _songsRepo.GetAllAsync(false, ct)).ToList();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            // 行番号はヘッダ含めた CSV 上の行番号を算出（エラーメッセージが人間に読みやすい）
            int csvLineNo = i + 2;

            string title = Get(row, "title").Trim();
            if (string.IsNullOrEmpty(title))
            {
                warnings.Add($"L{csvLineNo}: title が空です。スキップします。");
                skipped++;
                continue;
            }

            // シリーズ解決（title_short 完全一致 → title 部分一致の順で拾う。見つからなければ NULL）
            int? seriesId = null;
            string seriesKey = Get(row, "series_title_short").Trim();
            if (!string.IsNullOrEmpty(seriesKey))
            {
                var hit = allSeries.FirstOrDefault(s =>
                              string.Equals(s.TitleShort, seriesKey, StringComparison.OrdinalIgnoreCase))
                         ?? allSeries.FirstOrDefault(s =>
                              !string.IsNullOrEmpty(s.Title) && s.Title.Contains(seriesKey, StringComparison.OrdinalIgnoreCase));
                if (hit is null)
                {
                    warnings.Add($"L{csvLineNo}: シリーズ '{seriesKey}' が見つかりません。series_id=NULL で登録します。");
                }
                else
                {
                    seriesId = hit.SeriesId;
                }
            }

            // 音楽種別コード（song_music_classes.class_code）の存在確認。無ければ NULL に退避。
            string? musicClassCode = Get(row, "music_class_code").Trim();
            if (!string.IsNullOrEmpty(musicClassCode))
            {
                var classHit = allMusicClasses.FirstOrDefault(m =>
                    string.Equals(m.ClassCode, musicClassCode, StringComparison.Ordinal));
                if (classHit is null)
                {
                    warnings.Add($"L{csvLineNo}: music_class_code='{musicClassCode}' はマスタに存在しません。NULL として登録します。");
                    musicClassCode = null;
                }
            }
            else
            {
                musicClassCode = null;
            }

            string? arrangerName = NullIfEmpty(Get(row, "arranger_name"));

            // 既存キー検索：(title, series_id, arranger_name) の組み合わせで同値判定。
            // メロディ単位ではなく編曲単位でユニークとする運用の都合に合わせる。
            var existing = existingSongs.FirstOrDefault(s =>
                string.Equals(s.Title, title, StringComparison.Ordinal) &&
                s.SeriesId == seriesId &&
                string.Equals(s.ArrangerName, arrangerName, StringComparison.Ordinal));

            var song = new Song
            {
                SongId = existing?.SongId ?? 0,
                Title = title,
                TitleKana = NullIfEmpty(Get(row, "title_kana")),
                MusicClassCode = musicClassCode,
                SeriesId = seriesId,
                LyricistName = NullIfEmpty(Get(row, "lyricist_name")),
                LyricistNameKana = NullIfEmpty(Get(row, "lyricist_name_kana")),
                ComposerName = NullIfEmpty(Get(row, "composer_name")),
                ComposerNameKana = NullIfEmpty(Get(row, "composer_name_kana")),
                ArrangerName = arrangerName,
                ArrangerNameKana = NullIfEmpty(Get(row, "arranger_name_kana")),
                Notes = NullIfEmpty(Get(row, "notes")),
                CreatedBy = operatorName,
                UpdatedBy = operatorName,
                IsDeleted = existing?.IsDeleted ?? false
            };

            if (dryRun)
            {
                if (existing is null) inserted++; else updated++;
                continue;
            }

            if (existing is null)
            {
                int newId = await _songsRepo.InsertAsync(song, ct);
                song.SongId = newId;
                existingSongs.Add(song); // 以降の同一キー突合ずれを防ぐためローカルキャッシュへ追加
                inserted++;
            }
            else
            {
                await _songsRepo.UpdateAsync(song, ct);
                updated++;
            }
        }

        return new Result(inserted, updated, skipped, warnings);
    }

    /// <summary>CSV 行辞書からキー取得。存在しなければ空文字。</summary>
    private static string Get(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v : "";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}