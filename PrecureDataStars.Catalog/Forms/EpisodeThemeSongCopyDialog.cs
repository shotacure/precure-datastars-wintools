using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// エピソード主題歌の「他話からコピー」ダイアログ（v1.2.0 工程 B' 追加）。
/// <para>
/// ユーザーは 3 段階の操作で他のエピソードの主題歌設定を任意の話数範囲にまとめて
/// 反映できる：
/// <list type="number">
///   <item><description>[1] コピー元のシリーズ・エピソード・リリース文脈を選んで「読み込み」（DB は読むだけ）。</description></item>
///   <item><description>[2] コピー先のシリーズ・話数範囲（from/to）・リリース文脈を選んで「プレビュー生成」（DB は触らない）。</description></item>
///   <item><description>[3] プレビューを行単位で編集・除外して「すべて保存」で初めて
///   <see cref="EpisodeThemeSongsRepository.BulkUpsertAsync"/> がトランザクションで走る。</description></item>
/// </list>
/// </para>
/// <para>
/// 仕様の要点（v1.2.0 工程 B' 設計判断）：
/// <list type="bullet">
///   <item><description>コピー元のリリース文脈で「全て」を選んだ場合、コピー先のリリース文脈は
///   強制的に「コピー元と同じ」固定とし、UI 上は無効化する。これは複数の文脈を持つ
///   コピー元を 1 つの文脈に潰すと PK 衝突が発生しうるため。</description></item>
///   <item><description>コピー先のエピソード範囲が複数話におよぶ場合、コピー元の各 (release_context,
///   theme_kind, insert_seq) を範囲内の全エピソードに同様に適用する。</description></item>
///   <item><description>プレビューは保存前ステージング。<see cref="DialogResult.OK"/> でクローズすると
///   親フォーム側でグリッド再読込が走る（呼び出し側が ReloadEpisodeThemeSongsAsync を呼ぶ）。</description></item>
/// </list>
/// </para>
/// </summary>
public partial class EpisodeThemeSongCopyDialog : Form
{
    private readonly EpisodeThemeSongsRepository _etsRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly EpisodesRepository _episodesRepo;

    /// <summary>コピー元として読み込んだ行群（DB から取得した素のスナップショット）。</summary>
    private List<EpisodeThemeSong> _sourceRows = new();

    /// <summary>プレビュー（編集対象）行群。<see cref="BindingList{T}"/> でグリッドにバインドする。</summary>
    private BindingList<EpisodeThemeSong> _previewRows = new();

