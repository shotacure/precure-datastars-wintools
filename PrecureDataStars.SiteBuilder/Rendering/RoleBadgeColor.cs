using System.Globalization;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 役職バッジ（<c>.role-badge</c>）の色を 1 色（<c>#rrggbb</c>）から組み立てる。
/// メインスタッフのバッジ（シリーズ一覧のカード・作品詳細）は、作品ごとの行（<c>series_key_staff_roles.badge_color</c>）の色を
/// インラインの style で当てる。地と枠はその色を薄く敷き（地 13%・枠 42%）、字はその色を暗くして、
/// 白の上に敷いた地との間で 4.5:1 のコントラストを満たす最も明るい色にする（site.css の役職ごとの配色と同じ作り方）。
/// </summary>
public static class RoleBadgeColor
{
    /// <summary>地の不透明度。</summary>
    private const double BackgroundAlpha = 0.13;

    /// <summary>枠の不透明度。</summary>
    private const double BorderAlpha = 0.42;

    /// <summary>字と地のコントラストの下限（WCAG AA の本文）。</summary>
    private const double MinContrast = 4.5;

    /// <summary>
    /// <c>#rrggbb</c> をバッジの style 属性の値（地・字・枠の色）にする。読めない色なら空文字
    /// （そのときは site.css の役職ごとの配色か既定の灰色で出る）。
    /// </summary>
    public static string InlineStyle(string hex)
    {
        if (!TryParse(hex, out var r, out var g, out var b)) return "";
        var text = TextColor(r, g, b);
        string a(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        return $"background:rgba({r},{g},{b},{a(BackgroundAlpha)});color:{text};border-color:rgba({r},{g},{b},{a(BorderAlpha)})";
    }

    /// <summary><c>#rrggbb</c> を小文字の <c>#rrggbb</c> にそろえる。読めなければ空文字（OGP カードは補助色にフォールバック）。</summary>
    public static string Normalize(string hex)
        => TryParse(hex, out var r, out var g, out var b) ? $"#{r:x2}{g:x2}{b:x2}" : "";

    /// <summary>
    /// 字の色：元の色の RGB を同じ比率で暗くしていき、白の上に敷いた地（元の色 13%）とのコントラストが
    /// 4.5:1 に届いた最初の（いちばん明るい）色。
    /// </summary>
    private static string TextColor(int r, int g, int b)
    {
        double bgR = 255 + (r - 255) * BackgroundAlpha;
        double bgG = 255 + (g - 255) * BackgroundAlpha;
        double bgB = 255 + (b - 255) * BackgroundAlpha;
        double bgL = Luminance(bgR, bgG, bgB);

        for (int step = 100; step >= 0; step--)
        {
            double k = step / 100.0;
            int tr = (int)Math.Round(r * k), tg = (int)Math.Round(g * k), tb = (int)Math.Round(b * k);
            double tL = Luminance(tr, tg, tb);
            if ((bgL + 0.05) / (tL + 0.05) >= MinContrast) return $"#{tr:x2}{tg:x2}{tb:x2}";
        }
        return "#000000";
    }

    /// <summary>sRGB の相対輝度（WCAG の定義）。</summary>
    private static double Luminance(double r, double g, double b)
    {
        static double ch(double c)
        {
            c /= 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * ch(r) + 0.7152 * ch(g) + 0.0722 * ch(b);
    }

    private static bool TryParse(string? hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#') return false;
        return int.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
            && int.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
            && int.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
    }
}
