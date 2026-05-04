using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット本体構造（カード／役職／ブロック）の並べ替え用共通ヘルパ
/// （v1.2.0 工程 B-2 追加）。
/// <para>
/// クレジット本体には UNIQUE 制約 (credit_id, card_seq) や
/// (card_role_id, block_seq) が掛かっているため、単純な値の差し替えでは
/// 一時的な重複で衝突する。これを避けるため各リポジトリ側に
/// <c>BulkUpdateSeqAsync</c> を追加し、内部で「全件をいったん +10000 オフセットに
/// 退避 → 本来の値で再採番」をトランザクション 1 本にまとめて実行する。
/// 本ヘルパは UI 側（ボタン式 ↑↓ と TreeView DnD の両方）から共通で使う。
/// </para>
/// <para>
/// CreditCardRole の場合は <c>(tier, group_in_tier, order_in_group)</c> の 3 列で順序を
/// 表現する（v1.2.0 工程 E で 2 列構成から拡張）。本ヘルパでは
/// 「同一 (card_id, tier, group_in_tier) グループ内での並べ替え」を
/// <see cref="ReorderCardRolesInGroupAsync"/> が、別グループ／別カードへの自由な乗り換えを
/// <see cref="RelocateCardRoleAsync"/> がそれぞれ受け持つ。
/// </para>
/// </summary>
internal static class SeqReorderHelper
{
    /// <summary>
    /// 同一 credit 内のカード一覧を、与えた順序で card_seq=1, 2, 3, ... に再採番する
    /// （DB 側はリポジトリの <c>BulkUpdateSeqAsync</c> がトランザクションで処理）。
    /// </summary>
    public static async Task ReorderCardsAsync(
        CreditCardsRepository repo,
        int creditId,
        IList<CreditCard> orderedList,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedList is null) throw new ArgumentNullException(nameof(orderedList));

