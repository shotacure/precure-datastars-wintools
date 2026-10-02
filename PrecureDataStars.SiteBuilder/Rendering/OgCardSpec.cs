namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// OGP カード画像（1200×630）に載せる内容一式。
/// 各 Generator がページ種別に応じて組み立て、<see cref="LayoutModel.OgCard"/> に載せて渡す。
/// 画像の意匠（配色・書体・余白）は <see cref="OgCardRenderer"/> 側が一手に持ち、本型は中身だけを運ぶ
/// （カードのデザインを変えるときに Generator 側を触らずに済む分担）。
///
/// <para>
/// カードは中身に応じて 2 通りの組み方になる：
/// <list type="bullet">
///   <item><description><b>標準</b> — 見出しと数行のメタだけを大きく置く。人物・企業・楽曲など。</description></item>
///   <item><description><b>高密度</b> — <see cref="Bar"/> / <see cref="Badges"/> / <see cref="Facts"/> /
///     <see cref="InlineFacts"/> のいずれかを持つ場合。識別子・バッジ・帯グラフ・事実行を積み上げる。
///     エピソードのように「このサイトにしか無い情報」をカード 1 枚で見せたいページで使う。</description></item>
/// </list>
/// </para>
/// </summary>
/// <param name="Kicker">
/// 最上段の左に小さく出す前置き（アクセント色）。所属シリーズやページ種別（『ふたりはプリキュア』など）。
/// 空文字なら描画しない。
/// </param>
/// <param name="Title">
/// カードの主役となる見出し。長い場合は <see cref="OgCardRenderer"/> が自動で
/// 字送り幅に合わせて折り返し、規定行数を超える分は省略記号で切り詰める。
/// </param>
/// <param name="Subtitle">
/// 見出しの下に一回り小さく出す補助行（キャラクターの変身前名義、楽曲の歌唱者など）。空文字なら描画しない。
/// 標準レイアウトでのみ使う。
/// </param>
/// <param name="MetaLeft">
/// 左寄せで添えるメタ情報。空なら描画しない。
/// 標準レイアウトではカード下部に、高密度レイアウトでは数（<see cref="Badges"/>）の直下に置く。
/// 数の意味を限定する但し書き（クレジット収録範囲など）を載せる場所。
/// </param>
/// <param name="MetaRight">フッタ右端のメタ情報。空なら描画しない。</param>
public sealed record OgCardSpec(
    string Kicker,
    string Title,
    string Subtitle = "",
    string MetaLeft = "",
    string MetaRight = "")
{
    /// <summary>
    /// 最上段の右端に添える補助情報（放送日など）。<see cref="Kicker"/> と同じ行に右寄せで置く。
    /// 空文字なら描画しない。
    /// </summary>
    public string KickerRight { get; init; } = "";

    /// <summary>
    /// ルビ付きの見出し（<c>&lt;ruby&gt;漢&lt;rt&gt;かん&lt;/rt&gt;&lt;/ruby&gt;</c> 形式の HTML）。
    /// 非空ならこちらを解釈して振り仮名つきで組み、空なら <see cref="Title"/> をそのまま組む。
    /// サイト本体がエピソードのサブタイトルをルビ付きで見せているので、カードでも同じ読みを添える。
    /// </summary>
    public string TitleRubyHtml { get; init; } = "";

    /// <summary>
    /// サイトのヒーロー（ホームの大見出し）と同じ声で組むか。
    /// true にすると見出しとタグラインをブランド書体（Kiwi Maru）の濃ピンクで描く。
    /// サイトはこの組み方をホームのヒーローだけに使っているので、カードでもホームに限定する。
    /// </summary>
    public bool HeroVoice { get; init; }

    /// <summary>
    /// ヒーロー調のカードで、見出しの下に大きく組む言葉（タグライン）。改行は "\n" で指定する。
    /// <see cref="StatementFontFamily"/> の書体（無ければ透かしの書体）を機械的な斜体にして、数を並べる代わりに置く。
    /// </summary>
    public string Statement { get; init; } = "";

    /// <summary>
    /// <see cref="Statement"/> を組む書体の名前（「FOT-マティスえれがんと Pro EB」のように重さまで含む Windows の書体名）。
    /// 空なら透かしの書体。見つからなければ透かしの書体で描き、警告になる。
    /// </summary>
    public string StatementFontFamily { get; init; } = "";

    /// <summary>
    /// 見出しの上に大きく置く識別子（「第1話」など）。カードの中で最初に目に入る要素として、
    /// ブランド書体・本文色で見出しに次ぐ大きさで描く。空文字なら段ごと詰める。
    /// </summary>
    public string Headline { get; init; } = "";

    /// <summary>
    /// <see cref="Headline"/> の直下に並べる角丸バッジ。通算話数・通算放送回数のように
    /// 「数として見せたい事実」を独立した粒として置くための枠。空なら段ごと詰める。
    /// 横幅に収まらないぶんは末尾から捨てる。
    /// </summary>
    public IReadOnlyList<OgCardBadge> Badges { get; init; } = Array.Empty<OgCardBadge>();

    /// <summary>
    /// 尺構成の帯グラフ。エピソードのフォーマット（アバン / OP / 各パート / CM / ED / 予告）を
    /// そのまま横帯で見せるための入力で、サイト本体のフォーマット表と同じ配色・同じ比率で描く。
    /// </summary>
    public IReadOnlyList<OgCardBarSegment> Bar { get; init; } = Array.Empty<OgCardBarSegment>();

    /// <summary>帯グラフの右下に添える総尺ラベル（"本放送 28:45" など）。空なら描画しない。</summary>
    public string BarTotalLabel { get; init; } = "";

    /// <summary>
    /// 帯グラフの左下に添える尺の凡例（"アバン 1:17 ／ OP 1:15 ／ A 9:09 …" など）。
    /// 幅の狭い区画は帯の中にラベルを置けないため、構成と尺はこの行で読ませる。空なら描画しない。
    /// </summary>
    public string BarCaption { get; init; } = "";

    /// <summary>
    /// ラベルと値の組を 1 行に流し込むファクト（スタッフの「役職＋人名」など）。
    /// ラベルをアクセント色、値を本文色で交互に描き、幅に応じて折り返す。
    /// 役職名と人名が色で分かれることで、羅列ではなく表として読める状態を作る。
    /// </summary>
    public IReadOnlyList<OgCardFactLine> InlineFacts { get; init; } = Array.Empty<OgCardFactLine>();

    /// <summary>
    /// 1 行 1 項目で積むファクト行。<see cref="InlineFacts"/> と違い項目ごとに改行する。
    /// 値が長くて 1 行に流し込めない種類の情報に使う。
    /// </summary>
    public IReadOnlyList<OgCardFactLine> Facts { get; init; } = Array.Empty<OgCardFactLine>();

    /// <summary>
    /// 左端の色帯の色（<c>"#3370aa"</c> 形式）。カードの種別を色で見分けるためのもので、
    /// エピソード・シリーズ・統計はピンク、人物は青、声優は緑、キャラクターはイメージカラー、音楽は紫、書籍は橙。
    /// 空ならアクセントのピンク。透かしと年表の地色にも同じ色を使う。
    /// </summary>
    public string BandColorHex { get; init; } = "";

    /// <summary>
    /// 右上に薄く大きく置く透かしの文字。縮小表示でも「何のカードか」が形で伝わるようにする。
    /// エピソードは「第12話」、シリーズは放送開始年、人物は主な役職、キャラクターと歌は出身シリーズの作品名、
    /// 記念日は月日。空なら描かない。
    /// </summary>
    public string Watermark { get; init; } = "";

    /// <summary>
    /// 透かしの脇（左）に一回り小さく添える文字。エピソードの作品名（略さず、切れ目で折る）に使う。空なら描かない。
    /// </summary>
    public string WatermarkAside { get; init; } = "";

    /// <summary>
    /// 関わった期間の年表（人物・キャラクターのプロフィール組みでのみ使う）。
    /// 非空ならプロフィール組みになり、残った余白いっぱいに年表を描く。
    /// </summary>
    public IReadOnlyList<OgCardTimelineSegment> Timeline { get; init; } = Array.Empty<OgCardTimelineSegment>();

    /// <summary>年表の横軸の始まりと終わり。既定はシリーズの始まり（2004 年 2 月 1 日）から今日まで。</summary>
    public DateOnly TimelineStart { get; init; } = new(2004, 2, 1);
    public DateOnly TimelineEnd { get; init; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// プロフィール組みの下端に据える事実行（初参加・初登場など）。1 行 1 項目。
    /// 非空ならプロフィール組みになる。
    /// </summary>
    public IReadOnlyList<OgCardFactLine> FootFacts { get; init; } = Array.Empty<OgCardFactLine>();

    /// <summary>プロフィール組みでの数の拡大率。0 なら既定（1.8 倍）。</summary>
    public float StatScale { get; init; }

    /// <summary>
    /// 見出しに使う書体の名前（<c>series.font_subtitle</c>。「FOT-ハミング ProN B」のように Windows に見える書体名）。
    /// エピソードのサブタイトルを、その作品の本編のテロップと同じ書体で組むためのもので、
    /// インストールされていなければ既定の見出し書体で組んで警告を出す。空なら既定の見出し書体。
    /// </summary>
    public string TitleFontFamily { get; init; } = "";

    /// <summary>見出しが空のカードは意味を成さないため、描画対象として妥当かを判定する。</summary>
    public bool IsRenderable => !string.IsNullOrWhiteSpace(Title);

    /// <summary>プロフィールの組み方（年表か下端の事実行を持つ人物・キャラクターのカード）か。</summary>
    public bool IsProfile => Timeline.Count > 0 || FootFacts.Count > 0;

    /// <summary>
    /// 数だけのカード（索引・統計・ランディングなど）か。数のバッジを持ち、事実行・帯グラフ・年表を持たず、ヒーロー調でもない。
    /// 数を大きく組み、説明文を添え、全体を上下中央に据える対象。
    /// </summary>
    public bool IsNumbersOnly =>
        !HeroVoice && !IsProfile && Badges.Count > 0 && Facts.Count == 0 && InlineFacts.Count == 0 && Bar.Count == 0;

    /// <summary>
    /// 高密度の組み方を使うか（識別子・バッジ・帯グラフ・事実行のいずれかを持つか）。
    /// 右上の透かしや見出しの書体指定を持つカードも含める。これらは高密度側の疎な組み方（見出しと日付だけを大きく組む）が
    /// 前提で、標準の組み方は見出しの書体指定を無視するため（クレジット未収録で事実行が無い話も、作品の書体で組む）。
    /// ヒーロー調（ホーム）と、タグラインの言葉を持つカードも高密度側で組む。
    /// </summary>
    public bool IsDense =>
        !string.IsNullOrWhiteSpace(Headline) || Badges.Count > 0 || Bar.Count > 0
        || InlineFacts.Count > 0 || Facts.Count > 0 || IsProfile
        || !string.IsNullOrWhiteSpace(Watermark) || !string.IsNullOrWhiteSpace(TitleFontFamily)
        || HeroVoice || !string.IsNullOrWhiteSpace(Statement);
}

/// <summary>
/// 年表の区間 1 つ。<paramref name="Start"/> と <paramref name="End"/> が同じなら点（映画など）として描く。
/// </summary>
/// <param name="Start">区間の始まり。</param>
/// <param name="End">区間の終わり。</param>
/// <param name="ColorHex">塗り色（役職の色など）。空なら色帯と同じ色。</param>
public sealed record OgCardTimelineSegment(DateOnly Start, DateOnly End, string ColorHex = "");

/// <summary>
/// 角丸バッジ 1 個。ラベルを小さくアクセント色で、値を一回り大きく本文色で並べて描く。
/// 「通算 / 1話」のように、意味と数を対にして見せることを想定した組。
/// </summary>
/// <param name="Label">数の意味（"通算" / "放送" など）。</param>
/// <param name="Value">数そのもの（"663話" / "682回" など）。</param>
public sealed record OgCardBadge(string Label, string Value)
{
    /// <summary>
    /// 値の末尾に一回り小さく添える端数（".04" など）。合計の尺のように、桁は要るが
    /// 主役ではない部分を、数の大きさを損なわずに残すために使う。空なら描かない。
    /// </summary>
    public string Fraction { get; init; } = "";

    /// <summary>
    /// 値のあとにラベルと同じ体裁で添える文字（閉じ括弧など）。空なら描かない。
    /// </summary>
    public string Tail { get; init; } = "";

    /// <summary>
    /// この組から新しい行を起こすか。数の並びを意味のまとまりで折り返すために使う
    /// （幅が余っていても改行する。幅が足りないときの自動折り返しとは別）。
    /// </summary>
    public bool NewLine { get; init; }
}

/// <summary>
/// 帯グラフを構成する 1 区画。幅は <see cref="Seconds"/> の比で決まる。
/// </summary>
/// <param name="Seconds">区画の尺（秒）。幅の比重に使う。</param>
/// <param name="Label">区画内に出す短縮ラベル（"OP" / "Aパート" など）。幅が足りなければ描画を省く。</param>
/// <param name="ColorHex">塗り色（"#aacdf2" 形式）。サイトの <c>fmt-p-*</c> パレットと同値にする。</param>
/// <param name="Hatched">CM 枠のように斜線ハッチで表す区画なら true。</param>
public sealed record OgCardBarSegment(int Seconds, string Label, string ColorHex, bool Hatched = false);

/// <summary>
/// ファクト 1 項目。<paramref name="Label"/> をアクセント色の小見出しに、
/// <paramref name="Text"/> を本文色で続けて描く。
/// </summary>
/// <param name="Label">項目名（"脚本" / "作画監督" など）。空なら本文のみを描く。</param>
/// <param name="Text">値。1 行に収まらない場合は末尾を省略記号で切り詰める。</param>
public sealed record OgCardFactLine(string Label, string Text)
{
    /// <summary>
    /// 項目名の色（<c>"#3b82c4"</c> 形式）。空なら補助色で描く。
    /// 役職名にはサイトの役職バッジと同じ配色を当てて、項目の切れ目が色で読み取れるようにする
    /// （<see cref="OgRolePalette"/> 参照）。
    /// </summary>
    public string LabelColorHex { get; init; } = "";

    /// <summary>
    /// 項目名を色の異なる断片に分けて描くための指定。
    /// 「絵コンテ・演出」のように 1 つの見出しが複数の役職を束ねている場合、
    /// 役職ごとに色を変えないと束ねた全体が無彩色に落ちて他の項目から浮いてしまう。
    /// 非空ならこちらを使い、<see cref="Label"/> / <see cref="LabelColorHex"/> は無視する。
    /// </summary>
    public IReadOnlyList<OgCardLabelPart> LabelParts { get; init; } = Array.Empty<OgCardLabelPart>();

    /// <summary>
    /// 値の続き（2 行目）。非空なら値の左端に揃えた位置へ字下げして次の行に置く。
    /// 「作品名」と「話数・サブタイトル」のように、1 行に押し込むと切れてしまう情報を
    /// 2 段に分けて全部見せるための枠。1 行 1 項目で積む <see cref="OgCardSpec.Facts"/> でのみ使う。
    /// </summary>
    public string SubText { get; init; } = "";
}

/// <summary>
/// 色分けした項目名の断片 1 つ。区切り文字（「・」など）は色を持たない断片として並べる。
/// </summary>
/// <param name="Text">断片の文字列。</param>
/// <param name="ColorHex">色（<c>"#2ea7ad"</c> 形式）。空なら補助色。</param>
public sealed record OgCardLabelPart(string Text, string ColorHex);

/// <summary>
/// サイト共通のカバレッジ表記（「『○○』第N話(YYYY.M.D)時点」）を、カードの狭い一行に収まる長さへ詰める。
/// カードは前置き行の右端に置くため、「〜時点」より後ろに続く文があれば落とす。
/// </summary>
public static class OgCoverageLabel
{
    public static string Compact(string coverageLabel)
    {
        if (string.IsNullOrWhiteSpace(coverageLabel)) return "";
        int cut = coverageLabel.IndexOf("時点", StringComparison.Ordinal);
        return cut < 0 ? coverageLabel : coverageLabel[..(cut + 2)];
    }
}

/// <summary>
/// 役職コードから項目名の色を引く。サイトの <c>.role-badge[data-role-code]</c> と同じ配色にして、
/// カードとページで同じ役職が同じ色に見える状態を保つ。
/// 未マッピングの役職は空文字（＝カード側で補助色にフォールバック）。
/// </summary>
public static class OgRolePalette
{
    public static string ColorFor(string roleCode) => roleCode switch
    {
        "PRODUCER" => "#7e57c2",
        "SERIES_COMPOSITION" or "SCREENPLAY" => "#3b82c4",
        "STORYBOARD" => "#2ea7ad",
        "SERIES_DIRECTOR" or "EPISODE_DIRECTOR" or "DIRECTOR" => "#e91e63",
        "CHARACTER_DESIGN" or "ANIMATION_DIRECTOR" => "#4ca36b",
        "ART_DESIGN" or "ART_DIRECTOR" or "ART_DIRECTOR_TV" => "#d4a017",
        // 楽曲の役職（サイトの楽曲詳細のバッジと同じ 4 色）。
        "LYRICS" => "#c0354c",
        "COMPOSITION" => "#a17821",
        "ARRANGEMENT" => "#3c823c",
        "VOCALS" or "BACKING_VOCALS" or "DIALOGUE" => "#3b56b8",
        _ => ""
    };
}

/// <summary>
/// カードの種別ごとの色（左端の色帯・透かし・年表の地色）。サイトのアクセント色の系統からとる。
/// エピソード・シリーズ・統計はピンク、人物は青、声優は緑、プリキュア以外のキャラクターは藤色、音楽は紫、書籍は橙。
/// プリキュアはこの表ではなく、そのプリキュアのイメージカラー（<c>precures.key_color</c>）を使う。
/// </summary>
public static class OgCardColors
{
    public const string Episode = "#e91e63";
    public const string Staff = "#3370aa";
    public const string VoiceActor = "#4ca36b";
    public const string Character = "#9b7fd4";
    public const string Music = "#7e57c2";
    public const string Book = "#e8833a";

    /// <summary>
    /// プリキュアのイメージカラー（<c>precures.key_color</c>）を、色帯や透かしに使える濃さへ寄せる。
    /// キュアホワイトの白に近い水色のように明るすぎる色は、淡い地の上では見えないので、
    /// 色相を保ったまま明度を落とし、彩度が薄ければ少し上げる。読めなければ既定のキャラクターの色。
    /// </summary>
    public static string ForKeyColor(string? keyColorHex)
    {
        if (string.IsNullOrWhiteSpace(keyColorHex) || !SkiaSharp.SKColor.TryParse(keyColorHex, out var color))
            return Character;
        color.ToHsl(out float h, out float s, out float l);
        if (l > 55f) l = 50f;
        if (l < 22f) l = 30f;
        if (s < 45f) s = 45f;
        return SkiaSharp.SKColor.FromHsl(h, s, l).ToString();
    }
}
