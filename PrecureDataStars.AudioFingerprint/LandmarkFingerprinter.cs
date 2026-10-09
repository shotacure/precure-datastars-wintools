using System.Security.Cryptography;

namespace PrecureDataStars.AudioFingerprint;

/// <summary>
/// ランドマーク方式の音の指紋を取る。
/// モノ化した音を 11,025 Hz に間引き、512 点 FFT（ホップ 256 ≒ 23 ms、Hann 窓）のスペクトログラムから
/// 時間・周波数の局所的なピークを拾い、近くの時刻のピークと対にして「周波数 1・周波数の差・時刻の差」を 1 つのハッシュに詰める。
/// 1 秒あたりのピーク数を上限で抑えるので、音量に関わらず密度がそろう。
/// セリフなどが重なっても、対の一部が残っていれば同じハッシュが同じ時刻差で現れるため突き合わせられる。
/// </summary>
/// <remarks>
/// 使い方：<see cref="Append"/> に 16 ビット LE のサンプル列を順に渡し、最後に <see cref="Finish"/> を呼ぶ。
/// 取り方の版は <see cref="MethodVersion"/>。定数（周波数・FFT・ピークの拾い方・対の作り方）を変えたら上げる。
/// </remarks>
public sealed class LandmarkFingerprinter
{
    /// <summary>特徴量の取り方の版。</summary>
    public const byte MethodVersion = 1;

    /// <summary>解析時のサンプリング周波数（Hz）。入力は <see cref="InputSampleRateHz"/> からこれへ 4 分の 1 に間引く。</summary>
    public const int SampleRateHz = 11025;

    /// <summary>入力のサンプリング周波数（Hz。CD-DA）。</summary>
    public const int InputSampleRateHz = 44100;

    /// <summary>FFT の点数。</summary>
    public const int FftSize = 512;

    /// <summary>フレームの間隔（サンプル数）。</summary>
    public const int HopSamples = 256;

    // ── ピークの拾い方 ──
    /// <summary>局所最大とみなす範囲（時間方向、前後のフレーム数）。</summary>
    private const int PeakTimeRadius = 2;
    /// <summary>局所最大とみなす範囲（周波数方向、前後のビン数）。</summary>
    private const int PeakFreqRadius = 5;
    /// <summary>1 秒のまとまりごとに残すピークの上限。</summary>
    private const int PeaksPerSecond = 24;
    /// <summary>これより小さい（dBFS）ピークは無音とみなして捨てる。</summary>
    private const float MinPeakDb = -70f;

    // ── 対の作り方 ──
    /// <summary>1 つのピークから作る対の上限（時刻の近い順）。</summary>
    private const int Fanout = 3;
    /// <summary>対の相手を探す時刻の差（フレーム数）。下限は含み、上限は含まない。</summary>
    private const int PairMinDt = 2;
    private const int PairMaxDt = 64;
    /// <summary>対の相手を探す周波数の差（ビン数）の上限。</summary>
    private const int PairMaxDf = 31;

    private readonly int _channels;
    private readonly PcmUtil.Decimator4 _decimator = new();
    private readonly List<float> _mono = new();
    private readonly IncrementalHash _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _inputBytes;

    /// <param name="channels">入力のチャンネル数（CD-DA は 2）。</param>
    public LandmarkFingerprinter(int channels = 2)
    {
        _channels = channels;
    }

    /// <summary>16 ビット LE のサンプル列（インターリーブ）を順に受け取る。</summary>
    public void Append(ReadOnlySpan<byte> pcm16)
    {
        _sha256.AppendData(pcm16);
        _inputBytes += pcm16.Length;
        var mono = PcmUtil.ToMono(pcm16, _channels);
        _mono.AddRange(_decimator.Process(mono));
    }

    /// <summary>受け取った音をすべて解析して指紋を返す。元の PCM 全体の SHA-256（16 進小文字）も返す。</summary>
    public (LandmarkFingerprint Fingerprint, string PcmSha256) Finish()
    {
        var sha = Convert.ToHexString(_sha256.GetHashAndReset()).ToLowerInvariant();
        int durationMs = (int)(_inputBytes / (2L * _channels) * 1000L / InputSampleRateHz);

        var peaks = FindPeaks(_mono);
        var entries = MakePairs(peaks);

        var fp = new LandmarkFingerprint
        {
            MethodVersion = MethodVersion,
            SampleRateHz = SampleRateHz,
            FftSize = FftSize,
            HopSamples = HopSamples,
            DurationMs = durationMs,
            Entries = entries
        };
        return (fp, sha);
    }

    /// <summary>スペクトログラムのピーク（フレーム・ビン・大きさ dB）。</summary>
    private readonly record struct Peak(int Frame, int Bin, float Db);

