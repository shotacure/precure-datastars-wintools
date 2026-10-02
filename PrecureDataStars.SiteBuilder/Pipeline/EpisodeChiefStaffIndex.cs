using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 各話のチーフ（脚本・絵コンテ・演出・作画監督・美術。区分は <see cref="EpisodeChiefRoles"/>）の顔ぶれを
/// TV 系の全話分まとめた索引と、「この組み合わせは通算何回目か」の数え上げ。
/// <para>
/// エピソード詳細のスタッフ欄（「演出と作画監督のこの組み合わせは通算 3 回目」）と、
/// 歴代記録ページ（最も多く組んだ演出と作画監督）が同じ数え方を使う。
/// 数えるのは本編クレジット（<see cref="BuildContext.CreditsByEpisode"/>）に PERSON / TEXT エントリとして
/// 現れた顔ぶれで、本放送限定（<see cref="CreditBlockEntry.IsBroadcastOnly"/>）のエントリは
/// エピソード詳細のスタッフ欄と同じく含めない。
/// </para>
/// <para>
/// 人物は名義をまたいで同じ人物として扱う（<see cref="BuildContext.PersonIdByAlias"/> で人物 ID に寄せる）。
/// 人物に紐付かない名義（共同名義など）は名義 ID、TEXT エントリは生テキストで区別する。
/// 1 つの役職に複数人が並ぶ回（作画監督が 2 人など）は、その全員の集合を 1 つの顔ぶれとして扱う。
/// </para>
/// <para>
/// 通算の順序は放送日時順（同時刻ならシリーズの放送開始順 → 話数順）。
/// 読み取り専用で、並列のページ生成から参照してよい。
/// </para>
/// </summary>
public sealed class EpisodeChiefStaffIndex
{
    /// <summary>演出と作画監督の組み合わせ。</summary>
    public static readonly ChiefCombo DirectorAndAnimationDirector =
        new("director-ad", "演出と作画監督", new[] { EpisodeChiefRoles.EpisodeDirector, EpisodeChiefRoles.AnimationDirector });

    /// <summary>脚本・演出・作画監督の組み合わせ。</summary>
    public static readonly ChiefCombo ScreenplayDirectorAnimationDirector =
        new("screenplay-director-ad", "脚本・演出・作画監督", new[] { EpisodeChiefRoles.Screenplay, EpisodeChiefRoles.EpisodeDirector, EpisodeChiefRoles.AnimationDirector });

    /// <summary>数える組み合わせの一覧（表示順）。</summary>
    public static IReadOnlyList<ChiefCombo> Combos { get; } = new[] { DirectorAndAnimationDirector, ScreenplayDirectorAnimationDirector };

    /// <summary>エピソード ID → 区分（1〜5）→ 顔ぶれキーの昇順リスト。区分にエントリが無ければキー自体を持たない。</summary>
    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<string>>> _chiefsByEpisode;

    /// <summary>組み合わせコード → 顔ぶれ署名 → その顔ぶれで作られた話の ID（放送順）。</summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> _occurrences;

    /// <summary>組み合わせコード → 顔ぶれ署名 → その顔ぶれが初めて現れた順（0 始まり。放送順で早いほど小さい）。</summary>
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> _firstAppearance;

    /// <summary>顔ぶれキー → 表示用（名前とリンク先）。</summary>
    private readonly IReadOnlyDictionary<string, ChiefPerson> _people;

    private EpisodeChiefStaffIndex(
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, IReadOnlyList<string>>> chiefsByEpisode,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>> occurrences,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> firstAppearance,
        IReadOnlyDictionary<string, ChiefPerson> people)
    {
        _chiefsByEpisode = chiefsByEpisode;
        _occurrences = occurrences;
        _firstAppearance = firstAppearance;
        _people = people;
    }

    /// <summary>空の索引（クレジット未収録時のフォールバック）。</summary>
    public static EpisodeChiefStaffIndex Empty { get; } = new(
        new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<string>>>(),
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(),
        new Dictionary<string, IReadOnlyDictionary<string, int>>(),
        new Dictionary<string, ChiefPerson>());

