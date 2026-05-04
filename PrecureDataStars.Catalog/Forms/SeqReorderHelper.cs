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
/// CreditCardRole の場合は <c>tier</c> と <c>order_in_tier</c> の 2 列で順序を
/// 表現する設計のため、本ヘルパでは「同一 tier 内での並べ替えのみサポート」する。
/// 別 tier への移動が必要なケースは現状想定していない（tier はほぼ tier=1 固定で運用）。
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
    /// 同一 card 内 × 同一 tier の役職一覧を、与えた順序で order_in_tier=1, 2, 3, ...
    /// に再採番する。tier は呼び出し側でフィルタしておくこと（別 tier をまたぐ
    /// 並べ替えは本ヘルパではサポートしない）。
    /// </summary>
    public static async Task ReorderCardRolesInTierAsync(
        CreditCardRolesRepository repo,
        int cardId,
        byte tier,
        IList<CreditCardRole> orderedListSameTier,
        System.Threading.CancellationToken ct = default)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));
        if (orderedListSameTier is null) throw new ArgumentNullException(nameof(orderedListSameTier));

        // すべて同 tier であることを呼び出し側保証
        if (orderedListSameTier.Any(r => r.Tier != tier))
            throw new ArgumentException("orderedListSameTier に異なる tier の役職が含まれています。", nameof(orderedListSameTier));

        var updates = orderedListSameTier
            .Select((r, idx) => (cardRoleId: r.CardRoleId, tier: tier, orderInTier: (byte)(idx + 1)))
            .ToList();
        await repo.BulkUpdateSeqAsync(cardId, updates, ct);
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
