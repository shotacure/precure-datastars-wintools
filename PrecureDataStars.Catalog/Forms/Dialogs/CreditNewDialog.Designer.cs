#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class CreditNewDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblTarget = null!;
    private Label lblTargetValue = null!;

    private Label lblCreditKindCaption = null!;
    private RadioButton rbKindOp = null!;
    private RadioButton rbKindEd = null!;

    private Label lblPresentationCaption = null!;
    private RadioButton rbPresentationCards = null!;
    private RadioButton rbPresentationRoll = null!;

    private Label lblPartTypeCaption = null!;
    private ComboBox cboPartType = null!;

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
        ClientSize = new Size(420, 320);
        Name = "CreditNewDialog";
        Text = "新規クレジット作成";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        // 作成対象（呼び出し側が SetTarget で文字列を流し込む）
        lblTarget = new Label { Text = "作成対象:", Location = new Point(18, 18), Size = new Size(70, 20) };
        lblTargetValue = new Label
        {
            Location = new Point(94, 18),
            Size = new Size(310, 20),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = "(未設定)"
        };

        // クレジット種別（OP / ED）
        lblCreditKindCaption = new Label { Text = "種別:", Location = new Point(18, 54), Size = new Size(70, 20) };
        rbKindOp = new RadioButton { Text = "OP（オープニング）", Location = new Point(94, 52), Size = new Size(160, 22), Checked = true };
        rbKindEd = new RadioButton { Text = "ED（エンディング）", Location = new Point(258, 52), Size = new Size(150, 22) };

        // 提示形式
        lblPresentationCaption = new Label { Text = "presentation:", Location = new Point(18, 88), Size = new Size(90, 20) };
        rbPresentationCards = new RadioButton { Text = "CARDS（複数カード）", Location = new Point(110, 86), Size = new Size(160, 22), Checked = true };
        rbPresentationRoll  = new RadioButton { Text = "ROLL（巻物）",         Location = new Point(274, 86), Size = new Size(120, 22) };

        // part_type
        lblPartTypeCaption = new Label { Text = "part_type:", Location = new Point(18, 122), Size = new Size(80, 20) };
        cboPartType = new ComboBox
        {
            Location = new Point(110, 118),
            Size = new Size(290, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        // notes
        lblNotesCaption = new Label { Text = "備考:", Location = new Point(18, 156), Size = new Size(70, 20) };
        txtNotes = new TextBox
        {
            Location = new Point(18, 178),
            Size = new Size(382, 80),
            Multiline = true
        };

        // ボタン
        btnOk = new Button { Text = "作成", Location = new Point(212, 272), Size = new Size(90, 30), DialogResult = DialogResult.OK };
        btnCancel = new Button { Text = "キャンセル", Location = new Point(310, 272), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[]
        {
            lblTarget, lblTargetValue,
            lblCreditKindCaption, rbKindOp, rbKindEd,
            lblPresentationCaption, rbPresentationCards, rbPresentationRoll,
            lblPartTypeCaption, cboPartType,
            lblNotesCaption, txtNotes,
            btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
