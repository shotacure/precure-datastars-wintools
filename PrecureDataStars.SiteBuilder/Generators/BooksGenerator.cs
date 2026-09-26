using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 書籍索引（<c>/books/</c>）と書籍詳細（<c>/books/{book_id}/</c>）の生成。
/// 音楽商品（<see cref="ProductsGenerator"/>）とは独立した系統で、紙と Kindle の 2 版を
/// 1 冊のページに束ねて表示する。
/// <para>
/// 索引は「発売日順（既定）／ジャンル別／シリーズ別」の 3 タブ。商品索引と違って
/// 全パネルをサーバ描画する（書籍は件数が桁違いに少なく、カード複製の JS を持ち込むより
/// 素直に 3 セット出す方が保守しやすいため）。
/// </para>
/// <para>
/// シリーズ所属は多対多（<c>book_series</c>）。1 冊が複数シリーズに紐付く合同本は
/// 該当する全シリーズのセクションに出す。紐付けゼロの書籍は「オールスターズ・シリーズ横断」
/// セクションへ集める。
/// </para>
/// <para>
/// クレジットは <c>person_aliases</c> への紐付けとフリーテキストの併用。リンク化は
/// <see cref="StaffNameLinkResolver"/> に委ね、紐付けのあるものだけが &lt;a&gt; になる
/// （「下線が出るのはクリックできるものだけ」という全サイト共通のシグナルを守る）。
/// </para>
/// </summary>
public sealed class BooksGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly StaffNameLinkResolver _staffLinks;

    private readonly BooksRepository _booksRepo;
    private readonly BookMastersRepository _mastersRepo;
    private readonly ProductCompaniesRepository _productCompaniesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

    /// <summary>索引のセクション見出しに使う、シリーズ紐付けが無い書籍の受け皿ラベル。</summary>
    private const string CrossSeriesLabel = "オールスターズ・シリーズ横断";

    public BooksGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        StaffNameLinkResolver staffLinks)
    {
        _ctx = ctx;
        _page = page;
        _staffLinks = staffLinks;

        _booksRepo = new BooksRepository(factory);
        _mastersRepo = new BookMastersRepository(factory);
        _productCompaniesRepo = new ProductCompaniesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
    }

    /// <summary>索引 1 ページと書籍ごとの詳細ページを生成する。</summary>
    public async Task GenerateAsync(CancellationToken ct = default)
    {
        // マスタは起動時に全件ロードして辞書化する（per-id の GetByIdAsync はビルド中に呼ばない方針）。
        var books = await _booksRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var seriesLinks = await _booksRepo.GetAllSeriesLinksAsync(ct).ConfigureAwait(false);
        var genreLinks = await _booksRepo.GetAllGenreLinksAsync(ct).ConfigureAwait(false);
        var credits = await _booksRepo.GetAllCreditsAsync(ct).ConfigureAwait(false);
        var genres = await _mastersRepo.GetGenresAsync(ct).ConfigureAwait(false);
        var creditRoles = await _mastersRepo.GetCreditRolesAsync(ct).ConfigureAwait(false);
        var productCompanies = await _productCompaniesRepo.GetAllAsync(ct: ct).ConfigureAwait(false);
        var personAliases = await _personAliasesRepo.GetAllAsync(ct: ct).ConfigureAwait(false);

        var genreByCode = genres.ToDictionary(g => g.GenreCode, StringComparer.Ordinal);
        var roleByCode = creditRoles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        var companyById = productCompanies.ToDictionary(c => c.ProductCompanyId);
        var aliasById = personAliases.ToDictionary(a => a.AliasId);

        var seriesIdsByBook = seriesLinks
            .GroupBy(l => l.BookId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(x => x.SeriesId).ToList());
        var genreLinksByBook = genreLinks
            .GroupBy(l => l.BookId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BookGenreLink>)g.ToList());
        var creditsByBook = credits
            .GroupBy(c => c.BookId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BookCredit>)g.ToList());

        // 索引カードの素材は詳細ページでも使い回すため、先に 1 度だけ組み立てる。
        var rows = books
            .Select(b => BuildRow(b, seriesIdsByBook, genreLinksByBook, genreByCode))
            .ToList();

        GenerateIndex(rows, genres);

        foreach (var (book, row) in books.Zip(rows))
        {
            RenderDetail(book, row, seriesIdsByBook, genreLinksByBook, creditsByBook,
                genreByCode, roleByCode, companyById, aliasById);
        }

        _ctx.Logger.Success($"books: {books.Count + 1} ページ");
    }

    // ── 索引 ──

    /// <summary>/books/（書籍索引）。発売日順・ジャンル別・シリーズ別の 3 パネルを作る。</summary>
    private void GenerateIndex(IReadOnlyList<BookIndexRow> rows, IReadOnlyList<BookGenre> genres)
    {
        var content = new BooksIndexModel
        {
            TotalCount = rows.Count,
            ReleaseRows = rows.OrderBy(r => r.SortKey, StringComparer.Ordinal).ToList(),
            GenreSections = BuildGenreSections(rows, genres),
            SeriesSections = BuildSeriesSections(rows)
        };

        var layout = new LayoutModel
        {
            PageTitle = "プリキュアの書籍(本・Kindle)",
            // 本文リード行と同じ母数をカードにも置く。
            OgCard = new OgCardSpec(Kicker: "", Title: "プリキュアの書籍(本・Kindle)")
            {
                Badges = new[]
                {
                    new OgCardBadge("書籍", $"{rows.Count}件"),
                    new OgCardBadge("ジャンル", $"{genres.Count}種")
                }
            },
            MetaDescription = $"プリキュア関連の書籍 {rows.Count} 件。設定資料集・ファンブック・絵本・楽譜まで、紙と Kindle の購入先、発売日、出版社からたどれます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プリキュアの書籍(本・Kindle)", Url = "" }
            }
        };

        _page.RenderAndWrite("/books/", "books", "books-index.sbn", content, layout);
    }

    /// <summary>
    /// ジャンル別セクションを組む。1 冊が複数ジャンルを持つ場合は該当する全ジャンルに出す
    /// （代表ジャンルだけに絞ると「ムックでもある設定資料集」がムックのタブから消えてしまうため）。
    /// 空のジャンルはセクションごと出さない。
    /// </summary>
    private static List<BookIndexSection> BuildGenreSections(
        IReadOnlyList<BookIndexRow> rows, IReadOnlyList<BookGenre> genres)
    {
        var sections = new List<BookIndexSection>();
        foreach (var genre in genres)
        {
            var members = rows
                .Where(r => r.GenreCodes.Contains(genre.GenreCode, StringComparer.Ordinal))
                .OrderBy(r => r.SortKey, StringComparer.Ordinal)
                .ToList();
            if (members.Count == 0) continue;

            sections.Add(new BookIndexSection { Label = genre.NameJa, Members = members });
        }
        return sections;
    }

    /// <summary>
    /// シリーズ別セクションを組む。シリーズは放送開始日昇順、末尾に紐付けゼロの受け皿を置く。
    /// 合同本は紐付く全シリーズのセクションに重複して現れる（それが正しい所属の表現なので許容する）。
    /// </summary>
    private List<BookIndexSection> BuildSeriesSections(IReadOnlyList<BookIndexRow> rows)
    {
        var sections = new List<BookIndexSection>();

        foreach (var series in _ctx.Series)
        {
            var members = rows
                .Where(r => r.SeriesIds.Contains(series.SeriesId))
                .OrderBy(r => r.SortKey, StringComparer.Ordinal)
                .ToList();
            if (members.Count == 0) continue;

            sections.Add(new BookIndexSection
            {
                Label = series.Title,
                SeriesLink = PathUtil.SeriesUrl(series.Slug),
                SeriesStartYearLabel = series.StartDate.Year.ToString(),
                Members = members
            });
        }

        var crossSeries = rows
            .Where(r => r.SeriesIds.Count == 0)
            .OrderBy(r => r.SortKey, StringComparer.Ordinal)
            .ToList();
        if (crossSeries.Count > 0)
            sections.Add(new BookIndexSection { Label = CrossSeriesLabel, Members = crossSeries });

        return sections;
    }

    /// <summary>索引カード 1 枚分の表示素材を組み立てる。</summary>
    private static BookIndexRow BuildRow(
        Book book,
        IReadOnlyDictionary<int, IReadOnlyList<int>> seriesIdsByBook,
        IReadOnlyDictionary<int, IReadOnlyList<BookGenreLink>> genreLinksByBook,
        IReadOnlyDictionary<string, BookGenre> genreByCode)
    {
        var links = genreLinksByBook.TryGetValue(book.BookId, out var gl) ? gl : Array.Empty<BookGenreLink>();
        var primary = links.FirstOrDefault(l => l.IsPrimary) ?? links.FirstOrDefault();
        string primaryLabel = primary != null && genreByCode.TryGetValue(primary.GenreCode, out var pg) ? pg.NameJa : "";

        return new BookIndexRow
        {
            BookId = book.BookId,
            Title = book.Title,
            Url = PathUtil.BookUrl(book.BookId),
            CoverImageUrl = book.CoverImageUrl ?? "",
            ReleaseDateShort = FormatDateShort(book.ReleaseDate),
            PriceLabel = FormatPriceLabel(book),
            PageCountLabel = book.PageCount.HasValue ? $"{book.PageCount} ページ" : "",
            EditionLabel = FormatEditionLabel(book),
            PrimaryGenreLabel = primaryLabel,
            PrimaryGenreCode = primary?.GenreCode ?? "",
            BadgeClassSuffix = ToBadgeClassSuffix(primary?.GenreCode),
            GenreCodes = links.Select(l => l.GenreCode).ToList(),
            SeriesIds = seriesIdsByBook.TryGetValue(book.BookId, out var sids) ? sids : Array.Empty<int>(),
            // 発売日 + book_id の複合キー。同日発売の並びを登録順で安定させる。
            SortKey = $"{book.ReleaseDate:yyyyMMdd}-{book.BookId:D8}"
        };
    }

    // ── 詳細 ──

    /// <summary>/books/{book_id}/（書籍詳細）。</summary>
    private void RenderDetail(
        Book book,
        BookIndexRow row,
        IReadOnlyDictionary<int, IReadOnlyList<int>> seriesIdsByBook,
        IReadOnlyDictionary<int, IReadOnlyList<BookGenreLink>> genreLinksByBook,
        IReadOnlyDictionary<int, IReadOnlyList<BookCredit>> creditsByBook,
        IReadOnlyDictionary<string, BookGenre> genreByCode,
        IReadOnlyDictionary<string, BookCreditRole> roleByCode,
        IReadOnlyDictionary<int, ProductCompany> companyById,
        IReadOnlyDictionary<int, PersonAlias> aliasById)
    {
        string tag = _ctx.Config.AmazonAssociateTag;

        // 紙・Kindle の 2 系統の購入導線。ASIN があるものだけ URL を組み立てる。
        string printUrl = BuildAmazonUrl(book.AmazonAsinPrint, tag);
        string kindleUrl = BuildAmazonUrl(book.AmazonAsinKindle, tag);

        // 表紙は代表 1 枚が基本。「両方表示」が立っていて 2 枚の URL が互いに異なるときだけ 2 枚並べる。
        string coverPrimary = book.CoverImageUrl ?? "";
        string coverSecondary = "";
        if (book.CoverImageShowBoth
            && !string.IsNullOrEmpty(book.CoverImageUrlPrint)
            && !string.IsNullOrEmpty(book.CoverImageUrlKindle)
            && !string.Equals(book.CoverImageUrlPrint, book.CoverImageUrlKindle, StringComparison.Ordinal))
        {
            coverSecondary = string.Equals(coverPrimary, book.CoverImageUrlPrint, StringComparison.Ordinal)
                ? book.CoverImageUrlKindle!
                : book.CoverImageUrlPrint!;
        }

        var seriesRows = (seriesIdsByBook.TryGetValue(book.BookId, out var sids) ? sids : Array.Empty<int>())
            .Select(id => _ctx.SeriesById.TryGetValue(id, out var s) ? s : null)
            .Where(s => s != null)
            .OrderBy(s => s!.StartDate)
            .Select(s => new BookSeriesRow
            {
                Title = s!.Title,
                Url = PathUtil.SeriesUrl(s.Slug),
                StartYearLabel = s.StartDate.Year.ToString()
            })
            .ToList();

        var genreRows = (genreLinksByBook.TryGetValue(book.BookId, out var gl) ? gl : Array.Empty<BookGenreLink>())
            .Select(l => genreByCode.TryGetValue(l.GenreCode, out var g) ? g : null)
            .Where(g => g != null)
            .OrderBy(g => g!.DisplayOrder)
            .Select(g => g!.NameJa)
            .ToList();

        var creditRows = BuildCreditRows(
            creditsByBook.TryGetValue(book.BookId, out var cs) ? cs : Array.Empty<BookCredit>(),
            roleByCode, aliasById);

        string publisher = book.PublisherProductCompanyId.HasValue
            && companyById.TryGetValue(book.PublisherProductCompanyId.Value, out var pc)
            ? pc.NameJa
            : "";

        var content = new BookDetailModel
        {
            Book = new BookDetailView
            {
                Title = book.Title,
                CoverImageUrl = coverPrimary,
                CoverImageSecondaryUrl = coverSecondary,
                AmazonPrintUrl = printUrl,
                AmazonKindleUrl = kindleUrl,
                ReleaseDate = FormatDateLong(book.ReleaseDate),
                ReleaseDateKindle = book.ReleaseDateKindle.HasValue ? FormatDateLong(book.ReleaseDateKindle.Value) : "",
                Publisher = publisher,
                Isbn13 = book.Isbn13 ?? "",
                Isbn10 = BookCodes.ToIsbn10(book.Isbn13),
                CCode = book.CCode ?? "",
                BookJanSecondRow = BookCodes.ToBookJanSecondRow(book.CCode, book.PriceExTax),
                MagazineCode = book.MagazineCode ?? "",
                PeriodicalCode = book.PeriodicalCode ?? "",
                PageCountLabel = book.PageCount.HasValue ? $"{book.PageCount} ページ" : "",
                // 判型は人手で整えた trim_size を優先し、無ければ Amazon 由来の装丁表記で代替する。
                FormatLabel = !string.IsNullOrEmpty(book.TrimSize) ? book.TrimSize! : (book.BindingText ?? ""),
                PriceLabel = FormatPriceLabel(book),
                PriceKindleLabel = book.PriceKindleIncTax.HasValue ? $"{book.PriceKindleIncTax:#,0} 円（税込）" : "",
                EditionLabel = FormatEditionLabel(book),
                Notes = book.Notes ?? "",
                OfficialUrl = book.OfficialUrl ?? ""
            },
            GenreLabels = genreRows,
            SeriesRows = seriesRows,
            CreditRows = creditRows
        };

        var layout = new LayoutModel
        {
            PageTitle = book.Title,
            MetaDescription = BuildDetailDescription(book, publisher, row.PrimaryGenreLabel),
            // 商品詳細と同じ組み方。識別を上段に、量をバッジに、中身の手がかりを事実行に。
            OgCard = new OgCardSpec(
                Kicker: string.IsNullOrWhiteSpace(row.PrimaryGenreLabel) ? "書籍" : row.PrimaryGenreLabel,
                Title: book.Title)
            {
                KickerRight = $"{FormatDateLong(book.ReleaseDate)} 発売",
                Badges = BuildBookOgBadges(book),
                InlineFacts = string.IsNullOrWhiteSpace(publisher)
                    ? Array.Empty<OgCardFactLine>()
                    : new[] { new OgCardFactLine("出版社", publisher) },
                Facts = genreRows.Take(3).Select(g => new OgCardFactLine("", g)).ToArray()
            },
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プリキュアの書籍(本・Kindle)", Url = "/books/" },
                new BreadcrumbItem { Label = book.Title, Url = "" }
            }
        };

        _page.RenderAndWrite(row.Url, "books", "books-detail.sbn", content, layout);
    }

    /// <summary>
    /// クレジット行を役職ごとにまとめる。名義紐付けのあるものは人物詳細へのリンク、
    /// フリーテキストのみのものは平文（下線なし）になる。
    /// 表示名は「誌面の表記」を優先する：credit_text があればそれ、無ければ名義の表示名。
    /// </summary>
    private List<BookCreditGroup> BuildCreditRows(
        IReadOnlyList<BookCredit> credits,
        IReadOnlyDictionary<string, BookCreditRole> roleByCode,
        IReadOnlyDictionary<int, PersonAlias> aliasById)
    {
        var groups = new List<BookCreditGroup>();

        foreach (var byRole in credits.GroupBy(c => c.RoleCode, StringComparer.Ordinal))
        {
            if (!roleByCode.TryGetValue(byRole.Key, out var role)) continue;

            var names = new List<string>();
            foreach (var c in byRole.OrderBy(c => c.DisplayOrder).ThenBy(c => c.BookCreditId))
            {
                string display = c.CreditText ?? "";
                if (display.Length == 0 && c.PersonAliasId.HasValue && aliasById.TryGetValue(c.PersonAliasId.Value, out var alias))
                    display = alias.DisplayTextOverride ?? alias.Name;
                if (display.Length == 0) continue;

                // 紐付けがあれば <a>、無ければエスケープ済み平文。判定は Resolver 側に任せる。
                names.Add(_staffLinks.ResolveAsHtml(c.PersonAliasId, display));
            }
            if (names.Count == 0) continue;

            groups.Add(new BookCreditGroup
            {
                RoleLabel = role.NameJa,
                DisplayOrder = role.DisplayOrder,
                NamesHtml = string.Join("／", names)
            });
        }

        return groups.OrderBy(g => g.DisplayOrder).ToList();
    }

    // ── 表示整形ヘルパ ──

    /// <summary>ASIN とアソシエイトタグから商品ページ URL を組み立てる。ASIN が無ければ空文字。 タグ付与の規約は音楽商品（ProductsGenerator）と揃える。</summary>
    private static string BuildAmazonUrl(string? asin, string tag)
    {
        if (string.IsNullOrWhiteSpace(asin)) return "";
        string url = "https://www.amazon.co.jp/dp/" + Uri.EscapeDataString(asin);
        if (tag.Length > 0) url += "?tag=" + Uri.EscapeDataString(tag);
        return url;
    }

    /// <summary>ジャンルコードを CSS クラス接尾辞へ変換する（<c>SETTING_BOOK</c> → <c>setting-book</c>）。 未指定なら "other" に落とし、バッジの配色が必ず 1 つ決まるようにする。</summary>
    private static string ToBadgeClassSuffix(string? genreCode)
        => string.IsNullOrEmpty(genreCode) ? "other" : genreCode.ToLowerInvariant().Replace('_', '-');

    /// <summary>索引カード用の短い日付表記（2004.2.1）。</summary>
    private static string FormatDateShort(DateTime date) => $"{date.Year}.{date.Month}.{date.Day}";

    /// <summary>詳細ページ用の日付表記（2004年2月1日）。</summary>
    private static string FormatDateLong(DateTime date) => $"{date.Year}年{date.Month}月{date.Day}日";

    /// <summary>紙版の価格表記。税込を主、税抜を従で併記する。どちらも無ければ空文字。</summary>
    private static string FormatPriceLabel(Book book)
    {
        if (book.PriceIncTax.HasValue && book.PriceExTax.HasValue)
            return $"{book.PriceIncTax:#,0} 円（税込）／ {book.PriceExTax:#,0} 円（税抜）";
        if (book.PriceIncTax.HasValue) return $"{book.PriceIncTax:#,0} 円（税込）";
        if (book.PriceExTax.HasValue) return $"{book.PriceExTax:#,0} 円（税抜）";
        return "";
    }

    /// <summary>「紙 / Kindle」の版構成ラベル。両方あれば併記、片方だけならその名前だけ。</summary>
    private static string FormatEditionLabel(Book book)
    {
        if (book.HasPrint && book.HasKindle) return "紙・Kindle";
        if (book.HasKindle) return "Kindle";
        return "紙";
    }

    /// <summary>詳細ページの meta description。書名・ジャンル・出版社・発売日を 1 文に畳む。</summary>
    /// <summary>書籍カードの数バッジ。ページ数・価格・Kindle の有無など、数として語れるものだけを置く。</summary>
    private static OgCardBadge[] BuildBookOgBadges(Book book)
    {
        var badges = new List<OgCardBadge>();
        if (book.PageCount is ushort pages && pages > 0) badges.Add(new OgCardBadge("ページ", $"{pages}"));
        if (book.PriceIncTax is int price && price > 0) badges.Add(new OgCardBadge("価格", $"{price:#,0}円"));
        if (book.ReleaseDateKindle.HasValue) badges.Add(new OgCardBadge("電子", "Kindle あり"));
        return badges.ToArray();
    }

    private static string BuildDetailDescription(Book book, string publisher, string genreLabel)
    {
        var parts = new List<string>();
        if (genreLabel.Length > 0) parts.Add(genreLabel);
        if (publisher.Length > 0) parts.Add(publisher);
        parts.Add($"{book.ReleaseDate.Year}年{book.ReleaseDate.Month}月発売");

        return $"『{book.Title}』（{string.Join("／", parts)}）の書誌情報。"
             + "判型・ページ数・ISBN・収録シリーズと、紙版・Kindle 版の購入先をまとめています。";
    }
}

