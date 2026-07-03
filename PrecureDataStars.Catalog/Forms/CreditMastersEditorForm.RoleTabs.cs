using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;

namespace PrecureDataStars.Catalog.Forms;

/// <summary>クレジット系マスタ管理フォームの役職タブ／役職テンプレートタブ群。（タブ単位の partial 分割。ロジックは本体と共通の部分クラス）</summary>
public partial class CreditMastersEditorForm
{
    // 役職タブ

    private void OnRoleRowSelected()
    {
        if (gridRoles.CurrentRow?.DataBoundItem is Role r)
        {
            txtRoleCode.Text = r.RoleCode;
            txtRoleNameJa.Text = r.NameJa;
            txtRoleNameEn.Text = r.NameEn ?? "";
            cboRoleFormatKind.SelectedItem = r.RoleFormatKind;
            // 書式テンプレは「役職テンプレート」タブで編集する。
            numRoleDisplayOrder.Value = r.DisplayOrder ?? 0;
            // 役職名非表示フラグの取り込み。
            chkRoleHideRoleNameInCredit.Checked = (r.HideRoleNameInCredit == 1);

            // [系譜…] ボタン（Designer.cs 側）の活性化と
            // 編集対象の更新。タグに現在行の役職コード／名称を入れる。
            btnEditRoleSuccessions.Tag = (RoleCode: r.RoleCode, RoleNameJa: r.NameJa);
            btnEditRoleSuccessions.Enabled = !string.IsNullOrWhiteSpace(r.RoleCode);
        }
    }

    /// <summary>[系譜...] ボタン（Designer.cs 側で正規定義）のクリックハンドラ。</summary>
    private async Task OnEditRoleSuccessionsClickAsync()
    {
        if (btnEditRoleSuccessions.Tag is not ValueTuple<string, string> tagTuple)
        {
            // フォールバック：Tag が未設定または型が想定外なら現在選択行から再取得を試みる。
            if (gridRoles.CurrentRow?.DataBoundItem is not Role r) return;
            tagTuple = (r.RoleCode, r.NameJa);
        }

        var (roleCode, roleNameJa) = tagTuple;
        if (string.IsNullOrWhiteSpace(roleCode)) return;

        try
        {
            using var dlg = new Forms.Dialogs.RoleSuccessionsEditorDialog(
                _rolesRepo, _roleSuccessionsRepo, roleCode, roleNameJa);
            dlg.ShowDialog(this);
            // 系譜は roles 本体には影響しないのでグリッド再描画は不要。
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            this.ShowError(ex);
        }
    }

