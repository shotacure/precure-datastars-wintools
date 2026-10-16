using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// フォントのページ（<c>/fonts/</c>・<c>/fonts/{書体}/</c>）に並べる書体の見本の画像の材料。
/// 見本はサブタイトルのテロップ画像と同じ描き方（白い字・黒いフチ・影、同じ作り置き）で、作品の書体の表の 1 行
/// （<see cref="SeriesFontUsage"/>）ごとに 1 枚描く。ルビの行は親字の見本の中に描くので、ルビの行だけの画像は作らない。
/// <list type="bullet">
///   <item>サブタイトル：その作品の第 1 話のサブタイトルを、作品のテロップの組み方のまま描く（各話ページの画像と同じ作り置きを使う）。</item>
///   <item>歌詞：「女の子だって　暴れたい！」で統一し、同じ時期の歌詞のルビの行があれば「女」「子」「暴」にその書体でルビを振る。</item>
///   <item>クレジット：OP は「原作　東堂いづみ」、ED は「原画」と 3 人の人名。</item>
/// </list>
/// 描く書体（<c>fonts.render_font_name</c>）の無い書体は見本を描かない。
/// </summary>
public static class FontSampleRequests
{
    /// <summary>歌詞の見本の文（ルビ付き）。</summary>
    private const string LyricsSampleRubyHtml = "<ruby>女<rt>おんな</rt></ruby>の<ruby>子<rt>こ</rt></ruby>だって　<ruby>暴<rt>あば</rt></ruby>れたい！";

    /// <summary>歌詞の見本の文（ルビの行が無い作品）。</summary>
    private const string LyricsSamplePlain = "女の子だって　暴れたい！";

    /// <summary>OP クレジットの見本の文。</summary>
    private const string OpCreditSample = "原作　東堂いづみ";

    /// <summary>ED クレジットの見本の文（役職名と人名を 1 行ずつ）。</summary>
    private const string EdCreditSample = "原画<br>大工原 章<br>森 康二<br>大塚康生";

    /// <summary>書体のページの URL（<c>/fonts/{書体}/</c>）。</summary>
    public static string FontUrl(FontFace font) => $"/fonts/{UrlSlug.Encode(FontSlug(font))}/";

    /// <summary>書体のページのスラッグ（デコード済み。「ファン蘭_B」）。</summary>
    public static string FontSlug(FontFace font) => UrlSlug.FromName(font.DisplayName);

    /// <summary>
    /// 書体の表の 1 行の見本の材料。ルビの行・描く書体の無い書体・作品やサブタイトルの見つからない行は null。
    /// <paramref name="UrlPath"/> は書体のページ（ピンポイントビルドで <c>/fonts/</c> を指定したときも描かれるよう、書体ページの URL にする）。
    /// </summary>
    public static SubtitleTelopRequest? For(SeriesFontUsage usage, BuildContext ctx)
    {
        if (FontUsageKinds.IsRuby(usage.UsageKind)) return null;
        if (!ctx.FontById.TryGetValue(usage.FontId, out var font) || string.IsNullOrWhiteSpace(font.RenderFontName)) return null;
        if (!ctx.SeriesById.TryGetValue(usage.SeriesId, out var series)) return null;

        string urlPath = FontUrl(font);
        string relativePath = $"font-samples/{series.Slug}/{usage.UsageId}.png";

        if (usage.UsageKind == FontUsageKinds.Subtitle)
        {
            // 第 1 話（サブタイトルのある最初の話）を、作品のテロップの組み方で描く。書体はこの行の書体。
            if (!ctx.EpisodesBySeries.TryGetValue(series.SeriesId, out var episodes)) return null;
            var first = episodes.Where(e => !string.IsNullOrEmpty(e.TitleText)).OrderBy(e => e.SeriesEpNo).FirstOrDefault();
            if (first is null) return null;
            string html = string.IsNullOrEmpty(first.TitleRichHtml) ? HtmlUtil.Escape(first.TitleText) : first.TitleRichHtml;
            return new SubtitleTelopRequest(urlPath, relativePath, html, font.RenderFontName, SubtitleTelopProfile.FromSeries(series));
        }

        if (FontUsageKinds.IsLyrics(usage.UsageKind))
        {
            // 同じ範囲の歌詞のルビの行（無ければ同じ時期の通常の行、さらに無ければ通常の行のどれか）の書体でルビを振る。
            var ruby = FindRuby(usage, ctx);
            if (ruby is null) return new SubtitleTelopRequest(urlPath, relativePath, LyricsSamplePlain, font.RenderFontName, SubtitleTelopProfile.Default);
            string rubyFont = ctx.FontById.TryGetValue(ruby.FontId, out var rf) && !string.IsNullOrWhiteSpace(rf.RenderFontName)
                ? rf.RenderFontName : font.RenderFontName;
            var profile = SubtitleTelopProfile.Default with
            {
                RubyFontFamily = rubyFont == font.RenderFontName ? "" : rubyFont
            };
            return new SubtitleTelopRequest(urlPath, relativePath, LyricsSampleRubyHtml, font.RenderFontName, profile);
        }

        if (FontUsageKinds.IsCredit(usage.UsageKind))
        {
            string text = usage.UsageKind == FontUsageKinds.OpCredit ? OpCreditSample : EdCreditSample;
            return new SubtitleTelopRequest(urlPath, relativePath, text, font.RenderFontName, SubtitleTelopProfile.Default);
        }

        return null;
    }

    /// <summary>歌詞の行に対応する、歌詞のルビの行。</summary>
    private static SeriesFontUsage? FindRuby(SeriesFontUsage lyrics, BuildContext ctx)
    {
        string rubyKind = lyrics.UsageKind == FontUsageKinds.OpLyrics ? FontUsageKinds.OpLyricsRuby : FontUsageKinds.EdLyricsRuby;
        var candidates = ctx.FontUsages.Where(u => u.SeriesId == lyrics.SeriesId && u.UsageKind == rubyKind).ToList();
        return candidates.FirstOrDefault(u => u.PeriodNo == lyrics.PeriodNo && u.IsBroadcastOnly == lyrics.IsBroadcastOnly
                                              && u.FirstEpisodeNo == lyrics.FirstEpisodeNo)
            ?? candidates.FirstOrDefault(u => u.PeriodNo == lyrics.PeriodNo && !u.IsBroadcastOnly)
            ?? candidates.FirstOrDefault(u => !u.IsBroadcastOnly);
    }

    /// <summary>見本を描く全行の材料（書体の表の並び）。ビルド冒頭の作り置きの確認が、各話のテロップ画像と合わせて使う。</summary>
    public static IEnumerable<SubtitleTelopRequest> EnumerateAll(BuildContext ctx)
    {
        foreach (var usage in ctx.FontUsages)
        {
            if (For(usage, ctx) is { } request) yield return request;
        }
    }
}
