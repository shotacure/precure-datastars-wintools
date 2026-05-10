using System.Net;
using System.Text;
using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.TemplateRendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// クレジット 1 件分の階層を HTML 化するレンダラ（プレビュー完全準拠版）。
/// <para>
/// Catalog 側 <c>PrecureDataStars.Catalog.Forms.Preview.CreditPreviewRenderer</c> の
/// DB ベース描画 (<c>RenderOneCreditFromDbAsync</c>) と同一のロジックで HTML を生成する。
/// </para>
/// <list type="bullet">
///   <item><description>テンプレ DSL の役職テンプレ展開（<see cref="RoleTemplateRenderer"/>）。
///     <c>role_templates</c> から (role_code, series_id) → (role_code, NULL) フォールバックで
///     書式テンプレを引き、<c>{#BLOCKS}…{/BLOCKS}</c> や <c>{#THEME_SONGS:opts}…{/THEME_SONGS}</c>
///     を含む DSL を評価する。</description></item>
///   <item><description>テンプレ未定義時のフォールバック描画（<c>fallback-table</c> = 役職名 | エントリ右並び、
///     col_count カラム横並び、<c>leading_company</c> はブロック先頭行で太字なし、後続行は字下げ）。</description></item>
///   <item><description>VOICE_CAST 役職用 3 カラムフォールバック（<c>fallback-vc-table</c> = 役職名 | キャラ | 声優、
///     直前行と同キャラ名なら dim 空セル、<c>leading_company</c> 字下げ対応）。</description></item>
///   <item><description>CASTING_COOPERATION 役職を VOICE_CAST テーブル末尾に「協力」行として追記。
///     役職名カラム（右寄せ）に「協力」を置き、屋号一覧は声優名カラムに置く 3 セル構成。</description></item>
///   <item><description>絵コンテ・演出融合表示（<c>series.hide_storyboard_role = 1</c> + 同 Group 内に
///     STORYBOARD と EPISODE_DIRECTOR が 1 件ずつ + 各エントリ 1 件）→
///     同名なら役職名「（絵コンテ・）演出」の 1 行、異名なら役職名「演出」+ 2 行（絵コンテ→演出）。</description></item>
///   <item><description>VOICE_CAST 役職名のカード/Tier/Group 跨ぎ抑止
///     （直前ロールが同一 role_code の VOICE_CAST なら役職名カラムを空表示）。</description></item>
///   <item><description><c>is_broadcast_only=1</c> エントリは描画対象外。</description></item>
///   <item><description>テンプレ展開結果が <c>{ROLE_NAME}</c> を含まない場合、
///     <c>fallback-table</c> と同じ「役職名 + 内容」の 2 カラムテーブルで自動ラップ。</description></item>
/// </list>
/// <para>
/// クレジット内の各表示要素はそれぞれの詳細ページへリンク化される：
/// </para>
/// <list type="bullet">
///   <item><description>役職名 → <c>/stats/roles/{role_code}/</c>（VOICE_CAST 系は <c>/stats/voice-cast/</c>）</description></item>
///   <item><description>人物名義 → <c>/persons/{person_id}/</c>（共有名義は <see cref="StaffNameLinkResolver"/> 経由で添字付き複数リンク化）</description></item>
///   <item><description>企業屋号 → <c>/companies/{company_id}/</c>（屋号 → 親 company_id を <see cref="LookupCache.LookupCompanyIdFromAliasAsync"/> で解決）</description></item>
///   <item><description>ロゴ → 屋号名に置換したうえで <c>/companies/{company_id}/</c> へリンク（CI バージョンラベルは付けない）</description></item>
/// </list>
/// </summary>
internal sealed class CreditTreeRenderer
{
    private readonly IConnectionFactory _factory;
    private readonly RolesRepository _rolesRepo;
    private readonly RoleTemplatesRepository _roleTemplatesRepo;
    private readonly CreditCardsRepository _cardsRepo;
    private readonly CreditCardTiersRepository _tiersRepo;
    private readonly CreditCardGroupsRepository _groupsRepo;
    private readonly CreditCardRolesRepository _cardRolesRepo;
    private readonly CreditRoleBlocksRepository _blocksRepo;
    private readonly CreditBlockEntriesRepository _entriesRepo;
    private readonly LookupCache _lookup;

    /// <summary>
    /// 人物名義 → 人物詳細ページ HTML リンクの解決器。
    /// クレジット内のすべての人物表記をリンク化するために使う。
    /// 共有名義（1 名義 → 複数 person）は本リゾルバ側で「[1] [2]」付き複数リンクに展開される。
    /// </summary>
    private readonly StaffNameLinkResolver _staffLinkResolver;

    /// <summary>役職コード（v1.2.1 シードで投入される）。</summary>
    private const string RoleCodeStoryboard = "STORYBOARD";
    private const string RoleCodeEpisodeDirector = "EPISODE_DIRECTOR";
    private const string RoleCodeCastingCooperation = "CASTING_COOPERATION";

