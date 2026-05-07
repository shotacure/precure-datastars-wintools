using PrecureDataStars.Catalog.Forms.Drafting;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// クレジット階層の上位ノード（カード／ティア／グループ／役職）の Notes 編集パネル（v1.2.2 追加）。
/// <para>
/// クレジット編集画面の右ペインに、エントリ編集（<see cref="EntryEditorPanel"/>）／ブロック編集
/// （<see cref="BlockEditorPanel"/>）と並ぶ形で配置される。ツリーで Card / Tier / Group / Role
/// ノードが選択されたとき、対応する Draft オブジェクトの <c>Entity.Notes</c> を読み出して
/// 複数行 TextBox に表示し、編集後に「💾 保存」ボタンで Draft 上に書き戻す。
/// </para>
/// <para>
/// 操作対象は <see cref="DraftCard"/> / <see cref="DraftTier"/> / <see cref="DraftGroup"/> /
/// <see cref="DraftRole"/> のいずれか。<see cref="LoadCard"/> / <see cref="LoadTier"/> /
/// <see cref="LoadGroup"/> / <see cref="LoadRoleAsync"/> でそれぞれ呼び分ける。
/// </para>
/// <para>
/// DB への書き込みはクレジット編集画面下部の「💾 保存」ボタンで一括実行される設計のため、
/// 本パネルは「メモリ上の Draft.Entity.Notes を更新 → <see cref="DraftBase.MarkModified"/> を呼ぶ」だけ。
/// 保存完了後は <see cref="NodeSaved"/> イベントで親フォームのツリー再描画をトリガする。
/// </para>
/// </summary>
public sealed partial class NodePropertiesEditorPanel : UserControl
{
    /// <summary>役職名解決等で使用するルックアップキャッシュ（<see cref="Initialize"/> で注入）。</summary>
    private LookupCache? _lookupCache;

    /// <summary>編集対象の Draft オブジェクト。種別判別に <see cref="_currentKind"/> を併用する。</summary>
    private DraftBase? _currentDraft;

    /// <summary>編集対象の種別（<see cref="_currentDraft"/> と整合させて運用）。</summary>
    private TargetKind _currentKind = TargetKind.None;

    /// <summary>
    /// Notes が Draft に反映された後に発火（呼び出し元のツリー再構築をトリガするため）。
    /// <see cref="BlockEditorPanel.BlockSaved"/> と同じく <see cref="Func{Task}"/> 型にすることで、
    /// 購読側のツリー再構築（async）が完了するまで保存処理が return しないようにする
    /// （async void イベントの「画面更新が見えない」現象を回避）。
    /// </summary>
    public Func<Task>? NodeSaved;

    public NodePropertiesEditorPanel()
    {
        InitializeComponent();

        // 初期状態は無効。LoadXxx 呼び出し時に有効化する。
        ClearAndDisable();

        // ボタン結線
        btnSave.Click += async (_, __) => await OnSaveAsync();

        // テキスト変更検知 → 保存ボタン活性
        // パネル表示中に「無効化されたまま」だと使い勝手が悪いので、
        // ロード時の初期テキストとの差分有無で活性化を判定する。
        txtNotes.TextChanged += OnTextChanged;
    }

    /// <summary>
    /// 親フォームから依存性を流し込む。
    /// <para>
    /// 役職ノードの表示名解決（<c>roles.name_ja</c> 引き当て）に <see cref="LookupCache"/> を使用する。
    /// </para>
    /// </summary>
    internal void Initialize(LookupCache lookupCache)
    {
        _lookupCache = lookupCache ?? throw new ArgumentNullException(nameof(lookupCache));
    }

    /// <summary>編集対象を未設定にしてパネル全体を無効化する。</summary>
    public void ClearAndDisable()
    {
        _currentDraft = null;
        _currentKind = TargetKind.None;
        lblNodeHeader.Text = "選択中: (なし)";
        lblNodeKindNote.Text = "Card / Tier / Group / Role を選択すると備考を編集できます。";
        txtNotes.Text = "";
        txtNotes.Enabled = false;
        btnSave.Enabled = false;
    }

