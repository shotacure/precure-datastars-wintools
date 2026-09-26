using PrecureDataStars.Data.Models;

namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// <see cref="BuildContext"/> に対するシリーズ／エピソードの軽量ルックアップ拡張。
/// 人物・企業・キャラクター・プリキュアの各 Generator が、関与情報の並び替えや
/// 表示順決定のために個別の private メソッドとして同一実装を重複保持していた
/// （<c>SeriesStartDate</c> / <c>EpisodeSeriesEpNo</c> / <c>LookupEpisode</c>）。
/// いずれも <see cref="BuildContext.SeriesById"/> ／
/// <see cref="BuildContext.EpisodesBySeries"/> を引くだけの処理であり、
/// 参照する状態も挙動も完全一致していたため、本拡張に一本化した。
/// </summary>
public static class BuildContextLookupExtensions
{
    /// <summary>シリーズ ID から放送開始日を引く。並び替えキー用途のため、 未登録シリーズは末尾送りになるよう <see cref="DateOnly.MaxValue"/> を返す。</summary>
    public static DateOnly SeriesStartDate(this BuildContext ctx, int seriesId)
        => ctx.SeriesById.TryGetValue(seriesId, out var s) ? s.StartDate : DateOnly.MaxValue;

    /// <summary>シリーズ ID + エピソード ID から SeriesEpNo を引く（並び替え用、未登録時は int.MaxValue）。</summary>
    public static int EpisodeSeriesEpNo(this BuildContext ctx, int seriesId, int episodeId)
    {
        if (episodeId == 0) return -1; // シリーズスコープは先頭に
        var ep = ctx.LookupEpisode(seriesId, episodeId);
        return ep?.SeriesEpNo ?? int.MaxValue;
    }

