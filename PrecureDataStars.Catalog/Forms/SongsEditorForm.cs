using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 歌マスタ管理エディタ。
/// <para>
/// 左上: 曲一覧（ID 昇順 + 検索）、右上: 曲詳細入力、下: 選択中曲の歌唱者バージョン一覧と詳細、
/// さらに右下: 選択中バージョンの収録ディスク・トラック一覧。
/// 歌の二階層構造（曲マスタ <see cref="Song"/> = メロディ + アレンジ単位、歌唱者バージョン <see cref="SongRecording"/>）
/// を 1 画面で扱えるようにする。
/// </para>
/// </summary>
public partial class SongsEditorForm : Form
{
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly SongMusicClassesRepository _musicClassesRepo;
    private readonly SeriesRepository _seriesRepo;

    private List<Song> _allSongs = new();       // 再検索用に全件キャッシュ（シリーズ・フィルタ変更時に再利用）
    private List<Song> _songs = new();          // グリッドに表示中
    private List<SongRecording> _recordings = new();
    private List<SongRecordingTrackRef> _trackRefs = new();

    public SongsEditorForm(
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        TracksRepository tracksRepo,
        SongMusicClassesRepository musicClassesRepo,
        SeriesRepository seriesRepo)
    {
        _songsRepo = songsRepo;
        _songRecRepo = songRecRepo;
        _tracksRepo = tracksRepo;
        _musicClassesRepo = musicClassesRepo;
        _seriesRepo = seriesRepo;

        InitializeComponent();
        Load += async (_, __) => await InitAsync();

        gridSongs.SelectionChanged += async (_, __) => await OnSongSelectedAsync();
        gridRecordings.SelectionChanged += async (_, __) => await OnRecordingSelectedAsync();

        btnSongNew.Click += (_, __) => ClearSongForm();
        btnSongSave.Click += async (_, __) => await SaveSongAsync();
        btnSongDelete.Click += async (_, __) => await DeleteSongAsync();

        btnRecNew.Click += (_, __) => ClearRecordingForm();
        btnRecSave.Click += async (_, __) => await SaveRecordingAsync();
        btnRecDelete.Click += async (_, __) => await DeleteRecordingAsync();

        // 検索・フィルタ
        btnSearch.Click += (_, __) => ApplyFilter();
        txtSearch.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyFilter(); } };
        cboSeriesFilter.SelectedIndexChanged += (_, __) => ApplyFilter();
        cboMusicClassFilter.SelectedIndexChanged += (_, __) => ApplyFilter();
    }

    /// <summary>初期化：マスタとシリーズを読み込み、コンボにバインド、曲一覧をロード。</summary>
    private async Task InitAsync()
    {
        try
        {
            // 音楽種別コンボ（「(指定なし)」を先頭）
            var musicClasses = (await _musicClassesRepo.GetAllAsync()).ToList();
            cboMusicClass.DisplayMember = "NameJa";
            cboMusicClass.ValueMember = "ClassCode";
            cboMusicClass.DataSource = PrependNone(musicClasses.Select(x => new CodeItem(x.ClassCode, x.NameJa)).ToList());

            // 音楽種別フィルタコンボ（フィルタ側は独立インスタンス）
            cboMusicClassFilter.DisplayMember = "NameJa";
            cboMusicClassFilter.ValueMember = "ClassCode";
            cboMusicClassFilter.DataSource = PrependNone(musicClasses.Select(x => new CodeItem(x.ClassCode, x.NameJa)).ToList());

            // シリーズコンボ（編集側）
            var series = (await _seriesRepo.GetAllAsync()).ToList();
            var seriesItems = new List<SeriesItem> { new SeriesItem(null, "(指定なし)") };
            foreach (var s in series) seriesItems.Add(new SeriesItem(s.SeriesId, s.Title));
            cboSeries.DisplayMember = "Label";
            cboSeries.ValueMember = "Id";
            cboSeries.DataSource = seriesItems;

            // シリーズフィルタコンボ（独立インスタンス）
            var seriesFilterItems = new List<SeriesItem> { new SeriesItem(null, "(全て)") };
            foreach (var s in series) seriesFilterItems.Add(new SeriesItem(s.SeriesId, s.Title));
            cboSeriesFilter.DisplayMember = "Label";
            cboSeriesFilter.ValueMember = "Id";
            cboSeriesFilter.DataSource = seriesFilterItems;

            await ReloadSongsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>曲一覧を再取得（全件）してフィルタを適用。</summary>
    private async Task ReloadSongsAsync()
    {
        _allSongs = (await _songsRepo.GetAllAsync()).ToList();
        ApplyFilter();
    }

    /// <summary>
    /// 現在の検索・フィルタ条件を _allSongs に適用して gridSongs に反映する。
    /// 条件: タイトル／かな／歌唱者名の部分一致、シリーズ、音楽種別。
    /// </summary>
    private void ApplyFilter()
    {
        string keyword = txtSearch.Text.Trim();
        int? filterSeriesId = cboSeriesFilter.SelectedValue as int?;
        string? filterMusicClass = SelectedCode(cboMusicClassFilter);

        IEnumerable<Song> q = _allSongs;
        if (filterSeriesId.HasValue) q = q.Where(s => s.SeriesId == filterSeriesId.Value);
        if (!string.IsNullOrEmpty(filterMusicClass)) q = q.Where(s => s.MusicClassCode == filterMusicClass);
        if (!string.IsNullOrEmpty(keyword))
        {
            // タイトル/かな 部分一致。大文字小文字を無視する（StringComparison.OrdinalIgnoreCase）。
            // 歌唱者名での絞り込みはここでは行わない（曲一覧側には singer_name がないため）。
            q = q.Where(s =>
                (!string.IsNullOrEmpty(s.Title) && s.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.TitleKana) && s.TitleKana.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        }

        _songs = q.OrderBy(s => s.SongId).ToList();
        gridSongs.DataSource = null;
        gridSongs.DataSource = _songs;
        HideMetaColumns(gridSongs);
    }

    // ===== 曲（songs）側 =====

    private async Task OnSongSelectedAsync()
    {
        if (gridSongs.CurrentRow?.DataBoundItem is not Song s)
        {
            ClearSongForm();
            _recordings.Clear();
            gridRecordings.DataSource = null;
            _trackRefs.Clear();
            gridRecTracks.DataSource = null;
            ClearRecordingForm();
            return;
        }
        BindSongToForm(s);
        try
        {
            _recordings = (await _songRecRepo.GetBySongIdAsync(s.SongId)).ToList();
            gridRecordings.DataSource = null;
            gridRecordings.DataSource = _recordings;
            HideMetaColumns(gridRecordings);
            ClearRecordingForm();

            // 録音が選択されていない状態では tracks 一覧はクリア
            _trackRefs.Clear();
            gridRecTracks.DataSource = null;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void BindSongToForm(Song s)
    {
        numSongId.Value = s.SongId;
        txtTitle.Text = s.Title;
        txtTitleKana.Text = s.TitleKana ?? "";
        cboMusicClass.SelectedValue = s.MusicClassCode ?? "";
        cboSeries.SelectedValue = (object?)s.SeriesId ?? DBNull.Value;
        txtLyricist.Text = s.OriginalLyricistName ?? "";
        txtLyricistKana.Text = s.OriginalLyricistNameKana ?? "";
        txtComposer.Text = s.OriginalComposerName ?? "";
        txtComposerKana.Text = s.OriginalComposerNameKana ?? "";
        txtArranger.Text = s.ArrangerName ?? "";
        txtArrangerKana.Text = s.ArrangerNameKana ?? "";
        txtSongNotes.Text = s.Notes ?? "";
    }

    private void ClearSongForm()
    {
        numSongId.Value = 0;
        txtTitle.Text = "";
        txtTitleKana.Text = "";
        if (cboMusicClass.Items.Count > 0) cboMusicClass.SelectedIndex = 0;
        if (cboSeries.Items.Count > 0) cboSeries.SelectedIndex = 0;
        txtLyricist.Text = ""; txtLyricistKana.Text = "";
        txtComposer.Text = ""; txtComposerKana.Text = "";
        txtArranger.Text = ""; txtArrangerKana.Text = "";
        txtSongNotes.Text = "";
    }

    private async Task SaveSongAsync()
    {
        try
        {
            var s = new Song
            {
                SongId = (int)numSongId.Value,
                Title = txtTitle.Text.Trim(),
                TitleKana = NullIfEmpty(txtTitleKana.Text),
                MusicClassCode = SelectedCode(cboMusicClass),
                SeriesId = cboSeries.SelectedValue is int sid && sid > 0 ? sid : null,
                OriginalLyricistName = NullIfEmpty(txtLyricist.Text),
                OriginalLyricistNameKana = NullIfEmpty(txtLyricistKana.Text),
                OriginalComposerName = NullIfEmpty(txtComposer.Text),
                OriginalComposerNameKana = NullIfEmpty(txtComposerKana.Text),
                ArrangerName = NullIfEmpty(txtArranger.Text),
                ArrangerNameKana = NullIfEmpty(txtArrangerKana.Text),
                Notes = NullIfEmpty(txtSongNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrWhiteSpace(s.Title)) { MessageBox.Show(this, "タイトルは必須です。"); return; }
            if (s.SongId == 0)
            {
                int newId = await _songsRepo.InsertAsync(s);
                MessageBox.Show(this, $"新規曲を登録しました (ID={newId})。");
            }
            else
            {
                await _songsRepo.UpdateAsync(s);
                MessageBox.Show(this, $"曲 (ID={s.SongId}) を更新しました。");
            }
            await ReloadSongsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteSongAsync()
    {
        if (gridSongs.CurrentRow?.DataBoundItem is not Song s) return;
        if (Confirm($"曲 [{s.Title}] を削除しますか？歌唱者バージョンは CASCADE で連鎖削除されます。") != DialogResult.Yes) return;
        try
        {
            await _songsRepo.SoftDeleteAsync(s.SongId, Environment.UserName);
            await ReloadSongsAsync();
            ClearSongForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== 録音（song_recordings）側 =====

    private async Task OnRecordingSelectedAsync()
    {
        if (gridRecordings.CurrentRow?.DataBoundItem is not SongRecording r)
        {
            ClearRecordingForm();
            _trackRefs.Clear();
            gridRecTracks.DataSource = null;
            return;
        }
        BindRecordingToForm(r);

        try
        {
            // この歌唱者バージョンが収録されているディスク・トラックを引く
            _trackRefs = (await _tracksRepo.GetTracksBySongRecordingAsync(r.SongRecordingId)).ToList();
            gridRecTracks.DataSource = null;
            gridRecTracks.DataSource = _trackRefs;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void BindRecordingToForm(SongRecording r)
    {
        numRecId.Value = r.SongRecordingId;
        txtSinger.Text = r.SingerName ?? "";
        txtSingerKana.Text = r.SingerNameKana ?? "";
        txtVariantLabel.Text = r.VariantLabel ?? "";
        txtRecNotes.Text = r.Notes ?? "";
    }

    private void ClearRecordingForm()
    {
        numRecId.Value = 0;
        txtSinger.Text = ""; txtSingerKana.Text = "";
        txtVariantLabel.Text = "";
        txtRecNotes.Text = "";
    }

    private async Task SaveRecordingAsync()
    {
        if (gridSongs.CurrentRow?.DataBoundItem is not Song parent)
        {
            MessageBox.Show(this, "先に親となる曲を選択してください。"); return;
        }
        try
        {
            var r = new SongRecording
            {
                SongRecordingId = (int)numRecId.Value,
                SongId = parent.SongId,
                SingerName = NullIfEmpty(txtSinger.Text),
                SingerNameKana = NullIfEmpty(txtSingerKana.Text),
                VariantLabel = NullIfEmpty(txtVariantLabel.Text),
                Notes = NullIfEmpty(txtRecNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (r.SongRecordingId == 0)
            {
                int newId = await _songRecRepo.InsertAsync(r);
                MessageBox.Show(this, $"新規歌唱者バージョンを登録しました (ID={newId})。");
            }
            else
            {
                await _songRecRepo.UpdateAsync(r);
                MessageBox.Show(this, $"歌唱者バージョン (ID={r.SongRecordingId}) を更新しました。");
            }
            _recordings = (await _songRecRepo.GetBySongIdAsync(parent.SongId)).ToList();
            gridRecordings.DataSource = null;
            gridRecordings.DataSource = _recordings;
            HideMetaColumns(gridRecordings);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteRecordingAsync()
    {
        if (gridRecordings.CurrentRow?.DataBoundItem is not SongRecording r) return;
        if (Confirm($"歌唱者バージョン (ID={r.SongRecordingId}) を削除しますか？") != DialogResult.Yes) return;
        try
        {
            await _songRecRepo.SoftDeleteAsync(r.SongRecordingId, Environment.UserName);
            if (gridSongs.CurrentRow?.DataBoundItem is Song parent)
            {
                _recordings = (await _songRecRepo.GetBySongIdAsync(parent.SongId)).ToList();
                gridRecordings.DataSource = null;
                gridRecordings.DataSource = _recordings;
                HideMetaColumns(gridRecordings);
            }
            ClearRecordingForm();
            _trackRefs.Clear();
            gridRecTracks.DataSource = null;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== ヘルパ =====

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? SelectedCode(ComboBox cbo)
    {
        var v = cbo.SelectedValue?.ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    /// <summary>コンボボックス先頭に「(指定なし)」を追加。</summary>
    private static List<CodeItem> PrependNone(List<CodeItem> items)
    {
        var result = new List<CodeItem> { new CodeItem("", "(指定なし)") };
        result.AddRange(items);
        return result;
    }

    /// <summary>DataGridView から監査・削除・長文メモ系の列を非表示にする。</summary>
    private static void HideMetaColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn c in grid.Columns)
        {
            if (c.Name is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy" or "IsDeleted" or "Notes")
                c.Visible = false;
        }
    }

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>コード系コンボ表示用。</summary>
    public sealed record CodeItem(string Code, string Label)
    {
        public string NameJa => Label;
        public string ClassCode => Code;
    }

    /// <summary>シリーズコンボ表示用。</summary>
    private sealed record SeriesItem(int? Id, string Label);
}
