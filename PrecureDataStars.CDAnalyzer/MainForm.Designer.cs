using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.CDAnalyzer
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        private ComboBox cboDrives;
        private Button btnLoad;
        private Label lblSummary;
        private Label lblMcnTitle;
        private TextBox txtMcn;
        private Label lblTracks;
        private DataGridView gridTracks;
        private Label lblAlbum;
        private DataGridView gridAlbum;
        private Button btnCopyTsv;

        // DB 連携 UI（v1.1.0 追加）
        private Panel pnlDb;
        private Label lblDbTitle;
        private Button btnDbMatch;
        private Label lblDbStatus;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            cboDrives = new ComboBox();
            btnLoad = new Button();
            lblSummary = new Label();
            lblMcnTitle = new Label();
            txtMcn = new TextBox();
            lblTracks = new Label();
            gridTracks = new DataGridView();
            lblAlbum = new Label();
            gridAlbum = new DataGridView();
            btnCopyTsv = new Button();
            pnlDb = new Panel();
            lblDbTitle = new Label();
            btnDbMatch = new Button();
            lblDbStatus = new Label();

            SuspendLayout();

            // cboDrives
            cboDrives.DropDownStyle = ComboBoxStyle.DropDownList;
            cboDrives.Location = new Point(12, 12);
            cboDrives.Size = new Size(280, 23);

            // btnLoad
            btnLoad.Location = new Point(298, 12);
            btnLoad.Size = new Size(90, 23);
            btnLoad.Text = "読み取り";
            btnLoad.Click += btnLoad_Click;

            // lblSummary
            lblSummary.Location = new Point(394, 16);
            lblSummary.Size = new Size(540, 20);
            lblSummary.Text = "";

            // lblMcnTitle
            lblMcnTitle.Location = new Point(12, 45);
            lblMcnTitle.Size = new Size(60, 20);
            lblMcnTitle.Text = "MCN:";

            // txtMcn
            txtMcn.Location = new Point(75, 42);
            txtMcn.Size = new Size(200, 23);
            txtMcn.ReadOnly = true;

            // btnCopyTsv
            btnCopyTsv.Location = new Point(290, 42);
            btnCopyTsv.Size = new Size(140, 23);
            btnCopyTsv.Text = "TSV コピー";
            btnCopyTsv.Enabled = false;
            btnCopyTsv.Click += btnCopyTsv_Click;

            // lblTracks
            lblTracks.Location = new Point(12, 75);
            lblTracks.Size = new Size(200, 20);
            lblTracks.Text = "トラック一覧:";
            lblTracks.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            // gridTracks
            gridTracks.Location = new Point(12, 97);
            gridTracks.Size = new Size(920, 280);
            gridTracks.AllowUserToAddRows = false;
            gridTracks.AllowUserToDeleteRows = false;
            gridTracks.ReadOnly = true;
            gridTracks.RowHeadersVisible = false;

            // lblAlbum
            lblAlbum.Location = new Point(12, 385);
            lblAlbum.Size = new Size(200, 20);
            lblAlbum.Text = "アルバム情報 (CD-Text):";
            lblAlbum.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            // gridAlbum
            gridAlbum.Location = new Point(12, 407);
            gridAlbum.Size = new Size(540, 140);
            gridAlbum.AllowUserToAddRows = false;
            gridAlbum.AllowUserToDeleteRows = false;
            gridAlbum.ReadOnly = true;
            gridAlbum.RowHeadersVisible = false;

            // DB 連携パネル
            pnlDb.Location = new Point(560, 385);
            pnlDb.Size = new Size(372, 162);
            pnlDb.BorderStyle = BorderStyle.FixedSingle;

            lblDbTitle.Location = new Point(8, 6);
            lblDbTitle.Size = new Size(200, 20);
            lblDbTitle.Text = "DB 連携";
            lblDbTitle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

            btnDbMatch.Location = new Point(8, 32);
            btnDbMatch.Size = new Size(356, 38);
            btnDbMatch.Text = "既存ディスクと照合 / 新規登録...";
            btnDbMatch.Click += btnDbMatch_Click;

            lblDbStatus.Location = new Point(8, 80);
            lblDbStatus.Size = new Size(356, 70);
            lblDbStatus.Text = "";

            pnlDb.Controls.Add(lblDbTitle);
            pnlDb.Controls.Add(btnDbMatch);
            pnlDb.Controls.Add(lblDbStatus);

            // Form
            ClientSize = new Size(944, 559);
            Controls.AddRange(new Control[]
            {
                cboDrives, btnLoad, lblSummary,
                lblMcnTitle, txtMcn, btnCopyTsv,
                lblTracks, gridTracks,
                lblAlbum, gridAlbum, pnlDb
            });
            Text = "CDAnalyzer (PrecureDataStars)";
            StartPosition = FormStartPosition.CenterScreen;
            Load += MainForm_Load;

            ResumeLayout(false);
            PerformLayout();
        }
    }
}
