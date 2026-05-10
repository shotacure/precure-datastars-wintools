
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Catalog.Forms.Pickers;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 役職系譜編集ダイアログのロジック
/// （v1.3.0 ブラッシュアップ続編で新設）。
/// <para>
/// 前任セクション = 「この役職 (= ToRoleCode) に移行された旧役職 (= FromRoleCode)」。
/// 後任セクション = 「この役職 (= FromRoleCode) から派生した新役職 (= ToRoleCode)」。
/// </para>
/// <para>
/// 追加 / 削除は即時 DB 反映（保存ボタンは無し）。閉じる時に呼び出し元へ「変更があった」旨の通知も
/// 行わない（呼び出し元のグリッドは role_code 自体を編集しないため、閉じた後の再描画は不要）。
/// </para>
/// </summary>
public partial class RoleSuccessionsEditorDialog : Form
{
    private readonly RolesRepository _rolesRepo;
    private readonly RoleSuccessionsRepository _successionsRepo;

    /// <summary>編集対象の役職コード（このダイアログの中心）。</summary>
    private readonly string _targetRoleCode;
    /// <summary>表示用に役職コードと日本語名を引くマップ（ListBox の表示用）。</summary>
    private Dictionary<string, string> _roleNameByCode = new(StringComparer.Ordinal);

    /// <summary>
    /// ダイアログを構築する。<paramref name="targetRoleCode"/> がこのダイアログの中心役職。
    /// </summary>
    public RoleSuccessionsEditorDialog(
        RolesRepository rolesRepo,
        RoleSuccessionsRepository successionsRepo,
        string targetRoleCode,
        string targetRoleNameJa)
    {
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _successionsRepo = successionsRepo ?? throw new ArgumentNullException(nameof(successionsRepo));
        _targetRoleCode = targetRoleCode ?? throw new ArgumentNullException(nameof(targetRoleCode));

        InitializeComponent();

        // ヘッダに対象役職を反映（タイトルバーとは別に内部の lblHeader にも併記）。
        // role_code とユーザフレンドリ名を両方出すことで誤操作を防ぐ。
        this.lblHeader.Text = $"役職系譜の編集：{targetRoleNameJa}（{targetRoleCode}）";

        this.Load += async (_, _) => await ReloadAsync();

        this.btnAddFrom.Click += async (_, _) => await OnAddFromAsync();
        this.btnRemoveFrom.Click += async (_, _) => await OnRemoveFromAsync();
        this.btnAddTo.Click += async (_, _) => await OnAddToAsync();
        this.btnRemoveTo.Click += async (_, _) => await OnRemoveToAsync();
        this.btnClose.Click += (_, _) => this.Close();
    }

