using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 人物・キャラクター・企業・書籍の詳細ページ URL を、通し番号（ID）ではなく名前・コードから決める台帳。
/// ビルド開始時に 1 度だけ組み立て、<see cref="PathUtil.PersonUrl"/> 等の URL 組み立てはすべてここを引く。
/// <list type="bullet">
///   <item><description>人物 <c>/people/{名前}/</c>・企業 <c>/companies/{名前}/</c>・キャラ <c>/characters/{名前}/</c>。
///     名前は <see cref="UrlSlug.FromName"/> で整えたもの。企業・キャラはマスタの正式名（companies.name / characters.name）、
///     人物は表示名義（<see cref="DisplayPersonAliasId"/>）。表示名義は ① 指定した本名義（persons.primary_alias_id）
///     → ② いま公開している名義（published_entity_slugs で最後に記録したスラッグに当たる名義）→ ③ 最新名義
///     （<see cref="LatestAliasResolver.LatestPersonAliasIds"/>、TV 系のクレジットで最後に使われた名義）の順に決める。
///     いったん公開した人物はクレジットの入力が進んでも名乗りが変わらず、変えるのは本名義を指定したときだけになる。
///     どれも無い人物は正式名 persons.full_name。人物詳細の見出しもこの名前にそろえる（<see cref="PersonDisplayName"/>）。</description></item>
///   <item><description>人物の URL は本名義を指定すると変わり、キャラの URL はキャラ名を、企業の URL は正式名を変えると変わる。
///     本番デプロイで公開した人物・キャラ・企業の URL は台帳 <c>published_entity_slugs</c> に記録しておき（<see cref="RecordPublishedSlugsAsync"/>）、
///     いまの URL と違う記録済みの旧 URL は新 URL へ 301 で転送する（<see cref="LegacyRedirects"/> に <c>/people/{旧名}</c>・
///     <c>/characters/{旧名}</c>・<c>/companies/{旧名}</c> として載せる。同じ区分の別の実体がいまその名前の URL を使っていれば転送しない）。
///     個別ページを持っていたキャラが単発キャラ扱いに変わったときは、ゲストキャラクターページの登場話へ転送する。</description></item>
///   <item><description>同じ区分の中で名前（大文字小文字を区別しない）が衝突したら、キャラクターは全員に出身作品を
///     「長老 (ふたりはプリキュア)」の形で添えて分ける（<see cref="QualifyCollidingCharacterNames"/>）。それで分けられない組と、
///     人物・企業・書籍の衝突は、ID の若い 1 件が素の名前を持ち、残りは <c>_2</c>, <c>_3</c> … を付けて警告を出す
///     （付け方はその都度判断して名前側で解消する前提の仮措置）。
///     数字だけの名前は旧 URL（<c>/persons/123/</c>）と区別できないため末尾に <c>_</c> を足す。</description></item>
///   <item><description>書籍 <c>/books/{コード}/</c>。コードは ISBN-13 → 定期刊行物コード → Kindle ASIN → 紙の ASIN の
///     順に最初にあるもの。どれも無い書籍は書名から作る（警告を出す）。その本が持つほかのコードの URL からも、
///     いまの URL へ 301 で転送する（あとから ISBN を入れて URL が変わった本の旧 URL を 404 にしない）。</description></item>
///   <item><description>単発キャラ（<see cref="IsGuestCharacter"/>）は個別ページを持たず、登場シリーズごとの
///     ゲストキャラクターページ <c>/characters/guests/{series_slug}/</c> にまとめる。単発キャラへのリンクは
///     そのページの登場話アンカー（<c>#ep{話数}</c>、映画はアンカー無し）を指す。</description></item>
///   <item><description>旧 ID URL から新 URL への対応表（<see cref="LegacyRedirects"/>）も併せて作る。旧 ID は
///     URL を切り替えた時点で凍結した台帳 <c>legacy_entity_ids</c> から引き（ID を振り直しても旧 URL は元の実体を指す）、
///     サイト出力の <c>_edge/legacy-redirects.json</c> に書き出して、origin-request の Lambda@Edge が 301 転送に使う
///     （<see cref="Utilities.LegacyRedirectMapWriter"/>）。</description></item>
/// </list>
/// </summary>
public sealed class EntityUrlRegistry
{
    /// <summary>ゲストキャラクターページを置く <c>/characters/</c> 配下の予約セグメント。</summary>
    public const string GuestsSegment = "guests";

