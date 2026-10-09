using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// song_medley_parts テーブル（メドレーの中の曲の順序付き対応表）のリポジトリ。
/// 1 つのメドレーの曲（medley_song_id）に対して、何曲目か（part_seq）ごとに原曲を持つ。
/// 編集はメドレー単位の丸ごと差し替え（<see cref="ReplaceAllAsync"/>）で行う。
/// </summary>
public sealed class SongMedleyPartsRepository : RepositoryBase
{
    public SongMedleyPartsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          medley_song_id       AS MedleySongId,
          part_seq             AS PartSeq,
          source_song_id       AS SourceSongId,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy
        """;

    /// <summary>全行を取得する（medley_song_id, part_seq 昇順）。SiteBuilder の起動時の全件ロード用。</summary>
    public async Task<IReadOnlyList<SongMedleyPart>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_medley_parts
            ORDER BY medley_song_id, part_seq;
            """;
        return await QueryListAsync<SongMedleyPart>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定メドレーの曲の中身を part_seq 昇順で取得する。</summary>
    public async Task<IReadOnlyList<SongMedleyPart>> GetByMedleyAsync(int medleySongId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM song_medley_parts
            WHERE medley_song_id = @medleySongId
            ORDER BY part_seq;
            """;
        return await QueryListAsync<SongMedleyPart>(sql, new { medleySongId }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定メドレーの曲の中身を丸ごと差し替える（既存全削除 → 渡された並びどおり part_seq を 1 から振り直して INSERT）。
    /// 1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllAsync(int medleySongId, IReadOnlyList<SongMedleyPart> parts, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM song_medley_parts WHERE medley_song_id = @MedleySongId;",
                new { MedleySongId = medleySongId },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var p in parts)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO song_medley_parts
                      (medley_song_id, part_seq, source_song_id, notes, created_by, updated_by)
                    VALUES
                      (@MedleySongId, @PartSeq, @SourceSongId, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        MedleySongId = medleySongId,
                        PartSeq = seq,
                        p.SourceSongId,
                        p.Notes,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy
                    },
                    transaction: tx, cancellationToken: ct));
                seq++;
            }

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }
}
