#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Pickers;

partial class SongRecordingPickerDialog
{
    private System.ComponentModel.IContainer? components = null;

    private Label lblKeyword = null!;
    private TextBox txtKeyword = null!;
    private ListView lvResults = null!;
    private Label lblHitCount = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblKeyword = new Label { Text = "キーワード", Location = new Point(12, 14), Size = new Size(80, 20) };
        txtKeyword = new TextBox
        {
            Location = new Point(98, 10),
            Size = new Size(640, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "曲名 / 歌手名 / バリエーション ラベルで検索"
        };

        lvResults = new ListView
        {
            Location = new Point(12, 44),
            Size = new Size(726, 360),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true
        };
        // 列構成：録音 ID / 曲タイトル / 歌手 / バリエーション / 親曲 ID
        lvResults.Columns.Add("録音 ID", 70, HorizontalAlignment.Right);
        lvResults.Columns.Add("曲タイトル", 280);
        lvResults.Columns.Add("歌手", 180);
        lvResults.Columns.Add("バリエーション", 110);
        lvResults.Columns.Add("曲 ID", 70, HorizontalAlignment.Right);

        lblHitCount = new Label
        {
            Location = new Point(12, 410),
            Size = new Size(400, 20),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Text = ""
        };

        btnOk = new Button
        {
            Text = "OK",
            Location = new Point(578, 405),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(663, 405),
            Size = new Size(75, 28),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel
        };

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(750, 444);
        Controls.AddRange(new Control[] { lblKeyword, txtKeyword, lvResults, lblHitCount, btnOk, btnCancel });
        Name = "SongRecordingPickerDialog";
        Text = "歌録音を選択";
        StartPosition = FormStartPosition.CenterParent;
        AcceptButton = btnOk;
        CancelButton = btnCancel;
        MinimumSize = new Size(600, 350);
    }
}