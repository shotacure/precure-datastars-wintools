
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
    /// 「初クレジット日」昇順で並べる（v1.3.0 ブラッシュアップ続編で kana 昇順から変更）。
    /// <para>
    /// 初クレジット日 = 当該キャラが最初にクレジットされたエピソードの放送日。
    /// SERIES スコープのクレジット（EpisodeId が null）はシリーズの開始日（StartDate）を代用する。
    /// クレジット 0 件のキャラは時系列で並べようがないため、セクション内の末尾に kana 昇順で
    /// まとめて並べる（連結 1 リスト構成）。同じ初クレジット日のキャラが複数あるときは
    /// CharacterId 昇順で安定化する。
    /// </para>
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

        // 「初クレジット日」を求めるヘルパ：当該キャラの全 alias_id の Involvement を走査して
        // 各 Involvement の放送日（EpisodeId 経由で OnAirAt を逆引き）の最小値を返す。
        // SERIES スコープの Involvement は当該シリーズの StartDate を代用する。
        // Involvement が 1 件も無いキャラは null を返し、ソート時は末尾扱いとする。
        DateOnly? FirstCreditDate(int characterId)
        {
            if (!aliasesByCharacter.TryGetValue(characterId, out var aliases)) return null;
            DateOnly? best = null;
            foreach (var a in aliases)
            {
                if (!_index.ByCharacterAlias.TryGetValue(a.AliasId, out var invs)) continue;
                foreach (var inv in invs)
                {
                    DateOnly? d;
                    if (inv.EpisodeId.HasValue)
                    {
                        // エピソードスコープ：当該エピソードの放送日
                        var ep = LookupEpisode(inv.SeriesId, inv.EpisodeId.Value);
                        d = ep is null ? null : DateOnly.FromDateTime(ep.OnAirAt);
                    }
                    else
                    {
                        // SERIES スコープ：シリーズの開始日を代用
                        d = _ctx.SeriesById.TryGetValue(inv.SeriesId, out var s)
                            ? s.StartDate
                            : (DateOnly?)null;
                    }
                    if (d.HasValue && (best is null || d.Value < best.Value)) best = d.Value;
                }
            }
            return best;
        }

        // セクション = character_kind 単位。kind マスタの display_order 順で並べる。
        // セクション内のメンバは (1) 初クレジット日昇順、(2) クレジットなし群を末尾に kana 昇順、で連結。
        var sections = characters
            .GroupBy(c => c.CharacterKind)
            .Select(g => new
            {
                KindCode = g.Key,
                KindLabel = kindMap.TryGetValue(g.Key, out var k) ? k.NameJa : g.Key,
                Order = kindMap.TryGetValue(g.Key, out var k2) ? (k2.DisplayOrder ?? byte.MaxValue) : byte.MaxValue,
                Members = BuildIndexSectionMembers(g, FirstCreditDate, CountInvolvements)
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

    /// <summary>
    /// 単一セクション（character_kind）内のメンバ並びを構築する。
    /// クレジットありのキャラを「初クレジット日昇順 → CharacterId 昇順」、その後ろに
    /// クレジットなしのキャラを「kana 昇順 → 名前昇順」で連結する。
    /// </summary>
    private static List<CharacterIndexRow> BuildIndexSectionMembers(
        IGrouping<string, Character> kindGroup,
        Func<int, DateOnly?> firstCreditDate,
        Func<int, int> countInvolvements)
    {
        var rows = kindGroup
            .Select(c => new
            {
                Character = c,
                FirstDate = firstCreditDate(c.CharacterId),
                Count = countInvolvements(c.CharacterId)
            })
            .ToList();

        var withCredit = rows
            .Where(x => x.FirstDate.HasValue)
            .OrderBy(x => x.FirstDate!.Value)
            .ThenBy(x => x.Character.CharacterId);

        var withoutCredit = rows
            .Where(x => !x.FirstDate.HasValue)
            .OrderBy(x => string.IsNullOrEmpty(x.Character.NameKana) ? 1 : 0)
            .ThenBy(x => x.Character.NameKana, StringComparer.Ordinal)
            .ThenBy(x => x.Character.Name, StringComparer.Ordinal);

        return withCredit.Concat(withoutCredit)
            .Select(x => new CharacterIndexRow
            {
                CharacterId = x.Character.CharacterId,
                Name = x.Character.Name,
                NameKana = x.Character.NameKana ?? "",
                EpisodeCount = x.Count,
                HasInvolvement = x.Count > 0
            })
            .ToList();
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
    /// 全名義の character_alias_id に紐付く CHARACTER_VOICE Involvement を集約
    /// （v1.3.0 ブラッシュアップ続編：「(シリーズ × キャラ名義 × 声優名義)」のキー単位でグループ化に変更）。
    /// 旧仕様（前段）：エピソード行 1 件 1 行のテーブル。
    /// 旧仕様（中段）：シリーズ単位 1 行に集約し、シリーズ内のキャラ名義・声優名義を「、」連結で併記。
    /// 新仕様（本実装）：シリーズ内でも「キャラ名義」「声優名義」が異なれば別行に分割。
    ///   変身前/変身後の差や、ゲスト声優起用回（同キャラの声優が話数によって異なる）も別行で見える。
    /// 各行は「{キャラ名義} (CV: {声優名義})」のラベルを持ち、テンプレ側でそのまま表示する。
    /// 全話担当時は IsAllEpisodes=true、シリーズ全体スコープ（EpisodeId が無い）のクレジットは
    /// 別途「（シリーズ全体）」のラベル付き行に分離して並べる（既存仕様踏襲）。
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

        // (SeriesId, CharacterAliasId?, PersonAliasId?, IsSeriesScope) の 4 タプルで集約。
        // CharacterAliasId / PersonAliasId は null のケースもありうる（マスタ未紐付け、raw_character_text 利用など）
        // ので nullable で扱う。IsSeriesScope は EpisodeId 有無の二値。
        var groups = new Dictionary<(int SeriesId, int? CharacterAliasId, int? PersonAliasId, bool IsSeriesScope),
                                    (HashSet<int> EpisodeNos, string CharacterLabel, string PersonLabel)>();

        foreach (var inv in all)
        {
            bool isSeriesScope = !inv.EpisodeId.HasValue;
            int? caid = inv.CharacterAliasId;
            int? paid = inv.PersonAliasId;
            var key = (inv.SeriesId, caid, paid, isSeriesScope);

            if (!groups.TryGetValue(key, out var bucket))
            {
                // キャラ名義ラベル：CharacterAliasId が解決できればその名前、できなければ raw_character_text、
                // どちらも無ければ空文字（テンプレ側で空判定）。
                string charLabel;
                if (caid.HasValue && aliasById.TryGetValue(caid.Value, out var ca))
                    charLabel = ca.Name;
                else if (!string.IsNullOrEmpty(inv.RawCharacterText))
                    charLabel = inv.RawCharacterText;
                else
                    charLabel = "";

                // 声優名義ラベル：PersonAliasId が解決できれば DisplayTextOverride 優先、無ければ Name。
                string personLabel = "";
                if (paid.HasValue && personAliasById.TryGetValue(paid.Value, out var pa))
                    personLabel = pa.DisplayTextOverride ?? pa.Name;

                bucket = (new HashSet<int>(), charLabel, personLabel);
                groups[key] = bucket;
            }

            if (!isSeriesScope)
            {
                var ep = LookupEpisode(inv.SeriesId, inv.EpisodeId!.Value);
                if (ep is not null) bucket.EpisodeNos.Add(ep.SeriesEpNo);
                groups[key] = bucket; // HashSet は参照型だが、struct に包んでるので念のため再代入
            }
        }

        // シリーズ単位でまとめて並べ替え。シリーズ内では：
        //   1. シリーズ全体スコープ行（EpisodeId 無し）を先頭
        //   2. その後ろにエピソードスコープ行を「最小話数昇順 → キャラ名義ラベル昇順 → 声優名義ラベル昇順」で
        var rows = new List<VoiceCastRow>();
        foreach (var seriesGroup in groups.GroupBy(g => g.Key.SeriesId).OrderBy(g => SeriesStartDate(g.Key)))
        {
            if (!_ctx.SeriesById.TryGetValue(seriesGroup.Key, out var series)) continue;

            var allSeriesEpNos = _ctx.EpisodesBySeries.TryGetValue(seriesGroup.Key, out var allEps)
                ? allEps.Select(e => e.SeriesEpNo).ToHashSet()
                : new HashSet<int>();

            // シリーズ全体スコープの行を先に
            foreach (var entry in seriesGroup.Where(g => g.Key.IsSeriesScope)
                                              .OrderBy(g => g.Value.CharacterLabel, StringComparer.Ordinal)
                                              .ThenBy(g => g.Value.PersonLabel, StringComparer.Ordinal))
            {
                rows.Add(new VoiceCastRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    RangeLabel = "（シリーズ全体）",
                    IsAllEpisodes = false,
                    CharacterAndCvLabel = ComposeCharacterAndCvLabel(entry.Value.CharacterLabel, entry.Value.PersonLabel)
                });
            }

            // エピソード単位の行（最小話数昇順）
            foreach (var entry in seriesGroup.Where(g => !g.Key.IsSeriesScope)
                                              .OrderBy(g => g.Value.EpisodeNos.Count == 0 ? int.MaxValue : g.Value.EpisodeNos.Min())
                                              .ThenBy(g => g.Value.CharacterLabel, StringComparer.Ordinal)
                                              .ThenBy(g => g.Value.PersonLabel, StringComparer.Ordinal))
            {
                var episodeNos = entry.Value.EpisodeNos;
                if (episodeNos.Count == 0) continue;

                bool isAll = allSeriesEpNos.Count > 0 && episodeNos.SetEquals(allSeriesEpNos);
                string rangeLabel = isAll ? string.Empty : EpisodeRangeCompressor.Compress(episodeNos);

                rows.Add(new VoiceCastRow
                {
                    SeriesSlug = series.Slug,
                    SeriesTitle = series.Title,
                    RangeLabel = rangeLabel,
                    IsAllEpisodes = isAll,
                    CharacterAndCvLabel = ComposeCharacterAndCvLabel(entry.Value.CharacterLabel, entry.Value.PersonLabel)
                });
            }
        }
        return rows;
    }

    /// <summary>
    /// 「{キャラ名義} (CV: {声優名義})」表記を組み立てる。
    /// 片方しか無い場合のフォールバック：
    ///   両方あり → "美墨なぎさ (CV: 本名陽子)"
    ///   キャラのみ → "美墨なぎさ"
    ///   声優のみ → "(CV: 本名陽子)"
    ///   両方なし → ""（テンプレ側で空判定して非表示）
    /// </summary>
    private static string ComposeCharacterAndCvLabel(string characterLabel, string personLabel)
    {
        bool hasChar = !string.IsNullOrEmpty(characterLabel);
        bool hasPerson = !string.IsNullOrEmpty(personLabel);
        if (hasChar && hasPerson) return $"{characterLabel} (CV: {personLabel})";
        if (hasChar) return characterLabel;
        if (hasPerson) return $"(CV: {personLabel})";
        return "";
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

    /// <summary>
    /// キャラ詳細の声優出演 1 行。
    /// 旧仕様（中段）：シリーズ単位で集約、AliasNames / VoiceActorNames を「、」連結で併記。
    /// 新仕様（v1.3.0 ブラッシュアップ続編）：シリーズ × キャラ名義 × 声優名義 のグループキー単位で 1 行。
    /// 表示は「{キャラ名義} (CV: {声優名義})」を 1 ラベルにまとめた CharacterAndCvLabel をそのまま流す。
    /// </summary>
    private sealed class VoiceCastRow
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        /// <summary>
        /// 話数圧縮表記。例：「#1〜4, 8」。全話のときは空文字（テンプレ側で「(全話)」マークを別表示）。
        /// シリーズ全体スコープのときは「（シリーズ全体）」のような任意ラベルを入れる。
        /// </summary>
        public string RangeLabel { get; set; } = "";
        /// <summary>シリーズ内の全話を担当しているフラグ。テンプレで「(全話)」マークを出すかの判定。</summary>
        public bool IsAllEpisodes { get; set; }
        /// <summary>「{キャラ名義} (CV: {声優名義})」形式の表示ラベル（事前整形済み）。</summary>
        public string CharacterAndCvLabel { get; set; } = "";
    }
}
