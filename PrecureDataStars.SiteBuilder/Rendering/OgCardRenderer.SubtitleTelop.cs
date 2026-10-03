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

    /// <summary>
    /// 行と行のあいだの空きの既定（作品の組み方に指定が無いとき）。下の行に振り仮名があるときの、
    /// 上の行の字の下端から下の行の振り仮名の段の上端までに取る空き。
    /// </summary>
    private const float TelopLineGap = 0.28f;

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
    /// 字間の組み方。<paramref name="Solid"/> はベタ組み（詰めない）か、<paramref name="BaseEm"/>・<paramref name="RubyEm"/> は
    /// 親字・振り仮名の字と字のあいだに足す空き（それぞれの字の大きさに対する比）。
    /// </summary>
    private sealed record TelopSpacing(bool Solid, float BaseEm, float RubyEm);

    /// <summary>
    /// テロップ用の組版器（(書体のハンドル, ベタ組みか, 単独の「！」「？」を全角に置くか) → 組版器）。
    /// テロップの作品が初めて使うときに作る（OGP カードの組版器とは別に持つ）。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(IntPtr Handle, bool Solid, bool KeepPunctuation), Lazy<OgTextShaper>> _telopShapers = new();

    /// <summary>
    /// 字間の組み方に合った組版器。詰める組み方で <paramref name="keepPunctuation"/> なら、単独の「！」「？」を全角のまま置く
    /// （本編のテロップは、行の途中の「！」「？」を全角で組み、行末のものは前の字に寄せる。呼び出し側は行末の単位で false を渡す）。
    /// </summary>
    private OgTextShaper TelopShaper(SKFont font, TelopSpacing spacing, bool keepPunctuation = false)
    {
        var typeface = font.Typeface ?? throw new InvalidOperationException("書体の無いフォントです。");
        return _telopShapers.GetOrAdd((typeface.Handle, spacing.Solid, keepPunctuation && !spacing.Solid),
            key => new Lazy<OgTextShaper>(() => key.Solid
                ? new OgTextShaper(typeface, opticalTightening: false, solid: true)
                : new OgTextShaper(typeface, keepLonePunctuation: key.KeepPunctuation))).Value;
    }

    /// <summary>字間の組み方を効かせた文字列の幅。<paramref name="em"/> は字と字のあいだに足す空き（字の大きさに対する比）。</summary>
    private float TelopMeasure(SKFont font, string text, TelopSpacing spacing, float em, bool keepPunctuation = false) =>
        TelopShaper(font, spacing, keepPunctuation).Measure(text, font, font.Size * em);

    /// <summary>行の i 番目の単位のすぐあとに（空白をはさまず）字の単位が続くか。続くなら単独の「！」「？」を全角に置く。</summary>
    private static bool TelopContinues(IReadOnlyList<TelopUnit> line, int i) => i + 1 < line.Count && !line[i + 1].IsGap;

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
    /// <param name="fontFamily">作品の本編テロップの書体名（<c>series_subtitle_styles.font_subtitle</c>）。空か見つからなければ既定の見出し書体。</param>
    /// <param name="profile">作品ごとの組み方（字間、振り仮名の書体・斜体・大きさと高さ・はみ出し方、行間）。</param>
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
        var spacing = new TelopSpacing(profile.Solid, profile.LetterSpacingEm, profile.RubyLetterSpacingEm);

        using var baseFont = new SKFont(typeface, TelopFontSize);
        using var smallFont = new SKFont(typeface, TelopFontSize * TelopSmallSizeRatio);
        using var rubyFont = new SKFont(rubyTypeface, TelopFontSize * rubyRatio);

        // 振り仮名の段（振り仮名の上端から親字の上端までの高さ）と、字の高さ（いずれも字の大きさに対する比）。
        // 画像の上端に振り仮名の段を取るのは 1 行目に振り仮名があるときだけ。行と行のあいだは、下の行の振り仮名の有無で
        // 作品ごとの空きを使い分ける（本編は、下の行に振り仮名があると行を広げる作品と、行の位置が変わらない作品がある）。
        float rubyBandRatio = Math.Max(0f, rubyRaise + TelopAscentRatio * rubyRatio - TelopAscentRatio);
        float glyphBlockRatio = TelopAscentRatio + TelopDescentRatio;
        bool firstLineHasRuby = lines[0].Any(u => u.Ruby.Length > 0);
        // 字の外の幅：外周の余白と、行の端の振り仮名を外へはみ出させる組み方ではそのはみ出し分（行は中央にそろえるので左右とも取る）。
        float edgeOverhang = hasRuby && (rubyLayout.OverhangLineStart || rubyLayout.OverhangLineEnd) ? rubyLayout.OverhangRatio : 0f;
        float OuterWidth(float s) => 2f * s * (TelopPaddingRatio + edgeOverhang);

        // 字の大きさを決める。いちばん長い行が長体の下限で収まらなければ、収まるところまで全行の字を小さくする。
        float size = TelopFontSize;
        SetTelopSize(baseFont, smallFont, size);
        float longest = lines.Max(line => TelopLineWidth(line, baseFont, smallFont, size, 1f, spacing));
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
            float natural = TelopLineWidth(lines[i], baseFont, smallFont, size, 1f, spacing);
            scales[i] = natural > room && natural > 0f ? room / natural : 1f;
            widths[i] = TelopLineWidth(lines[i], baseFont, smallFont, size, scales[i], spacing);
        }

        float padding = size * TelopPaddingRatio;
        float rubyBand = size * rubyBandRatio;
        float glyphBlock = size * glyphBlockRatio;
        float topBand = firstLineHasRuby ? rubyBand : 0f;
        // 下の行に振り仮名があるときの行送り（振り仮名の段＋字の高さ＋空き）と、無いときの行送り（字の高さ＋空き）。
        // 3 行以上の組では、作品の組み方にその行間があればそれを使い、振り仮名の有無で行送りを変えない
        // （本編は 3 行のとき行を詰めて組むことが多い）。無いときの空きの指定が無い作品も、振り仮名の有無で行送りを変えない。
        bool useGap3 = lines.Count >= 3 && profile.LineGapRatio3 is not null;
        float gapRatio = (useGap3 ? profile.LineGapRatio3 : profile.LineGapRatio) ?? TelopLineGap;
        float pitchWithRuby = rubyBand + glyphBlock + size * gapRatio;
        float pitchPlain = !useGap3 && profile.LineGapRatioPlain is float plain ? glyphBlock + size * plain : pitchWithRuby;
        // 各行の字の上端（画像の上端からの距離）。
        var lineTops = new float[lines.Count];
        lineTops[0] = padding + topBand;
        for (int i = 1; i < lines.Count; i++)
            lineTops[i] = lineTops[i - 1] + (lines[i].Any(u => u.Ruby.Length > 0) ? pitchWithRuby : pitchPlain);

        // 縮めて表示したときに半端な画素が出ないよう、縦横とも倍率の倍数に切り上げる。
        int width = RoundUpToMultiple(widths.Max() + OuterWidth(size), TelopPixelRatio);
        int height = RoundUpToMultiple(lineTops[^1] + glyphBlock + padding, TelopPixelRatio);

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        for (int i = 0; i < lines.Count; i++)
        {
            float baseline = lineTops[i] + size * TelopAscentRatio;
            float x = (width - widths[i]) / 2f;
            DrawTelopLine(canvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], rubyLayout, spacing);
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
    private float TelopLineWidth(IReadOnlyList<TelopUnit> line, SKFont baseFont, SKFont smallFont, float size, float scaleX, TelopSpacing spacing)
    {
        baseFont.ScaleX = scaleX;
        smallFont.ScaleX = scaleX;
        float width = 0f;
        TelopUnit? previous = null;
        for (int i = 0; i < line.Count; i++)
        {
            var unit = line[i];
            if (unit.IsGap)
            {
                width += unit.GapEm * size * scaleX;
            }
            else
            {
                var font = unit.Small ? smallFont : baseFont;
                if (previous is { IsGap: false }) width += font.Size * spacing.BaseEm * scaleX;
                width += TelopMeasure(font, unit.Base, spacing, spacing.BaseEm, TelopContinues(line, i));
            }
            previous = unit;
        }
        return width;
    }

    /// <summary>
    /// 1 行を描く。影 → フチ → 白い字の順に、行の全単位をそれぞれ重ねる（単位ごとに 3 つを重ねると、
    /// 後の単位のフチが前の単位の白い字に食い込むため）。振り仮名は親字のあとに、同じ順で重ねる。
    /// </summary>
    private void DrawTelopLine(
        SKCanvas canvas, IReadOnlyList<TelopUnit> line, float x, float baseline,
        SKFont baseFont, SKFont smallFont, SKFont rubyFont, float size, float scaleX, TelopRubyLayout rubyLayout, TelopSpacing spacing)
    {
        baseFont.ScaleX = scaleX;
        smallFont.ScaleX = scaleX;

        // 各単位の起点と占有幅を先に決める。単位と単位のあいだにも、字と字のあいだと同じ空きを足す。
        var placed = new List<(TelopUnit Unit, SKFont Font, float X, float Slot, bool AfterGap)>();
        var continues = new HashSet<TelopUnit>(ReferenceEqualityComparer.Instance);
        float cursor = x;
        bool afterGap = false;
        for (int i = 0; i < line.Count; i++)
        {
            var unit = line[i];
            if (unit.IsGap)
            {
                cursor += unit.GapEm * size * scaleX;
                afterGap = true;
                continue;
            }
            var font = unit.Small ? smallFont : baseFont;
            if (placed.Count > 0 && !afterGap) cursor += font.Size * spacing.BaseEm * scaleX;
            bool keep = TelopContinues(line, i);
            if (keep) continues.Add(unit);
            float slot = TelopMeasure(font, unit.Base, spacing, spacing.BaseEm, keep);
            placed.Add((unit, font, cursor, slot, afterGap));
            cursor += slot;
            afterGap = false;
        }
        void DrawBase(TelopUnit unit, SKFont font, float ux, float y, SKPaint paint, SKPaint? outline) =>
            TelopShaper(font, spacing, continues.Contains(unit))
                .Draw(canvas, unit.Base, ux, y, SKTextAlign.Left, font, paint, outline, font.Size * spacing.BaseEm);

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

        var rubies = PlaceTelopRubies(placed.Select(p =>
            {
                var ink = TelopShaper(p.Font, spacing, continues.Contains(p.Unit))
                    .InkExtent(p.Unit.Base, p.Font, p.Font.Size * spacing.BaseEm);
                return (p.Unit.Ruby, p.Font, p.X, p.Slot, p.AfterGap, p.X + ink.Left, p.X + ink.Right);
            }).ToList(),
            rubyFont, size, scaleX, rubyLayout, spacing);

        // 親字と振り仮名をひとまとまりとして、影 → フチ → 白い字の順に重ねる（振り仮名のフチが親字の白い字にかぶらない）。
        // 影（フチの外形ごと右下へずらした黒）。
        foreach (var (unit, font, ux, _, _) in placed)
        {
            float d = font.Size * TelopShadowRatio;
            // 線は輪郭を中心に引かれるので、見せたいフチの太さの 2 倍にする。
            stroke.StrokeWidth = font.Size * TelopOutlineRatio * 2f;
            DrawBase(unit, font, ux + d, baseline + d, black, stroke);
        }
        DrawTelopRubyPass(canvas, rubies, baseline, rubyFont, rubyLayout, spacing, TelopPass.Shadow);
        // フチ。
        foreach (var (unit, font, ux, _, _) in placed)
        {
            stroke.StrokeWidth = font.Size * TelopOutlineRatio * 2f;
            DrawBase(unit, font, ux, baseline, stroke, null);
        }
        DrawTelopRubyPass(canvas, rubies, baseline, rubyFont, rubyLayout, spacing, TelopPass.Outline);
        // 白い字。
        foreach (var (unit, font, ux, _, _) in placed)
            DrawBase(unit, font, ux, baseline, white, null);
        DrawTelopRubyPass(canvas, rubies, baseline, rubyFont, rubyLayout, spacing, TelopPass.Fill);
    }

    /// <summary>テロップの体裁の 1 層（影・フチ・白い字）。親字と振り仮名を層ごとに重ねるために使う。</summary>
    private enum TelopPass { Shadow, Outline, Fill }

    /// <summary>置き場所の決まった振り仮名 1 つ（文字列・起点・長体の率・振り仮名の大きさ・親字の大きさ）。</summary>
    private sealed record TelopRubyGlyph(string Ruby, float X, float ScaleX, float RubySize, float BaseSize);

    /// <summary>
    /// 1 行の振り仮名の置き場所を決める。<paramref name="placed"/> は行の字の単位（振り仮名・親字のフォント・起点・占有幅・
    /// 直前が空白か・親字の字面の左端と右端）で、振り仮名の無い単位は空文字を持つ。<paramref name="size"/> は行の親字の大きさ（はみ出しの上限の基準）。
    /// 振り仮名は、字送りの箱ではなく字面（インク）の中心を親字の字面の中心にそろえる（書体によっては字形が箱の中で片寄るため）。
    /// </summary>
    private List<TelopRubyGlyph> PlaceTelopRubies(
        IReadOnlyList<(string Ruby, SKFont Font, float X, float Slot, bool AfterGap, float InkLeft, float InkRight)> placed,
        SKFont rubyFont, float size, float scaleX, TelopRubyLayout rubyLayout, TelopSpacing spacing)
    {
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
        var spans = new List<(string Ruby, SKFont Font, float X, float Slot, bool AfterGap, float InkLeft, float InkRight)>();
        foreach (var (unitRuby, font, ux, slot, gapBefore, inkLeft, inkRight) in placed)
        {
            if (grouped && unitRuby.Length > 0 && !gapBefore && spans.Count > 0 && spans[^1].Ruby.Length > 0)
            {
                var prev = spans[^1];
                spans[^1] = (prev.Ruby + unitRuby, prev.Font, prev.X, ux + slot - prev.X, prev.AfterGap, prev.InkLeft, inkRight);
                continue;
            }
            spans.Add((unitRuby, font, ux, slot, gapBefore, inkLeft, inkRight));
        }

        var rubyWidths = new float[spans.Count];
        for (int k = 0; k < spans.Count; k++)
        {
            if (spans[k].Ruby.Length == 0) continue;
            rubyFont.Size = spans[k].Font.Size * rubyLayout.SizeRatio;
            rubyFont.ScaleX = scaleX;
            rubyWidths[k] = TelopMeasure(rubyFont, spans[k].Ruby, spacing, spacing.RubyEm);
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

        var rubies = new List<TelopRubyGlyph>();
        for (int k = 0; k < spans.Count; k++)
        {
            var (ruby, font, ux, slot, gapBefore, inkLeft, inkRight) = spans[k];
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
                    chars.Add((ch, TelopMeasure(rubyFont, ch, spacing, 0f)));
                }
                float space = Math.Max(0f, (slot - chars.Sum(c => c.Width)) / chars.Count);
                float cx = ux + space / 2f;
                foreach (var (ch, w) in chars)
                {
                    rubies.Add(new TelopRubyGlyph(ch, cx, scaleX, rubySize, font.Size));
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
                // 振り仮名の字面の中心を親字の字面の中心に合わせ、はみ出してよい範囲に収める。
                rubyFont.Size = rubySize;
                rubyFont.ScaleX = scaleX;
                var rubyInk = TelopShaper(rubyFont, spacing).InkExtent(ruby, rubyFont, rubySize * spacing.RubyEm);
                float center = inkRight > inkLeft ? (inkLeft + inkRight) / 2f : ux + slot / 2f;
                float rubyInkCenter = rubyInk.Right > rubyInk.Left ? (rubyInk.Left + rubyInk.Right) / 2f : rubyWidth / 2f;
                rx = Math.Clamp(center - rubyInkCenter, left, right - rubyWidth);
            }
            rubies.Add(new TelopRubyGlyph(ruby, rx, rubyScale, rubySize, font.Size));
        }
        return rubies;
    }

    /// <summary>
    /// 振り仮名をテロップの体裁の 1 層だけ描く。フチの太さと影のずれは親字の字の大きさから決める。
    /// 斜体の組み方では、振り仮名ごとにそのベースラインを軸に倒し（上ほど右へ）、倒れた分の半分だけ左へ寄せて見た目の中央をそろえる。
    /// </summary>
    private void DrawTelopRubyPass(
        SKCanvas canvas, IReadOnlyList<TelopRubyGlyph> rubies, float baseline, SKFont rubyFont, TelopRubyLayout rubyLayout,
        TelopSpacing spacing, TelopPass pass)
    {
        if (rubies.Count == 0) return;
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
        var rubyShaper = TelopShaper(rubyFont, spacing);
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
        float RubyTracking() => rubyFont.Size * spacing.RubyEm;
        switch (pass)
        {
            case TelopPass.Shadow:
                ForEachRuby((ruby, rx, rb, d) => rubyShaper.Draw(canvas, ruby, rx + d, rb + d, SKTextAlign.Left, rubyFont, black, stroke, RubyTracking()));
                break;
            case TelopPass.Outline:
                ForEachRuby((ruby, rx, rb, _) => rubyShaper.Draw(canvas, ruby, rx, rb, SKTextAlign.Left, rubyFont, stroke, null, RubyTracking()));
                break;
            default:
                ForEachRuby((ruby, rx, rb, _) => rubyShaper.Draw(canvas, ruby, rx, rb, SKTextAlign.Left, rubyFont, white, null, RubyTracking()));
                break;
        }
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
