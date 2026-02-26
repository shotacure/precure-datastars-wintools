#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static PrecureDataStars.BDAnalyzer.MplsParser;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// Blu-ray .mpls（BDMV/PLAYLIST/xxxx.mpls）からチャプター境界を抽出（軽量版）。
    /// ・時間基準: 45 kHz tick (1/45000 秒)
    /// ・章境界: PlayListMark の Entry(1) + Link(2) を優先採用
    /// ・章が実質 1 個以下:
    ///     1) 同フォルダの「次番号」MPLS（例: 00000→00001）を試す
    ///     2) それでもダメなら同フォルダをスイープして「総尺が近い(±max(3s, 2%)) & マーク数≥2」のMPLSを採用
    /// ・Seconds: 四捨五入（AwayFromZero）
    /// ・先頭/末尾は -1 秒、最終章は最低 1 秒、末尾ダミーマーク除去・単調増加補正あり
    /// </summary>
    public static class MplsParser
    {
        /// <summary>Blu-ray 時間基準: 45 kHz (1 tick = 1/45000 秒)。</summary>
        private const int TicksPerSecond = 45000;

        /// <summary>1 チャプターの情報: 開始位置・尺・四捨五入した秒数。</summary>
        public sealed record Chapter(TimeSpan Start, TimeSpan Length, int SecondsRounded);

        /// <summary>MPLS 解析結果: チャプターリスト・総尺・PlayItem 数・マーク数。</summary>
        public sealed record ParseResult(
            List<Chapter> Chapters,
            TimeSpan PlaylistDuration,
            int PlayItemCount,
            int MarkCount
        );

        /// <summary>PlayItem: IN/OUT タイムスタンプ（45kHz tick）。1 ストリーム区間を表す。</summary>
        private sealed record PlayItem(uint In, uint Out);
        /// <summary>PlayListMark: PlayItem 内のタイムスタンプ。Type=1(Entry)/2(Link) がチャプター境界。</summary>
        private sealed record PlaylistMark(int PlayItemIdRaw, uint TimeStamp, byte Type, uint Duration);

        /// <summary>マーク種別が章境界（Entry=1 または Link=2）であるか判定する。</summary>
        private static bool IsChapterMark(byte type) => type == 1 || type == 2;

        /// <summary>
        /// MPLS ファイルを解析し、チャプター情報を返す。
        /// 章が 1 個以下の場合は次番号 MPLS → 同フォルダスイープの順にフォールバックする。
        /// </summary>
        /// <param name="mplsPath">MPLS ファイルパス（例: BDMV/PLAYLIST/00000.mpls）。</param>
        public static ParseResult Parse(string mplsPath)
        {
            // ---- まず自分を読む
            if (!TryExtractFromMpls(mplsPath, out var items, out var chapterMarks, out var playlistTicks, out var playlistDuration, out var markCount))
            {
                return new ParseResult(new(), TimeSpan.Zero, 0, 0);
            }

            // ---- 章が 1 個以下の場合のフォールバック戦略 ----
            // Step 1: ファイル名の数字を +1 した MPLS（例: 00000.mpls → 00001.mpls）を試す
            // Step 2: 同フォルダ内の全 MPLS をスイープし、総尺が近く(±max(3秒,2%))マーク≥2 のものを採用
            if (chapterMarks.Count < 2)
            {
                string? nextMpls = TryNextNumberedMplsPath(mplsPath);
                if (nextMpls != null && File.Exists(nextMpls))
                {
                    if (TryExtractFromMpls(nextMpls, out var i2, out var m2, out var ticks2, out var dur2, out var mc2) &&
                        m2.Count >= 2)
                    {
                        items = i2; chapterMarks = m2; playlistTicks = ticks2; playlistDuration = dur2; markCount = mc2;
                    }
                }
            }
            if (chapterMarks.Count < 2)
            {
                var (i3, m3, ticks3, dur3, _) = SweepForBetterMpls(mplsPath, playlistTicks);
                if (m3.Count >= 2)
                {
                    items = i3; chapterMarks = m3; playlistTicks = ticks3; playlistDuration = dur3;
                }
            }

            if (chapterMarks.Count == 0)
                return new ParseResult(new(), playlistDuration, items.Count, markCount);

            // ---- PlayItem の累積オフセット計算 ----
            // 複数 PlayItem にまたがるマーク位置を絶対 tick に変換するために、
            // 各 PlayItem の開始位置までの累積 tick を事前計算する
            var itemOffsets = new long[items.Count];
            long acc = 0;
            for (int i = 0; i < items.Count; i++)
            {
                itemOffsets[i] = acc;
                if (items[i].Out > items[i].In) acc += (long)items[i].Out - (long)items[i].In;
            }

            // ---- 各チャプターマークの開始位置をプレイリスト全体での絶対 tick に変換 ----
            var startTicks = new List<long>(chapterMarks.Count);
            for (int i = 0; i < chapterMarks.Count; i++)
            {
                var m = chapterMarks[i];
                int idx = -1;
                if (m.PlayItemIdRaw < items.Count) idx = m.PlayItemIdRaw;
                else if (m.PlayItemIdRaw > 0 && (m.PlayItemIdRaw - 1) < items.Count) idx = m.PlayItemIdRaw - 1;

                long abs;
                if (items.Count == 1) abs = (long)m.TimeStamp;
                else if (idx >= 0)
                {
                    var it = items[idx];
                    long local = (long)m.TimeStamp - (long)it.In;
                    if (local < 0) local = 0;
                    abs = itemOffsets[idx] + local;
                }
                else abs = playlistTicks;

                if (abs < 0) abs = 0;
                startTicks.Add(abs);
            }

            // ---- 末尾ダミーマーク除去（総尺以降に配置された無効マークを削除）----
            while (startTicks.Count >= 2 &&
                   startTicks[^1] >= playlistTicks &&
                   startTicks[^2] < playlistTicks)
            {
                startTicks.RemoveAt(startTicks.Count - 1);
                chapterMarks.RemoveAt(chapterMarks.Count - 1);
            }

            // ---- 単調増加補正（タイムスタンプの逆転を +1 tick で修正）----
            for (int i = 1; i < startTicks.Count; i++)
                if (startTicks[i] <= startTicks[i - 1]) startTicks[i] = startTicks[i - 1] + 1;

            if (startTicks.Count == 0)
                return new ParseResult(new(), playlistDuration, items.Count, markCount);

            // ---- 各チャプターの尺 (tick) を計算 ----
            // 最終チャプター以外: 次のチャプター開始位置との差分
            // 最終チャプター: Mark の Duration → PlayItem 残り → 総尺からの差分の順でフォールバック
            var lenTicks = new List<long>(startTicks.Count);
            for (int i = 0; i < startTicks.Count; i++)
            {
                if (i < startTicks.Count - 1)
                {
                    long len = startTicks[i + 1] - startTicks[i];
                    if (len < 0) len = 0;
                    lenTicks.Add(len);
                }
                else
                {
                    long lastStart = startTicks[i];
                    long len;
                    if (chapterMarks[^1].Duration > 0) len = Math.Max(0, (long)chapterMarks[^1].Duration);
                    else
                    {
                        // “最後のマーク以降”の残り（現在のPlayItemの残り + 後続PlayItem総和）
                        var lastRaw = chapterMarks[^1].PlayItemIdRaw;
                        int lastIdx = -1;
                        if (lastRaw < items.Count) lastIdx = lastRaw;
                        else if (lastRaw > 0 && (lastRaw - 1) < items.Count) lastIdx = lastRaw - 1;

                        long tail = 0;
                        if (lastIdx >= 0)
                        {
                            var cur = items[lastIdx];
                            if (cur.Out > cur.In)
                            {
                                long baseStamp = Math.Max((long)cur.In, (long)chapterMarks[^1].TimeStamp);
                                if ((long)cur.Out > baseStamp) tail += (long)cur.Out - baseStamp;
                            }
                            for (int j = lastIdx + 1; j < items.Count; j++)
                            {
                                var it = items[j];
                                if (it.Out > it.In) tail += (long)it.Out - (long)it.In;
                            }
                        }
                        else tail = 0;

                        len = tail;
                        if (len <= 0)
                        {
                            long byTotal = playlistTicks - lastStart;
                            if (byTotal > 0) len = byTotal;
                        }
                    }
                    if (len < TicksPerSecond) len = TicksPerSecond; // 最終チャプターは最低 1 秒を保証
                    lenTicks.Add(len);
                }
            }

            // ---- Chapter オブジェクトの組み立て ----
            // 秒数は MidpointRounding.AwayFromZero で四捨五入
            // 先頭/末尾チャプターおよび特定パターン一致時は -1 秒の補正を行う
            var chapters = new List<Chapter>(startTicks.Count);
            for (int i = 0; i < startTicks.Count; i++)
            {
                var startTs = TimeSpan.FromSeconds(startTicks[i] / (double)TicksPerSecond);
                var lenTs = TimeSpan.FromSeconds(lenTicks[i] / (double)TicksPerSecond);

                int secsRounded = (int)Math.Round(lenTs.TotalSeconds, 0, MidpointRounding.AwayFromZero);

                var secondsHistory = chapters.Select(c => c.SecondsRounded).ToList();

                if (ShouldMinusOneForChapter(i, secsRounded, secondsHistory, chapterMarks.Count))
                {
                    // 所定の条件に合致した場合は -1 秒
                    secsRounded = Math.Max(0, secsRounded - 1);
                }

                chapters.Add(new Chapter(startTs, lenTs, secsRounded));
            }

            return new ParseResult(chapters, playlistDuration, items.Count, markCount);
        }

        // ---- ここから内部実装（MPLS バイナリ読み取り・フォールバック処理） ----

        /// <summary>
        /// MPLS ファイルのバイナリ構造を読み取り、PlayItem リスト・チャプターマーク・総尺を返す。
        /// </summary>
        private static bool TryExtractFromMpls(
            string mpls,
            out List<PlayItem> items,
            out List<PlaylistMark> chMarks,
            out long playlistTicks,
            out TimeSpan playlistDuration,
            out int markCount)
        {
            items = new(); chMarks = new(); markCount = 0;
            playlistTicks = 0; playlistDuration = TimeSpan.Zero;

            try
            {
                using var fs = File.OpenRead(mpls);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

                // MPLS ファイルヘッダの検証（先頭 4 バイトが "MPLS" であること）
                var head = br.ReadBytes(8);
                if (!Encoding.ASCII.GetString(head).StartsWith("MPLS")) return false;

                // PlayList セクション / PlayListMark セクションの開始オフセットを取得
                uint playListStart = ReadU32BE(br);
                uint playMarkStart = ReadU32BE(br);

                // PlayList セクション: PlayItem（ストリーム区間）の一覧を読み取り
                fs.Position = playListStart;
                uint playListLength = ReadU32BE(br);
                br.ReadBytes(2);
                ushort playItemCount = ReadU16BE(br);
                ushort _subPathCount = ReadU16BE(br);

                long bodyEnd = playListStart + 4 + playListLength;
                for (int i = 0; i < playItemCount; i++)
                {
                    if (fs.Position + 2 > bodyEnd) break;
                    long itemStart = fs.Position;
                    ushort playItemLength = ReadU16BE(br);
                    long itemEnd = itemStart + 2 + playItemLength;
                    if (itemEnd > bodyEnd || itemEnd > fs.Length) { fs.Position = Math.Min(bodyEnd, fs.Length); break; }

                    br.ReadBytes(2);         // reserved
                    br.ReadBytes(9);         // clip name(5)+id(4) 読み捨て
                    br.ReadByte();           // flags
                    uint ins = ReadU32BE(br);
                    uint outs = ReadU32BE(br);
                    items.Add(new PlayItem(ins, outs));

                    if (outs > ins) playlistTicks += (long)outs - (long)ins;
                    fs.Position = itemEnd;
                }
                playlistDuration = TimeSpan.FromSeconds(playlistTicks / (double)TicksPerSecond);

                // PlayListMark セクション: チャプター境界のタイムスタンプ一覧を読み取り
                fs.Position = playMarkStart;
                uint markLength = ReadU32BE(br);
                ushort mcount = ReadU16BE(br);
                markCount = mcount;

                long markEnd = playMarkStart + 4 + markLength;
                for (int i = 0; i < mcount; i++)
                {
                    if (fs.Position + 14 > markEnd) break;
                    br.ReadByte();
                    byte type = br.ReadByte();
                    ushort id = ReadU16BE(br);
                    uint stamp = ReadU32BE(br);
                    br.ReadUInt16();
                    uint dur = ReadU32BE(br);
                    if (IsChapterMark(type)) chMarks.Add(new PlaylistMark(id, stamp, type, dur));
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 同フォルダ内の全 MPLS をスキャンし、総尺が近い（±max(3秒,2%)）かつ
        /// マーク数が最も多いファイルを代替として返す。
        /// </summary>
        private static (List<PlayItem>, List<PlaylistMark>, long, TimeSpan, int) SweepForBetterMpls(string mplsPath, long selfTicks)
        {
            var empty = (new List<PlayItem>(), new List<PlaylistMark>(), 0L, TimeSpan.Zero, 0);
            try
            {
                var dir = Path.GetDirectoryName(mplsPath) ?? "";
                var self = Path.GetFileName(mplsPath);

                double selfSec = selfTicks / (double)TicksPerSecond;
                double tol = Math.Max(3.0, selfSec * 0.02);

                string? best = null; int bestMarks = 0;
                foreach (var cand in Directory.EnumerateFiles(dir, "*.mpls"))
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(cand), self)) continue;
                    if (!TryQuickReadMarks(cand, out var mcount, out var sec)) continue;
                    if (mcount >= 2 && Math.Abs(sec - selfSec) <= tol)
                    {
                        if (mcount > bestMarks) { bestMarks = mcount; best = cand; }
                    }
                }
                if (best != null && TryExtractFromMpls(best, out var items, out var marks, out var ticks, out var dur, out var _))
                    return (items, marks, ticks, dur, bestMarks);
            }
            catch { /* ignore */ }
            return empty;
        }

        /// <summary>
        /// ファイル名が 5 桁数字（例: 00000.mpls）の場合、番号を +1 したパスを返す。
        /// </summary>
        private static string? TryNextNumberedMplsPath(string mplsPath)
        {
            try
            {
                var dir = Path.GetDirectoryName(mplsPath) ?? "";
                var name = Path.GetFileNameWithoutExtension(mplsPath);
                var m = Regex.Match(name, @"^(?<num>\d{5})$");
                if (!m.Success) return null;
                int n = int.Parse(m.Groups["num"].Value);
                if (n >= 99999) return null;
                string next = (n + 1).ToString("00000");
                string cand = Path.Combine(dir, next + ".mpls");
                return cand;
            }
            catch { return null; }
        }

        /// <summary>
        /// MPLS の総尺（秒）とマーク数だけを軽量に読み取る（フォールバック候補の事前スクリーニング用）。
        /// </summary>
        private static bool TryQuickReadMarks(string mpls, out int markCount, out double seconds)
        {
            markCount = 0; seconds = 0;
            try
            {
                using var fs = File.OpenRead(mpls);
                using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
                var head = br.ReadBytes(8);
                if (!Encoding.ASCII.GetString(head).StartsWith("MPLS")) return false;

                uint playListStart = ReadU32BE(br);
                uint playMarkStart = ReadU32BE(br);

                // PlayList 総尺
                fs.Position = playListStart;
                uint playListLength = ReadU32BE(br);
                br.ReadBytes(2);
                ushort playItemCount = ReadU16BE(br);
                ushort _subPathCount = ReadU16BE(br);

                long bodyEnd = playListStart + 4 + playListLength;
                long ticks = 0;
                for (int i = 0; i < playItemCount; i++)
                {
                    if (fs.Position + 2 > bodyEnd) break;
                    long itemStart = fs.Position;
                    ushort playItemLength = ReadU16BE(br);
                    long itemEnd = itemStart + 2 + playItemLength;
                    if (itemEnd > bodyEnd || itemEnd > fs.Length) { fs.Position = Math.Min(bodyEnd, fs.Length); break; }

                    br.ReadBytes(2);  // reserved
                    br.ReadBytes(9);  // clip name(5)+id(4) 読み捨て
                    br.ReadByte();    // flags
                    uint ins = ReadU32BE(br);
                    uint outs = ReadU32BE(br);
                    if (outs > ins) ticks += (long)outs - (long)ins;

                    fs.Position = itemEnd;
                }
                seconds = ticks / (double)TicksPerSecond;

                // Mark 数
                fs.Position = playMarkStart;
                uint markLength = ReadU32BE(br);
                markCount = ReadU16BE(br);
                return true;
            }
            catch { return false; }
        }

        // ---- ビッグエンディアン読み取りヘルパー ----
        /// <summary>ビッグエンディアン 16 ビット符号なし整数を読み取る。</summary>
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
        /// 直近の並びがパターンに一致するか判定する
        /// </summary>
        /// <param name="history"></param>
        /// <param name="endIndexInclusive"></param>
        /// <param name="pattern"></param>
        /// <param name="currentSecsRounded"></param>
        /// <returns></returns>
        static bool EndsWithPatternWithCurrent(
            IReadOnlyList<int> history, // chapters.Select(c => c.SecondsRounded).ToList()
            int endIndexInclusive,      // i
            IReadOnlyList<int> pattern, // 例: [90,31]
            int currentSecsRounded)     // secsRounded（まだhistory[i]に反映していない現在値）
        {
            int k = pattern.Count;
            if (k == 0) return false;
            int start = endIndexInclusive - (k - 1);
            if (start < 0) return false;

            // 末尾(パターン最後)は currentSecsRounded と比較
            if (pattern[k - 1] != currentSecsRounded) return false;

            // 末尾以外は history（過去の確定値）と照合
            for (int j = 0; j < k - 1; j++)
            {
                if (history[start + j] != pattern[j]) return false;
            }
            return true;
        }

        /// <summary>
        /// 秒数調整判定
        /// </summary>
        /// <param name="i"></param>
        /// <param name="secsRounded"></param>
        /// <param name="secondsHistory"></param>
        /// <param name="chapterCount"></param>
        /// <returns></returns>
        static bool ShouldMinusOneForChapter(
            int i,
            int secsRounded,
            IReadOnlyList<int> secondsHistory, // chapters.Select(c => c.SecondsRounded).ToList()
            int chapterCount)
        {
            // 先頭/末尾の黒味1秒は削除
            if (i == 0 || i == chapterCount - 1) return true;

            // 5チャプター目以降のみ発動
            if (i < 4) return false;

            // シリーズごとの発動パターン
            var patterns = new List<int[]>
            {
                // TODO: 逐次実行されると初期シリーズの61秒アバンや31秒アバンを誤検知してしまう
                new[] { 90, 31 },     // 2004～2016・2018: ED90秒・予告31秒という並びの場合、最後の予告31秒を-1
                new[] { 90, 30, 61 }, // 2017: ED90秒・予告30秒・コーナー61秒という並びの場合、最後のコーナー61秒を-1
                new[] { 90, 30, 31 }, // 2017: ED90秒・予告30秒・コーナー31秒という並びの場合、最後のコーナー31秒を-1
                new[] { 90, 20, 16 }, // 2019～2020: ED90秒・予告20秒・コーナー16秒という並びの場合、最後のコーナー16秒を-1
                new[] { 100, 21 },    // 2021～2023: ED100秒・予告21秒という並びの場合、最後の予告21秒を-1
                new[] { 90, 21 },     // 2024・2026: ED90秒・予告21秒という並びの場合、最後の予告21秒を-1
                new[] { 90, 20, 11 }, // 2025: ED90秒・予告20秒・コーナー11秒という並びの場合、最後のコーナー11秒を-1
            };

            foreach (var p in patterns)
            {
                if (EndsWithPatternWithCurrent(secondsHistory, i, p, secsRounded))
                    return true;
            }
            return false;
        }
    }
}
