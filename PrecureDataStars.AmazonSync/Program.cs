using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrecureDataStars.AmazonPaApi;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.AmazonSync;

/// <summary>
/// products / books テーブルから ASIN を持つ行を抽出し、Creators API GetItems を叩いて
/// ジャケット・表紙画像 URL（m.media-amazon.com 系）を <c>cover_image_*</c> に書き戻すバッチ。
/// 取得元コードは音楽商品が <c>amazon_cd</c> / <c>amazon_digital</c>、書籍が
/// <c>amazon_print</c> / <c>amazon_kindle</c>。代表の既定優先は音楽がデジタル、書籍が紙。
/// 書籍については取り込み（<c>--import-book</c>）と紙版合流（<c>--attach-print</c>）も担う。
/// <para>
/// CLI 引数：
/// <list type="bullet">
///   <item><c>--all</c>: 鮮度に関わらず全件強制再取得</item>
///   <item><c>--upgrade-cd-to-digital</c>: cover_image_source='amazon_cd'（CD 由来）かつデジタル ASIN を持つ商品に限り、デジタル由来へ差し替える</item>
///   <item><c>--asin B0XXXXXXXX</c>: 単一 ASIN だけテスト取得（DB 更新なし・診断表示のみ）</item>
///   <item><c>--search "キーワード"</c>: キーワード検索のテスト（DB 更新なし・診断表示のみ）</item>
///   <item><c>--index Books</c>: <c>--search</c> の検索カテゴリ（Music / DigitalMusic / Books / KindleStore、既定 Books）</item>
///   <item><c>--extended</c>: 拡張リソース（著者ロール・ISBN・ページ数・判型・カテゴリ）を要求する。書籍系の検索では既定で有効</item>
///   <item><c>--raw</c>: 生レスポンス JSON もダンプする</item>
///   <item><c>--import-book --print-asin X [--kindle-asin Y] [--series 3,7] [--genres MOOK,SETTING_BOOK]</c>:
///         ASIN から書籍を books 系テーブルへ登録する（<c>--dry-run</c> で内容確認のみ）</item>
///   <item><c>--attach-print --book-id N --print-asin X</c>: 登録済み書籍へ紙版の ASIN・ISBN・
///         ページ数・価格・書影を合流させる（電子だけで登録した書籍に紙版を足す用途）</item>
///   <item><c>--target products|books|all</c>: 巡回対象（既定 products。books は書籍の表紙のみ、all は両方）</item>
///   <item><c>--dry-run</c>: DB 更新せず取得結果だけ表示</item>
///   <item>引数なし: 鮮度切れ（未取得 or 90 日以上前）のみ取得</item>
/// </list>
/// </para>
/// Creators API のレート制限（1 TPS）を順守するため、各リクエストの間に 1100 ms の sleep を入れる。
/// </summary>
public static class Program
{
    /// <summary>鮮度判定の閾値（日数）。これより古い／未取得を再取得対象とする。</summary>
    private const int StaleDays = 90;

    /// <summary>Creators API レート制限（1 TPS）順守のためのリクエスト間隔ミリ秒。</summary>
    private const int RateLimitDelayMs = 1100;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            // CLI 引数のパース
            bool all = args.Any(a => a.Equals("--all", StringComparison.OrdinalIgnoreCase));
            bool dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
            // 既存の cover_image_source='amazon_cd'（CD 由来）の画像を、デジタル ASIN があるものに限って
            // デジタル由来へ差し替えるモード。CD（特に廃盤）は素人写真リスクがあるため、
            // 事業者アップが確実なデジタル画像へ「格上げ」する用途。
            bool upgradeCdToDigital = args.Any(a => a.Equals("--upgrade-cd-to-digital", StringComparison.OrdinalIgnoreCase));
            string? singleAsin = ReadOptionValue(args, "--asin");
            string? searchKeywords = ReadOptionValue(args, "--search");
            string searchIndexName = ReadOptionValue(args, "--index") ?? "Books";
            // 拡張リソース（著者ロール・ISBN・ページ数・判型・カテゴリ）を要求するか。
            // 書籍系の検索・取得は既定で拡張、音楽系は従来どおり最小集合。
            bool extended = args.Any(a => a.Equals("--extended", StringComparison.OrdinalIgnoreCase));
            bool raw = args.Any(a => a.Equals("--raw", StringComparison.OrdinalIgnoreCase));
            // 書籍取り込みモードのパラメータ。--import-book と併せて使う。
            bool importBook = args.Any(a => a.Equals("--import-book", StringComparison.OrdinalIgnoreCase));
            // 既存書籍への紙版合流モード（--attach-print --book-id N --print-asin X）。
            bool attachPrint = args.Any(a => a.Equals("--attach-print", StringComparison.OrdinalIgnoreCase));
            string? bookIdArg = ReadOptionValue(args, "--book-id");
            string? printAsin = ReadOptionValue(args, "--print-asin");
            string? kindleAsin = ReadOptionValue(args, "--kindle-asin");
            string? seriesIdsArg = ReadOptionValue(args, "--series");
            string? genreCodesArg = ReadOptionValue(args, "--genres");

