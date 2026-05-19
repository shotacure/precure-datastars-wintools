using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>
/// 映画作品の BGM リストを編集するフォーム。
/// <see cref="BgmCuesEditorForm"/>（TV シリーズのセッション制・劇伴専用）とは別概念で、
/// こちらは映画専用の <c>movie_bgm_cues</c> を扱う。映画にはセッション・パートの概念が
/// 無く、その映画固有の M ナンバー文字列・順序（seq）・サブ順序（sub_seq）・区分
/// （<c>track_content_kinds</c> 共用）・未使用/欠番フラグのみを編集する。
/// 紐づけ先シリーズは映画系 kind（MOVIE / MOVIE_SHORT / SPRING / EVENT）のみを
/// コンボに出す（DB 側トリガーでも担保されるが、操作ミス防止のため UI でも絞る）。
/// </summary>
public partial class MovieBgmCuesEditorForm : Form
{
    private readonly MovieBgmCuesRepository _movieBgmCuesRepo;
    private readonly SeriesRepository _seriesRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;

    // movie_bgm_cues の series_id に許容されるシリーズ種別（DB トリガーと同一集合）。
    private static readonly string[] MovieKindCodes = { "MOVIE", "MOVIE_SHORT", "SPRING", "EVENT" };

    // 現在グリッドに表示中のシリーズ ID（保存・再読込で使う）。
    private int _currentSeriesId = 0;

    /// <summary><see cref="MovieBgmCuesEditorForm"/> を生成する。</summary>
    public MovieBgmCuesEditorForm(
        MovieBgmCuesRepository movieBgmCuesRepo,
        SeriesRepository seriesRepo,
        TrackContentKindsRepository trackContentKindsRepo)
    {
        _movieBgmCuesRepo = movieBgmCuesRepo ?? throw new ArgumentNullException(nameof(movieBgmCuesRepo));
        _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
        _trackContentKindsRepo = trackContentKindsRepo ?? throw new ArgumentNullException(nameof(trackContentKindsRepo));

        InitializeComponent();

        cboSeries.SelectedIndexChanged += async (_, __) => await ReloadGridAsync();
        btnAddRow.Click += (_, __) => AddBlankRow();
        btnSave.Click += async (_, __) => await SaveAsync();
        btnDelete.Click += async (_, __) => await DeleteSelectedAsync();
        btnClose.Click += (_, __) => Close();

        Load += async (_, __) => await InitAsync();
    }

