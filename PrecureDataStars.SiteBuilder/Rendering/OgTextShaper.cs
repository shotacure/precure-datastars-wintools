using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// OGP カードの文字を HarfBuzz で組む（字詰めを効かせた計測と描画）。
/// <para>
/// SkiaSharp の <see cref="SKCanvas.DrawText(string, float, float, SKTextAlign, SKFont, SKPaint)"/> は
/// 文字を字送り幅のまま並べるだけで、フォントが持つカーニング（kern）やプロポーショナル詰め（palt）を
/// 使わない。商用の日本語書体はこの詰め情報を持っていて、効かせないと見出しが間延びするので、
/// HarfBuzz で字形と位置を決めてから <see cref="SKTextBlob"/> として描く。
/// </para>
/// <para>
/// 書体 1 つにつき 1 インスタンス。HarfBuzz の Face / Font は読み取り専用にして共有し、
/// 1 回の組版ごとに Buffer を作って捨てる。並列のページ生成から同時に呼ばれるため、
/// 組版そのものはロックで直列化する（文字列 1 本の組版はマイクロ秒単位なので足を引かない）。
/// </para>
/// </summary>
public sealed class OgTextShaper : IDisposable
{
    /// <summary>効かせる OpenType 機能。カーニングと、日本語のプロポーショナル詰め（palt）、合字。</summary>
    private static readonly Feature[] Features =
    {
        new(Tag.Parse("kern")),
        new(Tag.Parse("palt")),
        new(Tag.Parse("liga")),
        new(Tag.Parse("calt"))
    };

    /// <summary>
    /// ベタ組みで効かせる OpenType 機能。詰め（kern・palt）を外し、合字だけ。HarfBuzz は kern を既定で効かせるので、
    /// 0 を指定して明示的に切る（切らないと、かなどうしのペアカーニングを持つ書体で字送りが詰まる）。
    /// </summary>
    private static readonly Feature[] SolidFeatures =
    {
        new(Tag.Parse("kern"), 0u),
        new(Tag.Parse("palt"), 0u),
        new(Tag.Parse("liga")),
        new(Tag.Parse("calt"))
    };

    /// <summary>ベタ組み（字送り幅のまま並べ、詰めない）か。</summary>
    private readonly bool _solid;

    /// <summary>ベタ組みで、全角の約物（<see cref="IsFullWidthPunct"/>）だけに palt をかけて半分の幅に詰めるか（約物半角）。</summary>
    private readonly bool _punctHalf;

    /// <summary>欧文と約物（<see cref="IsLatinSpaced"/>）のあとに足す空き（em に対する比）。文字列の最後の字のあとには足さない。</summary>
    private readonly float _latinTrackingEm;

    /// <summary>ベタ組みで、小書きの仮名（<see cref="IsSmallKana"/>）にかける palt の割合（0〜1）。</summary>
    private readonly float _smallKanaPalt;

    /// <summary>小書きの仮名ごとの、palt で組んだときのベタとの (字送りの差, 字の位置のずれ)（フォント単位）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<char, (int Advance, int Offset)> _smallKanaAdjust = new();

    /// <summary>
    /// 単独の全角の「！」「？」（前後に「！」「？」が続かないもの）を詰めずに全角のまま置くか。
    /// 文字列の末尾の「！」「？」も対象（呼び出し側は、あとに字が続く文字列のときだけ有効にする）。
    /// </summary>
    private readonly bool _keepLonePunctuation;

    private readonly SKTypeface _typeface;

    /// <summary>欧文の部分を組む組版器（<see cref="UseSubstitutes"/>）。null なら差し替えない。</summary>
    private OgTextShaper? _latin;

    /// <summary>この書体に字形が無い字を組む組版器（<see cref="UseSubstitutes"/>）。null なら補わない。</summary>
    private OgTextShaper? _fallback;

    /// <summary>絵文字を組む組版器（<see cref="UseSubstitutes"/>。色つきの絵文字の書体）。null なら絵文字もこの書体のまま。</summary>
    private OgTextShaper? _emoji;

