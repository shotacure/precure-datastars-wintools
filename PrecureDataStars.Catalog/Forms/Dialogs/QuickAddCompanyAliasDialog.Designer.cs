#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class QuickAddCompanyAliasDialog
{
    private System.ComponentModel.IContainer? components = null;

    // モード選択
    private GroupBox grpMode = null!;
    private RadioButton rbModeExisting = null!;
    private RadioButton rbModeNewCompany = null!;

    // モード A：既存企業に屋号を追加
    private Panel pnlExisting = null!;
    private Label lblParentCompanyCaption = null!;
    private Label lblParentCompanyValue = null!;
    private Button btnPickParentCompany = null!;
    private Label lblExistingAliasNameCaption = null!;
    private TextBox txtExistingAliasName = null!;
    private Label lblExistingAliasKanaCaption = null!;
    private TextBox txtExistingAliasKana = null!;

    // モード B：企業ごと新規作成
    private Panel pnlNewCompany = null!;
    private Label lblNewCompanyNameCaption = null!;
    private TextBox txtNewCompanyName = null!;
    private Label lblNewCompanyKanaCaption = null!;
    private TextBox txtNewCompanyKana = null!;
    private Label lblNewCompanyEnCaption = null!;
    private TextBox txtNewCompanyEn = null!;
    private Label lblNewAliasNameCaption = null!;
    private TextBox txtNewAliasName = null!;
    private Label lblNewAliasKanaCaption = null!;
    private TextBox txtNewAliasKana = null!;
    private Label lblNewAliasNoticeLabel = null!;

    // ボタン
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(480, 460);
        Name = "QuickAddCompanyAliasDialog";
        Text = "企業屋号の即時追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        // ─── モード選択 GroupBox ───
        grpMode = new GroupBox
        {
            Text = "モード",
            Location = new Point(12, 12),
            Size = new Size(456, 76)
        };
        rbModeExisting = new RadioButton
        {
            Text = "既存の企業に屋号を追加する",
            Location = new Point(12, 22),
            Size = new Size(420, 22),
            Checked = true
        };
        rbModeNewCompany = new RadioButton
        {
            Text = "企業ごと新規作成する（企業 + 屋号を 1 トランザクションで投入）",
            Location = new Point(12, 46),
            Size = new Size(420, 22)
        };
        grpMode.Controls.AddRange(new Control[] { rbModeExisting, rbModeNewCompany });

        // ─── モード A 用 Panel（既存企業に屋号追加）───
        pnlExisting = new Panel
        {
            Location = new Point(12, 96),
            Size = new Size(456, 290),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = true
        };
        lblParentCompanyCaption = new Label { Text = "親企業（必須）:", Location = new Point(12, 16), Size = new Size(120, 20) };
        lblParentCompanyValue   = new Label
        {
            Location = new Point(12, 40),
            Size = new Size(330, 22),
            Text = "（未選択）",
            BorderStyle = BorderStyle.FixedSingle
        };
        btnPickParentCompany = new Button
        {
            Text = "選択...",
            Location = new Point(348, 38),
            Size = new Size(94, 26)
        };
        lblExistingAliasNameCaption = new Label { Text = "屋号名（必須）:", Location = new Point(12, 80), Size = new Size(120, 20) };
        txtExistingAliasName        = new TextBox { Location = new Point(140, 78), Size = new Size(302, 23) };
        lblExistingAliasKanaCaption = new Label { Text = "屋号かな:", Location = new Point(12, 112), Size = new Size(120, 20) };
        txtExistingAliasKana        = new TextBox { Location = new Point(140, 110), Size = new Size(302, 23) };
        pnlExisting.Controls.AddRange(new Control[]
        {
            lblParentCompanyCaption, lblParentCompanyValue, btnPickParentCompany,
            lblExistingAliasNameCaption, txtExistingAliasName,
            lblExistingAliasKanaCaption, txtExistingAliasKana
        });

        // ─── モード B 用 Panel（企業ごと新規作成）───
        pnlNewCompany = new Panel
        {
            Location = new Point(12, 96),
            Size = new Size(456, 290),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };
        lblNewCompanyNameCaption = new Label { Text = "企業名（必須）:", Location = new Point(12, 16), Size = new Size(120, 20) };
        txtNewCompanyName        = new TextBox { Location = new Point(140, 14), Size = new Size(302, 23) };
        lblNewCompanyKanaCaption = new Label { Text = "企業かな:", Location = new Point(12, 48), Size = new Size(120, 20) };
        txtNewCompanyKana        = new TextBox { Location = new Point(140, 46), Size = new Size(302, 23) };
        lblNewCompanyEnCaption   = new Label { Text = "企業英名:", Location = new Point(12, 80), Size = new Size(120, 20) };
        txtNewCompanyEn          = new TextBox { Location = new Point(140, 78), Size = new Size(302, 23) };

        lblNewAliasNoticeLabel = new Label
        {
            Text = "─── 屋号情報（companies と company_aliases の両方に投入されます）───",
            Location = new Point(12, 116),
            Size = new Size(430, 20),
            ForeColor = SystemColors.GrayText
        };
        lblNewAliasNameCaption = new Label { Text = "屋号名（必須）:", Location = new Point(12, 144), Size = new Size(120, 20) };
        txtNewAliasName        = new TextBox { Location = new Point(140, 142), Size = new Size(302, 23) };
        lblNewAliasKanaCaption = new Label { Text = "屋号かな:", Location = new Point(12, 176), Size = new Size(120, 20) };
        txtNewAliasKana        = new TextBox { Location = new Point(140, 174), Size = new Size(302, 23) };
        pnlNewCompany.Controls.AddRange(new Control[]
        {
            lblNewCompanyNameCaption, txtNewCompanyName,
            lblNewCompanyKanaCaption, txtNewCompanyKana,
            lblNewCompanyEnCaption,   txtNewCompanyEn,
            lblNewAliasNoticeLabel,
            lblNewAliasNameCaption, txtNewAliasName,
            lblNewAliasKanaCaption, txtNewAliasKana
        });

        // ─── ボタン ───
        btnOk = new Button
        {
            Text = "登録",
            Location = new Point(272, 412),
            Size = new Size(94, 30)
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(374, 412),
            Size = new Size(94, 30),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            grpMode, pnlExisting, pnlNewCompany, btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}