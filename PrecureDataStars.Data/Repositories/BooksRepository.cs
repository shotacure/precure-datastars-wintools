using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Data.Repositories;

/// <summary>
/// books テーブル（書籍）の CRUD リポジトリ。
/// 書籍本体の一覧取得・検索・追加・更新・論理削除に加えて、
/// 付随する多対多（<c>book_series</c> / <c>book_genre_links</c>）とクレジット（<c>book_credits</c>）の
/// 全置換保存を提供する。is_deleted=1 の行は既定で除外される。
/// <para>
/// 並び順の既定は「発売日昇順・同日内は book_id 昇順」。商品と同じく、時系列で埋めていく
/// データ入力運用に合わせている。
/// </para>
/// <para>
/// 表紙画像 URL 列は汎用の <see cref="UpdateAsync"/> では触らない（編集フォームの保存で
/// 取得済み画像を消さないため）。更新は <see cref="UpdateCoverImagesAsync"/> /
/// <see cref="UpdateCoverImageSelectionAsync"/> の専用経路のみ。ProductsRepository と同じ流儀。
/// </para>
/// </summary>
public sealed class BooksRepository : RepositoryBase
{
    /// <summary><see cref="BooksRepository"/> の新しいインスタンスを生成する。</summary>
    /// <param name="factory">DB 接続ファクトリ。</param>
    public BooksRepository(IConnectionFactory factory) : base(factory) { }

    // SELECT 列は SQL 側で列別名を揃える。シリーズ・ジャンルは多対多のため本 SELECT には含めない。
    private const string SelectColumns = """
          book_id                       AS BookId,
          title                         AS Title,
          title_kana                    AS TitleKana,
          title_en                      AS TitleEn,
          publisher_product_company_id  AS PublisherProductCompanyId,
          release_date                  AS ReleaseDate,
          release_date_kindle           AS ReleaseDateKindle,
          isbn13                        AS Isbn13,
          c_code                        AS CCode,
          magazine_code                 AS MagazineCode,
          periodical_code               AS PeriodicalCode,
          page_count                    AS PageCount,
          binding_text                  AS BindingText,
          trim_size                     AS TrimSize,
          price_ex_tax                  AS PriceExTax,
          price_inc_tax                 AS PriceIncTax,
          price_kindle_inc_tax          AS PriceKindleIncTax,
          has_print                     AS HasPrint,
          has_kindle                    AS HasKindle,
          amazon_asin_print             AS AmazonAsinPrint,
          amazon_asin_kindle            AS AmazonAsinKindle,
          cover_image_url_print         AS CoverImageUrlPrint,
          cover_image_url_kindle        AS CoverImageUrlKindle,
          cover_image_source            AS CoverImageSource,
          cover_image_show_both         AS CoverImageShowBoth,
          cover_image_fetched_at        AS CoverImageFetchedAt,
          notes                         AS Notes,
          official_url                  AS OfficialUrl,
          created_at                    AS CreatedAt,
          updated_at                    AS UpdatedAt,
          created_by                    AS CreatedBy,
          updated_by                    AS UpdatedBy,
          is_deleted                    AS IsDeleted
        """;

    /// <summary>全書籍を取得する（発売日昇順、同一日内は book_id 昇順）。</summary>
    /// <param name="includeDeleted">true の場合、論理削除済みも含める。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<IReadOnlyList<Book>> GetAllAsync(bool includeDeleted = false, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM books
            {(includeDeleted ? "" : "WHERE is_deleted = 0")}
            ORDER BY release_date ASC, book_id ASC;
            """;

        return await QueryListAsync<Book>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>書籍 ID で 1 件取得する。</summary>
    public async Task<Book?> GetByIdAsync(int bookId, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM books
            WHERE book_id = @bookId
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<Book>(sql, new { bookId }, ct).ConfigureAwait(false);
    }

    /// <summary>ISBN-13 で 1 件取得する。Amazon 取り込み時の重複登録チェックに使う。</summary>
    public async Task<Book?> GetByIsbn13Async(string isbn13, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM books
            WHERE isbn13 = @isbn13
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<Book>(sql, new { isbn13 }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// ASIN（紙 / Kindle のどちらの列でも）で 1 件取得する。
    /// Amazon 取り込みで「その ASIN は既に登録済みか」を判定するために使う。
    /// </summary>
    public async Task<Book?> GetByAsinAsync(string asin, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM books
            WHERE amazon_asin_print = @asin OR amazon_asin_kindle = @asin
            ORDER BY book_id
            LIMIT 1;
            """;

