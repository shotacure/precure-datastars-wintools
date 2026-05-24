using System;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 人物 + 単独名義の入力収集ダイアログ。
/// クレジットエントリ編集中に「マスタにまだ無い人物」を追加するためのダイアログ。
/// 氏名・かな・英名・備考だけを入力する。
/// ステージD で「保存ボタンまで DB に書かない」原則に揃えるため、ダイアログ自身は DB に
/// 一切書き込まない。OK で閉じたとき、入力値が公開プロパティに保持されるので、
/// 呼び出し側（<c>EntryEditorPanel</c> 等）がそれを <c>CreditDraftSession.PendingPersonAliases</c>
/// に積み、<c>CreditSaveService</c> Phase 0 が保存時に一括投入する流れ。
/// 共同名義（複数人 1 名義）はこのダイアログでは扱わない。
/// </summary>
public partial class QuickAddPersonDialog : Form
{
    /// <summary>OK 確定時、入力された氏名（必須）。</summary>
    public string ResultFullName { get; private set; } = "";

    /// <summary>OK 確定時、入力された氏名かな。空なら null。</summary>
    public string? ResultFullNameKana { get; private set; }

    /// <summary>OK 確定時、入力された英名。空なら null。</summary>
    public string? ResultNameEn { get; private set; }

    /// <summary>OK 確定時、入力された備考。空なら null。</summary>
    public string? ResultNotes { get; private set; }

    /// <summary>OK 確定時、氏名から自動分割された姓。区切り無しなら null。</summary>
    public string? ResultFamilyName { get; private set; }

    /// <summary>OK 確定時、氏名から自動分割された名。区切り無しなら null。</summary>
    public string? ResultGivenName { get; private set; }

    public QuickAddPersonDialog()
    {
        InitializeComponent();
        btnOk.Click += (_, __) => OnOk();
    }

    /// <summary>OK ボタン処理：必須チェック後、入力値を ResultXxx プロパティに格納してダイアログを閉じる。
    /// DB 投入は行わない（保存ボタンを押したときに Phase 0 が走るまで Draft 内に保留される）。</summary>
    private void OnOk()
    {
        string fullName = (txtFullName.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            MessageBox.Show(this, "氏名は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            txtFullName.Focus();
            return;
        }

        var (familyName, givenName) = SplitFamilyGivenName(fullName);

        ResultFullName = fullName;
        ResultFullNameKana = string.IsNullOrWhiteSpace(txtFullNameKana.Text) ? null : txtFullNameKana.Text.Trim();
        ResultNameEn       = string.IsNullOrWhiteSpace(txtNameEn.Text)        ? null : txtNameEn.Text.Trim();
        ResultNotes        = string.IsNullOrWhiteSpace(txtNotes.Text)         ? null : txtNotes.Text.Trim();
        ResultFamilyName   = familyName;
        ResultGivenName    = givenName;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>氏名文字列を 姓 / 名 に素朴分解する。 半角SP / 全角SP 区切り → (family, given)、 「・」区切り → (family, given) ※外国名想定で given・family の順、 区切りなし → (null, null)。</summary>
    private static (string? Family, string? Given) SplitFamilyGivenName(string fullName)
    {
        // 半角 SP / 全角 SP を許容
        int sp = fullName.IndexOf(' ');
        if (sp < 0) sp = fullName.IndexOf('　');
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
