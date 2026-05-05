using System.Net;
using System.Text;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Catalog.Forms.TemplateRendering;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Preview;

/// <summary>
/// クレジット 1 件を HTML 文字列として整形するレンダラ
/// （v1.2.0 工程 H-9 で導入、H-10 で role_templates 統合に追従、H-11 で Draft 直接描画 + 階層余白対応）。
/// <para>
/// クレジット編集画面に常時埋め込まれるプレビューペイン（v1.2.0 工程 H-11 で 4 ペインレイアウトに変更）が
/// WebBrowser コントロールに流し込む HTML を生成する。<br/>
/// (a) <see cref="RenderCreditAsync"/> ：DB 保存済みクレジット 1 件を SELECT して描画（保存後の確認用）<br/>
/// (b) <see cref="RenderDraftAsync"/> ：編集セッション（<see cref="CreditDraftSession"/>）の中身を直接描画
///   （Draft リアルタイム反映用、保存していない編集状態がそのまま見える）
/// </para>
/// <para>
/// 役職テンプレートは <see cref="RoleTemplatesRepository.ResolveAsync"/> を使って解決する。
/// 解決順序：(role_code, series_id) で検索 → 無ければ (role_code, NULL) → それも無ければ
/// テンプレ未定義扱いで「役職名 + ブロック内エントリの右並び表」のフォールバック表示に落とす。
/// </para>
/// <para>
/// HTML は IE11 互換モードで動くよう <c>X-UA-Compatible</c> メタタグを埋め込む（WebBrowser コントロール
/// は既定で IE7 互換のため）。<br/>
/// 工程 H-11 改修：階層構造（カード／Tier／グループ／ブロック）の境界に空行を入れ、視覚的に区切る。<br/>
/// テンプレ展開結果はエスケープせず素通し（<c>&lt;b&gt;</c> 等のタグが効く）。<br/>
/// クレジット種別の見出しは <see cref="CreditKindsRepository"/> から日本語名を引いて表示。<br/>
/// LOGO エントリは屋号名のみ表示（CI バージョンラベル非表示）。
/// </para>
/// </summary>
internal sealed class CreditPreviewRenderer
{
    private readonly IConnectionFactory _factory;
    private readonly RolesRepository _rolesRepo;
    private readonly RoleTemplatesRepository _roleTemplatesRepo;
    private readonly CreditKindsRepository _creditKindsRepo;
    private readonly CreditCardsRepository _cardsRepo;
    private readonly CreditCardTiersRepository _tiersRepo;
    private readonly CreditCardGroupsRepository _groupsRepo;
    private readonly CreditCardRolesRepository _cardRolesRepo;
    private readonly CreditRoleBlocksRepository _blocksRepo;
    private readonly CreditBlockEntriesRepository _entriesRepo;
    private readonly LookupCache _lookup;

    public CreditPreviewRenderer(
        IConnectionFactory factory,
        RolesRepository rolesRepo,
        RoleTemplatesRepository roleTemplatesRepo,
        CreditKindsRepository creditKindsRepo,
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
        _creditKindsRepo = creditKindsRepo;
        _cardsRepo = cardsRepo;
        _tiersRepo = tiersRepo;
        _groupsRepo = groupsRepo;
        _cardRolesRepo = cardRolesRepo;
        _blocksRepo = blocksRepo;
        _entriesRepo = entriesRepo;
        _lookup = lookup;
    }

    // ============================================================================================
    // 共通：HTML ヘッダ／フッタ
    // ============================================================================================

