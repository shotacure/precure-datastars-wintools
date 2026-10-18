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

    /// <summary>字の塗りを太らせる幅（字の輪郭から外へ）。描く書体の字が放送より細いときに使う。0 なら太らせない。</summary>
    public float FillGrow { get; init; }

    /// <summary>字の内側の光（字の輪郭から内側へにじむ色）。null なら無し。</summary>
    public TelopInnerGlow? InnerGlow { get; init; }

    /// <summary>字の塗りの上の斜線の模様。null なら無し。</summary>
    public TelopStripes? Stripes { get; init; }

    /// <summary>振り仮名だけの塗りの色（#RRGGBB）。null なら親字と同じ塗り。</summary>
    public string? RubyFill { get; init; }

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
        FillGradient = FillGradient is null ? null : FillGradient with { Period = FillGradient.Period * k },
        InnerGlow = InnerGlow is null ? null : InnerGlow with { Blur = InnerGlow.Blur * k },
        Stripes = Stripes is null ? null : Stripes with { Period = Stripes.Period * k, Width = Stripes.Width * k, Offset = Stripes.Offset * k },
        Glow = Glow is null ? null : Glow with { Width = Glow.Width * k, Blur = Glow.Blur * k },
        CharTilt = CharTilt is null ? null : CharTilt with { Dx = CharTilt.Dx * k, Dy = CharTilt.Dy * k },
        Layers = Layers.Select(l => l with
        {
            Width = l.Width * k, Dx = l.Dx * k, Dy = l.Dy * k, Blur = l.Blur * k, Spread = l.Spread * k,
            Gradient = l.Gradient is null ? null : l.Gradient with { Period = l.Gradient.Period * k }
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
            || PunctLoneCenter != other.PunctLoneCenter) return false;
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
        FillGradient is not null || FillChars is { Count: > 0 } || FillGrow > 0f || InnerGlow is not null || Stripes is not null
        || RubyFill is not null || Glow is not null
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

    /// <summary>縦（<c>v</c>）のとき、行ごとに親字の上端から下端までで色を移すか。false なら全行の字の範囲いっぱいに 1 度だけ。</summary>
    public bool PerLine { get; init; }
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
}

/// <summary>
/// 決まった字だけを傾ける指定。字ごとに、その字のベースラインの中央を軸に <see cref="Angle"/> 度だけ回し、
/// <see cref="Dx"/>・<see cref="Dy"/>（字の大きさに対する比。右・下を正）だけずらして置く。字送りは傾けない字と同じ。
/// </summary>
public sealed record TelopCharTilt
{
    /// <summary>傾ける字（この文字列に含まれる字）。</summary>
    public string Chars { get; init; } = "！？!?";

    /// <summary>傾ける角度（度、右回り＝上が右へ倒れるのを正）。</summary>
    public float Angle { get; init; }

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

    /// <summary>true なら面取りを字の上に重ねる（字そのものの浮き彫り）。false なら字＋フチの外形に、字の下で重ねる。</summary>
    public bool OverFill { get; init; }
}
