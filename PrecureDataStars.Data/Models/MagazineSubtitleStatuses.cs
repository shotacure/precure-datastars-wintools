namespace PrecureDataStars.Data.Models;

/// <summary>
/// episodes.magazine_subtitle_status（アニメ雑誌でのサブタイトル掲載状態）のコード定数と表示ヘルパ。
/// NULL はデータなしを表しコードを持たない（サイトにはセクション自体を出さない）。
/// サブタイトル未確定（title_text NULL）を許すのは <see cref="NotDisclosed"/> /
/// <see cref="Undecided"/> のときだけ（DB 側 CHECK: ck_ep_title_or_magazine_reason）。
/// </summary>
public static class MagazineSubtitleStatuses
{
    /// <summary>掲載（誌面にサブタイトルが載った）。サブタイトル必須。</summary>
    public const string Published = "PUBLISHED";

    /// <summary>非公開（誌面で「サブタイトル非公開」と案内された）。</summary>
    public const string NotDisclosed = "NOT_DISCLOSED";

    /// <summary>未定（誌面で「未定」と案内された）。</summary>
    public const string Undecided = "UNDECIDED";

    /// <summary>コード → 日本語ラベル（掲載 / 非公開 / 未定）。未知コード・NULL は空文字。</summary>
    public static string LabelFor(string? status) => status switch
    {
        Published => "掲載",
        NotDisclosed => "非公開",
        Undecided => "未定",
        _ => ""
    };

    /// <summary>
    /// サブタイトル未確定エピソードの表示プレースホルダ。誌面の案内文言を鉤括弧で引用する
    /// （非公開 → （サブタイトル「非公開」）、未定 → （サブタイトル「未定」））。
    /// 誌面根拠が無い状態でのサブタイトル未確定は CHECK 制約上存在しないはずだが、
    /// 防御的に鉤括弧なしの（サブタイトル未定）へフォールバックする。
    /// </summary>
    public static string SubtitlePlaceholderFor(string? status) => status switch
    {
        NotDisclosed => "（サブタイトル「非公開」）",
        Undecided => "（サブタイトル「未定」）",
        _ => "（サブタイトル未定）"
    };

    /// <summary>指定状態がサブタイトル未確定（title_text NULL / 空）を許すかどうか。</summary>
    public static bool AllowsMissingSubtitle(string? status)
        => status is NotDisclosed or Undecided;
}
