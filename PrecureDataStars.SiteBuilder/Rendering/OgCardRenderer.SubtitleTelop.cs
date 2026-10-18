using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Utilities;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// エピソード詳細ページのサブタイトルを、本編のサブタイトルテロップの体裁（白い字・黒いフチ・右下の黒い影）で
/// 背景透過の PNG に描く。書体の解決・字詰め・フチの比率は OGP カードの見出しと共通。
/// <para>
/// 組み方：改行は <c>title_rich_html</c> の <c>&lt;br&gt;</c> のとおりにし、行ごとに中央揃えで積む。
/// 行が最大幅に収まらないときは、その行だけ長体をかける（下限は作品ごとの <see cref="SubtitleTelopProfile.CondenseMin"/>）。
/// 書体の差し替え・斜体・振り仮名の大きさと高さは、作品ごとに <see cref="SubtitleTelopProfile"/> で持つ。
/// 下限でも収まらない行があるときは、全行の字をそろって小さくする。振り仮名は親字と同じ書体・同じ体裁で親字の上に載せ、
/// フチの太さと影のずれは親字と同じ幅にする（振り仮名の大きさに比例させると、細くて弱く見えるため）。
/// 画像は字の外形（フチ・影を含む）に余白を少し足した大きさで切り、ページ側は CSS で幅を最大 100% に抑えて縮める。
/// </para>
/// </summary>
public sealed partial class OgCardRenderer
{
    /// <summary>
    /// テロップ画像の描き方の版。作り置き（<see cref="SubtitleTelopRequest.CacheKey"/>）の鍵に入る。
    /// 寸法・フチ・影・字詰め・振り仮名の置き方など、同じサブタイトルと組み方から描ける画像が変わる変更をしたら 1 上げる
    /// （上げると全話の画像が次のビルドで描き直しになる。上げないと作り置きが使われ続ける）。
    /// </summary>
    public const int TelopRenderVersion = 1;

    /// <summary>出力の倍率（CSS ピクセル 1 つに対する画素数）。高精細の画面でも字がにじまないよう 2 倍で描く。</summary>
    public const int TelopPixelRatio = 2;

    /// <summary>字の大きさ（画素）。ページ上で 1 字 45px に見える大きさ。</summary>
    private const float TelopFontSize = 45f * TelopPixelRatio;

    /// <summary>画像の最大幅（画素、余白込み）。PC のサブタイトル欄の内側の幅に収まる大きさ。</summary>
    private const float TelopMaxWidth = 860f * TelopPixelRatio;

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
    /// <paramref name="TiltChars"/> は傾けて置く字（装飾の <see cref="TelopDecoration.CharTilt"/>）、<paramref name="PunctRunEm"/> は
    /// 「！」「？」が続くところの 1 字あたりの字送り（字の大きさに対する比、<see cref="TelopDecoration.PunctRunAdvance"/>）。
    /// どちらも null なら書体の字詰めのまま組む（OGP カードは使わない）。
    /// <paramref name="PunctFirstEm"/> は前の字の送りの端から、記号のまとまりの最初の字の中心までの距離（<see cref="TelopDecoration.PunctFirstCenter"/>）。
    /// <paramref name="PunctLoneEm"/> は単独の記号の中心の、前の字の送りの端からの距離（<see cref="TelopDecoration.PunctLoneCenter"/>）。
    private sealed record TelopSpacing(bool Solid, float BaseEm, float RubyEm, string? TiltChars = null, float? PunctRunEm = null,
        float? PunctFirstEm = null, float? PunctLoneEm = null);

    /// <summary>続けて組むと詰め方の変わる記号（「！」「？」）。</summary>
    private const string TelopPunct = "！？!?";

