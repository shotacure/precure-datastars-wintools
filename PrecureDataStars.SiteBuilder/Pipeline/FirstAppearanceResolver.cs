using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// クレジットの関与から「初登場」（キャラクター）・「初参加」（人物）の話を求める。
/// <para>
/// 関与のうちいちばん早いものを選ぶ。日付は TV 系なら話の放送日時、映画系（SERIES 単位のクレジット）なら公開日
/// （<see cref="Series.StartDate"/>）。同じ日付なら TV 系を先にし、次にシリーズの開始日、話数の順。
/// </para>
/// <para>
/// クレジットは古い順に入力しているので、収録範囲の最終話（<see cref="BuildContext.CreditCoverageEpisode"/>）の
/// 放送日時までに放送・公開された作品はすべてクレジットが入っている。初登場がその範囲内にあれば確定した事実として出せ、
/// 範囲より後なら、まだ入っていない作品にもっと早い登場がありうるので出さない（null を返す）。
/// </para>
/// </summary>
public static class FirstAppearanceResolver
{
    /// <summary>
    /// 関与の列から最初の登場を求める。収録範囲外・該当なしは null。
    /// </summary>
    public static FirstAppearance? Resolve(BuildContext ctx, IEnumerable<Involvement> involvements)
    {
        if (ctx.CreditCoverageEpisode is not { } coverage) return null;
        DateTime coverageAt = coverage.Episode.OnAirAt;

        FirstAppearance? best = null;
        foreach (var inv in involvements)
        {
            if (!ctx.SeriesById.TryGetValue(inv.SeriesId, out var series)) continue;

            FirstAppearance candidate;
            if (ctx.IsMovieKindSeries(inv.SeriesId))
            {
                candidate = new FirstAppearance(series, null, series.StartDate.ToDateTime(TimeOnly.MinValue));
            }
            else
            {
                if (inv.EpisodeId is not int episodeId) continue;
                var ep = ctx.LookupEpisode(inv.SeriesId, episodeId);
                if (ep is null) continue;
                candidate = new FirstAppearance(series, ep, ep.OnAirAt);
            }

            if (best is null || IsEarlier(candidate, best)) best = candidate;
        }

        if (best is null || best.At > coverageAt) return null;
        return best;
    }

    private static bool IsEarlier(FirstAppearance a, FirstAppearance b)
    {
        if (a.At.Date != b.At.Date) return a.At < b.At;
        // 同じ日なら TV 系（話あり）を先に。
        bool aTv = a.Episode is not null, bTv = b.Episode is not null;
        if (aTv != bTv) return aTv;
        if (a.At != b.At) return a.At < b.At;
        int c = a.Series.StartDate.CompareTo(b.Series.StartDate);
        if (c != 0) return c < 0;
        return (a.Episode?.SeriesEpNo ?? 0) < (b.Episode?.SeriesEpNo ?? 0);
    }
}

/// <summary>
/// 最初の登場。<see cref="Episode"/> が null なら映画系（シリーズ単位）。
/// </summary>
public sealed record FirstAppearance(Series Series, Episode? Episode, DateTime At)
{
    /// <summary>
    /// 基本情報のタイルに出すリンク付き HTML。TV 系は「『作品』第N話（2004年2月1日）」、
    /// 映画系は「『作品』（2005年4月16日公開）」。単一の作品を指す文脈なので年度の注釈は付けない。
    /// </summary>
    public string ToHtml()
    {
        string title = HtmlUtil.Escape(Series.Title);
        if (Episode is not null)
        {
            string url = PathUtil.EpisodeUrl(Series.Slug, Episode.SeriesEpNo);
            return $"<a href=\"{url}\">『{title}』第{Episode.SeriesEpNo}話</a>（{JpDateFormat.Date(Episode.OnAirAt)}）";
        }
        return $"<a href=\"{PathUtil.SeriesUrl(Series.Slug)}\">『{title}』</a>（{JpDateFormat.Date(At)}公開）";
    }

    /// <summary>OGP カードなどテキストだけの場所向け。「『作品』第N話（2004.2.1）」「『作品』（2005.4.16 公開）」。</summary>
    public string ToPlainText()
    {
        if (Episode is not null)
            return $"『{Series.Title}』第{Episode.SeriesEpNo}話（{JpDateFormat.DotDate(Episode.OnAirAt)}）";
        return $"『{Series.Title}』（{JpDateFormat.DotDate(At)} 公開）";
    }
}