// ── テンプレへ渡すビューモデル ──

/// <summary>books-index.sbn に渡すモデル。</summary>
public sealed class BooksIndexModel
{
    /// <summary>登録書籍の総数（論理削除を除く）。</summary>
    public int TotalCount { get; set; }

    /// <summary>発売日順パネルの全書籍（既定タブ）。</summary>
    public IReadOnlyList<BookIndexRow> ReleaseRows { get; set; } = Array.Empty<BookIndexRow>();

    /// <summary>ジャンル別パネルのセクション列。</summary>
    public IReadOnlyList<BookIndexSection> GenreSections { get; set; } = Array.Empty<BookIndexSection>();

    /// <summary>シリーズ別パネルのセクション列。</summary>
    public IReadOnlyList<BookIndexSection> SeriesSections { get; set; } = Array.Empty<BookIndexSection>();
}

/// <summary>索引のセクション 1 つ（ジャンル別・シリーズ別で共用）。</summary>
public sealed class BookIndexSection
{
    /// <summary>セクション見出し。</summary>
    public string Label { get; set; } = "";

    /// <summary>シリーズ詳細への URL（シリーズ別セクションのみ、それ以外は空文字）。</summary>
    public string SeriesLink { get; set; } = "";

    /// <summary>シリーズ開始年（シリーズ別セクションのみ、それ以外は空文字）。</summary>
    public string SeriesStartYearLabel { get; set; } = "";

