namespace PrecureDataStars.Data.Models;

/// <summary>
/// magazine_issues テーブルに対応するエンティティモデル（複合 PK: issue_year + issue_month）。
/// アニメ雑誌の「号」マスタ。各誌の発売日はほぼ横並びのため誌名は持たず、
/// 実際の発売日 1 つを代表値として持つ。
/// ある号がサブタイトルを掲載する対象は「その号の発売日 〜 次号の発売日の前日」に
/// 放送されるエピソードで、エピソード → 号の対応は放送日から導出する
/// （エピソード側には号情報を持たせない）。
/// 次号の発売予定日は事前に判明するため先行登録する運用
/// （最新号のカバー範囲を「次号発売日」で閉じられるようにする）。
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
}
