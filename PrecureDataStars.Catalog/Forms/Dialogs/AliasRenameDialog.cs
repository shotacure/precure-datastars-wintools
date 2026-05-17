using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 名寄せ「名義の改名」ダイアログ。
/// <para>
/// 既存の名義を新しい表記に「改名」する。種別ごとに動作が異なる:
/// <list type="bullet">
///   <item><description><b>人物名義 / 企業屋号</b>: 旧 alias は表記そのまま履歴として残し、
///     新しい name / name_kana を持つ <b>新 alias を INSERT</b> し、旧↔新を
///     <c>predecessor_alias_id</c> / <c>successor_alias_id</c> でリンクする。
///     人物の場合は中間表 person_alias_persons の紐付けも新 alias にコピーされる。</description></item>
///   <item><description><b>キャラ名義</b>: character_aliases に predecessor/successor 列が無いため、
///     <b>現 alias の name / name_kana を上書き</b> するシンプル方式。</description></item>
/// </list>
/// </para>
/// <para>
/// 「親同期」オプションを ON にすると、親本体の表示名（<c>persons.full_name</c> /
/// <c>companies.name</c> / <c>characters.name</c>）も新表記で上書きする。
/// 人物の場合は単独名義（中間表 1 行）のときのみ親同期が有効。
/// </para>
/// </summary>
public sealed partial class AliasRenameDialog : Form
{
    /// <summary>名寄せ対象の種別。</summary>
    public enum AliasKind
    {
        /// <summary>人物名義（person_aliases）。新 alias 作成 + predecessor/successor リンク。</summary>
        Person,

        /// <summary>企業屋号（company_aliases）。新 alias 作成 + predecessor/successor リンク。</summary>
        Company,

        /// <summary>キャラ名義（character_aliases）。現 alias の name 上書き。</summary>
        Character
    }

    private readonly AliasKind _kind;
    private readonly int _aliasId;
    private readonly string _oldAliasName;
    private readonly string? _oldAliasNameKana;

    private readonly PersonAliasesRepository? _personAliasesRepo;
    private readonly CompanyAliasesRepository? _companyAliasesRepo;
    private readonly CharacterAliasesRepository? _characterAliasesRepo;

    /// <summary>
    /// 改名後の新 alias_id（人物・企業の場合は新規作成された alias_id、キャラの場合は元の alias_id）。
    /// 操作成功時のみ値あり。呼び出し側は UI 上の選択を新 alias に切り替える際に使う。
    /// </summary>
    public int? ResultAliasId { get; private set; }

    /// <summary>本ダイアログでの操作が成功したかどうか。</summary>
    public bool Renamed { get; private set; }

    // ─────────────────────────────────────────────────────────
    //  コンストラクタ（種別別に 3 つ）
    // ─────────────────────────────────────────────────────────

    /// <summary>人物名義の改名用コンストラクタ。</summary>
    public AliasRenameDialog(
        int aliasId, string oldAliasName, string? oldAliasNameKana,
        PersonAliasesRepository personAliasesRepo)
    {
        _kind = AliasKind.Person;
        _aliasId = aliasId;
        _oldAliasName = oldAliasName ?? "";
        _oldAliasNameKana = oldAliasNameKana;
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        InitializeComponent();
        BindLabels();
    }

    /// <summary>企業屋号の改名用コンストラクタ。</summary>
    public AliasRenameDialog(
        int aliasId, string oldAliasName, string? oldAliasNameKana,
        CompanyAliasesRepository companyAliasesRepo)
    {
        _kind = AliasKind.Company;
        _aliasId = aliasId;
        _oldAliasName = oldAliasName ?? "";
        _oldAliasNameKana = oldAliasNameKana;
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        InitializeComponent();
        BindLabels();
    }

    /// <summary>キャラ名義の改名用コンストラクタ。</summary>
    public AliasRenameDialog(
        int aliasId, string oldAliasName, string? oldAliasNameKana,
        CharacterAliasesRepository characterAliasesRepo)
    {
        _kind = AliasKind.Character;
        _aliasId = aliasId;
        _oldAliasName = oldAliasName ?? "";
        _oldAliasNameKana = oldAliasNameKana;
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        InitializeComponent();
        BindLabels();
    }

