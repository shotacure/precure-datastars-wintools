using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// キャラクター名義の入力収集ダイアログ。
/// ステージD で「保存ボタンまで DB に書かない」原則に揃えるため、ダイアログ自身は DB に
/// 一切書き込まない。OK で閉じたとき、入力値と選択モードが公開プロパティに保持されるので、
/// 呼び出し側が <c>CreditDraftSession.PendingCharacterAliases</c> に積む。
/// 2 つのモードを切替で扱う:
/// <list type="bullet">
///   <item><description>
///     モード A：既存のキャラクターに名義だけ追加する。親キャラを <see cref="CharacterPickerDialog"/> で選び、
///     <see cref="ResultAttachToExistingCharacterId"/> にその character_id を入れる。
///   </description></item>
///   <item><description>
///     モード B：キャラクターごと新規作成する。
///     <see cref="ResultCharacterName"/> + <see cref="ResultCharacterKindCode"/> を埋めて返す。
///     キャラクター区分は character_kinds マスタから引いてコンボに流し込む。
///   </description></item>
/// </list>
/// </summary>
public partial class QuickAddCharacterAliasDialog : Form
{
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterKindsRepository _characterKindsRepo;

    /// <summary>モード A で選択された親キャラ ID（null の場合は未選択）。</summary>
    private int? _pickedCharacterId;

    /// <summary>OK 確定時、モード A（既存キャラに alias 追加）なら親 character_id。
    /// モード B（新規キャラ）なら null。</summary>
    public int? ResultAttachToExistingCharacterId { get; private set; }

    /// <summary>OK 確定時、入力された alias 名（必須）。</summary>
    public string ResultAliasName { get; private set; } = "";

    /// <summary>OK 確定時、入力された alias かな。空なら null。</summary>
    public string? ResultAliasKana { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規キャラの characters.name。
    /// モード A（attach）の場合は null。</summary>
    public string? ResultCharacterName { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規キャラの characters.name_kana。</summary>
    public string? ResultCharacterNameKana { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規キャラの characters.character_kind。</summary>
    public string? ResultCharacterKindCode { get; private set; }

    /// <summary>モード B 専用：OK 確定時、新規キャラの characters.notes。</summary>
    public string? ResultCharacterNotes { get; private set; }

    public QuickAddCharacterAliasDialog(
        CharactersRepository charactersRepo,
        CharacterKindsRepository characterKindsRepo)
    {
        _charactersRepo     = charactersRepo     ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterKindsRepo = characterKindsRepo ?? throw new ArgumentNullException(nameof(characterKindsRepo));
        InitializeComponent();

        rbModeExisting.CheckedChanged       += (_, __) => UpdateMode();
        rbModeNewCharacter.CheckedChanged   += (_, __) => UpdateMode();
        btnPickParentCharacter.Click        += async (_, __) => await OnPickParentCharacterAsync();
        btnOk.Click                         += (_, __) => OnOk();
        Load                                += async (_, __) => await OnLoadAsync();

        UpdateMode();
    }

    /// <summary>初回ロード：character_kinds マスタからコンボに流し込む。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var kinds = await _characterKindsRepo.GetAllAsync();
            // IReadOnlyList<T> は ConvertAll を持たないので System.Linq の Select で射影する。
            // KindItem は表示文字列とコードを保持するだけのアダプタ。
            cboCharacterKind.DataSource = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Select(kinds,
                    k => new KindItem(k.CharacterKindCode, $"{k.CharacterKindCode}  — {k.NameJa}")));
            cboCharacterKind.DisplayMember = nameof(KindItem.Display);
            cboCharacterKind.ValueMember   = nameof(KindItem.Code);
            if (cboCharacterKind.Items.Count > 0) cboCharacterKind.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "区分マスタ取得エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>モード切替に応じて 2 つの Panel の Visible を入れ替える。</summary>
    private void UpdateMode()
    {
        pnlExisting.Visible      = rbModeExisting.Checked;
        pnlNewCharacter.Visible  = rbModeNewCharacter.Checked;
    }

    /// <summary>「選択...」ボタン押下：CharacterPickerDialog を開いて親キャラを選ぶ。</summary>
    private async Task OnPickParentCharacterAsync()
    {
        try
        {
            using var picker = new CharacterPickerDialog(_charactersRepo);
            if (picker.ShowDialog(this) != DialogResult.OK || !picker.SelectedId.HasValue) return;
            _pickedCharacterId = picker.SelectedId.Value;
            // 表示用にキャラ名を引いて見せる
            var c = await _charactersRepo.GetByIdAsync(_pickedCharacterId.Value);
            lblParentCharacterValue.Text = c is null
                ? $"character_id={_pickedCharacterId} (未解決)"
                : $"#{c.CharacterId}  {c.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>OK ボタン処理：選択モードに応じて入力値を Result プロパティに格納してダイアログを閉じる。
    /// DB 投入は行わない。</summary>
    private void OnOk()
    {
        if (rbModeExisting.Checked)
        {
            if (_pickedCharacterId is null)
            {
                MessageBox.Show(this, "親キャラクターを選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string aliasName = (txtExistingAliasName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(aliasName))
            {
                MessageBox.Show(this, "名義名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtExistingAliasName.Focus();
                return;
            }

            ResultAttachToExistingCharacterId = _pickedCharacterId.Value;
            ResultAliasName = aliasName;
            ResultAliasKana = string.IsNullOrWhiteSpace(txtExistingAliasKana.Text) ? null : txtExistingAliasKana.Text.Trim();
        }
        else
        {
            string charName = (txtNewCharacterName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(charName))
            {
                MessageBox.Show(this, "キャラ名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtNewCharacterName.Focus();
                return;
            }
            if (cboCharacterKind.SelectedItem is not KindItem kindItem)
            {
                MessageBox.Show(this, "区分を選択してください。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResultAttachToExistingCharacterId = null;
            ResultAliasName = charName;
            ResultAliasKana = string.IsNullOrWhiteSpace(txtNewCharacterKana.Text) ? null : txtNewCharacterKana.Text.Trim();
            ResultCharacterName     = charName;
            ResultCharacterNameKana = ResultAliasKana;
            ResultCharacterKindCode = kindItem.Code;
            ResultCharacterNotes    = string.IsNullOrWhiteSpace(txtNewCharacterNotes.Text) ? null : txtNewCharacterNotes.Text.Trim();
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>区分コンボ用の表示アイテム（コード + 表示文字列）。</summary>
    private sealed class KindItem
    {
        public string Code { get; }
        public string Display { get; }
        public KindItem(string code, string display) { Code = code; Display = display; }
        public override string ToString() => Display;
    }
}
