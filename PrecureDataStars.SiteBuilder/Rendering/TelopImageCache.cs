using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// テロップの体裁で描いた画像（装飾込みの字の並び 1 つ）の作り置き。OGP カードの見出しに載せるサブタイトルは、
/// 装飾を画素で重ねるので 1 つ描くのに数秒かかる。カードの組み方を変えてカードを描き直すときも、
/// 同じ文字・書体・組み方・字の大きさの装飾は描き直さずにここから使う。
/// <para>
/// 置くのは <c>{鍵}.telop</c>。中身は見出し（印・幅・高さ・置くときの基準の 5 つの値）と、画素（RGBA・乗算済みアルファ）を
/// そのまま可逆圧縮したもの。PNG にすると半透明の画素が丸めで変わるので通さない。
/// 鍵はテロップの描画コードの印（<see cref="TelopRenderSourceStamp"/>）から作るので、カードの組み方のコードを変えても変わらない。
/// </para>
/// <para>
/// 使った作り置きは更新時刻を今に寄せ、全体ビルドの最後に <see cref="UnusedDays"/> 日使われなかったものだけを消す
/// （作業中のコードのビルドと、別の版のコードのビルドが、互いの作り置きを消し合わないように）。
/// 鍵に入らない理由で絵が変わっていないかは、<see cref="TakeVerifySlot"/> の数だけ描き直して突き合わせる。
/// </para>
/// </summary>
internal sealed class TelopImageCache
{
    /// <summary>作り置き 1 つ。<paramref name="Pixels"/> は RGBA・乗算済みアルファを行優先で詰めたもの（幅 × 高さ × 4 バイト）。</summary>
    public sealed record Entry(int Width, int Height, byte[] Pixels, float Baseline, float Left, float LineWidth, float OriginX, float OriginY)
    {
        /// <summary>画素と基準の値がすべて同じか。</summary>
        public bool SameAs(Entry other) =>
            Width == other.Width && Height == other.Height
            && Baseline == other.Baseline && Left == other.Left && LineWidth == other.LineWidth
            && OriginX == other.OriginX && OriginY == other.OriginY
            && Pixels.AsSpan().SequenceEqual(other.Pixels);
    }

    /// <summary>ファイルの頭の印。中身の並びを変えたら替える（前の並びのファイルは読まずに描き直す）。</summary>
    private const uint Magic = 0x31504C54; // "TLP1"

    /// <summary>この日数のあいだ使われなかった作り置きを、全体ビルドの最後に消す。</summary>
    public const int UnusedDays = 14;

    /// <summary>1 回のビルドで、使った作り置きを描き直して突き合わせる数。</summary>
    private const int VerifyCount = 3;

    private readonly string _directory;
    private readonly bool _refresh;
    private readonly ConcurrentDictionary<string, byte> _keysUsed = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _mismatches = new();
    private int _verifySlots = VerifyCount;
    private int _hits;
    private int _drawn;

    /// <param name="directory">置き場所（絶対パス）。</param>
    /// <param name="refresh">作り置きを使わずに描き直すか（描いたものは置き直す）。</param>
    public TelopImageCache(string directory, bool refresh)
    {
        _directory = directory;
        _refresh = refresh;
    }

    /// <summary>作り置きを使った数と、描いて作り置きに足した数。ビルド後のサマリ用。</summary>
    public (int Hits, int Drawn) Counts => (_hits, _drawn);

    /// <summary>描き直したら作り置きと絵が違った文字列（ビルド後の警告用）。</summary>
    public IReadOnlyCollection<string> Mismatches => _mismatches.ToArray();

