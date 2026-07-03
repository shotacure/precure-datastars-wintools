using PrecureDataStars.Data.Db;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// 「コード + 日英名 + display_order + 監査列」だけを持つ単純コード型マスタの共通 CRUD 基底。
/// DiscKinds / SongMusicClasses / SongSizeVariants / SongPartVariants / TrackContentKinds /
/// ProductKinds の 6 リポジトリがテーブル名・コード列名以外まったく同一の
/// GetAll / Upsert / Delete を個別に持っていたため単一実装へ集約したもの。
/// テーブル名・コード列名・モデル側プロパティ名は派生クラスの抽象プロパティで注入する
/// （SQL へ補間されるのは派生クラス内の定数のみで、外部入力は流れない）。
/// 列構成が異なるマスタ（例: 監査列を持たない song_arrange_classes）や
/// 追加メソッドを持つマスタ（series_kinds / credit_kinds 等）は本基底の対象外。
/// </summary>
/// <typeparam name="T">対応するモデル型（例: <c>DiscKind</c>）。</typeparam>
public abstract class SimpleCodeMasterRepository<T> : RepositoryBase
{
    protected SimpleCodeMasterRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>テーブル名（例: "disc_kinds"）。</summary>
    protected abstract string Table { get; }

    /// <summary>主キーのコード列名（例: "kind_code"）。</summary>
    protected abstract string CodeColumn { get; }

    /// <summary>コード列に対応するモデル側プロパティ名（例: "KindCode"。SELECT の AS 名 / Dapper パラメータ名）。</summary>
    protected abstract string CodeProperty { get; }

    /// <summary>全件取得する（display_order 昇順、未設定は 255 扱いで末尾、同順はコード順）。</summary>
    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT
              {CodeColumn}  AS {CodeProperty},
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder,
              created_by    AS CreatedBy,
              updated_by    AS UpdatedBy
            FROM {Table}
            ORDER BY COALESCE(display_order, 255), {CodeColumn};
            """;

        return await QueryListAsync<T>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>UPSERT（MastersEditor から利用）。</summary>
    public async Task UpsertAsync(T row, CancellationToken ct = default)
    {
        string sql = $"""
            INSERT INTO {Table} ({CodeColumn}, name_ja, name_en, display_order, created_by, updated_by)
            VALUES (@{CodeProperty}, @NameJa, @NameEn, @DisplayOrder, @CreatedBy, @UpdatedBy)
            ON DUPLICATE KEY UPDATE
              name_ja = VALUES(name_ja),
              name_en = VALUES(name_en),
              display_order = VALUES(display_order),
              updated_by = VALUES(updated_by);
            """;

        await ExecuteAsync(sql, row, ct).ConfigureAwait(false);
    }

    /// <summary>指定コードのマスタを削除する。</summary>
    public async Task DeleteAsync(string code, CancellationToken ct = default)
    {
        string sql = $"DELETE FROM {Table} WHERE {CodeColumn} = @code;";
        await ExecuteAsync(sql, new { code }, ct).ConfigureAwait(false);
    }
}
