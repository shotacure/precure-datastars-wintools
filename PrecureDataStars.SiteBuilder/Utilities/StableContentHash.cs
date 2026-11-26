using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 「このページは中身が変わったか」を判定するための印（MD5 の 16 進）を求める。サイトマップの更新日（<see cref="SitemapLastmodManifest"/>）と、
/// IndexNow へ送るページの選び方がこの印に従う。印を取るときだけ、中身の変化ではない部分を伏せる（表示は変わらない）。
/// <list type="bullet">
///   <item><description>ほかのデータで毎週変わる部分：<c>&lt;!--volatile--&gt;</c> 〜 <c>&lt;!--/volatile--&gt;</c> の囲み
///     （エピソード詳細の歴代記録のバッジ・スタッフの通算回数・パート尺の順位・サブタイトルの文字の初出など）。</description></item>
///   <item><description>見た目と速さの直しで変わる部分：head の CSS・JS・書体の読み込み（<c>&lt;link rel="stylesheet"&gt;</c>・<c>&lt;script src&gt;</c>・
///     Google Fonts の URL）、版の印 <c>?v=…</c>、画像の写しの選び方と読み込みの指定（<c>srcset</c>・<c>sizes</c>・<c>loading</c>・
///     <c>fetchpriority</c>・<c>decoding</c>）、自前の画像のファイル名（<c>/assets/</c>・<c>/subtitles/</c>・<c>/font-samples/</c> の <c>src</c>）。
///     CSS の区画分けや画像の写しのように、データが変わらない直しで全ページの更新日が進むと、検索エンジンが更新日を信じなくなるため。</description></item>
/// </list>
/// 印はページを書くときに手元の HTML から取る（<see cref="FromHtml"/>）。判定のしかたを変えたら <see cref="Version"/> を上げる
/// （控えの版が違う初回は、前の版のしかたの印 <see cref="LegacyFromHtml"/> で前回と比べ、印だけを今の版に取り直す）。
/// </summary>
public static class StableContentHash
{
    /// <summary>判定のしかたの版。伏せる部分を変えたら 1 つ上げる（サイトマップの控えに残し、版の違う初回は前の版の印で比べる）。</summary>
    public const int Version = 2;

    /// <summary>毎週変わる部分の囲みの始まり。</summary>
    public const string OpenMarker = "<!--volatile-->";

    /// <summary>毎週変わる部分の囲みの終わり。</summary>
    public const string CloseMarker = "<!--/volatile-->";

    private static readonly Regex VolatileRegex = new(
        Regex.Escape(OpenMarker) + ".*?" + Regex.Escape(CloseMarker), RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly byte[] OpenMarkerBytes = Encoding.UTF8.GetBytes(OpenMarker);

    /// <summary>
    /// 見た目と速さの直しで変わる部分（版 2 から伏せる）。上から順に当てる。
    /// </summary>
    private static readonly (Regex Pattern, string Replacement)[] PresentationMasks =
    {
        // CSS・書体の読み込み（区画の CSS の指し先、書体の CSS の URL）と、外のスクリプトの読み込み。
        (new Regex(@"<link\b[^>]*\brel=""(?:stylesheet|preconnect|preload|modulepreload)""[^>]*>", RegexOptions.Compiled), "<link>"),
        (new Regex(@"<script\b[^>]*\bsrc=""[^""]*""[^>]*>\s*</script>", RegexOptions.Compiled), "<script></script>"),
        // レイアウトのスクリプトが足す書体の CSS（Google Fonts の URL。ページの字で中身が変わる）。
        (new Regex(@"https://fonts\.googleapis\.com/[^'""\s<]*", RegexOptions.Compiled), "FONT"),
        // 画像の写しの選び方と読み込みの指定。
        (new Regex(@"\s(?:srcset|sizes|loading|fetchpriority|decoding)=""[^""]*""", RegexOptions.Compiled), ""),
        // 自前の画像のファイル名（写しの名前・版の印が変わっても、中身の変化ではない）。
        (new Regex(@"\bsrc=""/(?:assets|subtitles|font-samples)/[^""]*""", RegexOptions.Compiled), "src=\"\""),
        // 残った版の印（CSS・JS・画像の ?v=）。
        (new Regex(@"\?v=[0-9a-f]{6,}", RegexOptions.Compiled), ""),
    };

    /// <summary>囲みを持つページの URL（いまはエピソード詳細 <c>/series/{作品}/{話数}/</c> だけ）。ほかのページはファイルを読まずに済ませる。</summary>
    private static readonly Regex MarkedPageUrlRegex = new(@"^/series/[^/]+/\d+/$", RegexOptions.Compiled);

    /// <summary>その URL のページが囲みを持ちうるか。</summary>
    public static bool MayHaveMarkers(string urlPath) => MarkedPageUrlRegex.IsMatch(urlPath);

    /// <summary>
    /// 書き出すページの HTML から、今の版の印（毎週変わる部分と、見た目と速さの直しで変わる部分を伏せた中身の MD5。大文字の 16 進）を求める。
    /// </summary>
    public static string FromHtml(string html)
    {
        var masked = MaskVolatile(html);
        foreach (var (pattern, replacement) in PresentationMasks)
            masked = pattern.Replace(masked, replacement);
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(masked)));
    }

    /// <summary>
    /// 前の版（版 1）のしかたの印：毎週変わる部分だけを伏せた中身の MD5（大文字の 16 進）。控えの版が違う初回に、前回の印と比べるのに使う。
    /// </summary>
    public static string LegacyFromHtml(string html)
        => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(MaskVolatile(html))));

    /// <summary>毎週変わる部分の囲みの中を伏せる（囲みが無ければそのまま）。</summary>
    private static string MaskVolatile(string html)
        => html.Contains(OpenMarker, StringComparison.Ordinal)
            ? VolatileRegex.Replace(html, OpenMarker + CloseMarker)
            : html;

    /// <summary>
    /// 出力ファイルを読んで、今の版の印を求める（書くときに印を取っていないページの予備）。読めないときは空文字（呼び出し側で「変わった」扱い）。
    /// </summary>
    public static string FromFile(string filePath)
    {
        try
        {
            return FromHtml(File.ReadAllText(filePath, Encoding.UTF8));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 出力ファイルを読んで、前の版のしかたの印を求める。<paramref name="mayHaveMarkers"/> が false なら、書き出したときに取った印
    /// （<paramref name="writtenMd5"/>。ファイルそのものの MD5）を使う。読めないときは空文字。
    /// </summary>
    public static string LegacyFromFile(string filePath, bool mayHaveMarkers, Func<string, string?>? writtenMd5)
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
