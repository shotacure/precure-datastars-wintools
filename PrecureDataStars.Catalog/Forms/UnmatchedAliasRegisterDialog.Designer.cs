#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class UnmatchedAliasRegisterDialog
{
    private System.ComponentModel.IContainer? components = null;

    // ─── 入力フィールド ───
    private Label lblSourceText = null!;
    private TextBox txtSourceText = null!;

    private GroupBox grpMode = null!;
    private RadioButton rbAttachExisting = null!;
    private RadioButton rbCreateNew = null!;

    // 既存人物を選ぶ側のコントロール
    private Label lblExistingPerson = null!;
    private TextBox txtExistingPersonDisplay = null!;
    private Button btnPickExistingPerson = null!;
    private Label lblExistingPersonIdCaption = null!;
    private Label lblExistingPersonIdValue = null!;

    // 新規人物（QuickAdd）側のコントロール
    private Label lblNewFullName = null!;
    private TextBox txtNewFullName = null!;
    private Label lblNewFullNameKana = null!;
    private TextBox txtNewFullNameKana = null!;
    private Label lblNewNameEn = null!;
    private TextBox txtNewNameEn = null!;

    // 共通：作成する alias 自体の追加属性
    private Label lblAliasNameKana = null!;
    private TextBox txtAliasNameKana = null!;
    private Label lblAliasNameEn = null!;
    private TextBox txtAliasNameEn = null!;
    private Label lblAliasDisplayOverride = null!;
    private TextBox txtAliasDisplayOverride = null!;
    private Label lblAliasNote = null!;

    // ─── 下部ボタン ───
    private Button btnOk = null!;
    private Button btnCancel = null!;
    private Label lblStatus = null!;

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
        ClientSize = new Size(600, 480);
        Name = "UnmatchedAliasRegisterDialog";
        Text = "未マッチング名義の登録";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // ─── 元テキスト表示 ───
        lblSourceText = new Label
        {
            Text = "対象テキスト:",
            Location = new Point(12, 16),
            AutoSize = true
        };
        txtSourceText = new TextBox
        {
            Location = new Point(120, 12),
            Size = new Size(460, 23),
            ReadOnly = true,
            BackColor = SystemColors.Window
        };

        // ─── モード選択 GroupBox ───
        grpMode = new GroupBox
        {
            Text = "登録モード",
            Location = new Point(12, 48),
            Size = new Size(576, 60)
        };
        rbAttachExisting = new RadioButton
        {
            Text = "既存人物の新しい名義として登録",
            Location = new Point(16, 24),
            AutoSize = true,
            Checked = true
        };
        rbCreateNew = new RadioButton
        {
            Text = "新規人物としてまとめて登録",
            Location = new Point(280, 24),
            AutoSize = true
        };
        grpMode.Controls.AddRange(new Control[] { rbAttachExisting, rbCreateNew });

        // ─── 既存人物選択ペイン（rbAttachExisting 選択時のみ有効化） ───
        lblExistingPerson = new Label
        {
            Text = "既存人物:",
            Location = new Point(12, 124),
            AutoSize = true
        };
        txtExistingPersonDisplay = new TextBox
        {
            Location = new Point(120, 120),
            Size = new Size(280, 23),
            ReadOnly = true,
            BackColor = SystemColors.Window
        };
        btnPickExistingPerson = new Button
        {
            Text = "選択...",
            Location = new Point(408, 119),
            Size = new Size(72, 25)
        };
        lblExistingPersonIdCaption = new Label
        {
            Text = "person_id:",
            Location = new Point(488, 124),
            AutoSize = true
        };
        lblExistingPersonIdValue = new Label
        {
            Text = "(未選択)",
            Location = new Point(556, 124),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        // ─── 新規人物入力ペイン（rbCreateNew 選択時のみ有効化） ───
        lblNewFullName = new Label
        {
            Text = "氏名（フル）:",
            Location = new Point(12, 156),
            AutoSize = true
        };
        txtNewFullName = new TextBox
        {
            Location = new Point(120, 152),
            Size = new Size(280, 23),
            // 既定値は対象テキストで初期化する（コードビハインドで実施）
        };
        lblNewFullNameKana = new Label
        {
            Text = "氏名かな:",
            Location = new Point(12, 188),
            AutoSize = true
        };
        txtNewFullNameKana = new TextBox
        {
            Location = new Point(120, 184),
            Size = new Size(280, 23)
        };
        lblNewNameEn = new Label
        {
            Text = "氏名(英):",
            Location = new Point(12, 220),
            AutoSize = true
        };
        txtNewNameEn = new TextBox
        {
            Location = new Point(120, 216),
            Size = new Size(280, 23)
        };

        // ─── 共通：作成する alias 自体の追加属性 ───
        lblAliasNote = new Label
        {
            Text = "alias 作成情報（既存人物モード：name=対象テキスト＋かな/英語ここで指定。新規人物モード：name/かな/英語は人物欄の値が自動採用）",
            Location = new Point(12, 256),
            AutoSize = true,
            ForeColor = Color.DimGray
        };
        lblAliasNameKana = new Label
        {
            Text = "名義かな:",
            Location = new Point(12, 280),
            AutoSize = true
        };
        txtAliasNameKana = new TextBox
        {
            Location = new Point(120, 276),
            Size = new Size(280, 23)
        };
        lblAliasNameEn = new Label
        {
            Text = "名義(英):",
            Location = new Point(12, 312),
            AutoSize = true
        };
        txtAliasNameEn = new TextBox
        {
            Location = new Point(120, 308),
            Size = new Size(280, 23)
        };
        lblAliasDisplayOverride = new Label
        {
            Text = "表示上書:",
            Location = new Point(12, 344),
            AutoSize = true
        };
        txtAliasDisplayOverride = new TextBox
        {
            Location = new Point(120, 340),
            Size = new Size(460, 23)
        };

        // ─── 下部ボタン ───
        btnOk = new Button
        {
            Text = "登録",
            Location = new Point(404, 408),
            Size = new Size(88, 28),
            DialogResult = DialogResult.OK
        };
        btnCancel = new Button
        {
            Text = "キャンセル",
            Location = new Point(500, 408),
            Size = new Size(88, 28),
            DialogResult = DialogResult.Cancel
        };
        lblStatus = new Label
        {
            Text = "",
            Location = new Point(12, 412),
            Size = new Size(380, 22),
            ForeColor = Color.DimGray,
            AutoEllipsis = true
        };

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.Add(lblSourceText);
        Controls.Add(txtSourceText);
        Controls.Add(grpMode);
        Controls.Add(lblExistingPerson);
        Controls.Add(txtExistingPersonDisplay);
        Controls.Add(btnPickExistingPerson);
        Controls.Add(lblExistingPersonIdCaption);
        Controls.Add(lblExistingPersonIdValue);
        Controls.Add(lblNewFullName);
        Controls.Add(txtNewFullName);
        Controls.Add(lblNewFullNameKana);
        Controls.Add(txtNewFullNameKana);
        Controls.Add(lblNewNameEn);
        Controls.Add(txtNewNameEn);
        Controls.Add(lblAliasNote);
        Controls.Add(lblAliasNameKana);
        Controls.Add(txtAliasNameKana);
        Controls.Add(lblAliasNameEn);
        Controls.Add(txtAliasNameEn);
        Controls.Add(lblAliasDisplayOverride);
        Controls.Add(txtAliasDisplayOverride);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        Controls.Add(lblStatus);

        ResumeLayout(performLayout: false);
        PerformLayout();
    }
}