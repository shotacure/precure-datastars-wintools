using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// 作品ごとのサブタイトルテロップの組み方。親字の書体は <c>series_subtitle_styles.font_subtitle</c> で、ここでは字間、振り仮名の書体・斜体・
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

    /// <summary>振り仮名の書体名（インストール済み書体の名前）。空なら親字と同じ書体を使う。</summary>
    public string RubyFontFamily { get; init; } = "";

    /// <summary>振り仮名にかける機械的な斜体の角度（度。右に倒す）。0 なら倒さない。親字は倒さない。</summary>
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
            _ => SubtitleRubyGrouping.Mono
        }
    };
}

/// <summary>サブタイトルテロップの振り仮名の置き方（<c>series.subtitle_ruby_grouping</c>）。</summary>
public enum SubtitleRubyGrouping
{
    /// <summary>1 字ずつ（ルビの単位ごとに）親字の上に置く（MONO）。</summary>
    Mono,

    /// <summary>振り仮名のある字が続くところの読みをひと続きにして、熟語全体の中央に置く（JUKUGO）。</summary>
    Jukugo,

    /// <summary>
    /// 振り仮名のある字が続くところの読みを、熟語の幅に 1 字ずつ均等に空けて並べる（SPREAD）。
    /// 空きは字と字の間に 1 つ分、両端に半分ずつ。読みが熟語より長ければ <see cref="Jukugo"/> と同じに置く。
    /// </summary>
    Spread
}
