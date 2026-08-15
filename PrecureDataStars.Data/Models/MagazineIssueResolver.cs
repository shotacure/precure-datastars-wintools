namespace PrecureDataStars.Data.Models;

/// <summary>
/// 放送日 → アニメ雑誌の号の解決ヘルパ。
/// 号マスタ（発売日昇順）に対し「発売日 ≤ 放送日 を満たす最新の号」を担当号とする
/// （ある号の担当範囲は「その号の発売日 〜 次号の発売日の前日」。日曜発売は無いため
/// 発売日と放送日の同日競合は考えない）。
/// ただし次号（より後の発売日を持つ号）が未登録の場合は担当範囲の終端が確定しない
/// （実際には未登録の次号が担当かもしれない）ため、未確定として null を返す。
/// 次号の発売予定日を号マスタへ先行登録することで最新号分まで解決できる。
/// SiteBuilder のエピソード詳細セクションと Episodes エディタの号表示が共用する。
/// </summary>
public static class MagazineIssueResolver
{
    /// <summary>放送日を担当する号を返す。担当号が確定しない場合は null。</summary>
    /// <param name="issuesByReleaseDate">発売日昇順にソート済みの号リスト。</param>
    /// <param name="onAirDate">エピソードの放送日。</param>
    public static MagazineIssue? Resolve(IReadOnlyList<MagazineIssue> issuesByReleaseDate, DateOnly onAirDate)
    {
        // 「発売日 ≤ 放送日」を満たす最後の添字を二分探索で求める。
        int lo = 0, hi = issuesByReleaseDate.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (DateOnly.FromDateTime(issuesByReleaseDate[mid].ReleaseDate) <= onAirDate)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        // 担当候補が無い、または次号未登録（担当範囲の終端が確定しない）なら未確定。
        if (found < 0 || found + 1 >= issuesByReleaseDate.Count) return null;
        return issuesByReleaseDate[found];
    }
}
