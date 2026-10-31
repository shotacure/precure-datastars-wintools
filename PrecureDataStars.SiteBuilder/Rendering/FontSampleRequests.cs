using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Rendering;

/// <summary>
/// フォントのページ（<c>/fonts/</c>・<c>/fonts/{書体}/</c>）に並べる画像の材料。
/// <list type="bullet">
///   <item>場面の画像：作品ごとに、OP（「原作　東堂いづみ」と歌詞）・ED（時期ごと。「原画」と 6 人の人名と歌詞）・サブタイトルの 3 枚
///     （ED が前期・後期で変わる作品は 4 枚）。放送の画面の大きさ（16:9 は 1920×1080、2006 年までの 4:3 は 1440×1080）の灰色の市松模様の地に、
///     作品の書体の表の字の大きさ・装飾・組み方（<c>layout</c>）で置く。組み方の無い行は決まった位置に置く。</item>
///   <item>書体見本：書体（ウェイト）ごとに 1 枚。白地に黒い字の正方形に「愛」「永」と、サブタイトルで多く使われた字を並べる。</item>
/// </list>
/// 歌詞の見本の文は「女の子だって　暴れたい！」で、空白の空きは測った値（無ければ半角 2 つ分）。同じ時期の歌詞のルビの行があれば
/// 「女」「子」「暴」にルビを振る。サブタイトルの見本は「女の子だって暴れたい！」（空白なし）にルビを振り、2 行の話が半分を超える作品は
/// 2 行目に「極上の映像エンタメはこれだ！」（「極」「上」「映」「像」にもルビ）を足す。作品ごとに見本の文を決めてあれば、その文で描く（<see cref="SubtitleSampleByYear"/>）。
/// ルビは行頭でも親字の中央に載せる。本放送だけの行は見本を作らない。
/// </summary>
public static class FontSampleRequests
{
    /// <summary>歌詞の見本の文（ルビ付き）。空白の空きは組み方の値で決める。</summary>
    private const string LyricsRubyHtml = "<ruby>女<rt>おんな</rt></ruby>の<ruby>子<rt>こ</rt></ruby>だって　<ruby>暴<rt>あば</rt></ruby>れたい！";

    /// <summary>歌詞の見本の文（ルビの行が無い時期）。</summary>
    private const string LyricsPlain = "女の子だって　暴れたい！";

    /// <summary>サブタイトルの見本の文（ルビ付き、空白なし）。</summary>
    private const string SubtitleRubyHtml = "<ruby>女<rt>おんな</rt></ruby>の<ruby>子<rt>こ</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたい！";

    /// <summary>
    /// サブタイトルが 2 行の話が優勢な作品の見本の文（2 行目も『ふたりはプリキュア』企画書のキャッチフレーズを短くしたもの）。
    /// 2 行で組む作品は 1 行の見本では行送りが見えないので、2 行で描く。
    /// </summary>
    private const string SubtitleTwoLineRubyHtml = SubtitleRubyHtml + "<br>" + SubtitleSecondLineHead + "これだ！";

    /// <summary>
    /// 2 行の見本の短い文（「女の子だって／暴れたい！」）。本編のサブタイトルに 2 行目の見本の文ほど長い行の無い作品は、
    /// 長い行で字を大きく見せないよう、こちらで描く。
    /// </summary>
    private const string SubtitleShortTwoLineRubyHtml = "<ruby>女<rt>おんな</rt></ruby>の<ruby>子<rt>こ</rt></ruby>だって<br><ruby>暴<rt>あば</rt></ruby>れたい！";

    /// <summary>2 行目の見本の文の字数（「極上の映像エンタメはこれだ！」）。</summary>
    private const int SubtitleSecondLineLength = 14;

    /// <summary>2 行目の見本の文の頭（「極上の映像エンタメは」、ルビ付き）。</summary>
    private const string SubtitleSecondLineHead = "<ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>の<ruby>映<rt>えい</rt></ruby><ruby>像<rt>ぞう</rt></ruby>エンタメは";

