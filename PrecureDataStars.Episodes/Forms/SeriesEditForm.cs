using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Episodes.Forms;

/// <summary>シリーズ（TV シリーズ・劇場版・OVA 等）の CRUD 編集フォーム。</summary>
public partial class SeriesEditorForm : Form
{
    /// <summary>監査列 (created_by / updated_by) に記録するユーザー識別子。</summary>
    private const string AuditUser = "series_editor";
    private readonly SeriesRepository _seriesRepo;

    // ルックアップ用
    private readonly SeriesKindsRepository _kindsRepo;
    private readonly SeriesRelationKindsRepository _relKindsRepo;
    private IReadOnlyList<SeriesKind> _kinds = Array.Empty<SeriesKind>();
    private IReadOnlyList<SeriesRelationKind> _relKinds = Array.Empty<SeriesRelationKind>();

    private List<Series> _series = new();         // 画面上の全シリーズ（is_deleted=0）
    private Series? _current;                     // 現在編集中のシリーズ

    // 作品ごとのメインスタッフの役職（series_key_staff_roles）
    private readonly RolesRepository _rolesRepo;
    private readonly SeriesKeyStaffRolesRepository _keyStaffRolesRepo;
    /// <summary>役職ごとに、ほかの作品でいちばん多く使われている色（行を足して役職を選んだときの色の初期値）。</summary>
    private Dictionary<string, string> _usualColorByRole = new(StringComparer.Ordinal);
    /// <summary>メインスタッフの欄の読み込みの世代。読み込み中に別の作品を選んだら、古い読み込みの結果を捨てる。</summary>
    private int _keyStaffLoadGen;
    /// <summary>メインスタッフの欄の役職の列と色の列の名前。</summary>
    private const string KeyStaffRoleColumn = "colKeyStaffRole";
    private const string KeyStaffColorColumn = "colKeyStaffColor";
    /// <summary>バッジの色の書式（#rrggbb）。</summary>
    private static readonly Regex BadgeColorPattern = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    /// <summary>親シリーズ ComboBox 用の選択肢モデル（ID + 表示テキスト）。</summary>
    private sealed class ParentOption
    {
        public int? Id { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary>relation_to_parent ComboBox 用の汎用選択肢モデル。</summary>
    private sealed class ComboOption<T>
    {
        public T? Value { get; init; }
        public string Text { get; init; } = "";
        public override string ToString() => Text;
    }

    /// <summary><see cref="SeriesEditorForm"/> の新しいインスタンスを生成する。 初期化時にマスタのロードとシリーズ一覧の取得を非同期で開始する。</summary>
    /// <param name="seriesRepo">シリーズリポジトリ。</param>
    /// <param name="kindsRepo">シリーズ種別マスタリポジトリ。</param>
    /// <param name="relKindsRepo">シリーズ関係種別マスタリポジトリ。</param>
    /// <param name="rolesRepo">役職マスタリポジトリ（メインスタッフの役職の選択肢）。</param>
    /// <param name="keyStaffRolesRepo">作品ごとのメインスタッフの役職のリポジトリ。</param>
    public SeriesEditorForm(
        SeriesRepository seriesRepo,
        SeriesKindsRepository kindsRepo,
        SeriesRelationKindsRepository relKindsRepo,
        RolesRepository rolesRepo,
        SeriesKeyStaffRolesRepository keyStaffRolesRepo)
    {
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _kindsRepo = kindsRepo ?? throw new ArgumentNullException(nameof(kindsRepo));
        _relKindsRepo = relKindsRepo ?? throw new ArgumentNullException(nameof(relKindsRepo));
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _keyStaffRolesRepo = keyStaffRolesRepo ?? throw new ArgumentNullException(nameof(keyStaffRolesRepo));

        InitializeComponent();

        // メインスタッフの欄
        btnKeyStaffAdd.Click += (_, __) => AddKeyStaffRow();
        btnKeyStaffRemove.Click += (_, __) => RemoveKeyStaffRow();
        btnKeyStaffUp.Click += (_, __) => MoveKeyStaffRow(-1);
        btnKeyStaffDown.Click += (_, __) => MoveKeyStaffRow(+1);
        btnKeyStaffColor.Click += (_, __) => PickKeyStaffColor();
        dgvKeyStaff.CellFormatting += DgvKeyStaff_CellFormatting;
        dgvKeyStaff.CellValueChanged += DgvKeyStaff_CellValueChanged;
        dgvKeyStaff.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && IsColorColumn(e.ColumnIndex)) PickKeyStaffColor(); };
        // ドロップダウンで選んだ値をすぐ確定させ、色の初期値を入れられるようにする
        dgvKeyStaff.CurrentCellDirtyStateChanged += (_, __) =>
        {
            if (dgvKeyStaff.IsCurrentCellDirty && dgvKeyStaff.CurrentCell is DataGridViewComboBoxCell)
                dgvKeyStaff.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        // events
        lstSeries.SelectedIndexChanged += (_, __) => BindSelected();
        btnSave.Click += async (_, __) => await SaveAsync();
        btnAdd.Click += async (_, __) => await AddAsync();

        // 終了日 DTP の “NULL風表示” 制御（チェックON/OFFや日付変更で発火）
        dtEnd.ValueChanged += (_, __) => ApplyNullableFormat(dtEnd);

        // 初期ロード
        _ = LoadAsync();
    }

