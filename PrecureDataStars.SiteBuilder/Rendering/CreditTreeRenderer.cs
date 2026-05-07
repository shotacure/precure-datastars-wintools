using System.Net;
using System.Text;
using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.TemplateRendering;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// クレジット 1 件分の階層を HTML 化するレンダラ（v1.3.0 プレビュー完全準拠版）。
/// <para>
/// Catalog 側 <c>PrecureDataStars.Catalog.Forms.Preview.CreditPreviewRenderer</c> の
/// DB ベース描画 (<c>RenderOneCreditFromDbAsync</c>) と同一のロジックで HTML を生成する。
/// 取り込んでいる仕様：
/// </para>
/// <list type="bullet">
///   <item><description>テンプレ DSL の役職テンプレ展開（<see cref="RoleTemplateRenderer"/>）。
///     <c>role_templates</c> から (role_code, series_id) → (role_code, NULL) フォールバックで
///     書式テンプレを引き、<c>{#BLOCKS}…{/BLOCKS}</c> や <c>{#THEME_SONGS:opts}…{/THEME_SONGS}</c>
///     を含む DSL を評価する（連載／主題歌／原作 等の表示はすべてテンプレ任せ）。</description></item>
///   <item><description>テンプレ未定義時のフォールバック描画（<c>fallback-table</c> = 役職名 | エントリ右並び、
///     col_count カラム横並び、<c>leading_company</c> はブロック先頭行で太字なし、後続行は字下げ）。</description></item>
///   <item><description>VOICE_CAST 役職用 3 カラムフォールバック（<c>fallback-vc-table</c> = 役職名 | キャラ | 声優、
///     直前行と同キャラ名なら dim 空セル、<c>leading_company</c> 字下げ対応）。</description></item>
///   <item><description>CASTING_COOPERATION 役職を VOICE_CAST テーブル末尾に「<strong>協力</strong>　屋号 屋号 …」として追記。
///     カード内の最後の VOICE_CAST 役職にだけ追記する。</description></item>
///   <item><description>絵コンテ・演出融合表示（<c>series.hide_storyboard_role = 1</c> + 同 Group 内に
///     STORYBOARD と EPISODE_DIRECTOR が 1 件ずつ + 各エントリ 1 件）→
///     同名なら役職名「（絵コンテ・）演出」の 1 行、異名なら役職名「演出」+ 2 行（絵コンテ→演出）。</description></item>
///   <item><description>VOICE_CAST 役職名のカード/Tier/Group 跨ぎ抑止
///     （直前ロールが同一 role_code の VOICE_CAST なら役職名カラムを空表示）。</description></item>
///   <item><description><c>is_broadcast_only=1</c> エントリは描画対象外。</description></item>
///   <item><description>テンプレ展開結果が <c>{ROLE_NAME}</c> を含まない場合、
///     <c>fallback-table</c> と同じ「役職名 + 内容」の 2 カラムテーブルで自動ラップ。</description></item>
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
        LookupCache lookup)
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
    }

    private static string Esc(string s) => WebUtility.HtmlEncode(s ?? "");

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
                            await RenderStoryboardDirectorMergedAsync(sbEntries, dirEntries, html, ct).ConfigureAwait(false);
                            mergedCardRoleIds.Add(sbRole.CardRoleId);
                            mergedCardRoleIds.Add(dirRole.CardRoleId);
                            prevVoiceCastRoleCode = null;
                        }
                    }

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

                        var blocks = (await _blocksRepo.GetByCardRoleAsync(cr.CardRoleId, ct).ConfigureAwait(false))
                            .OrderBy(b => b.BlockSeq).ToList();
                        var snapshots = new List<BlockSnapshot>();
                        foreach (var b in blocks)
                        {
                            var entries = (await _entriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                .Where(e => !e.IsBroadcastOnly)
                                .OrderBy(e => e.EntrySeq).ToList();
                            snapshots.Add(new BlockSnapshot(b, entries));
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
                            suppressVoiceCastRoleName, appendThisRole, html, ct).ConfigureAwait(false);

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
        StringBuilder html,
        CancellationToken ct)
    {
        string roleName = "";
        if (!string.IsNullOrEmpty(roleCode))
        {
            roleName = roleMap.TryGetValue(roleCode!, out var r) ? (r.NameJa ?? roleCode!) : roleCode!;
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
                var ctx = new TemplateContext(roleCode ?? "", roleName, blocks, scopeKind, episodeId, creditKind);
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
                    html.Append("<table class=\"fallback-table\"><tr>");
                    html.Append($"<td class=\"role-name\">{Esc(roleName)}</td>");
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
            await RenderVoiceCastFallbackAsync(roleName, blocks, suppressVoiceCastRoleName,
                appendedCooperationEntries, html, ct).ConfigureAwait(false);
        }
        else
        {
            await RenderRoleFallbackAsync(roleName, blocks, html, ct).ConfigureAwait(false);
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
            string sbText = await ResolvePersonWithAffiliationAsync(sb, ct).ConfigureAwait(false);
            string drText = await ResolvePersonWithAffiliationAsync(dr, ct).ConfigureAwait(false);
            sameName = string.Equals(sbText, drText, StringComparison.Ordinal);
        }

        string directorLabel = await ResolvePersonWithAffiliationAsync(dr, ct).ConfigureAwait(false);

        html.Append("<div class=\"role\">");
        html.Append("<table class=\"fallback-table\"><tr>");
        if (sameName)
        {
            html.Append($"<td class=\"role-name\">{Esc("(絵コンテ・)演出")}</td>");
            html.Append("<td class=\"entry-cell\">");
            html.Append(Esc(directorLabel));
            html.Append("</td>");
        }
        else
        {
            string storyboardLabel = await ResolvePersonWithAffiliationAsync(sb, ct).ConfigureAwait(false);
            html.Append($"<td class=\"role-name\">{Esc("演出")}</td>");
            html.Append("<td class=\"entry-cell\">");
            html.Append($"{Esc(storyboardLabel)} {Esc("(絵コンテ)")}<br>");
            html.Append($"{Esc(directorLabel)} {Esc("(演出)")}");
            html.Append("</td>");
        }
        html.Append("</tr></table>");
        html.Append("</div>");
    }

    /// <summary>
    /// 通常フォールバック描画：役職名（左）+ ブロック内エントリ（右、col_count カラム横並び）。
    /// leading_company はブロック先頭行で太字なし、後続エントリ行は字下げ（全角SP 1 個分）。
    /// </summary>
    private async Task RenderRoleFallbackAsync(
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        StringBuilder html,
        CancellationToken ct)
    {
        if (blocks.Count == 0 || blocks.All(b => b.Entries.Count == 0))
        {
            html.Append($"<table class=\"fallback-table\"><tr><td class=\"role-name\">{Esc(roleName)}</td><td><span class=\"empty-credit\">（エントリ未登録）</span></td></tr></table>");
            return;
        }

        html.Append("<table class=\"fallback-table\">");
        bool firstRow = true;
        bool isFirstBlock = true;
        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;
            int cols = Math.Max(1, (int)bs.Block.ColCount);

            string? leadingCompanyName = null;
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            bool isFirstRowOfThisBlock = true;

            if (hasLeading)
            {
                bool addBreakClass = !isFirstBlock;
                html.Append(addBreakClass ? "<tr class=\"block-break\">" : "<tr>");
                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{Esc(roleName)}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }
                html.Append($"<td class=\"entry-cell\" colspan=\"{cols}\">{Esc(leadingCompanyName!)}</td>");
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
                    html.Append($"<td class=\"role-name\">{Esc(roleName)}</td>");
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
                        string label = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
                        if (hasLeading) html.Append("　"); // 全角SP 1 個分の字下げ
                        html.Append(Esc(label));
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
    /// CASTING_COOPERATION エントリは末尾に「協力」行として追記。
    /// </summary>
    private async Task RenderVoiceCastFallbackAsync(
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        bool suppressRoleName,
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        StringBuilder html,
        CancellationToken ct)
    {
        string roleNameForFirstRow = suppressRoleName ? "" : roleName;

        if (blocks.Count == 0 || blocks.All(b => b.Entries.Count == 0))
        {
            html.Append($"<table class=\"fallback-vc-table\"><tr><td class=\"role-name\">{Esc(roleNameForFirstRow)}</td><td class=\"character-cell\"></td><td class=\"actor-cell\"><span class=\"empty-credit\">（エントリ未登録）</span></td></tr></table>");
            return;
        }

        html.Append("<table class=\"fallback-vc-table\">");
        bool firstRow = true;
        bool isFirstBlock = true;
        string? prevCharLabel = null;

        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;

            string? leadingCompanyName = null;
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            bool isFirstRowOfThisBlock = true;

            if (hasLeading)
            {
                bool addBreakClass = !isFirstBlock;
                html.Append(addBreakClass ? "<tr class=\"block-break\">" : "<tr>");
                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{Esc(roleNameForFirstRow)}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }
                html.Append($"<td class=\"character-cell\" colspan=\"2\">{Esc(leadingCompanyName!)}</td>");
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
                    html.Append($"<td class=\"role-name\">{Esc(roleNameForFirstRow)}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }

                if (e.EntryKind == "CHARACTER_VOICE")
                {
                    string charLabel = await ResolveCharacterLabelAsync(e, ct).ConfigureAwait(false);
                    string actorLabel = await ResolvePersonWithAffiliationAsync(e, ct).ConfigureAwait(false);

                    bool sameAsPrev = prevCharLabel is not null
                        && string.Equals(charLabel, prevCharLabel, StringComparison.Ordinal);
                    if (sameAsPrev)
                    {
                        html.Append("<td class=\"character-cell dim\"></td>");
                    }
                    else
                    {
                        string charPrefix = hasLeading ? "　" : "";
                        html.Append($"<td class=\"character-cell\">{charPrefix}{Esc(charLabel)}</td>");
                    }
                    html.Append($"<td class=\"actor-cell\">{Esc(actorLabel)}</td>");

                    prevCharLabel = charLabel;
                }
                else
                {
                    string label = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
                    html.Append("<td class=\"character-cell\"></td>");
                    string actorPrefix = hasLeading ? "　" : "";
                    html.Append($"<td class=\"actor-cell\">{actorPrefix}{Esc(label)}</td>");
                    prevCharLabel = null;
                }

                html.Append("</tr>");
            }
            isFirstBlock = false;
        }

        // 「協力」行追記
        if (appendedCooperationEntries is not null && appendedCooperationEntries.Count > 0)
        {
            var labels = new List<string>();
            foreach (var e in appendedCooperationEntries)
            {
                string lbl = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lbl)) labels.Add(lbl);
            }
            if (labels.Count > 0)
            {
                html.Append("<tr class=\"cooperation-row\">");
                html.Append("<td class=\"role-name\"></td>");
                html.Append("<td class=\"character-cell\" colspan=\"2\"><strong>協力</strong>　");
                html.Append(string.Join("　", labels.Select(Esc)));
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

    private async Task<string> ResolvePersonWithAffiliationAsync(CreditBlockEntry e, CancellationToken ct)
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

    private async Task<string> ResolveEntryLabelAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
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
            case "CHARACTER_VOICE":
                {
                    string charName = e.CharacterAliasId.HasValue
                        ? ((await _lookup.LookupCharacterAliasNameAsync(e.CharacterAliasId.Value).ConfigureAwait(false)) ?? "(キャラ不明)")
                        : (e.RawCharacterText ?? "(キャラ未指定)");
                    string voice = e.PersonAliasId.HasValue
                        ? ((await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(声優不明)")
                        : "(声優未指定)";
                    return $"{charName} … {voice}";
                }
            case "COMPANY":
                return e.CompanyAliasId.HasValue
                    ? ((await _lookup.LookupCompanyAliasNameAsync(e.CompanyAliasId.Value).ConfigureAwait(false)) ?? "(企業屋号不明)")
                    : "(企業屋号未指定)";
            case "LOGO":
                {
                    if (!e.LogoId.HasValue) return "(ロゴ未指定)";
                    var lg = await _lookup.GetLogoForRenderingAsync(e.LogoId.Value).ConfigureAwait(false);
                    if (lg is null) return "(ロゴ不明)";
                    string? aliasName = await _lookup.LookupCompanyAliasNameAsync(lg.CompanyAliasId).ConfigureAwait(false);
                    return aliasName ?? "(屋号不明)";
                }
            case "TEXT":
                return e.RawText ?? "";
            default:
                return $"({e.EntryKind})";
        }
    }
}
