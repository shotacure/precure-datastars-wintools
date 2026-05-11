
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 統計ページ用の「どこまでの情報が反映されているか」表記（カバレッジラベル）を生成する共通ヘルパ
/// （v1.3.0 ブラッシュアップ続編で追加）。
/// <para>
/// 文言フォーマットは HomeGenerator の最終ビルド表記と揃えるが、日付の出処は異なり
/// <b>「カバレッジ最新話の放送日」</b>を採用する（HomeGenerator はビルド日を使う）：
/// </para>
/// <list type="bullet">
///   <item><description>該当 TV エピソードあり：<c>「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」</c>
///     （日付は当該エピソードの <see cref="Episode.OnAirAt"/> ベース）</description></item>
///   <item><description>該当無し（クリーン DB 等）：空文字を返す → テンプレ側で「ラベル無し」として空表示する</description></item>
/// </list>
/// <para>
/// 「最終話」の判定軸は統計ページごとに異なる：
/// </para>
/// <list type="bullet">
///   <item><description>サブタイトル統計：サブタイトル本文（<c>title_text</c>）が空でない最新 TV エピソード</description></item>
///   <item><description>エピソード尺統計：パート情報（<c>episode_parts</c> に行を持つ）最新 TV エピソード</description></item>
///   <item><description>クレジット統計：クレジットを持つ最新 TV エピソード（<see cref="CreditInvolvementIndex"/> 経由）</description></item>
/// </list>
/// <para>
/// シリーズ種別は「TV」のみを対象とする（<see cref="HomeGenerator"/> の <c>LatestAiredTvEpisode</c> と同じ方針で、
/// スピンオフ・映画・クロスオーバーは除外）。日付の粒度は日単位（時刻は付けない）。
/// </para>
/// </summary>
public static class StatsCoverageLabel
{
    /// <summary>
    /// カバレッジラベル文字列を組み立てる。
    /// 日付は <b>最終話の放送日</b>（<see cref="Episode.OnAirAt"/>）を使用し、ビルド日時とは無関係。
    /// 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」形式で返す。
    /// </summary>
    /// <param name="latest">
    /// 対象軸での最終話。null のときは空文字を返す（テンプレ側で「カバレッジラベル無し」として空表示にする）。
    /// </param>
    public static string Build((Series Series, Episode Episode)? latest)
    {
        if (latest is null) return string.Empty;
        var (series, episode) = latest.Value;
        var oa = episode.OnAirAt;
        string datePart = $"{oa.Year}年{oa.Month}月{oa.Day}日現在";
        return $"{datePart} 『{series.Title}』第{episode.SeriesEpNo}話時点の情報を表示しています";
    }

    /// <summary>
    /// サブタイトル本文が登録済みの最新 TV エピソードを判定する。
    /// 集計対象は <c>kind_code = 'TV'</c> のシリーズに限定。
    /// 「タイトルが入力されていればそこまで日付を進める」方針なので、未放送回でも対象にする。
    /// </summary>
    /// <param name="ctx">BuildContext（Series / EpisodesBySeries を参照）。</param>
    public static (Series Series, Episode Episode)? FindLatestTvEpisodeWithSubtitle(BuildContext ctx)
    {
        return FindLatestTvEpisodeWhere(ctx,
            (s, e) => !string.IsNullOrWhiteSpace(e.TitleText));
    }

    /// <summary>
    /// パート情報が登録済みの最新 TV エピソードを判定する。
    /// 「パート情報あり」の判定は呼び出し側が事前に取得した episode_id 集合で行う。
    /// 未放送回でもパート情報が登録されていれば対象にする。
    /// </summary>
    /// <param name="ctx">BuildContext。</param>
    /// <param name="episodeIdsWithParts"><c>episode_parts</c> に行を持つ episode_id の集合。</param>
    public static (Series Series, Episode Episode)? FindLatestTvEpisodeWithParts(
        BuildContext ctx, IReadOnlySet<int> episodeIdsWithParts)
    {
        return FindLatestTvEpisodeWhere(ctx,
            (s, e) => episodeIdsWithParts.Contains(e.EpisodeId));
    }

