using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class AliasReassignDialog
{
    private IContainer components = null!;

    private Label lblTitle = null!;
    private Label lblCurrentParent = null!;
    private Label lblPickHint = null!;
    private Button btnPickParent = null!;
    private Label lblNewParent = null!;
    private Label lblWarning = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    /// <summary>名寄せ「付け替え」ダイアログのレイアウト初期化（v1.2.1）。</summary>
    private void InitializeComponent()
    {
        components = new Container();
        SuspendLayout();

        Text = "名義の付け替え";
        ClientSize = new Size(560, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        lblTitle = new Label
        {
            Location = new Point(16, 12),
            Size = new Size(528, 36),
            Text = "(初期化前)",
            AutoEllipsis = true,
        };

        lblCurrentParent = new Label
        {
            Location = new Point(16, 52),
            Size = new Size(528, 22),
            Text = "(初期化前)",
            AutoEllipsis = true,
        };

        lblPickHint = new Label
        {
            Location = new Point(16, 84),
            Size = new Size(528, 22),
            Text = "(初期化前)",
        };

        btnPickParent = new Button
        {
            Location = new Point(16, 110),
            Size = new Size(160, 32),
            Text = "(初期化前)",
        };
        btnPickParent.Click += OnPickParentClick;

        lblNewParent = new Label
        {
            Location = new Point(184, 116),
            Size = new Size(360, 22),
            Text = "(未選択)",
            AutoEllipsis = true,
        };

        lblWarning = new Label
        {
            Location = new Point(16, 152),
            Size = new Size(528, 70),
            Text = "(初期化前)",
            ForeColor = Color.DarkOrange,
        };

        btnOk = new Button
        {
            Location = new Point(360, 232),
            Size = new Size(88, 32),
            Text = "付け替え",
            Enabled = false,
        };
        btnOk.Click += OnOkClick;

        btnCancel = new Button
        {
            Location = new Point(456, 232),
            Size = new Size(88, 32),
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
        };

        Controls.Add(lblTitle);
        Controls.Add(lblCurrentParent);
        Controls.Add(lblPickHint);
        Controls.Add(btnPickParent);
        Controls.Add(lblNewParent);
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
