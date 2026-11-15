namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// キャラクターの登場量を、本線の TV（登場話数）・映画系（登場本数）・大人向けスピンオフ（登場話数）に分けて数える。
/// キャラクター一覧とプリキュア一覧の件数バッジ（📺・🎥・🌙）で同じ数え方を使う。
/// 登場は声の出演のクレジット（<see cref="CreditInvolvementIndex.VoiceCastByCharacterAlias"/>）だけで数え、主題歌・挿入歌の歌唱は含めない。
/// </summary>
public static class CharacterAppearanceCounter
{
    /// <summary>
    /// キャラクターの名義（alias）群から登場量を数える。名義をまたいだ重複は (SeriesId, 話数) / SeriesId 単位で除く。
    /// TV 系（series_kinds.credit_attach_to='EPISODE'）は登場話数を、
    /// 映画系（'SERIES'：MOVIE / MOVIE_SHORT / SPRING / EVENT）は関与が 1 件以上あるシリーズを 1 本として数える。
    /// 大人向けスピンオフ（種別 OTONA）は登場話数を TV と分けて数え、それ以外のスピンオフはどれにも数えない
    /// （人物・企業詳細の担当量の札と同じ分け方。種別の判定は映画系より先に行う）。
    /// </summary>
    public static (int Episode, int Movie, int OtonaEpisode) Count(BuildContext ctx, CreditInvolvementIndex index, IEnumerable<int> characterAliasIds)
    {
        var tvEpisodes = new HashSet<(int SeriesId, int EpNo)>();
        var movieSeries = new HashSet<int>();
        var otonaEpisodes = new HashSet<(int SeriesId, int EpNo)>();
        foreach (var aliasId in characterAliasIds)
        {
            if (!index.VoiceCastByCharacterAlias.TryGetValue(aliasId, out var invs)) continue;
            foreach (var inv in invs)
            {
                if (!ctx.SeriesById.ContainsKey(inv.SeriesId)) continue;
                if (ctx.IsOtonaSeries(inv.SeriesId))
                {
                    if (inv.EpisodeId is int otonaEid && ctx.LookupEpisode(inv.SeriesId, otonaEid) is { } otonaEp)
                        otonaEpisodes.Add((inv.SeriesId, otonaEp.SeriesEpNo));
                }
                else if (ctx.IsSpinOffSeries(inv.SeriesId))
                {
                    // 大人向け以外のスピンオフは数えない。
                }
                else if (ctx.IsMovieKindSeries(inv.SeriesId))
                {
                    movieSeries.Add(inv.SeriesId);
                }
                // TV の話数は本線の TV だけを数える（スピンオフの話は入れない）
                else if (ctx.IsMainlineTvSeries(inv.SeriesId) && inv.EpisodeId is int eid)
                {
                    var ep = ctx.LookupEpisode(inv.SeriesId, eid);
                    if (ep is not null) tvEpisodes.Add((inv.SeriesId, ep.SeriesEpNo));
                }
            }
        }
        return (tvEpisodes.Count, movieSeries.Count, otonaEpisodes.Count);
    }
}
