using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// ビルドの時点でまだ出さない話（<see cref="BuildContext.HiddenEpisodes"/>。予告も雑誌の発売もまだの話）が、
/// 出力のどこにも残っていないかを最後に確かめる安全網。読み込みの段階で索引から外していても、DB を直接引く集計や
/// 前のビルドの残りから漏れることがあるので、出力の文字のファイルを全部読んで照らす。
/// 照らすのは、その話のページの URL（<c>/series/{slug}/{n}/</c>）と、サブタイトル・ルビ付きの表記・読み
/// （そのまま・HTML に書いた形・JSON に書いた形）。見つかったら警告にする（警告が 1 件でもあれば定時デプロイは公開しない）。
/// サブタイトルが短いと関係の無い文に当たることがあるので、2 文字以下の文字列は照らさない（URL は必ず照らす）。
/// </summary>
internal static class PrereleaseOutputGuard
{
    /// <summary>照らす出力のファイル（文字のファイル）の拡張子。</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".json", ".xml", ".txt", ".js", ".webmanifest", ".csv",
    };

    /// <summary>出力を照らし、見つかった件数を返す（見つかったものは警告として出す）。</summary>
    public static int Check(BuildContext ctx, string outputDirectory)
    {
        if (ctx.HiddenEpisodes.Count == 0 || !Directory.Exists(outputDirectory)) return 0;

        var needles = new List<(string Text, string Label)>();
        foreach (var ep in ctx.HiddenEpisodes)
        {
            string slug = ctx.SeriesById.TryGetValue(ep.SeriesId, out var series) ? series.Slug : "";
            string label = $"『{(series?.Title ?? $"series {ep.SeriesId}")}』第{ep.SeriesEpNo}話";
            if (!string.IsNullOrEmpty(slug))
            {
                string url = PathUtil.EpisodeUrl(slug, ep.SeriesEpNo);
                needles.Add((url, $"{label}のページの URL"));
                // ページの書き出し先が残っていないか（前のビルドの残りも含めて）
                string dir = Path.Combine(outputDirectory, url.Trim('/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(dir))
                    ctx.Logger.Warn($"まだ出さない話の出力: {label}のページのフォルダが出力にあります（{dir}）");
            }
            foreach (var (text, what) in new[] { (ep.TitleText, "サブタイトル"), (ep.TitleRichHtml, "ルビ付きのサブタイトル"), (ep.TitleKana, "サブタイトルの読み") })
            {
                if (string.IsNullOrEmpty(text) || text.Length <= 2) continue;
                foreach (var form in new[] { text, HtmlUtil.Escape(text), JsonForm(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping), JsonForm(text, JavaScriptEncoder.Default) }.Distinct(StringComparer.Ordinal))
                    needles.Add((form, $"{label}の{what}"));
            }
        }

        var hits = new ConcurrentBag<(string File, string Label)>();
        var files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Where(f => TextExtensions.Contains(Path.GetExtension(f)));
        Parallel.ForEach(files, file =>
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch (IOException) { return; }
            foreach (var (text, label) in needles)
            {
                if (content.Contains(text, StringComparison.Ordinal))
                    hits.Add((Path.GetRelativePath(outputDirectory, file), label));
            }
        });

        foreach (var group in hits.GroupBy(h => h.Label).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var sample = group.Select(h => h.File).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
            ctx.Logger.Warn($"まだ出さない話の出力: {group.Key}が {sample.Count} ファイルにあります（例: {string.Join(", ", sample.Take(3))}）");
        }
        return hits.Count;
    }

    /// <summary>文字列を JSON の文字列に書いた形（前後の引用符を除く）。</summary>
    private static string JsonForm(string text, JavaScriptEncoder encoder)
    {
        string json = JsonSerializer.Serialize(text, new JsonSerializerOptions { Encoder = encoder });
        return json.Length >= 2 ? json[1..^1] : json;
    }
}
