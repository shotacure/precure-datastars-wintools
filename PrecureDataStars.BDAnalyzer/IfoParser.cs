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
