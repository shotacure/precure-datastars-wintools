using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット本体編集フォーム（v1.2.0 工程 B 新設、本ファイルは B-1：表示のみの段階）。
/// <para>
/// 3 ペイン構成：
/// <list type="bullet">
///   <item><description>左ペイン：scope_kind / シリーズ / エピソード / release_context によるクレジット選択、
///   選択中クレジットのプロパティ表示。</description></item>
///   <item><description>中央ペイン：Card → Role → Block → Entry の 4 階層構造を TreeView で表示。
///   エントリは種別ごとに色とプレフィックス（[PERSON]/[COMPANY] 等）で識別される。</description></item>
///   <item><description>右ペイン：選択中エントリの種別とプレビュー文字列を表示（B-1 では編集不可）。</description></item>
/// </list>
/// </para>
/// <para>
/// 工程 B-1 では編集機能は無効化されており、構造ツリーは read-only。クレジット本体への
/// CRUD は B-2（カード／役職／ブロック／エントリの追加・並べ替え・削除）と
/// B-3（エントリ編集 UI と「+ 新規...」によるマスタ自動投入）で順次追加される。
/// </para>
/// </summary>
public partial class CreditEditorForm : Form
{
    // ── クレジット本体構造に必要なリポジトリ ──
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditCardsRepository _cardsRepo;
    private readonly CreditCardRolesRepository _cardRolesRepo;
    private readonly CreditRoleBlocksRepository _blocksRepo;
    private readonly CreditBlockEntriesRepository _entriesRepo;

    // ── 関連マスタ（クレジット選択・プレビュー描画に必要） ──
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly PartTypesRepository _partTypesRepo;

    // ── プレビュー文字列を組み立てるためのマスタ参照 ──
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly SongRecordingsRepository _songRecRepo;

    /// <summary>
    /// 現在 ListBox で選択しているクレジット。null なら未選択。
    /// </summary>
    private Credit? _currentCredit;

    /// <summary>
    /// プレビュー文字列を組み立てる際のマスタ参照キャッシュ
    /// （何度も DB を引かないようツリー再構築のスコープ内で使い回す）。
    /// </summary>
    private readonly LookupCache _lookupCache;

