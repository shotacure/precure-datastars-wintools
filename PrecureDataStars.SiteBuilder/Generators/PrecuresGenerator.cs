using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// プリキュア索引（<c>/precures/</c>）のみを生成する。プリキュア詳細ページは廃止し、
/// 詳細閲覧はキャラクター詳細（<c>/characters/{character_id}/</c>）に統合済み
/// （PRECURE 種別キャラの詳細ページ内「プリキュア情報」セクションで 4 区分名義 /
/// 学校 / 学年・組 / 家業 / 専属声優の全情報を担う）。
/// 索引行のリンク先 character_id は precures.transform_alias_id 経由で逆引きする。
/// </summary>
public sealed class PrecuresGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;

    private readonly PrecuresRepository _precuresRepo;
    private readonly SeriesPrecuresRepository _seriesPrecuresRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly PersonsRepository _personsRepo;

    public PrecuresGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _precuresRepo = new PrecuresRepository(factory);
        _seriesPrecuresRepo = new SeriesPrecuresRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating precures");

        // 索引用にプリキュア + 解決辞書を 1 度だけロード。
        var allPrecures = (await _precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allSeriesPrecures = await _seriesPrecuresRepo.GetAllAsync(ct).ConfigureAwait(false);

        var characterAliasById = allCharacterAliases.ToDictionary(a => a.AliasId);
        var personsById = allPersons.ToDictionary(p => p.PersonId);

        // 索引ページのみ。詳細はキャラクター詳細に統合済み。
        GenerateIndex(allPrecures, characterAliasById, personsById, allCharacterAliases, allSeriesPrecures);

        _ctx.Logger.Success($"precures: 1 ページ");
    }

    /// <summary>
    /// <c>/precures/</c>（プリキュア索引）。2 タブ構成。
    /// ・初登場順：最初に登場したシリーズ（紐付くシリーズのうち放送開始の最も早いもの）ごとのセクション。
    ///   セクション内は precure_id 昇順 = 概ね登場順。紐付くシリーズが無いプリキュアは末尾の「その他」。
    /// ・登場回数順：全員を 1 リストにし、キャラクターの登場話数と登場本数の合計が多い順（同数は登場順）。
    /// 各行のリンク先は <c>/characters/{character_id}/</c>（プリキュア詳細は廃止済み）。
    /// </summary>
    private void GenerateIndex(
        IReadOnlyList<Precure> precures,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, Person> personsById,
        IReadOnlyList<CharacterAlias> allCharacterAliases,
        IReadOnlyList<SeriesPrecure> seriesPrecures)
    {
        var aliasIdsByCharacter = allCharacterAliases
            .GroupBy(a => a.CharacterId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AliasId).ToList());

        // プリキュアごとの初登場シリーズ（紐付くシリーズのうち放送開始の最も早いもの）。
        var debutSeriesByPrecure = seriesPrecures
            .Where(sp => _ctx.SeriesById.ContainsKey(sp.SeriesId))
            .GroupBy(sp => sp.PrecureId)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(sp => _ctx.SeriesStartDate(sp.SeriesId))
                .ThenBy(sp => sp.SeriesId)
                .First().SeriesId);

        var rows = precures
            .OrderBy(p => p.PrecureId)
            .Select((p, order) =>
            {
                // 詳細リンク先となる character_id は変身後名義から解決する。
                int characterId = aliasById.TryGetValue(p.TransformAliasId, out var trAlias) ? trAlias.CharacterId : 0;
                var (ep, mv) = aliasIdsByCharacter.TryGetValue(characterId, out var aliasIds)
                    ? CharacterAppearanceCounter.Count(_ctx, _index, aliasIds)
                    : (0, 0);
                return new PrecureIndexRow
                {
                    PrecureId = p.PrecureId,
                    CharacterId = characterId,
                    TransformName = aliasById.TryGetValue(p.TransformAliasId, out var trans) ? trans.Name : "",
                    PreTransformName = aliasById.TryGetValue(p.PreTransformAliasId, out var pre) ? pre.Name : "",
                    VoiceActorName = (p.VoiceActorPersonId is int vid && personsById.TryGetValue(vid, out var v))
                        ? v.FullName : "",
                    VoiceActorPersonId = p.VoiceActorPersonId,
                    EpisodeCount = ep,
                    MovieCount = mv,
                    DebutSeriesId = debutSeriesByPrecure.TryGetValue(p.PrecureId, out var sid) ? sid : null,
                    Order = order
                };
            })
            .ToList();

        // 初登場順タブ：初登場シリーズごとのセクション（シリーズの放送開始順、見出しは「シリーズ名（年）」）。
        var debutSections = rows
            .GroupBy(r => r.DebutSeriesId)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key is int sid ? _ctx.SeriesStartDate(sid) : DateOnly.MaxValue)
            .Select(g =>
            {
                var series = g.Key is int sid ? _ctx.SeriesById[sid] : null;
                return new PrecureDebutSection
                {
                    SeriesHeadingLabel = series is null ? "その他" : $"{series.Title}（{series.StartDate.Year}）",
                    SeriesUrl = series is null ? "" : PathUtil.SeriesUrl(series.Slug),
                    Members = g.OrderBy(r => r.Order).ToList()
                };
            })
            .ToList();

        // 登場回数順タブ：登場話数と登場本数の合計が多い順、同数は登場順。
        var countRows = rows
            .OrderByDescending(r => r.EpisodeCount + r.MovieCount)
            .ThenBy(r => r.Order)
            .ToList();

        var content = new PrecuresIndexModel
        {
            Precures = rows,
            DebutSections = debutSections,
            CountRows = countRows,
            TotalCount = rows.Count,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュアオールスターズ",
            // 本文リード行と同じ母数をカードにも置く。
            OgCard = new OgCardSpec(Kicker: "", Title: "歴代プリキュアオールスターズ")
            {
                MetaLeft = OgCoverageLabel.Compact(_ctx.CreditCoverageLabel),
                Badges = new[] { new OgCardBadge("変身ヒロイン", $"{rows.Count}名") }
            },
            MetaDescription = $"歴代の変身ヒロイン（プリキュア）{rows.Count} 名を登場順に。変身前後の名前や担当声優をまとめています。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュアオールスターズ", Url = "" }
            }
        };
        _page.RenderAndWrite("/precures/", "precures", "precures-index.sbn", content, layout);
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class PrecuresIndexModel
    {
        public IReadOnlyList<PrecureIndexRow> Precures { get; set; } = Array.Empty<PrecureIndexRow>();
        /// <summary>初登場順タブのセクション（初登場シリーズごと、放送開始順）。</summary>
        public IReadOnlyList<PrecureDebutSection> DebutSections { get; set; } = Array.Empty<PrecureDebutSection>();
        /// <summary>登場回数順タブの行（登場話数と登場本数の合計が多い順）。</summary>
        public IReadOnlyList<PrecureIndexRow> CountRows { get; set; } = Array.Empty<PrecureIndexRow>();
        public int TotalCount { get; set; }
        /// <summary>クレジット横断カバレッジラベル。 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記を テンプレ側の lead 段落末尾に表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class PrecureIndexRow
    {
        public int PrecureId { get; set; }
        /// <summary>リンク先となる character_id（precures.transform_alias_id 経由で解決済み）。 /characters/{CharacterId}/ がプリキュア詳細を兼ねる。</summary>
        public int CharacterId { get; set; }
        public string TransformName { get; set; } = "";
        public string PreTransformName { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
        /// <summary>TV 系シリーズでの登場話数（キャラクター一覧と同じ数え方）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>映画系シリーズでの登場本数（1 シリーズ = 1 本）。</summary>
        public int MovieCount { get; set; }
        /// <summary>初登場シリーズの ID（紐付くシリーズが無ければ null）。</summary>
        public int? DebutSeriesId { get; set; }
        /// <summary>登場順（precure_id 昇順での並び位置）。同数の並べ替えの安定化に使う。</summary>
        public int Order { get; set; }
    }

    /// <summary>初登場順タブのセクション 1 つ分（初登場シリーズ単位）。</summary>
    private sealed class PrecureDebutSection
    {
        /// <summary>見出し（「シリーズ名（年）」。紐付くシリーズが無いプリキュアは「その他」）。</summary>
        public string SeriesHeadingLabel { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public IReadOnlyList<PrecureIndexRow> Members { get; set; } = Array.Empty<PrecureIndexRow>();
    }
}
