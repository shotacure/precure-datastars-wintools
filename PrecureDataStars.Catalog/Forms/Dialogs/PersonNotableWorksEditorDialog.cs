using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 人物の「代表作（プリキュアを除く）」（person_notable_works）の編集ダイアログ。
/// 選んだ人物の代表作を一覧し、作品名・役職・時期・公式サイト・裏取りの出典で追加・更新・削除する。
/// マスタ編集なので、追加・更新・削除はボタンを押した時点で DB に反映する（保存ボタンは無い）。
/// <list type="bullet">
///   <item>公式サイト：サイトでリンクする先。閉鎖済みでアーカイブに残る公式サイトで確かめたときは、アーカイブの URL を入れて「アーカイブ」にチェックする</item>
///   <item>出典：裏取りに使ったスタッフ表のページ（内部用。サイトには出さない）</item>
/// </list>
/// </summary>
public sealed class PersonNotableWorksEditorDialog : Form
{
    private readonly PersonNotableWorksRepository _worksRepo;
    private readonly int _personId;

    private readonly DataGridView _grid = new() { Dock = DockStyle.Top, Height = 220 };
    private readonly TextBox _txtTitle = new();
    private readonly ComboBox _cboRole = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _txtYearFrom = new();
    private readonly TextBox _txtYearTo = new();
    private readonly TextBox _txtOfficialUrl = new();
    private readonly CheckBox _chkArchive = new() { Text = "アーカイブ（公式サイトは閉鎖済み）" };
    private readonly TextBox _txtSourceUrl = new();
    private readonly NumericUpDown _nudOrder = new() { Minimum = 0, Maximum = 999 };
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnAdd = new() { Text = "新規追加" };
    private readonly Button _btnUpdate = new() { Text = "選択行を更新" };
    private readonly Button _btnDelete = new() { Text = "選択行を削除" };
    private readonly Button _btnClear = new() { Text = "入力欄を空にする" };
    private readonly Button _btnClose = new() { Text = "閉じる" };

    private List<WorkRow> _rows = new();

    /// <summary>ダイアログを構築する。<paramref name="personId"/> がこのダイアログの中心の人物。</summary>
    public PersonNotableWorksEditorDialog(PersonNotableWorksRepository worksRepo, int personId, string personName)
    {
        _worksRepo = worksRepo ?? throw new ArgumentNullException(nameof(worksRepo));
        _personId = personId;

        Text = $"代表作（プリキュアを除く）の編集：#{personId} {personName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 560);
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        BuildLayout();

        Load += async (_, _) => await ReloadAsync();
        _grid.SelectionChanged += (_, _) => OnRowSelected();
        _btnAdd.Click += async (_, _) => await SaveAsync(isNew: true);
        _btnUpdate.Click += async (_, _) => await SaveAsync(isNew: false);
        _btnDelete.Click += async (_, _) => await DeleteAsync();
        _btnClear.Click += (_, _) => ClearInputs();
        _btnClose.Click += (_, _) => Close();
    }

