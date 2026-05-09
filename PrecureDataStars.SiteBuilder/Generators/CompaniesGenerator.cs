
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 企業索引（<c>/companies/</c>）と企業詳細（<c>/companies/{company_id}/</c>）の生成。
/// <para>
/// 企業詳細は、当該企業に紐付く<b>すべての屋号（company_aliases）と、その配下のロゴ（logos）</b>
/// 経由で関与情報を集める。具体的には以下の 3 ルートを合算:
/// </para>
/// <list type="bullet">
///   <item><description><c>credit_block_entries.company_alias_id</c>（COMPANY エントリ）</description></item>
///   <item><description><c>credit_role_blocks.leading_company_alias_id</c>（ブロック先頭の屋号）</description></item>
///   <item><description><c>credit_block_entries.logo_id</c> → <c>logos.company_alias_id</c>（LOGO エントリ）</description></item>
/// </list>
/// </summary>
public sealed class CompaniesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _aliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly RolesRepository _rolesRepo;

    private readonly CreditInvolvementIndex _index;

    private IReadOnlyDictionary<string, Role>? _roleMap;

    public CompaniesGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _companiesRepo = new CompaniesRepository(factory);
        _aliasesRepo = new CompanyAliasesRepository(factory);
        _logosRepo = new LogosRepository(factory);
        _rolesRepo = new RolesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating companies");

        var companies = await _companiesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var allAliases = await _aliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var aliasesByCompany = allAliases
            .GroupBy(a => a.CompanyId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var aliasById = allAliases.ToDictionary(a => a.AliasId);

        // ロゴは alias 単位なので、company_alias_id でグルーピングしておく。
        var allLogos = await _logosRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var logosByAlias = allLogos
            .GroupBy(l => l.CompanyAliasId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Logo>)g.ToList());

        if (_roleMap is null)
        {
            var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _roleMap = allRoles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        }

        // 索引ページの行を組み立てる。
        // v1.3.0 ブラッシュアップ続編：
        //   - 主表示は企業の正式名称（companies.name）に変更。代表ブランド名（最新の company_alias.name）
        //     は副表示（テンプレ側で括弧書き）として別プロパティに分離。
        //   - 一覧の kana ソートと表示「読み」列のソースは企業名の読み（companies.name_kana）に統一。
        //     旧仕様（最新ブランドの読み）を採用していた場合に発生する「ブランド名は変わるけど企業名は同じ」
        //     ケースでの並び順の揺れを抑える狙い。
        var indexRows = new List<CompanyIndexRow>(companies.Count);
        foreach (var co in companies)
        {
            var aliases = aliasesByCompany.TryGetValue(co.CompanyId, out var lst) ? lst : new List<CompanyAlias>();

            // 代表ブランド: 最新の（successor が無い）alias、無ければ先頭。
            // 紐付くブランドが 1 つも無い企業もありうる（マスタ準備中など）。
            var current = aliases.FirstOrDefault(a => a.SuccessorAliasId is null) ?? aliases.FirstOrDefault();
            string brandName = current?.Name ?? "";
            string companyName = co.Name;
            string companyKana = co.NameKana ?? "";

            // 関与エピソード数 = 当該会社の全 alias と、配下ロゴの合算（unique episode）。
            int episodeCount = CollectAllInvolvements(aliases, logosByAlias)
                .Where(i => i.EpisodeId.HasValue)
                .Select(i => i.EpisodeId!.Value)
                .Distinct()
                .Count();

            indexRows.Add(new CompanyIndexRow
            {
                CompanyId = co.CompanyId,
                CompanyName = companyName,
                BrandName = brandName,
                // DisplayName は v1.3.0 までの API 互換用（旧 search-index などで参照していた可能性に備える）。
                // 新テンプレでは CompanyName / BrandName を使う。
                DisplayName = companyName,
                DisplayKana = companyKana,
                EpisodeCount = episodeCount,
                HasInvolvement = episodeCount > 0
            });
        }

        // 50 音順（kana 昇順）。kana 空は末尾。
        // v1.3.0 ブラッシュアップ続編：「株式会社」系の前置詞をスキップして実体名で並べるため
        // CompanyKanaNormalizer.Comparer を採用。例「株式会社サンライズ」は「さ」のセクションへ。
        indexRows = indexRows
            .OrderBy(r => string.IsNullOrEmpty(r.DisplayKana) ? 1 : 0)
            .ThenBy(r => r.DisplayKana, CompanyKanaNormalizer.Comparer)
            .ThenBy(r => r.CompanyName, StringComparer.Ordinal)
            .ToList();

        var indexContent = new CompanyIndexModel
        {
            Companies = indexRows,
            TotalCount = indexRows.Count,
            ActiveCount = indexRows.Count(r => r.HasInvolvement)
        };
        var indexLayout = new LayoutModel
        {
            PageTitle = "企業一覧",
            MetaDescription = "プリキュアシリーズに関わった企業の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "企業", Url = "" }
            }
        };
        _page.RenderAndWrite("/companies/", "companies", "companies-index.sbn", indexContent, indexLayout);

        // 詳細ページ。
        foreach (var co in companies)
        {
            var aliases = aliasesByCompany.TryGetValue(co.CompanyId, out var lst) ? lst : new List<CompanyAlias>();
            await GenerateDetailAsync(co, aliases, logosByAlias, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"companies: {companies.Count + 1} ページ");
    }

    private async Task GenerateDetailAsync(
        Company company,
        IReadOnlyList<CompanyAlias> aliases,
        IReadOnlyDictionary<int, IReadOnlyList<Logo>> logosByAlias,
        CancellationToken ct)
    {
        // 代表屋号
        var current = aliases.FirstOrDefault(a => a.SuccessorAliasId is null) ?? aliases.FirstOrDefault();
        string displayName = current?.Name ?? company.Name;

        // 屋号一覧（時系列順）。
        var aliasViews = OrderCompanyAliasesChronologically(aliases, logosByAlias);

        // 全関与（alias 経由 + ロゴ経由）を集めて役職別グループ化。
        var allInvolvements = CollectAllInvolvements(aliases, logosByAlias).ToList();
        var groups = BuildCompanyInvolvementGroups(allInvolvements);

        var content = new CompanyDetailModel
        {
            Company = new CompanyView
            {
                CompanyId = company.CompanyId,
                DisplayName = displayName,
                Name = company.Name,
                NameKana = company.NameKana ?? "",
                NameEn = company.NameEn ?? "",
                FoundedDate = FormatDate(company.FoundedDate),
                DissolvedDate = FormatDate(company.DissolvedDate),
                Notes = company.Notes ?? ""
            },
            Aliases = aliasViews,
            InvolvementGroups = groups
        };
        // 企業詳細の構造化データは Schema.org の Organization 型。
        // alternateName に屋号（alias.name）を配列で並べる。foundingDate / dissolutionDate は持っていれば埋め込む。
        string baseUrl = _ctx.Config.BaseUrl;
        string companyUrl = PathUtil.CompanyUrl(company.CompanyId);
        var alternateNames = aliasViews
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !string.Equals(n, company.Name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["name"] = displayName
        };
        if (alternateNames.Count > 0) jsonLdDict["alternateName"] = alternateNames;
        if (company.FoundedDate.HasValue) jsonLdDict["foundingDate"] = company.FoundedDate.Value.ToString("yyyy-MM-dd");
        if (company.DissolvedDate.HasValue) jsonLdDict["dissolutionDate"] = company.DissolvedDate.Value.ToString("yyyy-MM-dd");
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + companyUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = displayName,
            MetaDescription = $"{displayName} のプリキュア関連クレジット一覧。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "企業", Url = "/companies/" },
                new BreadcrumbItem { Label = displayName, Url = "" }
            },
            // 企業ページは website 寄り（プロフィール的でもあるが OGP profile は人物用なので使わない）。
            OgType = "website",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite(
            companyUrl,
            "companies",
            "companies-detail.sbn",
            content,
            layout);

        await Task.CompletedTask;
    }

    /// <summary>
    /// 企業に紐付く全 alias の関与（直接 + leading）+ 配下ロゴの関与（LOGO エントリ）を 1 シーケンスに合算。
    /// </summary>
    private IEnumerable<Involvement> CollectAllInvolvements(
        IReadOnlyList<CompanyAlias> aliases,
        IReadOnlyDictionary<int, IReadOnlyList<Logo>> logosByAlias)
    {
        foreach (var a in aliases)
        {
            if (_index.ByCompanyAlias.TryGetValue(a.AliasId, out var direct))
            {
                foreach (var inv in direct) yield return inv;
            }

            // 配下ロゴ経由
            if (logosByAlias.TryGetValue(a.AliasId, out var logos))
            {
                foreach (var lg in logos)
                {
                    if (_index.ByLogo.TryGetValue(lg.LogoId, out var logoInvs))
                    {
                        foreach (var inv in logoInvs) yield return inv;
                    }
                }
            }
        }
    }

    /// <summary>会社の全関与を役職別にグルーピング。</summary>
    /// <summary>
    /// 企業・団体に紐付く関与情報を、役職別 → シリーズ単位の話数圧縮表記に編成する
    /// （v1.3.0 ブラッシュアップ続編で大幅変更）。
    /// 旧仕様：役職別 → エピソードごと 1 行のテーブル形式。
    /// 新仕様：役職別 → シリーズ単位 1 行 + 話数を「#1〜4, 8」のように圧縮表示。
    /// 全話担当のときは「(全話)」マークを付加。シリーズ全体スコープは別行として残す。
    /// 企業・団体に声優役は通常存在しないので CharacterNames は常に空。
    /// </summary>
    private IReadOnlyList<InvolvementGroup> BuildCompanyInvolvementGroups(IReadOnlyList<Involvement> all)
    {
        if (all.Count == 0) return Array.Empty<InvolvementGroup>();

        int RoleOrder(string code)
        {
            if (string.IsNullOrEmpty(code)) return int.MaxValue;
            if (_roleMap!.TryGetValue(code, out var r) && r.DisplayOrder is ushort d) return d;
            return int.MaxValue - 1;
        }

        var groups = new List<InvolvementGroup>();
        foreach (var roleGroup in all.GroupBy(i => i.RoleCode).OrderBy(g => RoleOrder(g.Key)))
        {
            string roleCode = roleGroup.Key;
            string roleLabel = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                : (_roleMap!.TryGetValue(roleCode, out var r) ? (r.NameJa ?? roleCode) : roleCode);

            var seriesRows = new List<InvolvementSeriesRow>();
            int episodeCountTotal = 0;

            foreach (var bySeries in roleGroup
                .GroupBy(i => i.SeriesId)
                .OrderBy(sg => SeriesStartDate(sg.Key)))
            {
                if (!_ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;

                var episodeNos = new HashSet<int>();
                bool hasSeriesScope = false;
                foreach (var inv in bySeries)
                {
                    if (inv.EpisodeId is int eid)
                    {
                        var ep = LookupEpisode(bySeries.Key, eid);
                        if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
                    }
                    else
                    {
                        hasSeriesScope = true;
                    }
                }

                var allSeriesEpNos = _ctx.EpisodesBySeries.TryGetValue(bySeries.Key, out var allEps)
                    ? allEps.Select(e => e.SeriesEpNo).ToList()
                    : new List<int>();

                if (hasSeriesScope)
                {
                    seriesRows.Add(new InvolvementSeriesRow
                    {
                        SeriesSlug = series.Slug,
                        SeriesTitle = series.Title,
                        RangeLabel = "（シリーズ全体）",
                        IsAllEpisodes = false,
                        CharacterNames = ""
                    });
                }

                if (episodeNos.Count > 0)
                {
                    bool isAll = allSeriesEpNos.Count > 0
                        && episodeNos.SetEquals(allSeriesEpNos);
                    string rangeLabel = isAll
                        ? string.Empty
                        : EpisodeRangeCompressor.Compress(episodeNos);

                    seriesRows.Add(new InvolvementSeriesRow
                    {
                        SeriesSlug = series.Slug,
                        SeriesTitle = series.Title,
                        RangeLabel = rangeLabel,
                        IsAllEpisodes = isAll,
                        CharacterNames = ""
                    });

                    episodeCountTotal += episodeNos.Count;
                }
            }

            if (seriesRows.Count == 0) continue;

            groups.Add(new InvolvementGroup
            {
                RoleCode = roleCode,
                RoleLabel = roleLabel,
                SeriesRows = seriesRows,
                Count = episodeCountTotal,
                HasCharacterColumn = false
            });
        }
        return groups;
    }

    /// <summary>
    /// 企業の屋号（alias）を時系列順に並べ、各 alias 配下のロゴリストも添える。
    /// </summary>
    private static IReadOnlyList<CompanyAliasView> OrderCompanyAliasesChronologically(
        IReadOnlyList<CompanyAlias> aliases,
        IReadOnlyDictionary<int, IReadOnlyList<Logo>> logosByAlias)
    {
        if (aliases.Count == 0) return Array.Empty<CompanyAliasView>();

        var byId = aliases.ToDictionary(a => a.AliasId);
        var visited = new HashSet<int>();
        var ordered = new List<CompanyAlias>();

        foreach (var head in aliases.Where(a => a.PredecessorAliasId is null
            || !byId.ContainsKey(a.PredecessorAliasId!.Value)))
        {
            var cur = head;
            while (cur is not null && visited.Add(cur.AliasId))
            {
                ordered.Add(cur);
                if (cur.SuccessorAliasId is int nextId && byId.TryGetValue(nextId, out var next)) cur = next;
                else cur = null;
            }
        }
        foreach (var a in aliases) if (visited.Add(a.AliasId)) ordered.Add(a);

        return ordered.Select(a => new CompanyAliasView
        {
            AliasId = a.AliasId,
            Name = a.Name,
            NameKana = a.NameKana ?? "",
            NameEn = a.NameEn ?? "",
            ValidFrom = FormatDate(a.ValidFrom),
            ValidTo = FormatDate(a.ValidTo),
            Notes = a.Notes ?? "",
            Logos = (logosByAlias.TryGetValue(a.AliasId, out var lst) ? lst : Array.Empty<Logo>())
                .OrderBy(l => l.ValidFrom ?? DateTime.MinValue)
                .Select(l => new LogoView
                {
                    LogoId = l.LogoId,
                    CiVersionLabel = l.CiVersionLabel,
                    ValidFrom = l.ValidFrom?.ToString("yyyy.M.d") ?? "",
                    ValidTo = l.ValidTo?.ToString("yyyy.M.d") ?? "",
                    Description = l.Description ?? ""
                })
                .ToList()
        }).ToList();
    }

    private DateOnly SeriesStartDate(int seriesId)
        => _ctx.SeriesById.TryGetValue(seriesId, out var s) ? s.StartDate : DateOnly.MaxValue;

    private int EpisodeSeriesEpNo(int seriesId, int episodeId)
    {
        if (episodeId == 0) return -1;
        var ep = LookupEpisode(seriesId, episodeId);
        return ep?.SeriesEpNo ?? int.MaxValue;
    }

    private Episode? LookupEpisode(int seriesId, int episodeId)
    {
        if (!_ctx.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        for (int i = 0; i < eps.Count; i++)
            if (eps[i].EpisodeId == episodeId) return eps[i];
        return null;
    }

    private static string FormatDate(DateOnly? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    /// <summary>DateTime? 版（CompanyAlias.ValidFrom/ValidTo / Company.FoundedDate などが DateTime?）。</summary>
    private static string FormatDate(DateTime? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    // ─── テンプレ用 DTO 群 ───

    private sealed class CompanyIndexModel
    {
        public IReadOnlyList<CompanyIndexRow> Companies { get; set; } = Array.Empty<CompanyIndexRow>();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
    }

    private sealed class CompanyIndexRow
    {
        public int CompanyId { get; set; }
        /// <summary>
        /// 主表示用の企業正式名称（companies.name）。
        /// v1.3.0 ブラッシュアップ続編で追加。
        /// </summary>
        public string CompanyName { get; set; } = "";
        /// <summary>
        /// 副表示用の代表ブランド名（最新の company_aliases.name、無ければ空文字）。
        /// CompanyName と一致するときはテンプレ側で表示を抑制する想定。
        /// v1.3.0 ブラッシュアップ続編で追加。
        /// </summary>
        public string BrandName { get; set; } = "";
        /// <summary>
        /// 旧 API 互換のための表示名（現状は CompanyName と同値）。
        /// v1.3.0 以降の新テンプレは <see cref="CompanyName"/> / <see cref="BrandName"/> を直接使う。
        /// </summary>
        public string DisplayName { get; set; } = "";
        public string DisplayKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public bool HasInvolvement { get; set; }
    }

    private sealed class CompanyDetailModel
    {
        public CompanyView Company { get; set; } = new();
        public IReadOnlyList<CompanyAliasView> Aliases { get; set; } = Array.Empty<CompanyAliasView>();
        public IReadOnlyList<InvolvementGroup> InvolvementGroups { get; set; } = Array.Empty<InvolvementGroup>();
    }

    private sealed class CompanyView
    {
        public int CompanyId { get; set; }
        public string DisplayName { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string FoundedDate { get; set; } = "";
        public string DissolvedDate { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class CompanyAliasView
    {
        public int AliasId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string ValidFrom { get; set; } = "";
        public string ValidTo { get; set; } = "";
        public string Notes { get; set; } = "";
        public IReadOnlyList<LogoView> Logos { get; set; } = Array.Empty<LogoView>();
    }

    private sealed class LogoView
    {
        public int LogoId { get; set; }
        public string CiVersionLabel { get; set; } = "";
        public string ValidFrom { get; set; } = "";
        public string ValidTo { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
