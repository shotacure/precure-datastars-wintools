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

        /// <summary>
        /// ListView の Checked プロパティをコード側から一括設定する際、
        /// <see cref="listView_ItemChecked"/> による連動ロジックの再帰発火を抑制するためのフラグ。
        /// ListView アイテム投入時やプログラム的な一括更新の周辺で true にし、終わったら false に戻す。
        /// </summary>
        private bool _suppressItemCheckCascade = false;

        /// <summary>DB 連携無効モード（従来互換）コンストラクタ。</summary>
        public MainForm()
        {
            InitializeComponent();

            // v1.1.1: タイトル/チャプター行のチェックで登録対象を絞り込めるようにする。
            listView.CheckBoxes = true;
            listView.ItemChecked += listView_ItemChecked;

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
        /// DVD の IFO を解析してチャプター尺を一覧表示する。
        /// <para>
        /// v1.1.1 でフォルダ全走査モードを追加した。入力パスのファイル名によって処理を分岐する:
        /// </para>
        /// <list type="bullet">
        ///   <item>
        ///     <b>VIDEO_TS.IFO</b>: 同フォルダ内の全 VTS_xx_0.IFO を自動走査し、各 VTS の最長 PGC を
        ///     その VTS のタイトル本編として抽出する（複数話収録 DVD の推奨パス）。ダミー VTS・
        ///     ゼロ尺チャプター・境界の極短チャプターはフィルタで除外する。
        ///   </item>
        ///   <item>
        ///     <b>VTS_xx_0.IFO</b> 単体: 従来通りその VTS の先頭 PGC のみを解析する
        ///     （個別 VTS を明示的に確認したいケース向けの単一 VTS モード）。
        ///   </item>
        /// </list>
        /// </summary>
        /// <param name="path">IFO ファイルパス。</param>
        private void LoadIfo(string path)
        {
            _currentFilePath = path;
            listView.Items.Clear();

            if (!File.Exists(path))
                throw new FileNotFoundException("ファイルが見つかりません。", path);

            var name = Path.GetFileName(path) ?? path;
            if (string.Equals(name, "VIDEO_TS.IFO", StringComparison.OrdinalIgnoreCase))
            {
                // v1.1.1: VIDEO_TS.IFO 指定時はフォルダ全走査モード
                LoadIfoFolderScan(path);
                return;
            }

            // 単一 VTS モード（従来互換）: 指定された VTS の先頭 PGC のみを表示・登録
            LoadIfoSingleVts(path);
        }

        /// <summary>
        /// VIDEO_TS フォルダを全走査し、各 VTS の最長 PGC を代表タイトルとして
        /// 抽出・表示・DB 連携スナップショットにまとめる（v1.1.1 追加）。
        /// </summary>
        /// <param name="videoTsIfoPath">VIDEO_TS.IFO のフルパス（このファイル自体は目次のため
        /// 解析対象にはしないが、親フォルダの位置特定に使う）。</param>
        private void LoadIfoFolderScan(string videoTsIfoPath)
        {
            string? videoTsFolder = Path.GetDirectoryName(videoTsIfoPath);
            if (string.IsNullOrEmpty(videoTsFolder))
            {
                MessageBox.Show(this, "VIDEO_TS フォルダを特定できません。", "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            IfoParser.TitleScanResult scan;
            try
            {
                scan = IfoParser.ExtractTitlesFromVideoTs(videoTsFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "VIDEO_TS の走査に失敗しました: " + ex.Message, "エラー",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (scan.Titles.Count == 0)
            {
                MessageBox.Show(this,
                    "有効なタイトルが見つかりませんでした（全 VTS がダミーと判定されたか、IFO 構造を解釈できませんでした）。",
                    "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // ListView に階層表示する:
            //   タイトル行: "[VTS_02] Title"（尺は各タイトルの合計）
            //   チャプター行: "  1" "  2" … （インデント付き）
            // チャプターの「累積時間」はタイトル内の相対時間として表示する（タイトルごとにリセット）。
            // v1.1.1: 各行には ListRowInfo を Tag として付与し、以下を表現する:
            //   - Title 行: チェックを切り替えると配下のチャプター行と連動
            //   - Chapter 行: VideoChapter への参照を後で埋め込む（登録時フィルタに使用）
            //   - Summary (除外) 行: チェックはユーザー操作対象外
            // v1.1.3: 既定のチェック方針を変更。総尺最大のタイトル（同尺なら先頭優先）とそのチャプターのみ
            //   既定でチェックし、他のタイトル・チャプターは未チェックで提示する。
            //   DVD は本編タイトル 1 個 + メニュー / 特典の短尺タイトル多数という構成が大半で、
            //   従来の「全タイトル既定チェック」では本編以外を 1 つずつ外す手間が大きかったため。
            //   ユーザーは必要に応じて未チェック行を選び直す。
            var allChapters = new List<(TimeSpan Start, TimeSpan Duration, string PlaylistTag)>();
            int globalChapterNo = 0;
            TimeSpan longestTitleDuration = TimeSpan.Zero;
            TimeSpan totalOfAllTitles = TimeSpan.Zero;

            // 総尺最大タイトルの index を事前に特定する。同尺なら先頭優先（厳密大なりで更新）。
            // scan.Titles が空の場合は -1 のまま残し、ループ内で誰もチェックされないようにする。
            int defaultCheckedTitleIndex = -1;
            {
                TimeSpan bestDuration = TimeSpan.MinValue;
                for (int i = 0; i < scan.Titles.Count; i++)
                {
                    if (scan.Titles[i].TotalDuration > bestDuration)
                    {
                        bestDuration = scan.Titles[i].TotalDuration;
                        defaultCheckedTitleIndex = i;
                    }
                }
            }

            // 連動処理の再入抑制フラグを立てて、初期チェック付与時にハンドラを静かにさせる
            _suppressItemCheckCascade = true;

            for (int titleIdx = 0; titleIdx < scan.Titles.Count; titleIdx++)
            {
                var t = scan.Titles[titleIdx];
                bool isDefaultChecked = (titleIdx == defaultCheckedTitleIndex);

                // タイトルヘッダ行
                var header = new ListViewItem($"[{t.VtsTag}]");
                header.SubItems.Add(FormatTs(t.TotalDuration));
                header.SubItems.Add(""); // 累積欄は空
                header.SubItems.Add(Math.Ceiling(t.TotalDuration.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                header.BackColor = System.Drawing.SystemColors.ControlLight;
                header.Tag = new ListRowInfo { Kind = ListRowKind.Title, PlaylistTag = t.VtsTag };
                // v1.1.3: 総尺最大タイトル以外は未チェックで開始する
                header.Checked = isDefaultChecked;
                listView.Items.Add(header);

                TimeSpan accumInTitle = TimeSpan.Zero;
                for (int i = 0; i < t.ChapterDurations.Count; i++)
                {
                    var dur = t.ChapterDurations[i];
                    // video_chapters.start_time_ms はタイトル先頭からの相対時刻（= accumInTitle）
                    allChapters.Add((accumInTitle, dur, t.VtsTag));
                    accumInTitle += dur;
                    globalChapterNo++;

                    var chap = new ListViewItem($"    {i + 1}"); // 2 段インデント
                    chap.SubItems.Add(FormatTs(dur));
                    chap.SubItems.Add(FormatTs(accumInTitle));
                    chap.SubItems.Add(Math.Ceiling(dur.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                    chap.Tag = new ListRowInfo { Kind = ListRowKind.Chapter, PlaylistTag = t.VtsTag };
                    // v1.1.3: 配下チャプターも親タイトルのチェック状態に揃える（連動ロジックと整合）
                    chap.Checked = isDefaultChecked;
                    listView.Items.Add(chap);
                }

                if (t.TotalDuration > longestTitleDuration) longestTitleDuration = t.TotalDuration;
                totalOfAllTitles += t.TotalDuration;
            }

            // 除外件数のサマリ行（末尾）
            // v1.1.1: フィルタ 4（VMGI モードのタイトル重複）のカウントも含める
            int excludedTotal = scan.ExcludedVtsCount + scan.ExcludedZeroChapterCount
                              + scan.ExcludedBoundaryShortCount + scan.DuplicateTitlesRemoved;
            if (excludedTotal > 0)
            {
                var sep = new ListViewItem("除外");
                sep.SubItems.Add("");
                sep.SubItems.Add($"VTS {scan.ExcludedVtsCount} / 0ms {scan.ExcludedZeroChapterCount} / 境界極短 {scan.ExcludedBoundaryShortCount} / 重複 {scan.DuplicateTitlesRemoved}");
                sep.SubItems.Add(excludedTotal.ToString(CultureInfo.InvariantCulture));
                sep.ForeColor = System.Drawing.SystemColors.GrayText;
                // Summary 行のチェックは用途がないので外した状態で置く。ハンドラ側で再クリックも無視する。
                sep.Tag = new ListRowInfo { Kind = ListRowKind.Summary, PlaylistTag = "" };
                sep.Checked = false;
                listView.Items.Add(sep);
            }

            _suppressItemCheckCascade = false;

            // v1.1.1: 集約総尺の算出ロジック（VMGI モード / Per-VTS モード 共通）
            //   - VMGI モードでは titles は論理タイトル単位なので、通常は sum が正しい
            //     （ディスクが複数話独立収録でも、ハードリンク共有でも、VMGI が正しく
            //      1 論理タイトルか複数論理タイトルかを区別してくれるため）
            //   - ただし、VMGI が複数タイトル返しかつ VOB がハードリンクされている場合（同じ実データを
            //     別角度で複数回見せているナビゲーション）は max を採用して水増しを避ける
            //   - Per-VTS フォールバックモードでは、ハードリンクが検出されている場合は max
            //     （物理 VTS が重複なので）、されていなければ sum（真に独立した多話収録）。
            //   統一ルール: 「タイトルが複数 かつ ハードリンク検出」のときだけ max、それ以外は sum。
            TimeSpan aggregatedTotal;
            if (scan.Titles.Count > 1 && scan.VobsHardlinked)
            {
                aggregatedTotal = longestTitleDuration;
            }
            else
            {
                aggregatedTotal = totalOfAllTitles;
            }

            string modeLabel = scan.ScanMode == "VMGI" ? "VMGI" : "per-VTS";
            string hardlinkLabel = scan.VobsHardlinked ? "hardlinked" : "non-linked";
            lblInfo.Text = $"{Path.GetFileName(videoTsIfoPath)} - (DVD {modeLabel}, {hardlinkLabel}) Titles: {scan.Titles.Count}   Chapters: {globalChapterNo}   Aggregated: {FormatTs(aggregatedTotal)}";

            _lastRead = BuildSnapshot(
                videoTsIfoPath,
                mediaFormat: "DVD",
                chapterCount: globalChapterNo,
                totalLength: aggregatedTotal,
                sourceKind: "IFO",
                chapterTimings: allChapters);

            // v1.1.1: 登録時の再集計に備えてハードリンク検出結果を保持する
            _lastScanVobsHardlinked = scan.VobsHardlinked;

            // v1.1.1: 各チャプター行の ListRowInfo に、生成された VideoChapter への参照を紐付ける。
            // BuildSnapshot の VideoChapter 生成順は allChapters と完全一致するので、
            // 型 Chapter の行を先頭から順に歩けば index がそのまま対応する。
            int vcIndex = 0;
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Tag is ListRowInfo info && info.Kind == ListRowKind.Chapter)
                {
                    if (vcIndex < _lastRead.VideoChapters.Count)
                    {
                        info.Chapter = _lastRead.VideoChapters[vcIndex];
                        vcIndex++;
                    }
                }
            }

            SetDbPanelEnabled(_registration is not null, _registration is null ? "DB 接続が設定されていません" : "照合可能");
        }

        /// <summary>
        /// 単一 VTS モード: 指定された VTS_xx_0.IFO の先頭 PGC だけを解析して表示する
        /// （従来互換、個別 VTS 確認用）。
        /// </summary>
        private void LoadIfoSingleVts(string path)
        {
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

            _suppressItemCheckCascade = true;
            foreach (var row in rows)
            {
                var lvi = new ListViewItem(row.label);
                lvi.SubItems.Add(FormatTs(row.len));
                lvi.SubItems.Add(FormatTs(row.accum));
                lvi.SubItems.Add(Math.Ceiling(row.len.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                // v1.1.1: 単一 VTS モードでもチェックボックスを統一的に有効化（既定チェック）
                lvi.Tag = new ListRowInfo { Kind = ListRowKind.Chapter, PlaylistTag = Path.GetFileName(path) };
                lvi.Checked = true;
                listView.Items.Add(lvi);
            }
            _suppressItemCheckCascade = false;

            lblInfo.Text = $"{Path.GetFileName(path)} - (DVD) Programs: {result.ProgramDurations.Count}   Cells: {result.CellDurations.Count}";

            // v1.1.1: video_chapters へ投入するための章データを構築する。
            // DVD のプログラム (≒チャプター) は IFO の PGC に載っている再生時間をそのまま使う。
            // 開始時刻は連続した累積値（プログラム N の長さを積み上げる）として算出する。
            // PlaylistTag は "VTS_02_0.IFO" のようなファイル名をそのまま使う（単一 VTS モードの従来挙動）。
            string singleVtsTag = Path.GetFileName(path);
            var ifoChapters = new List<(TimeSpan Start, TimeSpan Duration, string PlaylistTag)>();
            TimeSpan ifoStart = TimeSpan.Zero;
            for (int p = startIndex; p < result.ProgramDurations.Count; p++)
            {
                var dur = result.ProgramDurations[p];
                ifoChapters.Add((ifoStart, dur, singleVtsTag));
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

            // v1.1.1: 単一 VTS モードではタイトルは実質 1 個なので、ハードリンク判定は常に false
            //         （集計は常に sum = 総尺でよい）
            _lastScanVobsHardlinked = false;

            // v1.1.1: 各チャプター行に VideoChapter 参照を紐付け（登録時フィルタ用）
            int singleVcIndex = 0;
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Tag is ListRowInfo info && info.Kind == ListRowKind.Chapter)
                {
                    if (singleVcIndex < _lastRead.VideoChapters.Count)
                    {
                        info.Chapter = _lastRead.VideoChapters[singleVcIndex];
                        singleVcIndex++;
                    }
                }
            }

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
            _suppressItemCheckCascade = true;
            string mplsPlaylistTagForRows = Path.GetFileName(path);
            for (int i = 0; i < r.Chapters.Count; i++)
            {
                var ch = r.Chapters[i];
                accum += ch.Length;

                var lvi = new ListViewItem((i + 1).ToString());
                lvi.SubItems.Add(FormatTs(ch.Length));
                lvi.SubItems.Add(FormatTs(accum));
                lvi.SubItems.Add(ch.SecondsRounded.ToString(CultureInfo.InvariantCulture));
                // v1.1.1: BD の場合も統一的にチェックボックス有効（既定チェック）
                lvi.Tag = new ListRowInfo { Kind = ListRowKind.Chapter, PlaylistTag = mplsPlaylistTagForRows };
                lvi.Checked = true;
                listView.Items.Add(lvi);
            }
            _suppressItemCheckCascade = false;

            lblInfo.Text = $"{Path.GetFileName(path)} - (Blu-ray) Items: {r.PlayItemCount}   Marks: {r.MarkCount}   Duration: {FormatTs(r.PlaylistDuration)}";

            // v1.1.1: video_chapters へ投入するための章データを構築する。
            // MPLS の Chapter は Start (PlayList 先頭からの位置) と Length を保持しているのでそのまま流用。
            // PlaylistTag は playlist_file に入れる .mpls ファイル名。
            string mplsPlaylistTag = Path.GetFileName(path);
            var mplsChapters = new List<(TimeSpan Start, TimeSpan Duration, string PlaylistTag)>();
            foreach (var ch in r.Chapters)
            {
                mplsChapters.Add((ch.Start, ch.Length, mplsPlaylistTag));
            }

            // DB 連携用スナップショットを作成（BD 側）
            _lastRead = BuildSnapshot(
                path,
                mediaFormat: "BD",
                chapterCount: r.Chapters.Count,
                totalLength: r.PlaylistDuration,
                sourceKind: "MPLS",
                chapterTimings: mplsChapters);

            // v1.1.1: BD (.mpls) 読み取りではハードリンクの概念が無いので常に false
            _lastScanVobsHardlinked = false;

            // v1.1.1: 各チャプター行に VideoChapter 参照を紐付け（登録時フィルタ用）
            int mplsVcIndex = 0;
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Tag is ListRowInfo info && info.Kind == ListRowKind.Chapter)
                {
                    if (mplsVcIndex < _lastRead.VideoChapters.Count)
                    {
                        info.Chapter = _lastRead.VideoChapters[mplsVcIndex];
                        mplsVcIndex++;
                    }
                }
            }

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

                // --- DVD をフォールバック ---
                // v1.1.1: VIDEO_TS.IFO を優先する（フォルダ全走査で多話 DVD に対応するため）。
                //         VIDEO_TS.IFO が無い稀なケースのみ VTS_01_0.IFO にフォールバック。
                //         v1.1.0 までは逆順で、VTS_01（ダミー）を掴むと 400ms のゴミしか読めないケースがあった。
                string[] dvdCandidates =
                {
                    Path.Combine(root, "VIDEO_TS", "VIDEO_TS.IFO"),
                    Path.Combine(root, "VIDEO_TS", "VTS_01_0.IFO"),
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
        /// <param name="chapterCount">チャプター総数（入力チェック用、実際の登録件数は chapterTimings の件数）。</param>
        /// <param name="totalLength">総再生時間。</param>
        /// <param name="sourceKind">video_chapters.source_kind に格納する値（"MPLS" または "IFO"）。</param>
        /// <param name="chapterTimings">
        /// 各章の (開始時刻, 尺, プレイリストタグ) のリスト。index 0 がチャプター 1 に相当。
        /// プレイリストタグは <c>video_chapters.playlist_file</c> にそのまま格納される文字列で、
        /// DVD 複数 VTS 走査時はタイトルごとに "VTS_02" などの識別子を使い分ける。
        /// </param>
        private LastReadSnapshot BuildSnapshot(
            string path, string mediaFormat, int chapterCount, TimeSpan totalLength,
            string sourceKind, IReadOnlyList<(TimeSpan Start, TimeSpan Duration, string PlaylistTag)> chapterTimings)
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

            // v1.1.1: BD/DVD の総尺は discs.total_length_ms （ミリ秒）で保持する。
            //         v1.1.0 までは CD-DA の 1/75秒フレームに換算して total_length_frames に格納していたが、
            //         BD/DVD 本来の精度 (ms) を失うため、v1.1.1 で専用列 total_length_ms を新設した。
            ulong totalMs = (ulong)Math.Max(0L, (long)totalLength.TotalMilliseconds);

            var disc = new Disc
            {
                CatalogNo = "",       // 登録時に入力
                ProductCatalogNo = "", // 登録時に確定（新規は catalog_no と同値、既存反映は対象ディスクから引き継ぎ）
                MediaFormat = mediaFormat,
                VolumeLabel = volumeLabel,
                NumChapters = (ushort)Math.Min(chapterCount, ushort.MaxValue),
                TotalLengthMs = totalMs,
                // v1.1.1: TotalTracks / TotalLengthFrames は CD-DA 専用のため BD/DVD では NULL のまま。
                LastReadAt = DateTime.Now,
                CreatedBy = Environment.UserName,
                UpdatedBy = Environment.UserName
            };

            // v1.1.1: 読み取った章を video_chapters テーブル用エンティティに変換する。
            // CatalogNo は登録時（btnDbMatch_Click 内）で確定するためここでは未設定。
            // title / part_type / notes は Catalog GUI で後から補完する想定で NULL のまま。
            // chapter_no は PK 制約のため全章通し番号（1..N）でユニークに振る。
            // playlist_file は呼び出し側が渡したタグをそのまま格納する（DVD 複数 VTS 時は "VTS_02" 等）。
            var videoChapters = new List<VideoChapter>(chapterTimings.Count);
            for (int i = 0; i < chapterTimings.Count; i++)
            {
                var (start, dur, tag) = chapterTimings[i];
                videoChapters.Add(new VideoChapter
                {
                    CatalogNo = "", // 登録時に disc.CatalogNo で埋める
                    ChapterNo = (ushort)(i + 1),
                    Title = null,
                    PartType = null,
                    StartTimeMs = (ulong)Math.Max(0L, (long)start.TotalMilliseconds),
                    DurationMs = (ulong)Math.Max(0L, (long)dur.TotalMilliseconds),
                    PlaylistFile = tag,
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
        /// ListView 行の種別。v1.1.1 追加。
        /// </summary>
        private enum ListRowKind
        {
            /// <summary>タイトル見出し行（折りたたみ的なグループヘッダ）。</summary>
            Title,
            /// <summary>個別チャプター行（登録時に VideoChapter として DB 投入される候補）。</summary>
            Chapter,
            /// <summary>「除外」行などの情報表示専用行。チェックボックスは無効扱い。</summary>
            Summary,
        }

        /// <summary>
        /// ListView 各行のメタ情報（Tag に格納）。v1.1.1 追加。
        /// <see cref="VideoChapter"/> 参照は LoadIfoFolderScan / LoadIfoSingleVts / LoadMpls の末尾で埋める。
        /// </summary>
        private sealed class ListRowInfo
        {
            /// <summary>行の種別。</summary>
            public ListRowKind Kind { get; init; }
            /// <summary>所属プレイリスト/タイトルの識別子（"Title_01" / "VTS_02" / "00000.mpls" 等）。
            /// 同じタイトルに属する Title 行と Chapter 行の連動判定に使う。</summary>
            public string PlaylistTag { get; init; } = "";
            /// <summary>Chapter 行のみ: 対応する <see cref="VideoChapter"/>（登録時の絞り込みに使用）。</summary>
            public VideoChapter? Chapter { get; set; }
        }

        /// <summary>
        /// ListView のチェック連動ハンドラ。v1.1.1 追加。
        /// <list type="bullet">
        ///   <item>Title 行のチェックを変更 → 配下の Chapter 行すべてに同じ値を波及</item>
        ///   <item>Chapter 行のチェックを変更 → 親 Title 行のチェックを「配下いずれかがチェックされているか」の OR で更新</item>
        ///   <item>Summary 行はチェック無効。クリックされても常に false に戻す</item>
        /// </list>
        /// <see cref="_suppressItemCheckCascade"/> フラグで再帰発火を抑制する。
        /// </summary>
        private void listView_ItemChecked(object? sender, ItemCheckedEventArgs e)
        {
            if (_suppressItemCheckCascade) return;
            if (e.Item?.Tag is not ListRowInfo info) return;

            _suppressItemCheckCascade = true;
            try
            {
                if (info.Kind == ListRowKind.Summary)
                {
                    // 除外行はチェック意味がないため、常に false に戻す
                    if (e.Item.Checked) e.Item.Checked = false;
                    return;
                }

                int idx = e.Item.Index;
                bool newState = e.Item.Checked;

                if (info.Kind == ListRowKind.Title)
                {
                    // 配下の Chapter 行を同じ値にそろえる（同じ PlaylistTag を持つ Chapter 行を後方走査）
                    for (int i = idx + 1; i < listView.Items.Count; i++)
                    {
                        if (listView.Items[i].Tag is not ListRowInfo child) break;
                        if (child.Kind != ListRowKind.Chapter) break;
                        if (!string.Equals(child.PlaylistTag, info.PlaylistTag, StringComparison.Ordinal)) break;
                        if (listView.Items[i].Checked != newState)
                        {
                            listView.Items[i].Checked = newState;
                        }
                    }
                }
                else if (info.Kind == ListRowKind.Chapter)
                {
                    // 親 Title 行を探す（後方に向かって走査、同じ PlaylistTag の Title 行が最初の親）
                    int titleIdx = -1;
                    for (int i = idx - 1; i >= 0; i--)
                    {
                        if (listView.Items[i].Tag is ListRowInfo r && r.Kind == ListRowKind.Title
                            && string.Equals(r.PlaylistTag, info.PlaylistTag, StringComparison.Ordinal))
                        {
                            titleIdx = i;
                            break;
                        }
                    }
                    if (titleIdx < 0) return;

                    // 親 Title に対応する Chapter 群のうち 1 つでもチェックされていれば親を true に
                    bool anyChecked = false;
                    for (int i = titleIdx + 1; i < listView.Items.Count; i++)
                    {
                        if (listView.Items[i].Tag is not ListRowInfo r) break;
                        if (r.Kind != ListRowKind.Chapter) break;
                        if (!string.Equals(r.PlaylistTag, info.PlaylistTag, StringComparison.Ordinal)) break;
                        if (listView.Items[i].Checked) { anyChecked = true; break; }
                    }
                    if (listView.Items[titleIdx].Checked != anyChecked)
                    {
                        listView.Items[titleIdx].Checked = anyChecked;
                    }
                }
            }
            finally
            {
                _suppressItemCheckCascade = false;
            }
        }

        /// <summary>
        /// ListView のチェック状態から、DB に投入する VideoChapter リストと再計算された
        /// ディスク集計値（チャプター数・総尺 ms）を抽出する。v1.1.1 追加。
        /// <para>
        /// 戻り値の VideoChapter は元のインスタンスをそのまま返すのではなく、
        /// chapter_no を連番（1, 2, 3, …）に振り直したコピーを生成する。
        /// 総尺 ms は、チェックされた章を PlaylistTag でグルーピングしてタイトル単位の尺を算出し、
        /// 「タイトル数 > 1 かつ VOB ハードリンク検出」のときは max、それ以外は sum を採用する
        /// （LoadIfoFolderScan の初期集約ロジックと一貫した挙動）。
        /// </para>
        /// </summary>
        /// <param name="vobsHardlinked">UDF ハードリンクが検出されていたかどうか（集計ルール切り替え用）。</param>
        private (List<VideoChapter> Chapters, ushort NumChapters, ulong TotalLengthMs)
            GetSelectedVideoChaptersFromListView(bool vobsHardlinked)
        {
            var selected = new List<VideoChapter>();
            foreach (ListViewItem item in listView.Items)
            {
                if (item.Tag is ListRowInfo info
                    && info.Kind == ListRowKind.Chapter
                    && item.Checked
                    && info.Chapter is not null)
                {
                    selected.Add(info.Chapter);
                }
            }

            // chapter_no を連番で振り直したコピーを生成（元インスタンスは破壊しない）
            var renum = new List<VideoChapter>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                var src = selected[i];
                renum.Add(new VideoChapter
                {
                    CatalogNo = src.CatalogNo,
                    ChapterNo = (ushort)(i + 1),
                    Title = src.Title,
                    PartType = src.PartType,
                    StartTimeMs = src.StartTimeMs,
                    DurationMs = src.DurationMs,
                    PlaylistFile = src.PlaylistFile,
                    SourceKind = src.SourceKind,
                    Notes = src.Notes,
                    CreatedBy = src.CreatedBy,
                    UpdatedBy = src.UpdatedBy,
                });
            }

            // タイトル単位（PlaylistFile 単位）で尺を集計
            var titleDurations = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var c in renum)
            {
                string tag = c.PlaylistFile ?? "";
                if (!titleDurations.ContainsKey(tag)) titleDurations[tag] = 0;
                titleDurations[tag] += c.DurationMs;
            }

            // LoadIfoFolderScan の初期集約と同じルール: 複数タイトル + hardlink → max、それ以外 → sum
            ulong totalMs;
            if (titleDurations.Count > 1 && vobsHardlinked)
            {
                ulong max = 0;
                foreach (var d in titleDurations.Values) if (d > max) max = d;
                totalMs = max;
            }
            else
            {
                ulong sum = 0;
                foreach (var d in titleDurations.Values) sum += d;
                totalMs = sum;
            }

            ushort numChapters = (ushort)Math.Min(renum.Count, ushort.MaxValue);
            return (renum, numChapters, totalMs);
        }

        /// <summary>
        /// 最後にスキャンしたディスクで UDF ハードリンクが検出されたかどうかを
        /// <see cref="_lastRead"/> から間接的に推定するためのフィールド。v1.1.1 追加。
        /// LoadIfoFolderScan 完了時に設定され、btnDbMatch_Click で集計ルールの切り替えに使う。
        /// （BD / 単一 VTS モードでは常に false でよい: タイトルは常に 1 個だけなので集計は sum = 総尺）。
        /// </summary>
        private bool _lastScanVobsHardlinked = false;

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
                // v1.1.1: ListView のチェック状態に基づいて、投入対象のチャプターを絞り込む。
                //         同時にディスク集計値（NumChapters / TotalLengthMs）も再計算する。
                //         （ユーザーが手動でタイトルやチャプターのチェックを外して、不要な
                //           ダミータイトルや前後のおまけチャプターを除外できるようにするため。）
                var (filteredChapters, filteredNumChapters, filteredTotalMs)
                    = GetSelectedVideoChaptersFromListView(_lastScanVobsHardlinked);

                if (filteredChapters.Count == 0)
                {
                    MessageBox.Show(this,
                        "登録対象のチャプターが 1 つも選択されていません。\n"
                        + "少なくとも 1 行はチェックを付けてから実行してください。",
                        "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Disc 側メタデータを上書きし、VideoChapters を絞り込み済みで置き換える
                _lastRead.Disc.NumChapters   = filteredNumChapters;
                _lastRead.Disc.TotalLengthMs = filteredTotalMs;
                _lastRead = _lastRead with { VideoChapters = filteredChapters };

                // v1.1.1: BD/DVD は MCN/CDDB が取れないため、TOC 曖昧（チャプター数 + 総尺 ms）でのみ照合。
                //         動画専用の照合メソッド FindCandidatesForVideoAsync を使い、チャプター数を
                //         totalTracks に詰め替える迂回はなくした（列の意味とシグネチャが一致するため）。
                var match = await _registration.FindCandidatesForVideoAsync(
                    numChapters: _lastRead.Disc.NumChapters ?? 0,
                    totalLengthMs: _lastRead.Disc.TotalLengthMs ?? 0UL);

                using var dlg = new DiscMatchDialog(_discsRepo, match.Candidates, match.MatchedBy);
                var result = dlg.ShowDialog(this);
                if (result != DialogResult.OK) return;

                if (dlg.SelectedDisc is not null)
                {
                    // 既存ディスクに反映：物理情報のみ同期する。
                    // BD/DVD は CD のような CD-Text / MCN / CDDB-ID は無いため、実質的に更新されるのは
                    // num_chapters / total_length_ms / volume_label / last_read_at のみ（v1.1.1 の単位是正後）。
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
                else if (dlg.WantsAttachToExistingProduct)
                {
                    // v1.1.3 追加分岐: 既存商品に追加ディスクとして登録する。
                    // BOX 商品の Disc 2 以降を追加するケースを想定。商品は新規作成せず、既存商品の
                    // disc_count をインクリメントしつつ、新しいディスクのみ INSERT する。
                    string? catalogNo = PromptCatalogNo();
                    if (string.IsNullOrWhiteSpace(catalogNo)) return;

                    using var adlg = new AttachToProductDialog(_productsRepo, _discsRepo, _seriesRepo);
                    if (adlg.ShowDialog(this) != DialogResult.OK || adlg.SelectedProduct is null) return;

                    var disc = _lastRead.Disc;
                    disc.CatalogNo = catalogNo!.Trim();
                    // シリーズはダイアログ側で「継承 / オールスターズ / 任意上書き」を解決済み
                    disc.SeriesId = adlg.OverrideSeriesId;

                    // _registration は DI で受け取った既存インスタンス。Product.disc_count 更新 +
                    // ディスク本体 + トラック・チャプターを共通サービスに任せる。
                    // v1.1.3: 組内番号 (disc_no_in_set) は呼び出し先で品番順に自動再採番される。
                    await _registration.AttachDiscToExistingProductAsync(
                        adlg.SelectedProduct.ProductCatalogNo,
                        disc,
                        Array.Empty<Track>()); // BD/DVD はトラックを使わない

                    // チャプターは既存ロジックと同じく専用テーブルへ
                    if (_videoChaptersRepo is not null && _lastRead.VideoChapters.Count > 0)
                    {
                        foreach (var vc in _lastRead.VideoChapters) vc.CatalogNo = disc.CatalogNo;
                        await _videoChaptersRepo.ReplaceAllForDiscAsync(disc.CatalogNo, _lastRead.VideoChapters);
                    }

                    MessageBox.Show(this,
                        $"既存商品 [{adlg.SelectedProduct.ProductCatalogNo}] にディスク [{disc.CatalogNo}] を追加し、" +
                        $"チャプター {_lastRead.VideoChapters.Count} 件を登録しました。\n" +
                        $"商品配下のディスクは品番順に組内番号を再採番済み。",
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
                    // v1.1.1: NewProductDialog で選ばれたシリーズ ID はディスク側の属性として適用する。
                    // 商品 (Product) には series_id を持たせない（列そのものが v1.1.1 で撤去された）。
                    disc.SeriesId            = pdlg.SelectedSeriesId;

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