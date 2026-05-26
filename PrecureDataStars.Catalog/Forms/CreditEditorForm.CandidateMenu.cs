using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dapper;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// CreditEditorForm の右クリック候補メニュー（テキストエリア内・役職コンテキスト連動）。
/// 入力の手間を減らし、過去のクレジットパターンの再利用率を上げるための入力補助機構。
///
/// 動作概要：
///   1. <see cref="txtBulkText"/> 上で右クリックされると <see cref="OnBulkTextMouseDown"/> が発火し、
///      クリック位置の文字インデックス → 行番号を計算する。
///   2. その行から上方向に walk して直近の「役職ヘッダ行」（<c>役職名:</c> 形式）を特定する。
///   3. 役職表示名を <see cref="RolesRepository"/> 全件から逆引きして role_code に変換し、
///      <see cref="RoleSuccessorResolver"/> 同等のロジックでクラスタ全メンバーを得る。
///   4. <see cref="RoleAliasUsageRepository"/> でクラスタに過去出現した PERSON / COMPANY 名義の
///      使用履歴サンプル（直近 730 日）を取得する。
///   5. 役職スコープ内に既に入力済みの名義を抽出して候補から除外する。
///   6. 各候補 alias を以下のスコア式で並べ替え、上位 K 件を ContextMenuStrip に並べる：
///        Score(a) = Σ exp(-(t_now - t_i)/84) × series_boost(s_i) × exp(-|p_i - p_target| / 1.5)
///      ・ 指数減衰: τ = 84 日（だいたい四半期）
///      ・ series_boost: 現シリーズ ×1.5、他シリーズ ×1.0
///      ・ position_match: p_target = 役職スコープ内の既存同種エントリ数 + 1
///   7. クリックされた候補は「名前#alias_id」（COMPANY は <c>[名前#alias_id]</c>）形式で
///      クリック行末尾に挿入する。区切りは「、」（既に行末が空でなければ）。
///
/// 役職系譜（role_successions）は無向グラフの連結成分として扱う。SiteBuilder 側の
/// <see cref="PrecureDataStars.SiteBuilder.Utilities.RoleSuccessorResolver"/> と同等の Union-Find
/// ロジックを Catalog 内ローカルで再構築するのは重いので、ここでは「指定 role_code を起点に
/// 隣接 from/to を BFS 展開」する簡易版で済ませる（深さは 3 段あれば実用上 OK、念のため上限 10）。
/// </summary>
public partial class CreditEditorForm
{
    /// <summary>役職ヘッダ行の判定用正規表現。<see cref="Dialogs.CreditBulkInputParser"/> と同一仕様：
    /// 行末コロン（半角 ":" / 全角 "："）、ただし行頭 '[' で始まる場合は除外（屋号エントリ優先）。</summary>
    private static readonly Regex RoleHeaderLineRegex = new(@"^(?<name>.+?)[：:]\s*$", RegexOptions.Compiled);

    /// <summary>カード／ティア／グループ区切り行の判定用正規表現。
    /// <c>----</c> = カード、 <c>---</c> = ティア、 <c>--</c> = グループ。
    /// 役職スコープを「次の役職ヘッダ or 区切り」までと定義するために使う。</summary>
    private static readonly Regex SeparatorLineRegex = new(@"^-{2,}\s*$", RegexOptions.Compiled);

    /// <summary>テキスト内の <c>名前#数字</c> 形式（PERSON エントリの alias_id 明示）を拾うための正規表現。
    /// 行末ノーツ <c>// xxx</c> や所属 <c>(xxx)</c> の手前まで読み取る。</summary>
    private static readonly Regex PersonAliasIdRegex = new(@"#(\d+)", RegexOptions.Compiled);

    /// <summary>テキスト内の COMPANY エントリ <c>[屋号#数字]</c> を拾うための正規表現。
    /// <c>[屋号#数字#CIラベル]</c>（LOGO）は別扱いで、ここでは「#数字]」または「#数字#」の
    /// どちらでもマッチする。</summary>
    private static readonly Regex CompanyAliasIdRegex = new(@"\[[^\[\]#]+#(\d+)(?:#|\])", RegexOptions.Compiled);

    /// <summary>候補メニューの最大件数（PERSON / COMPANY セクションごと）。</summary>
    private const int MaxCandidatesPerSection = 20;

