#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class QuickAddPersonDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblHeader = null!;
    private Label lblFullNameCaption = null!;
    private TextBox txtFullName = null!;
    private Label lblFullNameKanaCaption = null!;
    private TextBox txtFullNameKana = null!;
    private Label lblNameEnCaption = null!;
    private TextBox txtNameEn = null!;
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
        ClientSize = new Size(440, 300);
        Name = "QuickAddPersonDialog";
        Text = "人物の即時追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        lblHeader = new Label
        {
            Text = "新しい人物 1 名を「単独名義」として登録します。\n"
                + "（共同名義はクレジット系マスタ管理画面で別途作成してください）",
            Location = new Point(16, 12),
            Size = new Size(408, 36),
            ForeColor = SystemColors.GrayText
        };

        lblFullNameCaption = new Label { Text = "氏名（必須）:", Location = new Point(16, 60), Size = new Size(110, 20) };
        txtFullName        = new TextBox { Location = new Point(140, 58), Size = new Size(284, 23) };

        lblFullNameKanaCaption = new Label { Text = "かな:", Location = new Point(16, 92), Size = new Size(110, 20) };
        txtFullNameKana        = new TextBox { Location = new Point(140, 90), Size = new Size(284, 23) };

        lblNameEnCaption = new Label { Text = "英名:", Location = new Point(16, 124), Size = new Size(110, 20) };
        txtNameEn        = new TextBox { Location = new Point(140, 122), Size = new Size(284, 23) };

        lblNotesCaption = new Label { Text = "備考:", Location = new Point(16, 156), Size = new Size(110, 20) };
        txtNotes        = new TextBox
        {
            Location = new Point(140, 154),
            Size = new Size(284, 76),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        btnOk = new Button
        {
            Text = "登録",
            Location = new Point(232, 250),
            Size = new Size(90, 30)
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(330, 250),
            Size = new Size(94, 30),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            lblHeader,
            lblFullNameCaption, txtFullName,
            lblFullNameKanaCaption, txtFullNameKana,
            lblNameEnCaption, txtNameEn,
            lblNotesCaption, txtNotes,
            btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}