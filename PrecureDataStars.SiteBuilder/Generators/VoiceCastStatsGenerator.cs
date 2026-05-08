using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 声優ランキング（<c>/stats/voice-cast/</c>）の生成（v1.3.0 タスク追加）。
/// <para>
/// CHARACTER_VOICE エントリ経由の関与（<see cref="CreditInvolvementIndex.ByPersonAlias"/> のうち
/// <see cref="InvolvementKind.CharacterVoice"/> のもの）を、演じたキャラクターの種別（<c>characters.character_kind</c>）
/// に応じて 3 セクションに振り分ける：
/// </para>
/// <list type="bullet">
///   <item><description>メインキャラ：character_kind = PRECURE（プリキュア本人を演じた声優）</description></item>
///   <item><description>サブキャラ：character_kind = ALLY または VILLAIN（仲間・敵）</description></item>
///   <item><description>ゲスト：character_kind = SUPPORTING（とりまく人々）</description></item>
/// </list>
/// <para>
/// 1 人の声優が複数のセクションにまたがる場合は **重複表示** する（メインも演じてるサブキャラもいる声優は
/// 両セクションに出現）。各セクション内では (PersonId × EpisodeId) で重複排除し、エピソード数の多い順
/// に Wimbledon 形式（同点同順、次は飛ばす）の順位を付ける。
/// </para>
/// <para>
/// raw_character_text のみで character_alias_id が未設定のエントリ（モブ等）は、種別が判定できないため
/// 集計対象外とする。
/// </para>
/// </summary>
public sealed class VoiceCastStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;

    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;

    public VoiceCastStatsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _personsRepo = new PersonsRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _charactersRepo = new CharactersRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating voice cast stats");

        // マスタロード。
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacters = (await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        // character_alias_id → character_kind コードへの解決マップ。
        // 同時に character_alias_id → character_id の解決マップも構築（ループで線形検索しないため）。
        var characterById = allCharacters.ToDictionary(c => c.CharacterId);
        var aliasToKind = new Dictionary<int, string>();
        var aliasToCharId = new Dictionary<int, int>();
        foreach (var a in allCharacterAliases)
        {
            aliasToCharId[a.AliasId] = a.CharacterId;
            if (characterById.TryGetValue(a.CharacterId, out var ch))
                aliasToKind[a.AliasId] = ch.CharacterKind;
        }

        // 人物 → 紐付き alias 群。
        var aliasIdsByPersonId = new Dictionary<int, List<int>>();
        foreach (var p in allPersons)
        {
            var rows = await _personAliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
            aliasIdsByPersonId[p.PersonId] = rows.Select(r => r.AliasId).ToList();
        }

        // 各セクションの集計結果。
        var mainRows = new List<VoiceActorRow>();
        var subRows = new List<VoiceActorRow>();
        var guestRows = new List<VoiceActorRow>();

        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            // セクションごとに (SeriesId, EpisodeId) ユニーク集合 + 演じたキャラ集合を別々に蓄積。
            var mainEpisodes = new HashSet<(int, int)>();
            var subEpisodes = new HashSet<(int, int)>();
            var guestEpisodes = new HashSet<(int, int)>();
            var mainCharIds = new HashSet<int>();
            var subCharIds = new HashSet<int>();
            var guestCharIds = new HashSet<int>();

            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (inv.Kind != InvolvementKind.CharacterVoice) continue;
                    if (inv.CharacterAliasId is not int caId) continue;
                    if (!aliasToKind.TryGetValue(caId, out var kind)) continue;
                    if (!aliasToCharId.TryGetValue(caId, out var charId)) continue;

                    var key = (inv.SeriesId, inv.EpisodeId ?? 0);

                    switch (kind)
                    {
                        case "PRECURE":
                            mainEpisodes.Add(key);
                            mainCharIds.Add(charId);
                            break;
                        case "ALLY":
                        case "VILLAIN":
                            subEpisodes.Add(key);
                            subCharIds.Add(charId);
                            break;
                        case "SUPPORTING":
                            guestEpisodes.Add(key);
                            guestCharIds.Add(charId);
                            break;
                    }
                }
            }

            if (mainEpisodes.Count > 0)
                mainRows.Add(BuildRow(p, mainEpisodes.Count, mainCharIds, characterById));
            if (subEpisodes.Count > 0)
                subRows.Add(BuildRow(p, subEpisodes.Count, subCharIds, characterById));
            if (guestEpisodes.Count > 0)
                guestRows.Add(BuildRow(p, guestEpisodes.Count, guestCharIds, characterById));
        }

        AssignWimbledonRanks(mainRows);
        AssignWimbledonRanks(subRows);
        AssignWimbledonRanks(guestRows);

        var content = new VoiceCastStatsModel
        {
            MainSection = new VoiceCastSection
            {
                SectionLabel = "メインキャラ（プリキュア）",
                SectionDescription = "プリキュア本人として CHARACTER_VOICE エントリでクレジットされたキャストを集計しています。",
                Rows = mainRows
            },
            SubSection = new VoiceCastSection
            {
                SectionLabel = "サブキャラ（仲間・敵）",
                SectionDescription = "character_kind が ALLY（仲間たち）・VILLAIN（敵）のキャラを演じたキャストを集計しています。",
                Rows = subRows
            },
            GuestSection = new VoiceCastSection
            {
                SectionLabel = "ゲスト（とりまく人々）",
                SectionDescription = "character_kind が SUPPORTING（とりまく人々）のキャラを演じたキャストを集計しています。raw_character_text のみで character_alias_id が未設定のモブキャラ等は対象外。",
                Rows = guestRows
            }
        };
        var layout = new LayoutModel
        {
            PageTitle = "声優ランキング",
            MetaDescription = "声優のキャラ種別ごとの出演エピソード数ランキング。メインキャラ・サブキャラ・ゲストの 3 セクションで階層表示。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/roles/" },
                new BreadcrumbItem { Label = "声優ランキング", Url = "" }
            }
        };
        _page.RenderAndWrite("/stats/voice-cast/", "stats", "stats-voice-cast.sbn", content, layout);

        _ctx.Logger.Success($"voice cast stats: 1 ページ（メイン {mainRows.Count} 名 / サブ {subRows.Count} 名 / ゲスト {guestRows.Count} 名）");
    }

    /// <summary>1 人物 1 セクション分の表示用行を構築。</summary>
    private static VoiceActorRow BuildRow(
        Person p,
        int episodeCount,
        HashSet<int> charIds,
        IReadOnlyDictionary<int, Character> characterById)
    {
        // 演じたキャラ名のサマリ。多いと冗長なので上位 5 件まで。
        var charNames = charIds
            .Select(id => characterById.TryGetValue(id, out var c) ? c.Name : null)
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .OrderBy(n => n, StringComparer.Ordinal)
            .Take(5)
            .ToList();
        bool hasMore = charIds.Count > 5;

        return new VoiceActorRow
        {
            PersonId = p.PersonId,
            FullName = p.FullName,
            FullNameKana = p.FullNameKana ?? "",
            EpisodeCount = episodeCount,
            CharacterCount = charIds.Count,
            CharacterNamesPreview = string.Join("、", charNames) + (hasMore ? " ほか" : "")
        };
    }

    private static void AssignWimbledonRanks(List<VoiceActorRow> rows)
    {
        rows.Sort((a, b) =>
        {
            int c = b.EpisodeCount.CompareTo(a.EpisodeCount);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.FullNameKana, b.FullNameKana);
            if (c != 0) return c;
            return string.CompareOrdinal(a.FullName, b.FullName);
        });

        int currentRank = 0, lastCount = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].EpisodeCount != lastCount)
            {
                currentRank = i + 1;
                lastCount = rows[i].EpisodeCount;
            }
            rows[i].Rank = currentRank;
        }
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class VoiceCastStatsModel
    {
        public VoiceCastSection MainSection { get; set; } = new();
        public VoiceCastSection SubSection { get; set; } = new();
        public VoiceCastSection GuestSection { get; set; } = new();
    }

    private sealed class VoiceCastSection
    {
        public string SectionLabel { get; set; } = "";
        public string SectionDescription { get; set; } = "";
        public IReadOnlyList<VoiceActorRow> Rows { get; set; } = Array.Empty<VoiceActorRow>();
    }

    private sealed class VoiceActorRow
    {
        public int Rank { get; set; }
        public int PersonId { get; set; }
        public string FullName { get; set; } = "";
        public string FullNameKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public int CharacterCount { get; set; }
        /// <summary>演じたキャラ名の上位 5 件をカンマ連結したサマリ（5 件超は「 ほか」付き）。</summary>
        public string CharacterNamesPreview { get; set; } = "";
    }
}
