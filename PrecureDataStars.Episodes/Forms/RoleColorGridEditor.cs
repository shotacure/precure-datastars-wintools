using PrecureDataStars.Data.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Episodes.Forms;

/// <summary>
/// 「役職と色」の行を並べて編集する欄（シリーズ編集の「メインスタッフ」「エピソードの札」）の操作をまとめた部品。
/// 渡された <see cref="DataGridView"/> に役職のドロップダウンと色（#rrggbb）の 2 列を組み、
/// 行を足す・消す・上下に並べ替える・色を選ぶ（色のセルのダブルクリックでも）ボタンをつなぐ。
/// 役職を選んで色が空なら、その役職のいつもの色（ほかの作品でいちばん多く使われている色）を入れる。
/// 行の順がサイトでの並びになる。DB への読み書きは持たず、呼び出し側が行を渡し、<see cref="Collect"/> で受け取って保存する。
/// </summary>
internal sealed class RoleColorGridEditor
{
    /// <summary>役職の列の名前。</summary>
    private const string RoleColumn = "colRole";
    /// <summary>色の列の名前。</summary>
    private const string ColorColumn = "colColor";
    /// <summary>色の書式（#rrggbb）。</summary>
    private static readonly Regex ColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    private readonly IWin32Window _owner;
    private readonly DataGridView _grid;
    /// <summary>入力の誤りを知らせる文に出す欄の名前（「メインスタッフ」など）。</summary>
    private readonly string _caption;
    /// <summary>役職ごとに、ほかの作品でいちばん多く使われている色（役職を選んだときの色の初期値）。</summary>
    private Dictionary<string, string> _usualColorByRole = new(StringComparer.Ordinal);
    /// <summary>欄の読み込みの世代。読み込み中に別の作品を選んだら、古い読み込みの結果を捨てる。</summary>
    private int _loadGen;

    /// <summary>役職のドロップダウンの選択肢（表示は「役職名（コード）」、値は役職コード）。</summary>
    private sealed class RoleOption
    {
        public string Value { get; init; } = "";
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary><see cref="RoleColorGridEditor"/> の新しいインスタンスを生成し、グリッドとボタンのイベントをつなぐ。</summary>
    /// <param name="owner">色の選択ダイアログの親。</param>
    /// <param name="grid">編集する欄。</param>
    /// <param name="caption">入力の誤りを知らせる文に出す欄の名前。</param>
    /// <param name="add">行を足すボタン。</param>
    /// <param name="remove">行を消すボタン。</param>
    /// <param name="up">行を上へ動かすボタン。</param>
    /// <param name="down">行を下へ動かすボタン。</param>
    /// <param name="color">色を選ぶボタン。</param>
    public RoleColorGridEditor(IWin32Window owner, DataGridView grid, string caption,
        Button add, Button remove, Button up, Button down, Button color)
    {
        _owner = owner;
        _grid = grid;
        _caption = caption;

        add.Click += (_, __) => AddRow();
        remove.Click += (_, __) => RemoveRow();
        up.Click += (_, __) => MoveRow(-1);
        down.Click += (_, __) => MoveRow(+1);
        color.Click += (_, __) => PickColor();
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellValueChanged += Grid_CellValueChanged;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && IsColorColumn(e.ColumnIndex)) PickColor(); };
        // ドロップダウンで選んだ値をすぐ確定させ、色の初期値を入れられるようにする
        _grid.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    /// <summary>列が組まれているか（役職マスタの読み込み後）。</summary>
    public bool HasColumns => _grid.Columns.Count > 0;

