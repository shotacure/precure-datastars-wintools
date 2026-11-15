using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrecureDataStars.Data.Models;

/// <summary>
/// テロップの装飾（字の色・フチ・影・光彩）。<c>series_font_usages.decoration</c>（JSON）の中身で、
/// 字の塗りの色・フチの角の形と、字の下に重ねる層の並び（<see cref="Layers"/>、下から順）を持つ。
/// 塗りのグラデーション・字ごとの色・内側の光・斜線・光彩などの部品は省略でき、省略すれば使わない。
/// 太さ・ずれ・ぼかしはすべて字の大きさに対する比（クレジットは人名の字の大きさ。役職名も同じ太さで描く）。
/// <code>
/// { "version": 1, "fill": "#FFFFFF", "join": "ROUND",
///   "layers": [ { "kind": "shadow", "color": "#000000", "dx": 0.037, "dy": 0.037 },
///               { "kind": "outline", "color": "#000000", "width": 0.048 } ] }
/// </code>
/// </summary>
public sealed record TelopDecoration
{
    /// <summary>いまの JSON の形の版。形を変えるときは版を上げ、読む側で見分ける。</summary>
    public const int CurrentVersion = 1;

    /// <summary>既定の体裁（白い字・黒い丸フチ 0.048・フチごと右下へ 0.037 ずらした黒い影）。サブタイトルのテロップの体裁。</summary>
    public static readonly TelopDecoration Default = new()
    {
        Layers =
        [
            new TelopDecorationLayer { Kind = TelopDecorationLayer.KindShadow, Color = "#000000", Dx = 0.037f, Dy = 0.037f },
            new TelopDecorationLayer { Kind = TelopDecorationLayer.KindOutline, Color = "#000000", Width = 0.048f }
        ]
    };

    /// <summary>JSON の形の版。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>字の塗りの色（#RRGGBB）。</summary>
    public string Fill { get; init; } = "#FFFFFF";

    /// <summary>フチの角の形。<c>ROUND</c>（丸）/ <c>MITER</c>（とがり）。</summary>
    public string Join { get; init; } = "ROUND";

    /// <summary>字の下に重ねる層。下から順に重ね、最後に字を塗る。</summary>
    public IReadOnlyList<TelopDecorationLayer> Layers { get; init; } = [];

    /// <summary>字の塗りのグラデーション。null なら <see cref="Fill"/> の 1 色で塗る。</summary>
    public TelopGradient? FillGradient { get; init; }

    /// <summary>
    /// 字ごとの塗りの色（#RRGGBB、行の字の順に繰り返して使う）。字ごとに色の違うテロップの体裁で、
    /// 指定があれば <see cref="Fill"/>・<see cref="FillGradient"/> より優先する。
    /// </summary>
    public IReadOnlyList<string>? FillChars { get; init; }

    /// <summary>
    /// 字の塗りを太らせる幅（字の輪郭から外へ）。描く書体の字が放送より細いときに使う。0 なら太らせない。
    /// 負の値なら塗りをその幅だけ内側へ細らせ、フチを字の輪郭より内側まで見せる（フチが白い字に食い込む体裁）。
    /// </summary>
    public float FillGrow { get; init; }

    /// <summary>
    /// テロップ全体のやわらかさ（ガウスぼかしの標準偏差、字の大きさに対する比）。層と塗りを重ね終えた色と濃さをまとめてぼかす。
    /// 放送の字は縁がわずかにぼけている（映像の解像の甘さ）ので、その分を描き足す。0 ならぼかさない。
    /// </summary>
    public float Blur { get; init; }

    /// <summary>字の内側の光（字の輪郭から内側へにじむ色）。null なら無し。</summary>
    public TelopInnerGlow? InnerGlow { get; init; }

    /// <summary>字の塗りの上の斜線の模様。null なら無し。</summary>
    public TelopStripes? Stripes { get; init; }

    /// <summary>字の塗りの上に散らすキラキラ（4 つの角の星）。null なら無し。</summary>
    public TelopSparkles? Sparkles { get; init; }

