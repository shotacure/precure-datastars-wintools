using System.Collections.Generic;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 使用文字 TOP100（全文字／漢字限定）の脱テーブルレイアウト 1 行分のビューモデル。
/// 左ブロックはエピソード単位統計ページと共通の <c>stats-ep-rank</c> アクセントパネル
/// （順位＋指標値＝出現回数）を再利用し、右ブロックに対象文字を超特大表示する。
/// </summary>
public sealed class StatsCharRow
{
    /// <summary>順位の表示文字列。同率順位が連続する場合、2 件目以降は空文字（指標値は常に表示・カラム固定幅で整列）。</summary>
    public string RankLabel { get; init; } = "";

    /// <summary>指標値の表示文字列（数値のみ＝出現回数）。</summary>
    public string ValueLabel { get; init; } = "";

    /// <summary>対象文字（テンプレ側でエスケープ）。</summary>
    public string Char { get; init; } = "";

    /// <summary>順位の数（同率の 2 件目以降も入る）。上位 3 件の枠の判定と表示に使う。</summary>
    public int RankNo { get; init; }

    /// <summary>出現回数の横棒の長さ（1 位を 100 とした %）。style 属性用。</summary>
    public string BarPercent { get; init; } = "0";

    /// <summary>上位 3 件の枠で、この行を全幅の大きい枠にするか（単独 1 位のとき）。</summary>
    public bool IsFirst { get; set; }
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

/// <summary>
/// <see cref="StatsCharRow"/> 群を組み立てる共有ビルダー。同率順位（直前行と
/// <see cref="StatsCharInput.Rank"/> が等しい）の 2 件目以降の順位を空文字にする処理を集約し、
/// エピソード単位統計（<see cref="StatsEpisodeRows"/>）と同じ整列方針を使用文字ページにも適用する。
/// </summary>
public static class StatsCharRows
{
    /// <summary>
    /// 入力列（順位昇順で並んでいる前提）から表示行を生成する。
    /// 同率順位の 2 件目以降は順位を空文字にする（指標値は常に表示）。
    /// </summary>
    public static List<StatsCharRow> Build(IEnumerable<StatsCharInput> items)
    {
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

            list.Add(new StatsCharRow
            {
                RankLabel = rankLabel,
                ValueLabel = it.TotalCount.ToString(),
                Char = it.Char,
                RankNo = it.Rank,
                BarPercent = (max > 0 ? it.TotalCount * 100.0 / max : 0.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        return list;
    }

    /// <summary>
    /// 行の並びを、上位 3 件の枠に出す行（3 位までの全件。6 件を超えるなら枠を出さない）と、残りの行に分ける。
    /// 単独 1 位の行には <see cref="StatsCharRow.IsFirst"/> を立てる。エピソード単位の順位のページ（<see cref="StatsRankingView"/>）と同じ決まり。
    /// </summary>
    public static (List<StatsCharRow> Podium, List<StatsCharRow> Rows) SplitPodium(List<StatsCharRow> rows)
    {
        var podium = rows.Where(r => r.RankNo is >= 1 and <= 3).ToList();
        if (podium.Count == 0 || podium.Count > 6) return (new List<StatsCharRow>(), rows);
        if (podium.Count(r => r.RankNo == 1) == 1) podium[0].IsFirst = true;
        return (podium, rows.Where(r => !(r.RankNo is >= 1 and <= 3)).ToList());
    }
}
