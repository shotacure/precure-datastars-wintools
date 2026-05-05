using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms.Drafting;

/// <summary>
/// クレジット編集セッション（<see cref="CreditDraftSession"/>）を 1 トランザクション内で
/// DB に書き込む保存サービス（v1.2.0 工程 H-8 で導入）。
/// <para>
/// 既存 Repository（<c>InsertAsync</c> / <c>UpdateAsync</c> / <c>DeleteAsync</c>）は内部で個別に
/// トランザクションを張っているため、複数行を 1 トランザクションでまとめて書き込むには直接 SQL を
/// 発行する必要がある。本サービスはそのために設計された専用クラスで、Draft の状態フラグ
/// （Unchanged / Modified / Added / Deleted）に応じて適切な SQL を組み立てる。
/// </para>
/// <para>
/// 保存処理は 4 フェーズで構成される：
/// </para>
/// <list type="number">
///   <item><description><b>削除フェーズ</b>：DeletedXxx バケットに溜めた既存行を、深い階層から DELETE。
///     CASCADE があるため親階層の DELETE で子も消えるが、「親は残すが配下の一部を削除した」
///     ケースに対応するため明示的に処理する。</description></item>
///   <item><description><b>新規作成フェーズ</b>：Added 状態の Draft を浅い階層から INSERT。
///     親の RealId を FK 値として使い、自動採番された新 ID を Draft.RealId に書き戻して下位層に伝播。</description></item>
///   <item><description><b>更新フェーズ</b>：Modified 状態の Draft を UPDATE。</description></item>
///   <item><description><b>seq 整合性</b>：各層の seq 値（card_seq / block_seq / order_in_group / entry_seq /
///     tier_no / group_no）を 1, 2, 3, ... の連番に再採番する。
///     Added と Modified が混ざると UNIQUE 制約と一時衝突する可能性があるため、退避値経由の 2 段階更新で確定する。</description></item>
/// </list>
/// <para>
/// 全工程を単一の <see cref="MySqlTransaction"/> 内で実行し、いずれかが失敗すれば全体ロールバックする。
/// 成功時は Draft セッションの状態フラグをすべて Unchanged にリセットして Deleted バケットも空にする。
/// </para>
/// </summary>
internal sealed class CreditSaveService
{
    private readonly IConnectionFactory _factory;

