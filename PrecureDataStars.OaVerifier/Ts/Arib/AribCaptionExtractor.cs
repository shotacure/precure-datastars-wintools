using System.IO;

namespace PrecureDataStars.OaVerifier.Ts.Arib;

/// <summary>
/// 録画 TS を先頭から末尾まで読み、フルセグ番組の ARIB 字幕を全編抽出する。
///
/// <para>流れ：PAT → 対象番組の PMT から字幕 ES（stream_type 0x06 で component_tag 0x30〜0x37 のもの）と PCR_PID を特定 →
/// 字幕 PES を組み立て → PTS をメディア時刻（ms、ファイル先頭=0）に直して <see cref="AribCaptionDecoder"/> へ渡す。</para>
///
/// <para>メディア時刻は <see cref="TsTimebase"/> と同じ規則（採用 PID の最初の PCR=0、PCR の逆戻りや AF 不連続フラグの点で
/// 基準を貼り直して経過時刻を巻き戻さない）で PCR から求め、PTS は直前の PCR からの差分で載せる。これで再生位置・
/// 番組先頭アンカー・パート境界と同じ物差しで字幕の時刻を並べられる。</para>
///
/// <para>スレッド方針：<see cref="Extract"/> を背景スレッドで 1 回呼ぶ。ファイルは順読みのみ（数 GB でもディスクの読み出し速度で終わる）。</para>
/// </summary>
internal static class AribCaptionExtractor
{
    private const int TsPacketLen = 188;
    private const long PcrClockHz = 27_000_000L;
    private const int ReadChunkBytes = 8 * 1024 * 1024;
    private const long ProgressStepBytes = 64L * 1024 * 1024;
    private const long PtsMask = (1L << 33) - 1;

    /// <summary>抽出結果。</summary>
    /// <param name="Cues">字幕（表示開始順）。</param>
    /// <param name="CaptionPid">読んだ字幕 ES の PID（見つからなければ -1）。</param>
    /// <param name="StatementCount">読んだ字幕文の数。</param>
    /// <param name="BrokenCount">構造が壊れていて読み飛ばした PES の数。</param>
    /// <param name="Diagnostics">状態表示用の要約。</param>
    public sealed record Result(IReadOnlyList<CaptionCue> Cues, int CaptionPid, int StatementCount, int BrokenCount, string Diagnostics);

    /// <summary>TS 全編から字幕を抽出する。<paramref name="programNumber"/> にフルセグの program_number を渡すと
    /// その番組の字幕を読む（null なら MPEG-2 映像を持つ最初の番組）。<paramref name="pcrPid"/> に
    /// <see cref="TsTimebase.PcrPid"/> を渡すと、メディア時刻 0 を再生側と同じ PCR にそろえる（-1 なら PMT の PCR_PID）。
    /// <paramref name="progress"/> には 0–1 の読み進み割合を報告する。</summary>
    public static Result Extract(string path, int? programNumber, int pcrPid, IProgress<double>? progress, CancellationToken ct)
    {
        var state = new ScanState(programNumber, pcrPid);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        long fileLen = fs.Length;
        byte[] buf = new byte[ReadChunkBytes];
        int filled = ReadInto(fs, buf, 0, buf.Length);
        if (!TsTimebase.TryFindSync(buf, filled, out int firstSync, out int stride))
            return new Result(Array.Empty<CaptionCue>(), -1, 0, 0, "TS 同期バイト(0x47)が見つかりませんでした。");

        long bufFileOffset = 0;          // buf[0] のファイル内位置
        long nextProgressAt = 0;
        int pos = firstSync;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            while (pos + TsPacketLen <= filled)
            {
                if (buf[pos] != 0x47)
                {
                    long rs = TsTimebase.FindNextSync(buf, pos, filled, stride);
                    if (rs < 0) { pos = Math.Max(pos + 1, filled - 2 * stride); break; } // 続きを読んでから探し直す
                    pos = (int)rs;
                    continue;
                }
                state.ProcessPacket(buf, pos);
                pos += stride;
            }

            long here = bufFileOffset + Math.Min(pos, filled);
            if (progress != null && here >= nextProgressAt)
            {
                progress.Report(fileLen > 0 ? Math.Min(1.0, here / (double)fileLen) : 0);
                nextProgressAt = here + ProgressStepBytes;
            }

            // 読み残し（パケット途中）を先頭へ寄せて続きを読む。ストライド 192 で末尾をまたいだ分は次の読みで飛ばす。
            int carry = Math.Max(0, filled - pos);
            int skip = Math.Max(0, pos - filled);
            if (carry > 0) Buffer.BlockCopy(buf, pos, buf, 0, carry);
            bufFileOffset += filled - carry;
            int n = ReadInto(fs, buf, carry, buf.Length - carry);
            if (n <= 0) break;
            filled = carry + n;
            pos = skip;
        }

