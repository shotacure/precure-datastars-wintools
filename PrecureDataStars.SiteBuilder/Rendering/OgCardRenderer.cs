using PrecureDataStars.SiteBuilder.Utilities;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// OGP カード画像（1200×630 PNG）をビルド時にラスタライズするレンダラ。
/// SNS へリンクを貼ったときに表示される大カード（<c>twitter:card = summary_large_image</c>）の実体で、
/// ページ種別によらず同一の意匠に統一する（サイトの hero と同じ淡ピンク→クリームのグラデーション、
/// 見出しはブランド書体 Kiwi Maru）。カードだけ見てもどのサイトのものか判別できる状態を優先する設計。
///
/// <para>
/// 組み方は入力に応じて 2 通り。<see cref="OgCardSpec.IsDense"/> が false なら見出しを大きく置くだけの
/// 標準レイアウト、true なら識別子・バッジ・帯グラフ・事実行を積み上げる高密度レイアウトになる。
/// 高密度側はエピソードのように「このサイトにしか無い情報」をカード 1 枚で見せるためのもので、
/// 尺構成の帯グラフはサイト本体のフォーマット表と同じ配色・同じ比率で描く。
/// </para>
/// <para>
/// 情報の優先順位は上から「所属（シリーズ）→ 識別子（第N話）→ 数（通算バッジ）→ 主題（サブタイトル）
/// → 構造（帯グラフ）→ 担い手（スタッフ）」の順に置く。要素ごとに書体・大きさ・色を変え、
/// ラベルはアクセント色・値は本文色に分けることで、羅列ではなく表として読める状態を作る。
/// </para>
/// <para>
/// 書体はブラウザ側と同じ Kiwi Maru / Noto Sans JP を使うが、ラスタライズには実ファイルが要るため
/// <c>Fonts/</c> に同梱した TTF を読む（Google Fonts の CDN はビルド時には使えない）。
/// </para>
/// <para>
/// スレッド安全性：<see cref="SKTypeface"/> は読み取り専用に共有してよいのでインスタンス生成時に
/// 1 度だけ読み込み、描画のたびに使い回す。<see cref="SKFont"/> / <see cref="SKPaint"/> /
/// <see cref="SKSurface"/> はスレッド安全ではないため <see cref="Render"/> の呼び出しごとに作って捨てる。
/// これにより PageRenderer の並列レンダリングフェーズからそのまま呼べる。
/// </para>
/// </summary>
public sealed class OgCardRenderer : IDisposable
{
    // ──────── カードの寸法 ────────

    /// <summary>OGP 推奨サイズ。X / Facebook / LINE が大カードとして扱う 1.91:1 の実寸。</summary>
    private const int CardWidth = 1200;
    private const int CardHeight = 630;

    /// <summary>左右の内側余白。SNS のタイムラインで端が切れても文字が欠けない程度に広く取る。</summary>
    private const float PaddingX = 76f;

    /// <summary>上端のアクセントバーの高さ。</summary>
    private const float AccentBarHeight = 10f;

    /// <summary>フッタ罫線の Y 座標。中身の量によらずこの位置は動かさない。</summary>
    private const float FooterLineY = 552f;

    /// <summary>フッタのサイト名のベースライン。</summary>
    private const float FooterTextBaseline = 600f;

    // ──────── 配色（サイトの CSS 変数と対応させる） ────────

    /// <summary>アクセント（--accent-pink）。上端バーとラベル類に使う。</summary>
    private static readonly SKColor AccentPink = SKColor.Parse("#e91e63");
    /// <summary>本文色（--fg）。</summary>
    private static readonly SKColor Foreground = SKColor.Parse("#1a1a1a");
    /// <summary>補助色（--muted）。</summary>
    private static readonly SKColor Muted = SKColor.Parse("#666666");
    /// <summary>フッタ上の罫線。背景のピンク寄りに馴染む薄色。</summary>
    private static readonly SKColor Hairline = SKColor.Parse("#e7c8d4");
    /// <summary>帯グラフ区画の仕切り。隣り合う淡色を分離する。</summary>
    private static readonly SKColor BarDivider = SKColor.Parse("#ffffff");
    /// <summary>ハッチ（CM 枠）の斜線色。</summary>
    private static readonly SKColor HatchLine = SKColor.Parse("#c9c9d2");
    /// <summary>帯グラフ区画内のラベル色。</summary>
    private static readonly SKColor BarLabel = SKColor.Parse("#33333a");
    /// <summary>色指定が解決できなかった区画のフォールバック色（サイトの fmt-p-misc 相当）。</summary>
    private static readonly SKColor BarFallback = SKColor.Parse("#d7d7de");
    /// <summary>角丸バッジの塗り。背景のグラデーションを薄く透かして浮かせる。</summary>
    private static readonly SKColor BadgeFill = new(255, 255, 255, 190);
    /// <summary>角丸バッジの縁。サイトの外部リンクバッジと同じ「薄ボーダー」の流儀に揃える。</summary>
    private static readonly SKColor BadgeBorder = SKColor.Parse("#f0aec8");