    /// <summary>字の塗りに混ぜるまだら模様（色の濃淡のむら）。null なら無し。</summary>
    public TelopMottle? Mottle { get; init; }

    /// <summary>振り仮名だけの塗りの色（#RRGGBB）。null なら親字と同じ塗り。</summary>
    public string? RubyFill { get; init; }

    /// <summary>
    /// 振り仮名だけの装飾。あれば振り仮名はこの装飾（塗り・層・太らせ・全体のやわらかさ）で描き、親字の装飾は親字だけにかける
    /// （装飾の形は同じでも太さ・ずれ・ぼかしが親字と違うサブタイトル用）。値は親字の大きさに対する比。画素で重ねる描き方でだけ効く。
    /// null なら振り仮名も親字の装飾で描く。
    /// </summary>
    public TelopDecoration? Ruby { get; init; }

    /// <summary>
    /// クレジットの役職名だけの塗りの色（#RRGGBB）。役職名と人名で字の色の違うクレジットに使う（フチ・影などの装飾は共通）。
    /// null なら人名と同じ塗り。
    /// </summary>
    public string? RoleFill { get; init; }

    /// <summary>
    /// クレジットの役職名だけの字ごとの塗りの色（#RRGGBB、役職名の字の順に繰り返す）。役職名と人名で字ごとの色の並びが違う
    /// クレジットに使う。指定があれば <see cref="RoleFill"/> より優先する。null なら人名と同じ塗り。
    /// </summary>
    public IReadOnlyList<string>? RoleFillChars { get; init; }

    /// <summary>字とフチをまとめてぼかした光彩（フチの上に重ねる）。null なら無し。</summary>
    public TelopGlow? Glow { get; init; }

    /// <summary>決まった字（感嘆符・疑問符など）だけを傾けて置く指定。サブタイトルの見本と各話のテロップ画像に使う。null なら傾けない。</summary>
    public TelopCharTilt? CharTilt { get; init; }

    /// <summary>
    /// 「！」「？」のまとまり（2 字以上の連なりと、区切りの末尾の単独の字）の 1 字あたりの字送り（字の大きさに対する比）。
    /// 「！！」「？！」「！？」などを、1 字ずつこの間隔で置いて詰める。null なら書体の字詰めのまま。
    /// </summary>
    public float? PunctRunAdvance { get; init; }

    /// <summary>
    /// 前の字の送りの端から、「！」「？」のまとまり（連なり・区切りの末尾の単独の字）の最初の字の中心までの距離（字の大きさに対する比）。
    /// null なら字間の空き＋字送りの半分。<see cref="PunctRunAdvance"/> があるときだけ使う。
    /// </summary>
    public float? PunctFirstCenter { get; init; }

    /// <summary>区切りの末尾の単独の「！」「？」の中心の、前の字の送りの端からの距離（字の大きさに対する比）。null なら <see cref="PunctFirstCenter"/>。</summary>
    public float? PunctLoneCenter { get; init; }

    /// <summary>
    /// 行の途中の単独の「！」「？」も、行末の単独の記号と同じ詰め方（<see cref="PunctLoneCenter"/>・<see cref="PunctRunAdvance"/>）で置くか。
    /// false なら行の途中のものは全角の幅を取って中央に置く。
    /// </summary>
    public bool PunctMidLone { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// <c>decoration</c> 列の JSON を読む。NULL・空なら null（呼ぶ側で既定の体裁にする）。
    /// 読めない JSON や知らない版は例外にする（黙って既定の体裁で描くと、測った体裁と違う見本が出るため）。
    /// </summary>
    public static TelopDecoration? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var d = JsonSerializer.Deserialize<TelopDecoration>(json, JsonOptions)
            ?? throw new FormatException("decoration の JSON が空です。");
        if (d.Version != CurrentVersion)
            throw new FormatException($"decoration の JSON の版 {d.Version} は読めません（読めるのは {CurrentVersion}）。");
        foreach (var layer in d.Layers)
        {
            if (layer.Kind is not (TelopDecorationLayer.KindShadow or TelopDecorationLayer.KindOutline or TelopDecorationLayer.KindBevel))
                throw new FormatException($"decoration の層の種類 \"{layer.Kind}\" は知りません。");
        }
        return d;
    }

