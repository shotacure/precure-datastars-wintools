using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 人物詳細・企業/団体詳細・ユニット詳細・キャラクター詳細の「年表」（役職・役柄・声優ごとの線表）を組み立てる。
/// <list type="bullet">
///   <item><description>行は、本編クレジットの役職（声の出演を除く）ごと、声の出演の演じたキャラごと（人物だけ）、音楽の役職
///     （作詞・作曲・編曲・歌唱・演奏・録音など。歌・劇伴・盤のクレジットを役職ごとにまとめる）ごと。役職の行は行頭を札の印、
///     キャラの行は星の印にして見分け、名前は役職詳細・キャラクター詳細へリンクする。並びは最初の参加の早い順
///     （<see cref="RoleTimelineBuilder.Build"/> の決まり）。</description></item>
///   <item><description>人物の音楽の役職は、歌の作家・歌唱（song_credits / song_recording_singers）、劇伴の作家
///     （bgm_cue_credits）、盤の音楽クレジット（music_credits）から（ユニットも同じで、ユニットの名義の分）。企業・団体は、会社そのものが
///     載った盤の音楽クレジットから。</description></item>
///   <item><description>キャラクターの行は、声の出演を演じた声優ごと（人物の印。声優のページへリンク）と、キャラの名義で歌った歌の
///     役職（歌唱・コーラス・台詞など）ごと。</description></item>
///   <item><description>横軸はその参加のある期間だけ（<see cref="RoleTimelineBuilder.ForParticipation"/>）。</description></item>
///   <item><description>閉じた状態で見せる 1 本の帯（<see cref="CreditTimelineView.Summary"/>）は、すべての参加を 1 行にまとめたもの。</description></item>
/// </list>
/// 日付の付く参加（TV の話・映画・歌・劇伴・盤）が 1 つも無ければ作らない。
/// </summary>
internal sealed class CreditTimelineBuilder
{
    private readonly BuildContext _ctx;
    private readonly MusicTimelineDates _musicDates;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<(int SongId, string RoleCode)>> _songRolesByAlias;
    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, SongRecording>> _sungRecordingByAlias;

    /// <param name="ctx">ビルド全体の共有データ。</param>
    /// <param name="musicDates">歌・劇伴・盤の日付。</param>
    /// <param name="songRolesByAlias">名義 → 担当した曲と役職（人物・ユニット詳細は人物の名義、キャラクター詳細はキャラの名義。企業・団体詳細では null）。</param>
    /// <param name="sungRecordingByAlias">名義 → 曲 → 歌った録音（<paramref name="songRolesByAlias"/> と同じ名義。企業・団体詳細では null）。</param>
    public CreditTimelineBuilder(
        BuildContext ctx,
        MusicTimelineDates musicDates,
        IReadOnlyDictionary<int, IReadOnlyList<(int SongId, string RoleCode)>>? songRolesByAlias = null,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, SongRecording>>? sungRecordingByAlias = null)
    {
        _ctx = ctx;
        _musicDates = musicDates;
        _songRolesByAlias = songRolesByAlias ?? new Dictionary<int, IReadOnlyList<(int, string)>>();
        _sungRecordingByAlias = sungRecordingByAlias ?? new Dictionary<int, IReadOnlyDictionary<int, SongRecording>>();
    }

