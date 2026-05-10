using System.Net;
using System.Text;
// v1.2.1 追加: GetHideStoryboardRoleAsync で CommandDefinition / ExecuteScalarAsync 拡張メソッドを使うため Dapper を参照する。
using Dapper;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.TemplateRendering;
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
          /* 階層余白（v1.2.0 工程 H-11 追加 / v1.2.1 で再調整）──
             カード／Tier／グループ／ロール／ブロックの境界に空き高さを入れて、構造の切れ目を視覚化する。
             margin-top をそれぞれ持たせ、:first-child では 0 にして先頭の余白を抑える。
             v1.2.1 で「グループ内のロール間 = 基準値」を起点に、カード／ティア／グループ → 大きく、
             ブロック → 小さく、と序列が見て分かるよう値を調整した：
                ブロック (block-break) ＜ ロール ＜ グループ ＜ ティア ＜ カード
             */
          .card  { margin-top: 40px; }
          .card:first-child { margin-top: 0; }
          .tier  { margin-top: 24px; }
          .tier:first-child { margin-top: 0; }
          .group { margin-top: 14px; }
          .group:first-child { margin-top: 0; }
          .role  { margin-top: 6px; }   /* グループ内のロール間（基準値） */
          .role:first-child { margin-top: 0; }
          /* v1.2.1 追加: テーブル内のブロック区切り行。td に padding-top を入れて、
             同役職内でブロックが切り替わる箇所に最小の余白を出す（基準のロール間より小さい）。 */
          table.fallback-table tr.block-break > td,
          table.fallback-vc-table tr.block-break > td {
            padding-top: 2px;
          }
          /* v1.2.1 追加: キャスティング協力の追記行（VOICE_CAST テーブル末尾に詰め込む形式）。
             別ロール扱いとしての視覚的余白を出すため、ロール変わり目相当（.role の margin-top）と
             同じ大きさを上に確保する。 */
          table.fallback-vc-table tr.cooperation-row > td {
            padding-top: 6px;
          }
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
          /* v1.2.1 追加：VOICE_CAST 役職用の 3 カラムフォールバック表
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

        // v1.2.1 追加: シリーズの「絵コンテ・演出融合表示」フラグを取得。
        // ON のとき、Group 内に STORYBOARD と EPISODE_DIRECTOR が両方ある場合に
        // 1 つの融合テーブルとして描画する（同名なら「（絵コンテ・）演出 名前」、
        // 異名なら「演出 名前A （絵コンテ）／名前B （演出）」）。
        bool hideStoryboardRole = await GetHideStoryboardRoleAsync(resolveSeriesId, ct).ConfigureAwait(false);

        // v1.2.1 追加: 直前にレンダリングした VOICE_CAST 役職の role_code を覚えておく。
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

            // v1.2.1 追加: カード単位で CASTING_COOPERATION エントリを事前収集する。
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

                    // v1.2.1 追加: 絵コンテ・演出融合判定。
                    //   - シリーズフラグ ON
                    //   - 同 Group 内に STORYBOARD と EPISODE_DIRECTOR が「ちょうど 1 つずつ」存在
                    //   - 各役職配下のエントリ総数が「ちょうど 1 件ずつ」
                    // を満たす場合のみ融合描画。それ以外は通常ループにフォールバックする。
                    HashSet<int> mergedCardRoleIds = new();
                    if (hideStoryboardRole &&
                        TryDetectMergeableStoryboardDirector(cardRoles, r => r.RoleCode,
                            out var sbRole, out var dirRole))
                    {
                        // 各役職のエントリを取得
                        var sbEntries = await CollectEntriesUnderCardRoleAsync(sbRole!.CardRoleId, ct).ConfigureAwait(false);
                        var dirEntries = await CollectEntriesUnderCardRoleAsync(dirRole!.CardRoleId, ct).ConfigureAwait(false);

                        if (sbEntries.Count == 1 && dirEntries.Count == 1)
                        {
                            // 融合描画
                            await RenderStoryboardDirectorMergedAsync(sbEntries, dirEntries, html, ct).ConfigureAwait(false);
                            mergedCardRoleIds.Add(sbRole.CardRoleId);
                            mergedCardRoleIds.Add(dirRole.CardRoleId);
                            // 融合表示は VOICE_CAST 系の役職名抑止 chain には影響させない。
                            //   prevVoiceCastRoleCode は触らない（NORMAL 役職を挟んだのと同じ扱い → null に戻す）
                            prevVoiceCastRoleCode = null;
                        }
                    }

                    // v1.3.0 stage 19: 同 Group 内 sibling 役職の Block/Entry を事前ロードして辞書化。
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

                    foreach (var cr in cardRoles)
                    {
                        // 融合描画で消費済みの cardRole はスキップ。
                        if (mergedCardRoleIds.Contains(cr.CardRoleId)) continue;

                        // v1.2.1 追加: CASTING_COOPERATION 役職本体の描画スキップ判定。
                        // カード内に VOICE_CAST が共存していて、CASTING_COOPERATION エントリが事前収集
                        // されている場合は、本体描画をスキップ（VOICE_CAST テーブル末尾に追記される）。
                        if (cooperationEntriesForCard is not null
                            && cooperationEntriesForCard.Count > 0
                            && string.Equals(cr.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // v1.3.0 stage 19: sibling 辞書から自身の Block を引いて使い回し（重複ロード回避）。
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

                        // v1.2.1 追加: VOICE_CAST 役職名カード跨ぎ省略判定。
                        // 直前ロールが VOICE_CAST かつ同じ role_code なら、役職名表示を抑止する。
                        bool suppressVoiceCastRoleName =
                            !string.IsNullOrEmpty(cr.RoleCode)
                            && prevVoiceCastRoleCode is not null
                            && string.Equals(prevVoiceCastRoleCode, cr.RoleCode, StringComparison.Ordinal)
                            && IsVoiceCastRole(cr.RoleCode, roleMap);

                        // v1.2.1 追加: VOICE_CAST 役職にだけ「協力」行追記情報を渡す。
                        // ただしカード内に VOICE_CAST 役職が複数ある場合、最後の 1 つにだけ追記する
                        // （cooperationAppendTargetCardRoleId == cr.CardRoleId のときのみ）。
                        // それ以外の VOICE_CAST 役職には null を渡し、追記しない。
                        IReadOnlyList<CreditBlockEntry>? appendThisRole =
                            (IsVoiceCastRole(cr.RoleCode, roleMap)
                             && cooperationAppendTargetCardRoleId is int targetId
                             && targetId == cr.CardRoleId)
                                ? cooperationEntriesForCard
                                : null;

                        await RenderCardRoleCommonAsync(credit.ScopeKind, credit.EpisodeId, credit.CreditKind,
                            cr.RoleCode, roleMap, resolveSeriesId, snapshots,
                            suppressVoiceCastRoleName, appendThisRole, siblingResolver, html, ct).ConfigureAwait(false);

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
    /// 「カード内で最後に登場する VOICE_CAST 役職の cardRoleId」をペアで返す（v1.2.1 追加、DB 描画用）。
    /// VOICE_CAST が無いカード、または CASTING_COOPERATION が無いカードでは null を返す。
    /// <para>
    /// 「最後の VOICE_CAST 役職」を返す理由は、同一カード内に VOICE_CAST 役職が複数あるとき
    /// （例: 主役声優 Group と脇役声優 Group が分かれている）、すべての VOICE_CAST テーブル末尾に
    /// 「協力」行が付くと重複表示になるため、最後の 1 つにだけ追記する仕様にしているため。
    /// 「最後の」判定は描画順序と一致させる必要があるので、Tier の TierNo 昇順 → Group の GroupNo 昇順 →
    /// CardRole の OrderInGroup 昇順で走査して、見つかった VOICE_CAST 役職のうち最後のものを採用する。
    /// </para>
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

    /// <summary>
    /// 指定 cardRoleId 配下の全ブロック・全エントリ（is_broadcast_only 除外）を 1 つのフラットリストに集める
    /// （v1.2.1 追加。絵コンテ・演出融合判定用ヘルパ）。
    /// </summary>
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

        // v1.2.1 追加: シリーズの「絵コンテ・演出融合表示」フラグを取得（DB 描画側と同じ仕様）。
        bool hideStoryboardRole = await GetHideStoryboardRoleAsync(resolveSeriesId, ct).ConfigureAwait(false);

        // v1.2.1 追加: 直前 VOICE_CAST 役職コード追跡（DB 描画側と同じ仕様）。
        string? prevVoiceCastRoleCode = null;

        foreach (var dCard in draftCards)
        {
            html.Append("<div class=\"card\">");

            // v1.2.1 追加: カード単位で CASTING_COOPERATION エントリを事前収集（Draft 側、DB 側と同等）。
            // 「最後の VOICE_CAST 役職」の DraftRole 参照と、CASTING_COOPERATION エントリ群をペアで返す。
            // 両方そろっていなければ null（追記処理を発動させない）。
            var draftCooperationContext = CollectDraftCardCastingCooperationContext(dCard, roleMap);
            IReadOnlyList<CreditBlockEntry>? cooperationEntriesForCard = draftCooperationContext?.Entries;
            DraftRole? cooperationAppendTargetRole = draftCooperationContext?.LastVoiceCastRole;

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

                    // v1.2.1 追加: 絵コンテ・演出融合判定（Draft 側）。DB 描画側と同じ仕様：
                    //   - シリーズフラグ ON
                    //   - 同 Group 内に STORYBOARD と EPISODE_DIRECTOR が「ちょうど 1 つずつ」
                    //   - 各役職配下のエントリ総数が「ちょうど 1 件ずつ」
                    // すべて満たすときのみ融合描画。それ以外は通常ループにフォールバック。
                    // 融合済み役職の識別は参照同一性（DraftRole は sealed class なので参照比較で安全）。
                    DraftRole? mergedSb = null;
                    DraftRole? mergedDir = null;
                    if (hideStoryboardRole &&
                        TryDetectMergeableStoryboardDirector(dRoles, r => r.Entity.RoleCode,
                            out var sbDraftRole, out var dirDraftRole))
                    {
                        // Draft の Block/Entry を Entity 単位のフラットリストに集約
                        // （RenderStoryboardDirectorMergedAsync は CreditBlockEntry を受ける契約のため）。
                        List<CreditBlockEntry> sbEntries = sbDraftRole!.Blocks
                            .Where(b => b.State != DraftState.Deleted)
                            .OrderBy(b => b.Entity.BlockSeq)
                            .SelectMany(b => b.Entries
                                .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                                .OrderBy(e => e.Entity.EntrySeq)
                                .Select(e => e.Entity))
                            .ToList();
                        List<CreditBlockEntry> dirEntries = dirDraftRole!.Blocks
                            .Where(b => b.State != DraftState.Deleted)
                            .OrderBy(b => b.Entity.BlockSeq)
                            .SelectMany(b => b.Entries
                                .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                                .OrderBy(e => e.Entity.EntrySeq)
                                .Select(e => e.Entity))
                            .ToList();

                        if (sbEntries.Count == 1 && dirEntries.Count == 1)
                        {
                            await RenderStoryboardDirectorMergedAsync(sbEntries, dirEntries, html, ct).ConfigureAwait(false);
                            mergedSb = sbDraftRole;
                            mergedDir = dirDraftRole;
                            // 融合表示は VOICE_CAST 系の chain を切る。
                            prevVoiceCastRoleCode = null;
                        }
                    }

                    // v1.3.0 stage 19: Draft 側 sibling 役職辞書を作って {ROLE:CODE.PLACEHOLDER} 構文に備える。
                    // 各 DraftRole の Block/Entry を BlockSnapshot[] に詰めて role_code 単位で辞書化する。
                    // Draft 側は DB アクセス無しでメモリ上の DraftRole を走査するだけなので低コスト。
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

                    foreach (var dRole in dRoles)
                    {
                        // 融合済み役職はスキップ（参照同一性で判定）。
                        if (ReferenceEquals(dRole, mergedSb) || ReferenceEquals(dRole, mergedDir)) continue;

                        // v1.2.1 追加: CASTING_COOPERATION 役職本体の描画スキップ判定（Draft 側）。
                        if (cooperationEntriesForCard is not null
                            && cooperationEntriesForCard.Count > 0
                            && string.Equals(dRole.Entity.RoleCode, RoleCodeCastingCooperation, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // v1.3.0 stage 19: 自身の Block を sibling 辞書から流用（重複構築を回避）。
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

                        // v1.2.1 追加: VOICE_CAST 役職名カード跨ぎ省略判定（Draft 側）。
                        bool suppressVoiceCastRoleName =
                            !string.IsNullOrEmpty(dRole.Entity.RoleCode)
                            && prevVoiceCastRoleCode is not null
                            && string.Equals(prevVoiceCastRoleCode, dRole.Entity.RoleCode, StringComparison.Ordinal)
                            && IsVoiceCastRole(dRole.Entity.RoleCode, roleMap);

                        // v1.2.1 追加: VOICE_CAST 役職にだけ「協力」行追記情報を渡す（Draft 側）。
                        // ただしカード内に VOICE_CAST 役職が複数ある場合、最後の 1 つにだけ追記する
                        // （cooperationAppendTargetRole 参照と一致するときのみ）。
                        IReadOnlyList<CreditBlockEntry>? appendThisRole =
                            (IsVoiceCastRole(dRole.Entity.RoleCode, roleMap)
                             && cooperationAppendTargetRole is not null
                             && ReferenceEquals(dRole, cooperationAppendTargetRole))
                                ? cooperationEntriesForCard
                                : null;

                        await RenderCardRoleCommonAsync(credit.ScopeKind, credit.EpisodeId, credit.CreditKind,
                            dRole.Entity.RoleCode, roleMap, resolveSeriesId, snapshots,
                            suppressVoiceCastRoleName, appendThisRole, siblingResolver, html, ct).ConfigureAwait(false);

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
    /// Draft セッション上の指定カード内で「VOICE_CAST 役職」と「CASTING_COOPERATION 役職」が両方
    /// 存在するかを判定し、両方ある場合のみ CASTING_COOPERATION 役職配下の全エントリ
    /// （複数ロール・複数ブロック横断）と「カード内で最後に登場する VOICE_CAST 役職の DraftRole 参照」
    /// をペアで返す（v1.2.1 追加、Draft 描画用）。
    /// 描画順は Tier の TierNo 昇順 → Group の GroupNo 昇順 → Role の OrderInGroup 昇順。
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
        // v1.2.1 追加: 直前ロールが同じ VOICE_CAST 役職だった場合の役職名抑止フラグ。
        // フォールバック描画ルートでのみ尊重される。テンプレ展開ルートでは無視（テンプレ作者が
        // 制御する想定。{ROLE_NAME} 自動ラップでも、抑止指示はせず素直に役職名を出す）。
        bool suppressVoiceCastRoleName,
        // v1.2.1 追加: VOICE_CAST テーブル末尾に「協力」行として追記する CASTING_COOPERATION
        // エントリ群。呼び出し側で同一カード内の CASTING_COOPERATION 役職のエントリを集めて渡す。
        // フォールバックの VOICE_CAST 描画ルートでのみ尊重される。
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        // v1.3.0 stage 19: 同 Group 内 sibling 役職の Block を引くコールバック。
        // テンプレ DSL の {ROLE:CODE.PLACEHOLDER} 構文用。null の場合は ROLE 参照が空文字に展開される。
        Func<string, IReadOnlyList<BlockSnapshot>?>? siblingRoleResolver,
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
                // v1.3.0 ブラッシュアップ stage 16 Phase 4：
                // roles.hide_role_name_in_credit=1 の役職は HTML クレジット階層上で
                // 左カラム（役職名セル）を空文字にして「役職名を出さない」表示にする。
                // 関与集計や役職別ランキングは role_code ベースで動くので、本上書きの影響は受けない。
                if (r.HideRoleNameInCredit == 1) roleName = "";
            }
            else
            {
                roleName = roleCode!;
            }
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
                // v1.3.0 stage 19: sibling-role 解決のコールバックを渡して {ROLE:CODE.PLACEHOLDER} 構文に対応。
                var ctx = new TemplateContext(roleCode ?? "", roleName, blocks, scopeKind, episodeId, creditKind,
                    siblingRoleResolver: siblingRoleResolver,
                    visitedRoleCodes: null);
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
                // v1.2.1: VOICE_CAST 役職は専用の 3 カラム表示にフォールバックする。
                //         直前と同 VOICE_CAST 役職なら役職名カラムも抑止する。
                //         同一カード内に CASTING_COOPERATION があれば末尾に「協力」行を追記する。
                await RenderRoleFallbackDispatchAsync(roleCode, roleName, blocks, roleMap,
                    suppressVoiceCastRoleName, appendedCooperationEntries, html, ct).ConfigureAwait(false);
                html.Append("</div>");
            }
        }
        else
        {
            // ── テンプレ未定義時のフォールバック表 ──
            // v1.2.1: VOICE_CAST 役職は専用の 3 カラム表示にフォールバックする。
            //         直前と同 VOICE_CAST 役職なら役職名カラムも抑止する。
            //         同一カード内に CASTING_COOPERATION があれば末尾に「協力」行を追記する。
            await RenderRoleFallbackDispatchAsync(roleCode, roleName, blocks, roleMap,
                suppressVoiceCastRoleName, appendedCooperationEntries, html, ct).ConfigureAwait(false);
        }

        html.Append("</div>"); // .role
    }

    /// <summary>
    /// フォールバック描画の振り分け（v1.2.1 追加）。
    /// 役職の <c>role_format_kind</c> が <c>VOICE_CAST</c> なら 3 カラム表
    /// （役職名 | キャラ名義 | 声優名義）にフォールバックし、それ以外は従来の
    /// <see cref="RenderRoleFallbackAsync"/>（役職名 | エントリ群を col_count カラム）に流す。
    /// </summary>
    private async Task RenderRoleFallbackDispatchAsync(
        string? roleCode, string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        IReadOnlyDictionary<string, Role> roleMap,
        // v1.2.1 追加: VOICE_CAST 役職名抑止フラグ。VOICE_CAST 以外では使われない。
        bool suppressVoiceCastRoleName,
        // v1.2.1 追加: VOICE_CAST テーブルの末尾に「協力」行として追記するエントリ群。
        // 同一カード内に CASTING_COOPERATION 役職がある場合、呼び出し側で集めて渡す。
        // null または空のときは追記しない（通常の VOICE_CAST 描画）。
        IReadOnlyList<CreditBlockEntry>? appendedCooperationEntries,
        StringBuilder html, CancellationToken ct)
    {
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

    /// <summary>
    /// 指定 role_code が VOICE_CAST 役職かどうかを判定する（v1.2.1 追加）。
    /// マスタに無い役職や null の場合は false。
    /// </summary>
    private static bool IsVoiceCastRole(string? roleCode, IReadOnlyDictionary<string, Role> roleMap)
    {
        if (string.IsNullOrEmpty(roleCode)) return false;
        if (!roleMap.TryGetValue(roleCode, out var r)) return false;
        return string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定 series_id の <c>hide_storyboard_role</c> フラグを軽量 SQL で取得する（v1.2.1 追加）。
    /// <para>
    /// SeriesRepository を依存に追加するとコンストラクタの引数増加で呼び出し側まで波及するため、
    /// レンダラ内では <c>_factory</c> を直接使って単一値を取りに行く方針。値はクエリ単位の
    /// 計算量が小さく、キャッシュは導入していない（クレジット 1 件あたり 1 SELECT で十分軽量）。
    /// </para>
    /// <para>
    /// series_id が null（クレジットがどのシリーズに属するか解決できない）の場合は false を返す
    /// 安全側の動作とする。row が見つからない場合も同様に false（融合表示しない＝従来挙動）。
    /// </para>
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

    /// <summary>絵コンテ役職コード（v1.2.1 シードで投入される）。</summary>
    private const string RoleCodeStoryboard = "STORYBOARD";

    /// <summary>演出役職コード（v1.2.1 シードで投入される）。</summary>
    private const string RoleCodeEpisodeDirector = "EPISODE_DIRECTOR";

    /// <summary>キャスティング協力役職コード（v1.2.1 追加。マスタにはシードしないが、運用者が手動追加することを前提に専用処理を行う）。</summary>
    private const string RoleCodeCastingCooperation = "CASTING_COOPERATION";

    /// <summary>
    /// 融合可能な「絵コンテ・演出」ペアを 1 つのテーブルに描画する（v1.2.1 追加）。
    /// <para>
    /// 仕様（hide_storyboard_role が ON のシリーズで起動）:
    /// <list type="bullet">
    ///   <item><description>同名（絵コンテと演出が同じ person_alias_id、または同じ raw_text）→
    ///     役職名 = 「（絵コンテ・）演出」、エントリ = その人物名 1 行</description></item>
    ///   <item><description>異名 → 役職名 = 「演出」、エントリ = 「名前A （絵コンテ）」「名前B （演出）」の 2 行</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// 各役職のエントリ数がともに 1 件である場合に限り融合する。複数エントリの場合（共同絵コンテ、共同演出など）の
    /// 仕様は未定のため、本メソッドの呼び出し側で抑止し、通常描画にフォールバックする。
    /// </para>
    /// </summary>
    /// <param name="storyboardEntries">STORYBOARD 役職配下の全エントリ（複数ブロック横断のフラットリスト）。</param>
    /// <param name="directorEntries">EPISODE_DIRECTOR 役職配下の全エントリ。</param>
    /// <param name="html">出力 StringBuilder。</param>
    /// <param name="ct">キャンセルトークン。</param>
    private async Task RenderStoryboardDirectorMergedAsync(
        IReadOnlyList<CreditBlockEntry> storyboardEntries,
        IReadOnlyList<CreditBlockEntry> directorEntries,
        StringBuilder html,
        CancellationToken ct)
    {
        // 安全側ガード: ここに来ている時点で呼び出し側が「両方 1 件」を保証している前提。
        // 万一 0 件のものが含まれていたら通常描画に回したい行為なので、何もせず return。
        if (storyboardEntries.Count != 1 || directorEntries.Count != 1) return;

        var sb = storyboardEntries[0];
        var dr = directorEntries[0];

        // 同名判定: 1) person_alias_id が両方有効かつ等しい
        //          2) どちらかが NULL でも raw_text 同士が完全一致する
        // どちらでも該当しない場合は異名とみなす。
        bool sameName = false;
        if (sb.PersonAliasId.HasValue && dr.PersonAliasId.HasValue)
        {
            sameName = sb.PersonAliasId.Value == dr.PersonAliasId.Value;
        }
        else
        {
            // raw_text 比較（どちらかが NULL でも文字列が一致するならよし）。
            string sbText = await ResolvePersonWithAffiliationAsync(sb, ct).ConfigureAwait(false);
            string drText = await ResolvePersonWithAffiliationAsync(dr, ct).ConfigureAwait(false);
            sameName = string.Equals(sbText, drText, StringComparison.Ordinal);
        }

        // 表示用の「演出」エントリ表記（person_alias 解決 → 名前 + 所属）。
        string directorLabel = await ResolvePersonWithAffiliationAsync(dr, ct).ConfigureAwait(false);

        html.Append("<div class=\"role\">");
        html.Append("<table class=\"fallback-table\"><tr>");
        if (sameName)
        {
            // 同名: 役職名「（絵コンテ・）演出」、エントリ = 1 行のみ
            html.Append($"<td class=\"role-name\">{Esc("（絵コンテ・）演出")}</td>");
            html.Append("<td class=\"entry-cell\">");
            html.Append(Esc(directorLabel));
            html.Append("</td>");
        }
        else
        {
            // 異名: 役職名「演出」、エントリ = 2 行（絵コンテ → 演出 の順）
            string storyboardLabel = await ResolvePersonWithAffiliationAsync(sb, ct).ConfigureAwait(false);
            html.Append($"<td class=\"role-name\">{Esc("演出")}</td>");
            html.Append("<td class=\"entry-cell\">");
            html.Append($"{Esc(storyboardLabel)} {Esc("（絵コンテ）")}<br>");
            html.Append($"{Esc(directorLabel)} {Esc("（演出）")}");
            html.Append("</td>");
        }
        html.Append("</tr></table>");
        html.Append("</div>"); // .role
    }

    /// <summary>
    /// 「絵コンテ・演出融合」の対象になる Group 内の cardRoles から、ストーリーボード／演出役職を
    /// 抽出し、両者が「融合可能」かを判定するヘルパ（v1.2.1 追加）。
    /// 融合可能な場合のみ <paramref name="storyboardRoleCode"/> / <paramref name="directorRoleCode"/> が
    /// 設定された状態で true を返す。それ以外は false（呼び出し側は通常ループに委ねる）。
    /// </summary>
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
        bool isFirstBlock = true;
        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;
            // col_count に応じてエントリを行に分割。ColCount は byte 型のため Math.Max(int,int) と
            // Math.Max(byte,byte) の曖昧解決を避けるため明示的に int キャストする。
            int cols = Math.Max(1, (int)bs.Block.ColCount);

            // v1.2.1 追加: ブロックの leading_company（グループトップ屋号）を解決する。
            // 値があれば、ブロック先頭行のエントリ列にその屋号名を太字なしで出し、
            // 後続のエントリ行は字下げ（全角SP 1 個分）してから本来のエントリを表示する。
            // この字下げはブロック内の中身が「トップ屋号配下である」ことを視覚的に示すためのもの。
            string? leadingCompanyName = null;
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            // v1.2.1 追加: ブロック跨ぎの視覚的区切り。ブロックが切り替わる先頭の <tr> に
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
                        string label = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
                        // v1.2.1 追加: leading_company があるブロックの中身は全角SP 1 個分
                        // 字下げする（カラムごとに字下げを付与）。屋号配下であることを視覚化する。
                        if (hasLeading)
                        {
                            html.Append("　"); // 全角SP 1 個分の字下げ
                        }
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
    /// VOICE_CAST 役職用の 3 カラムフォールバック描画（v1.2.1 追加）。
    /// <para>
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
    /// </para>
    /// </summary>
    private async Task RenderVoiceCastFallbackAsync(
        string roleName,
        IReadOnlyList<BlockSnapshot> blocks,
        // v1.2.1 追加: 直前と同 VOICE_CAST 役職コードが連続した場合 true。役職名カラムを抑止する。
        // カード/Tier/Group 跨ぎで「声の出演」が繰り返し表示されるのを防ぐ用途。
        bool suppressRoleName,
        // v1.2.1 追加: VOICE_CAST テーブルの末尾に「協力」行として追記する CASTING_COOPERATION
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
        //   null = まだ 1 行も出していない、または比較対象なし
        //   "" = 直前行が空キャラ表示で、それと同じ「空」表示が続いた状態。
        string? prevCharLabel = null;

        foreach (var bs in blocks)
        {
            if (bs.Entries.Count == 0) continue;

            // v1.2.1 追加: ブロックの leading_company（グループトップ屋号）を解決する。
            // VOICE_CAST 役職に leading_company が設定されることは稀だが、入力可能な以上は対応する。
            // 役職名カラム（左）以外を colspan=2 で結合して屋号名を出し、後続行は字下げする。
            string? leadingCompanyName = null;
            if (bs.Block.LeadingCompanyAliasId is int leadId)
            {
                leadingCompanyName = await _lookup.LookupCompanyAliasNameAsync(leadId).ConfigureAwait(false);
            }
            bool hasLeading = !string.IsNullOrEmpty(leadingCompanyName);

            // v1.2.1 追加: ブロック跨ぎの視覚的区切り（VOICE_CAST 表でも同様に block-break クラスで管理）。
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

            foreach (var e in bs.Entries)
            {
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
                if (e.EntryKind == "CHARACTER_VOICE")
                {
                    string charLabel = await ResolveCharacterLabelAsync(e, ct).ConfigureAwait(false);
                    string actorLabel = await ResolvePersonWithAffiliationAsync(e, ct).ConfigureAwait(false);

                    // 直前行と同じキャラ名なら、表示を省略（class=dim で空セル）
                    bool sameAsPrev = prevCharLabel is not null
                        && string.Equals(charLabel, prevCharLabel, StringComparison.Ordinal);
                    if (sameAsPrev)
                    {
                        html.Append("<td class=\"character-cell dim\"></td>");
                    }
                    else
                    {
                        // v1.2.1 追加: leading_company があるブロックの中身は字下げ（全角SP 1 個分）。
                        // dim 表示の場合は空セルなので字下げ不要。
                        string charPrefix = hasLeading ? "　" : "";
                        html.Append($"<td class=\"character-cell\">{charPrefix}{Esc(charLabel)}</td>");
                    }
                    html.Append($"<td class=\"actor-cell\">{Esc(actorLabel)}</td>");

                    prevCharLabel = charLabel;
                }
                else
                {
                    // VOICE_CAST 役職に CHARACTER_VOICE 以外が混じっている場合の保険描画。
                    // キャラ列は空、声優列に汎用ラベルを出す。
                    string label = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
                    html.Append("<td class=\"character-cell\"></td>");
                    // v1.2.1 追加: leading_company がある場合は字下げ。
                    string actorPrefix = hasLeading ? "　" : "";
                    html.Append($"<td class=\"actor-cell\">{actorPrefix}{Esc(label)}</td>");
                    // この行で「同じキャラ名 dim 化判定」の連鎖を切るため、prevCharLabel は null に戻す。
                    prevCharLabel = null;
                }

                html.Append("</tr>");
            }
            // v1.2.1 追加: 1 ブロック分の出力が終わったタイミングで「最初のブロック」フラグを下ろす。
            // 次のブロックの先頭 <tr> から block-break クラスが付与されるようになる。
            isFirstBlock = false;
        }

        // v1.2.1 追加: VOICE_CAST テーブルの末尾に「協力」行を追記する。
        // 同一カード内に CASTING_COOPERATION 役職があり、そこにエントリが含まれる場合、呼び出し側が
        // appendedCooperationEntries を渡してくる。表記は「<strong>協力</strong>　屋号 屋号 …」。
        // テンプレートは使わず、レンダラがハードコードで描画する仕様。
        if (appendedCooperationEntries is not null && appendedCooperationEntries.Count > 0)
        {
            // 屋号/汎用エントリの文字列ラベルを集める。COMPANY/PERSON/TEXT/LOGO 何でも文字列化する
            // ResolveEntryLabelAsync を使う。空ラベルは除外する。
            var labels = new List<string>();
            foreach (var e in appendedCooperationEntries)
            {
                string lbl = await ResolveEntryLabelAsync(e, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lbl)) labels.Add(lbl);
            }
            if (labels.Count > 0)
            {
                // 出力形式（v1.2.1 ユーザー要望調整版）:
                //   <tr class="cooperation-row"><td class="role-name"></td>
                //       <td class="character-cell" colspan="2"><strong>協力</strong>　屋号1　屋号2 …</td></tr>
                //
                // 役職名カラムは空（直前の VOICE_CAST 役職名「声の出演」の表示位置と揃える＝抑止と同じ扱い）。
                // 「協力」テキストはキャラ列の位置から始め、声優列まで colspan=2 で結合してその中に
                // 屋号列を全角SPで連結して出す。これにより：
                //   雪城さなえ        野沢 雅子
                //   協力 東映アカデミー
                // のように、キャラ列の左端から「協力」が始まり、屋号は同じセル内に全角SP区切りで続く。
                // ※ 「協力」と屋号は同じセル内なのでカラム位置でズレない。
                // class="cooperation-row" は別ロール扱いの視覚的余白（CSS の .role 相当）を出すため。
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

    /// <summary>
    /// CHARACTER_VOICE エントリのキャラ名義表示文字列を返す（v1.2.1 追加）。
    /// character_alias_id があればそれを優先し、無ければ raw_character_text を、
    /// それも無ければ "(キャラ未指定)" を返す。
    /// </summary>
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
    /// CHARACTER_VOICE / PERSON エントリの「声優名 + (所属)」を整形して返す（v1.2.1 追加）。
    /// 既存 <see cref="ResolveEntryLabelAsync"/> の PERSON 分岐と同じ書式で揃える。
    /// </summary>
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