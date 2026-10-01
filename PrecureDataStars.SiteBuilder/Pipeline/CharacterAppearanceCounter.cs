namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// キャラクターの登場量を、TV 系（登場話数）と映画系（登場本数）に分けて数える。
/// キャラクター一覧とプリキュア一覧の件数バッジ（📺・🎥）で同じ数え方を使う。
/// 登場は声の出演のクレジット（<see cref="CreditInvolvementIndex.VoiceCastByCharacterAlias"/>）だけで数え、主題歌・挿入歌の歌唱は含めない。
/// </summary>
public static class CharacterAppearanceCounter
{
    /// <summary>
    /// キャラクターの名義（alias）群から登場量を数える。名義をまたいだ重複は (SeriesId, 話数) / SeriesId 単位で除く。
    /// TV 系（series_kinds.credit_attach_to='EPISODE'）は登場話数を、
    /// 映画系（'SERIES'：MOVIE / MOVIE_SHORT / SPRING / EVENT）は関与が 1 件以上あるシリーズを 1 本として数える。
    /// </summary>
    public static (int Episode, int Movie) Count(BuildContext ctx, CreditInvolvementIndex index, IEnumerable<int> characterAliasIds)
    {
        var tvEpisodes = new HashSet<(int SeriesId, int EpNo)>();
        var movieSeries = new HashSet<int>();
        foreach (var aliasId in characterAliasIds)
        {
            if (!index.VoiceCastByCharacterAlias.TryGetValue(aliasId, out var invs)) continue;
            foreach (var inv in invs)
            {
                if (!ctx.SeriesById.ContainsKey(inv.SeriesId)) continue;
                if (ctx.IsMovieKindSeries(inv.SeriesId))
                {
                    movieSeries.Add(inv.SeriesId);
                }
                else if (inv.EpisodeId is int eid)
                {
                    var ep = ctx.LookupEpisode(inv.SeriesId, eid);
                    if (ep is not null) tvEpisodes.Add((inv.SeriesId, ep.SeriesEpNo));
                }
            }
        }
        return (tvEpisodes.Count, movieSeries.Count);
    }
}
