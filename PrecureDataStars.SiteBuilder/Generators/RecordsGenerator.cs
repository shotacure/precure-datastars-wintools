using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 歴代記録（<c>/stats/records/</c>）と、本放送・配信・円盤の尺の違い（<c>/stats/records/format-differences/</c>）の生成。
/// <para>
/// 歴代記録は「いちばん」を集めた記録集。本編の尺（アバンタイトル・A パート・B パートの長短、中 CM の早遅）は
/// パート尺統計と同じランキング SQL から上位だけを取り、スタッフ（脚本・絵コンテ・演出・作画監督の担当話数、
/// 演出と作画監督の組み合わせ、声の出演の話数・役の数）とキャラクター（登場話数・登場作品数）は
/// クレジットの関与索引から数える。順位は同点を同じ順位とし、次の順位は同点の数だけ繰り下げる
/// （3 位までを出し、同率は全件載せる）。
/// </para>
/// <para>
/// 尺の違いは、本放送を基準に配信（Amazon Prime Video）と Blu-ray / DVD でパートの長さが違う話を並べる。
/// 本編（アバンタイトル・オープニング・A / B / C パート・エンディング）、次回予告、その他のパートの 3 つに分け、
/// それぞれシリーズごとにまとめる。本放送で実測できていないパート（備考に【本放送未確認】）は比べない。
/// </para>
/// </summary>
public sealed class RecordsGenerator
{
    /// <summary>歴代記録の URL。</summary>
    public const string IndexUrl = "/stats/records/";

    /// <summary>本放送・配信・円盤の尺の違いの URL。</summary>
    public const string FormatDifferencesUrl = "/stats/records/format-differences/";

    /// <summary>記録集に載せる順位の上限（同率は全件）。</summary>
    private const int TopRank = 3;

    /// <summary>ランキング SQL から取る件数。同率が続いても 3 位までが収まる程度に取る。</summary>
    private const int FetchLimit = 10;

    /// <summary>番組開始基準時刻（08:30:00）。中 CM 入り時刻の絶対時刻表示で使う（パート尺統計と同じ）。</summary>
    private static readonly TimeSpan ProgramStart = new(8, 30, 0);

    /// <summary>尺の違いで「本編」に数えるパート種別。</summary>
    private static readonly HashSet<string> MainParts = new(StringComparer.Ordinal)
    {
        "AVANT", "OPENING", "PART_A", "PART_B", "PART_C", "ENDING"
    };

    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly EpisodePartStatsRepository _partStatsRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CreditInvolvementIndex _involvements;
    private readonly EpisodeChiefStaffIndex _chiefs;