    /// <summary>
    /// 廃止したページの転送表（末尾スラッシュ無しの旧パス → 転送先 URL）。<see cref="LegacyRedirects"/> に毎ビルド載せる。
    /// 歌唱系の役職詳細（歌・コーラス）は歌唱ページ <c>/creators/singers/</c> に集約した。
    /// </summary>
    private static readonly (string From, string To)[] RetiredPageRedirects =
    {
        ("/creators/roles/vocals", PathUtil.CreatorsSingersUrl()),
        ("/creators/roles/backing_vocals", PathUtil.CreatorsSingersUrl()),
    };

    private readonly Dictionary<int, string> _personUrls = new();
    /// <summary>person_id → いまの人物 URL のスラッグ（デコード済み）。公開記録と旧名転送の突き合わせに使う。</summary>
    private readonly Dictionary<int, string> _personSlugs = new();
    /// <summary>person_id → 見出し名・読み（表示名義。表示名義の無い人物は正式名）。</summary>
    private readonly Dictionary<int, (string Name, string Kana)> _personNames = new();
    /// <summary>person_id → 表示名義の person_alias_id（本名義 → 公開中の名義 → 最新名義。どれも無い人物は載らない）。</summary>
    private readonly Dictionary<int, int> _displayPersonAliasIds = new();
    private readonly Dictionary<int, string> _companyUrls = new();
    /// <summary>company_id → いまの企業 URL のスラッグ（デコード済み）。公開記録と旧名転送の突き合わせに使う。</summary>
    private readonly Dictionary<int, string> _companySlugs = new();
    private readonly Dictionary<int, string> _characterUrls = new();
    /// <summary>character_id → いまのキャラ詳細 URL のスラッグ（デコード済み）。個別ページを持つキャラだけ載る（単発キャラは載らない）。
    /// 公開記録と旧名転送の突き合わせに使う。</summary>
    private readonly Dictionary<int, string> _characterSlugs = new();
    private readonly Dictionary<int, string> _bookUrls = new();
    private readonly Dictionary<int, GuestPlacement> _guestPlacements = new();
    private readonly List<LegacyRedirect> _legacyRedirects = new();

    private EntityUrlRegistry() { }

    /// <summary>未構築時（Catalog 側プレビュー等）用の空台帳。URL は旧来の ID 形式にフォールバックする。</summary>
    public static EntityUrlRegistry Empty { get; } = new();

    /// <summary>人物詳細ページの URL（パーセントエンコード済み）。台帳に無い ID は null。</summary>
    public string? PersonUrl(int personId) => _personUrls.TryGetValue(personId, out var u) ? u : null;

    /// <summary>人物の見出し名（表示名義。表示名義の無い人物は正式名）。台帳に無い ID は null。</summary>
    public string? PersonDisplayName(int personId) => _personNames.TryGetValue(personId, out var n) ? n.Name : null;

    /// <summary>人物の見出し名の読み（表示名義の読み。読み未登録なら空文字）。台帳に無い ID は null。</summary>
    public string? PersonDisplayKana(int personId) => _personNames.TryGetValue(personId, out var n) ? n.Kana : null;

    /// <summary>
    /// 人物の表示名義（見出し・URL・一覧の行表記に使う person_alias_id）。本名義 → 公開中の名義 → 最新名義の順に決めたもの。
    /// どれも無い（本名義の指定もクレジットも無い）人物は null。
    /// </summary>
    public int? DisplayPersonAliasId(int personId) => _displayPersonAliasIds.TryGetValue(personId, out var a) ? a : null;

    /// <summary>企業詳細ページの URL（パーセントエンコード済み）。台帳に無い ID は null。</summary>
    public string? CompanyUrl(int companyId) => _companyUrls.TryGetValue(companyId, out var u) ? u : null;

    /// <summary>キャラクターへのリンク先 URL（パーセントエンコード済み）。単発キャラはゲストページの登場話アンカー。台帳に無い ID は null。</summary>
    public string? CharacterUrl(int characterId) => _characterUrls.TryGetValue(characterId, out var u) ? u : null;

