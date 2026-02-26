using MeCab;
using Microsoft.VisualBasic;
using MySqlConnector;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Data.TitleCharStatsJson;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PrecureDataStars.Episodes.Forms;

/// <summary>
/// エピソード編集フォーム（メイン画面）。
/// <para>
/// 左ペインで TV シリーズとエピソードを選択し、右ペインでサブタイトル・放送日時・
/// ナンバリング・外部 URL・パート構成などを編集する。主な機能:
/// <list type="bullet">
///   <item>MeCab による自動かな変換・ルビ HTML 生成</item>
///   <item>パート構成の DnD 並べ替え・差分保存</item>
///   <item>東映サイトから URL / YouTube 動画 ID の自動提案</item>
///   <item>サブタイトル文字統計（初出・唯一・いつぶり）のリアルタイム表示</item>
///   <item>パート尺の偏差値・ランキング表示</item>
///   <item>「このあと…」「次回…」定型文のクリップボードコピー</item>
/// </list>
/// </para>
/// </summary>
public partial class EpisodesEditorForm : Form
{
    /// <summary>監査列 (created_by / updated_by) に記録するユーザー識別子。</summary>
    private const string AuditUser = "episodes_editor";
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly EpisodePartsRepository _partsRepo;
    private readonly PartTypesRepository _partTypesRepo;

    /// <summary>
    /// パートグリッド (<see cref="dgvParts"/>) にバインドする行ビューモデル。
    /// DB の <see cref="EpisodePart"/> と 1:1 対応するが、表示専用列 (OaTime) や
    /// 差分検出用フラグ (OriginalSeq / IsContentDirty) を追加で保持する。
    /// </summary>
    private sealed class PartRow
    {
        public int EpisodeSeq { get; set; }            // 並び（保存時に1..Nへ振り直す）
        public string PartType { get; set; } = "";     // 種別コード（ドロップダウン）
        public string? Label { get; set; }             // 任意の表示ラベル等（なければnull）
        public ushort? OaLength { get; set; }             // OA尺
        public ushort? DiscLength { get; set; }           // 円盤尺
        public ushort? VodLength { get; set; }            // 配信尺
        public string? OaTime { get; set; }             // OA時刻
        public string? Notes { get; set; }             // 備考
        public byte OriginalSeq { get; set; }  // 読み込み時のseq固定。新規は0にする
        public bool IsContentDirty { get; set; }  // 値を編集したらtrue

    }
    private BindingList<PartRow> _partRows = new();


    private List<Series> _tvSeries = new();
    private List<Episode> _episodes = new();
    private List<(string Code, string Name)> _partTypeOptions = new();
    private Series? _currentSeries;
    private Episode? _currentEpisode;
    private List<EpisodePart> _loadedEpisodeParts = new();

    private readonly string _imageRoot;

    // === 提案（薄黄）表示制御 ===
    private readonly Color _hintBack = Color.FromArgb(255, 255, 224); // LightYellow
    private Color _normalBackKana;
    private Color _normalBackHtml;
    private Color _normalBackToeiSummary;
    private Color _normalBackToeiLineup;
    private Color _normalBackYoutube;

    private bool _isLoading; // データ読み込み中は提案を抑止

    // TVシリーズ選択変更ハンドラ抑止フラグ（TV一覧バインド時の多重発火を止める）
    private bool _suppressSeriesSelectionChanged;

    // エピソード選択変更ハンドラ抑止フラグ（バインド中の多重発火を止める）
    private bool _suppressEpisodeSelectionChanged;

    // DnD reorder
    private int _dragRowIndex = -1;

    // DnD drag start 判定用
    private Point _dragStartPoint;
    private bool _dragCandidate;

    private static readonly HttpClient _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

    private readonly ToolTip _copyToolTip = new ToolTip();

