using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジット系マスタ管理フォームのエピソード主題歌タブ／シリーズ種別タブ／パート種別タブ群。（タブ単位の partial 分割。ロジックは本体と共通の部分クラス）</summary>
public partial class CreditMastersEditorForm
{
    // エピソード主題歌タブ

    private async Task ReloadEpisodesForEtsAsync()
    {
        try
        {
            if (cboEtsSeries.SelectedValue is not int seriesId) return;
            var eps = await _episodesRepo.GetBySeriesAsync(seriesId);
            cboEtsEpisode.DisplayMember = "Label";
            cboEtsEpisode.ValueMember = "Id";
            cboEtsEpisode.DataSource = eps
                .Select(e => new IdLabel<int>(e.EpisodeId, $"#{e.TotalEpNo ?? 0}  {e.TitleText}"))
                .ToList();
            if (eps.Count > 0) await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task ReloadEpisodeThemeSongsAsync()
    {
        try
        {
            if (cboEtsEpisode.SelectedValue is not int episodeId) return;
            gridEpisodeThemeSongs.DataSource = (await _episodeThemeSongsRepo.GetByEpisodeAsync(episodeId)).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private void OnEpisodeThemeSongRowSelected()
    {
        if (gridEpisodeThemeSongs.CurrentRow?.DataBoundItem is EpisodeThemeSong t)
        {
            // 行選択時に本放送限定フラグもチェックボックスに反映
            chkEtsBroadcastOnly.Checked = t.IsBroadcastOnly;
            cboEtsThemeKind.SelectedItem = t.ThemeKind;
            numEtsInsertSeq.Value = t.Seq;
            numEtsSongRecordingId.Value = t.SongRecordingId;
            txtEtsNotes.Text = t.Notes ?? "";
        }
    }

    private async Task SaveEpisodeThemeSongAsync()
    {
        try
        {
            if (cboEtsEpisode.SelectedValue is not int episodeId)
            { MessageBox.Show(this, "エピソードを選択してください。"); return; }
            // 本放送限定フラグはチェックボックスから取得
            bool isBroadcastOnly = chkEtsBroadcastOnly.Checked;
            string themeKind = (cboEtsThemeKind.SelectedItem as string) ?? "OP";
            byte seq = (byte)numEtsInsertSeq.Value;
            // seq は OP/ED/INSERT を区別しないエピソード単位の劇中順（特別な固定値制約は無い）。
            // 新仕様の seq は OP/ED/INSERT 区別なくエピソード内の劇中順（1, 2, 3, ...）を表す。
            // 0 が来た場合のみ最小値 1 にフォールバック（PK 重複を避ける程度のガード）。
            if (seq < 1) seq = 1;
            int songRecordingId = (int)numEtsSongRecordingId.Value;
            if (songRecordingId <= 0)
            { MessageBox.Show(this, "song_recording_id を指定してください。"); return; }

            var t = new EpisodeThemeSong
            {
                EpisodeId = episodeId,
                IsBroadcastOnly = isBroadcastOnly,
                ThemeKind = themeKind,
                Seq = seq,
                SongRecordingId = songRecordingId,
                Notes = NullIfEmpty(txtEtsNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _episodeThemeSongsRepo.UpsertAsync(t);
            await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteEpisodeThemeSongAsync()
    {
        try
        {
            if (gridEpisodeThemeSongs.CurrentRow?.DataBoundItem is not EpisodeThemeSong t)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            string flagLabel = t.IsBroadcastOnly ? "[本放送限定]" : "[全媒体共通]";
            if (MessageBox.Show(this,
                $"エピソード#{t.EpisodeId} {flagLabel} {t.ThemeKind}#{t.Seq} を削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            // PK が 4 列に変わったので is_broadcast_only も渡す
            await _episodeThemeSongsRepo.DeleteAsync(t.EpisodeId, t.IsBroadcastOnly, t.ThemeKind, t.Seq);
            await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>追加：他話からのコピーダイアログを開く。 ダイアログ側はプレビュー段階では DB 書き込みを行わず、「すべて保存」ボタンで初めて <see cref="EpisodeThemeSongsRepository.BulkUpsertAsync"/> をトランザクションで呼ぶ。</summary>
    private async Task OpenEtsCopyDialogAsync()
    {
        try
        {
            using var dlg = new EpisodeThemeSongCopyDialog(
                _episodeThemeSongsRepo, _seriesRepo, _episodesRepo);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // 保存が走った後はグリッドを最新化（現在表示中のエピソードと同じ場合は変化が見える）
                await ReloadEpisodeThemeSongsAsync();
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>範囲コピーダイアログを起動する。 1 話の主題歌を「シリーズ内の連続話数範囲（series_ep_no ベース）」の各エピソードに 一括投入する用途。例：1 話の OP / ED を 2 話〜49 話に同じ内容で流し込む、等。</summary>
    private async Task OpenEtsRangeCopyDialogAsync()
    {
        try
        {
            using var dlg = new EpisodeThemeSongRangeCopyDialog(
                _episodeThemeSongsRepo, _episodesRepo, _seriesRepo);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // 範囲コピー実行後、現在表示中のエピソードがコピー範囲内に含まれていれば
                // 値が更新されている可能性があるため、グリッドを最新化する。
                await ReloadEpisodeThemeSongsAsync();
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    // シリーズ種別タブ

    private void OnSeriesKindRowSelected()
    {
        if (gridSeriesKinds.CurrentRow?.DataBoundItem is SeriesKind k)
        {
            txtSkCode.Text = k.KindCode;
            txtSkNameJa.Text = k.NameJa;
            txtSkNameEn.Text = k.NameEn ?? "";
            cboSkAttachTo.SelectedItem = k.CreditAttachTo;
        }
    }

    private async Task SaveSeriesKindAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtSkCode.Text))
            { MessageBox.Show(this, "コードは必須です。"); return; }
            var k = new SeriesKind
            {
                KindCode = txtSkCode.Text.Trim(),
                NameJa = txtSkNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtSkNameEn.Text),
                CreditAttachTo = (cboSkAttachTo.SelectedItem as string) ?? "EPISODE",
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _seriesKindsRepo.UpsertAsync(k);
            gridSeriesKinds.DataSource = (await _seriesKindsRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteSeriesKindAsync()
    {
        try
        {
            if (gridSeriesKinds.CurrentRow?.DataBoundItem is not SeriesKind k)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"シリーズ種別 {k.KindCode} を削除しますか？（参照中なら失敗）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _seriesKindsRepo.DeleteAsync(k.KindCode);
            gridSeriesKinds.DataSource = (await _seriesKindsRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    // パート種別タブ

    private void OnPartTypeRowSelected()
    {
        if (gridPartTypes.CurrentRow?.DataBoundItem is PartType p)
        {
            txtPtCode.Text = p.PartTypeCode;
            txtPtNameJa.Text = p.NameJa;
            txtPtNameEn.Text = p.NameEn ?? "";
            numPtDisplayOrder.Value = p.DisplayOrder ?? 0;
            cboPtDefaultCreditKind.SelectedItem = p.DefaultCreditKind ?? "";
            chkPtSingleton.Checked = p.SingletonPerEpisode;
        }
    }

    private async Task SavePartTypeAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtPtCode.Text))
            { MessageBox.Show(this, "コードは必須です。"); return; }
            string? defaultKind = cboPtDefaultCreditKind.SelectedItem as string;
            if (string.IsNullOrEmpty(defaultKind)) defaultKind = null;

            var pt = new PartType
            {
                PartTypeCode = txtPtCode.Text.Trim(),
                NameJa = txtPtNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtPtNameEn.Text),
                DisplayOrder = numPtDisplayOrder.Value > 0 ? (byte)numPtDisplayOrder.Value : null,
                DefaultCreditKind = defaultKind,
                SingletonPerEpisode = chkPtSingleton.Checked,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _partTypesRepo.UpsertAsync(pt);
            gridPartTypes.DataSource = (await _partTypesRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeletePartTypeAsync()
    {
        try
        {
            if (gridPartTypes.CurrentRow?.DataBoundItem is not PartType p)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"パート種別 {p.PartTypeCode} を削除しますか？（参照中なら失敗）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _partTypesRepo.DeleteAsync(p.PartTypeCode);
            gridPartTypes.DataSource = (await _partTypesRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    // 主題歌タブの DnD
    // 同 (episode_id, is_broadcast_only, theme_kind='INSERT') グループ内のみ並べ替え可。
    // OP/ED 行は CHECK 制約 (ck_ets_op_ed_no_insert_seq) により insert_seq=0 固定で
    // 各グループに 1 行しか存在しないため、ドラッグ・ドロップとも対象外として扱う。

    private Rectangle _etsDragBoxFromMouseDown = Rectangle.Empty;
    private int _etsDragSourceIndex = -1;

    private void GridEts_MouseDown(object? sender, MouseEventArgs e)
    {
        var hit = gridEpisodeThemeSongs.HitTest(e.X, e.Y);
        if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0
            && gridEpisodeThemeSongs.Rows[hit.RowIndex].DataBoundItem is EpisodeThemeSong t
            && t.ThemeKind == "INSERT")
        {
            // INSERT 行のみ DnD 対象
            Size dragSize = SystemInformation.DragSize;
            _etsDragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
            _etsDragSourceIndex = hit.RowIndex;
        }
        else
        {
            _etsDragBoxFromMouseDown = Rectangle.Empty;
            _etsDragSourceIndex = -1;
        }
    }

    private void GridEts_MouseMove(object? sender, MouseEventArgs e)
    {
        if ((e.Button & MouseButtons.Left) == MouseButtons.Left
            && _etsDragBoxFromMouseDown != Rectangle.Empty
            && !_etsDragBoxFromMouseDown.Contains(e.X, e.Y)
            && _etsDragSourceIndex >= 0)
        {
            gridEpisodeThemeSongs.DoDragDrop(_etsDragSourceIndex, DragDropEffects.Move);
        }
    }

    private void GridEts_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetDataPresent(typeof(int)))
            e.Effect = DragDropEffects.Move;
        else
            e.Effect = DragDropEffects.None;
    }

    /// <summary>主題歌タブ DnD のドラッグオーバ判定。</summary>
    private void GridEts_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        if (e.Data is null || !e.Data.GetDataPresent(typeof(int))) return;
        int sourceIndex = (int)e.Data.GetData(typeof(int))!;
        if (sourceIndex < 0 || sourceIndex >= gridEpisodeThemeSongs.Rows.Count) return;

        var p = gridEpisodeThemeSongs.PointToClient(new Point(e.X, e.Y));
        var hit = gridEpisodeThemeSongs.HitTest(p.X, p.Y);
        if (hit.RowIndex < 0) return;
        if (gridEpisodeThemeSongs.Rows[sourceIndex].DataBoundItem is not EpisodeThemeSong src) return;
        if (gridEpisodeThemeSongs.Rows[hit.RowIndex].DataBoundItem is not EpisodeThemeSong tgt) return;

        // 同グループ判定：episode_id / is_broadcast_only / theme_kind='INSERT' が一致
        if (src.EpisodeId == tgt.EpisodeId
            && src.IsBroadcastOnly == tgt.IsBroadcastOnly
            && src.ThemeKind == "INSERT" && tgt.ThemeKind == "INSERT")
        {
            e.Effect = DragDropEffects.Move;
        }
    }

    /// <summary>主題歌タブ DnD のドロップ処理。</summary>
    private async Task GridEts_DragDropAsync(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data is null || !e.Data.GetDataPresent(typeof(int))) return;
            int sourceIndex = (int)e.Data.GetData(typeof(int))!;
            if (sourceIndex < 0) return;

            var p = gridEpisodeThemeSongs.PointToClient(new Point(e.X, e.Y));
            var hit = gridEpisodeThemeSongs.HitTest(p.X, p.Y);
            if (hit.RowIndex < 0) return;
            int targetIndex = hit.RowIndex;
            if (targetIndex == sourceIndex) return;

            if (gridEpisodeThemeSongs.Rows[sourceIndex].DataBoundItem is not EpisodeThemeSong src) return;
            if (gridEpisodeThemeSongs.Rows[targetIndex].DataBoundItem is not EpisodeThemeSong tgt) return;
            if (src.EpisodeId != tgt.EpisodeId
                || src.IsBroadcastOnly != tgt.IsBroadcastOnly
                || src.ThemeKind != "INSERT" || tgt.ThemeKind != "INSERT")
                return;

            var rowRect = gridEpisodeThemeSongs.GetRowDisplayRectangle(targetIndex, true);
            bool dropAbove = p.Y < rowRect.Top + rowRect.Height / 2;

            // 全件 DataSource から、対象グループ（INSERT のみ）の行を取り出して順序を組み替える
            if (gridEpisodeThemeSongs.DataSource is not List<EpisodeThemeSong> all)
            {
                MessageBox.Show(this, "主題歌一覧の取得に失敗しました（DataSource が想定外）。",
                    "DnD エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var sameGroup = all
                .Where(x => x.EpisodeId == src.EpisodeId
                         && x.IsBroadcastOnly == src.IsBroadcastOnly
                         && x.ThemeKind == "INSERT")
                .OrderBy(x => x.Seq)
                .ToList();
            int srcIdxInGroup = sameGroup.FindIndex(x => x.Seq == src.Seq);
            int tgtIdxInGroup = sameGroup.FindIndex(x => x.Seq == tgt.Seq);
            if (srcIdxInGroup < 0 || tgtIdxInGroup < 0) return;

            var srcEntity = sameGroup[srcIdxInGroup];
            sameGroup.RemoveAt(srcIdxInGroup);
            int adjustedTarget = (srcIdxInGroup < tgtIdxInGroup) ? tgtIdxInGroup - 1 : tgtIdxInGroup;
            int insertAt = dropAbove ? adjustedTarget : adjustedTarget + 1;
            if (insertAt < 0) insertAt = 0;
            if (insertAt > sameGroup.Count) insertAt = sameGroup.Count;
            sameGroup.Insert(insertAt, srcEntity);

            // DB 反映：当該グループのみ DELETE → 新順序で INSERT
            await SeqReorderHelper.ReorderEpisodeThemeSongsAsync(
                _episodeThemeSongsRepo, src.EpisodeId, src.IsBroadcastOnly, sameGroup);

            // 画面再ロード（既存メソッドを再利用）
            await ReloadEpisodeThemeSongsAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
        finally
        {
            _etsDragBoxFromMouseDown = Rectangle.Empty;
            _etsDragSourceIndex = -1;
        }
    }
}