            // Creators API クライアント（App.config のキーから）
            var paApi = PaApiClientFactory.TryCreateFromAppConfig();
            if (paApi == null)
            {
                Console.Error.WriteLine("ERROR: App.config に PaApi.CredentialId / PaApi.CredentialSecret / PaApi.CredentialVersion / PaApi.PartnerTag が設定されていません。");
                return 2;
            }

            // 単一 ASIN モード（接続不要のテスト用途）
            if (!string.IsNullOrWhiteSpace(singleAsin))
            {
                Console.WriteLine($"単一 ASIN テスト取得: {singleAsin}{(extended ? "（拡張リソース）" : "")}");
                var item = await paApi.GetItemAsync(singleAsin!, CancellationToken.None,
                    extended ? PaResourceSet.Extended : PaResourceSet.Standard);
                if (item == null)
                {
                    Console.WriteLine("該当商品なし、または応答が空でした。");
                    // GetItems のレスポンス構造（topkey "itemResults" → items[]）が想定と違うと
                    // パースで弾かれて item==null になる。生 JSON を吐いて実際のキー構造を確認する。
                    if (!string.IsNullOrEmpty(paApi.LastRawResponseJson))
                    {
                        Console.WriteLine();
                        Console.WriteLine("--- 生レスポンス JSON（診断用ダンプ） ---");
                        Console.WriteLine(paApi.LastRawResponseJson);
                    }
                    else
                    {
                        Console.WriteLine("(LastRawResponseJson も空 = HTTP 自体が失敗、または応答本文なし)");
                    }
                    return 0;
                }
                DumpItem(item);
                // 画像 URL が空のとき、API レスポンス側の構造（images.primary.* が来ているかどうか）を
                // 切り分けたいケースが頻発するため、診断用に生 JSON を末尾に丸ごと吐く。
                // --raw 指定時は無条件で吐く。
                if ((raw || (string.IsNullOrEmpty(item.LargeImageUrl) && string.IsNullOrEmpty(item.MediumImageUrl)))
                    && !string.IsNullOrEmpty(paApi.LastRawResponseJson))
                {
                    Console.WriteLine();
                    Console.WriteLine("--- 生レスポンス JSON（診断用ダンプ） ---");
                    Console.WriteLine(paApi.LastRawResponseJson);
                }
                return 0;
            }

            // キーワード検索モード（DB 更新なし・診断表示のみ）。
            // SearchIndex ごとに Amazon がどの属性を返すかを実地確認するための入り口で、
            // 書籍対応では Books / KindleStore の応答内容をここで検証する。
            if (!string.IsNullOrWhiteSpace(searchKeywords))
            {
                if (!TryParseSearchIndex(searchIndexName, out var searchIndex))
                {
                    Console.Error.WriteLine($"ERROR: --index の値が不正です: {searchIndexName}（Music / DigitalMusic / Books / KindleStore）");
                    return 2;
                }

                // 書籍系はロール付き著者・ISBN・ページ数まで見たいので既定で拡張リソースを要求する。
                bool useExtended = extended
                    || searchIndex == PaSearchIndex.Books
                    || searchIndex == PaSearchIndex.KindleStore;

                Console.WriteLine($"検索テスト: keywords=\"{searchKeywords}\" index={searchIndex} resources={(useExtended ? "Extended" : "Standard")}");
                var items = await paApi.SearchItemsAsync(searchKeywords!, searchIndex, itemCount: 10, CancellationToken.None,
                    useExtended ? PaResourceSet.Extended : PaResourceSet.Standard);
                Console.WriteLine($"取得件数: {items.Count}");
                int searchDumpNo = 0;
                foreach (var it in items)
                {
                    Console.WriteLine();
                    Console.WriteLine($"[{++searchDumpNo}]");
                    DumpItem(it);
                }
                if (raw && !string.IsNullOrEmpty(paApi.LastRawResponseJson))
                {
                    Console.WriteLine();
                    Console.WriteLine("--- 生レスポンス JSON（診断用ダンプ） ---");
                    Console.WriteLine(paApi.LastRawResponseJson);
                }
                return 0;
            }

