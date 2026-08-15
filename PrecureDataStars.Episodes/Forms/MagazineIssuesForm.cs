using MySqlConnector;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using System.ComponentModel;
using System.Globalization;

namespace PrecureDataStars.Episodes.Forms;

/// <summary>
/// アニメ雑誌の号マスタ（magazine_issues）管理ダイアログ。
/// 年・月・発売日の 3 列グリッドで一覧編集し、「保存」で DB へ一括反映する
/// （画面から消えた号は DELETE、残っている号は (年, 月) キーで upsert）。
/// 次号の発売予定日を先行登録しておくことで、最新号がカバーするエピソードまで
/// 担当号を確定できる（サイト側のセクション表示条件も同じ解決ルール）。
/// 小型の補助ダイアログのためレイアウトはコード組み立て（Designer なし）。
/// </summary>
public sealed class MagazineIssuesForm : Form
{
    private readonly MagazineIssuesRepository _repo;

    /// <summary>グリッド 1 行分のビューモデル。発売日はテキスト（yyyy/M/d）で保持し、保存時に日付検証する。</summary>
    private sealed class IssueRow
    {
        public int IssueYear { get; set; }
        public int IssueMonth { get; set; }
        public string ReleaseDateText { get; set; } = "";
    }

    /// <summary>発売日テキストの受理フォーマット（年が先頭なので月日の並び解釈の曖昧さが無い）。</summary>
    private static readonly string[] DateFormats = { "yyyy/M/d", "yyyy-M-d", "yyyy.M.d" };

    private readonly BindingList<IssueRow> _rows = new();

    // 読み込み時点で DB に存在した号の PK 集合。保存時に「画面から消えた号」の DELETE 対象を求める。
    private readonly HashSet<(int Year, int Month)> _loadedKeys = new();

    private readonly DataGridView _grid = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnDelete = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnClose = new();

    // 未保存編集フラグ。保存せずに閉じようとしたときの確認に使う。
    private bool _isDirty;

