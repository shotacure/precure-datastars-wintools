using System;
using System.Collections.Generic;
using System.IO;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// DVD の VTS_xx_0.IFO ファイルからプログラム（チャプター）単位の再生時間を抽出するパーサー。
    /// <para>
    /// IFO のバイナリ構造（VTS_PGCIT → PGC → Cell Playback Info）を解析し、
    /// プログラムマップに従って Cell 再生時間を集約する。
    /// 時刻は BCD エンコードされた DVD タイムコード (HH:MM:SS:FF) で格納されている。
    /// </para>
    /// </summary>
    public static class IfoParser
    {
        /// <summary>DVD セクタサイズ（2048 バイト）。IFO 内のセクタ番号をバイトオフセットに変換する際に使用。</summary>
        private const int Sector = 2048;
        /// <summary>VTS_PGCIT（Program Chain Information Table）へのセクタポインタの IFO 内オフセット。</summary>
        private const int Off_VTS_PGCI_SectorPtr = 0x00CC;

        /// <summary>IFO 解析結果。プログラム単位・セル単位の再生時間リストを保持する。</summary>
        public sealed class ParseResult
        {
            public List<TimeSpan> ProgramDurations { get; init; } = new();
            public List<TimeSpan> CellDurations { get; init; } = new();
        }

        /// <summary>
        /// 1 つの PGC (Program Chain) の解析結果。VTS 内の全 PGC を列挙する
        /// <see cref="ExtractAllPgcsFromVtsIfo"/> が返す要素型。v1.1.1 追加。
        /// </summary>
        public sealed class PgcInfo
        {
            /// <summary>VTS_PGCIT 内での 1 始まりの PGC 番号。</summary>
            public int PgcIndex { get; init; }
            /// <summary>PGC の合計再生時間（ProgramDurations の合計と同じになるはず）。</summary>
            public TimeSpan TotalDuration { get; init; }
            /// <summary>プログラム（チャプター）単位の再生時間。</summary>
            public List<TimeSpan> ProgramDurations { get; init; } = new();
            /// <summary>セル単位の再生時間。</summary>
            public List<TimeSpan> CellDurations { get; init; } = new();
        }

        /// <summary>
        /// VIDEO_TS フォルダ全走査時に 1 つの VTS から選ばれた代表タイトルの情報。
        /// v1.1.1 追加。
        /// </summary>
        public sealed class TitleInfo
        {
            /// <summary>"VTS_02" 形式のタイトル識別子（video_chapters.playlist_file に入れる）。</summary>
            public string VtsTag { get; init; } = "";
            /// <summary>VTS 番号（1 始まり、例: 2）。</summary>
            public int VtsNumber { get; init; }
            /// <summary>採用された PGC の VTS 内 PGC 番号（1 始まり）。</summary>
            public int PgcIndex { get; init; }
            /// <summary>フィルタ後のチャプターの合計尺。</summary>
            public TimeSpan TotalDuration { get; init; }
            /// <summary>フィルタ後のチャプター尺リスト。</summary>
            public List<TimeSpan> ChapterDurations { get; init; } = new();
        }

        /// <summary>
        /// <see cref="ExtractTitlesFromVideoTs"/> の戻り値。v1.1.1 追加。
        /// </summary>
        public sealed class TitleScanResult
        {
            /// <summary>有効タイトル一覧（VTS 番号昇順）。</summary>
            public List<TitleInfo> Titles { get; init; } = new();
            /// <summary>フィルタ 1（VTS 全体のダミー判定）で除外された VTS 数。VMGI モードでは常に 0。</summary>
            public int ExcludedVtsCount { get; init; }
            /// <summary>フィルタ 2（尺 0ms）で除外されたチャプター数の合計。</summary>
            public int ExcludedZeroChapterCount { get; init; }
            /// <summary>フィルタ 3（境界の極短チャプター）で除外されたチャプター数の合計。</summary>
            public int ExcludedBoundaryShortCount { get; init; }
            /// <summary>
            /// フィルタ 4（VMGI モードのタイトル重複）で除外されたタイトル数。
            /// 同じ VTS 内の同じ PTT シグネチャ（= 同じ PGC の同じ Program 列）を指す複数タイトルが
            /// 検出された場合、2 個目以降をこのカウントに加算し、タイトル一覧からは除外する。
            /// ARccOS 系の anti-rip 保護スキームで現れる「99 タイトルが全部同じ内容を指している」
            /// パターンへの対処。Per-VTS モードでは常に 0。v1.1.1 追加。
            /// </summary>
            public int DuplicateTitlesRemoved { get; init; }
            /// <summary>
            /// スキャンモード: "VMGI"（VIDEO_TS.IFO の TT_SRPT を読めたケース、論理タイトル構造ベース）
            /// または "PerVts"（フォールバック、物理 VTS 単位で最長 PGC を拾うケース）。v1.1.1 追加。
            /// </summary>
            public string ScanMode { get; init; } = "";
            /// <summary>
            /// 全 VTS_*_1.VOB が同一サイズ（= UDF ハードリンクで実体 1 本を共有）かどうか。
            /// 総尺の集約ロジックで「合計 vs 最大値」を切り替える判定に使う。v1.1.1 追加。
            /// </summary>
            public bool VobsHardlinked { get; init; }
        }

        /// <summary>
        /// VIDEO_TS.IFO (VMGI) の TT_SRPT 1 エントリの情報。v1.1.1 追加。
        /// </summary>
        public sealed class VmgiTitleEntry
        {
            /// <summary>ディスク全体での論理タイトル番号（1 始まり）。</summary>
            public int TitleNumber { get; init; }
            /// <summary>このタイトルが属する VTS の番号（1 始まり）。</summary>
            public int VtsNumber { get; init; }
            /// <summary>VTS 内での Title Track Number（1 始まり）。</summary>
            public int TtnInVts { get; init; }
            /// <summary>このタイトルが持つチャプター（PTT）数。</summary>
            public int NumChapters { get; init; }
        }

        /// <summary>
        /// <see cref="TryReadVmgi"/> の戻り値。v1.1.1 追加。
        /// </summary>
        public sealed class VmgiScanResult
        {
            /// <summary>TT_SRPT から読めた論理タイトル一覧（タイトル番号昇順）。</summary>
            public List<VmgiTitleEntry> Titles { get; init; } = new();
        }

        /// <summary>
        /// VTS_xx_0.IFO を解析し、プログラム（≒チャプター）単位の再生時間を抽出する。
        /// </summary>
        /// <param name="path">IFO ファイルのパス（VTS_01_0.IFO 等）。</param>
        /// <returns>プログラム単位・セル単位の再生時間リスト。</returns>
        public static ParseResult ExtractProgramsFromVtsIfo(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            // IFO ファイルの最小サイズチェック（ヘッダ部だけでも 0x200 以上必要）
            if (fs.Length < 0x200)
                throw new InvalidDataException("IFOが短すぎます。");

            // IFO オフセット 0xCC から VTS_PGCIT のセクタ番号を取得（ビッグエンディアン）
            fs.Position = Off_VTS_PGCI_SectorPtr;
            uint vtsPgciSector = ReadU32BE(br);
            long vtsPgciOffset = (long)vtsPgciSector * Sector;
            if (vtsPgciOffset <= 0 || vtsPgciOffset >= fs.Length)
                throw new InvalidDataException("VTS_PGCIT へのポインタが不正です。");

            // VTS_PGCIT ヘッダの読み取り（PGC 数・予約語・末尾バイト位置）
            fs.Position = vtsPgciOffset;
            ushort nrPgci = ReadU16BE(br);
            br.ReadUInt16(); // reserved
            ReadU32BE(br);   // last byte (unused)

            if (nrPgci == 0)
                throw new InvalidDataException("PGC が見つかりません。");

            // PGCI_SRP（Search Pointer）から PGC の相対オフセットを取得
            // SRP サイズは規格上 8 バイトだが、一部ディスクでは 12 バイトの場合があるため両方試行
            long srpBase = fs.Position;
            uint pgcStartRel = TryReadPgcStart(fs, br, vtsPgciOffset, srpBase, 8)
                               ?? TryReadPgcStart(fs, br, vtsPgciOffset, srpBase, 12)
                               ?? throw new InvalidDataException("PGCI_SRP の解釈に失敗しました。");

            long pgcOffset = vtsPgciOffset + pgcStartRel;
            if (pgcOffset <= 0 || pgcOffset >= fs.Length)
                throw new InvalidDataException("PGC 先頭位置が不正です。");

            // PGC（Program Chain）ヘッダの読み取り
            fs.Position = pgcOffset;
            ReadU16BE(br); // PGC ヘッダ 2 バイト（未使用フラグ）
            byte nrPrograms = (byte)fs.ReadByte(); // プログラム（チャプター）数
            byte nrCells = (byte)fs.ReadByte();

            _ = ReadDvdTime(br); // PGC playback time（未使用）

            // PGC 内の各サブテーブルへのオフセット群を読み取り（PGC 先頭 +0xE4 から）
            fs.Position = pgcOffset + 0x00E4;
            ushort cmdOff = ReadU16BE(br);
            ushort pgmMapOff = ReadU16BE(br);
            ushort cellPlayOff = ReadU16BE(br);
            ushort cellPosOff = ReadU16BE(br);

            if (nrPrograms == 0 || nrCells == 0)
                throw new InvalidDataException("Program/Cell 数が不正です。");

            // プログラムマップ: 各プログラムの開始セル番号を読み取り（1 バイト × nrPrograms）
            long pgmMapAddr = pgcOffset + pgmMapOff;
            fs.Position = pgmMapAddr;
            var entryCells = new List<int>();
            for (int i = 0; i < nrPrograms; i++)
                entryCells.Add(fs.ReadByte());

            // Cell Playback Information: 各セルの再生時間を BCD タイムコードから取得
            // 1 セルあたり 0x18 バイト（カテゴリ 4B + 時間 4B + 残り 16B）
            long cellPlayAddr = pgcOffset + cellPlayOff;
            fs.Position = cellPlayAddr;

            var cellDurations = new List<TimeSpan>();
            for (int i = 0; i < nrCells; i++)
            {
                ReadU32BE(br); // category 等
                var t = ReadDvdTime(br);
                cellDurations.Add(t);
                fs.Position += (0x18 - 0x08);
            }

            // プログラムマップに従い、各プログラムに属するセルの時間を集約
            var programDurations = new List<TimeSpan>();
            for (int p = 0; p < nrPrograms; p++)
            {
                int startCell = entryCells[p];
                int endCell = (p == nrPrograms - 1) ? nrCells : entryCells[p + 1] - 1;

                if (startCell < 1 || startCell > nrCells || endCell < startCell || endCell > nrCells)
                    throw new InvalidDataException("Program → Cell のマッピングが不正です。");

                TimeSpan sum = TimeSpan.Zero;
                for (int c = startCell; c <= endCell; c++)
                    sum += cellDurations[c - 1];

                programDurations.Add(sum);
            }

            return new ParseResult
            {
                ProgramDurations = programDurations,
                CellDurations = cellDurations
            };
        }

        /// <summary>
        /// VTS_xx_0.IFO 内の全 PGC を列挙してそれぞれの Program/Cell 時間を抽出する。
        /// <para>
        /// 1 つの VTS が複数の PGC（= 複数の再生シーケンス）を持つケース（多話収録 DVD の一部構造など）に対応するため、
        /// v1.1.1 で新設。<see cref="ExtractProgramsFromVtsIfo"/> は後方互換のため先頭 PGC のみを返す現行挙動を維持する。
        /// </para>
        /// </summary>
        /// <param name="path">IFO ファイルのパス（VTS_xx_0.IFO）。</param>
        /// <returns>PGC 情報のリスト。解析不能な PGC はスキップされる（例外は投げない）。</returns>
        public static List<PgcInfo> ExtractAllPgcsFromVtsIfo(string path)
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            if (fs.Length < 0x200)
                throw new InvalidDataException("IFOが短すぎます。");

            fs.Position = Off_VTS_PGCI_SectorPtr;
            uint vtsPgciSector = ReadU32BE(br);
            long vtsPgciOffset = (long)vtsPgciSector * Sector;
            if (vtsPgciOffset <= 0 || vtsPgciOffset >= fs.Length)
                throw new InvalidDataException("VTS_PGCIT へのポインタが不正です。");

            fs.Position = vtsPgciOffset;
            ushort nrPgci = ReadU16BE(br);
            br.ReadUInt16(); // reserved
            ReadU32BE(br);   // last byte (unused)

            var result = new List<PgcInfo>();
            if (nrPgci == 0)
                return result;

            // SRP サイズ判定: 先頭 SRP を 8 バイトで解釈してみて妥当なら 8 バイト、そうでなければ 12 バイト。
            // 単一 VTS 読み取りの TryReadPgcStart と同じ判定ロジック。
            long srpBase = vtsPgciOffset + 8;
            int srpSize = 8;
            if (TryReadPgcStart(fs, br, vtsPgciOffset, srpBase, 8) == null
                && TryReadPgcStart(fs, br, vtsPgciOffset, srpBase, 12) != null)
            {
                srpSize = 12;
            }

            for (int i = 0; i < nrPgci; i++)
            {
                long thisSrpBase = srpBase + (long)i * srpSize;
                uint? pgcStartRel = TryReadPgcStart(fs, br, vtsPgciOffset, thisSrpBase, srpSize);
                if (pgcStartRel == null) continue;

                long pgcOffset = vtsPgciOffset + pgcStartRel.Value;
                var info = TryParseSinglePgc(fs, br, pgcOffset, i + 1);
                if (info != null) result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// 指定 PGC オフセットから 1 つの PGC を解析する（失敗時は null を返す）。
        /// v1.1.1 で <see cref="ExtractAllPgcsFromVtsIfo"/> の内部用ヘルパとして追加。
        /// </summary>
        private static PgcInfo? TryParseSinglePgc(Stream fs, BinaryReader br, long pgcOffset, int pgcIndex)
        {
            try
            {
                if (pgcOffset <= 0 || pgcOffset + 0x00EC > fs.Length)
                    return null;

                fs.Position = pgcOffset;
                ReadU16BE(br); // ヘッダ 2 バイト（未使用フラグ）
                byte nrPrograms = (byte)fs.ReadByte();
                byte nrCells = (byte)fs.ReadByte();

                var totalTime = ReadDvdTime(br); // PGC playback time

                fs.Position = pgcOffset + 0x00E4;
                ReadU16BE(br); // cmd offset
                ushort pgmMapOff = ReadU16BE(br);
                ushort cellPlayOff = ReadU16BE(br);
                ReadU16BE(br); // cell pos offset

                if (nrPrograms == 0 || nrCells == 0)
                    return null;

                // プログラムマップ
                fs.Position = pgcOffset + pgmMapOff;
                if (fs.Position + nrPrograms > fs.Length) return null;
                var entryCells = new List<int>();
                for (int i = 0; i < nrPrograms; i++)
                    entryCells.Add(fs.ReadByte());

                // セル再生時間
                fs.Position = pgcOffset + cellPlayOff;
                if (fs.Position + nrCells * 0x18 > fs.Length) return null;
                var cellDurations = new List<TimeSpan>();
                for (int i = 0; i < nrCells; i++)
                {
                    ReadU32BE(br); // category
                    var t = ReadDvdTime(br);
                    cellDurations.Add(t);
                    fs.Position += (0x18 - 0x08);
                }

                // プログラム → セル 集約
                var programDurations = new List<TimeSpan>();
                for (int p = 0; p < nrPrograms; p++)
                {
                    int startCell = entryCells[p];
                    int endCell = (p == nrPrograms - 1) ? nrCells : entryCells[p + 1] - 1;
                    if (startCell < 1 || startCell > nrCells || endCell < startCell || endCell > nrCells)
                        return null;

                    TimeSpan sum = TimeSpan.Zero;
                    for (int c = startCell; c <= endCell; c++)
                        sum += cellDurations[c - 1];
                    programDurations.Add(sum);
                }

                return new PgcInfo
                {
                    PgcIndex = pgcIndex,
                    TotalDuration = totalTime,
                    ProgramDurations = programDurations,
                    CellDurations = cellDurations
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// VIDEO_TS フォルダを走査して、ディスク上の論理タイトル（およびそのチャプター）を抽出する。
        /// v1.1.1 で実装された。
        /// <para>
        /// 動作は 2 段階のルーティング:
        /// </para>
        /// <list type="number">
        ///   <item>
        ///     <b>VMGI 経路（正攻法、優先）</b>: VIDEO_TS.IFO の TT_SRPT（Title Search Pointer Table）
        ///     から論理タイトル一覧を取得し、各タイトルについて VTS_xx_0.IFO の VTS_PTT_SRPT を読んで
        ///     チャプターごとの (PGC 番号, Program 番号) を解決、該当 PGC の Program 尺リストから
        ///     各チャプターの再生時間を組み立てる。DVD プレイヤーがユーザーに見せる「タイトル/チャプター」
        ///     構造と完全一致する（たとえば UDF ハードリンク + 物理 VTS 11 個でも、VMGI が 1 論理タイトル
        ///     と主張していれば 1 タイトルで返す）。
        ///   </item>
        ///   <item>
        ///     <b>Per-VTS 経路（フォールバック）</b>: VMGI が読めない／TT_SRPT が壊れている場合に、
        ///     各 VTS_xx_0.IFO の最長 PGC を「その VTS のタイトル本編」とみなして拾い上げる従来実装。
        ///     VMGI が正常なディスクでは発火しないが、オーサリング破損ディスクのサルベージ用として維持。
        ///   </item>
        /// </list>
        /// <para>
        /// いずれの経路でも、ゼロ尺チャプターの除去（フィルタ 2）と境界極短チャプターの除去（フィルタ 3）は
        /// 同じしきい値で適用される。VTS レベルのダミー除去（フィルタ 1）は Per-VTS 経路でのみ意味を持つ。
        /// </para>
        /// </summary>
        public static TitleScanResult ExtractTitlesFromVideoTs(
            string videoTsFolderPath,
            int minVtsDurationSec = 5,
            long minChapterDurationMs = 1,
            long minBoundaryChapterMs = 500)
        {
            if (!Directory.Exists(videoTsFolderPath))
                throw new DirectoryNotFoundException($"VIDEO_TS フォルダが見つかりません: {videoTsFolderPath}");

            // まず VMGI（VIDEO_TS.IFO の TT_SRPT）を試す
            var vmgiResult = ExtractTitlesFromVideoTsUsingVmgi(
                videoTsFolderPath, minChapterDurationMs, minBoundaryChapterMs);
            if (vmgiResult != null) return vmgiResult;

            // VMGI が読めない場合は Per-VTS フォールバック
            return ExtractTitlesFromVideoTsPerVts(
                videoTsFolderPath, minVtsDurationSec, minChapterDurationMs, minBoundaryChapterMs);
        }

        /// <summary>
        /// VMGI (VIDEO_TS.IFO) の TT_SRPT を読み、論理タイトル一覧を返す。
        /// シグネチャや構造が壊れていたら null を返す（呼び出し側はフォールバックへ）。
        /// v1.1.1 追加。
        /// </summary>
        public static VmgiScanResult? TryReadVmgi(string videoTsIfoPath)
        {
            try
            {
                using var fs = File.OpenRead(videoTsIfoPath);
                using var br = new BinaryReader(fs);

                if (fs.Length < 0x200) return null;

                // シグネチャ検証: "DVDVIDEO-VMG" (12 バイト固定)
                fs.Position = 0;
                var sig = br.ReadBytes(12);
                if (sig.Length < 12) return null;
                string sigStr = System.Text.Encoding.ASCII.GetString(sig);
                if (sigStr != "DVDVIDEO-VMG") return null;

                // TT_SRPT へのセクタポインタ: VMGI の 0xC4 に 4 バイト BE
                fs.Position = 0xC4;
                uint ttSrptSector = ReadU32BE(br);
                long ttSrptOffset = (long)ttSrptSector * Sector;
                if (ttSrptOffset <= 0 || ttSrptOffset + 8 > fs.Length) return null;

                // TT_SRPT ヘッダ: 2B 件数 + 2B 予約 + 4B 末尾バイト
                fs.Position = ttSrptOffset;
                ushort numTitles = ReadU16BE(br);
                br.ReadUInt16(); // reserved
                ReadU32BE(br);   // end byte (unused)

                if (numTitles == 0 || numTitles > 99) return null;

                var titles = new List<VmgiTitleEntry>();
                for (int i = 0; i < numTitles; i++)
                {
                    // 各 TT_SRP: 12 バイト固定
                    long entryOffset = ttSrptOffset + 8 + (long)i * 12;
                    if (entryOffset + 12 > fs.Length) break;

                    fs.Position = entryOffset;
                    br.ReadByte();                // playback flags
                    br.ReadByte();                // number of angles
                    ushort numChapters = ReadU16BE(br);
                    ReadU16BE(br);                // parental mask
                    byte vtsNumber = br.ReadByte();
                    byte ttnInVts = br.ReadByte();
                    // 以降 4B: VTS 開始セクタ（未使用なのでスキップ）

                    if (vtsNumber == 0 || ttnInVts == 0 || numChapters == 0) continue;

                    titles.Add(new VmgiTitleEntry
                    {
                        TitleNumber = i + 1,
                        VtsNumber = vtsNumber,
                        TtnInVts = ttnInVts,
                        NumChapters = numChapters
                    });
                }

                return titles.Count > 0 ? new VmgiScanResult { Titles = titles } : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// VTS_xx_0.IFO の VTS_PTT_SRPT を読み、指定 TTN のチャプターを構成する
        /// (PGC 番号, Program 番号) ペアのリストを返す。失敗時は null。v1.1.1 追加。
        /// </summary>
        /// <param name="vtsIfoPath">VTS_xx_0.IFO のパス。</param>
        /// <param name="ttnInVts">VTS 内 Title Track Number（1 始まり）。</param>
        public static List<(int PgcNo, int PgmNo)>? TryReadTitlePttEntries(string vtsIfoPath, int ttnInVts)
        {
            try
            {
                using var fs = File.OpenRead(vtsIfoPath);
                using var br = new BinaryReader(fs);

                if (fs.Length < 0x200) return null;

                // VTS_PTT_SRPT_SA は VTSI の 0xC8 に 4 バイト BE のセクタポインタ
                fs.Position = 0xC8;
                uint pttSector = ReadU32BE(br);
                long pttOffset = (long)pttSector * Sector;
                if (pttOffset <= 0 || pttOffset + 8 > fs.Length) return null;

                // VTS_PTT_SRPT ヘッダ: 2B タイトル数 + 2B 予約 + 4B 末尾バイト（テーブル末尾オフセット - 1）
                fs.Position = pttOffset;
                ushort nrTitles = ReadU16BE(br);
                br.ReadUInt16(); // reserved
                uint endByte = ReadU32BE(br);

                if (ttnInVts < 1 || ttnInVts > nrTitles) return null;

                // 指定タイトルの PTT テーブル先頭オフセット（VTS_PTT_SRPT 相対）
                fs.Position = pttOffset + 8 + (long)(ttnInVts - 1) * 4;
                uint thisTitleOffset = ReadU32BE(br);

                // 次タイトル（または末尾）までのサイズで、このタイトルの PTT エントリ数を算出
                uint boundaryOffset;
                if (ttnInVts < nrTitles)
                {
                    fs.Position = pttOffset + 8 + (long)ttnInVts * 4;
                    boundaryOffset = ReadU32BE(br);
                }
                else
                {
                    boundaryOffset = endByte + 1;
                }

                if (boundaryOffset <= thisTitleOffset) return null;
                int numChapters = (int)((boundaryOffset - thisTitleOffset) / 4);
                if (numChapters <= 0 || numChapters > 999) return null;

                // PTT エントリ本体: 各 4 バイト (2B PGCN + 2B PGN)
                fs.Position = pttOffset + thisTitleOffset;
                if (fs.Position + (long)numChapters * 4 > fs.Length) return null;

                var result = new List<(int, int)>(numChapters);
                for (int i = 0; i < numChapters; i++)
                {
                    ushort pgcn = ReadU16BE(br);
                    ushort pgn = ReadU16BE(br);
                    result.Add((pgcn, pgn));
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 全 <c>VTS_*_1.VOB</c> ファイルが同一バイト数（= UDF ハードリンクで実体 1 本を共有）か判定する。
        /// v1.1.1 追加。総尺集約ロジックで「合計 vs 最大値」の切り替えに使う。
        /// </summary>
        public static bool AreVobsHardlinked(string videoTsFolderPath)
        {
            if (!Directory.Exists(videoTsFolderPath)) return false;
            var sizes = new List<long>();
            foreach (var p in Directory.EnumerateFiles(videoTsFolderPath, "VTS_*_1.VOB", SearchOption.TopDirectoryOnly))
            {
                long len = new FileInfo(p).Length;
                if (len > 0) sizes.Add(len);
            }
            if (sizes.Count < 2) return false;
            long first = sizes[0];
            foreach (var s in sizes)
            {
                if (s != first) return false;
            }
            return true;
        }

        /// <summary>
        /// VMGI 経路でのタイトル抽出本体。v1.1.1 追加。
        /// TT_SRPT 解析 + VTS_PTT_SRPT 解析 + PGC/Program ルックアップで、
        /// 論理タイトル構造（DVD プレイヤーが UI に見せる構造）と完全一致するチャプター一覧を返す。
        /// </summary>
        private static TitleScanResult? ExtractTitlesFromVideoTsUsingVmgi(
            string videoTsFolderPath,
            long minChapterDurationMs,
            long minBoundaryChapterMs)
        {
            string vmgIfoPath = Path.Combine(videoTsFolderPath, "VIDEO_TS.IFO");
            if (!File.Exists(vmgIfoPath)) return null;

            var vmgi = TryReadVmgi(vmgIfoPath);
            if (vmgi == null || vmgi.Titles.Count == 0) return null;

            // 同じ VTS の PGC 一覧を何度も読まないようキャッシュ
            var pgcCache = new Dictionary<int, List<PgcInfo>>();

            // フィルタ 4: タイトル重複除外用のシグネチャ集合。
            // 同じ (VtsNumber, PTT エントリ列) を持つタイトルは、ARccOS 系保護や
            // オーサリングノイズで生まれた論理的重複とみなし、2 個目以降を捨てる。
            var seenSignatures = new HashSet<string>();
            int duplicatesRemoved = 0;

            var titles = new List<TitleInfo>();
            int excludedZero = 0;
            int excludedBoundary = 0;

            foreach (var t in vmgi.Titles)
            {
                string vtsIfoPath = Path.Combine(videoTsFolderPath,
                    $"VTS_{t.VtsNumber.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}_0.IFO");
                if (!File.Exists(vtsIfoPath)) continue;

                var pttEntries = TryReadTitlePttEntries(vtsIfoPath, t.TtnInVts);
                if (pttEntries == null || pttEntries.Count == 0) continue;

                // フィルタ 4 判定: raw PTT エントリ列 + VTS 番号でシグネチャを組む。
                // (PGC, Program) のペア列が同じ = 同じコンテンツを別角度から指している重複タイトル。
                var sigBuilder = new System.Text.StringBuilder();
                sigBuilder.Append("V").Append(t.VtsNumber).Append('#');
                for (int i = 0; i < pttEntries.Count; i++)
                {
                    if (i > 0) sigBuilder.Append(',');
                    sigBuilder.Append(pttEntries[i].PgcNo).Append('-').Append(pttEntries[i].PgmNo);
                }
                string signature = sigBuilder.ToString();
                if (!seenSignatures.Add(signature))
                {
                    // 既出シグネチャ: 重複タイトルとして捨てる
                    duplicatesRemoved++;
                    continue;
                }

                if (!pgcCache.TryGetValue(t.VtsNumber, out var pgcs))
                {
                    try { pgcs = ExtractAllPgcsFromVtsIfo(vtsIfoPath); }
                    catch { continue; }
                    pgcCache[t.VtsNumber] = pgcs;
                }

                // 各チャプターの (PgcNo, PgmNo) を Program 尺に解決
                var chapters = new List<TimeSpan>();
                int? representativePgcNo = null;
                foreach (var (pgcNo, pgmNo) in pttEntries)
                {
                    // pgcs は VTS 内 PGCIT の出現順なので、PgcIndex == pgcNo の要素を探す
                    PgcInfo? pgc = null;
                    foreach (var p in pgcs)
                    {
                        if (p.PgcIndex == pgcNo) { pgc = p; break; }
                    }
                    if (pgc == null) continue;
                    if (pgmNo < 1 || pgmNo > pgc.ProgramDurations.Count) continue;
                    chapters.Add(pgc.ProgramDurations[pgmNo - 1]);
                    representativePgcNo ??= pgcNo;
                }

                if (chapters.Count == 0) continue;

                // フィルタ 2: 尺 0ms のチャプターを除外
                int before2 = chapters.Count;
                chapters.RemoveAll(d => d.TotalMilliseconds < minChapterDurationMs);
                excludedZero += before2 - chapters.Count;

                // フィルタ 3: 先頭・末尾の極短チャプター
                while (chapters.Count > 0 && chapters[0].TotalMilliseconds < minBoundaryChapterMs)
                {
                    chapters.RemoveAt(0);
                    excludedBoundary++;
                }
                while (chapters.Count > 0 && chapters[chapters.Count - 1].TotalMilliseconds < minBoundaryChapterMs)
                {
                    chapters.RemoveAt(chapters.Count - 1);
                    excludedBoundary++;
                }

                if (chapters.Count == 0) continue;

                double totalMs = 0;
                foreach (var d in chapters) totalMs += d.TotalMilliseconds;

                titles.Add(new TitleInfo
                {
                    // VMGI モードでは論理タイトル番号ベースのタグ（"Title_01" 等）を使う。
                    // video_chapters.playlist_file に入る値もこれになる。
                    VtsTag = $"Title_{t.TitleNumber.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)}",
                    VtsNumber = t.VtsNumber,
                    PgcIndex = representativePgcNo ?? 0,
                    TotalDuration = TimeSpan.FromMilliseconds(totalMs),
                    ChapterDurations = chapters
                });
            }

            if (titles.Count == 0) return null;

            return new TitleScanResult
            {
                Titles = titles,
                ExcludedVtsCount = 0,
                ExcludedZeroChapterCount = excludedZero,
                ExcludedBoundaryShortCount = excludedBoundary,
                DuplicateTitlesRemoved = duplicatesRemoved,
                ScanMode = "VMGI",
                VobsHardlinked = AreVobsHardlinked(videoTsFolderPath)
            };
        }

        /// <summary>
        /// Per-VTS 経路のフォールバック実装。VMGI が読めないディスクで、物理 VTS 単位に
        /// 最長 PGC を拾い上げる。v1.1.0 の挙動相当（v1.1.1 で private に移動）。
        /// </summary>
        private static TitleScanResult ExtractTitlesFromVideoTsPerVts(
            string videoTsFolderPath,
            int minVtsDurationSec,
            long minChapterDurationMs,
            long minBoundaryChapterMs)
        {
            var titles = new List<TitleInfo>();
            int excludedVts = 0;
            int excludedZero = 0;
            int excludedBoundary = 0;

            // VTS_NN_0.IFO を列挙（NN は 01-99 の 2 桁）。VIDEO_TS.IFO は対象外。
            var vtsIfos = new List<string>();
            foreach (var p in Directory.EnumerateFiles(videoTsFolderPath, "VTS_*_0.IFO", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(p);
                if (name.Length == 12
                    && char.IsDigit(name[4]) && char.IsDigit(name[5]))
                {
                    vtsIfos.Add(p);
                }
            }
            vtsIfos.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (var ifoPath in vtsIfos)
            {
                string stem = Path.GetFileNameWithoutExtension(ifoPath); // "VTS_02_0"
                string vtsTag = stem.Substring(0, 6);                    // "VTS_02"
                int vtsNumber = int.Parse(stem.Substring(4, 2), System.Globalization.CultureInfo.InvariantCulture);

                List<PgcInfo> pgcs;
                try
                {
                    pgcs = ExtractAllPgcsFromVtsIfo(ifoPath);
                }
                catch
                {
                    excludedVts++;
                    continue;
                }

                if (pgcs.Count == 0)
                {
                    excludedVts++;
                    continue;
                }

                // VTS 内で「プログラム尺の合計」が最大の PGC を代表タイトルとして採用
                PgcInfo? best = null;
                double bestMs = -1.0;
                foreach (var pgc in pgcs)
                {
                    double sumMs = 0;
                    foreach (var d in pgc.ProgramDurations) sumMs += d.TotalMilliseconds;
                    if (sumMs > bestMs)
                    {
                        bestMs = sumMs;
                        best = pgc;
                    }
                }

                // フィルタ 1: VTS レベルのダミー排除
                if (best == null || bestMs < minVtsDurationSec * 1000.0)
                {
                    excludedVts++;
                    continue;
                }

                var chapters = new List<TimeSpan>(best.ProgramDurations);

                // フィルタ 2: 尺 0ms のチャプターを全て除外
                int before2 = chapters.Count;
                chapters.RemoveAll(d => d.TotalMilliseconds < minChapterDurationMs);
                excludedZero += before2 - chapters.Count;

                // フィルタ 3: 先頭・末尾の極短チャプターを剥がす
                while (chapters.Count > 0 && chapters[0].TotalMilliseconds < minBoundaryChapterMs)
                {
                    chapters.RemoveAt(0);
                    excludedBoundary++;
                }
                while (chapters.Count > 0 && chapters[chapters.Count - 1].TotalMilliseconds < minBoundaryChapterMs)
                {
                    chapters.RemoveAt(chapters.Count - 1);
                    excludedBoundary++;
                }

                if (chapters.Count == 0)
                {
                    excludedVts++;
                    continue;
                }

                double totalMs = 0;
                foreach (var d in chapters) totalMs += d.TotalMilliseconds;

                titles.Add(new TitleInfo
                {
                    VtsTag = vtsTag,
                    VtsNumber = vtsNumber,
                    PgcIndex = best.PgcIndex,
                    TotalDuration = TimeSpan.FromMilliseconds(totalMs),
                    ChapterDurations = chapters
                });
            }

            return new TitleScanResult
            {
                Titles = titles,
                ExcludedVtsCount = excludedVts,
                ExcludedZeroChapterCount = excludedZero,
                ExcludedBoundaryShortCount = excludedBoundary,
                ScanMode = "PerVts",
                VobsHardlinked = AreVobsHardlinked(videoTsFolderPath)
            };
        }

        /// <summary>
        /// PGCI_SRP から PGC 開始オフセットの読み取りを試行する。
        /// SRP のサイズ（8 or 12 バイト）ごとに候補を検証し、有効なら返す。
        /// </summary>
        private static uint? TryReadPgcStart(Stream fs, BinaryReader br, long tableBase, long srpBase, int srpSize)
        {
            fs.Position = srpBase;
            // SRP の末尾 4 バイトが PGC 開始位置（VTS_PGCIT 先頭からの相対オフセット）
            var buf = br.ReadBytes(srpSize);
            if (buf.Length != srpSize) return null;
            uint candidate = (uint)((buf[srpSize - 4] << 24) | (buf[srpSize - 3] << 16) | (buf[srpSize - 2] << 8) | buf[srpSize - 1]);

            long pgcOffset = tableBase + candidate;
            if (pgcOffset + 0x00E8 + 2 > fs.Length) return null;

            long save = fs.Position;
            try
            {
                // 候補位置が妥当かどうか、プログラム数・セル数を検証
                fs.Position = pgcOffset + 0x0002; // nr_programs
                int nrPrograms = fs.ReadByte();
                int nrCells = fs.ReadByte();
                if (nrPrograms <= 0 || nrPrograms > 99 || nrCells <= 0 || nrCells > 255)
                    return null;

                fs.Position = pgcOffset + 0x00E6;
                ushort pgmMapOff = ReadU16BE(br);
                fs.Position = pgcOffset + pgmMapOff;
                if (pgcOffset + pgmMapOff + nrPrograms > fs.Length) return null;

                return candidate;
            }
            catch
            {
                return null;
            }
            finally
            {
                fs.Position = save;
            }
        }

        /// <summary>ビッグエンディアン 16 ビット符号なし整数を読み取る（DVD はビッグエンディアン）。</summary>
        private static ushort ReadU16BE(BinaryReader br)
        {
            var b = br.ReadBytes(2);
            if (b.Length < 2) throw new EndOfStreamException();
            return (ushort)((b[0] << 8) | b[1]);
        }

        /// <summary>ビッグエンディアン 32 ビット符号なし整数を読み取る。</summary>
        private static uint ReadU32BE(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        /// <summary>
        /// DVD タイムコード（4 バイト BCD: HH:MM:SS:FF）を TimeSpan に変換する。
        /// フレームレートは上位 2 ビット（01=25fps, 11=30fps）で判定する。
        /// </summary>
        private static TimeSpan ReadDvdTime(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();

            // BCD (Binary-Coded Decimal) デコード: 上位ニブル×10 + 下位ニブル
            int BcdToInt(byte x) => ((x >> 4) & 0x0F) * 10 + (x & 0x0F);

            int hh = BcdToInt(b[0]);
            int mm = BcdToInt(b[1]);
            int ss = BcdToInt(b[2]);

            // フレームバイト: 上位 2 ビット = fps フラグ、下位 6 ビット = フレーム番号 (BCD)
            int frameByte = b[3];
            int fpsFlag = (frameByte >> 6) & 0b11;
            int ff = BcdToInt((byte)(frameByte & 0x3F));

            double fps = fpsFlag switch
            {
                0b01 => 25.0,
                0b11 => 30.0,
                _ => 30.0
            };

            double totalSeconds = hh * 3600 + mm * 60 + ss + (ff / fps);
            return TimeSpan.FromSeconds(totalSeconds);
        }
    }
}
