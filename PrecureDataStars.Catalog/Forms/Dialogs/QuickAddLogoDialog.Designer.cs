#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class QuickAddLogoDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblHeader = null!;
    private Label lblParentAliasCaption = null!;
    private Label lblParentAliasValue = null!;
    private Button btnPickParentAlias = null!;
    private Label lblCiVersionLabelCaption = null!;
    private TextBox txtCiVersionLabel = null!;
    private Label lblValidFromCaption = null!;
    private DateTimePicker dtpValidFrom = null!;
    private CheckBox chkValidFromEnabled = null!;
    private Label lblValidToCaption = null!;
    private DateTimePicker dtpValidTo = null!;
    private CheckBox chkValidToEnabled = null!;
    private Label lblDescriptionCaption = null!;
    private TextBox txtDescription = null!;
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
        ClientSize = new Size(480, 360);
        Name = "QuickAddLogoDialog";
        Text = "ロゴの即時追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        lblHeader = new Label
        {
            Text = "新しいロゴを既存の企業屋号に紐付けて登録します。\n"
                + "（屋号がまだ無い場合は、先に屋号を追加してから戻ってください）",
            Location = new Point(16, 12),
            Size = new Size(448, 36),
            ForeColor = SystemColors.GrayText
        };

        // 親屋号
        lblParentAliasCaption = new Label { Text = "屋号（必須）:", Location = new Point(16, 60), Size = new Size(110, 20) };
        lblParentAliasValue   = new Label
        {
            Location = new Point(140, 58),
            Size = new Size(232, 22),
            Text = "（未選択）",
            BorderStyle = BorderStyle.FixedSingle
        };
        btnPickParentAlias = new Button
        {
            Text = "選択...",
            Location = new Point(378, 56),
            Size = new Size(86, 26)
        };

        // CI バージョンラベル
        lblCiVersionLabelCaption = new Label { Text = "CI バージョン（必須）:", Location = new Point(16, 96), Size = new Size(150, 20) };
        txtCiVersionLabel        = new TextBox
        {
            Location = new Point(176, 94),
            Size = new Size(288, 23),
            PlaceholderText = "例: 東映A 2010 / ABC 1990s 等"
        };

        // 有効期間
        lblValidFromCaption = new Label { Text = "有効期間 開始:", Location = new Point(16, 132), Size = new Size(110, 20) };
        chkValidFromEnabled = new CheckBox { Text = "指定", Location = new Point(140, 132), Size = new Size(60, 22) };
        dtpValidFrom        = new DateTimePicker { Location = new Point(206, 130), Size = new Size(170, 23), Enabled = false };

        lblValidToCaption = new Label { Text = "有効期間 終了:", Location = new Point(16, 164), Size = new Size(110, 20) };
        chkValidToEnabled = new CheckBox { Text = "指定", Location = new Point(140, 164), Size = new Size(60, 22) };
        dtpValidTo        = new DateTimePicker { Location = new Point(206, 162), Size = new Size(170, 23), Enabled = false };

        // 説明
        lblDescriptionCaption = new Label { Text = "説明:", Location = new Point(16, 196), Size = new Size(110, 20) };
        txtDescription        = new TextBox
        {
            Location = new Point(140, 196),
            Size = new Size(324, 76),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        btnOk = new Button
        {
            Text = "登録",
            Location = new Point(272, 312),
            Size = new Size(94, 30)
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(374, 312),
            Size = new Size(94, 30),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            lblHeader,
            lblParentAliasCaption, lblParentAliasValue, btnPickParentAlias,
            lblCiVersionLabelCaption, txtCiVersionLabel,
            lblValidFromCaption, chkValidFromEnabled, dtpValidFrom,
            lblValidToCaption,   chkValidToEnabled,   dtpValidTo,
            lblDescriptionCaption, txtDescription,
            btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        chkValidFromEnabled.CheckedChanged += (_, __) => dtpValidFrom.Enabled = chkValidFromEnabled.Checked;
        chkValidToEnabled.CheckedChanged   += (_, __) => dtpValidTo.Enabled   = chkValidToEnabled.Checked;
    }
}