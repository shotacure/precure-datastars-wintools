using System.IO;

namespace PrecureDataStars.OaVerifier.Ts;

/// <summary>
/// 地デジ録画 TS の時刻基準を解析する。
/// <list type="bullet">
///   <item>TOT/TDT（PID 0x0014）から放送日時（JST。ARIB の TDT/TOT は JST を直接搬送する）</item>
///   <item>PCR（27MHz）↔ メディア時刻（ファイル先頭=0）の線形写像</item>
///   <item>PCR を全域でサンプリングした「メディア時刻 ↔ ファイルバイト位置」索引</item>
/// </list>
///
/// <para>シークは <see cref="PositionForMediaMs"/> が返すバイト割合（0–1）で行う。VLC の TS 時刻シークは
/// 可変ビットレートだと「時刻→バイト位置」推定が大きく外れるため、PCR から実バイト位置を割り出して
/// <c>Position</c> で渡す。これにより推定誤差を排除し決定論的に着地する。</para>
///
/// <para>頭出しの基準点（番組先頭オフセット0の絶対時刻）は呼び出し側が <c>on_air_at</c> から与える。</para>
/// </summary>
public sealed class TsTimebase
{
    private const int TsPacketLen = 188;
    private const long PcrClockHz = 27_000_000L;          // PCR は 27MHz
    private const long HeadScanBytes = 64L * 1024 * 1024; // 先頭密走査（同期/TOT/pcrStart 用）
    private const long IndexSampleStep = 4L * 1024 * 1024; // 索引サンプル間隔（約 4MB ごと）
    private const int SampleWindow = 256 * 1024;          // 全域サンプリング時に各点で読む量
    private const long EndScanBytes = 32L * 1024 * 1024;  // 末尾アンカー取得のために末尾から読む量（TOT 秒境界を数点拾える長さ）

    public bool TotFound { get; private set; }
    public DateTime FirstTotJst { get; private set; }
    public DateOnly? BroadcastDate => TotFound ? DateOnly.FromDateTime(FirstTotJst) : null;
    public bool HasMapping { get; private set; }
    public DateTime WallClockAtMediaZero { get; private set; }
    public string Diagnostics { get; private set; } = "";

    /// <summary>解析対象ファイルの総バイト数。</summary>
    public long FileLength { get; private set; }

    /// <summary>フルセグ（MPEG-2 映像を含む番組）の program_number。PAT/PMT から特定できた場合のみ。
    /// LibVLC に <c>:program=</c> で渡し、ワンセグ（H.264 低レート）でなくフルセグを再生・時刻基準にするために使う。</summary>
    public int? FullsegProgramNumber { get; private set; }

    /// <summary>「メディア時刻↔バイト位置」索引で有効なシークが可能か。</summary>
    public bool HasByteIndex => _idxMediaMs.Length >= 2 && FileLength > 0;

    // メディア時刻（ms, 昇順）↔ ファイルバイト位置 の索引。
    private long[] _idxMediaMs = Array.Empty<long>();
    private long[] _idxByte = Array.Empty<long>();

    /// <summary>壁時計(ms) ÷ メディア時刻(ms) の傾き。PCR が実時間 27MHz にロックされていれば 1.0。
    /// 先頭・末尾の TOT アンカーを最小二乗で結んで求め、クロックドリフトがあれば補正する。</summary>
    private double _clockRate = 1.0;

    /// <summary>写像に使った TOT アンカー（秒境界）の数。診断用。</summary>
    public int AnchorCount { get; private set; }

    /// <summary>壁時計とメディア時刻の傾き（1.0 が理想）。診断用。</summary>
    public double ClockRate => _clockRate;

    /// <summary>指定した壁時計（JST）に対応するメディア時刻（ミリ秒、ファイル先頭=0）を返す。</summary>
    public long MediaMsForWallClock(DateTime jst)
    {
        if (!HasMapping) throw new InvalidOperationException("時刻写像が未確立です。");
        return (long)Math.Round((jst - WallClockAtMediaZero).TotalMilliseconds / _clockRate);
    }

