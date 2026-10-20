using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// テロップの体裁で描く画像 1 枚の材料（置くページ・出力先・文字列・書体・組み方）。1 話ぶんのサブタイトルのほか、
/// フォントのページの書体の見本（<see cref="FontSampleRequests"/>）も同じ材料で描く。
/// エピソード詳細・フォントのページの生成と、ビルド冒頭の作り置きの確認（<see cref="SubtitleTelopPreflight"/>）が同じものを使う。
/// </summary>
/// <param name="UrlPath">画像を置くページの URL パス（ピンポイントビルドの判定に使う）。</param>
/// <param name="RelativePath">出力先（サイトルートからの相対パス。例 <c>subtitles/2004tv/42.png</c>・<c>font-samples/2004tv/12.png</c>。
/// 2 つ目の区切りは作品の slug）。</param>
/// <param name="RubyHtml">ルビ付きの文字列（サブタイトルは <c>title_rich_html</c>、無ければ <c>title_text</c> をエスケープしたもの）。</param>
/// <param name="FontFamily">描く書体名（作品の書体の表が指す <c>fonts.render_font_name</c>）。</param>
/// <param name="Profile">作品ごとの組み方。</param>
public sealed record SubtitleTelopRequest(
    string UrlPath,
    string RelativePath,
    string RubyHtml,
    string FontFamily,
    SubtitleTelopProfile Profile)
{
    /// <summary>
    /// 場面の画像の材料（フォントのページの OP・ED の画面）。あれば <see cref="RubyHtml"/>・<see cref="FontFamily"/>・<see cref="Profile"/> は使わず、
    /// この場面を描く。
    /// </summary>
    public FontScene? Scene { get; init; }

    /// <summary>
    /// 書体見本の字の並び（右上・左下の 16 字ずつ）。あれば <see cref="FontFamily"/> の書体見本を描く。
    /// </summary>
    public (string TopRight, string BottomLeft)? Specimen { get; init; }
    /// <summary>
    /// その話のテロップ画像の材料を返す。サブタイトル未確定の話は画像にしないので null。
    /// ビルドの時点で解禁前の話も描く（ページでは、解禁されるか閲覧者が先の題を見る設定にするまで画像を出さず、
    /// ぼかした HTML のサブタイトルを出す。OGP カードは作らない）。
    /// </summary>
    public static SubtitleTelopRequest? For(Series series, Episode ep, BuildContext ctx)
    {
        if (string.IsNullOrEmpty(ep.TitleText)) return null;

        var profile = WithSubtitleStyle(SubtitleTelopProfile.FromSeries(series), series, ctx, ep.SeriesEpNo);
        // 字を傾ける回が決まっている作品は、一覧に無い回では傾けない
        if (profile.Decoration.CharTilt is { Episodes: { } tiltEpisodes } && !tiltEpisodes.Contains(ep.SeriesEpNo))
            profile = profile with { Decoration = profile.Decoration with { CharTilt = null } };
        return new SubtitleTelopRequest(
            PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo),
            $"subtitles/{series.Slug}/{ep.SeriesEpNo}.png",
            string.IsNullOrEmpty(ep.TitleRichHtml) ? HtmlUtil.Escape(ep.TitleText) : ep.TitleRichHtml,
            series.FontSubtitle ?? "",
            profile);
    }

    /// <summary>
    /// 作品の書体の表のサブタイトルの行（通常の時期の最初の行）の装飾・長体の下限を組み方に重ねる。装飾があれば、放送の画面と比べて
    /// 測ったときと同じ計算（画素で重ねる描き方）で、字の大きさに対する比のまま描く。行が無ければ既定の体裁のまま。
    /// <paramref name="episodeNo"/> があれば、その回の行ごとの横の倍率（<c>subtitle.episodeLineScales</c>）も重ねる。
    /// </summary>
    public static SubtitleTelopProfile WithSubtitleStyle(SubtitleTelopProfile profile, Series series, BuildContext ctx, int? episodeNo = null)
    {
        var usage = ctx.FontUsages.FirstOrDefault(u => u.SeriesId == series.SeriesId && u.UsageKind == FontUsageKinds.Subtitle
                                                      && !u.IsBroadcastOnly && u.VariantNo == 1);
        if (usage is null) return profile;
        var decoration = TelopDecoration.Parse(usage.Decoration);
        var layout = TelopLayout.Parse(usage.Layout);
        return profile with
        {
            Decoration = decoration ?? profile.Decoration,
            RasterDecoration = decoration is not null,
            CondenseMin = layout?.Subtitle?.CondenseMin ?? profile.CondenseMin,
            RubyKeepWidth = layout?.Subtitle?.RubyKeepWidth ?? false,
            LineScales = episodeNo is int no && layout?.Subtitle?.EpisodeLineScales is { } perEpisode
                         && perEpisode.TryGetValue(no.ToString(System.Globalization.CultureInfo.InvariantCulture), out var lineScales)
                ? lineScales
                : null,
            RubyRaiseOffsets = episodeNo is int no2 && layout?.Subtitle?.EpisodeRubyOffsets is { } rubyPerEpisode
                         && rubyPerEpisode.TryGetValue(no2.ToString(System.Globalization.CultureInfo.InvariantCulture), out var rubyOffsets)
                ? rubyOffsets
                : null,
            LineOffsets = episodeNo is int no3 && layout?.Subtitle?.EpisodeLineOffsets is { } linePerEpisode
                         && linePerEpisode.TryGetValue(no3.ToString(System.Globalization.CultureInfo.InvariantCulture), out var lineOffsets)
                ? lineOffsets
                : null,
            // 行の最大幅（画面の幅に対する比）を、測った字の大きさに対する比に直す（4:3 の作品は幅 1440 の画面）
            LineMaxEm = layout?.Subtitle?.MaxWidth is float mw && usage.SizePx is decimal sz && sz > 0
                ? mw * (layout.Screen?.Aspect == "4:3" ? 1440f : 1920f) / (float)sz
                : null
        };
    }

    /// <summary>
    /// エピソード詳細を作る全話（シリーズ順 → 話数順）のうちテロップ画像を描く話の材料と、
    /// フォントのページの書体の見本の材料（<see cref="FontSampleRequests.EnumerateAll"/>）を列挙する。
    /// </summary>
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
        foreach (var sample in FontSampleRequests.EnumerateAll(ctx)) yield return sample;
    }

    /// <summary>
    /// 作り置きの鍵（16 進 32 桁）。描画コードの印（<see cref="OgRenderSourceStamp"/>。描き方のコードを変えると変わる）・
    /// 描き方の版（<see cref="OgCardRenderer.TelopRenderVersion"/>）・書体名・組み方・サブタイトルのどれかが変われば変わるので、
    /// 同じ鍵の画像があれば描き直さずに使ってよい。書体はファイルの中身ではなく名前で見る。
    /// </summary>
    public string CacheKey()
    {
        var material = string.Join('\n',
            OgRenderSourceStamp.Value,
            OgCardRenderer.TelopRenderVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontFamily,
            JsonSerializer.Serialize(Profile),
            RubyHtml,
            (Scene is null ? "" : JsonSerializer.Serialize(Scene) + (OgCardRenderer.SceneTransparent ? "|transparent" : "")) + (OgCardRenderer.SceneGlyphOnly ? "|glyph" : ""),
            Specimen is { } sp ? $"{sp.TopRight}|{sp.BottomLeft}" : "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..32].ToLowerInvariant();
    }

    /// <summary>この画像を描くのに要る書体名（親字の書体と、指定があれば振り仮名の書体）。</summary>
    public IEnumerable<string> RequiredFontFamilies()
    {
        if (Scene is not null)
        {
            foreach (var piece in Scene.Pieces)
            {
                if (piece.FontFamily.Length > 0) yield return piece.FontFamily;
                if (piece.Profile.RubyFontFamily.Length > 0) yield return piece.Profile.RubyFontFamily;
            }
            yield break;
        }
        if (FontFamily.Length > 0) yield return FontFamily;
        if (Profile.RubyFontFamily.Length > 0) yield return Profile.RubyFontFamily;
    }
}
