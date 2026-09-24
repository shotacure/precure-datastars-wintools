using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>bgm_sections テーブル（録音セッション内のセクション）の CRUD リポジトリ。 (series_id, session_no) ごとに <c>section_no</c> を 1 から採番する。</summary>
public sealed class BgmSectionsRepository : RepositoryBase
{
    /// <summary><see cref="BgmSectionsRepository"/> の新しいインスタンスを生成する。</summary>
    public BgmSectionsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          series_id     AS SeriesId,
          session_no    AS SessionNo,
          section_no    AS SectionNo,
          section_name  AS SectionName,
          notes         AS Notes,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy
        """;

    /// <summary>指定シリーズの全セクションを (session_no, section_no) 昇順で取得する。</summary>
    public async Task<IReadOnlyList<BgmSection>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_sections
            WHERE series_id = @seriesId
            ORDER BY session_no, section_no;
            """;

        return await QueryListAsync<BgmSection>(sql, new { seriesId }, ct).ConfigureAwait(false);
    }

    /// <summary>全シリーズの全セクションを取得する（MastersEditor・CSV 取り込み・SiteBuilder 用）。</summary>
    public async Task<IReadOnlyList<BgmSection>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_sections
            ORDER BY series_id, session_no, section_no;
            """;

        return await QueryListAsync<BgmSection>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 新規セクションを採番追加する（セッション内の最大 section_no + 1 を割り当てる）。
    /// セッション内に既存セクションが無ければ 1 を返す。
    /// </summary>
    public async Task<byte> InsertNextAsync(int seriesId, byte sessionNo, string sectionName, string? notes, string? createdBy, CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            const string maxSql = """
                SELECT COALESCE(MAX(section_no), 0) FROM bgm_sections
                 WHERE series_id = @seriesId AND session_no = @sessionNo FOR UPDATE;
                """;
            var maxNo = await conn.ExecuteScalarAsync<byte>(new CommandDefinition(maxSql, new { seriesId, sessionNo }, transaction: tx, cancellationToken: ct));
            byte nextNo = (byte)(maxNo + 1);

            const string insSql = """
                INSERT INTO bgm_sections (series_id, session_no, section_no, section_name, notes, created_by, updated_by)
                VALUES (@seriesId, @sessionNo, @sectionNo, @sectionName, @notes, @createdBy, @createdBy);
                """;
            await conn.ExecuteAsync(new CommandDefinition(insSql,
                new { seriesId, sessionNo, sectionNo = nextNo, sectionName, notes, createdBy },
                transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return nextNo;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>セクション名・備考を更新する。section_no は PK のため変更不可。</summary>
    public async Task UpdateAsync(BgmSection s, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE bgm_sections
               SET section_name = @SectionName,
                   notes        = @Notes,
                   updated_by   = @UpdatedBy
             WHERE series_id = @SeriesId AND session_no = @SessionNo AND section_no = @SectionNo;
            """;

        await ExecuteAsync(sql, s, ct).ConfigureAwait(false);
    }

    /// <summary>セクションを物理削除する。所属する bgm_cues が残っている場合は FK 制約 (ON DELETE RESTRICT) によって失敗する。</summary>
    public async Task DeleteAsync(int seriesId, byte sessionNo, byte sectionNo, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM bgm_sections WHERE series_id = @seriesId AND session_no = @sessionNo AND section_no = @sectionNo;";
        await ExecuteAsync(sql, new { seriesId, sessionNo, sectionNo }, ct).ConfigureAwait(false);
    }
}
