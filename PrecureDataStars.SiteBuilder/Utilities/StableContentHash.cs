using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 「このページは中身が変わったか」を判定するための印（MD5 の 16 進）を求める。
/// ページの中で、ほかのデータで毎週変わる部分を <c>&lt;!--volatile--&gt;</c> 〜 <c>&lt;!--/volatile--&gt;</c> で囲んでおくと、
/// その部分を伏せて印を取る（エピソード詳細の歴代記録のバッジ・スタッフの通算回数・パート尺の順位・サブタイトルの文字の初出など）。
/// 囲みの無いページは、書き出したときの印（<c>OutputWriter.Md5Of</c>）をそのまま使う。
/// サイトマップの更新日（<see cref="SitemapLastmodManifest"/>）と、IndexNow へ送るページの選び方がこの印に従う。
/// </summary>
public static class StableContentHash
{
    /// <summary>毎週変わる部分の囲みの始まり。</summary>
    public const string OpenMarker = "<!--volatile-->";

    /// <summary>毎週変わる部分の囲みの終わり。</summary>
    public const string CloseMarker = "<!--/volatile-->";

    private static readonly Regex VolatileRegex = new(
        Regex.Escape(OpenMarker) + ".*?" + Regex.Escape(CloseMarker), RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly byte[] OpenMarkerBytes = Encoding.UTF8.GetBytes(OpenMarker);

    /// <summary>囲みを持つページの URL（いまはエピソード詳細 <c>/series/{作品}/{話数}/</c> だけ）。ほかのページはファイルを読まずに済ませる。</summary>
    private static readonly Regex MarkedPageUrlRegex = new(@"^/series/[^/]+/\d+/$", RegexOptions.Compiled);

    /// <summary>その URL のページが囲みを持ちうるか。</summary>
    public static bool MayHaveMarkers(string urlPath) => MarkedPageUrlRegex.IsMatch(urlPath);

    /// <summary>
    /// 出力ファイルの印を求める。囲みのあるファイルは囲みの中を伏せた中身の MD5、無いファイルは <paramref name="writtenMd5"/>
    /// が返す印（無ければファイルの MD5）。読めないときは空文字（呼び出し側で「変わった」扱い）。
    /// </summary>
    /// <param name="filePath">出力ファイルの絶対パス。</param>
    /// <param name="mayHaveMarkers">囲みがありうるページか（false ならファイルを読まずに <paramref name="writtenMd5"/> を使う）。</param>
    /// <param name="writtenMd5">書き出したときに取った印（<c>OutputWriter.Md5Of</c>）。null なら使わない。</param>
    public static string Compute(string filePath, bool mayHaveMarkers, Func<string, string?>? writtenMd5)
    {
        if (!mayHaveMarkers)
        {
            var known = writtenMd5?.Invoke(filePath);
            if (!string.IsNullOrEmpty(known)) return known.ToUpperInvariant();
        }
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.AsSpan().IndexOf(OpenMarkerBytes) < 0)
                return Convert.ToHexString(MD5.HashData(bytes));
            var masked = VolatileRegex.Replace(Encoding.UTF8.GetString(bytes), OpenMarker + CloseMarker);
            return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(masked)));
        }
        catch
        {
            return "";
        }
    }
}
