namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 一覧のページ（作品一覧・プリキュア一覧・キャラクター一覧・役職のページ）に置く Schema.org の <c>ItemList</c> 構造化データ。
/// 各項目は、一覧から個別のページへ飛ぶ形（Google の「概要ページ」の形）で、順番（position）・個別のページの絶対 URL・名前を持つ。
/// 同じ URL は最初の 1 つだけにする。<paramref name="numberOfItems"/> を渡すと、項目を絞ったときの全体の件数を出す。
/// </summary>
public static class ItemListJsonLd
{
    /// <summary>ItemList の JSON-LD 文字列を作る。項目が 1 つも無ければ空文字（ページに出さない）。</summary>
    /// <param name="baseUrl">サイトの絶対 URL の頭（<c>https://precure.tv</c>）。</param>
    /// <param name="name">一覧の名前（ページの見出し）。</param>
    /// <param name="items">並び順の項目（サイト内のパスと名前）。</param>
    /// <param name="numberOfItems">一覧の全体の件数（項目を絞ったとき）。null なら項目の数。</param>
    public static string Build(string baseUrl, string name, IEnumerable<(string Url, string Name)> items, int? numberOfItems = null)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var elements = new List<Dictionary<string, object?>>();
        foreach (var (url, itemName) in items)
        {
            if (string.IsNullOrEmpty(url) || !seen.Add(url)) continue;
            elements.Add(new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = elements.Count + 1,
                ["url"] = baseUrl + url,
                ["name"] = itemName
            });
        }
        if (elements.Count == 0) return "";
        return JsonLdBuilder.Serialize(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ItemList",
            ["name"] = name,
            ["numberOfItems"] = numberOfItems ?? elements.Count,
            ["itemListElement"] = elements
        });
    }
}
