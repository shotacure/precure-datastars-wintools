using PrecureDataStars.AmazonPaApi;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.AmazonSync;

/// <summary>
/// Amazon Creators API から取得した商品情報を <c>books</c> 系テーブルへ取り込むサービス。
/// 「Amazon の属性 → 書籍テーブルの列」のマッピングを 1 か所に集約する唯一の場所で、
/// Catalog 側に書籍登録 UI を足すときも本サービスと同じ対応表を使う。
/// <para>
/// 紙版 ASIN と Kindle 版 ASIN の両方（または片方）を受け取り、1 冊の <see cref="Book"/> に束ねる。
/// 書名・発売日・出版社などの重複する属性は紙版を優先する（紙が無い書籍のみ Kindle 側を採る）。
/// </para>
/// <para>
/// 出版社は <c>product_companies</c> の和名完全一致で解決する。一致しなければ NULL のままにして
/// 呼び出し側へ警告を返す（マスタを暗黙に増やさない。マスタ追加は人の判断を通す方針）。
/// </para>
/// <para>
/// 紙の価格は取り込まない。Creators API の <c>offersV2</c> が返すのは「現在の出品価格」であって
/// 定価ではなく、絶版書ではマーケットプレイスの中古値が乗るため。紙の定価は人手で入れる。
/// Kindle 価格は常に正価が返るので取り込む。
/// </para>
/// <para>
/// クレジットは <c>contributors[]</c> を役職マスタの <c>amazon_role_type</c> で突き合わせて役職を決め、
/// 名義は <c>person_aliases.name</c> の完全一致で人物マスタに紐付ける。一致しなければ
/// <c>credit_text</c> にフリーテキストとして残す（取りこぼしを捨てない）。
/// </para>
/// </summary>
public sealed class BookImportService
{
    private readonly PaApiClient _paApi;
    private readonly BooksRepository _booksRepo;
    private readonly BookMastersRepository _mastersRepo;
    private readonly ProductCompaniesRepository _productCompaniesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

    public BookImportService(
        PaApiClient paApi,
        BooksRepository booksRepo,
        BookMastersRepository mastersRepo,
        ProductCompaniesRepository productCompaniesRepo,
        PersonAliasesRepository personAliasesRepo)
    {
        _paApi = paApi;
        _booksRepo = booksRepo;
        _mastersRepo = mastersRepo;
        _productCompaniesRepo = productCompaniesRepo;
        _personAliasesRepo = personAliasesRepo;
    }

