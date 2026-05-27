using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dapper;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Forms.Drafting;
using PrecureDataStars.Catalog.Forms.Preview;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジット本体編集フォーム。</summary>
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
    /// <summary>シリーズ種別マスタ。<c>series_kinds.credit_attach_to</c> でクレジットが
    /// SERIES に紐付くか EPISODE に紐付くかを規範決定するために使う。
    /// 起動時に <c>GetAllAsync</c> で全件取得して <see cref="_seriesKindsByCode"/> に詰める。</summary>
    private readonly SeriesKindsRepository _seriesKindsRepo;

    // ── プレビュー文字列を組み立てるためのマスタ参照 ──
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly SongRecordingsRepository _songRecRepo;

    /// <summary>現在 ListBox で選択しているクレジット。null なら未選択。</summary>
    private Credit? _currentCredit;

    /// <summary>現在編集中のクレジットの Draft セッション（導入）。</summary>
    private CreditDraftSession? _draftSession;

    /// <summary>Draft セッション構築用のローダ（導入）。 コンストラクタで Repositories を流し込んで生成する。</summary>
    private readonly CreditDraftLoader _draftLoader;

    /// <summary>
    /// クレジット選択処理の直列化キュー（先頭ポインタ）。
    /// Windows Forms の <see cref="ListBox.SelectedIndexChanged"/> は、<c>DataSource</c> 再代入や
    /// 内部状態変化で連鎖発火するため、<see cref="OnCreditSelectedAsync"/> が連続して複数回呼ばれる。
    /// 旧実装では bool フラグで「処理中なら return」していたが、
    /// 「先発した中間状態の要求が現行処理を握ったまま、後発の最終状態が握り潰される」
    /// 競合により、ListBox は最新を指しているのに TreeView は中間状態のまま残るバグがあった。
    ///
    /// 新実装は serial 番号方式：
    /// <see cref="OnCreditSelectedAsync"/> 呼び出しのたびに <see cref="_selectionRequestSerial"/> を
    /// インクリメントし、自分の serial と <see cref="_selectionRequestSerial"/> が一致する要求だけが
    /// 実処理 <c>ProcessSelectionAsync</c> に進む。前の要求の処理が走っていれば
    /// <see cref="_currentSelectionTask"/> を待ったうえで、自分が最新でなければスキップする。
    /// これにより「連続発火 N 回 → 最新の 1 回だけが実処理を完走」が保証される。
    /// </summary>
    private int _selectionRequestSerial;

    /// <summary>現在走っている選択処理の Task。
    /// 次の <see cref="OnCreditSelectedAsync"/> はこれを await してから「自分が最新か」判定する。
    /// 完了済み Task を初期値とする。</summary>
    private Task _currentSelectionTask = Task.CompletedTask;

    /// <summary><see cref="OnSeriesChangedAsync"/> の再入防止フラグ。</summary>
    private bool _isReloadingSeries;

    /// <summary><see cref="ReloadCreditsAsync"/> の再入防止フラグ。</summary>
    private bool _isReloadingCredits;

    /// <summary>現在表示中のクレジットに対応する <see cref="lstCredits"/> のインデックス。 未保存変更があるクレジットを切り替えようとして「キャンセル」が選ばれた時に、 この値を使って元のクレジットへ <c>SelectedIndex</c> を戻すために使う。 初期値 -1 は「まだクレジットを 1 度も選んでいない」状態を表す。</summary>
    private int _lastCreditListIndex = -1;

    /// <summary>クレジット切替の確認ダイアログでキャンセルが選ばれた時、<c>lstCredits.SelectedIndex</c> を プログラムから戻すと <see cref="ListBox.SelectedIndexChanged"/> が再発火するため、 その再発火を「ユーザー操作ではない」と判別して再帰確認を抑止するためのフラグ。</summary>
    private bool _suppressCreditSelection;

    /// <summary>
    /// フォーム閉じ時の確認ダイアログ → 保存 → 改めて Close、というシーケンスを実現するためのフラグ
    ///。<see cref="OnFormClosing"/> から非同期に保存処理を実行するために、
    /// 一度 e.Cancel = true で閉じるのを止め、await 完了後に <see cref="Form.Close"/> を再呼び出しする。
    /// その再呼び出し時に再度確認ダイアログが出るのを防ぐため、このフラグで「プログラム由来の Close」を識別する。
    /// </summary>
    private bool _isClosingProgrammatically;

    /// <summary>
    /// クレジット話数コピー処理中、cboSeries / cboEpisode をコピー先の値に切り替えるとき、
    /// SelectedIndexChanged の連鎖発火（OnSeriesChangedAsync → ReloadCreditsAsync → lstCredits 再構成 →
    /// OnCreditSelectedAsync）を抑止するためのフラグ。
    /// このフラグが立っている間、cbo 系の SelectedIndexChanged は早期 return する。
    /// </summary>
    private bool _suppressComboCascade;

    /// <summary>直近に「ユーザーが選択を確定した」シリーズ ID。 未保存変更がある状態でシリーズを切り替えようとして確認ダイアログ「キャンセル」が選ばれた時、 <c>cboSeries.SelectedValue</c> をこの値に戻すために使う。 初期値 -1 は「まだ確定したシリーズが無い」状態。</summary>
    private int _lastSeriesIdAccepted = -1;

    /// <summary>直近に「ユーザーが選択を確定した」エピソード ID。 シリーズ ID 同様、未保存変更キャンセル時の戻し用。-1 は「未確定」。</summary>
    private int _lastEpisodeIdAccepted = -1;

    /// <summary>Draft セッションを 1 トランザクションで DB に書き込む保存サービス。 保存ボタン押下で <see cref="CreditSaveService.SaveAsync"/> が呼ばれる。</summary>
    private readonly CreditSaveService _saveService;

    /// <summary>プレビュー文字列を組み立てる際のマスタ参照キャッシュ （何度も DB を引かないようツリー再構築のスコープ内で使い回す）。</summary>
    private readonly LookupCache _lookupCache;

    // QuickAdd ダイアログでマスタ自動投入に使うリポジトリ。
    // EntryEditorPanel.Initialize の追加引数として下流に流す。
    private readonly PersonsRepository _personsRepo;
    private readonly CompaniesRepository _companiesRepo;

    // キャラ名義 QuickAdd 用のリポジトリ。
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterKindsRepository _characterKindsRepo;

    // Tier / Group 階層の実体テーブル用リポジトリ。
    private readonly CreditCardTiersRepository _cardTiersRepo;
    private readonly CreditCardGroupsRepository _cardGroupsRepo;

    /// <summary>「旧名義 =&gt; 新名義」記法による既存 person への新 alias 追加で必要となる 中間表 person_alias_persons 用リポジトリ。</summary>
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    /// <summary>HTML プレビュー用に IConnectionFactory をフィールドとして保持。 コンストラクタの引数を <c>_factory</c> に詰め直しただけで、追加 DI は不要。</summary>
    private readonly PrecureDataStars.Data.Db.IConnectionFactory _factory;
    /// <summary>HTML プレビューおよび主題歌役職の columns 抽出で役職テンプレを引くためのリポジトリ。 role_templates 統合テーブルを扱う。シリーズ別 / 既定の解決は <c>ResolveAsync(role_code, series_id)</c> が担う。 既存 DI に追加せず、コンストラクタ内で <c>_factory</c> から都度生成する。</summary>
    private readonly RoleTemplatesRepository _roleTemplatesRepo;

    /// <summary>右クリック候補メニュー（テキストエリア内・役職コンテキストの最近使用名義）で
    /// 履歴を集計するための専用リポジトリ。<c>_factory</c> から都度生成する。</summary>
    private readonly RoleAliasUsageRepository _roleAliasUsageRepo;

    /// <summary>役職系譜（多対多）リポジトリ。候補集計時に役職クラスタ（連結成分）を
    /// 解決して role_code 拡張クエリに渡すために使う。<c>_factory</c> から都度生成する。</summary>
    private readonly RoleSuccessionsRepository _roleSuccessionsRepo;

    /// <summary>series_kinds をコード引きする辞書。起動時に 1 回ロードしてセッション中は使い回す。
    /// シリーズが切り替わるたびに <c>series.kind_code</c> → 対応する <c>credit_attach_to</c>
    /// （"SERIES" / "EPISODE"）を引いて <see cref="_currentScopeKind"/> を自動決定する。</summary>
    private IReadOnlyDictionary<string, SeriesKind>? _seriesKindsByCode;

    /// <summary>現在のクレジット選択スコープ（"SERIES" / "EPISODE"）。
    /// 旧 <c>rbScopeSeries.Checked</c> / <c>rbScopeEpisode.Checked</c> の代わり。
    /// シリーズ選択時に <c>series_kinds.credit_attach_to</c> から自動決定され、ユーザー手動切替は不可。</summary>
    private string _currentScopeKind = "EPISODE";
    /// <summary>HTML プレビューでクレジット種別の表示名を解決するためのリポジトリ。</summary>
    private readonly CreditKindsRepository _creditKindsRepo;
    /// <summary>埋め込みプレビュー描画用のレンダラ（コンストラクタで 1 回だけ生成し使い回す）。</summary>
    private CreditPreviewRenderer _previewRenderer = null!;

    /// <summary>プレビュー再描画の非同期処理が連打されるのを防ぐためのフラグ。 Draft 編集中は秒間複数回 <see cref="RefreshPreviewAsync"/> が呼ばれる可能性があるため、 描画中なら即座にスキップして「最後の 1 回」だけ確実に反映させる軽量制御。</summary>
    private bool _isRenderingPreview;
    /// <summary>プレビュー再描画を遅延実行するためのタイマー。 編集中のキー入力一打ごとに即座に WebBrowser を再描画すると重いので、 入力後 250ms 待ってから 1 回だけ再描画する Debounce 動作を実装する。</summary>
    private System.Windows.Forms.Timer? _previewDebounceTimer;

    // ───────────── テキスト編集パイプライン（Stage 1b 新設） ─────────────
    // テキスト編集が SSoT。txtBulkText.TextChanged → デバウンス 500ms → パース →
    // ApplyToDraftReplaceAsync → ツリー再構築 + プレビュー更新 の流れ。
    // クレジット選択時の初期化（Encoder で逆翻訳して txtBulkText.Text にセット）でも
    // TextChanged が発火するため、フラグでパイプライン起動を抑止する。

    /// <summary>クレジット選択時の Encoder 経由初期化中フラグ。
    /// 初期化中の <c>txtBulkText.Text</c> 代入で発火する TextChanged を抑止するために立てる。</summary>
    private bool _isInitializingText;

    /// <summary>テキスト編集 → Draft 反映を遅延実行するためのデバウンスタイマー。
    /// キー入力ごとに即時パースは重いため、入力後 500ms 待ってから 1 回だけパース → 反映する。</summary>
    private System.Windows.Forms.Timer? _textDebounceTimer;

    /// <summary>テキスト → Draft 反映パイプラインが現在走行中かどうか。
    /// パース → ResolveAsync → ApplyToDraftReplaceAsync の async 区間中に次の Tick が
    /// 来てしまうケースを防ぐための再入抑止フラグ。</summary>
    private bool _isApplyingTextToDraft;

    // ───────────── 警告ペインの元データ（Stage 2 追加） ─────────────
    // フィルタトグル切替時に「すでに集計した警告群を再フィルタするだけ」で済むよう、
    // 直近の警告データを保持する。重複グルーピングもこの上で実施する。

    /// <summary>警告ペインに反映済みの「アイテム単位」警告データ。フィルタ切替時の再描画に使う。</summary>
    private readonly List<WarningItemData> _currentWarnings = new();

    /// <summary>警告 1 件分の整形済みデータ（lvWarnings に表示する単位）。
    /// 重複グルーピング後の「ユニーク 1 行」を表す。</summary>
    private sealed class WarningItemData
    {
        /// <summary>関連するテキスト行番号（1 始まり、無関係なら 0）。
        /// 同じメッセージで複数行のものをグルーピングする際は「最小行番号」を採用。</summary>
        public int LineNumber { get; init; }

        /// <summary>重要度（Block / Warning / Info）。</summary>
        public Dialogs.WarningSeverity Severity { get; init; }

        /// <summary>表示メッセージ（オリジナル、1 行化済み）。</summary>
        public required string Message { get; init; }

        /// <summary>同じメッセージで重複していた件数（1 ならグルーピング無し）。</summary>
        public int Count { get; init; } = 1;

        /// <summary>「マスタ未登録の役職」警告のとき、役職表示名（テキスト中の見出し）を持つ。
        /// 非 null なら、行ダブルクリック時に <see cref="Dialogs.QuickAddRoleDialog"/> を
        /// この名前で起動する（行ジャンプ動作の代わり）。役職が DB に登録されたら自動的に
        /// テキスト再パースが走って警告が消える。</summary>
        public string? UnresolvedRoleName { get; init; }

        /// <summary>「マスタ未登録の所属屋号」警告のとき、屋号表示名（テキスト中の括弧内）を持つ。
        /// 非 null なら、行ダブルクリック時に <see cref="Dialogs.QuickAddCompanyAliasDialog"/> を
        /// この名前で起動する。屋号が登録されたら自動的にテキスト再パースが走って警告が消える。</summary>
        public string? UnresolvedAffiliationName { get; init; }
    }

    /// <summary>クレジット編集フォームを生成する。Program.cs の DI 経由で各リポジトリを受け取る。</summary>
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
        SeriesKindsRepository seriesKindsRepo,
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
        // 役職テンプレ展開で episode_theme_songs JOIN するために
        // 直接 DB 接続を取れる IConnectionFactory を受け取る。
        PrecureDataStars.Data.Db.IConnectionFactory factory,
        // 「旧 =&gt; 新」記法で既存 person に新 alias を追加する際の中間表用リポジトリ。
        PersonAliasPersonsRepository personAliasPersonsRepo)
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
        _seriesKindsRepo = seriesKindsRepo ?? throw new ArgumentNullException(nameof(seriesKindsRepo));
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
        _personAliasPersonsRepo = personAliasPersonsRepo ?? throw new ArgumentNullException(nameof(personAliasPersonsRepo));

        // HTML プレビューの CreditPreviewRenderer 構築に使うため、factory および
        // role_templates / credit_kinds 用のリポジトリを保持しておく（コンストラクタで都度新規作成しても
        // 良いが、フィールドにしておけば btnPreviewHtml クリック時に毎回作り直す手間が無い）。
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _roleTemplatesRepo = new RoleTemplatesRepository(_factory);
        _creditKindsRepo = new CreditKindsRepository(_factory);
        _roleAliasUsageRepo = new RoleAliasUsageRepository(_factory);
        _roleSuccessionsRepo = new RoleSuccessionsRepository(_factory);

        _lookupCache = new LookupCache(
            _personAliasesRepo, _companyAliasesRepo, _logosRepo,
            _characterAliasesRepo, _songRecRepo, _rolesRepo,
            factory ?? throw new ArgumentNullException(nameof(factory)));

        // Draft セッション構築用のローダ。
        // クレジット選択時に DB から全階層を読み込んで CreditDraftSession を作るのに使う。
        _draftLoader = new CreditDraftLoader(
            _cardsRepo, _cardTiersRepo, _cardGroupsRepo,
            _cardRolesRepo, _blocksRepo, _entriesRepo);

        // Draft セッションの保存サービス。
        // 保存ボタン押下で SaveAsync が呼ばれて 1 トランザクション内に DB へ反映する。
        _saveService = new CreditSaveService(factory);

        InitializeComponent();

        // ── 常時表示プレビューの初期化 ──
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

        // ── テキスト編集パイプライン（Stage 1b 新設） ──
        // txtBulkText の TextChanged → デバウンス 500ms → パース → Draft 全置換 →
        // ツリー再構築 + プレビュー更新。
        _textDebounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _textDebounceTimer.Tick += async (_, __) =>
        {
            _textDebounceTimer.Stop();
            await ApplyTextToDraftAsync();
        };
        txtBulkText.TextChanged += (_, __) =>
        {
            // Encoder 経由の初期化中はパイプラインを起動しない。
            if (_isInitializingText) return;
            _textDebounceTimer?.Stop();
            _textDebounceTimer?.Start();
        };

        // ── 右クリック候補メニュー（役職コンテキストの最近使用名義候補） ──
        // 右クリック位置から「役職スコープ」を判定し、当該役職クラスタに過去出現した
        // person_alias / company_alias の候補をスコア順に並べたメニューを出す。
        // テキストエリア内のみで動作する仕様（構造化エディタは対象外）。
        //
        // 実装パターン：
        //   1. ダミーの ContextMenuStrip を txtBulkText.ContextMenuStrip に割り当てる
        //      → これだけで Windows ネイティブの右クリックメニュー（切り取り/コピー/貼り付け 等）が抑止される。
        //   2. ダミー側の Opening をキャンセル（e.Cancel = true）して、空メニューが一瞬出るのも防ぐ。
        //   3. キャンセル直後に Cursor.Position から「クリック位置 → 行番号」を解決して、
        //      自前の ContextMenuStrip を async で組み立てて手動 Show する。
        // MouseDown フックで自前メニューを出す方式は、native の WM_CONTEXTMENU を抑止できず
        // 「自前メニューと native メニューが二重に出る」問題を起こすため採らない。
        InitializeCandidateMenuTrigger();

        // ── 警告ペインの強化機能（Stage 2） ──
        // フィルタチェックの切替は元データは触らず lvWarnings を再描画するだけ。
        chkFilterBlock.CheckedChanged   += (_, __) => RenderWarningsToListView();
        chkFilterWarning.CheckedChanged += (_, __) => RenderWarningsToListView();
        chkFilterInfo.CheckedChanged    += (_, __) => RenderWarningsToListView();
        // 警告行ダブルクリック → テキスト該当行へジャンプ。
        lvWarnings.MouseDoubleClick += OnWarningRowDoubleClick;

        // ── 左ペイン：選択コンボのイベント結線 ──
        // スコープ（SERIES / EPISODE）は series_kinds.credit_attach_to から自動決定するため、
        // ユーザー手動切替ラジオは無く、CheckedChanged 結線も持たない。
        cboSeries.SelectedIndexChanged += async (_, __) => await OnSeriesChangedAsync();
        // エピソード切替時も未保存確認を行うため、専用ハンドラを経由する。
        cboEpisode.SelectedIndexChanged += async (_, __) => await OnEpisodeChangedAsync();
        lstCredits.SelectedIndexChanged += async (_, __) => await OnCreditSelectedAsync();

        // Stage 3: ツリーは新 UI で表示専用化されたため、AfterSelect ハンドラ等は撤去。

        // ── 左ペインのクレジット系編集ボタン ──
        btnNewCredit.Click       += async (_, __) => await OnNewCreditAsync();
        btnCopyCredit.Click      += async (_, __) => await OnCopyCreditAsync();
        // クレジット並べ替え（明示順序 credit_seq の ↑↓ 入れ替え）。
        btnCreditUp.Click        += async (_, __) => await OnReorderCreditAsync(up: true);
        btnCreditDown.Click      += async (_, __) => await OnReorderCreditAsync(up: false);
        btnSaveCreditProps.Click += async (_, __) => await OnSaveCreditPropsAsync();
        btnDeleteCredit.Click    += async (_, __) => await OnDeleteCreditAsync();
        // Stage 3: btnBulkInput / btnAddCard / btnAddTier / btnAddGroup / btnAddRole / btnAddBlock /
        // btnAddEntry / btnMoveUp / btnMoveDown / btnDeleteNode は旧右ペイン時代のボタン群で、
        // 5 ペイン化（テキスト編集 SSoT）で不要になったため Designer / 本体ともに撤去。

        // Draft セッションの保存・取消ボタン結線。
        btnSaveDraft.Click   += async (_, __) => await OnSaveDraftAsync();
        btnCancelDraft.Click += async (_, __) => await OnCancelDraftAsync();

        // Stage 3: 旧 EntryEditorPanel の EntrySaved / EntryDeleted / DnD / 右クリックメニュー結線は撤去。

        // ── フォームリサイズ時に右ペイン幅を 380 固定で追随させる ──
        // splitCenterRight.FixedPanel = Panel2 にしているため、フォームを横に伸ばしたら
        // 中央ペインだけが広がる挙動を維持する。ただしフォームを縮めて中央ペインが
        // Panel1MinSize に達した場合は自然と SplitContainer 側で停止する。
        Resize += (_, __) => ApplySplitterDistances();

        // フォーム閉じ時に未保存変更があれば確認ダイアログを出す。
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
            // SplitContainer の SplitterDistance を、
            // フォームの Width / Height が確定したこのタイミングで動的に設定する。
            ApplySplitterDistances();

            // Stage 3: 旧 EntryEditorPanel / BlockEditorPanel / NodePropertiesEditorPanel の
            // Initialize / イベント結線は撤去。テキスト編集 SSoT の新 UI ではこれらの右ペイン
            // 編集 UI 経路を使わない。LookupCache はテキストパース → Draft 反映パイプラインと
            // ツリー / プレビューの両方で引き続き共有される。

            // series_kinds を 1 回ロードしてコード引き辞書を作る（series_id → kind_code → credit_attach_to の解決用）。
            var seriesKinds = await _seriesKindsRepo.GetAllAsync();
            _seriesKindsByCode = seriesKinds.ToDictionary(sk => sk.KindCode, sk => sk, StringComparer.Ordinal);

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

            // 初期シリーズに応じてスコープを自動決定 → クレジット一覧読込。
            await OnSeriesChangedAsync();
            // Stage 3: UpdateButtonStates 呼び出しは撤去（旧右ペイン用ボタン群の Enabled 制御）。

            // 起動時の初期選択を「次に編集すべき話数」に上書きする。
            // 第1優先：OP / ED どちらかが不完全（credit 行が無い or 配下エントリが 0）な最初のエピソード。
            // 該当が無ければ既定の「最初のエピソード」のまま。
            await ApplyInitialEpisodeAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>「次に編集すべき話数」を DB から引いて cboSeries / cboEpisode の選択を上書きし、
    /// 続けて lstCredits 内の「不完全な credit_kind」の行も自動選択する。
    /// 判定対象は OP / ED の 2 種類だけ。両方とも「credit 行が存在し、かつ配下に credit_block_entries が
    /// 1 件以上ある」状態を「完備」とみなし、これを満たさない最初のエピソード（シリーズ昇順 → 話数昇順）を選ぶ。
    /// SQL は CASE 式で「不完全な側の credit_kind」（OP 優先）も同時に返す。
    /// 該当エピソードが見つからない場合（全話数が OP/ED 完備の場合）は何もしない（既定の最初のエピソードのまま）。</summary>
    private async Task ApplyInitialEpisodeAsync()
    {
        try
        {
            // EPISODE スコープでないシリーズ（映画など SERIES-attaching）には「次に編集すべき話数」の概念が
            // 無いので、何もせず return する。スコープラジオは撤去済みのため、強制切替も不要。
            if (!string.Equals(_currentScopeKind, "EPISODE", StringComparison.Ordinal))
            {
                return;
            }

            // OP / ED どちらかが「credit + 配下 entry あり」を満たさない最初のエピソード。
            // MissingKind には、OP が不完全なら 'OP'、そうでなければ（= ED が不完全）'ED' を返す。
            const string sql = """
                SELECT e.episode_id AS EpisodeId, e.series_id AS SeriesId,
                       CASE
                         WHEN NOT EXISTS (
                           SELECT 1
                           FROM credits c
                           JOIN credit_cards card ON card.credit_id = c.credit_id
                           JOIN credit_card_tiers tier ON tier.card_id = card.card_id
                           JOIN credit_card_groups grp ON grp.card_tier_id = tier.card_tier_id
                           JOIN credit_card_roles role ON role.card_group_id = grp.card_group_id
                           JOIN credit_role_blocks blk ON blk.card_role_id = role.card_role_id
                           JOIN credit_block_entries en ON en.block_id = blk.block_id
                           WHERE c.episode_id = e.episode_id
                             AND c.credit_kind = 'OP'
                             AND c.is_deleted = 0
                         ) THEN 'OP'
                         ELSE 'ED'
                       END AS MissingKind
                FROM episodes e
                WHERE e.is_deleted = 0
                  AND e.series_id IS NOT NULL
                  AND (
                    NOT EXISTS (
                      SELECT 1
                      FROM credits c
                      JOIN credit_cards card ON card.credit_id = c.credit_id
                      JOIN credit_card_tiers tier ON tier.card_id = card.card_id
                      JOIN credit_card_groups grp ON grp.card_tier_id = tier.card_tier_id
                      JOIN credit_card_roles role ON role.card_group_id = grp.card_group_id
                      JOIN credit_role_blocks blk ON blk.card_role_id = role.card_role_id
                      JOIN credit_block_entries en ON en.block_id = blk.block_id
                      WHERE c.episode_id = e.episode_id
                        AND c.credit_kind = 'OP'
                        AND c.is_deleted = 0
                    )
                    OR
                    NOT EXISTS (
                      SELECT 1
                      FROM credits c
                      JOIN credit_cards card ON card.credit_id = c.credit_id
                      JOIN credit_card_tiers tier ON tier.card_id = card.card_id
                      JOIN credit_card_groups grp ON grp.card_tier_id = tier.card_tier_id
                      JOIN credit_card_roles role ON role.card_group_id = grp.card_group_id
                      JOIN credit_role_blocks blk ON blk.card_role_id = role.card_role_id
                      JOIN credit_block_entries en ON en.block_id = blk.block_id
                      WHERE c.episode_id = e.episode_id
                        AND c.credit_kind = 'ED'
                        AND c.is_deleted = 0
                    )
                  )
                ORDER BY e.series_id, e.series_ep_no
                LIMIT 1;
                """;

            (int EpisodeId, int SeriesId, string MissingKind)? hit;
            await using (var conn = await _factory.CreateOpenedAsync().ConfigureAwait(false))
            {
                hit = await conn.QuerySingleOrDefaultAsync<(int EpisodeId, int SeriesId, string MissingKind)?>(
                    new Dapper.CommandDefinition(sql));
            }
            if (hit is null) return;

            // ── 連鎖発火を抑止しつつ手動でシリーズ → エピソード → クレジット の順で切り替える ──
            // cboSeries.SelectedValue = X は同期で SelectedIndexChanged を発火するが、async ハンドラは
            // fire-and-forget で進むため、即座に cboEpisode.SelectedValue = Y を設定しても DataSource が
            // 更新されておらず Y が反映されない。_suppressComboCascade で連鎖を抑止し、自前で
            // エピソード一覧をロードしてから cboEpisode を設定する。
            _suppressComboCascade = true;
            try
            {
                cboSeries.SelectedValue = hit.Value.SeriesId;

                // エピソード DataSource を OnSeriesChangedAsync 相当の処理で手動再構築。
                var eps = await _episodesRepo.GetBySeriesAsync(hit.Value.SeriesId);
                cboEpisode.DisplayMember = "Label";
                cboEpisode.ValueMember = "Id";
                cboEpisode.DataSource = eps
                    .Select(e => new IdLabel(e.EpisodeId, $"第{e.SeriesEpNo}話  {e.TitleText}"))
                    .ToList();
                cboEpisode.SelectedValue = hit.Value.EpisodeId;

                _lastSeriesIdAccepted = hit.Value.SeriesId;
                _lastEpisodeIdAccepted = hit.Value.EpisodeId;
            }
            finally { _suppressComboCascade = false; }

            // 該当エピソードのクレジット一覧をロード（lstCredits の DataSource が再構成される）。
            await ReloadCreditsAsync();

            // 不完全な側の credit_kind（OP or ED）に該当する行を lstCredits 内で探して選択する。
            // lstCredits.SelectedIndex の代入で SelectedIndexChanged が発火し、OnCreditSelectedAsync 経由で
            // テキスト / プレビュー / ツリー / 警告ペインがその不完全クレジットに切り替わる。
            // 該当 credit_kind が無い場合（クレジット行自体が無いエピソード）は ReloadCreditsAsync 既定の
            // 「先頭行選択」の挙動に任せる。
            if (lstCredits.DataSource is List<CreditListItem> items)
            {
                int idx = items.FindIndex(x =>
                    string.Equals(x.Credit.CreditKind, hit.Value.MissingKind, StringComparison.Ordinal));
                if (idx >= 0) lstCredits.SelectedIndex = idx;
            }
        }
        catch
        {
            // 初期選択上書きはあくまで UX 改善で、失敗しても既定の挙動でフォームは開けるべき。
            // 例外は静かに飲み込む。
        }
    }

    /// <summary>選択中シリーズの <c>series_kinds.credit_attach_to</c> を引いて、
    /// <see cref="_currentScopeKind"/> ("SERIES" / "EPISODE") を更新し、UI（エピソードコンボの可視性）も連動させる。
    /// シリーズ未選択や種別未解決時は既定の "EPISODE" にしておく。</summary>
    private async Task ApplyScopeFromCurrentSeriesAsync()
    {
        string scope = "EPISODE";
        if (cboSeries.SelectedValue is int seriesId && _seriesKindsByCode is not null)
        {
            var series = await _seriesRepo.GetByIdAsync(seriesId);
            if (series is not null
                && _seriesKindsByCode.TryGetValue(series.KindCode, out var sk)
                && !string.IsNullOrEmpty(sk.CreditAttachTo))
            {
                scope = sk.CreditAttachTo;
            }
        }
        _currentScopeKind = scope;
        ApplyScopeUi();
    }

    /// <summary><see cref="_currentScopeKind"/> に応じてエピソードコンボのラベル + 本体を表示／非表示する。
    /// SERIES スコープでは「エピソード」関連 UI を完全に隠して、誤入力を防ぐ。</summary>
    private void ApplyScopeUi()
    {
        bool isEpisode = string.Equals(_currentScopeKind, "EPISODE", StringComparison.Ordinal);
        lblEpisode.Visible = isEpisode;
        cboEpisode.Visible = isEpisode;
        cboEpisode.Enabled = isEpisode;
    }

    /// <summary>シリーズ変更時：エピソードコンボを更新し、クレジット一覧を再読込。</summary>
    private async Task OnSeriesChangedAsync()
    {
        // 話数コピー処理中のプログラム由来切替は抑止。
        if (_suppressComboCascade) return;
        // cboSeries の SelectedIndexChanged 連鎖発火による多重実行を防ぐ。
        if (_isReloadingSeries) return;

        // シリーズ切替で lstCredits の DataSource が再構成されると
        // 現在表示中のクレジットが事実上失われるため、未保存変更がある場合は確認ダイアログを出す。
        // キャンセル時はシリーズコンボを直前の確定値に戻す（「ユーザー選択のまま」放置だと
        // 「コンボ表示は新シリーズ、エピソード／クレジットは旧シリーズ」という UI 不整合を起こしていた）。
        if (_suppressCreditSelection) return; // 戻し処理中の再発火は無視
        if (_draftSession is not null && _draftSession.HasUnsavedChanges)
        {
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok)
            {
                // キャンセル：シリーズコンボを直前確定値に戻す。SelectedIndexChanged の再発火は
                // _suppressComboCascade で抑止する（自身の OnSeriesChangedAsync 冒頭で早期 return する）。
                if (_lastSeriesIdAccepted >= 0)
                {
                    _suppressComboCascade = true;
                    try { cboSeries.SelectedValue = _lastSeriesIdAccepted; }
                    finally { _suppressComboCascade = false; }
                }
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
            // ユーザーが選択を確定したシリーズ ID を覚えておく（戻し用）。
            _lastSeriesIdAccepted = seriesId;
            // エピソード側も DataSource 再構成で先頭エピソードに自動移動するので、_lastEpisodeIdAccepted も
            // 同期更新（OnEpisodeChangedAsync 経由で更新されるが、空シリーズの場合に備えて明示クリア）。
            _lastEpisodeIdAccepted = (cboEpisode.SelectedValue as int?) ?? -1;

            // series_kinds.credit_attach_to から _currentScopeKind を自動決定。
            // SERIES スコープ作品（映画系）は cboEpisode を隠す、EPISODE スコープ作品（TV 系）は表示する。
            await ApplyScopeFromCurrentSeriesAsync();

            await ReloadCreditsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _isReloadingSeries = false; }
    }

    /// <summary>
    /// エピソード切替ハンドラ。
    /// 未保存変更がある状態でエピソードを切り替えると、最終的に <see cref="lstCredits"/> の
    /// DataSource が再構成されて現在表示中のクレジットが事実上失われるため、ここで確認ダイアログを出す。
    /// 確認 OK なら <see cref="ReloadCreditsAsync"/> を呼んで実際の再読込を行う。キャンセル時は
    /// <see cref="cboEpisode"/> の選択値を直前確定値に戻す。
    /// </summary>
    private async Task OnEpisodeChangedAsync()
    {
        // 話数コピー処理中のプログラム由来切替は抑止。
        if (_suppressComboCascade) return;
        // OnSeriesChangedAsync 経由で連鎖呼び出しされる場合は、既にあちらで確認済みなので
        // 改めてダイアログを出さないようにする（_suppressCreditSelection を一時利用）。
        if (_suppressCreditSelection) { await ReloadCreditsAsync(); return; }
        // シリーズ切替経由でエピソードリストが再構成された場合も同様に確認スキップ。
        // _isReloadingSeries が true の間はシリーズ側で既に確認済みなので二重ダイアログを抑止する。
        if (_isReloadingSeries)
        {
            // _lastEpisodeIdAccepted 自体はこの後の OnSeriesChangedAsync 末尾で更新されるので、
            // ここでは ReloadCreditsAsync を呼ばずに OnSeriesChangedAsync 側に任せて return する
            // （二重 ReloadCreditsAsync 実行を避けるため、_isReloadingCredits でも抑止されているが念のため）。
            return;
        }

        if (_draftSession is not null && _draftSession.HasUnsavedChanges)
        {
            bool ok = await ConfirmUnsavedChangesAsync();
            if (!ok)
            {
                // キャンセル時、エピソードコンボを直前確定値に戻す。
                if (_lastEpisodeIdAccepted >= 0)
                {
                    _suppressComboCascade = true;
                    try { cboEpisode.SelectedValue = _lastEpisodeIdAccepted; }
                    finally { _suppressComboCascade = false; }
                }
                return;
            }
        }
        // ユーザーが確定したエピソード ID を覚えておく（戻し用）。
        _lastEpisodeIdAccepted = (cboEpisode.SelectedValue as int?) ?? -1;
        await ReloadCreditsAsync();
    }

    /// <summary>クレジット一覧を絞り込み条件で再読込し、ListBox に流し込む。 scope_kind と series_id / episode_id だけで絞り込む（本放送限定フラグは エントリ単位で管理するため、クレジット側の絞り込み条件には含めない）。</summary>
    private async Task ReloadCreditsAsync()
    {
        // cboEpisode の SelectedIndexChanged 連鎖発火による多重実行を防ぐ。
        if (_isReloadingCredits) return;
        _isReloadingCredits = true;
        try
        {
            IReadOnlyList<Credit> credits;
            if (string.Equals(_currentScopeKind, "SERIES", StringComparison.Ordinal))
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
            _lastCreditListIndex = -1;

            // 表示順は明示順序カラム credit_seq に従う（同一スコープ内 1 始まり）。
            // 従来の credit_kind 暗黙順（OP→ED ハードコード）依存を解消し、
            // 運用者が下記 ↑↓ ボタンで並べ替えた順序をそのまま反映する。
            // リポジトリ側も ORDER BY credit_seq, credit_id で返すが、保険として
            // ここでも明示ソートする。
            var sortedCredits = credits
                .OrderBy(c => c.CreditSeq)
                .ThenBy(c => c.CreditId)
                .ToList();

            lstCredits.DataSource = sortedCredits
                .Select((c, i) => new CreditListItem(c, BuildCreditListLabel(c, i + 1)))
                .ToList();

            if (sortedCredits.Count == 0)
            {
                // _draftSession も null 化しないと、リスト空でも「直前に開いていた
                _currentCredit = null;
                _draftSession = null;
                _lastCreditListIndex = -1;
                ClearTreeAndPreview();
                lblStatusBar.Text = "現在編集中: （該当クレジットなし）";
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _isReloadingCredits = false; }
    }

    /// <summary>
    /// クレジットリストボックスのラベルを生成。
    /// クレジット見出しは <c>#{credit_id}</c>（DB 主キー直表示）ではなく、
    /// ユーザー視点では DB 主キーは無関係でかえって混乱の元（同一エピソード内のクレジットが
    /// 「#7, #14」のように飛び番表示されてしまう）のため、表示母集合内での 1 始まり順序番号に変更。
    /// 順序は呼び出し側でソート済み（明示順序カラム credit_seq 昇順。
    /// ↑↓ ボタンで運用者が並べ替えた順をそのまま反映する）。
    /// </summary>
    /// <param name="c">対象クレジット。</param>
    /// <param name="orderNo">表示母集合内での 1 始まり順序番号（リスト先頭=1）。</param>
    private static string BuildCreditListLabel(Credit c, int orderNo)
        => $"#{orderNo}  {c.CreditKind}  ({c.Presentation})";

    /// <summary>クレジット選択時のエントリポイント（ディスパッチャ）。
    /// 直列化キュー方式で「連続発火 N 回 → 最新 1 回だけが実処理を完走」を保証する。
    /// 本体処理は <see cref="ProcessSelectionAsync"/> に委譲。</summary>
    private async Task OnCreditSelectedAsync()
    {
        // 話数コピー処理中のプログラム由来切替は抑止。
        if (_suppressComboCascade) return;
        // プログラムから SelectedIndex を戻したことによる再発火は無視する。
        if (_suppressCreditSelection) return;

        // 最新の要求 ID を払い出し。
        // この時点では「自分が最新」だが、await の間に後続の発火で更新される可能性がある。
        int mySerial = ++_selectionRequestSerial;

        // 前の処理を待つ。連続発火の中間要求はここで「自分が最新でない」判定により全てスキップされる。
        // ConfigureAwait(true) で UI スレッドに戻って続行する。
        try
        {
            await _currentSelectionTask.ConfigureAwait(true);
        }
        catch
        {
            // 前の処理の例外は当該処理側で ShowError 済み。ここでは握り潰して自分の処理に進む。
        }

        // 自分が最新でなければスキップ（後続の要求が既に来ている）。
        if (mySerial != _selectionRequestSerial) return;

        // 自分が最新なので実処理を起動し、次の呼び出しが待つ Task として記録する。
        var task = ProcessSelectionAsync(mySerial);
        _currentSelectionTask = task;
        await task.ConfigureAwait(true);
    }

    /// <summary>クレジット選択の実処理本体：プロパティを左下に表示し、中央ツリーを構築する。
    /// 直列化キュー（<see cref="OnCreditSelectedAsync"/>）から「自分が最新の要求」と確定したあとに
    /// 呼ばれる前提。途中の await の合間にさらに新しい要求が来ていたら早期 return する。</summary>
    /// <param name="serial">この処理に紐付く要求 ID。<see cref="_selectionRequestSerial"/> と一致しなくなったら諦める。</param>
    private async Task ProcessSelectionAsync(int serial)
    {
        // 処理途中で新しい選択要求が来ていたら、自分は途中で諦める（防御的に冒頭でも確認）。
        if (serial != _selectionRequestSerial) return;

        // 未保存変更がある状態で別クレジットへ切り替える前に確認ダイアログ。
        // 直列化キュー方式では「自分が最新の要求」と確定したあとで 1 回だけ出る。
        // DataSource 差し替えによる中間状態（SelectedIndex が一時的に -1 → 0 になる瞬間など）では
        // 後続の最新要求に追い越されてここまで来ないため、ユーザー意図に基づく切替時のみダイアログが出る。
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

        // ダイアログ表示中に新しい要求が来ていれば、ここで諦める。
        if (serial != _selectionRequestSerial) return;

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
            if (serial != _selectionRequestSerial) return;

            // ── Draft セッション構築 ──
            // ツリー描画は「DB → Draft → ツリー描画」の経路で行う。
            // 編集操作はすべてこの Draft オブジェクトに対して行い、保存ボタンで一括確定する設計。
            _draftSession = await _draftLoader.LoadAsync(_currentCredit);
            if (serial != _selectionRequestSerial) return;

            // 右ペインのエディタに最新の Draft セッション参照を流し込む。
            // EntryEditorPanel が新規 DraftEntry の Temp ID を払い出すために必要。
            // Stage 3: 旧 EntryEditorPanel / BlockEditorPanel への SetSession は撤去。
            _lookupCache.SetPendingSession(_draftSession);

            // テキスト編集ペインを Encoder で逆翻訳した内容で初期化（Stage 1b）。
            // 初期化中の TextChanged は _isInitializingText で抑止し、デバウンス起動を防ぐ。
            await ReinitializeTextFromDraftAsync();
            if (serial != _selectionRequestSerial) return;

            // 中央ペインのツリー再構築（Draft 経由）
            await RebuildTreeFromDraftAsync();
            if (serial != _selectionRequestSerial) return;

            // Stage 3: UpdateButtonStates 呼び出しは撤去（旧右ペイン用ボタン群の Enabled 制御）。

            // プレビューウィンドウが開いていればクレジット切替に追従して再描画
            await RefreshPreviewAsync();
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
        // 未保存変更がある場合はステータスバーに「★ 未保存」マークを表示。
        // ツリー背景色（黄色）と併せて、ユーザーが保存忘れに気付きやすくする。
        string unsavedMark = (_draftSession is not null && _draftSession.HasUnsavedChanges)
            ? "  ★ 未保存の変更あり"
            : "";
        lblStatusBar.Text =
            $"現在編集中: {scope} {idLabel}  /  {_currentCredit.CreditKind}  ({_currentCredit.Presentation}){unsavedMark}";
    }

    /// <summary>選択中クレジットのカード／役職／ブロック／エントリを TreeView に再構築する。</summary>
    /// fix4: 並列実行による Tree.Nodes 重複追加を防ぐため、
    /// 「先にローカル List にすべての TreeNode を組み立てきる → 最後に同期セクションで
    /// Clear → AddRange → EndUpdate を一気に実行」パターンに書き換えた。
    /// 旧実装では Nodes.Clear() の直後から DB アクセスの await を伴う foreach が続くため、
    /// ボタン連打や AfterSelect イベント連鎖で複数の RebuildTreeAsync が並列に await されると、
    /// 互いの Clear と Add が交互に走って同じカードノードが Tree に複数追加される問題があった。
    /// 新実装は同期反映区間に await を含まないので、並列で呼ばれても各呼び出しが
    /// 完成形のツリーで上書きするだけになり、重複は生じない。
    private async Task RebuildTreeAsync()
    {
        // で Draft 経由に切り替え。本メソッドは互換用ラッパで、
        // 実体は RebuildTreeFromDraftAsync が担う。Draft セッションが未構築の場合は何もしない
        // （クレジット未選択の状態。OnCreditSelectedAsync が呼ばれた時点で session が作られ、
        //  本メソッドが Draft からツリーを描画する流れになる）。
        await RebuildTreeFromDraftAsync();
    }

    /// <summary>TreeView 上で「Pending マスタを参照しているノード」を塗る色。
    /// HTML プレビュー側の ⚠ 赤太字（#cc0000）と同じトーンに揃える。</summary>
    private static readonly Color PendingNodeColor = Color.FromArgb(0xCC, 0x00, 0x00);

    /// <summary>エントリのマスタ参照列に Pending（負数仮 ID）が含まれているか判定する。
    /// 含まれていればツリーノードの ForeColor を赤にする条件として使う。
    /// 対象列：PersonAliasId / CharacterAliasId / CompanyAliasId / AffiliationCompanyAliasId / LogoId。</summary>
    private static bool HasPendingMasterId(CreditBlockEntry e)
        => (e.PersonAliasId is int p && p < 0)
        || (e.CharacterAliasId is int c && c < 0)
        || (e.CompanyAliasId is int co && co < 0)
        || (e.AffiliationCompanyAliasId is int af && af < 0)
        || (e.LogoId is int l && l < 0);

    /// <summary>Draft セッション（_draftSession）からツリーを構築する。</summary>
    /// 並列実行による Tree.Nodes 重複追加を防ぐため、「先にローカル List にすべての TreeNode を
    /// 組み立てきる → 最後に同期セクションで Clear → AddRange → EndUpdate を一気に実行」パターン。
    private async Task RebuildTreeFromDraftAsync()
    {
        if (_currentCredit is null || _draftSession is null) { ClearTreeAndPreview(); return; }

        // ─── フェーズ 1: Draft からツリーを組み立て（この間 treeStructure には触らない）───
        var newRootNodes = new List<TreeNode>();

        // ツリー上の Card 番号は 1 始まりの連続表示にする。
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
                            // 役職の書式テンプレは role_templates テーブルで管理するため、
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

                            // 先頭企業屋号 (leading_company_alias_id) が設定されていれば
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
                            // ブロック先頭屋号が Pending（負数 ID）なら、ブロックノード全体を赤色で警告表示。
                            // HTML プレビュー側の ⚠ 赤太字と意味論を揃える（TreeView は文字単位の色変えが
                            // 標準では出来ないためノード全体を塗る方針、ユーザー指定）。
                            if (block.LeadingCompanyAliasId is int lid && lid < 0)
                            {
                                blockNode.ForeColor = PendingNodeColor;
                            }
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
                                // Pending マスタを参照しているエントリは ForeColor を赤に。
                                if (HasPendingMasterId(entry))
                                {
                                    entryNode.ForeColor = PendingNodeColor;
                                }
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

        // 未保存変更があれば背景色を黄色っぽく。
        // 視覚的に「保存待ち」を示すため、TreeView 全体の背景色を変える。
        ApplyDraftBackgroundColor();

        // 終盤の修正：
        // ① TreeView の表示更新を画面へ反映させるため Refresh を強制呼び出し。Clear/AddRange の直後は
        //    まれに描画が遅延することがあるため、保険として明示的に Invalidate + Update する。
        // ② 末尾で blockEditor / entryEditor を ClearAndDisable はしない。編集中に再構築が走った
        //    場合に右ペインの状態（_currentDraft）が消えてしまうため。右ペインの状態は選択ノード
        //    変更時の OnTreeNodeSelected で適切に切り替わるので、ここでクリアする必要はない。
        // ※ Application.DoEvents() は SelectedIndexChanged 連鎖発火など別バグの温床になり得るため
        //    入れない。本来の真因（OnCreditSelectedAsync 等の再入による _draftSession 多重生成）は
        //    各ハンドラの再入防止フラグで根本対処済み。
        treeStructure.Refresh();

        // Draft 編集のリアルタイムプレビュー反映。
        RequestPreviewRefresh();
    }

    /// <summary>未保存変更の有無に応じてツリー背景色を切り替える。 未保存変更ありなら LightYellow 系、なしなら標準のウィンドウ色（白）。</summary>
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
        // 保存・取消ボタンの Enabled 状態も同時に反映する。
        // 未保存変更がある時のみ有効にすることで、押し間違いを防ぐ。
        btnSaveDraft.Enabled = dirty;
        btnCancelDraft.Enabled = dirty;

        // ステータスバーの「★ 未保存の変更あり」マークを同期更新する。
        // UpdateStatusBarAsync を再実行すると DB アクセスが走って高コストなので、
        // 既存のテキストの末尾だけを操作する形で軽量に切り替える。
        const string mark = "  ★ 未保存の変更あり";
        if (_currentCredit is not null)
        {
            // ToolStripStatusLabel.Text は string? 戻り。null 合体で吸収。
            string text = lblStatusBar.Text ?? "";
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

    // ─── Stage 1b: テキスト編集 → Draft 反映パイプライン本体 ───

    /// <summary>クレジット選択時・保存後の Draft 再ロード後に呼ぶ。
    /// 現在の <see cref="_draftSession"/> を Encoder で逆翻訳し、<c>txtBulkText.Text</c> に流し込む。
    /// 初期化中の TextChanged は <see cref="_isInitializingText"/> フラグで抑止する。
    /// Draft が null の場合は空文字をセット。</summary>
    private async Task ReinitializeTextFromDraftAsync()
    {
        // 編集中タイマーが走っていれば一旦止める（初期化後の Text 設定で発火する分は抑止される）。
        _textDebounceTimer?.Stop();

        _isInitializingText = true;
        try
        {
            string text = _draftSession?.Root is not null
                ? await CreditBulkInputEncoder.EncodeFullAsync(_draftSession.Root, _lookupCache)
                : "";
            txtBulkText.Text = text;
            // 初期化時点では警告は何も無いのでクリアする（前のクレジットの警告が残らないように）。
            UpdateWarningsPane(null, null, null, null);
            ClearTextParseErrorIndicator();
        }
        catch (Exception ex)
        {
            // Encoder で何か壊れていた場合は空文字にしてエラーを表示。
            txtBulkText.Text = "";
            ShowError(ex);
        }
        finally
        {
            _isInitializingText = false;
        }
    }

    /// <summary>テキストエディタの内容をパースし、現在の Draft セッションに ApplyToDraftReplaceAsync で
    /// 全置換する。デバウンスタイマー満了時に呼ばれる。
    /// パースエラー時はステータスバーに「⚠ パースエラー」を表示し、Draft は前の成功状態を保持する。</summary>
    private async Task ApplyTextToDraftAsync()
    {
        if (_draftSession is null || _currentCredit is null) return;
        // パイプライン区間中の再入を抑止（async 区間中に次の Tick が来ても何もしない）。
        if (_isApplyingTextToDraft) return;
        _isApplyingTextToDraft = true;
        try
        {
            string text = txtBulkText.Text;

            // パース → 役職解決 → Draft 全置換
            // CreditBulkInputParser は静的、ResolveAsync / ApplyToDraftReplaceAsync は
            // OnBulkInputAsync と同じ BulkApplyService インスタンスを使い回す形にしたいが、
            // 現状は BulkApplyService をフィールドに持っていない（OnBulkInputAsync 内で都度 new）ため、
            // ここでも同じパターンで都度 new する。インスタンスは軽量。
            var parsed = Dialogs.CreditBulkInputParser.Parse(text);
            // パース時の構造エラーは parsed.Warnings に積まれる。致命的エラーがあれば
            // ApplyToDraftReplaceAsync が例外を投げるが、通常は警告として扱われる。
            var bulkSvc = new Dialogs.CreditBulkApplyService(
                _rolesRepo,
                _personsRepo,
                _personAliasesRepo,
                _charactersRepo,
                _characterAliasesRepo,
                _companiesRepo,
                _companyAliasesRepo,
                _logosRepo,
                _personAliasPersonsRepo);
            await bulkSvc.ResolveAsync(parsed);
            // テキスト編集はクレジット全体を SSoT として扱うので、スコープは ForCredit で全体置換。
            var scope = DraftScopeRef.ForCredit(_draftSession.Root);
            await bulkSvc.ApplyToDraftReplaceAsync(parsed, _draftSession, scope, Environment.UserName);

            // Pending マップが変わった可能性があるので LookupCache に最新セッションを再通知。
            _lookupCache.SetPendingSession(_draftSession);

            // ツリー再構築 + プレビュー更新
            await RebuildTreeFromDraftAsync();
            await RefreshPreviewAsync();

            // 警告ペインを更新（パース警告 + Resolver / Apply の InfoMessages + 未解決役職 + 未解決所属屋号を一覧表示）。
            UpdateWarningsPane(parsed, bulkSvc.InfoMessages, bulkSvc.UnresolvedRoles, bulkSvc.UnresolvedAffiliations);

            // パイプライン成功時はステータスバーのパースエラー表記をクリア（成功した瞬間に消す）。
            ClearTextParseErrorIndicator();
        }
        catch (Exception ex)
        {
            // パースエラー時はステータスバーにマークを残し、Draft は前の成功状態を保持。
            // MessageBox は連発するとうるさいのでステータスバー表示のみ。
            // 警告ペインにもエラー 1 件を立てる。
            ShowTextParseErrorIndicator(ex.Message);
            UpdateWarningsPaneWithSingleError(ex.Message);
        }
        finally
        {
            _isApplyingTextToDraft = false;
        }
    }

    /// <summary>警告ペインの内容を、パイプライン実行結果で更新する。
    /// <paramref name="parsed"/> の <c>Warnings</c>（行番号付き構文警告）と、<paramref name="infoMessages"/>
    /// （マスタ解決時の「✅ … 追加予定」「⚠ … 1 字違い」等の文字列リスト）を結合し、
    /// 同じメッセージ文字列で重複していたら「×N」表記でグルーピングしたうえで <see cref="_currentWarnings"/> に格納する。
    /// 実際の ListView 描画は <see cref="RenderWarningsToListView"/> に委譲（フィルタ切替時に再呼び出し可）。</summary>
    private void UpdateWarningsPane(
        Dialogs.BulkParseResult? parsed,
        IReadOnlyList<string>? infoMessages,
        IReadOnlyList<Dialogs.ParsedRole>? unresolvedRoles,
        IReadOnlyList<Dialogs.UnresolvedAffiliation>? unresolvedAffiliations)
    {
        _currentWarnings.Clear();

        // (a) 元の警告群を「(message → エントリ群)」辞書にまとめる。
        // メッセージ文字列をキーに、最小 LineNumber と件数を集計する。
        var grouped = new Dictionary<(Dialogs.WarningSeverity Sev, string Msg),
                                     (int MinLine, int Count)>();
        void Add(int line, Dialogs.WarningSeverity sev, string msg)
        {
            string oneLine = (msg ?? "").Replace("\r", " ").Replace("\n", " ");
            var key = (sev, oneLine);
            if (grouped.TryGetValue(key, out var prev))
            {
                int minLine = (prev.MinLine == 0)
                    ? line
                    : (line == 0 ? prev.MinLine : Math.Min(prev.MinLine, line));
                grouped[key] = (minLine, prev.Count + 1);
            }
            else
            {
                grouped[key] = (line, 1);
            }
        }
        if (parsed is not null)
        {
            foreach (var w in parsed.Warnings) Add(w.LineNumber, w.Severity, w.Message);
        }
        if (infoMessages is not null)
        {
            foreach (var msg in infoMessages)
            {
                var sev = (msg ?? "").StartsWith("⚠", StringComparison.Ordinal)
                    ? Dialogs.WarningSeverity.Warning
                    : Dialogs.WarningSeverity.Info;
                Add(0, sev, msg ?? "");
            }
        }

        // (b) グループ化結果を _currentWarnings に格納（重要度 desc → 行番号 asc の順で安定ソート）。
        foreach (var kv in grouped
            .OrderByDescending(g => (int)g.Key.Sev)
            .ThenBy(g => g.Value.MinLine))
        {
            _currentWarnings.Add(new WarningItemData
            {
                LineNumber = kv.Value.MinLine,
                Severity = kv.Key.Sev,
                Message = kv.Key.Msg,
                Count = kv.Value.Count,
            });
        }

        // (c) マスタ未登録役職を独立した警告行として追加。ダブルクリック時の挙動が
        // 「行ジャンプ」ではなく「QuickAddRoleDialog 起動」に切り替わるよう
        // UnresolvedRoleName を立てておく。同名重複は HashSet で 1 件にまとめる。
        if (unresolvedRoles is not null && unresolvedRoles.Count > 0)
        {
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ur in unresolvedRoles)
            {
                string name = (ur.DisplayName ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!seenNames.Add(name)) continue;
                _currentWarnings.Add(new WarningItemData
                {
                    LineNumber = ur.LineNumber,
                    Severity = Dialogs.WarningSeverity.Block,
                    Message = $"役職「{name}」がマスタ未登録（ダブルクリックで登録ダイアログを開く）",
                    Count = 1,
                    UnresolvedRoleName = name,
                });
            }
        }

        // (d) マスタ未登録の所属屋号を警告化（重要度 Block、ダブルクリックで QuickAddCompanyAliasDialog 起動）。
        // クオート記法 ("..." 強制テキスト) は引き当てを試みないため、ここに来るのは引き当てを試みて失敗したものだけ。
        if (unresolvedAffiliations is not null && unresolvedAffiliations.Count > 0)
        {
            var seenAffilNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ua in unresolvedAffiliations)
            {
                string name = (ua.Name ?? "").Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!seenAffilNames.Add(name)) continue;
                _currentWarnings.Add(new WarningItemData
                {
                    LineNumber = ua.LineNumber,
                    Severity = Dialogs.WarningSeverity.Block,
                    Message = $"所属屋号「{name}」がマスタ未登録（ダブルクリックで登録ダイアログを開く）",
                    Count = 1,
                    UnresolvedAffiliationName = name,
                });
            }
        }

        RenderWarningsToListView();
    }

    /// <summary>パースが例外で死んだ時に警告ペインを「エラー 1 件のみ」状態にする。</summary>
    private void UpdateWarningsPaneWithSingleError(string message)
    {
        _currentWarnings.Clear();
        _currentWarnings.Add(new WarningItemData
        {
            LineNumber = 0,
            Severity = Dialogs.WarningSeverity.Block,
            Message = (message ?? "").Replace("\r", " ").Replace("\n", " "),
            Count = 1,
        });
        RenderWarningsToListView();
    }

    /// <summary><see cref="_currentWarnings"/> を現在のフィルタチェック状態に従って lvWarnings に描画する。
    /// フィルタトグル切替時もここを再呼び出しすれば再フィルタが効く。
    /// ヘッダの件数バッジ（「⚠ 警告 (N / 全 M)」）もここで同期更新する。</summary>
    private void RenderWarningsToListView()
    {
        bool showBlock   = chkFilterBlock.Checked;
        bool showWarning = chkFilterWarning.Checked;
        bool showInfo    = chkFilterInfo.Checked;

        lvWarnings.BeginUpdate();
        try
        {
            lvWarnings.Items.Clear();
            int shownCount = 0;
            foreach (var w in _currentWarnings)
            {
                bool pass = w.Severity switch
                {
                    Dialogs.WarningSeverity.Block => showBlock,
                    Dialogs.WarningSeverity.Warning => showWarning,
                    _ => showInfo,
                };
                if (!pass) continue;
                AddWarningRow(w);
                shownCount++;
            }
            UpdateWarningsHeaderBadge(shownCount, _currentWarnings.Count);
        }
        finally
        {
            lvWarnings.EndUpdate();
        }
    }

    /// <summary>WarningItemData 1 件を lvWarnings に行追加。Tag に LineNumber を入れてクリック→ジャンプで参照する。</summary>
    private void AddWarningRow(WarningItemData w)
    {
        (string icon, Color fore) = w.Severity switch
        {
            Dialogs.WarningSeverity.Block   => ("🔥", Color.FromArgb(0xCC, 0x00, 0x00)),
            Dialogs.WarningSeverity.Warning => ("⚠", Color.FromArgb(0xB0, 0x60, 0x00)),
            _                               => ("ⓘ", Color.FromArgb(0x00, 0x60, 0xC0)),
        };
        string lineText = w.LineNumber > 0 ? w.LineNumber.ToString() : "";
        string display = w.Count > 1 ? $"{w.Message}  (×{w.Count})" : w.Message;
        var item = new ListViewItem(lineText) { ForeColor = fore, ToolTipText = w.Message, Tag = w };
        item.SubItems.Add(icon);
        item.SubItems.Add(display);
        lvWarnings.Items.Add(item);
    }

    /// <summary>警告ペインヘッダの件数バッジを更新する。
    /// フィルタで除外されている件数があれば「⚠ 警告 (N / 全 M)」のような表記、なければ「⚠ 警告 (M)」、
    /// 件数 0 なら「⚠ 警告」のままにする。</summary>
    private void UpdateWarningsHeaderBadge(int shownCount, int totalCount)
    {
        if (totalCount == 0)
        {
            lblWarningsHeader.Text = "⚠ 警告";
        }
        else if (shownCount == totalCount)
        {
            lblWarningsHeader.Text = $"⚠ 警告 ({totalCount})";
        }
        else
        {
            lblWarningsHeader.Text = $"⚠ 警告 ({shownCount} / 全 {totalCount})";
        }
    }

    /// <summary>警告ペインの行をダブルクリックしたとき、その警告に紐付く <c>LineNumber</c> を
    /// テキストペインで選択して行頭にスクロールする。行番号 0（マスタ解決系で行番号を持たない警告）の
    /// 場合は何もしない。</summary>
    private async void OnWarningRowDoubleClick(object? sender, EventArgs e)
    {
        if (lvWarnings.SelectedItems.Count == 0) return;
        var item = lvWarnings.SelectedItems[0];
        if (item.Tag is not WarningItemData data) return;

        // マスタ未登録役職の警告行は、ダブルクリックで QuickAddRoleDialog を起動する。
        // 行ジャンプは「次に同じ警告が出てきた時にどこの行から始まったか」を出す既存挙動だが、
        // 未登録役職の場合は「登録すれば警告が消える」状況なので、ダイアログ直起動の方が UX 効率が高い。
        if (!string.IsNullOrEmpty(data.UnresolvedRoleName))
        {
            await OpenQuickAddRoleDialogAndReparseAsync(data.UnresolvedRoleName!);
            return;
        }

        // マスタ未登録の所属屋号も同様、QuickAddCompanyAliasDialog を起動する。
        if (!string.IsNullOrEmpty(data.UnresolvedAffiliationName))
        {
            await OpenQuickAddCompanyAliasDialogAndReparseAsync(data.UnresolvedAffiliationName!);
            return;
        }

        if (data.LineNumber <= 0) return;

        // txtBulkText の指定行の先頭オフセットを計算 → SelectionStart に設定 → ScrollToCaret。
        // 行は 1 始まりなので 0 始まりインデックスに変換。
        int lineIndex = data.LineNumber - 1;
        try
        {
            int offset = txtBulkText.GetFirstCharIndexFromLine(lineIndex);
            if (offset < 0) return; // 範囲外
            // 行末まで選択して該当行をハイライト表示。
            int lineEnd = (lineIndex + 1 < txtBulkText.Lines.Length)
                ? txtBulkText.GetFirstCharIndexFromLine(lineIndex + 1) - Environment.NewLine.Length
                : txtBulkText.TextLength;
            int len = Math.Max(0, lineEnd - offset);
            txtBulkText.Focus();
            txtBulkText.SelectionStart = offset;
            txtBulkText.SelectionLength = len;
            txtBulkText.ScrollToCaret();
        }
        catch
        {
            // 該当行が存在しない（テキスト編集後に行数が減った等）ケースは静かにスキップ。
        }
    }

    /// <summary>マスタ未登録役職の警告行ダブルクリックで <see cref="Dialogs.QuickAddRoleDialog"/> を開き、
    /// 役職が登録されたらテキストを再パースして警告を消し、Role 配下のエントリを反映する。
    /// <paramref name="prefilledNameJa"/> はダイアログの「表示名（日本語）」欄に流し込む既定値。
    /// role_code と name_en、書式区分は運用者が映画文脈や TV 文脈に応じて入力する。</summary>
    private async Task OpenQuickAddRoleDialogAndReparseAsync(string prefilledNameJa)
    {
        try
        {
            using var dlg = new Dialogs.QuickAddRoleDialog(_rolesRepo)
            {
                PrefilledNameJa = prefilledNameJa,
            };
            var result = dlg.ShowDialog(this);
            if (result != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedRoleCode))
            {
                return;
            }

            // 役職マスタが増えたので、候補メニュー側のキャッシュ（roles 全件 / role_successions 全件）を破棄して
            // 次回右クリックで再ロードさせる（候補メニュー機構が役職表示名を引けるようにするため）。
            InvalidateRoleMasterCaches();

            // テキストを再パースして反映：txtBulkText.Text は変えずに ApplyTextToDraftAsync を再起動する。
            // 直接 await すれば「ダイアログを閉じた直後」のタイミングで再パースが走る。
            await ApplyTextToDraftAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"役職登録に失敗しました:\n{ex.Message}",
                "役職登録", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>マスタ未登録の所属屋号の警告行ダブルクリックで <see cref="Dialogs.QuickAddCompanyAliasDialog"/> を開き、
    /// 屋号が登録されたらテキストを再パースして警告を消し、所属側を <c>affiliation_company_alias_id</c> に解決する。
    /// <paramref name="prefilledAliasName"/> はダイアログの「屋号名」欄に流し込む既定値。</summary>
    private async Task OpenQuickAddCompanyAliasDialogAndReparseAsync(string prefilledAliasName)
    {
        try
        {
            using var dlg = new Dialogs.QuickAddCompanyAliasDialog(_companiesRepo, _companyAliasesRepo, prefilledAliasName);
            var result = dlg.ShowDialog(this);
            if (result != DialogResult.OK || dlg.CreatedAliasId is null)
            {
                return;
            }

            // 候補メニュー側の役職マスタキャッシュ撤去と同じ理由：LookupCache 内の company_alias 同名件数辞書を
            // 撤去して、Encoder の「#alias_id 明示記法を出すか」判定が新規 alias を含めて再評価されるようにする。
            _lookupCache.ClearAll();

            // テキストを再パースして反映。これで「所属屋号未登録」警告が消えて、新 alias が引き当てられる。
            await ApplyTextToDraftAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"屋号登録に失敗しました:\n{ex.Message}",
                "屋号登録", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>ステータスバー右側に「⚠ パースエラー: {msg}」を出す（テキスト → Draft 反映が失敗した状態）。
    /// 次回パース成功時に <see cref="ClearTextParseErrorIndicator"/> で消える。</summary>
    private void ShowTextParseErrorIndicator(string msg)
    {
        const string prefix = "  ⚠ パースエラー: ";
        string baseText = lblStatusBar.Text ?? "";
        int markIdx = baseText.IndexOf(prefix, StringComparison.Ordinal);
        if (markIdx >= 0) baseText = baseText.Substring(0, markIdx);
        // メッセージは 1 行に収まる程度に詰める。
        string oneLine = msg.Replace("\r", " ").Replace("\n", " ");
        if (oneLine.Length > 80) oneLine = oneLine.Substring(0, 80) + "…";
        lblStatusBar.Text = baseText + prefix + oneLine;
        lblStatusBar.BackColor = System.Drawing.Color.MistyRose;
        // StatusStrip 本体の背景色も合わせて変える（ToolStripStatusLabel だけ変えると周囲が薄黄のまま浮く）。
        statusStrip.BackColor = System.Drawing.Color.MistyRose;
    }

    /// <summary>パースエラー表記をクリアし、ステータスバー背景色を通常に戻す。</summary>
    private void ClearTextParseErrorIndicator()
    {
        const string prefix = "  ⚠ パースエラー: ";
        string text = lblStatusBar.Text ?? "";
        int markIdx = text.IndexOf(prefix, StringComparison.Ordinal);
        if (markIdx >= 0)
        {
            lblStatusBar.Text = text.Substring(0, markIdx);
        }
        lblStatusBar.BackColor = SystemColors.Info;
        statusStrip.BackColor = SystemColors.Info;
    }

    /// <summary>保存ボタン押下処理。 現在の Draft セッションを <see cref="CreditSaveService.SaveAsync"/> で 1 トランザクション内に DB へ反映し、 成功したらツリーを再構築して背景色を通常状態に戻す。失敗時はロールバックされて Draft はそのまま残るので、 ユーザーは修正してリトライできる。</summary>
    private async Task OnSaveDraftAsync()
    {
        if (_draftSession is null) return;
        try
        {
            // 未保存変更がある状態でのクレジット切替など、別 UI フローと整合する形で即実行する。
            await _saveService.SaveAsync(_draftSession, Environment.UserName);

            // 保存成功 → ツリー再構築（DB の最新値が Draft に既に反映されているはずだが、
            // 安全のため DB から再読み込みする）。
            if (_currentCredit is not null)
            {
                // 話数コピー後の保存では、コピー元の credit_id ではなく
                // CreditSaveService が採番した新 credit_id（_currentCredit.CreditId に書き戻されている）が
                // 既に入っているため、これをそのまま再ロードに使える。
                _draftSession = await _draftLoader.LoadAsync(_currentCredit);
                // Stage 3: 旧 EntryEditorPanel / BlockEditorPanel への SetSession は撤去。
                _lookupCache.SetPendingSession(_draftSession);
                // テキスト編集ペインも保存後の DB 状態で再初期化（Stage 1b）。
                await ReinitializeTextFromDraftAsync();
                await RebuildTreeFromDraftAsync();

                // 話数コピーで新規作成されたクレジットの場合、ListBox の
                int keepId = _currentCredit.CreditId;
                await ReloadCreditsAsync();
                SelectCreditInListBox(keepId);
            }
            // 保存後もプレビューを再描画（DB の最新状態に追従）
            await RefreshPreviewAsync();
            MessageBox.Show(this, "保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>取消ボタン押下処理。 現在の Draft セッションを破棄して DB から再読み込みし、ツリーを最新の DB 状態で描画し直す。</summary>
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
            // 右ペインのエディタに最新の Draft セッション参照を流し込む。
            // EntryEditorPanel が新規 DraftEntry の Temp ID を払い出すために必要。
            // Stage 3: 旧 EntryEditorPanel / BlockEditorPanel への SetSession は撤去。
            _lookupCache.SetPendingSession(_draftSession);
            await RebuildTreeFromDraftAsync();
            // 取消後もプレビューを再描画（DB の最新状態に追従）
            await RefreshPreviewAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>未保存変更ライフサイクルの確認ヘルパ。</summary>
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
    /// フォーム閉じハンドラ。
    /// 未保存変更がある状態でフォームを閉じようとした時に確認ダイアログを出す。
    /// 「保存して閉じる」が選ばれた場合は <see cref="ConfirmUnsavedChangesAsync"/> 内で
    /// 保存処理が走った後に閉じる。
    /// FormClosing は同期コンテキストで呼ばれるため、async タスクを await できない。
    /// そこで「保存処理を await したい」場合は一度 <c>e.Cancel = true</c> で閉じるのを止め、
    /// 別途 async メソッドで保存を走らせ、完了後に <c>_isClosingProgrammatically = true</c> を立てて
    /// 改めて <see cref="Form.Close"/> を呼び直すパターンで対応する。
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
            // ただし FormClosing ハンドラ内（あるいはその async 継続）から直接 Close() を呼ぶと、
            // 元の e.Cancel = true と競合して「Close は走るが直前のキャンセルが残り、結局閉じない」
            // 状態になりやすい（実害：ユーザーが「いいえ」を押しても 1 回では閉じず、もう一度 X を
            // 押す必要がある）。BeginInvoke で次のメッセージループサイクルに繰り越せば、現 FormClosing
            // のスタックが解けた後の単独 Close として処理されるので、1 アクションで確実に閉じる。
            _isClosingProgrammatically = true;
            BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            ShowError(ex);
            _isClosingProgrammatically = false;
        }
    }


    /// <summary>
    /// 主題歌役職ノード <paramref name="roleNode"/> 配下に、<paramref name="episodeId"/> に
    /// 対応する <c>episode_theme_songs</c> 由来の楽曲サブノードを差し込む。
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

        // seq は劇中で流れた順を表す汎用カラム（OP/ED/INSERT を区別せず
        // エピソード単位の劇中順）。CHECK
        // 制約 ck_ets_op_ed_no_insert_seq は撤廃。並び順は ets.seq 単独でソート、
        // kinds パラメータはフィルタとしてのみ使う。同位置に既定行と本放送限定行が
        // あれば既定行（is_broadcast_only=0）を先に。
        // 構造化クレジット解決のため song_id も SELECT。
        string sql = $$"""
            SELECT
              ets.song_recording_id  AS SongRecordingId,
              ets.theme_kind         AS ThemeKind,
              ets.seq                AS Seq,
              ets.is_broadcast_only  AS IsBroadcastOnly,
              s.song_id              AS SongId,
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
              ets.seq,
              ets.is_broadcast_only;
            """;
        await using var conn = await _lookupCache.Factory.CreateOpenedAsync(default).ConfigureAwait(false);
        var rows = (await Dapper.SqlMapper.QueryAsync<ThemeSongRowForTree>(
            conn, sql, new { episodeId, kinds })).ToList();

        // 構造化クレジット（song_credits / song_recording_singers）が
        // 存在する曲・録音は、それを優先表示文字列に展開してフリーテキスト列を上書きする。
        // 動作は ThemeSongsHandler（HTML プレビュー側）と完全に同等で、表示の整合性を保つ。
        // 主題歌は 1 エピソードあたり 2-4 件程度なので、行ごとの追加クエリで実用上問題ない。
        var songCreditsRepo = new SongCreditsRepository(_lookupCache.Factory);
        var recordingSingersRepo = new SongRecordingSingersRepository(_lookupCache.Factory);
        foreach (var r in rows)
        {
            if (r.SongId > 0)
            {
                string lyr = await songCreditsRepo.GetDisplayStringAsync(r.SongId, SongCreditRoles.Lyrics).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(lyr)) r.LyricistName = lyr;

                string cmp = await songCreditsRepo.GetDisplayStringAsync(r.SongId, SongCreditRoles.Composition).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cmp)) r.ComposerName = cmp;

                string arr = await songCreditsRepo.GetDisplayStringAsync(r.SongId, SongCreditRoles.Arrangement).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(arr)) r.ArrangerName = arr;
            }
            if (r.SongRecordingId is int recId && recId > 0)
            {
                // VOCALS 役職を主題歌の歌い手として優先採用（CHORUS の併記は別途）。
                string sing = await recordingSingersRepo.GetDisplayStringAsync(recId, SongRecordingSingerRoles.Vocals).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(sing)) r.SingerName = sing;
            }
        }

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

    /// <summary>役職テンプレ文字列から <c>{THEME_SONGS:columns=N}</c> の N 値を抽出する （ノードラベル注記用、見つからなければ 1）。</summary>
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
        public byte? Seq { get; set; }
        public byte IsBroadcastOnly { get; set; }
        // 構造化クレジット（song_credits）解決のため、
        // 楽曲側のキーである song_id を保持する。
        public int SongId { get; set; }
        public string? SongTitle { get; set; }
        public string? LyricistName { get; set; }
        public string? ComposerName { get; set; }
        public string? ArrangerName { get; set; }
        public string? SingerName { get; set; }
        public string? VariantLabel { get; set; }
    }

    /// <summary>ツリーと右ペインを空にする（クレジット未選択時）。</summary>
    /// <summary>
    /// クレジット未選択時の UI 全リセット。
    /// ツリー・エントリエディタに加えて関連状態もリセットする。これをしないとシリーズ／エピソードを切り替えると
    /// 「ツリーは消えるがプレビューと左下プロパティパネルは旧クレジットのまま」という不整合状態に
    /// なっていた。本メソッドでクレジット選択にぶら下がる派生 UI を漏れなくリセットする。
    /// 呼び出し前提: <c>_currentCredit</c> および <c>_draftSession</c> は既に null 化されていること
    /// （プレビューレンダラが「未選択」HTML を出すため）。
    /// </summary>
    private void ClearTreeAndPreview()
    {
        // ─── ツリー本体 ───
        treeStructure.Nodes.Clear();

        // Stage 3: 旧右ペインの EntryEditor / BlockEditor / NodePropsEditor は撤去。
        // 新 UI ではテキスト編集ペインのみが Draft の入力経路。

        // ─── 左下のクレジットプロパティパネル ───
        rbPresentationCards.Checked = false;
        rbPresentationRoll.Checked = false;
        // PartType は ""（規定位置）アイテムを既定として選択（cboPartType の DataSource 先頭が "" 想定）。
        // SelectedValue 代入は SelectedIndexChanged を発火させる可能性があるが、cboPartType に
        // ハンドラは付いていないので副作用なし。
        if (cboPartType.Items.Count > 0)
        {
            cboPartType.SelectedValue = "";
        }
        txtCreditNotes.Text = string.Empty;

        // ─── HTML ライブプレビュー ───
        // RefreshPreviewAsync は _currentCredit / _draftSession が null のとき「（クレジット未選択）」HTML を
        // セットするので、fire-and-forget で呼べばプレビューがクリアされる。
        // await できない（このメソッドは同期）ので、_ で破棄して非同期実行に任せる。
        _ = RefreshPreviewAsync();

        // ─── ステータスバー ───
        lblStatusBar.Text = "現在編集中: （クレジット未選択）";
        // Stage 3: UpdateButtonStates 呼び出しは撤去（旧右ペイン用ボタン群の Enabled 制御）。
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // 補助型

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
    /// <summary>ツリーノードの種別。 <see cref="Tier"/> と <see cref="Group"/> を追加し、 クレジット → カード → Tier → Group → 役職 → ブロック → エントリ の 7 階層ツリーを表現するようになった。Tier と Group は仮想ノードで、 DB 行を直接持たず、配下の役職の tier / group_in_tier を集約して生成される。</summary>
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

    /// <summary>Tier 仮想ノードのキー（実体テーブル化に対応してリファクタ）。</summary>
    private sealed record TierKey(int CardId, int CardTierId, byte TierNo);

    /// <summary>Group 仮想ノードのキー（実体テーブル化に対応してリファクタ）。</summary>
    private sealed record GroupKey(int CardId, int CardTierId, byte TierNo, int CardGroupId, byte GroupNo);


    // クレジット CRUD（左ペイン）

    /// <summary>
    /// 選択中クレジットを 1 つ上／下へ移動し、明示順序 credit_seq を
    /// 1,2,3,... で再採番して即時 DB 反映する。クレジット階層下位
    /// （カード／役職／ブロック）の ↑↓ 並べ替えと同じ操作感。
    /// 未保存の Draft 変更がある場合は、誤って失わせないよう中断して警告する
    /// （クレジット切替時の確認と同じ方針）。並べ替えは即時 DB 反映系で、
    /// 中央ペインの Draft セッションとは独立。
    /// </summary>
    private async Task OnReorderCreditAsync(bool up)
    {
        try
        {
            if (_suppressCreditSelection) return;
            if (lstCredits.SelectedIndex < 0) return;
            if (lstCredits.DataSource is not List<CreditListItem> items) return;
            if (items.Count < 2) return;

            int idx = lstCredits.SelectedIndex;
            int target = up ? idx - 1 : idx + 1;
            if (target < 0 || target >= items.Count) return;

            // 未保存の Draft 変更があるときは中断（クレジット切替時と同じ配慮）。
            if (_draftSession is not null && _draftSession.HasUnsavedChanges)
            {
                MessageBox.Show(
                    "未保存の編集があります。クレジットの並べ替えを行う前に、" +
                    "中央ペインの「保存」または「取消」で確定してください。",
                    "並べ替えできません",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 表示順のリストを作り、対象 2 件を入れ替える。
            var ordered = items.Select(it => it.Credit).ToList();
            (ordered[idx], ordered[target]) = (ordered[target], ordered[idx]);

            // credit_seq を 1 始まりで全件再採番し、トランザクションで一括更新。
            var updates = ordered
                .Select((c, i) => (creditId: c.CreditId, creditSeq: (ushort)(i + 1)))
                .ToList();
            await _creditsRepo.BulkUpdateSeqAsync(updates);

            // 並べ替え後の位置を選択し直すため、移動先 credit_id を控える。
            int movedCreditId = ordered[target].CreditId;

            await ReloadCreditsAsync();

            // 再読込後のリストから移動した行を選び直す。
            if (lstCredits.DataSource is List<CreditListItem> reloaded)
            {
                int newIdx = reloaded.FindIndex(it => it.Credit.CreditId == movedCreditId);
                if (newIdx >= 0) lstCredits.SelectedIndex = newIdx;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 新規クレジット作成：CreditNewDialog でユーザーに OP/ED ・ presentation・本放送限定フラグ・part_type・notes を入力してもらい、 InsertAsync で INSERT
    /// 、ListBox を再読み込みして新規行を選択。
    /// </summary>
    private async Task OnNewCreditAsync()
    {
        try
        {
            // scope_kind / series_id / episode_id を現在の左ペイン状態から決定
            string scope = _currentScopeKind;
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

    /// <summary>クレジット話数コピー。</summary>
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

            // クレジット ListBox の表示母集合をコピー先エピソードに合わせる処理は、
            // 下の _suppressComboCascade スコープ内（destEpisodeId2 確定後）で実行する。
            // ここでは画面状態をコピー先 Draft に直接切り替える：
            _currentCredit = destEntity;  // CreditId は 0、保存時に採番
            _lastCreditListIndex = -1;    // ListBox との対応は無くなる（保存後の ReloadCreditsAsync で正しく戻る）

            // 右ペインのエディタを新セッション参照に張り替え
            // Stage 3: 旧 EntryEditorPanel / BlockEditorPanel への SetSession は撤去。
            _lookupCache.SetPendingSession(_draftSession);

            // cboSeries / cboEpisode をコピー先の値に合わせて切り替える。
            // SelectedIndexChanged の連鎖発火（→ ReloadCreditsAsync → lstCredits 再構成 → OnCreditSelectedAsync）が
            // 走るとコピー先 Draft が破棄されてしまうので、_suppressComboCascade フラグで連鎖を抑止する。
            // ステータスバーの「現在編集中: エピソード 第N話 ...」表示は cboEpisode.SelectedItem を参照するため、
            // この切替は表示の正確性のために必須。
            if (destEntity.EpisodeId is int destEpisodeId2 && dlg.ResultSeriesId is int destSeriesId)
            {
                _suppressComboCascade = true;
                try
                {
                    // EPISODE スコープに切り替え（_currentScopeKind を直接更新 + UI 同期）
                    _currentScopeKind = "EPISODE";
                    ApplyScopeUi();
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

                    // コピー先エピソードのクレジット一覧を lstCredits に流し込む。
                    // は「簡易実装として呼び出し前のまま」になっており、コピー後に左ペインの
                    // クレジットリストがコピー元エピソードのまま残るバグがあった。
                    // _suppressComboCascade=true 中は SelectedValue 変更による ReloadCreditsAsync 連鎖が
                    // 抑止されているため、同等処理をインラインで実行する。
                    //
                    // 注意: コピー先 Draft はまだ DB 未保存（CreditId=0）なのでこのリストには現れない。
                    // 既存の DB 上の他クレジット（同エピソードの ED 等）が見える形になる。
                    // 保存後の通常 ReloadCreditsAsync で新クレジットが選択可能になる。
                    var destCredits = await _creditsRepo.GetByEpisodeAsync(destEpisodeId2);
                    var sortedDestCredits = destCredits
                        .OrderBy(c => c.CreditSeq)
                        .ThenBy(c => c.CreditId)
                        .ToList();
                    lstCredits.DisplayMember = "Label";
                    lstCredits.ValueMember = "Credit";
                    lstCredits.DataSource = sortedDestCredits
                        .Select((c, i) => new CreditListItem(c, BuildCreditListLabel(c, i + 1)))
                        .ToList();
                    // ListBox との対応はコピー先 Draft (CreditId=0) では取れないため -1 で「未選択」状態にする。
                    _lastCreditListIndex = -1;
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

            // Stage 3: UpdateButtonStates 呼び出しは撤去（旧右ペイン用ボタン群の Enabled 制御）。

            MessageBox.Show(this,
                "コピー先クレジットを Draft として組み立てました。\n"
                + "内容を確認・編集してから「💾 保存」を押してください。\n"
                + "「✖ 取消」を押せば破棄できます。",
                "コピー完了（Draft）", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 埋め込みプレビューペインを再描画する（導入）。
    /// 現在の <see cref="_currentCredit"/> と <see cref="_draftSession"/> の状態に応じて、
    /// (a) Draft セッションが構築済みなら <see cref="CreditPreviewRenderer.RenderDraftAsync"/> で
    ///     Draft をリアルタイム反映した HTML、
    /// (b) Draft 未構築だが <see cref="_currentCredit"/> がある（保存済みクレジット）なら
    ///     <see cref="CreditPreviewRenderer.RenderCreditsAsync"/> で DB から SELECT した HTML、
    /// (c) いずれも無いなら「（クレジット未選択）」表示、
    /// を <see cref="webPreview"/> の <c>DocumentText</c> に流し込む。
    /// 多重実行防止：<see cref="_isRenderingPreview"/> フラグで描画中は即座にスキップする。
    /// 描画関数自体は async でやや時間がかかる可能性（テンプレ展開で SELECT が走る）があるため、
    /// 連打されても最後の 1 回だけ反映されることを意図している。
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
    /// プレビュー再描画を 250ms 後に 1 回だけ実行するよう要求する（デバウンス）。
    /// ツリー再構築・エントリ編集・ブロック編集などのタイミングで連続呼び出されてもまとめて 1 回だけ
    /// 描画される。<see cref="_previewDebounceTimer"/> をリスタートさせるだけのシンプルな実装。
    /// クレジット切替・保存・取消などの「明示的タイミング」では <see cref="RefreshPreviewAsync"/> を
    /// 直接 await して即時反映する方が UX として自然。
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
    /// プロパティ編集系は単一行で完結するため、配下の Card/Tier/...
    /// の Draft とは別系統で「即時 DB 反映」とする方針。これにより「ED を誤って ROLL で作っても
    /// プレゼンテーション形式を後から変更できる」要件を満たす。
    /// IsBroadcastOnly / CreditKind / scope 系は変えない（変える場合は別行への移し替えになる
    /// ため、専用の操作で行うべきという設計判断）。
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
            // 連鎖発火するが、_isReloadingCredits ガードと OnCreditSelectedAsync の直列化キュー
            // （_selectionRequestSerial / _currentSelectionTask）により、最終的に最新の選択 1 回だけが
            // 完走する。中間状態の発火は serial 不一致でスキップされる。
            int keepId = _currentCredit.CreditId;
            await ReloadCreditsAsync();
            SelectCreditInListBox(keepId);
            MessageBox.Show(this, "クレジットプロパティを保存しました。", "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>クレジット削除（論理削除）：SoftDeleteAsync で is_deleted=1 を立てる。</summary>
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

    /// <summary>
    /// 5 ペインのスプリッター位置を、現在のフォーム幅から計算して設定する（Stage 1a で 5 ペイン化）。
    /// 「左 320 / テキスト 560 / プレビュー 720 / ツリー 480 / 警告 320」の方針で初期配置する。
    /// SplitterDistance は各 SplitContainer の Panel1 の幅を表す。<br/>
    /// splitMain     → Panel1 = 左 (320)、     Panel2 = (テキスト + プレビュー + ツリー + 警告)<br/>
    /// splitText     → Panel1 = テキスト (560)、Panel2 = (プレビュー + ツリー + 警告)<br/>
    /// splitPreview  → Panel1 = プレビュー (720)、Panel2 = (ツリー + 警告)<br/>
    /// splitTreeWarn → Panel1 = ツリー (= 残り)、Panel2 = 警告 (320、FixedPanel=Panel2)<br/>
    /// 計算結果が Panel1MinSize / Panel2MinSize の制約に違反する場合は SplitContainer 側で
    /// 自動クランプされるため、本メソッドでは特別な例外処理は行わない。
    /// </summary>
    private void ApplySplitterDistances()
    {
        const int leftWidth     = 320;
        const int textWidth     = 560;
        const int previewWidth  = 720;
        const int warningsWidth = 320;

        try
        {
            if (splitMain.Width > leftWidth + splitMain.Panel2MinSize)
                splitMain.SplitterDistance = leftWidth;
            if (splitText.Width > textWidth + splitText.Panel2MinSize)
                splitText.SplitterDistance = textWidth;
            if (splitPreview.Width > previewWidth + splitPreview.Panel2MinSize)
                splitPreview.SplitterDistance = previewWidth;
            int treeWidth = splitTreeWarn.Width - warningsWidth - splitTreeWarn.SplitterWidth;
            if (treeWidth > splitTreeWarn.Panel1MinSize &&
                treeWidth < splitTreeWarn.Width - splitTreeWarn.Panel2MinSize)
                splitTreeWarn.SplitterDistance = treeWidth;
        }
        catch (InvalidOperationException)
        {
            // 起動直後などサイズ未確定タイミングの保険。次の Resize / Load で再試行される。
        }
    }
}
