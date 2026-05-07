using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Common.CsvImport;
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

    // v1.2.3 追加：構造化クレジット用リポジトリ群（4 本）と picker 用リポジトリ（2 本）
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;

    private List<Song> _allSongs = new();       // 再検索用に全件キャッシュ（シリーズ・フィルタ変更時に再利用）
    private List<Song> _songs = new();          // グリッドに表示中
    private List<SongRecording> _recordings = new();
    private List<SongRecordingTrackRef> _trackRefs = new();

    public SongsEditorForm(
        SongsRepository songsRepo,
        SongRecordingsRepository songRecRepo,
        TracksRepository tracksRepo,
        SongMusicClassesRepository musicClassesRepo,
        SeriesRepository seriesRepo,
        // v1.2.3 追加：構造化クレジット用
        PersonAliasesRepository personAliasesRepo,
        SongCreditsRepository songCreditsRepo,
        SongRecordingSingersRepository songRecordingSingersRepo,
        CharacterAliasesRepository characterAliasesRepo)
    {
        _songsRepo = songsRepo;
        _songRecRepo = songRecRepo;
        _tracksRepo = tracksRepo;
        _musicClassesRepo = musicClassesRepo;
        _seriesRepo = seriesRepo;

        // v1.2.3 追加分の保持
        _personAliasesRepo = personAliasesRepo ?? throw new ArgumentNullException(nameof(personAliasesRepo));
        _songCreditsRepo = songCreditsRepo ?? throw new ArgumentNullException(nameof(songCreditsRepo));
        _songRecordingSingersRepo = songRecordingSingersRepo ?? throw new ArgumentNullException(nameof(songRecordingSingersRepo));
        _characterAliasesRepo = characterAliasesRepo ?? throw new ArgumentNullException(nameof(characterAliasesRepo));

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

        // v1.1.3: CSV 取り込みボタン
        btnImportCsv.Click += async (_, __) => await ImportCsvAsync();

        // v1.2.3: 構造化クレジット編集ボタンのハンドラ
        btnEditStructLyricist.Click += async (_, __) => await OnEditSongCreditsAsync(SongCreditRole.Lyricist);
        btnEditStructComposer.Click += async (_, __) => await OnEditSongCreditsAsync(SongCreditRole.Composer);
        btnEditStructArranger.Click += async (_, __) => await OnEditSongCreditsAsync(SongCreditRole.Arranger);
        btnEditStructSingers.Click += async (_, __) => await OnEditSingersAsync();
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

            // v1.1.3: 作詞・作曲・編曲・歌手の各テキストボックスにオートコンプリート候補を設定する。
            // 既存マスタから抽出したユニーク値を CustomSource として与え、入力文字列に一致する候補を
            // サジェスト（SuggestAppend）する。大量件数でも UI は自身のキャッシュで判定するため重くない。
            await SetupAutoCompleteAsync();

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

            // v1.2.3：選択中の曲に紐付く構造化クレジットの連結表示を更新
            await RefreshSongCreditsLabelsAsync(s.SongId);
            // 録音未選択状態なので歌唱者ラベルは初期化（"(未設定)"）
            ApplyStructLabel(lblStructSingersValue, "");
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
        txtLyricist.Text = s.LyricistName ?? "";
        txtLyricistKana.Text = s.LyricistNameKana ?? "";
        txtComposer.Text = s.ComposerName ?? "";
        txtComposerKana.Text = s.ComposerNameKana ?? "";
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
        // v1.2.3: 構造化クレジット表示も初期化
        ApplyStructLabel(lblStructLyricistValue, "");
        ApplyStructLabel(lblStructComposerValue, "");
        ApplyStructLabel(lblStructArrangerValue, "");
        ApplyStructLabel(lblStructSingersValue, "");
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
                LyricistName = NullIfEmpty(txtLyricist.Text),
                LyricistNameKana = NullIfEmpty(txtLyricistKana.Text),
                ComposerName = NullIfEmpty(txtComposer.Text),
                ComposerNameKana = NullIfEmpty(txtComposerKana.Text),
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
            // v1.2.3：録音未選択時は歌唱者構造化ラベルもクリア
            ApplyStructLabel(lblStructSingersValue, "");
            return;
        }
        BindRecordingToForm(r);

        try
        {
            // この歌唱者バージョンが収録されているディスク・トラックを引く
            _trackRefs = (await _tracksRepo.GetTracksBySongRecordingAsync(r.SongRecordingId)).ToList();
            gridRecTracks.DataSource = null;
            gridRecTracks.DataSource = _trackRefs;

            // v1.2.3：選択録音の歌唱者構造化クレジットを連結表示
            await RefreshSingerLabelsAsync(r.SongRecordingId);
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
        // v1.2.3: 歌唱者構造化ラベルも初期化
        ApplyStructLabel(lblStructSingersValue, "");
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

    /// <summary>
    /// 作詞・作曲・編曲・歌手名テキストボックスにオートコンプリート候補を注入する（v1.1.3 追加）。
    /// 既存マスタからユニーク抽出した一覧を <see cref="AutoCompleteStringCollection"/> に詰め、
    /// <see cref="AutoCompleteMode.SuggestAppend"/> で打鍵ごとに候補をドロップダウン表示させる。
    /// </summary>
    private async Task SetupAutoCompleteAsync()
    {
        try
        {
            var creatorNames = await _songsRepo.GetCreatorNameCandidatesAsync();
            var singerNames = await _songRecRepo.GetSingerNameCandidatesAsync();

            var creatorSrc = new AutoCompleteStringCollection();
            foreach (var n in creatorNames) creatorSrc.Add(n);
            var singerSrc = new AutoCompleteStringCollection();
            foreach (var n in singerNames) singerSrc.Add(n);

            foreach (var box in new[] { txtLyricist, txtLyricistKana, txtComposer, txtComposerKana, txtArranger, txtArrangerKana })
            {
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.AutoCompleteCustomSource = creatorSrc;
            }

            foreach (var box in new[] { txtSinger, txtSingerKana })
            {
                box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                box.AutoCompleteSource = AutoCompleteSource.CustomSource;
                box.AutoCompleteCustomSource = singerSrc;
            }
        }
        catch (Exception ex)
        {
            // オートコンプリートは付加機能なので、取得失敗時は静かに無効化（ログ代わりの出力に留める）
            System.Diagnostics.Debug.WriteLine($"AutoComplete setup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 歌マスタ CSV 取り込みハンドラ（v1.1.3 追加）。
    /// ファイル選択ダイアログでファイルを選び、DRY-RUN プレビューで件数確認してから実行する 2 段階実行。
    /// </summary>
    private async Task ImportCsvAsync()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "歌マスタ CSV を選択",
            Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var svc = new SongCsvImportService(_songsRepo, _seriesRepo, _musicClassesRepo);
        try
        {
            // まず DRY-RUN：実際の書き込みはせず件数だけ集計して確認ダイアログを出す
            var preview = await svc.ImportAsync(ofd.FileName, Environment.UserName, dryRun: true);
            string warnSummary = preview.Warnings.Count == 0 ? "" : "\n\n警告:\n - " + string.Join("\n - ", preview.Warnings.Take(10));
            var ask = MessageBox.Show(this,
                $"取り込み結果（ドライラン）:\n" +
                $"  新規: {preview.Inserted} 件\n" +
                $"  更新: {preview.Updated} 件\n" +
                $"  スキップ: {preview.Skipped} 件\n" +
                $"  警告: {preview.Warnings.Count} 件" +
                warnSummary +
                "\n\nこの内容で実行しますか？",
                "CSV 取り込み確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ask != DialogResult.Yes) return;

            var applied = await svc.ImportAsync(ofd.FileName, Environment.UserName, dryRun: false);
            MessageBox.Show(this,
                $"取り込み完了:\n" +
                $"  新規: {applied.Inserted} 件\n" +
                $"  更新: {applied.Updated} 件\n" +
                $"  スキップ: {applied.Skipped} 件",
                "CSV 取り込み", MessageBoxButtons.OK, MessageBoxIcon.Information);

            await ReloadSongsAsync();
            await SetupAutoCompleteAsync();
        }
        catch (Exception ex) { ShowError(ex); }
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

    // ──────────────────────────────────────────────────────────────────
    //  v1.2.3 追加：構造化クレジット（song_credits / song_recording_singers）連携
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// 選択中曲の構造化クレジット（指定 role）の連結表示を更新する。
    /// 行が無い場合は「(未設定)」グレー表示、ある場合は連結文字列を黒で表示する。
    /// </summary>
    private async Task RefreshSongCreditsLabelsAsync(int songId)
    {
        try
        {
            string lyr = await _songCreditsRepo.GetDisplayStringAsync(songId, SongCreditRole.Lyricist);
            string cmp = await _songCreditsRepo.GetDisplayStringAsync(songId, SongCreditRole.Composer);
            string arr = await _songCreditsRepo.GetDisplayStringAsync(songId, SongCreditRole.Arranger);
            ApplyStructLabel(lblStructLyricistValue, lyr);
            ApplyStructLabel(lblStructComposerValue, cmp);
            ApplyStructLabel(lblStructArrangerValue, arr);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中録音の歌唱者構造化クレジットの連結表示を更新する。</summary>
    private async Task RefreshSingerLabelsAsync(int recordingId)
    {
        try
        {
            string s = await _songRecordingSingersRepo.GetDisplayStringAsync(recordingId);
            ApplyStructLabel(lblStructSingersValue, s);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>構造化クレジット用ラベルへの表示適用ヘルパ（空文字なら「(未設定)」グレー）。</summary>
    private static void ApplyStructLabel(Label label, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            label.Text = "(未設定)";
            label.ForeColor = Color.DimGray;
        }
        else
        {
            label.Text = value;
            label.ForeColor = SystemColors.ControlText;
        }
    }

    /// <summary>
    /// 「作詞 / 作曲 / 編曲」編集ボタンのハンドラ。
    /// 既存値を <see cref="PersonAliasCreditsEditDialog"/> に渡し、OK で返ってきた連名行を
    /// <see cref="SongCreditsRepository.ReplaceAllByRoleAsync"/> でトランザクション一括保存する。
    /// </summary>
    private async Task OnEditSongCreditsAsync(SongCreditRole role)
    {
        if (gridSongs.CurrentRow?.DataBoundItem is not Song s || s.SongId <= 0)
        {
            MessageBox.Show("先に曲を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // 既存連名行を取得し、表示用 alias 名は GetDisplayNameAsync で 1 件ずつ解決する
            var existing = await _songCreditsRepo.GetBySongAndRoleAsync(s.SongId, role);
            var initial = new List<PersonAliasCreditsEditDialog.LineDto>();
            foreach (var c in existing)
            {
                string display = await _personAliasesRepo.GetDisplayNameAsync(c.PersonAliasId);
                initial.Add(new PersonAliasCreditsEditDialog.LineDto
                {
                    AliasId = c.PersonAliasId,
                    AliasDisplay = display,
                    PrecedingSeparator = c.PrecedingSeparator,
                    Notes = c.Notes
                });
            }

            string title = role switch
            {
                SongCreditRole.Lyricist => $"作詞クレジット編集（song_id={s.SongId}）",
                SongCreditRole.Composer => $"作曲クレジット編集（song_id={s.SongId}）",
                _                       => $"編曲クレジット編集（song_id={s.SongId}）"
            };

            using var dlg = new PersonAliasCreditsEditDialog(title, initial, _personAliasesRepo);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            // 編集結果を SongCredit モデルに変換し、ReplaceAllByRoleAsync で一括 INSERT
            var newCredits = dlg.ResultLines.Select((l, i) => new SongCredit
            {
                SongId = s.SongId,
                CreditRole = role,
                CreditSeq = (byte)(i + 1),
                PersonAliasId = l.AliasId,
                PrecedingSeparator = i == 0 ? null : l.PrecedingSeparator,
                Notes = l.Notes
            }).ToList();

            await _songCreditsRepo.ReplaceAllByRoleAsync(s.SongId, role, newCredits, Environment.UserName);
            await RefreshSongCreditsLabelsAsync(s.SongId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 歌唱者編集ボタンのハンドラ。
    /// 既存値を <see cref="SongRecordingSingersEditDialog"/> に渡し、OK で返ってきた連名行を
    /// <see cref="SongRecordingSingersRepository.ReplaceAllAsync"/> で一括保存する。
    /// </summary>
    private async Task OnEditSingersAsync()
    {
        if (gridRecordings.CurrentRow?.DataBoundItem is not SongRecording r || r.SongRecordingId <= 0)
        {
            MessageBox.Show("先に録音を選択してください。", "未選択", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // 既存歌唱者行を取得し、関連する 5 種の alias 表示名を解決して詰め直す
            var existing = await _songRecordingSingersRepo.GetByRecordingAsync(r.SongRecordingId);
            var initial = new List<SongRecordingSingersEditDialog.LineDto>();
            foreach (var s in existing)
            {
                initial.Add(new SongRecordingSingersEditDialog.LineDto
                {
                    BillingKind = s.BillingKind,
                    PersonAliasId = s.PersonAliasId,
                    PersonDisplay = s.PersonAliasId.HasValue ? await _personAliasesRepo.GetDisplayNameAsync(s.PersonAliasId.Value) : null,
                    CharacterAliasId = s.CharacterAliasId,
                    CharacterDisplay = s.CharacterAliasId.HasValue ? (await _characterAliasesRepo.GetByIdAsync(s.CharacterAliasId.Value))?.Name : null,
                    VoicePersonAliasId = s.VoicePersonAliasId,
                    VoiceDisplay = s.VoicePersonAliasId.HasValue ? await _personAliasesRepo.GetDisplayNameAsync(s.VoicePersonAliasId.Value) : null,
                    SlashPersonAliasId = s.SlashPersonAliasId,
                    SlashPersonDisplay = s.SlashPersonAliasId.HasValue ? await _personAliasesRepo.GetDisplayNameAsync(s.SlashPersonAliasId.Value) : null,
                    SlashCharacterAliasId = s.SlashCharacterAliasId,
                    SlashCharacterDisplay = s.SlashCharacterAliasId.HasValue ? (await _characterAliasesRepo.GetByIdAsync(s.SlashCharacterAliasId.Value))?.Name : null,
                    PrecedingSeparator = s.PrecedingSeparator,
                    AffiliationText = s.AffiliationText,
                    Notes = s.Notes
                });
            }

            using var dlg = new SongRecordingSingersEditDialog(initial, _personAliasesRepo, _characterAliasesRepo);
            dlg.Text = $"歌唱者クレジット編集（song_recording_id={r.SongRecordingId}）";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var newSingers = dlg.ResultLines.Select((l, i) => new SongRecordingSinger
            {
                SongRecordingId = r.SongRecordingId,
                SingerSeq = (byte)(i + 1),
                BillingKind = l.BillingKind,
                PersonAliasId = l.PersonAliasId,
                CharacterAliasId = l.CharacterAliasId,
                VoicePersonAliasId = l.VoicePersonAliasId,
                SlashPersonAliasId = l.SlashPersonAliasId,
                SlashCharacterAliasId = l.SlashCharacterAliasId,
                PrecedingSeparator = i == 0 ? null : l.PrecedingSeparator,
                AffiliationText = l.AffiliationText,
                Notes = l.Notes
            }).ToList();

            await _songRecordingSingersRepo.ReplaceAllAsync(r.SongRecordingId, newSingers, Environment.UserName);
            await RefreshSingerLabelsAsync(r.SongRecordingId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>コード系コンボ表示用。</summary>
    public sealed record CodeItem(string Code, string Label)
    {
        public string NameJa => Label;
        public string ClassCode => Code;
    }

    /// <summary>シリーズコンボ表示用。</summary>
    private sealed record SeriesItem(int? Id, string Label);
}