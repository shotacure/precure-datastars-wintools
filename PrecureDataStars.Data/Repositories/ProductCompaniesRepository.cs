using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// product_companies テーブル（商品の社名マスタ）の CRUD リポジトリ。
/// <para>
/// 商品（products）の発売元（label）・販売元（distributor）として紐付けるための、
/// クレジット非依存の社名マスタを操作する。1 社 = 1 行・和名/かな/英名のみのシンプル構造で、
/// 屋号系譜は持たない。
/// </para>
/// <para>
/// is_deleted=1 の行は <see cref="GetAllAsync"/> の既定では除外される。
/// SoftDelete 経路は他リポジトリと同様に updated_by の更新も併せて行う。
/// </para>
/// <para>
/// 同 stage 確定版で <c>is_default_label</c> / <c>is_default_distributor</c> 列を扱う。
/// フラグ排他性（同フラグが立つ行はマスタ全体で最大 1 行）は本リポジトリの
/// <see cref="InsertAsync"/> / <see cref="UpdateAsync"/> 内でトランザクション処理する
/// （対象行に立てる前に他の全行のフラグを 0 に落とす）。
/// </para>
/// </summary>
public sealed class ProductCompaniesRepository
{
    private readonly IConnectionFactory _factory;

    /// <summary>
    /// <see cref="ProductCompaniesRepository"/> の新しいインスタンスを生成する。
    /// </summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public ProductCompaniesRepository(IConnectionFactory factory)
        => _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    // SELECT 列は SQL 側で一致させる。Dapper の自動マッピング前提。
    // is_default_label / is_default_distributor 追加。
    private const string SelectColumns = """
          product_company_id     AS ProductCompanyId,
          name_ja                AS NameJa,
          name_kana              AS NameKana,
          name_en                AS NameEn,
          is_default_label       AS IsDefaultLabel,
          is_default_distributor AS IsDefaultDistributor,
          notes                  AS Notes,
          created_at             AS CreatedAt,
          updated_at             AS UpdatedAt,
          created_by             AS CreatedBy,
          updated_by             AS UpdatedBy,
          is_deleted             AS IsDeleted
        """;

    /// <summary>
    /// 全社名を取得する（かな昇順、かな未登録は和名でフォールバック並び）。
    /// SiteBuilder の起動時メモリロード、Catalog UI の編集一覧の双方で利用する。
    /// </summary>
    /// <param name="includeDeleted">true の場合、論理削除済みも含める。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<ProductCompany>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM product_companies
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY COALESCE(name_kana, name_ja) ASC, product_company_id ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<ProductCompany>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>主キーで 1 件取得する（論理削除済みも含めて返す）。</summary>
    public async Task<ProductCompany?> GetByIdAsync(int productCompanyId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM product_companies
            WHERE product_company_id = @productCompanyId
            LIMIT 1;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<ProductCompany>(
            new CommandDefinition(sql, new { productCompanyId }, cancellationToken: ct));
    }

    /// <summary>
    /// 部分一致検索（picker の入力補助向け）。和名・かな・英名のいずれかにキーワードを含む行を返す。
    /// </summary>
    public async Task<IReadOnlyList<ProductCompany>> SearchAsync(string keyword, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM product_companies
            WHERE is_deleted = 0
              AND (name_ja   LIKE @kw
                OR name_kana LIKE @kw
                OR name_en   LIKE @kw)
            ORDER BY COALESCE(name_kana, name_ja) ASC, product_company_id ASC
            LIMIT 200;
            """;

        var param = new { kw = $"%{keyword}%" };
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<ProductCompany>(new CommandDefinition(sql, param, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// レーベル既定として指定されている社を 1 件返す。フラグが立っていなければ null。
    /// 複数立っていた場合（運用ミス）は <c>product_company_id</c> が小さい方を採用する。
    /// NewProductDialog 起動時の既定値取得に使う。
    /// </summary>
    public async Task<ProductCompany?> GetDefaultLabelAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM product_companies
            WHERE is_default_label = 1
              AND is_deleted = 0
            ORDER BY product_company_id ASC
            LIMIT 1;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<ProductCompany>(
            new CommandDefinition(sql, cancellationToken: ct));
    }

    /// <summary>
    /// 販売元既定として指定されている社を 1 件返す。フラグが立っていなければ null。
    /// 複数立っていた場合は <c>product_company_id</c> が小さい方を採用する。
    /// </summary>
    public async Task<ProductCompany?> GetDefaultDistributorAsync(CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM product_companies
            WHERE is_default_distributor = 1
              AND is_deleted = 0
            ORDER BY product_company_id ASC
            LIMIT 1;
            """;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<ProductCompany>(
            new CommandDefinition(sql, cancellationToken: ct));
    }

