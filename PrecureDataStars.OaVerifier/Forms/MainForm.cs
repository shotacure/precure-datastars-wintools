using System.ComponentModel;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.OaVerifier.Ts;

namespace PrecureDataStars.OaVerifier.Forms;

/// <summary>
/// 本放送フォーマット検証ツールのメインフォーム。
/// TS を再生し、TOT から放送日を確定して該当エピソードのパート境界を頭出しする。
/// 確認のため全パートを表示し、【本放送未確認】パートを薄い赤で強調する。
/// 再生は「未承認パート通し」「全パート通し」の 2 種のみ。承認したパートの notes から
/// マーカーを除去する。
///
/// <para>スレッド方針（重要）：libVLC の制御メソッド（Play/Pause/シーク/トラック選択）を
/// UI スレッドから呼ぶと、VideoView 描画のため UI スレッドを要求する VLC 映像出力スレッドと
/// ロックを奪い合ってデッドロック（フリーズ）する。よって <b>libVLC への制御呼び出しは
/// すべて <see cref="RunPlayer"/> 経由でスレッドプールに逃がす</b>。現在時刻は VLC の
/// <c>TimeChanged</c> イベントでキャッシュ（<see cref="_lastTimeMs"/>）し、UI タイマーは
/// その値を表示するだけで libVLC を一切叩かない。</para>
/// </summary>
internal sealed class MainForm : Form
{
    private const string Marker = "【本放送未確認】";
    private const string AuditUser = "oa-verifier";
    private static readonly Color UnconfirmedBack = Color.FromArgb(255, 224, 224); // 薄い赤

    /// <summary>確認幅（境界の前後）の選択肢（ミリ秒）。UI のコンボと対応。</summary>
    private static readonly long[] WindowOptionsMs = { 500, 1000, 2000, 3000 };

    /// <summary>現在の確認幅（境界の前後、ミリ秒）。既定 ±2 秒。UI のコンボで変更可。</summary>
    private long _halfWindowMs = 2000;

    // ── リポジトリ ──
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly EpisodePartsRepository _partsRepo;

    // ── 再生 ──
    private readonly LibVLC _libvlc;
    private readonly MediaPlayer _player;
    private readonly VideoView _videoView;
    private Media? _media;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _tracksSelected;
    private bool _pendingPauseAfterStart;

    // ── 再生状態（スレッド間共有。Interlocked / lock で調停）──
    private long _lastTimeMs;            // TimeChanged でキャッシュした現在時刻
    private long _lastLenMs;             // LengthChanged でキャッシュした総尺
    private CancellationTokenSource? _sweepCts; // 進行中の通し再生（キャンセルで停止/差し替え）
    private const long SeekSettleMs = 400;      // シーク着地ぶんの上乗せ滞在時間

    /// <summary>頭出し対象の 1 境界（センター時刻＋どのパートのどちら側か）。</summary>
    private sealed record CuePoint(long CenterMs, byte EpisodeSeq, string Label);

    // ── 状態 ──
    private TsTimebase? _timebase;
    private Episode? _episode;
    private readonly BindingList<PartRow> _partRows = new();
    private Dictionary<int, Series> _seriesById = new();
    private List<Episode> _allEpisodes = new();
    private long _programStartMediaMs;   // 番組先頭（オフセット0）のメディア時刻
    private bool _anchorIsManual;        // 番組先頭を手動再アンカー済みか
    private bool _suppressEpisodeComboEvent;
    private bool _suppressTrackComboEvent;

    // ── UI ──
    private SplitContainer _split = null!;
    private readonly Button _openButton = new();
    private readonly Label _fileLabel = new();
    private readonly Label _episodeLabel = new();
    private readonly Label _diagLabel = new();
    private readonly ComboBox _episodeCombo = new();
    private readonly Button _reloadButton = new();
    private readonly DataGridView _grid = new();
    private readonly Button _sweepUnconfirmedButton = new();
    private readonly Button _sweepAllButton = new();
    private readonly Button _approveButton = new();
    private readonly Button _approveAllButton = new();
    private readonly Label _remainLabel = new();
    private readonly Label _timeLabel = new();
    private readonly ComboBox _videoCombo = new();
    private readonly ComboBox _audioCombo = new();
    private readonly Button _reanchorButton = new();
    private readonly Button _totAnchorButton = new();
    private readonly ComboBox _windowCombo = new();
    private readonly Label _anchorLabel = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusLabel = new();

