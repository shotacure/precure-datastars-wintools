using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺と累積時間を一覧表示するフォーム。
    /// <para>
    /// 光学ドライブへのディスク挿入を WM_DEVICECHANGE で検知し、自動ロードする機能を持つ。
    /// 解析結果は TSV 形式でクリップボードにコピーできる（Ctrl+C / ボタン）。
    /// </para>
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>現在読み込み中のファイルパス。</summary>
        private string _currentFilePath = "";

        // ----- WM_DEVICECHANGE によるディスク挿入/取り外し検知用定数 -----
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        // 挿入イベントが短時間に複数回発火するのを抑制するデバウンス用フィールド
        private DateTime _lastAutoLoad = DateTime.MinValue;
        private bool _autoLoading = false;

        public MainForm()
        {
            InitializeComponent();

            // Ctrl+C で選択行（未選択時は全行）を TSV としてクリップボードにコピー
            listView.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopySelectedTsv();
                    e.SuppressKeyPress = true;
                }
            };

            // 全行 TSV コピーボタン
            btnCopyTsv.Click += (_, __) => CopyTsv();
                        
            btnLoadDefault.Click += btnLoadDefault_Click;
        }

        /// <summary>
        /// パスの拡張子に応じて Blu-ray (.mpls) または DVD (.IFO) の読み込み処理に振り分ける。
        /// </summary>
        /// <param name="path">対象ファイルパス（.mpls または .IFO）。</param>
        private void LoadPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(this, "ファイルが見つかりません。", "Load", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".mpls")
                {
                    LoadMpls(path); // 既存のBlu-ray表示ルーチン
                }
                else if (ext == ".ifo")
                {
                    LoadIfo(path);  // 既存のDVD表示ルーチン
                }
                else
                {
                    MessageBox.Show(this, "未対応の拡張子です（*.mpls / *.IFO）。", "Load", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// DVD の VTS_xx_0.IFO を解析し、プログラム（チャプター）ごとの尺を一覧表示する。
        /// VIDEO_TS.IFO が指定された場合は VTS ファイルの選択を案内する。
        /// </summary>
        /// <param name="path">IFO ファイルパス。</param>
        private void LoadIfo(string path)
        {
            _currentFilePath = path;
            listView.Items.Clear();

            if (!File.Exists(path))
                throw new FileNotFoundException("ファイルが見つかりません。", path);

            var name = Path.GetFileName(path) ?? path;
            // VIDEO_TS.IFO は全 VTS の目次であり、個別 VTS の解析には VTS_xx_0.IFO が必要
            if (string.Equals(name, "VIDEO_TS.IFO", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "まずは VTS_xx_0.IFO（例: VTS_01_0.IFO）をドロップ/選択してください。\r\n" +
                    "VIDEO_TS.IFO → VTS選択の自動化は今後対応予定です。",
                    "案内",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // IFO バイナリをパースしてプログラム（チャプター）単位の再生時間を取得
            var result = IfoParser.ExtractProgramsFromVtsIfo(path);

            // 各プログラムの尺と累積時間を表示用に組み立て
            var rows = new List<(string label, TimeSpan len, TimeSpan accum)>();
            TimeSpan accum = TimeSpan.Zero;

            int startIndex = 0;

            for (int p = startIndex; p < result.ProgramDurations.Count; p++)
            {
                var dur = result.ProgramDurations[p];
                accum += dur;
                int chapNum = (p - startIndex) + 1;
                rows.Add(($"{chapNum}", dur, accum));
            }

            foreach (var row in rows)
            {
                var lvi = new ListViewItem(row.label);
                lvi.SubItems.Add(FormatTs(row.len));
                lvi.SubItems.Add(FormatTs(row.accum));
                lvi.SubItems.Add(Math.Ceiling(row.len.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                listView.Items.Add(lvi);
            }

            lblInfo.Text = $"{Path.GetFileName(path)} - (DVD) Programs: {result.ProgramDurations.Count}   Cells: {result.CellDurations.Count}";
        }

        /// <summary>
        /// Blu-ray（.mpls）の読み込み表示。Prelude の概念は使わず、Entry マークを章として並べます。
        /// </summary>
        private void LoadMpls(string path)
        {
            _currentFilePath = path;
            listView.Items.Clear();

            if (!File.Exists(path))
                throw new FileNotFoundException("ファイルが見つかりません。", path);

            // MPLS バイナリをパースしてチャプター情報を取得（フォールバック付き）
            var r = MplsParser.Parse(path);

            // 各チャプターの尺と累積時間を ListView に表示
            TimeSpan accum = TimeSpan.Zero;
            for (int i = 0; i < r.Chapters.Count; i++)
            {
                var ch = r.Chapters[i];
                accum += ch.Length;

                var lvi = new ListViewItem((i + 1).ToString());
                lvi.SubItems.Add(FormatTs(ch.Length));
                lvi.SubItems.Add(FormatTs(accum));
                lvi.SubItems.Add(ch.SecondsRounded.ToString(CultureInfo.InvariantCulture));
                listView.Items.Add(lvi);
            }

            lblInfo.Text = $"{Path.GetFileName(path)} - (Blu-ray) Items: {r.PlayItemCount}   Marks: {r.MarkCount}   Duration: {FormatTs(r.PlaylistDuration)}";
        }

        /// <summary>
        /// ListView の選択行（未選択時は全行）をヘッダ付き TSV でクリップボードにコピーする。
        /// </summary>
        private void CopySelectedTsv()
        {
            if (listView.Items.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("#\tLength (hh:mm:ss.ff)\tCumulative\tSeconds (ceil)");

            if (listView.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in listView.SelectedItems)
                {
                    sb.AppendLine(string.Join("\t", new[]
                    {
                        item.SubItems[0].Text,
                        item.SubItems[1].Text,
                        item.SubItems[2].Text,
                        item.SubItems[3].Text
                    }));
                }
            }
            else
            {
                foreach (ListViewItem item in listView.Items)
                {
                    sb.AppendLine(string.Join("\t", new[]
                    {
                        item.SubItems[0].Text,
                        item.SubItems[1].Text,
                        item.SubItems[2].Text,
                        item.SubItems[3].Text
                    }));
                }
            }

            Clipboard.SetText(sb.ToString());
        }

        /// <summary>
        /// ListView の全行をヘッダ付き TSV でクリップボードにコピーし、完了メッセージを表示する。
        /// </summary>
        private void CopyTsv()
        {
            if (listView.Items.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("#\tLength (hh:mm:ss.ff)\tCumulative\tSeconds (ceil)");
            foreach (ListViewItem item in listView.Items)
            {
                sb.AppendLine(string.Join("\t", new[]
                {
                    item.SubItems[0].Text,
                    item.SubItems[1].Text,
                    item.SubItems[2].Text,
                    item.SubItems[3].Text
                }));
            }
            Clipboard.SetText(sb.ToString());
            MessageBox.Show("TSV をコピーしました。Ctrl+Vで貼り付けできます。", "コピー", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>TimeSpan を "HH:MM:SS.ff"（1/100 秒精度）形式にフォーマットする。</summary>
        private static string FormatTs(TimeSpan ts)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}.{3:00}",
                (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
        }

        /// <summary>
        /// 光学ドライブ（DriveType.CDRom）を走査し、既定の Blu-ray / DVD の代表パスを探す。
        /// 見つかれば true とパス、見つからなければ false。
        /// </summary>
        /// <param name="foundPath"></param>
        /// <returns></returns>
        private bool TryFindDiscFile(out string foundPath)
        {
            foundPath = string.Empty;

            // システム上の光学ドライブ（CDRom）のうち、Ready 状態のものを列挙
            var cdroms = DriveInfo.GetDrives()
                .Where(d => (d.DriveType == DriveType.CDRom /*|| d.DriveType == DriveType.Removable*/) && d.IsReady)
                .Select(d => d.RootDirectory.FullName.TrimEnd('\\'));

            foreach (var root in cdroms)
            {
                // --- Blu-ray を優先的にチェック（00000.mpls / 00001.mpls）---
                string[] bdCandidates =
                {
            Path.Combine(root, @"BDMV\PLAYLIST\00000.mpls"),
            Path.Combine(root, @"BDMV\PLAYLIST\00001.mpls"),
        };
                var bdHit = bdCandidates.FirstOrDefault(File.Exists);
                if (bdHit != null) { foundPath = bdHit; return true; }

                // --- DVD を次点でチェック（VTS_01_0.IFO / VIDEO_TS.IFO）---
                string[] dvdCandidates =
                {
            Path.Combine(root, @"VIDEO_TS\VTS_01_0.IFO"),
            Path.Combine(root, @"VIDEO_TS\VIDEO_TS.IFO"),
        };
                var dvdHit = dvdCandidates.FirstOrDefault(File.Exists);
                if (dvdHit != null) { foundPath = dvdHit; return true; }
            }
            return false;
        }

        /// <summary>
        /// ディスクが準備できていれば自動で LoadPath する。
        /// </summary>
        private void AutoLoadIfDiscReady()
        {
            if (TryFindDiscFile(out var autoPath))
            {
                // すでに同じ内容を表示している等の二重読み込みを避けたい場合は、ここでパス比較して弾いてもよい
                LoadPath(autoPath);
            }
        }

        /// <summary>
        /// ロードボタンのクリック：
        /// 1) 光学ドライブを自動探索 → あればそれを開く
        /// 2) なければファイルダイアログ（*.mpls;*.IFO）
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnLoadDefault_Click(object? sender, EventArgs e)
        {
            try
            {
                string path;
                if (TryFindDiscFile(out var autoPath))
                {
                    path = autoPath;
                }
                else
                {
                    using var ofd = new OpenFileDialog
                    {
                        Title = "Blu-ray/DVD プレイリストまたはIFOを選択",
                        Filter = "Blu-ray/MPEG-TS Playlist (*.mpls)|*.mpls|DVD IFO (*.IFO)|*.IFO|All Supported (*.mpls;*.IFO)|*.mpls;*.IFO|All Files (*.*)|*.*",
                        FilterIndex = 3,
                        RestoreDirectory = true,
                        CheckFileExists = true,
                        Multiselect = false
                    };
                    if (ofd.ShowDialog(this) != DialogResult.OK) return;
                    path = ofd.FileName;
                }

                // 既存のロード処理（拡張子で DVD / Blu-ray に振り分け）
                LoadPath(path); // ← あなたの既存メソッドに合わせて呼び出し名は調整
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// フォーム表示完了後に呼ばれる。ディスクが既に挿入されていれば自動で読み込む。
        /// BeginInvoke で UI スレッドのメッセージループに遅延投入する。
        /// </summary>
        /// <param name="e">イベント引数。</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 起動時、すでにディスクが入っていれば自動読み込み
            BeginInvoke((Action)AutoLoadIfDiscReady);
        }

        /// <summary>
        /// WM_DEVICECHANGE を監視し、光学ドライブへのディスク挿入/取り外しを検知して自動ロードする。
        /// 連続発火を防ぐため 0.8 秒の Timer デバウンスを行う。
        /// </summary>
        /// <param name="m">ウィンドウメッセージ。</param>
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_DEVICECHANGE)
            {
                // 到着 or 取り外しのどちらでも、少し待ってからチェック
                if (m.WParam.ToInt32() == DBT_DEVICEARRIVAL || m.WParam.ToInt32() == DBT_DEVICEREMOVECOMPLETE)
                {
                    // ディスク認識のための準備時間として 0.8 秒待ってから自動ロードを実行
                    var now = DateTime.UtcNow;
                    if (!_autoLoading && (now - _lastAutoLoad).TotalSeconds > 1.0)
                    {
                        _autoLoading = true;
                        _lastAutoLoad = now;
                        var t = new System.Windows.Forms.Timer { Interval = 800 }; // 0.8s待ってから実行
                        t.Tick += (s, e) =>
                        {
                            t.Stop(); t.Dispose();
                            try { AutoLoadIfDiscReady(); }
                            finally { _autoLoading = false; }
                        };
                        t.Start();
                    }
                }
            }
        }

    }
}
