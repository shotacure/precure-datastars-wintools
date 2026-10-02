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

    /// <summary>左端の色帯の幅。種別ごとの色（<see cref="OgCardSpec.BandColorHex"/>）で塗る。</summary>
    private const float BandWidth = 20f;

    /// <summary>左の内側余白（色帯の外側）と右の内側余白。縮小表示でも文字が欠けない程度に取りつつ、面積を使い切る。</summary>
    private const float PaddingLeft = BandWidth + 48f;
    private const float PaddingRight = 48f;

    /// <summary>
    /// X はカード画像の左下に、リンク先のドメイン名などを重ねて表示する。そこに文字を置くと読めなくなるので、
    /// 左下のこの範囲（幅 × 高さ）には何も描かない。サイト名と注記は右下に寄せる。
    /// </summary>
    private const float CaptionSafeWidth = 340f;
    private const float CaptionSafeHeight = 80f;

    /// <summary>本文が使える下端。これより下は右下のサイト名・注記と、左下の空けておく範囲。</summary>
    private const float FooterLineY = CardHeight - CaptionSafeHeight - 20f;

    /// <summary>右下のサイト名のベースラインと、その上に添える注記（基準点など）のベースライン。</summary>
    private const float FooterTextBaseline = CardHeight - 40f;
    private const float FooterNoteBaseline = CardHeight - 76f;

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
    private static readonly float[] TitleSizeCandidates = { 76f, 66f, 58f, 50f };

    /// <summary>高密度レイアウトの見出しサイズ候補（上下の要素に場所を譲るぶん小さめ）。</summary>
    private static readonly float[] DenseTitleSizeCandidates = { 60f, 52f, 46f, 40f };

    /// <summary>数も帯も持たない疎なカード（エピソード）の見出しサイズ候補。見出しが主役なので大きく。</summary>
    private static readonly float[] SparseTitleSizeCandidates = { 104f, 92f, 80f, 70f, 60f };

    /// <summary>疎なカードの事実行の拡大率の候補（大きい順に試す）。</summary>
    private static readonly float[] SparseFactScaleCandidates = { 1.7f, 1.5f, 1.3f, 1.15f, 1f, 0.9f, 0.8f, 0.7f, 0.6f };

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
    private const float HeadlineFontSize = 42f;

    /// <summary>
    /// 数の組（大きい数字＋小さい単位＋小さいラベル）の各サイズ。
    /// サイトの <c>.music-category-stats</c> と同じ「数を主役にして単位を添える」語彙に揃える。
    /// </summary>
    private const float StatValueFontSize = 48f;
    private const float StatUnitFontSize = 26f;
    private const float StatLabelFontSize = 24f;
    private const float StatGap = 38f;

    /// <summary>数の組が 1 行に収まらないときの行送り。</summary>
    private const float StatLineHeight = 64f;

    /// <summary>数のほかに見せるものが無いカードで、数の字を何倍にするか。</summary>
    private const float StatsOnlyScale = 1.7f;

    /// <summary>ヒーロー調（ホーム）の数の拡大率。6 つの数を 2 行に収める。</summary>
    private const float HeroStatsScale = 1.2f;

    /// <summary>ファクト行の文字サイズ・行送り・最大行数。超過分は末尾から捨てる。</summary>
    private const float FactFontSize = 27f;
    private const float FactLineHeight = 38f;
    private const int FactMaxLines = 3;

    /// <summary>1 行 1 項目で積むファクトの続き行を、値の左端からさらに落とす量。</summary>
    private const float StackedContinuationIndent = 26f;

    /// <summary>
    /// 標準レイアウトの説明文の文字サイズと行送り。
    /// フッタまでの余白に何行入るかを実測して折り返すため、行送りは固定値で持つ。
    /// </summary>
    private const float DescriptionFontSize = 40f;

    /// <summary>数だけのカード（索引・統計）に添えるリード文の字の大きさ（候補の先頭が標準）。</summary>
    private const float NumbersOnlyLeadFontSize = 40f;
    private static readonly float[] NumbersOnlyLeadSizeCandidates = { 40f, 36f, 32f, 28f };
    private const float DescriptionLineHeight = 62f;

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

    /// <summary>注意書き系のページの本文の書体（石井ゴシックなど）。機械的な斜体で組む。無ければ本文の書体。</summary>
    private readonly SKTypeface _noticeTypeface;

    /// <summary>機械的な斜体の傾き（<see cref="SKFont.SkewX"/>。負で右に倒れる）。設定の角度（度）から正接で求める。</summary>
    private readonly float _obliqueSkew;

    /// <summary>ブランド書体（Kiwi Maru）。カード下部のワードマークにのみ使う。</summary>
    private readonly SKTypeface _brandTypeface;
    /// <summary>本文書体（Noto Sans JP Regular）。</summary>
    private readonly SKTypeface _bodyTypeface;
    /// <summary>見出し書体（Noto Sans JP Bold）。サイトの h1・h2 と同じ太さ。設定で商用書体に差し替えられる。</summary>
    private readonly SKTypeface _boldTypeface;
    /// <summary>本文の強調部（役職名などのラベル、前置きの作品名）の書体。既定は見出しと同じ。</summary>
    private readonly SKTypeface _emphasisTypeface;
    /// <summary>大きいピンクの数字の書体（既定は見出しと同じ）。</summary>
    private readonly SKTypeface _numberTypeface;
    /// <summary>右上の透かしの書体（既定は見出しと同じ）。</summary>
    private readonly SKTypeface _watermarkTypeface;
    /// <summary>書体ごとの組版器（HarfBuzz）。キーは SKTypeface のネイティブハンドル。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr, OgTextShaper> _shapers = new();
    /// <summary>書体名（<see cref="OgCardSpec.TitleFontFamily"/>）→ インストール済み書体。見つからなければ null を控える。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SKTypeface?> _familyTypefaces = new(StringComparer.Ordinal);
    private readonly object _familyLock = new();
    /// <summary>読み込んだ書体のうち Dispose すべきもの（同じ書体を複数の用途で共有するため重複を持たない）。</summary>
    private readonly List<SKTypeface> _ownedTypefaces = new();

    /// <summary>
    /// 書体（ハンドル）→ そのコンデンス版を広い順に並べたもの。見出しが 1 行に収まらないとき、長体の代わりに差し替える。
    /// 今は見出し書体（<see cref="_boldTypeface"/>）にだけ持たせる。
    /// </summary>
    private readonly Dictionary<IntPtr, IReadOnlyList<SKTypeface>> _condensedByHandle = new();

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
    /// 書体を読み込んでレンダラを構築する。設定（<paramref name="fonts"/>）で指定された書体はそのファイルを、
    /// 空のものは同梱フォントを使う。
    /// </summary>
    /// <param name="brandLabel">カード下部に出すサイト名（可視ブランド表記）。</param>
    /// <param name="fonts">見出し・本文・数字・透かしの書体ファイル。</param>
    /// <exception cref="FileNotFoundException">フォントが見つからない場合。</exception>
    public OgCardRenderer(string brandLabel, OgCardFontPaths fonts)
    {
        _brandLabel = brandLabel;

        var fontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
        _brandTypeface = Own(LoadTypeface(Path.Combine(fontDir, "KiwiMaru-Medium.ttf")));
        // Noto Sans JP は可変フォントで、wght 軸の既定値が最小の 100（Thin）になっている。
        // 素直に読み込むと本文が Thin で焼かれ、サイト本文とは別書体に見えるほど細くなるため、
        // ウェイトを明示して本文用（400）と見出し用（700）の 2 つを作る。
        var notoPath = Path.Combine(fontDir, "NotoSansJP.ttf");
        _bodyTypeface = Own(fonts.Body.Length > 0 ? LoadTypeface(fonts.Body) : LoadTypeface(notoPath, BodyFontWeight));
        _boldTypeface = Own(fonts.Title.Length > 0 ? LoadTypeface(fonts.Title) : LoadTypeface(notoPath, HeadingFontWeight));
        _emphasisTypeface = fonts.Emphasis.Length > 0 ? Own(LoadTypeface(fonts.Emphasis)) : _boldTypeface;
        _numberTypeface = fonts.Number.Length > 0 ? Own(LoadTypeface(fonts.Number)) : _boldTypeface;
        _watermarkTypeface = fonts.Watermark.Length > 0 ? Own(LoadTypeface(fonts.Watermark)) : _boldTypeface;
        _noticeTypeface = fonts.Notice.Length > 0 ? Own(LoadTypeface(fonts.Notice)) : _bodyTypeface;
        _obliqueSkew = -(float)Math.Tan(fonts.ObliqueDegrees * Math.PI / 180.0);

        // 見出し書体のコンデンス版。字幅の比は実測して広い順に並べる（設定の順序に頼らない）。
        var condensed = new List<(float Ratio, SKTypeface Typeface)>();
        foreach (var path in fonts.TitleCondensedPaths)
        {
            var typeface = Own(LoadTypeface(path));
            condensed.Add((MeasureWidthRatio(typeface, _boldTypeface), typeface));
        }
        if (condensed.Count > 0)
            _condensedByHandle[_boldTypeface.Handle] = condensed.OrderByDescending(c => c.Ratio).Select(c => c.Typeface).ToList();

        foreach (var typeface in _ownedTypefaces)
            _shapers[typeface.Handle] = new OgTextShaper(typeface);
        // ワードマークはサイトのヘッダと同じ見た目にする。Kiwi Maru はかなを全角のまま組む書体なので、
        // 字面詰めはかけず、ブラウザと同じく OpenType 機能（欧文の kern）だけを効かせる。
        _shapers[_brandTypeface.Handle] = new OgTextShaper(_brandTypeface, opticalTightening: false);
    }

    /// <summary>読み込んだ書体を Dispose 対象として控える。</summary>
    private SKTypeface Own(SKTypeface typeface)
    {
        _ownedTypefaces.Add(typeface);
        return typeface;
    }

    /// <summary>基準の書体に対する字幅の比（かな・漢字・欧文を含む見本の幅で測る）。コンデンス版の並べ替えに使う。</summary>
    private static float MeasureWidthRatio(SKTypeface typeface, SKTypeface baseTypeface)
    {
        const string probe = "あいう漢字ABC";
        using var font = new SKFont(typeface, 100f);
        using var baseFont = new SKFont(baseTypeface, 100f);
        float baseWidth = baseFont.MeasureText(probe);
        return baseWidth > 0f ? font.MeasureText(probe) / baseWidth : 1f;
    }

    // ──────── 文字の計測と描画（HarfBuzz で字詰めを効かせる） ────────

    /// <summary>フォントの書体に対応する組版器。SKFont.Typeface は同じネイティブ書体を指すラッパを返すので、ハンドルで引く。</summary>
    private OgTextShaper ShaperFor(SKFont font)
    {
        var typeface = font.Typeface ?? throw new InvalidOperationException("書体の無いフォントです。");
        return _shapers.TryGetValue(typeface.Handle, out var shaper)
            ? shaper
            : throw new InvalidOperationException("レンダラが読み込んでいない書体で描こうとしました。");
    }

    /// <summary>字詰めを効かせた文字列の幅。</summary>
    private float Measure(SKFont font, string text) => ShaperFor(font).Measure(text, font);

    private float Measure(SKFont font, ReadOnlySpan<char> text) => Measure(font, text.ToString());

    /// <summary>字詰めを効かせて文字列を描く。引数の並びは SkiaSharp の DrawText と同じ。</summary>
    private void DrawText(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint)
        => ShaperFor(font).Draw(canvas, text, x, baseline, align, font, paint);

    /// <summary>
    /// 字詰めを効かせて、白フチ付きで文字列を描く。フチは細め（字の大きさの <see cref="OutlineWidthRatio"/>）で角は丸める。
    /// </summary>
    private void DrawTextOutlined(SKCanvas canvas, string text, float x, float baseline, SKTextAlign align, SKFont font, SKPaint paint)
    {
        using var outline = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            // 線は輪郭を中心に引かれるので、見せたいフチの太さの 2 倍にする。
            StrokeWidth = font.Size * OutlineWidthRatio * 2f,
            StrokeJoin = SKStrokeJoin.Round,
            StrokeCap = SKStrokeCap.Round
        };
        ShaperFor(font).Draw(canvas, text, x, baseline, align, font, paint, outline);
    }

    /// <summary>白フチの見える太さ（字の大きさに対する比）。細めにして字形を崩さない。</summary>
    private const float OutlineWidthRatio = 0.045f;

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
        DrawBand(canvas, spec);
        float watermarkBottom = DrawWatermark(canvas, spec);
        var titleTypeface = ResolveTypeface(spec.TitleFontFamily, _boldTypeface, out string? fontWarning);
        // タグラインの書体は描くときに同じ辞書から引く。見つからないときの警告だけここで拾う。
        ResolveTypeface(spec.StatementFontFamily, _watermarkTypeface, out string? statementWarning);
        fontWarning ??= statementWarning;

        using var paint = new SKPaint { IsAntialias = true };
        float contentWidth = CardWidth - PaddingLeft - PaddingRight;

        if (spec.IsProfile)
        {
            DrawProfileBody(canvas, paint, spec, contentWidth, watermarkBottom);
        }
        else if (spec.IsDense)
        {
            // ヒーロー調のカードは見出しそのものがワードマークなので、フッタに同じ名前を重ねない。
            // フッタが無い分だけ下に空きができるので、一度測ってから中身をカードの上下中央へ据える
            // （上詰めのままだと下半分がまるごと空いてしまう）。
            // フッタを持たないヒーロー調でも、下端は他のカードと同じ高さで止める（左下は X がドメイン名を重ねる場所）。
            // 数だけのカード（索引・統計）も要素が少ないので、同じく上下中央に据える。
            float floor = FooterLineY - FooterClearance;
            float offset = 0f;
            if (spec.HeroVoice || spec.IsNumbersOnly)
            {
                using var recorder = new SKPictureRecorder();
                var probe = recorder.BeginRecording(SKRect.Create(CardWidth, CardHeight));
                float bottom = DrawDenseBody(probe, paint, spec, contentWidth, watermarkBottom, titleTypeface);
                recorder.EndRecording().Dispose();
                offset = Math.Max(0f, (floor - bottom) / 2f);
            }

            canvas.Save();
            canvas.Translate(0f, offset);
            DrawDenseBody(canvas, paint, spec, contentWidth, watermarkBottom, titleTypeface);
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

        return fontWarning ?? FindMissingBrandGlyphs(_brandLabel);
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
            DrawText(canvas, Ellipsize(spec.Kicker, kickerFont, paint, contentWidth), PaddingLeft, y, SKTextAlign.Left, kickerFont, paint);
            y += 62f;
        }

        // ── 見出し＋ピンク罫 ──
        using var titleFont = new SKFont(_boldTypeface, TitleSizeCandidates[^1]);
        var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, TitleSizeCandidates, TitleMaxLines);

        paint.Color = Foreground;
        foreach (var line in titleLines)
        {
            y += titleFont.Size;
            DrawTextOutlined(canvas, line, PaddingLeft, y, SKTextAlign.Left, titleFont, paint);
            y += titleFont.Size * (TitleLineHeightRatio - 1f);
        }
        y = DrawTitleRule(canvas, paint, y, contentWidth);

        // ── 説明文（フッタ罫線までの余白に収まる行数だけ折り返す） ──
        // 注意書き系のページの本文なので、注意書きの書体（石井ゴシックなど）を機械的な斜体で組む。
        // 斜体は右へ張り出すぶん（字の高さ × 傾き）だけ行幅を控える。
        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            using var descFont = new SKFont(_noticeTypeface, DescriptionFontSize) { SkewX = _obliqueSkew };
            paint.Color = Foreground;
            float descWidth = contentWidth - DescriptionFontSize * Math.Abs(_obliqueSkew);

            y += 20f;
            float available = (FooterLineY - FooterClearance) - y;
            int maxLines = Math.Max(0, (int)(available / DescriptionLineHeight));
            if (maxLines > 0)
            {
                var lines = WrapText(spec.Subtitle, descFont, paint, descWidth, maxLines + 1);
                if (lines.Count > maxLines)
                {
                    lines = lines.Take(maxLines).ToList();
                    lines[^1] = Ellipsize(lines[^1] + "…", descFont, paint, descWidth);
                }
                foreach (var line in lines)
                {
                    y += DescriptionFontSize;
                    DrawText(canvas, line, PaddingLeft, y, SKTextAlign.Left, descFont, paint);
                    y += DescriptionLineHeight - DescriptionFontSize;
                }
            }
        }

    }

    // ════════════════════════════════ 高密度レイアウト ════════════════════════════════

    /// <summary>
    /// 識別子・数・帯グラフ・事実行を積む高密度の組み方。
    /// 上端からは「所属 → 識別子 → 見出し＋ピンク罫 → 数」を順に降ろし、事実行はフッタ罫線の直上から
    /// 上へ積む。帯グラフは残った余白へ入れるため、見出しが 1 行でも 2 行でも全体の重心が崩れない。
    /// </summary>
    /// <returns>描いた中身の下端 Y。上下中央に据え直すときの高さ計算に使う。</returns>
    /// <summary>高密度の組みで試す（数の拡大率, 流し込みの事実行の拡大率）の組。大きい順。</summary>
    private static readonly (float Stat, float Fact)[] DenseScaleCandidates =
    {
        (1.7f, 1.5f), (1.5f, 1.3f), (1.3f, 1.15f), (1.15f, 1f), (1f, 1f)
    };

    private float DrawDenseBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth, float watermarkBottom, SKTypeface titleTypeface)
    {
        // 数も帯も持たないカード（エピソードなど）は見出しと事実行だけなので、見出しを大きく組み、
        // 右上の透かしに重ならない高さから始める。
        // ヒーロー調（ホーム）は数を持たなくても疎の組みには回さない（前置き・見出し・特徴の言葉の順で組む）。
        bool sparse = !spec.HeroVoice && spec.Badges.Count == 0 && spec.Bar.Count == 0;
        if (sparse) return DrawSparseBody(canvas, paint, spec, contentWidth, watermarkBottom, titleTypeface);

        // ヒーロー調と帯グラフを持つカードは拡大しない（ヒーロー調は言葉が主役、帯は余白を自分で使う）。
        // それ以外（商品・書籍・人物など）は、数と事実行の拡大率を大きい順に試し、下端に収まり事実行が切れない
        // 最初の組で描く（上に寄って下半分が空いたままにならないように）。
        if (spec.HeroVoice || spec.Bar.Count > 0)
            return DrawDenseBodyAt(canvas, paint, spec, contentWidth, watermarkBottom, titleTypeface, 1f, 1f, out _);

        float floor = FooterLineY - FooterClearance;
        foreach (var (statScale, factScale) in DenseScaleCandidates)
        {
            using var recorder = new SKPictureRecorder();
            var probe = recorder.BeginRecording(SKRect.Create(CardWidth, CardHeight));
            float bottom = DrawDenseBodyAt(probe, paint, spec, contentWidth, watermarkBottom, titleTypeface, statScale, factScale, out bool truncated);
            recorder.EndRecording().Dispose();
            if (bottom > floor || truncated) continue;
            return DrawDenseBodyAt(canvas, paint, spec, contentWidth, watermarkBottom, titleTypeface, statScale, factScale, out _);
        }
        return DrawDenseBodyAt(canvas, paint, spec, contentWidth, watermarkBottom, titleTypeface, 1f, 1f, out _);
    }

    /// <summary>
    /// 高密度の組みを、指定の拡大率で描く。<paramref name="statScaleHint"/> は数の拡大率
    /// （数だけのカードとヒーロー調は固定の率を使うので無視する）、<paramref name="factScale"/> は流し込みの事実行の拡大率。
    /// <paramref name="truncated"/> は事実行が高さに入り切らず末尾を落としたとき true。
    /// </summary>
    private float DrawDenseBodyAt(
        SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth, float watermarkBottom, SKTypeface titleTypeface,
        float statScaleHint, float factScale, out bool truncated)
    {
        truncated = false;
        // ── 最上段（左：所属シリーズ・種別 ／ 右：放送日時などの補助） ──
        const float kickerBaseline = 72f;
        bool hasKicker = !string.IsNullOrWhiteSpace(spec.Kicker) || !string.IsNullOrWhiteSpace(spec.KickerRight);
        if (hasKicker)
        {
            // 左（所属シリーズ）はカードの主語なので太字で大きく、右（放送日）は補助なので小さく薄く。
            // ヒーロー調（ホーム）の前置きは肩書き（「プリキュアデータベース」）なので、ヘッダと同じくブランド書体の濃ピンクで添える。
            using var kickerFont = new SKFont(spec.HeroVoice ? _brandTypeface : _emphasisTypeface, 31f);
            using var kickerRightFont = new SKFont(_bodyTypeface, 25f);
            float rightWidth = 0f;
            if (!string.IsNullOrWhiteSpace(spec.KickerRight))
            {
                paint.Color = Muted;
                rightWidth = Measure(kickerRightFont, spec.KickerRight);
                DrawText(canvas, spec.KickerRight, CardWidth - PaddingRight, kickerBaseline, SKTextAlign.Right, kickerRightFont, paint);
            }
            if (!string.IsNullOrWhiteSpace(spec.Kicker))
            {
                paint.Color = spec.HeroVoice ? HeroLeadColor : Foreground;
                float room = contentWidth - (rightWidth > 0f ? rightWidth + 24f : 0f);
                DrawText(canvas, Ellipsize(spec.Kicker, kickerFont, paint, room), PaddingLeft, kickerBaseline, SKTextAlign.Left, kickerFont, paint);
            }
        }

        // 前置きを持たないカードはその行ぶんの空きを残さず、見出しを最上段へ繰り上げる
        // （空の 1 行を空けたままにすると、見出しが宙に浮いて見える）。
        float y = hasKicker ? kickerBaseline : kickerBaseline - 34f;
        // 右上の透かしがあれば、見出しはその下から始める（透かしと見出しを重ねない）。
        if (watermarkBottom > 0f) y = Math.Max(y, watermarkBottom - 36f);

        // ── 識別子（第N話） ──
        if (!string.IsNullOrWhiteSpace(spec.Headline))
        {
            using var headlineFont = new SKFont(_boldTypeface, HeadlineFontSize);
            y += 54f;
            paint.Color = Muted;
            DrawText(canvas, Ellipsize(spec.Headline, headlineFont, paint, contentWidth), PaddingLeft, y, SKTextAlign.Left, headlineFont, paint);
        }

        // ── 見出し＋ピンク罫 ──
        // ルビ付き HTML があれば振り仮名つきで組む（サイト本体のサブタイトル表示と同じ読みを添える）。
        y += 22f;
        var rubyUnits = string.IsNullOrWhiteSpace(spec.TitleRubyHtml)
            ? new List<RubyUnit>()
            : ParseRubyUnits(spec.TitleRubyHtml);

        // ヒーロー調の見出しはワードマークそのものなので、標準の組みと同じ大きさまで許す。
        var titleSizes = spec.HeroVoice ? TitleSizeCandidates : DenseTitleSizeCandidates;
        if (rubyUnits.Count > 0)
        {
            y = DrawRubyTitle(canvas, paint, rubyUnits, PaddingLeft, y, contentWidth, titleSizes, DenseTitleMaxLines, titleTypeface);
        }
        else
        {
            using var titleFont = new SKFont(spec.HeroVoice ? _brandTypeface : titleTypeface, titleSizes[^1]);
            var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, titleSizes, DenseTitleMaxLines);
            paint.Color = spec.HeroVoice ? HeroTitleColor : Foreground;
            foreach (var line in titleLines)
            {
                y += titleFont.Size;
                DrawTextOutlined(canvas, line, PaddingLeft, y, SKTextAlign.Left, titleFont, paint);
                y += titleFont.Size * (TitleLineHeightRatio - 1f);
            }
        }
        y = DrawTitleRule(canvas, paint, y, contentWidth);

        // ── タグライン（罫のすぐ下） ──
        // サイトのヒーローが h1 → 罫 → lead の順で組んでいるのに合わせる。
        // 数だけのカードの説明文（リード文）は数の下に置くので、ここでは描かない。
        if (!string.IsNullOrWhiteSpace(spec.Subtitle) && !spec.IsNumbersOnly)
        {
            using var leadFont = new SKFont(spec.HeroVoice ? _brandTypeface : _bodyTypeface, 26f);
            paint.Color = spec.HeroVoice ? HeroLeadColor : Muted;
            // ヒーロー調は要素数が少なくフッタも持たないぶん、行間を広く取って落ち着かせる。
            y += (spec.HeroVoice ? 34f : 20f) + leadFont.Size;
            DrawText(canvas, Ellipsize(spec.Subtitle, leadFont, paint, contentWidth), PaddingLeft, y, SKTextAlign.Left, leadFont, paint);
        }

        // ── 特徴の言葉（ヒーロー調。数の代わりに、サイトの特徴を透かしの書体の斜体で大きく） ──
        if (!string.IsNullOrWhiteSpace(spec.Statement))
            y = DrawStatement(canvas, paint, spec.Statement, ResolveTypeface(spec.StatementFontFamily, _watermarkTypeface, out _), y, contentWidth, FooterLineY - FooterClearance);

        // ── 数（大きいピンクの数字＋小さい単位） ──
        // 数のほかに見せるものが無いカード（索引・ランディング）は、数そのものが主役になる。
        // 通常の大きさのまま置くと面の大半が空いて間延びするので、字を大きくして紙面を持たせる。
        // 数の下に続きがあるカード（詳細ページなど）は、続きを圧迫しないよう等倍のままにする。
        bool badgesOnly = spec.Facts.Count == 0 && spec.InlineFacts.Count == 0 && spec.Bar.Count == 0;
        // ヒーロー調（ホーム）は数が 6 つあるので、数だけのカードより一段控えて 2 行に収める。
        float statScale = spec.HeroVoice ? HeroStatsScale : badgesOnly ? StatsOnlyScale : statScaleHint;
        if (spec.Badges.Count > 0)
            y = DrawStats(canvas, paint, spec.Badges, PaddingLeft, y + (spec.HeroVoice ? 40f : 16f), contentWidth, statScale);

        // ── リード文（数だけのカード。数の下の空きに、ページの説明文を注意書きの書体の斜体で 3 行まで） ──
        if (spec.IsNumbersOnly && !string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            // 字の大きさは、3 行までで下端（右下の注記の上）に収まる最大のものを候補から選ぶ。
            using var leadFont = new SKFont(_noticeTypeface, NumbersOnlyLeadFontSize) { SkewX = _obliqueSkew };
            paint.Color = Foreground;
            float leadFloor = FooterLineY - FooterClearance;
            var lines = new List<string>();
            float room = contentWidth;
            foreach (float size in NumbersOnlyLeadSizeCandidates)
            {
                leadFont.Size = size;
                room = contentWidth - size * Math.Abs(_obliqueSkew);
                lines = WrapText(spec.Subtitle, leadFont, paint, room, 4);
                float bottom = y + 28f + lines.Count * size * 1.35f - size * 0.35f;
                if (lines.Count <= 3 && bottom <= leadFloor) break;
            }
            if (lines.Count > 3)
            {
                lines = lines.Take(3).ToList();
                lines[^1] = Ellipsize(lines[^1] + "…", leadFont, paint, room);
            }
            y += 28f;
            foreach (var line in lines)
            {
                y += leadFont.Size;
                DrawText(canvas, line, PaddingLeft, y, SKTextAlign.Left, leadFont, paint);
                y += leadFont.Size * 0.35f;
            }
            y -= leadFont.Size * 0.35f;
        }

        // 基準点などの注記（MetaLeft）は右下のサイト名の上に置く（DrawFooter）。

        // ── 事実行 ──
        // 帯グラフを持つカードは、帯を置く余白を空けるためフッタ罫線の直上へ下寄せする。
        // 帯を持たないカードで下寄せすると上の要素とのあいだが大きく空いて間延びするため、
        // その場合は直前の要素の下へ続けて置く。
        bool hasBar = spec.Bar.Count > 0;
        float factLineHeight = FactLineHeight * factScale;
        float factsAnchor = hasBar ? FooterLineY - FooterClearance : y + 46f * factScale;
        // 帯が無いカードはフッタまでの空き高さから入る行数を決める。行数を固定にすると、
        // 項目が多いカード（シリーズの主要スタッフなど）で余白があるのに末尾が落ちてしまう。
        int factsMaxLines = hasBar
            ? InlineFactMaxLines
            : Math.Max(1, (int)(((FooterLineY - FooterClearance) - factsAnchor) / factLineHeight) + 1);
        if (hasBar) factsMaxLines = Math.Min(factsMaxLines, FactMaxLines);

        // 2 種のファクトは排他ではない。楽曲カードのように「作り手を 1 行へ流し込み、その下に
        // 版と歌い手を 1 行 1 項目で積む」構成があるため、両方あるときは上から順に置く。
        float factsTop = factsAnchor;
        float flowY = factsAnchor;
        float contentBottom = y;
        if (spec.InlineFacts.Count > 0)
        {
            var r = DrawInlineFacts(canvas, paint, spec.InlineFacts, PaddingLeft, flowY, contentWidth, anchorToTop: !hasBar, maxLines: factsMaxLines, scale: factScale);
            truncated |= r.Truncated;
            factsTop = r.Top;
            flowY = r.Bottom + factLineHeight + 8f;
            contentBottom = r.Bottom;
        }
        if (spec.Facts.Count > 0)
        {
            // 併記のときは必ず下段なので、上から積む（帯を持つカードで両方を使う想定は無い）。
            bool stackTop = !hasBar || spec.InlineFacts.Count > 0;
            // 行数枠は開始位置から数え直す。上段（InlineFacts）が使った高さを差し引かないと、
            // 併記のカードで下段がフッタ線を越えてサイト名に重なる。
            float stackedAnchor = stackTop ? flowY : factsAnchor;
            int stackedMaxLines = hasBar
                ? factsMaxLines
                : Math.Max(1, (int)(((FooterLineY - FooterClearance) - stackedAnchor) / FactLineHeight) + 1);
            var r = DrawStackedFacts(canvas, paint, spec.Facts, PaddingLeft, stackedAnchor, anchorToTop: stackTop, maxLines: stackedMaxLines);
            truncated |= r.Truncated;
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
            DrawFormatBar(canvas, paint, spec, PaddingLeft, barTop, contentWidth, barHeight, hasCaption);
        }

        return contentBottom;
    }

    /// <summary>
    /// 数も帯も持たない疎なカード（エピソード・楽曲など）の組み方。
    /// 「前置き → 見出し＋ピンク罫 → 補助行（放送日など）→ 流し込みの事実行 → 1 行 1 項目の事実行」を
    /// 上から置く。見出しを主役にして大きく組むぶん、下端（右下の注記の上）に収まらないことがあるので、
    /// 見出しの大きさと事実行の拡大率を候補の大きい順に試し、全部が収まる最初の組み合わせで描く。
    /// どれでも収まらなければ最小の組み合わせで描き、溢れた分は末尾から落ちる。
    /// 右上に透かしがあれば、その下から始める（透かしは薄いが、見出しが重なると読みにくい）。
    /// </summary>
    private float DrawSparseBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth, float watermarkBottom, SKTypeface titleTypeface)
    {
        const float kickerBaseline = 72f;
        bool hasKicker = !string.IsNullOrWhiteSpace(spec.Kicker) || !string.IsNullOrWhiteSpace(spec.KickerRight);
        if (hasKicker)
        {
            using var kickerFont = new SKFont(_emphasisTypeface, 31f);
            using var kickerRightFont = new SKFont(_bodyTypeface, 25f);
            float rightWidth = 0f;
            if (!string.IsNullOrWhiteSpace(spec.KickerRight))
            {
                paint.Color = Muted;
                rightWidth = Measure(kickerRightFont, spec.KickerRight);
                DrawText(canvas, spec.KickerRight, CardWidth - PaddingRight, kickerBaseline, SKTextAlign.Right, kickerRightFont, paint);
            }
            if (!string.IsNullOrWhiteSpace(spec.Kicker))
            {
                paint.Color = Foreground;
                float room = contentWidth - (rightWidth > 0f ? rightWidth + 24f : 0f);
                DrawText(canvas, Ellipsize(spec.Kicker, kickerFont, paint, room), PaddingLeft, kickerBaseline, SKTextAlign.Left, kickerFont, paint);
            }
        }

        float startY = hasKicker ? kickerBaseline : kickerBaseline - 34f;
        if (watermarkBottom > 0f) startY = Math.Max(startY, watermarkBottom - 36f);
        float floor = FooterLineY - FooterClearance;

        var rubyUnits = string.IsNullOrWhiteSpace(spec.TitleRubyHtml)
            ? new List<RubyUnit>()
            : ParseRubyUnits(spec.TitleRubyHtml);

        // 見出しの大きさと事実行の拡大率の組を、大きい順に試す。見出しの段 i に対して事実行は i+1 段目までを先に試し
        // （見出しだけ巨大で事実行が小さい組を避ける）、それで入らなければ見出しの段ごとに事実行を無制限に下げる
        // （長い曲名で見出しが小さくなっても、歌い手の字まで道連れに小さくしない）。事実行は切らずに全部載せる。
        static IEnumerable<(float TitleSize, float FactScale)> Candidates()
        {
            var t = SparseTitleSizeCandidates;
            var f = SparseFactScaleCandidates;
            for (int i = 0; i < t.Length; i++)
                for (int j = 0; j <= Math.Min(i + 1, f.Length - 1); j++) yield return (t[i], f[j]);
            for (int i = 0; i < t.Length; i++)
                for (int j = 0; j < f.Length; j++) yield return (t[i], f[j]);
        }
        foreach (var (titleSize, factScale) in Candidates())
        {
            using var recorder = new SKPictureRecorder();
            var probe = recorder.BeginRecording(SKRect.Create(CardWidth, CardHeight));
            float bottom = DrawSparseContent(probe, paint, spec, contentWidth, startY, titleSize, factScale, rubyUnits, titleTypeface, out bool truncated);
            recorder.EndRecording().Dispose();
            if (bottom > floor || truncated) continue;

            // 余った高さの半分だけ下げて、上下の空きを揃える。事実行（スタッフなど）があるカードは下端の注記に
            // 寄り過ぎないよう下げ幅に上限を置き、見出しと日付だけのカード（クレジット未収録の話など）は中央まで寄せる。
            bool hasFacts = spec.InlineFacts.Count > 0 || spec.Facts.Count > 0;
            float offset = Math.Min(hasFacts ? 48f : 160f, Math.Max(0f, (floor - bottom) / 2f));
            canvas.Save();
            canvas.Translate(0f, offset);
            float drawn = DrawSparseContent(canvas, paint, spec, contentWidth, startY, titleSize, factScale, rubyUnits, titleTypeface, out _);
            canvas.Restore();
            return drawn + offset;
        }
        return DrawSparseContent(canvas, paint, spec, contentWidth, startY, SparseTitleSizeCandidates[^1], SparseFactScaleCandidates[^1], rubyUnits, titleTypeface, out _);
    }

    /// <summary>
    /// 疎なカードの中身を、指定した見出しサイズと事実行の拡大率で描き、下端 Y を返す。
    /// <paramref name="truncated"/> は見出しか事実行が規定行数に収まらず末尾を落としたとき true。
    /// </summary>
    private float DrawSparseContent(
        SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth, float y,
        float titleSize, float factScale, List<RubyUnit> rubyUnits, SKTypeface titleTypeface, out bool truncated)
    {
        truncated = false;
        float floor = FooterLineY - FooterClearance;
        var sizes = new[] { titleSize };

        y += 22f;
        if (rubyUnits.Count > 0)
        {
            // 詰めた結果の書体（コンデンス版に差し替わることがある）と長体の率を、描画側へ渡す。
            SKTypeface fittedTypeface = titleTypeface;
            float scaleX = 1f;
            using (var baseFont = new SKFont(titleTypeface, titleSize))
            using (var rubyFont = new SKFont(_bodyTypeface, titleSize * RubySizeRatio))
            {
                // 振り仮名も地の文と同じ率で詰める（振り仮名だけ等幅のままだと、振り仮名のほうが広い字で地の文に空きができる）。
                CondenseToFit(baseFont, () =>
                {
                    rubyFont.ScaleX = baseFont.ScaleX;
                    return WrapRubyUnits(rubyUnits, baseFont, rubyFont, paint, contentWidth, 2).Count <= 1;
                });
                fittedTypeface = baseFont.Typeface ?? titleTypeface;
                scaleX = baseFont.ScaleX;
                rubyFont.ScaleX = scaleX;
                if (WrapRubyUnits(rubyUnits, baseFont, rubyFont, paint, contentWidth, DenseTitleMaxLines + 1).Count > DenseTitleMaxLines)
                    truncated = true;
            }
            y = DrawRubyTitle(canvas, paint, rubyUnits, PaddingLeft, y, contentWidth, sizes, DenseTitleMaxLines, fittedTypeface, scaleX);
        }
        else
        {
            using var titleFont = new SKFont(titleTypeface, titleSize);
            CondenseToFit(titleFont, () => WrapText(spec.Title, titleFont, paint, contentWidth, 2).Count <= 1);
            if (WrapText(spec.Title, titleFont, paint, contentWidth, DenseTitleMaxLines + 1).Count > DenseTitleMaxLines)
                truncated = true;
            var titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, sizes, DenseTitleMaxLines);
            paint.Color = Foreground;
            foreach (var line in titleLines)
            {
                y += titleFont.Size;
                DrawTextOutlined(canvas, line, PaddingLeft, y, SKTextAlign.Left, titleFont, paint);
                y += titleFont.Size * (TitleLineHeightRatio - 1f);
            }
        }
        y = DrawTitleRule(canvas, paint, y, contentWidth);

        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            // 補助行（放送日など）は事実行と同じ拡大率で、本文色より薄く。
            using var leadFont = new SKFont(_bodyTypeface, 26f * factScale);
            paint.Color = Muted;
            y += 22f + leadFont.Size;
            DrawText(canvas, Ellipsize(spec.Subtitle, leadFont, paint, contentWidth), PaddingLeft, y, SKTextAlign.Left, leadFont, paint);
        }

        float bottom = y;
        if (spec.InlineFacts.Count > 0)
        {
            float lineHeight = FactLineHeight * factScale;
            float first = y + 30f * factScale + FactFontSize * factScale;
            int maxLines = Math.Max(1, (int)((floor - first) / lineHeight) + 1);
            // 行数は高さに入るだけ（歌い手が多いキャラクターソングも、字を小さくしてでも全員載せる）。
            var r = DrawInlineFacts(canvas, paint, spec.InlineFacts, PaddingLeft, first, contentWidth, anchorToTop: true, maxLines: maxLines, scale: factScale);
            truncated |= r.Truncated;
            bottom = r.Bottom;
            y = r.Bottom + lineHeight * 0.4f;
        }
        if (spec.Facts.Count > 0)
        {
            float first = y + 30f + FactFontSize;
            int maxLines = Math.Max(1, (int)((floor - first) / FactLineHeight) + 1);
            var r = DrawStackedFacts(canvas, paint, spec.Facts, PaddingLeft, first, anchorToTop: true, maxLines: maxLines);
            truncated |= r.Truncated;
            bottom = Math.Max(bottom, r.Bottom);
        }
        return bottom;
    }

    /// <summary>
    /// 長体の下限と刻み（コンデンス版を持たない書体に使う）。見出しを折り返さずに済むなら、折り返すより先に
    /// 下限まで横幅を詰める。刻みを細かくして、必要な分だけ詰める（1 文字や記号 1 つだけが次の行へ落ちる組にしない）。
    /// </summary>
    private const float CondenseMin = 0.5f;
    private const float CondenseStep = 0.02f;

    /// <summary>
    /// 見出しが 1 行に収まるように <paramref name="font"/> を詰める。等幅で収まればそのまま true。
    /// 収まらなければ、書体にコンデンス版（<see cref="_condensedByHandle"/>）があれば広い順に書体を差し替えて試し、
    /// 無い書体は長体（<see cref="CondenseMin"/> まで刻みで）を試す。どれでも収まらなければ元の書体・等幅に戻して
    /// false を返す（呼び出し側が折り返しや級数の縮小に回す）。
    /// 収まるかどうかは <paramref name="fitsOnOneLine"/>（出力に使う折り返しそのもの）で判定するので、
    /// 幅の見積もりと実際の折り返しがずれて改行が出ることはない。判定はこのフォントで測ること。
    /// </summary>
    private bool CondenseToFit(SKFont font, Func<bool> fitsOnOneLine)
    {
        var baseTypeface = font.Typeface ?? throw new InvalidOperationException("書体の無いフォントです。");
        font.ScaleX = 1f;
        if (fitsOnOneLine()) return true;

        if (_condensedByHandle.TryGetValue(baseTypeface.Handle, out var variants))
        {
            foreach (var variant in variants)
            {
                font.Typeface = variant;
                if (fitsOnOneLine()) return true;
            }
            font.Typeface = baseTypeface;
            return false;
        }

        for (float scaleX = 1f - CondenseStep; scaleX >= CondenseMin - 0.0001f; scaleX -= CondenseStep)
        {
            font.ScaleX = scaleX;
            if (fitsOnOneLine()) return true;
        }
        font.ScaleX = 1f;
        return false;
    }

    /// <summary>
    /// 見出しの下に太いピンクの罫を引き、その下端 Y を返す。
    /// サイトの <c>h1</c> と同じ「1 本の罫でタイトル行を締める」規律をカードにも通す。
    /// </summary>
    private static float DrawTitleRule(SKCanvas canvas, SKPaint paint, float titleBottom, float contentWidth)
    {
        float ruleTop = titleBottom + TitleRuleGap;
        paint.Color = AccentPink;
        canvas.DrawRect(SKRect.Create(PaddingLeft, ruleTop, contentWidth, TitleRuleHeight), paint);
        return ruleTop + TitleRuleHeight;
    }

    /// <summary>特徴の言葉の字の大きさの候補（大きい順）。すべての行が幅に収まる最大のものを使う。</summary>
    private static readonly float[] StatementSizeCandidates = { 150f, 136f, 124f, 112f, 100f, 88f, 76f, 64f, 52f, 44f };

    /// <summary>タグラインの行送り（字の大きさに対する比）と、見出しの罫からの空き。字を大きく取るため詰めめにする。</summary>
    private const float StatementLineHeightRatio = 1.1f;
    private const float StatementTopGap = 30f;

    /// <summary>
    /// ヒーロー調のカードの言葉（タグライン）を描き、その下端 Y を返す。行は "\n" で切り、指定の書体を
    /// 機械的な斜体にして濃ピンクで組む。すべての行が幅に収まり、下端が <paramref name="floor"/> を超えない
    /// 最大の字の大きさを候補から選ぶ（斜体の右への張り出しぶんは幅から控える）。
    /// </summary>
    private float DrawStatement(SKCanvas canvas, SKPaint paint, string statement, SKTypeface typeface, float y, float contentWidth, float floor)
    {
        var lines = statement.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return y;

        using var font = new SKFont(typeface, StatementSizeCandidates[^1]) { SkewX = _obliqueSkew };
        foreach (var size in StatementSizeCandidates)
        {
            font.Size = size;
            float room = contentWidth - size * Math.Abs(_obliqueSkew);
            float bottom = y + StatementTopGap + lines.Count * size * StatementLineHeightRatio - size * (StatementLineHeightRatio - 1f);
            if (bottom <= floor && lines.All(l => Measure(font, l) <= room)) break;
        }

        paint.Color = HeroTitleColor;
        y += StatementTopGap;
        foreach (var line in lines)
        {
            y += font.Size;
            DrawTextOutlined(canvas, line, PaddingLeft, y, SKTextAlign.Left, font, paint);
            y += font.Size * (StatementLineHeightRatio - 1f);
        }
        return y - font.Size * (StatementLineHeightRatio - 1f);
    }

    /// <summary>
    /// 数の組を横に並べて描き、その下端 Y を返す。
    /// サイトの <c>.music-category-stats</c> と同じ語彙で、数字を大きくピンクの太字に、
    /// 単位とラベルを小さく添えて、視線が数へ行くようにする。
    /// 品番や尺のように「数の大小を語らない値」は分割せず 1 語として置く。
    /// </summary>
    private float DrawStats(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardBadge> badges, float x, float top, float maxWidth, float scale = 1f)
    {
        using var labelFont = new SKFont(_bodyTypeface, StatLabelFontSize * scale);
        using var valueFont = new SKFont(_numberTypeface, StatValueFontSize * scale);
        using var unitFont = new SKFont(_bodyTypeface, StatUnitFontSize * scale);

        float baseline = top + StatValueFontSize * scale;
        float cursor = x;

        foreach (var badge in badges)
        {
            // 収まらない場合は次の行へ送る（ホームのように数を 8 個並べるカードがあるため）。
            var match = CountValueRegex.Match(badge.Value);
            string number = match.Success ? match.Groups[1].Value : badge.Value;
            string unit = match.Success ? match.Groups[2].Value : "";

            // 収まらない組は落とす（幅を測ってから描く）。
            float width = 0f;
            if (!string.IsNullOrWhiteSpace(badge.Label)) width += Measure(labelFont, badge.Label) + 10f;
            width += Measure(valueFont, number);
            if (unit.Length > 0) width += Measure(unitFont, unit) + 3f;
            if (badge.Fraction.Length > 0) width += Measure(unitFont, badge.Fraction);
            if (badge.Tail.Length > 0) width += Measure(labelFont, badge.Tail);

            // 意味のまとまりでの改行が指定されていれば、幅が余っていても行を起こす。
            if (badge.NewLine && cursor > x)
            {
                cursor = x;
                baseline += StatLineHeight * scale;
            }
            if (cursor + width > x + maxWidth)
            {
                if (cursor <= x) break;
                cursor = x;
                baseline += StatLineHeight * scale;
            }

            if (!string.IsNullOrWhiteSpace(badge.Label))
            {
                paint.Color = Muted;
                DrawText(canvas, badge.Label, cursor, baseline, SKTextAlign.Left, labelFont, paint);
                cursor += Measure(labelFont, badge.Label) + 10f;
            }

            paint.Color = AccentPink;
            DrawText(canvas, number, cursor, baseline, SKTextAlign.Left, valueFont, paint);
            cursor += Measure(valueFont, number);

            if (unit.Length > 0)
            {
                paint.Color = Muted;
                cursor += 3f;
                DrawText(canvas, unit, cursor, baseline, SKTextAlign.Left, unitFont, paint);
                cursor += Measure(unitFont, unit);
            }

            // 端数は数の一部なので色は数と同じまま、大きさだけ落として続ける。
            if (badge.Fraction.Length > 0)
            {
                paint.Color = AccentPink;
                DrawText(canvas, badge.Fraction, cursor, baseline, SKTextAlign.Left, unitFont, paint);
                cursor += Measure(unitFont, badge.Fraction);
            }

            // 閉じ括弧などの添え字はラベルと同じ体裁で置く。
            if (badge.Tail.Length > 0)
            {
                paint.Color = Muted;
                DrawText(canvas, badge.Tail, cursor, baseline, SKTextAlign.Left, labelFont, paint);
                cursor += Measure(labelFont, badge.Tail);
            }

            cursor += StatGap * scale;
        }

        return baseline + 8f;
    }

    /// <summary>描画の最小単位。行に流し込んだあと、行ごとに左から順に描く。</summary>
    private sealed record FactPiece(string Text, SKColor Color, bool IsLabel, float TrailingGap);

    /// <summary>
    /// ラベルと値の組を流し込んで描き、ブロックの上端・下端の Y と、収まり切らず末尾を落としたかを返す。
    /// ラベルを役職色・値を本文色に分けることで、羅列ではなく「役職 → 担当者」の対応として読ませる。
    ///
    /// <para>
    /// 折り返しは語の境目で行う。値は「、」「／」の直後で語に割り、ラベルと最初の語は同じ行に置く
    /// （ラベルだけが行末に残らないように）。1 語が行幅を超えるときだけ文字単位で折る。
    /// 氏名の中（姓と名のあいだの空白）では折らないので、1 人の名前が 2 行に割れて別人に見えることがない。
    /// </para>
    /// </summary>
    /// <param name="anchor">
    /// <paramref name="anchorToTop"/> が true なら 1 行目のベースライン、false なら最終行のベースライン。
    /// </param>
    /// <param name="anchorToTop">true で上から下へ、false で下から上へ積む。</param>
    /// <param name="maxLines">描画に使える行数。</param>
    /// <param name="scale">文字と行送りの拡大率（疎なカードで大きく組むため）。</param>
    private (float Top, float Bottom, bool Truncated) DrawInlineFacts(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardFactLine> facts, float x, float anchor, float maxWidth, bool anchorToTop = false, int maxLines = InlineFactMaxLines, float scale = 1f)
    {
        using var labelFont = new SKFont(_emphasisTypeface, (FactFontSize - 4f) * scale);
        using var valueFont = new SKFont(_bodyTypeface, FactFontSize * scale);
        float lineHeight = FactLineHeight * scale;
        float gap = InlineFactGap * scale;

        var lines = new List<List<FactPiece>> { new() };
        float used = 0f;
        bool truncated = false;
        int limit = Math.Max(1, maxLines);

        bool NewLine()
        {
            if (lines.Count >= limit) return false;
            lines.Add(new List<FactPiece>());
            used = 0f;
            return true;
        }

        foreach (var fact in facts)
        {
            // 項目の頭（ラベル）。
            var labelPieces = new List<FactPiece>();
            float labelWidth = 0f;
            if (fact.LabelParts.Count > 0)
            {
                foreach (var part in fact.LabelParts)
                {
                    var color = SKColor.TryParse(part.ColorHex, out var c) ? c : Muted;
                    labelPieces.Add(new FactPiece(part.Text, color, true, 0f));
                    labelWidth += Measure(labelFont, part.Text);
                }
                labelWidth += 8f;
            }
            else if (!string.IsNullOrWhiteSpace(fact.Label))
            {
                var color = SKColor.TryParse(fact.LabelColorHex, out var c) ? c : Muted;
                labelPieces.Add(new FactPiece(fact.Label, color, true, 0f));
                labelWidth = Measure(labelFont, fact.Label) + 8f;
            }

            // 項目（役職と担当者）は行をまたがない。1 行に入る項目はひとかたまりとして扱い、
            // 行末に入らなければ項目ごと次の行へ送る。1 行に入らないほど長い項目だけ、区切りで中を折る。
            float valueWidth = Measure(valueFont, fact.Text);
            bool atomic = labelWidth + valueWidth <= maxWidth;
            var tokens = atomic ? new List<string> { fact.Text } : SplitFactTokens(fact.Text);
            float firstTokenWidth = tokens.Count > 0 ? Measure(valueFont, tokens[0]) : 0f;

            // ラベルと最初の語（ひとかたまりの項目なら全体）が今の行に載らなければ、項目ごと次の行へ。
            if (used > 0f && used + gap + labelWidth + firstTokenWidth > maxWidth)
            {
                if (!NewLine()) { truncated = true; break; }
            }
            if (used > 0f)
            {
                used += gap;
                lines[^1][^1] = lines[^1][^1] with { TrailingGap = gap };
            }
            for (int i = 0; i < labelPieces.Count; i++)
            {
                bool last = i == labelPieces.Count - 1;
                lines[^1].Add(labelPieces[i] with { TrailingGap = last ? 8f : 0f });
            }
            used += labelWidth;

            bool stop = false;
            foreach (var rawToken in tokens)
            {
                string token = rawToken;
                float width = Measure(valueFont, token);
                if (used > 0f && used + width > maxWidth && width <= maxWidth)
                {
                    if (!NewLine()) { truncated = true; stop = true; break; }
                    // 区切りの直後に続く空白（「, 」の空白など）は行頭に残さない。
                    token = token.TrimStart(' ');
                    width = Measure(valueFont, token);
                }
                if (width > maxWidth)
                {
                    // 区切りの無い 1 語が行幅を超える。文字単位で折るしかない。
                    string remaining = token;
                    while (remaining.Length > 0)
                    {
                        string chunk = TakeFittingPrefix(remaining, valueFont, paint, maxWidth - used);
                        if (chunk.Length == 0)
                        {
                            if (!NewLine()) { truncated = true; stop = true; break; }
                            continue;
                        }
                        lines[^1].Add(new FactPiece(chunk, Foreground, false, 0f));
                        used += Measure(valueFont, chunk);
                        remaining = remaining[chunk.Length..];
                    }
                    if (stop) break;
                    continue;
                }
                lines[^1].Add(new FactPiece(token, Foreground, false, 0f));
                used += width;
            }
            if (stop) break;
        }

        var (top, bottom) = FinishInlineFacts(canvas, paint, lines, labelFont, valueFont, x, anchor, anchorToTop, lineHeight);
        return (top, bottom, truncated);
    }

    /// <summary>値を折り返しの単位（語）に割る。「、」「／」の直後で切り、区切り文字は前の語に付ける。</summary>
    private static List<string> SplitFactTokens(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(text)) return tokens;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (!IsFactBreakPoint(text, i)) continue;
            tokens.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) tokens.Add(text[start..]);
        return tokens;
    }

    /// <summary>
    /// 指定幅に収まる最長の先頭部分を返す。切れ目が行頭禁則文字に当たる場合は 1 文字手前で切る。
    /// 1 文字も入らない場合は空文字（呼び出し側が改行する合図）。
    /// </summary>
    private string TakeFittingPrefix(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (maxWidth <= 0f) return "";
        if (Measure(font, text) <= maxWidth) return text;

        int take = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (Measure(font, text[..i]) > maxWidth) break;
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
        SKFont labelFont, SKFont valueFont, float x, float anchor, bool anchorToTop, float lineHeight)
    {
        if (lines.Count > 0 && lines[^1].Count == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return (anchor, anchor);

        float firstBaseline = anchorToTop ? anchor : anchor - (lines.Count - 1) * lineHeight;
        for (int i = 0; i < lines.Count; i++)
        {
            float baseline = firstBaseline + i * lineHeight;
            float cursor = x;
            foreach (var piece in lines[i])
            {
                var font = piece.IsLabel ? labelFont : valueFont;
                paint.Color = piece.Color;
                DrawText(canvas, piece.Text, cursor, baseline, SKTextAlign.Left, font, paint);
                cursor += Measure(font, piece.Text) + piece.TrailingGap;
            }
        }
        return (firstBaseline - valueFont.Size, firstBaseline + (lines.Count - 1) * lineHeight);
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
    private (float Top, float Bottom, bool Truncated) DrawStackedFacts(SKCanvas canvas, SKPaint paint, IReadOnlyList<OgCardFactLine> facts, float x, float anchor, bool anchorToTop = false, int maxLines = FactMaxLines)
    {
        using var labelFont = new SKFont(_emphasisTypeface, FactFontSize - 3f);
        using var valueFont = new SKFont(_bodyTypeface, FactFontSize);
        using var subFont = new SKFont(_bodyTypeface, FactFontSize - 3f);

        // 続き行の字下げ量は最も広いラベルに合わせる。値の左端が縦に揃うので、
        // 2 段に割れた項目も 1 つのまとまりとして読める。
        float indent = facts
            .Where(f => !string.IsNullOrWhiteSpace(f.Label))
            .Select(f => Measure(labelFont, f.Label))
            .DefaultIfEmpty(0f)
            .Max() + 14f;
        // 続き行はさらに一段深く落とす。値の左端に揃えるだけだと「同じ項目の続き」に見えず、
        // 別の項目が始まったように読めてしまう。
        float continuationIndent = indent + StackedContinuationIndent;

        // 先に描画行を組む（1 項目が値の折り返しと続き行で複数行を使うので、上限は行単位で数える）。
        // 行は「字下げ量・ラベル（先頭行のみ）・本文・従属級数か」の 4 点で表す。
        var rendered = new List<(float Indent, OgCardFactLine? Head, string Text, bool IsSub)>();
        int limit = Math.Max(1, maxLines);
        bool truncated = false;
        foreach (var fact in facts)
        {
            // 値はラベルの右に流し、入りきらない分は値の左端へ揃えて折り返す。
            // 折り返しは項目の区切り（読点・スラッシュ・空白）でのみ起こすので、氏名や社名が途中で割れない。
            var valueLines = WrapFactValue(fact.Text, valueFont, paint, CardWidth - PaddingRight - (x + indent));
            var subLines = string.IsNullOrWhiteSpace(fact.SubText)
                ? new List<string>()
                : WrapFactValue(fact.SubText, subFont, paint, CardWidth - PaddingRight - (x + continuationIndent));

            // 項目は途中で切らない。丸ごと入らないなら、その項目からは載せない。
            if (rendered.Count + valueLines.Count + subLines.Count > limit) { truncated = true; break; }

            for (int i = 0; i < valueLines.Count; i++)
                rendered.Add((0f, i == 0 ? fact : null, valueLines[i], false));
            foreach (var line in subLines)
                rendered.Add((continuationIndent, null, line, true));
        }
        if (rendered.Count == 0) return (anchor, anchor, truncated);

        float firstBaseline = anchorToTop ? anchor : anchor - (rendered.Count - 1) * FactLineHeight;
        for (int i = 0; i < rendered.Count; i++)
        {
            float baseline = firstBaseline + i * FactLineHeight;
            var (lineIndent, head, text, isSub) = rendered[i];
            float cursor = x + lineIndent;

            if (isSub)
            {
                paint.Color = Muted;
                DrawText(canvas, text, cursor, baseline, SKTextAlign.Left, subFont, paint);
                continue;
            }

            if (head is not null && !string.IsNullOrWhiteSpace(head.Label))
            {
                paint.Color = SKColor.TryParse(head.LabelColorHex, out var labelColor) ? labelColor : Muted;
                DrawText(canvas, head.Label, cursor, baseline, SKTextAlign.Left, labelFont, paint);
            }
            // ラベルの有無に関わらず値の左端は揃える。折り返した続き行も同じ位置から始まるので、
            // 1 項目が何行に伸びても「どこからどこまでが 1 項目か」が縦の揃いで読める。
            paint.Color = Foreground;
            DrawText(canvas, text, x + indent, baseline, SKTextAlign.Left, valueFont, paint);
        }
        return (firstBaseline - FactFontSize, firstBaseline + (rendered.Count - 1) * FactLineHeight, truncated);
    }

    /// <summary>
    /// 事実行の値を、与えられた幅に収まる行へ割る。
    /// 割り位置は項目の区切り（読点・中黒・スラッシュ）の直後のみ。
    /// 空白は割り位置に含めない——日本語の氏名は姓名のあいだを空白で区切るため、
    /// そこで折ると 1 人の名前が 2 行に割れて別人に見える。
    /// 区切りが無い長大な 1 語だけは例外的に文字単位で折る（それ以外に収める手段が無いため）。
    /// </summary>
    private List<string> WrapFactValue(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        int start = 0;
        while (start < text.Length)
        {
            if (Measure(font, text.AsSpan(start)) <= maxWidth)
            {
                lines.Add(text[start..]);
                break;
            }

            // 収まる範囲でいちばん後ろの区切り位置を探す。
            int lastBreak = -1;
            for (int i = start; i < text.Length; i++)
            {
                if (Measure(font, text.AsSpan(start, i - start + 1)) > maxWidth) break;
                if (IsFactBreakPoint(text, i)) lastBreak = i;
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
            while (fit < text.Length && Measure(font, text.AsSpan(start, fit - start + 1)) <= maxWidth) fit++;
            if (fit == start) fit = start + 1;
            lines.Add(text[start..fit]);
            start = fit;
        }
        return lines;
    }

    /// <summary>事実行を折ってよい文字（この文字の直後で改行する）。項目そのものの区切りだけを許す。</summary>
    private static bool IsFactBreakPoint(string text, int i)
    {
        char c = text[i];
        // 半角コンマは、数字に挟まれた千桁の区切り（「1,728円」）では折らない。
        if (c == ',') return !(i + 1 < text.Length && char.IsDigit(text[i + 1]) && i > 0 && char.IsDigit(text[i - 1]));
        return c is '、' or '，' or '・' or '／' or '/';
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
                && Measure(segmentFont, segment.Label) <= segWidth - 12f)
            {
                paint.Color = BarLabel;
                DrawText(canvas, segment.Label, cursor + segWidth / 2f, top + barHeight / 2f + 7f, SKTextAlign.Center, segmentFont, paint);
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
                totalWidth = Measure(captionFont, spec.BarTotalLabel);
                DrawText(canvas, spec.BarTotalLabel, x + width, baseline, SKTextAlign.Right, captionFont, paint);
            }
            if (!string.IsNullOrWhiteSpace(spec.BarCaption))
            {
                float room = width - (totalWidth > 0f ? totalWidth + 24f : 0f);
                DrawText(canvas, Ellipsize(spec.BarCaption, captionFont, paint, room), x, baseline, SKTextAlign.Left, captionFont, paint);
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
        // 左下は X が重ねるぶんを空けるので、右下に寄せる。幅はその空きを除いた分まで。
        float room = CardWidth - PaddingRight - (BandWidth + CaptionSafeWidth) - 16f;

        using (var brandFont = new SKFont(_brandTypeface, 26f))
        {
            paint.Color = Foreground;
            DrawText(canvas, Ellipsize(_brandLabel, brandFont, paint, room), CardWidth - PaddingRight, FooterTextBaseline, SKTextAlign.Right, brandFont, paint);
        }

        // 注記は基準点（MetaLeft）を優先し、無ければ右メタ。
        string note = !string.IsNullOrWhiteSpace(spec.MetaLeft) ? spec.MetaLeft : spec.MetaRight;
        if (!string.IsNullOrWhiteSpace(note))
        {
            using var metaFont = new SKFont(_bodyTypeface, 22f);
            paint.Color = Muted;
            DrawText(canvas, Ellipsize(note, metaFont, paint, room), CardWidth - PaddingRight, FooterNoteBaseline, SKTextAlign.Right, metaFont, paint);
        }
    }

    // ════════════════════════════════ 色帯・透かし ════════════════════════════════

    /// <summary>透かしの文字サイズの上限と下限、上端の位置。</summary>
    private const float WatermarkMaxSize = 128f;
    /// <summary>プロフィール組み（人物・キャラクター）の透かしの上限。見出しを透かしの下に置くぶん小さく。</summary>
    private const float WatermarkProfileMaxSize = 96f;
    private const float WatermarkMinSize = 44f;
    private const float WatermarkTop = 14f;

    /// <summary>種別の色（<see cref="OgCardSpec.BandColorHex"/>）。指定が無いか読めなければアクセントのピンク。</summary>
    private static SKColor BandColor(OgCardSpec spec)
        => SKColor.TryParse(spec.BandColorHex, out var c) ? c : AccentPink;

    /// <summary>左端の色帯。カードの種別を色で見分けるための帯で、全カードに描く。</summary>
    private static void DrawBand(SKCanvas canvas, OgCardSpec spec)
    {
        using var paint = new SKPaint { Color = BandColor(spec) };
        canvas.DrawRect(SKRect.Create(0f, 0f, BandWidth, CardHeight), paint);
    }

    /// <summary>
    /// 右上の透かし。種別の色を薄く（不透明度 2 割弱）敷いた大きな文字で、縮小表示でも「何のカードか」が形で伝わるようにする。
    /// 本文より先に描くので、本文の文字は透かしの上に乗る。
    /// 主の文字（<see cref="OgCardSpec.Watermark"/>）は右端に揃え、幅に収まる最大のサイズにする。
    /// 収まらない長い文字列（作品名など）は、作品名の切れ目で 2〜3 行に折る。
    /// 脇の文字（<see cref="OgCardSpec.WatermarkAside"/>）は主の左に一回り小さく置き、上端を主に揃えて同じ要領で折る。
    /// </summary>
    /// <returns>透かしが占める下端の Y（透かしが無ければ 0）。本文を透かしの下から始めるために使う。</returns>
    private float DrawWatermark(SKCanvas canvas, OgCardSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Watermark)) return 0f;

        using var paint = new SKPaint { IsAntialias = true, Color = BandColor(spec).WithAlpha(0x58) };
        float contentWidth = CardWidth - PaddingLeft - PaddingRight;
        bool hasAside = !string.IsNullOrWhiteSpace(spec.WatermarkAside);
        float mainMaxWidth = contentWidth * (hasAside ? 0.42f : 0.66f);

        // 見出しが透かしのすぐ下に来る組み（プロフィール・ヒーロー調・数を持つ高密度）は、透かしを一回り小さくする。
        // 疎な組み（エピソード・歌）だけ、見出しの横に大きく置く。
        bool compact = spec.IsProfile || spec.HeroVoice || spec.Badges.Count > 0 || spec.Bar.Count > 0;
        using var mainFont = new SKFont(_watermarkTypeface, compact ? WatermarkProfileMaxSize : WatermarkMaxSize);
        var mainLines = FitWatermark(spec.Watermark, mainFont, paint, mainMaxWidth, maxLines: hasAside ? 1 : 3);
        float mainWidth = mainLines.Max(l => Measure(mainFont, l));
        float lineHeight = mainFont.Size * 1.08f;
        float baseline = WatermarkTop + mainFont.Size * 0.92f;
        float right = CardWidth - PaddingRight;
        foreach (var line in mainLines)
        {
            DrawText(canvas, line, right, baseline, SKTextAlign.Right, mainFont, paint);
            baseline += lineHeight;
        }
        float bottom = baseline - lineHeight + mainFont.Size * 0.2f;

        if (!hasAside) return bottom;

        // 脇の文字。主の左に、主の 36%（下限 28px）の大きさで、残り幅へ折る。
        using var asideFont = new SKFont(_watermarkTypeface, Math.Max(28f, mainFont.Size * 0.36f));
        float asideRight = right - mainWidth - 28f;
        // 左上の前置き（種別など）と重ならないよう、前置きの幅ぶんは脇の幅から除く。
        float asideLeft = PaddingLeft;
        if (!string.IsNullOrWhiteSpace(spec.Kicker))
        {
            using var kickerFont = new SKFont(spec.HeroVoice ? _brandTypeface : _emphasisTypeface, 31f);
            asideLeft += Measure(kickerFont, spec.Kicker) + 24f;
        }
        float asideMaxWidth = Math.Max(120f, asideRight - asideLeft);
        var asideLines = FitWatermark(spec.WatermarkAside, asideFont, paint, asideMaxWidth, maxLines: 3);
        float asideBaseline = WatermarkTop + asideFont.Size * 0.95f;
        foreach (var line in asideLines)
        {
            DrawText(canvas, line, asideRight, asideBaseline, SKTextAlign.Right, asideFont, paint);
            asideBaseline += asideFont.Size * 1.2f;
        }
        return Math.Max(bottom, asideBaseline - asideFont.Size * 1.2f + asideFont.Size * 0.2f);
    }

    /// <summary>
    /// 見出しの書体を決める。<see cref="OgCardSpec.TitleFontFamily"/>（作品の本編テロップの書体名）が指定されていれば
    /// インストール済み書体から引く。見つからなければ既定の見出し書体で組み、警告（1 枚につき 1 つ）を返す。
    /// 書体名は「FOT-ハミング ProN B」のように重さまで含む名前なので、まずその名前で引き、
    /// 無ければ末尾の重さを切り離して「書体の族 ＋ スタイル名」で引く（Windows が族名と重さを分けて見せる書体のため）。
    /// </summary>
    private SKTypeface ResolveTypeface(string fontFamily, SKTypeface fallback, out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(fontFamily)) return fallback;

        var typeface = _familyTypefaces.GetOrAdd(fontFamily, family =>
        {
            var found = MatchInstalledTypeface(family);
            if (found is null) return null;
            lock (_familyLock)
            {
                _shapers.TryAdd(found.Handle, new OgTextShaper(found));
                _ownedTypefaces.Add(found);
            }
            return found;
        });
        if (typeface is null)
        {
            warning = $"書体「{fontFamily}」がこの PC に見つからないため、既定の書体で描画しました";
            return fallback;
        }
        return typeface;
    }

    /// <summary>この PC の書体ファイルの索引（name テーブルから引く）。初めて要るときに 1 度だけ作る。</summary>
    private static readonly Lazy<InstalledFontIndex> FontIndex = new(InstalledFontIndex.Build, isThreadSafe: true);

    /// <summary>インストール済み書体を名前で引く。まず書体ファイルの索引（日本語名・英語名とも）、次に Windows の書体一覧。見つからなければ null。</summary>
    private static SKTypeface? MatchInstalledTypeface(string name)
    {
        if (FontIndex.Value.Find(name) is { } hit)
        {
            var fromFile = SKTypeface.FromFile(hit.Path, hit.Index);
            if (fromFile is not null) return fromFile;
        }

        var manager = SKFontManager.Default;
        var direct = manager.MatchFamily(name);
        if (direct is not null) return direct;

        int cut = name.LastIndexOf(' ');
        if (cut <= 0) return null;
        string family = name[..cut];
        string styleName = name[(cut + 1)..];
        using var styles = manager.GetFontStyles(family);
        if (styles is null || styles.Count == 0) return null;
        for (int i = 0; i < styles.Count; i++)
        {
            if (string.Equals(styles.GetStyleName(i), styleName, StringComparison.OrdinalIgnoreCase))
                return styles.CreateTypeface(i);
        }
        return null;
    }

    /// <summary>
    /// 透かしの文字列を、幅に収まる最大のサイズで組む。まず 1 行で収まるサイズを上限から探し、
    /// 下限まで下げても収まらなければ、下限のサイズのまま長体（<see cref="CondenseMin"/> まで）で 1 行に詰め、
    /// それでも収まらなければ作品名の切れ目から折る（規定行数を超える分は末尾を省く）。
    /// <paramref name="font"/> のサイズと横方向の拡大率は採用した値に書き換わる。
    /// </summary>
    private List<string> FitWatermark(string text, SKFont font, SKPaint paint, float maxWidth, int maxLines)
    {
        float max = font.Size;
        font.ScaleX = 1f;
        for (float size = max; size >= WatermarkMinSize; size -= 4f)
        {
            font.Size = size;
            if (Measure(font, text) <= maxWidth) return new List<string> { text };
        }
        font.Size = WatermarkMinSize;
        if (CondenseToFit(font, () => Measure(font, text) <= maxWidth)) return new List<string> { text };
        var lines = WrapAtNaturalBreaks(text, font, paint, maxWidth, maxLines + 1);
        if (lines.Count > maxLines)
        {
            lines = lines.Take(maxLines).ToList();
            lines[^1] = Ellipsize(lines[^1] + "…", font, paint, maxWidth);
        }
        return lines;
    }

    /// <summary>折ってよい切れ目：この文字の直後で折る（閉じ括弧・記号・空白）。</summary>
    private const string BreakAfter = "』」）!！?？ 　・:：";

    /// <summary>折ってよい切れ目：この文字の直前で折る（開き括弧）。</summary>
    private const string BreakBefore = "『「（";

    /// <summary>
    /// 作品名のような固有名詞を、意味の切れ目（括弧・記号・空白）で折る。切れ目が無くて幅を超える語は文字単位で折る。
    /// 「映画 ふたりはプリキュアMax Heart 2 雪空のともだち」なら「映画 ／ ふたりはプリキュアMax Heart 2 ／ 雪空のともだち」のように割れる。
    /// </summary>
    private List<string> WrapAtNaturalBreaks(string text, SKFont font, SKPaint paint, float maxWidth, int maxLines)
    {
        var lines = new List<string>();
        int start = 0;
        while (start < text.Length && lines.Count < maxLines)
        {
            if (Measure(font, text.AsSpan(start)) <= maxWidth)
            {
                lines.Add(text[start..].Trim());
                break;
            }
            int lastBreak = -1;
            int fit = start;
            for (int i = start; i < text.Length; i++)
            {
                if (Measure(font, text.AsSpan(start, i - start + 1)) > maxWidth) break;
                fit = i + 1;
                if (i + 1 >= text.Length || i + 1 <= start) continue;
                char cur = text[i], nxt = text[i + 1];
                if (LineStartForbidden.Contains(nxt)) continue;
                if (BreakAfter.Contains(cur) || BreakBefore.Contains(nxt)) lastBreak = i + 1;
                else if (IsLatin(cur) != IsLatin(nxt) && cur != ' ' && nxt != ' ') lastBreak = i + 1;
            }
            // 欧文の単語の途中では折らない：文字単位で折るしかないときも、欧文の連なりの手前まで戻す。
            int cut = lastBreak > start ? lastBreak : Math.Max(start + 1, fit);
            if (lastBreak <= start && cut < text.Length && IsLatin(text[cut]) && IsLatin(text[cut - 1]))
            {
                int back = cut;
                while (back > start + 1 && IsLatin(text[back - 1])) back--;
                if (back > start) cut = back;
            }
            lines.Add(text[start..cut].Trim());
            start = cut;
            while (start < text.Length && (text[start] == ' ' || text[start] == '\u3000')) start++;
        }
        return lines.Where(l => l.Length > 0).ToList();
    }

    // ════════════════════════════════ プロフィール（人物・キャラクター） ════════════════════════════════

    /// <summary>年表の帯の高さと、年のラベルに使う高さ。</summary>
    private const float TimelineBarHeight = 26f;
    private const float TimelineLabelHeight = 26f;

    /// <summary>
    /// 人物・キャラクターの組み方。上から「所属（任意）→ 見出し＋ピンク罫 → 大きい数 → 役職と話数 → 関わった期間の年表」を置き、
    /// 下端に初参加などの事実行（<see cref="OgCardSpec.FootFacts"/>）を据える。
    /// 年表は残った余白いっぱいに広げるので、見出しが 1 行でも 2 行でも下半分が空かない。
    /// </summary>
    private void DrawProfileBody(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float contentWidth, float watermarkBottom)
    {
        // 見出しは透かしに重ねない（大きな名前と大きな透かしが重なると読みにくい）。透かしの下から始める。
        float y = watermarkBottom > 0f ? Math.Max(40f, watermarkBottom - 6f) : 40f;

        if (!string.IsNullOrWhiteSpace(spec.Kicker))
        {
            using var kickerFont = new SKFont(_emphasisTypeface, 30f);
            paint.Color = Muted;
            y += 30f;
            DrawText(canvas, Ellipsize(spec.Kicker, kickerFont, paint, contentWidth * 0.6f), PaddingLeft, y, SKTextAlign.Left, kickerFont, paint);
            y += 8f;
        }

        using (var titleFont = new SKFont(_boldTypeface, TitleSizeCandidates[0]))
        {
            // 見出しは 1 行に収めたい（下に年表を置く高さを残すため）。大きい順に、長体（下限まで）で
            // 1 行に収まるサイズを探し、どのサイズでも収まらなければ最小サイズで 2 行に折る。
            List<string> titleLines = new();
            foreach (float size in TitleSizeCandidates)
            {
                titleFont.Size = size;
                if (CondenseToFit(titleFont, () => WrapText(spec.Title, titleFont, paint, contentWidth, 2).Count <= 1))
                {
                    titleLines = new List<string> { spec.Title };
                    break;
                }
            }
            if (titleLines.Count == 0)
            {
                titleFont.Size = TitleSizeCandidates[^1];
                titleLines = FitTitle(spec.Title, titleFont, paint, contentWidth, new[] { TitleSizeCandidates[^1] }, TitleMaxLines);
            }
            paint.Color = Foreground;
            foreach (var line in titleLines)
            {
                y += titleFont.Size;
                DrawTextOutlined(canvas, line, PaddingLeft, y, SKTextAlign.Left, titleFont, paint);
                y += titleFont.Size * (TitleLineHeightRatio - 1f);
            }
        }
        y = DrawTitleRule(canvas, paint, y - 6f, contentWidth);

        float statScale = spec.StatScale > 0f ? spec.StatScale : 1.5f;
        if (spec.Badges.Count > 0)
            y = DrawStats(canvas, paint, spec.Badges, PaddingLeft, y + 10f, contentWidth, statScale);

        if (spec.InlineFacts.Count > 0)
        {
            var r = DrawInlineFacts(canvas, paint, spec.InlineFacts, PaddingLeft, y + 14f + FactFontSize, contentWidth, anchorToTop: true, maxLines: 2);
            y = r.Bottom + 4f;
        }

        // 下端の事実行（初参加など）は下から積む。年表や 1 行 1 項目の事実行はその上の余白へ。
        float floor = FooterLineY - 10f;
        float footTop = floor;
        if (spec.FootFacts.Count > 0)
        {
            var r = DrawStackedFacts(canvas, paint, spec.FootFacts, PaddingLeft, floor, anchorToTop: false, maxLines: 2);
            footTop = r.Top - 18f;
        }

        // 1 行 1 項目の事実行（キャラクターの登場作品など）。年表を持たないカードが、残った高さに入る行数だけ積む。
        if (spec.Facts.Count > 0)
        {
            float first = y + 26f + FactFontSize;
            int maxLines = Math.Max(1, (int)((footTop - first) / FactLineHeight) + 1);
            var r = DrawStackedFacts(canvas, paint, spec.Facts, PaddingLeft, first, anchorToTop: true, maxLines: maxLines);
            y = r.Bottom + 8f;
        }

        if (spec.Timeline.Count > 0)
        {
            float top = y + 18f;
            float height = footTop - top;
            if (height >= TimelineBarHeight + TimelineLabelHeight)
                DrawTimeline(canvas, paint, spec, PaddingLeft, top, contentWidth, height);
        }
    }

    /// <summary>
    /// 関わった期間の年表。横軸は <see cref="OgCardSpec.TimelineStart"/> から <see cref="OgCardSpec.TimelineEnd"/>、
    /// 年ごとの目盛りと、5 年おき（と両端）の年のラベルを引き、区間（<see cref="OgCardSpec.Timeline"/>）を
    /// 役職の色で塗った丸い帯で描く。始まりと終わりが同じ区間（映画）は小さな点にする。
    /// 役職詳細の線表（RoleTimelineBuilder）を 1 本に圧縮したもので、「いつからいつまで関わったか」を形で見せる。
    /// </summary>
    private void DrawTimeline(SKCanvas canvas, SKPaint paint, OgCardSpec spec, float x, float top, float width, float height)
    {
        var start = spec.TimelineStart;
        var end = spec.TimelineEnd <= start ? start.AddYears(1) : spec.TimelineEnd;
        float days = end.DayNumber - start.DayNumber;
        float X(DateOnly d) => x + width * Math.Clamp((d.DayNumber - start.DayNumber) / days, 0f, 1f);

        // 帯は使える高さの中で上下中央に。ラベルは帯の下。
        float barHeight = Math.Min(TimelineBarHeight * 1.4f, Math.Max(TimelineBarHeight, height - TimelineLabelHeight - 12f));
        float barTop = top + (height - TimelineLabelHeight - barHeight) / 2f;
        float labelBaseline = barTop + barHeight + TimelineLabelHeight - 2f;

        // 地の帯（種別の色をごく薄く）。
        paint.Color = BandColor(spec).WithAlpha(0x1a);
        canvas.DrawRoundRect(SKRect.Create(x, barTop, width, barHeight), 6f, 6f, paint);

        // 年の目盛りとラベル。ラベルは 5 年おきと両端で、端のラベルに近すぎるものは省く。
        using var tickFont = new SKFont(_bodyTypeface, 20f);
        float startLabelX = X(start);
        float endLabelX = X(end);
        for (int year = start.Year; year <= end.Year; year++)
        {
            var d = new DateOnly(year, 1, 1);
            if (d < start) d = start;
            float tx = X(d);
            paint.Color = Hairline;
            canvas.DrawRect(SKRect.Create(tx - 0.75f, barTop - 3f, 1.5f, barHeight + 6f), paint);

            bool isEdge = year == start.Year || year == end.Year;
            bool isRound = year % 5 == 0;
            if (!isEdge && !isRound) continue;
            if (!isEdge && (Math.Abs(tx - startLabelX) < 56f || Math.Abs(tx - endLabelX) < 56f)) continue;
            float lx = year == end.Year ? endLabelX : tx;
            paint.Color = Muted;
            DrawText(canvas, year.ToString(), lx, labelBaseline, year == end.Year ? SKTextAlign.Right : (year == start.Year ? SKTextAlign.Left : SKTextAlign.Center), tickFont, paint);
        }

        // 区間。
        foreach (var seg in spec.Timeline)
        {
            float sx = X(seg.Start);
            float ex = X(seg.End);
            paint.Color = SKColor.TryParse(seg.ColorHex, out var c) ? c : BandColor(spec);
            if (ex - sx < 10f)
            {
                // 点（映画など）。帯の中央に小さな丸。
                canvas.DrawCircle(sx, barTop + barHeight / 2f, Math.Min(7f, barHeight / 2f - 2f), paint);
                continue;
            }
            canvas.DrawRoundRect(SKRect.Create(sx, barTop, ex - sx, barHeight), 5f, 5f, paint);
        }
    }

    // ════════════════════════════════ ルビ付き見出し ════════════════════════════════

    /// <summary>
    /// ルビ組の 1 単位。<paramref name="Ruby"/> が空なら振り仮名を持たない素の文字。
    /// 折り返しは単位の境目でのみ起こすので、ルビ付きの文字が読みと切り離されることはない。
    /// </summary>
    private sealed record RubyUnit(string Base, string Ruby)
    {
        /// <summary>語の区切り（元の空白・改行）。字は描かず、<see cref="RubyGapRatio"/> ぶんの空きだけを取る。</summary>
        public static readonly RubyUnit Gap = new(" ", "");

        public bool IsGap => ReferenceEquals(this, Gap);
    }

    /// <summary>ルビ付き見出しの語の区切り（元の空白・改行）に取る空きの幅（字の大きさに対する比）。気持ち開ける程度。</summary>
    private const float RubyGapRatio = 0.3f;

    /// <summary>ルビ単位が行に占める幅。区切りは字の大きさの <see cref="RubyGapRatio"/>、それ以外は地の文の幅。</summary>
    private float RubyUnitWidth(SKFont baseFont, RubyUnit unit) =>
        unit.IsGap ? baseFont.Size * RubyGapRatio * baseFont.ScaleX : Measure(baseFont, unit.Base);

    /// <summary>
    /// <c>&lt;ruby&gt;漢&lt;rt&gt;かん&lt;/rt&gt;&lt;/ruby&gt;</c> 形式の HTML を組版単位へ分解する。
    /// 対象はサイトが出力するこの 1 形式だけなので、汎用 HTML パーサは持たずに正規表現で読む。
    /// ルビの無い地の文は 1 文字ずつの単位に割り、どこでも折り返せるようにする。
    /// </summary>
    private static List<RubyUnit> ParseRubyUnits(string html)
    {
        var units = new List<RubyUnit>();
        int pos = 0;

        // 改行（<br>）と空白は、サブタイトルの区切りなので「空き」の単位として残す（消すと前後の語がつながって読めてしまう）。
        // 続く空白・改行は 1 つの空きにまとめる。
        void AddPlain(string text)
        {
            string withBreaks = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var ch in System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(withBreaks, "<[^>]+>", "")))
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (units.Count > 0 && !units[^1].IsGap) units.Add(RubyUnit.Gap);
                    continue;
                }
                units.Add(new RubyUnit(ch.ToString(), ""));
            }
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
        float x, float topY, float maxWidth, float[] sizeCandidates, int maxLines, SKTypeface? typeface = null, float scaleX = 1f)
    {
        using var baseFont = new SKFont(typeface ?? _boldTypeface, sizeCandidates[^1]) { ScaleX = scaleX };
        // 振り仮名は本文の書体で、地の文と同じ率で詰める（占有幅の計算を地の文と揃えるため）。
        using var rubyFont = new SKFont(_bodyTypeface, sizeCandidates[^1] * RubySizeRatio) { ScaleX = scaleX };

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
                // 占有幅は地の文の幅。振り仮名のほうが広ければ、振り仮名を地の文の幅まで横に圧縮して載せる
                // （振り仮名に引きずられて地の文の字間が空かないようにする）。
                float slot = Measure(baseFont, unit.Base);

                paint.Color = Foreground;
                DrawTextOutlined(canvas, unit.Base, cursor, y, SKTextAlign.Left, baseFont, paint);

                if (unit.Ruby.Length > 0)
                {
                    float rubyScale = rubyFont.ScaleX;
                    float rubyWidth = Measure(rubyFont, unit.Ruby);
                    if (rubyWidth > slot && rubyWidth > 0f)
                    {
                        rubyFont.ScaleX = rubyScale * slot / rubyWidth;
                        rubyWidth = slot;
                    }
                    paint.Color = Muted;
                    DrawText(canvas, unit.Ruby, cursor + (slot - rubyWidth) / 2f, y - baseFont.Size * 0.98f, SKTextAlign.Left, rubyFont, paint);
                    rubyFont.ScaleX = rubyScale;
                }
                cursor += slot;
            }
            y += lineHeight - baseFont.Size;
        }

        return y - (lineHeight - baseFont.Size) + baseFont.Size * (TitleLineHeightRatio - 1f);
    }

    /// <summary>ルビ単位を行へ詰める。折り返し位置が行頭禁則に当たる場合は 1 単位前へ送る。</summary>
    private List<List<RubyUnit>> WrapRubyUnits(
        IReadOnlyList<RubyUnit> units, SKFont baseFont, SKFont rubyFont, SKPaint paint, float maxWidth, int maxLines)
    {
        var lines = new List<List<RubyUnit>> { new() };
        float used = 0f;

        foreach (var unit in units)
        {
            // 占有幅は地の文の幅。振り仮名が広くても地の文の幅へ圧縮して載せるので、行の長さには影響しない。
            float slot = Measure(baseFont, unit.Base);

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
                used = carry.Sum(u => Measure(baseFont, u.Base));
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
    private List<string> FitTitle(string title, SKFont font, SKPaint paint, float maxWidth, float[] sizeCandidates, int maxLines)
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
    private List<string> WrapText(string text, SKFont font, SKPaint paint, float maxWidth, int maxLines)
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
            if (Measure(font, current.ToString()) <= maxWidth) continue;

            // 溢れた。行の中に「空白や記号の直後」「文字種の境目（和文と欧文、カタカナ語の切れ目）」があれば
            // そこで折る（欧文の単語やカタカナ語を途中で割らない）。行頭近くにしか無いときは文字単位で折る。
            string line = current.ToString(0, current.Length - 1);
            int preferred = LastPreferredBreak(line);
            string carry;
            if (preferred > 0 && preferred >= line.Length * 0.4f)
            {
                carry = line[preferred..] + current[^1];
                line = line[..preferred];
            }
            else
            {
                // 送り出す 1 文字が行頭禁則なら、さらに 1 文字ぶん現在行から引き上げて次行へ回す。
                var overflow = current[^1];
                carry = overflow.ToString();
                if (LineStartForbidden.Contains(overflow) && line.Length > 1)
                {
                    carry = line[^1] + carry;
                    line = line[..^1];
                }
            }

            lines.Add(line.TrimEnd());
            current.Clear();
            current.Append(carry.TrimStart());
            if (lines.Count >= maxLines) return lines;
        }

        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    /// <summary>
    /// 行の中で折るのに向いた最後の位置（その位置の直前で折る）。無ければ -1。
    /// 空白・記号の直後、開き括弧の直前、和文と欧文の境目、カタカナ語（カタカナと長音の連なり）の切れ目を候補にする。
    /// </summary>
    private static int LastPreferredBreak(string line)
    {
        for (int k = line.Length - 1; k >= 1; k--)
        {
            char prev = line[k - 1];
            char next = line[k];
            if (LineStartForbidden.Contains(next)) continue;
            if (BreakAfter.Contains(prev) || BreakBefore.Contains(next)) return k;
            bool prevLatin = IsLatin(prev), nextLatin = IsLatin(next);
            if (prevLatin != nextLatin && prev != ' ' && next != ' ') return k;
            bool prevKana = IsKatakana(prev), nextKana = IsKatakana(next);
            if (prevKana != nextKana && !prevLatin && !nextLatin) return k;
        }
        return -1;
    }

    private static bool IsLatin(char c) => c < 0x3000 && (char.IsLetterOrDigit(c) || c == '!' || c == '?' || c == '\'' || c == '.');

    private static bool IsKatakana(char c) => (c >= '\u30a0' && c <= '\u30ff') || c == 'ー';

    /// <summary>1 行に収まらないテキストを末尾省略記号付きで切り詰める。</summary>
    private string Ellipsize(string text, SKFont font, SKPaint paint, float maxWidth)
    {
        if (Measure(font, text) <= maxWidth) return text;

        var trimmed = text;
        while (trimmed.Length > 1 && Measure(font, trimmed + "…") > maxWidth)
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
        return $"ブランド書体に無い文字「{missing}」を含む見出しは本文書体で描画しました";
    }

    public void Dispose()
    {
        foreach (var shaper in _shapers.Values) shaper.Dispose();
        foreach (var typeface in _ownedTypefaces) typeface.Dispose();
    }
}
