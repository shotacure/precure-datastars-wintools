using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cues テーブル（劇伴の音源 1 件 = 1 行）の CRUD リポジトリ。
/// <para>
/// v1.1.0 の旧 bgm_cues + bgm_recordings の二階層構造は廃止し、1 テーブルに統合した。
/// 録音セッションは <c>session_no</c> 属性として保持し、<c>bgm_sessions</c> マスタへ FK する。
/// 主キーは <c>(series_id, m_no_detail)</c> の 2 列複合。
/// </para>
/// </summary>
public sealed class BgmCuesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="BgmCuesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public BgmCuesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          series_id            AS SeriesId,
          m_no_detail          AS MNoDetail,
          session_no           AS SessionNo,
          m_no_class           AS MNoClass,
          menu_title           AS MenuTitle,
          composer_name        AS ComposerName,
          composer_name_kana   AS ComposerNameKana,
          arranger_name        AS ArrangerName,
          arranger_name_kana   AS ArrangerNameKana,
          length_seconds       AS LengthSeconds,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy,
          is_deleted           AS IsDeleted
        """;

    /// <summary>指定シリーズの全 cue を取得する（m_no_class, m_no_detail 昇順）。</summary>
    public async Task<IReadOnlyList<BgmCue>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId AND is_deleted = 0
            ORDER BY m_no_class, m_no_detail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCue>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定シリーズ・指定セッションの cue を取得する。</summary>
    public async Task<IReadOnlyList<BgmCue>> GetBySeriesAndSessionAsync(int seriesId, byte sessionNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId AND session_no = @sessionNo AND is_deleted = 0
            ORDER BY m_no_class, m_no_detail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmCue>(new CommandDefinition(sql, new { seriesId, sessionNo }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キー (series_id, m_no_detail) で 1 件取得する。</summary>
    public async Task<BgmCue?> GetAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cues
            WHERE series_id = @seriesId AND m_no_detail = @mNoDetail
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<BgmCue>(
            new CommandDefinition(sql, new { seriesId, mNoDetail }, cancellationToken: ct));
    }

    /// <summary>
    /// UPSERT。PK 衝突時は全属性を新しい値で上書きする。
    /// </summary>
    public async Task UpsertAsync(BgmCue cue, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO bgm_cues
              (series_id, m_no_detail, session_no, m_no_class, menu_title,
               composer_name, composer_name_kana,
               arranger_name, arranger_name_kana,
               length_seconds, notes,
               created_by, updated_by)
            VALUES
              (@SeriesId, @MNoDetail, @SessionNo, @MNoClass, @MenuTitle,
               @ComposerName, @ComposerNameKana,
               @ArrangerName, @ArrangerNameKana,
               @LengthSeconds, @Notes,
               @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              session_no         = VALUES(session_no),
              m_no_class         = VALUES(m_no_class),
              menu_title         = VALUES(menu_title),
              composer_name      = VALUES(composer_name),
              composer_name_kana = VALUES(composer_name_kana),
              arranger_name      = VALUES(arranger_name),
              arranger_name_kana = VALUES(arranger_name_kana),
              length_seconds     = VALUES(length_seconds),
              notes              = VALUES(notes),
              updated_by         = VALUES(updated_by),
              is_deleted         = 0;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, cue, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int seriesId, string mNoDetail, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE bgm_cues
               SET is_deleted = 1, updated_by = @UpdatedBy
             WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql,
            new { SeriesId = seriesId, MNoDetail = mNoDetail, UpdatedBy = updatedBy },
            cancellationToken: ct));
    }
}