    /// <summary>JSON に書き出す（<c>decoration</c> 列に入れる形）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// 太さ・ずれ・ぼかしなどの比を <paramref name="k"/> 倍した装飾。字の大きさの違う字（役職名）に、人名と同じ絶対の太さで
    /// 装飾をかけるときに使う（人名の大きさに対する比を、役職名の大きさに対する比へ直す。k = 人名の大きさ ÷ 役職名の大きさ）。
    /// </summary>
    public TelopDecoration ScaledBy(float k) => this with
    {
        FillGrow = FillGrow * k,
        Blur = Blur * k,
        FillGradient = FillGradient is null ? null : FillGradient with { Period = FillGradient.Period * k, Offset = FillGradient.Offset * k },
        InnerGlow = InnerGlow is null ? null : InnerGlow with { Blur = InnerGlow.Blur * k },
        Stripes = Stripes is null ? null : Stripes with
        {
            Period = Stripes.Period * k, Width = Stripes.Width * k, Offset = Stripes.Offset * k,
            Bands = Stripes.Bands is null ? null : Stripes.Bands with { Period = Stripes.Bands.Period * k, Width = Stripes.Bands.Width * k, Offset = Stripes.Bands.Offset * k }
        },
        Glow = Glow is null ? null : Glow with { Width = Glow.Width * k, Blur = Glow.Blur * k },
        Sparkles = Sparkles is null ? null : Sparkles with { Size = Sparkles.Size * k, Period = Sparkles.Period * k },
        Mottle = Mottle is null ? null : Mottle with { Scale = Mottle.Scale * k },
        CharTilt = CharTilt is null ? null : CharTilt with { Dx = CharTilt.Dx * k, Dy = CharTilt.Dy * k },
        Ruby = Ruby?.ScaledBy(k),
        Layers = Layers.Select(l => l with
        {
            Width = l.Width * k, Dx = l.Dx * k, Dy = l.Dy * k, Blur = l.Blur * k, Spread = l.Spread * k, SpreadX = l.SpreadX * k, Ring = l.Ring * k, Noise = l.Noise * k, NoiseScale = l.NoiseScale * k,
            Gradient = l.Gradient is null ? null : l.Gradient with { Period = l.Gradient.Period * k, Offset = l.Gradient.Offset * k }
        }).ToList()
    };

    /// <summary>
    /// 見た目の似た装飾か（層の種類の並びが同じで、色の差が各成分 <paramref name="colorTolerance"/> 以内、太さ・ずれ・ぼかしの差が
    /// 字の大きさの <paramref name="lengthTolerance"/> 以内、塗りの部品の有無が同じ）。書体のページで、ほとんど同じ見た目の見本をまとめるのに使う。
    /// </summary>
    public bool LooksLike(TelopDecoration other, int colorTolerance = 28, float lengthTolerance = 0.02f)
    {
        static bool Near(string? a, string? b, int tol)
        {
            if (a is null || b is null) return a == b;
            static (int, int, int) Rgb(string h) => h.Length >= 7
                ? (Convert.ToInt32(h.Substring(1, 2), 16), Convert.ToInt32(h.Substring(3, 2), 16), Convert.ToInt32(h.Substring(5, 2), 16))
                : (0, 0, 0);
            var (r1, g1, b1) = Rgb(a);
            var (r2, g2, b2) = Rgb(b);
            return Math.Abs(r1 - r2) <= tol && Math.Abs(g1 - g2) <= tol && Math.Abs(b1 - b2) <= tol;
        }
        bool Len(float a, float b) => Math.Abs(a - b) <= lengthTolerance;
        if (!Near(Fill, other.Fill, colorTolerance) || Layers.Count != other.Layers.Count) return false;
        if ((FillGradient is null) != (other.FillGradient is null) || (FillChars is null) != (other.FillChars is null)
            || (InnerGlow is null) != (other.InnerGlow is null) || (Stripes is null) != (other.Stripes is null)
            || (Glow is null) != (other.Glow is null) || (RubyFill is null) != (other.RubyFill is null)
            || !Near(RoleFill, other.RoleFill, colorTolerance) || (RoleFillChars is null) != (other.RoleFillChars is null)
            || (CharTilt is null) != (other.CharTilt is null) || PunctRunAdvance != other.PunctRunAdvance || PunctFirstCenter != other.PunctFirstCenter
            || PunctLoneCenter != other.PunctLoneCenter || PunctMidLone != other.PunctMidLone) return false;
        for (int i = 0; i < Layers.Count; i++)
        {
            var (a, b) = (Layers[i], other.Layers[i]);
            if (a.Kind != b.Kind || !Near(a.Color, b.Color, colorTolerance) || !Len(a.Width, b.Width)
                || !Len(a.Dx, b.Dx) || !Len(a.Dy, b.Dy) || !Len(a.Blur, b.Blur) || Math.Abs(a.Opacity - b.Opacity) > 0.15f) return false;
        }
        return true;
    }

