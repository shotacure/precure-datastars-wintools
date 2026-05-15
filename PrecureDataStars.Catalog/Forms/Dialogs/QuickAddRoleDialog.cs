using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 役職の即時追加ダイアログ。
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

    /// <summary>
    /// 表示前にセットされた場合、Load 時に <c>txtNameJa</c> へ事前入力する日本語名。
    /// クレジット一括入力ダイアログ <see cref="CreditBulkInputDialog"/> から「未登録役職」を
    /// 1 件ずつ追加する際に、テキスト中の表記をそのまま流し込むために使う。
    /// </summary>
    /// <remarks>
    /// WinForms Designer は <see cref="Form"/> 派生クラスのパブリック書き込み可能プロパティを
    /// シリアライズ対象とみなして警告 WFO1000 を出すため、デザイナーから隠す属性を付ける。
    /// 本プロパティはコード経由でのみ設定する用途で、デザイナー上で操作する性質のものではない。
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? PrefilledNameJa { get; set; }

    /// <summary>
    /// 表示前にセットされた場合、Load 時に <c>cboFormatKind</c> の初期選択を上書きする
    /// 役職書式区分コード（"NORMAL"/"VOICE_CAST"/"SERIAL"/etc.）。
    /// 一括入力ダイアログから VOICE_CAST 系の役職を追加する際に推定値を渡す用途。
    /// </summary>
    /// <remarks>
    /// <see cref="PrefilledNameJa"/> と同様、WFO1000 抑止のためデザイナーから隠す。
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? PrefilledFormatKind { get; set; }

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

        // 呼び出し側から事前入力が指示されていれば反映する。
        if (!string.IsNullOrEmpty(PrefilledNameJa))
        {
            txtNameJa.Text = PrefilledNameJa;
            // 役職コードは英字必須なので事前入力対象外。フォーカスをコード欄に戻して入力を促す。
            txtRoleCode.Focus();
        }
        if (!string.IsNullOrEmpty(PrefilledFormatKind))
        {
            // コンボの表示文字列は "NORMAL  — 通常の役職..." 形式。先頭が英字コードで一致するアイテムを探す。
            for (int i = 0; i < cboFormatKind.Items.Count; i++)
            {
                string? text = cboFormatKind.Items[i]?.ToString();
                if (string.IsNullOrEmpty(text)) continue;
                if (text.StartsWith(PrefilledFormatKind + " ", StringComparison.Ordinal)
                    || string.Equals(text, PrefilledFormatKind, StringComparison.Ordinal))
                {
                    cboFormatKind.SelectedIndex = i;
                    break;
                }
            }
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

            // 役職の書式テンプレは role_templates テーブルで管理するため、本ダイアログには
            // テンプレ入力欄を置かない。テンプレは「クレジット系マスタ管理 → 役職テンプレート」タブで
            // 別途編集する設計。ここでは役職コード／表示名／書式区分／表示順／備考だけを登録する。
            var newRole = new Role
            {
                RoleCode = roleCode,
                NameJa = nameJa,
                NameEn = string.IsNullOrWhiteSpace(txtNameEn.Text) ? null : txtNameEn.Text.Trim(),
                RoleFormatKind = formatKind,
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