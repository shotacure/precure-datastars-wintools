using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// credits テーブル（クレジット本体）の CRUD リポジトリ。
/// <para>
/// シリーズ単位 or エピソード単位で OP/ED 各 1 件まで保持できる
/// （UNIQUE は <c>(series_id, credit_kind)</c> と <c>(episode_id, credit_kind)</c> の 2 本）。
/// scope_kind と series_id / episode_id の整合性は DB 側のトリガー
/// <c>trg_credits_b{i,u}_scope_consistency</c> で担保される（CHECK は MySQL 8.0 の
/// FK 参照アクション制約 Error 3823 を回避するため使用しない）。
/// </para>
/// <para>
/// 本放送と円盤・配信での差し替え（ロゴバージョン違い等）はクレジット単位ではなく
/// エントリ単位（<see cref="CreditBlockEntry.IsBroadcastOnly"/>）で扱う。
/// クレジット本体には is_broadcast_only 関連カラムを持たない。
/// </para>
/// </summary>
public sealed class CreditsRepository
{
    private readonly IConnectionFactory _factory;

    public CreditsRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    private const string SelectColumns = """
          credit_id     AS CreditId,
          scope_kind    AS ScopeKind,
          series_id     AS SeriesId,
          episode_id    AS EpisodeId,
          credit_kind   AS CreditKind,
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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<Credit>(
            new CommandDefinition(sql, new { creditId }, cancellationToken: ct));
    }

    /// <summary>指定シリーズに紐付くクレジット（scope=SERIES）一覧を取得する（credit_kind 昇順）。</summary>
    public async Task<IReadOnlyList<Credit>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credits
            WHERE series_id = @seriesId AND is_deleted = 0
            ORDER BY credit_kind;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Credit>(new CommandDefinition(sql, new { seriesId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>指定エピソードに紐付くクレジット（scope=EPISODE）一覧を取得する（credit_kind 昇順）。</summary>
    public async Task<IReadOnlyList<Credit>> GetByEpisodeAsync(int episodeId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM credits
            WHERE episode_id = @episodeId AND is_deleted = 0
            ORDER BY credit_kind;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Credit>(new CommandDefinition(sql, new { episodeId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>新規作成。AUTO_INCREMENT の credit_id を返す。</summary>
    public async Task<int> InsertAsync(Credit credit, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO credits
              (scope_kind, series_id, episode_id, credit_kind, part_type, presentation,
               notes, created_by, updated_by)
            VALUES
              (@ScopeKind, @SeriesId, @EpisodeId, @CreditKind, @PartType, @Presentation,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, credit, cancellationToken: ct));
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

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, credit, cancellationToken: ct));
    }

    /// <summary>論理削除。</summary>
    public async Task SoftDeleteAsync(int creditId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE credits SET is_deleted = 1, updated_by = @UpdatedBy WHERE credit_id = @CreditId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { CreditId = creditId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}