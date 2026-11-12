using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_episode_staff_roles テーブル（作品ごとの各話のスタッフの札の役職・並び順・札の色）のリポジトリ。
/// 作品の行は画面でまとめて編集するので、書き込みは作品単位の置き換え（<see cref="ReplaceForSeriesAsync"/>）だけを持つ。
/// </summary>
public sealed class SeriesEpisodeStaffRolesRepository : RepositoryBase
{
    /// <summary><see cref="SeriesEpisodeStaffRolesRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public SeriesEpisodeStaffRolesRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          series_id      AS SeriesId,
          role_code      AS RoleCode,
          display_order  AS DisplayOrder,
          badge_color    AS BadgeColor,
          created_at     AS CreatedAt,
          updated_at     AS UpdatedAt,
          created_by     AS CreatedBy,
          updated_by     AS UpdatedBy
        """;

    /// <summary>全作品の行を取得する（作品 ID → 並び順 → 役職コードの順）。</summary>
    public async Task<IReadOnlyList<SeriesEpisodeStaffRole>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_episode_staff_roles
            ORDER BY series_id, display_order, role_code;
            """;

        return await QueryListAsync<SeriesEpisodeStaffRole>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定作品の行を並び順で取得する。</summary>
    public async Task<IReadOnlyList<SeriesEpisodeStaffRole>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM series_episode_staff_roles
            WHERE series_id = @seriesId
            ORDER BY display_order, role_code;
            """;

        return await QueryListAsync<SeriesEpisodeStaffRole>(sql, new { seriesId }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定作品の行を、渡した並びで置き換える（1 つのトランザクションで作品の行を消してから入れ直す）。
    /// 並び順は渡したリストの順に 1 から振り直す。空のリストを渡すと作品の行は無くなる。
    /// 同じ役職を 2 回渡すと PK 違反で全体が巻き戻る。
    /// </summary>
    /// <param name="seriesId">作品 ID。</param>
    /// <param name="rows">出す順に並べた行（<see cref="SeriesEpisodeStaffRole.SeriesId"/> と <see cref="SeriesEpisodeStaffRole.DisplayOrder"/> は上書きする）。</param>
    /// <param name="user">監査列に記録するユーザー識別子。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ReplaceForSeriesAsync(int seriesId, IReadOnlyList<SeriesEpisodeStaffRole> rows, string user, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM series_episode_staff_roles WHERE series_id = @seriesId;",
                new { seriesId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            var toInsert = rows
                .Select((r, i) => new
                {
                    SeriesId = seriesId,
                    r.RoleCode,
                    DisplayOrder = (byte)(i + 1),
                    BadgeColor = r.BadgeColor.ToLowerInvariant(),
                    CreatedBy = user,
                    UpdatedBy = user
                })
                .ToList();
            if (toInsert.Count > 0)
            {
                await conn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO series_episode_staff_roles
                      (series_id, role_code, display_order, badge_color, created_by, updated_by)
                    VALUES
                      (@SeriesId, @RoleCode, @DisplayOrder, @BadgeColor, @CreatedBy, @UpdatedBy);
                    """, toInsert, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
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