    public CreditTreeRenderer(
        IConnectionFactory factory,
        RolesRepository rolesRepo,
        RoleTemplatesRepository roleTemplatesRepo,
        CreditCardsRepository cardsRepo,
        CreditCardTiersRepository tiersRepo,
        CreditCardGroupsRepository groupsRepo,
        CreditCardRolesRepository cardRolesRepo,
        CreditRoleBlocksRepository blocksRepo,
        CreditBlockEntriesRepository entriesRepo,
        LookupCache lookup,
        StaffNameLinkResolver staffLinkResolver)
    {
        _factory = factory;
        _rolesRepo = rolesRepo;
        _roleTemplatesRepo = roleTemplatesRepo;
        _cardsRepo = cardsRepo;
        _tiersRepo = tiersRepo;
        _groupsRepo = groupsRepo;
        _cardRolesRepo = cardRolesRepo;
        _blocksRepo = blocksRepo;
        _entriesRepo = entriesRepo;
        _lookup = lookup;
        _staffLinkResolver = staffLinkResolver;
    }

    private static string Esc(string s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>
    /// 役職名 1 つをリンク済み HTML に変換する。
    /// 空文字の場合はそのまま空文字を返す（フォールバック表で 2 行目以降の役職名カラム抑止に使う）。
    /// VOICE_CAST 系役職は専用統計ページ <c>/stats/voice-cast/</c> へ、それ以外は
    /// <c>/stats/roles/{role_code}/</c> へリンクする。<paramref name="roleCode"/> が NULL/空の場合は
    /// リンク化せずエスケープのみ。
    /// </summary>
    private static string BuildRoleNameHtml(string? roleCode, string roleName, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (string.IsNullOrEmpty(roleName)) return "";
        if (string.IsNullOrEmpty(roleCode)) return Esc(roleName);

        // VOICE_CAST フォーマット種の役職は /stats/voice-cast/ に集約しているため、そちらに飛ばす。
        // それ以外は /stats/roles/{role_code}/ の役職別ランキング詳細ページへ。
        bool isVoiceCast = roleMap.TryGetValue(roleCode!, out var r)
                           && string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal);
        string url = isVoiceCast ? "/stats/voice-cast/" : PathUtil.RoleStatsUrl(roleCode!);
        return $"<a href=\"{url}\">{Esc(roleName)}</a>";
    }

    /// <summary>
    /// 企業屋号（company_alias）の表示名を、親企業詳細ページへのリンク済み HTML に変換する。
    /// alias_id が指す company_id を <see cref="LookupCache"/> 経由で解決し、解決できれば
    /// <c>/companies/{company_id}/</c> 行きの <c>&lt;a&gt;</c> に包む。失敗時はエスケープのみ。
    /// </summary>
    private async Task<string> BuildCompanyAliasHtmlAsync(int? aliasId, string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "";
        if (!aliasId.HasValue) return Esc(displayName);
        int? cid = await _lookup.LookupCompanyIdFromAliasAsync(aliasId.Value).ConfigureAwait(false);
        if (!cid.HasValue) return Esc(displayName);
        return $"<a href=\"{PathUtil.CompanyUrl(cid.Value)}\">{Esc(displayName)}</a>";
    }

    /// <summary>
    /// ロゴエントリの表示を「屋号名に置換 + 親企業詳細ページへのリンク」に変換する。
    /// CI バージョンラベルは省く方針（屋号単位で集約した方が読み手にとって分かりやすいため）。
    /// 解決失敗時はプレースホルダ文字列を返す。
    /// </summary>
    private async Task<string> BuildLogoHtmlAsync(int? logoId)
    {
        if (!logoId.HasValue) return Esc("(ロゴ未指定)");
        var lg = await _lookup.GetLogoForRenderingAsync(logoId.Value).ConfigureAwait(false);
        if (lg is null) return Esc("(ロゴ不明)");
        string? aliasName = await _lookup.LookupCompanyAliasNameAsync(lg.CompanyAliasId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(aliasName)) return Esc("(屋号不明)");
        return await BuildCompanyAliasHtmlAsync(lg.CompanyAliasId, aliasName).ConfigureAwait(false);
    }