    private readonly SKStreamAsset _stream;
    private readonly Blob _blob;
    private readonly Face _face;
    private readonly HbFont _font;
    private readonly int _unitsPerEm;
    private readonly object _lock = new();

    /// <summary>
    /// 字面（インク）に基づく詰め組みを使うか。ペアカーニング（GPOS の kern）とプロポーショナル詰め（palt）の
    /// 両方を持つ書体（モリサワの Pr6N / ProN、Noto Sans JP など）だけ OpenType 機能に任せ、どちらかを欠く書体は
    /// 字形の実際の幅から字送りを決める。写研由来の A-SK 書体（詰めの機能を何も持たない）と、フォントワークスの
    /// 書体（palt だけで、字間が空いて見える）がここに入る。コンストラクタで明示すれば自動判定を上書きできる
    /// （ワードマークの Kiwi Maru は、ブラウザと同じ見た目にするため詰めない）。
    /// </summary>
    public bool UsesOpticalTightening { get; }

    /// <summary>字面詰めで字面の両側に残す空き（em に対する比）。</summary>
    private const float OpticalSideGap = 0.035f;


    /// <summary>組んだ 1 本の文字列。グリフ ID と位置はフォントサイズ 1 のときの値ではなく、指定サイズのピクセル値。</summary>
    public sealed record ShapedRun(ushort[] Glyphs, SKPoint[] Positions, float Width);

    /// <param name="typeface">組む書体。</param>
    /// <param name="opticalTightening">字面詰めを使うか。null なら書体の OpenType 機能から自動判定する。</param>
    /// <param name="solid">ベタ組みにするか。true なら詰め（kern・palt・字面詰め）を一切かけず、字送り幅のまま並べる。</param>
    /// <param name="keepLonePunctuation">字面詰めで、単独の全角の「！」「？」を詰めずに全角のまま置くか（テロップの組み方）。</param>
    /// <param name="punctHalf">ベタ組みで、全角の約物だけを palt で半分の幅に詰めるか（約物半角）。</param>
    /// <param name="latinTrackingEm">欧文と約物のあとに足す空き（em に対する比）。</param>
    /// <param name="smallKanaPalt">ベタ組みで、小書きの仮名にかける palt の割合（0〜1）。</param>
    public OgTextShaper(SKTypeface typeface, bool? opticalTightening = null, bool solid = false, bool keepLonePunctuation = false,
        bool punctHalf = false, float latinTrackingEm = 0f, float smallKanaPalt = 0f)
    {
        _stream = typeface.OpenStream(out int ttcIndex);
        _blob = _stream.ToHarfBuzzBlob();
        _face = new Face(_blob, ttcIndex);
        _unitsPerEm = _face.UnitsPerEm > 0 ? _face.UnitsPerEm : typeface.UnitsPerEm;
        if (_unitsPerEm <= 0) _unitsPerEm = 1000;
        _font = new HbFont(_face);
        _font.SetScale(_unitsPerEm, _unitsPerEm);
        _font.SetFunctionsOpenType();
        _typeface = typeface;
        _solid = solid;
        _keepLonePunctuation = keepLonePunctuation;
        _punctHalf = solid && punctHalf;
        _latinTrackingEm = latinTrackingEm;
        _smallKanaPalt = solid ? smallKanaPalt : 0f;
        UsesOpticalTightening = !solid && (opticalTightening ?? !(HasGposFeature(typeface, "kern") && HasGposFeature(typeface, "palt")));
    }

