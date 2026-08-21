using PrecureDataStars.SiteBuilder.Utilities;
using SkiaSharp;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// OGP カード画像（1200×630 PNG）をビルド時にラスタライズするレンダラ。
/// SNS へリンクを貼ったときに表示される大カード（<c>twitter:card = summary_large_image</c>）の実体。
///
/// <para>
/// 意匠はサイト本体の視覚システムをそのまま持ち込む。サイトは見出しの下へ引いた太いピンクの罫
/// （<c>h1 { border-bottom: 3px solid --accent-pink }</c>）で紙面を締め、数は「大きいピンクの数字＋
/// 小さい単位」で見せ、ブランド書体 Kiwi Maru はヘッダ・フッタのワードマークにだけ使う——という
/// 規律で出来ている。カードもこれに従う：
/// </para>
/// <list type="bullet">
///   <item><description>地はホームのヒーローと同じ淡ピンク→クリームのグラデーション。
///     カードはページ本文ではなく「ページの顔」なので、通常ページの白地ではなくヒーローの地を採る。</description></item>
///   <item><description>見出しは本文書体（Noto Sans JP）の Bold。下に太いピンクの罫を引いて締める。</description></item>
///   <item><description>数は大きいピンクの数字と小さい単位の組。枠線で囲んだピルにはしない。</description></item>
///   <item><description>Kiwi Maru はカード下部のサイト名にのみ使う（＝ワードマーク運用）。</description></item>
/// </list>
///
/// <para>
/// 組み方は入力に応じて 2 通り。<see cref="OgCardSpec.IsDense"/> が false なら
/// 「パンくず → 見出し → 説明文」の標準レイアウト、true なら識別子・数・帯グラフ・事実行を積む
/// 高密度レイアウトになる。高密度側はエピソードのように「このサイトにしか無い情報」を
/// カード 1 枚で見せるためのもので、尺構成の帯グラフはサイト本体のフォーマット表と同じ配色・同じ比率で描く。
/// </para>
/// <para>
/// 書体はブラウザ側と同じ Noto Sans JP / Kiwi Maru を使うが、ラスタライズには実ファイルが要るため
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
    public const int CardWidth = 1200;
    public const int CardHeight = 630;

    /// <summary>左右の内側余白。SNS のタイムラインで端が切れても文字が欠けない程度に広く取る。</summary>
    private const float PaddingX = 76f;

    /// <summary>フッタ罫線の Y 座標。中身の量によらずこの位置は動かさない。</summary>
    private const float FooterLineY = 552f;

    /// <summary>フッタのサイト名のベースライン。</summary>
    private const float FooterTextBaseline = 600f;

    // ──────── 配色（サイトの CSS 変数と対応させる） ────────

    /// <summary>
    /// 地色。サイトの <c>.hero.hero-gradient</c>（180deg 淡ピンク→クリーム）と同値。
    /// カードはページ本文ではなく「ページの顔」なので、通常ページの白地ではなくヒーローの地を使う。
    /// </summary>
    private static readonly SKColor[] BackgroundColors =
    {
        SKColor.Parse("#fff1f6"),
        SKColor.Parse("#fde7ef"),
        SKColor.Parse("#fff8f0")
    };
    private static readonly float[] BackgroundStops = { 0f, 0.45f, 1f };
    /// <summary>アクセント（--accent-pink）。見出し下の罫と数字に使う。</summary>
    private static readonly SKColor AccentPink = SKColor.Parse("#e91e63");
    /// <summary>本文色（--fg）。</summary>
    private static readonly SKColor Foreground = SKColor.Parse("#1a1a1a");
    /// <summary>ヒーロー見出しの色（サイトの <c>.hero.hero-gradient h1</c>）。</summary>
    private static readonly SKColor HeroTitleColor = SKColor.Parse("#be185d");
    /// <summary>ヒーローのタグラインの色（サイトの <c>.hero.hero-gradient .lead</c>）。</summary>
    private static readonly SKColor HeroLeadColor = SKColor.Parse("#9d174d");
    /// <summary>補助色（--muted）。</summary>
    private static readonly SKColor Muted = SKColor.Parse("#666666");
    /// <summary>罫線。フッタの区切りと帯グラフの外枠に使う。ピンク寄りの地に馴染む薄色。</summary>
    private static readonly SKColor Hairline = SKColor.Parse("#e7c8d4");
    /// <summary>帯グラフ区画の仕切り。隣り合う淡色を分離する。</summary>
    private static readonly SKColor BarDivider = SKColor.Parse("#ffffff");
    /// <summary>ハッチ（CM 枠）の斜線色。</summary>
    private static readonly SKColor HatchLine = SKColor.Parse("#c9c9d2");
    /// <summary>帯グラフ区画内のラベル色。</summary>
    private static readonly SKColor BarLabel = SKColor.Parse("#33333a");
    /// <summary>色指定が解決できなかった区画のフォールバック色（サイトの fmt-p-misc 相当）。</summary>
    private static readonly SKColor BarFallback = SKColor.Parse("#d7d7de");

    // ──────── 組版パラメータ ────────

    /// <summary>標準レイアウトの見出しサイズ候補。上から順に試し、規定行数に収まった時点で採用する。</summary>
    private static readonly float[] TitleSizeCandidates = { 58f, 51f, 44f, 38f };

    /// <summary>高密度レイアウトの見出しサイズ候補（上下の要素に場所を譲るぶん小さめ）。</summary>
    private static readonly float[] DenseTitleSizeCandidates = { 46f, 40f, 35f, 32f };

    /// <summary>見出しの最大行数（標準 / 高密度）。これを超える分は末尾を省略記号で切り詰める。</summary>
    private const int TitleMaxLines = 2;
    private const int DenseTitleMaxLines = 2;

    /// <summary>行送り倍率（日本語の詰まりを避けるための基準）。</summary>
    private const float TitleLineHeightRatio = 1.28f;

    /// <summary>
    /// 見出し下のピンク罫。サイトの <c>h1</c> は 3px だが、あちらは本文 16px 基準・幅 960px の紙面。
    /// カードは 1200×630 を縮小表示されるので、同じ比率感が出るよう太めに引く。
    /// </summary>
    private const float TitleRuleHeight = 5f;
    private const float TitleRuleGap = 18f;

    /// <summary>本文ブロックがフッタ罫線に食い込まないよう確保する最小の間隔。</summary>
    private const float FooterClearance = 24f;

    /// <summary>帯グラフの高さ、区画の最小幅、区画内ラベルを出す最小幅。</summary>
    private const float BarHeight = 44f;
    private const float BarMinSegmentWidth = 5f;
    private const float BarLabelMinWidth = 62f;

    /// <summary>
    /// 見出しが 2 行に伸びて余白が痩せたときに帯グラフを縮められる下限。
    /// これを割り込む場合は尺の凡例を落として帯そのものを優先する。
    /// </summary>
    private const float BarMinHeight = 32f;

    /// <summary>帯グラフの下に置く尺凡例が占める高さ。</summary>
    private const float BarCaptionHeight = 30f;

    /// <summary>識別子（第N話など）の文字サイズ。</summary>
    private const float HeadlineFontSize = 35f;

    /// <summary>
    /// 数の組（大きい数字＋小さい単位＋小さいラベル）の各サイズ。
    /// サイトの <c>.music-category-stats</c> と同じ「数を主役にして単位を添える」語彙に揃える。
    /// </summary>
    private const float StatValueFontSize = 37f;
    private const float StatUnitFontSize = 21f;
    private const float StatLabelFontSize = 20f;
    private const float StatGap = 38f;

    /// <summary>数の組が 1 行に収まらないときの行送り。</summary>
    private const float StatLineHeight = 52f;

    /// <summary>ファクト行の文字サイズ・行送り・最大行数。超過分は末尾から捨てる。</summary>
    private const float FactFontSize = 23f;
    private const float FactLineHeight = 33f;
    private const int FactMaxLines = 3;

    /// <summary>1 行 1 項目で積むファクトの続き行を、値の左端からさらに落とす量。</summary>
    private const float StackedContinuationIndent = 26f;

    /// <summary>
    /// 標準レイアウトの説明文の文字サイズと行送り。
    /// フッタまでの余白に何行入るかを実測して折り返すため、行送りは固定値で持つ。
    /// </summary>
    private const float DescriptionFontSize = 28f;
    private const float DescriptionLineHeight = 48f;

    /// <summary>
    /// ラベル＋値を 1 行に流すファクトの項目間隔と最大行数。
    /// 役職と担当者の対を 4 組ほど横に並べるため、対の内側（ラベル→値）より
    /// 対と対のあいだを明確に広く取らないと 1 本の長い文字列に見えてしまう。
    /// </summary>
    private const float InlineFactGap = 44f;
    private const int InlineFactMaxLines = 2;

    /// <summary>
    /// 行頭に置いてはいけない文字（行頭禁則）。折り返し位置がこれらに当たった場合、
    /// 1 文字ぶん前の行へ送り込んで自然な組版にする。
    /// </summary>
    private const string LineStartForbidden = "。、．，）」』】〕〉》”’!?！？：；・ーぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ";

    /// <summary>
    /// 数の表記を「数字」と「単位」に割るための判定。先頭が数字（桁区切りのカンマ可）で始まり、
    /// そのあとに数字が現れないものだけを分割する。<c>28:45</c>（尺）や <c>MJCD-23079</c>（品番）は
    /// 数の大小を語る値ではないため分割せず、そのまま 1 語として扱う。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex CountValueRegex =
        new(@"^([0-9][0-9,]*)([^0-9]*)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>ブランド書体（Kiwi Maru）。カード下部のワードマークにのみ使う。</summary>
    private readonly SKTypeface _brandTypeface;
    /// <summary>本文書体（Noto Sans JP Regular）。</summary>
    private readonly SKTypeface _bodyTypeface;
    /// <summary>見出し書体（Noto Sans JP Bold）。サイトの h1・h2 と同じ太さ。</summary>
    private readonly SKTypeface _boldTypeface;

    /// <summary>ワードマークにブランド書体で描けない文字があった場合の記録（重複報告の抑止）。</summary>
    private readonly HashSet<string> _missingGlyphReported = new();
    private readonly object _missingGlyphLock = new();

    /// <summary>カード下部に固定で出すサイト名。</summary>
    private readonly string _brandLabel;

    /// <summary>本文書体に適用するウェイト（サイトの本文と同じ Regular）。</summary>
    private const int BodyFontWeight = 400;

    /// <summary>見出し書体に適用するウェイト（サイトの h1・h2 と同じ Bold）。</summary>
    private const int HeadingFontWeight = 700;

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
        // Noto Sans JP は可変フォントで、wght 軸の既定値が最小の 100（Thin）になっている。
        // 素直に読み込むと本文が Thin で焼かれ、サイト本文とは別書体に見えるほど細くなるため、
        // ウェイトを明示して本文用（400）と見出し用（700）の 2 つを作る。
        var notoPath = Path.Combine(fontDir, "NotoSansJP.ttf");
        _bodyTypeface = LoadTypeface(notoPath, BodyFontWeight);
        _boldTypeface = LoadTypeface(notoPath, HeadingFontWeight);
    }

    /// <summary>
    /// フォントファイルを読み込む。<paramref name="weight"/> を指定した場合は可変フォントの
    /// <c>wght</c> 軸をその値に固定したインスタンスを取り出す（可変でなければそのまま返る）。
    /// </summary>
    private static SKTypeface LoadTypeface(string path, int? weight = null)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"OGP カード描画用のフォントが見つかりません: {path}", path);

        var typeface = SKTypeface.FromFile(path)
            ?? throw new InvalidOperationException($"フォントの読み込みに失敗しました: {path}");

        if (weight is not int w) return typeface;

        var args = new SKFontArguments
        {
            VariationDesignPosition = new[]
            {
                new SKFontVariationPositionCoordinate
                {
                    // 'wght' を 4 バイトタグとして詰める。
                    Axis = ('w' << 24) | ('g' << 16) | ('h' << 8) | 't',
                    Value = w
                }
            }
        };
        var instance = typeface.Clone(args);
        if (instance is null) return typeface;

        typeface.Dispose();
        return instance;
    }

    /// <summary>
    /// カードを 1 枚描画して PNG ファイルへ書き出す。出力先の親ディレクトリは自動生成する。
    /// 同一入力からは常に同一バイト列が出るため、デプロイ時の MD5 差分比較で
    /// 内容が変わっていないカードは再アップロードされない。
    /// </summary>
    /// <param name="spec">カードに載せる内容。</param>
    /// <param name="outputFilePath">書き出し先の絶対パス（拡張子 .png）。</param>
    /// <returns>ワードマークにブランド書体で描けない文字があればその文字列、無ければ null。</returns>
    public string? Render(OgCardSpec spec, string outputFilePath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(CardWidth, CardHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        DrawBackground(canvas);

        using var paint = new SKPaint { IsAntialias = true };
        float contentWidth = CardWidth - PaddingX * 2;

        if (spec.IsDense)
        {
            // ヒーロー調のカードは見出しそのものがワードマークなので、フッタに同じ名前を重ねない。
            // フッタが無い分だけ下に空きができるので、一度測ってから中身をカードの上下中央へ据える
            // （上詰めのままだと下半分がまるごと空いてしまう）。
            float floor = spec.HeroVoice ? CardHeight - PaddingX : FooterLineY - FooterClearance;
            float offset = 0f;
            if (spec.HeroVoice)
            {
                using var recorder = new SKPictureRecorder();
                var probe = recorder.BeginRecording(SKRect.Create(CardWidth, CardHeight));
                float bottom = DrawDenseBody(probe, paint, spec, contentWidth);
                recorder.EndRecording().Dispose();
                offset = Math.Max(0f, (floor - bottom) / 2f);
            }

            canvas.Save();
            canvas.Translate(0f, offset);
            DrawDenseBody(canvas, paint, spec, contentWidth);
            canvas.Restore();
        }
        else
        {
            DrawStandardBody(canvas, paint, spec, contentWidth);
        }

        if (!spec.HeroVoice) DrawFooter(canvas, paint, spec, contentWidth);

        PathUtil.EnsureParentDirectory(outputFilePath);
        using (var image = surface.Snapshot())
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.Create(outputFilePath))
        {
            data.SaveTo(stream);
        }

        return FindMissingBrandGlyphs(_brandLabel);
    }

    // ════════════════════════════════ 標準レイアウト ════════════════════════════════

    /// <summary>
    /// 「パンくず → 見出し → 説明文」の標準の組み方。索引・統計・規約など、
    /// 構造化された事実を持たないページ向け。説明文はフッタまでの余白に入る行数を実測して折り返す
    /// （1 行に切り詰めるとカード面積の大半が空いたまま文章だけ途切れるため）。
    /// </summary>
    private void DrawStandardBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth)
    {
        float y = 96f;

        // ── パンくず経路 ──
        if (!string.IsNullOrWhiteSpace(spec.Kicker))
        {
            using var kickerFont = new SKFont(_bodyTypeface, 26f);
            paint.Color = Muted;
            canvas.DrawText(Ellipsize(spec.Kicker, kickerFont, paint, contentWidth), PaddingX, y, SKTextAlign.Left, kickerFont, paint);
            y += 62f;
        }

        // ── 見出し＋ピンク罫 ──
        using var titleFont = new SKFont(_boldTypeface, TitleSizeCandidates[^1]);
        var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, TitleSizeCandidates, TitleMaxLines);

        paint.Color = Foreground;
        foreach (var line in titleLines)
        {
            y += titleFont.Size;
            canvas.DrawText(line, PaddingX, y, SKTextAlign.Left, titleFont, paint);
            y += titleFont.Size * (TitleLineHeightRatio - 1f);
        }
        y = DrawTitleRule(canvas, paint, y, contentWidth);

        // ── 説明文（フッタ罫線までの余白に収まる行数だけ折り返す） ──
        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            using var descFont = new SKFont(_bodyTypeface, DescriptionFontSize);
            paint.Color = Foreground;

            y += 20f;
            float available = (FooterLineY - FooterClearance) - y;
            int maxLines = Math.Max(0, (int)(available / DescriptionLineHeight));
            if (maxLines > 0)
            {
                var lines = WrapText(spec.Subtitle, descFont, paint, contentWidth, maxLines + 1);
                if (lines.Count > maxLines)
                {
                    lines = lines.Take(maxLines).ToList();
                    lines[^1] = Ellipsize(lines[^1] + "…", descFont, paint, contentWidth);
                }
                foreach (var line in lines)
                {
                    y += DescriptionFontSize;
                    canvas.DrawText(line, PaddingX, y, SKTextAlign.Left, descFont, paint);
                    y += DescriptionLineHeight - DescriptionFontSize;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.MetaLeft))
        {
            using var metaFont = new SKFont(_bodyTypeface, 26f);
            paint.Color = Muted;
            float baseline = Math.Min(y + 26f, FooterLineY - FooterClearance);
            canvas.DrawText(Ellipsize(spec.MetaLeft, metaFont, paint, contentWidth), PaddingX, baseline, SKTextAlign.Left, metaFont, paint);
        }
    }

    // ════════════════════════════════ 高密度レイアウト ════════════════════════════════

    /// <summary>
    /// 識別子・数・帯グラフ・事実行を積む高密度の組み方。
    /// 上端からは「所属 → 識別子 → 見出し＋ピンク罫 → 数」を順に降ろし、事実行はフッタ罫線の直上から
    /// 上へ積む。帯グラフは残った余白へ入れるため、見出しが 1 行でも 2 行でも全体の重心が崩れない。
    /// </summary>
    /// <returns>描いた中身の下端 Y。上下中央に据え直すときの高さ計算に使う。</returns>
    private float DrawDenseBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth)
    {
        // ── 最上段（左：所属シリーズ・種別 ／ 右：放送日時などの補助） ──
        const float kickerBaseline = 72f;
        bool hasKicker = !string.IsNullOrWhiteSpace(spec.Kicker) || !string.IsNullOrWhiteSpace(spec.KickerRight);
        if (hasKicker)
        {
            // 左（所属シリーズ）はカードの主語なので太字で大きく、右（放送日）は補助なので小さく薄く。
            using var kickerFont = new SKFont(_boldTypeface, 31f);
            using var kickerRightFont = new SKFont(_bodyTypeface, 25f);
            float rightWidth = 0f;
            if (!string.IsNullOrWhiteSpace(spec.KickerRight))
            {
                paint.Color = Muted;
                rightWidth = kickerRightFont.MeasureText(spec.KickerRight, paint);
                canvas.DrawText(spec.KickerRight, CardWidth - PaddingX, kickerBaseline, SKTextAlign.Right, kickerRightFont, paint);
            }
            if (!string.IsNullOrWhiteSpace(spec.Kicker))
            {
                paint.Color = Foreground;
                float room = contentWidth - (rightWidth > 0f ? rightWidth + 24f : 0f);
                canvas.DrawText(Ellipsize(spec.Kicker, kickerFont, paint, room), PaddingX, kickerBaseline, SKTextAlign.Left, kickerFont, paint);
            }
        }

        // 前置きを持たないカードはその行ぶんの空きを残さず、見出しを最上段へ繰り上げる
        // （空の 1 行を空けたままにすると、見出しが宙に浮いて見える）。
        float y = hasKicker ? kickerBaseline : kickerBaseline - 34f;

        // ── 識別子（第N話） ──
        if (!string.IsNullOrWhiteSpace(spec.Headline))
        {
            using var headlineFont = new SKFont(_boldTypeface, HeadlineFontSize);
            y += 54f;
            paint.Color = Muted;
            canvas.DrawText(Ellipsize(spec.Headline, headlineFont, paint, contentWidth), PaddingX, y, SKTextAlign.Left, headlineFont, paint);
        }

        // ── 見出し＋ピンク罫 ──
        // ルビ付き HTML があれば振り仮名つきで組む（サイト本体のサブタイトル表示と同じ読みを添える）。
        y += 22f;
        var rubyUnits = string.IsNullOrWhiteSpace(spec.TitleRubyHtml)
            ? new List<RubyUnit>()
            : ParseRubyUnits(spec.TitleRubyHtml);

        if (rubyUnits.Count > 0)
        {
            y = DrawRubyTitle(canvas, paint, rubyUnits, PaddingX, y, contentWidth, DenseTitleSizeCandidates, DenseTitleMaxLines);
        }
        else
        {
            using var titleFont = new SKFont(spec.HeroVoice ? _brandTypeface : _boldTypeface, DenseTitleSizeCandidates[^1]);
            var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, DenseTitleSizeCandidates, DenseTitleMaxLines);
            paint.Color = spec.HeroVoice ? HeroTitleColor : Foreground;
            foreach (var line in titleLines)
            {
                y += titleFont.Size;
                canvas.DrawText(line, PaddingX, y, SKTextAlign.Left, titleFont, paint);
                y += titleFont.Size * (TitleLineHeightRatio - 1f);
            }
        }
        y = DrawTitleRule(canvas, paint, y, contentWidth);

        // ── タグライン（罫のすぐ下） ──
        // サイトのヒーローが h1 → 罫 → lead の順で組んでいるのに合わせる。
        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            using var leadFont = new SKFont(spec.HeroVoice ? _brandTypeface : _bodyTypeface, 26f);
            paint.Color = spec.HeroVoice ? HeroLeadColor : Muted;
            // ヒーロー調は要素数が少なくフッタも持たないぶん、行間を広く取って落ち着かせる。
            y += (spec.HeroVoice ? 34f : 20f) + leadFont.Size;
            canvas.DrawText(Ellipsize(spec.Subtitle, leadFont, paint, contentWidth), PaddingX, y, SKTextAlign.Left, leadFont, paint);
        }

        // ── 数（大きいピンクの数字＋小さい単位） ──
        if (spec.Badges.Count > 0)
            y = DrawStats(canvas, paint, spec.Badges, PaddingX, y + (spec.HeroVoice ? 40f : 16f), contentWidth);

        // ── 数の直下に添えるメタ（基準点など） ──
        // 「いつ時点の数か」は数そのものより後に読ませたい情報なので、数の直上ではなく直下に置く。
        if (!string.IsNullOrWhiteSpace(spec.MetaLeft))
        {
            using var metaFont = new SKFont(_bodyTypeface, 21f);
            paint.Color = Muted;
            y += spec.HeroVoice ? 44f : 28f;
            canvas.DrawText(Ellipsize(spec.MetaLeft, metaFont, paint, contentWidth), PaddingX, y, SKTextAlign.Left, metaFont, paint);
        }

        // ── 事実行 ──
        // 帯グラフを持つカードは、帯を置く余白を空けるためフッタ罫線の直上へ下寄せする。
        // 帯を持たないカードで下寄せすると上の要素とのあいだが大きく空いて間延びするため、
        // その場合は直前の要素の下へ続けて置く。
        bool hasBar = spec.Bar.Count > 0;
        float factsAnchor = hasBar ? FooterLineY - FooterClearance : y + 46f;
        // 帯が無いカードはフッタまでの空き高さから入る行数を決める。行数を固定にすると、
        // 項目が多いカード（シリーズの主要スタッフなど）で余白があるのに末尾が落ちてしまう。
        int factsMaxLines = hasBar
            ? InlineFactMaxLines
            : Math.Max(1, (int)(((FooterLineY - FooterClearance) - factsAnchor) / FactLineHeight) + 1);
        if (hasBar) factsMaxLines = Math.Min(factsMaxLines, FactMaxLines);

        // 2 種のファクトは排他ではない。楽曲カードのように「作り手を 1 行へ流し込み、その下に
        // 版と歌い手を 1 行 1 項目で積む」構成があるため、両方あるときは上から順に置く。
        float factsTop = factsAnchor;
        float flowY = factsAnchor;
        float contentBottom = y;
        if (spec.InlineFacts.Count > 0)
        {
            var r = DrawInlineFacts(canvas, paint, spec.InlineFacts, PaddingX, flowY, contentWidth, anchorToTop: !hasBar, maxLines: factsMaxLines);
            factsTop = r.Top;
            flowY = r.Bottom + FactLineHeight + 8f;
            contentBottom = r.Bottom;
        }
        if (spec.Facts.Count > 0)
        {
            // 併記のときは必ず下段なので、上から積む（帯を持つカードで両方を使う想定は無い）。
            bool stackTop = !hasBar || spec.InlineFacts.Count > 0;
            var r = DrawStackedFacts(canvas, paint, spec.Facts, PaddingX, stackTop ? flowY : factsAnchor, anchorToTop: stackTop, maxLines: factsMaxLines);
            if (spec.InlineFacts.Count == 0) factsTop = r.Top;
            contentBottom = Math.Max(contentBottom, r.Bottom);
        }

        // ── 帯グラフ（数と事実行のあいだの余白へ） ──
        if (spec.Bar.Count > 0)
        {
            float gapTop = y + 16f;
            float gapBottom = factsTop - 16f;
            float available = gapBottom - gapTop;

            // 見出しが 2 行に伸びると余白が痩せるため、帯の高さと凡例の有無を余白に合わせて畳む。
            // 事実行と重ならないことを最優先し、足りなければ凡例 → 帯の高さの順に譲る。
            bool hasCaption = !string.IsNullOrWhiteSpace(spec.BarCaption) || !string.IsNullOrWhiteSpace(spec.BarTotalLabel);
            if (hasCaption && available < BarMinHeight + BarCaptionHeight) hasCaption = false;
            float captionHeight = hasCaption ? BarCaptionHeight : 0f;
            float barHeight = Math.Clamp(available - captionHeight, BarMinHeight, BarHeight);
            float blockHeight = barHeight + captionHeight;

            float barTop = gapTop + Math.Max(0f, (available - blockHeight) / 3f);
            DrawFormatBar(canvas, paint, spec, PaddingX, barTop, contentWidth, barHeight, hasCaption);
        }

        return contentBottom;
    }

    /// <summary>
    /// 見出しの下に太いピンクの罫を引き、その下端 Y を返す。
    /// サイトの <c>h1</c> と同じ「1 本の罫でタイトル行を締める」規律をカードにも通す。
    /// </summary>
    private static float DrawTitleRule(SKCanvas canvas, SKPaint paint, float titleBottom, float contentWidth)
    {
        float ruleTop = titleBottom + TitleRuleGap;
        paint.Color = AccentPink;
        canvas.DrawRect(SKRect.Create(PaddingX, ruleTop, contentWidth, TitleRuleHeight), paint);
        return ruleTop + TitleRuleHeight;
    }

    /// <summary>
    /// 数の組を横に並べて描き、その下端 Y を返す。
    /// サイトの <c>.music-category-stats</c> と同じ語彙で、数字を大きくピンクの太字に、
    /// 単位とラベルを小さく添えて、視線が数へ行くようにする。
    /// 品番や尺のように「数の大小を語らない値」は分割せず 1 語として置く。
    /// </summary>
    private float DrawStats(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardBadge> badges, float x, float top, float maxWidth)
    {
        using var labelFont = new SKFont(_bodyTypeface, StatLabelFontSize);
        using var valueFont = new SKFont(_boldTypeface, StatValueFontSize);
        using var unitFont = new SKFont(_bodyTypeface, StatUnitFontSize);

        float baseline = top + StatValueFontSize;
        float cursor = x;

        foreach (var badge in badges)
        {
            // 収まらない場合は次の行へ送る（ホームのように数を 8 個並べるカードがあるため）。
            var match = CountValueRegex.Match(badge.Value);
            string number = match.Success ? match.Groups[1].Value : badge.Value;
            string unit = match.Success ? match.Groups[2].Value : "";

            // 収まらない組は落とす（幅を測ってから描く）。
            float width = 0f;
            if (!string.IsNullOrWhiteSpace(badge.Label)) width += labelFont.MeasureText(badge.Label, paint) + 10f;
            width += valueFont.MeasureText(number, paint);
            if (unit.Length > 0) width += unitFont.MeasureText(unit, paint) + 3f;
            if (cursor + width > x + maxWidth)
            {
                if (cursor <= x) break;
                cursor = x;
                baseline += StatLineHeight;
            }

            if (!string.IsNullOrWhiteSpace(badge.Label))
            {
                paint.Color = Muted;
                canvas.DrawText(badge.Label, cursor, baseline, SKTextAlign.Left, labelFont, paint);
                cursor += labelFont.MeasureText(badge.Label, paint) + 10f;
            }

            paint.Color = AccentPink;
            canvas.DrawText(number, cursor, baseline, SKTextAlign.Left, valueFont, paint);
            cursor += valueFont.MeasureText(number, paint);

            if (unit.Length > 0)
            {
                paint.Color = Muted;
                cursor += 3f;
                canvas.DrawText(unit, cursor, baseline, SKTextAlign.Left, unitFont, paint);
                cursor += unitFont.MeasureText(unit, paint);
            }

            cursor += StatGap;
        }

        return baseline + 8f;
    }

    /// <summary>描画の最小単位。行に流し込んだあと、行ごとに左から順に描く。</summary>
    private sealed record FactPiece(string Text, SKColor Color, bool IsLabel, float TrailingGap);

    /// <summary>
    /// ラベルと値の組を流し込んで描き、ブロック上端の Y を返す。
    /// ラベルを役職色・値を本文色に分けることで、羅列ではなく「役職 → 担当者」の対応として読ませる。
    ///
    /// <para>
    /// 折り返しは項目の境目だけでなく<b>項目の中でも</b>行う。連名が長い役職（プロデューサー 5 名など）は
    /// 1 項目だけで行幅を超えるため、項目単位でしか折り返せないと末尾が「、…」の形で落ちてしまう。
    /// 値を文字単位で流し込み、行頭禁則を効かせながら次の行へ送る。
    /// </para>
    /// </summary>
    /// <param name="anchor">
    /// <paramref name="anchorToTop"/> が true なら 1 行目のベースライン、false なら最終行のベースライン。
    /// </param>
    /// <param name="anchorToTop">true で上から下へ、false で下から上へ積む。</param>
    /// <param name="maxLines">描画に使える行数。</param>
    private (float Top, float Bottom) DrawInlineFacts(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardFactLine> facts, float x, float anchor, float maxWidth, bool anchorToTop = false, int maxLines = InlineFactMaxLines)
    {
        using var labelFont = new SKFont(_boldTypeface, FactFontSize - 4f);
        using var valueFont = new SKFont(_bodyTypeface, FactFontSize);

        var lines = new List<List<FactPiece>> { new() };
        float used = 0f;

        bool NewLine()
        {
            if (lines.Count >= Math.Max(1, maxLines)) return false;
            lines.Add(new List<FactPiece>());
            used = 0f;
            return true;
        }

        foreach (var fact in facts)
        {
            // 項目の頭（ラベル）。行に載る余地が無ければ改行してから置く。
            var labelPieces = new List<FactPiece>();
            float labelWidth = 0f;
            if (fact.LabelParts.Count > 0)
            {
                foreach (var part in fact.LabelParts)
                {
                    var color = SKColor.TryParse(part.ColorHex, out var c) ? c : Muted;
                    labelPieces.Add(new FactPiece(part.Text, color, true, 0f));
                    labelWidth += labelFont.MeasureText(part.Text, paint);
                }
                labelWidth += 8f;
            }
            else if (!string.IsNullOrWhiteSpace(fact.Label))
            {
                var color = SKColor.TryParse(fact.LabelColorHex, out var c) ? c : Muted;
                labelPieces.Add(new FactPiece(fact.Label, color, true, 0f));
                labelWidth = labelFont.MeasureText(fact.Label, paint) + 8f;
            }

            float lead = used > 0f ? InlineFactGap : 0f;
            // ラベルと最低 1 文字ぶんが載らないなら改行する。
            float minText = valueFont.Size * 2f;
            if (used > 0f && used + lead + labelWidth + minText > maxWidth && !NewLine()) break;

            if (used > 0f) { used += InlineFactGap; lines[^1][^1] = lines[^1][^1] with { TrailingGap = InlineFactGap }; }
            if (labelPieces.Count > 0)
            {
                // ラベル断片は隙間なしで連ね、最後の断片のあとにだけ 8px の間を置く。
                for (int i = 0; i < labelPieces.Count; i++)
                {
                    bool last = i == labelPieces.Count - 1;
                    lines[^1].Add(labelPieces[i] with { TrailingGap = last ? 8f : 0f });
                }
                used += labelWidth;
            }

            // 値を文字単位で流し込む。入りきらない分は次の行へ送る。
            string remaining = fact.Text;
            while (remaining.Length > 0)
            {
                float available = maxWidth - used;
                string chunk = TakeFittingPrefix(remaining, valueFont, paint, available);
                if (chunk.Length == 0)
                {
                    if (!NewLine()) return FinishInlineFacts(canvas, paint, lines, labelFont, valueFont, x, anchor, anchorToTop);
                    continue;
                }
                lines[^1].Add(new FactPiece(chunk, Foreground, false, 0f));
                used += valueFont.MeasureText(chunk, paint);
                remaining = remaining[chunk.Length..];
                if (remaining.Length > 0 && !NewLine()) return FinishInlineFacts(canvas, paint, lines, labelFont, valueFont, x, anchor, anchorToTop);
            }
        }

        return FinishInlineFacts(canvas, paint, lines, labelFont, valueFont, x, anchor, anchorToTop);
    }

    /// <summary>
    /// 指定幅に収まる最長の先頭部分を返す。切れ目が行頭禁則文字に当たる場合は 1 文字手前で切る。
    /// 1 文字も入らない場合は空文字（呼び出し側が改行する合図）。
    /// </summary>
    private static string TakeFittingPrefix(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (maxWidth <= 0f) return "";
        if (font.MeasureText(text, paint) <= maxWidth) return text;

        int take = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (font.MeasureText(text[..i], paint) > maxWidth) break;
            take = i;
        }
        if (take == 0) return "";

        // 次の行の頭が禁則文字にならないよう 1 文字戻す。
        if (take < text.Length && LineStartForbidden.Contains(text[take]) && take > 1) take--;
        return text[..take];
    }

    /// <summary>流し込み済みの行を描き、ブロック上端の Y を返す。</summary>
    private (float Top, float Bottom) FinishInlineFacts(
        SKCanvas canvas, SKPaint paint, List<List<FactPiece>> lines,
        SKFont labelFont, SKFont valueFont, float x, float anchor, bool anchorToTop)
    {
        if (lines.Count > 0 && lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return (anchor, anchor);

        float firstBaseline = anchorToTop ? anchor : anchor - (lines.Count - 1) * FactLineHeight;
        for (int i = 0; i < lines.Count; i++)
        {
            float baseline = firstBaseline + i * FactLineHeight;
            float cursor = x;
            foreach (var piece in lines[i])
            {
                var font = piece.IsLabel ? labelFont : valueFont;
                paint.Color = piece.Color;
                canvas.DrawText(piece.Text, cursor, baseline, SKTextAlign.Left, font, paint);
                cursor += font.MeasureText(piece.Text, paint) + piece.TrailingGap;
            }
        }
        return (firstBaseline - valueFont.Size, firstBaseline + (lines.Count - 1) * FactLineHeight);
    }

    /// <summary>
    /// 1 行 1 項目で積むファクト行を描き、ブロック上端の Y を返す。
    /// <see cref=OgCardFactLine.SubText/> を持つ項目は 2 行を使い、続きは値の左端へ字下げして揃える。
    /// </summary>
    /// <param name="anchor">
    /// <paramref name="anchorToTop"/> が true なら 1 行目のベースライン、false なら最終行のベースライン。
    /// </param>
    /// <param name="anchorToTop">true で上から下へ、false で下から上へ積む。</param>
    /// <param name="maxLines">描画に使える行数（項目数ではなく行数）。</param>
    private (float Top, float Bottom) DrawStackedFacts(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardFactLine> facts, float x, float anchor, bool anchorToTop = false, int maxLines = FactMaxLines)
    {
        using var labelFont = new SKFont(_boldTypeface, FactFontSize - 3f);
        using var valueFont = new SKFont(_bodyTypeface, FactFontSize);
        using var subFont = new SKFont(_bodyTypeface, FactFontSize - 3f);

        // 続き行の字下げ量は最も広いラベルに合わせる。値の左端が縦に揃うので、
        // 2 段に割れた項目も 1 つのまとまりとして読める。
        float indent = facts
            .Where(f => !string.IsNullOrWhiteSpace(f.Label))
            .Select(f => labelFont.MeasureText(f.Label, paint))
            .DefaultIfEmpty(0f)
            .Max() + 14f;
        // 続き行はさらに一段深く落とす。値の左端に揃えるだけだと「同じ項目の続き」に見えず、
        // 別の項目が始まったように読めてしまう。
        float continuationIndent = indent + StackedContinuationIndent;

        // 先に描画行を組む（1 項目が値の折り返しと続き行で複数行を使うので、上限は行単位で数える）。
        // 行は「字下げ量・ラベル（先頭行のみ）・本文・従属級数か」の 4 点で表す。
        var rendered = new List<(float Indent, OgCardFactLine? Head, string Text, bool IsSub)>();
        int limit = Math.Max(1, maxLines);
        foreach (var fact in facts)
        {
            // 値はラベルの右に流し、入りきらない分は値の左端へ揃えて折り返す。
            // 折り返しは項目の区切り（読点・スラッシュ・空白）でのみ起こすので、氏名や社名が途中で割れない。
            var valueLines = WrapFactValue(fact.Text, valueFont, paint, CardWidth - PaddingX - (x + indent));
            var subLines = string.IsNullOrWhiteSpace(fact.SubText)
                ? new List<string>()
                : WrapFactValue(fact.SubText, subFont, paint, CardWidth - PaddingX - (x + continuationIndent));

            // 項目は途中で切らない。丸ごと入らないなら、その項目からは載せない。
            if (rendered.Count + valueLines.Count + subLines.Count > limit) break;

            for (int i = 0; i < valueLines.Count; i++)
                rendered.Add((0f, i == 0 ? fact : null, valueLines[i], false));
            foreach (var line in subLines)
                rendered.Add((continuationIndent, null, line, true));
        }
        if (rendered.Count == 0) return (anchor, anchor);

        float firstBaseline = anchorToTop ? anchor : anchor - (rendered.Count - 1) * FactLineHeight;
        for (int i = 0; i < rendered.Count; i++)
        {
            float baseline = firstBaseline + i * FactLineHeight;
            var (lineIndent, head, text, isSub) = rendered[i];
            float cursor = x + lineIndent;

            if (isSub)
            {
                paint.Color = Muted;
                canvas.DrawText(text, cursor, baseline, SKTextAlign.Left, subFont, paint);
                continue;
            }

            if (head is not null && !string.IsNullOrWhiteSpace(head.Label))
            {
                paint.Color = SKColor.TryParse(head.LabelColorHex, out var labelColor) ? labelColor : Muted;
                canvas.DrawText(head.Label, cursor, baseline, SKTextAlign.Left, labelFont, paint);
            }
            // ラベルの有無に関わらず値の左端は揃える。折り返した続き行も同じ位置から始まるので、
            // 1 項目が何行に伸びても「どこからどこまでが 1 項目か」が縦の揃いで読める。
            paint.Color = Foreground;
            canvas.DrawText(text, x + indent, baseline, SKTextAlign.Left, valueFont, paint);
        }
        return (firstBaseline - FactFontSize, firstBaseline + (rendered.Count - 1) * FactLineHeight);
    }

    /// <summary>
    /// 事実行の値を、与えられた幅に収まる行へ割る。
    /// 割り位置は項目の区切り（読点・中黒・スラッシュ）の直後のみ。
    /// 空白は割り位置に含めない——日本語の氏名は姓名のあいだを空白で区切るため、
    /// そこで折ると 1 人の名前が 2 行に割れて別人に見える。
    /// 区切りが無い長大な 1 語だけは例外的に文字単位で折る（それ以外に収める手段が無いため）。
    /// </summary>
    private static List<string> WrapFactValue(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        int start = 0;
        while (start < text.Length)
        {
            if (font.MeasureText(text.AsSpan(start), paint) <= maxWidth)
            {
                lines.Add(text[start..]);
                break;
            }

            // 収まる範囲でいちばん後ろの区切り位置を探す。
            int lastBreak = -1;
            for (int i = start; i < text.Length; i++)
            {
                if (font.MeasureText(text.AsSpan(start, i - start + 1), paint) > maxWidth) break;
                if (IsFactBreakPoint(text[i])) lastBreak = i;
            }

            if (lastBreak >= start)
            {
                lines.Add(text[start..(lastBreak + 1)]);
                start = lastBreak + 1;
                // 区切りの直後に続く空白は行頭に残さない。
                while (start < text.Length && text[start] == ' ') start++;
                continue;
            }

            // 区切りが無い（＝1 語が幅を超えている）。文字単位で折るしかない。
            int fit = start;
            while (fit < text.Length && font.MeasureText(text.AsSpan(start, fit - start + 1), paint) <= maxWidth) fit++;
            if (fit == start) fit = start + 1;
            lines.Add(text[start..fit]);
            start = fit;
        }
        return lines;
    }

    /// <summary>事実行を折ってよい文字（この文字の直後で改行する）。項目そのものの区切りだけを許す。</summary>
    private static bool IsFactBreakPoint(char c) => c is '、' or '，' or '・' or '／' or '/';

    /// <summary>
    /// 尺構成の帯グラフを描く。区画幅は秒数の比で決まるが、極端に短いパート（提供クレジット 15 秒など）が
    /// 消えないよう最小幅を保証し、その分を余裕のある区画から比例配分で差し引く。
    /// </summary>
    /// <param name="barHeight">帯の高さ。余白に応じて呼び出し側が縮める。</param>
    /// <param name="withCaption">尺の凡例と総尺を帯の下に描くか。余白が足りない場合は false で呼ばれる。</param>
    private void DrawFormatBar(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float x, float top, float width, float barHeight, bool withCaption)
    {
        var widths = ComputeSegmentWidths(spec.Bar, width);

        using var segmentFont = new SKFont(_bodyTypeface, 19f);
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
            using var captionFont = new SKFont(_bodyTypeface, 21f);
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

    /// <summary>
    /// フッタ（罫線 + サイト名 + 右メタ）。組み方によらず同じ位置に出す。
    /// サイト名だけはブランド書体（Kiwi Maru）で描く——サイトがヘッダ・フッタのワードマークにのみ
    /// この書体を使っている運用に合わせ、カードでも「ここだけ」に限定する。
    /// </summary>
    private void DrawFooter(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth)
    {
        paint.Color = Hairline;
        canvas.DrawRect(SKRect.Create(PaddingX, FooterLineY, contentWidth, 1.5f), paint);

        using (var brandFont = new SKFont(_brandTypeface, 30f))
        {
            paint.Color = Foreground;
            canvas.DrawText(_brandLabel, PaddingX, FooterTextBaseline, SKTextAlign.Left, brandFont, paint);
        }
        if (!string.IsNullOrWhiteSpace(spec.MetaRight))
        {
            using var metaFont = new SKFont(_bodyTypeface, 25f);
            paint.Color = Muted;
            canvas.DrawText(spec.MetaRight, CardWidth - PaddingX, FooterTextBaseline - 2f, SKTextAlign.Right, metaFont, paint);
        }
    }

    // ════════════════════════════════ ルビ付き見出し ════════════════════════════════

    /// <summary>
    /// ルビ組の 1 単位。<paramref name="Ruby"/> が空なら振り仮名を持たない素の文字。
    /// 折り返しは単位の境目でのみ起こすので、ルビ付きの文字が読みと切り離されることはない。
    /// </summary>
    private sealed record RubyUnit(string Base, string Ruby);

    /// <summary>
    /// <c>&lt;ruby&gt;漢&lt;rt&gt;かん&lt;/rt&gt;&lt;/ruby&gt;</c> 形式の HTML を組版単位へ分解する。
    /// 対象はサイトが出力するこの 1 形式だけなので、汎用 HTML パーサは持たずに正規表現で読む。
    /// ルビの無い地の文は 1 文字ずつの単位に割り、どこでも折り返せるようにする。
    /// </summary>
    private static List<RubyUnit> ParseRubyUnits(string html)
    {
        var units = new List<RubyUnit>();
        int pos = 0;

        void AddPlain(string text)
        {
            foreach (var ch in System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "")))
                units.Add(new RubyUnit(ch.ToString(), ""));
        }

        foreach (System.Text.RegularExpressions.Match m in RubyTagRegex.Matches(html))
        {
            if (m.Index > pos) AddPlain(html[pos..m.Index]);
            units.Add(new RubyUnit(
                System.Net.WebUtility.HtmlDecode(m.Groups[1].Value),
                System.Net.WebUtility.HtmlDecode(m.Groups[2].Value)));
            pos = m.Index + m.Length;
        }
        if (pos < html.Length) AddPlain(html[pos..]);

        return units;
    }

    /// <summary><c>&lt;ruby&gt;…&lt;rt&gt;…&lt;/rt&gt;&lt;/ruby&gt;</c> を拾う正規表現。</summary>
    private static readonly System.Text.RegularExpressions.Regex RubyTagRegex =
        new(@"<ruby>(.*?)<rt>(.*?)</rt></ruby>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

    /// <summary>ルビの文字サイズ比と、ルビが占める縦の余白比（いずれも見出しサイズに対する割合）。</summary>
    private const float RubySizeRatio = 0.38f;
    private const float RubyLeadingRatio = 1.15f;

    /// <summary>
    /// ルビ付き見出しを組んで描き、見出しブロックの下端 Y を返す。
    /// 単位ごとに「地の文の幅」と「振り仮名の幅」の広いほうを占有幅として確保し、
    /// 双方をその中央へ置く（振り仮名が地の文より長い漢字でも重ならない）。
    /// </summary>
    private float DrawRubyTitle(
        SKCanvas canvas, SKPaint paint, IReadOnlyList<RubyUnit> units,
        float x, float topY, float maxWidth, float[] sizeCandidates, int maxLines)
    {
        using var baseFont = new SKFont(_boldTypeface, sizeCandidates[^1]);
        using var rubyFont = new SKFont(_bodyTypeface, sizeCandidates[^1] * RubySizeRatio);

        List<List<RubyUnit>> lines = new();
        foreach (var size in sizeCandidates)
        {
            baseFont.Size = size;
            rubyFont.Size = size * RubySizeRatio;
            lines = WrapRubyUnits(units, baseFont, rubyFont, paint, maxWidth, maxLines + 1);
            if (lines.Count <= maxLines) break;
        }
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();

        float rubyLead = rubyFont.Size * RubyLeadingRatio;
        float lineHeight = baseFont.Size * TitleLineHeightRatio + rubyLead;
        float y = topY + rubyLead;

        foreach (var line in lines)
        {
            y += baseFont.Size;
            float cursor = x;
            foreach (var unit in line)
            {
                float baseWidth = baseFont.MeasureText(unit.Base, paint);
                float rubyWidth = unit.Ruby.Length == 0 ? 0f : rubyFont.MeasureText(unit.Ruby, paint);
                float slot = Math.Max(baseWidth, rubyWidth);

                paint.Color = Foreground;
                canvas.DrawText(unit.Base, cursor + (slot - baseWidth) / 2f, y, SKTextAlign.Left, baseFont, paint);

                if (unit.Ruby.Length > 0)
                {
                    paint.Color = Muted;
                    canvas.DrawText(unit.Ruby, cursor + (slot - rubyWidth) / 2f, y - baseFont.Size * 0.98f, SKTextAlign.Left, rubyFont, paint);
                }
                cursor += slot;
            }
            y += lineHeight - baseFont.Size;
        }

        return y - (lineHeight - baseFont.Size) + baseFont.Size * (TitleLineHeightRatio - 1f);
    }

    /// <summary>ルビ単位を行へ詰める。折り返し位置が行頭禁則に当たる場合は 1 単位前へ送る。</summary>
    private static List<List<RubyUnit>> WrapRubyUnits(
        IReadOnlyList<RubyUnit> units, SKFont baseFont, SKFont rubyFont, SKPaint paint, float maxWidth, int maxLines)
    {
        var lines = new List<List<RubyUnit>> { new() };
        float used = 0f;

        foreach (var unit in units)
        {
            float slot = Math.Max(
                baseFont.MeasureText(unit.Base, paint),
                unit.Ruby.Length == 0 ? 0f : rubyFont.MeasureText(unit.Ruby, paint));

            if (lines[^1].Count > 0 && used + slot > maxWidth)
            {
                if (lines.Count >= maxLines) return lines;

                // 送り出す単位が行頭禁則なら、直前の単位も一緒に次行へ回す。
                var carry = new List<RubyUnit>();
                if (unit.Ruby.Length == 0 && LineStartForbidden.Contains(unit.Base[0]) && lines[^1].Count > 1)
                {
                    carry.Add(lines[^1][^1]);
                    lines[^1].RemoveAt(lines[^1].Count - 1);
                }
                lines.Add(carry);
                used = carry.Sum(u => Math.Max(
                    baseFont.MeasureText(u.Base, paint),
                    u.Ruby.Length == 0 ? 0f : rubyFont.MeasureText(u.Ruby, paint)));
            }

            lines[^1].Add(unit);
            used += slot;
        }

        if (lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
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
    /// ワードマークにブランド書体で描けない文字が無いか検査する。見出し類は本文書体（Noto Sans JP）で
    /// 描くようになったため、収録範囲が狭いブランド書体を使うのはカード下部のサイト名だけになった。
    /// サイト名は設定値なので、変更時に豆腐が出ないよう検査は残す。同じ文字の重複報告は抑止する。
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
        _boldTypeface.Dispose();
    }
}