    /// <summary>クレジット 1 件のクレジット種別見出し下を描画する。</summary>
    /// <remarks>
    /// プレビューと違い、見出し（&lt;h1&gt;）はテンプレ側で出すため本メソッドでは出さない。
    /// </remarks>
    public async Task<string> RenderAsync(Credit credit, BuildLogger logger, CancellationToken ct = default)
    {
        var html = new StringBuilder();

        var cards = (await _cardsRepo.GetByCreditAsync(credit.CreditId, ct).ConfigureAwait(false))
            .OrderBy(c => c.CardSeq).ToList();
        if (cards.Count == 0)
        {
            html.Append("<p class=\"empty-credit\">（カード未登録）</p>");
            return html.ToString();
        }

        var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var roleMap = allRoles.ToDictionary(r => r.RoleCode);
        int? resolveSeriesId = await ResolveTemplateSeriesIdAsync(credit, ct).ConfigureAwait(false);

        bool hideStoryboardRole = await GetHideStoryboardRoleAsync(resolveSeriesId, ct).ConfigureAwait(false);
        string? prevVoiceCastRoleCode = null;

        foreach (var card in cards)
        {
            html.Append("<div class=\"card\">");
            var tiers = (await _tiersRepo.GetByCardAsync(card.CardId, ct).ConfigureAwait(false))
                .OrderBy(t => t.TierNo).ToList();

            var cooperationContext = await CollectCardCastingCooperationContextAsync(
                card.CardId, tiers, roleMap, ct).ConfigureAwait(false);
            IReadOnlyList<CreditBlockEntry>? cooperationEntriesForCard = cooperationContext?.Entries;
            int? cooperationAppendTargetCardRoleId = cooperationContext?.LastVoiceCastCardRoleId;

            foreach (var tier in tiers)
            {
                html.Append("<div class=\"tier\">");
                var groups = (await _groupsRepo.GetByTierAsync(tier.CardTierId, ct).ConfigureAwait(false))
                    .OrderBy(g => g.GroupNo).ToList();
                foreach (var grp in groups)
                {
                    html.Append("<div class=\"group\">");
                    var cardRoles = (await _cardRolesRepo.GetByGroupAsync(grp.CardGroupId, ct).ConfigureAwait(false))
                        .OrderBy(r => r.OrderInGroup).ToList();

                    // 絵コンテ・演出融合判定
                    HashSet<int> mergedCardRoleIds = new();
                    if (hideStoryboardRole &&
                        TryDetectMergeableStoryboardDirector(cardRoles, r => r.RoleCode,
                            out var sbRole, out var dirRole))
                    {
                        var sbEntries = await CollectEntriesUnderCardRoleAsync(sbRole!.CardRoleId, ct).ConfigureAwait(false);
                        var dirEntries = await CollectEntriesUnderCardRoleAsync(dirRole!.CardRoleId, ct).ConfigureAwait(false);
                        if (sbEntries.Count == 1 && dirEntries.Count == 1)
                        {
                            await RenderStoryboardDirectorMergedAsync(sbEntries, dirEntries, roleMap, html, ct).ConfigureAwait(false);
                            mergedCardRoleIds.Add(sbRole.CardRoleId);
                            mergedCardRoleIds.Add(dirRole.CardRoleId);
                            prevVoiceCastRoleCode = null;
                        }
                    }

                    // v1.3.0 stage 19: 同 Group 内の sibling 役職を role_code → BlockSnapshot[] 辞書化。
                    // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文は、同 Group 内の別役職の Block 群を
                    // 引いて内側プレースホルダを評価するため、Group 配下の全役職について事前に Block と
                    // Entry をロードして辞書を作る。各役職の処理時に SiblingRoleResolver として TemplateContext に
                    // 渡すことで、レンダラ側から sibling 役職の中身が透過的に見える。
                    // 同 role_code が複数役職に重複している場合は最初の 1 つを採用（同 Group 内重複は通常起き得ない）。
                    var siblingBlocksByRoleCode = new Dictionary<string, IReadOnlyList<BlockSnapshot>>(StringComparer.Ordinal);
                    foreach (var siblingRole in cardRoles)
                    {
                        if (string.IsNullOrEmpty(siblingRole.RoleCode)) continue;
                        if (siblingBlocksByRoleCode.ContainsKey(siblingRole.RoleCode!)) continue;

                        var sbBlocks = (await _blocksRepo.GetByCardRoleAsync(siblingRole.CardRoleId, ct).ConfigureAwait(false))
                            .OrderBy(b => b.BlockSeq).ToList();
                        var sbSnapshots = new List<BlockSnapshot>();
                        foreach (var b in sbBlocks)
                        {
                            var entries = (await _entriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                .Where(e => !e.IsBroadcastOnly)
                                .OrderBy(e => e.EntrySeq).ToList();
                            sbSnapshots.Add(new BlockSnapshot(b, entries));
                        }
                        siblingBlocksByRoleCode[siblingRole.RoleCode!] = sbSnapshots;
                    }
                    // クロージャで辞書を捕捉。null 時は空文字に展開される（レンダラの仕様）。
                    Func<string, IReadOnlyList<BlockSnapshot>?> siblingResolver = code =>
                        siblingBlocksByRoleCode.TryGetValue(code, out var s) ? s : null;

                    foreach (var cr in cardRoles)
                    {
                        if (mergedCardRoleIds.Contains(cr.CardRoleId)) continue;

                        // CASTING_COOPERATION 役職本体は描画スキップ（VOICE_CAST 末尾追記される）
                        if (cooperationEntriesForCard is not null
                            && cooperationEntriesForCard.Count > 0
                            && string.Equals(cr.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // v1.3.0 stage 19: sibling 辞書から自身の Block を引いて使い回し（重複ロード回避）。
                        // 辞書に無い役職（RoleCode が null など）は従来通り個別ロード。
                        IReadOnlyList<BlockSnapshot> snapshots;
                        if (!string.IsNullOrEmpty(cr.RoleCode)
                            && siblingBlocksByRoleCode.TryGetValue(cr.RoleCode!, out var cached))
                        {
                            snapshots = cached;
                        }
                        else
                        {
                            var blocks = (await _blocksRepo.GetByCardRoleAsync(cr.CardRoleId, ct).ConfigureAwait(false))
                                .OrderBy(b => b.BlockSeq).ToList();
                            var snList = new List<BlockSnapshot>();
                            foreach (var b in blocks)
                            {
                                var entries = (await _entriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                    .Where(e => !e.IsBroadcastOnly)
                                    .OrderBy(e => e.EntrySeq).ToList();
                                snList.Add(new BlockSnapshot(b, entries));
                            }
                            snapshots = snList;
                        }

                        bool suppressVoiceCastRoleName =
                            !string.IsNullOrEmpty(cr.RoleCode)
                            && prevVoiceCastRoleCode is not null
                            && string.Equals(prevVoiceCastRoleCode, cr.RoleCode, StringComparison.Ordinal)
                            && IsVoiceCastRole(cr.RoleCode, roleMap);

                        IReadOnlyList<CreditBlockEntry>? appendThisRole =
                            (IsVoiceCastRole(cr.RoleCode, roleMap)
                             && cooperationAppendTargetCardRoleId is int targetId
                             && targetId == cr.CardRoleId)
                                ? cooperationEntriesForCard
                                : null;

                        await RenderCardRoleCommonAsync(credit.ScopeKind, credit.EpisodeId, credit.CreditKind,
                            cr.RoleCode, roleMap, resolveSeriesId, snapshots,
                            suppressVoiceCastRoleName, appendThisRole, siblingResolver, html, ct).ConfigureAwait(false);

                        prevVoiceCastRoleCode = IsVoiceCastRole(cr.RoleCode, roleMap)
                            ? cr.RoleCode
                            : null;
                    }
                    html.Append("</div>"); // .group
                }
                html.Append("</div>"); // .tier
            }
            html.Append("</div>"); // .card
        }

        return html.ToString();
    }

    /// <summary>
    /// テンプレ解決用シリーズ ID を決める：SERIES スコープなら credit.SeriesId、
    /// EPISODE スコープなら episodes テーブルから所属シリーズ ID を逆引き。
    /// </summary>
    private async Task<int?> ResolveTemplateSeriesIdAsync(Credit credit, CancellationToken ct)
    {
        if (credit.ScopeKind == "SERIES") return credit.SeriesId;
        if (credit.EpisodeId is not int eid || eid <= 0) return null;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT series_id FROM episodes WHERE episode_id = @eid;",
            new { eid }, cancellationToken: ct));
    }

    private async Task<bool> GetHideStoryboardRoleAsync(int? seriesId, CancellationToken ct)
    {
        if (!seriesId.HasValue) return false;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var raw = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT hide_storyboard_role FROM series WHERE series_id = @id AND is_deleted = 0;",
            new { id = seriesId.Value }, cancellationToken: ct));
        return raw.GetValueOrDefault() != 0;
    }

