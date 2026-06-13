using System.Globalization;
using Markdig;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>content/articles/*.md</c>（Markdown ＋ front-matter）から読み物記事を生成するジェネレータ。
/// DB に依存せず、ビルド出力へコピーされた Markdown を読み、<c>/articles/{slug}/</c> と
/// 一覧 <c>/articles/</c> を出力する。本文は Markdig で HTML 化し、既存レイアウト
/// （ヘッダ／フッタ／ナビ／シェア／AdSense）に流し込む。記事は運営情報ページと違い
/// シェア対象なので <see cref="LayoutModel.SuppressShareButtons"/> は立てない。
/// </summary>
public sealed class ArticlesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    // Markdig パイプライン。テーブル・自動リンク等の一般的な拡張を有効化する
    // （入力は内製の信頼ソースのため、サニタイズは行わない＝既存テンプレと同じ前提）。
    private static readonly MarkdownPipeline MarkdownPipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public ArticlesGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating articles");

        // 原稿の置き場は App.config の ArticlesContentDir（コードと分離した外部ディレクトリ）。
        var dir = _ctx.Config.ArticlesContentDir;

        var articles = new List<Article>();
        if (string.IsNullOrEmpty(dir))
        {
            _ctx.Logger.Warn("ArticlesContentDir が未設定のため、読み物は 0 件として継続します。");
        }
        else if (!Directory.Exists(dir))
        {
            _ctx.Logger.Warn($"記事ディレクトリが見つかりません: {dir}（記事 0 件として継続）");
        }
        else
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
            {
                var article = ParseArticle(path);
                if (article is not null) articles.Add(article);
            }
        }

        // 公開日降順（同日はタイトル昇順）で並べる。
        articles.Sort((a, b) =>
        {
            int cmp = b.Date.CompareTo(a.Date);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Title, b.Title);
        });

        // 各記事詳細。
        foreach (var a in articles)
        {
            var content = new ArticleContentModel
            {
                Title = a.Title,
                DateLabel = a.DateLabel,
                Tags = a.Tags,
                BodyHtml = a.BodyHtml,
            };
            var layout = new LayoutModel
            {
                PageTitle = a.Title,
                MetaDescription = a.Description,
                Breadcrumbs = new[]
                {
                    new BreadcrumbItem { Label = "ホーム", Url = "/" },
                    new BreadcrumbItem { Label = "読み物", Url = "/articles/" },
                    new BreadcrumbItem { Label = a.Title, Url = "" },
                },
            };
            _page.RenderAndWrite($"/articles/{a.Slug}/", "articles", "article-detail.sbn", content, layout);
        }

        // 一覧ページ。
        var indexModel = new ArticlesIndexModel
        {
            Articles = articles.Select(a => new ArticleSummary
            {
                Title = a.Title,
                Description = a.Description,
                Url = $"/articles/{a.Slug}/",
                DateLabel = a.DateLabel,
                Tags = a.Tags,
            }).ToList(),
        };
        var indexLayout = new LayoutModel
        {
            PageTitle = "読み物",
            MetaDescription =
                "プリキュアをもっと楽しむための記事・コラム。視聴ガイドやデータで見るプリキュアなど、" +
                "歴代シリーズを別の角度から掘り下げます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "読み物", Url = "" },
            },
        };
        _page.RenderAndWrite("/articles/", "articles", "articles-index.sbn", indexModel, indexLayout);

        _ctx.Logger.Success($"/articles/（{articles.Count} 記事）");
    }

    /// <summary>front-matter をパースし、本文を Markdig で HTML 化して <see cref="Article"/> を返す。
    /// タイトル欠落時は警告して null（スキップ）。</summary>
    private Article? ParseArticle(string path)
    {
        // 改行を LF に正規化してから front-matter（先頭 "---" 〜 次の "---"）を切り出す。
        var raw = File.ReadAllText(path).Replace("\r\n", "\n");
        var slug = Path.GetFileNameWithoutExtension(path);

        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string body = raw;

        if (raw.StartsWith("---\n", StringComparison.Ordinal))
        {
            int end = raw.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                var frontMatter = raw.Substring(4, end - 4);
                // 本文は閉じ "---" の行末（次の改行）以降。
                int bodyStart = raw.IndexOf('\n', end + 1);
                body = bodyStart >= 0 ? raw.Substring(bodyStart + 1) : "";

                foreach (var line in frontMatter.Split('\n'))
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    var key = line.Substring(0, colon).Trim();
                    var value = line.Substring(colon + 1).Trim();
                    if (key.Length > 0) meta[key] = value;
                }
            }
        }

        var title = meta.GetValueOrDefault("title", "").Trim();
        if (string.IsNullOrEmpty(title))
        {
            _ctx.Logger.Warn($"記事に title がありません（スキップ）: {Path.GetFileName(path)}");
            return null;
        }
        var description = meta.GetValueOrDefault("description", "").Trim();

        // slug は front-matter 指定があれば優先、無ければファイル名。
        var fmSlug = meta.GetValueOrDefault("slug", "").Trim();
        if (!string.IsNullOrEmpty(fmSlug)) slug = fmSlug;

        // 公開日。未指定／不正は最小値（並びは末尾）＆ラベル空。
        DateTime date = DateTime.MinValue;
        string dateLabel = "";
        var rawDate = meta.GetValueOrDefault("date", "").Trim();
        if (DateTime.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = parsed;
            dateLabel = $"{parsed.Year}年{parsed.Month}月{parsed.Day}日";
        }

        // タグ（カンマ区切り）。
        var tags = meta.GetValueOrDefault("tags", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var bodyHtml = Markdown.ToHtml(body, MarkdownPipeline);

        return new Article(slug, title, description, date, dateLabel, tags, bodyHtml);
    }

    private sealed record Article(
        string Slug, string Title, string Description,
        DateTime Date, string DateLabel, IReadOnlyList<string> Tags, string BodyHtml);

    // ── テンプレに渡すモデル ──

    /// <summary>記事詳細テンプレ（article-detail.sbn）に渡すモデル。</summary>
    private sealed class ArticleContentModel
    {
        public string Title { get; set; } = "";
        public string DateLabel { get; set; } = "";
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        /// <summary>Markdig で HTML 化済みの本文。テンプレ側でそのまま（raw）出力する。</summary>
        public string BodyHtml { get; set; } = "";
    }

    /// <summary>一覧テンプレ（articles-index.sbn）に渡すモデル。</summary>
    private sealed class ArticlesIndexModel
    {
        public IReadOnlyList<ArticleSummary> Articles { get; set; } = Array.Empty<ArticleSummary>();
    }

    /// <summary>一覧の 1 件分。</summary>
    private sealed class ArticleSummary
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Url { get; set; } = "";
        public string DateLabel { get; set; } = "";
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    }
}
