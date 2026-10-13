namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 日付を「平成27(2015)/06/01付」の形（元号と年・西暦・月日・「付」）にする。人物詳細の役職履歴の異動日に使う。
/// 元号の始まりは明治 1868-10-23 / 大正 1912-07-30 / 昭和 1926-12-25 / 平成 1989-01-08 / 令和 2019-05-01。
/// 元号の 1 年目は「元」と書く（「令和元(2019)/05/01付」）。明治より前の日付は西暦だけにする。
/// </summary>
public static class WarekiDate
{
    private static readonly (DateTime Start, string Name)[] Eras =
    {
        (new DateTime(2019, 5, 1), "令和"),
        (new DateTime(1989, 1, 8), "平成"),
        (new DateTime(1926, 12, 25), "昭和"),
        (new DateTime(1912, 7, 30), "大正"),
        (new DateTime(1868, 10, 23), "明治"),
    };

    /// <summary>「平成27(2015)/06/01付」の形の文字列。</summary>
    public static string EffectiveLabel(DateTime date)
    {
        string md = $"{date.Month:00}/{date.Day:00}付";
        foreach (var (start, name) in Eras)
        {
            if (date.Date < start) continue;
            int n = date.Year - start.Year + 1;
            return $"{name}{(n == 1 ? "元" : n.ToString())}({date.Year})/{md}";
        }
        return $"{date.Year}/{md}";
    }
}
