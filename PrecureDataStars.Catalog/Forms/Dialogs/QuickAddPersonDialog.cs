using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 人物 + 単独名義の即時追加ダイアログ（v1.2.0 工程 B-3c 追加）。
/// <para>
/// クレジットエントリ編集中に「マスタにまだ無い人物」を追加するためのダイアログ。
/// 氏名・かな・英名・備考だけを入力し、内部で <see cref="PersonsRepository.QuickAddWithSingleAliasAsync"/>
/// を呼んで persons → person_aliases → person_alias_persons の 3 行を 1 トランザクションで投入する。
/// </para>
/// <para>
/// OK で閉じたとき、新規 alias_id が <see cref="SelectedAliasId"/> にセットされる。
/// 呼び出し側はこれを <c>credit_block_entries.person_alias_id</c> 等に直接セットできる。
/// 共同名義（複数人 1 名義）はこのダイアログでは扱わない。
/// </para>
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

            int aliasId = await _personsRepo.QuickAddWithSingleAliasAsync(
                fullName,
                string.IsNullOrWhiteSpace(txtFullNameKana.Text) ? null : txtFullNameKana.Text.Trim(),
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
}
