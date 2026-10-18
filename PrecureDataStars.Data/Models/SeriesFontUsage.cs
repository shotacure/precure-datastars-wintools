namespace PrecureDataStars.Data.Models;

/// <summary>
/// series_font_usages テーブルに対応するエンティティモデル。作品のどこにどの書体を使ったかを、
/// 作品 × 使う場所（<see cref="FontUsageKinds"/>）× 時期（× 本放送だけの範囲）の 1 行で持つ。
/// サブタイトルのテロップ画像の書体もここ（サブタイトル・サブタイトルのルビの行）から引く。
/// </summary>
public sealed class SeriesFontUsage
{
    /// <summary>行 ID（PK）。</summary>
    public int UsageId { get; set; }

    /// <summary>作品の ID。</summary>
    public int SeriesId { get; set; }

    /// <summary>使う場所（<see cref="FontUsageKinds"/> のコード）。</summary>
    public string UsageKind { get; set; } = string.Empty;

    /// <summary>時期（1 = 前期・通期、2 = 後期 …）。</summary>
    public byte PeriodNo { get; set; } = 1;

    /// <summary>色違いの番号（同じ場所・時期で装飾の色だけが違う行。1 から）。</summary>
    public byte VariantNo { get; set; } = 1;

    /// <summary>色違いの呼び名（「ピンク」「水色」など）。色違いの無い場所は NULL。</summary>
    public string? VariantLabel { get; set; }

    /// <summary>本放送だけの書体なら true（BD などは通常の書体）。</summary>
    public bool IsBroadcastOnly { get; set; }

    /// <summary>使った最初の話（NULL は時期の全話）。</summary>
    public ushort? FirstEpisodeNo { get; set; }

    /// <summary>使った最後の話（NULL は時期の全話）。</summary>
    public ushort? LastEpisodeNo { get; set; }

    /// <summary>書体（fonts.font_id）。</summary>
    public int FontId { get; set; }

    /// <summary>ウェイトが決めきれていなければ true（サイトに「ウェイト不明」と出す）。</summary>
    public bool IsWeightUnknown { get; set; }

    /// <summary>書体に手を加えた旨（「太め加工」など。サイトに出す）。</summary>
    public string? ModificationNote { get; set; }

    /// <summary>字の大きさ（画面の高さ 1080 換算の px、仮想ボディ）。クレジットは人名。</summary>
    public decimal? SizePx { get; set; }

    /// <summary>斜体の角度（度、右に倒す）。NULL は 0（斜体でない）。書体の見本もこの角度で傾けて描く。</summary>
    public decimal? ObliqueDeg { get; set; }

    /// <summary>ルビのベースラインの、親字のベースラインからの高さ（親字の大きさに対する比）。ルビの行で持つ。</summary>
    public decimal? RubyRaiseEm { get; set; }

    /// <summary>ルビの字と字の間に足す空き（ルビの大きさに対する比）。ルビの行で持つ。</summary>
    public decimal? RubyLetterSpacingEm { get; set; }

    /// <summary>ルビの置き方（MONO＝1 字ずつ / JUKUGO＝熟語全体の上にひと続き / SPREAD＝熟語の幅に均等）。ルビの行で持つ。</summary>
    public string? RubyGrouping { get; set; }

    /// <summary>クレジットの役職名の大きさ（画面の高さ 1080 換算の px、仮想ボディ）。</summary>
    public decimal? RoleSizePx { get; set; }

    /// <summary>
    /// クレジットの組み方の JSON（<see cref="TelopLayout"/> の形）。画面の中の位置・役職名と人名の並び・人名の字取りと行送り・
    /// 声の出演の組みを持つ。値は人名の字の大きさに対する比（画面の位置は画面の幅・高さに対する比）。NULL なら組み方を測っていない。
    /// </summary>
    public string? Layout { get; set; }

    /// <summary>
    /// 装飾（字の色・フチ・影・光彩）の JSON（<see cref="TelopDecoration"/> の形）。書体の見本をこの体裁で描く。
    /// NULL ならテロップの既定の体裁（白い字・黒い丸フチ・右下の黒い影）。
    /// </summary>
    public string? Decoration { get; set; }

    /// <summary>サイトに添える注記。</summary>
    public string? DisplayNote { get; set; }

    /// <summary>内部メモ（サイトに出さない）。</summary>
    public string? Notes { get; set; }
}

/// <summary>series_font_usages.usage_kind（書体を使う場所）のコード定数と、サイトでの並び・名前。</summary>
public static class FontUsageKinds
{
    public const string OpCredit = "OP_CREDIT";
    public const string OpLyrics = "OP_LYRICS";
    public const string OpLyricsRuby = "OP_LYRICS_RUBY";
    public const string Subtitle = "SUBTITLE";
    public const string SubtitleRuby = "SUBTITLE_RUBY";
    public const string EdCredit = "ED_CREDIT";
    public const string EdLyrics = "ED_LYRICS";
    public const string EdLyricsRuby = "ED_LYRICS_RUBY";

    /// <summary>サイトでの並び（本編に出てくる順）。</summary>
    public static readonly IReadOnlyList<string> Ordered = new[]
    {
        OpCredit, OpLyrics, OpLyricsRuby, Subtitle, SubtitleRuby, EdCredit, EdLyrics, EdLyricsRuby
    };

    /// <summary>並びの位置（知らないコードは末尾）。</summary>
    public static int OrderOf(string kind)
    {
        for (int i = 0; i < Ordered.Count; i++)
            if (Ordered[i] == kind) return i;
        return Ordered.Count;
    }

    /// <summary>サイトに出す名前。</summary>
    public static string Label(string kind) => kind switch
    {
        OpCredit => "OP クレジット",
        OpLyrics => "OP 歌詞",
        OpLyricsRuby => "OP 歌詞のルビ",
        Subtitle => "サブタイトル",
        SubtitleRuby => "サブタイトルのルビ",
        EdCredit => "ED クレジット",
        EdLyrics => "ED 歌詞",
        EdLyricsRuby => "ED 歌詞のルビ",
        _ => kind
    };

    /// <summary>ルビの場所か。</summary>
    public static bool IsRuby(string kind) => kind is OpLyricsRuby or SubtitleRuby or EdLyricsRuby;

    /// <summary>クレジットの場所か（人名と役職名の 2 つの大きさを持つ）。</summary>
    public static bool IsCredit(string kind) => kind is OpCredit or EdCredit;

    /// <summary>歌詞の場所か（ルビは含まない）。</summary>
    public static bool IsLyrics(string kind) => kind is OpLyrics or EdLyrics;

    /// <summary>ルビの場所に対応する親字の場所（ルビでなければ自身）。</summary>
    public static string BaseOf(string kind) => kind switch
    {
        OpLyricsRuby => OpLyrics,
        SubtitleRuby => Subtitle,
        EdLyricsRuby => EdLyrics,
        _ => kind
    };
}
