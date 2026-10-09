namespace PrecureDataStars.Catalog.Forms.Dialogs;

/// <summary>
/// 役職どうしの関連（段階・並列）の編集ダイアログ。
/// <para>
/// 1 つの role_code を中心に、<c>role_relations</c> でつながる
/// 「前段階（この役職の前に担うことの多い役職）」「並列（この役職と並んで担う役職）」
/// 「後段階（この役職の後に担うことの多い役職）」を編集する。
/// </para>
/// <para>
/// レイアウト：縦に 3 つの GroupBox を重ねる（上から前段階・並列・後段階）。各 GroupBox は ListBox + [追加…] [削除]。
/// 最下部：[閉じる] ボタン 1 つ。保存タイミングは即時（追加 / 削除のたびに DB 反映）。
/// 役職タブの [関連…] ボタンから開き、編集対象 role_code はダイアログ表示中その役職に固定する。
/// </para>
/// </summary>
partial class RoleRelationsEditorDialog
{
    private System.ComponentModel.IContainer components = null;

    private System.Windows.Forms.Label lblHeader;
    private System.Windows.Forms.GroupBox grpBefore;
    private System.Windows.Forms.ListBox lstBefore;
    private System.Windows.Forms.Button btnAddBefore;
    private System.Windows.Forms.Button btnRemoveBefore;
    private System.Windows.Forms.GroupBox grpParallel;
    private System.Windows.Forms.ListBox lstParallel;
    private System.Windows.Forms.Button btnAddParallel;
    private System.Windows.Forms.Button btnRemoveParallel;
    private System.Windows.Forms.GroupBox grpAfter;
    private System.Windows.Forms.ListBox lstAfter;
    private System.Windows.Forms.Button btnAddAfter;
    private System.Windows.Forms.Button btnRemoveAfter;
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
        this.grpBefore = new System.Windows.Forms.GroupBox();
        this.lstBefore = new System.Windows.Forms.ListBox();
        this.btnAddBefore = new System.Windows.Forms.Button();
        this.btnRemoveBefore = new System.Windows.Forms.Button();
        this.grpParallel = new System.Windows.Forms.GroupBox();
        this.lstParallel = new System.Windows.Forms.ListBox();
        this.btnAddParallel = new System.Windows.Forms.Button();
        this.btnRemoveParallel = new System.Windows.Forms.Button();
        this.grpAfter = new System.Windows.Forms.GroupBox();
        this.lstAfter = new System.Windows.Forms.ListBox();
        this.btnAddAfter = new System.Windows.Forms.Button();
        this.btnRemoveAfter = new System.Windows.Forms.Button();
        this.btnClose = new System.Windows.Forms.Button();

        this.SuspendLayout();
        this.grpBefore.SuspendLayout();
        this.grpParallel.SuspendLayout();
        this.grpAfter.SuspendLayout();

        // ── lblHeader ─────────────────────────────────
        this.lblHeader.AutoSize = false;
        this.lblHeader.Location = new System.Drawing.Point(12, 9);
        this.lblHeader.Size = new System.Drawing.Size(540, 24);
        this.lblHeader.Text = "役職の関連の編集";
        this.lblHeader.Font = new System.Drawing.Font("Yu Gothic UI", 10F, System.Drawing.FontStyle.Bold);

        // ── grpBefore（上：前段階）─────────────────────
        // 「前段階」= role_relations の STEP_UP で to_role_code = 編集対象役職の行の from_role_code
        SetupSection(this.grpBefore, this.lstBefore, this.btnAddBefore, this.btnRemoveBefore,
            "前段階（この役職の前に担うことの多い役職）", 40);

        // ── grpParallel（中：並列）─────────────────────
        // 「並列」= role_relations の PARALLEL で編集対象役職を from / to のどちらかに持つ行の相手側
        SetupSection(this.grpParallel, this.lstParallel, this.btnAddParallel, this.btnRemoveParallel,
            "並列（この役職と並んで担う役職）", 200);

        // ── grpAfter（下：後段階）─────────────────────
        // 「後段階」= role_relations の STEP_UP で from_role_code = 編集対象役職の行の to_role_code
        SetupSection(this.grpAfter, this.lstAfter, this.btnAddAfter, this.btnRemoveAfter,
            "後段階（この役職の後に担うことの多い役職）", 360);

        // ── btnClose（最下部 1 ボタン）─────────────────
        this.btnClose.Location = new System.Drawing.Point(462, 525);
        this.btnClose.Size = new System.Drawing.Size(90, 30);
        this.btnClose.Text = "閉じる";

        // ── ダイアログ全体 ─────────────────────────────
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(564, 570);
        this.Controls.Add(this.lblHeader);
        this.Controls.Add(this.grpBefore);
        this.Controls.Add(this.grpParallel);
        this.Controls.Add(this.grpAfter);
        this.Controls.Add(this.btnClose);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        this.Text = "役職の関連の編集";

        this.grpBefore.ResumeLayout(false);
        this.grpParallel.ResumeLayout(false);
        this.grpAfter.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    /// <summary>1 セクション（GroupBox + ListBox + [追加…] [削除]）の配置を組む。<paramref name="top"/> は GroupBox の上端。</summary>
    private static void SetupSection(
        System.Windows.Forms.GroupBox grp,
        System.Windows.Forms.ListBox lst,
        System.Windows.Forms.Button btnAdd,
        System.Windows.Forms.Button btnRemove,
        string title,
        int top)
    {
        grp.Text = title;
        grp.Location = new System.Drawing.Point(12, top);
        grp.Size = new System.Drawing.Size(540, 150);

        lst.Location = new System.Drawing.Point(10, 22);
        lst.Size = new System.Drawing.Size(420, 120);
        lst.IntegralHeight = false;

        btnAdd.Location = new System.Drawing.Point(440, 22);
        btnAdd.Size = new System.Drawing.Size(90, 30);
        btnAdd.Text = "追加…";

        btnRemove.Location = new System.Drawing.Point(440, 60);
        btnRemove.Size = new System.Drawing.Size(90, 30);
        btnRemove.Text = "削除";

        grp.Controls.Add(lst);
        grp.Controls.Add(btnAdd);
        grp.Controls.Add(btnRemove);
    }
}