    /// <summary>
    /// ASIN から書籍 1 冊分の取り込み内容を組み立てる。DB への書き込みは行わない
    /// （<c>--dry-run</c> で内容を確認してから <see cref="SaveAsync"/> に渡す 2 段構え）。
    /// </summary>
    /// <param name="printAsin">紙版の ASIN（無ければ null）。</param>
    /// <param name="kindleAsin">Kindle 版の ASIN（無ければ null）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<BookImportPlan> BuildPlanAsync(string? printAsin, string? kindleAsin, CancellationToken ct = default)
    {
        var plan = new BookImportPlan();

        // 2 ASIN をまとめて 1 回の GetItems で取る（レート制限 1 TPS の節約）。
        var asins = new List<string>();
        if (!string.IsNullOrWhiteSpace(printAsin)) asins.Add(printAsin!);
        if (!string.IsNullOrWhiteSpace(kindleAsin)) asins.Add(kindleAsin!);
        if (asins.Count == 0)
        {
            plan.Errors.Add("ASIN が 1 つも指定されていません。");
            return plan;
        }

        var items = await _paApi.GetItemsAsync(asins, ct, PaResourceSet.Extended).ConfigureAwait(false);
        var itemByAsin = items.ToDictionary(i => i.Asin, StringComparer.OrdinalIgnoreCase);

        PaItem? print = printAsin != null && itemByAsin.TryGetValue(printAsin, out var pi) ? pi : null;
        PaItem? kindle = kindleAsin != null && itemByAsin.TryGetValue(kindleAsin, out var ki) ? ki : null;

        if (printAsin != null && print == null) plan.Errors.Add($"紙版 ASIN が Amazon で見つかりませんでした: {printAsin}");
        if (kindleAsin != null && kindle == null) plan.Errors.Add($"Kindle 版 ASIN が Amazon で見つかりませんでした: {kindleAsin}");
        if (plan.Errors.Count > 0) return plan;

        // 属性の主たる取得元。紙があれば紙、無ければ Kindle。
        PaItem primary = print ?? kindle!;

        // 既存登録の重複チェック。ISBN と ASIN の両方で引く。
        string? rawEan = print?.Ean ?? kindle?.Ean;
        string? isbn13 = NormalizeIsbn13(rawEan);
        if (isbn13 == null && !string.IsNullOrWhiteSpace(rawEan))
            plan.Warnings.Add($"Amazon の EAN『{rawEan}』は ISBN（978/979 始まり）ではないため isbn13 に入れませんでした。");
        if (isbn13 != null)
        {
            var byIsbn = await _booksRepo.GetByIsbn13Async(isbn13, ct).ConfigureAwait(false);
            if (byIsbn != null) plan.ExistingBookId = byIsbn.BookId;
        }
        if (plan.ExistingBookId == null)
        {
            foreach (var asin in asins)
            {
                var byAsin = await _booksRepo.GetByAsinAsync(asin, ct).ConfigureAwait(false);
                if (byAsin != null) { plan.ExistingBookId = byAsin.BookId; break; }
            }
        }

        // 出版社は和名の完全一致で解決する。見つからなければ NULL のまま警告を残す。
        var productCompanies = await _productCompaniesRepo.GetAllAsync(ct: ct).ConfigureAwait(false);
        string publisherName = primary.Manufacturer ?? primary.Brand ?? "";
        int? publisherId = null;
        if (publisherName.Length > 0)
        {
            var match = productCompanies.FirstOrDefault(c => string.Equals(c.NameJa, publisherName, StringComparison.Ordinal));
            if (match != null) publisherId = match.ProductCompanyId;
            else plan.Warnings.Add($"出版社『{publisherName}』は product_companies に未登録のため未設定にしました。");
        }

        DateTime? printDate = ParseAmazonDate(print?.PublicationDate ?? print?.ReleaseDate);
        DateTime? kindleDate = ParseAmazonDate(kindle?.PublicationDate ?? kindle?.ReleaseDate);
        DateTime? releaseDate = printDate ?? kindleDate;
        if (releaseDate == null)
        {
            plan.Errors.Add("発売日が Amazon から取得できませんでした（release_date は NOT NULL のため取り込めません）。");
            return plan;
        }

        plan.Book = new Book
        {
            Title = primary.Title,
            PublisherProductCompanyId = publisherId,
            ReleaseDate = releaseDate.Value,
            // 紙と電子で配信日が違うときだけ Kindle 側の日付を別列に残す。
            ReleaseDateKindle = (printDate != null && kindleDate != null && kindleDate != printDate) ? kindleDate : null,
            Isbn13 = isbn13,
            PageCount = print?.PagesCount is int pages and > 0 and <= ushort.MaxValue ? (ushort)pages : null,
            BindingText = print?.Binding ?? kindle?.Binding,
            // 紙の価格は Amazon から取り込まない。offersV2 が返すのは「現在の出品価格」であって
            // 定価ではなく、絶版書ではマーケットプレイスの中古値が乗って定価と大きくずれるため。
            // 紙の定価は人手で入れる運用とする。Kindle は常に正価が返るのでそのまま採る。
            PriceKindleIncTax = kindle?.PriceAmount,
            HasPrint = print != null,
            HasKindle = kindle != null,
            AmazonAsinPrint = print?.Asin,
            AmazonAsinKindle = kindle?.Asin,
            CoverImageUrlPrint = print?.LargeImageUrl,
            CoverImageUrlKindle = kindle?.LargeImageUrl,
            // 書影は Kindle を優先。電子は事業者アップの正規画像が確実で、紙（特に絶版書）は
            // 出品者の撮影画像が混ざるリスクがある。Kindle の画像が取れていなければ紙側を代表にする。
            CoverImageSource = !string.IsNullOrEmpty(kindle?.LargeImageUrl) ? "amazon_kindle"
                             : !string.IsNullOrEmpty(print?.LargeImageUrl) ? "amazon_print"
                             : null,
            CoverImageFetchedAt = (print?.LargeImageUrl ?? kindle?.LargeImageUrl) != null ? DateTime.Now : null
        };

        if (plan.Book.PageCount == null && print != null)
            plan.Warnings.Add("ページ数が Amazon から取得できませんでした。");
        if (string.IsNullOrEmpty(plan.Book.CoverImageUrlPrint) && string.IsNullOrEmpty(plan.Book.CoverImageUrlKindle))
            plan.Warnings.Add("表紙画像 URL が取得できませんでした（Amazon 側がプレースホルダ画像の可能性）。");

        plan.Credits = await BuildCreditsAsync(print, kindle, ct).ConfigureAwait(false);
        plan.BrowseNodes = primary.BrowseNodes;

        return plan;
    }

