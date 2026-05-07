using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// ユニット名義の構成メンバー（person_alias_members）を編集する専用ダイアログ
/// （v1.2.3 追加）。
/// <para>
/// 対象 alias（ユニット側）の構成メンバーリストを in-memory で編集し、OK 押下時に
/// <see cref="ResultMembers"/> プロパティ経由で呼び出し側に返す。リポジトリ呼び出しは
/// 行わない（呼び出し側が <c>ReplaceAllAsync</c> 等でまとめて保存する）。
/// </para>
/// <para>
/// 自分自身を PERSON メンバーとして追加することはダイアログ内で弾く。ネスト禁止
/// （PERSON メンバーがユニット、または親が他ユニットのメンバー）はダイアログでは
/// 検査せず、保存時に DB トリガーが拒否する。
/// </para>
/// </summary>
public partial class PersonAliasMembersEditDialog : Form
{
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly int _parentAliasId;
    private readonly BindingList<MemberRow> _members = new();

    /// <summary>編集確定時のメンバー行リスト（OK で閉じたとき有効）。</summary>
    public IReadOnlyList<MemberDto> ResultMembers { get; private set; } = Array.Empty<MemberDto>();

    public PersonAliasMembersEditDialog(
        int parentAliasId,
        IReadOnlyList<MemberDto> initialMembers,
        PersonAliasesRepository personAliasesRepo,
        CharacterAliasesRepository characterAliasesRepo)
    {
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));
        _parentAliasId = parentAliasId;

        InitializeComponent();
        gridMembers.DataSource = _members;

        byte seq = 1;
        foreach (var m in initialMembers)
        {
            _members.Add(new MemberRow
            {
                Seq = seq++,
                Kind = m.MemberKind,
                MemberPersonAliasId = m.MemberPersonAliasId,
                MemberCharacterAliasId = m.MemberCharacterAliasId,
                MemberDisplay = m.MemberDisplay,
                Notes = m.Notes
            });
        }

        gridMembers.SelectionChanged += (_, __) => UpdateDetailFromSelection();
        cboKind.SelectedIndexChanged += (_, __) => RefreshKindEnable();
        btnPickPerson.Click += async (_, __) => await OnPickPersonAsync();
        btnPickCharacter.Click += async (_, __) => await OnPickCharacterAsync();
        btnAdd.Click += (_, __) => OnAddNewLine();
        btnApply.Click += (_, __) => OnApplyLine();
        btnDelete.Click += (_, __) => OnDeleteLine();
        btnUp.Click += (_, __) => OnMove(-1);
        btnDown.Click += (_, __) => OnMove(+1);
        btnOk.Click += (_, __) => OnOkClick();

        if (cboKind.SelectedIndex < 0) cboKind.SelectedIndex = 0;
        UpdateDetailFromSelection();
    }

    private async Task OnPickPersonAsync()
    {
        using var dlg = new PersonAliasPickerDialog(_personAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;

        // 自己参照は disallow（DB 側 CHECK でも弾かれるが UX のため早期リジェクト）
        if (dlg.SelectedId.Value == _parentAliasId)
        {
            MessageBox.Show("ユニット自身をメンバーに含めることはできません。", "不可",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var alias = await _personAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
        if (alias is null) return;

        cboKind.SelectedItem = "PERSON";
        txtMemberDisplay.Text = alias.GetDisplayName();
        txtMemberDisplay.Tag = ("PERSON", alias.AliasId);
    }

    private async Task OnPickCharacterAsync()
    {
        using var dlg = new CharacterAliasPickerDialog(_characterAliasesRepo);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is null) return;
        var alias = await _characterAliasesRepo.GetByIdAsync(dlg.SelectedId.Value);
        if (alias is null) return;

        cboKind.SelectedItem = "CHARACTER";
        txtMemberDisplay.Text = alias.Name;
        txtMemberDisplay.Tag = ("CHARACTER", alias.AliasId);
    }

    /// <summary>kind を選び直したときに参照ボタンの強調を切り替えるだけ（入力欄は同じ TextBox を共有）。</summary>
    private void RefreshKindEnable()
    {
        bool isPerson = (cboKind.SelectedItem as string) == "PERSON";
        // どちらの種別でも 1 つの TextBox を共有する設計なので、ボタンの太字程度で UI ヒントを付ける。
        btnPickPerson.Font = new System.Drawing.Font(Font, isPerson ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
        btnPickCharacter.Font = new System.Drawing.Font(Font, !isPerson ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular);
    }

    private void UpdateDetailFromSelection()
    {
        if (gridMembers.CurrentRow?.DataBoundItem is MemberRow row)
        {
            lblSeqValue.Text = row.Seq.ToString();
            lblSeqValue.ForeColor = SystemColors.ControlText;
            cboKind.SelectedItem = row.Kind == PersonAliasMemberKind.Person ? "PERSON" : "CHARACTER";
            txtMemberDisplay.Text = row.MemberDisplay ?? "";
            // Tag は ValueTuple<string,int> として保持。両分岐とも同じ tuple 型のため
            // 三項演算子で型が確定し、object? Tag に暗黙ボックス化される。
            txtMemberDisplay.Tag = row.Kind == PersonAliasMemberKind.Person
                ? ("PERSON",    row.MemberPersonAliasId ?? 0)
                : ("CHARACTER", row.MemberCharacterAliasId ?? 0);
            txtNotes.Text = row.Notes ?? "";
        }
        else
        {
            lblSeqValue.Text = "(新規)";
            lblSeqValue.ForeColor = Color.DimGray;
            txtMemberDisplay.Text = "";
            txtMemberDisplay.Tag = null;
            txtNotes.Text = "";
        }
        RefreshKindEnable();
    }

    private void OnAddNewLine()
    {
        if (!TryBuildMember(out var newMember)) return;
        newMember.Seq = (byte)(_members.Count + 1);
        _members.Add(newMember);
        if (gridMembers.Rows.Count > 0)
            gridMembers.CurrentCell = gridMembers.Rows[gridMembers.Rows.Count - 1].Cells[0];
    }

    private void OnApplyLine()
    {
        if (gridMembers.CurrentRow?.DataBoundItem is not MemberRow row) return;
        if (!TryBuildMember(out var updated)) return;
        row.Kind = updated.Kind;
        row.MemberPersonAliasId = updated.MemberPersonAliasId;
        row.MemberCharacterAliasId = updated.MemberCharacterAliasId;
        row.MemberDisplay = updated.MemberDisplay;
        row.Notes = updated.Notes;
        _members.ResetItem(_members.IndexOf(row));
    }

    private bool TryBuildMember(out MemberRow row)
    {
        row = new MemberRow();
        if (txtMemberDisplay.Tag is not ValueTuple<string, int> tag || tag.Item2 == 0)
        {
            MessageBox.Show("メンバーが選択されていません。「PERSON 選択...」または「CHARACTER 選択...」から選んでください。",
                "未入力", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        if (tag.Item1 == "PERSON")
        {
            row.Kind = PersonAliasMemberKind.Person;
            row.MemberPersonAliasId = tag.Item2;
            row.MemberCharacterAliasId = null;
        }
        else
        {
            row.Kind = PersonAliasMemberKind.Character;
            row.MemberCharacterAliasId = tag.Item2;
            row.MemberPersonAliasId = null;
        }
        row.MemberDisplay = txtMemberDisplay.Text;
        row.Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text;

        // 同一メンバーの重複登録を弾く（PK + UNIQUE で DB も弾くが UX のため早期リジェクト）。
        // out パラメータ row はラムダ式内で直接参照できない（CS1628）ため、必要な値だけ
        // ローカル変数にコピーしてからクロージャで使う。
        var rowLocal = row;
        bool dup = _members.Any(m =>
            m != rowLocal && m.Kind == rowLocal.Kind &&
            ((rowLocal.Kind == PersonAliasMemberKind.Person && m.MemberPersonAliasId == rowLocal.MemberPersonAliasId) ||
             (rowLocal.Kind == PersonAliasMemberKind.Character && m.MemberCharacterAliasId == rowLocal.MemberCharacterAliasId)));
        if (dup)
        {
            MessageBox.Show("同じメンバーが既に登録されています。", "重複",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private void OnDeleteLine()
    {
        if (gridMembers.CurrentRow?.DataBoundItem is not MemberRow row) return;
        _members.Remove(row);
        ReassignSeqs();
    }

    private void OnMove(int direction)
    {
        if (gridMembers.CurrentRow?.DataBoundItem is not MemberRow row) return;
        int idx = _members.IndexOf(row);
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= _members.Count) return;
        _members.RemoveAt(idx);
        _members.Insert(newIdx, row);
        ReassignSeqs();
        gridMembers.CurrentCell = gridMembers.Rows[newIdx].Cells[0];
    }

    private void ReassignSeqs()
    {
        for (int i = 0; i < _members.Count; i++) _members[i].Seq = (byte)(i + 1);
        gridMembers.DataSource = null;
        gridMembers.DataSource = _members;
    }

    private void OnOkClick()
    {
        ResultMembers = _members.Select(m => new MemberDto
        {
            MemberKind = m.Kind,
            MemberPersonAliasId = m.MemberPersonAliasId,
            MemberCharacterAliasId = m.MemberCharacterAliasId,
            MemberDisplay = m.MemberDisplay ?? "",
            Notes = m.Notes
        }).ToList();
    }

    /// <summary>呼び出し側との受け渡し DTO（v1.2.3 追加）。</summary>
    public sealed class MemberDto
    {
        public PersonAliasMemberKind MemberKind { get; set; }
        public int? MemberPersonAliasId { get; set; }
        public int? MemberCharacterAliasId { get; set; }
        public string MemberDisplay { get; set; } = "";
        public string? Notes { get; set; }
    }

    /// <summary>DataGridView バインド用内部行クラス。</summary>
    private sealed class MemberRow
    {
        public byte Seq { get; set; }
        public PersonAliasMemberKind Kind { get; set; }
        public int? MemberPersonAliasId { get; set; }
        public int? MemberCharacterAliasId { get; set; }
        public string? MemberDisplay { get; set; }
        public string? Notes { get; set; }

        public string KindLabel => Kind == PersonAliasMemberKind.Person ? "PERSON" : "CHARACTER";
    }
}
