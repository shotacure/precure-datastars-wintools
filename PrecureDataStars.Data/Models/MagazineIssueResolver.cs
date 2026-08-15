namespace PrecureDataStars.Data.Models;

/// <summary>
/// 放送日 → アニメ雑誌の号の解決ヘルパ。
/// 各号の掲載範囲は「号の前月 10 日 〜 当月 10 日の前日」でちょうど 1 か月ぶんあり、
/// 号の年月だけから確定する（<see cref="MagazineIssue.CoverageStartDate"/> /
/// <see cref="MagazineIssue.CoverageEndDateExclusive"/> 参照）。
/// 発売日は 10 日が土日にあたる号などで繰り上がるが掲載範囲の境界は動かないため、
/// 判定に <see cref="MagazineIssue.ReleaseDate"/> は使わない。
/// 範囲が号単体で閉じているので、号マスタが歯抜け（間の月が未登録）でも、
/// 未登録の月に放送された回は前後の号へ吸い寄せられず未確定（null）になる。
/// 次号の先行登録は不要。
/// SiteBuilder のエピソード詳細セクションと Episodes エディタの号表示が共用する。
/// </summary>
public static class MagazineIssueResolver
{
    /// <summary>放送日を担当する号を返す。該当号が未登録の場合は null。</summary>
    /// <param name="issuesInIssueOrder">号の年月昇順にソート済みの号リスト。</param>
    /// <param name="onAirDate">エピソードの放送日。</param>
    public static MagazineIssue? Resolve(IReadOnlyList<MagazineIssue> issuesInIssueOrder, DateOnly onAirDate)
    {
        // 「掲載開始日 ≤ 放送日」を満たす最後の号を二分探索で求める。
        int lo = 0, hi = issuesInIssueOrder.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (issuesInIssueOrder[mid].CoverageStartDate <= onAirDate)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (found < 0) return null;

        // 見つかった号の掲載範囲を実際に跨いでいないか確認する。跨いでいる場合は
        // 本来の担当号が号マスタに未登録ということなので、未確定として null を返す。
        var issue = issuesInIssueOrder[found];
        return issue.Covers(onAirDate) ? issue : null;
    }
}
