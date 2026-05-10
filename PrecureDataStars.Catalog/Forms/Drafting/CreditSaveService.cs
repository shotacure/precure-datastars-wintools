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
/// 保存処理は 5 フェーズで構成される（v1.3.0 で削除フェーズを 1A / 1B に分割し、1B を更新後に移動）：
/// </para>
/// <list type="number">
///   <item><description><b>削除フェーズ 1A（エントリ）</b>：DeletedEntries バケットの既存 entry 行を DELETE。
///     エントリは最末端の階層なので、ここで先行削除しても他テーブルに副作用を出さない。
///     「親 Block は残すが配下エントリの一部を削除した」ケースを正しく扱うために必要。</description></item>
///   <item><description><b>新規作成フェーズ</b>：Added 状態の Draft を浅い階層から INSERT。
///     親の RealId を FK 値として使い、自動採番された新 ID を Draft.RealId に書き戻して下位層に伝播。</description></item>
///   <item><description><b>更新フェーズ</b>：Modified 状態の Draft を UPDATE。
///     DnD 移動でメモリ上だけ親 FK が付け替わっていた行も、ここで初めて DB の親 FK 列が新親に書き換わる。</description></item>
///   <item><description><b>削除フェーズ 1B（ブロック以上の親階層）</b>：DeletedBlocks / DeletedRoles /
///     DeletedGroups / DeletedTiers / DeletedCards の既存行を深い階層から DELETE。
///     更新フェーズで子の親 FK 列が DB 上も新親に書き換わった後で実施することにより、
///     旧親 DELETE 時の CASCADE が「DnD で既に縁を切った子」を巻き添え削除する事故を回避する
///     （v1.3.0 で発覚した「DnD でエントリを別ブロックに移動 → 旧ブロック削除を 1 回でまとめて保存
///     したときに、移動済みエントリが旧ブロックの CASCADE で消える」バグの恒久対策）。
///     真のオーファン（DnD で動かしていない素直な配下）は期待通り CASCADE で連鎖削除される。</description></item>
///   <item><description><b>seq 整合性</b>：各層の seq 値（card_seq / block_seq / order_in_group / entry_seq /
///     tier_no / group_no）を 1, 2, 3, ... の連番に再採番する。
///     Added と Modified が混ざると UNIQUE 制約と一時衝突する可能性があるため、退避値経由の 2 段階更新で確定する。
///     1B が先に走っているので、Deleted 行が DB に残ったまま再採番値と衝突する余地は無い。</description></item>
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
            // ─── フェーズ 1A: エントリ DELETE（深い階層から、最も末端） ───
            // entries の DELETE は他階層に依存しないので、ここで先行して実施しても副作用は無い。
            // ブロック以上の親階層の DELETE は Phase 1B として更新フェーズの後ろに分離して走らせる
            // （v1.3.0 修正：DnD で別ブロックへ移動したエントリが、旧ブロック DELETE 時の CASCADE で
            //  巻き添え削除される事故を回避するため。クラス docstring 参照）。
            // 既存行（RealId 値あり）のみを DELETE 対象とする。Added だった行が削除された場合は
            // バケットに入っていない（呼び出し元でリストから取り除いただけ）ので、ここでは扱わない。
            await DeleteRowsAsync(conn, tx, "credit_block_entries", "entry_id",
                session.DeletedEntries.Where(d => d.RealId.HasValue).Select(d => d.RealId!.Value), ct);

            // ─── フェーズ 2: 新規作成（浅い階層から） ───
            // Added 状態の Draft を INSERT し、戻りの auto_increment ID を Draft.RealId に書き戻す。
            // 子層は親の RealId（既存なら DB 値、新規なら今書き戻された値）を FK 値として使う。

            // v1.2.0 工程 H-8 ターン 7：Root（クレジット本体）が Added の場合は credits テーブルに INSERT して
            // credit_id を採番させ、Root.RealId に書き戻す。これは「クレジット話数コピー」機能で
            // コピー先クレジットを丸ごと新規作成するときに使う。通常の編集セッションでは Root は
            // 既存クレジットを開いたものなので Added にはならない。
            if (session.Root.State == DraftState.Added)
            {
                session.Root.Entity.CreatedBy ??= updatedBy;
                session.Root.Entity.UpdatedBy = updatedBy;
                int newCreditId = await InsertCreditAsync(conn, tx, session.Root.Entity, ct);
                session.Root.RealId = newCreditId;
                session.Root.Entity.CreditId = newCreditId;
            }

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

                                    // v1.2.2: A/B 併記（parallel_with_entry_id）の解決（INSERT 前）。
                                    // 一括入力で行頭 "& " プレフィクスが付いていたエントリには
                                    // DraftEntry.RequestParallelWithPrevious=true が立っているので、
                                    // 同一ブロック内の「直前の Deleted でないエントリ」の RealId を引き当てて
                                    // Entity.ParallelWithEntryId にセットする。
                                    // 直前エントリも Added の場合、本ループ内で既に INSERT 済み（== RealId 確定済み）なので
                                    // 単純に前方探索すればよい。
                                    if (en.RequestParallelWithPrevious)
                                    {
                                        int? prevRealId = FindPreviousLiveEntryRealId(blk, en);
                                        en.Entity.ParallelWithEntryId = prevRealId;
                                        // prevRealId が null の場合（ブロック先頭で誤マーカー、または直前が同タイミング INSERT 失敗など）
                                        // は ParallelWithEntryId も null のまま。CreditBulkApplyService 側で
                                        // 「ブロック先頭の & は無効」を InfoMessage で警告済み。
                                    }

                                    int newId = await InsertEntryAsync(conn, tx, en.Entity, ct);
                                    en.RealId = newId;
                                    en.Entity.EntryId = newId;
                                }
                            }
                        }
                    }
                }
            }

            // ─── フェーズ 2.7: A/B 併記の救済処理（v1.2.2 追加） ───
            // Phase 2 の Added Entry INSERT 時には RequestParallelWithPrevious が反映済みだが、
            // 万一 Modified / Unchanged 状態のエントリにフラグが残っていた場合（プログラマ向けの保険）、
            // ここで Entity.ParallelWithEntryId を直前エントリの RealId に揃え、Unchanged だった場合は
            // MarkModified() して Phase 3 の UPDATE で書き込まれるようにする。
            // 通常運用（一括入力ペーストや右クリック ReplaceScope）では本ループに該当エントリは無い。
            foreach (var card in session.Root.Cards.Where(c => c.State != DraftState.Deleted))
            {
                foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
                {
                    foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                    {
                        foreach (var role in grp.Roles.Where(r => r.State != DraftState.Deleted))
                        {
                            foreach (var blk in role.Blocks.Where(b => b.State != DraftState.Deleted))
                            {
                                foreach (var en in blk.Entries)
                                {
                                    if (en.State == DraftState.Deleted) continue;
                                    if (en.State == DraftState.Added) continue; // Phase 2 で処理済み
                                    if (!en.RequestParallelWithPrevious) continue;

                                    int? prevRealId = FindPreviousLiveEntryRealId(blk, en);
                                    if (prevRealId is int p && en.Entity.ParallelWithEntryId != p)
                                    {
                                        en.Entity.ParallelWithEntryId = p;
                                        if (en.State == DraftState.Unchanged) en.MarkModified();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // ─── フェーズ 2.5: FK 再同期（v1.2.1 修正：DnD 移動消失バグ対策） ───
            // DnD で Draft の親リンクを別親に付け替えた場合、付け替え時点では新親が Added で
            // RealId が未確定なことがあるため、Entity.<親FK> 列が古い値のまま残ることがある。
            // フェーズ 2 で全階層の Added に RealId が確定した今、全 Draft の Entity.<親FK> を
            // 親の RealId（既存なら既存値、新規なら採番直後の値）で強制再上書きする。
            // これと併せて UPDATE 系 SQL の SET 句に親 FK 列を含めることで、Modified 状態の
            // Draft の親変更が確実に DB に反映されるようになる。
            // 親 FK が実際に変わった Draft のみ Modified に格上げし、無駄な UPDATE を抑止する。
            int rootRealId = session.Root.RealId
                ?? throw new InvalidOperationException("Phase 2.5: Root.RealId 未確定。");
            foreach (var card in session.Root.Cards.Where(c => c.State != DraftState.Deleted))
            {
                int cardRealId = card.RealId
                    ?? throw new InvalidOperationException("Phase 2.5: Card.RealId 未確定。");
                if (card.Entity.CreditId != rootRealId)
                {
                    card.Entity.CreditId = rootRealId;
                    if (card.State == DraftState.Unchanged) card.MarkModified();
                }
                card.Entity.CardId = cardRealId;
                foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
                {
                    int tierRealId = tier.RealId
                        ?? throw new InvalidOperationException("Phase 2.5: Tier.RealId 未確定。");
                    if (tier.Entity.CardId != cardRealId)
                    {
                        tier.Entity.CardId = cardRealId;
                        if (tier.State == DraftState.Unchanged) tier.MarkModified();
                    }
                    tier.Entity.CardTierId = tierRealId;
                    foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                    {
                        int grpRealId = grp.RealId
                            ?? throw new InvalidOperationException("Phase 2.5: Group.RealId 未確定。");
                        if (grp.Entity.CardTierId != tierRealId)
                        {
                            grp.Entity.CardTierId = tierRealId;
                            if (grp.State == DraftState.Unchanged) grp.MarkModified();
                        }
                        grp.Entity.CardGroupId = grpRealId;
                        foreach (var role in grp.Roles.Where(r => r.State != DraftState.Deleted))
                        {
                            int roleRealId = role.RealId
                                ?? throw new InvalidOperationException("Phase 2.5: Role.RealId 未確定。");
                            if (role.Entity.CardGroupId != grpRealId)
                            {
                                role.Entity.CardGroupId = grpRealId;
                                if (role.State == DraftState.Unchanged) role.MarkModified();
                            }
                            role.Entity.CardRoleId = roleRealId;
                            foreach (var blk in role.Blocks.Where(b => b.State != DraftState.Deleted))
                            {
                                int blkRealId = blk.RealId
                                    ?? throw new InvalidOperationException("Phase 2.5: Block.RealId 未確定。");
                                if (blk.Entity.CardRoleId != roleRealId)
                                {
                                    blk.Entity.CardRoleId = roleRealId;
                                    if (blk.State == DraftState.Unchanged) blk.MarkModified();
                                }
                                blk.Entity.BlockId = blkRealId;
                                foreach (var en in blk.Entries.Where(e => e.State != DraftState.Deleted))
                                {
                                    int enRealId = en.RealId
                                        ?? throw new InvalidOperationException("Phase 2.5: Entry.RealId 未確定。");
                                    if (en.Entity.BlockId != blkRealId)
                                    {
                                        en.Entity.BlockId = blkRealId;
                                        if (en.State == DraftState.Unchanged) en.MarkModified();
                                    }
                                    en.Entity.EntryId = enRealId;
                                }
                            }
                        }
                    }
                }
            }

            // ─── フェーズ 2.6: seq 列の退避（v1.2.1 修正、v1.2.2 で型範囲対応） ───
            // フェーズ 2.5 で親 FK が DnD 移動先に確定したが、Phase 3 で UPDATE を発行する際に
            // UNIQUE (parent_id, seq) 制約と衝突する可能性がある（同じ親グループ内で複数の
            // Modified 行が同時に同じ seq 値を取り合うため）。例：
            //   - Group X: A(order=1), B(order=2)、Group Y: C(order=1)
            //   - DnD で A を Y の先頭に移動
            //   - Memory: Group X: B(order=1,mod) / Group Y: A(order=1,mod,Y), C(order=2,mod)
            //   - Phase 3 で B が先に UPDATE される: SET (X, 1) → 既存 A=(X,1) と衝突
            //
            // 対策: Phase 3 の前に、Modified 行の seq 列を全て大きな退避値に一括 UPDATE しておく。
            // Phase 4 の Resequence2PhaseAsync が既に「退避値経由で本来の連番に書き戻す」設計に
            // なっているので、Phase 3 では seq 列を SET 句から除外しても結果は変わらない
            // （seq 列の最終値は Phase 4 で確定する）。
            //
            // v1.2.2 修正：列の型に応じた退避値レンジを使い分ける。
            //   credit_cards.card_seq             : tinyint  unsigned (0-255)  → 退避値 200+i
            //   credit_card_tiers.tier_no         : tinyint  unsigned (0-255)  → ※Phase 2.6 では未対応（Tier の DnD 移動は仕様外）
            //   credit_card_groups.group_no       : tinyint  unsigned (0-255)  → ※Phase 2.6 では未対応（Group の DnD 移動は仕様外）
            //   credit_card_roles.order_in_group  : tinyint  unsigned (0-255)  → 退避値 200+i
            //   credit_role_blocks.block_seq      : tinyint  unsigned (0-255)  → 退避値 200+i
            //   credit_block_entries.entry_seq    : smallint unsigned (0-65535)→ 退避値 30000+i
            //
            // 旧実装（v1.2.1）では全テーブル横断で 30000+ を使っていたが、tinyint 列に 30000 を
            // INSERT/UPDATE すると MySQL が「Out of range value for column 'card_seq' at row 1」を
            // 返してトランザクション全体が失敗していた（話数コピー後に編集を加えてから保存する経路で発火）。
            //
            // tinyint 系は 56 件（200..255）までしか退避できないため、それを超える場合は
            // 明示的に InvalidOperationException で打ち切る（メッセージで状況を説明）。
            // 実用上、1 トランザクションでこれだけの Modified 行が同一テーブル内に出ることは想定していない。
            int tinyEscape = 200;   // tinyint unsigned (card_seq / order_in_group / block_seq) 用カウンタ
            int entryEscape = 30000; // smallint unsigned (entry_seq) 用カウンタ

            // テーブル × 親 FK 単位で UNIQUE スコープが分かれているため、テーブル間でカウンタを
            // 共有しても「同じ親内で衝突」は起きないが、グローバルにインクリメントすることで
            // 「異なるテーブル間でも同じ退避値を使わない」という保守上の安全側に倒している。
            // 例えば Card と Role が同時に Modified の場合でも、それぞれが別々の値（200, 201, ...）を
            // 取るため、デバッグ時の混乱を最小化できる。

            // tinyint 退避値が 255 を超える場合の早期検知ヘルパ。
            // Phase 2.6 内部のループ内で都度呼び、超えていたら明示的に例外を投げる。
            void EnsureTinyEscapeRoom(string columnLabel)
            {
                if (tinyEscape > 255)
                {
                    throw new InvalidOperationException(
                        $"Phase 2.6: {columnLabel} の退避値が tinyint unsigned 上限 (255) を超えました。"
                        + " 同一トランザクションで Modified 行が 56 件を超えています。"
                        + " 1 回の保存で扱う変更行数を分割してください。");
                }
            }

            foreach (var card in session.Root.Cards.Where(c => c.State == DraftState.Modified))
            {
                EnsureTinyEscapeRoom("card_seq");
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE credit_cards SET card_seq = @Val WHERE card_id = @Id;",
                    new { Val = tinyEscape++, Id = card.RealId!.Value },
                    transaction: tx, cancellationToken: ct));
            }
            foreach (var card in session.Root.Cards.Where(c => c.State != DraftState.Deleted))
            {
                foreach (var tier in card.Tiers.Where(t => t.State != DraftState.Deleted))
                {
                    foreach (var grp in tier.Groups.Where(g => g.State != DraftState.Deleted))
                    {
                        foreach (var role in grp.Roles.Where(r => r.State == DraftState.Modified))
                        {
                            EnsureTinyEscapeRoom("order_in_group");
                            await conn.ExecuteAsync(new CommandDefinition(
                                "UPDATE credit_card_roles SET order_in_group = @Val WHERE card_role_id = @Id;",
                                new { Val = tinyEscape++, Id = role.RealId!.Value },
                                transaction: tx, cancellationToken: ct));
                        }
                        foreach (var role in grp.Roles.Where(r => r.State != DraftState.Deleted))
                        {
                            foreach (var blk in role.Blocks.Where(b => b.State == DraftState.Modified))
                            {
                                EnsureTinyEscapeRoom("block_seq");
                                await conn.ExecuteAsync(new CommandDefinition(
                                    "UPDATE credit_role_blocks SET block_seq = @Val WHERE block_id = @Id;",
                                    new { Val = tinyEscape++, Id = blk.RealId!.Value },
                                    transaction: tx, cancellationToken: ct));
                            }
                            foreach (var blk in role.Blocks.Where(b => b.State != DraftState.Deleted))
                            {
                                foreach (var en in blk.Entries.Where(e => e.State == DraftState.Modified))
                                {
                                    // entry_seq は smallint unsigned なので 30000+i で安全（最大 65535 まで余裕）。
                                    await conn.ExecuteAsync(new CommandDefinition(
                                        "UPDATE credit_block_entries SET entry_seq = @Val WHERE entry_id = @Id;",
                                        new { Val = entryEscape++, Id = en.RealId!.Value },
                                        transaction: tx, cancellationToken: ct));
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

            // ─── フェーズ 1B: ブロック以上の親階層 DELETE（v1.3.0 で更新フェーズ後に移動） ───
            // ここに到達した時点で、DnD 移動した Block / Role / Group / Tier / Card 配下の生存行は
            // Phase 3 の UPDATE で親 FK 列が DB 上も新親に書き換わっている。そのため、これから
            // 旧親を DELETE しても CASCADE が辿る先には「DnD で既に縁を切った子」は居らず、
            // 巻き添え削除されない。
            //
            // 一方、旧親の配下にそのまま残っていた行（DnD で動かしていない真のオーファン）は
            // 親 FK が旧親のままなので、CASCADE で期待通り連鎖削除される。
            // 「親は残すが配下の一部を削除した」ケースに対応するため、深い階層から順に明示的に処理する。
            // 既存行（RealId 値あり）のみを DELETE 対象とする。Added だった行が削除された場合は
            // バケットに入っていない（呼び出し元でリストから取り除いただけ）ので、ここでは扱わない。
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

            // ─── フェーズ 4: seq 整合性 ───
            // 各層の seq 値（card_seq / order_in_group / block_seq / entry_seq）を 1, 2, 3, ... に
            // 再採番する。tier_no と group_no は番号体系が固定運用なので再採番しない。
            // Added / Modified が混ざった保存では UNIQUE (parent_id, seq) との衝突を避けるため
            // 退避値 30000+ 経由の 2 段階更新を使う。
            // Phase 1B でブロック以上の Deleted 行はすべて DB から消えているため、
            // 「Modified 行を本来の連番値に書き戻す」段で Deleted 行の seq 値と衝突する余地は無い。
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
    //   v1.2.2 追加：A/B 併記解決ヘルパ
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// 指定ブロック内で <paramref name="target"/> エントリの直前に位置する「Deleted でないエントリ」の
    /// <see cref="DraftBase.RealId"/> を返す（v1.2.2 追加）。
    /// <para>
    /// A/B 併記（<c>parallel_with_entry_id</c>）の解決時に、Phase 2 の INSERT 直前または
    /// Phase 2.7 の救済処理から呼ばれる。直前エントリが見つからない場合（target がブロックの
    /// 先頭、または直前が全て Deleted）は null を返す。
    /// </para>
    /// <para>
    /// 直前エントリが Phase 2 で同タイミング INSERT 済みなら RealId は確定済み、
    /// Modified / Unchanged なら元から RealId 値あり。Added で未 INSERT のままここに来ることは
    /// ループ順序的にあり得ないが、防御的に null を返す（呼び出し側で ParallelWithEntryId は null になる）。
    /// </para>
    /// </summary>
    /// <param name="block">エントリの所属ブロック。<see cref="DraftBlock.Entries"/> の順序が現在の意図順。</param>
    /// <param name="target">直前エントリを探したい対象エントリ。</param>
    /// <returns>直前の Deleted でないエントリの RealId、または該当なしで null。</returns>
    private static int? FindPreviousLiveEntryRealId(DraftBlock block, DraftEntry target)
    {
        // List 内の target の位置を特定。同一参照で見つけられない場合（呼び出し側のミス）は null。
        int idx = block.Entries.IndexOf(target);
        if (idx <= 0) return null;

        // idx-1 から先頭方向に走査して、Deleted でない最初のエントリを採用する。
        for (int i = idx - 1; i >= 0; i--)
        {
            var prev = block.Entries[i];
            if (prev.State == DraftState.Deleted) continue;
            return prev.RealId;
        }
        return null;
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

    /// <summary>
    /// クレジット本体を新規 INSERT して採番された credit_id を返す（v1.2.0 工程 H-8 ターン 7 で導入）。
    /// 「クレジット話数コピー」機能でコピー先クレジットを Draft の Added 状態として組み立てた場合に、
    /// 保存サービスが <see cref="CreditDraftSession.Root"/>.State == Added を見て本メソッドを呼ぶ。
    /// </summary>
    private static async Task<int> InsertCreditAsync(MySqlConnection conn, MySqlTransaction tx, Credit c, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO credits
              (scope_kind, series_id, episode_id, credit_kind, part_type, presentation,
               notes, created_by, updated_by)
            VALUES
              (@ScopeKind, @SeriesId, @EpisodeId, @CreditKind, @PartType, @Presentation,
               @Notes, @CreatedBy, @UpdatedBy);
            SELECT LAST_INSERT_ID();
            """;
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, c, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateCardAsync(MySqlConnection conn, MySqlTransaction tx, CreditCard c, CancellationToken ct)
    {
        // v1.2.1 修正: 階層対称性のため credit_id も SET 句に含める（通常編集セッション中に
        // Card の親 Credit が変わることはないが、フェーズ 2.5 の FK 再同期と整合させる目的）。
        // card_seq は Phase 4 の Resequence で確定するため SET 句から除外（Phase 2.6 で退避値に逃がし済み）。
        const string sql = """
            UPDATE credit_cards SET
              credit_id = @CreditId,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_id = @CardId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, c, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateTierAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardTier t, CancellationToken ct)
    {
        // v1.2.1 修正: DnD で Tier を別 Card に移動するケースに備えて card_id を SET 句に含める。
        // フェーズ 2.5 で Entity.CardId は親 RealId と同期済み。
        const string sql = """
            UPDATE credit_card_tiers SET
              card_id = @CardId,
              tier_no = @TierNo,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_tier_id = @CardTierId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, t, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateGroupAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardGroup g, CancellationToken ct)
    {
        // v1.2.1 修正: DnD で Group を別 Tier に移動するケースに備えて card_tier_id を SET 句に含める。
        const string sql = """
            UPDATE credit_card_groups SET
              card_tier_id = @CardTierId,
              group_no = @GroupNo,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_group_id = @CardGroupId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, g, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateRoleAsync(MySqlConnection conn, MySqlTransaction tx, CreditCardRole r, CancellationToken ct)
    {
        // v1.2.1 修正: DnD で Role を別 Group に移動する操作（DropDraftRole）に備えて
        // card_group_id を SET 句に含める。これがないと「Role 移動が DB に反映されず、
        // 再読込み時に Role が消えたように見える」最重要バグが発生していた。
        // order_in_group は Phase 4 の Resequence で確定するため SET 句から除外（Phase 2.6 で退避値に逃がし済み）。
        const string sql = """
            UPDATE credit_card_roles SET
              card_group_id = @CardGroupId,
              role_code = @RoleCode,
              notes = @Notes,
              updated_by = @UpdatedBy
            WHERE card_role_id = @CardRoleId;
            """;
        await conn.ExecuteAsync(new CommandDefinition(sql, r, transaction: tx, cancellationToken: ct));
    }

    private static async Task UpdateBlockAsync(MySqlConnection conn, MySqlTransaction tx, CreditRoleBlock b, CancellationToken ct)
    {
        // v1.2.1 修正: 将来 Block を別 Role に移動する DnD が追加されても破綻しないよう
        // card_role_id を SET 句に含める（現状の DnD 仕様では同 Role 内のみ並べ替えに制限されているが保険）。
        // block_seq は Phase 4 の Resequence で確定するため SET 句から除外。
        const string sql = """
            UPDATE credit_role_blocks SET
              card_role_id = @CardRoleId,
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
        // v1.2.1 修正: entry_seq は Phase 4 の Resequence で確定するため SET 句から除外
        // （Phase 2.6 で退避値に逃がし済み）。block_id は DnD で Entry を別 Block に移動する
        // 操作に必要なので SET 句に含めたまま。is_broadcast_only も含めたまま（フラグ違いの
        // Entry にドロップしたときの正規化のため）。
        const string sql = """
            UPDATE credit_block_entries SET
              block_id = @BlockId,
              is_broadcast_only = @IsBroadcastOnly,
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
    /// 各層の seq 値を 1, 2, 3, ... に再採番する。退避値経由の 2 段階更新で
    /// UNIQUE (parent_id, seq) との衝突を回避する。
    /// <para>
    /// v1.2.2 修正: 退避値レンジを列の型に応じて指定するように変更（<see cref="Resequence2PhaseAsync"/>
    /// に <c>escapeBase</c> 引数を追加）。tinyint unsigned (0-255) の seq 列
    /// （<c>card_seq</c> / <c>order_in_group</c> / <c>block_seq</c>）には <c>escapeBase=100</c>、
    /// smallint unsigned の <c>entry_seq</c> には <c>escapeBase=50000</c> を渡す。
    /// Phase 2.6 が使う退避値レンジ（tinyint=200+i）と衝突しない範囲を選んでおり、Phase 4 の
    /// 1 段階目で「Phase 2.6 で 200+i に飛ばされた行」を 100+i に上書きしても UNIQUE 制約に
    /// 衝突しない（200+i と 100+i は別の値）。
    /// </para>
    /// </summary>
    private static async Task ResequenceAsync(
        MySqlConnection conn, MySqlTransaction tx,
        CreditDraftSession session, CancellationToken ct)
    {
        // ─── card_seq（同 credit_id 内）───
        // tinyint unsigned (0-255)。同一クレジット内のカード数は実用上 50 以下なので
        // escapeBase=100 で 100..(100+件数-1) の範囲に逃がす（156 件以上で 255 超になるが、
        // ヘルパ側の早期検知ガードが例外を投げる）。
        var cards = session.Root.Cards.Where(c => c.State != DraftState.Deleted)
            .OrderBy(c => c.Entity.CardSeq).ToList();
        await Resequence2PhaseAsync(conn, tx,
            cards.Select(c => (c.RealId!.Value, (int)c.Entity.CardSeq)).ToList(),
            "credit_cards", "card_id", "card_seq", escapeBase: 100, ct);
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
                    // tinyint unsigned (0-255)。同一グループ内の役職数は実用上少数。
                    var roles = grp.Roles.Where(r => r.State != DraftState.Deleted)
                        .OrderBy(r => r.Entity.OrderInGroup).ToList();
                    await Resequence2PhaseAsync(conn, tx,
                        roles.Select(r => (r.RealId!.Value, (int)r.Entity.OrderInGroup)).ToList(),
                        "credit_card_roles", "card_role_id", "order_in_group", escapeBase: 100, ct);
                    for (int i = 0; i < roles.Count; i++) roles[i].Entity.OrderInGroup = (byte)(i + 1);

                    foreach (var role in roles)
                    {
                        // ─── block_seq（同 card_role_id 内） ───
                        // tinyint unsigned (0-255)。同一役職内のブロック数は実用上少数。
                        var blocks = role.Blocks.Where(b => b.State != DraftState.Deleted)
                            .OrderBy(b => b.Entity.BlockSeq).ToList();
                        await Resequence2PhaseAsync(conn, tx,
                            blocks.Select(b => (b.RealId!.Value, (int)b.Entity.BlockSeq)).ToList(),
                            "credit_role_blocks", "block_id", "block_seq", escapeBase: 100, ct);
                        for (int i = 0; i < blocks.Count; i++) blocks[i].Entity.BlockSeq = (byte)(i + 1);

                        foreach (var blk in blocks)
                        {
                            // ─── entry_seq（同 block_id × is_broadcast_only 内） ───
                            // smallint unsigned (0-65535)。エントリ数は最大数千を想定するため、
                            // tinyint より広いレンジが必要。escapeBase=50000 で安全に運用可能。
                            // is_broadcast_only=0 と =1 はそれぞれ別グループとして再採番する。
                            foreach (var flag in new[] { false, true })
                            {
                                var entries = blk.Entries
                                    .Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag)
                                    .OrderBy(e => e.Entity.EntrySeq).ToList();
                                await Resequence2PhaseAsync(conn, tx,
                                    entries.Select(e => (e.RealId!.Value, (int)e.Entity.EntrySeq)).ToList(),
                                    "credit_block_entries", "entry_id", "entry_seq", escapeBase: 50000, ct);
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
    /// 1 段階目で全行を <paramref name="escapeBase"/> 起点の退避値に逃がし、2 段階目で本来の連番値に書き戻す。
    /// 既に 1, 2, 3, ... 連番になっている場合でも 2 段階の UPDATE を実行する（実害なし、
    /// 同じ値で UPDATE しても DB は無視するか同値で書き戻すだけ）。
    /// <para>
    /// v1.2.2 修正: 列の型に応じた退避値レンジを呼び出し側から指定できるよう
    /// <paramref name="escapeBase"/> 引数を追加。tinyint unsigned (0-255) の seq 列に対して
    /// 旧来の 50000+i 退避値を使うと「Out of range value for column ... at row 1」エラーになるため、
    /// 呼び出し側で適切なベース値（card_seq / order_in_group / block_seq → 100、entry_seq → 50000 等）を
    /// 渡すこと。本ヘルパ内では「退避値が tinyint 上限 (255) を超えそうな件数」のときに早期で
    /// <see cref="InvalidOperationException"/> を投げ、原因を明示する。
    /// </para>
    /// </summary>
    /// <param name="rowsInDesiredOrder">並び順通りに並べた (RealId, 現在の seq 値) のリスト。</param>
    /// <param name="tableName">UPDATE 対象テーブル名（例: "credit_cards"）。</param>
    /// <param name="idColumn">行特定用の主キー列名（例: "card_id"）。</param>
    /// <param name="seqColumn">再採番対象の seq 列名（例: "card_seq"）。</param>
    /// <param name="escapeBase">1 段階目の退避値の開始値。tinyint 列なら 100、smallint 列なら 50000 等を指定。</param>
    private static async Task Resequence2PhaseAsync(
        MySqlConnection conn, MySqlTransaction tx,
        List<(int RealId, int CurrentSeq)> rowsInDesiredOrder,
        string tableName, string idColumn, string seqColumn,
        int escapeBase,
        CancellationToken ct)
    {
        if (rowsInDesiredOrder.Count == 0) return;

        // v1.2.1 修正: 旧来は「Memory 値が既に 1, 2, 3, ... なら早期 return」していたが、
        // Phase 2.6 で Modified 行の seq 列を退避値に書き換えたため、Memory 値が連番でも
        // DB 上は退避値のままになっているケースが発生する。早期 return すると DB が退避値のまま
        // 残ってしまうので、無条件に 2 段階更新を走らせて DB を正しい連番に確定させる。
        // 同値で UPDATE しても DB は冪等で問題ないため、不要な行に対する UPDATE もそのまま流す。

        // v1.2.2 修正: tinyint unsigned 上限 (255) ガード。
        // 呼び出し側が tinyint 列に対して escapeBase=100 を指定した場合、件数が 156 を超えると
        // 退避値が 255 を超えてしまい、MySQL が Out of range エラーを返す。早期発見のため
        // 件数チェックを行い、超過時は分かりやすいメッセージで例外を投げる。
        // 上限判定は「seqColumn の名前が tinyint 系 5 列のいずれかに一致するか」で素朴判定する
        // （列名と型のマッピングをハードコードしないと完全には保証できないが、運用上はこれで十分）。
        bool isTinyColumn = seqColumn is "card_seq" or "tier_no" or "group_no"
                                       or "order_in_group" or "block_seq";
        if (isTinyColumn && escapeBase + rowsInDesiredOrder.Count - 1 > 255)
        {
            throw new InvalidOperationException(
                $"Phase 4 (ResequenceAsync): {tableName}.{seqColumn} は tinyint unsigned (0-255) のため、"
                + $"退避値範囲 {escapeBase}..{escapeBase + rowsInDesiredOrder.Count - 1} が 255 を超えます。"
                + " 同一親 FK 配下の行数が想定を超えています（実用上の上限を超えた変更を 1 トランザクションで"
                + " 実行しようとしている可能性があります）。");
        }

        // 1 段階目：退避値（escapeBase, escapeBase+1, ...）で逃がす。
        // v1.2.2 修正: 旧来は 50000 をハードコードしていたが、tinyint 列では範囲外になるため
        // 呼び出し側が指定する escapeBase を使う。Phase 2.6 が使う退避値レンジ（tinyint=200+i）と
        // 衝突しない範囲を呼び出し側が選ぶ責任を持つ（card_seq 等は 100+i、entry_seq は 50000+i など）。
        for (int i = 0; i < rowsInDesiredOrder.Count; i++)
        {
            int tempVal = escapeBase + i;
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
                                // v1.2.2 追加: A/B 併記の一時フラグも保存成功時にクリアする。
                                // 保存後は実 ParallelWithEntryId 値が Entity 側に書き込まれているため、
                                // 次回保存時に再解決を試みないようフラグを下ろしておく。
                                en.RequestParallelWithPrevious = false;
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
