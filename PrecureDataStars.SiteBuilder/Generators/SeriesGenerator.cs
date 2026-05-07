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

    // ── 役職マスタ・name 解決の共通キャッシュ（複数シリーズ間で使い回す） ──
    private IReadOnlyDictionary<string, Role>? _roleMap;
    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();

    public SeriesGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;

        _creditsRepo = new CreditsRepository(factory);
        _staffCardsRepo = new CreditCardsRepository(factory);
        _staffTiersRepo = new CreditCardTiersRepository(factory);
        _staffGroupsRepo = new CreditCardGroupsRepository(factory);
        _staffCardRolesRepo = new CreditCardRolesRepository(factory);
        _staffBlocksRepo = new CreditRoleBlocksRepository(factory);
        _staffEntriesRepo = new CreditBlockEntriesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
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

        var content = new SeriesDetailModel
        {
            Series = seriesView,
            Episodes = epRows,
            Related = related,
            Parent = parent
        };

        var layout = new LayoutModel
        {
            PageTitle = s.Title,
            MetaDescription = $"{s.Title} のエピソード・スタッフ・楽曲情報。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "シリーズ一覧", Url = "/series/" },
                new BreadcrumbItem { Label = s.Title, Url = "" }
            }
        };

        _page.RenderAndWrite(PathUtil.SeriesUrl(s.Slug), "series", "series-detail.sbn", content, layout);
    }

    /// <summary>
    /// 指定エピソードのクレジット階層から、脚本・絵コンテ・演出・作画監督・美術の人物名（連名対応）を引く。
    /// 各役職に該当する役職コード／日本語表示名のいずれかにマッチした役職配下の PERSON エントリを抽出。
    /// 結果は人物名のリストを「、」で連結した 1 文字列にして返す。
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
                                    string? name = await ResolveStaffEntryNameAsync(e, ct).ConfigureAwait(false);
                                    if (string.IsNullOrEmpty(name)) continue;
                                    switch (bucket)
                                    {
                                        case 1: if (seenSc.Add(name)) screenplay.Add(name); break;
                                        case 2: if (seenSb.Add(name)) storyboard.Add(name); break;
                                        case 3: if (seenDr.Add(name)) director.Add(name); break;
                                        case 4: if (seenAd.Add(name)) animDirector.Add(name); break;
                                        case 5: if (seenAt.Add(name)) artDirector.Add(name); break;
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
    /// PERSON / TEXT エントリから人物名を取り出す。それ以外（CHARACTER_VOICE / COMPANY / LOGO）は null。
    /// 所属（屋号）は付与しない。
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