    /// <summary>背景グラデーション。サイトの <c>.hero.hero-gradient</c>（180deg 淡ピンク→クリーム）と同値。</summary>
    private static readonly SKColor[] BackgroundColors =
    {
        SKColor.Parse("#fff1f6"),
        SKColor.Parse("#fde7ef"),
        SKColor.Parse("#fff8f0")
    };
    private static readonly float[] BackgroundStops = { 0f, 0.45f, 1f };

    // ──────── 組版パラメータ ────────

    /// <summary>標準レイアウトの見出しサイズ候補。上から順に試し、規定行数に収まった時点で採用する。</summary>
    private static readonly float[] TitleSizeCandidates = { 68f, 60f, 52f, 46f };

    /// <summary>高密度レイアウトの見出しサイズ候補（上下の要素に場所を譲るぶん小さめ）。</summary>
    private static readonly float[] DenseTitleSizeCandidates = { 54f, 48f, 42f, 38f };

    /// <summary>見出しの最大行数（標準 / 高密度）。これを超える分は末尾を省略記号で切り詰める。</summary>
    private const int TitleMaxLines = 3;
    private const int DenseTitleMaxLines = 2;

    /// <summary>行送り倍率（日本語の詰まりを避けるための基準）。</summary>
    private const float TitleLineHeightRatio = 1.35f;
    private const float DenseTitleLineHeightRatio = 1.22f;

    /// <summary>本文ブロックがフッタ罫線に食い込まないよう確保する最小の間隔。</summary>
    private const float FooterClearance = 26f;

    /// <summary>帯グラフの高さ、区画の最小幅、区画内ラベルを出す最小幅。</summary>
    private const float BarHeight = 46f;
    private const float BarMinSegmentWidth = 5f;
    private const float BarLabelMinWidth = 62f;

    /// <summary>
    /// 見出しが 2 行に伸びて余白が痩せたときに帯グラフを縮められる下限。
    /// これを割り込む場合は尺の凡例を落として帯そのものを優先する。
    /// </summary>
    private const float BarMinHeight = 34f;

    /// <summary>帯グラフの下に置く尺凡例が占める高さ。</summary>
    private const float BarCaptionHeight = 30f;

    /// <summary>識別子（第N話など）の文字サイズ。見出しに次ぐ大きさで、カードの入り口になる。</summary>
    private const float HeadlineFontSize = 44f;

    /// <summary>角丸バッジの寸法と字送り。高さの半分を角丸半径にしてピル形にする。</summary>
    private const float BadgeHeight = 42f;
    private const float BadgePaddingX = 20f;
    private const float BadgeGap = 12f;
    private const float BadgeInnerGap = 10f;
    private const float BadgeLabelFontSize = 21f;
    private const float BadgeValueFontSize = 26f;

    /// <summary>ファクト行の文字サイズ・行送り・最大行数。超過分は末尾から捨てる。</summary>
    private const float FactFontSize = 27f;
    private const float FactLineHeight = 38f;
    private const int FactMaxLines = 3;

    /// <summary>ラベル＋値を 1 行に流すファクトの項目間隔と最大行数。</summary>
    private const float InlineFactGap = 30f;
    private const int InlineFactMaxLines = 2;

    /// <summary>
    /// 行頭に置いてはいけない文字（行頭禁則）。折り返し位置がこれらに当たった場合、
    /// 1 文字ぶん前の行へ送り込んで自然な組版にする。
    /// </summary>
    private const string LineStartForbidden = "。、．，）」』】〕〉》”’!?！？：；・ーぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ";

    private readonly SKTypeface _brandTypeface;
    private readonly SKTypeface _bodyTypeface;

    /// <summary>ブランド書体で表現できない文字（同じ組み合わせを何度も報告しないための記録）。</summary>
    private readonly HashSet<string> _missingGlyphReported = new();
    private readonly object _missingGlyphLock = new();

