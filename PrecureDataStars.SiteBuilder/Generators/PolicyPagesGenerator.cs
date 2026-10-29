using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>法律・運営情報系の補助ページを生成するジェネレータ。
/// プライバシーポリシーの文面は常に本番運用状態（GA4 + AdSense 有効）を前提とした固定記述で、
/// ローカルテスト時の ID 未設定（タグ出力オフ）には文面を追従させない。</summary>
public sealed class PolicyPagesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public PolicyPagesGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating policy pages");

        GeneratePrivacy();
        GenerateDisclaimer();
        GenerateContact();
        GenerateSources();

        _ctx.Logger.Success("/privacy/, /disclaimer/, /contact/, /about/sources/");
    }

    /// <summary><c>/about/sources/</c> — データの出典と収録の決まり。データの種別ごとに、どこから採ったかを説明する。</summary>
    private void GenerateSources()
    {
        var content = new SourcesContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "データの出典",
            MetaDescription = $"{_ctx.Config.SiteName} に収録しているデータの出どころと、収録の決まりです。エピソード・クレジット・尺・楽曲・商品・書籍・人物・キャラクターの各データについて説明しています。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "このサイトについて", Url = "/about/" },
                new BreadcrumbItem { Label = "データの出典", Url = "" }
            }
        };
        _page.RenderAndWrite("/about/sources/", "policy", "sources.sbn", content, layout);
    }

    /// <summary>/privacy/ — プライバシーポリシー。</summary>
    private void GeneratePrivacy()
    {
        var content = new PrivacyContentModel
        {
            SiteName = _ctx.Config.SiteName,
        };
        var layout = new LayoutModel
        {
            PageTitle = "プライバシーポリシー",
            MetaDescription = $"{_ctx.Config.SiteName} のプライバシーポリシーです。Cookie とブラウザへの保存、アクセス解析、広告配信、外部への情報送信、お問い合わせで受け取る情報の取り扱いを説明しています。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "プライバシーポリシー", Url = "" }
            }
        };
        _page.RenderAndWrite("/privacy/", "policy", "privacy.sbn", content, layout);
    }

    /// <summary><c>/disclaimer/</c> — 免責事項。</summary>
    private void GenerateDisclaimer()
    {
        var content = new DisclaimerContentModel
        {
            SiteName = _ctx.Config.SiteName,
            SubtitleFontGroups = BuildSubtitleFontGroups(),
        };
        var layout = new LayoutModel
        {
            PageTitle = "免責事項",
            MetaDescription = $"{_ctx.Config.SiteName} の免責事項です。情報の正確性、著作権と商標、サブタイトル画像とフォント、当サイトの内容の利用、Amazon アソシエイト、準拠法と管轄を説明しています。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "免責事項", Url = "" }
            }
        };
        _page.RenderAndWrite("/disclaimer/", "policy", "disclaimer.sbn", content, layout);
    }

    // ════════════════════ 使用フォントの一覧（免責事項「サブタイトル画像と書体の見本について」） ════════════════════

    /// <summary>
    /// サブタイトルのテロップ画像と書体の見本の画像を描くのに使っている手元のフォントを、ライセンス
    /// （フォントワークス LETS / Morisawa Fonts）ごとにまとめる。作品の書体の表（<see cref="BuildContext.FontUsages"/>）の
    /// 各行が指す書体（<see cref="BuildContext.FontById"/>）のうち、描く書体（<c>render_font_name</c>）のあるものを拾い、
    /// 描く書体ごとに 1 行にする（放送の書体を別のウェイトや改刻版で描いているときは、描く書体として数える）。
    /// フォントの並びは最初に使った作品の順（放送開始の早い順）、作品の並びも放送開始順で、作品には使っている場所を添える。
    /// ライセンスの無い書体は末尾の「その他」へ回して警告を出す。空のグループは出さない。
    /// </summary>
    private List<SubtitleFontGroupRow> BuildSubtitleFontGroups()
    {
        // 描く書体名 → (書体, 作品 ID → 使う場所)。作品は放送開始順に回す。
        var byRenderName = new Dictionary<string, (FontFace Font, Dictionary<int, SortedSet<int>> KindsBySeries)>(StringComparer.Ordinal);
        var fontOrder = new List<string>();
        var usagesBySeries = _ctx.FontUsages.ToLookup(u => u.SeriesId);
        foreach (var series in _ctx.Series)
        {
            foreach (var usage in usagesBySeries[series.SeriesId])
            {
                if (!_ctx.FontById.TryGetValue(usage.FontId, out var font) || string.IsNullOrWhiteSpace(font.RenderFontName)) continue;
                if (!byRenderName.TryGetValue(font.RenderFontName, out var entry))
                {
                    entry = (font, new Dictionary<int, SortedSet<int>>());
                    byRenderName[font.RenderFontName] = entry;
                    fontOrder.Add(font.RenderFontName);
                }
                if (!entry.KindsBySeries.TryGetValue(series.SeriesId, out var kinds))
                {
                    kinds = new SortedSet<int>();
                    entry.KindsBySeries[series.SeriesId] = kinds;
                }
                kinds.Add(FontUsageKinds.OrderOf(usage.UsageKind));
            }
        }

        // グループはライセンスごとに固定の順。どちらにも当たらない書体は末尾の「その他」へ。
        var groups = new List<SubtitleFontGroupRow>
        {
            new() { LicenseKind = FontLicenseKinds.FontworksLets, LicenseLabel = "フォントワークス LETS", LicenseUrl = "https://lets.fontworks.co.jp/" },
            new() { LicenseKind = FontLicenseKinds.MorisawaFonts, LicenseLabel = "Morisawa Fonts", LicenseUrl = "https://morisawafonts.com/" },
            new() { LicenseKind = "", LicenseLabel = "その他", LicenseUrl = "" },
        };

        foreach (var renderName in fontOrder)
        {
            var (font, kindsBySeries) = byRenderName[renderName];
            if (string.IsNullOrEmpty(font.LicenseKind))
                _ctx.Logger.Warn($"fonts のライセンスが空の書体: {renderName}（免責事項の使用フォント一覧では「その他」に出す）");

            var group = groups.FirstOrDefault(g => g.LicenseKind == (font.LicenseKind ?? "")) ?? groups[^1];
            group.Fonts.Add(new SubtitleFontRow
            {
                FontName = renderName,
                DisplayName = font.RenderDisplayName,
                ProductUrl = font.ProductUrl ?? "",
                Usages = kindsBySeries
                    .Select(kv => _ctx.SeriesById[kv.Key])
                    .OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId)
                    .Select(s => new SubtitleFontUsageRow
                    {
                        SeriesTitle = s.Title,
                        SeriesUrl = $"/series/{s.Slug}/",
                        SeriesStartYearLabel = s.StartDate.Year.ToString(),
                        UsageNote = string.Join("・", kindsBySeries[s.SeriesId].Select(i => FontUsageKinds.Label(FontUsageKinds.Ordered[i]))),
                    })
                    .ToList(),
            });
        }

        return groups.Where(g => g.Fonts.Count > 0).ToList();
    }

    /// <summary><c>/contact/</c> — お問い合わせページ。</summary>
    private void GenerateContact()
    {
        var content = new ContactContentModel { SiteName = _ctx.Config.SiteName };
        var layout = new LayoutModel
        {
            PageTitle = "お問い合わせ",
            MetaDescription = $"{_ctx.Config.SiteName} へのお問い合わせページです。誤りのご指摘、ご意見、ご本人からの訂正、引用や取材のご依頼はこちらからお寄せください。",
            // 運営情報系ページはシェアされる性質のものではないため、シェアボタンを出さない。
            SuppressShareButtons = true,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "お問い合わせ", Url = "" }
            }
        };
        _page.RenderAndWrite("/contact/", "policy", "contact.sbn", content, layout);
    }

    /// <summary>プライバシーポリシーページに渡すコンテンツモデル。</summary>
    private sealed class PrivacyContentModel
    {
        /// <summary>サイト名（テンプレ本文中に複数回出現するため毎ページ渡す）。</summary>
        public string SiteName { get; set; } = "";
    }

    /// <summary>免責事項ページに渡すコンテンツモデル。</summary>
    private sealed class DisclaimerContentModel
    {
        public string SiteName { get; set; } = "";

        /// <summary>「サブタイトル画像と書体の見本について」の使用フォント一覧（ライセンスごとのグループ。空なら折りたたみ自体を出さない）。</summary>
        public List<SubtitleFontGroupRow> SubtitleFontGroups { get; set; } = new();
    }

    /// <summary>免責事項「サブタイトル画像と書体の見本について」の使用フォント一覧の 1 グループ（ライセンスごと）。</summary>
    private sealed class SubtitleFontGroupRow
    {
        /// <summary>ライセンス区分のコード（<see cref="FontLicenseKinds"/>）。「その他」は空文字。グループの振り分けにだけ使う。</summary>
        public string LicenseKind { get; set; } = "";

        /// <summary>見出し（「フォントワークス LETS」「Morisawa Fonts」）。</summary>
        public string LicenseLabel { get; set; } = "";

        /// <summary>ライセンスの公式サイト。空なら見出しをリンクにしない。</summary>
        public string LicenseUrl { get; set; } = "";

        /// <summary>このライセンスのフォント（最初に使った作品の順）。</summary>
        public List<SubtitleFontRow> Fonts { get; set; } = new();
    }

    /// <summary>使用フォント一覧の 1 行（1 フォント）。</summary>
    private sealed class SubtitleFontRow
    {
        /// <summary>見せる名前（描く書体の書体名＋ウェイト。「ハミング B」など）。</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Windows の書体名（「FOT-ハミング ProN B」）。<see cref="DisplayName"/> と違うときだけ薄く添える。</summary>
        public string FontName { get; set; } = "";

        /// <summary>提供元の製品ページ。空ならリンクにしない。</summary>
        public string ProductUrl { get; set; } = "";

        /// <summary>このフォントを使っている作品（放送開始順）。</summary>
        public List<SubtitleFontUsageRow> Usages { get; set; } = new();
    }

    /// <summary>使用フォント一覧で、1 フォントを使っている 1 作品。</summary>
    private sealed class SubtitleFontUsageRow
    {
        /// <summary>作品のフルタイトル。</summary>
        public string SeriesTitle { get; set; } = "";

        /// <summary>作品詳細の URL（<c>/series/{slug}/</c>）。</summary>
        public string SeriesUrl { get; set; } = "";

        /// <summary>放送開始年（複数作品が並ぶので薄く添える）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";

        /// <summary>その作品でこのフォントを使っている場所（「サブタイトル・ED 歌詞」）。</summary>
        public string UsageNote { get; set; } = "";
    }

    /// <summary>お問い合わせページに渡すコンテンツモデル。</summary>
    private sealed class ContactContentModel
    {
        public string SiteName { get; set; } = "";
    }

    /// <summary>データの出典ページに渡すコンテンツモデル。</summary>
    private sealed class SourcesContentModel
    {
        public string SiteName { get; set; } = "";
    }
}
