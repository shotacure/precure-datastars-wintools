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
/// 人物または企業のプロフィールの出典（entity_sources）の編集ダイアログ。
/// 選んだ人物・企業の出典を一覧し、裏付ける項目・ページタイトル・見出し・発行者・URL・確認日で追加・更新・削除する。
/// マスタ編集なので、追加・更新・削除はボタンを押した時点で DB に反映する（保存ボタンは無い）。
/// <list type="bullet">
///   <item>裏付ける項目：この出典で確かめた項目のチェック。サイトの本文でその項目に脚注の番号が付く</item>
///   <item>ページタイトル・見出し・発行者：ページの &lt;title&gt;・情報が載っている欄の見出し・著作権表記の名前を、一字一句そのまま写す</item>
///   <item>URL：開いたページの URL（見出しへの # 付きでもよい）。確認日：そのページで確かめた日</item>
/// </list>
/// </summary>
public sealed class EntitySourcesEditorDialog : Form
{
    private readonly EntitySourcesRepository _sourcesRepo;
    private readonly int? _personId;
    private readonly int? _companyId;

    private readonly DataGridView _grid = new() { Dock = DockStyle.Top, Height = 220 };
    private readonly FlowLayoutPanel _pnlSupports = new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
    private readonly List<(string Code, CheckBox Box)> _supportBoxes = new();
    private readonly TextBox _txtPageTitle = new();
    private readonly TextBox _txtSectionTitle = new();
    private readonly TextBox _txtPublisher = new();
    private readonly TextBox _txtUrl = new();
    private readonly DateTimePicker _dtpCheckedOn = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd" };
    private readonly NumericUpDown _nudOrder = new() { Minimum = 0, Maximum = 999 };
    private readonly TextBox _txtNotes = new();
    private readonly Button _btnAdd = new() { Text = "新規追加" };
    private readonly Button _btnUpdate = new() { Text = "選択行を更新" };
    private readonly Button _btnDelete = new() { Text = "選択行を削除" };
    private readonly Button _btnClear = new() { Text = "入力欄を空にする" };
    private readonly Button _btnClose = new() { Text = "閉じる" };

    private List<SourceRow> _rows = new();

    /// <summary>人物の出典を編集するダイアログを作る。</summary>
    public static EntitySourcesEditorDialog ForPerson(EntitySourcesRepository repo, int personId, string personName)
        => new(repo, personId, null, $"出典の編集（人物）：#{personId} {personName}");

    /// <summary>企業の出典を編集するダイアログを作る。</summary>
    public static EntitySourcesEditorDialog ForCompany(EntitySourcesRepository repo, int companyId, string companyName)
        => new(repo, null, companyId, $"出典の編集（企業）：#{companyId} {companyName}");