    /// <summary>初期ロード: ルックアップ（種別・関係種別）取得 → シリーズ一覧取得 → リストバインド。</summary>
    private async Task LoadAsync()
    {
        // ルックアップ取得
        _kinds = await _kindsRepo.GetAllAsync();
        _relKinds = await _relKindsRepo.GetAllAsync();

        // テーブル由来でコンボ構築
        PopulateCombosFromLookups();

        // メインスタッフの欄の列（役職の選択肢）と、役職ごとのいつもの色
        var roles = await _rolesRepo.GetAllAsync();
        var allKeyStaff = await _keyStaffRolesRepo.GetAllAsync();
        _usualColorByRole = allKeyStaff
            .GroupBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(r => r.BadgeColor.ToLowerInvariant()).OrderByDescending(c => c.Count()).ThenBy(c => c.Key).First().Key,
                StringComparer.Ordinal);
        BuildKeyStaffColumns(roles);

        // DBから全シリーズを取得
        _series = (await _seriesRepo.GetAllAsync()).ToList();
        BindList();
    }

    /// <summary>シリーズ一覧を ListBox にバインドする（Title を表示、SeriesId を値に設定）。</summary>
    private void BindList()
    {
        lstSeries.DataSource = null;
        lstSeries.DataSource = _series;
        lstSeries.DisplayMember = nameof(Series.Title);
        lstSeries.ValueMember = nameof(Series.SeriesId);
        if (_series.Count > 0 && lstSeries.SelectedIndex < 0)
            lstSeries.SelectedIndex = 0;
    }

    /// <summary>series_kinds / series_relation_kinds テーブルの内容でコンボボックスを構築する。</summary>
    private void PopulateCombosFromLookups()
    {
        // kind：コード文字列のみ（既存ロジックが string を期待）
        cmbKind.Items.Clear();
        foreach (var k in _kinds)
            cmbKind.Items.Add(k.KindCode);

        // relation：先頭に（なし）
        var relOptions = new List<ComboOption<string?>> { new() { Value = null, Text = "（なし）" } };
        relOptions.AddRange(_relKinds.Select(r => new ComboOption<string?> { Value = r.RelationCode, Text = $"{r.RelationCode} - {r.NameJa}" }));

        cmbRelation.DisplayMember = nameof(ComboOption<string?>.Text);
        cmbRelation.ValueMember = nameof(ComboOption<string?>.Value);
        cmbRelation.DataSource = relOptions;

        // 絵コンテと演出の出し方（series.storyboard_display）：固定の 3 択
        var storyboardOptions = new List<ComboOption<string>>
        {
            new() { Value = Series.StoryboardDisplaySeparate, Text = "それぞれの役職名で出す" },
            new() { Value = Series.StoryboardDisplayMerge, Text = "いつも「演出」にまとめる（違う人なら「演出」の欄に並べる）" },
            new() { Value = Series.StoryboardDisplayMergeSamePerson, Text = "同じ人のときだけ「(絵コンテ・)演出」にまとめる" },
        };
        cmbStoryboardDisplay.DisplayMember = nameof(ComboOption<string>.Text);
        cmbStoryboardDisplay.ValueMember = nameof(ComboOption<string>.Value);
        cmbStoryboardDisplay.DataSource = storyboardOptions;

        // 開始日の確かさ（series.start_date_precision）：固定の 3 択。年や年月までしか発表されていない作品は、
        // 開始日に仮の日付を入れてここで確かな範囲を選ぶ（サイトは「2027年放送予定」のように出す）
        var precisionOptions = new List<ComboOption<string>>
        {
            new() { Value = Series.StartDatePrecisionDay, Text = "日付まで確か" },
            new() { Value = Series.StartDatePrecisionMonth, Text = "年月まで確か（日は仮）" },
            new() { Value = Series.StartDatePrecisionYear, Text = "年まで確か（月日は仮）" },
        };
        cmbStartDatePrecision.DisplayMember = nameof(ComboOption<string>.Text);
        cmbStartDatePrecision.ValueMember = nameof(ComboOption<string>.Value);
        cmbStartDatePrecision.DataSource = precisionOptions;
    }