            // 通常モード：DB 経由でバッチ取得
            string connStr = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString ?? "";
            if (string.IsNullOrWhiteSpace(connStr))
            {
                Console.Error.WriteLine("ERROR: App.config に DatastarsMySql 接続文字列が設定されていません。");
                return 2;
            }

            var factory = MySqlConnectionFactory.FromConnectionString(connStr);

            // 書籍取り込みモード。ASIN から書誌情報・表紙・クレジットを組み立てて books へ登録する。
            // --dry-run では組み立て内容を表示するだけで DB には触れない。
            if (importBook)
            {
                return await RunImportBookAsync(paApi, factory, printAsin, kindleAsin, seriesIdsArg, genreCodesArg, dryRun);
            }

            // 既存書籍への紙版合流モード。Kindle 版だけで登録済みの書籍に紙版の ASIN 等を足す。
            if (attachPrint)
            {
                if (!int.TryParse(bookIdArg, out int targetBookId) || string.IsNullOrWhiteSpace(printAsin))
                {
                    Console.Error.WriteLine("ERROR: --attach-print には --book-id と --print-asin が必要です。");
                    return 2;
                }
                var service = new BookImportService(
                    paApi,
                    new BooksRepository(factory),
                    new BookMastersRepository(factory),
                    new ProductCompaniesRepository(factory),
                    new PersonAliasesRepository(factory));
                var changes = await service.AttachPrintAsync(targetBookId, printAsin!, Environment.UserName, dryRun, CancellationToken.None);
                Console.WriteLine($"book_id={targetBookId} へ紙版 {printAsin} を合流{(dryRun ? "（dry-run）" : "")}");
                foreach (var c in changes) Console.WriteLine($"  - {c}");
                return 0;
            }
            // 巡回対象の切り替え。既定は従来どおり音楽商品のみ。
            string target = (ReadOptionValue(args, "--target") ?? "products").ToLowerInvariant();
            if (target is not ("products" or "books" or "all"))
            {
                Console.Error.WriteLine($"ERROR: --target の値が不正です: {target}（products / books / all）");
                return 2;
            }

            // 書籍だけを回すモードでは音楽商品側の処理を丸ごと飛ばす。
            if (target == "books")
            {
                return await RunBookCoverRefreshAsync(paApi, factory, all, dryRun);
            }

            var repo = new ProductsRepository(factory);
            var all_products = await repo.GetAllAsync();

            // 対象抽出。
            var threshold = DateTime.Now.AddDays(-StaleDays);
            List<Product> targets;
            string modeLabel;
            if (upgradeCdToDigital)
            {
                // CD 由来カバーかつデジタル ASIN を持つものだけ。デジタル画像へ差し替える。
                targets = all_products
                    .Where(p => string.Equals(p.CoverImageSource, "amazon_cd", StringComparison.Ordinal)
                             && !string.IsNullOrWhiteSpace(p.AmazonAsinDigital))
                    .ToList();
                modeLabel = "CD 由来 → デジタル差し替え";
            }
            else
            {
                // ASIN を 1 つでも持っていて、（全件モード or 未取得 or 鮮度切れ）の商品。
                // 「未取得」は CD/デジタルを別々に判定する：ASIN がある側の cover 列が空なら対象とする。
                // これにより「片方だけ取れている中途半端な状態」（例：デジタルだけ入って CD 列が空）も
                // 自動的に再取得対象になり、両列が揃う（CoverImageUrl 計算プロパティ任せだと
                // 片方でも非空なら未取得扱いにならずスキップされてしまう問題への対処）。
                targets = all_products
                    .Where(p =>
                    {
                        bool hasCdAsin = !string.IsNullOrWhiteSpace(p.AmazonAsinCd);
                        bool hasDigitalAsin = !string.IsNullOrWhiteSpace(p.AmazonAsinDigital);
                        if (!hasCdAsin && !hasDigitalAsin) return false;
                        bool missingCover =
                            (hasCdAsin && string.IsNullOrWhiteSpace(p.CoverImageUrlCd))
                            || (hasDigitalAsin && string.IsNullOrWhiteSpace(p.CoverImageUrlDigital));
                        return all
                            || missingCover
                            || p.CoverImageFetchedAt is null
                            || p.CoverImageFetchedAt < threshold;
                    })
                    .ToList();
                modeLabel = all ? "全件強制" : $"未取得・鮮度切れ {StaleDays} 日以上";
            }

