using System.Diagnostics;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// クレジット一括入力ダイアログ（v1.2.1 追加。v1.2.2 で ReplaceScope モード追加）。
/// <para>
/// クレジット編集画面（<see cref="CreditEditorForm"/>）から起動する 2 つの動作モードを持つ:
/// <list type="bullet">
///   <item><description><see cref="BulkInputMode.AppendToCredit"/>:
///     既存クレジットに対する「📝 クレジット一括入力...」ボタン経由（v1.2.1 仕様）。
///     パース結果を <see cref="CreditDraftSession"/> の末尾に追加する。</description></item>
///   <item><description><see cref="BulkInputMode.ReplaceScope"/>:
///     ツリー右クリック「📝 一括入力で編集...」経由（v1.2.2 追加）。
///     対象スコープ（クレジット全体／カード／Tier／Group／Role のいずれか）の現在の内容を
///     <see cref="CreditBulkInputEncoder"/> で逆翻訳して既定値とし、ユーザー編集後に
///     <see cref="CreditBulkApplyService.ApplyToDraftReplaceAsync"/> で対象スコープを置換する。</description></item>
/// </list>
/// </para>
/// <para>
/// 共通の動作フロー:
/// <list type="number">
///   <item><description>テキスト編集 → 250ms デバウンスで <see cref="CreditBulkInputParser.Parse"/> を非同期実行
///     → 結果を <see cref="BulkParseResult"/> として保持 → 右ペインのツリーと警告リストに反映。</description></item>
///   <item><description>Block 重大度の警告が 1 件でもあれば「適用」ボタンを Disabled にする。</description></item>
///   <item><description>「適用」押下時:
///     <list type="number">
///       <item><description><see cref="CreditBulkApplyService.ResolveAsync"/> で役職を解決</description></item>
///       <item><description>未解決役職があれば <see cref="QuickAddRoleDialog"/> を 1 件ずつ起動して登録</description></item>
///       <item><description>モードに応じて <see cref="CreditBulkApplyService.ApplyToDraftAsync"/> または
///         <see cref="CreditBulkApplyService.ApplyToDraftReplaceAsync"/> を呼ぶ</description></item>
///       <item><description>DialogResult=OK で閉じる（呼び出し側はその場でツリー再描画 → ユーザーが「保存」で DB 確定）</description></item>
///     </list>
///   </description></item>
/// </list>
/// </para>
/// <para>
/// 本ダイアログは DB に直接書かないが、マスタ自動投入（Person / Character / Company / Role の QuickAdd）は
/// この時点で実行される。クレジット本体の編集は Draft セッション経由で「保存」ボタンを押すまで確定しない。
/// </para>
/// </summary>
public partial class CreditBulkInputDialog : Form
{
    private readonly CreditDraftSession _session;
    private readonly CreditBulkApplyService _applyService;
    private readonly RolesRepository _rolesRepo;

    /// <summary>
    /// 動作モード（v1.2.2 追加）。コンストラクタで決定され以降変更されない。
    /// </summary>
    private readonly BulkInputMode _mode;

    /// <summary>
    /// ReplaceScope モードでの置換対象スコープ（v1.2.2 追加）。
    /// AppendToCredit モードでは null。
    /// </summary>
    private readonly DraftScopeRef? _scope;

    /// <summary>パース結果（リアルタイム反映）。</summary>
    private BulkParseResult _lastParse = new();

    /// <summary>
    /// 250ms デバウンス用タイマー。フィールド初期化子で生成して両コンストラクタから共有する
    /// （v1.2.2 のコンストラクタ追加に伴い <c>readonly</c> 維持のため宣言時に初期化）。
    /// </summary>
    private readonly System.Windows.Forms.Timer _debounceTimer = new() { Interval = 250 };

    /// <summary>
    /// 適用が成功して Draft に流し込まれたかを示す。呼び出し側はこれが true のときだけ
    /// 自フォームのツリーを再描画する。
    /// </summary>
    public bool Applied { get; private set; }

    /// <summary>
    /// AppendToCredit モード（v1.2.1 互換）でダイアログを起動するコンストラクタ。
    /// 既存クレジットの末尾に追加する用途。
    /// </summary>
    public CreditBulkInputDialog(
        CreditDraftSession session,
        CreditBulkApplyService applyService,
        RolesRepository rolesRepo)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _mode = BulkInputMode.AppendToCredit;
        _scope = null;

