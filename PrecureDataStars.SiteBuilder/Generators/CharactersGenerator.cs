using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// キャラクター索引（<c>/characters/</c>）と詳細（<c>/characters/{character_id}/</c>）の生成（v1.3.0 タスク追加）。
/// <para>
/// プリキュア本体は <see cref="PrecuresGenerator"/> 側で別ページとして扱うが、
/// 本ジェネレータは characters テーブル全件（プリキュア・妖精・敵キャラ・一般人など）を扱う。
/// プリキュアであっても character_kind=PRECURE のキャラとして本索引・本詳細にも登場する
/// （プリキュアページからは「変身」を主軸にした見せ方を、キャラページからは「全名義の表記揺れ + 家族」
///  を主軸にした見せ方をすることで、観点を分ける）。
/// </para>
/// </summary>
public sealed class CharactersGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;

    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly CharacterFamilyRelationsRepository _familyRepo;
    private readonly CharacterRelationKindsRepository _relationKindsRepo;
    private readonly CharacterKindsRepository _characterKindsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

    public CharactersGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _charactersRepo = new CharactersRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _familyRepo = new CharacterFamilyRelationsRepository(factory);
        _relationKindsRepo = new CharacterRelationKindsRepository(factory);
        _characterKindsRepo = new CharacterKindsRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating characters");

        var allCharacters = (await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allKinds = (await _characterKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allRelationKinds = (await _relationKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        var charactersById = allCharacters.ToDictionary(c => c.CharacterId);
        var aliasesByCharacter = allAliases.GroupBy(a => a.CharacterId).ToDictionary(g => g.Key, g => g.ToList());
        var kindMap = allKinds.ToDictionary(k => k.CharacterKindCode, k => k, StringComparer.Ordinal);
        var relationKindMap = allRelationKinds.ToDictionary(r => r.RelationCode, r => r, StringComparer.Ordinal);
        var personAliasById = allPersonAliases.ToDictionary(a => a.AliasId);

        // 索引ページ。
        GenerateIndex(allCharacters, aliasesByCharacter, kindMap);

        // 詳細ページ。
        foreach (var c in allCharacters)
        {
            await GenerateDetailAsync(c, aliasesByCharacter, charactersById, kindMap, relationKindMap, personAliasById, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"characters: {allCharacters.Count + 1} ページ");
    }

    /// <summary>
    /// <c>/characters/</c>（キャラクター索引）。character_kind でセクション分けし、各セクション内は
    /// 50 音順（name_kana 昇順、空読みは末尾）で並べる。
    /// </summary>
    private void GenerateIndex(
        IReadOnlyList<Character> characters,
        IReadOnlyDictionary<int, List<CharacterAlias>> aliasesByCharacter,
        IReadOnlyDictionary<string, CharacterKind> kindMap)
    {
        // 各キャラに紐付く全 alias_id の Involvement 件数を合算してエピソード数を表示。
        // (SeriesId, EpisodeId) でユニーク化。
        int CountInvolvements(int characterId)
        {
            if (!aliasesByCharacter.TryGetValue(characterId, out var aliases)) return 0;
            var seen = new HashSet<(int, int)>();
            foreach (var a in aliases)
            {
                if (!_index.ByCharacterAlias.TryGetValue(a.AliasId, out var invs)) continue;
                foreach (var inv in invs) seen.Add((inv.SeriesId, inv.EpisodeId ?? 0));
            }
            return seen.Count;
        }

        // セクション = character_kind 単位。kind マスタの display_order 順で並べる。
        var sections = characters
            .GroupBy(c => c.CharacterKind)
            .Select(g => new
            {
                KindCode = g.Key,
                KindLabel = kindMap.TryGetValue(g.Key, out var k) ? k.NameJa : g.Key,
                Order = kindMap.TryGetValue(g.Key, out var k2) ? (k2.DisplayOrder ?? byte.MaxValue) : byte.MaxValue,
                Members = g.OrderBy(c => string.IsNullOrEmpty(c.NameKana) ? 1 : 0)
                           .ThenBy(c => c.NameKana, StringComparer.Ordinal)
                           .ThenBy(c => c.Name, StringComparer.Ordinal)
                           .Select(c => new CharacterIndexRow
                           {
                               CharacterId = c.CharacterId,
                               Name = c.Name,
                               NameKana = c.NameKana ?? "",
                               EpisodeCount = CountInvolvements(c.CharacterId),
                               HasInvolvement = CountInvolvements(c.CharacterId) > 0
                           })
                           .ToList()
            })
            .OrderBy(s => s.Order)
            .ThenBy(s => s.KindCode, StringComparer.Ordinal)
            .Select(s => new CharacterKindSection
            {
                KindLabel = s.KindLabel,
                Members = s.Members
            })
            .ToList();

        var content = new CharactersIndexModel
        {
            Sections = sections,
            TotalCount = characters.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "キャラクター一覧",
            MetaDescription = "プリキュアシリーズに登場する全キャラクターの索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "キャラクター", Url = "" }
            }
        };
        _page.RenderAndWrite("/characters/", "characters", "characters-index.sbn", content, layout);
    }

    /// <summary><c>/characters/{character_id}/</c>（キャラクター詳細）。</summary>
    private async Task GenerateDetailAsync(
        Character character,
        IReadOnlyDictionary<int, List<CharacterAlias>> aliasesByCharacter,
        IReadOnlyDictionary<int, Character> charactersById,
        IReadOnlyDictionary<string, CharacterKind> kindMap,
        IReadOnlyDictionary<string, CharacterRelationKind> relationKindMap,
        IReadOnlyDictionary<int, PersonAlias> personAliasById,
        CancellationToken ct)
    {
        var aliases = aliasesByCharacter.TryGetValue(character.CharacterId, out var lst)
            ? lst
            : new List<CharacterAlias>();

        // 名義一覧。alias_id 昇順（時系列情報は持たない仕様）。
        var aliasRows = aliases
            .OrderBy(a => a.AliasId)
            .Select(a => new CharacterAliasRow
            {
                Name = a.Name,
                NameKana = a.NameKana ?? "",
                NameEn = a.NameEn ?? "",
                Notes = a.Notes ?? ""
            })
            .ToList();

        // 家族関係。
        var fams = await _familyRepo.GetByCharacterAsync(character.CharacterId, ct).ConfigureAwait(false);
        var familyRows = fams
            .OrderBy(f => f.DisplayOrder ?? byte.MaxValue)
            .Where(f => charactersById.ContainsKey(f.RelatedCharacterId))
            .Select(f => new FamilyRelationRow
            {
                RelationLabel = relationKindMap.TryGetValue(f.RelationCode, out var rk) ? rk.NameJa : f.RelationCode,
                RelatedCharacterId = f.RelatedCharacterId,
                RelatedCharacterName = charactersById[f.RelatedCharacterId].Name,
                Notes = f.Notes ?? ""
            })
            .ToList();

        // 声の出演履歴：当該キャラの全 alias_id の Involvement を合算。
        var aliasIds = aliases.Select(a => a.AliasId).ToList();
        var voiceRows = BuildVoiceCastRows(aliasIds, aliases.ToDictionary(a => a.AliasId), personAliasById);

        string kindLabel = kindMap.TryGetValue(character.CharacterKind, out var kk) ? kk.NameJa : character.CharacterKind;

        var content = new CharacterDetailModel
        {
            Character = new CharacterView
            {
                CharacterId = character.CharacterId,
                Name = character.Name,
                NameKana = character.NameKana ?? "",
                NameEn = character.NameEn ?? "",
                KindLabel = kindLabel,
                Notes = character.Notes ?? ""
            },
            Aliases = aliasRows,
            FamilyRelations = familyRows,
            VoiceCastRows = voiceRows
        };
        var layout = new LayoutModel
        {
            PageTitle = character.Name,
            MetaDescription = $"{character.Name}（{kindLabel}）のプロフィールと出演履歴。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "キャラクター", Url = "/characters/" },
                new BreadcrumbItem { Label = character.Name, Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CharacterUrl(character.CharacterId), "characters", "characters-detail.sbn", content, layout);
    }

    /// <summary>
    /// 全名義の character_alias_id に紐付く CHARACTER_VOICE Involvement を集約。
    /// プリキュア詳細と同じロジックだが、aliases は本キャラに限る。
    /// </summary>
    private List<VoiceCastRow> BuildVoiceCastRows(
        IReadOnlyList<int> aliasIds,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, PersonAlias> personAliasById)
    {
        var all = aliasIds
            .Where(_index.ByCharacterAlias.ContainsKey)
            .SelectMany(id => _index.ByCharacterAlias[id])
            .ToList();
        if (all.Count == 0) return new List<VoiceCastRow>();

        var rows = new List<VoiceCastRow>();
        foreach (var bySeries in all.GroupBy(i => i.SeriesId).OrderBy(g => SeriesStartDate(g.Key)))
        {
            if (!_ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;
            foreach (var byEp in bySeries
                .GroupBy(i => i.EpisodeId ?? 0)
                .OrderBy(eg => EpisodeSeriesEpNo(bySeries.Key, eg.Key)))
            {
                string episodeLabel;
                string episodeUrl;
                if (byEp.Key == 0)
                {
                    episodeLabel = "（シリーズ全体）";
                    episodeUrl = PathUtil.SeriesUrl(series.Slug);
                }
                else
                {
                    var ep = LookupEpisode(bySeries.Key, byEp.Key);
                    if (ep is null) continue;
                    episodeLabel = $"第{ep.SeriesEpNo}話　{ep.TitleText}";
                    episodeUrl = PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo);
                }

                var actorNames = new List<string>();
                var seenActor = new HashSet<string>(StringComparer.Ordinal);
                foreach (var inv in byEp)
                {
                    if (inv.PersonAliasId is int paid && personAliasById.TryGetValue(paid, out var pa))
                    {
                        string nm = pa.DisplayTextOverride ?? pa.Name;
                        if (!string.IsNullOrEmpty(nm) && seenActor.Add(nm))
                            actorNames.Add(nm);
                    }
                }

                var aliasNames = new List<string>();
                var seenAlias = new HashSet<string>(StringComparer.Ordinal);
                foreach (var inv in byEp)
                {
                    if (inv.CharacterAliasId is int caid && aliasById.TryGetValue(caid, out var ca))
                    {
                        if (seenAlias.Add(ca.Name)) aliasNames.Add(ca.Name);
                    }
                }

                rows.Add(new VoiceCastRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    EpisodeLabel = episodeLabel,
                    EpisodeUrl = episodeUrl,
                    AliasNames = string.Join("、", aliasNames),
                    VoiceActorNames = string.Join("、", actorNames)
                });
            }
        }
        return rows;
    }

    private DateOnly SeriesStartDate(int seriesId)
        => _ctx.SeriesById.TryGetValue(seriesId, out var s) ? s.StartDate : DateOnly.MaxValue;

    private int EpisodeSeriesEpNo(int seriesId, int episodeId)
    {
        if (episodeId == 0) return -1;
        var ep = LookupEpisode(seriesId, episodeId);
        return ep?.SeriesEpNo ?? int.MaxValue;
    }

    private Episode? LookupEpisode(int seriesId, int episodeId)
    {
        if (!_ctx.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        for (int i = 0; i < eps.Count; i++)
            if (eps[i].EpisodeId == episodeId) return eps[i];
        return null;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class CharactersIndexModel
    {
        public IReadOnlyList<CharacterKindSection> Sections { get; set; } = Array.Empty<CharacterKindSection>();
        public int TotalCount { get; set; }
    }

    private sealed class CharacterKindSection
    {
        public string KindLabel { get; set; } = "";
        public IReadOnlyList<CharacterIndexRow> Members { get; set; } = Array.Empty<CharacterIndexRow>();
    }

    private sealed class CharacterIndexRow
    {
        public int CharacterId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public bool HasInvolvement { get; set; }
    }

    private sealed class CharacterDetailModel
    {
        public CharacterView Character { get; set; } = new();
        public IReadOnlyList<CharacterAliasRow> Aliases { get; set; } = Array.Empty<CharacterAliasRow>();
        public IReadOnlyList<FamilyRelationRow> FamilyRelations { get; set; } = Array.Empty<FamilyRelationRow>();
        public IReadOnlyList<VoiceCastRow> VoiceCastRows { get; set; } = Array.Empty<VoiceCastRow>();
    }

    private sealed class CharacterView
    {
        public int CharacterId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class CharacterAliasRow
    {
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class FamilyRelationRow
    {
        public string RelationLabel { get; set; } = "";
        public int RelatedCharacterId { get; set; }
        public string RelatedCharacterName { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class VoiceCastRow
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string EpisodeLabel { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public string AliasNames { get; set; } = "";
        public string VoiceActorNames { get; set; } = "";
    }
}