    /// <summary>
    /// クレジットが登録済みの最新 TV エピソードを判定する。
    /// 「クレジットあり」の判定は呼び出し側が事前に取得した episode_id 集合で行う。
    /// 未放送回でもクレジットが登録されていれば対象にする。
    /// </summary>
    /// <param name="ctx">BuildContext。</param>
    /// <param name="episodeIdsWithCredits">クレジットを持つ episode_id の集合。</param>
    public static (Series Series, Episode Episode)? FindLatestTvEpisodeWithCredits(
        BuildContext ctx, IReadOnlySet<int> episodeIdsWithCredits)
    {
        return FindLatestTvEpisodeWhere(ctx,
            (s, e) => episodeIdsWithCredits.Contains(e.EpisodeId));
    }

    /// <summary>
    /// <see cref="CreditInvolvementIndex"/> の各索引から、エピソード単位でクレジットを 1 件以上持つ
    /// episode_id の集合を作る。VOICE_CAST など全ロールを横断して数える。
    /// SERIES スコープのクレジット（<c>EpisodeId is null</c>）は集計対象外。
    /// </summary>
    /// <param name="index">クレジット索引。</param>
    public static HashSet<int> CollectEpisodeIdsWithCredits(CreditInvolvementIndex index)
    {
        var set = new HashSet<int>();
        // 4 つの索引すべてからエピソードを取り出す（人物 / 企業屋号 / ロゴ / キャラ屋号）。
        // 同じエピソードが複数索引に出てくることはあるが、HashSet なので重複は自然に排除される。
        AddFrom(index.ByPersonAlias, set);
        AddFrom(index.ByCompanyAlias, set);
        AddFrom(index.ByLogo, set);
        AddFrom(index.ByCharacterAlias, set);
        return set;
    }

    private static void AddFrom(IReadOnlyDictionary<int, IReadOnlyList<Involvement>> map, HashSet<int> dest)
    {
        foreach (var list in map.Values)
        {
            foreach (var inv in list)
            {
                if (inv.EpisodeId is int eid) dest.Add(eid);
            }
        }
    }

    /// <summary>
    /// 共通ループ：BuildContext の TV シリーズすべてのエピソードを走査し、
    /// 述語 <paramref name="predicate"/> を満たす中で OnAirAt が最大のエピソードを返す。
    /// 同 OnAirAt のときはエピソードリストの後ろを優先する（Episode 配列は series_ep_no 昇順なので、
    /// 結果として series_ep_no が最大のものが優先される）。
    /// <para>
    /// 「未放送だが情報入力済み」のエピソード（次週放送予定でサブタイトルが登録されている等）も対象に含める。
    /// 「入力済みの最終話」を求める用途では、放送日とビルド時刻の前後関係は問わない方針。
    /// </para>
    /// </summary>
    private static (Series Series, Episode Episode)? FindLatestTvEpisodeWhere(
        BuildContext ctx, Func<Series, Episode, bool> predicate)
    {
        (Series Series, Episode Episode)? best = null;
        DateTime latestSoFar = DateTime.MinValue;
        foreach (var s in ctx.Series)
        {
            // TV シリーズ限定（スピンオフ・映画・クロスオーバーは除外、HomeGenerator と同じ方針）。
            // 厳密には episodes テーブルには TV シリーズしか入らない運用ではあるが、念のため絞り込み。
            if (!string.Equals(s.KindCode, "TV", StringComparison.Ordinal)) continue;
            if (!ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            foreach (var e in eps)
            {
                if (!predicate(s, e)) continue;
                if (e.OnAirAt >= latestSoFar)
                {
                    latestSoFar = e.OnAirAt;
                    best = (s, e);
                }
            }
        }
        return best;
    }
}