        return await QuerySingleOrDefaultAsync<Book>(sql, new { asin }, ct).ConfigureAwait(false);
    }

    /// <summary>キーワード部分一致で書籍を検索する。検索対象は書名・読み・英題・ISBN。</summary>
    public async Task<IReadOnlyList<Book>> SearchByTitleAsync(string keyword, CancellationToken ct = default)
    {
        string sql = $"""
            SELECT {SelectColumns}
            FROM books
            WHERE is_deleted = 0
              AND (title LIKE @kw
                OR title_kana LIKE @kw
                OR title_en LIKE @kw
                OR isbn13 LIKE @kw
                OR magazine_code LIKE @kw
                OR periodical_code LIKE @kw)
            ORDER BY release_date DESC, book_id
            LIMIT 200;
            """;

        return await QueryListAsync<Book>(sql, new { kw = $"%{keyword}%" }, ct).ConfigureAwait(false);
    }

    /// <summary>書籍を新規作成し、採番された book_id を返す。</summary>
    public async Task<int> InsertAsync(Book book, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO books
              (title, title_kana, title_en, publisher_product_company_id,
               release_date, release_date_kindle, isbn13, c_code, magazine_code, periodical_code, page_count,
               binding_text, trim_size,
               price_ex_tax, price_inc_tax, price_kindle_inc_tax,
               has_print, has_kindle,
               amazon_asin_print, amazon_asin_kindle,
               notes, official_url, created_by, updated_by)
            VALUES
              (@Title, @TitleKana, @TitleEn, @PublisherProductCompanyId,
               @ReleaseDate, @ReleaseDateKindle, @Isbn13, @CCode, @MagazineCode, @PeriodicalCode, @PageCount,
               @BindingText, @TrimSize,
               @PriceExTax, @PriceIncTax, @PriceKindleIncTax,
               @HasPrint, @HasKindle,
               @AmazonAsinPrint, @AmazonAsinKindle,
               @Notes, @OfficialUrl, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;

        int id = await ExecuteScalarAsync<int>(sql, book, ct).ConfigureAwait(false);
        book.BookId = id;
        return id;
    }

    /// <summary>書籍情報を更新する（book_id で UPDATE）。表紙画像列は触らない。</summary>
    public async Task UpdateAsync(Book book, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE books SET
              title                        = @Title,
              title_kana                   = @TitleKana,
              title_en                     = @TitleEn,
              publisher_product_company_id = @PublisherProductCompanyId,
              release_date                 = @ReleaseDate,
              release_date_kindle          = @ReleaseDateKindle,
              isbn13                       = @Isbn13,
              c_code                       = @CCode,
              magazine_code                = @MagazineCode,
              periodical_code              = @PeriodicalCode,
              page_count                   = @PageCount,
              binding_text                 = @BindingText,
              trim_size                    = @TrimSize,
              price_ex_tax                 = @PriceExTax,
              price_inc_tax                = @PriceIncTax,
              price_kindle_inc_tax         = @PriceKindleIncTax,
              has_print                    = @HasPrint,
              has_kindle                   = @HasKindle,
              amazon_asin_print            = @AmazonAsinPrint,
              amazon_asin_kindle           = @AmazonAsinKindle,
              -- cover_image_* は本汎用更新では触らない（編集フォームの保存で
              -- 取得済み画像 URL を誤って消さないため）。更新は専用メソッド経由。
              notes                        = @Notes,
              official_url                 = @OfficialUrl,
              updated_by                   = @UpdatedBy,
              is_deleted                   = @IsDeleted
            WHERE book_id = @BookId;
            """;

        await ExecuteAsync(sql, book, ct).ConfigureAwait(false);
    }

    /// <summary>表紙画像のキャッシュ情報（紙・Kindle 両 URL・採用ソース・取得日時）を更新する。 画像取得タスク（Catalog の手動操作や AmazonSync バッチ）から呼ぶ専用メソッド。 採用ソースの取り得る値は <c>amazon_print</c> / <c>amazon_kindle</c> / null（未選択）。</summary>
    public async Task UpdateCoverImagesAsync(
        int bookId,
        string? coverImageUrlPrint,
        string? coverImageUrlKindle,
        string? coverImageSource,
        DateTime fetchedAt,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE books SET
              cover_image_url_print  = @CoverImageUrlPrint,
              cover_image_url_kindle = @CoverImageUrlKindle,
              cover_image_source     = @CoverImageSource,
              cover_image_fetched_at = @FetchedAt
            WHERE book_id = @BookId;
            """;

        await ExecuteAsync(sql, new
        {
            BookId = bookId,
            CoverImageUrlPrint = coverImageUrlPrint,
            CoverImageUrlKindle = coverImageUrlKindle,
            CoverImageSource = coverImageSource,
            FetchedAt = fetchedAt
        }, ct).ConfigureAwait(false);
    }

    /// <summary>表示に採用する表紙画像ソース（代表）と「詳細で両方表示するか」だけを更新する。 既に両 URL は保存済みの前提で、採用フラグのみ切り替える（URL・取得日時は触らない）。</summary>
    public async Task UpdateCoverImageSelectionAsync(
        int bookId,
        string? coverImageSource,
        bool coverImageShowBoth,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE books SET
              cover_image_source    = @CoverImageSource,
              cover_image_show_both = @CoverImageShowBoth
            WHERE book_id = @BookId;
            """;

        await ExecuteAsync(sql, new
        {
            BookId = bookId,
            CoverImageSource = coverImageSource,
            CoverImageShowBoth = coverImageShowBoth
        }, ct).ConfigureAwait(false);
    }

    /// <summary>論理削除（is_deleted=1）。</summary>
    public async Task SoftDeleteAsync(int bookId, string? updatedBy, CancellationToken ct = default)
    {
        const string sql = "UPDATE books SET is_deleted = 1, updated_by = @UpdatedBy WHERE book_id = @BookId;";
        await ExecuteAsync(sql, new { BookId = bookId, UpdatedBy = updatedBy }, ct).ConfigureAwait(false);
    }

    // ── 多対多・クレジット ──
    // いずれも「全件ロード」と「1 書籍分の全置換」の 2 つを提供する。全置換にしているのは、
    // 編集 UI 側が「行の集合」を丸ごと持って保存する作りだから（差分計算を UI に持ち込まない）。

    /// <summary>全書籍分のシリーズ対応を取得する（SiteBuilder の一括ロード用）。</summary>
    public async Task<IReadOnlyList<BookSeriesLink>> GetAllSeriesLinksAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT book_id AS BookId, series_id AS SeriesId, display_order AS DisplayOrder
            FROM book_series
            ORDER BY book_id, display_order, series_id;
            """;

        return await QueryListAsync<BookSeriesLink>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>1 書籍分のシリーズ対応を取得する。</summary>
    public async Task<IReadOnlyList<BookSeriesLink>> GetSeriesLinksAsync(int bookId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT book_id AS BookId, series_id AS SeriesId, display_order AS DisplayOrder
            FROM book_series
            WHERE book_id = @bookId
            ORDER BY display_order, series_id;
            """;

        return await QueryListAsync<BookSeriesLink>(sql, new { bookId }, ct).ConfigureAwait(false);
    }

    /// <summary>全書籍分のジャンル対応を取得する（SiteBuilder の一括ロード用）。</summary>
    public async Task<IReadOnlyList<BookGenreLink>> GetAllGenreLinksAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT l.book_id AS BookId, l.genre_code AS GenreCode, l.is_primary AS IsPrimary
            FROM book_genre_links l
            JOIN book_genres g ON g.genre_code = l.genre_code
            ORDER BY l.book_id, l.is_primary DESC, g.display_order;
            """;

        return await QueryListAsync<BookGenreLink>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>1 書籍分のジャンル対応を取得する。</summary>
    public async Task<IReadOnlyList<BookGenreLink>> GetGenreLinksAsync(int bookId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT l.book_id AS BookId, l.genre_code AS GenreCode, l.is_primary AS IsPrimary
            FROM book_genre_links l
            JOIN book_genres g ON g.genre_code = l.genre_code
            WHERE l.book_id = @bookId
            ORDER BY l.is_primary DESC, g.display_order;
            """;

        return await QueryListAsync<BookGenreLink>(sql, new { bookId }, ct).ConfigureAwait(false);
    }

    /// <summary>全書籍分のクレジットを取得する（SiteBuilder の一括ロード用）。役職の表示順で並べる。</summary>
    public async Task<IReadOnlyList<BookCredit>> GetAllCreditsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.book_credit_id     AS BookCreditId,
                   c.book_id            AS BookId,
                   c.role_code          AS RoleCode,
                   c.person_alias_id    AS PersonAliasId,
                   c.credit_text        AS CreditText,
                   c.display_order      AS DisplayOrder,
                   c.amazon_source_role AS AmazonSourceRole,
                   c.notes              AS Notes,
                   c.created_at         AS CreatedAt,
                   c.updated_at         AS UpdatedAt,
                   c.created_by         AS CreatedBy,
                   c.updated_by         AS UpdatedBy,
                   c.is_deleted         AS IsDeleted
            FROM book_credits c
            JOIN book_credit_roles r ON r.role_code = c.role_code
            WHERE c.is_deleted = 0
            ORDER BY c.book_id, r.display_order, c.display_order, c.book_credit_id;
            """;

        return await QueryListAsync<BookCredit>(sql, ct: ct).ConfigureAwait(false);
    }

    /// <summary>1 書籍分のクレジットを取得する。</summary>
    public async Task<IReadOnlyList<BookCredit>> GetCreditsAsync(int bookId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.book_credit_id     AS BookCreditId,
                   c.book_id            AS BookId,
                   c.role_code          AS RoleCode,
                   c.person_alias_id    AS PersonAliasId,
                   c.credit_text        AS CreditText,
                   c.display_order      AS DisplayOrder,
                   c.amazon_source_role AS AmazonSourceRole,
                   c.notes              AS Notes,
                   c.created_at         AS CreatedAt,
                   c.updated_at         AS UpdatedAt,
                   c.created_by         AS CreatedBy,
                   c.updated_by         AS UpdatedBy,
                   c.is_deleted         AS IsDeleted
            FROM book_credits c
            JOIN book_credit_roles r ON r.role_code = c.role_code
            WHERE c.book_id = @bookId AND c.is_deleted = 0
            ORDER BY r.display_order, c.display_order, c.book_credit_id;
            """;

        return await QueryListAsync<BookCredit>(sql, new { bookId }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 1 書籍分のシリーズ対応・ジャンル対応・クレジットを 1 トランザクションで全置換する。
    /// 既存行を削除してから渡された集合を投入するため、呼び出し側は常に「保存後にあるべき全行」を渡す。
    /// <para>
    /// ジャンルの代表指定（is_primary）は本メソッド内で 1 件に正規化する。複数立っていれば先頭のみ、
    /// 1 件も立っていなければ先頭要素を代表にする（索引の代表ジャンルが必ず 1 つ決まるようにするため）。
    /// </para>
    /// </summary>
    /// <param name="bookId">対象書籍 ID。</param>
    /// <param name="seriesIds">紐付けるシリーズ ID（渡された順が display_order になる）。空ならオールスターズ扱い。</param>
    /// <param name="genreCodes">紐付けるジャンルコード。</param>
    /// <param name="primaryGenreCode">代表ジャンル。null または <paramref name="genreCodes"/> に無い値なら先頭を代表にする。</param>
    /// <param name="credits">クレジット行（渡された順が同一役職内の display_order になる）。</param>
    /// <param name="updatedBy">更新ユーザー。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ReplaceRelationsAsync(
        int bookId,
        IReadOnlyList<int> seriesIds,
        IReadOnlyList<string> genreCodes,
        string? primaryGenreCode,
        IReadOnlyList<BookCredit> credits,
        string? updatedBy,
        CancellationToken ct = default)
    {
        await using var conn = await Factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM book_series WHERE book_id = @BookId;", new { BookId = bookId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM book_genre_links WHERE book_id = @BookId;", new { BookId = bookId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM book_credits WHERE book_id = @BookId;", new { BookId = bookId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            int order = 1;
            foreach (int seriesId in seriesIds)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO book_series (book_id, series_id, display_order) VALUES (@BookId, @SeriesId, @DisplayOrder);",
                    new { BookId = bookId, SeriesId = seriesId, DisplayOrder = order++ },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            // 代表ジャンルの正規化。指定が無効なら先頭を代表に繰り上げる。
            string? primary = primaryGenreCode;
            if (primary == null || !genreCodes.Contains(primary, StringComparer.Ordinal))
                primary = genreCodes.Count > 0 ? genreCodes[0] : null;

            foreach (string genreCode in genreCodes)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO book_genre_links (book_id, genre_code, is_primary) VALUES (@BookId, @GenreCode, @IsPrimary);",
                    new { BookId = bookId, GenreCode = genreCode, IsPrimary = string.Equals(genreCode, primary, StringComparison.Ordinal) },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            int creditOrder = 1;
            foreach (var c in credits)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO book_credits
                      (book_id, role_code, person_alias_id, credit_text, display_order,
                       amazon_source_role, notes, created_by, updated_by)
                    VALUES
                      (@BookId, @RoleCode, @PersonAliasId, @CreditText, @DisplayOrder,
                       @AmazonSourceRole, @Notes, @UpdatedBy, @UpdatedBy);
                    """,
                    new
                    {
                        BookId = bookId,
                        c.RoleCode,
                        c.PersonAliasId,
                        c.CreditText,
                        DisplayOrder = creditOrder++,
                        c.AmazonSourceRole,
                        c.Notes,
                        UpdatedBy = updatedBy
                    },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
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
