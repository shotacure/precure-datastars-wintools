using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 歴代記録（<c>/stats/records/</c>）の生成。
/// <para>
/// 歴代記録は最長・最短・最多の上位を集めた記録集。本編の尺（アバンタイトル・A パート・B パートの長短、中 CM の早遅）は
/// パート尺統計と同じランキング SQL から上位だけを取り、スタッフ（脚本・絵コンテ・演出・作画監督の担当話数、
/// 声の出演の話数・役の数）とキャラクター（TV シリーズの登場話数・登場作品数）は
/// クレジットの関与索引から数える。順位は同点を同じ順位とし、次の順位は同点の数だけ繰り下げる
/// （3 位までを出し、同率は全件載せる）。
/// </para>
/// <para>
/// 1 話のクレジット（声の出演人数・参加スタッフ数の多い回と少ない回）も同じ関与索引から数え、
/// 記録集には 3 位までを、<c>/stats/records/{cast|staff}/{most|least}/</c> には上位 100 件（100 位が同点なら全件）を出す。
/// 数えるのは本線の TV シリーズの OP・ED のクレジットで、本放送だけの行（Blu-ray に無い行）と
/// 主題歌・挿入歌のクレジット（作家・歌唱・レーベルなど）は入れない。
/// </para>
/// </summary>
public sealed class RecordsGenerator
{
    /// <summary>歴代記録の URL。</summary>
    public const string IndexUrl = "/stats/records/";

    /// <summary>記録集に載せる順位の上限（同率は全件）。</summary>
    private const int TopRank = 3;

    /// <summary>ランキング SQL から取る件数。同率が続いても 3 位までが収まる程度に取る。</summary>
    private const int FetchLimit = 10;

    /// <summary>番組開始基準時刻（08:30:00）。中 CM 入り時刻の絶対時刻表示で使う（パート尺統計と同じ）。</summary>
    private static readonly TimeSpan ProgramStart = new(8, 30, 0);

    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly EpisodePartStatsRepository _partStatsRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CreditInvolvementIndex _involvements;