    /// <summary>シリーズコンボ（映画系のみ）と区分コンボのデータ源を構築する。</summary>
    private async Task InitAsync()
    {
        try
        {
            var allSeries = await _seriesRepo.GetAllAsync();
            var movieSeries = allSeries
                .Where(s => MovieKindCodes.Contains(s.KindCode))
                .OrderBy(s => s.SeriesId)
                .Select(s => new IdLabel(s.SeriesId, $"#{s.SeriesId}  {s.Title}"))
                .ToList();
            cboSeries.DisplayMember = nameof(IdLabel.Label);
            cboSeries.ValueMember = nameof(IdLabel.Id);
            cboSeries.DataSource = movieSeries;

            var kinds = await _trackContentKindsRepo.GetAllAsync();
            // グリッドの区分コンボ列にバインドする選択肢（kind_code 値、日本語表示）。
            var kindItems = kinds
                .Select(k => new IdLabelStr(k.KindCode, k.NameJa))
                .ToList();
            colKind.DataSource = kindItems;
            colKind.DisplayMember = nameof(IdLabelStr.Label);
            colKind.ValueMember = nameof(IdLabelStr.Id);

            if (movieSeries.Count > 0)
                await ReloadGridAsync();
            else
                lblStatus.Text = "映画系シリーズ（MOVIE / MOVIE_SHORT / SPRING / EVENT）が登録されていません。";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択中シリーズの映画 BGM キューをグリッドへ読み込む。</summary>
    private async Task ReloadGridAsync()
    {
        try
        {
            if (cboSeries.SelectedValue is not int seriesId)
            {
                gridCues.DataSource = null;
                _currentSeriesId = 0;
                return;
            }
            _currentSeriesId = seriesId;

            var rows = await _movieBgmCuesRepo.GetBySeriesAsync(seriesId);
            // グリッドは BindingList でラップして行追加・編集を可能にする。
            var binding = new System.ComponentModel.BindingList<MovieBgmCue>(rows.ToList());
            gridCues.DataSource = binding;

            lblStatus.Text = $"#{seriesId} の映画 BGM：{rows.Count} 件";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>空の新規行を 1 行追加する（保存時に INSERT 対象になる）。</summary>
    private void AddBlankRow()
    {
        if (_currentSeriesId <= 0)
        {
            MessageBox.Show(this, "先に映画シリーズを選択してください。");
            return;
        }
        if (gridCues.DataSource is not System.ComponentModel.BindingList<MovieBgmCue> list)
        {
            MessageBox.Show(this, "グリッドが未初期化です。シリーズを選択してください。");
            return;
        }

        // 末尾 seq + 1 を暫定で振る（0 は使わない。後で手編集も可）。
        int nextSeq = list.Count == 0 ? 1 : list.Max(r => r.Seq) + 1;
        list.Add(new MovieBgmCue
        {
            MovieBgmCueId = 0,
            SeriesId = _currentSeriesId,
            Seq = nextSeq,
            SubSeq = 0,
            ContentKindCode = "BGM",
            IsUnused = false,
            IsMissing = false,
        });
    }

    /// <summary>グリッドの全行を保存する。MovieBgmCueId == 0 は INSERT、それ以外は UPDATE。 未使用・欠番の排他は DB 側 CHECK でも担保されるが、UI でも事前に弾く。</summary>
    private async Task SaveAsync()
    {
        try
        {
            if (gridCues.DataSource is not System.ComponentModel.BindingList<MovieBgmCue> list)
            {
                MessageBox.Show(this, "保存対象がありません。");
                return;
            }

            // 排他チェック（未使用かつ欠番は不可）。
            var conflict = list.FirstOrDefault(r => r.IsUnused && r.IsMissing);
            if (conflict is not null)
            {
                MessageBox.Show(this,
                    $"seq={conflict.Seq} の行で「未使用」と「欠番」が同時に設定されています。"
                    + "音源があるのに存在しない、は矛盾するため両立できません。",
                    "保存できません", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(this,
                    $"{list.Count} 件を保存します。よろしいですか？",
                    "保存確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                != DialogResult.OK)
            {
                return;
            }

            int inserted = 0, updated = 0;
            foreach (var cue in list)
            {
                // 念のためシリーズ ID を現在選択へ強制（グリッドからは触らせない）。
                cue.SeriesId = _currentSeriesId;
                cue.UpdatedBy = Environment.UserName;

                if (cue.MovieBgmCueId == 0)
                {
                    cue.CreatedBy = Environment.UserName;
                    cue.MovieBgmCueId = await _movieBgmCuesRepo.InsertAsync(cue);
                    inserted++;
                }
                else
                {
                    await _movieBgmCuesRepo.UpdateAsync(cue);
                    updated++;
                }
            }

            await ReloadGridAsync();
            lblStatus.Text = $"保存しました（新規 {inserted} 件 / 更新 {updated} 件）。";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>選択行を論理削除する。</summary>
    private async Task DeleteSelectedAsync()
    {
        try
        {
            if (gridCues.CurrentRow?.DataBoundItem is not MovieBgmCue cue)
            {
                MessageBox.Show(this, "削除対象の行を選択してください。");
                return;
            }
            if (cue.MovieBgmCueId == 0)
            {
                // 未保存の新規行はグリッドから外すだけ。
                if (gridCues.DataSource is System.ComponentModel.BindingList<MovieBgmCue> list)
                    list.Remove(cue);
                return;
            }
            if (MessageBox.Show(this,
                    $"映画 BGM #{cue.MovieBgmCueId}（seq={cue.Seq}）を論理削除しますか？",
                    "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                != DialogResult.OK)
            {
                return;
            }

            await _movieBgmCuesRepo.SoftDeleteAsync(cue.MovieBgmCueId, Environment.UserName);
            await ReloadGridAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>例外をダイアログ表示する（他フォームと同じ流儀）。</summary>
    private void ShowError(Exception ex)
    {
        lblStatus.Text = "エラーが発生しました。";
        MessageBox.Show(this, ex.Message, "エラー",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>int 値 + 表示名のコンボ用アイテム（シリーズ選択）。</summary>
    private sealed record IdLabel(int Id, string Label);

    /// <summary>string 値 + 表示名のコンボ用アイテム（区分 kind_code）。</summary>
    private sealed record IdLabelStr(string Id, string Label);
}
