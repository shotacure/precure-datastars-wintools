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
/// 劇伴（BGM）マスタ管理エディタ。
/// <para>
/// v1.1.0 ターン C の再設計で劇伴は 1 テーブル (<c>bgm_cues</c>) に統合された。録音セッションは
/// <c>bgm_sessions</c> マスタに切り出され、<c>bgm_cues.session_no</c> で参照する。
/// </para>
/// <para>
/// 画面構成:
/// 上段 - シリーズ／セッションのフィルタコンボと検索バー。
/// 中段 - 左: 劇伴一覧グリッド（シリーズ × M 番号で 1 行 = 1 音源）、右: 選択行の詳細編集パネル。
/// 下段 - 選択中 cue の収録ディスク・トラック一覧。
/// </para>
/// </summary>
public partial class BgmCuesEditorForm : Form
{
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly BgmSessionsRepository _bgmSessionsRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly SeriesRepository _seriesRepo;

    // グリッド表示中の cue リスト（フィルタ適用後）
    private List<BgmCue> _cues = new();
    // シリーズ ID → そのシリーズの全セッションのキャッシュ（コンボボックス更新コスト削減）
    private readonly Dictionary<int, List<BgmSession>> _sessionsBySeries = new();

    public BgmCuesEditorForm(
        BgmCuesRepository bgmCuesRepo,
        BgmSessionsRepository bgmSessionsRepo,
        TracksRepository tracksRepo,
        SeriesRepository seriesRepo)
    {
        _bgmCuesRepo = bgmCuesRepo;
        _bgmSessionsRepo = bgmSessionsRepo;
        _tracksRepo = tracksRepo;
        _seriesRepo = seriesRepo;

        InitializeComponent();
        Load += async (_, __) => await InitAsync();

        gridCues.SelectionChanged += async (_, __) => await OnCueSelectedAsync();

        btnCueNew.Click += (_, __) => ClearCueForm();
        btnCueSave.Click += async (_, __) => await SaveCueAsync();
        btnCueDelete.Click += async (_, __) => await DeleteCueAsync();

        // フィルタ・検索
        cboSeriesFilter.SelectedIndexChanged += async (_, __) => await ApplyFilterAsync();
        cboSessionFilter.SelectedIndexChanged += async (_, __) => await ApplyFilterAsync();
        btnSearch.Click += async (_, __) => await ApplyFilterAsync();
        txtSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await ApplyFilterAsync(); }
        };

        // 編集側のシリーズ切替時、セッションコンボを再構築
        cboSeries.SelectedIndexChanged += async (_, __) => await RebuildCueSessionComboAsync();

        // v1.1.3: 仮番号採番ボタン（現在の編集対象シリーズで次の _temp_NNNNNN を生成して m_no_detail に入れる）
        btnAssignTempNo.Click += async (_, __) => await AssignTempMNoAsync();

        // v1.1.3: CSV 取り込みハンドラ
        btnImportCsv.Click += async (_, __) => await ImportCsvAsync();
    }

    /// <summary>初期化：シリーズコンボ、セッションコンボ、一覧をロードする。</summary>
    private async Task InitAsync()
    {
        try
        {
            // シリーズコンボ（フィルタ側・編集側の 2 つを同じソースで組む）
            var series = (await _seriesRepo.GetAllAsync()).ToList();

            var filterItems = new List<SeriesItem> { new SeriesItem(null, "(全て)") };
            foreach (var s in series) filterItems.Add(new SeriesItem(s.SeriesId, s.Title));
            cboSeriesFilter.DisplayMember = "Label";
            cboSeriesFilter.ValueMember = "Id";
            cboSeriesFilter.DataSource = filterItems;

            var editItems = new List<SeriesItem>();
            foreach (var s in series) editItems.Add(new SeriesItem(s.SeriesId, s.Title));
            cboSeries.DisplayMember = "Label";
            cboSeries.ValueMember = "Id";
            cboSeries.DataSource = editItems;

            // 全シリーズのセッションを先にキャッシュしておく（フィルタ・編集コンボの両方で使う）
            var allSessions = await _bgmSessionsRepo.GetAllAsync();
            foreach (var g in allSessions.GroupBy(x => x.SeriesId))
                _sessionsBySeries[g.Key] = g.OrderBy(x => x.SessionNo).ToList();

            // フィルタ側セッションコンボは初期「(全て)」のみ。シリーズフィルタで絞ったら再構築。
            RebuildFilterSessionCombo(null);

            // 編集側セッションコンボ（シリーズ連動）
            await RebuildCueSessionComboAsync();

            await ApplyFilterAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>フィルタ側セッションコンボを、指定シリーズ配下のセッションで構築する。</summary>
    private void RebuildFilterSessionCombo(int? seriesId)
    {
        var items = new List<SessionItem> { new SessionItem(null, "(全て)") };
        if (seriesId.HasValue && _sessionsBySeries.TryGetValue(seriesId.Value, out var list))
        {
            foreach (var s in list) items.Add(new SessionItem(s.SessionNo, $"{s.SessionNo}: {s.SessionName}"));
        }
        cboSessionFilter.DisplayMember = "Label";
        cboSessionFilter.ValueMember = "No";
        cboSessionFilter.DataSource = items;
    }

    /// <summary>編集側セッションコンボを、編集対象 cue の所属シリーズのセッション一覧で再構築する。</summary>
    private async Task RebuildCueSessionComboAsync()
    {
        try
        {
            int? editingSeriesId = cboSeries.SelectedValue as int?;
            if (!editingSeriesId.HasValue)
            {
                cboSession.DataSource = null;
                return;
            }

            // キャッシュに無ければ取得して詰め直す（新規シリーズに初めてセッション追加した直後など）
            if (!_sessionsBySeries.TryGetValue(editingSeriesId.Value, out var list))
            {
                list = (await _bgmSessionsRepo.GetBySeriesAsync(editingSeriesId.Value)).ToList();
                _sessionsBySeries[editingSeriesId.Value] = list;
            }

            var items = new List<SessionItem>();
            foreach (var s in list) items.Add(new SessionItem(s.SessionNo, $"{s.SessionNo}: {s.SessionName}"));
            cboSession.DisplayMember = "Label";
            cboSession.ValueMember = "No";
            cboSession.DataSource = items;
            if (items.Count > 0) cboSession.SelectedIndex = 0;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>フィルタ条件に従って cue 一覧を取得し、テキスト検索を適用してグリッドに反映する。</summary>
    private async Task ApplyFilterAsync()
    {
        try
        {
            int? seriesId = cboSeriesFilter.SelectedValue as int?;
            byte? sessionNo = cboSessionFilter.SelectedValue as byte?;

            // フィルタ側セッションコンボはシリーズに連動
            RebuildFilterSessionCombo(seriesId);

            IReadOnlyList<BgmCue> fetched;
            if (seriesId.HasValue && sessionNo.HasValue)
                fetched = await _bgmCuesRepo.GetBySeriesAndSessionAsync(seriesId.Value, sessionNo.Value);
            else if (seriesId.HasValue)
                fetched = await _bgmCuesRepo.GetBySeriesAsync(seriesId.Value);
            else
                fetched = Array.Empty<BgmCue>(); // シリーズ未選択では全件取得しない（件数膨大を想定）

            // テキスト検索（m_no_detail / m_no_class / menu_title / composer / arranger の部分一致）
            string kw = txtSearch.Text.Trim();
            IEnumerable<BgmCue> q = fetched;
            if (!string.IsNullOrEmpty(kw))
            {
                q = q.Where(c =>
                    Contains(c.MNoDetail, kw) ||
                    Contains(c.MNoClass, kw) ||
                    Contains(c.MenuTitle, kw) ||
                    Contains(c.ComposerName, kw) ||
                    Contains(c.ArrangerName, kw));
            }

            _cues = q.ToList();
            gridCues.DataSource = null;
            gridCues.DataSource = _cues;
            HideMetaColumns(gridCues);
            ClearCueForm();

            // 選択解除により下段も空に
            gridCueTracks.DataSource = null;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // ===== cue 編集 =====

    private async Task OnCueSelectedAsync()
    {
        if (gridCues.CurrentRow?.DataBoundItem is not BgmCue c)
        {
            ClearCueForm();
            gridCueTracks.DataSource = null;
            return;
        }

        // 編集フォームにバインド（シリーズを先に設定 → セッションコンボを再構築 → セッション値を当てる順）
        cboSeries.SelectedValue = c.SeriesId;
        await RebuildCueSessionComboAsync();
        cboSession.SelectedValue = c.SessionNo;

        txtMNoDetail.Text = c.MNoDetail;
        txtMNoClass.Text = c.MNoClass ?? "";
        txtMenuTitle.Text = c.MenuTitle ?? "";
        txtComposer.Text = c.ComposerName ?? "";
        txtComposerKana.Text = c.ComposerNameKana ?? "";
        txtArranger.Text = c.ArrangerName ?? "";
        txtArrangerKana.Text = c.ArrangerNameKana ?? "";
        numLength.Value = c.LengthSeconds ?? 0;
        txtNotes.Text = c.Notes ?? "";
        // v1.1.3: 仮 M 番号フラグを反映
        chkIsTempMNo.Checked = c.IsTempMNo;

        // 下段に収録ディスク・トラック一覧を表示
        try
        {
            var refs = await _tracksRepo.GetTracksByBgmCueAsync(c.SeriesId, c.MNoDetail);
            gridCueTracks.DataSource = null;
            gridCueTracks.DataSource = refs.ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ClearCueForm()
    {
        txtMNoDetail.Text = "";
        txtMNoClass.Text = "";
        txtMenuTitle.Text = "";
        txtComposer.Text = ""; txtComposerKana.Text = "";
        txtArranger.Text = ""; txtArrangerKana.Text = "";
        numLength.Value = 0;
        txtNotes.Text = "";
        // v1.1.3: 仮 M 番号フラグをリセット
        chkIsTempMNo.Checked = false;
    }

    private async Task SaveCueAsync()
    {
        try
        {
            if (cboSeries.SelectedValue is not int seriesId)
            { MessageBox.Show(this, "シリーズを選択してください。"); return; }
            if (string.IsNullOrWhiteSpace(txtMNoDetail.Text))
            { MessageBox.Show(this, "M 番号詳細 (m_no_detail) は必須です。"); return; }

            byte sessionNo = cboSession.SelectedValue is byte n ? n : (byte)0;

            var cue = new BgmCue
            {
                SeriesId = seriesId,
                MNoDetail = txtMNoDetail.Text.Trim(),
                SessionNo = sessionNo,
                MNoClass = NullIfEmpty(txtMNoClass.Text),
                MenuTitle = NullIfEmpty(txtMenuTitle.Text),
                ComposerName = NullIfEmpty(txtComposer.Text),
                ComposerNameKana = NullIfEmpty(txtComposerKana.Text),
                ArrangerName = NullIfEmpty(txtArranger.Text),
                ArrangerNameKana = NullIfEmpty(txtArrangerKana.Text),
                LengthSeconds = numLength.Value == 0 ? null : (ushort)numLength.Value,
                Notes = NullIfEmpty(txtNotes.Text),
                // v1.1.3: 仮 M 番号フラグも保存する
                IsTempMNo = chkIsTempMNo.Checked,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };

            await _bgmCuesRepo.UpsertAsync(cue);
            MessageBox.Show(this, $"劇伴 cue を保存しました ({cue.SeriesId}:{cue.MNoDetail})。");
            await ApplyFilterAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task DeleteCueAsync()
    {
        if (gridCues.CurrentRow?.DataBoundItem is not BgmCue c) return;
        if (Confirm($"劇伴 cue [{c.SeriesId}:{c.MNoDetail}] を論理削除しますか？") != DialogResult.Yes) return;
        try
        {
            await _bgmCuesRepo.SoftDeleteAsync(c.SeriesId, c.MNoDetail, Environment.UserName);
            await ApplyFilterAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // ===== ヘルパ =====

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// 「仮番号を採番」ボタン（v1.1.3 追加）。編集中シリーズ配下の既存 _temp_NNNNNN 連番から
    /// 次の値を算出し、<see cref="txtMNoDetail"/> に自動入力する。<see cref="chkIsTempMNo"/> も自動でオン。
    /// </summary>
    private async Task AssignTempMNoAsync()
    {
        try
        {
            if (cboSeries.SelectedValue is not int seriesId)
            {
                MessageBox.Show(this, "先にシリーズを選択してください。"); return;
            }
            string next = await _bgmCuesRepo.GenerateNextTempMNoAsync(seriesId);
            txtMNoDetail.Text = next;
            chkIsTempMNo.Checked = true;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 劇伴 CSV 取り込みハンドラ（v1.1.3 追加）。
    /// ドライランで件数確認 → 本実行の 2 段階で進む。進行中のシリーズフィルタは維持する。
    /// </summary>
    private async Task ImportCsvAsync()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "劇伴 CSV を選択",
            Filter = "CSV ファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        var svc = new BgmCueCsvImportService(_bgmCuesRepo, _bgmSessionsRepo, _seriesRepo);
        try
        {
            var preview = await svc.ImportAsync(ofd.FileName, Environment.UserName, dryRun: true);
            string warnSummary = preview.Warnings.Count == 0 ? "" : "\n\n警告:\n - " + string.Join("\n - ", preview.Warnings.Take(10));
            var ask = MessageBox.Show(this,
                $"取り込み結果（ドライラン）:\n" +
                $"  新規: {preview.Inserted} 件\n" +
                $"  更新: {preview.Updated} 件\n" +
                $"  スキップ: {preview.Skipped} 件\n" +
                $"  セッション自動作成: {preview.SessionsCreated} 件\n" +
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
                $"  セッション自動作成: {applied.SessionsCreated} 件\n" +
                $"  スキップ: {applied.Skipped} 件",
                "CSV 取り込み", MessageBoxButtons.OK, MessageBoxIcon.Information);

            // キャッシュを捨てて再読込（セッションが増えている可能性があるため）
            _sessionsBySeries.Clear();
            var allSessions = await _bgmSessionsRepo.GetAllAsync();
            foreach (var g in allSessions.GroupBy(x => x.SeriesId))
                _sessionsBySeries[g.Key] = g.OrderBy(x => x.SessionNo).ToList();
            await ApplyFilterAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>DataGridView から監査列などを非表示にする共通処理。</summary>
    private static void HideMetaColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn col in grid.Columns)
        {
            if (col.Name is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy" or "IsDeleted" or "Notes")
                col.Visible = false;
        }
    }

    private DialogResult Confirm(string msg)
        => MessageBox.Show(this, msg, "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>シリーズコンボ表示用。</summary>
    private sealed record SeriesItem(int? Id, string Label);

    /// <summary>セッションコンボ表示用。</summary>
    private sealed record SessionItem(byte? No, string Label);
}