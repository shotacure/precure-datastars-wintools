using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.NameResolution;

/// <summary>
/// 名寄せセンターから「新規」ボタンで開く、人物 + 人物名義のペア新規登録ダイアログ。
/// フリーテキストの原文を初期値に入れ、ユーザーが氏名・よみを確定すると 3 テーブルへ
/// 一括 INSERT（persons → person_aliases → person_alias_persons の順）し、新規発行された
/// alias_id を <see cref="CreatedAliasId"/> に返す。
/// <list type="bullet">
///   <item><c>persons</c>: 氏名（フルネーム）と任意のよみだけ詰める。 family_name / given_name は分離せず full_name 1 列にまとめて入れる方針（音楽クレジット由来のフルネームしか手元にない運用前提）。</item>
///   <item><c>person_aliases</c>: 同じ氏名・よみで 1 名義行を追加する。 改名チェーン（predecessor / successor）は付けない。</item>
///   <item><c>person_alias_persons</c>: 上記 2 行を <c>(alias_id, person_id)</c> 中間表でリンクする。 <c>person_seq = 1</c>。</item>
/// </list>
/// マスタへの自動 INSERT は名寄せセンター全体としては禁則だが、本ダイアログは
/// 「ユーザーが内容確認の上で OK を押して明示的に登録する」フローであり、
/// 「自動的に勝手に登録」とは趣旨が異なるため許容する。
/// </summary>
public sealed class NewPersonAliasDialog : Form
{
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _aliasesRepo;
    private readonly PersonAliasPersonsRepository _linkRepo;

    private readonly TextBox _txtName;
    private readonly TextBox _txtKana;
    private readonly Label _lblPreview;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;

    /// <summary>OK で確定したときに発行された新規 alias_id。キャンセル時は null。</summary>
    public int? CreatedAliasId { get; private set; }

    public NewPersonAliasDialog(
        PersonsRepository personsRepo,
        PersonAliasesRepository aliasesRepo,
        PersonAliasPersonsRepository linkRepo,
        string initialName)
    {
        _personsRepo = personsRepo ?? throw new ArgumentNullException(nameof(personsRepo));
        _aliasesRepo = aliasesRepo ?? throw new ArgumentNullException(nameof(aliasesRepo));
        _linkRepo = linkRepo ?? throw new ArgumentNullException(nameof(linkRepo));

        Text = "人物・名義を新規登録";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(440, 230);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = "氏名",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 28
        }, 0, 0);
        _txtName = new TextBox { Dock = DockStyle.Fill, Text = initialName ?? "" };
        layout.Controls.Add(_txtName, 1, 0);

        layout.Controls.Add(new Label
        {
            Text = "よみ（任意）",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 28
        }, 0, 1);
        _txtKana = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtKana, 1, 1);

        _lblPreview = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 80,
            ForeColor = SystemColors.GrayText,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8),
            Text = ""
        };
        layout.SetColumnSpan(_lblPreview, 2);
        layout.Controls.Add(_lblPreview, 0, 2);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40
        };
        layout.SetColumnSpan(btnPanel, 2);
        layout.Controls.Add(btnPanel, 0, 3);

        _btnOk = new Button { Text = "登録", Width = 100, Enabled = false };
        _btnCancel = new Button { Text = "キャンセル", Width = 100, DialogResult = DialogResult.Cancel };
        btnPanel.Controls.Add(_btnOk);
        btnPanel.Controls.Add(_btnCancel);
        CancelButton = _btnCancel;
        AcceptButton = _btnOk;

        _txtName.TextChanged += (_, __) => RefreshPreview();
        _txtKana.TextChanged += (_, __) => RefreshPreview();
        _btnOk.Click += async (_, __) => await OnOkAsync();

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        string name = _txtName.Text.Trim();
        string kana = _txtKana.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _lblPreview.Text = "氏名は必須です。";
            _btnOk.Enabled = false;
            return;
        }
        var (family, given) = SplitFamilyGiven(name);
        string personDetail = family is not null
            ? $"full_name='{name}', family_name='{family}', given_name='{given}'"
            : $"full_name='{name}'";
        if (!string.IsNullOrEmpty(kana)) personDetail += $", full_name_kana='{kana}'";

        _lblPreview.Text =
            $"以下を登録します（OK でコミット）：\n" +
            $"  persons               + 1 行（{personDetail}）\n" +
            $"  person_aliases        + 1 行（name='{name}'{(string.IsNullOrEmpty(kana) ? "" : $", name_kana='{kana}'")}）\n" +
            $"  person_alias_persons  + 1 行（リンク、person_seq=1）";
        _btnOk.Enabled = true;
    }

    /// <summary>
    /// 「姓 名」のように半角スペースで区切られている氏名を `family_name` / `given_name` の 2 列に分割する。
    /// 区切り無し or 区切り 1 文字のみ・先頭/末尾区切り等の場合は両方 null を返して
    /// full_name 単独運用にフォールバックする（DB の family_name / given_name は NULL のまま）。
    /// 区切りは最初のスペース固定（多重区切りはまれだが、姓 + 名残し全部を given に押し込む形）。
    /// </summary>
    private static (string? Family, string? Given) SplitFamilyGiven(string name)
    {
        int idx = name.IndexOf(' ');
        if (idx <= 0 || idx >= name.Length - 1) return (null, null);
        string family = name[..idx].Trim();
        string given = name[(idx + 1)..].Trim();
        if (family.Length == 0 || given.Length == 0) return (null, null);
        return (family, given);
    }

    private async Task OnOkAsync()
    {
        string name = _txtName.Text.Trim();
        string? kana = _txtKana.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        if (string.IsNullOrEmpty(kana)) kana = null;
        var (family, given) = SplitFamilyGiven(name);

        _btnOk.Enabled = false;
        try
        {
            // person → person_alias → person_alias_persons の順で INSERT。
            // どこかで失敗したら以降を実行せず、ユーザーに通知して終了（クリーンアップは手動）。
            int personId = await _personsRepo.InsertAsync(new Person
            {
                FamilyName = family,
                GivenName = given,
                FullName = name,
                FullNameKana = kana,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            });

            int aliasId = await _aliasesRepo.InsertAsync(new PersonAlias
            {
                Name = name,
                NameKana = kana,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            });

            await _linkRepo.UpsertAsync(new PersonAliasPerson
            {
                AliasId = aliasId,
                PersonId = personId,
                PersonSeq = 1
            });

            CreatedAliasId = aliasId;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnOk.Enabled = true;
        }
    }
}
