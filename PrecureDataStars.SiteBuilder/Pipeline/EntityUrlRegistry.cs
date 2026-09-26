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
///     名前はマスタの正式名（persons.full_name / companies.name / characters.name）を
///     <see cref="UrlSlug.FromName"/> で整えたもの。</description></item>
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

    private readonly Dictionary<int, string> _personUrls = new();
    private readonly Dictionary<int, string> _companyUrls = new();
    private readonly Dictionary<int, string> _characterUrls = new();
    private readonly Dictionary<int, string> _bookUrls = new();
    private readonly Dictionary<int, GuestPlacement> _guestPlacements = new();
    private readonly List<LegacyRedirect> _legacyRedirects = new();

    private EntityUrlRegistry() { }

    /// <summary>未構築時（Catalog 側プレビュー等）用の空台帳。URL は旧来の ID 形式にフォールバックする。</summary>
    public static EntityUrlRegistry Empty { get; } = new();

    /// <summary>人物詳細ページの URL（パーセントエンコード済み）。台帳に無い ID は null。</summary>
    public string? PersonUrl(int personId) => _personUrls.TryGetValue(personId, out var u) ? u : null;

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

        foreach (var (id, slug) in AssignSlugs("persons", persons.Select(p => (p.PersonId, p.FullName)), ctx.Logger))
            reg._personUrls[id] = $"/people/{UrlSlug.Encode(slug)}/";

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
            reg._characterUrls[id] = $"/characters/{UrlSlug.Encode(slug)}/";

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
        }

        ctx.Logger.Info($"entity urls: persons {reg._personUrls.Count} / characters {reg._characterUrls.Count}"
            + $"（うちゲスト {reg._guestPlacements.Count}）/ companies {reg._companyUrls.Count} / books {reg._bookUrls.Count}");
        return reg;
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
        foreach (var (aliasId, invs) in index.ByCharacterAlias)
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

/// <summary>単発キャラの唯一の登場位置（映画系は EpisodeId / SeriesEpNo が null）。</summary>
public sealed record GuestPlacement(int SeriesId, int? EpisodeId, int? SeriesEpNo);

/// <summary>旧 ID URL（末尾スラッシュ無しのパス。例 <c>/persons/123</c>）→ 新 URL（パーセントエンコード済み、アンカー付きもあり）。</summary>
public sealed record LegacyRedirect(string FromPath, string ToUrl);
