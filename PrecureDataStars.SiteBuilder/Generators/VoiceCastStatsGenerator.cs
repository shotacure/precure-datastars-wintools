
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 出演話数の多い声優さんページ（<c>/stats/voice-cast/</c>）の生成
/// （v1.3.0 ブラッシュアップ続編で 3 セクション分割表示を 1 リスト化に再編）。
/// <para>
/// CHARACTER_VOICE エントリ経由の関与（<see cref="CreditInvolvementIndex.ByPersonAlias"/> のうち
/// <see cref="InvolvementKind.CharacterVoice"/> のもの）を、演じたキャラクターの種別を問わず横断で集計する。
/// 同一声優を複数キャラで複数回カウントしないため、(PersonId, EpisodeId) のユニーク集合で重複排除する。
/// </para>
/// <para>
/// 旧版は character_kind に応じてメイン（プリキュア） / サブ（ALLY/VILLAIN）/ ゲスト（SUPPORTING）の
/// 3 セクションに分割表示していたが、利用者からは「セクションをまたいでも同じ声優の総出演話数で並べたい」
/// という要望があり、本版で 1 リストに統合した。キャラ種別の集計は不要なので、character_kind による
/// 振り分けロジックは廃止し、character_alias_id が解決できるエントリは全件集計対象とする。
/// </para>
/// <para>
/// 順位は Wimbledon 形式（同点同順、次は同点者数だけ飛ぶ）で全件出力する。同点最終順位の取りこぼし無し。
/// raw_character_text のみで character_alias_id が未設定のエントリ（モブ等）は character 解決ができないため
/// 集計対象外とする方針を引き継ぐ。
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

        // character_alias_id → character_id の解決マップ。
        // 1 リスト化したのでキャラ種別（character_kind）による振り分けは不要だが、
        // 演じたキャラ名のサマリ表示には依然として characters.name が必要なので、両方のマップを保持する。
        var characterById = allCharacters.ToDictionary(c => c.CharacterId);
        var aliasToCharId = new Dictionary<int, int>();
        foreach (var a in allCharacterAliases)
        {
            aliasToCharId[a.AliasId] = a.CharacterId;
        }

        // 人物 → 紐付き alias 群。
        var aliasIdsByPersonId = new Dictionary<int, List<int>>();
        foreach (var p in allPersons)
        {
            // 後段の集計結果リスト rows と名前が衝突するためローカル名は aliasRows にする
            // （v1.3.0 ブラッシュアップ続編で 1 リスト化したときに集計結果側の rows を新設したため）。
            var aliasRows = await _personAliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
            aliasIdsByPersonId[p.PersonId] = aliasRows.Select(r => r.AliasId).ToList();
        }

        // 1 リスト集計：人物単位に「(SeriesId, EpisodeId) のユニーク集合 + 演じたキャラ集合」を蓄積する。
        // 同一声優が同じエピソード内で複数キャラを演じていても、エピソード単位で 1 とカウントする
        // （= ユーザー指示「同一声優を複数キャラで複数回カウントしない」を満たす）。
        var rows = new List<VoiceActorRow>();

        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            var episodes = new HashSet<(int SeriesId, int EpisodeId)>();
            var charIds = new HashSet<int>();

            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (inv.Kind != InvolvementKind.CharacterVoice) continue;
                    // character_alias_id が未設定のエントリは character 解決ができないので除外
                    // （raw_character_text のみのモブ等）。
                    if (inv.CharacterAliasId is not int caId) continue;
                    if (!aliasToCharId.TryGetValue(caId, out var charId)) continue;

                    // EpisodeId が null（SERIES スコープのクレジット）は集計対象外。
                    // CHARACTER_VOICE はエピソード単位で記録される運用なので通常は null にならないが、
                    // 防御的に弾く。
                    if (inv.EpisodeId is not int eid) continue;

                    episodes.Add((inv.SeriesId, eid));
                    charIds.Add(charId);
                }
            }

            if (episodes.Count > 0)
                rows.Add(BuildRow(p, episodes.Count, charIds, characterById));
        }

        AssignWimbledonRanks(rows);

        var content = new VoiceCastStatsModel
        {
            Rows = rows,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "出演話数の多い声優さん",
            MetaDescription = "声優の出演エピソード数を 1 リストで集計したランキング。同一声優を複数キャラで複数回カウントしない、エピソード単位の純粋な出演話数集計。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/roles/" },
                new BreadcrumbItem { Label = "出演話数の多い声優さん", Url = "" }
            }
        };
        _page.RenderAndWrite("/stats/voice-cast/", "stats", "stats-voice-cast.sbn", content, layout);

        _ctx.Logger.Success($"voice cast stats: 1 ページ（{rows.Count} 名）");
    }

    /// <summary>1 人物 1 行分の表示用行を構築。</summary>
    private static VoiceActorRow BuildRow(
        Person p,
        int episodeCount,
        HashSet<int> charIds,
        IReadOnlyDictionary<int, Character> characterById)
    {
        // 演じたキャラ名のサマリ。多いと冗長なので上位 5 件まで（キャラ名 50 音順）。
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

    /// <summary>
    /// Wimbledon 形式の順位付け（同点同順、次は同点者数だけ飛ぶ）。
    /// 並び順は EpisodeCount 降順、同点時は FullNameKana → FullName で安定化。
    /// </summary>
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

    /// <summary>
    /// 1 リスト化した声優ランキングのテンプレ用モデル。
    /// 旧版の MainSection / SubSection / GuestSection 構造は v1.3.0 ブラッシュアップ続編で廃止。
    /// </summary>
    private sealed class VoiceCastStatsModel
    {
        public IReadOnlyList<VoiceActorRow> Rows { get; set; } = Array.Empty<VoiceActorRow>();
        /// <summary>
        /// クレジット横断のサイト全体カバレッジラベル。
        /// テンプレ側の lead 段落末尾に &lt;br&gt; 改行で続けて表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
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
