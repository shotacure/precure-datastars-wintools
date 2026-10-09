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
    /// 種別・話数・パート順はセルで直せる。OK で <see cref="BdChapter"/>（chapter_kind / episode_id / episode_seq）と
    /// <see cref="BdPlaylist"/>（playlist_kind / episode_id）に書き戻す（DB には触れない。記録は MainForm の「Blu-ray の情報を記録」で行う）。
    /// 尺の列は、チャプターの尺（Blu-ray の生の値）とパートの円盤尺（DB の値）の差を見せるだけで、どちらも書き換えない。
    /// 話の最後のチャプター（ユニットの末尾）には 1 秒の余白が付いているので、話の最後のパートは円盤尺 + 1 秒と比べる。
    /// 映画など作品単位の作品（話を持たない）を選ぶと、「自動で当てる」は上映時間に合うプレイリストを本編（FEATURE）にし、話・パートは空のまま。
    /// 本編の先頭の黒み（秒。既定 2）を入れると「プレイリストの尺 − 黒み」を上映時間として作品（<c>series.run_time_seconds</c>）に入れられる
    /// （<see cref="FeatureRunTimeSeconds"/>。DB への書き込みは MainForm の「Blu-ray の情報を記録」のときに bd_* と一緒に行う）。
    /// </summary>
    public sealed class EpisodeLinkDialog : Form
    {
        private static readonly string[] PlaylistKinds = { "", "EPISODE", "PLAY_ALL", "FEATURE", "BONUS", "MENU", "OTHER" };
        private static readonly string[] ChapterKinds = { "", "EPISODE_PART", "FEATURE", "BLANK", "BONUS", "OTHER" };

        private readonly SeriesRepository _seriesRepo;
        private readonly EpisodesRepository _episodesRepo;
        private readonly EpisodePartsRepository _partsRepo;
        private readonly IReadOnlyList<BdChapter> _chapters;
        private readonly IReadOnlyList<BdPlaylist> _playlists;
        private readonly int? _defaultSeriesId;

        private readonly ComboBox _cboSeries = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
        private readonly Button _btnAuto = new() { Text = "自動で当てる", Width = 130, Height = 28 };
        private readonly Label _lblHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
        // 作品単位の本編：先頭の黒み（秒）と、黒みを引いた尺を作品の上映時間に入れるかどうか
        private readonly Label _lblLeadBlack = new() { Text = "先頭の黒み（秒）：", AutoSize = true, Visible = false };
        private readonly NumericUpDown _numLeadBlack = new() { Minimum = 0, Maximum = 60, DecimalPlaces = 1, Increment = 0.1m, Value = 2, Width = 64, Visible = false };
        private readonly CheckBox _chkRunTime = new() { AutoSize = true, Visible = false };
        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
        };

        /// <summary>OK のときに選ばれていた作品（bd_discs.series_id に入れる）。</summary>
        public int? SelectedSeriesId { get; private set; }

        /// <summary>OK のとき、作品の上映時間（series.run_time_seconds）に入れる秒数（本編のプレイリストの尺 − 先頭の黒み）。入れないなら null。</summary>
        public ushort? FeatureRunTimeSeconds { get; private set; }

        private IReadOnlyList<Episode> _episodes = Array.Empty<Episode>();
        private Dictionary<int, IReadOnlyList<EpisodePart>> _partsByEpisode = new();

        /// <summary>選んでいる作品（話を持たない作品単位の作品なら、本編のプレイリストを当てる）。</summary>
        private Series? _series;

        private const int ColName = 0, ColLen = 1, ColKind = 2, ColEp = 3, ColSeq = 4, ColPart = 5, ColDiff = 6;


        public EpisodeLinkDialog(
            SeriesRepository seriesRepo, EpisodesRepository episodesRepo, EpisodePartsRepository partsRepo,
            IReadOnlyList<BdChapter> chapters, IReadOnlyList<BdPlaylist> playlists, int? defaultSeriesId)
        {
            _seriesRepo = seriesRepo; _episodesRepo = episodesRepo; _partsRepo = partsRepo;
            _chapters = chapters; _playlists = playlists; _defaultSeriesId = defaultSeriesId;

            Text = "話とパートを当てる";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1100, 700);
            MinimumSize = new Size(800, 400);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
            top.Controls.Add(new Label { Text = "作品：", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            top.Controls.Add(_cboSeries);
            top.Controls.Add(_btnAuto);
            top.Controls.Add(_lblLeadBlack);
            top.Controls.Add(_numLeadBlack);
            top.Controls.Add(_chkRunTime);
            top.Controls.Add(_lblHint);
            _lblLeadBlack.Margin = new Padding(12, 7, 0, 0);
            _numLeadBlack.Margin = new Padding(0, 4, 0, 0);
            _chkRunTime.Margin = new Padding(8, 6, 0, 0);
            _lblHint.Margin = new Padding(12, 7, 0, 0);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var btnCancel = new Button { Text = "キャンセル", Width = 100, Height = 28, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "OK", Width = 100, Height = 28 };
            bottom.Controls.Add(btnCancel);
            bottom.Controls.Add(btnOk);
            AcceptButton = btnOk; CancelButton = btnCancel;

            BuildGrid();
            Controls.Add(_grid); Controls.Add(top); Controls.Add(bottom);

            Load += async (_, _) => await LoadSeriesAsync();
            _btnAuto.Click += async (_, _) => await AutoMatchAsync();
            _grid.CellEndEdit += (_, e) =>
            {
                if (e.ColumnIndex is ColEp or ColSeq) ResolveRow(_grid.Rows[e.RowIndex]);
                if (e.ColumnIndex == ColKind) UpdateRunTime();
            };
            _numLeadBlack.ValueChanged += (_, _) => UpdateRunTime();
            btnOk.Click += (_, _) =>
            {
                if (!Apply()) return;
                SelectedSeriesId = _cboSeries.SelectedValue as int?;
                FeatureRunTimeSeconds = IsFeatureSeries && _chkRunTime.Checked ? ComputeRunTimeSeconds() : null;
                DialogResult = DialogResult.OK;
            };
        }

        /// <summary>表の列を作り、プレイリストごとに見出し行とチャプター行を並べる。</summary>
        private void BuildGrid()
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "プレイリスト / チャプター", Width = 220, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "チャプター尺", Width = 110, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "種別", Width = 130, FlatStyle = FlatStyle.Flat });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "話数", Width = 60 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "パート順", Width = 70 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "パート（DB の円盤尺）", Width = 260, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "差（秒）", Width = 80, ReadOnly = true });

            foreach (var group in _chapters.GroupBy(c => c.PlaylistFile ?? "", StringComparer.Ordinal))
            {
                var playlist = _playlists.FirstOrDefault(p => string.Equals(p.PlaylistFile, group.Key, StringComparison.OrdinalIgnoreCase));
                ulong total = (ulong)group.Sum(c => (decimal)c.DurationMs);
                int r = _grid.Rows.Add($"[{group.Key}]", FormatSec(total), playlist?.PlaylistKind ?? "", "", "", "", "");
                var row = _grid.Rows[r];
                row.Tag = new RowTag(playlist, null);
                row.DefaultCellStyle.BackColor = SystemColors.ControlLight;
                row.Cells[ColSeq].ReadOnly = true;
                SetKindItems(row, PlaylistKinds);
                if (playlist?.EpisodeId is int pid) row.Cells[ColEp].Value = EpNoOf(pid);

                int i = 0;
                foreach (var ch in group)
                {
                    i++;
                    int cr = _grid.Rows.Add($"    {i}", FormatSec(ch.DurationMs), ch.ChapterKind ?? "", "", "", "", "");
                    var crow = _grid.Rows[cr];
                    crow.Tag = new RowTag(null, ch);
                    SetKindItems(crow, ChapterKinds);
                    if (ch.EpisodeId is int eid) crow.Cells[ColEp].Value = EpNoOf(eid);
                    if (ch.EpisodeSeq is byte seq) crow.Cells[ColSeq].Value = seq.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static void SetKindItems(DataGridViewRow row, string[] items)
        {
            var cell = (DataGridViewComboBoxCell)row.Cells[ColKind];
            cell.Items.AddRange(items);
        }

        private async Task LoadSeriesAsync()
        {
            try
            {
                // TV も映画なども全部（作品 ID 順）。話を持たない作品は本編のプレイリストで当てる
                var all = (await _seriesRepo.GetAllAsync()).OrderBy(s => s.SeriesId).ToList();
                _cboSeries.DisplayMember = nameof(Series.Title);
                _cboSeries.ValueMember = nameof(Series.SeriesId);
                _cboSeries.DataSource = all;
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
            _lblHint.Text = IsFeatureSeries
                ? "作品単位（本編のプレイリストを FEATURE に）" + (_series?.RunTimeSeconds is ushort rt ? $"・上映時間 {FormatRunTime(rt)}" : "・上映時間なし")
                : $"話 {_episodes.Count}・円盤尺のあるパート {_partsByEpisode.Values.Sum(l => l.Count(p => p.DiscLength is not null))}";
            _lblLeadBlack.Visible = _numLeadBlack.Visible = _chkRunTime.Visible = IsFeatureSeries;
            // 上映時間の無い作品は入れる前提、ある作品は見比べてから入れる
            _chkRunTime.Checked = IsFeatureSeries && _series?.RunTimeSeconds is null;
            UpdateRunTime();
        }

        /// <summary>本編（FEATURE）のプレイリストの尺 − 先頭の黒み（秒）。本編のプレイリストが無ければ null。</summary>
        private ushort? ComputeRunTimeSeconds()
        {
            var feature = _grid.Rows.Cast<DataGridViewRow>()
                .Select(r => r.Tag as RowTag)
                .FirstOrDefault(t => t?.Playlist is not null && PlaylistKindOf(t.Playlist.PlaylistFile) == "FEATURE");
            if (feature?.Playlist is null) return null;
            double sec = feature.Playlist.DurationMs / 1000.0 - (double)_numLeadBlack.Value;
            return sec <= 0 ? null : (ushort)Math.Min(ushort.MaxValue, Math.Round(sec));
        }

        /// <summary>表の見出し行に入っているプレイリストの種別。</summary>
        private string? PlaylistKindOf(string? playlistFile)
            => _grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => r.Tag is RowTag { Playlist: { } pl } && string.Equals(pl.PlaylistFile, playlistFile, StringComparison.OrdinalIgnoreCase))
                ?.Cells[ColKind].Value?.ToString();

        /// <summary>「尺 N 秒を作品の上映時間に入れる」の文を、本編のプレイリストと黒みから作り直す。</summary>
        private void UpdateRunTime()
        {
            if (!IsFeatureSeries) return;
            var sec = ComputeRunTimeSeconds();
            if (sec is null)
            {
                _chkRunTime.Enabled = false;
                _chkRunTime.Text = "本編（FEATURE）のプレイリストがありません";
                return;
            }
            _chkRunTime.Enabled = true;
            string now = _series?.RunTimeSeconds is ushort rt && rt != sec ? $"（今は {rt} 秒）" : _series?.RunTimeSeconds == sec ? "（今と同じ）" : "";
            _chkRunTime.Text = $"尺 {sec} 秒（{FormatRunTime(sec.Value)}）を作品の上映時間に入れる{now}";
        }

        private static string FormatRunTime(ushort sec) => $"{sec / 60}:{sec % 60:00}";

        /// <summary>選んでいる作品が話を持たない（映画など作品単位）か。</summary>
        private bool IsFeatureSeries => _series is not null && _episodes.Count == 0;

        /// <summary>作品単位の作品：上映時間に合う（無ければいちばん長い）プレイリストを本編にし、ほかの行は触らない。</summary>
        private void AutoMatchFeature()
        {
            if (_series is null) return;
            var inputs = _playlists.Select(p => (p.PlaylistFile ?? "", p.DurationMs)).ToList();
            var (file, diff) = EpisodeLinkMatcher.ProposeFeature(inputs, _series);
            if (file is null)
            {
                _lblHint.Text = $"上映時間（{_series.RunTimeSeconds / 60} 分）に合うプレイリストがありません";
                return;
            }
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not RowTag tag) continue;
                string pf = tag.Playlist?.PlaylistFile ?? tag.Chapter?.PlaylistFile ?? "";
                if (!string.Equals(pf, file, StringComparison.OrdinalIgnoreCase)) continue;
                row.Cells[ColKind].Value = "FEATURE";
                row.Cells[ColEp].Value = "";
                row.Cells[ColSeq].Value = "";
                if (tag.Chapter is null)
                    row.Cells[ColPart].Value = _series.Title + (diff is long d ? $"　差 {d / 1000.0:+0.0;-0.0} 秒" : "　（上映時間なし。いちばん長いプレイリスト）");
                else
                    row.Cells[ColPart].Value = "";
                row.Cells[ColDiff].Value = "";
            }
            _lblHint.Text = $"本編 {file}" + (diff is long dd ? $"（上映時間との差 {dd / 1000.0:+0.0;-0.0} 秒）" : "（上映時間なし・要確認）");
            UpdateRunTime();
        }

        /// <summary>作品の各話のパート尺と突き合わせ、提案で表を埋める。</summary>
        private async Task AutoMatchAsync()
        {
            if (_episodes.Count == 0) await LoadEpisodesAsync();
            if (IsFeatureSeries)
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
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not RowTag tag) continue;
                string kind = row.Cells[ColKind].Value?.ToString() ?? "";
                string epText = row.Cells[ColEp].Value?.ToString() ?? "";
                var ep = EpisodeOf(epText);
                if (!string.IsNullOrWhiteSpace(epText) && ep is null)
                {
                    MessageBox.Show(this, $"{row.Cells[ColName].Value}：話数「{epText}」がこの作品にありません。", "確認", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (tag.Playlist is { } pl)
                {
                    pl.PlaylistKind = string.IsNullOrEmpty(kind) ? null : kind;
                    pl.EpisodeId = ep?.EpisodeId;
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