    /// <summary>
    /// 輪郭の線で描けない部品（面取り・フチの中のグラデーション・層や塗りのグラデーション・字ごとの色・内側の光・斜線・
    /// 振り仮名だけの塗り・光彩・塗りの太らせ）を使っているか。使っていれば画素を重ねる描き方でしか描けない。
    /// </summary>
    [JsonIgnore]
    public bool NeedsRaster =>
        FillGradient is not null || FillChars is { Count: > 0 } || FillGrow != 0f || Blur > 0f || InnerGlow is not null || Stripes is not null || Sparkles is not null || Mottle is not null
        || RubyFill is not null || Glow is not null || Ruby is not null
        || Layers.Any(l => l.Kind == TelopDecorationLayer.KindBevel || l.InnerColor is not null || l.Gradient is not null);
}

/// <summary>
/// グラデーション（字の行の範囲に沿って色を移す）。<see cref="Axis"/> が <c>v</c> なら字の上端→下端、<c>h</c> なら左端→右端。
/// 色の位置（<see cref="Positions"/>、0〜1）が無ければ等間隔。
/// </summary>
public sealed record TelopGradient
{
    /// <summary>向き（<c>v</c> 縦 / <c>h</c> 横）。</summary>
    public string Axis { get; init; } = "v";

    /// <summary>色（#RRGGBB、上から・左から順）。</summary>
    public IReadOnlyList<string> Colors { get; init; } = [];

    /// <summary>色の位置（0〜1、<see cref="Colors"/> と同じ数）。null なら等間隔。</summary>
    public IReadOnlyList<float>? Positions { get; init; }

    /// <summary>
    /// 横（<c>h</c>）のときの色の境目の傾き（度）。正なら境目の上側が右へ倒れる（行の高さの中ほどを軸に傾ける）。0 なら垂直。
    /// </summary>
    public float Tilt { get; init; }

    /// <summary>
    /// 繰り返しの長さ（字の大きさに対する比）。0 より大きければ、行の字の端からこの長さごとに色の並びを繰り返す（色の位置はこの長さに対する比）。
    /// 0 なら繰り返さず、行の字の範囲いっぱいに 1 度だけ移す。
    /// </summary>
    public float Period { get; init; }

    /// <summary>
    /// 色を移す軸の向き（度。0 が右、90 が下）。あれば <see cref="Axis"/> に代えて、字のある範囲の左上の角からこの向きへの射影で色を移し、
    /// <see cref="Period"/>（0 より大きいこと）ごとに繰り返す。色の境目はこの向きに直交する線になる（行・字をまたぐ斜めの縞のグラデーション）。
    /// null なら <see cref="Axis"/> の向き。
    /// </summary>
    public float? Angle { get; init; }