    /// <summary>
    /// 寄与者一覧をクレジット行へ変換する。紙版と Kindle 版で同じ人が重複することがあるため、
    /// 「役職 + 名前」で重複排除する。
    /// </summary>
    private async Task<List<BookCredit>> BuildCreditsAsync(PaItem? print, PaItem? kindle, CancellationToken ct)
    {
        var roles = await _mastersRepo.GetCreditRolesAsync(ct).ConfigureAwait(false);
        var roleByAmazonType = roles
            .Where(r => !string.IsNullOrEmpty(r.AmazonRoleType))
            .ToDictionary(r => r.AmazonRoleType!, StringComparer.OrdinalIgnoreCase);
        var roleByNameJa = roles.ToDictionary(r => r.NameJa, StringComparer.Ordinal);

        var aliases = await _personAliasesRepo.GetAllAsync(ct: ct).ConfigureAwait(false);
        // 同名 alias が複数ある場合は自動紐付けせず、フリーテキストに落として人の判断に回す。
        var aliasByName = aliases
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().AliasId, StringComparer.Ordinal);

        var result = new List<BookCredit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in (print?.Contributors ?? Array.Empty<PaContributor>())
                 .Concat(kindle?.Contributors ?? Array.Empty<PaContributor>()))
        {
            // roleType（"author" / "editor"）優先、無ければ表示ロール（"著" / "編集"）で引く。
            // どちらでも当たらなければ OTHER に落として取りこぼさない。
            BookCreditRole? role = null;
            if (!string.IsNullOrEmpty(c.RoleType)) roleByAmazonType.TryGetValue(c.RoleType!, out role);
            if (role == null && !string.IsNullOrEmpty(c.Role)) roleByNameJa.TryGetValue(c.Role!, out role);
            role ??= roles.FirstOrDefault(r => r.RoleCode == "OTHER");
            if (role == null) continue;

            string key = role.RoleCode + " " + c.Name;
            if (!seen.Add(key)) continue;

            int? aliasId = aliasByName.TryGetValue(c.Name, out var id) ? id : null;
            result.Add(new BookCredit
            {
                RoleCode = role.RoleCode,
                PersonAliasId = aliasId,
                // 名義に紐付いたときも誌面表記を残す必要は無いので、紐付けが取れた行は credit_text を空にする。
                CreditText = aliasId.HasValue ? null : c.Name,
                AmazonSourceRole = c.RoleType ?? c.Role
            });
        }

        return result;
    }

    /// <summary>
    /// 取り込み内容を DB へ保存する。書籍本体を INSERT したうえで、シリーズ・ジャンル・クレジットを
    /// <see cref="BooksRepository.ReplaceRelationsAsync"/> で一括投入する。
    /// </summary>
    /// <param name="plan">組み立て済みの取り込み内容。</param>
    /// <param name="seriesIds">紐付けるシリーズ ID。空ならオールスターズ扱い。</param>
    /// <param name="genreCodes">紐付けるジャンルコード。先頭が代表ジャンルになる。</param>
    /// <param name="updatedBy">更新ユーザー名。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <returns>採番された book_id。</returns>
    public async Task<int> SaveAsync(
        BookImportPlan plan,
        IReadOnlyList<int> seriesIds,
        IReadOnlyList<string> genreCodes,
        string? updatedBy,
        CancellationToken ct = default)
    {
        if (plan.Book == null) throw new InvalidOperationException("取り込み内容が組み立てられていません。");

        plan.Book.CreatedBy = updatedBy;
        plan.Book.UpdatedBy = updatedBy;

        int bookId = await _booksRepo.InsertAsync(plan.Book, ct).ConfigureAwait(false);

        // 表紙 URL は InsertAsync の対象外（汎用更新から画像列を守る設計）なので専用経路で入れる。
        if (plan.Book.CoverImageSource != null)
        {
            await _booksRepo.UpdateCoverImagesAsync(
                bookId,
                plan.Book.CoverImageUrlPrint,
                plan.Book.CoverImageUrlKindle,
                plan.Book.CoverImageSource,
                plan.Book.CoverImageFetchedAt ?? DateTime.Now,
                ct).ConfigureAwait(false);
        }

        await _booksRepo.ReplaceRelationsAsync(
            bookId,
            seriesIds,
            genreCodes,
            genreCodes.Count > 0 ? genreCodes[0] : null,
            plan.Credits,
            updatedBy,
            ct).ConfigureAwait(false);

        return bookId;
    }

    /// <summary>
    /// 既存の書籍レコードへ紙版の情報を合流させる。Kindle 版だけで登録済みの書籍に、
    /// 同一内容の紙版（通常版）の ASIN・ISBN・ページ数・価格・書影を足すための経路。
    /// <para>
    /// 発売日は「紙があれば紙が代表」という books の規約に従って付け替える：
    /// 紙の発売日を <c>release_date</c> に据え、元の Kindle 発売日は <c>release_date_kindle</c> へ移す
    /// （両者が同日なら付け替えない）。
    /// </para>
    /// <para>
    /// 空の項目だけを埋める方針で、既に値が入っている列は上書きしない（手で直した内容を潰さない）。
    /// 代表書影は Kindle 優先の方針なので、紙を合流させても代表は切り替えない
    /// （Kindle 側の書影がまだ無い書籍のときだけ紙を代表に据える）。
    /// クレジットは紙・電子で同一なので触らない。
    /// </para>
    /// </summary>
    /// <returns>更新内容の説明行。適用しなかった項目は含まれない。</returns>
    public async Task<IReadOnlyList<string>> AttachPrintAsync(
        int bookId, string printAsin, string? updatedBy, bool dryRun, CancellationToken ct = default)
    {
        var book = await _booksRepo.GetByIdAsync(bookId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"book_id={bookId} が見つかりません。");

        var item = await _paApi.GetItemAsync(printAsin, ct, PaResourceSet.Extended).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"ASIN が Amazon で見つかりません: {printAsin}");

        var changes = new List<string>();

        book.AmazonAsinPrint = item.Asin;
        book.HasPrint = true;
        changes.Add($"amazon_asin_print = {item.Asin}, has_print = 1");

        string? isbn13 = NormalizeIsbn13(item.Ean);
        if (isbn13 != null && string.IsNullOrEmpty(book.Isbn13))
        {
            book.Isbn13 = isbn13;
            changes.Add($"isbn13 = {isbn13}");
        }

        if (book.PageCount == null && item.PagesCount is int pages and > 0 and <= ushort.MaxValue)
        {
            book.PageCount = (ushort)pages;
            changes.Add($"page_count = {pages}");
        }

        // 装丁は紙の値の方が意味がある（Kindle 版では常に "Kindle版" になるため上書きする）。
        if (!string.IsNullOrEmpty(item.Binding)
            && !string.Equals(book.BindingText, item.Binding, StringComparison.Ordinal))
        {
            book.BindingText = item.Binding;
            changes.Add($"binding_text = {item.Binding}");
        }

        // 紙の価格は取り込まない（BuildPlanAsync と同じ理由：offersV2 は出品価格であって定価ではない）。

        var printDate = ParseAmazonDate(item.PublicationDate ?? item.ReleaseDate);
        if (printDate.HasValue && printDate.Value != book.ReleaseDate)
        {
            // 元の代表日（Kindle 由来）を電子側へ退避してから、紙の発売日を代表に据える。
            if (book.ReleaseDateKindle == null) book.ReleaseDateKindle = book.ReleaseDate;
            changes.Add($"release_date = {printDate:yyyy-MM-dd}（旧 {book.ReleaseDate:yyyy-MM-dd} を release_date_kindle へ退避）");
            book.ReleaseDate = printDate.Value;
        }

        string? printCover = item.LargeImageUrl;
        if (!string.IsNullOrEmpty(printCover))
        {
            book.CoverImageUrlPrint = printCover;
            changes.Add("cover_image_url_print を設定");
            // 代表書影は Kindle 優先の方針なので、紙を合流させても代表は切り替えない。
            // Kindle 側の書影がまだ無い書籍だけ、紙を代表に据える。
            if (string.IsNullOrEmpty(book.CoverImageUrlKindle))
            {
                book.CoverImageSource = "amazon_print";
                changes.Add("Kindle 書影が無いため代表書影を紙に設定");
            }
        }

        if (dryRun) return changes;

        book.UpdatedBy = updatedBy;
        await _booksRepo.UpdateAsync(book, ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(printCover))
        {
            await _booksRepo.UpdateCoverImagesAsync(
                bookId, book.CoverImageUrlPrint, book.CoverImageUrlKindle,
                book.CoverImageSource, DateTime.Now, ct).ConfigureAwait(false);
        }

        return changes;
    }

    /// <summary>
    /// Amazon の日付文字列（"2025-11-21T00:00:01Z" 等）を日付に変換する。解析できなければ null。
    /// タイムゾーン指定を持つ値でも、書籍の発売日として意味があるのは日付部分だけなので
    /// ローカル変換で日付が 1 日ずれないよう <see cref="DateTimeStyles.AdjustToUniversal"/> で受ける。
    /// </summary>
    private static DateTime? ParseAmazonDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var dt))
        {
            return dt.Date;
        }
        return null;
    }

    /// <summary>
    /// EAN / ISBN 文字列を ISBN-13 に正規化する。13 桁でなければ null。
    /// 13 桁でも書籍 JAN（雑誌コードやムックの独自 JAN など）が返ることがあるため、
    /// ISBN の接頭辞 978 / 979 を持つものだけを ISBN-13 として採用する。
    /// </summary>
    private static string? NormalizeIsbn13(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length != 13) return null;
        if (!digits.StartsWith("978", StringComparison.Ordinal) && !digits.StartsWith("979", StringComparison.Ordinal))
            return null;
        return digits;
    }
}

/// <summary>
/// 1 冊分の取り込み内容。DB へ書く前に内容を提示するための中間表現で、
/// 解決できなかった項目は <see cref="Warnings"/>、取り込み不能な事由は <see cref="Errors"/> に入る。
/// </summary>
public sealed class BookImportPlan
{
    /// <summary>取り込む書籍本体（エラー時は null）。</summary>
    public Book? Book { get; set; }

    /// <summary>Amazon の寄与者から組み立てたクレジット行。</summary>
    public List<BookCredit> Credits { get; set; } = new();

    /// <summary>Amazon のカテゴリ名（ジャンル選択の参考に表示する）。</summary>
    public IReadOnlyList<string> BrowseNodes { get; set; } = Array.Empty<string>();

    /// <summary>同じ ISBN / ASIN で既に登録済みの書籍 ID（未登録なら null）。</summary>
    public int? ExistingBookId { get; set; }

    /// <summary>取り込みは可能だが人の確認が要る事項。</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>取り込み不能な事由。1 件でもあれば保存してはいけない。</summary>
    public List<string> Errors { get; set; } = new();
}
