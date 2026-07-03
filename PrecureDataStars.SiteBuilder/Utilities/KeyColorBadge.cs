namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>キーカラー付きバッジ（プリキュアバッジ等）の配色解決ヘルパー。 SeriesGenerator / HomeGenerator が同一実装で個別に保持していた計算を単一定義へ集約したもの。</summary>
public static class KeyColorBadge
{
    /// <summary>
    /// バッジ地色（<c>#RRGGBB</c>）から、地色・文字色・ボーダー色の 3 値を解決する。
    /// 文字色は地色の相対輝度（WCAG 2.x 定義の linearized sRGB 加重和）を求め、
    /// しきい値 0.179（黒文字と白文字のコントラストが拮抗する境界）で
    /// 暗グレー（<c>#1a1a1a</c>）／明グレー（<c>#f5f5f5</c>）を出し分ける。
    /// ボーダーは文字色側に寄せた半透明色で、地色がページ背景に近いときでも輪郭を保つ。
    /// 入力が <c>#RRGGBB</c> 形式でなければ 3 値とも空文字を返し、呼び出し側で
    /// インライン色を付けない（CSS 既定の淡色／中立バッジにフォールバックする）。
    /// </summary>
    public static (string Background, string Text, string Border) Resolve(string keyColor)
    {
        if (string.IsNullOrEmpty(keyColor)
            || keyColor.Length != 7
            || keyColor[0] != '#')
        {
            return ("", "", "");
        }

        int r, g, b;
        try
        {
            r = Convert.ToInt32(keyColor.Substring(1, 2), 16);
            g = Convert.ToInt32(keyColor.Substring(3, 2), 16);
            b = Convert.ToInt32(keyColor.Substring(5, 2), 16);
        }
        catch (FormatException)
        {
            // 16 進として解釈できない文字が混じっていた場合は無装飾フォールバック。
            return ("", "", "");
        }

        // sRGB 1 チャンネルを相対輝度計算用にリニアライズする（WCAG 2.x 定義）。
        static double Linearize(int channel)
        {
            double c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double luminance = 0.2126 * Linearize(r)
                         + 0.7152 * Linearize(g)
                         + 0.0722 * Linearize(b);

        // しきい値 0.179 より明るい地色 → 暗い文字、暗い地色 → 明るい文字。
        bool darkText = luminance > 0.179;
        string text = darkText ? "#1a1a1a" : "#f5f5f5";
        // ボーダーは文字色側へ寄せた半透明。地色がページ地と近くても輪郭が出る。
        string border = darkText ? "rgba(0, 0, 0, 0.22)" : "rgba(255, 255, 255, 0.30)";

        return (keyColor, text, border);
    }
}
