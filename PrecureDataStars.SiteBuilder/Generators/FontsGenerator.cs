using System.Globalization;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// フォントのページ（<c>/fonts/</c>）と書体名ごとのページ（<c>/fonts/{書体名}/</c>）を生成する。
/// 材料は作品で使われた書体のマスタ（<see cref="BuildContext.FontById"/>）と、作品のどこにどの書体を使ったか
/// （<see cref="BuildContext.FontUsages"/>）。DB には触れない。
/// <list type="bullet">
///   <item><c>/fonts/</c>：作品ごと（放送開始順）に、本編に流れる順（OP・サブタイトル・ED の時期ごと）の場面の見本と、その下に使われた書体（リンク）を並べる。
///     見本を押すと大きな画像を重ねて出す。</item>
///   <item><c>/fonts/{書体名}/</c>：ウェイトごとの節に、書体見本・外部リンク・その書体が使われた場面の見本を並べる。同じ作品の場面で
///     装飾の見た目がほとんど同じものは 1 つにまとめる。</item>
/// </list>
/// 本放送だけの行は、装飾の値がそろうまで出さない。ルビの書体は親字と違うときだけ添える。
/// </summary>
public sealed class FontsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    public FontsGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating fonts");

        var usages = _ctx.FontUsages
            .Where(u => !u.IsBroadcastOnly && _ctx.SeriesById.ContainsKey(u.SeriesId) && _ctx.FontById.ContainsKey(u.FontId))
            .ToList();
        if (usages.Count == 0)
        {
            _ctx.Logger.Warn("series_font_usages に行がありません。フォントのページは作りません。");
            return;
        }

        var seriesList = usages.Select(u => _ctx.SeriesById[u.SeriesId]).DistinctBy(s => s.SeriesId)
            .OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId).ToList();
        var sections = seriesList.Select(BuildSeriesSection).ToList();

        GenerateIndex(sections);
        var families = usages.Select(u => _ctx.FontById[u.FontId]).DistinctBy(f => f.FontId)
            .GroupBy(f => f.PageFamilyName, StringComparer.Ordinal).ToList();
        foreach (var family in families)
            GenerateFamily(family.OrderBy(f => WeightOrder(f.Weight)).ThenBy(f => FamilyWeightOrder(f.FamilyName)).ToList(), sections);

        _ctx.Logger.Success($"fonts: {1 + families.Count} ページ（作品 {sections.Count}・書体名 {families.Count}）");
    }

    // ════════════════════ /fonts/ ════════════════════

    private void GenerateIndex(IReadOnlyList<FontSeriesSection> sections)
    {
        var content = new FontsIndexModel { SeriesSections = sections.Where(s => s.Cards.Count > 0).ToList() };
        var layout = new LayoutModel
        {
            PageTitle = "歴代タイトル字幕フォント",
            OgCard = new OgCardSpec(Kicker: "", Title: "歴代タイトル字幕フォント")
            {
                Subtitle = "OP・ED のクレジットと歌詞、サブタイトルに使われた書体を、作品ごとに見本つきでまとめました。",
                Badges = new[] { new OgCardBadge("作品", $"{content.SeriesSections.Count}") }
            },
            MetaDescription = $"歴代プリキュアのタイトル字幕フォント一覧。OP・ED のクレジットと歌詞、サブタイトルに使われた書体を、{content.SeriesSections.Count} 作品ごとに放送の画面を模した見本つきでまとめました。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "フォント", Url = "" }
            }
        };
        _page.RenderAndWrite("/fonts/", "fonts", "fonts-index.sbn", content, layout);
    }

    /// <summary>作品 1 つの場面の見本（本編に流れる順：OP → サブタイトル → ED の時期の順）。</summary>
    private FontSeriesSection BuildSeriesSection(Series series)
    {
        var cards = new List<FontSceneCard>();
        var scenes = FontSampleRequests.ForSeries(series, _ctx).ToList();
        int edCount = scenes.Count(s => s.Kind == "ED");
        foreach (var (kind, period, request) in scenes)
        {
            var image = _page.RenderSubtitleTelop(request);
            var card = new FontSceneCard
            {
                SeriesId = series.SeriesId,
                Kind = kind,
                Period = period,
                Label = kind switch
                {
                    "OP" => "オープニング",
                    "ED" when edCount >= 2 => period switch { 1 => "エンディング（前期）", 2 when edCount == 2 => "エンディング（後期）", _ => $"エンディング（第 {period} 期）" },
                    "ED" => "エンディング",
                    _ => "サブタイトル"
                },
                ImageSrc = image?.Src ?? "",
                ImageWidth = image?.Width ?? 0,
                ImageHeight = image?.Height ?? 0
            };
            card.ImageAlt = $"{series.Title}の{card.Label}の書体の見本";
            foreach (var (place, baseKind, rubyKind) in Places(kind))
            {
                var usage = FontSampleRequests.Usage(_ctx, series.SeriesId, baseKind, period);
                if (usage is null) continue;
                var font = _ctx.FontById[usage.FontId];
                var line = new FontLine { Place = place, Usage = usage, Font = font };
                if (rubyKind is not null && FontSampleRequests.Usage(_ctx, series.SeriesId, rubyKind, period) is { } ruby
                    && ruby.FontId != usage.FontId && _ctx.FontById.TryGetValue(ruby.FontId, out var rubyFont))
                    line.RubyFont = ToChip(rubyFont, ruby);
                line.Chip = ToChip(font, usage);
                card.Lines.Add(line);
            }
            if (card.Lines.Count > 0) cards.Add(card);
        }
        return new FontSeriesSection
        {
            SeriesId = series.SeriesId,
            SeriesTitle = series.Title,
            SeriesUrl = PathUtil.SeriesUrl(series.Slug),
            Anchor = $"s-{series.Slug}",
            StartYearLabel = series.StartDate.Year.ToString(CultureInfo.InvariantCulture),
            Cards = cards
        };
    }

    /// <summary>場面ごとの書体の並び（場所の名前、親字の場所、ルビの場所）。</summary>
    private static IEnumerable<(string Place, string Kind, string? RubyKind)> Places(string sceneKind) => sceneKind switch
    {
        "OP" => new[] { ("クレジット", FontUsageKinds.OpCredit, (string?)null), ("歌詞", FontUsageKinds.OpLyrics, FontUsageKinds.OpLyricsRuby) },
        "ED" => new[] { ("クレジット", FontUsageKinds.EdCredit, (string?)null), ("歌詞", FontUsageKinds.EdLyrics, FontUsageKinds.EdLyricsRuby) },
        _ => new[] { ("サブタイトル", FontUsageKinds.Subtitle, (string?)FontUsageKinds.SubtitleRuby) }
    };

    private static FontChip ToChip(FontFace font, SeriesFontUsage usage) => new()
    {
        Name = font.DisplayName,
        Url = FontSampleRequests.FontUrl(font),
        Vendor = VendorLabel(font),
        WeightUnknown = usage.IsWeightUnknown,
        ModificationNote = usage.ModificationNote ?? "",
        SubstituteNote = SubstituteNote(font, usage.IsWeightUnknown)
    };

    // ════════════════════ /fonts/{書体名}/ ════════════════════

    private void GenerateFamily(IReadOnlyList<FontFace> weights, IReadOnlyList<FontSeriesSection> sections)
    {
        var first = weights[0];
        string url = FontSampleRequests.FamilyUrl(first);
        var weightSections = weights.Select(font => BuildWeightSection(font, sections)).ToList();
        string foundry = FontFoundries.Label(first.Foundry);
        int seriesCount = weightSections.SelectMany(w => w.Scenes).SelectMany(s => s.SeriesIds).Distinct().Count();
        var content = new FontFamilyModel
        {
            Name = first.PageFamilyName,
            FoundryLabel = foundry,
            Weights = weightSections,
            SeriesCount = seriesCount
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{first.PageFamilyName}（フォント）",
            OgCard = new OgCardSpec(Kicker: "フォント", Title: first.PageFamilyName)
            {
                Subtitle = $"{foundry}の書体。歴代プリキュアで使われた作品と場所を、見本つきでまとめました。",
                Badges = new[] { new OgCardBadge("作品", $"{seriesCount}") }
            },
            MetaDescription = $"{first.PageFamilyName}（{foundry}）が使われた歴代プリキュアの作品と場所（OP・ED のクレジットと歌詞、サブタイトル）を、書体見本と放送の画面を模した見本つきでまとめました。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "フォント", Url = "/fonts/" },
                new BreadcrumbItem { Label = first.PageFamilyName, Url = "" }
            }
        };
        _page.RenderAndWrite(url, "fonts", "fonts-detail.sbn", content, layout);
    }

    /// <summary>
    /// ウェイト 1 つの節。書体見本・外部リンクと、その書体が使われた場面の見本。同じ作品の場面のうち、この書体を使う場所が同じで
    /// 装飾の見た目がほとんど同じもの（<see cref="TelopDecoration.LooksLike"/>）は、最初の場面の見本 1 つにまとめる。
    /// </summary>
    private FontWeightSection BuildWeightSection(FontFace font, IReadOnlyList<FontSeriesSection> sections)
    {
        var specimen = FontSampleRequests.Specimen(font) is { } request ? _page.RenderSubtitleTelop(request) : null;
        var scenes = new List<FontWeightScene>();
        foreach (var section in sections)
        {
            var merged = new List<(FontWeightScene Scene, List<(string Place, TelopDecoration Decoration)> Key)>();
            foreach (var card in section.Cards)
            {
                var lines = card.Lines.Where(l => l.Font.FontId == font.FontId || l.RubyFont is not null && l.RubyFont.Url == FontSampleRequests.FontUrl(font)).ToList();
                if (lines.Count == 0) continue;
                var key = lines.Select(l => (Place: l.Place, Decoration: TelopDecoration.Parse(l.Usage.Decoration) ?? TelopDecoration.Default)).ToList();
                var same = merged.FirstOrDefault(m => m.Key.Count == key.Count
                    && m.Key.Zip(key).All(p => p.First.Place == p.Second.Place && p.First.Decoration.LooksLike(p.Second.Decoration)));
                string place = string.Join("・", lines.Select(l => l.RubyFont is not null && l.Font.FontId != font.FontId ? $"{l.Place}のルビ" : l.Place));
                if (same.Scene is not null)
                {
                    same.Scene.Labels.Add(card.Label);
                    continue;
                }
                var scene = new FontWeightScene
                {
                    SeriesTitle = section.SeriesTitle,
                    SeriesUrl = section.SeriesUrl,
                    StartYearLabel = section.StartYearLabel,
                    IndexUrl = $"/fonts/#{section.Anchor}",
                    Place = place,
                    ImageSrc = card.ImageSrc,
                    ImageWidth = card.ImageWidth,
                    ImageHeight = card.ImageHeight,
                    ImageAlt = card.ImageAlt,
                    SeriesIds = { section.SeriesId }
                };
                scene.Labels.Add(card.Label);
                merged.Add((scene, key));
                scenes.Add(scene);
            }
        }
        string productLabel = ProductLabel(font.ProductUrl);
        return new FontWeightSection
        {
            Name = font.DisplayName,
            Anchor = FontSampleRequests.WeightAnchor(font),
            SpecimenSrc = specimen?.Src ?? "",
            SpecimenWidth = specimen?.Width ?? 0,
            SpecimenHeight = specimen?.Height ?? 0,
            SubstituteNote = SubstituteNote(font, weightUnknown: false),
            ArchiveUrl = font.ArchiveUrl ?? "",
            ProductUrl = font.ProductUrl ?? "",
            // 見本を代用で描く書体は、製品ページが代用の書体のものなので、どの書体のページかが分かるよう「モリサワ（ゴナ DB）」と書く。
            // 注記のある書体（改刻版で描くもの）は代用ではないので、提供元の名前だけ
            ProductLabel = productLabel == "" ? "" : font.IsRenderSubstitute && string.IsNullOrWhiteSpace(font.RenderNote)
                ? $"{productLabel}（{font.RenderDisplayName}）" : productLabel,
            Scenes = scenes
        };
    }

    // ════════════════════ 共通 ════════════════════

    /// <summary>
    /// 書体のベンダーの名前（「写研・モリサワ」「モリサワ」「フォントワークス」）。写研の書体はいまモリサワが提供しているので両方を並べる。
    /// </summary>
    private static string VendorLabel(FontFace font) => font.Foundry == FontFoundries.Shaken
        ? $"{FontFoundries.Label(FontFoundries.Shaken)}・{FontFoundries.Label(FontFoundries.Morisawa)}"
        : FontFoundries.Label(font.Foundry);

    /// <summary>
    /// 書体名にウェイトの入った写研の書体の並び（細い順）。まとめた書体名のページで、ウェイトの記号を持たない書体を並べるのに使う。
    /// </summary>
    private static int FamilyWeightOrder(string familyName) =>
        familyName.Contains("細") ? 0 : familyName.Contains("中太") ? 2 : familyName.Contains("太") ? 3 : familyName.Contains("中") ? 1 : 4;

    /// <summary>ウェイトの並び（細い順）。</summary>
    private static int WeightOrder(string? weight) => weight switch
    {
        null or "" => 0, "L" => 1, "R" => 2, "M" => 3, "D" => 4, "DB" => 5, "B" => 6, "EB" => 7, "E" => 8, "H" => 9, "U" => 10, "UB" => 11, _ => 12
    };

    /// <summary>
    /// 見本に添える注記。書体に注記（<see cref="FontFace.RenderNote"/>。改刻版で描くときなど）があればそれ、
    /// ウェイトの分からない行は「ウェイト不明。サンプルは「○○」で作成」、放送と別のウェイト・書体名で描くときは
    /// 「サンプルは別ウェイトの「○○」で代用（作品中では「△△」）」。どれでもなければ空文字。
    /// </summary>
    private static string SubstituteNote(FontFace font, bool weightUnknown)
    {
        if (string.IsNullOrWhiteSpace(font.RenderFontName)) return "";
        if (!string.IsNullOrWhiteSpace(font.RenderNote)) return font.RenderNote;
        string renderName = font.RenderDisplayName;
        if (weightUnknown)
            return renderName != font.FamilyName ? $"ウェイト不明。サンプルは「{renderName}」で作成" : "";
        if (!font.IsRenderSubstitute) return "";
        bool sameFamily = string.Equals(font.EffectiveRenderFamilyName, font.FamilyName, StringComparison.Ordinal);
        return sameFamily
            ? $"サンプルは別ウェイトの「{renderName}」で代用（作品中では「{font.DisplayName}」）"
            : $"サンプルは「{renderName}」で代用（作品中では「{font.DisplayName}」）";
    }

    /// <summary>製品ページのリンクの名前（提供元）。</summary>
    private static string ProductLabel(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (url.Contains("morisawa", StringComparison.OrdinalIgnoreCase)) return "モリサワ";
        if (url.Contains("fontworks", StringComparison.OrdinalIgnoreCase)) return "フォントワークス";
        return "製品ページ";
    }

    // ════════════════════ テンプレ用 DTO ════════════════════

    private sealed class FontsIndexModel
    {
        /// <summary>作品ごとの見本（放送開始順）。</summary>
        public List<FontSeriesSection> SeriesSections { get; set; } = new();
    }

    private sealed class FontSeriesSection
    {
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        /// <summary>フォントのページの中の作品の節の id（書体のページからのリンク先）。</summary>
        public string Anchor { get; set; } = "";
        public string StartYearLabel { get; set; } = "";
        public List<FontSceneCard> Cards { get; set; } = new();
    }

    /// <summary>場面の見本 1 枚と、その場面で使われた書体。</summary>
    private sealed class FontSceneCard
    {
        public int SeriesId { get; set; }
        /// <summary>場面の種類（OP / ED / SUBTITLE）。</summary>
        public string Kind { get; set; } = "";
        public int Period { get; set; }
        /// <summary>場面の名前（「オープニング」「エンディング（前期）」「サブタイトル」）。</summary>
        public string Label { get; set; } = "";
        public string ImageSrc { get; set; } = "";
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public string ImageAlt { get; set; } = "";
        public List<FontLine> Lines { get; set; } = new();
    }

    /// <summary>場面の中の場所 1 つ（「クレジット」「歌詞」）の書体。</summary>
    private sealed class FontLine
    {
        public string Place { get; set; } = "";
        public FontChip Chip { get; set; } = new();
        /// <summary>ルビの書体（親字と違うときだけ）。</summary>
        public FontChip? RubyFont { get; set; }
        internal SeriesFontUsage Usage { get; set; } = new();
        internal FontFace Font { get; set; } = new();
    }

    private sealed class FontChip
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>ベンダーの名前（「写研・モリサワ」）。</summary>
        public string Vendor { get; set; } = "";
        public bool WeightUnknown { get; set; }
        public string ModificationNote { get; set; } = "";
        public string SubstituteNote { get; set; } = "";
    }

    private sealed class FontFamilyModel
    {
        public string Name { get; set; } = "";
        public string FoundryLabel { get; set; } = "";
        public int SeriesCount { get; set; }
        public List<FontWeightSection> Weights { get; set; } = new();
    }

    private sealed class FontWeightSection
    {
        public string Name { get; set; } = "";
        public string Anchor { get; set; } = "";
        public string SpecimenSrc { get; set; } = "";
        public int SpecimenWidth { get; set; }
        public int SpecimenHeight { get; set; }
        public string SubstituteNote { get; set; } = "";
        public string ArchiveUrl { get; set; } = "";
        public string ProductUrl { get; set; } = "";
        public string ProductLabel { get; set; } = "";
        public List<FontWeightScene> Scenes { get; set; } = new();
    }

    /// <summary>書体のページの場面の見本 1 つ（見た目のほとんど同じ場面をまとめたもの）。</summary>
    private sealed class FontWeightScene
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string StartYearLabel { get; set; } = "";
        /// <summary>フォントのページのその作品の節。</summary>
        public string IndexUrl { get; set; } = "";
        /// <summary>この書体が使われた場所（「クレジット・歌詞」）。</summary>
        public string Place { get; set; } = "";
        /// <summary>まとめた場面の名前（「オープニング」「エンディング（前期）」）。</summary>
        public List<string> Labels { get; set; } = new();
        public string ImageSrc { get; set; } = "";
        public int ImageWidth { get; set; }
        public int ImageHeight { get; set; }
        public string ImageAlt { get; set; } = "";
        internal HashSet<int> SeriesIds { get; } = new();
    }
}
