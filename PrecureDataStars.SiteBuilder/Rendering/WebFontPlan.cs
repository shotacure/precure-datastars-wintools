using System.Text;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// ページごとに読み込む Google Fonts の CSS を決める。
/// <para>
/// Google Fonts の CSS は 1 書体 1 太さごとに数十 KB あり、全部を全ページで読むと CSS だけで約 400KB になる。
/// そこで、ページの本文に実際に出てくる書体だけを、なるべく少ない字に絞って読む。
/// </para>
/// <list type="bullet">
///   <item><description>本文の Noto Sans JP（＋簡体字を補う Noto Sans SC・ギリシャ文字の Noto Sans）は、マウスで操作する端末だけで読む
///     （<see cref="DesktopCssUrl"/>）。スマホ・タブレットは端末に入っている日本語の書体（iPhone・iPad はヒラギノ、Android は Noto Sans CJK）で出す。
///     Mac もヒラギノが先に当たるので、実際に落とすのは Windows などヒラギノの無い端末だけ（site.css の <c>--font-base</c>）。</description></item>
///   <item><description>ブランド書体の Kiwi Maru と、数字に使う DotGothic16 は、出す文言が決まっているので、Google Fonts の <c>text=</c> で
///     その字だけに絞った書体を読む（<see cref="CommonCssUrls"/> の 1 本目。全ページで同じ URL になるので、2 ページ目からはキャッシュが効く）。</description></item>
///   <item><description>Kaisei Decol（コラムの学習ノートの札・404 の見出し）と Shippori Mincho（統計の字の図）は、その要素のあるページだけで読む。</description></item>
/// </list>
/// Kiwi Maru・DotGothic16 を新しい所に使うときは、出す文言を <see cref="BrandTexts"/>・<see cref="DotTexts"/> に足す（足さないと、その字だけ端末の書体で出る）。
/// </summary>
public static class WebFontPlan
{
    private const string CssBase = "https://fonts.googleapis.com/css2?";

    /// <summary>
    /// Kiwi Maru で出す決まった文言（サイト名の 3 つは設定から足す）。
    /// ヘッダ・フッタのワードマーク、フッタの連絡先、トップのタグライン、姉妹サイトの文字のカード、販売中の商品の枠の見出し、
    /// 商品・本の詳細の購入の枠の見出し。
    /// </summary>
    private static readonly string[] BrandTexts =
    {
        "X: @shota_",
        "shota@precure.tv",
        "プリキュアまるごとデータベース。",
        "プリキュアニュース",
        "precure-news-stage",
        "この作品の Blu-ray・CD",
        "Blu-ray・CD のご案内",
        "購入・閲覧",
        "購入・試聴",
    };

    /// <summary>
    /// DotGothic16 で出す字（フォーマット表の数値・帯グラフの総尺・パート尺統計の尺・トップのビルド情報）。
    /// ビルド情報の「『作品名』第N話(日付)時点」の作品名は、トップのページでだけ本文から拾って足す。
    /// </summary>
    private static readonly string[] DotTexts =
    {
        "0123456789:.,()/-+%",
        "—〜『』第話時点！",
    };

