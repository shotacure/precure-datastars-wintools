using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using static PrecureDataStars.CDAnalyzer.ScsiMmci;
using static PrecureDataStars.CDAnalyzer.Helpers;

namespace PrecureDataStars.CDAnalyzer
{
    /// <summary>
    /// CD-DA ディスクのトラック情報を SCSI MMC コマンドで読み取り、一覧表示するフォーム。
    /// <para>
    /// TOC（トラック一覧・尺・累積時間）、MCN（メディアカタログ番号）、
    /// CD-Text（アルバム名・アーティスト名・トラックタイトル）を取得し、
    /// TSV 形式でのクリップボードコピーに対応する。
    /// WM_DEVICECHANGE でメディア挿抜を検知し、ドライブリストを自動更新する。
    /// </para>
    /// </summary>
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        // ----- イベントハンドラ -----

        /// <summary>フォームロード時にシステム上の光学ドライブを列挙する。</summary>
        private void MainForm_Load(object? sender, EventArgs e)
        {
            RefreshDriveList();
        }

        /// <summary>「読み取り」ボタンクリック時に選択ドライブの CD 情報を全取得する。</summary>
        private void btnLoad_Click(object? sender, EventArgs e)
        {
            LoadAll();
        }

        // ----- UI 更新メソッド -----

        /// <summary>
        /// システム上の光学ドライブ (DriveType.CDRom) を列挙し、コンボボックスに表示する。
        /// ドライブが見つからない場合はラベルに案内メッセージを表示する。
        /// </summary>
        private void RefreshDriveList()
        {
            var opticals = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.CDRom)
                .Select(d => d.Name[0])
                .OrderBy(c => c)
                .ToList();

            cboDrives.Items.Clear();
            foreach (var c in opticals) cboDrives.Items.Add($"{c}:\\ (CD/DVD/Blu-ray)");
            if (cboDrives.Items.Count > 0) cboDrives.SelectedIndex = 0;

