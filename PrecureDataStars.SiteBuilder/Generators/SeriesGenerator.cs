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
/// 一覧の見せ方は、TV シリーズを縦軸とした年表構造にする：
/// </para>
/// <list type="bullet">
///   <item><description>各 TV シリーズを 1 行とし、その配下に <c>parent_series_id</c> で
///     紐づく秋映画（MOVIE）・秋映画併映短編（MOVIE_SHORT）・春映画（SPRING）を字下げ表示。</description></item>
///   <item><description>SPIN-OFF（スピンオフ）は別セクションで独立表示。</description></item>
///   <item><description>TV / SPIN-OFF 以外の種別（MOVIE 系）は単独で表示しない。
///     必ず親 TV シリーズの配下にぶら下げる。親が無い場合のみ「未分類」セクションへ。</description></item>
/// </list>
/// <para>
/// 個別シリーズページのエピソード一覧は、<see cref="SeriesKind.CreditAttachTo"/>
/// が <c>EPISODE</c>（= TV または SPIN-OFF）のときだけ表示する。
/// 映画系（<c>SERIES</c> アタッチ）はエピソードを持たないので、エピソード一覧見出し自体を出さない。
/// 表には脚本・絵コンテ・演出・作画監督・美術カラムも含め、各話のスタッフを一覧できるようにする。
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

    // ── 劇伴情報（v1.3.0 追加：シリーズ詳細ページに劇伴一覧表を付与するため） ──
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly BgmSessionsRepository _bgmSessionsRepo;
    // ── 劇伴使用箇所の集計用（v1.3.0 後半追加：シリーズ詳細の劇伴一覧表に「使用回数」列を出すため） ──
    private readonly EpisodeUsesRepository _episodeUsesRepo;

    // ── スタッフ名リンク化（v1.3.0 追加：エピソード一覧表の脚本・絵コンテ・演出・作監・美術カラムに人物詳細リンクを張る） ──
    private readonly StaffNameLinkResolver _staffLinkResolver;

    // ── 主要スタッフ表（v1.3.0 追加：シリーズ詳細ページに「主要スタッフ」セクションを付与するため）。
    //    CreditInvolvementIndex を再利用して人物ごとの担当エピソード数を当該シリーズに絞って集計する。 ──
    private readonly CreditInvolvementIndex _involvementIndex;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;

    // ── 役職マスタ・name 解決の共通キャッシュ（複数シリーズ間で使い回す） ──
    private IReadOnlyDictionary<string, Role>? _roleMap;
    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();

    // ── 主要スタッフ表で使う「人物ID → 紐付き全 alias_id 群」キャッシュ（複数シリーズで使い回す） ──
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasIdsByPersonIdCache;
    private IReadOnlyList<Person>? _allPersonsCache;
    private IReadOnlyDictionary<string, SeriesKind>? _seriesKindMapCache;
    // ── 劇伴使用回数集計用キャッシュ（v1.3.0 後半追加）。
    //    全 episode_uses の BGM 行を 1 度だけ読み込んで (series_id, m_no_detail) 単位の使用回数辞書を作る。 ──
    private IReadOnlyDictionary<(int seriesId, string mNoDetail), int>? _bgmUseCountCache;

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
        _bgmCuesRepo = new BgmCuesRepository(factory);
        _bgmSessionsRepo = new BgmSessionsRepository(factory);
        _episodeUsesRepo = new EpisodeUsesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _seriesKindsRepo = new SeriesKindsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating series");

        GenerateIndex();

        foreach (var s in _ctx.Series)
            await GenerateDetailAsync(s, ct).ConfigureAwait(false);

        _ctx.Logger.Success($"series: {_ctx.Series.Count + 1} ページ");
    }

    /// <summary>
    /// <c>/series/</c> の索引ページ。TV シリーズを縦軸に、配下の映画系を字下げで併記する。
    /// 同期メソッドのまま残す（DB クエリを発行しないため）。
    /// </summary>
    private void GenerateIndex()
    {
        // TV シリーズだけを抽出（年代順）
        var tvSeries = _ctx.Series
            .Where(s => s.KindCode == "TV")
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .ToList();

        // TV ごとに配下のシリーズを集める。配下とは parent_series_id == TV.SeriesId のもの。
        var childrenByTv = _ctx.Series
            .Where(s => s.ParentSeriesId.HasValue && s.KindCode != "SPIN-OFF")
            .GroupBy(s => s.ParentSeriesId!.Value)
            .ToDictionary(g => g.Key, g => g
                .OrderBy(c => c.StartDate)
                .ThenBy(c => c.SeqInParent ?? (byte)0)
                .ToList());

        // TV 行を組み立て。
        // ガタつきを抑えるために、Title / Period / EpisodesLabel をそれぞれ独立の TD に並べる
        // テーブル構造で持つ。略称は表示しない（テンプレ側でも参照しない）。
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
                    Period = FormatPeriod(c.StartDate, c.EndDate)
                })
                .ToList()
        }).ToList();

        // スピンオフは独立表示。
        var spinOffRows = _ctx.Series
            .Where(s => s.KindCode == "SPIN-OFF")
            .OrderBy(s => s.StartDate)
            .Select(s => new RelatedSeriesRow
            {
                Slug = s.Slug,
                Title = s.Title,
                KindLabel = LookupKindLabel(s.KindCode),
                Period = FormatPeriod(s.StartDate, s.EndDate)
            })
            .ToList();

        // 親無し映画系（万一）は「その他」セクションに。
        var orphanRows = _ctx.Series
            .Where(s => s.KindCode != "TV"
                     && s.KindCode != "SPIN-OFF"
                     && !s.ParentSeriesId.HasValue)
            .OrderBy(s => s.StartDate)
            .Select(s => new RelatedSeriesRow
            {
                Slug = s.Slug,
                Title = s.Title,
                KindLabel = LookupKindLabel(s.KindCode),
                Period = FormatPeriod(s.StartDate, s.EndDate)
            })
            .ToList();

        var content = new SeriesIndexModel
        {
            TvSeries = tvRows,
            SpinOffSeries = spinOffRows,
            OrphanSeries = orphanRows,
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
    /// エピソード一覧の有無は当該シリーズ種別の <c>credit_attach_to</c> が EPISODE か否かで決まる。
    /// 一覧表には脚本・絵コンテ・演出・作画監督・美術のスタッフカラムを含める。
    /// </summary>
    private async Task GenerateDetailAsync(Series s, CancellationToken ct)
    {
        // エピソード表示の可否。EPISODE アタッチ（TV / SPIN-OFF）のときのみ一覧を出す。
        bool hasEpisodes = false;
        if (_ctx.SeriesKindByCode.TryGetValue(s.KindCode, out var kind))
        {
            hasEpisodes = string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal);
        }

        var eps = _ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var list) ? list : Array.Empty<Episode>();
        var epRows = new List<EpisodeIndexRow>();
        if (hasEpisodes)
        {
            // 役職マスタは初回ロード時に 1 回だけ引いてキャッシュ。
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
                    // エピソード一覧では時刻を出さず日付のみ。
                    OnAirDate = FormatJpDate(e.OnAirAt),
                    Screenplay = staff.Screenplay,
                    Storyboard = staff.Storyboard,
                    EpisodeDirector = staff.EpisodeDirector,
                    AnimationDirector = staff.AnimationDirector,
                    ArtDirector = staff.ArtDirector
                });
            }
        }

        // 関連シリーズ（自分が親で、配下の映画 / 短編 / 春映画 / スピンオフ）
        var related = _ctx.Series
            .Where(x => x.ParentSeriesId == s.SeriesId)
            .OrderBy(x => x.StartDate)
            .Select(x => new RelatedSeriesRow
            {
                Slug = x.Slug,
                Title = x.Title,
                KindLabel = LookupKindLabel(x.KindCode),
                Period = FormatPeriod(x.StartDate, x.EndDate)
            })
            .ToList();

        // 親シリーズへのリンク（自分が映画やスピンオフだった場合）
        RelatedSeriesRow? parent = null;
        if (s.ParentSeriesId is int pid && _ctx.SeriesById.TryGetValue(pid, out var p))
        {
            parent = new RelatedSeriesRow
            {
                Slug = p.Slug,
                Title = p.Title,
                KindLabel = LookupKindLabel(p.KindCode),
                Period = FormatPeriod(p.StartDate, p.EndDate)
            };
        }

        // シリーズ表示用 DTO。略称は持たせない（基本情報テーブルから撤去）。
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

        // 主要スタッフ表（v1.3.0 追加）。CreditInvolvementIndex を逆引きして、当該シリーズで担当エピソード数の多い
        // 人物を役職別にリストアップする。クレジットがエピソード単位で付与されるシリーズ（series_kinds.credit_attach_to=EPISODE）
        // のみ対象。SERIES 単位で付与されるシリーズ（映画やスピンオフの一部）はそもそもエピソード単位の集計が成立しない。
        var keyStaffSections = await BuildKeyStaffSectionsAsync(s, ct).ConfigureAwait(false);

        // 劇伴一覧（v1.3.0 追加）。当該シリーズに紐付く全 bgm_cues + 録音セッションを引き、
        // session_no 昇順 → m_no_class 昇順 → m_no_detail 昇順で並べる。
        // 仮 M 番号フラグ（is_temp_m_no=1）の行は番号表示を「(番号不明)」に差し替える。
        // v1.3.0 後半で「使用回数」列を追加（episode_uses 集計）。当該シリーズだけでなく
        // 全シリーズのエピソードで使われた回数を表示する（春映画・秋映画で本編 BGM が流用されるケースに対応）。
        if (_bgmUseCountCache is null)
        {
            // 全 episode_uses をロードし、BGM 行だけを (series_id, m_no_detail) 単位で件数集計。
            var allUses = await _episodeUsesRepo.GetAllAsync(ct).ConfigureAwait(false);
            var counts = new Dictionary<(int, string), int>();
            foreach (var u in allUses)
            {
                if (u.BgmSeriesId is int bsid && !string.IsNullOrEmpty(u.BgmMNoDetail))
                {
                    var key = (bsid, u.BgmMNoDetail!);
                    counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
            _bgmUseCountCache = counts;
        }

        var bgmRows = new List<BgmCueRow>();
        var bgmSessions = await _bgmSessionsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var sessionMap = bgmSessions
            .Where(b => b.SeriesId == s.SeriesId)
            .ToDictionary(b => b.SessionNo, b => b.SessionName);
        var cues = await _bgmCuesRepo.GetBySeriesAsync(s.SeriesId, ct).ConfigureAwait(false);
        foreach (var cue in cues
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.SessionNo)
            .ThenBy(c => c.MNoClass, StringComparer.Ordinal)
            .ThenBy(c => c.MNoDetail, StringComparer.Ordinal))
        {
            int useCount = _bgmUseCountCache.TryGetValue((cue.SeriesId, cue.MNoDetail), out var uc) ? uc : 0;
            bgmRows.Add(new BgmCueRow
            {
                MNoLabel = cue.IsTempMNo ? "(番号不明)" : cue.MNoDetail,
                MenuTitle = cue.MenuTitle ?? "",
                ComposerName = cue.ComposerName ?? "",
                ArrangerName = cue.ArrangerName ?? "",
                LengthLabel = FormatBgmLength(cue.LengthSeconds),
                SessionLabel = sessionMap.TryGetValue(cue.SessionNo, out var sn) ? sn : "",
                UseCount = useCount
            });
        }

        var content = new SeriesDetailModel
        {
            Series = seriesView,
            Episodes = epRows,
            Related = related,
            Parent = parent,
            BgmCues = bgmRows,
            KeyStaffSections = keyStaffSections
        };

        // シリーズ詳細の構造化データは Schema.org の TVSeries 型。
        // numberOfEpisodes、startDate、endDate、inLanguage を主要プロパティとして埋め込む。
        // BaseUrl が空のときは url キーは省略（Dictionary に追加しない）。
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
            // 映画は video.movie、TV シリーズは video.tv_show として OGP に通知する。
            OgType = s.KindCode == "MOVIE" ? "video.movie" : "video.tv_show",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite(seriesUrl, "series", "series-detail.sbn", content, layout);
    }

    /// <summary>
    /// 指定エピソードのクレジット階層から、脚本・絵コンテ・演出・作画監督・美術の人物名（連名対応）を引く。
    /// 各役職に該当する役職コード／日本語表示名のいずれかにマッチした役職配下の PERSON エントリを抽出。
    /// 結果は人物名のリストを「、」で連結した HTML 文字列にして返す。PERSON エントリは
    /// <see cref="StaffNameLinkResolver"/> 経由で人物詳細ページへの &lt;a&gt; リンクに置き換わる。
    /// </summary>
    private async Task<EpisodeStaffSummary> ExtractStaffSummaryAsync(int episodeId, CancellationToken ct)
    {
        // 各バケツの「HTML 断片リスト」と、重複排除のための「正規化キー集合」。
        // 重複判定キーは PERSON エントリなら "P:{alias_id}"、TEXT エントリなら "T:{raw_text}"
        // とし、リンク化の有無に関わらず同一エントリを 1 度だけ表示するようにする。
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

        // 役職コード → ターゲット種別のマッピング。
        // role_code もしくは name_ja のいずれかに合致する役職を判定して仕分ける。
        // 1=脚本 / 2=絵コンテ / 3=演出 / 4=作画監督 / 5=美術
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
    /// <summary>
    /// PERSON / TEXT エントリから (重複判定キー, 表示用 HTML 文字列) を取り出す（v1.3.0 追加）。
    /// PERSON エントリは <see cref="StaffNameLinkResolver"/> 経由で人物詳細ページへの &lt;a&gt; リンク HTML に
    /// 変換し、TEXT エントリは HTML エスケープのみ施したプレーンテキストにする。
    /// 重複判定キーは PERSON なら <c>"P:{alias_id}"</c>、TEXT なら <c>"T:{raw_text}"</c>、
    /// それ以外（CHARACTER_VOICE / COMPANY / LOGO）は空文字 + 空 HTML を返して呼び出し元で除外する。
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
                    // テキストエントリは LinkResolver には渡さず、HTML エスケープだけ施す。
                    string html = _staffLinkResolver.ResolveAsHtml(null, raw);
                    return ($"T:{raw}", html);
                }
            default:
                return ("", "");
        }
    }

    /// <summary>
    /// PERSON / TEXT エントリから人物名を取り出す（プレーンテキスト版、v1.2 系から残存）。
    /// 現状は本ファイル内では参照されないが、将来別文脈での利用を想定して保持。
    /// 所属（屋号）は付与しない。それ以外（CHARACTER_VOICE / COMPANY / LOGO）は null。
    /// </summary>
    private async Task<string?> ResolveStaffEntryNameAsync(CreditBlockEntry e, CancellationToken ct)
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
                    return pa?.DisplayTextOverride ?? pa?.Name;
                }
                return null;
            case "TEXT":
                return e.RawText;
            default:
                return null;
        }
    }

    /// <summary>kind_code → 表示用ラベル（name_ja）。マスタ未登録時はコードをそのまま返す。</summary>
    private string LookupKindLabel(string code)
        => _ctx.SeriesKindByCode.TryGetValue(code, out var kind) ? kind.NameJa : code;

    /// <summary>放送・公開期間を日本語フォーマット「2004年2月1日 〜 2005年1月30日」で返す。</summary>
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

    /// <summary>放送日を「2004年2月1日」フォーマットで返す（時刻なし、シリーズ詳細のエピソード一覧用）。</summary>
    private static string FormatJpDate(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    /// <summary>劇伴の尺（秒）を「m:ss」表記に整形。NULL は空文字。</summary>
    private static string FormatBgmLength(ushort? seconds)
    {
        if (!seconds.HasValue) return "";
        ushort s = seconds.Value;
        int min = s / 60;
        int sec = s % 60;
        return $"{min}:{sec:00}";
    }

    /// <summary>
    /// 主要スタッフ表のサブセクション群を構築する（v1.3.0 追加）。
    /// <para>
    /// CreditInvolvementIndex を逆引きして、当該シリーズで担当エピソード数の多い人物を役職別にリストアップする。
    /// クレジットがエピソード単位で付与されるシリーズ（series_kinds.credit_attach_to=EPISODE）のみ対象とし、
    /// SERIES 単位で付与されるシリーズはエピソード単位の集計が成立しないため空配列を返す。
    /// </para>
    /// <para>
    /// 役職と担当しきい値（しきい値以上のエピソード担当があれば掲載）：
    /// </para>
    /// <list type="bullet">
    ///   <item><description>シリーズ構成 (SERIES_COMPOSITION)：1 話以上（人数が少ないので全員主要扱い）</description></item>
    ///   <item><description>脚本 (SCREENPLAY)：2 話以上（ゲスト枠の脚本家を除外して主要だけ）</description></item>
    ///   <item><description>絵コンテ (STORYBOARD)：2 話以上</description></item>
    ///   <item><description>演出 (EPISODE_DIRECTOR)：2 話以上</description></item>
    ///   <item><description>作画監督 (ANIMATION_DIRECTOR)：2 話以上</description></item>
    ///   <item><description>美術監督 (ART_DIRECTOR)：1 話以上</description></item>
    /// </list>
    /// <para>
    /// 集計の単位は (PersonId × RoleCode × EpisodeId) で、同一エピソードに OP / ED 両方クレジットされていても 1 回扱い。
    /// fix12 で実装した役職別ランキングと同じ集計方針。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<KeyStaffSection>> BuildKeyStaffSectionsAsync(Series series, CancellationToken ct)
    {
        // クレジット付与単位の判定。EPISODE でないなら主要スタッフ表は出さない。
        _seriesKindMapCache ??= (await _seriesKindsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        if (!_seriesKindMapCache.TryGetValue(series.KindCode, out var kind)) return Array.Empty<KeyStaffSection>();
        if (!string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal)) return Array.Empty<KeyStaffSection>();

        // 人物マスタと person_alias_persons を初回だけロード（複数シリーズ間で共有）。
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

        // 役職コード → (表示ラベル, しきい値)。表示順もこの順序で並べる。
        var roleSpecs = new (string Code, string Label, int MinEpisodes)[]
        {
            ("SERIES_COMPOSITION", "シリーズ構成", 1),
            ("SCREENPLAY",         "脚本",         2),
            ("STORYBOARD",         "絵コンテ",     2),
            ("EPISODE_DIRECTOR",   "演出",         2),
            ("ANIMATION_DIRECTOR", "作画監督",     2),
            ("ART_DIRECTOR",       "美術監督",     1)
        };

        var sections = new List<KeyStaffSection>();

        foreach (var spec in roleSpecs)
        {
            var rows = new List<KeyStaffRow>();
            foreach (var p in _allPersonsCache)
            {
                if (!_aliasIdsByPersonIdCache.TryGetValue(p.PersonId, out var aliasIds)) continue;

                // (EpisodeId) ユニーク集合 = 当該役職での担当エピソード数。
                // CreditInvolvementIndex は SeriesId / EpisodeId / RoleCode を直接保持しているのでフィルタは O(N)。
                var episodeKeys = new HashSet<int>();
                foreach (var aid in aliasIds)
                {
                    if (!_involvementIndex.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                    foreach (var inv in invs)
                    {
                        if (inv.SeriesId != series.SeriesId) continue;
                        if (!string.Equals(inv.RoleCode, spec.Code, StringComparison.Ordinal)) continue;
                        if (inv.EpisodeId is int eid) episodeKeys.Add(eid);
                    }
                }
                if (episodeKeys.Count < spec.MinEpisodes) continue;

                rows.Add(new KeyStaffRow
                {
                    PersonId = p.PersonId,
                    FullName = p.FullName,
                    EpisodeCount = episodeKeys.Count
                });
            }

            if (rows.Count == 0) continue;

            // 担当エピソード数降順 → よみ昇順 → 名前昇順で安定化。
            rows.Sort((a, b) =>
            {
                int c = b.EpisodeCount.CompareTo(a.EpisodeCount);
                if (c != 0) return c;
                // よみは Person マスタを引き直す手間を避け、名前のみで補助ソート（数の多い順が主目的なので十分）。
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
        public IReadOnlyList<RelatedSeriesRow> SpinOffSeries { get; set; } = Array.Empty<RelatedSeriesRow>();
        public IReadOnlyList<RelatedSeriesRow> OrphanSeries { get; set; } = Array.Empty<RelatedSeriesRow>();
        public int TotalCount { get; set; }
    }

    /// <summary>TV シリーズ 1 件分（配下の映画／短編／春映画のリストを含む）。略称は持たせない。</summary>
    private sealed class TvSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Period { get; set; } = "";
        public string EpisodesLabel { get; set; } = "";
        public IReadOnlyList<RelatedSeriesRow> Children { get; set; } = Array.Empty<RelatedSeriesRow>();
    }

    /// <summary>シリーズ参照行（種別ラベルと期間付き）。</summary>
    private sealed class RelatedSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Period { get; set; } = "";
    }

    private sealed class SeriesDetailModel
    {
        public SeriesDetailView Series { get; set; } = new();
        public IReadOnlyList<EpisodeIndexRow> Episodes { get; set; } = Array.Empty<EpisodeIndexRow>();
        public IReadOnlyList<RelatedSeriesRow> Related { get; set; } = Array.Empty<RelatedSeriesRow>();
        public RelatedSeriesRow? Parent { get; set; }
        /// <summary>当該シリーズの劇伴一覧（v1.3.0 追加）。空のときはテンプレ側で「劇伴」セクション自体を非表示にする。</summary>
        public IReadOnlyList<BgmCueRow> BgmCues { get; set; } = Array.Empty<BgmCueRow>();
        /// <summary>
        /// 主要スタッフ表（v1.3.0 追加）。役職ごとにサブセクション化された配列で、各サブセクションには
        /// 当該シリーズの担当エピソード数が一定以上の人物を担当数降順で並べる。空のときはセクション自体を
        /// テンプレ側で非表示にする（クレジットがエピソード単位で付かないシリーズなど）。
        /// </summary>
        public IReadOnlyList<KeyStaffSection> KeyStaffSections { get; set; } = Array.Empty<KeyStaffSection>();
    }

    /// <summary>シリーズ詳細ページの劇伴一覧 1 行。</summary>
    private sealed class BgmCueRow
    {
        /// <summary>M 番号（仮 M 番号フラグが立っている行は「(番号不明)」に差し替え済み）。</summary>
        public string MNoLabel { get; set; } = "";
        public string MenuTitle { get; set; } = "";
        public string ComposerName { get; set; } = "";
        public string ArrangerName { get; set; } = "";
        public string LengthLabel { get; set; } = "";
        public string SessionLabel { get; set; } = "";
        /// <summary>
        /// この劇伴の使用回数（v1.3.0 後半追加）。<c>episode_uses</c> 全件から
        /// (series_id, m_no_detail) 一致行をカウントしたもの。0 のときはテンプレ側で「—」表示。
        /// 当該シリーズだけでなく全シリーズで使われた回数を含む（春映画・秋映画で本編 BGM が流用される
        /// ケースに対応するため）。
        /// </summary>
        public int UseCount { get; set; }
    }

    /// <summary>主要スタッフ表の役職別サブセクション 1 つ分。</summary>
    private sealed class KeyStaffSection
    {
        /// <summary>役職表示名（例: "脚本"、"絵コンテ"）。</summary>
        public string RoleLabel { get; set; } = "";
        /// <summary>このサブセクションに並ぶ人物行（担当エピソード数降順 → 名義よみ昇順 → 名義昇順）。</summary>
        public IReadOnlyList<KeyStaffRow> Members { get; set; } = Array.Empty<KeyStaffRow>();
    }

    /// <summary>主要スタッフ表の人物 1 行。</summary>
    private sealed class KeyStaffRow
    {
        public int PersonId { get; set; }
        public string FullName { get; set; } = "";
        public int EpisodeCount { get; set; }
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
        /// <summary>シリーズ詳細にエピソード一覧セクションを表示するか（credit_attach_to=EPISODE のみ true）。</summary>
        public bool HasEpisodeList { get; set; }
    }

    /// <summary>エピソード一覧 1 行。脚本・絵コンテ・演出・作画監督・美術カラム付き。</summary>
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

    /// <summary>エピソード単位のスタッフ抽出結果（5 役職分の人物名連結文字列）。</summary>
    private sealed class EpisodeStaffSummary
    {
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
    }
}