    /// <summary>モノの音からピークを拾う。時刻順・同じ時刻ならビン順。</summary>
    private static List<Peak> FindPeaks(List<float> mono)
    {
        int frames = mono.Count < FftSize ? 0 : (mono.Count - FftSize) / HopSamples + 1;
        int bins = FftSize / 2;
        if (frames == 0) return new List<Peak>();

        // スペクトログラム（dB）。フレームごとに bins 個
        var spec = new float[frames * bins];
        var window = MakeHann(FftSize);
        var fft = new Fft(FftSize);
        var re = new float[FftSize];
        var im = new float[FftSize];
        float norm = 2f / FftSize;
        for (int f = 0; f < frames; f++)
        {
            int start = f * HopSamples;
            for (int n = 0; n < FftSize; n++)
            {
                re[n] = mono[start + n] * window[n];
                im[n] = 0f;
            }
            fft.Transform(re, im);
            int row = f * bins;
            for (int b = 0; b < bins; b++)
            {
                float mag = MathF.Sqrt(re[b] * re[b] + im[b] * im[b]) * norm;
                spec[row + b] = 20f * MathF.Log10(MathF.Max(mag, 1e-7f));
            }
        }

        // 時間・周波数の局所最大（直流に近いビンと最上位のビンは外す）
        var candidates = new List<Peak>();
        for (int f = 0; f < frames; f++)
        {
            int row = f * bins;
            for (int b = 1; b < bins - 1; b++)
            {
                float v = spec[row + b];
                if (v < MinPeakDb) continue;
                bool isMax = true;
                for (int df = -PeakTimeRadius; df <= PeakTimeRadius && isMax; df++)
                {
                    int ff = f + df;
                    if (ff < 0 || ff >= frames) continue;
                    int r2 = ff * bins;
                    for (int db = -PeakFreqRadius; db <= PeakFreqRadius; db++)
                    {
                        int bb = b + db;
                        if (bb < 0 || bb >= bins || (df == 0 && db == 0)) continue;
                        float u = spec[r2 + bb];
                        // 同じ値が並ぶときは時刻・ビンの小さいほうを採る
                        if (u > v || (u == v && (df < 0 || (df == 0 && db < 0)))) { isMax = false; break; }
                    }
                }
                if (isMax) candidates.Add(new Peak(f, b, v));
            }
        }

        // 1 秒のまとまりごとに大きい順で上限まで残す
        int framesPerSecond = (int)Math.Round((double)SampleRateHz / HopSamples);
        var peaks = new List<Peak>();
        foreach (var group in candidates.GroupBy(p => p.Frame / framesPerSecond).OrderBy(g => g.Key))
        {
            peaks.AddRange(group.OrderByDescending(p => p.Db).ThenBy(p => p.Frame).ThenBy(p => p.Bin).Take(PeaksPerSecond));
        }
        peaks.Sort((a, b) => a.Frame != b.Frame ? a.Frame.CompareTo(b.Frame) : a.Bin.CompareTo(b.Bin));
        return peaks;
    }

    /// <summary>ピークを対にしてハッシュにする。ハッシュ = ビン 1（8 ビット）・ビンの差 + 32（6 ビット）・時刻の差（6 ビット）。</summary>
    private static List<(uint Hash, uint Frame)> MakePairs(List<Peak> peaks)
    {
        var entries = new List<(uint, uint)>(peaks.Count * Fanout);
        for (int i = 0; i < peaks.Count; i++)
        {
            var a = peaks[i];
            int made = 0;
            for (int j = i + 1; j < peaks.Count && made < Fanout; j++)
            {
                var b = peaks[j];
                int dt = b.Frame - a.Frame;
                if (dt < PairMinDt) continue;
                if (dt >= PairMaxDt) break;
                int df = b.Bin - a.Bin;
                if (df < -PairMaxDf || df > PairMaxDf) continue;
                uint hash = ((uint)(a.Bin & 0xFF) << 12) | ((uint)((df + 32) & 0x3F) << 6) | (uint)(dt & 0x3F);
                entries.Add((hash, (uint)a.Frame));
                made++;
            }
        }
        return entries;
    }

    private static float[] MakeHann(int n)
    {
        var w = new float[n];
        for (int i = 0; i < n; i++) w[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / n);
        return w;
    }

    /// <summary>基数 2 の反復 FFT（点数は 2 のべき乗）。</summary>
    private sealed class Fft
    {
        private readonly int _n;
        private readonly int[] _rev;
        private readonly float[] _cos;
        private readonly float[] _sin;

        public Fft(int n)
        {
            if (n <= 0 || (n & (n - 1)) != 0) throw new ArgumentException("2 のべき乗にすること", nameof(n));
            _n = n;
            int bits = (int)Math.Log2(n);
            _rev = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                _rev[i] = r;
            }
            _cos = new float[n / 2];
            _sin = new float[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                double a = -2.0 * Math.PI * i / n;
                _cos[i] = (float)Math.Cos(a);
                _sin[i] = (float)Math.Sin(a);
            }
        }

        /// <summary>その場で変換する（re・im は長さ n）。</summary>
        public void Transform(float[] re, float[] im)
        {
            int n = _n;
            for (int i = 0; i < n; i++)
            {
                int j = _rev[i];
                if (j > i)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int size = 2; size <= n; size <<= 1)
            {
                int half = size >> 1;
                int step = n / size;
                for (int start = 0; start < n; start += size)
                {
                    for (int k = 0; k < half; k++)
                    {
                        int t = k * step;
                        float c = _cos[t], s = _sin[t];
                        int i1 = start + k, i2 = i1 + half;
                        float tr = re[i2] * c - im[i2] * s;
                        float ti = re[i2] * s + im[i2] * c;
                        re[i2] = re[i1] - tr;
                        im[i2] = im[i1] - ti;
                        re[i1] += tr;
                        im[i1] += ti;
                    }
                }
            }
        }
    }
}