    /// <summary>このセクションに属する書籍カード。</summary>
    public IReadOnlyList<BookIndexRow> Members { get; set; } = Array.Empty<BookIndexRow>();
}

/// <summary>索引カード 1 枚分の表示素材。</summary>
public sealed class BookIndexRow
{
    /// <summary>書籍 ID。</summary>
    public int BookId { get; set; }

    /// <summary>書名。</summary>
    public string Title { get; set; } = "";

    /// <summary>詳細ページ URL。</summary>
    public string Url { get; set; } = "";

    /// <summary>表紙画像 URL（未取得なら空文字）。</summary>
    public string CoverImageUrl { get; set; } = "";

    /// <summary>短い発売日表記（2004.2.1）。</summary>
    public string ReleaseDateShort { get; set; } = "";

    /// <summary>価格表記（未登録なら空文字）。</summary>
    public string PriceLabel { get; set; } = "";

    /// <summary>ページ数表記（未登録なら空文字）。</summary>
    public string PageCountLabel { get; set; } = "";

    /// <summary>版構成ラベル（紙・Kindle / 紙 / Kindle）。</summary>
    public string EditionLabel { get; set; } = "";

    /// <summary>代表ジャンルの表示名（バッジに出す）。</summary>
    public string PrimaryGenreLabel { get; set; } = "";