    /// <summary>
    /// 見本の文を替える作品（放送開始の年 → ルビ付きの文）。サブタイトルに決まった語り口のある作品は、見本も同じ語り口にする
    /// （ハートキャッチは 2 行目の結びが「〜んです！！」で「とまどいのゆりさん！／高校生だって暴れたいんです！！」、スイートは 1 行目が擬音で「〜ニャ♪」で「ニャプニャプ〜！／音符だって暴れたいニャ♪」、スマイルは「れいかの道！／生徒会長だって暴れたい！」、ドキドキは 2 行目が登場人物の名前で始まる「ツバメだって暴れたい！／六花だんぜん悩む！」、ハピネスチャージは「恋愛禁止！？／誠司だって暴れたい！」、
    /// スプラッシュスターは「転校生だって絶好調なり！／満と薫はプリキュア！」、Yes！5 は「生徒会長だって暴れたい！」、5GoGo！は「ブンビーだって暴れたい！？」、フレッシュは「せつなとラブ／お母さんだって暴れたい！」、Go！プリンセスは「プリンセスだって暴れたい！／極上のドーナッツはこれだ！」、魔法つかいは「魔法つかいだって暴れたい！／極上の冷凍みかんはこれだ！」、アラモードは「お嬢さまだって暴れたい！／極上のアニマルスイーツ！」、HUGっとは「社長だって暴れたい！／極上のソウルがシャウトするのです！」、スター☆トゥインクルは「AIだって暴れたい！／極上の星空はキラやば〜☆」、ヒーリングっどは「ラテ様だって暴れたい！／極上のお手当てはこれだ！」、トロピカル～ジュは「生徒会長だってトロピカれ！／今一番大事なことはこれだ！」、デリシャスパーティは「ブラペだって暴れたい！／極上のデリシャスマイルはこれだ！」、ひろがるスカイは「ヒーローだって暴れたい！／アゲアゲな映像エンタメはこれだ！」、わんだふるは 1 行の話ばかりなので「メェメェだって暴れたい！」、
    /// キミとアイドルは「キミと一緒に暴れたい！／極上のキラッキランラン♪」、名探偵は 1 行の話が多く行の中に読点を使うので「名探偵だって、はなまる暴れたい！」）。1 行の話が多い作品でも、ここにあれば 2 行で描く。
    /// </summary>
    private static readonly Dictionary<int, string> SubtitleSampleByYear = new()
    {
        [2006] = "<ruby>転<rt>てん</rt></ruby><ruby>校<rt>こう</rt></ruby><ruby>生<rt>せい</rt></ruby>だって<ruby>絶<rt>ぜっ</rt></ruby><ruby>好<rt>こう</rt></ruby><ruby>調<rt>ちょう</rt></ruby>なり！<br><ruby>満<rt>みちる</rt></ruby>と<ruby>薫<rt>かおる</rt></ruby>はプリキュア！",
        [2007] = "<ruby>生<rt>せい</rt></ruby><ruby>徒<rt>と</rt></ruby><ruby>会<rt>かい</rt></ruby><ruby>長<rt>ちょう</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたい！",
        [2008] = "ブンビーだって<ruby>暴<rt>あば</rt></ruby>れたい！？",
        [2009] = "せつなとラブ<br>お<ruby>母<rt>かあ</rt></ruby>さんだって<ruby>暴<rt>あば</rt></ruby>れたい！",
        [2010] = "とまどいのゆりさん！<br><ruby>高<rt>こう</rt></ruby><ruby>校<rt>こう</rt></ruby><ruby>生<rt>せい</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたいんです！！",
        [2011] = "ニャプニャプ〜！<br><ruby>音<rt>おん</rt></ruby><ruby>符<rt>ぷ</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたいニャ♪",
        [2012] = "れいかの<ruby>道<rt>みち</rt></ruby>！<br><ruby>生<rt>せい</rt></ruby><ruby>徒<rt>と</rt></ruby><ruby>会<rt>かい</rt></ruby><ruby>長<rt>ちょう</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたい！",
        [2013] = "ツバメだって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>六花<rt>りっか</rt></ruby>だんぜん<ruby>悩<rt>なや</rt></ruby>む！",
        [2014] = "<ruby>恋<rt>れん</rt></ruby><ruby>愛<rt>あい</rt></ruby><ruby>禁<rt>きん</rt></ruby><ruby>止<rt>し</rt></ruby>！？<br><ruby>誠<rt>せい</rt></ruby><ruby>司<rt>じ</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたい！",
        [2015] = "プリンセスだって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>のドーナッツはこれだ！",
        [2016] = "<ruby>魔<rt>ま</rt></ruby><ruby>法<rt>ほう</rt></ruby>つかいだって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>の<ruby>冷<rt>れい</rt></ruby><ruby>凍<rt>とう</rt></ruby>みかんはこれだ！",
        [2017] = "お<ruby>嬢<rt>じょう</rt></ruby>さまだって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>のアニマルスイーツ！",
        [2018] = "<ruby>社<rt>しゃ</rt></ruby><ruby>長<rt>ちょう</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>のソウルがシャウトするのです！",
        [2019] = "AIだって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>の<ruby>星<rt>ほし</rt></ruby><ruby>空<rt>ぞら</rt></ruby>はキラやば〜☆",
        [2020] = "ラテ<ruby>様<rt>さま</rt></ruby>だって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>のお<ruby>手<rt>て</rt></ruby><ruby>当<rt>あ</rt></ruby>てはこれだ！",
        [2021] = "<ruby>生<rt>せい</rt></ruby><ruby>徒<rt>と</rt></ruby><ruby>会<rt>かい</rt></ruby><ruby>長<rt>ちょう</rt></ruby>だってトロピカれ！<br><ruby>今<rt>いま</rt></ruby><ruby>一<rt>いち</rt></ruby><ruby>番<rt>ばん</rt></ruby><ruby>大<rt>だい</rt></ruby><ruby>事<rt>じ</rt></ruby>なことはこれだ！",
        [2022] = "ブラペだって<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>のデリシャスマイルはこれだ！",
        [2023] = "ヒーローだって<ruby>暴<rt>あば</rt></ruby>れたい！<br>アゲアゲな<ruby>映<rt>えい</rt></ruby><ruby>像<rt>ぞう</rt></ruby>エンタメはこれだ！",
        [2024] = "メェメェだって<ruby>暴<rt>あば</rt></ruby>れたい！",
        [2025] = "キミと<ruby>一<rt>いっ</rt></ruby><ruby>緒<rt>しょ</rt></ruby>に<ruby>暴<rt>あば</rt></ruby>れたい！<br><ruby>極<rt>ごく</rt></ruby><ruby>上<rt>じょう</rt></ruby>のキラッキランラン♪",
        [2026] = "<ruby>名<rt>めい</rt></ruby><ruby>探<rt>たん</rt></ruby><ruby>偵<rt>てい</rt></ruby>だって、はなまる<ruby>暴<rt>あば</rt></ruby>れたい！"
    };

