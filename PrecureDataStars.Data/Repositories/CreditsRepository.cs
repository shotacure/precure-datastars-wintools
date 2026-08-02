using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credits テーブル（クレジット本体）の CRUD リポジトリ。
/// シリーズ単位 or エピソード単位で OP/ED 各 1 件まで保持できる
/// （UNIQUE は <c>(series_id, credit_kind)</c> と <c>(episode_id, credit_kind)</c> の 2 本）。
/// scope_kind と series_id / episode_id の整合性は DB 側のトリガー
/// <c>trg_credits_b{i,u}_scope_consistency</c> で担保される（CHECK は MySQL 8.0 の
/// FK 参照アクション制約 Error 3823 を回避するため使用しない）。
/// 本放送と円盤・配信での差し替え（ロゴバージョン違い等）はクレジット単位ではなく
/// エントリ単位（<see cref="CreditBlockEntry.IsBroadcastOnly"/>）で扱う。
/// クレジット本体には is_broadcast_only 関連カラムを持たない。
/// </summary>
public sealed class CreditsRepository : RepositoryBase
{
    public CreditsRepository(IConnectionFactory factory) : base(factory) { }

    private const string SelectColumns = """
          credit_id     AS CreditId,
          scope_kind    AS ScopeKind,
          series_id     AS SeriesId,
          episode_id    AS EpisodeId,
          credit_kind   AS CreditKind,
          credit_seq    AS CreditSeq,
          part_type     AS PartType,
          presentation  AS Presentation,
          notes         AS Notes,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy,
          is_deleted    AS IsDeleted
        """;

    /// <summary>主キー（credit_id）で 1 件取得する。</summary>
    public async Task<Credit?> GetByIdAsync(int creditId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credits
            WHERE credit_id = @creditId
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<Credit>(sql, new { creditId }, ct).ConfigureAwait(false);
    }

    /// <summary>指定シリーズに紐付くクレジット（scope=SERIES）一覧を取得する（credit_seq 昇順）。</summary>
    public async Task<IReadOnlyList<Credit>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credits
            WHERE series_id = @seriesId AND is_deleted = 0
            ORDER BY credit_seq, credit_id;
            """;

        return await QueryListAsync<Credit>(sql, new { seriesId }, ct).ConfigureAwait(false);
    }

    /// <summary>credits テーブルの論理削除を除く全行を取得する（scope_kind, episode_id/series_id, credit_seq 昇順）。 SiteBuilder の SiteDataLoader が起動時に 1 度だけ呼んで、episode_id / series_id 単位で グルーピングして共有する用途で使う（SeriesGenerator / EpisodeGenerator の per-page <see cref="GetByEpisodeAsync"/> / <see cref="GetBySeriesAsync"/> を撲滅するため）。</summary>
    public async Task<IReadOnlyList<Credit>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credits
            WHERE is_deleted = 0
            ORDER BY scope_kind, episode_id, series_id, credit_seq, credit_id;
            """;

