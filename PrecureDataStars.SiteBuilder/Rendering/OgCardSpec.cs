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

    /// <summary>見出しが空のカードは意味を成さないため、描画対象として妥当かを判定する。</summary>
    public bool IsRenderable => !string.IsNullOrWhiteSpace(Title);

    /// <summary>高密度の組み方を使うか（識別子・バッジ・帯グラフ・事実行のいずれかを持つか）。</summary>
    public bool IsDense =>
        !string.IsNullOrWhiteSpace(Headline) || Badges.Count > 0 || Bar.Count > 0
        || InlineFacts.Count > 0 || Facts.Count > 0;
}

/// <summary>
/// 角丸バッジ 1 個。ラベルを小さくアクセント色で、値を一回り大きく本文色で並べて描く。
/// 「通算 / 1話」のように、意味と数を対にして見せることを想定した組。
/// </summary>
/// <param name="Label">数の意味（"通算" / "放送" など）。</param>
/// <param name="Value">数そのもの（"663話" / "682回" など）。</param>
public sealed record OgCardBadge(string Label, string Value);

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
/// サイト共通のカバレッジ表記（「YYYY年M月D日現在 『○○』第N話時点の情報を表示しています」）を、
/// カードの狭い一行に収まる長さへ詰める。カードは前置き行の右端に置くため、
/// 文末の説明句を落として「〜時点」までにする。
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
        _ => ""
    };
}