    /// <summary>代表ジャンルのコード（ジャンル別セクションの代表判定に使う）。</summary>
    public string PrimaryGenreCode { get; set; } = "";

    /// <summary>バッジ配色用の CSS クラス接尾辞（<c>setting-book</c> 等）。</summary>
    public string BadgeClassSuffix { get; set; } = "other";

    /// <summary>この書籍が持つ全ジャンルコード（ジャンル別セクションの振り分け用）。</summary>
    public IReadOnlyList<string> GenreCodes { get; set; } = Array.Empty<string>();

    /// <summary>この書籍が紐付く全シリーズ ID（シリーズ別セクションの振り分け用）。</summary>
    public IReadOnlyList<int> SeriesIds { get; set; } = Array.Empty<int>();

    /// <summary>並べ替えキー（発売日 + 書籍 ID）。</summary>
    public string SortKey { get; set; } = "";
}

/// <summary>books-detail.sbn に渡すモデル。</summary>
public sealed class BookDetailModel
{
    /// <summary>書籍本体の表示素材。</summary>
    public BookDetailView Book { get; set; } = new();

    /// <summary>ジャンル表示名（表示順）。</summary>
    public IReadOnlyList<string> GenreLabels { get; set; } = Array.Empty<string>();

    /// <summary>収録シリーズ。</summary>
    public IReadOnlyList<BookSeriesRow> SeriesRows { get; set; } = Array.Empty<BookSeriesRow>();