    public CreditSaveService(IConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// セッションの全変更を 1 トランザクション内で DB に書き込む。
    /// 成功時はセッション内の状態フラグをリセットして「保存済み」状態に戻す。
    /// </summary>
    /// <param name="session">保存対象のクレジット編集セッション。</param>
    /// <param name="updatedBy">監査用ユーザー名（INSERT / UPDATE 時の created_by / updated_by に書き込む）。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task SaveAsync(CreditDraftSession session, string updatedBy, CancellationToken ct = default)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (session.Root is null) throw new InvalidOperationException("session.Root が null です。");

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // ─── フェーズ 1: 削除（深い階層から） ───
            // 既存行（RealId 値あり）のみを DELETE 対象とする。Added だった行が削除された場合は
            // バケットに入っていない（呼び出し元でリストから取り除いただけ）ので、ここでは扱わない。
            await DeleteRowsAsync(conn, tx, "credit_block_entries", "entry_id",
                session.DeletedEntries.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);
            await DeleteRowsAsync(conn, tx, "credit_role_blocks", "block_id",
                session.DeletedBlocks.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);
            await DeleteRowsAsync(conn, tx, "credit_card_roles", "card_role_id",
                session.DeletedRoles.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);
            await DeleteRowsAsync(conn, tx, "credit_card_groups", "card_group_id",
                session.DeletedGroups.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);
            await DeleteRowsAsync(conn, tx, "credit_card_tiers", "card_tier_id",
                session.DeletedTiers.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);
            await DeleteRowsAsync(conn, tx, "credit_cards", "card_id",
                session.DeletedCards.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);

            // ─── フェーズ 2: 新規作成（浅い階層から） ───
            // Added 状態の Draft を INSERT し、戻りの auto_increment ID を Draft.RealId に書き戻す。
            // 子層は親の RealId（既存なら DB 値、新規なら今書き戻された値）を FK 値として使う。

            // クレジット本体は編集セッションの中で新規作成されることはない（クレジットそのものは
            // 「+ 新規クレジット」ボタンで別途作成される）ので、Added 状態の Root は想定しない。
            // ただし Modified 状態の Root はあり得る（プロパティ編集）。

            foreach (var card in session.Root.Cards.Where(c => c.State == DraftState.Added))
            {
                card.Entity.CreditId = session.Root.RealId
                    ?? throw new InvalidOperationException("Root.RealId が null のまま Card を INSERT しようとしました。");
                card.Entity.CreatedBy ??= updatedBy;
                card.Entity.UpdatedBy = updatedBy;
                int newId = await InsertCardAsync(conn, tx, card.Entity, ct);
                card.RealId = newId;
                card.Entity.CardId = newId;
            }

            foreach (var card in session.Root.Cards.Where(c => c.State != DraftState.Deleted))
            {
                foreach (var tier in card.Tiers.Where(t => t.State == DraftState.Added))
                {
                    tier.Entity.CardId = card.RealId
                        ?? throw new InvalidOperationException("Card.RealId が null のまま Tier を INSERT しようとしました。");
                    tier.Entity.CreatedBy ??= updatedBy;
                    tier.Entity.UpdatedBy = updatedBy;
                    int newId = await InsertTierAsync(conn, tx, tier.Entity, ct);
                    tier.RealId = newId;
                    tier.Entity.CardTierId = newId;
                }
                foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
                {
                    foreach (var grp in tier.Groups.Where(g => g.State == DraftState.Added))
                    {
                        grp.Entity.CardTierId = tier.RealId
                            ?? throw new InvalidOperationException("Tier.RealId が null のまま Group を INSERT しようとしました。");
                        grp.Entity.CreatedBy ??= updatedBy;
                        grp.Entity.UpdatedBy = updatedBy;
                        int newId = await InsertGroupAsync(conn, tx, grp.Entity, ct);
                        grp.RealId = newId;
                        grp.Entity.CardGroupId = newId;
                    }
                    foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                    {
                        foreach (var role in grp.Roles.Where(r => r.State == DraftState.Added))
                        {
                            role.Entity.CardGroupId = grp.RealId
                                ?? throw new InvalidOperationException("Group.RealId が null のまま Role を INSERT しようとしました。");
                            role.Entity.CreatedBy ??= updatedBy;
                            role.Entity.UpdatedBy = updatedBy;
                            int newId = await InsertRoleAsync(conn, tx, role.Entity, ct);
                            role.RealId = newId;
                            role.Entity.CardRoleId = newId;
                        }
                        foreach (var role in grp.Roles.Where(r => r.State != DraftState.Deleted))
                        {
                            foreach (var blk in role.Blocks.Where(b => b.State == DraftState.Added))
                            {
                                blk.Entity.CardRoleId = role.RealId
                                    ?? throw new InvalidOperationException("Role.RealId が null のまま Block を INSERT しようとしました。");
                                blk.Entity.CreatedBy ??= updatedBy;
                                blk.Entity.UpdatedBy = updatedBy;
                                int newId = await InsertBlockAsync(conn, tx, blk.Entity, ct);
                                blk.RealId = newId;
                                blk.Entity.BlockId = newId;
                            }
                            foreach (var blk in role.Blocks.Where(b => b.State != DraftState.Deleted))
                            {
                                foreach (var en in blk.Entries.Where(e => e.State == DraftState.Added))
                                {
                                    en.Entity.BlockId = blk.RealId
                                        ?? throw new InvalidOperationException("Block.RealId が null のまま Entry を INSERT しようとしました。");
                                    en.Entity.CreatedBy ??= updatedBy;
                                    en.Entity.UpdatedBy = updatedBy;
                                    int newId = await InsertEntryAsync(conn, tx, en.Entity, ct);
                                    en.RealId = newId;
                                    en.Entity.EntryId = newId;
                                }
                            }
                        }
                    }
                }
            }

            // ─── フェーズ 3: 更新 ───
            // Modified 状態の Draft を UPDATE。Root（Credit）も Modified ならここで処理する。
            if (session.Root.State == DraftState.Modified)
            {
                session.Root.Entity.UpdatedBy = updatedBy;
                await UpdateCreditAsync(conn, tx, session.Root.Entity, ct);
            }
            foreach (var card in session.Root.Cards.Where(c => c.State == DraftState.Modified))
            {
                card.Entity.UpdatedBy = updatedBy;
                await UpdateCardAsync(conn, tx, card.Entity, ct);
            }
            foreach (var card in session.Root.Cards.Where(c => c.State != DraftState.Deleted))
            {
                foreach (var tier in card.Tiers.Where(t => t.State == DraftState.Modified))
                {
                    tier.Entity.UpdatedBy = updatedBy;
                    await UpdateTierAsync(conn, tx, tier.Entity, ct);
                }
                foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
                {
                    foreach (var grp in tier.Groups.Where(g => g.State == DraftState.Modified))
                    {
                        grp.Entity.UpdatedBy = updatedBy;
                        await UpdateGroupAsync(conn, tx, grp.Entity, ct);
                    }
                    foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                    {
                        foreach (var role in grp.Roles.Where(r => r.State == DraftState.Modified))
                        {
                            role.Entity.UpdatedBy = updatedBy;
                            await UpdateRoleAsync(conn, tx, role.Entity, ct);
                        }
                        foreach (var role in grp.Roles.Where(r => r.State != DraftState.Deleted))
                        {
                            foreach (var blk in role.Blocks.Where(b => b.State == DraftState.Modified))
                            {
                                blk.Entity.UpdatedBy = updatedBy;
                                await UpdateBlockAsync(conn, tx, blk.Entity, ct);
                            }
                            foreach (var blk in role.Blocks.Where(b => b.State != DraftState.Deleted))
                            {
                                foreach (var en in blk.Entries.Where(e => e.State == DraftState.Modified))
                                {
                                    en.Entity.UpdatedBy = updatedBy;
                                    await UpdateEntryAsync(conn, tx, en.Entity, ct);
                                }
                            }
                        }
                    }
                }
            }

            // ─── フェーズ 4: seq 整合性 ───
            // 各層の seq 値（card_seq / order_in_group / block_seq / entry_seq）を 1, 2, 3, ... に
            // 再採番する。tier_no と group_no は番号体系が固定運用なので再採番しない。
            // Added / Modified が混ざった保存では UNIQUE (parent_id, seq) との衝突を避けるため
            // 退避値 30000+ 経由の 2 段階更新を使う。
            await ResequenceAsync(conn, tx, session, ct);

            await tx.CommitAsync(ct).ConfigureAwait(false);

            // ─── 成功後：セッションの状態フラグをリセット ───
            ResetSessionState(session);
        }
        catch
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    // ════════════════════════════════════════════════════════════
    //   削除ヘルパ
    // ════════════════════════════════════════════════════════════

