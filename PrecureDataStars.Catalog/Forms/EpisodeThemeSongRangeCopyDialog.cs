using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// エピソード主題歌の範囲コピーダイアログ。
/// <para>
/// あるエピソード（コピー元）の主題歌（OP / ED / 挿入歌）を、同じシリーズの連続話数範囲
/// （<c>series_ep_no</c> ベース、開始〜終了）の各エピソードに一括投入する。
/// 既存の <see cref="EpisodeThemeSongCopyDialog"/> が「1 件のコピー先指定」だったのに対し、
/// 本ダイアログは「2 話〜49 話に同じ主題歌を一気に流す」用途を担う。
/// </para>
/// <para>
/// 衝突時は (a) 上書き or (b) スキップを選択できる。本放送限定行（is_broadcast_only=1）は
/// 既定行と同様にコピーするか、既定行のみコピーするかをチェックボックスで切り替える。
/// </para>
/// </summary>
public sealed partial class EpisodeThemeSongRangeCopyDialog : Form
{
    private readonly EpisodeThemeSongsRepository _etsRepo;
    private readonly EpisodesRepository _episodesRepo;
    private readonly SeriesRepository _seriesRepo;

    // コピー元シリーズ・エピソード一覧をキャッシュ
    private List<Series> _allSeries = new();
    private List<Episode> _srcEpisodes = new();
    private List<Episode> _dstEpisodes = new();

