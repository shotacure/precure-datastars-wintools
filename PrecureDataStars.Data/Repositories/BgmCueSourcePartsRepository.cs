using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// bgm_cue_source_parts テーブル（劇伴の曲が使っている曲の順序付き対応表）のリポジトリ。
/// 1 つの劇伴の曲（series_id + m_no_detail）に対して、曲の中で流れる順（part_seq）ごとに
/// 歌（source_song_id）か曲（著作物）（source_work_id）のどちらか一方を持つ。
/// 編集は劇伴の曲単位の丸ごと差し替え（<see cref="ReplaceAllAsync"/>）で行う。
/// </summary>
public sealed class BgmCueSourcePartsRepository : RepositoryBase
{
    public BgmCueSourcePartsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          series_id            AS SeriesId,
          m_no_detail          AS MNoDetail,
          part_seq             AS PartSeq,
          source_song_id       AS SourceSongId,
          source_work_id       AS SourceWorkId,
          notes                AS Notes,
          created_at           AS CreatedAt,
          updated_at           AS UpdatedAt,
          created_by           AS CreatedBy,
          updated_by           AS UpdatedBy
        """;

    /// <summary>全行を取得する（series_id, m_no_detail, part_seq 昇順）。SiteBuilder の起動時の全件ロード用。</summary>
    public async Task<IReadOnlyList<BgmCueSourcePart>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_source_parts
            ORDER BY series_id, m_no_detail, part_seq;
            """;
        return await QueryListAsync<BgmCueSourcePart>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定した劇伴の曲が使っている曲を part_seq 昇順で取得する。</summary>
    public async Task<IReadOnlyList<BgmCueSourcePart>> GetByCueAsync(int seriesId, string mNoDetail, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_cue_source_parts
            WHERE series_id = @seriesId AND m_no_detail = @m
            ORDER BY part_seq;
            """;
        return await QueryListAsync<BgmCueSourcePart>(sql, new { seriesId, m = mNoDetail }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定した劇伴の曲が使っている曲を丸ごと差し替える（既存全削除 → 渡された並びどおり part_seq を 1 から振り直して INSERT）。
    /// 各行は歌か曲（著作物）のどちらか一方だけを持つこと（DB の CHECK で拒否される）。1 トランザクションで実行する。
    /// </summary>
    public async Task ReplaceAllAsync(int seriesId, string mNoDetail, IReadOnlyList<BgmCueSourcePart> parts, string? updatedBy, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM bgm_cue_source_parts WHERE series_id = @SeriesId AND m_no_detail = @MNoDetail;",
                new { SeriesId = seriesId, MNoDetail = mNoDetail },
                transaction: tx, cancellationToken: ct));

            byte seq = 1;
            foreach (var p in parts)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO bgm_cue_source_parts
                      (series_id, m_no_detail, part_seq, source_song_id, source_work_id, notes, created_by, updated_by)
                    VALUES
                      (@SeriesId, @MNoDetail, @PartSeq, @SourceSongId, @SourceWorkId, @Notes, @CreatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        SeriesId = seriesId,
                        MNoDetail = mNoDetail,
                        PartSeq = seq,
                        p.SourceSongId,
                        p.SourceWorkId,
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
