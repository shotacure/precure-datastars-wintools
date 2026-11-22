using System.Globalization;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>作品別の内訳の 1 行（その作品の話がランキングに何話あるか）。</summary>
public sealed class StatsBreakdownRow
{
    public string SeriesTitle { get; init; } = "";
    public string SeriesUrl { get; init; } = "";
    public string SeriesStartYearLabel { get; init; } = "";
    public int Count { get; init; }
    /// <summary>横棒の長さ（いちばん多い作品を 100 とした %）。style 属性用。</summary>
    public string BarPercent { get; init; } = "0";
}

/// <summary>
/// 順位の行のひとまとまり（同じ順位の話）。左のパネル（順位と値）は 1 つだけ出し、右に話を縦に並べる。
/// 同点の話が続いても、同じ値のパネルを話の数だけ繰り返さない。
/// </summary>
public sealed class StatsRankGroup
{
    /// <summary>順位を持つか（持たない行は左のパネルを出さない）。</summary>
    public bool HasRank { get; init; }
    public string RankLabel { get; init; } = "";
    public string ValueLabel { get; init; } = "";
    public bool HasValue { get; init; }

    /// <summary>同じ順位の話の数（「104 話」。1 話だけなら空）。</summary>
    public string CountLabel { get; init; } = "";

    /// <summary>このまとまりの話（放送順など、渡された並びのまま）。</summary>
    public List<StatsEpisodeRow> Entries { get; init; } = new();

    /// <summary>このまとまりの話を、続いている同じ作品ごとに束ねたもの（作品名は束ごとに 1 回だけ出す）。</summary>
    public List<StatsSeriesBlock> Blocks { get; init; } = new();
}

/// <summary>同じ順位の中で続いている、同じ作品の話の束。</summary>
public sealed class StatsSeriesBlock
{
    public string SeriesTitle { get; init; } = "";
    public string SeriesUrl { get; init; } = "";
    public string SeriesStartYearLabel { get; init; } = "";
    public List<StatsEpisodeRow> Entries { get; init; } = new();
}

/// <summary>放送枠の帯の凡例の 1 項目。</summary>
public sealed class StatsBandLegendItem
{
    /// <summary>色の CSS クラス（<c>sb-hit</c> など）。</summary>
    public string Class { get; init; } = "";
    public string Label { get; init; } = "";
}

/// <summary>
/// 統計のエピソード単位のランキングのページ（<c>_stats-episode-rank.sbn</c>）へ渡すモデル。
/// 上から、上位 3 件の枠・全部の話の分布の図・作品別の内訳・4 位からの行。
/// </summary>
public sealed class StatsRankingView
{
    public string CoverageLabel { get; init; } = "";

    /// <summary>上位 3 件の枠に出す行（3 位までの全件）。枠を出さないページでは空。</summary>
    public List<StatsEpisodeRow> Podium { get; init; } = new();

    /// <summary>行の並び（上位 3 件の枠に出した行を除く）。</summary>
    public List<StatsEpisodeRow> Rows { get; init; } = new();

    /// <summary>行の並びを、同じ順位ごとにまとめたもの（テンプレートはこちらを描く）。</summary>
    public List<StatsRankGroup> Groups { get; init; } = new();

    /// <summary>行の並びの見出し（「4 位から」。上位 3 件の枠を出さないページでは空）。</summary>
    public string RestHeading { get; init; } = "";

    /// <summary>上位 3 件の枠の値の下に書く、値の名前（「漢字率」「アバンタイトル」など）。</summary>
    public string ValueCaption { get; init; } = "";

    /// <summary>分布の図（SVG）。無ければ空。</summary>
    public string ChartSvg { get; init; } = "";
    public string ChartHeading { get; init; } = "";
    public string ChartNote { get; init; } = "";

    /// <summary>作品別の内訳（放送順。ランキングに 1 話も無い作品も並べる）。</summary>
    public List<StatsBreakdownRow> Breakdown { get; init; } = new();
    public string BreakdownNote { get; init; } = "";

