using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_music_classes テーブル（曲の音楽種別マスタ）の読み取りリポジトリ。
/// </summary>
public sealed class SongMusicClassesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="SongMusicClassesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    public SongMusicClassesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>全件取得（display_order 昇順）。</summary>
    public async Task<IReadOnlyList<SongMusicClass>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              class_code    AS ClassCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM song_music_classes
            ORDER BY COALESCE(display_order, 255), class_code;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<SongMusicClass>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>UPSERT（MastersEditor から利用）。</summary>
    public async Task UpsertAsync(SongMusicClass kind, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_music_classes (class_code, name_ja, name_en, display_order, created_by, updated_by)
            VALUES (@ClassCode, @NameJa, @NameEn, @DisplayOrder, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja = VALUES(name_ja),
              name_en = VALUES(name_en),
              display_order = VALUES(display_order),
              updated_by = VALUES(updated_by);
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, kind, cancellationToken: ct));
    }

    /// <summary>指定コードのマスタを削除する。</summary>
    public async Task DeleteAsync(string classCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM song_music_classes WHERE class_code = @ClassCode;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { ClassCode = classCode }, cancellationToken: ct));
    }
}