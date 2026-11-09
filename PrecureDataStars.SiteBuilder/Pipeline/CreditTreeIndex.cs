using PrecureDataStars.Data.Models;
using PrecureDataStars.TemplateRendering;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クレジット階層（Card → Tier → Group → CardRole → Block → Entry の 6 段）を
/// ビルド開始時に 1 度だけ全件取得して、credit_id 単位でネスト構造化した事前展開インデックス。
/// <see cref="Rendering.CreditTreeRenderer"/> はページ生成中に階層別の <c>GetBy*Async</c> を
/// 発火する代わりに、本インデックスから credit_id でツリーを取り出して HTML 組み立てだけを行う。
/// <para>
/// 旧 CreditTreeRenderer は 1 credit あたり cards 1 + tiers × cards + groups × tiers +
/// roles × groups + blocks × roles + entries × blocks の per-key 呼び出しを順次発火しており、
/// クレジット入りエピソード × OP/ED 2 系統で累積数千〜数万クエリのオーダーになっていた。
/// 本インデックスは 6 階層をテーブル単位で <c>GetAllAsync</c> 6 回呼び出すだけで構築できるため、
/// SiteDataLoader で 1 度だけ走らせて <see cref="BuildContext.CreditTree"/> で共有する。
/// </para>
/// <para>
/// インデックスは <see cref="CreditBlockEntry.IsBroadcastOnly"/> も含めて全エントリを保持する。
/// レンダラ側で「is_broadcast_only=1 は描画対象外」のフィルタを既存ロジックで掛ける前提のため、
/// 本クラスでは絞り込みを行わない（インデックスを別用途で使う将来の余白を残す）。
/// </para>
/// </summary>
public sealed class CreditTreeIndex
{
    /// <summary>
    /// credit_id → ぶら下がる <see cref="CreditCardSnapshot"/> 一覧（card_seq 昇順整列済み）。集計・抜き出し用。
    /// 本放送のカード（<c>credit_cards.media_version = BROADCAST</c>）は、組になる Blu-ray・配信のカード（描画の枠と同じ 1 組）に
    /// 同じ人物・同じ会社がいるエントリと筆頭を除いて含める（本放送にしか名前の無い人を数え、両方に出る人を二重に数えない）。
    /// 該当 credit にカードが 1 枚も無い場合は辞書に登録されない（呼び出し側は <c>TryGetValue</c> で受ける）。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> CardsByCreditId { get; }

    /// <summary>
    /// credit_id → ぶら下がる <see cref="CreditCardSnapshot"/> 一覧（card_seq 昇順整列済み）。描画用で、本放送のカードも含む全部の版。
    /// クレジットの描画（<c>CreditTreeRenderer</c>）だけが使う。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> CardsByCreditIdForDisplay { get; }

    private CreditTreeIndex(
        IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> cardsByCreditId,
        IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> cardsByCreditIdForDisplay)
    {
        CardsByCreditId = cardsByCreditId;
        CardsByCreditIdForDisplay = cardsByCreditIdForDisplay;
    }