    public RecordsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex involvements,
        EpisodeChiefStaffIndex chiefs)
    {
        _ctx = ctx;
        _page = page;
        _partStatsRepo = new EpisodePartStatsRepository(factory);
        _charactersRepo = new CharactersRepository(factory);
        _involvements = involvements;
        _chiefs = chiefs;
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating records");

        var episodeIdsWithParts = (await _partStatsRepo.GetEpisodeIdsWithPartsAsync(ct).ConfigureAwait(false)).ToHashSet();
        string partsCoverage = StatsCoverageLabel.Build(StatsCoverageLabel.FindLatestTvEpisodeWithParts(_ctx, episodeIdsWithParts));
        string creditCoverage = _ctx.CreditCoverageLabel;

        var groups = new List<RecordGroup>
        {
            await BuildPartLengthGroupAsync(partsCoverage, ct).ConfigureAwait(false),
            BuildStaffGroup(creditCoverage),
            await BuildCharacterGroupAsync(creditCoverage, ct).ConfigureAwait(false)
        };
        var formatDiff = BuildFormatDifferences(partsCoverage);

        GenerateIndex(groups, formatDiff, partsCoverage);
        GenerateFormatDifferences(formatDiff);

        _ctx.Logger.Success($"{IndexUrl}, {FormatDifferencesUrl}");
    }

    // ── 歴代記録（索引） ──

    private void GenerateIndex(IReadOnlyList<RecordGroup> groups, FormatDiffModel formatDiff, string partsCoverage)
    {
        var content = new RecordsIndexModel
        {
            Groups = groups,
            FormatDiff = formatDiff
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代記録",
            // カードは記録を並べず、どんなページかをリード文で示す（記録の項目数はサイト側の都合の数なので出さない）。
            OgCard = new OgCardSpec(Kicker: "統計", Title: "歴代記録")
            {
                MetaLeft = OgCoverageLabel.Compact(partsCoverage),
                Subtitle = "いちばん長いアバンタイトルは？ いちばん多く脚本を書いた方は？ 歴代プリキュアの「いちばん」を集めました。"
            },
            MetaDescription = "歴代プリキュアの記録集。いちばん長いアバンタイトル、いちばん多く脚本を書いた方、いちばん多くの話に登場したキャラクターなど、全シリーズ・全話のデータから「いちばん」を集めました。",
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
        var items = new List<RecordItem>
        {
            await PartLengthItemAsync("AVANT", "アバンタイトルが長い回", "avant", ascending: false, ct).ConfigureAwait(false),
            await PartLengthItemAsync("AVANT", "アバンタイトルが短い回", "avant", ascending: true, ct).ConfigureAwait(false),
            await PartLengthItemAsync("PART_A", "Aパートが長い回", "part-a", ascending: false, ct).ConfigureAwait(false),
            await PartLengthItemAsync("PART_A", "Aパートが短い回", "part-a", ascending: true, ct).ConfigureAwait(false),
            await PartLengthItemAsync("PART_B", "Bパートが長い回", "part-b", ascending: false, ct).ConfigureAwait(false),
            await PartLengthItemAsync("PART_B", "Bパートが短い回", "part-b", ascending: true, ct).ConfigureAwait(false),
            await MidCmItemAsync(earliest: true, ct).ConfigureAwait(false),
            await MidCmItemAsync(earliest: false, ct).ConfigureAwait(false)
        };
        return new RecordGroup
        {
            Id = "part-length",
            Title = "本編の尺",
            Lead = "本放送で測ったパートの長さと、中 CM に入る時刻です。順位の全体は歴代エピソード尺統計にあります。",
            CoverageLabel = coverage,
            Items = items
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
            Title = earliest ? "中 CM に入るのが早い回" : "中 CM に入るのが遅い回",
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

    // ── スタッフ ──

    /// <summary>人物 1 人分の数え上げ。話は (episode_id) で重複を除く。</summary>
    private sealed class PersonStats
    {
        public Dictionary<int, HashSet<int>> ChiefEpisodes { get; } = new();
        public HashSet<int> VoiceEpisodes { get; } = new();
        public HashSet<int> Characters { get; } = new();
        public bool IsEmpty => ChiefEpisodes.Count == 0 && VoiceEpisodes.Count == 0;
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
                    if (_ctx.IsMovieKindSeries(inv.SeriesId)) continue;
                    if (inv.IsVoiceCast)
                    {
                        st.VoiceEpisodes.Add(episodeId);
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
                stats.Select(kv => (kv.Key, kv.Value.ChiefEpisodes.TryGetValue(chief, out var s) ? s.Count : 0)),
                PathUtil.CreatorsRoleUrl(roleCode),
                $"{label}の担当回数順を見る"));
        }
        items.Add(TopCombosItem(EpisodeChiefStaffIndex.DirectorAndAnimationDirector, "combo-director-ad", "多く組んだ演出と作画監督"));
        items.Add(TopCombosItem(EpisodeChiefStaffIndex.ScreenplayDirectorAnimationDirector, "combo-screenplay-director-ad", "多く組んだ脚本・演出・作画監督"));
        items.Add(TopPersons("voice-episodes", "声の出演が多い声優さん", "話",
            stats.Select(kv => (kv.Key, kv.Value.VoiceEpisodes.Count)),
            PathUtil.CreatorsVoiceCastUrl(), "声の出演一覧を見る"));
        items.Add(TopPersons("voice-characters", "演じた役が多い声優さん", "役",
            stats.Select(kv => (kv.Key, kv.Value.Characters.Count)),
            PathUtil.CreatorsVoiceCastUrl(), "声の出演一覧を見る"));

        return new RecordGroup
        {
            Id = "staff",
            Title = "スタッフ・声優",
            Lead = "TV シリーズのクレジットから数えた話数です。同じ人の別名義はまとめ、1 話に同じ役職で 2 度載っても 1 話に数えます。",
            CoverageLabel = coverage,
            Items = items
        };
    }

    /// <summary>人物の数え上げから上位を取り出す。同数は同じ順位で、名前の読みの順に並べる。</summary>
    private RecordItem TopPersons(string id, string title, string unit, IEnumerable<(int PersonId, int Count)> counts, string moreUrl, string moreLabel)
    {
        var ordered = counts
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => _ctx.EntityUrls.PersonDisplayKana(c.PersonId) ?? "", StringComparer.Ordinal)
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
                NameHtml = PersonLinkHtml(ordered[i].PersonId)
            });
        }
        return new RecordItem { Id = id, Title = title, Entries = entries, MoreUrl = moreUrl, MoreLabel = moreLabel };
    }

    private RecordItem TopCombosItem(ChiefCombo combo, string id, string title)
    {
        var entries = _chiefs.TopCombos(combo, TopRank)
            .Select(r => new RecordEntry
            {
                RankLabel = r.Rank.ToString(),
                ValueLabel = $"{r.Count}話",
                NameHtml = string.Join(" × ", r.Members.Select(m =>
                    $"<span class=\"records-role\">{HtmlUtil.Escape(m.RoleLabel)}</span> "
                    + string.Join("、", m.Persons.Select(ChiefPersonHtml)))),
                SubHtml = r.EpisodeIds.Count > 0
                    ? "初回 " + EpisodeRefHtml(r.EpisodeIds[0]) + (r.EpisodeIds.Count > 1 ? " ／ 最新 " + EpisodeRefHtml(r.EpisodeIds[^1]) : "")
                    : ""
            })
            .ToList();
        return new RecordItem { Id = id, Title = title, Entries = entries };
    }

    private static string ChiefPersonHtml(ChiefPerson p)
        => string.IsNullOrEmpty(p.Url) ? HtmlUtil.Escape(p.Name) : $"<a href=\"{p.Url}\">{HtmlUtil.Escape(p.Name)}</a>";

    private string PersonLinkHtml(int personId)
    {
        string name = _ctx.EntityUrls.PersonDisplayName(personId) ?? "";
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

        var rows = new List<(Character Character, int Episodes, int Movies, int Works)>();
        foreach (var c in characters)
        {
            if (!aliasesByCharacter.TryGetValue(c.CharacterId, out var aliasIds)) continue;
            var (episodes, movies) = CharacterAppearanceCounter.Count(_ctx, _involvements, aliasIds);
            if (episodes == 0 && movies == 0) continue;

            // 登場作品数：声の出演のクレジットがあるシリーズを 1 作品と数える。併映の短編は親映画に含める。
            var works = new HashSet<int>();
            foreach (var aliasId in aliasIds)
            {
                if (!_involvements.VoiceCastByCharacterAlias.TryGetValue(aliasId, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (!_ctx.SeriesById.TryGetValue(inv.SeriesId, out var series)) continue;
                    works.Add(SeriesClassifier.IsMovieShortChild(series) && series.ParentSeriesId is int parentId ? parentId : series.SeriesId);
                }
            }
            rows.Add((c, episodes, movies, works.Count));
        }

        var items = new List<RecordItem>
        {
            TopCharacters("character-episodes", "登場話数が多いキャラクター", rows
                .OrderByDescending(r => r.Episodes).ThenByDescending(r => r.Movies).ThenBy(r => r.Character.NameKana ?? "", StringComparer.Ordinal)
                .Select(r => (r.Character, r.Episodes, $"{r.Episodes}話", r.Movies > 0 ? $"映画 {r.Movies}本" : ""))),
            TopCharacters("character-works", "登場作品数が多いキャラクター", rows
                .OrderByDescending(r => r.Works).ThenByDescending(r => r.Episodes).ThenBy(r => r.Character.NameKana ?? "", StringComparer.Ordinal)
                .Select(r => (r.Character, r.Works, $"{r.Works}作品", $"TV {r.Episodes}話・映画 {r.Movies}本")))
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

    private RecordItem TopCharacters(string id, string title, IEnumerable<(Character Character, int Count, string Value, string Sub)> ordered)
    {
        var entries = new List<RecordEntry>();
        int rank = 0;
        int prevCount = -1;
        int i = 0;
        foreach (var r in ordered)
        {
            if (r.Count <= 0) break;
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
                ValueLabel = r.Value,
                NameHtml = url is null ? name : $"<a href=\"{url}\">{name}</a>",
                SubHtml = HtmlUtil.Escape(r.Sub)
            });
            i++;
        }
        return new RecordItem { Id = id, Title = title, Entries = entries, MoreUrl = "/characters/", MoreLabel = "キャラクター一覧を見る" };
    }

    // ── 本放送・配信・円盤の尺の違い ──

    private FormatDiffModel BuildFormatDifferences(string coverage)
    {
        var sections = new List<FormatDiffSection>
        {
            new()
            {
                Id = "main",
                Title = "本編の尺が違う話",
                Lead = "アバンタイトル・オープニング・A パート・B パート・C パート・エンディングの長さが、本放送と配信・円盤で違う話です。"
            },
            new()
            {
                Id = "trailer",
                Title = "次回予告の尺が違う話",
                Lead = "次回予告の長さが違う話です。本放送では告知などのために予告が短縮されることがあり、配信・円盤には短縮前の予告が収められています。"
            },
            new()
            {
                Id = "other",
                Title = "その他のパートの尺が違う話",
                Lead = "本編・次回予告以外のパートの長さが違う話です。"
            }
        };
        var bySectionId = sections.ToDictionary(s => s.Id, StringComparer.Ordinal);

        int compared = 0;
        var episodesWithDifference = new HashSet<int>();

        foreach (var series in _ctx.Series.Where(s => !_ctx.IsMovieKindSeries(s.SeriesId)).OrderBy(s => s.StartDate).ThenBy(s => s.SeriesId))
        {
            if (!_ctx.EpisodesBySeries.TryGetValue(series.SeriesId, out var episodes)) continue;
            var rowsBySection = sections.ToDictionary(s => s.Id, _ => new List<FormatDiffRow>(), StringComparer.Ordinal);

            foreach (var ep in episodes)
            {
                if (!_ctx.EpisodePartsByEpisode.TryGetValue(ep.EpisodeId, out var parts) || parts.Count == 0) continue;

                bool anyComparable = false;
                var partsBySection = sections.ToDictionary(s => s.Id, _ => new List<FormatDiffPart>(), StringComparer.Ordinal);
                foreach (var p in parts.OrderBy(x => x.EpisodeSeq))
                {
                    if (p.OaLength is not ushort oa) continue;
                    if ((p.Notes ?? "").Contains("【本放送未確認】", StringComparison.Ordinal)) continue;
                    if (p.VodLength is null && p.DiscLength is null) continue;
                    anyComparable = true;

                    int? vodDelta = p.VodLength is ushort vod ? vod - oa : null;
                    int? discDelta = p.DiscLength is ushort disc ? disc - oa : null;
                    if ((vodDelta ?? 0) == 0 && (discDelta ?? 0) == 0) continue;

                    string sectionId = p.PartType == "TRAILER" ? "trailer" : MainParts.Contains(p.PartType) ? "main" : "other";
                    partsBySection[sectionId].Add(new FormatDiffPart
                    {
                        PartName = _ctx.PartTypeByCode.TryGetValue(p.PartType, out var pt) ? pt.NameJa : p.PartType,
                        PartCss = FormatTableBuilder.PaletteCss(p.PartType),
                        OaLabel = HtmlUtil.FormatSeconds(oa),
                        VodLabel = p.VodLength is ushort v ? HtmlUtil.FormatSeconds(v) : "",
                        VodDeltaLabel = DeltaLabel(vodDelta),
                        DiscLabel = p.DiscLength is ushort d ? HtmlUtil.FormatSeconds(d) : "",
                        DiscDeltaLabel = DeltaLabel(discDelta),
                        Note = BroadcastNoteText.ForDisplay(p.Notes)
                    });
                }
                if (anyComparable) compared++;

                foreach (var (sectionId, diffParts) in partsBySection)
                {
                    if (diffParts.Count == 0) continue;
                    episodesWithDifference.Add(ep.EpisodeId);
                    rowsBySection[sectionId].Add(new FormatDiffRow
                    {
                        SeriesEpNo = ep.SeriesEpNo,
                        EpisodeUrl = PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo),
                        TitleHtml = SubtitleGuardRenderer.GuardRichHtml(
                            StatsEpisodeRows.BuildTitleHtml(ep.TitleRichHtml, ep.TitleText),
                            SubtitleGuardRenderer.RevealAtFor(ep.EpisodeId, _ctx.SubtitleRevealAtByEpisodeId)),
                        OnAirDate = JpDateFormat.Date(ep.OnAirAt),
                        Parts = diffParts
                    });
                }
            }

            foreach (var (sectionId, rows) in rowsBySection)
            {
                if (rows.Count == 0) continue;
                bySectionId[sectionId].SeriesGroups.Add(new FormatDiffSeriesGroup
                {
                    SeriesTitle = series.Title,
                    SeriesUrl = PathUtil.SeriesUrl(series.Slug),
                    SeriesStartYearLabel = _ctx.StartYearLabel(series.SeriesId),
                    Rows = rows
                });
            }
        }

        foreach (var s in sections)
            s.EpisodeCount = s.SeriesGroups.Sum(g => g.Rows.Count);

        return new FormatDiffModel
        {
            CoverageLabel = coverage,
            ComparedEpisodes = compared,
            EpisodesWithDifference = episodesWithDifference.Count,
            Sections = sections.Where(s => s.SeriesGroups.Count > 0).ToList(),
            MainCount = bySectionId["main"].EpisodeCount,
            TrailerCount = bySectionId["trailer"].EpisodeCount,
            OtherCount = bySectionId["other"].EpisodeCount
        };
    }

    private void GenerateFormatDifferences(FormatDiffModel content)
    {
        var layout = new LayoutModel
        {
            PageTitle = "本放送・配信・円盤の尺の違い",
            OgCard = new OgCardSpec(Kicker: "歴代記録", Title: "本放送・配信・円盤の尺の違い")
            {
                // カードのリード文（どんなページか。数の代わり、または数の下に添える）。
                Subtitle = "同じ話でも、本放送と配信・Blu-ray / DVD でパートの長さが違うことがあります。本放送を基準に、違いのある話を集めました。",
                MetaLeft = OgCoverageLabel.Compact(content.CoverageLabel),
                Badges = new[]
                {
                    new OgCardBadge("本編が違う", $"{content.MainCount}話"),
                    new OgCardBadge("予告が違う", $"{content.TrailerCount}話")
                }
            },
            MetaDescription = "プリキュアの本放送・配信（Amazon Prime Video）・Blu-ray / DVD でパートの長さが違う話の一覧。本放送を基準に、歴代全シリーズから違いのある話を集めました。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/" },
                new BreadcrumbItem { Label = "歴代記録", Url = IndexUrl },
                new BreadcrumbItem { Label = "本放送・配信・円盤の尺の違い", Url = "" }
            }
        };
        _page.RenderAndWrite(FormatDifferencesUrl, "stats", "stats-records-format-differences.sbn", content, layout);
    }

    /// <summary>本放送との差の表記。長ければ「+0:15」、短ければ「−0:15」、差が無いか比べられなければ空文字。</summary>
    private static string DeltaLabel(int? delta)
    {
        if (delta is not int d || d == 0) return "";
        return d > 0 ? "+" + HtmlUtil.FormatSeconds(d) : "−" + HtmlUtil.FormatSeconds(-d);
    }

    // ── 共通 ──

    /// <summary>シリーズ名（リンク）と年度の添え書き。複数シリーズが並ぶ文脈なので年度を薄色で付ける。</summary>
    private static string SeriesRefHtml(string seriesUrl, string seriesTitle, string startYearLabel)
        => $"<a href=\"{seriesUrl}\">{HtmlUtil.Escape(seriesTitle)}</a> <span class=\"stats-ep-year\">({startYearLabel})</span>";

    /// <summary>話への参照（「シリーズ名 (年) 第N話」。話はリンク、サブタイトルは出さない）。</summary>
    private string EpisodeRefHtml(int episodeId)
    {
        if (!_ctx.EpisodeById.TryGetValue(episodeId, out var ep)) return "";
        if (!_ctx.SeriesById.TryGetValue(ep.SeriesId, out var series)) return "";
        return SeriesRefHtml(PathUtil.SeriesUrl(series.Slug), series.Title, _ctx.StartYearLabel(series.SeriesId))
            + $" <a href=\"{PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo)}\">第{ep.SeriesEpNo}話</a>";
    }

    // ── テンプレ用モデル ──

    private sealed class RecordsIndexModel
    {
        public IReadOnlyList<RecordGroup> Groups { get; set; } = Array.Empty<RecordGroup>();
        public FormatDiffModel FormatDiff { get; set; } = new();
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
    }

    /// <summary>記録 1 項目（カード 1 枚）。</summary>
    private sealed class RecordItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public IReadOnlyList<RecordEntry> Entries { get; set; } = Array.Empty<RecordEntry>();
        /// <summary>全体のランキングなど、続きを見るページ。空なら出さない。</summary>
        public string MoreUrl { get; set; } = "";
        public string MoreLabel { get; set; } = "";
    }

    /// <summary>記録の 1 行。<see cref="NameHtml"/> と <see cref="SubHtml"/> はリンク化済みの HTML 断片。</summary>
    private sealed class RecordEntry
    {
        public string RankLabel { get; set; } = "";
        public string ValueLabel { get; set; } = "";
        public string NameHtml { get; set; } = "";
        public string SubHtml { get; set; } = "";
    }

    private sealed class FormatDiffModel
    {
        public string CoverageLabel { get; set; } = "";
        /// <summary>本放送と配信・円盤のどちらかを比べられた話の数。</summary>
        public int ComparedEpisodes { get; set; }
        /// <summary>どこかのパートの長さが違った話の数（本編・予告・その他の重複を除く）。</summary>
        public int EpisodesWithDifference { get; set; }
        public int MainCount { get; set; }
        public int TrailerCount { get; set; }
        public int OtherCount { get; set; }
        public IReadOnlyList<FormatDiffSection> Sections { get; set; } = Array.Empty<FormatDiffSection>();
    }

    private sealed class FormatDiffSection
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Lead { get; set; } = "";
        public int EpisodeCount { get; set; }
        public List<FormatDiffSeriesGroup> SeriesGroups { get; } = new();
    }

    private sealed class FormatDiffSeriesGroup
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string SeriesStartYearLabel { get; set; } = "";
        public IReadOnlyList<FormatDiffRow> Rows { get; set; } = Array.Empty<FormatDiffRow>();
    }

    private sealed class FormatDiffRow
    {
        public int SeriesEpNo { get; set; }
        public string EpisodeUrl { get; set; } = "";
        /// <summary>サブタイトル（ルビ付き・解禁ガード済みの HTML）。</summary>
        public string TitleHtml { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public IReadOnlyList<FormatDiffPart> Parts { get; set; } = Array.Empty<FormatDiffPart>();
    }

    private sealed class FormatDiffPart
    {
        public string PartName { get; set; } = "";
        public string PartCss { get; set; } = "";
        public string OaLabel { get; set; } = "";
        public string VodLabel { get; set; } = "";
        public string VodDeltaLabel { get; set; } = "";
        public string DiscLabel { get; set; } = "";
        public string DiscDeltaLabel { get; set; } = "";
        public string Note { get; set; } = "";
    }
}