    /// <summary>
    /// 欄の列を組む。役職の列は役職マスタのドロップダウン（表示は役職名、値は役職コード）、
    /// 色の列は #rrggbb の文字（セルの地にその色を敷く）。
    /// </summary>
    public void BuildColumns(IReadOnlyList<Role> roles)
    {
        _grid.Columns.Clear();
        var roleOptions = roles
            .Select(r => new RoleOption { Value = r.RoleCode, Text = $"{r.NameJa}（{r.RoleCode}）" })
            .ToList();
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = RoleColumn,
            HeaderText = "役職",
            DataSource = roleOptions,
            ValueMember = nameof(RoleOption.Value),
            DisplayMember = nameof(RoleOption.Text),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = ColorColumn,
            HeaderText = "色",
            Width = 100,
            MaxInputLength = 7,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    /// <summary>役職ごとのいつもの色を、全作品の行（役職コードと色）から決める。</summary>
    public void SetUsualColors(IEnumerable<(string RoleCode, string BadgeColor)> allRows)
    {
        _usualColorByRole = allRows
            .GroupBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.BadgeColor.ToLowerInvariant()).OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First().Key,
                StringComparer.Ordinal);
    }

    /// <summary>欄を空にする（読み込み中の結果も捨てる）。</summary>
    public void Clear()
    {
        Interlocked.Increment(ref _loadGen);
        _grid.Rows.Clear();
    }

    /// <summary>
    /// 行を読み込んで欄に並べる。読み込み中に別の作品を選んだら、結果を捨てる。
    /// 列がまだ組まれていなければ（役職マスタの読み込み前）何もしない。
    /// </summary>
    /// <param name="fetch">作品の行（役職コードと色、並び順の順）を読む処理。</param>
    public async Task LoadAsync(Func<Task<IEnumerable<(string RoleCode, string BadgeColor)>>> fetch)
    {
        int gen = Interlocked.Increment(ref _loadGen);
        _grid.Rows.Clear();
        if (!HasColumns) return;
        var rows = await fetch();
        if (gen != _loadGen) return;
        _grid.Rows.Clear();
        foreach (var (roleCode, badgeColor) in rows)
            _grid.Rows.Add(roleCode, badgeColor);
    }

    /// <summary>
    /// 欄を、保存する行（欄の上からの順）にする。役職が空・色が #rrggbb でない・同じ役職が 2 回あるときは
    /// 理由を返して保存しない。色は小文字にそろえる。
    /// </summary>
    public (List<(string RoleCode, string BadgeColor)>? Rows, string? Error) Collect()
    {
        _grid.EndEdit();
        var rows = new List<(string RoleCode, string BadgeColor)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow r in _grid.Rows)
        {
            int no = r.Index + 1;
            if (r.Cells[RoleColumn].Value is not string code || code.Length == 0)
                return (null, $"{_caption}の {no} 行目の役職が空です。");
            var color = (r.Cells[ColorColumn].Value as string ?? "").Trim();
            if (!ColorPattern.IsMatch(color))
                return (null, $"{_caption}の {no} 行目の色は #rrggbb の形で書いてください（いま「{color}」）。");
            if (!seen.Add(code))
                return (null, $"{_caption}の {no} 行目の役職（{code}）が、上の行と重なっています。");
            rows.Add((code, color.ToLowerInvariant()));
        }
        return (rows, null);
    }

    /// <summary>空の行を足して選ぶ（役職を選ぶと色の初期値が入る）。</summary>
    private void AddRow()
    {
        if (!HasColumns) return;
        int i = _grid.Rows.Add();
        _grid.CurrentCell = _grid.Rows[i].Cells[RoleColumn];
    }

    /// <summary>選んだ行を消す。</summary>
    private void RemoveRow()
    {
        if (_grid.CurrentRow is not { } row) return;
        _grid.Rows.Remove(row);
    }

    /// <summary>選んだ行を上（-1）か下（+1）へ 1 つ動かす。</summary>
    private void MoveRow(int delta)
    {
        if (_grid.CurrentRow is not { } row) return;
        int from = row.Index, to = from + delta;
        if (to < 0 || to >= _grid.Rows.Count) return;
        _grid.EndEdit();
        _grid.Rows.RemoveAt(from);
        _grid.Rows.Insert(to, row);
        _grid.CurrentCell = _grid.Rows[to].Cells[RoleColumn];
    }

    /// <summary>選んだ行の色を、色の選択ダイアログで選ぶ。</summary>
    private void PickColor()
    {
        if (_grid.CurrentRow is not { } row) return;
        var cell = row.Cells[ColorColumn];
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
        if (TryParseColor(cell.Value as string, out var current)) dlg.Color = current;
        if (dlg.ShowDialog(_owner) != DialogResult.OK) return;
        cell.Value = $"#{dlg.Color.R:x2}{dlg.Color.G:x2}{dlg.Color.B:x2}";
    }

    /// <summary>役職を選んで色が空なら、その役職のいつもの色を入れる。</summary>
    private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _grid.Columns[e.ColumnIndex].Name != RoleColumn) return;
        var row = _grid.Rows[e.RowIndex];
        var colorCell = row.Cells[ColorColumn];
        if (!string.IsNullOrWhiteSpace(colorCell.Value as string)) return;
        if (row.Cells[RoleColumn].Value is string code && _usualColorByRole.TryGetValue(code, out var usual))
            colorCell.Value = usual;
    }

    /// <summary>色の列のセルの地に、その色を敷く（読めない色なら敷かない）。</summary>
    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || !IsColorColumn(e.ColumnIndex) || e.CellStyle is null) return;
        if (!TryParseColor(e.Value as string, out var c)) return;
        e.CellStyle.BackColor = c;
        e.CellStyle.SelectionBackColor = c;
        // 字は地の明るさで白か黒にする
        var fore = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 >= 140 ? Color.Black : Color.White;
        e.CellStyle.ForeColor = fore;
        e.CellStyle.SelectionForeColor = fore;
    }

    private bool IsColorColumn(int columnIndex)
        => columnIndex >= 0 && _grid.Columns[columnIndex].Name == ColorColumn;

    private static bool TryParseColor(string? text, out Color color)
    {
        color = Color.Empty;
        if (text is null || !ColorPattern.IsMatch(text.Trim())) return false;
        int rgb = int.Parse(text.Trim().AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = Color.FromArgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        return true;
    }
}
