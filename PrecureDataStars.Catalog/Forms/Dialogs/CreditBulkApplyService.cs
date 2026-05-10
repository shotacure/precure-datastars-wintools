using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// <see cref="BulkParseResult"/> を Draft レイヤ（<see cref="DraftCredit"/>）へ流し込む適用サービス（v1.2.1 追加。v1.2.2 で拡張）。
/// <para>
/// 役割（高位フロー）:
/// <list type="number">
///   <item><description><b>名前解決フェーズ</b>: 役職名を <c>roles</c> に引き当てて role_code / role_format_kind を確定。
///     person_aliases / character_aliases / company_aliases から alias_id を引き当て。
///     未解決の役職は呼び出し側に「QuickAdd ダイアログを 1 件ずつ開く」ことを依頼するための
///     <see cref="UnresolvedRoles"/> リストを返す。</description></item>
///   <item><description><b>マスタ自動投入フェーズ</b>: 未解決の Person / Character / Company は
///     既存の <c>QuickAddWithSingleAliasAsync</c> を呼んで自動登録する（ユーザー確認なし）。</description></item>
///   <item><description><b>Draft 注入フェーズ</b>: <see cref="ApplyToDraftAsync"/> を実行すると、
///     既存の <see cref="DraftCredit"/> の末尾にカード／Tier／Group／Role／Block／Entry を追加する。
///     ただし「ロール 0 件のカード 1 枚しかない」状態のときは、その空カードを上書きしてから始める。</description></item>
/// </list>
/// </para>
/// <para>
/// マスタに引き当て不能な人物名・キャラ名・企業名は <see cref="ParsedEntryKind.Text"/> に降格させ、
/// <see cref="CreditBlockEntry.RawText"/> として保持する。後で人手で人物作成 → 紐付け直しできる。
/// </para>
/// <para>
/// v1.2.2 で追加された機能:
/// <list type="bullet">
///   <item><description>LOGO エントリ（<c>[屋号#CIバージョン]</c>）の引き当てに対応（<see cref="LogosRepository"/> 依存追加、
///     屋号 + CI バージョン完全一致で <c>logos.logo_id</c> を解決。未ヒット時は TEXT 降格 + InfoMessage）。</description></item>
///   <item><description><see cref="ParsedEntry.IsBroadcastOnly"/> / <see cref="ParsedEntry.Notes"/> をエントリ実体に転写。</description></item>
///   <item><description>カード／Tier／Group／Role／Block の <c>Notes</c>（<see cref="ParsedCard.Notes"/> 等）を Draft 実体に転写。
///     既存ノードを再利用するケースでは <see cref="DraftBase.MarkModified"/> を発火させる。</description></item>
///   <item><description><see cref="ParsedBlock.ColCountExplicit"/> が true の場合、明示指定された
///     <see cref="ParsedBlock.ColCount"/> をブロックに優先反映する（タブ数推測値より優先）。</description></item>
///   <item><description><see cref="ParsedEntry.IsParallelContinuation"/> を <see cref="DraftEntry.RequestParallelWithPrevious"/>
///     へ転写する（実 ID 解決は <c>CreditSaveService</c> の保存フェーズで実施）。</description></item>
/// </list>
/// </para>
/// </summary>
public sealed class CreditBulkApplyService
{
    private readonly RolesRepository _rolesRepo;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    /// <summary>
    /// LOGO エントリ（<c>[屋号#CIバージョン]</c>）の引き当て用リポジトリ（v1.2.2 追加）。
    /// 屋号 alias_id + CI バージョンラベルで <c>logos</c> テーブルを引き、合致する logo_id を返す。
    /// </summary>
    private readonly LogosRepository _logosRepo;

    /// <summary>
    /// 「旧名義 =&gt; 新名義」記法（v1.3.0 追加）で、既存人物への新 alias 追加を行う際に
    /// 必要となる中間表 <c>person_alias_persons</c> 用リポジトリ。
    /// 旧 alias 経由で <c>person_id</c> を逆引きし、新 alias を同じ person に Upsert で結合する。
    /// </summary>
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;

    /// <summary>
    /// 似て非なる名義の比較進捗を呼び出し側に通知するイベント（v1.3.0 追加）。
    /// 引数は <c>(完了件数, 全体件数)</c>。<c>ApplyToDraftAsync</c> / <c>ApplyToDraftReplaceAsync</c> の
    /// 名義解決中に複数回発火する。一括入力ダイアログがこのイベントを購読してステータスラベルに反映する。
    /// 全件比較が走らない場合（リダイレクトで決着、または比較対象 0 件）は発火しない。
    /// </summary>
    public event Action<int, int>? CompareProgress;

    // ────────────────────────────────────────────────────────────
    // 似て非なる名義判定用のマスタキャッシュ（v1.3.0 追加）
    // ────────────────────────────────────────────────────────────
    // 1 適用フェーズ中、各エントリの名義引き当てごとに毎回 GetAllAsync を呼ぶと
    // N×M の DB アクセスになるため、最初の 1 回だけ取得してフィールドにキャッシュする。
    // ResolveAsync の冒頭でキャッシュをクリアし、必要時に lazy load する。
    private IReadOnlyList<PersonAlias>? _allPersonAliasesCache;
    private IReadOnlyList<CharacterAlias>? _allCharacterAliasesCache;
    private IReadOnlyList<CompanyAlias>? _allCompanyAliasesCache;

    /// <summary>
    /// 名前解決の結果として未解決だった役職表示名のリスト（<see cref="ResolveAsync"/> 後に参照）。
    /// 呼び出し側はこのリストを使って <c>QuickAddRoleDialog</c> をループ起動し、
    /// ユーザーに role_code / name_en / role_format_kind を入力させる想定。
    /// </summary>
    public List<ParsedRole> UnresolvedRoles { get; } = new();

    /// <summary>名前解決時に蓄積された情報メッセージ（複数ヒット警告など）。</summary>
    public List<string> InfoMessages { get; } = new();

    /// <summary>
    /// <see cref="CreditBulkApplyService"/> の新しいインスタンスを構築する。
    /// <para>v1.2.2 で <paramref name="logosRepo"/> 引数を追加（LOGO エントリ解決用）。</para>
    /// <para>v1.3.0 で <paramref name="personAliasPersonsRepo"/> 引数を追加（「旧 =&gt; 新」記法による
    /// 既存人物への新 alias 追加で、中間表 <c>person_alias_persons</c> を Upsert するため）。</para>
    /// </summary>
    public CreditBulkApplyService(
        RolesRepository rolesRepo,
        PersonsRepository personsRepo,
        PersonAliasesRepository personAliasesRepo,
        CharactersRepository charactersRepo,
        CharacterAliasesRepository characterAliasesRepo,
        CompaniesRepository companiesRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        PersonAliasPersonsRepository personAliasPersonsRepo)
    {
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _logosRepo = logosRepo ?? throw new ArgumentNullException(nameof(logosRepo));
        _personAliasPersonsRepo = personAliasPersonsRepo ?? throw new ArgumentNullException(nameof(personAliasPersonsRepo));
    }