    /// <summary>
    /// 人物 1 人分の年表を組む。描ける参加が無ければ null。
    /// </summary>
    /// <param name="aliasIds">人物の名義。</param>
    /// <param name="mainInvolvements">本編クレジットの関与（声の出演を含む。主題歌・劇伴経由のものは含めない）。</param>
    /// <param name="involvementGroups">本編クレジットの役職グループ（行の役職名とリンク先に使う）。</param>
    public CreditTimelineView? BuildForPerson(
        IReadOnlyList<int> aliasIds,
        IReadOnlyList<Involvement> mainInvolvements,
        IReadOnlyList<InvolvementGroup> involvementGroups)
    {
        var entities = new List<RoleTimelineEntity>();
        int roleRows = AddCreditRoleRows(entities, mainInvolvements, involvementGroups);

        // 声の出演は演じたキャラごと（名義をまたいで同じキャラにまとめる。キャラ名義の無い生の役名だけのクレジットは描かない）。
        int characterRows = 0;
        foreach (var byChar in mainInvolvements
                     .Where(i => i.IsVoiceCast && i.CharacterAliasId is int caId && _ctx.CharacterAliasById.ContainsKey(caId))
                     .GroupBy(i => _ctx.CharacterAliasById[i.CharacterAliasId!.Value].CharacterId))
        {
            var (episodes, movies) = Split(byChar);
            if (episodes.Count == 0 && movies.Count == 0) continue;
            // 役名は、その人がそのキャラで最も多くクレジットされた名義（苗字の無い名義ならフルネームの名義）。
            var alias = byChar
                .GroupBy(i => i.CharacterAliasId!.Value)
                .OrderByDescending(a => a.Count())
                .ThenBy(a => a.Key)
                .Select(a => _ctx.CharacterAliasById[a.Key])
                .First();
            entities.Add(new RoleTimelineEntity
            {
                EntityKind = "character",
                EntityName = _ctx.CharacterAliasNames.DisplayName(alias),
                EntityUrl = PathUtil.CharacterUrl(byChar.Key),
                FirstSortPos = entities.Count,
                Episodes = episodes,
                MovieSeriesIds = movies
            });
            characterRows++;
        }

        // 音楽の役職：歌の作家・歌唱、劇伴の作家、盤の音楽クレジットを、役職ごとに 1 行にまとめる。
        var aliasSet = aliasIds.ToHashSet();
        roleRows += AddMusicRoleRows(entities, (music, keyOf) =>
        {
            foreach (int aliasId in aliasIds)
            {
                if (_songRolesByAlias.TryGetValue(aliasId, out var songRoles))
                {
                    _sungRecordingByAlias.TryGetValue(aliasId, out var sung);
                    foreach (var (songId, roleCode) in songRoles)
                    {
                        // 歌唱は歌った録音の日、作家は曲の日に置く。
                        int? recId = PathUtil.IsSingerRole(roleCode) && sung is not null && sung.TryGetValue(songId, out var rec)
                            ? rec.SongRecordingId
                            : null;
                        music.AddSong(keyOf(roleCode), songId, recId);
                    }
                }
                if (_ctx.MusicCredits.ByPersonAlias.TryGetValue(aliasId, out var credits))
                    foreach (var r in credits) music.AddMusicCredit(keyOf(r.RoleCode), r);
            }
            foreach (var (cue, credits) in _ctx.BgmCueCreditsByCue)
                foreach (var c in credits)
                    if (c.PersonAliasId is int aid && aliasSet.Contains(aid))
                        music.AddCue(keyOf(c.CreditRole), cue.SeriesId, cue.MNoDetail);
        });

        return Finish(entities, roleRows, characterRows);
    }

    /// <summary>
    /// キャラクター 1 体分の年表を組む。描ける参加が無ければ null。
    /// </summary>
    /// <param name="characterAliasIds">キャラの名義。</param>
    /// <param name="voiceInvolvements">キャラの声の出演のクレジット（歌唱は含めない）。</param>
    public CreditTimelineView? BuildForCharacter(IReadOnlyList<int> characterAliasIds, IReadOnlyList<Involvement> voiceInvolvements)
    {
        var entities = new List<RoleTimelineEntity>();

        // 声の出演は演じた声優ごと（声優の名義をまたいで同じ人物にまとめる。人物に結び付かない名義のクレジットは描かない）。
        int personRows = 0;
        foreach (var byPerson in voiceInvolvements
                     .Where(i => i.IsVoiceCast && i.PersonAliasId is int pa && _ctx.PersonIdByAlias.ContainsKey(pa))
                     .GroupBy(i => _ctx.PersonIdByAlias[i.PersonAliasId!.Value]))
        {
            var (episodes, movies) = Split(byPerson);
            if (episodes.Count == 0 && movies.Count == 0) continue;
            entities.Add(new RoleTimelineEntity
            {
                EntityKind = "person",
                EntityName = _ctx.EntityUrls.PersonDisplayLabel(byPerson.Key) ?? "",
                EntityUrl = PathUtil.PersonUrl(byPerson.Key),
                FirstSortPos = entities.Count,
                Episodes = episodes,
                MovieSeriesIds = movies
            });
            personRows++;
        }

        // キャラの名義で歌った歌（歌唱は歌った録音の日に置く）。
        int roleRows = AddMusicRoleRows(entities, (music, keyOf) =>
        {
            foreach (int aliasId in characterAliasIds)
            {
                if (!_songRolesByAlias.TryGetValue(aliasId, out var songRoles)) continue;
                _sungRecordingByAlias.TryGetValue(aliasId, out var sung);
                foreach (var (songId, roleCode) in songRoles)
                {
                    int? recId = sung is not null && sung.TryGetValue(songId, out var rec) ? rec.SongRecordingId : null;
                    music.AddSong(keyOf(roleCode), songId, recId);
                }
            }
        });

        return Finish(entities, roleRows, 0, verb: roleRows > 0 ? "参加" : "登場");
    }