    /// <summary>
    /// 文字列の一部を別の書体で組むように指定する。<paramref name="latin"/> は欧文（<see cref="IsLatinRun"/>）の部分を、
    /// <paramref name="fallback"/> はこの書体に字形が無い字を、<paramref name="emoji"/> は絵文字（<see cref="IsEmoji"/>）を組む。
    /// いずれも、差し替え先に字形が無い字はこの書体のまま組む。
    /// 指定すると <see cref="Measure"/>・<see cref="InkExtent"/>・<see cref="Draw(SKCanvas, string, float, float, SKTextAlign, SKFont, SKPaint, SKPaint?, float)"/>
    /// が文字列を書体ごとの断片に切り分けて組み、続けて並べる（<see cref="Shape"/> はこの書体だけで組む）。
    /// </summary>
    public void UseSubstitutes(OgTextShaper? latin, OgTextShaper? fallback, OgTextShaper? emoji = null)
    {
        _latin = latin;
        _fallback = fallback;
        _emoji = emoji;
    }

    private bool HasSubstitutes => _latin is not null || _fallback is not null || _emoji is not null;

    /// <summary>
    /// 絵文字か（絵文字の面 U+1F000〜U+1FAFF、記号・装飾記号 U+2600〜U+27BF）。絵文字の異体字セレクタ（U+FE0F）と
    /// ゼロ幅接合子（U+200D）は、直前の絵文字と同じ断片に入れる（<see cref="Segment"/>）。
    /// </summary>
    private static bool IsEmoji(int codePoint) =>
        (codePoint >= 0x1F000 && codePoint <= 0x1FAFF) || (codePoint >= 0x2600 && codePoint <= 0x27BF);

    /// <summary>字ごとの、この書体が字形を持つかの控え（並列のページ生成から引くので並行用の辞書）。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _hasGlyph = new();

    /// <summary>この書体が字形を持つか（SKFont はスレッド安全ではないので、問い合わせのたびに使い捨てる）。</summary>
    private bool HasGlyph(int codePoint) => _hasGlyph.GetOrAdd(codePoint, cp =>
    {
        using var probe = new SKFont(_typeface);
        return probe.ContainsGlyph(cp);
    });

    /// <summary>
    /// 欧文の書体で組む字か。半角の英数字と記号、ラテン文字の拡張（アクセント付きの字など）。
    /// 欧文の引用符（‘ ’ “ ”）は、隣が半角の英数字のときだけ欧文として扱う（和文の中の “ ” は和文の書体のまま）。
    /// </summary>
    private static bool IsLatinRun(string text, int index, int codePoint)
    {
        if ((codePoint >= 0x21 && codePoint <= 0x7E) || (codePoint >= 0xA1 && codePoint <= 0x24F)) return true;
        if (codePoint is not (0x2018 or 0x2019 or 0x201C or 0x201D)) return false;
        static bool IsAsciiWord(char c) => char.IsAsciiLetterOrDigit(c);
        return (index > 0 && IsAsciiWord(text[index - 1])) || (index + 1 < text.Length && IsAsciiWord(text[index + 1]));
    }

    /// <summary>
    /// 文字列を、組む組版器ごとの断片に切り分ける（出てきた順）。空白は直前の断片に含める（文字列の頭の空白は次の断片へ）。
    /// 欧文の断片の半角のアポストロフィ（'）は「’」に置き換える。
    /// </summary>
    private List<(OgTextShaper Shaper, string Text)> Segment(string text)
    {
        var segments = new List<(OgTextShaper Shaper, string Text)>();
        var current = new System.Text.StringBuilder();
        OgTextShaper? currentShaper = null;
        for (int i = 0; i < text.Length;)
        {
            // 1 字ずつ積んで測る呼び出し（折り返し）では、サロゲートペアの前半だけが末尾に来ることがある。
            // 対になっていないサロゲートはその 1 単位を 1 字として扱う（どの書体にも字形が無いので、この書体のまま組む）。
            int length = char.IsSurrogatePair(text, i) ? 2 : 1;
            int codePoint = length == 2 ? char.ConvertToUtf32(text, i) : text[i];
            OgTextShaper shaper;
            if (char.IsWhiteSpace(text[i]))
                shaper = currentShaper ?? this;
            else if (_emoji is not null && currentShaper == _emoji && codePoint is 0xFE0F or 0x200D)
                shaper = _emoji;
            else if (_emoji is not null && IsEmoji(codePoint) && _emoji.HasGlyph(codePoint))
                shaper = _emoji;
            else if (_latin is not null && IsLatinRun(text, i, codePoint) && _latin.HasGlyph(codePoint == '\'' ? 0x2019 : codePoint))
                shaper = _latin;
            else if (!HasGlyph(codePoint) && _fallback is not null && _fallback.HasGlyph(codePoint))
                shaper = _fallback;
            else
                shaper = this;

            if (currentShaper is not null && shaper != currentShaper && current.Length > 0)
            {
                segments.Add((currentShaper, current.ToString()));
                current.Clear();
            }
            currentShaper = shaper;
            if (shaper == _latin && text[i] == '\'') current.Append('’');
            else current.Append(text, i, length);
            i += length;
        }
        if (currentShaper is not null && current.Length > 0) segments.Add((currentShaper, current.ToString()));
        return segments;
    }

