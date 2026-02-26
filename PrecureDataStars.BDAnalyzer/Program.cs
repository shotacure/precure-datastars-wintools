using System;
using System.Windows.Forms;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>BDAnalyzer（Blu-ray/DVD チャプター解析ツール）のエントリポイント。</summary>
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
