using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PrecureDataStars.SiteBuilder.Utilities;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// エピソード詳細ページのサブタイトルを、本編のサブタイトルテロップの体裁（白い字・黒いフチ・右下の黒い影）で
/// 背景透過の PNG に描く。書体の解決・字詰め・フチの比率は OGP カードの見出しと共通。
/// <para>
/// 組み方：改行は <c>title_rich_html</c> の <c>&lt;br&gt;</c> のとおりにし、行ごとに中央揃えで積む。
/// 行が最大幅に収まらないときは、その行だけ長体をかける（下限 <see cref="TelopCondenseMin"/>）。
/// 書体の差し替え・斜体・振り仮名の大きさと高さは、作品ごとに <see cref="SubtitleTelopProfile"/> で持つ。
/// 下限でも収まらない行があるときは、全行の字をそろって小さくする。振り仮名は親字と同じ書体・同じ体裁で親字の上に載せ、
/// フチの太さと影のずれは親字と同じ幅にする（振り仮名の大きさに比例させると、細くて弱く見えるため）。
/// 画像は字の外形（フチ・影を含む）に余白を少し足した大きさで切り、ページ側は CSS で幅を最大 100% に抑えて縮める。
/// </para>
/// </summary>
public sealed partial class OgCardRenderer
{
    /// <summary>出力の倍率（CSS ピクセル 1 つに対する画素数）。高精細の画面でも字がにじまないよう 2 倍で描く。</summary>
    public const int TelopPixelRatio = 2;

    /// <summary>字の大きさ（画素）。ページ上で 1 字 45px に見える大きさ。</summary>
    private const float TelopFontSize = 45f * TelopPixelRatio;

    /// <summary>画像の最大幅（画素、余白込み）。PC のサブタイトル欄の内側の幅に収まる大きさ。</summary>
    private const float TelopMaxWidth = 860f * TelopPixelRatio;

    /// <summary>長体の下限。これでも行が収まらないときは、字そのものを小さくする。</summary>
    private const float TelopCondenseMin = 0.8f;

    /// <summary><c>&lt;small&gt;</c> で囲まれた字の大きさ（親字に対する比）。振り仮名の大きさは作品ごと（<see cref="SubtitleTelopProfile"/>）。</summary>
    private const float TelopSmallSizeRatio = 0.65f;

    /// <summary>行の中の空白が取る空き（字の大きさに対する比）。全角の空白は 1 字ぶん、半角の空白は半字ぶん。</summary>
    private const float TelopFullWidthGapEm = 1f;
    private const float TelopHalfWidthGapEm = 0.5f;

    /// <summary>縦の寸法（字の大きさに対する比）。字の上端・下端は em ボックスの 0.88 / 0.12 で見積もる。</summary>
    private const float TelopAscentRatio = 0.88f;
    private const float TelopDescentRatio = 0.12f;

    /// <summary>行と行のあいだの空きの既定（作品の組み方に指定が無いとき）。振り仮名が無い組では、上の行の影と下の行のフチが触れないよう広めに取る。</summary>
    private const float TelopLineGapWithRuby = 0.28f;
    private const float TelopLineGapWithoutRuby = 0.22f;

    /// <summary>画像の外周の余白。フチ（0.048）と影（0.037）が切れないだけの幅を取る。</summary>
    private const float TelopPaddingRatio = 0.1f;

    /// <summary>
    /// テロップの組版単位。<paramref name="Ruby"/> が空なら振り仮名を持たない地の文（続く地の文は 1 単位にまとめて字詰めを効かせる）。
    /// <paramref name="GapEm"/> が正なら字を描かない空き（元の空白）。
    /// </summary>
    private sealed record TelopUnit(string Base, string Ruby, bool Small, float GapEm)
    {
        public bool IsGap => GapEm > 0f;
    }

