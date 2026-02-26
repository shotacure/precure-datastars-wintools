using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static PrecureDataStars.CDAnalyzer.Helpers;

namespace PrecureDataStars.CDAnalyzer
{
    /// <summary>
    /// SCSI MMC (Multi-Media Commands) を使用して CD-DA ディスクの情報を読み取る低レベルクラス。
    /// <para>
    /// Windows の IOCTL_SCSI_PASS_THROUGH_DIRECT を使い、光学ドライブに対して
    /// TOC 読み取り (READ TOC)、MCN 読み取り (READ SUB-CHANNEL)、
    /// CD-Text 読み取り (READ TOC/PMA/ATIP Format 5) 等の SCSI コマンドを発行する。
    /// </para>
    /// <remarks>
    /// CDB は unsafe/fixed を使わず 16 個の byte フィールドで定義している。
    /// P/Invoke による Win32 API 直接呼び出しのため、Windows 専用。
    /// </remarks>
    /// </summary>
    internal static class ScsiMmci
    {
        // ===== P/Invoke: Win32 API (kernel32.dll) 関数宣言 =====

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            ref SCSI_PASS_THROUGH_DIRECT sptd,
            int sptdLen,
            ref SCSI_PASS_THROUGH_DIRECT sptdOut,
            int sptdOutLen,
            out uint bytesReturned,
            IntPtr overlapped);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        // IOCTL_SCSI_PASS_THROUGH_DIRECT
        private const uint IOCTL_SCSI_PASS_THROUGH_DIRECT = 0x4D014;

        // SCSI Pass-Through Direct 構造体
        // fixed 配列を使わず、CDB を 16 個の個別 byte フィールドで定義（unsafe キーワード不要）
        [StructLayout(LayoutKind.Sequential)]
        private struct SCSI_PASS_THROUGH_DIRECT
        {
            public ushort Length;
            public byte ScsiStatus;
            public byte PathId;
            public byte TargetId;
            public byte Lun;
            public byte CdbLength;
            public byte SenseInfoLength;
            public byte DataIn;              // 0=OUT, 1=IN
            public uint DataTransferLength;
            public uint TimeOutValue;        // seconds
            public IntPtr DataBuffer;        // void*
            public uint SenseInfoOffset;     // offset to sense buffer

            // 16-byte CDB (fixedを使わず16個のbyteで定義)
            public byte Cdb0; public byte Cdb1; public byte Cdb2; public byte Cdb3;
            public byte Cdb4; public byte Cdb5; public byte Cdb6; public byte Cdb7;
            public byte Cdb8; public byte Cdb9; public byte Cdb10; public byte Cdb11;
            public byte Cdb12; public byte Cdb13; public byte Cdb14; public byte Cdb15;
        }

        /// <summary>
        /// SCSI_PASS_THROUGH_DIRECT 構造体の CDB（Command Descriptor Block）フィールドに
        /// 指定のコマンドバイト列をセットする。全 16 バイトをゼロクリア後、入力長分をコピー。
        /// </summary>
        private static void SetCdb(ref SCSI_PASS_THROUGH_DIRECT s, ReadOnlySpan<byte> cdb)
        {
            // 全部ゼロクリア
            s.Cdb0 = s.Cdb1 = s.Cdb2 = s.Cdb3 = s.Cdb4 = s.Cdb5 = s.Cdb6 = s.Cdb7 =
            s.Cdb8 = s.Cdb9 = s.Cdb10 = s.Cdb11 = s.Cdb12 = s.Cdb13 = s.Cdb14 = s.Cdb15 = 0;

            // 入力を順に詰める（最大16）
            if (cdb.Length > 0) s.Cdb0 = cdb[0];
            if (cdb.Length > 1) s.Cdb1 = cdb[1];
            if (cdb.Length > 2) s.Cdb2 = cdb[2];
            if (cdb.Length > 3) s.Cdb3 = cdb[3];
            if (cdb.Length > 4) s.Cdb4 = cdb[4];
            if (cdb.Length > 5) s.Cdb5 = cdb[5];
            if (cdb.Length > 6) s.Cdb6 = cdb[6];
            if (cdb.Length > 7) s.Cdb7 = cdb[7];
            if (cdb.Length > 8) s.Cdb8 = cdb[8];
            if (cdb.Length > 9) s.Cdb9 = cdb[9];
            if (cdb.Length > 10) s.Cdb10 = cdb[10];
            if (cdb.Length > 11) s.Cdb11 = cdb[11];
            if (cdb.Length > 12) s.Cdb12 = cdb[12];
            if (cdb.Length > 13) s.Cdb13 = cdb[13];
            if (cdb.Length > 14) s.Cdb14 = cdb[14];
            if (cdb.Length > 15) s.Cdb15 = cdb[15];

            s.CdbLength = 16; // 常に16に揃える
        }

