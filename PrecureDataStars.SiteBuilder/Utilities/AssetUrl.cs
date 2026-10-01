using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// <c>/assets/</c> 配下の CSS・JS に、中身から作った版の印（<c>?v=ハッシュ先頭 10 桁</c>）を付けた URL を返す。
/// 同じ URL のまま中身だけ変わると、ブラウザや CDN が古いファイルを使い続けて表示が崩れるため、
/// 中身が変わったら URL も変わるようにする。テンプレートからは <c>{{ asset_url "/assets/site.css" }}</c> で呼ぶ。
/// ハッシュはファイルごとに 1 度だけ計算して覚えておく（並列レンダリングから呼ばれても安全）。
/// </summary>
public static class AssetUrl
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// <paramref name="path"/>（<c>/assets/site.css</c> のようなサイトルートからのパス）に版の印を付けて返す。
    /// 出力元の <c>wwwroot</c> にファイルが見つからないときは、印を付けずにそのまま返す。
    /// </summary>
    public static string Versioned(string path)
        => Cache.GetOrAdd(path, static p =>
        {
            var file = Path.Combine(AppContext.BaseDirectory, "wwwroot", p.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) return p;
            using var stream = File.OpenRead(file);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return $"{p}?v={hash[..10]}";
        });
}
