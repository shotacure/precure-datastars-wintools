using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 人物の「役職履歴」（person_positions）の編集ダイアログ。
/// 選んだ人物の異動を一覧し、会社・異動日・役職・裏取りの出典で追加・更新・削除する。
/// マスタ編集なので、追加・更新・削除はボタンを押した時点で DB に反映する（保存ボタンは無い）。
/// <list type="bullet">
///   <item>会社：その役職の会社。サイトでは見出しを「役職履歴（会社名）」にして企業詳細へリンクする</item>
///   <item>異動日：yyyy-MM-dd。空なら日付不明（サイトでは「？」）。日付不明の行は並び順で正しい位置に置く</item>
///   <item>出典：裏取りに使った発表のページ（内部用。サイトには出さない）</item>
/// </list>
/// </summary>
public sealed class PersonPositionsEditorDialog : Form
{
    private readonly PersonPositionsRepository _positionsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly int _personId;

    private readonly DataGridView _grid = new() { Dock = DockStyle.Top, Height = 240 };
    private readonly TextBox _txtCompany = new() { ReadOnly = true, BackColor = SystemColors.Window };
    private readonly Button _btnPickCompany = new() { Text = "選択..." };
    private readonly Button _btnClearCompany = new() { Text = "解除" };
    private readonly TextBox _txtDate = new();
    private readonly TextBox _txtPosition = new();
    private readonly TextBox _txtSourceUrl = new();
    private readonly NumericUpDown _nudOrder = new() { Minimum = 0, Maximum = 999 };
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnAdd = new() { Text = "新規追加" };
    private readonly Button _btnUpdate = new() { Text = "選択行を更新" };
    private readonly Button _btnDelete = new() { Text = "選択行を削除" };
    private readonly Button _btnClear = new() { Text = "入力欄を空にする" };
    private readonly Button _btnClose = new() { Text = "閉じる" };

    private List<PositionRow> _rows = new();
    /// <summary>グリッドの会社名の表示用（company_id → 名前）。</summary>
    private readonly Dictionary<int, string> _companyNames = new();

    /// <summary>ダイアログを構築する。<paramref name="personId"/> がこのダイアログの中心の人物。</summary>
    public PersonPositionsEditorDialog(PersonPositionsRepository positionsRepo, CompaniesRepository companiesRepo, int personId, string personName)
    {
        _positionsRepo = positionsRepo ?? throw new ArgumentNullException(nameof(positionsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _personId = personId;

        Text = $"役職履歴の編集：#{personId} {personName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 540);
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        BuildLayout();

        Load += async (_, _) => await ReloadAsync();
        _grid.SelectionChanged += (_, _) => OnRowSelected();
        _btnPickCompany.Click += async (_, _) => await PickCompanyAsync();
        _btnClearCompany.Click += (_, _) => SetCompany(null);
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

        AddRow("会社", _txtCompany, 12, 300);
        _btnPickCompany.SetBounds(442, 11, 64, 26);
        _btnClearCompany.SetBounds(510, 11, 48, 26);
        pnl.Controls.AddRange(new Control[] { _btnPickCompany, _btnClearCompany });
        AddRow("異動日", _txtDate, 44, 110);
        AddHint("yyyy-MM-dd。空なら日付不明（サイトは「？」）", 254, 44, 360);
        AddRow("役職", _txtPosition, 76, 560);
        AddRow("出典（内部）", _txtSourceUrl, 108, 560);
        AddHint("裏取りに使った発表のページ", 136, 132, 300);
        AddRow("並び順", _nudOrder, 164, 70);
        AddHint("小さい順。同じなら異動日順", 214, 164, 260);
        AddRow("メモ", _txtNotes, 196, 560);

        _btnAdd.SetBounds(720, 12, 150, 28);
        _btnUpdate.SetBounds(720, 44, 150, 28);
        _btnDelete.SetBounds(720, 76, 150, 28);
        _btnClear.SetBounds(720, 108, 150, 28);
        _btnClose.SetBounds(720, 196, 150, 28);
        pnl.Controls.AddRange(new Control[] { _btnAdd, _btnUpdate, _btnDelete, _btnClear, _btnClose });

        Controls.Add(pnl);
        Controls.Add(_grid);
    }

    /// <summary>この人物の役職履歴を読み直して画面に反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var positions = await _positionsRepo.GetByPersonAsync(_personId);
            foreach (int cid in positions.Where(p => p.CompanyId.HasValue).Select(p => p.CompanyId!.Value).Distinct())
            {
                if (_companyNames.ContainsKey(cid)) continue;
                var c = await _companiesRepo.GetByIdAsync(cid);
                _companyNames[cid] = c?.Name ?? $"#{cid}";
            }
            _rows = positions.Select(ToRow).ToList();
            _grid.DataSource = _rows.ToList();
            if (_grid.Columns.Contains(nameof(PositionRow.Position))) _grid.Columns[nameof(PositionRow.Position)]!.Visible = false;
            foreach (var (column, header) in ColumnHeaders)
            {
                if (_grid.Columns.Contains(column)) _grid.Columns[column]!.HeaderText = header;
            }
            _grid.ClearSelection();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private PositionRow ToRow(PersonPosition p) => new()
    {
        Position = p,
        Order = p.DisplayOrder,
        Date = p.EffectiveDate?.ToString("yyyy-MM-dd") ?? "？",
        Company = p.CompanyId is int cid && _companyNames.TryGetValue(cid, out var n) ? n : "",
        Text = p.PositionText,
        SourceUrl = p.SourceUrl ?? ""
    };

    private void OnRowSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not PositionRow row || _grid.SelectedRows.Count == 0) return;
        var p = row.Position;
        SetCompany(p.CompanyId);
        _txtDate.Text = p.EffectiveDate?.ToString("yyyy-MM-dd") ?? "";
        _txtPosition.Text = p.PositionText;
        _txtSourceUrl.Text = p.SourceUrl ?? "";
        _nudOrder.Value = Math.Clamp(p.DisplayOrder, (int)_nudOrder.Minimum, (int)_nudOrder.Maximum);
        _txtNotes.Text = p.Notes ?? "";
    }

