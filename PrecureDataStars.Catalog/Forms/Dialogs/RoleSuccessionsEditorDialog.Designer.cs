namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 役職系譜編集ダイアログ。
/// <para>
/// 1 つの role_code を中心に、その役職と <c>role_successions</c> 関係テーブル経由でつながる
/// 「前任（この役職に移行された旧役職）」と「後任（この役職から派生した新役職）」を編集する。
/// </para>
/// <para>
/// レイアウト：縦に 2 つの GroupBox を重ねる。
/// 上：前任セクション（ListBox + [追加] [削除] ボタン）。
/// 下：後任セクション（ListBox + [追加] [削除] ボタン）。
/// 最下部：[閉じる] ボタン 1 つ。保存タイミングは即時（追加 / 削除のたびに DB 反映）。
/// </para>
/// <para>
/// 動的生成方式：
/// 役職タブの編集ペインに動的追加された [系譜...] ボタンから本ダイアログが開く。
/// 編集対象 role_code はダイアログのコンストラクタで受け取り、ダイアログ表示中はその役職に固定する。
/// </para>
/// </summary>
partial class RoleSuccessionsEditorDialog
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label lblHeader;
    private System.Windows.Forms.GroupBox grpFromList;
    private System.Windows.Forms.ListBox lstFrom;
    private System.Windows.Forms.Button btnAddFrom;
    private System.Windows.Forms.Button btnRemoveFrom;
    private System.Windows.Forms.GroupBox grpToList;
    private System.Windows.Forms.ListBox lstTo;
    private System.Windows.Forms.Button btnAddTo;
    private System.Windows.Forms.Button btnRemoveTo;
    private System.Windows.Forms.Button btnClose;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();

        this.lblHeader = new System.Windows.Forms.Label();
        this.grpFromList = new System.Windows.Forms.GroupBox();
        this.lstFrom = new System.Windows.Forms.ListBox();
        this.btnAddFrom = new System.Windows.Forms.Button();
        this.btnRemoveFrom = new System.Windows.Forms.Button();
        this.grpToList = new System.Windows.Forms.GroupBox();
        this.lstTo = new System.Windows.Forms.ListBox();
        this.btnAddTo = new System.Windows.Forms.Button();
        this.btnRemoveTo = new System.Windows.Forms.Button();
        this.btnClose = new System.Windows.Forms.Button();

        this.SuspendLayout();
        this.grpFromList.SuspendLayout();
        this.grpToList.SuspendLayout();

        // ── lblHeader ─────────────────────────────────
        this.lblHeader.AutoSize = false;
        this.lblHeader.Location = new System.Drawing.Point(12, 9);
        this.lblHeader.Size = new System.Drawing.Size(540, 24);
        this.lblHeader.Text = "役職系譜の編集";
        this.lblHeader.Font = new System.Drawing.Font("Yu Gothic UI", 10F, System.Drawing.FontStyle.Bold);

        // ── grpFromList（上：前任）─────────────────────
        // 「前任」= この役職に移行された旧役職（role_successions.from_role_code = 旧、to_role_code = 編集対象役職）
        this.grpFromList.Text = "前任（この役職に移行された旧役職）";
        this.grpFromList.Location = new System.Drawing.Point(12, 40);
        this.grpFromList.Size = new System.Drawing.Size(540, 180);

        this.lstFrom.Location = new System.Drawing.Point(10, 22);
        this.lstFrom.Size = new System.Drawing.Size(420, 150);
        this.lstFrom.IntegralHeight = false;

        this.btnAddFrom.Location = new System.Drawing.Point(440, 22);
        this.btnAddFrom.Size = new System.Drawing.Size(90, 30);
        this.btnAddFrom.Text = "追加…";

        this.btnRemoveFrom.Location = new System.Drawing.Point(440, 60);
        this.btnRemoveFrom.Size = new System.Drawing.Size(90, 30);
        this.btnRemoveFrom.Text = "削除";

        this.grpFromList.Controls.Add(this.lstFrom);
        this.grpFromList.Controls.Add(this.btnAddFrom);
        this.grpFromList.Controls.Add(this.btnRemoveFrom);

        // ── grpToList（下：後任）─────────────────────
        // 「後任」= この役職から派生した新役職（role_successions.from_role_code = 編集対象、to_role_code = 新）
        this.grpToList.Text = "後任（この役職から派生した新役職）";
        this.grpToList.Location = new System.Drawing.Point(12, 230);
        this.grpToList.Size = new System.Drawing.Size(540, 180);

        this.lstTo.Location = new System.Drawing.Point(10, 22);
        this.lstTo.Size = new System.Drawing.Size(420, 150);
        this.lstTo.IntegralHeight = false;

        this.btnAddTo.Location = new System.Drawing.Point(440, 22);
        this.btnAddTo.Size = new System.Drawing.Size(90, 30);
        this.btnAddTo.Text = "追加…";

        this.btnRemoveTo.Location = new System.Drawing.Point(440, 60);
        this.btnRemoveTo.Size = new System.Drawing.Size(90, 30);
        this.btnRemoveTo.Text = "削除";

        this.grpToList.Controls.Add(this.lstTo);
        this.grpToList.Controls.Add(this.btnAddTo);
        this.grpToList.Controls.Add(this.btnRemoveTo);

        // ── btnClose（最下部 1 ボタン）─────────────────
        this.btnClose.Location = new System.Drawing.Point(462, 425);
        this.btnClose.Size = new System.Drawing.Size(90, 30);
        this.btnClose.Text = "閉じる";

        // ── ダイアログ全体 ─────────────────────────────
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(564, 470);
        this.Controls.Add(this.lblHeader);
        this.Controls.Add(this.grpFromList);
        this.Controls.Add(this.grpToList);
        this.Controls.Add(this.btnClose);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "役職系譜編集";

        this.grpFromList.ResumeLayout(false);
        this.grpToList.ResumeLayout(false);
        this.ResumeLayout(false);
    }
}