            Console.WriteLine($"対象商品: {targets.Count} 件 ({modeLabel})");
            if (dryRun) Console.WriteLine("(dry-run モード：DB 更新は行いません)");

            int ok = 0, miss = 0;
            int idx = 0;
            foreach (var prod in targets)
            {
                idx++;
                Console.Write($"[{idx}/{targets.Count}] {prod.ProductCatalogNo,-20} ... ");

                // CD・デジタル両系統を取得して両列に保存する（CD とデジタルでジャケットが
                // 異なる場合があるため、片方を採用しても両方を保持して後から切り替えられるようにする）。
                string? digitalUrl = null;
                string? cdUrl = null;

                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinDigital))
                {
                    try
                    {
                        var item = await paApi.GetItemAsync(prod.AmazonAsinDigital!, CancellationToken.None);
                        if (item?.LargeImageUrl is { Length: > 0 } u) digitalUrl = u;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ! Digital ASIN 取得失敗: {ex.Message}");
                    }
                    await Task.Delay(RateLimitDelayMs);
                }
                if (!string.IsNullOrWhiteSpace(prod.AmazonAsinCd))
                {
                    try
                    {
                        var item = await paApi.GetItemAsync(prod.AmazonAsinCd!, CancellationToken.None);
                        if (item?.LargeImageUrl is { Length: > 0 } u) cdUrl = u;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ! CD ASIN 取得失敗: {ex.Message}");
                    }
                    await Task.Delay(RateLimitDelayMs);
                }

                if (digitalUrl != null || cdUrl != null)
                {
                    // 採用ソース決定：upgrade モードはデジタルへ格上げ、それ以外は既存の明示選択を尊重しつつ
                    // 未選択／選択先が空ならデジタル→CD の既定優先（DecideCoverSource 参照）。
                    string? source = DecideCoverSource(prod.CoverImageSource, cdUrl, digitalUrl, upgradeCdToDigital);
                    if (!dryRun)
                    {
                        await repo.UpdateCoverImagesAsync(prod.ProductCatalogNo, cdUrl, digitalUrl, source, DateTime.Now);
                    }
                    ok++;
                    Console.WriteLine($"OK (CD={(cdUrl != null ? "有" : "無")} / デジタル={(digitalUrl != null ? "有" : "無")} / 採用={source ?? "なし"})");
                }
                else
                {
                    miss++;
                    Console.WriteLine("該当画像なし");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"完了: 取得成功 {ok} 件 / 失敗・該当なし {miss} 件");

            // --target all では音楽商品の巡回に続けて書籍も回す。
            if (target == "all")
            {
                Console.WriteLine();
                return await RunBookCoverRefreshAsync(paApi, factory, all, dryRun);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex);
            return 1;
        }
    }

    /// <summary>
    /// 表示に採用するジャケット画像ソースを決める。
    /// <list type="bullet">
    ///   <item><paramref name="upgrade"/>（--upgrade-cd-to-digital）が true：デジタルがあれば <c>amazon_digital</c> へ格上げ、無ければ CD。</item>
    ///   <item>既存の明示選択（<paramref name="existing"/> が <c>amazon_cd</c> / <c>amazon_digital</c>）があり、その URL が取れていれば尊重する。</item>
    ///   <item>それ以外（未選択、または選択先の URL が空）はデジタル → CD の既定優先。</item>
    ///   <item>両方とも URL が無ければ null（未選択）。</item>
    /// </list>
    /// 既定優先がデジタルのため、「人が CD へ切り替えた」選択だけが実質的に尊重される
    /// （digital 選択は既定と一致するので区別不要）。
    /// </summary>
    private static string? DecideCoverSource(string? existing, string? cdUrl, string? digitalUrl, bool upgrade)
    {
        bool hasCd = !string.IsNullOrEmpty(cdUrl);
        bool hasDigital = !string.IsNullOrEmpty(digitalUrl);
        if (!hasCd && !hasDigital) return null;
        if (upgrade) return hasDigital ? "amazon_digital" : "amazon_cd";
        if (existing == "amazon_digital" && hasDigital) return "amazon_digital";
        if (existing == "amazon_cd" && hasCd) return "amazon_cd";
        return hasDigital ? "amazon_digital" : "amazon_cd";
    }

    /// <summary>
    /// 書籍の表紙画像を巡回取得する。判定規則は音楽商品と同じで、
    /// 「ASIN を 1 つ以上持ち、かつ（全件モード or ASIN のある側の書影列が空 or 鮮度切れ）」を対象にする。
    /// <para>
    /// 採用ソースは Kindle 優先（電子は事業者アップの正規画像が確実で、紙は絶版書で出品者の
    /// 撮影画像が混ざりうるため）。ただし人が紙を明示選択していて、その URL が取れている場合は
    /// その選択を尊重する。
    /// </para>
    /// </summary>
    private static async Task<int> RunBookCoverRefreshAsync(
        PaApiClient paApi, MySqlConnectionFactory factory, bool all, bool dryRun)
    {
        var booksRepo = new BooksRepository(factory);
        var books = await booksRepo.GetAllAsync();
        var threshold = DateTime.Now.AddDays(-StaleDays);

        var targets = books
            .Where(b =>
            {
                bool hasPrintAsin = !string.IsNullOrWhiteSpace(b.AmazonAsinPrint);
                bool hasKindleAsin = !string.IsNullOrWhiteSpace(b.AmazonAsinKindle);
                if (!hasPrintAsin && !hasKindleAsin) return false;
                bool missingCover =
                    (hasPrintAsin && string.IsNullOrWhiteSpace(b.CoverImageUrlPrint))
                    || (hasKindleAsin && string.IsNullOrWhiteSpace(b.CoverImageUrlKindle));
                return all
                    || missingCover
                    || b.CoverImageFetchedAt is null
                    || b.CoverImageFetchedAt < threshold;
            })
            .ToList();

        Console.WriteLine($"対象書籍: {targets.Count} 件 ({(all ? "全件強制" : $"未取得・鮮度切れ {StaleDays} 日以上")})");
        if (dryRun) Console.WriteLine("(dry-run モード：DB 更新は行いません)");

        int ok = 0, miss = 0, idx = 0;
        foreach (var book in targets)
        {
            idx++;
            Console.Write($"[{idx}/{targets.Count}] book_id={book.BookId,-5} ... ");

            string? printUrl = null;
            string? kindleUrl = null;

            if (!string.IsNullOrWhiteSpace(book.AmazonAsinPrint))
            {
                try
                {
                    var item = await paApi.GetItemAsync(book.AmazonAsinPrint!, CancellationToken.None);
                    if (item?.LargeImageUrl is { Length: > 0 } u) printUrl = u;
                }
                catch (Exception ex) { Console.WriteLine($"  ! 紙 ASIN 取得失敗: {ex.Message}"); }
                await Task.Delay(RateLimitDelayMs);
            }
            if (!string.IsNullOrWhiteSpace(book.AmazonAsinKindle))
            {
                try
                {
                    var item = await paApi.GetItemAsync(book.AmazonAsinKindle!, CancellationToken.None);
                    if (item?.LargeImageUrl is { Length: > 0 } u) kindleUrl = u;
                }
                catch (Exception ex) { Console.WriteLine($"  ! Kindle ASIN 取得失敗: {ex.Message}"); }
                await Task.Delay(RateLimitDelayMs);
            }

            if (printUrl != null || kindleUrl != null)
            {
                string? source = DecideBookCoverSource(book.CoverImageSource, printUrl, kindleUrl);
                if (!dryRun)
                {
                    await booksRepo.UpdateCoverImagesAsync(book.BookId, printUrl, kindleUrl, source, DateTime.Now);
                }
                ok++;
                Console.WriteLine($"OK (紙={(printUrl != null ? "有" : "無")} / Kindle={(kindleUrl != null ? "有" : "無")} / 採用={source ?? "なし"})");
            }
            else
            {
                miss++;
                Console.WriteLine("該当画像なし");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"書籍完了: 取得成功 {ok} 件 / 失敗・該当なし {miss} 件");
        return 0;
    }

    /// <summary>
    /// 書籍の表紙で採用するソースを決める。既定は Kindle 優先で、人が紙を明示選択していて
    /// その URL が取れているときだけその選択を尊重する。両方とも URL が無ければ null。
    /// </summary>
    private static string? DecideBookCoverSource(string? existing, string? printUrl, string? kindleUrl)
    {
        bool hasPrint = !string.IsNullOrEmpty(printUrl);
        bool hasKindle = !string.IsNullOrEmpty(kindleUrl);
        if (!hasPrint && !hasKindle) return null;
        if (existing == "amazon_print" && hasPrint) return "amazon_print";
        return hasKindle ? "amazon_kindle" : "amazon_print";
    }

    /// <summary>
    /// 書籍取り込みモードの本体。ASIN から取り込み内容を組み立てて表示し、
    /// <paramref name="dryRun"/> でなければ books 系テーブルへ登録する。
    /// シリーズ・ジャンルは Amazon から確定できないので、CLI 引数で明示的に受け取る
    /// （未指定ならシリーズ紐付けなし＝オールスターズ扱い、ジャンルなしで登録される）。
    /// </summary>
    private static async Task<int> RunImportBookAsync(
        PaApiClient paApi,
        MySqlConnectionFactory factory,
        string? printAsin,
        string? kindleAsin,
        string? seriesIdsArg,
        string? genreCodesArg,
        bool dryRun)
    {
        var booksRepo = new BooksRepository(factory);
        var service = new BookImportService(
            paApi,
            booksRepo,
            new BookMastersRepository(factory),
            new ProductCompaniesRepository(factory),
            new PersonAliasesRepository(factory));

        var plan = await service.BuildPlanAsync(printAsin, kindleAsin, CancellationToken.None);

        foreach (var e in plan.Errors) Console.Error.WriteLine($"ERROR: {e}");
        if (plan.Errors.Count > 0 || plan.Book == null) return 2;

        var book = plan.Book;
        Console.WriteLine("── 取り込み内容 ──");
        Console.WriteLine($"  書名        : {book.Title}");
        Console.WriteLine($"  発売日      : {book.ReleaseDate:yyyy-MM-dd}");
        if (book.ReleaseDateKindle.HasValue)
            Console.WriteLine($"  Kindle 配信 : {book.ReleaseDateKindle:yyyy-MM-dd}");
        Console.WriteLine($"  出版社 ID   : {(book.PublisherProductCompanyId?.ToString() ?? "(未設定)")}");
        Console.WriteLine($"  ISBN13      : {book.Isbn13 ?? "(なし)"}");
        Console.WriteLine($"  ページ数    : {(book.PageCount?.ToString() ?? "(不明)")}");
        Console.WriteLine($"  装丁        : {book.BindingText ?? "(不明)"}");
        Console.WriteLine($"  版          : 紙={book.HasPrint} / Kindle={book.HasKindle}");
        Console.WriteLine($"  ASIN        : 紙={book.AmazonAsinPrint ?? "-"} / Kindle={book.AmazonAsinKindle ?? "-"}");
        Console.WriteLine($"  価格        : 紙={(book.PriceIncTax?.ToString() ?? "-")} / Kindle={(book.PriceKindleIncTax?.ToString() ?? "-")}");
        Console.WriteLine($"  表紙        : {book.CoverImageUrl ?? "(なし)"}");

        if (plan.Credits.Count > 0)
        {
            Console.WriteLine("  クレジット  :");
            foreach (var c in plan.Credits)
            {
                string who = c.PersonAliasId.HasValue ? $"alias#{c.PersonAliasId}" : $"\"{c.CreditText}\"（マスタ未紐付け）";
                Console.WriteLine($"    - {c.RoleCode}: {who}");
            }
        }
        if (plan.BrowseNodes.Count > 0)
            Console.WriteLine($"  Amazon 分類 : {string.Join(" / ", plan.BrowseNodes.Take(6))}");

        foreach (var w in plan.Warnings) Console.WriteLine($"  [警告] {w}");

        if (plan.ExistingBookId.HasValue)
        {
            Console.Error.WriteLine($"ERROR: 同じ ISBN / ASIN の書籍が既に登録されています（book_id={plan.ExistingBookId}）。取り込みを中止しました。");
            return 2;
        }

        var seriesIds = ParseIntList(seriesIdsArg);
        var genreCodes = ParseCodeList(genreCodesArg);
        Console.WriteLine($"  シリーズ    : {(seriesIds.Count > 0 ? string.Join(", ", seriesIds) : "(なし＝オールスターズ扱い)")}");
        Console.WriteLine($"  ジャンル    : {(genreCodes.Count > 0 ? string.Join(", ", genreCodes) : "(なし)")}");

        if (dryRun)
        {
            Console.WriteLine("--dry-run のため DB へは書き込みませんでした。");
            return 0;
        }

        int bookId = await service.SaveAsync(plan, seriesIds, genreCodes, Environment.UserName, CancellationToken.None);
        Console.WriteLine($"登録しました: book_id={bookId}");
        return 0;
    }

    /// <summary>カンマ区切りの整数リストを解析する。null / 空なら空リスト。</summary>
    private static List<int> ParseIntList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<int>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int v) ? v : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
    }

    /// <summary>カンマ区切りのコードリストを解析する。null / 空なら空リスト。</summary>
    private static List<string> ParseCodeList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToList();
    }

    /// <summary>
    /// <c>--option value</c> 形式の引数から値を 1 つ読む。指定が無ければ null。
    /// 最後の要素がオプション名だけで値を伴わない場合も null（範囲外参照を避ける）。
    /// </summary>
    private static string? ReadOptionValue(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    /// <summary><c>--index</c> の文字列を <see cref="PaSearchIndex"/> に解決する。大文字小文字は無視。</summary>
    private static bool TryParseSearchIndex(string name, out PaSearchIndex index)
        => Enum.TryParse(name, ignoreCase: true, out index) && Enum.IsDefined(typeof(PaSearchIndex), index);

    /// <summary>
    /// 診断表示用に <see cref="PaItem"/> の全項目をコンソールへ並べる。
    /// 拡張リソースでのみ埋まる書籍向け属性（寄与者・ISBN・ページ数・判型・カテゴリ）は、
    /// 値があるときだけ行を出して Standard 取得時の出力が膨らまないようにする。
    /// </summary>
    private static void DumpItem(PaItem item)
    {
        Console.WriteLine($"  ASIN          : {item.Asin}");
        Console.WriteLine($"  Title         : {item.Title}");
        Console.WriteLine($"  ByLine        : {item.ByLine}");
        Console.WriteLine($"  PriceDisplay  : {item.PriceDisplay}");
        Console.WriteLine($"  ReleaseDate   : {item.ReleaseDate}");
        Console.WriteLine($"  LargeImageUrl : {item.LargeImageUrl}");
        Console.WriteLine($"  MediumImageUrl: {item.MediumImageUrl}");
        if (item.Contributors.Count > 0)
            Console.WriteLine($"  Contributors  : {string.Join(" / ", item.Contributors)}");
        if (!string.IsNullOrEmpty(item.Manufacturer)) Console.WriteLine($"  Manufacturer  : {item.Manufacturer}");
        if (!string.IsNullOrEmpty(item.Brand)) Console.WriteLine($"  Brand         : {item.Brand}");
        if (!string.IsNullOrEmpty(item.PublicationDate)) Console.WriteLine($"  PublicationDt : {item.PublicationDate}");
        if (item.PagesCount.HasValue) Console.WriteLine($"  PagesCount    : {item.PagesCount}");
        if (!string.IsNullOrEmpty(item.Edition)) Console.WriteLine($"  Edition       : {item.Edition}");
        if (!string.IsNullOrEmpty(item.Binding)) Console.WriteLine($"  Binding       : {item.Binding}");
        if (!string.IsNullOrEmpty(item.ProductGroup)) Console.WriteLine($"  ProductGroup  : {item.ProductGroup}");
        if (!string.IsNullOrEmpty(item.Isbn)) Console.WriteLine($"  ISBN          : {item.Isbn}");
        if (!string.IsNullOrEmpty(item.Ean)) Console.WriteLine($"  EAN           : {item.Ean}");
        if (item.BrowseNodes.Count > 0)
            Console.WriteLine($"  BrowseNodes   : {string.Join(" / ", item.BrowseNodes)}");
    }
}