    /// <summary>役職ごとにまとめたクレジット。</summary>
    public IReadOnlyList<BookCreditGroup> CreditRows { get; set; } = Array.Empty<BookCreditGroup>();
}

/// <summary>書籍詳細ページの書籍本体ブロック。</summary>
public sealed class BookDetailView
{
    /// <summary>書名。</summary>
    public string Title { get; set; } = "";

    /// <summary>表紙画像 URL（代表）。</summary>
    public string CoverImageUrl { get; set; } = "";

    /// <summary>2 枚目の表紙画像 URL（「両方表示」設定時のみ、それ以外は空文字）。</summary>
    public string CoverImageSecondaryUrl { get; set; } = "";

    /// <summary>紙版の Amazon 商品ページ URL（ASIN 未登録なら空文字）。</summary>
    public string AmazonPrintUrl { get; set; } = "";

    /// <summary>Kindle 版の Amazon 商品ページ URL（ASIN 未登録なら空文字）。</summary>
    public string AmazonKindleUrl { get; set; } = "";

    /// <summary>発売日（2004年2月1日）。</summary>
    public string ReleaseDate { get; set; } = "";

    /// <summary>Kindle 版の配信日（紙と同日なら空文字）。</summary>
    public string ReleaseDateKindle { get; set; } = "";

