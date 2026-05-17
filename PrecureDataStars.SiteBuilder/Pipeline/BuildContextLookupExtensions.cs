using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// <see cref="BuildContext"/> に対するシリーズ／エピソードの軽量ルックアップ拡張。
/// <para>
/// 人物・企業・キャラクター・プリキュアの各 Generator が、関与情報の並び替えや
/// 表示順決定のために個別の private メソッドとして同一実装を重複保持していた
/// （<c>SeriesStartDate</c> / <c>EpisodeSeriesEpNo</c> / <c>LookupEpisode</c>）。
/// いずれも <see cref="BuildContext.SeriesById"/> ／
/// <see cref="BuildContext.EpisodesBySeries"/> を引くだけの処理であり、
/// 参照する状態も挙動も完全一致していたため、本拡張に一本化した。
/// </para>
/// </summary>
public static class BuildContextLookupExtensions
{
    /// <summary>
    /// シリーズ ID から放送開始日を引く。並び替えキー用途のため、
    /// 未登録シリーズは末尾送りになるよう <see cref="DateOnly.MaxValue"/> を返す。
    /// </summary>
    public static DateOnly SeriesStartDate(this BuildContext ctx, int seriesId)
        => ctx.SeriesById.TryGetValue(seriesId, out var s) ? s.StartDate : DateOnly.MaxValue;

    /// <summary>
    /// シリーズ ID + エピソード ID から SeriesEpNo を引く（並び替え用、未登録時は int.MaxValue）。
    /// </summary>
    public static int EpisodeSeriesEpNo(this BuildContext ctx, int seriesId, int episodeId)
    {
        if (episodeId == 0) return -1; // シリーズスコープは先頭に
        var ep = ctx.LookupEpisode(seriesId, episodeId);
        return ep?.SeriesEpNo ?? int.MaxValue;
    }

    /// <summary>
    /// シリーズ ID + エピソード ID からエピソードモデルを引く。
    /// 未登録シリーズ・未登録エピソードは <c>null</c>。
    /// </summary>
    public static Episode? LookupEpisode(this BuildContext ctx, int seriesId, int episodeId)
    {
        if (!ctx.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        for (int i = 0; i < eps.Count; i++)
            if (eps[i].EpisodeId == episodeId) return eps[i];
        return null;
    }
}