    /// <summary>書籍詳細ページの URL（パーセントエンコード済み）。台帳に無い ID は null。</summary>
    public string? BookUrl(int bookId) => _bookUrls.TryGetValue(bookId, out var u) ? u : null;

    /// <summary>単発キャラ（個別ページを作らずゲストキャラクターページにまとめるキャラ）か。</summary>
    public bool IsGuestCharacter(int characterId) => _guestPlacements.ContainsKey(characterId);

    /// <summary>単発キャラの唯一の登場位置。単発キャラでなければ null。</summary>
    public GuestPlacement? GetGuestPlacement(int characterId)
        => _guestPlacements.TryGetValue(characterId, out var p) ? p : null;

    /// <summary>series_id → そのシリーズにまとめる単発キャラの character_id 群。</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> GuestCharacterIdsBySeries
        => _guestPlacements
            .GroupBy(kv => kv.Value.SeriesId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(kv => kv.Key).OrderBy(id => id).ToList());

    /// <summary>旧 ID URL → 新 URL の対応表（301 転送用）。</summary>
    public IReadOnlyList<LegacyRedirect> LegacyRedirects => _legacyRedirects;

    /// <summary>シリーズのゲストキャラクターページ URL。</summary>
    public static string GuestCharactersUrl(string seriesSlug) => $"/characters/{GuestsSegment}/{seriesSlug}/";

    /// <summary>ゲストキャラクターページ内の登場話アンカー ID（映画などエピソードの無い登場は空文字）。</summary>
    public static string GuestEpisodeAnchor(int? seriesEpNo) => seriesEpNo is int n ? $"ep{n}" : "";

    /// <summary>
    /// マスタ・クレジット逆引きインデックスから台帳を組み立てる。
    /// 単発キャラの判定にクレジット関与を使うため、<see cref="CreditInvolvementIndex"/> 構築後に呼ぶ。
    /// </summary>
    public static async Task<EntityUrlRegistry> BuildAsync(
        BuildContext ctx, IConnectionFactory factory, CreditInvolvementIndex index, CancellationToken ct)
    {
        var persons = await new PersonsRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var companies = await new CompaniesRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var characters = await new CharactersRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var books = await new BooksRepository(factory).GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);

        var reg = new EntityUrlRegistry();

