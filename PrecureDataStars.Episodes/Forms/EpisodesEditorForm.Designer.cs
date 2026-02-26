namespace PrecureDataStars.Episodes.Forms
{
    partial class EpisodesEditorForm
    {
        private System.ComponentModel.IContainer components = null!;

        private System.Windows.Forms.ListBox lstTvSeries = null!;
        private System.Windows.Forms.ListBox lstEpisodes = null!;

        private System.Windows.Forms.PictureBox picPreview = null!;

        private System.Windows.Forms.Label lblTitleText = null!;
        private System.Windows.Forms.TextBox txtTitleText = null!;
        private System.Windows.Forms.Label lblTitleKana = null!;
        private System.Windows.Forms.TextBox txtTitleKana = null!;

        private System.Windows.Forms.Label lblSeriesEpNo = null!;
        private System.Windows.Forms.NumericUpDown numSeriesEpNo = null!;
        private System.Windows.Forms.Label lblOnAirAt = null!;
        private System.Windows.Forms.DateTimePicker dtOnAirAt = null!;

        private System.Windows.Forms.Label lblTotalEpNo = null!;
        private System.Windows.Forms.NumericUpDown numTotalEpNo = null!;
        private System.Windows.Forms.Label lblTotalOaNo = null!;
        private System.Windows.Forms.NumericUpDown numTotalOaNo = null!;
        private System.Windows.Forms.Label lblNitiasaOaNo = null!;
        private System.Windows.Forms.NumericUpDown numNitiasaOaNo = null!;

        private System.Windows.Forms.Label lblToeiSummary = null!;
        private System.Windows.Forms.TextBox txtToeiSummary = null!;
        private System.Windows.Forms.Label lblToeiLineup = null!;
        private System.Windows.Forms.TextBox txtToeiLineup = null!;
        private System.Windows.Forms.Label lblYoutube = null!;
        private System.Windows.Forms.TextBox txtYoutube = null!;

        private System.Windows.Forms.Label lblRichHtml = null!;
        private System.Windows.Forms.TextBox txtTitleRichHtml = null!;
        private System.Windows.Forms.Button btnRuby = null!;
        private System.Windows.Forms.Button btnBr = null!;
        private System.Windows.Forms.WebBrowser webHtmlPreview = null!;

        private System.Windows.Forms.Label lblNotes = null!;
        private System.Windows.Forms.TextBox txtNotes = null!;

        private System.Windows.Forms.TextBox txtTitleInformation;

        private System.Windows.Forms.DataGridView dgvParts = null!;
        private System.Windows.Forms.Button btnPartAdd = null!;
        private System.Windows.Forms.Button btnPartDelete = null!;
        private System.Windows.Forms.Label lblPartTotals = null!;

        private System.Windows.Forms.Button btnPartCopyPrev = null!;
        private System.Windows.Forms.Button btnAdd = null!;
        private System.Windows.Forms.Button btnSave = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            lstTvSeries = new ListBox();
            lstEpisodes = new ListBox();
            picPreview = new PictureBox();
            lblTitleText = new Label();
            txtTitleText = new TextBox();
            lblTitleKana = new Label();
            txtTitleKana = new TextBox();
            lblSeriesEpNo = new Label();
            numSeriesEpNo = new NumericUpDown();
            lblOnAirAt = new Label();
            dtOnAirAt = new DateTimePicker();
            lblTotalEpNo = new Label();
            numTotalEpNo = new NumericUpDown();
            lblTotalOaNo = new Label();
            numTotalOaNo = new NumericUpDown();
            lblNitiasaOaNo = new Label();
            numNitiasaOaNo = new NumericUpDown();
            lblToeiSummary = new Label();
            txtToeiSummary = new TextBox();
            lblToeiLineup = new Label();
            txtToeiLineup = new TextBox();
            lblYoutube = new Label();
            txtYoutube = new TextBox();
            lblRichHtml = new Label();
            txtTitleRichHtml = new TextBox();
            btnRuby = new Button();
            btnBr = new Button();
            webHtmlPreview = new WebBrowser();
            lblNotes = new Label();
            txtNotes = new TextBox();
            dgvParts = new DataGridView();
            btnPartCopyPrev = new Button();
            btnPartAdd = new Button();
            btnPartDelete = new Button();
            lblPartTotals = new Label();
            btnAdd = new Button();
            btnSave = new Button();
            txtTitleInformation = new TextBox();
            lblTitleInformation = new Label();
            txtPartLengthStats = new TextBox();
            lblPartLengthStats = new Label();
            btnJunctionCopy = new Button();
            btnNextTitleCopy = new Button();
            ((System.ComponentModel.ISupportInitialize)picPreview).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numSeriesEpNo).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numTotalEpNo).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numTotalOaNo).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numNitiasaOaNo).BeginInit();
            ((System.ComponentModel.ISupportInitialize)dgvParts).BeginInit();
            SuspendLayout();
            // 
            // lstTvSeries
            // 
            lstTvSeries.BorderStyle = BorderStyle.FixedSingle;
            lstTvSeries.Location = new Point(12, 12);
            lstTvSeries.Name = "lstTvSeries";
            lstTvSeries.Size = new Size(280, 1222);
            lstTvSeries.TabIndex = 0;
            // 
            // lstEpisodes
            // 
            lstEpisodes.BorderStyle = BorderStyle.FixedSingle;
            lstEpisodes.Location = new Point(300, 12);
            lstEpisodes.Name = "lstEpisodes";
            lstEpisodes.Size = new Size(380, 1222);
            lstEpisodes.TabIndex = 1;
            // 
            // picPreview
            // 
            picPreview.BorderStyle = BorderStyle.FixedSingle;
            picPreview.Location = new Point(700, 12);
            picPreview.Name = "picPreview";
            picPreview.Size = new Size(640, 360);
            picPreview.SizeMode = PictureBoxSizeMode.Zoom;
            picPreview.TabIndex = 2;
            picPreview.TabStop = false;
            // 
            // lblTitleText
            // 
            lblTitleText.AutoSize = true;
            lblTitleText.Location = new Point(700, 576);
            lblTitleText.Name = "lblTitleText";
            lblTitleText.Size = new Size(75, 20);
            lblTitleText.TabIndex = 3;
            lblTitleText.Text = "サブタイトル";
            // 
            // txtTitleText
            // 
            txtTitleText.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleText.Location = new Point(880, 570);
            txtTitleText.Name = "txtTitleText";
            txtTitleText.Size = new Size(460, 27);
            txtTitleText.TabIndex = 4;
            // 
            // lblTitleKana
            // 
            lblTitleKana.AutoSize = true;
            lblTitleKana.Location = new Point(700, 606);
            lblTitleKana.Name = "lblTitleKana";
            lblTitleKana.Size = new Size(100, 20);
            lblTitleKana.TabIndex = 5;
            lblTitleKana.Text = "サブタイトルかな";
            // 
            // txtTitleKana
            // 
            txtTitleKana.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleKana.Location = new Point(880, 600);
            txtTitleKana.Name = "txtTitleKana";
            txtTitleKana.Size = new Size(460, 27);
            txtTitleKana.TabIndex = 6;
            // 
            // lblSeriesEpNo
            // 
            lblSeriesEpNo.AutoSize = true;
            lblSeriesEpNo.Location = new Point(700, 397);
            lblSeriesEpNo.Name = "lblSeriesEpNo";
            lblSeriesEpNo.Size = new Size(39, 20);
            lblSeriesEpNo.TabIndex = 7;
            lblSeriesEpNo.Text = "話数";
            // 
            // numSeriesEpNo
            // 
            numSeriesEpNo.Location = new Point(880, 391);
            numSeriesEpNo.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
            numSeriesEpNo.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numSeriesEpNo.Name = "numSeriesEpNo";
            numSeriesEpNo.Size = new Size(120, 27);
            numSeriesEpNo.TabIndex = 8;
            numSeriesEpNo.Value = new decimal(new int[] { 1, 0, 0, 0 });
            // 
            // lblOnAirAt
            // 
            lblOnAirAt.AutoSize = true;
            lblOnAirAt.Location = new Point(700, 427);
            lblOnAirAt.Name = "lblOnAirAt";
            lblOnAirAt.Size = new Size(69, 20);
            lblOnAirAt.TabIndex = 9;
            lblOnAirAt.Text = "放送日時";
            // 
            // dtOnAirAt
            // 
            dtOnAirAt.CustomFormat = "yyyy-MM-dd HH:mm:ss";
            dtOnAirAt.Format = DateTimePickerFormat.Custom;
            dtOnAirAt.Location = new Point(880, 421);
            dtOnAirAt.Name = "dtOnAirAt";
            dtOnAirAt.Size = new Size(200, 27);
            dtOnAirAt.TabIndex = 10;
            // 
            // lblTotalEpNo
            // 
            lblTotalEpNo.AutoSize = true;
            lblTotalEpNo.Location = new Point(700, 457);
            lblTotalEpNo.Name = "lblTotalEpNo";
            lblTotalEpNo.Size = new Size(123, 20);
            lblTotalEpNo.TabIndex = 11;
            lblTotalEpNo.Text = "プリキュア通算話数";
            // 
            // numTotalEpNo
            // 
            numTotalEpNo.Location = new Point(880, 451);
            numTotalEpNo.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            numTotalEpNo.Name = "numTotalEpNo";
            numTotalEpNo.Size = new Size(120, 27);
            numTotalEpNo.TabIndex = 12;
            // 
            // lblTotalOaNo
            // 
            lblTotalOaNo.AutoSize = true;
            lblTotalOaNo.Location = new Point(700, 487);
            lblTotalOaNo.Name = "lblTotalOaNo";
            lblTotalOaNo.Size = new Size(153, 20);
            lblTotalOaNo.TabIndex = 13;
            lblTotalOaNo.Text = "プリキュア通算放送回数";
            // 
            // numTotalOaNo
            // 
            numTotalOaNo.Location = new Point(880, 481);
            numTotalOaNo.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            numTotalOaNo.Name = "numTotalOaNo";
            numTotalOaNo.Size = new Size(120, 27);
            numTotalOaNo.TabIndex = 14;
            // 
            // lblNitiasaOaNo
            // 
            lblNitiasaOaNo.AutoSize = true;
            lblNitiasaOaNo.Location = new Point(700, 517);
            lblNitiasaOaNo.Name = "lblNitiasaOaNo";
            lblNitiasaOaNo.Size = new Size(145, 20);
            lblNitiasaOaNo.TabIndex = 15;
            lblNitiasaOaNo.Text = "ニチアサ通算放送回数";
            // 
            // numNitiasaOaNo
            // 
            numNitiasaOaNo.Location = new Point(880, 511);
            numNitiasaOaNo.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            numNitiasaOaNo.Name = "numNitiasaOaNo";
            numNitiasaOaNo.Size = new Size(120, 27);
            numNitiasaOaNo.TabIndex = 16;
            // 
            // lblToeiSummary
            // 
            lblToeiSummary.AutoSize = true;
            lblToeiSummary.Location = new Point(700, 1039);
            lblToeiSummary.Name = "lblToeiSummary";
            lblToeiSummary.Size = new Size(111, 20);
            lblToeiSummary.TabIndex = 17;
            lblToeiSummary.Text = "東映あらすじURL";
            // 
            // txtToeiSummary
            // 
            txtToeiSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtToeiSummary.Location = new Point(880, 1033);
            txtToeiSummary.Name = "txtToeiSummary";
            txtToeiSummary.Size = new Size(460, 27);
            txtToeiSummary.TabIndex = 18;
            // 
            // lblToeiLineup
            // 
            lblToeiLineup.AutoSize = true;
            lblToeiLineup.Location = new Point(700, 1069);
            lblToeiLineup.Name = "lblToeiLineup";
            lblToeiLineup.Size = new Size(129, 20);
            lblToeiLineup.TabIndex = 19;
            lblToeiLineup.Text = "東映ラインナップURL";
            // 
            // txtToeiLineup
            // 
            txtToeiLineup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtToeiLineup.Location = new Point(880, 1063);
            txtToeiLineup.Name = "txtToeiLineup";
            txtToeiLineup.Size = new Size(460, 27);
            txtToeiLineup.TabIndex = 20;
            // 
            // lblYoutube
            // 
            lblYoutube.AutoSize = true;
            lblYoutube.Location = new Point(700, 1099);
            lblYoutube.Name = "lblYoutube";
            lblYoutube.Size = new Size(120, 20);
            lblYoutube.TabIndex = 21;
            lblYoutube.Text = "YouTube予告URL";
            // 
            // txtYoutube
            // 
            txtYoutube.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtYoutube.Location = new Point(880, 1093);
            txtYoutube.Name = "txtYoutube";
            txtYoutube.Size = new Size(460, 27);
            txtYoutube.TabIndex = 22;
            // 
            // lblRichHtml
            // 
            lblRichHtml.AutoSize = true;
            lblRichHtml.Location = new Point(700, 644);
            lblRichHtml.Name = "lblRichHtml";
            lblRichHtml.Size = new Size(174, 20);
            lblRichHtml.TabIndex = 23;
            lblRichHtml.Text = "サブタイトル (ルビつきHTML)";
            // 
            // txtTitleRichHtml
            // 
            txtTitleRichHtml.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleRichHtml.Location = new Point(880, 670);
            txtTitleRichHtml.Multiline = true;
            txtTitleRichHtml.Name = "txtTitleRichHtml";
            txtTitleRichHtml.Size = new Size(460, 120);
            txtTitleRichHtml.TabIndex = 25;
            // 
            // btnRuby
            // 
            btnRuby.Location = new Point(880, 640);
            btnRuby.Name = "btnRuby";
            btnRuby.Size = new Size(100, 27);
            btnRuby.TabIndex = 24;
            btnRuby.Text = "選択ルビ";
            // 
            // btnBr
            // 
            btnBr.Location = new Point(986, 640);
            btnBr.Name = "btnBr";
            btnBr.Size = new Size(60, 27);
            btnBr.TabIndex = 25;
            btnBr.Text = "<br>";
            // 
            // webHtmlPreview
            // 
            webHtmlPreview.AllowWebBrowserDrop = false;
            webHtmlPreview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            webHtmlPreview.Location = new Point(880, 796);
            webHtmlPreview.MinimumSize = new Size(20, 20);
            webHtmlPreview.Name = "webHtmlPreview";
            webHtmlPreview.ScriptErrorsSuppressed = true;
            webHtmlPreview.Size = new Size(460, 201);
            webHtmlPreview.TabIndex = 26;
            // 
            // lblNotes
            // 
            lblNotes.AutoSize = true;
            lblNotes.Location = new Point(700, 1164);
            lblNotes.Name = "lblNotes";
            lblNotes.Size = new Size(89, 20);
            lblNotes.TabIndex = 31;
            lblNotes.Text = "備考 (notes)";
            // 
            // txtNotes
            // 
            txtNotes.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtNotes.Location = new Point(880, 1154);
            txtNotes.Multiline = true;
            txtNotes.Name = "txtNotes";
            txtNotes.ScrollBars = ScrollBars.Vertical;
            txtNotes.Size = new Size(460, 80);
            txtNotes.TabIndex = 34;
            // 
            // dgvParts
            // 
            dgvParts.AllowUserToAddRows = false;
            dgvParts.AllowUserToDeleteRows = false;
            dgvParts.AllowUserToResizeRows = false;
            dgvParts.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dgvParts.EnableHeadersVisualStyles = false;
            dgvParts.Location = new Point(1367, 391);
            dgvParts.MultiSelect = false;
            dgvParts.Name = "dgvParts";
            dgvParts.RowHeadersVisible = false;
            dgvParts.RowHeadersWidth = 51;
            dgvParts.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvParts.Size = new Size(815, 520);
            dgvParts.TabIndex = 100;
            // 
            // btnPartCopyPrev
            // 
            btnPartCopyPrev.Location = new Point(1888, 923);
            btnPartCopyPrev.Name = "btnPartCopyPrev";
            btnPartCopyPrev.Size = new Size(100, 27);
            btnPartCopyPrev.TabIndex = 101;
            btnPartCopyPrev.Text = "前話コピー";
            btnPartCopyPrev.UseVisualStyleBackColor = true;
            // 
            // btnPartAdd
            // 
            btnPartAdd.Location = new Point(1996, 923);
            btnPartAdd.Name = "btnPartAdd";
            btnPartAdd.Size = new Size(90, 27);
            btnPartAdd.TabIndex = 102;
            btnPartAdd.Text = "行を追加";
            // 
            // btnPartDelete
            // 
            btnPartDelete.Location = new Point(2092, 923);
            btnPartDelete.Name = "btnPartDelete";
            btnPartDelete.Size = new Size(90, 27);
            btnPartDelete.TabIndex = 103;
            btnPartDelete.Text = "選択削除";
            // 
            // lblPartTotals
            // 
            lblPartTotals.AutoSize = true;
            lblPartTotals.Location = new Point(1367, 926);
            lblPartTotals.Name = "lblPartTotals";
            lblPartTotals.Size = new Size(266, 20);
            lblPartTotals.TabIndex = 104;
            lblPartTotals.Text = "合計: OA=0:00 / 円盤=0:00 / 配信=0:00";
            // 
            // btnAdd
            // 
            btnAdd.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnAdd.Location = new Point(1798, 1179);
            btnAdd.Name = "btnAdd";
            btnAdd.Size = new Size(205, 58);
            btnAdd.TabIndex = 35;
            btnAdd.Text = "新規追加";
            // 
            // btnSave
            // 
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Location = new Point(2009, 1179);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(182, 58);
            btnSave.TabIndex = 36;
            btnSave.Text = "保存";
            // 
            // txtTitleInformation
            // 
            txtTitleInformation.Location = new Point(1367, 43);
            txtTitleInformation.Multiline = true;
            txtTitleInformation.Name = "txtTitleInformation";
            txtTitleInformation.ReadOnly = true;
            txtTitleInformation.Size = new Size(815, 329);
            txtTitleInformation.TabIndex = 37;
            // 
            // lblTitleInformation
            // 
            lblTitleInformation.AutoSize = true;
            lblTitleInformation.Location = new Point(1367, 12);
            lblTitleInformation.Name = "lblTitleInformation";
            lblTitleInformation.Size = new Size(135, 20);
            lblTitleInformation.TabIndex = 3;
            lblTitleInformation.Text = "サブタイトル文字情報";
            // 
            // txtPartLengthStats
            // 
            txtPartLengthStats.Location = new Point(1367, 997);
            txtPartLengthStats.Multiline = true;
            txtPartLengthStats.Name = "txtPartLengthStats";
            txtPartLengthStats.ReadOnly = true;
            txtPartLengthStats.Size = new Size(815, 162);
            txtPartLengthStats.TabIndex = 106;
            // 
            // lblPartLengthStats
            // 
            lblPartLengthStats.AutoSize = true;
            lblPartLengthStats.Location = new Point(1367, 966);
            lblPartLengthStats.Name = "lblPartLengthStats";
            lblPartLengthStats.Size = new Size(131, 20);
            lblPartLengthStats.TabIndex = 105;
            lblPartLengthStats.Text = "パート尺長統計情報";
            // 
            // btnJunctionCopy
            // 
            btnJunctionCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnJunctionCopy.Location = new Point(1128, 498);
            btnJunctionCopy.Name = "btnJunctionCopy";
            btnJunctionCopy.Size = new Size(103, 58);
            btnJunctionCopy.TabIndex = 36;
            btnJunctionCopy.Text = "このあと8:30";
            btnJunctionCopy.Click += btnJunctionCopy_Click;
            // 
            // btnNextTitleCopy
            // 
            btnNextTitleCopy.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnNextTitleCopy.Location = new Point(1237, 498);
            btnNextTitleCopy.Name = "btnNextTitleCopy";
            btnNextTitleCopy.Size = new Size(103, 58);
            btnNextTitleCopy.TabIndex = 36;
            btnNextTitleCopy.Text = "次回";
            btnNextTitleCopy.Click += btnNextTitleCopy_Click;
            // 
            // EpisodesEditorForm
            // 
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(2203, 1249);
            Controls.Add(txtPartLengthStats);
            Controls.Add(lblPartLengthStats);
            Controls.Add(txtTitleInformation);
            Controls.Add(lstTvSeries);
            Controls.Add(lstEpisodes);
            Controls.Add(picPreview);
            Controls.Add(lblTitleInformation);
            Controls.Add(lblTitleText);
            Controls.Add(txtTitleText);
            Controls.Add(lblTitleKana);
            Controls.Add(txtTitleKana);
            Controls.Add(lblSeriesEpNo);
            Controls.Add(numSeriesEpNo);
            Controls.Add(lblOnAirAt);
            Controls.Add(dtOnAirAt);
            Controls.Add(lblTotalEpNo);
            Controls.Add(numTotalEpNo);
            Controls.Add(lblTotalOaNo);
            Controls.Add(numTotalOaNo);
            Controls.Add(lblNitiasaOaNo);
            Controls.Add(numNitiasaOaNo);
            Controls.Add(lblToeiSummary);
            Controls.Add(txtToeiSummary);
            Controls.Add(lblToeiLineup);
            Controls.Add(txtToeiLineup);
            Controls.Add(lblYoutube);
            Controls.Add(txtYoutube);
            Controls.Add(lblRichHtml);
            Controls.Add(btnRuby);
            Controls.Add(btnBr);
            Controls.Add(txtTitleRichHtml);
            Controls.Add(webHtmlPreview);
            Controls.Add(lblNotes);
            Controls.Add(txtNotes);
            Controls.Add(dgvParts);
            Controls.Add(btnPartCopyPrev);
            Controls.Add(btnPartAdd);
            Controls.Add(btnPartDelete);
            Controls.Add(lblPartTotals);
            Controls.Add(btnAdd);
            Controls.Add(btnNextTitleCopy);
            Controls.Add(btnJunctionCopy);
            Controls.Add(btnSave);
            MinimumSize = new Size(1200, 820);
            Name = "EpisodesEditorForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "エピソード編集 (TVシリーズのみ)";
            ((System.ComponentModel.ISupportInitialize)picPreview).EndInit();
            ((System.ComponentModel.ISupportInitialize)numSeriesEpNo).EndInit();
            ((System.ComponentModel.ISupportInitialize)numTotalEpNo).EndInit();
            ((System.ComponentModel.ISupportInitialize)numTotalOaNo).EndInit();
            ((System.ComponentModel.ISupportInitialize)numNitiasaOaNo).EndInit();
            ((System.ComponentModel.ISupportInitialize)dgvParts).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
        private Label lblTitleInformation;
        private TextBox txtPartLengthStats;
        private Label lblPartLengthStats;
        private Button btnJunctionCopy;
        private Button btnNextTitleCopy;
    }
}