    public EpisodeThemeSongCopyDialog(
        EpisodeThemeSongsRepository etsRepo,
        SeriesRepository seriesRepo,
        EpisodesRepository episodesRepo)
    {
        _etsRepo = etsRepo ?? throw new ArgumentNullException(nameof(etsRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _episodesRepo = episodesRepo ?? throw new ArgumentNullException(nameof(episodesRepo));

        InitializeComponent();

        // 初期既定
        cboSrcContext.SelectedItem = "全て";
        cboTgtContext.SelectedItem = "コピー元と同じ";

        // イベント結線
        cboSrcSeries.SelectedIndexChanged += async (_, __) => await ReloadSrcEpisodesAsync();
        cboTgtSeries.SelectedIndexChanged += async (_, __) => await ReloadTgtEpisodesAsync();
        cboSrcContext.SelectedIndexChanged += (_, __) => OnSrcContextChanged();

        btnLoadSrc.Click += async (_, __) => await LoadSourceAsync();
        btnGeneratePreview.Click += (_, __) => GeneratePreview();
        btnRemoveSelected.Click += (_, __) => RemoveSelectedFromPreview();
        btnSaveAll.Click += async (_, __) => await SaveAllAsync();

        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>
    /// ダイアログ初期化：シリーズ一覧をコピー元・コピー先の両コンボに流し込む。
    /// </summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var allSeries = await _seriesRepo.GetAllAsync();
            var items = allSeries
                .Select(s => new SeriesItem(s.SeriesId, $"#{s.SeriesId}  {s.Title}"))
                .ToList();
            cboSrcSeries.DisplayMember = "Label";
            cboSrcSeries.ValueMember = "Id";
            cboSrcSeries.DataSource = new List<SeriesItem>(items);

            cboTgtSeries.DisplayMember = "Label";
            cboTgtSeries.ValueMember = "Id";
            cboTgtSeries.DataSource = new List<SeriesItem>(items);

            // プレビュー列の初期化（編集可制御は GeneratePreview で再適用）
            ConfigurePreviewColumns();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// コピー元シリーズ変更時：当該シリーズのエピソードリストをコピー元エピソードコンボに反映。
    /// </summary>
    private async Task ReloadSrcEpisodesAsync()
    {
        try
        {
            if (cboSrcSeries.SelectedValue is not int seriesId) return;
            var eps = await _episodesRepo.GetBySeriesAsync(seriesId);
            cboSrcEpisode.DisplayMember = "Label";
            cboSrcEpisode.ValueMember = "Id";
            cboSrcEpisode.DataSource = eps
                .Select(e => new EpisodeItem(e.EpisodeId, e.SeriesEpNo, $"第{e.SeriesEpNo}話  {e.TitleText}"))
                .ToList();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// コピー先シリーズ変更時：エピソード範囲コンボ（from / to）にエピソード一覧を反映。
    /// </summary>
    private async Task ReloadTgtEpisodesAsync()
    {
        try
        {
            if (cboTgtSeries.SelectedValue is not int seriesId) return;
            var eps = await _episodesRepo.GetBySeriesAsync(seriesId);
            var items = eps
                .Select(e => new EpisodeItem(e.EpisodeId, e.SeriesEpNo, $"第{e.SeriesEpNo}話  {e.TitleText}"))
                .ToList();
            cboTgtEpFrom.DisplayMember = "Label";
            cboTgtEpFrom.ValueMember = "Id";
            cboTgtEpFrom.DataSource = new List<EpisodeItem>(items);

            cboTgtEpTo.DisplayMember = "Label";
            cboTgtEpTo.ValueMember = "Id";
            cboTgtEpTo.DataSource = new List<EpisodeItem>(items);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// コピー元のリリース文脈変更時：「全て」が選ばれた場合は、コピー先のリリース文脈を
    /// 「コピー元と同じ」固定にして無効化する。これは PK 衝突を未然に防ぐため。
    /// </summary>
    private void OnSrcContextChanged()
    {
        var src = (cboSrcContext.SelectedItem as string) ?? "全て";
        if (src == "全て")
        {
            cboTgtContext.SelectedItem = "コピー元と同じ";
            cboTgtContext.Enabled = false;
        }
        else
        {
            cboTgtContext.Enabled = true;
        }
    }

    /// <summary>
    /// 「コピー元を読み込み」ボタン：選択中の (シリーズ, エピソード, 文脈) で DB から
    /// 行を取得して内部リストに格納する（プレビュー生成や保存には DB を再ヒットしない）。
    /// </summary>
    private async Task LoadSourceAsync()
    {
        try
        {
            if (cboSrcEpisode.SelectedValue is not int episodeId)
            { MessageBox.Show(this, "コピー元のエピソードを選択してください。"); return; }

            var srcContext = (cboSrcContext.SelectedItem as string) ?? "全て";
            if (srcContext == "全て")
            {
                _sourceRows = (await _etsRepo.GetByEpisodeAsync(episodeId)).ToList();
            }
            else
            {
                _sourceRows = (await _etsRepo.GetByEpisodeAndContextAsync(episodeId, srcContext)).ToList();
            }
            lblSrcStatus.Text = $"{_sourceRows.Count} 行を読み込みました。";
            // 読み込み直後は古いプレビューをクリア
            _previewRows = new BindingList<EpisodeThemeSong>();
            gridPreview.DataSource = _previewRows;
            ConfigurePreviewColumns();
            lblPreviewStatus.Text = "";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「プレビュー生成」ボタン：コピー元の内部スナップショット × コピー先のエピソード範囲 ×
    /// 文脈オーバーライド を組み合わせて、ステージング行を生成する（DB は触らない）。
    /// </summary>
    private void GeneratePreview()
    {
        try
        {
            if (_sourceRows.Count == 0)
            { MessageBox.Show(this, "先にコピー元を読み込んでください。"); return; }
            if (cboTgtEpFrom.SelectedItem is not EpisodeItem fromEp ||
                cboTgtEpTo.SelectedItem is not EpisodeItem toEp)
            { MessageBox.Show(this, "コピー先の話数範囲を選択してください。"); return; }
            if (cboTgtSeries.SelectedValue is not int tgtSeriesId)
            { MessageBox.Show(this, "コピー先のシリーズを選択してください。"); return; }

            // 範囲が逆向きの場合はスワップ
            int fromNo = Math.Min(fromEp.SeriesEpNo, toEp.SeriesEpNo);
            int toNo   = Math.Max(fromEp.SeriesEpNo, toEp.SeriesEpNo);

            // コピー先シリーズ全エピソードから範囲内のものを取得
            // （cboTgtEpFrom の DataSource を再利用するため再フェッチは省略）
            var allTgtItems = (cboTgtEpFrom.DataSource as List<EpisodeItem>)
                ?? new List<EpisodeItem>();
            var tgtEpisodes = allTgtItems
                .Where(x => x.SeriesEpNo >= fromNo && x.SeriesEpNo <= toNo)
                .ToList();
            if (tgtEpisodes.Count == 0)
            { MessageBox.Show(this, "コピー先範囲にエピソードが見つかりません。"); return; }

            var tgtCtxOption = (cboTgtContext.SelectedItem as string) ?? "コピー元と同じ";
            string user = Environment.UserName;

            var preview = new BindingList<EpisodeThemeSong>();
            foreach (var tgtEp in tgtEpisodes)
            {
                foreach (var src in _sourceRows)
                {
                    string newCtx = tgtCtxOption == "コピー元と同じ" ? src.ReleaseContext : tgtCtxOption;
                    preview.Add(new EpisodeThemeSong
                    {
                        EpisodeId = tgtEp.EpisodeId,
                        ReleaseContext = newCtx,
                        ThemeKind = src.ThemeKind,
                        InsertSeq = src.InsertSeq,
                        SongRecordingId = src.SongRecordingId,
                        LabelCompanyAliasId = src.LabelCompanyAliasId,
                        Notes = src.Notes,
                        CreatedBy = user,
                        UpdatedBy = user
                    });
                }
            }
            _previewRows = preview;
            gridPreview.DataSource = _previewRows;
            ConfigurePreviewColumns();
            lblPreviewStatus.Text = $"{preview.Count} 行を生成しました（保存前のため DB は未変更）。";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>プレビュー列の表示設定。監査列は非表示にし、必要列を編集可能にする。</summary>
    private void ConfigurePreviewColumns()
    {
        // BindingList のスキーマで列が立つので、立った後に表示制御する
        gridPreview.AutoGenerateColumns = true;
        // DataSource 入れ替え後の列調整は遅延発火するため、現在の列を見て安全に隠す
        foreach (DataGridViewColumn col in gridPreview.Columns)
        {
            switch (col.Name)
            {
                case nameof(EpisodeThemeSong.CreatedAt):
                case nameof(EpisodeThemeSong.UpdatedAt):
                case nameof(EpisodeThemeSong.CreatedBy):
                case nameof(EpisodeThemeSong.UpdatedBy):
                    col.Visible = false;
                    break;
                case nameof(EpisodeThemeSong.EpisodeId):
                    col.HeaderText = "コピー先 episode_id";
                    col.ReadOnly = false; // 個別調整可
                    break;
                case nameof(EpisodeThemeSong.ReleaseContext):
                    col.HeaderText = "release_context";
                    break;
                case nameof(EpisodeThemeSong.ThemeKind):
                    col.HeaderText = "theme_kind";
                    break;
                case nameof(EpisodeThemeSong.InsertSeq):
                    col.HeaderText = "insert_seq";
                    break;
                case nameof(EpisodeThemeSong.SongRecordingId):
                    col.HeaderText = "song_recording_id";
                    break;
                case nameof(EpisodeThemeSong.LabelCompanyAliasId):
                    col.HeaderText = "label_company_alias_id";
                    break;
            }
        }
    }

    /// <summary>選択行をプレビューから除外する（DB 影響なし、ステージング操作のみ）。</summary>
    private void RemoveSelectedFromPreview()
    {
        try
        {
            if (gridPreview.SelectedRows.Count == 0) return;
            // 後ろから消すことでインデックスがずれないようにする
            var idx = gridPreview.SelectedRows
                .Cast<DataGridViewRow>()
                .Select(r => r.Index)
                .OrderByDescending(i => i)
                .ToList();
            foreach (var i in idx)
            {
                if (i >= 0 && i < _previewRows.Count) _previewRows.RemoveAt(i);
            }
            lblPreviewStatus.Text = $"{_previewRows.Count} 行 残（保存前）。";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「すべて保存」ボタン：確認ダイアログ → BulkUpsertAsync → 成功時 OK で閉じる。
    /// 失敗時はリポジトリ側でロールバック、ユーザーには例外メッセージを表示してダイアログは
    /// 開いたまま（プレビューはそのまま残るので再試行できる）。
    /// </summary>
    private async Task SaveAllAsync()
    {
        try
        {
            if (_previewRows.Count == 0)
            { MessageBox.Show(this, "保存対象の行がありません。"); return; }

            var msg = $"{_previewRows.Count} 行を保存します（既存行は UPSERT で上書きされます）。よろしいですか？";
            if (MessageBox.Show(this, msg, "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            await _etsRepo.BulkUpsertAsync(_previewRows.ToList(), Environment.UserName);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception ex)
        => MessageBox.Show(this, ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ─────────── コンボ用の小さな表示型 ───────────

    private sealed class SeriesItem
    {
        public int Id { get; }
        public string Label { get; }
        public SeriesItem(int id, string label) { Id = id; Label = label; }
        public override string ToString() => Label;
    }

    private sealed class EpisodeItem
    {
        public int Id => EpisodeId;
        public int EpisodeId { get; }
        public int SeriesEpNo { get; }
        public string Label { get; }
        public EpisodeItem(int episodeId, int seriesEpNo, string label)
        {
            EpisodeId = episodeId; SeriesEpNo = seriesEpNo; Label = label;
        }
        public override string ToString() => Label;
    }
}