        // 人物の表示名義を決める。① 本名義（persons.primary_alias_id。その人物の名義であるときだけ有効）
        // → ② いま公開している名義（本番で最後に記録したスラッグに当たる名義。正式名で公開していた人物は名義に当てない）
        // → ③ 最新名義。どれも無い人物は正式名で名乗る。
        var latestAliasIds = LatestAliasResolver.LatestPersonAliasIds(ctx, index);
        var publishedPersonSlugs = await LoadCurrentPublishedPersonSlugsAsync(factory, ct).ConfigureAwait(false);
        foreach (var p in persons)
        {
            var ownAliasIds = ctx.AliasIdsByPerson.TryGetValue(p.PersonId, out var ids) ? ids : Array.Empty<int>();
            int? chosen = null;
            if (p.PrimaryAliasId is int primary && ownAliasIds.Contains(primary))
            {
                chosen = primary;
            }
            else if (p.PrimaryAliasId is int invalid)
            {
                ctx.Logger.Warn($"persons: 「{p.FullName}」(person_id={p.PersonId}) の本名義 alias_id={invalid} はこの人物の名義ではないため使いません。");
            }
            if (chosen is null && publishedPersonSlugs.TryGetValue(p.PersonId, out var publishedSlug))
            {
                foreach (var aid in ownAliasIds)
                {
                    if (ctx.PersonAliasById.TryGetValue(aid, out var a)
                        && string.Equals(UrlSlug.FromName(a.Name), publishedSlug, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = aid;
                        break;
                    }
                }
            }
            if (chosen is null && latestAliasIds.TryGetValue(p.PersonId, out var latest))
                chosen = latest;
            if (chosen is int c) reg._displayPersonAliasIds[p.PersonId] = c;
        }
        foreach (var p in persons)
        {
            if (reg._displayPersonAliasIds.TryGetValue(p.PersonId, out var aid)
                && ctx.PersonAliasById.TryGetValue(aid, out var alias))
            {
                // 読みは名義の読み。名義が正式名と同じ表記なら、名義側に読みが無くても正式名の読みを使う。
                string kana = alias.NameKana
                    ?? (string.Equals(alias.Name, p.FullName, StringComparison.Ordinal) ? p.FullNameKana : null)
                    ?? "";
                reg._personNames[p.PersonId] = (alias.Name, kana);
            }
            else
            {
                reg._personNames[p.PersonId] = (p.FullName, p.FullNameKana ?? "");
            }
        }
        foreach (var (id, slug) in AssignSlugs("persons", persons.Select(p => (p.PersonId, reg._personNames[p.PersonId].Name)), ctx.Logger))
        {
            reg._personSlugs[id] = slug;
            reg._personUrls[id] = $"/people/{UrlSlug.Encode(slug)}/";
        }

        foreach (var (id, slug) in AssignSlugs("companies", companies.Select(c => (c.CompanyId, c.Name)), ctx.Logger))
        {
            reg._companySlugs[id] = slug;
            reg._companyUrls[id] = $"/companies/{UrlSlug.Encode(slug)}/";
        }

        // 単発キャラを先に決め、残りのキャラだけで名前の衝突を判定する（単発キャラは名前 URL を持たない）。
        foreach (var (characterId, placement) in DetectGuestCharacters(ctx, index, characters))
        {
            reg._guestPlacements[characterId] = placement;
            var series = ctx.SeriesById[placement.SeriesId];
            var anchor = GuestEpisodeAnchor(placement.SeriesEpNo);
            reg._characterUrls[characterId] = GuestCharactersUrl(series.Slug) + (anchor.Length > 0 ? "#" + anchor : "");
        }
        var namedCharacters = QualifyCollidingCharacterNames(
            ctx, index,
            characters.Where(c => !reg._guestPlacements.ContainsKey(c.CharacterId)).Select(c => (c.CharacterId, c.Name)).ToList());
        foreach (var (id, slug) in AssignSlugs("characters", namedCharacters, ctx.Logger, reserved: GuestsSegment))
        {
            reg._characterSlugs[id] = slug;
            reg._characterUrls[id] = $"/characters/{UrlSlug.Encode(slug)}/";
        }

        // 書籍はコードをそのまま URL にする（数字だけの ISBN・定期刊行物コードも旧 ID と桁数で区別できるので許す）。
        var bookCodes = books
            .Where(b => !b.IsDeleted)
            .Select(b =>
            {
                string? code = new[] { b.Isbn13, b.PeriodicalCode, b.AmazonAsinKindle, b.AmazonAsinPrint }
                    .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
                if (code is null)
                    ctx.Logger.Warn($"books: 「{b.Title}」は ISBN・定期刊行物コード・ASIN のいずれも無いため、書名から URL を作りました。");
                return (b.BookId, Name: code ?? b.Title);
            });
        foreach (var (id, slug) in AssignSlugs("books", bookCodes, ctx.Logger, allowNumeric: true))
            reg._bookUrls[id] = $"/books/{UrlSlug.Encode(slug)}/";

        // 旧 ID URL の転送表。旧 ID は台帳 legacy_entity_ids（URL 切り替え時点で凍結）から引き、
        // 転送先はいまの実体の新 URL。実体が台帳に無い（削除済み等の）旧 ID は転送しない。
        await using (var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false))
        {
            const string sql = """
                SELECT entity_kind AS EntityKind, legacy_id AS LegacyId,
                       COALESCE(person_id, character_id, company_id, book_id) AS EntityId
                  FROM legacy_entity_ids
                 ORDER BY entity_kind, legacy_id
                """;
            var legacyRows = await conn.QueryAsync<LegacyEntityRow>(
                new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
            foreach (var row in legacyRows)
            {
                var (section, urls) = row.EntityKind switch
                {
                    // 旧 URL の区分名は persons（新 URL は people）。
                    "PERSON" => ("persons", reg._personUrls),
                    "CHARACTER" => ("characters", reg._characterUrls),
                    "COMPANY" => ("companies", reg._companyUrls),
                    "BOOK" => ("books", reg._bookUrls),
                    _ => ("", null)
                };
                if (urls is null || row.EntityId is not int eid || !urls.TryGetValue(eid, out var to)) continue;
                reg._legacyRedirects.Add(new LegacyRedirect($"/{section}/{row.LegacyId}", to));
            }

            // 旧名の人物・キャラ・企業 URL の転送表。本番で公開した記録（published_entity_slugs）のうち、いまの URL と違うものを
            // いまの URL へ転送する。同じ区分でいま別の実体がその名前の URL を使っている（ページが実在する）ときは転送しない。
            // 転送元のキーはデコード済みのスラッグで持つ（Lambda@Edge 側でリクエスト URI をデコードして引く）。
            const string publishedSql = """
                SELECT entity_kind AS EntityKind, slug AS Slug, COALESCE(person_id, character_id, company_id) AS EntityId
                  FROM published_entity_slugs
                 WHERE entity_kind IN ('PERSON', 'CHARACTER', 'COMPANY')
                 ORDER BY entity_kind, slug
                """;
            var published = await conn.QueryAsync<PublishedSlugRow>(
                new CommandDefinition(publishedSql, cancellationToken: ct)).ConfigureAwait(false);
            var livePersonSlugs = new HashSet<string>(reg._personSlugs.Values, StringComparer.OrdinalIgnoreCase);
            var liveCharacterSlugs = new HashSet<string>(reg._characterSlugs.Values, StringComparer.OrdinalIgnoreCase);
            var liveCompanySlugs = new HashSet<string>(reg._companySlugs.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var row in published)
            {
                var (section, liveSlugs, urls) = row.EntityKind switch
                {
                    "PERSON" => ("people", livePersonSlugs, reg._personUrls),
                    "CHARACTER" => ("characters", liveCharacterSlugs, reg._characterUrls),
                    "COMPANY" => ("companies", liveCompanySlugs, reg._companyUrls),
                    _ => ("", null, null)
                };
                if (liveSlugs is null || urls is null || row.EntityId is not int eid) continue;
                if (liveSlugs.Contains(row.Slug)) continue;
                if (!urls.TryGetValue(eid, out var to)) continue;
                reg._legacyRedirects.Add(new LegacyRedirect($"/{section}/{row.Slug}", to));
            }
        }

        foreach (var (from, to) in RetiredPageRedirects)
            reg._legacyRedirects.Add(new LegacyRedirect(from, to));

        // 書籍は URL に使うコードの優先順（ISBN-13 → 定期刊行物コード → Kindle ASIN → 紙の ASIN）が変わると URL が変わる
        // （Kindle だけ登録していた本にあとから紙版の ISBN を入れたときなど）。その本が持つほかのコードの URL からも
        // いまの URL へ転送して、以前のコードで公開していたページを 404 にしない。ほかの本がいまそのコードの URL を
        // 使っているときは転送しない。
        var bookUrlPaths = new HashSet<string>(reg._bookUrls.Values.Select(u => u.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);
        foreach (var b in books.Where(b => !b.IsDeleted && reg._bookUrls.ContainsKey(b.BookId)))
        {
            string to = reg._bookUrls[b.BookId];
            foreach (var code in new[] { b.Isbn13, b.PeriodicalCode, b.AmazonAsinKindle, b.AmazonAsinPrint })
            {
                if (string.IsNullOrWhiteSpace(code)) continue;
                string from = $"/books/{UrlSlug.Encode(UrlSlug.FromName(code.Trim()))}";
                if (bookUrlPaths.Contains(from)) continue;
                reg._legacyRedirects.Add(new LegacyRedirect(from, to));
            }
        }

        ctx.Logger.Info($"entity urls: persons {reg._personUrls.Count} / characters {reg._characterUrls.Count}"
            + $"（うちゲスト {reg._guestPlacements.Count}）/ companies {reg._companyUrls.Count} / books {reg._bookUrls.Count}");
        return reg;
    }

    /// <summary>
    /// person_id → いま公開している人物 URL のスラッグ（<c>published_entity_slugs</c> で最後に公開した日時が最も新しいもの）。
    /// 本番デプロイのたびに、いまの URL の行の last_published_at をデプロイ時刻に更新するので、その値が最も新しい行が
    /// いま公開している URL に当たる。同じ日時の行が複数あるときは最初に公開した日時 → スラッグの順で決めて結果を揺らさない。
    /// </summary>
    private static async Task<Dictionary<int, string>> LoadCurrentPublishedPersonSlugsAsync(
        IConnectionFactory factory, CancellationToken ct)
    {
        const string sql = """
            SELECT person_id AS PersonId, slug AS Slug
              FROM published_entity_slugs
             WHERE entity_kind = 'PERSON' AND person_id IS NOT NULL
             ORDER BY person_id, last_published_at DESC, created_at DESC, slug
            """;
        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<(int PersonId, string Slug)>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        var result = new Dictionary<int, string>();
        foreach (var (personId, slug) in rows)
            result.TryAdd(personId, slug);
        return result;
    }

    /// <summary>
    /// いまの人物 URL・キャラ詳細 URL・企業詳細 URL のスラッグを、本番で公開した記録として台帳 <c>published_entity_slugs</c> に追記する。
    /// 本番デプロイが成功した（本番がこのビルドの出力と一致した）ときだけ呼ぶ。記録済みのスラッグの持ち主はそのまま残す
    /// （最初に公開した実体を指し続ける）。区分に応じて person_id / character_id / company_id のどれか 1 つだけを埋める。
    /// あわせて、いまの URL の行（同じ実体の行）の last_published_at をこのデプロイの時刻に更新する
    /// （一度別の URL に変わってから元の URL に戻っても、いま公開している URL を正しく引けるように）。
    /// 戻り値は新たに記録した件数（人物・キャラ・企業）。
    /// </summary>
    public async Task<(int Persons, int Characters, int Companies)> RecordPublishedSlugsAsync(IConnectionFactory factory, CancellationToken ct)
    {
        const string personSql = """
            INSERT IGNORE INTO published_entity_slugs (entity_kind, slug, person_id, last_published_at)
            VALUES ('PERSON', @Slug, @EntityId, @At)
            """;
        const string characterSql = """
            INSERT IGNORE INTO published_entity_slugs (entity_kind, slug, character_id, last_published_at)
            VALUES ('CHARACTER', @Slug, @EntityId, @At)
            """;
        const string companySql = """
            INSERT IGNORE INTO published_entity_slugs (entity_kind, slug, company_id, last_published_at)
            VALUES ('COMPANY', @Slug, @EntityId, @At)
            """;
        // 記録済みの行は、同じ実体の行だけ最後に公開した日時を更新する（別の実体が先に使っていたスラッグは触らない）。
        const string personTouchSql = """
            UPDATE published_entity_slugs SET last_published_at = @At
             WHERE entity_kind = 'PERSON' AND slug = @Slug AND person_id = @EntityId
            """;
        const string characterTouchSql = """
            UPDATE published_entity_slugs SET last_published_at = @At
             WHERE entity_kind = 'CHARACTER' AND slug = @Slug AND character_id = @EntityId
            """;
        const string companyTouchSql = """
            UPDATE published_entity_slugs SET last_published_at = @At
             WHERE entity_kind = 'COMPANY' AND slug = @Slug AND company_id = @EntityId
            """;
        var at = DateTime.Now;
        var personRows = _personSlugs.Select(kv => new { Slug = kv.Value, EntityId = kv.Key, At = at }).ToList();
        var characterRows = _characterSlugs.Select(kv => new { Slug = kv.Value, EntityId = kv.Key, At = at }).ToList();
        var companyRows = _companySlugs.Select(kv => new { Slug = kv.Value, EntityId = kv.Key, At = at }).ToList();
        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        int persons = await conn.ExecuteAsync(new CommandDefinition(personSql, personRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        int characters = await conn.ExecuteAsync(new CommandDefinition(characterSql, characterRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        int companies = await conn.ExecuteAsync(new CommandDefinition(companySql, companyRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(personTouchSql, personRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(characterTouchSql, characterRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(companyTouchSql, companyRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return (persons, characters, companies);
    }

    /// <summary>
    /// 名前が衝突するキャラクター（「長老」が 2 人など）には、全員に出身作品（声の出演で最初に登場した作品）を
    /// 「長老 (ふたりはプリキュア)」の形で添えて URL を分ける。素の名前を誰か 1 人に残すと、あとから登場した側が
    /// 番号付きの URL になり、どちらがどの作品のキャラか URL から分からなくなるため。
    /// 本番で素の名前の URL を公開していたキャラは、公開記録（published_entity_slugs）から新しい URL へ 301 で転送される。
    /// 出身作品が分からない、または出身作品まで同じ組は添えずに残す（<see cref="AssignSlugs"/> が番号を付けて警告する）。
    /// </summary>
    private static List<(int Id, string Name)> QualifyCollidingCharacterNames(
        BuildContext ctx, CreditInvolvementIndex index, List<(int Id, string Name)> entries)
    {
        var colliding = entries
            .GroupBy(e => UrlSlug.FromName(e.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(e => e.Id))
            .ToHashSet();
        if (colliding.Count == 0) return entries;

        // 衝突したキャラだけ、名義ごとの声の出演から最初に登場した作品を求める。
        var involvementsByCharacter = new Dictionary<int, List<Involvement>>();
        foreach (var (aliasId, invs) in index.VoiceCastByCharacterAlias)
        {
            if (!ctx.CharacterAliasById.TryGetValue(aliasId, out var alias) || !colliding.Contains(alias.CharacterId)) continue;
            if (!involvementsByCharacter.TryGetValue(alias.CharacterId, out var list))
                involvementsByCharacter[alias.CharacterId] = list = new List<Involvement>();
            list.AddRange(invs);
        }
        // 出身作品は登場した作品のうち放送・公開の始まりがいちばん早いもの（クレジットの収録範囲では絞らない）。
        string? OriginTitle(int characterId)
        {
            if (!involvementsByCharacter.TryGetValue(characterId, out var invs)) return null;
            return invs
                .Select(i => ctx.SeriesById.TryGetValue(i.SeriesId, out var series) ? series : null)
                .Where(series => series is not null)
                .OrderBy(series => series!.StartDate)
                .ThenBy(series => series!.SeriesId)
                .FirstOrDefault()?.Title;
        }

        var result = new List<(int Id, string Name)>(entries.Count);
        foreach (var g in entries.GroupBy(e => UrlSlug.FromName(e.Name), StringComparer.OrdinalIgnoreCase))
        {
            var members = g.ToList();
            if (members.Count == 1) { result.AddRange(members); continue; }

            var origins = members.Select(m => OriginTitle(m.Id)).ToList();
            bool distinct = origins.All(o => o is not null) && origins.Distinct(StringComparer.Ordinal).Count() == origins.Count;
            if (!distinct) { result.AddRange(members); continue; }
            for (int i = 0; i < members.Count; i++)
                result.Add((members[i].Id, $"{members[i].Name} ({origins[i]})"));
        }
        return result;
    }

    /// <summary>
    /// ID と名前の組にスラッグを割り当てる。衝突（大文字小文字を区別しない）は ID の若い順に
    /// 素の名前 → <c>_2</c> → <c>_3</c> … とし、警告を出す。
    /// </summary>
    /// <param name="allowNumeric">数字だけのスラッグを許すか（書籍の ISBN 等）。許さない区分では末尾に <c>_</c> を足す。</param>
    private static IEnumerable<(int Id, string Slug)> AssignSlugs(
        string section, IEnumerable<(int Id, string Name)> entries, BuildLogger logger,
        string? reserved = null, bool allowNumeric = false)
    {
        var bySlug = entries
            .Select(e =>
            {
                var slug = UrlSlug.FromName(e.Name);
                if (slug.Length == 0) slug = "_";
                // 数字だけのスラッグは旧 ID URL と区別できないため末尾に "_" を足す。
                if (!allowNumeric && slug.All(char.IsAsciiDigit)) slug += "_";
                return (e.Id, e.Name, Slug: slug);
            })
            .GroupBy(e => e.Slug, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(e => e.Id));

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reserved is not null) taken.Add(reserved);

        foreach (var g in bySlug)
        {
            var members = g.OrderBy(e => e.Id).ToList();
            bool collided = members.Count > 1 || taken.Contains(g.Key);
            if (collided)
            {
                logger.Warn($"{section}: URL の名前が衝突しています（{string.Join(" / ", members.Select(m => m.Name))}）。"
                    + "2 件目以降に連番を付けて出力しました。名前の付け方を決めて解消してください。");
            }
            int n = 1;
            foreach (var m in members)
            {
                string slug = m.Slug;
                while (!taken.Add(slug))
                {
                    n++;
                    slug = $"{m.Slug}_{n}";
                }
                yield return (m.Id, slug);
            }
        }
    }

    /// <summary>
    /// 単発キャラを判定する。条件はすべて満たすこと：
    /// プリキュア（character_kind='PRECURE'）でない / クレジット上の登場がちょうど 1 回
    /// （TV 系は 1 話、映画系は 1 本）/ 歌唱の記録が無い / 家族関係を持たない（どちら向きでも）。
    /// </summary>
    private static IEnumerable<(int CharacterId, GuestPlacement Placement)> DetectGuestCharacters(
        BuildContext ctx, CreditInvolvementIndex index, IReadOnlyList<Character> characters)
    {
        // character_id → そのキャラの名義で集まった登場（TV は (series, episode)、映画は (series, null)）。
        var appearances = new Dictionary<int, HashSet<(int SeriesId, int? EpisodeId)>>();
        // 登場は声の出演のクレジットだけで数える（主題歌・挿入歌の歌唱は下の「歌唱の記録」で別に見る）。
        foreach (var (aliasId, invs) in index.VoiceCastByCharacterAlias)
        {
            if (!ctx.CharacterAliasById.TryGetValue(aliasId, out var alias)) continue;
            foreach (var inv in invs)
            {
                if (!ctx.SeriesById.ContainsKey(inv.SeriesId)) continue;
                int? episodeId = ctx.IsMovieKindSeries(inv.SeriesId) ? null : inv.EpisodeId;
                if (!appearances.TryGetValue(alias.CharacterId, out var set))
                {
                    set = new HashSet<(int, int?)>();
                    appearances[alias.CharacterId] = set;
                }
                set.Add((inv.SeriesId, episodeId));
            }
        }

        // 歌唱の記録があるキャラ（ユニットのキャラメンバーも含めて展開）。
        var singers = new HashSet<int>();
        foreach (var (_, recSingers) in ctx.SingersByRecording)
        {
            foreach (var s in recSingers)
            {
                foreach (var p in ctx.ExpandSingerParticipants(s))
                {
                    if (p.CharacterAliasId is int caId && ctx.CharacterAliasById.TryGetValue(caId, out var ca))
                        singers.Add(ca.CharacterId);
                }
            }
        }

        // 家族関係を持つキャラ（関係の起点・相手のどちらでも）。
        var withFamily = new HashSet<int>();
        foreach (var (characterId, rels) in ctx.FamilyRelationsByCharacter)
        {
            if (rels.Count == 0) continue;
            withFamily.Add(characterId);
            foreach (var r in rels) withFamily.Add(r.RelatedCharacterId);
        }

        foreach (var c in characters)
        {
            if (string.Equals(c.CharacterKind, "PRECURE", StringComparison.Ordinal)) continue;
            if (singers.Contains(c.CharacterId) || withFamily.Contains(c.CharacterId)) continue;
            if (!appearances.TryGetValue(c.CharacterId, out var set) || set.Count != 1) continue;

            var (seriesId, episodeId) = set.First();
            int? epNo = episodeId is int eid ? ctx.LookupEpisode(seriesId, eid)?.SeriesEpNo : null;
            yield return (c.CharacterId, new GuestPlacement(seriesId, episodeId, epNo));
        }
    }
}

/// <summary>旧 ID 台帳 <c>legacy_entity_ids</c> の 1 行（区分・凍結した旧 ID・いまの実体 ID）。</summary>
internal sealed class LegacyEntityRow
{
    public string EntityKind { get; set; } = "";
    public int LegacyId { get; set; }
    public int? EntityId { get; set; }
}

/// <summary>URL の公開記録 <c>published_entity_slugs</c> の 1 行（区分・スラッグ・その URL で公開した人物・キャラ・企業）。</summary>
internal sealed class PublishedSlugRow
{
    public string EntityKind { get; set; } = "";
    public string Slug { get; set; } = "";
    public int? EntityId { get; set; }
}

/// <summary>単発キャラの唯一の登場位置（映画系は EpisodeId / SeriesEpNo が null）。</summary>
public sealed record GuestPlacement(int SeriesId, int? EpisodeId, int? SeriesEpNo);

/// <summary>旧 URL（末尾スラッシュ無しのパス。例 <c>/persons/123</c>、旧名の人物・キャラ・企業は <c>/people/{デコード済みスラッグ}</c>・<c>/characters/{デコード済みスラッグ}</c>・<c>/companies/{デコード済みスラッグ}</c>）→ 新 URL（パーセントエンコード済み、アンカー付きもあり）。</summary>
public sealed record LegacyRedirect(string FromPath, string ToUrl);
