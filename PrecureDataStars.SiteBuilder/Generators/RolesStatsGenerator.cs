using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 役職別ランキング（<c>/stats/roles/</c>・<c>/stats/roles/{role_code}/</c>）と、
/// 総合ランキング（<c>/stats/roles/all-persons/</c>・<c>/stats/roles/all-companies/</c>）の生成（v1.3.0 タスク追加）。
/// <para>
/// <see cref="CreditInvolvementIndex"/> の <c>ByPersonAlias</c> / <c>ByCompanyAlias</c> / <c>ByLogo</c> を
/// 人物・企業単位に集約してランキング化する。集計のキーは下記のとおり：
/// </para>
/// <list type="bullet">
///   <item><description>役職別ランキング：(PersonId × RoleCode × EpisodeId) で重複排除。
///     同一エピソードで同じ役職に OP / ED 両方クレジットされていても 1 回扱いとし、
///     credit_kind は集計キーに含めない（業務ルール）。</description></item>
///   <item><description>人物総合ランキング：(PersonId × EpisodeId) で重複排除。
///     同一エピソードで複数役職を兼任していても 1 回扱い。役職別の内訳を上位 5 件まで併記。</description></item>
///   <item><description>企業ランキング：上記の人物版と同じルールを CompanyId に適用。
///     COMPANY エントリ + LOGO エントリ + leading_company_alias_id の 3 ルートを合算。</description></item>
/// </list>
/// <para>
/// 順位は Wimbledon 形式（1, 2, 2, 4, ...）。同点の次の順位は同点者数だけスキップする。
/// </para>
/// <para>
/// VOICE_CAST 役職は本ジェネレータの集計対象から除外し、専用ページ <c>/stats/voice-cast/</c>
/// （<see cref="VoiceCastStatsGenerator"/> 担当）で別途扱う。
/// </para>
/// </summary>
public sealed class RolesStatsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;

    private readonly RolesRepository _rolesRepo;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;

    public RolesStatsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _rolesRepo = new RolesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _companiesRepo = new CompaniesRepository(factory);
        _companyAliasesRepo = new CompanyAliasesRepository(factory);
        _logosRepo = new LogosRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating role stats");

        // マスタ全件をロード。
        var allRoles = (await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCompanies = (await _companiesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCompanyAliases = (await _companyAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allLogos = (await _logosRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        // 人物と紐付く全 alias_id を集める（中間表 person_alias_persons を人物ごとに引く）。
        // 人物数 × ループ 1 回のクエリ。サイトビルダーは起動時 1 回限りの実行なので許容。
        var aliasIdsByPersonId = new Dictionary<int, List<int>>();
        foreach (var p in allPersons)
        {
            var rows = await _personAliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
            aliasIdsByPersonId[p.PersonId] = rows.Select(r => r.AliasId).ToList();
        }

        // 企業 → 屋号 → ロゴ の構造を辞書化。
        var companyAliasesByCompany = allCompanyAliases.GroupBy(a => a.CompanyId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AliasId).ToList());
        var logosByCompanyAlias = allLogos.GroupBy(l => l.CompanyAliasId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.LogoId).ToList());

        // 役職マスタから VOICE_CAST 区分を除外（別ページ）。残りの役職を表示順で並べる。
        var rankableRoles = allRoles
            .Where(r => !string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal))
            .OrderBy(r => r.DisplayOrder ?? ushort.MaxValue)
            .ThenBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToList();

        // ── 各役職について役職別ランキングページを生成 + 役職索引用のエントリも構築 ──
        var roleIndexEntries = new List<RoleIndexEntry>();
        foreach (var role in rankableRoles)
        {
            int personCount, companyCount;
            GenerateRoleDetail(role, aliasIdsByPersonId, allPersons, companyAliasesByCompany, logosByCompanyAlias, allCompanies, out personCount, out companyCount);
            roleIndexEntries.Add(new RoleIndexEntry
            {
                RoleCode = role.RoleCode,
                RoleNameJa = role.NameJa,
                RoleFormatKind = role.RoleFormatKind,
                PersonCount = personCount,
                CompanyCount = companyCount
            });
        }

        // ── 索引ページ・総合ページ ──
        GenerateIndex(roleIndexEntries);
        GenerateAllPersonsRanking(aliasIdsByPersonId, allPersons, rankableRoles);
        GenerateAllCompaniesRanking(companyAliasesByCompany, logosByCompanyAlias, allCompanies, rankableRoles);

        _ctx.Logger.Success($"role stats: {rankableRoles.Count + 3} ページ");
    }

    /// <summary>
    /// <c>/stats/roles/</c>（役職別ランキング索引）。
    /// 各役職について「人数 / 社数」を一覧化し、詳細ページへリンクする。
    /// </summary>
    private void GenerateIndex(IReadOnlyList<RoleIndexEntry> entries)
    {
        var content = new RolesIndexModel
        {
            Roles = entries,
            TotalRoles = entries.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "役職別ランキング",
            MetaDescription = "役職ごとの人物・企業の関与エピソード数ランキング索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "" }
            }
        };
        _page.RenderAndWrite("/stats/roles/", "stats", "stats-roles-index.sbn", content, layout);
    }

    /// <summary>
    /// <c>/stats/roles/{role_code}/</c>（役職別ランキング詳細）。
    /// 当該役職での関与人物/企業を、関与エピソード数の多い順に Wimbledon 順位で並べる。
    /// </summary>
    private void GenerateRoleDetail(
        Role role,
        IReadOnlyDictionary<int, List<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias,
        IReadOnlyList<Company> allCompanies,
        out int personCount,
        out int companyCount)
    {
        // ── 人物側集計：(PersonId × RoleCode × EpisodeId) で重複排除 ──
        var personRows = new List<RoleRankRow>();
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            // 当該人物の全 alias の Involvement のうち、当該役職のものだけを抽出。
            // (EpisodeId, SeriesId) でユニーク化（EpisodeId が null の場合は SeriesId のみで識別）。
            var keys = new HashSet<(int seriesId, int episodeId)>();
            var seriesIds = new HashSet<int>();
            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (!string.Equals(inv.RoleCode, role.RoleCode, StringComparison.Ordinal)) continue;
                    keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    seriesIds.Add(inv.SeriesId);
                }
            }
            if (keys.Count == 0) continue;

            personRows.Add(new RoleRankRow
            {
                EntityKind = "person",
                EntityId = p.PersonId,
                EntityName = p.FullName,
                EntityNameKana = p.FullNameKana ?? "",
                EntityUrl = PathUtil.PersonUrl(p.PersonId),
                EpisodeCount = keys.Count,
                SeriesCount = seriesIds.Count
            });
        }

        // ── 企業側集計：(CompanyId × RoleCode × EpisodeId) ──
        // CompanyAlias 由来 + LeadingCompany 由来 (= ByCompanyAlias) と Logo 由来 (= ByLogo) の和。
        var companyRows = new List<RoleRankRow>();
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            var keys = new HashSet<(int seriesId, int episodeId)>();
            var seriesIds = new HashSet<int>();
            foreach (var aid in aliasIds)
            {
                // 屋号への関与（COMPANY エントリ + leading_company_alias_id）。
                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs)
                    {
                        if (!string.Equals(inv.RoleCode, role.RoleCode, StringComparison.Ordinal)) continue;
                        keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                        seriesIds.Add(inv.SeriesId);
                    }
                }
                // 当該屋号の配下にあるロゴ経由の関与。
                if (logosByCompanyAlias.TryGetValue(aid, out var logoIds))
                {
                    foreach (var logoId in logoIds)
                    {
                        if (!_index.ByLogo.TryGetValue(logoId, out var logoInvs)) continue;
                        foreach (var inv in logoInvs)
                        {
                            if (!string.Equals(inv.RoleCode, role.RoleCode, StringComparison.Ordinal)) continue;
                            keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                            seriesIds.Add(inv.SeriesId);
                        }
                    }
                }
            }
            if (keys.Count == 0) continue;

            companyRows.Add(new RoleRankRow
            {
                EntityKind = "company",
                EntityId = c.CompanyId,
                EntityName = c.Name,
                EntityNameKana = c.NameKana ?? "",
                EntityUrl = PathUtil.CompanyUrl(c.CompanyId),
                EpisodeCount = keys.Count,
                SeriesCount = seriesIds.Count
            });
        }

        AssignWimbledonRanks(personRows);
        AssignWimbledonRanks(companyRows);

        personCount = personRows.Count;
        companyCount = companyRows.Count;

        var content = new RoleDetailModel
        {
            RoleCode = role.RoleCode,
            RoleNameJa = role.NameJa,
            RoleFormatKind = role.RoleFormatKind,
            PersonRanking = personRows,
            CompanyRanking = companyRows
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}：関与ランキング",
            MetaDescription = $"役職「{role.NameJa}」の関与エピソード数ランキング。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/roles/" },
                new BreadcrumbItem { Label = role.NameJa, Url = "" }
            }
        };
        _page.RenderAndWrite($"/stats/roles/{role.RoleCode}/", "stats", "stats-role-detail.sbn", content, layout);
    }

    /// <summary>
    /// <c>/stats/roles/all-persons/</c>（人物総合ランキング、上位 100 件）。
    /// (PersonId × EpisodeId) で重複排除し、複数役職を兼任していても 1 エピソード扱い。
    /// </summary>
    private void GenerateAllPersonsRanking(
        IReadOnlyDictionary<int, List<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyList<Role> rankableRoles)
    {
        var roleNameMap = rankableRoles.ToDictionary(r => r.RoleCode, r => r.NameJa, StringComparer.Ordinal);

        var rows = new List<AllPersonsRow>();
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            // (EpisodeId) のユニーク集合 = 総合カウント。
            var episodeKeys = new HashSet<(int seriesId, int episodeId)>();
            // 役職別の (RoleCode, EpisodeId) ユニーク集合 = 役職別内訳カウント。
            var byRole = new Dictionary<string, HashSet<(int, int)>>(StringComparer.Ordinal);

            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    // VOICE_CAST 役職はランキング対象外。
                    if (!roleNameMap.ContainsKey(inv.RoleCode)) continue;

                    var key = (inv.SeriesId, inv.EpisodeId ?? 0);
                    episodeKeys.Add(key);

                    if (!byRole.TryGetValue(inv.RoleCode, out var set))
                    {
                        set = new HashSet<(int, int)>();
                        byRole[inv.RoleCode] = set;
                    }
                    set.Add(key);
                }
            }
            if (episodeKeys.Count == 0) continue;

            // 役職別内訳を多い順に並べて上位 5 件まで併記。
            var breakdown = byRole
                .Select(kv => new RoleBreakdownItem
                {
                    RoleCode = kv.Key,
                    RoleNameJa = roleNameMap.TryGetValue(kv.Key, out var nm) ? nm : kv.Key,
                    EpisodeCount = kv.Value.Count
                })
                .OrderByDescending(x => x.EpisodeCount)
                .ThenBy(x => x.RoleNameJa, StringComparer.Ordinal)
                .Take(5)
                .ToList();

            rows.Add(new AllPersonsRow
            {
                PersonId = p.PersonId,
                FullName = p.FullName,
                FullNameKana = p.FullNameKana ?? "",
                EpisodeCount = episodeKeys.Count,
                Breakdown = breakdown
            });
        }

        // 上位 100 件に絞る。Wimbledon 順位付け後に切ると同点付近が乱れるので、
        // 100 件のしきい値で切ったうえで 100 件以内の同点者は全員残す方針。
        rows = rows
            .OrderByDescending(r => r.EpisodeCount)
            .ThenBy(r => r.FullNameKana, StringComparer.Ordinal)
            .ThenBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();
        if (rows.Count > 100)
        {
            int cutoff = rows[99].EpisodeCount;
            rows = rows.Where(r => r.EpisodeCount >= cutoff).ToList();
        }

        // 順位付け（Wimbledon 形式）。
        AssignWimbledonRanksAllPersons(rows);

        var content = new AllPersonsModel { Rows = rows };
        var layout = new LayoutModel
        {
            PageTitle = "人物総合ランキング（TOP 100）",
            MetaDescription = "全シリーズ・全役職を横断した人物別の関与エピソード数ランキング。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/roles/" },
                new BreadcrumbItem { Label = "人物総合ランキング", Url = "" }
            }
        };
        _page.RenderAndWrite("/stats/roles/all-persons/", "stats", "stats-roles-all-persons.sbn", content, layout);
    }

    /// <summary>
    /// <c>/stats/roles/all-companies/</c>（企業総合ランキング、上位 100 件）。
    /// (CompanyId × EpisodeId) で重複排除し、複数役職を跨いでも 1 エピソード扱い。
    /// </summary>
    private void GenerateAllCompaniesRanking(
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias,
        IReadOnlyList<Company> allCompanies,
        IReadOnlyList<Role> rankableRoles)
    {
        var roleNameMap = rankableRoles.ToDictionary(r => r.RoleCode, r => r.NameJa, StringComparer.Ordinal);

        var rows = new List<AllCompaniesRow>();
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            var episodeKeys = new HashSet<(int seriesId, int episodeId)>();
            var byRole = new Dictionary<string, HashSet<(int, int)>>(StringComparer.Ordinal);

            void AccumulateInvolvement(Involvement inv)
            {
                if (!roleNameMap.ContainsKey(inv.RoleCode)) return;
                var key = (inv.SeriesId, inv.EpisodeId ?? 0);
                episodeKeys.Add(key);
                if (!byRole.TryGetValue(inv.RoleCode, out var set))
                {
                    set = new HashSet<(int, int)>();
                    byRole[inv.RoleCode] = set;
                }
                set.Add(key);
            }

            foreach (var aid in aliasIds)
            {
                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs) AccumulateInvolvement(inv);
                }
                if (logosByCompanyAlias.TryGetValue(aid, out var logoIds))
                {
                    foreach (var logoId in logoIds)
                    {
                        if (!_index.ByLogo.TryGetValue(logoId, out var logoInvs)) continue;
                        foreach (var inv in logoInvs) AccumulateInvolvement(inv);
                    }
                }
            }
            if (episodeKeys.Count == 0) continue;

            var breakdown = byRole
                .Select(kv => new RoleBreakdownItem
                {
                    RoleCode = kv.Key,
                    RoleNameJa = roleNameMap.TryGetValue(kv.Key, out var nm) ? nm : kv.Key,
                    EpisodeCount = kv.Value.Count
                })
                .OrderByDescending(x => x.EpisodeCount)
                .ThenBy(x => x.RoleNameJa, StringComparer.Ordinal)
                .Take(5)
                .ToList();

            rows.Add(new AllCompaniesRow
            {
                CompanyId = c.CompanyId,
                NameJa = c.Name,
                NameJaKana = c.NameKana ?? "",
                EpisodeCount = episodeKeys.Count,
                Breakdown = breakdown
            });
        }

        rows = rows
            .OrderByDescending(r => r.EpisodeCount)
            .ThenBy(r => r.NameJaKana, StringComparer.Ordinal)
            .ThenBy(r => r.NameJa, StringComparer.Ordinal)
            .ToList();
        if (rows.Count > 100)
        {
            int cutoff = rows[99].EpisodeCount;
            rows = rows.Where(r => r.EpisodeCount >= cutoff).ToList();
        }
        AssignWimbledonRanksAllCompanies(rows);

        var content = new AllCompaniesModel { Rows = rows };
        var layout = new LayoutModel
        {
            PageTitle = "企業総合ランキング（TOP 100）",
            MetaDescription = "全シリーズ・全役職を横断した企業別の関与エピソード数ランキング。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "統計", Url = "/stats/roles/" },
                new BreadcrumbItem { Label = "企業総合ランキング", Url = "" }
            }
        };
        _page.RenderAndWrite("/stats/roles/all-companies/", "stats", "stats-roles-all-companies.sbn", content, layout);
    }

    /// <summary>
    /// Wimbledon 形式の順位付け（同点は同順位、次は同点者数だけスキップ）を <see cref="RoleRankRow"/> リストに付与する。
    /// 入力リストは EpisodeCount 降順 → よみ順 → 名前順 でソートしてから順位を付ける。
    /// </summary>
    private static void AssignWimbledonRanks(List<RoleRankRow> rows)
    {
        rows.Sort((a, b) =>
        {
            int c = b.EpisodeCount.CompareTo(a.EpisodeCount);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.EntityNameKana, b.EntityNameKana);
            if (c != 0) return c;
            return string.CompareOrdinal(a.EntityName, b.EntityName);
        });

        int currentRank = 0, lastCount = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].EpisodeCount != lastCount)
            {
                currentRank = i + 1;
                lastCount = rows[i].EpisodeCount;
            }
            rows[i].Rank = currentRank;
        }
    }

    private static void AssignWimbledonRanksAllPersons(List<AllPersonsRow> rows)
    {
        // ソートは呼び出し側で済ませている前提。
        int currentRank = 0, lastCount = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].EpisodeCount != lastCount)
            {
                currentRank = i + 1;
                lastCount = rows[i].EpisodeCount;
            }
            rows[i].Rank = currentRank;
        }
    }

    private static void AssignWimbledonRanksAllCompanies(List<AllCompaniesRow> rows)
    {
        int currentRank = 0, lastCount = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].EpisodeCount != lastCount)
            {
                currentRank = i + 1;
                lastCount = rows[i].EpisodeCount;
            }
            rows[i].Rank = currentRank;
        }
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class RolesIndexModel
    {
        public IReadOnlyList<RoleIndexEntry> Roles { get; set; } = Array.Empty<RoleIndexEntry>();
        public int TotalRoles { get; set; }
    }

    private sealed class RoleIndexEntry
    {
        public string RoleCode { get; set; } = "";
        public string RoleNameJa { get; set; } = "";
        public string RoleFormatKind { get; set; } = "";
        public int PersonCount { get; set; }
        public int CompanyCount { get; set; }
    }

    private sealed class RoleDetailModel
    {
        public string RoleCode { get; set; } = "";
        public string RoleNameJa { get; set; } = "";
        public string RoleFormatKind { get; set; } = "";
        public IReadOnlyList<RoleRankRow> PersonRanking { get; set; } = Array.Empty<RoleRankRow>();
        public IReadOnlyList<RoleRankRow> CompanyRanking { get; set; } = Array.Empty<RoleRankRow>();
    }

    private sealed class RoleRankRow
    {
        public int Rank { get; set; }
        /// <summary>"person" または "company"（テンプレ側で使い分け用）。</summary>
        public string EntityKind { get; set; } = "";
        public int EntityId { get; set; }
        public string EntityName { get; set; } = "";
        public string EntityNameKana { get; set; } = "";
        public string EntityUrl { get; set; } = "";
        public int EpisodeCount { get; set; }
        public int SeriesCount { get; set; }
    }

    private sealed class AllPersonsModel
    {
        public IReadOnlyList<AllPersonsRow> Rows { get; set; } = Array.Empty<AllPersonsRow>();
    }

    private sealed class AllPersonsRow
    {
        public int Rank { get; set; }
        public int PersonId { get; set; }
        public string FullName { get; set; } = "";
        public string FullNameKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public IReadOnlyList<RoleBreakdownItem> Breakdown { get; set; } = Array.Empty<RoleBreakdownItem>();
    }

    private sealed class AllCompaniesModel
    {
        public IReadOnlyList<AllCompaniesRow> Rows { get; set; } = Array.Empty<AllCompaniesRow>();
    }

    private sealed class AllCompaniesRow
    {
        public int Rank { get; set; }
        public int CompanyId { get; set; }
        /// <summary>企業の正式名称（Company.Name と同じ）。</summary>
        public string NameJa { get; set; } = "";
        /// <summary>企業の正式名称の読み（Company.NameKana と同じ、空読みは空文字）。</summary>
        public string NameJaKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public IReadOnlyList<RoleBreakdownItem> Breakdown { get; set; } = Array.Empty<RoleBreakdownItem>();
    }

    private sealed class RoleBreakdownItem
    {
        public string RoleCode { get; set; } = "";
        public string RoleNameJa { get; set; } = "";
        public int EpisodeCount { get; set; }
    }
}
