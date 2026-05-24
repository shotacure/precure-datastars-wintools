using PrecureDataStars.Data.Models;
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
    /// credit_id → ぶら下がる <see cref="CreditCardSnapshot"/> 一覧（card_seq 昇順整列済み）。
    /// 該当 credit にカードが 1 枚も無い場合は辞書に登録されない（呼び出し側は <c>TryGetValue</c> で受ける）。
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> CardsByCreditId { get; }

    private CreditTreeIndex(IReadOnlyDictionary<int, IReadOnlyList<CreditCardSnapshot>> cardsByCreditId)
    {
        CardsByCreditId = cardsByCreditId;
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
        CancellationToken ct = default)
    {
        // 6 テーブルを順次全件取得（接続再利用は Repository 側に任せる）。
        var allCards = await cardsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allTiers = await tiersRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allGroups = await groupsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allRoles = await cardRolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allBlocks = await blocksRepo.GetAllAsync(ct).ConfigureAwait(false);
        var allEntries = await entriesRepo.GetAllAsync(ct).ConfigureAwait(false);

        // 子テーブルを親 ID でグルーピング。GetAllAsync の戻り値が既に親 ID でソート済みのため、
        // GroupBy 結果はそのまま使えば per-key 取得と同じ並び順になる。
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

        foreach (var (creditId, list) in snapshotsByCredit)
        {
            cardsByCreditId[creditId] = list;
        }

        return new CreditTreeIndex(cardsByCreditId);
    }
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
