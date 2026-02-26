using System.Windows.Forms;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace PrecureDataStars.Episodes;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;
    private MenuStrip menuStrip1 = null!;
    private ToolStripMenuItem mnuSeries = null!;
    private ToolStripMenuItem mnuEpisodes = null!;

    /// <inheritdoc />
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
        components = new System.ComponentModel.Container();
        menuStrip1 = new MenuStrip();
        mnuSeries = new ToolStripMenuItem();
        mnuEpisodes = new ToolStripMenuItem();

        // menuStrip
        menuStrip1.Items.AddRange(new ToolStripItem[] { mnuSeries, mnuEpisodes });
        menuStrip1.Location = new Point(0, 0);
        menuStrip1.Name = "menuStrip1";
        menuStrip1.Size = new Size(1000, 24);
        menuStrip1.TabIndex = 0;
        menuStrip1.Text = "menuStrip1";

        // mnuSeries
        mnuSeries.Name = "mnuSeries";
        mnuSeries.Size = new Size(92, 20);
        mnuSeries.Text = "シリーズ編集";
        mnuSeries.Click += mnuSeries_Click;

        // mnuEpisodes
        mnuEpisodes.Name = "mnuEpisodes";
        mnuEpisodes.Size = new Size(137, 20);
        mnuEpisodes.Text = "エピソード編集 (TV)";
        mnuEpisodes.Click += mnuEpisodes_Click;

        // MainForm
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1000, 700);
        Controls.Add(menuStrip1);
        MainMenuStrip = menuStrip1;
        Name = "MainForm";
        Text = "precure-datastars-wintools — Episodes";
        StartPosition = FormStartPosition.CenterScreen;
    }
}
