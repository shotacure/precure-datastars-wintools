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

    /// <summary>クレジットの役職名の大きさ（画面の高さ 1080 換算の px、仮想ボディ）。</summary>
    public decimal? RoleSizePx { get; set; }

    /// <summary>
    /// クレジットの人名を割り付ける幅（字の大きさを 1 とした字数）。四字の名前を五字分に広げる「五字取り」なら 5。
    /// 測った値をそのまま持つ（データとして持つだけで、サイトには出さない）。
    /// </summary>
    public decimal? NameSpanEm { get; set; }

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
