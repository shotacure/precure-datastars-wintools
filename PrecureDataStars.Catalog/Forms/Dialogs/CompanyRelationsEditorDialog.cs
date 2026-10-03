using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 団体どうしの関係（company_relations）の編集ダイアログ。中心の団体から見た関係を一覧し、
/// 「親（所属先）」「子（部署・子会社など）」「前身」「後継」の 4 方向で追加・更新・削除する。
/// マスタ編集なので、追加・更新・削除はボタンを押した時点で DB に反映する（保存ボタンは無い）。
/// <list type="bullet">
///   <item>親：relation_kind=PARENT、from=相手、to=この団体</item>
///   <item>子：relation_kind=PARENT、from=この団体、to=相手</item>
///   <item>前身：relation_kind=SUCCESSOR、from=相手、to=この団体</item>
///   <item>後継：relation_kind=SUCCESSOR、from=この団体、to=相手</item>
/// </list>
/// 同じ会社の改名は屋号の前後リンクで扱うので、このダイアログでは扱わない。
/// </summary>
public sealed class CompanyRelationsEditorDialog : Form
{
    /// <summary>この団体から見た関係の向き。</summary>
    private enum Direction { Parent, Child, Predecessor, Successor }

    private static readonly (Direction Dir, string Label)[] DirectionItems =
    {
        (Direction.Parent, "親（この団体の所属先）"),
        (Direction.Child, "子（この団体の部署・子会社など）"),
        (Direction.Predecessor, "前身（この団体が事業を引き継いだ元）"),
        (Direction.Successor, "後継（この団体の事業を引き継いだ先）"),
    };

    private readonly CompanyRelationsRepository _relationsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly int _companyId;

