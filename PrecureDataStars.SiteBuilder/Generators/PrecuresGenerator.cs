using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// プリキュア索引（<c>/precures/</c>）と詳細（<c>/precures/{precure_id}/</c>）の生成（v1.3.0 タスク追加）。
/// <para>
/// 1 プリキュア = <c>precures</c> テーブル 1 行。4 つの名義 FK（変身前 / 変身後 / 変身後 2 / 別形態）が
/// 同一 character_id を指す業務ルール（DB トリガで強制）に依存して、4 名義の <c>character_alias_id</c> を
/// 集めて <see cref="CreditInvolvementIndex.ByCharacterAlias"/> から声の出演履歴を逆引きする。
/// </para>
/// <para>
/// 肌色情報（HSL / RGB の 6 列）は本ページでは一切描画しない（センシティブな研究情報のため、
/// Web には載せず内部データとしてのみ保持する運用ルール）。
/// </para>
/// </summary>
public sealed class PrecuresGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;

    private readonly PrecuresRepository _precuresRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly CharacterFamilyRelationsRepository _familyRepo;
    private readonly CharacterRelationKindsRepository _relationKindsRepo;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

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
        _charactersRepo = new CharactersRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _familyRepo = new CharacterFamilyRelationsRepository(factory);
        _relationKindsRepo = new CharacterRelationKindsRepository(factory);
        _personsRepo = new PersonsRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating precures");

        // 共通マスタを 1 回だけロード。
        var allPrecures = (await _precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacters = (await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allRelationKinds = (await _relationKindsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();

        var charactersById = allCharacters.ToDictionary(c => c.CharacterId);
        var characterAliasById = allCharacterAliases.ToDictionary(a => a.AliasId);
        var personsById = allPersons.ToDictionary(p => p.PersonId);
        var personAliasById = allPersonAliases.ToDictionary(a => a.AliasId);
        var relationKindMap = allRelationKinds.ToDictionary(r => r.RelationCode, r => r, StringComparer.Ordinal);

        // 索引ページ。
        GenerateIndex(allPrecures, characterAliasById, personsById);

        // 詳細ページ。
        foreach (var p in allPrecures)
        {
            await GenerateDetailAsync(p, charactersById, characterAliasById, personsById, personAliasById, relationKindMap, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"precures: {allPrecures.Count + 1} ページ");
    }

    /// <summary><c>/precures/</c>（プリキュア索引）。precure_id 昇順 = 概ね登場年代順。</summary>
    private void GenerateIndex(
        IReadOnlyList<Precure> precures,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, Person> personsById)
    {
        var rows = precures
            .OrderBy(p => p.PrecureId)
            .Select(p => new PrecureIndexRow
            {
                PrecureId = p.PrecureId,
                TransformName = aliasById.TryGetValue(p.TransformAliasId, out var trans) ? trans.Name : "",
                PreTransformName = aliasById.TryGetValue(p.PreTransformAliasId, out var pre) ? pre.Name : "",
                VoiceActorName = (p.VoiceActorPersonId is int vid && personsById.TryGetValue(vid, out var v))
                    ? v.FullName : "",
                VoiceActorPersonId = p.VoiceActorPersonId
            })
            .ToList();

        var content = new PrecuresIndexModel
        {
            Precures = rows,
            TotalCount = rows.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "プリキュア一覧",
            MetaDescription = "プリキュアシリーズの全プリキュア（変身ヒロイン）の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プリキュア", Url = "" }
            }
        };
        _page.RenderAndWrite("/precures/", "precures", "precures-index.sbn", content, layout);
    }

    /// <summary><c>/precures/{precure_id}/</c>（プリキュア詳細）。</summary>
    private async Task GenerateDetailAsync(
        Precure precure,
        IReadOnlyDictionary<int, Character> charactersById,
        IReadOnlyDictionary<int, CharacterAlias> aliasById,
        IReadOnlyDictionary<int, Person> personsById,
        IReadOnlyDictionary<int, PersonAlias> personAliasById,
        IReadOnlyDictionary<string, CharacterRelationKind> relationKindMap,
        CancellationToken ct)
    {
        // 4 名義（変身前 / 変身後 / 変身後 2 / 別形態）の表示用エントリを構築。
        // すべて同一 character_id を指す業務ルール（precures テーブルのトリガで強制）。
        var aliasEntries = new List<PrecureAliasEntry>();
        AppendAliasEntry(aliasEntries, "変身前", precure.PreTransformAliasId, aliasById);
        AppendAliasEntry(aliasEntries, "変身後", precure.TransformAliasId, aliasById);
        if (precure.Transform2AliasId is int t2)
            AppendAliasEntry(aliasEntries, "変身後 2", t2, aliasById);
        if (precure.AltFormAliasId is int alt)
            AppendAliasEntry(aliasEntries, "別形態", alt, aliasById);

        // 表示用タイトル（変身後名）と、リンク先となる character_id。
        string mainTitle = aliasById.TryGetValue(precure.TransformAliasId, out var ma) ? ma.Name : $"プリキュア#{precure.PrecureId}";
        int? characterId = aliasById.TryGetValue(precure.TransformAliasId, out var maCa) ? maCa.CharacterId : (int?)null;

        // 誕生日を「M月D日」「Month D」に整形。
        string birthdayJa = (precure.BirthMonth is byte bm && precure.BirthDay is byte bd)
            ? $"{bm}月{bd}日"
            : "";

        // 声優情報。
        string voiceActorName = "";
        int? voiceActorPersonId = precure.VoiceActorPersonId;
        if (voiceActorPersonId is int vid && personsById.TryGetValue(vid, out var vp))
        {
            voiceActorName = vp.FullName;
        }

        // 家族関係（character_id を起点に、関連キャラを引いて続柄ラベルを引く）。
        var familyRows = new List<FamilyRelationRow>();
        if (characterId.HasValue)
        {
            var fams = await _familyRepo.GetByCharacterAsync(characterId.Value, ct).ConfigureAwait(false);
            foreach (var f in fams.OrderBy(f => f.DisplayOrder ?? byte.MaxValue))
            {
                if (!charactersById.TryGetValue(f.RelatedCharacterId, out var related)) continue;
                string relationLabel = relationKindMap.TryGetValue(f.RelationCode, out var rk) ? rk.NameJa : f.RelationCode;
                familyRows.Add(new FamilyRelationRow
                {
                    RelationLabel = relationLabel,
                    RelatedCharacterId = related.CharacterId,
                    RelatedCharacterName = related.Name,
                    Notes = f.Notes ?? ""
                });
            }
        }

        // 声の出演履歴：4 名義の character_alias_id それぞれに紐付く Involvement を集める。
        // 同一エピソードで複数名義が並んだら 1 行にまとめる（重複排除キーは (SeriesId, EpisodeId)）。
        var aliasIds = new List<int> { precure.PreTransformAliasId, precure.TransformAliasId };
        if (precure.Transform2AliasId is int t2b) aliasIds.Add(t2b);
        if (precure.AltFormAliasId is int altb) aliasIds.Add(altb);
        var voiceRows = BuildVoiceCastRows(aliasIds, aliasById, personAliasById);

        var content = new PrecureDetailModel
        {
            Precure = new PrecureView
            {
                PrecureId = precure.PrecureId,
                MainTitle = mainTitle,
                CharacterId = characterId,
                BirthdayJa = birthdayJa,
                VoiceActorName = voiceActorName,
                VoiceActorPersonId = voiceActorPersonId,
                School = precure.School ?? "",
                SchoolClass = precure.SchoolClass ?? "",
                FamilyBusiness = precure.FamilyBusiness ?? "",
                Notes = precure.Notes ?? ""
            },
            AliasEntries = aliasEntries,
            FamilyRelations = familyRows,
            VoiceCastRows = voiceRows
        };
        var layout = new LayoutModel
        {
            PageTitle = mainTitle,
            MetaDescription = $"{mainTitle} のプロフィールと声の出演履歴。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プリキュア", Url = "/precures/" },
                new BreadcrumbItem { Label = mainTitle, Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.PrecureUrl(precure.PrecureId), "precures", "precures-detail.sbn", content, layout);
    }

    private static void AppendAliasEntry(
        List<PrecureAliasEntry> sink,
        string label,
        int aliasId,
        IReadOnlyDictionary<int, CharacterAlias> aliasById)
    {
        if (aliasById.TryGetValue(aliasId, out var a))
        {
            sink.Add(new PrecureAliasEntry
            {
                Label = label,
                Name = a.Name,
                NameKana = a.NameKana ?? "",
                NameEn = a.NameEn ?? ""
            });
        }
    }

    /// <summary>
    /// 4 名義の character_alias_id それぞれに紐付く CHARACTER_VOICE Involvement を集めて、
    /// シリーズ → エピソード順でテーブル行を組み立てる。
    /// 同一エピソードで複数名義が並んだ場合は 1 行に統合し、声優は連結表示する。
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

        // (SeriesId, EpisodeId ?? 0) でグルーピング。グループ内で声優・名義を集約。
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

                // 声優名義名（PersonAliasId → PersonAlias.DisplayTextOverride or .Name）。
                // 同一エピソードで複数声優が居た場合は「、」で連結。
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

                // 演じた名義名（character_alias_id → name）。複数あれば「、」で連結。
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

    private sealed class PrecuresIndexModel
    {
        public IReadOnlyList<PrecureIndexRow> Precures { get; set; } = Array.Empty<PrecureIndexRow>();
        public int TotalCount { get; set; }
    }

    private sealed class PrecureIndexRow
    {
        public int PrecureId { get; set; }
        public string TransformName { get; set; } = "";
        public string PreTransformName { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
    }

    private sealed class PrecureDetailModel
    {
        public PrecureView Precure { get; set; } = new();
        public IReadOnlyList<PrecureAliasEntry> AliasEntries { get; set; } = Array.Empty<PrecureAliasEntry>();
        public IReadOnlyList<FamilyRelationRow> FamilyRelations { get; set; } = Array.Empty<FamilyRelationRow>();
        public IReadOnlyList<VoiceCastRow> VoiceCastRows { get; set; } = Array.Empty<VoiceCastRow>();
    }

    private sealed class PrecureView
    {
        public int PrecureId { get; set; }
        public string MainTitle { get; set; } = "";
        public int? CharacterId { get; set; }
        public string BirthdayJa { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
        public string School { get; set; } = "";
        public string SchoolClass { get; set; } = "";
        public string FamilyBusiness { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class PrecureAliasEntry
    {
        public string Label { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
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
        /// <summary>当該エピソードで使用された名義名（連名は「、」で連結）。</summary>
        public string AliasNames { get; set; } = "";
        /// <summary>声優名（連名は「、」で連結、暫定で空文字を許容）。</summary>
        public string VoiceActorNames { get; set; } = "";
    }
}
