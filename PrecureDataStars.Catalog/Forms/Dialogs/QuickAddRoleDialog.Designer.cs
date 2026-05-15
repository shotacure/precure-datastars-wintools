#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class QuickAddRoleDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblHeader = null!;
    private Label lblRoleCodeCaption = null!;
    private TextBox txtRoleCode = null!;
    private Label lblNameJaCaption = null!;
    private TextBox txtNameJa = null!;
    private Label lblNameEnCaption = null!;
    private TextBox txtNameEn = null!;
    private Label lblFormatKindCaption = null!;
    private ComboBox cboFormatKind = null!;
    // (lblFormatTemplateCaption / txtFormatTemplate)。
    private Label lblDisplayOrderCaption = null!;
    private NumericUpDown numDisplayOrder = null!;
    private Label lblNotesCaption = null!;
    private TextBox txtNotes = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(500, 438);
        Name = "QuickAddRoleDialog";
        Text = "役職の即時追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        lblHeader = new Label
        {
            Text = "新しい役職をマスタへ即時登録します。\n"
                + "（コード命名は英大文字 + アンダースコア推奨。例: DIRECTOR / VOICE_CAST / SERIAL_KIKAKU）",
            Location = new Point(16, 12),
            Size = new Size(468, 38),
            ForeColor = SystemColors.GrayText
        };

        // 役職コード（PK、必須）
        lblRoleCodeCaption = new Label { Text = "役職コード（必須）:", Location = new Point(16, 60), Size = new Size(150, 20) };
        txtRoleCode = new TextBox
        {
            Location = new Point(176, 58),
            Size = new Size(308, 23),
            CharacterCasing = CharacterCasing.Upper,
            PlaceholderText = "例: DIRECTOR / SCRIPT / VOICE_CAST"
        };

        // 表示名（日本語、必須）
        lblNameJaCaption = new Label { Text = "表示名（日本語、必須）:", Location = new Point(16, 92), Size = new Size(150, 20) };
        txtNameJa = new TextBox
        {
            Location = new Point(176, 90),
            Size = new Size(308, 23),
            PlaceholderText = "例: 監督 / 脚本 / 声の出演"
        };

        // 表示名（英語、任意）
        lblNameEnCaption = new Label { Text = "表示名（英語、任意）:", Location = new Point(16, 124), Size = new Size(150, 20) };
        txtNameEn = new TextBox
        {
            Location = new Point(176, 122),
            Size = new Size(308, 23),
            PlaceholderText = "例: Director / Screenplay / Voice Cast"
        };

        // 役職書式区分（必須）
        lblFormatKindCaption = new Label { Text = "書式区分（必須）:", Location = new Point(16, 156), Size = new Size(150, 20) };
        cboFormatKind = new ComboBox
        {
            Location = new Point(176, 154),
            Size = new Size(308, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        // 6 つの書式区分の表示順（クレジット運用の頻度順）
        cboFormatKind.Items.AddRange(new object[]
        {
            "NORMAL  — 通常の役職（人物名義のリスト）",
            "VOICE_CAST  — 声の出演（キャラ × 声優ペア）",
            "THEME_SONG  — 主題歌（歌録音 + ラベル）",
            "COMPANY_ONLY  — 企業のみ（屋号のリスト）",
            "LOGO_ONLY  — ロゴのみ",
            "SERIAL  — 連載（書式テンプレで上書き）"
        });
        cboFormatKind.SelectedIndex = 0;

        // 書式テンプレート入力欄は持たない（テンプレは role_templates テーブルで管理し、
        // 「クレジット系マスタ管理 → 役職テンプレート」タブで編集する設計に変更）。
        // 表示位置を詰めるため、後続コントロールの Y 座標を 32 px ずつ繰り上げる。

        // 表示順（必須、既定値は呼び出し側でセット）
        lblDisplayOrderCaption = new Label { Text = "表示順:", Location = new Point(16, 188), Size = new Size(150, 20) };
        numDisplayOrder = new NumericUpDown
        {
            Location = new Point(176, 186),
            Size = new Size(120, 23),
            Minimum = 1,
            Maximum = 9999,
            Value = 10,
            Increment = 10
        };

        // 備考
        lblNotesCaption = new Label { Text = "備考:", Location = new Point(16, 220), Size = new Size(150, 20) };
        txtNotes = new TextBox
        {
            Location = new Point(176, 218),
            Size = new Size(308, 130),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        // ボタン
        btnOk = new Button
        {
            Text = "登録",
            Location = new Point(296, 390),
            Size = new Size(94, 30)
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(396, 390),
            Size = new Size(94, 30),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            lblHeader,
            lblRoleCodeCaption, txtRoleCode,
            lblNameJaCaption, txtNameJa,
            lblNameEnCaption, txtNameEn,
            lblFormatKindCaption, cboFormatKind,
            lblDisplayOrderCaption, numDisplayOrder,
            lblNotesCaption, txtNotes,
            btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}