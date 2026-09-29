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
///     人物は最新名義（<see cref="LatestAliasResolver.LatestPersonAliasIds"/>、TV 系のクレジットで最後に使われた名義。
///     クレジットの無い人物は正式名 persons.full_name）。人物詳細の見出しもこの名前にそろえる（<see cref="PersonDisplayName"/>）。</description></item>
///   <item><description>人物の URL は最新名義が変わると変わり、キャラの URL はキャラ名を変えると変わる。本番デプロイで公開した
///     人物・キャラの URL は台帳 <c>published_entity_slugs</c> に記録しておき（<see cref="RecordPublishedSlugsAsync"/>）、
///     いまの URL と違う記録済みの旧 URL は新 URL へ 301 で転送する（<see cref="LegacyRedirects"/> に <c>/people/{旧名}</c>・
///     <c>/characters/{旧名}</c> として載せる。同じ区分の別の実体がいまその名前の URL を使っていれば転送しない）。
///     個別ページを持っていたキャラが単発キャラ扱いに変わったときは、ゲストキャラクターページの登場話へ転送する。</description></item>
///   <item><description>同じ区分の中で名前（大文字小文字を区別しない）が衝突したら、ID の若い 1 件が素の名前を持ち、
///     残りは <c>_2</c>, <c>_3</c> … を付けて警告を出す（付け方はその都度判断して名前側で解消する前提の仮措置）。
///     数字だけの名前は旧 URL（<c>/persons/123/</c>）と区別できないため末尾に <c>_</c> を足す。</description></item>
///   <item><description>書籍 <c>/books/{コード}/</c>。コードは ISBN-13 → 定期刊行物コード → Kindle ASIN → 紙の ASIN の
///     順に最初にあるもの。どれも無い書籍は書名から作る（警告を出す）。</description></item>
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
    /// <summary>person_id → 見出し名・読み（最新名義。クレジットの無い人物は正式名）。</summary>
    private readonly Dictionary<int, (string Name, string Kana)> _personNames = new();
    /// <summary>person_id → 最新名義の person_alias_id（クレジットの無い人物は載らない）。</summary>
    private readonly Dictionary<int, int> _latestPersonAliasIds = new();
    private readonly Dictionary<int, string> _companyUrls = new();
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

    /// <summary>人物の見出し名（最新名義。クレジットの無い人物は正式名）。台帳に無い ID は null。</summary>
    public string? PersonDisplayName(int personId) => _personNames.TryGetValue(personId, out var n) ? n.Name : null;

    /// <summary>人物の見出し名の読み（最新名義の読み。読み未登録なら空文字）。台帳に無い ID は null。</summary>
    public string? PersonDisplayKana(int personId) => _personNames.TryGetValue(personId, out var n) ? n.Kana : null;

    /// <summary>人物の最新名義（TV 系のクレジットで最後に使われた、共同名義でない person_alias_id）。クレジットの無い人物は null。</summary>
    public int? LatestPersonAliasId(int personId) => _latestPersonAliasIds.TryGetValue(personId, out var a) ? a : null;

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

        // 人物は最新名義で名乗る。最新名義が無い（クレジットの無い）人物は正式名。
        foreach (var (pid, aid) in LatestAliasResolver.LatestPersonAliasIds(ctx, index))
            reg._latestPersonAliasIds[pid] = aid;
        foreach (var p in persons)
        {
            if (reg._latestPersonAliasIds.TryGetValue(p.PersonId, out var aid)
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
            reg._companyUrls[id] = $"/companies/{UrlSlug.Encode(slug)}/";

        // 単発キャラを先に決め、残りのキャラだけで名前の衝突を判定する（単発キャラは名前 URL を持たない）。
        foreach (var (characterId, placement) in DetectGuestCharacters(ctx, index, characters))
        {
            reg._guestPlacements[characterId] = placement;
            var series = ctx.SeriesById[placement.SeriesId];
            var anchor = GuestEpisodeAnchor(placement.SeriesEpNo);
            reg._characterUrls[characterId] = GuestCharactersUrl(series.Slug) + (anchor.Length > 0 ? "#" + anchor : "");
        }
        var namedCharacters = characters
            .Where(c => !reg._guestPlacements.ContainsKey(c.CharacterId))
            .Select(c => (c.CharacterId, c.Name));
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

            // 旧名の人物・キャラ URL の転送表。本番で公開した記録（published_entity_slugs）のうち、いまの URL と違うものを
            // いまの URL へ転送する。同じ区分でいま別の実体がその名前の URL を使っている（ページが実在する）ときは転送しない。
            // 転送元のキーはデコード済みのスラッグで持つ（Lambda@Edge 側でリクエスト URI をデコードして引く）。
            const string publishedSql = """
                SELECT entity_kind AS EntityKind, slug AS Slug, COALESCE(person_id, character_id) AS EntityId
                  FROM published_entity_slugs
                 WHERE entity_kind IN ('PERSON', 'CHARACTER')
                 ORDER BY entity_kind, slug
                """;
            var published = await conn.QueryAsync<PublishedSlugRow>(
                new CommandDefinition(publishedSql, cancellationToken: ct)).ConfigureAwait(false);
            var livePersonSlugs = new HashSet<string>(reg._personSlugs.Values, StringComparer.OrdinalIgnoreCase);
            var liveCharacterSlugs = new HashSet<string>(reg._characterSlugs.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var row in published)
            {
                var (section, liveSlugs, urls) = row.EntityKind switch
                {
                    "PERSON" => ("people", livePersonSlugs, reg._personUrls),
                    "CHARACTER" => ("characters", liveCharacterSlugs, reg._characterUrls),
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

        ctx.Logger.Info($"entity urls: persons {reg._personUrls.Count} / characters {reg._characterUrls.Count}"
            + $"（うちゲスト {reg._guestPlacements.Count}）/ companies {reg._companyUrls.Count} / books {reg._bookUrls.Count}");
        return reg;
    }

    /// <summary>
    /// いまの人物 URL・キャラ詳細 URL のスラッグを、本番で公開した記録として台帳 <c>published_entity_slugs</c> に追記する。
    /// 本番デプロイが成功した（本番がこのビルドの出力と一致した）ときだけ呼ぶ。記録済みのスラッグはそのまま残す
    /// （最初に公開した実体を指し続ける）。区分に応じて person_id / character_id の一方だけを埋める。
    /// 戻り値は新たに記録した件数（人物・キャラ）。
    /// </summary>
    public async Task<(int Persons, int Characters)> RecordPublishedSlugsAsync(IConnectionFactory factory, CancellationToken ct)
    {
        const string personSql = """
            INSERT IGNORE INTO published_entity_slugs (entity_kind, slug, person_id)
            VALUES ('PERSON', @Slug, @EntityId)
            """;
        const string characterSql = """
            INSERT IGNORE INTO published_entity_slugs (entity_kind, slug, character_id)
            VALUES ('CHARACTER', @Slug, @EntityId)
            """;
        var personRows = _personSlugs.Select(kv => new { Slug = kv.Value, EntityId = kv.Key }).ToList();
        var characterRows = _characterSlugs.Select(kv => new { Slug = kv.Value, EntityId = kv.Key }).ToList();
        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        int persons = await conn.ExecuteAsync(new CommandDefinition(personSql, personRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        int characters = await conn.ExecuteAsync(new CommandDefinition(characterSql, characterRows, tx, cancellationToken: ct)).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return (persons, characters);
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

/// <summary>URL の公開記録 <c>published_entity_slugs</c> の 1 行（区分・スラッグ・その URL で公開した人物またはキャラ）。</summary>
internal sealed class PublishedSlugRow
{
    public string EntityKind { get; set; } = "";
    public string Slug { get; set; } = "";
    public int? EntityId { get; set; }
}

/// <summary>単発キャラの唯一の登場位置（映画系は EpisodeId / SeriesEpNo が null）。</summary>
public sealed record GuestPlacement(int SeriesId, int? EpisodeId, int? SeriesEpNo);

/// <summary>旧 URL（末尾スラッシュ無しのパス。例 <c>/persons/123</c>、旧名の人物・キャラは <c>/people/{デコード済みスラッグ}</c>・<c>/characters/{デコード済みスラッグ}</c>）→ 新 URL（パーセントエンコード済み、アンカー付きもあり）。</summary>
public sealed record LegacyRedirect(string FromPath, string ToUrl);
