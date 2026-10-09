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
/// 役職どうしの関連（段階・並列）の編集ダイアログのロジック。
/// 前段階セクション = STEP_UP で「相手 (= FromRoleCode) → この役職 (= ToRoleCode)」。
/// 並列セクション   = PARALLEL で、この役職を from / to のどちらかに持つ行の相手側。
/// 後段階セクション = STEP_UP で「この役職 (= FromRoleCode) → 相手 (= ToRoleCode)」。
/// 追加 / 削除は即時 DB 反映（保存ボタンは無し）。同じ組の役職に別の関連がすでにあるときは
/// リポジトリが例外で弾くので、その文面をそのまま知らせる。
/// </summary>
public partial class RoleRelationsEditorDialog : Form
{
    private readonly RolesRepository _rolesRepo;
    private readonly RoleRelationsRepository _relationsRepo;

    /// <summary>編集対象の役職コード（このダイアログの中心）。</summary>
    private readonly string _targetRoleCode;
    /// <summary>表示用に役職コードと日本語名を引くマップ（ListBox の表示用）。</summary>
    private Dictionary<string, string> _roleNameByCode = new(StringComparer.Ordinal);

    /// <summary>ダイアログを構築する。<paramref name="targetRoleCode"/> がこのダイアログの中心役職。</summary>
    public RoleRelationsEditorDialog(
        RolesRepository rolesRepo,
        RoleRelationsRepository relationsRepo,
        string targetRoleCode,
        string targetRoleNameJa)
    {
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        _relationsRepo = relationsRepo ?? throw new ArgumentNullException(nameof(relationsRepo));
        _targetRoleCode = targetRoleCode ?? throw new ArgumentNullException(nameof(targetRoleCode));

        InitializeComponent();

        // role_code とユーザフレンドリ名を両方出すことで誤操作を防ぐ。
        this.lblHeader.Text = $"役職の関連の編集：{targetRoleNameJa}（{targetRoleCode}）";

        this.Load += async (_, _) => await ReloadAsync();

        this.btnAddBefore.Click += async (_, _) => await OnAddAsync(Section.Before);
        this.btnRemoveBefore.Click += async (_, _) => await OnRemoveAsync(Section.Before);
        this.btnAddParallel.Click += async (_, _) => await OnAddAsync(Section.Parallel);
        this.btnRemoveParallel.Click += async (_, _) => await OnRemoveAsync(Section.Parallel);
        this.btnAddAfter.Click += async (_, _) => await OnAddAsync(Section.After);
        this.btnRemoveAfter.Click += async (_, _) => await OnRemoveAsync(Section.After);
        this.btnClose.Click += (_, _) => this.Close();
    }

    /// <summary>ダイアログの 3 セクション。</summary>
    private enum Section { Before, Parallel, After }

    /// <summary>roles マスタと role_relations の最新状態を取得して 3 つの ListBox を更新する。追加・削除のたびに呼ぶ。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            var allRoles = await _rolesRepo.GetAllAsync();
            _roleNameByCode = allRoles.ToDictionary(r => r.RoleCode, r => r.NameJa, StringComparer.Ordinal);

            var rows = await _relationsRepo.GetByRoleAsync(_targetRoleCode);
            this.lstBefore.Items.Clear();
            this.lstParallel.Items.Clear();
            this.lstAfter.Items.Clear();
            foreach (var row in rows)
            {
                bool isFrom = string.Equals(row.FromRoleCode, _targetRoleCode, StringComparison.Ordinal);
                string other = isFrom ? row.ToRoleCode : row.FromRoleCode;
                var item = new RelationListItem(row.FromRoleCode, row.ToRoleCode, FormatRoleLabel(other));
                if (string.Equals(row.RelationKind, RoleRelationKinds.Parallel, StringComparison.Ordinal))
                    this.lstParallel.Items.Add(item);
                else if (isFrom)
                    this.lstAfter.Items.Add(item);
                else
                    this.lstBefore.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"関連のロードに失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>「ROLE_CODE  日本語名」形式の表示文字列を作る。</summary>
    private string FormatRoleLabel(string roleCode)
        => _roleNameByCode.TryGetValue(roleCode, out var nm) ? $"{roleCode}  {nm}" : roleCode;

    /// <summary>セクションの見出し（メッセージ用）。</summary>
    private static string SectionLabel(Section s) => s switch
    {
        Section.Before => "前段階",
        Section.Parallel => "並列",
        _ => "後段階"
    };

    /// <summary>関連の追加（RolePickerDialog で相手の役職を選び、セクションに応じた向き・種類で Upsert）。</summary>
    private async Task OnAddAsync(Section section)
    {
        try
        {
            using var picker = new RolePickerDialog(_rolesRepo);
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            var selected = picker.SelectedRole;
            if (selected is null) return;

            if (string.Equals(selected.RoleCode, _targetRoleCode, StringComparison.Ordinal))
            {
                MessageBox.Show(this, $"自分自身を{SectionLabel(section)}に設定することはできません。", "警告",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var relation = section switch
            {
                Section.Before => new RoleRelation
                {
                    FromRoleCode = selected.RoleCode, ToRoleCode = _targetRoleCode, RelationKind = RoleRelationKinds.StepUp
                },
                Section.After => new RoleRelation
                {
                    FromRoleCode = _targetRoleCode, ToRoleCode = selected.RoleCode, RelationKind = RoleRelationKinds.StepUp
                },
                _ => new RoleRelation
                {
                    FromRoleCode = _targetRoleCode, ToRoleCode = selected.RoleCode, RelationKind = RoleRelationKinds.Parallel
                }
            };
            relation.CreatedBy = Environment.UserName;
            relation.UpdatedBy = Environment.UserName;

            await _relationsRepo.UpsertAsync(relation);
            await ReloadAsync();
        }
        catch (InvalidOperationException ex)
        {
            // 同じ組の役職に別の関連がすでにある（逆向きの段階・段階と並列の重複）。
            MessageBox.Show(this, ex.Message, "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{SectionLabel(section)}の追加に失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>関連の削除（セクションの ListBox で選んだ行を Delete）。</summary>
    private async Task OnRemoveAsync(Section section)
    {
        try
        {
            var list = section switch
            {
                Section.Before => this.lstBefore,
                Section.Parallel => this.lstParallel,
                _ => this.lstAfter
            };
            if (list.SelectedItem is not RelationListItem item) return;
            if (MessageBox.Show(this,
                    $"{SectionLabel(section)} {item.Label} を関連から外しますか？",
                    "確認", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            await _relationsRepo.DeleteAsync(item.FromRoleCode, item.ToRoleCode);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{SectionLabel(section)}の削除に失敗しました：{ex.Message}", "エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>ListBox に表示する 1 行分のアイテム（DB の行の from / to と表示用 Label を保持。ToString() で Label を返す）。</summary>
    private sealed class RelationListItem
    {
        public string FromRoleCode { get; }
        public string ToRoleCode { get; }
        public string Label { get; }
        public RelationListItem(string fromRoleCode, string toRoleCode, string label)
        {
            FromRoleCode = fromRoleCode;
            ToRoleCode = toRoleCode;
            Label = label;
        }
        public override string ToString() => Label;
    }
}