    /// <summary>
    /// サブタイトルそのものを扱うページか（サブタイトル統計の順位のページ）。true なら、題名を統計の対象の字と同じ
    /// 太い明朝体で出す（テンプレートが <c>is-subtitle-stats</c> を付ける）。
    /// </summary>
    public bool SubtitleStats { get; init; }

    /// <summary>放送枠の帯の凡例（帯を出さないページでは空）。</summary>
    public List<StatsBandLegendItem> BandLegend { get; init; } = new();

    // 歴代記録の 1 話のクレジットの順位のページが、冒頭の節に出す文。
    public string Title { get; init; } = "";
    public string Lead { get; init; } = "";
    public string Note { get; init; } = "";

    /// <summary>上位 3 件の枠は、3 位までがこの件数以内のときだけ出す（同点が大量に並ぶページでは出さない）。</summary>
    private const int PodiumMaxCards = 6;

    /// <summary>
    /// 行の並びから、上位 3 件の枠・作品別の内訳を切り出してモデルを組む。
    /// </summary>
    /// <param name="rows">ランキングの全行（順位の昇順）。</param>
    /// <param name="usePodium">上位 3 件の枠を出すか（順位を持たない一覧では false）。</param>
    public static StatsRankingView Build(
        BuildContext ctx, List<StatsEpisodeRow> rows, string coverageLabel, string valueCaption,
        bool usePodium = true, string chartSvg = "", string chartHeading = "", string chartNote = "",
        string bandHighlightPartType = "", bool hasBand = false,
        string title = "", string lead = "", string note = "", bool subtitleStats = false)
    {
        var podium = usePodium ? rows.Where(r => r.RankNo is >= 1 and <= 3).ToList() : new List<StatsEpisodeRow>();
        if (podium.Count == 0 || podium.Count > PodiumMaxCards) podium = new List<StatsEpisodeRow>();
        if (podium.Count > 0 && podium.Count(r => r.RankNo == 1) == 1) podium[0].IsFirst = true;
        var rest = podium.Count > 0 ? rows.Where(r => !(r.RankNo is >= 1 and <= 3)).ToList() : rows;

        return new StatsRankingView
        {
            CoverageLabel = coverageLabel,
            Podium = podium,
            Rows = rest,
            Groups = BuildGroups(rest),
            SubtitleStats = subtitleStats,
            RestHeading = podium.Count > 0 && rest.Count > 0 ? "4 位から" : "",
            ValueCaption = valueCaption,
            ChartSvg = chartSvg,
            ChartHeading = chartHeading,
            ChartNote = chartNote,
            Breakdown = BuildBreakdown(ctx, rows),
            BreakdownNote = $"この一覧の {rows.Count:N0} 話を、作品ごとに数えた話数です。作品は放送順です。",
            BandLegend = hasBand ? BuildBandLegend(ctx, bandHighlightPartType) : new List<StatsBandLegendItem>(),
            Title = title,
            Lead = lead,
            Note = note
        };
    }

    /// <summary>続いている同じ順位の行を 1 つにまとめる。順位を持たない行は 1 行ずつ。</summary>
    private static List<StatsRankGroup> BuildGroups(List<StatsEpisodeRow> rows)
    {
        var groups = new List<StatsRankGroup>();
        int i = 0;
        while (i < rows.Count)
        {
            var head = rows[i];
            int j = i + 1;
            if (head.HasRank)
                while (j < rows.Count && rows[j].HasRank && rows[j].RankNo == head.RankNo) j++;
            groups.Add(new StatsRankGroup
            {
                HasRank = head.HasRank,
                RankLabel = head.HasRank ? head.RankNo.ToString(CultureInfo.InvariantCulture) : "",
                ValueLabel = head.ValueLabel,
                HasValue = head.HasValue,
                CountLabel = j - i >= 2 ? $"{j - i:N0} 話" : "",
                Entries = rows.GetRange(i, j - i),
                Blocks = BuildBlocks(rows.GetRange(i, j - i))
            });
            i = j;
        }
        return groups;
    }