    /// <summary><see cref="Angle"/> の繰り返しを、色の並びを行って戻る形（三角の波）にするか。false なら端で最初の色に戻る（のこぎりの波）。</summary>
    public bool Mirror { get; init; }

    /// <summary><see cref="Angle"/> の繰り返しの始まりのずらし（字の大きさに対する比）。</summary>
    public float Offset { get; init; }

    /// <summary>
    /// 行ごとに色を移すか。縦（<c>v</c>）なら行ごとに親字の上端から下端まで、横（<c>h</c>、繰り返しなし）なら行ごとに親字の左端から右端まで。
    /// false なら全行の字の範囲いっぱいに 1 度だけ。
    /// </summary>
    public bool PerLine { get; init; }

    /// <summary>
    /// 行ごとの色（<see cref="PerLine"/> のとき。1 行目から順の色の並びで、行の数より少なければ繰り返す）。あれば行ごとに
    /// <see cref="Colors"/> に代えてこの色で移す（行によって色の組が違う作品用。色の位置は <see cref="Positions"/> を共通に使う）。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>>? LineColors { get; init; }

    /// <summary>
    /// 画面全体にかかるグラデーションの始点と終点（x0, y0, x1, y1。画面の幅・高さに対する比）。あれば <see cref="Axis"/> などに代えて、
    /// この線分への射影で色を移す（クレジットのまとまり全体に 1 つのグラデーションがかかる作品用。フォントのページの場面の画像だけに効く）。
    /// </summary>
    public IReadOnlyList<float>? SceneLine { get; init; }

    /// <summary>
    /// <see cref="SceneLine"/> を、描く画像の画素の座標に直したもの（x0, y0, x1, y1）。場面の画像を描くときに、置く位置から求めて入れる。
    /// <see cref="Radial"/> のときは (x0, y0) が中心、(x1, y1) までの距離が半径。
    /// </summary>
    public IReadOnlyList<float>? Line { get; init; }

    /// <summary>
    /// 環状に色を移すか。true なら <see cref="Axis"/> などに代えて、字のまとまりの中心から外へ同心円で色を移す（最初の色が中心、最後の色が外）。
    /// まとまりは、各話のサブタイトルの画像ならその画像の字の範囲、フォントのページの場面の画像ならクレジット（役職名と人名）全体。
    /// 半径は、まとまりの中心から外形のいちばん遠い角までの距離に <see cref="Radius"/> を掛けた長さで、その外は最後の色のまま。
    /// </summary>
    public bool Radial { get; init; }

    /// <summary><see cref="Radial"/> の半径の倍率（まとまりの中心からいちばん遠い角までの距離に対する比）。</summary>
    public float Radius { get; init; } = 1f;
}

/// <summary>字の内側の光。字の形をぼかし、輪郭に近いところほど濃く色を載せる。</summary>
public sealed record TelopInnerGlow
{
    /// <summary>光の色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>ぼかし（ガウスぼかしの標準偏差）。</summary>
    public float Blur { get; init; }
}

/// <summary>字の塗りの上の斜線の模様。</summary>
public sealed record TelopStripes
{
    /// <summary>線の色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>線の向き（度、右回り。0 で横線）。</summary>
    public float Angle { get; init; }

    /// <summary>線の間隔（線に直交する向きの周期）。</summary>
    public float Period { get; init; }

    /// <summary>線の太さ。</summary>
    public float Width { get; init; }

    /// <summary>線の位置のずらし。</summary>
    public float Offset { get; init; }

    /// <summary>直交する向き（<see cref="Angle"/> ＋ 90 度）にも同じ線を引いて格子にするか。</summary>
    public bool Cross { get; init; }

    /// <summary>
    /// 線の下に敷く半透明の帯（チェック模様）。線と同じ向き（格子なら両方の向き）に帯を並べ、帯の重なる所はさらに濃くなる。
    /// null なら帯は無い。
    /// </summary>
    public TelopStripeBands? Bands { get; init; }
}

