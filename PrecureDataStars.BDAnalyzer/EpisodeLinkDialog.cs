#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// 読み取ったプレイリストとチャプターに、話とパートを当てるダイアログ。
    /// 作品を選んで「自動で当てる」を押すと <see cref="EpisodeLinkMatcher"/> の提案で表を埋め、
    /// 種別・話数・パート順はセルで直せる。OK で <see cref="BdChapter"/>（chapter_kind / episode_id / episode_seq / series_id）と
    /// <see cref="BdPlaylist"/>（playlist_kind / episode_id / series_id）に書き戻す（DB には触れない。記録は MainForm の「Blu-ray の情報を記録」で行う）。
    /// 尺の列は、チャプターの尺（Blu-ray の生の値）とパートの円盤尺（DB の値）の差を見せるだけで、どちらも書き換えない。
    /// 話の最後のチャプター（ユニットの末尾）には 1 秒の余白が付いているので、話の最後のパートは円盤尺 + 1 秒と比べる。
    /// <para>
    /// 映画など作品単位の作品（話を持たない）を選ぶと、親の映画と併映のまとまり（<see cref="FeatureLinkMatcher.FeatureGroup"/>）を候補にし、
    /// 行ごとに「作品」を選ぶ列が出る。プレイリストの行で選んだ作品はそのチャプターに引き継がれ（併映と続いているときはチャプターの行で選び直す）、
    /// 「自動で当てる」は上映時間のある作品を <see cref="FeatureLinkMatcher"/> で当てる。
    /// 下の表に作品ごとの「チャプターの合計 − 先頭の黒み（秒。既定 2）− 末尾の黒み（秒。既定 1）」を出し、チェックした作品の上映時間（<c>series.run_time_seconds</c>）に入れられる
    /// （<see cref="FeatureRunTimes"/>。DB への書き込みは MainForm の「Blu-ray の情報を記録」のときに bd_* と一緒に行う）。
    /// </para>
    /// </summary>
    public sealed class EpisodeLinkDialog : Form
    {
        private static readonly string[] PlaylistKinds = { "", "EPISODE", "PLAY_ALL", "FEATURE", "BONUS", "MENU", "OTHER" };
        private static readonly string[] ChapterKinds = { "", "EPISODE_PART", "FEATURE", "BLANK", "BONUS", "OTHER" };
        /// <summary>作品の本編に数えないチャプターの種別。</summary>
        private static readonly HashSet<string> NonFeatureKinds = new(StringComparer.Ordinal) { "BLANK", "BONUS", "OTHER", "MENU", "EPISODE_PART" };

        private readonly SeriesRepository _seriesRepo;
        private readonly EpisodesRepository _episodesRepo;
        private readonly EpisodePartsRepository _partsRepo;
        private readonly IReadOnlyList<BdChapter> _chapters;
        private readonly IReadOnlyList<BdPlaylist> _playlists;
        private readonly int? _defaultSeriesId;

        private readonly ComboBox _cboSeries = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        private readonly Button _btnAuto = new() { Text = "自動で当てる", Width = 130, Height = 28 };
        private readonly Label _lblHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
        };
        // 作品単位の作品：作品ごとの尺と上映時間に入れるか
        private readonly GroupBox _grpWorks = new() { Text = "作品ごとの尺（チャプターの合計 − 先頭の黒み − 末尾の黒み）", Dock = DockStyle.Bottom, Height = 140, Visible = false, Padding = new Padding(8) };
        private readonly TableLayoutPanel _tblWorks = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 7 };

        /// <summary>OK のときの作品（bd_discs.series_id に入れる）。作品単位の作品では親の映画（3 本立ては親のまとまり）。</summary>
        public int? SelectedSeriesId { get; private set; }

        /// <summary>OK のとき、上映時間（series.run_time_seconds）に入れる作品と秒数（チャプターの合計 − 先頭の黒み − 末尾の黒み）。</summary>
        public IReadOnlyDictionary<int, ushort> FeatureRunTimes { get; private set; } = new Dictionary<int, ushort>();

        private IReadOnlyList<Series> _allSeries = Array.Empty<Series>();
        private IReadOnlyList<Episode> _episodes = Array.Empty<Episode>();
        private Dictionary<int, IReadOnlyList<EpisodePart>> _partsByEpisode = new();

        /// <summary>選んでいる作品（話を持たない作品単位の作品なら、本編のプレイリストを当てる）。</summary>
        private Series? _series;
        /// <summary>作品単位の作品のとき、親の映画と併映のまとまり。</summary>
        private FeatureLinkMatcher.FeatureGroup? _group;
        /// <summary>作品の表示名 → 作品 ID（「作品」の列のプルダウンの値）。</summary>
        private readonly Dictionary<string, int> _workIdByTitle = new(StringComparer.Ordinal);
        /// <summary>作品ごとの尺の表の行。</summary>
        private readonly List<WorkRow> _workRows = new();

        private const int ColName = 0, ColLen = 1, ColKind = 2, ColEp = 3, ColSeq = 4, ColPart = 5, ColDiff = 6, ColWork = 7;


        public EpisodeLinkDialog(
            SeriesRepository seriesRepo, EpisodesRepository episodesRepo, EpisodePartsRepository partsRepo,
            IReadOnlyList<BdChapter> chapters, IReadOnlyList<BdPlaylist> playlists, int? defaultSeriesId)
        {
            _seriesRepo = seriesRepo; _episodesRepo = episodesRepo; _partsRepo = partsRepo;
            _chapters = chapters; _playlists = playlists; _defaultSeriesId = defaultSeriesId;

            Text = "話とパートを当てる";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1200, 760);
            MinimumSize = new Size(900, 480);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
            top.Controls.Add(new Label { Text = "作品：", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            top.Controls.Add(_cboSeries);
            top.Controls.Add(_btnAuto);
            top.Controls.Add(_lblHint);
            _lblHint.Margin = new Padding(12, 7, 0, 0);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var btnCancel = new Button { Text = "キャンセル", Width = 100, Height = 28, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "OK", Width = 100, Height = 28 };
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnOk);
            AcceptButton = btnOk; CancelButton = btnCancel;

            _grpWorks.Controls.Add(_tblWorks);

            BuildGrid();
            Controls.Add(_grid); Controls.Add(top); Controls.Add(_grpWorks); Controls.Add(bottom);

            Load += async (_, _) => await LoadSeriesAsync();
            _btnAuto.Click += async (_, _) => await AutoMatchAsync();
            // プルダウンは選んだ時点で確定させる（「作品」を選ぶと種別と尺の表がすぐ変わるように）
            _grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (_, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = _grid.Rows[e.RowIndex];
                if (e.ColumnIndex is ColEp or ColSeq) ResolveRow(row);
                if (e.ColumnIndex == ColWork && !string.IsNullOrEmpty(row.Cells[ColWork].Value?.ToString())
                    && string.IsNullOrEmpty(row.Cells[ColKind].Value?.ToString()))
                {
                    row.Cells[ColKind].Value = "FEATURE";
                }
                if (e.ColumnIndex is ColWork or ColKind) UpdateWorkRows();
            };
            _grid.DataError += (_, e) => e.ThrowException = false;
            btnOk.Click += (_, _) =>
            {
                if (!Apply()) return;
                SelectedSeriesId = _group?.Root.SeriesId ?? _cboSeries.SelectedValue as int?;
                FeatureRunTimes = CollectRunTimes();
                DialogResult = DialogResult.OK;
            };
        }

        /// <summary>表の列を作り、プレイリストごとに見出し行とチャプター行を並べる。</summary>
        private void BuildGrid()
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "プレイリスト / チャプター", Width = 200, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "チャプター尺", Width = 100, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "種別", Width = 120, FlatStyle = FlatStyle.Flat });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "話数", Width = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "パート順", Width = 70 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "パート（DB の円盤尺）", Width = 260, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "差（秒）", Width = 80, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "作品", Width = 380, FlatStyle = FlatStyle.Flat, Visible = false });

            foreach (var group in _chapters.GroupBy(c => c.PlaylistFile ?? "", StringComparer.Ordinal))
            {
                var playlist = _playlists.FirstOrDefault(p => string.Equals(p.PlaylistFile, group.Key, StringComparison.OrdinalIgnoreCase));
                ulong total = (ulong)group.Sum(c => (decimal)c.DurationMs);
                int r = _grid.Rows.Add($"[{group.Key}]", FormatSec(total), playlist?.PlaylistKind ?? "", "", "", "", "", "");
                var row = _grid.Rows[r];
                row.Tag = new RowTag(playlist, null);
                row.DefaultCellStyle.BackColor = SystemColors.ControlLight;
                row.Cells[ColSeq].ReadOnly = true;
                SetItems(row, ColKind, PlaylistKinds);
                if (playlist?.EpisodeId is int pid) row.Cells[ColEp].Value = EpNoOf(pid);

                int i = 0;
                foreach (var ch in group)
                {
                    i++;
                    int cr = _grid.Rows.Add($"    {i}", FormatSec(ch.DurationMs), ch.ChapterKind ?? "", "", "", "", "", "");
                    var crow = _grid.Rows[cr];
                    crow.Tag = new RowTag(null, ch);
                    SetItems(crow, ColKind, ChapterKinds);
                    if (ch.EpisodeId is int eid) crow.Cells[ColEp].Value = EpNoOf(eid);
                    if (ch.EpisodeSeq is byte seq) crow.Cells[ColSeq].Value = seq.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static void SetItems(DataGridViewRow row, int column, IEnumerable<string> items)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[column];
            cell.Value = null;
            cell.Items.Clear();
            cell.Items.AddRange(items.Cast<object>().ToArray());
        }

        private async Task LoadSeriesAsync()
        {
            try
            {
                // TV も映画なども全部（作品 ID 順）。話を持たない作品は本編のプレイリストで当てる
                _allSeries = (await _seriesRepo.GetAllAsync()).OrderBy(s => s.SeriesId).ToList();
                _cboSeries.DisplayMember = nameof(Series.Title);
                _cboSeries.ValueMember = nameof(Series.SeriesId);
                _cboSeries.DataSource = _allSeries.ToList();
                if (_defaultSeriesId is int sid) _cboSeries.SelectedValue = sid;
                _cboSeries.SelectedIndexChanged += async (_, _) => await LoadEpisodesAsync();
                await LoadEpisodesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "作品の一覧を読めませんでした: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoadEpisodesAsync()
        {
            if (_cboSeries.SelectedValue is not int sid) return;
            _series = _cboSeries.SelectedItem as Series;
            _episodes = await _episodesRepo.GetBySeriesAsync(sid);
            var ids = _episodes.Select(e => e.EpisodeId).ToHashSet();
            var parts = await _partsRepo.GetAllAsync();
            _partsByEpisode = parts.Where(p => ids.Contains(p.EpisodeId))
                .GroupBy(p => p.EpisodeId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EpisodePart>)g.OrderBy(p => p.EpisodeSeq).ToList());
            foreach (DataGridViewRow row in _grid.Rows) ResolveRow(row);

            _group = IsFeatureSeries && _series is not null ? FeatureLinkMatcher.BuildGroup(_series, _allSeries) : null;
            SetupWorkColumn();
            RebuildWorkRows();

            if (_group is not null)
            {
                string works = string.Join("、", _group.Pieces.Select(p => p.Title + (p.RunTimeSeconds is ushort rt ? $"（{FormatRunTime(rt)}）" : "（上映時間なし）")));
                _lblHint.Text = "作品単位：" + works;
            }
            else
            {
                _lblHint.Text = $"話 {_episodes.Count}・円盤尺のあるパート {_partsByEpisode.Values.Sum(l => l.Count(p => p.DiscLength is not null))}";
            }
        }

        /// <summary>選んでいる作品が話を持たない（映画など作品単位）か。</summary>
        private bool IsFeatureSeries => _series is not null && _episodes.Count == 0;

        /// <summary>
        /// 「作品」の列を作品単位の作品のときだけ出し、プルダウンの候補（プレイリストの行は親と各作品、チャプターの行は本編を持つ作品）と
        /// 記録済みの当て方を入れる。チャプターの作品がプレイリストの作品と同じなら空にしておく（空はプレイリストの作品を引き継ぐ）。
        /// </summary>
        private void SetupWorkColumn()
        {
            bool feature = _group is not null;
            _grid.Columns[ColWork].Visible = feature;
            _grid.Columns[ColEp].Visible = _grid.Columns[ColSeq].Visible = _grid.Columns[ColDiff].Visible = !feature;
            _grid.Columns[ColPart].HeaderText = feature ? "メモ" : "パート（DB の円盤尺）";
            _workIdByTitle.Clear();
            if (_group is null)
            {
                foreach (DataGridViewRow row in _grid.Rows) SetItems(row, ColWork, Array.Empty<string>());
                return;
            }
            var headWorks = new List<Series> { _group.Root };
            headWorks.AddRange(_group.Pieces.Where(p => p.SeriesId != _group.Root.SeriesId));
            foreach (var s in headWorks) _workIdByTitle[s.Title] = s.SeriesId;
            var headItems = new[] { "" }.Concat(headWorks.Select(s => s.Title)).ToList();
            var chapterItems = new[] { "" }.Concat(_group.Pieces.Select(s => s.Title)).ToList();

            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not RowTag tag) continue;
                if (tag.Playlist is { } pl)
                {
                    SetItems(row, ColWork, headItems);
                    row.Cells[ColWork].Value = TitleOf(pl.SeriesId, headItems) ?? "";
                }
                else
                {
                    SetItems(row, ColWork, chapterItems);
                    var head = _playlists.FirstOrDefault(p => string.Equals(p.PlaylistFile, tag.Chapter!.PlaylistFile, StringComparison.OrdinalIgnoreCase));
                    bool inherits = tag.Chapter!.SeriesId == head?.SeriesId && head?.SeriesId != _group.Root.SeriesId;
                    row.Cells[ColWork].Value = inherits ? "" : TitleOf(tag.Chapter.SeriesId, chapterItems) ?? "";
                }
            }
        }

        private string? TitleOf(int? seriesId, IReadOnlyList<string> items)
        {
            if (seriesId is not int id) return null;
            var title = _workIdByTitle.FirstOrDefault(kv => kv.Value == id).Key;
            return title is not null && items.Contains(title) ? title : null;
        }

        private int? WorkIdOf(DataGridViewRow row)
        {
            string t = row.Cells[ColWork].Value?.ToString() ?? "";
            return t.Length > 0 && _workIdByTitle.TryGetValue(t, out var id) ? id : null;
        }

        /// <summary>
        /// チャプターの行が当たる作品：チャプターの行で選んだ作品、空ならプレイリストの行の作品（親のまとまりは引き継がない）。
        /// 本編に数えない種別（余白・特典など）のチャプターは null。
        /// </summary>
        private int? EffectiveWorkOf(DataGridViewRow chapterRow, DataGridViewRow? headRow)
        {
            string kind = chapterRow.Cells[ColKind].Value?.ToString() ?? "";
            if (NonFeatureKinds.Contains(kind)) return null;
            if (WorkIdOf(chapterRow) is int own) return own;
            if (headRow is null || _group is null) return null;
            int? head = WorkIdOf(headRow);
            if (head is null || (_group.RootIsUmbrella && head == _group.Root.SeriesId)) return null;
            return head;
        }

        /// <summary>プレイリストごとに、見出し行とチャプター行の組。</summary>
        private IEnumerable<(DataGridViewRow Head, List<DataGridViewRow> Chapters)> PlaylistBlocks()
        {
            DataGridViewRow? head = null;
            var chapters = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not RowTag tag) continue;
                if (tag.Chapter is null)
                {
                    if (head is not null) yield return (head, chapters);
                    head = row; chapters = new List<DataGridViewRow>();
                }
                else chapters.Add(row);
            }
            if (head is not null) yield return (head, chapters);
        }

        /// <summary>
        /// 作品の本編の尺（ミリ秒）：その作品に当たるチャプターの尺の合計。作品だけが入っているプレイリストを優先し、無ければ併映と続いているプレイリストから取る。
        /// 親のまとまり（3 本立て）は、親を当てたプレイリストの本編のチャプターの合計。どこにも当たっていなければ null。
        /// </summary>
        private ulong? WorkSpanMs(int workId)
        {
            ulong? connected = null;
            foreach (var (head, chapters) in PlaylistBlocks())
            {
                if (_group is not null && _group.RootIsUmbrella && workId == _group.Root.SeriesId)
                {
                    if (WorkIdOf(head) != workId) continue;
                    ulong whole = 0;
                    foreach (var r in chapters)
                        if (!NonFeatureKinds.Contains(r.Cells[ColKind].Value?.ToString() ?? "")) whole += ((RowTag)r.Tag!).Chapter!.DurationMs;
                    if (whole > 0) return whole;
                    continue;
                }
                ulong sum = 0; bool others = false;
                foreach (var r in chapters)
                {
                    int? w = EffectiveWorkOf(r, head);
                    if (w == workId) sum += ((RowTag)r.Tag!).Chapter!.DurationMs;
                    else if (w is not null) others = true;
                }
                if (sum == 0) continue;
                if (!others) return sum;
                connected ??= sum;
            }
            return connected;
        }

        /// <summary>作品ごとの尺の表の 1 行。</summary>
        private sealed class WorkRow
        {
            public Series Series { get; init; } = null!;
            public Label Span { get; init; } = null!;
            public NumericUpDown Black { get; init; } = null!;
            public NumericUpDown TailBlack { get; init; } = null!;
            public Label RunTime { get; init; } = null!;
            public CheckBox Apply { get; init; } = null!;
            public ushort? Seconds { get; set; }
        }

        /// <summary>作品ごとの尺の表を作り直す（作品を選び直したとき）。</summary>
        private void RebuildWorkRows()
        {
            _tblWorks.SuspendLayout();
            _tblWorks.Controls.Clear();
            _tblWorks.RowStyles.Clear();
            _workRows.Clear();
            _grpWorks.Visible = _group is not null;
            if (_group is null) { _tblWorks.ResumeLayout(); return; }

            var works = new List<Series>();
            if (_group.RootIsUmbrella) works.Add(_group.Root);
            works.AddRange(_group.Pieces);

            string[] headers = { "作品", "チャプターの合計", "先頭の黒み（秒）", "末尾の黒み（秒）", "上映時間", "今の上映時間", "" };
            _tblWorks.RowCount = works.Count + 1;
            for (int c = 0; c < headers.Length; c++)
                _tblWorks.Controls.Add(new Label { Text = headers[c], AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 2, 12, 2) }, c, 0);
            for (int i = 0; i < works.Count; i++)
            {
                var s = works[i];
                var row = new WorkRow
                {
                    Series = s,
                    Span = new Label { AutoSize = true, Margin = new Padding(4, 6, 12, 0) },
                    Black = new NumericUpDown { Minimum = 0, Maximum = 60, DecimalPlaces = 1, Increment = 0.1m, Value = 2, Width = 64 },
                    // 本編の最後にも 1 秒の黒みが付いている（TV の話の末尾の余白と同じ）
                    TailBlack = new NumericUpDown { Minimum = 0, Maximum = 60, DecimalPlaces = 1, Increment = 0.1m, Value = 1, Width = 64 },
                    RunTime = new Label { AutoSize = true, Margin = new Padding(4, 6, 12, 0) },
                    // 上映時間の無い作品は入れる前提、ある作品は見比べてから入れる
                    Apply = new CheckBox { Text = "上映時間に入れる", AutoSize = true, Checked = s.RunTimeSeconds is null, Margin = new Padding(4, 4, 0, 0) },
                };
                row.Black.ValueChanged += (_, _) => UpdateWorkRows();
                row.TailBlack.ValueChanged += (_, _) => UpdateWorkRows();
                _tblWorks.Controls.Add(new Label { Text = s.Title, AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(4, 6, 12, 0) }, 0, i + 1);
                _tblWorks.Controls.Add(row.Span, 1, i + 1);
                _tblWorks.Controls.Add(row.Black, 2, i + 1);
                _tblWorks.Controls.Add(row.TailBlack, 3, i + 1);
                _tblWorks.Controls.Add(row.RunTime, 4, i + 1);
                _tblWorks.Controls.Add(new Label { Text = s.RunTimeSeconds is ushort now ? $"{FormatRunTime(now)}（{now} 秒）" : "なし", AutoSize = true, Margin = new Padding(4, 6, 12, 0) }, 5, i + 1);
                _tblWorks.Controls.Add(row.Apply, 6, i + 1);
                _workRows.Add(row);
            }
            _grpWorks.Height = Math.Min(260, 64 + works.Count * 30);
            _tblWorks.ResumeLayout();
            UpdateWorkRows();
        }

        /// <summary>作品ごとの尺の表の数字を、表の当て方と黒みから計算し直す。</summary>
        private void UpdateWorkRows()
        {
            foreach (var row in _workRows)
            {
                ulong? span = WorkSpanMs(row.Series.SeriesId);
                if (span is null)
                {
                    row.Span.Text = "（当たっていない）";
                    row.RunTime.Text = "";
                    row.Seconds = null;
                    row.Apply.Enabled = false;
                    continue;
                }
                double sec = span.Value / 1000.0 - (double)row.Black.Value - (double)row.TailBlack.Value;
                row.Seconds = sec <= 0 ? null : (ushort)Math.Min(ushort.MaxValue, Math.Round(sec));
                row.Span.Text = FormatClock(span.Value / 1000.0);
                row.RunTime.Text = row.Seconds is ushort v
                    ? $"{FormatRunTime(v)}（{v} 秒）" + (row.Series.RunTimeSeconds == v ? "　今と同じ" : row.Series.RunTimeSeconds is ushort now ? $"　今と {v - now:+0;-0} 秒" : "")
                    : "";
                row.Apply.Enabled = row.Seconds is not null;
            }
        }

        private Dictionary<int, ushort> CollectRunTimes()
            => _workRows.Where(r => r.Apply.Enabled && r.Apply.Checked && r.Seconds is not null)
                        .ToDictionary(r => r.Series.SeriesId, r => r.Seconds!.Value);

        private static string FormatRunTime(ushort sec) => $"{sec / 60}:{sec % 60:00}";

        private static string FormatClock(double sec) => $"{(int)(sec / 60)}:{sec % 60:00.0}";

        /// <summary>作品単位の作品：まとまりの作品を <see cref="FeatureLinkMatcher"/> でプレイリスト・チャプターに当てる（当たらない行は触らない）。</summary>
        private void AutoMatchFeature()
        {
            if (_group is null) return;
            // メニュー（MENU）のプレイリストは本編の当てから外す
            var inputs = PlaylistBlocks()
                .Where(b => (b.Head.Cells[ColKind].Value?.ToString() ?? "") != "MENU")
                .Select(b => (((RowTag)b.Head.Tag!).Playlist?.PlaylistFile ?? "",
                              ((RowTag)b.Head.Tag!).Playlist?.DurationMs ?? 0UL,
                              (IReadOnlyList<ulong>)b.Chapters.Select(r => ((RowTag)r.Tag!).Chapter!.DurationMs).ToList()))
                .ToList();
            var proposals = FeatureLinkMatcher.Propose(inputs, _group);
            if (proposals.Count == 0)
            {
                _lblHint.Text = "上映時間のある作品に合うプレイリストがありません。プレイリストの行（併映と続いているときはチャプターの行）で作品を選んでください";
                return;
            }
            foreach (var p in proposals)
            {
                var block = PlaylistBlocks().First(b => string.Equals(((RowTag)b.Head.Tag!).Playlist?.PlaylistFile, p.PlaylistFile, StringComparison.OrdinalIgnoreCase));
                string headTitle = _workIdByTitle.First(kv => kv.Value == p.SeriesId).Key;
                block.Head.Cells[ColKind].Value = "FEATURE";
                block.Head.Cells[ColWork].Value = headTitle;
                block.Head.Cells[ColPart].Value = p.Note;
                bool unsplit = p.ChapterSeriesIds.All(id => id is null);
                for (int i = 0; i < block.Chapters.Count && i < p.ChapterSeriesIds.Count; i++)
                {
                    var row = block.Chapters[i];
                    int? id = p.ChapterSeriesIds[i];
                    if (id is null)
                    {
                        // 親のまとまりをプレイリスト全体で当てたときは本編のまま（作品は人が振り分ける）、繋がっている盤の前後の余りは余白
                        row.Cells[ColKind].Value = unsplit ? "FEATURE" : "BLANK";
                        row.Cells[ColWork].Value = "";
                    }
                    else
                    {
                        row.Cells[ColKind].Value = "FEATURE";
                        row.Cells[ColWork].Value = id == p.SeriesId ? "" : _workIdByTitle.First(kv => kv.Value == id).Key;
                    }
                }
            }
            _lblHint.Text = $"当てたプレイリスト {proposals.Count}：" + string.Join("、", proposals.Select(p => $"{p.PlaylistFile} {p.Note}"));
            UpdateWorkRows();
        }

        /// <summary>作品の各話のパート尺と突き合わせ、提案で表を埋める。</summary>
        private async Task AutoMatchAsync()
        {
            if (_episodes.Count == 0) await LoadEpisodesAsync();
            if (_group is not null)
            {
                AutoMatchFeature();
                return;
            }
            var inputs = new List<(string, IReadOnlyList<ulong>)>();
            foreach (var group in _chapters.GroupBy(c => c.PlaylistFile ?? "", StringComparer.Ordinal))
                inputs.Add((group.Key, group.Select(c => c.DurationMs).ToList()));
            var proposals = EpisodeLinkMatcher.Propose(inputs, _episodes, _partsByEpisode);

            int matched = 0, ambiguous = 0;
            foreach (var p in proposals)
            {
                var rows = _grid.Rows.Cast<DataGridViewRow>()
                    .Where(r => r.Tag is RowTag t && (t.Playlist?.PlaylistFile ?? t.Chapter?.PlaylistFile ?? "") == p.PlaylistFile)
                    .ToList();
                var head = rows.FirstOrDefault(r => ((RowTag)r.Tag!).Chapter is null);
                var chapterRows = rows.Where(r => ((RowTag)r.Tag!).Chapter is not null).ToList();
                if (head is not null)
                {
                    head.Cells[ColKind].Value = p.Kind ?? "";
                    head.Cells[ColEp].Value = p.EpisodeId is int eid ? EpNoOf(eid) : "";
                    // 当たった話の範囲（全話連続なら「第 1〜6 話」）と、同じ所に複数の話が当たったときの注意。
                    var epNos = p.Chapters.Where(c => c.EpisodeId is not null).Select(c => EpNoOf(c.EpisodeId!.Value))
                        .Where(t => t != "").Select(int.Parse).Distinct().OrderBy(n => n).ToList();
                    string range = epNos.Count == 0 ? "" : epNos.Count == 1 ? $"第 {epNos[0]} 話" : $"第 {epNos[0]}〜{epNos[^1]} 話（{epNos.Count} 話）";
                    head.Cells[ColPart].Value = range + (p.CandidateCount > 1 ? $"　候補 {p.CandidateCount} 話（要確認）" : "");
                }
                if (p.EpisodeId is not null) { matched++; if (p.CandidateCount > 1) ambiguous++; }
                for (int i = 0; i < chapterRows.Count && i < p.Chapters.Count; i++)
                {
                    var cp = p.Chapters[i];
                    chapterRows[i].Cells[ColKind].Value = cp.Kind ?? "";
                    chapterRows[i].Cells[ColEp].Value = cp.EpisodeId is int ceid ? EpNoOf(ceid) : "";
                    chapterRows[i].Cells[ColSeq].Value = cp.EpisodeSeq is byte s ? s.ToString(CultureInfo.InvariantCulture) : "";
                    ResolveRow(chapterRows[i]);
                }
            }
            var large = CollectLargeDiffs();
            _lblHint.Text = $"当たった話 {matched} / プレイリスト {proposals.Count}"
                + (ambiguous > 0 ? $"（候補が複数のもの {ambiguous}）" : "")
                + (large.Count > 0 ? $"　差 1 秒超 {large.Count} 件（円盤尺の再計測の候補）" : "");
        }

        /// <summary>
        /// 当てたチャプターのうち、チャプターの尺と期待する尺（DB の円盤尺。話の最後のパートは末尾の余白 1 秒込み）の差が 1 秒を超えるもの
        /// （「第 N 話 パート k：差 +3.5 秒」の形）。
        /// チャプターの区切りが粗いだけのこともあるが、DB の円盤尺のほうが古い計測で狂っていることもあるので、再計測の候補として知らせる。
        /// </summary>
        private List<string> CollectLargeDiffs()
        {
            var list = new List<string>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not RowTag { Chapter: { } ch }) continue;
                var ep = EpisodeOf(row.Cells[ColEp].Value?.ToString());
                if (ep is null || !byte.TryParse(row.Cells[ColSeq].Value?.ToString(), out var seq)) continue;
                if (!_partsByEpisode.TryGetValue(ep.EpisodeId, out var parts)) continue;
                var part = parts.FirstOrDefault(p => p.EpisodeSeq == seq);
                if (part?.DiscLength is not ushort dl) continue;
                bool isLast = IsLastDiscPart(ep, part);
                long diff = (long)ch.DurationMs - EpisodeLinkMatcher.ExpectedMs(part, isLast);
                if (!EpisodeLinkMatcher.IsWithinTolerance(diff))
                    list.Add($"第 {ep.SeriesEpNo} 話 {part.PartType}（DB {dl} 秒{(isLast ? "＋余白 1 秒" : "")}）：差 {diff / 1000.0:+0.0;-0.0} 秒");
            }
            return list;
        }

        /// <summary>話数・パート順のセルから、パート名・円盤尺・差の列を埋め直す（当たらなければ赤く）。</summary>
        private void ResolveRow(DataGridViewRow row)
        {
            if (row.Tag is not RowTag tag) return;
            var ep = EpisodeOf(row.Cells[ColEp].Value?.ToString());
            if (tag.Chapter is null)
            {
                row.Cells[ColEp].Style.ForeColor = ep is null && !string.IsNullOrWhiteSpace(row.Cells[ColEp].Value?.ToString()) ? Color.Red : SystemColors.ControlText;
                if (ep is not null) row.Cells[ColPart].Value = ep.TitleText;
                return;
            }
            var part = ep is not null && byte.TryParse(row.Cells[ColSeq].Value?.ToString(), out var seq)
                && _partsByEpisode.TryGetValue(ep.EpisodeId, out var parts)
                ? parts.FirstOrDefault(p => p.EpisodeSeq == seq) : null;
            bool wants = !string.IsNullOrWhiteSpace(row.Cells[ColEp].Value?.ToString()) || !string.IsNullOrWhiteSpace(row.Cells[ColSeq].Value?.ToString());
            row.Cells[ColPart].Style.ForeColor = wants && part is null ? Color.Red : SystemColors.ControlText;
            if (part is null)
            {
                row.Cells[ColPart].Value = wants ? "（話またはパートが見つかりません）" : "";
                row.Cells[ColDiff].Value = "";
                return;
            }
            bool isLast = IsLastDiscPart(ep!, part);
            row.Cells[ColPart].Value = part.DiscLength is ushort d
                ? $"{part.PartType}（{d} 秒{(isLast ? "＋余白 1 秒" : "")}）"
                : $"{part.PartType}（円盤尺なし）";
            if (part.DiscLength is not null)
            {
                long diff = (long)tag.Chapter.DurationMs - EpisodeLinkMatcher.ExpectedMs(part, isLast);
                row.Cells[ColDiff].Value = (diff / 1000.0).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
                row.Cells[ColDiff].Style.ForeColor = EpisodeLinkMatcher.IsWithinTolerance(diff) ? SystemColors.ControlText : Color.Red;
            }
            else row.Cells[ColDiff].Value = "";
        }

        /// <summary>表の内容を BdChapter / BdPlaylist に書き戻す。解決できない指定があれば止める。</summary>
        private bool Apply()
        {
            _grid.EndEdit();
            // 差の大きいチャプターがあれば、登録の前に円盤尺の再計測を勧める（紐付けはそのまま通せる）。
            var large = CollectLargeDiffs();
            if (large.Count > 0)
            {
                var shown = large.Take(12).ToList();
                string nl = Environment.NewLine;
                string more = large.Count > shown.Count ? nl + "…ほか " + (large.Count - shown.Count) + " 件" : "";
                var answer = MessageBox.Show(this,
                    $"チャプターの尺と DB の円盤尺（話の最後のパートは末尾の余白 1 秒込み）の差が 1 秒を超えるパートが {large.Count} 件あります。" + nl
                    + "DB の円盤尺（episode_parts.disc_length）の再計測をおすすめします（この紐付け自体はこのまま登録できます）。" + nl + nl
                    + string.Join(nl, shown) + more + nl + nl + "このまま OK にしますか？",
                    "円盤尺の再計測の候補", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer != DialogResult.OK) return false;
            }
            DataGridViewRow? head = null;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not RowTag tag) continue;
                string kind = row.Cells[ColKind].Value?.ToString() ?? "";
                if (tag.Playlist is { } pl)
                {
                    head = row;
                    if (_group is not null)
                    {
                        int? work = WorkIdOf(row);
                        if (work is not null && kind.Length == 0) kind = "FEATURE";
                        pl.PlaylistKind = kind.Length == 0 ? null : kind;
                        pl.EpisodeId = null;
                        pl.SeriesId = work;
                        continue;
                    }
                }
                if (_group is not null)
                {
                    var c = tag.Chapter!;
                    int? effective = EffectiveWorkOf(row, head);
                    if (effective is not null && kind.Length == 0) kind = "FEATURE";
                    c.ChapterKind = kind.Length == 0 ? null : kind;
                    c.EpisodeId = null;
                    c.EpisodeSeq = null;
                    c.SeriesId = effective;
                    continue;
                }

                string epText = row.Cells[ColEp].Value?.ToString() ?? "";
                var ep = EpisodeOf(epText);
                if (!string.IsNullOrWhiteSpace(epText) && ep is null)
                {
                    MessageBox.Show(this, $"{row.Cells[ColName].Value}：話数「{epText}」がこの作品にありません。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (tag.Playlist is { } tvPl)
                {
                    tvPl.PlaylistKind = string.IsNullOrEmpty(kind) ? null : kind;
                    tvPl.EpisodeId = ep?.EpisodeId;
                    tvPl.SeriesId = null;
                    continue;
                }
                var ch = tag.Chapter!;
                string seqText = row.Cells[ColSeq].Value?.ToString() ?? "";
                byte? seq = null;
                if (!string.IsNullOrWhiteSpace(seqText))
                {
                    if (ep is null || !byte.TryParse(seqText, out var s) || !_partsByEpisode.TryGetValue(ep.EpisodeId, out var parts) || parts.All(p => p.EpisodeSeq != s))
                    {
                        MessageBox.Show(this, $"{row.Cells[ColName].Value}：パート順「{seqText}」が第 {epText} 話にありません。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                    seq = s;
                }
                if ((ep is null) != (seq is null))
                {
                    MessageBox.Show(this, $"{row.Cells[ColName].Value}：話数とパート順は両方入れるか、両方空にしてください。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                ch.ChapterKind = string.IsNullOrEmpty(kind) ? null : kind;
                ch.EpisodeId = ep?.EpisodeId;
                ch.EpisodeSeq = seq;
                ch.SeriesId = null;
            }
            return true;
        }

        /// <summary>そのパートが、話の円盤尺を持つパートのうち最後のものか（話の最後のチャプターには末尾の余白 1 秒が付く）。</summary>
        private bool IsLastDiscPart(Episode ep, EpisodePart part)
            => _partsByEpisode.TryGetValue(ep.EpisodeId, out var parts)
               && parts.LastOrDefault(p => p.DiscLength is not null) is { } last && last.EpisodeSeq == part.EpisodeSeq;

        private Episode? EpisodeOf(string? epNoText)
            => int.TryParse(epNoText, out var n) ? _episodes.FirstOrDefault(e => e.SeriesEpNo == n) : null;

        private string EpNoOf(int episodeId)
            => _episodes.FirstOrDefault(e => e.EpisodeId == episodeId)?.SeriesEpNo.ToString(CultureInfo.InvariantCulture) ?? "";

        private static string FormatSec(ulong ms) => (ms / 1000.0).ToString("0.0", CultureInfo.InvariantCulture);

        private sealed record RowTag(BdPlaylist? Playlist, BdChapter? Chapter);
    }
}
