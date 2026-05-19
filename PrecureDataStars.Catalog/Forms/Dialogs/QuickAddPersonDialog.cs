using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 人物 + 単独名義の即時追加ダイアログ。
/// クレジットエントリ編集中に「マスタにまだ無い人物」を追加するためのダイアログ。
/// 氏名・かな・英名・備考だけを入力し、内部で <see cref="PersonsRepository.QuickAddWithSingleAliasAsync"/>
/// を呼んで persons → person_aliases → person_alias_persons の 3 行を 1 トランザクションで投入する。
/// OK で閉じたとき、新規 alias_id が <see cref="SelectedAliasId"/> にセットされる。
/// 呼び出し側はこれを <c>credit_block_entries.person_alias_id</c> 等に直接セットできる。
/// 共同名義（複数人 1 名義）はこのダイアログでは扱わない。
/// </summary>
public partial class QuickAddPersonDialog : Form
{
    private readonly PersonsRepository _personsRepo;

    /// <summary>登録成功時の新規 person_aliases.alias_id。キャンセル時は null。</summary>
    public int? SelectedAliasId { get; private set; }

    public QuickAddPersonDialog(PersonsRepository personsRepo)
    {
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        InitializeComponent();
        btnOk.Click += async (_, __) => await OnOkAsync();
    }

    /// <summary>OK ボタン処理：必須チェック後、リポジトリの QuickAddWithSingleAliasAsync を呼んで結果を保持。</summary>
    private async Task OnOkAsync()
    {
        try
        {
            string fullName = (txtFullName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fullName))
            {
                MessageBox.Show(this, "氏名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtFullName.Focus();
                return;
            }

            // PersonsRepository.QuickAddWithSingleAliasAsync の引数に
            var (familyName, givenName) = SplitFamilyGivenName(fullName);

            int aliasId = await _personsRepo.QuickAddWithSingleAliasAsync(
                fullName,
                string.IsNullOrWhiteSpace(txtFullNameKana.Text) ? null : txtFullNameKana.Text.Trim(),
                familyName,
                givenName,
                string.IsNullOrWhiteSpace(txtNameEn.Text)        ? null : txtNameEn.Text.Trim(),
                string.IsNullOrWhiteSpace(txtNotes.Text)         ? null : txtNotes.Text.Trim(),
                Environment.UserName);

            SelectedAliasId = aliasId;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>氏名文字列を 姓 / 名 に素朴分解する。 半角SP / 全角SP 区切り → (family, given)、 「・」区切り → (family, given) ※外国名想定で given・family の順、 区切りなし → (null, null)。</summary>
    private static (string? Family, string? Given) SplitFamilyGivenName(string fullName)
    {
        // 半角 SP / 全角 SP を許容
        int sp = fullName.IndexOf(' ');
        if (sp < 0) sp = fullName.IndexOf('\u3000');
        if (sp > 0)
        {
            string family = fullName[..sp].Trim();
            string given = fullName[(sp + 1)..].Trim();
            if (family.Length > 0 && given.Length > 0) return (family, given);
        }

        // 「・」区切り（外国名想定）→ given を前、family を後ろ
        int mid = fullName.IndexOf('・');
        if (mid > 0)
        {
            string given = fullName[..mid].Trim();
            string family = fullName[(mid + 1)..].Trim();
            if (family.Length > 0 && given.Length > 0) return (family, given);
        }

        // 分割なし
        return (null, null);
    }
}
