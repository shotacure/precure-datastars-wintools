using System.Net;
using System.Text.RegularExpressions;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 統計のエピソード単位ページ（パート尺・中 CM 入り時刻・アバンスキップ回・サブタイトル文字数/漢字率/記号率）
/// で共通利用する 1 行分のビューモデル。テーブルではなく
/// 「順位＋指標値（左・やや大きめ）／シリーズと年度・第N話と放送日・ルビ付きサブタイトル」
/// の脱テーブルレイアウトを全エピソード単位ページで統一するために設ける。
/// </summary>
public sealed class StatsEpisodeRow
{
    /// <summary>順位・指標値を持つか（アバンスキップ回は順位も指標値も持たない）。false のとき左ブロックを描画しない。</summary>
    public bool HasRank { get; init; }

    /// <summary>順位の表示文字列。同率順位が連続する場合、2 件目以降は空文字（カラム幅は固定で全行整列）。</summary>
    public string RankLabel { get; init; } = "";

    /// <summary>指標値の表示文字列（数値のみ。例: 文字数 / m:ss / h:mm:ss / 37.5%）。</summary>
    public string ValueLabel { get; init; } = "";

    /// <summary>シリーズ正式名称（プレーンテキスト。テンプレ側でエスケープ）。</summary>
    public string SeriesTitle { get; init; } = "";

    /// <summary>シリーズ詳細ページ URL。</summary>
    public string SeriesUrl { get; init; } = "";

    /// <summary>シリーズ開始年（西暦 4 桁）。</summary>
    public string SeriesStartYearLabel { get; init; } = "";

    /// <summary>シリーズ内話数。</summary>
    public int SeriesEpNo { get; init; }

    /// <summary>
    /// サブタイトル HTML。ルビ付き（<c>TitleRichHtml</c>）があればそれを、無ければ
    /// <c>TitleText</c> をエスケープしたもの。いずれも改行（<c>&lt;br&gt;</c>・LF/CR）を除去済みで
    /// テンプレ側はそのまま（エスケープせず）出力する。
    /// </summary>
    public string TitleHtml { get; init; } = "";

    /// <summary>エピソード詳細ページ URL。</summary>
    public string EpisodeUrl { get; init; } = "";
}

/// <summary>1 行分の入力（順位は呼び出し側で確定済みの Wimbledon 順位、表示用の同率ブランク化は本ビルダーが行う）。</summary>
public readonly struct StatsEpisodeInput
{
    public StatsEpisodeInput(string seriesSlug, int seriesEpNo, string seriesTitle,
        string seriesStartYearLabel, bool hasRank, int rank, string valueLabel, string titleTextFallback)
    {
        SeriesSlug = seriesSlug;
        SeriesEpNo = seriesEpNo;
        SeriesTitle = seriesTitle;
        SeriesStartYearLabel = seriesStartYearLabel;
        HasRank = hasRank;
        Rank = rank;
        ValueLabel = valueLabel;
        TitleTextFallback = titleTextFallback;
    }

    public string SeriesSlug { get; }
    public int SeriesEpNo { get; }
    public string SeriesTitle { get; }
    public string SeriesStartYearLabel { get; }
    public bool HasRank { get; }
    public int Rank { get; }
    public string ValueLabel { get; }
    public string TitleTextFallback { get; }
}

/// <summary>
/// <see cref="StatsEpisodeRow"/> 群を組み立てる共有ビルダー。エピソード解決
/// （シリーズ slug + 話数 → <c>Episode</c>）、放送日整形、ルビ付き／改行除去サブタイトル生成、
/// 同率順位のブランク化を 1 箇所に集約し、パート尺・サブタイトル両ジェネレータから再利用する。
/// </summary>
public static class StatsEpisodeRows
{
    // <br> 各種表記。改行は半角スペースへ畳む。
    private static readonly Regex BrTag = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NewLines = new(@"[\r\n]+", RegexOptions.Compiled);
    private static readonly Regex MultiSpace = new(@"[ \t\u3000]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// 入力列（順位昇順で並んでいる前提）から表示行を生成する。
    /// 同率順位（直前行と <see cref="StatsEpisodeInput.Rank"/> が等しい）の 2 件目以降は順位を空文字にする
    /// （指標値は常に表示。カラムは固定幅で整列）。
    /// </summary>
    public static List<StatsEpisodeRow> Build(BuildContext ctx, IEnumerable<StatsEpisodeInput> items)
    {
        var list = new List<StatsEpisodeRow>();
        bool havePrev = false;
        int prevRank = 0;

        foreach (var it in items)
        {
            string rankLabel = "";
            if (it.HasRank)
            {
                rankLabel = (havePrev && prevRank == it.Rank) ? "" : it.Rank.ToString();
                prevRank = it.Rank;
                havePrev = true;
            }

            var ep = ctx.LookupEpisodeBySeriesEpNo(it.SeriesSlug, it.SeriesEpNo);

            list.Add(new StatsEpisodeRow
            {
                HasRank = it.HasRank,
                RankLabel = rankLabel,
                ValueLabel = it.ValueLabel,
                SeriesTitle = it.SeriesTitle,
                SeriesUrl = PathUtil.SeriesUrl(it.SeriesSlug),
                SeriesStartYearLabel = it.SeriesStartYearLabel,
                SeriesEpNo = it.SeriesEpNo,
                TitleHtml = BuildTitleHtml(
                    ep?.TitleRichHtml,
                    !string.IsNullOrEmpty(ep?.TitleText) ? ep!.TitleText : it.TitleTextFallback),
                EpisodeUrl = PathUtil.EpisodeUrl(it.SeriesSlug, it.SeriesEpNo)
            });
        }

        return list;
    }

    /// <summary>
    /// ルビ付き優先のサブタイトル HTML を返す。<paramref name="richHtml"/>（<c>&lt;ruby&gt;</c> を含む
    /// 信頼済み HTML）があればルビ構造を保ったまま改行のみ除去、無ければプレーン本文をエスケープして
    /// 改行除去する。いずれの場合も <c>&lt;br&gt;</c>・改行コードは半角スペースへ畳む。
    /// </summary>
    private static string BuildTitleHtml(string? richHtml, string plainFallback)
    {
        if (!string.IsNullOrWhiteSpace(richHtml))
            return StripBreaks(richHtml);
        return StripBreaks(WebUtility.HtmlEncode(plainFallback ?? ""));
    }

    private static string StripBreaks(string s)
    {
        s = BrTag.Replace(s, " ");
        s = NewLines.Replace(s, " ");
        s = MultiSpace.Replace(s, " ");
        return s.Trim();
    }
}