    /// <summary>
    /// 6 階層 Repository それぞれの <c>GetAllAsync</c> を 1 回ずつ呼び、親 ID で GroupBy しながら
    /// ネスト構造を組み立てる。各階層は per-key 取得時と同等の並び順を維持する：
    /// <list type="bullet">
    ///   <item>cards: credit_id, card_seq 昇順</item>
    ///   <item>tiers: card_id, tier_no 昇順</item>
    ///   <item>groups: card_tier_id, group_no 昇順</item>
    ///   <item>roles: card_group_id, order_in_group 昇順</item>
    ///   <item>blocks: card_role_id, block_seq 昇順</item>
    ///   <item>entries: block_id, is_broadcast_only, entry_seq 昇順</item>
    /// </list>
    /// </summary>
    public static async Task<CreditTreeIndex> BuildAsync(
        CreditCardsRepository cardsRepo,
        CreditCardTiersRepository tiersRepo,
        CreditCardGroupsRepository groupsRepo,
        CreditCardRolesRepository cardRolesRepo,
        CreditRoleBlocksRepository blocksRepo,
        CreditBlockEntriesRepository entriesRepo,
        CancellationToken ct = default,
        PersonAliasPersonsRepository? aliasPersonsRepo = null)
    {
        // 6 テーブルを順次全件取得（接続再利用は Repository 側に任せる）。
        var allCards = await cardsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allTiers = await tiersRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allGroups = await groupsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allRoles = await cardRolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allBlocks = await blocksRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allEntries = await entriesRepo.GetAllAsync(ct).ConfigureAwait(false);
        // 名義 → 人物（本放送と Blu-ray・配信で同じ人が別の名義で出ても同じ人として照らす）。無ければ名義どうしで照らす。
        IReadOnlyDictionary<int, int>? personByAlias = null;
        if (aliasPersonsRepo is not null)
        {
            var links = await aliasPersonsRepo.GetAllAsync(ct).ConfigureAwait(false);
            personByAlias = links.GroupBy(l => l.AliasId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.PersonSeq).First().PersonId);
        }
        return BuildFromRows(allCards, allTiers, allGroups, allRoles, allBlocks, allEntries, personByAlias);
    }

    /// <summary>
    /// 6 階層分の全行（呼び出し側で既に <c>GetAllAsync</c> 済み）を受け取って、ネスト構造を組み立てる
    /// 純粋同期ビルダ。SiteDataLoader が <see cref="Task.WhenAll(System.Threading.Tasks.Task[])"/> で
    /// 階層別 SELECT を並列発火した結果を渡す経路で使う。
    /// </summary>
    public static CreditTreeIndex BuildFromRows(
        IReadOnlyList<CreditCard> allCards,
        IReadOnlyList<CreditCardTier> allTiers,
        IReadOnlyList<CreditCardGroup> allGroups,
        IReadOnlyList<CreditCardRole> allRoles,
        IReadOnlyList<CreditRoleBlock> allBlocks,
        IReadOnlyList<CreditBlockEntry> allEntries,
        IReadOnlyDictionary<int, int>? personByAlias = null)
    {
        // 子テーブルを親 ID でグルーピング。各 GetAllAsync の戻り値は親 ID でソート済みのため、
        // GroupBy 結果はそのまま per-key 取得と同じ並び順になる。
        var entriesByBlock = allEntries
            .GroupBy(e => e.BlockId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CreditBlockEntry>)g.ToList());

        var blocksByRole = allBlocks
            .GroupBy(b => b.CardRoleId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CreditBlockSnapshot>)g
                    .Select(b => new CreditBlockSnapshot(
                        b,
                        entriesByBlock.TryGetValue(b.BlockId, out var es) ? es : Array.Empty<CreditBlockEntry>()))
                    .ToList());

        var rolesByGroup = allRoles
            .GroupBy(r => r.CardGroupId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CreditCardRoleSnapshot>)g
                    .Select(r => new CreditCardRoleSnapshot(
                        r,
                        blocksByRole.TryGetValue(r.CardRoleId, out var bs) ? bs : Array.Empty<CreditBlockSnapshot>()))
                    .ToList());

        var groupsByTier = allGroups
            .GroupBy(g => g.CardTierId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CreditCardGroupSnapshot>)g
                    .Select(grp => new CreditCardGroupSnapshot(
                        grp,
                        rolesByGroup.TryGetValue(grp.CardGroupId, out var rs) ? rs : Array.Empty<CreditCardRoleSnapshot>()))
                    .ToList());

        var tiersByCard = allTiers
            .GroupBy(t => t.CardId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CreditCardTierSnapshot>)g
                    .Select(t => new CreditCardTierSnapshot(
                        t,
                        groupsByTier.TryGetValue(t.CardTierId, out var gs) ? gs : Array.Empty<CreditCardGroupSnapshot>()))
                    .ToList());

        // credit_id 単位で最終的なツリーを組み立てる。
        var cardsByCreditId = new Dictionary<int, IReadOnlyList<CreditCardSnapshot>>();
        var snapshotsByCredit = allCards
            .GroupBy(c => c.CreditId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CreditCardSnapshot>)g
                    .Select(c => new CreditCardSnapshot(
                        c,
                        tiersByCard.TryGetValue(c.CardId, out var ts) ? ts : Array.Empty<CreditCardTierSnapshot>()))
                    .ToList());

        var cardsByCreditIdForDisplay = new Dictionary<int, IReadOnlyList<CreditCardSnapshot>>();
        foreach (var (creditId, list) in snapshotsByCredit)
        {
            cardsByCreditIdForDisplay[creditId] = list;
            // 集計用は、本放送のカードから組の Blu-ray・配信のカードにいる人物・会社を除いた並び。
            var forStats = BuildStatsCards(list, personByAlias);
            if (forStats.Count > 0) cardsByCreditId[creditId] = forStats;
        }

        return new CreditTreeIndex(cardsByCreditId, cardsByCreditIdForDisplay);
    }

    /// <summary>
    /// 集計用のカードの並びを作る。共通と Blu-ray・配信のカードはそのまま、本放送のカードは、組になる Blu-ray・配信のカード
    /// （<see cref="CardVersionLayout.SplitVersionFrames{T}"/> の 1 組）に同じ人物・同じ会社がいるエントリと筆頭の会社を除いて入れる。
    /// </summary>
    private static List<CreditCardSnapshot> BuildStatsCards(IReadOnlyList<CreditCardSnapshot> cards, IReadOnlyDictionary<int, int>? personByAlias)
    {
        if (!cards.Any(c => c.Card.MediaVersion == CreditMediaVersions.Broadcast)) return cards.ToList();

        string PersonKey(int aliasId) => personByAlias is not null && personByAlias.TryGetValue(aliasId, out var pid) ? $"p{pid}" : $"a{aliasId}";
        static IEnumerable<CreditBlockSnapshot> Blocks(CreditCardSnapshot c) =>
            c.Tiers.SelectMany(t => t.Groups).SelectMany(g => g.Roles).SelectMany(r => r.Blocks);

        // 本放送のカード → 組の Blu-ray・配信のカードにいる人物・会社の鍵
        var hvKeysByBroadcastCard = new Dictionary<int, HashSet<string>>();
        foreach (var frame in CardVersionLayout.SplitVersionFrames(cards, c => c.Card.MediaVersion))
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in frame.Where(c => c.Card.MediaVersion == CreditMediaVersions.HomeVideo))
                foreach (var b in Blocks(c))
                {
                    if (b.Block.LeadingCompanyAliasId is int l1) keys.Add($"c{l1}");
                    if (b.Block.LeadingCompany2AliasId is int l2) keys.Add($"c{l2}");
                    foreach (var e in b.Entries)
                    {
                        if (e.PersonAliasId is int pa) keys.Add(PersonKey(pa));
                        if (e.CompanyAliasId is int ca) keys.Add($"c{ca}");
                    }
                }
            foreach (var c in frame.Where(c => c.Card.MediaVersion == CreditMediaVersions.Broadcast))
                hvKeysByBroadcastCard[c.Card.CardId] = keys;
        }

        var result = new List<CreditCardSnapshot>(cards.Count);
        foreach (var c in cards)
        {
            if (c.Card.MediaVersion != CreditMediaVersions.Broadcast || !hvKeysByBroadcastCard.TryGetValue(c.Card.CardId, out var hv))
            {
                result.Add(c);
                continue;
            }
            // 本放送のカードは、組の Blu-ray・配信のカードにいない人物・会社だけを残した写しにする（筆頭の会社も同じ）
            var tiers = c.Tiers.Select(t => new CreditCardTierSnapshot(t.Tier, t.Groups.Select(g => new CreditCardGroupSnapshot(g.Group,
                g.Roles.Select(r => new CreditCardRoleSnapshot(r.Role, r.Blocks.Select(b =>
                {
                    var block = b.Block;
                    bool dropLead1 = block.LeadingCompanyAliasId is int l1 && hv.Contains($"c{l1}");
                    bool dropLead2 = block.LeadingCompany2AliasId is int l2 && hv.Contains($"c{l2}");
                    if (dropLead1 || dropLead2)
                    {
                        block = CopyBlock(block);
                        if (dropLead1) block.LeadingCompanyAliasId = null;
                        if (dropLead2) block.LeadingCompany2AliasId = null;
                    }
                    var entries = b.Entries.Where(e =>
                        !(e.PersonAliasId is int pa && hv.Contains(PersonKey(pa))) &&
                        !(e.CompanyAliasId is int ca && hv.Contains($"c{ca}"))).ToList();
                    return new CreditBlockSnapshot(block, entries);
                }).ToList())).ToList())).ToList())).ToList();
            result.Add(new CreditCardSnapshot(c.Card, tiers));
        }
        return result;
    }

    /// <summary>ブロックの行の写し（集計用に筆頭の会社だけを外すため。元の行は描画用にそのまま残す）。</summary>
    private static CreditRoleBlock CopyBlock(CreditRoleBlock b) => new()
    {
        BlockId = b.BlockId, CardRoleId = b.CardRoleId, BlockSeq = b.BlockSeq, ColCount = b.ColCount,
        ContinuesPrevious = b.ContinuesPrevious, LeadingCompanyAliasId = b.LeadingCompanyAliasId,
        LeadingCompanyNotOnScreen = b.LeadingCompanyNotOnScreen, LeadingCompany2AliasId = b.LeadingCompany2AliasId,
        LeadingCompanySeparator = b.LeadingCompanySeparator, LeadingCompanySuffix = b.LeadingCompanySuffix,
        LeadingText = b.LeadingText, HeadingSeriesId = b.HeadingSeriesId, HeadingText = b.HeadingText,
        HeadingItalic = b.HeadingItalic, Notes = b.Notes, CreatedAt = b.CreatedAt, UpdatedAt = b.UpdatedAt,
        CreatedBy = b.CreatedBy, UpdatedBy = b.UpdatedBy,
    };
}

