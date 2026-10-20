using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 作品ごとのサブタイトルテロップの組み方。親字の書体は <c>series_font_usages</c> のサブタイトルの行が指す <c>fonts.render_font_name</c> で、ここでは字間、振り仮名の書体・斜体・
/// 大きさと高さ、行間、親字より長い振り仮名の置き方を持つ。値は <c>series_subtitle_styles</c> の列から作り（<see cref="FromSeries"/>）、
/// 列が NULL の項目は既定値で組む。
/// </summary>
public sealed record SubtitleTelopProfile
{
    /// <summary>すべて既定値の組み方。</summary>
    public static readonly SubtitleTelopProfile Default = new();

    /// <summary>親字・振り仮名をベタ組み（字送り幅のまま、詰めない）にするか。false なら書体の詰め情報（なければ字面）で詰める。</summary>
    public bool Solid { get; init; }

    /// <summary>親字の字と字のあいだに足す空き（字の大きさに対する比）。負なら詰める。</summary>
    public float LetterSpacingEm { get; init; }

    /// <summary>振り仮名の字と字のあいだに足す空き（振り仮名の大きさに対する比）。</summary>
    public float RubyLetterSpacingEm { get; init; }

    /// <summary>ベタ組み（<see cref="Solid"/>）で、全角の約物だけを書体の詰め情報（palt）で半分の幅に詰めるか（約物半角）。</summary>
    public bool PunctHalf { get; init; }

    /// <summary>
    /// 親字の欧文（英数字と半角の記号）と約物のあとに、<see cref="LetterSpacingEm"/> に加えて足す空き（字の大きさに対する比）。
    /// 行の最後の字のあとには足さず、欧文・約物のすぐあとの空白のあとにも足す。
    /// </summary>
    public float LatinSpacingEm { get; init; }

    /// <summary>ベタ組み（<see cref="Solid"/>）で、小書きの仮名にかける詰め（palt）の割合（0〜1）。0 なら詰めない。</summary>
    public float SmallKanaPalt { get; init; }

    /// <summary>振り仮名の書体名（インストール済み書体の名前）。空なら親字と同じ書体を使う。</summary>
    public string RubyFontFamily { get; init; } = "";

    /// <summary>
    /// 親字にかける機械的な斜体の角度（度。右に倒す）。0 なら倒さない。サブタイトルでは使わず、書体の見本で
    /// 斜体のクレジット（作品の書体の表の <c>oblique_deg</c>）を描くときに使う。振り仮名は <see cref="RubyObliqueDegrees"/>。
    /// </summary>
    public float BaseObliqueDegrees { get; init; }

    /// <summary>
    /// 装飾（字の色・フチ・影・光彩）。サブタイトルは既定の体裁（白い字・黒い丸フチ・右下の黒い影）で、
    /// 書体の見本では作品の書体の表の <c>decoration</c> を使う。
    /// </summary>
    public TelopDecoration Decoration { get; init; } = TelopDecoration.Default;

    /// <summary>
    /// 装飾を画素で重ねて描くか（<see cref="TelopRaster"/>）。書体の見本は、放送の画面と比べて体裁を測ったときと同じ計算で描くので true。
    /// サブタイトルは輪郭の線で描く（false）。
    /// </summary>
    public bool RasterDecoration { get; init; }

    /// <summary>長体の下限の目安（作品の組み方の <c>subtitle.condenseMin</c>）。各話のテロップ画像は字の大きさを変えないので、収まらない行はこれより縮めて収め、描き方には使わない。</summary>
    public float CondenseMin { get; init; } = 0.8f;

    /// <summary>親字の行に長体をかけても、振り仮名は細くしないか（作品の組み方の <c>subtitle.rubyKeepWidth</c>）。</summary>
    public bool RubyKeepWidth { get; init; }

