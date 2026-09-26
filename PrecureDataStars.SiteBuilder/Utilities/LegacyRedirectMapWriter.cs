using System.Text;
using System.Text.Json;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 旧 ID URL → 新 URL の転送表（<see cref="EntityUrlRegistry.LegacyRedirects"/>）を、サイト出力の
/// <c>_edge/legacy-redirects.json</c> に書き出すヘルパー。
/// <list type="bullet">
///   <item><description>通常のデプロイ（S3 差分同期）でそのままバケットへ上がり、origin-request の Lambda@Edge
///     （<c>scripts/lambda-edge/legacy-redirect/index.mjs</c>）が S3 から読んで 301 を返すのに使う。</description></item>
///   <item><description>形式は <c>{"/persons/123": "/people/%E9%AB%98…/", …}</c>。キーは末尾スラッシュ無しの旧パス、
///     値はパーセントエンコード済みの新 URL（ゲストキャラはアンカー付き）。キー順に並べて出力を決定的にする。</description></item>
///   <item><description><c>_edge/</c> は閲覧者から見せないファイルの置き場で、viewer-request の CloudFront Function
///     （<c>scripts/cloudfront/viewer-request.js</c>）が外部からのアクセスを 404 にする。sitemap には載せない。</description></item>
/// </list>
/// </summary>
public static class LegacyRedirectMapWriter
{
    /// <summary>出力ルートからの相対パス。Lambda@Edge 側の読み込みキーと一致させる。</summary>
    public const string RelativePath = "_edge/legacy-redirects.json";

    /// <summary>転送表を書き出す。</summary>
    public static void Write(string outputRoot, IReadOnlyList<LegacyRedirect> redirects)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in redirects) map[r.FromPath] = r.ToUrl;

        var path = Path.Combine(outputRoot, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        PathUtil.EnsureParentDirectory(path);
        File.WriteAllText(path, JsonSerializer.Serialize(map), new UTF8Encoding(false));
    }
}
