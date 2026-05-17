#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class MovieBgmCuesEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    // ─── 上部：映画シリーズ選択 ───
    private Label lblSeries = null!;
    private ComboBox cboSeries = null!;

    // ─── 操作ボタン ───
    private Button btnAddRow = null!;
    private Button btnSave = null!;
    private Button btnDelete = null!;
    private Button btnClose = null!;

    // ─── 中央：映画 BGM キューのグリッド ───
    private DataGridView gridCues = null!;
    private DataGridViewTextBoxColumn colSeq = null!;
    private DataGridViewTextBoxColumn colSubSeq = null!;
    private DataGridViewTextBoxColumn colMNo = null!;
    private DataGridViewComboBoxColumn colKind = null!;
    private DataGridViewTextBoxColumn colTitle = null!;
    private DataGridViewCheckBoxColumn colUnused = null!;
    private DataGridViewCheckBoxColumn colMissing = null!;
    private DataGridViewTextBoxColumn colNotes = null!;

    // ─── 下部：ステータス ───
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
        ClientSize = new Size(1100, 620);
        Name = "MovieBgmCuesEditorForm";
        Text = "映画 BGM リスト管理";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 520);

        lblSeries = new Label
        {
            AutoSize = true,
            Location = new Point(12, 16),
            Text = "映画シリーズ:",
        };
        cboSeries = new ComboBox
        {
            Location = new Point(104, 12),
            Size = new Size(420, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
        };

        btnAddRow = new Button
        {
            Text = "行を追加",
            Location = new Point(540, 11),
            Size = new Size(96, 26),
        };
        btnSave = new Button
        {
            Text = "保存",
            Location = new Point(644, 11),
            Size = new Size(96, 26),
        };
        btnDelete = new Button
        {
            Text = "選択行を削除",
            Location = new Point(748, 11),
            Size = new Size(110, 26),
        };
        btnClose = new Button
        {
            Text = "閉じる",
            Location = new Point(972, 11),
            Size = new Size(116, 26),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        // グリッド列定義。DataPropertyName は MovieBgmCue のプロパティ名に対応。
        colSeq = new DataGridViewTextBoxColumn
        {
            HeaderText = "順序",
            DataPropertyName = "Seq",
            Width = 60,
        };
        colSubSeq = new DataGridViewTextBoxColumn
        {
            HeaderText = "サブ",
            DataPropertyName = "SubSeq",
            Width = 60,
        };
        colMNo = new DataGridViewTextBoxColumn
        {
            HeaderText = "M番号",
            DataPropertyName = "MNo",
            Width = 110,
        };
        colKind = new DataGridViewComboBoxColumn
        {
            HeaderText = "区分",
            DataPropertyName = "ContentKindCode",
            Width = 110,
            FlatStyle = FlatStyle.Flat,
        };
        colTitle = new DataGridViewTextBoxColumn
        {
            HeaderText = "曲名 / メニュー表記",
            DataPropertyName = "Title",
            Width = 240,
        };
        colUnused = new DataGridViewCheckBoxColumn
        {
            HeaderText = "未使用",
            DataPropertyName = "IsUnused",
            Width = 60,
            ToolTipText = "音源は存在するが本編で未使用",
        };
        colMissing = new DataGridViewCheckBoxColumn
        {
            HeaderText = "欠番",
            DataPropertyName = "IsMissing",
            Width = 60,
            ToolTipText = "そもそも制作されていない（音源なし）。未使用とは併用不可",
        };
        colNotes = new DataGridViewTextBoxColumn
        {
            HeaderText = "備考",
            DataPropertyName = "Notes",
            Width = 220,
        };

        gridCues = new DataGridView
        {
            Location = new Point(12, 48),
            Size = new Size(1076, 540),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                   | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        };
        gridCues.Columns.AddRange(new DataGridViewColumn[]
        {
            colSeq, colSubSeq, colMNo, colKind, colTitle, colUnused, colMissing, colNotes,
        });

        lblStatus = new Label
        {
            AutoSize = false,
            Location = new Point(12, 596),
            Size = new Size(1076, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Text = "",
        };

        Controls.Add(lblSeries);
        Controls.Add(cboSeries);
        Controls.Add(btnAddRow);
        Controls.Add(btnSave);
        Controls.Add(btnDelete);
        Controls.Add(btnClose);
        Controls.Add(gridCues);
        Controls.Add(lblStatus);

        ResumeLayout(false);
        PerformLayout();
    }
}
