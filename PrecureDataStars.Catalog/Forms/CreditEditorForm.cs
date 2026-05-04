using System;
using System.Collections.Generic;
using System.Drawing;
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
        lstCredits.SelectedIndexChanged += async (_, __) => await OnCreditSelectedAsync();

        // ── ツリー：選択時のプレビュー反映＋ボタン状態切替 ──
        treeStructure.AfterSelect += (_, __) => { OnTreeNodeSelected(); UpdateButtonStates(); };

        // ── v1.2.0 工程 B-2 追加：左ペインのクレジット系編集ボタン 3 個を結線 ──
        btnNewCredit.Click       += async (_, __) => await OnNewCreditAsync();
        btnSaveCreditProps.Click += async (_, __) => await OnSaveCreditPropsAsync();
        btnDeleteCredit.Click    += async (_, __) => await OnDeleteCreditAsync();

        // ── v1.2.0 工程 B-2 追加：中央ペインのツリー編集ボタン 6 個を結線 ──
        // （Entry の追加は B-3 で扱うため btnAddEntry はここでは結線しない）
        btnAddCard.Click    += async (_, __) => await OnAddCardAsync();
        btnAddRole.Click    += async (_, __) => await OnAddRoleAsync();
        btnAddBlock.Click   += async (_, __) => await OnAddBlockAsync();
        btnMoveUp.Click     += async (_, __) => await OnMoveAsync(up: true);
        btnMoveDown.Click   += async (_, __) => await OnMoveAsync(up: false);
        btnDeleteNode.Click += async (_, __) => await OnDeleteNodeAsync();

        // ── v1.2.0 工程 B-2 追加：TreeView の DnD 並べ替えイベント ──
        // ItemDrag でドラッグ開始、DragOver で同階層内かつ Card/Role/Block であることを判定、
        // DragDrop で実際の seq 値再採番を実行する。Entry ノードはドラッグ不可。
        treeStructure.ItemDrag  += OnTreeItemDrag;
        treeStructure.DragEnter += OnTreeDragEnter;
        treeStructure.DragOver  += OnTreeDragOver;
        treeStructure.DragDrop  += async (s, e) => await OnTreeDragDropAsync(s, e);

        // ── v1.2.0 工程 B-2 修正：フォームリサイズ時に右ペイン幅を 380 固定で追随させる ──
        // splitCenterRight.FixedPanel = Panel2 にしているため、フォームを横に伸ばしたら
        // 中央ペインだけが広がる挙動を維持する。ただしフォームを縮めて中央ペインが
        // Panel1MinSize に達した場合は自然と SplitContainer 側で停止する。
        Resize += (_, __) => ApplySplitterDistances();

        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>初期ロード：シリーズ一覧と part_type コンボの初期化、初回クレジット一覧表示。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            // v1.2.0 工程 B-2 修正：SplitContainer の SplitterDistance を、
            // フォームの Width / Height が確定したこのタイミングで動的に設定する。
            // Designer 側の初期化子で SplitterDistance を入れるとフォーム幅未確定で
            // 値が無視される（Panel1MinSize に丸められる）ため、ここでセットすることで
            // 「左 320 / 中央 残り / 右 380」のバランスを起動時から確実にする。
            ApplySplitterDistances();

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
            // v1.2.0 工程 B-2: 初期表示時のボタン状態（クレジット未選択 = ほとんど無効）
            UpdateButtonStates();
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
    /// scope_kind と series_id / episode_id だけで絞り込む（v1.2.0 工程 B' 再修正で
    /// 本放送限定フラグはエントリ単位に移管したため、クレジット側の絞り込み条件には含めない）。
    /// </summary>
    private async Task ReloadCreditsAsync()
    {
        try
        {
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

            lstCredits.DisplayMember = "Label";
            lstCredits.ValueMember = "Credit";
            lstCredits.DataSource = credits
                .Select(c => new CreditListItem(c, BuildCreditListLabel(c)))
                .ToList();

            if (credits.Count == 0)
            {
                _currentCredit = null;
                ClearTreeAndPreview();
                lblStatusBar.Text = "現在編集中: （該当クレジットなし）";
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>クレジットリストボックスのラベルを生成。</summary>
    private static string BuildCreditListLabel(Credit c)
        => $"#{c.CreditId}  {c.CreditKind}  ({c.Presentation})";

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

            // v1.2.0 工程 B-2: クレジット選択直後はツリー上にノード未選択なので、
            // クレジットレベルのボタン（左ペイン）と「+ カード」だけが有効。
            UpdateButtonStates();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>ステータスバー文字列を更新する（シリーズ／エピソード／OP-ED）。</summary>
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
        lblStatusBar.Text =
            $"現在編集中: {scope} {idLabel}  /  {_currentCredit.CreditKind}  ({_currentCredit.Presentation})";
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

    // ============================================================
    // v1.2.0 工程 B-2 追加：ボタン状態管理 / クレジット CRUD / ツリー編集 / DnD
    // ============================================================

    /// <summary>
    /// ツリー上の選択ノード種別とクレジット選択状態に応じて、編集ボタンの Enabled を切り替える。
    /// 選択ノード種別 → 有効化されるボタンの対応表は <c>CreditEditorForm</c> のドキュメント参照。
    /// Entry ノードを選択した場合の Entry 編集系（btnAddEntry / btnSaveEntry / btnDeleteEntry /
    /// Entry の ↑↓ ↓ × 削除）は工程 B-3 で扱うため、本メソッドでは無効のまま。
    /// </summary>
    private void UpdateButtonStates()
    {
        bool hasCredit = (_currentCredit is not null);
        // クレジット系：左ペインのボタン
        btnNewCredit.Enabled = true;                  // クレジットがなくても新規作成可
        btnSaveCreditProps.Enabled = hasCredit;
        btnDeleteCredit.Enabled = hasCredit;

        if (!hasCredit)
        {
            btnAddCard.Enabled = btnAddRole.Enabled = btnAddBlock.Enabled = false;
            btnMoveUp.Enabled = btnMoveDown.Enabled = false;
            btnDeleteNode.Enabled = false;
            // Entry 系（B-3 担当）は引き続き無効
            btnAddEntry.Enabled = btnSaveEntry.Enabled = btnDeleteEntry.Enabled = false;
            return;
        }

        // ツリー操作ボタン：選択ノード種別で切替
        var tag = treeStructure.SelectedNode?.Tag as NodeTag;
        switch (tag?.Kind)
        {
            case NodeKind.Card:
                btnAddCard.Enabled = true;
                btnAddRole.Enabled = true;
                btnAddBlock.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.CardRole:
                btnAddCard.Enabled = true;
                btnAddRole.Enabled = true;
                btnAddBlock.Enabled = true;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Block:
                btnAddCard.Enabled = true;
                btnAddRole.Enabled = false;
                btnAddBlock.Enabled = true;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Entry:
                // Entry の追加・並べ替え・削除は B-3 担当
                btnAddCard.Enabled = true;
                btnAddRole.Enabled = false;
                btnAddBlock.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = false;
                btnDeleteNode.Enabled = false;
                break;
            default:
                // 何も選択されていない（クレジットだけ選択されている状態）
                btnAddCard.Enabled = true;
                btnAddRole.Enabled = false;
                btnAddBlock.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = false;
                btnDeleteNode.Enabled = false;
                break;
        }
        // Entry 系編集ボタンは B-3 担当なので常に無効
        btnAddEntry.Enabled = btnSaveEntry.Enabled = btnDeleteEntry.Enabled = false;
    }

    // ────────────────────────────────────────────────────────────
    // クレジット CRUD（左ペイン）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// 新規クレジット作成：<see cref="Dialogs.CreditNewDialog"/> でユーザーに OP/ED ・
    /// presentation・本放送限定フラグ・part_type・notes を入力してもらい、
    /// <see cref="CreditsRepository.InsertAsync"/> で INSERT、ListBox を再読み込みして新規行を選択。
    /// UNIQUE 衝突（同 scope/フラグ/credit_kind が既に存在）は呼び出し側で catch して案内する。
    /// </summary>
    private async Task OnNewCreditAsync()
    {
        try
        {
            // scope_kind / series_id / episode_id を現在の左ペイン状態から決定
            string scope = rbScopeSeries.Checked ? "SERIES" : "EPISODE";
            int? seriesId = null;
            int? episodeId = null;
            string targetLabel;
            if (scope == "SERIES")
            {
                if (cboSeries.SelectedValue is not int sid)
                { MessageBox.Show(this, "シリーズを選択してください。"); return; }
                seriesId = sid;
                targetLabel = (cboSeries.SelectedItem as IdLabel)?.Label ?? $"シリーズ #{sid}";
            }
            else
            {
                if (cboEpisode.SelectedValue is not int eid)
                { MessageBox.Show(this, "エピソードを選択してください。"); return; }
                episodeId = eid;
                targetLabel = (cboEpisode.SelectedItem as IdLabel)?.Label ?? $"エピソード #{eid}";
            }

            using var dlg = new Dialogs.CreditNewDialog(_partTypesRepo, scope, seriesId, episodeId, targetLabel);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result is null) return;

            int newCreditId;
            try
            {
                newCreditId = await _creditsRepo.InsertAsync(dlg.Result);
            }
            catch (MySqlConnector.MySqlException mex) when (mex.Number == 1062)
            {
                // 1062 = Duplicate entry: UNIQUE 衝突
                MessageBox.Show(this,
                    "同じスコープ・OP/ED 区分のクレジットが既に存在します。\n" +
                    "（既存クレジットのプロパティを編集してください）",
                    "重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // ListBox を再読込し、新規クレジットを選択状態に
            await ReloadCreditsAsync();
            SelectCreditInListBox(newCreditId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// クレジットプロパティ保存：左ペインの presentation / part_type / notes を反映して
    /// <see cref="CreditsRepository.UpdateAsync"/> を呼ぶ。
    /// IsBroadcastOnly / CreditKind / scope 系は変えない（変える場合は別行への移し替えになる
    /// ため、専用の操作で行うべきという設計判断。B-2 では非対応）。
    /// </summary>
    private async Task OnSaveCreditPropsAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            _currentCredit.Presentation = rbPresentationCards.Checked ? "CARDS" : "ROLL";
            _currentCredit.PartType = (cboPartType.SelectedValue as string) is { Length: > 0 } code ? code : null;
            _currentCredit.Notes = string.IsNullOrWhiteSpace(txtCreditNotes.Text) ? null : txtCreditNotes.Text.Trim();
            _currentCredit.UpdatedBy = Environment.UserName;

            await _creditsRepo.UpdateAsync(_currentCredit);
            await UpdateStatusBarAsync();
            // ListBox の表示も updated 後の値に追随させる（presentation を変えたら反映される）
            int keepId = _currentCredit.CreditId;
            await ReloadCreditsAsync();
            SelectCreditInListBox(keepId);
            MessageBox.Show(this, "クレジットプロパティを保存しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// クレジット削除（論理削除）：<see cref="CreditsRepository.SoftDeleteAsync"/> で
    /// is_deleted=1 を立てる。配下のカード／役職／ブロック／エントリは物理削除しない
    /// （データが見えなくなるだけで残す）。
    /// </summary>
    private async Task OnDeleteCreditAsync()
    {
        try
        {
            if (_currentCredit is null) return;
            var msg = $"クレジット #{_currentCredit.CreditId} {_currentCredit.CreditKind} を論理削除します。\n" +
                      "（is_deleted=1 を立てるだけで、配下のカード／役職／ブロック／エントリは物理削除されません）";
            if (MessageBox.Show(this, msg, "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            await _creditsRepo.SoftDeleteAsync(_currentCredit.CreditId, Environment.UserName);
            _currentCredit = null;
            await ReloadCreditsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>ListBox から指定 credit_id の行を選択状態にする補助メソッド。</summary>
    private void SelectCreditInListBox(int creditId)
    {
        if (lstCredits.DataSource is not List<CreditListItem> items) return;
        int idx = items.FindIndex(x => x.Credit.CreditId == creditId);
        if (idx >= 0) lstCredits.SelectedIndex = idx;
    }

    // ────────────────────────────────────────────────────────────
    // ツリー構造編集（中央ペイン）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// カード追加：選択中クレジットに新規カードを追加する。
    /// presentation=ROLL のクレジットでは「カードは 1 枚（card_seq=1）固定」のため、
    /// 既にカードが存在する場合は警告して中止。
    /// </summary>
    private async Task OnAddCardAsync()
    {
        try
        {
            if (_currentCredit is null) return;
            var existing = await _cardsRepo.GetByCreditAsync(_currentCredit.CreditId);
            if (_currentCredit.Presentation == "ROLL" && existing.Count > 0)
            {
                MessageBox.Show(this,
                    "presentation=ROLL のクレジットでは、カードは 1 枚（card_seq=1）固定です。\n" +
                    "複数カードが必要な場合は presentation を CARDS に変更してください。",
                    "操作不可", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            byte newSeq = (byte)(existing.Count + 1);
            var newCard = new CreditCard
            {
                CreditId = _currentCredit.CreditId,
                CardSeq = newSeq,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            int newCardId = await _cardsRepo.InsertAsync(newCard);
            await RebuildTreeAsync();
            SelectNodeById(NodeKind.Card, newCardId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 役職追加：<see cref="Pickers.RolePickerDialog"/> で role_code を選んで、
    /// 選択中 Card または選択中 Role と同じ Card にぶら下げる新規役職を作成する。
    /// 新 tier=1（既定）/ 新 order_in_tier = 同 card 内 tier=1 の最大 order + 1。
    /// </summary>
    private async Task OnAddRoleAsync()
    {
        try
        {
            if (_currentCredit is null) return;
            // 選択ノードから対象 card_id を取得（Card 選択時は自分、Role 選択時は親）
            int? cardId = ResolveTargetCardIdFromSelection();
            if (cardId is null)
            {
                MessageBox.Show(this, "Card または Role ノードを選択してから「+ 役職」を押してください。");
                return;
            }

            // 役職コードをピッカーで選んでもらう
            using var dlg = new Pickers.RolePickerDialog(_rolesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedRole is null) return;

            byte tier = 1;
            var existingInTier = (await _cardRolesRepo.GetByCardAsync(cardId.Value))
                .Where(r => r.Tier == tier).ToList();
            byte newOrder = (byte)(existingInTier.Count + 1);

            var newRole = new CreditCardRole
            {
                CardId = cardId.Value,
                RoleCode = dlg.SelectedRole.RoleCode,
                Tier = tier,
                OrderInTier = newOrder,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            int newCardRoleId = await _cardRolesRepo.InsertAsync(newRole);
            await RebuildTreeAsync();
            SelectNodeById(NodeKind.CardRole, newCardRoleId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// ブロック追加：選択中 Role または選択中 Block と同じ Role にぶら下げる新規ブロックを作成する。
    /// rows / cols は既定 1×1。新 block_seq = 同 card_role 内の最大 + 1。
    /// </summary>
    private async Task OnAddBlockAsync()
    {
        try
        {
            if (_currentCredit is null) return;
            int? cardRoleId = ResolveTargetCardRoleIdFromSelection();
            if (cardRoleId is null)
            {
                MessageBox.Show(this, "Role または Block ノードを選択してから「+ ブロック」を押してください。");
                return;
            }
            var existing = await _blocksRepo.GetByCardRoleAsync(cardRoleId.Value);
            byte newSeq = (byte)(existing.Count + 1);

            var newBlock = new CreditRoleBlock
            {
                CardRoleId = cardRoleId.Value,
                BlockSeq = newSeq,
                Rows = 1,
                Cols = 1,
                LeadingCompanyAliasId = null,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            int newBlockId = await _blocksRepo.InsertAsync(newBlock);
            await RebuildTreeAsync();
            SelectNodeById(NodeKind.Block, newBlockId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// ノード削除（B-2 では Card / Role / Block のみ）：選択ノード種別を判定して
    /// 該当リポジトリの DeleteAsync を呼ぶ。子要素は ON DELETE CASCADE で連動削除される。
    /// 削除確認ダイアログでは子要素件数を伝える。
    /// </summary>
    private async Task OnDeleteNodeAsync()
    {
        try
        {
            if (treeStructure.SelectedNode?.Tag is not NodeTag tag) return;
            if (tag.Kind == NodeKind.Entry)
            {
                MessageBox.Show(this, "Entry ノードの削除は工程 B-3 で対応します。");
                return;
            }

            int childCount = treeStructure.SelectedNode.Nodes.Count;
            string nodeName = tag.Kind switch
            {
                NodeKind.Card     => $"カード（{treeStructure.SelectedNode.Text}）",
                NodeKind.CardRole => $"役職（{treeStructure.SelectedNode.Text}）",
                NodeKind.Block    => $"ブロック（{treeStructure.SelectedNode.Text}）",
                _                 => "(不明)"
            };
            string warn = childCount > 0 ? $"\n※ 配下の {childCount} 件も連鎖削除されます。" : "";
            if (MessageBox.Show(this,
                $"{nodeName} を削除します。{warn}",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            switch (tag.Kind)
            {
                case NodeKind.Card:     await _cardsRepo.DeleteAsync(tag.Id);     break;
                case NodeKind.CardRole: await _cardRolesRepo.DeleteAsync(tag.Id); break;
                case NodeKind.Block:    await _blocksRepo.DeleteAsync(tag.Id);    break;
            }
            await RebuildTreeAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────
    // 並べ替え（ボタン式 ↑↓）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// ↑↓ ボタンによる並べ替え：選択ノードと同階層の兄弟リストを取得し、
    /// 指定方向に 1 つずらしてリポジトリの BulkUpdateSeqAsync で一括 UPDATE する。
    /// Entry の並べ替えは工程 B-3 担当のため本メソッドは Card / Role / Block のみ。
    /// </summary>
    private async Task OnMoveAsync(bool up)
    {
        try
        {
            if (treeStructure.SelectedNode?.Tag is not NodeTag tag) return;
            int keepId = tag.Id;

            switch (tag.Kind)
            {
                case NodeKind.Card:
                {
                    if (_currentCredit is null) return;
                    var list = (await _cardsRepo.GetByCreditAsync(_currentCredit.CreditId))
                        .OrderBy(c => c.CardSeq).ToList();
                    int idx = list.FindIndex(c => c.CardId == tag.Id);
                    bool moved = up ? SeqReorderHelper.MoveUp(list, idx) : SeqReorderHelper.MoveDown(list, idx);
                    if (!moved) return;
                    await SeqReorderHelper.ReorderCardsAsync(_cardsRepo, _currentCredit.CreditId, list);
                    break;
                }
                case NodeKind.CardRole:
                {
                    var node = treeStructure.SelectedNode;
                    if (node.Parent?.Tag is not NodeTag pt || pt.Kind != NodeKind.Card) return;
                    int cardId = pt.Id;
                    var orig = (CreditCardRole)tag.Payload;
                    // 同 tier 内のみで並べ替え（仕様上 tier をまたぐ移動は B-2 ではサポートしない）
                    var sameTier = (await _cardRolesRepo.GetByCardAsync(cardId))
                        .Where(r => r.Tier == orig.Tier)
                        .OrderBy(r => r.OrderInTier).ToList();
                    int idx = sameTier.FindIndex(r => r.CardRoleId == tag.Id);
                    bool moved = up ? SeqReorderHelper.MoveUp(sameTier, idx) : SeqReorderHelper.MoveDown(sameTier, idx);
                    if (!moved) return;
                    await SeqReorderHelper.ReorderCardRolesInTierAsync(_cardRolesRepo, cardId, orig.Tier, sameTier);
                    break;
                }
                case NodeKind.Block:
                {
                    var node = treeStructure.SelectedNode;
                    if (node.Parent?.Tag is not NodeTag pt || pt.Kind != NodeKind.CardRole) return;
                    int cardRoleId = pt.Id;
                    var list = (await _blocksRepo.GetByCardRoleAsync(cardRoleId))
                        .OrderBy(b => b.BlockSeq).ToList();
                    int idx = list.FindIndex(b => b.BlockId == tag.Id);
                    bool moved = up ? SeqReorderHelper.MoveUp(list, idx) : SeqReorderHelper.MoveDown(list, idx);
                    if (!moved) return;
                    await SeqReorderHelper.ReorderRoleBlocksAsync(_blocksRepo, cardRoleId, list);
                    break;
                }
                default:
                    return;
            }
            await RebuildTreeAsync();
            SelectNodeById(tag.Kind, keepId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────
    // 並べ替え（DnD）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// ItemDrag：ノード上でマウスドラッグが始まった時、対象ノードが Card/Role/Block のいずれかなら
    /// DoDragDrop で Move 操作を開始する。Entry ノードはドラッグ不可。
    /// </summary>
    private void OnTreeItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not TreeNode node) return;
        if (node.Tag is not NodeTag tag) return;
        if (tag.Kind == NodeKind.Entry) return; // Entry の DnD は B-3 担当
        treeStructure.DoDragDrop(node, DragDropEffects.Move);
    }

    /// <summary>DragEnter：TreeNode が運ばれてきた場合のみ Move を許可する。</summary>
    private void OnTreeDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true
            ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>
    /// DragOver：マウス位置のノードを取得し、ドラッグ元と「同じ親（同階層）」かつ
    /// 同じ NodeKind であればドロップを許可する。それ以外は無効。
    /// 同 tier 内のみ並べ替え可（CardRole の場合）の判定もここで行う。
    /// </summary>
    private void OnTreeDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode src) { e.Effect = DragDropEffects.None; return; }
        var pt = treeStructure.PointToClient(new Point(e.X, e.Y));
        var target = treeStructure.GetNodeAt(pt);
        if (target is null || target == src || target.Parent != src.Parent)
        { e.Effect = DragDropEffects.None; return; }
        if (src.Tag is not NodeTag st || target.Tag is not NodeTag tt || st.Kind != tt.Kind)
        { e.Effect = DragDropEffects.None; return; }

        // CardRole の場合は同 tier 内のみ
        if (st.Kind == NodeKind.CardRole &&
            st.Payload is CreditCardRole sr && tt.Payload is CreditCardRole tr &&
            sr.Tier != tr.Tier)
        { e.Effect = DragDropEffects.None; return; }

        e.Effect = DragDropEffects.Move;
        // ホットトラック：ドロップ可能ターゲットをハイライト
        treeStructure.SelectedNode = target;
    }

    /// <summary>
    /// DragDrop：ドラッグ元を「ドロップ位置」へ移動して、同階層の全要素を seq=1,2,...
    /// で再採番する。ドロップ位置はノード矩形の上半分なら直前、下半分なら直後と判定。
    /// </summary>
    private async Task OnTreeDragDropAsync(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode src) return;
            var pt = treeStructure.PointToClient(new Point(e.X, e.Y));
            var target = treeStructure.GetNodeAt(pt);
            if (target is null || target == src || target.Parent != src.Parent) return;
            if (src.Tag is not NodeTag st || target.Tag is not NodeTag tt || st.Kind != tt.Kind) return;

            bool dropAbove = (pt.Y < target.Bounds.Y + target.Bounds.Height / 2);

            switch (st.Kind)
            {
                case NodeKind.Card:
                {
                    if (_currentCredit is null) return;
                    var list = (await _cardsRepo.GetByCreditAsync(_currentCredit.CreditId))
                        .OrderBy(c => c.CardSeq).ToList();
                    var srcCard = list.First(c => c.CardId == st.Id);
                    list.Remove(srcCard);
                    int targetIdx = list.FindIndex(c => c.CardId == tt.Id);
                    if (targetIdx < 0) return;
                    list.Insert(dropAbove ? targetIdx : targetIdx + 1, srcCard);
                    await SeqReorderHelper.ReorderCardsAsync(_cardsRepo, _currentCredit.CreditId, list);
                    break;
                }
                case NodeKind.CardRole:
                {
                    if (src.Parent?.Tag is not NodeTag pt2 || pt2.Kind != NodeKind.Card) return;
                    int cardId = pt2.Id;
                    var srcRole = (CreditCardRole)st.Payload;
                    var sameTier = (await _cardRolesRepo.GetByCardAsync(cardId))
                        .Where(r => r.Tier == srcRole.Tier)
                        .OrderBy(r => r.OrderInTier).ToList();
                    var srcEntity = sameTier.First(r => r.CardRoleId == st.Id);
                    sameTier.Remove(srcEntity);
                    int targetIdx = sameTier.FindIndex(r => r.CardRoleId == tt.Id);
                    if (targetIdx < 0) return;
                    sameTier.Insert(dropAbove ? targetIdx : targetIdx + 1, srcEntity);
                    await SeqReorderHelper.ReorderCardRolesInTierAsync(_cardRolesRepo, cardId, srcRole.Tier, sameTier);
                    break;
                }
                case NodeKind.Block:
                {
                    if (src.Parent?.Tag is not NodeTag pt2 || pt2.Kind != NodeKind.CardRole) return;
                    int cardRoleId = pt2.Id;
                    var list = (await _blocksRepo.GetByCardRoleAsync(cardRoleId))
                        .OrderBy(b => b.BlockSeq).ToList();
                    var srcBlock = list.First(b => b.BlockId == st.Id);
                    list.Remove(srcBlock);
                    int targetIdx = list.FindIndex(b => b.BlockId == tt.Id);
                    if (targetIdx < 0) return;
                    list.Insert(dropAbove ? targetIdx : targetIdx + 1, srcBlock);
                    await SeqReorderHelper.ReorderRoleBlocksAsync(_blocksRepo, cardRoleId, list);
                    break;
                }
                default:
                    return;
            }
            int keepId = st.Id;
            NodeKind keepKind = st.Kind;
            await RebuildTreeAsync();
            SelectNodeById(keepKind, keepId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────
    // 補助：ノード選択 / 親 ID 解決
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// ツリーから指定種別＋ID のノードを再帰検索して選択状態にする。
    /// 並べ替え／追加／削除後にユーザーが見失わないよう元の位置に戻す用途。
    /// </summary>
    private void SelectNodeById(NodeKind kind, int id)
    {
        TreeNode? Find(TreeNodeCollection nodes)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.Tag is NodeTag t && t.Kind == kind && t.Id == id) return n;
                var deep = Find(n.Nodes);
                if (deep is not null) return deep;
            }
            return null;
        }
        var found = Find(treeStructure.Nodes);
        if (found is not null)
        {
            treeStructure.SelectedNode = found;
            found.EnsureVisible();
        }
    }

    /// <summary>
    /// 選択ノードから「役職追加先となる Card の card_id」を解決する。
    /// Card 選択時 → そのノード自身、Role 選択時 → 親 Card、Block 選択時 → 祖父 Card。
    /// 該当しない選択状態（Entry など）の場合は null。
    /// </summary>
    private int? ResolveTargetCardIdFromSelection()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag t && t.Kind == NodeKind.Card) return t.Id;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// 選択ノードから「ブロック追加先となる Role の card_role_id」を解決する。
    /// Role 選択時 → そのノード自身、Block 選択時 → 親 Role、Entry 選択時 → 祖父 Role。
    /// </summary>
    private int? ResolveTargetCardRoleIdFromSelection()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag t && t.Kind == NodeKind.CardRole) return t.Id;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// 3 ペインのスプリッター位置を、現在のフォーム幅から計算して設定する
    /// （v1.2.0 工程 B-2 修正）。
    /// <para>
    /// 「左 320 / 右 380 / 中央 = 残り」の方針で固定する。SplitterDistance は
    /// Panel1 の幅を表すため、splitMain は左ペイン幅 320 を直接渡し、
    /// splitCenterRight は中央ペイン幅 = (splitMain.Panel2.Width - 右ペイン幅) で計算する。
    /// </para>
    /// <para>
    /// 計算結果が Panel1MinSize / Panel2MinSize の制約に違反する場合は、
    /// SplitContainer 側で自動的にクランプされるため、本メソッドでは特別な
    /// 例外処理は行わない。例えばフォームを極端に細くした場合、中央ペインは
    /// Panel1MinSize（540）まで縮み、それ以上はフォームが MinimumSize に阻まれる。
    /// </para>
    /// </summary>
    private void ApplySplitterDistances()
    {
        const int leftWidth = 320;
        const int rightWidth = 380;

        try
        {
            // splitMain: 左ペイン幅 = 320 px
            if (splitMain.Width > leftWidth + splitMain.Panel2MinSize)
            {
                splitMain.SplitterDistance = leftWidth;
            }

            // splitCenterRight: 中央ペイン幅 = 残り全体から右 380 を引いた値
            int centerWidth = splitMain.Panel2.Width - rightWidth - splitCenterRight.SplitterWidth;
            if (centerWidth > splitCenterRight.Panel1MinSize &&
                centerWidth < splitCenterRight.Width - splitCenterRight.Panel2MinSize)
            {
                splitCenterRight.SplitterDistance = centerWidth;
            }
        }
        catch (InvalidOperationException)
        {
            // 起動直後など SplitContainer の Width が確定していないタイミングで呼ばれた場合の保険。
            // 実害がないので静かにスキップ（次の Resize / Load で再試行される）。
        }
    }
}
