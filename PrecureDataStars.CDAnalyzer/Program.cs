using System;
using System.Windows.Forms;

namespace PrecureDataStars.CDAnalyzer
{
    /// <summary>CDAnalyzer（CD-DA トラック解析ツール）のエントリポイント。</summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