    /// <summary>親シリーズ ComboBox を構築する（自分自身は除外）。</summary>
    /// <param name="currentSeriesId">現在編集中のシリーズ ID（除外対象）。新規の場合は NULL。</param>
    private void PopulateParentCombo(int? currentSeriesId)
    {
        // 親シリーズは「自分自身は選べない」。
        // 先頭に（なし）を追加。
        var options = new List<ParentOption>
        {
            new()
            {
                Id = null,
                Text = "（なし）"
            }
        };
        foreach (var s in _series)
        {
            if (currentSeriesId.HasValue && s.SeriesId == currentSeriesId.Value) continue;
            options.Add(new ParentOption { Id = s.SeriesId, Text = $"[{s.SeriesId}] {s.Title}" });
        }

        cmbParent.DisplayMember = nameof(ParentOption.Text);
        cmbParent.ValueMember = nameof(ParentOption.Id);
        cmbParent.DataSource = options;
    }

    /// <summary>Nullable な DateTimePicker の表示制御。Checked=false なら CustomFormat を空白にし、 視覚的に「未設定」を表現する。</summary>
    /// <param name="dtp">対象の <see cref="DateTimePicker"/>（ShowCheckBox = true 前提）。</param>
    private static void ApplyNullableFormat(DateTimePicker dtp)
    {
        if (!dtp.ShowCheckBox) return;
        if (dtp.Checked)
        {
            if (string.IsNullOrEmpty(dtp.CustomFormat) || dtp.CustomFormat == " ")
                dtp.CustomFormat = "yyyy-MM-dd";
        }
        else
        {
            dtp.CustomFormat = " ";
        }
    }