    /// <summary>
    /// 振り仮名の組み方（作品ごとの組み方から描画に使う値だけを取り出したもの）。
    /// <paramref name="SizeRatio"/>・<paramref name="RaiseRatio"/>・<paramref name="OverhangRatio"/> は親字の大きさに対する比、
    /// <paramref name="Skew"/> は斜体の傾き（tan）、<paramref name="OverhangLineStart"/>・<paramref name="OverhangLineEnd"/> は
    /// 行頭・行末の外へはみ出させるか、<paramref name="Grouping"/> は振り仮名の置き方。
    /// </summary>
    private sealed record TelopRubyLayout(
        float SizeRatio, float RaiseRatio, float Skew, float OverhangRatio, bool OverhangLineStart, bool OverhangLineEnd,
        SubtitleRubyGrouping Grouping);

    /// <summary>サブタイトルの HTML を字句に割る（ルビ・振り仮名・小さい字・改行のタグと、それ以外のタグ、地の文）。</summary>
    private static readonly Regex TelopTokenRegex =
        new(@"<(/?)(ruby|rt|small|br)\b[^>]*>|<[^>]*>|[^<]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// サブタイトルをテロップの体裁で描き、PNG に書き出す。描く字が無ければ何も書かずに null を返す。
    /// </summary>
    /// <param name="rubyHtml">ルビ付きのサブタイトル（<c>title_rich_html</c>）。</param>
    /// <param name="fontFamily">作品の本編テロップの書体名（<c>series.font_subtitle</c>）。空か見つからなければ既定の見出し書体。</param>
    /// <param name="profile">作品ごとの組み方（振り仮名の書体・斜体・大きさと高さ・はみ出し方、行間）。</param>
    /// <param name="outputFilePath">書き出し先の絶対パス（拡張子 .png）。</param>
    /// <returns>書き出した画像の大きさ（画素）。</returns>
    public (int Width, int Height)? RenderSubtitleTelop(string rubyHtml, string fontFamily, SubtitleTelopProfile profile, string outputFilePath)
    {
        var lines = ParseTelopLines(rubyHtml);
        if (lines.Count == 0) return null;

        // 書体が見つからないときの警告は、同じページの OGP カードが同じ書体名で出すのでここでは拾わない
        // （作品ごとの組み方で指定した振り仮名の書体は、見つからなければ親字と同じ書体になる）。
        var typeface = ResolveTypeface(fontFamily, _boldTypeface, out _);
        var rubyTypeface = profile.RubyFontFamily.Length > 0 ? ResolveTypeface(profile.RubyFontFamily, typeface, out _) : typeface;
        bool hasRuby = lines.Any(line => line.Any(u => u.Ruby.Length > 0));
        float rubyRatio = profile.RubySizeRatio;
        float rubyRaise = profile.RubyRaiseRatio;
        float rubySkew = (float)Math.Tan(profile.RubyObliqueDegrees * Math.PI / 180.0);
        var rubyLayout = new TelopRubyLayout(rubyRatio, rubyRaise, rubySkew,
            profile.RubyOverhangRatio ?? rubyRatio, profile.RubyOverhangLineStart, profile.RubyOverhangLineEnd, profile.RubyGrouping);

        using var baseFont = new SKFont(typeface, TelopFontSize);
        using var smallFont = new SKFont(typeface, TelopFontSize * TelopSmallSizeRatio);
        using var rubyFont = new SKFont(rubyTypeface, TelopFontSize * rubyRatio);

        // 振り仮名の段（振り仮名の上端から親字の上端までの高さ）と、行の高さ（いずれも字の大きさに対する比）。
        float rubyBandRatio = hasRuby ? rubyRaise + TelopAscentRatio * rubyRatio - TelopAscentRatio : 0f;
        float lineBlockRatio = rubyBandRatio + TelopAscentRatio + TelopDescentRatio;
        // 字の外の幅：外周の余白と、行の端の振り仮名を外へはみ出させる組み方ではそのはみ出し分（行は中央にそろえるので左右とも取る）。
        float edgeOverhang = hasRuby && (rubyLayout.OverhangLineStart || rubyLayout.OverhangLineEnd) ? rubyLayout.OverhangRatio : 0f;
        float OuterWidth(float s) => 2f * s * (TelopPaddingRatio + edgeOverhang);

        // 字の大きさを決める。いちばん長い行が長体の下限で収まらなければ、収まるところまで全行の字を小さくする。
        float size = TelopFontSize;
        SetTelopSize(baseFont, smallFont, size);
        float longest = lines.Max(line => TelopLineWidth(line, baseFont, smallFont, size, 1f));
        float room = TelopMaxWidth - OuterWidth(size);
        if (longest * TelopCondenseMin > room)
        {
            size *= room / (longest * TelopCondenseMin);
            SetTelopSize(baseFont, smallFont, size);
            room = TelopMaxWidth - OuterWidth(size);
        }

        // 行ごとの長体の率と、その率での行の幅。収まる行は等幅のまま。
        var scales = new float[lines.Count];
        var widths = new float[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            float natural = TelopLineWidth(lines[i], baseFont, smallFont, size, 1f);
            scales[i] = natural > room && natural > 0f ? room / natural : 1f;
            widths[i] = TelopLineWidth(lines[i], baseFont, smallFont, size, scales[i]);
        }

        float padding = size * TelopPaddingRatio;
        float rubyBand = size * rubyBandRatio;
        float lineBlock = size * lineBlockRatio;
        float pitch = lineBlock + size * (profile.LineGapRatio ?? (hasRuby ? TelopLineGapWithRuby : TelopLineGapWithoutRuby));

        // 縮めて表示したときに半端な画素が出ないよう、縦横とも倍率の倍数に切り上げる。
        int width = RoundUpToMultiple(widths.Max() + OuterWidth(size), TelopPixelRatio);
        int height = RoundUpToMultiple(padding * 2f + pitch * (lines.Count - 1) + lineBlock, TelopPixelRatio);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        for (int i = 0; i < lines.Count; i++)
        {
            float baseline = padding + pitch * i + rubyBand + size * TelopAscentRatio;
            float x = (width - widths[i]) / 2f;
            DrawTelopLine(canvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], rubyLayout);
        }

        PathUtil.EnsureParentDirectory(outputFilePath);
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(outputFilePath))
        {
            data.SaveTo(stream);
        }
        return (width, height);
    }

    private static int RoundUpToMultiple(float value, int multiple) =>
        (int)Math.Ceiling(value / multiple) * multiple;

    private static void SetTelopSize(SKFont baseFont, SKFont smallFont, float size)
    {
        baseFont.Size = size;
        smallFont.Size = size * TelopSmallSizeRatio;
    }

    /// <summary>行の幅（長体の率 <paramref name="scaleX"/> をかけたとき）。振り仮名は親字の幅に収めるので行の幅には効かない。</summary>
    private float TelopLineWidth(IReadOnlyList<TelopUnit> line, SKFont baseFont, SKFont smallFont, float size, float scaleX)
    {
        baseFont.ScaleX = scaleX;
        smallFont.ScaleX = scaleX;
        float width = 0f;
        foreach (var unit in line)
            width += unit.IsGap ? unit.GapEm * size * scaleX : Measure(unit.Small ? smallFont : baseFont, unit.Base);
        return width;
    }

    /// <summary>
    /// 1 行を描く。影 → フチ → 白い字の順に、行の全単位をそれぞれ重ねる（単位ごとに 3 つを重ねると、
    /// 後の単位のフチが前の単位の白い字に食い込むため）。振り仮名は親字のあとに、同じ順で重ねる。
    /// </summary>
    private void DrawTelopLine(
        SKCanvas canvas, IReadOnlyList<TelopUnit> line, float x, float baseline,
        SKFont baseFont, SKFont smallFont, SKFont rubyFont, float size, float scaleX, TelopRubyLayout rubyLayout)
    {
        baseFont.ScaleX = scaleX;
        smallFont.ScaleX = scaleX;

        // 各単位の起点と占有幅を先に決める。
        var placed = new List<(TelopUnit Unit, SKFont Font, float X, float Slot, bool AfterGap)>();
        float cursor = x;
        bool afterGap = false;
        foreach (var unit in line)
        {
            if (unit.IsGap)
            {
                cursor += unit.GapEm * size * scaleX;
                afterGap = true;
                continue;
            }
            var font = unit.Small ? smallFont : baseFont;
            float slot = Measure(font, unit.Base);
            placed.Add((unit, font, cursor, slot, afterGap));
            cursor += slot;
            afterGap = false;
        }

        using var stroke = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };
        using var black = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };

        // 影（フチの外形ごと右下へずらした黒）。
        foreach (var (unit, font, ux, _, _) in placed)
        {
            float d = font.Size * TelopShadowRatio;
            // 線は輪郭を中心に引かれるので、見せたいフチの太さの 2 倍にする。
            stroke.StrokeWidth = font.Size * TelopOutlineRatio * 2f;
            ShaperFor(font).Draw(canvas, unit.Base, ux + d, baseline + d, SKTextAlign.Left, font, black, stroke);
        }
        // フチ。
        foreach (var (unit, font, ux, _, _) in placed)
        {
            stroke.StrokeWidth = font.Size * TelopOutlineRatio * 2f;
            ShaperFor(font).Draw(canvas, unit.Base, ux, baseline, SKTextAlign.Left, font, stroke);
        }
        // 白い字。
        foreach (var (unit, font, ux, _, _) in placed)
            ShaperFor(font).Draw(canvas, unit.Base, ux, baseline, SKTextAlign.Left, font, white);

        // 振り仮名。親字の幅に収まれば親字の中央に置く。親字より長ければ両隣へはみ出させる。はみ出してよい幅は、
        // 振り仮名の無い字（と字間の空白）の上なら組み方の上限まで、振り仮名のある字の上ならその振り仮名の脇の空きまで、
        // 行頭・行末では組み方しだいで上限まで（はみ出させない組み方なら 0 で、行の端にそろえて内側へ寄せる）。
        // それでも収まらなければ、収まる幅まで振り仮名を横に圧縮する（長体）。
        // 体裁は親字と同じ（影 → フチ → 白い字）で、フチの太さと影のずれは親字の字の大きさから決める。
        // 斜体の組み方では、振り仮名ごとにそのベースラインを軸に倒し（上ほど右へ）、倒れた分の半分だけ左へ寄せて見た目の中央をそろえる。
        // 熟語の組み方（ひと続き・均等）では、振り仮名のある字が空白をはさまず続くところを 1 つにまとめ、熟語全体の上に置く。
        // 均等の組み方では、読みが熟語より短ければ 1 字ずつ熟語の幅に均等に空けて並べる（字の間に 1 つ分、両端に半分ずつ）。
        bool grouped = rubyLayout.Grouping != SubtitleRubyGrouping.Mono;
        bool spread = rubyLayout.Grouping == SubtitleRubyGrouping.Spread;
        var spans = new List<(string Ruby, SKFont Font, float X, float Slot, bool AfterGap)>();
        foreach (var (unit, font, ux, slot, gapBefore) in placed)
        {
            if (grouped && unit.Ruby.Length > 0 && !gapBefore && spans.Count > 0 && spans[^1].Ruby.Length > 0)
            {
                var prev = spans[^1];
                spans[^1] = (prev.Ruby + unit.Ruby, prev.Font, prev.X, ux + slot - prev.X, prev.AfterGap);
                continue;
            }
            spans.Add((unit.Ruby, font, ux, slot, gapBefore));
        }

        var rubyWidths = new float[spans.Count];
        for (int k = 0; k < spans.Count; k++)
        {
            if (spans[k].Ruby.Length == 0) continue;
            rubyFont.Size = spans[k].Font.Size * rubyLayout.SizeRatio;
            rubyFont.ScaleX = scaleX;
            rubyWidths[k] = Measure(rubyFont, spans[k].Ruby);
        }
        float overhangMax = size * rubyLayout.OverhangRatio * scaleX;
        // 隣 n の側へはみ出してよい幅。separated は字間の空白をはさむとき。n が行の外なら行頭・行末。
        float Allowance(int n, bool separated)
        {
            if (n < 0) return rubyLayout.OverhangLineStart ? overhangMax : 0f;
            if (n >= spans.Count) return rubyLayout.OverhangLineEnd ? overhangMax : 0f;
            if (separated || spans[n].Ruby.Length == 0) return overhangMax;
            // 均等に並べた振り仮名は熟語の幅いっぱいを使うので、脇に空きは残らない。
            if (spread) return 0f;
            return Math.Max(0f, (spans[n].Slot - rubyWidths[n]) / 2f);
        }

        var rubies = new List<(string Ruby, float X, float ScaleX, float RubySize, float BaseSize)>();
        for (int k = 0; k < spans.Count; k++)
        {
            var (ruby, font, ux, slot, gapBefore) = spans[k];
            if (ruby.Length == 0) continue;
            bool gapAfter = k + 1 < spans.Count && spans[k + 1].AfterGap;
            float left = ux - Allowance(k - 1, gapBefore);
            float right = ux + slot + Allowance(k + 1, gapAfter);
            float rubyWidth = rubyWidths[k];
            float rubyScale = scaleX;
            float rx;
            float rubySize = font.Size * rubyLayout.SizeRatio;
            if (spread && rubyWidth < slot)
            {
                rubyFont.Size = rubySize;
                rubyFont.ScaleX = scaleX;
                var chars = new List<(string Text, float Width)>();
                var elements = StringInfo.GetTextElementEnumerator(ruby);
                while (elements.MoveNext())
                {
                    string ch = elements.GetTextElement();
                    chars.Add((ch, Measure(rubyFont, ch)));
                }
                float space = Math.Max(0f, (slot - chars.Sum(c => c.Width)) / chars.Count);
                float cx = ux + space / 2f;
                foreach (var (ch, w) in chars)
                {
                    rubies.Add((ch, cx, scaleX, rubySize, font.Size));
                    cx += w + space;
                }
                continue;
            }
            if (rubyWidth > right - left && rubyWidth > 0f)
            {
                rubyScale = scaleX * (right - left) / rubyWidth;
                rx = left;
            }
            else
            {
                rx = Math.Clamp(ux + (slot - rubyWidth) / 2f, left, right - rubyWidth);
            }
            rubies.Add((ruby, rx, rubyScale, rubySize, font.Size));
        }
        if (rubies.Count == 0) return;

        var rubyShaper = ShaperFor(rubyFont);
        void ForEachRuby(Action<string, float, float, float> draw)
        {
            foreach (var (ruby, rx, rubyScale, rubySize, baseSize) in rubies)
            {
                rubyFont.Size = rubySize;
                rubyFont.ScaleX = rubyScale;
                stroke.StrokeWidth = baseSize * TelopOutlineRatio * 2f;
                float rb = baseline - baseSize * rubyLayout.RaiseRatio;
                canvas.Save();
                if (rubyLayout.Skew != 0f)
                {
                    canvas.Translate(0f, rb);
                    canvas.Skew(-rubyLayout.Skew, 0f);
                    canvas.Translate(0f, -rb);
                }
                draw(ruby, rx - rubyLayout.Skew * rubySize * TelopAscentRatio / 2f, rb, baseSize * TelopShadowRatio);
                canvas.Restore();
            }
        }
        ForEachRuby((ruby, rx, rb, d) => rubyShaper.Draw(canvas, ruby, rx + d, rb + d, SKTextAlign.Left, rubyFont, black, stroke));
        ForEachRuby((ruby, rx, rb, _) => rubyShaper.Draw(canvas, ruby, rx, rb, SKTextAlign.Left, rubyFont, stroke));
        ForEachRuby((ruby, rx, rb, _) => rubyShaper.Draw(canvas, ruby, rx, rb, SKTextAlign.Left, rubyFont, white));
    }

    /// <summary>
    /// サブタイトルの HTML を行ごとの組版単位へ分解する。対象はサイトが持つ <c>&lt;ruby&gt;…&lt;rt&gt;…&lt;/rt&gt;&lt;/ruby&gt;</c>・
    /// <c>&lt;small&gt;</c>・<c>&lt;br&gt;</c> の形だけで、それ以外のタグは外して中身を残す。
    /// 行頭・行末の空白と、字の無い行は捨てる。
    /// </summary>
    private static List<List<TelopUnit>> ParseTelopLines(string html)
    {
        var lines = new List<List<TelopUnit>> { new() };
        bool inRuby = false, inRt = false, rubySmall = false;
        int smallDepth = 0;
        var rubyBase = new StringBuilder();
        var rubyText = new StringBuilder();

        void AddPlain(string text)
        {
            var line = lines[^1];
            var chars = StringInfo.GetTextElementEnumerator(text);
            while (chars.MoveNext())
            {
                string ch = chars.GetTextElement();
                if (string.IsNullOrWhiteSpace(ch))
                {
                    if (line.Count == 0) continue;
                    float gap = ch == "　" ? TelopFullWidthGapEm : TelopHalfWidthGapEm;
                    // 続く空白は 1 つの空きにまとめ、広いほうを採る。
                    if (line[^1].IsGap) line[^1] = line[^1] with { GapEm = Math.Max(line[^1].GapEm, gap) };
                    else line.Add(new TelopUnit("", "", false, gap));
                    continue;
                }
                bool small = smallDepth > 0;
                // 続く地の文（同じ大きさ）は 1 単位にまとめ、ペアカーニングなどの字詰めを効かせる。
                if (line.Count > 0 && line[^1] is { IsGap: false, Ruby.Length: 0 } last && last.Small == small)
                    line[^1] = last with { Base = last.Base + ch };
                else
                    line.Add(new TelopUnit(ch, "", small, 0f));
            }
        }

        foreach (Match m in TelopTokenRegex.Matches(html))
        {
            if (m.Groups[2].Success)
            {
                bool close = m.Groups[1].Value == "/";
                switch (m.Groups[2].Value.ToLowerInvariant())
                {
                    case "br":
                        lines.Add(new());
                        break;
                    case "small":
                        smallDepth = Math.Max(0, smallDepth + (close ? -1 : 1));
                        break;
                    case "ruby":
                        if (!close)
                        {
                            inRuby = true;
                            rubyBase.Clear();
                            rubyText.Clear();
                        }
                        else if (inRuby)
                        {
                            if (rubyBase.Length > 0)
                                lines[^1].Add(new TelopUnit(rubyBase.ToString(), rubyText.ToString().Trim(), rubySmall, 0f));
                            inRuby = false;
                            inRt = false;
                        }
                        break;
                    case "rt":
                        inRt = inRuby && !close;
                        break;
                }
                continue;
            }
            if (m.Value.StartsWith('<')) continue;

            string text = System.Net.WebUtility.HtmlDecode(m.Value);
            if (inRuby)
            {
                if (inRt)
                {
                    rubyText.Append(text);
                }
                else
                {
                    // 親字の大きさは、親字の最初の字が <small> の中にあるかで決める（<ruby><small>VS</small><rt>たい</rt></ruby>）。
                    if (rubyBase.Length == 0) rubySmall = smallDepth > 0;
                    rubyBase.Append(text);
                }
                continue;
            }
            AddPlain(text);
        }

        foreach (var line in lines)
            while (line.Count > 0 && line[^1].IsGap) line.RemoveAt(line.Count - 1);
        lines.RemoveAll(line => line.Count == 0);
        return lines;
    }
}
