using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// video_chapters テーブル（BD/DVD チャプター）のリポジトリ。
/// <para>
/// BDAnalyzer からの一括登録と、Catalog GUI での閲覧・編集・削除に対応する。
/// ディスク単位で「全削除 → 一括挿入」する運用が基本（再読み取り時のチャプター境界変動に対応するため）。
/// </para>
/// </summary>
public sealed class VideoChaptersRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="VideoChaptersRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public VideoChaptersRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // SELECT 句共通化。閲覧・編集どちらからも全列を返す。
    private const string SelectColumns = """
          catalog_no    AS CatalogNo,
          chapter_no    AS ChapterNo,
          title         AS Title,
          part_type     AS PartType,
          start_time_ms AS StartTimeMs,
          duration_ms   AS DurationMs,
          playlist_file AS PlaylistFile,
          source_kind   AS SourceKind,
          notes         AS Notes,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy,
          is_deleted    AS IsDeleted
        """;

    /// <summary>指定ディスクの全チャプターを chapter_no 昇順で取得する。</summary>
    public async Task<IReadOnlyList<VideoChapter>> GetByCatalogNoAsync(string catalogNo, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM video_chapters
            WHERE catalog_no = @catalogNo
              AND is_deleted = 0
            ORDER BY chapter_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<VideoChapter>(new CommandDefinition(sql, new { catalogNo }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 指定ディスクのチャプターを全削除してから渡されたリストで一括登録する。
    /// BDAnalyzer が再読み取りしたときにチャプター境界が変動していても整合させるため、
    /// 「全削除 → 一括挿入」のトランザクションで置き換える設計。
    /// </summary>
    public async Task ReplaceAllForDiscAsync(string catalogNo, IReadOnlyList<VideoChapter> chapters, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 既存チャプターは物理削除する（BDAnalyzer 側で上書き前提の運用）
            const string delSql = "DELETE FROM video_chapters WHERE catalog_no = @catalogNo;";
            await conn.ExecuteAsync(new CommandDefinition(delSql, new { catalogNo }, transaction: tx, cancellationToken: ct));

            if (chapters.Count > 0)
            {
                const string insSql = """
                    INSERT INTO video_chapters
                      (catalog_no, chapter_no, title, part_type,
                       start_time_ms, duration_ms, playlist_file, source_kind,
                       notes, created_by, updated_by)
                    VALUES
                      (@CatalogNo, @ChapterNo, @Title, @PartType,
                       @StartTimeMs, @DurationMs, @PlaylistFile, @SourceKind,
                       @Notes, @CreatedBy, @UpdatedBy);
                    """;
                // Dapper はコレクションを渡すと自動でバッチ INSERT になる
                await conn.ExecuteAsync(new CommandDefinition(insSql, chapters, transaction: tx, cancellationToken: ct));
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>個別チャプターを UPSERT する（Catalog GUI でのタイトル補完などから利用）。</summary>
    public async Task UpsertAsync(VideoChapter ch, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO video_chapters
              (catalog_no, chapter_no, title, part_type,
               start_time_ms, duration_ms, playlist_file, source_kind,
               notes, created_by, updated_by)
            VALUES
              (@CatalogNo, @ChapterNo, @Title, @PartType,
               @StartTimeMs, @DurationMs, @PlaylistFile, @SourceKind,
               @Notes, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              title         = VALUES(title),
              part_type     = VALUES(part_type),
              start_time_ms = VALUES(start_time_ms),
              duration_ms   = VALUES(duration_ms),
              playlist_file = VALUES(playlist_file),
              source_kind   = VALUES(source_kind),
              notes         = VALUES(notes),
              updated_by    = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, ch, cancellationToken: ct));
    }

    /// <summary>個別チャプターを物理削除する。</summary>
    public async Task DeleteAsync(string catalogNo, ushort chapterNo, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM video_chapters WHERE catalog_no = @catalogNo AND chapter_no = @chapterNo;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { catalogNo, chapterNo }, cancellationToken: ct));
    }
}