    /// <summary>
    /// クレジット編集フォームを生成する。Program.cs の DI 経由で各リポジトリを受け取る。
    /// </summary>
    public CreditEditorForm(
        CreditsRepository creditsRepo,
        CreditCardsRepository cardsRepo,
        CreditCardRolesRepository cardRolesRepo,
        CreditRoleBlocksRepository blocksRepo,
        CreditBlockEntriesRepository entriesRepo,
        SeriesRepository seriesRepo,
        EpisodesRepository episodesRepo,
        RolesRepository rolesRepo,
        PartTypesRepository partTypesRepo,
        PersonAliasesRepository personAliasesRepo,
        CompanyAliasesRepository companyAliasesRepo,
        LogosRepository logosRepo,
        CharacterAliasesRepository characterAliasesRepo,
        SongRecordingsRepository songRecRepo)
    {
        _creditsRepo = creditsRepo ?? throw new ArgumentNullException(nameof(creditsRepo));
        _cardsRepo = cardsRepo ?? throw new ArgumentNullException(nameof(cardsRepo));
        _cardRolesRepo = cardRolesRepo ?? throw new ArgumentNullException(nameof(cardRolesRepo));
        _blocksRepo = blocksRepo ?? throw new ArgumentNullException(nameof(blocksRepo));
        _entriesRepo = entriesRepo ?? throw new ArgumentNullException(nameof(entriesRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _logosRepo = logosRepo ?? throw new ArgumentNullException(nameof(logosRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _songRecRepo = songRecRepo ?? throw new ArgumentNullException(nameof(songRecRepo));

        _lookupCache = new LookupCache(
            _personAliasesRepo, _companyAliasesRepo, _logosRepo,
            _characterAliasesRepo, _songRecRepo, _rolesRepo);

        InitializeComponent();

        // ── 左ペイン：選択コンボのイベント結線 ──
        rbScopeSeries.CheckedChanged  += async (_, __) => await OnScopeChangedAsync();
        rbScopeEpisode.CheckedChanged += async (_, __) => await OnScopeChangedAsync();
        cboSeries.SelectedIndexChanged += async (_, __) => await OnSeriesChangedAsync();
        cboEpisode.SelectedIndexChanged += async (_, __) => await ReloadCreditsAsync();
        chkShowBroadcastOnly.CheckedChanged += async (_, __) => await ReloadCreditsAsync();
        lstCredits.SelectedIndexChanged += async (_, __) => await OnCreditSelectedAsync();

        // ── ツリー：選択時のプレビュー反映 ──
        treeStructure.AfterSelect += (_, __) => OnTreeNodeSelected();

        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>初期ロード：シリーズ一覧と part_type コンボの初期化、初回クレジット一覧表示。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var allSeries = await _seriesRepo.GetAllAsync();
            cboSeries.DisplayMember = "Label";
            cboSeries.ValueMember = "Id";
            cboSeries.DataSource = allSeries
                .Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}"))
                .ToList();

            // part_type コンボ（NULL 表現の "" を先頭に置く）
            var pts = await _partTypesRepo.GetAllAsync();
            var ptItems = new List<CodeLabel> { new CodeLabel("", "（規定位置）") };
            ptItems.AddRange(pts.Select(p => new CodeLabel(p.PartTypeCode, $"{p.PartTypeCode}  {p.NameJa}")));
            cboPartType.DisplayMember = "Label";
            cboPartType.ValueMember = "Code";
            cboPartType.DataSource = ptItems;

            // SelectedIndex 連動を起動するために選択を再セット
            await OnScopeChangedAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>scope=SERIES 時はエピソードコンボを無効化。</summary>
    private async Task OnScopeChangedAsync()
    {
        cboEpisode.Enabled = rbScopeEpisode.Checked;
        if (rbScopeSeries.Checked)
        {
            // SERIES スコープではエピソードは選択不要だが、コンボの状態は前回値を残す
            await ReloadCreditsAsync();
        }
        else
        {
            await OnSeriesChangedAsync();
        }
    }

    /// <summary>シリーズ変更時：エピソードコンボを更新し、クレジット一覧を再読込。</summary>
    private async Task OnSeriesChangedAsync()
    {
        try
        {
            if (cboSeries.SelectedValue is not int seriesId) return;
            var eps = await _episodesRepo.GetBySeriesAsync(seriesId);
            cboEpisode.DisplayMember = "Label";
            cboEpisode.ValueMember = "Id";
            cboEpisode.DataSource = eps
                .Select(e => new IdLabel(e.EpisodeId, $"第{e.SeriesEpNo}話  {e.TitleText}"))
                .ToList();
            await ReloadCreditsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// クレジット一覧を絞り込み条件で再読込し、ListBox に流し込む。
    /// 既定（チェックボックス OFF）では全媒体共通行（is_broadcast_only=0）のみを表示。
    /// チェックボックスを ON にすると本放送限定行（is_broadcast_only=1）も併せて表示する。
    /// </summary>
    private async Task ReloadCreditsAsync()
    {
        try
        {
            bool showBroadcastOnly = chkShowBroadcastOnly.Checked;
            IReadOnlyList<Credit> credits;
            if (rbScopeSeries.Checked)
            {
                if (cboSeries.SelectedValue is not int seriesId) { lstCredits.DataSource = null; return; }
                credits = await _creditsRepo.GetBySeriesAsync(seriesId);
            }
            else
            {
                if (cboEpisode.SelectedValue is not int episodeId) { lstCredits.DataSource = null; return; }
                credits = await _creditsRepo.GetByEpisodeAsync(episodeId);
            }
            // チェックボックスが OFF なら全媒体共通行のみ。ON なら全行表示。
            var filtered = showBroadcastOnly
                ? credits.ToList()
                : credits.Where(c => !c.IsBroadcastOnly).ToList();

            lstCredits.DisplayMember = "Label";
            lstCredits.ValueMember = "Credit";
            lstCredits.DataSource = filtered
                .Select(c => new CreditListItem(c, BuildCreditListLabel(c)))
                .ToList();

            if (filtered.Count == 0)
            {
                _currentCredit = null;
                ClearTreeAndPreview();
                lblStatusBar.Text = "現在編集中: （該当クレジットなし）";
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// クレジットリストボックスのラベルを生成。本放送限定行は明示的に [本放送限定] を付ける。
    /// </summary>
    private static string BuildCreditListLabel(Credit c)
    {
        string flag = c.IsBroadcastOnly ? " [本放送限定]" : "";
        return $"#{c.CreditId}  {c.CreditKind}{flag}  ({c.Presentation})";
    }

    /// <summary>
    /// クレジット選択時：プロパティを左下に表示し、中央ツリーを構築する。
    /// </summary>
    private async Task OnCreditSelectedAsync()
    {
        try
        {
            if (lstCredits.SelectedItem is not CreditListItem item)
            {
                _currentCredit = null;
                ClearTreeAndPreview();
                return;
            }
            _currentCredit = item.Credit;

            // プロパティ反映（B-1 では read-only として表示するだけ）
            rbPresentationCards.Checked = (_currentCredit.Presentation == "CARDS");
            rbPresentationRoll.Checked  = (_currentCredit.Presentation == "ROLL");
            // PartType は NULL なら ""（規定位置）アイテムを選ぶ
            cboPartType.SelectedValue = _currentCredit.PartType ?? "";
            txtCreditNotes.Text = _currentCredit.Notes ?? "";

            // ステータスバー更新
            await UpdateStatusBarAsync();

            // 中央ペインのツリー再構築
            await RebuildTreeAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>ステータスバー文字列を更新する（シリーズ／エピソード／本放送フラグ／OP-ED）。</summary>
    private async Task UpdateStatusBarAsync()
    {
        if (_currentCredit is null)
        {
            lblStatusBar.Text = "現在編集中: （クレジット未選択）";
            return;
        }
        string scope = _currentCredit.ScopeKind == "SERIES" ? "シリーズ" : "エピソード";
        string idLabel;
        if (_currentCredit.ScopeKind == "SERIES" && _currentCredit.SeriesId.HasValue)
        {
            var s = await _seriesRepo.GetByIdAsync(_currentCredit.SeriesId.Value);
            idLabel = s is null ? $"series#{_currentCredit.SeriesId}" : $"{s.Title}";
        }
        else if (_currentCredit.EpisodeId.HasValue)
        {
            idLabel = (cboEpisode.SelectedItem as IdLabel)?.Label ?? $"episode#{_currentCredit.EpisodeId}";
        }
        else
        {
            idLabel = "(未指定)";
        }
        // 本放送限定フラグの表示。既定行は明示せず、フラグ 1 のときだけ目立たせる。
        string flagLabel = _currentCredit.IsBroadcastOnly ? "  [本放送限定]" : "";
        lblStatusBar.Text =
            $"現在編集中: {scope} {idLabel}{flagLabel}  /  {_currentCredit.CreditKind}  ({_currentCredit.Presentation})";
    }

    /// <summary>
    /// 選択中クレジットのカード／役職／ブロック／エントリを TreeView に再構築する。
    /// 各エントリは種別ごとに表示テキストの先頭にプレフィックス（[PERSON]/[COMPANY] 等）を付け、
    /// マスタを引いて人物名・企業名・キャラ名・曲名等のプレビュー文字列を併記する。
    /// </summary>
    private async Task RebuildTreeAsync()
    {
        if (_currentCredit is null) { ClearTreeAndPreview(); return; }

        treeStructure.BeginUpdate();
        try
        {
            treeStructure.Nodes.Clear();

            var cards = await _cardsRepo.GetByCreditAsync(_currentCredit.CreditId);
            foreach (var card in cards.OrderBy(c => c.CardSeq))
            {
                var cardNode = new TreeNode($"📂 Card #{card.CardSeq}{(string.IsNullOrEmpty(card.Notes) ? "" : "  " + card.Notes)}")
                {
                    Tag = new NodeTag(NodeKind.Card, card.CardId, card)
                };

                var roles = await _cardRolesRepo.GetByCardAsync(card.CardId);
                foreach (var role in roles.OrderBy(r => r.Tier).ThenBy(r => r.OrderInTier))
                {
                    string roleName = await _lookupCache.ResolveRoleNameAsync(role.RoleCode);
                    var roleNode = new TreeNode($"📋 Role: {roleName}  (tier {role.Tier} / order {role.OrderInTier})")
                    {
                        Tag = new NodeTag(NodeKind.CardRole, role.CardRoleId, role)
                    };

                    var blocks = await _blocksRepo.GetByCardRoleAsync(role.CardRoleId);
                    foreach (var block in blocks.OrderBy(b => b.BlockSeq))
                    {
                        var blockNode = new TreeNode($"🔵 Block #{block.BlockSeq}  ({block.Rows}×{block.Cols})")
                        {
                            Tag = new NodeTag(NodeKind.Block, block.BlockId, block)
                        };

                        var entries = await _entriesRepo.GetByBlockAsync(block.BlockId);
                        foreach (var entry in entries.OrderBy(e => e.EntrySeq))
                        {
                            string preview = await _lookupCache.BuildEntryPreviewAsync(entry);
                            string prefix = entry.EntryKind switch
                            {
                                "PERSON"          => "🟢 [PERSON]         ",
                                "CHARACTER_VOICE" => "🟣 [CHARACTER_VOICE]",
                                "COMPANY"         => "🟠 [COMPANY]        ",
                                "LOGO"            => "🟡 [LOGO]           ",
                                "SONG"            => "🔵 [SONG]           ",
                                "TEXT"            => "⚪ [TEXT]            ",
                                _                 => "❓ [UNKNOWN]        "
                            };
                            var entryNode = new TreeNode($"{prefix} #{entry.EntrySeq}  {preview}")
                            {
                                Tag = new NodeTag(NodeKind.Entry, entry.EntryId, entry)
                            };
                            blockNode.Nodes.Add(entryNode);
                        }
                        roleNode.Nodes.Add(blockNode);
                    }
                    cardNode.Nodes.Add(roleNode);
                }
                treeStructure.Nodes.Add(cardNode);
            }
            treeStructure.ExpandAll();
        }
        finally
        {
            treeStructure.EndUpdate();
        }

        // 右ペインはクリア
        ClearEntryPreview();
    }

    /// <summary>
    /// ツリー上で選択されたノードを右ペインのプレビューに反映する。
    /// Entry 以外のノードを選択した場合、右ペインは「（エントリではありません）」表示にする。
    /// </summary>
    private void OnTreeNodeSelected()
    {
        if (treeStructure.SelectedNode?.Tag is not NodeTag tag)
        {
            ClearEntryPreview();
            return;
        }
        if (tag.Kind != NodeKind.Entry || tag.Payload is not CreditBlockEntry e)
        {
            lblEntryKind.Text = $"（{tag.Kind}）";
            txtEntryPreview.Text = "エントリではありません。";
            return;
        }
        lblEntryKind.Text = e.EntryKind;
        // プレビュー文字列はキャッシュ経由で同期取得（既にツリー構築時にロード済みのはず）
        txtEntryPreview.Text = _lookupCache.LastPreviewFor(e.EntryId) ?? "(プレビュー未取得)";
    }

    /// <summary>ツリーと右ペインを空にする（クレジット未選択時）。</summary>
    private void ClearTreeAndPreview()
    {
        treeStructure.Nodes.Clear();
        ClearEntryPreview();
        lblStatusBar.Text = "現在編集中: （クレジット未選択）";
    }

    private void ClearEntryPreview()
    {
        lblEntryKind.Text = "（未選択）";
        txtEntryPreview.Text = "";
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ============================================================
    // 補助型
    // ============================================================

    /// <summary>ListBox 用の (id, label) ペア。</summary>
    private sealed class IdLabel
    {
        public int Id { get; }
        public string Label { get; }
        public IdLabel(int id, string label) { Id = id; Label = label; }
        public override string ToString() => Label;
    }

    /// <summary>part_type 用の (code, label) ペア（NULL を "" で表現する）。</summary>
    private sealed class CodeLabel
    {
        public string Code { get; }
        public string Label { get; }
        public CodeLabel(string code, string label) { Code = code; Label = label; }
        public override string ToString() => Label;
    }

    /// <summary>クレジットリストボックス用のペア。</summary>
    private sealed class CreditListItem
    {
        public Credit Credit { get; }
        public string Label { get; }
        public CreditListItem(Credit credit, string label) { Credit = credit; Label = label; }
        public override string ToString() => Label;
    }

    /// <summary>TreeView ノード種別。</summary>
    private enum NodeKind { Card, CardRole, Block, Entry }

    /// <summary>TreeNode.Tag に積む構造体（種別 + 主キー + 元エンティティ）。</summary>
    private sealed class NodeTag
    {
        public NodeKind Kind { get; }
        public int Id { get; }
        public object Payload { get; }
        public NodeTag(NodeKind kind, int id, object payload)
        { Kind = kind; Id = id; Payload = payload; }
    }
}
