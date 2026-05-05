#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class LogoPickerDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblKeyword = null!;
    private TextBox txtKeyword = null!;
    private ListView lvResults = null!;
    private Label lblHitCount = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblKeyword = new Label { Text = "キーワード", Location = new Point(12, 14), Size = new Size(80, 20) };
        txtKeyword = new TextBox
        {
            Location = new Point(98, 10),
            Size = new Size(620, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "屋号名 / CI バージョンラベルで検索"
        };

        lvResults = new ListView
        {
            Location = new Point(12, 44),
            Size = new Size(706, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        lvResults.Columns.Add("logo_id", 70, HorizontalAlignment.Right);
        lvResults.Columns.Add("企業屋号", 240);
        lvResults.Columns.Add("CI バージョン", 180);
        lvResults.Columns.Add("有効期間", 170);

        lblHitCount = new Label
        {
            Location = new Point(12, 410),
            Size = new Size(440, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = ""
        };

        btnOk = new Button
        {
            Text = "OK",
            Location = new Point(558, 405),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(643, 405),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(730, 444);
        Controls.AddRange(new Control[] { lblKeyword, txtKeyword, lvResults, lblHitCount, btnOk, btnCancel });
        Name = "LogoPickerDialog";
        Text = "ロゴを選択";
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        MinimumSize = new Size(560, 350);
    }
}
