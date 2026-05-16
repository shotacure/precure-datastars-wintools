namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 和文の日付表記を組み立てる共通ヘルパー。
/// <para>
/// 「2024年2月4日」「2004年2月1日 〜 2005年1月30日」「2024.2.4」「2024年2月4日（日）」など、
/// 各 Generator が個別に持っていた同一実装のローカルメソッドを単一の出力定義に集約したもの。
/// 月日はいずれも 0 詰めしない（実データ表示の従来仕様に合わせる）。
/// </para>
/// <para>
/// 用途ごとに出力書式が異なるため、書式単位でメソッドを分けている。
/// 呼び出し側は意味の合うメソッドを選ぶこと（同名で書式の異なる実装が
/// 複数 Generator に散在していた経緯があるため、ここでは書式を名前で明示する）。
/// </para>
/// </summary>
public static class JpDateFormat
{
    /// <summary>
    /// 「2024年2月4日」形式。<see cref="DateTime"/> の日付部分のみを使う（時刻は無視）。
    /// </summary>
    public static string Date(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    /// <summary>
    /// 「2024年2月4日」形式。値が <c>null</c> のときは空文字を返す。
    /// </summary>
    public static string NullableDate(DateOnly? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    /// <summary>
    /// 「2024年2月4日」形式。値が <c>null</c> のときは空文字を返す（日付部分のみ使用）。
    /// </summary>
    public static string NullableDate(DateTime? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    /// <summary>
    /// 放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。
    /// 終了日が <c>null</c> のときは開始日単独表記にする。
    /// </summary>
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
    /// 「2024.2.4」形式（ドット区切り・0 詰めなし）。
    /// スタッフ段との同居など、密表示を要する文脈で使う。
    /// </summary>
    public static string DotDate(DateTime dt)
        => $"{dt.Year}.{dt.Month}.{dt.Day}";

    /// <summary>
    /// 「2024年2月4日（日）」形式。曜日を和文 1 文字で併記する。
    /// 曜日が判定不能（理論上発生しない）の場合は「?」を入れる。
    /// </summary>
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
}
