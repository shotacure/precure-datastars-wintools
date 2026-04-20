using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 小マスタ群の統合編集フォーム。
/// <para>
/// TabControl により以下のマスタテーブルを 1 画面で編集できる:
/// product_kinds / disc_kinds / track_content_kinds /
/// song_music_classes / song_size_variants / song_part_variants /
/// bgm_sessions（劇伴の録音セッションマスタ、シリーズごとに session_no を採番）。
/// </para>
/// </summary>
public partial class MastersEditorForm : Form
{
    private readonly ProductKindsRepository _productKindsRepo;
    private readonly DiscKindsRepository _discKindsRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongMusicClassesRepository _songMusicClassesRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    private readonly BgmSessionsRepository _bgmSessionsRepo;
    private readonly SeriesRepository _seriesRepo;

    public MastersEditorForm(
        ProductKindsRepository productKindsRepo,
        DiscKindsRepository discKindsRepo,
        TrackContentKindsRepository trackContentKindsRepo,
        SongMusicClassesRepository songMusicClassesRepo,
        SongSizeVariantsRepository songSizeVariantsRepo,
        SongPartVariantsRepository songPartVariantsRepo,
        BgmSessionsRepository bgmSessionsRepo,
        SeriesRepository seriesRepo)
    {
        _productKindsRepo = productKindsRepo;
        _discKindsRepo = discKindsRepo;
        _trackContentKindsRepo = trackContentKindsRepo;
        _songMusicClassesRepo = songMusicClassesRepo;
        _songSizeVariantsRepo = songSizeVariantsRepo;
        _songPartVariantsRepo = songPartVariantsRepo;
        _bgmSessionsRepo = bgmSessionsRepo;
        _seriesRepo = seriesRepo;

        InitializeComponent();
        Load += async (_, __) => await LoadAllAsync();

        // bgm_sessions タブ：シリーズ切替でセッション一覧を更新
        cboBgmSessionSeries.SelectedIndexChanged += async (_, __) => await ReloadBgmSessionsAsync();
        btnAddBgmSession.Click += async (_, __) => await AddBgmSessionAsync();
        btnSaveBgmSession.Click += async (_, __) => await SaveBgmSessionAsync();
        btnDeleteBgmSession.Click += async (_, __) => await DeleteBgmSessionAsync();
        gridBgmSessions.SelectionChanged += (_, __) => OnBgmSessionRowSelected();
    }

    /// <summary>全タブのデータを一括で読み込む。</summary>
    private async Task LoadAllAsync()
    {
        gridProductKinds.DataSource = await _productKindsRepo.GetAllAsync();
        gridDiscKinds.DataSource = await _discKindsRepo.GetAllAsync();
        gridTrackContentKinds.DataSource = await _trackContentKindsRepo.GetAllAsync();
        gridSongMusicClasses.DataSource = await _songMusicClassesRepo.GetAllAsync();
        gridSongSizeVariants.DataSource = await _songSizeVariantsRepo.GetAllAsync();
        gridSongPartVariants.DataSource = await _songPartVariantsRepo.GetAllAsync();

        // bgm_sessions タブ: シリーズコンボのバインド
        var series = await _seriesRepo.GetAllAsync();
        var items = new List<SeriesItem>();
        foreach (var s in series) items.Add(new SeriesItem(s.SeriesId, s.Title));
        cboBgmSessionSeries.DisplayMember = "Label";
        cboBgmSessionSeries.ValueMember = "Id";
        cboBgmSessionSeries.DataSource = items;
        if (items.Count > 0) await ReloadBgmSessionsAsync();
    }

    // ===== bgm_sessions =====