    private void BuildLayout()
    {
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        var pnl = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        void AddRow(string label, Control input, int y, int width)
        {
            pnl.Controls.Add(new Label { Text = label, Location = new Point(12, y + 4), Size = new Size(120, 20) });
            input.Location = new Point(136, y);
            input.Size = new Size(width, 24);
            pnl.Controls.Add(input);
        }
        void AddHint(string text, int x, int y, int width)
            => pnl.Controls.Add(new Label { Text = text, Location = new Point(x, y + 4), Size = new Size(width, 20), ForeColor = SystemColors.GrayText });

        _cboRole.Items.AddRange(PersonNotableWorkRoles.Suggestions.Cast<object>().ToArray());

        AddRow("作品名", _txtTitle, 12, 480);
        AddRow("役職", _cboRole, 44, 220);
        AddHint("候補以外も入れられる", 364, 44, 200);
        AddRow("開始年", _txtYearFrom, 76, 70);
        AddRow("終了年", _txtYearTo, 108, 70);
        AddHint("西暦。1 年だけなら終了年は空", 214, 76, 260);
        AddRow("公式サイト", _txtOfficialUrl, 140, 480);
        _chkArchive.Location = new Point(136, 168);
        _chkArchive.Size = new Size(320, 22);
        pnl.Controls.Add(_chkArchive);
        AddRow("出典（内部）", _txtSourceUrl, 196, 480);
        AddHint("裏取りに使ったスタッフ表のページ", 136, 220, 300);
        AddRow("並び順", _nudOrder, 252, 70);
        AddHint("小さい順。同じなら開始年順", 214, 252, 260);
        AddRow("メモ", _txtNotes, 284, 480);

        _btnAdd.SetBounds(720, 12, 150, 28);
        _btnUpdate.SetBounds(720, 44, 150, 28);
        _btnDelete.SetBounds(720, 76, 150, 28);
        _btnClear.SetBounds(720, 108, 150, 28);
        _btnClose.SetBounds(720, 284, 150, 28);
        pnl.Controls.AddRange(new Control[] { _btnAdd, _btnUpdate, _btnDelete, _btnClear, _btnClose });

        Controls.Add(pnl);
        Controls.Add(_grid);
    }

    /// <summary>この人物の代表作を読み直して画面に反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var works = await _worksRepo.GetByPersonAsync(_personId);
            _rows = works.Select(ToRow).ToList();
            _grid.DataSource = _rows.ToList();
            if (_grid.Columns.Contains(nameof(WorkRow.Work))) _grid.Columns[nameof(WorkRow.Work)]!.Visible = false;
            foreach (var (column, header) in ColumnHeaders)
            {
                if (_grid.Columns.Contains(column)) _grid.Columns[column]!.HeaderText = header;
            }
            _grid.ClearSelection();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private static WorkRow ToRow(PersonNotableWork w) => new()
    {
        Work = w,
        Order = w.DisplayOrder,
        Period = w.YearFrom is int from
            ? (w.YearTo is int to && to != from ? $"{from}–{to}" : $"{from}")
            : "",
        Title = w.WorkTitle,
        Role = w.RoleLabel,
        OfficialUrl = w.OfficialUrlIsArchive ? $"［アーカイブ］{w.OfficialUrl}" : (w.OfficialUrl ?? ""),
        SourceUrl = w.SourceUrl ?? ""
    };

