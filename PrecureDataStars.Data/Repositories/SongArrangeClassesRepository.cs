using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>song_arrange_classes テーブル（曲のアレンジ種別マスタ）の読み取りリポジトリ。 監査列（created_by / updated_by）を持たない 4 列構成のため、 6 列構成前提の <see cref="SimpleCodeMasterRepository{T}"/> には乗せず個別実装のままとする。</summary>
public sealed class SongArrangeClassesRepository : RepositoryBase
{
    /// <summary><see cref="SongArrangeClassesRepository"/> の新しいインスタンスを生成する。</summary>
    public SongArrangeClassesRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>全件取得（display_order 昇順）。</summary>
    public async Task<IReadOnlyList<SongArrangeClass>> GetAllAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
              class_code    AS ClassCode,
              name_ja       AS NameJa,
              name_en       AS NameEn,
              display_order AS DisplayOrder
            FROM song_arrange_classes
            ORDER BY COALESCE(display_order, 255), class_code;
            """;

        return await QueryListAsync<SongArrangeClass>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>UPSERT（MastersEditor から利用）。</summary>
    public async Task UpsertAsync(SongArrangeClass kind, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO song_arrange_classes (class_code, name_ja, name_en, display_order)
            VALUES (@ClassCode, @NameJa, @NameEn, @DisplayOrder)
            ON DUPLICATE KEY UPDATE
              name_ja = VALUES(name_ja),
              name_en = VALUES(name_en),
              display_order = VALUES(display_order);
            """;

        await ExecuteAsync(sql, kind, ct).ConfigureAwait(false);
    }

    /// <summary>指定コードのマスタを削除する。</summary>
    public async Task DeleteAsync(string classCode, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM song_arrange_classes WHERE class_code = @ClassCode;";
        await ExecuteAsync(sql, new { ClassCode = classCode }, ct).ConfigureAwait(false);
    }
}
