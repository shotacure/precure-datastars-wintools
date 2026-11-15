using PrecureDataStars.Data.Models;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// テロップの装飾を画素で重ねて描く（書体の見本用）。字の形（親字と振り仮名の塗りの濃さ、0〜1）を受け取り、
/// 装飾（<see cref="TelopDecoration"/>）の層を下から順に重ねて、背景透過の画像を作る。
/// <para>
/// 輪郭の線で描く描き方（サブタイトルのテロップ）では表せない、面取り・フチの中のグラデーション・層や塗りのグラデーション・
/// 字ごとの色・内側の光・斜線・光彩・塗りの太らせを描くためのもの。部品の計算は、放送の画面と比べて体裁を測ったときの描き方と同じ：
/// </para>
/// <list type="bullet">
///   <item>太らせ：字の形からの距離が太さ以内の画素を塗る（縁は 1 画素ぶん濃さを落とす）。</item>
///   <item>ぼかし：ガウスぼかし（標準偏差）。ずらし：端数のずれは隣り合う画素の濃さを線形に混ぜる。</item>
///   <item>面取り：外形をぼかした山の傾きと光の向きの内積で、光に面した側を明るく、反対側を暗く塗る。</item>
///   <item>グラデーション：行の字の範囲（濃さ 0.5 を超える画素の位置の 1〜99%）に沿って、色の位置の間を線形に移す。</item>
/// </list>
/// 太さ・ずれ・ぼかしは字の大きさに対する比で持ち、描くときに字の大きさ（画素）をかける。
/// </summary>
internal static class TelopRaster
{
    /// <summary>
    /// 太らせ・細らせの距離を測る細かい面の倍率。字の形をこの倍の細かさでも塗っておき、距離はそちらで測って平均して戻す
    /// （等倍の画素で測ると 1 画素未満の太さが丸められるため）。
    /// </summary>
    public const int FineScale = 4;

    /// <summary>
    /// 装飾を重ねる面の、出力の画像に対する倍率。字の形と装飾をこの倍の大きさで重ねてから、出力の大きさへ平均して縮める
    /// （斜線・帯・キラキラのくっきりした境目や、太らせた縁の 1 画素の段がぎざぎざに見えないように）。
    /// </summary>
    public const int SuperSample = 2;