    /// <summary>
    /// コンストラクタ。リポジトリの DI、パートグリッド初期化、イベント配線、初期ロードを行う。
    /// </summary>
    /// <param name="seriesRepo"></param>
    /// <param name="episodesRepo"></param>
    /// <param name="partsRepo"></param>
    /// <param name="partTypesRepo"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public EpisodesEditorForm(
    SeriesRepository seriesRepo,
    EpisodesRepository episodesRepo,
    EpisodePartsRepository partsRepo,
    PartTypesRepository partTypesRepo)
    {
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));
        _partsRepo = partsRepo ?? throw new ArgumentNullException(nameof(partsRepo));
        _partTypesRepo = partTypesRepo ?? throw new ArgumentNullException(nameof(partTypesRepo));

        // App.config からプレビュー画像のルートパスを取得
        _imageRoot = ConfigurationManager.AppSettings["EpisodeImageRoot"] ?? string.Empty;

        InitializeComponent();

        // Parts UI 初期化
        InitPartsGrid();

        // 通常色を覚えておく
        _normalBackKana = txtTitleKana.BackColor;
        _normalBackHtml = txtTitleRichHtml.BackColor;
        _normalBackToeiSummary = txtToeiSummary.BackColor;
        _normalBackToeiLineup = txtToeiLineup.BackColor;
        _normalBackYoutube = txtYoutube.BackColor;

        // イベント
        lstTvSeries.SelectedIndexChanged += async (_, __) => await BindSeriesAsync();
        lstEpisodes.SelectedIndexChanged += async (_, __) => await BindEpisode();
        lstEpisodes.Format += LstEpisodes_Format;

        btnSave.Click += async (_, __) => await SaveAsync();
        btnAdd.Click += async (_, __) => await AddAsync();

        btnPartCopyPrev.Click += async (_, __) => await CopyFromPreviousAsync();

        // OA尺変更 → OA時刻を再計算
        dgvParts.CellValueChanged += DgvParts_CellValueChanged;
        // 編集終了時（TextBoxセルは CellValueChanged が遅れるケースに備えて）
        dgvParts.CellEndEdit += (_, __) => { RecalcOaTimes(); RecalcTotals(); };
        // 放送開始日時を動かしたら全行のOA時刻を再計算
        dtOnAirAt.ValueChanged += (_, __) => { RecalcOaTimes(); };

        // HTML操作系
        btnRuby.Click += (_, __) => InsertRuby();
        btnBr.Click += (_, __) => InsertAtCaret(txtTitleRichHtml, "<br>");

        // 入力即時プレビュー
        txtTitleRichHtml.TextChanged += (_, __) => ShowHtmlPreview();

        // フォーム全体で Ctrl+S を捕捉
        this.KeyPreview = true;
        this.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                btnSave.PerformClick();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };

        // ルビ内選択テキストを次の <rt> に送る Ctrl+R
        txtTitleRichHtml.KeyDown += TxtTitleRichHtml_KeyDown;

        // ユーザーが提案テキストを手動編集したら、提案色（薄黄）を通常色に戻す
        txtTitleKana.TextChanged += (_, __) =>
        {
            if (txtTitleKana.BackColor == _hintBack && !string.IsNullOrWhiteSpace(txtTitleKana.Text))
                txtTitleKana.BackColor = _normalBackKana;
        };
        txtTitleRichHtml.TextChanged += (_, __) =>
        {
            if (txtTitleRichHtml.BackColor == _hintBack && !string.IsNullOrWhiteSpace(txtTitleRichHtml.Text))
                txtTitleRichHtml.BackColor = _normalBackHtml;
        };
        txtToeiSummary.TextChanged += (_, __) =>
        {
            if (txtToeiSummary.BackColor == _hintBack && !string.IsNullOrWhiteSpace(txtToeiSummary.Text))
                txtToeiSummary.BackColor = _normalBackToeiSummary;
        };
        txtToeiLineup.TextChanged += (_, __) =>
        {
            if (txtToeiLineup.BackColor == _hintBack && !string.IsNullOrWhiteSpace(txtToeiLineup.Text))
                txtToeiLineup.BackColor = _normalBackToeiLineup;
        };
        txtYoutube.TextChanged += (_, __) =>
        {
            if (txtYoutube.BackColor == _hintBack && !string.IsNullOrWhiteSpace(txtYoutube.Text))
                txtYoutube.BackColor = _normalBackYoutube;
        };

        // サブタイトル変更時の自動提案: 空欄の場合のみ MeCab かな → ルビ HTML を生成
        txtTitleText.TextChanged += (_, __) =>
        {
            if (_isLoading) return;
            if (!txtTitleText.Focused) return;

            if (string.IsNullOrWhiteSpace(txtTitleKana.Text))
                SuggestKanaFromTitle();

            if (string.IsNullOrWhiteSpace(txtTitleRichHtml.Text))
            {
                SuggestRubyPerKanjiFromTitle();
                ShowHtmlPreview();
            }
        };

        // かなが変わった → HTMLが空欄なら再提案
        txtTitleKana.TextChanged += (_, __) =>
        {
            if (string.IsNullOrWhiteSpace(txtTitleRichHtml.Text))
            {
                SuggestRubyPerKanjiFromTitle();
                ShowHtmlPreview();
            }
        };

        // URL テキストボックスのダブルクリックで既定ブラウザを開く
        txtToeiSummary.DoubleClick += (_, __) => OpenUrlFromTextBox(txtToeiSummary); // ← 追加
        txtToeiLineup.DoubleClick += (_, __) => OpenUrlFromTextBox(txtToeiLineup);  // ← 追加
        txtYoutube.DoubleClick += (_, __) => OpenUrlFromTextBox(txtYoutube);     // ← 追加

        // --- parts グリッドの基本設定（編集しやすさ向上＆初期選択の抑止）---
        dgvParts.EditMode = DataGridViewEditMode.EditOnEnter;
        dgvParts.SelectionMode = DataGridViewSelectionMode.CellSelect;
        dgvParts.MultiSelect = false;
        dgvParts.AllowUserToResizeRows = false;
        dgvParts.RowHeadersVisible = false;
        dgvParts.AutoGenerateColumns = false;
        dgvParts.DataError += (_, e) => e.ThrowException = false; // 型不一致などは落とさず無視
        dgvParts.EditingControlShowing += DgvParts_EditingControlShowing; // ComboBox を DropDownList に

        _ = LoadTvAsync();
    }

    /// <summary>
    /// エピソードリストの表示書式を "#{話数}  {サブタイトル}" にカスタマイズする。
    /// </summary>
    private void LstEpisodes_Format(object? sender, ListControlConvertEventArgs e)
    {
        if (e.ListItem is Episode ep)
            e.Value = $"#{ep.SeriesEpNo}  {ep.TitleText}";
    }

    /// <summary>
    /// TV シリーズ一覧を DB から取得し、ListBox にバインドする。
    /// 初回ロード時のみパート種別マスタも取得する。
    /// </summary>
    private async Task LoadTvAsync()
    {
        _suppressSeriesSelectionChanged = true;

        try
        {
            _tvSeries = (await _seriesRepo.GetTvSeriesAsync()).ToList();
            lstTvSeries.DataSource = _tvSeries;
            lstTvSeries.DisplayMember = nameof(Series.Title);
            lstTvSeries.ValueMember = nameof(Series.SeriesId);
            if (_tvSeries.Count > 0)
                lstTvSeries.SelectedIndex = 0;
        }
        finally
        {
            _suppressSeriesSelectionChanged = false;
        }

        await LoadPartTypesOnceAsync();
        // ここで**明示的に1回だけ**シリーズ→エピソード→フォーマットを読み込む
        await BindSeriesAsync();
    }

    /// <summary>
    /// シリーズ選択変更時: 選択シリーズのエピソード一覧を DB から取得し、ListBox にバインドする。
    /// 最新シリーズ以外では「新規追加」ボタンを無効化する。
    /// </summary>
    private async Task BindSeriesAsync()
    {
        if (_suppressSeriesSelectionChanged) return;

        _currentSeries = lstTvSeries.SelectedItem as Series;

        // レコード遷移時は色を戻す
        ResetHintColors();

        // 追加：最新TVシリーズ以外では「新規追加」を無効化
        if (_currentSeries is null)
        {
            btnAdd.Enabled = false; // ← 追加（null時は無効）
            _episodes.Clear();
            lstEpisodes.DataSource = null;
            await BindEpisode();
            return;
        }
        else
        {
            btnAdd.Enabled = IsLatestTvSeries(_currentSeries); // ← 追加
        }

        _episodes = (await _episodesRepo.GetBySeriesAsync(_currentSeries.SeriesId)).ToList();

        _suppressEpisodeSelectionChanged = true;
        try
        {
            lstEpisodes.DataSource = null;
            lstEpisodes.FormattingEnabled = true;
            lstEpisodes.DataSource = _episodes;
            lstEpisodes.DisplayMember = ""; // Format イベントで表示
            lstEpisodes.ValueMember = nameof(Episode.EpisodeId);

            if (_episodes.Count > 0)
                lstEpisodes.SelectedIndex = 0; // ← ここではイベント抑止したまま
        }
        finally
        {
            _suppressEpisodeSelectionChanged = false;
        }

        await BindEpisode();
    }

    /// <summary>
    /// エピソード選択変更時の処理。選択エピソードの全フィールドを右ペインに反映し、
    /// パートグリッドの読み込み、HTML プレビュー、文字統計の非同期取得などを行う。
    /// 空欄時には MeCab によるかな・ルビの自動提案、URL の自動提案も実行する。
    /// </summary>
    private async Task BindEpisode()
    {
        // DataSource 再バインド中に SelectedIndexChanged が連発するのを抑止
        if (_suppressEpisodeSelectionChanged) return;

        // レコード遷移時は色を戻す
        ResetHintColors();

        // ロード中
        _isLoading = true;

        try
        {
            _currentEpisode = lstEpisodes.SelectedItem as Episode;

            if (_currentSeries is null)
            {
                ClearEditor();
                return;
            }

            if (_currentEpisode is null)
            {
                txtTitleText.Text = "";
                txtTitleKana.Text = "";
                txtTitleRichHtml.Text = "";
                numSeriesEpNo.Value = 1;
                dtOnAirAt.Value = DateTime.Now;

                numTotalEpNo.Value = 0;
                numTotalOaNo.Value = 0;
                numNitiasaOaNo.Value = 0;

                txtToeiSummary.Text = "";
                txtToeiLineup.Text = "";
                txtYoutube.Text = "";

                txtNotes.Text = "";
                webHtmlPreview.DocumentText = "<html><body style='font-size:16px;font-family:Segoe UI;'>（プレビュー）</body></html>";

                LoadPreviewImage(null);
                return;
            }

            txtTitleText.Text = _currentEpisode.TitleText ?? "";
            txtTitleKana.Text = _currentEpisode.TitleKana ?? "";
            txtTitleRichHtml.Text = _currentEpisode.TitleRichHtml ?? "";
            numSeriesEpNo.Value = Math.Max(1, _currentEpisode.SeriesEpNo);
            dtOnAirAt.Value = _currentEpisode.OnAirAt == default ? DateTime.Now : _currentEpisode.OnAirAt;

            // 文字統計（初出・唯一・復活）と パート尺統計（偏差値・順位）を非同期で取得
            _ = SetTitleInformationAsync(_currentEpisode.EpisodeId, CancellationToken.None);
            _ = UpdatePartLengthStatsAsync(_currentEpisode.EpisodeId, CancellationToken.None);

            numTotalEpNo.Value = _currentEpisode.TotalEpNo ?? 0;
            numTotalOaNo.Value = _currentEpisode.TotalOaNo ?? 0;
            numNitiasaOaNo.Value = _currentEpisode.NitiasaOaNo ?? 0;

            txtToeiSummary.Text = _currentEpisode.ToeiAnimSummaryUrl ?? "";
            txtToeiLineup.Text = _currentEpisode.ToeiAnimLineupUrl ?? "";
            txtYoutube.Text = _currentEpisode.YoutubeTrailerUrl ?? "";

            txtNotes.Text = _currentEpisode.Notes ?? "";

            if (_currentSeries != null && _currentEpisode != null)
            {
                var junctionText = BuildJunctionCopyText(_currentSeries, _currentEpisode);
                var nextText = BuildNextTitleCopyText(_currentSeries, _currentEpisode);

                _copyToolTip.SetToolTip(btnJunctionCopy, junctionText);
                _copyToolTip.SetToolTip(btnNextTitleCopy, nextText);
            }
            else
            {
                _copyToolTip.SetToolTip(btnJunctionCopy, string.Empty);
                _copyToolTip.SetToolTip(btnNextTitleCopy, string.Empty);
            }

            await LoadPartsForEpisodeAsync();
        }
        finally
        {
            // ロード完了
            _isLoading = false;
        }

        // 空欄の場合のみ提案
        if (string.IsNullOrWhiteSpace(txtTitleKana.Text))
            SuggestKanaFromTitle();

        if (string.IsNullOrWhiteSpace(txtTitleRichHtml.Text))
            SuggestRubyPerKanjiFromTitle();

        ShowHtmlPreview();
        LoadPreviewImage(_currentEpisode);


        // 「前話コピー」ボタンの有効/無効
        btnPartCopyPrev.Enabled = (lstEpisodes.SelectedIndex > 0) && (_currentEpisode != null);

        // URL 提案（非同期・失敗はスルー）
        if (_currentEpisode is not null)
            _ = SuggestEpisodeUrlsAsync(_currentEpisode);
    }

    /// <summary>HTML 文字列に &lt; タグが含まれるかを簡易判定する（ルビタグ有無の目安）。</summary>
    private static bool ContainsRubyTag(string? html)
        => !string.IsNullOrEmpty(html) && html.IndexOf("<", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>bool? → CheckState 変換（null=Indeterminate, true=Checked, false=Unchecked）。</summary>
    private static CheckState BoolToState(bool? v)
        => v is null ? CheckState.Indeterminate : (v.Value ? CheckState.Checked : CheckState.Unchecked);

    /// <summary>CheckState → bool? 変換（Indeterminate=null）。</summary>
    private static bool? StateToBool(CheckState s)
        => s == CheckState.Indeterminate ? (bool?)null : (s == CheckState.Checked);

    /// <summary>右ペインの全コントロールを初期状態にクリアする。</summary>
    private void ClearEditor()
    {
        txtTitleText.Text = "";
        txtTitleKana.Text = "";
        txtTitleRichHtml.Text = "";
        numSeriesEpNo.Value = 1;
        dtOnAirAt.Value = DateTime.Now;

        numTotalEpNo.Value = 0;
        numTotalOaNo.Value = 0;
        numNitiasaOaNo.Value = 0;

        txtToeiSummary.Text = "";
        txtToeiLineup.Text = "";
        txtYoutube.Text = "";

        txtNotes.Text = "";
        webHtmlPreview.DocumentText = "<html><body style='font-size:16px;font-family:Segoe UI;'>（プレビュー）</body></html>";
        LoadPreviewImage(null);
    }

    /// <summary>
    /// 編集内容を DB に保存する。新規（EpisodeId=0）なら INSERT、既存なら UPDATE。
    /// title_char_stats JSON は TitleCharStatsBuilder で自動生成する。
    /// パート尺は差分操作 (ApplyOps) で保存する。
    /// </summary>
    private async Task SaveAsync()
    {
        if (_currentEpisode is null) return;

        _currentEpisode.TitleText = NormalizeWaveDash((txtTitleText.Text ?? string.Empty).Trim());
        _currentEpisode.TitleKana = string.IsNullOrWhiteSpace(txtTitleKana.Text) ? null : NormalizeWaveDash(txtTitleKana.Text.Trim());

        // 保存時に疑問符・感嘆符の正規化を実施
        _currentEpisode.TitleRichHtml = string.IsNullOrWhiteSpace(txtTitleRichHtml.Text)
             ? null
             : NormalizeWaveDash(txtTitleRichHtml.Text.Trim());

        _currentEpisode.SeriesEpNo = (int)numSeriesEpNo.Value;
        _currentEpisode.OnAirAt = dtOnAirAt.Value;

        _currentEpisode.TotalEpNo = numTotalEpNo.Value == 0 ? null : (int?)numTotalEpNo.Value;
        _currentEpisode.TotalOaNo = numTotalOaNo.Value == 0 ? null : (int?)numTotalOaNo.Value;
        _currentEpisode.NitiasaOaNo = numNitiasaOaNo.Value == 0 ? null : (int?)numNitiasaOaNo.Value;

        // total_oa_no があり、nitiasa が null なら補完
        if (_currentEpisode.TotalOaNo is int toa && _currentEpisode.NitiasaOaNo is null)
        {
            _currentEpisode.NitiasaOaNo = toa + 978;
        }

        _currentEpisode.ToeiAnimSummaryUrl = string.IsNullOrWhiteSpace(txtToeiSummary.Text) ? null : txtToeiSummary.Text.Trim();
        _currentEpisode.ToeiAnimLineupUrl = string.IsNullOrWhiteSpace(txtToeiLineup.Text) ? null : txtToeiLineup.Text.Trim();
        _currentEpisode.YoutubeTrailerUrl = string.IsNullOrWhiteSpace(txtYoutube.Text) ? null : txtYoutube.Text.Trim();

        _currentEpisode.Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text.Trim();

        // title_text → NFKC 正規化 → 書記素分類 → JSON 生成（空なら NULL）
        _currentEpisode.TitleCharStats = string.IsNullOrWhiteSpace(_currentEpisode.TitleText)
            ? null
            : TitleCharStatsBuilder.BuildJson(_currentEpisode.TitleText);

        _currentEpisode.UpdatedBy = AuditUser;

        // EpisodeId == 0 は未保存の新規行（AddAsync で仮生成されたもの）
        if (_currentEpisode.EpisodeId == 0)
        {
            // 新規 → INSERT → LAST_INSERT_ID() で ID を取得
            var newId = await _episodesRepo.InsertAsync(_currentEpisode);
            _currentEpisode.EpisodeId = newId;

            // リスト再バインド（IDが付いたので）
            var selected = _currentEpisode;
            lstEpisodes.DataSource = null;
            lstEpisodes.FormattingEnabled = true;
            lstEpisodes.DataSource = _episodes;
            lstEpisodes.DisplayMember = "";
            lstEpisodes.ValueMember = nameof(Episode.EpisodeId);
            lstEpisodes.SelectedItem = selected;
        }
        else
        {
            // 既存 → UPDATE
            try
            {
                await _episodesRepo.UpdateAsync(_currentEpisode);
            }
            // MySQL Error 1062 = Duplicate entry（UNIQUE キー重複）
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show("プリキュア通算話数 / プリキュア放送回数 / ニチアサ放送回数のいずれかが既に使われています。値を見直してください。",
                    "重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                await SavePartsAsync();
            }
            catch (MySqlException ex) when (ex.Number == 1062)
            {
                MessageBox.Show("パートのキーが重複しています。",
                    "重複エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // 保存後は提案色を通常に戻す
        ResetHintColors();

        lstEpisodes.Invalidate();
        lstEpisodes.Update();

        MessageBox.Show("保存しました", "Episode", MessageBoxButtons.OK, MessageBoxIcon.Information);

        await BindEpisode();
        ShowHtmlPreview();
    }

    /// <summary>
    /// 新規エピソードを仮データで生成し、画面リストに追加する。
    /// DB には INSERT しない（保存ボタン押下時に SaveAsync が INSERT する）。
    /// 直前エピソードから話数 +1 / 放送日 +7 日を自動補完する。
    /// </summary>
    private async Task AddAsync()
    {
        if (_currentSeries is null) return;

        // 直近エピソード（最後尾）を基準に +1 / +7日
        var last = _episodes.LastOrDefault();

        var e = new Episode
        {
            EpisodeId = 0, // ← 未保存マーク（保存時に INSERT）
            SeriesId = _currentSeries.SeriesId,
            SeriesEpNo = (last?.SeriesEpNo ?? 0) + 1,
            TotalEpNo = (last?.TotalEpNo ?? 0) + 1,
            TotalOaNo = (last?.TotalOaNo ?? 0) + 1,
            NitiasaOaNo = (last?.NitiasaOaNo ?? 0) + 1,
            OnAirAt = (last?.OnAirAt ?? DateTime.Now).AddDays(7),

            TitleText = "",       // 空で良い（要件）
            TitleKana = "",
            TitleRichHtml = "",

            CreatedBy = AuditUser,
            UpdatedBy = AuditUser
        };

        // 画面リストに追加（DBにはまだ入れない）
        _episodes.Add(e);
        lstEpisodes.DataSource = null;
        lstEpisodes.FormattingEnabled = true;
        lstEpisodes.DataSource = _episodes;
        lstEpisodes.DisplayMember = "";
        lstEpisodes.ValueMember = nameof(Episode.EpisodeId);
        lstEpisodes.SelectedItem = e;

        // 新規にもプレビューは試みる（画像があれば出る）
        LoadPreviewImage(e);
        ShowHtmlPreview();

        await Task.CompletedTask;
    }

    /// <summary>
    /// エピソードに対応するプレビュー画像を読み込み、PictureBox に表示する。
    /// App.config の EpisodeImageRoot/{slug数字部}/{話数}.jpg|png を検索する。
    /// </summary>
    /// <param name="ep">対象エピソード。NULL の場合は画像をクリアする。</param>
    private void LoadPreviewImage(Episode? ep)
    {
        try
        {
            picPreview.Image?.Dispose();
            picPreview.Image = null;

            if (string.IsNullOrWhiteSpace(_imageRoot) || _currentSeries is null || ep is null)
                return;

            // slug から数字部分を抽出してフォルダ名にする（例: "precure-23" → "23"）
            var slug = _currentSeries.Slug ?? string.Empty;
            var digits = new string(slug.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits)) return;

            var folder = Path.Combine(_imageRoot, digits);
            var n = ep.SeriesEpNo;

            var names = new[]
            {
                n.ToString(),
                n.ToString("00"),
                n.ToString("000")
            };

            string? path = null;
            foreach (var name in names)
            {
                var jpg = Path.Combine(folder, name + ".jpg");
                var png = Path.Combine(folder, name + ".png");
                if (File.Exists(jpg)) { path = jpg; break; }
                if (File.Exists(png)) { path = png; break; }
            }

            if (path is null) return;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var bmp = new Bitmap(fs);
            picPreview.Image = new Bitmap(bmp);
        }
        catch
        {
            // プレビューなしでスルー
        }
    }

    /// <summary>
    /// サブタイトルから MeCab を使ってかなを提案（失敗時は簡易ひらがな化にフォールバック）
    /// </summary>
    private void SuggestKanaFromTitle()
    {
        var title = (txtTitleText.Text ?? "").Trim();
        if (string.IsNullOrEmpty(title)) return;

        var kana = TryKanaWithMeCab(title) ?? KatakanaToHiragana(title);
        if (string.IsNullOrWhiteSpace(kana)) return;

        txtTitleKana.Text = kana;
        txtTitleKana.BackColor = _hintBack; // 提案色
    }

    /// <summary>
    /// MeCab 形態素を使いつつ、「漢字ブロックごとに ruby」を基本とし、
    /// うまく読みに割り当てられない場合は「各漢字1文字に空 rt だけ」を付与するフォールバック。
    /// 既に HTML に <ruby> が含まれている場合は実行しない。
    /// </summary>
    private void SuggestRubyPerKanjiFromTitle()
    {
        var title = (txtTitleText.Text ?? "").Trim();
        if (string.IsNullOrEmpty(title)) return;

        // MeCab 解析
        var nodes = TryParseWithMeCab(title);
        if (nodes is null || nodes.Count == 0) return;

        var html = BuildRubyFromMeCabPerKanji(nodes);

        if (!string.IsNullOrWhiteSpace(html))
        {
            txtTitleRichHtml.Text = html;
            txtTitleRichHtml.BackColor = _hintBack; // 提案色
        }
    }

    /// <summary>全提案フィールドの背景色を通常色に戻す。</summary>
    private void ResetHintColors()
    {
        txtTitleKana.BackColor = _normalBackKana;
        txtTitleRichHtml.BackColor = _normalBackHtml;
        txtToeiSummary.BackColor = _normalBackToeiSummary;
        txtToeiLineup.BackColor = _normalBackToeiLineup;
        txtYoutube.BackColor = _normalBackYoutube;
    }

    /// <summary>
    /// 選択テキストにルビ（&lt;ruby&gt;…&lt;rt&gt;…&lt;/rt&gt;&lt;/ruby&gt;）を付与する。
    /// InputBox でふりがなを入力させ、かな文字のみに絞ってから HTML を生成する。
    /// </summary>
    private void InsertRuby()
    {
        var sel = txtTitleRichHtml.SelectedText;
        if (string.IsNullOrEmpty(sel))
        {
            sel = txtTitleText.SelectedText;
            if (string.IsNullOrEmpty(sel))
            {
                MessageBox.Show("ルビを付ける文字列を選択してください。", "Ruby", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        var yomi = Interaction.InputBox("ふりがなを入力してください：", "ルビ", "");
        if (string.IsNullOrWhiteSpace(yomi)) return;

        // ルビはかな類のみを許容（巻き込み防止）
        yomi = KeepKanaOnly(yomi);

        var ruby = $"<ruby>{EscapeHtml(sel)}<rt>{EscapeHtml(yomi)}</rt></ruby>";
        InsertAtCaret(txtTitleRichHtml, ruby);
    }

    /// <summary>
    /// テキストボックスのキャレット位置（または選択範囲）にテキストを挿入する。
    /// </summary>
    /// <param name="target">対象テキストボックス。</param>
    /// <param name="text">挿入するテキスト。</param>
    private void InsertAtCaret(TextBox target, string text)
    {
        var start = target.SelectionStart;
        var length = target.SelectionLength;
        var old = target.Text;
        target.Text = old.Substring(0, start) + text + old.Substring(start + length);
        target.SelectionStart = start + text.Length;
        target.SelectionLength = 0;
        target.Focus();
    }

    /// <summary>
    /// title_rich_html の内容を WebBrowser コントロールでリアルタイムプレビューする。
    /// シリーズ固有のサブタイトルフォント (FontSubtitle) があれば CSS に反映する。
    /// </summary>
    private void ShowHtmlPreview()
    {
        var html = txtTitleRichHtml.Text?.Trim();
        if (string.IsNullOrEmpty(html))
        {
            webHtmlPreview.DocumentText = "<html><body style='font-size:16px;font-family:Segoe UI;'>（プレビュー）</body></html>";
        }
        else
        {
            string subtitle_font = string.Empty;

            if (_currentSeries is not null)
            {
                if (_currentSeries.FontSubtitle is not null)
                {
                    subtitle_font = "'" + _currentSeries.FontSubtitle + "', ";
                }
                
            }

            webHtmlPreview.DocumentText =
                "<html><head><meta http-equiv='X-UA-Compatible' content='IE=edge'/>" +
                "<style>body{font-family:" + subtitle_font + "'Segoe UI', Meiryo, sans-serif;font-size:32px;text-align: center;font-feature-settings: 'kern' 1, 'palt' 1;}" +
                "ruby{ruby-position:over;}</style></head><body>" +
                html +
                "</body></html>";
        }
    }


    /// <summary>MeCab 形態素解析結果の軽量モデル（表層形 + 読み）。</summary>
    private sealed class MeCabNodeLite
    {
        public string Surface { get; init; } = "";
        public string? Reading { get; init; }
    }

    /// <summary>
    /// MeCab.DotNet でテキストを形態素解析し、各ノードの表層形と読みを返す。
    /// Feature CSV の第 8 フィールド（読み）を使用する。失敗時は null。
    /// </summary>
    /// <param name="text">解析対象の日本語テキスト。</param>
    /// <returns>形態素ノードのリスト。失敗時は null。</returns>
    private List<MeCabNodeLite>? TryParseWithMeCab(string text)
    {
        // 1) MeCab.DotNet を優先
        try
        {
            // 既定辞書で作成（必要に応じて MeCabParam で ipadic 等を指定可能）
            var tagger = MeCabTagger.Create();
            var nodes = tagger.ParseToNodes(text);

            var list = new List<MeCabNodeLite>();
            foreach (var n in nodes)
            {
                if (n is null) continue;

                var surface = n?.Surface ?? "";
                if (string.IsNullOrEmpty(surface)) continue;

                // Feature CSV 例: "名詞,一般,*,*,*,*,花,ハナ,ハナ"（第8フィールドが読み）
                string? reading = null;
                var feat = n?.Feature ?? "";
                if (!string.IsNullOrEmpty(feat))
                {
                    var parts = feat.Split(',');
                    if (parts.Length >= 8)
                    {
                        // BOSとEOSはスルー
                        if (parts[0] == "BOS/EOS") continue;

                        // 読み
                        var rd = parts[7];
                        if (!string.IsNullOrWhiteSpace(rd)) reading = rd;
                    }
                    else
                    {
                        // 想定外の記号はそのまま返却
                        reading = surface;
                    }
                }

                list.Add(new MeCabNodeLite { Surface = surface, Reading = reading });
            }

            return list;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// MeCab でテキストを解析し、全ノードの読みを連結してひらがな文字列を返す。
    /// 失敗時は null（呼び出し元で KatakanaToHiragana フォールバック）。
    /// </summary>
    /// <param name="text">対象テキスト。</param>
    /// <returns>ひらがな読み文字列。MeCab 失敗時は null。</returns>
    private string? TryKanaWithMeCab(string text)
    {
        var nodes = TryParseWithMeCab(text);
        if (nodes is null || nodes.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var n in nodes)
        {
            if (!string.IsNullOrEmpty(n.Reading))
                sb.Append(KatakanaToHiragana(n.Reading));
            else
                sb.Append(n.Surface);
        }
        return KatakanaToHiragana(sb.ToString());
    }


    /// <summary>
    /// MeCabノードだけを見て ruby を作る。
    /// 各ノード内：最初に出てくる漢字に「ノード全体の読み（かな）」を丸ごと付与、
    /// その後に出る漢字は <rt></rt>（空）でタグだけ用意する。
    /// 読みが null/空 の場合も、漢字は <rt></rt>（空）でルビを作る。
    /// </summary>
    /// <param name="nodes"></param>
    /// <returns></returns>
    private string BuildRubyFromMeCabPerKanji(IReadOnlyList<MeCabNodeLite> nodes)
    {
        var sb = new StringBuilder();

        foreach (var n in nodes)
        {
            if (n is null) continue;
            var surface = n.Surface ?? string.Empty;
            if (surface.Length == 0)
            {
                continue;
            }

            // 読み（ノード全体）を抽出：カタカナ→ひらがな→かなのみ
            var readingRaw = n.Reading;

            var hira = string.IsNullOrWhiteSpace(readingRaw)
                ? string.Empty
                : KeepKanaOnly(KatakanaToHiragana(readingRaw));

            bool firstKanjiDone = false;

            for (int i = 0; i < surface.Length; i++)
            {
                var ch = surface[i];

                if (IsKanji(ch))
                {
                    if (!firstKanjiDone && !string.IsNullOrEmpty(hira))
                    {
                        // ノード内の最初の漢字：読み全体を付与
                        sb.Append("<ruby>")
                          .Append(EscapeHtml(ch.ToString()))
                          .Append("<rt>")
                          .Append(EscapeHtml(hira))
                          .Append("</rt></ruby>");
                        firstKanjiDone = true;
                    }
                    else
                    {
                        // 2 個目以降の漢字、または読みが無い場合：空 rt
                        sb.Append("<ruby>")
                          .Append(EscapeHtml(ch.ToString()))
                          .Append("<rt></rt></ruby>");
                    }
                }
                else
                {
                    // 非漢字はそのまま
                    sb.Append(EscapeHtml(ch.ToString()));
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>CJK 統合漢字 / 拡張A / 互換漢字の範囲に含まれるかを判定する。</summary>
    private static bool IsKanji(char c)
        => (c >= 0x4E00 && c <= 0x9FFF)     // CJK 統合漢字
        || (c >= 0x3400 && c <= 0x4DBF)     // CJK 拡張A
        || (c >= 0xF900 && c <= 0xFAFF);    // CJK 互換漢字

    /// <summary>ひらがな (U+3040–U+309F) かを判定する。</summary>
    private static bool IsHiragana(char c) => c >= 0x3040 && c <= 0x309F;

    /// <summary>カタカナ (U+30A0–U+30FF, U+31F0–U+31FF) かを判定する。</summary>
    private static bool IsKatakana(char c) => (c >= 0x30A0 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF);

    /// <summary>ひらがなまたはカタカナかを判定する。</summary>
    private static bool IsKana(char c) => IsHiragana(c) || IsKatakana(c);

    /// <summary>文字列からかな文字（ひらがな・カタカナ）のみを抽出する。</summary>
    private static string KeepKanaOnly(string s)
        => new string(s.Where(IsKana).ToArray());

    /// <summary>HTML 特殊文字 (&amp; &lt; &gt; &quot;) をエスケープする。</summary>
    private static string EscapeHtml(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// カタカナ (ァ〜ヶ) をひらがなに変換する（-0x60 シフト）。長音符「ー」や記号は対象外。
    /// </summary>
    private static string KatakanaToHiragana(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        // 変換対象：カタカナ本体（ァ〜ヶ）。長音符「ー」や記号は対象外。
        // ヴ(30F4)、ヵ(30F5)、ヶ(30F6) も含める。
        return Regex.Replace(s, "[\u30A1-\u30F6]", m =>
        {
            char katakana = m.Value[0];
            // 長音符はパターンに含めていないが、将来の拡張で混入してもそのまま返す
            if (katakana == '\u30FC') return m.Value;
            // カタカナ→ひらがな（基本は -0x60 でOK）
            char hira = (char)(katakana - 0x60);
            return new string(hira, 1);
        });
    }

    /// <summary>
    /// 指定シリーズが TV シリーズ一覧の中で最新（start_date が最大）かを判定する。
    /// 新規エピソード追加の可否判定に使用する。
    /// </summary>
    private bool IsLatestTvSeries(Series s) // ← 追加（最小追加）
    {
        if (_tvSeries.Count == 0) return false;
        var maxStart = _tvSeries.Max(x => x.StartDate);
        return s.StartDate == maxStart;
    }

    /// <summary>TextBox 内の URL を既定ブラウザで開く（http/https のみ、失敗時は黙殺）。</summary>
    private static void OpenUrlFromTextBox(TextBox tb) // ← 追加（最小追加）
    {
        var url = tb.Text?.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            }
            catch
            {
                // 失敗時は黙殺
            }
        }
    }

    /// <summary>
    /// 全角チルダ(～) → 全角波ダッシュ(〜)
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    private static string NormalizeWaveDash(string s)
    => string.IsNullOrEmpty(s) ? s : s.Replace('\uFF5E', '\u301C');

    /// <summary>
    /// Ctrl+R キーハンドラ: &lt;rt&gt; 内の選択テキスト（またはキャレット以降）を次の &lt;rt&gt; に移動する。
    /// &lt;ruby&gt; 範囲外の場合は &lt;br&gt; を挿入する。
    /// </summary>
    private void TxtTitleRichHtml_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.R)
        {
            bool handled =
                (txtTitleRichHtml.SelectionLength > 0 && TryShiftSelectedRtTailToNext())
                || (txtTitleRichHtml.SelectionLength == 0 && TryShiftCaretTailToNext());

            if (!handled)
            {
                // <ruby> 範囲外 → <br> を挿入
                InsertAtCaret(txtTitleRichHtml, "<br>");
                handled = true;
            }

            if (handled)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                ShowHtmlPreview();
            }
        }
    }

    /// <summary>
    /// 選択テキストが &lt;rt&gt;…&lt;/rt&gt; 内にある場合、選択部分を次の &lt;rt&gt; の先頭に移動する。
    /// ルビの読みを隣の漢字に振り直す操作を支援する。
    /// </summary>
    /// <returns>移動が成功した場合は true。</returns>
    private bool TryShiftSelectedRtTailToNext()
    {
        var text = txtTitleRichHtml.Text ?? string.Empty;
        int selStart = txtTitleRichHtml.SelectionStart;
        int selEnd = selStart + txtTitleRichHtml.SelectionLength;
        if (selStart < 0 || selEnd > text.Length) return false;

        // いまの選択が含まれる直近の <rt>…</rt> を特定
        int rtOpen = text.LastIndexOf("<rt>", selStart, StringComparison.OrdinalIgnoreCase);
        if (rtOpen < 0) return false;
        int rtContentStart = rtOpen + 4;

        int rtClose = text.IndexOf("</rt>", rtContentStart, StringComparison.OrdinalIgnoreCase);
        if (rtClose < 0) return false;

        // 選択が <rt>…</rt> の“中“に完全に収まっていることを確認
        if (selStart < rtContentStart || selEnd > rtClose) return false;

        string before = text.Substring(rtContentStart, selStart - rtContentStart);
        string selected = text.Substring(selStart, selEnd - selStart);
        string after = text.Substring(selEnd, rtClose - selEnd);

        // 次の <rt> を探す
        int nextOpen = text.IndexOf("<rt>", rtClose + 5, StringComparison.OrdinalIgnoreCase);
        if (nextOpen >= 0)
        {
            int nextContentStart = nextOpen + 4;
            int nextClose = text.IndexOf("</rt>", nextContentStart, StringComparison.OrdinalIgnoreCase);
            if (nextClose < 0) return false;

            string nextContent = text.Substring(nextContentStart, nextClose - nextContentStart);

            // 新しい文字列を組み立て：
            // 現在の <rt> には before のみ残し、after は“次の <rt>”の先頭へ移動
            var sb = new StringBuilder();
            sb.Append(text, 0, rtContentStart);          // ～ 現在 <rt> の中身の直前まで
            sb.Append(before);                            // 残留
            sb.Append(text, rtClose, nextContentStart - rtClose); // 現在 </rt> ～ 次の <rt> 開タグ直後まで
            sb.Append(after);                             // ← after を次の <rt> 先頭へ
            sb.Append(nextContent);                       // もともとの次の内容を後ろへ
            sb.Append(text, nextClose, text.Length - nextClose);  // 次の </rt> 以降

            txtTitleRichHtml.Text = sb.ToString();
            txtTitleRichHtml.SelectionStart = rtContentStart + before.Length;
            txtTitleRichHtml.SelectionLength = 0;
            return true;
        }
        else
        {
            // 次の <rt> が無い：選択のみ削除して、after は元の <rt> に残す
            var sb = new StringBuilder();
            sb.Append(text, 0, rtContentStart);
            sb.Append(before);
            sb.Append(after);
            sb.Append(text, rtClose, text.Length - rtClose);

            txtTitleRichHtml.Text = sb.ToString();
            txtTitleRichHtml.SelectionStart = rtContentStart + before.Length;
            txtTitleRichHtml.SelectionLength = 0;
            return true;
        }
    }

    /// <summary>
    /// キャレットが &lt;rt&gt;…&lt;/rt&gt; 内にある場合、キャレット以降の文字を次の &lt;rt&gt; の先頭に移動する。
    /// 選択なし版の TryShiftSelectedRtTailToNext。
    /// </summary>
    /// <returns>移動が成功した場合は true。</returns>
    private bool TryShiftCaretTailToNext()
    {
        var text = txtTitleRichHtml.Text ?? string.Empty;
        int caret = txtTitleRichHtml.SelectionStart;
        if (caret < 0 || caret > text.Length) return false;

        // キャレットが属する <rt>…</rt> を特定
        int rtOpen = text.LastIndexOf("<rt>", caret, StringComparison.OrdinalIgnoreCase);
        if (rtOpen < 0) return false;
        int rtContentStart = rtOpen + 4;

        int rtClose = text.IndexOf("</rt>", rtContentStart, StringComparison.OrdinalIgnoreCase);
        if (rtClose < 0) return false;

        // キャレットが <rt>…</rt> の“中”でなければ何もしない
        if (caret < rtContentStart || caret > rtClose) return false;

        string before = text.Substring(rtContentStart, caret - rtContentStart);
        string after = text.Substring(caret, rtClose - caret);

        // 次の <rt> を探す
        int nextOpen = text.IndexOf("<rt>", rtClose + 5, StringComparison.OrdinalIgnoreCase);
        if (nextOpen >= 0)
        {
            int nextContentStart = nextOpen + 4;
            int nextClose = text.IndexOf("</rt>", nextContentStart, StringComparison.OrdinalIgnoreCase);
            if (nextClose < 0) return false;

            string nextContent = text.Substring(nextContentStart, nextClose - nextContentStart);

            var sb = new StringBuilder();
            sb.Append(text, 0, rtContentStart);                         // ～ 現在 <rt> の中身の直前
            sb.Append(before);                                           // 残留
            sb.Append(text, rtClose, nextContentStart - rtClose);        // 現在 </rt> ～ 次の <rt> 開タグ直後
            sb.Append(after);                                            // ← after を次の <rt> 先頭へ
            sb.Append(nextContent);                                      // 次のもともとの内容
            sb.Append(text, nextClose, text.Length - nextClose);         // 次の </rt> 以降

            txtTitleRichHtml.Text = sb.ToString();
            txtTitleRichHtml.SelectionStart = rtContentStart + before.Length; // キャレットは残留末尾
            txtTitleRichHtml.SelectionLength = 0;
            return true;
        }
        else
        {
            // 次の <rt> が無い：何も移さず、そのまま（＝何もする必要が無いので false にしておく）
            return false;
        }
    }

    /// <summary>
    /// 先頭が "/1/" を含むURLだけ話数で置換
    /// </summary>
    /// <param name="urlOfEp1"></param>
    /// <param name="epNo"></param>
    /// <returns></returns>
    private static string? BuildEpisodeUrlFromEp1(string? urlOfEp1, int epNo)
    {
        if (string.IsNullOrWhiteSpace(urlOfEp1)) return null;
        // "/1/" のみを対象に置換（要件どおり）
        return urlOfEp1.Contains("/1/") ? urlOfEp1.Replace("/1/", $"/{epNo}/") : null;
    }

    /// <summary>
    /// HTTPステータスコードが200かを確認
    /// </summary>
    /// <param name="url"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private static async Task<bool> UrlExistsAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return res.StatusCode == System.Net.HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// あらすじページHTMLから YouTube 動画IDを抽出（Crawler と同等ロジック）
    /// </summary>
    /// <param name="html"></param>
    /// <returns></returns>
    private static string? TryExtractYoutubeId(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        // 1) 通常の watch?v=XXXX
        var m = Regex.Match(html, @"https?://(?:www\.)?youtube\.com/watch\?v=([A-Za-z0-9_\-]{11})");
        if (m.Success && m.Groups[1].Value != "iqGPVmdr-3A") return m.Groups[1].Value;

        // 2) youtu.be/XXXX
        m = Regex.Match(html, @"https?://youtu\.be/([A-Za-z0-9_\-]{11})");
        if (m.Success && m.Groups[1].Value != "iqGPVmdr-3A") return m.Groups[1].Value;

        // 3) data-mvid=XXXX 形式（東映サイトのボタン埋め込み）
        m = Regex.Match(html, @"data-mvid\s*=\s*([A-Za-z0-9_\-]{11})\??");
        if (m.Success && m.Groups[1].Value != "iqGPVmdr-3A") return m.Groups[1].Value;

        // 4) <iframe src="https://www.youtube.com/embed/XXXX">
        m = Regex.Match(html, @"youtube\.com/embed/([A-Za-z0-9_\-]{11})");
        if (m.Success && m.Groups[1].Value != "iqGPVmdr-3A") return m.Groups[1].Value;

        return null;
    }

    /// <summary>
    /// 現在選択中エピソードに対する URL 提案（実体）
    /// </summary>
    /// <param name="ep"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task SuggestEpisodeUrlsAsync(Episode ep, CancellationToken ct = default)
    {
        if (_currentSeries is null) return;

        // 1 話の URL を基点に推測
        var ep1 = _episodes.FirstOrDefault(x => x.SeriesEpNo == 1);
        var epNo = ep.SeriesEpNo;

        // ---- あらすじ URL ----
        if (string.IsNullOrWhiteSpace(txtToeiSummary.Text))
        {
            var candidate = BuildEpisodeUrlFromEp1(ep1?.ToeiAnimSummaryUrl, epNo);
            if (!string.IsNullOrWhiteSpace(candidate) && await UrlExistsAsync(candidate, ct).ConfigureAwait(false))
            {
                txtToeiSummary.Text = candidate;
                txtToeiSummary.BackColor = _hintBack; // 提案色
            }
        }

        // ---- ラインナップ URL ----
        if (string.IsNullOrWhiteSpace(txtToeiLineup.Text))
        {
            var candidate = BuildEpisodeUrlFromEp1(ep1?.ToeiAnimLineupUrl, epNo);
            if (!string.IsNullOrWhiteSpace(candidate) && await UrlExistsAsync(candidate, ct).ConfigureAwait(false))
            {
                txtToeiLineup.Text = candidate;
                txtToeiLineup.BackColor = _hintBack; // 提案色
            }
        }

        // ---- YouTube 予告 URL ----
        // 条件：YouTube 未入力 && あらすじURLが有効（既にあった or 上で入った）
        if (string.IsNullOrWhiteSpace(txtYoutube.Text))
        {
            var summaryUrl = txtToeiSummary.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(summaryUrl) && await UrlExistsAsync(summaryUrl!, ct).ConfigureAwait(false))
            {
                try
                {
                    var html = await _http.GetStringAsync(summaryUrl!, ct).ConfigureAwait(false);
                    var vid = TryExtractYoutubeId(html);
                    if (!string.IsNullOrWhiteSpace(vid))
                    {
                        txtYoutube.Text = $"https://www.youtube.com/watch?v={vid}";
                        txtYoutube.BackColor = _hintBack; // 提案色
                    }
                }
                catch { /* 失敗は無視（提案なし） */ }
            }
        }
    }

    /// <summary>
    /// パートグリッド (dgvParts) の列定義・DnD イベント・追加/削除ボタンを初期化する。
    /// 列構成: 順 / 種別 (ComboBox) / OA尺 / 円盤尺 / 配信尺 / OA時刻 (ReadOnly) / 備考。
    /// </summary>
    private void InitPartsGrid()
    {
        dgvParts.AutoGenerateColumns = false;

        // 目に見える並び（読み取り専用）
        dgvParts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PartRow.EpisodeSeq),
            HeaderText = "順",
            Width = 40,
            ReadOnly = true,
            Name = "colSeq"
        });

        // 種別（ComboBox列）
        var colType = new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(PartRow.PartType),
            HeaderText = "種別",
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            FlatStyle = FlatStyle.Flat,
            Width = 140,
            Name = "colType"
        };
        dgvParts.Columns.Add(colType);

        // 尺（OA / 円盤 / 配信）
        dgvParts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PartRow.OaLength),
            HeaderText = "OA(s)",
            Width = 70,
            Name = "colOa"
        });
        dgvParts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PartRow.DiscLength),
            HeaderText = "円盤(s)",
            Width = 70,
            Name = "colDisc"
        });
        dgvParts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PartRow.VodLength),
            HeaderText = "配信(s)",
            Width = 70,
            Name = "colVod"
        });

        // OA時刻
        dgvParts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PartRow.OaTime),
            HeaderText = "OA時刻",
            Width = 140,
            Name = "colOaTime",
            ReadOnly = true
        });

        // 備考
        dgvParts.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PartRow.Notes),
            HeaderText = "備考",
            Width = 280,
            Name = "colNotes"
        });

        // DnD 並べ替え（簡易）
        dgvParts.AllowDrop = true;
        dgvParts.MouseDown += DgvParts_MouseDown;
        dgvParts.MouseMove += DgvParts_MouseMove;
        dgvParts.DragOver += DgvParts_DragOver;
        dgvParts.DragDrop += DgvParts_DragDrop;

        // 追加/削除
        btnPartAdd.Click += (_, __) =>
        {
            _partRows.Add(new PartRow
            {
                EpisodeSeq = _partRows.Count + 1,
                PartType = _partTypeOptions.FirstOrDefault().Code ?? string.Empty,
                OriginalSeq = 0
            });
            RecalcTotals();
        };
        btnPartDelete.Click += (_, __) =>
        {
            if (dgvParts.CurrentRow?.DataBoundItem is PartRow r)
            {
                _partRows.Remove(r);
                RenumberSeq();
                RecalcTotals();
            }
        };

        dgvParts.CurrentCellDirtyStateChanged += (_, __) =>
        {
            // ComboBox ではなく Checkbox の即時確定のみに限定する
            if (dgvParts.IsCurrentCellDirty && dgvParts.CurrentCell is DataGridViewCheckBoxCell)
                dgvParts.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        dgvParts.DataSource = _partRows;
    }

    /// <summary>
    /// パートグリッドの DnD: マウス移動時にドラッグ開始を判定する。
    /// システム既定のドラッグしきい値を超えた場合に DoDragDrop を開始する。
    /// </summary>
    private void DgvParts_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragCandidate) return;
        if ((e.Button & MouseButtons.Left) == 0) { _dragCandidate = false; return; }

        // コンボや編集中は開始しない（二重防御）
        if (dgvParts.IsCurrentCellInEditMode) { _dragCandidate = false; return; }
        var hit = dgvParts.HitTest(e.X, e.Y);
        if (hit.Type == DataGridViewHitTestType.Cell && dgvParts.Columns[hit.ColumnIndex] is DataGridViewComboBoxColumn)
        {
            _dragCandidate = false; return;
        }

        // システム既定のドラッグしきい値以上の移動で開始
        var dragRect = SystemInformation.DragSize;
        var dx = Math.Abs(e.X - _dragStartPoint.X);
        var dy = Math.Abs(e.Y - _dragStartPoint.Y);
        if (dx >= dragRect.Width || dy >= dragRect.Height)
        {
            _dragCandidate = false;
            dgvParts.DoDragDrop(dgvParts.Rows[_dragRowIndex], DragDropEffects.Move);
        }
    }

    /// <summary>DnD: マウスダウンで行ヘッダ or 順序列 (colSeq) からのドラッグ候補を記録する。</summary>
    private void DgvParts_MouseDown(object? sender, MouseEventArgs e)
    {
        var hit = dgvParts.HitTest(e.X, e.Y);
        _dragRowIndex = hit.RowIndex;
        _dragCandidate = false;
        _dragStartPoint = new Point(e.X, e.Y);

        if (e.Button != MouseButtons.Left) return;
        if (_dragRowIndex < 0) return;

        // コンボセルや編集中はドラッグ禁止
        if (dgvParts.IsCurrentCellInEditMode) return;
        if (hit.Type == DataGridViewHitTestType.Cell && dgvParts.Columns[hit.ColumnIndex] is DataGridViewComboBoxColumn) return;

        // 行ヘッダ or 先頭の順序列（colSeq）からのドラッグだけ許可
        bool onRowHeader = hit.Type == DataGridViewHitTestType.RowHeader;
        bool onSeqColumn = (hit.ColumnIndex >= 0) && dgvParts.Columns[hit.ColumnIndex].Name == "colSeq";
        if (onRowHeader || onSeqColumn)
            _dragCandidate = true;
    }

    /// <summary>DnD: ドラッグ中のカーソル効果を Move に設定する。</summary>
    private void DgvParts_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.Move;
    }

    /// <summary>DnD: ドロップ先の行に要素を移動し、seq を振り直して OA 時刻を再計算する。</summary>
    private void DgvParts_DragDrop(object? sender, DragEventArgs e)
    {
        var clientPoint = dgvParts.PointToClient(new Point(e.X, e.Y));
        var targetIndex = dgvParts.HitTest(clientPoint.X, clientPoint.Y).RowIndex;
        if (_dragRowIndex < 0 || targetIndex < 0 || targetIndex == _dragRowIndex) return;

        var item = _partRows[_dragRowIndex];
        _partRows.RemoveAt(_dragRowIndex);
        _partRows.Insert(targetIndex, item);
        RenumberSeq();
        RecalcOaTimes();           // 順序変更に追従
        dgvParts.ClearSelection();
        dgvParts.Rows[targetIndex].Selected = true;
        dgvParts.CurrentCell = dgvParts.Rows[targetIndex].Cells["colSeq"];
    }

    /// <summary>パートグリッドの EpisodeSeq を 1..N に振り直す。</summary>
    private void RenumberSeq()
    {
        for (int i = 0; i < _partRows.Count; i++)
            _partRows[i].EpisodeSeq = i + 1;
        dgvParts.Refresh();
        RecalcTotals();
    }

    /// <summary>
    /// パートグリッド全行の OA/円盤/配信尺を合計し、ラベルに "合計: OA=m:ss / 円盤=m:ss / 配信=m:ss" 形式で表示する。
    /// 配信尺には series.vod_intro（VOD 導入尺）を加算する。
    /// </summary>
    private void RecalcTotals()
    {
        int oa = 0, disc = 0, vod = 0;
        foreach (var r in _partRows)
        {
            if (r.OaLength is ushort a) oa += a;
            if (r.DiscLength is ushort b) disc += b;
            if (r.VodLength is ushort c) vod += c;
        }

        // series の VOD 導入尺を加算（指示仕様）
        if (_currentSeries?.VodIntro is ushort addVod) vod += addVod;

        var oa_span = new TimeSpan(0, 0, oa);
        var disc_span = new TimeSpan(0, 0, disc);
        var vod_span = new TimeSpan(0, 0, vod);

        // フォーマットする
        var oa_fmt = oa_span.ToString(@"m\:ss");
        var disc_fmt = disc_span.ToString(@"m\:ss");
        var vod_fmt = vod_span.ToString(@"m\:ss");

        lblPartTotals.Text = $"合計: OA={oa_fmt} / 円盤={disc_fmt} / 配信={vod_fmt}";
    }

    /// <summary>
    /// 既存の LoadTvAsync の最後など “TVシリーズ一覧のロード後” に1回呼ぶ
    /// </summary>
    /// <returns></returns>
    private async Task LoadPartTypesOnceAsync()
    {
        if (_partTypeOptions.Count > 0) return;
        var types = await _partTypesRepo.GetAllAsync();
        _partTypeOptions = types.Select(t => (t.PartTypeCode, t.NameJa ?? t.PartTypeCode)).ToList();

        var colType = dgvParts.Columns["colType"] as DataGridViewComboBoxColumn;
        if (colType != null)
        {
            // List<(string Code, string Name)> をバインド可能な匿名型に変換
            var comboSource = (_partTypeOptions ?? new List<(string Code, string Name)>())
                .Select(t => new { t.Code, t.Name })
                .ToList();

            colType.DisplayMember = "Name";
            colType.ValueMember = "Code";
            colType.DataSource = comboSource;
        }
    }

    /// <summary>
    /// 選択エピソードのパート一覧を DB から取得し、グリッドにバインドする。
    /// 各行の OA 時刻（開始～終了）を放送開始時刻 + 累積尺で算出する。
    /// </summary>
    private async Task LoadPartsForEpisodeAsync()
    {
        _partRows.Clear();
        if (_currentEpisode is null) { dgvParts.Refresh(); RecalcTotals(); return; }

        var episode_start_time = _currentEpisode.OnAirAt;
        var length_sum = 0;

        var rows = await _partsRepo.GetByEpisodeAsync(_currentEpisode.EpisodeId);

        // 元状態を保持（以降の差分検出に使う）
        _loadedEpisodeParts = rows.ToList();

        foreach (var p in rows)
        {
            var oa_time = string.Empty;

            if (p.OaLength != null)
            {
                var start_time = episode_start_time + new TimeSpan(0, 0, length_sum);
                var end_time = start_time + new TimeSpan(0, 0, (int)p.OaLength);
                length_sum += (int)p.OaLength;
                oa_time = start_time.ToString(@"HH\:mm\:ss") + "～" + end_time.ToString(@"HH\:mm\:ss");
            }

            _partRows.Add(new PartRow
            {
                EpisodeSeq = p.EpisodeSeq,
                PartType = p.PartType,
                OaLength = p.OaLength,
                DiscLength = p.DiscLength,
                VodLength = p.VodLength,
                OaTime = oa_time,
                Notes = p.Notes,
                OriginalSeq = p.EpisodeSeq
            });
        }
        RenumberSeq(); // 念のため整形
    }

    /// <summary>
    /// パートグリッドの内容を差分操作 (BuildEpisodePartOps → ApplyOps) で DB に保存する。
    /// 保存後は OriginalSeq / IsContentDirty をリセットし、次回差分判定に備える。
    /// </summary>
    private async Task SavePartsAsync()
    {
        if (_currentEpisode is null) return;

        // 1..Nに振り直し（D&D後など）
        RenumberSeq();

        // 画面の PartRow → Repository DTO へ
        var list = _partRows.Select((r, idx) => new EpisodePart
        {
            EpisodeId = _currentEpisode.EpisodeId,
            EpisodeSeq = (byte)(idx + 1),           // 1..N に振り直し
            PartType = r.PartType ?? "",
            OaLength = r.OaLength,
            DiscLength = r.DiscLength,
            VodLength = r.VodLength,
            Notes = r.Notes,
            CreatedBy = AuditUser,
            UpdatedBy = AuditUser
        })
        .ToList();

        // 1..Nに振り直し（D&D後など）
        RenumberSeq();

        // 差分を組み立てて実行（入替はseqだけ、値変更は値だけ）
        var ops = BuildEpisodePartOps();
        await _partsRepo.ApplyOpsAsync(_currentEpisode.EpisodeId, ops, AuditUser, CancellationToken.None);

        // 成功後：画面側の元状態を更新（次回の差分判定が正しくなるように）
        foreach (var r in _partRows)
        {
            r.OriginalSeq = (byte)r.EpisodeSeq;
            r.IsContentDirty = false;
        }

        // バックアップも今の状態で再構築（再読み込みでもOK）
        _loadedEpisodeParts = _partRows.Select(r => new EpisodePart
        {
            EpisodeId = _currentEpisode.EpisodeId,
            EpisodeSeq = (byte)r.EpisodeSeq,
            PartType = r.PartType ?? "",
            OaLength = r.OaLength,
            DiscLength = r.DiscLength,
            VodLength = r.VodLength,
            Notes = r.Notes,
            CreatedBy = AuditUser,
            UpdatedBy = AuditUser
        }).ToList();
    }

    /// <summary>ComboBox セルの編集開始時に DropDownList スタイルを強制する（自由入力を禁止）。</summary>
    private void DgvParts_EditingControlShowing(object? sender, DataGridViewEditingControlShowingEventArgs e)
    {
        if (e.Control is DataGridViewComboBoxEditingControl cb)
        {
            cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cb.FlatStyle = FlatStyle.Standard;
        }
    }

    /// <summary>
    /// 直前のエピソードのフォーマット（パート一覧）をそのまま現在話のグリッドへ展開する。
    /// DB更新は行わない。保存時に現在話の EpisodeId で ReplaceAll が走る。
    /// </summary>
    /// <returns></returns>
    private async Task CopyFromPreviousAsync()
    {
        if (_currentSeries is null || _currentEpisode is null) return;
        var idx = lstEpisodes.SelectedIndex;
        if (idx <= 0 || idx >= _episodes.Count) return; // 前話が無い

        var prev = _episodes[idx - 1];
        if (prev is null || prev.EpisodeId <= 0) return;

        // 前話のパートを取得
        var rows = await _partsRepo.GetByEpisodeAsync(prev.EpisodeId);

        // OA時刻の再計算（画面表示用）
        var episodeStart = _currentEpisode.OnAirAt;
        int lenSum = 0;

        _partRows.Clear();

        foreach (var p in rows)
        {
            string oaTime = string.Empty;
            if (p.OaLength is ushort a)
            {
                var start = episodeStart + TimeSpan.FromSeconds(lenSum);
                var end = start + TimeSpan.FromSeconds(a);
                lenSum += a;
                oaTime = $"{start:HH\\:mm\\:ss}～{end:HH\\:mm\\:ss}";
            }

            _partRows.Add(new PartRow
            {
                // EpisodeId はバインドしない（保存時に現在話IDでDTO化）
                EpisodeSeq = p.EpisodeSeq,   // 後で Renumber で1..Nに整える
                PartType = p.PartType,
                OaLength = p.OaLength,
                DiscLength = p.DiscLength,
                VodLength = p.VodLength,
                OaTime = oaTime,
                Notes = p.Notes,
                OriginalSeq = 0
            });
        }

        RenumberSeq();
        RecalcTotals();
        dgvParts.Refresh();
    }

    /// <summary>
    /// セル値変更時: OaLength 列なら OA 時刻再計算 + 合計更新。
    /// 値変更があった行の IsContentDirty を true にマークする（差分保存用）。
    /// </summary>
    private void DgvParts_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        var col = dgvParts.Columns[e.ColumnIndex];

        // OaLength 列だけをトリガに OA時刻と合計を更新
        var isOaLength = string.Equals(col.DataPropertyName, nameof(PartRow.OaLength), StringComparison.Ordinal);
        if (isOaLength)
        {
            RecalcOaTimes();
            RecalcTotals(); // 既存の合計ラベル更新ロジックを再利用
        }

        var colName = dgvParts.Columns[e.ColumnIndex].DataPropertyName;
        if (e.RowIndex >= 0 && (colName == nameof(PartRow.PartType)
                             || colName == nameof(PartRow.OaLength)
                             || colName == nameof(PartRow.DiscLength)
                             || colName == nameof(PartRow.VodLength)
                             || colName == nameof(PartRow.Notes)))
        {
            if (dgvParts.Rows[e.RowIndex].DataBoundItem is PartRow r)
                r.IsContentDirty = true;
        }
    }

    /// <summary>
    /// _partRows の OaLength をもとに、dtOnAirAt を基準とした OA時刻 (OaTime) を全行再計算して反映する。
    /// 保存ロジックや EpisodeId には影響しない（UI表示のみ更新）。
    /// </summary>
    private void RecalcOaTimes()
    {
        if (_partRows == null || _partRows.Count == 0) return;

        // 画面上の放送開始時刻（dtOnAirAt）を基準に計算する
        var baseStart = dtOnAirAt.Value;
        int accSec = 0;

        foreach (var row in _partRows)
        {
            string oa = string.Empty;

            if (row?.OaLength is ushort secs && secs > 0)
            {
                var start = baseStart + TimeSpan.FromSeconds(accSec);
                var end = start + TimeSpan.FromSeconds(secs);
                oa = $"{start:HH\\:mm\\:ss}～{end:HH\\:mm\\:ss}";
                accSec += secs;
            }

            // 既存の列名に合わせて、画面表示用の OaTime を上書き
            row!.OaTime = oa;
        }

        // 列の存在を明示チェックしてから Index を参照
        var col = dgvParts.Columns[nameof(PartRow.OaTime)];
        if (col != null)
            dgvParts.InvalidateColumn(col.Index);
        else
            dgvParts.Invalidate();
    }

    /// <summary>
    /// 画面のパートグリッドと読み込み時の状態 (_loadedEpisodeParts) を比較し、
    /// 差分操作（Delete / Move / Update / Insert）を構築する。
    /// <para>
    /// 判定ロジック: OriginalSeq=0 → 新規 INSERT。
    /// Dirty=false + Moved=true → MOVE（seq のみ変更）。
    /// Dirty=true + Moved=false → UPDATE（値のみ変更）。
    /// Dirty=true + Moved=true → DELETE + INSERT（安全に再作成）。
    /// 旧行のうち使われなかったもの → 純粋 DELETE。
    /// </para>
    /// </summary>
    private EpisodePartsRepository.EpisodePartOps BuildEpisodePartOps()
    {
        var current = _partRows.ToList(); // RenumberSeq() 済み想定

        var originalSeqs = new HashSet<byte>(_loadedEpisodeParts.Select(p => p.EpisodeSeq));
        var usedOriginalSeqs = new HashSet<byte>();

        var moves = new List<(byte OldSeq, byte NewSeq)>();
        var updates = new List<EpisodePart>();
        var deletesForChangedMoved = new List<byte>();
        var insertsForChangedMoved = new List<EpisodePart>();
        var insertsNew = new List<EpisodePart>();

        foreach (var r in current)
        {
            var newSeq = (byte)r.EpisodeSeq;

            // OriginalSeq == 0 は画面上で新規追加された行 → INSERT 対象
            if (r.OriginalSeq == 0)
            {
                insertsNew.Add(new EpisodePart
                {
                    EpisodeId = _currentEpisode!.EpisodeId,
                    EpisodeSeq = newSeq,
                    PartType = r.PartType ?? "",
                    OaLength = r.OaLength,
                    DiscLength = r.DiscLength,
                    VodLength = r.VodLength,
                    Notes = r.Notes
                });
                continue;
            }

            // 既存行
            var oldSeq = r.OriginalSeq;
            var dirty = r.IsContentDirty;
            var moved = oldSeq != newSeq;

            if (!dirty && moved)
            {
                // 位置だけ変更 → MOVE
                moves.Add((oldSeq, newSeq));
                usedOriginalSeqs.Add(oldSeq);           // ★ 旧行は使うので削除しない
            }
            else if (dirty && !moved)
            {
                // 値だけ変更 → UPDATE
                updates.Add(new EpisodePart
                {
                    EpisodeId = _currentEpisode!.EpisodeId,
                    EpisodeSeq = newSeq,
                    PartType = r.PartType ?? "",
                    OaLength = r.OaLength,
                    DiscLength = r.DiscLength,
                    VodLength = r.VodLength,
                    Notes = r.Notes
                });
                usedOriginalSeqs.Add(oldSeq);           // ★ 旧行は使うので削除しない
            }
            else if (dirty && moved)
            {
                // 値＋位置の同時変更 → Delete→Insert で安全に
                deletesForChangedMoved.Add(oldSeq);     // ★ 旧行は消す
                insertsForChangedMoved.Add(new EpisodePart
                {
                    EpisodeId = _currentEpisode!.EpisodeId,
                    EpisodeSeq = newSeq,
                    PartType = r.PartType ?? "",
                    OaLength = r.OaLength,
                    DiscLength = r.DiscLength,
                    VodLength = r.VodLength,
                    Notes = r.Notes
                });
                // usedOriginalSeqs には入れない（消すので）
            }
            else
            {
                // 変更なし（Dirty=false, Moved=false）→ 何もしないが「使われた旧行」としてマーク
                usedOriginalSeqs.Add(oldSeq);           // ★ これが抜けていたせいで“削除扱い”になっていた
            }
        }

        // 純粋削除: 旧行 (originalSeqs) から使用済み行 (usedOriginalSeqs) を除いた残り
        var deletesPure = originalSeqs.Except(usedOriginalSeqs).ToList();

        return new EpisodePartsRepository.EpisodePartOps
        {
            Deletes = deletesPure.Concat(deletesForChangedMoved).Distinct().OrderBy(x => x).ToList(),
            Moves = moves,
            Updates = updates,
            Inserts = insertsForChangedMoved.Concat(insertsNew).ToList()
        };
    }

    /// <summary>
    /// サブタイトルの“登場順ユニーク”で1行ずつ出力を組み立て、最終テキストを返す。
    /// </summary>
    /// <param name="episodeId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task<string> BuildTitleInformationPerCharAsync(int episodeId, CancellationToken ct)
    {
        // 呼び出し時点のエピソードを固定（途中切替の取り違え防止）
        var ep = _currentEpisode;
        if (ep == null || ep.EpisodeId != episodeId)
            return string.Empty;

        var title = ep.TitleText ?? string.Empty;

        // 1) サブタイトル内の「最初に出た順」のユニーク文字（空白除外、大小文字は厳密区別）
        var orderedChars = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ch in title)
        {
            if (char.IsWhiteSpace(ch)) continue;
            var s = ch.ToString();
            if (seen.Add(s)) orderedChars.Add(s);
        }
        if (orderedChars.Count == 0)
            return string.Empty;

        // 2) 各文字について「初出」（プリキュア史上初使用）と「唯一」（全シリーズで 1 話だけ）を判定
        var firstSet = new HashSet<string>(StringComparer.Ordinal);
        var uniqueSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in orderedChars)
        {
            if (_currentEpisode == null || _currentEpisode.EpisodeId != episodeId)
                return string.Empty;

            var (firstId, _) = await _episodesRepo.GetFirstUseOfCharAsync(c).ConfigureAwait(false);
            var cnt = await _episodesRepo.GetEpisodeUsageCountOfCharAsync(c).ConfigureAwait(false);

            if (firstId.HasValue && firstId.Value == ep.EpisodeId) firstSet.Add(c);
            if (cnt == 1) uniqueSet.Add(c);
        }

        // 3) 「1 年以上ぶり」の復活文字: 前回使用シリーズ・話数・サブタイトル・OA 日（シリーズ名・話数・サブタイトル・OA日）
        var revival = await _episodesRepo.GetTitleCharRevivalStatsAsync(episodeId, ct).ConfigureAwait(false);
        var revivalSeq = revival ?? Enumerable.Empty<EpisodesRepository.TitleCharRevivalStat>();
        var revivalByChar = revivalSeq.ToDictionary(x => x.Char, StringComparer.Ordinal);

        // 4) 行ごとに "文字…[初出] [唯一] N年Mか月(P話)ぶりQ回目 『…』第N話「…」(日付)以来" を組み立て
        var sb = new StringBuilder();
        foreach (var c in orderedChars)
        {
            if (_currentEpisode == null || _currentEpisode.EpisodeId != episodeId)
                return string.Empty;

            bool hasAnything = false;
            var line = new StringBuilder();
            line.Append(c).Append('…');

            if (firstSet.Contains(c)) { line.Append("[初出] "); hasAnything = true; }
            if (uniqueSet.Contains(c)) { line.Append("[唯一] "); hasAnything = true; }

            if (revivalByChar.TryGetValue(c, out var r))
            {
                if (hasAnything) line.Append(' ');
                line.AppendFormat("{0}年{1}か月({2}話)ぶり{3}回目 ",
                    r.Years, r.Months, r.EpisodesSince, r.OccurrenceIndex);

                if (!string.IsNullOrEmpty(r.LastSeriesTitle))
                {
                    line.Append('『').Append(r.LastSeriesTitle).Append('』')
                        .Append('第').Append(r.LastSeriesEpNo).Append("話");
                }
                if (!string.IsNullOrEmpty(r.LastTitleText))
                {
                    line.Append('「').Append(r.LastTitleText).Append('」');
                }
                line.Append("(").Append(r.LastOnAirAt.ToString("yyyy.M.d")).Append(")以来");
                hasAnything = true;
            }

            if (hasAnything)
                sb.AppendLine(line.ToString().TrimEnd());
            // hasAnything==false の文字は出力しない
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 文字統計テキストを非同期で生成し、UI スレッドで txtTitleInformation に一括反映する。
    /// エピソード切替競合を episodeId で防止する。
    /// </summary>
    /// <param name="episodeId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task SetTitleInformationAsync(int episodeId, CancellationToken ct)
    {
        var text = await BuildTitleInformationPerCharAsync(episodeId, ct).ConfigureAwait(false);

        // UIスレッドで一度だけ更新
        void apply()
        {
            if (_currentEpisode?.EpisodeId != episodeId) return; // 切替競合防止
            txtTitleInformation.Text = text;
        }
        if (InvokeRequired) BeginInvoke(new Action(apply)); else apply();
    }

    /// <summary>
    /// パート尺統計（AVANT/PART_A/PART_B のシリーズ内・歴代の順位と偏差値）を非同期で取得し、
    /// txtPartLengthStats に "{パート名}: {シリーズ略称}…{順位}/{総数}位 (偏差値: {値}) 歴代…" 形式で表示する。
    /// </summary>
    private async Task UpdatePartLengthStatsAsync(int episodeId, CancellationToken ct)
    {
        try
        {
            // 現在表示中のエピソードが変わっていたら何もしない
            if (_currentEpisode == null || _currentEpisode.EpisodeId != episodeId)
                return;

            var stats = await _partsRepo.GetPartLengthStatsAsync(episodeId, ct)
                                               .ConfigureAwait(false);
            if (stats == null || stats.Count == 0)
            {
                // 対象パートが存在しない場合は空にする
                void clear() { txtPartLengthStats.Text = string.Empty; }
                if (InvokeRequired) BeginInvoke((Action)clear); else clear();
                return;
            }

            var sb = new StringBuilder();
            foreach (var s in stats)
            {
                sb.AppendFormat(
                    "{0}:\r\n{1}…{2}/{3}位 (偏差値: {4})\t歴代…{5}/{6}位 (偏差値: {7})",
                    s.PartTypeNameJa,
                    s.SeriesTitleShort,
                    s.SeriesRank,
                    s.SeriesTotal,
                    s.SeriesHensachi.ToString("0.00"),
                    s.GlobalRank,
                    s.GlobalTotal,
                    s.GlobalHensachi.ToString("0.00")
                );
                sb.AppendLine();
            }

            var text = sb.ToString().TrimEnd();

            void apply()
            {
                if (_currentEpisode == null || _currentEpisode.EpisodeId != episodeId)
                    return;

                txtPartLengthStats.Text = text;
            }

            if (InvokeRequired) BeginInvoke((Action)apply); else apply();
        }
        catch (Exception ex)
        {
            void showError()
            {
                txtPartLengthStats.Text = $"統計の取得に失敗しました: {ex.Message}";
            }
            if (InvokeRequired) BeginInvoke((Action)showError); else showError();
        }
    }

    /// <summary>
    /// 現在のエピソードについて、時刻つきの「このあと…」用文面を生成します。
    /// 例:
    /// このあと8:30から
    /// 『キミとアイドルプリキュア♪』第43話(通算1061話 / 放送1075回)「うたの歌」（OA: 2025.12.7）
    /// </summary>
    private string BuildJunctionCopyText(Series series, Episode episode)
    {
        // 放送開始時刻（H:mm）
        string time = episode.OnAirAt.ToString("H:mm");
        // 放送日（yyyy.M.d）
        string date = episode.OnAirAt.ToString("yyyy.M.d");

        int seriesEpNo = episode.SeriesEpNo;
        int? totalEpNo = episode.TotalEpNo;
        int? totalOaNo = episode.TotalOaNo;

        string title = episode.TitleText ?? string.Empty;
        string seriesTitle = series.Title ?? string.Empty;

        var sb = new StringBuilder();
        sb.Append("このあと").Append(time).Append("から").AppendLine();
        sb.Append('『').Append(seriesTitle).Append('』')
          .Append("第").Append(seriesEpNo).Append("話")
          .Append("(通算").Append(totalEpNo).Append("話 / 放送").Append(totalOaNo).Append("回)")
          .Append('「').Append(title).Append('」')
          .Append("（OA: ").Append(date).Append('）');

        return sb.ToString();
    }

    /// <summary>
    /// 現在のエピソードについて、「次回…」用文面を生成します。
    /// 例:
    /// 次回『キミとアイドルプリキュア♪』第44話(通算1062話 / 放送1076回)「キラキランドのひみつ！」（OA: 2025.12.14）
    /// </summary>
    private string BuildNextTitleCopyText(Series series, Episode episode)
    {
        // 放送日（yyyy.M.d）
        string date = episode.OnAirAt.ToString("yyyy.M.d");

        int seriesEpNo = episode.SeriesEpNo;
        int? totalEpNo = episode.TotalEpNo;
        int? totalOaNo = episode.TotalOaNo;

        string title = episode.TitleText ?? string.Empty;
        string seriesTitle = series.Title ?? string.Empty;

        var sb = new StringBuilder();
        sb.Append("次回");
        sb.Append('『').Append(seriesTitle).Append('』')
          .Append("第").Append(seriesEpNo).Append("話")
          .Append("(通算").Append(totalEpNo).Append("話 / 放送").Append(totalOaNo).Append("回)")
          .Append('「').Append(title).Append('」')
          .Append("（OA: ").Append(date).Append('）');

        return sb.ToString();
    }

    /// <summary>「このあと…」用テキストをクリップボードにコピーする。</summary>
    private void btnJunctionCopy_Click(object sender, EventArgs e)
    {
        var text = _copyToolTip.GetToolTip(btnJunctionCopy);
        if (string.IsNullOrEmpty(text)) return;

        Clipboard.SetText(text);
    }

    /// <summary>「次回…」用テキストをクリップボードにコピーする。</summary>
    private void btnNextTitleCopy_Click(object sender, EventArgs e)
    {
        var text = _copyToolTip.GetToolTip(btnNextTitleCopy);
        if (string.IsNullOrEmpty(text)) return;

        Clipboard.SetText(text);
    }
}
