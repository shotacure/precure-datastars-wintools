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

    /// <summary>
    /// コピー元クレジットを DB から読み込み、コピー先用に「すべて Added 状態の Draft セッション」を組み立てる
    /// （v1.2.0 工程 H-8 ターン 7「クレジット話数コピー」で導入）。
    /// <para>
    /// コピー先のクレジット本体（<see cref="DraftCredit"/>）は <c>RealId = null, State = Added</c> として作成し、
    /// 引数で指定された <paramref name="destEntity"/>（scope_kind / series_id / episode_id / credit_kind /
    /// part_type / presentation / notes が設定済みの新規 Credit インスタンス）を <see cref="DraftBase{T}.Entity"/> に格納する。
    /// </para>
    /// <para>
    /// 配下の Card / Tier / Group / Role / Block / Entry はコピー元の構造とエントリ内容をそのまま複製し、
    /// 全て <c>RealId = null, State = Added, TempId = 新規払い出し</c> として組み上げる。Entity 側の値も
    /// すべて新インスタンスに deep clone するので、コピー元 Draft への副作用は無い。
    /// </para>
    /// <para>
    /// 保存処理（<see cref="CreditSaveService"/>）はコピー先セッションの Root.State == Added を見て
    /// クレジット本体を INSERT し、配下も Added として一括 INSERT する。
    /// </para>
    /// </summary>
    /// <param name="srcCredit">コピー元クレジット（DB 上の既存行）。</param>
    /// <param name="destEntity">コピー先クレジット本体の値が設定された新規 <see cref="Credit"/> インスタンス。
    /// scope_kind / series_id / episode_id / credit_kind / part_type / presentation / notes / created_by /
    /// updated_by が設定されていること。CreditId は 0（保存時に採番される）。</param>
    public async Task<CreditDraftSession> CloneForCopyAsync(Credit srcCredit, Credit destEntity, CancellationToken ct = default)
    {
        if (srcCredit is null) throw new ArgumentNullException(nameof(srcCredit));
        if (destEntity is null) throw new ArgumentNullException(nameof(destEntity));

        // ① まずコピー元の DB 内容を通常通りロードする（既存の LoadAsync を流用）。
        var srcSession = await LoadAsync(srcCredit, ct).ConfigureAwait(false);

        // ② コピー先用の新セッションを構築。Root は Added、Entity は呼び出し元で設定済みの destEntity。
        var destSession = new CreditDraftSession
        {
            Root = new DraftCredit
            {
                RealId = null,
                TempId = 0, // ルートは 0 を割り当てる（CreditDraftLoader.LoadAsync と同じ規則）
                State = DraftState.Added,
                Entity = destEntity
            }
        };

        // ③ srcSession の階層を全部 Added で複製していく。Entity も new でコピー（参照共有しない）。
        foreach (var srcCard in srcSession.Root.Cards)
        {
            var destCard = new DraftCard
            {
                RealId = null,
                TempId = destSession.AllocateTempId(),
                State = DraftState.Added,
                Entity = CloneCardEntity(srcCard.Entity),
                Parent = destSession.Root
            };
            destSession.Root.Cards.Add(destCard);

            foreach (var srcTier in srcCard.Tiers)
            {
                var destTier = new DraftTier
                {
                    RealId = null,
                    TempId = destSession.AllocateTempId(),
                    State = DraftState.Added,
                    Entity = CloneTierEntity(srcTier.Entity),
                    Parent = destCard
                };
                destCard.Tiers.Add(destTier);

                foreach (var srcGroup in srcTier.Groups)
                {
                    var destGroup = new DraftGroup
                    {
                        RealId = null,
                        TempId = destSession.AllocateTempId(),
                        State = DraftState.Added,
                        Entity = CloneGroupEntity(srcGroup.Entity),
                        Parent = destTier
                    };
                    destTier.Groups.Add(destGroup);

                    foreach (var srcRole in srcGroup.Roles)
                    {
                        var destRole = new DraftRole
                        {
                            RealId = null,
                            TempId = destSession.AllocateTempId(),
                            State = DraftState.Added,
                            Entity = CloneRoleEntity(srcRole.Entity),
                            Parent = destGroup
                        };
                        destGroup.Roles.Add(destRole);

                        foreach (var srcBlock in srcRole.Blocks)
                        {
                            var destBlock = new DraftBlock
                            {
                                RealId = null,
                                TempId = destSession.AllocateTempId(),
                                State = DraftState.Added,
                                Entity = CloneBlockEntity(srcBlock.Entity),
                                Parent = destRole
                            };
                            destRole.Blocks.Add(destBlock);

                            foreach (var srcEntry in srcBlock.Entries)
                            {
                                var destEntry = new DraftEntry
                                {
                                    RealId = null,
                                    TempId = destSession.AllocateTempId(),
                                    State = DraftState.Added,
                                    Entity = CloneEntryEntity(srcEntry.Entity),
                                    Parent = destBlock
                                };
                                destBlock.Entries.Add(destEntry);
                            }
                        }
                    }
                }
            }
        }

        return destSession;
    }

    // ─── Entity の deep clone ヘルパ群 ───
    // 元インスタンスを変更しないため、保存対象の各 Entity を新規インスタンスとして作り直す。
    // 主キー（CardId / CardTierId / CardGroupId / CardRoleId / BlockId / EntryId）と FK（CreditId / CardId 等）は
    // 0 にしておき、保存時に CreditSaveService が親の RealId / 自分の新採番 ID で上書きする。

    private static CreditCard CloneCardEntity(CreditCard s) => new()
    {
        CardId = 0,
        CreditId = 0,
        CardSeq = s.CardSeq,
        Notes = s.Notes,
        CreatedBy = s.CreatedBy,
        UpdatedBy = s.UpdatedBy
    };

    private static CreditCardTier CloneTierEntity(CreditCardTier s) => new()
    {
        CardTierId = 0,
        CardId = 0,
        TierNo = s.TierNo,
        Notes = s.Notes,
        CreatedBy = s.CreatedBy,
        UpdatedBy = s.UpdatedBy
    };

    private static CreditCardGroup CloneGroupEntity(CreditCardGroup s) => new()
    {
        CardGroupId = 0,
        CardTierId = 0,
        GroupNo = s.GroupNo,
        Notes = s.Notes,
        CreatedBy = s.CreatedBy,
        UpdatedBy = s.UpdatedBy
    };

    private static CreditCardRole CloneRoleEntity(CreditCardRole s) => new()
    {
        CardRoleId = 0,
        CardGroupId = 0,
        RoleCode = s.RoleCode,
        OrderInGroup = s.OrderInGroup,
        Notes = s.Notes,
        CreatedBy = s.CreatedBy,
        UpdatedBy = s.UpdatedBy
    };

    private static CreditRoleBlock CloneBlockEntity(CreditRoleBlock s) => new()
    {
        BlockId = 0,
        CardRoleId = 0,
        BlockSeq = s.BlockSeq,
        ColCount = s.ColCount,
        LeadingCompanyAliasId = s.LeadingCompanyAliasId,
        Notes = s.Notes,
        CreatedBy = s.CreatedBy,
        UpdatedBy = s.UpdatedBy
    };

    private static CreditBlockEntry CloneEntryEntity(CreditBlockEntry s) => new()
    {
        EntryId = 0,
        BlockId = 0,
        IsBroadcastOnly = s.IsBroadcastOnly,
        EntrySeq = s.EntrySeq,
        EntryKind = s.EntryKind,
        PersonAliasId = s.PersonAliasId,
        CharacterAliasId = s.CharacterAliasId,
        RawCharacterText = s.RawCharacterText,
        CompanyAliasId = s.CompanyAliasId,
        LogoId = s.LogoId,
        RawText = s.RawText,
        AffiliationCompanyAliasId = s.AffiliationCompanyAliasId,
        AffiliationText = s.AffiliationText,
        Notes = s.Notes,
        CreatedBy = s.CreatedBy,
        UpdatedBy = s.UpdatedBy
    };
}