    /// <summary>
    /// 見本の文を替えるスピンオフ（作品のスラッグ → ルビ付きの文）。放送開始の年で引く <see cref="SubtitleSampleByYear"/> は本線の作品のもので、
    /// 同じ年に始まったスピンオフには当てないので、スピンオフの見本の文はここに置く（キボウノチカラ〜オトナプリキュア'23〜は、本編の題が
    /// 全話片仮名なので、見本も片仮名の「オトナノアバレ」。魔法つかいプリキュア！！〜MIRAI DAYS〜は、本編の振り仮名が「刻（とき）」だけなので、
    /// 見本も「刻」だけに振り仮名を付けた「刻だって暴れたい」）。
    /// </summary>
    private static readonly Dictionary<string, string> SubtitleSampleBySeries = new(StringComparer.Ordinal)
    {
        ["otona"] = "オトナノアバレ",
        ["maho2"] = "<ruby>刻<rt>とき</rt></ruby>だって暴れたい"
    };

    /// <summary>
    /// 作品のサブタイトルの見本の文（2 行の話が優勢な作品は 2 行。語り口の違う作品は 2 行目の結びを替え、本編に 2 行目の見本ほど
    /// 長い行の無い作品は短い文にする）。
    /// </summary>
    private static string SubtitleSampleHtml(Series series, BuildContext ctx) =>
        SubtitleSampleBySeries.TryGetValue(series.Slug, out var seriesHtml) ? seriesHtml
        // 放送開始の年で引く見本の文は本線の作品のもの（同じ年に始まったスピンオフには当てない）
        : !SeriesClassifier.IsSpinOff(series) && SubtitleSampleByYear.TryGetValue(series.StartDate.Year, out var html) ? html
        : !MostlyTwoLines(series, ctx) ? SubtitleRubyHtml
        : LongestLineLength(series, ctx) < SubtitleSecondLineLength ? SubtitleShortTwoLineRubyHtml
        : SubtitleTwoLineRubyHtml;