    /// <summary><see cref="MagazineIssuesForm"/> の新しいインスタンスを生成する。</summary>
    /// <param name="repo">号マスタリポジトリ。</param>
    /// <exception cref="ArgumentNullException"></exception>
    public MagazineIssuesForm(MagazineIssuesRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));

        Text = "アニメ雑誌 号マスタ";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 560);
        MinimumSize = new Size(480, 360);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        // グリッド：年 / 月号 / 発売日 の 3 列。発売日はテキスト編集し保存時に検証する。
        _grid.Bounds = new Rectangle(12, 12, 576, 490);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "年", DataPropertyName = nameof(IssueRow.IssueYear), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "月号", DataPropertyName = nameof(IssueRow.IssueMonth), Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "発売日 (yyyy/m/d)", DataPropertyName = nameof(IssueRow.ReleaseDateText), Width = 300 });
        _grid.DataSource = _rows;
        _grid.CellValueChanged += (_, __) => _isDirty = true;
        _grid.DataError += (_, e) => e.ThrowException = false; // 数値列への非数値入力等は落とさず無視

        _btnAdd.Text = "行を追加";
        _btnAdd.Bounds = new Rectangle(12, 514, 100, 34);
        _btnAdd.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnAdd.Click += (_, __) => AddRow();

        _btnDelete.Text = "選択削除";
        _btnDelete.Bounds = new Rectangle(118, 514, 100, 34);
        _btnDelete.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _btnDelete.Click += (_, __) => DeleteSelectedRow();

        _btnSave.Text = "保存";
        _btnSave.Bounds = new Rectangle(382, 514, 100, 34);
        _btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnSave.Click += async (_, __) => await SaveAsync();

        _btnClose.Text = "閉じる";
        _btnClose.Bounds = new Rectangle(488, 514, 100, 34);
        _btnClose.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        _btnClose.Click += (_, __) => Close();

        Controls.Add(_grid);
        Controls.Add(_btnAdd);
        Controls.Add(_btnDelete);
        Controls.Add(_btnSave);
        Controls.Add(_btnClose);

        Load += async (_, __) => await LoadAsync();

        // 未保存編集があるまま閉じようとしたら確認する（エディタ本体と同方針）。
        FormClosing += (_, e) =>
        {
            if (!_isDirty) return;
            var r = MessageBox.Show(this,
                "保存していない変更があります。保存せずに閉じますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.Yes) e.Cancel = true;
        };
    }

    /// <summary>号マスタを DB から読み込み、グリッドへ反映する（号の年月昇順）。</summary>
    private async Task LoadAsync()
    {
        var issues = await _repo.GetAllAsync();

        _rows.Clear();
        _loadedKeys.Clear();
        foreach (var issue in issues)
        {
            _rows.Add(new IssueRow
            {
                IssueYear = issue.IssueYear,
                IssueMonth = issue.IssueMonth,
                ReleaseDateText = issue.ReleaseDate.ToString("yyyy/M/d", CultureInfo.InvariantCulture)
            });
            _loadedKeys.Add((issue.IssueYear, issue.IssueMonth));
        }
        _isDirty = false;
    }

    /// <summary>新しい号の行を追加する。最終行を基準に「翌月号 / 発売日 +1 か月」を提案値として入れる。</summary>
    private void AddRow()
    {
        var last = _rows.LastOrDefault();

        int year, month;
        string releaseText;
        if (last is null)
        {
            // 初回登録の提案値：今日を基準に「今月号 / 発売日 = 今日」を仮置きする（編集前提）。
            var today = DateTime.Today;
            year = today.Year;
            month = today.Month;
            releaseText = today.ToString("yyyy/M/d", CultureInfo.InvariantCulture);
        }
        else
        {
            year = last.IssueYear;
            month = last.IssueMonth + 1;
            if (month == 13) { month = 1; year++; }
            releaseText = TryParseDate(last.ReleaseDateText, out var lastRelease)
                ? lastRelease.AddMonths(1).ToString("yyyy/M/d", CultureInfo.InvariantCulture)
                : "";
        }

        _rows.Add(new IssueRow { IssueYear = year, IssueMonth = month, ReleaseDateText = releaseText });
        _isDirty = true;

        // 追加行を選択状態にして編集しやすくする。
        int newIndex = _rows.Count - 1;
        _grid.ClearSelection();
        _grid.Rows[newIndex].Selected = true;
        _grid.CurrentCell = _grid.Rows[newIndex].Cells[2];
    }

    /// <summary>選択中の号の行をグリッドから削除する（DB からの削除は保存時に確定する）。</summary>
    private void DeleteSelectedRow()
    {
        if (_grid.CurrentRow is null || _grid.CurrentRow.Index < 0 || _grid.CurrentRow.Index >= _rows.Count) return;
        _rows.RemoveAt(_grid.CurrentRow.Index);
        _isDirty = true;
    }

    /// <summary>グリッド内容を検証して DB へ一括反映する。 画面から消えた号の DELETE を先に流し（発売日 UNIQUE の付け替え衝突を減らすため）、 残った号を (年, 月) キーで upsert する。成功後は再読込して号の年月昇順に整列する。</summary>
    private async Task SaveAsync()
    {
        _grid.EndEdit();

        var seenKeys = new HashSet<(int Year, int Month)>();
        var seenDates = new HashSet<DateTime>();
        var issues = new List<MagazineIssue>();
        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            string rowLabel = $"{i + 1} 行目";

            if (row.IssueYear < 1900 || row.IssueYear > 2999)
            {
                ShowValidationError($"{rowLabel}: 年が不正です（1900〜2999）。");
                return;
            }
            if (row.IssueMonth < 1 || row.IssueMonth > 12)
            {
                ShowValidationError($"{rowLabel}: 月号が不正です（1〜12）。");
                return;
            }
            if (!TryParseDate(row.ReleaseDateText, out var release))
            {
                ShowValidationError($"{rowLabel}: 発売日を yyyy/m/d 形式で入力してください。");
                return;
            }
            if (!seenKeys.Add((row.IssueYear, row.IssueMonth)))
            {
                ShowValidationError($"{rowLabel}: {row.IssueYear}年{row.IssueMonth}月号 が重複しています。");
                return;
            }
            if (!seenDates.Add(release))
            {
                ShowValidationError($"{rowLabel}: 発売日 {release:yyyy/M/d} が別の号と重複しています。");
                return;
            }

            issues.Add(new MagazineIssue { IssueYear = row.IssueYear, IssueMonth = row.IssueMonth, ReleaseDate = release });
        }

        try
        {
            foreach (var (year, month) in _loadedKeys.Where(k => !seenKeys.Contains(k)))
            {
                await _repo.DeleteAsync(year, month);
            }
            foreach (var issue in issues)
            {
                await _repo.UpsertAsync(issue);
            }
        }
        // MySQL Error 1062 = Duplicate entry（発売日 UNIQUE と既存行の衝突）
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            MessageBox.Show(this,
                "発売日が既存の号と重複しています。値を見直してください。",
                "重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await LoadAsync();
        MessageBox.Show(this, "保存しました。", "号マスタ", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>発売日テキストを受理フォーマット（年先頭の 3 形式）で厳密パースする。</summary>
    private static bool TryParseDate(string text, out DateTime date)
        => DateTime.TryParseExact((text ?? "").Trim(), DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    /// <summary>検証エラーの警告ダイアログを出す。</summary>
    private void ShowValidationError(string message)
        => MessageBox.Show(this, message, "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