    /// <summary>出版社名（未紐付けなら空文字）。</summary>
    public string Publisher { get; set; } = "";

    /// <summary>ISBN-13（紙のみ、未登録なら空文字）。</summary>
    public string Isbn13 { get; set; } = "";

    /// <summary>ISBN-10（978 始まりの ISBN-13 から導いた値。導けなければ空文字）。</summary>
    public string Isbn10 { get; set; } = "";

    /// <summary>Cコード（未登録なら空文字）。</summary>
    public string CCode { get; set; } = "";

    /// <summary>書籍 JAN の 2 段目（Cコードと税抜価格から導いた値。導けなければ空文字）。</summary>
    public string BookJanSecondRow { get; set; } = "";

    /// <summary>雑誌コード（未登録なら空文字）。</summary>
    public string MagazineCode { get; set; } = "";

    /// <summary>定期刊行物コード（雑誌 JAN、未登録なら空文字）。</summary>
    public string PeriodicalCode { get; set; } = "";

    /// <summary>ページ数表記。</summary>
    public string PageCountLabel { get; set; } = "";

    /// <summary>判型・装丁の表記。</summary>
    public string FormatLabel { get; set; } = "";

    /// <summary>紙版の価格表記。</summary>
    public string PriceLabel { get; set; } = "";