    /// <summary>
    /// roles マスタと role_successions の最新状態を取得して 2 つの ListBox を更新する。
    /// 追加・削除のたびに呼び出して整合を保つ。
    /// </summary>
    private async Task ReloadAsync()
    {
        try
        {
            // 役職コード → 表示名のマップを作る（ListBox に「ROLE_CODE  日本語名」形式で出す）。
            var allRoles = await _rolesRepo.GetAllAsync();
            _roleNameByCode = allRoles.ToDictionary(r => r.RoleCode, r => r.NameJa, StringComparer.Ordinal);

            // 前任：to = 自分 の行を取得 → from が前任
            var inboundRows = await _successionsRepo.GetByToAsync(_targetRoleCode);
            this.lstFrom.Items.Clear();
            foreach (var row in inboundRows)
            {
                this.lstFrom.Items.Add(new RoleListItem(row.FromRoleCode, FormatRoleLabel(row.FromRoleCode)));
            }

            // 後任：from = 自分 の行を取得 → to が後任
            var outboundRows = await _successionsRepo.GetByFromAsync(_targetRoleCode);
            this.lstTo.Items.Clear();
            foreach (var row in outboundRows)
            {
                this.lstTo.Items.Add(new RoleListItem(row.ToRoleCode, FormatRoleLabel(row.ToRoleCode)));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"系譜情報のロードに失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>「ROLE_CODE  日本語名」形式の表示文字列を作る。</summary>
    private string FormatRoleLabel(string roleCode)
    {
        return _roleNameByCode.TryGetValue(roleCode, out var nm)
            ? $"{roleCode}  {nm}"
            : roleCode;
    }

    /// <summary>前任の追加（RolePickerDialog で旧役職を選択 → role_successions に from = 旧、to = 自分 を Upsert）。</summary>
    private async Task OnAddFromAsync()
    {
        try
        {
            using var picker = new RolePickerDialog(_rolesRepo);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            var selected = picker.SelectedRole;
            if (selected is null) return;

            // 自己ループは Repository 層で ArgumentException が出るが、ここで先に止めて UI で警告する。
            if (string.Equals(selected.RoleCode, _targetRoleCode, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "自分自身を前任に設定することはできません。", "警告",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 既に登録済みでも Upsert は冪等なので問題ないが、UX 上「既に登録されています」と知らせる。
            var existing = await _successionsRepo.GetByToAsync(_targetRoleCode);
            if (existing.Any(r => string.Equals(r.FromRoleCode, selected.RoleCode, StringComparison.Ordinal)))
            {
                MessageBox.Show(this, "既に前任として登録されています。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await _successionsRepo.UpsertAsync(new RoleSuccession
            {
                FromRoleCode = selected.RoleCode,
                ToRoleCode = _targetRoleCode,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            });
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"前任の追加に失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>前任の削除（lstFrom 選択行に対応する role_successions 行を Delete）。</summary>
    private async Task OnRemoveFromAsync()
    {
        try
        {
            if (this.lstFrom.SelectedItem is not RoleListItem item) return;
            if (MessageBox.Show(this,
                    $"前任 {item.Label} を系譜から外しますか？",
                    "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            await _successionsRepo.DeleteAsync(item.RoleCode, _targetRoleCode);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"前任の削除に失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>後任の追加（RolePickerDialog で新役職を選択 → role_successions に from = 自分、to = 新 を Upsert）。</summary>
    private async Task OnAddToAsync()
    {
        try
        {
            using var picker = new RolePickerDialog(_rolesRepo);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            var selected = picker.SelectedRole;
            if (selected is null) return;

            if (string.Equals(selected.RoleCode, _targetRoleCode, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "自分自身を後任に設定することはできません。", "警告",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var existing = await _successionsRepo.GetByFromAsync(_targetRoleCode);
            if (existing.Any(r => string.Equals(r.ToRoleCode, selected.RoleCode, StringComparison.Ordinal)))
            {
                MessageBox.Show(this, "既に後任として登録されています。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await _successionsRepo.UpsertAsync(new RoleSuccession
            {
                FromRoleCode = _targetRoleCode,
                ToRoleCode = selected.RoleCode,
                Notes = null,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            });
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"後任の追加に失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>後任の削除（lstTo 選択行に対応する role_successions 行を Delete）。</summary>
    private async Task OnRemoveToAsync()
    {
        try
        {
            if (this.lstTo.SelectedItem is not RoleListItem item) return;
            if (MessageBox.Show(this,
                    $"後任 {item.Label} を系譜から外しますか？",
                    "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            await _successionsRepo.DeleteAsync(_targetRoleCode, item.RoleCode);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"後任の削除に失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// ListBox に表示する 1 行分のアイテム
    /// （RoleCode と表示用 Label を保持。ToString() で Label を返すので ListBox に直接突っ込める）。
    /// </summary>
    private sealed class RoleListItem
    {
        public string RoleCode { get; }
        public string Label { get; }
        public RoleListItem(string roleCode, string label)
        {
            RoleCode = roleCode;
            Label = label;
        }
        public override string ToString() => Label;
    }
}
