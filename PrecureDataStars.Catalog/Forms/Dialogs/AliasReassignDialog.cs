using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 名寄せ「名義の付け替え」ダイアログ（v1.2.1 追加）。
/// <para>
/// 既存の名義（人物名義 / 企業屋号 / キャラ名義）を別の親（人物 / 企業 / キャラ）に紐付け直す。
/// 親本体の表示名（<c>persons.full_name</c> / <c>companies.name</c> / <c>characters.name</c>）には
/// 一切手を加えず、結合だけを動かすシンプル版。改名（親の表示名も合わせて上書き）が必要な場合は
/// <see cref="AliasRenameDialog"/> を使う。
/// </para>
/// <para>
/// 切り離されて孤立した（=有効な alias を 1 つも持たなくなった）旧親は、
/// 各リポジトリの <c>ReassignTo*Async</c> メソッド側で自動論理削除される
/// （<see cref="PersonAliasesRepository.ReassignToPersonAsync"/> /
/// <see cref="CompanyAliasesRepository.ReassignToCompanyAsync"/> /
/// <see cref="CharacterAliasesRepository.ReassignToCharacterAsync"/>）。
/// </para>
/// <para>
/// 本ダイアログは 3 種別を一つの実装にまとめるため、種別判定は <see cref="AliasKind"/> の
/// switch で行う。各種別ごとに必要なリポジトリだけが non-null となるコンストラクタを 3 つ持つ。
/// </para>
/// </summary>
public sealed partial class AliasReassignDialog : Form
{
    /// <summary>名寄せ対象の種別（v1.2.1）。</summary>
    public enum AliasKind
    {
        /// <summary>人物名義（person_aliases）。中間表 person_alias_persons を介して人物に紐付く。</summary>
        Person,

        /// <summary>企業屋号（company_aliases）。company_id 列で企業に直接紐付く。</summary>
        Company,

        /// <summary>キャラクター名義（character_aliases）。character_id 列でキャラに直接紐付く。</summary>
        Character
    }

    private readonly AliasKind _kind;
    private readonly int _aliasId;
    private readonly string _aliasName;
    private readonly string _currentParentLabel;

    // ── 種別ごとに必要なリポジトリだけが non-null になる ──
    // 直接 alias 操作用
    private readonly PersonAliasesRepository? _personAliasesRepo;
    private readonly CompanyAliasesRepository? _companyAliasesRepo;
    private readonly CharacterAliasesRepository? _characterAliasesRepo;

    // ピッカー用
    private readonly PersonsRepository? _personsRepo;
    private readonly CompaniesRepository? _companiesRepo;
    private readonly CharactersRepository? _charactersRepo;

    /// <summary>選択された新しい親の ID（OK 確定後）。</summary>
    private int? _pickedNewParentId;

    /// <summary>選択された新しい親の表示用ラベル（OK 確定後）。</summary>
    private string? _pickedNewParentLabel;

    /// <summary>本ダイアログでの操作が成功したかどうか。</summary>
    public bool Reassigned { get; private set; }

    // ─────────────────────────────────────────────────────────
    //  コンストラクタ（種別別に 3 つ）
    // ─────────────────────────────────────────────────────────

    /// <summary>人物名義の付け替え用コンストラクタ。</summary>
    public AliasReassignDialog(
        int aliasId, string aliasName, string currentParentLabel,
        PersonAliasesRepository personAliasesRepo,
        PersonsRepository personsRepo)
    {
        _kind = AliasKind.Person;
        _aliasId = aliasId;
        _aliasName = aliasName ?? "";
        _currentParentLabel = currentParentLabel ?? "";
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        InitializeComponent();
        BindLabels();
    }

