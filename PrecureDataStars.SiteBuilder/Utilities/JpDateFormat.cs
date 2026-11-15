using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 和文の日付表記を組み立てる共通ヘルパー。
/// 「2024年2月4日」「2004年2月1日 〜 2005年1月30日」「2024.2.4」「2024年2月4日（日）」の各書式を提供。
/// 月日はいずれも 0 詰めしない。用途ごとに出力書式が異なるため、書式単位でメソッドを分けている。
/// 呼び出し側は意味の合うメソッドを選ぶこと。
/// </summary>
public static class JpDateFormat
{
    /// <summary>「2024年2月4日」形式。<see cref="DateTime"/> の日付部分のみを使う（時刻は無視）。</summary>
    public static string Date(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    /// <summary>「2024年2月4日」形式。値が <c>null</c> のときは空文字を返す。</summary>
    public static string NullableDate(DateOnly? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    /// <summary>「2024年2月4日」形式。値が <c>null</c> のときは空文字を返す（日付部分のみ使用）。</summary>
    public static string NullableDate(DateTime? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    /// <summary>放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。 終了日が <c>null</c> のときは開始日単独表記にする。</summary>
    public static string Period(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        return startStr;
    }

    /// <summary>
    /// 継続中があり得るシリーズ（<c>series_kinds.credit_attach_to='EPISODE'</c> のシリーズ：
    /// TV / SPIN-OFF / OTONA / SHORT）向けの期間表記。終了日が確定していない（<c>null</c>）
    /// 場合は「2025年2月2日 〜」のように開始日のあとに「〜」を付けて継続中であることを示す。
    /// 終了日があるときは <see cref="Period(DateOnly, DateOnly?)"/> と同じ両端表記。
    /// 映画系（<c>credit_attach_to='SERIES'</c>）はそもそも継続概念を持たないので、
    /// 開始日単独表記の <see cref="Period(DateOnly, DateOnly?)"/> を使う。
    /// </summary>
    public static string PeriodOrOngoing(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        // 終了日未確定のシリーズ：開始日のあとに「〜」を付けて継続中を示す。
        return $"{startStr} 〜";
    }

    /// <summary>
    /// 作品の放送・公開期間を、開始日の確かさ（<see cref="Series.StartDatePrecision"/>）に合わせて返す。
    /// 日付まで確かな作品は <paramref name="ongoing"/> が真なら <see cref="PeriodOrOngoing"/>、偽なら <see cref="Period(DateOnly, DateOnly?)"/>。
    /// 年や年月までしか確かでない作品は <see cref="PlannedStart"/> の「2027年放送予定」「2027年7月公開予定」。
    /// <paramref name="ongoing"/> には、継続中があり得る EPISODE 系の作品か（<see cref="SeriesClassifier.IsEpisodeAttaching"/>）を渡す。
    /// </summary>
    public static string SeriesPeriod(Series s, bool ongoing)
    {
        if (!s.IsStartDateExact) return PlannedStart(s, ongoing);
        return ongoing ? PeriodOrOngoing(s.StartDate, s.EndDate) : Period(s.StartDate, s.EndDate);
    }

    /// <summary>
    /// 開始日が年や年月までしか確かでない作品の予定表記。年だけなら「2027年」、年月なら「2027年7月」に、
    /// EPISODE 系（<paramref name="broadcast"/> が真）は「放送予定」、映画などは「公開予定」を続ける。
    /// 日付まで確かな作品に使ったときも、年月日の「2027年2月7日放送予定」を返す。
    /// </summary>
    public static string PlannedStart(Series s, bool broadcast)
    {
        var d = s.StartDate;
        string when = s.StartDatePrecision switch
        {
            Series.StartDatePrecisionYear => $"{d.Year}年",
            Series.StartDatePrecisionMonth => $"{d.Year}年{d.Month}月",
            _ => $"{d.Year}年{d.Month}月{d.Day}日",
        };
        return when + (broadcast ? "放送予定" : "公開予定");
    }

    /// <summary>「2024.2.4」形式（ドット区切り・0 詰めなし）。 スタッフ段との同居など、密表示を要する文脈で使う。</summary>
    public static string DotDate(DateTime dt)
        => $"{dt.Year}.{dt.Month}.{dt.Day}";

    /// <summary>「2024年2月4日（日）」形式。曜日を和文 1 文字で併記する。 曜日が判定不能（理論上発生しない）の場合は「?」を入れる。</summary>
    public static string DateWithWeekday(DateTime dt)
    {
        string dayOfWeek = dt.DayOfWeek switch
        {
            DayOfWeek.Sunday => "日",
            DayOfWeek.Monday => "月",
            DayOfWeek.Tuesday => "火",
            DayOfWeek.Wednesday => "水",
            DayOfWeek.Thursday => "木",
            DayOfWeek.Friday => "金",
            DayOfWeek.Saturday => "土",
            _ => "?"
        };
        return $"{dt.Year}年{dt.Month}月{dt.Day}日（{dayOfWeek}）";
    }

    /// <summary>
    /// 深夜の放送の境目の時刻。開始がこの時刻より前（0:00〜4:59）の回は、前の日の放送として日付を出し、時刻は 24 を足して書く
    /// （土曜深夜の 2:00 開始なら「土曜 26:00」）。番組表や公式の表記に合わせるためで、DB は実際の日時のまま持つ。
    /// </summary>
    public const int LateNightEndHour = 5;

    /// <summary>放送日（開始が <see cref="LateNightEndHour"/> 時より前の深夜の回は前の日）。時刻は 0:00 にそろえて返す。</summary>
    public static DateTime BroadcastDay(DateTime onAirAt)
        => onAirAt.Hour < LateNightEndHour ? onAirAt.Date.AddDays(-1) : onAirAt.Date;

    /// <summary>放送日の「2024年2月4日」形式（深夜の回は前の日）。</summary>
    public static string BroadcastDate(DateTime onAirAt) => Date(BroadcastDay(onAirAt));

    /// <summary>放送日の「2024.2.4」形式（深夜の回は前の日）。</summary>
    public static string BroadcastDotDate(DateTime onAirAt) => DotDate(BroadcastDay(onAirAt));

    /// <summary>
    /// 放送日を起点にした「H:mm」の時刻。<paramref name="at"/> が放送日の翌日なら 24 を足す（深夜の回の「26:00」、
    /// 深夜に終わる回の終了時刻の「24:30」）。<paramref name="broadcastDay"/> は <see cref="BroadcastDay"/> の値。
    /// </summary>
    public static string BroadcastClock(DateTime at, DateTime broadcastDay)
    {
        var span = at - broadcastDay.Date;
        return $"{(int)span.TotalHours}:{span.Minutes:D2}";
    }

    /// <summary>
    /// 放送日と放送の時間帯（「2004年2月1日 8:30〜9:00」、深夜の回は「2025年1月11日 26:00〜26:30」）。
    /// 枠の長さ（分）が無いか 0 なら開始の時刻だけ。
    /// </summary>
    public static string BroadcastDateTime(DateTime onAirAt, int? durationMinutes)
    {
        var day = BroadcastDay(onAirAt);
        string head = $"{Date(day)} {BroadcastClock(onAirAt, day)}";
        return durationMinutes is int m && m > 0 ? $"{head}〜{BroadcastClock(onAirAt.AddMinutes(m), day)}" : head;
    }
}