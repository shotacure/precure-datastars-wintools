using System.Configuration;
using System.Net;
using System.Text.Json;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Configuration;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 他のサイト（precure.news など）の OGP カードを、このサイトと同じ描き方・同じ書体で描く起動方法（<c>--og-cards</c>）。
/// カードの一覧（JSON）を読んで PNG を書き出す。書体は App.config の <c>OgCard*Font</c> を使う。
/// <para>
/// 一覧の形：<c>{ "brand": "フッタのサイト名", "cards": [ { "path": "2026/01/11/slug.png", "spec": { …OgCardSpec… } } ] }</c>。
/// <c>path</c> は出力先フォルダからの相対パス、<c>spec</c> は <see cref="OgCardSpec"/> を JSON にしたもの（名前の大文字小文字は問わない）。
/// </para>
/// <c>episode</c>（precure.tv のエピソードの URL のうち /series/ の後ろ。例 "2026tv/37"）を持つカードは、
/// DB（App.config の接続文字列）からその話を引き、補助行（『作品名』第N話）と、エピソードのカードと同じ組み方のサブタイトル
/// （本編テロップの書体・ルビ）を足す（precure.news の感想記事）。DB を使うのはこのカードがあるときだけ。
/// 出力先の PNG のうち一覧に無いものは消す（記事を消したときに古いカードが残らないように）。
/// 同じ材料からは同じバイト列が出るので、描き直しても中身が変わらないカードは git の差分にならない。
/// </summary>
internal static class OgCardBatch
{
    private sealed record Manifest(string Brand, IReadOnlyList<ManifestCard> Cards);

    private sealed record ManifestCard(string Path, OgCardSpec Spec, string? Episode = null);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>一覧のカードをすべて描く。戻り値は終了コード。</summary>
    /// <param name="manifestPath">カードの一覧（JSON）のパス。</param>
    /// <param name="outputDirectory">PNG の出力先フォルダ。</param>
    public static int Run(string manifestPath, string outputDirectory)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidOperationException($"カードの一覧を読めませんでした: {manifestPath}");
        var outRoot = Path.GetFullPath(outputDirectory);

        using var renderer = new OgCardRenderer(manifest.Brand, BuildConfig.ReadOgCardFonts());
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EpisodeLookup? episodes = null;
        int warnings = 0;
        foreach (var card in manifest.Cards)
        {
            if (!card.Spec.IsRenderable) continue;
            var file = Path.GetFullPath(Path.Combine(outRoot, card.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(outRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"出力先フォルダの外を指すパスです: {card.Path}");

            var spec = card.Spec;
            if (!string.IsNullOrWhiteSpace(card.Episode))
            {
                episodes ??= EpisodeLookup.Load();
                if (episodes.Find(card.Episode.Trim('/')) is { } hit)
                {
                    spec = spec with
                    {
                        Subtitle = $"『{hit.Series.Title}』第{hit.Episode.SeriesEpNo}話",
                        EpisodeTitleRubyHtml = !string.IsNullOrEmpty(hit.Episode.TitleRichHtml)
                            ? hit.Episode.TitleRichHtml
                            : WebUtility.HtmlEncode(hit.Episode.TitleDisplayText),
                        EpisodeTitleFontFamily = hit.Series.FontSubtitle ?? "",
                        EpisodeTitleRubyProfile = SubtitleTelopProfile.FromSeries(hit.Series)
                    };
                }
                else
                {
                    warnings++;
                    Console.Error.WriteLine($"警告: {card.Path}: エピソード {card.Episode} が DB に見つかりません。サブタイトルなしで描きます");
                }
            }

            var missing = renderer.Render(spec, file);
            written.Add(file);
            if (missing is not null)
            {
                warnings++;
                Console.Error.WriteLine($"警告: {card.Path}: {missing}");
            }
        }

        int removed = 0;
        if (Directory.Exists(outRoot))
        {
            foreach (var png in Directory.EnumerateFiles(outRoot, "*.png", SearchOption.AllDirectories))
            {
                if (written.Contains(png)) continue;
                File.Delete(png);
                removed++;
            }
            // 消したカードの入っていたフォルダが空になったら、それも消す（深い順に見る）。
            foreach (var dir in Directory.EnumerateDirectories(outRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
        }

        Console.WriteLine($"OGP カード: {written.Count} 枚を描きました（消した古いカード {removed} 枚、警告 {warnings} 件）。");
        return 0;
    }

    /// <summary>作品の slug と話数からエピソードを引く表（DB から 1 度だけ読む）。</summary>
    private sealed class EpisodeLookup
    {
        private readonly Dictionary<string, (Series Series, Episode Episode)> _byKey;

        private EpisodeLookup(Dictionary<string, (Series Series, Episode Episode)> byKey) => _byKey = byKey;

        /// <summary>"2026tv/37" の形で引く。無ければ null。</summary>
        public (Series Series, Episode Episode)? Find(string key) => _byKey.TryGetValue(key, out var hit) ? hit : null;

        public static EpisodeLookup Load()
        {
            var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString
                ?? throw new InvalidOperationException("App.config に接続文字列 'DatastarsMySql' がありません（感想記事のカードに要る）。");
            var factory = MySqlConnectionFactory.FromConnectionString(cs);
            var seriesList = new SeriesRepository(factory).GetAllAsync().GetAwaiter().GetResult();
            var episodes = new EpisodesRepository(factory).GetAllAsync().GetAwaiter().GetResult();
            var seriesById = seriesList.ToDictionary(s => s.SeriesId);
            var byKey = new Dictionary<string, (Series Series, Episode Episode)>(StringComparer.OrdinalIgnoreCase);
            foreach (var ep in episodes)
            {
                if (!seriesById.TryGetValue(ep.SeriesId, out var series)) continue;
                byKey[$"{series.Slug}/{ep.SeriesEpNo}"] = (series, ep);
            }
            return new EpisodeLookup(byKey);
        }
    }
}
