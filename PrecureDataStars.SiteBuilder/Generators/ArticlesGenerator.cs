using System.Globalization;
using Markdig;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// <c>content/articles/*.md</c>（Markdown ＋ front-matter）からコラムを生成するジェネレータ。
/// DB に依存せず、ビルド出力へコピーされた Markdown を読み、<c>/articles/{slug}/</c> と
/// 一覧 <c>/articles/</c> を出力する。本文は Markdig で HTML 化し、既存レイアウト
/// （ヘッダ／フッタ／ナビ／シェア／AdSense）に流し込む。記事は運営情報ページと違い
/// シェア対象なので <see cref="LayoutModel.SuppressShareButtons"/> は立てない。
/// <para>
/// 本番モードでは、下書き（front-matter の <c>draft: true</c>）と、公開日時（<c>date</c>）がビルド時点より
/// 後の原稿を出さない。公開日時を迎えた後の本番ビルドで初めて出る（土曜 8:30 の定時ビルドがこれを拾う）。
/// テストモードでは、原稿を書いている途中に読めるよう、下書きも公開前の原稿も出す。
/// </para>
/// <para>
/// 原稿と同じ名前のフォルダ（<c>articles/{原稿のファイル名}/</c>）に置いた図などの素材は、出すコラムについてだけ
/// 記事のページと同じ場所（<c>/articles/{slug}/</c>）へ写す。原稿からは相対パス（<c>![説明](size-chart.svg)</c>）で参照する。
/// 名前が「.」で始まるファイル・フォルダと、記事のページそのものと同名の <c>index.html</c> は写さない。
/// </para>
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
            _ctx.Logger.Warn("ArticlesContentDir が未設定のため、コラムは 0 件として継続します。");
        }
        else if (!Directory.Exists(dir))
        {
            _ctx.Logger.Warn($"記事ディレクトリが見つかりません: {dir}（記事 0 件として継続）");
        }
        else
        {
            // 公開日時の判定はビルド開始時点の 1 回の現在時刻で行い、ページごとに判定がずれないようにする。
            var now = DateTimeOffset.Now;
            int held = 0;
            foreach (var path in Directory.EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly))
            {
                var article = ParseArticle(path);
                if (article is null) continue;

                // 本番では下書きと公開日時前の原稿を出さない（テストでは出す）。
                if (_ctx.Config.IsProductionMode && (article.IsDraft || article.PublishAt > now))
                {
                    held++;
                    continue;
                }
                articles.Add(article);
            }
            if (held > 0)
                _ctx.Logger.Info($"下書き・公開日時前のコラム {held} 本は本番に出しません。");
        }

        // 公開日降順（同日はタイトル昇順）で並べる。
        articles.Sort((a, b) =>
        {
            int cmp = b.Date.CompareTo(a.Date);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Title, b.Title);
        });

        // 各記事詳細。
        for (int i = 0; i < articles.Count; i++)
        {
            var a = articles[i];
            var content = new ArticleContentModel
            {
                Title = a.Title,
                // 記事メタは読み込み元で欠けている可能性があるため、表示側の非 null 前提に合わせて丸める。
                DateLabel = a.DateLabel ?? "",
                DateIso = a.DateIso,
                Tags = a.Tags ?? Array.Empty<string>(),
                BodyHtml = a.BodyHtml,
                HasTweet = a.BodyHtml.Contains("twitter-tweet", StringComparison.Ordinal),
                // 並びは新しい順なので、1 つ後ろが古い記事（前の記事）、1 つ前が新しい記事（次の記事）。
                Prev = i + 1 < articles.Count ? ToLink(articles[i + 1]) : null,
                Next = i > 0 ? ToLink(articles[i - 1]) : null,
            };
            var layout = new LayoutModel
            {
                PageTitle = a.Title,
                MetaDescription = a.Description,
                // 記事は本文の量を数で語れないので、種別と日付・タグで識別できるようにする。
                OgCard = new OgCardSpec(Kicker: "コラム", Title: a.Title)
                {
                    KickerRight = a.DateLabel ?? "",
                    Facts = (a.Tags ?? Array.Empty<string>())
                        .Take(3)
                        .Select(t => new OgCardFactLine("", t))
                        .ToArray()
                },
                Breadcrumbs = new[]
                {
                    new BreadcrumbItem { Label = "ホーム", Url = "/" },
                    new BreadcrumbItem { Label = "コラム", Url = "/articles/" },
                    new BreadcrumbItem { Label = a.Title, Url = "" },
                },
            };
            _page.RenderAndWrite($"/articles/{a.Slug}/", "articles", "article-detail.sbn", content, layout);
            CopyAssets(a);
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
                DateIso = a.DateIso,
                Tags = a.Tags,
            }).ToList(),
        };
        var indexLayout = new LayoutModel
        {
            PageTitle = "コラム",
            OgCard = new OgCardSpec(Kicker: "", Title: "コラム")
            {
                Badges = new[] { new OgCardBadge("記事", $"{articles.Count}本") }
            },
            MetaDescription =
                "プリキュアのコラム。precure.tv を運営する祥太が、歴代プリキュアを多種多様なアプローチで読み解きます。" +
                "毎週土曜朝 8:30 更新。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "コラム", Url = "" },
            },
        };
        _page.RenderAndWrite("/articles/", "articles", "articles-index.sbn", indexModel, indexLayout);

        _ctx.Logger.Success($"/articles/（{articles.Count} 記事）");
    }

    /// <summary>
    /// 原稿と同じ名前のフォルダの中身（図などの素材）を、記事のページと同じ場所へ写す。フォルダが無ければ何もしない。
    /// </summary>
    private void CopyAssets(Article a)
    {
        if (!Directory.Exists(a.AssetDir)) return;
        var pageUrl = $"/articles/{a.Slug}/";
        int copied = 0;
        foreach (var file in Directory.EnumerateFiles(a.AssetDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(a.AssetDir, file);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p.StartsWith('.'))) continue;
            if (string.Equals(rel, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                _ctx.Logger.Warn($"コラムの素材に index.html があります（記事のページと重なるので写しません）: {a.Slug}");
                continue;
            }
            _page.CopyPageAsset(pageUrl, file, rel);
            copied++;
        }
        if (copied > 0) _ctx.Logger.Info($"コラム {a.Slug} の素材 {copied} 件を写しました。");
    }

    /// <summary>前後の記事カードに出す 1 件分を作る。</summary>
    private static ArticleLink ToLink(Article a) => new() { Title = a.Title, Url = $"/articles/{a.Slug}/" };

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

        // 公開日時。"2026-10-17T08:30:00+09:00" のように時刻・時差つきで書く（日付だけならその日の 0:00）。
        // 時差を省いたときはビルドする PC の時刻（日本時間）として読む。表示の日付も日本時間にそろえる。
        // 未指定／不正は最小値（並びは末尾・公開日時の判定では公開済み扱い）＆ラベル空。
        DateTimeOffset publishAt = DateTimeOffset.MinValue;
        DateTime date = DateTime.MinValue;
        string dateLabel = "";
        string dateIso = "";
        var rawDate = meta.GetValueOrDefault("date", "").Trim();
        if (DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            publishAt = parsed;
            date = parsed.LocalDateTime;
            dateLabel = $"{date.Year}年{date.Month}月{date.Day}日";
            dateIso = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrEmpty(rawDate))
        {
            _ctx.Logger.Warn($"コラムの date を読めません（{rawDate}）: {Path.GetFileName(path)}");
        }

        // 下書き。"draft: true" のときだけ下書き扱い（本番に出さない）。
        var isDraft = string.Equals(meta.GetValueOrDefault("draft", "").Trim(), "true", StringComparison.OrdinalIgnoreCase);

        // タグ（カンマ区切り）。
        var tags = meta.GetValueOrDefault("tags", "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var bodyHtml = Markdown.ToHtml(body, MarkdownPipeline);

        // 素材のフォルダは原稿のファイル名に合わせる（front-matter で slug を変えても、置き場は原稿の隣のまま）。
        var assetDir = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path));

        return new Article(slug, title, description, date, dateLabel, dateIso, tags, bodyHtml, isDraft, publishAt, assetDir);
    }

    /// <summary>原稿 1 本。<paramref name="IsDraft"/> と <paramref name="PublishAt"/> は本番に出すかどうかの判定に使う。
    /// <paramref name="DateIso"/> は time 要素の datetime 属性に入れる "yyyy-MM-dd"（日付が無ければ空）。
    /// <paramref name="AssetDir"/> は図などの素材を置くフォルダ（原稿と同じ名前。無くてもよい）。</summary>
    private sealed record Article(
        string Slug, string Title, string Description,
        DateTime Date, string DateLabel, string DateIso, IReadOnlyList<string> Tags, string BodyHtml,
        bool IsDraft, DateTimeOffset PublishAt, string AssetDir);

    // ── テンプレに渡すモデル ──

    /// <summary>記事詳細テンプレ（article-detail.sbn）に渡すモデル。</summary>
    private sealed class ArticleContentModel
    {
        public string Title { get; set; } = "";
        public string DateLabel { get; set; } = "";
        /// <summary>time 要素の datetime 属性（"yyyy-MM-dd"、日付が無ければ空）。</summary>
        public string DateIso { get; set; } = "";
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        /// <summary>Markdig で HTML 化済みの本文。テンプレ側でそのまま（raw）出力する。</summary>
        public string BodyHtml { get; set; } = "";
        /// <summary>本文に X の埋め込み（blockquote.twitter-tweet）があるか。あれば X の読み込みスクリプトを置く。</summary>
        public bool HasTweet { get; set; }
        /// <summary>1 つ古い記事（「← 前の記事」）。最も古い記事では null。</summary>
        public ArticleLink? Prev { get; set; }
        /// <summary>1 つ新しい記事（「次の記事 →」）。最新の記事では null。</summary>
        public ArticleLink? Next { get; set; }
    }

    /// <summary>前後の記事カードの 1 件分。</summary>
    private sealed class ArticleLink
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
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
        /// <summary>time 要素の datetime 属性（"yyyy-MM-dd"、日付が無ければ空）。</summary>
        public string DateIso { get; set; } = "";
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
    }
}