    /// <summary>HTML 文書ヘッダ（DOCTYPE ～ &lt;body&gt;）と CSS。本クラスの全レンダーメソッドで共通。</summary>
    private const string HtmlHead = """
        <!DOCTYPE html>
        <html lang="ja">
        <head>
        <meta charset="utf-8">
        <meta http-equiv="X-UA-Compatible" content="IE=edge">
        <title>クレジットプレビュー</title>
        <style>
          /* 基本タイポグラフィ */
          body {
            font-family: 'Yu Gothic UI', 'Meiryo', sans-serif;
            font-size: 14px;
            color: #222;
            background: #fff;
            margin: 16px;
            line-height: 1.6;
          }
          /* クレジット種別見出し（オープニングクレジット／エンディングクレジット） */
          h1 {
            font-size: 16px;
            font-weight: bold;
            margin: 0 0 12px 0;
            padding: 0 0 4px 0;
            border: none;
            border-bottom: 1px solid #ccc;
          }
          /* クレジット間の境界（複数クレジットを 1 HTML に入れたとき） */
          hr.credit-separator {
            border: 0;
            border-top: 1px dashed #aaa;
            margin: 24px 0;
          }
          /* 階層余白（v1.2.0 工程 H-11 追加） ──
             カード／Tier／グループ／ブロックの境界に空き高さを入れて、構造の切れ目を視覚化する。
             margin-top をそれぞれ持たせ、:first-child では 0 にして先頭の余白を抑える。 */
          .card  { margin-top: 18px; }
          .card:first-child { margin-top: 0; }
          .tier  { margin-top: 12px; }
          .tier:first-child { margin-top: 0; }
          .group { margin-top: 8px; }
          .group:first-child { margin-top: 0; }
          .role  { margin-top: 4px; }
          .role:first-child { margin-top: 0; }
          /* テンプレ展開結果ブロック：エスケープせず素通しで <b> 等を効かせる。
             v1.2.0 工程 H-14：旧来 white-space: pre-wrap を指定していたが、これを有効にすると
             改行コード \r が単独でも改行扱いされ、レンダラ側で \n → <br> 置換した後に \r が残ると
             二重改行になってしまう。本工程ではレンダラ側で改行コードを完全に正規化したうえで
             <br> に変換する方式に統一したため、CSS 側では pre-wrap を外して通常の HTML 改行として
             扱う。連続空白の保持は不要（テンプレ作者は明示的に &nbsp; や HTML 構造で表現する）。 */
          .role-rendered {
            margin: 0;
            padding: 0;
          }
          /* テンプレ未定義時のフォールバック表（役職名 | エントリ右並び） */
          table.fallback-table {
            border-collapse: collapse;
            margin: 0;
          }
          table.fallback-table td {
            padding: 0 16px 0 0;
            vertical-align: top;
          }
          table.fallback-table td.role-name {
            font-weight: bold;
            min-width: 8em;
            padding-right: 16px;
          }
          table.fallback-table td.entry-cell {
            padding-right: 24px;
          }
          /* クレジット未選択／空状態のメッセージ */
          .empty-credit {
            color: #999;
            font-style: italic;
          }
          /* テンプレ展開エラー表示 */
          .render-error {
            color: #c0392b;
            font-size: 12px;
          }
        </style>
        </head>
        <body>
        """;

    /// <summary>HTML 文書末尾（&lt;/body&gt;&lt;/html&gt;）。</summary>
    private const string HtmlFoot = "</body></html>";

    /// <summary>HTML エスケープ。</summary>
    private static string Esc(string s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>表示順固定マップ：OP=1, ED=2, それ以外=999。</summary>
    private static int KindOrder(string k) => k switch { "OP" => 1, "ED" => 2, _ => 999 };

    /// <summary>
    /// テンプレ解決用のシリーズ ID を決める：SERIES スコープなら credit.SeriesId、
    /// EPISODE スコープなら episode_id を逆引きしてその所属シリーズ ID を返す
    /// （v1.2.0 工程 H-12 追加）。
    /// <para>
    /// これにより EPISODE スコープのクレジットでも「シリーズ専用テンプレ」が反映されるようになる。
    /// それまでは EPISODE スコープでは常に null（既定テンプレのみ）を渡していたため、シリーズ別
    /// テンプレを設定してもプレビューに反映されない問題があった。
    /// </para>
    /// </summary>
    private async Task<int?> ResolveTemplateSeriesIdAsync(Credit credit, CancellationToken ct)
    {
        if (credit.ScopeKind == "SERIES") return credit.SeriesId;
        if (credit.EpisodeId is not int eid || eid <= 0) return null;

        // episodes.series_id を引いてくる軽量クエリ。テーブル直接 SELECT で済ませる
        // （EpisodesRepository に GetByIdAsync を追加するほどでもないため）。
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        const string sql = "SELECT series_id FROM episodes WHERE episode_id = @eid LIMIT 1;";
        return await Dapper.SqlMapper.ExecuteScalarAsync<int?>(conn,
            new Dapper.CommandDefinition(sql, new { eid }, cancellationToken: ct));
    }


    /// <summary>
    /// 複数クレジット（OP / ED 等）をまとめて HTML 文字列にする（DB 保存済み版）。
    /// 各クレジットの間にはセクション見出しと薄い水平線を入れる。
    /// 並び順は <see cref="KindOrder"/> に従う（OP → ED → その他）。
    /// </summary>
    public async Task<string> RenderCreditsAsync(IReadOnlyList<Credit> credits, CancellationToken ct = default)
    {
        var kinds = await _creditKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var kindMap = kinds.ToDictionary(k => k.KindCode, k => k.NameJa);

        var html = new StringBuilder();
        html.Append(HtmlHead);

        if (credits.Count == 0)
        {
            html.Append("<p class=\"empty-credit\">表示するクレジットがありません。</p>");
        }
        else
        {
            // 並び順を OP → ED 固定に揃える
            var sorted = credits.OrderBy(c => KindOrder(c.CreditKind)).ThenBy(c => c.CreditKind).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) html.Append("<hr class=\"credit-separator\">");
                await RenderOneCreditFromDbAsync(sorted[i], kindMap, html, ct).ConfigureAwait(false);
            }
        }

