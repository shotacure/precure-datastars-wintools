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
    /// <summary>CD-DA ディスクのトラック情報を SCSI MMC コマンドで読み取り、一覧表示するフォーム。</summary>
    public partial class MainForm : Form
    {
        // DB 連携用リポジトリ群（DB 無効モードでは null）
        private readonly DiscRegistrationService? _registration;
        private readonly DiscsRepository? _discsRepo;
        private readonly ProductsRepository? _productsRepo;
        private readonly TracksRepository? _tracksRepo;
        private readonly ProductKindsRepository? _productKindsRepo;
        private readonly SeriesRepository? _seriesRepo;
        // 商品社名マスタ（NewProductDialog の既定社取得・picker 用）
        private readonly ProductCompaniesRepository? _productCompaniesRepo;

        // 最後に読み取った CD の情報（DB 連携時に照合／登録に使う）
        private LastReadSnapshot? _lastRead;

        /// <summary>DB 連携無効モード（従来互換）コンストラクタ。</summary>
        public MainForm()
        {
            InitializeComponent();
            // DB 連携 UI は表示するが、使用不可にしておく
            SetDbPanelEnabled(false, "DB 接続が設定されていません (App.config)");
        }

        /// <summary>DB 連携有効モードのコンストラクタ。</summary>
        public MainForm(
            DiscRegistrationService registration,
            DiscsRepository discsRepo,
            ProductsRepository productsRepo,
            TracksRepository tracksRepo,
            ProductKindsRepository productKindsRepo,
            SeriesRepository seriesRepo,
            // 商品社名マスタ
            ProductCompaniesRepository productCompaniesRepo)
        {
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
            _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
            _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
            _productKindsRepo = productKindsRepo ?? throw new ArgumentNullException(nameof(productKindsRepo));
            _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
            _productCompaniesRepo = productCompaniesRepo ?? throw new ArgumentNullException(nameof(productCompaniesRepo));

            InitializeComponent();
            SetDbPanelEnabled(false, "CD を読み込むと有効になります");
        }

        // ----- イベントハンドラ -----

        /// <summary>フォームロード時にシステム上の光学ドライブを列挙する。</summary>
        private void MainForm_Load(object? sender, EventArgs e)
        {
            RefreshDriveList();
        }

        /// <summary>実行中のディスク読み取りのキャンセルソース。null のとき読み取りは走っていない。 読み取り中は「読み取り」ボタンが「キャンセル」ボタンに切り替わり、クリックで本ソースの Cancel を呼ぶ。</summary>
        private CancellationTokenSource? _readCts;

        /// <summary>「読み取り」ボタンクリック時に選択ドライブの CD 情報を全取得する。 読み取り実行中のクリックはキャンセル要求として扱う（ボタンは「キャンセル」表記に切り替わっている）。</summary>
        private async void btnLoad_Click(object? sender, EventArgs e)
        {
            if (_readCts is not null)
            {
                _readCts.Cancel();
                return;
            }
            await LoadAllAsync();
        }

        // ----- UI 更新メソッド -----

        /// <summary>システム上の光学ドライブ (DriveType.CDRom) を列挙し、コンボボックスに表示する。 ドライブが見つからない場合はラベルに案内メッセージを表示する。</summary>
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

        /// <summary>ワーカースレッドでのディスク読み取り結果（UI スレッドへ渡すスナップショット）。 メディア種別ごとの離脱ケースは <see cref="Tracks"/> の null / 空で表現する： 非 CD・メディア未挿入は <c>Tracks=null</c>、TOC 読み取り失敗は空リスト、成功時は 1 件以上。</summary>
        private sealed record DiscReadOutcome
        {
            public MediaProfile Profile { get; init; }
            public ushort RawProfile { get; init; }
            public List<TocTrack>? Tracks { get; init; }
            public int LeadOutLba { get; init; }
            public string? McnRaw { get; init; }
            public Dictionary<int, string?>? IsrcMap { get; init; }
            public int CdTextPackCount { get; init; }
            public CdTextCatalog? Catalog { get; init; }
        }

        /// <summary>選択された光学ドライブから TOC・MCN・ISRC・CD-Text を一括読み取りし、 DataGridView にバインドする。 デバイス I/O（数秒かかり得る）は <see cref="Task.Run(Action)"/> のワーカースレッドで実行し、 UI スレッドを止めない。読み取り中は「読み取り」ボタンが「キャンセル」に切り替わり、 トラック単位の区切りで中断できる。</summary>
        /// <param name="silent">
        /// true のとき、ドライブメディア挿入の自動トリガから呼ばれた扱いとし、
        /// 非 CD メディア検知時にメッセージボックスを出さずサイレントに終了する。
        /// false のとき（既定）、ユーザの「読み取り」ボタン操作扱いで、エラーや警告を MessageBox 表示する。
        /// </param>
        private async Task LoadAllAsync(bool silent = false)
        {
            // 再入抑止（自動トリガ経路。手動クリックは btnLoad_Click 側でキャンセル要求に分岐済み）。
            if (_readCts is not null) return;

            if (cboDrives.SelectedItem is null)
            {
                if (!silent)
                    MessageBox.Show("光学ドライブを選択してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            char driveLetter = cboDrives.SelectedItem.ToString()![0];

            // 読み取り中の UI 状態へ。ボタンはキャンセルに切替、ドライブ変更・TSV コピー・DB 連携は封鎖する。
            _readCts = new CancellationTokenSource();
            btnLoad.Text = "キャンセル";
            cboDrives.Enabled = false;
            btnCopyTsv.Enabled = false;
            SetDbPanelEnabled(false, "読み取り中...");
            lblSummary.Text = $"Drive {driveLetter}: 読み取り中...";

            try
            {
                var ct = _readCts.Token;
                var outcome = await Task.Run(() => ReadDiscCore(driveLetter, ct), ct);
                ApplyReadOutcome(driveLetter, outcome, silent);
            }
            catch (OperationCanceledException)
            {
                // ユーザーキャンセル（またはメディア取り外しに伴う中断）。UI は未読み取り状態に戻す。
                gridTracks.DataSource = null;
                gridAlbum.DataSource = null;
                txtMcn.Text = "";
                _lastRead = null;
                SetDbPanelEnabled(false, _registration is null ? "DB 接続が設定されていません" : "CD を読み込むと有効になります");
                lblSummary.Text = $"Drive {driveLetter}: 読み取りをキャンセルしました";
            }
            catch (Exception ex)
            {
                // silent モード（自動トリガ）ではダイアログを抑止し、ステータスラベルにのみ反映する。
                // CDAnalyzer 単独起動時の手動操作と異なり、BDAnalyzer 側で正常に処理されている可能性が高いため、
                // 余計なダイアログでユーザの BDAnalyzer 操作を邪魔しない。
                if (!silent)
                    MessageBox.Show($"読み取りエラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                else
                    lblSummary.Text = $"読み取りエラー: {ex.Message}";
            }
            finally
            {
                _readCts.Dispose();
                _readCts = null;
                btnLoad.Text = "読み取り";
                cboDrives.Enabled = true;
            }
        }

        /// <summary>デバイス I/O 本体（ワーカースレッドで実行。UI コントロールには一切触れない）。 各 SCSI 操作の区切り（トラック単位の ISRC 読み取り含む）で <paramref name="ct"/> を確認し、 キャンセル時は <see cref="OperationCanceledException"/> で離脱する（ハンドルは using で確実に閉じる）。</summary>
        private static DiscReadOutcome ReadDiscCore(char driveLetter, CancellationToken ct)
        {
            // SCSI パススルー用にデバイスハンドルを開く
            using SafeFileHandle h = OpenCdDevice(driveLetter);

            // --- メディア種別判定---
            // GET CONFIGURATION で Current Profile を取得し、CD 系以外なら早期 return する。
            // 早期 return により using スコープを抜けてハンドルが即座にクローズされ、
            // 同時起動中の BDAnalyzer のファイル I/O との競合を最小化する。
            var (profile, rawProfile) = GetCurrentProfile(h);
            switch (profile)
            {
                case MediaProfile.Cd:
                    // 想定動作: 通常通り TOC 等を読み出す。フォールスルーで後続処理へ進む。
                    break;

                case MediaProfile.Dvd:
                case MediaProfile.BluRay:
                case MediaProfile.HdDvd:
                case MediaProfile.None:
                    // 非 CD メディア / メディア未挿入: 何もせずハンドルを閉じて離脱（UI 反映は呼び出し側）。
                    return new DiscReadOutcome { Profile = profile, RawProfile = rawProfile };

                case MediaProfile.Other:
                default:
                    // 不明プロファイル / GET CONFIGURATION 非対応の旧ドライブ:
                    // 安全側に倒して従来動作にフォールバックする（TOC 読み取りで判定）。
                    break;
            }

            ct.ThrowIfCancellationRequested();

            // --- TOC (Table of Contents): 全トラックの開始 LBA を取得 ---
            var tocAll = ReadToc(h);
            var tracksOnly = tocAll.Where(t => t.TrackNumber != 0xAA).OrderBy(t => t.TrackNumber).ToList();
            if (tracksOnly.Count == 0)
            {
                // TOC 読み取り失敗（オーディオ CD ではない可能性）。空リストで離脱を表現する。
                return new DiscReadOutcome { Profile = profile, RawProfile = rawProfile, Tracks = tracksOnly };
            }
            // Lead-Out (Track 0xAA) の LBA = ディスク末尾。なければ推定値を使用
            int leadOutLba = tocAll.FirstOrDefault(t => t.TrackNumber == 0xAA)?.StartLba
                             ?? (tracksOnly.Last().StartLba + 75 * 60 * 10);

            ct.ThrowIfCancellationRequested();

            // --- MCN (Media Catalog Number): JAN/EAN バーコード相当の 13 桁数字 ---
            string? mcnRaw = ReadMediaCatalogNumber(h);

            // --- ISRC: 各トラックの国際標準レコーディングコード (12 文字) ---
            var isrcMap = new Dictionary<int, string?>();
            // 第 1 パス: 各トラックを SEEK 込みで 1 回ずつ読む（高速にディスク全体の傾向を把握）。
            foreach (var t in tracksOnly)
            {
                ct.ThrowIfCancellationRequested();
                isrcMap[t.TrackNumber] = ReadIsrcForTrack(h, (byte)t.TrackNumber, t.StartLba, 1, 60);
            }

            // ディスクに 1 つでも ISRC が取れたトラックがあれば、そのディスクは ISRC 収録盤と判断し、
            if (isrcMap.Values.Any(v => !string.IsNullOrEmpty(v)))
            {
                foreach (var t in tracksOnly)
                {
                    if (!string.IsNullOrEmpty(isrcMap[t.TrackNumber]))
                        continue;
                    ct.ThrowIfCancellationRequested();
                    isrcMap[t.TrackNumber] = ReadIsrcForTrack(h, (byte)t.TrackNumber, t.StartLba, 5, 120);
                }
            }

            ct.ThrowIfCancellationRequested();

            // --- CD-Text: パック列を読み取り → デコードしてカタログ化 ---
            var packs = ReadCdTextPacks(h);
            var catalog = BuildCdTextCatalog(packs);

            return new DiscReadOutcome
            {
                Profile = profile,
                RawProfile = rawProfile,
                Tracks = tracksOnly,
                LeadOutLba = leadOutLba,
                McnRaw = mcnRaw,
                IsrcMap = isrcMap,
                CdTextPackCount = packs.Count,
                Catalog = catalog
            };
        }

        /// <summary>読み取り結果を UI（グリッド・ラベル・DB 連携パネル・スナップショット）へ反映する。 UI スレッドで呼ぶこと。</summary>
        private void ApplyReadOutcome(char driveLetter, DiscReadOutcome outcome, bool silent)
        {
            // --- 非 CD メディア（DVD / BD / HD DVD）: BDAnalyzer に委ねて静かに離脱 ---
            if (outcome.Profile is MediaProfile.Dvd or MediaProfile.BluRay or MediaProfile.HdDvd)
            {
                string mediaName = outcome.Profile switch
                {
                    MediaProfile.Dvd => "DVD",
                    MediaProfile.BluRay => "Blu-ray",
                    MediaProfile.HdDvd => "HD DVD",
                    _ => "非 CD"
                };
                // UI 状態は読み取り未実施の状態に揃える。
                gridTracks.DataSource = null;
                gridAlbum.DataSource = null;
                txtMcn.Text = "";
                btnCopyTsv.Enabled = false;
                _lastRead = null;
                SetDbPanelEnabled(false,
                    $"Drive {driveLetter}: {mediaName} (Profile 0x{outcome.RawProfile:X4}) を検知 — CDAnalyzer は CD 専用のためスキップしました");
                lblSummary.Text = $"Drive {driveLetter}: {mediaName} を検知したため読み取りをスキップ（BDAnalyzer 側で読み込んでください）";

                if (!silent)
                {
                    // 手動操作時のみダイアログで案内。自動トリガ時は静かに離脱して BDAnalyzer の作業を妨げない。
                    MessageBox.Show(
                        $"挿入されているメディアは {mediaName} (Profile 0x{outcome.RawProfile:X4}) です。\n"
                        + "CDAnalyzer は CD-DA 専用のため、このディスクは読み取りません。\n"
                        + "Blu-ray / DVD のチャプター情報は BDAnalyzer をご利用ください。",
                        "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            // --- メディア未挿入: GET CONFIGURATION で Profile=0x0000 が返るケース ---
            // 通常は DriveInfo.IsReady でも弾かれるが、稀にここに来るので明示的に処理する。
            if (outcome.Profile == MediaProfile.None)
            {
                gridTracks.DataSource = null;
                gridAlbum.DataSource = null;
                txtMcn.Text = "";
                btnCopyTsv.Enabled = false;
                _lastRead = null;
                SetDbPanelEnabled(false, $"Drive {driveLetter}: メディア未挿入");
                lblSummary.Text = $"Drive {driveLetter}: メディアが挿入されていません";
                if (!silent)
                    MessageBox.Show("メディアが挿入されていないか、ドライブが認識できない状態です。",
                        "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // --- TOC 読み取り失敗 ---
            if (outcome.Tracks is null || outcome.Tracks.Count == 0)
            {
                lblSummary.Text = $"Drive {driveLetter}: TOC が読み取れませんでした";
                SetDbPanelEnabled(false, _registration is null ? "DB 接続が設定されていません" : "CD を読み込むと有効になります");
                if (!silent)
                    MessageBox.Show("TOCが読み取れませんでした。オーディオCDか確認してください。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var tracksOnly = outcome.Tracks;
            int leadOutLba = outcome.LeadOutLba;
            var isrcMap = outcome.IsrcMap!;
            var catalog = outcome.Catalog!;

            txtMcn.Text = outcome.McnRaw ?? "—";

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

            lblSummary.Text = $"Drive {driveLetter}: Tracks={tracksOnly.Count}, Lead-Out LBA={leadOutLba}, CD-Text packs={outcome.CdTextPackCount}";
            btnCopyTsv.Enabled = table.Rows.Count > 0;

            // DB 連携パネル用に、読み取り結果をスナップショット保存
            _lastRead = BuildSnapshot(tracksOnly, leadOutLba, outcome.McnRaw, isrcMap, catalog);
            SetDbPanelEnabled(_registration is not null, _registration is null ? "DB 接続が設定されていません" : "照合可能");
        }

        /// <summary>WM_DEVICECHANGE を監視し、メディアの挿入/取り外しに応じて ドライブリストを再構築し、挿入時は自動読み取りを行う。</summary>
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
                            // 自動トリガでは silent=true 指定で LoadAllAsync を呼ぶ（fire-and-forget。
                            // 読み取り実行中なら LoadAllAsync 側の再入抑止で何もしない）。
                            // 非 CD メディア（DVD/BD）が挿入された場合や読み取りエラー時に
                            // メッセージボックスを抑止し、同時起動中の BDAnalyzer の操作を妨げない。
                            if (wparam == DBT_DEVICEARRIVAL) _ = LoadAllAsync(silent: true); // 挿入時は自動読み取り
                            else
                            {
                                // メディア取り外し：実行中の読み取りがあれば中断する（デバイスが消えており
                                // 続行不能。キャンセル経路が UI を未読み取り状態へ戻す）。
                                _readCts?.Cancel();
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

        /// <summary>トラック情報を TSV 形式でクリップボードにコピーする。 出力形式: [空列]\t[Track]\t[Length(frames)] × 行数。</summary>
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

        // ===== DB 連携機能 =====

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
                // CD-DA には「チャプター」概念がないため NumChapters は NULL のまま。
                // TotalLengthMs も BD/DVD 専用のため CD では NULL のまま。
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

        /// <summary>簡易的な freedb 互換 Disc ID を算出する。 仕様: sum(トラック開始秒の各桁合計) % 0xFF を上位 2 桁、総秒数を中央 4 桁、トラック数を下位 2 桁。</summary>
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
                || _productKindsRepo is null || _seriesRepo is null
                || _productCompaniesRepo is null || _lastRead is null)
            {
                return;
            }

            try
            {
                // 1. 自動照合
                var match = await _registration.FindCandidatesForCdAsync(
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
                else if (dlg.WantsAttachToExistingProduct)
                {
                    // 既存商品に追加ディスクとして登録する。
                    // CD でも複数枚組商品（DISC 1 だけ既登録）に DISC 2 を追加するケースで使う。
                    //
                    // フロー:
                    //   1. DiscMatchDialog で対象 BOX のいずれかのディスク（例: Disc 1）を選択しておく
                    //   2. AttachReferenceDisc.ProductCatalogNo から所属商品を引き、所属ディスクも一括取得
                    //   3. ConfirmAttachDialog で商品確認＋シリーズ継承選択＋新ディスクの品番入力（次の品番候補が初期値で入る）
                    //   4. 「追加して登録」確定 → 登録
                    if (dlg.AttachReferenceDisc is null
                        || string.IsNullOrEmpty(dlg.AttachReferenceDisc.ProductCatalogNo))
                    {
                        MessageBox.Show(this, "選択ディスクの所属商品が確認できません。", "エラー",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var product = await _productsRepo.GetByCatalogNoAsync(dlg.AttachReferenceDisc.ProductCatalogNo);
                    if (product is null)
                    {
                        MessageBox.Show(this, $"商品 [{dlg.AttachReferenceDisc.ProductCatalogNo}] が見つかりません。",
                            "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    var existingDiscs = await _discsRepo.GetByProductCatalogNoAsync(product.ProductCatalogNo);

                    using var cdlg = new ConfirmAttachDialog(product, existingDiscs, _seriesRepo);
                    if (cdlg.ShowDialog(this) != DialogResult.OK) return;

                    // 品番は ConfirmAttachDialog 内で入力済み。
                    if (string.IsNullOrWhiteSpace(cdlg.CatalogNo)) return;

                    var disc = _lastRead.Disc;
                    disc.CatalogNo = cdlg.CatalogNo!.Trim();
                    // シリーズはダイアログ側で「継承 / オールスターズ / 任意上書き」を解決済み
                    disc.SeriesId = cdlg.OverrideSeriesId;
                    // 既存ディスクのタイトルを初期値として継承する。
                    if (!string.IsNullOrWhiteSpace(cdlg.InheritedDiscTitle))
                    {
                        disc.Title = cdlg.InheritedDiscTitle;
                    }
                    // 新ディスクの全トラックの CatalogNo を確定させる（既存フローと同じ）
                    foreach (var t in _lastRead.Tracks) t.CatalogNo = disc.CatalogNo;

                    // _registration は DI で受け取った既存インスタンス。Product.disc_count 更新 +
                    await _registration.AttachDiscToExistingProductAsync(
                        product.ProductCatalogNo,
                        disc,
                        _lastRead.Tracks);

                    MessageBox.Show(this,
                        $"既存商品 [{product.ProductCatalogNo}] にディスク [{disc.CatalogNo}] を追加し、" +
                        $"トラック {_lastRead.Tracks.Count} 件を登録しました。\n" +
                        $"商品配下のディスクは品番順に組内番号を再採番済み。",
                        "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (dlg.WantsNewRegistration)
                {
                    // 新規商品作成 → 新規ディスク登録
                    // 品番を先に入力させ、それを代表品番としても使う（CreateProductAndCommitAsync の内部で自動コピー）
                    string? catalogNo = PromptCatalogNo();
                    if (string.IsNullOrWhiteSpace(catalogNo)) return;

                    var initTitle = _lastRead.Disc.CdTextAlbumTitle ?? "";
                    using var pdlg = new NewProductDialog(_productKindsRepo, _seriesRepo, _productCompaniesRepo!, initTitle);
                    if (pdlg.ShowDialog(this) != DialogResult.OK || pdlg.Result is null) return;

                    var disc = _lastRead.Disc;
                    disc.CatalogNo = catalogNo!.Trim();
                    // NewProductDialog で選ばれたシリーズ ID はディスク側の属性として適用する。
                    // 商品 (Product) には series_id を持たせない（series_id 列を持たない）。
                    disc.SeriesId = pdlg.SelectedSeriesId;
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

        /// <summary>品番入力用の簡易プロンプト（新規商品＋ディスクとして登録するフローで使用）。</summary>
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
            var lbl = new Label { Text = "品番 (例: MJSA-01000 / MJSS-09000)", Location = new Point(12, 12), Size = new Size(380, 20) };
            var txt = new TextBox { Location = new Point(12, 36), Size = new Size(380, 23) };
            var ok = new Button { Text = "OK", Location = new Point(226, 68), Size = new Size(80, 28), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "キャンセル", Location = new Point(312, 68), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
        }

        /// <summary>CDAnalyzer の読み取り結果スナップショット。DB 連携時に使用。</summary>
        private sealed record LastReadSnapshot(Disc Disc, List<Track> Tracks);
    }
}
