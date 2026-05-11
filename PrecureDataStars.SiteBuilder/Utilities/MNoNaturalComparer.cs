using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// M 番号（bgm_cues.m_no_detail）の自然順比較を提供するコンパレータ。
/// <para>
/// 例：素朴な文字列ソートでは <c>"M1", "M10", "M11", "M2"</c> という不自然な順になるが、
/// 本コンパレータは <c>"M1", "M2", "M10", "M11"</c> の順に並べる。
/// 加えて「枝番無し &lt; 枝番有り」というルールを適用：
/// <list type="bullet">
///   <item><c>"M-7" &lt; "M-7-2"</c></item>
///   <item><c>"M3" &lt; "M3a"</c></item>
/// </list>
/// </para>
/// <para>
/// 比較ロジックの優先順位：
/// </para>
/// <list type="number">
///   <item>先頭の数字トークンを数値抽出した値（"M220b" の 220）で数値比較</item>
///   <item>枝番無し優先（先頭非数字 + 先頭数字を取り除いた残りが空なら 0、ある場合は 1）</item>
///   <item>枝番有り同士の場合は残り文字列を Ordinal 比較</item>
///   <item>最後のタイブレークは元の m_no_detail 全体の Ordinal 比較</item>
/// </list>
/// <para>
/// マイグレ（v1.3.0_add_bgm_cues_seq_in_session.sql）と同等のロジックを C# 側でも保持することで、
/// SiteBuilder などアプリケーション側でも同じ並び順を再現できる。
/// </para>
/// </summary>
public sealed class MNoNaturalComparer : IComparer<string?>
{
    /// <summary>シングルトンインスタンス。</summary>
    public static readonly MNoNaturalComparer Instance = new();

    // 先頭の連続数字を抜き出す正規表現
    private static readonly Regex FirstDigitsRx = new(@"\d+", RegexOptions.Compiled);

    // 先頭の非数字を取り除く正規表現
    private static readonly Regex StripLeadingNonDigitsRx = new(@"^[^0-9]*", RegexOptions.Compiled);

    // 先頭の数字を取り除く正規表現
    private static readonly Regex StripLeadingDigitsRx = new(@"^[0-9]+", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        // NULL は最後尾（NULL 同士は等価）
        if (x is null && y is null) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        // 先頭数字トークンの値を抽出。見つからなければ 0 として扱う。
        long xn = ExtractLeadingNumber(x);
        long yn = ExtractLeadingNumber(y);
        if (xn != yn) return xn.CompareTo(yn);

        // 枝番有無で比較。枝番無し（suffix が空）が先。
        string xSuf = ExtractSuffix(x);
        string ySuf = ExtractSuffix(y);
        bool xHasBranch = xSuf.Length > 0;
        bool yHasBranch = ySuf.Length > 0;
        if (xHasBranch != yHasBranch) return xHasBranch ? 1 : -1;

        // 枝番有り同士なら suffix を Ordinal 比較
        int sufCmp = string.CompareOrdinal(xSuf, ySuf);
        if (sufCmp != 0) return sufCmp;

        // 最終タイブレークは元文字列全体
        return string.CompareOrdinal(x, y);
    }

    /// <summary>
    /// 先頭の連続数字トークンを数値として抽出する。見つからない場合は 0 を返す。
    /// </summary>
    private static long ExtractLeadingNumber(string s)
    {
        var m = FirstDigitsRx.Match(s);
        if (!m.Success) return 0;
        if (long.TryParse(m.Value, out long n)) return n;
        return 0;
    }

    /// <summary>
    /// 「先頭の非数字 + 先頭の連続数字」を取り除いた残りの文字列を返す。
    /// 例：<c>"M-7-2"</c> → <c>"-2"</c>、<c>"M3"</c> → <c>""</c>、<c>"M220b Cut"</c> → <c>"b Cut"</c>
    /// </summary>
    private static string ExtractSuffix(string s)
    {
        // 先頭の非数字を取り除く（"M-" → "")
        string t = StripLeadingNonDigitsRx.Replace(s, string.Empty);
        // 残りの先頭数字を取り除く（"7-2" → "-2"）
        t = StripLeadingDigitsRx.Replace(t, string.Empty);
        return t;
    }
}
