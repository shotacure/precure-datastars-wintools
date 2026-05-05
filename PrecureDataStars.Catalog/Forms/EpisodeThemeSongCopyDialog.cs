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
///   <item><description>[1] コピー元のシリーズ・エピソード・読み込む行（フラグ 0 / 1 のチェック）を
///   選んで「読み込み」（DB は読むだけ）。</description></item>
///   <item><description>[2] コピー先のシリーズ・話数範囲（from/to）・本放送フラグの扱い
///   （コピー元のフラグを保つ／全行を全媒体共通に／全行を本放送限定に）を
///   選んで「プレビュー生成」（DB は触らない）。</description></item>
///   <item><description>[3] プレビューを行単位で編集・除外して「すべて保存」で初めて
///   <see cref="EpisodeThemeSongsRepository.BulkUpsertAsync"/> がトランザクションで走る。</description></item>
/// </list>
/// </para>
/// <para>
/// 仕様の要点（v1.2.0 工程 B' 設計判断）：
/// <list type="bullet">
///   <item><description>本放送と Blu-ray・配信で同じ主題歌が大半を占めるため、既定の
///   <c>is_broadcast_only=0</c> 行が「全媒体共通」を表す。本放送だけ例外的に異なる場合のみ
///   <c>is_broadcast_only=1</c> の行を別途立てる運用とする。</description></item>
///   <item><description>コピー元で両方のチェックを有効にし、コピー先で「コピー元と同じ」を選ぶ場合は
///   フラグの違いがそのまま保たれる。コピー先で「全行を全媒体共通に」「全行を本放送限定に」を
///   選ぶと、コピー元のフラグを無視してすべての行のフラグが書き換わる
///   （ただし PK 衝突を避けるため、両方読み込んだ状態で全媒体共通に倒すと同じキーの行が複数
///   生成される可能性があるので、生成後はプレビュー段階で警告される）。</description></item>
///   <item><description>プレビューは保存前ステージング。<see cref="DialogResult.OK"/> でクローズすると
///   親フォーム側でグリッド再読込が走る。</description></item>
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

        // イベント結線
        cboSrcSeries.SelectedIndexChanged += async (_, __) => await ReloadSrcEpisodesAsync();
        cboTgtSeries.SelectedIndexChanged += async (_, __) => await ReloadTgtEpisodesAsync();

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
    /// 「コピー元を読み込み」ボタン：選択中の (シリーズ, エピソード) で DB から行を取得し、
    /// コピー元チェックボックス（全媒体共通行 / 本放送限定行）の指定に従って絞り込む。
    /// </summary>
    private async Task LoadSourceAsync()
    {
        try
        {
            if (cboSrcEpisode.SelectedValue is not int episodeId)
            { MessageBox.Show(this, "コピー元のエピソードを選択してください。"); return; }
            if (!chkSrcLoadCommon.Checked && !chkSrcLoadBroadcastOnly.Checked)
            {
                MessageBox.Show(this, "「全媒体共通行」と「本放送限定行」のどちらか（または両方）を選択してください。");
                return;
            }

            // フラグ別に取得して結合（重複は PK が異なるので発生しない）
            var loaded = new List<EpisodeThemeSong>();
            if (chkSrcLoadCommon.Checked)
            {
                loaded.AddRange(await _etsRepo.GetByEpisodeAndFlagAsync(episodeId, isBroadcastOnly: false));
            }
            if (chkSrcLoadBroadcastOnly.Checked)
            {
                loaded.AddRange(await _etsRepo.GetByEpisodeAndFlagAsync(episodeId, isBroadcastOnly: true));
            }
            _sourceRows = loaded;

            int common = _sourceRows.Count(r => !r.IsBroadcastOnly);
            int broad  = _sourceRows.Count(r =>  r.IsBroadcastOnly);
            lblSrcStatus.Text = $"{_sourceRows.Count} 行を読み込みました（全媒体共通: {common} 行 / 本放送限定: {broad} 行）。";

            // 古いプレビューはクリア
            _previewRows = new BindingList<EpisodeThemeSong>();
            gridPreview.DataSource = _previewRows;
            ConfigurePreviewColumns();
            lblPreviewStatus.Text = "";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// 「プレビュー生成」ボタン：コピー元の内部スナップショット × コピー先のエピソード範囲 ×
    /// フラグオーバーライドを組み合わせてステージング行を生成する（DB は触らない）。
    /// 生成された行群に PK 衝突（同 episode + 同 flag + 同 theme + 同 seq の重複）が発生した
    /// 場合は警告を出す。
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
            if (cboTgtSeries.SelectedValue is not int)
            { MessageBox.Show(this, "コピー先のシリーズを選択してください。"); return; }

            int fromNo = Math.Min(fromEp.SeriesEpNo, toEp.SeriesEpNo);
            int toNo   = Math.Max(fromEp.SeriesEpNo, toEp.SeriesEpNo);

            var allTgtItems = (cboTgtEpFrom.DataSource as List<EpisodeItem>) ?? new List<EpisodeItem>();
            var tgtEpisodes = allTgtItems
                .Where(x => x.SeriesEpNo >= fromNo && x.SeriesEpNo <= toNo)
                .ToList();
            if (tgtEpisodes.Count == 0)
            { MessageBox.Show(this, "コピー先範囲にエピソードが見つかりません。"); return; }

            // フラグオーバーライドの方針を決定
            // null = コピー元のフラグを保つ、true = 全行を本放送限定に、false = 全行を全媒体共通に
            bool? flagOverride;
            if (rbTgtForceCommon.Checked) flagOverride = false;
            else if (rbTgtForceBroadcastOnly.Checked) flagOverride = true;
            else flagOverride = null;

            string user = Environment.UserName;
            var preview = new BindingList<EpisodeThemeSong>();
            foreach (var tgtEp in tgtEpisodes)
            {
                foreach (var src in _sourceRows)
                {
                    bool newFlag = flagOverride ?? src.IsBroadcastOnly;
                    preview.Add(new EpisodeThemeSong
                    {
                        EpisodeId = tgtEp.EpisodeId,
                        IsBroadcastOnly = newFlag,
                        ThemeKind = src.ThemeKind,
                        InsertSeq = src.InsertSeq,
                        SongRecordingId = src.SongRecordingId,
                        // LabelCompanyAliasId は v1.2.0 工程 H 補修で撤去済み（クレジット側で COMPANY エントリとして保持）。
                        Notes = src.Notes,
                        CreatedBy = user,
                        UpdatedBy = user
                    });
                }
            }
            _previewRows = preview;
            gridPreview.DataSource = _previewRows;
            ConfigurePreviewColumns();

            // PK 衝突チェック（保存前に警告のみ。実 INSERT は ON DUPLICATE KEY で吸収されるが、
            // 意図せぬ上書きを防ぐためユーザーに気付かせる）
            int dupCount = preview
                .GroupBy(r => (r.EpisodeId, r.IsBroadcastOnly, r.ThemeKind, r.InsertSeq))
                .Count(g => g.Count() > 1);
            string dupMsg = dupCount > 0 ? $"  ⚠ プレビュー内に {dupCount} 組の PK 重複あり（保存時は後勝ち）" : "";
            lblPreviewStatus.Text = $"{preview.Count} 行を生成しました（保存前のため DB は未変更）。{dupMsg}";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>プレビュー列の表示設定。監査列は非表示にし、必要列を編集可能にする。</summary>
    private void ConfigurePreviewColumns()
    {
        gridPreview.AutoGenerateColumns = true;
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
                    col.ReadOnly = false;
                    break;
                case nameof(EpisodeThemeSong.IsBroadcastOnly):
                    col.HeaderText = "本放送限定";
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
                // LabelCompanyAliasId 列は v1.2.0 工程 H 補修で撤去済み。
            }
        }
    }

    /// <summary>選択行をプレビューから除外する（DB 影響なし、ステージング操作のみ）。</summary>
    private void RemoveSelectedFromPreview()
    {
        try
        {
            if (gridPreview.SelectedRows.Count == 0) return;
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