    /// <summary>
    /// BuildContext のクレジット階層スナップショットから索引を組み立てる。
    /// 人物の表示名とリンク先は <see cref="BuildContext.EntityUrls"/> を引くので、台帳の確定後に呼ぶ。
    /// </summary>
    public static EpisodeChiefStaffIndex Build(BuildContext ctx)
    {
        var people = new Dictionary<string, ChiefPerson>(StringComparer.Ordinal);
        var chiefsByEpisode = new Dictionary<int, IReadOnlyDictionary<int, IReadOnlyList<string>>>();

        // 放送順に並べた TV 系の全話。映画系（SERIES 単位のクレジット）は「話」の数え上げに入れない。
        var episodes = new List<(Series Series, Episode Episode)>();
        foreach (var s in ctx.Series)
        {
            if (ctx.IsMovieKindSeries(s.SeriesId)) continue;
            if (!ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            foreach (var e in eps) episodes.Add((s, e));
        }
        episodes.Sort((a, b) =>
        {
            int c = a.Episode.OnAirAt.CompareTo(b.Episode.OnAirAt);
            if (c != 0) return c;
            c = a.Series.StartDate.CompareTo(b.Series.StartDate);
            if (c != 0) return c;
            return a.Episode.SeriesEpNo.CompareTo(b.Episode.SeriesEpNo);
        });

        foreach (var (_, ep) in episodes)
        {
            if (!ctx.CreditsByEpisode.TryGetValue(ep.EpisodeId, out var credits)) continue;
            var byRole = new Dictionary<int, SortedSet<string>>();

            foreach (var credit in credits)
            {
                if (!ctx.CreditTree.CardsByCreditId.TryGetValue(credit.CreditId, out var cards)) continue;
                foreach (var card in cards)
                foreach (var tier in card.Tiers)
                foreach (var group in tier.Groups)
                foreach (var cardRole in group.Roles)
                {
                    string? roleCode = cardRole.Role.RoleCode;
                    if (roleCode is null) continue;
                    string? nameJa = ctx.RoleByCode.TryGetValue(roleCode, out var role) ? role.NameJa : null;
                    int? chief = EpisodeChiefRoles.Classify(roleCode, nameJa);
                    if (chief is not int chiefNo) continue;

                    foreach (var block in cardRole.Blocks)
                    foreach (var entry in block.Entries)
                    {
                        if (entry.IsBroadcastOnly) continue;
                        var person = ResolvePerson(ctx, entry);
                        if (person is null) continue;
                        people.TryAdd(person.Key, person);
                        if (!byRole.TryGetValue(chiefNo, out var set))
                        {
                            set = new SortedSet<string>(StringComparer.Ordinal);
                            byRole[chiefNo] = set;
                        }
                        set.Add(person.Key);
                    }
                }
            }

            if (byRole.Count == 0) continue;
            chiefsByEpisode[ep.EpisodeId] = byRole.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.ToList());
        }

        // 組み合わせごとに、顔ぶれ署名 → 放送順の話 ID 列と、署名が初めて現れた順を作る。
        var occurrences = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<int>>>(StringComparer.Ordinal);
        var firstAppearance = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var combo in Combos)
        {
            var bySignature = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var firstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, ep) in episodes)
            {
                if (!chiefsByEpisode.TryGetValue(ep.EpisodeId, out var chiefs)) continue;
                string? signature = Signature(combo, chiefs);
                if (signature is null) continue;
                if (!bySignature.TryGetValue(signature, out var list))
                {
                    list = new List<int>();
                    bySignature[signature] = list;
                    firstSeen[signature] = firstSeen.Count;
                }
                list.Add(ep.EpisodeId);
            }
            occurrences[combo.Code] = bySignature.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value, StringComparer.Ordinal);
            firstAppearance[combo.Code] = firstSeen;
        }

        return new EpisodeChiefStaffIndex(chiefsByEpisode, occurrences, firstAppearance, people);
    }

    /// <summary>
    /// 指定の話での組み合わせの通算情報。組み合わせを構成する役職のどれかが空（未収録・該当役職なし）なら null。
    /// </summary>
    public ChiefComboOccurrence? Lookup(ChiefCombo combo, int episodeId)
    {
        if (!_chiefsByEpisode.TryGetValue(episodeId, out var chiefs)) return null;
        string? signature = Signature(combo, chiefs);
        if (signature is null) return null;
        if (!_occurrences.TryGetValue(combo.Code, out var bySignature)) return null;
        if (!bySignature.TryGetValue(signature, out var list)) return null;
        int index = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == episodeId) { index = i; break; }
        }
        if (index < 0) return null;
        return new ChiefComboOccurrence(
            Ordinal: index + 1,
            Total: list.Count,
            FirstEpisodeId: list[0],
            PreviousEpisodeId: index > 0 ? list[index - 1] : null,
            NextEpisodeId: index + 1 < list.Count ? list[index + 1] : null);
    }

    /// <summary>
    /// 組み合わせの登場回数が多い順の上位。同数は同じ順位（次の順位は同数の数だけ繰り下げ）で、
    /// <paramref name="limit"/> 位までを返す（同率は全件含める）。並びは回数の多い順 → 初回の放送順。
    /// </summary>
    public IReadOnlyList<ChiefComboRanking> TopCombos(ChiefCombo combo, int limit)
    {
        if (!_occurrences.TryGetValue(combo.Code, out var bySignature)) return Array.Empty<ChiefComboRanking>();
        // 同数の並びは初回の放送順。話 ID の大小は放送順と一致しない（ID は投入順）ので、
        // 索引構築時に放送順で記録した「初めて現れた順」を使う。
        var firstOrder = _firstAppearance.TryGetValue(combo.Code, out var fo) ? fo : new Dictionary<string, int>();

        var ordered = bySignature
            .Select(kv => (Signature: kv.Key, Episodes: kv.Value))
            .OrderByDescending(x => x.Episodes.Count)
            .ThenBy(x => firstOrder.TryGetValue(x.Signature, out int order) ? order : int.MaxValue)
            .ThenBy(x => x.Signature, StringComparer.Ordinal)
            .ToList();

        var result = new List<ChiefComboRanking>();
        int rank = 0;
        int prevCount = -1;
        for (int i = 0; i < ordered.Count; i++)
        {
            var (signature, episodes) = ordered[i];
            if (episodes.Count != prevCount)
            {
                rank = i + 1;
                prevCount = episodes.Count;
            }
            if (rank > limit) break;
            result.Add(new ChiefComboRanking(rank, episodes.Count, MembersOf(combo, signature), episodes));
        }
        return result;
    }

    /// <summary>顔ぶれ署名から、役職ごとの人物リストを復元する（表示用）。</summary>
    private IReadOnlyList<ChiefComboMember> MembersOf(ChiefCombo combo, string signature)
    {
        var parts = signature.Split('|');
        var members = new List<ChiefComboMember>();
        for (int i = 0; i < combo.ChiefRoles.Count && i < parts.Length; i++)
        {
            var persons = parts[i].Split('+')
                .Select(k => _people.TryGetValue(k, out var p) ? p : new ChiefPerson(k, k, ""))
                .ToList();
            members.Add(new ChiefComboMember(combo.ChiefRoles[i], EpisodeChiefRoles.Label(combo.ChiefRoles[i]), persons));
        }
        return members;
    }

    /// <summary>組み合わせの署名。構成役職のいずれかが空なら null（その回は数えない）。</summary>
    private static string? Signature(ChiefCombo combo, IReadOnlyDictionary<int, IReadOnlyList<string>> chiefs)
    {
        var parts = new string[combo.ChiefRoles.Count];
        for (int i = 0; i < combo.ChiefRoles.Count; i++)
        {
            if (!chiefs.TryGetValue(combo.ChiefRoles[i], out var keys) || keys.Count == 0) return null;
            parts[i] = string.Join("+", keys);
        }
        return string.Join("|", parts);
    }

    /// <summary>
    /// エントリ 1 件を顔ぶれキーに解決する。PERSON は人物 ID（無ければ名義 ID）、TEXT は生テキスト。
    /// それ以外（CHARACTER_VOICE / COMPANY / LOGO）はチーフの顔ぶれに数えない。
    /// </summary>
    private static ChiefPerson? ResolvePerson(BuildContext ctx, CreditBlockEntry entry)
    {
        switch (entry.EntryKind)
        {
            case "PERSON":
                if (entry.PersonAliasId is not int aliasId) return null;
                var alias = ctx.PersonAliasById.TryGetValue(aliasId, out var a) ? a : null;
                string aliasName = alias?.DisplayTextOverride ?? alias?.Name ?? "";
                if (ctx.PersonIdByAlias.TryGetValue(aliasId, out int personId))
                {
                    string name = ctx.EntityUrls.PersonDisplayName(personId) ?? aliasName;
                    if (string.IsNullOrEmpty(name)) return null;
                    return new ChiefPerson($"P{personId}", name, PathUtil.PersonUrl(personId));
                }
                if (string.IsNullOrEmpty(aliasName)) return null;
                return new ChiefPerson($"A{aliasId}", aliasName, "");
            case "TEXT":
                string? raw = entry.RawText?.Trim();
                if (string.IsNullOrEmpty(raw)) return null;
                return new ChiefPerson($"T:{raw}", raw, "");
            default:
                return null;
        }
    }
}

