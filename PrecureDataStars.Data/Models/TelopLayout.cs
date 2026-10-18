using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrecureDataStars.Data.Models;

/// <summary>
/// クレジットの組み方。<c>series_font_usages.layout</c>（JSON）の中身で、画面の中の位置・役職名と人名の並び・
/// 人名の字取りと行送り・声の出演の組みを持つ。値は人名の字の大きさに対する比で、画面の位置だけは画面の幅・高さに対する比。
/// 測っていない項目は省略する（null）。
/// <code>
/// { "version": 1,
///   "screen": { "x": 0.36, "y": 0.22 },
///   "roles":  { "原画": { "span": 3.0, "position": "left", "dx": 3.3, "baselineOffset": 0.08 } },
///   "name":   { "span": 5.0, "pitch": 1.33, "condense": true },
///   "cast":   { "headingSize": 0.75, "charSize": 0.75, "columnGap": 4.7, "pitch": 1.27 } }
/// </code>
/// </summary>
public sealed record TelopLayout
{
    /// <summary>いまの JSON の形の版。</summary>
    public const int CurrentVersion = 1;

    /// <summary>JSON の形の版。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>暫定の値（そのカードを測っておらず、ほかのカードや年の値から作った）なら true。</summary>
    public bool Provisional { get; init; }

    /// <summary>画面の中の位置（クレジットは人名の 1 行目の左端・ベースライン、歌詞は行の中心・ベースライン）。</summary>
    public TelopScreenPosition? Screen { get; init; }

    /// <summary>
    /// 役職名の組み（役職名ごと。「原作」「原画」）。同じ時期でも役職名によって字取りや人名との位置が違うことがあるので、
    /// 役職名を鍵にして持つ。
    /// </summary>
    public IReadOnlyDictionary<string, TelopRoleLayout>? Roles { get; init; }

    /// <summary>人名の組み。</summary>
    public TelopNameLayout? Name { get; init; }

    /// <summary>声の出演の組み。</summary>
    public TelopCastLayout? Cast { get; init; }

    /// <summary>サブタイトルの組み方（縦のそろえ方・行の最大幅・長体の下限）。サブタイトルの行で持つ。</summary>
    public TelopSubtitleLayout? Subtitle { get; init; }

    /// <summary>歌詞の空白のアキ（前の字の字送りの箱の右端から、後の字の箱の左端まで。歌詞の字の大きさに対する比）。歌詞の行で持つ。</summary>
    public float? Space { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary><c>layout</c> 列の JSON を読む。NULL・空なら null。知らない版は例外にする。</summary>
    public static TelopLayout? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var l = JsonSerializer.Deserialize<TelopLayout>(json, JsonOptions)
            ?? throw new FormatException("layout の JSON が空です。");
        if (l.Version != CurrentVersion)
            throw new FormatException($"layout の JSON の版 {l.Version} は読めません（読めるのは {CurrentVersion}）。");
        return l;
    }

    /// <summary>JSON に書き出す（<c>layout</c> 列に入れる形）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>画面の中の位置。画面の幅・高さに対する比（左上が 0）。</summary>
public sealed record TelopScreenPosition
{
    /// <summary>画面の縦横比。<c>4:3</c>（2006 年までの作品）/ <c>16:9</c>（既定）。</summary>
    public string Aspect { get; init; } = "16:9";

    /// <summary>横の位置（クレジットは人名の 1 行目の左端、歌詞は行の中心）。</summary>
    public float X { get; init; }

    /// <summary>縦の位置（ベースライン）。</summary>
    public float Y { get; init; }
}

/// <summary>役職名の組み。値は人名の字の大きさに対する比（<see cref="Span"/> だけは役職名の字の大きさに対する比）。</summary>
public sealed record TelopRoleLayout
{
    /// <summary>役職名を割り付ける幅（役職名の字の大きさを 1 とした字数。「原　画」が 3 字分なら 3）。</summary>
    public float? Span { get; init; }

    /// <summary>人名に対する置き場所。<c>left</c>（人名の 1 行目の左）/ <c>above</c>（人名の上の行）。</summary>
    public string? Position { get; init; }

    /// <summary>役職名の左端から人名の左端までの横の差（右を正）。</summary>
    public float? Dx { get; init; }

    /// <summary>役職名のベースラインの、人名の 1 行目のベースラインからの高さ（上を正）。</summary>
    public float? BaselineOffset { get; init; }
}

/// <summary>人名の組み。値は人名の字の大きさに対する比。</summary>
public sealed record TelopNameLayout
{
    /// <summary>人名を割り付ける幅（字数。四字の名前を五字分に広げる「五字取り」なら 5）。</summary>
    public float? Span { get; init; }

    /// <summary>人名の行送り（行のベースラインどうしの間隔）。</summary>
    public float? Pitch { get; init; }

    /// <summary>割り付ける幅に収まらない長い名前を、長体で詰めて幅に収めるか。</summary>
    public bool? Condense { get; init; }

    /// <summary>人名を並べる列の数（null なら 1 列）。2 列なら、上から順に左の列を埋めてから右の列に移る。</summary>
    public int? Columns { get; init; }

    /// <summary>列と列の間隔（左の列の左端から右の列の左端までの横の差）。</summary>
    public float? ColumnPitch { get; init; }
}

/// <summary>声の出演の組み。値は声優名の字の大きさに対する比。</summary>
public sealed record TelopCastLayout
{
    /// <summary>見出し「声の出演」の字の大きさ。</summary>
    public float? HeadingSize { get; init; }

    /// <summary>見出しのベースラインから、1 行目のベースラインまでの高さ。</summary>
    public float? HeadingGap { get; init; }

    /// <summary>役名の字の大きさ。</summary>
    public float? CharSize { get; init; }

    /// <summary>役名のそろえ。<c>left</c>（左そろえ、既定）/ <c>right</c>（右そろえ）。</summary>
    public string? CharAlign { get; init; }

    /// <summary>役名を割り付ける幅（役名の字の大きさを 1 とした字数）。収まらない役名は長体で詰める。null なら字数どおり。</summary>
    public float? CharSpan { get; init; }

    /// <summary>役名の列の基準（左そろえは左端、右そろえは右端）から声優名の左端までの横の差。</summary>
    public float? ColumnGap { get; init; }

    /// <summary>行送り（行のベースラインどうしの間隔）。</summary>
    public float? Pitch { get; init; }

    /// <summary>声優名を割り付ける幅（字数）。</summary>
    public float? NameSpan { get; init; }
}

/// <summary>サブタイトルの組み方。</summary>
public sealed record TelopSubtitleLayout
{
    /// <summary>縦のそろえ方。<c>block-center</c>（行の塊の中央を画面の位置に置く）/ <c>first-line</c>（1 行目のベースラインを置く）。</summary>
    public string? Anchor { get; init; }

    /// <summary>行の最大幅（画面の幅に対する比）。超える行は長体で詰める。</summary>
    public float? MaxWidth { get; init; }

    /// <summary>長体の下限（横の縮めの率の最小）。これでも収まらない行は、全行の字を小さくする。</summary>
    public float? CondenseMin { get; init; }
}