    // ─────────────────────────────────────────────────────────
    //  名前解決フェーズ
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// パース結果の役職名・人物名・キャラ名・企業名をマスタに引き当てる（v1.2.1）。
    /// <para>
    /// 役職は表示名と <c>roles.name_ja</c> の完全一致を優先し、見つからなければ <see cref="UnresolvedRoles"/> に積む。
    /// 引き当てできた役職には <see cref="ParsedRole.ResolvedRoleCode"/> /
    /// <see cref="ParsedRole.ResolvedFormatKind"/> をセットする。
    /// </para>
    /// <para>
    /// 人物・キャラ・企業の解決は <see cref="ApplyToDraftAsync"/> 内で行う（マスタ自動追加が伴うため、
    /// 同じトランザクション境界で扱いたい）。本メソッドは役職解決のみを担当する。
    /// </para>
    /// </summary>
    public async Task ResolveAsync(BulkParseResult parsed, CancellationToken ct = default)
    {
        UnresolvedRoles.Clear();
        InfoMessages.Clear();

        // v1.3.0: 似て非なる名義判定用のキャッシュを毎回クリアする。
        // 別ダイアログ起動間でキャッシュが残っているとマスタ更新が反映されないため。
        _allPersonAliasesCache = null;
        _allCharacterAliasesCache = null;
        _allCompanyAliasesCache = null;

        if (parsed.IsEmpty) return;

        // 役職マスタを 1 回だけ取得して辞書化（複数役職をまとめて解決するため）
        var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var byNameJa = allRoles
            .GroupBy(r => r.NameJa, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var card in parsed.Cards)
        {
            foreach (var tier in card.Tiers)
            {
                foreach (var group in tier.Groups)
                {
                    foreach (var role in group.Roles)
                    {
                        // STEP 1: name_ja 完全一致で引き当てを試みる
                        if (byNameJa.TryGetValue(role.DisplayName, out var hits))
                        {
                            if (hits.Count == 1)
                            {
                                role.ResolvedRoleCode = hits[0].RoleCode;
                                role.ResolvedFormatKind = hits[0].RoleFormatKind;
                            }
                            else
                            {
                                // 複数ヒット時は先頭採用（display_order 昇順なので運用上もっとも自然な役職が来るはず）
                                role.ResolvedRoleCode = hits[0].RoleCode;
                                role.ResolvedFormatKind = hits[0].RoleFormatKind;
                                InfoMessages.Add(
                                    $"役職「{role.DisplayName}」がマスタに {hits.Count} 件ヒット。先頭の {hits[0].RoleCode} を採用しました。");
                            }
                            continue;
                        }

                        // STEP 2: 未解決リストに積む（呼び出し側で QuickAddRoleDialog 起動）
                        UnresolvedRoles.Add(role);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 未解決役職に対してユーザーが入力した結果を反映する（v1.2.1）。
    /// 呼び出し側は QuickAddRoleDialog を 1 件ずつ起動し、確定したら本メソッドで結果を流し込む。
    /// </summary>
    /// <param name="parsed">パース結果。</param>
    /// <param name="resolutions">role 表示名 → (role_code, role_format_kind) のマッピング。</param>
    public void ApplyRoleResolutions(BulkParseResult parsed, IReadOnlyDictionary<string, (string RoleCode, string FormatKind)> resolutions)
    {
        foreach (var card in parsed.Cards)
        {
            foreach (var tier in card.Tiers)
            {
                foreach (var group in tier.Groups)
                {
                    foreach (var role in group.Roles)
                    {
                        if (role.ResolvedRoleCode is null
                            && resolutions.TryGetValue(role.DisplayName, out var pair))
                        {
                            role.ResolvedRoleCode = pair.RoleCode;
                            role.ResolvedFormatKind = pair.FormatKind;
                        }
                    }
                }
            }
        }

        // 未解決リストから解消済みを除く
        UnresolvedRoles.RemoveAll(r => r.ResolvedRoleCode is not null);
    }

    // ─────────────────────────────────────────────────────────
    //  Draft 注入フェーズ
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// パース結果を <paramref name="session"/> 配下の <see cref="DraftCredit"/> へ追加する（v1.2.1）。
    /// <para>
    /// 動作:
    /// <list type="bullet">
    ///   <item><description>「ロール 0 件のカード 1 枚」状態の Draft なら、その空カードを上書き始点とする。
    ///     空 Tier 1 / 空 Group 1 を再利用してから新しい役職群を流し込む。</description></item>
    ///   <item><description>それ以外は末尾に新規 Card を append し、Tier 1 / Group 1 を新設する。</description></item>
    ///   <item><description>person / character / company は ID 引き当てを試みて、未ヒットなら QuickAdd で自動投入。
    ///     それでも投入不能（名前空など）なら TEXT エントリに降格。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="parsed">パース結果（解決済み）。</param>
    /// <param name="session">適用対象の編集セッション。</param>
    /// <param name="updatedBy">マスタ自動投入時の created_by/updated_by に使う値。</param>
    /// <param name="ct">キャンセル。</param>
    public async Task ApplyToDraftAsync(
        BulkParseResult parsed, CreditDraftSession session,
        string? updatedBy, CancellationToken ct = default)
    {
        if (parsed.IsEmpty) return;
        if (session is null) throw new ArgumentNullException(nameof(session));

        // ─── 起点となる Card を決める ───
        // 「ロール 0 件のカード 1 枚」だけの Draft は空のテンプレ状態とみなして上書き始点にする。
        DraftCard? overwriteSeedCard = null;
        if (session.Root.Cards.Count == 1)
        {
            var only = session.Root.Cards[0];
            bool isEmpty = only.Tiers.All(t => t.Groups.All(g => g.Roles.Count == 0));
            if (isEmpty) overwriteSeedCard = only;
        }

        // ─── パース結果のカードを順に流し込む ───
        for (int ci = 0; ci < parsed.Cards.Count; ci++)
        {
            var pc = parsed.Cards[ci];

            DraftCard targetCard;
            if (ci == 0 && overwriteSeedCard is not null)
            {
                // 空の seed カードを再利用：配下の Tier/Group は使い回し、新規分を追加する形に整える
                targetCard = overwriteSeedCard;
            }
            else
            {
                // 末尾に新規 Card を append
                targetCard = AppendNewCard(session, session.Root);
            }

            await ApplyCardAsync(pc, session, targetCard, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 指定スコープ配下を <see cref="BulkParseResult"/> の内容で置換する（v1.2.2 追加）。
    /// <para>
    /// ツリー右クリック「📝 一括入力で編集...」（Phase 4 で UI 接続）から呼ばれる経路。
    /// 動作:
    /// <list type="number">
    ///   <item><description>対象スコープ配下の既存子ノードをすべて <see cref="DraftBase.MarkDeleted"/> または
    ///     <see cref="DraftState.Added"/> 状態の場合は親リストから直接除去する。</description></item>
    ///   <item><description>パース結果からスコープに対応する範囲を抜き出し、新規 Draft ノードを生成して
    ///     対象スコープに追加する。スコープ自身（カード／ティア／グループ／役職）の Notes は
    ///     パース結果のトップレベル <c>Notes</c> から転写し、必要なら <see cref="DraftBase.MarkModified"/>。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// パース結果が想定スコープより外側を持っている場合（例: Role スコープなのに <c>----</c> でカードを増やした）
    /// は、最上位のみ採用して残りは <see cref="InfoMessages"/> に警告として残す（呼び出し側でユーザーに表示）。
    /// </para>
    /// </summary>
    /// <param name="parsed">置換ソースとなるパース結果。事前に <see cref="ResolveAsync"/> で役職解決済みであること。</param>
    /// <param name="session">対象の編集セッション。</param>
    /// <param name="scope">置換対象のスコープ。</param>
    /// <param name="updatedBy">マスタ自動投入時の created_by/updated_by に使う値。</param>
    /// <param name="ct">キャンセル。</param>
    public async Task ApplyToDraftReplaceAsync(
        BulkParseResult parsed, CreditDraftSession session,
        DraftScopeRef scope, string? updatedBy, CancellationToken ct = default)
    {
        if (parsed is null) throw new ArgumentNullException(nameof(parsed));
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (scope is null) throw new ArgumentNullException(nameof(scope));

        switch (scope.Kind)
        {
            case ScopeKind.Credit:
            {
                // クレジット全体置換: 既存 Cards をすべて Deleted/除去 → パース結果の Cards を新規追加。
                var credit = scope.Credit ?? throw new InvalidOperationException("scope.Credit が null です。");
                ClearChildren(credit.Cards);

                // パース結果の全カードを末尾追加（ApplyToDraftAsync の overwriteSeedCard ロジックは使わず、
                // ReplaceScope は明示的に「全部入れ替える」セマンティクスなので素直に追加）。
                foreach (var pc in parsed.Cards)
                {
                    ct.ThrowIfCancellationRequested();
                    var newCard = AppendNewCard(session, credit);
                    // AppendNewCard は seed Tier 1 + Group 1 を生成するが、ApplyCardAsync が
                    // 既存 Tier をそのまま再利用する設計なのでこのまま渡せばよい。
                    await ApplyCardAsync(pc, session, newCard, updatedBy, ct).ConfigureAwait(false);
                }
                return;
            }

            case ScopeKind.Card:
            {
                // カード内置換: パース結果の最初のカードのみを採用して、対象 Card 配下を置き換える。
                var card = scope.Card ?? throw new InvalidOperationException("scope.Card が null です。");
                if (parsed.Cards.Count == 0) return;
                if (parsed.Cards.Count > 1)
                {
                    InfoMessages.Add(
                        $"カードスコープの編集中に '----' でカード区切りが {parsed.Cards.Count - 1} 個書かれていました。最初のカードのみ適用し、残りは無視します。");
                }
                var pc = parsed.Cards[0];

                // 既存 Tier 群を全削除してから ApplyCardAsync を呼ぶと、ApplyCardAsync の
                // 「既存 Tier 再利用ロジック」が「再利用対象なし → 全部新規追加」モードになる。
                ClearChildren(card.Tiers);

                // ParsedCard.Notes は ApplyCardAsync の冒頭で targetCard.Entity.Notes に転写される。
                await ApplyCardAsync(pc, session, card, updatedBy, ct).ConfigureAwait(false);
                return;
            }

            case ScopeKind.Tier:
            {
                // Tier 内置換: パース結果の Cards[0].Tiers[0] のみを採用。
                var tier = scope.Tier ?? throw new InvalidOperationException("scope.Tier が null です。");
                if (parsed.Cards.Count == 0 || parsed.Cards[0].Tiers.Count == 0) return;
                if (parsed.Cards.Count > 1 || parsed.Cards[0].Tiers.Count > 1)
                {
                    InfoMessages.Add(
                        "ティアスコープの編集中にカード区切り '----' またはティア区切り '---' が書かれていました。最初のティアのみ適用し、残りは無視します。");
                }
                var pt = parsed.Cards[0].Tiers[0];

                ClearChildren(tier.Groups);
                await ApplyTierAsync(pt, session, tier, updatedBy, ct).ConfigureAwait(false);
                return;
            }

            case ScopeKind.Group:
            {
                // Group 内置換: パース結果の Cards[0].Tiers[0].Groups[0] のみを採用。
                var group = scope.Group ?? throw new InvalidOperationException("scope.Group が null です。");
                if (parsed.Cards.Count == 0 || parsed.Cards[0].Tiers.Count == 0
                    || parsed.Cards[0].Tiers[0].Groups.Count == 0) return;
                if (parsed.Cards.Count > 1 || parsed.Cards[0].Tiers.Count > 1
                    || parsed.Cards[0].Tiers[0].Groups.Count > 1)
                {
                    InfoMessages.Add(
                        "グループスコープの編集中に外側スコープの区切り（'----' / '---' / '--'）が書かれていました。最初のグループのみ適用し、残りは無視します。");
                }
                var pg = parsed.Cards[0].Tiers[0].Groups[0];

                ClearChildren(group.Roles);
                await ApplyGroupAsync(pg, session, group, updatedBy, ct).ConfigureAwait(false);
                return;
            }

            case ScopeKind.Role:
            {
                // Role 内置換: パース結果の Cards[0].Tiers[0].Groups[0].Roles[0] のみを採用。
                // 既存 Role の RoleCode は保持する（パースされた DisplayName は無視）が、
                // 役職備考（ParsedRole.Notes）は転写する。
                var role = scope.Role ?? throw new InvalidOperationException("scope.Role が null です。");
                if (parsed.Cards.Count == 0 || parsed.Cards[0].Tiers.Count == 0
                    || parsed.Cards[0].Tiers[0].Groups.Count == 0
                    || parsed.Cards[0].Tiers[0].Groups[0].Roles.Count == 0) return;
                if (parsed.Cards.Count > 1 || parsed.Cards[0].Tiers.Count > 1
                    || parsed.Cards[0].Tiers[0].Groups.Count > 1
                    || parsed.Cards[0].Tiers[0].Groups[0].Roles.Count > 1)
                {
                    InfoMessages.Add(
                        "役職スコープの編集中に複数の役職が書かれていました。最初の役職のみ適用し、残りは無視します。");
                }
                var pr = parsed.Cards[0].Tiers[0].Groups[0].Roles[0];

                // 既存 Block 群を削除。
                ClearChildren(role.Blocks);

                // 役職備考の転写（既存ノードなので変化検出 + MarkModified）。
                ApplyNotesIfChanged(role, pr.Notes, n => role.Entity.Notes = n,
                    () => role.Entity.Notes);

                // パース結果のブロック群を、対象 Role の配下に追加していく。
                // ApplyGroupAsync 内のブロック追加ロジックを直接インラインで呼ぶ（Group 階層を経由せず Role に直接入れる）。
                foreach (var pb in pr.Blocks)
                {
                    ct.ThrowIfCancellationRequested();
                    if (pb.Rows.Count == 0 && pb.LeadingCompanyText is null && pb.Notes is null) continue;

                    var block = AppendNewBlock(session, role, (byte)pb.ColCount);

                    if (pb.Notes is not null)
                    {
                        block.Entity.Notes = pb.Notes;
                    }

                    if (!string.IsNullOrEmpty(pb.LeadingCompanyText))
                    {
                        int? leadingAliasId = await ResolveOrCreateCompanyAliasAsync(
                            pb.LeadingCompanyText, oldName: null, updatedBy, ct).ConfigureAwait(false);
                        if (leadingAliasId.HasValue)
                        {
                            block.Entity.LeadingCompanyAliasId = leadingAliasId;
                        }
                        else
                        {
                            InfoMessages.Add($"ブロック先頭企業 [{pb.LeadingCompanyText}] を登録できませんでした。TEXT エントリとして退避します。");
                            AppendTextEntry(session, block, $"[{pb.LeadingCompanyText}]");
                        }
                    }

                    foreach (var prow in pb.Rows)
                    {
                        foreach (var pe in prow.Entries)
                        {
                            await AppendParsedEntryAsync(session, block, pe, pr, updatedBy, ct).ConfigureAwait(false);
                        }
                    }
                }
                return;
            }

            default:
                throw new InvalidOperationException($"未対応のスコープ種別: {scope.Kind}");
        }
    }

    /// <summary>
    /// Draft 子ノードリストの中身を「適切な状態」でクリアする（v1.2.2 追加、ReplaceScope 用）。
    /// <list type="bullet">
    ///   <item><description><see cref="DraftState.Added"/> の子は親リストから直接除去（DB に未投入なので痕跡を残さない）</description></item>
    ///   <item><description><see cref="DraftState.Unchanged"/> / <see cref="DraftState.Modified"/> の子は <see cref="DraftBase.MarkDeleted"/>
    ///     を呼んで <see cref="DraftState.Deleted"/> に遷移させる（DB 上の対応行が保存時に DELETE される）</description></item>
    ///   <item><description><see cref="DraftState.Deleted"/> の子はそのまま（既に削除予定）</description></item>
    /// </list>
    /// クリア後のリストには Deleted 状態のノードが残る場合があるが、ApplyCardAsync 等の
    /// 「既存 Tier 再利用ロジック」は <see cref="DraftBase.State"/> を見ない（リスト位置で再利用判定する）ため、
    /// 念のため Deleted ノードもリストから物理除去しておく方が安全。
    /// </summary>
    private static void ClearChildren<T>(List<T> children) where T : DraftBase
    {
        // 既存子ノードを走査して Deleted マーク or リスト除去を実施。
        // 後段で同リストに新規ノードを Add していくため、Deleted ノードは物理除去する。
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var child = children[i];
            if (child.State == DraftState.Added)
            {
                // DB 未投入の新規追加ノード → そのまま除去。
                children.RemoveAt(i);
            }
            else
            {
                // Unchanged / Modified → DB から削除予定としてマーク。
                child.MarkDeleted();
                // Deleted ノードはリストには残しておかず、物理的に外す
                // （ApplyCardAsync 等の「リスト先頭から既存ノードを再利用」ロジックと干渉しないように）。
                children.RemoveAt(i);
            }
        }
    }

    /// <summary>パース結果の 1 カード分を <see cref="DraftCard"/> に流し込む。</summary>
    private async Task ApplyCardAsync(
        ParsedCard pc, CreditDraftSession session, DraftCard targetCard,
        string? updatedBy, CancellationToken ct)
    {
        // v1.2.2: カード備考を Draft 実体にコピー。
        // 既存カードを再利用するケース（overwriteSeedCard）では既に Notes が空のはずだが、
        // 値が異なる場合に限り MarkModified() を呼ぶ（無条件 modify は避ける）。
        // ParsedCard.Notes が null の場合は明示クリア指示なので null をそのまま代入する。
        ApplyNotesIfChanged(targetCard, pc.Notes, n => targetCard.Entity.Notes = n,
            () => targetCard.Entity.Notes);

        // 既存の Tier をそのまま使うか、新規追加するか。
        // Card 作成直後の seed Tier (TierNo=1) は 1 つ既にあるので、最初の ParsedTier はそれを使う。
        for (int ti = 0; ti < pc.Tiers.Count; ti++)
        {
            var pt = pc.Tiers[ti];

            DraftTier targetTier;
            if (ti < targetCard.Tiers.Count)
            {
                // 既存 Tier を再利用（seed の Tier 1）
                targetTier = targetCard.Tiers[ti];
            }
            else
            {
                // 新規 Tier 追加。最大 2 までしか作らない（仕様）。3 つ目以降は無視。
                if (targetCard.Tiers.Count >= 2) break;
                targetTier = AppendNewTier(session, targetCard);
            }

            await ApplyTierAsync(pt, session, targetTier, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>パース結果の 1 Tier 分を <see cref="DraftTier"/> に流し込む。</summary>
    private async Task ApplyTierAsync(
        ParsedTier pt, CreditDraftSession session, DraftTier targetTier,
        string? updatedBy, CancellationToken ct)
    {
        // v1.2.2: Tier 備考を Draft 実体にコピー。
        ApplyNotesIfChanged(targetTier, pt.Notes, n => targetTier.Entity.Notes = n,
            () => targetTier.Entity.Notes);

        for (int gi = 0; gi < pt.Groups.Count; gi++)
        {
            var pg = pt.Groups[gi];

            DraftGroup targetGroup;
            if (gi < targetTier.Groups.Count)
            {
                targetGroup = targetTier.Groups[gi];
            }
            else
            {
                targetGroup = AppendNewGroup(session, targetTier);
            }

            await ApplyGroupAsync(pg, session, targetGroup, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>パース結果の 1 Group 分を <see cref="DraftGroup"/> に流し込む。</summary>
    private async Task ApplyGroupAsync(
        ParsedGroup pg, CreditDraftSession session, DraftGroup targetGroup,
        string? updatedBy, CancellationToken ct)
    {
        // v1.2.2: Group 備考を Draft 実体にコピー。
        ApplyNotesIfChanged(targetGroup, pg.Notes, n => targetGroup.Entity.Notes = n,
            () => targetGroup.Entity.Notes);

        foreach (var pr in pg.Roles)
        {
            if (pr.ResolvedRoleCode is null) continue; // 未解決はスキップ（呼び出し側が事前に解消する想定）

            // 役職を新規追加（同 group 内で重複役職は許容、Draft では既存 role を再利用しない＝末尾追加で素直に）
            var role = AppendNewRole(session, targetGroup, pr.ResolvedRoleCode);

            // v1.2.2: 役職備考を Draft 実体にコピー（新規 role なので未保存状態 = MarkModified() 不要、直接代入で OK）。
            // 既存役職に対する notes の上書きは ApplyToDraftReplaceAsync（Phase 3 で実装予定）の領分。
            if (pr.Notes is not null)
            {
                role.Entity.Notes = pr.Notes;
            }

            // ─── ブロック群を流し込む ───
            foreach (var pb in pr.Blocks)
            {
                if (pb.Rows.Count == 0 && pb.LeadingCompanyText is null && pb.Notes is null) continue;

                // v1.2.2: ColCountExplicit が true なら明示指定された ColCount を使う。
                // false ならパーサが推測したタブ数値（既存挙動）が入っている。
                var block = AppendNewBlock(session, role, (byte)pb.ColCount);

                // v1.2.2: ブロック備考を Draft 実体にコピー（新規 block なので直接代入で OK）。
                if (pb.Notes is not null)
                {
                    block.Entity.Notes = pb.Notes;
                }

                // [先頭企業屋号]
                if (!string.IsNullOrEmpty(pb.LeadingCompanyText))
                {
                    int? leadingAliasId = await ResolveOrCreateCompanyAliasAsync(
                        pb.LeadingCompanyText, oldName: null, updatedBy, ct).ConfigureAwait(false);
                    if (leadingAliasId.HasValue)
                    {
                        block.Entity.LeadingCompanyAliasId = leadingAliasId;
                    }
                    else
                    {
                        // 解決不能なら警告メッセージに残し、TEXT エントリとしてブロック先頭に挿入
                        InfoMessages.Add($"ブロック先頭企業 [{pb.LeadingCompanyText}] を登録できませんでした。TEXT エントリとして退避します。");
                        AppendTextEntry(session, block, $"[{pb.LeadingCompanyText}]");
                    }
                }

                // ─── 各行・各セル ───
                foreach (var row in pb.Rows)
                {
                    // v1.2.2: 1 行内のエントリは並びの先頭から順に追加するが、
                    // & プレフィクス（IsParallelContinuation）が付いたセルは「直前エントリと A/B 併記」として
                    // DraftEntry.RequestParallelWithPrevious=true で追加する（実 ID 解決は CreditSaveService）。
                    foreach (var pe in row.Entries)
                    {
                        await AppendParsedEntryAsync(session, block, pe, pr, updatedBy, ct).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draft ノードの Notes プロパティに対して「値が変わっていれば代入 + Modified 化」を行うヘルパ（v1.2.2 追加）。
    /// 新規追加（State=Added）のノードでは MarkModified の効果は無いが、副作用なく安全。
    /// 既存ノード（State=Unchanged）に対しては値変更があった場合のみ Modified に遷移する。
    /// </summary>
    private static void ApplyNotesIfChanged(DraftBase node, string? newValue,
        Action<string?> setter, Func<string?> getter)
    {
        string? cur = getter();
        if (string.Equals(cur, newValue, StringComparison.Ordinal)) return;
        setter(newValue);
        if (node.State == DraftState.Unchanged) node.MarkModified();
    }

    /// <summary>1 つの <see cref="ParsedEntry"/> を解決して Draft の Block にエントリ追加する。</summary>
    private async Task AppendParsedEntryAsync(
        CreditDraftSession session, DraftBlock block, ParsedEntry pe, ParsedRole pr,
        string? updatedBy, CancellationToken ct)
    {
        // v1.2.2: 装飾情報を最終 Draft エントリに反映するためのヘルパ。
        // どの分岐で生成されたエントリにも一律で IsBroadcastOnly / Notes / RequestParallelWithPrevious を
        // 後付けする（TEXT 降格分も含めて適用）。
        // ブロックの最後に追加された DraftEntry を対象とする（AppendXxxEntry 系は parent.Entries.Add
        // で末尾に push するため、呼び出し直後に末尾を取れば該当 Draft が手に入る）。
        void ApplyEntryModifiersToLast()
        {
            if (block.Entries.Count == 0) return;
            var last = block.Entries[^1];
            if (pe.IsBroadcastOnly) last.Entity.IsBroadcastOnly = true;
            if (pe.Notes is not null) last.Entity.Notes = pe.Notes;
            // A/B 併記フラグは保存フェーズで実 ID 解決のために使う一時情報。
            // ブロック先頭エントリ（直前エントリが無い）の場合は Block 警告級だが、ここでは
            // 適用フェーズなので緩く InfoMessages に積むだけにする（パーサ側で警告するのが本筋）。
            if (pe.IsParallelContinuation)
            {
                if (block.Entries.Count <= 1)
                {
                    InfoMessages.Add(
                        $"{pe.LineNumber} 行目: A/B 併記マーカー「& 」がブロック先頭に置かれています。直前エントリがないため併記関係は構築されません。");
                }
                else
                {
                    last.RequestParallelWithPrevious = true;
                }
            }
        }

        switch (pe.Kind)
        {
            case ParsedEntryKind.CharacterVoice:
            {
                // キャラ alias 引き当て or 新規作成。v1.3.0: CharacterOldName をリダイレクトキーとして渡す。
                int? characterAliasId = await ResolveOrCreateCharacterAliasAsync(
                    pe.CharacterRawText, pe.IsForcedNewCharacter, pe.CharacterOldName, updatedBy, ct).ConfigureAwait(false);

                // 声優 alias 引き当て or 新規作成。v1.3.0: PersonOldName をリダイレクトキーとして渡す。
                int? personAliasId = await ResolveOrCreatePersonAliasAsync(
                    pe.PersonRawText, pe.PersonOldName, updatedBy, ct).ConfigureAwait(false);

                // 所属
                int? affCompanyAliasId = null;
                string? affRawText = null;
                await ResolveAffiliationAsync(pe.AffiliationRawText, updatedBy, ct,
                    aliasId => affCompanyAliasId = aliasId,
                    raw => affRawText = raw).ConfigureAwait(false);

                if (personAliasId is null)
                {
                    // 声優 alias が無い CHARACTER_VOICE は不正なので TEXT に降格。
                    string raw = string.IsNullOrEmpty(pe.PersonRawText)
                        ? $"<{pe.CharacterRawText}>"
                        : $"<{pe.CharacterRawText}> {pe.PersonRawText}";
                    AppendTextEntry(session, block, raw);
                    ApplyEntryModifiersToLast();
                    return;
                }

                AppendCharacterVoiceEntry(session, block,
                    personAliasId.Value, characterAliasId,
                    rawCharacterText: characterAliasId is null ? pe.CharacterRawText : null,
                    affCompanyAliasId, affRawText);
                ApplyEntryModifiersToLast();
                return;
            }

            case ParsedEntryKind.Logo:
            {
                // v1.2.2 追加: [屋号#CIバージョン] 構文。屋号名で company_alias を引き当て、
                // その配下のロゴから ci_version_label が一致するものを採用する。
                // 屋号自動投入は LOGO 解決の文脈では行わない（LOGO はマスタ管理画面で明示登録すべき
                // 性質のもののため、未ヒットなら TEXT 降格 + InfoMessages に残す）。
                // v1.3.0: CompanyOldName（屋号部分の旧 => 新）を引き当て軸として渡す。
                int? logoId = await ResolveLogoAsync(pe.CompanyRawText, pe.CompanyOldName, pe.LogoCiVersionLabel, ct)
                    .ConfigureAwait(false);

                if (logoId is null)
                {
                    // TEXT 降格して [屋号#CIバージョン] を raw_text に残す。後で人手で対応可能にする。
                    string raw = string.IsNullOrEmpty(pe.LogoCiVersionLabel)
                        ? $"[{pe.CompanyRawText}]"
                        : $"[{pe.CompanyRawText}#{pe.LogoCiVersionLabel}]";
                    AppendTextEntry(session, block, raw);
                    InfoMessages.Add(
                        $"{pe.LineNumber} 行目: ロゴ「[{pe.CompanyRawText}#{pe.LogoCiVersionLabel}]」を引き当てできませんでした。TEXT エントリとして退避します（マスタ管理画面の「ロゴ」タブで登録してください）。");
                    ApplyEntryModifiersToLast();
                    return;
                }

                AppendLogoEntry(session, block, logoId.Value);
                ApplyEntryModifiersToLast();
                return;
            }

            case ParsedEntryKind.Company:
            {
                // v1.3.0: CompanyOldName をリダイレクトキーとして渡す。
                int? companyAliasId = await ResolveOrCreateCompanyAliasAsync(
                    pe.CompanyRawText, pe.CompanyOldName, updatedBy, ct).ConfigureAwait(false);

                if (companyAliasId is null)
                {
                    // 企業名空文字など解決不能 → TEXT 退避。
                    AppendTextEntry(session, block, $"[{pe.CompanyRawText}]");
                    ApplyEntryModifiersToLast();
                    return;
                }
                AppendCompanyEntry(session, block, companyAliasId.Value);
                ApplyEntryModifiersToLast();
                return;
            }

            case ParsedEntryKind.Person:
            default:
            {
                // VOICE_CAST 役職に PERSON エントリが現れた場合は CHARACTER_VOICE への降格は行わない
                // （パーサ側で既に <X> なし行は警告済みのため、ここに来るのは PERSON が確実な局面）。

                // v1.3.0: PersonOldName をリダイレクトキーとして渡す。
                int? personAliasId = await ResolveOrCreatePersonAliasAsync(
                    pe.PersonRawText, pe.PersonOldName, updatedBy, ct).ConfigureAwait(false);

                int? affCompanyAliasId = null;
                string? affRawText = null;
                await ResolveAffiliationAsync(pe.AffiliationRawText, updatedBy, ct,
                    aliasId => affCompanyAliasId = aliasId,
                    raw => affRawText = raw).ConfigureAwait(false);

                if (personAliasId is null)
                {
                    AppendTextEntry(session, block, pe.PersonRawText ?? "");
                    ApplyEntryModifiersToLast();
                    return;
                }
                AppendPersonEntry(session, block, personAliasId.Value, affCompanyAliasId, affRawText);
                ApplyEntryModifiersToLast();
                return;
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  マスタ引き当て + 自動 QuickAdd
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 屋号 + CI バージョンラベルから <c>logo_id</c> を引き当てる（v1.2.2 追加）。
    /// <para>
    /// 動作:
    /// <list type="number">
    ///   <item><description><paramref name="companyName"/> で <c>company_aliases</c> を name 完全一致検索。
    ///     ヒットしなければ null を返す（屋号自動投入は行わない＝LOGO は明示登録すべきリソース）。</description></item>
    ///   <item><description>屋号 alias_id 配下のロゴ群を <c>logos</c> から取得し、
    ///     <c>ci_version_label</c> が <paramref name="ciVersionLabel"/> と完全一致するロゴを採用。</description></item>
    ///   <item><description>未ヒット時は null を返す（呼び出し側で TEXT 降格 + InfoMessage 出力）。</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// LOGO は屋号テキストとは別レイヤのマスタ（CI デザインバージョン管理用）であり、
    /// 一括入力からの自動投入は <c>ci_version_label</c> 以外のメタデータ（valid_from, description 等）が
    /// 揃わないため、未ヒット時は明示的にユーザーに気付かせる方針とする。
    /// </para>
    /// <summary>
    /// LOGO エントリの引き当て（v1.2.2 追加）。屋号 alias_id + CI バージョンラベルで <c>logos</c> テーブルを引く。
    /// 屋号自動投入は行わない（LOGO は明示登録すべき性質のため、未ヒット時は呼び出し側で TEXT 降格）。
    /// <para>
    /// v1.3.0: <paramref name="companyOldName"/> が指定されていれば、引き当ての軸として旧屋号を採用する
    /// （旧屋号で登録された logos を新屋号表記からも引きたいケースに対応）。
    /// 屋号部分の似て非なる判定もここで一緒に走らせる（誤記検出のため）。
    /// </para>
    /// </summary>
    /// <param name="companyName">屋号テキスト（<c>[屋号#CIバージョン]</c> の屋号部分）。</param>
    /// <param name="companyOldName">屋号部分の「旧 =&gt; 新」記法における旧側参照キー（v1.3.0 追加）。</param>
    /// <param name="ciVersionLabel">CI バージョンラベル（<c>[屋号#CIバージョン]</c> の CI 部分）。</param>
    /// <returns>引き当てできた logo_id、または引き当て不能なら null。</returns>
    private async Task<int?> ResolveLogoAsync(
        string? companyName, string? companyOldName, string? ciVersionLabel, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(companyName)) return null;
        if (string.IsNullOrWhiteSpace(ciVersionLabel)) return null;

        string name = companyName.Trim();
        string ci = ciVersionLabel.Trim();

        // STEP 1: 屋号 alias_id を引く。旧屋号指定があれば旧側を優先（旧屋号で登録された logos に届けるため）。
        // 旧屋号で見つからなければ新屋号で再試行するフォールバック付き。
        CompanyAlias? exact = null;
        if (!string.IsNullOrWhiteSpace(companyOldName))
        {
            string oldTrim = companyOldName!.Trim();
            var oldHits = await _companyAliasesRepo.SearchAsync(oldTrim, limit: 5, ct).ConfigureAwait(false);
            exact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal));
            if (exact is null)
            {
                InfoMessages.Add(
                    $"⚠ ロゴ「[{oldTrim} => {name}#{ci}]」の旧屋号が既存マスタに見つかりませんでした。新屋号「{name}」で引き当てを再試行します。");
            }
        }
        if (exact is null)
        {
            var hits = await _companyAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
            exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        }

        if (exact is null)
        {
            // 屋号が引き当たらないので類似度判定だけ走らせて警告に積む（LOGO は新規 alias 作成しないので警告のみ）。
            await WarnIfSimilarCompanyAliasAsync(name, ct).ConfigureAwait(false);
            return null;
        }

        // STEP 2: 屋号下のロゴ群から ci_version_label が一致するロゴを探す。
        var logos = await _logosRepo.GetByCompanyAliasAsync(exact.AliasId, ct).ConfigureAwait(false);
        var match = logos.FirstOrDefault(l => string.Equals(l.CiVersionLabel, ci, StringComparison.Ordinal));
        if (match is null) return null;

        return match.LogoId;
    }

    /// <summary>
    /// 人物名義の引き当て or 新規作成。空文字 / 空白なら null を返す。
    /// <para>
    /// 「人物名 半角SP区切り → family/given を分割保存」「・区切り → given・family」「区切りなし → full のみ」
    /// の規則で <see cref="PersonsRepository.QuickAddWithSingleAliasAsync"/> を呼ぶ。
    /// </para>
    /// <para>
    /// v1.3.0: <paramref name="oldName"/> が指定されていれば、左側「旧名義」で既存 <c>person_aliases</c> を
    /// 引き当てて主人物（<c>person_alias_persons.person_seq=1</c>）の <c>person_id</c> を取得し、
    /// 同 person 配下に右側「新名義」を <c>person_aliases</c> + 中間表 <c>person_alias_persons</c> Upsert で
    /// 追加登録する。旧名義が引き当たらなければ警告 <see cref="InfoMessages"/> + 通常新規作成にフォールバック。
    /// 旧名義リダイレクトで決着しない場合のみ、似て非なる名義の全件比較が走る（リダイレクトの方が
    /// 強い意図表現なので、両方の警告が二重に出るのを避ける）。
    /// </para>
    /// </summary>
    private async Task<int?> ResolveOrCreatePersonAliasAsync(
        string? rawName, string? oldName, string? updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        // 既存検索（name 完全一致）
        var hits = await _personAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
        var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        if (exact is not null) return exact.AliasId;

        // v1.3.0: 「旧 => 新」記法で旧名義が指定されている場合、既存 person への新 alias 追加を試みる。
        if (!string.IsNullOrWhiteSpace(oldName))
        {
            string oldTrim = oldName!.Trim();
            var oldHits = await _personAliasesRepo.SearchAsync(oldTrim, limit: 5, ct).ConfigureAwait(false);
            var oldExact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal));
            if (oldExact is not null)
            {
                // 旧 alias 経由で結合済みの person_id を取得（PersonSeq=1 が主人物）。
                var rels = await _personAliasPersonsRepo.GetByAliasAsync(oldExact.AliasId, ct).ConfigureAwait(false);
                var primary = rels.OrderBy(r => r.PersonSeq).FirstOrDefault();
                if (primary is not null)
                {
                    // 同 person 配下に新 alias を追加。姓・名分割は新表記側に対して行う。
                    var newAlias = new PersonAlias
                    {
                        Name = name,
                        NameKana = null,
                        NameEn = null,
                        DisplayTextOverride = null,
                        Notes = null,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy,
                    };
                    int newAliasId = await _personAliasesRepo.InsertAsync(newAlias, ct).ConfigureAwait(false);

                    // 中間表に新 alias と person を紐付ける（共同名義は稀なので person_seq=1 固定で良い）。
                    await _personAliasPersonsRepo.UpsertAsync(new PersonAliasPerson
                    {
                        AliasId = newAliasId,
                        PersonId = primary.PersonId,
                        PersonSeq = 1,
                    }, ct).ConfigureAwait(false);

                    InfoMessages.Add(
                        $"✅ 「{oldTrim}」の人物（person_id={primary.PersonId}）に新名義「{name}」を別 alias として追加しました。");
                    return newAliasId;
                }
                // 旧 alias は見つかったが person 結合が無い（DB データ破損相当）→ 通常新規作成へフォールバック。
                InfoMessages.Add(
                    $"⚠ 旧名義「{oldTrim}」は見つかりましたが、結合先の人物が特定できませんでした。「{name}」を新規人物として登録します。");
            }
            else
            {
                InfoMessages.Add(
                    $"⚠ 「{oldTrim} => {name}」の旧名義が既存マスタに見つかりませんでした。タイポなら入力を見直してください。続行する場合「{name}」のみで新規人物として登録されます。");
            }
        }

        // 似て非なる類似度判定（リダイレクト無し or 旧側引き当て失敗で新規作成しようとしている表記が対象）。
        await WarnIfSimilarPersonAliasAsync(name, ct).ConfigureAwait(false);

        // 新規作成: 姓・名分割
        var (familyName, givenName) = SplitFamilyGivenName(name);

        return await _personsRepo.QuickAddWithSingleAliasAsync(
            fullName: name,
            fullNameKana: null,
            familyName: familyName,
            givenName: givenName,
            nameEn: null,
            notes: null,
            createdBy: updatedBy,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// キャラクター名義の引き当て or 新規作成。
    /// <paramref name="forceNew"/> が true の場合は同名既存キャラがあっても新規作成する（モブ用途）。
    /// <para>
    /// v1.3.0: <paramref name="oldName"/> が指定されていれば、既存 <c>character_aliases</c> から引き当てて
    /// <c>character_id</c> を取得し、同 character 配下に新 alias を追加登録する。
    /// 旧名義が引き当たらなければ警告 + 通常新規作成にフォールバック。<paramref name="forceNew"/> が true の場合は
    /// リダイレクトより強制新規が優先される（モブ用途のため、旧側参照を試みず必ず新規作成する）。
    /// </para>
    /// </summary>
    private async Task<int?> ResolveOrCreateCharacterAliasAsync(
        string? rawName, bool forceNew, string? oldName, string? updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        if (!forceNew)
        {
            var hits = await _characterAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
            var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
            if (exact is not null) return exact.AliasId;

            // v1.3.0: 「旧 => 新」記法で旧キャラ名義が指定されている場合、既存 character への新 alias 追加。
            if (!string.IsNullOrWhiteSpace(oldName))
            {
                string oldTrim = oldName!.Trim();
                var oldHits = await _characterAliasesRepo.SearchAsync(oldTrim, limit: 5, ct).ConfigureAwait(false);
                var oldExact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal));
                if (oldExact is not null)
                {
                    // character_aliases は character_id を直接列に持つので中間表は不要。
                    var newAlias = new CharacterAlias
                    {
                        CharacterId = oldExact.CharacterId,
                        Name = name,
                        NameKana = null,
                        NameEn = null,
                        Notes = null,
                        CreatedBy = updatedBy,
                        UpdatedBy = updatedBy,
                    };
                    int newAliasId = await _characterAliasesRepo.InsertAsync(newAlias, ct).ConfigureAwait(false);
                    InfoMessages.Add(
                        $"✅ 「{oldTrim}」のキャラクター（character_id={oldExact.CharacterId}）に新名義「{name}」を別 alias として追加しました。");
                    return newAliasId;
                }
                InfoMessages.Add(
                    $"⚠ 「{oldTrim} => {name}」の旧キャラ名義が既存マスタに見つかりませんでした。「{name}」を新規キャラとして登録します。");
            }

            // 似て非なる類似度判定（リダイレクト無し or 旧側引き当て失敗ケース）。
            await WarnIfSimilarCharacterAliasAsync(name, ct).ConfigureAwait(false);
        }

        // 新規作成: characters.character_kind は character_kinds マスタに必ず存在する値で投入する必要がある
        // （v1.2.0 工程 F-D で character_kind は ENUM → character_kinds 表への FK 参照に変更されており、
        //  マスタに無いコードを INSERT すると FK 制約 fk_characters_kind 違反で失敗する）。
        // 当該マスタには PRECURE / ALLY / VILLAIN / SUPPORTING の 4 類型のみが初期投入される。
        // 一括入力で自動追加されるキャラは多くの場合「名もなき脇役・モブ・取り巻き」のため、
        // 4 類型のうち意味が最も近い "SUPPORTING"（とりまく人々）を機械投入の既定値とする。
        // 主要キャラはユーザーが後でマスタ管理画面で PRECURE / ALLY / VILLAIN に変更可能。
        return await _charactersRepo.QuickAddWithSingleAliasAsync(
            characterName: name,
            characterNameKana: null,
            characterKindCode: "SUPPORTING",
            notes: null,
            createdBy: updatedBy,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 企業屋号の引き当て or 新規作成。
    /// <para>
    /// v1.3.0: <paramref name="oldName"/> が指定されていれば、既存 <c>company_aliases</c> から引き当てて
    /// <c>company_id</c> を取得し、同 company 配下に新屋号を追加登録する。
    /// </para>
    /// </summary>
    private async Task<int?> ResolveOrCreateCompanyAliasAsync(
        string? rawName, string? oldName, string? updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        var hits = await _companyAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
        var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        if (exact is not null) return exact.AliasId;

        // v1.3.0: 「旧 => 新」記法で旧屋号が指定されている場合、既存 company への新屋号追加を試みる。
        if (!string.IsNullOrWhiteSpace(oldName))
        {
            string oldTrim = oldName!.Trim();
            var oldHits = await _companyAliasesRepo.SearchAsync(oldTrim, limit: 5, ct).ConfigureAwait(false);
            var oldExact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal));
            if (oldExact is not null)
            {
                var newAlias = new CompanyAlias
                {
                    CompanyId = oldExact.CompanyId,
                    Name = name,
                    NameKana = null,
                    NameEn = null,
                    Notes = null,
                    CreatedBy = updatedBy,
                    UpdatedBy = updatedBy,
                };
                int newAliasId = await _companyAliasesRepo.InsertAsync(newAlias, ct).ConfigureAwait(false);
                InfoMessages.Add(
                    $"✅ 「{oldTrim}」の企業（company_id={oldExact.CompanyId}）に新屋号「{name}」を別 alias として追加しました。");
                return newAliasId;
            }
            InfoMessages.Add(
                $"⚠ 「{oldTrim} => {name}」の旧屋号が既存マスタに見つかりませんでした。「{name}」を新規企業として登録します。");
        }

        // 似て非なる類似度判定（リダイレクト無し or 旧側引き当て失敗ケース）。
        await WarnIfSimilarCompanyAliasAsync(name, ct).ConfigureAwait(false);

        return await _companiesRepo.QuickAddWithSingleAliasAsync(
            companyName: name,
            companyNameKana: null,
            companyNameEn: null,
            aliasName: name,
            aliasNameKana: null,
            createdBy: updatedBy,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 所属表記の解決：マスタに既存があればその alias_id、なければ rawText のまま保持
    /// （所属は気軽に新規作成しない＝企業マスタを意図せず増殖させない設計）。
    /// </summary>
    private async Task ResolveAffiliationAsync(
        string? raw, string? updatedBy, CancellationToken ct,
        Action<int?> setAliasId, Action<string?> setRawText)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            setAliasId(null); setRawText(null); return;
        }

        string name = raw.Trim();
        var hits = await _companyAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
        var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        if (exact is not null)
        {
            setAliasId(exact.AliasId); setRawText(null);
        }
        else
        {
            setAliasId(null); setRawText(name);
        }
    }

    /// <summary>
    /// 人物名を 姓・名 に分割する素朴ロジック。
    /// 半角SP区切り → family / given、「・」区切り → given / family（外国名想定）、区切りなし → 両方 null。
    /// </summary>
    private static (string? Family, string? Given) SplitFamilyGivenName(string fullName)
    {
        // 半角 SP / 全角 SP を許容
        int sp = fullName.IndexOf(' ');
        if (sp < 0) sp = fullName.IndexOf('\u3000');
        if (sp > 0)
        {
            string family = fullName[..sp].Trim();
            string given = fullName[(sp + 1)..].Trim();
            if (family.Length > 0 && given.Length > 0) return (family, given);
        }

        // 「・」区切り（外国名想定）→ given・family
        int mid = fullName.IndexOf('・');
        if (mid > 0)
        {
            string given = fullName[..mid].Trim();
            string family = fullName[(mid + 1)..].Trim();
            if (family.Length > 0 && given.Length > 0) return (family, given);
        }

        // 分割なし
        return (null, null);
    }

    // ─────────────────────────────────────────────────────────
    //  Draft オブジェクト生成ヘルパ
    // ─────────────────────────────────────────────────────────

    /// <summary>クレジット末尾に新しい <see cref="DraftCard"/> を作って追加する。</summary>
    private static DraftCard AppendNewCard(CreditDraftSession session, DraftCredit parent)
    {
        var card = new DraftCard
        {
            TempId = session.AllocateTempId(),
            State = DraftState.Added,
            Parent = parent,
            Entity = new CreditCard
            {
                CardSeq = (byte)(parent.Cards.Count + 1),
            }
        };
        parent.Cards.Add(card);

        // seed の Tier 1 / Group 1 も同時に作る（CreditCardsRepository.InsertAsync 側の挙動と整合）
        AppendNewTier(session, card);
        return card;
    }

    /// <summary>カード末尾に新しい <see cref="DraftTier"/> を作って追加する（Group 1 も seed する）。</summary>
    private static DraftTier AppendNewTier(CreditDraftSession session, DraftCard parent)
    {
        var tier = new DraftTier
        {
            TempId = session.AllocateTempId(),
            State = DraftState.Added,
            Parent = parent,
            Entity = new CreditCardTier
            {
                TierNo = (byte)(parent.Tiers.Count + 1),
            }
        };
        parent.Tiers.Add(tier);

        AppendNewGroup(session, tier);
        return tier;
    }

    /// <summary>Tier 末尾に新しい <see cref="DraftGroup"/> を作って追加する。</summary>
    private static DraftGroup AppendNewGroup(CreditDraftSession session, DraftTier parent)
    {
        var group = new DraftGroup
        {
            TempId = session.AllocateTempId(),
            State = DraftState.Added,
            Parent = parent,
            Entity = new CreditCardGroup
            {
                GroupNo = (byte)(parent.Groups.Count + 1),
            }
        };
        parent.Groups.Add(group);
        return group;
    }

    /// <summary>Group 末尾に新しい <see cref="DraftRole"/> を作って追加する。</summary>
    private static DraftRole AppendNewRole(CreditDraftSession session, DraftGroup parent, string roleCode)
    {
        var role = new DraftRole
        {
            TempId = session.AllocateTempId(),
            State = DraftState.Added,
            Parent = parent,
            Entity = new CreditCardRole
            {
                RoleCode = roleCode,
                OrderInGroup = (byte)(parent.Roles.Count + 1),
            }
        };
        parent.Roles.Add(role);
        return role;
    }

    /// <summary>役職末尾に新しい <see cref="DraftBlock"/> を作って追加する。</summary>
    private static DraftBlock AppendNewBlock(CreditDraftSession session, DraftRole parent, byte colCount)
    {
        var block = new DraftBlock
        {
            TempId = session.AllocateTempId(),
            State = DraftState.Added,
            Parent = parent,
            Entity = new CreditRoleBlock
            {
                BlockSeq = (byte)(parent.Blocks.Count + 1),
                ColCount = colCount < 1 ? (byte)1 : colCount,
            }
        };
        parent.Blocks.Add(block);
        return block;
    }

    /// <summary>ブロック末尾に PERSON エントリを 1 件追加する。</summary>
    private static void AppendPersonEntry(
        CreditDraftSession session, DraftBlock parent,
        int personAliasId, int? affCompanyAliasId, string? affRawText)
    {
        AppendEntry(session, parent, new CreditBlockEntry
        {
            EntryKind = "PERSON",
            EntrySeq = (ushort)(parent.Entries.Count + 1),
            PersonAliasId = personAliasId,
            AffiliationCompanyAliasId = affCompanyAliasId,
            AffiliationText = affRawText,
        });
    }

    /// <summary>ブロック末尾に CHARACTER_VOICE エントリを 1 件追加する。</summary>
    private static void AppendCharacterVoiceEntry(
        CreditDraftSession session, DraftBlock parent,
        int personAliasId, int? characterAliasId, string? rawCharacterText,
        int? affCompanyAliasId, string? affRawText)
    {
        AppendEntry(session, parent, new CreditBlockEntry
        {
            EntryKind = "CHARACTER_VOICE",
            EntrySeq = (ushort)(parent.Entries.Count + 1),
            PersonAliasId = personAliasId,
            CharacterAliasId = characterAliasId,
            RawCharacterText = rawCharacterText,
            AffiliationCompanyAliasId = affCompanyAliasId,
            AffiliationText = affRawText,
        });
    }

    /// <summary>ブロック末尾に COMPANY エントリを 1 件追加する。</summary>
    private static void AppendCompanyEntry(CreditDraftSession session, DraftBlock parent, int companyAliasId)
    {
        AppendEntry(session, parent, new CreditBlockEntry
        {
            EntryKind = "COMPANY",
            EntrySeq = (ushort)(parent.Entries.Count + 1),
            CompanyAliasId = companyAliasId,
        });
    }

    /// <summary>
    /// ブロック末尾に LOGO エントリを 1 件追加する（v1.2.2 追加）。
    /// <c>credit_block_entries.entry_kind = "LOGO"</c> + <c>logo_id</c> 必須。
    /// </summary>
    private static void AppendLogoEntry(CreditDraftSession session, DraftBlock parent, int logoId)
    {
        AppendEntry(session, parent, new CreditBlockEntry
        {
            EntryKind = "LOGO",
            EntrySeq = (ushort)(parent.Entries.Count + 1),
            LogoId = logoId,
        });
    }

    /// <summary>ブロック末尾に TEXT エントリ（マスタ未登録の退避口）を 1 件追加する。</summary>
    private static void AppendTextEntry(CreditDraftSession session, DraftBlock parent, string raw)
    {
        AppendEntry(session, parent, new CreditBlockEntry
        {
            EntryKind = "TEXT",
            EntrySeq = (ushort)(parent.Entries.Count + 1),
            RawText = raw,
        });
    }

    /// <summary>共通：ブロックに <see cref="CreditBlockEntry"/> を 1 件 append する。</summary>
    private static void AppendEntry(CreditDraftSession session, DraftBlock parent, CreditBlockEntry entity)
    {
        var entry = new DraftEntry
        {
            TempId = session.AllocateTempId(),
            State = DraftState.Added,
            Parent = parent,
            Entity = entity,
        };
        parent.Entries.Add(entry);
    }

    // ════════════════════════════════════════════════════════════════════
    //   v1.3.0 追加: 似て非なる名義の類似度判定ヘルパ群
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 「似て非なる」判定の閾値（v1.3.0 追加）。
    /// 「空白を除いた文字数のうち過半数が一致するも完全一致ではない」というユーザー要件に対応するため、
    /// LCS（最長共通部分列）の長さを <c>max(len(A), len(B))</c> で割った比率で評価する。
    /// 0.5 ちょうどを含めた「過半数」判定（&gt;= 0.5）。
    /// </summary>
    private const double SimilarityThreshold = 0.5;

    /// <summary>
    /// 比較用の文字列正規化（空白除去）。
    /// 半角スペース・全角スペース・タブ・各種空白文字をすべて除去する。
    /// 「五條 真由美」と「五条真由美」のように空白の有無による表記揺れを吸収するための前処理。
    /// </summary>
    private static string NormalizeForCompare(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            // char.IsWhiteSpace は半角・全角スペース両方を拾う（U+3000 全角スペースも対象）。
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 2 文字列の最長共通部分列（LCS）の長さを返す。
    /// 動的計画法 O(|A|×|B|) 実装。日本語名義は最大数十文字なので性能的に問題なし。
    /// </summary>
    private static int LongestCommonSubsequenceLength(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                dp[i, j] = (a[i - 1] == b[j - 1])
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }
        return dp[a.Length, b.Length];
    }

    /// <summary>
    /// 「正規化後 完全一致ではないが LCS 比率が閾値以上」を判定する（v1.3.0 追加）。
    /// 完全一致は呼び出し側で既に除外されている前提だが、空白違いだけの「実質完全一致」は
    /// 警告対象から除外したいので、ここで正規化後完全一致もスキップする。
    /// </summary>
    private static bool IsSimilarNonExact(string normalizedRaw, string targetName)
    {
        if (string.IsNullOrEmpty(normalizedRaw)) return false;
        var targetNorm = NormalizeForCompare(targetName);
        if (targetNorm.Length == 0) return false;
        // 空白違いだけで本質同名 → 警告対象から除外
        if (string.Equals(normalizedRaw, targetNorm, StringComparison.Ordinal)) return false;

        int lcs = LongestCommonSubsequenceLength(normalizedRaw, targetNorm);
        int max = Math.Max(normalizedRaw.Length, targetNorm.Length);
        if (max == 0) return false;
        return (double)lcs / max >= SimilarityThreshold;
    }

    /// <summary>
    /// 人物名義の全件キャッシュを返す（初回呼び出し時に lazy load）。
    /// 1 適用フェーズ中は再ロードしないことで、N×M の全件比較を 1 回のロードで済ませる。
    /// </summary>
    private async Task<IReadOnlyList<PersonAlias>> GetAllPersonAliasesCachedAsync(CancellationToken ct)
    {
        if (_allPersonAliasesCache is null)
        {
            _allPersonAliasesCache = await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct: ct).ConfigureAwait(false);
        }
        return _allPersonAliasesCache;
    }

    /// <summary>キャラクター名義の全件キャッシュを返す（初回呼び出し時に lazy load）。</summary>
    private async Task<IReadOnlyList<CharacterAlias>> GetAllCharacterAliasesCachedAsync(CancellationToken ct)
    {
        if (_allCharacterAliasesCache is null)
        {
            _allCharacterAliasesCache = await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct: ct).ConfigureAwait(false);
        }
        return _allCharacterAliasesCache;
    }

    /// <summary>企業屋号の全件キャッシュを返す（初回呼び出し時に lazy load）。</summary>
    private async Task<IReadOnlyList<CompanyAlias>> GetAllCompanyAliasesCachedAsync(CancellationToken ct)
    {
        if (_allCompanyAliasesCache is null)
        {
            _allCompanyAliasesCache = await _companyAliasesRepo.GetAllAsync(includeDeleted: false, ct: ct).ConfigureAwait(false);
        }
        return _allCompanyAliasesCache;
    }

    /// <summary>
    /// 進捗報告の発火頻度（v1.3.0 追加）。
    /// 全件比較中、毎件発火すると UI スレッドに負担がかかるため、約 50 件ごとに 1 回イベントを上げる。
    /// 全体件数が 50 未満の場合は最後に 1 回だけ「完了」を発火する。
    /// </summary>
    private const int CompareProgressTick = 50;

    /// <summary>人物名義の似て非なる警告（v1.3.0 追加）。</summary>
    private async Task WarnIfSimilarPersonAliasAsync(string rawName, CancellationToken ct)
    {
        var all = await GetAllPersonAliasesCachedAsync(ct).ConfigureAwait(false);
        if (all.Count == 0) return;

        string normalizedRaw = NormalizeForCompare(rawName);
        if (normalizedRaw.Length == 0) return;

        int total = all.Count;
        for (int i = 0; i < total; i++)
        {
            if (i % CompareProgressTick == 0) CompareProgress?.Invoke(i, total);
            var a = all[i];
            // 完全一致は呼び出し側で先に除外済み（SearchAsync 完全一致 hit パス）。
            if (string.Equals(a.Name, rawName, StringComparison.Ordinal)) continue;
            if (IsSimilarNonExact(normalizedRaw, a.Name))
            {
                InfoMessages.Add(
                    $"⚠ 新規登録予定の人物名義「{rawName}」は既存名義「{a.Name}」（alias_id={a.AliasId}）と類似しています。漢字違い・空白違いの可能性があります。同一人物なら「旧名義 => 新名義」記法で書くか、マスタ管理画面で別名義として統合してください。");
            }
        }
        CompareProgress?.Invoke(total, total);
    }

    /// <summary>キャラクター名義の似て非なる警告（v1.3.0 追加）。</summary>
    private async Task WarnIfSimilarCharacterAliasAsync(string rawName, CancellationToken ct)
    {
        var all = await GetAllCharacterAliasesCachedAsync(ct).ConfigureAwait(false);
        if (all.Count == 0) return;

        string normalizedRaw = NormalizeForCompare(rawName);
        if (normalizedRaw.Length == 0) return;

        int total = all.Count;
        for (int i = 0; i < total; i++)
        {
            if (i % CompareProgressTick == 0) CompareProgress?.Invoke(i, total);
            var a = all[i];
            if (string.Equals(a.Name, rawName, StringComparison.Ordinal)) continue;
            if (IsSimilarNonExact(normalizedRaw, a.Name))
            {
                InfoMessages.Add(
                    $"⚠ 新規登録予定のキャラクター名義「{rawName}」は既存名義「{a.Name}」（alias_id={a.AliasId}, character_id={a.CharacterId}）と類似しています。同一キャラなら「旧名義 => 新名義」記法で書くか、マスタ管理画面で別名義として統合してください。");
            }
        }
        CompareProgress?.Invoke(total, total);
    }

    /// <summary>企業屋号の似て非なる警告（v1.3.0 追加）。LOGO の屋号引き当て失敗時の警告にも兼用。</summary>
    private async Task WarnIfSimilarCompanyAliasAsync(string rawName, CancellationToken ct)
    {
        var all = await GetAllCompanyAliasesCachedAsync(ct).ConfigureAwait(false);
        if (all.Count == 0) return;

        string normalizedRaw = NormalizeForCompare(rawName);
        if (normalizedRaw.Length == 0) return;

        int total = all.Count;
        for (int i = 0; i < total; i++)
        {
            if (i % CompareProgressTick == 0) CompareProgress?.Invoke(i, total);
            var a = all[i];
            if (string.Equals(a.Name, rawName, StringComparison.Ordinal)) continue;
            if (IsSimilarNonExact(normalizedRaw, a.Name))
            {
                InfoMessages.Add(
                    $"⚠ 新規登録予定の企業屋号「{rawName}」は既存屋号「{a.Name}」（alias_id={a.AliasId}, company_id={a.CompanyId}）と類似しています。同一企業なら「旧屋号 => 新屋号」記法で書くか、マスタ管理画面で別屋号として統合してください。");
            }
        }
        CompareProgress?.Invoke(total, total);
    }
}