    /// <summary>行ごとの横の倍率（その回の <c>subtitle.episodeLineScales</c>）。組み方で決まった長体の率に、さらに掛ける。null なら掛けない。</summary>
    public IReadOnlyList<float>? LineScales { get; init; }

    /// <summary>行ごとの振り仮名の高さのずれ（その回の <c>subtitle.episodeRubyOffsets</c>、親字の大きさに対する比、上を正）。null なら足さない。</summary>
    public IReadOnlyList<float>? RubyRaiseOffsets { get; init; }

    /// <summary>行ごとの位置のずれ（その回の <c>subtitle.episodeLineOffsets</c>、行ごとに [横, 縦]。親字の大きさに対する比、右・下を正）。null ならずらさない。</summary>
    public IReadOnlyList<IReadOnlyList<float>>? LineOffsets { get; init; }

    /// <summary>
    /// 字の大きさを決めて描く（場面の見本）ときの行の最大幅（画素）。超える行はその行だけ、この幅に収まるまで長体をかける
    /// （長体の下限は見ない）。null なら長体をかけない。
    /// </summary>
    public float? LineMaxWidth { get; init; }

    /// <summary>
    /// 行の最大幅（字の大きさに対する比）。本編のいちばん長い行の幅を字の大きさで割った値で、各話のテロップ画像はこれを超える行を
    /// その幅まで長体で詰める（本編と同じく字を小さくせずに横だけ縮める）。null なら画像の最大幅だけで決める。
    /// </summary>
    public float? LineMaxEm { get; init; }

    /// <summary>
    /// 行の中の全角の空白（続く空白のまとまり 1 つ）の空き（字の大きさに対する比）。半角の空白だけのまとまりは、書体の半角の空白の字送りに
    /// 前後の字間 2 つ分（<see cref="LetterSpacingEm"/>）を足した空きにする。null なら全角 1 字・半角半字。
    /// </summary>
    public float? SpaceEm { get; init; }

    /// <summary>振り仮名にかける機械的な斜体の角度（度。右に倒す）。0 なら倒さない。親字は <see cref="BaseObliqueDegrees"/>。</summary>
    public float RubyObliqueDegrees { get; init; }

    /// <summary>振り仮名の大きさ（親字の大きさに対する比）。</summary>
    public float RubySizeRatio { get; init; } = 0.3f;

    /// <summary>
    /// 振り仮名のベースラインを親字のベースラインからどれだけ上げるか（親字の大きさに対する比）。
    /// 既定は、親字の上端（0.88）とフチ（0.048）の上に、振り仮名の下端とそのフチが少し空きを残して載る高さ。
    /// </summary>
    public float RubyRaiseRatio { get; init; } = 1.04f;

    /// <summary>下の行に振り仮名があるときの、上の行の字の下端から下の行の振り仮名の段の上端までの空き（親字の大きさに対する比）。null なら既定の空きで組む。</summary>
    public float? LineGapRatio { get; init; }

    /// <summary>
    /// 下の行に振り仮名が無いときの、上の行の字の下端から下の行の字の上端までの空き（親字の大きさに対する比）。
    /// null なら <see cref="LineGapRatio"/> に振り仮名の段の高さを足した空き（振り仮名の有無で行送りを変えない）。
    /// </summary>
    public float? LineGapRatioPlain { get; init; }

    /// <summary>3 行以上の組での行と行のあいだの空き（親字の大きさに対する比）。null なら <see cref="LineGapRatio"/> と同じ。</summary>
    public float? LineGapRatio3 { get; init; }

    /// <summary>
    /// 親字より長い振り仮名が、振り仮名の無い隣の字へはみ出してよい最大の幅（片側、親字の大きさに対する比）。
    /// null なら振り仮名 1 字分（<see cref="RubySizeRatio"/> と同じ）。
    /// </summary>
    public float? RubyOverhangRatio { get; init; }

    /// <summary>行頭の振り仮名を行の外へはみ出させるか。false なら行頭にそろえて内側へ寄せる。</summary>
    public bool RubyOverhangLineStart { get; init; }

