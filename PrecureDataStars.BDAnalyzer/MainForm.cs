using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.Catalog.Common.Dialogs;
using PrecureDataStars.Catalog.Common.Services;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// Blu-ray (.mpls) / DVD (.IFO) のチャプター情報を解析し、各章の尺と累積時間を一覧表示するフォーム。
    /// <para>
    /// 光学ドライブへのディスク挿入を WM_DEVICECHANGE で検知し、自動ロードする機能を持つ。
    /// 解析結果は TSV 形式でクリップボードにコピーできる（Ctrl+C / ボタン）。
    /// </para>
    /// <remarks>
    /// v1.1.0 以降は DB 連携パネルを持ち、既存ディスクとの照合・新規商品登録が可能。
    /// BD/DVD は CD と異なり ISRC/MCN が取得できないため、照合は
    /// ボリュームラベル + 総尺 + チャプター数 による近似照合となる。
    /// v1.1.1 でチャプター情報を <c>video_chapters</c> テーブルへ一括登録する処理を追加した。
    /// title / part_type / notes は登録時点では NULL で、Catalog GUI 側で後から補完する。
    /// </remarks>
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

        // DB 連携用（DB 無効モードでは null）
        private readonly DiscRegistrationService? _registration;
        private readonly DiscsRepository? _discsRepo;
        private readonly ProductsRepository? _productsRepo;
        private readonly TracksRepository? _tracksRepo;
        private readonly ProductKindsRepository? _productKindsRepo;
        private readonly SeriesRepository? _seriesRepo;
        // v1.1.1 で追加: BD/DVD チャプターの一括登録用リポジトリ
        private readonly VideoChaptersRepository? _videoChaptersRepo;

        // 最後に読み込んだディスクのスナップショット（DB 連携時に使用）
        private LastReadSnapshot? _lastRead;

        /// <summary>DB 連携無効モード（従来互換）コンストラクタ。</summary>
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

            // DB 連携ボタン（非活性からスタート）
            btnDbMatch.Click += btnDbMatch_Click;
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
            VideoChaptersRepository videoChaptersRepo) : this()
        {
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
            _discsRepo = discsRepo ?? throw new ArgumentNullException(nameof(discsRepo));
            _productsRepo = productsRepo ?? throw new ArgumentNullException(nameof(productsRepo));
            _tracksRepo = tracksRepo ?? throw new ArgumentNullException(nameof(tracksRepo));
            _productKindsRepo = productKindsRepo ?? throw new ArgumentNullException(nameof(productKindsRepo));
            _seriesRepo = seriesRepo ?? throw new ArgumentNullException(nameof(seriesRepo));
            _videoChaptersRepo = videoChaptersRepo ?? throw new ArgumentNullException(nameof(videoChaptersRepo));

            SetDbPanelEnabled(false, "ディスクを読み込むと有効になります");
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

            // v1.1.1: video_chapters へ投入するための章データを構築する。
            // DVD のプログラム (≒チャプター) は IFO の PGC に載っている再生時間をそのまま使う。
            // 開始時刻は連続した累積値（プログラム N の長さを積み上げる）として算出する。
            var ifoChapters = new List<(TimeSpan Start, TimeSpan Duration)>();
            TimeSpan ifoStart = TimeSpan.Zero;
            for (int p = startIndex; p < result.ProgramDurations.Count; p++)
            {
                var dur = result.ProgramDurations[p];
                ifoChapters.Add((ifoStart, dur));
                ifoStart += dur;
            }

            // DB 連携用スナップショットを作成（DVD 側）
            _lastRead = BuildSnapshot(
                path,
                mediaFormat: "DVD",
                chapterCount: result.ProgramDurations.Count,
                totalLength: accum,
                sourceKind: "IFO",
                chapterTimings: ifoChapters);
            SetDbPanelEnabled(_registration is not null, _registration is null ? "DB 接続が設定されていません" : "照合可能");
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

            // v1.1.1: video_chapters へ投入するための章データを構築する。
            // MPLS の Chapter は Start (PlayList 先頭からの位置) と Length を保持しているのでそのまま流用。
            var mplsChapters = new List<(TimeSpan Start, TimeSpan Duration)>();
            foreach (var ch in r.Chapters)
            {
                mplsChapters.Add((ch.Start, ch.Length));
            }

            // DB 連携用スナップショットを作成（BD 側）
            _lastRead = BuildSnapshot(
                path,
                mediaFormat: "BD",
                chapterCount: r.Chapters.Count,
                totalLength: r.PlaylistDuration,
                sourceKind: "MPLS",
                chapterTimings: mplsChapters);
            SetDbPanelEnabled(_registration is not null, _registration is null ? "DB 接続が設定されていません" : "照合可能");
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
                    Path.Combine(root, "BDMV", "PLAYLIST", "00000.mpls"),
                    Path.Combine(root, "BDMV", "PLAYLIST", "00001.mpls"),
                };
                foreach (var c in bdCandidates)
                {
                    if (File.Exists(c)) { foundPath = c; return true; }
                }

                // --- DVD をフォールバック（VTS_01_0.IFO → VIDEO_TS.IFO）---
                string[] dvdCandidates =
                {
                    Path.Combine(root, "VIDEO_TS", "VTS_01_0.IFO"),
                    Path.Combine(root, "VIDEO_TS", "VIDEO_TS.IFO"),
                };
                foreach (var c in dvdCandidates)
                {
                    if (File.Exists(c)) { foundPath = c; return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// 「Load DISC」ボタンクリック時に光学ドライブを探索し、見つかれば自動ロードする。
        /// </summary>
        private void btnLoadDefault_Click(object? sender, EventArgs e)
        {
            if (TryFindDiscFile(out var path))
            {
                LoadPath(path);
            }
            else
            {
                MessageBox.Show(this, "光学ドライブに BD/DVD が見つかりませんでした。", "Load DISC",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>
        /// WM_DEVICECHANGE を捕捉し、ディスク挿入時には短時間のデバウンス後に自動ロードを試みる。
        /// 取り外し時は一覧をクリアする。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int wparam = m.WParam.ToInt32();
                if (wparam == DBT_DEVICEARRIVAL && !_autoLoading)
                {
                    // 500ms 以内の連続イベントは 1 回にまとめる
                    var now = DateTime.UtcNow;
                    if ((now - _lastAutoLoad).TotalMilliseconds > 500)
                    {
                        _lastAutoLoad = now;
                        _autoLoading = true;
                        try
                        {
                            if (TryFindDiscFile(out var path)) LoadPath(path);
                        }
                        finally { _autoLoading = false; }
                    }
                }
                else if (wparam == DBT_DEVICEREMOVECOMPLETE)
                {
                    listView.Items.Clear();
                    lblInfo.Text = "";
                    SetDbPanelEnabled(false, "ディスクが取り外されました");
                    _lastRead = null;
                }
            }
            base.WndProc(ref m);
        }

        // ===== DB 連携（v1.1.0 追加） =====

        /// <summary>DB 連携パネルの活性状態を切り替える。</summary>
        private void SetDbPanelEnabled(bool enabled, string status)
        {
            btnDbMatch.Enabled = enabled;
            lblDbStatus.Text = status;
        }

        /// <summary>
        /// 読み取り結果から DB 連携用のディスクスナップショットを組み立てる。
        /// BD/DVD では物理トラックは空のまま、代わりに video_chapters 用の章リストを組み立てる。
        /// </summary>
        /// <param name="path">解析対象のディスクファイルパス（ボリュームラベル取得に使用）。</param>
        /// <param name="mediaFormat">"BD" または "DVD"。</param>
        /// <param name="chapterCount">チャプター総数。</param>
        /// <param name="totalLength">総再生時間。</param>
        /// <param name="sourceKind">video_chapters.source_kind に格納する値（"MPLS" または "IFO"）。</param>
        /// <param name="chapterTimings">各章の (開始時刻, 尺) のリスト。index 0 がチャプター 1 に相当。</param>
        private LastReadSnapshot BuildSnapshot(
            string path, string mediaFormat, int chapterCount, TimeSpan totalLength,
            string sourceKind, IReadOnlyList<(TimeSpan Start, TimeSpan Duration)> chapterTimings)
        {
            // ディスクルートのボリュームラベルを取得（商品タイトル特定に役立つ）
            string? volumeLabel = null;
            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root))
                {
                    var di = new DriveInfo(root);
                    if (di.IsReady) volumeLabel = di.VolumeLabel;
                }
            }
            catch { /* ラベル取得失敗は許容 */ }

            // BD/DVD の尺を CD-DA と同じく「75 分の 1 秒 = 1 フレーム」単位に換算して統一格納
            // (本来 BD/DVD のフレームは 90kHz クロックだが、DB 横断での比較のため CD-DA 基準に合わせる)
            uint totalFrames = (uint)Math.Max(0L, (long)(totalLength.TotalSeconds * 75.0));

            var disc = new Disc
            {
                CatalogNo = "",       // 登録時に入力
                ProductCatalogNo = "", // 登録時に確定（新規は catalog_no と同値、既存反映は対象ディスクから引き継ぎ）
                MediaFormat = mediaFormat,
                VolumeLabel = volumeLabel,
                NumChapters = (ushort)Math.Min(chapterCount, ushort.MaxValue),
                TotalLengthFrames = totalFrames,
                LastReadAt = DateTime.Now,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };

            // v1.1.1: 読み取った章を video_chapters テーブル用エンティティに変換する。
            // CatalogNo は登録時（btnDbMatch_Click 内）で確定するためここでは未設定。
            // title / part_type / notes は Catalog GUI で後から補完する想定で NULL のまま。
            string playlistFile = Path.GetFileName(path);
            var videoChapters = new List<VideoChapter>(chapterTimings.Count);
            for (int i = 0; i < chapterTimings.Count; i++)
            {
                var (start, dur) = chapterTimings[i];
                videoChapters.Add(new VideoChapter
                {
                    CatalogNo = "", // 登録時に disc.CatalogNo で埋める
                    ChapterNo = (ushort)(i + 1),
                    Title = null,
                    PartType = null,
                    StartTimeMs = (ulong)Math.Max(0L, (long)start.TotalMilliseconds),
                    DurationMs = (ulong)Math.Max(0L, (long)dur.TotalMilliseconds),
                    PlaylistFile = playlistFile,
                    SourceKind = sourceKind,
                    Notes = null,
                    CreatedBy = Environment.UserName,
                    UpdatedBy = Environment.UserName
                });
            }

            // Track は BD/DVD では使わないので常に空リストで返す。
            return new LastReadSnapshot(disc, new List<Track>(), videoChapters);
        }

        /// <summary>
        /// DB 連携ボタン：既存ディスク照合 → 反映 or 新規登録のフロー起点。
        /// </summary>
        private async void btnDbMatch_Click(object? sender, EventArgs e)
        {
            if (_registration is null || _discsRepo is null || _productsRepo is null
                || _productKindsRepo is null || _seriesRepo is null || _lastRead is null)
            {
                return;
            }

            try
            {
                // BD/DVD は MCN/CDDB が取れないため、TOC 曖昧（ここではチャプター数＋総尺）でのみ照合
                // ※ DiscRegistrationService は totalTracks と totalLengthFrames で曖昧照合するため、
                //   BD/DVD の場合はチャプター数を totalTracks にマップして呼ぶ。
                //   NumChapters は ushort? のため、int に統一してから Math.Min の曖昧解決を避ける。
                int chapterCountInt = _lastRead.Disc.NumChapters ?? 0;
                byte chapterCountAsTracks = (byte)Math.Min(chapterCountInt, (int)byte.MaxValue);
                var match = await _registration.FindCandidatesAsync(
                    mcn: null,
                    cddbDiscId: null,
                    totalTracks: chapterCountAsTracks,
                    totalLengthFrames: _lastRead.Disc.TotalLengthFrames ?? 0);

                using var dlg = new DiscMatchDialog(_discsRepo, match.Candidates, match.MatchedBy);
                var result = dlg.ShowDialog(this);
                if (result != DialogResult.OK) return;

                if (dlg.SelectedDisc is not null)
                {
                    // 既存ディスクに反映：物理情報のみ同期する。
                    // BD/DVD は CD のような CD-Text / MCN / CDDB-ID は無いため、実質的に更新されるのは
                    // total_tracks / total_length_frames / num_chapters / volume_label / last_read_at のみ。
                    // タイトル・disc_kind_code・product_catalog_no・notes 等 Catalog で磨いた情報は保全される。
                    var disc = _lastRead.Disc;
                    disc.CatalogNo = dlg.SelectedDisc.CatalogNo;

                    // discs の物理情報を同期（タイトル等はそのまま）
                    await _discsRepo.UpsertPhysicalInfoAsync(disc);

                    // v1.1.1: video_chapters を「全削除 → 新章リストで置換」で更新する。
                    //   タイトル・part_type・notes は再読み取り時に失われるが、これは
                    //   「再読み取りは物理境界の更新が目的」という運用前提のため許容する。
                    //   保全したい場合は Catalog GUI 側で事前にエクスポート／戻す設計も可能。
                    if (_videoChaptersRepo is not null && _lastRead.VideoChapters.Count > 0)
                    {
                        foreach (var vc in _lastRead.VideoChapters) vc.CatalogNo = disc.CatalogNo;
                        await _videoChaptersRepo.ReplaceAllForDiscAsync(disc.CatalogNo, _lastRead.VideoChapters);
                    }

                    MessageBox.Show(this,
                        $"ディスク [{disc.CatalogNo}] に BD/DVD 物理情報とチャプター {_lastRead.VideoChapters.Count} 件を反映しました。\n"
                        + "（タイトル・種別等の Catalog 情報は保全されます）",
                        "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (dlg.WantsNewRegistration)
                {
                    // 新規商品作成 → 新規ディスク登録
                    // 品番を先に入力してもらい、それを代表品番にも使う
                    string? catalogNo = PromptCatalogNo();
                    if (string.IsNullOrWhiteSpace(catalogNo)) return;

                    var initTitle = _lastRead.Disc.VolumeLabel ?? "";
                    using var pdlg = new NewProductDialog(_productKindsRepo, _seriesRepo, initTitle);
                    if (pdlg.ShowDialog(this) != DialogResult.OK || pdlg.Result is null) return;

                    var disc = _lastRead.Disc;
                    disc.CatalogNo = catalogNo!.Trim();

                    // 代表品番 = このディスクの品番 として商品を作成
                    var product = pdlg.Result;
                    product.ProductCatalogNo = disc.CatalogNo;
                    disc.ProductCatalogNo    = disc.CatalogNo;

                    // 新規登録は全列 INSERT が正しい挙動（保全対象の既存データがない）。
                    await _productsRepo.InsertAsync(product);
                    await _discsRepo.UpsertAsync(disc);

                    // v1.1.1: チャプターも一括登録する（新規なので CatalogNo を埋めるだけ）
                    if (_videoChaptersRepo is not null && _lastRead.VideoChapters.Count > 0)
                    {
                        foreach (var vc in _lastRead.VideoChapters) vc.CatalogNo = disc.CatalogNo;
                        await _videoChaptersRepo.ReplaceAllForDiscAsync(disc.CatalogNo, _lastRead.VideoChapters);
                    }

                    MessageBox.Show(this,
                        $"新規商品 [{disc.CatalogNo}] とディスクを作成し、チャプター {_lastRead.VideoChapters.Count} 件を登録しました。\n"
                        + "チャプターのタイトル・パート種別は video_chapters テーブル上で NULL なので、Catalog GUI から補完してください。",
                        "完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "DB 連携エラー: " + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>品番入力用の簡易プロンプト。</summary>
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
            var lbl = new Label { Text = "品番 (例: PCXE-12345)", Location = new Point(12, 12), Size = new Size(380, 20) };
            var txt = new TextBox { Location = new Point(12, 36), Size = new Size(380, 23) };
            var ok = new Button { Text = "OK", Location = new Point(226, 68), Size = new Size(80, 28), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "キャンセル", Location = new Point(312, 68), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            return f.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
        }

        /// <summary>
        /// BDAnalyzer の読み取り結果スナップショット。
        /// DB 連携時はディスク本体に加えて、BD/DVD のチャプター一覧を <see cref="VideoChapters"/> に保持する。
        /// CD 時代の名残で <see cref="Tracks"/> を持つが、BDAnalyzer 経由では常に空。
        /// </summary>
        private sealed record LastReadSnapshot(Disc Disc, List<Track> Tracks, List<VideoChapter> VideoChapters);
    }
}