    public RecordsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex involvements)
    {
        _ctx = ctx;
        _page = page;
        _partStatsRepo = new EpisodePartStatsRepository(factory);
        _charactersRepo = new CharactersRepository(factory);
        _involvements = involvements;
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating records");

        var episodeIdsWithParts = (await _partStatsRepo.GetEpisodeIdsWithPartsAsync(ct).ConfigureAwait(false)).ToHashSet();
        string partsCoverage = StatsCoverageLabel.Build(StatsCoverageLabel.FindLatestTvEpisodeWithParts(_ctx, episodeIdsWithParts));
        string creditCoverage = _ctx.CreditCoverageLabel;

        var episodeCounts = CountEpisodeCredits();

        var groups = new List<RecordGroup>
        {
            await BuildPartLengthGroupAsync(partsCoverage, ct).ConfigureAwait(false),
            BuildEpisodeCreditGroup(episodeCounts, creditCoverage),
            BuildStaffGroup(creditCoverage),
            await BuildCharacterGroupAsync(creditCoverage, ct).ConfigureAwait(false)
        };

        GenerateIndex(groups, partsCoverage);

        // 1 話のクレジットの全体の順位（上位 100 件）。声の出演人数と参加スタッフ数を、それぞれ多い順と少ない順で。
        foreach (var metric in EpisodeCreditMetrics)
        {
            GenerateEpisodeCreditRanking(episodeCounts, metric, most: true, creditCoverage);
            GenerateEpisodeCreditRanking(episodeCounts, metric, most: false, creditCoverage);
        }

        _ctx.Logger.Success(IndexUrl);
    }

    // ── 歴代記録（索引） ──

    private void GenerateIndex(IReadOnlyList<RecordGroup> groups, string partsCoverage)
    {
        var content = new RecordsIndexModel
        {
            Groups = groups
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代記録",
            // カードは記録を並べず、どんなページかをリード文で示す（記録の項目数はサイト側の都合の数なので出さない）。
            OgCard = new OgCardSpec(Kicker: "統計", Title: "歴代記録")
            {
                MetaLeft = OgCoverageLabel.Compact(partsCoverage),
                // ページの冒頭文（stats-records-index.sbn の .lead）と同じ文。
                Subtitle = "最長・最短・最多・最少の上位 3 位"
            },
            MetaDescription = "最長・最短・最多・最少の上位 3 位",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "歴代記録", Url = "" }
            }
        };
        _page.RenderAndWrite(IndexUrl, "stats", "stats-records-index.sbn", content, layout);
    }

    // ── 本編の尺 ──

    private async Task<RecordGroup> BuildPartLengthGroupAsync(string coverage, CancellationToken ct)
    {
        // 同じパートの「長い回」と「短い回」（中 CM は「早い回」と「遅い回」）は、左右に並べた 1 つの組にする。
        var pairs = new List<RecordPair>
        {
            new()
            {
                Title = "アバンタイトルの長さ",
                High = await PartLengthItemAsync("AVANT", "長い回", "avant", ascending: false, ct).ConfigureAwait(false),
                Low = await PartLengthItemAsync("AVANT", "短い回", "avant", ascending: true, ct).ConfigureAwait(false)
            },
            new()
            {
                Title = "Aパートの長さ",
                High = await PartLengthItemAsync("PART_A", "長い回", "part-a", ascending: false, ct).ConfigureAwait(false),
                Low = await PartLengthItemAsync("PART_A", "短い回", "part-a", ascending: true, ct).ConfigureAwait(false)
            },
            new()
            {
                Title = "Bパートの長さ",
                High = await PartLengthItemAsync("PART_B", "長い回", "part-b", ascending: false, ct).ConfigureAwait(false),
                Low = await PartLengthItemAsync("PART_B", "短い回", "part-b", ascending: true, ct).ConfigureAwait(false)
            },
            new()
            {
                Title = "中 CM に入る時刻",
                High = await MidCmItemAsync(earliest: true, ct).ConfigureAwait(false),
                Low = await MidCmItemAsync(earliest: false, ct).ConfigureAwait(false)
            }
        };
        return new RecordGroup
        {
            Id = "part-length",
            Title = "本編の尺",
            Lead = "本放送で測ったパートの長さと、中 CM に入る時刻です。順位の全体は歴代エピソード尺統計にあります。",
            CoverageLabel = coverage,
            Pairs = pairs
        };
    }

    private async Task<RecordItem> PartLengthItemAsync(string partType, string title, string slug, bool ascending, CancellationToken ct)
    {
        var rows = await _partStatsRepo.GetPartLengthRankingAsync(partType, ascending, FetchLimit, ct).ConfigureAwait(false);
        var entries = rows
            .Where(r => r.Rank <= TopRank)
            .Select(r => EpisodeEntry(r.Rank, HtmlUtil.FormatSeconds((int)Math.Round(r.LengthSeconds)), r.SeriesSlug, r.SeriesEpNo, r.SeriesTitle, r.TitleText))
            .ToList();
        string order = ascending ? "shortest" : "longest";
        return new RecordItem
        {
            Id = $"{slug}-{order}",
            Title = title,
            Entries = entries,
            MoreUrl = $"/stats/episodes/{slug}/{order}/",
            MoreLabel = "TOP 100 を見る"
        };
    }

    private async Task<RecordItem> MidCmItemAsync(bool earliest, CancellationToken ct)
    {
        var rows = await _partStatsRepo.GetCmTimeRankingAsync(earliest, FetchLimit, ct).ConfigureAwait(false);
        var entries = rows
            .Where(r => r.Rank <= TopRank)
            .Select(r => EpisodeEntry(r.Rank, FormatAbsoluteTime(r.Cm2OffsetSeconds), r.SeriesSlug, r.SeriesEpNo, r.SeriesTitle, r.TitleText))
            .ToList();
        string slug = earliest ? "earliest" : "latest";
        return new RecordItem
        {
            Id = $"midcm-{slug}",
            Title = earliest ? "早い回" : "遅い回",
            Entries = entries,
            MoreUrl = $"/stats/episodes/midcm/{slug}/",
            MoreLabel = "TOP 100 を見る"
        };
    }

    /// <summary>エピソード 1 件の記録行。名前は「第N話 サブタイトル」（解禁ガード済み）、添え書きはシリーズ名と年度。</summary>
    private RecordEntry EpisodeEntry(int rank, string value, string seriesSlug, int seriesEpNo, string seriesTitle, string titleFallback)
    {
        var row = StatsEpisodeRows.Build(_ctx, new[]
        {
            new StatsEpisodeInput(seriesSlug, seriesEpNo, seriesTitle, _ctx.StartYearLabelBySlug(seriesSlug), true, rank, value, titleFallback)
        })[0];
        return new RecordEntry
        {
            RankLabel = rank.ToString(),
            ValueLabel = value,
            ValueNumber = value,
            NameHtml = $"<a href=\"{row.EpisodeUrl}\">第{row.SeriesEpNo}話 {row.TitleHtml}</a>",
            SubHtml = SeriesRefHtml(row.SeriesUrl, row.SeriesTitle, row.SeriesStartYearLabel)
        };
    }

    /// <summary>中 CM 入りの絶対時刻（番組開始 08:30:00 からの累積秒）。「8:45:12」のように先頭の時は零埋めしない。</summary>
    private static string FormatAbsoluteTime(double offsetSeconds)
    {
        var t = ProgramStart + TimeSpan.FromSeconds(Math.Round(offsetSeconds));
        return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    // ── 1 話のクレジット ──

    /// <summary>全体の順位のページに載せる件数（100 位が同点なら全件）。</summary>
    private const int RankingLimit = 100;

    /// <summary>1 話のクレジットの数。<see cref="VoicePeople"/> は声の出演の声優の人数、<see cref="Staff"/> はスタッフの人と会社を合わせた数。</summary>
    private sealed record EpisodeCreditCount(Episode Episode, int VoicePeople, int Staff);

    /// <summary>1 話のクレジットの記録の 1 指標（声の出演人数 / 参加スタッフ数）。</summary>
    private sealed record EpisodeCreditMetric(string Slug, string Label, string Unit, Func<EpisodeCreditCount, int> Value, string Lead);

    private static readonly EpisodeCreditMetric[] EpisodeCreditMetrics =
    {
        new("cast", "声の出演人数", "人", c => c.VoicePeople,
            "声の出演に名前の出た声優の人数です。1 人で何役演じても 1 人と数えます。"),
        new("staff", "参加スタッフ数", "", c => c.Staff,
            "OP・ED に名前の出た人と会社を合わせた数です。声の出演は入れず、同じ人・会社が何度載っても 1 と数えます。")
    };

    /// <summary>主題歌・挿入歌のクレジットの役職か（曲の行・レーベル・主題歌協力・主題歌プロモーション）。1 話のクレジットの数に入れない。</summary>
    private bool IsThemeSongRole(string roleCode)
        => roleCode is "LABEL" or "THEME_SONG_COOPERATION" or "THEME_SONG_PROMOTION"
           || (_ctx.RoleByCode.TryGetValue(roleCode, out var role)
               && string.Equals(role.RoleFormatKind, "THEME_SONG", StringComparison.Ordinal));

    /// <summary>
    /// 本線の TV の話ごとに、声の出演の声優の人数と、スタッフ（人と会社）の数を数える。
    /// 人は別名義をまとめて人物で、会社は屋号・ロゴをまとめて会社で 1 と数える（人物につながらないユニット名義は名義で 1）。
    /// 本放送だけの行と、主題歌・挿入歌のクレジット（曲の作家・歌唱は関与の索引で本編のクレジットでないもの、
    /// カードの上ではレーベルなどの役職）は数えない。声の出演に付いた所属の会社もスタッフに入れない。
    /// </summary>
    private List<EpisodeCreditCount> CountEpisodeCredits()
    {
        var voice = new Dictionary<int, HashSet<string>>();
        var staff = new Dictionary<int, HashSet<string>>();

        // 数える関与か（本線の TV の話の、Blu-ray にもある行）。
        bool Eligible(Involvement inv, out int episodeId)
        {
            episodeId = inv.EpisodeId ?? 0;
            return inv.EpisodeId is not null && !inv.IsBroadcastOnly && _ctx.IsMainlineTvSeries(inv.SeriesId);
        }
        static void Add(Dictionary<int, HashSet<string>> map, int episodeId, string key)
        {
            if (!map.TryGetValue(episodeId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[episodeId] = set;
            }
            set.Add(key);
        }

        foreach (var (aliasId, invs) in _involvements.ByPersonAlias)
        {
            string key = _ctx.PersonIdByAlias.TryGetValue(aliasId, out var personId) ? $"p{personId}" : $"a{aliasId}";
            foreach (var inv in invs)
            {
                if (!Eligible(inv, out int episodeId)) continue;
                if (inv.IsVoiceCast) Add(voice, episodeId, key);
                else if (inv.Kind == InvolvementKind.Person && inv.IsMainCredit && !IsThemeSongRole(inv.RoleCode)) Add(staff, episodeId, key);
            }
        }

        foreach (var (companyAliasId, invs) in _involvements.ByCompanyAlias)
        {
            string key = _ctx.CompanyAliasById.TryGetValue(companyAliasId, out var ca) ? $"c{ca.CompanyId}" : $"ca{companyAliasId}";
            foreach (var inv in invs)
            {
                if (!Eligible(inv, out int episodeId)) continue;
                // 屋号のエントリ・ブロックの先頭の会社・人に付いた所属の会社。所属の人の側の関与（Member）は人で数え済み。
                if (inv.Kind is not (InvolvementKind.Company or InvolvementKind.LeadingCompany)) continue;
                if (inv.RoleCode == "VOICE CAST" || IsThemeSongRole(inv.RoleCode)) continue;
                Add(staff, episodeId, key);
            }
        }

        foreach (var (logoId, invs) in _involvements.ByLogo)
        {
            string key = _ctx.LogoById.TryGetValue(logoId, out var logo)
                         && _ctx.CompanyAliasById.TryGetValue(logo.CompanyAliasId, out var logoAlias)
                ? $"c{logoAlias.CompanyId}"
                : $"l{logoId}";
            foreach (var inv in invs)
            {
                if (!Eligible(inv, out int episodeId)) continue;
                if (IsThemeSongRole(inv.RoleCode)) continue;
                Add(staff, episodeId, key);
            }
        }

        var rows = new List<EpisodeCreditCount>();
        foreach (int episodeId in voice.Keys.Union(staff.Keys))
        {
            // 公開前などで索引から外れた話は数えない。
            if (!_ctx.EpisodeById.TryGetValue(episodeId, out var episode)) continue;
            rows.Add(new EpisodeCreditCount(episode,
                voice.TryGetValue(episodeId, out var v) ? v.Count : 0,
                staff.TryGetValue(episodeId, out var s) ? s.Count : 0));
        }
        return rows;
    }

    /// <summary>
    /// 指標の多い順（<paramref name="most"/>）か少ない順に並べ、同点を同じ順位にした順位を振る。
    /// 同じ数の話は放送順に並べる。数が 0 の話（声の出演の無い回など）は順位に入れない。
    /// </summary>
    private static List<(EpisodeCreditCount Row, int Rank, int Value)> RankEpisodeCredits(
        IEnumerable<EpisodeCreditCount> rows, EpisodeCreditMetric metric, bool most, int limit)
    {
        var withValue = rows.Select(r => (Row: r, Value: metric.Value(r))).Where(x => x.Value > 0);
        var ordered = (most ? withValue.OrderByDescending(x => x.Value) : withValue.OrderBy(x => x.Value))
            .ThenBy(x => x.Row.Episode.OnAirAt)
            .ThenBy(x => x.Row.Episode.EpisodeId)
            .ToList();
        var result = new List<(EpisodeCreditCount, int, int)>();
        int rank = 0;
        int prev = int.MinValue;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Value != prev)
            {
                rank = i + 1;
                prev = ordered[i].Value;
            }
            if (rank > limit) break;
            result.Add((ordered[i].Row, rank, ordered[i].Value));
        }
        return result;
    }

    /// <summary>1 話のクレジットの全体の順位のページの URL。</summary>
    private static string EpisodeCreditRankingUrl(EpisodeCreditMetric metric, bool most)
        => $"{IndexUrl}{metric.Slug}/{(most ? "most" : "least")}/";

    private RecordGroup BuildEpisodeCreditGroup(IReadOnlyList<EpisodeCreditCount> counts, string coverage)
    {
        // 同じ指標の「多い回」と「少ない回」は、本編の尺と同じく左右に並べた 1 つの組にする。
        var pairs = EpisodeCreditMetrics
            .Select(m => new RecordPair
            {
                Title = m.Label,
                High = EpisodeCreditItem(counts, m, most: true),
                Low = EpisodeCreditItem(counts, m, most: false)
            })
            .ToList();
        return new RecordGroup
        {
            Id = "episode-credits",
            Title = "1 話のクレジット",
            Lead = "TV シリーズの OP・ED のクレジットから数えた、1 話あたりの数です。Blu-ray のクレジットで数え、主題歌・挿入歌のクレジットは入れません。",
            CoverageLabel = coverage,
            Pairs = pairs
        };
    }

    private RecordItem EpisodeCreditItem(IReadOnlyList<EpisodeCreditCount> counts, EpisodeCreditMetric metric, bool most)
    {
        var entries = RankEpisodeCredits(counts, metric, most, TopRank)
            .Select(x =>
            {
                var ep = x.Row.Episode;
                var series = _ctx.SeriesById[ep.SeriesId];
                var entry = EpisodeEntry(x.Rank, x.Value.ToString(), series.Slug, ep.SeriesEpNo, series.Title, ep.TitleText);
                entry.ValueLabel = $"{x.Value}{metric.Unit}";
                entry.ValueUnit = metric.Unit;
                return entry;
            })
            .ToList();
        return new RecordItem
        {
            Id = $"{metric.Slug}-{(most ? "most" : "least")}",
            Title = most ? "多い回" : "少ない回",
            Entries = entries,
            MoreUrl = EpisodeCreditRankingUrl(metric, most),
            MoreLabel = "TOP 100 を見る"
        };
    }

    /// <summary>1 話のクレジットの全体の順位（上位 100 件、100 位が同点なら全件）を 1 ページ書き出す。</summary>
    private void GenerateEpisodeCreditRanking(IReadOnlyList<EpisodeCreditCount> counts, EpisodeCreditMetric metric, bool most, string coverage)
    {
        var ranked = RankEpisodeCredits(counts, metric, most, RankingLimit);
        var rows = StatsEpisodeRows.Build(_ctx, ranked.Select(x =>
        {
            var ep = x.Row.Episode;
            var series = _ctx.SeriesById[ep.SeriesId];
            return new StatsEpisodeInput(series.Slug, ep.SeriesEpNo, series.Title, _ctx.StartYearLabelBySlug(series.Slug),
                true, x.Rank, $"{x.Value}{metric.Unit}", ep.TitleText);
        }));
        string orderLabel = most ? "多い順" : "少ない順";
        string title = $"{metric.Label} {orderLabel} TOP 100";
        var content = new
        {
            Title = title,
            Lead = $"TV シリーズで{metric.Label}が{(most ? "多い" : "少ない")}回の上位 100 件",
            Note = metric.Lead + "Blu-ray のクレジットで数え、主題歌・挿入歌のクレジットは入れません。",
            CoverageLabel = coverage,
            Rows = rows
        };
        var layout = StatsPageLayout.Make(
            title, "。プリキュアの TV シリーズの OP・ED のクレジットから数えた記録です。",
            "歴代記録", IndexUrl, $"{metric.Label} {orderLabel}");
        _page.RenderAndWrite(EpisodeCreditRankingUrl(metric, most), "stats", "stats-records-episode-credits.sbn", content, layout);
    }

    // ── スタッフ ──

    /// <summary>人物 1 人分の数え上げ。話は (episode_id) で重複を除く。</summary>
    private sealed class PersonStats
    {
        public Dictionary<int, HashSet<int>> ChiefEpisodes { get; } = new();
        public HashSet<int> VoiceEpisodes { get; } = new();
        public HashSet<int> Characters { get; } = new();
        /// <summary>役職（<see cref="ChiefEpisodes"/> のキー）ごとの初参加の位置（放送開始日 → series_id → 話数 → クレジットの位置）。同じ回数の人の並びに使う。</summary>
        public Dictionary<int, (long Start, int SeriesId, int EpNo, long Pos)> FirstChief { get; } = new();
        /// <summary>声の出演の初出演の位置（並べ方は <see cref="FirstChief"/> と同じ）。</summary>
        public (long Start, int SeriesId, int EpNo, long Pos) FirstVoice { get; set; } = (long.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue);
        public bool IsEmpty => ChiefEpisodes.Count == 0 && VoiceEpisodes.Count == 0;
    }

    /// <summary>関与のクレジットの位置（放送開始日 → series_id → 話数 → クレジットの位置）。</summary>
    private (long Start, int SeriesId, int EpNo, long Pos) CreditKey(Involvement inv)
    {
        var (start, epNo, pos) = LatestAliasResolver.CreditOrderKey(_ctx, inv);
        return (start, inv.SeriesId, epNo, pos);
    }

    private RecordGroup BuildStaffGroup(string coverage)
    {
        // 人物ごとに、TV 系の話だけを数える（映画はシリーズ単位のクレジットなので「話数」に入れない）。
        // 名義をまたいで同じ人物にまとめ、同じ話に同じ役職で 2 度載っても 1 話に数える。
        var stats = new Dictionary<int, PersonStats>();
        foreach (var (personId, aliasIds) in _ctx.AliasIdsByPerson)
        {
            var st = new PersonStats();
            foreach (var aliasId in aliasIds)
            {
                if (!_involvements.ByPersonAlias.TryGetValue(aliasId, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (inv.EpisodeId is not int episodeId) continue;
                    // 記録は本線の TV の話だけで数える（映画系・スピンオフは入れない）
                    if (!_ctx.IsMainlineTvSeries(inv.SeriesId)) continue;
                    if (inv.IsVoiceCast)
                    {
                        st.VoiceEpisodes.Add(episodeId);
                        var voiceKey = CreditKey(inv);
                        if (voiceKey.CompareTo(st.FirstVoice) < 0) st.FirstVoice = voiceKey;
                        if (inv.CharacterAliasId is int characterAliasId
                            && _ctx.CharacterAliasById.TryGetValue(characterAliasId, out var characterAlias))
                        {
                            st.Characters.Add(characterAlias.CharacterId);
                        }
                        continue;
                    }
                    if (inv.Kind != InvolvementKind.Person || !inv.IsMainCredit) continue;
                    string? nameJa = _ctx.RoleByCode.TryGetValue(inv.RoleCode, out var role) ? role.NameJa : null;
                    if (EpisodeChiefRoles.Classify(inv.RoleCode, nameJa) is int chief)
                    {
                        if (!st.ChiefEpisodes.TryGetValue(chief, out var set))
                        {
                            set = new HashSet<int>();
                            st.ChiefEpisodes[chief] = set;
                        }
                        set.Add(episodeId);
                        var chiefKey = CreditKey(inv);
                        if (!st.FirstChief.TryGetValue(chief, out var curChief) || chiefKey.CompareTo(curChief) < 0)
                            st.FirstChief[chief] = chiefKey;
                    }
                }
            }
            if (!st.IsEmpty) stats[personId] = st;
        }

        var items = new List<RecordItem>();
        foreach (var (chief, roleCode) in new[]
        {
            (EpisodeChiefRoles.Screenplay, "SCREENPLAY"),
            (EpisodeChiefRoles.Storyboard, "STORYBOARD"),
            (EpisodeChiefRoles.EpisodeDirector, "EPISODE_DIRECTOR"),
            (EpisodeChiefRoles.AnimationDirector, "ANIMATION_DIRECTOR")
        })
        {
            string label = EpisodeChiefRoles.Label(chief);
            items.Add(TopPersons(
                $"chief-{roleCode.ToLowerInvariant().Replace('_', '-')}",
                $"{label}を多く担当した人",
                "話",
                stats.Select(kv => (kv.Key, kv.Value.ChiefEpisodes.TryGetValue(chief, out var s) ? s.Count : 0,
                    kv.Value.FirstChief.TryGetValue(chief, out var f) ? f : (long.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue))),
                // 役職詳細は初参加順のタブで開くので、担当回数順のタブを開いた状態（view-state.js の #tab=）へリンクする。
                PathUtil.CreatorsRoleUrl(roleCode) + "#tab=count",
                $"{label}の担当回数順を見る"));
        }
        items.Add(TopPersons("voice-episodes", "声の出演が多い声優さん", "話",
            stats.Select(kv => (kv.Key, kv.Value.VoiceEpisodes.Count, kv.Value.FirstVoice)),
            // 声の出演一覧の出演回数順のタブを開いた状態へリンクする。
            PathUtil.CreatorsVoiceCastUrl() + "#tab=count", "声の出演一覧を見る"));
        items.Add(TopPersons("voice-characters", "クレジットされた役柄が多い声優さん", "役",
            stats.Select(kv => (kv.Key, kv.Value.Characters.Count, kv.Value.FirstVoice)),
            // 声の出演一覧のクレジット役柄数順のタブを、TV シリーズで演じたキャラの数（📺）の多い順にした状態へリンクする。
            PathUtil.CreatorsVoiceCastUrl() + "#tab=charcount&csort=tv-desc", "声の出演一覧を見る"));

        return new RecordGroup
        {
            Id = "staff",
            Title = "スタッフ・声優",
            Lead = "TV シリーズのクレジットから数えた話数です。同じ人の別名義はまとめ、1 話に同じ役職で 2 度載っても 1 話に数えます。",
            CoverageLabel = coverage,
            Items = items
        };
    }

    /// <summary>人物の数え上げから上位を取り出す。同数は同じ順位で、初参加の順（放送開始日 → series_id → 話数 → クレジットの位置）→ person_id に並べる。</summary>
    private RecordItem TopPersons(string id, string title, string unit,
        IEnumerable<(int PersonId, int Count, (long Start, int SeriesId, int EpNo, long Pos) First)> counts, string moreUrl, string moreLabel)
    {
        var ordered = counts
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.First)
            .ThenBy(c => c.PersonId)
            .ToList();
        var entries = new List<RecordEntry>();
        int rank = 0;
        int prevCount = -1;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Count != prevCount)
            {
                rank = i + 1;
                prevCount = ordered[i].Count;
            }
            if (rank > TopRank) break;
            entries.Add(new RecordEntry
            {
                RankLabel = rank.ToString(),
                ValueLabel = $"{ordered[i].Count}{unit}",
                ValueNumber = ordered[i].Count.ToString(),
                ValueUnit = unit,
                BarPercent = BarPercentOf(ordered[i].Count, ordered[0].Count),
                NameHtml = PersonLinkHtml(ordered[i].PersonId)
            });
        }
        return new RecordItem { Id = id, Title = title, Entries = entries, MoreUrl = moreUrl, MoreLabel = moreLabel };
    }

    private string PersonLinkHtml(int personId)
    {
        string name = _ctx.EntityUrls.PersonDisplayLabel(personId) ?? "";
        if (string.IsNullOrEmpty(name)
            && _ctx.AliasIdsByPerson.TryGetValue(personId, out var aliasIds)
            && aliasIds.Count > 0
            && _ctx.PersonAliasById.TryGetValue(aliasIds[0], out var alias))
        {
            name = alias.DisplayTextOverride ?? alias.Name;
        }
        return $"<a href=\"{PathUtil.PersonUrl(personId)}\">{HtmlUtil.Escape(name)}</a>";
    }

    // ── キャラクター ──

    private async Task<RecordGroup> BuildCharacterGroupAsync(string coverage, CancellationToken ct)
    {
        var characters = await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var aliasesByCharacter = _ctx.CharacterAliasById.Values
            .GroupBy(a => a.CharacterId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AliasId).ToList());

        // First は初登場のクレジットの位置（放送開始日 → series_id → 話数 → クレジットの位置）。同じ数のキャラの並びに使う。
        var rows = new List<(Character Character, int Episodes, int Movies, int Works, int OtonaWorks, int SpinOffWorks, (long Start, int SeriesId, int EpNo, long Pos) First)>();
        // キャラクター一覧の登場回数順のリスト（個別ページを持つキャラ）に 🌙 の札が出るか。出るときは件数バッジでの
        // 並べ替えの合計に 🌙 も入る（count-sort.js）ので、「もっと見る」のリンクに書く並べ替えの値が変わる。
        bool countListHasOtona = false;
        foreach (var c in characters)
        {
            if (!aliasesByCharacter.TryGetValue(c.CharacterId, out var aliasIds)) continue;
            // 話数・本数は本線の TV と映画だけで数える（大人向けスピンオフの話数は使わない）。
            var (episodes, movies, otonaEpisodes) = CharacterAppearanceCounter.Count(_ctx, _involvements, aliasIds);
            if (otonaEpisodes > 0 && !_ctx.EntityUrls.IsGuestCharacter(c.CharacterId)) countListHasOtona = true;

            // 登場作品数：声の出演のクレジットがあるシリーズを 1 作品と数える。スピンオフ（大人向けを含む）も 1 作品に数え、
            // 併映の短編は親映画に含める。
            var works = new HashSet<int>();
            // 添え書きでは、オトナシリーズとそれ以外のスピンオフを分けて数える（サイトでの呼び名が違う）。
            var otonaWorks = new HashSet<int>();
            var spinOffWorks = new HashSet<int>();
            var first = (Start: long.MaxValue, SeriesId: int.MaxValue, EpNo: int.MaxValue, Pos: long.MaxValue);
            foreach (var aliasId in aliasIds)
            {
                if (!_involvements.VoiceCastByCharacterAlias.TryGetValue(aliasId, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (!_ctx.SeriesById.TryGetValue(inv.SeriesId, out var series)) continue;
                    var key = CreditKey(inv);
                    if (key.CompareTo(first) < 0) first = key;
                    if (SeriesClassifier.IsOtona(series)) otonaWorks.Add(series.SeriesId);
                    else if (SeriesClassifier.IsSpinOff(series)) spinOffWorks.Add(series.SeriesId);
                    works.Add(SeriesClassifier.IsMovieShortChild(series) && series.ParentSeriesId is int parentId ? parentId : series.SeriesId);
                }
            }
            if (works.Count == 0) continue;
            rows.Add((c, episodes, movies, works.Count, otonaWorks.Count, spinOffWorks.Count, first));
        }

        // 「もっと見る」はキャラクター一覧の登場回数順のタブへ。TV シリーズの登場話数は 📺 の多い順、登場作品数は
        // 件数バッジの合計（📺🎥。🌙 の札があれば 🌙 も含む）の多い順に並べた状態（view-state.js の #tab= / csort=）を開く。
        string countTabUrl = "/characters/#tab=count";
        string tvSortUrl = countTabUrl + "&csort=tv-desc";
        string totalSortUrl = countTabUrl + (countListHasOtona ? "&csort=tv_movie_otona-desc" : "&csort=tv_movie-desc");
        var items = new List<RecordItem>
        {
            TopCharacters("character-episodes", "TVシリーズ登場話数が多いキャラクター", rows
                .OrderByDescending(r => r.Episodes).ThenByDescending(r => r.Movies).ThenBy(r => r.First).ThenBy(r => r.Character.CharacterId)
                .Select(r => (r.Character, r.Episodes, "話", "")),
                tvSortUrl),
            TopCharacters("character-works", "登場作品数が多いキャラクター", rows
                .OrderByDescending(r => r.Works).ThenByDescending(r => r.Episodes).ThenBy(r => r.First).ThenBy(r => r.Character.CharacterId)
                .Select(r => (r.Character, r.Works, "作品",
                    $"TV {r.Episodes}話・映画 {r.Movies}本" + (r.OtonaWorks > 0 ? $"・オトナ {r.OtonaWorks}作品" : "")
                        + (r.SpinOffWorks > 0 ? $"・スピンオフ {r.SpinOffWorks}作品" : ""))),
                totalSortUrl)
        };

        return new RecordGroup
        {
            Id = "characters",
            Title = "キャラクター",
            Lead = "声の出演のクレジットから数えた登場回数です。主題歌・挿入歌の歌唱は数えません。",
            CoverageLabel = coverage,
            Items = items
        };
    }

    private RecordItem TopCharacters(string id, string title, IEnumerable<(Character Character, int Count, string Unit, string Sub)> ordered, string moreUrl)
    {
        var entries = new List<RecordEntry>();
        int rank = 0;
        int prevCount = -1;
        int topCount = 0;
        int i = 0;
        foreach (var r in ordered)
        {
            if (r.Count <= 0) break;
            if (i == 0) topCount = r.Count;
            if (r.Count != prevCount)
            {
                rank = i + 1;
                prevCount = r.Count;
            }
            if (rank > TopRank) break;
            string? url = _ctx.EntityUrls.CharacterUrl(r.Character.CharacterId);
            string name = HtmlUtil.Escape(r.Character.Name);
            entries.Add(new RecordEntry
            {
                RankLabel = rank.ToString(),
                ValueLabel = $"{r.Count}{r.Unit}",
                ValueNumber = r.Count.ToString(),
                ValueUnit = r.Unit,
                BarPercent = BarPercentOf(r.Count, topCount),
                NameHtml = url is null ? name : $"<a href=\"{url}\">{name}</a>",
                SubHtml = HtmlUtil.Escape(r.Sub)
            });
            i++;
        }
        return new RecordItem { Id = id, Title = title, Entries = entries, MoreUrl = moreUrl, MoreLabel = "キャラクター一覧を見る" };
    }

    // ── 共通 ──

    /// <summary>1 位の数に対する割合（%。棒の長さ）。細すぎる棒にならないよう下限を 2% にする。</summary>
    private static int BarPercentOf(int count, int topCount)
        => topCount <= 0 ? 0 : Math.Clamp((int)Math.Round(count * 100.0 / topCount), 2, 100);

    /// <summary>シリーズ名（リンク）と年度の添え書き。複数シリーズが並ぶ文脈なので年度を薄色で付ける。</summary>
    private static string SeriesRefHtml(string seriesUrl, string seriesTitle, string startYearLabel)
        => $"<a href=\"{seriesUrl}\">{HtmlUtil.Escape(seriesTitle)}</a> <span class=\"stats-ep-year\">({startYearLabel})</span>";

    // ── テンプレ用モデル ──

    private sealed class RecordsIndexModel
    {
        public IReadOnlyList<RecordGroup> Groups { get; set; } = Array.Empty<RecordGroup>();
    }

    /// <summary>記録の大分類（本編の尺 / スタッフ・声優 / キャラクター）。</summary>
    private sealed class RecordGroup
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Lead { get; set; } = "";
        /// <summary>この分類の基準点ラベル（尺はパート情報、スタッフとキャラクターはクレジットの収録範囲）。</summary>
        public string CoverageLabel { get; set; } = "";
        public IReadOnlyList<RecordItem> Items { get; set; } = Array.Empty<RecordItem>();
        /// <summary>左右に並べる 2 項目の組（本編の尺）。組で出す分類は <see cref="Items"/> を持たない。</summary>
        public IReadOnlyList<RecordPair> Pairs { get; set; } = Array.Empty<RecordPair>();
    }

    /// <summary>対になる記録 2 項目（長い回と短い回、早い回と遅い回）。1 つの枠の左右に並べる。</summary>
    private sealed class RecordPair
    {
        public string Title { get; set; } = "";
        public RecordItem High { get; set; } = new();
        public RecordItem Low { get; set; } = new();
    }

    /// <summary>記録 1 項目（カード 1 枚）。</summary>
    private sealed class RecordItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public IReadOnlyList<RecordEntry> Entries { get; set; } = Array.Empty<RecordEntry>();
        /// <summary>1 位の行（同率なら全件）。大きく出す。</summary>
        public IReadOnlyList<RecordEntry> Tops => Entries.Where(e => e.RankLabel == "1").ToList();
        /// <summary>2 位以下の行。</summary>
        public IReadOnlyList<RecordEntry> Rest => Entries.Where(e => e.RankLabel != "1").ToList();
        /// <summary>全体のランキングなど、続きを見るページ。空なら出さない。</summary>
        public string MoreUrl { get; set; } = "";
        public string MoreLabel { get; set; } = "";
    }

    /// <summary>記録の 1 行。<see cref="NameHtml"/> と <see cref="SubHtml"/> はリンク化済みの HTML 断片。</summary>
    private sealed class RecordEntry
    {
        public string RankLabel { get; set; } = "";
        public string ValueLabel { get; set; } = "";
        /// <summary>値の数の部分（「150」「7:50」）。大きく出す。</summary>
        public string ValueNumber { get; set; } = "";
        /// <summary>値の単位（「話」「役」「作品」）。時間の記録は空。</summary>
        public string ValueUnit { get; set; } = "";
        /// <summary>1 位の数に対する割合（%）。数を棒で比べる記録だけが持つ（時間の記録は 0）。</summary>
        public int BarPercent { get; set; }
        public string NameHtml { get; set; } = "";
        public string SubHtml { get; set; } = "";
    }
}