    /// <summary>使用履歴の参照期間（過去日数）。</summary>
    private const int LookbackDays = 730;

    /// <summary>指数減衰スコアの時定数 τ（日数）。84 日でだいたい四半期。
    /// 30 日前: exp(-30/84) ≈ 0.70 / 84 日前: 0.37 / 365 日前: 0.013。</summary>
    private const double DecayTauDays = 84.0;

    /// <summary>現シリーズで使われた場合のスコア乗算ブースト。</summary>
    private const double SeriesBoost = 1.5;

    /// <summary>ブロック内位置一致スコアの広がり（| Δposition | / spread を exp 内の指数に使う）。
    /// spread=1.5 のとき：差 0 → ×1.00、差 1 → ×0.51、差 2 → ×0.26、差 3 → ×0.14。</summary>
    private const double PositionSpread = 1.5;

    /// <summary>役職表示名 → role_code 解決用のキャッシュ。
    /// 1 つのフォームインスタンスで何度も使うので最初の右クリックで全件ロードして以降は使い回す。</summary>
    private Dictionary<string, string>? _roleCodeByDisplayName;

    /// <summary>role_code → ロールマスタ全件のキャッシュ（クラスタ解決用）。</summary>
    private IReadOnlyList<Role>? _allRolesCache;

    /// <summary>role_successions 全件キャッシュ（クラスタ解決用）。</summary>
    private IReadOnlyList<RoleSuccession>? _allRoleSuccessionsCache;

    /// <summary>txtBulkText に割り当てた単一の ContextMenuStrip インスタンス。
    /// Opening 内で items を async に詰め替えてから <see cref="ContextMenuStrip.Show(System.Windows.Forms.Control, System.Drawing.Point)"/>
    /// で再表示するというパターンで動かす（毎回 new した別インスタンスを Show する方式は
    /// 元の Opening dispatch との競合で「表示されない / すぐ閉じる」事故が起きやすいため避ける）。</summary>
    private ContextMenuStrip? _candidateMenu;

    /// <summary>「次の Opening は async 構築済み items を見せる目的なのでキャンセルせず通せ」フラグ。
    /// async 構築が終わった瞬間に true → Show() → 再 Opening で「通過」→ 表示確定 → false に戻す。</summary>
    private bool _candidateMenuReady;

    /// <summary>右クリック候補メニューのトリガを初期化する。
    /// 単一の ContextMenuStrip を <see cref="txtBulkText"/> に割り当てて Windows ネイティブの
    /// 右クリックメニュー（切り取り/コピー/貼り付け 等）を完全に抑止する。
    /// Opening イベントで「items 構築 → 再 Show」の 2 段階で表示するため、初回 Opening は
    /// キャンセル、構築完了後の再 Opening は通過、というフラグ駆動の流れで動かす。</summary>
    private void InitializeCandidateMenuTrigger()
    {
        _candidateMenu = new ContextMenuStrip();
        _candidateMenu.Opening += OnCandidateMenuOpening;
        txtBulkText.ContextMenuStrip = _candidateMenu;
    }

