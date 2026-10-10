namespace PrecureDataStars.TemplateRendering;

/// <summary>
/// クレジットのテキストだけの行（entry_kind = TEXT の raw_text）を HTML にする。
/// 画面の文字として出る一般のお子さんの名前（「張 羽蓁ちゃん」など）は、画面で敬称の「ちゃん」「くん」が
/// 名前より小さく（8 割ほどで）組まれているので、末尾の敬称だけを <c>&lt;span class="credit-honorific"&gt;</c> で包み、
/// CSS で 80% の大きさにする。DB の文字は画面どおりのまま持ち、表示するときだけ包む。
/// SiteBuilder のクレジットと Catalog のプレビュー（役職テンプレの <c>{TEXTS}</c>）で共有する。
/// </summary>
public static class TextEntryHtml
{
    /// <summary>小さく組む末尾の敬称。</summary>
    private static readonly string[] Honorifics = { "ちゃん", "くん" };

    /// <summary>raw_text を HTML エスケープし、末尾の敬称があればそこだけ小さく組む span で包む。null は空文字にする。</summary>
    public static string Format(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return "";
        foreach (var h in Honorifics)
        {
            // 敬称だけの文字列は名前が無いので包まない。
            if (rawText.Length > h.Length && rawText.EndsWith(h, StringComparison.Ordinal))
            {
                string name = rawText[..^h.Length];
                return System.Net.WebUtility.HtmlEncode(name)
                    + "<span class=\"credit-honorific\">" + System.Net.WebUtility.HtmlEncode(h) + "</span>";
            }
        }
        return System.Net.WebUtility.HtmlEncode(rawText);
    }
}