        /// <summary>
        /// 光学ドライブをデバイスパス（\\.\X:）で開き、SCSI コマンド発行用のハンドルを返す。
        /// </summary>
        public static SafeFileHandle OpenCdDevice(char driveLetter)
        {
            var path = $@"\\.\{char.ToUpperInvariant(driveLetter)}:";
            var handle = CreateFile(path,
                                    GENERIC_READ | GENERIC_WRITE,
                                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    OPEN_EXISTING,
                                    FILE_ATTRIBUTE_NORMAL,
                                    IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFile failed: {path}");
            return handle;
        }

        /// <summary>
        /// IOCTL_SCSI_PASS_THROUGH_DIRECT を使い、任意の SCSI コマンドを発行する。
        /// データバッファは GCHandle.Alloc で pin して DeviceIoControl に渡す。
        /// </summary>
        private static bool ScsiCommand(SafeFileHandle h, ReadOnlySpan<byte> cdb, Span<byte> data, bool dataIn, int timeoutSeconds, out byte scsiStatus)
        {
            scsiStatus = 0;

            var sptd = new SCSI_PASS_THROUGH_DIRECT
            {
                Length = (ushort)Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>(),
                SenseInfoLength = 0,
                DataIn = (byte)(dataIn ? 1 : 0),
                DataTransferLength = (uint)data.Length,
                TimeOutValue = (uint)Math.Max(3, timeoutSeconds),
                DataBuffer = IntPtr.Zero,
                SenseInfoOffset = 0,
                PathId = 0,
                TargetId = 0,
                Lun = 0
            };
            SetCdb(ref sptd, cdb);

            // マネージド配列を GCHandle で pin し、アンマネージドポインタとして SPTD に設定
            var arr = data.ToArray();
            var handle = GCHandle.Alloc(arr, GCHandleType.Pinned);
            try
            {
                sptd.DataBuffer = handle.AddrOfPinnedObject();

                var outSptd = sptd;
                if (!DeviceIoControl(h, IOCTL_SCSI_PASS_THROUGH_DIRECT,
                                     ref sptd, Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>(),
                                     ref outSptd, Marshal.SizeOf<SCSI_PASS_THROUGH_DIRECT>(),
                                     out uint _, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DeviceIoControl(SPTD) failed");
                }
                scsiStatus = outSptd.ScsiStatus;

                // DeviceIoControl の結果（ドライブからの応答データ）を呼び出し元の Span に反映
                arr.AsSpan().CopyTo(data);
                return true;
            }
            finally
            {
                handle.Free();
                arr.AsSpan().CopyTo(data);
            }
        }

        // ===== データモデル =====

        /// <summary>TOC のトラック情報: トラック番号・開始 LBA・Control/ADR フィールド。</summary>
        public sealed class TocTrack
        {
            public int TrackNumber { get; set; }
            public int StartLba { get; set; }
            public byte Control { get; set; }
            public byte Adr { get; set; }
        }

        /// <summary>CD-Text の 18 バイトパック 1 個分: PackType (0x80=Title 等)・トラック番号・12 バイトペイロード。</summary>
        public sealed class CdTextPack
        {
            public byte PackType { get; set; }     // 0x80=Title, 0x81=Performer, ...
            public byte TrackNumber { get; set; }  // 0=アルバム単位
            public byte Sequence { get; set; }
            public byte Block { get; set; }        // 上位4bit
            public byte Charset { get; set; }      // 下位4bit
            public byte[] Payload12 { get; set; } = Array.Empty<byte>();
        }

        /// <summary>CD-Text パックを再構成した結果: アルバム情報 + トラック別情報の辞書。</summary>
        public sealed class CdTextCatalog
        {
            public Dictionary<string, string> Album { get; } = new();
            public Dictionary<int, Dictionary<string, string>> Tracks { get; } = new();

            public string GetTrackField(int track, string key)
            {
                if (Tracks.TryGetValue(track, out var dict) && dict.TryGetValue(key, out var val))
                    return val;
                return string.Empty;
            }
        }

        // ===== SCSI コマンド発行メソッド =====

        /// <summary>
        /// READ TOC/PMA/ATIP (0x43) Format=0x00 を発行し、全トラックの TOC を取得する。
        /// トラック番号 0xAA は Lead-Out を表す。
        /// </summary>
        public static List<TocTrack> ReadToc(SafeFileHandle h)
        {
            // TOC 応答バッファ: 4 バイトヘッダ + 最大 100 エントリ × 8 バイト = 804
            var alloc = 4 + 804;
            var buf = new byte[alloc];

            // CDB (Command Descriptor Block) の構築: READ TOC, Format=0, MSF=0
            var cdb = new byte[10];
            cdb[0] = 0x43;   // READ TOC/PMA/ATIP
            cdb[1] = 0x00;   // MSF=0
            cdb[2] = 0x00;   // Format=TOC
            cdb[7] = (byte)((alloc >> 8) & 0xFF);
            cdb[8] = (byte)(alloc & 0xFF);

            if (!ScsiCommand(h, cdb, buf, dataIn: true, timeoutSeconds: 5, out _))
                throw new IOException("SCSI TOC read failed.");

            // 応答の先頭 2 バイトがデータ長（ビッグエンディアン）
            int dataLen = (buf[0] << 8) | buf[1];
            int pos = 4;
            var list = new List<TocTrack>();
            while (pos + 8 <= Math.Min(buf.Length, dataLen + 2))
            {
                byte adrCtl = buf[pos + 1];
                byte track = buf[pos + 2];
                int lba = (buf[pos + 4] << 24) | (buf[pos + 5] << 16) | (buf[pos + 6] << 8) | buf[pos + 7];

                list.Add(new TocTrack
                {
                    TrackNumber = track,
                    Control = (byte)((adrCtl >> 4) & 0x0F),
                    Adr = (byte)(adrCtl & 0x0F),
                    StartLba = lba
                });
                pos += 8;
            }
            list.Sort((a, b) => a.TrackNumber.CompareTo(b.TrackNumber));
            return list;
        }

        /// <summary>
        /// READ SUB-CHANNEL (0x42) DataFormat=0x02 を発行し、メディアカタログ番号 (MCN/EAN) を取得する。
        /// MCN は 13 桁の数字（JAN/EAN コード）で、CD のバーコードに対応する。
        /// </summary>
        public static string? ReadMediaCatalogNumber(SafeFileHandle h)
        {
            var cdb = new byte[10];
            cdb[0] = 0x42;
            cdb[1] = 0x40; // SubQ=1
            cdb[2] = 0x02; // Media Catalog
            cdb[3] = 0x00; // Track=0
            cdb[8] = 0x3C; // 60bytes

            var buf = new byte[0x3C];
            if (!ScsiCommand(h, cdb, buf, dataIn: true, timeoutSeconds: 5, out _))
                return null;

            var ascii = Encoding.ASCII.GetString(buf);
            var digits = new string(ascii.Where(ch => char.IsDigit(ch)).ToArray());
            return string.IsNullOrWhiteSpace(digits) ? null : digits;
        }

        /// <summary>
        /// READ SUB-CHANNEL (0x42) DataFormat=0x03 を発行し、指定トラックの ISRC を取得する。
        /// ISRC (International Standard Recording Code) は 12 文字の英数字。
        /// </summary>
        public static string? ReadIsrcForTrack(SafeFileHandle h, byte trackNumber)
        {
            var cdb = new byte[10];
            cdb[0] = 0x42;
            cdb[1] = 0x40; // SubQ=1
            cdb[2] = 0x03; // ISRC
            cdb[3] = trackNumber;
            cdb[8] = 0x3C; // 60bytes

            var buf = new byte[0x3C];
            if (!ScsiCommand(h, cdb, buf, dataIn: true, timeoutSeconds: 5, out _))
                return null;

            var ascii = Encoding.ASCII.GetString(buf).ToUpperInvariant();
            var filtered = System.Text.RegularExpressions.Regex.Replace(ascii, @"[^A-Z0-9]", "");
            return (filtered.Length >= 12) ? filtered.Substring(0, 12) : null;
        }

        /// <summary>
        /// READ TOC/PMA/ATIP (0x43) Format=0x05 を発行し、CD-Text の生パック列を取得する。
        /// 1 パック = 18 バイト（PackType 1B + TrackNo 1B + SeqNo 1B + CharPos 1B + Payload 12B + CRC 2B）。
        /// </summary>
        public static List<CdTextPack> ReadCdTextPacks(SafeFileHandle h)
        {
            var alloc = 8192; // 十分大きめ
            var buf = new byte[alloc];

            var cdb = new byte[10];
            cdb[0] = 0x43;
            cdb[1] = 0x00;   // MSF=0
            cdb[2] = 0x05;   // Format=CD-Text
            cdb[7] = (byte)((alloc >> 8) & 0xFF);
            cdb[8] = (byte)(alloc & 0xFF);

            if (!ScsiCommand(h, cdb, buf, dataIn: true, timeoutSeconds: 5, out _))
                return new List<CdTextPack>();

            int length = (buf[0] << 8) | buf[1];
            int end = Math.Min(length + 2, buf.Length);

            var list = new List<CdTextPack>();
            for (int pos = 4; pos + 18 <= end; pos += 18)
            {
                byte packType = buf[pos + 0];
                byte trackNo = buf[pos + 1];
                byte seqNo = buf[pos + 2];
                byte charpos = buf[pos + 3];
                byte block = (byte)(charpos >> 4);
                byte charset = (byte)(charpos & 0x0F);

                var payload = new byte[12];
                Buffer.BlockCopy(buf, pos + 4, payload, 0, 12);

                list.Add(new CdTextPack
                {
                    PackType = packType,
                    TrackNumber = trackNo,
                    Sequence = seqNo,
                    Block = block,
                    Charset = charset,
                    Payload12 = payload
                });
            }
            return list;
        }

        // ===== CD-Text パック列の再構成（パック → 文字列の復元）=====
        /// <summary>CD-Text PackType (0x80〜0x8F) をフィールド名文字列に変換する。</summary>
        private static string FieldNameFromPackType(byte packType) => packType switch
        {
            0x80 => "Title",
            0x81 => "Performer",
            0x82 => "Songwriter",
            0x83 => "Composer",
            0x84 => "Arranger",
            0x85 => "Message",
            0x86 => "DiscId",
            0x87 => "Genre",
            0x88 => "TocInfo",
            0x8E => "SecondToc",
            0x8F => "TocInfo2",
            _ => $"Pack0x{packType:X2}"
        };

        /// <summary>
        /// CD-Text パック列をフィールド別・トラック別に再構成し、デコード済みカタログを返す。
        /// 同一 (PackType, TrackNo) のパックを Block 順・Sequence 順に連結してデコードする。
        /// </summary>
        public static CdTextCatalog BuildCdTextCatalog(List<CdTextPack> packs)
        {
            var catalog = new CdTextCatalog();

            // (PackType, TrackNo) でグループ化し、さらに Block 番号で分割してシーケンス順にソート
            var grouping = packs
                .GroupBy(p => (p.PackType, p.TrackNumber))
                .ToDictionary(g => g.Key,
                    g => g
                        .GroupBy(x => x.Block)
                        .OrderBy(x => x.Key)
                        .ToDictionary(
                            bg => bg.Key,
                            bg => bg.OrderBy(x => x.Sequence).ToList()
                        ));

            // 各 (PackType, TrackNo) グループについて、ペイロードを連結 → デコード → カタログに格納
            foreach (var kv in grouping)
            {
                var (packType, trackNo) = kv.Key;
                var blocks = kv.Value;

                var payloadAll = new List<byte>();
                int charset = 0;
                bool charsetSet = false;

                foreach (var b in blocks.OrderBy(b => b.Key))
                {
                    foreach (var p in b.Value)
                    {
                        if (!charsetSet) { charset = p.Charset; charsetSet = true; }
                        payloadAll.AddRange(p.Payload12);
                    }
                }

                // 末尾の NULL パディングを除去
                var parts = payloadAll.ToArray();
                int nonZeroTail = parts.Length - 1;
                while (nonZeroTail >= 0 && parts[nonZeroTail] == 0x00) nonZeroTail--;
                if (nonZeroTail >= 0) parts = parts.AsSpan(0, nonZeroTail + 1).ToArray();

                // charset ニブルに応じてデコード（2=Shift_JIS, その他=Latin-1）
                string decoded = DecodeCdText(parts, charset);
                var fields = decoded.Split('\0').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                string value = fields.Count == 0 ? "" : fields[0];

                string fieldName = FieldNameFromPackType(packType);

                if (trackNo == 0)
                {
                    if (!catalog.Album.ContainsKey(fieldName))
                        catalog.Album[fieldName] = value;
                    else if (!string.IsNullOrWhiteSpace(value) && catalog.Album[fieldName] != value)
                        catalog.Album[fieldName] += " / " + value;
                }
                else
                {
                    if (!catalog.Tracks.TryGetValue(trackNo, out var dict))
                    {
                        dict = new Dictionary<string, string>();
                        catalog.Tracks[trackNo] = dict;
                    }
                    if (!dict.ContainsKey(fieldName))
                        dict[fieldName] = value;
                    else if (!string.IsNullOrWhiteSpace(value) && dict[fieldName] != value)
                        dict[fieldName] += " / " + value;
                }
            }

            return catalog;
        }
    }
}
