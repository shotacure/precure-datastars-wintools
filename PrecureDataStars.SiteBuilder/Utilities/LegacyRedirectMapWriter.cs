using System.Text;
using System.Text.Json;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 旧 ID URL → 新 URL の転送表（<see cref="EntityUrlRegistry.LegacyRedirects"/>）を、サイト出力の
/// <c>_edge/legacy-redirects.json</c> に書き出すヘルパー。
/// <list type="bullet">
///   <item><description>本番デプロイが、この表を CloudFront の KeyValueStore へ差分で同期する
///     （<see cref="Deploy.KeyValueStoreSyncService"/>）。viewer-request の CloudFront Function
///     （<c>scripts/cloudfront/viewer-request.js</c>）が KeyValueStore を引いて 301 を返す。
///     ファイルそのものも通常のデプロイ（S3 差分同期）でバケットへ上がる。</description></item>
///   <item><description>形式は <c>{"/persons/123": "/people/%E9%AB%98…/", …}</c>。キーは末尾スラッシュ無しの旧パス、
///     値はパーセントエンコード済みの新 URL（ゲストキャラはアンカー付き）。キー順に並べて出力を決定的にする。</description></item>
///   <item><description><c>_edge/</c> は閲覧者から見せないファイルの置き場で、同じ CloudFront Function が
///     外部からのアクセスを 404 にする。sitemap には載せない。</description></item>
/// </list>
/// </summary>
public static class LegacyRedirectMapWriter
{
    /// <summary>出力ルートからの相対パス。KeyValueStore への同期（<see cref="Deploy.KeyValueStoreSyncService"/>）もここを読む。</summary>
    public const string RelativePath = "_edge/legacy-redirects.json";

    /// <summary>転送表を書き出す（UTF-8、BOM なし。中身が前回と同じなら書かない）。</summary>
    public static void Write(OutputWriter output, IReadOnlyList<LegacyRedirect> redirects)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in redirects) map[r.FromPath] = r.ToUrl;

        var path = Path.Combine(output.Root, RelativePath.Replace('/', Path.DirectorySeparatorChar));
        output.WriteText(path, JsonSerializer.Serialize(map));
    }
}