    /// <summary>カード編集モードでロードする。</summary>
    public void LoadCard(DraftCard card)
    {
        if (card is null) throw new ArgumentNullException(nameof(card));

        _currentDraft = card;
        _currentKind = TargetKind.Card;

        // 表示順 (card_seq) は 1 始まり。同じカードでも再描画のたびに seq が詰められうるため、
        // 表示は entity の card_seq を素直に使う。Notes が空でも当該カードを編集対象として表示する。
        lblNodeHeader.Text = $"選択中: カード #{card.Entity.CardSeq}";
        lblNodeKindNote.Text = "カード備考は credit_cards.notes に保存されます。";
        SetNotesText(card.Entity.Notes);
        txtNotes.Enabled = true;
        // 初期ロード直後は変更なしなので保存ボタンは無効。
        btnSave.Enabled = false;
    }

    /// <summary>ティア編集モードでロードする。</summary>
    public void LoadTier(DraftTier tier)
    {
        if (tier is null) throw new ArgumentNullException(nameof(tier));

        _currentDraft = tier;
        _currentKind = TargetKind.Tier;

        lblNodeHeader.Text = $"選択中: ティア #{tier.Entity.TierNo}";
        lblNodeKindNote.Text = "ティア備考は credit_card_tiers.notes に保存されます。";
        SetNotesText(tier.Entity.Notes);
        txtNotes.Enabled = true;
        btnSave.Enabled = false;
    }

    /// <summary>グループ編集モードでロードする。</summary>
    public void LoadGroup(DraftGroup group)
    {
        if (group is null) throw new ArgumentNullException(nameof(group));

        _currentDraft = group;
        _currentKind = TargetKind.Group;

        lblNodeHeader.Text = $"選択中: グループ #{group.Entity.GroupNo}";
        lblNodeKindNote.Text = "グループ備考は credit_card_groups.notes に保存されます。";
        SetNotesText(group.Entity.Notes);
        txtNotes.Enabled = true;
        btnSave.Enabled = false;
    }

    /// <summary>
    /// 役職編集モードでロードする（役職表示名のためにマスタ参照が必要なので非同期）。
    /// </summary>
    public async Task LoadRoleAsync(DraftRole role)
    {
        if (role is null) throw new ArgumentNullException(nameof(role));

        _currentDraft = role;
        _currentKind = TargetKind.Role;

        // 役職コードから日本語表示名を引き当ててヘッダ表示に使う。
        // LookupCache 未注入の場合は code 直書きにフォールバック。
        // CreditCardRole.RoleCode は「自由記述ロール」用に NULL 許容なので、
        // null の場合は「(自由記述)」というラベルにフォールバックする。
        string nameJa = role.Entity.RoleCode ?? "(自由記述)";
        if (_lookupCache is not null)
        {
            string? resolved = await _lookupCache.LookupRoleNameJaAsync(role.Entity.RoleCode).ConfigureAwait(true);
            if (!string.IsNullOrEmpty(resolved)) nameJa = resolved;
        }

        lblNodeHeader.Text = $"選択中: 役職「{nameJa}」 (order={role.Entity.OrderInGroup})";
        lblNodeKindNote.Text = "役職備考は credit_card_roles.notes に保存されます。";
        SetNotesText(role.Entity.Notes);
        txtNotes.Enabled = true;
        btnSave.Enabled = false;
    }

    /// <summary>
    /// TextBox に Notes 値をセットする内部ヘルパ。
    /// null や空文字を区別せずに「空のテキストボックス」として表示し、
    /// TextChanged イベントの誤発火を抑えるため <see cref="_loadingFromDraft"/> フラグで保護する。
    /// </summary>
    private void SetNotesText(string? notes)
    {
        _loadingFromDraft = true;
        try
        {
            txtNotes.Text = notes ?? "";
            // 編集の起点として保存値をスナップショットしておく（差分検知に使用）。
            _initialNotes = txtNotes.Text;
        }
        finally
        {
            _loadingFromDraft = false;
        }
    }

