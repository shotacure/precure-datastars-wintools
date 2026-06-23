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
    private static readonly Color SilenceWarnBack = Color.FromArgb(255, 236, 179); // 琥珀（無音警告）
    private static readonly Color MidpointFore = Color.FromArgb(120, 40, 160);     // 紫（中間再生パートの種別文字）
    private static readonly Color WindowInvalidBack = Color.FromArgb(255, 190, 190); // 赤（確認幅が同値・反転で再生不可）

    /// <summary>無音チェック対象のパート種別。予告（次回予告）に限定する。
    /// 30 秒の予告が中央の無音で 15+15 に割れていないかを、予告の中間部だけ切り出して高速に確かめる。</summary>
    private const string SilenceTargetType = "TRAILER";

    /// <summary>予告の中間部解析窓の半幅（ミリ秒）。中点 ± この幅だけをデコードして無音を探す。</summary>
    private const long TrailerWindowHalfMs = 5000;

    /// <summary>解析窓を予告の前後境界から内側に保つマージン（ミリ秒）。境界の渡り無音を窓に入れないため。</summary>
    private const long TrailerBoundaryMs = 2000;

    /// <summary>解析窓の両端からさらに除く端マージン（ミリ秒）。start-time シーク直後の過渡を無視するため。</summary>
    private const long TrailerEdgeMarginMs = 300;

    /// <summary>警告対象とする連続無音の最小長（ミリ秒）。</summary>
    private const long SilenceMinMs = 500;

    /// <summary>映画連動期の OP/ED 判定で notes に探すマーカー。</summary>
    private const string MovieMarker = "映画";

    /// <summary>確認窓の始点（境界中心からのオフセット、ミリ秒）の選択肢。負＝境界より前。UI の始点コンボと対応（0.5 秒刻み）。</summary>
    private static readonly long[] WindowStartOptionsMs = { -3000, -2500, -2000, -1500, -1000, -500, 0, 500, 1000, 1500, 2000 };

    /// <summary>確認窓の終点（境界中心からのオフセット、ミリ秒）の選択肢。正＝境界より後。UI の終点コンボと対応（0.5 秒刻み）。</summary>
    private static readonly long[] WindowEndOptionsMs = { -2000, -1500, -1000, -500, 0, 500, 1000, 1500, 2000, 2500, 3000 };

    /// <summary>TOT 自動アンカーに足す既定補正（ミリ秒）。枠時刻(on_air_at=08:30:00)と実本編開始の
    /// 数秒差を埋めるための暫定固定値。プラスで番組先頭がファイル内の後方へ動く（境界が後ろにずれる）。
    /// 当面 +3 秒固定。実測で別値が妥当なら見直す。</summary>
    private const long DefaultAnchorBiasMs = 3000;

    /// <summary>確認窓の始点（境界中心からのオフセット、ミリ秒）。負で境界より前から再生を始める。既定 −2 秒。UI の始点コンボで変更可。</summary>
    private long _windowStartMs = -2000;

    /// <summary>確認窓の終点（境界中心からのオフセット、ミリ秒）。正で境界より後まで再生する。既定 +2 秒。UI の終点コンボで変更可。
    /// 始点 ＜ 終点 を満たさない（同値・反転）と不正で、再生は拒否しコンボを赤表示する。</summary>
    private long _windowEndMs = 2000;

    // ── リポジトリ ──
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly EpisodePartsRepository _partsRepo;

    // ── 再生 ──
    private readonly LibVLC _libvlc;
    private readonly MediaPlayer _player;
    private readonly VideoView _videoView;
    private Panel _videoContainer = null!; // 黒背景。中で _videoView を 16:9 に保って中央配置する
    private Media? _media;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _tracksSelected;
    private bool _pendingPauseAfterStart;

    // ── 再生状態（スレッド間共有。Interlocked / lock で調停）──
    private long _lastTimeMs;            // TimeChanged でキャッシュした現在時刻
    private long _lastLenMs;             // LengthChanged でキャッシュした総尺
    private CancellationTokenSource? _sweepCts; // 進行中の通し再生（キャンセルで停止/差し替え）
    private CancellationTokenSource? _indexCts; // 進行中の背景バイト索引ビルド（新ファイル/終了でキャンセル）
    private CancellationTokenSource? _silenceCts; // 進行中の予告無音プローブ（新ファイル/エピソード切替/終了でキャンセル）
    private const long SeekSettleMs = 400;      // シーク着地ぶんの上乗せ滞在時間

    /// <summary>頭出し対象の 1 境界（センター時刻＋どのパートのどちら側か）。</summary>
    private sealed record CuePoint(long CenterMs, byte EpisodeSeq, string Label);

    // ── 状態 ──
    private TsTimebase? _timebase;
    private string _currentPath = "";            // 現在開いている TS のパス（予告無音プローブ用）
    private string _silenceDiag = "";            // 予告無音プローブの診断文（診断ラベルに合成）
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
    private readonly ComboBox _windowStartCombo = new();
    private readonly ComboBox _windowEndCombo = new();
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
        _videoView = new VideoView { MediaPlayer = _player, BackColor = Color.Black };

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
        // エピソード選択（自動同定の上書き）＋リロード。各要素が重ならないよう TableLayout で縦 3 段に固定。
        var epPanel = new TableLayoutPanel { Dock = DockStyle.Top, Height = 86, ColumnCount = 1, RowCount = 3, Padding = new Padding(6, 4, 6, 4) };
        epPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        epPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        epPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var epCaption = new Label { Text = "エピソード（手動上書き可）:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _episodeCombo.Dock = DockStyle.Fill;
        _episodeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _episodeCombo.SelectedIndexChanged += async (_, _) => await EpisodeCombo_ChangedAsync();
        _reloadButton.Text = "パートデータをリロード";
        _reloadButton.Dock = DockStyle.Fill;
        _reloadButton.Click += async (_, _) => await ReloadPartsAsync();
        epPanel.Controls.Add(epCaption, 0, 0);
        epPanel.Controls.Add(_episodeCombo, 0, 1);
        epPanel.Controls.Add(_reloadButton, 0, 2);

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
        var transport = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 160, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

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
        // 確認窓は境界中心からの始点/終点オフセットで指定する（負＝前 / 正＝後、0.5 秒刻み）。境界中心の
        // 「始点」から「終点」までを再生する。始点 ＜ 終点 を満たさない設定（同値・反転）は不正で、両コンボを
        // 赤表示にして再生時に拒否する（UpdateWindowValidity / IsWindowValid）。
        ConfigureWindowCombo(_windowStartCombo, WindowStartOptionsMs);
        ConfigureWindowCombo(_windowEndCombo, WindowEndOptionsMs);
        _windowStartCombo.SelectedIndexChanged += (_, _) =>
        {
            int i = _windowStartCombo.SelectedIndex;
            if (i >= 0 && i < WindowStartOptionsMs.Length) Interlocked.Exchange(ref _windowStartMs, WindowStartOptionsMs[i]);
            UpdateWindowValidity();
        };
        _windowEndCombo.SelectedIndexChanged += (_, _) =>
        {
            int i = _windowEndCombo.SelectedIndex;
            if (i >= 0 && i < WindowEndOptionsMs.Length) Interlocked.Exchange(ref _windowEndMs, WindowEndOptionsMs[i]);
            UpdateWindowValidity();
        };
        _windowStartCombo.SelectedIndex = Array.IndexOf(WindowStartOptionsMs, -2000L); // 既定 −2.0s
        _windowEndCombo.SelectedIndex = Array.IndexOf(WindowEndOptionsMs, 2000L);       // 既定 +2.0s
        UpdateWindowValidity();
        row3.Controls.Add(_reanchorButton);
        row3.Controls.Add(_totAnchorButton);
        row3.Controls.Add(new Label { Text = "確認幅 始点:", AutoSize = true, Padding = new Padding(8, 8, 2, 0) });
        row3.Controls.Add(_windowStartCombo);
        row3.Controls.Add(new Label { Text = "終点:", AutoSize = true, Padding = new Padding(6, 8, 2, 0) });
        row3.Controls.Add(_windowEndCombo);

        // 4 行目：番組先頭の微調整（枠時刻 08:30:00 と実コンテンツ開始の数秒ズレを境界を見ながら詰める）＋アンカー状態表示。
        var row4 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        row4.Controls.Add(new Label { Text = "番組先頭 微調整:", AutoSize = true, Padding = new Padding(2, 8, 2, 0) });
        foreach (var (label, delta) in new (string, long)[] { ("−1s", -1000), ("−0.5s", -500), ("+0.5s", 500), ("+1s", 1000) })
        {
            var b = new Button { Text = label, Size = new Size(60, 28) };
            long d = delta;
            b.Click += (_, _) => AdjustAnchor(d);
            row4.Controls.Add(b);
        }
        _anchorLabel.AutoSize = false;
        _anchorLabel.Size = new Size(330, 28);
        _anchorLabel.TextAlign = ContentAlignment.MiddleLeft;
        _anchorLabel.Text = "アンカー: 未設定";
        row4.Controls.Add(_anchorLabel);

        transport.Controls.Add(row1, 0, 0);
        transport.Controls.Add(row2, 0, 1);
        transport.Controls.Add(row3, 0, 2);
        transport.Controls.Add(row4, 0, 3);

        // 映像は 16:9 固定。コンテナ（黒）いっぱいに収まる最大の 16:9 矩形を中央配置する。
        _videoContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _videoView.Bounds = _videoContainer.ClientRectangle;
        _videoContainer.Controls.Add(_videoView);
        _videoContainer.Resize += (_, _) => LayoutVideo16x9();

        host.Controls.Add(_videoContainer);
        host.Controls.Add(transport);
    }

    /// <summary>確認窓の始点/終点コンボを共通設定する。オフセット(ms)を符号付きラベルで列挙し、
    /// 不正設定時に赤背景を反映できるよう Flat スタイルにする（テーマ描画の DropDownList は BackColor を反映しないため）。</summary>
    private static void ConfigureWindowCombo(ComboBox combo, long[] optionsMs)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.FlatStyle = FlatStyle.Flat;
        combo.Width = 78;
        foreach (long ms in optionsMs) combo.Items.Add(FmtOffsetLabel(ms));
    }

    /// <summary>境界中心からのオフセット(ms)を符号付きラベル（"+2.0s" / "−1.5s" / "0.0s"）に整形する。</summary>
    private static string FmtOffsetLabel(long ms)
    {
        string sign = ms > 0 ? "+" : ms < 0 ? "−" : "";
        return $"{sign}{Math.Abs(ms) / 1000.0:0.0}s";
    }

    /// <summary>確認窓の始点・終点の整合（始点 ＜ 終点）を判定し、不正なら両コンボを赤背景にする。</summary>
    private void UpdateWindowValidity()
    {
        var back = IsWindowValid() ? SystemColors.Window : WindowInvalidBack;
        _windowStartCombo.BackColor = back;
        _windowEndCombo.BackColor = back;
    }

    /// <summary>確認窓が再生可能か（始点 ＜ 終点 を満たすか）。同値・反転は不正で再生を拒否する。</summary>
    private bool IsWindowValid() => Interlocked.Read(ref _windowStartMs) < Interlocked.Read(ref _windowEndMs);

    private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is not PartRow r) return;
        if (r.Highlight) e.CellStyle.BackColor = UnconfirmedBack;

        bool isPartTypeCol = _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(PartRow.PartType);
        // 無音警告は「種別」セルだけ琥珀色にして、未確認の薄い赤と独立に視認できるようにする。
        if (r.SilenceWarn && isPartTypeCol)
            e.CellStyle.BackColor = SilenceWarnBack;
        // 中間再生（映画連動 OP/ED）は「種別」に〔中間〕マーカー＋紫文字で明示する（背景色とは独立）。
        if (r.PlayMidpoint && isPartTypeCol)
        {
            e.Value = "〔中間〕" + (e.Value?.ToString() ?? r.PartType);
            e.CellStyle.ForeColor = MidpointFore;
            e.FormattingApplied = true;
        }
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
        LayoutVideo16x9();
    }

    /// <summary>映像コンテナ内に収まる最大の 16:9 矩形を計算し、VideoView を中央配置する。</summary>
    private void LayoutVideo16x9()
    {
        int cw = _videoContainer.ClientSize.Width, ch = _videoContainer.ClientSize.Height;
        if (cw <= 0 || ch <= 0) return;
        int w = cw, h = w * 9 / 16;
        if (h > ch) { h = ch; w = h * 16 / 9; }
        _videoView.SetBounds((cw - w) / 2, (ch - h) / 2, w, h);
    }

    private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
    {
        _timer.Stop();
        StopSweep();
        try { _indexCts?.Cancel(); _indexCts?.Dispose(); } catch { }
        try { _silenceCts?.Cancel(); _silenceCts?.Dispose(); } catch { }
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
            _currentPath = path;
            _fileLabel.Text = path;
            _fileLabel.ForeColor = Color.Black;
            SetStatus("TS を解析中（TOT / PCR）…");

            // 即時パス：先頭 64MB ＋末尾 32MB だけ読んで 放送日・フルセグ program・時刻写像を確定する
            //（重い処理なのでバックグラウンドで）。精密シーク用の全域バイト索引はこの後に背景構築する。
            _timebase = await Task.Run(() => TsTimebase.AnalyzeQuick(path));
            _silenceDiag = "";
            RefreshDiagLabel();

            // 再生開始（制御呼び出しはバックグラウンド。トラック選択は Playing イベントで行う）。
            StartMedia(path);

            // 放送日からエピソードを自動同定。
            if (_timebase.BroadcastDate is DateOnly date)
                AutoIdentifyEpisode(date);
            else
                SetStatus("TOT から放送日を取得できませんでした。エピソードを手動選択してください。");

            // 全域バイト索引（精密シーク用）を背景で構築する。完成するまでの間、未索引域への
            // 通し再生シークは時刻シークへ自動フォールバックする（SeekToMediaMs 参照）。
            StartIndexBuild(_timebase, path);

            // 予告の無音チェックはエピソード同定後（AutoIdentifyEpisode → LoadEpisodeAsync）に
            // 予告パートの中間部だけを切り出して背景プローブする（ProbeTrailerSilence）。
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
        // フルセグ（MPEG-2 映像を含む番組）を明示選択し、ワンセグ(H.264 低レート)を掴むのを防ぐ。
        if (_timebase?.FullsegProgramNumber is int prog)
            media.AddOption($":program={prog}");

        var old = _media;
        _media = media;
        StopSweep();
        Interlocked.Exchange(ref _lastTimeMs, 0);
        _tracksSelected = false;
        _pendingPauseAfterStart = true;
        _anchorIsManual = false; // 新しい TS は TOT 自動アンカーから開始（前ファイルの手動調整を持ち越さない）

        // 停止→再生は libVLC 制御なのでバックグラウンドで。
        RunPlayer(() =>
        {
            try { _player.Stop(); } catch { }
            old?.Dispose();
            _player.Play(media);
        });
        _timer.Start();
    }

    /// <summary>精密シーク用の全域バイト索引を背景スレッドで構築する。進行中の旧ビルドはキャンセルする。
    /// 完成までは未索引域のシークが時刻シークに落ちるだけで、再生・頭出しは支障なく行える。
    /// 完了したら（同じファイルのままなら）診断行に最終索引点数を反映する。</summary>
    private void StartIndexBuild(TsTimebase tb, string path)
    {
        _indexCts?.Cancel();
        _indexCts?.Dispose();
        var cts = new CancellationTokenSource();
        _indexCts = cts;
        var token = cts.Token;
        _ = Task.Run(() =>
        {
            try
            {
                tb.BuildFullIndex(token);
                if (token.IsCancellationRequested) return;
                // 診断行（索引点数）の更新だけ UI スレッドへ。現在のタイムベースが差し替わっていなければ反映。
                try { BeginInvoke(new Action(() => { if (ReferenceEquals(_timebase, tb)) RefreshDiagLabel(); })); } catch { }
            }
            catch { /* キャンセル・終了競合等は無視 */ }
        });
    }

    /// <summary>現在エピソードの予告（TRAILER）パートについて、その中間部だけを切り出して音声デコードし、
    /// 連続無音（既定 0.5 秒以上）があれば警告する（30 秒予告が中央の無音で 15+15 に割れていないかの確認）。
    /// 全尺デコードは重いので、予告の中点 ± 数秒の窓だけを <c>start-time</c>/<c>stop-time</c> で切り出す。
    /// 進行中の旧プローブはキャンセルする。窓は予告の長さ・アンカーに対し十分広く取るので、微調整ごとの
    /// 再デコードはしない（エピソード読込・明示的な再アンカー時のみ走る）。</summary>
    private void ProbeTrailerSilence()
    {
        _silenceCts?.Cancel();
        _silenceCts?.Dispose();
        _silenceCts = null;

        // 既存の予告警告をクリア。
        foreach (var r in _partRows) if (r.PartType == SilenceTargetType) r.SilenceWarn = false;
        _silenceDiag = "";

        var trailer = _partRows.FirstOrDefault(r => r.PartType == SilenceTargetType);
        if (trailer is null || _timebase is not { HasMapping: true } || _episode is null || string.IsNullOrEmpty(_currentPath))
        {
            _grid.Invalidate();
            RefreshDiagLabel();
            return;
        }

        long start = StartMsOf(trailer), end = EndMsOf(trailer);
        long mid = (start + end) / 2;
        long half = Math.Min(TrailerWindowHalfMs, (end - start) / 2 - TrailerBoundaryMs);
        if (half < SilenceMinMs) // 予告が短すぎて中間窓が取れない
        {
            _grid.Invalidate();
            RefreshDiagLabel();
            return;
        }
        long fromMs = Math.Max(0, mid - half), toMs = mid + half;

        _silenceDiag = "予告無音: 解析中…";
        RefreshDiagLabel();

        var cts = new CancellationTokenSource();
        _silenceCts = cts;
        var token = cts.Token;
        string path = _currentPath;
        int? program = _timebase.FullsegProgramNumber;
        byte trailerSeq = trailer.EpisodeSeq;
        long windowMs = toMs - fromMs;
        var probe = new AudioSilenceProbe();
        _ = Task.Run(() =>
        {
            AudioSilenceProbe.Result res;
            try { res = probe.Probe(path, program, fromMs, toMs, TrailerEdgeMarginMs, SilenceMinMs, token); }
            catch { return; }
            if (token.IsCancellationRequested) return;
            try { BeginInvoke(new Action(() => ApplyTrailerProbe(cts, trailerSeq, res, windowMs))); } catch { }
        });
    }

    /// <summary>予告無音プローブの結果を UI に反映する（プローブが差し替わっていなければ）。</summary>
    private void ApplyTrailerProbe(CancellationTokenSource owner, byte trailerSeq, AudioSilenceProbe.Result res, long windowMs)
    {
        if (!ReferenceEquals(_silenceCts, owner)) return; // 新しいプローブ/ファイルに差し替わっていたら破棄

        var row = _partRows.FirstOrDefault(r => r.EpisodeSeq == trailerSeq && r.PartType == SilenceTargetType);
        if (row is not null) row.SilenceWarn = res.SilenceFound;

        double ratio = res.Buckets > 0 ? 100.0 * res.SilentBuckets / res.Buckets : 0;
        _silenceDiag = res.Decoded
            ? $"予告無音: 中間±{windowMs / 2000.0:0.#}s / 無音率 {ratio:0.0}% / {(res.SilenceFound ? "検出あり" : "なし")}"
            : "予告無音: 解析不可";
        _grid.Invalidate();
        RefreshDiagLabel();
        if (res.SilenceFound)
            SetStatus($"⚠ 予告の中間に {SilenceMinMs / 1000.0:0.#}s 以上の無音を検出（15+15 分割の可能性）。");
    }

    /// <summary>診断ラベルを「タイムベース診断｜予告無音（窓・無音率・判定）」で組み直す。
    /// タイムベース索引の完成と予告無音プローブの完了が別タイミングで来るため、両者をここで合成して
    /// 互いに上書きしないようにする。</summary>
    private void RefreshDiagLabel()
    {
        string s = _timebase?.Diagnostics ?? "";
        if (!string.IsNullOrEmpty(_silenceDiag)) s += "　｜　" + _silenceDiag;
        _diagLabel.Text = s;
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

            // 映画連動期の OP/ED 判定（同シリーズ・当該＋前後 1 話の OP/ED notes に「映画」を含むか）。
            var (movieOp, movieEd) = await DetectMovieOpEdAsync(ep, parts);
            BuildPartRows(parts, movieOp, movieEd);

            // TOT 自動アンカー（手動再アンカー済みなら据え置く）。
            if (!_anchorIsManual) ResetAnchorToTot(silent: true);
            UpdateAnchorLabel();
            UpdateRemainLabel();
            ProbeTrailerSilence(); // 予告の中間部を切り出して無音チェック（背景）
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "エピソード読み込みに失敗しました。\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BuildPartRows(IReadOnlyList<EpisodePart> parts, bool movieOp, bool movieEd)
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
            // 映画連動期の OP/ED は通し再生で中間地点も再生する（境界だけでは中盤の差し替えを見落とすため）。
            bool midpoint = (p.PartType == "OPENING" && movieOp) || (p.PartType == "ENDING" && movieEd);
            _partRows.Add(new PartRow
            {
                EpisodeSeq = p.EpisodeSeq,
                PartType = p.PartType,
                StartOffsetSec = start,
                EndOffsetSec = end,
                OaLengthSec = len,
                IsUnconfirmed = unconfirmed,
                Approved = false,
                Source = p,
                PlayMidpoint = midpoint
            });
            cum = end;
        }

        _partRows.RaiseListChangedEvents = true;
        _partRows.ResetBindings();
    }

    /// <summary>同シリーズで当該話＋前後 1 話（SeriesEpNo±1）のいずれかの OP/ED パートの notes に「映画」が
    /// 含まれるかを判定し、(OP対象, ED対象) を返す。映画連動期の OP/ED 中間地点再生の対象決定に使う。</summary>
    private async Task<(bool movieOp, bool movieEd)> DetectMovieOpEdAsync(Episode ep, IReadOnlyList<EpisodePart> currentParts)
    {
        bool op = PartsContainMovie(currentParts, "OPENING");
        bool ed = PartsContainMovie(currentParts, "ENDING");
        if (op && ed) return (op, ed);

        var neighbors = _allEpisodes.Where(e =>
            e.SeriesId == ep.SeriesId &&
            e.EpisodeId != ep.EpisodeId &&
            Math.Abs(e.SeriesEpNo - ep.SeriesEpNo) == 1).ToList();

        foreach (var e in neighbors)
        {
            if (op && ed) break;
            IReadOnlyList<EpisodePart> parts;
            try { parts = await _partsRepo.GetByEpisodeAsync(e.EpisodeId); }
            catch { continue; }
            op |= PartsContainMovie(parts, "OPENING");
            ed |= PartsContainMovie(parts, "ENDING");
        }
        return (op, ed);
    }

    private static bool PartsContainMovie(IReadOnlyList<EpisodePart> parts, string partType)
        => parts.Any(p => p.PartType == partType && (p.Notes ?? "").Contains(MovieMarker, StringComparison.Ordinal));

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

    // ── 通し再生（境界の前後確認幅。始点/終点オフセットを独立指定）──

    private long StartMsOf(PartRow r) => _programStartMediaMs + OffsetDeltaMs(r.StartOffsetSec);
    private long EndMsOf(PartRow r) => _programStartMediaMs + OffsetDeltaMs(r.EndOffsetSec);

    /// <summary>番組先頭からの相対秒（offsetSec）を、メディア時刻の差分(ms)に変換する。
    /// クロックレートが 1 でない（写像の傾きが 1 でない）場合も正しくなるよう、壁時計経由で換算する。</summary>
    private long OffsetDeltaMs(int offsetSec)
    {
        if (_timebase is { HasMapping: true } tb && _episode is Episode ep)
            return tb.MediaMsForWallClock(ep.OnAirAt.AddSeconds(offsetSec)) - tb.MediaMsForWallClock(ep.OnAirAt);
        return (long)offsetSec * 1000;
    }

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
        if (!IsWindowValid())
        {
            SetStatus("⚠ 確認幅が不正です（始点 ＜ 終点 になるよう設定してください）。再生を中止しました。");
            return;
        }

        var groups = new SortedDictionary<long, List<(byte seq, string text, bool isStart)>>();
        void Add(long center, byte seq, string text, bool isStart)
        {
            if (!groups.TryGetValue(center, out var list)) { list = new(); groups[center] = list; }
            list.Add((seq, text, isStart));
        }
        foreach (var r in rows.OrderBy(x => x.EpisodeSeq))
        {
            Add(StartMsOf(r), r.EpisodeSeq, $"{r.PartType} 開始", true);
            // 映画連動期の OP/ED は中間地点も確認列に加える（開始→中間→終了）。
            if (r.PlayMidpoint)
                Add((StartMsOf(r) + EndMsOf(r)) / 2, r.EpisodeSeq, $"{r.PartType} 中間", false);
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

    /// <summary>（バックグラウンド・直列）各境界へ「PCR 索引によるバイト位置シーク → 壁時計で窓長（終点−始点）滞在」を順に行う。
    /// 索引（<see cref="TsTimebase.PositionForMediaMs"/>）から実バイト位置を割り出して <c>Position</c> で一発シークするので、
    /// VLC の時刻シーク推定誤差に左右されず決定論的に着地する。再生したまま行い、終端でのみ 1 回ポーズする。</summary>
    private async Task RunSweepAsync(List<CuePoint> cues, CancellationToken ct)
    {
        try
        {
            foreach (var cue in cues)
            {
                if (ct.IsCancellationRequested) return;

                long startOff = Interlocked.Read(ref _windowStartMs);
                long endOff = Interlocked.Read(ref _windowEndMs);
                long len = Interlocked.Read(ref _lastLenMs);
                long target = Math.Max(0, cue.CenterMs + startOff);   // 窓の始端（境界中心＋始点オフセット）
                if (len > 0) target = Math.Min(target, Math.Max(0, len - 200));

                SeekToMediaMs(target);
                try { BeginInvoke(new Action(() => { SetStatus($"再生中: {cue.Label}"); HighlightPlayingRow(cue.EpisodeSeq); })); } catch { }

                // 壁時計で窓長（終点−始点。＋シーク着地ぶん）だけ滞在。
                try { await Task.Delay((int)(endOff - startOff + SeekSettleMs), ct).ConfigureAwait(false); }
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
        ProbeTrailerSilence(); // 番組先頭が大きく動いたので予告無音を再プローブ
        SetStatus("現在位置を番組先頭（オフセット0）に再アンカーしました。");
    }

    /// <summary>番組先頭を微調整する。枠時刻(08:30:00)と実コンテンツ開始の数秒ズレを、境界を見ながら詰めるのに使う。
    /// プラスで境界がファイル内で後ろへ（＝コンテンツが早く出るズレを補正）、マイナスで前へ動く。
    /// 調整後は手動アンカー扱いとし、エピソード切替・リロードでも保持する。</summary>
    private void AdjustAnchor(long deltaMs)
    {
        _programStartMediaMs += deltaMs;
        _anchorIsManual = true;
        UpdateAnchorLabel();
        // 微調整（±0.5/1s）は予告中間窓（±数秒）に十分収まるので予告無音の再プローブはしない。
        SetStatus($"番組先頭を {(deltaMs >= 0 ? "+" : "")}{deltaMs / 1000.0:0.0}s 微調整しました。");
    }

    private void ResetAnchorToTot(bool silent = false)
    {
        if (_timebase is { HasMapping: true } tb && _episode is Episode ep)
        {
            // 番組先頭(on_air_at)のメディア時刻。録画が放送開始より遅れて始まった場合は
            // 番組先頭がファイル先頭より前になり負値になる（0 にクランプしない）。これにより
            // 各境界 = 番組先頭(負) + オフセット が録画遅延ぶんを差し引いた正しいファイル位置になる。
            // さらに枠時刻と実本編開始の数秒差を埋める既定補正（暫定 +1 秒固定）を足す。
            _programStartMediaMs = tb.MediaMsForWallClock(ep.OnAirAt) + DefaultAnchorBiasMs;
            _anchorIsManual = false;
        }
        else
        {
            _programStartMediaMs = 0;
            _anchorIsManual = false;
        }
        UpdateAnchorLabel();
        // 自動アンカー（LoadEpisodeAsync 経由・silent）は読込側で予告無音をプローブするので二重に走らせない。
        // 「TOT 基準に戻す」ボタン（!silent）でアンカーが動いたときだけ再プローブする。
        if (!silent) { ProbeTrailerSilence(); SetStatus("番組先頭を TOT 基準（on_air_at）に戻しました。"); }
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
        string totAuto = DefaultAnchorBiasMs != 0 ? $"TOT自動(+{DefaultAnchorBiasMs / 1000.0:0.#}s)" : "TOT自動";
        string mode = _anchorIsManual ? "手動" : (_timebase is { HasMapping: true } ? totAuto : "未確立");
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