/// <summary>
/// 字の塗りの上に散らすキラキラ。4 つの角の星（アステロイド形）を決まった間隔の格子に置き、星ごとに決まった種でずらす。
/// 長さは字の大きさに対する比。
/// </summary>
public sealed record TelopSparkles
{
    /// <summary>星の色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>星の濃さ（0〜1）。</summary>
    public float Opacity { get; init; } = 1f;

    /// <summary>星の高さ（縦の端から端まで）。</summary>
    public float Size { get; init; }

    /// <summary>星の幅と高さの比（1 で正方、1 より小さければ縦長）。</summary>
    public float Aspect { get; init; } = 1f;

    /// <summary>星の傾き（度。正なら上が右へ、負なら左へ倒れる）。</summary>
    public float Angle { get; init; }

    /// <summary>星を置く格子の間隔。</summary>
    public float Period { get; init; }

    /// <summary>星を格子の点からずらす幅（格子の間隔に対する比、0〜0.5）。</summary>
    public float Jitter { get; init; } = 0.3f;

    /// <summary>星の大きさのばらつき（0〜1。0 ならすべて同じ大きさ）。</summary>
    public float SizeJitter { get; init; } = 0.4f;

    /// <summary>
    /// 星を置く縦の範囲（行の親字の上端を 0、下端を 1 とした from・to）。範囲の外に中心の来る星は置かない。null なら行のどこにでも置く。
    /// </summary>
    public IReadOnlyList<float>? VerticalRange { get; init; }

    /// <summary>格子の点に星を置く割合（0〜1。決まった種で間引く）。1 ならすべての点に置く。</summary>
    public float Density { get; init; } = 1f;

    /// <summary>星ごとの幅のばらつき（0〜1。<see cref="Aspect"/> に掛ける倍率を 1 ± この値の範囲で決まった種で変える）。0 ならそろえる。</summary>
    public float AspectJitter { get; init; }

    /// <summary>星ごとの傾きのばらつき（度。<see cref="Angle"/> に ± この値の範囲で決まった種で足す）。0 ならそろえる。</summary>
    public float AngleJitter { get; init; }
}

/// <summary>
/// 字の塗りに混ぜるまだら模様。決まった種の乱れを格子の点に置いてなめらかにつないだ濃淡（粗い粒と細かい粒の 2 段）で、
/// 塗りの色に <see cref="Color"/> を混ぜる。長さは字の大きさに対する比。
/// </summary>
public sealed record TelopMottle
{
    /// <summary>混ぜる色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#000000";

    /// <summary>いちばん濃い所で混ぜる割合（0〜1）。</summary>
    public float Opacity { get; init; } = 0.3f;

    /// <summary>粗い粒の大きさ（格子の間隔）。細かい粒はその半分。</summary>
    public float Scale { get; init; }

    /// <summary>濃淡のコントラスト（1 でそのまま、大きいほど濃い所と薄い所の差がはっきりする）。</summary>
    public float Contrast { get; init; } = 1f;
}

/// <summary>斜線の下に敷く半透明の帯（チェック模様）。値は字の大きさに対する比。</summary>
public sealed record TelopStripeBands
{
    /// <summary>帯の色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#000000";

    /// <summary>帯の濃さ（0〜1）。2 つの向きの帯が重なる所は 2 回重ねる。</summary>
    public float Opacity { get; init; } = 0.5f;

    /// <summary>帯の間隔（帯に直交する向きの周期）。</summary>
    public float Period { get; init; }

    /// <summary>帯の太さ。</summary>
    public float Width { get; init; }

    /// <summary>帯の位置のずらし。</summary>
    public float Offset { get; init; }
}

/// <summary>
/// 決まった字だけを傾ける指定。字ごとに、その字のベースラインの中央を軸に <see cref="Angle"/> 度だけ回し、
/// <see cref="Dx"/>・<see cref="Dy"/>（字の大きさに対する比。右・下を正）だけずらして置く。字送りは傾けない字と同じ。
/// </summary>
public sealed record TelopCharTilt
{
    /// <summary>傾ける字（この文字列に含まれる字）。</summary>
    public string Chars { get; init; } = "！？!?";