    /// <summary>指定テーブルから ID 列の値が渡された ID 群に一致する行を DELETE する。</summary>
    private static async Task DeleteRowsAsync(
        MySqlConnection conn, MySqlTransaction tx,
        string tableName, string idColumn, IEnumerable<int> ids,
        CancellationToken ct)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;
        // 1 件ずつ DELETE（件数が多くないので IN 句にせず素朴な方法で良い）
        foreach (var id in idList)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM `{tableName}` WHERE `{idColumn}` = @Id;",
                new { Id = id },
                transaction: tx, cancellationToken: ct));
        }
    }

    // ════════════════════════════════════════════════════════════
    //   INSERT ヘルパ（各層）
    // ════════════════════════════════════════════════════════════

    private static async Task<int> InsertCardAsync(MySqlConnection conn, MySqlTransaction tx, CreditCard c, CancellationToken ct)
    {
        // credit_cards テーブルには presentation 列は無い（presentation は credits 側の列）。
        const string sql = """
            INSERT INTO credit_cards (credit_id, card_seq, notes, created_by, updated_by)
            VALUES (@CreditId, @CardSeq, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, c, transaction: tx, cancellationToken: ct));
    }

    private static async Task<int> InsertTierAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardTier t, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO credit_card_tiers (card_id, tier_no, notes, created_by, updated_by)
            VALUES (@CardId, @TierNo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, t, transaction: tx, cancellationToken: ct));
    }

    private static async Task<int> InsertGroupAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardGroup g, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO credit_card_groups (card_tier_id, group_no, notes, created_by, updated_by)
            VALUES (@CardTierId, @GroupNo, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, g, transaction: tx, cancellationToken: ct));
    }

    private static async Task<int> InsertRoleAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardRole r, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO credit_card_roles (card_group_id, role_code, order_in_group, notes, created_by, updated_by)
            VALUES (@CardGroupId, @RoleCode, @OrderInGroup, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, r, transaction: tx, cancellationToken: ct));
    }

    private static async Task<int> InsertBlockAsync(MySqlConnection conn, MySqlTransaction tx, CreditRoleBlock b, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO credit_role_blocks (card_role_id, block_seq, col_count, leading_company_alias_id, notes, created_by, updated_by)
            VALUES (@CardRoleId, @BlockSeq, @ColCount, @LeadingCompanyAliasId, @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, b, transaction: tx, cancellationToken: ct));
    }

    private static async Task<int> InsertEntryAsync(MySqlConnection conn, MySqlTransaction tx, CreditBlockEntry e, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO credit_block_entries
              (block_id, is_broadcast_only, entry_seq, entry_kind,
               person_alias_id, character_alias_id, raw_character_text,
               company_alias_id, logo_id, raw_text,
               affiliation_company_alias_id, affiliation_text, parallel_with_entry_id,
               notes, created_by, updated_by)
            VALUES
              (@BlockId, @IsBroadcastOnly, @EntrySeq, @EntryKind,
               @PersonAliasId, @CharacterAliasId, @RawCharacterText,
               @CompanyAliasId, @LogoId, @RawText,
               @AffiliationCompanyAliasId, @AffiliationText, @ParallelWithEntryId,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, e, transaction: tx, cancellationToken: ct));
    }

    // ════════════════════════════════════════════════════════════
    //   UPDATE ヘルパ（各層）
    // ════════════════════════════════════════════════════════════

    private static async Task UpdateCreditAsync(MySqlConnection conn, MySqlTransaction tx, Credit c, CancellationToken ct)
    {
        const string sql = """
            UPDATE credits SET
              presentation = @Presentation,
              part_type = @PartType,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE credit_id = @CreditId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, c, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateCardAsync(MySqlConnection conn, MySqlTransaction tx, CreditCard c, CancellationToken ct)
    {
        const string sql = """
            UPDATE credit_cards SET
              card_seq = @CardSeq,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_id = @CardId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, c, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateTierAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardTier t, CancellationToken ct)
    {
        const string sql = """
            UPDATE credit_card_tiers SET
              tier_no = @TierNo,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_tier_id = @CardTierId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, t, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateGroupAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardGroup g, CancellationToken ct)
    {
        const string sql = """
            UPDATE credit_card_groups SET
              group_no = @GroupNo,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_group_id = @CardGroupId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, g, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateRoleAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardRole r, CancellationToken ct)
    {
        const string sql = """
            UPDATE credit_card_roles SET
              role_code = @RoleCode,
              order_in_group = @OrderInGroup,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_role_id = @CardRoleId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, r, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateBlockAsync(MySqlConnection conn, MySqlTransaction tx, CreditRoleBlock b, CancellationToken ct)
    {
        const string sql = """
            UPDATE credit_role_blocks SET
              block_seq = @BlockSeq,
              col_count = @ColCount,
              leading_company_alias_id = @LeadingCompanyAliasId,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE block_id = @BlockId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, b, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateEntryAsync(MySqlConnection conn, MySqlTransaction tx, CreditBlockEntry e, CancellationToken ct)
    {
        const string sql = """
            UPDATE credit_block_entries SET
              block_id = @BlockId,
              is_broadcast_only = @IsBroadcastOnly,
              entry_seq = @EntrySeq,
              entry_kind = @EntryKind,
              person_alias_id = @PersonAliasId,
              character_alias_id = @CharacterAliasId,
              raw_character_text = @RawCharacterText,
              company_alias_id = @CompanyAliasId,
              logo_id = @LogoId,
              raw_text = @RawText,
              affiliation_company_alias_id = @AffiliationCompanyAliasId,
              affiliation_text = @AffiliationText,
              parallel_with_entry_id = @ParallelWithEntryId,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE entry_id = @EntryId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, e, transaction: tx, cancellationToken: ct));
    }

    // ════════════════════════════════════════════════════════════
    //   seq 再採番（フェーズ 4）
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 各層の seq 値を 1, 2, 3, ... に再採番する。退避値 30000+ 経由の 2 段階更新で
    /// UNIQUE (parent_id, seq) との衝突を回避する。
    /// </summary>
    private static async Task ResequenceAsync(
        MySqlConnection conn, MySqlTransaction tx,
        CreditDraftSession session, CancellationToken ct)
    {
        // ─── card_seq（同 credit_id 内）───
        var cards = session.Root.Cards.Where(c => c.State != DraftState.Deleted)
            .OrderBy(c => c.Entity.CardSeq).ToList();
        await Resequence2PhaseAsync(conn, tx,
            cards.Select(c => (c.RealId!.Value, (int)c.Entity.CardSeq)).ToList(),
            "credit_cards", "card_id", "card_seq", ct);
        // メモリ側にも反映（次の保存時に正しい値が UPDATE 文に乗るように）
        for (int i = 0; i < cards.Count; i++) cards[i].Entity.CardSeq = (byte)(i + 1);

        foreach (var card in cards)
        {
            // tier_no は固定運用（1=上段、2=下段）なので再採番しない。
            // group_no も同じく固定運用。

            foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
            {
                foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                {
                    // ─── order_in_group（同 card_group_id 内） ───
                    var roles = grp.Roles.Where(r => r.State != DraftState.Deleted)
                        .OrderBy(r => r.Entity.OrderInGroup).ToList();
                    await Resequence2PhaseAsync(conn, tx,
                        roles.Select(r => (r.RealId!.Value, (int)r.Entity.OrderInGroup)).ToList(),
                        "credit_card_roles", "card_role_id", "order_in_group", ct);
                    for (int i = 0; i < roles.Count; i++) roles[i].Entity.OrderInGroup = (byte)(i + 1);

                    foreach (var role in roles)
                    {
                        // ─── block_seq（同 card_role_id 内） ───
                        var blocks = role.Blocks.Where(b => b.State != DraftState.Deleted)
                            .OrderBy(b => b.Entity.BlockSeq).ToList();
                        await Resequence2PhaseAsync(conn, tx,
                            blocks.Select(b => (b.RealId!.Value, (int)b.Entity.BlockSeq)).ToList(),
                            "credit_role_blocks", "block_id", "block_seq", ct);
                        for (int i = 0; i < blocks.Count; i++) blocks[i].Entity.BlockSeq = (byte)(i + 1);

                        foreach (var blk in blocks)
                        {
                            // ─── entry_seq（同 block_id × is_broadcast_only 内） ───
                            // is_broadcast_only=0 と =1 はそれぞれ別グループとして再採番する。
                            foreach (var flag in new[] { false, true })
                            {
                                var entries = blk.Entries
                                    .Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag)
                                    .OrderBy(e => e.Entity.EntrySeq).ToList();
                                await Resequence2PhaseAsync(conn, tx,
                                    entries.Select(e => (e.RealId!.Value, (int)e.Entity.EntrySeq)).ToList(),
                                    "credit_block_entries", "entry_id", "entry_seq", ct);
                                for (int i = 0; i < entries.Count; i++) entries[i].Entity.EntrySeq = (ushort)(i + 1);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 渡された行を 1, 2, 3, ... の連番に再採番する 2 段階更新ヘルパ。
    /// 1 段階目で全行を 30000+ の退避値に逃がし、2 段階目で本来の値に書き戻す。
    /// 既に 1, 2, 3, ... 連番になっている場合でも 2 段階の UPDATE を実行する（実害なし、
    /// 同じ値で UPDATE しても DB は無視するか同値で書き戻すだけ）。
    /// </summary>
    /// <param name="rowsInDesiredOrder">並び順通りに並べた (RealId, 現在の seq 値) のリスト。</param>
    private static async Task Resequence2PhaseAsync(
        MySqlConnection conn, MySqlTransaction tx,
        List<(int RealId, int CurrentSeq)> rowsInDesiredOrder,
        string tableName, string idColumn, string seqColumn,
        CancellationToken ct)
    {
        if (rowsInDesiredOrder.Count == 0) return;

        // 既に 1, 2, 3, ... の連番になっているか確認（早期 return で UNIQUE 衝突リスクを最小化）
        bool alreadyOk = true;
        for (int i = 0; i < rowsInDesiredOrder.Count; i++)
        {
            if (rowsInDesiredOrder[i].CurrentSeq != i + 1) { alreadyOk = false; break; }
        }
        if (alreadyOk) return;

        // 1 段階目：退避値（30000, 30001, ...）で逃がす
        for (int i = 0; i < rowsInDesiredOrder.Count; i++)
        {
            int tempVal = 30000 + i;
            await conn.ExecuteAsync(new CommandDefinition(
                $"UPDATE `{tableName}` SET `{seqColumn}` = @Val WHERE `{idColumn}` = @Id;",
                new { Val = tempVal, Id = rowsInDesiredOrder[i].RealId },
                transaction: tx, cancellationToken: ct));
        }
        // 2 段階目：本来の連番値で確定
        for (int i = 0; i < rowsInDesiredOrder.Count; i++)
        {
            int newVal = i + 1;
            await conn.ExecuteAsync(new CommandDefinition(
                $"UPDATE `{tableName}` SET `{seqColumn}` = @Val WHERE `{idColumn}` = @Id;",
                new { Val = newVal, Id = rowsInDesiredOrder[i].RealId },
                transaction: tx, cancellationToken: ct));
        }
    }

    // ════════════════════════════════════════════════════════════
    //   状態リセット（保存成功後）
    // ════════════════════════════════════════════════════════════

    /// <summary>保存成功後にセッションの状態フラグをすべて Unchanged にリセットして Deleted バケットを空にする。</summary>
    private static void ResetSessionState(CreditDraftSession session)
    {
        session.Root.State = DraftState.Unchanged;
        foreach (var card in session.Root.Cards.Where(c => c.State != DraftState.Deleted))
        {
            card.State = DraftState.Unchanged;
            foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
            {
                tier.State = DraftState.Unchanged;
                foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                {
                    grp.State = DraftState.Unchanged;
                    foreach (var role in grp.Roles.Where(r => r.State != DraftState.Deleted))
                    {
                        role.State = DraftState.Unchanged;
                        foreach (var blk in role.Blocks.Where(b => b.State != DraftState.Deleted))
                        {
                            blk.State = DraftState.Unchanged;
                            foreach (var en in blk.Entries.Where(e => e.State != DraftState.Deleted))
                            {
                                en.State = DraftState.Unchanged;
                            }
                        }
                    }
                }
            }
        }
        // Deleted バケットの全削除（すでに DB から消えたので Draft も捨てる）
        session.DeletedEntries.Clear();
        session.DeletedBlocks.Clear();
        session.DeletedRoles.Clear();
        session.DeletedGroups.Clear();
        session.DeletedTiers.Clear();
        session.DeletedCards.Clear();
    }
}
