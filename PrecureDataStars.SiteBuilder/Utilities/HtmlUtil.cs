using System.Text;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// HTML 出力周りの共通ヘルパー。
/// </summary>
public static class HtmlUtil
{
    /// <summary>
    /// 任意文字列を HTML テキストノード用にエスケープする。
    /// 属性値にも安全に使えるよう、引用符もエスケープ対象に含める。
    /// </summary>
    public static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 秒数（NULL 許可）を mm:ss / h:mm:ss 形式の文字列に変換する。NULL は空文字。
    /// </summary>
    public static string FormatSeconds(int? totalSeconds)
    {
        if (totalSeconds is null) return string.Empty;
        var v = totalSeconds.Value;
        if (v < 0) return string.Empty;
        var hh = v / 3600;
        var mm = (v % 3600) / 60;
        var ss = v % 60;
        return hh > 0
            ? $"{hh}:{mm:D2}:{ss:D2}"
            : $"{mm}:{ss:D2}";
    }
}