    private EntitySourcesEditorDialog(EntitySourcesRepository sourcesRepo, int? personId, int? companyId, string title)
    {
        _sourcesRepo = sourcesRepo ?? throw new ArgumentNullException(nameof(sourcesRepo));
        _personId = personId;
        _companyId = companyId;

        // 裏付ける項目のチェックは、人物なら読み・誕生日・没年、企業なら読み・設立・解散・メンバー。
        foreach (var (code, label) in personId.HasValue ? EntitySourceFields.ForPerson : EntitySourceFields.ForCompany)
        {
            var box = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(0, 2, 16, 0) };
            _supportBoxes.Add((code, box));
            _pnlSupports.Controls.Add(box);
        }

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 560);
        MinimizeBox = false; MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        BuildLayout();

        Load += async (_, _) => { await ReloadAsync(); ClearInputs(); };
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

        AddRow("裏付ける項目", _pnlSupports, 12, 480);
        AddRow("ページタイトル", _txtPageTitle, 44, 480);
        AddRow("見出し", _txtSectionTitle, 76, 480);
        AddRow("発行者", _txtPublisher, 108, 480);
        AddHint("どれもページの表記を一字一句そのまま（<title>・欄の見出し・© の名前）。見出しは任意", 136, 132, 560);
        AddRow("URL", _txtUrl, 160, 480);
        AddRow("確認日", _dtpCheckedOn, 192, 120);
        AddRow("並び順", _nudOrder, 224, 70);
        AddHint("小さい順", 214, 224, 200);
        AddRow("メモ（内部）", _txtNotes, 256, 480);

        _btnAdd.SetBounds(720, 12, 150, 28);
        _btnUpdate.SetBounds(720, 44, 150, 28);
        _btnDelete.SetBounds(720, 76, 150, 28);
        _btnClear.SetBounds(720, 108, 150, 28);
        _btnClose.SetBounds(720, 256, 150, 28);
        pnl.Controls.AddRange(new Control[] { _btnAdd, _btnUpdate, _btnDelete, _btnClear, _btnClose });

        Controls.Add(pnl);
        Controls.Add(_grid);
    }

    /// <summary>この人物・企業の出典を読み直して画面に反映する。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var sources = _personId is int pid
                ? await _sourcesRepo.GetByPersonAsync(pid)
                : await _sourcesRepo.GetByCompanyAsync(_companyId!.Value);
            _rows = sources.Select(ToRow).ToList();
            _grid.DataSource = _rows.ToList();
            if (_grid.Columns.Contains(nameof(SourceRow.Source))) _grid.Columns[nameof(SourceRow.Source)]!.Visible = false;
            foreach (var (column, header) in ColumnHeaders)
            {
                if (_grid.Columns.Contains(column)) _grid.Columns[column]!.HeaderText = header;
            }
            _grid.ClearSelection();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private static SourceRow ToRow(EntitySource s) => new()
    {
        Source = s,
        Order = s.DisplayOrder,
        Supports = SupportsLabel(s),
        PageTitle = s.PageTitle,
        SectionTitle = s.SectionTitle ?? "",
        Url = s.Url,
        CheckedOn = s.CheckedOn.ToString("yyyy-MM-dd")
    };

    private void OnRowSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not SourceRow row || _grid.SelectedRows.Count == 0) return;
        var s = row.Source;
        var codes = s.SupportCodes();
        foreach (var (code, box) in _supportBoxes) box.Checked = codes.Contains(code);
        _txtPageTitle.Text = s.PageTitle;
        _txtSectionTitle.Text = s.SectionTitle ?? "";
        _txtPublisher.Text = s.Publisher;
        _txtUrl.Text = s.Url;
        _dtpCheckedOn.Value = s.CheckedOn.Date;
        _nudOrder.Value = Math.Clamp(s.DisplayOrder, (int)_nudOrder.Minimum, (int)_nudOrder.Maximum);
        _txtNotes.Text = s.Notes ?? "";
    }

    /// <summary>入力欄を空にし、確認日に今日、並び順に「今ある行の最大 + 1」を入れる（新規追加の準備）。</summary>
    private void ClearInputs()
    {
        _grid.ClearSelection();
        foreach (var (_, box) in _supportBoxes) box.Checked = false;
        _txtPageTitle.Text = "";
        _txtSectionTitle.Text = "";
        _txtPublisher.Text = "";
        _txtUrl.Text = "";
        _dtpCheckedOn.Value = DateTime.Today;
        _nudOrder.Value = Math.Min(_nudOrder.Maximum, _rows.Count == 0 ? 0 : _rows.Max(r => r.Order) + 1);
        _txtNotes.Text = "";
    }

    /// <summary>入力欄の内容で出典を追加（<paramref name="isNew"/>）または選択行を更新する。</summary>
    private async Task SaveAsync(bool isNew)
    {
        try
        {
            // ページタイトルは <title> のまま持つので、前後の空白も含めて削らない（空かどうかだけ見る）。
            string pageTitle = _txtPageTitle.Text;
            string publisher = _txtPublisher.Text.Trim();
            string url = _txtUrl.Text.Trim();
            if (pageTitle.Trim().Length == 0 || publisher.Length == 0 || url.Length == 0)
            { MessageBox.Show(this, "ページタイトル・発行者・URL を入れてください。"); return; }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            { MessageBox.Show(this, "URL は http:// か https:// で始まる形で入れてください。"); return; }

            EntitySource target;
            if (isNew)
            {
                target = new EntitySource { PersonId = _personId, CompanyId = _companyId, CreatedBy = Environment.UserName };
            }
            else
            {
                if (_grid.CurrentRow?.DataBoundItem is not SourceRow row || _grid.SelectedRows.Count == 0)
                { MessageBox.Show(this, "更新する行を選んでください。"); return; }
                target = row.Source;
            }

            target.Supports = string.Join(",", _supportBoxes.Where(b => b.Box.Checked).Select(b => b.Code));
            target.PageTitle = pageTitle;
            target.SectionTitle = string.IsNullOrWhiteSpace(_txtSectionTitle.Text) ? null : _txtSectionTitle.Text.Trim();
            target.Publisher = publisher;
            target.Url = url;
            target.CheckedOn = _dtpCheckedOn.Value.Date;
            target.DisplayOrder = (int)_nudOrder.Value;
            target.Notes = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
            target.UpdatedBy = Environment.UserName;

            if (isNew) await _sourcesRepo.InsertAsync(target);
            else await _sourcesRepo.UpdateAsync(target);

            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteAsync()
    {
        try
        {
            if (_grid.CurrentRow?.DataBoundItem is not SourceRow row || _grid.SelectedRows.Count == 0)
            { MessageBox.Show(this, "削除する行を選んでください。"); return; }
            if (MessageBox.Show(this, $"「{row.PageTitle}」（{row.Url}）を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _sourcesRepo.DeleteAsync(row.Source.SourceId);
            await ReloadAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>裏付ける項目のコードを表示名にして「・」でつなぐ（グリッド用）。</summary>
    private static string SupportsLabel(EntitySource s)
    {
        var labels = EntitySourceFields.ForPerson.Concat(EntitySourceFields.ForCompany)
            .GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);
        return string.Join("・", s.SupportCodes().Select(c => labels.TryGetValue(c, out var l) ? l : c));
    }

    /// <summary>グリッドの 1 行。見出しは <see cref="ColumnHeaders"/> で付ける。</summary>
    private sealed class SourceRow
    {
        public EntitySource Source { get; init; } = new();
        public int Order { get; init; }
        public string Supports { get; init; } = "";
        public string PageTitle { get; init; } = "";
        public string SectionTitle { get; init; } = "";
        public string Url { get; init; } = "";
        public string CheckedOn { get; init; } = "";
    }

    /// <summary>グリッドの列名 → 見出し。</summary>
    private static readonly (string Column, string Header)[] ColumnHeaders =
    {
        (nameof(SourceRow.Order), "並び順"),
        (nameof(SourceRow.Supports), "裏付ける項目"),
        (nameof(SourceRow.PageTitle), "ページタイトル"),
        (nameof(SourceRow.SectionTitle), "見出し"),
        (nameof(SourceRow.Url), "URL"),
        (nameof(SourceRow.CheckedOn), "確認日"),
    };
}