    /// <summary>指定メディア時刻（ms）に対応するファイルバイト割合（0–1）を返す。索引が無ければ -1。</summary>
    public double PositionForMediaMs(long ms)
    {
        int n = _idxMediaMs.Length;
        if (n == 0 || FileLength <= 0) return -1;
        if (ms <= _idxMediaMs[0]) return Clamp01(_idxByte[0] / (double)FileLength);
        if (ms >= _idxMediaMs[n - 1]) return Clamp01(_idxByte[n - 1] / (double)FileLength);

        int lo = 0, hi = n - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_idxMediaMs[mid] <= ms) lo = mid; else hi = mid;
        }
        long m0 = _idxMediaMs[lo], m1 = _idxMediaMs[hi];
        long b0 = _idxByte[lo], b1 = _idxByte[hi];
        double f = m1 > m0 ? (ms - m0) / (double)(m1 - m0) : 0;
        double bytePos = b0 + f * (b1 - b0);
        return Clamp01(bytePos / FileLength);
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    /// <summary>TS ファイルを解析して <see cref="TsTimebase"/> を構築する。</summary>
    public static TsTimebase Analyze(string path, CancellationToken ct = default)
    {
        var result = new TsTimebase();
        result.FileLength = new FileInfo(path).Length;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);

        // ── フェーズ1：先頭を密に走査して 同期/ストライド・pcrPid・pcrStart・TOT アンカーを得る ──
        long headLen = Math.Min(result.FileLength, HeadScanBytes);
        byte[] head = new byte[headLen];
        int hn = ReadFull(fs, head, head.Length);

        if (!TryFindSync(head, hn, out int firstSync, out int stride))
        {
            result.Diagnostics = "TS 同期バイト(0x47)が見つかりませんでした。TS ファイルではない可能性があります。";
            return result;
        }

        // PAT/PMT を解析してフルセグ（MPEG-2 映像 stream_type 0x02 を持つ番組）の PCR_PID と
        // program_number を特定する。地デジ TS はワンセグ(H.264)が別番組として多重化されており、
        // 「最初に見つかった PCR」方式だとワンセグ側を掴むことがあるため、明示的にフルセグを選ぶ。
        DetectFullseg(head, hn, firstSync, stride, out int forcedPcrPid, out int? fullsegProg);
        result.FullsegProgramNumber = fullsegProg;

        int pcrPid = forcedPcrPid;          // フルセグの PCR_PID（特定できなければ -1 ＝ 最初の PCR にフォールバック）
        long pcrStart = -1, lastPcr = -1;
        DateTime? prevTot = null;
        bool firstTotSeen = false; long firstTotPcr = -1; DateTime firstTotWall = default;
        // TOT 秒境界アンカー（節目で複数取り、最小二乗で先頭壁時計＋クロックレートを求める）。
        var rawAnchors = new List<(long pcr, DateTime wall)>();

        var idxMs = new List<long>();
        var idxByte = new List<long>();
        long lastSampleByte = long.MinValue;

        for (long pos = firstSync; pos + TsPacketLen <= hn; pos += stride)
        {
            ct.ThrowIfCancellationRequested();
            if (head[pos] != 0x47)
            {
                long rs = FindNextSync(head, pos, hn, stride);
                if (rs < 0) break;
                pos = rs - stride;
                continue;
            }

            if (TryExtractPcr(head, pos, out int pid, out long pcr27))
            {
                if (pcrPid < 0) pcrPid = pid;          // フォールバック：最初に PCR を載せた PID を採用
                if (pid == pcrPid)
                {
                    if (pcrStart < 0) pcrStart = pcr27; // 採用 PID の最初の PCR ＝ メディア時刻 0
                    lastPcr = pcr27;
                    // 索引サンプル（pcrStart 基準のメディア時刻 ↔ このパケットのバイト位置）。
                    if (pos - lastSampleByte >= IndexSampleStep || idxByte.Count == 0)
                    {
                        idxMs.Add((pcr27 - pcrStart) / (PcrClockHz / 1000));
                        idxByte.Add(pos);
                        lastSampleByte = pos;
                    }
                }
            }

            if (TryExtractTot(head, pos, hn, out DateTime totWall))
            {
                if (!result.TotFound) { result.TotFound = true; result.FirstTotJst = totWall; }
                if (!firstTotSeen && lastPcr >= 0) { firstTotSeen = true; firstTotPcr = lastPcr; firstTotWall = totWall; }
                // 秒境界（TOT 秒値が変わった瞬間）＝その秒の .000 を直近 PCR に結びつけるアンカー。
                if (prevTot is DateTime pv && totWall != pv && pcrStart >= 0 && lastPcr >= 0)
                    rawAnchors.Add((lastPcr, totWall));
                prevTot = totWall;
            }
        }

        // ── フェーズ2：残り全域を疎にサンプリングして索引を伸ばす ──
        if (pcrPid >= 0 && pcrStart >= 0)
        {
            long start = (lastSampleByte == long.MinValue ? 0 : lastSampleByte) + IndexSampleStep;
            byte[] win = new byte[SampleWindow];
            for (long off = Math.Max(start, headLen); off + TsPacketLen < result.FileLength; off += IndexSampleStep)
            {
                ct.ThrowIfCancellationRequested();
                fs.Seek(off, SeekOrigin.Begin);
                int wn = ReadFull(fs, win, win.Length);
                if (wn < TsPacketLen * 2) break;
                if (SampleFirstPcr(win, wn, stride, pcrPid, out long bytePosInWin, out long pcr27))
                {
                    long ms = (pcr27 - pcrStart) / (PcrClockHz / 1000);
                    long bytePos = off + bytePosInWin;
                    if (ms > (idxMs.Count > 0 ? idxMs[^1] : long.MinValue))
                    {
                        idxMs.Add(ms);
                        idxByte.Add(bytePos);
                    }
                }
            }
        }

        result._idxMediaMs = idxMs.ToArray();
        result._idxByte = idxByte.ToArray();

        // ── 末尾アンカー：ファイル末尾付近も走査して TOT 秒境界アンカーを足す（広いベースラインで
        //    クロックレートを検証・補正するため）。先頭クラスタだけだと傾きが TOT の 1 秒量子化に埋もれる。 ──
        if (pcrStart >= 0 && result.FileLength > headLen + EndScanBytes)
        {
            long endOff = Math.Max(headLen, result.FileLength - EndScanBytes);
            byte[] tail = new byte[(int)Math.Min(EndScanBytes, result.FileLength - endOff)];
            fs.Seek(endOff, SeekOrigin.Begin);
            int tn = ReadFull(fs, tail, tail.Length);
            if (TryFindSync(tail, tn, out int tSync, out int tStride))
            {
                long tLastPcr = -1; DateTime? tPrev = null;
                for (long pos = tSync; pos + TsPacketLen <= tn; pos += tStride)
                {
                    ct.ThrowIfCancellationRequested();
                    if (tail[pos] != 0x47) { long rs = FindNextSync(tail, pos, tn, tStride); if (rs < 0) break; pos = rs - tStride; continue; }
                    if (TryExtractPcr(tail, pos, out int pid, out long pcr) && pid == pcrPid) tLastPcr = pcr;
                    if (TryExtractTot(tail, pos, tn, out DateTime tot))
                    {
                        if (tPrev is DateTime pv && tot != pv && tLastPcr >= 0) rawAnchors.Add((tLastPcr, tot));
                        tPrev = tot;
                    }
                }
            }
        }

        // ── 写像（壁時計↔メディア時刻）の確定：複数 TOT アンカーを最小二乗で結ぶ ──
        if (pcrStart >= 0 && rawAnchors.Count >= 1)
        {
            result.AnchorCount = rawAnchors.Count;
            DateTime baseDate = rawAnchors[0].wall;
            double sx = 0, sy = 0, sxx = 0, sxy = 0, minX = double.MaxValue, maxX = double.MinValue;
            int nN = rawAnchors.Count;
            foreach (var (pcr, wall) in rawAnchors)
            {
                double x = (pcr - pcrStart) / (PcrClockHz / 1000.0);   // メディア時刻(ms)
                double y = (wall - baseDate).TotalMilliseconds;        // 壁時計(ms, baseDate 基準)
                sx += x; sy += y; sxx += x * x; sxy += x * y;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
            }
            double meanX = sx / nN, meanY = sy / nN, a, b;
            // ベースラインが十分広い（>2 分）ときだけ傾きを推定。狭いと TOT の 1 秒量子化に埋もれるため。
            if (nN >= 2 && (maxX - minX) > 120000.0)
            {
                double denom = sxx - sx * meanX;
                a = denom > 0 ? (sxy - sx * meanY) / denom : 1.0;
                if (a < 0.99 || a > 1.01) a = 1.0;   // 実用上レートは 1 近傍。大きく外れたら誤検出として 1 固定
                b = meanY - a * meanX;
            }
            else { a = 1.0; b = meanY - meanX; }       // 単点 or ベースライン不足 → レート1で切片のみ
            result._clockRate = a;
            result.WallClockAtMediaZero = baseDate.AddMilliseconds(b);
            result.HasMapping = true;
            result.Diagnostics =
                $"TOT={result.FirstTotJst:yyyy-MM-dd HH:mm:ss}（アンカー{nN}点 / レート{a:0.0000} / 索引{result._idxMediaMs.Length}点）";
        }
        else if (pcrStart >= 0 && firstTotSeen)
        {
            double mediaSec = (firstTotPcr - pcrStart) / (double)PcrClockHz;
            result.WallClockAtMediaZero = firstTotWall - TimeSpan.FromSeconds(mediaSec);
            result.AnchorCount = 1;
            result.HasMapping = true;
            result.Diagnostics =
                $"TOT={result.FirstTotJst:yyyy-MM-dd HH:mm:ss}（1秒精度フォールバック / 索引{result._idxMediaMs.Length}点。再アンカー推奨）";
        }
        else if (result.TotFound)
            result.Diagnostics = $"TOT={result.FirstTotJst:yyyy-MM-dd HH:mm:ss}（PCR が取れず写像未確立）";
        else
            result.Diagnostics = "TOT/TDT が見つかりませんでした。放送日を自動判定できません。";

        return result;
    }

    // ── パケット解析ヘルパ ──

    /// <summary>指定パケット位置からアダプテーションフィールドの PCR を取り出す（PID も返す）。</summary>
    private static bool TryExtractPcr(byte[] buf, long pos, out int pid, out long pcr27)
    {
        pid = ((buf[pos + 1] & 0x1F) << 8) | buf[pos + 2];
        pcr27 = -1;
        int afc = (buf[pos + 3] >> 4) & 0x3;
        if (afc != 2 && afc != 3) return false;
        int afl = buf[pos + 4];
        if (afl <= 0) return false;
        int flags = buf[pos + 5];
        if ((flags & 0x10) == 0 || afl < 7) return false;
        long baseClk =
            ((long)buf[pos + 6] << 25) | ((long)buf[pos + 7] << 17) |
            ((long)buf[pos + 8] << 9) | ((long)buf[pos + 9] << 1) | ((long)buf[pos + 10] >> 7);
        long ext = (((long)buf[pos + 10] & 0x01) << 8) | buf[pos + 11];
        pcr27 = baseClk * 300 + ext;
        return true;
    }

    /// <summary>指定パケット位置が PID 0x0014 の TOT/TDT なら時刻（JST）を取り出す。</summary>
    private static bool TryExtractTot(byte[] buf, long pos, long limit, out DateTime jst)
    {
        jst = default;
        int pid = ((buf[pos + 1] & 0x1F) << 8) | buf[pos + 2];
        if (pid != 0x0014) return false;
        bool pusi = (buf[pos + 1] & 0x40) != 0;
        if (!pusi) return false;
        int afc = (buf[pos + 3] >> 4) & 0x3;
        if (afc != 1 && afc != 3) return false;

        int payloadOffset = (afc == 3) ? 5 + buf[pos + 4] : 4;
        long p = pos + payloadOffset;
        if (p >= limit) return false;
        int pointer = buf[p];
        long sec = p + 1 + pointer;
        if (sec + 8 > limit) return false;
        int tableId = buf[sec];
        if (tableId != 0x70 && tableId != 0x73) return false;
        return TryParseUtcTime(buf, sec + 3, out jst);
    }

    /// <summary>サンプリング窓の中で最初の pcrPid の PCR を探す（窓内バイト位置と PCR を返す）。</summary>
    private static bool SampleFirstPcr(byte[] win, int wn, int stride, int pcrPid, out long bytePosInWin, out long pcr27)
    {
        bytePosInWin = -1; pcr27 = -1;
        if (!TryFindSync(win, wn, out int firstSync, out int s)) return false;
        if (s != stride) stride = s;
        for (long pos = firstSync; pos + TsPacketLen <= wn; pos += stride)
        {
            if (win[pos] != 0x47)
            {
                long rs = FindNextSync(win, pos, wn, stride);
                if (rs < 0) break;
                pos = rs - stride;
                continue;
            }
            if (TryExtractPcr(win, pos, out int pid, out long pcr) && pid == pcrPid)
            {
                bytePosInWin = pos; pcr27 = pcr; return true;
            }
        }
        return false;
    }

    /// <summary>PAT/PMT を解析し、フルセグ（MPEG-2 映像 stream_type 0x02 を含む番組）の PCR_PID と program_number を返す。
    /// 見つからなければ <paramref name="fullsegPcrPid"/>=-1（呼び出し側で「最初の PCR」へフォールバック）。</summary>
    private static void DetectFullseg(byte[] buf, int len, int firstSync, int stride, out int fullsegPcrPid, out int? fullsegProgram)
    {
        fullsegPcrPid = -1; fullsegProgram = null;
        Dictionary<int, int>? progToPmt = null;                 // program_number -> PMT PID
        var pmtInfo = new Dictionary<int, (int pcrPid, bool hasMpeg2)>();
        long cap = Math.Min(len, firstSync + 8L * 1024 * 1024); // PAT/PMT は数百 ms 周期で来るので先頭 8MB で十分

        for (long pos = firstSync; pos + TsPacketLen <= len && pos < cap; pos += stride)
        {
            if (buf[pos] != 0x47) { long rs = FindNextSync(buf, pos, len, stride); if (rs < 0) break; pos = rs - stride; continue; }
            int pid = ((buf[pos + 1] & 0x1F) << 8) | buf[pos + 2];
            bool pusi = (buf[pos + 1] & 0x40) != 0;
            if (!pusi) continue;
            int afc = (buf[pos + 3] >> 4) & 0x3;
            if (afc != 1 && afc != 3) continue;
            int payloadOffset = (afc == 3) ? 5 + buf[pos + 4] : 4;
            long p = pos + payloadOffset;
            if (p >= len) continue;
            long sec = p + 1 + buf[p];   // pointer_field をスキップしたセクション先頭
            if (sec + 12 > len) continue;

            if (pid == 0x0000 && progToPmt == null && buf[sec] == 0x00)
            {
                progToPmt = ParsePat(buf, sec, len);
            }
            else if (progToPmt != null && buf[sec] == 0x02 && progToPmt.ContainsValue(pid))
            {
                if (ParsePmt(buf, sec, len, out int prog, out int pcrPid, out bool hasMpeg2) && !pmtInfo.ContainsKey(prog))
                    pmtInfo[prog] = (pcrPid, hasMpeg2);
            }
            if (progToPmt is { Count: > 0 } && pmtInfo.Count >= progToPmt.Count) break;
        }

        if (progToPmt == null) return;
        foreach (var kv in pmtInfo)
            if (kv.Value.hasMpeg2) { fullsegPcrPid = kv.Value.pcrPid; fullsegProgram = kv.Key; return; }
    }

    /// <summary>PAT セクションから program_number → PMT PID の辞書を作る（network_PID は除く）。</summary>
    private static Dictionary<int, int> ParsePat(byte[] buf, long sec, long len)
    {
        var map = new Dictionary<int, int>();
        int sectionLength = ((buf[sec + 1] & 0x0F) << 8) | buf[sec + 2];
        long end = Math.Min(len, sec + 3 + sectionLength - 4); // 末尾 4 バイトは CRC32
        for (long i = sec + 8; i + 4 <= end; i += 4)
        {
            int prog = (buf[i] << 8) | buf[i + 1];
            int pid = ((buf[i + 2] & 0x1F) << 8) | buf[i + 3];
            if (prog != 0) map[prog] = pid;
        }
        return map;
    }

    /// <summary>PMT セクションから program_number・PCR_PID・MPEG-2 映像(0x02)の有無を取り出す。</summary>
    private static bool ParsePmt(byte[] buf, long sec, long len, out int prog, out int pcrPid, out bool hasMpeg2)
    {
        prog = -1; pcrPid = -1; hasMpeg2 = false;
        if (sec + 12 > len) return false;
        int sectionLength = ((buf[sec + 1] & 0x0F) << 8) | buf[sec + 2];
        long end = Math.Min(len, sec + 3 + sectionLength - 4); // 末尾 4 バイトは CRC32
        prog = (buf[sec + 3] << 8) | buf[sec + 4];
        pcrPid = ((buf[sec + 8] & 0x1F) << 8) | buf[sec + 9];
        int programInfoLength = ((buf[sec + 10] & 0x0F) << 8) | buf[sec + 11];
        for (long i = sec + 12 + programInfoLength; i + 5 <= end;)
        {
            int streamType = buf[i];
            int esInfoLength = ((buf[i + 3] & 0x0F) << 8) | buf[i + 4];
            if (streamType == 0x02) hasMpeg2 = true; // MPEG-2 Video ＝ フルセグ
            i += 5 + esInfoLength;
        }
        return true;
    }

    private static bool TryParseUtcTime(byte[] buf, long off, out DateTime jst)
    {
        jst = default;
        if (off + 5 > buf.Length) return false;
        int mjd = (buf[off] << 8) | buf[off + 1];
        if (mjd == 0xFFFF || mjd == 0) return false;
        int hour = FromBcd(buf[off + 2]), min = FromBcd(buf[off + 3]), secv = FromBcd(buf[off + 4]);
        if (hour > 23 || min > 59 || secv > 59) return false;

        int yp = (int)((mjd - 15078.2) / 365.25);
        int mp = (int)((mjd - 14956.1 - (int)(yp * 365.25)) / 30.6001);
        int day = mjd - 14956 - (int)(yp * 365.25) - (int)(mp * 30.6001);
        int k = (mp == 14 || mp == 15) ? 1 : 0;
        int year = 1900 + yp + k;
        int month = mp - 1 - k * 12;
        if (year < 2000 || year > 2100 || month < 1 || month > 12 || day < 1 || day > 31) return false;
        try { jst = new DateTime(year, month, day, hour, min, secv, DateTimeKind.Unspecified); }
        catch { return false; }
        return true;
    }

    private static int FromBcd(int b) => ((b >> 4) & 0x0F) * 10 + (b & 0x0F);

    private static int ReadFull(FileStream fs, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = fs.Read(buf, total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static bool TryFindSync(byte[] buf, int len, out int firstSync, out int stride)
    {
        firstSync = -1; stride = TsPacketLen;
        int limit = Math.Min(len, 200_000);
        foreach (int s in new[] { 188, 192 })
        {
            for (int i = 0; i + 2 * s < limit; i++)
                if (buf[i] == 0x47 && buf[i + s] == 0x47 && buf[i + 2 * s] == 0x47)
                {
                    firstSync = i; stride = s; return true;
                }
        }
        return false;
    }

    private static long FindNextSync(byte[] buf, long from, long len, int stride)
    {
        for (long i = from + 1; i + 2 * stride < len; i++)
            if (buf[i] == 0x47 && buf[i + stride] == 0x47) return i;
        return -1;
    }
}