    /// <summary>傾ける角度（度、右回り＝上が右へ倒れるのを正）。「！」「？」が続くところ（まとまり）の字はこの角度。</summary>
    public float Angle { get; init; }

    /// <summary>単独の字（前後に傾ける字が続かないもの）の角度。null なら <see cref="Angle"/>。</summary>
    public float? LoneAngle { get; init; }

    /// <summary>「？」が混ざるまとまり（「！？」など）の字の角度。null なら <see cref="Angle"/>。</summary>
    public float? QuestionRunAngle { get; init; }

    /// <summary>
    /// 傾ける回（シリーズ内話数）の一覧。null ならすべての回とフォントのページの見本。一覧があれば、載っていない回の各話のテロップ画像は
    /// 字を傾けない（見本は傾ける）。
    /// </summary>
    public IReadOnlyList<int>? Episodes { get; init; }

    /// <summary>横のずれ（字の大きさに対する比、右を正）。</summary>
    public float Dx { get; init; }

    /// <summary>縦のずれ（字の大きさに対する比、下を正）。</summary>
    public float Dy { get; init; }
}

/// <summary>字とフチをまとめた形をぼかした光彩。</summary>
public sealed record TelopGlow
{
    /// <summary>光彩の色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#FFFFFF";

    /// <summary>ぼかす前に形を太らせる幅。</summary>
    public float Width { get; init; }

    /// <summary>ぼかし（ガウスぼかしの標準偏差）。</summary>
    public float Blur { get; init; }

    /// <summary>濃さ（0〜1）。</summary>
    public float Opacity { get; init; } = 1f;

    /// <summary>
    /// true ならフチの上・字の下に重ねる（形は字と不透明なフチ、影にはかからない）。
    /// false なら字の上も含めていちばん上に重ねる（形は字・フチ・影のすべて）。
    /// </summary>
    public bool BehindFill { get; init; }
}

/// <summary>
/// テロップの装飾の 1 層。種類は影（<c>shadow</c>）・フチ（<c>outline</c>）・面取り（<c>bevel</c>）の 3 つで、光彩はずれ 0 でぼかした影、
/// ぼかしたフチはフチの <see cref="Blur"/> で表す。値はすべて字の大きさに対する比。
/// </summary>
public sealed record TelopDecorationLayer
{
    /// <summary>影の層の種類名。</summary>
    public const string KindShadow = "shadow";

    /// <summary>フチの層の種類名。</summary>
    public const string KindOutline = "outline";

    /// <summary>
    /// 面取りの層の種類名。字＋フチの外形（<see cref="OverFill"/> なら字そのもの）に、光の向き（<see cref="Angle"/>）に面した側を
    /// 明るい色（<see cref="Highlight"/>）、反対側を暗い色（<see cref="Shade"/>）で塗って立体に見せる。
    /// </summary>
    public const string KindBevel = "bevel";

    /// <summary>層の種類（<c>shadow</c> / <c>outline</c>）。</summary>
    public string Kind { get; init; } = KindOutline;

    /// <summary>層の色（#RRGGBB）。</summary>
    public string Color { get; init; } = "#000000";

    /// <summary>層の濃さ（0〜1）。</summary>
    public float Opacity { get; init; } = 1f;

    /// <summary>フチの太さ（字の輪郭から外へ）。フチの層だけ。2 重フチは、外側のフチを太い値で下に、内側のフチを細い値で上に重ねる。</summary>
    public float Width { get; init; }

    /// <summary>影の横のずれ（右を正）。影の層だけ。</summary>
    public float Dx { get; init; }

    /// <summary>影の縦のずれ（下を正）。影の層だけ。</summary>
    public float Dy { get; init; }

    /// <summary>ぼかし（ガウスぼかしの標準偏差）。0 ならぼかさない。</summary>
    public float Blur { get; init; }