    /// <summary>シリーズ ID + エピソード ID からエピソードモデルを引く。 未登録シリーズ・未登録エピソードは <c>null</c>。</summary>
    public static Episode? LookupEpisode(this BuildContext ctx, int seriesId, int episodeId)
    {
        if (!ctx.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        for (int i = 0; i < eps.Count; i++)
            if (eps[i].EpisodeId == episodeId) return eps[i];
        return null;
    }

    /// <summary>
    /// シリーズ slug + シリーズ内話数からエピソードモデルを引く。統計のエピソード単位ページが、
    /// 集計クエリ結果（slug と話数のみ保持）から放送日・ルビ付きサブタイトルを補完するために使う。
    /// 未登録 slug・該当話なしは <c>null</c>。
    /// </summary>
    public static Episode? LookupEpisodeBySeriesEpNo(this BuildContext ctx, string seriesSlug, int seriesEpNo)
    {
        if (string.IsNullOrEmpty(seriesSlug)) return null;
        if (!ctx.SeriesIdBySlug.TryGetValue(seriesSlug, out var seriesId)) return null;
        if (!ctx.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        for (int i = 0; i < eps.Count; i++)
            if (eps[i].SeriesEpNo == seriesEpNo) return eps[i];
        return null;
    }

    /// <summary>シリーズ ID から放送開始年（西暦 4 桁文字列）を引く。未登録シリーズは空文字。 シリーズ年度注釈（複数シリーズが並列で出る文脈の「年度」列・薄色 inline span）用。</summary>
    public static string StartYearLabel(this BuildContext ctx, int seriesId)
        => ctx.SeriesById.TryGetValue(seriesId, out var s) ? s.StartDate.Year.ToString() : "";

    /// <summary>シリーズ slug から放送開始年（西暦 4 桁文字列）を引く。未登録 slug は空文字。 統計系ページ（集計クエリ結果が slug のみ保持）のテーブル「年度」（または「初出年」）列用。</summary>
    public static string StartYearLabelBySlug(this BuildContext ctx, string seriesSlug)
        => ctx.SeriesIdBySlug.TryGetValue(seriesSlug, out var sid) ? ctx.StartYearLabel(sid) : "";

    /// <summary>当該シリーズが映画系（series_kinds.credit_attach_to='SERIES'。MOVIE / MOVIE_SHORT / SPRING / EVENT）かを判定する。 関与集計で「TV 話（📺）のエピソード参加」と「映画 本（🎥）のシリーズ参加」を分けるのに使う。 未登録シリーズ・未登録種別は安全側で <c>false</c>。</summary>
    public static bool IsMovieKindSeries(this BuildContext ctx, int seriesId)
        => ctx.SeriesById.TryGetValue(seriesId, out var s)
           && ctx.SeriesKindByCode.TryGetValue(s.KindCode, out var sk)
           && string.Equals(sk.CreditAttachTo, "SERIES", StringComparison.Ordinal);

    /// <summary>録音の出典シリーズ開始日を引く。出典が無い録音は末尾扱い（<see cref="DateOnly.MaxValue"/>）。 「歌った録音」の選択（複数あれば出典シリーズが最も早いものを採る）に使う。</summary>
    public static DateOnly RecordingSeriesStart(this BuildContext ctx, SongRecording rec)
        => rec.SeriesId is int sid && ctx.SeriesById.TryGetValue(sid, out var s) ? s.StartDate : DateOnly.MaxValue;

    /// <summary><see cref="ExpandSingerParticipants(BuildContext, bool, int?, int?, int?, int?, int?)"/> の <see cref="SongRecordingSinger"/> 版。</summary>
    public static IReadOnlyList<SingerParticipant> ExpandSingerParticipants(this BuildContext ctx, SongRecordingSinger s)
        => ctx.ExpandSingerParticipants(
            s.BillingKind == SingerBillingKind.Person,
            s.PersonAliasId, s.SlashPersonAliasId,
            s.CharacterAliasId, s.SlashCharacterAliasId, s.VoicePersonAliasId);

    /// <summary>
    /// 歌唱者 1 行（song_recording_singers）を「実際に歌唱した参加者」の列へ展開する。
    /// 楽曲の歌唱関与を人物・キャラクター・声優の各詳細ページや歌系役職集計へ載せる経路は、
    /// すべてこの展開結果を使う（表記そのものは展開せず、曲ページ等の歌唱者表示は元の行のまま）。
    /// <list type="bullet">
    ///   <item>PERSON 行：主名義・スラッシュ相方をそれぞれ人物参加者として返す。名義がユニット
    ///     （<see cref="BuildContext.UnitMembersByAlias"/> にメンバーを持つ）なら、ユニット名義自身に続けて
    ///     メンバーも返す。PERSON メンバーは人物参加者、CHARACTER メンバーはキャラ参加者
    ///     （声優名義が紐付いていれば声優も同じ参加者に載せる）。</item>
    ///   <item>CHARACTER_WITH_CV 行：主キャラ・スラッシュ相方キャラを、いずれも同じ声優付きのキャラ参加者として返す。</item>
    /// </list>
    /// 返却順は「行の並び → ユニットのメンバー順」。同一の (人物, キャラ) 組は 1 回だけ返す。
    /// </summary>
    public static IReadOnlyList<SingerParticipant> ExpandSingerParticipants(
        this BuildContext ctx,
        bool isPersonBilling,
        int? personAliasId,
        int? slashPersonAliasId,
        int? characterAliasId,
        int? slashCharacterAliasId,
        int? voicePersonAliasId)
    {
        var result = new List<SingerParticipant>(4);
        var seen = new HashSet<(int?, int?)>();
        void Add(int? personId, int? charId)
        {
            if (personId is null && charId is null) return;
            if (seen.Add((personId, charId))) result.Add(new SingerParticipant(personId, charId));
        }

        if (isPersonBilling)
        {
            foreach (var aliasId in new[] { personAliasId, slashPersonAliasId })
            {
                if (aliasId is not int aid) continue;
                Add(aid, null);
                if (!ctx.UnitMembersByAlias.TryGetValue(aid, out var members)) continue;
                foreach (var m in members)
                {
                    if (m.MemberKind == PersonAliasMemberKind.Person)
                        Add(m.MemberPersonAliasId, null);
                    else
                        Add(m.MemberVoicePersonAliasId, m.MemberCharacterAliasId);
                }
            }
        }
        else
        {
            Add(voicePersonAliasId, characterAliasId);
            if (slashCharacterAliasId is int sca) Add(voicePersonAliasId, sca);
        }
        return result;
    }
}

/// <summary>
/// 歌唱者行を展開した 1 参加者。
/// 人物のみ（<see cref="CharacterAliasId"/> が null）＝人物名義での歌唱、
/// キャラ付き＝キャラクターとしての歌唱で <see cref="PersonAliasId"/> はその声優（未紐付けなら null）。
/// </summary>
public readonly record struct SingerParticipant(int? PersonAliasId, int? CharacterAliasId);
