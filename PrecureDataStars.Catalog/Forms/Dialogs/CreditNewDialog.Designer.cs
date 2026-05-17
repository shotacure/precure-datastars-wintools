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
    // 種別 (OP/ED) と presentation (CARDS/ROLL) はそれぞれ別の選択軸なので、
    // 同じ親コンテナにすべての RadioButton を直接 Add してしまうと WinForms の仕様により
    // 4 つすべてが排他選択グループになって、ED と CARDS のような組み合わせが両立しなくなる。
    // 各軸を別々の Panel でラップして、それぞれの Panel 配下のラジオボタン同士だけが排他になるようにする。
    private Panel pnlCreditKind = null!;
    private RadioButton rbKindOp = null!;
    private RadioButton rbKindEd = null!;

    private Label lblPresentationCaption = null!;
    private Panel pnlPresentation = null!;
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
        // ラジオボタンは Panel pnlCreditKind 配下に置き、presentation 軸とは別の
        // 排他選択グループにする。Panel の位置に対する相対座標で配置する。
        lblCreditKindCaption = new Label { Text = "種別:", Location = new Point(18, 54), Size = new Size(70, 20) };
        pnlCreditKind = new Panel
        {
            Location = new Point(94, 50),
            Size = new Size(316, 26),
        };
        rbKindOp = new RadioButton { Text = "OP（オープニング）", Location = new Point(0, 2), Size = new Size(160, 22), Checked = true };
        rbKindEd = new RadioButton { Text = "ED（エンディング）", Location = new Point(164, 2), Size = new Size(150, 22) };
        pnlCreditKind.Controls.Add(rbKindOp);
        pnlCreditKind.Controls.Add(rbKindEd);

        // 提示形式
        // presentation の 2 つのラジオも独立した Panel pnlPresentation 配下にまとめる。
        lblPresentationCaption = new Label { Text = "presentation:", Location = new Point(18, 88), Size = new Size(90, 20) };
        pnlPresentation = new Panel
        {
            Location = new Point(110, 84),
            Size = new Size(300, 26),
        };
        rbPresentationCards = new RadioButton { Text = "CARDS（複数カード）", Location = new Point(0, 2), Size = new Size(160, 22), Checked = true };
        rbPresentationRoll  = new RadioButton { Text = "ROLL（巻物）",         Location = new Point(164, 2), Size = new Size(120, 22) };
        pnlPresentation.Controls.Add(rbPresentationCards);
        pnlPresentation.Controls.Add(rbPresentationRoll);

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
            // ラジオボタンは Panel 経由で追加（排他選択グループを軸ごとに分離するため）。
            lblCreditKindCaption, pnlCreditKind,
            lblPresentationCaption, pnlPresentation,
            lblPartTypeCaption, cboPartType,
            lblNotesCaption, txtNotes,
            btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}