    /// <summary>
    /// 企業・団体 1 つ分の年表を組む。描ける参加が無ければ null。
    /// </summary>
    /// <param name="involvements">本編クレジットの関与（屋号・ロゴ・ブロックの先頭の会社として）。</param>
    /// <param name="involvementGroups">本編クレジットの役職グループ（行の役職名とリンク先に使う）。</param>
    /// <param name="musicCredits">会社そのものが載った盤の音楽クレジット（所属として載った人物の行は含めない）。</param>
    public CreditTimelineView? BuildForCompany(
        IReadOnlyList<Involvement> involvements,
        IReadOnlyList<InvolvementGroup> involvementGroups,
        IEnumerable<MusicCredit> musicCredits)
    {
        var entities = new List<RoleTimelineEntity>();
        int roleRows = AddCreditRoleRows(entities, involvements, involvementGroups);
        roleRows += AddMusicRoleRows(entities, (music, keyOf) =>
        {
            foreach (var r in musicCredits) music.AddMusicCredit(keyOf(r.RoleCode), r);
        });
        return Finish(entities, roleRows, 0);
    }

    /// <summary>本編クレジットの役職ごとの行を足し、足した行の数を返す（声の出演の役職グループはキャラごとの行にするので除く）。</summary>
    private int AddCreditRoleRows(List<RoleTimelineEntity> entities, IReadOnlyList<Involvement> involvements, IReadOnlyList<InvolvementGroup> involvementGroups)
    {
        int added = 0;
        foreach (var g in involvementGroups)
        {
            if (g.HasCharacterColumn || string.IsNullOrEmpty(g.RoleCode)) continue;
            var invs = involvements.Where(i => !i.IsVoiceCast && string.Equals(i.RoleCode, g.RoleCode, StringComparison.Ordinal));
            var (episodes, movies) = Split(invs);
            if (episodes.Count == 0 && movies.Count == 0) continue;
            entities.Add(new RoleTimelineEntity
            {
                EntityKind = "role",
                EntityName = g.RoleLabel,
                EntityUrl = g.RoleUrl,
                FirstSortPos = entities.Count,
                Episodes = episodes,
                MovieSeriesIds = movies
            });
            added++;
        }
        return added;
    }

    /// <summary>
    /// 音楽の役職ごとの行を足し、足した行の数を返す。<paramref name="fill"/> は参加を役職のキー（2 つ目の引数で役職コードから引く）で
    /// 年表の入力に足す。役職の並びは最初に出てきた順（行の並びは最終的に最初の参加の早い順）。
    /// </summary>
    private int AddMusicRoleRows(List<RoleTimelineEntity> entities, Action<MusicTimelineCollector, Func<string, (char Kind, int Id)>> fill)
    {
        var music = new MusicTimelineCollector(_ctx, _musicDates);
        var roleOrder = new List<string>();
        (char Kind, int Id) KeyOf(string roleCode)
        {
            int idx = roleOrder.IndexOf(roleCode);
            if (idx < 0)
            {
                roleOrder.Add(roleCode);
                idx = roleOrder.Count - 1;
            }
            return ('M', idx);
        }
        fill(music, KeyOf);

        int added = 0;
        for (int i = 0; i < roleOrder.Count; i++)
        {
            string code = roleOrder[i];
            var e = music.ToEntity(('M', i), "role", MusicCreditViewBuilder.RoleName(_ctx, code), MusicRoleUrl(code), entities.Count);
            if (e is null) continue;
            entities.Add(e);
            added++;
        }
        return added;
    }

