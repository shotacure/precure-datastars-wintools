using System.Diagnostics;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// クレジット一括入力ダイアログ（v1.2.1 追加）。
/// <para>
/// クレジット編集画面（<see cref="CreditEditorForm"/>）の「📝 クレジット一括入力...」ボタンから起動する。
/// 左ペインに複数行テキストエディタ（書式: <see cref="CreditBulkInputParser"/> 仕様）を、
/// 右ペインにツリープレビューと警告リスト、下段に「💾 適用」ボタンを配置した左右分割画面。
/// </para>
/// <para>
/// 動作フロー:
/// <list type="number">
///   <item><description>テキスト編集 → 250ms デバウンスで <see cref="CreditBulkInputParser.Parse"/> を非同期実行
///     → 結果を <see cref="BulkParseResult"/> として保持 → 右ペインのツリーと警告リストに反映。</description></item>
///   <item><description>Block 重大度の警告が 1 件でもあれば「適用」ボタンを Disabled にする。</description></item>
///   <item><description>「適用」押下時:
///     <list type="number">
///       <item><description><see cref="CreditBulkApplyService.ResolveAsync"/> で役職を解決</description></item>
///       <item><description>未解決役職があれば <see cref="QuickAddRoleDialog"/> を 1 件ずつ起動して登録</description></item>
///       <item><description><see cref="CreditBulkApplyService.ApplyToDraftAsync"/> で
///         呼び出し元の <see cref="CreditDraftSession"/> に流し込む</description></item>
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

    /// <summary>パース結果（リアルタイム反映）。</summary>
    private BulkParseResult _lastParse = new();

    /// <summary>250ms デバウンス用タイマー。</summary>
    private readonly System.Windows.Forms.Timer _debounceTimer;

    /// <summary>
    /// 適用が成功して Draft に流し込まれたかを示す。呼び出し側はこれが true のときだけ
    /// 自フォームのツリーを再描画する。
    /// </summary>
    public bool Applied { get; private set; }

    public CreditBulkInputDialog(
        CreditDraftSession session,
        CreditBulkApplyService applyService,
        RolesRepository rolesRepo)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _applyService = applyService ?? throw new ArgumentNullException(nameof(applyService));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));

        InitializeComponent();

        // 250ms デバウンス：キーストローク連打時はパースを遅延させ、最後の入力から 250ms 静止したら実行する。
        _debounceTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _debounceTimer.Tick += OnDebounceTick;

        txtInput.TextChanged += OnInputChanged;
        btnApply.Click += async (_, __) => await OnApplyAsync();
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

        // 初回パース（空入力の状態でも UI を整える）
        RunParseAndRefresh();
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
                var nodeCard = treePreview.Nodes.Add($"カード {cardIdx}");

                int tierIdx = 0;
                foreach (var tier in card.Tiers)
                {
                    tierIdx++;
                    var nodeTier = nodeCard.Nodes.Add($"ティア {tierIdx}");

                    int groupIdx = 0;
                    foreach (var group in tier.Groups)
                    {
                        groupIdx++;
                        var nodeGroup = nodeTier.Nodes.Add($"グループ {groupIdx}");

                        foreach (var role in group.Roles)
                        {
                            // 解決済みなら role_code を表示、未解決なら表示名そのまま。
                            string roleLabel = role.ResolvedRoleCode is null
                                ? $"[未解決] {role.DisplayName}"
                                : $"{role.DisplayName} ({role.ResolvedRoleCode})";
                            var nodeRole = nodeGroup.Nodes.Add(roleLabel);

                            int blockIdx = 0;
                            foreach (var block in role.Blocks)
                            {
                                blockIdx++;
                                string blockLabel = block.LeadingCompanyText is null
                                    ? $"ブロック {blockIdx} (col={block.ColCount})"
                                    : $"ブロック {blockIdx} [{block.LeadingCompanyText}] (col={block.ColCount})";
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

    /// <summary>1 エントリの表示文字列を組み立てる。</summary>
    private static string FormatEntryLabel(ParsedEntry e)
    {
        return e.Kind switch
        {
            ParsedEntryKind.CharacterVoice =>
                $"🎤 <{(e.IsForcedNewCharacter ? "*" : "")}{e.CharacterRawText}> {e.PersonRawText}"
                + (string.IsNullOrEmpty(e.AffiliationRawText) ? "" : $" ({e.AffiliationRawText})"),
            ParsedEntryKind.Company =>
                $"🏢 [{e.CompanyRawText}]",
            ParsedEntryKind.Logo =>
                $"🖼 {e.RawText}",
            ParsedEntryKind.Text =>
                $"📝 {e.RawText}",
            _ =>
                $"👤 {e.PersonRawText}"
                + (string.IsNullOrEmpty(e.AffiliationRawText) ? "" : $" ({e.AffiliationRawText})")
        };
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
            string? user = Environment.UserName;
            await _applyService.ApplyToDraftAsync(_lastParse, _session, user).ConfigureAwait(true);

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