            lblSummary.Text = opticals.Count == 0 ? "光学ドライブが見つかりません" : "";
            gridTracks.DataSource = null;
            gridAlbum.DataSource = null;
            txtMcn.Text = "";
            btnCopyTsv.Enabled = false;
        }

        /// <summary>
        /// 選択された光学ドライブから TOC・MCN・ISRC・CD-Text を一括読み取りし、
        /// DataGridView にバインドする。
        /// </summary>
        private void LoadAll()
        {
            if (cboDrives.SelectedItem is null)
            {
                MessageBox.Show("光学ドライブを選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            char driveLetter = cboDrives.SelectedItem.ToString()![0];

            try
            {
                // SCSI パススルー用にデバイスハンドルを開く
                using SafeFileHandle h = OpenCdDevice(driveLetter);

                // --- TOC (Table of Contents): 全トラックの開始 LBA を取得 ---
                var tocAll = ReadToc(h);
                var tracksOnly = tocAll.Where(t => t.TrackNumber != 0xAA).OrderBy(t => t.TrackNumber).ToList();
                if (tracksOnly.Count == 0)
                {
                    MessageBox.Show("TOCが読み取れませんでした。オーディオCDか確認してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                // Lead-Out (Track 0xAA) の LBA = ディスク末尾。なければ推定値を使用
                int leadOutLba = tocAll.FirstOrDefault(t => t.TrackNumber == 0xAA)?.StartLba
                                 ?? (tracksOnly.Last().StartLba + 75 * 60 * 10);

                // --- MCN (Media Catalog Number): JAN/EAN バーコード相当の 13 桁数字 ---
                txtMcn.Text = ReadMediaCatalogNumber(h) ?? "—";

                // --- ISRC: 各トラックの国際標準レコーディングコード (12 文字) ---
                var isrcMap = new Dictionary<int, string?>();
                foreach (var t in tracksOnly)
                    isrcMap[t.TrackNumber] = ReadIsrcForTrack(h, (byte)t.TrackNumber);

                // --- CD-Text: パック列を読み取り → デコードしてカタログ化 ---
                var packs = ReadCdTextPacks(h);
                var catalog = BuildCdTextCatalog(packs);

                // 上段の DataGridView: トラック情報テーブルの構築
                var table = new DataTable();
                table.Columns.Add("Track", typeof(int));
                table.Columns.Add("Start LBA", typeof(int));
                table.Columns.Add("Start (MM:SS.ff)", typeof(string));
                table.Columns.Add("Length (frames)", typeof(int));
                table.Columns.Add("Length (MM:SS.ff)", typeof(string));
                table.Columns.Add("Control", typeof(string));
                table.Columns.Add("ADR", typeof(byte));
                table.Columns.Add("ISRC", typeof(string));
                table.Columns.Add("Title", typeof(string));
                table.Columns.Add("Performer", typeof(string));

                for (int i = 0; i < tracksOnly.Count; i++)
                {
                    var t = tracksOnly[i];
                    int start = t.StartLba;
                    int end = (i < tracksOnly.Count - 1) ? tracksOnly[i + 1].StartLba : leadOutLba;
                    int len = Math.Max(0, end - start);

                    // Control フィールドから属性文字列を構築（Audio/Data, Emphasis, CopyOK）
                    string controlStr = ((t.Control & 0x4) != 0 ? "Data" : "Audio")
                        + (((t.Control & 0x1) != 0) ? ", Emph" : "")
                        + (((t.Control & 0x8) != 0) ? ", CopyOK" : "");

                    int relFrames = t.StartLba;
                    string startTime = FramesToTimeString(relFrames);

                    string title = catalog.GetTrackField(t.TrackNumber, "Title");
                    string perf = catalog.GetTrackField(t.TrackNumber, "Performer");

                    table.Rows.Add(
                        t.TrackNumber,
                        t.StartLba,
                        startTime,
                        len,
                        FramesToTimeString(len),
                        controlStr,
                        t.Adr,
                        isrcMap[t.TrackNumber] ?? "—",
                        string.IsNullOrWhiteSpace(title) ? "—" : title,
                        string.IsNullOrWhiteSpace(perf) ? "—" : perf
                    );
                }
                gridTracks.DataSource = table;

                // 下段の DataGridView: アルバム単位の CD-Text 情報
                var albumTable = new DataTable();
                albumTable.Columns.Add("Field", typeof(string));
                albumTable.Columns.Add("Value", typeof(string));

                string[] keys = new[] { "Title", "Performer", "Songwriter", "Composer", "Arranger", "Message", "DiscId", "Genre" };
                foreach (var k in keys)
                {
                    if (catalog.Album.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v))
                        albumTable.Rows.Add(k, v);
                }
                foreach (var kv in catalog.Album.OrderBy(kv => kv.Key))
                {
                    if (!keys.Contains(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                        albumTable.Rows.Add(kv.Key, kv.Value);
                }
                gridAlbum.DataSource = albumTable;

                lblSummary.Text = $"Drive {driveLetter}: Tracks={tracksOnly.Count}, Lead-Out LBA={leadOutLba}, CD-Text packs={packs.Count}";
                btnCopyTsv.Enabled = table.Rows.Count > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"読み取りエラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// WM_DEVICECHANGE を監視し、メディアの挿入/取り外しに応じて
        /// ドライブリストを再構築し、挿入時は自動読み取りを行う。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_DEVICECHANGE = 0x0219;
            const int DBT_DEVICEARRIVAL = 0x8000;
            const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

            if (m.Msg == WM_DEVICECHANGE)
            {
                int wparam = m.WParam.ToInt32();
                if (wparam == DBT_DEVICEARRIVAL || wparam == DBT_DEVICEREMOVECOMPLETE)
                {
                    // 現在の選択を記憶してからドライブリストを再構築
                    var prev = cboDrives.SelectedItem?.ToString();
                    RefreshDriveList();
                    if (!string.IsNullOrEmpty(prev))
                    {
                        int idx = -1;
                        for (int i = 0; i < cboDrives.Items.Count; i++)
                            if (Equals(cboDrives.Items[i]!.ToString(), prev)) { idx = i; break; }
                        if (idx >= 0)
                        {
                            cboDrives.SelectedIndex = idx;
                            if (wparam == DBT_DEVICEARRIVAL) LoadAll(); // 挿入時は自動読み取り
                            else
                            {
                                gridTracks.DataSource = null;
                                gridAlbum.DataSource = null;
                                txtMcn.Text = "";
                            }
                        }
                    }
                }
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// トラック情報を TSV 形式でクリップボードにコピーする。
        /// 出力形式: [空列]\t[Track]\t[Length(frames)] × 行数。
        /// </summary>
        private void btnCopyTsv_Click(object? sender, EventArgs e)
        {
            if (gridTracks.DataSource is not DataTable dt || dt.Rows.Count == 0)
            {
                MessageBox.Show("コピー対象のトラック情報がありません。", "情報",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 必須列の存在チェック（DataTable のスキーマが想定通りか確認）
            if (!dt.Columns.Contains("Track") || !dt.Columns.Contains("Length (frames)"))
            {
                MessageBox.Show("必要な列（Track / Length (frames)）が見つかりません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var sb = new StringBuilder(dt.Rows.Count * 16);

            foreach (DataRow row in dt.Rows)
            {
                // 出力形式: 空列 + Track 番号 + Length (フレーム数) の TSV
                var track = row["Track"]?.ToString() ?? "";
                var lengthSectors = row["Length (frames)"]?.ToString() ?? "0";

                sb.Append('\t')          // 空列
                  .Append(track).Append('\t')
                  .Append(lengthSectors)
                  .Append(Environment.NewLine);
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                btnCopyTsv.Text = "コピー済み ✓";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"クリップボードへコピーできませんでした。\n{ex.Message}", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
