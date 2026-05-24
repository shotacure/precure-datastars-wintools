#nullable enable
using System.Drawing;
using System.Windows.Forms;

namespace PrecureDataStars.Catalog.Forms.NameResolution;

partial class MusicNameResolutionForm
{
    private System.ComponentModel.IContainer? components = null;

    private TabControl tabRoles = null!;
    private TabPage tabLyrics = null!;
    private TabPage tabComposition = null!;
    private TabPage tabArrangement = null!;
    private TabPage tabVocals = null!;

    // 4 タブ共通：各タブには 1 つの SplitContainer + 内訳 UI を新規生成して詰める。
    // Designer 側では空タブだけ用意し、ロジック側で各タブの中身を組み立てる。
    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel lblStatus = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        tabRoles = new TabControl();
        tabLyrics = new TabPage();
        tabComposition = new TabPage();
        tabArrangement = new TabPage();
        tabVocals = new TabPage();
        statusStrip = new StatusStrip();
        lblStatus = new ToolStripStatusLabel();

        SuspendLayout();

        tabRoles.Dock = DockStyle.Fill;
        tabRoles.TabPages.Add(tabLyrics);
        tabRoles.TabPages.Add(tabComposition);
        tabRoles.TabPages.Add(tabArrangement);
        tabRoles.TabPages.Add(tabVocals);

        tabLyrics.Text = "作詞";
        tabLyrics.Padding = new Padding(6);
        tabComposition.Text = "作曲";
        tabComposition.Padding = new Padding(6);
        tabArrangement.Text = "編曲";
        tabArrangement.Padding = new Padding(6);
        tabVocals.Text = "歌唱者";
        tabVocals.Padding = new Padding(6);

        statusStrip.Items.Add(lblStatus);
        lblStatus.Text = "";

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 720);
        Controls.Add(tabRoles);
        Controls.Add(statusStrip);
        Name = "MusicNameResolutionForm";
        Text = "音楽名寄せセンター";
        StartPosition = FormStartPosition.CenterParent;

        ResumeLayout(false);
        PerformLayout();
    }
}