        state.Decoder.Finish(state.LastElapsedMs);
        progress?.Report(1.0);

        string diag = state.CaptionPid < 0
            ? "字幕の流れ（字幕 ES）が見つかりませんでした。字幕の無い放送か、ワンセグのみの録画の可能性があります。"
            : $"字幕 PID 0x{state.CaptionPid:X4} / 字幕文 {state.Decoder.StatementCount} / 字幕 {state.Decoder.Cues.Count} 件"
              + (state.Decoder.BrokenCount > 0 ? $" / 読み飛ばし {state.Decoder.BrokenCount}" : "");
        return new Result(state.Decoder.Cues, state.CaptionPid, state.Decoder.StatementCount, state.Decoder.BrokenCount, diag);
    }

    private static int ReadInto(FileStream fs, byte[] buf, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = fs.Read(buf, offset + total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    /// <summary>1 パケットずつ与えて番組表・PCR・字幕 PES を追う走査状態。</summary>
    private sealed class ScanState
    {
        private readonly int? _wantedProgram;
        private Dictionary<int, int>? _pmtPidByProgram;
        private int _targetProgram = -1;
        private int _pcrPid = -1;

        // PCR タイムライン（TsTimebase.ElapsedMs と同じ規則）
        private long _lastPcr = -1;
        private long _basePcr;
        private long _baseElapsedMs;

        // 字幕 PES の組み立て
        private readonly List<byte> _pes = new(4096);
        private bool _collecting;
        private int _lastCc = -1;

        public AribCaptionDecoder Decoder { get; } = new();
        public int CaptionPid { get; private set; } = -1;
        public long LastElapsedMs { get; private set; }

        public ScanState(int? wantedProgram, int pcrPid)
        {
            _wantedProgram = wantedProgram;
            _pcrPid = pcrPid;   // 既知なら先頭から PCR を追える（PMT を待たない）
        }

        public void ProcessPacket(byte[] buf, int p)
        {
            int pid = ((buf[p + 1] & 0x1F) << 8) | buf[p + 2];
            bool pusi = (buf[p + 1] & 0x40) != 0;
            int afc = (buf[p + 3] >> 4) & 0x3;
            int cc = buf[p + 3] & 0x0F;

            if (pid == _pcrPid && TsTimebase.TryExtractPcr(buf, p, out _, out long pcr27, out bool disc))
                AdvancePcr(pcr27, disc);

            if (afc is not (1 or 3)) return;                               // ペイロード無し
            int payload = afc == 3 ? 5 + buf[p + 4] : 4;
            int end = p + TsPacketLen;
            if (p + payload >= end) return;

            if (pid == 0x0000)
            {
                if (pusi && _pmtPidByProgram == null)
                {
                    int sec = p + payload + 1 + buf[p + payload];
                    if (sec + 8 < end && buf[sec] == 0x00) _pmtPidByProgram = TsTimebase.ParsePat(buf, sec, end);
                }
                return;
            }

            if (_pmtPidByProgram != null && pusi && _pmtPidByProgram.ContainsValue(pid))
            {
                int sec = p + payload + 1 + buf[p + payload];
                if (sec + 12 < end && buf[sec] == 0x02) ParsePmt(buf, sec, end);
                return;
            }

            if (pid == CaptionPid && CaptionPid >= 0)
                AppendCaptionPayload(buf, p + payload, end, pusi, cc);
        }

        /// <summary>PMT を読み、対象番組なら PCR_PID と字幕 ES の PID を確定（更新）する。
        /// 対象番組は指定の program_number、未指定なら MPEG-2 映像（フルセグ）を持つ最初の番組。</summary>
        private void ParsePmt(byte[] buf, int sec, int limit)
        {
            int sectionLength = ((buf[sec + 1] & 0x0F) << 8) | buf[sec + 2];
            int end = Math.Min(limit, sec + 3 + sectionLength - 4);   // 末尾 4 バイトは CRC32
            int prog = (buf[sec + 3] << 8) | buf[sec + 4];
            if (_targetProgram >= 0 && prog != _targetProgram) return;
            if (_wantedProgram is int w && prog != w) return;

            int pcrPid = ((buf[sec + 8] & 0x1F) << 8) | buf[sec + 9];
            int programInfoLength = ((buf[sec + 10] & 0x0F) << 8) | buf[sec + 11];
            bool hasMpeg2 = false;
            int captionPid = -1, captionTag = int.MaxValue;
            for (int i = sec + 12 + programInfoLength; i + 5 <= end;)
            {
                int streamType = buf[i];
                int esPid = ((buf[i + 1] & 0x1F) << 8) | buf[i + 2];
                int esInfoLength = ((buf[i + 3] & 0x0F) << 8) | buf[i + 4];
                if (streamType == 0x02) hasMpeg2 = true;
                if (streamType == 0x06)
                {
                    // stream_identifier_descriptor（0x52）の component_tag 0x30〜0x37 が字幕（0x38〜は文字スーパー）。
                    for (int d = i + 5; d + 2 <= Math.Min(end, i + 5 + esInfoLength);)
                    {
                        int tag = buf[d], len = buf[d + 1];
                        if (tag == 0x52 && len >= 1 && d + 2 < end)
                        {
                            int componentTag = buf[d + 2];
                            if (componentTag is >= 0x30 and <= 0x37 && componentTag < captionTag)
                            {
                                captionTag = componentTag;
                                captionPid = esPid;
                            }
                        }
                        d += 2 + len;
                    }
                }
                i += 5 + esInfoLength;
            }

            if (_wantedProgram is null && _targetProgram < 0 && !hasMpeg2) return; // ワンセグ等は対象にしない
            _targetProgram = prog;
            if (_pcrPid < 0) _pcrPid = pcrPid;
            if (captionPid >= 0 && captionPid != CaptionPid)
            {
                CaptionPid = captionPid;
                _collecting = false;
                _lastCc = -1;
            }
        }

        /// <summary>PCR を経過メディア時刻に積む（逆戻り・不連続フラグで基準を貼り直し、時刻を巻き戻さない）。</summary>
        private void AdvancePcr(long pcr27, bool discontinuity)
        {
            if (_lastPcr < 0)
            {
                _basePcr = pcr27; _baseElapsedMs = 0; _lastPcr = pcr27; LastElapsedMs = 0;
                return;
            }
            if (discontinuity || pcr27 < _lastPcr)
            {
                _basePcr = pcr27;
                _baseElapsedMs = LastElapsedMs;
            }
            else
            {
                LastElapsedMs = _baseElapsedMs + (pcr27 - _basePcr) / (PcrClockHz / 1000);
            }
            _lastPcr = pcr27;
        }

        /// <summary>PTS（90kHz）を、直前の PCR を基準にしたメディア時刻（ms）に直す。33 ビットの折り返しを考慮する。</summary>
        private long MediaMsForPts(long pts)
        {
            long pcrBase90 = (_lastPcr / 300) & PtsMask;
            long d = (pts - pcrBase90) & PtsMask;
            if (d >= 1L << 32) d -= 1L << 33;
            return LastElapsedMs + d / 90;
        }

        /// <summary>字幕 PID のペイロードを PES に組み立てる。連続性カウンタが飛んだら組み立て中の PES は捨てる。</summary>
        private void AppendCaptionPayload(byte[] buf, int from, int end, bool pusi, int cc)
        {
            if (!pusi && _lastCc >= 0 && cc == _lastCc) return;           // 重複パケット
            bool ccOk = _lastCc < 0 || cc == ((_lastCc + 1) & 0x0F);
            _lastCc = cc;

            if (pusi)
            {
                if (_collecting) FinishPes();
                _pes.Clear();
                _collecting = true;
            }
            else if (!ccOk)
            {
                _collecting = false;
                return;
            }
            if (!_collecting) return;

            for (int i = from; i < end; i++) _pes.Add(buf[i]);
            if (_pes.Count >= 6)
            {
                int pesLen = (_pes[4] << 8) | _pes[5];
                if (pesLen > 0 && _pes.Count >= 6 + pesLen)
                {
                    FinishPes();
                    _collecting = false;
                }
            }
        }

        /// <summary>組み上がった PES からペイロードと表示時刻を取り出してデコーダへ渡す。
        /// 同期型（stream_id 0xBD）は PTS、非同期型（0xBF）は PTS を持たないので直前の PCR 時刻を使う。</summary>
        private void FinishPes()
        {
            if (_lastPcr < 0 || _pes.Count < 9) return;
            var b = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pes);
            if (b[0] != 0 || b[1] != 0 || b[2] != 1) return;
            int streamId = b[3];
            int pesLen = (b[4] << 8) | b[5];
            int total = pesLen > 0 ? Math.Min(b.Length, 6 + pesLen) : b.Length;

            if (streamId == 0xBD)
            {
                int hdrLen = b[8];
                int start = 9 + hdrLen;
                if (start >= total) return;
                long mediaMs = LastElapsedMs;
                if ((b[7] & 0x80) != 0 && b.Length >= 14)
                {
                    long pts = ((long)(b[9] >> 1) & 0x07) << 30 | (long)b[10] << 22 | (long)(b[11] >> 1) << 15
                               | (long)b[12] << 7 | (long)(b[13] >> 1);
                    mediaMs = MediaMsForPts(pts);
                }
                Decoder.DecodePes(b[start..total], mediaMs);
            }
            else if (streamId == 0xBF)
            {
                if (6 < total) Decoder.DecodePes(b[6..total], LastElapsedMs);
            }
        }
    }
}
