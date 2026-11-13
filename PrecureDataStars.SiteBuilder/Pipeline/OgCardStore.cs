using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// OGP カード画像の作り置き。2 段で持つ。
/// <list type="bullet">
///   <item><description>1 段目は索引（<c>index.json</c>）で、カードの鍵（<see cref="OgCardRenderer.CacheKey"/>。描画コードの印・書体・材料一式）
///     から、描いた画素のハッシュを引く。</description></item>
///   <item><description>2 段目は画像で、<c>{画素のハッシュ}.png</c> の名前で置く。</description></item>
/// </list>
/// 描画コードを変えると鍵は全部変わるが、描いた画素が前と同じカードは、同じ PNG を指す行を索引に足すだけで済む。
/// PNG は書き直さないので、出力のファイルも変わらず、デプロイの差分にも sitemap の更新日にも入らない。
/// <para>
/// 掃除は全体ビルドの最後に行い、<see cref="CacheIndex.UnusedDays"/> 日使われなかった行と、どの行からも指されない画像を消す。
/// 作り置きはテストと本番で共用する。
/// </para>
/// ページの並列レンダリングと、カードを描く仕事から同時に呼ばれる。
/// </summary>
internal sealed class OgCardStore
{
    private readonly string _directory;
    private readonly bool _refresh;
    private readonly CacheIndex _index;
    /// <summary>同じ画素のカードを同時に描き終えたときに、PNG を 1 度だけ書くための錠（画素のハッシュごと）。</summary>
    private readonly object[] _writeLocks = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    private int _pngsWritten;

    /// <param name="directory">置き場所（絶対パス）。</param>
    /// <param name="refresh">索引を引かずに描き直すか（<c>--refresh-og</c>）。描いた画素が同じなら PNG は書き直さない。</param>
    public OgCardStore(string directory, bool refresh)
    {
        _directory = directory;
        _refresh = refresh;
        _index = new CacheIndex(Path.Combine(directory, "index.json"));
    }

    /// <summary>今回のビルドで新しく書いた PNG の数（描いたが、同じ画素の PNG がすでにあった分は含まない）。</summary>
    public int PngsWritten => _pngsWritten;

    private string PngPath(string pixelHash) => Path.Combine(_directory, pixelHash + ".png");

    /// <summary>鍵のカードの画像ファイル。索引に無い・画像が無い・描き直しの指定があるときは null。</summary>
    public string? TryGet(string key)
    {
        if (_refresh) return null;
        if (_index.Get(key) is not { } pixelHash) return null;
        var path = PngPath(pixelHash);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 描いたカードを作り置きに入れ、その画像ファイルを返す。同じ画素の PNG がすでにあれば書かずに、索引の行だけを足す。
    /// PNG は一時ファイルに書いてから置き換える。
    /// </summary>
    public string Put(string key, OgCardDrawing drawn)
    {
        string pixelHash = drawn.PixelHash();
        var path = PngPath(pixelHash);
        lock (_writeLocks[(pixelHash[0] * 31 + pixelHash[1]) % _writeLocks.Length])
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(_directory);
                var temp = $"{path}.{Guid.NewGuid():N}.tmp";
                drawn.SavePng(temp);
                File.Move(temp, path, overwrite: true);
                Interlocked.Increment(ref _pngsWritten);
            }
        }
        _index.Set(key, pixelHash);
        return path;
    }

    /// <summary>
    /// 索引を書く。<paramref name="prune"/>（全体ビルド）なら、<see cref="CacheIndex.UnusedDays"/> 日使われなかった行を落とし、
    /// どの行からも指されない画像と描きかけの一時ファイルを消す（どちらも 1 日より前のものだけ。同じ時間に別のビルドが
    /// 書いたばかりで、まだ索引に載っていない画像を消さないように）。戻り値は落とした行の数と消した画像の数。
    /// </summary>
    public (int Entries, int Images) Finish(bool prune)
    {
        int entries = _index.Save(prune);
        if (!prune || !Directory.Exists(_directory)) return (entries, 0);

        var referenced = _index.Values;
        var staleBefore = DateTime.UtcNow.AddDays(-1);
        int images = 0;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.png"))
        {
            if (referenced.Contains(Path.GetFileNameWithoutExtension(file))) continue;
            if (File.GetLastWriteTimeUtc(file) >= staleBefore) continue;
            File.Delete(file);
            images++;
        }
        foreach (var file in Directory.EnumerateFiles(_directory, "*.tmp"))
            if (File.GetLastWriteTimeUtc(file) < staleBefore) File.Delete(file);
        return (entries, images);
    }
}
