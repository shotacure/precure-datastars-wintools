using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// songs テーブル（歌マスタ）の CRUD リポジトリ。
/// <para>
/// 作品としての 1 曲を扱い、録音バージョンは <see cref="SongRecordingsRepository"/> が管理する。
/// </para>
/// </summary>
public sealed class SongsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="SongsRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public SongsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          song_id                     AS SongId,
          title                       AS Title,
          title_kana                  AS TitleKana,
          music_class_code            AS MusicClassCode,
          series_id                   AS SeriesId,
          lyricist_name               AS LyricistName,
          lyricist_name_kana          AS LyricistNameKana,
          composer_name               AS ComposerName,
          composer_name_kana          AS ComposerNameKana,
          arranger_name               AS ArrangerName,
          arranger_name_kana          AS ArrangerNameKana,
          notes                       AS Notes,
          created_at                  AS CreatedAt,
          updated_at                  AS UpdatedAt,
          created_by                  AS CreatedBy,
          updated_by                  AS UpdatedBy,
          is_deleted                  AS IsDeleted
        """;

    /// <summary>全曲を取得する（song_id 昇順）。</summary>
    public async Task<IReadOnlyList<Song>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM songs
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY song_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Song>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>シリーズ ID で絞り込み（song_id 昇順）。</summary>
    public async Task<IReadOnlyList<Song>> GetBySeriesAsync(int? seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM songs
            WHERE is_deleted = 0
              AND {(seriesId.HasValue ? "series_id = @seriesId" : "series_id IS NULL")}
            ORDER BY song_id;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Song>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>タイトル部分一致で検索（DiscsEditor のトラック曲紐付けから利用）。</summary>
    public async Task<IReadOnlyList<Song>> SearchByTitleAsync(string keyword, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM songs
            WHERE is_deleted = 0
              AND (title LIKE @kw OR title_kana LIKE @kw)
            ORDER BY title
            LIMIT 200;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Song>(new CommandDefinition(sql, new { kw = $"%{keyword}%" }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>曲 ID で 1 件取得する。</summary>
    public async Task<Song?> GetByIdAsync(int songId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM songs
            WHERE song_id = @songId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Song>(
            new CommandDefinition(sql, new { songId }, cancellationToken: ct));
    }

    /// <summary>新規作成。AUTO_INCREMENT の song_id を返す。</summary>
    public async Task<int> InsertAsync(Song song, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO songs
              (title, title_kana, music_class_code, series_id,
               lyricist_name, lyricist_name_kana,
               composer_name, composer_name_kana,
               arranger_name, arranger_name_kana,
               notes, created_by, updated_by)
            VALUES
              (@Title, @TitleKana, @MusicClassCode, @SeriesId,
               @LyricistName, @LyricistNameKana,
               @ComposerName, @ComposerNameKana,
               @ArrangerName, @ArrangerNameKana,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, song, cancellationToken: ct));
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Song song, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE songs SET
              title                       = @Title,
              title_kana                  = @TitleKana,
              music_class_code            = @MusicClassCode,
              series_id                   = @SeriesId,
              lyricist_name               = @LyricistName,
              lyricist_name_kana          = @LyricistNameKana,
              composer_name               = @ComposerName,
              composer_name_kana          = @ComposerNameKana,
              arranger_name               = @ArrangerName,
              arranger_name_kana          = @ArrangerNameKana,
              notes                       = @Notes,
              updated_by                  = @UpdatedBy,
              is_deleted                  = @IsDeleted
            WHERE song_id = @SongId;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, song, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int songId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE songs SET is_deleted = 1, updated_by = @UpdatedBy WHERE song_id = @SongId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { SongId = songId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}