    /// <summary>続いている同じ作品の話を束ねる（並びは変えない）。</summary>
    private static List<StatsSeriesBlock> BuildBlocks(List<StatsEpisodeRow> entries)
    {
        var blocks = new List<StatsSeriesBlock>();
        foreach (var e in entries)
        {
            if (blocks.Count > 0 && blocks[^1].Entries[0].SeriesSlug == e.SeriesSlug)
            {
                blocks[^1].Entries.Add(e);
                continue;
            }
            blocks.Add(new StatsSeriesBlock
            {
                SeriesTitle = e.SeriesTitle,
                SeriesUrl = e.SeriesUrl,
                SeriesStartYearLabel = e.SeriesStartYearLabel,
                Entries = new List<StatsEpisodeRow> { e }
            });
        }
        return blocks;
    }

    /// <summary>本線の TV シリーズを放送順に並べ、行の並びの中の話数を数える。</summary>
    private static List<StatsBreakdownRow> BuildBreakdown(BuildContext ctx, List<StatsEpisodeRow> rows)
    {
        var counts = rows.GroupBy(r => r.SeriesSlug, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        int max = counts.Count > 0 ? counts.Values.Max() : 0;
        return ctx.SeriesById.Values
            .Where(s => ctx.IsMainlineTvSeries(s.SeriesId))
            .OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId)
            .Select(s =>
            {
                int n = counts.TryGetValue(s.Slug, out var c) ? c : 0;
                return new StatsBreakdownRow
                {
                    SeriesTitle = s.Title,
                    SeriesUrl = PathUtil.SeriesUrl(s.Slug),
                    SeriesStartYearLabel = ctx.StartYearLabelBySlug(s.Slug),
                    Count = n,
                    BarPercent = (max > 0 ? n * 100.0 / max : 0.0).ToString("0.0", CultureInfo.InvariantCulture)
                };
            })
            .ToList();
    }

    private static List<StatsBandLegendItem> BuildBandLegend(BuildContext ctx, string highlightPartType)
    {
        var list = new List<StatsBandLegendItem>();
        bool hitIsMain = highlightPartType is "AVANT" or "PART_A" or "PART_B" or "PART_C";
        bool hitIsCm = highlightPartType.StartsWith("CM", StringComparison.Ordinal);
        if (highlightPartType.Length > 0)
        {
            string name = highlightPartType == "CM2" ? "中 CM"
                : ctx.PartTypeByCode.TryGetValue(highlightPartType, out var pt) ? pt.NameJa : highlightPartType;
            list.Add(new StatsBandLegendItem { Class = "sb-hit", Label = name });
        }
        list.Add(new StatsBandLegendItem { Class = "sb-song", Label = "OP・ED" });
        list.Add(new StatsBandLegendItem { Class = "sb-main", Label = hitIsMain ? "ほかの本編" : "本編" });
        list.Add(new StatsBandLegendItem { Class = "sb-cm", Label = hitIsCm ? "ほかの CM" : "CM" });
        list.Add(new StatsBandLegendItem { Class = "sb-misc", Label = "提供・予告など" });
        return list;
    }

    /// <summary>値の横棒（<paramref name="max"/> を端まで届く長さにする）。シリーズ別の順位のページの棒と同じ作り。</summary>
    public static string BarHtml(double value, double max)
        => "<span class=\"stats-series-bar\" aria-hidden=\"true\"><span class=\"stats-series-bar-fill\" style=\"width: "
           + (max > 0 ? value * 100.0 / max : 0.0).ToString("0.0", CultureInfo.InvariantCulture) + "%\"></span></span>";

    // ── 分布の図の材料 ──