    public EpisodeThemeSongRangeCopyDialog(
        EpisodeThemeSongsRepository etsRepo,
        EpisodesRepository episodesRepo,
        SeriesRepository seriesRepo)
    {
        _etsRepo = etsRepo ?? throw new ArgumentNullException(nameof(etsRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

        InitializeComponent();

        // 各コンボの SelectedIndexChanged にハンドラを結線
        cboSrcSeries.SelectedIndexChanged += async (_, __) => await OnSrcSeriesChangedAsync();
        cboSrcEpisode.SelectedIndexChanged += (_, __) => UpdatePreview();
        cboDstSeries.SelectedIndexChanged += async (_, __) => await OnDstSeriesChangedAsync();
        numDstFrom.ValueChanged += (_, __) => UpdatePreview();
        numDstTo.ValueChanged += (_, __) => UpdatePreview();
        chkOverwrite.CheckedChanged += (_, __) => UpdatePreview();
        chkIncludeBroadcastOnly.CheckedChanged += (_, __) => UpdatePreview();

        btnExecute.Click += async (_, __) => await OnExecuteAsync();
        btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

        Load += async (_, __) => await OnLoadAsync();
    }

    private async Task OnLoadAsync()
    {
        try
        {
            _allSeries = (await _seriesRepo.GetAllAsync()).ToList();

            cboSrcSeries.DisplayMember = "Label";
            cboSrcSeries.ValueMember = "Id";
            cboSrcSeries.DataSource = _allSeries.Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}")).ToList();

            cboDstSeries.DisplayMember = "Label";
            cboDstSeries.ValueMember = "Id";
            cboDstSeries.DataSource = _allSeries.Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}")).ToList();

            // 初期値はコピー元シリーズと同じ（同シリーズ内範囲コピーが最も多い想定）
            await OnSrcSeriesChangedAsync();
            await OnDstSeriesChangedAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task OnSrcSeriesChangedAsync()
    {
        try
        {
            if (cboSrcSeries.SelectedValue is not int seriesId) return;
            _srcEpisodes = (await _episodesRepo.GetBySeriesAsync(seriesId)).ToList();
            cboSrcEpisode.DisplayMember = "Label";
            cboSrcEpisode.ValueMember = "Id";
            cboSrcEpisode.DataSource = _srcEpisodes
                .Select(ep => new IdLabel(ep.EpisodeId,
                    $"#{ep.SeriesEpNo}  {ep.TitleText}"))
                .ToList();
            UpdatePreview();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task OnDstSeriesChangedAsync()
    {
        try
        {
            if (cboDstSeries.SelectedValue is not int seriesId) return;
            _dstEpisodes = (await _episodesRepo.GetBySeriesAsync(seriesId)).ToList();
            // 範囲指定の最小値・最大値を実話数の最小・最大に揃える
            if (_dstEpisodes.Count > 0)
            {
                int minNo = _dstEpisodes.Min(ep => ep.SeriesEpNo);
                int maxNo = _dstEpisodes.Max(ep => ep.SeriesEpNo);
                numDstFrom.Minimum = minNo;
                numDstFrom.Maximum = maxNo;
                numDstTo.Minimum = minNo;
                numDstTo.Maximum = maxNo;
                // 既定値：コピー元と同じシリーズなら、コピー元の次話〜最終話を提示
                if (cboSrcSeries.SelectedValue is int srcSeriesId && srcSeriesId == seriesId
                    && cboSrcEpisode.SelectedValue is int srcEpId)
                {
                    var srcEp = _srcEpisodes.FirstOrDefault(ep => ep.EpisodeId == srcEpId);
                    if (srcEp is not null)
                    {
                        int from = Math.Min(srcEp.SeriesEpNo + 1, maxNo);
                        numDstFrom.Value = from;
                        numDstTo.Value = maxNo;
                    }
                }
                else
                {
                    numDstFrom.Value = minNo;
                    numDstTo.Value = maxNo;
                }
            }
            UpdatePreview();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>プレビュー欄の更新。コピー元の主題歌件数と、範囲内の各話の処理予定を表示する。</summary>
    private async void UpdatePreview()
    {
        try
        {
            txtPreview.Clear();
            if (cboSrcEpisode.SelectedValue is not int srcEpId) return;
            // コピー元の主題歌を取得（既定行 + 必要なら本放送限定行）
            var allSrcRows = (await _etsRepo.GetByEpisodeAsync(srcEpId)).ToList();
            var srcRows = chkIncludeBroadcastOnly.Checked
                ? allSrcRows
                : allSrcRows.Where(r => !r.IsBroadcastOnly).ToList();

            int from = (int)numDstFrom.Value;
            int to = (int)numDstTo.Value;
            if (from > to) (from, to) = (to, from);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"コピー元主題歌: {srcRows.Count} 件 "
                + $"（OP={srcRows.Count(r => r.ThemeKind == "OP")}, "
                + $"ED={srcRows.Count(r => r.ThemeKind == "ED")}, "
                + $"INSERT={srcRows.Count(r => r.ThemeKind == "INSERT")}）");
            sb.AppendLine();
            sb.AppendLine($"コピー先範囲: 第 {from} 話 ～ 第 {to} 話");
            sb.AppendLine();

            int targetCount = 0;
            int skippedCount = 0;
            foreach (var ep in _dstEpisodes.Where(ep => ep.SeriesEpNo >= from && ep.SeriesEpNo <= to)
                                            .OrderBy(ep => ep.SeriesEpNo))
            {
                // コピー元と同じエピソードはスキップ（自分自身に上書きする意味がない）
                if (ep.EpisodeId == srcEpId)
                {
                    sb.AppendLine($"  第 {ep.SeriesEpNo} 話 ※コピー元と同一のためスキップ");
                    skippedCount++;
                    continue;
                }
                targetCount++;
                sb.AppendLine($"  第 {ep.SeriesEpNo} 話 {ep.TitleText}: {srcRows.Count} 件投入予定"
                    + (chkOverwrite.Checked ? "（上書き）" : "（衝突時スキップ）"));
            }
            sb.AppendLine();
            sb.AppendLine($"処理対象 {targetCount} 話 / コピー元同一スキップ {skippedCount} 話");

            txtPreview.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            txtPreview.Text = $"プレビュー生成中にエラー: {ex.Message}";
        }
    }

    /// <summary>実行ボタン処理：コピー元の主題歌を範囲内の各話に投入する。</summary>
    private async Task OnExecuteAsync()
    {
        try
        {
            if (cboSrcEpisode.SelectedValue is not int srcEpId)
            {
                MessageBox.Show(this, "コピー元エピソードを選択してください。", "確認");
                return;
            }
            int from = (int)numDstFrom.Value;
            int to = (int)numDstTo.Value;
            if (from > to) (from, to) = (to, from);

            var allSrcRows = (await _etsRepo.GetByEpisodeAsync(srcEpId)).ToList();
            var srcRows = chkIncludeBroadcastOnly.Checked
                ? allSrcRows
                : allSrcRows.Where(r => !r.IsBroadcastOnly).ToList();

            if (srcRows.Count == 0)
            {
                MessageBox.Show(this, "コピー元エピソードに主題歌行がありません。", "確認");
                return;
            }

            var dstTargets = _dstEpisodes
                .Where(ep => ep.SeriesEpNo >= from && ep.SeriesEpNo <= to)
                .Where(ep => ep.EpisodeId != srcEpId) // 自分自身はスキップ
                .ToList();

            if (dstTargets.Count == 0)
            {
                MessageBox.Show(this, "コピー先範囲に該当するエピソードがありません。", "確認");
                return;
            }

            if (MessageBox.Show(this,
                $"コピー元の主題歌 {srcRows.Count} 件を {dstTargets.Count} 話に投入します。\n"
                + (chkOverwrite.Checked ? "※ 既存の主題歌行は上書きされます。" : "※ 既存の主題歌行があるエピソードはスキップします。")
                + "\n\n実行しますか？",
                "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            // 各エピソードについて：
            // - overwrite=true なら BulkUpsertAsync で UPSERT
            // - overwrite=false で既に主題歌行があるエピソードはスキップ、無ければ BulkUpsertAsync
            string user = Environment.UserName;
            int processedEpisodes = 0;
            int skippedEpisodes = 0;
            int totalRowsInserted = 0;
            foreach (var dstEp in dstTargets)
            {
                if (!chkOverwrite.Checked)
                {
                    var existing = await _etsRepo.GetByEpisodeAsync(dstEp.EpisodeId);
                    // 既存行が 1 件でもあればスキップ（上書きしない方針）
                    if (existing.Count > 0)
                    {
                        skippedEpisodes++;
                        continue;
                    }
                }
                // コピー元の各行を新エピソード ID で複製
                var clonedRows = srcRows.Select(r => new EpisodeThemeSong
                {
                    EpisodeId = dstEp.EpisodeId,
                    IsBroadcastOnly = r.IsBroadcastOnly,
                    ThemeKind = r.ThemeKind,
                    Seq = r.Seq,
                    SongRecordingId = r.SongRecordingId,
                    Notes = r.Notes,
                    CreatedBy = user,
                    UpdatedBy = user
                }).ToList();
                await _etsRepo.BulkUpsertAsync(clonedRows, user);
                processedEpisodes++;
                totalRowsInserted += clonedRows.Count;
            }

            MessageBox.Show(this,
                $"範囲コピーが完了しました。\n\n"
                + $"  処理対象エピソード: {processedEpisodes} 話\n"
                + $"  既存ありスキップ: {skippedEpisodes} 話\n"
                + $"  投入行数合計: {totalRowsInserted} 件",
                "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex)
    {
        MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>シンプルな (Id, Label) ペア。WinForms ComboBox の DataSource に使うため。</summary>
    private sealed record IdLabel(int Id, string Label);
}