    /// <summary>選択中シリーズのセッション一覧を読み直す。</summary>
    private async Task ReloadBgmSessionsAsync()
    {
        try
        {
            if (cboBgmSessionSeries.SelectedValue is not int seriesId) return;
            // v1.1.1 以降は session_no=0 の既定セッションを自動生成しない。
            // セッションはシリーズ内で必要になったときに「追加」ボタンで 1, 2, 3... と採番して作る運用。
            var list = await _bgmSessionsRepo.GetBySeriesAsync(seriesId);
            gridBgmSessions.DataSource = null;
            gridBgmSessions.DataSource = list.ToList();
            foreach (DataGridViewColumn c in gridBgmSessions.Columns)
            {
                if (c.Name is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy")
                    c.Visible = false;
            }
            ClearBgmSessionForm();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択行のセッション名・備考を編集フォームに反映。</summary>
    private void OnBgmSessionRowSelected()
    {
        if (gridBgmSessions.CurrentRow?.DataBoundItem is BgmSession s)
        {
            numBgmSessionNo.Value = s.SessionNo;
            txtBgmSessionName.Text = s.SessionName;
            txtBgmSessionNotes.Text = s.Notes ?? "";
        }
    }

    /// <summary>編集フォーム初期化。</summary>
    private void ClearBgmSessionForm()
    {
        numBgmSessionNo.Value = 0;
        txtBgmSessionName.Text = "";
        txtBgmSessionNotes.Text = "";
    }

    /// <summary>セッションを新規追加する（session_no は自動採番）。</summary>
    private async Task AddBgmSessionAsync()
    {
        try
        {
            if (cboBgmSessionSeries.SelectedValue is not int seriesId)
            { MessageBox.Show(this, "シリーズを選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtBgmSessionName.Text))
            { MessageBox.Show(this, "セッション名を入力してください。"); return; }

            var newNo = await _bgmSessionsRepo.InsertNextAsync(
                seriesId, txtBgmSessionName.Text.Trim(),
                NullIfEmpty(txtBgmSessionNotes.Text), Environment.UserName);
            MessageBox.Show(this, $"セッション #{newNo} を追加しました。");
            await ReloadBgmSessionsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中セッションのセッション名・備考を更新する。session_no は PK のため変更不可。</summary>
    private async Task SaveBgmSessionAsync()
    {
        try
        {
            if (gridBgmSessions.CurrentRow?.DataBoundItem is not BgmSession s)
            { MessageBox.Show(this, "編集対象のセッションを選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtBgmSessionName.Text))
            { MessageBox.Show(this, "セッション名は必須です。"); return; }

            s.SessionName = txtBgmSessionName.Text.Trim();
            s.Notes = NullIfEmpty(txtBgmSessionNotes.Text);
            s.UpdatedBy = Environment.UserName;
            await _bgmSessionsRepo.UpdateAsync(s);
            MessageBox.Show(this, "セッションを更新しました。");
            await ReloadBgmSessionsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中セッションを削除する。配下の bgm_cues があれば FK RESTRICT で失敗する。</summary>
    private async Task DeleteBgmSessionAsync()
    {
        try
        {
            if (gridBgmSessions.CurrentRow?.DataBoundItem is not BgmSession s)
            { MessageBox.Show(this, "削除対象のセッションを選択してください。"); return; }
            if (Confirm($"セッション [{s.SeriesId}:{s.SessionNo} {s.SessionName}] を削除しますか？") != DialogResult.Yes) return;
            await _bgmSessionsRepo.DeleteAsync(s.SeriesId, s.SessionNo);
            await ReloadBgmSessionsAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private sealed record SeriesItem(int Id, string Label);

    // ===== product_kinds =====

    /// <summary>商品種別: UPSERT（コード・名称・表示順）。</summary>
    private async void btnSaveProductKind_Click(object? sender, EventArgs e)
    {
        try
        {
            var k = new ProductKind
            {
                KindCode = txtPkCode.Text.Trim(),
                NameJa = txtPkNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtPkNameEn.Text),
                DisplayOrder = (byte?)numPkOrder.Value,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrEmpty(k.KindCode) || string.IsNullOrEmpty(k.NameJa)) { Beep(); return; }
            await _productKindsRepo.UpsertAsync(k);
            gridProductKinds.DataSource = await _productKindsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>商品種別: 選択行の削除。</summary>
    private async void btnDeleteProductKind_Click(object? sender, EventArgs e)
    {
        try
        {
            var code = GetSelectedCode(gridProductKinds, "KindCode");
            if (code is null) return;
            if (Confirm($"product_kinds [{code}] を削除しますか？") != DialogResult.Yes) return;
            await _productKindsRepo.DeleteAsync(code);
            gridProductKinds.DataSource = await _productKindsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== disc_kinds =====

    private async void btnSaveDiscKind_Click(object? sender, EventArgs e)
    {
        try
        {
            var k = new DiscKind
            {
                KindCode = txtDkCode.Text.Trim(),
                NameJa = txtDkNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtDkNameEn.Text),
                DisplayOrder = (byte?)numDkOrder.Value,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrEmpty(k.KindCode) || string.IsNullOrEmpty(k.NameJa)) { Beep(); return; }
            await _discKindsRepo.UpsertAsync(k);
            gridDiscKinds.DataSource = await _discKindsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void btnDeleteDiscKind_Click(object? sender, EventArgs e)
    {
        try
        {
            var code = GetSelectedCode(gridDiscKinds, "KindCode");
            if (code is null) return;
            if (Confirm($"disc_kinds [{code}] を削除しますか？") != DialogResult.Yes) return;
            await _discKindsRepo.DeleteAsync(code);
            gridDiscKinds.DataSource = await _discKindsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== track_content_kinds =====

    private async void btnSaveTrackContentKind_Click(object? sender, EventArgs e)
    {
        try
        {
            var k = new TrackContentKind
            {
                KindCode = txtTcCode.Text.Trim(),
                NameJa = txtTcNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtTcNameEn.Text),
                DisplayOrder = (byte?)numTcOrder.Value,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrEmpty(k.KindCode) || string.IsNullOrEmpty(k.NameJa)) { Beep(); return; }
            await _trackContentKindsRepo.UpsertAsync(k);
            gridTrackContentKinds.DataSource = await _trackContentKindsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void btnDeleteTrackContentKind_Click(object? sender, EventArgs e)
    {
        try
        {
            var code = GetSelectedCode(gridTrackContentKinds, "KindCode");
            if (code is null) return;
            if (Confirm($"track_content_kinds [{code}] を削除しますか？") != DialogResult.Yes) return;
            await _trackContentKindsRepo.DeleteAsync(code);
            gridTrackContentKinds.DataSource = await _trackContentKindsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== song_music_classes =====

    private async void btnSaveSongMusicClass_Click(object? sender, EventArgs e)
    {
        try
        {
            var k = new SongMusicClass
            {
                ClassCode = txtSmcCode.Text.Trim(),
                NameJa = txtSmcNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtSmcNameEn.Text),
                DisplayOrder = (byte?)numSmcOrder.Value,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrEmpty(k.ClassCode) || string.IsNullOrEmpty(k.NameJa)) { Beep(); return; }
            await _songMusicClassesRepo.UpsertAsync(k);
            gridSongMusicClasses.DataSource = await _songMusicClassesRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void btnDeleteSongMusicClass_Click(object? sender, EventArgs e)
    {
        try
        {
            var code = GetSelectedCode(gridSongMusicClasses, "ClassCode");
            if (code is null) return;
            if (Confirm($"song_music_classes [{code}] を削除しますか？") != DialogResult.Yes) return;
            await _songMusicClassesRepo.DeleteAsync(code);
            gridSongMusicClasses.DataSource = await _songMusicClassesRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== song_size_variants =====

    private async void btnSaveSongSizeVariant_Click(object? sender, EventArgs e)
    {
        try
        {
            var k = new SongSizeVariant
            {
                VariantCode = txtSsvCode.Text.Trim(),
                NameJa = txtSsvNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtSsvNameEn.Text),
                DisplayOrder = (byte?)numSsvOrder.Value,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrEmpty(k.VariantCode) || string.IsNullOrEmpty(k.NameJa)) { Beep(); return; }
            await _songSizeVariantsRepo.UpsertAsync(k);
            gridSongSizeVariants.DataSource = await _songSizeVariantsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void btnDeleteSongSizeVariant_Click(object? sender, EventArgs e)
    {
        try
        {
            var code = GetSelectedCode(gridSongSizeVariants, "VariantCode");
            if (code is null) return;
            if (Confirm($"song_size_variants [{code}] を削除しますか？") != DialogResult.Yes) return;
            await _songSizeVariantsRepo.DeleteAsync(code);
            gridSongSizeVariants.DataSource = await _songSizeVariantsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== song_part_variants =====

    private async void btnSaveSongPartVariant_Click(object? sender, EventArgs e)
    {
        try
        {
            var k = new SongPartVariant
            {
                VariantCode = txtSpvCode.Text.Trim(),
                NameJa = txtSpvNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtSpvNameEn.Text),
                DisplayOrder = (byte?)numSpvOrder.Value,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            if (string.IsNullOrEmpty(k.VariantCode) || string.IsNullOrEmpty(k.NameJa)) { Beep(); return; }
            await _songPartVariantsRepo.UpsertAsync(k);
            gridSongPartVariants.DataSource = await _songPartVariantsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void btnDeleteSongPartVariant_Click(object? sender, EventArgs e)
    {
        try
        {
            var code = GetSelectedCode(gridSongPartVariants, "VariantCode");
            if (code is null) return;
            if (Confirm($"song_part_variants [{code}] を削除しますか？") != DialogResult.Yes) return;
            await _songPartVariantsRepo.DeleteAsync(code);
            gridSongPartVariants.DataSource = await _songPartVariantsRepo.GetAllAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== 共通ヘルパ =====

    /// <summary>DataGridView で現在選択中の行から指定列の値（文字列）を取得する。</summary>
    private static string? GetSelectedCode(DataGridView grid, string columnName)
    {
        if (grid.CurrentRow is null) return null;
        var v = grid.CurrentRow.Cells[columnName]?.Value?.ToString();
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Beep() => System.Media.SystemSounds.Beep.Play();

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
}