    /// <summary>リストで選択されたシリーズの全フィールドを右ペインの各コントロールにバインドする。</summary>
    private void BindSelected()
    {
        _current = lstSeries.SelectedItem as Series;
        if (_current is null)
        {
            // 空クリア
            txtTitle.Text = "";
            txtSlug.Text = "";
            cmbKind.SelectedIndex = -1;
            cmbParent.DataSource = null;
            cmbRelation.SelectedIndex = 0; // （なし）
            numSeqInParent.Value = 0;
            dtStart.Value = DateTime.Today;
            dtEnd.Checked = false;
            dtEnd.Value = DateTime.Today;
            ApplyNullableFormat(dtEnd);
            numEpisodes.Value = 0;
            numRunTimeSeconds.Value = 0;
            txtTitleKana.Text = "";
            txtTitleShort.Text = "";
            txtTitleShortKana.Text = "";
            txtTitleEn.Text = "";
            txtTitleShortEn.Text = "";
            txtToeiSite.Text = "";
            txtToeiLineup.Text = "";
            txtAbcSite.Text = "";
            txtAmazonPrimeAsin.Text = "";
            txtYoutubeTrailer.Text = "";
            txtFilmRatingNo.Text = "";
            chkFilmCjMark.Checked = false;
            // 絵コンテと演出の出し方も初期状態（それぞれの役職名で出す）に戻す。
            cmbStoryboardDisplay.SelectedValue = Series.StoryboardDisplaySeparate;
            // 開始日の確かさも初期状態（日付まで確か）に戻す。
            cmbStartDatePrecision.SelectedValue = Series.StartDatePrecisionDay;
            // メインスタッフの欄も空にする。
            Interlocked.Increment(ref _keyStaffLoadGen);
            dgvKeyStaff.Rows.Clear();
            return;
        }

        // 親シリーズ ComboBox を再構築（自分自身を除外してから選択値をセット）
        PopulateParentCombo(_current.SeriesId);

        // 値のバインド
        txtTitle.Text = _current.Title;
        txtSlug.Text = _current.Slug;

        // kind_code
        var kindIndex = cmbKind.Items.IndexOf(_current.KindCode);
        cmbKind.SelectedIndex = kindIndex >= 0 ? kindIndex : -1;

        // parent_series_id
        if (_current.ParentSeriesId is { } pid)
        {
            var hasPid = (cmbParent.DataSource as IEnumerable<ParentOption>)?
                         .Any(o => o.Id == pid) == true;
            if (hasPid) cmbParent.SelectedValue = (int?)pid;
            else if (cmbParent.Items.Count > 0) cmbParent.SelectedIndex = 0;
        }
        else
        {
            if (cmbParent.Items.Count > 0) cmbParent.SelectedIndex = 0; // （なし）
        }

        // relation_to_parent（DataSource 利用）
        cmbRelation.SelectedValue = _current.RelationToParent ?? (object)DBNull.Value;

        // seq_in_parent
        numSeqInParent.Value = _current.SeqInParent.HasValue ? _current.SeqInParent.Value : 0;

        // dates
        dtStart.Value = _current.StartDate.ToDateTime(TimeOnly.MinValue);
        cmbStartDatePrecision.SelectedValue = _current.StartDatePrecision;
        if (_current.EndDate is { } ed)
        {
            dtEnd.Checked = true;
            dtEnd.CustomFormat = "yyyy-MM-dd";
            dtEnd.Value = ed.ToDateTime(TimeOnly.MinValue);
        }
        else
        {
            dtEnd.Checked = false;
            dtEnd.Value = DateTime.Today; // 値はダミー
            dtEnd.CustomFormat = " ";
        }
        ApplyNullableFormat(dtEnd);

        // 数値
        numEpisodes.Value = _current.Episodes.HasValue ? _current.Episodes.Value : 0;
        numRunTimeSeconds.Value = _current.RunTimeSeconds.HasValue ? _current.RunTimeSeconds.Value : 0;

        // 文字列群
        txtTitleKana.Text = _current.TitleKana ?? "";
        txtTitleShort.Text = _current.TitleShort ?? "";
        txtTitleShortKana.Text = _current.TitleShortKana ?? "";
        txtTitleEn.Text = _current.TitleEn ?? "";
        txtTitleShortEn.Text = _current.TitleShortEn ?? "";
        txtToeiSite.Text = _current.ToeiAnimOfficialSiteUrl ?? "";
        txtToeiLineup.Text = _current.ToeiAnimLineupUrl ?? "";
        txtAbcSite.Text = _current.AbcOfficialSiteUrl ?? "";
        txtAmazonPrimeAsin.Text = _current.AmazonPrimeVideoAsin ?? "";
        txtYoutubeTrailer.Text = _current.YoutubeTrailerUrl ?? "";
        txtFilmRatingNo.Text = _current.FilmRatingNo ?? "";
        chkFilmCjMark.Checked = _current.FilmCjMark;
        // 絵コンテと演出の出し方の読み込み。
        cmbStoryboardDisplay.SelectedValue = _current.StoryboardDisplay;
        // メインスタッフの役職の読み込み。
        _ = LoadKeyStaffAsync(_current.SeriesId);
    }