    /// <summary>
    /// 影の形の太さ（字の輪郭から外へ太らせる幅）。影の層だけ。null なら、いちばん太いフチの外形をそのまま影にする。
    /// ぼかしたフチの下にくっきりした影を敷く体裁では、フチのぼかしを掛けない字の形を太らせて影にするのでここに値を持つ。
    /// </summary>
    public float? Spread { get; init; }

    /// <summary>影の形を、<see cref="Spread"/> に加えて左右だけ余分に広げる幅（字の大きさに対する比）。横に長い影・帯に使う。null なら広げない。</summary>
    public float? SpreadX { get; init; }

    /// <summary>
    /// 影の形を外周の輪だけにする幅（字の大きさに対する比）。影の層だけ。影の形から、それをこの幅だけ細らせた形を抜いて残る縁だけを塗る。
    /// 半透明の帯の外周に暗い縁を引くときに使う（塗りつぶした暗い影を帯の下に敷くと、半透明の帯の中に透けてしまうため）。null なら輪にしない。
    /// </summary>
    public float? Ring { get; init; }

    /// <summary>面取りの暗い側だけの濃さ（0〜1）。null なら <see cref="Opacity"/>。0 なら明るい側（光沢）だけを描く。</summary>
    public float? ShadeOpacity { get; init; }

    /// <summary>
    /// 面取りの面のでこぼこの強さ（面取りの山に足す乱れの大きさ。字の大きさに対する比）。null・0 なら平らな面。
    /// 乱れの模様は字の位置によらず決まった種から作るので、同じ画像は何度描いても同じになる。
    /// </summary>
    public float? Noise { get; init; }

    /// <summary>面取りの面のでこぼこの細かさ（乱れをぼかす標準偏差。字の大きさに対する比）。null なら 0.02。</summary>
    public float? NoiseScale { get; init; }

    /// <summary>フチの内側の色（#RRGGBB）。フチの層だけ。あれば、字の輪郭のこの色からフチの外の縁の <see cref="Color"/> へ色を移す。</summary>
    public string? InnerColor { get; init; }

    /// <summary>層の色のグラデーション（字の行の範囲に沿う）。あれば <see cref="Color"/> の代わりに使う。</summary>
    public TelopGradient? Gradient { get; init; }

    /// <summary>面取りの明るい側の色（#RRGGBB）。面取りの層だけ。</summary>
    public string? Highlight { get; init; }

    /// <summary>面取りの暗い側の色（#RRGGBB）。面取りの層だけ。</summary>
    public string? Shade { get; init; }

    /// <summary>面取りの光の向き（度。0 が右、90 が下。左上から当てるなら 225）。面取りの層だけ。</summary>
    public float Angle { get; init; } = 225f;

    /// <summary>面取りの強さ（傾きを明暗の濃さに変える倍率）。面取りの層だけ。</summary>
    public float Strength { get; init; } = 1f;

    /// <summary>
    /// 面取りの暗い側を、決まった色（<see cref="Shade"/>）ではなく、下の塗りの色をこの割合だけ暗くした色にする（0〜1。字の上の面取りだけ）。
    /// 塗りにグラデーションがあれば、側面もその色に沿って暗くなる。null なら <see cref="Shade"/> の色。
    /// </summary>
    public float? ShadeFromFill { get; init; }

    /// <summary>
    /// 明るい側を、色を混ぜずに光として足す（加算。字の上の面取りだけ）。色のついた光を別の向きから当てる（右下から青い光など）ときに使う。
    /// false なら明るい側の色を濃さの割合で混ぜる。
    /// </summary>
    public bool Additive { get; init; }

    /// <summary>
    /// 面取りの断面を台形にするか。true なら縁から <see cref="Width"/> まで一定の傾きで上がり、その内側は平ら（斜面は輪郭に沿った帯で、向きごとに明るさが一様）。
    /// false なら外形をぼかした山（丸い断面）。面取りの層だけ。
    /// </summary>
    public bool Chamfer { get; init; }

    /// <summary>true なら面取りを字の上に重ねる（字そのものの浮き彫り）。false なら字＋フチの外形に、字の下で重ねる。</summary>
    public bool OverFill { get; init; }
}
