using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 1 話ぶんのサブタイトルのテロップ画像を描くための材料（置くページ・出力先・サブタイトル・書体・組み方）。
/// エピソード詳細の生成と、ビルド冒頭の作り置きの確認（<see cref="SubtitleTelopPreflight"/>）が同じものを使う。
/// </summary>
/// <param name="UrlPath">画像を置くページの URL パス（ピンポイントビルドの判定に使う）。</param>
/// <param name="RelativePath">出力先（サイトルートからの相対パス。例 <c>subtitles/2004tv/42.png</c>）。</param>
/// <param name="RubyHtml">ルビ付きのサブタイトル（<c>title_rich_html</c>、無ければ <c>title_text</c> をエスケープしたもの）。</param>
/// <param name="FontFamily">作品の本編テロップの書体名（<c>series_subtitle_styles.font_subtitle</c>）。</param>
/// <param name="Profile">作品ごとの組み方。</param>
public sealed record SubtitleTelopRequest(
    string UrlPath,
    string RelativePath,
    string RubyHtml,
    string FontFamily,
    SubtitleTelopProfile Profile)
{
    /// <summary>
    /// その話のテロップ画像の材料を返す。サブタイトル未確定の話と、ビルドの時点で解禁前の話は画像にしないので null
    /// （解禁前の話は画像にするとぼかしが効かないため、HTML のサブタイトル（ガード付き）を出す）。
    /// </summary>
    public static SubtitleTelopRequest? For(Series series, Episode ep, BuildContext ctx)
    {
        if (string.IsNullOrEmpty(ep.TitleText)) return null;
        var revealAt = SubtitleGuardRenderer.RevealAtFor(ep.EpisodeId, ctx.SubtitleRevealAtByEpisodeId);
        if (SubtitleGuardRenderer.IsEmbargoedAt(revealAt, ctx.BuildStartedAt)) return null;

        return new SubtitleTelopRequest(
            PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo),
            $"subtitles/{series.Slug}/{ep.SeriesEpNo}.png",
            string.IsNullOrEmpty(ep.TitleRichHtml) ? HtmlUtil.Escape(ep.TitleText) : ep.TitleRichHtml,
            series.FontSubtitle ?? "",
            SubtitleTelopProfile.FromSeries(series));
    }

    /// <summary>エピソード詳細を作る全話（シリーズ順 → 話数順）のうち、テロップ画像を描く話の材料を列挙する。</summary>
    public static IEnumerable<SubtitleTelopRequest> EnumerateAll(BuildContext ctx)
    {
        foreach (var series in ctx.Series)
        {
            if (!ctx.EpisodesBySeries.TryGetValue(series.SeriesId, out var episodes)) continue;
            foreach (var ep in episodes)
            {
                if (For(series, ep, ctx) is { } request) yield return request;
            }
        }
    }

    /// <summary>
    /// 作り置きの鍵（16 進 32 桁）。描き方の版（<see cref="OgCardRenderer.TelopRenderVersion"/>）・書体名・組み方・サブタイトルの
    /// どれかが変われば変わるので、同じ鍵の画像があれば描き直さずに使ってよい。
    /// </summary>
    public string CacheKey()
    {
        var material = string.Join('\n',
            OgCardRenderer.TelopRenderVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontFamily,
            JsonSerializer.Serialize(Profile),
            RubyHtml);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32].ToLowerInvariant();
    }

    /// <summary>この画像を描くのに要る書体名（親字の書体と、指定があれば振り仮名の書体）。</summary>
    public IEnumerable<string> RequiredFontFamilies()
    {
        if (FontFamily.Length > 0) yield return FontFamily;
        if (Profile.RubyFontFamily.Length > 0) yield return Profile.RubyFontFamily;
    }
}
