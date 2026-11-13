using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// ビルドの冒頭（出力に手を付ける前）に、サブタイトルのテロップ画像と書体の見本の画像（どちらもテロップの体裁）の作り置きを確かめる。
/// <list type="bullet">
/// <item>今回のビルドで描き直しが要る画像（同じ鍵の作り置きが無い、または <c>--refresh-telop</c>）について、
/// 描くのに要る書体がこの PC にあるかを描くときと同じ引き方で確かめ、1 つでも無ければ例外で止める
/// （既定の書体に差し替えて描いたり、出力に手を付けてから止まったりしないため）。作り置きで足りる画像は書体が無くても通る。</item>
/// <item>鍵を最後に使った日を索引（<c>index.json</c>。<see cref="CacheIndex"/>）に控え、全体ビルド（<c>--page</c> なし）では、
/// <see cref="CacheIndex.UnusedDays"/> 日使われなかった鍵の作り置きを消す（サブタイトルや組み方を変える前の古い画像）。
/// 使わなかった鍵をその場で消すと、中身の違うコードのビルドどうしが互いの作り置きを消し合うので、日数で消す。</item>
/// </list>
/// </summary>
public static class SubtitleTelopPreflight
{
    /// <summary>作り置きを確かめる。描き直しに要る書体が無ければ <see cref="InvalidOperationException"/>。</summary>
    public static void Run(BuildContext ctx, BuildConfig config, BuildLogger logger)
    {
        var dir = config.SubtitleTelopCacheDirectory;
        var requests = SubtitleTelopRequest.EnumerateAll(ctx)
            .Select(r => (Request: r, Key: r.CacheKey()))
            .ToList();

        int cached = 0;
        var toRender = new List<SubtitleTelopRequest>();
        var index = new CacheIndex(Path.Combine(dir, "index.json"));
        foreach (var (request, key) in requests)
        {
            // ピンポイントビルドで描かないページの画像は確かめない（PageRenderer の対象判定と同じ）。
            if (!config.MatchesPageFilter(request.UrlPath)) continue;
            index.Set(key, "");
            if (!config.RefreshSubtitleTelops && File.Exists(Path.Combine(dir, key + ".png"))) cached++;
            else toRender.Add(request);
        }

        var missing = toRender
            .SelectMany(r => r.RequiredFontFamilies().Select(font => (Font: font, Request: r)))
            .GroupBy(x => x.Font, StringComparer.Ordinal)
            .Where(g => !OgCardRenderer.IsTypefaceInstalled(g.Key))
            .ToList();
        if (missing.Count > 0)
        {
            var lines = missing.Select(g =>
            {
                // 出力先 subtitles/{シリーズ slug}/{話数}.png・font-samples/{シリーズ slug}/{行 ID}.png から作品を拾う。
                var slugs = g.Select(x => x.Request.RelativePath.Split('/')[1]).Distinct().ToList();
                return $"  書体「{g.Key}」（{string.Join("・", slugs)} の {g.Count()} 枚）";
            });
            throw new InvalidOperationException(
                "サブタイトルのテロップ画像・書体の見本を描き直すのに要る書体が、この PC に見つかりません。出力には手を付けずに止めます。\n"
                + string.Join("\n", lines));
        }

        logger.Info($"Subtitle telops  : 作り置き {cached} 件 / 描き直し {toRender.Count} 件（{dir}）");

        // 今回使った鍵を索引に控える。全体ビルドでは、決まった日数使われなかった鍵を索引から落とす。
        bool fullBuild = string.IsNullOrEmpty(config.PageFilter);
        index.Save(prune: fullBuild);
        if (!fullBuild || !Directory.Exists(dir)) return;

        // 全体ビルドでは、索引に無い鍵の作り置きと、描きかけで残った一時ファイルを消す（どちらも 1 日より前のものだけ。
        // 同じ時間に別のビルドが描いたばかりの画像を消さないように）。
        var staleBefore = DateTime.UtcNow.AddDays(-1);
        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
        {
            if (index.Contains(Path.GetFileNameWithoutExtension(file))) continue;
            if (File.GetLastWriteTimeUtc(file) >= staleBefore) continue;
            File.Delete(file);
            removed++;
        }
        foreach (var file in Directory.EnumerateFiles(dir, "*.tmp"))
            if (File.GetLastWriteTimeUtc(file) < staleBefore) File.Delete(file);
        if (removed > 0) logger.Info($"Subtitle telops  : {CacheIndex.UnusedDays} 日使わなかった作り置き {removed} 件を消しました");
    }

    /// <summary>
    /// 書体の表の描く書体名（<c>fonts.render_font_name</c>）が、この PC でどう引けるかをログに残す。
    /// 書体ファイルの索引で引けた書体は数だけ、それ以外の経路で引けた書体と引けない書体は名前を出す。
    /// 引けない書体があっても止めない（作り置きで足りているあいだは描かない。描くときは <see cref="Run"/> とカードの側で止める・描かない）。
    /// </summary>
    public static void LogFontRoutes(BuildContext ctx, BuildLogger logger)
    {
        var names = ctx.FontById.Values
            .Select(f => f.RenderFontName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        var probed = names.Select(n => (Name: n, Probe: OgCardRenderer.ProbeTypeface(n))).ToList();
        int byIndex = probed.Count(p => p.Probe.Found && p.Probe.Route == "書体ファイルの索引");
        var others = probed.Where(p => p.Probe.Found && p.Probe.Route != "書体ファイルの索引").ToList();
        var missing = probed.Where(p => !p.Probe.Found).ToList();
        logger.Info($"Fonts            : 書体の表の {names.Count} 書体（書体ファイルの索引 {byIndex} / ほかの経路 {others.Count} / 引けない {missing.Count}）");
        foreach (var (name, probe) in others) logger.Info($"  書体「{name}」: {probe.Route}");
        foreach (var (name, probe) in missing) logger.Info($"  書体「{name}」: {probe.Route}");
    }
}
