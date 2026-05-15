using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット本体構造（カード／役職／ブロック）の並べ替え用共通ヘルパ。
/// <para>
/// クレジット本体には UNIQUE 制約 (credit_id, card_seq) や
/// (card_role_id, block_seq) が掛かっているため、単純な値の差し替えでは
/// 一時的な重複で衝突する。これを避けるため各リポジトリ側に
/// <c>BulkUpdateSeqAsync</c> を追加し、内部で「全件をいったん +10000 オフセットに
/// 退避 → 本来の値で再採番」をトランザクション 1 本にまとめて実行する。
/// 本ヘルパは UI 側（ボタン式 ↑↓ と TreeView DnD の両方）から共通で使う。
/// </para>
/// <para>
/// CreditCardRole の場合は <c>card_group_id</c> + <c>order_in_group</c> の
/// 「同一 card_group_id 内での並べ替え」を <see cref="ReorderCardRolesInGroupAsync"/> が、
/// 別 Group / 別 Tier / 別 Card への自由な乗り換えを <see cref="RelocateCardRoleAsync"/> が
/// それぞれ受け持つ（Tier や Card の移動は、移動先 Group の card_group_id を解決して渡せば足りる）。
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
    /// 同一 card_group_id の役職一覧を、与えた順序で order_in_group=1, 2, 3, ... に再採番する
    /// （旧 (card_id, tier, group_in_tier) シグネチャから単一 card_group_id へ刷新）。
    /// 別 Group をまたぐ並べ替えは本ヘルパではサポートしない
    /// （その用途には <see cref="RelocateCardRoleAsync"/> を使う）。
    /// </summary>
    public static async Task ReorderCardRolesInGroupAsync(
        CreditCardRolesRepository repo,
        int cardGroupId,
        IList<CreditCardRole> orderedListSameGroup,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedListSameGroup is null) throw new ArgumentNullException(nameof(orderedListSameGroup));

        // すべて同一 card_group_id であることを呼び出し側保証
        if (orderedListSameGroup.Any(r => r.CardGroupId != cardGroupId))
        {
            throw new ArgumentException(
                "orderedListSameGroup に対象グループ外の役職（異なる card_group_id）が混在しています。",
                nameof(orderedListSameGroup));
        }

        var updates = orderedListSameGroup
            .Select((r, idx) => (
                cardRoleId: r.CardRoleId,
                cardGroupId: cardGroupId,
                orderInGroup: (byte)(idx + 1)))
            .ToList();
        await repo.BulkUpdateSeqAsync(updates, ct);
    }

    /// <summary>
    /// 役職 1 つを「別 Group」へ自由に乗り換える（簡素化）。
    /// <para>
    /// Tier / Group は実体テーブルのため、移動先は <see cref="CreditCardGroup.CardGroupId"/>
    /// 1 個で一意に決まるようになった。Tier の指定は不要（Group が Tier 配下なので暗黙的に決まる）。
    /// 別カードへの移動は事前に CreditEditorForm 側で「移動先カードの該当 Tier / Group の card_group_id を
    /// 解決してから本メソッドに渡す」ことで実現する。
    /// </para>
    /// </summary>
    /// <param name="repo">CreditCardRolesRepository。</param>
    /// <param name="movedRoleId">移動する役職の card_role_id。</param>
    /// <param name="oldGroupOrdered">移動前グループの役職一覧（元の order_in_group 昇順、移動対象を含む）。</param>
    /// <param name="newGroupOrdered">移動先グループの役職一覧（元の order_in_group 昇順、移動対象は含まない）。</param>
    /// <param name="newCardGroupId">移動先 card_group_id。</param>
    /// <param name="insertAt"><paramref name="newGroupOrdered"/> 内の挿入位置（0 始まり）。末尾なら <c>newGroupOrdered.Count</c>。</param>
    public static async Task RelocateCardRoleAsync(
        CreditCardRolesRepository repo,
        int movedRoleId,
        IList<CreditCardRole> oldGroupOrdered,
        IList<CreditCardRole> newGroupOrdered,
        int newCardGroupId,
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
        var updates = new System.Collections.Generic.List<(int cardRoleId, int cardGroupId, byte orderInGroup)>();

        // 1) 元グループの残り役職：元の card_group_id のまま 1, 2, 3, ... に詰めなおす
        for (int i = 0; i < oldRemaining.Count; i++)
        {
            var r = oldRemaining[i];
            updates.Add((r.CardRoleId, r.CardGroupId, (byte)(i + 1)));
        }

        // 2) 新グループ（moved 含む）：newCardGroupId で 1, 2, 3, ...
        // 注意：oldGroup と newGroup が同一（card_group_id が等しい）の場合、
        //       1) と 2) で同じ役職を 2 度 update することになる。BulkUpdateSeqAsync は
        //       最後に勝った値で確定するので、結果は 2) の値になり整合する。
        for (int i = 0; i < newWithMoved.Count; i++)
        {
            var r = newWithMoved[i];
            updates.Add((r.CardRoleId, newCardGroupId, (byte)(i + 1)));
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
    /// entry_seq=1, 2, 3, ... に再採番する。
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
    /// 同一 (episode_id, is_broadcast_only) グループ内の主題歌行を、
    /// 与えた順序で seq=1, 2, 3, ... に再採番する（OP/ED/INSERT 区別なくなった
    /// ため対象を全 theme_kind に拡張）。
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
        // BulkUpdateSeqAsync 側でも検証するが、呼び出し側でも早期に弾く。
        // theme_kind の制約は撤廃（OP/ED/INSERT を 1 つの劇中順に統合できる）。
        if (orderedListSameGroup.Any(r => r.EpisodeId != episodeId
                                          || r.IsBroadcastOnly != isBroadcastOnly))
        {
            throw new ArgumentException(
                "orderedListSameGroup に対象グループ外の行（異なる episode_id / is_broadcast_only）" +
                "が混在しています。",
                nameof(orderedListSameGroup));
        }
        await repo.BulkUpdateSeqAsync(episodeId, isBroadcastOnly, orderedListSameGroup, ct);
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
    /// <summary>
    /// 同 list[index] と list[index+1] の入れ替え（下向き）。
    /// </summary>
    public static bool MoveDown<T>(IList<T> list, int index)
    {
        if (index < 0 || index >= list.Count - 1) return false;
        (list[index + 1], list[index]) = (list[index], list[index + 1]);
        return true;
    }

    /// <summary>
    /// エントリの自由乗り換えを実行する。
    /// 内部実装は <see cref="CreditBlockEntriesRepository.RelocateAsync"/> に委譲する薄いラッパー。
    /// </summary>
    /// <param name="repo">エントリリポジトリ。</param>
    /// <param name="movedEntryId">移動対象エントリの entry_id。</param>
    /// <param name="srcBlockId">移動元の block_id。</param>
    /// <param name="isBroadcastOnly">移動対象の is_broadcast_only 値（移動先でも保持される）。</param>
    /// <param name="oldGroupOrdered">移動元グループの全エントリ（移動対象を含む元順序）。</param>
    /// <param name="newGroupOrdered">移動先グループの全エントリ（移動対象を含まない元順序）。</param>
    /// <param name="dstBlockId">移動先の block_id。</param>
    /// <param name="insertAt"><paramref name="newGroupOrdered"/> 内の挿入位置（0 始まり）。</param>
    public static async Task RelocateBlockEntryAsync(
        CreditBlockEntriesRepository repo,
        int movedEntryId,
        int srcBlockId,
        bool isBroadcastOnly,
        IList<CreditBlockEntry> oldGroupOrdered,
        IList<CreditBlockEntry> newGroupOrdered,
        int dstBlockId,
        int insertAt,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (oldGroupOrdered is null) throw new ArgumentNullException(nameof(oldGroupOrdered));
        if (newGroupOrdered is null) throw new ArgumentNullException(nameof(newGroupOrdered));

        // 移動対象を除外した残りリストを作って Repository に委譲
        var srcRemaining = oldGroupOrdered.Where(e => e.EntryId != movedEntryId).ToList();
        await repo.RelocateAsync(
            movedEntryId,
            srcBlockId,
            isBroadcastOnly,
            srcRemaining,
            newGroupOrdered,
            dstBlockId,
            insertAt,
            ct);
    }
}