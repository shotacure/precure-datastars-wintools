namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// ページのテンプレートを、配る CSS の区画（<c>/assets/css/{区画}.css</c>）に振り分ける表。
/// 人がページを渡り歩く単位（サイトの区画）でまとめ、同じ区画の中を移るあいだは CSS をキャッシュから使い回せるようにする。
/// 区画ごとの CSS の中身は <see cref="CssBundler"/> が、その区画のページに出てくる語から作る。
/// <para>
/// 新しい種類のページ（テンプレート）を足したら、ここに 1 行足す。表に無いテンプレートのページは、全部入りの
/// <c>/assets/site.css</c> を読む（見た目は崩れない）。ビルドは表に無いテンプレートを知らせる。
/// </para>
/// </summary>
public static class CssBundlePlan
{
    /// <summary>
    /// テンプレート名（<c>.sbn</c> を除いた形）の当て方と区画。上から順に当て、最初に当たった区画にする。
    /// <c>IsPrefix</c> が true の行は名前の頭の一致で当てる（統計のようにテンプレート名をプログラムの中で組み立てるものもある）。
    /// </summary>
    private static readonly (string Pattern, bool IsPrefix, string Group)[] Rules =
    {
        ("home", false, "home"),
        ("series-", true, "series"),
        ("episode-", true, "series"),
        ("episodes-", true, "series"),
        ("persons-", true, "people"),
        ("creators-", true, "people"),
        ("companies-", true, "people"),
        ("units-", true, "people"),
        ("characters-", true, "characters"),
        ("precures-", true, "characters"),
        ("songs-", true, "music"),
        ("bgms-", true, "music"),
        ("music-", true, "music"),
        ("products-", true, "music"),
        ("books-", true, "music"),
        ("stats-", true, "stats"),
        ("article-", true, "articles"),
        ("articles-", true, "articles"),
        ("anniversary-", true, "anniversary"),
        ("fonts-", true, "fonts"),
        ("about", false, "info"),
        ("privacy", false, "info"),
        ("disclaimer", false, "info"),
        ("contact", false, "info"),
        ("sources", false, "info"),
        ("not-found", false, "info"),
    };

    /// <summary>区画の名前の一覧（表に出てくる順）。</summary>
    public static IReadOnlyList<string> Groups { get; } = Rules.Select(r => r.Group).Distinct().ToList();

    /// <summary>
    /// どのページにも常にあるとみなす語。HTML の文字には出てこないが、ブラウザが自分で足す要素
    /// （<c>tbody</c> など）と、利用者の操作でブラウザが付ける属性（details・dialog の <c>open</c>）。
    /// 外のスクリプトが足す要素（広告の <c>ins.adsbygoogle</c> など）に site.css で見た目を付けるときも、その語をここに足す。
    /// タグ名と属性名は小文字で書く。
    /// </summary>
    public static IReadOnlyList<string> AlwaysPresentWords { get; } = new[]
    {
        "html", "head", "body", "tbody", "thead", "colgroup", "open",
    };

    /// <summary>テンプレート名（<c>episode-detail.sbn</c> のように拡張子付きでもよい）の区画。表に無ければ null。</summary>
    public static string? GroupOf(string templateName)
    {
        var name = templateName.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase)
            ? templateName[..^4]
            : templateName;
        foreach (var (pattern, isPrefix, group) in Rules)
        {
            if (isPrefix ? name.StartsWith(pattern, StringComparison.Ordinal) : string.Equals(name, pattern, StringComparison.Ordinal))
                return group;
        }
        return null;
    }

    /// <summary>
    /// 表と常にあるとみなす語を、版の印（<see cref="CssBundler.Version"/>）に混ぜるための文字列にする。
    /// 表を変えれば印が変わるので、手で版を上げ忘れても、同じ印のまま区画の中身が減ることがない。
    /// </summary>
    public static string Describe()
        => string.Join("\n", Rules.Select(r => $"{r.Pattern}|{(r.IsPrefix ? "prefix" : "exact")}|{r.Group}"))
           + "\nalways:" + string.Join(",", AlwaysPresentWords);
}
