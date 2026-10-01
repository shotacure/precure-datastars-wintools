namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 備考の文をサイトに出すときの言い換え。入力では短い放送業界の略語「OA」（On Air ＝ 本放送）を使うが、
/// サイトでは同じ意味を「本放送」と書くので、出すときだけ「OA時」→「本放送時」のように置き換える（DB の値はそのまま）。
/// 置き換えるのは「OA」の直後に「時・後・前・中」が続く形だけ（英字の並びの中の「OA」を巻き込まないため）。
/// </summary>
public static class BroadcastNoteText
{
    private static readonly (string From, string To)[] Replacements =
    {
        ("OA時", "本放送時"),
        ("OA後", "本放送後"),
        ("OA前", "本放送前"),
        ("OA中", "本放送中"),
    };

    /// <summary>備考の文を、サイトに出す言い方にする。null は空文字にする。</summary>
    public static string ForDisplay(string? notes)
    {
        if (string.IsNullOrEmpty(notes)) return "";
        string s = notes;
        foreach (var (from, to) in Replacements)
            s = s.Replace(from, to, StringComparison.Ordinal);
        return s;
    }
}