    public MainForm(SeriesRepository seriesRepo, EpisodesRepository episodesRepo, EpisodePartsRepository partsRepo)
    {
        _seriesRepo = seriesRepo;
        _episodesRepo = episodesRepo;
        _partsRepo = partsRepo;

        _libvlc = new LibVLC();
        _player = new MediaPlayer(_libvlc) { EnableKeyInput = false, EnableMouseInput = false };
        _videoView = new VideoView { MediaPlayer = _player, Dock = DockStyle.Fill, BackColor = Color.Black };

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += Timer_Tick;

        BuildUi();

        // VLC イベント（VLC スレッドから発火）。制御呼び出しは RunPlayer へ、UI 反映は BeginInvoke へ。
        _player.Playing += Player_Playing;
        _player.TimeChanged += Player_TimeChanged;
        _player.LengthChanged += Player_LengthChanged;
        _player.EndReached += Player_EndReached;

        Load += MainForm_Load;
        Shown += MainForm_Shown;
        FormClosed += MainForm_FormClosed;
    }

    /// <summary>libVLC の制御呼び出しを UI スレッドから外してスレッドプールで実行する（デッドロック回避）。</summary>
    private static void RunPlayer(Action act)
        => ThreadPool.QueueUserWorkItem(_ => { try { act(); } catch { /* 終了競合等は無視 */ } });

    // ── UI 構築 ──

    private void BuildUi()
    {
        Text = "本放送フォーマット検証 (OaVerifier)";
        ClientSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        DragEnter += MainForm_DragEnter;
        DragDrop += MainForm_DragDrop;

        // 上部バー
        var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(8) };
        _openButton.Text = "TS を開く…";
        _openButton.Location = new Point(8, 8);
        _openButton.Size = new Size(110, 28);
        _openButton.Click += async (_, _) => await OpenViaDialogAsync();

        _fileLabel.Text = "(ファイル未選択 — TS をドラッグ＆ドロップでも開けます)";
        _fileLabel.AutoSize = false;
        _fileLabel.Location = new Point(128, 6);
        _fileLabel.Size = new Size(1030, 16);
        _fileLabel.ForeColor = Color.DimGray;

        _episodeLabel.Text = "エピソード未同定";
        _episodeLabel.AutoSize = false;
        _episodeLabel.Location = new Point(128, 24);
        _episodeLabel.Size = new Size(1030, 16);
        _episodeLabel.Font = new Font(Font, FontStyle.Bold);

        _diagLabel.Text = "";
        _diagLabel.AutoSize = false;
        _diagLabel.Location = new Point(128, 42);
        _diagLabel.Size = new Size(1030, 16);
        _diagLabel.ForeColor = Color.DimGray;

        top.Controls.Add(_openButton);
        top.Controls.Add(_fileLabel);
        top.Controls.Add(_episodeLabel);
        top.Controls.Add(_diagLabel);

        // ステータスバー
        _statusStrip.Items.Add(_statusLabel);
        _statusLabel.Text = "準備完了";

