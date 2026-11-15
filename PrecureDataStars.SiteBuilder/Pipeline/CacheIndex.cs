using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 作り置きの索引（鍵 → 値と、最後に使った日）。1 つの JSON ファイルに置き、ビルドの始めに読んで終わりに書く。
/// <list type="bullet">
///   <item><description>OGP カードは「鍵 → 描いた画素のハッシュ」を持つ（<see cref="OgCardStore"/>）。
///     各話のテロップ画像は値を持たず、鍵を最後に使った日だけを持つ（<see cref="SubtitleTelopPreflight"/>）。</description></item>
///   <item><description>書くときは、ファイルを読み直してから今回使った行を重ねる（同じ時間に別のビルドが書いた行を落とさない）。
///     一時ファイルに書いてから置き換えるので、途中で止まっても壊れない。</description></item>
///   <item><description>掃除は日数で行う。使わなかった行をその場で消すと、中身の違うコードのビルドどうし
///     （作業中のコードのテストビルドと、定時の本番ビルド）が互いの作り置きを消し合う。</description></item>
/// </list>
/// ページの並列レンダリングから同時に呼ばれる。
/// </summary>
internal sealed class CacheIndex
{
    /// <summary>索引の 1 行。<paramref name="Value"/> は鍵に結び付く値（無ければ空）、<paramref name="Day"/> は最後に使った日（yyyyMMdd）。</summary>
    public sealed record Entry(
        [property: JsonPropertyName("v")] string Value,
        [property: JsonPropertyName("d")] int Day);

    /// <summary>この日数のあいだ使われなかった行を、全体ビルドの最後に消す。</summary>
    public const int UnusedDays = 14;

    private readonly string _path;
    private readonly int _today;
    private readonly ConcurrentDictionary<string, Entry> _entries;
    /// <summary>今回のビルドで使った行・足した行（書くときにファイルの中身へ重ねる分）。</summary>
    private readonly ConcurrentDictionary<string, Entry> _touched = new(StringComparer.Ordinal);

    public CacheIndex(string path)
    {
        _path = path;
        _today = DayOf(DateTime.Today);
        _entries = new ConcurrentDictionary<string, Entry>(Load(path), StringComparer.Ordinal);
    }

    private static int DayOf(DateTime date) => date.Year * 10000 + date.Month * 100 + date.Day;

    /// <summary>索引のファイルを読む。無いか読めなければ空（作り置きを描き直すことになるだけで、害は無い）。</summary>
    private static Dictionary<string, Entry> Load(string path)
    {
        try
        {
            if (File.Exists(path)
                && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllBytes(path)) is { } loaded)
                return loaded.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new Dictionary<string, Entry>(StringComparer.Ordinal);
    }

    /// <summary>鍵の値を引き、今日使った行として控える。行が無ければ null。</summary>
    public string? Get(string key)
    {
        if (!_entries.TryGetValue(key, out var entry)) return null;
        Touch(key, entry.Value);
        return entry.Value;
    }

    /// <summary>鍵の行があるか（使ったことにはしない）。</summary>
    public bool Contains(string key) => _entries.ContainsKey(key);

    /// <summary>鍵に値を結び付け、今日使った行として控える。</summary>
    public void Set(string key, string value) => Touch(key, value);

    private void Touch(string key, string value)
    {
        var entry = new Entry(value, _today);
        _entries[key] = entry;
        _touched[key] = entry;
    }

    /// <summary>いま索引にある行の値（重なりを除く）。</summary>
    public IReadOnlySet<string> Values => _entries.Values.Select(e => e.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 索引を書く。ファイルを読み直して今回使った行を重ね、<paramref name="prune"/> なら <see cref="UnusedDays"/> 日
    /// 使われなかった行を落とす。戻り値は落とした行の数。
    /// </summary>
    public int Save(bool prune)
    {
        var merged = Load(_path);
        foreach (var (key, entry) in _touched) merged[key] = entry;
        int removed = 0;
        if (prune)
        {
            int cutoff = DayOf(DateTime.Today.AddDays(-UnusedDays));
            foreach (var key in merged.Where(kv => kv.Value.Day < cutoff).Select(kv => kv.Key).ToList())
            {
                merged.Remove(key);
                removed++;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(new SortedDictionary<string, Entry>(merged, StringComparer.Ordinal)));
        File.Move(temp, _path, overwrite: true);

        _entries.Clear();
        foreach (var (key, entry) in merged) _entries[key] = entry;
        return removed;
    }
}