    /// <summary>作品の本編のサブタイトルのいちばん長い行の字数（振り仮名とタグを除き、改行で分けた行ごと）。</summary>
    private static int LongestLineLength(Series series, BuildContext ctx)
    {
        if (!ctx.EpisodesBySeries.TryGetValue(series.SeriesId, out var episodes)) return 0;
        int longest = 0;
        foreach (var ep in episodes)
        {
            if (string.IsNullOrEmpty(ep.TitleRichHtml)) continue;
            string plain = System.Text.RegularExpressions.Regex.Replace(ep.TitleRichHtml, @"<rt>.*?</rt>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            foreach (var line in System.Text.RegularExpressions.Regex.Split(plain, @"<br\s*/?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                string text = System.Text.RegularExpressions.Regex.Replace(line, "<[^>]+>", "");
                longest = Math.Max(longest, new System.Globalization.StringInfo(text).LengthInTextElements);
            }
        }
        return longest;
    }

    /// <summary>作品のサブタイトルは 2 行が優勢か（サブタイトルのある話のうち、2 行の話が半分を超える）。</summary>
    private static bool MostlyTwoLines(Series series, BuildContext ctx)
    {
        if (!ctx.EpisodesBySeries.TryGetValue(series.SeriesId, out var episodes)) return false;
        var titled = episodes.Where(e => !string.IsNullOrEmpty(e.TitleText)).ToList();
        int twoLines = titled.Count(e => System.Text.RegularExpressions.Regex.Matches(e.TitleRichHtml ?? "", @"<br\s*/?>", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count == 1);
        return twoLines * 2 > titled.Count;
    }

    /// <summary>OP の役職名と人名。</summary>
    private const string OpRole = "原作";
    private static readonly string[] OpNames = { "東堂いづみ" };

    /// <summary>ED の役職名と人名（東映動画の黎明期を支えた原画家の方々のお名前をお借りしている。姓と名の間は全角の空白）。</summary>
    private const string EdRole = "原画";
    private static readonly string[] EdNames = { "大工原　章", "森　康二", "大塚康生", "中村和子", "楠部大吉郎", "奥山玲子" };

    /// <summary>
    /// ED の見本を役職名のまとまりの並びで描く作品（作品のスラッグ → 上から順のまとまり）。放送の原画の欄が、メインアニメーターの下に
    /// 原画が多段で続く組みの作品は、見本も同じ組みにする（キボウノチカラ〜オトナプリキュア'23〜）。原画の人数が多く列を組む作品は、
    /// 見本も同じ 15 人を「原画」1 つに並べる（魔法つかいプリキュア！！〜MIRAI DAYS〜）。ここに無い作品は「原画」と <see cref="EdNames"/> の 1 つ。
    /// </summary>
    private static readonly Dictionary<string, (string Role, string[] Names)[]> EdBlocksBySeries = new(StringComparer.Ordinal)
    {
        ["otona"] =
        [
            ("メインアニメーター", ["大工原　章", "森　康二"]),
            ("原画", ["大塚康生", "坂本雄作", "喜多真佐武", "紺野修司", "中村和子", "寺千賀雄", "楠部大吉郎", "長沢寿美子",
                      "藤井　武", "加藤洋子", "松隅玉江", "赤坂　進", "奥山玲子"])
        ],
        ["maho2"] =
        [
            ("原画", ["大工原　章", "森　康二", "大塚康生", "坂本雄作", "喜多真佐武", "紺野修司", "中村和子", "寺千賀雄", "楠部大吉郎",
                      "長沢寿美子", "藤井　武", "加藤洋子", "松隅玉江", "赤坂　進", "奥山玲子"])
        ]
    };

    /// <summary>
    /// 場面の見本の文の差し替え（体裁の測定用）。環境変数 <c>PRECURE_FONT_SCENE_TEXTS</c> に JSON のファイルを指すと、
    /// <c>{"{作品のスラッグ}/{場面}": {"role": …, "names": […], "lyrics": "…", "namePitch": …}}</c>（場面は op・ed1 など、歌詞はルビ付きの HTML）の
    /// 役職名・人名・歌詞で描く。放送の画面と同じ文で描いて、字の形と装飾を画素ごとに比べるために使う。<c>namePitch</c> は人名の行送り
    /// （人名の字の大きさに対する比）で、見本の人数に合わせて組みの行送りを変えた場面を、放送の人数の行送りで描くときに渡す。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, SceneTextOverride> TextOverrides = LoadTextOverrides();

    private sealed record SceneTextOverride(string? Role, IReadOnlyList<string>? Names, string? Lyrics, float? NamePitch = null);

    private static IReadOnlyDictionary<string, SceneTextOverride> LoadTextOverrides()
    {
        var path = Environment.GetEnvironmentVariable("PRECURE_FONT_SCENE_TEXTS");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Dictionary<string, SceneTextOverride>();
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SceneTextOverride>>(File.ReadAllText(path),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, SceneTextOverride>();
    }

    /// <summary>空白の空きを測っていない歌詞の空き（半角 2 つ分）。</summary>
    private const float DefaultSpaceEm = 0.5f;

    /// <summary>
    /// 書体見本の右上・左下の字（各 16 字）。右上はかなで、4 字 × 4 行で読める言葉にした（「あなたが／いるから／プリティ／キュア！」）。
    /// 左下は漢字で、サブタイトル全話で使われた回数の多い字から選んだ（「愛」「永」と、横棒だけの「一」は除く）。
    /// </summary>
    public const string SpecimenTopRight = "あなたがいるからプリティキュア！";
    public const string SpecimenBottomLeft = "大生戦人最決力気会新誕日魔作夢私";

    /// <summary>書体（ウェイトごと）のページの URL（<c>/fonts/{書体名とウェイトをつなげた名前}/</c>。例：<c>/fonts/ゴナE/</c>）。</summary>
    public static string FontUrl(FontFace font) => $"/fonts/{UrlSlug.Encode(UrlSlug.FromName(font.FamilyName + (font.Weight ?? "")))}/";

    /// <summary>作品の 1 つの場所・時期の通常の行（本放送だけの行・色違いの 2 つ目以降は除く）。</summary>
    public static SeriesFontUsage? Usage(BuildContext ctx, int seriesId, string kind, int period) =>
        ctx.FontUsages.FirstOrDefault(u => u.SeriesId == seriesId && u.UsageKind == kind && u.PeriodNo == period
                                           && !u.IsBroadcastOnly && u.VariantNo == 1);

    /// <summary>作品の ED の時期の番号（通常の行のあるもの、昇順）。</summary>
    public static IReadOnlyList<int> EdPeriods(BuildContext ctx, int seriesId) =>
        ctx.FontUsages.Where(u => u.SeriesId == seriesId && !u.IsBroadcastOnly && (u.UsageKind == FontUsageKinds.EdCredit || u.UsageKind == FontUsageKinds.EdLyrics))
            .Select(u => (int)u.PeriodNo).Distinct().OrderBy(p => p).ToList();

    /// <summary>OP の場面の画像の材料（クレジットか歌詞の書体があれば）。</summary>
    public static SubtitleTelopRequest? OpScene(Series series, BuildContext ctx) =>
        Scene(series, ctx, Usage(ctx, series.SeriesId, FontUsageKinds.OpCredit, 1), Usage(ctx, series.SeriesId, FontUsageKinds.OpLyrics, 1),
            Usage(ctx, series.SeriesId, FontUsageKinds.OpLyricsRuby, 1), [(OpRole, OpNames)], opening: true, "op");

    /// <summary>ED の場面の画像の材料（時期ごと）。</summary>
    public static SubtitleTelopRequest? EdScene(Series series, int period, BuildContext ctx) =>
        Scene(series, ctx, Usage(ctx, series.SeriesId, FontUsageKinds.EdCredit, period), Usage(ctx, series.SeriesId, FontUsageKinds.EdLyrics, period),
            Usage(ctx, series.SeriesId, FontUsageKinds.EdLyricsRuby, period),
            EdBlocksBySeries.TryGetValue(series.Slug, out var blocks) ? blocks.Select(b => (b.Role, (IReadOnlyList<string>)b.Names)).ToList() : [(EdRole, EdNames)],
            opening: false, $"ed{period}");

    /// <summary>サブタイトルの場面の画像の材料。サブタイトルの位置はまだ測っていないので、画面の中央に置く。</summary>
    public static SubtitleTelopRequest? SubtitleScene(Series series, BuildContext ctx)
    {
        var usage = Usage(ctx, series.SeriesId, FontUsageKinds.Subtitle, 1);
        if (usage is null || RenderFont(ctx, usage) is not { } font) return null;
        var ruby = Usage(ctx, series.SeriesId, FontUsageKinds.SubtitleRuby, 1);
        string rubyFont = ruby is not null && RenderFont(ctx, ruby) is { } rf && rf != font ? rf : "";
        var layout = TelopLayout.Parse(usage.Layout);
        int width = SceneWidth(series, layout);
        float size = (float)(usage.SizePx ?? 80m);
        var profile = WithDecoration(SubtitleTelopProfile.FromSeries(series), usage, 1f) with
        {
            RubyFontFamily = rubyFont,
            RubyOverhangLineStart = true,
            // 作品の本編のいちばん長い行の幅（組み方の subtitle.maxWidth、画面の幅に対する比）を超える行は、その幅まで長体をかける
            LineMaxWidth = layout?.Subtitle?.MaxWidth is float mw ? mw * width : null,
            RubyKeepWidth = layout?.Subtitle?.RubyKeepWidth ?? false
        };
        var piece = new FontScenePiece(SubtitleSampleHtml(series, ctx), font, profile, size,
            (layout?.Screen?.X ?? 0.5f) * width, (layout?.Screen?.Y ?? 0.5f) * FontScene.Height, CenterX: true);
        return new SubtitleTelopRequest("/fonts/", $"font-samples/{series.Slug}/subtitle.png", "", font, profile)
        {
            Scene = new FontScene(width, new[] { piece })
        };
    }

    /// <summary>書体見本の材料（描く書体の無い書体は null）。</summary>
    public static SubtitleTelopRequest? Specimen(FontFace font)
    {
        if (string.IsNullOrWhiteSpace(font.RenderFontName)) return null;
        return new SubtitleTelopRequest(FontUrl(font), $"font-specimens/{font.FontId}.png", "", font.RenderFontName, SubtitleTelopProfile.Default)
        {
            Specimen = (SpecimenTopRight, SpecimenBottomLeft)
        };
    }

    /// <summary>描く書体名（無ければ null）。</summary>
    private static string? RenderFont(BuildContext ctx, SeriesFontUsage usage) =>
        ctx.FontById.TryGetValue(usage.FontId, out var f) && !string.IsNullOrWhiteSpace(f.RenderFontName) ? f.RenderFontName : null;

    /// <summary>場面の幅。2006 年までの作品（組み方が 4:3 か、組み方の無いとき放送開始が 2007 年より前）は 4:3。</summary>
    private static int SceneWidth(Series series, TelopLayout? layout)
    {
        bool sd = layout?.Screen is { } s ? s.Aspect == "4:3" : series.StartDate.Year < 2007;
        return sd ? 1440 : 1920;
    }

    /// <summary>
    /// 行の装飾を組み方に重ねる。<paramref name="scale"/> は装飾の比を直す倍率（役職名は人名の大きさ ÷ 役職名の大きさ）。
    /// 装飾のある行は画素で重ねる描き方で描く。
    /// </summary>
    private static SubtitleTelopProfile WithDecoration(SubtitleTelopProfile baseProfile, SeriesFontUsage u, float scale)
    {
        var decoration = TelopDecoration.Parse(u.Decoration);
        return baseProfile with
        {
            BaseObliqueDegrees = (float)(u.ObliqueDeg ?? 0m),
            Decoration = decoration is null ? TelopDecoration.Default : scale == 1f ? decoration : decoration.ScaledBy(scale),
            RasterDecoration = decoration is not null
        };
    }

    /// <summary>
    /// クレジットと歌詞を置いた場面の材料。クレジットは人名の 1 行目の左端・ベースラインを組み方の画面の位置に置き、役職名は
    /// 組み方の役職名の値（人名に対する位置・字取り）で、人名は字取りの幅に均等に割り付けて行送りで並べる。
    /// 役職名のまとまりが複数あれば上から順に、前のまとまりの最後の行から行送りとまとまりの間の空き（<c>name.blockGap</c>）だけ下げて並べる。
    /// 歌詞は行の中心・ベースラインを組み方の画面の位置に置く。
    /// </summary>
    private static SubtitleTelopRequest? Scene(Series series, BuildContext ctx, SeriesFontUsage? credit, SeriesFontUsage? lyrics,
        SeriesFontUsage? ruby, IReadOnlyList<(string Role, IReadOnlyList<string> Names)> blocks, bool opening, string fileKey)
    {
        TextOverrides.TryGetValue($"{series.Slug}/{fileKey}", out var textOverride);
        // 測定用の文の差し替えは、最初のまとまりの役職名・人名に当てる
        if (textOverride is not null && blocks.Count > 0)
        {
            var (firstRole, firstNames) = blocks[0];
            if (textOverride.Role is { Length: > 0 } overrideRole) firstRole = overrideRole;
            if (textOverride.Names is { Count: > 0 } overrideNames) firstNames = overrideNames;
            blocks = [(firstRole, firstNames), .. blocks.Skip(1)];
        }
        var pieces = new List<FontScenePiece>();
        var creditLayout = credit is null ? null : TelopLayout.Parse(credit.Layout);
        var lyricsLayout = lyrics is null ? null : TelopLayout.Parse(lyrics.Layout);
        int width = SceneWidth(series, creditLayout ?? lyricsLayout);

        if (credit is not null && RenderFont(ctx, credit) is { } creditFont)
        {
            float nameSize = (float)(credit.SizePx ?? 60m);
            float roleSize = (float)(credit.RoleSizePx ?? credit.SizePx * 0.78m ?? 47m);
            var screen = creditLayout?.Screen;
            float x = (screen?.X ?? (opening ? 0.62f : 0.28f)) * width;
            float y = (screen?.Y ?? (opening ? 0.40f : 0.22f)) * FontScene.Height;
            float pitch = textOverride?.NamePitch ?? creditLayout?.Name?.Pitch ?? 1.3f;
            float blockGap = creditLayout?.Name?.BlockGap ?? 0f;
            float? nameSpan = creditLayout?.Name?.Span;
            // 2 列以上の組みは、行ごとに左から右へ並べ、右端まで埋めたら次の行の左へ移る
            int columns = Math.Max(1, creditLayout?.Name?.Columns ?? 1);
            float columnPitch = creditLayout?.Name?.ColumnPitch ?? 6f;
            // 役職名と人名は 1 つのまとまり（環状のグラデーションの軸を決める範囲）
            const string creditGroup = "credit";

            foreach (var (role, names) in blocks)
            {
                TelopRoleLayout? rl = null;
                creditLayout?.Roles?.TryGetValue(role, out rl);
                float dx = rl?.Dx ?? (opening ? 0.75f : 3.3f);
                float offset = rl?.BaselineOffset ?? (opening ? 1.2f : 0.08f);
                float roleSpan = rl?.Span ?? 3f;

                var roleProfile = WithDecoration(SubtitleTelopProfile.Default, credit, nameSize / roleSize) with
                {
                    Solid = true,
                    LetterSpacingEm = Justify(role, roleSpan),
                    // 役職名の欄の幅を超える役職名は、その幅まで長体で詰める
                    LineMaxWidth = rl?.MaxWidth is float roleMax ? roleMax * nameSize : null
                };
                // 役職名だけ字の色が違うクレジットは、塗りを役職名の色にする（字ごとの色・塗りのグラデーションは人名の塗りなので外す）
                if (roleProfile.Decoration.RoleFillChars is { Count: > 0 } roleChars)
                    roleProfile = roleProfile with
                    {
                        Decoration = roleProfile.Decoration with { Fill = roleChars[0], FillGradient = null, FillChars = roleChars }
                    };
                else if (roleProfile.Decoration.RoleFill is { } roleFill)
                    roleProfile = roleProfile with
                    {
                        Decoration = roleProfile.Decoration with { Fill = roleFill, FillGradient = null, FillChars = null }
                    };
                pieces.Add(new FontScenePiece(HtmlUtil.Escape(role), creditFont, roleProfile, roleSize, x - dx * nameSize, y - offset * nameSize, CenterX: false, creditGroup));

                for (int i = 0; i < names.Count; i++)
                {
                    int col = i % columns, row = i / columns;
                    // 名前の中の全角の空白も 1 字に数えて字取りの幅に均等に割り付ける（空白の空きは 1 字＋前後の字間）。
                    // 列ごとの字取りがあればその列の幅に割り付ける
                    float? colSpan = creditLayout?.Name?.ColumnSpans is { } spans && col < spans.Count ? spans[col] : nameSpan;
                    float ls = colSpan is float s ? Justify(names[i], s) : 0f;
                    var nameProfile = WithDecoration(SubtitleTelopProfile.Default, credit, 1f) with
                    {
                        Solid = creditLayout?.Name?.Solid ?? true,
                        LetterSpacingEm = ls,
                        SpaceEm = 1f + 2f * ls
                    };
                    pieces.Add(new FontScenePiece(HtmlUtil.Escape(names[i]), creditFont, nameProfile, nameSize,
                        x + col * columnPitch * nameSize, y + row * pitch * nameSize, CenterX: false, creditGroup));
                }
                // 次のまとまりは、このまとまりの最後の行から行送りとまとまりの間の空きだけ下げる
                int rows = Math.Max(1, (names.Count + columns - 1) / columns);
                y += ((rows - 1) * pitch + pitch + blockGap) * nameSize;
            }
        }

        if (lyrics is not null && RenderFont(ctx, lyrics) is { } lyricsFont)
        {
            float size = (float)(lyrics.SizePx ?? 54m);
            var profile = WithDecoration(SubtitleTelopProfile.Default, lyrics, 1f) with
            {
                SpaceEm = lyricsLayout?.Space ?? DefaultSpaceEm,
                Solid = lyricsLayout?.Solid ?? false,
                LetterSpacingEm = lyricsLayout?.LetterSpacing ?? 0f,
                PunctHalf = lyricsLayout?.PunctHalf ?? false,
                LatinSpacingEm = lyricsLayout?.LatinSpacing ?? 0f,
                SmallKanaPalt = lyricsLayout?.SmallKana ?? 0f,
                RubyOverhangLineStart = true
            };
            string html = textOverride?.Lyrics ?? LyricsPlain;
            if (ruby is not null && lyrics.SizePx is decimal ls && ls > 0)
            {
                string rubyFont = RenderFont(ctx, ruby) is { } rf && rf != lyricsFont ? rf : "";
                profile = profile with
                {
                    RubyFontFamily = rubyFont,
                    RubySizeRatio = ruby.SizePx is decimal rs ? (float)(rs / ls) : SubtitleTelopProfile.Default.RubySizeRatio,
                    RubyRaiseRatio = ruby.RubyRaiseEm is decimal raise ? (float)raise : SubtitleTelopProfile.Default.RubyRaiseRatio,
                    RubyLetterSpacingEm = (float)(ruby.RubyLetterSpacingEm ?? 0m),
                    RubyGrouping = ruby.RubyGrouping?.ToUpperInvariant() switch
                    {
                        "JUKUGO" => SubtitleRubyGrouping.Jukugo,
                        "SPREAD" => SubtitleRubyGrouping.Spread,
                        "GROUP" => SubtitleRubyGrouping.Group,
                        _ => SubtitleRubyGrouping.Mono
                    }
                };
                html = textOverride?.Lyrics ?? LyricsRubyHtml;
            }
            var screen = lyricsLayout?.Screen;
            pieces.Add(new FontScenePiece(html, lyricsFont, profile, size, (screen?.X ?? 0.5f) * width, (screen?.Y ?? 0.89f) * FontScene.Height, CenterX: true));
        }

        if (pieces.Count == 0) return null;
        return new SubtitleTelopRequest("/fonts/", $"font-samples/{series.Slug}/{fileKey}.png", "", pieces[0].FontFamily, SubtitleTelopProfile.Default)
        {
            Scene = new FontScene(width, pieces)
        };
    }

    /// <summary>字を <paramref name="span"/> 字分の幅に均等に割り付ける字間（字の大きさに対する比）。字数が字取り以上なら 0。</summary>
    private static float Justify(string text, float span)
    {
        // 字取りが字数よりわずかに狭い（字数の 1/4 字以内）ときは字間を詰めて字取りの両端にそろえる（放送でも字数どおりの名前と
        // 字数の少ない名前の行末がそろう）。それより狭いときは詰めずにベタで組む
        int n = new System.Globalization.StringInfo(text).LengthInTextElements;
        return n >= 2 && span >= n - 0.25f ? (span - n) / (n - 1) : 0f;
    }

    /// <summary>作品の場面の画像の材料（本編に流れる順：OP → サブタイトル → ED の時期の順）。</summary>
    public static IEnumerable<(string Kind, int Period, SubtitleTelopRequest Request)> ForSeries(Series series, BuildContext ctx)
    {
        if (OpScene(series, ctx) is { } op) yield return ("OP", 1, op);
        if (SubtitleScene(series, ctx) is { } sub) yield return ("SUBTITLE", 1, sub);
        foreach (int period in EdPeriods(ctx, series.SeriesId))
            if (EdScene(series, period, ctx) is { } ed) yield return ("ED", period, ed);
    }

    /// <summary>描く全画像の材料（作品の場面と書体見本）。ビルド冒頭の作り置きの確認が、各話のテロップ画像と合わせて使う。</summary>
    public static IEnumerable<SubtitleTelopRequest> EnumerateAll(BuildContext ctx)
    {
        var seriesIds = ctx.FontUsages.Select(u => u.SeriesId).Distinct().ToHashSet();
        foreach (var series in ctx.Series.Where(s => seriesIds.Contains(s.SeriesId)))
            foreach (var (_, _, request) in ForSeries(series, ctx))
                yield return request;
        var fontIds = ctx.FontUsages.Where(u => !u.IsBroadcastOnly).Select(u => u.FontId).Distinct();
        foreach (int id in fontIds)
            if (ctx.FontById.TryGetValue(id, out var font) && Specimen(font) is { } specimen)
                yield return specimen;
    }
}
