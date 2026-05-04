using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 役職の即時追加ダイアログ（v1.2.0 工程 F 追加）。
/// <para>
/// クレジット編集中に「マスタにまだ無い役職」を即座に登録するためのダイアログ。
/// 役職コード・表示名（和英）・書式区分・既定書式テンプレ・表示順・備考を入力し、
/// <see cref="RolesRepository.UpsertAsync"/> で <c>roles</c> に 1 行 INSERT する
/// （RolesRepository は INSERT ... ON DUPLICATE KEY UPDATE の Upsert 方式で 1 系統に統一されているため、
///  既存と同じ役職コードを指定した場合は更新になる）。
/// </para>
/// <para>
/// 起動時に <c>RolesRepository.GetAllAsync()</c> から既存の display_order の最大値を取得し、
/// 新規行の <see cref="NumericUpDown"/> 既定値を「最大値 + 10」にセットする
/// （マスタ画面の DnD 並べ替えと同じ 10 単位飛び番運用）。
/// </para>
/// <para>
/// OK で閉じたとき、新規 role_code が <see cref="SelectedRoleCode"/> にセットされる。
/// 呼び出し側（<see cref="Pickers.RolePickerDialog"/>）はこれを受けて自動で OK 状態に進ませる。
/// </para>
/// </summary>
public partial class QuickAddRoleDialog : Form
{
    private readonly RolesRepository _rolesRepo;

    /// <summary>登録成功時の新規 role_code。キャンセル時は null。</summary>
    public string? SelectedRoleCode { get; private set; }

    public QuickAddRoleDialog(RolesRepository rolesRepo)
    {
        _rolesRepo = rolesRepo ?? throw new ArgumentNullException(nameof(rolesRepo));
        InitializeComponent();
        btnOk.Click += async (_, __) => await OnOkAsync();
        Load += async (_, __) => await OnLoadAsync();
    }

    /// <summary>初期表示：display_order の既定値を「既存最大 + 10」にセットする。</summary>
    private async Task OnLoadAsync()
    {
        try
        {
            var existing = await _rolesRepo.GetAllAsync();
            int maxOrder = existing
                .Where(r => r.DisplayOrder.HasValue)
                .Select(r => (int)r.DisplayOrder!.Value)
                .DefaultIfEmpty(0)
                .Max();
            int next = maxOrder + 10;
            if (next < 1) next = 10;
            if (next > (int)numDisplayOrder.Maximum) next = (int)numDisplayOrder.Maximum;
            numDisplayOrder.Value = next;
        }
        catch
        {
            // 取得失敗時は既定値 10 のままで続行（致命的ではない）
        }
    }

    /// <summary>登録ボタン処理：必須チェック → INSERT → 戻り値セット。</summary>
    private async Task OnOkAsync()
    {
        try
        {
            string roleCode = (txtRoleCode.Text ?? "").Trim();
            string nameJa   = (txtNameJa.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(roleCode))
            {
                MessageBox.Show(this, "役職コードは必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtRoleCode.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(nameJa))
            {
                MessageBox.Show(this, "表示名（日本語）は必須です。", "入力エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtNameJa.Focus();
                return;
            }

            // 書式区分はコンボの SelectedIndex から英字コードを取り出す（表示テキストの先頭部分）
            string formatKind = ExtractFormatKindCode(cboFormatKind.SelectedItem?.ToString() ?? "NORMAL");

            var newRole = new Role
            {
                RoleCode = roleCode,
                NameJa = nameJa,
                NameEn = string.IsNullOrWhiteSpace(txtNameEn.Text) ? null : txtNameEn.Text.Trim(),
                RoleFormatKind = formatKind,
                DefaultFormatTemplate = string.IsNullOrWhiteSpace(txtFormatTemplate.Text) ? null : txtFormatTemplate.Text.Trim(),
                DisplayOrder = (ushort)numDisplayOrder.Value,
                Notes = string.IsNullOrWhiteSpace(txtNotes.Text) ? null : txtNotes.Text.Trim(),
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };

            await _rolesRepo.UpsertAsync(newRole);
            SelectedRoleCode = roleCode;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "登録エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// コンボの表示テキスト（"NORMAL  — 通常の役職..." 形式）から先頭の英字コードを切り出す。
    /// </summary>
    private static string ExtractFormatKindCode(string display)
    {
        if (string.IsNullOrWhiteSpace(display)) return "NORMAL";
        int sep = display.IndexOf(' ');
        return sep > 0 ? display.Substring(0, sep) : display;
    }
}
