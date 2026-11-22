using System.Net;
using System.Text;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 統計のランキングの行に添える、1 話ぶんの放送枠の帯（<c>div.stats-band</c>）。パートを放送順に、尺に比例した幅で並べ、
/// そのページの対象のパートだけを濃い色（<c>sb-hit</c>）で塗る。帯の幅は 30 分で、どの回も同じ物差しになる。
/// </summary>
public static class StatsPartBand
{
    /// <summary>帯の全幅に当たる秒数（30 分の放送枠）。</summary>
    private const int AxisSeconds = 1800;

    /// <summary>対象のパートがこの秒数より短いと、幅の狭い枠では名前が入らない（<c>sb-narrow</c> を付けて CSS で消す）。</summary>
    private const int NarrowSeconds = 150;

    /// <summary>帯の中に書く短い名前（上位の枠の太い帯だけ）。</summary>
    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["AVANT"] = "アバン",
        ["OPENING"] = "OP",
        ["PART_A"] = "A パート",
        ["PART_B"] = "B パート",
        ["PART_C"] = "C パート",
        ["ENDING"] = "ED",
        ["CM2"] = "CM"
    };

    /// <summary>
    /// 帯の HTML を返す。尺（OA）を持つパートが無い回は空文字。
    /// </summary>
    /// <param name="episodeId">対象の回。</param>
    /// <param name="highlightPartType">濃く塗るパート種別（無ければ空文字）。</param>
    /// <param name="minLabelSeconds">この秒数以上のパートに名前を書く。0 なら名前を書かない。</param>
    public static string Build(BuildContext ctx, int episodeId, string highlightPartType, int minLabelSeconds = 0)
    {
        if (!ctx.EpisodePartsByEpisode.TryGetValue(episodeId, out var parts)) return "";
        var list = parts.Where(p => p.OaLength is > 0).OrderBy(p => p.EpisodeSeq).ToList();
        if (list.Count == 0) return "";

        var sb = new StringBuilder(64 + list.Count * 72);
        sb.Append("<div class=\"stats-band\" role=\"img\" aria-label=\"この回の放送枠のパートの並び\">");
        int total = 0;
        foreach (var p in list)
        {
            int sec = p.OaLength!.Value;
            total += sec;
            bool hit = string.Equals(p.PartType, highlightPartType, StringComparison.Ordinal);
            string name = ctx.PartTypeByCode.TryGetValue(p.PartType, out var pt) ? pt.NameJa : p.PartType;
            sb.Append("<span class=\"").Append(hit ? (sec < NarrowSeconds ? "sb-hit sb-narrow" : "sb-hit") : KindClass(p.PartType))
              .Append("\" style=\"flex:").Append(sec).Append(" 0 0\" title=\"")
              .Append(WebUtility.HtmlEncode($"{name} {sec / 60}:{sec % 60:D2}")).Append("\">");
            if (minLabelSeconds > 0 && sec >= minLabelSeconds
                && ShortNames.TryGetValue(p.PartType, out var shortName)
                && (hit || p.PartType != "CM2"))
            {
                sb.Append("<b>").Append(WebUtility.HtmlEncode(shortName)).Append("</b>");
            }
            sb.Append("</span>");
        }
        // 30 分に届かない分は空けておく（どの回も同じ物差しで並べるため）。
        if (total < AxisSeconds)
            sb.Append("<span class=\"sb-pad\" style=\"flex:").Append(AxisSeconds - total).Append(" 0 0\"></span>");
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>対象でないパートの色分け：歌（OP・ED）・本編・CM・そのほか（提供・予告など）。</summary>
    private static string KindClass(string partType) => partType switch
    {
        "OPENING" or "ENDING" => "sb-song",
        "AVANT" or "PART_A" or "PART_B" or "PART_C" => "sb-main",
        _ when partType.StartsWith("CM", StringComparison.Ordinal) => "sb-cm",
        _ => "sb-misc"
    };
}
