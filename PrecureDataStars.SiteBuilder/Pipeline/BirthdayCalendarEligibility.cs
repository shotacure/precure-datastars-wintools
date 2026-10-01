namespace PrecureDataStars.SiteBuilder.Pipeline;

/// <summary>
/// 人物の誕生日を記念日カレンダー（ホームの「今月のカレンダー」「今日の記念日」と記念日の日別ページ）に出すかの判定。
/// 誕生日の分かっている人物が増えてもカレンダーが埋まり過ぎないよう、シリーズに深く関わった人物に絞る。
/// <list type="bullet">
///   <item><description>声の出演がある人物：出演回数が <see cref="VoiceCastMinAppearances"/> 回以上。出演回数は TV の 1 話も
///     映画の 1 本も 1 回と数える（声の出演一覧の 📺 話数と 🎥 本数の合計と同じ数え方）</description></item>
///   <item><description>声の出演が無い人物：OP のクレジットか、各話のチーフ（<see cref="EpisodeChiefRoles"/>）としてクレジット
///     されたことがある人物。TV・映画のどちらのクレジットでもよい</description></item>
///   <item><description>TV シリーズの主題歌（OP・ED）を歌った人物。ユニット名義で歌った曲はメンバーを、キャラクター名義で
///     歌った曲はその声優を含める。コーラスだけの参加と挿入歌は含めない</description></item>
///   <item><description>役職を問わず、TV・映画の本編のクレジットに通算 <see cref="MinTotalCreditAppearances"/> 回以上載った人物。
///     回数は人物詳細の「本編クレジット」の 📺 話数と 🎥 本数の合計と同じ数え方</description></item>
/// </list>
/// 声の出演がある人物でも、OP・各話のチーフ・TV の主題歌の歌唱のどれかの経験があるか、通算のクレジット回数が足りていれば出す。
/// 人物詳細の誕生日はこの判定に関係なく出す。クレジットの入力が進めば、出演回数や経験が増えて対象も増える。
/// </summary>
public sealed class BirthdayCalendarEligibility
{
    /// <summary>声の出演がある人物の誕生日をカレンダーに出す、出演回数の下限。</summary>
    public const int VoiceCastMinAppearances = 5;

    /// <summary>役職を問わず誕生日をカレンダーに出す、TV・映画の本編のクレジットの通算回数の下限。</summary>
    public const int MinTotalCreditAppearances = 100;

    /// <summary>カレンダーに出す人物の person_id。null は全員を出す（未構築時）。</summary>
    private readonly HashSet<int>? _eligiblePersonIds;

    private BirthdayCalendarEligibility(HashSet<int>? eligiblePersonIds)
    {
        _eligiblePersonIds = eligiblePersonIds;
    }

    /// <summary>未構築時（Catalog 側プレビュー等）用。全員を出す。</summary>
    public static BirthdayCalendarEligibility All { get; } = new(null);

    /// <summary>この人物の誕生日をカレンダーに出すか。</summary>
    public bool IsEligible(int personId) => _eligiblePersonIds is null || _eligiblePersonIds.Contains(personId);

    /// <summary>
    /// クレジット関与の索引から、カレンダーに誕生日を出す人物を決める。
    /// 出演回数は声の出演一覧（CreatorsGenerator）と同じく、キャラクター名義に紐付く声の出演だけを、
    /// TV は (シリーズ, 話数) の重複を除いた数、映画は映画系シリーズの重複を除いた本数で数える。
    /// </summary>
    public static BirthdayCalendarEligibility Build(BuildContext ctx, CreditInvolvementIndex index)
    {
        var eligible = new HashSet<int>();
        foreach (var (personId, aliasIds) in ctx.AliasIdsByPerson)
        {
            // 声の出演の回数（TV の話数と映画の本数）。
            var episodeKeys = new HashSet<(int SeriesId, int EpNo)>();
            var movieSeries = new HashSet<int>();
            // 本編のクレジットの通算回数（役職を問わない TV の話数と映画の本数）。
            var creditEpisodeKeys = new HashSet<(int SeriesId, int EpNo)>();
            var creditMovieSeries = new HashSet<int>();
            // OP・各話のチーフ・TV の主題歌の歌唱のどれかを務めたことがあるか（声の出演の回数に関係なく出す）。
            bool hasKeyCredit = false;

            foreach (var aid in aliasIds)
            {
                if (!index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    // 人物詳細の「本編クレジット」と同じく、映画系シリーズは 1 本、TV 系は重複を除いた話数で数える。
                    if (inv.IsMainCredit && ctx.SeriesById.ContainsKey(inv.SeriesId))
                    {
                        if (ctx.IsMovieKindSeries(inv.SeriesId))
                            creditMovieSeries.Add(inv.SeriesId);
                        else if (inv.EpisodeId is int ceid && ctx.LookupEpisode(inv.SeriesId, ceid) is { } cep)
                            creditEpisodeKeys.Add((inv.SeriesId, cep.SeriesEpNo));
                    }

                    if (inv.IsVoiceCast)
                    {
                        if (inv.CharacterAliasId is not int caId || !ctx.CharacterAliasById.ContainsKey(caId)) continue;
                        if (!ctx.SeriesById.ContainsKey(inv.SeriesId)) continue;
                        if (inv.EpisodeId is int eid && ctx.LookupEpisode(inv.SeriesId, eid) is { } ep)
                            episodeKeys.Add((inv.SeriesId, ep.SeriesEpNo));
                        if (ctx.IsMovieKindSeries(inv.SeriesId))
                            movieSeries.Add(inv.SeriesId);
                        continue;
                    }

                    // TV シリーズの主題歌（OP・ED）の歌唱。索引の歌唱者の関与はユニット名義をメンバーへ、
                    // キャラクター名義をその声優へ展開済み。コーラス（CHORUS）と挿入歌（INSERT）は含めない。
                    if (string.Equals(inv.EntryKind, "RECORDING_SINGER", StringComparison.Ordinal))
                    {
                        if (!hasKeyCredit
                            && string.Equals(inv.RoleCode, "VOCALS", StringComparison.Ordinal)
                            && inv.ThemeKind is "OP" or "ED"
                            && ctx.SeriesById.TryGetValue(inv.SeriesId, out var series)
                            && string.Equals(series.KindCode, "TV", StringComparison.Ordinal))
                        {
                            hasKeyCredit = true;
                        }
                        continue;
                    }

                    // スタッフとしての経験は、本編のクレジットの人物エントリだけで見る
                    // （主題歌・挿入歌の作家や劇伴の作曲・編曲、企業・ロゴは含めない）。
                    if (hasKeyCredit || inv.Kind != InvolvementKind.Person || !inv.IsMainCredit) continue;
                    if (string.Equals(inv.CreditKind, "OP", StringComparison.Ordinal))
                    {
                        hasKeyCredit = true;
                        continue;
                    }
                    string? roleNameJa = ctx.RoleByCode.TryGetValue(inv.RoleCode, out var role) ? role.NameJa : null;
                    if (EpisodeChiefRoles.Classify(inv.RoleCode, roleNameJa) is not null)
                        hasKeyCredit = true;
                }
            }

            if (hasKeyCredit
                || episodeKeys.Count + movieSeries.Count >= VoiceCastMinAppearances
                || creditEpisodeKeys.Count + creditMovieSeries.Count >= MinTotalCreditAppearances)
                eligible.Add(personId);
        }
        return new BirthdayCalendarEligibility(eligible);
    }
}
