using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 各話のスタッフの札（作品のページのエピソード一覧・トップページのエピソード欄・/episodes/・エピソード詳細のスタッフ欄）を組むときの、
/// 読むカードの選び方と、札の組み立て。
/// <para>
/// 札に出す役職・並び・色は作品ごとの表（<c>series_episode_staff_roles</c>）で決める。表に行のある役職だけを、
/// 役職コードの完全一致でクレジットから拾い、行の並び順・行の色で出す。表に行の無い作品には札を出さない。
/// どの役職も、その回にその役職の名前があるときだけ札が出る。作品の種別ごとの決まりは持たない。
/// </para>
/// </summary>
public static class EpisodeStaffCards
{
    /// <summary>絵コンテの役職コード。演出と同じ顔ぶれの回は、演出と 1 つの札にまとめる。</summary>
    public const string StoryboardCode = "STORYBOARD";

    /// <summary>演出の役職コード。</summary>
    public const string EpisodeDirectorCode = "EPISODE_DIRECTOR";

    /// <summary>
    /// 札を組むときに読むカードを、カードの順に返す。2 つの版のカードを持つクレジットは、組の後ろの版（集計の本体。
    /// 「Blu-ray・配信」「配信」など）と共通のカードだけを読み、組の前の版（「本放送」「本放送・Blu-ray」など）のカードは読まない。
    /// </summary>
    public static IEnumerable<CreditCardSnapshot> CardsForStaff(IReadOnlyList<CreditCardSnapshot> cards)
    {
        var ordered = cards.OrderBy(c => c.Card.CardSeq).ToList();
        var plan = TemplateRendering.CardVersionLayout.Plan(ordered, c => c.Card.MediaVersion);
        bool hasLower = plan.LowerVersion is not null && ordered.Any(c => c.Card.MediaVersion == plan.LowerVersion);
        return ordered.Where(c => !(hasLower && plan.IsUpper(c)));
    }

    /// <summary>カードの役職を、段 → グループ → グループ内の順（クレジットの順）に並べて返す。</summary>
    public static List<CreditCardRoleSnapshot> OrderedRoles(CreditCardSnapshot card)
        => card.Tiers.OrderBy(t => t.Tier.TierNo)
            .SelectMany(t => t.Groups.OrderBy(g => g.Group.GroupNo))
            .SelectMany(g => g.Roles.OrderBy(r => r.Role.OrderInGroup))
            .ToList();

