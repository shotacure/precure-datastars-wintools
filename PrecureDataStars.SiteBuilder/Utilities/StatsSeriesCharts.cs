using System.Globalization;
using System.Net;
using System.Text;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>箱ひげ図の 1 作品ぶんの材料（<see cref="StatsSeriesCharts.BuildBoxPlot"/> へ渡す）。</summary>
/// <param name="Year">放送開始年。</param>
/// <param name="SeriesTitle">作品名（マウスを載せたときの説明に出す）。</param>
/// <param name="Values">その作品の各話の値（並びは問わない。1 件以上）。</param>
public sealed record StatsBoxPlotItem(int Year, string SeriesTitle, IReadOnlyList<int> Values);

/// <summary>折れ線グラフの 1 作品ぶんの材料（<see cref="StatsSeriesCharts.BuildLineChart"/> へ渡す）。</summary>
/// <param name="Year">放送開始年。</param>
/// <param name="SeriesTitle">作品名（マウスを載せたときの説明に出す）。</param>
/// <param name="Value">その作品の値。</param>
/// <param name="DetailLabel">説明に添える内訳（「漢字 312 / 総文字数 901」など。無ければ空）。</param>
public sealed record StatsLineChartItem(int Year, string SeriesTitle, double Value, string DetailLabel);

/// <summary>横軸の目盛り、または図の上に添える印（偏差値など）。</summary>
/// <param name="Value">横軸上の値。</param>
/// <param name="Label">書く字。</param>
public readonly record struct StatsAxisMark(double Value, string Label);

/// <summary>分布の図（<see cref="StatsSeriesCharts.BuildHistogram"/>）の材料。</summary>
public sealed class StatsHistogramSpec
{
    /// <summary>全部の話の値。</summary>
    public required IReadOnlyList<double> All { get; init; }
    /// <summary>ランキングに入った話の値（<see cref="All"/> の一部）。棒の中で濃い色に塗る。</summary>
    public required IReadOnlyList<double> Ranked { get; init; }
    /// <summary>横軸の左端の値。</summary>
    public required double Start { get; init; }
    /// <summary>棒 1 本ぶんの幅（値の単位）。</summary>
    public required double BinWidth { get; init; }
    /// <summary>棒の本数。範囲の外の値は両端の棒に入れる。</summary>
    public required int BinCount { get; init; }
    /// <summary>横軸の目盛り。</summary>
    public required IReadOnlyList<StatsAxisMark> Ticks { get; init; }
    /// <summary>棒にマウスを載せたときの説明に出す、棒の範囲の名前（棒の番号から作る。「1:20〜1:29」など）。</summary>
    public required Func<int, string> BinLabel { get; init; }
    /// <summary>ランキングの境目に引く線の位置と、その説明。線を引かないときは null。</summary>
    public StatsAxisMark? Cut { get; init; }
    /// <summary>図の上に添える印（偏差値 50・60・70 の位置など）。</summary>
    public IReadOnlyList<StatsAxisMark> TopMarks { get; init; } = Array.Empty<StatsAxisMark>();
    /// <summary>図の上の印の並びの名前（「偏差値」。印の行の左端に書く）。</summary>
    public string TopMarksCaption { get; init; } = "";
    /// <summary>図の代替テキスト。</summary>
    public required string AriaLabel { get; init; }
}

/// <summary>
/// 統計のページに置くグラフを、SVG の文字列として描く。シリーズ別のページの図は、
/// 横軸が放送開始年（渡された並びのまま左から置く）、縦軸が値。ランキングのページの図は、全部の話の分布。
/// ページに直に埋め込むので、字の書体はページのものを継ぐ。どの図も、印にマウスを載せると数値の説明が出る。
/// </summary>
public static class StatsSeriesCharts
{
    // 図の枠（viewBox の単位）。作品の列は PlotLeft〜PlotRight を等分して、その中央に置く。
    private const double Width = 900;
    private const double PlotLeft = 80;
    private const double PlotRight = 840;
    private const double PlotTop = 64;
    private const double PlotBottom = 404;
    private const double Height = 464;