    /// <summary><paramref name="font"/> と同じ大きさ・長体・斜体で、この組版器の書体を使うフォント。</summary>
    private SKFont FontLike(SKFont font) => new(_typeface, font.Size, font.ScaleX, font.SkewX)
    {
        Edging = font.Edging,
        Subpixel = font.Subpixel,
        Hinting = font.Hinting,
        LinearMetrics = font.LinearMetrics,
        Embolden = font.Embolden
    };

    /// <summary>書体の GPOS テーブルに指定の機能タグがあるか（FeatureList の FeatureRecord を直接読む）。</summary>
    private static bool HasGposFeature(SKTypeface typeface, string tag)
    {
        const uint gpos = 0x47504F53; // 'GPOS'
        byte[]? table;
        try { table = typeface.GetTableData(gpos); }
        catch (Exception) { return false; }
        if (table is null || table.Length < 10) return false;

        int featureList = (table[6] << 8) | table[7];
        if (featureList + 2 > table.Length) return false;
        int count = (table[featureList] << 8) | table[featureList + 1];
        for (int i = 0; i < count; i++)
        {
            int rec = featureList + 2 + i * 6;
            if (rec + 4 > table.Length) break;
            if (table[rec] == tag[0] && table[rec + 1] == tag[1] && table[rec + 2] == tag[2] && table[rec + 3] == tag[3])
                return true;
        }
        return false;
    }

    /// <summary>
    /// 文字列を指定サイズで組む。空文字なら幅 0 の空の結果。
    /// <paramref name="tracking"/>（ピクセル）は字と字のあいだに足す空き（最後の字のあとには足さない）。
    /// </summary>
    public ShapedRun Shape(string text, float fontSize, float tracking = 0f)
    {
        var run = ShapeCore(text, fontSize);
        if (tracking == 0f || run.Glyphs.Length < 2) return run;
        var points = new SKPoint[run.Positions.Length];
        for (int i = 0; i < points.Length; i++)
            points[i] = new SKPoint(run.Positions[i].X + tracking * i, run.Positions[i].Y);
        return new ShapedRun(run.Glyphs, points, run.Width + tracking * (points.Length - 1));
    }

    private ShapedRun ShapeCore(string text, float fontSize)
    {
        if (string.IsNullOrEmpty(text)) return new ShapedRun(Array.Empty<ushort>(), Array.Empty<SKPoint>(), 0f);
        var run = ShapeGlyphs(text, fontSize, out var clusters);
        return _latinTrackingEm == 0f ? run : WithLatinTracking(run, text, clusters, fontSize * _latinTrackingEm);
    }

