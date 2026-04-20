using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Common.Dialogs;
using PrecureDataStars.Catalog.Common.Services;
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
    /// <remarks>
    /// v1.1.0 以降は DB 連携パネルを持ち、既存ディスクとの照合・新規商品登録が可能。
    /// DB 接続が構成されていない場合（App.config なし）は従来どおり読み取り専用で動作する。
    /// </remarks>
    /// </summary>
    public partial class MainForm : Form
    {
        // DB 連携用リポジトリ群（DB 無効モードでは null）
        private readonly DiscRegistrationService? _registration;
        private readonly DiscsRepository? _discsRepo;
        private readonly ProductsRepository? _productsRepo;
        private readonly TracksRepository? _tracksRepo;
        private readonly ProductKindsRepository? _productKindsRepo;
        private readonly SeriesRepository? _seriesRepo;

        // 最後に読み取った CD の情報（DB 連携時に照合／登録に使う）
        private LastReadSnapshot? _lastRead;

        /// <summary>DB 連携無効モード（従来互換）コンストラクタ。</summary>
        public MainForm()
        {
            InitializeComponent();
            // DB 連携 UI は表示するが、使用不可にしておく
            SetDbPanelEnabled(false, "DB 接続が設定されていません (App.config)");
        }

        /// <summary>
        /// DB 連携有効モードのコンストラクタ。
        /// </summary>
        public MainForm(
            DiscRegistrationService registration,
            DiscsRepository discsRepo,
            ProductsRepository productsRepo,
            TracksRepository tracksRepo,
            ProductKindsRepository productKindsRepo,
            SeriesRepository seriesRepo)
        {
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
            _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
            _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
            _productKindsRepo = productKindsRepo ?? throw new ArgumentNullException(nameof(productKindsRepo));
            _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));

            InitializeComponent();
            SetDbPanelEnabled(false, "CD を読み込むと有効になります");
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
            SetDbPanelEnabled(false, _registration is null ? "DB 接続が設定されていません" : "CD を読み込むと有効になります");
            _lastRead = null;
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
                string? mcnRaw = ReadMediaCatalogNumber(h);
                txtMcn.Text = mcnRaw ?? "—";

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

                // DB 連携パネル用に、読み取り結果をスナップショット保存
                _lastRead = BuildSnapshot(tracksOnly, leadOutLba, mcnRaw, isrcMap, catalog);
                SetDbPanelEnabled(_registration is not null, _registration is null ? "DB 接続が設定されていません" : "照合可能");
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

        // ===== DB 連携機能（v1.1.0 追加） =====

        /// <summary>DB 連携パネルの活性状態を切り替える。</summary>
        private void SetDbPanelEnabled(bool enabled, string status)
        {
            btnDbMatch.Enabled = enabled;
            lblDbStatus.Text = status;
        }

        /// <summary>読み取り直後のスナップショットから DiscRegistration 用のオブジェクトを組み立てる。</summary>
        private LastReadSnapshot BuildSnapshot(
            List<TocTrack> tracksOnly,
            int leadOutLba,
            string? mcn,
            Dictionary<int, string?> isrcMap,
            CdTextCatalog catalog)
        {
            // freedb 互換 Disc ID の計算（トラック開始 LBA の簡易ハッシュ）
            string? cddbId = ComputeCddbDiscId(tracksOnly, leadOutLba);

            // ディスクレコード（catalog_no / product_catalog_no は登録時に確定）
            var disc = new Disc
            {
                CatalogNo = "", // 登録時に入力
                ProductCatalogNo = "", // 登録時に CatalogNo をコピー（単品時）または 1 枚目の catalog_no を設定
                MediaFormat = "CD",
                Mcn = string.IsNullOrWhiteSpace(mcn) ? null : mcn,
                TotalTracks = (byte)tracksOnly.Count,
                TotalLengthFrames = (uint)Math.Max(0, leadOutLba),
                NumChapters = (ushort)tracksOnly.Count,
                CdTextAlbumTitle = catalog.Album.GetValueOrDefault("Title"),
                CdTextAlbumPerformer = catalog.Album.GetValueOrDefault("Performer"),
                CdTextAlbumSongwriter = catalog.Album.GetValueOrDefault("Songwriter"),
                CdTextAlbumComposer = catalog.Album.GetValueOrDefault("Composer"),
                CdTextAlbumArranger = catalog.Album.GetValueOrDefault("Arranger"),
                CdTextAlbumMessage = catalog.Album.GetValueOrDefault("Message"),
                CdTextDiscId = catalog.Album.GetValueOrDefault("DiscId"),
                CdTextGenre = catalog.Album.GetValueOrDefault("Genre"),
                CddbDiscId = cddbId,
                LastReadAt = DateTime.Now,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };

            // トラックレコード一覧（content_kind は初期 OTHER。後続 GUI で個別紐付け想定）
            var trackRecs = new List<Track>(tracksOnly.Count);
            for (int i = 0; i < tracksOnly.Count; i++)
            {
                var t = tracksOnly[i];
                int start = t.StartLba;
                int end = (i < tracksOnly.Count - 1) ? tracksOnly[i + 1].StartLba : leadOutLba;
                int len = Math.Max(0, end - start);

                trackRecs.Add(new Track
                {
                    CatalogNo = "",
                    TrackNo = (byte)t.TrackNumber,
                    ContentKindCode = "OTHER",
                    StartLba = (uint)t.StartLba,
                    LengthFrames = (uint)len,
                    Isrc = isrcMap.TryGetValue(t.TrackNumber, out var isrc) ? isrc : null,
                    IsDataTrack = (t.Control & 0x4) != 0,
                    HasPreEmphasis = (t.Control & 0x1) != 0,
                    IsCopyPermitted = (t.Control & 0x8) != 0,
                    CdTextTitle = catalog.GetTrackField(t.TrackNumber, "Title") is { Length: > 0 } tt ? tt : null,
                    CdTextPerformer = catalog.GetTrackField(t.TrackNumber, "Performer") is { Length: > 0 } tp ? tp : null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                });
            }

            return new LastReadSnapshot(disc, trackRecs);
        }

        /// <summary>
        /// 簡易的な freedb 互換 Disc ID を算出する。
        /// 仕様: sum(トラック開始秒の各桁合計) % 0xFF を上位 2 桁、総秒数を中央 4 桁、トラック数を下位 2 桁。
        /// </summary>
        private static string ComputeCddbDiscId(List<TocTrack> tracks, int leadOutLba)
        {
            int sum = 0;
            foreach (var t in tracks)
            {
                int sec = t.StartLba / 75;
                while (sec > 0) { sum += sec % 10; sec /= 10; }
            }
            int totalSec = (leadOutLba - tracks[0].StartLba) / 75;
            int n = tracks.Count;
            uint id = (((uint)(sum % 0xFF)) << 24) | (((uint)totalSec & 0xFFFF) << 8) | (uint)(n & 0xFF);
            return id.ToString("X8");
        }

        /// <summary>DB 連携ボタン：既存ディスク照合 → 反映 or 新規登録のフロー起点。</summary>
        private async void btnDbMatch_Click(object? sender, EventArgs e)
        {
            if (_registration is null || _discsRepo is null || _productsRepo is null
                || _productKindsRepo is null || _seriesRepo is null || _lastRead is null)
            {
                return;
            }

            try
            {
                // 1. 自動照合
                var match = await _registration.FindCandidatesAsync(
                    _lastRead.Disc.Mcn,
                    _lastRead.Disc.CddbDiscId,
                    _lastRead.Disc.TotalTracks ?? 0,
                    _lastRead.Disc.TotalLengthFrames ?? 0);

                // 2. ダイアログ表示
                using var dlg = new DiscMatchDialog(_discsRepo, match.Candidates, match.MatchedBy);
                var result = dlg.ShowDialog(this);
                if (result != DialogResult.OK) return;

                if (dlg.SelectedDisc is not null)
                {
                    // 既存ディスクに反映：物理情報のみ同期する。
                    // SyncPhysicalInfoAsync は DB 側で title / title_short / title_en / disc_no_in_set /
                    // disc_kind_code / product_catalog_no / notes 等を保全するため、ここでそれらを
                    // 明示コピーする必要はない（むしろ既存 DB 値が NULL の場合に上書きしてしまう危険がある）。
                    // CatalogNo は一致させる必要があるため、これだけは引き継ぐ。
                    var disc = _lastRead.Disc;
                    disc.CatalogNo = dlg.SelectedDisc.CatalogNo;

                    foreach (var t in _lastRead.Tracks) t.CatalogNo = disc.CatalogNo;
                    await _registration.SyncPhysicalInfoAsync(disc, _lastRead.Tracks);

                    MessageBox.Show(this,
                        $"ディスク [{disc.CatalogNo}] に CD 物理情報を反映しました。\n"
                        + $"トラック {_lastRead.Tracks.Count} 件を更新しました。\n"
                        + "（タイトル・曲紐付け等の Catalog 情報は保全されます）",
                        "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (dlg.WantsNewRegistration)
                {
                    // 新規商品作成 → 新規ディスク登録
                    // 品番を先に入力させ、それを代表品番としても使う（CreateProductAndCommitAsync の内部で自動コピー）
                    string? catalogNo = PromptCatalogNo();
                    if (string.IsNullOrWhiteSpace(catalogNo)) return;

                    var initTitle = _lastRead.Disc.CdTextAlbumTitle ?? "";
                    using var pdlg = new NewProductDialog(_productKindsRepo, _seriesRepo, initTitle);
                    if (pdlg.ShowDialog(this) != DialogResult.OK || pdlg.Result is null) return;

                    var disc = _lastRead.Disc;
                    disc.CatalogNo = catalogNo!.Trim();
                    foreach (var t in _lastRead.Tracks) t.CatalogNo = disc.CatalogNo;

                    // 新規登録は全列 INSERT が正しい挙動（保全対象の既存データがない）。
                    await _registration.CreateProductAndCommitAsync(pdlg.Result, disc, _lastRead.Tracks);

                    MessageBox.Show(this,
                        $"新規商品 [{disc.CatalogNo}] とディスクを作成しました。\n"
                        + $"トラック {_lastRead.Tracks.Count} 件を登録しました。",
                        "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "DB 連携エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>品番入力用の簡易プロンプト（InputBox 相当）。</summary>
        private string? PromptCatalogNo()
        {
            using var f = new Form
            {
                Text = "品番入力",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ClientSize = new Size(420, 110),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false
            };
            var lbl = new Label { Text = "品番 (例: COCX-12345)", Location = new Point(12, 12), Size = new Size(380, 20) };
            var txt = new TextBox { Location = new Point(12, 36), Size = new Size(380, 23) };
            var ok = new Button { Text = "OK", Location = new Point(226, 68), Size = new Size(80, 28), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "キャンセル", Location = new Point(312, 68), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
        }

        /// <summary>
        /// CDAnalyzer の読み取り結果スナップショット。DB 連携時に使用。
        /// </summary>
        private sealed record LastReadSnapshot(Disc Disc, List<Track> Tracks);
    }
}
