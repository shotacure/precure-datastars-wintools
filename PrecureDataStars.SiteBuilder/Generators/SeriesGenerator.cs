using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// シリーズ系ページ（一覧 + 個別）の生成。
/// <para>
/// <c>/series/</c> は全シリーズの索引、<c>/series/{slug}/</c> は各シリーズの詳細。
/// </para>
/// <para>
/// 一覧の構成（v1.3.0 ブラッシュアップ）：
/// </para>
/// <list type="number">
///   <item><description>TV シリーズセクション：放送開始順。配下の併映短編・子映画は字下げで親 TV の下に表示。</description></item>
///   <item><description>映画セクション：親 TV を持たない単独映画系（秋映画・春映画）。
///     ※併映短編や子映画は親シリーズの下に出すので、ここでは <c>parent_series_id</c> が無いものだけ出す。</description></item>
///   <item><description>スピンオフセクション：SPIN-OFF 種別だけを集める。</description></item>
/// </list>
/// <para>
/// 子作品（<c>parent_series_id</c> が NULL でない映画系）は単独詳細ページを生成しない。
/// 親映画詳細の中の「併映・子作品」セクションに一覧表示するだけにとどめる。
/// これにより sitemap・search-index・ナビからも自動的に除外される（生成しないため）。
/// </para>
/// <para>
/// 個別シリーズページのエピソード一覧は <see cref="SeriesKind.CreditAttachTo"/> が EPISODE のときだけ表示する。
/// 表構造ではなく <c>&lt;dl class="ep-list"&gt;</c> + <c>&lt;dt&gt;</c>（話数 + サブタイトル）+ <c>&lt;dd&gt;</c>
/// （字下げでスタッフ群）の縦並びレイアウト。
/// </para>
/// <para>
/// メインスタッフセクションは PRODUCER / SERIES_COMPOSITION / SERIES_DIRECTOR / CHARACTER_DESIGN / ART_DESIGN
/// の 5 役職を出し、全話担当者は名前のみ、部分担当者は「名前 (#1～4, 8)」表記。
/// </para>
/// <para>
/// 劇伴一覧はシリーズ詳細から除外し、別ページ <c>/bgms/{slug}/</c> へのリンク 1 本に置き換える。
/// </para>
/// </summary>
public sealed class SeriesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    // ── スタッフ抽出用のクレジット階層 Repository（各エピソードに対して走査する） ──
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditCardsRepository _staffCardsRepo;
    private readonly CreditCardTiersRepository _staffTiersRepo;
    private readonly CreditCardGroupsRepository _staffGroupsRepo;
    private readonly CreditCardRolesRepository _staffCardRolesRepo;
    private readonly CreditRoleBlocksRepository _staffBlocksRepo;
    private readonly CreditBlockEntriesRepository _staffEntriesRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

    // ── スタッフ名リンク化（エピソード「行」のスタッフ群リンク化に使用） ──
    private readonly StaffNameLinkResolver _staffLinkResolver;

    // ── メインスタッフ集計用 ──
    private readonly CreditInvolvementIndex _involvementIndex;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;

    // ── 役職マスタ・name 解決の共通キャッシュ ──
    private IReadOnlyDictionary<string, Role>? _roleMap;
    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();

    // ── メインスタッフ集計用キャッシュ ──
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasIdsByPersonIdCache;
    private IReadOnlyList<Person>? _allPersonsCache;
    private IReadOnlyDictionary<string, SeriesKind>? _seriesKindMapCache;

    public SeriesGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        StaffNameLinkResolver staffLinkResolver,
        CreditInvolvementIndex involvementIndex)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _staffLinkResolver = staffLinkResolver;
        _involvementIndex = involvementIndex;

        _creditsRepo = new CreditsRepository(factory);
        _staffCardsRepo = new CreditCardsRepository(factory);
        _staffTiersRepo = new CreditCardTiersRepository(factory);
        _staffGroupsRepo = new CreditCardGroupsRepository(factory);
        _staffCardRolesRepo = new CreditCardRolesRepository(factory);
        _staffBlocksRepo = new CreditRoleBlocksRepository(factory);
        _staffEntriesRepo = new CreditBlockEntriesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _seriesKindsRepo = new SeriesKindsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating series");

        GenerateIndex();

        // 子作品（parent_series_id != NULL の映画系）は単独詳細ページを生成しない。
        // 親映画詳細の「併映・子作品」セクションに表示されるだけにする。
        // SPIN-OFF は親を持っても単独ページが必要（parent_series_id を持つことは想定していないが念のため）。
        int generated = 0;
        foreach (var s in _ctx.Series)
        {
            if (IsChildOfMovie(s)) continue;
            await GenerateDetailAsync(s, ct).ConfigureAwait(false);
            generated++;
        }

        _ctx.Logger.Success($"series: {generated + 1} ページ");
    }

    /// <summary>
    /// 子作品判定：親シリーズが存在し、かつ自分が SPIN-OFF ではない場合は子作品扱い。
    /// （秋映画併映短編・子映画など、親映画にぶら下がる作品が該当）。
    /// </summary>
    private static bool IsChildOfMovie(Series s)
    {
        if (!s.ParentSeriesId.HasValue) return false;
        if (string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// <c>/series/</c> の索引ページ。TV / 映画 / スピンオフ の 3 セクション構成。
    /// </summary>
    private void GenerateIndex()
    {
        // TV シリーズだけを抽出（年代順）
        var tvSeries = _ctx.Series
            .Where(s => s.KindCode == "TV")
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .ToList();

        // TV ごとに配下のシリーズ（SPIN-OFF 以外）を集める。
        var childrenByTv = _ctx.Series
            .Where(s => s.ParentSeriesId.HasValue && s.KindCode != "SPIN-OFF")
            .GroupBy(s => s.ParentSeriesId!.Value)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(c => c.StartDate)
                .ThenBy(c => c.SeqInParent ?? (byte)0)
                .ToList());

        var tvRows = tvSeries.Select(tv => new TvSeriesRow
        {
            Slug = tv.Slug,
            Title = tv.Title,
            Period = FormatPeriod(tv.StartDate, tv.EndDate),
            EpisodesLabel = tv.Episodes.HasValue ? $"全 {tv.Episodes.Value} 話" : "",
            Children = (childrenByTv.TryGetValue(tv.SeriesId, out var kids) ? kids : new List<Series>())
                .Select(c => new RelatedSeriesRow
                {
                    Slug = c.Slug,
                    Title = c.Title,
                    KindLabel = LookupKindLabel(c.KindCode),
                    Period = FormatPeriod(c.StartDate, c.EndDate),
                    HasOwnPage = !IsChildOfMovie(c)
                })
                .ToList()
        }).ToList();

        // 映画セクション（親 TV を持たない単独映画系）。
        var movieRows = _ctx.Series
            .Where(s => s.KindCode != "TV"
                     && s.KindCode != "SPIN-OFF"
                     && !s.ParentSeriesId.HasValue)
            .OrderBy(s => s.StartDate)
            .Select(s => new RelatedSeriesRow
            {
                Slug = s.Slug,
                Title = s.Title,
                KindLabel = LookupKindLabel(s.KindCode),
                Period = FormatPeriod(s.StartDate, s.EndDate),
                HasOwnPage = true
            })
            .ToList();

        // スピンオフセクション。
        var spinOffRows = _ctx.Series
            .Where(s => s.KindCode == "SPIN-OFF")
            .OrderBy(s => s.StartDate)
            .Select(s => new RelatedSeriesRow
            {
                Slug = s.Slug,
                Title = s.Title,
                KindLabel = LookupKindLabel(s.KindCode),
                Period = FormatPeriod(s.StartDate, s.EndDate),
                HasOwnPage = true
            })
            .ToList();

        var content = new SeriesIndexModel
        {
            TvSeries = tvRows,
            MovieSeries = movieRows,
            SpinOffSeries = spinOffRows,
            TotalCount = _ctx.Series.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "シリーズ一覧",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "シリーズ一覧", Url = "" }
            }
        };
        _page.RenderAndWrite("/series/", "series", "series-index.sbn", content, layout);
    }

    /// <summary>
    /// <c>/series/{slug}/</c> 個別シリーズページ。
    /// </summary>
    private async Task GenerateDetailAsync(Series s, CancellationToken ct)
    {
        bool hasEpisodes = false;
        if (_ctx.SeriesKindByCode.TryGetValue(s.KindCode, out var kind))
        {
            hasEpisodes = string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal);
        }

        var eps = _ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var list) ? list : Array.Empty<Episode>();
        var epRows = new List<EpisodeIndexRow>();
        if (hasEpisodes)
        {
            if (_roleMap is null)
            {
                var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
                _roleMap = allRoles.ToDictionary(r => r.RoleCode, r => r, StringComparer.Ordinal);
            }

            foreach (var e in eps.OrderBy(x => x.SeriesEpNo))
            {
                var staff = await ExtractStaffSummaryAsync(e.EpisodeId, ct).ConfigureAwait(false);
                epRows.Add(new EpisodeIndexRow
                {
                    SeriesEpNo = e.SeriesEpNo,
                    TitleText = e.TitleText,
                    OnAirDate = FormatJpDate(e.OnAirAt),
                    Screenplay = staff.Screenplay,
                    Storyboard = staff.Storyboard,
                    EpisodeDirector = staff.EpisodeDirector,
                    AnimationDirector = staff.AnimationDirector,
                    ArtDirector = staff.ArtDirector
                });
            }
        }

        // 関連シリーズ（自分が親で、配下にいる作品）。子作品（HasOwnPage=false）も併映として表示する。
        var related = _ctx.Series
            .Where(x => x.ParentSeriesId == s.SeriesId)
            .OrderBy(x => x.StartDate)
            .Select(x => new RelatedSeriesRow
            {
                Slug = x.Slug,
                Title = x.Title,
                KindLabel = LookupKindLabel(x.KindCode),
                Period = FormatPeriod(x.StartDate, x.EndDate),
                HasOwnPage = !IsChildOfMovie(x)
            })
            .ToList();

        // 親シリーズへのリンク。自分が子作品の場合は親への戻るリンクとして使う想定だが、
        // そもそも子作品は単独ページを生成しないのでここに到達するのは SPIN-OFF などのみ。
        RelatedSeriesRow? parent = null;
        if (s.ParentSeriesId is int pid && _ctx.SeriesById.TryGetValue(pid, out var p))
        {
            parent = new RelatedSeriesRow
            {
                Slug = p.Slug,
                Title = p.Title,
                KindLabel = LookupKindLabel(p.KindCode),
                Period = FormatPeriod(p.StartDate, p.EndDate),
                HasOwnPage = !IsChildOfMovie(p)
            };
        }

        var seriesView = new SeriesDetailView
        {
            Slug = s.Slug,
            Title = s.Title,
            TitleKana = s.TitleKana ?? "",
            TitleEn = s.TitleEn ?? "",
            KindLabel = LookupKindLabel(s.KindCode),
            Period = FormatPeriod(s.StartDate, s.EndDate),
            Episodes = s.Episodes?.ToString() ?? "",
            RunTimeSeconds = s.RunTimeSeconds?.ToString() ?? "",
            ToeiAnimOfficialSiteUrl = s.ToeiAnimOfficialSiteUrl ?? "",
            ToeiAnimLineupUrl = s.ToeiAnimLineupUrl ?? "",
            AbcOfficialSiteUrl = s.AbcOfficialSiteUrl ?? "",
            AmazonPrimeDistributionUrl = s.AmazonPrimeDistributionUrl ?? "",
            HasEpisodeList = hasEpisodes
        };
        seriesView.HasExternalUrls =
            seriesView.ToeiAnimOfficialSiteUrl.Length > 0 ||
            seriesView.ToeiAnimLineupUrl.Length > 0 ||
            seriesView.AbcOfficialSiteUrl.Length > 0 ||
            seriesView.AmazonPrimeDistributionUrl.Length > 0;

        // メインスタッフセクション（v1.3.0 ブラッシュアップ）。
        var keyStaffSections = await BuildMainStaffSectionsAsync(s, eps, ct).ConfigureAwait(false);

        var content = new SeriesDetailModel
        {
            Series = seriesView,
            Episodes = epRows,
            Related = related,
            Parent = parent,
            KeyStaffSections = keyStaffSections
        };

        // JSON-LD（TVSeries / Movie）
        string baseUrl = _ctx.Config.BaseUrl;
        string seriesUrl = PathUtil.SeriesUrl(s.Slug);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = s.KindCode == "MOVIE" ? "Movie" : "TVSeries",
            ["name"] = s.Title,
            ["alternateName"] = string.IsNullOrEmpty(s.TitleShort) ? null : s.TitleShort,
            ["description"] = $"{s.Title} のエピソード・スタッフ・楽曲情報。",
            ["startDate"] = s.StartDate.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja"
        };
        if (s.EndDate.HasValue) jsonLdDict["endDate"] = s.EndDate.Value.ToString("yyyy-MM-dd");
        if (s.Episodes.HasValue) jsonLdDict["numberOfEpisodes"] = s.Episodes.Value;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + seriesUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = s.Title,
            MetaDescription = $"{s.Title} のエピソード・スタッフ・楽曲情報。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "シリーズ一覧", Url = "/series/" },
                new BreadcrumbItem { Label = s.Title, Url = "" }
            },
            OgType = s.KindCode == "MOVIE" ? "video.movie" : "video.tv_show",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite(seriesUrl, "series", "series-detail.sbn", content, layout);
    }

    /// <summary>
    /// 指定エピソードのクレジット階層から、脚本・絵コンテ・演出・作画監督・美術の人物名を引く。
    /// </summary>
    private async Task<EpisodeStaffSummary> ExtractStaffSummaryAsync(int episodeId, CancellationToken ct)
    {
        var screenplay = new List<string>();
        var storyboard = new List<string>();
        var director = new List<string>();
        var animDirector = new List<string>();
        var artDirector = new List<string>();
        var seenSc = new HashSet<string>(StringComparer.Ordinal);
        var seenSb = new HashSet<string>(StringComparer.Ordinal);
        var seenDr = new HashSet<string>(StringComparer.Ordinal);
        var seenAd = new HashSet<string>(StringComparer.Ordinal);
        var seenAt = new HashSet<string>(StringComparer.Ordinal);

        int? ClassifyRole(CreditCardRole cr)
        {
            if (cr.RoleCode is null) return null;
            if (!_roleMap!.TryGetValue(cr.RoleCode, out var role)) return null;
            string code = cr.RoleCode;
            string nm = role.NameJa ?? "";
            if (code == "SCREENPLAY"          || nm == "脚本")     return 1;
            if (code == "STORYBOARD"          || nm == "絵コンテ") return 2;
            if (code == "EPISODE_DIRECTOR"    || nm == "演出")     return 3;
            if (code == "ANIMATION_DIRECTOR"  || nm == "作画監督") return 4;
            if (code == "ART_DIRECTOR"        || nm == "美術")     return 5;
            return null;
        }

        var credits = (await _creditsRepo.GetByEpisodeAsync(episodeId, ct).ConfigureAwait(false))
            .Where(c => !c.IsDeleted)
            .ToList();

        foreach (var credit in credits)
        {
            var cards = (await _staffCardsRepo.GetByCreditAsync(credit.CreditId, ct).ConfigureAwait(false))
                .OrderBy(c => c.CardSeq);
            foreach (var card in cards)
            {
                var tiers = (await _staffTiersRepo.GetByCardAsync(card.CardId, ct).ConfigureAwait(false))
                    .OrderBy(t => t.TierNo);
                foreach (var tier in tiers)
                {
                    var groups = (await _staffGroupsRepo.GetByTierAsync(tier.CardTierId, ct).ConfigureAwait(false))
                        .OrderBy(g => g.GroupNo);
                    foreach (var grp in groups)
                    {
                        var cardRoles = (await _staffCardRolesRepo.GetByGroupAsync(grp.CardGroupId, ct).ConfigureAwait(false))
                            .OrderBy(r => r.OrderInGroup);
                        foreach (var cr in cardRoles)
                        {
                            int? bucket = ClassifyRole(cr);
                            if (bucket is null) continue;

                            var blocks = (await _staffBlocksRepo.GetByCardRoleAsync(cr.CardRoleId, ct).ConfigureAwait(false))
                                .OrderBy(b => b.BlockSeq);
                            foreach (var b in blocks)
                            {
                                var entries = (await _staffEntriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                    .Where(e => !e.IsBroadcastOnly)
                                    .OrderBy(e => e.EntrySeq);
                                foreach (var e in entries)
                                {
                                    var (key, html) = await ResolveStaffEntryAsync(e, ct).ConfigureAwait(false);
                                    if (string.IsNullOrEmpty(html)) continue;
                                    switch (bucket)
                                    {
                                        case 1: if (seenSc.Add(key)) screenplay.Add(html); break;
                                        case 2: if (seenSb.Add(key)) storyboard.Add(html); break;
                                        case 3: if (seenDr.Add(key)) director.Add(html); break;
                                        case 4: if (seenAd.Add(key)) animDirector.Add(html); break;
                                        case 5: if (seenAt.Add(key)) artDirector.Add(html); break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return new EpisodeStaffSummary
        {
            Screenplay        = string.Join("、", screenplay),
            Storyboard        = string.Join("、", storyboard),
            EpisodeDirector   = string.Join("、", director),
            AnimationDirector = string.Join("、", animDirector),
            ArtDirector       = string.Join("、", artDirector)
        };
    }

    /// <summary>
    /// PERSON / TEXT エントリから (重複判定キー, 表示用 HTML 文字列) を取り出す。
    /// </summary>
    private async Task<(string Key, string Html)> ResolveStaffEntryAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    if (!_personAliasCache.TryGetValue(pid, out var pa))
                    {
                        pa = await _personAliasesRepo.GetByIdAsync(pid, ct).ConfigureAwait(false);
                        _personAliasCache[pid] = pa;
                    }
                    string? displayText = pa?.DisplayTextOverride ?? pa?.Name;
                    if (string.IsNullOrEmpty(displayText)) return ("", "");
                    string html = _staffLinkResolver.ResolveAsHtml(pid, displayText);
                    return ($"P:{pid}", html);
                }
                return ("", "");
            case "TEXT":
                {
                    string raw = e.RawText ?? "";
                    if (string.IsNullOrEmpty(raw)) return ("", "");
                    string html = _staffLinkResolver.ResolveAsHtml(null, raw);
                    return ($"T:{raw}", html);
                }
            default:
                return ("", "");
        }
    }

    /// <summary>kind_code → 表示用ラベル（name_ja）。</summary>
    private string LookupKindLabel(string code)
        => _ctx.SeriesKindByCode.TryGetValue(code, out var kind) ? kind.NameJa : code;

    /// <summary>放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。</summary>
    private static string FormatPeriod(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        return startStr;
    }

    /// <summary>放送日を「2004年2月1日」で返す。</summary>
    private static string FormatJpDate(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    /// <summary>
    /// メインスタッフセクション群を構築する。
    /// 5 役職：PRODUCER / SERIES_COMPOSITION / SERIES_DIRECTOR / CHARACTER_DESIGN / ART_DESIGN。
    /// 担当話数による足切りは無し（1 話でも掲載）。
    /// 全話担当者は名前のみ、部分担当者は「名前 (#1〜4, 8)」表記。
    /// </summary>
    private async Task<IReadOnlyList<KeyStaffSection>> BuildMainStaffSectionsAsync(
        Series series, IReadOnlyList<Episode> eps, CancellationToken ct)
    {
        _seriesKindMapCache ??= (await _seriesKindsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        if (!_seriesKindMapCache.TryGetValue(series.KindCode, out var kind)) return Array.Empty<KeyStaffSection>();
        if (!string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal)) return Array.Empty<KeyStaffSection>();

        if (_allPersonsCache is null)
            _allPersonsCache = await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        if (_aliasIdsByPersonIdCache is null)
        {
            var dict = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var p in _allPersonsCache)
            {
                var rows = await _personAliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
                dict[p.PersonId] = rows.Select(r => r.AliasId).ToList();
            }
            _aliasIdsByPersonIdCache = dict;
        }

        var roleSpecs = new (string Code, string Label)[]
        {
            ("PRODUCER",            "プロデューサー"),
            ("SERIES_COMPOSITION",  "シリーズ構成"),
            ("SERIES_DIRECTOR",     "シリーズディレクター"),
            ("CHARACTER_DESIGN",    "キャラクターデザイン"),
            ("ART_DESIGN",          "美術デザイン")
        };

        var allEpisodeNos = eps.Select(e => e.SeriesEpNo).Distinct().OrderBy(x => x).ToList();
        var epNoByEpId = eps.ToDictionary(e => e.EpisodeId, e => e.SeriesEpNo);

        var sections = new List<KeyStaffSection>();

        foreach (var spec in roleSpecs)
        {
            var rows = new List<MainStaffRow>();
            foreach (var p in _allPersonsCache)
            {
                if (!_aliasIdsByPersonIdCache.TryGetValue(p.PersonId, out var aliasIds)) continue;

                var episodeNos = new HashSet<int>();
                foreach (var aid in aliasIds)
                {
                    if (!_involvementIndex.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                    foreach (var inv in invs)
                    {
                        if (inv.SeriesId != series.SeriesId) continue;
                        if (!string.Equals(inv.RoleCode, spec.Code, StringComparison.Ordinal)) continue;
                        if (inv.EpisodeId is int eid
                            && epNoByEpId.TryGetValue(eid, out var epNo))
                        {
                            episodeNos.Add(epNo);
                        }
                    }
                }
                if (episodeNos.Count == 0) continue;

                bool isAllEpisodes = allEpisodeNos.Count > 0
                    && episodeNos.SetEquals(allEpisodeNos);
                string rangeLabel = isAllEpisodes
                    ? string.Empty
                    : EpisodeRangeCompressor.Compress(episodeNos);

                rows.Add(new MainStaffRow
                {
                    PersonId = p.PersonId,
                    // Person.FullName は string? 型のため空文字へフォールバック（NULL 警告の抑制）。
                    FullName = p.FullName ?? "",
                    RangeLabel = rangeLabel,
                    EpisodeCount = episodeNos.Count,
                    SortKey = p.FullNameKana ?? p.FullName ?? ""
                });
            }

            if (rows.Count == 0) continue;

            rows.Sort((a, b) =>
            {
                int c = b.EpisodeCount.CompareTo(a.EpisodeCount);
                if (c != 0) return c;
                int k = string.CompareOrdinal(a.SortKey, b.SortKey);
                if (k != 0) return k;
                return string.CompareOrdinal(a.FullName, b.FullName);
            });

            sections.Add(new KeyStaffSection
            {
                RoleLabel = spec.Label,
                Members = rows
            });
        }

        return sections;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class SeriesIndexModel
    {
        public IReadOnlyList<TvSeriesRow> TvSeries { get; set; } = Array.Empty<TvSeriesRow>();
        public IReadOnlyList<RelatedSeriesRow> MovieSeries { get; set; } = Array.Empty<RelatedSeriesRow>();
        public IReadOnlyList<RelatedSeriesRow> SpinOffSeries { get; set; } = Array.Empty<RelatedSeriesRow>();
        public int TotalCount { get; set; }
    }

    private sealed class TvSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Period { get; set; } = "";
        public string EpisodesLabel { get; set; } = "";
        public IReadOnlyList<RelatedSeriesRow> Children { get; set; } = Array.Empty<RelatedSeriesRow>();
    }

    private sealed class RelatedSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Period { get; set; } = "";
        /// <summary>子作品（HasOwnPage=false）はリンク化せず表示のみ行う。</summary>
        public bool HasOwnPage { get; set; } = true;
    }

    private sealed class SeriesDetailModel
    {
        public SeriesDetailView Series { get; set; } = new();
        public IReadOnlyList<EpisodeIndexRow> Episodes { get; set; } = Array.Empty<EpisodeIndexRow>();
        public IReadOnlyList<RelatedSeriesRow> Related { get; set; } = Array.Empty<RelatedSeriesRow>();
        public RelatedSeriesRow? Parent { get; set; }
        public IReadOnlyList<KeyStaffSection> KeyStaffSections { get; set; } = Array.Empty<KeyStaffSection>();
    }

    private sealed class KeyStaffSection
    {
        public string RoleLabel { get; set; } = "";
        public IReadOnlyList<MainStaffRow> Members { get; set; } = Array.Empty<MainStaffRow>();
    }

    private sealed class MainStaffRow
    {
        public int PersonId { get; set; }
        public string FullName { get; set; } = "";
        public string RangeLabel { get; set; } = "";
        public int EpisodeCount { get; set; }
        public string SortKey { get; set; } = "";
    }

    private sealed class SeriesDetailView
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleKana { get; set; } = "";
        public string TitleEn { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Period { get; set; } = "";
        public string Episodes { get; set; } = "";
        public string RunTimeSeconds { get; set; } = "";
        public string ToeiAnimOfficialSiteUrl { get; set; } = "";
        public string ToeiAnimLineupUrl { get; set; } = "";
        public string AbcOfficialSiteUrl { get; set; } = "";
        public string AmazonPrimeDistributionUrl { get; set; } = "";
        public bool HasExternalUrls { get; set; }
        public bool HasEpisodeList { get; set; }
    }

    private sealed class EpisodeIndexRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
    }

    private sealed class EpisodeStaffSummary
    {
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
    }
}