    /// <summary>行から線表と閉じた状態の帯を組む。描ける参加が無ければ null。</summary>
    /// <param name="verb">凡例の動詞。null なら行の顔ぶれで決める（役柄だけなら「出演」、役職だけなら「担当」、両方なら「参加」）。</param>
    private CreditTimelineView? Finish(List<RoleTimelineEntity> entities, int roleRows, int characterRows, string? verb = null)
    {
        if (entities.Count == 0) return null;
        var dates = entities.SelectMany(ParticipationDates).ToList();
        if (dates.Count == 0) return null;
        var builder = RoleTimelineBuilder.ForParticipation(_ctx, dates);

        bool hasVoice = characterRows > 0;
        bool hasRole = roleRows > 0;
        var rules = RoleTimelineRules.Credit with { Verb = verb ?? (hasVoice && hasRole ? "参加" : hasVoice ? "出演" : "担当") };
        var timeline = builder.Build(entities, rules);
        if (timeline is null) return null;

        // 閉じた状態の 1 本の帯：すべての参加を 1 行にまとめる。
        var all = new RoleTimelineEntity
        {
            EntityKind = "role",
            EntityName = "",
            EntityUrl = "",
            Episodes = entities.SelectMany(e => e.Episodes).ToHashSet(),
            MovieSeriesIds = entities.SelectMany(e => e.MovieSeriesIds).ToHashSet(),
            Songs = entities.SelectMany(e => e.Songs).ToList(),
            Bgms = entities.SelectMany(e => e.Bgms).ToList(),
            Products = entities.SelectMany(e => e.Products).ToList()
        };
        var summary = builder.Build(new[] { all }, rules)?.Rows.FirstOrDefault();

        int firstYear = dates.Min().Year, lastYear = dates.Max().Year;
        return new CreditTimelineView
        {
            Timeline = timeline,
            Summary = summary,
            PeriodLabel = firstYear == lastYear ? $"{firstYear}年" : $"{firstYear}〜{lastYear}年",
            RoleCount = timeline.Rows.Count(r => r.EntityKind == "role"),
            CharacterCount = timeline.Rows.Count(r => r.EntityKind == "character"),
            PersonCount = timeline.Rows.Count(r => r.EntityKind == "person")
        };
    }

    /// <summary>関与を TV 系の話 (series_id, episode_id) と映画系のシリーズに分ける（話の無い TV 系のクレジットは描けないので落とす）。</summary>
    private (HashSet<(int SeriesId, int EpisodeId)> Episodes, HashSet<int> Movies) Split(IEnumerable<Involvement> invs)
    {
        var episodes = new HashSet<(int, int)>();
        var movies = new HashSet<int>();
        foreach (var i in invs)
        {
            if (_ctx.IsMovieKindSeries(i.SeriesId)) movies.Add(i.SeriesId);
            else if (i.EpisodeId is int eid) episodes.Add((i.SeriesId, eid));
        }
        return (episodes, movies);
    }

    /// <summary>行の参加の日付（軸に入れる帯を決めるのに使う）。</summary>
    private IEnumerable<DateOnly> ParticipationDates(RoleTimelineEntity e)
    {
        foreach (var (_, eid) in e.Episodes)
            if (_ctx.EpisodeById.TryGetValue(eid, out var ep)) yield return ep.OnAirDate;
        foreach (var sid in e.MovieSeriesIds)
            if (_ctx.SeriesById.TryGetValue(sid, out var s)) yield return s.StartDate;
        foreach (var p in e.Songs.Concat(e.Bgms).Concat(e.Products)) yield return p.Date;
    }

    /// <summary>
    /// 音楽の役職の行のリンク先。作詞・作曲・編曲は役職詳細、歌唱系は歌唱ページ、それ以外（演奏・録音・盤の製作など）は
    /// 音楽の役職詳細。
    /// </summary>
    private static string MusicRoleUrl(string roleCode)
    {
        string songUrl = PathUtil.SongRoleBadgeUrl(roleCode);
        return songUrl != "" ? songUrl : PathUtil.CreatorsRoleUrl(roleCode);
    }
}

/// <summary>人物詳細・企業/団体詳細・ユニット詳細・キャラクター詳細の年表の表示モデル（テンプレは _credit-timeline.sbn）。</summary>
internal sealed class CreditTimelineView
{
    /// <summary>開いたときの線表（役職・役柄ごとの行）。テンプレは _role-timeline.sbn で描く。</summary>
    public RoleTimelineModel Timeline { get; set; } = new();
    /// <summary>閉じた状態で見せる、すべての参加を 1 行にまとめた帯（描けなければ null）。</summary>
    public RoleTimelineRow? Summary { get; set; }
    /// <summary>参加の期間（「2004〜2026年」。同じ年なら「2004年」）。</summary>
    public string PeriodLabel { get; set; } = "";
    /// <summary>役職の行の数。</summary>
    public int RoleCount { get; set; }
    /// <summary>役柄（演じたキャラ）の行の数（人物詳細だけ。ほかは 0）。</summary>
    public int CharacterCount { get; set; }
    /// <summary>声優（キャラを演じた人物）の行の数（キャラクター詳細だけ。ほかは 0）。</summary>
    public int PersonCount { get; set; }
}