    /// <summary>平均と標準偏差（母集団）。値が無ければ (0, 0)。</summary>
    public static (double Mean, double Std) MeanStd(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return (0, 0);
        double mean = values.Average();
        double std = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        return (mean, std);
    }

    /// <summary>偏差値（平均 50、標準偏差 10）の表記。各話のページの尺の偏差値と同じ小数 2 桁。標準偏差が 0 なら空。</summary>
    public static string HensachiLabel(double value, double mean, double std)
        => std > 0 ? "偏差値 " + (50.0 + 10.0 * (value - mean) / std).ToString("0.00", CultureInfo.InvariantCulture) : "";

    /// <summary>
    /// 秒数の分布の図。棒は 10 秒きざみ（範囲が広いときは 20・30 秒）、目盛りは 1 分ごと。図の上に偏差値の位置を添える。
    /// </summary>
    /// <param name="descending">ランキングが大きい順か（境目の線の説明が「以上」「以下」で変わる）。</param>
    /// <param name="format">秒数の表記（<c>m:ss</c>、または放送時刻）。</param>
    /// <param name="clockTime">値が時刻か（境目の線の説明を「以降」「以前」にする。尺なら「以上」「以下」）。</param>
    /// <param name="tickFormat">目盛りの表記（分ちょうどの値だけが渡る）。null なら <paramref name="format"/> と同じ。</param>
    public static string TimeHistogram(IReadOnlyList<double> all, IReadOnlyList<double> ranked, IReadOnlyList<double> top10, bool descending,
        Func<double, string> format, string ariaLabel, Func<double, string>? tickFormat = null, bool clockTime = false)
    {
        tickFormat ??= format;
        if (all.Count == 0) return "";
        double lo = Math.Floor(all.Min() / 60.0) * 60;
        double hi = Math.Ceiling((all.Max() + 1) / 60.0) * 60;
        double width = (hi - lo) / 10 <= 90 ? 10 : (hi - lo) / 20 <= 90 ? 20 : 30;
        int bins = (int)Math.Ceiling((hi - lo) / width);
        int tickStep = (hi - lo) / 60 <= 12 ? 60 : (hi - lo) / 120 <= 12 ? 120 : 300;
        var ticks = new List<StatsAxisMark>();
        for (double v = lo; v <= hi + 1e-9; v += tickStep) ticks.Add(new StatsAxisMark(v, tickFormat(v)));

        var (mean, std) = MeanStd(all);
        var marks = new List<StatsAxisMark>();
        if (std > 0)
            foreach (int h in new[] { 20, 30, 40, 50, 60, 70, 80 })
                marks.Add(new StatsAxisMark(mean + std * (h - 50) / 10.0, h.ToString(CultureInfo.InvariantCulture)));

        return StatsSeriesCharts.BuildHistogram(new StatsHistogramSpec
        {
            All = all,
            Ranked = ranked,
            Start = lo,
            BinWidth = width,
            BinCount = bins,
            Ticks = ticks,
            BinLabel = i => $"{format(lo + width * i)}〜{format(lo + width * (i + 1) - 1)}",
            Cuts = CutMarks(ranked, top10, descending, format, upperEdge: 1, clockTime ? "以降" : "以上", clockTime ? "以前" : "以下"),
            TopMarks = marks,
            TopMarksCaption = "偏差値",
            AriaLabel = ariaLabel
        });
    }