    /// <summary>トップのビルド情報の行（DotGothic16 で出す）。作品名の字をここから拾う。</summary>
    private static readonly System.Text.RegularExpressions.Regex BuildInfoRegex =
        new(@"class=""[^""]*\bhome-build-info\b[^""]*"">([^<]*)<", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 全端末で読む CSS の URL（1 本目は Kiwi Maru と DotGothic16 を字で絞ったもの。必要なページでは Kaisei Decol・Shippori Mincho を足す）。
    /// </summary>
    public static IReadOnlyList<string> CommonCssUrls(string contentHtml, LayoutModel layout, bool isNotFoundPage)
    {
        var urls = new List<string>(2);

        // Kiwi Maru と DotGothic16 は text= で 1 本にまとめる（text= はリクエスト内の全書体に効くので、
        // どちらの書体もこの字の和だけを持つ小さな書体になる）。
        var chars = new SortedSet<string>(StringComparer.Ordinal);
        AddChars(chars, layout.SiteName);
        AddChars(chars, layout.SiteNameJa);
        AddChars(chars, layout.SiteBrandLabel);
        foreach (var t in BrandTexts) AddChars(chars, t);
        foreach (var t in DotTexts) AddChars(chars, t);
        var buildInfo = BuildInfoRegex.Match(contentHtml);
        if (buildInfo.Success) AddChars(chars, System.Net.WebUtility.HtmlDecode(buildInfo.Groups[1].Value));
        chars.Remove(" ");
        urls.Add(CssBase + "family=Kiwi+Maru:wght@400;500&family=DotGothic16&display=swap&text="
            + Uri.EscapeDataString(string.Concat(chars)));

        // 字を絞れない飾りの書体は、要素のあるページだけで読む。
        var accents = new List<string>(2);
        if (isNotFoundPage || contentHtml.Contains("class=\"study-note", StringComparison.Ordinal))
            accents.Add("family=Kaisei+Decol:wght@700");
        if (contentHtml.Contains("stats-cloud", StringComparison.Ordinal)
            || contentHtml.Contains("stats-char-glyph", StringComparison.Ordinal)
            || contentHtml.Contains("stats-by-series-topchars-table", StringComparison.Ordinal)
            || contentHtml.Contains("stats-by-series-symbols-table", StringComparison.Ordinal))
            accents.Add("family=Shippori+Mincho:wght@800");
        if (accents.Count > 0)
            urls.Add(CssBase + string.Join("&", accents) + "&display=swap");

        return urls;
    }

    /// <summary>
    /// マウスで操作する端末だけで読む本文の書体の CSS の URL。
    /// Noto Sans SC は Noto Sans JP に字形の無い漢字（簡体字）が本文にあるページだけ、
    /// Noto Sans はギリシャ文字の指定（<c>lang="el"</c>・<c>lang="grc"</c>）があるページだけ足す。
    /// </summary>
    public static string DesktopCssUrl(string contentHtml)
    {
        var sb = new StringBuilder(CssBase).Append("family=Noto+Sans+JP:wght@400;500;700");
        if (HasHanMissingFromNotoSansJp(contentHtml))
            sb.Append("&family=Noto+Sans+SC:wght@400;500;700");
        if (contentHtml.Contains("lang=\"el\"", StringComparison.Ordinal)
            || contentHtml.Contains("lang=\"grc\"", StringComparison.Ordinal))
            sb.Append("&family=Noto+Sans:wght@400;700");
        return sb.Append("&display=swap").ToString();
    }

    /// <summary>文字列の字（サロゲートペアは 1 字にまとめる）を集合に足す。</summary>
    private static void AddChars(SortedSet<string> set, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var e = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) set.Add((string)e.Current);
    }

    /// <summary>漢字の範囲のうち、Noto Sans JP に字形のある字の表（初回に 1 度だけ作る）。</summary>
    private static readonly Lazy<HashSet<int>?> NotoSansJpHan = new(LoadNotoSansJpHan);

    /// <summary>漢字の範囲（CJK 統合漢字・拡張 A・互換漢字・拡張 B 以降）か。</summary>
    private static bool IsHan(int cp) =>
        (cp >= 0x3400 && cp <= 0x4DBF) || (cp >= 0x4E00 && cp <= 0x9FFF)
        || (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0x20000 && cp <= 0x323AF);

    private static HashSet<int>? LoadNotoSansJpHan()
    {
        // OGP カードの描画と同じ、出力フォルダの Fonts/NotoSansJP.ttf（Google Fonts で配られている Noto Sans JP と同じ字の範囲）。
        var path = Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansJP.ttf");
        if (!File.Exists(path)) return null;
        using var typeface = SKTypeface.FromFile(path);
        if (typeface is null) return null;
        using var font = new SKFont(typeface);
        var set = new HashSet<int>();
        void Scan(int from, int to)
        {
            for (var cp = from; cp <= to; cp++)
                if (font.ContainsGlyph(cp)) set.Add(cp);
        }
        Scan(0x3400, 0x4DBF);
        Scan(0x4E00, 0x9FFF);
        Scan(0xF900, 0xFAFF);
        Scan(0x20000, 0x323AF);
        return set;
    }

    /// <summary>
    /// 本文に Noto Sans JP の持たない漢字があるか。書体のファイルが読めないときは、これまでどおり Noto Sans SC を読む側に倒す。
    /// </summary>
    private static bool HasHanMissingFromNotoSansJp(string html)
    {
        var covered = NotoSansJpHan.Value;
        if (covered is null) return true;
        for (var i = 0; i < html.Length; i++)
        {
            var c = html[i];
            if (c < 0x3400) continue;
            int cp = c;
            if (char.IsHighSurrogate(c) && i + 1 < html.Length && char.IsLowSurrogate(html[i + 1]))
            {
                cp = char.ConvertToUtf32(c, html[i + 1]);
                i++;
            }
            if (IsHan(cp) && !covered.Contains(cp)) return true;
        }
        return false;
    }
}
