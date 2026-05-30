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
/// Catalog 側 <c>PrecureDataStars.Catalog.Forms.Preview.CreditPreviewRenderer</c> の
/// DB ベース描画 (<c>RenderOneCreditFromDbAsync</c>) と同一のロジックで HTML を生成する。
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
/// クレジット内の各表示要素はそれぞれの詳細ページへリンク化される：
/// <list type="bullet">
///   <item><description>役職名 → <c>/stats/roles/{role_code}/</c>（VOICE_CAST 系は <c>/stats/voice-cast/</c>）</description></item>
///   <item><description>人物名義 → <c>/persons/{person_id}/</c>（共有名義は <see cref="StaffNameLinkResolver"/> 経由で添字付き複数リンク化）</description></item>
///   <item><description>企業屋号 → <c>/companies/{company_id}/</c>（屋号 → 親 company_id を <see cref="LookupCache.LookupCompanyIdFromAliasAsync"/> で解決）</description></item>
///   <item><description>ロゴ → 屋号名に置換したうえで <c>/companies/{company_id}/</c> へリンク（CI バージョンラベルは付けない）</description></item>
/// </list>
/// </summary>
internal sealed class CreditTreeRenderer
{
    private readonly BuildContext _ctx;
    /// <summary>RoleTemplateRenderer がテンプレ DSL 展開で SQL 評価フックを必要とするケース （特に <c>{#THEME_SONGS:opts}…{/THEME_SONGS}</c> 等の動的取得系）に備えて接続ファクトリを保持する。 クレジット階層 6 段の取得には使わない（その経路は <see cref="BuildContext.CreditTree"/> に事前展開済み）。</summary>
    private readonly IConnectionFactory _factory;
    private readonly LookupCache _lookup;

    /// <summary>人物名義 → 人物詳細ページ HTML リンクの解決器。 クレジット内のすべての人物表記をリンク化するために使う。 共有名義（1 名義 → 複数 person）は本リゾルバ側で「[1] [2]」付き複数リンクに展開される。</summary>
    private readonly StaffNameLinkResolver _staffLinkResolver;

    /// <summary>役職コード。</summary>
    private const string RoleCodeStoryboard = "STORYBOARD";
    private const string RoleCodeEpisodeDirector = "EPISODE_DIRECTOR";
    private const string RoleCodeCastingCooperation = "CASTING_COOPERATION";