/// <summary>クレジット階層の Card 段スナップショット。Tier 群をぶら下げる。</summary>
public sealed record CreditCardSnapshot(
    CreditCard Card,
    IReadOnlyList<CreditCardTierSnapshot> Tiers);

/// <summary>クレジット階層の Tier 段スナップショット。Group 群をぶら下げる。</summary>
public sealed record CreditCardTierSnapshot(
    CreditCardTier Tier,
    IReadOnlyList<CreditCardGroupSnapshot> Groups);

/// <summary>クレジット階層の Group 段スナップショット。CardRole 群をぶら下げる。</summary>
public sealed record CreditCardGroupSnapshot(
    CreditCardGroup Group,
    IReadOnlyList<CreditCardRoleSnapshot> Roles);

/// <summary>クレジット階層の CardRole 段スナップショット。Block 群をぶら下げる。</summary>
public sealed record CreditCardRoleSnapshot(
    CreditCardRole Role,
    IReadOnlyList<CreditBlockSnapshot> Blocks);

/// <summary>クレジット階層の Block 段スナップショット。Entry 群をぶら下げる。 <see cref="CreditBlockEntry.IsBroadcastOnly"/> は本スナップショットでは絞り込まずに全件保持し、 レンダラ側でフィルタする方針。</summary>
public sealed record CreditBlockSnapshot(
    CreditRoleBlock Block,
    IReadOnlyList<CreditBlockEntry> Entries);