    private readonly DataGridView _grid = new() { Dock = DockStyle.Top, Height = 220 };
    private readonly ComboBox _cboDirection = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cboOther = new() { DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly TextBox _txtLabel = new();
    private readonly TextBox _txtFrom = new();
    private readonly TextBox _txtTo = new();
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnAdd = new() { Text = "新規追加" };
    private readonly Button _btnUpdate = new() { Text = "選択行を更新" };
    private readonly Button _btnDelete = new() { Text = "選択行を削除" };
    private readonly Button _btnClose = new() { Text = "閉じる" };

    private Dictionary<int, string> _companyNameById = new();
    private List<RelationRow> _rows = new();

    /// <summary>ダイアログを構築する。<paramref name="companyId"/> がこのダイアログの中心の団体。</summary>
    public CompanyRelationsEditorDialog(
        CompanyRelationsRepository relationsRepo,
        CompaniesRepository companiesRepo,
        int companyId,
        string companyName)
    {
        _relationsRepo = relationsRepo ?? throw new ArgumentNullException(nameof(relationsRepo));
        _companiesRepo = companiesRepo ?? throw new ArgumentNullException(nameof(companiesRepo));
        _companyId = companyId;

        Text = $"団体の関係の編集：#{companyId} {companyName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(820, 470);
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        BuildLayout();

        Load += async (_, _) => await ReloadAsync();
        _grid.SelectionChanged += (_, _) => OnRowSelected();
        _btnAdd.Click += async (_, _) => await SaveAsync(isNew: true);
        _btnUpdate.Click += async (_, _) => await SaveAsync(isNew: false);
        _btnDelete.Click += async (_, _) => await DeleteAsync();
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

        _cboDirection.Items.AddRange(DirectionItems.Select(d => (object)d.Label).ToArray());
        _cboDirection.SelectedIndex = 0;
        AddRow("関係", _cboDirection, 12, 300);
        AddRow("相手の団体", _cboOther, 44, 420);
        AddRow("言い回し", _txtLabel, 76, 200);
        pnl.Controls.Add(new Label { Text = "部署・子会社・雑誌・会社分割など。空なら既定の言葉", Location = new Point(344, 80), Size = new Size(320, 20), ForeColor = SystemColors.GrayText });
        AddRow("開始日", _txtFrom, 108, 120);
        AddRow("終了日", _txtTo, 140, 120);
        pnl.Controls.Add(new Label { Text = "yyyy-MM-dd（分からなければ空）", Location = new Point(264, 112), Size = new Size(240, 20), ForeColor = SystemColors.GrayText });
        AddRow("メモ", _txtNotes, 172, 420);

        _btnAdd.SetBounds(640, 12, 150, 28);
        _btnUpdate.SetBounds(640, 44, 150, 28);
        _btnDelete.SetBounds(640, 76, 150, 28);
        _btnClose.SetBounds(640, 172, 150, 28);
        pnl.Controls.AddRange(new Control[] { _btnAdd, _btnUpdate, _btnDelete, _btnClose });

        Controls.Add(pnl);
        Controls.Add(_grid);
    }

    /// <summary>団体の一覧と、この団体の関係を読み直して画面に反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var companies = await _companiesRepo.GetAllAsync();
            _companyNameById = companies.ToDictionary(c => c.CompanyId, c => c.Name);
            var others = companies.Where(c => c.CompanyId != _companyId)
                .Select(c => new IdLabel<int>(c.CompanyId, $"#{c.CompanyId}  {c.Name}"))
                .ToList();
            _cboOther.DisplayMember = "Label";
            _cboOther.ValueMember = "Id";
            _cboOther.DataSource = others;
            _cboOther.SelectedIndex = -1;

            var relations = await _relationsRepo.GetByCompanyAsync(_companyId);
            _rows = relations.Select(ToRow).ToList();
            _grid.DataSource = _rows.ToList();
            if (_grid.Columns.Contains(nameof(RelationRow.Relation))) _grid.Columns[nameof(RelationRow.Relation)]!.Visible = false;
            foreach (var (column, header) in ColumnHeaders)
            {
                if (_grid.Columns.Contains(column)) _grid.Columns[column]!.HeaderText = header;
            }
            _grid.ClearSelection();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>DB の 1 行を、この団体から見た表示行に変換する。</summary>
    private RelationRow ToRow(CompanyRelation r)
    {
        bool selfIsFrom = r.FromCompanyId == _companyId;
        var dir = (r.RelationKind, selfIsFrom) switch
        {
            (CompanyRelationKinds.Parent, false) => Direction.Parent,
            (CompanyRelationKinds.Parent, true) => Direction.Child,
            (CompanyRelationKinds.Successor, false) => Direction.Predecessor,
            _ => Direction.Successor
        };
        int otherId = selfIsFrom ? r.ToCompanyId : r.FromCompanyId;
        return new RelationRow
        {
            Relation = r,
            DirectionLabel = DirectionItems.First(d => d.Dir == dir).Label,
            OtherName = _companyNameById.TryGetValue(otherId, out var n) ? $"#{otherId} {n}" : $"#{otherId}",
            Label = r.RelationLabel ?? "",
            From = r.ValidFrom?.ToString("yyyy-MM-dd") ?? "",
            To = r.ValidTo?.ToString("yyyy-MM-dd") ?? "",
            Notes = r.Notes ?? ""
        };
    }

    private void OnRowSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RelationRow row || _grid.SelectedRows.Count == 0) return;
        var r = row.Relation;
        bool selfIsFrom = r.FromCompanyId == _companyId;
        _cboDirection.SelectedIndex = Array.FindIndex(DirectionItems, d => d.Label == row.DirectionLabel);
        _cboOther.SelectedValue = selfIsFrom ? r.ToCompanyId : r.FromCompanyId;
        _txtLabel.Text = r.RelationLabel ?? "";
        _txtFrom.Text = r.ValidFrom?.ToString("yyyy-MM-dd") ?? "";
        _txtTo.Text = r.ValidTo?.ToString("yyyy-MM-dd") ?? "";
        _txtNotes.Text = r.Notes ?? "";
    }

    /// <summary>入力欄の内容で関係を追加（<paramref name="isNew"/>）または選択行を更新する。</summary>
    private async Task SaveAsync(bool isNew)
    {
        try
        {
            if (_cboOther.SelectedValue is not int otherId)
            { MessageBox.Show(this, "相手の団体を一覧から選んでください。"); return; }
            if (!TryParseDate(_txtFrom.Text, out var from) || !TryParseDate(_txtTo.Text, out var to))
            { MessageBox.Show(this, "日付は yyyy-MM-dd の形で入れてください。"); return; }
            if (from.HasValue && to.HasValue && from > to)
            { MessageBox.Show(this, "開始日が終了日より後になっています。"); return; }

            var dir = DirectionItems[Math.Max(0, _cboDirection.SelectedIndex)].Dir;
            bool selfIsFrom = dir is Direction.Child or Direction.Successor;

            CompanyRelation target;
            if (isNew)
            {
                target = new CompanyRelation { CreatedBy = Environment.UserName };
            }
            else
            {
                if (_grid.CurrentRow?.DataBoundItem is not RelationRow row || _grid.SelectedRows.Count == 0)
                { MessageBox.Show(this, "更新する行を選んでください。"); return; }
                target = row.Relation;
            }

            target.RelationKind = dir is Direction.Parent or Direction.Child
                ? CompanyRelationKinds.Parent
                : CompanyRelationKinds.Successor;
            target.FromCompanyId = selfIsFrom ? _companyId : otherId;
            target.ToCompanyId = selfIsFrom ? otherId : _companyId;
            target.RelationLabel = string.IsNullOrWhiteSpace(_txtLabel.Text) ? null : _txtLabel.Text.Trim();
            target.ValidFrom = from;
            target.ValidTo = to;
            target.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
            target.UpdatedBy = Environment.UserName;

            if (isNew) await _relationsRepo.InsertAsync(target);
            else await _relationsRepo.UpdateAsync(target);

            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteAsync()
    {
        try
        {
            if (_grid.CurrentRow?.DataBoundItem is not RelationRow row || _grid.SelectedRows.Count == 0)
            { MessageBox.Show(this, "削除する行を選んでください。"); return; }
            if (MessageBox.Show(this, $"「{row.DirectionLabel}：{row.OtherName}」の関係を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _relationsRepo.DeleteAsync(row.Relation.RelationId);
            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>空なら null、yyyy-MM-dd / yyyy/M/d なら日付として読む。読めなければ false。</summary>
    private static bool TryParseDate(string text, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (DateTime.TryParseExact(text.Trim(), new[] { "yyyy-MM-dd", "yyyy-M-d", "yyyy/MM/dd", "yyyy/M/d" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            value = d.Date;
            return true;
        }
        return false;
    }

    /// <summary>グリッドの 1 行（この団体から見た関係）。見出しは <see cref="ColumnHeaders"/> で付ける。</summary>
    private sealed class RelationRow
    {
        public CompanyRelation Relation { get; init; } = new();
        public string DirectionLabel { get; init; } = "";
        public string OtherName { get; init; } = "";
        public string Label { get; init; } = "";
        public string From { get; init; } = "";
        public string To { get; init; } = "";
        public string Notes { get; init; } = "";
    }

    /// <summary>グリッドの列名 → 見出し。</summary>
    private static readonly (string Column, string Header)[] ColumnHeaders =
    {
        (nameof(RelationRow.DirectionLabel), "関係"),
        (nameof(RelationRow.OtherName), "相手の団体"),
        (nameof(RelationRow.Label), "言い回し"),
        (nameof(RelationRow.From), "開始日"),
        (nameof(RelationRow.To), "終了日"),
        (nameof(RelationRow.Notes), "メモ"),
    };
}
