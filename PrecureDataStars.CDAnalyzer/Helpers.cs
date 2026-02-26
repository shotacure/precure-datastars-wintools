using System;
using System.Linq;
using System.Text;

namespace PrecureDataStars.CDAnalyzer
{
    /// <summary>
    /// CD-DA 解析で共通的に使用するユーティリティメソッド群。
    /// フレーム↔時間変換、MSF→LBA 変換、バイト列の16進表示、CD-Text デコード等を提供する。
    /// </summary>
    internal static class Helpers
    {
        /// <summary>
        /// CD-DA フレーム数を "MM:SS.FF" 形式の文字列に変換する。
        /// CD-DA の 1 秒は 75 フレーム（= 75 セクタ = 176,400 バイト）。
        /// </summary>
        public static string FramesToTimeString(int frames)
        {
            int totalSeconds = frames / 75;
            int ff = frames % 75;
            int mm = totalSeconds / 60;
            int ss = totalSeconds % 60;
            return $"{mm:D2}:{ss:D2}.{ff:D2}";
        }

        /// <summary>
        /// MSF (Minutes/Seconds/Frames) アドレスを LBA (Logical Block Address) に変換する。
        /// LBA = (M×60 + S)×75 + F − 150（150 = 2 秒のプリギャップ）。
        /// </summary>
        public static int MsfcToLba(byte m, byte s, byte f)
            => ((m * 60 + s) * 75 + f) - 150;

        /// <summary>バイト列を大文字 16 進文字列に変換する（デバッグ用）。</summary>
        public static string BytesToHex(ReadOnlySpan<byte> bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.AppendFormat("{0:X2}", b);
            return sb.ToString();
        }

        /// <summary>NULL 文字 (\0) をスペースに置換し、前後の空白を除去する。</summary>
        public static string TrimNulls(string s) => s.Replace('\0', ' ').Trim();

        /// <summary>
        /// CD-Text のバイト列を文字列にデコードする。
        /// charset ニブル 2 = MS-JIS (Shift_JIS/CP932)、その他 = Latin-1 (ISO-8859-1)。
        /// デコード失敗時は Latin-1 にフォールバックする。
        /// </summary>
        public static string DecodeCdText(byte[] data, int charsetNibble /*0..15*/)
        {
            try
            {
                if (charsetNibble == 2) // MS-JIS (Shift_JIS) — 日本盤 CD に多い
                    return TrimNulls(Encoding.GetEncoding(932).GetString(data));
                // 既定: Latin-1
                return TrimNulls(Encoding.GetEncoding("iso-8859-1").GetString(data));
            }
            catch
            {
                return TrimNulls(Encoding.GetEncoding("iso-8859-1").GetString(data));
            }
        }
    }
}