        html.Append(HtmlFoot);
        return html.ToString();
    }

    /// <summary>クレジット 1 件を DB から SELECT しながら HTML 化（保存後の確認用、Draft なし）。</summary>
    private async Task RenderOneCreditFromDbAsync(
        Credit credit,
        IReadOnlyDictionary<string, string> kindMap,
        StringBuilder html,
        CancellationToken ct)
    {
        string kindLabel = kindMap.TryGetValue(credit.CreditKind, out var nm) ? nm : credit.CreditKind;
        html.Append($"<h1>{Esc(kindLabel)}</h1>");

        var cards = (await _cardsRepo.GetByCreditAsync(credit.CreditId, ct).ConfigureAwait(false))
            .OrderBy(c => c.CardSeq).ToList();
        if (cards.Count == 0) { html.Append("<p class=\"empty-credit\">（カード未登録）</p>"); return; }

        var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var roleMap = allRoles.ToDictionary(r => r.RoleCode);
        int? resolveSeriesId = await ResolveTemplateSeriesIdAsync(credit, ct).ConfigureAwait(false);

        foreach (var card in cards)
        {
            html.Append("<div class=\"card\">");
            var tiers = (await _tiersRepo.GetByCardAsync(card.CardId, ct).ConfigureAwait(false))
                .OrderBy(t => t.TierNo).ToList();
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
                    foreach (var cr in cardRoles)
                    {
                        // 配下のブロック・エントリを SELECT で構築
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
                        await RenderCardRoleCommonAsync(credit.ScopeKind, credit.EpisodeId, credit.CreditKind,
                            cr.RoleCode, roleMap, resolveSeriesId, snapshots, html, ct).ConfigureAwait(false);
                    }
                    html.Append("</div>"); // .group
                }
                html.Append("</div>"); // .tier
            }
            html.Append("</div>"); // .card
        }
    }

    // ============================================================================================
    // 公開メソッド：Draft セッションからの直接描画（編集中リアルタイム反映用、v1.2.0 工程 H-11 追加）
    // ============================================================================================

    /// <summary>
    /// 編集セッション（<see cref="CreditDraftSession"/>）の中身を直接 HTML 化する（Draft リアルタイム反映用）。
    /// <para>
    /// クレジット編集画面の常時埋め込みプレビューペインから呼ばれる。Draft オブジェクトは仮 ID（負値）を
    /// 持つ可能性があるが、テンプレ展開エンジン（<see cref="RoleTemplateRenderer"/>）は ID を直接参照しないため、
    /// そのまま <see cref="BlockSnapshot"/> に詰めて渡せばよい。<br/>
    /// State==Deleted の Draft はツリーから取り除かれているわけではない（バケットに退避される設計）が、
    /// ここでは念のため Deleted を除外しながら走査する。
    /// </para>
    /// <para>
    /// 注意：テンプレ DSL の <c>{THEME_SONGS}</c> ハンドラは <c>episode_theme_songs</c> テーブルを SELECT する
    /// ため、Draft の編集途中でも DB の現状値が反映される。主題歌は別管理の運用なので、Draft 編集の影響は
    /// 主題歌セクションには及ばない設計（ユーザーは納得済み）。
    /// </para>
    /// </summary>
    public async Task<string> RenderDraftAsync(CreditDraftSession session, CancellationToken ct = default)
    {
        var html = new StringBuilder();
        html.Append(HtmlHead);

        if (session is null || session.Root is null)
        {
            html.Append("<p class=\"empty-credit\">（クレジット未選択）</p>");
            html.Append(HtmlFoot);
            return html.ToString();
        }

        var kinds = await _creditKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var kindMap = kinds.ToDictionary(k => k.KindCode, k => k.NameJa);

        var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var roleMap = allRoles.ToDictionary(r => r.RoleCode);

        var credit = session.Root.Entity;
        int? resolveSeriesId = await ResolveTemplateSeriesIdAsync(credit, ct).ConfigureAwait(false);

        string kindLabel = kindMap.TryGetValue(credit.CreditKind, out var nm) ? nm : credit.CreditKind;
        html.Append($"<h1>{Esc(kindLabel)}</h1>");

        // Draft の Card → Tier → Group → Role を辿る。State==Deleted は除外。順序キーは Entity の seq 列に従う。
        var draftCards = session.Root.Cards
            .Where(c => c.State != DraftState.Deleted)
            .OrderBy(c => c.Entity.CardSeq).ToList();
        if (draftCards.Count == 0)
        {
            html.Append("<p class=\"empty-credit\">（カード未登録）</p>");
            html.Append(HtmlFoot);
            return html.ToString();
        }

        foreach (var dCard in draftCards)
        {
            html.Append("<div class=\"card\">");
            foreach (var dTier in dCard.Tiers
                .Where(t => t.State != DraftState.Deleted)
                .OrderBy(t => t.Entity.TierNo))
            {
                html.Append("<div class=\"tier\">");
                foreach (var dGroup in dTier.Groups
                    .Where(g => g.State != DraftState.Deleted)
                    .OrderBy(g => g.Entity.GroupNo))
                {
                    html.Append("<div class=\"group\">");
                    foreach (var dRole in dGroup.Roles
                        .Where(r => r.State != DraftState.Deleted)
                        .OrderBy(r => r.Entity.OrderInGroup))
                    {
                        // Draft の Block / Entry を BlockSnapshot に詰める
                        var snapshots = new List<BlockSnapshot>();
                        foreach (var dBlock in dRole.Blocks
                            .Where(b => b.State != DraftState.Deleted)
                            .OrderBy(b => b.Entity.BlockSeq))
                        {
                            var entries = dBlock.Entries
                                .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                                .OrderBy(e => e.Entity.EntrySeq)
                                .Select(e => e.Entity)
                                .ToList();
                            snapshots.Add(new BlockSnapshot(dBlock.Entity, entries));
                        }
                        await RenderCardRoleCommonAsync(credit.ScopeKind, credit.EpisodeId, credit.CreditKind,
                            dRole.Entity.RoleCode, roleMap, resolveSeriesId, snapshots, html, ct).ConfigureAwait(false);
                    }
                    html.Append("</div>"); // .group
                }
                html.Append("</div>"); // .tier
            }
            html.Append("</div>"); // .card
        }

        html.Append(HtmlFoot);
        return html.ToString();
    }

    // ============================================================================================
    // 内部：1 役職の描画（DB / Draft 共通）
    // ============================================================================================

    /// <summary>
    /// 1 つの役職を HTML 化する共通ロジック。テンプレが取れれば DSL 展開、無ければフォールバック表に落とす。
    /// </summary>
    private async Task RenderCardRoleCommonAsync(
        string scopeKind,
        int? episodeId,
        string creditKind,
        string? roleCode,
        IReadOnlyDictionary<string, Role> roleMap,
        int? resolveSeriesId,
        IReadOnlyList<BlockSnapshot> blocks,
        StringBuilder html,
        CancellationToken ct)
    {
        // 役職表示名を解決
        string roleName = "";
        if (!string.IsNullOrEmpty(roleCode))
        {
            roleName = roleMap.TryGetValue(roleCode!, out var r) ? (r.NameJa ?? roleCode!) : roleCode!;
        }

        // テンプレを role_templates から解決
        string? template = null;
        if (!string.IsNullOrEmpty(roleCode))
        {
            var tpl = await _roleTemplatesRepo.ResolveAsync(roleCode!, resolveSeriesId, ct).ConfigureAwait(false);
            template = tpl?.FormatTemplate;
        }

        html.Append("<div class=\"role\">");

        if (!string.IsNullOrWhiteSpace(template))
        {
            // ── DSL 展開で描画 ──
            // テンプレ展開結果は HTML として信頼する（テンプレは管理者が書く＝信頼できる入力）。
            // <b> 等のタグは素通しさせ、改行のみ <br> に変換する。
            try
            {
                var ast = TemplateParser.Parse(template!);
                var ctx = new TemplateContext(roleCode ?? "", roleName, blocks, scopeKind, episodeId, creditKind);
                string rendered = await RoleTemplateRenderer.RenderAsync(ast, ctx, _factory, _lookup, ct).ConfigureAwait(false);

                // v1.2.0 工程 H-14：改行コード正規化。
                // TextBox.Text は Windows 標準で \r\n、Linux 由来テンプレでは \n、MacOS 古い形式では \r
                // が混在し得る。「\r\n → \n、\r → \n」の順で正規化してから <br> に置換することで
                // 二重改行や CR が残るのを確実に防ぐ。
                string normalized = rendered.Replace("\r\n", "\n").Replace("\r", "\n");
                string brTransformed = normalized.Replace("\n", "<br>");

                // v1.2.0 工程 H-15：テンプレに {ROLE_NAME} プレースホルダが含まれていなければ、
                // フォールバック表（役職名カラム + 内容カラム）と同じ 2 カラム整列にレンダラ側で自動的に
                // ラップする。これにより、テンプレ作者は内容部分だけを書けばよくなり、
                // 役職名はレンダラが固定幅で前置するため、テンプレ展開役職とフォールバック役職が
                // 視覚的に整列する。
                // 旧来の「テンプレ自前レイアウト」を維持したい場合は、テンプレ内に {ROLE_NAME} を
                // 1 つでも含めれば判定が変わり、自動ラップは行われず素通し展開になる。
                bool templateHasRoleName = template!.Contains("{ROLE_NAME}", StringComparison.Ordinal);
                if (templateHasRoleName)
                {
                    // 自前レイアウト：テンプレ側で {ROLE_NAME} を使って構造を完全制御する想定。
                    html.Append("<div class=\"role-rendered\">");
                    html.Append(brTransformed);
                    html.Append("</div>");
                }
                else
                {
                    // 自動ラップ：フォールバック表と同じ「役職名 | 展開結果」の 2 カラムテーブル。
                    // CSS class は既存の fallback-table を流用し、視覚的整列を保つ。
                    html.Append("<table class=\"fallback-table\"><tr>");
                    html.Append($"<td class=\"role-name\">{Esc(roleName)}</td>");
                    html.Append("<td class=\"entry-cell\">");
                    html.Append(brTransformed);
                    html.Append("</td></tr></table>");
                }
            }
            catch (Exception ex)
            {
                // テンプレ展開エラー時はエラー注記 + フォールバック表で続行(プレビュー全体は止めない)
                html.Append("<div class=\"role-rendered\">");
                html.Append($"<span class=\"render-error\">⚠ テンプレ展開エラー: {Esc(ex.Message)} — フォールバック表示に切り替え</span><br>");
                await RenderRoleFallbackAsync(roleName, blocks, html, ct).ConfigureAwait(false);
                html.Append("</div>");
            }
        }
        else
        {
            // ── テンプレ未定義時のフォールバック表 ──
            await RenderRoleFallbackAsync(roleName, blocks, html, ct).ConfigureAwait(false);
        }

        html.Append("</div>"); // .role
    }

    /// <summary>
    /// テンプレ未定義時のフォールバック表示：
    /// 役職名を左カラムに固定幅で出し、その右に Block 内の各エントリを <c>col_count</c> で横並びにする。
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
        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;
            // col_count に応じてエントリを行に分割。ColCount は byte 型のため Math.Max(int,int) と
            // Math.Max(byte,byte) の曖昧解決を避けるため明示的に int キャストする。
            int cols = Math.Max(1, (int)bs.Block.ColCount);
            for (int i = 0; i < bs.Entries.Count; i += cols)
            {
                html.Append("<tr>");
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
                        html.Append(Esc(label));
                    }
                    html.Append("</td>");
                }
                html.Append("</tr>");
            }
        }
        html.Append("</table>");
    }

    /// <summary>
    /// 1 エントリを表示文字列に解決する（フォールバック表示用）。種別ごとに名義・屋号・ロゴ屋号名・キャラ
    /// 名義 + 声優名義・フリーテキストを LookupCache 経由で引いて整形する。
    /// LOGO は CI バージョンラベルを出さず、紐づく屋号名のみを表示する（v1.2.0 工程 H-10 改修）。
    /// </summary>
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
