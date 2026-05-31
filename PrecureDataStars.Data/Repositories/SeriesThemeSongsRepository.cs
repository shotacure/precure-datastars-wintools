using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_theme_songs テーブル（シリーズ × 主題歌の紐付け）の CRUD リポジトリ。
/// <see cref="EpisodeThemeSongsRepository"/> のミラー実装で、対象キーが
/// episode_id → series_id に置き換わっただけ。映画系列（series_kinds.credit_attach_to='SERIES'）の
/// 主題歌・OP 主題歌・挿入歌をシリーズ単位で持つ用途。
/// 複合主キーは 4 列 (series_id, is_broadcast_only, theme_kind, seq)。
/// 役職テンプレ DSL の <c>{SONG_TITLE}</c> / <c>{LYRICIST}</c> 等の auto-expand は、
/// SERIES スコープのクレジットでこのテーブルから引き当てる。
/// </summary>
public sealed class SeriesThemeSongsRepository
{
    private readonly IConnectionFactory _factory;

    public SeriesThemeSongsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          series_id               AS SeriesId,
          is_broadcast_only       AS IsBroadcastOnly,
          theme_kind              AS ThemeKind,
          seq                     AS Seq,
          usage_actuality         AS UsageActuality,
          song_recording_id       AS SongRecordingId,
          notes                   AS Notes,
          created_at              AS CreatedAt,
          updated_at              AS UpdatedAt,
          created_by              AS CreatedBy,
          updated_by              AS UpdatedBy
        """;

    /// <summary>全シリーズ × 主題歌の紐付け行を取得する（series_id → is_broadcast_only → seq 昇順）。 SiteBuilder の SiteDataLoader が起動時 1 回だけ全件メモリ展開するための入口。</summary>
    public async Task<IReadOnlyList<SeriesThemeSong>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_theme_songs
            ORDER BY series_id, is_broadcast_only, seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesThemeSong>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定シリーズに紐付く主題歌一覧を取得する。 is_broadcast_only → seq 昇順で並ぶ（劇中順）。</summary>
    public async Task<IReadOnlyList<SeriesThemeSong>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_theme_songs
            WHERE series_id = @seriesId
            ORDER BY is_broadcast_only, seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SeriesThemeSong>(
            new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>4 列複合 PK で 1 件取得する。</summary>
    public async Task<SeriesThemeSong?> GetByKeyAsync(
        int seriesId, bool isBroadcastOnly, string themeKind, byte seq,
        CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_theme_songs
            WHERE series_id         = @seriesId
              AND is_broadcast_only = @flag
              AND theme_kind        = @themeKind
              AND seq               = @seq
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<SeriesThemeSong>(
            new CommandDefinition(
                sql,
                new { seriesId, flag = isBroadcastOnly ? 1 : 0, themeKind, seq },
                cancellationToken: ct));
    }

    /// <summary>UPSERT（is_broadcast_only が PK の一部のため、フラグが変わると別レコードとして INSERT）。</summary>
    public async Task UpsertAsync(SeriesThemeSong row, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO series_theme_songs
              (series_id, is_broadcast_only, theme_kind, seq, usage_actuality,
               song_recording_id, notes, created_by, updated_by)
            VALUES
              (@SeriesId, @IsBroadcastOnly, @ThemeKind, @Seq, @UsageActuality,
               @SongRecordingId, @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              usage_actuality         = VALUES(usage_actuality),
              song_recording_id       = VALUES(song_recording_id),
              notes                   = VALUES(notes),
              updated_by              = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, row, cancellationToken: ct));
    }

    /// <summary>4 列複合 PK で 1 件削除する。</summary>
    public async Task DeleteAsync(
        int seriesId, bool isBroadcastOnly, string themeKind, byte seq,
        CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM series_theme_songs
            WHERE series_id         = @SeriesId
              AND is_broadcast_only = @Flag
              AND theme_kind        = @ThemeKind
              AND seq               = @Seq;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                SeriesId = seriesId,
                Flag = isBroadcastOnly ? 1 : 0,
                ThemeKind = themeKind,
                Seq = seq
            },
            cancellationToken: ct));
    }
}
