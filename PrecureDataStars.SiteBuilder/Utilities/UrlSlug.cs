using System.Text;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 表示名から URL パスの 1 セグメント（パーセントエンコード前の素の文字列）を作るヘルパー。
/// 人物・キャラクター・企業の詳細ページ URL（<c>/people/高橋任治/</c> 等）に使う。
/// <list type="bullet">
///   <item><description>Unicode 正規化（NFC）してから前後の空白を落とす。</description></item>
///   <item><description>空白（半角・全角）の連なりは、前後が両方とも全角文字（漢字・かな・全角記号）なら詰め、
///     それ以外は <c>_</c> にする（<c>高橋 任治</c> → <c>高橋任治</c>、<c>John Smith</c> → <c>John_Smith</c>）。</description></item>
///   <item><description>URL・S3 キー・Windows のファイル名で問題になる記号（<c>/ \ : * ? " &lt; &gt; | # % + { } ^ ` [ ] ~</c>）と
///     制御文字は <c>_</c> にする（<c>キュアブラック / 美墨なぎさ</c> → <c>キュアブラック_美墨なぎさ</c>）。</description></item>
///   <item><description><c>_</c> の連なりは 1 つに畳み、前後の <c>_</c> と <c>.</c> は落とす
///     （Windows は末尾ドットのディレクトリ名を作れないため）。</description></item>
///   <item><description>Windows の予約デバイス名（CON / NUL / COM1 等）と一致するときは末尾に <c>_</c> を足す。</description></item>
/// </list>
/// 出力はデコード済みの文字列で、href 等に埋めるときは <see cref="Uri.EscapeDataString"/> を通す。
/// </summary>
public static class UrlSlug
{
    private const string ForbiddenChars = "/\\:*?\"<>|#%+{}^`[]~";

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>表示名からスラッグ（デコード済み）を作る。記号しか無い名前は空文字を返す。</summary>
    public static string FromName(string name)
    {
        var s = (name ?? "").Normalize(NormalizationForm.FormC).Trim();
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (char.IsWhiteSpace(ch))
            {
                int j = i;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                char prev = sb.Length > 0 ? sb[^1] : '\0';
                char next = j < s.Length ? s[j] : '\0';
                if (!(IsWide(prev) && IsWide(next))) sb.Append('_');
                i = j - 1;
                continue;
            }
            if (char.IsControl(ch) || ForbiddenChars.IndexOf(ch) >= 0)
            {
                sb.Append('_');
                continue;
            }
            sb.Append(ch);
        }

        // "_" の連なりを 1 つに畳み、前後の "_" と "." を落とす。
        var collapsed = new StringBuilder(sb.Length);
        foreach (var ch in sb.ToString())
        {
            if (ch == '_' && collapsed.Length > 0 && collapsed[^1] == '_') continue;
            collapsed.Append(ch);
        }
        var slug = collapsed.ToString().Trim('_', '.');
        if (WindowsReservedNames.Contains(slug)) slug += "_";
        return slug;
    }

    /// <summary>スラッグを URL パスのセグメントとして埋められる形（パーセントエンコード済み）にする。</summary>
    public static string Encode(string slug) => Uri.EscapeDataString(slug);

    /// <summary>全角扱いの文字か（CJK 部首補助以降の漢字・かな・全角記号・半角カナ）。空白を詰めるかの判定に使う。</summary>
    private static bool IsWide(char c) => c >= '⺀';
}