    /// <summary>入力欄の会社を設定する（null で未設定）。</summary>
    private void SetCompany(int? companyId)
    {
        _txtCompany.Tag = companyId;
        _txtCompany.Text = companyId is int cid ? (_companyNames.TryGetValue(cid, out var n) ? n : $"#{cid}") : "";
    }

    /// <summary>会社 picker を開いて入力欄の会社にする。</summary>
    private async Task PickCompanyAsync()
    {
        try
        {
            using var dlg = new CompanyPickerDialog(_companiesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedId is not int cid) return;
            var c = await _companiesRepo.GetByIdAsync(cid);
            _companyNames[cid] = c?.Name ?? $"#{cid}";
            SetCompany(cid);
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>入力欄を空にし、並び順に「今ある行の最大 + 1」を入れる（新規追加の準備）。会社は直前の値を残す。</summary>
    private void ClearInputs()
    {
        _grid.ClearSelection();
        _txtDate.Text = "";
        _txtPosition.Text = "";
        _txtSourceUrl.Text = "";
        _nudOrder.Value = Math.Min(_nudOrder.Maximum, _rows.Count == 0 ? 0 : _rows.Max(r => r.Order) + 1);
        _txtNotes.Text = "";
    }

    /// <summary>入力欄の内容で役職履歴を追加（<paramref name="isNew"/>）または選択行を更新する。</summary>
    private async Task SaveAsync(bool isNew)
    {
        try
        {
            string text = _txtPosition.Text.Trim();
            if (text.Length == 0) { MessageBox.Show(this, "役職を入れてください。"); return; }
            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(_txtDate.Text))
            {
                if (!DateTime.TryParseExact(_txtDate.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                { MessageBox.Show(this, "異動日は yyyy-MM-dd で入れてください（日付不明なら空にする）。"); return; }
                date = d;
            }

            PersonPosition target;
            if (isNew)
            {
                target = new PersonPosition { PersonId = _personId, CreatedBy = Environment.UserName };
            }
            else
            {
                if (_grid.CurrentRow?.DataBoundItem is not PositionRow row || _grid.SelectedRows.Count == 0)
                { MessageBox.Show(this, "更新する行を選んでください。"); return; }
                target = row.Position;
            }

            target.CompanyId = _txtCompany.Tag as int?;
            target.EffectiveDate = date;
            target.PositionText = text;
            target.SourceUrl = string.IsNullOrWhiteSpace(_txtSourceUrl.Text) ? null : _txtSourceUrl.Text.Trim();
            target.DisplayOrder = (int)_nudOrder.Value;
            target.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
            target.UpdatedBy = Environment.UserName;

            if (isNew) await _positionsRepo.InsertAsync(target);
            else await _positionsRepo.UpdateAsync(target);

            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteAsync()
    {
        try
        {
            if (_grid.CurrentRow?.DataBoundItem is not PositionRow row || _grid.SelectedRows.Count == 0)
            { MessageBox.Show(this, "削除する行を選んでください。"); return; }
            if (MessageBox.Show(this, $"「{row.Date} {row.Text}」を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _positionsRepo.DeleteAsync(row.Position.PositionId);
            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>グリッドの 1 行。見出しは <see cref="ColumnHeaders"/> で付ける。</summary>
    private sealed class PositionRow
    {
        public PersonPosition Position { get; init; } = new();
        public int Order { get; init; }
        public string Date { get; init; } = "";
        public string Company { get; init; } = "";
        public string Text { get; init; } = "";
        public string SourceUrl { get; init; } = "";
    }

    /// <summary>グリッドの列名 → 見出し。</summary>
    private static readonly (string Column, string Header)[] ColumnHeaders =
    {
        (nameof(PositionRow.Order), "並び順"),
        (nameof(PositionRow.Date), "異動日"),
        (nameof(PositionRow.Company), "会社"),
        (nameof(PositionRow.Text), "役職"),
        (nameof(PositionRow.SourceUrl), "出典（内部）"),
    };
}
