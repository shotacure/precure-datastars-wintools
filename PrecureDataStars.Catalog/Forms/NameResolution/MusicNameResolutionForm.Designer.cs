#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.NameResolution;

partial class MusicNameResolutionForm
{
    private System.ComponentModel.IContainer? components = null;

    // ルートレイアウトとステータスバーだけ Designer 側で持つ。実体の UI（左の未解決一覧 +
    // 右の token 編集 2 種：PERSON 系・VOCALS）はロジック側 BuildLayout で動的に生成する。
    private SplitContainer split = null!;
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel lblStatus = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        split = new SplitContainer();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();

        SuspendLayout();

        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.FixedPanel = FixedPanel.Panel1;
        split.SplitterDistance = 540;

        statusStrip.Items.Add(lblStatus);
        lblStatus.Text = "";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 720);
        Controls.Add(split);
        Controls.Add(statusStrip);
        Name = "MusicNameResolutionForm";
        Text = "音楽名寄せセンター";
        StartPosition = FormStartPosition.CenterParent;

        ResumeLayout(false);
        PerformLayout();
    }
}