    /// <summary>
    /// 1 話分のクレジットから、その回の札を組む。
    /// <list type="bullet">
    ///   <item>作品の行（<paramref name="rows"/>、並び順の順）にある役職だけを、役職コードの完全一致で拾う。行が無ければ札は無い。</item>
    ///   <item>札の文字は画面の役職の表記（role_label_text）があればそれ、無ければ役職マスタの名前。同じ役職でも表記の違うものは表記ごとに札を分ける
    ///     （並びは行の順、同じ役職の中はクレジットに初めて出た順）。</item>
    ///   <item>担当者は PERSON / TEXT のエントリを、クレジットの順に、重複判定キー（<c>P:alias_id</c> / <c>T:テキスト</c>）で 1 度だけ並べる。
    ///     本放送のみのエントリは読まない。担当者のいない札は出さない。</item>
    ///   <item>絵コンテと演出が作品の行に両方あり、その回にどちらも札が 1 つずつで、顔ぶれ（重複判定キーの集合）が同じなら、
    ///     絵コンテの札の位置に 2 つの役職を並べた 1 つの札にまとめ、名前は 1 回だけ出す。</item>
    ///   <item>札の色は行の色（style 属性は <see cref="RoleBadgeColor.InlineStyle"/>）、リンク先は系譜の代表の役職のページ（<see cref="PathUtil.CreatorsRoleUrl"/>）。</item>
    /// </list>
    /// </summary>
    /// <param name="credits">その回のクレジット（削除済みを除いたもの。この順に読む）。</param>
    /// <param name="cardsByCreditId">クレジット ID → カード群（<see cref="CreditTreeIndex.CardsByCreditId"/>）。</param>
    /// <param name="rows">作品の札の役職の行（並び順の順）。null や空なら札は無い。</param>
    /// <param name="roleMap">役職マスタ（role_code → <see cref="Role"/>）。</param>
    /// <param name="resolveEntry">エントリ 1 件から（重複判定キー、表示用 HTML）を取り出す。HTML が空のエントリは数えない。</param>
    public static IReadOnlyList<EpisodeStaffBadge> BuildBadges(
        IEnumerable<Credit> credits,
        IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> cardsByCreditId,
        IReadOnlyList<SeriesEpisodeStaffRole>? rows,
        IReadOnlyDictionary<string, Role> roleMap,
        Func<CreditBlockEntry, (string Key, string Html)> resolveEntry)
    {
        if (rows is null || rows.Count == 0) return Array.Empty<EpisodeStaffBadge>();

        // 役職コード → 行の位置（並び順の順に 0 から）と色。
        var rowIndexByCode = new Dictionary<string, (int Index, string Color)>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
            rowIndexByCode.TryAdd(rows[i].RoleCode, (i, RoleBadgeColor.Normalize(rows[i].BadgeColor)));

        // (役職コード, 札の文字) ごとの集まり。並びはクレジットに初めて出た順。
        var buckets = new List<Bucket>();
        foreach (var credit in credits)
        {
            if (!cardsByCreditId.TryGetValue(credit.CreditId, out var cardSnapshots)) continue;
            // 版のあるクレジットは、本放送の版のカードを読まない（札は Blu-ray・配信の版で組む）。
            foreach (var cardSnap in CardsForStaff(cardSnapshots))
            {
                foreach (var roleSnap in OrderedRoles(cardSnap))
                {
                    var cr = roleSnap.Role;
                    if (cr.RoleCode is null || !rowIndexByCode.TryGetValue(cr.RoleCode, out var row)) continue;

                    string label = !string.IsNullOrWhiteSpace(cr.RoleLabelText)
                        ? cr.RoleLabelText!
                        : roleMap.TryGetValue(cr.RoleCode, out var role) && !string.IsNullOrEmpty(role.NameJa) ? role.NameJa : cr.RoleCode;
                    var bucket = buckets.Find(b => b.RoleCode == cr.RoleCode && b.Label == label);
                    if (bucket is null)
                    {
                        bucket = new Bucket(cr.RoleCode, label, row.Index, row.Color);
                        buckets.Add(bucket);
                    }

                    foreach (var blockSnap in roleSnap.Blocks.OrderBy(b => b.Block.BlockSeq))
                    {
                        foreach (var e in blockSnap.Entries.Where(en => !en.IsBroadcastOnly).OrderBy(en => en.EntrySeq))
                        {
                            var (key, html) = resolveEntry(e);
                            if (string.IsNullOrEmpty(html)) continue;
                            if (bucket.Seen.Add(key)) bucket.Names.Add(html);
                        }
                    }
                }
            }
        }

        // 担当者のいる札を、行の順（同じ役職の中は初めて出た順）に並べる。OrderBy は安定なので初めて出た順が保たれる。
        var ordered = buckets.Where(b => b.Names.Count > 0).OrderBy(b => b.RowIndex).ToList();

        // 絵コンテと演出が同じ顔ぶれなら 1 つの札にまとめる（絵コンテの札の位置。演出の札は出さない）。
        Bucket? mergedStoryboard = null, mergedDirector = null;
        var storyboards = ordered.Where(b => b.RoleCode == StoryboardCode).ToList();
        var directors = ordered.Where(b => b.RoleCode == EpisodeDirectorCode).ToList();
        if (storyboards.Count == 1 && directors.Count == 1 && storyboards[0].Seen.SetEquals(directors[0].Seen))
        {
            mergedStoryboard = storyboards[0];
            mergedDirector = directors[0];
        }

        var result = new List<EpisodeStaffBadge>(ordered.Count);
        foreach (var b in ordered)
        {
            if (ReferenceEquals(b, mergedDirector)) continue;
            var roles = ReferenceEquals(b, mergedStoryboard)
                ? new[] { ToRole(b), ToRole(mergedDirector!) }
                : new[] { ToRole(b) };
            result.Add(new EpisodeStaffBadge { Roles = roles, NamesHtml = string.Join("、", b.Names) });
        }
        return result;
    }

    private static EpisodeStaffBadgeRole ToRole(Bucket b) => new()
    {
        RoleCode = b.RoleCode,
        Label = b.Label,
        RoleUrl = PathUtil.CreatorsRoleUrl(b.RoleCode),
        BadgeStyle = RoleBadgeColor.InlineStyle(b.Color),
        ColorHex = b.Color
    };

    /// <summary>札 1 つ分の集まり（役職コード・札の文字・行の位置と色・担当者）。</summary>
    private sealed class Bucket(string roleCode, string label, int rowIndex, string color)
    {
        public string RoleCode { get; } = roleCode;
        public string Label { get; } = label;
        public int RowIndex { get; } = rowIndex;
        public string Color { get; } = color;
        public List<string> Names { get; } = new();
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
    }
}