        InitializeComponent();
        InitializeCommonHandlers();

        // AppendToCredit モードではスコープ表示ラベルは「クレジット末尾追加」固定。
        ApplyScopeLabel("対象: クレジット末尾に追加");

        // 初回パース（空入力の状態でも UI を整える）
        RunParseAndRefresh();
    }

    /// <summary>
    /// ReplaceScope モード（v1.2.2 追加）でダイアログを起動するコンストラクタ。
    /// 指定スコープの中身を Encoder で逆翻訳した文字列を初期値とし、ユーザー編集後に
    /// 同スコープの中身を新パース結果で置換する用途。
    /// </summary>
    /// <param name="session">対象の編集セッション。</param>
    /// <param name="applyService">役職解決 / Draft 注入を行うサービス。</param>
    /// <param name="rolesRepo">未解決役職の QuickAdd 用。</param>
    /// <param name="scope">置換対象スコープ。</param>
    /// <param name="initialText">テキストエディタ初期値（通常は <see cref="CreditBulkInputEncoder"/> の出力）。</param>
    public CreditBulkInputDialog(
        CreditDraftSession session,
        CreditBulkApplyService applyService,
        RolesRepository rolesRepo,
        DraftScopeRef scope,
        string initialText)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        _mode = BulkInputMode.ReplaceScope;

        InitializeComponent();
        InitializeCommonHandlers();

        // 対象スコープの種別をユーザーに視覚的に明示する（誤って別範囲を編集しないように）。
        ApplyScopeLabel($"対象: {scope.GetDisplayLabel()}（既存内容を置換）");

        // 初期テキストをセット（イベント発火を抑制せず、TextChanged → デバウンス → パースの通常経路を辿らせる）。
        // null/空文字を渡された場合はそのまま空入力状態でダイアログを開く（新規作成相当のフロー）。
        txtInput.Text = initialText ?? string.Empty;

        // 初回パース実行（TextChanged はコンストラクタ完了前のため発火しない可能性があるので明示）。
        RunParseAndRefresh();
    }

    /// <summary>
    /// 両コンストラクタ共通のイベントハンドラ初期化（v1.2.2 リファクタで共通化）。
    /// </summary>
    private void InitializeCommonHandlers()
    {
        // 250ms デバウンス：キーストローク連打時はパースを遅延させ、最後の入力から 250ms 静止したら実行する。
        // タイマー本体はフィールド初期化子で生成済みなので、ここでは Tick ハンドラの取り付けのみ行う。
        _debounceTimer.Tick += OnDebounceTick;

        txtInput.TextChanged += OnInputChanged;
        btnApply.Click += async (_, __) => await OnApplyAsync();
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

        // v1.3.0: 似て非なる名義の全件比較進捗を警告ペイン上部のステータスラベルに反映する。
        // ApplyService 側は比較ループ中に約 50 件単位で発火する。完了時には (total, total) が来るので、
        // それを検知して非表示に戻す（次回 Apply まで領域を取らない）。
        _applyService.CompareProgress += OnCompareProgress;
    }

    /// <summary>
    /// 似て非なる名義の比較進捗を警告ペイン上部のステータスラベルに反映する（v1.3.0 追加）。
    /// ApplyService からは UI スレッド上で同期発火される（全 await が ConfigureAwait(true) のため）想定だが、
    /// 念のため <see cref="Control.InvokeRequired"/> で保護してクロスズスレッド例外を避ける。
    /// </summary>
    private void OnCompareProgress(int done, int total)
    {
        if (lblCompareProgress is null) return;

        // クロスズスレッド呼び出しの保険（実運用では UI スレッド経由のはずだが、将来の改修で
        // ApplyService が並列化された場合の事故防止）。
        if (lblCompareProgress.InvokeRequired)
        {
            lblCompareProgress.BeginInvoke(new Action(() => OnCompareProgress(done, total)));
            return;
        }

        if (done >= total)
        {
            // 完了 → 表示を消して領域を返す。
            lblCompareProgress.Visible = false;
            lblCompareProgress.Text = string.Empty;
        }
        else
        {
            lblCompareProgress.Visible = true;
            lblCompareProgress.Text = $"似て非なる名義を比較中... ({done}/{total})";
        }
    }

    /// <summary>
    /// ダイアログ上部のスコープ表示ラベルにテキストを設定する（v1.2.2 追加）。
    /// Designer 側で <c>lblScope</c> を配置していればそこに反映、なければ Form の Text に書く（フォールバック）。
    /// </summary>
    private void ApplyScopeLabel(string text)
    {
        // lblScope は Designer.cs 側で v1.2.2 に追加された Label コントロール。
        // 互換性のため null チェックして、未配置の場合はタイトルバーに反映する。
        if (lblScope is not null)
        {
            lblScope.Text = text;
        }
        else
        {
            this.Text = $"クレジット一括入力 — {text}";
        }
    }

    /// <summary>テキスト変更時：デバウンスタイマーを再スタート（最後の入力から 250ms 後に発火）。</summary>
    private void OnInputChanged(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>デバウンスタイマー発火：パース実行 → UI 反映。</summary>
    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RunParseAndRefresh();
    }

    /// <summary>
    /// 現在のテキストをパースして結果を <see cref="_lastParse"/> に格納し、ツリー / 警告リスト / 適用ボタンを更新する。
    /// 例外はメッセージとして警告リストに表示する（ダイアログを落とさない）。
    /// </summary>
    private void RunParseAndRefresh()
    {
        try
        {
            _lastParse = CreditBulkInputParser.Parse(txtInput.Text);
        }
        catch (Exception ex)
        {
            // パーサ自体の不具合に備えて握る（ユーザー視点では「警告に何か出る」程度の挙動）。
            _lastParse = new BulkParseResult();
            _lastParse.Warnings.Add(new ParseWarning
            {
                Severity = WarningSeverity.Block,
                Message = $"パースエラー: {ex.Message}"
            });
            Debug.WriteLine(ex);
        }

        RefreshPreviewTree();
        RefreshWarningList();
        UpdateApplyButtonState();
    }

    /// <summary>右ペインのツリーをパース結果で再構築する。</summary>
    private void RefreshPreviewTree()
    {
        treePreview.BeginUpdate();
        try
        {
            treePreview.Nodes.Clear();

            int cardIdx = 0;
            foreach (var card in _lastParse.Cards)
            {
                cardIdx++;
                // v1.2.2: カード備考があればラベルに付記。
                string cardLabel = string.IsNullOrEmpty(card.Notes)
                    ? $"カード {cardIdx}"
                    : $"カード {cardIdx}  📝{card.Notes}";
                var nodeCard = treePreview.Nodes.Add(cardLabel);

                int tierIdx = 0;
                foreach (var tier in card.Tiers)
                {
                    tierIdx++;
                    // v1.2.2: ティア備考があればラベルに付記。
                    string tierLabel = string.IsNullOrEmpty(tier.Notes)
                        ? $"ティア {tierIdx}"
                        : $"ティア {tierIdx}  📝{tier.Notes}";
                    var nodeTier = nodeCard.Nodes.Add(tierLabel);

                    int groupIdx = 0;
                    foreach (var group in tier.Groups)
                    {
                        groupIdx++;
                        // v1.2.2: グループ備考があればラベルに付記。
                        string groupLabel = string.IsNullOrEmpty(group.Notes)
                            ? $"グループ {groupIdx}"
                            : $"グループ {groupIdx}  📝{group.Notes}";
                        var nodeGroup = nodeTier.Nodes.Add(groupLabel);

                        foreach (var role in group.Roles)
                        {
                            // 解決済みなら role_code を表示、未解決なら表示名そのまま。
                            // v1.2.2: 役職備考があればラベルに付記。
                            string roleHead = role.ResolvedRoleCode is null
                                ? $"[未解決] {role.DisplayName}"
                                : $"{role.DisplayName} ({role.ResolvedRoleCode})";
                            string roleLabel = string.IsNullOrEmpty(role.Notes)
                                ? roleHead
                                : $"{roleHead}  📝{role.Notes}";
                            var nodeRole = nodeGroup.Nodes.Add(roleLabel);

                            int blockIdx = 0;
                            foreach (var block in role.Blocks)
                            {
                                blockIdx++;
                                // v1.2.2: 明示 col_count か推測値かをラベルで区別。leading_company・備考も付記。
                                string colsLabel = block.ColCountExplicit
                                    ? $"col={block.ColCount}*"
                                    : $"col={block.ColCount}";
                                string leadCompPart = block.LeadingCompanyText is null
                                    ? ""
                                    : $" [[{block.LeadingCompanyText}]]";
                                string notesPart = string.IsNullOrEmpty(block.Notes)
                                    ? ""
                                    : $"  📝{block.Notes}";
                                string blockLabel = $"ブロック {blockIdx} ({colsLabel}){leadCompPart}{notesPart}";
                                var nodeBlock = nodeRole.Nodes.Add(blockLabel);

                                foreach (var row in block.Rows)
                                {
                                    foreach (var entry in row.Entries)
                                    {
                                        nodeBlock.Nodes.Add(FormatEntryLabel(entry));
                                    }
                                }
                            }
                        }
                    }
                }
            }

            treePreview.ExpandAll();
        }
        finally
        {
            treePreview.EndUpdate();
        }
    }

    /// <summary>
    /// 1 エントリの表示文字列を組み立てる。
    /// <para>
    /// v1.2.2: LOGO は <c>[屋号#CIバージョン]</c> 形式で表示し、エントリ前後修飾子（🎬／&amp;／備考）も表記する。
    /// </para>
    /// </summary>
    private static string FormatEntryLabel(ParsedEntry e)
    {
        // 種別ごとの本体ラベル。アイコン絵文字でユーザーが視覚的に種別を識別できるようにする。
        string body = e.Kind switch
        {
            ParsedEntryKind.CharacterVoice =>
                $"🎤 <{(e.IsForcedNewCharacter ? "*" : "")}{e.CharacterRawText}> {e.PersonRawText}"
                + (string.IsNullOrEmpty(e.AffiliationRawText) ? "" : $" ({e.AffiliationRawText})"),
            ParsedEntryKind.Company =>
                $"🏢 [{e.CompanyRawText}]",
            ParsedEntryKind.Logo =>
                // v1.2.2: 屋号 + CI バージョンラベルの両方を保持しているのでフル表現で表示。
                $"🖼 [{e.CompanyRawText}#{e.LogoCiVersionLabel}]",
            ParsedEntryKind.Text =>
                $"📝 {e.RawText}",
            _ =>
                $"👤 {e.PersonRawText}"
                + (string.IsNullOrEmpty(e.AffiliationRawText) ? "" : $" ({e.AffiliationRawText})")
        };

        // v1.2.2: エントリ前後修飾子を本体ラベルに付加。
        // 行頭: 🎬（本放送限定）/ &（A/B 併記）はアイコン化して左に置く。
        string prefix = "";
        if (e.IsBroadcastOnly) prefix += "🎬 ";
        if (e.IsParallelContinuation) prefix += "& ";

        // 行末: エントリ備考は「📝備考」として右に付加。
        string suffix = string.IsNullOrEmpty(e.Notes) ? "" : $"  📝{e.Notes}";

        return prefix + body + suffix;
    }

    /// <summary>右下の警告リストをパース結果で再構築する。</summary>
    private void RefreshWarningList()
    {
        lstWarnings.BeginUpdate();
        try
        {
            lstWarnings.Items.Clear();

            foreach (var w in _lastParse.Warnings)
            {
                string prefix = w.Severity switch
                {
                    WarningSeverity.Block => "🛑 ",
                    WarningSeverity.Warning => "⚠ ",
                    _ => "ℹ "
                };
                lstWarnings.Items.Add(prefix + w.Message);
            }
        }
        finally
        {
            lstWarnings.EndUpdate();
        }
    }

    /// <summary>
    /// 「適用」ボタンの活性／非活性を判定する。
    /// 入力が空、Block 警告あり、パース結果が実質空の場合は適用不可。
    /// </summary>
    private void UpdateApplyButtonState()
    {
        bool hasContent = !string.IsNullOrWhiteSpace(txtInput.Text);
        bool blocked = _lastParse.HasBlockingWarnings;
        bool empty = _lastParse.IsEmpty;

        btnApply.Enabled = hasContent && !blocked && !empty;
    }

    /// <summary>
    /// 「適用」ボタン押下：役職解決 → 未解決役職の QuickAdd ループ → Draft 注入 → DialogResult=OK で閉じる。
    /// </summary>
    private async Task OnApplyAsync()
    {
        try
        {
            // STEP 1: 役職解決（roles マスタとの引き当て）
            await _applyService.ResolveAsync(_lastParse).ConfigureAwait(true);

            // STEP 2: 未解決役職を 1 件ずつユーザーに登録してもらう
            if (_applyService.UnresolvedRoles.Count > 0)
            {
                // 同じ DisplayName の未解決役職をまとめて 1 回の QuickAdd で解決する
                var distinctNames = _applyService.UnresolvedRoles
                    .Select(r => r.DisplayName)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var resolutions = new Dictionary<string, (string RoleCode, string FormatKind)>(StringComparer.Ordinal);

                foreach (var displayName in distinctNames)
                {
                    using var dlg = new QuickAddRoleDialog(_rolesRepo);
                    // 日本語名を事前入力する。書式区分は素朴判定：表示名に「声」「キャスト」「Voice」を含めば VOICE_CAST。
                    dlg.PrefilledNameJa = displayName;
                    dlg.PrefilledFormatKind = LooksLikeVoiceCastDisplayName(displayName) ? "VOICE_CAST" : null;
                    dlg.Text = $"未登録役職を追加: 「{displayName}」";

                    if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedRoleCode))
                    {
                        // ユーザーがキャンセルした場合は適用処理ごと中断する。
                        // 既に途中で投入された役職はマスタに残るが、ロールバック対象ではないので許容。
                        MessageBox.Show(this,
                            $"役職「{displayName}」が未登録のため、適用を中断しました。",
                            "適用中断", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // QuickAddRoleDialog は role_format_kind を返さないので、登録直後にマスタから引き直す。
                    var newRole = await _rolesRepo.GetByCodeAsync(dlg.SelectedRoleCode).ConfigureAwait(true);
                    string formatKind = newRole?.RoleFormatKind ?? "NORMAL";

                    resolutions[displayName] = (dlg.SelectedRoleCode, formatKind);
                }

                _applyService.ApplyRoleResolutions(_lastParse, resolutions);
            }

            // STEP 3: Draft 注入（Person/Character/Company の自動 QuickAdd を含む）
            // v1.2.2: モードに応じて末尾追加 or スコープ置換を呼び分ける。
            string? user = Environment.UserName;
            switch (_mode)
            {
                case BulkInputMode.AppendToCredit:
                    // v1.2.1 既存挙動: 既存クレジットの末尾に新規 Card 群を追加。
                    await _applyService.ApplyToDraftAsync(_lastParse, _session, user).ConfigureAwait(true);
                    break;

                case BulkInputMode.ReplaceScope:
                    // v1.2.2 追加: 指定スコープ配下を置換。
                    if (_scope is null)
                    {
                        // 論理エラー（ReplaceScope モードで _scope が null）。安全側に倒して表示。
                        MessageBox.Show(this, "ReplaceScope モードのスコープが指定されていません。",
                            "適用エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    await _applyService.ApplyToDraftReplaceAsync(
                        _lastParse, _session, _scope, user).ConfigureAwait(true);
                    break;

                default:
                    throw new InvalidOperationException($"未対応のモード: {_mode}");
            }

            // STEP 4: 完了
            Applied = true;

            // 情報メッセージがあれば最後にまとめて表示
            if (_applyService.InfoMessages.Count > 0)
            {
                MessageBox.Show(this,
                    "適用は完了しましたが、以下の情報があります:\r\n\r\n" + string.Join("\r\n", _applyService.InfoMessages),
                    "適用完了（情報あり）", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "適用エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>役職表示名から VOICE_CAST っぽさを推定（QuickAddRoleDialog の事前選択用）。</summary>
    private static bool LooksLikeVoiceCastDisplayName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("声")
            || name.Contains("キャスト")
            || name.Contains("CAST", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Voice", StringComparison.OrdinalIgnoreCase);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _debounceTimer.Stop();
        _debounceTimer.Dispose();
    }
}

/// <summary>
/// <see cref="CreditBulkInputDialog"/> の動作モード（v1.2.2 追加）。
/// </summary>
public enum BulkInputMode
{
    /// <summary>
    /// 既存クレジットの末尾にパース結果を追加するモード（v1.2.1 既存仕様）。
    /// 「📝 クレジット一括入力...」ボタン経由で起動される。
    /// </summary>
    AppendToCredit,

    /// <summary>
    /// 指定スコープ（クレジット全体／カード／Tier／Group／Role のいずれか）の中身を、
    /// パース結果で置換するモード（v1.2.2 追加）。ツリー右クリック「📝 一括入力で編集...」経由で起動される。
    /// 既定テキストには <see cref="Drafting.CreditBulkInputEncoder"/> が現在の Draft を逆翻訳した文字列が入る。
    /// </summary>
    ReplaceScope,
}
