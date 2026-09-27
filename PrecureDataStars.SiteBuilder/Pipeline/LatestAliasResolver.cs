namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 「全クレジット横断で最後に使われた名義」を決める共通処理。
/// 人物詳細の見出し・URL（<see cref="EntityUrlRegistry"/>）と、役職詳細・スタッフ一覧の行表記
/// （CreatorsGenerator）が同じ規則で名義を選ぶために 1 か所にまとめる。
/// 「最後」は関与の (シリーズ放送開始日, 話数, クレジット出現位置) が最も遅いもの（役職・種別を問わない）。
/// </summary>
public static class LatestAliasResolver
{
    /// <summary>関与のクレジット上の並び順キー (シリーズ放送開始日シリアル, 話数, クレジット出現位置)。 シリーズスコープ（episode_id=null）は話数 0。</summary>
    public static (long Start, int EpNo, long Pos) CreditOrderKey(BuildContext ctx, Involvement inv)
    {
        long start = ctx.SeriesStartDate(inv.SeriesId).DayNumber;
        int epNo = inv.EpisodeId is int eid
            ? (ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
            : 0;
        return (start, epNo, inv.CreditPos);
    }

    /// <summary>
    /// person_id → 最新名義の person_alias_id。TV 系シリーズ（credit_attach_to='EPISODE'）のクレジットで
    /// 最後に使われた名義を正とする（映画のクレジットは表記が TV と違うことがあるため、見出しの名乗りには使わない）。
    /// TV 系のクレジットが 1 件も無い人物に限り、映画系を含めた全クレジットで最後に使われた名義にする。
    /// 複数の人物が共有する名義（共同名義）は、その人物個人の名前として扱えないため候補から外す。
    /// クレジットに一度も出ない人物は辞書に載らない（呼び出し側で正式名にフォールバックする）。
    /// </summary>
    public static Dictionary<int, int> LatestPersonAliasIds(BuildContext ctx, CreditInvolvementIndex index)
    {
        // 名義 → それを持つ人物の数（共同名義の判定用）。
        var ownerCount = new Dictionary<int, int>();
        foreach (var aliasIds in ctx.AliasIdsByPerson.Values)
        {
            foreach (var aid in aliasIds)
                ownerCount[aid] = ownerCount.TryGetValue(aid, out var n) ? n + 1 : 1;
        }

        var result = new Dictionary<int, int>();
        foreach (var (personId, aliasIds) in ctx.AliasIdsByPerson)
        {
            // TV 系で最後の名義 → 無ければ全クレジットで最後の名義。
            int? Pick(bool tvOnly)
            {
                var best = (Start: long.MinValue, EpNo: int.MinValue, Pos: long.MinValue);
                int? bestAid = null;
                foreach (var aid in aliasIds)
                {
                    if (ownerCount.TryGetValue(aid, out var owners) && owners > 1) continue;
                    if (!index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                    foreach (var inv in invs)
                    {
                        if (tvOnly && ctx.IsMovieKindSeries(inv.SeriesId)) continue;
                        var key = CreditOrderKey(ctx, inv);
                        if (bestAid is null || key.CompareTo(best) > 0) { best = key; bestAid = aid; }
                    }
                }
                return bestAid;
            }
            if ((Pick(tvOnly: true) ?? Pick(tvOnly: false)) is int b) result[personId] = b;
        }
        return result;
    }
}
