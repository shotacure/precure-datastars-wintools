namespace PrecureDataStars.Data.Models;

/// <summary>
/// episodes.magazine_subtitle_status（アニメ雑誌でのサブタイトル掲載状態）のコード定数と表示ヘルパ。
/// NULL はデータなし（未調査）を表しコードを持たない（サイトにはセクション自体を出さない）。
/// 「誌面を確認した結果、その号に枠自体が無かった」は未調査とは別の事実なので
/// <see cref="NotListed"/> で区別する。
/// サブタイトル未確定（title_text NULL）を許すのは <see cref="NotDisclosed"/> /
/// <see cref="Undecided"/> / <see cref="NotListed"/> のとき（DB 側 CHECK:
/// ck_ep_title_or_magazine_reason）。
/// </summary>
public static class MagazineSubtitleStatuses
{
    /// <summary>掲載（誌面にサブタイトルが載った）。サブタイトル必須。</summary>
    public const string Published = "PUBLISHED";

    /// <summary>非公開（誌面で「サブタイトル非公開」と案内された）。</summary>
    public const string NotDisclosed = "NOT_DISCLOSED";

    /// <summary>未定（誌面で「未定」と案内された）。</summary>
    public const string Undecided = "UNDECIDED";

    /// <summary>掲載なし（誌面に当該作品の枠自体が無かった）。 新シリーズが前号までの番組表に登場していない場合などに使う。 未調査を表す NULL とは異なり「確認した結果、枠が無かった」という一次情報。</summary>
    public const string NotListed = "NOT_LISTED";

    /// <summary>コード → 日本語ラベル（掲載 / 非公開 / 未定 / 掲載なし）。未知コード・NULL は空文字。</summary>
    public static string LabelFor(string? status) => status switch
    {
        Published => "掲載",
        NotDisclosed => "非公開",
        Undecided => "未定",
        NotListed => "掲載なし",
        _ => ""
    };

    /// <summary>
    /// サブタイトル未確定エピソードの表示プレースホルダ。誌面の案内文言があるものは
    /// それを鉤括弧で引用する（非公開 → （サブタイトル「非公開」）、未定 → （サブタイトル「未定」））。
    /// 掲載なしは誌面に文言そのものが無いため、引用ではない素の（サブタイトル未定）を使う。
    /// 誌面根拠が無い状態でのサブタイトル未確定は CHECK 制約上存在しないはずだが、
    /// 防御的に同じ素の表記へフォールバックする。
    /// </summary>
    public static string SubtitlePlaceholderFor(string? status) => status switch
    {
        NotDisclosed => "（サブタイトル「非公開」）",
        Undecided => "（サブタイトル「未定」）",
        _ => "（サブタイトル未定）"
    };

    /// <summary>指定状態がサブタイトル未確定（title_text NULL / 空）を許すかどうか。</summary>
    public static bool AllowsMissingSubtitle(string? status)
        => status is NotDisclosed or Undecided or NotListed;
}