    /// <summary>個数（文字数・人数）の分布の図。棒は 1 つの数ごと（範囲が広いときは 2・5 ごと）。</summary>
    public static string CountHistogram(IReadOnlyList<double> all, IReadOnlyList<double> ranked, IReadOnlyList<double> top10, bool descending,
        string unit, string ariaLabel)
    {
        if (all.Count == 0) return "";
        double lo = Math.Min(0, all.Min());
        double max = all.Max();
        double width = (max - lo + 1) <= 70 ? 1 : (max - lo + 1) / 2 <= 70 ? 2 : 5;
        int bins = (int)Math.Ceiling((max - lo + 1) / width);
        double range = width * bins;
        int tickStep = range <= 12 ? 1 : range <= 30 ? 5 : range <= 70 ? 10 : range <= 140 ? 20 : 50;
        var ticks = new List<StatsAxisMark>();
        // 目盛りは棒のまんなかに置く（1 つの数に 1 本の棒が当たるため）。
        for (double v = Math.Ceiling(lo / tickStep) * tickStep; v <= lo + range - 1 + 1e-9; v += tickStep)
            ticks.Add(new StatsAxisMark(v + (width == 1 ? 0.5 : 0), v.ToString("0", CultureInfo.InvariantCulture) + unit));

        string Fmt(double v) => v.ToString("0", CultureInfo.InvariantCulture) + unit;
        return StatsSeriesCharts.BuildHistogram(new StatsHistogramSpec
        {
            All = all,
            Ranked = ranked,
            Start = lo,
            BinWidth = width,
            BinCount = bins,
            Ticks = ticks,
            BinLabel = i => width == 1 ? Fmt(lo + i) : $"{Fmt(lo + width * i)}〜{Fmt(lo + width * (i + 1) - 1)}",
            Cuts = CutMarks(ranked, top10, descending, Fmt, upperEdge: 1),
            AriaLabel = ariaLabel
        });
    }

    /// <summary>率（%）の分布の図。棒は 5% きざみ、目盛りは 20% ごと。100% は最後の棒に入れる。</summary>
    public static string PercentHistogram(IReadOnlyList<double> all, IReadOnlyList<double> ranked, IReadOnlyList<double> top10, bool descending, string ariaLabel)
    {
        if (all.Count == 0) return "";
        string Fmt(double v) => v.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var ticks = new List<StatsAxisMark>();
        for (int v = 0; v <= 100; v += 20) ticks.Add(new StatsAxisMark(v, v + "%"));
        return StatsSeriesCharts.BuildHistogram(new StatsHistogramSpec
        {
            All = all,
            Ranked = ranked,
            Start = 0,
            BinWidth = 5,
            BinCount = 20,
            Ticks = ticks,
            BinLabel = i => i == 19 ? "95〜100%" : $"{i * 5}%〜{i * 5 + 5}% 未満",
            Cuts = CutMarks(ranked, top10, descending, Fmt, upperEdge: 0),
            AriaLabel = ariaLabel
        });
    }

    /// <summary>
    /// ランキングの境目の線（TOP 100 と TOP 10）。大きい順なら、その範囲のいちばん小さい値に引いて「TOP 100：2:09 以上」、
    /// 小さい順ならいちばん大きい値（の棒の右端）に引いて「TOP 100：6:26 以下」と添える。
    /// TOP 10 の境目が TOP 100 と同じ値なら（同点が多いとき）、TOP 10 の線は引かない。
    /// </summary>
    /// <param name="ranked">TOP 100 に入った話の値（100 位が同点なら全件）。</param>
    /// <param name="top10">TOP 10 に入った話の値（10 位が同点なら全件）。</param>
    private static IReadOnlyList<StatsAxisMark> CutMarks(IReadOnlyList<double> ranked, IReadOnlyList<double> top10, bool descending,
        Func<double, string> format, double upperEdge, string fromWord = "以上", string toWord = "以下")
    {
        var marks = new List<StatsAxisMark>();
        void Add(string name, IReadOnlyList<double> values)
        {
            if (values.Count == 0) return;
            double edge = descending ? values.Min() : values.Max();
            var mark = descending
                ? new StatsAxisMark(edge, $"{name}：{format(edge)} {fromWord}")
                : new StatsAxisMark(edge + upperEdge, $"{name}：{format(edge)} {toWord}");
            if (marks.Count > 0 && marks[^1].Value == mark.Value) return;
            marks.Add(mark);
        }
        Add("TOP 100", ranked);
        Add("TOP 10", top10);
        return marks;
    }
}
