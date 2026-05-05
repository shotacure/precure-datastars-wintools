using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.Dialogs;

partial class CreditCopyDialog
{
    private IContainer components = null!;

    private Label lblSrcInfo = null!;
    private Label lblFixedCreditKind = null!;
    private Label lblSeries = null!;
    private ComboBox cboSeries = null!;
    private Label lblEpisode = null!;
    private ComboBox cboEpisode = null!;
    private Label lblPresentation = null!;
    private RadioButton rbPresentationCards = null!;
    private RadioButton rbPresentationRoll = null!;
    private Label lblPartType = null!;
    private ComboBox cboPartType = null!;
    private Label lblNotes = null!;
    private TextBox txtNotes = null!;
    private Button btnOk = null!;
    private Button btnCancel = null!;

    /// <summary>コンボに流す「ID + 表示文字列」のシンプル DTO（コンボの DisplayMember=Label / ValueMember=Id 用）。</summary>
    private sealed class IdLabel
    {
        public int Id { get; }
        public string Label { get; }
        public IdLabel(int id, string label) { Id = id; Label = label; }
    }

    /// <summary>コンボに流す「Code + 表示文字列」のシンプル DTO（cboPartType 用）。</summary>
    private sealed class CodeLabel
    {
        public string Code { get; }
        public string Label { get; }
        public CodeLabel(string code, string label) { Code = code; Label = label; }
    }

    private void InitializeComponent()
    {
        components = new Container();
        Text = "クレジット話数コピー";
        // ダイアログサイズは固定。中央寄せで表示する。
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(440, 380);

        lblSrcInfo = new Label
        {
            Location = new Point(12, 12),
            Size = new Size(416, 20),
            Text = "コピー元: -"
        };
        lblFixedCreditKind = new Label
        {
            Location = new Point(12, 36),
            Size = new Size(416, 20),
            Text = "クレジット種別（固定）: -"
        };

        lblSeries = new Label { Text = "コピー先シリーズ", Location = new Point(12, 70), Size = new Size(120, 20) };
        cboSeries = new ComboBox
        {
            Location = new Point(140, 66), Size = new Size(286, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        lblEpisode = new Label { Text = "コピー先エピソード", Location = new Point(12, 102), Size = new Size(120, 20) };
        cboEpisode = new ComboBox
        {
            Location = new Point(140, 98), Size = new Size(286, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        lblPresentation = new Label { Text = "presentation", Location = new Point(12, 138), Size = new Size(120, 20) };
        rbPresentationCards = new RadioButton { Text = "CARDS", Location = new Point(140, 136), Size = new Size(70, 22), Checked = true };
        rbPresentationRoll  = new RadioButton { Text = "ROLL",  Location = new Point(216, 136), Size = new Size(70, 22) };

        lblPartType = new Label { Text = "part_type", Location = new Point(12, 170), Size = new Size(120, 20) };
        cboPartType = new ComboBox
        {
            Location = new Point(140, 166), Size = new Size(286, 23),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        lblNotes = new Label { Text = "備考", Location = new Point(12, 200), Size = new Size(80, 20) };
        txtNotes = new TextBox
        {
            Location = new Point(12, 220), Size = new Size(414, 100),
            Multiline = true
        };

        btnOk = new Button { Text = "OK", Location = new Point(252, 336), Size = new Size(80, 28), DialogResult = DialogResult.None };
        btnCancel = new Button { Text = "キャンセル", Location = new Point(346, 336), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[]
        {
            lblSrcInfo, lblFixedCreditKind,
            lblSeries, cboSeries,
            lblEpisode, cboEpisode,
            lblPresentation, rbPresentationCards, rbPresentationRoll,
            lblPartType, cboPartType,
            lblNotes, txtNotes,
            btnOk, btnCancel
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }
}
