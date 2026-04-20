namespace PrecureDataStars.Catalog.Common.Dialogs;

partial class DiscMatchDialog
{
    private System.ComponentModel.IContainer components = null!;

    // 自動照合結果
    private Label lblMatchInfo = null!;
    private DataGridView gridCandidates = null!;
    // 手動検索
    private Label lblSearchTitle = null!;
    private TextBox txtKeyword = null!;
    private Button btnSearch = null!;
    private Label lblSearchInfo = null!;
    private DataGridView gridSearch = null!;
    // アクション
    private Button btnUseSelected = null!;
    private Button btnNewRegistration = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblMatchInfo = new Label();
        gridCandidates = new DataGridView();
        lblSearchTitle = new Label();
        txtKeyword = new TextBox();
        btnSearch = new Button();
        lblSearchInfo = new Label();
        gridSearch = new DataGridView();
        btnUseSelected = new Button();
        btnNewRegistration = new Button();
        btnCancel = new Button();

        SuspendLayout();

        // lblMatchInfo
        lblMatchInfo.Location = new Point(12, 9);
        lblMatchInfo.Size = new Size(760, 24);
        lblMatchInfo.Text = "";

        // gridCandidates (上段: 自動照合候補)
        gridCandidates.Location = new Point(12, 38);
        gridCandidates.Size = new Size(760, 150);
        gridCandidates.AllowUserToAddRows = false;
        gridCandidates.AllowUserToDeleteRows = false;
        gridCandidates.RowHeadersVisible = false;

        // lblSearchTitle
        lblSearchTitle.Location = new Point(12, 200);
        lblSearchTitle.Size = new Size(200, 20);
        lblSearchTitle.Text = "手動検索 (品番 / タイトル):";

        // txtKeyword
        txtKeyword.Location = new Point(12, 222);
        txtKeyword.Size = new Size(580, 23);

        // btnSearch
        btnSearch.Location = new Point(600, 221);
        btnSearch.Size = new Size(80, 25);
        btnSearch.Text = "検索";

        // lblSearchInfo
        lblSearchInfo.Location = new Point(690, 226);
        lblSearchInfo.Size = new Size(80, 20);
        lblSearchInfo.Text = "";

        // gridSearch
        gridSearch.Location = new Point(12, 252);
        gridSearch.Size = new Size(760, 180);
        gridSearch.AllowUserToAddRows = false;
        gridSearch.AllowUserToDeleteRows = false;
        gridSearch.RowHeadersVisible = false;

        // ボタン群
        btnUseSelected.Location = new Point(12, 445);
        btnUseSelected.Size = new Size(200, 32);
        btnUseSelected.Text = "選択したディスクに反映";

        btnNewRegistration.Location = new Point(220, 445);
        btnNewRegistration.Size = new Size(200, 32);
        btnNewRegistration.Text = "新規ディスクとして登録";

        btnCancel.Location = new Point(680, 445);
        btnCancel.Size = new Size(92, 32);
        btnCancel.Text = "キャンセル";

        // Form
        ClientSize = new Size(784, 489);
        Controls.AddRange(new Control[]
        {
            lblMatchInfo, gridCandidates, lblSearchTitle, txtKeyword, btnSearch,
            lblSearchInfo, gridSearch, btnUseSelected, btnNewRegistration, btnCancel
        });
        Text = "ディスク照合";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        ResumeLayout(false);
        PerformLayout();
    }
}