    /// <summary>ContextMenuStrip の Opening ハンドラ。2 段階で動く：
    ///  1 段目（<see cref="_candidateMenuReady"/> = false）：キャンセル → クリック位置取得 →
    ///     async で items を <see cref="_candidateMenu"/>.Items に詰める → ready = true → 再 Show
    ///  2 段目（<see cref="_candidateMenuReady"/> = true）：通過させて表示確定 → false に戻す
    /// この 2 段階パターンにより「Opening dispatch 中に別 ContextMenuStrip を Show すると
    /// メニューシステムが過渡状態にあって表示されない / すぐ閉じる」現象を回避する。</summary>
    private async void OnCandidateMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_candidateMenu is null || txtBulkText is null) return;

        if (_candidateMenuReady)
        {
            // 2 段目：構築済み items を含むメニューを表示確定させる。
            _candidateMenuReady = false;
            return;
        }

        // 1 段目：キャンセルして async 構築に入る。
        e.Cancel = true;

        Point clientPos;
        int clickedLine;
        try
        {
            clientPos = txtBulkText.PointToClient(Cursor.Position);
            int charIndex = txtBulkText.GetCharIndexFromPosition(clientPos);
            clickedLine = txtBulkText.GetLineFromCharIndex(charIndex);
        }
        catch
        {
            return;
        }

        try
        {
            await PopulateCandidateMenuAsync(clickedLine).ConfigureAwait(true);
            _candidateMenuReady = true;
            // BeginInvoke で次のメッセージループサイクルに繰り越して Show を発行する。
            // Opening dispatch のスタックが解けた後に Show すれば、再 Opening も独立した
            // メッセージとして処理されて確実に表示確定する。Opening 内で直接 Show を呼ぶと
            // 親側の WM_CONTEXTMENU 処理と過渡的に競合して「表示されない / すぐ閉じる」事故が
            // 起きやすいため避ける。
            BeginInvoke(new Action(() =>
            {
                try { _candidateMenu.Show(txtBulkText, clientPos); }
                catch { _candidateMenuReady = false; }
            }));
        }
        catch (Exception ex)
        {
            _candidateMenuReady = false;
            MessageBox.Show(
                $"候補メニューの取得に失敗しました:\n{ex.Message}",
                "候補メニュー",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>クリックされた行を起点に候補を集計し、<see cref="_candidateMenu"/>.Items を詰め替える。
    /// 役職スコープが特定できない場合は「候補なし（理由）」の 1 行だけを入れる。
    /// メニューの実表示は呼び出し側（<see cref="OnCandidateMenuOpening"/>）が ready フラグを立てて Show する。</summary>
    private async Task PopulateCandidateMenuAsync(int clickedLine)
    {
        if (_candidateMenu is null) return;
        _candidateMenu.Items.Clear();

        var lines = txtBulkText.Lines;
        if (clickedLine < 0 || clickedLine >= lines.Length)
        {
            _candidateMenu.Items.Add(new ToolStripMenuItem("候補なし（行外）") { Enabled = false });
            return;
        }

        // クリック行から上方向に walk して、直近の「役職ヘッダ行」を探す。
        // 途中に区切り行（----/---/--）があれば、その上は別の役職スコープなので打ち切る。
        int roleHeaderLine = -1;
        for (int i = clickedLine; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (SeparatorLineRegex.IsMatch(trimmed))
            {
                break;
            }
            var m = RoleHeaderLineRegex.Match(trimmed);
            if (m.Success && !trimmed.StartsWith('['))
            {
                roleHeaderLine = i;
                break;
            }
        }
        if (roleHeaderLine < 0)
        {
            _candidateMenu.Items.Add(new ToolStripMenuItem("候補なし（役職スコープ外）") { Enabled = false });
            return;
        }

        var headerMatch = RoleHeaderLineRegex.Match(lines[roleHeaderLine].Trim());
        string displayName = headerMatch.Groups["name"].Value.Trim();

        // 役職表示名 → role_code 解決。マスタにない（未登録の自由記述役職）は候補集計不能。
        if (_roleCodeByDisplayName is null)
        {
            _allRolesCache = await _rolesRepo.GetAllAsync().ConfigureAwait(true);
            _roleCodeByDisplayName = _allRolesCache
                .Where(r => !string.IsNullOrEmpty(r.NameJa))
                .GroupBy(r => r.NameJa!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().RoleCode, StringComparer.Ordinal);
        }
        if (!_roleCodeByDisplayName.TryGetValue(displayName, out var roleCode))
        {
            _candidateMenu.Items.Add(new ToolStripMenuItem($"候補なし（役職未登録: {displayName}）") { Enabled = false });
            return;
        }

        // 役職クラスタ（連結成分）を解決。
        if (_allRoleSuccessionsCache is null)
        {
            _allRoleSuccessionsCache = await _roleSuccessionsRepo.GetAllAsync().ConfigureAwait(true);
        }
        var clusterCodes = ResolveRoleCluster(roleCode, _allRoleSuccessionsCache);

        // 役職スコープ全体のテキスト範囲（クリック行から見て上下に walk）
        var scope = DetermineRoleScope(lines, roleHeaderLine);

        // スコープ内の既存エントリ alias_id（除外集合）と既存エントリ件数（p_target 計算用）。
        var (personIdsInScope, companyIdsInScope, personNamesInScope, companyNamesInScope, entryCountInScope) =
            ExtractScopeEntries(lines, scope.startLine, scope.endLine);

        int pTarget = entryCountInScope + 1;

        // 使用履歴取得のアンカー日付：編集中クレジットの放送日（EPISODE スコープ）
        // または シリーズ開始日（SERIES スコープ）を採用する。これにより
        // 「初代プリキュアを編集してるときは初代周辺のスタッフだけが候補に出る」状態になる。
        int? currentSeriesId = (cboSeries.SelectedValue is int sid) ? sid : (int?)null;
        var anchorDate = await ResolveAnchorDateAsync(currentSeriesId).ConfigureAwait(true);
        if (anchorDate is null)
        {
            _candidateMenu.Items.Add(new ToolStripMenuItem("候補なし（編集中クレジットの基準日が解決できません）") { Enabled = false });
            return;
        }

        var personUsages = await _roleAliasUsageRepo
            .GetRecentPersonAliasUsagesAsync(clusterCodes, anchorDate.Value, LookbackDays)
            .ConfigureAwait(true);
        var companyUsages = await _roleAliasUsageRepo
            .GetRecentCompanyAliasUsagesAsync(clusterCodes, anchorDate.Value, LookbackDays)
            .ConfigureAwait(true);

        var personRanked = RankUsages(personUsages, anchorDate.Value, currentSeriesId, pTarget,
            excludeIds: personIdsInScope, excludeNormalizedNames: personNamesInScope);
        var companyRanked = RankUsages(companyUsages, anchorDate.Value, currentSeriesId, pTarget,
            excludeIds: companyIdsInScope, excludeNormalizedNames: companyNamesInScope);

        // _candidateMenu.Items を直接組み立てる。
        FillCandidateMenuItems(_candidateMenu, personRanked, companyRanked, clickedLine, displayName);
    }

    /// <summary>役職スコープ（クリック行が属する役職の上下境界）を返す。
    /// startLine = 役職ヘッダの 1 行下、endLine = 次の役職ヘッダ or 区切り or テキスト末端の直前。</summary>
    private static (int startLine, int endLine) DetermineRoleScope(string[] lines, int roleHeaderLine)
    {
        int start = roleHeaderLine + 1;
        int end = lines.Length - 1;
        for (int i = start; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (SeparatorLineRegex.IsMatch(trimmed))
            {
                end = i - 1;
                break;
            }
            // 役職ヘッダ（'[' で始まらない）に当たったら、その手前までがスコープ。
            var rm = RoleHeaderLineRegex.Match(trimmed);
            if (rm.Success && !trimmed.StartsWith('['))
            {
                end = i - 1;
                break;
            }
        }
        return (start, end);
    }

    /// <summary>役職スコープ内のテキストから既存エントリの alias_id と名前テキストを抽出する。
    /// 戻り値の HashSet 群は候補除外集合として使う。entryCountInScope は p_target 計算用。</summary>
    private static (HashSet<int> personIds, HashSet<int> companyIds,
                    HashSet<string> personNames, HashSet<string> companyNames,
                    int entryCountInScope)
        ExtractScopeEntries(string[] lines, int startLine, int endLine)
    {
        var personIds = new HashSet<int>();
        var companyIds = new HashSet<int>();
        var personNames = new HashSet<string>(StringComparer.Ordinal);
        var companyNames = new HashSet<string>(StringComparer.Ordinal);
        int entryCount = 0;

        for (int i = startLine; i <= endLine && i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            // ブロック区切りの単独 '-' 行はスキップ。
            if (trimmed == "-") continue;
            // 役職ノート（@notes=）行はスキップ。
            if (trimmed.StartsWith("@notes=", StringComparison.Ordinal)) continue;
            // leading_company [[屋号]] はエントリではないのでスキップ。
            if (trimmed.StartsWith("[[", StringComparison.Ordinal)) continue;

            // 行内の「、」「,」（全角・半角カンマ）で複数エントリを分解。
            // 簡易処理なので、フリーテキストノーツ <c>// xxx</c> 内のカンマ等を厳密には扱わない。
            // 抽出目的は除外集合の構築なので、多少漏れがあっても候補に既に並んでる
            // 名前を 1 つ余分に表示してしまう程度の影響しかない。
            var cells = trimmed.Split(new[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawCell in cells)
            {
                var cell = rawCell.Trim();
                if (string.IsNullOrEmpty(cell)) continue;
                // ロール内の "// xxx" コメントは無視。
                int slashSlash = cell.IndexOf("//", StringComparison.Ordinal);
                if (slashSlash >= 0) cell = cell.Substring(0, slashSlash).Trim();
                if (string.IsNullOrEmpty(cell)) continue;

                entryCount++;

                // COMPANY: [屋号] or [屋号#aliasid] or [屋号#aliasid#CI] or [屋号#CI]
                if (cell.StartsWith('['))
                {
                    var mc = CompanyAliasIdRegex.Match(cell);
                    if (mc.Success && int.TryParse(mc.Groups[1].Value, out var caid))
                    {
                        companyIds.Add(caid);
                    }
                    else
                    {
                        // [屋号] / [屋号#CI] 形式 → 屋号名のみ。
                        // 末尾 ']' を落として、'#' 以降を切ったものを名前とみなす。
                        var inner = cell.TrimStart('[').TrimEnd(']');
                        int hash = inner.IndexOf('#');
                        var rawName = hash >= 0 ? inner.Substring(0, hash) : inner;
                        var norm = NormalizeName(rawName);
                        if (norm.Length > 0) companyNames.Add(norm);
                    }
                    continue;
                }

                // CHARACTER_VOICE: <キャラ>声優 → 声優部分のみ PERSON 除外として加える。
                if (cell.StartsWith('<'))
                {
                    int gt = cell.IndexOf('>');
                    if (gt >= 0 && gt + 1 < cell.Length)
                    {
                        var voicePart = cell.Substring(gt + 1).Trim();
                        // 所属括弧 "(xxx)" を落とす。
                        int paren = voicePart.IndexOf('(');
                        if (paren >= 0) voicePart = voicePart.Substring(0, paren).Trim();
                        var mp = PersonAliasIdRegex.Match(voicePart);
                        if (mp.Success && int.TryParse(mp.Groups[1].Value, out var paid))
                        {
                            personIds.Add(paid);
                        }
                        else
                        {
                            var rawName = voicePart;
                            int hash = rawName.IndexOf('#');
                            if (hash >= 0) rawName = rawName.Substring(0, hash);
                            var norm = NormalizeName(rawName);
                            if (norm.Length > 0) personNames.Add(norm);
                        }
                    }
                    continue;
                }

                // PERSON: 名前 or 名前#aliasid。所属括弧 "(xxx)" を落とす。
                var personCell = cell;
                int parenP = personCell.IndexOf('(');
                if (parenP >= 0) personCell = personCell.Substring(0, parenP).Trim();
                var mpp = PersonAliasIdRegex.Match(personCell);
                if (mpp.Success && int.TryParse(mpp.Groups[1].Value, out var pid))
                {
                    personIds.Add(pid);
                }
                else
                {
                    var rawName = personCell;
                    int hash = rawName.IndexOf('#');
                    if (hash >= 0) rawName = rawName.Substring(0, hash);
                    var norm = NormalizeName(rawName);
                    if (norm.Length > 0) personNames.Add(norm);
                }
            }
        }

        return (personIds, companyIds, personNames, companyNames, entryCount);
    }

    /// <summary>名前テキストの正規化（前後 SP 除去 + 内部の半角/全角 SP 除去）。
    /// 「成田 良美」「成田良美」のような半角 SP 有無のゆらぎを吸収して比較できるようにする。</summary>
    private static string NormalizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
    }

    /// <summary>指定 role_code からクラスタ（連結成分）の全メンバー role_code を返す。
    /// role_successions を無向グラフとみなして BFS で展開する。深さ上限 10。
    /// 候補が無い場合（系譜情報無し）は引数の role_code 1 件だけを返す。</summary>
    private static IReadOnlyList<string> ResolveRoleCluster(
        string rootCode,
        IReadOnlyList<RoleSuccession> allSuccessions)
    {
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var s in allSuccessions)
        {
            if (!adjacency.TryGetValue(s.FromRoleCode, out var fromSet))
            {
                fromSet = new HashSet<string>(StringComparer.Ordinal);
                adjacency[s.FromRoleCode] = fromSet;
            }
            fromSet.Add(s.ToRoleCode);
            if (!adjacency.TryGetValue(s.ToRoleCode, out var toSet))
            {
                toSet = new HashSet<string>(StringComparer.Ordinal);
                adjacency[s.ToRoleCode] = toSet;
            }
            toSet.Add(s.FromRoleCode);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { rootCode };
        var queue = new Queue<(string code, int depth)>();
        queue.Enqueue((rootCode, 0));
        while (queue.Count > 0)
        {
            var (code, depth) = queue.Dequeue();
            if (depth >= 10) continue;
            if (!adjacency.TryGetValue(code, out var neighbors)) continue;
            foreach (var n in neighbors)
            {
                if (visited.Add(n)) queue.Enqueue((n, depth + 1));
            }
        }
        return visited.ToList();
    }

    /// <summary>使用履歴を alias_id でグルーピングしてスコア計算 → 上位 K 件を返す。
    /// 指数減衰は「現在編集中クレジットのアンカー日付 t_E」と「履歴クレジットの日付 t_i」の絶対差 |t_E - t_i|
    /// を τ=84 日で減衰させる。これにより、2005 年の初代プリキュア編集時には 2003〜2007 年付近の
    /// クレジットだけが高スコアになる（2024 年のオフショアスタッフは候補に出ない）。
    /// excludeIds / excludeNormalizedNames に含まれる alias は完全除外する。</summary>
    private static IReadOnlyList<(int AliasId, string Name, double Score)> RankUsages(
        IEnumerable<RoleAliasUsage> usages,
        DateTime anchorDate,
        int? currentSeriesId,
        int pTarget,
        HashSet<int> excludeIds,
        HashSet<string> excludeNormalizedNames)
    {
        var byAlias = usages
            .Where(u => !excludeIds.Contains(u.AliasId))
            .Where(u => !excludeNormalizedNames.Contains(NormalizeName(u.Name)))
            .GroupBy(u => u.AliasId);

        var scored = new List<(int AliasId, string Name, double Score)>();
        foreach (var grp in byAlias)
        {
            double sum = 0.0;
            string name = grp.First().Name;
            foreach (var u in grp)
            {
                double deltaDays = Math.Abs((anchorDate - u.UsedAt).TotalDays);
                double decay = Math.Exp(-deltaDays / DecayTauDays);
                double seriesBoost = (currentSeriesId.HasValue && u.SeriesId == currentSeriesId.Value)
                    ? SeriesBoost
                    : 1.0;
                double posMatch = Math.Exp(-Math.Abs(u.EntrySeq - pTarget) / PositionSpread);
                sum += decay * seriesBoost * posMatch;
            }
            scored.Add((grp.Key, name, sum));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(MaxCandidatesPerSection)
            .ToList();
    }

    /// <summary>編集中クレジットのアンカー日付を解決する。
    /// EPISODE スコープなら <c>episodes.on_air_at</c>、SERIES スコープなら <c>series.start_date</c>。
    /// 取得失敗時は null を返す（呼び出し側は候補集計を中止する）。
    /// <c>EpisodesRepository</c> には GetByIdAsync が無いため、エピソード側は _factory 経由で
    /// インライン Dapper クエリで on_air_at を直接引く（既存リポジトリ群への余計な API 追加を回避）。</summary>
    private async Task<DateTime?> ResolveAnchorDateAsync(int? currentSeriesId)
    {
        // EPISODE スコープ：current credit が持つ episode_id → episodes.on_air_at
        if (_currentCredit is not null && _currentCredit.ScopeKind == "EPISODE" && _currentCredit.EpisodeId.HasValue)
        {
            await using var conn = await _factory.CreateOpenedAsync().ConfigureAwait(true);
            var onAirAt = await conn.QuerySingleOrDefaultAsync<DateTime?>(
                "SELECT on_air_at FROM episodes WHERE episode_id = @EpisodeId LIMIT 1;",
                new { _currentCredit.EpisodeId }).ConfigureAwait(true);
            if (onAirAt.HasValue) return onAirAt.Value;
        }
        // SERIES スコープ：series.start_date
        if (currentSeriesId.HasValue)
        {
            var ser = await _seriesRepo.GetByIdAsync(currentSeriesId.Value).ConfigureAwait(true);
            if (ser is not null) return ser.StartDate.ToDateTime(TimeOnly.MinValue);
        }
        return null;
    }

    /// <summary>候補メニュー本体を組み立てる（既存 <see cref="ContextMenuStrip"/> の Items を詰め替える）。
    /// PERSON / COMPANY を 2 セクションに分け、セクション間にセパレータと「>>> 人物」「>>> 屋号」ラベルを挟む。
    /// 先頭には役職表示名のラベルを置いて「どの役職の候補なのか」を一目で分かるようにする。</summary>
    private void FillCandidateMenuItems(
        ContextMenuStrip menu,
        IReadOnlyList<(int AliasId, string Name, double Score)> personRanked,
        IReadOnlyList<(int AliasId, string Name, double Score)> companyRanked,
        int clickedLine,
        string roleDisplayName)
    {
        // ── ヘッダラベル（役職名） ──
        var headerLabel = new ToolStripLabel($"役職: {roleDisplayName}")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Italic),
            ForeColor = Color.DimGray,
        };
        menu.Items.Add(headerLabel);
        menu.Items.Add(new ToolStripSeparator());

        // ── 人物セクション ──
        var personLabel = new ToolStripLabel(">>> 人物")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
            ForeColor = Color.DimGray,
        };
        menu.Items.Add(personLabel);
        if (personRanked.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("(候補なし)") { Enabled = false });
        }
        else
        {
            foreach (var c in personRanked)
            {
                // ItemLabel: 名前 #aliasid（参考スコア表示は要らない、簡潔に）。
                var item = new ToolStripMenuItem($"{c.Name}  #{c.AliasId}")
                {
                    Tag = (Kind: "PERSON", AliasId: c.AliasId, Name: c.Name),
                };
                item.Click += (_, __) => InsertCandidateAtLineEnd(clickedLine, "PERSON", c.AliasId, c.Name);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        // ── 屋号セクション ──
        var companyLabel = new ToolStripLabel(">>> 屋号")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
            ForeColor = Color.DimGray,
        };
        menu.Items.Add(companyLabel);
        if (companyRanked.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("(候補なし)") { Enabled = false });
        }
        else
        {
            foreach (var c in companyRanked)
            {
                var item = new ToolStripMenuItem($"[{c.Name}]  #{c.AliasId}")
                {
                    Tag = (Kind: "COMPANY", AliasId: c.AliasId, Name: c.Name),
                };
                item.Click += (_, __) => InsertCandidateAtLineEnd(clickedLine, "COMPANY", c.AliasId, c.Name);
                menu.Items.Add(item);
            }
        }
    }

    /// <summary>指定行の末尾に候補テキストを挿入する。
    /// PERSON は <c>名前#aliasid</c>、COMPANY は <c>[名前#aliasid]</c> 形式。
    /// 行が既に何か入っている場合は「、」を区切り文字として前置する。
    /// テキストエリアの行は <see cref="TextBox.Lines"/> 由来なので、再構築は <see cref="TextBox.Text"/> を
    /// 単純に文字列連結し直す方式で行う（CRLF/LF の改行差は <c>Environment.NewLine</c> に統一）。</summary>
    private void InsertCandidateAtLineEnd(int lineIndex, string kind, int aliasId, string name)
    {
        if (txtBulkText is null) return;
        var lines = txtBulkText.Lines;
        if (lineIndex < 0 || lineIndex >= lines.Length) return;

        // 挿入文字列の組み立て。
        string token = kind switch
        {
            "PERSON" => $"{name}#{aliasId}",
            "COMPANY" => $"[{name}#{aliasId}]",
            _ => name,
        };

        var current = lines[lineIndex];
        var currentTrimmed = current.TrimEnd();
        string updatedLine;
        if (string.IsNullOrWhiteSpace(currentTrimmed))
        {
            // 完全空行：そのまま token を入れる（行頭から）。
            updatedLine = token;
        }
        else
        {
            // 既存内容ありの場合：末尾に「、token」を追加。
            // 既に「、」「,」で終わっていればそのまま append、そうでなければ「、」を挿入。
            char last = currentTrimmed[^1];
            if (last == '、' || last == ',' || last == '，')
            {
                updatedLine = currentTrimmed + token;
            }
            else
            {
                updatedLine = currentTrimmed + "、" + token;
            }
        }

        var newLines = (string[])lines.Clone();
        newLines[lineIndex] = updatedLine;
        // TextBox.Lines セッタは CRLF を自動補完する。
        txtBulkText.Lines = newLines;
        // 挿入後はカーソルをその行末に置いておく（直後の続き入力をしやすくするため）。
        int caretPos = 0;
        for (int i = 0; i < lineIndex; i++) caretPos += newLines[i].Length + Environment.NewLine.Length;
        caretPos += updatedLine.Length;
        txtBulkText.SelectionStart = caretPos;
        txtBulkText.SelectionLength = 0;
        txtBulkText.Focus();
    }
}