    /// <summary>カード下部に固定で出すサイト名。</summary>
    private readonly string _brandLabel;

    /// <summary>
    /// 同梱フォントを読み込んでレンダラを構築する。
    /// </summary>
    /// <param name="brandLabel">カード下部に出すサイト名（可視ブランド表記）。</param>
    /// <exception cref="FileNotFoundException">同梱フォントが見つからない場合。</exception>
    public OgCardRenderer(string brandLabel)
    {
        _brandLabel = brandLabel;

        var fontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
        _brandTypeface = LoadTypeface(Path.Combine(fontDir, "KiwiMaru-Medium.ttf"));
        _bodyTypeface = LoadTypeface(Path.Combine(fontDir, "NotoSansJP.ttf"));
    }

    private static SKTypeface LoadTypeface(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"OGP カード描画用のフォントが見つかりません: {path}", path);
        return SKTypeface.FromFile(path)
            ?? throw new InvalidOperationException($"フォントの読み込みに失敗しました: {path}");
    }

    /// <summary>
    /// カードを 1 枚描画して PNG ファイルへ書き出す。出力先の親ディレクトリは自動生成する。
    /// 同一入力からは常に同一バイト列が出るため、デプロイ時の MD5 差分比較で
    /// 内容が変わっていないカードは再アップロードされない。
    /// </summary>
    /// <param name="spec">カードに載せる内容。</param>
    /// <param name="outputFilePath">書き出し先の絶対パス（拡張子 .png）。</param>
    /// <returns>ブランド書体に無い文字が含まれていた場合はその文字列、無ければ null。</returns>
    public string? Render(OgCardSpec spec, string outputFilePath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CardWidth, CardHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        DrawBackground(canvas);

        using var paint = new SKPaint { IsAntialias = true };
        float contentWidth = CardWidth - PaddingX * 2;

        // 上端のアクセントバー。カード全体をブランドに紐づける最小限の装飾。
        paint.Color = AccentPink;
        canvas.DrawRect(SKRect.Create(0, 0, CardWidth, AccentBarHeight), paint);

        if (spec.IsDense)
            DrawDenseBody(canvas, paint, spec, contentWidth);
        else
            DrawStandardBody(canvas, paint, spec, contentWidth);

        DrawFooter(canvas, paint, spec, contentWidth);

        PathUtil.EnsureParentDirectory(outputFilePath);
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(outputFilePath))
        {
            data.SaveTo(stream);
        }

        return FindMissingBrandGlyphs(spec.Title);
    }

    // ════════════════════════════════ 標準レイアウト ════════════════════════════════

    /// <summary>見出しを大きく見せる標準の組み方。人物・企業・楽曲など、載せる事実が少ないページ向け。</summary>
    private void DrawStandardBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth)
    {
        float y = 150f;

        if (!string.IsNullOrWhiteSpace(spec.Kicker))
        {
            using var kickerFont = new SKFont(_bodyTypeface, 30f);
            paint.Color = AccentPink;
            canvas.DrawText(Ellipsize(spec.Kicker, kickerFont, paint, contentWidth), PaddingX, y, SKTextAlign.Left, kickerFont, paint);
            y += 58f;
        }

        using var titleFont = new SKFont(_brandTypeface, TitleSizeCandidates[^1]);
        var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, TitleSizeCandidates, TitleMaxLines);

        paint.Color = Foreground;
        y += titleFont.Size;
        foreach (var line in titleLines)
        {
            canvas.DrawText(line, PaddingX, y, SKTextAlign.Left, titleFont, paint);
            y += titleFont.Size * TitleLineHeightRatio;
        }
        y += 6f;

        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            using var subtitleFont = new SKFont(_bodyTypeface, 34f);
            paint.Color = Muted;
            float baseline = Math.Min(y + 34f, FooterLineY - FooterClearance - 40f);
            canvas.DrawText(Ellipsize(spec.Subtitle, subtitleFont, paint, contentWidth), PaddingX, baseline, SKTextAlign.Left, subtitleFont, paint);
            y = baseline + 28f;
        }

        if (!string.IsNullOrWhiteSpace(spec.MetaLeft))
        {
            using var metaFont = new SKFont(_bodyTypeface, 30f);
            paint.Color = Muted;
            float baseline = Math.Min(y + 30f, FooterLineY - FooterClearance);
            canvas.DrawText(Ellipsize(spec.MetaLeft, metaFont, paint, contentWidth), PaddingX, baseline, SKTextAlign.Left, metaFont, paint);
        }
    }

    // ════════════════════════════════ 高密度レイアウト ════════════════════════════════

    /// <summary>
    /// 識別子・バッジ・帯グラフ・事実行を積む高密度の組み方。
    /// 上端からは「所属 → 識別子 → バッジ → 見出し」を順に降ろし、事実行はフッタ罫線の直上から
    /// 上へ積む。帯グラフは残った余白へ入れるため、見出しが 1 行でも 2 行でも全体の重心が崩れない。
    /// </summary>
    private void DrawDenseBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth)
    {
        // ── 最上段（左：所属シリーズ・種別 ／ 右：放送日時などの補助） ──
        const float kickerBaseline = 78f;
        if (!string.IsNullOrWhiteSpace(spec.Kicker) || !string.IsNullOrWhiteSpace(spec.KickerRight))
        {
            using var kickerFont = new SKFont(_bodyTypeface, 28f);
            float rightWidth = 0f;
            if (!string.IsNullOrWhiteSpace(spec.KickerRight))
            {
                rightWidth = kickerFont.MeasureText(spec.KickerRight, paint);
                paint.Color = Muted;
                canvas.DrawText(spec.KickerRight, CardWidth - PaddingX, kickerBaseline, SKTextAlign.Right, kickerFont, paint);
            }
            if (!string.IsNullOrWhiteSpace(spec.Kicker))
            {
                paint.Color = AccentPink;
                float room = contentWidth - (rightWidth > 0f ? rightWidth + 24f : 0f);
                canvas.DrawText(Ellipsize(spec.Kicker, kickerFont, paint, room), PaddingX, kickerBaseline, SKTextAlign.Left, kickerFont, paint);
            }
        }

        float y = kickerBaseline;

        // ── 識別子（第N話） ──
        if (!string.IsNullOrWhiteSpace(spec.Headline))
        {
            using var headlineFont = new SKFont(_brandTypeface, HeadlineFontSize);
            y += 58f;
            paint.Color = Foreground;
            canvas.DrawText(Ellipsize(spec.Headline, headlineFont, paint, contentWidth), PaddingX, y, SKTextAlign.Left, headlineFont, paint);
        }

        // ── バッジ（通算話数・通算放送回数） ──
        if (spec.Badges.Count > 0)
            y = DrawBadges(canvas, paint, spec.Badges, PaddingX, y + 16f, contentWidth);

        // ── 見出し（サブタイトル） ──
        using var titleFont = new SKFont(_brandTypeface, DenseTitleSizeCandidates[^1]);
        var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, DenseTitleSizeCandidates, DenseTitleMaxLines);

        y += 26f + titleFont.Size;
        paint.Color = Foreground;
        foreach (var line in titleLines)
        {
            canvas.DrawText(line, PaddingX, y, SKTextAlign.Left, titleFont, paint);
            y += titleFont.Size * DenseTitleLineHeightRatio;
        }
        float titleBottom = y - titleFont.Size * (DenseTitleLineHeightRatio - 1f);

        // ── 事実行 ──
        // 帯グラフがある場合は、帯を置く余白を空けるためフッタ罫線の直上へ下寄せする。
        // 帯が無い場合に下寄せすると見出しとのあいだが大きく空いてしまうため、見出し直下へ上寄せして
        // 内容を上半分にまとめる（空きは footer 側に寄る）。
        bool hasBar = spec.Bar.Count > 0;
        float factsAnchor = hasBar ? FooterLineY - FooterClearance : titleBottom + 52f;
        float factsTop = factsAnchor;
        if (spec.InlineFacts.Count > 0)
            factsTop = DrawInlineFacts(canvas, paint, spec.InlineFacts, PaddingX, factsAnchor, contentWidth, anchorToTop: !hasBar);
        else if (spec.Facts.Count > 0)
            factsTop = DrawStackedFacts(canvas, paint, spec.Facts, PaddingX, factsAnchor, anchorToTop: !hasBar);

        // ── 帯グラフ（見出しと事実行のあいだの余白へ） ──
        if (spec.Bar.Count > 0)
        {
            float gapTop = titleBottom + 16f;
            float gapBottom = factsTop - 16f;
            float available = gapBottom - gapTop;

            // 見出しが 2 行に伸びると余白が痩せるため、帯の高さと凡例の有無を余白に合わせて畳む。
            // 事実行と重ならないことを最優先し、足りなければ凡例 → 帯の高さの順に譲る。
            bool hasCaption = !string.IsNullOrWhiteSpace(spec.BarCaption) || !string.IsNullOrWhiteSpace(spec.BarTotalLabel);
            if (hasCaption && available < BarMinHeight + BarCaptionHeight) hasCaption = false;
            float captionHeight = hasCaption ? BarCaptionHeight : 0f;
            float barHeight = Math.Clamp(available - captionHeight, BarMinHeight, BarHeight);
            float blockHeight = barHeight + captionHeight;

            // 余白は均等割りではなく上 1 : 下 2 に配る（見出しと帯を近づけ、下方向にゆとりを残す）。
            float barTop = gapTop + Math.Max(0f, (available - blockHeight) / 3f);

            DrawFormatBar(canvas, paint, spec, PaddingX, barTop, contentWidth, barHeight, hasCaption);
        }
    }

    /// <summary>
    /// 角丸バッジ列を左から並べて描き、その下端 Y を返す。
    /// ラベル（意味）をアクセント色で小さく、値（数）を本文色で一回り大きく組むことで、
    /// 数のほうへ視線が行くようにする。横幅に収まらないバッジは描かずに捨てる。
    /// </summary>
    private float DrawBadges(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardBadge> badges, float x, float top, float maxWidth)
    {
        using var labelFont = new SKFont(_bodyTypeface, BadgeLabelFontSize);
        using var valueFont = new SKFont(_bodyTypeface, BadgeValueFontSize);

        float cursor = x;
        foreach (var badge in badges)
        {
            float labelWidth = string.IsNullOrWhiteSpace(badge.Label) ? 0f : labelFont.MeasureText(badge.Label, paint);
            float valueWidth = valueFont.MeasureText(badge.Value, paint);
            float width = BadgePaddingX * 2f + labelWidth + (labelWidth > 0f ? BadgeInnerGap : 0f) + valueWidth;
            if (cursor + width > x + maxWidth) break;

            using var pill = new SKRoundRect(SKRect.Create(cursor, top, width, BadgeHeight), BadgeHeight / 2f);
            paint.Color = BadgeFill;
            canvas.DrawRoundRect(pill, paint);
            paint.Color = BadgeBorder;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.5f;
            canvas.DrawRoundRect(pill, paint);
            paint.Style = SKPaintStyle.Fill;

            float textX = cursor + BadgePaddingX;
            float baseline = top + BadgeHeight / 2f + BadgeValueFontSize * 0.36f;
            if (labelWidth > 0f)
            {
                paint.Color = AccentPink;
                canvas.DrawText(badge.Label, textX, baseline, SKTextAlign.Left, labelFont, paint);
                textX += labelWidth + BadgeInnerGap;
            }
            paint.Color = Foreground;
            canvas.DrawText(badge.Value, textX, baseline, SKTextAlign.Left, valueFont, paint);

            cursor += width + BadgeGap;
        }
        return top + BadgeHeight;
    }

    /// <summary>
    /// ラベルと値の組を 1 行に流し込んで描き、ブロック上端の Y を返す。
    /// ラベルをアクセント色・値を本文色に分けることで、羅列ではなく「役職 → 担当者」の対応として読ませる。
    /// 幅に収まらない項目は次の行へ送り、規定行数を超える分は捨てる。
    /// </summary>
    /// <param name="anchor">
    /// <paramref name="anchorToTop"/> が true なら 1 行目のベースライン、false なら最終行のベースライン。
    /// </param>
    /// <param name="anchorToTop">true で上から下へ、false で下から上へ積む。</param>
    private float DrawInlineFacts(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardFactLine> facts, float x, float anchor, float maxWidth, bool anchorToTop = false)
    {
        using var labelFont = new SKFont(_bodyTypeface, FactFontSize - 5f);
        using var valueFont = new SKFont(_bodyTypeface, FactFontSize);

        float LabelWidth(OgCardFactLine f) =>
            string.IsNullOrWhiteSpace(f.Label) ? 0f : labelFont.MeasureText(f.Label, paint) + 9f;
        float ItemWidth(OgCardFactLine f) => LabelWidth(f) + valueFont.MeasureText(f.Text, paint);

        // まず行へ詰める（貪欲法）。規定行数に達した時点で以降は捨てる。
        var lines = new List<List<OgCardFactLine>> { new() };
        float used = 0f;
        foreach (var fact in facts)
        {
            float width = ItemWidth(fact);
            float need = lines[^1].Count == 0 ? width : InlineFactGap + width;
            if (lines[^1].Count > 0 && used + need > maxWidth)
            {
                if (lines.Count >= InlineFactMaxLines) break;
                lines.Add(new List<OgCardFactLine>());
                used = 0f;
                need = width;
            }
            lines[^1].Add(fact);
            used += need;
        }
        if (lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return anchor;

        float firstBaseline = anchorToTop ? anchor : anchor - (lines.Count - 1) * FactLineHeight;
        foreach (var (line, index) in lines.Select((line, index) => (line, index)))
        {
            float baseline = firstBaseline + index * FactLineHeight;
            float cursor = x;
            foreach (var fact in line)
            {
                if (!string.IsNullOrWhiteSpace(fact.Label))
                {
                    paint.Color = AccentPink;
                    canvas.DrawText(fact.Label, cursor, baseline, SKTextAlign.Left, labelFont, paint);
                    cursor += LabelWidth(fact);
                }
                paint.Color = Foreground;
                canvas.DrawText(fact.Text, cursor, baseline, SKTextAlign.Left, valueFont, paint);
                cursor += valueFont.MeasureText(fact.Text, paint) + InlineFactGap;
            }
        }
        return firstBaseline - FactFontSize;
    }

    /// <summary>1 行 1 項目で積むファクト行を描き、ブロック上端の Y を返す。</summary>
    /// <param name="anchor">
    /// <paramref name="anchorToTop"/> が true なら 1 行目のベースライン、false なら最終行のベースライン。
    /// </param>
    /// <param name="anchorToTop">true で上から下へ、false で下から上へ積む。</param>
    private float DrawStackedFacts(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardFactLine> facts, float x, float anchor, bool anchorToTop = false)
    {
        var rows = facts.Take(FactMaxLines).ToList();
        using var labelFont = new SKFont(_bodyTypeface, FactFontSize - 3f);
        using var valueFont = new SKFont(_bodyTypeface, FactFontSize);

        float firstBaseline = anchorToTop ? anchor : anchor - (rows.Count - 1) * FactLineHeight;
        for (int i = 0; i < rows.Count; i++)
        {
            float baseline = firstBaseline + i * FactLineHeight;
            float cursor = x;
            if (!string.IsNullOrWhiteSpace(rows[i].Label))
            {
                paint.Color = AccentPink;
                canvas.DrawText(rows[i].Label, cursor, baseline, SKTextAlign.Left, labelFont, paint);
                cursor += labelFont.MeasureText(rows[i].Label, paint) + 16f;
            }
            paint.Color = Muted;
            canvas.DrawText(Ellipsize(rows[i].Text, valueFont, paint, CardWidth - PaddingX - cursor), cursor, baseline, SKTextAlign.Left, valueFont, paint);
        }
        return firstBaseline - FactFontSize;
    }

    /// <summary>
    /// 尺構成の帯グラフを描く。区画幅は秒数の比で決まるが、極端に短いパート（提供クレジット 15 秒など）が
    /// 消えないよう最小幅を保証し、その分を余裕のある区画から比例配分で差し引く。
    /// </summary>
    /// <param name="barHeight">帯の高さ。余白に応じて呼び出し側が縮める。</param>
    /// <param name="withCaption">尺の凡例と総尺を帯の下に描くか。余白が足りない場合は false で呼ばれる。</param>
    private void DrawFormatBar(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float x, float top, float width, float barHeight, bool withCaption)
    {
        var widths = ComputeSegmentWidths(spec.Bar, width);

        using var segmentFont = new SKFont(_bodyTypeface, 20f);
        float cursor = x;
        for (int i = 0; i < spec.Bar.Count; i++)
        {
            var segment = spec.Bar[i];
            float segWidth = widths[i];
            var rect = SKRect.Create(cursor, top, segWidth, barHeight);

            paint.Color = SKColor.TryParse(segment.ColorHex, out var color) ? color : BarFallback;
            canvas.DrawRect(rect, paint);

            if (segment.Hatched) DrawHatch(canvas, paint, rect);

            // 区画の仕切り。淡色どうしが隣接しても切れ目が見えるように白い細線を入れる。
            if (i > 0)
            {
                paint.Color = BarDivider;
                canvas.DrawRect(SKRect.Create(cursor, top, 1.5f, barHeight), paint);
            }

            // ラベルは収まる幅の区画にだけ入れる（サイト本体の帯グラフと同じ判断）。
            if (segWidth >= BarLabelMinWidth && !string.IsNullOrWhiteSpace(segment.Label)
                && segmentFont.MeasureText(segment.Label, paint) <= segWidth - 12f)
            {
                paint.Color = BarLabel;
                canvas.DrawText(segment.Label, cursor + segWidth / 2f, top + barHeight / 2f + 7f, SKTextAlign.Center, segmentFont, paint);
            }

            cursor += segWidth;
        }

        // 外枠。
        paint.Color = Hairline;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.5f;
        canvas.DrawRect(SKRect.Create(x, top, width, barHeight), paint);
        paint.Style = SKPaintStyle.Fill;

        // 帯の下段：左に尺の凡例、右に総尺。幅の狭い区画のラベルはここで補う。
        if (withCaption && (!string.IsNullOrWhiteSpace(spec.BarCaption) || !string.IsNullOrWhiteSpace(spec.BarTotalLabel)))
        {
            using var captionFont = new SKFont(_bodyTypeface, 22f);
            float baseline = top + barHeight + 26f;
            float totalWidth = 0f;
            paint.Color = Muted;
            if (!string.IsNullOrWhiteSpace(spec.BarTotalLabel))
            {
                totalWidth = captionFont.MeasureText(spec.BarTotalLabel, paint);
                canvas.DrawText(spec.BarTotalLabel, x + width, baseline, SKTextAlign.Right, captionFont, paint);
            }
            if (!string.IsNullOrWhiteSpace(spec.BarCaption))
            {
                float room = width - (totalWidth > 0f ? totalWidth + 24f : 0f);
                canvas.DrawText(Ellipsize(spec.BarCaption, captionFont, paint, room), x, baseline, SKTextAlign.Left, captionFont, paint);
            }
        }
    }

    /// <summary>
    /// 帯グラフ各区画のピクセル幅を求める。まず秒数比で割り付け、最小幅に満たない区画を最小幅へ引き上げ、
    /// 増えたぶんを余裕のある区画から比例配分で回収して総幅を合わせる。
    /// </summary>
    private static float[] ComputeSegmentWidths(IReadOnlyList<OgCardBarSegment> segments, float totalWidth)
    {
        var widths = new float[segments.Count];
        int totalSeconds = segments.Sum(s => Math.Max(s.Seconds, 1));

        for (int i = 0; i < segments.Count; i++)
            widths[i] = totalWidth * Math.Max(segments[i].Seconds, 1) / totalSeconds;

        float deficit = 0f;
        float surplusPool = 0f;
        for (int i = 0; i < widths.Length; i++)
        {
            if (widths[i] < BarMinSegmentWidth)
            {
                deficit += BarMinSegmentWidth - widths[i];
                widths[i] = BarMinSegmentWidth;
            }
            else
            {
                surplusPool += widths[i] - BarMinSegmentWidth;
            }
        }
        if (deficit > 0f && surplusPool > 0f)
        {
            for (int i = 0; i < widths.Length; i++)
            {
                if (widths[i] <= BarMinSegmentWidth) continue;
                widths[i] -= deficit * (widths[i] - BarMinSegmentWidth) / surplusPool;
            }
        }
        return widths;
    }

    /// <summary>CM 枠を表す斜線ハッチを区画内に引く。</summary>
    private static void DrawHatch(SKCanvas canvas, SKPaint paint, SKRect rect)
    {
        canvas.Save();
        canvas.ClipRect(rect);
        paint.Color = HatchLine;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2f;
        for (float offset = -rect.Height; offset < rect.Width + rect.Height; offset += 10f)
            canvas.DrawLine(rect.Left + offset, rect.Bottom, rect.Left + offset + rect.Height, rect.Top, paint);
        paint.Style = SKPaintStyle.Fill;
        canvas.Restore();
    }

    // ════════════════════════════════ 共通パーツ ════════════════════════════════

    /// <summary>フッタ（罫線 + サイト名 + 右メタ）。組み方によらず同じ位置に出す。</summary>
    private void DrawFooter(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth)
    {
        paint.Color = Hairline;
        canvas.DrawRect(SKRect.Create(PaddingX, FooterLineY, contentWidth, 1.5f), paint);

        using (var brandFont = new SKFont(_brandTypeface, 32f))
        {
            paint.Color = Foreground;
            canvas.DrawText(_brandLabel, PaddingX, FooterTextBaseline, SKTextAlign.Left, brandFont, paint);
        }
        if (!string.IsNullOrWhiteSpace(spec.MetaRight))
        {
            using var metaFont = new SKFont(_bodyTypeface, 26f);
            paint.Color = Muted;
            canvas.DrawText(spec.MetaRight, CardWidth - PaddingX, FooterTextBaseline - 2f, SKTextAlign.Right, metaFont, paint);
        }
    }

    /// <summary>背景の縦グラデーションを敷く。</summary>
    private static void DrawBackground(SKCanvas canvas)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, CardHeight),
            BackgroundColors,
            BackgroundStops,
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(SKRect.Create(0, 0, CardWidth, CardHeight), paint);
    }

    /// <summary>
    /// 見出しが規定行数に収まる最大の文字サイズを候補から選び、その組版結果（行配列）を返す。
    /// <paramref name="font"/> の <see cref="SKFont.Size"/> は採用したサイズに書き換わる。
    /// どの候補でも収まらない場合は最小サイズで規定行数に切り詰め、末尾へ省略記号を付ける。
    /// </summary>
    private static List<string> FitTitle(string title, SKFont font, SKPaint paint, float maxWidth, float[] sizeCandidates, int maxLines)
    {
        var lines = new List<string>();
        foreach (var size in sizeCandidates)
        {
            font.Size = size;
            lines = WrapText(title, font, paint, maxWidth, maxLines + 1);
            if (lines.Count <= maxLines) return lines;
        }
        lines = lines.Take(maxLines).ToList();
        if (lines.Count > 0) lines[^1] = Ellipsize(lines[^1] + "…", font, paint, maxWidth);
        return lines;
    }

    /// <summary>
    /// 日本語テキストを指定幅で折り返す。単語区切りが無いため 1 文字ずつ積んで幅を測り、
    /// 溢れた時点で改行する。改行位置が行頭禁則文字に当たる場合は 1 文字前へ送る。
    /// </summary>
    /// <param name="maxLines">この行数に達したら以降は積まずに打ち切る（呼び出し側が超過を検知できるよう +1 を渡す運用）。</param>
    private static List<string> WrapText(string text, SKFont font, SKPaint paint, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var ch in text)
        {
            // 明示的な改行はそのまま行の区切りとして扱う。
            if (ch == '\n')
            {
                lines.Add(current.ToString());
                current.Clear();
                if (lines.Count >= maxLines) return lines;
                continue;
            }

            current.Append(ch);
            if (font.MeasureText(current.ToString(), paint) <= maxWidth) continue;

            // 溢れたので直前までで改行する。ただし送り出す 1 文字が行頭禁則なら、
            // さらに 1 文字ぶん現在行から引き上げて次行へ回す。
            var overflow = current[^1];
            current.Length--;
            string carry = overflow.ToString();
            if (LineStartForbidden.Contains(overflow) && current.Length > 1)
            {
                carry = current[^1] + carry;
                current.Length--;
            }

            lines.Add(current.ToString());
            current.Clear();
            current.Append(carry);
            if (lines.Count >= maxLines) return lines;
        }

        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    /// <summary>1 行に収まらないテキストを末尾省略記号付きで切り詰める。</summary>
    private static string Ellipsize(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (font.MeasureText(text, paint) <= maxWidth) return text;

        var trimmed = text;
        while (trimmed.Length > 1 && font.MeasureText(trimmed + "…", paint) > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed + "…";
    }

    /// <summary>
    /// ブランド書体に含まれない文字を検出する。豆腐（□）で出力されるのを防ぐための検査で、
    /// 該当があればビルドログに警告を出して本文書体への差し替えを検討できるようにする。
    /// 同じ文字の重複報告は抑止する。
    /// </summary>
    private string? FindMissingBrandGlyphs(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        // SKFont はスレッド安全ではないため判定のたびに使い捨てる（描画と同じ流儀）。
        using var probeFont = new SKFont(_brandTypeface, 10f);
        if (probeFont.ContainsGlyphs(text)) return null;

        var missing = new string(text.Where(c => !char.IsSurrogate(c) && !probeFont.ContainsGlyph(c)).Distinct().ToArray());
        if (missing.Length == 0) return null;

        lock (_missingGlyphLock)
        {
            if (!_missingGlyphReported.Add(missing)) return null;
        }
        return missing;
    }

    public void Dispose()
    {
        _brandTypeface.Dispose();
        _bodyTypeface.Dispose();
    }
}
