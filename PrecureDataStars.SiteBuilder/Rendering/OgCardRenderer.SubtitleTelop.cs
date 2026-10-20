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
/// 行が最大幅（本編のいちばん長い行の幅）に収まらないときは、本編と同じくその行だけ長体をかける。字の大きさ（仮想ボディの高さ）はどの回も同じで、
/// 長い行も字を小さくせず長体だけで収める。
/// 書体の差し替え・斜体・振り仮名の大きさと高さは、作品ごとに <see cref="SubtitleTelopProfile"/> で持つ。振り仮名は親字と同じ書体・同じ体裁で親字の上に載せ、
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
    public const int TelopRenderVersion = 2;

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
    /// 見積もった画像の外に足して描く余白（字の大きさに対する比）。字の形は字送りの外へはみ出すことがある
    /// （傾けて置く「！」、行の端の振り仮名、回ごとの行のずれなど）ので、この余白ごと描いてから、描いた字の端に合わせて切り抜く。
    /// </summary>
    private const float TelopBleedRatio = 0.5f;

    /// <summary>切り抜くとき、描いた字の端の外に残す透明な空き（画素）。</summary>
    private const int TelopBleedGap = 2;

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
    /// <paramref name="Tilt"/> は傾けて置く字の傾き（字面が前後の字にかからないかを見るのに使う）。
    /// <paramref name="PunctMid"/> は行の途中の単独の記号も行末と同じ詰め方で置くか（<see cref="TelopDecoration.PunctMidLone"/>）。
    /// <paramref name="PunctHalf"/> はベタ組みで全角の約物だけを半分の幅に詰めるか（<see cref="SubtitleTelopProfile.PunctHalf"/>）、
    /// <paramref name="LatinEm"/> は親字の欧文と約物のあとに足す空き（<see cref="SubtitleTelopProfile.LatinSpacingEm"/>）、
    /// <paramref name="SmallKana"/> はベタ組みで小書きの仮名にかける詰めの割合（<see cref="SubtitleTelopProfile.SmallKanaPalt"/>）。
    private sealed record TelopSpacing(bool Solid, float BaseEm, float RubyEm, string? TiltChars = null, float? PunctRunEm = null,
        float? PunctFirstEm = null, float? PunctLoneEm = null, TelopCharTilt? Tilt = null, bool PunctMid = false,
        bool PunctHalf = false, float LatinEm = 0f, float SmallKana = 0f);

    /// <summary>続けて組むと詰め方の変わる記号（「！」「？」）。</summary>
    private const string TelopPunct = "！？!?";

    /// <summary>記号と前後の字の字面どうしのあいだに、少なくとも残す空き（字の大きさに対する比）。</summary>
    private const float TelopPunctMinGapEm = 0.08f;

    /// <summary>
    /// 字間の組み方で別に置く字を切り出した、地の文の区切り。<c>Kind</c> は 0 ＝書体の字詰めで組むひと続き、
    /// 1 ＝「！」「？」のまとまり（2 字以上の連なりか、行末の単独の字。<see cref="TelopSpacing.PunctMid"/> なら行の途中の単独の字も）の 1 字（字送りは <see cref="TelopSpacing.PunctRunEm"/>）、
    /// 2 ＝傾けて置く単独の字。
    /// </summary>
    /// <paramref name="continues"/> は、この単位のあとに（空白をはさまず）字の単位が続くか（続くなら末尾の単独の記号は行の途中のもの）。
    private static List<(string Text, int Kind)> TelopPieces(string text, TelopSpacing spacing, bool continues = false)
    {
        var elements = new List<string>();
        var en = StringInfo.GetTextElementEnumerator(text);
        while (en.MoveNext()) elements.Add(en.GetTextElement());
        bool IsPunct(int k) => elements[k].Length == 1 && TelopPunct.Contains(elements[k][0]);
        bool IsTilt(int k) => spacing.TiltChars is { } t && elements[k].Length == 1 && t.Contains(elements[k][0]);
        bool InRun(int k) => spacing.PunctRunEm is not null && IsPunct(k)
            && (spacing.PunctMid || (k > 0 && IsPunct(k - 1)) || (k + 1 < elements.Count && IsPunct(k + 1)) || (k == elements.Count - 1 && !continues));
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
    /// テロップ用の組版器（(書体のハンドル, ベタ組みか, 単独の「！」「？」を全角に置くか, 約物半角か, 欧文の字間, 小書きの仮名の詰め) → 組版器）。
    /// テロップの作品が初めて使うときに作る（OGP カードの組版器とは別に持つ）。
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(IntPtr Handle, bool Solid, bool KeepPunctuation, bool PunctHalf, float LatinEm, float SmallKana), Lazy<OgTextShaper>> _telopShapers = new();

    /// <summary>
    /// 字間の組み方に合った組版器。詰める組み方で <paramref name="keepPunctuation"/> なら、単独の「！」「？」を全角のまま置く
    /// （本編のテロップは、行の途中の「！」「？」を全角で組み、行末のものは前の字に寄せる。呼び出し側は行末の単位で false を渡す）。
    /// </summary>
    private OgTextShaper TelopShaper(SKFont font, TelopSpacing spacing, bool keepPunctuation = false)
    {
        var typeface = font.Typeface ?? throw new InvalidOperationException("書体の無いフォントです。");
        return _telopShapers.GetOrAdd((typeface.Handle, spacing.Solid, keepPunctuation && !spacing.Solid, spacing.PunctHalf && spacing.Solid, spacing.LatinEm,
                spacing.Solid ? spacing.SmallKana : 0f),
            key => new Lazy<OgTextShaper>(() => key.Solid
                ? new OgTextShaper(typeface, opticalTightening: false, solid: true, punctHalf: key.PunctHalf, latinTrackingEm: key.LatinEm,
                    smallKanaPalt: key.SmallKana)
                : new OgTextShaper(typeface, keepLonePunctuation: key.KeepPunctuation, latinTrackingEm: key.LatinEm))).Value;
    }

    /// <summary>
    /// 区切り（<see cref="TelopPieces"/>）の字を組む組版器。記号を別に置く作品（<see cref="TelopSpacing.PunctRunEm"/> あり）では、
    /// 区切りの中の単独の「！」「？」は行の途中のものなので、行末かどうかによらず全角に置く。
    /// </summary>
    private OgTextShaper TelopPieceShaper(SKFont font, TelopSpacing spacing, bool keep)
        => TelopShaper(font, spacing, keep || spacing.PunctRunEm is not null);

    /// <summary>字間の組み方を効かせた文字列の幅。<paramref name="em"/> は字と字のあいだに足す空き（字の大きさに対する比）。</summary>
    private float TelopMeasure(SKFont font, string text, TelopSpacing spacing, float em, bool keepPunctuation = false, string? previousChar = null)
    {
        var shaper = TelopShaper(font, spacing, keepPunctuation);
        if (spacing.TiltChars is null && spacing.PunctRunEm is null) return shaper.Measure(text, font, font.Size * em);
        return TelopPieceLayout(font, text, spacing, em, keepPunctuation, previousChar).Width;
    }

    /// <summary>
    /// 区切り（<see cref="TelopPieces"/>）ごとの置き場所と全体の幅。<c>X</c> は書体の字詰めで組むひと続きなら左端、記号と傾ける字なら字の中心。
    /// 区切りと区切りのあいだには字間の空きを足す。記号のまとまりは、最初の字の中心を前の字の送りの端から
    /// <see cref="TelopSpacing.PunctFirstEm"/>（無ければ字間の空き＋字送りの半分）に置き、続く字は字送りずつ右へ、まとまりの幅は最後の字の中心＋字送りの半分まで。
    /// </summary>
    /// <paramref name="previousChar"/> は、この区切りの前に（空白をはさまず）続く字の単位の最後の字（無ければ null）。あれば先頭の記号のまとまりも、
    /// 前の単位の字の送りの端（呼び出し側が単位のあいだに足した字間の空きの手前）から位置を決める。
    /// 記号と前後の字は、測った位置が詰まりすぎても字面どうしのあいだに <see cref="TelopPunctMinGapEm"/> の空きを残す。
    private (List<(string Text, int Kind, float X)> Pieces, float Width) TelopPieceLayout(SKFont font, string text, TelopSpacing spacing, float em, bool keep,
        string? previousChar = null)
    {
        bool afterChar = previousChar is not null;
        // 記号のまとまりは単位の組み方のまま、ほかの区切りは区切り用の組版器で組む
        var runShaper = TelopShaper(font, spacing, keep);
        var shaper = TelopPieceShaper(font, spacing, keep);
        var pieces = TelopPieces(text, spacing, keep);
        var placed = new List<(string, int, float)>();
        float InkWidth(string ch)
        {
            var ink = runShaper.InkExtent(ch, font, 0f);
            return ink.Right - ink.Left;
        }
        float gap = font.Size * em * font.ScaleX;
        float minGap = TelopPunctMinGapEm * font.Size * font.ScaleX;
        // 字送りの原点から見た、字面の左端・右端と字送り
        (float Left, float Right, float Advance) Ink(string ch, OgTextShaper? with = null)
        {
            var sh = with ?? shaper;
            var ink = sh.InkExtent(ch, font, 0f);
            return (ink.Left, ink.Right, sh.Measure(ch, font, 0f));
        }
        // 字の中心に置く字（記号・傾ける字）の、中心から見た字面の左端・右端。傾ける字は回したあとの字面で見る（lone は単独の字か）
        (float Left, float Right) CenteredInk(string ch, OgTextShaper? with = null, bool lone = true, bool qrun = false)
        {
            var (l, r, adv) = Ink(ch, with);
            if (spacing.Tilt is not { } tilt || !tilt.Chars.Contains(ch[0])) return (l - adv / 2f, r - adv / 2f);
            font.MeasureText(ch, out var b);
            double rad = TelopTiltAngle(tilt, lone, qrun) * Math.PI / 180.0;
            float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var x in new[] { b.Left - adv / 2f, b.Right - adv / 2f })
            foreach (var y in new[] { b.Top, b.Bottom })
            {
                float xr = x * cos - y * sin;
                lo = Math.Min(lo, xr); hi = Math.Max(hi, xr);
            }
            float dx = tilt.Dx * font.Size;
            return (lo + dx, hi + dx);
        }
        string LastElement(string s) => StringInfo.GetNextTextElement(s, StringInfo.ParseCombiningCharacters(s)[^1]);
        // 直前の字の字面の右端（この単位の起点からの位置）。前の単位の字は、単位のあいだの字間の空きの手前で送りが終わる
        float prevInkRight = float.NegativeInfinity;
        if (previousChar is { Length: > 0 })
        {
            var p = Ink(LastElement(previousChar));
            prevInkRight = -gap - p.Advance + p.Right;
        }
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
                // まとまりの最初の字は、字面が前の字にかからないよう中心を右へ逃がす（まとまりの中は測った字送りのまま。
                // 傾けた記号どうしは外接矩形が重なるほど詰めて組まれる）
                bool loneHere = (k == 0 || pieces[k - 1].Kind != 1) && (k + 1 >= pieces.Count || pieces[k + 1].Kind != 1);
                var pi = CenteredInk(t, runShaper, loneHere, !loneHere && TelopRunHasQuestion(pieces, k, q => q));
                if (first) center = Math.Max(center, prevInkRight + minGap - pi.Left);
                placed.Add((t, 1, center));
                prevInkRight = center + pi.Right;
                cursor = center + slot / 2f;
                continue;
            }
            if (k > 0) cursor += gap;
            float natural = shaper.Measure(t, font, font.Size * em);
            if (kind == 0)
            {
                // 記号・傾ける字のあとの字は、字面が前の字にかからないところから組む
                if (k > 0 && pieces[k - 1].Kind != 0)
                    cursor = Math.Max(cursor, prevInkRight + minGap - Ink(StringInfo.GetNextTextElement(t, 0)).Left);
                placed.Add((t, 0, cursor));
                cursor += natural;
                var last = Ink(LastElement(t));
                prevInkRight = cursor - last.Advance + last.Right;
            }
            else
            {
                // 傾ける字：字送りは字の幅。前の字にかからないよう中心を右へ逃がす
                var ti = CenteredInk(t);
                float c = Math.Max(cursor + natural / 2f, prevInkRight + minGap - ti.Left);
                placed.Add((t, kind, c));
                cursor = c + natural / 2f;
                prevInkRight = c + ti.Right;
            }
        }
        // 末尾が記号・傾ける字で、あとに字の単位が続くなら、次の字（呼び出し側が字間の空きを足して置く）が字面にかからない幅を取る
        if (keep && pieces.Count > 0 && pieces[^1].Kind != 0) cursor = Math.Max(cursor, prevInkRight + minGap - gap);
        return (placed, cursor);
    }

    /// <summary>
    /// 傾ける字の角度。単独の字は <see cref="TelopCharTilt.LoneAngle"/>、「？」が混ざるまとまりの字は <see cref="TelopCharTilt.QuestionRunAngle"/>
    /// （どちらも無ければ <see cref="TelopCharTilt.Angle"/>）。
    /// </summary>
    private static float TelopTiltAngle(TelopCharTilt tilt, bool lone, bool questionRun = false)
        => lone ? tilt.LoneAngle ?? tilt.Angle : questionRun ? tilt.QuestionRunAngle ?? tilt.Angle : tilt.Angle;

    /// <summary>区切りの並びの i 番目を含む記号のまとまり（Kind 1 の連なり）に「？」が混ざるか。</summary>
    private static bool TelopRunHasQuestion<T>(IReadOnlyList<T> pieces, int i, Func<T, (string Text, int Kind)> get)
    {
        int a = i, b = i;
        while (a > 0 && get(pieces[a - 1]).Kind == 1) a--;
        while (b + 1 < pieces.Count && get(pieces[b + 1]).Kind == 1) b++;
        for (int k = a; k <= b; k++)
            if (get(pieces[k]).Text.IndexOfAny(['？', '?']) >= 0) return true;
        return false;
    }

    /// <summary>
    /// 単位 <paramref name="unit"/> の最後の字のあとに足す欧文の字間（字の大きさに対する比）。最後の字が欧文・約物なら
    /// <see cref="TelopSpacing.LatinEm"/>、ほかは 0。字の単位が続くときも空白が続くときも、この空きを足す。
    /// </summary>
    private static float TelopLatinAfter(TelopUnit unit, TelopSpacing spacing)
        => spacing.LatinEm != 0f && unit.Base.Length > 0 && OgTextShaper.IsLatinSpaced(unit.Base[^1]) ? spacing.LatinEm : 0f;

    /// <summary>行の i 番目の単位のすぐあとに（空白をはさまず）字の単位が続くか。続くなら単独の「！」「？」を全角に置く。</summary>
    private static bool TelopContinues(IReadOnlyList<TelopUnit> line, int i) => i + 1 < line.Count && !line[i + 1].IsGap;

    /// <summary>
    /// 振り仮名の組み方（作品ごとの組み方から描画に使う値だけを取り出したもの）。
    /// <paramref name="SizeRatio"/>・<paramref name="RaiseRatio"/>・<paramref name="OverhangRatio"/> は親字の大きさに対する比、
    /// <paramref name="Skew"/> は斜体の傾き（tan）、<paramref name="OverhangLineStart"/>・<paramref name="OverhangLineEnd"/> は
    /// 行頭・行末の外へはみ出させるか、<paramref name="Grouping"/> は振り仮名の置き方。
    /// </summary>
    /// <paramref name="KeepWidth"/> は、親字の行に長体をかけても振り仮名は細くしない（放送で振り仮名の幅が変わらない作品）か。
    private sealed record TelopRubyLayout(
        float SizeRatio, float RaiseRatio, float Skew, float OverhangRatio, bool OverhangLineStart, bool OverhangLineEnd,
        SubtitleRubyGrouping Grouping, bool KeepWidth = false);

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
        var drawn = DrawTelopImage(rubyHtml, fontFamily, SceneGlyphOnly ? GlyphOnly(profile) : profile, null);
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

    /// <summary>
    /// 場面の画像の地：明るさの近い 2 つの灰色の市松模様（透過の地であることを示し、白いフチも暗い影も、半透明の光や影も見えるように）。
    /// マスの 1 辺は画面の高さ 1080 に対する画素で、1920×1080 の画面がちょうど横 32・縦 18 マス（4:3 の 1440 幅は横 24 マス）に割り切れる大きさ。
    /// </summary>
    private static readonly SKColor SceneBackground = new(0x9A, 0x9A, 0x9A);

    private static readonly SKColor SceneBackgroundAlt = new(0xA8, 0xA8, 0xA8);

    private const int SceneCheckerCell = 60;

    /// <summary>場面の画像の地の市松模様を描く。</summary>
    private static void DrawSceneBackground(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SceneBackground);
        using var paint = new SKPaint { Color = SceneBackgroundAlt, IsAntialias = false };
        for (int y = 0; y < height; y += SceneCheckerCell)
            for (int x = (y / SceneCheckerCell % 2) * SceneCheckerCell; x < width; x += SceneCheckerCell * 2)
                canvas.DrawRect(x, y, SceneCheckerCell, SceneCheckerCell, paint);
    }

    /// <summary>
    /// 環境変数 <c>PRECURE_FONT_SCENE_TRANSPARENT</c> が <c>1</c> なら、場面の画像を地の色を塗らない透過 PNG で描く（体裁の測定用）。
    /// 字の濃さ（透明度）がそのまま残るので、放送のノンテロップの画面に重ねて画素ごとに比べられる。作り置きの鍵も分ける。
    /// </summary>
    internal static readonly bool SceneTransparent = Environment.GetEnvironmentVariable("PRECURE_FONT_SCENE_TRANSPARENT") == "1";

    /// <summary>
    /// 環境変数 <c>PRECURE_FONT_SCENE_GLYPH</c> が <c>1</c> なら、場面の見本と各話のサブタイトル画像を装飾なし（白い塗りだけ）で描く（体裁の測定用）。
    /// 字の形そのものを放送の画面に合わせ、その上で装飾の層を当てはめるために使う。組み方（字の傾き・記号の詰め）はそのまま。作り置きの鍵も分ける。
    /// </summary>
    internal static readonly bool SceneGlyphOnly = Environment.GetEnvironmentVariable("PRECURE_FONT_SCENE_GLYPH") == "1";

    /// <summary>装飾を外し、白い塗りだけにした組み方（<see cref="SceneGlyphOnly"/> のとき）。</summary>
    private static SubtitleTelopProfile GlyphOnly(SubtitleTelopProfile profile) => profile with
    {
        Decoration = profile.Decoration with
        {
            Fill = "#FFFFFF", Layers = [], FillGradient = null, FillChars = null, FillGrow = 0f, InnerGlow = null, Stripes = null,
            RubyFill = null, RoleFill = null, RoleFillChars = null, Glow = null, Ruby = null,
        },
    };

    /// <summary>
    /// フォントのページの場面の画像を描き、PNG に書き出す。灰色の市松模様の地に、テロップを 1 つずつ測った大きさで描いて、
    /// 測った位置（行の左端か中心と、ベースライン）に置く。
    /// </summary>
    public (int Width, int Height)? RenderFontScene(FontScene scene, string outputFilePath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(scene.Width, FontScene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        if (SceneTransparent) canvas.Clear(SKColors.Transparent);
        else DrawSceneBackground(canvas, scene.Width, FontScene.Height);
        foreach (var scenePiece in scene.Pieces)
        {
            var piece = SceneGlyphOnly ? scenePiece with { Profile = GlyphOnly(scenePiece.Profile) } : scenePiece;
            var drawn = DrawTelopImage(piece.Html, piece.FontFamily, piece.Profile, piece.SizePx);
            if (drawn is null) continue;
            float left = piece.CenterX ? piece.X - drawn.LineWidth / 2f : piece.X;
            float ox = left - drawn.Left, oy = piece.Y - drawn.Baseline;
            // 画面全体にかかるグラデーションは、置く位置を差し引いて組みの座標に直し、描き直す（置く位置も切り抜き方も塗りによらない）
            if (piece.Profile.Decoration.FillGradient is { SceneLine: { Count: 4 } sl } fg)
            {
                drawn.Image.Dispose();
                float lx = ox + drawn.OriginX, ly = oy + drawn.OriginY;
                var local = fg with { Line = [sl[0] * scene.Width - lx, sl[1] * FontScene.Height - ly, sl[2] * scene.Width - lx, sl[3] * FontScene.Height - ly] };
                var profile = piece.Profile with { Decoration = piece.Profile.Decoration with { FillGradient = local } };
                drawn = DrawTelopImage(piece.Html, piece.FontFamily, profile, piece.SizePx);
                if (drawn is null) continue;
            }
            using var image = drawn.Image;
            canvas.DrawImage(image, ox, oy, new SKSamplingOptions(SKFilterMode.Linear), null);
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
    /// <paramref name="OriginX"/>・<paramref name="OriginY"/> は、組みの座標（装飾の <see cref="TelopGradient.Line"/> を測る座標）の原点が
    /// 画像の中のどこにあるか（字の端に合わせて切り抜いた分だけ、画像の左上からずれる）。
    /// </summary>
    private sealed record TelopImage(SKImage Image, float Baseline, float Left, float LineWidth, float OriginX = 0f, float OriginY = 0f);

    /// <summary>
    /// テロップを描いた画像を作る。<paramref name="fixedSize"/> を渡すとその字の大きさ（画素）で描き、画像の幅に合わせて
    /// 字を小さくしたり長体をかけたりしない（場面の画像に、測った大きさのまま置くため）。描く字が無ければ null。
    /// </summary>
    private TelopImage? DrawTelopImage(string rubyHtml, string fontFamily, SubtitleTelopProfile profile, float? fixedSize)
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
            profile.RubyOverhangRatio ?? rubyRatio, profile.RubyOverhangLineStart, profile.RubyOverhangLineEnd, profile.RubyGrouping,
            profile.RubyKeepWidth);
        // 行ごとの位置のずれ（回ごとに測った行の左右・上下のずれ。親字の大きさに対する比、右・下を正）
        (float Dx, float Dy) LineOffset(int line) =>
            profile.LineOffsets is { } lo && line < lo.Count && lo[line] is { Count: 2 } d ? (d[0], d[1]) : (0f, 0f);
        // 行ごとの振り仮名の組み方（回ごとに測った振り仮名の高さのずれがあれば、その行だけ高さを足す）
        TelopRubyLayout LineRubyLayout(int line) =>
            profile.RubyRaiseOffsets is { } offsets && line < offsets.Count && offsets[line] != 0f
                ? rubyLayout with { RaiseRatio = rubyLayout.RaiseRatio + offsets[line] }
                : rubyLayout;
        var spacing = new TelopSpacing(profile.Solid, profile.LetterSpacingEm, profile.RubyLetterSpacingEm,
            profile.Decoration.CharTilt?.Chars, profile.Decoration.PunctRunAdvance, profile.Decoration.PunctFirstCenter,
            profile.Decoration.PunctLoneCenter, profile.Decoration.CharTilt, profile.Decoration.PunctMidLone,
            profile.PunctHalf, profile.LatinSpacingEm, profile.SmallKanaPalt);

        using var baseFont = new SKFont(typeface, TelopFontSize);
        using var smallFont = new SKFont(typeface, TelopFontSize * TelopSmallSizeRatio);
        using var rubyFont = new SKFont(rubyTypeface, TelopFontSize * rubyRatio);

        // 空白の空きを測った値にそろえる。全角の空白（を含むまとまり）はこの幅にし、半角の空白だけのまとまりは
        // 書体の半角の空白の字送りに、前後の字との字間 2 つ分を足した幅にする（放送の文字組みで半角の空白を打ったときと同じ空き）。
        if (profile.SpaceEm is float space)
        {
            float halfSpace = Math.Max(0f, baseFont.MeasureText(" ") / TelopFontSize + 2f * profile.LetterSpacingEm);
            foreach (var line in lines)
                for (int i = 0; i < line.Count; i++)
                    if (line[i].IsGap) line[i] = line[i] with { GapEm = line[i].GapEm >= TelopFullWidthGapEm ? space : halfSpace };
        }

        // 振り仮名の段（振り仮名の上端から親字の上端までの高さ）と、字の高さ（いずれも字の大きさに対する比）。
        // 画像の上端に振り仮名の段を取るのは 1 行目に振り仮名があるときだけ。行と行のあいだは、下の行の振り仮名の有無で
        // 作品ごとの空きを使い分ける（本編は、下の行に振り仮名があると行を広げる作品と、行の位置が変わらない作品がある）。
        float rubyBandRatio = Math.Max(0f, rubyRaise + TelopAscentRatio * rubyRatio - TelopAscentRatio);
        float glyphBlockRatio = TelopAscentRatio + TelopDescentRatio;
        bool firstLineHasRuby = lines[0].Any(u => u.Ruby.Length > 0);
        // 字の外の幅：外周の余白と、行の端の振り仮名を外へはみ出させる組み方ではそのはみ出し分（行は中央にそろえるので左右とも取る）。
        // 熟語にまとめる組み方では、まとめた読みが行の端からはみ出しうるので、その分も取る。
        float edgeOverhang = hasRuby && (rubyLayout.OverhangLineStart || rubyLayout.OverhangLineEnd || rubyLayout.Grouping != SubtitleRubyGrouping.Mono)
            ? rubyLayout.OverhangRatio : 0f;
        // 親字の斜体：ベースラインを軸に倒すので、字の上端は傾き × 字の高さだけ右へ出る。その分を左右の余白に足す。
        float baseSkew = (float)Math.Tan(profile.BaseObliqueDegrees * Math.PI / 180.0);
        float skewOverhang = Math.Abs(baseSkew) * TelopAscentRatio / 2f;
        var style = TelopStyle.From(profile);
        // 画素で重ねる描き方では、光彩・面取りなども含めた装飾の広がりから余白を決める
        bool raster = profile.RasterDecoration;
        float extraPadding = raster
            ? Math.Max(0f, Math.Max(TelopRaster.Extent(profile.Decoration), profile.Decoration.Ruby is { } rd ? TelopRaster.Extent(rd) : 0f) - TelopPaddingRatio + 0.015f)
            : style.ExtraPadding;
        float OuterWidth(float s) => 2f * s * (TelopPaddingRatio + extraPadding + edgeOverhang + skewOverhang);

        // 字の大きさ（仮想ボディの高さ）はどの回も同じ。長い行は字を小さくせず、下で行ごとに長体をかけて収める（本編と同じ処理）。
        float size = fixedSize ?? TelopFontSize;
        SetTelopSize(baseFont, smallFont, size);
        float longest = lines.Max(line => TelopLineWidth(line, baseFont, smallFont, size, 1f, spacing));
        float room = fixedSize is null ? TelopMaxWidth - OuterWidth(size) : profile.LineMaxWidth ?? float.MaxValue;
        // 各話のテロップ画像：本編のいちばん長い行の幅（字の大きさに対する比）を超える行は、本編と同じく字を小さくせずその幅まで長体で詰める
        if (fixedSize is null && profile.LineMaxEm is float lineEm) room = Math.Min(room, lineEm * size);

        // 行ごとの長体の率と、その率での行の幅。収まる行は等幅のまま。
        var scales = new float[lines.Count];
        var widths = new float[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            float natural = TelopLineWidth(lines[i], baseFont, smallFont, size, 1f, spacing);
            scales[i] = natural > room && natural > 0f ? room / natural : 1f;
            // 回ごとに測った行の横の倍率があれば、さらに掛ける
            if (profile.LineScales is { } lineScales && i < lineScales.Count && lineScales[i] > 0f) scales[i] *= lineScales[i];
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
        // 見積もった大きさ（組みの座標）の四方に余白を足した面に描き、あとで描いた字の端に合わせて切り抜く
        int bleed = RoundUpToMultiple(size * TelopBleedRatio, TelopPixelRatio);
        int canvasWidth = width + 2 * bleed, canvasHeight = height + 2 * bleed;
        float LineX(int i) => bleed + (width - widths[i]) / 2f + LineOffset(i).Dx * size;
        float LineBaseline(int i) => bleed + lineTops[i] + size * TelopAscentRatio + LineOffset(i).Dy * size;

        using var surface = SKSurface.Create(new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
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
            // 画素で重ねる描き方：親字と振り仮名の字の形を別々の面に塗り、装飾の層を重ねた画像を作って貼る。
            // 面は出力の SuperSample 倍の大きさで、重ね終えたら平均して出力の大きさに縮める
            int ss = TelopRaster.SuperSample;
            int sw = canvasWidth * ss, sh = canvasHeight * ss;
            var maskInfo = new SKImageInfo(sw, sh, SKColorType.Alpha8, SKAlphaType.Premul);
            using var baseMask = new SKBitmap(maskInfo);
            using var rubyMask = new SKBitmap(maskInfo);
            using (var baseCanvas = new SKCanvas(baseMask))
            using (var rubyCanvas = new SKCanvas(rubyMask))
            {
                baseCanvas.Clear(SKColors.Transparent);
                rubyCanvas.Clear(SKColors.Transparent);
                baseCanvas.Scale(ss);
                rubyCanvas.Scale(ss);
                var centers = new List<float>();
                for (int i = 0; i < lines.Count; i++)
                {
                    float baseline = LineBaseline(i);
                    float x = LineX(i);
                    BeginLine(baseCanvas, baseline);
                    BeginLine(rubyCanvas, baseline);
                    DrawTelopLine(baseCanvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], LineRubyLayout(i), spacing, style,
                        new TelopMaskTarget(rubyCanvas, centers));
                    baseCanvas.Restore();
                    rubyCanvas.Restore();
                }
                baseCanvas.Flush();
                rubyCanvas.Flush();
                // 太らせ・細らせの距離を測るための細かい字の形（出力の FineScale 倍＝重ねる面の FineScale / SuperSample 倍）。
                // 振り仮名だけの装飾があれば、親字と振り仮名を別の面に塗る
                int k = TelopRaster.FineScale;
                int fk = Math.Max(1, k / ss);
                var decoration = TelopRaster.ScalePixelCoordinates(TelopRaster.OffsetPixelCoordinates(profile.Decoration, bleed, bleed), ss);
                var rubyDecoration = decoration.Ruby;
                var planeCenters = centers.Select(c => c * ss).ToList();
                float planeSize = size * ss;
                var fineInfo = new SKImageInfo(canvasWidth * k, canvasHeight * k, SKColorType.Alpha8, SKAlphaType.Premul);
                using var fineMask = new SKBitmap(fineInfo);
                using var fineRubyMask = rubyDecoration is null ? null : new SKBitmap(fineInfo);
                using (var fineCanvas = new SKCanvas(fineMask))
                using (var fineRubyCanvas = fineRubyMask is null ? null : new SKCanvas(fineRubyMask))
                {
                    fineCanvas.Clear(SKColors.Transparent);
                    fineCanvas.Scale(k);
                    fineRubyCanvas?.Clear(SKColors.Transparent);
                    fineRubyCanvas?.Scale(k);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        float baseline = LineBaseline(i);
                        float x = LineX(i);
                        BeginLine(fineCanvas, baseline);
                        if (fineRubyCanvas is not null) BeginLine(fineRubyCanvas, baseline);
                        DrawTelopLine(fineCanvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], LineRubyLayout(i), spacing, style,
                            new TelopMaskTarget(fineRubyCanvas ?? fineCanvas, new List<float>()));
                        fineCanvas.Restore();
                        fineRubyCanvas?.Restore();
                    }
                    fineCanvas.Flush();
                    fineRubyCanvas?.Flush();
                }
                if (rubyDecoration is null)
                {
                    using var composedLarge = TelopRaster.Compose(sw, sh, ReadMask(baseMask), ReadMask(rubyMask), planeCenters, decoration, planeSize, ReadMask(fineMask), fk);
                    using var composed = TelopRaster.Downsample(composedLarge, ss);
                    using var composedImage = SKImage.FromBitmap(composed);
                    canvas.DrawImage(composedImage, 0f, 0f, new SKSamplingOptions(SKFilterMode.Nearest), null);
                }
                else
                {
                    // 親字と振り仮名をそれぞれの装飾で重ねる。どちらのフチ・影も相手の字の本体にかぶらないよう、
                    // 装飾ごとの絵を貼ったあと、親字と振り仮名の本体（塗りと塗りの上の面取りだけ）をもう一度上に貼る
                    var empty = new float[sw * sh];
                    var baseAlpha = ReadMask(baseMask);
                    var rubyAlpha = ReadMask(rubyMask);
                    var baseFine = ReadMask(fineMask);
                    var rubyFine = ReadMask(fineRubyMask!);
                    static TelopDecoration BodyOnly(TelopDecoration d) => d with
                    {
                        Layers = d.Layers.Where(l => l.Kind == TelopDecorationLayer.KindBevel && l.OverFill).ToList(),
                        Glow = null
                    };
                    var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
                    foreach (var (mBase, mRuby, deco, fine) in new[]
                             {
                                 (baseAlpha, empty, decoration, baseFine), (empty, rubyAlpha, rubyDecoration, rubyFine),
                                 (baseAlpha, empty, BodyOnly(decoration), baseFine), (empty, rubyAlpha, BodyOnly(rubyDecoration), rubyFine)
                             })
                    {
                        using var large = TelopRaster.Compose(sw, sh, mBase, mRuby, planeCenters, deco, planeSize, fine, fk);
                        using var bmp = TelopRaster.Downsample(large, ss);
                        using var image = SKImage.FromBitmap(bmp);
                        canvas.DrawImage(image, 0f, 0f, sampling, null);
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < lines.Count; i++)
            {
                float baseline = LineBaseline(i);
                float x = LineX(i);
                BeginLine(canvas, baseline);
                DrawTelopLine(canvas, lines[i], x, baseline, baseFont, smallFont, rubyFont, size, scales[i], LineRubyLayout(i), spacing, style);
                canvas.Restore();
            }
        }

        // 描いた字（装飾込み）の端の外に空きを残して切り抜く。左右は同じだけ切って行の中央を画像の中央に保ち、
        // 上下はそれぞれの側で切る。はみ出しが無ければ見積もった大きさに戻る
        using var full = surface.Snapshot();
        var (inkLeft, inkTop, inkRight, inkBottom) = InkBounds(full);
        int Trim(int slack) => Math.Clamp(slack - TelopBleedGap, 0, bleed) / TelopPixelRatio * TelopPixelRatio;
        int cutX = inkRight < inkLeft ? bleed : Trim(Math.Min(inkLeft, canvasWidth - 1 - inkRight));
        int cutTop = inkBottom < inkTop ? bleed : Trim(inkTop);
        int cutBottom = inkBottom < inkTop ? bleed : Trim(canvasHeight - 1 - inkBottom);
        var cropped = full.Subset(new SKRectI(cutX, cutTop, canvasWidth - cutX, canvasHeight - cutBottom));
        float originX = bleed - cutX, originY = bleed - cutTop;
        return new TelopImage(cropped, originY + lineTops[0] + size * TelopAscentRatio, originX + (width - widths[0]) / 2f, widths[0],
            originX, originY);
    }

    /// <summary>画像の不透明な画素の範囲（左・上・右・下、両端を含む）。不透明な画素が無ければ右 &lt; 左になる。</summary>
    private static (int Left, int Top, int Right, int Bottom) InkBounds(SKImage image)
    {
        using var bitmap = SKBitmap.FromImage(image);
        int w = bitmap.Width, h = bitmap.Height;
        var pixels = bitmap.GetPixelSpan();
        int rowBytes = bitmap.RowBytes, bpp = bitmap.BytesPerPixel;
        int alphaOffset = bitmap.ColorType == SKColorType.Bgra8888 || bitmap.ColorType == SKColorType.Rgba8888 ? 3 : 0;
        int left = w, top = h, right = -1, bottom = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                if (pixels[row + x * bpp + alphaOffset] == 0) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                bottom = y;
            }
        }
        return (left, top, right, bottom);
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
                // 欧文・約物のあとの空白は、前の字のあとと空白のあとの 2 つ分の欧文の字間を足す
                float latin = previous is { IsGap: false } p ? 2f * TelopLatinAfter(p, spacing) : 0f;
                width += (unit.GapEm + latin) * size * scaleX;
            }
            else
            {
                var font = unit.Small ? smallFont : baseFont;
                string? previousChar = previous is { IsGap: false } ? previous.Base : null;
                if (previousChar is not null) width += font.Size * (spacing.BaseEm + TelopLatinAfter(previous!, spacing)) * scaleX;
                width += TelopMeasure(font, unit.Base, spacing, spacing.BaseEm, TelopContinues(line, i), previousChar);
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
        // 空白をはさまず前に字の単位がある単位と、その前の単位の字
        var afterChars = new Dictionary<TelopUnit, string>(ReferenceEqualityComparer.Instance);
        float cursor = x;
        bool afterGap = false;
        for (int i = 0; i < line.Count; i++)
        {
            var unit = line[i];
            if (unit.IsGap)
            {
                // 欧文・約物のあとの空白は、前の字のあとと空白のあとの 2 つ分の欧文の字間を足す
                float latin = placed.Count > 0 && !afterGap ? 2f * TelopLatinAfter(placed[^1].Unit, spacing) : 0f;
                cursor += (unit.GapEm + latin) * size * scaleX;
                afterGap = true;
                continue;
            }
            var font = unit.Small ? smallFont : baseFont;
            if (placed.Count > 0 && !afterGap) cursor += font.Size * (spacing.BaseEm + TelopLatinAfter(placed[^1].Unit, spacing)) * scaleX;
            bool keep = TelopContinues(line, i);
            if (keep) continues.Add(unit);
            string? previousChar = placed.Count > 0 && !afterGap ? placed[^1].Unit.Base : null;
            if (previousChar is not null) afterChars[unit] = previousChar;
            float slot = TelopMeasure(font, unit.Base, spacing, spacing.BaseEm, keep, previousChar);
            placed.Add((unit, font, cursor, slot, afterGap));
            cursor += slot;
            afterGap = false;
        }
        void DrawBase(TelopUnit unit, SKFont font, float ux, float y, SKPaint paint, SKPaint? outline)
        {
            bool keep = continues.Contains(unit);
            var shaper = TelopShaper(font, spacing, keep);
            float em = spacing.BaseEm;
            var pieces = spacing.TiltChars is null && spacing.PunctRunEm is null ? null : TelopPieces(unit.Base, spacing, keep);
            if (pieces is null || (pieces.Count == 1 && pieces[0].Kind == 0))
            {
                shaper.Draw(canvas, unit.Base, ux, y, SKTextAlign.Left, font, paint, outline, font.Size * em);
                return;
            }
            // 区切りごとに置く（置き場所は TelopPieceLayout と同じ）。傾ける字はベースラインの中央を軸に回して置く
            var tilt = style.Tilt;
            var pieceShaper = TelopPieceShaper(font, spacing, keep);
            var laid = TelopPieceLayout(font, unit.Base, spacing, em, keep, afterChars.GetValueOrDefault(unit)).Pieces;
            for (int pi = 0; pi < laid.Count; pi++)
            {
                var (text, kind, px) = laid[pi];
                // 記号のまとまりの中の字か（前後に記号のまとまりの字が続くか）。単独の字は単独のときの角度で傾ける。
                // 記号を別に置かない作品（傾ける字だけを切り出す）では、すぐ隣の字が「！」「？」ならまとまりとみなす
                bool AdjacentPunct(Func<char, bool> match) =>
                    (pi > 0 && laid[pi - 1].Text.Length > 0 && match(laid[pi - 1].Text[^1]))
                    || (pi + 1 < laid.Count && laid[pi + 1].Text.Length > 0 && match(laid[pi + 1].Text[0]));
                bool tiltRun = kind == 2 && text.Length == 1 && TelopPunct.Contains(text[0]) && AdjacentPunct(c => TelopPunct.Contains(c));
                bool inRun = kind == 1 && ((pi > 0 && laid[pi - 1].Kind == 1) || (pi + 1 < laid.Count && laid[pi + 1].Kind == 1)) || tiltRun;
                bool questionRun = kind == 1 ? inRun && TelopRunHasQuestion(laid, pi, q => (q.Text, q.Kind))
                    : tiltRun && (text[0] is '？' or '?' || AdjacentPunct(c => c is '？' or '?'));
                if (kind == 0)
                {
                    pieceShaper.Draw(canvas, text, ux + px, y, SKTextAlign.Left, font, paint, outline, font.Size * em);
                    continue;
                }
                var sh = kind == 1 ? shaper : pieceShaper;
                float natural = sh.Measure(text, font, font.Size * em);
                if (tilt is not null && tilt.Chars.Contains(text[0]))
                {
                    float cx = ux + px + tilt.Dx * font.Size;
                    float cy = y + tilt.Dy * font.Size;
                    canvas.Save();
                    // まとまりの中の「？」を傾ける字に含む作品では、「？」自体は単独のときの角度のまま（「！」だけをまとまりの角度にする）
                    bool keepQuestion = inRun && text[0] is '？' or '?';
                    canvas.RotateDegrees(keepQuestion ? TelopTiltAngle(tilt, lone: true) : TelopTiltAngle(tilt, !inRun, questionRun), cx, cy);
                    sh.Draw(canvas, text, cx - natural / 2f, cy, SKTextAlign.Left, font, paint, outline, font.Size * em);
                    canvas.Restore();
                }
                else
                {
                    sh.Draw(canvas, text, ux + px - natural / 2f, y, SKTextAlign.Left, font, paint, outline, font.Size * em);
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
                    string element = elements.GetTextElement();
                    prefix.Append(element);
                    float after = TelopMeasure(font, prefix.ToString(), spacing, spacing.BaseEm, keep);
                    mask.CharCenters.Add(ux + (before + after) / 2f);
                    float latin = spacing.LatinEm != 0f && OgTextShaper.IsLatinSpaced(element[^1]) ? spacing.LatinEm : 0f;
                    before = after + font.Size * (spacing.BaseEm + latin) * scaleX;
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
        // 振り仮名の横の率：親字の行の長体に合わせて細くするか、振り仮名の幅は変えないか
        float rubyAspect = rubyLayout.KeepWidth ? 1f : scaleX;
        // 振り仮名。親字の幅に収まれば親字の中央に置く。親字より長ければ両隣へはみ出させる。はみ出してよい幅は、
        // 振り仮名の無い字（と字間の空白）の上なら組み方の上限まで、振り仮名のある字の上ならその振り仮名の脇の空きまで、
        // 行頭・行末では組み方しだいで上限まで（はみ出させない組み方なら 0 で、行の端にそろえて内側へ寄せる）。
        // それでも収まらなければ、収まる幅まで振り仮名を横に圧縮する（長体）。
        // 体裁は親字と同じ（影 → フチ → 白い字）で、フチの太さと影のずれは親字の字の大きさから決める。
        // 斜体の組み方では、振り仮名ごとにそのベースラインを軸に倒し（上ほど右へ）、倒れた分の半分だけ左へ寄せて見た目の中央をそろえる。
        // 均等の組み方では、振り仮名のある字が空白をはさまず続くところを 1 つにまとめ、読みが熟語より短ければ
        // 1 字ずつ熟語の幅に均等に空けて並べる（字の間に 1 つ分、両端に半分ずつ）。
        // ひと続きの組み方でも、まず 1 字ずつ置く。熟語にまとめるのは最後の手段で、読みが親字と脇の空きに収まらない字だけを
        // 隣の振り仮名のある字とまとめ、熟語全体の上に置く（収まるまで繰り返す）。
        bool spread = rubyLayout.Grouping == SubtitleRubyGrouping.Spread;
        // ひと続きの組み方（GROUP）：まとめ方は均等と同じで、置き方は熟語全体の字面の中央
        bool group = rubyLayout.Grouping == SubtitleRubyGrouping.Group;
        bool jukugoWhenCrowded = rubyLayout.Grouping == SubtitleRubyGrouping.Jukugo;
        var spans = new List<(string Ruby, SKFont Font, float X, float Slot, bool AfterGap, float InkLeft, float InkRight)>();
        // 熟語にまとめた振り仮名か（spans と同じ並び）
        var merged = new List<bool>();
        foreach (var (unitRuby, font, ux, slot, gapBefore, inkLeft, inkRight) in placed)
        {
            if ((spread || group) && unitRuby.Length > 0 && !gapBefore && spans.Count > 0 && spans[^1].Ruby.Length > 0)
            {
                var prev = spans[^1];
                spans[^1] = (prev.Ruby + unitRuby, prev.Font, prev.X, ux + slot - prev.X, prev.AfterGap, prev.InkLeft, inkRight);
                merged[^1] = true;
                continue;
            }
            spans.Add((unitRuby, font, ux, slot, gapBefore, inkLeft, inkRight));
            merged.Add(false);
        }

        var rubyWidths = Array.Empty<float>();
        void MeasureRubies()
        {
            rubyWidths = new float[spans.Count];
            for (int k = 0; k < spans.Count; k++)
            {
                if (spans[k].Ruby.Length == 0) continue;
                rubyFont.Size = spans[k].Font.Size * rubyLayout.SizeRatio;
                rubyFont.ScaleX = rubyAspect;
                rubyWidths[k] = TelopMeasure(rubyFont, spans[k].Ruby, spacing, spacing.RubyEm);
            }
        }
        MeasureRubies();
        float overhangMax = size * rubyLayout.OverhangRatio * scaleX;
        // 隣 n の側へはみ出してよい幅。separated は字間の空白をはさむとき。n が行の外なら行頭・行末。
        // 熟語にまとめた読み（jukugo）は、行の端でも熟語全体の中央に置く（端にそろえない）。
        float Allowance(int n, bool separated, bool jukugo)
        {
            if (n < 0) return rubyLayout.OverhangLineStart || jukugo ? overhangMax : 0f;
            if (n >= spans.Count) return rubyLayout.OverhangLineEnd || jukugo ? overhangMax : 0f;
            if (separated || spans[n].Ruby.Length == 0) return overhangMax;
            // 均等に並べた振り仮名は熟語の幅いっぱいを使うので、脇に空きは残らない。
            if (spread) return 0f;
            return Math.Max(0f, (spans[n].Slot - rubyWidths[n]) / 2f);
        }
        // 読みが親字と脇の空きに収まらないか（収まらなければ長体で詰めることになる）
        bool Crowded(int k)
        {
            if (spans[k].Ruby.Length == 0) return false;
            bool after = k + 1 < spans.Count && spans[k + 1].AfterGap;
            float room = spans[k].Slot + Allowance(k - 1, spans[k].AfterGap, merged[k]) + Allowance(k + 1, after, merged[k]);
            return rubyWidths[k] > room;
        }
        if (jukugoWhenCrowded)
        {
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int k = 0; k + 1 < spans.Count; k++)
                {
                    if (spans[k].Ruby.Length == 0 || spans[k + 1].Ruby.Length == 0 || spans[k + 1].AfterGap) continue;
                    if (!Crowded(k) && !Crowded(k + 1)) continue;
                    var a = spans[k];
                    var b = spans[k + 1];
                    spans[k] = (a.Ruby + b.Ruby, a.Font, a.X, b.X + b.Slot - a.X, a.AfterGap, a.InkLeft, b.InkRight);
                    spans.RemoveAt(k + 1);
                    merged[k] = true;
                    merged.RemoveAt(k + 1);
                    MeasureRubies();
                    changed = true;
                    break;
                }
            }
        }

        var rubies = new List<TelopRubyGlyph>();
        for (int k = 0; k < spans.Count; k++)
        {
            var (ruby, font, ux, slot, gapBefore, inkLeft, inkRight) = spans[k];
            if (ruby.Length == 0) continue;
            bool gapAfter = k + 1 < spans.Count && spans[k + 1].AfterGap;
            float left = ux - Allowance(k - 1, gapBefore, merged[k]);
            float right = ux + slot + Allowance(k + 1, gapAfter, merged[k]);
            float rubyWidth = rubyWidths[k];
            float rubyScale = rubyAspect;
            float rx;
            float rubySize = font.Size * rubyLayout.SizeRatio;
            if (spread && rubyWidth < slot)
            {
                rubyFont.Size = rubySize;
                rubyFont.ScaleX = rubyAspect;
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
                    rubies.Add(new TelopRubyGlyph(ch, cx, rubyAspect, rubySize, font.Size));
                    cx += w + space;
                }
                continue;
            }
            if (rubyWidth > right - left && rubyWidth > 0f)
            {
                rubyScale = rubyAspect * (right - left) / rubyWidth;
                rx = left;
            }
            else
            {
                // 振り仮名の字面の中心を親字の字面の中心に合わせ、はみ出してよい範囲に収める。
                rubyFont.Size = rubySize;
                rubyFont.ScaleX = rubyAspect;
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
