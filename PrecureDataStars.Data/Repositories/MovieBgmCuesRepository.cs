using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// movie_bgm_cues テーブル（映画作品の BGM リスト 1 キュー = 1 行）の CRUD リポジトリ。
/// <para>
/// <see cref="BgmCuesRepository"/>（TV シリーズのセッション制・劇伴専用、複合 PK）
/// とは別概念で、こちらは代理キー <c>movie_bgm_cue_id</c> を主キーとする。映画には
/// セッション・パートの概念が無く、その映画固有の M ナンバー文字列・順序（seq）・
/// サブ順序（sub_seq）・区分（track_content_kinds 共用）・未使用/欠番フラグのみを持つ。
/// </para>
/// <para>
/// <c>series_id</c> は映画系シリーズ（kind_code が MOVIE / MOVIE_SHORT / SPRING /
/// EVENT）のみ許容され、DB 側のトリガーで担保される。違反する INSERT/UPDATE は
/// SQLSTATE 45000 で失敗するため、呼び出し側は映画系シリーズのみを渡すこと。
/// </para>
/// </summary>
public sealed class MovieBgmCuesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="MovieBgmCuesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public MovieBgmCuesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // Dapper は tinyint(0/1) を bool（IsUnused / IsMissing / IsDeleted）にマップする。
    private const string SelectColumns = """
          movie_bgm_cue_id    AS MovieBgmCueId,
          series_id           AS SeriesId,
          seq                 AS Seq,
          sub_seq             AS SubSeq,
          m_no                AS MNo,
          content_kind_code   AS ContentKindCode,
          title               AS Title,
          notes               AS Notes,
          is_unused           AS IsUnused,
          is_missing          AS IsMissing,
          created_at          AS CreatedAt,
          updated_at          AS UpdatedAt,
          created_by          AS CreatedBy,
          updated_by          AS UpdatedBy,
          is_deleted          AS IsDeleted
        """;

    /// <summary>
    /// 指定シリーズの映画 BGM キューを取得する（論理削除を除く）。
    /// 並び順は (seq, sub_seq, movie_bgm_cue_id) の昇順。
    /// </summary>
    public async Task<IReadOnlyList<MovieBgmCue>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM movie_bgm_cues
            WHERE series_id = @seriesId AND is_deleted = 0
            ORDER BY seq, sub_seq, movie_bgm_cue_id
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<MovieBgmCue>(
            new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// 代理キーで 1 件取得する（論理削除済みも返す。編集対象の再読込用）。
    /// </summary>
    public async Task<MovieBgmCue?> GetByIdAsync(int movieBgmCueId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM movie_bgm_cues
            WHERE movie_bgm_cue_id = @movieBgmCueId
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<MovieBgmCue>(
            new CommandDefinition(sql, new { movieBgmCueId }, cancellationToken: ct));
    }

    /// <summary>
    /// 新規キューを挿入し、採番された movie_bgm_cue_id を返す。
    /// series_id が映画系シリーズでない場合は DB トリガーが SQLSTATE 45000 を送出する。
    /// </summary>
    public async Task<int> InsertAsync(MovieBgmCue cue, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO movie_bgm_cues
              (series_id, seq, sub_seq, m_no, content_kind_code, title, notes,
               is_unused, is_missing, created_by, updated_by)
            VALUES
              (@SeriesId, @Seq, @SubSeq, @MNo, @ContentKindCode, @Title, @Notes,
               @IsUnused, @IsMissing, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, cue, cancellationToken: ct));
    }

    /// <summary>
    /// 既存キューを更新する（代理キー指定）。created_* は変更しない。
    /// </summary>
    public async Task UpdateAsync(MovieBgmCue cue, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE movie_bgm_cues SET
              series_id         = @SeriesId,
              seq               = @Seq,
              sub_seq           = @SubSeq,
              m_no              = @MNo,
              content_kind_code = @ContentKindCode,
              title             = @Title,
              notes             = @Notes,
              is_unused         = @IsUnused,
              is_missing        = @IsMissing,
              updated_by        = @UpdatedBy
            WHERE movie_bgm_cue_id = @MovieBgmCueId
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, cue, cancellationToken: ct));
    }

    /// <summary>
    /// キューを論理削除する（is_deleted = 1）。物理削除はしない。
    /// </summary>
    public async Task SoftDeleteAsync(int movieBgmCueId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE movie_bgm_cues
            SET is_deleted = 1, updated_by = @updatedBy
            WHERE movie_bgm_cue_id = @movieBgmCueId
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(
            new CommandDefinition(sql, new { movieBgmCueId, updatedBy }, cancellationToken: ct));
    }
}
