#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class ProductCompanyPickerDialog
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

    /// <summary>
    /// レイアウト初期化。検索キーワード入力欄＋結果 ListView（ID/和名/かな/英名）＋OK/Cancel ボタンの
    /// 単純なピッカー UI。<see cref="PersonAliasPickerDialog"/> と並びを揃えてあるので、
    /// 利用者が見たことのある形のままで迷わず使える。
    /// </summary>
    private void InitializeComponent()
    {
        lblKeyword = new Label
        {
            Text = "キーワード",
            Location = new Point(12, 16),
            Size = new Size(80, 20)
        };
        txtKeyword = new TextBox
        {
            Location = new Point(98, 12),
            Size = new Size(540, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "和名 / かな / 英名 で部分一致検索"
        };

        lvResults = new ListView
        {
            Location = new Point(12, 46),
            Size = new Size(626, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        // 列：ID / 和名 / かな / 英名
        lvResults.Columns.Add("ID", 60, HorizontalAlignment.Right);
        lvResults.Columns.Add("和名", 220);
        lvResults.Columns.Add("かな", 180);
        lvResults.Columns.Add("英名", 160);

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
        Controls.AddRange(new Control[] { lblKeyword, txtKeyword, lvResults, lblHitCount, btnOk, btnCancel });
        Name = "ProductCompanyPickerDialog";
        Text = "商品社名を選択";
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        MinimumSize = new Size(500, 350);
    }
}