    /// <summary>初期表示：種別に応じてラベル・説明文を切り替え、入力欄に旧表記をプリセット。</summary>
    private void BindLabels()
    {
        string kindLabel = _kind switch
        {
            AliasKind.Person => "人物名義",
            AliasKind.Company => "企業屋号",
            AliasKind.Character => "キャラ名義",
            _ => "名義"
        };
        string parentLabel = _kind switch
        {
            AliasKind.Person => "人物",
            AliasKind.Company => "企業",
            AliasKind.Character => "キャラクター",
            _ => "親"
        };

        Text = $"{kindLabel}の改名";
        lblTitle.Text = $"{kindLabel}「{_oldAliasName}」(alias_id={_aliasId}) を改名します。";
        lblOldName.Text = $"旧 name: {_oldAliasName}";
        lblOldNameKana.Text = $"旧 name_kana: {_oldAliasNameKana ?? "(なし)"}";

        // 入力欄のプリセット（編集して新表記にしてもらう）
        txtNewName.Text = _oldAliasName;
        txtNewNameKana.Text = _oldAliasNameKana ?? "";

        chkSyncParent.Text = $"{parentLabel}本体の表示名も新表記で上書きする";

        // 種別ごとの注記
        lblWarning.Text = _kind switch
        {
            AliasKind.Person =>
                "※ 人物名義は新しい alias 行を作成し、旧 alias とは predecessor/successor で自動リンクされます（旧 alias は履歴として残ります）。\r\n"
              + "※ 共同名義（複数人で 1 名義）の場合、親同期はスキップされます。",
            AliasKind.Company =>
                "※ 企業屋号は新しい alias 行を作成し、旧 alias とは predecessor/successor で自動リンクされます（旧 alias は履歴として残ります）。",
            AliasKind.Character =>
                "※ キャラ名義は現 alias の表記をそのまま上書きします（履歴は残りません）。\r\n"
              + "※ 別表記を並存させたい場合はキャラ管理から alias を追加してください。",
            _ => ""
        };
    }

    /// <summary>OK ボタン：必須チェック → 種別に応じたリポジトリの <c>RenameAsync</c> を呼ぶ。</summary>
    private async void OnOkClick(object? sender, EventArgs e)
    {
        string newName = (txtNewName.Text ?? "").Trim();
        string? newNameKana = string.IsNullOrWhiteSpace(txtNewNameKana.Text) ? null : txtNewNameKana.Text.Trim();
        bool syncParent = chkSyncParent.Checked;

        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(this, "新しい name は必須です。", "入力エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtNewName.Focus();
            return;
        }

        // 旧表記と完全一致 = 改名しても何も起きない。誤操作防止のため確認だけ取って続行する
        // （もしユーザーが「親同期だけ走らせたい」場合に活用できるため、強制ブロックはしない）。
        if (string.Equals(newName, _oldAliasName, StringComparison.Ordinal)
            && string.Equals(newNameKana ?? "", _oldAliasNameKana ?? "", StringComparison.Ordinal)
            && !syncParent)
        {
            var msg = MessageBox.Show(this,
                "新しい表記が旧表記と同一で、親同期も無効です。何も変更されませんが続行しますか？",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (msg != DialogResult.OK) return;
        }

        try
        {
            string? user = Environment.UserName;
            switch (_kind)
            {
                case AliasKind.Person:
                {
                    int newAliasId = await _personAliasesRepo!.RenameAsync(
                        _aliasId, newName, newNameKana, syncParent, user).ConfigureAwait(true);
                    ResultAliasId = newAliasId;
                    break;
                }
                case AliasKind.Company:
                {
                    int newAliasId = await _companyAliasesRepo!.RenameAsync(
                        _aliasId, newName, newNameKana, syncParent, user).ConfigureAwait(true);
                    ResultAliasId = newAliasId;
                    break;
                }
                case AliasKind.Character:
                {
                    // キャラ名義は現 alias 上書き。RenameAsync は void 系なので結果 alias_id は元と同じ。
                    await _characterAliasesRepo!.RenameAsync(
                        _aliasId, newName, newNameKana, syncParent, user).ConfigureAwait(true);
                    ResultAliasId = _aliasId;
                    break;
                }
            }

            Renamed = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "改名エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}