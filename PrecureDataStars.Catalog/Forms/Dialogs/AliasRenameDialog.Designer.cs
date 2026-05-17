using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class AliasRenameDialog
{
    private IContainer components = null!;

    private Label lblTitle = null!;
    private Label lblOldName = null!;
    private Label lblOldNameKana = null!;

    private Label lblNewNameCaption = null!;
    private TextBox txtNewName = null!;
    private Label lblNewNameKanaCaption = null!;
    private TextBox txtNewNameKana = null!;

    private CheckBox chkSyncParent = null!;
    private Label lblWarning = null!;

    private Button btnOk = null!;
    private Button btnCancel = null!;

    /// <summary>名寄せ「改名」ダイアログのレイアウト初期化。</summary>
    private void InitializeComponent()
    {
        components = new Container();
        SuspendLayout();

        Text = "名義の改名";
        ClientSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        lblTitle = new Label
        {
            Location = new Point(16, 12),
            Size = new Size(528, 22),
            Text = "(初期化前)",
            AutoEllipsis = true,
        };

        lblOldName = new Label
        {
            Location = new Point(16, 40),
            Size = new Size(528, 22),
            Text = "(初期化前)",
            AutoEllipsis = true,
        };

        lblOldNameKana = new Label
        {
            Location = new Point(16, 64),
            Size = new Size(528, 22),
            Text = "(初期化前)",
            AutoEllipsis = true,
        };

        lblNewNameCaption = new Label
        {
            Location = new Point(16, 100),
            Size = new Size(120, 22),
            Text = "新しい name:",
        };

        txtNewName = new TextBox
        {
            Location = new Point(140, 96),
            Size = new Size(404, 24),
        };

        lblNewNameKanaCaption = new Label
        {
            Location = new Point(16, 132),
            Size = new Size(120, 22),
            Text = "新しい name_kana:",
        };

        txtNewNameKana = new TextBox
        {
            Location = new Point(140, 128),
            Size = new Size(404, 24),
        };

        chkSyncParent = new CheckBox
        {
            Location = new Point(16, 168),
            Size = new Size(528, 24),
            Text = "(初期化前)",
            Checked = false,
        };

        lblWarning = new Label
        {
            Location = new Point(16, 200),
            Size = new Size(528, 96),
            Text = "(初期化前)",
            ForeColor = Color.DarkOrange,
        };

        btnOk = new Button
        {
            Location = new Point(360, 312),
            Size = new Size(88, 32),
            Text = "改名",
        };
        btnOk.Click += OnOkClick;

        btnCancel = new Button
        {
            Location = new Point(456, 312),
            Size = new Size(88, 32),
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
        };

        Controls.Add(lblTitle);
        Controls.Add(lblOldName);
        Controls.Add(lblOldNameKana);
        Controls.Add(lblNewNameCaption);
        Controls.Add(txtNewName);
        Controls.Add(lblNewNameKanaCaption);
        Controls.Add(txtNewNameKana);
        Controls.Add(chkSyncParent);
        Controls.Add(lblWarning);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && components is not null)
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }
}