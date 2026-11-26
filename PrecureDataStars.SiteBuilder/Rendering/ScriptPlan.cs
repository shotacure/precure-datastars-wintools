using System.Text.RegularExpressions;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// レイアウト（<c>_layout.sbn</c>）が読む JS のうち、要るページでだけ読むものの判定。本文の HTML に入口の印があるかで決め、
/// 結果を <see cref="LayoutModel"/> の <c>Needs…</c> に入れる。印が無いページでは、その JS は何もせずに終わるので、読まずに済ませる。
/// <list type="bullet">
///   <item><description>入口がレイアウトにある JS（検索・スマホのメニュー・共有・引用・字幕の伏せ・閲覧状態・フォーカスの閉じ込め・
///     配信音源の好み）は、ここに載せず全ページで読む。</description></item>
///   <item><description>販売中の商品の枠（<c>HasShopBox</c>）と YouTube（<c>HasYoutubeEmbeds</c>）の JS は、それぞれの判定で出し分けている。</description></item>
///   <item><description>どれも defer で読むので、何本か抜いても残りの JS の読む順番は変わらない。</description></item>
/// </list>
/// App.config の <c>ScriptPlan=false</c> で、全部の JS を全ページで読む形に戻る。
/// </summary>
public static class ScriptPlan
{
    /// <summary>id 付きの section の開きタグ（目次の JS は <c>section[id]</c> を全部拾う）。</summary>
    private static readonly Regex SectionWithIdRegex = new(@"<section\b[^>]*\bid=", RegexOptions.Compiled);

    /// <summary>
    /// 本文の HTML（<paramref name="contentHtml"/>。レイアウトに包む前のもの）から、JS ごとに読むかどうかを決めて詰める。
    /// <paramref name="enabled"/> が false なら全部読む。
    /// </summary>
    public static void Apply(LayoutModel layoutMeta, string contentHtml, bool enabled)
    {
        if (!enabled)
        {
            layoutMeta.NeedsCountSort = true;
            layoutMeta.NeedsCreditAlign = true;
            layoutMeta.NeedsReleaseCountdown = true;
            layoutMeta.NeedsSectionNav = true;
            return;
        }
        // 件数バッジでの並べ替え（count-sort.js）：ボタンはテンプレートの本文にだけ出る。
        layoutMeta.NeedsCountSort = contentHtml.Contains("count-sort-btn", StringComparison.Ordinal);
        // クレジット表の列の幅そろえ（credit-align.js）：.credit-section はエピソード詳細と作品詳細の本文にだけ出る。
        layoutMeta.NeedsCreditAlign = contentHtml.Contains("credit-section", StringComparison.Ordinal);
        // 発売までの日数（release-countdown.js）：data-release-date はトップの本文にだけ出る。
        layoutMeta.NeedsReleaseCountdown = contentHtml.Contains("data-release-date", StringComparison.Ordinal);
        // 目次（section-nav.js）：id 付きの section が 2 つ以上あるときだけ目次を出す JS なので、2 つ未満なら読まない
        // （目次の置き場はレイアウトで最初から隠してあるので、読まなくても空の枠は出ない）。
        layoutMeta.NeedsSectionNav = CountAtLeast(SectionWithIdRegex, contentHtml, 2);
    }

    /// <summary><paramref name="regex"/> が <paramref name="text"/> に <paramref name="count"/> 回以上当たるか（数えきらずに済ませる）。</summary>
    private static bool CountAtLeast(Regex regex, string text, int count)
    {
        int found = 0;
        for (var m = regex.Match(text); m.Success; m = m.NextMatch())
        {
            if (++found >= count) return true;
        }
        return false;
    }
}
