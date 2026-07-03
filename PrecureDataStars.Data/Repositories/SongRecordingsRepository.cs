using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>song_recordings テーブル（歌の歌唱者バージョン）の CRUD リポジトリ。</summary>
public sealed class SongRecordingsRepository : RepositoryBase
{
    /// <summary><see cref="SongRecordingsRepository"/> の新しいインスタンスを生成する。</summary>
    public SongRecordingsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          song_recording_id    AS SongRecordingId,
          song_id              AS SongId,
          series_id            AS SeriesId,
          singer_name          AS SingerName,
          singer_name_kana     AS SingerNameKana,
          variant_label        AS VariantLabel,
          music_class_code     AS MusicClassCode,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy,
          is_deleted           AS IsDeleted
        """;

    /// <summary>全録音を取得する（song_recording_id 昇順）。 SiteBuilderの楽曲詳細ページで「歌 → 録音バージョン → 収録トラック」の逆引きを 効率的に行うために、起動時 1 回だけ全件をメモリに読み込んで使う想定。</summary>
    public async Task<IReadOnlyList<SongRecording>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_recordings
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY song_recording_id;
            """;

        return await QueryListAsync<SongRecording>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定曲に紐づく全録音を取得する（song_recording_id 昇順）。</summary>
    public async Task<IReadOnlyList<SongRecording>> GetBySongIdAsync(int songId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_recordings
            WHERE song_id = @songId AND is_deleted = 0
            ORDER BY song_recording_id;
            """;

        return await QueryListAsync<SongRecording>(sql, new { songId }, ct).ConfigureAwait(false);
    }

    /// <summary>録音 ID で 1 件取得する。</summary>
    public async Task<SongRecording?> GetByIdAsync(int songRecordingId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_recordings
            WHERE song_recording_id = @songRecordingId
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<SongRecording>(sql, new { songRecordingId }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。AUTO_INCREMENT の song_recording_id を返す。</summary>
    public async Task<int> InsertAsync(SongRecording rec, CancellationToken ct = default)
    {
        // 録音単位で持つ音楽種別 music_class_code と出典シリーズ series_id もここで保存する。
        const string sql = """
            INSERT INTO song_recordings
              (song_id, series_id, singer_name, singer_name_kana,
               variant_label, music_class_code,
               notes, created_by, updated_by)
            VALUES
              (@SongId, @SeriesId, @SingerName, @SingerNameKana,
               @VariantLabel, @MusicClassCode,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        return await ExecuteScalarAsync<int>(sql, rec, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(SongRecording rec, CancellationToken ct = default)
    {
        // 録音単位で持つ音楽種別 music_class_code と出典シリーズ series_id もここで更新する。
        const string sql = """
            UPDATE song_recordings SET
              song_id            = @SongId,
              series_id          = @SeriesId,
              singer_name        = @SingerName,
              singer_name_kana   = @SingerNameKana,
              variant_label      = @VariantLabel,
              music_class_code   = @MusicClassCode,
              notes              = @Notes,
              updated_by         = @UpdatedBy,
              is_deleted         = @IsDeleted
            WHERE song_recording_id = @SongRecordingId;
            """;

        await ExecuteAsync(sql, rec, ct).ConfigureAwait(false);
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int songRecordingId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE song_recordings SET is_deleted = 1, updated_by = @UpdatedBy WHERE song_recording_id = @SongRecordingId;";
        await ExecuteAsync(sql, new { SongRecordingId = songRecordingId, UpdatedBy = updatedBy }, ct).ConfigureAwait(false);
    }

    /// <summary>既存録音から歌手名・かなをユニーク抽出して返す。 歌マスタ管理フォームで、歌手名テキストボックスの AutoCompleteSource として使う。</summary>
    public async Task<IReadOnlyList<string>> GetSingerNameCandidatesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT name FROM (
              SELECT singer_name      AS name FROM song_recordings WHERE is_deleted = 0 AND singer_name      IS NOT NULL AND singer_name      <> ''
              UNION
              SELECT singer_name_kana AS name FROM song_recordings WHERE is_deleted = 0 AND singer_name_kana IS NOT NULL AND singer_name_kana <> ''
            ) u
            ORDER BY name
            LIMIT 5000;
            """;

        return await QueryListAsync<string>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 録音をキーワード検索する。
    /// 親曲タイトル（songs.title / songs.title_kana）と当該録音の歌手名・歌手名かな・
    /// バリエーションラベルを対象に LIKE 検索する。クレジット系マスタ管理および
    /// クレジット本体編集の各ピッカーダイアログから呼ばれる。
    /// 結果は曲タイトル昇順 → song_recording_id 昇順、最大 <paramref name="limit"/> 件。
    /// 親曲タイトルを返却に含める必要があるため、専用 DTO
    /// <see cref="SongRecordingSearchResult"/> にマップして返す。
    /// </summary>
    /// <param name="keyword">検索キーワード（前後の空白は呼び出し側で除去すること）。空文字なら全件相当。</param>
    /// <param name="limit">最大取得件数（既定 100）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<SongRecordingSearchResult>> SearchAsync(
        string keyword, int limit = 100, CancellationToken ct = default)
    {
        // LIKE 用のキーワードを生成。空文字なら % のみで全件マッチ。
        string kw = "%" + (keyword ?? "").Trim() + "%";

        const string sql = """
            SELECT
              sr.song_recording_id  AS SongRecordingId,
              sr.song_id            AS SongId,
              s.title               AS SongTitle,
              s.title_kana          AS SongTitleKana,
              sr.singer_name        AS SingerName,
              sr.singer_name_kana   AS SingerNameKana,
              sr.variant_label      AS VariantLabel
            FROM song_recordings sr
            LEFT JOIN songs s ON s.song_id = sr.song_id
            WHERE sr.is_deleted = 0
              AND (
                    s.title              LIKE @kw
                 OR s.title_kana         LIKE @kw
                 OR sr.singer_name       LIKE @kw
                 OR sr.singer_name_kana  LIKE @kw
                 OR sr.variant_label     LIKE @kw
              )
            ORDER BY s.title, sr.song_recording_id
            LIMIT @limit;
            """;

        return await QueryListAsync<SongRecordingSearchResult>(sql, new { kw, limit }, ct).ConfigureAwait(false);
    }
}