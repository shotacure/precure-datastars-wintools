using System.Text;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 話数リスト（series_ep_no の集合）を「#1～4, 8, 11～13」のような圧縮表記に変換するヘルパー。
/// <para>
/// キャラクター詳細・人物詳細・企業詳細ページで、シリーズ単位での出演／クレジット話数を
/// 「全話」「#1～n」「#n～m」「#n～m, l」のように人間が読みやすい範囲表記に整形するために使う。
/// </para>
/// <para>
/// 機能の主軸：
/// <list type="bullet">
///   <item>連続する整数を範囲（"a～b"）に折りたたむ</item>
///   <item>範囲どうし／単発を「, 」区切りで連結する</item>
///   <item>シリーズ全話と一致する場合は末尾に「(全話)」を付与する</item>
///   <item>"#" プレフィックスを各範囲の先頭にだけ付ける（例：「#1～4, 8, 11～13」）</item>
/// </list>
/// </para>
/// </summary>
public static class EpisodeRangeCompressor
{
    /// <summary>
    /// 話数集合を圧縮表記に変換する（「#」プレフィックスを各範囲の先頭にだけ付ける形）。
    /// 例：[1,2,3,4,8,11,12,13] → "#1～4, 8, 11～13"
    /// </summary>
    /// <param name="episodeNos">話数の集合（重複・順不同を許容）。空集合のときは空文字を返す。</param>
    /// <returns>圧縮表記文字列。範囲が 1 つしか無いときも先頭に "#" を付ける。</returns>
    public static string Compress(IEnumerable<int> episodeNos)
    {
        if (episodeNos is null) return string.Empty;
        var sorted = episodeNos.Distinct().OrderBy(x => x).ToList();
        if (sorted.Count == 0) return string.Empty;

        var buf = new StringBuilder();
        int rangeStart = sorted[0];
        int rangeEnd = sorted[0];
        bool isFirstRange = true;

        for (int i = 1; i <= sorted.Count; i++)
        {
            // 末端まで来たか、隣接が切れたら 1 つの範囲を確定
            if (i == sorted.Count || sorted[i] != rangeEnd + 1)
            {
                if (!isFirstRange) buf.Append(", ");
                buf.Append('#').Append(rangeStart);
                if (rangeEnd != rangeStart)
                {
                    buf.Append('～').Append(rangeEnd);
                }
                isFirstRange = false;

                if (i < sorted.Count)
                {
                    rangeStart = sorted[i];
                    rangeEnd = sorted[i];
                }
            }
            else
            {
                rangeEnd = sorted[i];
            }
        }

        return buf.ToString();
    }

    /// <summary>
    /// 「全話」判定付きの圧縮表記を返す。
    /// <paramref name="episodeNos"/> が <paramref name="allEpisodeNos"/> をすべて含むなら
    /// 末尾に「(全話)」を付ける。
    /// </summary>
    /// <param name="episodeNos">対象話数の集合。</param>
    /// <param name="allEpisodeNos">シリーズ内の全話数の集合（this シリーズの母集合）。</param>
    /// <returns>圧縮表記。全話一致なら末尾に「 (全話)」が付く。</returns>
    public static string CompressWithAllEpisodesMark(
        IEnumerable<int> episodeNos,
        IEnumerable<int> allEpisodeNos)
    {
        var targetSet = episodeNos?.Distinct().ToHashSet() ?? new HashSet<int>();
        var allSet = allEpisodeNos?.Distinct().ToHashSet() ?? new HashSet<int>();

        var compressed = Compress(targetSet);
        if (string.IsNullOrEmpty(compressed)) return string.Empty;

        // 全話判定：母集合が空でなく、対象が母集合をすべて包含するか
        if (allSet.Count > 0 && allSet.IsSubsetOf(targetSet))
        {
            return compressed + " (全話)";
        }
        return compressed;
    }
}