    /// <summary>行末の振り仮名を行の外へはみ出させるか。false なら行末にそろえて内側へ寄せる。</summary>
    public bool RubyOverhangLineEnd { get; init; } = true;

    /// <summary>振り仮名の置き方（1 字ずつ / 熟語でひと続き / 熟語の幅に均等に並べる）。</summary>
    public SubtitleRubyGrouping RubyGrouping { get; init; } = SubtitleRubyGrouping.Mono;

    /// <summary>シリーズの列から組み方を作る。NULL の列は既定値のまま。</summary>
    public static SubtitleTelopProfile FromSeries(Series series) => new()
    {
        Solid = string.Equals(series.SubtitleKerning, "MONO", StringComparison.OrdinalIgnoreCase),
        LetterSpacingEm = (float)(series.SubtitleLetterSpacingEm ?? 0m),
        RubyLetterSpacingEm = (float)(series.SubtitleRubyLetterSpacingEm ?? 0m),
        RubyFontFamily = series.FontSubtitleRuby ?? "",
        RubyObliqueDegrees = (float)(series.SubtitleRubyObliqueDeg ?? 0m),
        RubySizeRatio = series.SubtitleRubySizeRatio is decimal size ? (float)size : Default.RubySizeRatio,
        RubyRaiseRatio = series.SubtitleRubyRaiseRatio is decimal raise ? (float)raise : Default.RubyRaiseRatio,
        LineGapRatio = series.SubtitleLineGapRatio is decimal gap ? (float)gap : null,
        LineGapRatio3 = series.SubtitleLineGapRatio3 is decimal gap3 ? (float)gap3 : null,
        LineGapRatioPlain = series.SubtitleLineGapRatioPlain is decimal plain ? (float)plain : null,
        RubyOverhangRatio = series.SubtitleRubyOverhangRatio is decimal overhang ? (float)overhang : null,
        RubyOverhangLineStart = string.Equals(series.SubtitleRubyLineEdge, "OVERHANG", StringComparison.OrdinalIgnoreCase),
        RubyOverhangLineEnd = !string.Equals(series.SubtitleRubyLineEdge, "ALIGN", StringComparison.OrdinalIgnoreCase),
        RubyGrouping = series.SubtitleRubyGrouping?.ToUpperInvariant() switch
        {
            "JUKUGO" => SubtitleRubyGrouping.Jukugo,
            "SPREAD" => SubtitleRubyGrouping.Spread,
            "GROUP" => SubtitleRubyGrouping.Group,
            _ => SubtitleRubyGrouping.Mono
        }
    };
}

/// <summary>サブタイトルテロップの振り仮名の置き方（<c>series.subtitle_ruby_grouping</c>）。</summary>
public enum SubtitleRubyGrouping
{
    /// <summary>1 字ずつ（ルビの単位ごとに）親字の上に置く（MONO）。</summary>
    Mono,

    /// <summary>
    /// 1 字ずつ置き、読みが親字と脇の空きに収まらないところだけ、隣の振り仮名のある字と読みをひと続きにして熟語全体の中央に置く（JUKUGO）。
    /// </summary>
    Jukugo,

    /// <summary>
    /// 振り仮名のある字が続くところの読みを、熟語の幅に 1 字ずつ均等に空けて並べる（SPREAD）。
    /// 空きは字と字の間に 1 つ分、両端に半分ずつ。読みが熟語より長ければ <see cref="Jukugo"/> と同じに置く。
    /// </summary>
    Spread,

    /// <summary>
    /// 振り仮名のある字が空白をはさまず続くところの読みを、いつもひと続きにして、振り仮名の字間（<c>subtitle_ruby_letter_spacing_em</c>）で
    /// 熟語全体の字面の中央に置く（GROUP）。熟語の幅には広げない。
    /// </summary>
    Group,
}
