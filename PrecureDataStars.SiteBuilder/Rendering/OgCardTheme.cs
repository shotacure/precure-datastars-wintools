namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// OGP カードの配色。既定（<see cref="Default"/>）は precure.tv のピンク（サイトの <c>.hero.hero-gradient</c> と同値）。
/// precure.news のように別の配色のサイトのカードを同じ描き方で描くときに、色だけを差し替える（<c>--og-cards</c> の一覧の <c>theme</c>）。
/// 色はどれも <c>"#rrggbb"</c> の形。
/// </summary>
/// <param name="Background">地色のグラデーション（上・中ほど・下の 3 色）。</param>
/// <param name="Accent">アクセント。見出し下の罫、数字、左端の色帯の既定に使う。</param>
/// <param name="HeroTitle">ヒーロー調（ホーム）の見出しの色。</param>
/// <param name="HeroLead">ヒーロー調の前置き・タグライン・特徴の言葉の色。</param>
/// <param name="Hairline">罫線（フッタの区切り・帯グラフの外枠）。</param>
public sealed record OgCardTheme(IReadOnlyList<string> Background, string Accent, string HeroTitle, string HeroLead, string Hairline)
{
    /// <summary>precure.tv の配色。</summary>
    public static OgCardTheme Default { get; } = new(
        new[] { "#fff1f6", "#fde7ef", "#fff8f0" }, "#e91e63", "#be185d", "#9d174d", "#e7c8d4");
}
