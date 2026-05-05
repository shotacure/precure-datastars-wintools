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

    // v1.2.0 工程 B-3c 追加：QuickAdd ダイアログでマスタ自動投入に使うリポジトリ。
    // EntryEditorPanel.Initialize の追加引数として下流に流す。
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;

    // v1.2.0 工程 F 追加：キャラ名義 QuickAdd 用のリポジトリ。
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterKindsRepository _characterKindsRepo;

    // v1.2.0 工程 G 追加：Tier / Group 階層の実体テーブル用リポジトリ。
    private readonly CreditCardTiersRepository _cardTiersRepo;
    private readonly CreditCardGroupsRepository _cardGroupsRepo;

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
        SongRecordingsRepository songRecRepo,
        PersonsRepository personsRepo,
        CompaniesRepository companiesRepo,
        CharactersRepository charactersRepo,
        CharacterKindsRepository characterKindsRepo,
        CreditCardTiersRepository cardTiersRepo,
        CreditCardGroupsRepository cardGroupsRepo,
        // v1.2.0 工程 H 追加：役職テンプレ展開で episode_theme_songs JOIN するために
        // 直接 DB 接続を取れる IConnectionFactory を受け取る。
        PrecureDataStars.Data.Db.IConnectionFactory factory)
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
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterKindsRepo = characterKindsRepo ?? throw new ArgumentNullException(nameof(characterKindsRepo));
        _cardTiersRepo = cardTiersRepo ?? throw new ArgumentNullException(nameof(cardTiersRepo));
        _cardGroupsRepo = cardGroupsRepo ?? throw new ArgumentNullException(nameof(cardGroupsRepo));

        _lookupCache = new LookupCache(
            _personAliasesRepo, _companyAliasesRepo, _logosRepo,
            _characterAliasesRepo, _songRecRepo, _rolesRepo,
            factory ?? throw new ArgumentNullException(nameof(factory)));

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
        btnAddCard.Click    += async (_, __) => await OnAddCardAsync();
        // v1.2.0 工程 G 追加：「+ Tier」「+ Group」
        btnAddTier.Click    += async (_, __) => await OnAddTierAsync();
        btnAddGroup.Click   += async (_, __) => await OnAddGroupAsync();
        btnAddRole.Click    += async (_, __) => await OnAddRoleAsync();
        btnAddBlock.Click   += async (_, __) => await OnAddBlockAsync();
        btnMoveUp.Click     += async (_, __) => await OnMoveAsync(up: true);
        btnMoveDown.Click   += async (_, __) => await OnMoveAsync(up: false);
        btnDeleteNode.Click += async (_, __) => await OnDeleteNodeAsync();

        // ── v1.2.0 工程 B-3 追加：エントリ追加ボタンの結線 ──
        // 押下時：選択中の Block（または Entry の親 Block）配下に新規エントリ追加モードで
        // 右ペイン EntryEditorPanel を開く。INSERT は EntryEditorPanel 内の「保存」ボタンで実行。
        btnAddEntry.Click += async (_, __) => await OnAddEntryAsync();

        // ── v1.2.0 工程 B-3 追加：EntryEditorPanel からのイベント購読 ──
        // 保存／削除完了時にツリー再構築。EntryEditorPanel.Initialize(repo, lookupCache) は
        // OnLoadAsync の冒頭で実行する（OnLoadAsync が依存関係を全部ロードする責務）。
        entryEditor.EntrySaved   += async (_, __) => await OnEntryEditorChangedAsync(reselectLastEdited: true);
        entryEditor.EntryDeleted += async (_, __) => await OnEntryEditorChangedAsync(reselectLastEdited: false);

        // ── v1.2.0 工程 B-2 追加：TreeView の DnD 並べ替えイベント ──
        // ItemDrag でドラッグ開始、DragOver で同階層内であることを判定、
        // DragDrop で実際の seq 値再採番を実行する。
        // v1.2.0 工程 B-3 で Entry ノードもドラッグ可に拡張（ただし同 (block_id, is_broadcast_only) 内のみ）。
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
            ApplySplitterDistances();

            // v1.2.0 工程 B-3 追加：右ペインの EntryEditorPanel に依存性を流し込む。
            // LookupCache はクレジットツリー構築でも使うので、ここで生成して両者に共有させる。
            // v1.2.0 工程 B-3b でピッカー用のマスタリポジトリ 5 本を追加引数で渡す。
            // v1.2.0 工程 B-3c で QuickAdd 用のリポジトリ 2 本（Persons / Companies）を更に追加。
            // v1.2.0 工程 F でキャラ名義 QuickAdd 用 2 本（Characters / CharacterKinds）を更に追加。
            // v1.2.0 工程 H で SongRecordingsRepository を撤去（SONG エントリ種別の物理削除）。
            entryEditor.Initialize(
                _entriesRepo,
                _lookupCache,
                _personAliasesRepo,
                _companyAliasesRepo,
                _characterAliasesRepo,
                _logosRepo,
                _personsRepo,
                _companiesRepo,
                _charactersRepo,
                _characterKindsRepo);

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
    /// <remarks>
    /// v1.2.0 fix4: 並列実行による Tree.Nodes 重複追加を防ぐため、
    /// 「先にローカル List にすべての TreeNode を組み立てきる → 最後に同期セクションで
    /// Clear → AddRange → EndUpdate を一気に実行」パターンに書き換えた。
    /// 旧実装では Nodes.Clear() の直後から DB アクセスの await を伴う foreach が続くため、
    /// ボタン連打や AfterSelect イベント連鎖で複数の RebuildTreeAsync が並列に await されると、
    /// 互いの Clear と Add が交互に走って同じカードノードが Tree に複数追加される問題があった。
    /// 新実装は同期反映区間に await を含まないので、並列で呼ばれても各呼び出しが
    /// 完成形のツリーで上書きするだけになり、重複は生じない。
    /// </remarks>
    private async Task RebuildTreeAsync()
    {
        if (_currentCredit is null) { ClearTreeAndPreview(); return; }

        // ─── フェーズ 1: データ取得 + ローカルでツリー組み立て（この間 treeStructure には触らない）───
        var newRootNodes = new List<TreeNode>();
        var cards = await _cardsRepo.GetByCreditAsync(_currentCredit.CreditId);
        foreach (var card in cards.OrderBy(c => c.CardSeq))
        {
            var cardNode = new TreeNode($"📂 Card #{card.CardSeq}{(string.IsNullOrEmpty(card.Notes) ? "" : "  " + card.Notes)}")
            {
                Tag = new NodeTag(NodeKind.Card, card.CardId, card)
            };

            // v1.2.0 工程 G：Tier / Group が実体テーブル化されたため、ツリーは
            // credit_card_tiers / credit_card_groups から直接取得して組み立てる。
            // これにより「役職がゼロのブランク Tier / ブランク Group」も正しくノード表示される。
            var tiers  = await _cardTiersRepo.GetByCardAsync(card.CardId);
            var groups = await _cardGroupsRepo.GetByCardAsync(card.CardId);
            foreach (var tier in tiers.OrderBy(t => t.TierNo))
            {
                var tierKey = new TierKey(card.CardId, tier.CardTierId, tier.TierNo);
                var tierNode = new TreeNode($"📐 Tier {tier.TierNo}")
                {
                    Tag = new NodeTag(NodeKind.Tier, tier.CardTierId, tierKey)
                };

                foreach (var group in groups.Where(g => g.CardTierId == tier.CardTierId).OrderBy(g => g.GroupNo))
                {
                    var groupKey = new GroupKey(card.CardId, tier.CardTierId, tier.TierNo, group.CardGroupId, group.GroupNo);
                    var groupNode = new TreeNode($"🗂 Group {group.GroupNo}")
                    {
                        Tag = new NodeTag(NodeKind.Group, group.CardGroupId, groupKey)
                    };

                    var roles = await _cardRolesRepo.GetByGroupAsync(group.CardGroupId);
                    foreach (var role in roles.OrderBy(r => r.OrderInGroup))
                    {
                        string roleName = await _lookupCache.ResolveRoleNameAsync(role.RoleCode);

                        // v1.2.0 工程 H 追加：役職テンプレ展開のために role_format_kind と
                        // default_format_template を取得し、必要に応じて役職ラベルに注記を追加。
                        Role? roleEntity = string.IsNullOrEmpty(role.RoleCode)
                            ? null
                            : await _rolesRepo.GetByCodeAsync(role.RoleCode);
                        string roleNote = "";
                        bool isThemeSongRole = (roleEntity?.RoleFormatKind == "THEME_SONG");
                        if (isThemeSongRole)
                        {
                            // テンプレに columns 指定があれば抽出して注記化（ツリーは縦並び固定で表示する仕様）
                            int columns = ExtractThemeSongsColumns(roleEntity?.DefaultFormatTemplate);
                            if (columns >= 2) roleNote = $"  [横 {columns} カラム表示指定]";
                        }

                        var roleNode = new TreeNode($"📋 Role: {roleName}  (order {role.OrderInGroup}){roleNote}")
                        {
                            Tag = new NodeTag(NodeKind.CardRole, role.CardRoleId, role)
                        };

                        // 主題歌役職の場合：episode_theme_songs から楽曲情報を引いて、楽曲サブノードを差し込む。
                        // これらは仮想ノード（DB 行を持たない読み取り専用、削除/並べ替え不可）。
                        // 役職コードに応じて表示する theme_kind を絞り込む（v1.2.0 工程 H 最終形）。
                        if (isThemeSongRole && _currentCredit?.ScopeKind == "EPISODE" && _currentCredit.EpisodeId is int epId)
                        {
                            await AddThemeSongVirtualNodesAsync(roleNode, epId, role.RoleCode ?? "");
                        }

                        var blocks = await _blocksRepo.GetByCardRoleAsync(role.CardRoleId);
                        foreach (var block in blocks.OrderBy(b => b.BlockSeq))
                        {
                            var blockNode = new TreeNode($"🔵 Block #{block.BlockSeq}  ({block.RowCount}×{block.ColCount})")
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
                                    // SONG ケースは v1.2.0 工程 H で物理削除済み（DB に SONG 行は存在しない）
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
                        groupNode.Nodes.Add(roleNode);
                    }
                    tierNode.Nodes.Add(groupNode);
                }
                cardNode.Nodes.Add(tierNode);
            }
            newRootNodes.Add(cardNode);
        }

        // ─── フェーズ 2: 同期セクションで treeStructure を一気に更新（await を含まない）───
        // この区間に await が無いので、別の RebuildTreeAsync が割り込む余地が無く、
        // Clear と AddRange の間に他のスレッドが介入することはない。
        treeStructure.BeginUpdate();
        try
        {
            treeStructure.Nodes.Clear();
            treeStructure.Nodes.AddRange(newRootNodes.ToArray());
            treeStructure.ExpandAll();
        }
        finally
        {
            treeStructure.EndUpdate();
        }

        // 右ペインはクリア（v1.2.0 工程 B-3：エントリエディタも非アクティブ化）
        entryEditor.ClearAndDisable();
    }

    /// <summary>
    /// ツリーノード選択時：Entry なら EntryEditorPanel に編集モードで読み込む、
    /// それ以外（Card/Role/Block/クレジット直下）の場合は EntryEditorPanel を非アクティブ化する。
    /// </summary>
    private async void OnTreeNodeSelected()
    {
        try
        {
            if (treeStructure.SelectedNode?.Tag is not NodeTag tag)
            {
                entryEditor.ClearAndDisable();
                return;
            }
            if (tag.Kind != NodeKind.Entry || tag.Payload is not CreditBlockEntry e)
            {
                // Card/Role/Block を選択した場合は右ペインは非アクティブ
                entryEditor.ClearAndDisable();
                return;
            }
            // 既存エントリの編集モードに切替
            await entryEditor.LoadForEditAsync(e);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 主題歌役職ノード <paramref name="roleNode"/> 配下に、<paramref name="episodeId"/> に
    /// 対応する <c>episode_theme_songs</c> 由来の楽曲サブノードを差し込む（v1.2.0 工程 H 追加）。
    /// 仮想ノード（<see cref="NodeKind.ThemeSongVirtual"/>）として作るため、Tag.Id には
    /// song_recording_id を入れるが、削除・並べ替え対象には含めない（UpdateButtonStates で抑止）。
    /// </summary>
    private async Task AddThemeSongVirtualNodesAsync(TreeNode roleNode, int episodeId, string roleCode)
    {
        // 役職コードに応じて、ツリーに表示する theme_kind を決める。
        // これは「その役職に紐付く楽曲だけを楽曲ノードとして表示する」ためのフィルタで、
        // テンプレ DSL の {THEME_SONGS:kind=...} と同じセマンティクスを持つ。
        // 既知の主題歌役職コードに該当しない場合は OP/ED/INSERT 全部を表示するフォールバック。
        // INSERT_SONG と INSERT_SONGS_NONCREDITED は同じ INSERT を表示する（本来運用上は
        // 一方だけ置く前提だが、両方置かれた場合は両方ともに楽曲を表示してユーザー判断を尊重）。
        IReadOnlyList<string> kinds = roleCode switch
        {
            "THEME_SONG_OP"             => new[] { "OP" },
            "THEME_SONG_ED"             => new[] { "ED" },
            "THEME_SONG_OP_COMBINED"    => new[] { "OP", "ED" },
            "INSERT_SONG"               => new[] { "INSERT" },
            "INSERT_SONGS_NONCREDITED"  => new[] { "INSERT" },
            _                           => new[] { "OP", "ED", "INSERT" }
        };

        // ノンクレジット役職のときは楽曲ノードラベルに視認用マークを付けて、
        // 「これらは実放送ではクレジットされない」ことを一目でわかるようにする。
        bool isNoncredited = (roleCode == "INSERT_SONGS_NONCREDITED");

        // episode_theme_songs の正しい列名は insert_seq（PK の一部）。
        // theme_kind ENUM は ('OP','ED','INSERT')、CHECK 制約により OP/ED は insert_seq=0、
        // INSERT は insert_seq>=1。並び順は kinds パラメータの順序を尊重し、INSERT 内では
        // insert_seq 昇順、同位置に既定行と本放送限定行があれば既定行（is_broadcast_only=0）を先に。
        string fieldList = string.Join(",", kinds.Select(k => $"'{k}'"));
        string sql = $$"""
            SELECT
              ets.song_recording_id  AS SongRecordingId,
              ets.theme_kind         AS ThemeKind,
              ets.insert_seq         AS InsertSeq,
              ets.is_broadcast_only  AS IsBroadcastOnly,
              s.title                AS SongTitle,
              s.lyricist_name        AS LyricistName,
              s.composer_name        AS ComposerName,
              s.arranger_name        AS ArrangerName,
              sr.singer_name         AS SingerName,
              sr.variant_label       AS VariantLabel
            FROM episode_theme_songs ets
            JOIN song_recordings sr ON sr.song_recording_id = ets.song_recording_id
            JOIN songs           s  ON s.song_id           = sr.song_id
            WHERE ets.episode_id = @episodeId
              AND ets.theme_kind IN @kinds
            ORDER BY
              FIELD(ets.theme_kind, {{fieldList}}),
              ets.insert_seq,
              ets.is_broadcast_only;
            """;
        await using var conn = await _lookupCache.Factory.CreateOpenedAsync(default).ConfigureAwait(false);
        var rows = await Dapper.SqlMapper.QueryAsync<ThemeSongRowForTree>(
            conn, sql, new { episodeId, kinds });
        foreach (var r in rows)
        {
            string title = r.SongTitle ?? "(曲名未登録)";
            string variant = string.IsNullOrEmpty(r.VariantLabel) ? "" : $" [{r.VariantLabel}]";
            string broadcastMark = (r.IsBroadcastOnly == 1) ? "🎬[本放送限定] " : "";
            string noncreditedMark = isNoncredited ? "🚫[ノンクレジット] " : "";
            string detail = "";
            var detailParts = new List<string>();
            if (!string.IsNullOrEmpty(r.LyricistName)) detailParts.Add($"作詞:{r.LyricistName}");
            if (!string.IsNullOrEmpty(r.ComposerName)) detailParts.Add($"作曲:{r.ComposerName}");
            if (!string.IsNullOrEmpty(r.ArrangerName)) detailParts.Add($"編曲:{r.ArrangerName}");
            if (!string.IsNullOrEmpty(r.SingerName))   detailParts.Add($"うた:{r.SingerName}");
            if (detailParts.Count > 0) detail = "  [" + string.Join(" / ", detailParts) + "]";
            string label = $"📀 Song({r.ThemeKind}): {noncreditedMark}{broadcastMark}『{title}』{variant}{detail}";
            var node = new TreeNode(label)
            {
                Tag = new NodeTag(NodeKind.ThemeSongVirtual, r.SongRecordingId ?? 0, r),
                ForeColor = System.Drawing.SystemColors.GrayText
            };
            roleNode.Nodes.Add(node);
        }
    }

    /// <summary>
    /// 役職テンプレ文字列から <c>{THEME_SONGS:columns=N}</c> の N 値を抽出する
    /// （v1.2.0 工程 H 追加。ノードラベル注記用、見つからなければ 1）。
    /// </summary>
    private static int ExtractThemeSongsColumns(string? template)
    {
        if (string.IsNullOrEmpty(template)) return 1;
        // 雑な抽出：{THEME_SONGS:columns=N} に含まれる数値を読む
        int idx = template.IndexOf("THEME_SONGS", StringComparison.Ordinal);
        if (idx < 0) return 1;
        int colon = template.IndexOf(':', idx);
        int close = template.IndexOf('}', idx);
        if (colon < 0 || close < 0 || colon > close) return 1;
        string opts = template.Substring(colon + 1, close - colon - 1);
        // columns=N をスキャン
        var m = System.Text.RegularExpressions.Regex.Match(opts, @"columns\s*=\s*(\d+)");
        if (!m.Success) return 1;
        return int.TryParse(m.Groups[1].Value, out var n) && n >= 1 ? n : 1;
    }

    /// <summary>主題歌仮想ノード Tag に格納する DTO（ツリー表示用、編集不可）。</summary>
    private sealed class ThemeSongRowForTree
    {
        public int? SongRecordingId { get; set; }
        public string? ThemeKind { get; set; }
        public byte? InsertSeq { get; set; }
        public byte IsBroadcastOnly { get; set; }
        public string? SongTitle { get; set; }
        public string? LyricistName { get; set; }
        public string? ComposerName { get; set; }
        public string? ArrangerName { get; set; }
        public string? SingerName { get; set; }
        public string? VariantLabel { get; set; }
    }

    /// <summary>ツリーと右ペインを空にする（クレジット未選択時）。</summary>
    private void ClearTreeAndPreview()
    {
        treeStructure.Nodes.Clear();
        entryEditor.ClearAndDisable();
        lblStatusBar.Text = "現在編集中: （クレジット未選択）";
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
    /// <summary>
    /// ツリーノードの種別。
    /// v1.2.0 工程 E で <see cref="Tier"/> と <see cref="Group"/> を追加し、
    /// クレジット → カード → Tier → Group → 役職 → ブロック → エントリ の
    /// 7 階層ツリーを表現するようになった。Tier と Group は仮想ノードで、
    /// DB 行を直接持たず、配下の役職の tier / group_in_tier を集約して生成される。
    /// </summary>
    private enum NodeKind { Card, Tier, Group, CardRole, Block, Entry, ThemeSongVirtual }

    /// <summary>TreeNode.Tag に積む構造体（種別 + 主キー + 元エンティティ）。</summary>
    private sealed class NodeTag
    {
        public NodeKind Kind { get; }
        public int Id { get; }
        public object Payload { get; }
        public NodeTag(NodeKind kind, int id, object payload)
        { Kind = kind; Id = id; Payload = payload; }
    }

    /// <summary>
    /// Tier 仮想ノードのキー（v1.2.0 工程 G で実体テーブル化に対応してリファクタ）。
    /// 旧 (CardId, Tier) の複合キーから単一の <see cref="CardTierId"/> へ簡素化。
    /// 親カード ID は再描画時のヒントとして併せて保持しておく。
    /// </summary>
    private sealed record TierKey(int CardId, int CardTierId, byte TierNo);

    /// <summary>
    /// Group 仮想ノードのキー（v1.2.0 工程 G で実体テーブル化に対応してリファクタ）。
    /// 旧 (CardId, Tier, GroupInTier) の複合キーから単一の <see cref="CardGroupId"/> へ簡素化。
    /// 親カード / 親 Tier の ID は再描画時のヒントとして併せて保持しておく。
    /// </summary>
    private sealed record GroupKey(int CardId, int CardTierId, byte TierNo, int CardGroupId, byte GroupNo);

    // ============================================================
    // v1.2.0 工程 B-2 追加：ボタン状態管理 / クレジット CRUD / ツリー編集 / DnD
    // ============================================================

    /// <summary>
    /// ツリー上の選択ノード種別とクレジット選択状態に応じて、編集ボタンの Enabled を切り替える。
    /// 選択ノード種別 → 有効化されるボタンの対応表は <c>CreditEditorForm</c> のドキュメント参照。
    /// v1.2.0 工程 B-3 で Entry 系（追加・並べ替え・削除）も有効化。Entry の編集本体（保存・削除）は
    /// 右ペインの EntryEditorPanel に移管したので、本メソッドからは btnSaveEntry / btnDeleteEntry の
    /// 参照は撤去している。
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
            btnAddCard.Enabled = btnAddTier.Enabled = btnAddGroup.Enabled = false;
            btnAddRole.Enabled = btnAddBlock.Enabled = btnAddEntry.Enabled = false;
            btnMoveUp.Enabled = btnMoveDown.Enabled = false;
            btnDeleteNode.Enabled = false;
            return;
        }

        // ツリー操作ボタン：選択ノード種別で切替（v1.2.0 工程 G で Tier / Group 操作対応）
        var tag = treeStructure.SelectedNode?.Tag as NodeTag;
        switch (tag?.Kind)
        {
            case NodeKind.Card:
                // カード選択時：Tier も Group も Role もこのカード配下に作れる
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = true;
                btnAddGroup.Enabled = true;
                btnAddRole.Enabled  = true;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Tier:
                // Tier 選択時：Tier 配下に Group / Role が作れる。Tier 自体も削除可（CASCADE で配下も連動削除）。
                // ただし Tier の並べ替えは tier_no が 1/2 の固定意味を持つので無効。
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = true;     // 同カード内にもう 1 つ Tier を作る用途
                btnAddGroup.Enabled = true;
                btnAddRole.Enabled  = true;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = false;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Group:
                // Group 選択時：Group 配下に Role が作れる。Group 自体も削除可（CASCADE 連動）。
                // 同 Tier 内で Group の並べ替えは現状未対応（必要になったら DnD 経由で実装予定）。
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = true;
                btnAddGroup.Enabled = true;     // 隣に並ぶ Group を作る用途
                btnAddRole.Enabled  = true;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = false;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.CardRole:
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = true;
                btnAddGroup.Enabled = true;     // Role 選択時にも親 Tier 配下に Group 追加できる
                btnAddRole.Enabled  = true;
                btnAddBlock.Enabled = true;
                btnAddEntry.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Block:
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = false;
                btnAddGroup.Enabled = false;
                btnAddRole.Enabled  = false;
                btnAddBlock.Enabled = true;
                btnAddEntry.Enabled = true;     // v1.2.0 工程 B-3: Block 選択時にエントリ追加可
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Entry:
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = false;
                btnAddGroup.Enabled = false;
                btnAddRole.Enabled  = false;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = true;     // 同 block 内に追加するために有効
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.ThemeSongVirtual:
                // v1.2.0 工程 H 追加：episode_theme_songs 由来の楽曲仮想ノード。
                // 読み取り専用なので、追加・並べ替え・削除はすべて無効化（編集は
                // 「クレジット系マスタ管理 → エピソード主題歌」タブで行う運用）。
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = false;
                btnAddGroup.Enabled = false;
                btnAddRole.Enabled  = false;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = false;
                btnDeleteNode.Enabled = false;
                break;
            default:
                // 何も選択されていない（クレジットだけ選択されている状態）
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = false;
                btnAddGroup.Enabled = false;
                btnAddRole.Enabled  = false;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = false;
                btnMoveUp.Enabled = btnMoveDown.Enabled = false;
                btnDeleteNode.Enabled = false;
                break;
        }
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
    /// Tier 追加（v1.2.0 工程 G 追加）：選択中ノードからカードを特定し、
    /// そのカード配下に新しい Tier をブランクで作成する（Tier 作成時に Group 1 が自動投入される）。
    /// <para>
    /// Tier 番号は既存 Tier の最大 + 1（仕様上の上限は 2）。既に Tier 1 / Tier 2 の両方が
    /// 揃っているカードでは作成不可（メッセージで通知）。
    /// </para>
    /// <para>
    /// 動作目的：「同一カード内で本来別 Tier になるべき役職群が混在している」状態を、
    /// ブランク Tier を作って役職をそこへ移動することで整理できるようにする。
    /// </para>
    /// </summary>
    private async Task OnAddTierAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            // 選択ノードから親カード ID を解決
            int? cardId = ResolveAncestorCardId();
            if (cardId is null)
            {
                MessageBox.Show(this, "Card / Tier / Group / Role / Block / Entry のいずれかを選択してから「+ Tier」を押してください。");
                return;
            }

            // 既存 Tier を取得して、新 tier_no を決める（最大 + 1、上限 2）
            var existingTiers = await _cardTiersRepo.GetByCardAsync(cardId.Value);
            byte nextTierNo = (byte)(existingTiers.Select(t => (int)t.TierNo).DefaultIfEmpty(0).Max() + 1);
            if (nextTierNo > 2)
            {
                MessageBox.Show(this, "このカードには既に Tier 1 / Tier 2 が揃っています。これ以上 Tier は追加できません。");
                return;
            }

            var newTier = new CreditCardTier
            {
                CardId = cardId.Value,
                TierNo = nextTierNo,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            // InsertAsync は内部で Group 1 を自動投入する（CreditCardTiersRepository）
            int newTierId = await _cardTiersRepo.InsertAsync(newTier);
            await RebuildTreeAsync();
            SelectNodeById(NodeKind.Tier, newTierId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// Group 追加（v1.2.0 工程 G 追加）：選択中ノードから所属 Tier を特定し、
    /// その Tier 配下に新しい Group をブランク（役職ゼロ）で作成する。
    /// <para>
    /// 推測ルール:
    /// <list type="bullet">
    ///   <item><description>Card 選択時 → そのカードの Tier 1 配下に新 Group を追加</description></item>
    ///   <item><description>Tier 選択時 → その Tier 配下に新 Group を追加</description></item>
    ///   <item><description>Group / Role 選択時 → 親 Tier 配下に新 Group を追加（隣に並ぶ Group ができる）</description></item>
    /// </list>
    /// 新 group_no は既存 Group の最大 + 1。
    /// </para>
    /// </summary>
    private async Task OnAddGroupAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            // 選択ノードから所属 card_tier_id を解決
            int? cardTierId = await ResolveAncestorCardTierIdAsync();
            if (cardTierId is null)
            {
                MessageBox.Show(this, "Card / Tier / Group / Role のいずれかを選択してから「+ Group」を押してください。");
                return;
            }

            // 既存 Group を取得して、新 group_no を決める（最大 + 1）
            var existingGroups = await _cardGroupsRepo.GetByTierAsync(cardTierId.Value);
            byte nextGroupNo = (byte)(existingGroups.Select(g => (int)g.GroupNo).DefaultIfEmpty(0).Max() + 1);

            var newGroup = new CreditCardGroup
            {
                CardTierId = cardTierId.Value,
                GroupNo = nextGroupNo,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            int newGroupId = await _cardGroupsRepo.InsertAsync(newGroup);
            await RebuildTreeAsync();
            SelectNodeById(NodeKind.Group, newGroupId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 選択ノードから祖先のカード ID を解決する（v1.2.0 工程 G 追加）。
    /// Card / Tier / Group / CardRole / Block / Entry のいずれかを選択していれば、
    /// その所属カードの card_id を返す。
    /// </summary>
    private int? ResolveAncestorCardId()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag tag && tag.Kind == NodeKind.Card) return tag.Id;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// 選択ノードから所属する card_tier_id を解決する（v1.2.0 工程 G 追加）。
    /// <list type="bullet">
    ///   <item><description>Tier ノード自身 → tag.Id</description></item>
    ///   <item><description>Group / Role / Block / Entry → 祖先の Tier ノードを探して tag.Id</description></item>
    ///   <item><description>Card 選択時 → そのカードの Tier 1 を DB から引いて返す</description></item>
    /// </list>
    /// </summary>
    private async Task<int?> ResolveAncestorCardTierIdAsync()
    {
        var node = treeStructure.SelectedNode;
        // 祖先側を辿る
        var cur = node;
        while (cur is not null)
        {
            if (cur.Tag is NodeTag tag)
            {
                if (tag.Kind == NodeKind.Tier) return tag.Id;
                if (tag.Kind == NodeKind.Card)
                {
                    // Card 直下なら Tier 1 を返す（カード作成時の自動投入で必ず存在する）
                    var tiers = await _cardTiersRepo.GetByCardAsync(tag.Id);
                    return tiers.OrderBy(t => t.TierNo).FirstOrDefault()?.CardTierId;
                }
            }
            cur = cur.Parent;
        }
        return null;
    }

    /// <summary>
    /// 役職追加：<see cref="Pickers.RolePickerDialog"/> で role_code を選んで、
    /// 選択中ノードに応じて適切な card_group_id に新規役職を作成する。
    /// <para>
    /// 推測ルール（v1.2.0 工程 G で更新）：
    /// <list type="bullet">
    ///   <item><description>Card 選択時 → tier_no=1 / group_no=1 の末尾（カード作成時に自動投入されている）</description></item>
    ///   <item><description>Tier ノード選択時 → 該当 Tier の末尾グループの末尾</description></item>
    ///   <item><description>Group ノード選択時 → 該当グループの末尾</description></item>
    ///   <item><description>Role 選択時 → 同 card_group_id の末尾</description></item>
    /// </list>
    /// order_in_group は推測対象グループの最大値 + 1。
    /// 「+ Tier」「+ Group」とは違い、「+ 役職」は既存の Group に追加するだけ
    /// （新規 Tier / Group の生成はそれぞれ専用ボタンを使う）。
    /// </para>
    /// </summary>
    private async Task OnAddRoleAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            // 選択ノードから推測する card_group_id を解決
            int? cardGroupId = await ResolveAddRoleTargetGroupIdAsync();
            if (cardGroupId is null)
            {
                MessageBox.Show(this, "Card / Tier / Group / Role のいずれかのノードを選択してから「+ 役職」を押してください。");
                return;
            }

            // 役職コードをピッカーで選んでもらう
            using var dlg = new Pickers.RolePickerDialog(_rolesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedRole is null) return;

            // 同 group_id 内の役職数 + 1 を新 order_in_group とする
            var existingInGroup = await _cardRolesRepo.GetByGroupAsync(cardGroupId.Value);
            byte newOrder = (byte)(existingInGroup.Count + 1);

            var newRole = new CreditCardRole
            {
                CardGroupId = cardGroupId.Value,
                RoleCode = dlg.SelectedRole.RoleCode,
                OrderInGroup = newOrder,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            // InsertAsync は内部で Block 1 を自動投入する（v1.2.0 工程 G 追加）
            int newCardRoleId = await _cardRolesRepo.InsertAsync(newRole);
            await RebuildTreeAsync();
            SelectNodeById(NodeKind.CardRole, newCardRoleId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「+ 役職」押下時の挿入先 card_group_id を選択ノードから推測する
    /// （v1.2.0 工程 G で実体テーブル化に対応してリファクタ）。
    /// </summary>
    private async Task<int?> ResolveAddRoleTargetGroupIdAsync()
    {
        var node = treeStructure.SelectedNode;
        if (node?.Tag is not NodeTag tag) return null;

        switch (tag.Kind)
        {
            case NodeKind.Card:
                {
                    // カード選択 → tier_no=1 / group_no=1 が既定（カード作成時に自動投入されている）。
                    // もし無ければ（既存データが工程 G 移行前の状態だった場合の保険）、最初の Tier の最初の Group を返す。
                    int cardId = tag.Id;
                    var groups = await _cardGroupsRepo.GetByCardAsync(cardId);
                    return groups.FirstOrDefault()?.CardGroupId;
                }
            case NodeKind.Tier when tag.Payload is TierKey tk:
                {
                    // Tier ノード選択 → その Tier の末尾グループの末尾
                    var groups = await _cardGroupsRepo.GetByTierAsync(tk.CardTierId);
                    return groups.LastOrDefault()?.CardGroupId;
                }
            case NodeKind.Group when tag.Payload is GroupKey gk:
                return gk.CardGroupId;
            case NodeKind.CardRole when tag.Payload is CreditCardRole r:
                return r.CardGroupId;
            default:
                return null;
        }
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
                RowCount = 1,
                ColCount = 1,
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
    /// ノード削除：選択ノード種別を判定して該当リポジトリの DeleteAsync を呼ぶ。
    /// Card / Role / Block の子要素は ON DELETE CASCADE で連動削除される。
    /// v1.2.0 工程 B-3 で Entry 削除も対応。Entry は単体の物理削除（CASCADE 連鎖なし）。
    /// 削除確認ダイアログでは子要素件数を伝える。
    /// </summary>
    private async Task OnDeleteNodeAsync()
    {
        try
        {
            if (treeStructure.SelectedNode?.Tag is not NodeTag tag) return;

            // v1.2.0 工程 G：Tier / Group は実体テーブル化されたため削除対象になる。
            // CASCADE で配下の Group / Role / Block / Entry がまとめて消える。

            int childCount = treeStructure.SelectedNode.Nodes.Count;
            string nodeName = tag.Kind switch
            {
                NodeKind.Card     => $"カード（{treeStructure.SelectedNode.Text}）",
                NodeKind.Tier     => $"Tier（{treeStructure.SelectedNode.Text}）",
                NodeKind.Group    => $"Group（{treeStructure.SelectedNode.Text}）",
                NodeKind.CardRole => $"役職（{treeStructure.SelectedNode.Text}）",
                NodeKind.Block    => $"ブロック（{treeStructure.SelectedNode.Text}）",
                NodeKind.Entry    => $"エントリ（{treeStructure.SelectedNode.Text}）",
                _                 => "(不明)"
            };
            string warn = childCount > 0 ? $"\n※ 配下の {childCount} 件も連鎖削除されます。" : "";
            if (MessageBox.Show(this,
                $"{nodeName} を削除します。{warn}",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            switch (tag.Kind)
            {
                case NodeKind.Card:     await _cardsRepo.DeleteAsync(tag.Id);       break;
                case NodeKind.Tier:     await _cardTiersRepo.DeleteAsync(tag.Id);   break;
                case NodeKind.Group:    await _cardGroupsRepo.DeleteAsync(tag.Id);  break;
                case NodeKind.CardRole: await _cardRolesRepo.DeleteAsync(tag.Id);   break;
                case NodeKind.Block:    await _blocksRepo.DeleteAsync(tag.Id);      break;
                case NodeKind.Entry:    await _entriesRepo.DeleteAsync(tag.Id);     break;
            }
            entryEditor.ClearAndDisable();
            await RebuildTreeAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────
    // エントリ追加（v1.2.0 工程 B-3 追加）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// 「+ エントリ」ボタン処理：選択中ノードから追加先 block_id を解決し、
    /// 右ペインの EntryEditorPanel を新規追加モードに切り替える（INSERT は EntryEditorPanel 内の
    /// 「保存」ボタンで実行されるので、ここでは UI モード切替だけを行う）。
    /// 新規 entry_seq は同 (block_id, is_broadcast_only=false) グループの max+1 を使う
    /// （本放送限定行はチェックボックス OFF が初期値のため、既定の円盤・配信行として作る）。
    /// </summary>
    private async Task OnAddEntryAsync()
    {
        try
        {
            if (_currentCredit is null) return;
            int? blockId = ResolveTargetBlockIdFromSelection();
            if (blockId is null)
            {
                MessageBox.Show(this, "Block または Entry ノードを選択してから「+ エントリ」を押してください。");
                return;
            }
            // 既定で is_broadcast_only=false 行のみ採番対象とする（本放送行は手動でチェックを入れた時点で別グループに移る）
            var existingEntries = await _entriesRepo.GetByBlockAsync(blockId.Value);
            ushort newSeq = (ushort)(existingEntries.Where(x => !x.IsBroadcastOnly).DefaultIfEmpty().Max(x => x?.EntrySeq ?? 0) + 1);

            entryEditor.LoadForNew(blockId.Value, isBroadcastOnly: false, newSeq);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 選択ノードから「エントリ追加先となる Block の block_id」を解決する。
    /// Block 選択時 → そのノード自身、Entry 選択時 → 親 Block。
    /// 該当しない選択状態（Card/Role など）の場合は null。
    /// </summary>
    private int? ResolveTargetBlockIdFromSelection()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag t && t.Kind == NodeKind.Block) return t.Id;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// EntryEditorPanel から保存／削除完了の通知を受けたとき、ツリーを再構築して反映する。
    /// 保存時は最後に編集していたノードを再選択、削除時は親 Block を選択状態にする。
    /// </summary>
    private async Task OnEntryEditorChangedAsync(bool reselectLastEdited)
    {
        try
        {
            int? selectedBlockId = ResolveTargetBlockIdFromSelection();
            await RebuildTreeAsync();
            if (!reselectLastEdited && selectedBlockId.HasValue)
            {
                SelectNodeById(NodeKind.Block, selectedBlockId.Value);
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ────────────────────────────────────────────────────────────
    // 並べ替え（ボタン式 ↑↓）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// ↑↓ ボタンによる並べ替え：選択ノードと同階層の兄弟リストを取得し、
    /// 指定方向に 1 つずらしてリポジトリの BulkUpdateSeqAsync で一括 UPDATE する。
    /// v1.2.0 工程 B-3 で Entry も対象に追加（同 block_id × 同 is_broadcast_only 内のみ）。
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
                    // v1.2.0 工程 G：CardRole は card_group_id で所属が決まる。
                    // ↑↓ ボタンは「同 card_group_id 内」のみで並べ替える。
                    // 別 Group / 別 Tier / 別 Card への乗り換えは DnD で行う。
                    var orig = (CreditCardRole)tag.Payload;
                    var sameGroup = (await _cardRolesRepo.GetByGroupAsync(orig.CardGroupId))
                        .OrderBy(r => r.OrderInGroup).ToList();
                    int idx = sameGroup.FindIndex(r => r.CardRoleId == tag.Id);
                    bool moved = up ? SeqReorderHelper.MoveUp(sameGroup, idx) : SeqReorderHelper.MoveDown(sameGroup, idx);
                    if (!moved) return;
                    await SeqReorderHelper.ReorderCardRolesInGroupAsync(
                        _cardRolesRepo, orig.CardGroupId, sameGroup);
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
                case NodeKind.Entry:
                {
                    // v1.2.0 工程 B-3 追加：Entry の並べ替えは「同 block_id × 同 is_broadcast_only」内のみ。
                    // 0/1 行は別グループ扱いで、グループ間をまたぐ並べ替えは UI 側でブロック。
                    var node = treeStructure.SelectedNode;
                    if (node.Parent?.Tag is not NodeTag pt || pt.Kind != NodeKind.Block) return;
                    int blockId = pt.Id;
                    var orig = (CreditBlockEntry)tag.Payload;
                    var sameGroup = (await _entriesRepo.GetByBlockAsync(blockId))
                        .Where(e => e.IsBroadcastOnly == orig.IsBroadcastOnly)
                        .OrderBy(e => e.EntrySeq).ToList();
                    int idx = sameGroup.FindIndex(e => e.EntryId == tag.Id);
                    bool moved = up ? SeqReorderHelper.MoveUp(sameGroup, idx) : SeqReorderHelper.MoveDown(sameGroup, idx);
                    if (!moved) return;
                    await SeqReorderHelper.ReorderBlockEntriesAsync(_entriesRepo, blockId, orig.IsBroadcastOnly, sameGroup);
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
    /// ItemDrag：ノード上でマウスドラッグが始まった時、Card/Role/Block/Entry のいずれかなら
    /// DoDragDrop で Move 操作を開始する。
    /// v1.2.0 工程 B-3 で Entry も DnD 対応に拡張（DragOver で同階層 + 同 is_broadcast_only を判定）。
    /// </summary>
    private void OnTreeItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not TreeNode node) return;
        if (node.Tag is not NodeTag) return;
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
        if (target is null || target == src) { e.Effect = DragDropEffects.None; return; }
        if (src.Tag is not NodeTag st || target.Tag is not NodeTag tt) { e.Effect = DragDropEffects.None; return; }

        // ─── v1.2.0 工程 E：CardRole の自由乗り換え DnD ───
        // CardRole ノードは、同じクレジット（同じツリー内）であれば
        // 別 Card / 別 Tier / 別 Group へドロップ可能（ドロップ先のノード種別に応じて
        // 移動先 (card_id, tier, group_in_tier) を決める）。ターゲット種別ごとの解決:
        //   ・別 CardRole にドロップ → そのカードロールと同じ (card, tier, group)、上下半分で前後判定
        //   ・Group ノードにドロップ → そのグループの末尾
        //   ・Tier ノードにドロップ → その tier の末尾グループの末尾
        //   ・Card ノードにドロップ → tier=1, group_in_tier=1 の末尾
        if (st.Kind == NodeKind.CardRole)
        {
            // CardRole のドロップ先として許容する種別
            if (tt.Kind == NodeKind.CardRole || tt.Kind == NodeKind.Group
                || tt.Kind == NodeKind.Tier || tt.Kind == NodeKind.Card)
            {
                e.Effect = DragDropEffects.Move;
                treeStructure.SelectedNode = target;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
            return;
        }

        // ─── 既存：Card / Block / Entry は同階層内のみ ───
        if (target.Parent != src.Parent) { e.Effect = DragDropEffects.None; return; }
        if (st.Kind != tt.Kind) { e.Effect = DragDropEffects.None; return; }

        // v1.2.0 工程 B-3 追加：Entry の場合は同 is_broadcast_only グループ内のみ
        if (st.Kind == NodeKind.Entry &&
            st.Payload is CreditBlockEntry se && tt.Payload is CreditBlockEntry te &&
            se.IsBroadcastOnly != te.IsBroadcastOnly)
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
            if (target is null || target == src) return;
            if (src.Tag is not NodeTag st || target.Tag is not NodeTag tt) return;

            bool dropAbove = (pt.Y < target.Bounds.Y + target.Bounds.Height / 2);

            // ─── v1.2.0 工程 E：CardRole の自由乗り換え DnD ───
            // CardRole はターゲット種別に応じて移動先 (card_id, tier, group_in_tier) を決め、
            // SeqReorderHelper.RelocateCardRoleAsync で旧グループ詰め直し + 新グループ挿入を実行。
            // 変数名は後段の switch ブロック側にも keepId / keepKind があるため
            // 衝突を避けて keepIdRole / keepKindRole としている（CS0136 回避）。
            if (st.Kind == NodeKind.CardRole)
            {
                await OnDropCardRoleAsync(st, tt, target, dropAbove);
                int keepIdRole = st.Id;
                NodeKind keepKindRole = st.Kind;
                await RebuildTreeAsync();
                SelectNodeById(keepKindRole, keepIdRole);
                return;
            }

            // ─── 既存：Card / Block / Entry は同階層内のみ ───
            if (target.Parent != src.Parent) return;
            if (st.Kind != tt.Kind) return;

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
                case NodeKind.Entry:
                {
                    // v1.2.0 工程 B-3 追加：Entry の DnD は同 (block_id, is_broadcast_only) グループ内のみ。
                    // DragOver で同グループであることは検証済み。
                    if (src.Parent?.Tag is not NodeTag pt2 || pt2.Kind != NodeKind.Block) return;
                    int blockId = pt2.Id;
                    var srcEntry = (CreditBlockEntry)st.Payload;
                    var sameGroup = (await _entriesRepo.GetByBlockAsync(blockId))
                        .Where(en => en.IsBroadcastOnly == srcEntry.IsBroadcastOnly)
                        .OrderBy(en => en.EntrySeq).ToList();
                    var srcEntity = sameGroup.First(en => en.EntryId == st.Id);
                    sameGroup.Remove(srcEntity);
                    int targetIdx = sameGroup.FindIndex(en => en.EntryId == tt.Id);
                    if (targetIdx < 0) return;
                    sameGroup.Insert(dropAbove ? targetIdx : targetIdx + 1, srcEntity);
                    await SeqReorderHelper.ReorderBlockEntriesAsync(_entriesRepo, blockId, srcEntry.IsBroadcastOnly, sameGroup);
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

    /// <summary>
    /// CardRole 役職ノードのドロップ処理（v1.2.0 工程 E 追加）。
    /// ターゲット種別ごとに移動先 (card_id, tier, group_in_tier) を解決し、
    /// <see cref="SeqReorderHelper.RelocateCardRoleAsync"/> で旧グループ詰め直しと
    /// 新グループ挿入を 1 トランザクションで行う。
    /// </summary>
    /// <param name="st">ドラッグ元ノードの NodeTag（CardRole）。</param>
    /// <param name="tt">ドロップ先ノードの NodeTag（CardRole / Group / Tier / Card）。</param>
    /// <param name="target">ドロップ先 TreeNode（Bounds 計算に使う）。</param>
    /// <param name="dropAbove">CardRole 同士のドロップ時に使う「上に挿入か下に挿入か」のフラグ。</param>
    private async Task OnDropCardRoleAsync(NodeTag st, NodeTag tt, TreeNode target, bool dropAbove)
    {
        if (st.Payload is not CreditCardRole srcRole) return;
        if (_currentCredit is null) return;

        // ─── 移動先の card_group_id と insertAt を解決 ───
        // v1.2.0 工程 G で簡素化：移動先は card_group_id 1 個で完全に決まる
        // （Tier も Card も Group の親なので、Group が決まれば一意）。
        int newCardGroupId;
        int insertAt;

        switch (tt.Kind)
        {
            case NodeKind.CardRole when tt.Payload is CreditCardRole tgtRole:
            {
                // 同じ card_group_id に揃え、上下半分で前後判定
                newCardGroupId = tgtRole.CardGroupId;
                var newGroupList = (await _cardRolesRepo.GetByGroupAsync(newCardGroupId))
                    .Where(r => r.CardRoleId != srcRole.CardRoleId)
                    .OrderBy(r => r.OrderInGroup).ToList();
                int targetIdx = newGroupList.FindIndex(r => r.CardRoleId == tgtRole.CardRoleId);
                if (targetIdx < 0) targetIdx = newGroupList.Count; // 念のための保険
                insertAt = dropAbove ? targetIdx : targetIdx + 1;
                break;
            }
            case NodeKind.Group when tt.Payload is GroupKey gk:
            {
                // グループ末尾に追加
                newCardGroupId = gk.CardGroupId;
                var newGroupList = (await _cardRolesRepo.GetByGroupAsync(newCardGroupId))
                    .Where(r => r.CardRoleId != srcRole.CardRoleId)
                    .ToList();
                insertAt = newGroupList.Count;
                break;
            }
            case NodeKind.Tier when tt.Payload is TierKey tk:
            {
                // Tier の末尾グループの末尾に追加（Tier 配下に Group が必ず 1 つ以上ある前提：
                // 工程 G では Tier 作成時に Group 1 が自動投入されているため成立）
                var groupsInTier = await _cardGroupsRepo.GetByTierAsync(tk.CardTierId);
                var lastGroup = groupsInTier.LastOrDefault();
                if (lastGroup is null) return; // ありえないが防御
                newCardGroupId = lastGroup.CardGroupId;
                var newGroupList = (await _cardRolesRepo.GetByGroupAsync(newCardGroupId))
                    .Where(r => r.CardRoleId != srcRole.CardRoleId)
                    .ToList();
                insertAt = newGroupList.Count;
                break;
            }
            case NodeKind.Card:
            {
                // カードの tier_no=1 / group_no=1 の末尾に追加（カード作成時に自動投入されている）
                int newCardId = tt.Id;
                var groups = await _cardGroupsRepo.GetByCardAsync(newCardId);
                var firstGroup = groups.FirstOrDefault();
                if (firstGroup is null) return; // 工程 G 以前のデータ等の保険
                newCardGroupId = firstGroup.CardGroupId;
                var newGroupList = (await _cardRolesRepo.GetByGroupAsync(newCardGroupId))
                    .Where(r => r.CardRoleId != srcRole.CardRoleId)
                    .ToList();
                insertAt = newGroupList.Count;
                break;
            }
            default:
                return; // ありえない種別
        }

        // ─── 旧グループ役職一覧（移動対象を含む）と、新グループ役職一覧（移動対象を含まない）を取得 ───
        var oldGroupOrdered = (await _cardRolesRepo.GetByGroupAsync(srcRole.CardGroupId))
            .OrderBy(r => r.OrderInGroup).ToList();

        IList<CreditCardRole> newGroupOrdered;
        if (newCardGroupId == srcRole.CardGroupId)
        {
            // 同一グループ内の並べ替え
            newGroupOrdered = oldGroupOrdered.Where(r => r.CardRoleId != srcRole.CardRoleId).ToList();
        }
        else
        {
            // 別グループへの乗り換え
            newGroupOrdered = (await _cardRolesRepo.GetByGroupAsync(newCardGroupId))
                .Where(r => r.CardRoleId != srcRole.CardRoleId)
                .OrderBy(r => r.OrderInGroup).ToList();
        }

        await SeqReorderHelper.RelocateCardRoleAsync(
            _cardRolesRepo, srcRole.CardRoleId,
            oldGroupOrdered, newGroupOrdered,
            newCardGroupId, insertAt);
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
