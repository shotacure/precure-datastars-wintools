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
            ((System.ComponentModel.ISupportInitialize)gridTracks).BeginInit();
            ((System.ComponentModel.ISupportInitialize)gridAlbum).BeginInit();
            SuspendLayout();
            // 
            // cboDrives
            // 
            cboDrives.DropDownStyle = ComboBoxStyle.DropDownList;
            cboDrives.Location = new Point(12, 12);
            cboDrives.Name = "cboDrives";
            cboDrives.Size = new Size(180, 28);
            cboDrives.TabIndex = 0;
            // 
            // btnLoad
            // 
            btnLoad.Location = new Point(213, 11);
            btnLoad.Name = "btnLoad";
            btnLoad.Size = new Size(120, 28);
            btnLoad.TabIndex = 1;
            btnLoad.Text = "読み込み";
            btnLoad.Click += btnLoad_Click;
            // 
            // lblSummary
            // 
            lblSummary.AutoSize = true;
            lblSummary.Location = new Point(480, 14);
            lblSummary.Name = "lblSummary";
            lblSummary.Size = new Size(0, 20);
            lblSummary.TabIndex = 2;
            // 
            // lblMcnTitle
            // 
            lblMcnTitle.AutoSize = true;
            lblMcnTitle.Location = new Point(12, 46);
            lblMcnTitle.Name = "lblMcnTitle";
            lblMcnTitle.Size = new Size(121, 20);
            lblMcnTitle.TabIndex = 3;
            lblMcnTitle.Text = "MCN (UPC/EAN):";
            // 
            // txtMcn
            // 
            txtMcn.Location = new Point(120, 42);
            txtMcn.Name = "txtMcn";
            txtMcn.ReadOnly = true;
            txtMcn.Size = new Size(260, 27);
            txtMcn.TabIndex = 4;
            // 
            // lblTracks
            // 
            lblTracks.AutoSize = true;
            lblTracks.Location = new Point(12, 74);
            lblTracks.Name = "lblTracks";
            lblTracks.Size = new Size(230, 20);
            lblTracks.TabIndex = 5;
            lblTracks.Text = "トラック情報（TOC/ISRC/CD-Text）";
            // 
            // gridTracks
            // 
            gridTracks.AllowUserToAddRows = false;
            gridTracks.AllowUserToDeleteRows = false;
            gridTracks.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gridTracks.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridTracks.ColumnHeadersHeight = 29;
            gridTracks.Location = new Point(12, 94);
            gridTracks.Name = "gridTracks";
            gridTracks.ReadOnly = true;
            gridTracks.RowHeadersVisible = false;
            gridTracks.RowHeadersWidth = 51;
            gridTracks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridTracks.Size = new Size(882, 320);
            gridTracks.TabIndex = 6;
            // 
            // lblAlbum
            // 
            lblAlbum.AutoSize = true;
            lblAlbum.Location = new Point(12, 424);
            lblAlbum.Name = "lblAlbum";
            lblAlbum.Size = new Size(186, 20);
            lblAlbum.TabIndex = 7;
            lblAlbum.Text = "アルバム CD-Text（再構成）";
            // 
            // gridAlbum
            // 
            gridAlbum.AllowUserToAddRows = false;
            gridAlbum.AllowUserToDeleteRows = false;
            gridAlbum.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            gridAlbum.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridAlbum.ColumnHeadersHeight = 29;
            gridAlbum.Location = new Point(12, 444);
            gridAlbum.Name = "gridAlbum";
            gridAlbum.ReadOnly = true;
            gridAlbum.RowHeadersVisible = false;
            gridAlbum.RowHeadersWidth = 51;
            gridAlbum.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            gridAlbum.Size = new Size(882, 253);
            gridAlbum.TabIndex = 8;
            // 
            // btnCopyTsv
            // 
            btnCopyTsv.Location = new Point(339, 12);
            btnCopyTsv.Name = "btnCopyTsv";
            btnCopyTsv.Size = new Size(123, 28);
            btnCopyTsv.TabIndex = 9;
            btnCopyTsv.Text = "TSVコピー";
            btnCopyTsv.Click += btnCopyTsv_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(906, 720);
            Controls.Add(cboDrives);
            Controls.Add(btnLoad);
            Controls.Add(lblSummary);
            Controls.Add(lblMcnTitle);
            Controls.Add(txtMcn);
            Controls.Add(lblTracks);
            Controls.Add(gridTracks);
            Controls.Add(lblAlbum);
            Controls.Add(gridAlbum);
            Controls.Add(btnCopyTsv);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PrecureDataStars.CDAnalyzer";
            Load += MainForm_Load;
            ((System.ComponentModel.ISupportInitialize)gridTracks).EndInit();
            ((System.ComponentModel.ISupportInitialize)gridAlbum).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
