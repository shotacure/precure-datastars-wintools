using System.Globalization;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// フォントのページ（<c>/fonts/</c>）と書体ごとのページ（<c>/fonts/{書体}/</c>）を生成する。
/// 材料は作品で使われた書体のマスタ（<see cref="BuildContext.FontById"/>）と、作品のどこにどの書体を使ったか
/// （<see cref="BuildContext.FontUsages"/>）。DB には触れない。
/// <list type="bullet">
///   <item><c>/fonts/</c>：作り手（写研・モリサワ・フォントワークス）ごとの書体の一覧と、作品ごと（放送開始順）の
///     使う場所 ｜ 書体 ｜ 見本の表。</item>
///   <item><c>/fonts/{書体}/</c>：作り手・外部リンク（写研アーカイブ・提供元の製品ページ）と、その書体を使った作品・場所 ｜ 見本の表。</item>
/// </list>
/// 見本の画像は <see cref="FontSampleRequests"/> の材料をテロップ画像と同じ作り置きで描く。見本を放送と別のウェイトで描く書体は、
/// その旨を見本に添える。
/// </summary>
public sealed class FontsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    /// <summary>作り手の並び（書体の一覧の見出しの順）。</summary>
    private static readonly string[] FoundryOrder = { FontFoundries.Shaken, FontFoundries.Morisawa, FontFoundries.Fontworks };

    public FontsGenerator(BuildContext ctx, PageRenderer page)
    {
        _ctx = ctx;
        _page = page;
    }

    public void Generate()
    {
        _ctx.Logger.Section("Generating fonts");

        // 作品の放送開始順 → 使う場所の順 → 時期 → 本放送だけの行は後 → 最初の話の順に並べた、表に出す行。
        var usages = _ctx.FontUsages
            .Where(u => _ctx.SeriesById.ContainsKey(u.SeriesId) && _ctx.FontById.ContainsKey(u.FontId))
            .OrderBy(u => _ctx.SeriesById[u.SeriesId].StartDate)
            .ThenBy(u => u.SeriesId)
            .ThenBy(u => FontUsageKinds.OrderOf(u.UsageKind))
            .ThenBy(u => u.PeriodNo)
            .ThenBy(u => u.IsBroadcastOnly)
            .ThenBy(u => u.FirstEpisodeNo ?? 0)
            .ThenBy(u => u.UsageId)
            .ToList();
        if (usages.Count == 0)
        {
            _ctx.Logger.Warn("series_font_usages に行がありません。フォントのページは作りません。");
            return;
        }

        // 見本の画像は 1 行 1 枚。テロップ画像と同じ作り置きから写す（ピンポイントビルドの対象外は null）。
        var rows = usages.Select(BuildUsageRow).ToList();
        var usedFonts = usages.Select(u => _ctx.FontById[u.FontId]).DistinctBy(f => f.FontId).ToList();

        GenerateIndex(rows, usedFonts);
        foreach (var font in usedFonts)
            GenerateDetail(font, rows.Where(r => r.FontId == font.FontId).ToList());

        _ctx.Logger.Success($"fonts: {1 + usedFonts.Count} ページ（書体 {usedFonts.Count}・行 {rows.Count}）");
    }

    // ════════════════════ /fonts/ ════════════════════

    private void GenerateIndex(IReadOnlyList<FontUsageRow> rows, IReadOnlyList<FontFace> usedFonts)
    {
        var seriesCountByFont = rows.GroupBy(r => r.FontId).ToDictionary(g => g.Key, g => g.Select(r => r.SeriesId).Distinct().Count());
        var foundryGroups = FoundryOrder
            .Select(code => new FontFoundryGroup
            {
                Label = FontFoundries.Label(code),
                Fonts = usedFonts
                    .Where(f => f.Foundry == code)
                    .OrderBy(f => f.FamilyName, StringComparer.Ordinal).ThenBy(f => f.Weight ?? "", StringComparer.Ordinal)
                    .Select(f => new FontChip { Name = f.DisplayName, Url = FontSampleRequests.FontUrl(f), SeriesCount = seriesCountByFont[f.FontId] })
                    .ToList()
            })
            .Where(g => g.Fonts.Count > 0)
            .ToList();

        var seriesSections = rows
            .GroupBy(r => r.SeriesId)
            .Select(g =>
            {
                var series = _ctx.SeriesById[g.Key];
                return new FontSeriesSection
                {
                    SeriesTitle = series.Title,
                    SeriesUrl = PathUtil.SeriesUrl(series.Slug),
                    StartYearLabel = series.StartDate.Year.ToString(CultureInfo.InvariantCulture),
                    Rows = g.ToList(),
                    HasSize = g.Any(r => r.SizeLabel != "")
                };
            })
            .ToList();

        var content = new FontsIndexModel
        {
            FoundryGroups = foundryGroups,
            SeriesSections = seriesSections,
            FontCount = usedFonts.Count,
            SeriesCount = seriesSections.Count,
            HasEdCreditSample = rows.Any(r => r.IsEdCreditSample)
        };
        var layout = new LayoutModel
        {
            PageTitle = "フォント",
            OgCard = new OgCardSpec(Kicker: "", Title: "歴代プリキュアのフォント")
            {
                Subtitle = "OP・ED のクレジットと歌詞、サブタイトルに使われた書体を、作品ごとに見本つきでまとめました。",
                Badges = new[]
                {
                    new OgCardBadge("書体", $"{usedFonts.Count}"),
                    new OgCardBadge("作品", $"{seriesSections.Count}")
                }
            },
            MetaDescription = $"歴代プリキュアのフォント一覧。OP・ED のクレジットと歌詞、サブタイトルに使われた書体 {usedFonts.Count} 種を、{seriesSections.Count} 作品ごとに見本つきでまとめました。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "フォント", Url = "" }
            }
        };
        _page.RenderAndWrite("/fonts/", "fonts", "fonts-index.sbn", content, layout);
    }

    // ════════════════════ /fonts/{書体}/ ════════════════════

    private void GenerateDetail(FontFace font, IReadOnlyList<FontUsageRow> rows)
    {
        string url = FontSampleRequests.FontUrl(font);
        string productLabel = ProductLabel(font.ProductUrl);
        var content = new FontDetailModel
        {
            Name = font.DisplayName,
            FoundryLabel = FontFoundries.Label(font.Foundry),
            ArchiveUrl = font.ArchiveUrl ?? "",
            ProductUrl = font.ProductUrl ?? "",
            ProductLabel = productLabel,
            HasSample = !string.IsNullOrWhiteSpace(font.RenderFontName),
            SubstituteNote = SubstituteNote(font, weightUnknown: false),
            Rows = rows.ToList(),
            SeriesCount = rows.Select(r => r.SeriesId).Distinct().Count(),
            HasSize = rows.Any(r => r.SizeLabel != ""),
            HasEdCreditSample = rows.Any(r => r.IsEdCreditSample)
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{font.DisplayName}（フォント）",
            OgCard = new OgCardSpec(Kicker: "フォント", Title: font.DisplayName)
            {
                Subtitle = $"{content.FoundryLabel}の書体。歴代プリキュアで使われた作品と場所を、見本つきでまとめました。",
                Badges = new[] { new OgCardBadge("作品", $"{content.SeriesCount}") }
            },
            MetaDescription = $"{font.DisplayName}（{content.FoundryLabel}）が使われた歴代プリキュアの作品と場所（OP・ED のクレジットと歌詞、サブタイトル）を、見本つきでまとめました。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "フォント", Url = "/fonts/" },
                new BreadcrumbItem { Label = font.DisplayName, Url = "" }
            }
        };
        _page.RenderAndWrite(url, "fonts", "fonts-detail.sbn", content, layout);
    }

    // ════════════════════ 行の組み立て ════════════════════

    /// <summary>書体の表の 1 行を、ページに出す形にする（見本の画像もここで描く）。</summary>
    private FontUsageRow BuildUsageRow(SeriesFontUsage usage)
    {
        var font = _ctx.FontById[usage.FontId];
        var series = _ctx.SeriesById[usage.SeriesId];
        var sample = FontSampleRequests.For(usage, _ctx) is { } request ? _page.RenderSubtitleTelop(request) : null;

        return new FontUsageRow
        {
            SeriesId = series.SeriesId,
            SeriesTitle = series.Title,
            SeriesUrl = PathUtil.SeriesUrl(series.Slug),
            StartYearLabel = series.StartDate.Year.ToString(CultureInfo.InvariantCulture),
            FontId = font.FontId,
            UsageLabel = UsageLabel(usage),
            IsRuby = FontUsageKinds.IsRuby(usage.UsageKind),
            IsBroadcastOnly = usage.IsBroadcastOnly,
            FontName = font.DisplayName,
            FontUrl = FontSampleRequests.FontUrl(font),
            WeightUnknown = usage.IsWeightUnknown,
            ModificationNote = usage.ModificationNote ?? "",
            DisplayNote = usage.DisplayNote ?? "",
            SizeLabel = SizeLabel(usage),
            SampleSrc = sample?.Src ?? "",
            SampleWidth = sample?.Width ?? 0,
            SampleHeight = sample?.Height ?? 0,
            SampleAlt = sample is null ? "" : $"{font.DisplayName}の見本（{FontUsageKinds.Label(usage.UsageKind)}）",
            SubstituteNote = sample is null ? "" : SubstituteNote(font, usage.IsWeightUnknown),
            IsEdCreditSample = sample is not null && usage.UsageKind == FontUsageKinds.EdCredit
        };
    }

    /// <summary>
    /// 使う場所の名前。時期が 2 つ以上ある場所は「（前期）」「（後期）」（3 つ目からは「（第 3 期）」）を、
    /// 本放送だけの行は「（本放送の第 N〜M 話）」を、範囲を持つ通常の行は「（第 N〜M 話）」を添える。
    /// </summary>
    private string UsageLabel(SeriesFontUsage usage)
    {
        string label = FontUsageKinds.Label(usage.UsageKind);
        string range = EpisodeRange(usage.FirstEpisodeNo, usage.LastEpisodeNo);
        if (usage.IsBroadcastOnly)
            return range == "" ? $"{label}（本放送）" : $"{label}（本放送の{range}）";

        int periods = _ctx.FontUsages
            .Where(u => u.SeriesId == usage.SeriesId && u.UsageKind == usage.UsageKind && !u.IsBroadcastOnly)
            .Select(u => u.PeriodNo).Distinct().Count();
        var notes = new List<string>();
        if (periods >= 2)
            notes.Add(usage.PeriodNo switch { 1 => "前期", 2 when periods == 2 => "後期", _ => $"第 {usage.PeriodNo} 期" });
        if (range != "") notes.Add(range);
        return notes.Count == 0 ? label : $"{label}（{string.Join("・", notes)}）";
    }

    /// <summary>話の範囲（「第 33〜34 話」「第 22 話」）。どちらも無ければ空文字。</summary>
    private static string EpisodeRange(ushort? first, ushort? last)
    {
        if (first is null && last is null) return "";
        if (first is ushort f && last is ushort l && f != l) return $"第{f}〜{l}話";
        return $"第{first ?? last}話";
    }

    /// <summary>
    /// 字の大きさ（クレジットは「人名 48px・役職名 36px」、ほかは「48px」）。無ければ空文字。
    /// 人名の字取り（name_span_em）はデータとして持つだけで、サイトには出さない。
    /// </summary>
    private static string SizeLabel(SeriesFontUsage usage)
    {
        static string Px(decimal v) => $"{v.ToString("0.#", CultureInfo.InvariantCulture)}px";
        if (FontUsageKinds.IsCredit(usage.UsageKind))
        {
            var parts = new List<string>();
            if (usage.SizePx is decimal name) parts.Add($"人名 {Px(name)}");
            if (usage.RoleSizePx is decimal role) parts.Add($"役職名 {Px(role)}");
            return string.Join("・", parts);
        }
        return usage.SizePx is decimal size ? Px(size) : "";
    }

    /// <summary>
    /// 見本に添える注記。書体に注記（<see cref="FontFace.RenderNote"/>。改刻版で描くときなど）があればそれ、
    /// ウェイトの分からない行は「ウェイト不明。見本は ○○ で描いています」、
    /// 放送と別の書体名・ウェイトで描くときは「見本は ○○ で代用（放送は △△）」。どれでもなければ空文字。
    /// </summary>
    private static string SubstituteNote(FontFace font, bool weightUnknown)
    {
        if (string.IsNullOrWhiteSpace(font.RenderFontName)) return "";
        if (!string.IsNullOrWhiteSpace(font.RenderNote)) return font.RenderNote;
        string renderName = font.RenderDisplayName;
        if (weightUnknown)
            return renderName != font.FamilyName ? $"ウェイト不明。見本は{renderName}で描いています" : "";
        return font.IsRenderSubstitute ? $"見本は{renderName}で代用（放送は{font.DisplayName}）" : "";
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
        /// <summary>作り手ごとの書体の一覧（写研 → モリサワ → フォントワークス）。</summary>
        public List<FontFoundryGroup> FoundryGroups { get; set; } = new();
        /// <summary>作品ごとの表（放送開始順）。</summary>
        public List<FontSeriesSection> SeriesSections { get; set; } = new();
        public int FontCount { get; set; }
        public int SeriesCount { get; set; }
        /// <summary>ED クレジットの見本（人名をお借りしている）があれば true（断り書きを出す）。</summary>
        public bool HasEdCreditSample { get; set; }
    }

    private sealed class FontFoundryGroup
    {
        public string Label { get; set; } = "";
        public List<FontChip> Fonts { get; set; } = new();
    }

    private sealed class FontChip
    {
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>この書体を使った作品の数。</summary>
        public int SeriesCount { get; set; }
    }

    private sealed class FontSeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string StartYearLabel { get; set; } = "";
        public List<FontUsageRow> Rows { get; set; } = new();
        /// <summary>字の大きさの入った行があれば true（大きさの列を出す）。</summary>
        public bool HasSize { get; set; }
    }

    private sealed class FontDetailModel
    {
        public string Name { get; set; } = "";
        public string FoundryLabel { get; set; } = "";
        /// <summary>写研アーカイブの書体のページ。空ならリンクを出さない。</summary>
        public string ArchiveUrl { get; set; } = "";
        /// <summary>提供元の製品ページ。空ならリンクを出さない。</summary>
        public string ProductUrl { get; set; } = "";
        /// <summary>製品ページのリンクの名前（「モリサワ」「フォントワークス」）。</summary>
        public string ProductLabel { get; set; } = "";
        /// <summary>見本を描く書体があるか（無ければ見本が無い旨を出す）。</summary>
        public bool HasSample { get; set; }
        /// <summary>見本を別のウェイトで描く旨（無ければ空文字）。</summary>
        public string SubstituteNote { get; set; } = "";
        public List<FontUsageRow> Rows { get; set; } = new();
        public int SeriesCount { get; set; }
        public bool HasSize { get; set; }
        /// <summary>ED クレジットの見本（人名をお借りしている）があれば true（断り書きを出す）。</summary>
        public bool HasEdCreditSample { get; set; }
    }

    /// <summary>書体の表の 1 行（作品 × 使う場所 × 時期）。</summary>
    private sealed class FontUsageRow
    {
        public int SeriesId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string StartYearLabel { get; set; } = "";
        public int FontId { get; set; }
        /// <summary>使う場所（時期・本放送の範囲つき）。</summary>
        public string UsageLabel { get; set; } = "";
        /// <summary>ルビの行（見本は親字の行に含まれる）。</summary>
        public bool IsRuby { get; set; }
        public bool IsBroadcastOnly { get; set; }
        public string FontName { get; set; } = "";
        public string FontUrl { get; set; } = "";
        public bool WeightUnknown { get; set; }
        public string ModificationNote { get; set; } = "";
        public string DisplayNote { get; set; } = "";
        public string SizeLabel { get; set; } = "";
        public string SampleSrc { get; set; } = "";
        public int SampleWidth { get; set; }
        public int SampleHeight { get; set; }
        public string SampleAlt { get; set; } = "";
        public string SubstituteNote { get; set; } = "";
        /// <summary>ED クレジットの見本を描いた行か（見本の人名の断り書きを出すかの判定）。</summary>
        public bool IsEdCreditSample { get; set; }
    }
}
