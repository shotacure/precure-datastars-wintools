#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms;

partial class ProductCompaniesEditorForm
{
    private System.ComponentModel.IContainer? components = null;

    private SplitContainer splitMain = null!;

    // ── 左ペイン：一覧 + 再読込ボタン ──
    private Panel pnlListHeader = null!;
    private Button btnReload = null!;
    private DataGridView gridItems = null!;

    // ── 右ペイン：詳細フォーム ──
    private Panel pnlDetail = null!;
    private NumericUpDown numId = null!;
    private TextBox txtNameJa = null!;
    private TextBox txtNameKana = null!;
    private TextBox txtNameEn = null!;
    // 新規商品作成時の既定社を指定するフラグ。
    // チェック ON で保存すると、Repository 側でマスタ全体の同フラグを 0 に落としてから本行に立てる。
    private CheckBox chkIsDefaultLabel = null!;
    private CheckBox chkIsDefaultDistributor = null!;
    private TextBox txtNotes = null!;
    private Button btnNew = null!;
    private Button btnSave = null!;
    private Button btnDelete = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// レイアウト初期化。左右分割の SplitContainer に、左ペインへ一覧グリッドとヘッダー
    /// （再読込ボタン）、右ペインへ詳細フォーム（5 フィールド + 既定フラグ 2 つ + 3 ボタン）を配置する。
    /// </summary>
    private void InitializeComponent()
    {
        splitMain = new SplitContainer();
        pnlListHeader = new Panel();
        btnReload = new Button();
        gridItems = new DataGridView();
        pnlDetail = new Panel();
        numId = new NumericUpDown();
        txtNameJa = new TextBox();
        txtNameKana = new TextBox();
        txtNameEn = new TextBox();
        chkIsDefaultLabel = new CheckBox();
        chkIsDefaultDistributor = new CheckBox();
        txtNotes = new TextBox();
        btnNew = new Button();
        btnSave = new Button();
        btnDelete = new Button();

        // SplitContainer：左右分割。
        // Designer 段階では SplitContainer がまだ親 Form に追加されていないため、
        // Width は SplitContainer のデフォルト値（150）しか持っていない。
        // この状態で Panel1MinSize / Panel2MinSize を大きい値（200/280 等）に設定したり、
        // SplitterDistance を大きい値（520 等）に設定したりすると、いずれも
        // 「SplitterDistance は Panel1MinSize と幅 Panel2MinSize の間でなければなりません」
        // (InvalidOperationException) で弾かれる。
        // よって Designer では SplitContainer の最小限の属性のみ設定し、
        // SplitterDistance / Panel1MinSize / Panel2MinSize の実値は Form.Load で
        // ClientSize 確定後に設定する（実装側 .cs の InitializeSplitterLayout）。
        splitMain.Dock = DockStyle.Fill;
        splitMain.Orientation = Orientation.Vertical;
        splitMain.SplitterWidth = 6;
        splitMain.SplitterDistance = 100;

        // ── 左ペイン ──
        pnlListHeader.Dock = DockStyle.Top;
        pnlListHeader.Height = 32;

        btnReload.Text = "再読込";
        btnReload.Location = new Point(8, 4);
        btnReload.Size = new Size(80, 24);
        pnlListHeader.Controls.Add(btnReload);

        gridItems.Dock = DockStyle.Fill;
        gridItems.AllowUserToAddRows = false;
        gridItems.AllowUserToDeleteRows = false;
        gridItems.ReadOnly = true;
        gridItems.RowHeadersVisible = false;
        gridItems.MultiSelect = false;
        gridItems.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        gridItems.AutoGenerateColumns = false;
        gridItems.DefaultCellStyle.Font = new Font("Yu Gothic UI", 9F);

        splitMain.Panel1.Controls.Add(gridItems);
        splitMain.Panel1.Controls.Add(pnlListHeader);

        // ── 右ペイン ──
        pnlDetail.Dock = DockStyle.Fill;
        pnlDetail.AutoScroll = true;

        const int labelW = 100;
        const int fieldX = 22 + labelW;
        const int fieldW = 240;
        const int rowH = 28;
        int y = 8;

        // ID（読み取り専用表示。新規時は 0）
        AddRow(pnlDetail, "ID", numId, y, labelW, fieldW); y += rowH;
        numId.Minimum = 0;
        numId.Maximum = int.MaxValue;
        numId.ReadOnly = true;
        numId.Increment = 0;
        numId.BackColor = SystemColors.Control;

        AddRow(pnlDetail, "社名(和)", txtNameJa, y, labelW, fieldW); y += rowH;
        txtNameJa.MaxLength = 128;

        AddRow(pnlDetail, "かな", txtNameKana, y, labelW, fieldW); y += rowH;
        txtNameKana.MaxLength = 128;

        AddRow(pnlDetail, "英名", txtNameEn, y, labelW, fieldW); y += rowH;
        txtNameEn.MaxLength = 128;

        // 既定フラグ 2 つを通常入力欄の下に並べる。
        // ラベル列は使わず、CheckBox の Text に説明文を入れることで「ON にすると新規商品作成時の
        // 既定社になる」ことを直感的に示す。位置は fieldX に揃えて他の入力欄と縦線が合うようにする。
        chkIsDefaultLabel.Text = "新規商品作成時のレーベル既定にする";
        chkIsDefaultLabel.Location = new Point(fieldX, y + 4);
        chkIsDefaultLabel.AutoSize = true;
        pnlDetail.Controls.Add(chkIsDefaultLabel);
        y += rowH;

        chkIsDefaultDistributor.Text = "新規商品作成時の販売元既定にする";
        chkIsDefaultDistributor.Location = new Point(fieldX, y + 4);
        chkIsDefaultDistributor.AutoSize = true;
        pnlDetail.Controls.Add(chkIsDefaultDistributor);
        y += rowH;

        // 備考は複数行
        var lblNotes = new Label
        {
            Text = "備考",
            Location = new Point(18, y + 4),
            Size = new Size(labelW, 20)
        };
        txtNotes.Location = new Point(fieldX, y);
        txtNotes.Size = new Size(fieldW, 80);
        txtNotes.Multiline = true;
        txtNotes.ScrollBars = ScrollBars.Vertical;
        pnlDetail.Controls.Add(lblNotes);
        pnlDetail.Controls.Add(txtNotes);

        // ボタン列（パネル右側に縦並び）
        int btnX = fieldX + fieldW + 16;
        btnNew.Text = "新規"; btnNew.Size = new Size(80, 28); btnNew.Location = new Point(btnX, 8);
        btnSave.Text = "保存"; btnSave.Size = new Size(80, 28); btnSave.Location = new Point(btnX, 40);
        btnDelete.Text = "削除"; btnDelete.Size = new Size(80, 28); btnDelete.Location = new Point(btnX, 72);
        pnlDetail.Controls.AddRange(new Control[] { btnNew, btnSave, btnDelete });

        splitMain.Panel2.Controls.Add(pnlDetail);

        // ── Form ──
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(900, 540);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(splitMain);
        Name = "ProductCompaniesEditorForm";
        Text = "商品社名マスタ管理 - Catalog";
    }

    /// <summary>
    /// ラベル + 入力コントロールを指定 y 座標の行として配置する。
    /// 他のエディタフォームと同じく、ラベル左端を x=18 に、入力欄左端を x=22+labelW に置いて
    /// 統一感のあるパディングを確保する。
    /// </summary>
    private static void AddRow(Panel panel, string label, Control control, int y, int labelW, int fieldW)
    {
        var lbl = new Label { Text = label, Location = new Point(18, y + 4), Size = new Size(labelW, 20) };
        control.Location = new Point(22 + labelW, y);
        control.Size = new Size(fieldW, 23);
        panel.Controls.Add(lbl);
        panel.Controls.Add(control);
    }
}