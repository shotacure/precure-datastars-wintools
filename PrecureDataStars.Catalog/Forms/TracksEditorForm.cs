#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// トラック管理エディタ。
/// <para>
/// ディスクを選んでトラックを編集する専用画面。旧 <c>DiscsEditorForm</c> からディスク編集機能を外し、
/// SONG / BGM の紐付けは「検索テキスト → 候補リスト → 選択」のオートコンプリート形式で行う。
/// </para>
/// <para>
/// 候補検索は入力の打鍵ごとに 250ms のデバウンスを挟んで発火し、過剰な DB 問い合わせを抑える。
/// SONG は <c>SongsRepository.SearchAsync</c>（曲名/作詞作曲横断）、BGM は
/// <c>BgmCuesRepository.SearchInSeriesAsync</c>（M 番号/メニュー名/作曲者など横断）を使う。
/// </para>
/// </summary>
public partial class TracksEditorForm : Form
{
    private readonly DiscsRepository _discsRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly TrackContentKindsRepository _contentKindsRepo;
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    private readonly SeriesRepository _seriesRepo;

    // 現在のディスク一覧（フィルタ済み）
    private List<Disc> _discs = new();
    // 選択中ディスクのトラック一覧（sub_order=0 の親行のみ）
    private List<Track> _tracks = new();

    // 候補検索のデバウンス（入力ごとに再スタートする Timer 一本で制御）
    private readonly System.Windows.Forms.Timer _songSearchDebounce = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _bgmSearchDebounce = new() { Interval = 250 };
    // 進行中の検索をキャンセルして最新入力だけ採用するためのトークン
    private CancellationTokenSource? _songSearchCts;
    private CancellationTokenSource? _bgmSearchCts;

    // SONG 候補で選んだ曲。null の間は歌唱者コンボは空。
    private Song? _selectedSong;
    // BGM 候補で選んだ cue。
    private BgmCue? _selectedBgmCue;

    public TracksEditorForm(
        DiscsRepository discsRepo,
        TracksRepository tracksRepo,
        TrackContentKindsRepository contentKindsRepo,
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        BgmCuesRepository bgmCuesRepo,
        SongSizeVariantsRepository songSizeVariantsRepo,
        SongPartVariantsRepository songPartVariantsRepo,
        SeriesRepository seriesRepo)
    {
        _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
        _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
        _contentKindsRepo = contentKindsRepo ?? throw new ArgumentNullException(nameof(contentKindsRepo));
        _songsRepo = songsRepo ?? throw new ArgumentNullException(nameof(songsRepo));
        _songRecRepo = songRecRepo ?? throw new ArgumentNullException(nameof(songRecRepo));
        _bgmCuesRepo = bgmCuesRepo ?? throw new ArgumentNullException(nameof(bgmCuesRepo));
        _songSizeVariantsRepo = songSizeVariantsRepo ?? throw new ArgumentNullException(nameof(songSizeVariantsRepo));
        _songPartVariantsRepo = songPartVariantsRepo ?? throw new ArgumentNullException(nameof(songPartVariantsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();
        SetupGridColumns();

        Load += async (_, __) => await InitAsync();

        gridDiscs.SelectionChanged += async (_, __) => await OnDiscSelectedAsync();
        gridTracks.SelectionChanged += async (_, __) => await OnTrackSelectedAsync();

        btnSearch.Click += (_, __) => ApplyDiscFilter();
        btnReload.Click += async (_, __) => await ReloadDiscsAsync();
        txtSearchDiscs.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyDiscFilter(); } };

        btnTrackNew.Click += (_, __) => ClearTrackForm();
        btnTrackSave.Click += async (_, __) => await SaveTrackAsync();
        btnTrackDelete.Click += async (_, __) => await DeleteTrackAsync();

        cboContentKind.SelectedIndexChanged += (_, __) => UpdateContentKindPanelVisibility();

        // SONG 候補検索：入力 → デバウンス → DB 問合せ → リストボックス更新
        txtSongSearch.TextChanged += (_, __) => { _songSearchDebounce.Stop(); _songSearchDebounce.Start(); };
        _songSearchDebounce.Tick += async (_, __) => { _songSearchDebounce.Stop(); await RunSongSearchAsync(); };
        lstSongCandidates.SelectedIndexChanged += async (_, __) => await OnSongCandidateSelectedAsync();

        // BGM 候補検索
        txtBgmSearch.TextChanged += (_, __) => { _bgmSearchDebounce.Stop(); _bgmSearchDebounce.Start(); };
        chkBgmIncludeTemp.CheckedChanged += (_, __) => { _bgmSearchDebounce.Stop(); _bgmSearchDebounce.Start(); };
        cboBgmSeries.SelectedIndexChanged += (_, __) => { _bgmSearchDebounce.Stop(); _bgmSearchDebounce.Start(); };
        _bgmSearchDebounce.Tick += async (_, __) => { _bgmSearchDebounce.Stop(); await RunBgmSearchAsync(); };
        lstBgmCandidates.SelectedIndexChanged += (_, __) => OnBgmCandidateSelected();
    }