    /// <summary>
    /// カード単位で「VOICE_CAST 役職」と「CASTING_COOPERATION 役職」の両方が居るか調べ、
    /// 両方ある場合は CASTING_COOPERATION 配下のエントリを集約 + カード内最後の VOICE_CAST 役職の
    /// CardRoleId を返す（プレビューと同一仕様）。
    /// </summary>
    private async Task<(List<CreditBlockEntry> Entries, int LastVoiceCastCardRoleId)?> CollectCardCastingCooperationContextAsync(
        int cardId,
        IReadOnlyList<CreditCardTier> tiersInCard,
        IReadOnlyDictionary<string, Role> roleMap,
        CancellationToken ct)
    {
        int? lastVcCardRoleId = null;
        var cooperationCardRoleIds = new List<int>();
        foreach (var tier in tiersInCard.OrderBy(t => t.TierNo))
        {
            var groups = (await _groupsRepo.GetByTierAsync(tier.CardTierId, ct).ConfigureAwait(false))
                .OrderBy(g => g.GroupNo);
            foreach (var grp in groups)
            {
                var roles = (await _cardRolesRepo.GetByGroupAsync(grp.CardGroupId, ct).ConfigureAwait(false))
                    .OrderBy(r => r.OrderInGroup);
                foreach (var cr in roles)
                {
                    if (IsVoiceCastRole(cr.RoleCode, roleMap)) lastVcCardRoleId = cr.CardRoleId;
                    if (string.Equals(cr.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        cooperationCardRoleIds.Add(cr.CardRoleId);
                }
            }
        }

        if (lastVcCardRoleId is null || cooperationCardRoleIds.Count == 0) return null;

        var aggregated = new List<CreditBlockEntry>();
        foreach (var crId in cooperationCardRoleIds)
        {
            var entries = await CollectEntriesUnderCardRoleAsync(crId, ct).ConfigureAwait(false);
            aggregated.AddRange(entries);
        }
        return (aggregated, lastVcCardRoleId.Value);
    }

    private async Task<List<CreditBlockEntry>> CollectEntriesUnderCardRoleAsync(int cardRoleId, CancellationToken ct)
    {
        var blocks = (await _blocksRepo.GetByCardRoleAsync(cardRoleId, ct).ConfigureAwait(false))
            .OrderBy(b => b.BlockSeq).ToList();
        var result = new List<CreditBlockEntry>();
        foreach (var b in blocks)
        {
            var entries = (await _entriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                .Where(e => !e.IsBroadcastOnly)
                .OrderBy(e => e.EntrySeq);
            result.AddRange(entries);
        }
        return result;
    }

    private static bool IsVoiceCastRole(string? roleCode, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (string.IsNullOrEmpty(roleCode)) return false;
        if (!roleMap.TryGetValue(roleCode, out var r)) return false;
        return string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal);
    }

    /// <summary>
    /// 役職 1 つの描画。テンプレ展開 → 失敗時または未定義時はフォールバック表へ。
    /// </summary>
    private async Task RenderCardRoleCommonAsync(
        string scopeKind,
        int? episodeId,
        string creditKind,
        string? roleCode,
        IReadOnlyDictionary<string, Role> roleMap,
        int? resolveSeriesId,
        IReadOnlyList<BlockSnapshot> blocks,
        bool suppressVoiceCastRoleName,
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        // v1.3.0 stage 19: 同 Group 内の sibling 役職を role_code で引くコールバック。
        // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文に使う。null の場合は ROLE 参照が空文字に展開される
        // （旧呼び出し経路の後方互換用）。
        Func<string, IReadOnlyList<BlockSnapshot>?>? siblingRoleResolver,
        StringBuilder html,
        CancellationToken ct)
    {
        string roleName = "";
        if (!string.IsNullOrEmpty(roleCode))
        {
            if (roleMap.TryGetValue(roleCode!, out var r))
            {
                roleName = r.NameJa ?? roleCode!;
                // v1.3.0 ブラッシュアップ stage 16 Phase 4：
                // roles.hide_role_name_in_credit=1 の役職は HTML クレジット階層上で
                // 左カラム（役職名セル）を空文字にして「役職名を出さない」表示にする。
                // CreditInvolvementIndex / 役職別ランキング / 企業詳細の関与一覧は
                // role_code ベースで動くため、本上書きは表示テンプレ側にだけ作用する。
                if (r.HideRoleNameInCredit == 1) roleName = "";
            }
            else
            {
                roleName = roleCode!;
            }
        }

        string? template = null;
        if (!string.IsNullOrEmpty(roleCode))
        {
            var tpl = await _roleTemplatesRepo.ResolveAsync(roleCode!, resolveSeriesId, ct).ConfigureAwait(false);
            template = tpl?.FormatTemplate;
        }

        html.Append("<div class=\"role\">");

        if (!string.IsNullOrWhiteSpace(template))
        {
            try
            {
                var ast = TemplateParser.Parse(template!);
                // v1.3.0 stage 19: sibling-role 解決のコールバックを渡して {ROLE:CODE.PLACEHOLDER} 構文に対応。
                var ctx = new TemplateContext(roleCode ?? "", roleName, blocks, scopeKind, episodeId, creditKind,
                    siblingRoleResolver: siblingRoleResolver,
                    visitedRoleCodes: null);
                string rendered = await RoleTemplateRenderer.RenderAsync(ast, ctx, _factory, _lookup, ct).ConfigureAwait(false);

                string normalized = rendered.Replace("\r\n", "\n").Replace("\r", "\n");
                string brTransformed = normalized.Replace("\n", "<br>");

                bool templateHasRoleName = template!.Contains("{ROLE_NAME}", StringComparison.Ordinal);
                if (templateHasRoleName)
                {
                    html.Append("<div class=\"role-rendered\">");
                    html.Append(brTransformed);
                    html.Append("</div>");
                }
                else
                {
                    // テンプレ結果が役職名を含まない場合の自動ラップ：fallback-table と同形の 2 列表で囲む。
                    // 役職名カラムは詳細ページにリンクして表示。
                    html.Append("<table class=\"fallback-table\"><tr>");
                    html.Append($"<td class=\"role-name\">{BuildRoleNameHtml(roleCode, roleName, roleMap)}</td>");
                    html.Append("<td class=\"entry-cell\">");
                    html.Append(brTransformed);
                    html.Append("</td></tr></table>");
                }
            }
            catch (Exception ex)
            {
                html.Append("<div class=\"role-rendered\">");
                html.Append($"<span class=\"render-error\">⚠ テンプレ展開エラー: {Esc(ex.Message)} — フォールバック表示に切り替え</span><br>");
                await RenderRoleFallbackDispatchAsync(roleCode, roleName, blocks, roleMap,
                    suppressVoiceCastRoleName, appendedCooperationEntries, html, ct).ConfigureAwait(false);
                html.Append("</div>");
            }
        }
        else
        {
            await RenderRoleFallbackDispatchAsync(roleCode, roleName, blocks, roleMap,
                suppressVoiceCastRoleName, appendedCooperationEntries, html, ct).ConfigureAwait(false);
        }

        html.Append("</div>"); // .role
    }

    private async Task RenderRoleFallbackDispatchAsync(
        string? roleCode, string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        IReadOnlyDictionary<string, Role> roleMap,
        bool suppressVoiceCastRoleName,
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        StringBuilder html, CancellationToken ct)
    {
        string formatKind = "NORMAL";
        if (!string.IsNullOrEmpty(roleCode) && roleMap.TryGetValue(roleCode, out var r))
        {
            formatKind = r.RoleFormatKind ?? "NORMAL";
        }

        if (string.Equals(formatKind, "VOICE_CAST", StringComparison.Ordinal))
        {
            await RenderVoiceCastFallbackAsync(roleCode, roleName, blocks, roleMap, suppressVoiceCastRoleName,
                appendedCooperationEntries, html, ct).ConfigureAwait(false);
        }
        else
        {
            await RenderRoleFallbackAsync(roleCode, roleName, blocks, roleMap, html, ct).ConfigureAwait(false);
        }
    }

    private static bool TryDetectMergeableStoryboardDirector<TRole>(
        IEnumerable<TRole> cardRoles,
        Func<TRole, string?> getRoleCode,
        out TRole? storyboardRole,
        out TRole? directorRole)
        where TRole : class
    {
        storyboardRole = null;
        directorRole = null;
        foreach (var r in cardRoles)
        {
            string? code = getRoleCode(r);
            if (string.Equals(code, RoleCodeStoryboard, StringComparison.Ordinal))
            {
                if (storyboardRole is not null) { storyboardRole = null; directorRole = null; return false; }
                storyboardRole = r;
            }
            else if (string.Equals(code, RoleCodeEpisodeDirector, StringComparison.Ordinal))
            {
                if (directorRole is not null) { storyboardRole = null; directorRole = null; return false; }
                directorRole = r;
            }
        }
        return storyboardRole is not null && directorRole is not null;
    }

    /// <summary>絵コンテ・演出融合表示。</summary>
    private async Task RenderStoryboardDirectorMergedAsync(
        IReadOnlyList<CreditBlockEntry> storyboardEntries,
        IReadOnlyList<CreditBlockEntry> directorEntries,
        IReadOnlyDictionary<string, Role> roleMap,
        StringBuilder html,
        CancellationToken ct)
    {
        if (storyboardEntries.Count != 1 || directorEntries.Count != 1) return;

        var sb = storyboardEntries[0];
        var dr = directorEntries[0];

        bool sameName = false;
        if (sb.PersonAliasId.HasValue && dr.PersonAliasId.HasValue)
        {
            sameName = sb.PersonAliasId.Value == dr.PersonAliasId.Value;
        }
        else
        {
            // 表示テキストの完全一致で判定（リンクではなくプレーンテキストで比較）。
            string sbText = await ResolvePersonWithAffiliationLabelAsync(sb, ct).ConfigureAwait(false);
            string drText = await ResolvePersonWithAffiliationLabelAsync(dr, ct).ConfigureAwait(false);
            sameName = string.Equals(sbText, drText, StringComparison.Ordinal);
        }

        // 融合表示の役職名は "演出" にあたるため、EPISODE_DIRECTOR の役職統計ページにリンクする
        // （他の役職名と同じくクリックで詳細ページに飛べるように統一）。
        string directorRoleName = roleMap.TryGetValue(RoleCodeEpisodeDirector, out var dirR)
            ? (dirR.NameJa ?? "演出")
            : "演出";

        // エントリ表示は人物名義をリンク化、所属屋号もリンク化した HTML 断片を作る。
        string directorHtml = await ResolvePersonWithAffiliationHtmlAsync(dr, ct).ConfigureAwait(false);

        html.Append("<div class=\"role\">");
        html.Append("<table class=\"fallback-table\"><tr>");
        if (sameName)
        {
            // 「（絵コンテ・）演出」というラベル全体は EPISODE_DIRECTOR にリンクする
            // （絵コンテ部分も同一人物に集約されるため、演出側にリンクを寄せて違和感が無い）。
            string mergedLabel = "(絵コンテ・)" + directorRoleName;
            html.Append($"<td class=\"role-name\">{BuildRoleNameHtml(RoleCodeEpisodeDirector, mergedLabel, roleMap)}</td>");
            html.Append("<td class=\"entry-cell\">");
            html.Append(directorHtml);
            html.Append("</td>");
        }
        else
        {
            string storyboardHtml = await ResolvePersonWithAffiliationHtmlAsync(sb, ct).ConfigureAwait(false);
            html.Append($"<td class=\"role-name\">{BuildRoleNameHtml(RoleCodeEpisodeDirector, directorRoleName, roleMap)}</td>");
            html.Append("<td class=\"entry-cell\">");
            html.Append($"{storyboardHtml} {Esc("(絵コンテ)")}<br>");
            html.Append($"{directorHtml} {Esc("(演出)")}");
            html.Append("</td>");
        }
        html.Append("</tr></table>");
        html.Append("</div>");
    }

    /// <summary>
    /// 通常フォールバック描画：役職名（左）+ ブロック内エントリ（右、col_count カラム横並び）。
    /// leading_company はブロック先頭行で太字なし、後続エントリ行は字下げ（全角SP 1 個分）。
    /// 役職名・名義・屋号・ロゴをそれぞれ詳細ページへリンク化する。
    /// </summary>
    private async Task RenderRoleFallbackAsync(
        string? roleCode,
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        IReadOnlyDictionary<string, Role> roleMap,
        StringBuilder html,
        CancellationToken ct)
    {
        string roleNameHtml = BuildRoleNameHtml(roleCode, roleName, roleMap);

        if (blocks.Count == 0 || blocks.All(b => b.Entries.Count == 0))
        {
            html.Append($"<table class=\"fallback-table\"><tr><td class=\"role-name\">{roleNameHtml}</td><td><span class=\"empty-credit\">（エントリ未登録）</span></td></tr></table>");
            return;
        }

        html.Append("<table class=\"fallback-table\">");
        bool firstRow = true;
        bool isFirstBlock = true;
        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;
            int cols = Math.Max(1, (int)bs.Block.ColCount);

            // leading_company（ブロック先頭の屋号）も詳細ページへのリンク付きで出す。
            string? leadingCompanyName = null;
            string leadingCompanyHtml = "";
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(leadingCompanyName))
                {
                    leadingCompanyHtml = await BuildCompanyAliasHtmlAsync(leadId, leadingCompanyName).ConfigureAwait(false);
                }
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            bool isFirstRowOfThisBlock = true;

            if (hasLeading)
            {
                bool addBreakClass = !isFirstBlock;
                html.Append(addBreakClass ? "<tr class=\"block-break\">" : "<tr>");
                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{roleNameHtml}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }
                html.Append($"<td class=\"entry-cell\" colspan=\"{cols}\">{leadingCompanyHtml}</td>");
                html.Append("</tr>");
                isFirstRowOfThisBlock = false;
            }

            for (int i = 0; i < bs.Entries.Count; i += cols)
            {
                bool addBreakClass = isFirstRowOfThisBlock && !isFirstBlock;
                html.Append(addBreakClass ? "<tr class=\"block-break\">" : "<tr>");
                isFirstRowOfThisBlock = false;
                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{roleNameHtml}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }
                for (int j = 0; j < cols; j++)
                {
                    html.Append("<td class=\"entry-cell\">");
                    if (i + j < bs.Entries.Count)
                    {
                        var e = bs.Entries[i + j];
                        string entryHtml = await ResolveEntryHtmlAsync(e, ct).ConfigureAwait(false);
                        if (hasLeading) html.Append("　"); // 全角SP 1 個分の字下げ
                        html.Append(entryHtml);
                    }
                    html.Append("</td>");
                }
                html.Append("</tr>");
            }
            isFirstBlock = false;
        }
        html.Append("</table>");
    }

    /// <summary>
    /// VOICE_CAST 役職用 3 カラムフォールバック（役職名 | キャラ名 | 声優名）。
    /// 同キャラ連続は dim 空セルで省略、leading_company は colspan=2 で見出し行 + 後続字下げ、
    /// CASTING_COOPERATION エントリは末尾に「協力」行として追記する。
    /// <para>
    /// 協力行のレイアウト：「協力」を役職名カラムに右寄せで置き、屋号一覧は声優名カラムに置く
    /// （3 カラム構成と整合性を取る）。役職名・名義・屋号はリンク化。
    /// </para>
    /// </summary>
    private async Task RenderVoiceCastFallbackAsync(
        string? roleCode,
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        IReadOnlyDictionary<string, Role> roleMap,
        bool suppressRoleName,
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        StringBuilder html,
        CancellationToken ct)
    {
        // suppress フラグが立っている時は 1 行目の役職名カラムも空表示。
        string roleNameHtmlFirst = suppressRoleName ? "" : BuildRoleNameHtml(roleCode, roleName, roleMap);

        if (blocks.Count == 0 || blocks.All(b => b.Entries.Count == 0))
        {
            html.Append($"<table class=\"fallback-vc-table\"><tr><td class=\"role-name\">{roleNameHtmlFirst}</td><td class=\"character-cell\"></td><td class=\"actor-cell\"><span class=\"empty-credit\">（エントリ未登録）</span></td></tr></table>");
            return;
        }

        html.Append("<table class=\"fallback-vc-table\">");
        bool firstRow = true;
        bool isFirstBlock = true;
        string? prevCharLabel = null;

        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;

            // leading_company もリンク化。
            string? leadingCompanyName = null;
            string leadingCompanyHtml = "";
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(leadingCompanyName))
                {
                    leadingCompanyHtml = await BuildCompanyAliasHtmlAsync(leadId, leadingCompanyName).ConfigureAwait(false);
                }
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            bool isFirstRowOfThisBlock = true;

            if (hasLeading)
            {
                bool addBreakClass = !isFirstBlock;
                html.Append(addBreakClass ? "<tr class=\"block-break\">" : "<tr>");
                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{roleNameHtmlFirst}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }
                html.Append($"<td class=\"character-cell\" colspan=\"2\">{leadingCompanyHtml}</td>");
                html.Append("</tr>");
                isFirstRowOfThisBlock = false;
                prevCharLabel = null;
            }

            foreach (var e in bs.Entries)
            {
                bool addBreakClass = isFirstRowOfThisBlock && !isFirstBlock;
                html.Append(addBreakClass ? "<tr class=\"block-break\">" : "<tr>");
                isFirstRowOfThisBlock = false;

                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{roleNameHtmlFirst}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }

                if (e.EntryKind == "CHARACTER_VOICE")
                {
                    string charLabel = await ResolveCharacterLabelAsync(e, ct).ConfigureAwait(false);
                    string actorHtml = await ResolvePersonWithAffiliationHtmlAsync(e, ct).ConfigureAwait(false);

                    bool sameAsPrev = prevCharLabel is not null
                        && string.Equals(charLabel, prevCharLabel, StringComparison.Ordinal);
                    if (sameAsPrev)
                    {
                        html.Append("<td class=\"character-cell dim\"></td>");
                    }
                    else
                    {
                        // キャラ名はリンク化しない方針（キャラ詳細リンクは将来対応）。
                        // 字下げ用の全角スペースを leading_company 直下行にだけ加える。
                        string charPrefix = hasLeading ? "　" : "";
                        html.Append($"<td class=\"character-cell\">{charPrefix}{Esc(charLabel)}</td>");
                    }
                    html.Append($"<td class=\"actor-cell\">{actorHtml}</td>");

                    prevCharLabel = charLabel;
                }
                else
                {
                    // CHARACTER_VOICE 以外（PERSON / COMPANY / LOGO / TEXT 等）は声優名カラム側に表示。
                    string entryHtml = await ResolveEntryHtmlAsync(e, ct).ConfigureAwait(false);
                    html.Append("<td class=\"character-cell\"></td>");
                    string actorPrefix = hasLeading ? "　" : "";
                    html.Append($"<td class=\"actor-cell\">{actorPrefix}{entryHtml}</td>");
                    prevCharLabel = null;
                }

                html.Append("</tr>");
            }
            isFirstBlock = false;
        }

        // 「協力」行の追記。
        // 役職名カラムに「協力」（右寄せ・太字、CSS で .cooperation-row td.role-name を装飾）を置き、
        // 屋号一覧は声優名カラム（actor-cell）に出す。キャラ名カラムは空。
        // 屋号は <a href="/companies/{cid}/"> でリンク化。
        if (appendedCooperationEntries is not null && appendedCooperationEntries.Count > 0)
        {
            var aliasHtmls = new List<string>();
            foreach (var e in appendedCooperationEntries)
            {
                string lbl = await ResolveEntryHtmlAsync(e, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lbl)) aliasHtmls.Add(lbl);
            }
            if (aliasHtmls.Count > 0)
            {
                html.Append("<tr class=\"cooperation-row\">");
                // 役職名カラムに「協力」を置く。CSS 側で右寄せ・太字を当てる。
                html.Append("<td class=\"role-name\">協力</td>");
                html.Append("<td class=\"character-cell\"></td>");
                html.Append("<td class=\"actor-cell\">");
                html.Append(string.Join("　", aliasHtmls));
                html.Append("</td>");
                html.Append("</tr>");
            }
        }

        html.Append("</table>");
    }

    private async Task<string> ResolveCharacterLabelAsync(CreditBlockEntry e, CancellationToken ct)
    {
        if (e.CharacterAliasId is int caId)
        {
            string? n = await _lookup.LookupCharacterAliasNameAsync(caId).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(n)) return n;
        }
        if (!string.IsNullOrWhiteSpace(e.RawCharacterText)) return e.RawCharacterText!;
        return "(キャラ未指定)";
    }

    /// <summary>
    /// 人物名義 ＋ 所属屋号をプレーンテキストとして返す（融合表示の同名判定にだけ使う）。
    /// 表示用 HTML を作りたいときは <see cref="ResolvePersonWithAffiliationHtmlAsync"/> を使うこと。
    /// </summary>
    private async Task<string> ResolvePersonWithAffiliationLabelAsync(CreditBlockEntry e, CancellationToken ct)
    {
        string name = e.PersonAliasId.HasValue
            ? (await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)"
            : "(名義未指定)";
        if (e.AffiliationCompanyAliasId is int afid)
        {
            string? af = await _lookup.LookupCompanyAliasNameAsync(afid).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(af)) name += $" ({af})";
        }
        else if (!string.IsNullOrWhiteSpace(e.AffiliationText))
        {
            name += $" ({e.AffiliationText})";
        }
        return name;
    }

    /// <summary>
    /// 人物名義 ＋ 所属屋号を「リンク済み HTML 断片」として返す。
    /// 名義は <see cref="StaffNameLinkResolver"/> で、所属屋号は
    /// <see cref="BuildCompanyAliasHtmlAsync"/> でリンク化する。所属がプレーンテキスト
    /// （<see cref="CreditBlockEntry.AffiliationText"/>）のときはリンク化せず HTML エスケープのみ。
    /// </summary>
    private async Task<string> ResolvePersonWithAffiliationHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        // 表示テキスト：LookupCache.LookupPersonAliasNameAsync は pa.Name を返す。
        // StaffNameLinkResolver.ResolveAsHtml が「person_alias_id が複数 person を持つ場合の添字付き
        // 複数リンク化」を内部で吸収する。
        string displayText = e.PersonAliasId.HasValue
            ? ((await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)")
            : "(名義未指定)";
        string nameHtml = _staffLinkResolver.ResolveAsHtml(e.PersonAliasId, displayText);

        // 所属付加：括弧付きで表示。所属が屋号 ID のときは屋号 → 企業詳細リンクに、
        // テキストのときはエスケープのみ。
        if (e.AffiliationCompanyAliasId is int afid)
        {
            string? af = await _lookup.LookupCompanyAliasNameAsync(afid).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(af))
            {
                string afHtml = await BuildCompanyAliasHtmlAsync(afid, af).ConfigureAwait(false);
                nameHtml += $" ({afHtml})";
            }
        }
        else if (!string.IsNullOrWhiteSpace(e.AffiliationText))
        {
            nameHtml += $" ({Esc(e.AffiliationText!)})";
        }
        return nameHtml;
    }

    /// <summary>
    /// 1 エントリ分の表示を「リンク済み HTML 断片」として返す。
    /// PERSON / CHARACTER_VOICE / COMPANY / LOGO / TEXT の 5 種に対応。
    /// </summary>
    private async Task<string> ResolveEntryHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                return await ResolvePersonWithAffiliationHtmlAsync(e, ct).ConfigureAwait(false);

            case "CHARACTER_VOICE":
                {
                    // VC テーブル外の文脈で CHARACTER_VOICE エントリが現れた場合のフォールバック表示。
                    // 通常は VC 専用テーブル内でキャラ／声優別カラムに分けて出す。
                    string charName = e.CharacterAliasId.HasValue
                        ? ((await _lookup.LookupCharacterAliasNameAsync(e.CharacterAliasId.Value).ConfigureAwait(false)) ?? "(キャラ不明)")
                        : (e.RawCharacterText ?? "(キャラ未指定)");
                    string voiceText = e.PersonAliasId.HasValue
                        ? ((await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(声優不明)")
                        : "(声優未指定)";
                    string voiceHtml = _staffLinkResolver.ResolveAsHtml(e.PersonAliasId, voiceText);
                    return $"{Esc(charName)} … {voiceHtml}";
                }

            case "COMPANY":
                {
                    if (!e.CompanyAliasId.HasValue) return Esc("(企業屋号未指定)");
                    string? name = await _lookup.LookupCompanyAliasNameAsync(e.CompanyAliasId.Value).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(name)) return Esc("(企業屋号不明)");
                    return await BuildCompanyAliasHtmlAsync(e.CompanyAliasId.Value, name).ConfigureAwait(false);
                }

            case "LOGO":
                return await BuildLogoHtmlAsync(e.LogoId).ConfigureAwait(false);

            case "TEXT":
                return Esc(e.RawText ?? "");

            default:
                return Esc($"({e.EntryKind})");
        }
    }
}