using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>統計のランキングで、サブタイトルのどの字に印を付けるか。</summary>
public enum StatsTitleMark
{
    /// <summary>印もマスも付けない。</summary>
    None,
    /// <summary>字には印を付けず、空白を除いた全部の字をマスとして塗る（文字数のランキング）。</summary>
    All,
    /// <summary>漢字と繰り返し記号「々」に印を付ける（漢字率のランキング）。</summary>
    Kanji,
    /// <summary>記号（句読点・記号・絵文字など）に印を付ける（記号率のランキング）。</summary>
    Symbol
}

/// <summary>
/// サブタイトルの HTML（ルビ付き）の中の、数える対象の字に印（<c>span.stats-hit</c>）を付け、
/// 1 字 1 マスの並び（<c>span.stats-cells</c>）を組み立てる。ルビの読み（<c>rt</c>・<c>rp</c>）とタグは数えない。
/// 空白は数えず、マスにもしない。
/// </summary>
public static class StatsTitleMarker
{
    private static readonly Regex TagSplit = new(@"(<[^>]+>)", RegexOptions.Compiled);
    private static readonly Regex Entity = new(@"\G&(#\d+|#[xX][0-9a-fA-F]+|\w+);", RegexOptions.Compiled);

    // 記号の判定は、文字統計（title_char_stats.categories）を作る TitleCharStatsBuilder と同じ順に当てる：
    // 絵文字・句読点・記号が先で、かな・漢字・数字・英字のどれでもない字（「々」など）も記号側（Other）に入る。
    private static readonly Regex RxHira = new(@"^\p{IsHiragana}", RegexOptions.Compiled);
    private static readonly Regex RxKata = new(@"^[\p{IsKatakana}ｦ-ﾟー]", RegexOptions.Compiled);
    private static readonly Regex RxKanji = new(@"^\p{IsCJKUnifiedIdeographs}", RegexOptions.Compiled);
    private static readonly Regex RxLatin = new(@"^[\p{IsBasicLatin}\p{IsLatin-1Supplement}\p{IsLatinExtended-A}\p{IsLatinExtended-B}Ａ-Ｚａ-ｚ]", RegexOptions.Compiled);
    private static readonly Regex RxDigit = new(@"^\p{Nd}", RegexOptions.Compiled);
    private static readonly Regex RxEmoji = new(@"^(\uD83C[\uDF00-\uDFFF]|\uD83D[\uDC00-\uDEFF]|\uD83E[\uDD00-\uDDFF])", RegexOptions.Compiled);
    private static readonly Regex RxPunct = new(@"^\p{P}", RegexOptions.Compiled);
    private static readonly Regex RxSymbol = new(@"^\p{S}", RegexOptions.Compiled);

    /// <summary>印を付けた題名の HTML と、空白を除いた字ごとの「数える対象か」の並び。</summary>
    public readonly record struct Result(string Html, IReadOnlyList<bool> Cells);

    /// <summary>
    /// <paramref name="titleHtml"/> の字を先頭から見て、<paramref name="mark"/> の対象の字を <c>span.stats-hit</c> で包む。
    /// <see cref="StatsTitleMark.All"/> は字を包まず、全部の字を対象として数える。<see cref="StatsTitleMark.None"/> は何もしない。
    /// </summary>
    public static Result Mark(string titleHtml, StatsTitleMark mark)
    {
        if (mark == StatsTitleMark.None || string.IsNullOrEmpty(titleHtml))
            return new Result(titleHtml, Array.Empty<bool>());

        var cells = new List<bool>();
        var sb = new StringBuilder(titleHtml.Length + 64);
        bool open = false;      // span.stats-hit を開いているか
        int rubyTextDepth = 0;  // rt・rp の中にいるか

        void Close()
        {
            if (open) { sb.Append("</span>"); open = false; }
        }

        foreach (string token in TagSplit.Split(titleHtml))
        {
            if (token.Length == 0) continue;
            if (token[0] == '<')
            {
                Close();
                if (token.StartsWith("<rt", StringComparison.OrdinalIgnoreCase) || token.StartsWith("<rp", StringComparison.OrdinalIgnoreCase)) rubyTextDepth++;
                else if (token.StartsWith("</rt", StringComparison.OrdinalIgnoreCase) || token.StartsWith("</rp", StringComparison.OrdinalIgnoreCase)) rubyTextDepth = Math.Max(0, rubyTextDepth - 1);
                sb.Append(token);
                continue;
            }
            if (rubyTextDepth > 0) { sb.Append(token); continue; }

            int i = 0;
            while (i < token.Length)
            {
                // 文字参照（&amp; など）は 1 字として扱い、元の書き方のまま出す。
                string raw, ch;
                var m = token[i] == '&' ? Entity.Match(token, i) : Match.Empty;
                if (m.Success)
                {
                    raw = m.Value;
                    ch = WebUtility.HtmlDecode(raw);
                }
                else
                {
                    raw = StringInfo.GetNextTextElement(token, i);
                    ch = raw;
                }
                i += raw.Length;

                if (ch.Length == 0 || IsSpace(ch))
                {
                    Close();
                    sb.Append(raw);
                    continue;
                }

                bool hit = mark switch
                {
                    StatsTitleMark.All => true,
                    StatsTitleMark.Kanji => IsKanji(ch),
                    _ => IsSymbol(ch)
                };
                cells.Add(hit);

                if (hit && mark != StatsTitleMark.All)
                {
                    if (!open) { sb.Append("<span class=\"stats-hit\">"); open = true; }
                }
                else
                {
                    Close();
                }
                sb.Append(raw);
            }
        }
        Close();
        return new Result(sb.ToString(), cells);
    }

