using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// <see cref="BulkParseResult"/> を Draft レイヤ（<see cref="DraftCredit"/>）へ流し込む適用サービス（v1.2.1 追加）。
/// <para>
/// 役割（高位フロー）:
/// <list type="number">
///   <item><description><b>名前解決フェーズ</b>: 役職名を <c>roles</c> に引き当てて role_code / role_format_kind を確定。
///     person_aliases / character_aliases / company_aliases から alias_id を引き当て。
///     未解決の役職は呼び出し側に「QuickAdd ダイアログを 1 件ずつ開く」ことを依頼するための
///     <see cref="UnresolvedRoles"/> リストを返す。</description></item>
///   <item><description><b>マスタ自動投入フェーズ</b>: 未解決の Person / Character / Company は
///     既存の <c>QuickAddWithSingleAliasAsync</c> を呼んで自動登録する（ユーザー確認なし）。</description></item>
///   <item><description><b>Draft 注入フェーズ</b>: <see cref="ApplyToDraft"/> を実行すると、
///     既存の <see cref="DraftCredit"/> の末尾にカード／Tier／Group／Role／Block／Entry を追加する。
///     ただし「ロール 0 件のカード 1 枚しかない」状態のときは、その空カードを上書きしてから始める。</description></item>
/// </list>
/// </para>
/// <para>
/// マスタに引き当て不能な人物名・キャラ名・企業名は <see cref="ParsedEntryKind.Text"/> に降格させ、
/// <see cref="CreditBlockEntry.RawText"/> として保持する。後で人手で人物作成 → 紐付け直しできる。
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
    /// 名前解決の結果として未解決だった役職表示名のリスト（<see cref="ResolveAsync"/> 後に参照）。
    /// 呼び出し側はこのリストを使って <c>QuickAddRoleDialog</c> をループ起動し、
    /// ユーザーに role_code / name_en / role_format_kind を入力させる想定。
    /// </summary>
    public List<ParsedRole> UnresolvedRoles { get; } = new();

    /// <summary>名前解決時に蓄積された情報メッセージ（複数ヒット警告など）。</summary>
    public List<string> InfoMessages { get; } = new();

    public CreditBulkApplyService(
        RolesRepository rolesRepo,
        PersonsRepository personsRepo,
        PersonAliasesRepository personAliasesRepo,
        CharactersRepository charactersRepo,
        CharacterAliasesRepository characterAliasesRepo,
        CompaniesRepository companiesRepo,
        CompanyAliasesRepository companyAliasesRepo)
    {
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
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

    /// <summary>パース結果の 1 カード分を <see cref="DraftCard"/> に流し込む。</summary>
    private async Task ApplyCardAsync(
        ParsedCard pc, CreditDraftSession session, DraftCard targetCard,
        string? updatedBy, CancellationToken ct)
    {
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
        foreach (var pr in pg.Roles)
        {
            if (pr.ResolvedRoleCode is null) continue; // 未解決はスキップ（呼び出し側が事前に解消する想定）

            // 役職を新規追加（同 group 内で重複役職は許容、Draft では既存 role を再利用しない＝末尾追加で素直に）
            var role = AppendNewRole(session, targetGroup, pr.ResolvedRoleCode);

            // ─── ブロック群を流し込む ───
            foreach (var pb in pr.Blocks)
            {
                if (pb.Rows.Count == 0 && pb.LeadingCompanyText is null) continue;

                var block = AppendNewBlock(session, role, (byte)pb.ColCount);

                // [先頭企業屋号]
                if (!string.IsNullOrEmpty(pb.LeadingCompanyText))
                {
                    int? leadingAliasId = await ResolveOrCreateCompanyAliasAsync(
                        pb.LeadingCompanyText, updatedBy, ct).ConfigureAwait(false);
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
                    foreach (var pe in row.Entries)
                    {
                        await AppendParsedEntryAsync(session, block, pe, pr, updatedBy, ct).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    /// <summary>1 つの <see cref="ParsedEntry"/> を解決して Draft の Block にエントリ追加する。</summary>
    private async Task AppendParsedEntryAsync(
        CreditDraftSession session, DraftBlock block, ParsedEntry pe, ParsedRole pr,
        string? updatedBy, CancellationToken ct)
    {
        switch (pe.Kind)
        {
            case ParsedEntryKind.CharacterVoice:
            {
                // キャラ alias 引き当て or 新規作成
                int? characterAliasId = await ResolveOrCreateCharacterAliasAsync(
                    pe.CharacterRawText, pe.IsForcedNewCharacter, updatedBy, ct).ConfigureAwait(false);

                // 声優 alias 引き当て or 新規作成
                int? personAliasId = await ResolveOrCreatePersonAliasAsync(
                    pe.PersonRawText, updatedBy, ct).ConfigureAwait(false);

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
                    return;
                }

                AppendCharacterVoiceEntry(session, block,
                    personAliasId.Value, characterAliasId,
                    rawCharacterText: characterAliasId is null ? pe.CharacterRawText : null,
                    affCompanyAliasId, affRawText);
                return;
            }

            case ParsedEntryKind.Company:
            {
                int? companyAliasId = await ResolveOrCreateCompanyAliasAsync(
                    pe.CompanyRawText, updatedBy, ct).ConfigureAwait(false);

                if (companyAliasId is null)
                {
                    // 企業名空文字など解決不能 → TEXT 退避。
                    AppendTextEntry(session, block, $"[{pe.CompanyRawText}]");
                    return;
                }
                AppendCompanyEntry(session, block, companyAliasId.Value);
                return;
            }

            case ParsedEntryKind.Person:
            default:
            {
                // VOICE_CAST 役職に PERSON エントリが現れた場合は CHARACTER_VOICE への降格は行わない
                // （パーサ側で既に <X> なし行は警告済みのため、ここに来るのは PERSON が確実な局面）。

                int? personAliasId = await ResolveOrCreatePersonAliasAsync(
                    pe.PersonRawText, updatedBy, ct).ConfigureAwait(false);

                int? affCompanyAliasId = null;
                string? affRawText = null;
                await ResolveAffiliationAsync(pe.AffiliationRawText, updatedBy, ct,
                    aliasId => affCompanyAliasId = aliasId,
                    raw => affRawText = raw).ConfigureAwait(false);

                if (personAliasId is null)
                {
                    AppendTextEntry(session, block, pe.PersonRawText ?? "");
                    return;
                }
                AppendPersonEntry(session, block, personAliasId.Value, affCompanyAliasId, affRawText);
                return;
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  マスタ引き当て + 自動 QuickAdd
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// 人物名義の引き当て or 新規作成。空文字 / 空白なら null を返す。
    /// <para>
    /// 「人物名 半角SP区切り → family/given を分割保存」「・区切り → given・family」「区切りなし → full のみ」
    /// の規則で <see cref="PersonsRepository.QuickAddWithSingleAliasAsync"/> を呼ぶ。
    /// </para>
    /// </summary>
    private async Task<int?> ResolveOrCreatePersonAliasAsync(
        string? rawName, string? updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        // 既存検索（name 完全一致）
        var hits = await _personAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
        var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        if (exact is not null) return exact.AliasId;

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
    /// </summary>
    private async Task<int?> ResolveOrCreateCharacterAliasAsync(
        string? rawName, bool forceNew, string? updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        if (!forceNew)
        {
            var hits = await _characterAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
            var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
            if (exact is not null) return exact.AliasId;
        }

        // 新規作成: characters.character_kind は最低限の既定値 "MOB" で投入する想定
        // （v1.2.0 工程 F でマスタ化、ここでは UI 経由の choose ではなく機械投入なのでデフォルトを使う）
        return await _charactersRepo.QuickAddWithSingleAliasAsync(
            characterName: name,
            characterNameKana: null,
            characterKindCode: "MOB",
            notes: null,
            createdBy: updatedBy,
            ct: ct).ConfigureAwait(false);
    }

    /// <summary>企業屋号の引き当て or 新規作成。</summary>
    private async Task<int?> ResolveOrCreateCompanyAliasAsync(
        string? rawName, string? updatedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        var hits = await _companyAliasesRepo.SearchAsync(name, limit: 5, ct).ConfigureAwait(false);
        var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
        if (exact is not null) return exact.AliasId;

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
}
