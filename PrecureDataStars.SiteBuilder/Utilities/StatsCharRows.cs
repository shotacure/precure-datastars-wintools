using System.Collections.Generic;
using System.Globalization;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>作品ごとの使われ方の列の 1 マス（その作品でその字が何回使われたか）。</summary>
public sealed class StatsCharSeriesCell
{
    /// <summary>マスの地色の濃さ（アクセント色の不透明度。その字がいちばん多く使われた作品を最も濃くする）。0 回は空文字（塗らない）。</summary>
    public string Shade { get; init; } = "";

    /// <summary>マウスを載せたときの説明（「2011年『スイートプリキュア♪』：21 回」）。</summary>
    public string Title { get; init; } = "";

    /// <summary>棒の色（「216, 27, 96」の形。5 年ごとに 2 色を交互に使う）。style 属性用。</summary>
    public string Rgb { get; init; } = StatsCharRows.BlockRgb(0);

    /// <summary>マスの高さ（%。その字がいちばん多く使われた作品を 100 とする）。0 回は空文字（CSS の細い線のまま）。</summary>
    public string Height { get; init; } = "";
}

/// <summary>
/// 使用文字 TOP100（全文字／漢字限定）の脱テーブルレイアウト 1 行分のビューモデル。
/// 左ブロックはエピソード単位統計ページと共通の <c>stats-ep-rank</c> アクセントパネル
/// （順位＋指標値＝出現回数）を再利用し、右ブロックに対象文字と、作品ごとの使われ方の列を置く。
/// 同じ行を、ページの頭の字の一覧図（回数が多い字ほど大きい札）にも使う。
/// </summary>
public sealed class StatsCharRow
{
    /// <summary>順位の表示文字列。同率順位が連続する場合、2 件目以降は空文字（指標値は常に表示・カラム固定幅で整列）。</summary>
    public string RankLabel { get; init; } = "";

    /// <summary>指標値の表示文字列（数値のみ＝出現回数）。</summary>
    public string ValueLabel { get; init; } = "";

    /// <summary>対象文字（テンプレ側でエスケープ）。</summary>
    public string Char { get; init; } = "";

    /// <summary>札と字の色の CSS クラス（全文字のページは字の種類 <c>ct-kanji</c> など、漢字限定のページは習う学年 <c>kg-1</c> など）。</summary>
    public string TypeClass { get; init; } = "";

    /// <summary>字の一覧図での札の字の大きさ（em。回数が多い字ほど大きい）。style 属性用。</summary>
    public string CloudSize { get; init; } = "1";

    /// <summary>字の一覧図の札にマウスを載せたときの説明（「3 位：1,739 回」）。</summary>
    public string CloudTitle { get; init; } = "";

    /// <summary>作品ごとの使われ方（本線の TV シリーズを放送順に並べたマス）。作品ごとの回数を渡さなかったときは空。</summary>
    public List<StatsCharSeriesCell> SeriesCells { get; init; } = new();

    /// <summary>いちばん多く使われた作品の説明（「最多：スイートプリキュア♪ 21 回」）。無ければ空。</summary>
    public string PeakLabel { get; init; } = "";

    /// <summary>順位の数（同率の 2 件目以降も入る）。同じ順位の字をまとめるのに使う。</summary>
    public int RankNo { get; init; }
}

/// <summary>
/// 同じ順位（同じ出現回数）の字のまとまり。左のパネル（順位と出現回数）は 1 回だけ出し、右に字を縦に並べる。
/// </summary>
public sealed class StatsCharGroup
{
    public string RankLabel { get; init; } = "";
    public string ValueLabel { get; init; } = "";

    public List<StatsCharRow> Entries { get; init; } = new();
}

/// <summary>1 行分の入力（順位は呼び出し側で確定済みの Wimbledon 順位。表示用の同率ブランク化は本ビルダーが行う）。</summary>
public readonly struct StatsCharInput
{
    public StatsCharInput(int rank, long totalCount, string ch)
    {
        Rank = rank;
        TotalCount = totalCount;
        Char = ch;
    }

    public int Rank { get; }
    public long TotalCount { get; }
    public string Char { get; }
}

/// <summary>作品ごとの使われ方の列に並べる 1 作品（放送順に渡す）。</summary>
/// <param name="SeriesId">作品。</param>
/// <param name="Title">作品名。</param>
/// <param name="Year">放送開始年。</param>
public readonly record struct StatsCharSeries(int SeriesId, string Title, int Year);

/// <summary>
/// <see cref="StatsCharRow"/> 群を組み立てる共有ビルダー。同率順位（直前行と
/// <see cref="StatsCharInput.Rank"/> が等しい）の 2 件目以降の順位を空文字にする処理を集約し、
/// エピソード単位統計（<see cref="StatsEpisodeRows"/>）と同じ整列方針を使用文字ページにも適用する。
/// </summary>
public static class StatsCharRows
{
    /// <summary>字の一覧図の札の字の大きさ（em）の下限と上限。</summary>
    private const double CloudMinEm = 0.95;
    private const double CloudMaxEm = 8.0;

    /// <summary>札の大きさを決める、回数の比に掛ける指数。1 に近いほど、多い字と少ない字の大きさの差が開く。</summary>
    private const double CloudPower = 0.8;

