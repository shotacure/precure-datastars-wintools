using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// series_precures テーブル（シリーズ × プリキュアの多対多関連）の CRUD リポジトリ。
/// 1 プリキュアが複数シリーズに渡って登場するケース（クロスオーバー作品で別シリーズに登場、
/// 続編シリーズで引き続きレギュラー、変身前の姿で出てきて変身しない出演 等）に対応するため、
/// 純粋な多対多関連テーブルを扱う。論理削除フラグは持たない設計（紐付けは入れる／消すの 2 値で運用）。
/// 各メソッドは Dapper 経由の単純な CRUD。順序保証は <c>display_order ASC, precure_id ASC</c>
/// のタイブレーク（同シリーズ内表示順）と <c>series_id ASC, display_order ASC</c>
/// （プリキュア → シリーズ群の引き）の 2 通り。
/// </summary>
public sealed class SeriesPrecuresRepository : RepositoryBase
{
    public SeriesPrecuresRepository(IConnectionFactory factory) : base(factory) { }

    // SELECT 共通列（Dapper マッピング用エイリアス）。
    private const string SelectColumns = """
          series_id     AS SeriesId,
          precure_id    AS PrecureId,
          display_order AS DisplayOrder,
          created_at    AS CreatedAt,
          updated_at    AS UpdatedAt,
          created_by    AS CreatedBy,
          updated_by    AS UpdatedBy
        """;

    /// <summary>全件取得。並び順は <c>series_id ASC, display_order ASC, precure_id ASC</c>。 SiteBuilder が「全シリーズ × 全プリキュア」を一括ロードして in-memory グルーピングする 用途を想定。シリーズ別に絞らず素直に全件を返す。</summary>
    public async Task<IReadOnlyList<SeriesPrecure>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM series_precures
            ORDER BY series_id ASC, display_order ASC, precure_id ASC;
            """;

        return await QueryListAsync<SeriesPrecure>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>指定シリーズに紐付くプリキュアを表示順で取得する。 並び順は <c>display_order ASC, precure_id ASC</c>。</summary>
    /// <param name="seriesId">対象シリーズの ID。</param>
    public async Task<IReadOnlyList<SeriesPrecure>> GetBySeriesAsync(int seriesId, CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM series_precures
            WHERE series_id = @SeriesId
            ORDER BY display_order ASC, precure_id ASC;
            """;

        return await QueryListAsync<SeriesPrecure>(sql, new { SeriesId = seriesId }, ct).ConfigureAwait(false);
    }

    /// <summary>指定プリキュアが登場するシリーズ群を取得する（プリキュア → 複数シリーズの逆引き）。 並び順は <c>series_id ASC</c>（呼び出し側でシリーズの放送開始日順にソートし直すことを想定）。</summary>
    /// <param name="precureId">対象プリキュアの ID。</param>
    public async Task<IReadOnlyList<SeriesPrecure>> GetByPrecureAsync(int precureId, CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT {SelectColumns}
            FROM series_precures
            WHERE precure_id = @PrecureId
            ORDER BY series_id ASC;
            """;

        return await QueryListAsync<SeriesPrecure>(sql, new { PrecureId = precureId }, ct).ConfigureAwait(false);
    }

    /// <summary>紐付け 1 件を新規追加する。複合 PK 重複時は DB レベルで例外（DuplicateKeyException 相当）。</summary>
    public async Task AddAsync(SeriesPrecure entity, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO series_precures
              (series_id, precure_id, display_order, created_by, updated_by)
            VALUES
              (@SeriesId, @PrecureId, @DisplayOrder, @CreatedBy, @UpdatedBy);
            """;

        await ExecuteAsync(sql, entity, ct).ConfigureAwait(false);
    }

    /// <summary>紐付け 1 件の display_order を更新する（並び替え用途）。</summary>
    public async Task UpdateDisplayOrderAsync(int seriesId, int precureId, byte displayOrder, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE series_precures
               SET display_order = @DisplayOrder,
                   updated_by    = @UpdatedBy
             WHERE series_id  = @SeriesId
               AND precure_id = @PrecureId;
            """;

        await ExecuteAsync(sql, new
        {
            SeriesId = seriesId,
            PrecureId = precureId,
            DisplayOrder = displayOrder,
            UpdatedBy = updatedBy
        }, ct).ConfigureAwait(false);
    }

    /// <summary>紐付け 1 件を削除する（複合 PK 指定）。</summary>
    public async Task RemoveAsync(int seriesId, int precureId, CancellationToken ct = default)
    {
        const string sql = """
            DELETE FROM series_precures
             WHERE series_id  = @SeriesId
               AND precure_id = @PrecureId;
            """;

        await ExecuteAsync(sql, new { SeriesId = seriesId, PrecureId = precureId }, ct).ConfigureAwait(false);
    }
}
