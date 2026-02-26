namespace PrecureDataStars.Episodes.Forms
{
    partial class SeriesEditorForm
    {
        private System.ComponentModel.IContainer components = null!;

        // 左リスト
        private System.Windows.Forms.ListBox lstSeries = null!;
        // ボタン
        private System.Windows.Forms.Button btnAdd = null!;
        private System.Windows.Forms.Button btnSave = null!;

        // ラベル
        private System.Windows.Forms.Label lblTitle = null!;
        private System.Windows.Forms.Label lblSlug = null!;
        private System.Windows.Forms.Label lblKind = null!;
        private System.Windows.Forms.Label lblParent = null!;
        private System.Windows.Forms.Label lblRelation = null!;
        private System.Windows.Forms.Label lblSeq = null!;
        private System.Windows.Forms.Label lblStart = null!;
        private System.Windows.Forms.Label lblEnd = null!;
        private System.Windows.Forms.Label lblEpisodes = null!;
        private System.Windows.Forms.Label lblRunTime = null!;
        private System.Windows.Forms.Label lblTitleKana = null!;
        private System.Windows.Forms.Label lblTitleShort = null!;
        private System.Windows.Forms.Label lblTitleShortKana = null!;
        private System.Windows.Forms.Label lblTitleEn = null!;
        private System.Windows.Forms.Label lblTitleShortEn = null!;
        private System.Windows.Forms.Label lblToeiSite = null!;
        private System.Windows.Forms.Label lblToeiLineup = null!;
        private System.Windows.Forms.Label lblAbcSite = null!;
        private System.Windows.Forms.Label lblAmazonPrime = null!;

        // エディタ
        private System.Windows.Forms.TextBox txtTitle = null!;
        private System.Windows.Forms.TextBox txtSlug = null!;
        private System.Windows.Forms.ComboBox cmbKind = null!;
        private System.Windows.Forms.ComboBox cmbParent = null!;
        private System.Windows.Forms.ComboBox cmbRelation = null!;
        private System.Windows.Forms.NumericUpDown numSeqInParent = null!;
        private System.Windows.Forms.DateTimePicker dtStart = null!;
        private System.Windows.Forms.DateTimePicker dtEnd = null!;
        private System.Windows.Forms.NumericUpDown numEpisodes = null!;
        private System.Windows.Forms.NumericUpDown numRunTimeSeconds = null!;
        private System.Windows.Forms.TextBox txtTitleKana = null!;
        private System.Windows.Forms.TextBox txtTitleShort = null!;
        private System.Windows.Forms.TextBox txtTitleShortKana = null!;
        private System.Windows.Forms.TextBox txtTitleEn = null!;
        private System.Windows.Forms.TextBox txtTitleShortEn = null!;
        private System.Windows.Forms.TextBox txtToeiSite = null!;
        private System.Windows.Forms.TextBox txtToeiLineup = null!;
        private System.Windows.Forms.TextBox txtAbcSite = null!;
        private System.Windows.Forms.TextBox txtAmazonPrime = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            lstSeries = new ListBox();
            lblTitle = new Label();
            txtTitle = new TextBox();
            lblSlug = new Label();
            txtSlug = new TextBox();
            lblKind = new Label();
            cmbKind = new ComboBox();
            lblParent = new Label();
            cmbParent = new ComboBox();
            lblRelation = new Label();
            cmbRelation = new ComboBox();
            lblSeq = new Label();
            numSeqInParent = new NumericUpDown();
            lblStart = new Label();
            dtStart = new DateTimePicker();
            lblEnd = new Label();
            dtEnd = new DateTimePicker();
            lblEpisodes = new Label();
            numEpisodes = new NumericUpDown();
            lblRunTime = new Label();
            numRunTimeSeconds = new NumericUpDown();
            lblTitleKana = new Label();
            txtTitleKana = new TextBox();
            lblTitleShort = new Label();
            txtTitleShort = new TextBox();
            lblTitleShortKana = new Label();
            txtTitleShortKana = new TextBox();
            lblTitleEn = new Label();
            txtTitleEn = new TextBox();
            lblTitleShortEn = new Label();
            txtTitleShortEn = new TextBox();
            lblToeiSite = new Label();
            txtToeiSite = new TextBox();
            lblToeiLineup = new Label();
            txtToeiLineup = new TextBox();
            lblAbcSite = new Label();
            txtAbcSite = new TextBox();
            lblAmazonPrime = new Label();
            txtAmazonPrime = new TextBox();
            btnAdd = new Button();
            btnSave = new Button();
            ((System.ComponentModel.ISupportInitialize)numSeqInParent).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numEpisodes).BeginInit();
            ((System.ComponentModel.ISupportInitialize)numRunTimeSeconds).BeginInit();
            SuspendLayout();
            // 
            // lstSeries
            // 
            lstSeries.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            lstSeries.BorderStyle = BorderStyle.FixedSingle;
            lstSeries.Location = new Point(12, 12);
            lstSeries.Name = "lstSeries";
            lstSeries.Size = new Size(640, 822);
            lstSeries.TabIndex = 0;
            // 
            // lblTitle
            // 
            lblTitle.AutoSize = true;
            lblTitle.Location = new Point(664, 12);
            lblTitle.Name = "lblTitle";
            lblTitle.Size = new Size(53, 20);
            lblTitle.TabIndex = 1;
            lblTitle.Text = "タイトル";
            // 
            // txtTitle
            // 
            txtTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitle.Location = new Point(900, 12);
            txtTitle.Name = "txtTitle";
            txtTitle.Size = new Size(588, 27);
            txtTitle.TabIndex = 2;
            // 
            // lblSlug
            // 
            lblSlug.AutoSize = true;
            lblSlug.Location = new Point(664, 48);
            lblSlug.Name = "lblSlug";
            lblSlug.Size = new Size(52, 20);
            lblSlug.TabIndex = 3;
            lblSlug.Text = "スラッグ";
            // 
            // txtSlug
            // 
            txtSlug.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtSlug.Location = new Point(900, 48);
            txtSlug.Name = "txtSlug";
            txtSlug.Size = new Size(120, 27);
            txtSlug.TabIndex = 4;
            // 
            // lblKind
            // 
            lblKind.AutoSize = true;
            lblKind.Location = new Point(664, 84);
            lblKind.Name = "lblKind";
            lblKind.Size = new Size(120, 20);
            lblKind.TabIndex = 5;
            lblKind.Text = "種別 (kind_code)";
            // 
            // cmbKind
            // 
            cmbKind.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbKind.Location = new Point(900, 84);
            cmbKind.Name = "cmbKind";
            cmbKind.Size = new Size(120, 28);
            cmbKind.TabIndex = 6;
            // 
            // lblParent
            // 
            lblParent.AutoSize = true;
            lblParent.Location = new Point(664, 120);
            lblParent.Name = "lblParent";
            lblParent.Size = new Size(69, 20);
            lblParent.TabIndex = 7;
            lblParent.Text = "親シリーズ";
            // 
            // cmbParent
            // 
            cmbParent.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbParent.Location = new Point(900, 120);
            cmbParent.Name = "cmbParent";
            cmbParent.Size = new Size(520, 28);
            cmbParent.TabIndex = 8;
            // 
            // lblRelation
            // 
            lblRelation.AutoSize = true;
            lblRelation.Location = new Point(664, 156);
            lblRelation.Name = "lblRelation";
            lblRelation.Size = new Size(69, 20);
            lblRelation.TabIndex = 9;
            lblRelation.Text = "親子関係";
            // 
            // cmbRelation
            // 
            cmbRelation.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRelation.Location = new Point(900, 156);
            cmbRelation.Name = "cmbRelation";
            cmbRelation.Size = new Size(120, 28);
            cmbRelation.TabIndex = 10;
            // 
            // lblSeq
            // 
            lblSeq.AutoSize = true;
            lblSeq.Location = new Point(664, 192);
            lblSeq.Name = "lblSeq";
            lblSeq.Size = new Size(113, 20);
            lblSeq.TabIndex = 11;
            lblSeq.Text = "子タイトル上映順";
            // 
            // numSeqInParent
            // 
            numSeqInParent.Location = new Point(900, 192);
            numSeqInParent.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
            numSeqInParent.Name = "numSeqInParent";
            numSeqInParent.Size = new Size(120, 27);
            numSeqInParent.TabIndex = 12;
            // 
            // lblStart
            // 
            lblStart.AutoSize = true;
            lblStart.Location = new Point(664, 228);
            lblStart.Name = "lblStart";
            lblStart.Size = new Size(105, 20);
            lblStart.TabIndex = 13;
            lblStart.Text = "開始日/公開日";
            // 
            // dtStart
            // 
            dtStart.CustomFormat = "yyyy-MM-dd";
            dtStart.Format = DateTimePickerFormat.Custom;
            dtStart.Location = new Point(900, 228);
            dtStart.Name = "dtStart";
            dtStart.Size = new Size(160, 27);
            dtStart.TabIndex = 14;
            // 
            // lblEnd
            // 
            lblEnd.AutoSize = true;
            lblEnd.Location = new Point(664, 264);
            lblEnd.Name = "lblEnd";
            lblEnd.Size = new Size(110, 20);
            lblEnd.TabIndex = 15;
            lblEnd.Text = "終了日 [TVのみ]";
            // 
            // dtEnd
            // 
            dtEnd.CustomFormat = "yyyy-MM-dd";
            dtEnd.Format = DateTimePickerFormat.Custom;
            dtEnd.Location = new Point(900, 264);
            dtEnd.Name = "dtEnd";
            dtEnd.ShowCheckBox = true;
            dtEnd.Size = new Size(180, 27);
            dtEnd.TabIndex = 16;
            // 
            // lblEpisodes
            // 
            lblEpisodes.AutoSize = true;
            lblEpisodes.Location = new Point(664, 300);
            lblEpisodes.Name = "lblEpisodes";
            lblEpisodes.Size = new Size(110, 20);
            lblEpisodes.TabIndex = 17;
            lblEpisodes.Text = "総話数 [TVのみ]";
            // 
            // numEpisodes
            // 
            numEpisodes.Location = new Point(900, 300);
            numEpisodes.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            numEpisodes.Name = "numEpisodes";
            numEpisodes.Size = new Size(120, 27);
            numEpisodes.TabIndex = 18;
            // 
            // lblRunTime
            // 
            lblRunTime.AutoSize = true;
            lblRunTime.Location = new Point(664, 336);
            lblRunTime.Name = "lblRunTime";
            lblRunTime.Size = new Size(148, 20);
            lblRunTime.TabIndex = 19;
            lblRunTime.Text = "上映尺(秒) [映画のみ]";
            // 
            // numRunTimeSeconds
            // 
            numRunTimeSeconds.Location = new Point(900, 336);
            numRunTimeSeconds.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            numRunTimeSeconds.Name = "numRunTimeSeconds";
            numRunTimeSeconds.Size = new Size(120, 27);
            numRunTimeSeconds.TabIndex = 20;
            // 
            // lblTitleKana
            // 
            lblTitleKana.AutoSize = true;
            lblTitleKana.Location = new Point(664, 372);
            lblTitleKana.Name = "lblTitleKana";
            lblTitleKana.Size = new Size(78, 20);
            lblTitleKana.TabIndex = 21;
            lblTitleKana.Text = "タイトルかな";
            // 
            // txtTitleKana
            // 
            txtTitleKana.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleKana.Location = new Point(900, 372);
            txtTitleKana.Name = "txtTitleKana";
            txtTitleKana.Size = new Size(588, 27);
            txtTitleKana.TabIndex = 22;
            // 
            // lblTitleShort
            // 
            lblTitleShort.AutoSize = true;
            lblTitleShort.Location = new Point(664, 408);
            lblTitleShort.Name = "lblTitleShort";
            lblTitleShort.Size = new Size(39, 20);
            lblTitleShort.TabIndex = 23;
            lblTitleShort.Text = "略称";
            // 
            // txtTitleShort
            // 
            txtTitleShort.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleShort.Location = new Point(900, 408);
            txtTitleShort.Name = "txtTitleShort";
            txtTitleShort.Size = new Size(180, 27);
            txtTitleShort.TabIndex = 24;
            // 
            // lblTitleShortKana
            // 
            lblTitleShortKana.AutoSize = true;
            lblTitleShortKana.Location = new Point(664, 444);
            lblTitleShortKana.Name = "lblTitleShortKana";
            lblTitleShortKana.Size = new Size(64, 20);
            lblTitleShortKana.TabIndex = 25;
            lblTitleShortKana.Text = "略称かな";
            // 
            // txtTitleShortKana
            // 
            txtTitleShortKana.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleShortKana.Location = new Point(900, 444);
            txtTitleShortKana.Name = "txtTitleShortKana";
            txtTitleShortKana.Size = new Size(588, 27);
            txtTitleShortKana.TabIndex = 26;
            // 
            // lblTitleEn
            // 
            lblTitleEn.AutoSize = true;
            lblTitleEn.Location = new Point(664, 480);
            lblTitleEn.Name = "lblTitleEn";
            lblTitleEn.Size = new Size(82, 20);
            lblTitleEn.TabIndex = 27;
            lblTitleEn.Text = "タイトル(EN)";
            // 
            // txtTitleEn
            // 
            txtTitleEn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleEn.Location = new Point(900, 480);
            txtTitleEn.Name = "txtTitleEn";
            txtTitleEn.Size = new Size(588, 27);
            txtTitleEn.TabIndex = 28;
            // 
            // lblTitleShortEn
            // 
            lblTitleShortEn.AutoSize = true;
            lblTitleShortEn.Location = new Point(664, 516);
            lblTitleShortEn.Name = "lblTitleShortEn";
            lblTitleShortEn.Size = new Size(68, 20);
            lblTitleShortEn.TabIndex = 29;
            lblTitleShortEn.Text = "略称(EN)";
            // 
            // txtTitleShortEn
            // 
            txtTitleShortEn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtTitleShortEn.Location = new Point(900, 516);
            txtTitleShortEn.Name = "txtTitleShortEn";
            txtTitleShortEn.Size = new Size(180, 27);
            txtTitleShortEn.TabIndex = 30;
            // 
            // lblToeiSite
            // 
            lblToeiSite.AutoSize = true;
            lblToeiSite.Location = new Point(664, 552);
            lblToeiSite.Name = "lblToeiSite";
            lblToeiSite.Size = new Size(129, 20);
            lblToeiSite.TabIndex = 31;
            lblToeiSite.Text = "東映アニメ公式URL";
            // 
            // txtToeiSite
            // 
            txtToeiSite.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtToeiSite.Location = new Point(900, 552);
            txtToeiSite.Name = "txtToeiSite";
            txtToeiSite.Size = new Size(588, 27);
            txtToeiSite.TabIndex = 32;
            // 
            // lblToeiLineup
            // 
            lblToeiLineup.AutoSize = true;
            lblToeiLineup.Location = new Point(664, 588);
            lblToeiLineup.Name = "lblToeiLineup";
            lblToeiLineup.Size = new Size(129, 20);
            lblToeiLineup.TabIndex = 33;
            lblToeiLineup.Text = "作品ラインナップURL";
            // 
            // txtToeiLineup
            // 
            txtToeiLineup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtToeiLineup.Location = new Point(900, 588);
            txtToeiLineup.Name = "txtToeiLineup";
            txtToeiLineup.Size = new Size(588, 27);
            txtToeiLineup.TabIndex = 34;
            // 
            // lblAbcSite
            // 
            lblAbcSite.AutoSize = true;
            lblAbcSite.Location = new Point(664, 624);
            lblAbcSite.Name = "lblAbcSite";
            lblAbcSite.Size = new Size(93, 20);
            lblAbcSite.TabIndex = 35;
            lblAbcSite.Text = "ABC公式URL";
            // 
            // txtAbcSite
            // 
            txtAbcSite.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtAbcSite.Location = new Point(900, 624);
            txtAbcSite.Name = "txtAbcSite";
            txtAbcSite.Size = new Size(588, 27);
            txtAbcSite.TabIndex = 36;
            // 
            // lblAmazonPrime
            // 
            lblAmazonPrime.AutoSize = true;
            lblAmazonPrime.Location = new Point(664, 660);
            lblAmazonPrime.Name = "lblAmazonPrime";
            lblAmazonPrime.Size = new Size(162, 20);
            lblAmazonPrime.TabIndex = 37;
            lblAmazonPrime.Text = "Amazon Prime配信URL";
            // 
            // txtAmazonPrime
            // 
            txtAmazonPrime.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtAmazonPrime.Location = new Point(900, 660);
            txtAmazonPrime.Name = "txtAmazonPrime";
            txtAmazonPrime.Size = new Size(588, 27);
            txtAmazonPrime.TabIndex = 38;
            // 
            // btnAdd
            // 
            btnAdd.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnAdd.AutoSize = true;
            btnAdd.Location = new Point(1327, 858);
            btnAdd.Name = "btnAdd";
            btnAdd.Size = new Size(75, 30);
            btnAdd.TabIndex = 39;
            btnAdd.Text = "新規";
            // 
            // btnSave
            // 
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.AutoSize = true;
            btnSave.Location = new Point(1413, 858);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(75, 30);
            btnSave.TabIndex = 40;
            btnSave.Text = "保存";
            // 
            // SeriesEditorForm
            // 
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(1500, 900);
            Controls.Add(lstSeries);
            Controls.Add(lblTitle);
            Controls.Add(txtTitle);
            Controls.Add(lblSlug);
            Controls.Add(txtSlug);
            Controls.Add(lblKind);
            Controls.Add(cmbKind);
            Controls.Add(lblParent);
            Controls.Add(cmbParent);
            Controls.Add(lblRelation);
            Controls.Add(cmbRelation);
            Controls.Add(lblSeq);
            Controls.Add(numSeqInParent);
            Controls.Add(lblStart);
            Controls.Add(dtStart);
            Controls.Add(lblEnd);
            Controls.Add(dtEnd);
            Controls.Add(lblEpisodes);
            Controls.Add(numEpisodes);
            Controls.Add(lblRunTime);
            Controls.Add(numRunTimeSeconds);
            Controls.Add(lblTitleKana);
            Controls.Add(txtTitleKana);
            Controls.Add(lblTitleShort);
            Controls.Add(txtTitleShort);
            Controls.Add(lblTitleShortKana);
            Controls.Add(txtTitleShortKana);
            Controls.Add(lblTitleEn);
            Controls.Add(txtTitleEn);
            Controls.Add(lblTitleShortEn);
            Controls.Add(txtTitleShortEn);
            Controls.Add(lblToeiSite);
            Controls.Add(txtToeiSite);
            Controls.Add(lblToeiLineup);
            Controls.Add(txtToeiLineup);
            Controls.Add(lblAbcSite);
            Controls.Add(txtAbcSite);
            Controls.Add(lblAmazonPrime);
            Controls.Add(txtAmazonPrime);
            Controls.Add(btnAdd);
            Controls.Add(btnSave);
            MinimumSize = new Size(1200, 780);
            Name = "SeriesEditorForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "シリーズ編集";
            ((System.ComponentModel.ISupportInitialize)numSeqInParent).EndInit();
            ((System.ComponentModel.ISupportInitialize)numEpisodes).EndInit();
            ((System.ComponentModel.ISupportInitialize)numRunTimeSeconds).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
