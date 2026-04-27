using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
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

        // v1.1.4 改: 全グリッドで監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）を
        // データバインド完了時に非表示にする。bgm_sessions の従来の個別非表示処理（ReloadBgmSessionsAsync 内）も
        // 残しているが、こちらでも二重に処理されるだけで実害はない。
        HideAuditColumns(gridProductKinds);
        HideAuditColumns(gridDiscKinds);
        HideAuditColumns(gridTrackContentKinds);
        HideAuditColumns(gridSongMusicClasses);
        HideAuditColumns(gridSongSizeVariants);
        HideAuditColumns(gridSongPartVariants);
        HideAuditColumns(gridBgmSessions);

        // v1.1.4 改: 6 つのマスタタブで行ドラッグ&ドロップによる並べ替えを有効化。
        // ドロップしただけでは DB は変わらず、グリッド上の List<T> 内で要素を入れ替えるだけ。
        // 実際の DisplayOrder 反映は「並べ替えを反映」ボタンの確認ダイアログを経て行う。
        // bgm_sessions タブは session_no が表示順を兼ねるため対象外。
        EnableRowDrag(gridProductKinds);
        EnableRowDrag(gridDiscKinds);
        EnableRowDrag(gridTrackContentKinds);
        EnableRowDrag(gridSongMusicClasses);
        EnableRowDrag(gridSongSizeVariants);
        EnableRowDrag(gridSongPartVariants);
    }

    /// <summary>
    /// 全タブのデータを一括で読み込む。
    /// v1.1.4 改: 行ドラッグ&ドロップで要素を入れ替えるため、DataSource は IList を実装する
    /// 具象 <see cref="List{T}"/> としてバインドする（<see cref="IEnumerable{T}"/> のままだと
    /// 並べ替え操作ができない）。
    /// </summary>
    private async Task LoadAllAsync()
    {
        gridProductKinds.DataSource = (await _productKindsRepo.GetAllAsync()).ToList();
        gridDiscKinds.DataSource = (await _discKindsRepo.GetAllAsync()).ToList();
        gridTrackContentKinds.DataSource = (await _trackContentKindsRepo.GetAllAsync()).ToList();
        gridSongMusicClasses.DataSource = (await _songMusicClassesRepo.GetAllAsync()).ToList();
        gridSongSizeVariants.DataSource = (await _songSizeVariantsRepo.GetAllAsync()).ToList();
        gridSongPartVariants.DataSource = (await _songPartVariantsRepo.GetAllAsync()).ToList();

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
            // v1.1.4 改: 監査列の Visible=false 設定はコンストラクタで結線した HideAuditColumns ヘルパが
            // DataBindingComplete 時に自動適用するため、ここでは個別処理しない。
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
            gridProductKinds.DataSource = (await _productKindsRepo.GetAllAsync()).ToList();
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
            gridProductKinds.DataSource = (await _productKindsRepo.GetAllAsync()).ToList();
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
            gridDiscKinds.DataSource = (await _discKindsRepo.GetAllAsync()).ToList();
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
            gridDiscKinds.DataSource = (await _discKindsRepo.GetAllAsync()).ToList();
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
            gridTrackContentKinds.DataSource = (await _trackContentKindsRepo.GetAllAsync()).ToList();
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
            gridTrackContentKinds.DataSource = (await _trackContentKindsRepo.GetAllAsync()).ToList();
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
            gridSongMusicClasses.DataSource = (await _songMusicClassesRepo.GetAllAsync()).ToList();
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
            gridSongMusicClasses.DataSource = (await _songMusicClassesRepo.GetAllAsync()).ToList();
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
            gridSongSizeVariants.DataSource = (await _songSizeVariantsRepo.GetAllAsync()).ToList();
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
            gridSongSizeVariants.DataSource = (await _songSizeVariantsRepo.GetAllAsync()).ToList();
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
            gridSongPartVariants.DataSource = (await _songPartVariantsRepo.GetAllAsync()).ToList();
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
            gridSongPartVariants.DataSource = (await _songPartVariantsRepo.GetAllAsync()).ToList();
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

    /// <summary>
    /// グリッドの監査列（CreatedAt / UpdatedAt / CreatedBy / UpdatedBy）を、データバインド完了時に
    /// 自動的に非表示にする。v1.1.4 改で全マスタタブに統一適用。
    /// </summary>
    private static void HideAuditColumns(DataGridView grid)
    {
        grid.DataBindingComplete += (_, __) =>
        {
            foreach (DataGridViewColumn c in grid.Columns)
            {
                if (c.Name is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy")
                    c.Visible = false;
            }
        };
    }

    /// <summary>
    /// 6 つのマスタタブのフォーム入力欄をクリアする共通ヘルパ。
    /// 「新規」ボタン押下時に呼ばれ、グリッドの選択も解除して、これから追加する内容と
    /// 既存行の編集内容が混ざらないようにする。
    /// </summary>
    private static void ClearMasterForm(DataGridView grid, TextBox txtCode, TextBox txtJa, TextBox txtEn, NumericUpDown numOrder)
    {
        grid.ClearSelection();
        txtCode.Text = "";
        txtJa.Text = "";
        txtEn.Text = "";
        numOrder.Value = 0;
        txtCode.Focus();
    }

    /// <summary>
    /// グリッドに行ドラッグ&ドロップ機能を組み込む。データソースが <see cref="IList"/> の場合に限り
    /// ドラッグ位置に応じて要素を入れ替え、再バインドする。
    /// <para>
    /// この時点では DB は変わらず、グリッド表示上の並び順だけが変わる。実際の DisplayOrder への
    /// 反映は「並べ替えを反映」ボタンの確認ダイアログを経て、現在の並び順で 1, 2, 3... と振り直す。
    /// </para>
    /// </summary>
    private static void EnableRowDrag(DataGridView grid)
    {
        // ドラッグ開始位置（マウスを押した時点での行 Y 座標）を記録するための変数。
        // SystemInformation.DragSize を超える移動があったらドラッグ開始と見なす。
        Rectangle dragBoxFromMouseDown = Rectangle.Empty;
        int dragRowIndex = -1;

        grid.MouseDown += (s, e) =>
        {
            var hit = grid.HitTest(e.X, e.Y);
            if (hit.Type == DataGridViewHitTestType.Cell && hit.RowIndex >= 0)
            {
                dragRowIndex = hit.RowIndex;
                Size dragSize = SystemInformation.DragSize;
                dragBoxFromMouseDown = new Rectangle(
                    new Point(e.X - dragSize.Width / 2, e.Y - dragSize.Height / 2),
                    dragSize);
            }
            else
            {
                dragRowIndex = -1;
                dragBoxFromMouseDown = Rectangle.Empty;
            }
        };

        grid.MouseMove += (s, e) =>
        {
            if ((e.Button & MouseButtons.Left) == MouseButtons.Left
                && dragRowIndex >= 0
                && dragBoxFromMouseDown != Rectangle.Empty
                && !dragBoxFromMouseDown.Contains(e.X, e.Y))
            {
                // ドラッグ中はカーソルを移動カーソルにし、DoDragDrop を発火する。
                grid.DoDragDrop(dragRowIndex, DragDropEffects.Move);
            }
        };

        grid.DragEnter += (s, e) =>
        {
            // 自分自身からのドラッグのみ受け入れる
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.None;
        };

        grid.DragOver += (s, e) =>
        {
            if (e.Data?.GetDataPresent(typeof(int)) == true)
                e.Effect = DragDropEffects.Move;
            else
                e.Effect = DragDropEffects.None;
        };

        grid.DragDrop += (s, e) =>
        {
            // ドロップ位置の行インデックスを取得
            var pt = grid.PointToClient(new Point(e.X, e.Y));
            var hit = grid.HitTest(pt.X, pt.Y);
            int destIndex = hit.RowIndex;

            // セル外（ヘッダー直下や末尾の空白部分）にドロップされた場合は末尾に挿入
            if (destIndex < 0)
            {
                if (grid.Rows.Count > 0)
                    destIndex = grid.Rows.Count - 1;
                else
                    return;
            }

            if (e.Data?.GetData(typeof(int)) is not int srcIndex) return;
            if (srcIndex == destIndex) return;

            // DataSource が IList を実装している場合のみ並べ替えを実行する
            if (grid.DataSource is not IList list) return;
            if (srcIndex < 0 || srcIndex >= list.Count) return;
            if (destIndex < 0 || destIndex >= list.Count) return;

            object? item = list[srcIndex];
            list.RemoveAt(srcIndex);
            list.Insert(destIndex, item);

            // List 側を直接書き換えても DataGridView は変更を検知しない（INotifyCollectionChanged を
            // 実装していないため）ので、DataSource を一旦 null にしてから再バインドする。
            grid.DataSource = null;
            grid.DataSource = list;

            // 移動した行を再選択
            if (destIndex >= 0 && destIndex < grid.Rows.Count)
            {
                grid.ClearSelection();
                grid.Rows[destIndex].Selected = true;
                grid.CurrentCell = grid.Rows[destIndex].Cells[0];
            }

            dragRowIndex = -1;
            dragBoxFromMouseDown = Rectangle.Empty;
        };
    }

    /// <summary>
    /// グリッドに表示中の <see cref="IList"/> を「現在の並び順で DisplayOrder を 1, 2, 3... に
    /// 振り直す」ロジック。各要素の DisplayOrder を更新したうえで、呼び出し側に渡されたコールバック
    /// （Repository の UpsertAsync を呼ぶ）を実行する。
    /// <para>
    /// 既存の <c>CreatedBy</c> は List 内のアイテムに保持されているため UPSERT 時に保全される
    /// （UpdatedBy のみ <see cref="Environment.UserName"/> で上書き）。
    /// </para>
    /// </summary>
    private async Task ApplyDisplayOrderAsync<T>(
        DataGridView grid,
        Action<T, byte> setOrder,
        Action<T, string> setUpdatedBy,
        Func<T, Task> upsert)
    {
        if (grid.DataSource is not IList<T> list) return;
        if (list.Count == 0) { MessageBox.Show(this, "対象データがありません。"); return; }
        if (Confirm($"現在の並び順で表示順 (DisplayOrder) を 1〜{list.Count} に振り直します。よろしいですか？") != DialogResult.Yes) return;

        try
        {
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                setOrder(item, (byte)(i + 1));
                setUpdatedBy(item, Environment.UserName);
                await upsert(item);
            }
            MessageBox.Show(this, "並べ替えを反映しました。");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== 各マスタの「新規」ボタン =====
    // v1.1.4 改: 既存行の編集と新規追加を明確に区別するため、フォームをクリアする「新規」ボタンを
    // 6 つのマスタタブすべてに追加。コードを変えて「保存 / 更新」を押せば新規 INSERT として動作する。

    private void btnNewProductKind_Click(object? sender, EventArgs e)
        => ClearMasterForm(gridProductKinds, txtPkCode, txtPkNameJa, txtPkNameEn, numPkOrder);

    private void btnNewDiscKind_Click(object? sender, EventArgs e)
        => ClearMasterForm(gridDiscKinds, txtDkCode, txtDkNameJa, txtDkNameEn, numDkOrder);

    private void btnNewTrackContentKind_Click(object? sender, EventArgs e)
        => ClearMasterForm(gridTrackContentKinds, txtTcCode, txtTcNameJa, txtTcNameEn, numTcOrder);

    private void btnNewSongMusicClass_Click(object? sender, EventArgs e)
        => ClearMasterForm(gridSongMusicClasses, txtSmcCode, txtSmcNameJa, txtSmcNameEn, numSmcOrder);

    private void btnNewSongSizeVariant_Click(object? sender, EventArgs e)
        => ClearMasterForm(gridSongSizeVariants, txtSsvCode, txtSsvNameJa, txtSsvNameEn, numSsvOrder);

    private void btnNewSongPartVariant_Click(object? sender, EventArgs e)
        => ClearMasterForm(gridSongPartVariants, txtSpvCode, txtSpvNameJa, txtSpvNameEn, numSpvOrder);

    // ===== 各マスタの「並べ替えを反映」ボタン =====
    // v1.1.4 改: 行ドラッグ&ドロップで並べ替えた現在のグリッド順を、DisplayOrder = 1, 2, 3... として
    // 一斉 UPSERT する。確認ダイアログ後に実行。完了後は再読み込みして DB 上の最新順を表示する。

    private async void btnApplyOrderProductKind_Click(object? sender, EventArgs e)
    {
        await ApplyDisplayOrderAsync<ProductKind>(
            gridProductKinds,
            (k, o) => k.DisplayOrder = o,
            (k, u) => k.UpdatedBy = u,
            k => _productKindsRepo.UpsertAsync(k));
        gridProductKinds.DataSource = (await _productKindsRepo.GetAllAsync()).ToList();
    }

    private async void btnApplyOrderDiscKind_Click(object? sender, EventArgs e)
    {
        await ApplyDisplayOrderAsync<DiscKind>(
            gridDiscKinds,
            (k, o) => k.DisplayOrder = o,
            (k, u) => k.UpdatedBy = u,
            k => _discKindsRepo.UpsertAsync(k));
        gridDiscKinds.DataSource = (await _discKindsRepo.GetAllAsync()).ToList();
    }

    private async void btnApplyOrderTrackContentKind_Click(object? sender, EventArgs e)
    {
        await ApplyDisplayOrderAsync<TrackContentKind>(
            gridTrackContentKinds,
            (k, o) => k.DisplayOrder = o,
            (k, u) => k.UpdatedBy = u,
            k => _trackContentKindsRepo.UpsertAsync(k));
        gridTrackContentKinds.DataSource = (await _trackContentKindsRepo.GetAllAsync()).ToList();
    }

    private async void btnApplyOrderSongMusicClass_Click(object? sender, EventArgs e)
    {
        await ApplyDisplayOrderAsync<SongMusicClass>(
            gridSongMusicClasses,
            (k, o) => k.DisplayOrder = o,
            (k, u) => k.UpdatedBy = u,
            k => _songMusicClassesRepo.UpsertAsync(k));
        gridSongMusicClasses.DataSource = (await _songMusicClassesRepo.GetAllAsync()).ToList();
    }

    private async void btnApplyOrderSongSizeVariant_Click(object? sender, EventArgs e)
    {
        await ApplyDisplayOrderAsync<SongSizeVariant>(
            gridSongSizeVariants,
            (k, o) => k.DisplayOrder = o,
            (k, u) => k.UpdatedBy = u,
            k => _songSizeVariantsRepo.UpsertAsync(k));
        gridSongSizeVariants.DataSource = (await _songSizeVariantsRepo.GetAllAsync()).ToList();
    }

    private async void btnApplyOrderSongPartVariant_Click(object? sender, EventArgs e)
    {
        await ApplyDisplayOrderAsync<SongPartVariant>(
            gridSongPartVariants,
            (k, o) => k.DisplayOrder = o,
            (k, u) => k.UpdatedBy = u,
            k => _songPartVariantsRepo.UpsertAsync(k));
        gridSongPartVariants.DataSource = (await _songPartVariantsRepo.GetAllAsync()).ToList();
    }
}