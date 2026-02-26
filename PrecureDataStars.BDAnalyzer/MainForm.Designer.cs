#nullable enable
using System.Windows.Forms;

namespace PrecureDataStars.BDAnalyzer
{
    public partial class MainForm : Form
    {
        private System.ComponentModel.IContainer? components = null;

        private Panel panelTop = null!;
        private Label lblInfo = null!;
        private FlowLayoutPanel panelRight = null!;
        private Button btnCopyTsv = null!;
        // ★ 追加
        private Button btnLoadDefault = null!;

        private ListView listView = null!;
        private ColumnHeader colIdx = null!;
        private ColumnHeader colLen = null!;
        private ColumnHeader colAccum = null!;
        private ColumnHeader colSec = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            panelTop = new Panel();
            lblInfo = new Label();
            panelRight = new FlowLayoutPanel();
            btnCopyTsv = new Button();
            btnLoadDefault = new Button();
            listView = new ListView();
            colIdx = new ColumnHeader();
            colLen = new ColumnHeader();
            colAccum = new ColumnHeader();
            colSec = new ColumnHeader();
            panelTop.SuspendLayout();
            panelRight.SuspendLayout();
            SuspendLayout();
            // 
            // panelTop
            // 
            panelTop.Controls.Add(lblInfo);
            panelTop.Controls.Add(panelRight);
            panelTop.Dock = DockStyle.Top;
            panelTop.Location = new Point(0, 0);
            panelTop.Margin = new Padding(3, 4, 3, 4);
            panelTop.Name = "panelTop";
            panelTop.Padding = new Padding(9, 11, 9, 11);
            panelTop.Size = new Size(1029, 107);
            panelTop.TabIndex = 0;
            // 
            // lblInfo
            // 
            lblInfo.AutoEllipsis = true;
            lblInfo.Dock = DockStyle.Fill;
            lblInfo.Location = new Point(9, 11);
            lblInfo.Name = "lblInfo";
            lblInfo.Size = new Size(595, 85);
            lblInfo.TabIndex = 0;
            // 
            // panelRight
            // 
            panelRight.Controls.Add(btnCopyTsv);
            panelRight.Controls.Add(btnLoadDefault);
            panelRight.Dock = DockStyle.Right;
            panelRight.Location = new Point(604, 11);
            panelRight.Margin = new Padding(3, 4, 3, 4);
            panelRight.Name = "panelRight";
            panelRight.Padding = new Padding(9, 11, 9, 11);
            panelRight.Size = new Size(416, 85);
            panelRight.TabIndex = 1;
            // 
            // btnCopyTsv
            // 
            btnCopyTsv.Location = new Point(12, 18);
            btnCopyTsv.Margin = new Padding(3, 7, 3, 4);
            btnCopyTsv.Name = "btnCopyTsv";
            btnCopyTsv.Size = new Size(137, 31);
            btnCopyTsv.TabIndex = 1;
            btnCopyTsv.Text = "Copy TSV";
            btnCopyTsv.UseVisualStyleBackColor = true;
            // 
            // btnLoadDefault
            // 
            btnLoadDefault.Location = new Point(155, 18);
            btnLoadDefault.Margin = new Padding(3, 7, 3, 4);
            btnLoadDefault.Name = "btnLoadDefault";
            btnLoadDefault.Size = new Size(145, 31);
            btnLoadDefault.TabIndex = 2;
            btnLoadDefault.Text = "Load DISC";
            btnLoadDefault.UseVisualStyleBackColor = true;
            // 
            // listView
            // 
            listView.Columns.AddRange(new ColumnHeader[] { colIdx, colLen, colAccum, colSec });
            listView.Dock = DockStyle.Fill;
            listView.FullRowSelect = true;
            listView.GridLines = true;
            listView.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            listView.Location = new Point(0, 107);
            listView.Margin = new Padding(3, 4, 3, 4);
            listView.Name = "listView";
            listView.ShowGroups = false;
            listView.Size = new Size(1029, 971);
            listView.TabIndex = 1;
            listView.UseCompatibleStateImageBehavior = false;
            listView.View = View.Details;
            // 
            // colIdx
            // 
            colIdx.Text = "#";
            colIdx.TextAlign = HorizontalAlignment.Right;
            // 
            // colLen
            // 
            colLen.Text = "Length (hh:mm:ss.ff)";
            colLen.Width = 220;
            // 
            // colAccum
            // 
            colAccum.Text = "Cumulative";
            colAccum.Width = 220;
            // 
            // colSec
            // 
            colSec.Text = "Seconds";
            colSec.TextAlign = HorizontalAlignment.Right;
            colSec.Width = 140;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1029, 1078);
            Controls.Add(listView);
            Controls.Add(panelTop);
            Margin = new Padding(3, 4, 3, 4);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Precure DISC Chapter Analyzer";
            panelTop.ResumeLayout(false);
            panelRight.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
    }
}
