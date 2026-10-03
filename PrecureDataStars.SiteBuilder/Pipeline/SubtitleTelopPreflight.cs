using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// ビルドの冒頭（出力に手を付ける前）に、サブタイトルのテロップ画像の作り置きを確かめる。
/// <list type="bullet">
/// <item>今回のビルドで描き直しが要る画像（同じ鍵の作り置きが無い、または <c>--refresh-telop</c>）について、
/// 描くのに要る書体がこの PC にあるかを描くときと同じ引き方で確かめ、1 つでも無ければ例外で止める
/// （既定の書体に差し替えて描いたり、出力に手を付けてから止まったりしないため）。作り置きで足りる画像は書体が無くても通る。</item>
/// <item>全体ビルド（<c>--page</c> なし）では、今回のどの話にも使わない鍵の作り置きを消す（サブタイトルや組み方を変える前の古い画像）。</item>
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
        foreach (var (request, key) in requests)
        {
            // ピンポイントビルドで描かないページの画像は確かめない（PageRenderer の対象判定と同じ）。
            if (!string.IsNullOrEmpty(config.PageFilter) && !request.UrlPath.Contains(config.PageFilter, StringComparison.Ordinal)) continue;
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
                // 出力先 subtitles/{シリーズ slug}/{話数}.png から作品を拾う。
                var slugs = g.Select(x => x.Request.RelativePath.Split('/')[1]).Distinct().ToList();
                return $"  書体「{g.Key}」（{string.Join("・", slugs)} の {g.Count()} 話）";
            });
            throw new InvalidOperationException(
                "サブタイトルのテロップ画像を描き直すのに要る書体が、この PC に見つかりません。出力には手を付けずに止めます。\n"
                + string.Join("\n", lines));
        }

        logger.Info($"Subtitle telops  : 作り置き {cached} 件 / 描き直し {toRender.Count} 件（{dir}）");

        if (!string.IsNullOrEmpty(config.PageFilter) || !Directory.Exists(dir)) return;

        // 全体ビルドでは、今回のどの話にも使わない鍵の作り置きと、描きかけで残った一時ファイルを消す。
        var used = requests.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        int removed = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
        {
            if (used.Contains(Path.GetFileNameWithoutExtension(file))) continue;
            File.Delete(file);
            removed++;
        }
        foreach (var file in Directory.EnumerateFiles(dir, "*.tmp")) File.Delete(file);
        if (removed > 0) logger.Info($"Subtitle telops  : 使わなくなった作り置き {removed} 件を消しました");
    }
}