    /// <summary>
    /// 欧文と約物の字形のあとに <paramref name="tracking"/>（ピクセル）を足す。最後の字形のあとには足さない。
    /// <paramref name="clusters"/> は字形ごとの、元の文字列の位置（UTF-16 の位置）。
    /// </summary>
    private static ShapedRun WithLatinTracking(ShapedRun run, string text, int[] clusters, float tracking)
    {
        int count = run.Glyphs.Length;
        if (count < 2) return run;
        var points = new SKPoint[count];
        float shift = 0f;
        for (int i = 0; i < count; i++)
        {
            points[i] = new SKPoint(run.Positions[i].X + shift, run.Positions[i].Y);
            // 同じ字の字形（合字・結合文字）のあいだには足さない
            bool clusterEnd = i + 1 < count && clusters[i + 1] != clusters[i];
            if (clusterEnd && clusters[i] < text.Length && IsLatinSpaced(text[clusters[i]])) shift += tracking;
        }
        return new ShapedRun(run.Glyphs, points, run.Width + shift);
    }

    /// <summary>
    /// 全角の約物（句読点・括弧・「！」「？」などの全角の記号）か。約物半角（<c>punctHalf</c>）で半分の幅に詰める字。
    /// </summary>
    public static bool IsFullWidthPunct(char c) =>
        c is '、' or '。' || (c >= '〈' && c <= '〟')
        || (c >= '！' && c <= '／') || (c >= '：' && c <= '＠')
        || (c >= '［' && c <= '｀') || (c >= '｛' && c <= '･');

    /// <summary>
    /// 欧文の字間（<c>latinTrackingEm</c>）を足す字か。欧文（英数字・半角の記号・欧文の引用符など。和文の書体で全角に組む
    /// 記号類（矢印・図形・音符など U+2190〜U+2BFF）と「…」「‥」「※」「―」は除く）、全角の英数字、全角の約物（<see cref="IsFullWidthPunct"/>）。
    /// </summary>
    public static bool IsLatinSpaced(char c)
    {
        if (char.IsWhiteSpace(c)) return false;
        if (IsFullWidthPunct(c)) return true;
        if ((c >= '０' && c <= '９') || (c >= 'Ａ' && c <= 'Ｚ') || (c >= 'ａ' && c <= 'ｚ')) return true;
        if (c is '―' or '‥' or '…' or '※') return false;
        return c < '←' || (c > '⯿' && c < '⺀');
    }

