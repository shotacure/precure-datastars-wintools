#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PrecureDataStars.BDAnalyzer
{
    /// <summary>
    /// Blu-ray のチャプターに当たる区間を、STREAM/*.m2ts からそのまま切り出す。
    /// チャプターごとのファイルサイズ・ビットレートを計測するための機能で、再エンコードもタイムスタンプの付け直しもしない。
    /// <para>
    /// 位置の決め方：プレイリスト（.mpls）の PlayItem で、チャプターの区間（プレイリスト時間軸）をクリップごとの時刻に直し、
    /// クリップ情報（.clpi）の EP map（映像の I ピクチャの提示時刻 → ソースパケット番号 SPN）で m2ts の中の位置に直す。
    /// 区間の始まり・終わりとも「その時刻以前でいちばん近いエントリポイント」に揃える（チャプターのマークは普通エントリポイントに打たれているので、
    /// チャプターの境目と一致する）。PlayItem の頭から始まる区間が最初のエントリポイントに当たるときはクリップ（STC シーケンス）の頭から。
    /// PlayItem の終わりまで続く区間は、その PlayItem の OUT 時刻以降で最初のエントリポイント（無ければクリップの終わり）まで。
    /// これで隣り合うチャプターの切り出しは重ならず、すべてつなぐと元の区間に戻る。
    /// </para>
    /// <para>
    /// 1 つのチャプターが複数の PlayItem（クリップ）にまたがるときは、各クリップの該当部分を順に 1 つのファイルへつなぐ。
    /// m2ts は 192 バイトのソースパケット（4 バイトの TP_extra_header ＋ 188 バイトの TS パケット）の並びなので、位置は SPN × 192。
    /// マルチアングルは主アングルのクリップだけを見る。
    /// </para>
    /// </summary>
    public static class M2tsChapterCutter
    {
        /// <summary>Blu-ray 時間基準: 45 kHz (1 tick = 1/45000 秒)。PlayItem の IN/OUT と同じ単位。</summary>
        private const int TicksPerSecond = 45000;

        /// <summary>m2ts のソースパケット 1 個のバイト数。</summary>
        private const int SourcePacketSize = 192;

        /// <summary>
        /// 時刻の突き合わせで許す差（tick）。チャプターの時刻は <see cref="TimeSpan"/> を経由して戻すので丸めの誤差が乗る。
        /// エントリポイントの間隔（0.5〜1 秒程度）よりずっと小さい 1 ms にする。
        /// </summary>
        private const long ToleranceTicks = TicksPerSecond / 1000;

        /// <summary>
        /// EP map の提示時刻の刻み（tick）。細かいエントリは PTS（90 kHz）のビット 19〜9 しか持たないので、45 kHz では 256 tick 未満を切り捨てた値になる。
        /// 「ある時刻以降で最初のエントリポイント」を探すときは、この分だけ手前から探さないと 1 つ先のエントリポイントまで行き過ぎる。
        /// </summary>
        private const long EpPtsResolution = 256;

        /// <summary>切り出しの 1 片：あるクリップ（m2ts）の [<see cref="StartByte"/>, <see cref="EndByte"/>) のバイト範囲。</summary>
        public sealed record Segment(string ClipPath, long StartByte, long EndByte)
        {
            /// <summary>この片のバイト数。</summary>
            public long Length => EndByte - StartByte;
        }

        /// <summary>
        /// 同じ盤の中で、読んだプレイリストとクリップ情報を使い回すための入れ物。1 回の切り出し（複数チャプター）の間だけ持つ。
        /// </summary>
        public sealed class Cache
        {
            internal readonly Dictionary<string, List<PlayItem>> Playlists = new(StringComparer.OrdinalIgnoreCase);
            internal readonly Dictionary<string, ClipInfo> Clips = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>PlayItem：主アングルのクリップ名・STC 番号・IN/OUT（45 kHz）。</summary>
        internal sealed record PlayItem(string ClipName, byte StcId, long In, long Out);

        /// <summary>エントリポイント 1 個：提示時刻（45 kHz）とソースパケット番号。</summary>
        internal readonly record struct EntryPoint(long Pts, long Spn);

        /// <summary>クリップ情報のうち切り出しに使うもの：ソースパケット数・STC シーケンスの開始 SPN・EP map。</summary>
        internal sealed class ClipInfo
        {
            /// <summary>クリップのソースパケット数（ClipInfo の値。読めなければ 0）。</summary>
            public long SourcePackets { get; init; }
            /// <summary>STC 番号 → その STC シーケンスの開始 SPN。</summary>
            public Dictionary<int, long> StcSpnStart { get; init; } = new();
            /// <summary>EP map（SPN 昇順）。</summary>
            public List<EntryPoint> EntryPoints { get; init; } = new();
        }

        /// <summary>
        /// プレイリスト <paramref name="playlistFile"/> の [<paramref name="start"/>, <paramref name="start"/> + <paramref name="length"/>)
        /// （プレイリスト時間軸）に当たる m2ts のバイト範囲を、再生順に返す。
        /// </summary>
        /// <param name="bdmvRoot">BDMV フォルダのパス（PLAYLIST / CLIPINF / STREAM の親）。</param>
        /// <param name="playlistFile">プレイリストのファイル名（例: <c>00001.mpls</c>）。</param>
        /// <param name="start">チャプターの開始時刻（プレイリスト時間軸）。</param>
        /// <param name="length">チャプターの尺。</param>
        /// <param name="cache">読んだ管理ファイルの使い回し用。</param>
        public static List<Segment> PlanChapter(string bdmvRoot, string playlistFile, TimeSpan start, TimeSpan length, Cache cache)
        {
            if (!cache.Playlists.TryGetValue(playlistFile, out var items))
            {
                items = ReadPlayItems(Path.Combine(bdmvRoot, "PLAYLIST", playlistFile));
                cache.Playlists[playlistFile] = items;
            }

            long total = items.Sum(it => Math.Max(0, it.Out - it.In));
            long a = ToTicks(start);
            long b = ToTicks(start + length);
            if (b > total - ToleranceTicks) b = total; // 最後のチャプターはプレイリストの終わりまで
            if (a < 0) a = 0;

            var segments = new List<Segment>();
            long offset = 0;
            foreach (var it in items)
            {
                long itemLen = Math.Max(0, it.Out - it.In);
                long itemStart = offset, itemEnd = offset + itemLen;
                offset = itemEnd;

                long s = Math.Max(a, itemStart), e = Math.Min(b, itemEnd);
                if (e - s <= ToleranceTicks) continue; // 丸めでわずかに掛かっただけの PlayItem は外す

                if (!cache.Clips.TryGetValue(it.ClipName, out var clip))
                {
                    clip = ReadClipInfo(Path.Combine(bdmvRoot, "CLIPINF", it.ClipName + ".clpi"));
                    cache.Clips[it.ClipName] = clip;
                }
                string m2ts = Path.Combine(bdmvRoot, "STREAM", it.ClipName + ".m2ts");
                if (!File.Exists(m2ts))
                    throw new FileNotFoundException($"クリップ {it.ClipName}.m2ts が見つかりません。", m2ts);
                long fileSpn = new FileInfo(m2ts).Length / SourcePacketSize;

                var (eps, rangeStart, rangeEnd) = EntryPointsOfStc(clip, it.StcId, fileSpn);
                long spnStart = FloorSpn(eps, it.In + (s - itemStart) + ToleranceTicks, rangeStart);
                // PlayItem の頭から始まる区間が最初のエントリポイントに当たるときは、その前のパケット（PAT・PMT など）も含めて範囲の頭から写す
                if (s - itemStart <= ToleranceTicks && eps.Count > 0 && spnStart == eps[0].Spn) spnStart = rangeStart;
                long spnEnd = itemEnd - e <= ToleranceTicks
                    ? CeilSpn(eps, it.Out - EpPtsResolution, rangeEnd)
                    : FloorSpn(eps, it.In + (e - itemStart) + ToleranceTicks, rangeStart);
                spnEnd = Math.Min(spnEnd, fileSpn);
                if (spnEnd <= spnStart) continue;

                segments.Add(new Segment(m2ts, spnStart * SourcePacketSize, spnEnd * SourcePacketSize));
            }
            return segments;
        }

        /// <summary>
        /// <paramref name="segments"/> を順に <paramref name="outputPath"/> へ書き出す。いったん <c>.part</c> に書き、終わったら名前を付け替える
        /// （中止・失敗のときは <c>.part</c> を消す）。<paramref name="progress"/> にはこの呼び出しで書いたバイト数の累計を渡す。
        /// </summary>
        public static async Task WriteAsync(IReadOnlyList<Segment> segments, string outputPath, IProgress<long>? progress, CancellationToken ct)
        {
            string partPath = outputPath + ".part";
            const int BufferSize = 4 * 1024 * 1024;
            var buffer = new byte[BufferSize];
            long written = 0;
            try
            {
                // 書き出しのループは UI スレッドに戻さずに回す（進み具合は progress が UI スレッドへ届ける）
                using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
                {
                    foreach (var seg in segments)
                    {
                        using var src = new FileStream(seg.ClipPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                        src.Position = seg.StartByte;
                        long remaining = seg.Length;
                        while (remaining > 0)
                        {
                            int want = (int)Math.Min(buffer.Length, remaining);
                            int read = await src.ReadAsync(buffer.AsMemory(0, want), ct).ConfigureAwait(false);
                            if (read <= 0) throw new EndOfStreamException($"{Path.GetFileName(seg.ClipPath)} が途中で終わっています。");
                            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            remaining -= read;
                            written += read;
                            progress?.Report(written);
                        }
                    }
                }
                File.Move(partPath, outputPath, overwrite: true);
            }
            catch
            {
                try { File.Delete(partPath); } catch { /* 消せなくても元の例外を優先する */ }
                throw;
            }
        }

        // ---- 位置の引き当て ----

        /// <summary>
        /// PlayItem の STC 番号に当たる STC シーケンスの EP と、その SPN の範囲 [rangeStart, rangeEnd) を返す。
        /// STC シーケンスが 1 つか、番号が見つからないときはクリップ全体。
        /// </summary>
        private static (List<EntryPoint> Eps, long RangeStart, long RangeEnd) EntryPointsOfStc(ClipInfo clip, byte stcId, long fileSpn)
        {
            long clipEnd = clip.SourcePackets > 0 ? Math.Min(clip.SourcePackets, fileSpn) : fileSpn;
            if (clip.StcSpnStart.Count <= 1 || !clip.StcSpnStart.TryGetValue(stcId, out long rangeStart))
                return (clip.EntryPoints, 0, clipEnd);
            long rangeEnd = clip.StcSpnStart.Values.Where(v => v > rangeStart).DefaultIfEmpty(clipEnd).Min();
            var eps = clip.EntryPoints.Where(ep => ep.Spn >= rangeStart && ep.Spn < rangeEnd).ToList();
            return (eps, rangeStart, rangeEnd);
        }

        /// <summary>提示時刻が <paramref name="pts"/> 以下でいちばん遅いエントリポイントの SPN。無ければ <paramref name="fallback"/>。</summary>
        private static long FloorSpn(List<EntryPoint> eps, long pts, long fallback)
        {
            long spn = fallback;
            foreach (var ep in eps)
            {
                if (ep.Pts > pts) break;
                spn = ep.Spn;
            }
            return spn;
        }

        /// <summary>提示時刻が <paramref name="pts"/> 以上でいちばん早いエントリポイントの SPN。無ければ <paramref name="fallback"/>（範囲の終わり）。</summary>
        private static long CeilSpn(List<EntryPoint> eps, long pts, long fallback)
        {
            foreach (var ep in eps)
                if (ep.Pts >= pts) return ep.Spn;
            return fallback;
        }

        private static long ToTicks(TimeSpan ts) => (long)Math.Round(ts.TotalSeconds * TicksPerSecond, MidpointRounding.AwayFromZero);

        // ---- MPLS ----

        /// <summary>
        /// MPLS の PlayItem を読む。先頭：識別子 "MPLS"(4) → 版(4) → PlayList の開始位置(4) → …。
        /// PlayList：長さ(4) → 予約(2) → PlayItem 数(2) → SubPath 数(2)。
        /// PlayItem：長さ(2) → クリップ名(5) → 形式識別子(4) → 予約と multi-angle・connection_condition(2) → STC 番号(1) → in(4) → out(4) → …。
        /// </summary>
        private static List<PlayItem> ReadPlayItems(string mplsPath)
        {
            using var fs = File.OpenRead(mplsPath);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
            if (!Encoding.ASCII.GetString(br.ReadBytes(8)).StartsWith("MPLS", StringComparison.Ordinal))
                throw new InvalidDataException($"{Path.GetFileName(mplsPath)} はプレイリストではありません。");
            uint playListStart = ReadU32BE(br);

            fs.Position = playListStart;
            uint playListLength = ReadU32BE(br);
            br.ReadBytes(2);
            ushort itemCount = ReadU16BE(br);
            ReadU16BE(br);
            long bodyEnd = playListStart + 4 + playListLength;

            var items = new List<PlayItem>(itemCount);
            for (int i = 0; i < itemCount; i++)
            {
                if (fs.Position + 2 > bodyEnd) break;
                long itemStart = fs.Position;
                ushort itemLength = ReadU16BE(br);
                long itemEnd = itemStart + 2 + itemLength;
                if (itemEnd > bodyEnd || itemEnd > fs.Length) break;

                string clipName = Encoding.ASCII.GetString(br.ReadBytes(5));
                br.ReadBytes(4); // 形式識別子（M2TS）
                br.ReadBytes(2); // 予約・multi-angle・connection_condition
                byte stcId = br.ReadByte();
                uint ins = ReadU32BE(br);
                uint outs = ReadU32BE(br);
                items.Add(new PlayItem(clipName, stcId, ins, outs));
                fs.Position = itemEnd;
            }
            return items;
        }

        // ---- CLPI ----

        /// <summary>
        /// CLPI のうち切り出しに使う所を読む。先頭：識別子 "HDMV"(4) → 版(4) → SequenceInfo / ProgramInfo / CPI / ClipMark / ExtensionData の開始位置(各 4) → 予約(12)
        /// → ClipInfo（長さ(4) → 予約(2) → ストリーム種別(1) → 用途種別(1) → 予約(4) → 記録レート(4) → ソースパケット数(4)）。
        /// SequenceInfo：長さ(4) → 予約(1) → ATC シーケンス数(1) → ATC ごとに（開始 SPN(4) → STC シーケンス数(1) → STC 番号の初期値(1)
        /// → STC ごとに（PCR PID(2) → 開始 SPN(4) → 提示開始(4) → 提示終了(4)））。
        /// CPI：長さ(4) → 予約 12 ビットと CPI 種別 4 ビット(2) → EP map（予約(1) → ストリーム数(1) → ストリームごとに
        /// PID(2) と「予約 10・EP の種別 4・粗いエントリ数 16・細かいエントリ数 18 ビット」(6) と開始位置(4。EP map の先頭から)）。
        /// ストリームの EP：細かいエントリの開始位置(4。ストリームの EP の先頭から) → 粗いエントリ（細かいエントリの番号 18・PTS の上位 14 ビット(4) → SPN(4)）
        /// → 細かいエントリ（アングル切り替え点 1・I ピクチャの終わりの位置 3・PTS 11・SPN の下位 17 ビット(4)）。
        /// 映像のストリーム（EP の種別 1）があればそれを、無ければ最初のストリームを使う。
        /// </summary>
        private static ClipInfo ReadClipInfo(string clpiPath)
        {
            if (!File.Exists(clpiPath))
                throw new FileNotFoundException($"クリップ情報 {Path.GetFileName(clpiPath)} が見つかりません。", clpiPath);

            using var fs = File.OpenRead(clpiPath);
            using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
            if (!Encoding.ASCII.GetString(br.ReadBytes(8)).StartsWith("HDMV", StringComparison.Ordinal))
                throw new InvalidDataException($"{Path.GetFileName(clpiPath)} はクリップ情報ではありません。");
            uint seqStart = ReadU32BE(br);
            ReadU32BE(br); // ProgramInfo
            uint cpiStart = ReadU32BE(br);
            ReadU32BE(br); ReadU32BE(br);
            br.ReadBytes(12);

            ReadU32BE(br);
            br.ReadBytes(2);
            br.ReadByte(); br.ReadByte();
            br.ReadBytes(4);
            ReadU32BE(br);
            long sourcePackets = ReadU32BE(br);

            var stcSpnStart = new Dictionary<int, long>();
            if (seqStart > 0 && seqStart + 6 <= fs.Length)
            {
                fs.Position = seqStart;
                ReadU32BE(br);
                br.ReadByte();
                byte atcCount = br.ReadByte();
                for (int a = 0; a < atcCount; a++)
                {
                    ReadU32BE(br);
                    byte stcCount = br.ReadByte();
                    byte offsetStcId = br.ReadByte();
                    for (int s = 0; s < stcCount; s++)
                    {
                        ReadU16BE(br);
                        uint spn = ReadU32BE(br);
                        ReadU32BE(br); ReadU32BE(br);
                        stcSpnStart.TryAdd(offsetStcId + s, spn);
                    }
                }
            }

            var entryPoints = new List<EntryPoint>();
            if (cpiStart > 0 && cpiStart + 6 <= fs.Length)
            {
                fs.Position = cpiStart;
                uint cpiLength = ReadU32BE(br);
                if (cpiLength > 0)
                {
                    br.ReadBytes(2);
                    long epMapPos = fs.Position;
                    br.ReadByte();
                    byte streamCount = br.ReadByte();
                    var streams = new List<(int EpType, int Coarse, int Fine, long Addr)>();
                    for (int i = 0; i < streamCount; i++)
                    {
                        ReadU16BE(br); // PID
                        var bits = br.ReadBytes(6);
                        ulong v = 0;
                        foreach (var x in bits) v = (v << 8) | x;
                        int epType = (int)((v >> 34) & 0xF);
                        int coarse = (int)((v >> 18) & 0xFFFF);
                        int fine = (int)(v & 0x3FFFF);
                        long addr = epMapPos + ReadU32BE(br);
                        streams.Add((epType, coarse, fine, addr));
                    }
                    if (streams.Count > 0)
                    {
                        var st = streams.FirstOrDefault(x => x.EpType == 1);
                        if (st == default) st = streams[0];
                        entryPoints = ReadEpStream(fs, br, st.Addr, st.Coarse, st.Fine);
                    }
                }
            }

            return new ClipInfo { SourcePackets = sourcePackets, StcSpnStart = stcSpnStart, EntryPoints = entryPoints };
        }

        /// <summary>
        /// 1 ストリーム分の EP を読み、粗いエントリと細かいエントリを組み合わせた (PTS, SPN) の並びにする。
        /// PTS（90 kHz、33 ビット）は粗いエントリの上位 14 ビット（ビット 32〜19）と細かいエントリの 11 ビット（ビット 19〜9）で、
        /// ビット 19 が重なるので粗いほうの最下位を落として足す。ここでは PlayItem の IN/OUT と同じ 45 kHz（PTS の 1/2）に直して持つ。
        /// SPN は粗いエントリの上位（下位 17 ビットを落としたもの）に細かいエントリの 17 ビットを足す。
        /// </summary>
        private static List<EntryPoint> ReadEpStream(FileStream fs, BinaryReader br, long addr, int coarseCount, int fineCount)
        {
            fs.Position = addr;
            long fineStart = addr + ReadU32BE(br);
            var coarse = new (int FineRef, long Pts, long Spn)[coarseCount];
            for (int i = 0; i < coarseCount; i++)
            {
                uint w = ReadU32BE(br);
                uint spn = ReadU32BE(br);
                coarse[i] = ((int)(w >> 14), w & 0x3FFF, spn);
            }
            fs.Position = fineStart;
            var fine = new (long Pts, long Spn)[fineCount];
            for (int i = 0; i < fineCount; i++)
            {
                uint w = ReadU32BE(br);
                fine[i] = ((w >> 17) & 0x7FF, w & 0x1FFFF);
            }

            var eps = new List<EntryPoint>(fineCount);
            for (int c = 0; c < coarseCount; c++)
            {
                int from = coarse[c].FineRef;
                int to = c + 1 < coarseCount ? coarse[c + 1].FineRef : fineCount;
                for (int f = from; f < to && f < fineCount; f++)
                {
                    long pts45 = ((coarse[c].Pts & ~1L) << 18) + (fine[f].Pts << 8);
                    long spn = (coarse[c].Spn & ~0x1FFFFL) + fine[f].Spn;
                    eps.Add(new EntryPoint(pts45, spn));
                }
            }
            return eps;
        }

        // ---- ビッグエンディアン読み取りヘルパー ----

        private static ushort ReadU16BE(BinaryReader br)
        {
            var b = br.ReadBytes(2);
            if (b.Length < 2) throw new EndOfStreamException();
            return (ushort)((b[0] << 8) | b[1]);
        }

        private static uint ReadU32BE(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (b.Length < 4) throw new EndOfStreamException();
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }
    }
}