    /// <summary>
    /// 新規登録。AUTO_INCREMENT で割り当てられた ID を返す。
    /// <see cref="ProductCompany.IsDefaultLabel"/> / <see cref="ProductCompany.IsDefaultDistributor"/>
    /// が true で渡された場合、同じトランザクション内で他の全行の対応フラグを 0 に落としてから
    /// 新規行を INSERT する（マスタ全体で 1 行のみが既定となる排他性を担保）。
    /// </summary>
    public async Task<int> InsertAsync(ProductCompany pc, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 排他処理：新規行に既定フラグを立てる場合、他の全行を 0 に落とす。
            // INSERT 前なので「他の全行」は文字通り「テーブル全体」を意味する。
            await ClearDefaultFlagsIfNeededAsync(conn, tx, pc, excludeId: null, ct).ConfigureAwait(false);

            const string sql = """
                INSERT INTO product_companies
                  (name_ja, name_kana, name_en,
                   is_default_label, is_default_distributor,
                   notes, created_by, updated_by)
                VALUES
                  (@NameJa, @NameKana, @NameEn,
                   @IsDefaultLabel, @IsDefaultDistributor,
                   @Notes, @CreatedBy, @UpdatedBy);
                SELECT LAST_INSERT_ID();
                """;

            int newId = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(sql, pc, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return newId;
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 既存社名の更新（product_company_id で UPDATE）。
    /// <see cref="ProductCompany.IsDefaultLabel"/> / <see cref="ProductCompany.IsDefaultDistributor"/>
    /// が true で渡された場合、同じトランザクション内で「対象 ID 以外の全行」の対応フラグを 0 に
    /// 落としてから UPDATE する（排他性の担保）。
    /// </summary>
    public async Task UpdateAsync(ProductCompany pc, CancellationToken ct = default)
    {
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 排他処理：対象行に既定フラグを立てる場合、対象 ID 以外の全行を 0 に落とす。
            // 対象 ID 自体は除外しないと、続く UPDATE で 1 に戻すまでに同フラグが衝突しうる。
            await ClearDefaultFlagsIfNeededAsync(conn, tx, pc, excludeId: pc.ProductCompanyId, ct).ConfigureAwait(false);

            const string sql = """
                UPDATE product_companies SET
                  name_ja                = @NameJa,
                  name_kana              = @NameKana,
                  name_en                = @NameEn,
                  is_default_label       = @IsDefaultLabel,
                  is_default_distributor = @IsDefaultDistributor,
                  notes                  = @Notes,
                  updated_by             = @UpdatedBy,
                  is_deleted             = @IsDeleted
                WHERE product_company_id = @ProductCompanyId;
                """;

            await conn.ExecuteAsync(
                new CommandDefinition(sql, pc, transaction: tx, cancellationToken: ct));

            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 既定フラグの排他処理ヘルパ。
    /// 対象 <see cref="ProductCompany"/> で立てようとしているフラグについて、テーブル内の
    /// 他の行に立っているものを 0 に落とす。<paramref name="excludeId"/> が指定されていれば
    /// その ID 自身は除外する（UPDATE 経路で「自分自身を巻き込まない」ようにする用途）。
    /// 立てる予定が無いフラグ（pc 側が false）は何もしない。
    /// </summary>
    private static async Task ClearDefaultFlagsIfNeededAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        ProductCompany pc,
        int? excludeId,
        CancellationToken ct)
    {
        if (pc.IsDefaultLabel)
        {
            string sql = excludeId.HasValue
                ? "UPDATE product_companies SET is_default_label = 0 WHERE product_company_id <> @ExcludeId;"
                : "UPDATE product_companies SET is_default_label = 0;";
            await conn.ExecuteAsync(new CommandDefinition(
                sql, new { ExcludeId = excludeId ?? 0 }, transaction: tx, cancellationToken: ct));
        }
        if (pc.IsDefaultDistributor)
        {
            string sql = excludeId.HasValue
                ? "UPDATE product_companies SET is_default_distributor = 0 WHERE product_company_id <> @ExcludeId;"
                : "UPDATE product_companies SET is_default_distributor = 0;";
            await conn.ExecuteAsync(new CommandDefinition(
                sql, new { ExcludeId = excludeId ?? 0 }, transaction: tx, cancellationToken: ct));
        }
    }

    /// <summary>論理削除（is_deleted=1）。FK 側は ON DELETE SET NULL なので物理削除は不要。</summary>
    public async Task SoftDeleteAsync(int productCompanyId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE product_companies SET is_deleted = 1, updated_by = @UpdatedBy WHERE product_company_id = @ProductCompanyId;";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { ProductCompanyId = productCompanyId, UpdatedBy = updatedBy }, cancellationToken: ct));
    }
}