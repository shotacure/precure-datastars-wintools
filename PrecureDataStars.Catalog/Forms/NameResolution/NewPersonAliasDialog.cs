using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.NameResolution;

/// <summary>
/// 名寄せセンターから「新規」ボタンで開く、人物 + 人物名義のペア新規登録ダイアログ。
/// 種別は「個人」「ユニット」の 2 モードを切り替えられる：
/// <list type="bullet">
///   <item>
///     <b>個人</b>: <c>persons</c> + <c>person_aliases</c> + <c>person_alias_persons</c> の 3 表へ INSERT。
///     姓名欄が半角スペース区切りなら姓・名にも分割して保存。
///   </item>
///   <item>
///     <b>ユニット</b>: <c>person_aliases</c> の 1 行だけ INSERT する（<c>persons</c> 行は持たない仕様、
///     <c>person_alias_persons</c> も張らない）。メンバー構成（<c>person_alias_members</c>）の編集は
///     Catalog 側のクレジット系マスタ編集に委ねる（本ダイアログでは扱わない）。
///   </item>
/// </list>
/// どちらのモードでも新規発行された alias_id を <see cref="CreatedAliasId"/> に返し、
/// 呼び出し側はそれを当該 token 行に自動マッピングできる。
/// マスタへの自動 INSERT は名寄せセンター全体としては禁則だが、本ダイアログは
/// 「ユーザーが内容確認の上で OK を押して明示的に登録する」フローのため許容する。
/// </summary>
public sealed class NewPersonAliasDialog : Form
{
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _aliasesRepo;
    private readonly PersonAliasPersonsRepository _linkRepo;

    private readonly RadioButton _rbIndividual;
    private readonly RadioButton _rbUnit;
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
        ClientSize = new Size(460, 280);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        // 種別行：個人 / ユニット。
        layout.Controls.Add(new Label
        {
            Text = "種別",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 28
        }, 0, 0);
        _rbIndividual = new RadioButton { Text = "個人", Checked = true, AutoSize = true };
        _rbUnit = new RadioButton { Text = "ユニット", AutoSize = true };
        var modeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false
        };
        modeRow.Controls.Add(_rbIndividual);
        modeRow.Controls.Add(_rbUnit);
        layout.Controls.Add(modeRow, 1, 0);

        // 氏名行。
        layout.Controls.Add(new Label
        {
            Text = "氏名",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 28
        }, 0, 1);
        _txtName = new TextBox { Dock = DockStyle.Fill, Text = initialName ?? "" };
        layout.Controls.Add(_txtName, 1, 1);

        // よみ行。
        layout.Controls.Add(new Label
        {
            Text = "よみ（任意）",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 28
        }, 0, 2);
        _txtKana = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(_txtKana, 1, 2);

        // プレビュー行。
        _lblPreview = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 100,
            ForeColor = SystemColors.GrayText,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8),
            Text = ""
        };
        layout.SetColumnSpan(_lblPreview, 2);
        layout.Controls.Add(_lblPreview, 0, 3);

        // ボタン行。
        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40
        };
        layout.SetColumnSpan(btnPanel, 2);
        layout.Controls.Add(btnPanel, 0, 4);

        _btnOk = new Button { Text = "登録", Width = 100, Enabled = false };
        _btnCancel = new Button { Text = "キャンセル", Width = 100, DialogResult = DialogResult.Cancel };
        btnPanel.Controls.Add(_btnOk);
        btnPanel.Controls.Add(_btnCancel);
        CancelButton = _btnCancel;
        AcceptButton = _btnOk;

        _txtName.TextChanged += (_, __) => RefreshPreview();
        _txtKana.TextChanged += (_, __) => RefreshPreview();
        _rbIndividual.CheckedChanged += (_, __) => RefreshPreview();
        _rbUnit.CheckedChanged += (_, __) => RefreshPreview();
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

        if (_rbIndividual.Checked)
        {
            var (family, given) = SplitFamilyGiven(name);
            string personDetail = family is not null
                ? $"full_name='{name}', family_name='{family}', given_name='{given}'"
                : $"full_name='{name}'";
            if (!string.IsNullOrEmpty(kana)) personDetail += $", full_name_kana='{kana}'";

            _lblPreview.Text =
                $"以下を登録します（個人 / OK でコミット）：\n" +
                $"  persons               + 1 行（{personDetail}）\n" +
                $"  person_aliases        + 1 行（name='{name}'{(string.IsNullOrEmpty(kana) ? "" : $", name_kana='{kana}'")}）\n" +
                $"  person_alias_persons  + 1 行（リンク、person_seq=1）";
        }
        else
        {
            _lblPreview.Text =
                $"以下を登録します（ユニット / OK でコミット）：\n" +
                $"  person_aliases  + 1 行（name='{name}'{(string.IsNullOrEmpty(kana) ? "" : $", name_kana='{kana}'")}）\n" +
                $"\n" +
                $"※ ユニットは persons 行を持たない仕様です。\n" +
                $"   メンバー構成（person_alias_members）の編集は Catalog 側のマスタ編集で行ってください。";
        }
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

        _btnOk.Enabled = false;
        try
        {
            int aliasId;
            if (_rbIndividual.Checked)
            {
                // 個人モード：person → person_alias → person_alias_persons の順で INSERT。
                // どこかで失敗したら以降を実行せず、ユーザーに通知して終了（クリーンアップは手動）。
                var (family, given) = SplitFamilyGiven(name);
                int personId = await _personsRepo.InsertAsync(new Person
                {
                    FamilyName = family,
                    GivenName = given,
                    FullName = name,
                    FullNameKana = kana,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                });

                aliasId = await _aliasesRepo.InsertAsync(new PersonAlias
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
            }
            else
            {
                // ユニットモード：person_aliases のみ INSERT。 persons 行も person_alias_persons も持たない
                // （schema コメント「ユニット名義などで〜」「persons は個人単位」運用に従う）。
                aliasId = await _aliasesRepo.InsertAsync(new PersonAlias
                {
                    Name = name,
                    NameKana = kana,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                });
            }

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