    // =========================================================================
    // 列定義
    // =========================================================================

    private void SetupGridColumns()
    {
        gridDiscs.Columns.Clear();
        gridDiscs.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "CatalogNo", HeaderText = "品番",
                DataPropertyName = nameof(Disc.CatalogNo), Width = 110 },
            new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "タイトル",
                DataPropertyName = nameof(Disc.Title),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "MediaFormat", HeaderText = "メディア",
                DataPropertyName = nameof(Disc.MediaFormat), Width = 70 },
            new DataGridViewTextBoxColumn { Name = "DiscNoInSet", HeaderText = "組内",
                DataPropertyName = nameof(Disc.DiscNoInSet), Width = 50,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } }
        });

        gridTracks.Columns.Clear();
        gridTracks.Columns.AddRange(new DataGridViewColumn[]
        {
            new DataGridViewTextBoxColumn { Name = "TrackNo", HeaderText = "#",
                DataPropertyName = nameof(Track.TrackNo), Width = 50,
                DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } },
            new DataGridViewTextBoxColumn { Name = "ContentKind", HeaderText = "種別",
                DataPropertyName = nameof(Track.ContentKindCode), Width = 70 },
            new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "タイトル上書き",
                DataPropertyName = nameof(Track.TrackTitleOverride),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill },
            new DataGridViewTextBoxColumn { Name = "SongRecId", HeaderText = "SongRecId",
                DataPropertyName = nameof(Track.SongRecordingId), Width = 80 },
            new DataGridViewTextBoxColumn { Name = "BgmMNo", HeaderText = "BGM M番号",
                DataPropertyName = nameof(Track.BgmMNoDetail), Width = 100 }
        });
    }

    // =========================================================================
    // 初期化・ロード
    // =========================================================================

    private async Task InitAsync()
    {
        try
        {
            // トラック内容種別コンボ（コードではなく日本語を見せる）
            var contentKinds = (await _contentKindsRepo.GetAllAsync()).ToList();
            cboContentKind.DisplayMember = nameof(TrackContentKind.NameJa);
            cboContentKind.ValueMember = nameof(TrackContentKind.KindCode);
            cboContentKind.DataSource = contentKinds;

            // サイズ／パート種別コンボ（先頭に「(未設定)」を追加）
            var sizes = (await _songSizeVariantsRepo.GetAllAsync()).ToList();
            cboSongSize.DisplayMember = nameof(CodeItem.Label);
            cboSongSize.ValueMember = nameof(CodeItem.Code);
            cboSongSize.DataSource = BuildCodeItems(sizes.Select(v => new CodeItem(v.VariantCode, v.NameJa)));

            var parts = (await _songPartVariantsRepo.GetAllAsync()).ToList();
            cboSongPart.DisplayMember = nameof(CodeItem.Label);
            cboSongPart.ValueMember = nameof(CodeItem.Code);
            cboSongPart.DataSource = BuildCodeItems(parts.Select(v => new CodeItem(v.VariantCode, v.NameJa)));

            // BGM シリーズ（候補検索のシリーズ絞り込み）
            var series = (await _seriesRepo.GetAllAsync()).ToList();
            var bgmSeriesItems = new List<CodeItemInt> { new CodeItemInt(-1, "(シリーズ未選択)") };
            foreach (var s in series) bgmSeriesItems.Add(new CodeItemInt(s.SeriesId, $"[{s.SeriesId}] {s.TitleShort ?? s.Title}"));
            cboBgmSeries.DisplayMember = nameof(CodeItemInt.Label);
            cboBgmSeries.ValueMember = nameof(CodeItemInt.Id);
            cboBgmSeries.DataSource = bgmSeriesItems;

            await ReloadDiscsAsync();
            UpdateContentKindPanelVisibility();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task ReloadDiscsAsync()
    {
        try
        {
            // 並びは商品発売日昇順・同日は品番昇順（UX: 時系列で入力していく運用）
            _discs = (await _discsRepo.GetByProductReleaseOrderAsync()).ToList();
            ApplyDiscFilter();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ApplyDiscFilter()
    {
        string kw = txtSearchDiscs.Text.Trim();
        IEnumerable<Disc> q = _discs;
        if (!string.IsNullOrEmpty(kw))
        {
            q = q.Where(d =>
                (d.CatalogNo?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (d.Title?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false));
        }
        gridDiscs.DataSource = null;
        gridDiscs.DataSource = q.ToList();
    }

    // =========================================================================
    // ディスク選択
    // =========================================================================

    private async Task OnDiscSelectedAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc d)
        {
            _tracks.Clear();
            gridTracks.DataSource = null;
            lblTracks.Text = "トラック一覧（ディスクを選択してください）";
            ClearTrackForm();
            return;
        }
        try
        {
            _tracks = (await _tracksRepo.GetByCatalogNoAsync(d.CatalogNo))
                .Where(x => x.SubOrder == 0)
                .ToList();
            gridTracks.DataSource = null;
            gridTracks.DataSource = _tracks;
            lblTracks.Text = $"トラック一覧 - {d.CatalogNo} {d.Title} （{_tracks.Count} 行）";
            ClearTrackForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // トラック選択・フォームバインド
    // =========================================================================

    private async Task OnTrackSelectedAsync()
    {
        if (gridTracks.CurrentRow?.DataBoundItem is not Track t)
        {
            ClearTrackForm();
            return;
        }
        await BindTrackToFormAsync(t);
    }

    private async Task BindTrackToFormAsync(Track t)
    {
        numTrackNo.Value = Math.Min(numTrackNo.Maximum, Math.Max(numTrackNo.Minimum, t.TrackNo));
        cboContentKind.SelectedValue = t.ContentKindCode ?? "OTHER";
        txtTrackTitleOverride.Text = t.TrackTitleOverride ?? "";
        numStartLba.Value = t.StartLba ?? 0;
        numLengthFrames.Value = t.LengthFrames ?? 0;
        txtIsrc.Text = t.Isrc ?? "";
        txtCdTextTitle.Text = t.CdTextTitle ?? "";
        txtCdTextPerformer.Text = t.CdTextPerformer ?? "";
        chkIsData.Checked = t.IsDataTrack;
        chkPreEmphasis.Checked = t.HasPreEmphasis;
        chkCopyOk.Checked = t.IsCopyPermitted;
        txtTrackNotes.Text = t.Notes ?? "";

        // SONG: 既存 SongRecordingId から親曲を引いて候補リストに載せ、そのまま選択状態にする
        _selectedSong = null;
        lstSongCandidates.DataSource = null;
        if (t.SongRecordingId is int srId && srId > 0)
        {
            var rec = await _songRecRepo.GetByIdAsync(srId);
            if (rec is not null)
            {
                var song = await _songsRepo.GetByIdAsync(rec.SongId);
                if (song is not null)
                {
                    _selectedSong = song;
                    lblSongSelected.Text = $"曲: {song.Title} (#{song.SongId})";
                    await ReloadSongRecordingsAsync(song.SongId);
                    cboSongRecording.SelectedValue = srId;
                }
            }
        }
        else
        {
            lblSongSelected.Text = "(曲未選択)";
            cboSongRecording.DataSource = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
        }

        // サイズ／パート
        SelectCodeItem(cboSongSize, t.SongSizeVariantCode);
        SelectCodeItem(cboSongPart, t.SongPartVariantCode);

        // BGM: 既存 (series_id, m_no_detail) から cue を引いて選択状態にする
        _selectedBgmCue = null;
        lstBgmCandidates.DataSource = null;
        if (t.BgmSeriesId is int bsId && !string.IsNullOrEmpty(t.BgmMNoDetail))
        {
            var cue = await _bgmCuesRepo.GetAsync(bsId, t.BgmMNoDetail!);
            if (cue is not null)
            {
                _selectedBgmCue = cue;
                // シリーズコンボにセット（存在しない場合は無視）
                if (cboBgmSeries.Items.Cast<CodeItemInt>().Any(i => i.Id == bsId))
                {
                    cboBgmSeries.SelectedValue = bsId;
                }
                lblBgmSelected.Text = FormatBgmLabel(cue);
            }
        }
        else
        {
            lblBgmSelected.Text = "(劇伴未選択)";
        }

        UpdateContentKindPanelVisibility();
    }

    private void ClearTrackForm()
    {
        numTrackNo.Value = 0;
        if (cboContentKind.Items.Count > 0) cboContentKind.SelectedIndex = 0;
        txtTrackTitleOverride.Text = "";
        numStartLba.Value = 0;
        numLengthFrames.Value = 0;
        txtIsrc.Text = "";
        txtCdTextTitle.Text = "";
        txtCdTextPerformer.Text = "";
        chkIsData.Checked = false;
        chkPreEmphasis.Checked = false;
        chkCopyOk.Checked = false;
        txtTrackNotes.Text = "";

        _selectedSong = null;
        _selectedBgmCue = null;
        txtSongSearch.Text = "";
        lstSongCandidates.DataSource = null;
        lblSongSelected.Text = "(曲未選択)";
        cboSongRecording.DataSource = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
        if (cboSongSize.Items.Count > 0) cboSongSize.SelectedIndex = 0;
        if (cboSongPart.Items.Count > 0) cboSongPart.SelectedIndex = 0;

        txtBgmSearch.Text = "";
        lstBgmCandidates.DataSource = null;
        lblBgmSelected.Text = "(劇伴未選択)";

        UpdateContentKindPanelVisibility();
    }

    /// <summary>内容種別に応じて SONG / BGM サブパネルの可視性を切り替える。</summary>
    private void UpdateContentKindPanelVisibility()
    {
        string code = cboContentKind.SelectedValue?.ToString() ?? "OTHER";
        pnlSongLink.Visible = (code == "SONG");
        pnlBgmLink.Visible = (code == "BGM");
        // タイトル上書きは全種別で使える
    }

    // =========================================================================
    // SONG 候補検索
    // =========================================================================

    private async Task RunSongSearchAsync()
    {
        string kw = txtSongSearch.Text.Trim();
        if (kw.Length < 2)
        {
            lstSongCandidates.DataSource = null;
            return;
        }
        _songSearchCts?.Cancel();
        _songSearchCts = new CancellationTokenSource();
        var ct = _songSearchCts.Token;
        try
        {
            var hits = await _songsRepo.SearchAsync(kw, 100, ct);
            if (ct.IsCancellationRequested) return;
            // 表示名は曲名・作詞・作曲・編曲を簡潔に並べる
            var items = hits.Select(s => new SongCandidateItem(s)).ToList();
            lstSongCandidates.DisplayMember = nameof(SongCandidateItem.Label);
            lstSongCandidates.DataSource = items;
        }
        catch (OperationCanceledException) { /* 最新入力に置き換わった */ }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>曲候補を選択 → 歌唱者バージョンコンボを更新する。</summary>
    private async Task OnSongCandidateSelectedAsync()
    {
        if (lstSongCandidates.SelectedItem is not SongCandidateItem item) return;
        _selectedSong = item.Song;
        lblSongSelected.Text = $"曲: {item.Song.Title} (#{item.Song.SongId})";
        await ReloadSongRecordingsAsync(item.Song.SongId);
    }

    private async Task ReloadSongRecordingsAsync(int songId)
    {
        try
        {
            var recs = await _songRecRepo.GetBySongIdAsync(songId);
            var items = new List<CodeItemInt> { new CodeItemInt(0, "(未設定)") };
            foreach (var r in recs)
            {
                string label = $"#{r.SongRecordingId} {r.SingerName ?? "(歌唱者未設定)"} {r.VariantLabel ?? ""}".Trim();
                items.Add(new CodeItemInt(r.SongRecordingId, label));
            }
            cboSongRecording.DisplayMember = nameof(CodeItemInt.Label);
            cboSongRecording.ValueMember = nameof(CodeItemInt.Id);
            cboSongRecording.DataSource = items;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // BGM 候補検索
    // =========================================================================

    private async Task RunBgmSearchAsync()
    {
        string kw = txtBgmSearch.Text.Trim();
        if (kw.Length < 2)
        {
            lstBgmCandidates.DataSource = null;
            return;
        }
        _bgmSearchCts?.Cancel();
        _bgmSearchCts = new CancellationTokenSource();
        var ct = _bgmSearchCts.Token;
        bool includeTemp = chkBgmIncludeTemp.Checked;

        try
        {
            IReadOnlyList<BgmCue> hits;
            int? seriesId = (cboBgmSeries.SelectedValue is int id && id > 0) ? id : null;
            if (seriesId.HasValue)
            {
                hits = await _bgmCuesRepo.SearchInSeriesAsync(seriesId.Value, kw, includeTemp, 100, ct);
            }
            else
            {
                // シリーズ未指定時は全シリーズ横断（件数制限付き）
                hits = await _bgmCuesRepo.SearchAllSeriesAsync(kw, includeTemp, 100, ct);
            }
            if (ct.IsCancellationRequested) return;
            var items = hits.Select(c => new BgmCandidateItem(c)).ToList();
            lstBgmCandidates.DisplayMember = nameof(BgmCandidateItem.Label);
            lstBgmCandidates.DataSource = items;
        }
        catch (OperationCanceledException) { /* 最新入力に置き換わった */ }
        catch (Exception ex) { ShowError(ex); }
    }

    private void OnBgmCandidateSelected()
    {
        if (lstBgmCandidates.SelectedItem is not BgmCandidateItem item) return;
        _selectedBgmCue = item.Cue;
        lblBgmSelected.Text = FormatBgmLabel(item.Cue);
        // シリーズコンボも選んだキューのシリーズに追随させる（UX: 次候補を絞り込みやすくするため）
        if (cboBgmSeries.Items.Cast<CodeItemInt>().Any(i => i.Id == item.Cue.SeriesId))
        {
            cboBgmSeries.SelectedValue = item.Cue.SeriesId;
        }
    }

    private static string FormatBgmLabel(BgmCue c)
    {
        // 仮番号はマスタメンテとは別文脈だが、トラック登録画面でも素の _temp_... を見せると分かりづらい。
        // 候補表示では「(番号不明)」と置換し、括弧でメニュー名を添えて識別できるようにする。
        string mno = c.IsTempMNo ? "(番号不明)" : c.MNoDetail;
        string menu = string.IsNullOrEmpty(c.MenuTitle) ? "" : $" [{c.MenuTitle}]";
        return $"series={c.SeriesId} {mno}{menu}";
    }

    // =========================================================================
    // 保存・削除
    // =========================================================================

    private async Task SaveTrackAsync()
    {
        if (gridDiscs.CurrentRow?.DataBoundItem is not Disc parent)
        {
            MessageBox.Show(this, "先に親となるディスクを選択してください。"); return;
        }
        try
        {
            string contentKind = cboContentKind.SelectedValue?.ToString() ?? "OTHER";

            int? songRecId = null;
            int? bgmSeriesId = null;
            string? bgmMNoDetail = null;
            string? songSizeCode = null;
            string? songPartCode = null;

            if (contentKind == "SONG")
            {
                if (cboSongRecording.SelectedValue is int sid && sid > 0) songRecId = sid;
                if (songRecId is null)
                {
                    MessageBox.Show(this, "SONG トラックには曲録音を選択してください。"); return;
                }
                if (cboSongSize.SelectedItem is CodeItem sv && !string.IsNullOrEmpty(sv.Code)) songSizeCode = sv.Code;
                if (cboSongPart.SelectedItem is CodeItem pv && !string.IsNullOrEmpty(pv.Code)) songPartCode = pv.Code;
            }
            else if (contentKind == "BGM")
            {
                if (_selectedBgmCue is null)
                {
                    MessageBox.Show(this, "BGM トラックには劇伴 cue を選択してください。"); return;
                }
                bgmSeriesId = _selectedBgmCue.SeriesId;
                bgmMNoDetail = _selectedBgmCue.MNoDetail;
            }

            var t = new Track
            {
                CatalogNo = parent.CatalogNo,
                TrackNo = (byte)numTrackNo.Value,
                SubOrder = 0,
                ContentKindCode = contentKind,
                SongRecordingId = songRecId,
                SongSizeVariantCode = songSizeCode,
                SongPartVariantCode = songPartCode,
                BgmSeriesId = bgmSeriesId,
                BgmMNoDetail = bgmMNoDetail,
                TrackTitleOverride = NullIfEmpty(txtTrackTitleOverride.Text),
                StartLba = numStartLba.Value == 0 ? null : (uint)numStartLba.Value,
                LengthFrames = numLengthFrames.Value == 0 ? null : (uint)numLengthFrames.Value,
                Isrc = NullIfEmpty(txtIsrc.Text),
                CdTextTitle = NullIfEmpty(txtCdTextTitle.Text),
                CdTextPerformer = NullIfEmpty(txtCdTextPerformer.Text),
                IsDataTrack = chkIsData.Checked,
                HasPreEmphasis = chkPreEmphasis.Checked,
                IsCopyPermitted = chkCopyOk.Checked,
                Notes = NullIfEmpty(txtTrackNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (t.TrackNo == 0) { MessageBox.Show(this, "トラック番号を 1 以上で指定してください。"); return; }

            await _tracksRepo.UpsertAsync(t);

            _tracks = (await _tracksRepo.GetByCatalogNoAsync(parent.CatalogNo))
                .Where(x => x.SubOrder == 0)
                .ToList();
            gridTracks.DataSource = null;
            gridTracks.DataSource = _tracks;
            MessageBox.Show(this, $"トラック #{t.TrackNo} を保存しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteTrackAsync()
    {
        if (gridTracks.CurrentRow?.DataBoundItem is not Track t) return;
        if (Confirm($"トラック #{t.TrackNo} を削除しますか？") != DialogResult.Yes) return;
        try
        {
            await _tracksRepo.DeleteAllSubOrdersAsync(t.CatalogNo, t.TrackNo);
            if (gridDiscs.CurrentRow?.DataBoundItem is Disc parent)
            {
                _tracks = (await _tracksRepo.GetByCatalogNoAsync(parent.CatalogNo))
                    .Where(x => x.SubOrder == 0)
                    .ToList();
                gridTracks.DataSource = null;
                gridTracks.DataSource = _tracks;
            }
            ClearTrackForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // =========================================================================
    // ヘルパ
    // =========================================================================

    private static List<CodeItem> BuildCodeItems(IEnumerable<CodeItem> items)
    {
        var result = new List<CodeItem> { new CodeItem("", "(未設定)") };
        result.AddRange(items);
        return result;
    }

    private static void SelectCodeItem(ComboBox cbo, string? code)
    {
        string target = code ?? "";
        foreach (var obj in cbo.Items)
        {
            if (obj is CodeItem ci && ci.Code == target)
            {
                cbo.SelectedItem = ci;
                return;
            }
        }
        if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>文字列コードのコンボ項目（サイズ／パート種別用）。</summary>
    private sealed record CodeItem(string Code, string Label);

    /// <summary>int コードのコンボ項目（SongRecording / SeriesId 用）。</summary>
    private sealed record CodeItemInt(int Id, string Label);

    /// <summary>SONG 候補リストボックス項目。Song モデルを保持してハイライト表示文字列を組み立てる。</summary>
    private sealed class SongCandidateItem
    {
        public Song Song { get; }
        public string Label { get; }
        public SongCandidateItem(Song s)
        {
            Song = s;
            // 例: "DANZEN! ふたりはプリキュア / 青木久美子 / 小杉保夫 (#1)"
            string creator = string.Join(" / ", new[] { s.LyricistName, s.ComposerName, s.ArrangerName }
                .Where(x => !string.IsNullOrEmpty(x)));
            Label = string.IsNullOrEmpty(creator)
                ? $"{s.Title} (#{s.SongId})"
                : $"{s.Title}  /  {creator}  (#{s.SongId})";
        }
    }

    /// <summary>BGM 候補リストボックス項目。</summary>
    private sealed class BgmCandidateItem
    {
        public BgmCue Cue { get; }
        public string Label { get; }
        public BgmCandidateItem(BgmCue c)
        {
            Cue = c;
            string mno = c.IsTempMNo ? "[仮]" : c.MNoDetail;
            string menu = string.IsNullOrEmpty(c.MenuTitle) ? "" : $" - {c.MenuTitle}";
            string composer = string.IsNullOrEmpty(c.ComposerName) ? "" : $" / {c.ComposerName}";
            Label = $"series={c.SeriesId} {mno}{menu}{composer}";
        }
    }
}