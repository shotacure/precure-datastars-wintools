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
    /// 装飾を重ねた画像を作る。<paramref name="baseMask"/>・<paramref name="rubyMask"/> は親字・振り仮名の塗りの濃さ（幅 × 高さ、行優先）、
    /// <paramref name="charCenters"/> は親字の 1 字ずつの中心の x（字ごとの色を割り当てる）、<paramref name="size"/> は親字の字の大きさ（画素）。
    /// </summary>
    public static SKBitmap Compose(int width, int height, float[] baseMask, float[] rubyMask, IReadOnlyList<float> charCenters,
        TelopDecoration d, float size)
    {
        int n = width * height;
        var fm = new float[n];
        for (int i = 0; i < n; i++) fm[i] = Math.Max(baseMask[i], rubyMask[i]);
        var plane = new Plane(width, height);
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
        var lineBands = LineBands(baseMask, width, height, size);
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
        Func<int, (float, float, float)> ColorField(string? hex, TelopGradient? gradient)
        {
            if (gradient is { Colors.Count: > 0 })
            {
                bool vertical = !string.Equals(gradient.Axis, "h", StringComparison.OrdinalIgnoreCase);
                var (lo, hi) = vertical ? rangeV : rangeH;
                var stops = gradient.Colors.Select(Rgb).ToArray();
                var pos = gradient.Positions is { Count: > 0 } p && p.Count == stops.Length
                    ? p.ToArray()
                    : Enumerable.Range(0, stops.Length).Select(k => stops.Length == 1 ? 0f : k / (float)(stops.Length - 1)).ToArray();
                // 横のグラデーションの傾き：行の高さの中ほどからの縦のずれに応じて横の位置をずらす
                float slope = vertical ? 0f : MathF.Tan(gradient.Tilt * MathF.PI / 180f);
                float midY = (rangeV.Lo + rangeV.Hi) / 2f;
                return i =>
                {
                    float coord = vertical ? i / width : i % width + (i / width - midY) * slope;
                    float t;
                    if (vertical && gradient.PerLine && lineBands.Count > 0)
                    {
                        // 行ごと：その行の親字の上端から下端までで 0〜1
                        var (blo, bhi) = lineBands[rowBand[i / width]];
                        t = Math.Clamp((coord - blo) / Math.Max(1f, bhi - blo), 0f, 1f);
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
                var (hi, sh) = plane.Bevel(fm, bevelShape, Px(layer.Width), layer.Angle, layer.Strength);
                Scale(hi, layer.Opacity);
                Scale(sh, layer.Opacity);
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
            fill = i =>
            {
                float p = (i % width) * cos + (i / width) * sin - offset;
                p -= period * MathF.Floor(p / period);
                return p < lineWidth ? sc : baseFill(i);
            };
        }
        var fillAlpha = d.FillGrow > 0f ? plane.Grow(fm, Px(d.FillGrow)) : fm;
        Over(fillAlpha, fill);

        // 字の上の面取り（字そのものの浮き彫り）
        foreach (var layer in d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindBevel && l.OverFill))
        {
            var (hi, sh) = plane.Bevel(fillAlpha, 0f, Px(layer.Width), layer.Angle, layer.Strength);
            for (int i = 0; i < n; i++)
            {
                hi[i] *= fillAlpha[i] * layer.Opacity;
                sh[i] *= fillAlpha[i] * layer.Opacity;
            }
            var hc = Rgb(layer.Highlight ?? "#FFFFFF");
            var sc = Rgb(layer.Shade ?? "#000000");
            Over(hi, _ => hc);
            Over(sh, _ => sc);
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

    /// <summary>装飾が字の外へ広がる幅（字の大きさに対する比）。画像の余白に使う。</summary>
    public static float Extent(TelopDecoration d)
    {
        float outer = d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindOutline).Select(l => l.Width).DefaultIfEmpty(0f).Max();
        float layers = d.Layers.Select(l => l.Kind switch
        {
            TelopDecorationLayer.KindOutline => l.Width + l.Blur * 3f,
            TelopDecorationLayer.KindShadow => (l.Spread ?? outer) + Math.Max(Math.Abs(l.Dx), Math.Abs(l.Dy)) + l.Blur * 3f,
            _ => outer
        }).DefaultIfEmpty(0f).Max();
        float glow = d.Glow is { } g ? layers + g.Width + g.Blur * 3f : 0f;
        return Math.Max(Math.Max(layers, glow), d.FillGrow);
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
            const float inf = 1e12f;
            var f = new float[width * height];
            for (int i = 0; i < f.Length; i++) f[i] = a[i] > 0.5f ? 0f : inf;
            // 2 乗の距離を、横 → 縦の順に 1 次元の距離変換で求める（Felzenszwalb）
            var line = new float[Math.Max(width, height)];
            var result = new float[Math.Max(width, height)];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) line[x] = f[y * width + x];
                Transform(line, width, result);
                for (int x = 0; x < width; x++) f[y * width + x] = result[x];
            }
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++) line[y] = f[y * width + x];
                Transform(line, height, result);
                for (int y = 0; y < height; y++) f[y * width + x] = result[y];
            }
            for (int i = 0; i < f.Length; i++) f[i] = MathF.Sqrt(f[i]);
            _distanceSource = a;
            _distance = f;
            return f;
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
            var dist = DistanceFromGlyph(a);
            var o = new float[a.Length];
            for (int i = 0; i < o.Length; i++) o[i] = Math.Max(a[i], Math.Clamp(r + 0.5f - dist[i], 0f, 1f));
            return o;
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
        /// </summary>
        public (float[] Highlight, float[] Shade) Bevel(float[] fm, float outerWidth, float bevelWidth, float angleDeg, float strength)
        {
            var shape = outerWidth > 0f ? Grow(fm, outerWidth) : (float[])fm.Clone();
            var h = Blur(shape, Math.Max(bevelWidth / 2f, 0.5f));
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