        return await QueryListAsync<Credit>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定エピソードに紐付くクレジット（scope=EPISODE）一覧を取得する（credit_seq 昇順）。</summary>
    public async Task<IReadOnlyList<Credit>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credits
            WHERE episode_id = @episodeId AND is_deleted = 0
            ORDER BY credit_seq, credit_id;
            """;

        return await QueryListAsync<Credit>(sql, new { episodeId }, ct).ConfigureAwait(false);
    }

    /// <summary>指定シリーズ内で、指定 credit_kind のエピソードスコープ・クレジットをまだ持たない
    /// 最初のエピソード（series_ep_no 最小）の episode_id を返す。全話が充足済み、またはエピソードが
    /// 0 件のときは null。クレジット話数コピーのコピー先デフォルト選定に使う。</summary>
    public async Task<int?> FindFirstEpisodeMissingCreditKindAsync(int seriesId, string creditKind, CancellationToken ct = default)
    {
        const string sql = """
            SELECT e.episode_id
            FROM episodes e
            LEFT JOIN credits c
              ON c.episode_id = e.episode_id AND c.credit_kind = @creditKind AND c.is_deleted = 0
            WHERE e.series_id = @seriesId AND e.is_deleted = 0 AND c.credit_id IS NULL
            ORDER BY e.series_ep_no
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<int?>(sql, new { seriesId, creditKind }, ct).ConfigureAwait(false);
    }

    /// <summary>新規作成。</summary>
    public async Task<int> InsertAsync(Credit credit, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credits
              (scope_kind, series_id, episode_id, credit_kind, credit_seq, part_type, presentation,
               notes, created_by, updated_by)
            VALUES
              (@ScopeKind, @SeriesId, @EpisodeId, @CreditKind,
               (SELECT COALESCE(MAX(c2.credit_seq), 0) + 1
                  FROM (SELECT credit_seq, series_id, episode_id FROM credits) AS c2
                 WHERE (@SeriesId  IS NOT NULL AND c2.series_id  = @SeriesId)
                    OR (@EpisodeId IS NOT NULL AND c2.episode_id = @EpisodeId)),
               @PartType, @Presentation,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        return await ExecuteScalarAsync<int>(sql, credit, ct).ConfigureAwait(false);
    }

    /// <summary>更新。</summary>
    public async Task UpdateAsync(Credit credit, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE credits SET
              scope_kind    = @ScopeKind,
              series_id     = @SeriesId,
              episode_id    = @EpisodeId,
              credit_kind   = @CreditKind,
              part_type     = @PartType,
              presentation  = @Presentation,
              notes         = @Notes,
              updated_by    = @UpdatedBy,
              is_deleted    = @IsDeleted
            WHERE credit_id = @CreditId;
            """;

        await ExecuteAsync(sql, credit, ct).ConfigureAwait(false);
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int creditId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE credits SET is_deleted = 1, updated_by = @UpdatedBy WHERE credit_id = @CreditId;";
        await ExecuteAsync(sql, new { CreditId = creditId, UpdatedBy = updatedBy }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 同一スコープ内のクレジット一覧を、与えた順序で credit_seq=1,2,3,... に再採番する。
    /// UNIQUE 制約 (series_id, credit_seq) / (episode_id, credit_seq) のため
    /// 単純な値差し替えでは一時的な重複で衝突する。下位階層の
    /// <c>BulkUpdateSeqAsync</c> と同じく「全件を一意な退避値へ移動 → 本来値で
    /// 再採番」をトランザクション 1 本で実行する。退避値は credit_seq の
    /// smallint unsigned 上限（65535）に衝突しない 30000 台を用いる。
    /// </summary>
    public async Task BulkUpdateSeqAsync(
        IEnumerable<(int creditId, ushort creditSeq)> updates,
        CancellationToken ct = default)
    {
        if (updates is null) throw new ArgumentNullException(nameof(updates));
        var list = updates.ToList();
        if (list.Count == 0) return;
        if (list.Count > 100)
            throw new ArgumentException(
                "BulkUpdateSeqAsync: 1 スコープあたり 100 クレジットを超える並べ替えは想定していません。",
                nameof(updates));

        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1 段階目：対象行に一意な退避値（30000, 30001, ...）を割り当てて UNIQUE 衝突回避。
            int i = 0;
            foreach (var u in list)
            {
                int tempVal = 30000 + i;
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credits SET credit_seq = @TempVal WHERE credit_id = @CreditId;",
                    new { TempVal = tempVal, CreditId = u.creditId },
                    transaction: tx, cancellationToken: ct));
                i++;
            }
            // 2 段階目：本来の値で再採番。
            foreach (var u in list)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credits SET credit_seq = @CreditSeq WHERE credit_id = @CreditId;",
                    new { CreditSeq = u.creditSeq, CreditId = u.creditId },
                    transaction: tx, cancellationToken: ct));
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