    /// <summary>ロード処理中の TextChanged を抑制するフラグ。</summary>
    private bool _loadingFromDraft;

    /// <summary>ロード時の初期テキスト（差分検知に使用）。</summary>
    private string _initialNotes = "";

    /// <summary>
    /// TextBox 変更時：差分があれば保存ボタンを活性化、無ければ無効化。
    /// 差分判定はトリム後ではなく素のまま比較する（末尾改行や空白も「変更」とみなす）。
    /// </summary>
    private void OnTextChanged(object? sender, EventArgs e)
    {
        if (_loadingFromDraft) return;
        if (_currentDraft is null) return;

        bool changed = !string.Equals(txtNotes.Text, _initialNotes, StringComparison.Ordinal);
        btnSave.Enabled = changed;
    }

    /// <summary>
    /// 保存ボタン押下：現在の TextBox 値を Draft.Entity.Notes に書き戻し、
    /// <see cref="DraftBase.MarkModified"/> を呼んだ上で <see cref="NodeSaved"/> を発火する。
    /// </summary>
    private async Task OnSaveAsync()
    {
        if (_currentDraft is null || _currentKind == TargetKind.None) return;

        try
        {
            // 空文字は null として保存する（DB スキーマ上、空文字と NULL を区別しない運用）。
            string? newValue = string.IsNullOrEmpty(txtNotes.Text) ? null : txtNotes.Text;

            // 種別ごとに対応する Entity プロパティに書き込む。
            // 値が変わっていなければ MarkModified しない（既存の Modified を維持）。
            switch (_currentKind)
            {
                case TargetKind.Card:
                {
                    var card = (DraftCard)_currentDraft;
                    if (!string.Equals(card.Entity.Notes, newValue, StringComparison.Ordinal))
                    {
                        card.Entity.Notes = newValue;
                        card.MarkModified();
                    }
                    break;
                }
                case TargetKind.Tier:
                {
                    var tier = (DraftTier)_currentDraft;
                    if (!string.Equals(tier.Entity.Notes, newValue, StringComparison.Ordinal))
                    {
                        tier.Entity.Notes = newValue;
                        tier.MarkModified();
                    }
                    break;
                }
                case TargetKind.Group:
                {
                    var group = (DraftGroup)_currentDraft;
                    if (!string.Equals(group.Entity.Notes, newValue, StringComparison.Ordinal))
                    {
                        group.Entity.Notes = newValue;
                        group.MarkModified();
                    }
                    break;
                }
                case TargetKind.Role:
                {
                    var role = (DraftRole)_currentDraft;
                    if (!string.Equals(role.Entity.Notes, newValue, StringComparison.Ordinal))
                    {
                        role.Entity.Notes = newValue;
                        role.MarkModified();
                    }
                    break;
                }
            }

            // ロード時のスナップショットを更新（連続編集に備える）。
            _initialNotes = txtNotes.Text;
            btnSave.Enabled = false;

            // 親フォーム側のツリー再描画。await することで「保存後に画面が遅れて更新される」現象を防ぐ。
            if (NodeSaved is not null)
            {
                await NodeSaved.Invoke().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "備考保存エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// 編集対象の種別。<see cref="_currentDraft"/> の実型と組み合わせて使用する。
    /// </summary>
    private enum TargetKind
    {
        /// <summary>未設定（パネル無効化中）。</summary>
        None,
        /// <summary><see cref="DraftCard"/>。</summary>
        Card,
        /// <summary><see cref="DraftTier"/>。</summary>
        Tier,
        /// <summary><see cref="DraftGroup"/>。</summary>
        Group,
        /// <summary><see cref="DraftRole"/>。</summary>
        Role,
    }
}