    /// <summary>
    /// 入力列（順位昇順で並んでいる前提）から表示行を生成する。
    /// 同率順位の 2 件目以降は順位を空文字にする（指標値は常に表示）。
    /// </summary>
    /// <param name="series">作品ごとの使われ方の列に並べる作品（放送順）。</param>
    /// <param name="countBySeriesChar">(作品, 字) → 回数。字は序数で比べる。</param>
    /// <param name="typeClass">字 → 札の色の CSS クラス。null なら字の種類（<see cref="StatsTitleMarker.TypeClass"/>）。</param>
    public static List<StatsCharRow> Build(
        IEnumerable<StatsCharInput> items,
        IReadOnlyList<StatsCharSeries>? series = null,
        IReadOnlyDictionary<(int SeriesId, string Char), long>? countBySeriesChar = null,
        Func<string, string>? typeClass = null)
    {
        typeClass ??= StatsTitleMarker.TypeClass;
        var list = new List<StatsCharRow>();
        bool havePrev = false;
        int prevRank = 0;
        var source = items.ToList();
        long max = source.Count > 0 ? source.Max(i => i.TotalCount) : 0;

        foreach (var it in source)
        {
            string rankLabel = (havePrev && prevRank == it.Rank) ? "" : it.Rank.ToString();
            prevRank = it.Rank;
            havePrev = true;

            var (cells, peakLabel) = series is not null && countBySeriesChar is not null
                ? BuildSeriesCells(series, countBySeriesChar, it.Char)
                : (new List<StatsCharSeriesCell>(), "");

            list.Add(new StatsCharRow
            {
                RankLabel = rankLabel,
                ValueLabel = it.TotalCount.ToString(),
                Char = it.Char,
                TypeClass = typeClass(it.Char),
                CloudSize = CloudSize(it.TotalCount, max),
                CloudTitle = $"{it.Rank} 位：{it.TotalCount:N0} 回",
                SeriesCells = cells,
                PeakLabel = peakLabel,
                RankNo = it.Rank
            });
        }

        return list;
    }

    /// <summary>
    /// 作品ごとの棒の色。最初の作品の年から 5 年ごとに、ピンクと青を交互に使う（1〜5 年目・6〜10 年目…）。
    /// 各色の最後の 1 本が、5・10・15・20 周年の年の作品になる。
    /// </summary>
    /// <param name="yearOffset">最初の作品の放送開始年からの年数（0 始まり）。</param>
    public static string BlockRgb(int yearOffset)
        => (Math.Max(0, yearOffset) / 5) % 2 == 0 ? "216, 27, 96" : "51, 112, 170";

    /// <summary>5 年ごとの区切りの最後の年（5・10・15・20 周年の年）か。</summary>
    public static bool IsAnniversaryOffset(int yearOffset) => yearOffset >= 0 && yearOffset % 5 == 4;

    /// <summary>続いている同じ順位の行を 1 つにまとめる。</summary>
    public static List<StatsCharGroup> BuildGroups(List<StatsCharRow> rows)
    {
        var groups = new List<StatsCharGroup>();
        int i = 0;
        while (i < rows.Count)
        {
            int j = i + 1;
            while (j < rows.Count && rows[j].RankNo == rows[i].RankNo) j++;
            groups.Add(new StatsCharGroup
            {
                RankLabel = rows[i].RankNo.ToString(CultureInfo.InvariantCulture),
                ValueLabel = rows[i].ValueLabel,
                Entries = rows.GetRange(i, j - i)
            });
            i = j;
        }
        return groups;
    }

    /// <summary>字の一覧図での札の字の大きさ（em）。回数の比の <see cref="CloudPower"/> 乗で、下限から上限の間に取る。style 属性用。</summary>
    public static string CloudSize(long count, long max)
        => (max > 0
            ? CloudMinEm + (CloudMaxEm - CloudMinEm) * Math.Pow(count / (double)max, CloudPower)
            : CloudMinEm).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// 作品ごとの使われ方のマスと、いちばん多く使われた作品の説明を組む。
    /// マスの濃さは、その字がいちばん多く使われた作品を最も濃くする（字ごとに濃さの物差しを取る）。
    /// </summary>
    public static (List<StatsCharSeriesCell> Cells, string PeakLabel) BuildSeriesCells(
        IReadOnlyList<StatsCharSeries> series,
        IReadOnlyDictionary<(int SeriesId, string Char), long> countBySeriesChar,
        string ch)
    {
        var cells = new List<StatsCharSeriesCell>();
        var counts = series.Select(s => countBySeriesChar.TryGetValue((s.SeriesId, ch), out var n) ? n : 0L).ToList();
        long peak = counts.Count > 0 ? counts.Max() : 0;
        for (int i = 0; i < series.Count; i++)
        {
            long n = counts[i];
            cells.Add(new StatsCharSeriesCell
            {
                Shade = n > 0 && peak > 0
                    ? (0.10 + 0.90 * n / (double)peak).ToString("0.00", CultureInfo.InvariantCulture)
                    : "",
                // 少ない回数でも棒が見えるよう、高さの下限を持たせる。
                Height = n > 0 && peak > 0
                    ? Math.Max(14.0, n * 100.0 / peak).ToString("0", CultureInfo.InvariantCulture)
                    : "",
                Rgb = BlockRgb(series[i].Year - series[0].Year),
                Title = $"{series[i].Year}年『{series[i].Title}』：{n:N0} 回"
            });
        }
        string peakLabel = peak > 0 ? $"最多：{series[counts.IndexOf(peak)].Title} {peak:N0} 回" : "";
        return (cells, peakLabel);
    }
}