    /// <summary>Kindle 版の価格表記。</summary>
    public string PriceKindleLabel { get; set; } = "";

    /// <summary>版構成ラベル（紙・Kindle / 紙 / Kindle）。</summary>
    public string EditionLabel { get; set; } = "";

    /// <summary>備考。</summary>
    public string Notes { get; set; } = "";

    /// <summary>公式ページ URL。</summary>
    public string OfficialUrl { get; set; } = "";
}

/// <summary>書籍詳細の収録シリーズ 1 行。</summary>
public sealed class BookSeriesRow
{
    /// <summary>シリーズ正式タイトル。</summary>
    public string Title { get; set; } = "";

    /// <summary>シリーズ詳細 URL。</summary>
    public string Url { get; set; } = "";

    /// <summary>開始年（複数シリーズが並ぶ文脈なので年を添える）。</summary>
    public string StartYearLabel { get; set; } = "";
}

/// <summary>書籍詳細のクレジット 1 役職ぶん。</summary>
public sealed class BookCreditGroup
{
    /// <summary>役職の表示名（著 / 監修 / イラスト …）。</summary>
    public string RoleLabel { get; set; } = "";

    /// <summary>役職マスタの表示順。</summary>
    public int DisplayOrder { get; set; }

    /// <summary>名義の並び（リンク済み HTML を「／」で連結済み）。</summary>
    public string NamesHtml { get; set; } = "";
}
