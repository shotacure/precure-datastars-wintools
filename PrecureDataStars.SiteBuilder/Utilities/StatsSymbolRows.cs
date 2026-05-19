using System;
using System.Collections.Generic;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 「記号出現回数・初使用エピソード」ページの脱テーブルレイアウト 1 行分のビューモデル。
/// 左は使用文字 TOP100 と同じ大きな記号グリフ＋出現回数のみのパネル（順位は持たない）、
/// 右に初使用エピソード（シリーズと年度・放送日＋第N話・ルビ付きサブタイトル）を並べる。
/// 並びは初使用が早い順（リポジトリ側の順序）をそのまま保持する。
/// </summary>
public sealed class StatsSymbolRow
{
    /// <summary>対象記号（テンプレ側でエスケープ）。</summary>
    public string Char { get; init; } = "";

    /// <summary>指標値の表示文字列（数値のみ＝出現回数）。</summary>
    public string ValueLabel { get; init; } = "";

    /// <summary>初使用エピソードのシリーズ正式名称（プレーンテキスト。テンプレ側でエスケープ）。</summary>
    public string SeriesTitle { get; init; } = "";

    /// <summary>初使用エピソードのシリーズ詳細ページ URL。</summary>
    public string SeriesUrl { get; init; } = "";

    /// <summary>初使用エピソードのシリーズ開始年（西暦 4 桁）。</summary>
    public string SeriesStartYearLabel { get; init; } = "";

    /// <summary>「第N話」ラベル。</summary>
    public string EpNoLabel { get; init; } = "";

    /// <summary>「YYYY.M.D」形式の初使用放送日ラベル（括弧なし・必須。未登録時のみ空）。</summary>
    public string FirstDateLabel { get; init; } = "";

    /// <summary>初使用回のサブタイトル HTML（ルビ付き優先・改行除去済み。テンプレはそのまま出力）。</summary>
    public string TitleHtml { get; init; } = "";

    /// <summary>初使用エピソード詳細ページ URL。</summary>
    public string EpisodeUrl { get; init; } = "";
}

/// <summary>1 行分の入力（記号 / 出現回数 / 初使用エピソード情報）。</summary>
public readonly struct StatsSymbolInput
{
    public StatsSymbolInput(string ch, long totalCount, string firstSeriesSlug, int firstSeriesEpNo,
        string firstSeriesTitle, string firstSeriesStartYearLabel, DateOnly? firstBroadcastDate,
        string firstTitleTextFallback)
    {
        Char = ch;
        TotalCount = totalCount;
        FirstSeriesSlug = firstSeriesSlug;
        FirstSeriesEpNo = firstSeriesEpNo;
        FirstSeriesTitle = firstSeriesTitle;
        FirstSeriesStartYearLabel = firstSeriesStartYearLabel;
        FirstBroadcastDate = firstBroadcastDate;
        FirstTitleTextFallback = firstTitleTextFallback;
    }

    public string Char { get; }
    public long TotalCount { get; }
    public string FirstSeriesSlug { get; }
    public int FirstSeriesEpNo { get; }
    public string FirstSeriesTitle { get; }
    public string FirstSeriesStartYearLabel { get; }
    public DateOnly? FirstBroadcastDate { get; }
    public string FirstTitleTextFallback { get; }
}

/// <summary>
/// <see cref="StatsSymbolRow"/> 群を組み立てる共有ビルダー。初使用エピソードの解決
/// （シリーズ slug + 話数 → <c>Episode</c>）とルビ付き／改行除去サブタイトル生成は
/// <see cref="StatsEpisodeRows.BuildTitleHtml"/> を再利用する。初使用放送日は括弧なしの
/// 「YYYY.M.D」形式で、表示順は「放送日 → 第N話 → サブタイトル」（このページのみ日付重要のため）。
/// 並び替えは行わず、入力（初使用が早い順）の順序をそのまま保持する。
/// </summary>
public static class StatsSymbolRows
{
    public static List<StatsSymbolRow> Build(BuildContext ctx, IEnumerable<StatsSymbolInput> items)
    {
        var list = new List<StatsSymbolRow>();

        foreach (var it in items)
        {
            var ep = ctx.LookupEpisodeBySeriesEpNo(it.FirstSeriesSlug, it.FirstSeriesEpNo);

            // 初使用放送日：リポジトリの FirstBroadcastDate を優先し、無ければ解決済みエピソードの放送日。
            DateOnly? d = it.FirstBroadcastDate
                          ?? (ep is null ? (DateOnly?)null : DateOnly.FromDateTime(ep.OnAirAt));
            string firstDate = d is { } dt ? $"{dt.Year}.{dt.Month}.{dt.Day}" : "";

            list.Add(new StatsSymbolRow
            {
                Char = it.Char,
                ValueLabel = it.TotalCount.ToString(),
                SeriesTitle = it.FirstSeriesTitle,
                SeriesUrl = PathUtil.SeriesUrl(it.FirstSeriesSlug),
                SeriesStartYearLabel = it.FirstSeriesStartYearLabel,
                EpNoLabel = $"第{it.FirstSeriesEpNo}話",
                FirstDateLabel = firstDate,
                TitleHtml = StatsEpisodeRows.BuildTitleHtml(
                    ep?.TitleRichHtml,
                    !string.IsNullOrEmpty(ep?.TitleText) ? ep!.TitleText : it.FirstTitleTextFallback),
                EpisodeUrl = PathUtil.EpisodeUrl(it.FirstSeriesSlug, it.FirstSeriesEpNo)
            });
        }

        return list;
    }
}
