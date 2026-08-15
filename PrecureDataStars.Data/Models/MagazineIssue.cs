namespace PrecureDataStars.Data.Models;

/// <summary>
/// magazine_issues テーブルに対応するエンティティモデル（複合 PK: issue_year + issue_month）。
/// アニメ雑誌の「号」マスタ。各誌の発売日はほぼ横並びのため誌名は持たず、
/// 実際の発売日 1 つを代表値として持つ。
/// ある号がサブタイトルを掲載する対象は「その号の掲載開始日 〜 次号の掲載開始日の前日」に
/// 放送されるエピソードで、エピソード → 号の対応は放送日から導出する
/// （エピソード側には号情報を持たせない）。
/// 掲載開始日は号の前月 10 日固定で発売日とは独立に決まるため、範囲判定には
/// <see cref="CoverageStartDate"/> を使い、<see cref="ReleaseDate"/> は表示専用とする。
/// 掲載範囲は号単体で閉じている（前月 10 日から 1 か月）ため、次号の先行登録や
/// 連続した登録は不要で、必要な号だけを飛び飛びに登録してよい。
/// </summary>
public sealed class MagazineIssue
{
    /// <summary>号の年（「2026年9月号」の 2026）。</summary>
    public int IssueYear { get; set; }

    /// <summary>号の月（「2026年9月号」の 9。1〜12）。</summary>
    public int IssueMonth { get; set; }

    /// <summary>実際の発売日（各誌横並びの代表日。DATE 列、時刻部は 00:00 固定）。</summary>
    public DateTime ReleaseDate { get; set; }

    // ── 計算プロパティ ──

    /// <summary>号の表示名（例：「2026年9月号」）。</summary>
    public string IssueLabel => $"{IssueYear}年{IssueMonth}月号";

    /// <summary>
    /// この号がサブタイトルを掲載する範囲の開始日（号の前月 10 日）。
    /// アニメ誌は毎月 10 日発売が原則で、10 日が日曜・土曜にあたる号は発売日そのものが
    /// 繰り上がるが、掲載範囲の境界は 10 日から動かない
    /// （2026 年 2 月号〜9 月号の 8 号で検証。例：9 月号は 8/7 発売だが 8/9 放送回は
    /// 8 月号側にあり、掲載は 8/16 から始まる）。
    /// そのため担当号の判定にはこの導出値を使い、<see cref="ReleaseDate"/> は
    /// 「2026年8月7日発売」という表示にのみ用いる。
    /// </summary>
    public DateOnly CoverageStartDate => new DateOnly(IssueYear, IssueMonth, 10).AddMonths(-1);

    /// <summary>
    /// この号がサブタイトルを掲載する範囲の終端（翌号の掲載開始日 ＝ 当月 10 日。この日は含まない）。
    /// 掲載範囲は号ごとにちょうど 1 か月で、号の年月だけから閉区間が決まる。
    /// 次号がマスタに登録されていなくても範囲が確定するため、号マスタが歯抜けでも
    /// 未登録の月に放送された回を誤って前後の号へ吸い寄せることがない。
    /// </summary>
    public DateOnly CoverageEndDateExclusive => CoverageStartDate.AddMonths(1);

    /// <summary>指定した放送日がこの号の掲載範囲に入るかどうか。</summary>
    public bool Covers(DateOnly onAirDate)
        => onAirDate >= CoverageStartDate && onAirDate < CoverageEndDateExclusive;
}
