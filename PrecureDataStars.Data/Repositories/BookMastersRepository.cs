using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// 書籍まわりのコードマスタ（<c>book_genres</c> / <c>book_credit_roles</c>）の読み取りリポジトリ。
/// どちらも件数が十数行で更新頻度が極めて低いため、参照系のみを提供する
/// （追加・改称はマイグレーション SQL 側で行う。product_kinds / disc_kinds と同じ運用）。
/// </summary>
public sealed class BookMastersRepository : RepositoryBase
{
    /// <summary><see cref="BookMastersRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public BookMastersRepository(IConnectionFactory factory) : base(factory) { }

    /// <summary>ジャンルマスタを表示順で全件取得する。</summary>
    public async Task<IReadOnlyList<BookGenre>> GetGenresAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT genre_code AS GenreCode, name_ja AS NameJa, name_en AS NameEn, display_order AS DisplayOrder
            FROM book_genres
            ORDER BY display_order;
            """;

        return await QueryListAsync<BookGenre>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>書籍役職マスタを表示順で全件取得する。</summary>
    public async Task<IReadOnlyList<BookCreditRole>> GetCreditRolesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT role_code AS RoleCode, name_ja AS NameJa, name_en AS NameEn,
                   amazon_role_type AS AmazonRoleType, display_order AS DisplayOrder
            FROM book_credit_roles
            ORDER BY display_order;
            """;

        return await QueryListAsync<BookCreditRole>(sql, ct: ct).ConfigureAwait(false);
    }
}
