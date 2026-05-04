#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class RolePickerDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblSearchCaption = null!;
    private TextBox txtSearch = null!;
    private ListView listResults = null!;
    private Label lblStatus = null!;
    private Button btnNewRole = null!;
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
        ClientSize = new Size(640, 480);
        Name = "RolePickerDialog";
        Text = "役職を選択";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterParent;

        lblSearchCaption = new Label
        {
            Text = "検索（役職コード or 名前 部分一致）:",
            Location = new Point(12, 14),
            Size = new Size(220, 20)
        };
        txtSearch = new TextBox
        {
            Location = new Point(238, 12),
            Size = new Size(380, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        listResults = new ListView
        {
            Location = new Point(12, 44),
            Size = new Size(606, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false
        };
        listResults.Columns.Add("role_code", 160);
        listResults.Columns.Add("name_ja", 280);
        listResults.Columns.Add("format_kind", 130);

        lblStatus = new Label
        {
            Location = new Point(12, 412),
            Size = new Size(330, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = ""
        };
        // v1.2.0 工程 F：「+ 新規役職...」ボタン。押下で QuickAddRoleDialog を開き、
        // 登録された場合はピッカーを自動 OK 扱いで閉じて呼び出し元に新 role_code を返す。
        btnNewRole = new Button
        {
            Text = "+ 新規役職...",
            Location = new Point(348, 408),
            Size = new Size(120, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnOk = new Button
        {
            Text = "OK",
            Location = new Point(458, 438),
            Size = new Size(76, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(540, 438),
            Size = new Size(88, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            lblSearchCaption, txtSearch, listResults, lblStatus, btnNewRole, btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }
}
