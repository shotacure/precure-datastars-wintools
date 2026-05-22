using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>bgm_sessions テーブル（劇伴の録音セッションマスタ）の CRUD リポジトリ。 シリーズごとに <c>session_no</c> 0, 1, 2, ... と採番。<c>session_no</c>=0 は「未設定」用の既定値で、 各シリーズに 1 件ずつ初期投入される想定。</summary>
public sealed class BgmSessionsRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary><see cref="BgmSessionsRepository"/> の新しいインスタンスを生成する。</summary>
    public BgmSessionsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // caption は公開サイト表示用の補足説明カラム（NULL 許容）。SELECT 時もモデルへ
    // 確実に流し込むため列マッピングに常時含める。
    private const string SelectColumns = """
          series_id     AS SeriesId,
          session_no    AS SessionNo,
          session_name  AS SessionName,
          caption       AS Caption,
          notes         AS Notes,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy
        """;

    /// <summary>指定シリーズの全セッションを session_no 昇順で取得する。</summary>
    public async Task<IReadOnlyList<BgmSession>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_sessions
            WHERE series_id = @seriesId
            ORDER BY session_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmSession>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>全シリーズの全セッションを取得する（MastersEditor 用）。</summary>
    public async Task<IReadOnlyList<BgmSession>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM bgm_sessions
            ORDER BY series_id, session_no;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<BgmSession>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// 新規セッションを採番追加する（シリーズ内の最大 session_no + 1 を割り当てる）。
    /// 採番方針: シリーズ内に既存セッションが無ければ 1 から始まる番号を返す
    /// （session_no=0 は予約しない）。
    /// caption（公開サイト表示用の補足説明）は任意。NULL または空文字を渡せば未設定扱いとなる。
    /// </summary>
    public async Task<byte> InsertNextAsync(int seriesId, string sessionName, string? caption, string? notes, string? createdBy, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // シリーズ内の最大 session_no を取る。空（既存セッション無し）なら 0 が返り、新番は 1 になる。
            const string maxSql = "SELECT COALESCE(MAX(session_no), 0) FROM bgm_sessions WHERE series_id = @seriesId FOR UPDATE;";
            var maxNo = await conn.ExecuteScalarAsync<byte>(new CommandDefinition(maxSql, new { seriesId }, transaction: tx, cancellationToken: ct));
            byte nextNo = (byte)(maxNo + 1);

            const string insSql = """
                INSERT INTO bgm_sessions (series_id, session_no, session_name, caption, notes, created_by, updated_by)
                VALUES (@seriesId, @sessionNo, @sessionName, @caption, @notes, @createdBy, @createdBy);
                """;
            await conn.ExecuteAsync(new CommandDefinition(insSql,
                new { seriesId, sessionNo = nextNo, sessionName, caption, notes, createdBy },
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

    /// <summary>
    /// セッション名・補足説明（caption）・備考（notes）を更新する。session_no は PK のため変更不可。
    /// </summary>
    public async Task UpdateAsync(BgmSession s, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE bgm_sessions
               SET session_name = @SessionName,
                   caption      = @Caption,
                   notes        = @Notes,
                   updated_by   = @UpdatedBy
             WHERE series_id = @SeriesId AND session_no = @SessionNo;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, s, cancellationToken: ct));
    }

    /// <summary>セッションを物理削除する。配下に bgm_cues が残っている場合は FK 制約 (ON DELETE RESTRICT) によって失敗する。</summary>
    public async Task DeleteAsync(int seriesId, byte sessionNo, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM bgm_sessions WHERE series_id = @seriesId AND session_no = @sessionNo;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { seriesId, sessionNo }, cancellationToken: ct));
    }
}