/// <summary>数える組み合わせの定義。<see cref="ChiefRoles"/> は <see cref="EpisodeChiefRoles"/> の区分番号。</summary>
public sealed record ChiefCombo(string Code, string Label, IReadOnlyList<int> ChiefRoles);

/// <summary>ある話での組み合わせの通算情報。</summary>
/// <param name="Ordinal">この話が通算何回目か（1 始まり）。</param>
/// <param name="Total">収録済みの範囲での登場回数。</param>
/// <param name="FirstEpisodeId">初回の話。</param>
/// <param name="PreviousEpisodeId">前回の話（初回なら null）。</param>
/// <param name="NextEpisodeId">次回の話（最新なら null）。</param>
public sealed record ChiefComboOccurrence(int Ordinal, int Total, int FirstEpisodeId, int? PreviousEpisodeId, int? NextEpisodeId);

/// <summary>組み合わせの登場回数ランキングの 1 行。</summary>
public sealed record ChiefComboRanking(int Rank, int Count, IReadOnlyList<ChiefComboMember> Members, IReadOnlyList<int> EpisodeIds);

/// <summary>組み合わせを構成する役職 1 つ分の顔ぶれ。</summary>
public sealed record ChiefComboMember(int ChiefRole, string RoleLabel, IReadOnlyList<ChiefPerson> Persons);

/// <summary>顔ぶれの 1 人。<see cref="Url"/> が空なら人物ページを持たない（名義のみ・テキストのみ）。</summary>
public sealed record ChiefPerson(string Key, string Name, string Url);
