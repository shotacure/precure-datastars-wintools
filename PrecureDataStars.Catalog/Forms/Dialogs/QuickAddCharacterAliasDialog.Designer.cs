#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class QuickAddCharacterAliasDialog
{
    private System.ComponentModel.IContainer? components = null;

    // モード選択
    private GroupBox grpMode = null!;
    private RadioButton rbModeExisting = null!;
    private RadioButton rbModeNewCharacter = null!;

    // モード A：既存キャラに名義を追加
    private Panel pnlExisting = null!;
    private Label lblParentCharacterCaption = null!;
    private Label lblParentCharacterValue = null!;
    private Button btnPickParentCharacter = null!;
    private Label lblExistingAliasNameCaption = null!;
    private TextBox txtExistingAliasName = null!;
    private Label lblExistingAliasKanaCaption = null!;
    private TextBox txtExistingAliasKana = null!;

    // モード B：キャラごと新規作成
    private Panel pnlNewCharacter = null!;
    private Label lblNewCharacterNameCaption = null!;
    private TextBox txtNewCharacterName = null!;
    private Label lblNewCharacterKanaCaption = null!;
    private TextBox txtNewCharacterKana = null!;
    private Label lblCharacterKindCaption = null!;
    private ComboBox cboCharacterKind = null!;
    private Label lblNewCharacterNotesCaption = null!;
    private TextBox txtNewCharacterNotes = null!;
    private Label lblNewAliasNoticeLabel = null!;

    // ボタン
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(500, 480);
        Name = "QuickAddCharacterAliasDialog";
        Text = "キャラクター名義の即時追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        // ─── モード選択 GroupBox ───
        grpMode = new GroupBox
        {
            Text = "モード",
            Location = new Point(12, 12),
            Size = new Size(476, 76)
        };
        rbModeExisting = new RadioButton
        {
            Text = "既存のキャラクターに名義を追加する",
            Location = new Point(12, 22),
            Size = new Size(440, 22),
            Checked = true
        };
        rbModeNewCharacter = new RadioButton
        {
            Text = "キャラクターごと新規作成する（characters + character_aliases を 1 トランザクションで投入）",
            Location = new Point(12, 46),
            Size = new Size(440, 22)
        };
        grpMode.Controls.AddRange(new Control[] { rbModeExisting, rbModeNewCharacter });

        // ─── モード A 用 Panel（既存キャラに名義追加）───
        pnlExisting = new Panel
        {
            Location = new Point(12, 96),
            Size = new Size(476, 310),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = true
        };
        lblParentCharacterCaption = new Label { Text = "親キャラクター（必須）:", Location = new Point(12, 16), Size = new Size(140, 20) };
        lblParentCharacterValue   = new Label
        {
            Location = new Point(12, 40),
            Size = new Size(350, 22),
            Text = "（未選択）",
            BorderStyle = BorderStyle.FixedSingle
        };
        btnPickParentCharacter = new Button
        {
            Text = "選択...",
            Location = new Point(368, 38),
            Size = new Size(94, 26)
        };
        lblExistingAliasNameCaption = new Label { Text = "名義名（必須）:", Location = new Point(12, 80), Size = new Size(140, 20) };
        txtExistingAliasName        = new TextBox { Location = new Point(160, 78), Size = new Size(302, 23) };
        lblExistingAliasKanaCaption = new Label { Text = "名義かな:", Location = new Point(12, 112), Size = new Size(140, 20) };
        txtExistingAliasKana        = new TextBox { Location = new Point(160, 110), Size = new Size(302, 23) };
        pnlExisting.Controls.AddRange(new Control[]
        {
            lblParentCharacterCaption, lblParentCharacterValue, btnPickParentCharacter,
            lblExistingAliasNameCaption, txtExistingAliasName,
            lblExistingAliasKanaCaption, txtExistingAliasKana
        });

        // ─── モード B 用 Panel（キャラごと新規作成）───
        pnlNewCharacter = new Panel
        {
            Location = new Point(12, 96),
            Size = new Size(476, 310),
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };
        lblNewCharacterNameCaption = new Label { Text = "キャラ名（必須）:", Location = new Point(12, 16), Size = new Size(140, 20) };
        txtNewCharacterName        = new TextBox
        {
            Location = new Point(160, 14),
            Size = new Size(302, 23),
            PlaceholderText = "例: キュアブラック / 美墨なぎさ"
        };
        lblNewCharacterKanaCaption = new Label { Text = "キャラかな:", Location = new Point(12, 48), Size = new Size(140, 20) };
        txtNewCharacterKana        = new TextBox { Location = new Point(160, 46), Size = new Size(302, 23) };

        // 区分（character_kinds マスタから取得して投入）
        lblCharacterKindCaption = new Label { Text = "区分（必須）:", Location = new Point(12, 80), Size = new Size(140, 20) };
        cboCharacterKind = new ComboBox
        {
            Location = new Point(160, 78),
            Size = new Size(302, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
            // Items はコード側 OnLoadAsync で character_kinds マスタから流し込む
        };

        lblNewCharacterNotesCaption = new Label { Text = "備考:", Location = new Point(12, 112), Size = new Size(140, 20) };
        txtNewCharacterNotes        = new TextBox
        {
            Location = new Point(160, 112),
            Size = new Size(302, 60),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical
        };

        lblNewAliasNoticeLabel = new Label
        {
            Text = "※ 名義は character_aliases にもキャラ名と同じものが自動投入されます。\n   別表記が必要なら、登録後にマスタ管理画面で名義を追加してください。",
            Location = new Point(12, 184),
            Size = new Size(450, 40),
            ForeColor = SystemColors.GrayText
        };
        pnlNewCharacter.Controls.AddRange(new Control[]
        {
            lblNewCharacterNameCaption, txtNewCharacterName,
            lblNewCharacterKanaCaption, txtNewCharacterKana,
            lblCharacterKindCaption, cboCharacterKind,
            lblNewCharacterNotesCaption, txtNewCharacterNotes,
            lblNewAliasNoticeLabel
        });

        // ─── ボタン ───
        btnOk = new Button
        {
            Text = "登録",
            Location = new Point(292, 432),
            Size = new Size(94, 30)
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(394, 432),
            Size = new Size(94, 30),
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            grpMode, pnlExisting, pnlNewCharacter, btnOk, btnCancel
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}