    public CreditTreeRenderer(
        BuildContext ctx,
        IConnectionFactory factory,
        LookupCache lookup,
        StaffNameLinkResolver staffLinkResolver)
    {
        _ctx = ctx;
        _factory = factory;
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

        // VOICE_CAST フォーマット種の役職は声の出演一覧（/creators/voice-cast/）に集約しているため、
        // そちらに飛ばす。それ以外は /creators/roles/{role_code}/ の役職詳細ページへ。
        // どちらの URL も PathUtil に集約し、本レンダラ内に文字列リテラルでパスを持たない。
        bool isVoiceCast = roleMap.TryGetValue(roleCode!, out var r)
                           && string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal);
        string url = isVoiceCast ? PathUtil.CreatorsVoiceCastUrl() : PathUtil.RoleStatsUrl(roleCode!);
        return $"<a href=\"{url}\">{Esc(roleName)}</a>";
    }

    /// <summary>
    /// クレジット時の誤記（事故）を「正名義」の左側に「打ち消し線 + 半角SP」で前置する HTML を生成する。
    /// SEO 上は誤記を「削除済みコンテンツ」として扱わせるため <c>&lt;del&gt;</c> 要素でマークアップする
    /// （<c>&lt;s&gt;</c> 要素は HTML 仕様で「もう正しくない」用途で、訂正には <c>&lt;del&gt;</c> が推奨される）。
    /// title 属性で「クレジット時の誤記」のホバー注釈も出す。
    /// <paramref name="misprint"/> が null / 空文字の場合は <paramref name="baseHtml"/> をそのまま返す。
    /// </summary>
    private static string PrependMisprintHtml(string baseHtml, string? misprint)
    {
        if (string.IsNullOrEmpty(misprint)) return baseHtml;
        return $"<del title=\"クレジット時の誤記\">{Esc(misprint)}</del> {baseHtml}";
    }

    /// <summary>企業屋号（company_alias）の表示名を、親企業詳細ページへのリンク済み HTML に変換する。</summary>
    private async Task<string> BuildCompanyAliasHtmlAsync(int? aliasId, string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "";
        if (!aliasId.HasValue) return Esc(displayName);
        int? cid = await _lookup.LookupCompanyIdFromAliasAsync(aliasId.Value).ConfigureAwait(false);
        if (!cid.HasValue) return Esc(displayName);
        return $"<a href=\"{PathUtil.CompanyUrl(cid.Value)}\">{Esc(displayName)}</a>";
    }

    /// <summary>ロゴエントリの表示を「屋号名に置換 + 親企業詳細ページへのリンク」に変換する。 CI バージョンラベルは省く方針（屋号単位で集約した方が読み手にとって分かりやすいため）。 解決失敗時はプレースホルダ文字列を返す。</summary>
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
    /// プレビューと違い、見出し（&lt;h1&gt;）はテンプレ側で出すため本メソッドでは出さない。
    public async Task<string> RenderAsync(Credit credit, BuildLogger logger, CancellationToken ct = default)
    {
        var html = new StringBuilder();

        // クレジット階層は SiteDataLoader がビルド開始時に 1 度だけ 6 テーブル分の GetAllAsync で
        // 取得して BuildContext.CreditTree に詰めている。本ループでは credit_id 経由で
        // 該当 credit のカード群 (CreditCardSnapshot) を辞書から引くだけで、生成中の DB 往復は発生しない。
        if (!_ctx.CreditTree.CardsByCreditId.TryGetValue(credit.CreditId, out var cardSnapshotsAll)
            || cardSnapshotsAll.Count == 0)
        {
            html.Append("<p class=\"empty-credit\">（カード未登録）</p>");
            return html.ToString();
        }
        var cardSnapshots = cardSnapshotsAll.OrderBy(c => c.Card.CardSeq).ToList();

        // 役職マスタは BuildContext で事前展開済みのため直接参照する。
        var roleMap = _ctx.RoleByCode;
        int? resolveSeriesId = ResolveTemplateSeriesId(credit);

        bool hideStoryboardRole = GetHideStoryboardRole(resolveSeriesId);
        string? prevVoiceCastRoleCode = null;

        foreach (var cardSnap in cardSnapshots)
        {
            var card = cardSnap.Card;
            html.Append("<div class=\"card\">");
            var tierSnapshots = cardSnap.Tiers.OrderBy(t => t.Tier.TierNo).ToList();

            var cooperationContext = CollectCardCastingCooperationContext(cardSnap, roleMap);
            IReadOnlyList<CreditBlockEntry>? cooperationEntriesForCard = cooperationContext?.Entries;
            int? cooperationAppendTargetCardRoleId = cooperationContext?.LastVoiceCastCardRoleId;

            foreach (var tierSnap in tierSnapshots)
            {
                var tier = tierSnap.Tier;
                html.Append("<div class=\"tier\">");
                var groupSnapshots = tierSnap.Groups.OrderBy(g => g.Group.GroupNo).ToList();
                foreach (var groupSnap in groupSnapshots)
                {
                    var grp = groupSnap.Group;
                    html.Append("<div class=\"group\">");
                    var roleSnapshots = groupSnap.Roles.OrderBy(r => r.Role.OrderInGroup).ToList();
                    var cardRoles = roleSnapshots.Select(rs => rs.Role).ToList();
                    // CardRoleId → Snapshot の局所辞書。絵コンテ・演出マージ判定で出力された
                    // CreditCardRole から配下の Block / Entry を引き戻すために使う。
                    var roleSnapshotById = roleSnapshots.ToDictionary(rs => rs.Role.CardRoleId);

                    // 絵コンテ・演出融合判定
                    HashSet<int> mergedCardRoleIds = new();
                    if (hideStoryboardRole &&
                        TryDetectMergeableStoryboardDirector(cardRoles, r => r.RoleCode,
                            out var sbRole, out var dirRole))
                    {
                        var sbEntries = CollectEntriesUnderCardRole(roleSnapshotById[sbRole!.CardRoleId]);
                        var dirEntries = CollectEntriesUnderCardRole(roleSnapshotById[dirRole!.CardRoleId]);
                        if (sbEntries.Count == 1 && dirEntries.Count == 1)
                        {
                            await RenderStoryboardDirectorMergedAsync(sbEntries, dirEntries, roleMap, html, ct).ConfigureAwait(false);
                            mergedCardRoleIds.Add(sbRole.CardRoleId);
                            mergedCardRoleIds.Add(dirRole.CardRoleId);
                            prevVoiceCastRoleCode = null;
                        }
                    }

                    // 同 Group 内の sibling 役職を role_code → BlockSnapshot[] 辞書化。
                    // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文は、同 Group 内の別役職の Block 群を
                    // 引いて内側プレースホルダを評価するため、Group 配下の全役職について事前に Block と
                    // Entry を CreditTreeIndex のスナップショットから引き出して辞書を作る。各役職の処理時に
                    // SiblingRoleResolver として TemplateContext に渡すことで、レンダラ側から sibling 役職の
                    // 中身が透過的に見える。
                    // 同 role_code が複数役職に重複している場合は最初の 1 つを採用（同 Group 内重複は通常起き得ない）。
                    var siblingBlocksByRoleCode = new Dictionary<string, IReadOnlyList<BlockSnapshot>>(StringComparer.Ordinal);
                    foreach (var siblingRoleSnap in roleSnapshots)
                    {
                        var siblingRole = siblingRoleSnap.Role;
                        if (string.IsNullOrEmpty(siblingRole.RoleCode)) continue;
                        if (siblingBlocksByRoleCode.ContainsKey(siblingRole.RoleCode!)) continue;

                        siblingBlocksByRoleCode[siblingRole.RoleCode!] = BuildBlockSnapshots(siblingRoleSnap);
                    }
                    // クロージャで辞書を捕捉。null 時は空文字に展開される（レンダラの仕様）。
                    Func<string, IReadOnlyList<BlockSnapshot>?> siblingResolver = code =>
                        siblingBlocksByRoleCode.TryGetValue(code, out var s) ? s : null;

                    // 同 Group 内の役職テンプレ群を事前スキャンして、{ROLE:CODE.PLACEHOLDER} で
                    // 「消費」される sibling role_code 集合を作る。メインループで消費先 role_code を
                    // 持つロールは描画スキップする（典型例：SERIALIZED_IN テンプレが MANGA を
                    // {ROLE:MANGA.PERSONS} で参照する場合、MANGA 自体は空の見出し行として
                    // 重複出力されてしまうのを防ぐ）。
                    // 役職テンプレが見つからない、または ROLE 参照を含まない場合は何も追加しない。
                    var consumedRoleCodes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var siblingRole in cardRoles)
                    {
                        if (string.IsNullOrEmpty(siblingRole.RoleCode)) continue;
                        var tpl = _ctx.RoleTemplateResolver.Resolve(siblingRole.RoleCode!, resolveSeriesId);
                        string? template = tpl?.FormatTemplate;
                        if (string.IsNullOrWhiteSpace(template)) continue;
                        // 軽量検出：テンプレ文字列に {ROLE:<CODE>. が含まれる先で <CODE> を抜き出す。
                        int pos = 0;
                        while (true)
                        {
                            int idx = template!.IndexOf("{ROLE:", pos, StringComparison.Ordinal);
                            if (idx < 0) break;
                            int dotIdx = template.IndexOf('.', idx + 6);
                            int endIdx = template.IndexOf('}', idx + 6);
                            if (dotIdx < 0 || (endIdx >= 0 && dotIdx > endIdx))
                            {
                                pos = idx + 6;
                                continue;
                            }
                            string consumedCode = template.Substring(idx + 6, dotIdx - (idx + 6)).Trim();
                            if (!string.IsNullOrEmpty(consumedCode))
                                consumedRoleCodes.Add(consumedCode);
                            pos = dotIdx + 1;
                        }
                    }

                    foreach (var crSnap in roleSnapshots)
                    {
                        var cr = crSnap.Role;
                        if (mergedCardRoleIds.Contains(cr.CardRoleId)) continue;

                        // CASTING_COOPERATION 役職本体は描画スキップ（VOICE_CAST 末尾追記される）
                        if (cooperationEntriesForCard is not null
                            && cooperationEntriesForCard.Count > 0
                            && string.Equals(cr.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // 他ロールのテンプレに {ROLE:CODE.…} で消費されている role はメインループから外す
                        // （二重出力防止）。
                        if (!string.IsNullOrEmpty(cr.RoleCode)
                            && consumedRoleCodes.Contains(cr.RoleCode!))
                        {
                            continue;
                        }

                        // sibling 辞書から自身の Block を引いて使い回し（重複ロード回避）。
                        // 辞書に無い役職（RoleCode が null など）は CreditTreeIndex のスナップショットから直接組み立てる。
                        IReadOnlyList<BlockSnapshot> snapshots;
                        if (!string.IsNullOrEmpty(cr.RoleCode)
                            && siblingBlocksByRoleCode.TryGetValue(cr.RoleCode!, out var cached))
                        {
                            snapshots = cached;
                        }
                        else
                        {
                            snapshots = BuildBlockSnapshots(crSnap);
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
                            suppressVoiceCastRoleName, appendThisRole, siblingResolver,
                            affiliationLayout: cr.AffiliationLayout,
                            html, ct).ConfigureAwait(false);

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

    /// <summary>テンプレ解決用シリーズ ID を決める：SERIES スコープなら credit.SeriesId、 EPISODE スコープなら <see cref="BuildContext.EpisodeById"/> から所属シリーズ ID を逆引き。</summary>
    private int? ResolveTemplateSeriesId(Credit credit)
    {
        if (credit.ScopeKind == "SERIES") return credit.SeriesId;
        if (credit.EpisodeId is not int eid || eid <= 0) return null;
        return _ctx.EpisodeById.TryGetValue(eid, out var ep) ? ep.SeriesId : (int?)null;
    }

    /// <summary><c>series.hide_storyboard_role</c> フラグを <see cref="BuildContext.SeriesById"/> から引く。 SiteDataLoader は <c>is_deleted = 0</c> のシリーズのみロードしているため、削除済みシリーズは辞書に存在せず false 扱いになる。</summary>
    private bool GetHideStoryboardRole(int? seriesId)
    {
        if (!seriesId.HasValue) return false;
        return _ctx.SeriesById.TryGetValue(seriesId.Value, out var s) && s.HideStoryboardRole;
    }

    /// <summary>カード単位で「VOICE_CAST 役職」と「CASTING_COOPERATION 役職」の両方が居るか調べ、 両方ある場合は CASTING_COOPERATION 配下のエントリを集約 + カード内最後の VOICE_CAST 役職の CardRoleId を返す（プレビューと同一仕様）。 探索は <see cref="CreditTreeIndex"/> の事前展開済みスナップショットを辿るだけで完結する。</summary>
    private static (List<CreditBlockEntry> Entries, int LastVoiceCastCardRoleId)? CollectCardCastingCooperationContext(
        CreditCardSnapshot cardSnap,
        IReadOnlyDictionary<string, Role> roleMap)
    {
        int? lastVcCardRoleId = null;
        var cooperationRoleSnaps = new List<CreditCardRoleSnapshot>();
        foreach (var tierSnap in cardSnap.Tiers.OrderBy(t => t.Tier.TierNo))
        {
            foreach (var groupSnap in tierSnap.Groups.OrderBy(g => g.Group.GroupNo))
            {
                foreach (var roleSnap in groupSnap.Roles.OrderBy(r => r.Role.OrderInGroup))
                {
                    var cr = roleSnap.Role;
                    if (IsVoiceCastRole(cr.RoleCode, roleMap)) lastVcCardRoleId = cr.CardRoleId;
                    if (string.Equals(cr.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        cooperationRoleSnaps.Add(roleSnap);
                }
            }
        }

        if (lastVcCardRoleId is null || cooperationRoleSnaps.Count == 0) return null;

        var aggregated = new List<CreditBlockEntry>();
        foreach (var rs in cooperationRoleSnaps)
        {
            aggregated.AddRange(CollectEntriesUnderCardRole(rs));
        }
        return (aggregated, lastVcCardRoleId.Value);
    }

    /// <summary>指定 CardRole 配下の全エントリ（<c>is_broadcast_only = 0</c> のみ）を block_seq → entry_seq 順で平坦化する。</summary>
    private static List<CreditBlockEntry> CollectEntriesUnderCardRole(CreditCardRoleSnapshot roleSnap)
    {
        var result = new List<CreditBlockEntry>();
        foreach (var blockSnap in roleSnap.Blocks.OrderBy(b => b.Block.BlockSeq))
        {
            var entries = blockSnap.Entries
                .Where(e => !e.IsBroadcastOnly)
                .OrderBy(e => e.EntrySeq);
            result.AddRange(entries);
        }
        return result;
    }

    /// <summary>CardRole スナップショット配下の Block 群を、レンダラ内部流通型 <see cref="BlockSnapshot"/> のリストに変換する。 旧 per-key ロードと同等の結果（<c>is_broadcast_only = 0</c> のエントリのみ、block_seq → entry_seq 順）を返す。</summary>
    private static IReadOnlyList<BlockSnapshot> BuildBlockSnapshots(CreditCardRoleSnapshot roleSnap)
    {
        var snList = new List<BlockSnapshot>();
        foreach (var blockSnap in roleSnap.Blocks.OrderBy(b => b.Block.BlockSeq))
        {
            var entries = blockSnap.Entries
                .Where(e => !e.IsBroadcastOnly)
                .OrderBy(e => e.EntrySeq).ToList();
            snList.Add(new BlockSnapshot(blockSnap.Block, entries));
        }
        return snList;
    }

    private static bool IsVoiceCastRole(string? roleCode, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (string.IsNullOrEmpty(roleCode)) return false;
        if (!roleMap.TryGetValue(roleCode, out var r)) return false;
        return string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal);
    }

    /// <summary>役職 1 つの描画。テンプレ展開 → 失敗時または未定義時はフォールバック表へ。
    /// <paramref name="affiliationLayout"/> は人物所属の表記レイアウト指定（"SUFFIX" 既定 / "PREFIX" 映画製作・配給用）。</summary>
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
        // 同 Group 内の sibling 役職を role_code で引くコールバック。
        // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文に使う。null のとき ROLE 参照は空文字に展開される。
        Func<string, IReadOnlyList<BlockSnapshot>?>? siblingRoleResolver,
        string affiliationLayout,
        StringBuilder html,
        CancellationToken ct)
    {
        string roleName = "";
        if (!string.IsNullOrEmpty(roleCode))
        {
            if (roleMap.TryGetValue(roleCode!, out var r))
            {
                roleName = r.NameJa ?? roleCode!;
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
        string? contentHeaderOverride = null;
        if (!string.IsNullOrEmpty(roleCode))
        {
            var tpl = _ctx.RoleTemplateResolver.Resolve(roleCode!, resolveSeriesId);
            template = tpl?.FormatTemplate;
            contentHeaderOverride = string.IsNullOrEmpty(tpl?.ContentHeaderOverride) ? null : tpl!.ContentHeaderOverride;
        }

        html.Append("<div class=\"role\">");

        // コンテンツ領域ヘッダ上書き：シリーズ別「役職名の表示テキストだけ変えて、本体は通常描画」
        // 用途。非 NULL のとき役職ラッパ直下に <strong>+ 役職詳細リンクを出し、後段のフォールバック
        // 描画では左カラム役職名を空表示にして二重ヘッダを防ぐ。テンプレ本体が同時に設定されている
        // 場合はテンプレ展開結果をヘッダ直下に role-rendered で出力する（fallback-table の自動ラップを
        // 通さない＝コンテンツヘッダが左ラベルの代替として既に出てるため）。
        if (contentHeaderOverride is not null)
        {
            html.Append("<div class=\"role-content-header\"><strong>");
            html.Append(BuildRoleNameHtml(roleCode, contentHeaderOverride, roleMap));
            html.Append("</strong></div>");
        }

        if (!string.IsNullOrWhiteSpace(template))
        {
            try
            {
                var ast = TemplateParser.Parse(template!);
                // sibling-role 解決のコールバックを渡して {ROLE:CODE.PLACEHOLDER} 構文に対応。
                // SERIES スコープのクレジット（映画系列）では scopeSeriesId に credit.SeriesId 相当を渡し、
                // {THEME_SONGS} ハンドラが series_theme_songs を引き当てるようにする。EPISODE スコープでは null。
                int? scopeSeriesIdForCtx = scopeKind == "SERIES" ? resolveSeriesId : null;
                // SERIES スコープの場合、テンプレで {SERIES_TITLE} を使えるよう series.title を解決して詰める。
                string? scopeSeriesTitleForCtx = (scopeSeriesIdForCtx is int ssid
                    && _ctx.SeriesById.TryGetValue(ssid, out var seriesForCtx))
                    ? seriesForCtx.Title
                    : null;
                var ctx = new TemplateContext(roleCode ?? "", roleName, blocks, scopeKind, episodeId, scopeSeriesIdForCtx, creditKind,
                    siblingRoleResolver: siblingRoleResolver,
                    visitedRoleCodes: null,
                    scopeSeriesTitle: scopeSeriesTitleForCtx);
                string rendered = await RoleTemplateRenderer.RenderAsync(ast, ctx, _factory, _lookup, ct).ConfigureAwait(false);

                string normalized = rendered.Replace("\r\n", "\n").Replace("\r", "\n");
                string brTransformed = normalized.Replace("\n", "<br>");

                // 「テンプレが自前で役職見出しを持っている」と判定したら左ラベル抑止（role-rendered 単段）。
                // 判定：(a) {ROLE_NAME} を含む、または (b) 自分の役職コードを参照する {ROLE_LINK:code=<roleCode>} を含む。
                // (b) はテンプレ冒頭に <strong>{ROLE_LINK:code=<this>,label=...}</strong> のような自己見出しを置く
                // ケース（シリーズ別カスタムテンプレ等）に対応するためのもの。他役職コードの {ROLE_LINK} を
                // 単にフィールドラベルとして使うだけのテンプレ（既定の主題歌テンプレ等）は誤抑止しない。
                // 「テンプレが自前で役職見出しを持っている」と判定したら左ラベル抑止（role-rendered 単段）。
                // 判定：(a) {ROLE_NAME} を含む、または (b) 自分の役職コードを参照する {ROLE_LINK:code=<roleCode>} を含む、
                // または (c) ContentHeaderOverride が同行に設定されていてヘッダが既に役職ラッパ直下に出力済み。
                // (b) はテンプレ冒頭に <strong>{ROLE_LINK:code=<this>,label=...}</strong> のような自己見出しを置く
                // ケース（シリーズ別カスタムテンプレ等）に対応するためのもの。他役職コードの {ROLE_LINK} を
                // 単にフィールドラベルとして使うだけのテンプレ（既定の主題歌テンプレ等）は誤抑止しない。
                bool templateHasOwnRoleHeader =
                    contentHeaderOverride is not null
                    || template!.Contains("{ROLE_NAME}", StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(roleCode)
                        && template!.Contains("{ROLE_LINK:code=" + roleCode, StringComparison.Ordinal));
                if (templateHasOwnRoleHeader)
                {
                    // テンプレ / ContentHeaderOverride が自前で役職見出しを持つケースでも、
                    // 展開結果のコンテンツ位置を fallback-table の右カラム（entry-cell）と揃えるため、
                    // 左カラム role-name を空文字で出して 2 カラム表構造の中に流し込む。
                    // role-name の min-width / credit-align.js による --credit-role-name-w 共有が効いて
                    // 同ページ内の他役職と x 位置が揃う（旧版は role-rendered 単独 div で左端 0 px から
                    // 始まり、声の出演や他フォールバック行の右カラムと水平にズレていた）。
                    html.Append("<table class=\"fallback-table\"><tr>");
                    html.Append("<td class=\"role-name\"></td>");
                    html.Append("<td class=\"entry-cell\">");
                    html.Append(brTransformed);
                    html.Append("</td></tr></table>");
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
                // ContentHeaderOverride 設定済みの場合は左カラム役職名を抑止（コンテンツヘッダで既出のため）。
                string fallbackRoleNameOnError = contentHeaderOverride is not null ? "" : roleName;
                await RenderRoleFallbackDispatchAsync(roleCode, fallbackRoleNameOnError, blocks, roleMap,
                    suppressVoiceCastRoleName, appendedCooperationEntries, affiliationLayout, html, ct).ConfigureAwait(false);
                html.Append("</div>");
            }
        }
        else
        {
            // テンプレ本体無し → フォールバック描画。ContentHeaderOverride が設定されているなら
            // 左カラム役職名を抑止する（コンテンツヘッダで既に役職名を出しているため二重ヘッダを防ぐ）。
            string fallbackRoleName = contentHeaderOverride is not null ? "" : roleName;
            await RenderRoleFallbackDispatchAsync(roleCode, fallbackRoleName, blocks, roleMap,
                suppressVoiceCastRoleName, appendedCooperationEntries, affiliationLayout, html, ct).ConfigureAwait(false);
        }

        html.Append("</div>"); // .role
    }

    private async Task RenderRoleFallbackDispatchAsync(
        string? roleCode, string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        IReadOnlyDictionary<string, Role> roleMap,
        bool suppressVoiceCastRoleName,
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        string affiliationLayout,
        StringBuilder html, CancellationToken ct)
    {
        // PREFIX レイアウトは映画の「製作:」「配給:」のような 2 カラム表示。
        // VOICE_CAST 経路や CASTING_COOPERATION 追記の概念は適用しないため、専用レンダラに直行する。
        if (string.Equals(affiliationLayout, "PREFIX", StringComparison.Ordinal))
        {
            await RenderRoleFallbackPrefixAsync(roleCode, roleName, blocks, roleMap, html, ct).ConfigureAwait(false);
            return;
        }

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

    /// <summary>PREFIX レイアウト専用フォールバック描画：役職名（左）+ 「屋号 + 人名」の 2 カラムグリッド（右）。
    /// 直前行と屋号 alias_id が同じ場合は左セル（屋号）を空にして繰り返しを圧縮表示する。
    /// 屋号は <c>.affil-prefix</c> クラスで 80% 縮小フォント + muted、人名は通常スタイル。</summary>
    private async Task RenderRoleFallbackPrefixAsync(
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
        int? prevAffilAliasId = null;
        string? prevAffilText = null;
        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;

            bool isFirstRowOfThisBlock = true;
            // ブロック区切り直後は屋号繰り返し圧縮の状態を一旦リセットする
            // （ブロック単位で表現を分けるユーザー意図を尊重するため）。
            prevAffilAliasId = null;
            prevAffilText = null;

            foreach (var e in bs.Entries)
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

                // 屋号セル。直前行と同じなら空欄に圧縮。
                string affilHtml = "";
                int? curAffilAliasId = e.AffiliationCompanyAliasId;
                string? curAffilText = e.AffiliationText;
                bool sameAsPrev =
                    (curAffilAliasId.HasValue && prevAffilAliasId == curAffilAliasId)
                    || (!curAffilAliasId.HasValue && prevAffilAliasId is null
                        && !string.IsNullOrEmpty(curAffilText)
                        && string.Equals(prevAffilText, curAffilText, StringComparison.Ordinal));
                if (!sameAsPrev)
                {
                    if (curAffilAliasId is int affId)
                    {
                        string? affName = await _lookup.LookupCompanyAliasNameAsync(affId).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(affName))
                        {
                            affilHtml = await BuildCompanyAliasHtmlAsync(affId, affName).ConfigureAwait(false);
                        }
                    }
                    else if (!string.IsNullOrEmpty(curAffilText))
                    {
                        affilHtml = Esc(curAffilText);
                    }
                }
                html.Append($"<td class=\"affil-prefix\">{affilHtml}</td>");

                // 人物名セル。所属サフィックスは抑止して名前のみを出すため、ResolveEntryHtmlAsync に
                // suppressAffiliation 相当の経路を入れたいところだが、現状の API では分岐できない。
                // 暫定：affiliation_company_alias_id / affiliation_text を一時退避してから ResolveEntryHtmlAsync を
                // 呼び、戻ってきたら復元する（メモリ上の同一インスタンスを書き換えるので副作用なし）。
                int? savedAffilAlias = e.AffiliationCompanyAliasId;
                string? savedAffilText = e.AffiliationText;
                try
                {
                    e.AffiliationCompanyAliasId = null;
                    e.AffiliationText = null;
                    string entryHtml = await ResolveEntryHtmlAsync(e, ct).ConfigureAwait(false);
                    html.Append($"<td class=\"entry-cell\">{entryHtml}</td>");
                }
                finally
                {
                    e.AffiliationCompanyAliasId = savedAffilAlias;
                    e.AffiliationText = savedAffilText;
                }
                html.Append("</tr>");

                prevAffilAliasId = curAffilAliasId;
                prevAffilText = curAffilText;
            }
            isFirstBlock = false;
        }
        html.Append("</table>");
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
        // 絵コンテ部分も独立した役職統計ページを持つので、別リンクに分割する。
        string storyboardRoleName = roleMap.TryGetValue(RoleCodeStoryboard, out var sbR)
            ? (sbR.NameJa ?? "絵コンテ")
            : "絵コンテ";

        // エントリ表示は人物名義をリンク化、所属屋号もリンク化した HTML 断片を作る。
        string directorHtml = await ResolvePersonWithAffiliationHtmlAsync(dr, ct).ConfigureAwait(false);

        html.Append("<div class=\"role\">");
        html.Append("<table class=\"fallback-table\"><tr>");
        if (sameName)
        {
            // 「(絵コンテ・)演出」というラベルを分割表示：
            string storyboardLinkHtml = BuildRoleNameHtml(RoleCodeStoryboard, storyboardRoleName, roleMap);
            string directorLinkHtml = BuildRoleNameHtml(RoleCodeEpisodeDirector, directorRoleName, roleMap);
            html.Append($"<td class=\"role-name\">({storyboardLinkHtml}・){directorLinkHtml}</td>");
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

    /// <summary>通常フォールバック描画：役職名（左）+ ブロック内エントリ（右、col_count カラム横並び）。 leading_company はブロック先頭行で太字なし、後続エントリ行は字下げ（全角SP 1 個分）。 役職名・名義・屋号・ロゴをそれぞれ詳細ページへリンク化する。</summary>
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
    /// 協力行のレイアウト：「協力」を役職名カラムに右寄せで置き、屋号一覧は声優名カラムに置く
    /// （3 カラム構成と整合性を取る）。役職名・名義・屋号はリンク化。
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

            // 各エントリの表示要素を先に解決し、隣接 CHARACTER_VOICE 行で actor 表示 HTML が一致する
            // 連続区間（n 役連続）を rowspan で結合する事前計算を行う。actor の同一性判定は
            // 「最終的に表示される actor-cell の HTML 文字列」で行うため、所属（affiliation）や
            // スラッシュ配偶名義の違いがあれば自動的に別グループとして扱われる。
            int entryCount = bs.Entries.Count;
            var resolved = new (bool IsCharVoice, string CharLabel, string CharHtml, string ActorHtml, string EntryHtml)[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                var ent = bs.Entries[i];
                if (ent.EntryKind == "CHARACTER_VOICE")
                {
                    string lbl = await ResolveCharacterLabelAsync(ent, ct).ConfigureAwait(false);
                    string chh = await ResolveCharacterLabelHtmlAsync(ent, ct).ConfigureAwait(false);
                    string ahh = await ResolvePersonWithAffiliationHtmlAsync(ent, ct).ConfigureAwait(false);
                    resolved[i] = (true, lbl, chh, ahh, "");
                }
                else
                {
                    string eh = await ResolveEntryHtmlAsync(ent, ct).ConfigureAwait(false);
                    resolved[i] = (false, "", "", "", eh);
                }
            }
            // rowspanFor[i]：このエントリの actor-cell を rowspan=N で出す（N>=2 の場合属性付き、
            // N==1 は普通の単一セル、N==0 は actor-cell を一切出さない rowspan 吸収行）。
            int[] rowspanFor = new int[entryCount];
            for (int i = 0; i < entryCount; i++) rowspanFor[i] = 1;
            for (int i = 0; i < entryCount; )
            {
                if (!resolved[i].IsCharVoice || string.IsNullOrEmpty(resolved[i].ActorHtml))
                {
                    i++;
                    continue;
                }
                int j = i + 1;
                while (j < entryCount
                       && resolved[j].IsCharVoice
                       && string.Equals(resolved[j].ActorHtml, resolved[i].ActorHtml, StringComparison.Ordinal))
                {
                    j++;
                }
                int span = j - i;
                rowspanFor[i] = span;
                for (int k = i + 1; k < j; k++) rowspanFor[k] = 0;
                i = j;
            }

            for (int i = 0; i < entryCount; i++)
            {
                var e = bs.Entries[i];
                var slot = resolved[i];
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

                if (slot.IsCharVoice)
                {
                    bool sameAsPrev = prevCharLabel is not null
                        && string.Equals(slot.CharLabel, prevCharLabel, StringComparison.Ordinal);
                    if (sameAsPrev)
                    {
                        html.Append("<td class=\"character-cell dim\"></td>");
                    }
                    else
                    {
                        // キャラ名を /characters/{character_id}/ にリンク化する。
                        // CharacterAliasId → CharacterId を LookupCache で解決し、解決できれば <a> ラップ、
                        // できないときはエスケープ済み素テキストにフォールバックする。
                        // 字下げ用の全角スペースは leading_company 直下行にだけ加える。
                        string charPrefix = hasLeading ? "　" : "";
                        html.Append($"<td class=\"character-cell\">{charPrefix}{slot.CharHtml}</td>");
                    }

                    int span = rowspanFor[i];
                    if (span >= 2)
                    {
                        // n 役連続：先頭行で rowspan=N の actor-cell を出し、後続行は actor-cell を完全省略。
                        // CSS で td.actor-cell[rowspan] の vertical-align を middle に上書きしているので、
                        // 結合セルは N 行ぶんの中央に視覚的に揃う。
                        html.Append($"<td class=\"actor-cell\" rowspan=\"{span}\">{slot.ActorHtml}</td>");
                    }
                    else if (span == 1)
                    {
                        html.Append($"<td class=\"actor-cell\">{slot.ActorHtml}</td>");
                    }
                    // span == 0：直前の rowspan で吸収されるので何も出さない。

                    prevCharLabel = slot.CharLabel;
                }
                else
                {
                    // CHARACTER_VOICE 以外（PERSON / COMPANY / LOGO / TEXT 等）は声優名カラム側に表示。
                    html.Append("<td class=\"character-cell\"></td>");
                    string actorPrefix = hasLeading ? "　" : "";
                    html.Append($"<td class=\"actor-cell\">{actorPrefix}{slot.EntryHtml}</td>");
                    prevCharLabel = null;
                }

                html.Append("</tr>");
            }
            isFirstBlock = false;
        }

        // 「協力」行の追記。
        // レイアウトを変更し、「協力」ラベルを 1 段目（役職名カラム）ではなく
        // 2 段目（キャラ名カラム = character-cell）に置く。声の出演ブロックでは
        // 「○○役」がキャラ名カラム、声優名が声優名カラムに並ぶ構造なので、協力行も同じく
        // 「協力」をキャラ名カラム、屋号一覧を声優名カラムに置くことで、目線の流れが自然に揃う。
        // 1 段目（役職名カラム）は空にして縦の見出し列をすっきりさせる。
        //
        // character-cell に置く「協力」ラベルを
        // BuildRoleNameHtml で /stats/roles/CASTING_COOPERATION/ にリンク化する。
        // roleMap に CASTING_COOPERATION が登録されていればその NameJa（通常「協力」）+ リンク、
        // 未登録時は素のフォールバック文字列「協力」となる。
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
                // role マスタから「協力」相当の表示名（NameJa）を引いてリンク化。
                // マスタ未登録のときはフォールバックで素テキスト「協力」を出す（BuildRoleNameHtml の仕様）。
                string cooperationRoleName = roleMap.TryGetValue(RoleCodeCastingCooperation, out var coopRole)
                    ? (string.IsNullOrEmpty(coopRole.NameJa) ? "協力" : coopRole.NameJa)
                    : "協力";
                string cooperationLabelHtml = BuildRoleNameHtml(RoleCodeCastingCooperation, cooperationRoleName, roleMap);

                html.Append("<tr class=\"cooperation-row\">");
                // 1 段目（役職名カラム）は空。声の出演ブロックの 2 行目以降と同じ「役職名抑止」状態。
                html.Append("<td class=\"role-name\"></td>");
                // 2 段目（キャラ名カラム）に「協力」を置く。CSS .cooperation-row .character-cell で
                // 装飾（右寄せ・太字）を当てる。役職リンクは BuildRoleNameHtml が <a> を含めて返す。
                html.Append("<td class=\"character-cell\">").Append(cooperationLabelHtml).Append("</td>");
                // 3 段目（声優名カラム）に屋号一覧。屋号はクレジット階層の通常通り <a> リンク済み。
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

    /// <summary>キャラ名義（character_alias）の表示名を「キャラ詳細ページへのリンク済み HTML 断片」として返す。</summary>
    private async Task<string> ResolveCharacterLabelHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        // キャラ名義の本体 HTML を組み立てた後、キャラ側の誤記（CharacterMisprintText）があれば
        // 左側に「<del>誤記</del> 」を前置する。
        string baseHtml;
        if (e.CharacterAliasId is int caId)
        {
            string? name = await _lookup.LookupCharacterAliasNameAsync(caId).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(name))
            {
                int? characterId = await _lookup.LookupCharacterIdFromAliasAsync(caId).ConfigureAwait(false);
                baseHtml = characterId.HasValue
                    ? $"<a href=\"/characters/{characterId.Value}/\">{Esc(name)}</a>"
                    : Esc(name);
                return PrependMisprintHtml(baseHtml, e.CharacterMisprintText);
            }
        }
        if (!string.IsNullOrWhiteSpace(e.RawCharacterText))
            return PrependMisprintHtml(Esc(e.RawCharacterText!), e.CharacterMisprintText);
        return PrependMisprintHtml("(キャラ未指定)", e.CharacterMisprintText);
    }

    /// <summary>人物名義 ＋ 所属屋号をプレーンテキストとして返す（融合表示の同名判定にだけ使う）。</summary>
    private async Task<string> ResolvePersonWithAffiliationLabelAsync(CreditBlockEntry e, CancellationToken ct)
    {
        string name = e.PersonAliasId.HasValue
            ? (await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)"
            : "(名義未指定)";
        // 所属表示は 3 パターン：
        //   両持ち (ID + override テキスト) → テキスト側を表示（リンク先は ID の企業詳細だが、ラベル経路では URL は出さない）
        //   ID のみ → 屋号マスタ名
        //   テキストのみ → そのまま
        if (e.AffiliationCompanyAliasId is int afid)
        {
            string displayLabel = !string.IsNullOrEmpty(e.AffiliationText)
                ? e.AffiliationText!
                : (await _lookup.LookupCompanyAliasNameAsync(afid).ConfigureAwait(false)) ?? "";
            if (!string.IsNullOrEmpty(displayLabel)) name += $" ({displayLabel})";
        }
        else if (!string.IsNullOrWhiteSpace(e.AffiliationText))
        {
            name += $" ({e.AffiliationText})";
        }
        return name;
    }

    /// <summary>人物名義 ＋ 所属屋号を「リンク済み HTML 断片」として返す。 人物側に誤記（<see cref="CreditBlockEntry.PersonMisprintText"/>）が立っていれば 「&lt;del&gt;誤記&lt;/del&gt; 正名義(所属)」の形で左側に前置する。</summary>
    private async Task<string> ResolvePersonWithAffiliationHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        // 表示テキスト：LookupCache.LookupPersonAliasNameAsync は pa.Name を返す。
        string displayText = e.PersonAliasId.HasValue
            ? ((await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)")
            : "(名義未指定)";
        string nameHtml = _staffLinkResolver.ResolveAsHtml(e.PersonAliasId, displayText);

        // 所属付加：括弧付きで表示。3 パターン：
        //   両持ち (ID + override テキスト) → テキスト側を表示文字列にしつつ ID の企業詳細リンクを張る
        //                                     （「本名 陽子 (ABCアナウンサー)」表示で、ABCアナウンサーが朝日放送詳細リンクになる）
        //   ID のみ → 屋号マスタ名 + 企業詳細リンク
        //   テキストのみ → エスケープしたテキスト（リンクなし）
        // affiliation_inline = false なら名前の直後ではなく <br> で改行して別行で表示する。
        // .staff-affiliation クラスで 80% 縮小フォント・muted 色を適用。
        string? affilInnerHtml = null;
        if (e.AffiliationCompanyAliasId is int afid)
        {
            string displayLabel = !string.IsNullOrEmpty(e.AffiliationText)
                ? e.AffiliationText!
                : (await _lookup.LookupCompanyAliasNameAsync(afid).ConfigureAwait(false)) ?? "";
            if (!string.IsNullOrEmpty(displayLabel))
            {
                affilInnerHtml = await BuildCompanyAliasHtmlAsync(afid, displayLabel).ConfigureAwait(false);
            }
        }
        else if (!string.IsNullOrWhiteSpace(e.AffiliationText))
        {
            affilInnerHtml = Esc(e.AffiliationText!);
        }
        if (affilInnerHtml is not null)
        {
            string sep = e.AffiliationInline ? " " : "<br>";
            nameHtml += $"{sep}<span class=\"staff-affiliation\">({affilInnerHtml})</span>";
        }
        // 誤記は所属の外側、名義断片全体の左に前置（誤記は名義そのものに対する注釈であって、
        // 所属表記まで含めた塊に対するものではないため、所属より外で前置するのが意味的に整合）。
        return PrependMisprintHtml(nameHtml, e.PersonMisprintText);
    }

    /// <summary>1 エントリ分の表示を「リンク済み HTML 断片」として返す。 PERSON / CHARACTER_VOICE / COMPANY / LOGO / TEXT の 5 種に対応。 各種別ごとにクレジット時の誤記（Person / Character / Company）を左側に前置する。</summary>
    private async Task<string> ResolveEntryHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                // PERSON は ResolvePersonWithAffiliationHtmlAsync 側で誤記前置済み。
                return await ResolvePersonWithAffiliationHtmlAsync(e, ct).ConfigureAwait(false);

            case "CHARACTER_VOICE":
                {
                    // VC テーブル外の文脈で CHARACTER_VOICE エントリが現れた場合のフォールバック表示。
                    // 通常は VC 専用テーブル内でキャラ／声優別カラムに分けて出す。
                    // ここではキャラ側・人物側それぞれの誤記をそれぞれの名義に前置する。
                    string charName = e.CharacterAliasId.HasValue
                        ? ((await _lookup.LookupCharacterAliasNameAsync(e.CharacterAliasId.Value).ConfigureAwait(false)) ?? "(キャラ不明)")
                        : (e.RawCharacterText ?? "(キャラ未指定)");
                    string charHtml = PrependMisprintHtml(Esc(charName), e.CharacterMisprintText);

                    string voiceText = e.PersonAliasId.HasValue
                        ? ((await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(声優不明)")
                        : "(声優未指定)";
                    string voiceHtml = _staffLinkResolver.ResolveAsHtml(e.PersonAliasId, voiceText);
                    voiceHtml = PrependMisprintHtml(voiceHtml, e.PersonMisprintText);
                    return $"{charHtml} … {voiceHtml}";
                }

            case "COMPANY":
                {
                    if (!e.CompanyAliasId.HasValue) return PrependMisprintHtml(Esc("(企業屋号未指定)"), e.CompanyMisprintText);
                    string? name = await _lookup.LookupCompanyAliasNameAsync(e.CompanyAliasId.Value).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(name)) return PrependMisprintHtml(Esc("(企業屋号不明)"), e.CompanyMisprintText);
                    string baseHtml = await BuildCompanyAliasHtmlAsync(e.CompanyAliasId.Value, name).ConfigureAwait(false);
                    return PrependMisprintHtml(baseHtml, e.CompanyMisprintText);
                }

            case "LOGO":
                {
                    string baseHtml = await BuildLogoHtmlAsync(e.LogoId).ConfigureAwait(false);
                    return PrependMisprintHtml(baseHtml, e.CompanyMisprintText);
                }

            case "TEXT":
                return Esc(e.RawText ?? "");

            default:
                return Esc($"({e.EntryKind})");
        }
    }
}