    /// <summary>
    /// 字間の組み方で別に置く字を切り出した、地の文の区切り。<c>Kind</c> は 0 ＝書体の字詰めで組むひと続き、
    /// 1 ＝「！」「？」のまとまり（2 字以上の連なりか、区切りの末尾の単独の字）の 1 字（字送りは <see cref="TelopSpacing.PunctRunEm"/>）、
    /// 2 ＝傾けて置く単独の字。
    /// </summary>
    private static List<(string Text, int Kind)> TelopPieces(string text, TelopSpacing spacing)
    {
        var elements = new List<string>();
        var en = StringInfo.GetTextElementEnumerator(text);
        while (en.MoveNext()) elements.Add(en.GetTextElement());
        bool IsPunct(int k) => elements[k].Length == 1 && TelopPunct.Contains(elements[k][0]);
        bool IsTilt(int k) => spacing.TiltChars is { } t && elements[k].Length == 1 && t.Contains(elements[k][0]);
        bool InRun(int k) => spacing.PunctRunEm is not null && IsPunct(k)
            && ((k > 0 && IsPunct(k - 1)) || (k + 1 < elements.Count && IsPunct(k + 1)) || k == elements.Count - 1);
        var pieces = new List<(string, int)>();
        var normal = new StringBuilder();
        for (int k = 0; k < elements.Count; k++)
        {
            int kind = InRun(k) ? 1 : IsTilt(k) ? 2 : 0;
            if (kind == 0) { normal.Append(elements[k]); continue; }
            if (normal.Length > 0) { pieces.Add((normal.ToString(), 0)); normal.Clear(); }
            pieces.Add((elements[k], kind));
        }
        if (normal.Length > 0) pieces.Add((normal.ToString(), 0));
        return pieces;
    }

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
    private float TelopMeasure(SKFont font, string text, TelopSpacing spacing, float em, bool keepPunctuation = false, bool afterChar = false)
    {
        var shaper = TelopShaper(font, spacing, keepPunctuation);
        if (spacing.TiltChars is null && spacing.PunctRunEm is null) return shaper.Measure(text, font, font.Size * em);
        return TelopPieceLayout(font, text, spacing, em, keepPunctuation, afterChar).Width;
    }

