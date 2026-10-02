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

    private readonly SKTypeface _typeface;
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
    public OgTextShaper(SKTypeface typeface, bool? opticalTightening = null)
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
        UsesOpticalTightening = opticalTightening ?? !(HasGposFeature(typeface, "kern") && HasGposFeature(typeface, "palt"));
    }

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

    /// <summary>文字列を指定サイズで組む。空文字なら幅 0 の空の結果。</summary>
    public ShapedRun Shape(string text, float fontSize)
    {
        if (string.IsNullOrEmpty(text)) return new ShapedRun(Array.Empty<ushort>(), Array.Empty<SKPoint>(), 0f);

        float scale = fontSize / _unitsPerEm;
        lock (_lock)
        {
            using var buffer = new HbBuffer();
            buffer.AddUtf16(text);
            buffer.GuessSegmentProperties();
            _font.Shape(buffer, Features);

            var infos = buffer.GlyphInfos;
            var positions = buffer.GlyphPositions;
            int count = infos.Length;
            var glyphs = new ushort[count];
            var points = new SKPoint[count];
            for (int i = 0; i < count; i++) glyphs[i] = (ushort)infos[i].Codepoint;

            if (UsesOpticalTightening)
                return TightenByInk(glyphs, positions, fontSize, scale);

            float x = 0f;
            for (int i = 0; i < count; i++)
            {
                points[i] = new SKPoint(x + positions[i].XOffset * scale, -positions[i].YOffset * scale);
                x += positions[i].XAdvance * scale;
            }
            return new ShapedRun(glyphs, points, x);
        }
    }

    /// <summary>
    /// 字面に基づく詰め組み。各字形のインクの左右端を測り、「前の字面の右端 ＋ 空き」に次の字面の左端を揃えて送る。
    /// 元の字送りより広げることはしない（インクが広い字はそのまま）。インクの無い字（空白）は元の字送りのまま。
    /// 「！」「・」のように字面の細い字が、全角のまま間延びしないようにする。
    /// </summary>
    private ShapedRun TightenByInk(ushort[] glyphs, GlyphPosition[] positions, float fontSize, float scale)
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

    /// <summary>組んだ幅（ピクセル）。フォントの横方向の拡大率（<see cref="SKFont.ScaleX"/>＝長体）を含む。</summary>
    public float Measure(string text, SKFont font) => Shape(text, font.Size).Width * font.ScaleX;

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
    /// </summary>
    public void Draw(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint, SKPaint? outline)
    {
        var run = Shape(text, font.Size);
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