    /// <summary>
    /// メインスタッフの欄の列を組む。役職の列は役職マスタのドロップダウン（表示は役職名、値は役職コード）、
    /// 色の列は #rrggbb の文字（セルの地にその色を敷く）。
    /// </summary>
    private void BuildKeyStaffColumns(IReadOnlyList<Role> roles)
    {
        dgvKeyStaff.Columns.Clear();
        var roleOptions = roles
            .Select(r => new ComboOption<string> { Value = r.RoleCode, Text = $"{r.NameJa}（{r.RoleCode}）" })
            .ToList();
        dgvKeyStaff.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = KeyStaffRoleColumn,
            HeaderText = "役職",
            DataSource = roleOptions,
            ValueMember = nameof(ComboOption<string>.Value),
            DisplayMember = nameof(ComboOption<string>.Text),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        dgvKeyStaff.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = KeyStaffColorColumn,
            HeaderText = "色",
            Width = 100,
            MaxInputLength = 7,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        // 列を組む前に作品を選んでいたら、ここで読み込み直す
        if (_current is not null) _ = LoadKeyStaffAsync(_current.SeriesId);
    }

    /// <summary>
    /// 指定作品のメインスタッフの役職を読み込んで欄に並べる。読み込み中に別の作品を選んだら、結果を捨てる。
    /// 列がまだ組まれていなければ（役職マスタの読み込み前）何もしない。
    /// </summary>
    private async Task LoadKeyStaffAsync(int seriesId)
    {
        int gen = Interlocked.Increment(ref _keyStaffLoadGen);
        dgvKeyStaff.Rows.Clear();
        if (dgvKeyStaff.Columns.Count == 0) return;
        var rows = await _keyStaffRolesRepo.GetBySeriesAsync(seriesId);
        if (gen != _keyStaffLoadGen) return;
        dgvKeyStaff.Rows.Clear();
        foreach (var r in rows)
            dgvKeyStaff.Rows.Add(r.RoleCode, r.BadgeColor);
    }

    /// <summary>メインスタッフの欄に空の行を足して選ぶ（役職を選ぶと色の初期値が入る）。</summary>
    private void AddKeyStaffRow()
    {
        if (_current is null || dgvKeyStaff.Columns.Count == 0) return;
        int i = dgvKeyStaff.Rows.Add();
        dgvKeyStaff.CurrentCell = dgvKeyStaff.Rows[i].Cells[KeyStaffRoleColumn];
    }

    /// <summary>メインスタッフの欄の選んだ行を消す。</summary>
    private void RemoveKeyStaffRow()
    {
        if (dgvKeyStaff.CurrentRow is not { } row) return;
        dgvKeyStaff.Rows.Remove(row);
    }

    /// <summary>メインスタッフの欄の選んだ行を上（-1）か下（+1）へ 1 つ動かす。</summary>
    private void MoveKeyStaffRow(int delta)
    {
        if (dgvKeyStaff.CurrentRow is not { } row) return;
        int from = row.Index, to = from + delta;
        if (to < 0 || to >= dgvKeyStaff.Rows.Count) return;
        dgvKeyStaff.EndEdit();
        dgvKeyStaff.Rows.RemoveAt(from);
        dgvKeyStaff.Rows.Insert(to, row);
        dgvKeyStaff.CurrentCell = dgvKeyStaff.Rows[to].Cells[KeyStaffRoleColumn];
    }

    /// <summary>メインスタッフの欄の選んだ行の色を、色の選択ダイアログで選ぶ。</summary>
    private void PickKeyStaffColor()
    {
        if (dgvKeyStaff.CurrentRow is not { } row) return;
        var cell = row.Cells[KeyStaffColorColumn];
        using var dlg = new ColorDialog { FullOpen = true, AnyColor = true };
        if (TryParseBadgeColor(cell.Value as string, out var current)) dlg.Color = current;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        cell.Value = $"#{dlg.Color.R:x2}{dlg.Color.G:x2}{dlg.Color.B:x2}";
    }