    /// <summary>
    /// 区切り（<see cref="TelopPieces"/>）ごとの置き場所と全体の幅。<c>X</c> は書体の字詰めで組むひと続きなら左端、記号と傾ける字なら字の中心。
    /// 区切りと区切りのあいだには字間の空きを足す。記号のまとまりは、最初の字の中心を前の字の送りの端から
    /// <see cref="TelopSpacing.PunctFirstEm"/>（無ければ字間の空き＋字送りの半分）に置き、続く字は字送りずつ右へ、まとまりの幅は最後の字の中心＋字送りの半分まで。
    /// </summary>
    /// <paramref name="afterChar"/> は、この区切りの前に（空白をはさまず）字の単位があるか。あれば先頭の記号のまとまりも、前の単位の字の
    /// 送りの端（呼び出し側が単位のあいだに足した字間の空きの手前）から位置を決める。
    private (List<(string Text, int Kind, float X)> Pieces, float Width) TelopPieceLayout(SKFont font, string text, TelopSpacing spacing, float em, bool keep,
        bool afterChar = false)
    {
        var shaper = TelopShaper(font, spacing, keep);
        var pieces = TelopPieces(text, spacing);
        var placed = new List<(string, int, float)>();
        float InkWidth(string ch)
        {
            var ink = shaper.InkExtent(ch, font, 0f);
            return ink.Right - ink.Left;
        }
        float gap = font.Size * em * font.ScaleX;
        float cursor = 0f, center = 0f;
        for (int k = 0; k < pieces.Count; k++)
        {
            var (t, kind) = pieces[k];
            if (kind == 1)
            {
                float slot = spacing.PunctRunEm!.Value * font.Size * font.ScaleX;
                bool first = k == 0 || pieces[k - 1].Kind != 1;
                if (first)
                {
                    bool lone = k + 1 >= pieces.Count || pieces[k + 1].Kind != 1;
                    float? firstEm = lone ? spacing.PunctLoneEm ?? spacing.PunctFirstEm : spacing.PunctFirstEm;
                    center = k == 0
                        ? afterChar && firstEm is float f0 ? cursor - gap + f0 * font.Size * font.ScaleX : cursor + slot / 2f
                        : cursor + (firstEm is float f ? f * font.Size * font.ScaleX : gap + slot / 2f);
                }
                else
                {
                    // 字送りは「！」どうしの間隔。字面の幅の違う記号（「？」など）は、幅の差の半分ずつ間隔を広げる
                    float bang = InkWidth("！");
                    center += slot + (InkWidth(pieces[k - 1].Text) - bang) / 2f + (InkWidth(t) - bang) / 2f;
                }
                placed.Add((t, 1, center));
                cursor = center + slot / 2f;
                continue;
            }
            if (k > 0) cursor += gap;
            float natural = shaper.Measure(t, font, font.Size * em);
            placed.Add((t, kind, kind == 0 ? cursor : cursor + natural / 2f));
            cursor += natural;
        }
        return (placed, cursor);
    }

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
    /// <param name="fontFamily">作品の本編テロップの書体名（<c>series_font_usages</c> のサブタイトルの行が指す <c>fonts.render_font_name</c>）。空か見つからなければ既定の見出し書体。</param>
    /// <param name="profile">作品ごとの組み方（字間、振り仮名の書体・斜体・大きさと高さ・はみ出し方、行間）。</param>
    /// <param name="outputFilePath">書き出し先の絶対パス（拡張子 .png）。</param>
    /// <returns>書き出した画像の大きさ（画素）。</returns>
    public (int Width, int Height)? RenderSubtitleTelop(string rubyHtml, string fontFamily, SubtitleTelopProfile profile, string outputFilePath)
    {
        var drawn = DrawTelopImage(rubyHtml, fontFamily, profile, null);
        if (drawn is null) return null;
        using var image = drawn.Image;
        PathUtil.EnsureParentDirectory(outputFilePath);
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(outputFilePath))
        {
            data.SaveTo(stream);
        }
        return (image.Width, image.Height);
    }

    /// <summary>場面の画像の地の色（中間の灰色。白いフチも黒いフチも見えるように）。</summary>
    private static readonly SKColor SceneBackground = new(0x80, 0x80, 0x80);

    /// <summary>
    /// フォントのページの場面の画像を描き、PNG に書き出す。灰色の地に、テロップを 1 つずつ測った大きさで描いて、
    /// 測った位置（行の左端か中心と、ベースライン）に置く。
    /// </summary>
    public (int Width, int Height)? RenderFontScene(FontScene scene, string outputFilePath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(scene.Width, FontScene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SceneBackground);
        foreach (var piece in scene.Pieces)
        {
            var drawn = DrawTelopImage(piece.Html, piece.FontFamily, piece.Profile, piece.SizePx);
            if (drawn is null) continue;
            using var image = drawn.Image;
            float left = piece.CenterX ? piece.X - drawn.LineWidth / 2f : piece.X;
            canvas.DrawImage(image, left - drawn.Left, piece.Y - drawn.Baseline, new SKSamplingOptions(SKFilterMode.Linear), null);
        }
        PathUtil.EnsureParentDirectory(outputFilePath);
        using (var snapshot = surface.Snapshot())
        using (var data = snapshot.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(outputFilePath))
        {
            data.SaveTo(stream);
        }
        return (scene.Width, FontScene.Height);
    }

    /// <summary>書体見本の画像の 1 辺（画素）。</summary>
    public const int SpecimenSize = 1200;

    /// <summary>
    /// 書体見本の画像（白地に黒い字の正方形）を描く。左上に「愛」、右下に「永」を大きく、右上と左下にその 4 分の 1 の大きさの字を
    /// 字間・行間ゼロ（仮想ボディどうしが接する）で 4 字 × 4 行に並べる。
    /// 字はサブタイトルで多く使われた字から選んだもの（<paramref name="topRight"/>・<paramref name="bottomLeft"/> は 16 字ずつ）。
    /// </summary>
    public (int Width, int Height)? RenderFontSpecimen(string fontFamily, string topRight, string bottomLeft, string outputFilePath)
    {
        var typeface = ResolveTypeface(fontFamily, _boldTypeface, out _);
        const int n = SpecimenSize;
        const float half = n / 2f;
        using var surface = SKSurface.Create(new SKImageInfo(n, n, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        // 大きな字（「愛」「永」）の仮想ボディの 1 辺。区画（画像の 4 分の 1）の中央に、少し余白を残して置く。
        // 字の並びは、その 4 分の 1 の大きさの字を仮想ボディどうしが接するよう（字間・行間ゼロ）4 字 × 4 行に並べ、大きな字と同じ大きさの四角に収める。
        float big = half * 0.92f;
        float margin = (half - big) / 2f;

        // 字を、仮想ボディ（幅は字の大きさの正方形、ベースラインは上端から 0.88）の左上を (x, y) に合わせて、横は字送りを中央に置く
        void DrawInBody(string ch, float x, float y, float size)
        {
            using var font = new SKFont(typeface, size);
            float w = font.MeasureText(ch);
            canvas.DrawText(ch, x + (size - w) / 2f, y + size * TelopAscentRatio, SKTextAlign.Left, font, paint);
        }
        void DrawGrid(string chars, float x0, float y0)
        {
            var elements = StringInfo.GetTextElementEnumerator(chars);
            int k = 0;
            float cell = big / 4f;
            while (elements.MoveNext() && k < 16)
            {
                DrawInBody(elements.GetTextElement(), x0 + cell * (k % 4), y0 + cell * (k / 4), cell);
                k++;
            }
        }

        DrawInBody("愛", margin, margin, big);
        DrawGrid(topRight, half + margin, margin);
        DrawGrid(bottomLeft, margin, half + margin);
        DrawInBody("永", half + margin, half + margin, big);

        PathUtil.EnsureParentDirectory(outputFilePath);
        using (var snapshot = surface.Snapshot())
        using (var data = snapshot.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(outputFilePath))
        {
            data.SaveTo(stream);
        }
        return (n, n);
    }

    /// <summary>
    /// 描いたテロップ 1 つ。<paramref name="Baseline"/>・<paramref name="Left"/>・<paramref name="LineWidth"/> は 1 行目のベースライン・
    /// 字の並びの左端・行の幅（画像の中の画素）。場面の画像に置くときの基準に使う。
    /// </summary>
    private sealed record TelopImage(SKImage Image, float Baseline, float Left, float LineWidth);

    /// <summary>
    /// テロップを描いた画像を作る。<paramref name="fixedSize"/> を渡すとその字の大きさ（画素）で描き、画像の幅に合わせて
    /// 字を小さくしたり長体をかけたりしない（場面の画像に、測った大きさのまま置くため）。描く字が無ければ null。
    /// </summary>
    private TelopImage? DrawTelopImage(string rubyHtml, string fontFamily, SubtitleTelopProfile profile, float? fixedSize)
    {
        var lines = ParseTelopLines(rubyHtml);
        if (lines.Count == 0) return null;
        // 空白の空きを測った値にそろえる（全角・半角の別によらず、空白のまとまり 1 つをこの幅にする）
        if (profile.SpaceEm is float space)
        {
            foreach (var line in lines)
                for (int i = 0; i < line.Count; i++)
                    if (line[i].IsGap) line[i] = line[i] with { GapEm = space };
        }

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
        var spacing = new TelopSpacing(profile.Solid, profile.LetterSpacingEm, profile.RubyLetterSpacingEm,
            profile.Decoration.CharTilt?.Chars, profile.Decoration.PunctRunAdvance, profile.Decoration.PunctFirstCenter,
            profile.Decoration.PunctLoneCenter);

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
        // 親字の斜体：ベースラインを軸に倒すので、字の上端は傾き × 字の高さだけ右へ出る。その分を左右の余白に足す。
        float baseSkew = (float)Math.Tan(profile.BaseObliqueDegrees * Math.PI / 180.0);
        float skewOverhang = Math.Abs(baseSkew) * TelopAscentRatio / 2f;
        var style = TelopStyle.From(profile);
        // 画素で重ねる描き方では、光彩・面取りなども含めた装飾の広がりから余白を決める
        bool raster = profile.RasterDecoration;
        float extraPadding = raster
            ? Math.Max(0f, TelopRaster.Extent(profile.Decoration) - TelopPaddingRatio + 0.015f)
            : style.ExtraPadding;
        float OuterWidth(float s) => 2f * s * (TelopPaddingRatio + extraPadding + edgeOverhang + skewOverhang);

        // 字の大きさを決める。いちばん長い行が長体の下限で収まらなければ、収まるところまで全行の字を小さくする。
        float size = fixedSize ?? TelopFontSize;
        SetTelopSize(baseFont, smallFont, size);
        float longest = lines.Max(line => TelopLineWidth(line, baseFont, smallFont, size, 1f, spacing));
        float room = fixedSize is null ? TelopMaxWidth - OuterWidth(size) : profile.LineMaxWidth ?? float.MaxValue;
        // 各話のテロップ画像：本編のいちばん長い行の幅（字の大きさに対する比）を超える行は、字を小さくせずその幅まで長体で詰める
        if (fixedSize is null && profile.LineMaxEm is float lineEm) room = Math.Min(room, lineEm * size);
        if (fixedSize is null && longest * profile.CondenseMin > room)
        {
            size *= room / (longest * profile.CondenseMin);
            SetTelopSize(baseFont, smallFont, size);
            room = TelopMaxWidth - OuterWidth(size);
            if (profile.LineMaxEm is float lineEm2) room = Math.Min(room, lineEm2 * size);
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

        float padding = size * (TelopPaddingRatio + extraPadding);
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

        // 親字の斜体は、行のベースラインを軸に倒し（上ほど右へ）、倒れた分の半分だけ左へ寄せて見た目の中央をそろえる。
        void BeginLine(SKCanvas c, float baseline)
        {
            c.Save();
            if (baseSkew == 0f) return;
            c.Translate(-baseSkew * size * TelopAscentRatio / 2f, baseline);
            c.Skew(-baseSkew, 0f);
            c.Translate(0f, -baseline);
        }

        if (raster)
        {
            // 画素で重ねる描き方：親字と振り仮名の字の形を別々の面に塗り、装飾の層を重ねた画像を作って貼る
            var maskInfo = new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul);
            using var baseMask = new SKBitmap(maskInfo);
            using var rubyMask = new SKBitmap(maskInfo);
            using (var baseCanvas = new SKCanvas(baseMask))
            using (var rubyCanvas = new SKCanvas(rubyMask))
            {
                baseCanvas.Clear(SKColors.Transparent);
                rubyCanvas.Clear(SKColors.Transparent);
                var centers = new List<float>();
                for (int i = 0; i < lines.Count; i++)
                {
                    float baseline = lineTops[i] + size * TelopAscentRatio;
                    float x = (width - widths[i]) / 2f;
                    BeginLine(baseCanvas, baseline);
                    BeginLine(rubyCanvas, baseline);
                    DrawTelopLine(baseCanvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], rubyLayout, spacing, style,
                        new TelopMaskTarget(rubyCanvas, centers));
                    baseCanvas.Restore();
                    rubyCanvas.Restore();
                }
                baseCanvas.Flush();
                rubyCanvas.Flush();
                using var composed = TelopRaster.Compose(width, height, ReadMask(baseMask), ReadMask(rubyMask), centers, profile.Decoration, size);
                using var composedImage = SKImage.FromBitmap(composed);
                canvas.DrawImage(composedImage, 0f, 0f, new SKSamplingOptions(SKFilterMode.Nearest), null);
            }
        }
        else
        {
            for (int i = 0; i < lines.Count; i++)
            {
                float baseline = lineTops[i] + size * TelopAscentRatio;
                float x = (width - widths[i]) / 2f;
                BeginLine(canvas, baseline);
                DrawTelopLine(canvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], rubyLayout, spacing, style);
                canvas.Restore();
            }
        }

        return new TelopImage(surface.Snapshot(), lineTops[0] + size * TelopAscentRatio, (width - widths[0]) / 2f, widths[0]);
    }

    private static int RoundUpToMultiple(float value, int multiple) =>
        (int)Math.Ceiling(value / multiple) * multiple;

    /// <summary>字の形を塗った面（Alpha8）の濃さ（0〜1、行優先）。</summary>
    private static float[] ReadMask(SKBitmap mask)
    {
        var span = mask.GetPixelSpan();
        int w = mask.Width, h = mask.Height, rowBytes = mask.RowBytes;
        var a = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                a[y * w + x] = span[y * rowBytes + x] / 255f;
        return a;
    }

    /// <summary>
    /// 画素で重ねる描き方で、字の形だけを塗る先。親字は呼び出し側のキャンバスに、振り仮名は <paramref name="Ruby"/> に塗り、
    /// 親字の 1 字ずつの中心の x を <paramref name="CharCenters"/> に足す（字ごとの色を割り当てるため）。
    /// </summary>
    private sealed record TelopMaskTarget(SKCanvas Ruby, List<float> CharCenters);

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
                bool afterChar = previous is { IsGap: false };
                if (afterChar) width += font.Size * spacing.BaseEm * scaleX;
                width += TelopMeasure(font, unit.Base, spacing, spacing.BaseEm, TelopContinues(line, i), afterChar);
            }
            previous = unit;
        }
        return width;
    }

    /// <summary>
    /// 1 行を描く。影 → フチ → 白い字の順に、行の全単位をそれぞれ重ねる（単位ごとに 3 つを重ねると、
    /// 後の単位のフチが前の単位の白い字に食い込むため）。振り仮名は親字のあとに、同じ順で重ねる。
    /// <paramref name="mask"/> があれば装飾は描かず、字の形だけを塗る（親字は <paramref name="canvas"/>、振り仮名は <see cref="TelopMaskTarget.Ruby"/>）。
    /// </summary>
    private void DrawTelopLine(
        SKCanvas canvas, IReadOnlyList<TelopUnit> line, float x, float baseline,
        SKFont baseFont, SKFont smallFont, SKFont rubyFont, float size, float scaleX, TelopRubyLayout rubyLayout, TelopSpacing spacing,
        TelopStyle style, TelopMaskTarget? mask = null)
    {
        baseFont.ScaleX = scaleX;
        smallFont.ScaleX = scaleX;

        // 各単位の起点と占有幅を先に決める。単位と単位のあいだにも、字と字のあいだと同じ空きを足す。
        var placed = new List<(TelopUnit Unit, SKFont Font, float X, float Slot, bool AfterGap)>();
        var continues = new HashSet<TelopUnit>(ReferenceEqualityComparer.Instance);
        var afterChars = new HashSet<TelopUnit>(ReferenceEqualityComparer.Instance);
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
            bool afterChar = placed.Count > 0 && !afterGap;
            if (afterChar) afterChars.Add(unit);
            float slot = TelopMeasure(font, unit.Base, spacing, spacing.BaseEm, keep, afterChar);
            placed.Add((unit, font, cursor, slot, afterGap));
            cursor += slot;
            afterGap = false;
        }
        void DrawBase(TelopUnit unit, SKFont font, float ux, float y, SKPaint paint, SKPaint? outline)
        {
            bool keep = continues.Contains(unit);
            var shaper = TelopShaper(font, spacing, keep);
            float em = spacing.BaseEm;
            var pieces = spacing.TiltChars is null && spacing.PunctRunEm is null ? null : TelopPieces(unit.Base, spacing);
            if (pieces is null || (pieces.Count == 1 && pieces[0].Kind == 0))
            {
                shaper.Draw(canvas, unit.Base, ux, y, SKTextAlign.Left, font, paint, outline, font.Size * em);
                return;
            }
            // 区切りごとに置く（置き場所は TelopPieceLayout と同じ）。傾ける字はベースラインの中央を軸に回して置く
            var tilt = style.Tilt;
            foreach (var (text, kind, px) in TelopPieceLayout(font, unit.Base, spacing, em, keep, afterChars.Contains(unit)).Pieces)
            {
                if (kind == 0)
                {
                    shaper.Draw(canvas, text, ux + px, y, SKTextAlign.Left, font, paint, outline, font.Size * em);
                    continue;
                }
                float natural = shaper.Measure(text, font, font.Size * em);
                if (tilt is not null && tilt.Chars.Contains(text[0]))
                {
                    float cx = ux + px + tilt.Dx * font.Size;
                    float cy = y + tilt.Dy * font.Size;
                    canvas.Save();
                    canvas.RotateDegrees(tilt.Angle, cx, cy);
                    shaper.Draw(canvas, text, cx - natural / 2f, cy, SKTextAlign.Left, font, paint, outline, font.Size * em);
                    canvas.Restore();
                }
                else
                {
                    shaper.Draw(canvas, text, ux + px - natural / 2f, y, SKTextAlign.Left, font, paint, outline, font.Size * em);
                }
            }
        }

        var rubies = PlaceTelopRubies(placed.Select(p =>
            {
                var ink = TelopShaper(p.Font, spacing, continues.Contains(p.Unit))
                    .InkExtent(p.Unit.Base, p.Font, p.Font.Size * spacing.BaseEm);
                return (p.Unit.Ruby, p.Font, p.X, p.Slot, p.AfterGap, p.X + ink.Left, p.X + ink.Right);
            }).ToList(),
            rubyFont, size, scaleX, rubyLayout, spacing);

        if (mask is not null)
        {
            // 字の形だけを白で塗る。親字は 1 字ずつの中心（字送りの幅の中央）を控える。
            using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
            foreach (var (unit, font, ux, _, _) in placed)
            {
                DrawBase(unit, font, ux, baseline, white, null);
                bool keep = continues.Contains(unit);
                var elements = StringInfo.GetTextElementEnumerator(unit.Base);
                var prefix = new StringBuilder();
                float before = 0f;
                while (elements.MoveNext())
                {
                    prefix.Append(elements.GetTextElement());
                    float after = TelopMeasure(font, prefix.ToString(), spacing, spacing.BaseEm, keep);
                    mask.CharCenters.Add(ux + (before + after) / 2f);
                    before = after + font.Size * spacing.BaseEm * scaleX;
                }
            }
            DrawTelopRubyPass(mask.Ruby, rubies, baseline, rubyFont, rubyLayout, spacing, style, null, SKColors.White);
            return;
        }

        // 親字と振り仮名をひとまとまりとして、装飾の層を下から順に重ね、最後に字を塗る（振り仮名のフチが親字の字にかぶらない）。
        foreach (var layer in style.Layers)
        {
            foreach (var (unit, font, ux, _, _) in placed)
            {
                var (layerFill, layerStroke) = style.Paints(layer, font.Size);
                using (layerFill)
                using (layerStroke)
                {
                    if (layerFill is null && layerStroke is null) continue;
                    DrawBase(unit, font, ux + font.Size * layer.Dx, baseline + font.Size * layer.Dy,
                        layerFill ?? layerStroke!, layerFill is null ? null : layerStroke);
                }
            }
            DrawTelopRubyPass(canvas, rubies, baseline, rubyFont, rubyLayout, spacing, style, layer);
        }
        // 字。
        using (var fill = new SKPaint { IsAntialias = true, Color = style.Fill })
        {
            foreach (var (unit, font, ux, _, _) in placed)
                DrawBase(unit, font, ux, baseline, fill, null);
        }
        DrawTelopRubyPass(canvas, rubies, baseline, rubyFont, rubyLayout, spacing, style, null);
    }

    /// <summary>
    /// テロップの装飾の 1 層（影かフチ）。色は濃さ込み。<see cref="Width"/> はフチの太さ、影では影の形の太さ（字の輪郭から外へ）。
    /// ずれ・ぼかしも字の大きさに対する比で、フチのずれは 0。
    /// </summary>
    private sealed record TelopLayer(bool IsShadow, SKColor Color, float Width, float Dx, float Dy, float Blur);

    /// <summary>
    /// テロップの体裁（字の色・フチの角の形・字の下に重ねる層）。装飾（<see cref="TelopDecoration"/>）から作り、
    /// 影の形の太さの省略はいちばん太いフチの外形に解決しておく。
    /// </summary>
    private sealed record TelopStyle(SKColor Fill, bool Miter, IReadOnlyList<TelopLayer> Layers)
    {
        /// <summary>決まった字だけを傾ける指定（装飾の <see cref="TelopDecoration.CharTilt"/>）。</summary>
        public TelopCharTilt? Tilt { get; init; }

        public static TelopStyle From(SubtitleTelopProfile p) => From(p.Decoration);

        public static TelopStyle From(TelopDecoration d)
        {
            static SKColor Parse(string hex, SKColor fallback) =>
                !string.IsNullOrEmpty(hex) && SKColor.TryParse(hex, out var c) ? c : fallback;
            float outermost = d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindOutline).Select(l => l.Width).DefaultIfEmpty(0f).Max();
            // 面取りは輪郭の線では描けないので、線で描く体裁には入れない（画素で重ねる描き方だけが描く）
            var layers = d.Layers.Where(l => l.Kind != TelopDecorationLayer.KindBevel).Select(l =>
            {
                bool shadow = l.Kind == TelopDecorationLayer.KindShadow;
                var color = Parse(l.Color, SKColors.Black).WithAlpha((byte)Math.Clamp(Math.Round(l.Opacity * 255), 0, 255));
                return shadow
                    ? new TelopLayer(true, color, l.Spread ?? outermost, l.Dx, l.Dy, l.Blur)
                    : new TelopLayer(false, color, l.Width, 0f, 0f, l.Blur);
            }).ToList();
            return new TelopStyle(Parse(d.Fill, SKColors.White), string.Equals(d.Join, "MITER", StringComparison.OrdinalIgnoreCase), layers)
            {
                Tilt = d.CharTilt
            };
        }

        /// <summary>層が既定の余白（<see cref="TelopPaddingRatio"/>）からはみ出す分（字の大きさに対する比）。</summary>
        public float ExtraPadding =>
            Math.Max(0f, Layers.Select(l => l.Width + Math.Max(Math.Abs(l.Dx), Math.Abs(l.Dy)) + l.Blur * 3f).DefaultIfEmpty(0f).Max()
                - TelopPaddingRatio + 0.015f);

        /// <summary>層の線の塗り（フチ・影の外形）。線は輪郭を中心に引かれるので、見せたい太さの 2 倍にする。</summary>
        public SKPaint Stroke(float width, SKColor color) => new()
        {
            IsAntialias = true, Color = color, Style = SKPaintStyle.Stroke, StrokeWidth = width * 2f,
            StrokeJoin = Miter ? SKStrokeJoin.Miter : SKStrokeJoin.Round, StrokeCap = Miter ? SKStrokeCap.Butt : SKStrokeCap.Round
        };

        /// <summary>
        /// 1 層の塗り（字の形の塗りと、輪郭の線）。<paramref name="size"/> は層の太さ・ぼかしの基準の字の大きさ。
        /// 影は字の形を塗って線で太らせる。フチは線だけで、ぼかすときは字の内側も塗る（線だけをぼかすと内側が薄くなるため）。
        /// 太さ 0 の層は線を持たない。ぼかしがあれば両方にガウスぼかしをかける。
        /// </summary>
        public (SKPaint? Fill, SKPaint? Stroke) Paints(TelopLayer layer, float size)
        {
            SKPaint? fill = layer.IsShadow || layer.Blur > 0f ? new SKPaint { IsAntialias = true, Color = layer.Color } : null;
            SKPaint? stroke = layer.Width > 0f ? Stroke(size * layer.Width, layer.Color) : null;
            if (layer.Blur > 0f)
            {
                if (fill is not null) fill.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * layer.Blur);
                if (stroke is not null) stroke.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * layer.Blur);
            }
            return (fill, stroke);
        }
    }

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
    /// 振り仮名をテロップの体裁の 1 層だけ描く（<paramref name="layer"/> が null なら字の色で塗る層。<paramref name="fillOverride"/> があればその色で塗る）。
    /// 層の太さ・ずれ・ぼかしは親字の字の大きさから決める。
    /// 斜体の組み方では、振り仮名ごとにそのベースラインを軸に倒し（上ほど右へ）、倒れた分の半分だけ左へ寄せて見た目の中央をそろえる。
    /// </summary>
    private void DrawTelopRubyPass(
        SKCanvas canvas, IReadOnlyList<TelopRubyGlyph> rubies, float baseline, SKFont rubyFont, TelopRubyLayout rubyLayout,
        TelopSpacing spacing, TelopStyle style, TelopLayer? layer, SKColor? fillOverride = null)
    {
        if (rubies.Count == 0) return;
        var rubyShaper = TelopShaper(rubyFont, spacing);
        foreach (var (ruby, rx, rubyScale, rubySize, baseSize) in rubies)
        {
            rubyFont.Size = rubySize;
            rubyFont.ScaleX = rubyScale;
            var (rubyFill, rubyStroke) = layer is null
                ? ((SKPaint?)new SKPaint { IsAntialias = true, Color = fillOverride ?? style.Fill }, (SKPaint?)null)
                : style.Paints(layer, baseSize);
            using (rubyFill)
            using (rubyStroke)
            {
                if (rubyFill is null && rubyStroke is null) continue;
                float dx = layer is null ? 0f : baseSize * layer.Dx;
                float dy = layer is null ? 0f : baseSize * layer.Dy;
                float rb = baseline - baseSize * rubyLayout.RaiseRatio;
                canvas.Save();
                if (rubyLayout.Skew != 0f)
                {
                    canvas.Translate(0f, rb);
                    canvas.Skew(-rubyLayout.Skew, 0f);
                    canvas.Translate(0f, -rb);
                }
                rubyShaper.Draw(canvas, ruby, rx - rubyLayout.Skew * rubySize * TelopAscentRatio / 2f + dx, rb + dy, SKTextAlign.Left, rubyFont,
                    rubyFill ?? rubyStroke!, rubyFill is null ? null : rubyStroke, rubyFont.Size * spacing.RubyEm);
                canvas.Restore();
            }
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