    /// <summary>1 字 1 マスの並び。数える対象の字のマスに <c>on</c> を付ける。字が無ければ空文字。</summary>
    /// <param name="countStyle">
    /// 全部のマスが対象の並び（文字数）か。true なら <c>is-count</c> を付け、CSS で淡い色の小さいマスを 5 個ずつに区切って並べる
    /// （濃い色のマスが何十個も続くと行がうるさくなるため）。
    /// </param>
    public static string BuildCellsHtml(IReadOnlyList<bool> cells, bool countStyle = false)
    {
        if (cells.Count == 0) return "";
        var sb = new StringBuilder(40 + cells.Count * 18);
        sb.Append(countStyle ? "<span class=\"stats-cells is-count\" aria-hidden=\"true\">" : "<span class=\"stats-cells\" aria-hidden=\"true\">");
        foreach (bool on in cells) sb.Append(on ? "<i class=\"on\"></i>" : "<i></i>");
        sb.Append("</span>");
        return sb.ToString();
    }

    /// <summary>
    /// 字の種類の色の CSS クラス（文字種別比率のページの 6 色：<c>ct-kanji</c>・<c>ct-hiragana</c>・<c>ct-katakana</c>・
    /// <c>ct-latin</c>・<c>ct-digits</c>・<c>ct-symbols</c>）。漢字は「々」を含む。ほかは文字統計と同じ順に当てる。
    /// </summary>
    public static string TypeClass(string ch)
    {
        if (string.IsNullOrEmpty(ch)) return "ct-symbols";
        if (IsKanji(ch)) return "ct-kanji";
        if (IsSymbol(ch)) return "ct-symbols";
        string g = ch.Normalize(NormalizationForm.FormKC);
        if (RxHira.IsMatch(g)) return "ct-hiragana";
        if (RxKata.IsMatch(g)) return "ct-katakana";
        if (RxDigit.IsMatch(g)) return "ct-digits";
        return "ct-latin";
    }

    private static bool IsSpace(string ch)
        => ch.All(c => char.IsWhiteSpace(c) || c == ' ');

    /// <summary>漢字率の集計（SQL の <c>\p{Han}</c> と「々」）と同じ範囲：漢字・部首・漢数字のゼロ「〇」・「々」「〆」。</summary>
    private static bool IsKanji(string ch)
    {
        int cp = char.ConvertToUtf32(ch, 0);
        return (cp >= 0x4E00 && cp <= 0x9FFF)
            || (cp >= 0x3400 && cp <= 0x4DBF)
            || (cp >= 0xF900 && cp <= 0xFAFF)
            || (cp >= 0x2E80 && cp <= 0x2FDF)
            || (cp >= 0x3005 && cp <= 0x3007)
            || (cp >= 0x3021 && cp <= 0x3029)
            || (cp >= 0x3038 && cp <= 0x303B)
            || (cp >= 0x20000 && cp <= 0x3FFFF);
    }

    private static bool IsSymbol(string ch)
    {
        // 文字統計と同じ正規化（NFKC と波ダッシュの統一）をかけてから種類を見る。
        string g = ch.Normalize(NormalizationForm.FormKC).Replace('〜', '〜').Replace('～', '〜');
        if (g.Length == 0) return false;
        if (RxEmoji.IsMatch(g) || RxPunct.IsMatch(g) || RxSymbol.IsMatch(g)) return true;
        return !(RxHira.IsMatch(g) || RxKata.IsMatch(g) || RxKanji.IsMatch(g) || RxDigit.IsMatch(g) || RxLatin.IsMatch(g));
    }
}