    /// <summary>
    /// 作り置きの鍵（16 進 32 桁）。テロップの描画コードの印・描き方の版・SkiaSharp の版・書体名・組み方・文字列・字の大きさ・
    /// 既定の見出し書体のファイル名（書体名が空のときに使う書体）のどれかが変われば変わる。書体はファイルの中身ではなく名前で見る。
    /// </summary>
    public static string Key(string rubyHtml, string fontFamily, SubtitleTelopProfile profile, float? fixedSize, string headingFontFile)
    {
        var material = string.Join('\n',
            TelopRenderSourceStamp.Value,
            OgCardRenderer.TelopRenderVersion.ToString(CultureInfo.InvariantCulture),
            SkiaSharpVersion.Native.ToString(),
            fontFamily,
            JsonSerializer.Serialize(profile),
            rubyHtml,
            fixedSize?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            headingFontFile);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32].ToLowerInvariant();
    }

    private string PathOf(string key) => Path.Combine(_directory, key + ".telop");

    /// <summary>
    /// 作り置きを読む。無い・読めない・描き直しの指定があるときは null。読めたら使った鍵として控え、
    /// 更新時刻が 1 日より前なら今に寄せる（使われているあいだは掃除で消えないように）。
    /// </summary>
    public Entry? TryRead(string key)
    {
        _keysUsed.TryAdd(key, 0);
        if (_refresh) return null;
        var path = PathOf(key);
        try
        {
            if (!File.Exists(path)) return null;
            Entry entry;
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != Magic) return null;
                int width = reader.ReadInt32(), height = reader.ReadInt32();
                float baseline = reader.ReadSingle(), left = reader.ReadSingle(), lineWidth = reader.ReadSingle();
                float originX = reader.ReadSingle(), originY = reader.ReadSingle();
                if (width <= 0 || height <= 0 || (long)width * height * 4 > int.MaxValue) return null;
                var pixels = new byte[width * height * 4];
                using var zlib = new ZLibStream(stream, CompressionMode.Decompress);
                zlib.ReadExactly(pixels);
                entry = new Entry(width, height, pixels, baseline, left, lineWidth, originX, originY);
            }
            if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-1)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            Interlocked.Increment(ref _hits);
            return entry;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // 書きかけ・壊れたファイル・ほかのビルドが置き換えている最中は、無いものとして描き直す
            return null;
        }
    }

    /// <summary>作り置きを置く。一時ファイルに書いてから置き換えるので、同じ鍵を同時に書いても壊れない。</summary>
    public void Write(string key, Entry entry)
    {
        Directory.CreateDirectory(_directory);
        var path = PathOf(key);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        using (var stream = File.Create(temp))
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(entry.Width);
                writer.Write(entry.Height);
                writer.Write(entry.Baseline);
                writer.Write(entry.Left);
                writer.Write(entry.LineWidth);
                writer.Write(entry.OriginX);
                writer.Write(entry.OriginY);
            }
            using var zlib = new ZLibStream(stream, CompressionLevel.Optimal);
            zlib.Write(entry.Pixels);
        }
        File.Move(temp, path, overwrite: true);
        Interlocked.Increment(ref _drawn);
    }

    /// <summary>使った作り置きを描き直して突き合わせる番か（1 回のビルドで <see cref="VerifyCount"/> 回まで true）。</summary>
    public bool TakeVerifySlot() => Interlocked.Decrement(ref _verifySlots) >= 0;

    /// <summary>描き直したら作り置きと絵が違った、と控える。</summary>
    public void ReportMismatch(string rubyHtml) => _mismatches.Enqueue(rubyHtml);

    /// <summary>
    /// 今回のビルドで使わず、<see cref="UnusedDays"/> 日のあいだ使われなかった作り置きと、1 日より前の描きかけの一時ファイルを消す。
    /// 戻り値は消した作り置きの数。全体ビルドの最後に呼ぶ。
    /// </summary>
    public int Prune()
    {
        if (!Directory.Exists(_directory)) return 0;
        var unusedBefore = DateTime.UtcNow.AddDays(-UnusedDays);
        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.telop"))
        {
            if (_keysUsed.ContainsKey(Path.GetFileNameWithoutExtension(file))) continue;
            if (File.GetLastWriteTimeUtc(file) >= unusedBefore) continue;
            File.Delete(file);
            removed++;
        }
        var staleBefore = DateTime.UtcNow.AddDays(-1);
        foreach (var file in Directory.EnumerateFiles(_directory, "*.tmp"))
            if (File.GetLastWriteTimeUtc(file) < staleBefore) File.Delete(file);
        return removed;
    }
}
