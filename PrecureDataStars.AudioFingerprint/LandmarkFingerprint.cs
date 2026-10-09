namespace PrecureDataStars.AudioFingerprint;

/// <summary>
/// 音の特徴量（ランドマーク指紋）1 本分。スペクトログラムのピークの対をハッシュにしたものを、
/// 出現したフレーム番号と組にして時刻順に並べて持つ。
/// </summary>
/// <remarks>
/// バイト列（<see cref="ToBytes"/>）は 1 項目 6 バイト（ハッシュ 3 バイト LE ＋ フレーム番号 3 バイト LE）の並びで、
/// ヘッダは持たない。取り方の条件（サンプリング周波数・FFT の点数・ホップ）は DB の列で別に持つ。
/// フレーム番号 × <see cref="HopSamples"/> ÷ <see cref="SampleRateHz"/> が秒。
/// </remarks>
public sealed class LandmarkFingerprint
{
    /// <summary>1 項目のバイト数（ハッシュ 3 ＋ フレーム 3）。</summary>
    public const int BytesPerEntry = 6;

    /// <summary>特徴量の取り方の版（アルゴリズムや定数を変えたら上げる。版が違う指紋どうしは突き合わせない）。</summary>
    public byte MethodVersion { get; init; }

    /// <summary>解析時のサンプリング周波数（Hz）。</summary>
    public int SampleRateHz { get; init; }

    /// <summary>FFT の点数。</summary>
    public int FftSize { get; init; }

    /// <summary>フレームの間隔（サンプル数）。</summary>
    public int HopSamples { get; init; }

    /// <summary>元の音の長さ（ミリ秒）。</summary>
    public int DurationMs { get; init; }

    /// <summary>項目（時刻順）。ハッシュは下位 20 ビットを使う。</summary>
    public IReadOnlyList<(uint Hash, uint Frame)> Entries { get; init; } = Array.Empty<(uint, uint)>();

    /// <summary>項目の数。</summary>
    public int HashCount => Entries.Count;

    /// <summary>DB に入れるバイト列（1 項目 6 バイト）。</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Entries.Count * BytesPerEntry];
        int p = 0;
        foreach (var (hash, frame) in Entries)
        {
            bytes[p++] = (byte)(hash & 0xFF);
            bytes[p++] = (byte)((hash >> 8) & 0xFF);
            bytes[p++] = (byte)((hash >> 16) & 0xFF);
            bytes[p++] = (byte)(frame & 0xFF);
            bytes[p++] = (byte)((frame >> 8) & 0xFF);
            bytes[p++] = (byte)((frame >> 16) & 0xFF);
        }
        return bytes;
    }

    /// <summary>バイト列から項目を戻す（<see cref="ToBytes"/> の逆）。</summary>
    public static (uint Hash, uint Frame)[] ParseEntries(ReadOnlySpan<byte> bytes)
    {
        int n = bytes.Length / BytesPerEntry;
        var entries = new (uint, uint)[n];
        for (int i = 0, p = 0; i < n; i++, p += BytesPerEntry)
        {
            uint hash = (uint)(bytes[p] | (bytes[p + 1] << 8) | (bytes[p + 2] << 16));
            uint frame = (uint)(bytes[p + 3] | (bytes[p + 4] << 8) | (bytes[p + 5] << 16));
            entries[i] = (hash, frame);
        }
        return entries;
    }
}
