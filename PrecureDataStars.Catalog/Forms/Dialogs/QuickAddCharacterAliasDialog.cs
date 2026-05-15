using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// キャラクター名義の即時追加ダイアログ。
/// <para>
/// 2 つのモードを切替で扱う:
/// <list type="bullet">
///   <item><description>
///     モード A：既存のキャラクターに名義だけ追加する。親キャラを <see cref="CharacterPickerDialog"/> で選び、
///     <see cref="CharacterAliasesRepository.InsertAsync"/> を 1 回呼ぶだけ。
///   </description></item>
///   <item><description>
///     モード B：キャラクターごと新規作成する。<see cref="CharactersRepository.QuickAddWithSingleAliasAsync"/>
///     で characters + character_aliases を 1 トランザクションで投入する。
///     キャラクター区分は character_kinds マスタから引いてコンボに流し込む。
///   </description></item>
/// </list>
/// </para>
/// <para>
/// OK 完了後、新規 character_aliases.alias_id が <see cref="SelectedAliasId"/> にセットされる。
/// </para>
/// </summary>
public partial class QuickAddCharacterAliasDialog : Form
{
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly CharacterKindsRepository _characterKindsRepo;

    /// <summary>モード A で選択された親キャラ ID（null の場合は未選択）。</summary>
    private int? _pickedCharacterId;

    /// <summary>登録成功時の新規 character_aliases.alias_id。キャンセル時は null。</summary>
    public int? SelectedAliasId { get; private set; }

    public QuickAddCharacterAliasDialog(
        CharactersRepository charactersRepo,
        CharacterAliasesRepository characterAliasesRepo,
        CharacterKindsRepository characterKindsRepo)
    {
        _charactersRepo       = charactersRepo       ?? throw new ArgumentNullException(nameof(charactersRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _characterKindsRepo   = characterKindsRepo   ?? throw new ArgumentNullException(nameof(characterKindsRepo));
        InitializeComponent();

        rbModeExisting.CheckedChanged       += (_, __) => UpdateMode();
        rbModeNewCharacter.CheckedChanged   += (_, __) => UpdateMode();
        btnPickParentCharacter.Click        += async (_, __) => await OnPickParentCharacterAsync();
        btnOk.Click                         += async (_, __) => await OnOkAsync();
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

    /// <summary>登録ボタン処理：選択モードに応じて適切なリポジトリ呼び出しを実行。</summary>
    private async Task OnOkAsync()
    {
        try
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

                int aliasId = await _characterAliasesRepo.InsertAsync(new CharacterAlias
                {
                    CharacterId = _pickedCharacterId.Value,
                    Name = aliasName,
                    NameKana = string.IsNullOrWhiteSpace(txtExistingAliasKana.Text) ? null : txtExistingAliasKana.Text.Trim(),
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                });
                SelectedAliasId = aliasId;
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

                int aliasId = await _charactersRepo.QuickAddWithSingleAliasAsync(
                    charName,
                    string.IsNullOrWhiteSpace(txtNewCharacterKana.Text) ? null : txtNewCharacterKana.Text.Trim(),
                    kindItem.Code,
                    string.IsNullOrWhiteSpace(txtNewCharacterNotes.Text) ? null : txtNewCharacterNotes.Text.Trim(),
                    Environment.UserName);
                SelectedAliasId = aliasId;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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