    // 色。箱と折れ線は青、最頻値は橙、いちばん大きい値と小さい値は赤で目立たせる。
    private const string Ink = "#0b0b0b";
    private const string Sub = "#52514e";
    private const string Grid = "#e4e3df";
    private const string Blue = "#2a78d6";
    private const string BlueFill = "#d4e4f7";
    private const string Orange = "#eb6834";
    private const string Red = "#c0143c";
    private const string Paper = "#ffffff";

    /// <summary>
    /// 作品ごとの値の分布を箱ひげ図で描く。ひげは最小値〜最大値、箱は四分位範囲、箱の中の線は中央値、
    /// 右の三角は最頻値（複数あれば全部）、線で結んだ白い丸は平均値。全作品でいちばん大きい値と
    /// いちばん小さい値は、ひげの端と数値を赤で描く。
    /// </summary>
    /// <param name="items">作品の並び（放送開始年の昇順）。</param>
    /// <param name="unit">値の単位（縦軸の上と説明に出す。「文字」など）。</param>
    /// <param name="ariaLabel">図の代替テキスト。</param>
    public static string BuildBoxPlot(IReadOnlyList<StatsBoxPlotItem> items, string unit, string ariaLabel)
    {
        if (items.Count == 0) return "";

        var stats = items.Select(i => BoxStats.From(i.Values)).ToList();
        int overallMax = stats.Max(s => s.Max);
        int overallMin = stats.Min(s => s.Min);

        // 縦軸は 0 から、最大値を 5 刻みへ切り上げた値まで。
        const int step = 5;
        int top = Math.Max(step, (int)Math.Ceiling(overallMax / (double)step) * step);
        double Y(double v) => PlotBottom - (PlotBottom - PlotTop) * v / top;
        double colWidth = (PlotRight - PlotLeft) / items.Count;
        double X(int i) => PlotLeft + colWidth * (i + 0.5);

        var sb = new StringBuilder();
        OpenSvg(sb, ariaLabel);

        // 凡例
        const double ly = 24;
        sb.Append(Line(87, ly - 11, 87, ly + 11, Sub, 1.5));
        sb.Append(Line(83, ly - 11, 91, ly - 11, Sub, 1.5));
        sb.Append(Line(83, ly + 11, 91, ly + 11, Sub, 1.5));
        sb.Append(Text(100, ly + 5, 13, Sub, "ひげ：最小値〜最大値"));
        sb.Append($"<rect x=\"252\" y=\"{F(ly - 8)}\" width=\"14\" height=\"16\" fill=\"{BlueFill}\" stroke=\"{Blue}\" stroke-width=\"1\"/>");
        sb.Append(Text(274, ly + 5, 13, Sub, "箱：四分位範囲"));
        sb.Append(Line(387, ly, 401, ly, Blue, 3));
        sb.Append(Text(409, ly + 5, 13, Sub, "中央値"));
        sb.Append(Line(468, ly, 486, ly, Ink, 1.5));
        sb.Append(Circle(477, ly, Paper, Ink));
        sb.Append(Text(492, ly + 5, 13, Sub, "平均値"));
        sb.Append($"<path d=\"M553,{F(ly)} l6.6,-5.5 v11 z\" fill=\"{Orange}\"/>");
        sb.Append(Text(567, ly + 5, 13, Sub, "最頻値（複数あれば全部）"));

        // 目盛り
        for (int v = 0; v <= top; v += step)
        {
            sb.Append(Line(PlotLeft, Y(v), PlotRight, Y(v), Grid, 1));
            sb.Append(Text(PlotLeft - 8, Y(v) + 5, 13, Sub, v.ToString(CultureInfo.InvariantCulture), "end"));
        }
        sb.Append(Text(PlotLeft - 8, PlotTop - 18, 13, Sub, unit, "end"));

        // 作品ごとの箱とひげ
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var s = stats[i];
            double x = X(i);
            bool isMax = s.Max == overallMax;
            bool isMin = s.Min == overallMin;

            sb.Append("<g><title>").Append(Esc(
                $"{it.Year}年『{it.SeriesTitle}』：最短 {s.Min}・中央値 {Num(s.Median)}・平均 {s.Mean:0.0}・最長 {s.Max}・最頻値 {string.Join("・", s.Modes)}"))
              .Append("</title>");
            sb.Append(Line(x, Y(s.Max), x, Y(s.Min), Sub, 1.5));
            sb.Append(isMin ? Line(x - 6, Y(s.Min), x + 6, Y(s.Min), Red, 2.5) : Line(x - 4, Y(s.Min), x + 4, Y(s.Min), Sub, 1.5));
            sb.Append(isMax ? Line(x - 6, Y(s.Max), x + 6, Y(s.Max), Red, 2.5) : Line(x - 4, Y(s.Max), x + 4, Y(s.Max), Sub, 1.5));
            sb.Append($"<rect x=\"{F(x - 7)}\" y=\"{F(Y(s.Q3))}\" width=\"14\" height=\"{F(Y(s.Q1) - Y(s.Q3))}\" fill=\"{BlueFill}\" stroke=\"{Blue}\" stroke-width=\"1\"/>");
            sb.Append(Line(x - 7, Y(s.Median), x + 7, Y(s.Median), Blue, 3));
            foreach (int m in s.Modes)
                sb.Append($"<path d=\"M{F(x + 9)},{F(Y(m))} l5.4,-4.5 v9 z\" fill=\"{Orange}\"/>");
            sb.Append(ValueLabel(x, Y(s.Max) - 7, s.Max.ToString(CultureInfo.InvariantCulture), isMax));
            sb.Append(ValueLabel(x, Y(s.Min) + 16, s.Min.ToString(CultureInfo.InvariantCulture), isMin));
            sb.Append("</g>");
            sb.Append(YearLabel(x, it.Year));
        }