    private void OnRowSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not WorkRow row || _grid.SelectedRows.Count == 0) return;
        var w = row.Work;
        _txtTitle.Text = w.WorkTitle;
        _cboRole.Text = w.RoleLabel;
        _txtYearFrom.Text = w.YearFrom?.ToString() ?? "";
        _txtYearTo.Text = w.YearTo?.ToString() ?? "";
        _txtOfficialUrl.Text = w.OfficialUrl ?? "";
        _chkArchive.Checked = w.OfficialUrlIsArchive;
        _txtSourceUrl.Text = w.SourceUrl ?? "";
        _nudOrder.Value = Math.Clamp(w.DisplayOrder, (int)_nudOrder.Minimum, (int)_nudOrder.Maximum);
        _txtNotes.Text = w.Notes ?? "";
    }

    /// <summary>入力欄を空にし、並び順に「今ある行の最大 + 1」を入れる（新規追加の準備）。</summary>
    private void ClearInputs()
    {
        _grid.ClearSelection();
        _txtTitle.Text = "";
        _cboRole.Text = "";
        _txtYearFrom.Text = "";
        _txtYearTo.Text = "";
        _txtOfficialUrl.Text = "";
        _chkArchive.Checked = false;
        _txtSourceUrl.Text = "";
        _nudOrder.Value = Math.Min(_nudOrder.Maximum, _rows.Count == 0 ? 0 : _rows.Max(r => r.Order) + 1);
        _txtNotes.Text = "";
    }

    /// <summary>入力欄の内容で代表作を追加（<paramref name="isNew"/>）または選択行を更新する。</summary>
    private async Task SaveAsync(bool isNew)
    {
        try
        {
            string title = _txtTitle.Text.Trim();
            string role = _cboRole.Text.Trim();
            if (title.Length == 0 || role.Length == 0)
            { MessageBox.Show(this, "作品名と役職を入れてください。"); return; }
            if (!TryParseYear(_txtYearFrom.Text, out var from) || !TryParseYear(_txtYearTo.Text, out var to))
            { MessageBox.Show(this, "年は西暦 4 桁で入れてください。"); return; }
            if (to.HasValue && !from.HasValue)
            { MessageBox.Show(this, "終了年だけを入れることはできません。開始年も入れてください。"); return; }
            if (from.HasValue && to.HasValue && from > to)
            { MessageBox.Show(this, "開始年が終了年より後になっています。"); return; }
            string? officialUrl = string.IsNullOrWhiteSpace(_txtOfficialUrl.Text) ? null : _txtOfficialUrl.Text.Trim();
            if (_chkArchive.Checked && officialUrl is null)
            { MessageBox.Show(this, "「アーカイブ」にチェックするときは、公式サイトの欄にアーカイブの URL を入れてください。"); return; }

            PersonNotableWork target;
            if (isNew)
            {
                target = new PersonNotableWork { PersonId = _personId, CreatedBy = Environment.UserName };
            }
            else
            {
                if (_grid.CurrentRow?.DataBoundItem is not WorkRow row || _grid.SelectedRows.Count == 0)
                { MessageBox.Show(this, "更新する行を選んでください。"); return; }
                target = row.Work;
            }

            target.WorkTitle = title;
            target.RoleLabel = role;
            target.YearFrom = from;
            // 開始年と同じ終了年は 1 年だけの作品として持つ。
            target.YearTo = to == from ? null : to;
            target.OfficialUrl = officialUrl;
            target.OfficialUrlIsArchive = _chkArchive.Checked;
            target.SourceUrl = string.IsNullOrWhiteSpace(_txtSourceUrl.Text) ? null : _txtSourceUrl.Text.Trim();
            target.DisplayOrder = (int)_nudOrder.Value;
            target.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
            target.UpdatedBy = Environment.UserName;

            if (isNew) await _worksRepo.InsertAsync(target);
            else await _worksRepo.UpdateAsync(target);

            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteAsync()
    {
        try
        {
            if (_grid.CurrentRow?.DataBoundItem is not WorkRow row || _grid.SelectedRows.Count == 0)
            { MessageBox.Show(this, "削除する行を選んでください。"); return; }
            if (MessageBox.Show(this, $"「{row.Title}（{row.Role}）」を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _worksRepo.DeleteAsync(row.Work.WorkId);
            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>空なら null、西暦 4 桁（1900〜2100）なら年として読む。読めなければ false。</summary>
    private static bool TryParseYear(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (int.TryParse(text.Trim(), out var y) && y is >= 1900 and <= 2100)
        {
            value = y;
            return true;
        }
        return false;
    }

    /// <summary>グリッドの 1 行。見出しは <see cref="ColumnHeaders"/> で付ける。</summary>
    private sealed class WorkRow
    {
        public PersonNotableWork Work { get; init; } = new();
        public int Order { get; init; }
        public string Period { get; init; } = "";
        public string Title { get; init; } = "";
        public string Role { get; init; } = "";
        public string OfficialUrl { get; init; } = "";
        public string SourceUrl { get; init; } = "";
    }

    /// <summary>グリッドの列名 → 見出し。</summary>
    private static readonly (string Column, string Header)[] ColumnHeaders =
    {
        (nameof(WorkRow.Order), "並び順"),
        (nameof(WorkRow.Period), "時期"),
        (nameof(WorkRow.Title), "作品名"),
        (nameof(WorkRow.Role), "役職"),
        (nameof(WorkRow.OfficialUrl), "公式サイト"),
        (nameof(WorkRow.SourceUrl), "出典（内部）"),
    };
}
