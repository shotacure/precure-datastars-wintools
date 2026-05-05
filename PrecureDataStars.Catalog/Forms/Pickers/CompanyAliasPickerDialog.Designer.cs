#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class CompanyAliasPickerDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblScopeInfo = null!;
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
        lblScopeInfo = new Label
        {
            Location = new Point(12, 8),
            Size = new Size(640, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = SystemColors.GrayText,
            Visible = false
        };

        lblKeyword = new Label { Text = "キーワード", Location = new Point(12, 36), Size = new Size(80, 20) };
        txtKeyword = new TextBox
        {
            Location = new Point(98, 32),
            Size = new Size(540, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "屋号 / 読みで検索"
        };

        lvResults = new ListView
        {
            Location = new Point(12, 66),
            Size = new Size(626, 340),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        lvResults.Columns.Add("ID", 60, HorizontalAlignment.Right);
        lvResults.Columns.Add("屋号", 220);
        lvResults.Columns.Add("かな", 180);
        lvResults.Columns.Add("有効開始", 90);
        lvResults.Columns.Add("有効終了", 90);

        lblHitCount = new Label
        {
            Location = new Point(12, 412),
            Size = new Size(400, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = ""
        };

        btnOk = new Button
        {
            Text = "OK",
            Location = new Point(478, 407),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(563, 407),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(650, 446);
        Controls.AddRange(new Control[] { lblScopeInfo, lblKeyword, txtKeyword, lvResults, lblHitCount, btnOk, btnCancel });
        Name = "CompanyAliasPickerDialog";
        Text = "企業屋号を選択";
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        MinimumSize = new Size(500, 350);
    }
}