    /// <summary>役職を選んで色が空なら、その役職のいつもの色を入れる。</summary>
    private void DgvKeyStaff_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || dgvKeyStaff.Columns[e.ColumnIndex].Name != KeyStaffRoleColumn) return;
        var row = dgvKeyStaff.Rows[e.RowIndex];
        var colorCell = row.Cells[KeyStaffColorColumn];
        if (!string.IsNullOrWhiteSpace(colorCell.Value as string)) return;
        if (row.Cells[KeyStaffRoleColumn].Value is string code && _usualColorByRole.TryGetValue(code, out var usual))
            colorCell.Value = usual;
    }

    /// <summary>色の列のセルの地に、その色を敷く（読めない色なら敷かない）。</summary>
    private void DgvKeyStaff_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || !IsColorColumn(e.ColumnIndex) || e.CellStyle is null) return;
        if (!TryParseBadgeColor(e.Value as string, out var c)) return;
        e.CellStyle.BackColor = c;
        e.CellStyle.SelectionBackColor = c;
        // 字は地の明るさで白か黒にする
        var fore = (c.R * 299 + c.G * 587 + c.B * 114) / 1000 >= 140 ? Color.Black : Color.White;
        e.CellStyle.ForeColor = fore;
        e.CellStyle.SelectionForeColor = fore;
    }

    private bool IsColorColumn(int columnIndex)
        => columnIndex >= 0 && dgvKeyStaff.Columns[columnIndex].Name == KeyStaffColorColumn;

    private static bool TryParseBadgeColor(string? text, out Color color)
    {
        color = Color.Empty;
        if (text is null || !BadgeColorPattern.IsMatch(text.Trim())) return false;
        int rgb = int.Parse(text.Trim().AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = Color.FromArgb((rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
        return true;
    }

    /// <summary>
    /// メインスタッフの欄を、保存する行（欄の上からの順）にする。役職が空・色が #rrggbb でない・同じ役職が 2 回あるときは
    /// 理由を返して保存しない。
    /// </summary>
    private (List<SeriesKeyStaffRole>? Rows, string? Error) CollectKeyStaffRows()
    {
        dgvKeyStaff.EndEdit();
        var rows = new List<SeriesKeyStaffRole>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridViewRow r in dgvKeyStaff.Rows)
        {
            int no = r.Index + 1;
            if (r.Cells[KeyStaffRoleColumn].Value is not string code || code.Length == 0)
                return (null, $"メインスタッフの {no} 行目の役職が空です。");
            var color = (r.Cells[KeyStaffColorColumn].Value as string ?? "").Trim();
            if (!BadgeColorPattern.IsMatch(color))
                return (null, $"メインスタッフの {no} 行目の色は #rrggbb の形で書いてください（いま「{color}」）。");
            if (!seen.Add(code))
                return (null, $"メインスタッフの {no} 行目の役職（{code}）が、上の行と重なっています。");
            rows.Add(new SeriesKeyStaffRole { RoleCode = code, BadgeColor = color.ToLowerInvariant() });
        }
        return (rows, null);
    }

    /// <summary>右ペインの編集内容を Series モデルに反映し、DB へ UPDATE する。</summary>
    private async Task SaveAsync()
    {
        if (_current is null) return;

        // UI コントロールの値 → Series モデルへ反映（空文字は NULL 扱い）
        _current.Title = NormalizeWaveDash(txtTitle.Text.Trim());
        _current.Slug = txtSlug.Text.Trim();

        // kind_code
        _current.KindCode = (cmbKind.SelectedItem as string) ?? "TV";

        // parent_series_id（自分自身は選ばせていない前提だが、念のため防御）
        var parentOption = cmbParent.SelectedItem as ParentOption;
        var selectedParentId = parentOption?.Id;
        if (selectedParentId.HasValue && selectedParentId.Value == _current.SeriesId)
        {
            MessageBox.Show("自分自身を親シリーズには指定できません。", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _current.ParentSeriesId = selectedParentId;

        // relation_to_parent と seq_in_parent（DataSource）
        var rel = cmbRelation.SelectedValue as string;
        _current.RelationToParent = string.IsNullOrWhiteSpace(rel) ? null : rel;
        _current.SeqInParent = (byte?)(numSeqInParent.Value == 0 ? null : (byte)numSeqInParent.Value);

        // 日付
        _current.StartDate = DateOnly.FromDateTime(dtStart.Value.Date);
        _current.StartDatePrecision = cmbStartDatePrecision.SelectedValue as string ?? Series.StartDatePrecisionDay;
        _current.EndDate = dtEnd.Checked ? DateOnly.FromDateTime(dtEnd.Value.Date) : null;

        // 数値
        _current.Episodes = numEpisodes.Value == 0 ? null : (ushort?)numEpisodes.Value;
        _current.RunTimeSeconds = numRunTimeSeconds.Value == 0 ? null : (ushort?)numRunTimeSeconds.Value;

        // 文字列群（空文字は NULL 扱い）
        _current.TitleKana = string.IsNullOrWhiteSpace(txtTitleKana.Text) ? null : txtTitleKana.Text.Trim();
        _current.TitleShort = string.IsNullOrWhiteSpace(txtTitleShort.Text) ? null : txtTitleShort.Text.Trim();
        _current.TitleShortKana = string.IsNullOrWhiteSpace(txtTitleShortKana.Text) ? null : txtTitleShortKana.Text.Trim();
        _current.TitleEn = string.IsNullOrWhiteSpace(txtTitleEn.Text) ? null : txtTitleEn.Text.Trim();
        _current.TitleShortEn = string.IsNullOrWhiteSpace(txtTitleShortEn.Text) ? null : txtTitleShortEn.Text.Trim();
        _current.ToeiAnimOfficialSiteUrl = string.IsNullOrWhiteSpace(txtToeiSite.Text) ? null : txtToeiSite.Text.Trim();
        _current.ToeiAnimLineupUrl = string.IsNullOrWhiteSpace(txtToeiLineup.Text) ? null : txtToeiLineup.Text.Trim();
        _current.AbcOfficialSiteUrl = string.IsNullOrWhiteSpace(txtAbcSite.Text) ? null : txtAbcSite.Text.Trim();
        _current.AmazonPrimeVideoAsin = string.IsNullOrWhiteSpace(txtAmazonPrimeAsin.Text) ? null : txtAmazonPrimeAsin.Text.Trim();
        _current.YoutubeTrailerUrl = string.IsNullOrWhiteSpace(txtYoutubeTrailer.Text) ? null : txtYoutubeTrailer.Text.Trim();
        _current.FilmRatingNo = string.IsNullOrWhiteSpace(txtFilmRatingNo.Text) ? null : txtFilmRatingNo.Text.Trim();
        _current.FilmCjMark = chkFilmCjMark.Checked;
        // 絵コンテと演出の出し方の保存。
        _current.StoryboardDisplay = cmbStoryboardDisplay.SelectedValue as string ?? Series.StoryboardDisplaySeparate;

        _current.UpdatedBy = AuditUser;

        // INSERT 前のバリデーション（スキーマの NOT NULL / CHECK 制約に対応）
        if (string.IsNullOrWhiteSpace(_current.Title))
        {
            MessageBox.Show("タイトルは必須です。", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(_current.Slug))
        {
            MessageBox.Show("スラッグは必須です。", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // メインスタッフの欄（書き込む前に全部の行を確かめる）
        var (keyStaffRows, keyStaffError) = CollectKeyStaffRows();
        if (keyStaffRows is null)
        {
            MessageBox.Show(keyStaffError, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 保存（作品の行と、メインスタッフの役職の置き換え）
        await _seriesRepo.UpdateAsync(_current);
        await _keyStaffRolesRepo.ReplaceForSeriesAsync(_current.SeriesId, keyStaffRows, AuditUser);

        // リストの表示文字（タイトル）が変化する可能性があるので再バインド
        var idx = lstSeries.SelectedIndex;
        BindList();
        if (idx >= 0 && idx < lstSeries.Items.Count) lstSeries.SelectedIndex = idx;

        MessageBox.Show("保存しました", "Series", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>仮データで新規シリーズを INSERT し、リストに追加して選択状態にする。</summary>
    private async Task AddAsync()
    {
        var s = new Series
        {
            KindCode = "TV",
            Title = "(新規)",
            Slug = $"new-{DateTime.Now:yyyyMMddHHmmss}",
            StartDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedBy = AuditUser,
            UpdatedBy = AuditUser
        };

        // ひとまず Insert
        var id = await _seriesRepo.InsertAsync(s);
        s.SeriesId = id;

        // 画面リストに追加して選択
        _series.Add(s);
        BindList();
        lstSeries.SelectedItem = s;
    }

    /// <summary>全角チルダ(～) → 全角波ダッシュ(〜)</summary>
    /// <param name="s"></param>
    /// <returns></returns>
    private static string NormalizeWaveDash(string s)
    => string.IsNullOrEmpty(s) ? s : s.Replace('\uFF5E', '\u301C');
}