    /// <summary>企業屋号の付け替え用コンストラクタ。</summary>
    public AliasReassignDialog(
        int aliasId, string aliasName, string currentParentLabel,
        CompanyAliasesRepository companyAliasesRepo,
        CompaniesRepository companiesRepo)
    {
        _kind = AliasKind.Company;
        _aliasId = aliasId;
        _aliasName = aliasName ?? "";
        _currentParentLabel = currentParentLabel ?? "";
        _companyAliasesRepo = companyAliasesRepo ?? throw new ArgumentNullException(nameof(companyAliasesRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        InitializeComponent();
        BindLabels();
    }

    /// <summary>キャラ名義の付け替え用コンストラクタ。</summary>
    public AliasReassignDialog(
        int aliasId, string aliasName, string currentParentLabel,
        CharacterAliasesRepository characterAliasesRepo,
        CharactersRepository charactersRepo)
    {
        _kind = AliasKind.Character;
        _aliasId = aliasId;
        _aliasName = aliasName ?? "";
        _currentParentLabel = currentParentLabel ?? "";
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _charactersRepo = charactersRepo ?? throw new ArgumentNullException(nameof(charactersRepo));
        InitializeComponent();
        BindLabels();
    }

    /// <summary>ラベルへ初期値をバインドし、種別に応じてタイトル・説明文を切り替える。</summary>
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

        Text = $"{kindLabel}の付け替え";
        lblTitle.Text = $"{kindLabel}「{_aliasName}」(alias_id={_aliasId}) を別の{parentLabel}に紐付け直します。";
        lblCurrentParent.Text = $"現在の{parentLabel}: {_currentParentLabel}";
        lblPickHint.Text = $"新しい{parentLabel}を選んでください:";
        btnPickParent.Text = $"{parentLabel}を検索...";
        lblNewParent.Text = "(未選択)";

        // 操作の意図と副作用を表示
        lblWarning.Text =
            $"※ {parentLabel}本体の表示名は変更されません。\r\n"
            + $"※ 切り離されて他の名義を持たなくなった{parentLabel}は自動的に論理削除されます。\r\n"
            + (_kind == AliasKind.Person
                ? "※ 共同名義（複数人で 1 名義）は単独名義に集約されます。"
                : "");

        btnOk.Enabled = false;
    }

    /// <summary>「親を検索」ボタン押下：種別に応じたピッカーを開いて新しい親を選択させる。</summary>
    private void OnPickParentClick(object? sender, EventArgs e)
    {
        switch (_kind)
        {
            case AliasKind.Person:
            {
                using var dlg = new PersonPickerDialog(_personsRepo!);
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
                {
                    _pickedNewParentId = dlg.SelectedId;
                    // ピッカーは ID しか返さないので、リポジトリから 1 件引いて表示用ラベルを作る
                    _ = LoadPickedPersonAsync(dlg.SelectedId.Value);
                }
                break;
            }
            case AliasKind.Company:
            {
                using var dlg = new CompanyPickerDialog(_companiesRepo!);
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
                {
                    _pickedNewParentId = dlg.SelectedId;
                    _ = LoadPickedCompanyAsync(dlg.SelectedId.Value);
                }
                break;
            }
            case AliasKind.Character:
            {
                using var dlg = new CharacterPickerDialog(_charactersRepo!);
                if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedId.HasValue)
                {
                    _pickedNewParentId = dlg.SelectedId;
                    _ = LoadPickedCharacterAsync(dlg.SelectedId.Value);
                }
                break;
            }
        }
    }

    /// <summary>新親候補（人物）の表示ラベルをロードする。</summary>
    private async Task LoadPickedPersonAsync(int personId)
    {
        try
        {
            var p = await _personsRepo!.GetByIdAsync(personId).ConfigureAwait(true);
            _pickedNewParentLabel = p is null
                ? $"person_id={personId}"
                : $"{p.FullName} (person_id={personId})";
            lblNewParent.Text = _pickedNewParentLabel;
            btnOk.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "取得エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>新親候補（企業）の表示ラベルをロードする。</summary>
    private async Task LoadPickedCompanyAsync(int companyId)
    {
        try
        {
            var c = await _companiesRepo!.GetByIdAsync(companyId).ConfigureAwait(true);
            _pickedNewParentLabel = c is null
                ? $"company_id={companyId}"
                : $"{c.Name} (company_id={companyId})";
            lblNewParent.Text = _pickedNewParentLabel;
            btnOk.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "取得エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>新親候補（キャラ）の表示ラベルをロードする。</summary>
    private async Task LoadPickedCharacterAsync(int characterId)
    {
        try
        {
            var c = await _charactersRepo!.GetByIdAsync(characterId).ConfigureAwait(true);
            _pickedNewParentLabel = c is null
                ? $"character_id={characterId}"
                : $"{c.Name} (character_id={characterId})";
            lblNewParent.Text = _pickedNewParentLabel;
            btnOk.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "取得エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>OK ボタン：種別ごとに <c>ReassignTo*Async</c> を呼んで付け替えをコミットする。</summary>
    private async void OnOkClick(object? sender, EventArgs e)
    {
        if (!_pickedNewParentId.HasValue)
        {
            MessageBox.Show(this, "新しい紐付け先を選んでください。", "未選択",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            string? user = Environment.UserName;
            switch (_kind)
            {
                case AliasKind.Person:
                    await _personAliasesRepo!.ReassignToPersonAsync(
                        _aliasId, _pickedNewParentId.Value, user).ConfigureAwait(true);
                    break;
                case AliasKind.Company:
                    await _companyAliasesRepo!.ReassignToCompanyAsync(
                        _aliasId, _pickedNewParentId.Value, user).ConfigureAwait(true);
                    break;
                case AliasKind.Character:
                    await _characterAliasesRepo!.ReassignToCharacterAsync(
                        _aliasId, _pickedNewParentId.Value, user).ConfigureAwait(true);
                    break;
            }

            Reassigned = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "付け替えエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