    private async Task SaveRoleAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtRoleCode.Text))
            { MessageBox.Show(this, "コードは必須です。"); return; }
            if (string.IsNullOrWhiteSpace(txtRoleNameJa.Text))
            { MessageBox.Show(this, "名称(日)は必須です。"); return; }

            ushort? order = numRoleDisplayOrder.Value > 0 ? (ushort)numRoleDisplayOrder.Value : null;

            var r = new Role
            {
                RoleCode = txtRoleCode.Text.Trim(),
                NameJa = txtRoleNameJa.Text.Trim(),
                NameEn = NullIfEmpty(txtRoleNameEn.Text),
                RoleFormatKind = (cboRoleFormatKind.SelectedItem as string) ?? "NORMAL",
                // 役職の書式テンプレは role_templates テーブルで管理する。
                DisplayOrder = order,
                // チェック状態を 0/1 に変換して永続化。
                HideRoleNameInCredit = chkRoleHideRoleNameInCredit.Checked ? (byte)1 : (byte)0,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _rolesRepo.UpsertAsync(r);
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();

            // 「役職テンプレート」タブの役職コンボも追随
            cboOvRole.DataSource = (await _rolesRepo.GetAllAsync())
                .Select(x => new IdLabel<string>(x.RoleCode, $"{x.RoleCode}  {x.NameJa}"))
                .ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    private async Task DeleteRoleAsync()
    {
        try
        {
            if (gridRoles.CurrentRow?.DataBoundItem is not Role r)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            if (MessageBox.Show(this, $"役職 {r.RoleCode} を削除しますか？（参照されている場合は失敗します）", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _rolesRepo.DeleteAsync(r.RoleCode);
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    // 役職テンプレートタブ

    /// <summary>
    /// 上部の役職コンボ（cboOvSeries にフィールド名を流用）の選択変更時、または初回ロード時に
    /// role_templates から該当役職の全テンプレ（既定 + シリーズ別）をグリッドへロードする
    /// （再設計）。
    /// SelectedValue 経由は DataSource バインドのタイミング次第で null や型不一致になり得るため、
    /// SelectedItem を直接 IdLabel<string> にキャストして取得する方式に変更。
    /// </summary>
    private async Task ReloadRoleOverridesAsync()
    {
        try
        {
            if (cboOvSeries.SelectedItem is not IdLabel<string> sel || string.IsNullOrEmpty(sel.Id))
            {
                gridRoleOverrides.DataSource = null;
                return;
            }
            string roleCode = sel.Id;
            var rows = await _roleTemplatesRepo.GetByRoleAsync(roleCode);
            // 表示用 DTO（series 名付き）に変換してグリッドへ
            var seriesNameMap = (await _seriesRepo.GetAllAsync()).ToDictionary(s => s.SeriesId, s => s.Title);
            var rowsView = rows.Select(t => new RoleTemplateRow
            {
                TemplateId = t.TemplateId,
                RoleCode = t.RoleCode,
                SeriesId = t.SeriesId,
                SeriesLabel = t.SeriesId.HasValue
                    ? (seriesNameMap.TryGetValue(t.SeriesId.Value, out var nm) ? $"#{t.SeriesId} {nm}" : $"#{t.SeriesId}")
                    : "（既定 / 全シリーズ）",
                FormatTemplate = t.FormatTemplate,
                Notes = t.Notes
            }).ToList();
            gridRoleOverrides.DataSource = rowsView;
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>グリッドで行が選択されたら詳細パネルにロード。 cboOvTemplateSeries の DataSource は IdLabel<int?>（Id は int? 型）に 切り替えたため、SelectedValue にも int? を渡す。row.SeriesId が null（既定行）なら null を 渡せば「（既定 / 全シリーズ）」が選ばれる。</summary>
    /// <summary>グリッドで行が選択されたら詳細パネルにロード（簡素化）。</summary>
    private void OnRoleOverrideRowSelected()
    {
        if (gridRoleOverrides.CurrentRow?.DataBoundItem is RoleTemplateRow row)
        {
            // 既定行は SelectedIndex=0、特定シリーズ行は SelectedValue で対応する int を指定。
            if (row.SeriesId is int sid)
            {
                cboOvTemplateSeries.SelectedValue = (int?)sid;
            }
            else
            {
                cboOvTemplateSeries.SelectedIndex = 0; // 「（既定 / 全シリーズ）」エントリ
            }
            // DB 由来文字列の改行コードを Windows 形式 (\r\n) に正規化してから TextBox にセット。
            // TextBox は内部的に \r\n 改行が前提のコントロールで、\n 単独の文字列を Text プロパティにセットすると
            // 改行が反映されず 1 行表示になることがある。逆に、ユーザーが Enter で打った改行は \r\n となるため
            // 保存時はそのまま MySQL TEXT 列に格納される。両方向で改行が崩れないよう、表示時に正規化する。
            string fmtForDisplay = (row.FormatTemplate ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
            txtOvFormatTemplate.Text = fmtForDisplay;
            txtOvNotes.Text = row.Notes ?? "";
        }
    }

    /// <summary>「+ 新規追加」ボタン：詳細パネルをクリアして、新規作成モードにする （導入）。役職は上部の cboOvSeries の選択中のものをそのまま使う設計のため、 ここでは触らない。シリーズは「（既定 / 全シリーズ）」を初期選択とし、ユーザーが必要なら 特定シリーズに変更する。</summary>
    private void OnNewRoleOverride()
    {
        // グリッド選択を解除（既存行を上書きしないように）
        gridRoleOverrides.ClearSelection();
        if (cboOvTemplateSeries.Items.Count > 0) cboOvTemplateSeries.SelectedIndex = 0;
        txtOvFormatTemplate.Clear();
        txtOvNotes.Clear();
        txtOvFormatTemplate.Focus();
    }

    /// <summary>「💾 保存 / 更新」ボタン：詳細パネルの値で role_templates を UPSERT する。 役職は上部の cboOvSeries（フィルタ兼編集対象）から取得するように変更。 cboOvRole は使わない（フィールドは他参照箇所の都合で残置、Visible=false）。</summary>
    private async Task SaveRoleOverrideAsync()
    {
        try
        {
            // 役職は上部のコンボから取得（cboOvSeries は実体は役職コンボ）
            if (cboOvSeries.SelectedItem is not IdLabel<string> roleSel || string.IsNullOrEmpty(roleSel.Id))
            { MessageBox.Show(this, "上部の「役職」コンボから役職を選択してください。"); return; }
            string roleCode = roleSel.Id;

            if (string.IsNullOrWhiteSpace(txtOvFormatTemplate.Text))
            { MessageBox.Show(this, "書式テンプレは必須です。"); return; }

            // SelectedItem を辿って Id (int?) を取得（SelectedValue 経由だと型変換問題が出るため）。
            int? seriesId = null;
            if (cboOvTemplateSeries.SelectedItem is IdLabel<int?> item) seriesId = item.Id;

            var t = new RoleTemplate
            {
                RoleCode = roleCode,
                SeriesId = seriesId,
                FormatTemplate = txtOvFormatTemplate.Text,  // 改行を保持するため Trim しない
                Notes = NullIfEmpty(txtOvNotes.Text),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };
            await _roleTemplatesRepo.UpsertAsync(t);
            await ReloadRoleOverridesAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>
    /// </summary>
    private async Task DeleteRoleOverrideAsync()
    {
        try
        {
            if (gridRoleOverrides.CurrentRow?.DataBoundItem is not RoleTemplateRow row)
            { MessageBox.Show(this, "削除対象を選択してください。"); return; }
            string label = row.SeriesId.HasValue ? $"({row.RoleCode}, series_id={row.SeriesId})" : $"({row.RoleCode}, 既定)";
            if (MessageBox.Show(this,
                $"{label} のテンプレを削除しますか？", "確認",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            await _roleTemplatesRepo.DeleteAsync(row.TemplateId);
            await ReloadRoleOverridesAsync();
        }
        catch (Exception ex) { this.ShowError(ex); }
    }

    /// <summary>「役職テンプレート」タブの DataGridView 表示用 DTO（series_id を解決済みのラベルとして持つ）。</summary>
    private sealed class RoleTemplateRow
    {
        public int TemplateId { get; set; }
        public string RoleCode { get; set; } = "";
        public int? SeriesId { get; set; }
        public string SeriesLabel { get; set; } = "";
        public string FormatTemplate { get; set; } = "";
        public string? Notes { get; set; }
    }

    // 役職タブの DnD
    // WinForms の DataGridView は標準で「行 DnD」を持たないため、
    // 行ヘッダのマウスダウン位置を記録 → ドラッグ閾値超過で DoDragDrop 起動 →
    // ターゲット行を HitTest で判定 → ドロップ位置（その行の上 or 下）を Y 座標で判別 →
    // 並び順を組み替えて RolesRepository.BulkUpdateDisplayOrderAsync で永続化、
    // という 5 段階を自前で実装する。

    /// <summary>役職タブ DnD：マウスダウン時のセル位置（行ヘッダか否か）を記録する。</summary>
    private Rectangle _rolesDragBoxFromMouseDown = Rectangle.Empty;
    private int _rolesDragSourceIndex = -1;

    private void GridRoles_MouseDown(object? sender, MouseEventArgs e)
    {
        var hit = gridRoles.HitTest(e.X, e.Y);
        // 行ヘッダ列クリックのみドラッグ開始候補とする（セルクリックは編集動作と区別）
        if (hit.Type == DataGridViewHitTestType.RowHeader && hit.RowIndex >= 0)
        {
            Size dragSize = SystemInformation.DragSize;
            _rolesDragBoxFromMouseDown = new Rectangle(
                new Point(e.X - (dragSize.Width / 2), e.Y - (dragSize.Height / 2)),
                dragSize);
            _rolesDragSourceIndex = hit.RowIndex;
        }
        else
        {
            _rolesDragBoxFromMouseDown = Rectangle.Empty;
            _rolesDragSourceIndex = -1;
        }
    }

    private void GridRoles_MouseMove(object? sender, MouseEventArgs e)
    {
        // ドラッグ閾値を超えるまでは何もしない（クリックとドラッグの誤判定回避）
        if ((e.Button & MouseButtons.Left) == MouseButtons.Left
            && _rolesDragBoxFromMouseDown != Rectangle.Empty
            && !_rolesDragBoxFromMouseDown.Contains(e.X, e.Y)
            && _rolesDragSourceIndex >= 0)
        {
            gridRoles.DoDragDrop(_rolesDragSourceIndex, DragDropEffects.Move);
        }
    }

    private void GridRoles_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetDataPresent(typeof(int)))
            e.Effect = DragDropEffects.Move;
        else
            e.Effect = DragDropEffects.None;
    }

    private void GridRoles_DragOver(object? sender, DragEventArgs e)
    {
        // 役職タブはグループ判定が無いので、行ヘッダ・セル領域内なら常に許可
        var p = gridRoles.PointToClient(new Point(e.X, e.Y));
        var hit = gridRoles.HitTest(p.X, p.Y);
        e.Effect = (hit.RowIndex >= 0) ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>役職タブ DnD：ドロップ時に並べ替えを実行し DB へ反映する。</summary>
    private async Task GridRoles_DragDropAsync(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data is null || !e.Data.GetDataPresent(typeof(int))) return;
            int sourceIndex = (int)e.Data.GetData(typeof(int))!;
            if (sourceIndex < 0) return;

            var p = gridRoles.PointToClient(new Point(e.X, e.Y));
            var hit = gridRoles.HitTest(p.X, p.Y);
            if (hit.RowIndex < 0) return;
            int targetIndex = hit.RowIndex;
            if (targetIndex == sourceIndex) return;

            // ターゲット行の上半分にドロップ → その上に挿入、下半分 → その下に挿入
            var rowRect = gridRoles.GetRowDisplayRectangle(targetIndex, true);
            bool dropAbove = p.Y < rowRect.Top + rowRect.Height / 2;

            // 現在の DataSource を List<Role> として取得し、順序を組み替える
            if (gridRoles.DataSource is not List<Role> rows)
            {
                MessageBox.Show(this, "役職一覧の取得に失敗しました（DataSource が想定外）。",
                    "DnD エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var working = rows.ToList();
            var src = working[sourceIndex];
            working.RemoveAt(sourceIndex);

            // RemoveAt 後はインデックスがずれるので調整：
            //   ・src を targetIndex より「前」から取ってきた場合、targetIndex は 1 つ小さくなる
            int adjustedTarget = (sourceIndex < targetIndex) ? targetIndex - 1 : targetIndex;
            int insertAt = dropAbove ? adjustedTarget : adjustedTarget + 1;
            // 範囲安全クランプ
            if (insertAt < 0) insertAt = 0;
            if (insertAt > working.Count) insertAt = working.Count;
            working.Insert(insertAt, src);

            // DB へ display_order の再採番（10, 20, 30, ...）を反映
            await _rolesRepo.BulkUpdateDisplayOrderAsync(working.Select(r => r.RoleCode));

            // 画面を再ロードして確定状態を表示
            gridRoles.DataSource = (await _rolesRepo.GetAllAsync()).ToList();
            HideAuditColumns(gridRoles);
            // 移動後の行を選択状態に保つ（ベストエフォート）
            for (int i = 0; i < gridRoles.Rows.Count; i++)
            {
                if (gridRoles.Rows[i].DataBoundItem is Role rr && rr.RoleCode == src.RoleCode)
                {
                    gridRoles.ClearSelection();
                    gridRoles.Rows[i].Selected = true;
                    gridRoles.CurrentCell = gridRoles.Rows[i].Cells[0];
                    break;
                }
            }
        }
        catch (Exception ex) { this.ShowError(ex); }
        finally
        {
            _rolesDragBoxFromMouseDown = Rectangle.Empty;
            _rolesDragSourceIndex = -1;
        }
    }
}
