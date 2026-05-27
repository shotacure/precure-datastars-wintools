using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// <see cref="BulkParseResult"/> を Draft レイヤ（<see cref="DraftCredit"/>）へ流し込む適用サービス（拡張）。
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
/// マスタに引き当て不能な人物名・キャラ名・企業名は <see cref="ParsedEntryKind.Text"/> に降格させ、
/// <see cref="CreditBlockEntry.RawText"/> として保持する。後で人手で人物作成 → 紐付け直しできる。
/// 追加された機能:
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

    /// <summary>LOGO エントリ（<c>[屋号#CIバージョン]</c>）の引き当て用リポジトリ。 屋号 alias_id + CI バージョンラベルで <c>logos</c> テーブルを引き、合致する logo_id を返す。</summary>
    private readonly LogosRepository _logosRepo;

    /// <summary>「旧名義 =&gt; 新名義」記法で、既存人物への新 alias 追加を行う際に 必要となる中間表 person_alias_persons 用リポジトリ。</summary>
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;

    /// <summary>似て非なる名義の比較進捗を呼び出し側に通知するイベント。</summary>
    public event Action<int, int>? CompareProgress;

    // 似て非なる名義判定用のマスタキャッシュ
    // 1 適用フェーズ中、各エントリの名義引き当てごとに毎回 GetAllAsync を呼ぶと
    // N×M の DB アクセスになるため、最初の 1 回だけ取得してフィールドにキャッシュする。
    // ResolveAsync の冒頭でキャッシュをクリアし、必要時に lazy load する。
    private IReadOnlyList<PersonAlias>? _allPersonAliasesCache;
    private IReadOnlyList<CharacterAlias>? _allCharacterAliasesCache;
    private IReadOnlyList<CompanyAlias>? _allCompanyAliasesCache;

    /// <summary>役職マスタの全件キャッシュ。 プレビュー時の「未登録役職の新規登録候補警告」で参照する。</summary>
    private IReadOnlyList<Role>? _allRolesCache;

    /// <summary>名前解決の結果として未解決だった役職表示名のリスト（<see cref="ResolveAsync"/> 後に参照）。 呼び出し側はこのリストを使って <c>QuickAddRoleDialog</c> をループ起動し、 ユーザーに role_code / name_en / role_format_kind を入力させる想定。</summary>
    public List<ParsedRole> UnresolvedRoles { get; } = new();

    /// <summary>名前解決の結果として未解決だった所属屋号のリスト。Apply 経路で `(屋号)` 記法によりマスタ引き当てを
    /// 試みたが見つからなかった場合に積まれる。呼び出し側（CreditEditorForm 警告ペイン）はこれを警告化し、
    /// ダブルクリックで <c>QuickAddCompanyAliasDialog</c> を起動して登録 → 再パース、というフローを駆動する。
    /// クオート記法 <c>("...")</c> による強制テキストは引き当てを試みないため、ここには積まれない。</summary>
    public List<UnresolvedAffiliation> UnresolvedAffiliations { get; } = new();

    /// <summary>名前解決時に蓄積された情報メッセージ（複数ヒット警告など）。</summary>
    public List<string> InfoMessages { get; } = new();

    /// <summary>
    /// 「未登録役職警告」の同 Apply 内重複抑制用。
    /// 同じ (person_alias_id, role_code) の組み合わせは 1 度だけ警告する。
    /// <see cref="ResolveAsync"/> 冒頭でクリアされる。
    /// </summary>
    private readonly HashSet<(int aliasId, string roleCode)> _warnedRoleCombos = new();

    /// <summary>
    /// <see cref="CreditBulkApplyService"/> の新しいインスタンスを構築する。
    /// <para><paramref name="logosRepo"/> 引数を追加（LOGO エントリ解決用）。</para>
    /// <para><paramref name="personAliasPersonsRepo"/> 引数を追加（「旧 =&gt; 新」記法による
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

    //  名前解決フェーズ

    /// <summary>
    /// パース結果の役職名・人物名・キャラ名・企業名をマスタに引き当てる。
    /// 役職は表示名と <c>roles.name_ja</c> の完全一致を優先し、見つからなければ <see cref="UnresolvedRoles"/> に積む。
    /// 引き当てできた役職には <see cref="ParsedRole.ResolvedRoleCode"/> /
    /// <see cref="ParsedRole.ResolvedFormatKind"/> をセットする。
    /// 人物・キャラ・企業の解決は <see cref="ApplyToDraftAsync"/> 内で行う（マスタ自動追加が伴うため、
    /// 同じトランザクション境界で扱いたい）。本メソッドは役職解決のみを担当する。
    /// </summary>
    public async Task ResolveAsync(BulkParseResult parsed, CancellationToken ct = default)
    {
        UnresolvedRoles.Clear();
        UnresolvedAffiliations.Clear();
        InfoMessages.Clear();

        // 似て非なる名義判定用のキャッシュを毎回クリアする。
        // 別ダイアログ起動間でキャッシュが残っているとマスタ更新が反映されないため。
        _allPersonAliasesCache = null;
        _allCharacterAliasesCache = null;
        _allCompanyAliasesCache = null;
        // 役職マスタキャッシュも同様にクリア。
        _allRolesCache = null;
        // 未登録役職警告の重複抑制セットもクリア（別ダイアログで同じ警告が黙殺されないようにする）。
        _warnedRoleCombos.Clear();

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

    /// <summary>未解決役職に対してユーザーが入力した結果を反映する。 呼び出し側は QuickAddRoleDialog を 1 件ずつ起動し、確定したら本メソッドで結果を流し込む。</summary>
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

    //  リアルタイム類似度判定（プレビュー用）

    /// <summary>
    /// パース結果から「新規作成予定の名義」を抜き出し、既存マスタとの類似度警告を
    /// <see cref="BulkParseResult.Warnings"/> に <see cref="ParseWarning"/> として追加する。
    /// プレビュー時にリアルタイム呼び出しすることで、ユーザーが入力中に「ちょっと違う既存名義」を
    /// 警告ペインで確認できるようにする。Apply 経路の <see cref="ResolveOrCreatePersonAliasAsync"/> 等が出す
    /// <see cref="InfoMessages"/> とは別経路。InfoMessages は Apply 完了後の MessageBox 用、こちらは
    /// 警告ペイン（lstWarnings）への即時反映用。
    /// 「新規作成予定」の判定基準:
    /// <list type="bullet">
    ///   <item><description>各 Entry の PersonRawText / CharacterRawText / CompanyRawText のうち、
    ///     対応する OldName（=&gt; 記法のリダイレクト）が指定されていないもの</description></item>
    ///   <item><description>SearchAsync 完全一致でヒットしないもの（既存名義そのまま使用なら警告不要）</description></item>
    ///   <item><description>強制新規キャラ（&lt;*X&gt;）はキャラの類似度判定対象外（モブ用途で意図的に新規作成のため）</description></item>
    /// </list>
    /// 同名重複は HashSet で 1 度だけ評価し、警告も同名につき 1 回だけ積む。
    /// </summary>
    public async Task CheckSimilarNamesAsync(BulkParseResult parsed, CancellationToken ct = default)
    {
        if (parsed is null) return;
        if (parsed.IsEmpty) return;

        // ─── 役職の未登録チェック ───
        await CheckUnregisteredRolesAsync(parsed, ct).ConfigureAwait(false);

        // 名義ごとに「最初に出現した行番号」を覚えておき、警告メッセージで該当行に紐付ける。
        var personNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var characterNames = new Dictionary<string, int>(StringComparer.Ordinal);
        var companyNames = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var card in parsed.Cards)
        foreach (var tier in card.Tiers)
        foreach (var group in tier.Groups)
        foreach (var role in group.Roles)
        foreach (var block in role.Blocks)
        foreach (var row in block.Rows)
        foreach (var entry in row.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // PERSON: PersonOldName が指定されているリダイレクト経由は警告対象外（既存への新 alias 追加）。
            if (entry.PersonOldName is null && !string.IsNullOrWhiteSpace(entry.PersonRawText))
            {
                string name = entry.PersonRawText.Trim();
                if (name.Length > 0 && !personNames.ContainsKey(name))
                {
                    personNames[name] = entry.LineNumber;
                }
            }

            // CHARACTER: CharacterOldName 指定または IsForcedNewCharacter（モブ）は対象外。
            if (entry.CharacterOldName is null && !entry.IsForcedNewCharacter
                && !string.IsNullOrWhiteSpace(entry.CharacterRawText))
            {
                string name = entry.CharacterRawText.Trim();
                if (name.Length > 0 && !characterNames.ContainsKey(name))
                {
                    characterNames[name] = entry.LineNumber;
                }
            }

            // COMPANY / LOGO の屋号部: CompanyOldName が指定されているリダイレクト経由は対象外。
            if (entry.CompanyOldName is null && !string.IsNullOrWhiteSpace(entry.CompanyRawText))
            {
                string name = entry.CompanyRawText.Trim();
                if (name.Length > 0 && !companyNames.ContainsKey(name))
                {
                    companyNames[name] = entry.LineNumber;
                }
            }
        }

        // 各名義について SearchAsync 完全一致を確認し、未ヒットなら全件比較で類似判定を実施。
        // 内部で GetAllXxxAliasesCachedAsync を呼ぶので、1 ダイアログセッション中の最初の呼び出し時のみ
        // 全件取得が走り、以降はキャッシュ。CompareProgress イベントでキャッシュ取得・比較中の進捗が出る。
        foreach (var (name, lineNo) in personNames)
        {
            ct.ThrowIfCancellationRequested();
            await CheckSimilarPersonForParseAsync(name, lineNo, parsed, ct).ConfigureAwait(false);
        }
        foreach (var (name, lineNo) in characterNames)
        {
            ct.ThrowIfCancellationRequested();
            await CheckSimilarCharacterForParseAsync(name, lineNo, parsed, ct).ConfigureAwait(false);
        }
        foreach (var (name, lineNo) in companyNames)
        {
            ct.ThrowIfCancellationRequested();
            await CheckSimilarCompanyForParseAsync(name, lineNo, parsed, ct).ConfigureAwait(false);
        }
    }

    /// <summary>人物名義のリアルタイム類似度判定。 FindByExactNameAsync で完全一致（および空白除去後一致）が拾えるなら何もせず終了。 完全一致なしのときだけ、全件キャッシュとの LCS 比較で 似て非なる候補を <see cref="BulkParseResult.Warnings"/> に積む。 似て非なる候補も 1 件もなければ「新規登録候補」として情報レベルで警告に積む。</summary>
    private async Task CheckSimilarPersonForParseAsync(
        string name, int lineNo, BulkParseResult parsed, CancellationToken ct)
    {
        // 完全一致確認（既存名義そのまま使うなら警告不要）。
        // 空白除去後の一致もここで吸収する（FindByExactNameAsync は REPLACE 経由で半角・全角 SP を無視する）。
        var hits = await _personAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
        if (hits.Count > 0) return;

        var all = await GetAllPersonAliasesCachedAsync(ct).ConfigureAwait(false);
        string normalizedRaw = NormalizeForCompare(name);

        bool similarFound = false;
        if (all.Count > 0 && normalizedRaw.Length > 0)
        {
            int total = all.Count;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % CompareProgressTick == 0) CompareProgress?.Invoke(i, total);
                var a = all[i];
                if (string.Equals(a.Name, name, StringComparison.Ordinal)) continue;
                if (IsLikelyTypo(normalizedRaw, a.Name))
                {
                    parsed.Warnings.Add(new ParseWarning
                    {
                        Severity = WarningSeverity.Warning,
                        LineNumber = lineNo,
                        Message = $"{lineNo} 行目: 人物名義「{name}」は既存名義「{a.Name}」（alias_id={a.AliasId}）と 1 字違い（同じ文字種構成）です。誤字の可能性があります。同一人物なら「{a.Name} => {name}」で書くか、マスタ管理画面で別名義として統合してください。"
                    });
                    similarFound = true;
                }
            }
            CompareProgress?.Invoke(total, total);
        }

        // 似て非なる候補が 0 件なら「新規登録候補」として情報レベルで警告。
        // 似て非なる候補ありの場合は類似警告に含意されているため、こちらは出さない（重複回避）。
        if (!similarFound)
        {
            parsed.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Info,
                LineNumber = lineNo,
                Message = $"{lineNo} 行目: 人物名義「{name}」は新規登録候補です（マスタに既存名義および類似名義なし）。Apply 時に新規 person + alias を作成します。"
            });
        }
    }

    /// <summary>キャラクター名義のリアルタイム類似度判定。</summary>
    private async Task CheckSimilarCharacterForParseAsync(
        string name, int lineNo, BulkParseResult parsed, CancellationToken ct)
    {
        // 空白除去後の一致も完全一致として扱う（FindByExactNameAsync の SQL が REPLACE 経由で吸収）。
        var hits = await _characterAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
        if (hits.Count > 0) return;

        var all = await GetAllCharacterAliasesCachedAsync(ct).ConfigureAwait(false);
        string normalizedRaw = NormalizeForCompare(name);

        bool similarFound = false;
        if (all.Count > 0 && normalizedRaw.Length > 0)
        {
            int total = all.Count;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % CompareProgressTick == 0) CompareProgress?.Invoke(i, total);
                var a = all[i];
                if (string.Equals(a.Name, name, StringComparison.Ordinal)) continue;
                if (IsLikelyTypo(normalizedRaw, a.Name))
                {
                    parsed.Warnings.Add(new ParseWarning
                    {
                        Severity = WarningSeverity.Warning,
                        LineNumber = lineNo,
                        Message = $"{lineNo} 行目: キャラクター名義「{name}」は既存名義「{a.Name}」（alias_id={a.AliasId}, character_id={a.CharacterId}）と 1 字違い（同じ文字種構成）です。誤字の可能性があります。同一キャラなら「{a.Name} => {name}」で書くか、マスタ管理画面で別名義として統合してください。"
                    });
                    similarFound = true;
                }
            }
            CompareProgress?.Invoke(total, total);
        }

        if (!similarFound)
        {
            parsed.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Info,
                LineNumber = lineNo,
                Message = $"{lineNo} 行目: キャラクター名義「{name}」は新規登録候補です（マスタに既存名義および類似名義なし）。Apply 時に新規 character + alias を作成します（character_kind=SUPPORTING で投入、後でマスタ管理画面で変更可）。"
            });
        }
    }

    /// <summary>企業屋号のリアルタイム類似度判定。LOGO の屋号部分にも兼用。</summary>
    private async Task CheckSimilarCompanyForParseAsync(
        string name, int lineNo, BulkParseResult parsed, CancellationToken ct)
    {
        // 空白除去後の一致も完全一致として扱う（FindByExactNameAsync の SQL が REPLACE 経由で吸収）。
        var hits = await _companyAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
        if (hits.Count > 0) return;

        var all = await GetAllCompanyAliasesCachedAsync(ct).ConfigureAwait(false);
        string normalizedRaw = NormalizeForCompare(name);

        bool similarFound = false;
        if (all.Count > 0 && normalizedRaw.Length > 0)
        {
            int total = all.Count;
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % CompareProgressTick == 0) CompareProgress?.Invoke(i, total);
                var a = all[i];
                if (string.Equals(a.Name, name, StringComparison.Ordinal)) continue;
                if (IsLikelyTypo(normalizedRaw, a.Name))
                {
                    parsed.Warnings.Add(new ParseWarning
                    {
                        Severity = WarningSeverity.Warning,
                        LineNumber = lineNo,
                        Message = $"{lineNo} 行目: 企業屋号「{name}」は既存屋号「{a.Name}」（alias_id={a.AliasId}, company_id={a.CompanyId}）と 1 字違い（同じ文字種構成）です。誤字の可能性があります。同一企業なら「{a.Name} => {name}」で書くか、マスタ管理画面で別屋号として統合してください。"
                    });
                    similarFound = true;
                }
            }
            CompareProgress?.Invoke(total, total);
        }

        if (!similarFound)
        {
            parsed.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Info,
                LineNumber = lineNo,
                Message = $"{lineNo} 行目: 企業屋号「{name}」は新規登録候補です（マスタに既存屋号および類似屋号なし）。Apply 時に新規 company + alias を作成します。"
            });
        }
    }

    /// <summary>パース結果中の役職表示名を roles.name_ja と完全一致比較し、未登録の役職を Warnings に「新規登録候補」として情報レベルで積む。</summary>
    private async Task CheckUnregisteredRolesAsync(BulkParseResult parsed, CancellationToken ct)
    {
        // 役職マスタを 1 度だけ取得してキャッシュ。
        if (_allRolesCache is null)
        {
            _allRolesCache = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        }
        if (_allRolesCache.Count == 0) return;

        // name_ja 辞書（重複は List で持つが、ここでは Contains 判定のみなので HashSet で十分）。
        var byNameJa = new HashSet<string>(_allRolesCache.Select(r => r.NameJa), StringComparer.Ordinal);

        // 同じ DisplayName が複数行に出現した場合、警告を 1 度だけ出すための重複防止セット。
        var alreadyWarned = new HashSet<string>(StringComparer.Ordinal);

        foreach (var card in parsed.Cards)
        foreach (var tier in card.Tiers)
        foreach (var group in tier.Groups)
        foreach (var role in group.Roles)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(role.DisplayName)) continue;

            string display = role.DisplayName.Trim();
            if (display.Length == 0) continue;
            if (byNameJa.Contains(display)) continue; // 既存役職
            if (!alreadyWarned.Add(display)) continue; // 同一表示名で 2 回目以降はスキップ

            parsed.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Info,
                LineNumber = role.LineNumber,
                Message = $"{role.LineNumber} 行目: 役職「{display}」は roles マスタに存在しません。Apply 時に QuickAddRoleDialog で role_code / 英名 / role_format_kind を入力して新規登録します。"
            });
        }
    }

    //  Draft 注入フェーズ

    /// <summary>パース結果を <paramref name="session"/> 配下の DraftCredit へ追加する。</summary>
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
    /// 指定スコープ配下を <see cref="BulkParseResult"/> の内容で置換する。
    /// ツリー右クリック「📝 一括入力で編集...」（Phase 4 で UI 接続）から呼ばれる経路。
    /// 動作:
    /// <list type="number">
    ///   <item><description>対象スコープ配下の既存子ノードをすべて <see cref="DraftBase.MarkDeleted"/> または
    ///     <see cref="DraftState.Added"/> 状態の場合は親リストから直接除去する。</description></item>
    ///   <item><description>パース結果からスコープに対応する範囲を抜き出し、新規 Draft ノードを生成して
    ///     対象スコープに追加する。スコープ自身（カード／ティア／グループ／役職）の Notes は
    ///     パース結果のトップレベル <c>Notes</c> から転写し、必要なら <see cref="DraftBase.MarkModified"/>。</description></item>
    /// </list>
    /// パース結果が想定スコープより外側を持っている場合（例: Role スコープなのに <c>----</c> でカードを増やした）
    /// は、最上位のみ採用して残りは <see cref="InfoMessages"/> に警告として残す（呼び出し側でユーザーに表示）。
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

        // 全置換モードでは Pending マップも一度クリアする。
        // テキスト編集 SSoT の新 UI ではデバウンス満了ごとに本メソッドが呼ばれるため、
        // Pending マップをクリアしないと「同じ名前を入力 → 別のフィールドを編集 → 再パース」のループで
        // 同名 Pending が累積し、保存時に「同じ名前を複数回 INSERT」してマスタにゴミ重複ができる。
        // 全置換 = 現在のテキスト内容から Draft / Pending を作り直す、というセマンティクスに揃える。
        session.PendingPersonAliases.Clear();
        session.PendingCharacterAliases.Clear();
        session.PendingCompanyAliases.Clear();
        session.PendingLogos.Clear();

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
                            session, pb.LeadingCompanyText, oldName: null, updatedBy, ct).ConfigureAwait(false);
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
    /// Draft 子ノードリストの中身を「適切な状態」でクリアする。
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
        // カード備考を Draft 実体にコピー。
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
        // Tier 備考を Draft 実体にコピー。
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
        // Group 備考を Draft 実体にコピー。
        ApplyNotesIfChanged(targetGroup, pg.Notes, n => targetGroup.Entity.Notes = n,
            () => targetGroup.Entity.Notes);

        foreach (var pr in pg.Roles)
        {
            if (pr.ResolvedRoleCode is null) continue; // 未解決はスキップ（呼び出し側が事前に解消する想定）

            // 役職追加処理は Diff 経路でも再利用するため別メソッドに分けてある。
            // 役職を新規追加（同 group 内で重複役職は許容、Draft では既存 role を再利用しない＝末尾追加で素直に）。
            await ApplyParsedRoleNewAsync(pr, session, targetGroup, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>パース結果の 1 役職分を、指定 Group の末尾に新規 Role として追加する（切り出し）。 Group 直下に新 Role を挿入してから、その配下に Block 群（パース結果の Blocks）を流し込む。 既存 Role への上書きは想定しない（Apply 経路での新規 Role 追加 + Diff 経路での新 Role 追加の双方で再利用）。</summary>
    private async Task ApplyParsedRoleNewAsync(
        ParsedRole pr, CreditDraftSession session, DraftGroup targetGroup,
        string? updatedBy, CancellationToken ct)
    {
        if (pr.ResolvedRoleCode is null) return;

        var role = AppendNewRole(session, targetGroup, pr.ResolvedRoleCode);

        // 役職備考を Draft 実体にコピー（新規 role なので未保存状態 = MarkModified() 不要、直接代入で OK）。
        if (pr.Notes is not null)
        {
            role.Entity.Notes = pr.Notes;
        }

        // 所属表記レイアウト（SUFFIX 既定 / PREFIX = 映画の製作・配給などの 2 カラム表記）。
        role.Entity.AffiliationLayout = pr.AffiliationLayout;

        // 配下 Block を順に追加。
        foreach (var pb in pr.Blocks)
        {
            if (pb.Rows.Count == 0 && pb.LeadingCompanyText is null && pb.Notes is null) continue;
            await ApplyParsedBlockNewAsync(pb, session, role, pr, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>パース結果の 1 ブロック分を、指定 Role の末尾に新規 Block として追加する（切り出し）。 先頭企業屋号、ブロック備考、各行のエントリを順に流し込む。</summary>
    private async Task ApplyParsedBlockNewAsync(
        ParsedBlock pb, CreditDraftSession session, DraftRole targetRole, ParsedRole parentRole,
        string? updatedBy, CancellationToken ct)
    {
        // ColCountExplicit が true なら明示指定された ColCount を使う。
        // false ならパーサが推測したタブ数値（既存挙動）が入っている。
        var block = AppendNewBlock(session, targetRole, (byte)pb.ColCount);

        // ブロック備考を Draft 実体にコピー（新規 block なので直接代入で OK）。
        if (pb.Notes is not null)
        {
            block.Entity.Notes = pb.Notes;
        }

        // [先頭企業屋号]
        if (!string.IsNullOrEmpty(pb.LeadingCompanyText))
        {
            int? leadingAliasId = await ResolveOrCreateCompanyAliasAsync(
                session, pb.LeadingCompanyText, oldName: null, updatedBy, ct).ConfigureAwait(false);
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
            // 1 行内のエントリは並びの先頭から順に追加するが、
            foreach (var pe in row.Entries)
            {
                await AppendParsedEntryAsync(session, block, pe, parentRole, updatedBy, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Draft ノードの Notes プロパティに対して「値が変わっていれば代入 + Modified 化」を行うヘルパ。</summary>
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
        // 装飾情報を最終 Draft エントリに反映するためのヘルパ。
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
            // クレジット時の誤記（事故）。マスタを汚さず、エントリ単位でフリーテキストとして保持する。
            // 非 NULL のみ転記する（NULL = 誤記なし、上書きクリアの意図は今のフェーズでは扱わない）。
            // 種別ごとの妥当性（PERSON で character_misprint が立つ等）は CreditPreviewRenderer 側で
            // 無視されるため、ここでは入力された値をそのまま転記する。
            if (pe.PersonMisprintText is not null) last.Entity.PersonMisprintText = pe.PersonMisprintText;
            if (pe.CharacterMisprintText is not null) last.Entity.CharacterMisprintText = pe.CharacterMisprintText;
            if (pe.CompanyMisprintText is not null) last.Entity.CompanyMisprintText = pe.CompanyMisprintText;
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
                // キャラ alias 引き当て or 新規作成。CharacterAliasIdOverride（明示参照）と
                // IsForcedNewCharacter（強制新規）と CharacterOldName（リダイレクト）を渡す。
                int? characterAliasId = await ResolveOrCreateCharacterAliasAsync(
                    session, pe.CharacterRawText, pe.IsForcedNewCharacter, pe.CharacterOldName, updatedBy, ct,
                    aliasIdOverride: pe.CharacterAliasIdOverride,
                    lineNumberForWarning: pe.LineNumber).ConfigureAwait(false);

                // 声優 alias 引き当て or 新規作成。PersonAliasIdOverride（明示参照）と
                // IsForcedNewPerson（強制新規）と PersonOldName（リダイレクト）と役職コード（未登録役職警告用）を渡す。
                int? personAliasId = await ResolveOrCreatePersonAliasAsync(
                    session, pe.PersonRawText, pe.PersonOldName, updatedBy, ct,
                    forceNew: pe.IsForcedNewPerson,
                    aliasIdOverride: pe.PersonAliasIdOverride,
                    roleCodeForWarning: pr.ResolvedRoleCode,
                    lineNumberForWarning: pe.LineNumber).ConfigureAwait(false);

                // 所属（4 パターン対応：なし / ID 屋号 / 強制テキスト / 両持ち）
                int? affCompanyAliasId = null;
                string? affRawText = null;
                await ResolveAffiliationAsync(pe.AffiliationRawText, pe.AffiliationOverrideText, pe.AffiliationForceText, pe.LineNumber, ct,
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
                    affCompanyAliasId, affRawText, pe.AffiliationInline);
                ApplyEntryModifiersToLast();
                return;
            }

            case ParsedEntryKind.Logo:
            {
                // [屋号#CIバージョン] 構文。屋号名で company_alias を引き当て、
                // その配下のロゴから ci_version_label が一致するものを採用する。
                // 屋号自動投入は LOGO 解決の文脈では行わない（LOGO はマスタ管理画面で明示登録すべき
                // 性質のもののため、未ヒットなら TEXT 降格 + InfoMessages に残す）。
                // CompanyOldName（屋号部分の旧 => 新）を引き当て軸として渡す。
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
                // CompanyAliasIdOverride（明示参照）と CompanyOldName（リダイレクト）を渡す。
                int? companyAliasId = await ResolveOrCreateCompanyAliasAsync(
                    session, pe.CompanyRawText, pe.CompanyOldName, updatedBy, ct,
                    aliasIdOverride: pe.CompanyAliasIdOverride,
                    lineNumberForWarning: pe.LineNumber).ConfigureAwait(false);

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

                // PersonAliasIdOverride（明示参照）と IsForcedNewPerson（強制新規）と
                // PersonOldName（リダイレクト）と役職コード（未登録役職警告用）を渡す。
                int? personAliasId = await ResolveOrCreatePersonAliasAsync(
                    session, pe.PersonRawText, pe.PersonOldName, updatedBy, ct,
                    forceNew: pe.IsForcedNewPerson,
                    aliasIdOverride: pe.PersonAliasIdOverride,
                    roleCodeForWarning: pr.ResolvedRoleCode,
                    lineNumberForWarning: pe.LineNumber).ConfigureAwait(false);

                int? affCompanyAliasId = null;
                string? affRawText = null;
                await ResolveAffiliationAsync(pe.AffiliationRawText, pe.AffiliationOverrideText, pe.AffiliationForceText, pe.LineNumber, ct,
                    aliasId => affCompanyAliasId = aliasId,
                    raw => affRawText = raw).ConfigureAwait(false);

                if (personAliasId is null)
                {
                    AppendTextEntry(session, block, pe.PersonRawText ?? "");
                    ApplyEntryModifiersToLast();
                    return;
                }
                AppendPersonEntry(session, block, personAliasId.Value, affCompanyAliasId, affRawText, pe.AffiliationInline);
                ApplyEntryModifiersToLast();
                return;
            }
        }
    }

    //  マスタ引き当て + 自動 QuickAdd

    /// <summary>
    /// 屋号 + CI バージョンラベルから <c>logo_id</c> を引き当てる。
    /// 動作:
    /// <list type="number">
    ///   <item><description><paramref name="companyName"/> で <c>company_aliases</c> を name 完全一致検索。
    ///     ヒットしなければ null を返す（屋号自動投入は行わない＝LOGO は明示登録すべきリソース）。</description></item>
    ///   <item><description>屋号 alias_id 配下のロゴ群を <c>logos</c> から取得し、
    ///     <c>ci_version_label</c> が <paramref name="ciVersionLabel"/> と完全一致するロゴを採用。</description></item>
    ///   <item><description>未ヒット時は null を返す（呼び出し側で TEXT 降格 + InfoMessage 出力）。</description></item>
    /// </list>
    /// LOGO は屋号テキストとは別レイヤのマスタ（CI デザインバージョン管理用）であり、
    /// 一括入力からの自動投入は <c>ci_version_label</c> 以外のメタデータ（valid_from, description 等）が
    /// 揃わないため、未ヒット時は明示的にユーザーに気付かせる方針とする。
    /// LOGO エントリの引き当て。屋号 alias_id + CI バージョンラベルで <c>logos</c> テーブルを引く。
    /// 屋号自動投入は行わない（LOGO は明示登録すべき性質のため、未ヒット時は呼び出し側で TEXT 降格）。
    /// <paramref name="companyOldName"/> が指定されていれば、引き当ての軸として旧屋号を採用する
    /// （旧屋号で登録された logos を新屋号表記からも引きたいケースに対応）。
    /// 屋号部分の似て非なる判定もここで一緒に走らせる（誤記検出のため）。
    /// </summary>
    /// <param name="companyName">屋号テキスト（<c>[屋号#CIバージョン]</c> の屋号部分）。</param>
    /// <param name="companyOldName">屋号部分の「旧 =&gt; 新」記法における旧側参照キー。</param>
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
        // FindByExactNameAsync は SQL レベルで「空白除去後の一致」も吸収するため、
        // 結果が複数返ったときは厳密一致を優先し、無ければ空白除去一致の先頭を採用する。
        CompanyAlias? exact = null;
        if (!string.IsNullOrWhiteSpace(companyOldName))
        {
            string oldTrim = companyOldName!.Trim();
            var oldHits = await _companyAliasesRepo.FindByExactNameAsync(oldTrim, ct).ConfigureAwait(false);
            exact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal))
                    ?? oldHits.FirstOrDefault();
            if (exact is null)
            {
                InfoMessages.Add(
                    $"⚠ ロゴ「[{oldTrim} => {name}#{ci}]」の旧屋号が既存マスタに見つかりませんでした。新屋号「{name}」で引き当てを再試行します。");
            }
        }
        if (exact is null)
        {
            var hits = await _companyAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
            exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
                    ?? hits.FirstOrDefault();
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
    /// 人物名義の引き当て or 「保存時に投入予定」の Pending 登録。空文字 / 空白なら null を返す。
    /// 既存マスタに <c>name</c> 完全一致するエントリがあればその実 <c>alias_id</c> を返す（変更なし）。
    /// 引き当たらない場合は <see cref="CreditDraftSession.PendingPersonAliases"/> に追加し、
    /// <see cref="CreditDraftSession.AllocateTempId"/> で払い出した負数 ID を返す。
    /// この時点では DB に一切書き込まない。保存ボタン押下で
    /// <c>CreditSaveService.ApplyPendingMastersAsync</c>（Phase 0）が一括投入する。
    /// <para>Pending 振り分けの 2 系統：</para>
    /// <list type="bullet">
    ///   <item><description><b>系統A（既存人物に名義追加）</b>：<paramref name="oldName"/> が指定されていて、
    ///   左側「旧名義」が既存マスタに引き当たり、主人物（<c>person_alias_persons.person_seq=1</c>）が特定できた場合。
    ///   <see cref="PendingPersonAlias.AttachToExistingPersonId"/> に旧名義の主人物 <c>person_id</c> を入れる。</description></item>
    ///   <item><description><b>系統B（人物本体ごと新設）</b>：旧名義リダイレクト無し、または旧名義が引き当たらなかった場合。
    ///   <c>AttachToExistingPersonId = null</c> + 姓・名分割（<see cref="SplitFamilyGivenName"/>）した
    ///   <c>FamilyName</c> / <c>GivenName</c> / <c>FullName</c> を入れる。</description></item>
    /// </list>
    /// 旧名義リダイレクトで決着しない場合のみ、似て非なる名義の全件比較が走る（リダイレクトの方が
    /// 強い意図表現なので、両方の警告が二重に出るのを避ける）。
    /// </summary>
    /// <summary>
    /// 既存 person_alias を引き当てたときに、その人物の過去クレジット履歴に「今回の役職コード」が
    /// 含まれているかを確認し、含まれていなければ警告を出すヘルパ。
    /// 用途は「同姓同名の別人」「役職転向」の検出。同 Apply 内で同じ (alias_id, role_code) は
    /// 1 回だけ警告（<see cref="_warnedRoleCombos"/> で重複抑制）。
    /// 過去クレジット履歴が無い（=新規人物相当）or 既に同役職でクレジット済みなら警告対象外。
    /// </summary>
    private async Task WarnIfNewRoleForPersonAliasAsync(
        PersonAlias alias, string? roleCode, int lineNumber, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(roleCode)) return;
        if (!_warnedRoleCombos.Add((alias.AliasId, roleCode))) return;

        var pastRoleCodes = await _personAliasesRepo.GetCreditedRoleCodesByPersonOfAliasAsync(alias.AliasId, ct).ConfigureAwait(false);
        if (pastRoleCodes.Count == 0) return;            // 過去クレジット無し = 警告対象外
        if (pastRoleCodes.Contains(roleCode)) return;    // 既に同役職経験あり = 通常パターン

        // 役職コードは内部値だが、過去履歴と今回値を並べてユーザーに見せれば文脈は伝わる。
        // 役職表示名（roles.name_ja）まで引いて見せた方が親切だが、ここではコードのみで簡素化。
        // 「*」「#alias_id」の使い方のヒントも添える。
        string pastList = string.Join(", ", pastRoleCodes);
        InfoMessages.Add(
            $"⚠ {lineNumber} 行目: 「{alias.Name}」(person_alias_id={alias.AliasId}) は過去に [{pastList}] でクレジットされていますが、" +
            $"今回〈{roleCode}〉として登録しようとしています。同姓同名の別人または役職転向の可能性があります。" +
            $"同姓同名の別人なら *{alias.Name} で強制新規、" +
            $"別 alias を明示するなら {alias.Name}#alias_id を使ってください。" +
            $"同一人物の役職転向なら本警告は無視して構いません。");
    }

    private async Task<int?> ResolveOrCreatePersonAliasAsync(
        CreditDraftSession session, string? rawName, string? oldName, string? updatedBy, CancellationToken ct,
        bool forceNew = false,
        int? aliasIdOverride = null,
        string? roleCodeForWarning = null,
        int lineNumberForWarning = 0)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        // alias_id 明示参照（テキスト構文 「山田 太郎#100」）：DB に該当 ID があれば直接採用、
        // 無ければ警告 + 通常引き当てにフォールバック。エンコーダがラウンドトリップ保証のために
        // 出力する記法を読み戻す経路。
        if (aliasIdOverride.HasValue)
        {
            var hitById = await _personAliasesRepo.GetByIdAsync(aliasIdOverride.Value, ct).ConfigureAwait(false);
            if (hitById is not null && !hitById.IsDeleted)
            {
                await WarnIfNewRoleForPersonAliasAsync(hitById, roleCodeForWarning, lineNumberForWarning, ct).ConfigureAwait(false);
                return hitById.AliasId;
            }
            InfoMessages.Add(
                $"⚠ {lineNumberForWarning} 行目: 「{name}#{aliasIdOverride.Value}」の person_alias_id={aliasIdOverride.Value} が DB に存在しません。通常引き当てにフォールバックします。");
        }

        // forceNew=true（「*山田 太郎」明示）の場合は同名既存スキップして必ず新規作成
        // （同姓同名の別人を意図的に登録する用途。未登録役職警告も抑制する）。
        if (!forceNew)
        {
            // 既存検索（name 完全一致 + 空白除去後一致）。
            // FindByExactNameAsync は SQL レベルで半角・全角 SP を無視した一致も拾うため、
            // 「本名陽子」入力に対して DB「本名 陽子」も同名として引き当てられる。
            // 結果が複数返ったときは厳密一致を優先し、無ければ空白除去一致の先頭を採用する。
            var hits = await _personAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
            var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
                        ?? hits.FirstOrDefault();
            if (exact is not null)
            {
                await WarnIfNewRoleForPersonAliasAsync(exact, roleCodeForWarning, lineNumberForWarning, ct).ConfigureAwait(false);
                return exact.AliasId;
            }
        }

        // 「旧 => 新」記法で旧名義が指定されている場合、既存 person への新 alias 追加（系統A）を試みる。
        // forceNew=true（*X 明示）の場合は「同姓同名でも別人として強制新規」の意図なので、リダイレクト
        // 処理もスキップする（=> と * は意味が衝突するため）。
        if (!forceNew && !string.IsNullOrWhiteSpace(oldName))
        {
            string oldTrim = oldName!.Trim();
            var oldHits = await _personAliasesRepo.FindByExactNameAsync(oldTrim, ct).ConfigureAwait(false);
            var oldExact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal))
                           ?? oldHits.FirstOrDefault();
            if (oldExact is not null)
            {
                // 旧 alias 経由で結合済みの person_id を取得（PersonSeq=1 が主人物）。
                var rels = await _personAliasPersonsRepo.GetByAliasAsync(oldExact.AliasId, ct).ConfigureAwait(false);
                var primary = rels.OrderBy(r => r.PersonSeq).FirstOrDefault();
                if (primary is not null)
                {
                    // 系統A: 同 person 配下に新 alias を追加する Pending を積む。
                    // 既に同名 + 同 attach 先の Pending がマップにあれば再利用（1 回の Apply 内で同名が
                    // 複数役職に登場するケース、例えば「作詞 + 作曲」が同じ人、で重複積み上げを防ぐ）。
                    var dup = session.PendingPersonAliases.Values.FirstOrDefault(p =>
                        string.Equals(p.AliasName, name, StringComparison.Ordinal)
                        && p.AttachToExistingPersonId == primary.PersonId);
                    if (dup is not null) return dup.TempAliasId;

                    int tempId = session.AllocateTempId();
                    session.PendingPersonAliases[tempId] = new PendingPersonAlias
                    {
                        TempAliasId = tempId,
                        AliasName = name,
                        AttachToExistingPersonId = primary.PersonId,
                    };
                    InfoMessages.Add(
                        $"✅ 「{oldTrim}」の人物（person_id={primary.PersonId}）に新名義「{name}」を別 alias として追加予定にしました（保存時に投入）。");
                    return tempId;
                }
                // 旧 alias は見つかったが person 結合が無い（DB データ破損相当）→ 系統B（本体新設）にフォールバック。
                InfoMessages.Add(
                    $"⚠ 旧名義「{oldTrim}」は見つかりましたが、結合先の人物が特定できませんでした。「{name}」を新規人物として登録予定にします。");
            }
            else
            {
                InfoMessages.Add(
                    $"⚠ 「{oldTrim} => {name}」の旧名義が既存マスタに見つかりませんでした。タイポなら入力を見直してください。続行する場合「{name}」のみで新規人物として登録予定にします（保存時に投入）。");
            }
        }

        // 似て非なる類似度判定（リダイレクト無し or 旧側引き当て失敗で新規作成しようとしている表記が対象）。
        // forceNew=true は同姓同名でも別人として強制新規の意図なので、類似名警告も抑制する。
        if (!forceNew)
        {
            await WarnIfSimilarPersonAliasAsync(name, ct).ConfigureAwait(false);
        }

        // 系統B: 人物本体ごと新設する Pending を積む。
        // forceNew=false（暗黙の新規人物作成）の場合のみ、同 Apply 内で同名 Pending を再利用する（重複排除）。
        // forceNew=true（*X 明示）は「同一話数内でも別人物として立てる」意図的な強制新規なので、
        // 同名でも独立した別 person + 別 alias を毎回作成する（CHARACTER 側と同じ流儀）。
        if (!forceNew)
        {
            var dupNew = session.PendingPersonAliases.Values.FirstOrDefault(p =>
                string.Equals(p.AliasName, name, StringComparison.Ordinal)
                && p.AttachToExistingPersonId is null);
            if (dupNew is not null) return dupNew.TempAliasId;
        }

        // 姓・名分割は新表記に対して実施。
        var (familyName, givenName) = SplitFamilyGivenName(name);
        int newTempId = session.AllocateTempId();
        session.PendingPersonAliases[newTempId] = new PendingPersonAlias
        {
            TempAliasId = newTempId,
            AliasName = name,
            AttachToExistingPersonId = null,
            FullName = name,
            FamilyName = familyName,
            GivenName = givenName,
        };
        return newTempId;
    }

    /// <summary>
    /// キャラクター名義の引き当て or 新規作成。
    /// <paramref name="forceNew"/> が true の場合は同名既存キャラがあっても新規作成する（モブ用途）。
    /// <paramref name="oldName"/> が指定されていれば、既存 <c>character_aliases</c> から引き当てて
    /// <c>character_id</c> を取得し、同 character 配下に新 alias を追加登録する。
    /// 旧名義が引き当たらなければ警告 + 通常新規作成にフォールバック。<paramref name="forceNew"/> が true の場合は
    /// リダイレクトより強制新規が優先される（モブ用途のため、旧側参照を試みず必ず新規作成する）。
    /// </summary>
    private async Task<int?> ResolveOrCreateCharacterAliasAsync(
        CreditDraftSession session, string? rawName, bool forceNew, string? oldName, string? updatedBy, CancellationToken ct,
        int? aliasIdOverride = null,
        int lineNumberForWarning = 0)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        // alias_id 明示参照（テキスト構文 「<少年#42>」）：DB に該当 ID があれば直接採用、
        // 無ければ警告 + 通常引き当てにフォールバック。
        if (aliasIdOverride.HasValue)
        {
            var hitById = await _characterAliasesRepo.GetByIdAsync(aliasIdOverride.Value, ct).ConfigureAwait(false);
            if (hitById is not null && !hitById.IsDeleted)
            {
                return hitById.AliasId;
            }
            InfoMessages.Add(
                $"⚠ {lineNumberForWarning} 行目: 「<{name}#{aliasIdOverride.Value}>」の character_alias_id={aliasIdOverride.Value} が DB に存在しません。通常引き当てにフォールバックします。");
        }

        if (!forceNew)
        {
            // 完全一致 + 空白除去後一致（FindByExactNameAsync の SQL レベル吸収）。
            // 厳密一致を優先しつつ、無ければ空白除去一致の先頭を採用する。
            var hits = await _characterAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
            var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
                        ?? hits.FirstOrDefault();
            if (exact is not null) return exact.AliasId;

            // 「旧 => 新」記法で旧キャラ名義が指定されている場合、既存 character への新 alias 追加（系統A）。
            if (!string.IsNullOrWhiteSpace(oldName))
            {
                string oldTrim = oldName!.Trim();
                var oldHits = await _characterAliasesRepo.FindByExactNameAsync(oldTrim, ct).ConfigureAwait(false);
                var oldExact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal))
                               ?? oldHits.FirstOrDefault();
                if (oldExact is not null)
                {
                    // 系統A: 旧 character_id 配下に新名義の Pending を積む（保存時に投入）。
                    // 既に同名 + 同 attach 先の Pending があれば再利用（重複排除）。
                    var dup = session.PendingCharacterAliases.Values.FirstOrDefault(p =>
                        string.Equals(p.AliasName, name, StringComparison.Ordinal)
                        && p.AttachToExistingCharacterId == oldExact.CharacterId);
                    if (dup is not null) return dup.TempAliasId;

                    int tempId = session.AllocateTempId();
                    session.PendingCharacterAliases[tempId] = new PendingCharacterAlias
                    {
                        TempAliasId = tempId,
                        AliasName = name,
                        AttachToExistingCharacterId = oldExact.CharacterId,
                    };
                    InfoMessages.Add(
                        $"✅ 「{oldTrim}」のキャラクター（character_id={oldExact.CharacterId}）に新名義「{name}」を別 alias として追加予定にしました（保存時に投入）。");
                    return tempId;
                }
                InfoMessages.Add(
                    $"⚠ 「{oldTrim} => {name}」の旧キャラ名義が既存マスタに見つかりませんでした。「{name}」を新規キャラとして登録予定にします（保存時に投入）。");
            }

            // 似て非なる類似度判定（リダイレクト無し or 旧側引き当て失敗ケース）。
            await WarnIfSimilarCharacterAliasAsync(name, ct).ConfigureAwait(false);
        }

        // 系統B: キャラクター本体ごと新設する Pending を積む。
        // forceNew=false（暗黙の新規キャラ作成）の場合のみ、同 Apply 内で同名の Pending があれば再利用する
        // （重複排除）。forceNew=true（<*キャラ> 明示）は「同一話数内でも別キャラとして立てる」モブ用途の
        // 意図的な強制新規なので、同名でも独立した別 character + 別 alias を毎回作成する。これによりたとえば
        // 同じ「サッカー部員」役を 4 人並べたいとき <*サッカー部員> × 4 行で 4 体のキャラが作られる
        // （旧ロジックは forceNew でも同名 Pending を 1 件に集約していたため、結果的に全員が同一キャラに紐付く
        // 不具合があった）。
        if (!forceNew)
        {
            var dupNew = session.PendingCharacterAliases.Values.FirstOrDefault(p =>
                string.Equals(p.AliasName, name, StringComparison.Ordinal)
                && p.AttachToExistingCharacterId is null);
            if (dupNew is not null) return dupNew.TempAliasId;
        }

        // characters.character_kind は character_kinds マスタに必ず存在する値で投入する必要がある
        // （character_kind は ENUM → character_kinds 表への FK 参照に変更されており、
        //  マスタに無いコードを INSERT すると FK 制約 fk_characters_kind 違反で失敗する）。
        // 当該マスタには PRECURE / ALLY / VILLAIN / SUPPORTING の 4 類型のみが初期投入される。
        // 一括入力で自動追加されるキャラは多くの場合「名もなき脇役・モブ・取り巻き」のため、
        // 4 類型のうち意味が最も近い "SUPPORTING"（とりまく人々）を機械投入の既定値とする。
        // 主要キャラはユーザーが後でマスタ管理画面で PRECURE / ALLY / VILLAIN に変更可能。
        int newTempId = session.AllocateTempId();
        session.PendingCharacterAliases[newTempId] = new PendingCharacterAlias
        {
            TempAliasId = newTempId,
            AliasName = name,
            AttachToExistingCharacterId = null,
            CharacterName = name,
            CharacterKindCode = "SUPPORTING",
        };
        return newTempId;
    }

    /// <summary>企業屋号の引き当て or 「保存時に投入予定」の Pending 登録。
    /// 既存マスタに <c>name</c> 完全一致するエントリがあればその実 <c>alias_id</c> を返す（変更なし）。
    /// 引き当たらない場合は <see cref="CreditDraftSession.PendingCompanyAliases"/> に Pending を積み、
    /// 仮の負数 ID を返す。<paramref name="oldName"/> が指定されていて旧屋号が引き当たった場合は系統A
    /// （既存 company に新屋号追加 = <see cref="PendingCompanyAlias.AttachToExistingCompanyId"/> 設定）、
    /// それ以外は系統B（companies + company_aliases 新設）として積む。</summary>
    private async Task<int?> ResolveOrCreateCompanyAliasAsync(
        CreditDraftSession session, string? rawName, string? oldName, string? updatedBy, CancellationToken ct,
        int? aliasIdOverride = null,
        int lineNumberForWarning = 0)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string name = rawName.Trim();

        // alias_id 明示参照（テキスト構文 「[東映#100]」）：DB に該当 ID があれば直接採用、
        // 無ければ警告 + 通常引き当てにフォールバック。
        if (aliasIdOverride.HasValue)
        {
            var hitById = await _companyAliasesRepo.GetByIdAsync(aliasIdOverride.Value, ct).ConfigureAwait(false);
            if (hitById is not null && !hitById.IsDeleted)
            {
                return hitById.AliasId;
            }
            InfoMessages.Add(
                $"⚠ {lineNumberForWarning} 行目: 「[{name}#{aliasIdOverride.Value}]」の company_alias_id={aliasIdOverride.Value} が DB に存在しません。通常引き当てにフォールバックします。");
        }

        // 完全一致 + 空白除去後一致（FindByExactNameAsync の SQL レベル吸収）。
        // 厳密一致を優先しつつ、無ければ空白除去一致の先頭を採用する。
        var hits = await _companyAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
        var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
                    ?? hits.FirstOrDefault();
        if (exact is not null) return exact.AliasId;

        // 「旧 => 新」記法で旧屋号が指定されている場合、既存 company への新屋号追加（系統A）を試みる。
        if (!string.IsNullOrWhiteSpace(oldName))
        {
            string oldTrim = oldName!.Trim();
            var oldHits = await _companyAliasesRepo.FindByExactNameAsync(oldTrim, ct).ConfigureAwait(false);
            var oldExact = oldHits.FirstOrDefault(a => string.Equals(a.Name, oldTrim, StringComparison.Ordinal))
                           ?? oldHits.FirstOrDefault();
            if (oldExact is not null)
            {
                // 系統A: 旧 company_id 配下に新屋号の Pending を積む（保存時に投入）。
                // 既に同名 + 同 attach 先の Pending があれば再利用（重複排除）。
                var dup = session.PendingCompanyAliases.Values.FirstOrDefault(p =>
                    string.Equals(p.AliasName, name, StringComparison.Ordinal)
                    && p.AttachToExistingCompanyId == oldExact.CompanyId);
                if (dup is not null) return dup.TempAliasId;

                int tempId = session.AllocateTempId();
                session.PendingCompanyAliases[tempId] = new PendingCompanyAlias
                {
                    TempAliasId = tempId,
                    AliasName = name,
                    AttachToExistingCompanyId = oldExact.CompanyId,
                };
                InfoMessages.Add(
                    $"✅ 「{oldTrim}」の企業（company_id={oldExact.CompanyId}）に新屋号「{name}」を別 alias として追加予定にしました（保存時に投入）。");
                return tempId;
            }
            InfoMessages.Add(
                $"⚠ 「{oldTrim} => {name}」の旧屋号が既存マスタに見つかりませんでした。「{name}」を新規企業として登録予定にします（保存時に投入）。");
        }

        // 似て非なる類似度判定（リダイレクト無し or 旧側引き当て失敗ケース）。
        await WarnIfSimilarCompanyAliasAsync(name, ct).ConfigureAwait(false);

        // 系統B: 企業本体ごと新設する Pending を積む。
        // 既に同名 + 本体新設系統の Pending があれば再利用（重複排除）。
        var dupNew = session.PendingCompanyAliases.Values.FirstOrDefault(p =>
            string.Equals(p.AliasName, name, StringComparison.Ordinal)
            && p.AttachToExistingCompanyId is null);
        if (dupNew is not null) return dupNew.TempAliasId;

        int newTempId = session.AllocateTempId();
        session.PendingCompanyAliases[newTempId] = new PendingCompanyAlias
        {
            TempAliasId = newTempId,
            AliasName = name,
            AttachToExistingCompanyId = null,
            CompanyName = name,
        };
        return newTempId;
    }

    /// <summary>所属表記の解決：マスタに既存があればその alias_id、なければ rawText のまま保持 （所属は気軽に新規作成しない＝企業マスタを意図せず増殖させない設計）。</summary>
    /// <summary>所属表記を 4 パターンに対応する形で解決する：
    /// <list type="bullet">
    ///   <item>所属なし（<paramref name="raw"/> も <paramref name="overrideText"/> も null） → 両方 null</item>
    ///   <item>強制テキスト（<paramref name="forceText"/> = true、<paramref name="overrideText"/> あり） →
    ///         alias_id = null、affiliation_text = overrideText（マスタ引き当てスキップ）</item>
    ///   <item>ID 屋号のみ（<paramref name="raw"/> あり、<paramref name="overrideText"/> = null）→
    ///         <c>company_aliases</c> 引き当て。成功時は alias_id セット、失敗時は <see cref="UnresolvedAffiliations"/> に積んで両方 null</item>
    ///   <item>両持ち（<paramref name="raw"/> も <paramref name="overrideText"/> もあり）→
    ///         alias_id 引き当て + affiliation_text = overrideText。引き当て失敗時は同様に Unresolved に積み、テキストのみ保存</item>
    /// </list>
    /// 旧仕様にあった「マスタ未マッチ時にとりあえず <c>affiliation_text</c> にフリーテキスト保存」のフォールバック路は撤去。
    /// 明示的にクオート記法 <c>("...")</c> を使った時のみ <c>affiliation_text</c> が残るようになった。</summary>
    private async Task ResolveAffiliationAsync(
        string? raw, string? overrideText, bool forceText, int lineNumber, CancellationToken ct,
        Action<int?> setAliasId, Action<string?> setRawText)
    {
        // パターン 1: 何もなし
        if (string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(overrideText))
        {
            setAliasId(null); setRawText(null); return;
        }

        // パターン 3: 強制テキスト（クオート記法）→ マスタ引き当てスキップ
        if (forceText && !string.IsNullOrWhiteSpace(overrideText))
        {
            setAliasId(null); setRawText(overrideText!.Trim()); return;
        }

        // 共通：ID 屋号引き当て試行（raw に屋号名がある場合）。
        int? resolvedAliasId = null;
        string? unresolvedName = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            string name = raw!.Trim();
            // 所属表記もマスタ既存があれば結び付ける。FindByExactNameAsync の SQL レベルで
            // 空白除去後の一致も拾うため、「東映 アニメーション」⇄「東映アニメーション」も同名として解決する。
            var hits = await _companyAliasesRepo.FindByExactNameAsync(name, ct).ConfigureAwait(false);
            var exact = hits.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))
                        ?? hits.FirstOrDefault();
            if (exact is not null)
            {
                resolvedAliasId = exact.AliasId;
            }
            else
            {
                unresolvedName = name;
            }
        }

        // パターン 4: 両持ち → alias_id + override テキスト両方
        if (!string.IsNullOrWhiteSpace(overrideText))
        {
            setAliasId(resolvedAliasId);
            setRawText(overrideText!.Trim());
            if (unresolvedName is not null)
            {
                UnresolvedAffiliations.Add(new UnresolvedAffiliation(unresolvedName, lineNumber));
            }
            return;
        }

        // パターン 2: ID 屋号のみ
        if (resolvedAliasId is not null)
        {
            setAliasId(resolvedAliasId); setRawText(null); return;
        }

        // マスタ未登録の屋号 → Unresolved に積んで両方 null（フリーテキスト fallback は廃止）。
        // QuickAddCompanyAliasDialog で登録後に再パースされれば 1 つ上のブロックでヒットする。
        if (unresolvedName is not null)
        {
            UnresolvedAffiliations.Add(new UnresolvedAffiliation(unresolvedName, lineNumber));
        }
        setAliasId(null); setRawText(null);
    }

    /// <summary>人物名を 姓・名 に分割する素朴ロジック。 半角SP区切り → family / given、「・」区切り → given / family（外国名想定）、区切りなし → 両方 null。</summary>
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

    //  Draft オブジェクト生成ヘルパ

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
        int personAliasId, int? affCompanyAliasId, string? affRawText, bool affInline)
    {
        AppendEntry(session, parent, new CreditBlockEntry
        {
            EntryKind = "PERSON",
            EntrySeq = (ushort)(parent.Entries.Count + 1),
            PersonAliasId = personAliasId,
            AffiliationCompanyAliasId = affCompanyAliasId,
            AffiliationText = affRawText,
            AffiliationInline = affInline,
        });
    }

    /// <summary>ブロック末尾に CHARACTER_VOICE エントリを 1 件追加する。</summary>
    private static void AppendCharacterVoiceEntry(
        CreditDraftSession session, DraftBlock parent,
        int personAliasId, int? characterAliasId, string? rawCharacterText,
        int? affCompanyAliasId, string? affRawText, bool affInline)
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
            AffiliationInline = affInline,
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

    /// <summary>ブロック末尾に LOGO エントリを 1 件追加する。 <c>credit_block_entries.entry_kind = "LOGO"</c> + <c>logo_id</c> 必須。</summary>
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

    /// <summary>誤字候補と判定する最小文字数。これ未満の名前同士の 1 字違いは
    /// 「タイポ」と「別人」の区別が困難（極端な場合 1 字違いで完全別人）なので警告対象外とする。</summary>
    private const int TypoMinNameLength = 3;

    /// <summary>誤字候補と判定する最大編集距離。1 = 1 字の差し替え / 挿入 / 削除のみ警告。
    /// 2 以上は「複数字違い = 別名義」とみなして警告しない。</summary>
    private const int TypoMaxEditDistance = 1;

    /// <summary>文字種カテゴリ別の構成数の最大許容差。<see cref="SameScriptComposition"/> で使う。
    /// 0 = 構成完全一致、1 = いずれかのカテゴリで 1 つだけ違ってよい（差し替え系の編集距離 1 と整合）。</summary>
    private const int ScriptCompositionMaxDelta = 1;

    /// <summary>比較用の文字列正規化（空白除去）。 半角スペース・全角スペース・タブ・各種空白文字をすべて除去する。 「五條 真由美」と「五条真由美」のように空白の有無による表記揺れを吸収するための前処理。</summary>
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

    /// <summary>文字種カテゴリ。ひらがな⇔カタカナ・カナ⇔漢字をまたぐ違いを
    /// 「タイポではなく別物」と判定するために、各文字を 6 カテゴリに分類する。
    /// プロジェクト方針：ひらがなとカタカナは別物として扱う（カナ統一の正規化は禁忌）。</summary>
    private enum ScriptCategory { Hiragana, Katakana, CjkIdeograph, Latin, Digit, Other }

    /// <summary>文字を <see cref="ScriptCategory"/> に分類する。</summary>
    private static ScriptCategory ClassifyChar(char c)
    {
        // ひらがな: U+3040..U+309F
        if (c >= '぀' && c <= 'ゟ') return ScriptCategory.Hiragana;
        // 全角カタカナ: U+30A0..U+30FF / カタカナ拡張 (片仮名拡張): U+31F0..U+31FF / 半角カタカナ: U+FF65..U+FF9F
        if ((c >= '゠' && c <= 'ヿ')
            || (c >= 'ㇰ' && c <= 'ㇿ')
            || (c >= '･' && c <= 'ﾟ')) return ScriptCategory.Katakana;
        // CJK 統合漢字 + 拡張 A + 互換漢字: U+3400..U+4DBF, U+4E00..U+9FFF, U+F900..U+FAFF
        if ((c >= '㐀' && c <= '䶿')
            || (c >= '一' && c <= '鿿')
            || (c >= '豈' && c <= '﫿')) return ScriptCategory.CjkIdeograph;
        // ラテン英字（半角 A-Z / a-z、全角 Ａ-Ｚ / ａ-ｚ）
        if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
            || (c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ')) return ScriptCategory.Latin;
        // 数字（半角 0-9、全角 ０-９）
        if ((c >= '0' && c <= '9') || (c >= '０' && c <= '９')) return ScriptCategory.Digit;
        return ScriptCategory.Other;
    }

    /// <summary>2 つの文字列の文字種構成（各カテゴリ何文字か）がほぼ一致するか判定する。
    /// 各カテゴリの個数差が <see cref="ScriptCompositionMaxDelta"/> 以内ならば「同じ文字種構成」とみなす。
    /// これによりひらがな⇔カタカナ・カナ⇔漢字をまたぐ違いは「別物」として誤字検知から弾く。
    /// 例：「アスカ」(カタカナ3) vs 「あすか」(ひらがな3) は 各カテゴリ差 3 で不一致 → 警告しない。
    /// 例：「田中花子」(漢字4) vs 「田中華子」(漢字4) は 完全一致 → 編集距離 1 と合わせて誤字候補。</summary>
    private static bool SameScriptComposition(string a, string b)
    {
        Span<int> ca = stackalloc int[6];
        Span<int> cb = stackalloc int[6];
        foreach (char ch in a) ca[(int)ClassifyChar(ch)]++;
        foreach (char ch in b) cb[(int)ClassifyChar(ch)]++;
        for (int i = 0; i < 6; i++)
        {
            if (Math.Abs(ca[i] - cb[i]) > ScriptCompositionMaxDelta) return false;
        }
        return true;
    }

    /// <summary>2 文字列の編集距離（レーベンシュタイン距離）を返す。挿入・削除・置換のコストを 1 で計算。
    /// 動的計画法 O(|A|×|B|) 実装。日本語名義は最大数十文字なので性能的に問題なし。
    /// 早期打ち切り最適化：<paramref name="cutoff"/> を超えた距離は計算途中で諦めて
    /// <c>cutoff + 1</c> を返す（誤字検知は距離 1 以下しか興味がないため、無駄計算を省く）。</summary>
    private static int LevenshteinDistance(string a, string b, int cutoff)
    {
        if (string.IsNullOrEmpty(a)) return b?.Length ?? 0;
        if (string.IsNullOrEmpty(b)) return a.Length;
        // 文字数差が cutoff を超えていれば距離も必ず超える（早期判定）
        if (Math.Abs(a.Length - b.Length) > cutoff) return cutoff + 1;

        int n = a.Length, m = b.Length;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (int j = 0; j <= m; j++) prev[j] = j;
        for (int i = 1; i <= n; i++)
        {
            curr[0] = i;
            int rowMin = curr[0];
            for (int j = 1; j <= m; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                curr[j] = Math.Min(
                    Math.Min(curr[j - 1] + 1, prev[j] + 1),
                    prev[j - 1] + cost);
                if (curr[j] < rowMin) rowMin = curr[j];
            }
            // 行最小値が cutoff を既に超えていれば、以降の行で距離が縮むことはないので打ち切り
            if (rowMin > cutoff) return cutoff + 1;
            (prev, curr) = (curr, prev);
        }
        return prev[m];
    }

    /// <summary>「正規化後 完全一致ではない」かつ「タイポ候補（距離 1 + 文字種構成一致 + 名前長 3 以上）」を判定する。
    /// 完全一致は呼び出し側で既に除外されている前提だが、空白違いだけの「実質完全一致」は
    /// 警告対象から除外したいので、ここで正規化後完全一致もスキップする。
    /// 文字種構成が違う（例：ひらがな⇔カタカナ・漢字⇔カナ）場合は別物として警告しない（プロジェクト方針）。
    /// 編集距離 2 以上は「複数字違い = 別人」と判断して警告しない（誤字検知に焦点を絞る方針）。</summary>
    private static bool IsLikelyTypo(string normalizedRaw, string targetName)
    {
        if (string.IsNullOrEmpty(normalizedRaw)) return false;
        var targetNorm = NormalizeForCompare(targetName);
        if (targetNorm.Length == 0) return false;
        // 空白違いだけで本質同名 → 警告対象から除外
        if (string.Equals(normalizedRaw, targetNorm, StringComparison.Ordinal)) return false;

        // 短い名前同士は 1 字違いでもタイポか別人か区別できないので警告しない
        if (normalizedRaw.Length < TypoMinNameLength || targetNorm.Length < TypoMinNameLength) return false;

        // 文字種構成が違うなら別物（カナ違い・カナ漢字違いは誤字ではない）
        if (!SameScriptComposition(normalizedRaw, targetNorm)) return false;

        // 編集距離が許容範囲内ならタイポ候補
        int dist = LevenshteinDistance(normalizedRaw, targetNorm, TypoMaxEditDistance);
        return dist > 0 && dist <= TypoMaxEditDistance;
    }

    /// <summary>人物名義の全件キャッシュを返す（初回呼び出し時に lazy load）。 1 適用フェーズ中は再ロードしないことで、N×M の全件比較を 1 回のロードで済ませる。</summary>
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

    /// <summary>進捗報告の発火頻度。 全件比較中、毎件発火すると UI スレッドに負担がかかるため、約 50 件ごとに 1 回イベントを上げる。 全体件数が 50 未満の場合は最後に 1 回だけ「完了」を発火する。</summary>
    private const int CompareProgressTick = 50;

    /// <summary>人物名義の似て非なる警告。</summary>
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
            if (IsLikelyTypo(normalizedRaw, a.Name))
            {
                InfoMessages.Add(
                    $"⚠ 新規登録予定の人物名義「{rawName}」は既存名義「{a.Name}」（alias_id={a.AliasId}）と 1 字違い（同じ文字種構成）です。誤字の可能性があります。同一人物なら「旧名義 => 新名義」記法で書くか、マスタ管理画面で別名義として統合してください。");
            }
        }
        CompareProgress?.Invoke(total, total);
    }

    /// <summary>キャラクター名義の似て非なる警告。</summary>
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
            if (IsLikelyTypo(normalizedRaw, a.Name))
            {
                InfoMessages.Add(
                    $"⚠ 新規登録予定のキャラクター名義「{rawName}」は既存名義「{a.Name}」（alias_id={a.AliasId}, character_id={a.CharacterId}）と 1 字違い（同じ文字種構成）です。誤字の可能性があります。同一キャラなら「旧名義 => 新名義」記法で書くか、マスタ管理画面で別名義として統合してください。");
            }
        }
        CompareProgress?.Invoke(total, total);
    }

    /// <summary>企業屋号の似て非なる警告。LOGO の屋号引き当て失敗時の警告にも兼用。</summary>
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
            if (IsLikelyTypo(normalizedRaw, a.Name))
            {
                InfoMessages.Add(
                    $"⚠ 新規登録予定の企業屋号「{rawName}」は既存屋号「{a.Name}」（alias_id={a.AliasId}, company_id={a.CompanyId}）と 1 字違い（同じ文字種構成）です。誤字の可能性があります。同一企業なら「旧屋号 => 新屋号」記法で書くか、マスタ管理画面で別屋号として統合してください。");
            }
        }
        CompareProgress?.Invoke(total, total);
    }

    // ════════════════════════════════════════════════════════════════════
    //   構造差分検出モード（AppendToCredit ボタン経由）
    // ════════════════════════════════════════════════════════════════════
    //
    // 「📝 クレジット一括入力...」ボタン押下時の AppendToCredit モードを構造差分検出モードに置き換える。
    // 旧テキスト（Encoder で逆翻訳した現状）と新テキスト（ユーザー編集後）を両方パースして、
    // 階層を i 番目同士で対応付けて降下し、変わった末端だけ Modified / Added / Deleted で反映する。
    //
    // LCS 適用範囲（A 案）:
    //   - Card / Tier / Group / Block: i 番目同士の単純対応
    //   - Role: role_code 辞書マッチング（同 Group 内では UNIQUE 前提）
    //   - Entry: 同 Block 内で LCS マッチング（行入れ替えのヒューマンエラーを entry_seq 更新の Modified で拾う）
    //
    // ════════════════════════════════════════════════════════════════════

    /// <summary>旧テキストと新テキストの差分を検出して Draft セッションに反映する。</summary>
    /// <param name="newParsed">ユーザー編集後の新テキストをパースした結果。</param>
    /// <param name="oldText">旧テキスト（ダイアログ起動時の <c>CreditBulkInputEncoder.EncodeFullAsync</c> 出力）。</param>
    /// <param name="session">対象の編集セッション。<c>session.Root</c> 配下が更新される。</param>
    /// <param name="updatedBy">更新者。新規 alias 投入や Modified マーキング時に使用。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ApplyDiffToCreditAsync(
        BulkParseResult newParsed, string oldText,
        CreditDraftSession session, string? updatedBy, CancellationToken ct = default)
    {
        if (newParsed is null) throw new ArgumentNullException(nameof(newParsed));
        if (session is null) throw new ArgumentNullException(nameof(session));
        if (session.Root is null)
            throw new InvalidOperationException("session.Root is null. Credit が選択されていません。");

        // 旧テキストを再パースして oldParsed を得る。Encoder のラウンドトリップ性により、
        var oldParsed = CreditBulkInputParser.Parse(oldText ?? string.Empty);
        await ResolveAsync(oldParsed, ct).ConfigureAwait(false);

        // Card 階層を i 番目で降下。
        var draftCards = session.Root.Cards
            .Where(c => c.State != DraftState.Deleted)
            .OrderBy(c => c.Entity.CardSeq)
            .ToList();

        int maxCards = Math.Max(oldParsed.Cards.Count,
            Math.Max(newParsed.Cards.Count, draftCards.Count));

        for (int ci = 0; ci < maxCards; ci++)
        {
            ParsedCard? oldCard = ci < oldParsed.Cards.Count ? oldParsed.Cards[ci] : null;
            ParsedCard? newCard = ci < newParsed.Cards.Count ? newParsed.Cards[ci] : null;
            DraftCard? draftCard = ci < draftCards.Count ? draftCards[ci] : null;

            if (newCard is null)
            {
                // 新側に対応 Card 無し → Card 削除。
                if (draftCard is not null)
                {
                    draftCard.MarkDeleted();
                    session.Root.Cards.Remove(draftCard);
                    session.DeletedCards.Add(draftCard);
                }
                continue;
            }

            if (oldCard is null || draftCard is null)
            {
                // 旧側または Draft 側に対応 Card 無し → Card 新規追加。
                var addedCard = AppendNewCard(session, session.Root);
                await ApplyCardAsync(newCard, session, addedCard, updatedBy, ct).ConfigureAwait(false);
                continue;
            }

            // 共通 Card: 完全一致なら Unchanged 維持、不一致なら降下。
            if (string.Equals(SerializeCardForCompare(oldCard), SerializeCardForCompare(newCard), StringComparison.Ordinal))
            {
                continue;
            }
            await ApplyDiffCardAsync(oldCard, newCard, session, draftCard, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Card 階層の差分適用（Notes 比較 + Tier 階層を i 番目で降下）。</summary>
    private async Task ApplyDiffCardAsync(
        ParsedCard oldCard, ParsedCard newCard,
        CreditDraftSession session, DraftCard draftCard,
        string? updatedBy, CancellationToken ct)
    {
        // Card 自身の Notes 比較（不一致なら Modified）。
        ApplyNotesIfChanged(draftCard, newCard.Notes,
            n => draftCard.Entity.Notes = n,
            () => draftCard.Entity.Notes);

        var draftTiers = draftCard.Tiers
            .Where(t => t.State != DraftState.Deleted)
            .OrderBy(t => t.Entity.TierNo)
            .ToList();

        int maxTiers = Math.Max(oldCard.Tiers.Count,
            Math.Max(newCard.Tiers.Count, draftTiers.Count));

        for (int ti = 0; ti < maxTiers; ti++)
        {
            ParsedTier? oldTier = ti < oldCard.Tiers.Count ? oldCard.Tiers[ti] : null;
            ParsedTier? newTier = ti < newCard.Tiers.Count ? newCard.Tiers[ti] : null;
            DraftTier? draftTier = ti < draftTiers.Count ? draftTiers[ti] : null;

            if (newTier is null)
            {
                if (draftTier is not null)
                {
                    draftTier.MarkDeleted();
                    draftCard.Tiers.Remove(draftTier);
                    session.DeletedTiers.Add(draftTier);
                }
                continue;
            }
            if (oldTier is null || draftTier is null)
            {
                // Tier は最大 2 個（仕様）。3 つ目以降は無視（ApplyCardAsync と同じ挙動）。
                if (draftCard.Tiers.Count >= 2) break;
                var addedTier = AppendNewTier(session, draftCard);
                await ApplyTierAsync(newTier, session, addedTier, updatedBy, ct).ConfigureAwait(false);
                continue;
            }
            if (string.Equals(SerializeTierForCompare(oldTier), SerializeTierForCompare(newTier), StringComparison.Ordinal))
            {
                continue;
            }
            await ApplyDiffTierAsync(oldTier, newTier, session, draftTier, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Tier 階層の差分適用（Notes 比較 + Group 階層を i 番目で降下）。</summary>
    private async Task ApplyDiffTierAsync(
        ParsedTier oldTier, ParsedTier newTier,
        CreditDraftSession session, DraftTier draftTier,
        string? updatedBy, CancellationToken ct)
    {
        ApplyNotesIfChanged(draftTier, newTier.Notes,
            n => draftTier.Entity.Notes = n,
            () => draftTier.Entity.Notes);

        var draftGroups = draftTier.Groups
            .Where(g => g.State != DraftState.Deleted)
            .OrderBy(g => g.Entity.GroupNo)
            .ToList();

        int maxGroups = Math.Max(oldTier.Groups.Count,
            Math.Max(newTier.Groups.Count, draftGroups.Count));

        for (int gi = 0; gi < maxGroups; gi++)
        {
            ParsedGroup? oldGroup = gi < oldTier.Groups.Count ? oldTier.Groups[gi] : null;
            ParsedGroup? newGroup = gi < newTier.Groups.Count ? newTier.Groups[gi] : null;
            DraftGroup? draftGroup = gi < draftGroups.Count ? draftGroups[gi] : null;

            if (newGroup is null)
            {
                if (draftGroup is not null)
                {
                    draftGroup.MarkDeleted();
                    draftTier.Groups.Remove(draftGroup);
                    session.DeletedGroups.Add(draftGroup);
                }
                continue;
            }
            if (oldGroup is null || draftGroup is null)
            {
                var addedGroup = AppendNewGroup(session, draftTier);
                await ApplyGroupAsync(newGroup, session, addedGroup, updatedBy, ct).ConfigureAwait(false);
                continue;
            }
            if (string.Equals(SerializeGroupForCompare(oldGroup), SerializeGroupForCompare(newGroup), StringComparison.Ordinal))
            {
                continue;
            }
            await ApplyDiffGroupAsync(oldGroup, newGroup, session, draftGroup, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Group 階層の差分適用（Notes 比較 + Role 階層を role_code 辞書マッチングで降下）。 同 Group 内では role_code は UNIQUE 前提で、辞書ベースで対応付けする。</summary>
    private async Task ApplyDiffGroupAsync(
        ParsedGroup oldGroup, ParsedGroup newGroup,
        CreditDraftSession session, DraftGroup draftGroup,
        string? updatedBy, CancellationToken ct)
    {
        ApplyNotesIfChanged(draftGroup, newGroup.Notes,
            n => draftGroup.Entity.Notes = n,
            () => draftGroup.Entity.Notes);

        // 旧 Role と Draft Role を role_code で辞書化。
        // ParsedRole.ResolvedRoleCode は ResolveAsync 後にセットされている。
        // Encoder ラウンドトリップ性により、旧 Role[i] と Draft Role[i] は位置で 1:1 対応するため、
        // インデックスから Draft を引く形で辞書を作る。
        // CreditCardRole の同 Group 内の並び順は OrderInGroup（byte）列で表現されるため、
        // 並び替えは OrderInGroup 昇順で行う（MySQL 列名 order_in_group に対応）。
        var draftRoles = draftGroup.Roles
            .Where(r => r.State != DraftState.Deleted)
            .OrderBy(r => r.Entity.OrderInGroup)
            .ToList();

        var oldByCode = new Dictionary<string, (ParsedRole Old, DraftRole Draft)>(StringComparer.Ordinal);
        for (int i = 0; i < oldGroup.Roles.Count && i < draftRoles.Count; i++)
        {
            var pr = oldGroup.Roles[i];
            if (!string.IsNullOrEmpty(pr.ResolvedRoleCode))
            {
                // 同 Group 内 UNIQUE 前提だが、万一重複していたら最初の方を採用（後の方は辞書追加で上書きされない）。
                if (!oldByCode.ContainsKey(pr.ResolvedRoleCode))
                {
                    oldByCode[pr.ResolvedRoleCode] = (pr, draftRoles[i]);
                }
            }
        }

        // 新 Role を順に処理。マッチした role_code を記録して、未マッチ旧 Role は最後に Deleted。
        var matchedCodes = new HashSet<string>(StringComparer.Ordinal);
        int newRoleSeq = 1;
        foreach (var newRole in newGroup.Roles)
        {
            if (string.IsNullOrEmpty(newRole.ResolvedRoleCode)) continue;
            matchedCodes.Add(newRole.ResolvedRoleCode);

            if (oldByCode.TryGetValue(newRole.ResolvedRoleCode, out var pair))
            {
                // 既存 Role: シリアライズ完全一致なら Unchanged 維持、それ以外は降下。
                if (!string.Equals(SerializeRoleForCompare(pair.Old), SerializeRoleForCompare(newRole), StringComparison.Ordinal))
                {
                    await ApplyDiffRoleAsync(pair.Old, newRole, session, pair.Draft, updatedBy, ct).ConfigureAwait(false);
                }
                // 順序変更時のみ Modified。CreditCardRole.OrderInGroup は byte 型。
                byte targetSeq = (byte)newRoleSeq;
                if (pair.Draft.Entity.OrderInGroup != targetSeq)
                {
                    pair.Draft.Entity.OrderInGroup = targetSeq;
                    pair.Draft.MarkModified();
                }
            }
            else
            {
                // 新規 Role: 切り出した共通ヘルパで追加する。
                await ApplyParsedRoleNewAsync(newRole, session, draftGroup, updatedBy, ct).ConfigureAwait(false);
                // 末尾に追加された Role の OrderInGroup をセット（AppendNewRole は seq=末尾+1 を振るが、
                // 既存 Role の seq 並び替えが入る場合に備えて明示）。
                var lastAdded = draftGroup.Roles[^1];
                lastAdded.Entity.OrderInGroup = (byte)newRoleSeq;
            }
            newRoleSeq++;
        }

        // 旧側で未マッチ = 削除された Role。
        foreach (var (code, pair) in oldByCode)
        {
            if (matchedCodes.Contains(code)) continue;
            pair.Draft.MarkDeleted();
            draftGroup.Roles.Remove(pair.Draft);
            session.DeletedRoles.Add(pair.Draft);
        }
    }

    /// <summary>Role 階層の差分適用（Notes 比較 + Block 階層を i 番目で降下）。</summary>
    private async Task ApplyDiffRoleAsync(
        ParsedRole oldRole, ParsedRole newRole,
        CreditDraftSession session, DraftRole draftRole,
        string? updatedBy, CancellationToken ct)
    {
        ApplyNotesIfChanged(draftRole, newRole.Notes,
            n => draftRole.Entity.Notes = n,
            () => draftRole.Entity.Notes);

        // 所属表記レイアウト変更の追従（SUFFIX ⇔ PREFIX）。
        if (!string.Equals(draftRole.Entity.AffiliationLayout, newRole.AffiliationLayout, StringComparison.Ordinal))
        {
            draftRole.Entity.AffiliationLayout = newRole.AffiliationLayout;
            draftRole.MarkModified();
        }

        var draftBlocks = draftRole.Blocks
            .Where(b => b.State != DraftState.Deleted)
            .OrderBy(b => b.Entity.BlockSeq)
            .ToList();

        int maxBlocks = Math.Max(oldRole.Blocks.Count,
            Math.Max(newRole.Blocks.Count, draftBlocks.Count));

        for (int bi = 0; bi < maxBlocks; bi++)
        {
            ParsedBlock? oldBlock = bi < oldRole.Blocks.Count ? oldRole.Blocks[bi] : null;
            ParsedBlock? newBlock = bi < newRole.Blocks.Count ? newRole.Blocks[bi] : null;
            DraftBlock? draftBlock = bi < draftBlocks.Count ? draftBlocks[bi] : null;

            if (newBlock is null)
            {
                if (draftBlock is not null)
                {
                    draftBlock.MarkDeleted();
                    draftRole.Blocks.Remove(draftBlock);
                    session.DeletedBlocks.Add(draftBlock);
                }
                continue;
            }
            if (oldBlock is null || draftBlock is null)
            {
                await ApplyParsedBlockNewAsync(newBlock, session, draftRole, newRole, updatedBy, ct).ConfigureAwait(false);
                continue;
            }
            if (string.Equals(SerializeBlockForCompare(oldBlock), SerializeBlockForCompare(newBlock), StringComparison.Ordinal))
            {
                continue;
            }
            await ApplyDiffBlockAsync(oldBlock, newBlock, session, draftBlock, newRole, updatedBy, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Block 階層の差分適用（Notes / col_count / leading_company 比較 + Entry 階層を Block 内 LCS で降下）。</summary>
    private async Task ApplyDiffBlockAsync(
        ParsedBlock oldBlock, ParsedBlock newBlock,
        CreditDraftSession session, DraftBlock draftBlock, ParsedRole parentRole,
        string? updatedBy, CancellationToken ct)
    {
        // Block 自身の属性比較。
        ApplyNotesIfChanged(draftBlock, newBlock.Notes,
            n => draftBlock.Entity.Notes = n,
            () => draftBlock.Entity.Notes);

        // col_count 比較（明示指定の有無に関わらず ColCount 値を直接見る）。
        byte newCols = (byte)newBlock.ColCount;
        if (draftBlock.Entity.ColCount != newCols)
        {
            draftBlock.Entity.ColCount = newCols;
            draftBlock.MarkModified();
        }

        // leading_company_alias_id 比較（旧/新の LeadingCompanyText 文字列が違うときだけ再解決）。
        if (!string.Equals(oldBlock.LeadingCompanyText, newBlock.LeadingCompanyText, StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(newBlock.LeadingCompanyText))
            {
                if (draftBlock.Entity.LeadingCompanyAliasId is not null)
                {
                    draftBlock.Entity.LeadingCompanyAliasId = null;
                    draftBlock.MarkModified();
                }
            }
            else
            {
                int? leadingAliasId = await ResolveOrCreateCompanyAliasAsync(
                    session, newBlock.LeadingCompanyText, oldName: null, updatedBy, ct).ConfigureAwait(false);
                if (leadingAliasId.HasValue && draftBlock.Entity.LeadingCompanyAliasId != leadingAliasId)
                {
                    draftBlock.Entity.LeadingCompanyAliasId = leadingAliasId;
                    draftBlock.MarkModified();
                }
                else if (!leadingAliasId.HasValue)
                {
                    InfoMessages.Add($"ブロック先頭企業 [{newBlock.LeadingCompanyText}] を登録できませんでした。Draft の値は変更しません。");
                }
            }
        }

        // Entry 階層を LCS マッチング（is_broadcast_only=false / true で 2 グループに分けて各々）。
        await ApplyDiffEntriesInBlockAsync(oldBlock, newBlock, session, draftBlock, parentRole, updatedBy, ct).ConfigureAwait(false);
    }

    /// <summary>Block 内の Entry 階層差分を LCS マッチングで適用。</summary>
    private async Task ApplyDiffEntriesInBlockAsync(
        ParsedBlock oldBlock, ParsedBlock newBlock,
        CreditDraftSession session, DraftBlock draftBlock, ParsedRole parentRole,
        string? updatedBy, CancellationToken ct)
    {
        // ParsedBlock の Rows をフラット化して Entry リストに変換（タブ区切り行は順序通りに展開）。
        var oldFlat = oldBlock.Rows.SelectMany(r => r.Entries).ToList();
        var newFlat = newBlock.Rows.SelectMany(r => r.Entries).ToList();

        // is_broadcast_only=false / true でグループ分け。
        var oldNormal = oldFlat.Where(e => !e.IsBroadcastOnly).ToList();
        var newNormal = newFlat.Where(e => !e.IsBroadcastOnly).ToList();
        var oldBroadcast = oldFlat.Where(e => e.IsBroadcastOnly).ToList();
        var newBroadcast = newFlat.Where(e => e.IsBroadcastOnly).ToList();

        // Draft Entry 側も同様に 2 グループ + entry_seq 順。
        var draftNormal = draftBlock.Entries
            .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
            .OrderBy(e => e.Entity.EntrySeq)
            .ToList();
        var draftBroadcast = draftBlock.Entries
            .Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly)
            .OrderBy(e => e.Entity.EntrySeq)
            .ToList();

        await ApplyEntryGroupDiffAsync(oldNormal, newNormal, draftNormal, session, draftBlock, parentRole, updatedBy, ct).ConfigureAwait(false);
        await ApplyEntryGroupDiffAsync(oldBroadcast, newBroadcast, draftBroadcast, session, draftBlock, parentRole, updatedBy, ct).ConfigureAwait(false);
    }

    /// <summary>is_broadcast_only グループ単位の Entry 差分を LCS で適用する。 旧 Entry の i 番目は Draft Entry の i 番目に対応する（Encoder ラウンドトリップ性）。</summary>
    private async Task ApplyEntryGroupDiffAsync(
        List<ParsedEntry> oldEntries, List<ParsedEntry> newEntries, List<DraftEntry> draftEntries,
        CreditDraftSession session, DraftBlock draftBlock, ParsedRole parentRole,
        string? updatedBy, CancellationToken ct)
    {
        // LCS マッチング（シリアライズ文字列ベース）。
        var pairs = LcsMatchEntries(oldEntries, newEntries);

        // 未マッチの旧 Entry のインデックス集合。
        var matchedOld = new HashSet<int>(pairs.Select(p => p.OldIdx));

        // Step 1: 未マッチ旧を Deleted バケットへ移送。
        // 注意: 旧 i 番目に対応する Draft は draftEntries[i]（位置ベース 1:1）。
        for (int oldIdx = 0; oldIdx < oldEntries.Count; oldIdx++)
        {
            if (matchedOld.Contains(oldIdx)) continue;
            if (oldIdx >= draftEntries.Count) break;
            var de = draftEntries[oldIdx];
            de.MarkDeleted();
            draftBlock.Entries.Remove(de);
            session.DeletedEntries.Add(de);
        }

        // Step 2: 新 Entry の並び順通りに entry_seq を 1..N で振り直す。
        var oldIdxByNewIdx = pairs.ToDictionary(p => p.NewIdx, p => p.OldIdx);
        for (int newIdx = 0; newIdx < newEntries.Count; newIdx++)
        {
            ushort targetSeq = (ushort)(newIdx + 1);
            if (oldIdxByNewIdx.TryGetValue(newIdx, out int oldIdx))
            {
                // マッチペア: 既存 Draft Entry の entry_seq を targetSeq に更新。
                if (oldIdx < draftEntries.Count)
                {
                    var de = draftEntries[oldIdx];
                    if (de.Entity.EntrySeq != targetSeq)
                    {
                        de.Entity.EntrySeq = targetSeq;
                        de.MarkModified();
                    }
                }
            }
            else
            {
                // 未マッチ新: 新規追加。AppendParsedEntryAsync は draftBlock.Entries の末尾に追加するので、
                // 直後に末尾要素の entry_seq を targetSeq に上書きする。
                int beforeCount = draftBlock.Entries.Count;
                await AppendParsedEntryAsync(session, draftBlock, newEntries[newIdx], parentRole, updatedBy, ct).ConfigureAwait(false);
                if (draftBlock.Entries.Count > beforeCount)
                {
                    var added = draftBlock.Entries[^1];
                    added.Entity.EntrySeq = targetSeq;
                }
            }
        }
    }

    /// <summary>Entry リスト 2 つの LCS マッチペア (oldIdx, newIdx) を返す。 シリアライズ文字列の完全一致で対応付けする。動的計画法 O(|old|×|new|)。</summary>
    private static List<(int OldIdx, int NewIdx)> LcsMatchEntries(
        List<ParsedEntry> oldEntries, List<ParsedEntry> newEntries)
    {
        int n = oldEntries.Count, m = newEntries.Count;
        if (n == 0 || m == 0) return new List<(int, int)>();

        var oldKeys = oldEntries.Select(SerializeEntryForCompare).ToList();
        var newKeys = newEntries.Select(SerializeEntryForCompare).ToList();

        var dp = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                dp[i, j] = string.Equals(oldKeys[i - 1], newKeys[j - 1], StringComparison.Ordinal)
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // バックトレースしてマッチペアを復元（昇順インデックスで返す）。
        var pairs = new List<(int, int)>();
        int x = n, y = m;
        while (x > 0 && y > 0)
        {
            if (string.Equals(oldKeys[x - 1], newKeys[y - 1], StringComparison.Ordinal))
            {
                pairs.Add((x - 1, y - 1));
                x--; y--;
            }
            else if (dp[x - 1, y] >= dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }
        pairs.Reverse();
        return pairs;
    }

    //   Serialize*ForCompare ヘルパ群

    /// <summary>Card のシリアライズ（配下 Tier 群を含む全文）。</summary>
    private static string SerializeCardForCompare(ParsedCard c)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("C|notes=").Append(c.Notes ?? string.Empty).Append('\n');
        foreach (var t in c.Tiers) sb.Append(SerializeTierForCompare(t));
        return sb.ToString();
    }

    /// <summary>Tier のシリアライズ（配下 Group 群を含む全文）。</summary>
    private static string SerializeTierForCompare(ParsedTier t)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("T|notes=").Append(t.Notes ?? string.Empty).Append('\n');
        foreach (var g in t.Groups) sb.Append(SerializeGroupForCompare(g));
        return sb.ToString();
    }

    /// <summary>Group のシリアライズ（配下 Role 群を含む全文）。</summary>
    private static string SerializeGroupForCompare(ParsedGroup g)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("G|notes=").Append(g.Notes ?? string.Empty).Append('\n');
        foreach (var r in g.Roles) sb.Append(SerializeRoleForCompare(r));
        return sb.ToString();
    }

    /// <summary>Role のシリアライズ（配下 Block 群を含む全文）。</summary>
    private static string SerializeRoleForCompare(ParsedRole r)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("R|code=").Append(r.ResolvedRoleCode ?? r.DisplayName)
          .Append("|notes=").Append(r.Notes ?? string.Empty)
          .Append("|affil=").Append(r.AffiliationLayout).Append('\n');
        foreach (var b in r.Blocks) sb.Append(SerializeBlockForCompare(b));
        return sb.ToString();
    }

    /// <summary>Block のシリアライズ（配下 Entry 群を含む全文）。</summary>
    private static string SerializeBlockForCompare(ParsedBlock b)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("B|cols=").Append(b.ColCount)
          .Append("|leading=").Append(b.LeadingCompanyText ?? string.Empty)
          .Append("|notes=").Append(b.Notes ?? string.Empty).Append('\n');
        foreach (var row in b.Rows)
        {
            foreach (var e in row.Entries) sb.Append(SerializeEntryForCompare(e));
        }
        return sb.ToString();
    }

    /// <summary>Entry のシリアライズ（LCS マッチングのキーとしても使う）。</summary>
    private static string SerializeEntryForCompare(ParsedEntry e)
    {
        // 各種 RawText / 修飾子を pipe 区切りで連結。LineNumber は除外（行番号は本質ではない）。
        var sb = new System.Text.StringBuilder();
        sb.Append("E|");
        sb.Append((int)e.Kind).Append('|');
        sb.Append(e.PersonRawText ?? string.Empty).Append('|');
        sb.Append(e.PersonOldName ?? string.Empty).Append('|');
        sb.Append(e.CharacterRawText ?? string.Empty).Append('|');
        sb.Append(e.CharacterOldName ?? string.Empty).Append('|');
        sb.Append(e.IsForcedNewCharacter ? '1' : '0').Append('|');
        sb.Append(e.CompanyRawText ?? string.Empty).Append('|');
        sb.Append(e.CompanyOldName ?? string.Empty).Append('|');
        sb.Append(e.LogoCiVersionLabel ?? string.Empty).Append('|');
        sb.Append(e.AffiliationRawText ?? string.Empty).Append('|');
        sb.Append(e.AffiliationOverrideText ?? string.Empty).Append('|');
        sb.Append(e.AffiliationForceText ? '1' : '0').Append('|');
        sb.Append(e.AffiliationInline ? '1' : '0').Append('|');
        sb.Append(e.IsBroadcastOnly ? '1' : '0').Append('|');
        sb.Append(e.IsParallelContinuation ? '1' : '0').Append('|');
        sb.Append(e.Notes ?? string.Empty).Append('|');
        // 誤記もシリアライズに含めて差分検出の対象にする
        // （誤記の追加・削除・修正をエントリ差分として認識させるため）。
        sb.Append(e.PersonMisprintText ?? string.Empty).Append('|');
        sb.Append(e.CharacterMisprintText ?? string.Empty).Append('|');
        sb.Append(e.CompanyMisprintText ?? string.Empty).Append('\n');
        return sb.ToString();
    }
}

/// <summary>Apply 経路で「未解決の所属屋号」を表す軽量レコード。 警告ペインの行データに変換され、ダブルクリックで <see cref="Dialogs.QuickAddCompanyAliasDialog"/> を起動するためのテキスト + 行番号セット。</summary>
public sealed record UnresolvedAffiliation(string Name, int LineNumber);