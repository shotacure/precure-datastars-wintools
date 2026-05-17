#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class CharacterAliasPickerDialog
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
            Size = new Size(540, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "キャラ名義の名前 / 読みで検索"
        };

        lvResults = new ListView
        {
            Location = new Point(12, 44),
            Size = new Size(626, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        lvResults.Columns.Add("alias_id", 70, HorizontalAlignment.Right);
        lvResults.Columns.Add("名義", 240);
        lvResults.Columns.Add("かな", 200);
        // character_aliases に有効期間カラムは無いため、列も持たない。

        lblHitCount = new Label
        {
            Location = new Point(12, 410),
            Size = new Size(400, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = ""
        };

        btnOk = new Button
        {
            Text = "OK",
            Location = new Point(478, 405),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(563, 405),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(650, 444);
        Controls.AddRange(new Control[] { lblKeyword, txtKeyword, lvResults, lblHitCount, btnOk, btnCancel });
        Name = "CharacterAliasPickerDialog";
        Text = "キャラ名義を選択";
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        MinimumSize = new Size(500, 350);
    }
}