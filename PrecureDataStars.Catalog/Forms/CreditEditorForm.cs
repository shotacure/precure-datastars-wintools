using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Catalog.Forms.Preview;

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
    /// 現在編集中のクレジットの Draft セッション（v1.2.0 工程 H-8 で導入）。
    /// クレジット選択時に <see cref="CreditDraftLoader"/> で構築され、
    /// 編集操作はすべてこのメモリ上の Draft オブジェクトに対して行い、
    /// 保存ボタンが押されるまで DB には反映しない。
    /// null の場合はクレジット未選択（または読み込み中）。
    /// </summary>
    private CreditDraftSession? _draftSession;

    /// <summary>
    /// Draft セッション構築用のローダ（v1.2.0 工程 H-8 で導入）。
    /// コンストラクタで Repositories を流し込んで生成する。
    /// </summary>
    private readonly CreditDraftLoader _draftLoader;

    /// <summary>
    /// クレジット選択処理の再入防止フラグ（v1.2.0 工程 H-8 ターン 5 で追加）。
    /// <para>
    /// Windows Forms の <see cref="ListBox.SelectedIndexChanged"/> は、<c>DataSource</c> 再代入や
    /// 内部状態変化で連鎖発火することが知られており、その結果 <see cref="OnCreditSelectedAsync"/> が
    /// 同一クレジット選択に対して複数回呼び出され、<see cref="_draftSession"/> が複数回新インスタンスで
    /// 上書きされる現象が発生していた。これにより「ツリーの Tag.Payload に入っている Draft オブジェクト」と
    /// 「<c>_draftSession</c> 内の Draft オブジェクト」が別インスタンスになり、編集（適用）が画面に
    /// 反映されないバグの原因となっていた。
    /// </para>
    /// <para>
    /// このフラグで再入を防ぐことで、1 回のユーザー選択につき <c>_draftSession</c> 構築は 1 回に
    /// 限定される。同様のガードを <see cref="OnSeriesChangedAsync"/>・<see cref="ReloadCreditsAsync"/>
    /// にも入れて、コンボボックス系の連鎖発火による多重実行を防いでいる。
    /// </para>
    /// </summary>
    private bool _isLoadingCredit;

    /// <summary><see cref="OnSeriesChangedAsync"/> の再入防止フラグ。</summary>
    private bool _isReloadingSeries;

    /// <summary><see cref="ReloadCreditsAsync"/> の再入防止フラグ。</summary>
    private bool _isReloadingCredits;

    /// <summary>
    /// 現在表示中のクレジットに対応する <see cref="lstCredits"/> のインデックス
    /// （v1.2.0 工程 H-8 ターン 6 で追加）。
    /// 未保存変更があるクレジットを切り替えようとして「キャンセル」が選ばれた時に、
    /// この値を使って元のクレジットへ <c>SelectedIndex</c> を戻すために使う。
    /// 初期値 -1 は「まだクレジットを 1 度も選んでいない」状態を表す。
    /// </summary>
    private int _lastCreditListIndex = -1;

    /// <summary>
    /// クレジット切替の確認ダイアログでキャンセルが選ばれた時、<c>lstCredits.SelectedIndex</c> を
    /// プログラムから戻すと <see cref="ListBox.SelectedIndexChanged"/> が再発火するため、
    /// その再発火を「ユーザー操作ではない」と判別して再帰確認を抑止するためのフラグ
    /// （v1.2.0 工程 H-8 ターン 6 で追加）。
    /// </summary>
    private bool _suppressCreditSelection;

    /// <summary>
    /// フォーム閉じ時の確認ダイアログ → 保存 → 改めて Close、というシーケンスを実現するためのフラグ
    /// （v1.2.0 工程 H-8 ターン 6 で追加）。<see cref="OnFormClosing"/> から非同期に保存処理を実行するために、
    /// 一度 e.Cancel = true で閉じるのを止め、await 完了後に <see cref="Form.Close"/> を再呼び出しする。
    /// その再呼び出し時に再度確認ダイアログが出るのを防ぐため、このフラグで「プログラム由来の Close」を識別する。
    /// </summary>
    private bool _isClosingProgrammatically;

    /// <summary>
    /// クレジット話数コピー処理中、cboSeries / cboEpisode をコピー先の値に切り替えるとき、
    /// SelectedIndexChanged の連鎖発火（OnSeriesChangedAsync → ReloadCreditsAsync → lstCredits 再構成 →
    /// OnCreditSelectedAsync）を抑止するためのフラグ（v1.2.0 工程 H-8 ターン 7 で追加）。
    /// このフラグが立っている間、cbo 系の SelectedIndexChanged は早期 return する。
    /// </summary>
    private bool _suppressComboCascade;

    /// <summary>
    /// Draft セッションを 1 トランザクションで DB に書き込む保存サービス（v1.2.0 工程 H-8 ターン 3 で導入）。
    /// 保存ボタン押下で <see cref="CreditSaveService.SaveAsync"/> が呼ばれる。
    /// </summary>
    private readonly CreditSaveService _saveService;

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
    /// v1.2.0 工程 H-9：HTML プレビュー用に IConnectionFactory をフィールドとして保持。
    /// コンストラクタの引数を <c>_factory</c> に詰め直しただけで、追加 DI は不要。
    /// </summary>
    private readonly PrecureDataStars.Data.Db.IConnectionFactory _factory;
    /// <summary>
    /// v1.2.0 工程 H-10：HTML プレビューおよび主題歌役職の columns 抽出で役職テンプレを引くためのリポジトリ。
    /// 旧 SeriesRoleFormatOverridesRepository を撤去し、role_templates 統合テーブルを扱う本リポジトリに
    /// 一本化した。シリーズ別 / 既定の解決は <c>ResolveAsync(role_code, series_id)</c> が担う。
    /// 既存 DI に追加せず、コンストラクタ内で <c>_factory</c> から都度生成する。
    /// </summary>
    private readonly RoleTemplatesRepository _roleTemplatesRepo;
    /// <summary>
    /// v1.2.0 工程 H-10：HTML プレビューでクレジット種別の表示名を解決するためのリポジトリ。
    /// </summary>
    private readonly CreditKindsRepository _creditKindsRepo;
    /// <summary>
    /// v1.2.0 工程 H-11：埋め込みプレビュー描画用のレンダラ（コンストラクタで 1 回だけ生成し使い回す）。
    /// 旧 H-9 の <c>CreditPreviewForm</c> 別ウィンドウは廃止し、本フォーム内の <c>webPreview</c>
    /// （Designer の <see cref="BuildPreviewPane"/> で生成）に直接 HTML を流し込む方式に変更。
    /// </summary>
    private CreditPreviewRenderer _previewRenderer = null!;

    /// <summary>
    /// v1.2.0 工程 H-11：プレビュー再描画の非同期処理が連打されるのを防ぐためのフラグ。
    /// Draft 編集中は秒間複数回 <see cref="RefreshPreviewAsync"/> が呼ばれる可能性があるため、
    /// 描画中なら即座にスキップして「最後の 1 回」だけ確実に反映させる軽量制御。
    /// </summary>
    private bool _isRenderingPreview;
    /// <summary>
    /// v1.2.0 工程 H-11：プレビュー再描画を遅延実行するためのタイマー。
    /// 編集中のキー入力一打ごとに即座に WebBrowser を再描画すると重いので、
    /// 入力後 250ms 待ってから 1 回だけ再描画する Debounce 動作を実装する。
    /// </summary>
    private System.Windows.Forms.Timer? _previewDebounceTimer;

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

        // v1.2.0 工程 H-9 / H-10：HTML プレビューの CreditPreviewRenderer 構築に使うため、factory および
        // role_templates / credit_kinds 用のリポジトリを保持しておく（コンストラクタで都度新規作成しても
        // 良いが、フィールドにしておけば btnPreviewHtml クリック時に毎回作り直す手間が無い）。
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _roleTemplatesRepo = new RoleTemplatesRepository(_factory);
        _creditKindsRepo = new CreditKindsRepository(_factory);

        _lookupCache = new LookupCache(
            _personAliasesRepo, _companyAliasesRepo, _logosRepo,
            _characterAliasesRepo, _songRecRepo, _rolesRepo,
            factory ?? throw new ArgumentNullException(nameof(factory)));

        // v1.2.0 工程 H-8 追加：Draft セッション構築用のローダ。
        // クレジット選択時に DB から全階層を読み込んで CreditDraftSession を作るのに使う。
        _draftLoader = new CreditDraftLoader(
            _cardsRepo, _cardTiersRepo, _cardGroupsRepo,
            _cardRolesRepo, _blocksRepo, _entriesRepo);

        // v1.2.0 工程 H-8 ターン 3 追加：Draft セッションの保存サービス。
        // 保存ボタン押下で SaveAsync が呼ばれて 1 トランザクション内に DB へ反映する。
        _saveService = new CreditSaveService(factory);

        InitializeComponent();

        // ── v1.2.0 工程 H-11：常時表示プレビューの初期化 ──
        // InitializeComponent の後で webPreview が生成されているので、ここで初期 HTML を流し込み、
        // レンダラとデバウンスタイマーを準備する。
        _previewRenderer = new CreditPreviewRenderer(
            _factory,
            _rolesRepo, _roleTemplatesRepo, _creditKindsRepo,
            _cardsRepo, _cardTiersRepo, _cardGroupsRepo, _cardRolesRepo, _blocksRepo, _entriesRepo,
            _lookupCache);
        webPreview.DocumentText = "<html><body style='font-family:sans-serif;color:#999;padding:24px'>"
            + "（クレジット未選択）</body></html>";
        // デバウンス：250ms 経過後に 1 回だけ RefreshPreviewAsync を実行する仕組み。
        // Tick で一旦タイマーを止めてから描画関数を呼ぶ。
        _previewDebounceTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _previewDebounceTimer.Tick += async (_, __) =>
        {
            _previewDebounceTimer.Stop();
            await RefreshPreviewAsync();
        };

        // ── 左ペイン：選択コンボのイベント結線 ──
        rbScopeSeries.CheckedChanged  += async (_, __) => await OnScopeChangedAsync();
        rbScopeEpisode.CheckedChanged += async (_, __) => await OnScopeChangedAsync();
        cboSeries.SelectedIndexChanged += async (_, __) => await OnSeriesChangedAsync();
        // v1.2.0 工程 H-8 ターン 6：エピソード切替時も未保存確認を行うため、専用ハンドラを経由する。
        cboEpisode.SelectedIndexChanged += async (_, __) => await OnEpisodeChangedAsync();
        lstCredits.SelectedIndexChanged += async (_, __) => await OnCreditSelectedAsync();

        // ── ツリー：選択時のプレビュー反映＋ボタン状態切替 ──
        treeStructure.AfterSelect += (_, __) => { OnTreeNodeSelected(); UpdateButtonStates(); };

        // ── v1.2.0 工程 B-2 追加：左ペインのクレジット系編集ボタン 3 個を結線 ──
        btnNewCredit.Click       += async (_, __) => await OnNewCreditAsync();
        btnCopyCredit.Click      += async (_, __) => await OnCopyCreditAsync();
        // v1.2.0 工程 H-11：btnPreviewHtml は廃止（プレビュー常時表示化に伴い）。
        // 旧コード: btnPreviewHtml.Click += (_, __) => OnPreviewHtml();
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

        // v1.2.0 工程 H-8 ターン 3 追加：Draft セッションの保存・取消ボタン結線。
        // 保存ボタン押下で CreditSaveService.SaveAsync を 1 トランザクションで実行、
        // 取消ボタン押下で現在の Draft セッションを破棄して DB から再読み込みする。
        btnSaveDraft.Click   += async (_, __) => await OnSaveDraftAsync();
        btnCancelDraft.Click += async (_, __) => await OnCancelDraftAsync();

        // ── v1.2.0 工程 B-3 追加：エントリ追加ボタンの結線 ──
        // 押下時：選択中の Block（または Entry の親 Block）配下に新規エントリ追加モードで
        // 右ペイン EntryEditorPanel を開く。INSERT は EntryEditorPanel 内の「保存」ボタンで実行。
        btnAddEntry.Click += async (_, __) => await OnAddEntryAsync();

        // ── v1.2.0 工程 B-3 追加：EntryEditorPanel からのイベント購読 ──
        // 保存／削除完了時にツリー再構築。EntryEditorPanel.Initialize(repo, lookupCache) は
        // OnLoadAsync の冒頭で実行する（OnLoadAsync が依存関係を全部ロードする責務）。
        // EntrySaved / EntryDeleted は Func<Task>? 型（v1.2.0 工程 H-8 ターン 5 終盤で変更）。
        // += ではなく = で結線し、await で確実にツリー再構築を完了させる。
        entryEditor.EntrySaved   = () => OnEntryEditorChangedAsync(reselectLastEdited: true);
        entryEditor.EntryDeleted = () => OnEntryEditorChangedAsync(reselectLastEdited: false);

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

        // v1.2.0 工程 H-8 ターン 6：フォーム閉じ時に未保存変更があれば確認ダイアログを出す。
        // FormClosing は同期コンテキストで動くため async ハンドラから直接 await できないが、
        // 「未保存があるなら一度キャンセルして保存処理を await したあと改めて Close する」という
        // パターンで対処する（_isClosingProgrammatically フラグで再帰防止）。
        FormClosing += OnFormClosing;

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

            // v1.2.0 工程 H 補修：BlockEditorPanel に依存性を流し込み、適用イベントを購読。
            // ターン 5 で Draft 経由に切替：BlocksRepository 引数は撤去（Draft.Entity を直接編集する設計）。
            blockEditor.Initialize(
                _companyAliasesRepo,
                _companiesRepo,
                _lookupCache);
            // ブロックプロパティの Draft 反映後はツリーを再構築して値（特に「N cols, M entries」表示と背景色）を更新する。
            // BlockSaved は Func<Task>? 型なので += ではなく代入で結線する（複数購読不要なので問題なし）。
            // EventHandler 型にすると async void 風 continuation が UI メッセージポンプ待ちで保留されて
            // 画面に反映されない問題が起きるため、Func<Task> + await で確実に完了させる。
            blockEditor.BlockSaved = async () => await RebuildTreeFromDraftAsync();

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
        // v1.2.0 工程 H-8 ターン 7：話数コピー処理中のプログラム由来切替は抑止。
        if (_suppressComboCascade) return;
        // v1.2.0 工程 H-8 ターン 5：cboSeries の SelectedIndexChanged 連鎖発火による多重実行を防ぐ。
        if (_isReloadingSeries) return;

        // v1.2.0 工程 H-8 ターン 6：シリーズ切替で lstCredits の DataSource が再構成されると
        // 現在表示中のクレジットが事実上失われるため、未保存変更がある場合は確認ダイアログを出す。
        // キャンセルが選ばれたら cboSeries の選択を元に戻す。
        if (_suppressCreditSelection) return; // 戻し処理中の再発火は無視
        if (_draftSession is not null && _draftSession.HasUnsavedChanges)
        {
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok)
            {
                // キャンセル：実装の簡易化のため、ここではシリーズ選択を元に戻すのではなく
                // 「警告して何もしない（DataSource は再構成されない）」という挙動を取る。
                // ただし cboSeries.SelectedIndex を直接操作すると再帰呼び出しの恐れがあるので、
                // _suppressCreditSelection を立てた上で静的に元の値（_lastCreditListIndex で参照される
                // クレジットが属するシリーズ）に戻すのが理想。だが現実的にはユーザーの「キャンセル」は
                // 「シリーズ切替自体を取りやめたい」という意図なので、何もしないのは UX 違和感あり。
                // → 暫定実装：エピソード／クレジット側だけ抑止して、シリーズコンボの値はユーザーが選んだ
                //    新シリーズのまま残す（後ほどエピソード／クレジットを選びなおせば戻れる）。
                return;
            }
        }

        _isReloadingSeries = true;
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
        finally { _isReloadingSeries = false; }
    }

    /// <summary>
    /// エピソード切替ハンドラ（v1.2.0 工程 H-8 ターン 6 で導入）。
    /// 未保存変更がある状態でエピソードを切り替えると、最終的に <see cref="lstCredits"/> の
    /// DataSource が再構成されて現在表示中のクレジットが事実上失われるため、ここで確認ダイアログを出す。
    /// 確認 OK なら <see cref="ReloadCreditsAsync"/> を呼んで実際の再読込を行う。
    /// </summary>
    private async Task OnEpisodeChangedAsync()
    {
        // v1.2.0 工程 H-8 ターン 7：話数コピー処理中のプログラム由来切替は抑止。
        if (_suppressComboCascade) return;
        // OnSeriesChangedAsync 経由で連鎖呼び出しされる場合は、既にあちらで確認済みなので
        // 改めてダイアログを出さないようにする（_suppressCreditSelection を一時利用）。
        if (_suppressCreditSelection) { await ReloadCreditsAsync(); return; }

        if (_draftSession is not null && _draftSession.HasUnsavedChanges)
        {
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok) return;  // キャンセル：エピソードコンボの値はユーザー操作のまま残し、再読込しない
        }
        await ReloadCreditsAsync();
    }

    /// <summary>
    /// クレジット一覧を絞り込み条件で再読込し、ListBox に流し込む。
    /// scope_kind と series_id / episode_id だけで絞り込む（v1.2.0 工程 B' 再修正で
    /// 本放送限定フラグはエントリ単位に移管したため、クレジット側の絞り込み条件には含めない）。
    /// </summary>
    private async Task ReloadCreditsAsync()
    {
        // v1.2.0 工程 H-8 ターン 5：cboEpisode の SelectedIndexChanged 連鎖発火による多重実行を防ぐ。
        if (_isReloadingCredits) return;
        _isReloadingCredits = true;
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
            // DataSource を再代入する前に「クレジットリストの母集合が変わる」ことを記録する。
            // _lastCreditListIndex は古いリスト基準の値だったので、リストが入れ替わった以上は
            // -1 にリセットして「未選択」状態にしておく（v1.2.0 工程 H-8 ターン 6）。
            _lastCreditListIndex = -1;

            // v1.2.0 工程 H-11：表示順は credit_kinds.display_order に従う（OP=10, ED=20 が既定なので
            // 結果的に「OP → ED」の順に並ぶ）。マスタを毎回引くと重いので、簡易的に CreditKind 文字列の
            // 辞書順（"OP" < "ED" は文字列上 "ED" < "OP" になってしまうので、ED/OP の優先順を明示する）。
            // OP / ED 以外のコードがマスタに追加された場合に備え、未知コードは末尾に回す。
            int KindOrder(string k) => k switch { "OP" => 1, "ED" => 2, _ => 999 };
            var sortedCredits = credits
                .OrderBy(c => KindOrder(c.CreditKind))
                .ThenBy(c => c.CreditKind)
                .ThenBy(c => c.PartType ?? "")
                .ToList();

            lstCredits.DataSource = sortedCredits
                .Select(c => new CreditListItem(c, BuildCreditListLabel(c)))
                .ToList();

            if (sortedCredits.Count == 0)
            {
                _currentCredit = null;
                ClearTreeAndPreview();
                lblStatusBar.Text = "現在編集中: （該当クレジットなし）";
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _isReloadingCredits = false; }
    }

    /// <summary>クレジットリストボックスのラベルを生成。</summary>
    private static string BuildCreditListLabel(Credit c)
        => $"#{c.CreditId}  {c.CreditKind}  ({c.Presentation})";

    /// <summary>
    /// クレジット選択時：プロパティを左下に表示し、中央ツリーを構築する。
    /// </summary>
    private async Task OnCreditSelectedAsync()
    {
        // v1.2.0 工程 H-8 ターン 7：話数コピー処理中のプログラム由来切替は抑止。
        if (_suppressComboCascade) return;
        // v1.2.0 工程 H-8 ターン 5：ListBox の SelectedIndexChanged 連鎖発火による多重実行を防ぐ。
        // 既に処理中の呼び出しがあれば即 return（フィールド更新が走っている最中の重複呼び出しを抑止）。
        if (_isLoadingCredit) return;

        // v1.2.0 工程 H-8 ターン 6：プログラムから SelectedIndex を戻したことによる再発火は無視する。
        if (_suppressCreditSelection) return;

        // v1.2.0 工程 H-8 ターン 6：未保存変更がある状態で別クレジットへ切り替える前に
        // 確認ダイアログを出す。キャンセルが選ばれたら lstCredits の選択を元に戻す。
        // 「同じインデックスへの再選択」は変化なしなのでスキップ（_lastCreditListIndex == 現在値）。
        if (lstCredits.SelectedIndex != _lastCreditListIndex)
        {
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok)
            {
                // ユーザーがキャンセルを選んだ → SelectedIndex を元に戻す。
                // この戻し処理で SelectedIndexChanged が再発火するので、_suppressCreditSelection で抑止。
                _suppressCreditSelection = true;
                try
                {
                    if (_lastCreditListIndex >= 0 && _lastCreditListIndex < lstCredits.Items.Count)
                        lstCredits.SelectedIndex = _lastCreditListIndex;
                    else
                        lstCredits.SelectedIndex = -1;
                }
                finally { _suppressCreditSelection = false; }
                return;
            }
        }

        _isLoadingCredit = true;
        try
        {
            if (lstCredits.SelectedItem is not CreditListItem item)
            {
                _currentCredit = null;
                _draftSession = null;
                _lastCreditListIndex = -1;
                ClearTreeAndPreview();
                return;
            }
            _currentCredit = item.Credit;
            _lastCreditListIndex = lstCredits.SelectedIndex;

            // プロパティ反映（B-1 では read-only として表示するだけ）
            rbPresentationCards.Checked = (_currentCredit.Presentation == "CARDS");
            rbPresentationRoll.Checked  = (_currentCredit.Presentation == "ROLL");
            // PartType は NULL なら ""（規定位置）アイテムを選ぶ
            cboPartType.SelectedValue = _currentCredit.PartType ?? "";
            txtCreditNotes.Text = _currentCredit.Notes ?? "";

            // ステータスバー更新
            await UpdateStatusBarAsync();

            // ── v1.2.0 工程 H-8 追加：Draft セッション構築 ──
            // 旧来の「DB を直接読んでツリー描画」から「DB → Draft → ツリー描画」に切り替え。
            // 編集操作はすべてこの Draft オブジェクトに対して行い、保存ボタンで一括確定する設計。
            _draftSession = await _draftLoader.LoadAsync(_currentCredit);
            // 右ペインのエディタに最新の Draft セッション参照を流し込む。
            // EntryEditorPanel が新規 DraftEntry の Temp ID を払い出すために必要。
            entryEditor.SetSession(_draftSession);

            // 中央ペインのツリー再構築（Draft 経由）
            await RebuildTreeFromDraftAsync();

            // v1.2.0 工程 B-2: クレジット選択直後はツリー上にノード未選択なので、
            // クレジットレベルのボタン（左ペイン）と「+ カード」だけが有効。
            UpdateButtonStates();

            // v1.2.0 工程 H-9：プレビューウィンドウが開いていればクレジット切替に追従して再描画
            await RefreshPreviewAsync();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _isLoadingCredit = false; }
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
        // v1.2.0 工程 H-8 ターン 6 追加：未保存変更がある場合はステータスバーに「★ 未保存」マークを表示。
        // ツリー背景色（黄色）と併せて、ユーザーが保存忘れに気付きやすくする。
        string unsavedMark = (_draftSession is not null && _draftSession.HasUnsavedChanges)
            ? "  ★ 未保存の変更あり"
            : "";
        lblStatusBar.Text =
            $"現在編集中: {scope} {idLabel}  /  {_currentCredit.CreditKind}  ({_currentCredit.Presentation}){unsavedMark}";
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
        // v1.2.0 工程 H-8 ターン 2 で Draft 経由に切り替え。本メソッドは互換用ラッパで、
        // 実体は RebuildTreeFromDraftAsync が担う。Draft セッションが未構築の場合は何もしない
        // （クレジット未選択の状態。OnCreditSelectedAsync が呼ばれた時点で session が作られ、
        //  本メソッドが Draft からツリーを描画する流れになる）。
        await RebuildTreeFromDraftAsync();
    }

    /// <summary>
    /// Draft セッション（<see cref="_draftSession"/>）からツリーを構築する（v1.2.0 工程 H-8 ターン 2 で導入）。
    /// 旧 RebuildTreeAsync は DB から直接読み込んでいたが、これからは「クレジット選択時に DB → Draft、
    /// それ以降は Draft → ツリー描画」に統一する。編集操作はすべて Draft オブジェクトに対して行い、
    /// 保存ボタンが押されたときだけ Draft → DB へ反映する。
    /// </summary>
    /// <remarks>
    /// 並列実行による Tree.Nodes 重複追加を防ぐため、旧来通り「先にローカル List にすべての TreeNode を
    /// 組み立てきる → 最後に同期セクションで Clear → AddRange → EndUpdate を一気に実行」パターン。
    /// </remarks>
    private async Task RebuildTreeFromDraftAsync()
    {
        if (_currentCredit is null || _draftSession is null) { ClearTreeAndPreview(); return; }

        // ─── フェーズ 1: Draft からツリーを組み立て（この間 treeStructure には触らない）───
        // Draft の Cards / Tiers / Groups / Roles / Blocks / Entries を順に走査。
        // Deleted 状態のものはツリーには出さない（ユーザーから見えなくする）。
        var newRootNodes = new List<TreeNode>();

        // ツリー上の Card 番号は 1 始まりの連続表示にする（v1.2.0 工程 H 補修）。
        int cardDisplayIndex = 1;
        foreach (var draftCard in _draftSession.Root.Cards.Where(c => c.State != DraftState.Deleted))
        {
            var card = draftCard.Entity;
            var cardNode = new TreeNode($"📂 Card #{cardDisplayIndex}{(string.IsNullOrEmpty(card.Notes) ? "" : "  " + card.Notes)}")
            {
                Tag = new NodeTag(NodeKind.Card, draftCard.CurrentId, draftCard)
            };
            cardDisplayIndex++;

            foreach (var draftTier in draftCard.Tiers.Where(t => t.State != DraftState.Deleted)
                                                       .OrderBy(t => t.Entity.TierNo))
            {
                var tier = draftTier.Entity;
                var tierKey = new TierKey(card.CardId, tier.CardTierId, tier.TierNo);
                var tierNode = new TreeNode($"📐 Tier {tier.TierNo}")
                {
                    Tag = new NodeTag(NodeKind.Tier, draftTier.CurrentId, draftTier)
                };

                foreach (var draftGroup in draftTier.Groups.Where(g => g.State != DraftState.Deleted)
                                                              .OrderBy(g => g.Entity.GroupNo))
                {
                    var grp = draftGroup.Entity;
                    var groupKey = new GroupKey(card.CardId, tier.CardTierId, tier.TierNo, grp.CardGroupId, grp.GroupNo);
                    var groupNode = new TreeNode($"🗂 Group {grp.GroupNo}")
                    {
                        Tag = new NodeTag(NodeKind.Group, draftGroup.CurrentId, draftGroup)
                    };

                    int roleDisplayIndex = 1;
                    foreach (var draftRole in draftGroup.Roles.Where(r => r.State != DraftState.Deleted)
                                                                  .OrderBy(r => r.Entity.OrderInGroup))
                    {
                        var role = draftRole.Entity;
                        string roleName = await _lookupCache.ResolveRoleNameAsync(role.RoleCode);

                        // 役職テンプレ展開（既存と同じロジック、Role エンティティは DB から再取得）
                        Role? roleEntity = string.IsNullOrEmpty(role.RoleCode)
                            ? null
                            : await _rolesRepo.GetByCodeAsync(role.RoleCode);
                        string roleNote = "";
                        bool isThemeSongRole = (roleEntity?.RoleFormatKind == "THEME_SONG");
                        if (isThemeSongRole && !string.IsNullOrEmpty(role.RoleCode))
                        {
                            // v1.2.0 工程 H-10 / H-12：roles.default_format_template が撤去されたため、
                            // 主題歌役職の columns 抽出はここで RoleTemplatesRepository.ResolveAsync 経由で
                            // テンプレを引いてから ExtractThemeSongsColumns に渡す。
                            // SERIES スコープなら credit.SeriesId、EPISODE スコープなら episodes 経由で逆引き
                            // した series_id を渡すことで「シリーズ専用テンプレ」を正しく解決させる。
                            int? seriesIdForResolve;
                            if (_currentCredit?.ScopeKind == "SERIES")
                            {
                                seriesIdForResolve = _currentCredit?.SeriesId;
                            }
                            else if (_currentCredit?.EpisodeId is int eid && eid > 0)
                            {
                                // 軽量に逆引き（EpisodesRepository に GetByIdAsync が無いため
                                // 直接生 SQL で series_id を取得）
                                await using var conn = await _factory.CreateOpenedAsync();
                                seriesIdForResolve = await Dapper.SqlMapper.ExecuteScalarAsync<int?>(conn,
                                    new Dapper.CommandDefinition(
                                        "SELECT series_id FROM episodes WHERE episode_id = @eid LIMIT 1;",
                                        new { eid }));
                            }
                            else
                            {
                                seriesIdForResolve = null;
                            }
                            var tpl = await _roleTemplatesRepo.ResolveAsync(role.RoleCode!, seriesIdForResolve);
                            int columns = ExtractThemeSongsColumns(tpl?.FormatTemplate);
                            if (columns >= 2) roleNote = $"  [横 {columns} カラム表示指定]";
                        }

                        var roleNode = new TreeNode($"📋 Role: {roleName}  (order {roleDisplayIndex}){roleNote}")
                        {
                            Tag = new NodeTag(NodeKind.CardRole, draftRole.CurrentId, draftRole)
                        };
                        roleDisplayIndex++;

                        // 主題歌役職の場合：episode_theme_songs から楽曲情報を引いて、楽曲サブノードを差し込む。
                        // 主題歌は Draft 化対象外（episode_theme_songs は別マスタなので、クレジット編集の保存待ち
                        // 範疇に入れない）。即時取得して仮想ノードとして表示するだけで、削除/並べ替え不可。
                        if (isThemeSongRole && _currentCredit?.ScopeKind == "EPISODE" && _currentCredit.EpisodeId is int epId)
                        {
                            await AddThemeSongVirtualNodesAsync(roleNode, epId, role.RoleCode ?? "");
                        }

                        int blockDisplayIndex = 1;
                        foreach (var draftBlock in draftRole.Blocks.Where(b => b.State != DraftState.Deleted)
                                                                       .OrderBy(b => b.Entity.BlockSeq))
                        {
                            var block = draftBlock.Entity;
                            // ブロック内エントリ：Deleted を除外、is_broadcast_only ASC, entry_seq ASC で並べる
                            var entries = draftBlock.Entries
                                .Where(en => en.State != DraftState.Deleted)
                                .OrderBy(en => en.Entity.IsBroadcastOnly)
                                .ThenBy(en => en.Entity.EntrySeq)
                                .ToList();

                            // v1.2.0 工程 H-9：先頭企業屋号 (leading_company_alias_id) が設定されていれば
                            // ブロックラベルに名前を併記する（連載役職などで「どの出版社か」が一目で分かるように）。
                            // 屋号名は LookupCache 経由で引き、設定なしなら何も表示しない。
                            string leadingLabel = "";
                            if (block.LeadingCompanyAliasId is int lcid)
                            {
                                string? lname = await _lookupCache.LookupCompanyAliasNameAsync(lcid);
                                if (!string.IsNullOrEmpty(lname)) leadingLabel = $"  先頭=「{lname}」";
                                else leadingLabel = $"  先頭=#{lcid}";
                            }

                            var blockNode = new TreeNode(
                                $"🔵 Block #{blockDisplayIndex}  ({block.ColCount} cols, {entries.Count} entries){leadingLabel}")
                            {
                                Tag = new NodeTag(NodeKind.Block, draftBlock.CurrentId, draftBlock)
                            };
                            blockDisplayIndex++;

                            int displayIndex = 1;
                            foreach (var draftEntry in entries)
                            {
                                var entry = draftEntry.Entity;
                                string preview = await _lookupCache.BuildEntryPreviewAsync(entry);
                                string prefix = entry.EntryKind switch
                                {
                                    "PERSON"          => "🟢 [PERSON]         ",
                                    "CHARACTER_VOICE" => "🟣 [CHARACTER_VOICE]",
                                    "COMPANY"         => "🟠 [COMPANY]        ",
                                    "LOGO"            => "🟡 [LOGO]           ",
                                    "TEXT"            => "⚪ [TEXT]            ",
                                    _                 => "❓ [UNKNOWN]        "
                                };
                                var entryNode = new TreeNode($"{prefix} #{displayIndex}  {preview}")
                                {
                                    Tag = new NodeTag(NodeKind.Entry, draftEntry.CurrentId, draftEntry)
                                };
                                blockNode.Nodes.Add(entryNode);
                                displayIndex++;
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

        // ─── フェーズ 2: 同期セクションで treeStructure を一気に更新 ───
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

        // 未保存変更があれば背景色を黄色っぽく（v1.2.0 工程 H-8 ターン 2 で導入）。
        // 視覚的に「保存待ち」を示すため、TreeView 全体の背景色を変える。
        ApplyDraftBackgroundColor();

        // v1.2.0 工程 H-8 ターン 5 終盤の修正：
        // ① TreeView の表示更新を画面へ反映させるため Refresh を強制呼び出し。Clear/AddRange の直後は
        //    まれに描画が遅延することがあるため、保険として明示的に Invalidate + Update する。
        // ② 末尾で blockEditor / entryEditor を ClearAndDisable していたが、編集中に再構築が走った
        //    場合に右ペインの状態（_currentDraft）が消えるため、削除した。右ペインの状態は選択ノード
        //    変更時の OnTreeNodeSelected で適切に切り替わるので、ここでクリアする必要はない。
        // ※ Application.DoEvents() は SelectedIndexChanged 連鎖発火など別バグの温床になり得るため
        //    入れない。本来の真因（OnCreditSelectedAsync 等の再入による _draftSession 多重生成）は
        //    各ハンドラの再入防止フラグで根本対処済み。
        treeStructure.Refresh();

        // v1.2.0 工程 H-11：Draft 編集のリアルタイムプレビュー反映。
        // ツリー再構築は Draft 構造（Card/Tier/Group/Role/Block/Entry）が変わるたびに呼ばれる
        // 共通点なので、ここで RequestPreviewRefresh を 1 回呼べば全ての編集操作（追加・削除・移動・
        // エントリ編集）に追従できる。デバウンスで 250ms 後に 1 回だけ描画されるので連打にも強い。
        RequestPreviewRefresh();
    }

    /// <summary>
    /// 未保存変更の有無に応じてツリー背景色を切り替える（v1.2.0 工程 H-8 ターン 2 で導入）。
    /// 未保存変更ありなら LightYellow 系、なしなら標準のウィンドウ色（白）。
    /// </summary>
    private void ApplyDraftBackgroundColor()
    {
        bool dirty = (_draftSession is not null && _draftSession.HasUnsavedChanges);
        if (dirty)
        {
            // 控えめな黄色（標準のウィンドウ色から少し黄味を足したくらい）
            treeStructure.BackColor = Color.FromArgb(0xFF, 0xFF, 0xE0);
        }
        else
        {
            treeStructure.BackColor = SystemColors.Window;
        }
        // 保存・取消ボタンの Enabled 状態も同時に反映する（v1.2.0 工程 H-8 ターン 3）。
        // 未保存変更がある時のみ有効にすることで、押し間違いを防ぐ。
        btnSaveDraft.Enabled = dirty;
        btnCancelDraft.Enabled = dirty;

        // v1.2.0 工程 H-8 ターン 6 追加：ステータスバーの「★ 未保存の変更あり」マークを同期更新する。
        // UpdateStatusBarAsync を再実行すると DB アクセスが走って高コストなので、
        // 既存のテキストの末尾だけを操作する形で軽量に切り替える。
        const string mark = "  ★ 未保存の変更あり";
        if (_currentCredit is not null)
        {
            string text = lblStatusBar.Text;
            bool hasMark = text.EndsWith(mark);
            if (dirty && !hasMark)
            {
                lblStatusBar.Text = text + mark;
            }
            else if (!dirty && hasMark)
            {
                lblStatusBar.Text = text.Substring(0, text.Length - mark.Length);
            }
        }
    }

    /// <summary>
    /// 保存ボタン押下処理（v1.2.0 工程 H-8 ターン 3 で導入）。
    /// 現在の Draft セッションを <see cref="CreditSaveService.SaveAsync"/> で 1 トランザクション内に DB へ反映し、
    /// 成功したらツリーを再構築して背景色を通常状態に戻す。失敗時はロールバックされて Draft はそのまま残るので、
    /// ユーザーは修正してリトライできる。
    /// </summary>
    private async Task OnSaveDraftAsync()
    {
        if (_draftSession is null) return;
        try
        {
            // 保存前に「本当に保存するか」確認したいケースもあるが、ターン 3 では暫定的に即実行。
            // ターン 6 以降で「未保存変更がある状態でクレジット切替」など別 UI フローと整合させる。
            await _saveService.SaveAsync(_draftSession, Environment.UserName);

            // 保存成功 → ツリー再構築（DB の最新値が Draft に既に反映されているはずだが、
            // 安全のため DB から再読み込みする）。
            if (_currentCredit is not null)
            {
                // v1.2.0 工程 H-8 ターン 7：話数コピー後の保存では、コピー元の credit_id ではなく
                // CreditSaveService が採番した新 credit_id（_currentCredit.CreditId に書き戻されている）が
                // 既に入っているため、これをそのまま再ロードに使える。
                _draftSession = await _draftLoader.LoadAsync(_currentCredit);
                // v1.2.0 工程 H-8 ターン 5：右ペインのエディタに最新の Draft セッション参照を流し込む。
                // EntryEditorPanel が新規 DraftEntry の Temp ID を払い出すために必要。
                entryEditor.SetSession(_draftSession);
                await RebuildTreeFromDraftAsync();

                // v1.2.0 工程 H-8 ターン 7：話数コピーで新規作成されたクレジットの場合、ListBox の
                // 表示母集合（コピー先エピソード）を改めて読み直して、新クレジットを選択状態にする。
                // クレジットプロパティの保存（OnSaveCreditPropsAsync）でも同等の処理をしている。
                int keepId = _currentCredit.CreditId;
                await ReloadCreditsAsync();
                SelectCreditInListBox(keepId);
            }
            // v1.2.0 工程 H-9：保存後もプレビューを再描画（DB の最新状態に追従）
            await RefreshPreviewAsync();
            MessageBox.Show(this, "保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 取消ボタン押下処理（v1.2.0 工程 H-8 ターン 3 で導入）。
    /// 現在の Draft セッションを破棄して DB から再読み込みし、ツリーを最新の DB 状態で描画し直す。
    /// </summary>
    private async Task OnCancelDraftAsync()
    {
        if (_draftSession is null || _currentCredit is null) return;
        try
        {
            if (_draftSession.HasUnsavedChanges)
            {
                if (MessageBox.Show(this,
                    "未保存の変更を破棄して、DB から最新状態を再読み込みします。よろしいですか？",
                    "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                    return;
            }
            _draftSession = await _draftLoader.LoadAsync(_currentCredit);
            // v1.2.0 工程 H-8 ターン 5：右ペインのエディタに最新の Draft セッション参照を流し込む。
            // EntryEditorPanel が新規 DraftEntry の Temp ID を払い出すために必要。
            entryEditor.SetSession(_draftSession);
            await RebuildTreeFromDraftAsync();
            // v1.2.0 工程 H-9：取消後もプレビューを再描画（DB の最新状態に追従）
            await RefreshPreviewAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 未保存変更ライフサイクルの確認ヘルパ（v1.2.0 工程 H-8 ターン 6 で導入）。
    /// <para>
    /// クレジット切替・シリーズ／エピソード切替・フォーム閉じなど、現在の Draft セッションが
    /// 失われる可能性のある操作の前に呼び出して、未保存変更がある場合の確認ダイアログを出す。
    /// </para>
    /// <para>
    /// ダイアログは「保存して続行 / 破棄して続行 / キャンセル」の 3 択。
    /// 戻り値が <c>true</c> なら呼び出し元は処理を続行できる（保存または破棄が選ばれた）。
    /// 戻り値が <c>false</c> なら呼び出し元は処理を中断する（キャンセルが選ばれた）。
    /// </para>
    /// <para>
    /// 未保存変更が無い場合（<c>_draftSession?.HasUnsavedChanges == false</c>）はダイアログを出さずに
    /// 即座に <c>true</c> を返す。<c>_draftSession</c> 自体が null の場合（クレジット未選択時）も同様。
    /// </para>
    /// </summary>
    /// <returns>処理を続行してよければ <c>true</c>、ユーザーがキャンセルしたら <c>false</c>。</returns>
    private async Task<bool> ConfirmUnsavedChangesAsync()
    {
        if (_draftSession is null || !_draftSession.HasUnsavedChanges) return true;

        // 「現在編集中のクレジット」をダイアログ文面に表示するため、ラベル文字列を組み立てる。
        // _currentCredit が null の場合（理論上ありえないが）は無難なフォールバック文言。
        string label = _currentCredit is null
            ? "現在のクレジット"
            : $"#{_currentCredit.CreditId} {_currentCredit.CreditKind} ({_currentCredit.Presentation})";

        var result = MessageBox.Show(this,
            $"「{label}」に未保存の変更があります。\n\n"
            + "[はい]   = 保存してから続行\n"
            + "[いいえ] = 変更を破棄して続行\n"
            + "[キャンセル] = 操作を取りやめて元の状態に戻す",
            "未保存の変更があります", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        switch (result)
        {
            case DialogResult.Yes:
                // 保存して続行：CreditSaveService で 1 トランザクション保存を実行。
                // 失敗した場合は例外が出るので、呼び出し元の catch にバブルアップさせる
                //（保存失敗時に「続行」してしまうとデータロストになるため）。
                try
                {
                    await _saveService.SaveAsync(_draftSession, Environment.UserName);
                    return true;
                }
                catch (Exception ex)
                {
                    ShowError(ex);
                    // 保存失敗 → ユーザーに改めて選択させるべきだが、安全側に倒して中断扱い。
                    // 呼び出し元は false なので元の状態を維持する。
                    return false;
                }
            case DialogResult.No:
                // 破棄して続行：何もしないでそのまま続行（呼び出し元が新しいセッションをロードする）。
                return true;
            default:
                // キャンセル：呼び出し元は元の状態に戻す責任がある。
                return false;
        }
    }

    /// <summary>
    /// フォーム閉じハンドラ（v1.2.0 工程 H-8 ターン 6 で導入）。
    /// <para>
    /// 未保存変更がある状態でフォームを閉じようとした時に確認ダイアログを出す。
    /// 「保存して閉じる」が選ばれた場合は <see cref="ConfirmUnsavedChangesAsync"/> 内で
    /// 保存処理が走った後に閉じる。
    /// </para>
    /// <para>
    /// FormClosing は同期コンテキストで呼ばれるため、async タスクを await できない。
    /// そこで「保存処理を await したい」場合は一度 <c>e.Cancel = true</c> で閉じるのを止め、
    /// 別途 async メソッドで保存を走らせ、完了後に <c>_isClosingProgrammatically = true</c> を立てて
    /// 改めて <see cref="Form.Close"/> を呼び直すパターンで対応する。
    /// </para>
    /// </summary>
    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // 既に「プログラム由来の Close」中であれば確認は不要（保存後の再 Close）。
        if (_isClosingProgrammatically) return;

        // 未保存変更が無ければ確認なしで閉じる。
        if (_draftSession is null || !_draftSession.HasUnsavedChanges) return;

        // ここから先は確認ダイアログを出す。閉じる動作はキャンセルし、
        // 答え次第で改めて Close を再発行する。
        e.Cancel = true;

        try
        {
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok) return;  // 「キャンセル」が選ばれたらフォームを閉じないままにする

            // 「保存して閉じる」または「破棄して閉じる」が選ばれた場合：プログラム由来の
            // Close を再発行（このときは _isClosingProgrammatically が true なので確認スキップ）。
            _isClosingProgrammatically = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            _isClosingProgrammatically = false;
        }
    }

    /// <summary>
    /// ツリーノード選択時：Entry なら EntryEditorPanel に編集モードで読み込む、
    /// それ以外（Card/Role/Block/クレジット直下）の場合は EntryEditorPanel を非アクティブ化する。
    /// </summary>
    private async void OnTreeNodeSelected()
    {
        try
        {
            // v1.2.0 工程 H-8 ターン 5 ：Draft オブジェクト経由で右ペインエディタを切り替える。
            // ツリーノードの Tag.Payload には Draft オブジェクト本体（DraftBlock / DraftEntry 等）が
            // 入っているので、種別判定後にそれを直接 LoadBlockAsync / LoadForEditAsync に渡す。
            if (treeStructure.SelectedNode?.Tag is not NodeTag tag)
            {
                entryEditor.ClearAndDisable();
                blockEditor.ClearAndDisable();
                entryEditor.Visible = true;
                blockEditor.Visible = false;
                return;
            }

            // Block 選択時 → BlockEditorPanel に Draft オブジェクト本体を渡す
            if (tag.Kind == NodeKind.Block && tag.Payload is DraftBlock draftBlk)
            {
                entryEditor.ClearAndDisable();
                entryEditor.Visible = false;
                blockEditor.Visible = true;
                await blockEditor.LoadBlockAsync(draftBlk);
                return;
            }

            // Entry 選択時 → EntryEditorPanel に Draft オブジェクト本体を渡す
            if (tag.Kind == NodeKind.Entry && tag.Payload is DraftEntry draftEntry)
            {
                blockEditor.ClearAndDisable();
                blockEditor.Visible = false;
                entryEditor.Visible = true;
                await entryEditor.LoadForEditAsync(draftEntry);
                return;
            }

            // それ以外（Card / Tier / Group / CardRole / ThemeSongVirtual）→ 両エディタ非アクティブ
            entryEditor.ClearAndDisable();
            blockEditor.ClearAndDisable();
            entryEditor.Visible = true;
            blockEditor.Visible = false;
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
            string label = $"📀 Song({r.ThemeKind}): {noncreditedMark}{broadcastMark}「{title}」{variant}{detail}";
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
        // v1.2.0 工程 H-8 ターン 6.5 で復活：クレジット本体プロパティの保存・削除は即時 DB 反映系。
        // Draft（中央ペインの編集セッション）とは独立した操作なので、未保存変更ありの状態でも
        // 押せるように単純に「クレジットが選択されているか」だけでガードする。
        // ただしハンドラ側で「未保存の Draft 変更があれば先に処理してくれ」と警告を出す。
        btnSaveCreditProps.Enabled = hasCredit;
        btnDeleteCredit.Enabled = hasCredit;
        // v1.2.0 工程 H-8 ターン 7：話数コピーはクレジット選択中のみ有効。
        btnCopyCredit.Enabled = hasCredit;
        // v1.2.0 工程 H-11：HTML プレビューはボタン廃止のため Enable 制御不要。
        // 代わりに常時表示の埋め込みプレビューが、クレジット未選択時には「（クレジット未選択）」と表示する。

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
                btnAddGroup.Enabled = true;
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
                btnAddEntry.Enabled = true;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.Entry:
                btnAddCard.Enabled  = true;
                btnAddTier.Enabled  = false;
                btnAddGroup.Enabled = false;
                btnAddRole.Enabled  = false;
                btnAddBlock.Enabled = false;
                btnAddEntry.Enabled = true;
                btnMoveUp.Enabled = btnMoveDown.Enabled = true;
                btnDeleteNode.Enabled = true;
                break;
            case NodeKind.ThemeSongVirtual:
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

        // v1.2.0 工程 H-8 ターン 5 完了：Block / Entry の編集も Draft 経由になり、
        // ターン 4 にあった暫定オーバーライドは撤去された。各 case ブロックの設定がそのまま有効。
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
    /// クレジット話数コピー（v1.2.0 工程 H-8 ターン 7 で導入）。
    /// 現在選択中のクレジット（<see cref="_currentCredit"/>）をコピー元として、
    /// 別シリーズ／別エピソードへ「構造 + エントリ全部」を Draft として複製する。
    /// <para>
    /// フロー：
    /// </para>
    /// <list type="number">
    ///   <item><description>未保存 Draft があれば確認ダイアログを出して片付ける（保存／破棄／キャンセル）</description></item>
    ///   <item><description><see cref="CreditCopyDialog"/> でコピー先（シリーズ／エピソード／presentation/part_type/備考）を選択</description></item>
    ///   <item><description>コピー先に同種クレジット（episode_id × credit_kind）が既にあれば「上書き／中止」確認</description></item>
    ///   <item><description>上書き選択なら既存クレジットを論理削除（即時 DB 反映）</description></item>
    ///   <item><description><see cref="CreditDraftLoader.CloneForCopyAsync"/> でコピー先用の Draft セッションを構築</description></item>
    ///   <item><description>画面をコピー先クレジットに切り替えて Draft を表示。ユーザーは内容を確認した上で「💾 保存」を押下</description></item>
    /// </list>
    /// </summary>
    private async Task OnCopyCreditAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            // 未保存 Draft があれば先に処理してもらう
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok) return;

            // コピー元情報を退避（_currentCredit はコピー先選択後に書き換わるため）
            var srcCredit = _currentCredit;

            // コピー先選択ダイアログ
            using var dlg = new Dialogs.CreditCopyDialog(_seriesRepo, _episodesRepo, _partTypesRepo, srcCredit);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Result is null) return;
            var destEntity = dlg.Result;

            // コピー先に同種クレジット（episode_id × credit_kind）の既存があるか確認。
            // ある場合は「上書き（旧を論理削除）／中止」を選ばせる。
            if (destEntity.EpisodeId is int destEpisodeId)
            {
                var existing = await _creditsRepo.GetByEpisodeAsync(destEpisodeId);
                var conflict = existing.FirstOrDefault(c =>
                    c.CreditKind == destEntity.CreditKind);
                if (conflict is not null)
                {
                    var ans = MessageBox.Show(this,
                        $"コピー先エピソードに既に同種クレジット（#{conflict.CreditId} {conflict.CreditKind} ({conflict.Presentation})）があります。\n\n"
                        + "[はい]   = 既存を論理削除して上書き\n"
                        + "[いいえ] = 操作を中止",
                        "クレジットの重複", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (ans != DialogResult.Yes) return;

                    // 既存を論理削除（即時 DB 反映）。新クレジットは Draft 保存時に INSERT される。
                    await _creditsRepo.SoftDeleteAsync(conflict.CreditId, Environment.UserName);
                }
            }

            // コピー先用の Draft セッションを組み立て（Root.State = Added、配下も全部 Added）
            _draftSession = await _draftLoader.CloneForCopyAsync(srcCredit, destEntity);

            // クレジット ListBox の表示母集合をコピー先エピソードに合わせる。
            // 現在のスコープが EPISODE 以外（SERIES 中）の場合は EPISODE スコープに切り替える必要があるが、
            // 簡易実装として「コピー先エピソードを選択するまでの表示は呼び出し前のまま」にしておく。
            // ユーザーは保存後にエピソード切替で実際のクレジットを確認できる。
            // ここでは画面状態をコピー先 Draft に直接切り替える：
            _currentCredit = destEntity;  // CreditId は 0、保存時に採番
            _lastCreditListIndex = -1;    // ListBox との対応は無くなる（保存後の ReloadCreditsAsync で正しく戻る）

            // 右ペインのエディタを新セッション参照に張り替え
            entryEditor.SetSession(_draftSession);

            // v1.2.0 工程 H-8 ターン 7：cboSeries / cboEpisode をコピー先の値に合わせて切り替える。
            // SelectedIndexChanged の連鎖発火（→ ReloadCreditsAsync → lstCredits 再構成 → OnCreditSelectedAsync）が
            // 走るとコピー先 Draft が破棄されてしまうので、_suppressComboCascade フラグで連鎖を抑止する。
            // ステータスバーの「現在編集中: エピソード 第N話 ...」表示は cboEpisode.SelectedItem を参照するため、
            // この切替は表示の正確性のために必須。
            if (destEntity.EpisodeId is int destEpisodeId2 && dlg.ResultSeriesId is int destSeriesId)
            {
                _suppressComboCascade = true;
                try
                {
                    // EPISODE スコープに切り替え（rbScopeEpisode）
                    rbScopeEpisode.Checked = true;
                    // シリーズコンボをコピー先のシリーズ ID に切替
                    cboSeries.SelectedValue = destSeriesId;
                    // シリーズ切替で本来は cboEpisode.DataSource が更新されるはずだが、抑止フラグで止めている。
                    // ここで明示的にコピー先シリーズのエピソード一覧を読み込んで cboEpisode を再構築する。
                    var eps = await _episodesRepo.GetBySeriesAsync(destSeriesId);
                    cboEpisode.DisplayMember = "Label";
                    cboEpisode.ValueMember = "Id";
                    cboEpisode.DataSource = eps
                        .Select(e => new IdLabel(e.EpisodeId, $"第{e.SeriesEpNo}話  {e.TitleText}"))
                        .ToList();
                    cboEpisode.SelectedValue = destEpisodeId2;
                }
                finally { _suppressComboCascade = false; }
            }

            // ステータスバーに新クレジット情報を表示（上で cboEpisode を切替済みなので正しいラベルになる）
            await UpdateStatusBarAsync();

            // 中央ペインをコピー先 Draft で再構築（ツリーは Added でいっぱい、背景色は黄色）
            await RebuildTreeFromDraftAsync();

            // クレジットプロパティ欄もコピー先の値に追従
            rbPresentationCards.Checked = (destEntity.Presentation == "CARDS");
            rbPresentationRoll.Checked  = (destEntity.Presentation == "ROLL");
            cboPartType.SelectedValue = destEntity.PartType ?? "";
            txtCreditNotes.Text = destEntity.Notes ?? "";

            UpdateButtonStates();

            MessageBox.Show(this,
                "コピー先クレジットを Draft として組み立てました。\n"
                + "内容を確認・編集してから「💾 保存」を押してください。\n"
                + "「✖ 取消」を押せば破棄できます。",
                "コピー完了（Draft）", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 埋め込みプレビューペインを再描画する（v1.2.0 工程 H-11 で導入）。
    /// <para>
    /// 現在の <see cref="_currentCredit"/> と <see cref="_draftSession"/> の状態に応じて、
    /// (a) Draft セッションが構築済みなら <see cref="CreditPreviewRenderer.RenderDraftAsync"/> で
    ///     Draft をリアルタイム反映した HTML、
    /// (b) Draft 未構築だが <see cref="_currentCredit"/> がある（保存済みクレジット）なら
    ///     <see cref="CreditPreviewRenderer.RenderCreditsAsync"/> で DB から SELECT した HTML、
    /// (c) いずれも無いなら「（クレジット未選択）」表示、
    /// を <see cref="webPreview"/> の <c>DocumentText</c> に流し込む。
    /// </para>
    /// <para>
    /// 多重実行防止：<see cref="_isRenderingPreview"/> フラグで描画中は即座にスキップする。
    /// 描画関数自体は async でやや時間がかかる可能性（テンプレ展開で SELECT が走る）があるため、
    /// 連打されても最後の 1 回だけ反映されることを意図している。
    /// </para>
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        if (_isRenderingPreview) return;
        _isRenderingPreview = true;
        try
        {
            string html;
            if (_draftSession is not null && _currentCredit is not null)
            {
                // Draft 経由で描画（編集中のリアルタイム反映）
                html = await _previewRenderer.RenderDraftAsync(_draftSession);
            }
            else if (_currentCredit is not null)
            {
                // Draft 未構築だが保存済みクレジットがある場合：DB ベースで描画（フォールバック）
                html = await _previewRenderer.RenderCreditsAsync(new[] { _currentCredit });
            }
            else
            {
                html = "<html><body style='font-family:sans-serif;color:#999;padding:24px'>"
                     + "（クレジット未選択）</body></html>";
            }
            // WebBrowser の DocumentText 設定はデザイナスレッド上で行われる前提。
            // RefreshPreviewAsync は UI スレッドから呼ばれるので、そのまま代入して問題ない。
            webPreview.DocumentText = html;
        }
        catch (Exception ex)
        {
            // プレビュー失敗はモーダルダイアログ化せず、本文中にエラーを表示する（編集の流れを阻害しない）。
            string esc = System.Net.WebUtility.HtmlEncode(ex.ToString());
            webPreview.DocumentText = $"<html><body style='font-family:sans-serif;color:#c0392b;padding:24px'>"
                + $"<h2>プレビュー生成エラー</h2><pre>{esc}</pre></body></html>";
        }
        finally
        {
            _isRenderingPreview = false;
        }
    }

    /// <summary>
    /// プレビュー再描画を 250ms 後に 1 回だけ実行するよう要求する（デバウンス、v1.2.0 工程 H-11 追加）。
    /// <para>
    /// ツリー再構築・エントリ編集・ブロック編集などのタイミングで連続呼び出されてもまとめて 1 回だけ
    /// 描画される。<see cref="_previewDebounceTimer"/> をリスタートさせるだけのシンプルな実装。
    /// クレジット切替・保存・取消などの「明示的タイミング」では <see cref="RefreshPreviewAsync"/> を
    /// 直接 await して即時反映する方が UX として自然。
    /// </para>
    /// </summary>
    private void RequestPreviewRefresh()
    {
        if (_previewDebounceTimer is null) return;
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    /// <summary>
    /// クレジットプロパティ保存：左ペインの presentation / part_type / notes を反映して
    /// <see cref="CreditsRepository.UpdateAsync"/> を呼ぶ（即時 DB 反映、Draft セッションは経由しない）。
    /// <para>
    /// v1.2.0 工程 H-8 ターン 6.5 で復活：プロパティ編集系は単一行で完結するため、配下の Card/Tier/...
    /// の Draft とは別系統で「即時 DB 反映」とする方針。これにより「ED を誤って ROLL で作っても
    /// プレゼンテーション形式を後から変更できる」要件を満たす。
    /// </para>
    /// <para>
    /// IsBroadcastOnly / CreditKind / scope 系は変えない（変える場合は別行への移し替えになる
    /// ため、専用の操作で行うべきという設計判断）。
    /// </para>
    /// </summary>
    private async Task OnSaveCreditPropsAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            string newPresentation = rbPresentationCards.Checked ? "CARDS" : "ROLL";

            // ─── presentation 切替の妥当性検証 ───
            // CARDS → ROLL：ROLL は「カードは 1 枚（card_seq=1）固定」が制約のため、
            //   Draft 上に有効カードが 2 枚以上ある場合は変更不可。
            // ROLL → CARDS：制約がゆるくなる方向なので無条件で OK。
            if (_currentCredit.Presentation == "CARDS" && newPresentation == "ROLL")
            {
                int liveCardCount = _draftSession?.Root.Cards.Count(c => c.State != DraftState.Deleted) ?? 0;
                if (liveCardCount > 1)
                {
                    MessageBox.Show(this,
                        $"presentation を ROLL に変更できません。\n"
                        + $"ROLL は「カードは 1 枚（card_seq=1）固定」の制約があるため、\n"
                        + $"現在の {liveCardCount} 枚のカードを 1 枚に整理してから変更してください。",
                        "操作不可", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    // ラジオボタンの選択を元に戻す
                    rbPresentationCards.Checked = true;
                    rbPresentationRoll.Checked = false;
                    return;
                }
            }

            // ─── 未保存の Draft 変更がある場合は警告 ───
            // クレジットプロパティ保存は即時 DB 反映だが、Draft は別系統。
            // 同時編集中はユーザーの意図が読み取りにくいので、先にどちらかを片付けるよう促す。
            if (_draftSession is not null && _draftSession.HasUnsavedChanges)
            {
                MessageBox.Show(this,
                    "Draft 側に未保存の変更があります。\n"
                    + "先に「💾 保存」または「✖ 取消」で Draft を片付けてから、クレジットプロパティを保存してください。",
                    "操作不可", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _currentCredit.Presentation = newPresentation;
            _currentCredit.PartType = (cboPartType.SelectedValue as string) is { Length: > 0 } code ? code : null;
            _currentCredit.Notes = string.IsNullOrWhiteSpace(txtCreditNotes.Text) ? null : txtCreditNotes.Text.Trim();
            _currentCredit.UpdatedBy = Environment.UserName;

            await _creditsRepo.UpdateAsync(_currentCredit);
            await UpdateStatusBarAsync();

            // ListBox の表示も updated 後の値に追随させる（presentation を変えたら反映される）。
            // ReloadCreditsAsync が DataSource を入れ替えるため lstCredits.SelectedIndexChanged が
            // 発火するが、_isReloadingCredits / _isLoadingCredit ガードで多重実行は抑止される。
            int keepId = _currentCredit.CreditId;
            await ReloadCreditsAsync();
            SelectCreditInListBox(keepId);
            MessageBox.Show(this, "クレジットプロパティを保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// クレジット削除（論理削除）：<see cref="CreditsRepository.SoftDeleteAsync"/> で
    /// is_deleted=1 を立てる。配下のカード／役職／ブロック／エントリは物理削除しない
    /// （データが見えなくなるだけで残す）。
    /// <para>
    /// v1.2.0 工程 H-8 ターン 6.5 で復活：未保存の Draft 変更がある場合は先に保存／取消するよう促す。
    /// </para>
    /// </summary>
    private async Task OnDeleteCreditAsync()
    {
        try
        {
            if (_currentCredit is null) return;

            // 未保存の Draft 変更がある場合は警告（削除対象が変わるとさらに混乱するため）。
            if (_draftSession is not null && _draftSession.HasUnsavedChanges)
            {
                MessageBox.Show(this,
                    "Draft 側に未保存の変更があります。\n"
                    + "先に「💾 保存」または「✖ 取消」で Draft を片付けてから、クレジット削除を実行してください。",
                    "操作不可", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var msg = $"クレジット #{_currentCredit.CreditId} {_currentCredit.CreditKind} を論理削除します。\n" +
                      "（is_deleted=1 を立てるだけで、配下のカード／役職／ブロック／エントリは物理削除されません）";
            if (MessageBox.Show(this, msg, "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            await _creditsRepo.SoftDeleteAsync(_currentCredit.CreditId, Environment.UserName);
            _currentCredit = null;
            _draftSession = null;
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
            if (_currentCredit is null || _draftSession is null) return;

            // ROLL クレジットでは card 1 枚固定。Draft 上の非削除カード数で判定。
            int existingCount = _draftSession.Root.Cards.Count(c => c.State != DraftState.Deleted);
            if (_currentCredit.Presentation == "ROLL" && existingCount > 0)
            {
                MessageBox.Show(this,
                    "presentation=ROLL のクレジットでは、カードは 1 枚（card_seq=1）固定です。\n" +
                    "複数カードが必要な場合は presentation を CARDS に変更してください。",
                    "操作不可", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            byte newSeq = (byte)(existingCount + 1);

            // ─── Draft 上で新規 Card を組み立てる ───
            // 旧仕様では「Card 新規作成時に Tier 1 + Group 1 を自動投入」を Repository 側で
            // 1 トランザクションでやっていた。Draft ベースでは保存時に階層的に INSERT されるので、
            // メモリ上で Card / Tier / Group の 3 階層を Added 状態で先んじて積み上げる。
            var draftCard = new DraftCard
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = _draftSession.Root,
                Entity = new CreditCard
                {
                    CreditId = _draftSession.Root.RealId ?? 0, // 保存時に親 RealId で上書きされるが、参考値で入れておく
                    CardSeq = newSeq,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            // 配下に Tier 1 を自動投入
            var draftTier = new DraftTier
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftCard,
                Entity = new CreditCardTier
                {
                    TierNo = 1,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            draftCard.Tiers.Add(draftTier);
            // Tier 配下に Group 1 を自動投入
            var draftGroup = new DraftGroup
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftTier,
                Entity = new CreditCardGroup
                {
                    GroupNo = 1,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            draftTier.Groups.Add(draftGroup);
            _draftSession.Root.Cards.Add(draftCard);

            await RebuildTreeFromDraftAsync();
            SelectNodeById(NodeKind.Card, draftCard.CurrentId);
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
            if (_currentCredit is null || _draftSession is null) return;

            // 選択ノードから親カード Draft を解決
            var draftCard = ResolveAncestorDraftCard();
            if (draftCard is null)
            {
                MessageBox.Show(this, "Card / Tier / Group / Role / Block / Entry のいずれかを選択してから「+ Tier」を押してください。");
                return;
            }

            // 既存 Tier のうち削除されていないものから tier_no の最大を取り、+ 1（上限 2）
            var liveTiers = draftCard.Tiers.Where(t => t.State != DraftState.Deleted).ToList();
            byte nextTierNo = (byte)(liveTiers.Select(t => (int)t.Entity.TierNo).DefaultIfEmpty(0).Max() + 1);
            if (nextTierNo > 2)
            {
                MessageBox.Show(this, "このカードには既に Tier 1 / Tier 2 が揃っています。これ以上 Tier は追加できません。");
                return;
            }

            // ─── Draft 上で新 Tier + Group 1 を組み立てる ───
            var draftTier = new DraftTier
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftCard,
                Entity = new CreditCardTier
                {
                    TierNo = nextTierNo,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            // 旧 Repository では Tier 作成時に Group 1 を自動投入する仕様だった。Draft ベースでも同様。
            var draftGroup = new DraftGroup
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftTier,
                Entity = new CreditCardGroup
                {
                    GroupNo = 1,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            draftTier.Groups.Add(draftGroup);
            draftCard.Tiers.Add(draftTier);

            await RebuildTreeFromDraftAsync();
            SelectNodeById(NodeKind.Tier, draftTier.CurrentId);
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
            if (_currentCredit is null || _draftSession is null) return;

            // 選択ノードから所属 DraftTier を解決
            var draftTier = ResolveAncestorDraftTier();
            if (draftTier is null)
            {
                MessageBox.Show(this, "Card / Tier / Group / Role のいずれかを選択してから「+ Group」を押してください。");
                return;
            }

            // 既存 Group のうち削除されていないものから最大 group_no を取って + 1
            var liveGroups = draftTier.Groups.Where(g => g.State != DraftState.Deleted).ToList();
            byte nextGroupNo = (byte)(liveGroups.Select(g => (int)g.Entity.GroupNo).DefaultIfEmpty(0).Max() + 1);

            var draftGroup = new DraftGroup
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftTier,
                Entity = new CreditCardGroup
                {
                    GroupNo = nextGroupNo,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            draftTier.Groups.Add(draftGroup);

            await RebuildTreeFromDraftAsync();
            SelectNodeById(NodeKind.Group, draftGroup.CurrentId);
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
    /// 選択ノードから所属する <see cref="DraftCard"/> を解決する（v1.2.0 工程 H-8 ターン 4 で導入）。
    /// 祖先側を辿って Card ノードを見つけ、その Tag.Payload に積まれている DraftCard 本体を返す。
    /// </summary>
    private DraftCard? ResolveAncestorDraftCard()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag tag && tag.Kind == NodeKind.Card && tag.Payload is DraftCard dc)
                return dc;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// 選択ノードから所属する <see cref="DraftTier"/> を解決する（v1.2.0 工程 H-8 ターン 4 で導入）。
    /// 直接 Tier 選択中ならそれを、そうでなければ祖先側を辿って見つける。
    /// Card 選択時のフォールバック：そのカードの Tier 1 を返す（無ければ最初の Tier）。
    /// </summary>
    private DraftTier? ResolveAncestorDraftTier()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag tag)
            {
                if (tag.Kind == NodeKind.Tier && tag.Payload is DraftTier dt) return dt;
                if (tag.Kind == NodeKind.Card && tag.Payload is DraftCard dc)
                {
                    // Card ノード選択時は Tier 1 を優先、無ければ最初の有効 Tier
                    return dc.Tiers.Where(t => t.State != DraftState.Deleted)
                                   .OrderBy(t => t.Entity.TierNo)
                                   .FirstOrDefault();
                }
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>
    /// 選択ノードから所属する <see cref="DraftGroup"/> を解決する（v1.2.0 工程 H-8 ターン 4 で導入）。
    /// 直接 Group 選択中ならそれを、Role 選択中なら親 Group を、Tier / Card なら配下の最初の Group を返す。
    /// </summary>
    private DraftGroup? ResolveAncestorDraftGroup()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag tag)
            {
                if (tag.Kind == NodeKind.Group && tag.Payload is DraftGroup dg) return dg;
                if (tag.Kind == NodeKind.CardRole && tag.Payload is DraftRole dr) return dr.Parent;
                if (tag.Kind == NodeKind.Tier && tag.Payload is DraftTier dt)
                {
                    return dt.Groups.Where(g => g.State != DraftState.Deleted)
                                    .OrderBy(g => g.Entity.GroupNo)
                                    .FirstOrDefault();
                }
                if (tag.Kind == NodeKind.Card && tag.Payload is DraftCard dc)
                {
                    var firstTier = dc.Tiers.Where(t => t.State != DraftState.Deleted)
                                            .OrderBy(t => t.Entity.TierNo).FirstOrDefault();
                    return firstTier?.Groups.Where(g => g.State != DraftState.Deleted)
                                            .OrderBy(g => g.Entity.GroupNo).FirstOrDefault();
                }
            }
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
            if (_currentCredit is null || _draftSession is null) return;

            // 選択ノードから DraftGroup を解決
            var draftGroup = ResolveAncestorDraftGroup();
            if (draftGroup is null)
            {
                MessageBox.Show(this, "Card / Tier / Group / Role のいずれかのノードを選択してから「+ 役職」を押してください。");
                return;
            }

            // 役職コードをピッカーで選んでもらう
            using var dlg = new Pickers.RolePickerDialog(_rolesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedRole is null) return;

            // 同 Group 内の非削除役職数 + 1 を新 order_in_group とする
            var liveRoles = draftGroup.Roles.Where(r => r.State != DraftState.Deleted).ToList();
            byte newOrder = (byte)(liveRoles.Count + 1);

            // ─── Draft 上で新 Role + Block 1 を組み立てる ───
            var draftRole = new DraftRole
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftGroup,
                Entity = new CreditCardRole
                {
                    RoleCode = dlg.SelectedRole.RoleCode,
                    OrderInGroup = newOrder,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            // 旧 Repository では Role 作成時に Block 1 を自動投入する仕様だった。
            var draftBlock = new DraftBlock
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftRole,
                Entity = new CreditRoleBlock
                {
                    BlockSeq = 1,
                    ColCount = 1,
                    LeadingCompanyAliasId = null,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            draftRole.Blocks.Add(draftBlock);
            draftGroup.Roles.Add(draftRole);

            await RebuildTreeFromDraftAsync();
            SelectNodeById(NodeKind.CardRole, draftRole.CurrentId);
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
    /// col_count は既定 1（縦並び）。新 block_seq = 同 card_role 内の最大 + 1。
    /// </summary>
    private Task OnAddBlockAsync()
    {
        try
        {
            if (_currentCredit is null || _draftSession is null) return Task.CompletedTask;
            var draftRole = ResolveTargetDraftRoleFromSelection();
            if (draftRole is null)
            {
                MessageBox.Show(this, "Role または Block ノードを選択してから「+ ブロック」を押してください。");
                return Task.CompletedTask;
            }
            // 同 Role 配下の非削除 Block 数 + 1 を新 block_seq とする
            byte newSeq = (byte)(draftRole.Blocks.Count(b => b.State != DraftState.Deleted) + 1);

            var draftBlock = new DraftBlock
            {
                RealId = null,
                TempId = _draftSession.AllocateTempId(),
                State = DraftState.Added,
                Parent = draftRole,
                Entity = new CreditRoleBlock
                {
                    BlockSeq = newSeq,
                    ColCount = 1,
                    LeadingCompanyAliasId = null,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                }
            };
            draftRole.Blocks.Add(draftBlock);

            return RebuildAndSelectAsync(NodeKind.Block, draftBlock.CurrentId);
        }
        catch (Exception ex) { ShowError(ex); return Task.CompletedTask; }
    }

    /// <summary>
    /// 選択ノードから「ブロック追加先となる DraftRole」を解決する（v1.2.0 工程 H-8 ターン 5 で導入）。
    /// CardRole 選択時 → そのノード自身、Block 選択時 → 親 Role、Entry 選択時 → 親 Role（祖父）。
    /// </summary>
    private DraftRole? ResolveTargetDraftRoleFromSelection()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag t)
            {
                if (t.Kind == NodeKind.CardRole && t.Payload is DraftRole dr) return dr;
                if (t.Kind == NodeKind.Block && t.Payload is DraftBlock db) return db.Parent;
                if (t.Kind == NodeKind.Entry && t.Payload is DraftEntry de) return de.Parent.Parent;
            }
            node = node.Parent;
        }
        return null;
    }

    /// <summary>RebuildTreeFromDraftAsync を呼んで指定ノードを選択し直すヘルパ。</summary>
    private async Task RebuildAndSelectAsync(NodeKind kind, int id)
    {
        await RebuildTreeFromDraftAsync();
        SelectNodeById(kind, id);
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
            if (_draftSession is null) return;

            // v1.2.0 工程 H-8 ターン 5 で Block / Entry にも対応。Card / Tier / Group / CardRole / Block / Entry が削除可能。
            if (tag.Kind is not (NodeKind.Card or NodeKind.Tier or NodeKind.Group
                or NodeKind.CardRole or NodeKind.Block or NodeKind.Entry))
            {
                return; // ThemeSongVirtual など読み取り専用ノードは削除不可
            }

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
                $"{nodeName} を削除します（保存ボタン押下時に確定）。{warn}",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            // ─── Draft 上で削除マーク ───
            // 既存行（RealId 値あり）の場合：State = Deleted にマークし、親リストから取り外して DeletedXxx バケットへ退避。
            //   → 保存時に DELETE 文が発行される。CASCADE があるので親 1 件 DELETE で配下も自動的に消えるため、
            //     配下までバケットに個別退避する必要は無い。
            // メモリ上の新規行（State = Added）の場合：親リストから単に取り除くだけ（DB に行が無いので退避不要）。
            switch (tag.Kind)
            {
                case NodeKind.Card when tag.Payload is DraftCard dc:
                    if (dc.State == DraftState.Added) _draftSession.Root.Cards.Remove(dc);
                    else { dc.MarkDeleted(); _draftSession.Root.Cards.Remove(dc); _draftSession.DeletedCards.Add(dc); }
                    break;
                case NodeKind.Tier when tag.Payload is DraftTier dt:
                    if (dt.State == DraftState.Added) dt.Parent.Tiers.Remove(dt);
                    else { dt.MarkDeleted(); dt.Parent.Tiers.Remove(dt); _draftSession.DeletedTiers.Add(dt); }
                    break;
                case NodeKind.Group when tag.Payload is DraftGroup dg:
                    if (dg.State == DraftState.Added) dg.Parent.Groups.Remove(dg);
                    else { dg.MarkDeleted(); dg.Parent.Groups.Remove(dg); _draftSession.DeletedGroups.Add(dg); }
                    break;
                case NodeKind.CardRole when tag.Payload is DraftRole dr:
                    if (dr.State == DraftState.Added) dr.Parent.Roles.Remove(dr);
                    else { dr.MarkDeleted(); dr.Parent.Roles.Remove(dr); _draftSession.DeletedRoles.Add(dr); }
                    break;
                case NodeKind.Block when tag.Payload is DraftBlock db:
                    if (db.State == DraftState.Added) db.Parent.Blocks.Remove(db);
                    else { db.MarkDeleted(); db.Parent.Blocks.Remove(db); _draftSession.DeletedBlocks.Add(db); }
                    break;
                case NodeKind.Entry when tag.Payload is DraftEntry de:
                    if (de.State == DraftState.Added) de.Parent.Entries.Remove(de);
                    else { de.MarkDeleted(); de.Parent.Entries.Remove(de); _draftSession.DeletedEntries.Add(de); }
                    break;
            }

            entryEditor.ClearAndDisable();
            blockEditor.ClearAndDisable();
            await RebuildTreeFromDraftAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 削除や移動の直後に呼び出し、同階層に残った行の seq / order_in_group を
    /// 1, 2, 3, ... の連番に詰める（v1.2.0 工程 H 補修で追加）。
    /// 各リポジトリの <c>BulkUpdateSeqAsync</c> はトランザクション内で「対象行を退避値 200 系に
    /// 逃がす → 本来の値で再採番」の 2 段階更新を実行するので、UNIQUE 制約との一時衝突は回避される。
    /// 飛び番号や歯抜けが残ると、ユーザーに見える「Card #3」「Card #5」のような表記が
    /// 表示連番（1,2,3,...）と DB 上の実値との乖離を生むため、削除直後に詰めておく。
    /// </summary>
    private async Task ResequenceSiblingsAsync(NodeKind kind, int parentId)
    {
        switch (kind)
        {
            case NodeKind.Card:
                {
                    var siblings = (await _cardsRepo.GetByCreditAsync(parentId))
                        .OrderBy(c => c.CardSeq).ToList();
                    if (siblings.Count == 0) return;
                    // CreditCardsRepository.BulkUpdateSeqAsync は引数として
                    // (cardId, cardSeq) のタプル列を受け取る仕様。
                    var updates = new List<(int cardId, byte cardSeq)>();
                    byte seq = 1;
                    foreach (var c in siblings) updates.Add((c.CardId, seq++));
                    await _cardsRepo.BulkUpdateSeqAsync(parentId, updates);
                    break;
                }
            case NodeKind.CardRole:
                {
                    // CardRole の seq は order_in_group。同 card_group_id 内で詰める。
                    // CreditCardRolesRepository.BulkUpdateSeqAsync は引数として
                    // (cardRoleId, cardGroupId, orderInGroup) のタプル列を受け取る仕様
                    // （card_group_id を引数に含むことで「複数グループにまたがる移動」も
                    // 同じトランザクションで処理できる設計）。詰め直しの場合は
                    // すべて同じ parentId（cardGroupId）を渡す。
                    var siblings = (await _cardRolesRepo.GetByGroupAsync(parentId))
                        .OrderBy(r => r.OrderInGroup).ToList();
                    if (siblings.Count == 0) return;
                    var updates = new List<(int cardRoleId, int cardGroupId, byte orderInGroup)>();
                    byte seq = 1;
                    foreach (var r in siblings) updates.Add((r.CardRoleId, parentId, seq++));
                    await _cardRolesRepo.BulkUpdateSeqAsync(updates);
                    break;
                }
            case NodeKind.Block:
                {
                    var siblings = (await _blocksRepo.GetByCardRoleAsync(parentId))
                        .OrderBy(b => b.BlockSeq).ToList();
                    if (siblings.Count == 0) return;
                    // CreditRoleBlocksRepository.BulkUpdateSeqAsync は引数として
                    // (blockId, blockSeq) のタプル列を受け取る仕様。
                    var updates = new List<(int blockId, byte blockSeq)>();
                    byte seq = 1;
                    foreach (var b in siblings) updates.Add((b.BlockId, seq++));
                    await _blocksRepo.BulkUpdateSeqAsync(parentId, updates);
                    break;
                }
            case NodeKind.Entry:
                {
                    // エントリは (block_id, is_broadcast_only) 単位で seq を持つので、
                    // 既定行（false）と本放送限定行（true）の両方をそれぞれ詰める。
                    // CreditBlockEntriesRepository.BulkUpdateSeqAsync は引数として
                    // (entryId, entrySeq) のタプル列 + blockId + isBroadcastOnly を受け取る仕様。
                    var allSiblings = (await _entriesRepo.GetByBlockAsync(parentId)).ToList();
                    foreach (var flag in new[] { false, true })
                    {
                        var groupSiblings = allSiblings
                            .Where(e => e.IsBroadcastOnly == flag)
                            .OrderBy(e => e.EntrySeq).ToList();
                        if (groupSiblings.Count == 0) continue;
                        var updates = new List<(int entryId, ushort entrySeq)>();
                        ushort seq = 1;
                        foreach (var e in groupSiblings) updates.Add((e.EntryId, seq++));
                        await _entriesRepo.BulkUpdateSeqAsync(parentId, flag, updates);
                    }
                    break;
                }
        }
    }

    // ────────────────────────────────────────────────────────────
    // エントリ追加（v1.2.0 工程 B-3 追加）
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// 「+ エントリ」ボタン処理：選択中ノードから追加先 DraftBlock を解決し、
    /// 右ペインの EntryEditorPanel を新規追加モードに切り替える
    /// （v1.2.0 工程 H-8 ターン 5 で Draft 経由に書き換え）。
    /// 実 INSERT は中央ペイン下の「💾 保存」ボタンで一括実行される。
    /// </summary>
    private Task OnAddEntryAsync()
    {
        try
        {
            if (_currentCredit is null || _draftSession is null) return Task.CompletedTask;
            var draftBlock = ResolveTargetDraftBlockFromSelection();
            if (draftBlock is null)
            {
                MessageBox.Show(this, "Block または Entry ノードを選択してから「+ エントリ」を押してください。");
                return Task.CompletedTask;
            }
            // 既定 is_broadcast_only=false 行のみ採番対象（本放送限定行は別グループ扱い）。
            // Draft 上の同 block_id × is_broadcast_only=false の最大 entry_seq + 1 を新 seq とする。
            ushort newSeq = (ushort)(draftBlock.Entries
                .Where(e => e.State != DraftState.Deleted && !e.Entity.IsBroadcastOnly)
                .Select(e => (int)e.Entity.EntrySeq)
                .DefaultIfEmpty(0)
                .Max() + 1);

            // 親フォーム側で常に最新の Draft セッション参照を流し込む（クレジット切替や保存後の再ロードで更新される）
            entryEditor.SetSession(_draftSession);

            // v1.2.0 工程 H-8 ターン 5 修正：右ペインの可視性切替が抜けていたバグ修正。
            // Block 選択中は blockEditor が前面に出ているため、エントリ追加モードに切り替えるには
            // BlockEditor を非表示・無効化、EntryEditor を表示する必要がある。
            blockEditor.ClearAndDisable();
            blockEditor.Visible = false;
            entryEditor.Visible = true;

            entryEditor.LoadForNew(draftBlock, isBroadcastOnly: false, newSeq);
            return Task.CompletedTask;
        }
        catch (Exception ex) { ShowError(ex); return Task.CompletedTask; }
    }

    /// <summary>
    /// 選択ノードから「エントリ追加先となる DraftBlock」を解決する（v1.2.0 工程 H-8 ターン 5 で導入）。
    /// Block 選択時 → そのノード自身、Entry 選択時 → 親 Block。
    /// 該当しない選択状態（Card/Tier/Group/Role など）の場合は null。
    /// </summary>
    private DraftBlock? ResolveTargetDraftBlockFromSelection()
    {
        var node = treeStructure.SelectedNode;
        while (node is not null)
        {
            if (node.Tag is NodeTag t)
            {
                if (t.Kind == NodeKind.Block && t.Payload is DraftBlock db) return db;
                if (t.Kind == NodeKind.Entry && t.Payload is DraftEntry de) return de.Parent;
            }
            node = node.Parent;
        }
        return null;
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
            if (_draftSession is null) return;
            int keepId = tag.Id;

            switch (tag.Kind)
            {
                case NodeKind.Card when tag.Payload is DraftCard dc:
                {
                    // ─── Draft 上で同 credit 内の Cards を並べ替え ───
                    // 削除済みを除いた現在の表示順リストを作って、対象を 1 つ前 / 後ろに動かす。
                    // CardSeq の値も同時に 1, 2, 3, ... に詰め直す。並べ替えは保存時に DB へ反映される
                    // ので、Modified マークは「順序が変わった全 Card」につける。
                    var list = _draftSession.Root.Cards.Where(c => c.State != DraftState.Deleted)
                        .OrderBy(c => c.Entity.CardSeq).ToList();
                    int idx = list.FindIndex(c => c == dc);
                    bool moved = up ? SeqReorderHelper.MoveUp(list, idx) : SeqReorderHelper.MoveDown(list, idx);
                    if (!moved) return;
                    // 入れ替え後の順序で CardSeq を 1, 2, 3, ... に振り直す。
                    // 値が変わったものは MarkModified（既に Added の場合は MarkModified では何もしない）。
                    for (int i = 0; i < list.Count; i++)
                    {
                        byte newSeq = (byte)(i + 1);
                        if (list[i].Entity.CardSeq != newSeq)
                        {
                            list[i].Entity.CardSeq = newSeq;
                            list[i].MarkModified();
                        }
                    }
                    break;
                }
                case NodeKind.CardRole when tag.Payload is DraftRole dr:
                {
                    // 同 Group 内の役職を並べ替え（OrderInGroup を 1, 2, 3, ... に詰め直す）
                    var list = dr.Parent.Roles.Where(r => r.State != DraftState.Deleted)
                        .OrderBy(r => r.Entity.OrderInGroup).ToList();
                    int idx = list.FindIndex(r => r == dr);
                    bool moved = up ? SeqReorderHelper.MoveUp(list, idx) : SeqReorderHelper.MoveDown(list, idx);
                    if (!moved) return;
                    for (int i = 0; i < list.Count; i++)
                    {
                        byte newOrder = (byte)(i + 1);
                        if (list[i].Entity.OrderInGroup != newOrder)
                        {
                            list[i].Entity.OrderInGroup = newOrder;
                            list[i].MarkModified();
                        }
                    }
                    break;
                }
                case NodeKind.Block when tag.Payload is DraftBlock db:
                {
                    // 同 Role 内の Block を並べ替え（BlockSeq を 1, 2, 3, ... に詰め直す）。
                    var list = db.Parent.Blocks.Where(b => b.State != DraftState.Deleted)
                        .OrderBy(b => b.Entity.BlockSeq).ToList();
                    int idx = list.FindIndex(b => b == db);
                    bool moved = up ? SeqReorderHelper.MoveUp(list, idx) : SeqReorderHelper.MoveDown(list, idx);
                    if (!moved) return;
                    for (int i = 0; i < list.Count; i++)
                    {
                        byte newSeq = (byte)(i + 1);
                        if (list[i].Entity.BlockSeq != newSeq)
                        {
                            list[i].Entity.BlockSeq = newSeq;
                            list[i].MarkModified();
                        }
                    }
                    break;
                }
                case NodeKind.Entry when tag.Payload is DraftEntry de:
                {
                    // 同 Block 内、同 IsBroadcastOnly グループ内の Entry を並べ替え（EntrySeq を 1, 2, ... に）。
                    bool flag = de.Entity.IsBroadcastOnly;
                    var list = de.Parent.Entries
                        .Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag)
                        .OrderBy(e => e.Entity.EntrySeq).ToList();
                    int idx = list.FindIndex(e => e == de);
                    bool moved = up ? SeqReorderHelper.MoveUp(list, idx) : SeqReorderHelper.MoveDown(list, idx);
                    if (!moved) return;
                    for (int i = 0; i < list.Count; i++)
                    {
                        ushort newSeq = (ushort)(i + 1);
                        if (list[i].Entity.EntrySeq != newSeq)
                        {
                            list[i].Entity.EntrySeq = newSeq;
                            list[i].MarkModified();
                        }
                    }
                    break;
                }
                default:
                    return;
            }
            await RebuildTreeFromDraftAsync();
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
        // v1.2.0 工程 H-8 ターン 5 で Draft 経由の DnD を復活。
        // CardRole の自由乗り換え（別 Card / Tier / Group へ移動）と
        // Entry の自由乗り換え（別 Block へ移動）の 2 系統をサポートする。
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode src) { e.Effect = DragDropEffects.None; return; }
        var pt = treeStructure.PointToClient(new Point(e.X, e.Y));
        var target = treeStructure.GetNodeAt(pt);
        if (target is null || target == src) { e.Effect = DragDropEffects.None; return; }
        if (src.Tag is not NodeTag st || target.Tag is not NodeTag tt) { e.Effect = DragDropEffects.None; return; }

        // ─── CardRole の自由乗り換え DnD ───
        // 別 Card / Tier / Group / CardRole にドロップ可能。
        if (st.Kind == NodeKind.CardRole)
        {
            if (tt.Kind is NodeKind.CardRole or NodeKind.Group or NodeKind.Tier or NodeKind.Card)
            {
                e.Effect = DragDropEffects.Move;
                treeStructure.SelectedNode = target;
            }
            else { e.Effect = DragDropEffects.None; }
            return;
        }

        // ─── Entry の自由乗り換え DnD ───
        // 別 Block / 別 Entry にドロップ可能。is_broadcast_only 値は移動元の値を保持。
        if (st.Kind == NodeKind.Entry)
        {
            if (tt.Kind is NodeKind.Entry or NodeKind.Block)
            {
                e.Effect = DragDropEffects.Move;
                treeStructure.SelectedNode = target;
            }
            else { e.Effect = DragDropEffects.None; }
            return;
        }

        // ─── Card / Block の同階層内並べ替え ───
        if (target.Parent != src.Parent) { e.Effect = DragDropEffects.None; return; }
        if (st.Kind != tt.Kind) { e.Effect = DragDropEffects.None; return; }
        e.Effect = DragDropEffects.Move;
        treeStructure.SelectedNode = target;
    }

    /// <summary>
    /// DragDrop：ドラッグ元を「ドロップ位置」へ移動して、同階層の全要素を seq=1,2,...
    /// で再採番する。ドロップ位置はノード矩形の上半分なら直前、下半分なら直後と判定。
    /// </summary>
    private async Task OnTreeDragDropAsync(object? sender, DragEventArgs e)
    {
        // v1.2.0 工程 H-8 ターン 5：DnD を Draft 経由に書き換えて復活。
        // CardRole の自由乗り換え（別 Card / Tier / Group へ移動）と、
        // Entry の自由乗り換え（別 Block へ移動）の 2 系統 + 既存の同階層並べ替えをサポートする。
        // すべての操作はメモリ上の Draft オブジェクトに対して行い、保存ボタンで一括 DB 反映する。
        try
        {
            if (_draftSession is null) return;
            if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode src) return;
            var pt = treeStructure.PointToClient(new Point(e.X, e.Y));
            var target = treeStructure.GetNodeAt(pt);
            if (target is null || target == src) return;
            if (src.Tag is not NodeTag st || target.Tag is not NodeTag tt) return;

            bool dropAbove = (pt.Y < target.Bounds.Y + target.Bounds.Height / 2);
            int keepId = st.Id;
            NodeKind keepKind = st.Kind;

            // ─── CardRole の自由乗り換え DnD ───
            if (st.Kind == NodeKind.CardRole && st.Payload is DraftRole srcRole)
            {
                DropDraftRole(srcRole, tt, dropAbove);
                await RebuildTreeFromDraftAsync();
                SelectNodeById(keepKind, srcRole.CurrentId);
                return;
            }

            // ─── Entry の自由乗り換え DnD ───
            if (st.Kind == NodeKind.Entry && st.Payload is DraftEntry srcEntry)
            {
                DropDraftEntry(srcEntry, tt, dropAbove);
                await RebuildTreeFromDraftAsync();
                SelectNodeById(keepKind, srcEntry.CurrentId);
                return;
            }

            // ─── Card / Block の同階層内並べ替え ───
            if (target.Parent != src.Parent) return;
            if (st.Kind != tt.Kind) return;

            switch (st.Kind)
            {
                case NodeKind.Card when st.Payload is DraftCard sdc && tt.Payload is DraftCard tdc:
                {
                    var list = _draftSession.Root.Cards.Where(c => c.State != DraftState.Deleted)
                        .OrderBy(c => c.Entity.CardSeq).ToList();
                    list.Remove(sdc);
                    int targetIdx = list.FindIndex(c => c == tdc);
                    if (targetIdx < 0) return;
                    list.Insert(dropAbove ? targetIdx : targetIdx + 1, sdc);
                    for (int i = 0; i < list.Count; i++)
                    {
                        byte ns = (byte)(i + 1);
                        if (list[i].Entity.CardSeq != ns) { list[i].Entity.CardSeq = ns; list[i].MarkModified(); }
                    }
                    break;
                }
                case NodeKind.Block when st.Payload is DraftBlock sdb && tt.Payload is DraftBlock tdb:
                {
                    if (sdb.Parent != tdb.Parent) return; // 同 Role 内のみ（別 Role への移動は未対応）
                    var list = sdb.Parent.Blocks.Where(b => b.State != DraftState.Deleted)
                        .OrderBy(b => b.Entity.BlockSeq).ToList();
                    list.Remove(sdb);
                    int targetIdx = list.FindIndex(b => b == tdb);
                    if (targetIdx < 0) return;
                    list.Insert(dropAbove ? targetIdx : targetIdx + 1, sdb);
                    for (int i = 0; i < list.Count; i++)
                    {
                        byte ns = (byte)(i + 1);
                        if (list[i].Entity.BlockSeq != ns) { list[i].Entity.BlockSeq = ns; list[i].MarkModified(); }
                    }
                    break;
                }
                default:
                    return;
            }
            await RebuildTreeFromDraftAsync();
            SelectNodeById(keepKind, keepId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// Draft 上で CardRole を別 Card / Tier / Group へ移動する（v1.2.0 工程 H-8 ターン 5 で導入）。
    /// ドロップ先 NodeTag の種別に応じて移動先 DraftGroup と挿入位置を解決する。
    /// </summary>
    private void DropDraftRole(DraftRole srcRole, NodeTag tt, bool dropAbove)
    {
        // 1. 移動先 DraftGroup と insertAt（移動対象を除外したリスト基準のインデックス）を決定
        DraftGroup? dstGroup = null;
        int insertAt = 0;
        if (tt.Kind == NodeKind.CardRole && tt.Payload is DraftRole tdr)
        {
            dstGroup = tdr.Parent;
            var dstList = dstGroup.Roles.Where(r => r.State != DraftState.Deleted && r != srcRole)
                .OrderBy(r => r.Entity.OrderInGroup).ToList();
            int idx = dstList.FindIndex(r => r == tdr);
            insertAt = (idx < 0) ? dstList.Count : (dropAbove ? idx : idx + 1);
        }
        else if (tt.Kind == NodeKind.Group && tt.Payload is DraftGroup tdg)
        {
            dstGroup = tdg;
            insertAt = dstGroup.Roles.Count(r => r.State != DraftState.Deleted && r != srcRole); // 末尾
        }
        else if (tt.Kind == NodeKind.Tier && tt.Payload is DraftTier tdt)
        {
            // Tier の末尾 Group の末尾
            dstGroup = tdt.Groups.Where(g => g.State != DraftState.Deleted)
                .OrderBy(g => g.Entity.GroupNo).LastOrDefault();
            if (dstGroup is null) return;
            insertAt = dstGroup.Roles.Count(r => r.State != DraftState.Deleted && r != srcRole);
        }
        else if (tt.Kind == NodeKind.Card && tt.Payload is DraftCard tdc)
        {
            // Card の Tier 1 の Group 1 の末尾
            var firstTier = tdc.Tiers.Where(t => t.State != DraftState.Deleted)
                .OrderBy(t => t.Entity.TierNo).FirstOrDefault();
            dstGroup = firstTier?.Groups.Where(g => g.State != DraftState.Deleted)
                .OrderBy(g => g.Entity.GroupNo).FirstOrDefault();
            if (dstGroup is null) return;
            insertAt = dstGroup.Roles.Count(r => r.State != DraftState.Deleted && r != srcRole);
        }
        else { return; }

        // 2. 旧 Group から取り外し
        var oldGroup = srcRole.Parent;
        oldGroup.Roles.Remove(srcRole);

        // 3. 新 Group の指定位置に挿入
        // srcRole.Parent を新 Group に付け替えたいが、Parent は init only。
        // → DraftRole の Parent は init で設定された後変更不可なので、新しい DraftRole を作って Entity と State を移植する手もあるが、
        //   ここでは Parent を変更可能にする方が単純。Parent setter を持たないので、暫定対応として新 DraftRole を作成して旧データをコピーする。
        var movedRole = new DraftRole
        {
            RealId = srcRole.RealId,
            TempId = srcRole.TempId,
            State = (srcRole.State == DraftState.Unchanged) ? DraftState.Modified : srcRole.State,
            Parent = dstGroup,
            Entity = srcRole.Entity
        };
        // 配下の Blocks も新インスタンスの Blocks リストに移植
        foreach (var blk in srcRole.Blocks) movedRole.Blocks.Add(blk);
        // Entity.CardGroupId を新 Group の RealId（または既存値）に更新（保存時に親 RealId で再上書きされるが）
        if (dstGroup.RealId.HasValue) movedRole.Entity.CardGroupId = dstGroup.RealId.Value;

        // 挿入
        var newDstList = dstGroup.Roles.Where(r => r.State != DraftState.Deleted)
            .OrderBy(r => r.Entity.OrderInGroup).ToList();
        if (insertAt < 0) insertAt = 0;
        if (insertAt > newDstList.Count) insertAt = newDstList.Count;
        newDstList.Insert(insertAt, movedRole);

        // 旧 Group の OrderInGroup を 1, 2, ... に詰める
        var oldList = oldGroup.Roles.Where(r => r.State != DraftState.Deleted)
            .OrderBy(r => r.Entity.OrderInGroup).ToList();
        for (int i = 0; i < oldList.Count; i++)
        {
            byte ns = (byte)(i + 1);
            if (oldList[i].Entity.OrderInGroup != ns) { oldList[i].Entity.OrderInGroup = ns; oldList[i].MarkModified(); }
        }

        // 新 Group の Roles リストを差し替え（Deleted 済み Role はそのまま、有効分だけ詰めて再採番）
        var deletedKept = dstGroup.Roles.Where(r => r.State == DraftState.Deleted).ToList();
        dstGroup.Roles.Clear();
        for (int i = 0; i < newDstList.Count; i++)
        {
            byte ns = (byte)(i + 1);
            if (newDstList[i].Entity.OrderInGroup != ns) { newDstList[i].Entity.OrderInGroup = ns; newDstList[i].MarkModified(); }
            dstGroup.Roles.Add(newDstList[i]);
        }
        foreach (var d in deletedKept) dstGroup.Roles.Add(d);

        // 元の srcRole は捨てる（Blocks は movedRole に移植済み）。
        // ただし srcRole が Deleted バケットには行かない（ロケーション変更は削除ではない）。
    }

    /// <summary>
    /// Draft 上で Entry を別 Block / 別 Entry の位置へ移動する（v1.2.0 工程 H-8 ターン 5 で導入）。
    /// is_broadcast_only 値は移動元の値を保持。フラグ違いの Entry にドロップした場合は移動先グループの末尾に正規化。
    /// </summary>
    private void DropDraftEntry(DraftEntry srcEntry, NodeTag tt, bool dropAbove)
    {
        bool flag = srcEntry.Entity.IsBroadcastOnly;
        DraftBlock? dstBlock = null;
        int insertAt = 0;
        if (tt.Kind == NodeKind.Entry && tt.Payload is DraftEntry tde)
        {
            dstBlock = tde.Parent;
            var dstSameGroup = dstBlock.Entries.Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag && e != srcEntry)
                .OrderBy(e => e.Entity.EntrySeq).ToList();
            if (tde.Entity.IsBroadcastOnly == flag)
            {
                int idx = dstSameGroup.FindIndex(e => e == tde);
                insertAt = (idx < 0) ? dstSameGroup.Count : (dropAbove ? idx : idx + 1);
            }
            else
            {
                insertAt = dstSameGroup.Count;
            }
        }
        else if (tt.Kind == NodeKind.Block && tt.Payload is DraftBlock tdb)
        {
            dstBlock = tdb;
            insertAt = dstBlock.Entries.Count(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag && e != srcEntry);
        }
        else { return; }

        var oldBlock = srcEntry.Parent;
        oldBlock.Entries.Remove(srcEntry);

        // DraftEntry の Parent は init only なので、新しいインスタンスを作って付け替える
        var movedEntry = new DraftEntry
        {
            RealId = srcEntry.RealId,
            TempId = srcEntry.TempId,
            State = (srcEntry.State == DraftState.Unchanged) ? DraftState.Modified : srcEntry.State,
            Parent = dstBlock,
            Entity = srcEntry.Entity
        };
        if (dstBlock.RealId.HasValue) movedEntry.Entity.BlockId = dstBlock.RealId.Value;

        // 旧 Block の同フラググループの EntrySeq を 1, 2, ... に詰める
        var oldGroup = oldBlock.Entries.Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag)
            .OrderBy(e => e.Entity.EntrySeq).ToList();
        for (int i = 0; i < oldGroup.Count; i++)
        {
            ushort ns = (ushort)(i + 1);
            if (oldGroup[i].Entity.EntrySeq != ns) { oldGroup[i].Entity.EntrySeq = ns; oldGroup[i].MarkModified(); }
        }

        // 新 Block の同フラググループに movedEntry を挿入し、再採番
        var dstGroupList = dstBlock.Entries.Where(e => e.State != DraftState.Deleted && e.Entity.IsBroadcastOnly == flag)
            .OrderBy(e => e.Entity.EntrySeq).ToList();
        if (insertAt < 0) insertAt = 0;
        if (insertAt > dstGroupList.Count) insertAt = dstGroupList.Count;
        dstGroupList.Insert(insertAt, movedEntry);
        // 新 Block の Entries に movedEntry を追加（リストの順序保持の都合上、まず追加してから seq だけ詰める）
        dstBlock.Entries.Add(movedEntry);
        for (int i = 0; i < dstGroupList.Count; i++)
        {
            ushort ns = (ushort)(i + 1);
            if (dstGroupList[i].Entity.EntrySeq != ns) { dstGroupList[i].Entity.EntrySeq = ns; dstGroupList[i].MarkModified(); }
        }
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
    /// <summary>
    /// 4 ペインのスプリッター位置を、現在のフォーム幅から計算して設定する
    /// （v1.2.0 工程 H-11 で 4 ペイン化に対応）。
    /// <para>
    /// 「左 320 / 右 380 / プレビュー 460 / 中央 = 残り」の方針で固定する。SplitterDistance は
    /// 各 SplitContainer の Panel1 の幅を表す。<br/>
    /// splitMain → Panel1 = 左ペイン (320)、Panel2 = (中央 + プレビュー + 右)<br/>
    /// splitCenterRest → Panel1 = 中央 (=残り)、Panel2 = (プレビュー + 右)<br/>
    /// splitPreviewRight → Panel1 = プレビュー (460)、Panel2 = 右 (380)
    /// </para>
    /// <para>
    /// 計算結果が Panel1MinSize / Panel2MinSize の制約に違反する場合は SplitContainer 側で
    /// 自動クランプされるため、本メソッドでは特別な例外処理は行わない。
    /// </para>
    /// </summary>
    private void ApplySplitterDistances()
    {
        const int leftWidth = 320;
        const int rightWidth = 380;
        const int previewWidth = 920;

        try
        {
            // splitMain: 左ペイン幅 = 320 px
            if (splitMain.Width > leftWidth + splitMain.Panel2MinSize)
            {
                splitMain.SplitterDistance = leftWidth;
            }

            // splitPreviewRight: プレビュー幅 = 460 px、右 = 残り (= 380)
            // FixedPanel=Panel2 なのでフォーム拡大時にプレビューが伸びて右ペインが固定される。
            if (splitPreviewRight.Width > previewWidth + splitPreviewRight.Panel2MinSize)
            {
                splitPreviewRight.SplitterDistance = previewWidth;
            }

            // splitCenterRest: 中央ペイン幅 = 全体から (プレビュー + 右 + スプリッタ) を引いた残り
            int rightHalfWidth = previewWidth + rightWidth + splitPreviewRight.SplitterWidth;
            int centerWidth = splitMain.Panel2.Width - rightHalfWidth - splitCenterRest.SplitterWidth;
            if (centerWidth > splitCenterRest.Panel1MinSize &&
                centerWidth < splitCenterRest.Width - splitCenterRest.Panel2MinSize)
            {
                splitCenterRest.SplitterDistance = centerWidth;
            }
        }
        catch (InvalidOperationException)
        {
            // 起動直後など SplitContainer の Width が確定していないタイミングで呼ばれた場合の保険。
            // 実害がないので静かにスキップ（次の Resize / Load で再試行される）。
        }
    }
}