    /// <summary>
    /// 装飾を重ねた画像を作る。<paramref name="baseMask"/>・<paramref name="rubyMask"/> は親字・振り仮名の塗りの濃さ（幅 × 高さ、行優先）、
    /// <paramref name="charCenters"/> は親字の 1 字ずつの中心の x（字ごとの色を割り当てる）、<paramref name="size"/> は親字の字の大きさ（画素）。
    /// <paramref name="fineMask"/> は親字と振り仮名を合わせた字の形を <paramref name="fineScale"/> 倍の細かさで塗った濃さ（無ければ等倍の画素で距離を測る）。
    /// </summary>
    public static SKBitmap Compose(int width, int height, float[] baseMask, float[] rubyMask, IReadOnlyList<float> charCenters,
        TelopDecoration d, float size, float[]? fineMask = null, int fineScale = FineScale)
    {
        int n = width * height;
        var fm = new float[n];
        for (int i = 0; i < n; i++) fm[i] = Math.Max(baseMask[i], rubyMask[i]);
        var plane = new Plane(width, height);
        if (fineMask is not null) plane.SetFine(fm, fineMask, fineScale);
        var outR = new float[n];
        var outG = new float[n];
        var outB = new float[n];
        var outA = new float[n];

        void Over(float[] alpha, Func<int, (float R, float G, float B)> color)
        {
            for (int i = 0; i < n; i++)
            {
                float a = alpha[i];
                if (a <= 0f) continue;
                var (r, g, b) = color(i);
                float k = 1f - a;
                outR[i] = r * a + outR[i] * k;
                outG[i] = g * a + outG[i] * k;
                outB[i] = b * a + outB[i] * k;
                outA[i] = a + outA[i] * k;
            }
        }

        // 行の字の範囲（グラデーションの両端）
        var rangeV = InkRange(fm, width, height, vertical: true);
        var rangeH = InkRange(fm, width, height, vertical: false);
        // 行ごとの縦の範囲（親字のある行の帯）。行ごとのグラデーションに使う。行と行のあいだの段（振り仮名）は近い方の行に含める
        // 振り仮名だけを描くとき（親字の形が空）は、振り仮名の形から行の段を決める（行ごとのグラデーションを振り仮名の行ごとにかける）
        var lineBands = LineBands(baseMask.Any(v => v > 0.5f) ? baseMask : rubyMask, width, height, size);
        var rowBand = new int[height];
        for (int y = 0; y < height; y++)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int b = 0; b < lineBands.Count; b++)
            {
                var (blo, bhi) = lineBands[b];
                float dd = y < blo ? blo - y : y > bhi ? y - bhi : 0f;
                if (dd < bestD) { bestD = dd; best = b; }
            }
            rowBand[y] = best;
        }
        // 行ごとの横の範囲（親字の左端から右端まで）。横のグラデーションを行ごとに移すときに使う
        var lineXRanges = new (float Lo, float Hi)[lineBands.Count];
        for (int b = 0; b < lineBands.Count; b++)
        {
            int xl = width, xr = -1;
            for (int y = (int)lineBands[b].Lo; y <= (int)lineBands[b].Hi && y < height; y++)
                for (int x = 0, i = y * width; x < width; x++, i++)
                    if (baseMask[i] > 0.5f) { if (x < xl) xl = x; if (x > xr) xr = x; }
            lineXRanges[b] = xr >= xl ? (xl, xr) : rangeH;
        }
        Func<int, (float, float, float)> ColorField(string? hex, TelopGradient? gradient)
        {
            if (gradient is { Colors.Count: > 0 })
            {
                bool vertical = !string.Equals(gradient.Axis, "h", StringComparison.OrdinalIgnoreCase);
                var (lo, hi) = vertical ? rangeV : rangeH;
                var stops = gradient.Colors.Select(Rgb).ToArray();
                // 行ごとの色の組（行によって色が違う作品）。色の数は Colors と同じにそろっている前提で、位置は共通
                var lineStops = gradient.LineColors is { Count: > 0 } lc
                    ? lc.Where(c => c.Count == stops.Length).Select(c => c.Select(Rgb).ToArray()).ToArray()
                    : null;
                var pos = gradient.Positions is { Count: > 0 } p && p.Count == stops.Length
                    ? p.ToArray()
                    : Enumerable.Range(0, stops.Length).Select(k => stops.Length == 1 ? 0f : k / (float)(stops.Length - 1)).ToArray();
                // 横のグラデーションの傾き：行の高さの中ほどからの縦のずれに応じて横の位置をずらす
                float slope = vertical ? 0f : MathF.Tan(gradient.Tilt * MathF.PI / 180f);
                float midY = (rangeV.Lo + rangeV.Hi) / 2f;
                if (gradient.Radial)
                {
                    // 環状：中心からの距離を半径で割って 0〜1。場面の画像では置く位置から求めた中心と半径（Line）、
                    // それ以外はこの画像の字の範囲の中心と、そこからいちばん遠い角までの距離に倍率を掛けた半径
                    float cx, cy, radius;
                    if (gradient.Line is { Count: 4 } rl)
                    {
                        cx = rl[0]; cy = rl[1];
                        radius = MathF.Sqrt((rl[2] - rl[0]) * (rl[2] - rl[0]) + (rl[3] - rl[1]) * (rl[3] - rl[1]));
                    }
                    else
                    {
                        cx = (rangeH.Lo + rangeH.Hi) / 2f; cy = (rangeV.Lo + rangeV.Hi) / 2f;
                        float hw = (rangeH.Hi - rangeH.Lo) / 2f, hh = (rangeV.Hi - rangeV.Lo) / 2f;
                        radius = MathF.Sqrt(hw * hw + hh * hh) * gradient.Radius;
                    }
                    radius = Math.Max(1f, radius);
                    return i =>
                    {
                        float ddx = i % width - cx, ddy = i / width - cy;
                        float t = Math.Clamp(MathF.Sqrt(ddx * ddx + ddy * ddy) / radius, 0f, 1f);
                        return Interpolate(stops, pos, t);
                    };
                }
                if (gradient.Angle is float axisDeg && gradient.Period > 0f)
                {
                    // 傾けた軸の繰り返し：字のある範囲の左上の角からの、軸の向きへの射影を繰り返しの長さで折り返す
                    double ar = axisDeg * Math.PI / 180.0;
                    float ac = (float)Math.Cos(ar), asn = (float)Math.Sin(ar);
                    float len = Math.Max(1f, gradient.Period * size), off = gradient.Offset * size;
                    bool mirror = gradient.Mirror;
                    return i =>
                    {
                        float p = ((i % width - rangeH.Lo) * ac + (i / width - rangeV.Lo) * asn - off) / len;
                        float t = p - MathF.Floor(p);
                        if (mirror) t = 1f - Math.Abs(2f * t - 1f);
                        return Interpolate(stops, pos, t);
                    };
                }
                if (gradient.Line is { Count: 4 } ln)
                {
                    // 画像の座標の線分への射影で色を移す（画面全体にかかるグラデーション）
                    float lx = ln[2] - ln[0], ly = ln[3] - ln[1], len2 = Math.Max(1f, lx * lx + ly * ly);
                    return i =>
                    {
                        float t = Math.Clamp(((i % width - ln[0]) * lx + (i / width - ln[1]) * ly) / len2, 0f, 1f);
                        return Interpolate(stops, pos, t);
                    };
                }
                return i =>
                {
                    float coord = vertical ? i / width : i % width + (i / width - midY) * slope;
                    float t;
                    if (!vertical && gradient.PerLine && lineBands.Count > 0 && gradient.Period <= 0f)
                    {
                        // 行ごと（横）：その行の親字の左端から右端までで 0〜1
                        var (xlo, xhi) = lineXRanges[rowBand[i / width]];
                        t = Math.Clamp((coord - xlo) / Math.Max(1f, xhi - xlo), 0f, 1f);
                    }
                    else if (vertical && gradient.PerLine && lineBands.Count > 0)
                    {
                        // 行ごと：その行の親字の上端から下端までで 0〜1
                        int band = rowBand[i / width];
                        var (blo, bhi) = lineBands[band];
                        t = Math.Clamp((coord - blo) / Math.Max(1f, bhi - blo), 0f, 1f);
                        if (lineStops is { Length: > 0 }) return Interpolate(lineStops[band % lineStops.Length], pos, t);
                    }
                    else if (gradient.Period > 0f)
                    {
                        // 繰り返し：行の字の端から、字の大きさに比した長さごとに 0〜1 を回す
                        float len = Math.Max(1f, gradient.Period * size);
                        t = (coord - lo) / len;
                        t -= MathF.Floor(t);
                    }
                    else
                    {
                        t = Math.Clamp((coord - lo) / Math.Max(1f, hi - lo), 0f, 1f);
                    }
                    return Interpolate(stops, pos, t);
                };
            }
            var c = Rgb(hex ?? "#000000");
            return _ => c;
        }

        float Px(float ratio) => ratio * size;
        float outerWidth = d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindOutline).Select(l => Px(l.Width)).DefaultIfEmpty(0f).Max();

        foreach (var layer in d.Layers)
        {
            if (layer.Kind == TelopDecorationLayer.KindBevel)
            {
                if (layer.OverFill) continue;
                // 面取りをかける形：spread があれば字をその幅だけ太らせた形（内側のフチだけの面取り）、無ければいちばん外のフチの形
                float bevelShape = layer.Spread is float bs ? Px(bs) : outerWidth;
                var (hi, sh) = plane.Bevel(fm, bevelShape, Px(layer.Width), layer.Angle, layer.Strength, Px(layer.Noise ?? 0f), Px(layer.NoiseScale ?? 0.02f), layer.Chamfer);
                Scale(hi, layer.Opacity);
                Scale(sh, layer.ShadeOpacity ?? layer.Opacity);
                var hc = Rgb(layer.Highlight ?? "#FFFFFF");
                var sc = Rgb(layer.Shade ?? "#000000");
                Over(hi, _ => hc);
                Over(sh, _ => sc);
                continue;
            }
            float[] a;
            var color = ColorField(layer.Color, layer.Gradient);
            if (layer.Kind == TelopDecorationLayer.KindOutline)
            {
                a = plane.Grow(fm, Px(layer.Width));
                if (layer.Blur > 0f) a = Clip(plane.Blur(a, Px(layer.Blur)));
                if (layer.InnerColor is not null && layer.Width > 0f)
                {
                    // フチの中のグラデーション：字の輪郭からの距離 t（0＝輪郭、1＝フチの外の縁）で内側の色から外側の色へ
                    var dist = plane.DistanceFromGlyph(fm);
                    var inner = Rgb(layer.InnerColor);
                    float w = Px(layer.Width);
                    var outer = color;
                    color = i =>
                    {
                        float t = Math.Clamp(dist[i] / w, 0f, 1f);
                        var o = outer(i);
                        return (inner.R * (1 - t) + o.Item1 * t, inner.G * (1 - t) + o.Item2 * t, inner.B * (1 - t) + o.Item3 * t);
                    };
                }
            }
            else
            {
                float spread = layer.Spread is float s ? Px(s) : outerWidth;
                a = spread > 0f ? plane.Grow(fm, spread) : (float[])fm.Clone();
                // 左右だけ余分に広げる（横に長い影・帯）：横方向の最大値で広げる
                if (layer.SpreadX is float sx && Px(sx) > 0f) a = WidenX(a, width, height, Px(sx));
                // 外周の輪だけ：同じ形を輪の幅だけ細らせた形（太らせる幅を減らし、左右の広げも同じだけ減らす）を抜く
                if (layer.Ring is float ring && Px(ring) > 0f)
                {
                    float innerSpread = spread - Px(ring);
                    var inner = innerSpread > 0f ? plane.Grow(fm, innerSpread) : innerSpread < 0f ? plane.Shrink(fm, -innerSpread) : (float[])fm.Clone();
                    if (layer.SpreadX is float sx2 && Px(sx2) > 0f) inner = WidenX(inner, width, height, Px(sx2));
                    for (int i = 0; i < a.Length; i++) a[i] = Math.Max(0f, a[i] - inner[i]);
                }
                a = plane.Shift(a, Px(layer.Dx), Px(layer.Dy));
                if (layer.Blur > 0f) a = Clip(plane.Blur(a, Px(layer.Blur)));
            }
            Scale(a, layer.Opacity);
            Over(a, color);
        }

        // フチの上・字の下の光彩（形は字と不透明なフチ）
        if (d.Glow is { BehindFill: true } g1)
        {
            float ow = d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindOutline && l.Opacity >= 1f).Select(l => Px(l.Width)).DefaultIfEmpty(0f).Max();
            var ag = Clip(plane.Blur(plane.Grow(fm, ow + Px(g1.Width)), Px(g1.Blur)));
            Scale(ag, g1.Opacity);
            var gc = Rgb(g1.Color);
            Over(ag, _ => gc);
        }

        // 字の塗り：字ごとの色 → 塗りのグラデーション → 1 色。振り仮名だけの色があれば振り仮名の画素はその色。
        Func<int, (float, float, float)> fill;
        if (d.FillChars is { Count: > 0 } chars && charCenters.Count > 0)
        {
            var colors = chars.Select(Rgb).ToArray();
            var centers = charCenters.ToArray();
            var byColumn = new (float, float, float)[width];
            for (int x = 0; x < width; x++)
            {
                int best = 0;
                for (int k = 1; k < centers.Length; k++)
                    if (Math.Abs(centers[k] - x) < Math.Abs(centers[best] - x)) best = k;
                byColumn[x] = colors[best % colors.Length];
            }
            fill = i => byColumn[i % width];
        }
        else
        {
            fill = ColorField(d.Fill, d.FillGradient);
        }
        if (d.RubyFill is not null)
        {
            var rc = Rgb(d.RubyFill);
            var baseFill = fill;
            fill = i => rubyMask[i] > baseMask[i] ? rc : baseFill(i);
        }
        if (d.Stripes is { Period: > 0f } st)
        {
            double t = (st.Angle + 90.0) * Math.PI / 180.0;
            float cos = (float)Math.Cos(t), sin = (float)Math.Sin(t);
            float period = Px(st.Period), lineWidth = Px(st.Width), offset = Px(st.Offset);
            var sc = Rgb(st.Color);
            var baseFill = fill;
            bool cross = st.Cross;
            // 線に直交する向きの位置を周期で折り返し、線の太さの内側なら線の色（格子なら直交する向きも見る）
            bool OnLine(float p)
            {
                p -= offset;
                p -= period * MathF.Floor(p / period);
                return p < lineWidth;
            }
            // 線の下の半透明の帯（チェック模様）：向きごとに帯の内側なら帯の色を重ねる（重なる所は 2 回）
            var bands = st.Bands is { Period: > 0f } bd ? bd : null;
            float bPeriod = bands is null ? 0f : Px(bands.Period), bWidth = bands is null ? 0f : Px(bands.Width), bOffset = bands is null ? 0f : Px(bands.Offset);
            var bc = Rgb(bands?.Color ?? "#000000");
            float bOpacity = bands?.Opacity ?? 0f;
            bool InBand(float p)
            {
                p -= bOffset;
                p -= bPeriod * MathF.Floor(p / bPeriod);
                return p < bWidth;
            }
            (float, float, float) Mix((float R, float G, float B) c, (float R, float G, float B) o, float a) =>
                (c.R * (1 - a) + o.R * a, c.G * (1 - a) + o.G * a, c.B * (1 - a) + o.B * a);
            fill = i =>
            {
                float x = i % width, y = i / width;
                float u = x * cos + y * sin, v = -x * sin + y * cos;
                if (OnLine(u)) return sc;
                if (cross && OnLine(v)) return sc;
                var c = baseFill(i);
                if (bands is not null)
                {
                    if (InBand(u)) c = Mix(c, bc, bOpacity);
                    if (cross && InBand(v)) c = Mix(c, bc, bOpacity);
                }
                return c;
            };
        }
        if (d.Mottle is { Scale: > 0f, Opacity: > 0f } mo)
        {
            // まだら模様：粗い粒と細かい粒（半分の間隔）の濃淡を 2:1 で重ね、コントラストをかけて、その割合だけ模様の色を混ぜる
            float cell = Px(mo.Scale);
            var mc = Rgb(mo.Color);
            var baseFill = fill;
            fill = i =>
            {
                float x = i % width / cell, y = i / width / cell;
                float nz = (2f * ValueNoise(x, y, 11) + ValueNoise(x * 2f, y * 2f, 23)) / 3f;
                float a = mo.Opacity * Math.Clamp((nz - 0.5f) * mo.Contrast + 0.5f, 0f, 1f);
                var c = baseFill(i);
                return (c.Item1 * (1 - a) + mc.R * a, c.Item2 * (1 - a) + mc.G * a, c.Item3 * (1 - a) + mc.B * a);
            };
        }
        var fillAlpha = d.FillGrow > 0f ? plane.Grow(fm, Px(d.FillGrow)) : d.FillGrow < 0f ? plane.Shrink(fm, Px(-d.FillGrow)) : fm;
        Over(fillAlpha, fill);

        // 塗りの上のキラキラ：格子の点ごとに決まった種でずらした 4 つの角の星（アステロイド形 |x|^½+|y|^½≦1）を、傾けて縦長に置く
        if (d.Sparkles is { Period: > 0f, Size: > 0f } spk)
        {
            float period = Px(spk.Period), h = Px(spk.Size) / 2f, w = h * Math.Max(0.05f, spk.Aspect);
            double th = spk.Angle * Math.PI / 180.0;
            float ca = (float)Math.Cos(th), sa = (float)Math.Sin(th);
            var spc = Rgb(spk.Color);
            static float Hash(int a, int b, int k) => Hash01(a, b, k);
            // 星を置く縦の範囲（行の親字の上端 0 〜 下端 1）
            bool InRange(float py)
            {
                if (spk.VerticalRange is not { Count: 2 } vr || lineBands.Count == 0) return true;
                var (blo, bhi) = lineBands[rowBand[Math.Clamp((int)py, 0, height - 1)]];
                float rel = (py - blo) / Math.Max(1f, bhi - blo);
                return rel >= vr[0] && rel <= vr[1];
            }
            var star = new float[n];
            int reach = (int)Math.Ceiling(h * (1 + spk.SizeJitter) / period) + 1;
            for (int i = 0; i < n; i++)
            {
                if (fillAlpha[i] <= 0f) continue;
                float x = i % width, y = i / width;
                int gx = (int)MathF.Floor(x / period), gy = (int)MathF.Floor(y / period);
                float best = 0f;
                for (int oy = -reach; oy <= reach; oy++)
                for (int ox = -reach; ox <= reach; ox++)
                {
                    int cx = gx + ox, cy = gy + oy;
                    float px = (cx + 0.5f + (Hash(cx, cy, 1) - 0.5f) * 2f * spk.Jitter) * period;
                    float py = (cy + 0.5f + (Hash(cx, cy, 2) - 0.5f) * 2f * spk.Jitter) * period;
                    if (Hash(cx, cy, 4) >= spk.Density || !InRange(py)) continue;
                    float k = 1f + (Hash(cx, cy, 3) - 0.5f) * 2f * spk.SizeJitter;
                    float dx = x - px, dy = y - py;
                    // 星ごとの縦横比と傾きのばらつき（決まった種）
                    float ws = w * (1f + (Hash(cx, cy, 5) - 0.5f) * 2f * spk.AspectJitter);
                    float sc = ca, ss = sa;
                    if (spk.AngleJitter > 0f)
                    {
                        double ta = th + (Hash(cx, cy, 6) - 0.5f) * 2f * spk.AngleJitter * Math.PI / 180.0;
                        sc = (float)Math.Cos(ta); ss = (float)Math.Sin(ta);
                    }
                    // 傾きを戻した座標（正の角度で上が右へ倒れる）
                    float u = dx * sc + dy * ss, v = -dx * ss + dy * sc;
                    float q = MathF.Sqrt(Math.Abs(u) / (ws * k)) + MathF.Sqrt(Math.Abs(v) / (h * k));
                    // 縁の 1 画素をなめらかに
                    float edge = Math.Clamp((1f - q) * Math.Min(ws, h) * k / 2f + 0.5f, 0f, 1f);
                    if (edge > best) best = edge;
                }
                star[i] = best * fillAlpha[i] * spk.Opacity;
            }
            Over(star, _ => spc);
        }

        // 字の上の面取り（字そのものの浮き彫り）
        foreach (var layer in d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindBevel && l.OverFill))
        {
            var (hi, sh) = plane.Bevel(fillAlpha, 0f, Px(layer.Width), layer.Angle, layer.Strength, Px(layer.Noise ?? 0f), Px(layer.NoiseScale ?? 0.02f), layer.Chamfer);
            for (int i = 0; i < n; i++)
            {
                hi[i] *= fillAlpha[i] * layer.Opacity;
                sh[i] *= fillAlpha[i] * (layer.ShadeOpacity ?? layer.Opacity);
            }
            var hc = Rgb(layer.Highlight ?? "#FFFFFF");
            var sc = Rgb(layer.Shade ?? "#000000");
            if (layer.Additive)
            {
                // 光として足す：濃さを掛けた色に、光の色 × 明るさを足す（字の濃さを超えない）
                for (int i = 0; i < n; i++)
                {
                    float v = hi[i];
                    if (v <= 0f) continue;
                    outR[i] = Math.Min(outA[i], outR[i] + hc.R * v);
                    outG[i] = Math.Min(outA[i], outG[i] + hc.G * v);
                    outB[i] = Math.Min(outA[i], outB[i] + hc.B * v);
                }
            }
            else
            {
                Over(hi, _ => hc);
            }
            if (layer.ShadeFromFill is float darken)
            {
                // 暗い側は下の塗りの色を暗くした色（塗りのグラデーションに沿う）
                float keep = 1f - Math.Clamp(darken, 0f, 1f);
                Over(sh, i => { var c = fill(i); return (c.Item1 * keep, c.Item2 * keep, c.Item3 * keep); });
            }
            else
            {
                Over(sh, _ => sc);
            }
        }

        // 内側の光：字の形をぼかし、輪郭に近いところほど濃く
        if (d.InnerGlow is { } ig)
        {
            var b = Clip(plane.Blur(fillAlpha, Px(ig.Blur)));
            var ai = new float[n];
            for (int i = 0; i < n; i++) ai[i] = fillAlpha[i] * Math.Clamp(2f * (1f - b[i]), 0f, 1f);
            var ic = Rgb(ig.Color);
            Over(ai, _ => ic);
        }

        // いちばん上の光彩（形は字・フチ・影のすべて）
        if (d.Glow is { BehindFill: false } g2)
        {
            var shape = (float[])fm.Clone();
            foreach (var layer in d.Layers)
            {
                float[]? s = null;
                if (layer.Kind == TelopDecorationLayer.KindShadow)
                {
                    float spread = layer.Spread is float sp ? Px(sp) : outerWidth;
                    s = plane.Shift(spread > 0f ? plane.Grow(fm, spread) : fm, Px(layer.Dx), Px(layer.Dy));
                }
                else if (layer.Kind == TelopDecorationLayer.KindOutline && layer.Opacity >= 1f)
                {
                    s = plane.Grow(fm, Px(layer.Width));
                }
                if (s is null) continue;
                for (int i = 0; i < n; i++) shape[i] = Math.Max(shape[i], s[i]);
            }
            var ag = Clip(plane.Blur(g2.Width > 0f ? plane.Grow(shape, Px(g2.Width)) : shape, Px(g2.Blur)));
            Scale(ag, g2.Opacity);
            var gc = Rgb(g2.Color);
            Over(ag, _ => gc);
        }

        // テロップ全体のやわらかさ：重ね終えた色（濃さを掛けた色）と濃さをまとめてぼかす
        if (d.Blur > 0f)
        {
            float sigma = Px(d.Blur);
            outR = plane.Blur(outR, sigma);
            outG = plane.Blur(outG, sigma);
            outB = plane.Blur(outB, sigma);
            outA = plane.Blur(outA, sigma);
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var pixels = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            pixels[i * 4] = ToByte(outR[i]);
            pixels[i * 4 + 1] = ToByte(outG[i]);
            pixels[i * 4 + 2] = ToByte(outB[i]);
            pixels[i * 4 + 3] = ToByte(outA[i]);
        }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    /// <summary>格子の点 (<paramref name="a"/>, <paramref name="b"/>) と種 <paramref name="k"/> から決まる 0〜1 の値（模様の乱れ）。</summary>
    private static float Hash01(int a, int b, int k)
    {
        uint z = (uint)(a * 73856093 ^ b * 19349663 ^ k * 83492791);
        z ^= z >> 15; z *= 2246822519u; z ^= z >> 13; z *= 3266489917u; z ^= z >> 16;
        return (z & 0xFFFFFF) / (float)0xFFFFFF;
    }

    /// <summary>格子の点の乱れ（<see cref="Hash01"/>）を、格子の間でなめらかにつないだ 0〜1 の濃淡（格子の間隔 1 の座標）。</summary>
    private static float ValueNoise(float x, float y, int seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash01(x0, y0, seed), b = Hash01(x0 + 1, y0, seed), c = Hash01(x0, y0 + 1, seed), e = Hash01(x0 + 1, y0 + 1, seed);
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + e * fx) * fy;
    }

    /// <summary>
    /// 濃さを掛けた色の画像を、縦横 <paramref name="k"/> 画素ずつの平均で 1/<paramref name="k"/> の大きさに縮める
    /// （<see cref="SuperSample"/> 倍で重ねた装飾を出力の大きさに戻す）。
    /// </summary>
    public static SKBitmap Downsample(SKBitmap source, int k)
    {
        int w = source.Width / k, h = source.Height / k;
        var src = new byte[source.Width * source.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), src, 0, src.Length);
        var dst = new byte[w * h * 4];
        int sw = source.Width;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int acc = 0;
                    for (int sy = 0; sy < k; sy++)
                        for (int sx = 0; sx < k; sx++)
                            acc += src[((y * k + sy) * sw + x * k + sx) * 4 + c];
                    dst[(y * w + x) * 4 + c] = (byte)((acc + k * k / 2) / (k * k));
                }
            }
        }
        var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(dst, 0, bitmap.GetPixels(), dst.Length);
        return bitmap;
    }

    /// <summary>装飾の中の画像の画素の座標（画面全体にかかるグラデーションの <see cref="TelopGradient.Line"/>）を <paramref name="k"/> 倍にする。</summary>
    public static TelopDecoration ScalePixelCoordinates(TelopDecoration d, float k)
    {
        static TelopGradient? G(TelopGradient? g, float k) => g?.Line is { Count: 4 } ln ? g with { Line = ln.Select(v => v * k).ToList() } : g;
        return d with
        {
            FillGradient = G(d.FillGradient, k),
            Layers = d.Layers.Select(l => l with { Gradient = G(l.Gradient, k) }).ToList(),
            Ruby = d.Ruby is null ? null : ScalePixelCoordinates(d.Ruby, k)
        };
    }

    /// <summary>装飾の中の画像の画素の座標（画面全体にかかるグラデーションの <see cref="TelopGradient.Line"/>）を (<paramref name="dx"/>, <paramref name="dy"/>) だけずらす。</summary>
    public static TelopDecoration OffsetPixelCoordinates(TelopDecoration d, float dx, float dy)
    {
        static TelopGradient? G(TelopGradient? g, float dx, float dy) =>
            g?.Line is { Count: 4 } ln ? g with { Line = [ln[0] + dx, ln[1] + dy, ln[2] + dx, ln[3] + dy] } : g;
        return d with
        {
            FillGradient = G(d.FillGradient, dx, dy),
            Layers = d.Layers.Select(l => l with { Gradient = G(l.Gradient, dx, dy) }).ToList(),
            Ruby = d.Ruby is null ? null : OffsetPixelCoordinates(d.Ruby, dx, dy)
        };
    }

    /// <summary>装飾が字の外へ広がる幅（字の大きさに対する比）。画像の余白に使う。</summary>
    /// <summary>濃さの面を左右へ <paramref name="r"/> 画素だけ広げる（横方向の最大値。端の 1 画素未満は比で混ぜる）。</summary>
    private static float[] WidenX(float[] a, int width, int height, float r)
    {
        int whole = (int)MathF.Floor(r);
        float frac = r - whole;
        var o = new float[a.Length];
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float m = 0f;
                for (int k = -whole; k <= whole; k++)
                {
                    int xx = x + k;
                    if (xx >= 0 && xx < width && a[row + xx] > m) m = a[row + xx];
                }
                if (frac > 0f)
                {
                    float e = 0f;
                    if (x - whole - 1 >= 0) e = Math.Max(e, a[row + x - whole - 1]);
                    if (x + whole + 1 < width) e = Math.Max(e, a[row + x + whole + 1]);
                    m = Math.Max(m, e * frac);
                }
                o[row + x] = m;
            }
        }
        return o;
    }

    public static float Extent(TelopDecoration d)
    {
        float outer = d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindOutline).Select(l => l.Width).DefaultIfEmpty(0f).Max();
        float layers = d.Layers.Select(l => l.Kind switch
        {
            TelopDecorationLayer.KindOutline => l.Width + l.Blur * 3f,
            TelopDecorationLayer.KindShadow => (l.Spread ?? outer) + (l.SpreadX ?? 0f) + Math.Max(Math.Abs(l.Dx), Math.Abs(l.Dy)) + l.Blur * 3f,
            _ => outer
        }).DefaultIfEmpty(0f).Max();
        float glow = d.Glow is { } g ? layers + g.Width + g.Blur * 3f : 0f;
        return Math.Max(Math.Max(layers, glow), Math.Max(d.FillGrow, 0f)) + d.Blur * 3f;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static (float R, float G, float B) Rgb(string hex)
    {
        var c = SKColor.TryParse(hex, out var parsed) ? parsed : SKColors.Black;
        return (c.Red / 255f, c.Green / 255f, c.Blue / 255f);
    }

    private static (float, float, float) Interpolate((float R, float G, float B)[] stops, float[] pos, float t)
    {
        if (t <= pos[0]) return stops[0];
        for (int k = 1; k < stops.Length; k++)
        {
            if (t > pos[k]) continue;
            float u = (t - pos[k - 1]) / Math.Max(1e-6f, pos[k] - pos[k - 1]);
            var (a, b) = (stops[k - 1], stops[k]);
            return (a.R + (b.R - a.R) * u, a.G + (b.G - a.G) * u, a.B + (b.B - a.B) * u);
        }
        return stops[^1];
    }

    /// <summary>濃さ 0.5 を超える画素の位置の 1% 点と 99% 点（縦なら行、横なら列）。</summary>
    /// <summary>親字の形の行ごとの縦の帯（上端・下端の行）。字の大きさの 0.3 倍より狭い切れ目はつなぐ。</summary>
    private static List<(float Lo, float Hi)> LineBands(float[] mask, int width, int height, float size)
    {
        var bands = new List<(float, float)>();
        int start = -1, last = -1;
        for (int y = 0; y < height; y++)
        {
            bool ink = false;
            for (int x = 0, i = y * width; x < width; x++, i++)
                if (mask[i] > 0.5f) { ink = true; break; }
            if (!ink) continue;
            if (start >= 0 && y - last > size * 0.3f) { bands.Add((start, last)); start = -1; }
            if (start < 0) start = y;
            last = y;
        }
        if (start >= 0) bands.Add((start, last));
        return bands;
    }

    private static (float Lo, float Hi) InkRange(float[] fm, int width, int height, bool vertical)
    {
        int len = vertical ? height : width;
        var hist = new long[len];
        long total = 0;
        for (int i = 0; i < fm.Length; i++)
        {
            if (fm[i] <= 0.5f) continue;
            hist[vertical ? i / width : i % width]++;
            total++;
        }
        if (total == 0) return (0f, len);
        float Percentile(double q)
        {
            double target = q * (total - 1);
            long acc = 0;
            for (int k = 0; k < len; k++)
            {
                acc += hist[k];
                if (acc > target) return k;
            }
            return len - 1;
        }
        return (Percentile(0.01), Percentile(0.99));
    }

    private static void Scale(float[] a, float k)
    {
        if (k == 1f) return;
        for (int i = 0; i < a.Length; i++) a[i] *= k;
    }

    private static float[] Clip(float[] a)
    {
        for (int i = 0; i < a.Length; i++) a[i] = Math.Clamp(a[i], 0f, 1f);
        return a;
    }

    /// <summary>画像 1 枚ぶんの濃さの面の計算（太らせ・ぼかし・ずらし・距離・面取り）。</summary>
    private sealed class Plane(int width, int height)
    {
        private float[]? _distance;
        private float[]? _distanceSource;

        /// <summary>字の形（濃さ 0.5 超）からの距離（画素）。字の中は 0。</summary>
        public float[] DistanceFromGlyph(float[] a)
        {
            if (ReferenceEquals(_distanceSource, a) && _distance is not null) return _distance;
            var f = Distance(a, width, height);
            _distanceSource = a;
            _distance = f;
            return f;
        }

        /// <summary>幅 <paramref name="w"/> × 高さ <paramref name="h"/> の面で、濃さ 0.5 超の画素からのユークリッド距離（画素）。</summary>
        private static float[] Distance(float[] a, int w, int h)
        {
            const float inf = 1e12f;
            var f = new float[w * h];
            for (int i = 0; i < f.Length; i++) f[i] = a[i] > 0.5f ? 0f : inf;
            // 2 乗の距離を、横 → 縦の順に 1 次元の距離変換で求める（Felzenszwalb）
            var line = new float[Math.Max(w, h)];
            var result = new float[Math.Max(w, h)];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) line[x] = f[y * w + x];
                Transform(line, w, result);
                for (int x = 0; x < w; x++) f[y * w + x] = result[x];
            }
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) line[y] = f[y * w + x];
                Transform(line, h, result);
                for (int y = 0; y < h; y++) f[y * w + x] = result[y];
            }
            for (int i = 0; i < f.Length; i++) f[i] = MathF.Sqrt(f[i]);
            return f;
        }

        /// <summary>
        /// 字の形を <paramref name="k"/> 倍の細かさで塗った面を登録する。<paramref name="source"/>（等倍の字の形）を太らせる・細らせるときは、
        /// 細かい面で距離を測って太らせてから k × k 画素ずつ平均して戻すので、1 画素未満の太さもその値のまま効く。
        /// </summary>
        public void SetFine(float[] source, float[] fine, int k)
        {
            _fineSource = source;
            _fine = fine;
            _k = k;
        }

        private float[]? _fineSource;
        private float[]? _fine;
        private float[]? _fineInverse;
        private float[]? _fineDistance;
        private float[]? _fineInverseDistance;
        private int _k = 1;

        /// <summary>細かい面 <paramref name="a"/>（距離 <paramref name="dist"/> は細かい画素）を半径 <paramref name="r"/>（等倍の画素）だけ太らせて等倍に戻す。</summary>
        private float[] GrowFine(float[] a, float[] dist, float r)
        {
            int k = _k, fw = width * k;
            float rk = r * k;
            var o = new float[width * height];
            float inv = 1f / (k * k);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float acc = 0f;
                    for (int sy = 0; sy < k; sy++)
                    {
                        int row = (y * k + sy) * fw + x * k;
                        for (int sx = 0; sx < k; sx++)
                        {
                            int i = row + sx;
                            acc += Math.Max(a[i], Math.Clamp(rk + 0.5f - dist[i], 0f, 1f));
                        }
                    }
                    o[y * width + x] = acc * inv;
                }
            }
            return o;
        }

        private static void Transform(float[] f, int n, float[] d)
        {
            var v = new int[n];
            var z = new float[n + 1];
            int k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                    if (s <= z[k] && k > 0) { k--; continue; }
                    break;
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }

        /// <summary>半径 <paramref name="r"/> の円で太らせる（角は丸い）。</summary>
        public float[] Grow(float[] a, float r)
        {
            if (r <= 0f) return (float[])a.Clone();
            if (_fine is not null && ReferenceEquals(a, _fineSource))
            {
                _fineDistance ??= Distance(_fine, width * _k, height * _k);
                return GrowFine(_fine, _fineDistance, r);
            }
            var dist = DistanceFromGlyph(a);
            var o = new float[a.Length];
            for (int i = 0; i < o.Length; i++) o[i] = Math.Max(a[i], Math.Clamp(r + 0.5f - dist[i], 0f, 1f));
            return o;
        }

        /// <summary>半径 <paramref name="r"/> の円で細らせる（字の外側を太らせた形の裏返し）。</summary>
        public float[] Shrink(float[] a, float r)
        {
            if (r <= 0f) return (float[])a.Clone();
            if (_fine is not null && ReferenceEquals(a, _fineSource))
            {
                if (_fineInverse is null)
                {
                    _fineInverse = new float[_fine.Length];
                    for (int i = 0; i < _fine.Length; i++) _fineInverse[i] = 1f - _fine[i];
                    _fineInverseDistance = Distance(_fineInverse, width * _k, height * _k);
                }
                var gf = GrowFine(_fineInverse, _fineInverseDistance!, r);
                for (int i = 0; i < gf.Length; i++) gf[i] = 1f - gf[i];
                return gf;
            }
            var inv = new float[a.Length];
            for (int i = 0; i < inv.Length; i++) inv[i] = 1f - a[i];
            var g = Grow(inv, r);
            for (int i = 0; i < g.Length; i++) g[i] = 1f - g[i];
            return g;
        }

        /// <summary>ガウスぼかし（標準偏差 <paramref name="sigma"/> 画素、外は 0）。</summary>
        public float[] Blur(float[] a, float sigma)
        {
            if (sigma <= 0f) return (float[])a.Clone();
            int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3f));
            var kernel = new float[radius * 2 + 1];
            float sum = 0f;
            for (int k = -radius; k <= radius; k++) sum += kernel[k + radius] = MathF.Exp(-k * k / (2f * sigma * sigma));
            for (int k = 0; k < kernel.Length; k++) kernel[k] /= sum;
            var tmp = new float[a.Length];
            var o = new float[a.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float acc = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int xx = x + k;
                        if (xx >= 0 && xx < width) acc += a[row + xx] * kernel[k + radius];
                    }
                    tmp[row + x] = acc;
                }
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float acc = 0f;
                    for (int k = -radius; k <= radius; k++)
                    {
                        int yy = y + k;
                        if (yy >= 0 && yy < height) acc += tmp[yy * width + x] * kernel[k + radius];
                    }
                    o[y * width + x] = acc;
                }
            }
            return o;
        }

        /// <summary>右へ <paramref name="dx"/>・下へ <paramref name="dy"/> 画素ずらす（端数は隣の画素と線形に混ぜる）。</summary>
        public float[] Shift(float[] a, float dx, float dy)
        {
            if (dx == 0f && dy == 0f) return (float[])a.Clone();
            int x0 = (int)MathF.Floor(dx), y0 = (int)MathF.Floor(dy);
            float fx = dx - x0, fy = dy - y0;
            var o = new float[a.Length];
            float At(int x, int y) => x >= 0 && x < width && y >= 0 && y < height ? a[y * width + x] : 0f;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sx = x - x0, sy = y - y0;
                    o[y * width + x] = (1 - fx) * (1 - fy) * At(sx, sy) + fx * (1 - fy) * At(sx - 1, sy)
                                       + (1 - fx) * fy * At(sx, sy - 1) + fx * fy * At(sx - 1, sy - 1);
                }
            }
            return o;
        }

        /// <summary>
        /// 面取りの明るい側・暗い側の濃さ。<paramref name="outerWidth"/> だけ太らせた外形をぼかした山の傾きと、
        /// 光の向き（<paramref name="angleDeg"/>。0 が右、90 が下）の内積から決め、外形の内側だけに塗る。
        /// <paramref name="noise"/>（画素）が 0 より大きければ、山に <paramref name="noiseScale"/>（画素）でぼかした乱れを足し、面をでこぼこにする。
        /// <paramref name="chamfer"/> なら山を台形にする（外形の縁からの内向きの距離を <paramref name="bevelWidth"/> で割って 0〜1 に切る。斜面は輪郭に沿った帯になる）。
        /// </summary>
        public (float[] Highlight, float[] Shade) Bevel(float[] fm, float outerWidth, float bevelWidth, float angleDeg, float strength,
            float noise = 0f, float noiseScale = 0f, bool chamfer = false)
        {
            var shape = outerWidth > 0f ? Grow(fm, outerWidth) : (float[])fm.Clone();
            float[] h;
            if (chamfer)
            {
                // 台形：外形の外からの距離（＝縁からの深さ）を面取りの幅で 0〜1 に。縁の画素は半画素ぶん引いて、縁を 0 からの立ち上がりにする
                var outside = new float[shape.Length];
                for (int i = 0; i < shape.Length; i++) outside[i] = 1f - shape[i];
                var depth = Distance(outside, width, height);
                float w = Math.Max(bevelWidth, 0.5f);
                h = new float[shape.Length];
                for (int i = 0; i < h.Length; i++) h[i] = Math.Clamp((depth[i] - 0.5f) / w, 0f, 1f);
                // 距離の 1 画素刻みの段で明暗がざらつかないよう、斜面の高さを少しならす
                h = Blur(h, 0.75f);
            }
            else
            {
                h = Blur(shape, Math.Max(bevelWidth / 2f, 0.5f));
            }
            if (noise > 0f)
            {
                // 決まった種の乱れ（画素の座標から作る）をぼかし、標準偏差 1 にそろえて、山の高さの単位（0〜1 を bevelWidth で上る）に直して足す
                var r = new float[fm.Length];
                for (int i = 0; i < r.Length; i++)
                {
                    uint z = (uint)i * 2654435761u ^ 0x9E3779B9u;
                    z ^= z >> 15; z *= 2246822519u; z ^= z >> 13; z *= 3266489917u; z ^= z >> 16;
                    r[i] = (z & 0xFFFFFF) / (float)0xFFFFFF - 0.5f;
                }
                r = Blur(r, Math.Max(noiseScale, 0.5f));
                double sum = 0;
                for (int i = 0; i < r.Length; i++) sum += r[i] * r[i];
                float sd = (float)Math.Sqrt(sum / Math.Max(1, r.Length));
                float k = sd > 0f ? noise / Math.Max(bevelWidth, 0.5f) / sd : 0f;
                for (int i = 0; i < h.Length; i++) h[i] += r[i] * k * shape[i];
            }
            double th = angleDeg * Math.PI / 180.0;
            float lx = (float)Math.Cos(th), ly = (float)Math.Sin(th);
            var hi = new float[fm.Length];
            var sh = new float[fm.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (shape[i] <= 0.5f) continue;
                    float gx = x == 0 ? h[i + 1] - h[i] : x == width - 1 ? h[i] - h[i - 1] : (h[i + 1] - h[i - 1]) / 2f;
                    float gy = y == 0 ? h[i + width] - h[i] : y == height - 1 ? h[i] - h[i - width] : (h[i + width] - h[i - width]) / 2f;
                    float v = -(gx * lx + gy * ly) * strength * bevelWidth;
                    hi[i] = Math.Clamp(v, 0f, 1f);
                    sh[i] = Math.Clamp(-v, 0f, 1f);
                }
            }
            return (hi, sh);
        }
    }
}
