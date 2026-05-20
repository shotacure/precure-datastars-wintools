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
/// 期待する CSV ヘッダ（UTF-8、カンマ区切り、ヘッダ行必須）:
/// <code>
/// title,title_kana,series_title_short,lyricist_name,lyricist_name_kana,composer_name,composer_name_kana,arranger_name,arranger_name_kana,notes
/// </code>
/// <list type="bullet">
///   <item><c>title</c> は必須。空行はスキップ</item>
///   <item><c>series_title_short</c> は <c>series.title_short</c> を優先し、マッチしなければ
///     <c>series.title</c> を部分一致で探す。見つからない場合は出典シリーズ未設定で取り込む。
///     v1.3.8 以降は出典シリーズが録音単位（<c>song_recordings.series_id</c>）に移ったため、
///     本サービスは「曲ヘッダ + 対応する単一録音 1 件」をワンセットで登録／更新する：
///     <list type="bullet">
///       <item>新規曲なら、その曲と一緒に同 series_id を持つ <c>song_recordings</c> 行を 1 件作る</item>
///       <item>既存曲なら、同 (song_id, series_id, arranger_name) の組合せで既存 recording を
///         判定し、無ければ追加、あれば touch のみ（既存 recording は触らない）</item>
///     </list></item>
///   <item><c>music_class_code</c> は本サービスでは扱わない（音楽種別は <c>song_recordings</c> 側で
///     後段の編集 UI で個別設定する仕様のため）。後方互換のため CSV に列が残っていても
///     無視して取り込みを継続する（値は使わず警告のみ出力）</item>
///   <item>既存曲の判定は <c>(title, arranger_name)</c> の等価で行う（簡易キー）。
///     同一キーが既にあれば更新、なければ新規追加。
///     旧バージョンでは <c>series_id</c> も判定キーだったが、songs から series_id が
///     撤去されたためキー集合から外した（series 違いは録音側で表現する）</item>
/// </list>
/// </summary>
public sealed class SongCsvImportService
{
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly SeriesRepository _seriesRepo;
    // 音楽種別は録音単位で管理する仕様のため、本サービスは SongMusicClassesRepository に依存しない。

    public SongCsvImportService(
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        SeriesRepository seriesRepo)
    {
        _songsRepo = songsRepo ?? throw new ArgumentNullException(nameof(songsRepo));
        _songRecRepo = songRecRepo ?? throw new ArgumentNullException(nameof(songRecRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
    }

    /// <summary>CSV インポート 1 件の処理結果。</summary>
    public sealed record Result(int Inserted, int Updated, int Skipped, IReadOnlyList<string> Warnings);

    /// <summary>指定パスの CSV を取り込む。引数 <paramref name="dryRun"/> が true の場合、件数とサマリだけ 返して DB には一切書き込まない（事前確認用）。</summary>
    public async Task<Result> ImportAsync(string csvPath, string operatorName, bool dryRun = false, CancellationToken ct = default)
    {
        var (_, rows) = SimpleCsvReader.ReadFile(csvPath);
        var warnings = new List<string>();
        int inserted = 0, updated = 0, skipped = 0;

        // マスタ参照用に事前ロード（CSV サイズが中規模でも線形探索で足りる）
        var allSeries = (await _seriesRepo.GetAllAsync(ct)).ToList();
        var existingSongs = (await _songsRepo.GetAllAsync(false, ct)).ToList();
        // 既存録音を全件取得（後で「同 song_id + 同 series_id を持つ録音」が既にあるかを線形探索する）。
        var existingRecordings = (await _songRecRepo.GetAllAsync(false, ct)).ToList();

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

            // シリーズ解決（title_short 完全一致 → title 部分一致の順で拾う。見つからなければ NULL）。
            // この series_id は曲側ではなく、新規作成する録音（song_recordings）の出典シリーズに使う。
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
                    warnings.Add($"L{csvLineNo}: シリーズ '{seriesKey}' が見つかりません。出典シリーズ未設定で取り込みます。");
                }
                else
                {
                    seriesId = hit.SeriesId;
                }
            }

            // music_class_code は本サービスでは扱わない（音楽種別は song_recordings 側で管理する）。
            // 後方互換のため CSV に列が残っていれば警告だけ出して値は捨てる。
            string legacyMusicClass = Get(row, "music_class_code").Trim();
            if (!string.IsNullOrEmpty(legacyMusicClass))
            {
                warnings.Add($"L{csvLineNo}: music_class_code='{legacyMusicClass}' は曲側では扱いません（音楽種別は song_recordings 側で管理）。値は無視されます。");
            }

            string? arrangerName = NullIfEmpty(Get(row, "arranger_name"));

            // 既存曲キー検索：(title, arranger_name) の組み合わせで同値判定。
            // 旧バージョンでは series_id も判定キーだったが、songs から series_id が
            // 撤去されたためキー集合から外した。series 違いの曲を分けて管理したい場合は
            // 曲側で title や arranger_name を変えるか、後段の Catalog GUI で個別に録音追加する。
            var existing = existingSongs.FirstOrDefault(s =>
                string.Equals(s.Title, title, StringComparison.Ordinal) &&
                string.Equals(s.ArrangerName, arrangerName, StringComparison.Ordinal));

            var song = new Song
            {
                SongId = existing?.SongId ?? 0,
                Title = title,
                TitleKana = NullIfEmpty(Get(row, "title_kana")),
                // 音楽種別・出典シリーズは録音単位で持つため Song インスタンス化からは除外。
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

            int songId;
            if (existing is null)
            {
                songId = await _songsRepo.InsertAsync(song, ct);
                song.SongId = songId;
                existingSongs.Add(song); // 以降の同一キー突合ずれを防ぐためローカルキャッシュへ追加
                inserted++;
            }
            else
            {
                songId = existing.SongId;
                await _songsRepo.UpdateAsync(song, ct);
                updated++;
            }

            // 出典シリーズが指定された場合、対応する録音を 1 件確保する。
            // 「同 song_id + 同 series_id を持つ録音」が既にあれば touch せず、無ければ追加。
            // CSV からは歌唱者やバリエーションラベルは伝達しないため、自動作成された
            // 録音は singer_name = NULL / variant_label = NULL の素朴な行となる（後で UI で編集する想定）。
            if (seriesId.HasValue)
            {
                var existingRec = existingRecordings.FirstOrDefault(r =>
                    r.SongId == songId && r.SeriesId == seriesId.Value);
                if (existingRec is null)
                {
                    var newRec = new SongRecording
                    {
                        SongId = songId,
                        SeriesId = seriesId,
                        SingerName = null,
                        SingerNameKana = null,
                        VariantLabel = null,
                        MusicClassCode = null,
                        Notes = null,
                        CreatedBy = operatorName,
                        UpdatedBy = operatorName
                    };
                    int newRecId = await _songRecRepo.InsertAsync(newRec, ct);
                    newRec.SongRecordingId = newRecId;
                    existingRecordings.Add(newRec);
                }
            }
        }

        return new Result(inserted, updated, skipped, warnings);
    }

    /// <summary>CSV 行辞書からキー取得。存在しなければ空文字。</summary>
    private static string Get(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) ? v : "";

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}