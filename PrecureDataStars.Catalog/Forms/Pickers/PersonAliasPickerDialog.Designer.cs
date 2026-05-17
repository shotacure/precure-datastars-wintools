#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class PersonAliasPickerDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblScopeInfo = null!;       // スコープ表示（指定された人物 ID 配下に絞られている旨）
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
        // スコープ案内ラベル（最上段）：scope 指定時のみ可視化
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
            PlaceholderText = "名義名 / 読みで検索"
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
        // 列：alias_id / 名義名 / かな / 有効開始 / 有効終了
        lvResults.Columns.Add("ID", 60, HorizontalAlignment.Right);
        lvResults.Columns.Add("名義名", 200);
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
        Name = "PersonAliasPickerDialog";
        Text = "人物名義を選択";
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        MinimumSize = new Size(500, 350);
    }
}