        // 平均値の折れ線（箱の上に重ねる）
        sb.Append("<polyline points=\"")
          .Append(string.Join(" ", stats.Select((s, i) => $"{F(X(i))},{F(Y(s.Mean))}")))
          .Append($"\" fill=\"none\" stroke=\"{Ink}\" stroke-width=\"1.5\" stroke-linejoin=\"round\"/>");
        for (int i = 0; i < stats.Count; i++)
            sb.Append(Circle(X(i), Y(stats[i].Mean), Paper, Ink));

        CloseSvg(sb);
        return sb.ToString();
    }

    /// <summary>
    /// 作品ごとの値を折れ線グラフで描く。各点に値を添え、いちばん大きい値といちばん小さい値は赤で描く。
    /// </summary>
    /// <param name="items">作品の並び（放送開始年の昇順）。</param>
    /// <param name="valueName">値の名前（説明に出す。「漢字率」など）。</param>
    /// <param name="unit">値の単位（目盛りと説明に付ける。「%」など）。</param>
    /// <param name="ariaLabel">図の代替テキスト。</param>
    public static string BuildLineChart(IReadOnlyList<StatsLineChartItem> items, string valueName, string unit, string ariaLabel)
    {
        if (items.Count == 0) return "";

        double max = items.Max(i => i.Value);
        double min = items.Min(i => i.Value);

        // 縦軸は 0 から。目盛りは 7 本以内に収まる、いちばん細かい切りのよい刻みにする。
        double[] steps = { 1, 2, 5, 10, 20, 25, 50 };
        double step = steps.FirstOrDefault(s => Math.Ceiling(max / s) <= 7, 100);
        double top = Math.Max(step, Math.Ceiling(max / step) * step);
        // いちばん大きい値が上端に重なるときは、数値を書く場所を 1 目盛りぶん空ける。
        if (max > top - step * 0.25) top += step;
        double Y(double v) => PlotBottom - (PlotBottom - PlotTop) * v / top;
        double colWidth = (PlotRight - PlotLeft) / items.Count;
        double X(int i) => PlotLeft + colWidth * (i + 0.5);

        var sb = new StringBuilder();
        // 凡例を持たないので、凡例のぶんの上の空きを切り詰める。
        OpenSvg(sb, ariaLabel, top: PlotTop - 24);

        for (double v = 0; v <= top + 1e-9; v += step)
        {
            sb.Append(Line(PlotLeft, Y(v), PlotRight, Y(v), Grid, 1));
            sb.Append(Text(PlotLeft - 8, Y(v) + 5, 13, Sub, Num(v) + unit, "end"));
        }

        sb.Append("<polyline points=\"")
          .Append(string.Join(" ", items.Select((it, i) => $"{F(X(i))},{F(Y(it.Value))}")))
          .Append($"\" fill=\"none\" stroke=\"{Blue}\" stroke-width=\"2\" stroke-linejoin=\"round\"/>");

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            double x = X(i);
            double y = Y(it.Value);
            bool highlight = it.Value == max || it.Value == min;
            // 両隣より低い点は、線と重ならないよう数値を点の下に書く。
            bool below = (i == 0 || items[i - 1].Value > it.Value)
                      && (i == items.Count - 1 || items[i + 1].Value > it.Value);

            string detail = string.IsNullOrEmpty(it.DetailLabel) ? "" : $"（{it.DetailLabel}）";
            sb.Append("<g><title>").Append(Esc($"{it.Year}年『{it.SeriesTitle}』：{valueName} {it.Value:0.0}{unit}{detail}")).Append("</title>");
            sb.Append(highlight ? Circle(x, y, Red, Red) : Circle(x, y, Paper, Blue));
            sb.Append(ValueLabel(x, below ? y + 18 : y - 9, it.Value.ToString("0.0", CultureInfo.InvariantCulture), highlight));
            sb.Append("</g>");
            sb.Append(YearLabel(x, it.Year));
        }

        CloseSvg(sb);
        return sb.ToString();
    }

    // 分布の図の枠（viewBox の単位）と色。棒は灰色、ランキングに入った話の分はサイトのアクセント色。
    private const double HistLeft = 56;
    private const double HistRight = 884;
    private const double HistTop = 44;
    private const double HistBottom = 206;
    private const double HistHeight = 244;
    private const string Bar = "#d0cfca";
    private const string Accent = "#d81b60";
    private const string MarkLine = "#b9b8b3";

    /// <summary>
    /// 全部の話の値の分布を棒で描き、ランキングに入った話の分を濃い色で塗る。縦軸は話数。
    /// 境目の線（<see cref="StatsHistogramSpec.Cut"/>）と、図の上の印（<see cref="StatsHistogramSpec.TopMarks"/>）を重ねる。
    /// </summary>
    public static string BuildHistogram(StatsHistogramSpec spec)
    {
        if (spec.All.Count == 0 || spec.BinCount <= 0) return "";

        int Bin(double v) => Math.Clamp((int)Math.Floor((v - spec.Start) / spec.BinWidth + 1e-9), 0, spec.BinCount - 1);
        var total = new int[spec.BinCount];
        var ranked = new int[spec.BinCount];
        foreach (double v in spec.All) total[Bin(v)]++;
        foreach (double v in spec.Ranked) ranked[Bin(v)]++;
        int max = total.Max();

        double end = spec.Start + spec.BinWidth * spec.BinCount;
        double X(double v) => HistLeft + (HistRight - HistLeft) * (v - spec.Start) / (end - spec.Start);
        double barWidth = (HistRight - HistLeft) / spec.BinCount;
        double gap = Math.Min(3, barWidth * 0.18);

        // 縦軸の目盛りは 4 本以内に収まる、いちばん細かい切りのよい刻み。上端は最大の棒に合わせる。
        int[] steps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };
        int step = steps.FirstOrDefault(st => max / st <= 4, 2000);
        double H(double n) => (HistBottom - HistTop) * n / max;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {F(Width)} {F(HistHeight)}\" role=\"img\" aria-label=\"{Esc(spec.AriaLabel)}\">");

        for (int v = step; v <= max; v += step)
        {
            double y = HistBottom - H(v);
            sb.Append(Line(HistLeft, y, HistRight, y, Grid, 1));
            sb.Append(Text(HistLeft - 8, y + 4, 12, Sub, v.ToString(CultureInfo.InvariantCulture) + "話", "end"));
        }

        // 図の上の印（偏差値など）。近すぎる印は字が重なるので飛ばす。
        double lastMarkX = double.NegativeInfinity;
        if (spec.TopMarks.Count > 0 && spec.TopMarksCaption.Length > 0)
            sb.Append(Text(HistLeft - 8, HistTop - 12, 12, Sub, spec.TopMarksCaption, "end"));
        foreach (var mk in spec.TopMarks)
        {
            if (mk.Value < spec.Start || mk.Value > end) continue;
            double x = X(mk.Value);
            if (x - lastMarkX < 26) continue;
            lastMarkX = x;
            sb.Append($"<line x1=\"{F(x)}\" y1=\"{F(HistTop - 6)}\" x2=\"{F(x)}\" y2=\"{F(HistBottom)}\" stroke=\"{MarkLine}\" stroke-width=\"1\" stroke-dasharray=\"2 3\"/>");
            string anchor = x < HistLeft + 40 ? "start" : x > HistRight - 40 ? "end" : "middle";
            sb.Append(Text(x, HistTop - 12, 12, Sub, mk.Label, anchor));
        }

        for (int i = 0; i < spec.BinCount; i++)
        {
            int n = total[i];
            if (n == 0) continue;
            int r = Math.Min(ranked[i], n);
            double x = HistLeft + barWidth * i + gap / 2;
            double w = barWidth - gap;
            // 1 話しか無い棒も見えるよう、最低の高さを持たせる。
            double h = Math.Max(H(n), 2.5);
            double hr = r == 0 ? 0 : r == n ? h : Math.Max(H(r), 2.5);
            sb.Append("<g><title>").Append(Esc(r > 0
                ? $"{spec.BinLabel(i)}：{n} 話（うちランキング {r} 話）"
                : $"{spec.BinLabel(i)}：{n} 話")).Append("</title>");
            if (h - hr > 0)
                sb.Append($"<rect x=\"{F(x)}\" y=\"{F(HistBottom - h)}\" width=\"{F(w)}\" height=\"{F(h - hr)}\" fill=\"{Bar}\"/>");
            if (hr > 0)
                sb.Append($"<rect x=\"{F(x)}\" y=\"{F(HistBottom - hr)}\" width=\"{F(w)}\" height=\"{F(hr)}\" fill=\"{Accent}\"/>");
            sb.Append("</g>");
        }

        sb.Append(Line(HistLeft, HistBottom, HistRight, HistBottom, Ink, 1));
        foreach (var t in spec.Ticks)
        {
            if (t.Value < spec.Start - 1e-9 || t.Value > end + 1e-9) continue;
            double x = X(t.Value);
            sb.Append(Line(x, HistBottom, x, HistBottom + 5, Ink, 1));
            string anchor = x < HistLeft + 16 ? "start" : x > HistRight - 16 ? "end" : "middle";
            sb.Append(Text(x, HistBottom + 22, 12, Sub, t.Label, anchor));
        }

        if (spec.Cut is { } cut)
        {
            double x = Math.Clamp(X(cut.Value), HistLeft, HistRight);
            sb.Append($"<line x1=\"{F(x)}\" y1=\"{F(HistTop)}\" x2=\"{F(x)}\" y2=\"{F(HistBottom)}\" stroke=\"{Red}\" stroke-width=\"1.5\" stroke-dasharray=\"5 3\"/>");
            bool left = x > (HistLeft + HistRight) / 2;
            sb.Append($"<text x=\"{F(left ? x - 8 : x + 8)}\" y=\"{F(HistTop + 14)}\" font-size=\"13\" font-weight=\"700\" fill=\"{Red}\" stroke=\"{Paper}\" stroke-width=\"4\" paint-order=\"stroke\" text-anchor=\"{(left ? "end" : "start")}\">{Esc(cut.Label)}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>1 作品ぶんの五数要約・平均値・最頻値。四分位数は、順位を線形に補間して求める。</summary>
    private sealed record BoxStats(int Min, double Q1, double Median, double Q3, int Max, double Mean, IReadOnlyList<int> Modes)
    {
        public static BoxStats From(IReadOnlyList<int> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            double Quantile(double p)
            {
                double pos = (sorted.Count - 1) * p;
                int lo = (int)Math.Floor(pos);
                int hi = Math.Min(lo + 1, sorted.Count - 1);
                return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
            }
            var counts = sorted.GroupBy(v => v).Select(g => (Value: g.Key, Count: g.Count())).ToList();
            int maxCount = counts.Max(c => c.Count);
            return new BoxStats(
                sorted[0], Quantile(0.25), Quantile(0.5), Quantile(0.75), sorted[^1],
                sorted.Average(),
                counts.Where(c => c.Count == maxCount).Select(c => c.Value).OrderBy(v => v).ToList());
        }
    }

    /// <param name="top">図の上端（viewBox の単位）。ここより上は描かない。</param>
    private static void OpenSvg(StringBuilder sb, string ariaLabel, double top = 0)
        => sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 {F(top)} {F(Width)} {F(Height - top)}\" role=\"img\" aria-label=\"{Esc(ariaLabel)}\">");

    private static void CloseSvg(StringBuilder sb)
    {
        sb.Append(Text(PlotRight, PlotBottom + 50, 13, Sub, "放送開始年", "end"));
        sb.Append("</svg>");
    }

    /// <summary>横軸の年（下 2 桁）。</summary>
    private static string YearLabel(double x, int year)
        => Text(x, PlotBottom + 24, 13, Sub, (year % 100).ToString("00", CultureInfo.InvariantCulture), "middle");

    /// <summary>印に添える数値。線や箱に重なっても読めるよう、地の色でふちを取る。</summary>
    private static string ValueLabel(double x, double y, string text, bool highlight)
        => highlight
            ? $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"12\" font-weight=\"700\" fill=\"{Red}\" stroke=\"{Paper}\" stroke-width=\"3\" paint-order=\"stroke\" text-anchor=\"middle\">{Esc(text)}</text>"
            : $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"11\" fill=\"{Sub}\" stroke=\"{Paper}\" stroke-width=\"3\" paint-order=\"stroke\" text-anchor=\"middle\">{Esc(text)}</text>";

    private static string Line(double x1, double y1, double x2, double y2, string stroke, double width)
        => $"<line x1=\"{F(x1)}\" y1=\"{F(y1)}\" x2=\"{F(x2)}\" y2=\"{F(y2)}\" stroke=\"{stroke}\" stroke-width=\"{F(width)}\"/>";

    private static string Circle(double cx, double cy, string fill, string stroke)
        => $"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"3.5\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"1.5\"/>";

    private static string Text(double x, double y, int size, string fill, string text, string? anchor = null)
        => $"<text x=\"{F(x)}\" y=\"{F(y)}\" font-size=\"{size}\" fill=\"{fill}\"{(anchor is null ? "" : $" text-anchor=\"{anchor}\"")}>{Esc(text)}</text>";

    /// <summary>座標の書式（小数第 1 位まで。末尾の 0 は書かない）。</summary>
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>値の書式（整数ならそのまま、端数があれば小数第 1 位まで）。</summary>
    private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Esc(string s) => WebUtility.HtmlEncode(s);
}