    private ShapedRun ShapeGlyphs(string text, float fontSize, out int[] clusters)
    {
        float scale = fontSize / _unitsPerEm;
        lock (_lock)
        {
            using var buffer = new HbBuffer();
            buffer.AddUtf16(text);
            buffer.GuessSegmentProperties();
            _font.Shape(buffer, _punctHalf ? PunctHalfFeatures(text) : _solid ? SolidFeatures : Features);

            var infos = buffer.GlyphInfos;
            var positions = buffer.GlyphPositions;
            int count = infos.Length;
            var glyphs = new ushort[count];
            var points = new SKPoint[count];
            clusters = new int[count];
            for (int i = 0; i < count; i++)
            {
                glyphs[i] = (ushort)infos[i].Codepoint;
                clusters[i] = (int)infos[i].Cluster;
            }

            var keep = new bool[count];
            if (_keepLonePunctuation)
            {
                for (int i = 0; i < count; i++)
                {
                    int c = (int)infos[i].Cluster;
                    keep[i] = c < text.Length && IsLonePunctuation(text, c);
                }
            }
            if (UsesOpticalTightening)
                return TightenByInk(glyphs, positions, fontSize, scale, keep);
            // 小書きの仮名は、palt の字送りの差と位置のずれを割合だけ効かせる
            var smallAdjust = new (float Advance, float Offset)[count];
            if (_smallKanaPalt > 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    int c = clusters[i];
                    if (c < text.Length && IsSmallKana(text[c]))
                    {
                        var (da, dofs) = SmallKanaAdjust(text[c]);
                        smallAdjust[i] = (da * scale * _smallKanaPalt, dofs * scale * _smallKanaPalt);
                    }
                }
            }

            // 単独の「！」「？」は、詰め（palt）で狭まった字送りを全角に戻し、字形をその中央に置く。
            float x = 0f;
            for (int i = 0; i < count; i++)
            {
                float advance = positions[i].XAdvance * scale;
                float shift = 0f;
                if (keep[i] && advance < fontSize)
                {
                    shift = (fontSize - advance) / 2f;
                    advance = fontSize;
                }
                points[i] = new SKPoint(x + shift + smallAdjust[i].Offset + positions[i].XOffset * scale, -positions[i].YOffset * scale);
                x += advance + smallAdjust[i].Advance;
            }
            return new ShapedRun(glyphs, points, x);
        }
    }

    /// <summary>小書きの仮名（ぁぃぅぇぉっゃゅょゎゕゖ と片仮名の同じ字、片仮名の小書きの拡張 U+31F0〜U+31FF）か。</summary>
    public static bool IsSmallKana(char c) =>
        "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶ".IndexOf(c) >= 0 || (c >= '\u31F0' && c <= '\u31FF');

    /// <summary>
    /// 小書きの仮名 1 字を palt で組んだときの、ベタとの字送りの差と位置のずれ（フォント単位）。
    /// 組版のロックの中（<see cref="ShapeGlyphs"/>）から呼ぶ。
    /// </summary>
    private (int Advance, int Offset) SmallKanaAdjust(char c) => _smallKanaAdjust.GetOrAdd(c, ch =>
    {
        int Shape1(bool palt, out int offset)
        {
            using var b = new HbBuffer();
            b.AddUtf16(ch.ToString());
            b.GuessSegmentProperties();
            _font.Shape(b, new[] { new Feature(Tag.Parse("kern"), 0u), new Feature(Tag.Parse("palt"), palt ? 1u : 0u) });
            offset = b.GlyphPositions.Length > 0 ? b.GlyphPositions[0].XOffset : 0;
            return b.GlyphPositions.Length > 0 ? b.GlyphPositions[0].XAdvance : 0;
        }
        int a0 = Shape1(false, out int o0), a1 = Shape1(true, out int o1);
        return (a1 - a0, o1 - o0);
    });

    /// <summary>
    /// 約物半角のベタ組みの OpenType 機能。kern を切り、合字と、全角の約物の字の範囲だけの palt
    /// （<see cref="SolidFeatures"/> の文字列全体の palt の打ち消しは、範囲の指定とぶつかるので入れない。palt は既定では効かない）。
    /// </summary>
    private static Feature[] PunctHalfFeatures(string text)
    {
        var features = new List<Feature> { new(Tag.Parse("kern"), 0u), new(Tag.Parse("liga")), new(Tag.Parse("calt")) };
        for (int i = 0; i < text.Length; i++)
            if (IsFullWidthPunct(text[i])) features.Add(new Feature(Tag.Parse("palt"), 1u, (uint)i, (uint)i + 1));
        return features.ToArray();
    }

    /// <summary>text[index] が、前後に「！」「？」の続かない全角の「！」「？」か。</summary>
    private static bool IsLonePunctuation(string text, int index)
    {
        static bool IsMark(char c) => c is '！' or '？' or '!' or '?';
        char ch = text[index];
        if (ch != '！' && ch != '？') return false;
        bool before = index > 0 && IsMark(text[index - 1]);
        bool after = index + 1 < text.Length && IsMark(text[index + 1]);
        return !before && !after;
    }

    /// <summary>
    /// 字面に基づく詰め組み。各字形のインクの左右端を測り、「前の字面の右端 ＋ 空き」に次の字面の左端を揃えて送る。
    /// 元の字送りより広げることはしない（インクが広い字はそのまま）。インクの無い字（空白）は元の字送りのまま。
    /// 「！」「・」のように字面の細い字が、全角のまま間延びしないようにする。<paramref name="keep"/> が立つ字は詰めず元の字送りのまま。
    /// </summary>
    private ShapedRun TightenByInk(ushort[] glyphs, GlyphPosition[] positions, float fontSize, float scale, bool[] keep)
    {
        int count = glyphs.Length;
        using var font = new SKFont(_typeface, fontSize);
        var widths = new float[count];
        var bounds = new SKRect[count];
        font.GetGlyphWidths(glyphs, widths, bounds);

        float gap = fontSize * OpticalSideGap;
        var points = new SKPoint[count];
        float cursor = 0f;
        for (int i = 0; i < count; i++)
        {
            float advance = positions[i].XAdvance * scale;
            float y = -positions[i].YOffset * scale;
            var ink = bounds[i];
            float tight = ink.Width + gap * 2f;
            if (keep[i] && ink.Width > 0f)
            {
                // 単独の「！」「？」は全角の字送りを取り、字面をその中央に置く（palt で狭まった字送りも全角に戻す）。
                float full = Math.Max(advance, fontSize);
                points[i] = new SKPoint(cursor + (full - ink.Width) / 2f - ink.Left, y);
                cursor += full;
                continue;
            }
            if (ink.Width <= 0f || tight >= advance)
            {
                points[i] = new SKPoint(cursor + positions[i].XOffset * scale, y);
                cursor += advance;
                continue;
            }
            points[i] = new SKPoint(cursor + gap - ink.Left, y);
            cursor += tight;
        }
        return new ShapedRun(glyphs, points, cursor);
    }

    /// <summary>
    /// 組んだ幅（ピクセル）。フォントの横方向の拡大率（<see cref="SKFont.ScaleX"/>＝長体）を含む。
    /// <paramref name="tracking"/> は字と字のあいだに足す空き（長体の率をかける前のピクセル）。
    /// </summary>
    public float Measure(string text, SKFont font, float tracking = 0f)
    {
        if (!HasSubstitutes) return Shape(text, font.Size, tracking).Width * font.ScaleX;
        // 断片ごとに組んだ幅の和。断片の切れ目にも字と字のあいだと同じ空きを足す。
        var segments = Segment(text);
        float width = 0f;
        for (int i = 0; i < segments.Count; i++)
        {
            width += segments[i].Shaper.Shape(segments[i].Text, font.Size, tracking).Width;
            if (i + 1 < segments.Count) width += tracking;
        }
        return width * font.ScaleX;
    }

    /// <summary>
    /// 組んだ文字列の字面（インク）の左端と右端（起点からのピクセル。長体の率を含む）。字面の無い文字列なら (0, 0)。
    /// <paramref name="tracking"/> は字と字のあいだに足す空き（長体の率をかける前のピクセル）。
    /// </summary>
    public (float Left, float Right) InkExtent(string text, SKFont font, float tracking = 0f)
    {
        if (HasSubstitutes)
        {
            // 断片ごとの字面を、断片の起点（それまでの断片の幅）だけずらして合わせる。
            float origin = 0f, inkLeft = float.MaxValue, inkRight = float.MinValue;
            foreach (var (shaper, part) in Segment(text))
            {
                using var partFont = shaper.FontLike(font);
                var (l, r) = shaper.InkExtentSingle(part, partFont, tracking);
                if (r > l)
                {
                    inkLeft = Math.Min(inkLeft, origin + l);
                    inkRight = Math.Max(inkRight, origin + r);
                }
                origin += (shaper.Shape(part, font.Size, tracking).Width + tracking) * font.ScaleX;
            }
            return inkLeft > inkRight ? (0f, 0f) : (inkLeft, inkRight);
        }
        return InkExtentSingle(text, font, tracking);
    }

    /// <summary><see cref="InkExtent"/> の、この書体だけで組む場合。</summary>
    private (float Left, float Right) InkExtentSingle(string text, SKFont font, float tracking)
    {
        var run = Shape(text, font.Size, tracking);
        if (run.Glyphs.Length == 0) return (0f, 0f);
        var widths = new float[run.Glyphs.Length];
        var bounds = new SKRect[run.Glyphs.Length];
        font.GetGlyphWidths(run.Glyphs, widths, bounds);
        float left = float.MaxValue, right = float.MinValue;
        for (int i = 0; i < run.Glyphs.Length; i++)
        {
            if (bounds[i].Width <= 0f) continue;
            float x = run.Positions[i].X * font.ScaleX;
            left = Math.Min(left, x + bounds[i].Left);
            right = Math.Max(right, x + bounds[i].Right);
        }
        return left > right ? (0f, 0f) : (left, right);
    }

    /// <summary>
    /// 組んだ結果を描く。<paramref name="align"/> は幅を測って起点をずらすだけで、
    /// SkiaSharp の文字揃えと同じ意味（Right なら x が右端、Center なら x が中央）。
    /// </summary>
    public void Draw(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint)
        => Draw(canvas, text, x, baseline, align, font, paint, null);

    /// <summary>
    /// 組んだ結果を、縁取り付きで描く。<paramref name="outline"/>（線の塗り。線の幅・角の丸みは呼び出し側が設定する）で
    /// 同じ字形を先に描き、その上に <paramref name="paint"/> で塗る。線は字形の輪郭を中心に引かれるので、
    /// 見えるフチの太さは線の幅の半分になる。<paramref name="outline"/> が null なら塗りだけ。
    /// <paramref name="tracking"/> は字と字のあいだに足す空き（長体の率をかける前のピクセル）。
    /// </summary>
    public void Draw(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint, SKPaint? outline,
        float tracking = 0f)
    {
        if (!HasSubstitutes)
        {
            DrawSingle(canvas, text, x, baseline, align, font, paint, outline, tracking);
            return;
        }

        // 書体ごとの断片を左から続けて描く。揃えは全体の幅で決める。縁取りは全部の断片ぶんを先に描いてから塗る
        // （断片ごとに縁取りと塗りを続けると、あとの断片の縁取りが前の断片の字にかぶる）。
        float width = Measure(text, font, tracking);
        float start = align switch
        {
            SKTextAlign.Right => x - width,
            SKTextAlign.Center => x - width / 2f,
            _ => x
        };
        var segments = Segment(text);
        void DrawPass(SKPaint pass)
        {
            float cursor = start;
            foreach (var (shaper, part) in segments)
            {
                using var partFont = shaper.FontLike(font);
                shaper.DrawSingle(canvas, part, cursor, baseline, SKTextAlign.Left, partFont, pass, null, tracking);
                cursor += (shaper.Shape(part, font.Size, tracking).Width + tracking) * font.ScaleX;
            }
        }
        if (outline is not null) DrawPass(outline);
        DrawPass(paint);
    }

    /// <summary><see cref="Draw(SKCanvas, string, float, float, SKTextAlign, SKFont, SKPaint, SKPaint?, float)"/> の、この書体だけで組む場合。</summary>
    private void DrawSingle(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint, SKPaint? outline,
        float tracking)
    {
        var run = Shape(text, font.Size, tracking);
        if (run.Glyphs.Length == 0) return;

        // 長体（ScaleX < 1）は Skia が字形を横に縮めて描くので、字送りも同じ率で詰める。
        float scaleX = font.ScaleX;
        float width = run.Width * scaleX;
        var positions = run.Positions;
        if (Math.Abs(scaleX - 1f) > 0.0001f)
        {
            positions = new SKPoint[run.Positions.Length];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = new SKPoint(run.Positions[i].X * scaleX, run.Positions[i].Y);
        }

        float startX = align switch
        {
            SKTextAlign.Right => x - width,
            SKTextAlign.Center => x - width / 2f,
            _ => x
        };

        using var builder = new SKTextBlobBuilder();
        var buffer = builder.AllocatePositionedRun(font, run.Glyphs.Length);
        buffer.SetGlyphs(run.Glyphs);
        buffer.SetPositions(positions);
        using var blob = builder.Build();
        if (blob is null) return;
        if (outline is not null) canvas.DrawText(blob, startX, baseline, outline);
        canvas.DrawText(blob, startX, baseline, paint);
    }

    public void Dispose()
    {
        _font.Dispose();
        _face.Dispose();
        _blob.Dispose();
        _stream.Dispose();
    }
}