        // 中央分割（SplitterDistance は Shown で確定させる：InitializeComponent 時点では効かない）
        _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 360 };

        BuildLeftPanel(_split.Panel1);
        BuildRightPanel(_split.Panel2);

        Controls.Add(_split);
        Controls.Add(top);
        Controls.Add(_statusStrip);
    }

    private void BuildLeftPanel(Control host)
    {
        // エピソード選択（自動同定の上書き）＋リロード
        var epPanel = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(6) };
        _reloadButton.Text = "パートデータをリロード";
        _reloadButton.Dock = DockStyle.Bottom;
        _reloadButton.Height = 28;
        _reloadButton.Click += async (_, _) => await ReloadPartsAsync();
        _episodeCombo.Dock = DockStyle.Top;
        _episodeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _episodeCombo.SelectedIndexChanged += async (_, _) => await EpisodeCombo_ChangedAsync();
        var epCaption = new Label { Text = "エピソード（手動上書き可）:", Dock = DockStyle.Top, Height = 18 };
        epPanel.Controls.Add(_episodeCombo);
        epPanel.Controls.Add(epCaption);
        epPanel.Controls.Add(_reloadButton);

        // パート一覧（全パート表示、未確認は薄い赤で強調）
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", DataPropertyName = nameof(PartRow.EpisodeSeq), FillWeight = 8 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "種別", DataPropertyName = nameof(PartRow.PartType), FillWeight = 34 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "開始", DataPropertyName = nameof(PartRow.StartLabel), FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "終了", DataPropertyName = nameof(PartRow.EndLabel), FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "尺", DataPropertyName = nameof(PartRow.OaLabel), FillWeight = 12 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状態", DataPropertyName = nameof(PartRow.StatusLabel), FillWeight = 16 });
        _grid.DataSource = _partRows;
        _grid.CellFormatting += Grid_CellFormatting;
        _grid.CellDoubleClick += Grid_CellDoubleClick;

        // 操作ボタン群（通し 2 種 ＋ 承認 2 種）
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 104, ColumnCount = 2, RowCount = 3, Padding = new Padding(4) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _sweepUnconfirmedButton.Text = "▶ 未承認パート通し";
        _sweepUnconfirmedButton.Dock = DockStyle.Fill;
        _sweepUnconfirmedButton.Click += (_, _) => SweepUnconfirmed();
        _sweepAllButton.Text = "▶ 全パート通し";
        _sweepAllButton.Dock = DockStyle.Fill;
        _sweepAllButton.Click += (_, _) => SweepAll();
        _approveButton.Text = "選択を承認（除去）";
        _approveButton.Dock = DockStyle.Fill;
        _approveButton.Click += async (_, _) => await ApproveSelectedAsync();
        _approveAllButton.Text = "全パート承認";
        _approveAllButton.Dock = DockStyle.Fill;
        _approveAllButton.Click += async (_, _) => await ApproveAllAsync();
        _remainLabel.Text = "確認対象なし";
        _remainLabel.Dock = DockStyle.Fill;
        _remainLabel.TextAlign = ContentAlignment.MiddleLeft;
        buttons.Controls.Add(_sweepUnconfirmedButton, 0, 0);
        buttons.Controls.Add(_sweepAllButton, 1, 0);
        buttons.Controls.Add(_approveButton, 0, 1);
        buttons.Controls.Add(_approveAllButton, 1, 1);
        buttons.Controls.Add(_remainLabel, 0, 2);
        buttons.SetColumnSpan(_remainLabel, 2);

        host.Controls.Add(_grid);
        host.Controls.Add(buttons);
        host.Controls.Add(epPanel);
    }

    private void BuildRightPanel(Control host)
    {
        var transport = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 124, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        // 1 行目：±5秒 / ±15秒 送り戻し ＋ 時刻
        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        foreach (var (label, delta) in new (string, long)[] { ("◀◀ 15s", -15000), ("◀ 5s", -5000), ("5s ▶", 5000), ("15s ▶▶", 15000) })
        {
            var b = new Button { Text = label, Size = new Size(70, 28) };
            long d = delta;
            b.Click += (_, _) => Nudge(d);
            row1.Controls.Add(b);
        }
        _timeLabel.AutoSize = false;
        _timeLabel.Size = new Size(420, 28);
        _timeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _timeLabel.Text = "00:00.000 / 00:00.000";
        row1.Controls.Add(_timeLabel);

        // 2 行目：トラック選択
        var row2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _videoCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _videoCombo.Width = 320;
        _videoCombo.SelectedIndexChanged += (_, _) => OnVideoComboChanged();
        _audioCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _audioCombo.Width = 320;
        _audioCombo.SelectedIndexChanged += (_, _) => OnAudioComboChanged();
        row2.Controls.Add(new Label { Text = "映像:", AutoSize = true, Padding = new Padding(2, 8, 2, 0) });
        row2.Controls.Add(_videoCombo);
        row2.Controls.Add(new Label { Text = "音声:", AutoSize = true, Padding = new Padding(8, 8, 2, 0) });
        row2.Controls.Add(_audioCombo);

        // 3 行目：再アンカー・確認幅
        var row3 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _reanchorButton.Text = "この位置を番組先頭に再アンカー";
        _reanchorButton.Size = new Size(220, 28);
        _reanchorButton.Click += (_, _) => ReanchorToCurrent();
        _totAnchorButton.Text = "TOT 基準に戻す";
        _totAnchorButton.Size = new Size(120, 28);
        _totAnchorButton.Click += (_, _) => ResetAnchorToTot();
        _windowCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _windowCombo.Width = 84;
        _windowCombo.Items.AddRange(new object[] { "±0.5s", "±1.0s", "±2.0s", "±3.0s" });
        _windowCombo.SelectedIndexChanged += (_, _) =>
        {
            int i = _windowCombo.SelectedIndex;
            if (i >= 0 && i < WindowOptionsMs.Length) Interlocked.Exchange(ref _halfWindowMs, WindowOptionsMs[i]);
        };
        _windowCombo.SelectedIndex = 2; // ±2.0s 既定（ハンドラ経由で _halfWindowMs を設定）
        _anchorLabel.AutoSize = false;
        _anchorLabel.Size = new Size(230, 28);
        _anchorLabel.TextAlign = ContentAlignment.MiddleLeft;
        _anchorLabel.Text = "アンカー: 未設定";
        row3.Controls.Add(_reanchorButton);
        row3.Controls.Add(_totAnchorButton);
        row3.Controls.Add(new Label { Text = "確認幅:", AutoSize = true, Padding = new Padding(8, 8, 2, 0) });
        row3.Controls.Add(_windowCombo);
        row3.Controls.Add(_anchorLabel);

        transport.Controls.Add(row1, 0, 0);
        transport.Controls.Add(row2, 0, 1);
        transport.Controls.Add(row3, 0, 2);

        host.Controls.Add(_videoView);
        host.Controls.Add(transport);
    }

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is PartRow r && r.Highlight)
            e.CellStyle.BackColor = UnconfirmedBack;
    }

    // ── 起動・終了 ──

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            SetStatus("マスタ読込中…");
            _seriesById = (await _seriesRepo.GetAllAsync()).ToDictionary(s => s.SeriesId);
            _allEpisodes = (await _episodesRepo.GetAllAsync()).OrderBy(x => x.OnAirAt).ToList();
            PopulateEpisodeCombo();
            SetStatus("準備完了。TS を開いてください。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "マスタ読込に失敗しました。\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void MainForm_Shown(object? sender, EventArgs e)
    {
        // 実描画後にスプリッタ位置を確定（コンストラクト時に設定するとフォーム既定サイズ基準で
        // 評価され意図した比率にならないため）。
        try { _split.SplitterDistance = 430; } catch { /* 範囲外なら既定のまま */ }
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _timer.Stop();
        StopSweep();
        _player.Playing -= Player_Playing;
        _player.TimeChanged -= Player_TimeChanged;
        _player.LengthChanged -= Player_LengthChanged;
        _player.EndReached -= Player_EndReached;
        // 破棄前に VideoView から切り離す（映像出力スレッドとのデッドロック回避）。
        try { _videoView.MediaPlayer = null; } catch { }
        try { _player.Stop(); } catch { }
        _media?.Dispose();
        _player.Dispose();
        _libvlc.Dispose();
    }

    // ── ファイルを開く（ダイアログ / DnD）──

    private async Task OpenViaDialogAsync()
    {
        using var dlg = new OpenFileDialog { Filter = "TS ファイル (*.ts;*.m2ts)|*.ts;*.m2ts|すべてのファイル (*.*)|*.*" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            await OpenTsAsync(dlg.FileName);
    }

    private void MainForm_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private async void MainForm_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await OpenTsAsync(files[0]);
    }

    private async Task OpenTsAsync(string path)
    {
        try
        {
            _fileLabel.Text = path;
            _fileLabel.ForeColor = Color.Black;
            SetStatus("TS を解析中（TOT / PCR）…");

            // TS の時刻基準を解析（重い処理なのでバックグラウンドで）。
            _timebase = await Task.Run(() => TsTimebase.Analyze(path));
            _diagLabel.Text = _timebase.Diagnostics;

            // 再生開始（制御呼び出しはバックグラウンド。トラック選択は Playing イベントで行う）。
            StartMedia(path);

            // 放送日からエピソードを自動同定。
            if (_timebase.BroadcastDate is DateOnly date)
                AutoIdentifyEpisode(date);
            else
                SetStatus("TOT から放送日を取得できませんでした。エピソードを手動選択してください。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "TS の読み込みに失敗しました。\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartMedia(string path)
    {
        var media = new Media(_libvlc, path, FromType.FromPath);
        media.AddOption(":input-fast-seek=0"); // 正確シーク（キーフレームスナップを避ける）

        var old = _media;
        _media = media;
        StopSweep();
        Interlocked.Exchange(ref _lastTimeMs, 0);
        _tracksSelected = false;
        _pendingPauseAfterStart = true;

        // 停止→再生は libVLC 制御なのでバックグラウンドで。
        RunPlayer(() =>
        {
            try { _player.Stop(); } catch { }
            old?.Dispose();
            _player.Play(media);
        });
        _timer.Start();
    }

    // ── 再生イベント（VLC スレッド）──

    private void Player_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        => Interlocked.Exchange(ref _lastTimeMs, e.Time); // 表示用に現在時刻をキャッシュするだけ

    private void Player_EndReached(object? sender, EventArgs e)
    {
        // 末尾到達。ここで libVLC を呼ぶと EndReached コールバック内呼び出しでデッドロックするため、
        // 進行中の通しを止めて UI を戻すだけにする。再生器は Ended 状態になるが、次の通し/送り戻し時に
        // SeekToMediaMs が貼り直して自己回復するのでフリーズしない。
        StopSweep();
        try { BeginInvoke(new Action(() => { SetStatus("末尾に到達（次の再生操作で復帰します）。"); HighlightPlayingRow(-1); })); } catch { }
    }

    private void Player_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        => Interlocked.Exchange(ref _lastLenMs, e.Length);

    private void Player_Playing(object? sender, EventArgs e)
    {
        if (_tracksSelected) return;
        _tracksSelected = true;

        // libVLC 読み取り/制御はバックグラウンドで実施し、UI（コンボ）反映だけ BeginInvoke。
        RunPlayer(() =>
        {
            var (vids, curV, auds, curA) = SelectFullsegAndGatherTracks();
            try { BeginInvoke(new Action(() => PopulateTrackCombos(vids, curV, auds, curA))); } catch { }

            if (_pendingPauseAfterStart)
            {
                _pendingPauseAfterStart = false;
                try { _player.SetPause(true); _player.Time = 0; } catch { }
                Interlocked.Exchange(ref _lastTimeMs, 0);
            }
        });
    }

    /// <summary>（バックグラウンド）フルセグ＝解像度最大の映像を選択し、トラック一覧を集める。</summary>
    private (List<TrackItem> vids, int curV, List<TrackItem> auds, int curA) SelectFullsegAndGatherTracks()
    {
        var vids = new List<TrackItem>();
        var auds = new List<TrackItem>();
        int curV = -1, curA = -1;
        try
        {
            var media = _player.Media;
            if (media != null && media.Tracks is { Length: > 0 } tracks)
            {
                MediaTrack? best = null;
                long bestPixels = -1;
                foreach (var t in tracks)
                {
                    if (t.TrackType != TrackType.Video) continue;
                    long px = (long)t.Data.Video.Width * t.Data.Video.Height;
                    if (px > bestPixels) { bestPixels = px; best = t; }
                }
                if (best is MediaTrack b) _player.SetVideoTrack(b.Id);
            }

            foreach (var d in _player.VideoTrackDescription) vids.Add(new TrackItem(d.Id, d.Name ?? $"映像 {d.Id}"));
            foreach (var d in _player.AudioTrackDescription) auds.Add(new TrackItem(d.Id, d.Name ?? $"音声 {d.Id}"));
            curV = _player.VideoTrack;
            curA = _player.AudioTrack;
        }
        catch { /* トラック未確定でも継続 */ }
        return (vids, curV, auds, curA);
    }

    private void PopulateTrackCombos(List<TrackItem> vids, int curV, List<TrackItem> auds, int curA)
    {
        _suppressTrackComboEvent = true;
        try
        {
            _videoCombo.Items.Clear();
            foreach (var v in vids) _videoCombo.Items.Add(v);
            SelectComboById(_videoCombo, curV);

            _audioCombo.Items.Clear();
            foreach (var a in auds) _audioCombo.Items.Add(a);
            SelectComboById(_audioCombo, curA);
        }
        finally { _suppressTrackComboEvent = false; }
    }

    private static void SelectComboById(ComboBox combo, int id)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (combo.Items[i] is TrackItem ti && ti.Id == id) { combo.SelectedIndex = i; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private void OnVideoComboChanged()
    {
        if (_suppressTrackComboEvent) return;
        if (_videoCombo.SelectedItem is TrackItem ti) RunPlayer(() => _player.SetVideoTrack(ti.Id));
    }

    private void OnAudioComboChanged()
    {
        if (_suppressTrackComboEvent) return;
        if (_audioCombo.SelectedItem is TrackItem ti) RunPlayer(() => _player.SetAudioTrack(ti.Id));
    }

    private sealed record TrackItem(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    // ── エピソード同定・読み込み ──

    private void PopulateEpisodeCombo()
    {
        _suppressEpisodeComboEvent = true;
        try
        {
            _episodeCombo.Items.Clear();
            foreach (var ep in _allEpisodes)
                _episodeCombo.Items.Add(new EpisodeChoice(ep, BuildEpisodeLabel(ep)));
        }
        finally { _suppressEpisodeComboEvent = false; }
    }

    private string BuildEpisodeLabel(Episode ep)
    {
        string title = _seriesById.TryGetValue(ep.SeriesId, out var s) ? s.Title : $"series {ep.SeriesId}";
        return $"{ep.OnAirDate:yyyy-MM-dd} {title} 第{ep.SeriesEpNo}話 {ep.TitleText}";
    }

    private void AutoIdentifyEpisode(DateOnly date)
    {
        var matches = _allEpisodes.Where(e => e.OnAirDate == date).ToList();
        if (matches.Count == 0)
        {
            SetStatus($"放送日 {date:yyyy-MM-dd} に該当するエピソードが見つかりません。手動選択してください。");
            return;
        }

        // 複数一致時は TV シリーズを優先。
        Episode pick = matches.FirstOrDefault(e =>
            _seriesById.TryGetValue(e.SeriesId, out var s) && s.KindCode == "TV") ?? matches[0];

        SelectEpisodeInCombo(pick); // SelectedIndexChanged 経由で LoadEpisode が走る
        if (matches.Count > 1)
            SetStatus($"放送日 {date:yyyy-MM-dd} に {matches.Count} 件一致。TV を選択しました（必要なら手動で切替）。");
    }

    private void SelectEpisodeInCombo(Episode ep)
    {
        for (int i = 0; i < _episodeCombo.Items.Count; i++)
            if (_episodeCombo.Items[i] is EpisodeChoice c && c.Episode.EpisodeId == ep.EpisodeId)
            {
                _episodeCombo.SelectedIndex = i;
                return;
            }
    }

    private async Task EpisodeCombo_ChangedAsync()
    {
        if (_suppressEpisodeComboEvent) return;
        if (_episodeCombo.SelectedItem is EpisodeChoice c)
            await LoadEpisodeAsync(c.Episode);
    }

    private async Task LoadEpisodeAsync(Episode ep)
    {
        try
        {
            _episode = ep;
            _episodeLabel.Text = BuildEpisodeLabel(ep) + $"（on_air {ep.OnAirAt:HH:mm:ss}）";

            var parts = await _partsRepo.GetByEpisodeAsync(ep.EpisodeId);
            BuildPartRows(parts);

            // TOT 自動アンカー（手動再アンカー済みなら据え置く）。
            if (!_anchorIsManual) ResetAnchorToTot(silent: true);
            UpdateAnchorLabel();
            UpdateRemainLabel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "エピソード読み込みに失敗しました。\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BuildPartRows(IReadOnlyList<EpisodePart> parts)
    {
        _partRows.RaiseListChangedEvents = false;
        _partRows.Clear();

        int cum = 0;
        foreach (var p in parts.OrderBy(x => x.EpisodeSeq))
        {
            int start = cum;
            int len = p.OaLength ?? 0;
            int end = cum + len;
            bool unconfirmed = (p.Notes ?? "").Contains(Marker, StringComparison.Ordinal);
            _partRows.Add(new PartRow
            {
                EpisodeSeq = p.EpisodeSeq,
                PartType = p.PartType,
                StartOffsetSec = start,
                EndOffsetSec = end,
                OaLengthSec = len,
                IsUnconfirmed = unconfirmed,
                Approved = false,
                Source = p
            });
            cum = end;
        }

        _partRows.RaiseListChangedEvents = true;
        _partRows.ResetBindings();
    }

    private async Task ReloadPartsAsync()
    {
        if (_episode is null) { SetStatus("エピソード未選択です。"); return; }
        try
        {
            // on_air_at の修正も取り込むため、エピソード本体も DB から取り直す。
            var fresh = (await _episodesRepo.GetBySeriesAsync(_episode.SeriesId))
                        .FirstOrDefault(e => e.EpisodeId == _episode.EpisodeId) ?? _episode;
            int idx = _allEpisodes.FindIndex(e => e.EpisodeId == fresh.EpisodeId);
            if (idx >= 0) _allEpisodes[idx] = fresh;

            await LoadEpisodeAsync(fresh);
            SetStatus($"パートデータをリロードしました（確認対象 {_partRows.Count(r => r.IsUnconfirmed && !r.Approved)} 件）。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "リロードに失敗しました。\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record EpisodeChoice(Episode Episode, string Label)
    {
        public override string ToString() => Label;
    }

    // ── 通し再生（境界 ±確認幅）──

    private long StartMsOf(PartRow r) => _programStartMediaMs + (long)r.StartOffsetSec * 1000;
    private long EndMsOf(PartRow r) => _programStartMediaMs + (long)r.EndOffsetSec * 1000;

    private void SweepUnconfirmed()
    {
        var rows = _partRows.Where(r => r.IsUnconfirmed && !r.Approved).ToList();
        if (rows.Count == 0) { SetStatus("未承認の【本放送未確認】パートがありません。"); return; }
        StartSweep(rows, "未承認パート通し");
    }

    private void SweepAll()
    {
        if (_partRows.Count == 0) { SetStatus("パートがありません。"); return; }
        StartSweep(_partRows, "全パート通し");
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is PartRow r)
            StartSweep(new[] { r }, $"{r.PartType}");
    }

    /// <summary>対象パート群について各境界の頭出し列を組み、直列ワーカーで順次再生する。
    /// 隣接パートが共有する同一時刻の境界（前パート終点＝次パート始点）は 1 つに統合して
    /// ラベルを連結する。</summary>
    private void StartSweep(IEnumerable<PartRow> rows, string label)
    {
        var groups = new SortedDictionary<long, List<(byte seq, string text, bool isStart)>>();
        void Add(long center, byte seq, string text, bool isStart)
        {
            if (!groups.TryGetValue(center, out var list)) { list = new(); groups[center] = list; }
            list.Add((seq, text, isStart));
        }
        foreach (var r in rows.OrderBy(x => x.EpisodeSeq))
        {
            Add(StartMsOf(r), r.EpisodeSeq, $"{r.PartType} 開始", true);
            Add(EndMsOf(r), r.EpisodeSeq, $"{r.PartType} 終了", false);
        }

        var cues = new List<CuePoint>();
        foreach (var kv in groups)
        {
            var items = kv.Value;
            // 強調行は「その境界で始まるパート」を優先（無ければ先頭＝終了側）。
            byte seq = items.FirstOrDefault(i => i.isStart).seq;
            if (seq == 0) seq = items[0].seq;
            cues.Add(new CuePoint(kv.Key, seq, string.Join(" / ", items.Select(i => i.text))));
        }
        if (cues.Count == 0) { SetStatus("再生対象がありません。"); return; }

        StopSweep();
        var cts = new CancellationTokenSource();
        _sweepCts = cts;
        SetStatus($"{label} を再生（{cues.Count} 境界）。");
        _ = Task.Run(() => RunSweepAsync(cues, cts.Token));
    }

    /// <summary>進行中の通し再生を停止（キャンセル）する。</summary>
    private void StopSweep()
    {
        var cts = _sweepCts;
        _sweepCts = null;
        try { cts?.Cancel(); cts?.Dispose(); } catch { }
    }

    /// <summary>（バックグラウンド・直列）各境界へ「PCR 索引によるバイト位置シーク → 壁時計で確認幅×2 滞在」を順に行う。
    /// 索引（<see cref="TsTimebase.PositionForMediaMs"/>）から実バイト位置を割り出して <c>Position</c> で一発シークするので、
    /// VLC の時刻シーク推定誤差に左右されず決定論的に着地する。再生したまま行い、終端でのみ 1 回ポーズする。</summary>
    private async Task RunSweepAsync(List<CuePoint> cues, CancellationToken ct)
    {
        try
        {
            foreach (var cue in cues)
            {
                if (ct.IsCancellationRequested) return;

                long half = Interlocked.Read(ref _halfWindowMs);
                long len = Interlocked.Read(ref _lastLenMs);
                long target = Math.Max(0, cue.CenterMs - half);   // 窓の始端
                if (len > 0) target = Math.Min(target, Math.Max(0, len - 200));

                SeekToMediaMs(target);
                try { BeginInvoke(new Action(() => { SetStatus($"再生中: {cue.Label}"); HighlightPlayingRow(cue.EpisodeSeq); })); } catch { }

                // 壁時計で確認幅×2（＋シーク着地ぶん）だけ滞在。
                try { await Task.Delay((int)(2 * half + SeekSettleMs), ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { return; }
            }

            try { _player.SetPause(true); } catch { }   // 通し終端でのみ停止
            try { BeginInvoke(new Action(() => { SetStatus("通し再生 完了。"); HighlightPlayingRow(-1); })); } catch { }
        }
        catch { /* 終了競合等は無視 */ }
    }

    /// <summary>（バックグラウンド）指定メディア時刻へシークして再生する。
    /// PCR 索引が使えればバイト位置（Position）で、無ければ時刻（Time）でフォールバックする。
    /// 末尾到達後（Ended）などシーク不能な状態なら、メディアを貼り直して再生可能状態へ自己回復する。</summary>
    private void SeekToMediaMs(long ms)
    {
        try
        {
            var st = _player.State;
            if (st is VLCState.Ended or VLCState.Stopped or VLCState.Error or VLCState.NothingSpecial)
            {
                // 末尾到達などで再生終了している → 貼り直して再生開始を待ってからシークする。
                if (_media is null) return;
                try { _player.Stop(); } catch { }
                _player.Play(_media);
                for (int i = 0; i < 40 && !_player.IsPlaying; i++) Thread.Sleep(25); // 最大 ~1 秒
            }
            else if (!_player.IsPlaying)
            {
                _player.Play();
            }

            double frac = _timebase?.PositionForMediaMs(ms) ?? -1;
            if (frac >= 0) _player.Position = (float)frac;
            else _player.Time = ms;
        }
        catch { }
    }

    /// <summary>再生中パートの行を選択して可視化する（-1 で選択解除のみ）。</summary>
    private void HighlightPlayingRow(int episodeSeq)
    {
        if (episodeSeq < 0) return;
        for (int i = 0; i < _grid.Rows.Count; i++)
        {
            if (_grid.Rows[i].DataBoundItem is PartRow r && r.EpisodeSeq == episodeSeq)
            {
                _grid.ClearSelection();
                _grid.Rows[i].Selected = true;
                try { _grid.FirstDisplayedScrollingRowIndex = Math.Max(0, i - 2); } catch { }
                return;
            }
        }
    }

    private void Timer_Tick(object? sender, EventArgs e) => UpdateTimeLabel();

    // ── 送り戻し・アンカー ──

    private void Nudge(long deltaMs)
    {
        StopSweep();
        long t = Math.Max(0, Interlocked.Read(ref _lastTimeMs) + deltaMs);
        Interlocked.Exchange(ref _lastTimeMs, t);
        RunPlayer(() => SeekToMediaMs(t));
    }

    private void ReanchorToCurrent()
    {
        if (_timebase is null) { SetStatus("先に TS を開いてください。"); return; }
        _programStartMediaMs = Math.Max(0, Interlocked.Read(ref _lastTimeMs));
        _anchorIsManual = true;
        UpdateAnchorLabel();
        SetStatus("現在位置を番組先頭（オフセット0）に再アンカーしました。");
    }

    private void ResetAnchorToTot(bool silent = false)
    {
        if (_timebase is { HasMapping: true } tb && _episode is Episode ep)
        {
            // 番組先頭(on_air_at)のメディア時刻。録画が放送開始より遅れて始まった場合は
            // 番組先頭がファイル先頭より前になり負値になる（0 にクランプしない）。これにより
            // 各境界 = 番組先頭(負) + オフセット が録画遅延ぶんを差し引いた正しいファイル位置になる。
            _programStartMediaMs = tb.MediaMsForWallClock(ep.OnAirAt);
            _anchorIsManual = false;
        }
        else
        {
            _programStartMediaMs = 0;
            _anchorIsManual = false;
        }
        UpdateAnchorLabel();
        if (!silent) SetStatus("番組先頭を TOT 基準（on_air_at）に戻しました。");
    }

    // ── 承認（マーカー除去）──

    private async Task ApproveSelectedAsync()
    {
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(r => r.DataBoundItem as PartRow)
            .Where(r => r is { IsUnconfirmed: true, Approved: false })
            .Cast<PartRow>()
            .ToList();
        if (rows.Count == 0) { SetStatus("未承認の【本放送未確認】パートを選択してください。"); return; }
        await ApproveRowsAsync(rows);
    }

    private async Task ApproveAllAsync()
    {
        var rows = _partRows.Where(r => r.IsUnconfirmed && !r.Approved).ToList();
        if (rows.Count == 0) { SetStatus("承認対象がありません。"); return; }
        if (MessageBox.Show(this, $"{rows.Count} 件のパートを承認し、【本放送未確認】を除去します。よろしいですか？",
                "全パート承認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        await ApproveRowsAsync(rows);
    }

    private async Task ApproveRowsAsync(List<PartRow> rows)
    {
        try
        {
            foreach (var row in rows)
            {
                var src = row.Source;
                string? newNotes = src.Notes?.Replace(Marker, "");
                newNotes = string.IsNullOrWhiteSpace(newNotes) ? null : newNotes.Trim();

                var updated = new EpisodePart
                {
                    EpisodeId = src.EpisodeId,
                    EpisodeSeq = src.EpisodeSeq,
                    PartType = src.PartType,
                    OaLength = src.OaLength,
                    DiscLength = src.DiscLength,
                    VodLength = src.VodLength,
                    Notes = newNotes,
                    UpdatedBy = AuditUser
                };
                await _partsRepo.UpdateAsync(updated);

                src.Notes = newNotes;
                row.Approved = true;
            }
            _grid.Invalidate(); // 強調（薄い赤）を再評価
            UpdateRemainLabel();
            SetStatus($"{rows.Count} 件を承認しました（マーカー除去）。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "承認（DB 更新）に失敗しました。\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── 表示更新 ──

    private void UpdateTimeLabel()
    {
        long t = Interlocked.Read(ref _lastTimeMs);
        long len = Interlocked.Read(ref _lastLenMs);
        string wc = _timebase is { HasMapping: true } tb
            ? tb.WallClockAtMediaZero.AddMilliseconds(t < 0 ? 0 : t).ToString("HH:mm:ss.fff")
            : "--:--:--";
        _timeLabel.Text = $"{FmtMs(t)} / {FmtMs(len)}　壁時計(JST): {wc}";
    }

    private void UpdateAnchorLabel()
    {
        string mode = _anchorIsManual ? "手動" : (_timebase is { HasMapping: true } ? "TOT自動" : "未確立");
        // 番組先頭がファイル先頭より前（負）＝録画が放送開始より遅れて始まったケース。遅延量を明示する。
        long ps = _programStartMediaMs;
        string head = ps >= 0
            ? $"番組先頭={FmtMs(ps)}"
            : $"番組先頭=ファイル先頭-{FmtMs(-ps)}（録画開始が遅延）";
        _anchorLabel.Text = $"アンカー: {mode}　{head}";
    }

    private void UpdateRemainLabel()
    {
        int totalMarkers = _partRows.Count(r => r.IsUnconfirmed);
        int remain = _partRows.Count(r => r.IsUnconfirmed && !r.Approved);
        _remainLabel.Text = totalMarkers == 0
            ? $"本放送未確認なし（全 {_partRows.Count} パート）"
            : $"未確認 {remain} / {totalMarkers}（全 {_partRows.Count} パート）";
    }

    private void SetStatus(string msg) => _statusLabel.Text = msg;

    private static string FmtMs(long ms)
    {
        if (ms < 0) ms = 0;
        long totalSec = ms / 1000;
        return $"{totalSec / 60:00}:{totalSec % 60:00}.{ms % 1000:000}";
    }
}