        // 1, 2, 3, ... の連番を割り当て、(card_id, new_seq) のリストにして渡す
        var updates = orderedList
            .Select((c, idx) => (cardId: c.CardId, cardSeq: (byte)(idx + 1)))
            .ToList();
        await repo.BulkUpdateSeqAsync(creditId, updates, ct);
    }

    /// <summary>
    /// 同一 card × 同一 tier × 同一 group_in_tier の役職一覧を、与えた順序で
    /// order_in_group=1, 2, 3, ... に再採番する（v1.2.0 工程 E で
    /// 旧 ReorderCardRolesInTierAsync を改名・拡張したもの）。
    /// 別 card / 別 tier / 別 group をまたぐ並べ替えは本ヘルパではサポートしない
    /// （その用途には <see cref="RelocateCardRoleAsync"/> を使う）。
    /// </summary>
    public static async Task ReorderCardRolesInGroupAsync(
        CreditCardRolesRepository repo,
        int cardId,
        byte tier,
        byte groupInTier,
        IList<CreditCardRole> orderedListSameGroup,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedListSameGroup is null) throw new ArgumentNullException(nameof(orderedListSameGroup));

        // すべて同 (cardId, tier, groupInTier) であることを呼び出し側保証
        if (orderedListSameGroup.Any(r => r.CardId != cardId || r.Tier != tier || r.GroupInTier != groupInTier))
        {
            throw new ArgumentException(
                "orderedListSameGroup に対象グループ外の役職（異なる card_id / tier / group_in_tier）が混在しています。",
                nameof(orderedListSameGroup));
        }

        var updates = orderedListSameGroup
            .Select((r, idx) => (
                cardRoleId: r.CardRoleId,
                cardId: cardId,
                tier: tier,
                groupInTier: groupInTier,
                orderInGroup: (byte)(idx + 1)))
            .ToList();
        await repo.BulkUpdateSeqAsync(updates, ct);
    }

    /// <summary>
    /// 役職 1 つを「別 card / 別 tier / 別 group」へ自由に乗り換える（v1.2.0 工程 E 追加）。
    /// <para>
    /// CreditEditorForm の DnD で、役職ノードを同じクレジット内の別カード・別 tier・
    /// 別グループへドロップしたときに呼ばれる。動作:
    /// <list type="number">
    ///   <item><description>移動対象の元グループ（旧 (card, tier, group)）から該当役職を取り除き、残りを order_in_group で詰めなおす</description></item>
    ///   <item><description>移動先グループ（新 (card, tier, group)）の挿入位置以降を 1 ずつ後ろへずらす</description></item>
    ///   <item><description>移動対象を新位置（card / tier / group / order）に確定する</description></item>
    /// </list>
    /// 全ステップを 1 つの <see cref="CreditCardRolesRepository.BulkUpdateSeqAsync"/> 呼び出しで実行する
    /// （内部で退避値経由の 2 段階更新、1 トランザクション）。
    /// </para>
    /// </summary>
    /// <param name="repo">CreditCardRolesRepository。</param>
    /// <param name="movedRoleId">移動する役職の card_role_id。</param>
    /// <param name="oldGroupOrdered">
    /// 移動前グループ (old card_id, old tier, old group_in_tier) の役職一覧
    /// （元の <c>order_in_group</c> 昇順、移動対象を含む）。
    /// </param>
    /// <param name="newGroupOrdered">
    /// 移動先グループ (new card_id, new tier, new group_in_tier) の役職一覧
    /// （元の <c>order_in_group</c> 昇順、移動対象は含まない）。
    /// </param>
    /// <param name="newCardId">移動先 card_id。</param>
    /// <param name="newTier">移動先 tier。</param>
    /// <param name="newGroupInTier">移動先 group_in_tier。</param>
    /// <param name="insertAt">
    /// <paramref name="newGroupOrdered"/> 内の挿入位置（0 始まり）。
    /// 末尾に追加するなら <c>newGroupOrdered.Count</c>。
    /// </param>
    public static async Task RelocateCardRoleAsync(
        CreditCardRolesRepository repo,
        int movedRoleId,
        IList<CreditCardRole> oldGroupOrdered,
        IList<CreditCardRole> newGroupOrdered,
        int newCardId,
        byte newTier,
        byte newGroupInTier,
        int insertAt,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (oldGroupOrdered is null) throw new ArgumentNullException(nameof(oldGroupOrdered));
        if (newGroupOrdered is null) throw new ArgumentNullException(nameof(newGroupOrdered));

        var moved = oldGroupOrdered.FirstOrDefault(r => r.CardRoleId == movedRoleId)
            ?? throw new ArgumentException("oldGroupOrdered に movedRoleId が含まれていません。", nameof(oldGroupOrdered));

        // 元グループから moved を除外して詰めなおすリスト
        var oldRemaining = oldGroupOrdered.Where(r => r.CardRoleId != movedRoleId).ToList();

        // 新グループの insertAt 位置に moved を挿入したリスト
        if (insertAt < 0) insertAt = 0;
        if (insertAt > newGroupOrdered.Count) insertAt = newGroupOrdered.Count;
        var newWithMoved = newGroupOrdered.ToList();
        newWithMoved.Insert(insertAt, moved);

        // updates タプルを組み立てる
        var updates = new System.Collections.Generic.List<(int cardRoleId, int cardId, byte tier, byte groupInTier, byte orderInGroup)>();

        // 1) 元グループの残り役職：旧 (card_id, tier, group_in_tier) のまま 1, 2, 3, ... に詰めなおす
        for (int i = 0; i < oldRemaining.Count; i++)
        {
            var r = oldRemaining[i];
            updates.Add((r.CardRoleId, r.CardId, r.Tier, r.GroupInTier, (byte)(i + 1)));
        }

        // 2) 新グループ（moved 含む）：(newCardId, newTier, newGroupInTier) で 1, 2, 3, ...
        // 注意：oldGroupOrdered と newGroupOrdered が同一グループ（同 card_id × tier × group_in_tier）の場合、
        //       1) と 2) で同じ役職を 2 度 update することになる。BulkUpdateSeqAsync は最後に勝った値で
        //       確定するので、結果は 2) の値になり整合する。
        for (int i = 0; i < newWithMoved.Count; i++)
        {
            var r = newWithMoved[i];
            updates.Add((r.CardRoleId, newCardId, newTier, newGroupInTier, (byte)(i + 1)));
        }

        await repo.BulkUpdateSeqAsync(updates, ct);
    }

    /// <summary>
    /// 同一 card_role 内のブロック一覧を、与えた順序で block_seq=1, 2, 3, ... に再採番する。
    /// </summary>
    public static async Task ReorderRoleBlocksAsync(
        CreditRoleBlocksRepository repo,
        int cardRoleId,
        IList<CreditRoleBlock> orderedList,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedList is null) throw new ArgumentNullException(nameof(orderedList));

        var updates = orderedList
            .Select((b, idx) => (blockId: b.BlockId, blockSeq: (byte)(idx + 1)))
            .ToList();
        await repo.BulkUpdateSeqAsync(cardRoleId, updates, ct);
    }

    /// <summary>
    /// 同一 (block_id, is_broadcast_only) グループ内のエントリ一覧を、与えた順序で
    /// entry_seq=1, 2, 3, ... に再採番する（v1.2.0 工程 B-3 追加）。
    /// 呼び出し側で <paramref name="orderedListSameFlag"/> が「同一 is_broadcast_only」で
    /// ある状態を保証すること（混在していると例外を投げる）。
    /// </summary>
    public static async Task ReorderBlockEntriesAsync(
        CreditBlockEntriesRepository repo,
        int blockId,
        bool isBroadcastOnly,
        IList<CreditBlockEntry> orderedListSameFlag,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedListSameFlag is null) throw new ArgumentNullException(nameof(orderedListSameFlag));
        if (orderedListSameFlag.Any(e => e.IsBroadcastOnly != isBroadcastOnly))
            throw new ArgumentException("orderedListSameFlag に異なる is_broadcast_only 値のエントリが混在しています。", nameof(orderedListSameFlag));

        var updates = orderedListSameFlag
            .Select((e, idx) => (entryId: e.EntryId, entrySeq: (ushort)(idx + 1)))
            .ToList();
        await repo.BulkUpdateSeqAsync(blockId, isBroadcastOnly, updates, ct);
    }

    /// <summary>
    /// 同一 (episode_id, is_broadcast_only, theme_kind='INSERT') グループ内の挿入歌行を、
    /// 与えた順序で insert_seq=1, 2, 3, ... に再採番する（v1.2.0 工程 D 追加）。
    /// マスタ主題歌タブの DnD 並べ替え後に呼び出される。
    /// </summary>
    public static async Task ReorderEpisodeThemeSongsAsync(
        EpisodeThemeSongsRepository repo,
        int episodeId,
        bool isBroadcastOnly,
        IList<EpisodeThemeSong> orderedListSameGroup,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedListSameGroup is null) throw new ArgumentNullException(nameof(orderedListSameGroup));
        // BulkUpdateInsertSeqAsync 側でも検証するが、呼び出し側でも早期に弾く
        if (orderedListSameGroup.Any(r => r.EpisodeId != episodeId
                                          || r.IsBroadcastOnly != isBroadcastOnly
                                          || r.ThemeKind != "INSERT"))
        {
            throw new ArgumentException(
                "orderedListSameGroup に対象グループ外の行（異なる episode_id / is_broadcast_only、" +
                "または theme_kind != 'INSERT'）が混在しています。",
                nameof(orderedListSameGroup));
        }
        await repo.BulkUpdateInsertSeqAsync(episodeId, isBroadcastOnly, orderedListSameGroup, ct);
    }

    /// <summary>
    /// 順序付きリスト内で、指定要素を 1 つ前に動かす（先頭なら何もしない）。
    /// ボタン式「↑」用のリスト操作ヘルパ。
    /// </summary>
    public static bool MoveUp<T>(IList<T> list, int index)
    {
        if (index <= 0 || index >= list.Count) return false;
        (list[index - 1], list[index]) = (list[index], list[index - 1]);
        return true;
    }

    /// <summary>
    /// 順序付きリスト内で、指定要素を 1 つ後ろに動かす（末尾なら何もしない）。
    /// ボタン式「↓」用のリスト操作ヘルパ。
    /// </summary>
    public static bool MoveDown<T>(IList<T> list, int index)
    {
        if (index < 0 || index >= list.Count - 1) return false;
        (list[index + 1], list[index]) = (list[index], list[index + 1]);
        return true;
    }
}
