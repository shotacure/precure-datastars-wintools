using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 要約の欄（スタッフ欄・エピソード一覧・担当回数の欄・役職のページ・スタッフ一覧・声優一覧など）で、クレジットされた名義の後ろに
/// 本名義（今の名乗り・表示名義）を小さく添える決まり。作品のページのメインスタッフ（<c>SeriesGenerator.CreditedAliasLabel</c>）と同じ：
/// 名義と本名義が、空白（半角・全角）と中黒（・／･）の違いだけなら添えない。添える形は「名義 (本名義)」（<c>.staff-current-name</c>。
/// 名義と小書きの間はふつうの空白。名義と小書きを合わせて欄の 1 行より長いときに、ここで折ってはみ出さないようにする。
/// 名義と小書きのそれぞれは途中で折らない）。当時のクレジットそのもの（OP・ED・楽曲）には使わない。
/// </summary>
public static class AliasNote
{
    /// <summary>空白（半角・全角）と中黒（・／･）を除いた名前（表記の揺れだけの違いを同じとみなすため）。</summary>
    public static string NameKey(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            if (ch is not (' ' or '　' or '・' or '･')) sb.Append(ch);
        return sb.ToString();
    }

    /// <summary>クレジットの名義に本名義を添えるか（本名義が空でなく、表記の揺れだけでない違いがあるとき）。</summary>
    public static bool Differs(string credited, string? current)
        => !string.IsNullOrEmpty(current) && NameKey(credited) != NameKey(current);

    /// <summary>名義の人物が 1 人だけのとき、その方の本名義が名義と違えば本名義を、そうでなければ null を返す（共同名義には添えない）。</summary>
    public static string? CurrentNameFor(BuildContext ctx, StaffNameLinkResolver links, int aliasId, string credited)
    {
        if (links.SinglePersonId(aliasId) is not int personId) return null;
        string? current = ctx.EntityUrls.PersonDisplayName(personId);
        return Differs(credited, current) ? current : null;
    }

    /// <summary>
    /// 名前の HTML から添えた小書き（<see cref="Html"/>）を取り除く。名前の HTML から文字を取り出して、名義のまま出す所
    /// （エピソード詳細の meta description・OGP カード・構造化データ）に使う。
    /// </summary>
    public static string StripNotes(string html)
        => System.Text.RegularExpressions.Regex.Replace(html, " ?<span class=\"staff-current-name\">[^<]*</span>", "");

    /// <summary>添える小書きの HTML（「 (本名義)」。名義との間はふつうの空白で、欄の幅が足りないときだけここで折れる）。</summary>
    public static string Html(string current)
        => $" <span class=\"staff-current-name\">({HtmlUtil.Escape(current)})</span>";
}
