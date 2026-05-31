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

    /// <summary>ブロックトップ屋号 <c>[[XXX]]</c> 行の検出。<c>CreditBulkInputParser</c> の同名 Regex と仕様一致。
    /// <c>[[OLD=>NEW]]</c> 形式の名前リダイレクトもこの Regex が拾い、中身の <c>OLD=&gt;NEW</c> 文字列を
    /// <see cref="ResolveLeadingCompanyAliasIdAsync"/> 側で分離して新側 NEW を alias 検索に使う。</summary>
    private static readonly Regex LeadingCompanyLineRegex = new(@"^\[\[(?<name>[^\[\]]+)\]\]$", RegexOptions.Compiled);

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

    /// <summary>共起ブーストの強さ α。スコア乗数は <c>1 + α × log(1 + coBlocks)</c>。
    /// α=0.5 のとき：共起 1 回 ×1.35、5 回 ×1.90、10 回 ×2.20。0 回（共起なし）は無効化（×1.0）。</summary>
    private const double CoOccurrenceAlpha = 0.5;

    /// <summary>入力途中トークンの前方一致検索で取得する最大件数（PERSON / COMPANY セクションごと）。</summary>
    private const int MaxPrefixCandidates = 10;

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

    /// <summary>役職マスタ系のフォームローカルキャッシュ
    /// (<see cref="_roleCodeByDisplayName"/> / <see cref="_allRolesCache"/> / <see cref="_allRoleSuccessionsCache"/>)
    /// を破棄して、次回右クリック時に DB から再ロードさせる。
    /// QuickAddRoleDialog で新しい role が登録された直後に呼ぶことで、候補メニューが
    /// 「直後に登録した役職」も認識できるようにする。</summary>
    private void InvalidateRoleMasterCaches()
    {
        _roleCodeByDisplayName = null;
        _allRolesCache = null;
        _allRoleSuccessionsCache = null;
    }

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
        int clickedColInLine;
        try
        {
            clientPos = txtBulkText.PointToClient(Cursor.Position);
            int charIndex = txtBulkText.GetCharIndexFromPosition(clientPos);
            clickedLine = txtBulkText.GetLineFromCharIndex(charIndex);
            // クリック行先頭文字インデックスからの差分が「行内列位置」。
            // 入力途中トークン抽出時のキャレット位置として使う。
            int lineStartChar = txtBulkText.GetFirstCharIndexFromLine(clickedLine);
            clickedColInLine = Math.Max(0, charIndex - lineStartChar);
        }
        catch
        {
            return;
        }

        try
        {
            await PopulateCandidateMenuAsync(clickedLine, clickedColInLine).ConfigureAwait(true);
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
    /// メニューの実表示は呼び出し側（<see cref="OnCandidateMenuOpening"/>）が ready フラグを立てて Show する。
    /// <paramref name="clickedColInLine"/> はクリック位置の行内列番号で、入力途中トークン抽出に使う。</summary>
    private async Task PopulateCandidateMenuAsync(int clickedLine, int clickedColInLine)
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

        // 共起件数の集計。既入力 alias の集合と過去同居した alias_id ごとに共起ブロック数を引いてくる。
        // 既入力ゼロのときは DB クエリをスキップして空辞書を渡す（ブースト無効化）。
        var personCoBlocks = new Dictionary<int, int>();
        if (personIdsInScope.Count > 0)
        {
            var coRows = await _roleAliasUsageRepo
                .GetPersonCoOccurrencesAsync(clusterCodes, personIdsInScope.ToList(), anchorDate.Value, LookbackDays)
                .ConfigureAwait(true);
            foreach (var r in coRows) personCoBlocks[r.CoAliasId] = r.CoBlockCount;
        }
        var companyCoBlocks = new Dictionary<int, int>();
        if (companyIdsInScope.Count > 0)
        {
            var coRows = await _roleAliasUsageRepo
                .GetCompanyCoOccurrencesAsync(clusterCodes, companyIdsInScope.ToList(), anchorDate.Value, LookbackDays)
                .ConfigureAwait(true);
            foreach (var r in coRows) companyCoBlocks[r.CoAliasId] = r.CoBlockCount;
        }

        var personRanked = RankUsages(personUsages, anchorDate.Value, currentSeriesId, pTarget,
            excludeIds: personIdsInScope, excludeNormalizedNames: personNamesInScope,
            coBlockCountByAlias: personCoBlocks);
        var companyRanked = RankUsages(companyUsages, anchorDate.Value, currentSeriesId, pTarget,
            excludeIds: companyIdsInScope, excludeNormalizedNames: companyNamesInScope,
            coBlockCountByAlias: companyCoBlocks);

        // ── ブロック先頭屋号 [[X]] が設定されているなら、そのロースター（過去同屋号ブロックの PERSON）を別枠候補として算出。
        // クリック位置を含むブロックの先頭屋号を抜き出し、alias_id 解決 → 専用クエリで集計 → スコアリング。
        // ロースター候補は通常の人物セクションの上に「>>> 屋号「X」の所属（過去同屋号ブロック）」セクションで出す。
        IReadOnlyList<(int AliasId, string Name, double Score)> rosterRanked = Array.Empty<(int, string, double)>();
        string? rosterCompanyDisplay = null;
        var blockScope = DetermineBlockScope(lines, scope.startLine, scope.endLine, clickedLine);
        if (!string.IsNullOrEmpty(blockScope.leadingCompanyText))
        {
            var resolved = await ResolveLeadingCompanyAliasIdAsync(blockScope.leadingCompanyText!)
                .ConfigureAwait(true);
            if (resolved.AliasId > 0)
            {
                rosterCompanyDisplay = resolved.DisplayName;
                var rosterUsages = await _roleAliasUsageRepo
                    .GetPersonUsagesUnderLeadingCompanyAsync(clusterCodes, resolved.AliasId, anchorDate.Value, LookbackDays)
                    .ConfigureAwait(true);
                rosterRanked = RankUsages(rosterUsages, anchorDate.Value, currentSeriesId, pTarget,
                    excludeIds: personIdsInScope, excludeNormalizedNames: personNamesInScope,
                    coBlockCountByAlias: personCoBlocks);
            }
        }

        // 入力途中トークン抽出 → 該当 alias マスタを前方一致検索。
        // Kind == Unspecified（明示プレフィクスなし）のときは、役職コンテキストから「どのセクションが妥当か」を
        // 既存スコープ内エントリと使用履歴の構成比で推定する：
        //   - スコープか履歴のどちらかに人物のみ → PERSON 候補だけ
        //   - スコープか履歴のどちらかに屋号のみ → COMPANY 候補だけ
        //   - 両方混在 or 履歴ゼロ → 両方サブセクションで出す
        // 明示プレフィクス（'[' → COMPANY、'>' → PERSON）があればそれを尊重して 1 種だけ検索。
        var partial = ExtractPartialTokenAt(lines[clickedLine], clickedColInLine);
        PrefixMatchResults prefixMatches = PrefixMatchResults.Empty;
        if (partial.Kind != PartialTokenKind.None && !string.IsNullOrEmpty(partial.Text))
        {
            bool wantPerson = partial.Kind == PartialTokenKind.Person;
            bool wantCompany = partial.Kind == PartialTokenKind.Company;
            if (partial.Kind == PartialTokenKind.Unspecified)
            {
                bool hasPersonContext = personIdsInScope.Count > 0 || personUsages.Count > 0;
                bool hasCompanyContext = companyIdsInScope.Count > 0 || companyUsages.Count > 0;
                if (hasPersonContext && !hasCompanyContext) { wantPerson = true; }
                else if (hasCompanyContext && !hasPersonContext) { wantCompany = true; }
                else { wantPerson = true; wantCompany = true; }
            }
            prefixMatches = await ResolvePrefixMatchCandidatesAsync(
                    partial,
                    wantPerson: wantPerson,
                    wantCompany: wantCompany,
                    excludePersonIds: personIdsInScope,
                    excludeCompanyIds: companyIdsInScope)
                .ConfigureAwait(true);
        }

        // _candidateMenu.Items を直接組み立てる。
        FillCandidateMenuItems(_candidateMenu, personRanked, companyRanked, prefixMatches,
            rosterRanked, rosterCompanyDisplay,
            clickedLine, displayName, partial);
    }

    /// <summary>クリック行が属する「ブロック」の範囲とブロックトップ屋号 <c>[[X]]</c> を返す。
    /// ロールスコープ <c>[roleStart..roleEnd]</c> 内を線形に走査して、単独 '-' 行と空行（既にエントリありの場合に発火）で
    /// ブロックを切る。<paramref name="clickedLine"/> を含むブロックの範囲と、その先頭に置かれた <c>[[X]]</c> 中身を返す。
    /// 中身は <c>OLD=&gt;NEW</c> リダイレクト形式の場合もそのまま返す（呼び出し側で split する）。</summary>
    private static (int startLine, int endLine, string? leadingCompanyText) DetermineBlockScope(
        string[] lines, int roleStartLine, int roleEndLine, int clickedLine)
    {
        int blockStart = roleStartLine;
        string? leadingCompany = null;
        bool sawEntry = false;
        for (int i = roleStartLine; i <= roleEndLine && i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            // 単独ハイフン = ブロック区切り。
            if (trimmed == "-")
            {
                if (clickedLine >= blockStart && clickedLine <= i)
                {
                    return (blockStart, i, leadingCompany);
                }
                blockStart = i + 1;
                leadingCompany = null;
                sawEntry = false;
                continue;
            }
            // 空行 = エントリありなら次のブロックの開始マーカー。
            if (trimmed.Length == 0)
            {
                if (sawEntry)
                {
                    if (clickedLine >= blockStart && clickedLine <= i)
                    {
                        return (blockStart, i, leadingCompany);
                    }
                    blockStart = i + 1;
                    leadingCompany = null;
                    sawEntry = false;
                }
                continue;
            }
            // [[X]] = ブロックトップ屋号（最初の有意行に置かれた場合のみ有効）。
            var lcm = LeadingCompanyLineRegex.Match(trimmed);
            if (lcm.Success && !sawEntry && leadingCompany is null)
            {
                leadingCompany = lcm.Groups["name"].Value.Trim();
                continue;
            }
            // 通常エントリ行。
            sawEntry = true;
        }
        // ロールスコープ末端まで来た場合、最後のブロックがクリック行を含む。
        if (clickedLine >= blockStart) return (blockStart, roleEndLine, leadingCompany);
        return (-1, -1, null);
    }

    /// <summary><c>[[XXX]]</c> 中身の文字列（<c>OLD=&gt;NEW</c> リダイレクト形式可）から company_alias_id を解決する。
    /// <c>=&gt;</c> がある場合は新側 NEW を引き当てキーに、無ければそのまま全体を使う。
    /// 完全一致で見つからなければ <c>(0, null)</c>。半角・全角スペース除去ゆらぎは
    /// <see cref="CompanyAliasesRepository.FindByExactNameAsync"/> が吸収する。</summary>
    private async Task<(int AliasId, string? DisplayName)> ResolveLeadingCompanyAliasIdAsync(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return (0, null);
        string searchName = rawText;
        int arrowIdx = rawText.IndexOf("=>", StringComparison.Ordinal);
        if (arrowIdx >= 0 && arrowIdx + 2 < rawText.Length)
        {
            searchName = rawText.Substring(arrowIdx + 2).Trim();
        }
        if (string.IsNullOrWhiteSpace(searchName)) return (0, null);

        var hits = await _companyAliasesRepo.FindByExactNameAsync(searchName).ConfigureAwait(true);
        if (hits.Count == 0) return (0, null);
        var first = hits[0];
        return (first.AliasId, first.Name);
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
    /// excludeIds / excludeNormalizedNames に含まれる alias は完全除外する。
    /// <paramref name="coBlockCountByAlias"/> は「役職スコープ内の既入力 alias と過去同一ブロックに同居した
    /// 回数」マップ。ヒット alias は <c>1 + α × log(1 + coN)</c> 倍にブースト（α=0.5）。
    /// 共起なしの alias は等倍。</summary>
    private static IReadOnlyList<(int AliasId, string Name, double Score)> RankUsages(
        IEnumerable<RoleAliasUsage> usages,
        DateTime anchorDate,
        int? currentSeriesId,
        int pTarget,
        HashSet<int> excludeIds,
        HashSet<string> excludeNormalizedNames,
        IReadOnlyDictionary<int, int> coBlockCountByAlias)
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
            // 共起ブースト：既入力 alias と過去同一ブロックに同居していたなら底上げ。
            if (coBlockCountByAlias.TryGetValue(grp.Key, out var coN) && coN > 0)
            {
                sum *= 1.0 + CoOccurrenceAlpha * Math.Log(1.0 + coN);
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
    /// セクション構成（上から順）：
    ///   1. 「>>> 入力途中『X』に一致（人物）」（<paramref name="prefixMatches"/>.Persons が非空のときだけ）
    ///   2. 「>>> 入力途中『X』に一致（屋号）」（<paramref name="prefixMatches"/>.Companies が非空のときだけ）
    ///   3. 「🏢 屋号「Y」の所属」（<paramref name="rosterRanked"/> が非空のときだけ）
    ///      クリック位置を含むブロックに <c>[[Y]]</c> ブロックトップ屋号が設定されているとき、
    ///      過去同屋号ブロックで仕事した PERSON を一覧。
    ///   4. 「>>> 人物」（役職クラスタ使用履歴順）
    ///   5. 「>>> 屋号」（役職クラスタ使用履歴順）
    /// 先頭には役職表示名のラベルを置いて「どの役職の候補なのか」を一目で分かるようにする。
    /// 入力途中セクションは <see cref="InsertCandidateReplacingToken"/> でトークン置換、
    /// 通常セクション・ロースターセクションは <see cref="InsertCandidateAtLineEnd"/> で行末追加と挿入挙動が異なる。</summary>
    private void FillCandidateMenuItems(
        ContextMenuStrip menu,
        IReadOnlyList<(int AliasId, string Name, double Score)> personRanked,
        IReadOnlyList<(int AliasId, string Name, double Score)> companyRanked,
        PrefixMatchResults prefixMatches,
        IReadOnlyList<(int AliasId, string Name, double Score)> rosterRanked,
        string? rosterCompanyDisplay,
        int clickedLine,
        string roleDisplayName,
        PartialTokenInfo partial)
    {
        // ── ヘッダラベル（役職名） ──
        var headerLabel = new ToolStripLabel($"役職: {roleDisplayName}")
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Italic),
            ForeColor = Color.DimGray,
        };
        menu.Items.Add(headerLabel);
        menu.Items.Add(new ToolStripSeparator());

        // ── 入力途中セクション（人物 / 屋号 のうち該当ありの方を、上下に並べて表示） ──
        if (prefixMatches.TotalCount > 0 && partial.Kind != PartialTokenKind.None)
        {
            if (prefixMatches.Persons.Count > 0)
            {
                AppendPrefixMatchSubsection(menu, prefixMatches.Persons,
                    $">>> 入力途中「{partial.Text}」に一致（人物）", clickedLine, partial);
            }
            if (prefixMatches.Companies.Count > 0)
            {
                AppendPrefixMatchSubsection(menu, prefixMatches.Companies,
                    $">>> 入力途中「{partial.Text}」に一致（屋号）", clickedLine, partial);
            }
            menu.Items.Add(new ToolStripSeparator());
        }

        // ── ロースターセクション（ブロックトップ屋号設定時のみ） ──
        if (rosterRanked.Count > 0 && !string.IsNullOrEmpty(rosterCompanyDisplay))
        {
            var rosterLabel = new ToolStripLabel($"🏢 屋号「{rosterCompanyDisplay}」の所属（過去同屋号ブロック）")
            {
                Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
                ForeColor = Color.FromArgb(0xB0, 0x40, 0x00),
            };
            menu.Items.Add(rosterLabel);
            foreach (var c in rosterRanked)
            {
                var item = new ToolStripMenuItem($"{c.Name}  #{c.AliasId}")
                {
                    Tag = (Kind: "PERSON", AliasId: c.AliasId, Name: c.Name),
                };
                item.Click += (_, __) => InsertCandidateAtLineEnd(clickedLine, "PERSON", c.AliasId, c.Name);
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
        }

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

    /// <summary>入力途中トークン情報。<see cref="ExtractPartialTokenAt"/> の戻り値。
    /// Kind = None のときは入力途中扱いしない（プレフィクス検索もスキップ）。
    /// Kind = Unspecified のときは明示プレフィクスが無く、PopulateCandidateMenuAsync 側で
    /// 役職コンテキスト（既存スコープ内エントリと使用履歴の構成比）から PERSON / COMPANY / 両方 を決める。</summary>
    private readonly record struct PartialTokenInfo(PartialTokenKind Kind, int StartCol, int EndCol, string Text);

    /// <summary>入力途中トークンの種別。
    /// <see cref="None"/> = 抽出失敗（ボーダー文字に囲まれていないなど）。
    /// <see cref="Person"/> = '&gt;'（CHARACTER_VOICE 閉じ）直後で PERSON 確定。
    /// <see cref="Company"/> = '[' 直後で COMPANY 確定。
    /// <see cref="Unspecified"/> = 行頭 / '、' / ',' / '，' 直後で「役職コンテキストから推定」が必要。</summary>
    private enum PartialTokenKind { None, Person, Company, Unspecified }

    /// <summary>前方一致候補（DB から引いた alias マスタ行を入力補助 UI 用に縮約した形）。
    /// <see cref="Kind"/> はメニュー描画と insertion 時の表記（PERSON は <c>名前#id</c>、COMPANY は <c>[名前#id]</c>）の分岐に使う。</summary>
    private readonly record struct PrefixMatchCandidate(PartialTokenKind Kind, int AliasId, string Name);

    /// <summary>PERSON 候補リストと COMPANY 候補リストの組。<see cref="Unspecified"/> 入力で両方検索する用途。</summary>
    private sealed record class PrefixMatchResults(
        IReadOnlyList<PrefixMatchCandidate> Persons,
        IReadOnlyList<PrefixMatchCandidate> Companies)
    {
        public static readonly PrefixMatchResults Empty = new(
            Array.Empty<PrefixMatchCandidate>(), Array.Empty<PrefixMatchCandidate>());
        public int TotalCount => Persons.Count + Companies.Count;
    }

    /// <summary>クリック位置（行内列番号 <paramref name="colInLine"/>）を起点に、その左側に伸びる
    /// 「入力途中トークン」を取り出す。トークン境界は以下：
    ///   <list type="bullet">
    ///     <item>'、' ',' '，' = エントリ区切り → Kind=Unspecified（役職コンテキストから決定）</item>
    ///     <item>'>' = CHARACTER_VOICE の閉じ括弧 → Kind=Person 確定</item>
    ///     <item>'[' = COMPANY 屋号の開き括弧 → Kind=Company 確定</item>
    ///     <item>'(' '&lt;' = 所属/キャラ名コンテキスト → 無視（None 返却）</item>
    ///     <item>']' ')' = 既に閉じてる → 入力途中ではない（None 返却）</item>
    ///     <item>'#' トークン内 = alias_id 明示済み → 候補不要（None 返却）</item>
    ///   </list>
    /// ボーダー無しで行頭まで遡った場合も Kind=Unspecified。先頭の空白は除去。空文字になったら None。</summary>
    private static PartialTokenInfo ExtractPartialTokenAt(string lineText, int colInLine)
    {
        if (string.IsNullOrEmpty(lineText)) return new(PartialTokenKind.None, 0, 0, "");
        int end = Math.Min(colInLine, lineText.Length);
        int start = end;
        var kind = PartialTokenKind.Unspecified;
        while (start > 0)
        {
            char c = lineText[start - 1];
            if (c == '、' || c == ',' || c == '，')
            {
                kind = PartialTokenKind.Unspecified;
                break;
            }
            if (c == '>')
            {
                kind = PartialTokenKind.Person;
                break;
            }
            if (c == '[')
            {
                kind = PartialTokenKind.Company;
                break;
            }
            if (c == '(' || c == '<' || c == ']' || c == ')')
            {
                return new(PartialTokenKind.None, 0, 0, "");
            }
            start--;
        }

        // 先頭の空白をスキップ。
        while (start < end && (lineText[start] == ' ' || lineText[start] == '　' || lineText[start] == '\t'))
            start++;

        if (start >= end) return new(PartialTokenKind.None, 0, 0, "");

        string partial = lineText.Substring(start, end - start);
        // alias_id 明示済みなら候補不要。
        if (partial.Contains('#')) return new(PartialTokenKind.None, 0, 0, "");
        // 純粋な空白だけだった場合も None。
        if (string.IsNullOrWhiteSpace(partial)) return new(PartialTokenKind.None, 0, 0, "");

        return new(kind, start, end, partial.TrimEnd());
    }

    /// <summary>入力途中トークンを alias マスタに対して前方一致 → 部分一致でフォールバック検索する。
    /// <paramref name="wantPerson"/> / <paramref name="wantCompany"/> で検索対象を選ぶ（両方 true なら両方検索）。
    /// PERSON は <see cref="_personAliasesRepo"/>、COMPANY は <see cref="_companyAliasesRepo"/>。
    /// 既に役職スコープ内に入力済みの alias_id は除外する。</summary>
    private async Task<PrefixMatchResults> ResolvePrefixMatchCandidatesAsync(
        PartialTokenInfo partial,
        bool wantPerson,
        bool wantCompany,
        HashSet<int> excludePersonIds,
        HashSet<int> excludeCompanyIds)
    {
        IReadOnlyList<PrefixMatchCandidate> persons = Array.Empty<PrefixMatchCandidate>();
        IReadOnlyList<PrefixMatchCandidate> companies = Array.Empty<PrefixMatchCandidate>();

        if (wantPerson)
        {
            var rows = await _personAliasesRepo
                .SearchByPrefixThenContainsAsync(partial.Text, MaxPrefixCandidates + excludePersonIds.Count)
                .ConfigureAwait(true);
            persons = rows
                .Where(r => !excludePersonIds.Contains(r.AliasId))
                .Take(MaxPrefixCandidates)
                .Select(r => new PrefixMatchCandidate(PartialTokenKind.Person, r.AliasId, r.Name))
                .ToList();
        }
        if (wantCompany)
        {
            var rows = await _companyAliasesRepo
                .SearchByPrefixThenContainsAsync(partial.Text, MaxPrefixCandidates + excludeCompanyIds.Count)
                .ConfigureAwait(true);
            companies = rows
                .Where(r => !excludeCompanyIds.Contains(r.AliasId))
                .Take(MaxPrefixCandidates)
                .Select(r => new PrefixMatchCandidate(PartialTokenKind.Company, r.AliasId, r.Name))
                .ToList();
        }
        return new PrefixMatchResults(persons, companies);
    }

    /// <summary>入力途中サブセクション 1 枚分（人物 or 屋号）の ToolStrip 要素群を追加する。
    /// セクションラベル + 各候補 ToolStripMenuItem を <paramref name="menu"/>.Items に積む。
    /// 挿入時の表記は <paramref name="candidates"/> 各 <see cref="PrefixMatchCandidate.Kind"/> に従う
    /// （PERSON は <c>名前#id</c>、COMPANY は <c>[名前#id]</c> または既存 '[' 直後の置換）。</summary>
    private void AppendPrefixMatchSubsection(
        ContextMenuStrip menu,
        IReadOnlyList<PrefixMatchCandidate> candidates,
        string sectionLabel,
        int clickedLine,
        PartialTokenInfo partial)
    {
        var pmLabel = new ToolStripLabel(sectionLabel)
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x00, 0x60, 0xC0),
        };
        menu.Items.Add(pmLabel);
        foreach (var c in candidates)
        {
            // 候補メニュー上のラベル文字列：屋号候補は `[屋号名] #id`、人物候補は `氏名 #id`。
            string itemText = c.Kind == PartialTokenKind.Company
                ? $"[{c.Name}]  #{c.AliasId}"
                : $"{c.Name}  #{c.AliasId}";
            var item = new ToolStripMenuItem(itemText);
            // ローカル変数キャプチャは foreach 内で OK（c は毎回 fresh）。
            item.Click += (_, __) => InsertCandidateReplacingToken(
                clickedLine, partial.StartCol, partial.EndCol, partial.Kind, c.Kind, c.AliasId, c.Name);
            menu.Items.Add(item);
        }
    }

    /// <summary>入力途中セクション専用の挿入：トークン位置 <c>[startCol..endCol)</c> を
    /// alias_id 付きの正式表記で置換する。
    /// <paramref name="contextKind"/> は「クリック位置のコンテキスト」（'[' 内 / '&gt;' 後 / 何もなし）。
    /// <paramref name="candidateKind"/> は「ユーザーが選んだ候補の種類」（PERSON / COMPANY）。
    /// 表記分岐：
    ///   - candidateKind=Company かつ contextKind=Company → <c>名前#id]</c>（既に行内に '[' があるので閉じ括弧だけ追加。右側に既に ']' があれば付けない）
    ///   - candidateKind=Company かつ contextKind ≠ Company → <c>[名前#id]</c>（コンテキストに括弧が無いので両側に追加）
    ///   - candidateKind=Person → <c>名前#id</c>（括弧不要）</summary>
    private void InsertCandidateReplacingToken(
        int lineIndex, int startCol, int endCol,
        PartialTokenKind contextKind, PartialTokenKind candidateKind,
        int aliasId, string name)
    {
        if (txtBulkText is null) return;
        var lines = txtBulkText.Lines;
        if (lineIndex < 0 || lineIndex >= lines.Length) return;

        string line = lines[lineIndex];
        if (startCol < 0 || endCol < startCol || startCol > line.Length) return;
        int safeEnd = Math.Min(endCol, line.Length);

        string replacement;
        if (candidateKind == PartialTokenKind.Company)
        {
            if (contextKind == PartialTokenKind.Company)
            {
                // 既存 '[' の続きを書く形：右側に ']' が無ければ閉じる、あれば素通り。
                bool alreadyClosed = safeEnd < line.Length && line[safeEnd] == ']';
                replacement = alreadyClosed ? $"{name}#{aliasId}" : $"{name}#{aliasId}]";
            }
            else
            {
                // コンテキストが括弧なし（Unspecified）で COMPANY 候補を選んだ：両側に '[' ']' を補う。
                replacement = $"[{name}#{aliasId}]";
            }
        }
        else
        {
            replacement = $"{name}#{aliasId}";
        }

        string newLine = line.Substring(0, startCol) + replacement + line.Substring(safeEnd);
        var newLines = (string[])lines.Clone();
        newLines[lineIndex] = newLine;
        // 縦スクロールを退避（TextBox.Lines セッタは内部で完全リフレッシュが走り先頭行に戻る）。
        int savedFirstLine = CaptureBulkTextFirstVisibleLine();
        txtBulkText.Lines = newLines;

        // 置換後はキャレットを replacement の直後に置く。
        int caretPos = 0;
        for (int i = 0; i < lineIndex; i++) caretPos += newLines[i].Length + Environment.NewLine.Length;
        caretPos += startCol + replacement.Length;
        txtBulkText.SelectionStart = caretPos;
        txtBulkText.SelectionLength = 0;
        txtBulkText.Focus();
        // SelectionStart 設定後にスクロール復元（順番が逆だと SelectionStart のキャレット可視化処理で再びズレる）。
        RestoreBulkTextFirstVisibleLine(savedFirstLine);
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
        // 縦スクロールを退避（TextBox.Lines セッタは内部で完全リフレッシュが走り先頭行に戻る）。
        int savedFirstLine = CaptureBulkTextFirstVisibleLine();
        // TextBox.Lines セッタは CRLF を自動補完する。
        txtBulkText.Lines = newLines;
        // 挿入後はカーソルをその行末に置いておく（直後の続き入力をしやすくするため）。
        int caretPos = 0;
        for (int i = 0; i < lineIndex; i++) caretPos += newLines[i].Length + Environment.NewLine.Length;
        caretPos += updatedLine.Length;
        txtBulkText.SelectionStart = caretPos;
        txtBulkText.SelectionLength = 0;
        // スクロール復元は SelectionStart 設定後（順番が逆だと SelectionStart のキャレット可視化で再びズレる）。
        RestoreBulkTextFirstVisibleLine(savedFirstLine);
        txtBulkText.Focus();
    }
}
