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

        foreach (var it in items)
        {
            string rankLabel = (havePrev && prevRank == it.Rank) ? "" : it.Rank.ToString();
            prevRank = it.Rank;
            havePrev = true;

            list.Add(new StatsCharRow
            {
                RankLabel = rankLabel,
                ValueLabel = it.TotalCount.ToString(),
                Char = it.Char
            });
        }

        return list;
    }
}
