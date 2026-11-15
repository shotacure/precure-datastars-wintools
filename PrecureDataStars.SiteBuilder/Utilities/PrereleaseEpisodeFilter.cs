using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// まだどこからも明らかになっていない話（前もって DB に入れた先の回）を、ビルドの出力から外すための判定。
/// 回の情報が明らかになる経路は 2 つだけで、それぞれ出し方が決まっている。
/// <list type="bullet">
///   <item><description>前の回の本編の後の予告（前の回の放送開始 + 28 分 40 秒。<see cref="SubtitleEmbargoCalculator"/>）：誰にでもそのまま出す。</description></item>
///   <item><description>アニメ雑誌の号の発売（その回の放送日を掲載範囲に持つ号の発売日の 0:00、JST）：サブタイトルはぼかしの枠に入れて出し、
///     早く見る設定を選んだ閲覧者にだけ見せる（ぼかしの枠は予告の時刻で外れる）。</description></item>
/// </list>
/// どちらもまだ来ていない話は、ぼかしの枠の中にも入れられない（早く見る設定の閲覧者には HTML の中身が見える）ため、
/// ビルドの時点で無い話として扱う。号が号マスタに無い話も、雑誌の発売はまだとみなす。
/// 同じ理由で、発売前の号から入れた掲載状態（<see cref="Episode.MagazineSubtitleStatus"/>）は、予告で明らかになった話でも出さない。
/// 予告の時刻を持たない話（通算放送回の無いスピンオフ・初代の第 1 話）は、ぼかしと同じく対象にしない。
/// </summary>
public static class PrereleaseEpisodeFilter
{
    private static readonly TimeSpan JstOffset = TimeSpan.FromHours(9);

    /// <summary>号の発売日の 0:00（JST）が <paramref name="at"/> 以前か。</summary>
    public static bool IsReleased(MagazineIssue issue, DateTimeOffset at)
        => new DateTimeOffset(DateTime.SpecifyKind(issue.ReleaseDate.Date, DateTimeKind.Unspecified), JstOffset) <= at;

    /// <summary>
    /// <paramref name="at"/> の時点で無い話として扱う episode_id の集合。
    /// <paramref name="revealAtByEpisodeId"/> は <see cref="SubtitleEmbargoCalculator.Build"/> の結果（<paramref name="at"/> の時点で予告がまだの話だけを持つ）、
    /// <paramref name="issuesInIssueOrder"/> は号の年月昇順の全号。
    /// </summary>
    public static IReadOnlySet<int> FindHidden(
        IEnumerable<Episode> episodes,
        IReadOnlyDictionary<int, DateTimeOffset> revealAtByEpisodeId,
        IReadOnlyList<MagazineIssue> issuesInIssueOrder,
        DateTimeOffset at)
    {
        var hidden = new HashSet<int>();
        foreach (var ep in episodes)
        {
            if (!revealAtByEpisodeId.ContainsKey(ep.EpisodeId)) continue;
            var issue = MagazineIssueResolver.Resolve(issuesInIssueOrder, ep.OnAirDate);
            if (issue is null || !IsReleased(issue, at)) hidden.Add(ep.EpisodeId);
        }
        return hidden;
    }

    /// <summary>
    /// 掲載状態を持つ話のうち、担当する号が <paramref name="at"/> の時点でまだ発売されていない話の掲載状態を消す（読み込んだ値を書き換える）。
    /// 号マスタに号の無い話はそのまま残す（エピソード詳細が号を解決できない旨を警告する）。消した数を返す。
    /// </summary>
    public static int ClearUnreleasedMagazineStatuses(
        IEnumerable<Episode> episodes,
        IReadOnlyList<MagazineIssue> issuesInIssueOrder,
        DateTimeOffset at)
    {
        int cleared = 0;
        foreach (var ep in episodes)
        {
            if (ep.MagazineSubtitleStatus is null) continue;
            var issue = MagazineIssueResolver.Resolve(issuesInIssueOrder, ep.OnAirDate);
            if (issue is null || IsReleased(issue, at)) continue;
            ep.MagazineSubtitleStatus = null;
            cleared++;
        }
        return cleared;
    }
}
