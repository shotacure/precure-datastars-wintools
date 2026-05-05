using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット 1 件の全階層を DB から読み込んで <see cref="CreditDraftSession"/> を構築するローダ
/// （v1.2.0 工程 H-8 で導入）。
/// <para>
/// クレジット編集画面（CreditEditorForm）がクレジットを選択するたびに呼ばれ、
/// 配下の Card → Tier → Group → Role → Block → Entry を全て取得して Draft オブジェクトの木に変換する。
/// 各 Draft の <see cref="DraftBase.RealId"/> には DB の実 ID、<see cref="DraftBase.TempId"/> には
/// セッションから払い出した負数を入れ、<see cref="DraftBase.State"/> はすべて <see cref="DraftState.Unchanged"/> 起点。
/// </para>
/// <para>
/// 読み込みは Repository ごとに発行する（GetByCreditAsync / GetByCardAsync / GetByGroupAsync /
/// GetByCardRoleAsync / GetByBlockAsync）。クレジット 1 件の階層は通常せいぜい数十〜数百行
/// 程度なので、N+1 問題は実害が出ないと判断して個別取得とする。
/// </para>
/// </summary>
internal sealed class CreditDraftLoader
{
    private readonly CreditCardsRepository _cardsRepo;
    private readonly CreditCardTiersRepository _tiersRepo;
    private readonly CreditCardGroupsRepository _groupsRepo;
    private readonly CreditCardRolesRepository _rolesRepo;
    private readonly CreditRoleBlocksRepository _blocksRepo;
    private readonly CreditBlockEntriesRepository _entriesRepo;

    public CreditDraftLoader(
        CreditCardsRepository cardsRepo,
        CreditCardTiersRepository tiersRepo,
        CreditCardGroupsRepository groupsRepo,
        CreditCardRolesRepository rolesRepo,
        CreditRoleBlocksRepository blocksRepo,
        CreditBlockEntriesRepository entriesRepo)
    {
        _cardsRepo = cardsRepo ?? throw new ArgumentNullException(nameof(cardsRepo));
        _tiersRepo = tiersRepo ?? throw new ArgumentNullException(nameof(tiersRepo));
        _groupsRepo = groupsRepo ?? throw new ArgumentNullException(nameof(groupsRepo));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _blocksRepo = blocksRepo ?? throw new ArgumentNullException(nameof(blocksRepo));
        _entriesRepo = entriesRepo ?? throw new ArgumentNullException(nameof(entriesRepo));
    }

    /// <summary>
    /// 指定クレジット <paramref name="credit"/> を起点に、配下の全階層を DB から読み込んで
    /// 新しい <see cref="CreditDraftSession"/> を構築して返す。
    /// </summary>
    public async Task<CreditDraftSession> LoadAsync(Credit credit, CancellationToken ct = default)
    {
        if (credit is null) throw new ArgumentNullException(nameof(credit));

        // セッション本体を作る。Root の DraftCredit は credit_id = RealId、State = Unchanged。
        var session = new CreditDraftSession
        {
            Root = new DraftCredit
            {
                RealId = credit.CreditId,
                TempId = 0, // ルートは 0 を割り当て（負数払い出しは新規行のみ使う）
                State = DraftState.Unchanged,
                Entity = credit
            }
        };

        // ─── カード ───
        var cards = (await _cardsRepo.GetByCreditAsync(credit.CreditId, ct))
            .OrderBy(c => c.CardSeq).ToList();
        foreach (var card in cards)
        {
            var draftCard = new DraftCard
            {
                RealId = card.CardId,
                TempId = session.AllocateTempId(),
                State = DraftState.Unchanged,
                Entity = card,
                Parent = session.Root
            };
            session.Root.Cards.Add(draftCard);

            // ─── Tier ───
            var tiers = (await _tiersRepo.GetByCardAsync(card.CardId, ct))
                .OrderBy(t => t.TierNo).ToList();
            foreach (var tier in tiers)
            {
                var draftTier = new DraftTier
                {
                    RealId = tier.CardTierId,
                    TempId = session.AllocateTempId(),
                    State = DraftState.Unchanged,
                    Entity = tier,
                    Parent = draftCard
                };
                draftCard.Tiers.Add(draftTier);

                // ─── Group ───
                var groups = (await _groupsRepo.GetByTierAsync(tier.CardTierId, ct))
                    .OrderBy(g => g.GroupNo).ToList();
                foreach (var grp in groups)
                {
                    var draftGroup = new DraftGroup
                    {
                        RealId = grp.CardGroupId,
                        TempId = session.AllocateTempId(),
                        State = DraftState.Unchanged,
                        Entity = grp,
                        Parent = draftTier
                    };
                    draftTier.Groups.Add(draftGroup);

                    // ─── Role ───
                    var roles = (await _rolesRepo.GetByGroupAsync(grp.CardGroupId, ct))
                        .OrderBy(r => r.OrderInGroup).ToList();
                    foreach (var role in roles)
                    {
                        var draftRole = new DraftRole
                        {
                            RealId = role.CardRoleId,
                            TempId = session.AllocateTempId(),
                            State = DraftState.Unchanged,
                            Entity = role,
                            Parent = draftGroup
                        };
                        draftGroup.Roles.Add(draftRole);

                        // ─── Block ───
                        var blocks = (await _blocksRepo.GetByCardRoleAsync(role.CardRoleId, ct))
                            .OrderBy(b => b.BlockSeq).ToList();
                        foreach (var blk in blocks)
                        {
                            var draftBlock = new DraftBlock
                            {
                                RealId = blk.BlockId,
                                TempId = session.AllocateTempId(),
                                State = DraftState.Unchanged,
                                Entity = blk,
                                Parent = draftRole
                            };
                            draftRole.Blocks.Add(draftBlock);

                            // ─── Entry ───
                            // entries はブロック単位で取得（既定行/本放送限定行両方を含む）。
                            // 表示順は (is_broadcast_only ASC, entry_seq ASC) で並べておくと、
                            // ツリー描画時の並びが安定する。
                            var entries = (await _entriesRepo.GetByBlockAsync(blk.BlockId, ct))
                                .OrderBy(en => en.IsBroadcastOnly)
                                .ThenBy(en => en.EntrySeq)
                                .ToList();
                            foreach (var en in entries)
                            {
                                var draftEntry = new DraftEntry
                                {
                                    RealId = en.EntryId,
                                    TempId = session.AllocateTempId(),
                                    State = DraftState.Unchanged,
                                    Entity = en,
                                    Parent = draftBlock
                                };
                                draftBlock.Entries.Add(draftEntry);
                            }
                        }
                    }
                }
            }
        }

        return session;
    }
}
