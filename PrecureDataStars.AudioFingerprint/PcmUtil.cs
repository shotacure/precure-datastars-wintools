namespace PrecureDataStars.AudioFingerprint;

/// <summary>PCM の下ごしらえ：16 ビット LE ステレオの混合（モノ化）と、4 分の 1 への間引き（ローパス付き）。</summary>
public static class PcmUtil
{
    /// <summary>16 ビット LE のサンプル列を [-1, 1] の float にし、チャンネル数ぶんを平均してモノにする。</summary>
    /// <param name="pcm16">インターリーブされた 16 ビット LE のサンプル列。</param>
    /// <param name="channels">チャンネル数（CD-DA は 2）。</param>
    public static float[] ToMono(ReadOnlySpan<byte> pcm16, int channels)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        int frameBytes = 2 * channels;
        int frames = pcm16.Length / frameBytes;
        var mono = new float[frames];
        float scale = 1f / (32768f * channels);
        for (int i = 0, p = 0; i < frames; i++)
        {
            int sum = 0;
            for (int c = 0; c < channels; c++, p += 2)
            {
                sum += (short)(pcm16[p] | (pcm16[p + 1] << 8));
            }
            mono[i] = sum * scale;
        }
        return mono;
    }

    /// <summary>
    /// 4 分の 1 に間引く FIR ローパス（窓付き sinc、Hamming 窓）。
    /// 入力を分けて渡しても境界で切れないように、直前の入力をフィルタの長さぶん持ち越す。
    /// </summary>
    public sealed class Decimator4
    {
        private const int Factor = 4;
        private static readonly float[] Taps = DesignLowPass(taps: 48, cutoff: 0.11f);

        private readonly float[] _history = new float[Taps.Length - 1];
        /// <summary>次に出力を取る入力の位置（持ち越し分を含めた並びでの位置）。</summary>
        private int _phase;

        /// <summary>入力（元のサンプリング周波数）を受けて、間引いた出力を返す。</summary>
        public float[] Process(ReadOnlySpan<float> input)
        {
            int h = _history.Length;
            // 持ち越し ＋ 今回の入力
            var buf = new float[h + input.Length];
            _history.CopyTo(buf, 0);
            input.CopyTo(buf.AsSpan(h));

            // 出力は入力 Factor 個ごと。buf[i] を中心に Taps を畳み込む（i は Taps.Length-1 以上）
            var outList = new List<float>(input.Length / Factor + 1);
            int taps = Taps.Length;
            int i = h + _phase;
            for (; i < buf.Length; i += Factor)
            {
                float acc = 0f;
                int start = i - (taps - 1);
                for (int k = 0; k < taps; k++) acc += buf[start + k] * Taps[k];
                outList.Add(acc);
            }
            _phase = i - buf.Length;

            // 末尾 h サンプルを持ち越す
            if (buf.Length >= h) Array.Copy(buf, buf.Length - h, _history, 0, h);
            return outList.ToArray();
        }

        /// <summary>窓付き sinc のローパス FIR を作る（cutoff は入力のサンプリング周波数に対する比）。</summary>
        private static float[] DesignLowPass(int taps, float cutoff)
        {
            var w = new float[taps];
            double m = taps - 1;
            double sum = 0;
            for (int n = 0; n < taps; n++)
            {
                double x = n - m / 2.0;
                double sinc = x == 0 ? 2.0 * cutoff : Math.Sin(2.0 * Math.PI * cutoff * x) / (Math.PI * x);
                double hamming = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * n / m);
                w[n] = (float)(sinc * hamming);
                sum += w[n];
            }
            for (int n = 0; n < taps; n++) w[n] = (float)(w[n] / sum);
            return w;
        }
    }
}
