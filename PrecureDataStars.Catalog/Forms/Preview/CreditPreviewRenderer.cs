using System.Net;
using System.Text;
// GetHideStoryboardRoleAsync で CommandDefinition / ExecuteScalarAsync 拡張メソッドを使うため Dapper を参照する。
using Dapper;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.TemplateRendering;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Preview;

/// <summary>
/// クレジット 1 件を HTML 文字列として整形するレンダラ
/// （role_templates 統合に対応、Draft 直接描画 + 階層余白対応）。
/// クレジット編集画面に常時埋め込まれるプレビューペイン（4 ペインレイアウトに変更）が
/// WebBrowser コントロールに流し込む HTML を生成する。<br/>
/// (a) <see cref="RenderCreditAsync"/> ：DB 保存済みクレジット 1 件を SELECT して描画（保存後の確認用）<br/>
/// (b) <see cref="RenderDraftAsync"/> ：編集セッション（<see cref="CreditDraftSession"/>）の中身を直接描画
///   （Draft リアルタイム反映用、保存していない編集状態がそのまま見える）
/// 役職テンプレートは <see cref="RoleTemplatesRepository.ResolveAsync"/> を使って解決する。
/// 解決順序：(role_code, series_id) で検索 → 無ければ (role_code, NULL) → それも無ければ
/// テンプレ未定義扱いで「役職名 + ブロック内エントリの右並び表」のフォールバック表示に落とす。
/// HTML は IE11 互換モードで動くよう <c>X-UA-Compatible</c> メタタグを埋め込む（WebBrowser コントロール
/// は既定で IE7 互換のため）。<br/>
/// 階層構造（カード／Tier／グループ／ブロック）の境界に空行を入れ、視覚的に区切る。<br/>
/// テンプレ展開結果はエスケープせず素通し（<c>&lt;b&gt;</c> 等のタグが効く）。<br/>
/// クレジット種別の見出しは <see cref="CreditKindsRepository"/> から日本語名を引いて表示。<br/>
/// LOGO エントリは屋号名のみ表示（CI バージョンラベル非表示）。
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

    // 共通：HTML ヘッダ／フッタ

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
          /* 階層余白──
             カード／Tier／グループ／ロール／ブロックの境界に空き高さを入れて、構造の切れ目を視覚化する。
             margin-top をそれぞれ持たせ、:first-child では 0 にして先頭の余白を抑える。
             「グループ内のロール間 = 基準値」を起点に、カード／ティア／グループ → 大きく、
             ブロックの切り替わり → グループ内ロール間と同等の余白を確保する：
                ブロック (block-break) ≦ ロール ＜ グループ ＜ ティア ＜ カード
             */
          .card  { margin-top: 40px; }
          .card:first-child { margin-top: 0; }
          .tier  { margin-top: 24px; }
          .tier:first-child { margin-top: 0; }
          .group { margin-top: 14px; }
          .group:first-child { margin-top: 0; }
          .role  { margin-top: 6px; }   /* グループ内のロール間（基準値） */
          .role:first-child { margin-top: 0; }
          /* テーブル内のブロック区切り行。td に padding-top を入れて、
             同役職内でブロックが切り替わる箇所に、グループ内のロール変わり目（.role の margin-top）と
             同等の余白を確保し、構造の切れ目を視覚化する。 */
          table.fallback-table tr.block-break > td,
          table.fallback-vc-table tr.block-break > td {
            padding-top: 6px;
          }
          /* キャスティング協力の追記行（VOICE_CAST テーブル末尾に詰め込む形式）。
             別ロール扱いとしての視覚的余白を出すため、ロール変わり目相当（.role の margin-top）と
             同じ大きさを上に確保する。 */
          table.fallback-vc-table tr.cooperation-row > td {
            padding-top: 6px;
          }
          /* 協力行の「協力」セル。SiteBuilder の .cooperation-row .character-cell と同じく
             右寄せ・太字にして、リンクが無くても見た目を SiteBuilder と揃える。 */
          table.fallback-vc-table tr.cooperation-row td.character-cell {
            text-align: right;
            font-weight: bold;
          }
          /* テンプレ展開結果ブロック：エスケープせず素通しで <b> 等を効かせる。
             white-space: pre-wrap は指定しない。レンダラ側で改行コードを完全に正規化したうえで
             <br> に変換する方式に統一しているため、CSS 側で pre-wrap を指定すると \r 残留により
             二重改行になり得る。連続空白の保持は不要（テンプレ作者は明示的に &nbsp; や
             HTML 構造で表現する）。 */
          .role-rendered {
            margin: 0;
            padding: 0;
          }
          /* コンテンツ領域ヘッダ上書き（role_templates.content_header_override）。
             左カラム役職名の代替として、シリーズ別「役職名の表示テキスト・位置だけ変える」用途で使う。
             本体（フォールバック表 or テンプレ展開結果）と視覚的に区切るために下マージンを取る。
             x 位置は他フォールバック行の右カラム（entry-cell）と揃えるため、role-name 列の
             min-width: 8em + padding-right: 16px に相当する左マージンを足す（Catalog プレビューは
             credit-align.js を持たないので固定値で運用）。 */
          .role-content-header {
            margin: 0 0 8px calc(8em + 16px);
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
          /* PREFIX レイアウト（映画の製作・配給など）の屋号セル。80% 縮小フォント + muted で、
             左の人名カラムを引き立てる「2 カラム的」見せ方を作る。直前行と同じ屋号が空欄に
             圧縮されているとき、空セルになっても列幅は維持する（右側 cell が縦に揃う）。 */
          table.fallback-table td.affil-prefix {
            font-size: 80%;
            color: #888;
            padding-right: 14px;
            min-width: 8em;
            vertical-align: top;
          }
          /* 名前 (所属) 表記の所属括弧部分。SiteBuilder の .staff-affiliation と同じ意匠で
             80% 縮小フォント + muted。インライン / 別行どちらのレイアウトでも適用される。 */
          .staff-affiliation {
            font-size: 80%;
            color: #888;
          }
          /* VOICE_CAST 役職用の 3 カラムフォールバック表
             （役職名 | キャラ名義 | 声優名義）。テンプレ未定義時に role_format_kind="VOICE_CAST" を
             検出して適用する。.fallback-table と挙動を揃えるため共通項目は重複定義しない。 */
          table.fallback-vc-table {
            border-collapse: collapse;
            margin: 0;
          }
          table.fallback-vc-table td {
            padding: 0 16px 0 0;
            vertical-align: top;
          }
          table.fallback-vc-table td.role-name {
            font-weight: bold;
            min-width: 8em;
            padding-right: 16px;
          }
          table.fallback-vc-table td.character-cell {
            min-width: 6em;
            padding-right: 16px;
          }
          table.fallback-vc-table td.actor-cell {
            padding-right: 24px;
          }
          /* n 役連続（同じ声優が連続キャラを担当）で rowspan 結合された actor-cell は
             既定の vertical-align: top を上書きして、N 行ぶんの中央に揃える。 */
          table.fallback-vc-table td.actor-cell[rowspan] {
            vertical-align: middle;
          }
          /* 直前行と同じキャラ名義のときに表示を省略するときの空セル
             （横幅は維持してテキストを空にする）。 */
          table.fallback-vc-table td.character-cell.dim {
            color: #ccc;
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
    /// <summary>表示文字列を HTML エスケープする。
    /// 「⚠ 」プレフィクス（<c>LookupCache.PendingMark</c> と同じ U+26A0 + 半角SP）で始まる文字列は
    /// 「Pending マスタを参照している」サインなので、全体を赤太字の span で包んで出す。
    /// これによりプレビュー上で「保存待ちのマスタ」を視覚的に区別できる（TreeView 側のノード全体赤色と意味論を揃える）。
    /// </summary>
    private static string Esc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.StartsWith("⚠ ", StringComparison.Ordinal))
        {
            return "<span style=\"color:#c00;font-weight:bold;\">" + WebUtility.HtmlEncode(s) + "</span>";
        }
        return WebUtility.HtmlEncode(s);
    }

    /// <summary>表示順固定マップ：OP=1, ED=2, それ以外=999。</summary>
    private static int KindOrder(string k) => k switch { "OP" => 1, "ED" => 2, _ => 999 };

    /// <summary>
    /// テンプレ解決用のシリーズ ID を決める：SERIES スコープなら credit.SeriesId、
    /// EPISODE スコープなら episode_id を逆引きしてその所属シリーズ ID を返す。
    /// これにより EPISODE スコープのクレジットでも「シリーズ専用テンプレ」が反映されるようになる。
    /// それまでは EPISODE スコープでは常に null（既定テンプレのみ）を渡していたため、シリーズ別
    /// テンプレを設定してもプレビューに反映されない問題があった。
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

    /// <summary>複数クレジット（OP / ED 等）をまとめて HTML 文字列にする（DB 保存済み版）。 各クレジットの間にはセクション見出しと薄い水平線を入れる。 並び順は <see cref="KindOrder"/> に従う（OP → ED → その他）。</summary>
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

        // シリーズの「絵コンテ・演出融合表示」フラグを取得。
        bool hideStoryboardRole = await GetHideStoryboardRoleAsync(resolveSeriesId, ct).ConfigureAwait(false);

        // 直前にレンダリングした VOICE_CAST 役職の role_code を覚えておく。
        // 同じ role_code が連続して登場した場合、フォールバック描画では役職名カラムを空表示にし、
        // 「声の出演」見出しがカード/Tier/Group 跨ぎで毎回再表示されるのを抑止する
        // （ユーザー要望: VOICE_CAST だけが対象。「原画」等の通常役職は従来どおり毎回再表示）。
        // 切り替わり判定の対象は VOICE_CAST 役職のみ。NORMAL 役職を間に挟むと chain は途切れる。
        string? prevVoiceCastRoleCode = null;

        foreach (var card in cards)
        {
            html.Append("<div class=\"card\">");
            var tiers = (await _tiersRepo.GetByCardAsync(card.CardId, ct).ConfigureAwait(false))
                .OrderBy(t => t.TierNo).ToList();

            // カード単位で CASTING_COOPERATION エントリを事前収集する。
            // 「同一カードに VOICE_CAST 役職と CASTING_COOPERATION 役職の両方がある」場合のみ、
            // 「カード内で最後の VOICE_CAST 役職」のテーブル末尾に「協力」行として追記する仕様。
            // CASTING_COOPERATION 役職本体はこの場合スキップ。VOICE_CAST が無いカードの
            // CASTING_COOPERATION は通常通り描画される。
            var cooperationContext = await CollectCardCastingCooperationContextAsync(
                card.CardId, tiers, roleMap, ct).ConfigureAwait(false);
            // null チェック簡略化用の変数。
            // tuple なので、null 時は entries も lastId も使わない（appendThisRole 判定で短絡される）。
            IReadOnlyList<CreditBlockEntry>? cooperationEntriesForCard = cooperationContext?.Entries;
            int? cooperationAppendTargetCardRoleId = cooperationContext?.LastVoiceCastCardRoleId;

            // 絵コンテ・演出融合のカード横断事前スキャン。
            // STORYBOARD と EPISODE_DIRECTOR がカード内の表示順（tier_no → group_no → order_in_group）で
            // 隣接していれば、Group を跨いでいても融合表示する（テキスト入力 `--` で Group 区切りされたパターンも捕捉）。
            // 戻り値：(sb の CardRoleId → ペア情報の辞書, dir の CardRoleId 集合)。
            var sbMergeBySbId = new Dictionary<int, (CreditCardRole Sb, CreditCardRole Dir, bool SameGroup)>();
            var sbMergedDirIds = new HashSet<int>();
            if (hideStoryboardRole)
            {
                var orderedRoles = new List<(int GroupId, CreditCardRole Role)>();
                foreach (var pt in tiers)
                {
                    var pgroups = (await _groupsRepo.GetByTierAsync(pt.CardTierId, ct).ConfigureAwait(false))
                        .OrderBy(g => g.GroupNo).ToList();
                    foreach (var pg in pgroups)
                    {
                        var prRoles = (await _cardRolesRepo.GetByGroupAsync(pg.CardGroupId, ct).ConfigureAwait(false))
                            .OrderBy(r => r.OrderInGroup);
                        foreach (var prr in prRoles) orderedRoles.Add((pg.CardGroupId, prr));
                    }
                }
                for (int i = 0; i + 1 < orderedRoles.Count; i++)
                {
                    var (gid1, r1) = orderedRoles[i];
                    var (gid2, r2) = orderedRoles[i + 1];
                    bool isSb = string.Equals(r1.RoleCode, RoleCodeStoryboard, StringComparison.Ordinal);
                    bool isDir = string.Equals(r2.RoleCode, RoleCodeEpisodeDirector, StringComparison.Ordinal);
                    if (isSb && isDir)
                    {
                        sbMergeBySbId[r1.CardRoleId] = (r1, r2, gid1 == gid2);
                        sbMergedDirIds.Add(r2.CardRoleId);
                        i++;
                    }
                }
            }

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

                    // 絵コンテ・演出融合：カード事前スキャンで決定済みの dir 側 ID を group ローカル skip 集合に詰める。
                    // 旧版の「Group 内重複検出 + 1:1 限定」ロジックは廃止し、カード横断 + N:M 対応の新フローに移行。
                    // sb 側 role が来た時点で融合本体を発火する判定は cardRoles ループ内（後段）で行う。
                    HashSet<int> mergedCardRoleIds = new(sbMergedDirIds);

                    // 同 Group 内 sibling 役職の Block/Entry を事前ロードして辞書化。
                    // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文用。各役職の処理時に SiblingRoleResolver として
                    // TemplateContext に渡すことで、レンダラから sibling 役職の中身が透過的に見える。
                    // 各役職本体の処理でもこの辞書を流用して Block の重複ロードを避ける。
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
                    Func<string, IReadOnlyList<BlockSnapshot>?> siblingResolver = code =>
                        siblingBlocksByRoleCode.TryGetValue(code, out var s) ? s : null;

                    // 同 Group 内の役職テンプレ群を事前スキャンし、{ROLE:CODE.PLACEHOLDER}
                    // 構文で「消費」される sibling role_code 集合を作る。メインループで消費先
                    // role_code を持つ役職は描画スキップする（典型例：SERIALIZED_IN テンプレが
                    // MANGA を {ROLE:MANGA.PERSONS} で参照する場合、MANGA 自体が空見出し行として
                    // 二重出力されるのを防ぐ）。役職テンプレが無い、または ROLE 参照を含まない
                    // 場合は何も追加しない。判定は SiteBuilder 側 CreditTreeRenderer と同一規則。
                    var consumedRoleCodes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var siblingCr in cardRoles)
                    {
                        var prescanCode = siblingCr.RoleCode;
                        if (string.IsNullOrEmpty(prescanCode)) continue;
                        var prescanTpl = await _roleTemplatesRepo.ResolveAsync(prescanCode!, resolveSeriesId, ct).ConfigureAwait(false);
                        string? prescanTemplate = prescanTpl?.FormatTemplate;
                        if (string.IsNullOrWhiteSpace(prescanTemplate)) continue;
                        // 軽量検出：テンプレ文字列中の {ROLE:<CODE>. から <CODE> を抜き出す。
                        // 正規の AST 解析は実描画時に RoleTemplateRenderer 側で行うため、ここは
                        // 「ある role_code が他役職に参照されているか」の判定で足りる。
                        int prescanPos = 0;
                        while (true)
                        {
                            int idx = prescanTemplate!.IndexOf("{ROLE:", prescanPos, StringComparison.Ordinal);
                            if (idx < 0) break;
                            int dotIdx = prescanTemplate.IndexOf('.', idx + 6);
                            int endIdx = prescanTemplate.IndexOf('}', idx + 6);
                            if (dotIdx < 0 || (endIdx >= 0 && dotIdx > endIdx))
                            {
                                prescanPos = idx + 6;
                                continue;
                            }
                            string consumedCode = prescanTemplate.Substring(idx + 6, dotIdx - (idx + 6)).Trim();
                            if (!string.IsNullOrEmpty(consumedCode))
                                consumedRoleCodes.Add(consumedCode);
                            prescanPos = dotIdx + 1;
                        }
                    }

                    foreach (var cr in cardRoles)
                    {
                        // 融合描画で消費済みの cardRole はスキップ。
                        if (mergedCardRoleIds.Contains(cr.CardRoleId)) continue;

                        // 絵コンテ・演出融合：sb 側 role に到達した時点で融合本体を発火。
                        // dir 側 role は事前スキャンで mergedCardRoleIds に入っており、上の continue で既にスキップ済み。
                        if (sbMergeBySbId.TryGetValue(cr.CardRoleId, out var mergePair))
                        {
                            // sb / dir それぞれの block → entries 二重リストを broadcast_only フィルタ + 順序付きで構築。
                            async Task<List<List<CreditBlockEntry>>> LoadBlocksAsync(int cardRoleId)
                            {
                                var result = new List<List<CreditBlockEntry>>();
                                var blocks = (await _blocksRepo.GetByCardRoleAsync(cardRoleId, ct).ConfigureAwait(false))
                                    .OrderBy(b => b.BlockSeq).ToList();
                                foreach (var b in blocks)
                                {
                                    var entries = (await _entriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                        .Where(e => !e.IsBroadcastOnly)
                                        .OrderBy(e => e.EntrySeq).ToList();
                                    if (entries.Count > 0) result.Add(entries);
                                }
                                return result;
                            }
                            var sbBlocks = await LoadBlocksAsync(mergePair.Sb.CardRoleId).ConfigureAwait(false);
                            var dirBlocks = await LoadBlocksAsync(mergePair.Dir.CardRoleId).ConfigureAwait(false);
                            await RenderStoryboardDirectorMergedAsync(sbBlocks, dirBlocks, mergePair.SameGroup, html, ct).ConfigureAwait(false);
                            prevVoiceCastRoleCode = null;
                            continue;
                        }

                        // CASTING_COOPERATION 役職本体の描画スキップ判定。
                        if (cooperationEntriesForCard is not null
                            && cooperationEntriesForCard.Count > 0
                            && string.Equals(cr.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // 他役職テンプレに {ROLE:CODE.…} で消費される役職はメインループから外す
                        // （SERIALIZED_IN に取り込まれた MANGA の二重出力防止）。
                        if (!string.IsNullOrEmpty(cr.RoleCode)
                            && consumedRoleCodes.Contains(cr.RoleCode!))
                        {
                            continue;
                        }

                        // sibling 辞書から自身の Block を引いて使い回し（重複ロード回避）。
                        IReadOnlyList<BlockSnapshot> snapshots;
                        if (!string.IsNullOrEmpty(cr.RoleCode)
                            && siblingBlocksByRoleCode.TryGetValue(cr.RoleCode!, out var cached))
                        {
                            snapshots = cached;
                        }
                        else
                        {
                            // 配下のブロック・エントリを SELECT で構築（RoleCode が null の場合の fallback）
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

                        // VOICE_CAST 役職名カード跨ぎ省略判定。
                        // 直前ロールが VOICE_CAST かつ同じ role_code なら、役職名表示を抑止する。
                        bool suppressVoiceCastRoleName =
                            !string.IsNullOrEmpty(cr.RoleCode)
                            && prevVoiceCastRoleCode is not null
                            && string.Equals(prevVoiceCastRoleCode, cr.RoleCode, StringComparison.Ordinal)
                            && IsVoiceCastRole(cr.RoleCode, roleMap);

                        // VOICE_CAST 役職にだけ「協力」行追記情報を渡す。
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

                        // 直前ロール記憶を更新: 当該ロールが VOICE_CAST なら role_code を覚える、
                        // それ以外（NORMAL/SERIAL/THEME_SONG など）なら chain を切るために null に戻す。
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
    }

    /// <summary>
    /// 指定カード内で「VOICE_CAST 役職」と「CASTING_COOPERATION 役職」が両方存在するかを判定し、
    /// 両方ある場合のみ、CASTING_COOPERATION 役職配下の全エントリ（複数ロール・複数ブロック横断）と
    /// 「カード内で最後に登場する VOICE_CAST 役職の cardRoleId」をペアで返す。
    /// VOICE_CAST が無いカード、または CASTING_COOPERATION が無いカードでは null を返す。
    /// 「最後の VOICE_CAST 役職」を返す理由は、同一カード内に VOICE_CAST 役職が複数あるとき
    /// （例: 主役声優 Group と脇役声優 Group が分かれている）、すべての VOICE_CAST テーブル末尾に
    /// 「協力」行が付くと重複表示になるため、最後の 1 つにだけ追記する仕様にしているため。
    /// 「最後の」判定は描画順序と一致させる必要があるので、Tier の TierNo 昇順 → Group の GroupNo 昇順 →
    /// CardRole の OrderInGroup 昇順で走査して、見つかった VOICE_CAST 役職のうち最後のものを採用する。
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

        // 両方そろっていなければ null（追記処理を発動させない）。
        if (lastVcCardRoleId is null || cooperationCardRoleIds.Count == 0) return null;

        // CASTING_COOPERATION 配下のエントリを集約する。
        var aggregated = new List<CreditBlockEntry>();
        foreach (var crId in cooperationCardRoleIds)
        {
            var entries = await CollectEntriesUnderCardRoleAsync(crId, ct).ConfigureAwait(false);
            aggregated.AddRange(entries);
        }
        return (aggregated, lastVcCardRoleId.Value);
    }

    /// <summary>指定 cardRoleId 配下の全ブロック・全エントリ（is_broadcast_only 除外）を 1 つのフラットリストに集める （絵コンテ・演出融合判定用ヘルパ）。</summary>
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

    // 公開メソッド：Draft セッションからの直接描画（編集中リアルタイム反映用）

    /// <summary>
    /// 編集セッション（<see cref="CreditDraftSession"/>）の中身を直接 HTML 化する（Draft リアルタイム反映用）。
    /// クレジット編集画面の常時埋め込みプレビューペインから呼ばれる。Draft オブジェクトは仮 ID（負値）を
    /// 持つ可能性があるが、テンプレ展開エンジン（<see cref="RoleTemplateRenderer"/>）は ID を直接参照しないため、
    /// そのまま <see cref="BlockSnapshot"/> に詰めて渡せばよい。<br/>
    /// State==Deleted の Draft はツリーから取り除かれているわけではない（バケットに退避される設計）が、
    /// ここでは念のため Deleted を除外しながら走査する。
    /// 注意：テンプレ DSL の <c>{THEME_SONGS}</c> ハンドラは <c>episode_theme_songs</c> テーブルを SELECT する
    /// ため、Draft の編集途中でも DB の現状値が反映される。主題歌は別管理の運用なので、Draft 編集の影響は
    /// 主題歌セクションには及ばない設計（ユーザーは納得済み）。
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

        // シリーズの「絵コンテ・演出融合表示」フラグを取得（DB 描画側と同じ仕様）。
        bool hideStoryboardRole = await GetHideStoryboardRoleAsync(resolveSeriesId, ct).ConfigureAwait(false);

        // 直前 VOICE_CAST 役職コード追跡（DB 描画側と同じ仕様）。
        string? prevVoiceCastRoleCode = null;

        foreach (var dCard in draftCards)
        {
            html.Append("<div class=\"card\">");

            // カード単位で CASTING_COOPERATION エントリを事前収集（Draft 側、DB 側と同等）。
            var draftCooperationContext = CollectDraftCardCastingCooperationContext(dCard, roleMap);
            IReadOnlyList<CreditBlockEntry>? cooperationEntriesForCard = draftCooperationContext?.Entries;
            DraftRole? cooperationAppendTargetRole = draftCooperationContext?.LastVoiceCastRole;

            // 絵コンテ・演出融合のカード横断事前スキャン（Draft 側）。
            // 表示順（tier_no → group_no → order_in_group）で STORYBOARD と EPISODE_DIRECTOR が
            // 隣接しているペアを検出する。Group を跨いでいても OK（テキスト入力 `--` 越えも捕捉）。
            // 参照同一性で照合するため、key には DraftRole の CurrentId（負数 ID を含む）を使う。
            var draftSbMergeBySbId = new Dictionary<int, (DraftRole Sb, DraftRole Dir, bool SameGroup)>();
            var draftSbMergedDirIds = new HashSet<int>();
            if (hideStoryboardRole)
            {
                var orderedRoles = new List<(int GroupId, DraftRole Role)>();
                foreach (var dt in dCard.Tiers.Where(t => t.State != DraftState.Deleted).OrderBy(t => t.Entity.TierNo))
                {
                    foreach (var dg in dt.Groups.Where(g => g.State != DraftState.Deleted).OrderBy(g => g.Entity.GroupNo))
                    {
                        foreach (var dr in dg.Roles.Where(r => r.State != DraftState.Deleted).OrderBy(r => r.Entity.OrderInGroup))
                        {
                            orderedRoles.Add((dg.CurrentId, dr));
                        }
                    }
                }
                for (int i = 0; i + 1 < orderedRoles.Count; i++)
                {
                    var (gid1, r1) = orderedRoles[i];
                    var (gid2, r2) = orderedRoles[i + 1];
                    bool isSb = string.Equals(r1.Entity.RoleCode, RoleCodeStoryboard, StringComparison.Ordinal);
                    bool isDir = string.Equals(r2.Entity.RoleCode, RoleCodeEpisodeDirector, StringComparison.Ordinal);
                    if (isSb && isDir)
                    {
                        draftSbMergeBySbId[r1.CurrentId] = (r1, r2, gid1 == gid2);
                        draftSbMergedDirIds.Add(r2.CurrentId);
                        i++;
                    }
                }
            }

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
                    var dRoles = dGroup.Roles
                        .Where(r => r.State != DraftState.Deleted)
                        .OrderBy(r => r.Entity.OrderInGroup)
                        .ToList();

                    // 絵コンテ・演出融合は Draft カード事前スキャンで決定済み。
                    // ここでは「sb 側 DraftRole が dRoles に含まれていれば融合本体を発火、dir 側 DraftRole は skip」を
                    // dRoles ループ内（次の foreach）で処理する。group 単独での検出ロジックは廃止済み。

                    // Draft 側 sibling 役職辞書を作って {ROLE:CODE.PLACEHOLDER} 構文に備える。
                    var siblingBlocksByRoleCode = new Dictionary<string, IReadOnlyList<BlockSnapshot>>(StringComparer.Ordinal);
                    foreach (var siblingDRole in dRoles)
                    {
                        string? rc = siblingDRole.Entity.RoleCode;
                        if (string.IsNullOrEmpty(rc)) continue;
                        if (siblingBlocksByRoleCode.ContainsKey(rc!)) continue;

                        var sbSnapshots = new List<BlockSnapshot>();
                        foreach (var dBlock in siblingDRole.Blocks
                            .Where(b => b.State != DraftState.Deleted)
                            .OrderBy(b => b.Entity.BlockSeq))
                        {
                            var entries = dBlock.Entries
                                .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                                .OrderBy(e => e.Entity.EntrySeq)
                                .Select(e => e.Entity)
                                .ToList();
                            sbSnapshots.Add(new BlockSnapshot(dBlock.Entity, entries));
                        }
                        siblingBlocksByRoleCode[rc!] = sbSnapshots;
                    }
                    Func<string, IReadOnlyList<BlockSnapshot>?> siblingResolver = code =>
                        siblingBlocksByRoleCode.TryGetValue(code, out var s) ? s : null;

                    // 同 Group 内の役職テンプレ群を事前スキャンし、{ROLE:CODE.PLACEHOLDER}
                    // 構文で「消費」される sibling role_code 集合を作る。メインループで消費先
                    // role_code を持つ役職は描画スキップする（典型例：SERIALIZED_IN テンプレが
                    // MANGA を {ROLE:MANGA.PERSONS} で参照する場合、MANGA 自体が空見出し行として
                    // 二重出力されるのを防ぐ）。役職テンプレが無い、または ROLE 参照を含まない
                    // 場合は何も追加しない。判定は SiteBuilder 側 CreditTreeRenderer と同一規則。
                    var consumedRoleCodes = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var siblingDRole in dRoles)
                    {
                        var prescanCode = siblingDRole.Entity.RoleCode;
                        if (string.IsNullOrEmpty(prescanCode)) continue;
                        var prescanTpl = await _roleTemplatesRepo.ResolveAsync(prescanCode!, resolveSeriesId, ct).ConfigureAwait(false);
                        string? prescanTemplate = prescanTpl?.FormatTemplate;
                        if (string.IsNullOrWhiteSpace(prescanTemplate)) continue;
                        // 軽量検出：テンプレ文字列中の {ROLE:<CODE>. から <CODE> を抜き出す。
                        // 正規の AST 解析は実描画時に RoleTemplateRenderer 側で行うため、ここは
                        // 「ある role_code が他役職に参照されているか」の判定で足りる。
                        int prescanPos = 0;
                        while (true)
                        {
                            int idx = prescanTemplate!.IndexOf("{ROLE:", prescanPos, StringComparison.Ordinal);
                            if (idx < 0) break;
                            int dotIdx = prescanTemplate.IndexOf('.', idx + 6);
                            int endIdx = prescanTemplate.IndexOf('}', idx + 6);
                            if (dotIdx < 0 || (endIdx >= 0 && dotIdx > endIdx))
                            {
                                prescanPos = idx + 6;
                                continue;
                            }
                            string consumedCode = prescanTemplate.Substring(idx + 6, dotIdx - (idx + 6)).Trim();
                            if (!string.IsNullOrEmpty(consumedCode))
                                consumedRoleCodes.Add(consumedCode);
                            prescanPos = dotIdx + 1;
                        }
                    }

                    foreach (var dRole in dRoles)
                    {
                        // 融合済み dir 側 role は skip（事前スキャンで決定済み、CurrentId 突合）。
                        if (draftSbMergedDirIds.Contains(dRole.CurrentId)) continue;

                        // 絵コンテ・演出融合：sb 側 role に到達した時点で融合本体を発火。
                        if (draftSbMergeBySbId.TryGetValue(dRole.CurrentId, out var draftMergePair))
                        {
                            static List<List<CreditBlockEntry>> CollectDraftBlocks(DraftRole role)
                            {
                                var result = new List<List<CreditBlockEntry>>();
                                foreach (var dBlock in role.Blocks
                                    .Where(b => b.State != DraftState.Deleted)
                                    .OrderBy(b => b.Entity.BlockSeq))
                                {
                                    var entries = dBlock.Entries
                                        .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                                        .OrderBy(e => e.Entity.EntrySeq)
                                        .Select(e => e.Entity)
                                        .ToList();
                                    if (entries.Count > 0) result.Add(entries);
                                }
                                return result;
                            }
                            var sbBlocks = CollectDraftBlocks(draftMergePair.Sb);
                            var dirBlocks = CollectDraftBlocks(draftMergePair.Dir);
                            await RenderStoryboardDirectorMergedAsync(sbBlocks, dirBlocks, draftMergePair.SameGroup, html, ct).ConfigureAwait(false);
                            prevVoiceCastRoleCode = null;
                            continue;
                        }

                        // CASTING_COOPERATION 役職本体の描画スキップ判定（Draft 側）。
                        if (cooperationEntriesForCard is not null
                            && cooperationEntriesForCard.Count > 0
                            && string.Equals(dRole.Entity.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // 他役職テンプレに {ROLE:CODE.…} で消費される役職はメインループから外す
                        // （SERIALIZED_IN に取り込まれた MANGA の二重出力防止、Draft 側）。
                        if (!string.IsNullOrEmpty(dRole.Entity.RoleCode)
                            && consumedRoleCodes.Contains(dRole.Entity.RoleCode!))
                        {
                            continue;
                        }

                        // 自身の Block を sibling 辞書から流用（重複構築を回避）。
                        IReadOnlyList<BlockSnapshot> snapshots;
                        if (!string.IsNullOrEmpty(dRole.Entity.RoleCode)
                            && siblingBlocksByRoleCode.TryGetValue(dRole.Entity.RoleCode!, out var cached))
                        {
                            snapshots = cached;
                        }
                        else
                        {
                            // Draft の Block / Entry を BlockSnapshot に詰める（RoleCode が null の場合の fallback）
                            var snList = new List<BlockSnapshot>();
                            foreach (var dBlock in dRole.Blocks
                                .Where(b => b.State != DraftState.Deleted)
                                .OrderBy(b => b.Entity.BlockSeq))
                            {
                                var entries = dBlock.Entries
                                    .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                                    .OrderBy(e => e.Entity.EntrySeq)
                                    .Select(e => e.Entity)
                                    .ToList();
                                snList.Add(new BlockSnapshot(dBlock.Entity, entries));
                            }
                            snapshots = snList;
                        }

                        // VOICE_CAST 役職名カード跨ぎ省略判定（Draft 側）。
                        bool suppressVoiceCastRoleName =
                            !string.IsNullOrEmpty(dRole.Entity.RoleCode)
                            && prevVoiceCastRoleCode is not null
                            && string.Equals(prevVoiceCastRoleCode, dRole.Entity.RoleCode, StringComparison.Ordinal)
                            && IsVoiceCastRole(dRole.Entity.RoleCode, roleMap);

                        // VOICE_CAST 役職にだけ「協力」行追記情報を渡す（Draft 側）。
                        IReadOnlyList<CreditBlockEntry>? appendThisRole =
                            (IsVoiceCastRole(dRole.Entity.RoleCode, roleMap)
                             && cooperationAppendTargetRole is not null
                             && ReferenceEquals(dRole, cooperationAppendTargetRole))
                                ? cooperationEntriesForCard
                                : null;

                        await RenderCardRoleCommonAsync(credit.ScopeKind, credit.EpisodeId, credit.CreditKind,
                            dRole.Entity.RoleCode, roleMap, resolveSeriesId, snapshots,
                            suppressVoiceCastRoleName, appendThisRole, siblingResolver,
                            affiliationLayout: dRole.Entity.AffiliationLayout,
                            html, ct).ConfigureAwait(false);

                        prevVoiceCastRoleCode = IsVoiceCastRole(dRole.Entity.RoleCode, roleMap)
                            ? dRole.Entity.RoleCode
                            : null;
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

    /// <summary>
    /// Draft セッション上の指定カード内で「VOICE_CAST 役職」と「CASTING_COOPERATION 役職」が両方 存在するかを判定し
    /// 、両方ある場合のみ CASTING_COOPERATION 役職配下の全エントリ （複数ロール・複数ブロック横断）と「カード内で最後に登場する VOICE_CAST 役職の DraftRole 参照」 をペアで返す。
    /// </summary>
    private (List<CreditBlockEntry> Entries, DraftRole LastVoiceCastRole)? CollectDraftCardCastingCooperationContext(
        DraftCard dCard,
        IReadOnlyDictionary<string, Role> roleMap)
    {
        DraftRole? lastVcRole = null;
        var cooperationRoles = new List<DraftRole>();
        foreach (var dTier in dCard.Tiers.Where(t => t.State != DraftState.Deleted)
                                          .OrderBy(t => t.Entity.TierNo))
        {
            foreach (var dGroup in dTier.Groups.Where(g => g.State != DraftState.Deleted)
                                                .OrderBy(g => g.Entity.GroupNo))
            {
                foreach (var dRole in dGroup.Roles.Where(r => r.State != DraftState.Deleted)
                                                   .OrderBy(r => r.Entity.OrderInGroup))
                {
                    if (IsVoiceCastRole(dRole.Entity.RoleCode, roleMap)) lastVcRole = dRole;
                    if (string.Equals(dRole.Entity.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        cooperationRoles.Add(dRole);
                }
            }
        }

        if (lastVcRole is null || cooperationRoles.Count == 0) return null;

        // CASTING_COOPERATION 役職配下のエントリ（is_broadcast_only 除外）を時系列順に集約。
        var aggregated = new List<CreditBlockEntry>();
        foreach (var dRole in cooperationRoles)
        {
            foreach (var dBlock in dRole.Blocks.Where(b => b.State != DraftState.Deleted)
                                              .OrderBy(b => b.Entity.BlockSeq))
            {
                aggregated.AddRange(dBlock.Entries
                    .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                    .OrderBy(e => e.Entity.EntrySeq)
                    .Select(e => e.Entity));
            }
        }
        return (aggregated, lastVcRole);
    }

    // 内部：1 役職の描画（DB / Draft 共通）

    /// <summary>1 つの役職を HTML 化する共通ロジック。テンプレが取れれば DSL 展開、無ければフォールバック表に落とす。</summary>
    private async Task RenderCardRoleCommonAsync(
        string scopeKind,
        int? episodeId,
        string creditKind,
        string? roleCode,
        IReadOnlyDictionary<string, Role> roleMap,
        int? resolveSeriesId,
        IReadOnlyList<BlockSnapshot> blocks,
        // 直前ロールが同じ VOICE_CAST 役職だった場合の役職名抑止フラグ。
        // フォールバック描画ルートでのみ尊重される。テンプレ展開ルートでは無視（テンプレ作者が
        // 制御する想定。{ROLE_NAME} 自動ラップでも、抑止指示はせず素直に役職名を出す）。
        bool suppressVoiceCastRoleName,
        // VOICE_CAST テーブル末尾に「協力」行として追記する CASTING_COOPERATION
        // エントリ群。呼び出し側で同一カード内の CASTING_COOPERATION 役職のエントリを集めて渡す。
        // フォールバックの VOICE_CAST 描画ルートでのみ尊重される。
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        // 同 Group 内 sibling 役職の Block を引くコールバック。
        // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文用。null の場合は ROLE 参照が空文字に展開される。
        Func<string, IReadOnlyList<BlockSnapshot>?>? siblingRoleResolver,
        // 人物所属表記レイアウト ("SUFFIX" / "PREFIX")。PREFIX は映画製作・配給などの 2 カラム表示。
        string affiliationLayout,
        StringBuilder html,
        CancellationToken ct)
    {
        // 役職表示名を解決
        string roleName = "";
        if (!string.IsNullOrEmpty(roleCode))
        {
            if (roleMap.TryGetValue(roleCode!, out var r))
            {
                roleName = r.NameJa ?? roleCode!;
                // roles.hide_role_name_in_credit=1 の役職は HTML クレジット階層上で
                if (r.HideRoleNameInCredit == 1) roleName = "";
            }
            else
            {
                roleName = roleCode!;
            }
        }

        // テンプレを role_templates から解決
        string? template = null;
        string? contentHeaderOverride = null;
        if (!string.IsNullOrEmpty(roleCode))
        {
            var tpl = await _roleTemplatesRepo.ResolveAsync(roleCode!, resolveSeriesId, ct).ConfigureAwait(false);
            template = tpl?.FormatTemplate;
            contentHeaderOverride = string.IsNullOrEmpty(tpl?.ContentHeaderOverride) ? null : tpl!.ContentHeaderOverride;
        }

        html.Append("<div class=\"role\">");

        // コンテンツ領域ヘッダ上書き：シリーズ別「役職名の表示テキストだけ変えて、本体は通常描画」用途。
        // 非 NULL のとき役職ラッパ直下に <strong> + 役職詳細リンクを出し、後段のフォールバック描画では
        // 左カラム役職名を空表示にして二重ヘッダを防ぐ。プレビューはリンク無しの素テキストで出す。
        if (contentHeaderOverride is not null)
        {
            html.Append("<div class=\"role-content-header\"><strong>");
            html.Append(Esc(contentHeaderOverride));
            html.Append("</strong></div>");
        }

        if (!string.IsNullOrWhiteSpace(template))
        {
            // ── DSL 展開で描画 ──
            try
            {
                var ast = TemplateParser.Parse(template!);
                // sibling-role 解決のコールバックを渡して {ROLE:CODE.PLACEHOLDER} 構文に対応。
                // SERIES スコープのクレジット（映画系列）では scopeSeriesId に resolveSeriesId 相当を渡し、
                // {THEME_SONGS} ハンドラが series_theme_songs を引き当てるようにする。EPISODE スコープでは null。
                int? scopeSeriesIdForCtx = scopeKind == "SERIES" ? resolveSeriesId : null;
                // SERIES スコープの場合、テンプレで {SERIES_TITLE} を使えるよう series.title を解決して詰める。
                string scopeSeriesTitleForCtx = await GetSeriesTitleAsync(scopeSeriesIdForCtx, ct).ConfigureAwait(false);
                var ctx = new TemplateContext(roleCode ?? "", roleName, blocks, scopeKind, episodeId, scopeSeriesIdForCtx, creditKind,
                    siblingRoleResolver: siblingRoleResolver,
                    visitedRoleCodes: null,
                    scopeSeriesTitle: scopeSeriesTitleForCtx);
                string rendered = await RoleTemplateRenderer.RenderAsync(ast, ctx, _factory, _lookup, ct).ConfigureAwait(false);

                // 改行コード正規化。
                // TextBox.Text は Windows 標準で \r\n、Linux 由来テンプレでは \n、MacOS 古い形式では \r
                // が混在し得る。「\r\n → \n、\r → \n」の順で正規化してから <br> に置換することで
                // 二重改行や CR が残るのを確実に防ぐ。
                string normalized = rendered.Replace("\r\n", "\n").Replace("\r", "\n");
                string brTransformed = normalized.Replace("\n", "<br>");

                // 「テンプレが自前で役職見出しを持っている」と判定したら自動ラップを抑止して素通し展開。
                // 判定：(a) {ROLE_NAME} を含む、または (b) 自分の役職コードを参照する {ROLE_LINK:code=<roleCode>} を含む。
                // (b) はテンプレ冒頭に <strong>{ROLE_LINK:code=<this>,label=...}</strong> のような自己見出しを置く
                // ケース（シリーズ別カスタムテンプレ等）に対応するため。他役職コードの {ROLE_LINK} を
                // 単にフィールドラベル（作詞・作曲 等）として使うだけのテンプレは誤抑止しない。
                bool templateHasOwnRoleHeader =
                    contentHeaderOverride is not null
                    || template!.Contains("{ROLE_NAME}", StringComparison.Ordinal)
                    || (!string.IsNullOrEmpty(roleCode)
                        && template!.Contains("{ROLE_LINK:code=" + roleCode, StringComparison.Ordinal));
                if (templateHasOwnRoleHeader)
                {
                    // 自前レイアウト：テンプレ側 / コンテンツヘッダ上書き側で役職見出しを完全制御する想定。
                    // ただし展開結果のコンテンツ位置は fallback-table の右カラムと揃えたいので、
                    // 左カラム role-name を空文字で出して 2 カラム表構造に流し込む（声の出演や
                    // 他フォールバック行の右カラムと水平整列）。
                    html.Append("<table class=\"fallback-table\"><tr>");
                    html.Append("<td class=\"role-name\"></td>");
                    html.Append("<td class=\"entry-cell\">");
                    html.Append(brTransformed);
                    html.Append("</td></tr></table>");
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
                // VOICE_CAST 役職は専用の 3 カラム表示にフォールバックする。
                //         直前と同 VOICE_CAST 役職なら役職名カラムも抑止する。
                //         同一カード内に CASTING_COOPERATION があれば末尾に「協力」行を追記する。
                // ContentHeaderOverride 設定済みなら左カラム役職名を抑止（コンテンツヘッダで既出）。
                string fallbackRoleNameOnError = contentHeaderOverride is not null ? "" : roleName;
                await RenderRoleFallbackDispatchAsync(roleCode, fallbackRoleNameOnError, blocks, roleMap,
                    suppressVoiceCastRoleName, appendedCooperationEntries, affiliationLayout, html, ct).ConfigureAwait(false);
                html.Append("</div>");
            }
        }
        else
        {
            // ── テンプレ未定義時のフォールバック表 ──
            // VOICE_CAST 役職は専用の 3 カラム表示にフォールバックする。
            //         直前と同 VOICE_CAST 役職なら役職名カラムも抑止する。
            //         同一カード内に CASTING_COOPERATION があれば末尾に「協力」行を追記する。
            // ContentHeaderOverride 設定済みなら左カラム役職名を抑止（コンテンツヘッダで既出）。
            string fallbackRoleName = contentHeaderOverride is not null ? "" : roleName;
            await RenderRoleFallbackDispatchAsync(roleCode, fallbackRoleName, blocks, roleMap,
                suppressVoiceCastRoleName, appendedCooperationEntries, affiliationLayout, html, ct).ConfigureAwait(false);
        }

        html.Append("</div>"); // .role
    }

    /// <summary>フォールバック描画の振り分け。 役職の <c>role_format_kind</c> が <c>VOICE_CAST</c> なら 3 カラム表 （役職名 | キャラ名義 | 声優名義）にフォールバックし、それ以外は従来の <see cref="RenderRoleFallbackAsync"/>（役職名 | エントリ群を col_count カラム）に流す。
    /// <paramref name="affiliationLayout"/> が "PREFIX" の場合は専用の 3 カラム表（役職名 | 屋号 | 人名）に振り分ける。</summary>
    private async Task RenderRoleFallbackDispatchAsync(
        string? roleCode, string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        IReadOnlyDictionary<string, Role> roleMap,
        // VOICE_CAST 役職名抑止フラグ。VOICE_CAST 以外では使われない。
        bool suppressVoiceCastRoleName,
        // VOICE_CAST テーブルの末尾に「協力」行として追記するエントリ群。
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        string affiliationLayout,
        StringBuilder html, CancellationToken ct)
    {
        // PREFIX レイアウト（映画の製作・配給など）は専用 2 カラム表（屋号 | 人名）にフォールバックする。
        // VOICE_CAST / CASTING_COOPERATION 経路は適用しない。
        if (string.Equals(affiliationLayout, "PREFIX", StringComparison.Ordinal))
        {
            await RenderRoleFallbackPrefixAsync(roleName, blocks, html, ct).ConfigureAwait(false);
            return;
        }

        // role_format_kind を取得（マスタに無い役職や roleCode が null の場合は NORMAL 扱い）。
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

    /// <summary>PREFIX レイアウト専用フォールバック描画（プレビュー）：役職名（左）+ 「屋号 + 人名」の 2 カラム（右）。
    /// 直前行と屋号が同じなら左セルを空にして繰り返しを圧縮表示する。</summary>
    private async Task RenderRoleFallbackPrefixAsync(
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
        int? prevAffilAliasId = null;
        string? prevAffilText = null;
        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;

            bool isFirstRowOfThisBlock = true;
            prevAffilAliasId = null;
            prevAffilText = null;

            foreach (var e in bs.Entries)
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
                        affilHtml = !string.IsNullOrEmpty(affName) ? Esc(affName) : $"alias#{affId}";
                    }
                    else if (!string.IsNullOrEmpty(curAffilText))
                    {
                        affilHtml = Esc(curAffilText);
                    }
                }
                html.Append($"<td class=\"affil-prefix\">{affilHtml}</td>");

                // 人物名側は所属表記を抑止して取得する（同インスタンスを一時的に書き換えて復元）。
                int? savedAffilAlias = e.AffiliationCompanyAliasId;
                string? savedAffilText = e.AffiliationText;
                try
                {
                    e.AffiliationCompanyAliasId = null;
                    e.AffiliationText = null;
                    string entryHtml = await ResolveEntryLabelHtmlAsync(e, ct).ConfigureAwait(false);
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

    /// <summary>指定 role_code が VOICE_CAST 役職かどうかを判定する。 マスタに無い役職や null の場合は false。</summary>
    private static bool IsVoiceCastRole(string? roleCode, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (string.IsNullOrEmpty(roleCode)) return false;
        if (!roleMap.TryGetValue(roleCode, out var r)) return false;
        return string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定 series_id の <c>hide_storyboard_role</c> フラグを軽量 SQL で取得する。
    /// SeriesRepository を依存に追加するとコンストラクタの引数増加で呼び出し側まで波及するため、
    /// レンダラ内では <c>_factory</c> を直接使って単一値を取りに行く方針。値はクエリ単位の
    /// 計算量が小さく、キャッシュは導入していない（クレジット 1 件あたり 1 SELECT で十分軽量）。
    /// series_id が null（クレジットがどのシリーズに属するか解決できない）の場合は false を返す
    /// 安全側の動作とする。row が見つからない場合も同様に false（融合表示しない＝従来挙動）。
    /// </summary>
    private async Task<bool> GetHideStoryboardRoleAsync(int? seriesId, CancellationToken ct)
    {
        if (!seriesId.HasValue) return false;
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var raw = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT hide_storyboard_role FROM series WHERE series_id = @id AND is_deleted = 0;",
            new { id = seriesId.Value }, cancellationToken: ct));
        return raw.GetValueOrDefault() != 0;
    }

    /// <summary>指定 series_id の <c>series.title</c> を軽量 SQL で取得する。 テンプレ DSL の <c>{SERIES_TITLE}</c> プレースホルダ展開に使う。 series_id が null・行未存在・論理削除済みは空文字を返す（テンプレ側では空に展開される）。</summary>
    private async Task<string> GetSeriesTitleAsync(int? seriesId, CancellationToken ct)
    {
        if (!seriesId.HasValue) return "";
        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var title = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT title FROM series WHERE series_id = @id AND is_deleted = 0;",
            new { id = seriesId.Value }, cancellationToken: ct));
        return title ?? "";
    }

    /// <summary>絵コンテ役職コード。</summary>
    private const string RoleCodeStoryboard = "STORYBOARD";

    /// <summary>演出役職コード。</summary>
    private const string RoleCodeEpisodeDirector = "EPISODE_DIRECTOR";

    /// <summary>キャスティング協力役職コード（マスタにはシードしないが、運用者が手動追加することを前提に専用処理を行う）。</summary>
    private const string RoleCodeCastingCooperation = "CASTING_COOPERATION";

    /// <summary>
    /// 融合可能な「絵コンテ・演出」ペアを 1 つのテーブルに描画する。
    /// 仕様（hide_storyboard_role が ON のシリーズで起動）:
    /// <list type="bullet">
    ///   <item><description>同名（絵コンテと演出が同じ person_alias_id、または同じ raw_text）→
    ///     役職名 = 「（絵コンテ・）演出」、エントリ = その人物名 1 行</description></item>
    ///   <item><description>異名 → 役職名 = 「演出」、エントリ = 「名前A （絵コンテ）」「名前B （演出）」の 2 行</description></item>
    /// </list>
    /// 各役職のエントリ数がともに 1 件である場合に限り融合する。複数エントリの場合（共同絵コンテ、共同演出など）の
    /// 仕様は未定のため、本メソッドの呼び出し側で抑止し、通常描画にフォールバックする。
    /// </summary>
    /// <param name="storyboardEntries">STORYBOARD 役職配下の全エントリ（複数ブロック横断のフラットリスト）。</param>
    /// <param name="directorEntries">EPISODE_DIRECTOR 役職配下の全エントリ。</param>
    /// <param name="html">出力 StringBuilder。</param>
    /// <param name="ct">キャンセルトークン。</param>
    /// <summary>絵コンテ・演出融合表示（N:M 対応）。
    /// 入力：sb / dir それぞれの「ブロック → エントリ」二重リスト（broadcast_only フィルタ済み）と
    /// sameGroup フラグ（両 role が同 Group なら true、`--` で Group 区切りされていれば false）。
    /// 出力：1 つの &lt;table class="fallback-table"&gt; に左カラム空（先頭行のみ「演出」）+ 右カラム
    /// に sb エントリ群を「（絵コンテ）」後置で並べ、続けて dir エントリ群を「（演出）」後置で並べる。
    /// ブロック区切り（block_seq の境界）は &lt;tr class="block-break"&gt; で視覚ギャップ。
    /// sb の最終エントリと dir の先頭エントリの間は sameGroup=false の場合だけ block-break ギャップ。
    /// 1:1 で同一人物のケースは「（絵コンテ・）演出 ｜ 名前」の旧コンパクト表記を維持。</summary>
    private async Task RenderStoryboardDirectorMergedAsync(
        IReadOnlyList<IReadOnlyList<CreditBlockEntry>> storyboardBlocks,
        IReadOnlyList<IReadOnlyList<CreditBlockEntry>> directorBlocks,
        bool sameGroup,
        StringBuilder html,
        CancellationToken ct)
    {
        int totalSb = storyboardBlocks.Sum(b => b.Count);
        int totalDir = directorBlocks.Sum(b => b.Count);
        if (totalSb == 0 && totalDir == 0) return;

        // 旧コンパクト表記：sb 1 件 / dir 1 件 / 同一人物 → 「（絵コンテ・）演出 | 名前」1 行。
        if (totalSb == 1 && totalDir == 1)
        {
            var sb = storyboardBlocks[0][0];
            var dr = directorBlocks[0][0];
            bool sameName;
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
            if (sameName)
            {
                string directorHtml = await ResolvePersonWithAffiliationHtmlAsync(dr, ct).ConfigureAwait(false);
                html.Append("<div class=\"role\">");
                html.Append("<table class=\"fallback-table\"><tr>");
                html.Append($"<td class=\"role-name\">{Esc("（絵コンテ・）演出")}</td>");
                html.Append("<td class=\"entry-cell\">").Append(directorHtml).Append("</td>");
                html.Append("</tr></table></div>");
                return;
            }
        }

        // N:M 一般形：1 つの fallback-table に sb 群 → dir 群を縦に並べる。
        // 左カラム role-name は先頭行のみ「演出」、以降は空。役職区別は末尾「（絵コンテ）」「（演出）」で。
        string directorLabel = Esc("演出");
        string sbSuffix = $" {Esc("（絵コンテ）")}";
        string dirSuffix = $" {Esc("（演出）")}";

        html.Append("<div class=\"role\">");
        html.Append("<table class=\"fallback-table\">");
        bool firstRow = true;
        bool emittedAnySb = false;

        for (int bi = 0; bi < storyboardBlocks.Count; bi++)
        {
            var entries = storyboardBlocks[bi];
            bool isBlockBreak = bi > 0;
            for (int ei = 0; ei < entries.Count; ei++)
            {
                string trClass = (ei == 0 && isBlockBreak) ? " class=\"block-break\"" : "";
                html.Append($"<tr{trClass}>");
                if (firstRow) { html.Append($"<td class=\"role-name\">{directorLabel}</td>"); firstRow = false; }
                else          { html.Append("<td class=\"role-name\"></td>"); }
                string entryHtml = await ResolvePersonWithAffiliationHtmlAsync(entries[ei], ct).ConfigureAwait(false);
                html.Append("<td class=\"entry-cell\">").Append(entryHtml).Append(sbSuffix).Append("</td></tr>");
                emittedAnySb = true;
            }
        }

        bool gapBetween = emittedAnySb && !sameGroup;
        for (int bi = 0; bi < directorBlocks.Count; bi++)
        {
            var entries = directorBlocks[bi];
            bool isBlockBreak = bi > 0 || (bi == 0 && gapBetween);
            for (int ei = 0; ei < entries.Count; ei++)
            {
                string trClass = (ei == 0 && isBlockBreak) ? " class=\"block-break\"" : "";
                html.Append($"<tr{trClass}>");
                if (firstRow) { html.Append($"<td class=\"role-name\">{directorLabel}</td>"); firstRow = false; }
                else          { html.Append("<td class=\"role-name\"></td>"); }
                string entryHtml = await ResolvePersonWithAffiliationHtmlAsync(entries[ei], ct).ConfigureAwait(false);
                html.Append("<td class=\"entry-cell\">").Append(entryHtml).Append(dirSuffix).Append("</td></tr>");
            }
        }

        html.Append("</table></div>");
    }

    /// <summary>「絵コンテ・演出融合」の対象になる Group 内の cardRoles から、ストーリーボード／演出役職を 抽出し、両者が「融合可能」かを判定するヘルパ。</summary>
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
                if (storyboardRole is not null) { storyboardRole = null; directorRole = null; return false; } // 重複あり
                storyboardRole = r;
            }
            else if (string.Equals(code, RoleCodeEpisodeDirector, StringComparison.Ordinal))
            {
                if (directorRole is not null) { storyboardRole = null; directorRole = null; return false; } // 重複あり
                directorRole = r;
            }
        }
        return storyboardRole is not null && directorRole is not null;
    }

    /// <summary>テンプレ未定義時のフォールバック表示： 役職名を左カラムに固定幅で出し、その右に Block 内の各エントリを <c>col_count</c> で横並びにする。</summary>
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
            // col_count に応じてエントリを行に分割。ColCount は byte 型のため Math.Max(int,int) と
            // Math.Max(byte,byte) の曖昧解決を避けるため明示的に int キャストする。
            int cols = Math.Max(1, (int)bs.Block.ColCount);

            // ブロックの leading_company（グループトップ屋号）を解決する。
            // 値があれば、ブロック先頭行のエントリ列にその屋号名を太字なしで出し、
            // 後続のエントリ行は字下げ（全角SP 1 個分）してから本来のエントリを表示する。
            // この字下げはブロック内の中身が「トップ屋号配下である」ことを視覚的に示すためのもの。
            string? leadingCompanyName = null;
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            // ブロック跨ぎの視覚的区切り。ブロックが切り替わる先頭の <tr> に
            // class="block-break" を付与して CSS で間隔を出す（同役職内のブロック分けが見える）。
            // 最初のブロックには付けない（役職開始直後の余白は role 単位で既に出ているため）。
            bool isFirstRowOfThisBlock = true;

            // ─ leading_company がある場合の先頭行（屋号名のみ）─
            if (hasLeading)
            {
                bool addBreakClass = !isFirstBlock; // 最初のブロックには付けない
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
                // エントリ列の先頭セルに屋号名（太字なし、字下げなし）。
                // colspan で複数カラムをまたいで「役職と並ぶ位置に屋号 1 個」を出す。
                html.Append($"<td class=\"entry-cell\" colspan=\"{cols}\">{Esc(leadingCompanyName!)}</td>");
                html.Append("</tr>");
                isFirstRowOfThisBlock = false; // 屋号行で「最初の行」を消費した扱い
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
                        // 誤記前置を含む HTML を取得。Esc 済みのため html.Append にそのまま流し込む。
                        string labelHtml = await ResolveEntryLabelHtmlAsync(e, ct).ConfigureAwait(false);
                        // leading_company があるブロックの中身は全角SP 1 個分
                        // 字下げする（カラムごとに字下げを付与）。屋号配下であることを視覚化する。
                        if (hasLeading)
                        {
                            html.Append("　"); // 全角SP 1 個分の字下げ
                        }
                        html.Append(labelHtml);
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
    /// VOICE_CAST 役職用の 3 カラムフォールバック描画。
    /// 役職名（左）／キャラ名義（中）／声優名義（右）の 3 カラム表で出力する。
    /// <list type="bullet">
    ///   <item><description><c>col_count</c> は無視して常に 1 行 1 エントリで縦並びにする
    ///     （VOICE_CAST にカラム分けの慣習が無いため）。</description></item>
    ///   <item><description>同役職内（=本メソッド呼び出しの 1 回内）で「直前行と同じキャラ名」のときは
    ///     キャラ名セルを薄く（class=dim、空表示）出して視覚的に省略する。
    ///     ブロック跨ぎでも継続するので、複数ブロックに分かれている VOICE_CAST 内でも自然に動く。</description></item>
    ///   <item><description>CHARACTER_VOICE 以外の種別（PERSON、COMPANY、TEXT 等）が混じっている場合、
    ///     キャラセルは空表示にして声優セル位置に <see cref="ResolveEntryLabelAsync"/> の文字列を
    ///     そのまま出す（書式違いのエントリでも壊れず描画する保険）。</description></item>
    /// </list>
    /// </summary>
    private async Task RenderVoiceCastFallbackAsync(
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        // 直前と同 VOICE_CAST 役職コードが連続した場合 true。役職名カラムを抑止する。
        // カード/Tier/Group 跨ぎで「声の出演」が繰り返し表示されるのを防ぐ用途。
        bool suppressRoleName,
        // VOICE_CAST テーブルの末尾に「協力」行として追記する CASTING_COOPERATION
        // エントリ群。null または空なら追記しない。同一カード内に CASTING_COOPERATION 役職が
        // 存在するとき、呼び出し側で集めて渡す（仕様: 「協力」を太字、その後に全角SP、屋号列を出す）。
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        StringBuilder html,
        CancellationToken ct)
    {
        // 役職名カラムに出す表示用文字列。抑止フラグが立っていれば空。
        // null（カラム自体を出さない）にせず空文字で出すのは、列幅・列数が他カードと揃った
        // 状態を保ち、視覚的な縦の整列が壊れないようにするため。
        string roleNameForFirstRow = suppressRoleName ? "" : roleName;

        if (blocks.Count == 0 || blocks.All(b => b.Entries.Count == 0))
        {
            html.Append($"<table class=\"fallback-vc-table\"><tr><td class=\"role-name\">{Esc(roleNameForFirstRow)}</td><td class=\"character-cell\"></td><td class=\"actor-cell\"><span class=\"empty-credit\">（エントリ未登録）</span></td></tr></table>");
            return;
        }

        html.Append("<table class=\"fallback-vc-table\">");
        bool firstRow = true;
        bool isFirstBlock = true;
        // 直前行のキャラ名（表示用文字列）。空文字とは null を区別する：
        string? prevCharLabel = null;

        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;

            // ブロックの leading_company（グループトップ屋号）を解決する。
            string? leadingCompanyName = null;
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            // ブロック跨ぎの視覚的区切り（VOICE_CAST 表でも同様に block-break クラスで管理）。
            bool isFirstRowOfThisBlock = true;

            // ─ leading_company がある場合の先頭行（屋号名のみ）─
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
                // キャラ列 + 声優列を colspan=2 で結合して屋号名を 1 個だけ出す（太字なし）。
                html.Append($"<td class=\"character-cell\" colspan=\"2\">{Esc(leadingCompanyName!)}</td>");
                html.Append("</tr>");
                isFirstRowOfThisBlock = false;
                // 字下げ視覚化のため、ブロック先頭の prevCharLabel をリセット（前ブロックの dim 連鎖を断つ）。
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
                    string eh = await ResolveEntryLabelHtmlAsync(ent, ct).ConfigureAwait(false);
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

                // 役職名カラム
                //   - suppressRoleName=true: 全行で空（カード跨ぎ抑止）
                //   - suppressRoleName=false: 先頭行のみ役職名、以降は空（同役職内の自然挙動）
                if (firstRow)
                {
                    html.Append($"<td class=\"role-name\">{Esc(roleNameForFirstRow)}</td>");
                    firstRow = false;
                }
                else
                {
                    html.Append("<td class=\"role-name\"></td>");
                }

                // キャラ名カラム ／ 声優名カラム
                if (slot.IsCharVoice)
                {
                    // 直前行と同じキャラ名なら、表示を省略（class=dim で空セル）
                    bool sameAsPrev = prevCharLabel is not null
                        && string.Equals(slot.CharLabel, prevCharLabel, StringComparison.Ordinal);
                    if (sameAsPrev)
                    {
                        html.Append("<td class=\"character-cell dim\"></td>");
                    }
                    else
                    {
                        // leading_company があるブロックの中身は字下げ（全角SP 1 個分）。
                        // dim 表示の場合は空セルなので字下げ不要。
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
                    // VOICE_CAST 役職に CHARACTER_VOICE 以外が混じっている場合の保険描画。
                    // キャラ列は空、声優列に汎用ラベルを出す（誤記前置あり HTML）。
                    html.Append("<td class=\"character-cell\"></td>");
                    // leading_company がある場合は字下げ。
                    string actorPrefix = hasLeading ? "　" : "";
                    html.Append($"<td class=\"actor-cell\">{actorPrefix}{slot.EntryHtml}</td>");
                    // この行で「同じキャラ名 dim 化判定」の連鎖を切るため、prevCharLabel は null に戻す。
                    prevCharLabel = null;
                }

                html.Append("</tr>");
            }
            // 1 ブロック分の出力が終わったタイミングで「最初のブロック」フラグを下ろす。
            // 次のブロックの先頭 <tr> から block-break クラスが付与されるようになる。
            isFirstBlock = false;
        }

        // VOICE_CAST テーブルの末尾に「協力」行を追記する。
        // 同一カード内に CASTING_COOPERATION 役職があり、そこにエントリが含まれる場合、呼び出し側が
        // appendedCooperationEntries を渡してくる。表記は「<strong>協力</strong>　屋号 屋号 …」。
        // テンプレートは使わず、レンダラがハードコードで描画する仕様。
        if (appendedCooperationEntries is not null && appendedCooperationEntries.Count > 0)
        {
            // 屋号/汎用エントリの HTML ラベル（誤記前置あり）を集める。COMPANY/PERSON/TEXT/LOGO 何でも HTML 化する。
            // 空ラベルは除外する。
            var labelHtmls = new List<string>();
            foreach (var e in appendedCooperationEntries)
            {
                string lbl = await ResolveEntryLabelHtmlAsync(e, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lbl)) labelHtmls.Add(lbl);
            }
            if (labelHtmls.Count > 0)
            {
                // 出力形式（SiteBuilder の協力行と同じ 3 セル構成）:
                //   <tr class="cooperation-row">
                //     <td class="role-name"></td>
                //     <td class="character-cell">協力</td>
                //     <td class="actor-cell">屋号1　屋号2 …</td>
                //   </tr>
                //
                // 1 段目（役職名カラム）は空。声の出演ブロックの 2 行目以降と同じ役職名抑止状態。
                // 2 段目（キャラ名カラム）に「協力」を置き、3 段目（声優名カラム）に屋号一覧を置く。
                // 声の出演ブロックでは「○○役」が 2 段目・声優名が 3 段目に並ぶので、協力行も同じ
                // 位置関係に揃えることで表全体を縦に走査したときの認知負荷が下がる。
                // 右寄せ・太字は CSS .cooperation-row td.character-cell が担う（SiteBuilder と同じく
                // 見た目は CSS に寄せ、テキスト自体は素の「協力」とする）。プレビューは UI なので
                // 「協力」も屋号もリンク化せず、屋号はエスケープ済みプレーン文字列を全角SPで連結する。
                // class="cooperation-row" は別ロール扱いの視覚的余白を出すための目印。
                html.Append("<tr class=\"cooperation-row\">");
                html.Append("<td class=\"role-name\"></td>");
                html.Append("<td class=\"character-cell\">協力</td>");
                html.Append("<td class=\"actor-cell\">");
                // 誤記前置を含む HTML を全角SPで連結（既に Esc 済みなので二重エスケープ不要）。
                html.Append(string.Join("　", labelHtmls));
                html.Append("</td>");
                html.Append("</tr>");
            }
        }

        html.Append("</table>");
    }

    /// <summary>CHARACTER_VOICE エントリのキャラ名義表示文字列（プレーンテキスト）を返す。 character_alias_id があればそれを優先し、無ければ raw_character_text を、 それも無ければ "(キャラ未指定)" を返す。 ※ 同一キャラ判定（dim 化）の比較キーとしても使うため、誤記マークアップは含めない。</summary>
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

    /// <summary>CHARACTER_VOICE エントリのキャラ名義を、誤記前置を含む HTML として返す。 キャラ側の <see cref="CreditBlockEntry.CharacterMisprintText"/> があれば 「&lt;del&gt;誤記&lt;/del&gt; 正名義」の形で前置する。</summary>
    private async Task<string> ResolveCharacterLabelHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        string baseLabel = await ResolveCharacterLabelAsync(e, ct).ConfigureAwait(false);
        return PrependMisprintHtml(Esc(baseLabel), e.CharacterMisprintText);
    }

    /// <summary>CHARACTER_VOICE / PERSON エントリの「声優名 + (所属)」を整形してプレーンテキストで返す。 既存 <see cref="ResolveEntryLabelAsync"/> の PERSON 分岐と同じ書式で揃える。</summary>
    private async Task<string> ResolvePersonWithAffiliationAsync(CreditBlockEntry e, CancellationToken ct)
    {
        string name = e.PersonAliasId.HasValue
            ? (await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)"
            : "(名義未指定)";
        // 所属は 3 パターン：両持ち (ID + override テキスト) はテキスト側を表示、ID のみは屋号マスタ名、テキストのみはそのまま。
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

    /// <summary>CHARACTER_VOICE / PERSON エントリの「声優名 + (所属)」を、人物側の誤記前置を含む HTML として返す。
    /// affiliation_inline=false なら所属を <c>&lt;br&gt;</c> で改行して別行表示する。
    /// 所属括弧は <c>.staff-affiliation</c> クラスで 80% 縮小フォント + muted 色。</summary>
    private async Task<string> ResolvePersonWithAffiliationHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        string baseName = e.PersonAliasId.HasValue
            ? (await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)"
            : "(名義未指定)";
        string nameHtml = Esc(baseName);

        string? affilInnerLabel = null;
        if (e.AffiliationCompanyAliasId is int afid)
        {
            affilInnerLabel = !string.IsNullOrEmpty(e.AffiliationText)
                ? e.AffiliationText!
                : (await _lookup.LookupCompanyAliasNameAsync(afid).ConfigureAwait(false)) ?? "";
        }
        else if (!string.IsNullOrWhiteSpace(e.AffiliationText))
        {
            affilInnerLabel = e.AffiliationText!;
        }
        if (!string.IsNullOrEmpty(affilInnerLabel))
        {
            string sep = e.AffiliationInline ? " " : "<br>";
            nameHtml += $"{sep}<span class=\"staff-affiliation\">({Esc(affilInnerLabel)})</span>";
        }
        return PrependMisprintHtml(nameHtml, e.PersonMisprintText);
    }

    /// <summary>1 エントリを HTML（誤記前置あり）に解決する（フォールバック表示用）。 <see cref="ResolveEntryLabelAsync"/> の HTML 版で、エントリ種別ごとに誤記を左側に前置する。
    /// PERSON は <see cref="ResolvePersonWithAffiliationHtmlAsync"/> に委譲して所属クラス付き + インライン/別行レイアウトを尊重する。</summary>
    private async Task<string> ResolveEntryLabelHtmlAsync(CreditBlockEntry e, CancellationToken ct)
    {
        // PERSON は所属を class 付き span でラップする版のヘルパに委譲。誤記前置もそちらが処理する。
        if (e.EntryKind == "PERSON")
        {
            return await ResolvePersonWithAffiliationHtmlAsync(e, ct).ConfigureAwait(false);
        }
        // 種別ごとに該当する誤記列を選び、ベースラベルにエスケープを掛けた上で前置する。
        // TEXT / 未知種別は誤記の概念が無いのでそのままエスケープして返す。
        string baseLabel = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
        string baseHtml = Esc(baseLabel);
        return e.EntryKind switch
        {
            "CHARACTER_VOICE" => baseHtml, // この経路はキャラ・人物両方を含む合成文字列なので個別前置は行わない
            "COMPANY" or "LOGO" => PrependMisprintHtml(baseHtml, e.CompanyMisprintText),
            _ => baseHtml,
        };
    }

    /// <summary>
    /// クレジット時の誤記を「正名義」の左側に「打ち消し線 + 半角SP」で前置した HTML を組み立てる。
    /// SEO 上は <c>&lt;del&gt;</c> で誤記を「削除済みコンテンツ」として明示する。
    /// <paramref name="misprint"/> が null / 空文字なら <paramref name="baseHtml"/> をそのまま返す。
    /// </summary>
    private static string PrependMisprintHtml(string baseHtml, string? misprint)
    {
        if (string.IsNullOrEmpty(misprint)) return baseHtml;
        return $"<del title=\"クレジット時の誤記\">{Esc(misprint)}</del> {baseHtml}";
    }

    /// <summary>1 エントリを表示文字列に解決する（フォールバック表示用）。種別ごとに名義・屋号・ロゴ屋号名・キャラ 名義 + 声優名義・フリーテキストを LookupCache 経由で引いて整形する。 LOGO は CI バージョンラベルを出さず、紐づく屋号名のみを表示する。</summary>
    private async Task<string> ResolveEntryLabelAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                {
                    string name = e.PersonAliasId.HasValue
                        ? (await _lookup.LookupPersonAliasNameAsync(e.PersonAliasId.Value).ConfigureAwait(false)) ?? "(名義不明)"
                        : "(名義未指定)";
                    // 所属は 3 パターン：両持ちはテキスト側を表示、ID のみは屋号マスタ名、テキストのみはそのまま。
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
