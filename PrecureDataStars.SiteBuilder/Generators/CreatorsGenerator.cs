using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 「クリエーター」セクション一式（人物・企業/団体・声優のハブ）の生成。
/// 生成ページ：
/// <list type="bullet">
///   <item><description><c>/creators/</c> … スタッフ / 声の出演の 2 カードを案内するランディング。</description></item>
///   <item><description><c>/creators/staff/</c> … 役職順 / 五十音順 / 初参加順 /
///     参加話数が多い順 の 4 タブ。五十音順以降のタブは人物と企業・団体を 1 リストに混在させ、
///     行ごとに「個人 / 団体」バッジで区別し、上部トグルで個人のみ・団体のみに絞れる。</description></item>
///   <item><description><c>/creators/roles/{rep_role_code}/</c> … 当該役職クラスタに
///     関わった人物・企業/団体を 1 リストに混在させ、五十音順 / 初参加順 / 担当話数が多い順
///     のタブで切り替える役職詳細。</description></item>
///   <item><description><c>/creators/voice-cast/</c> … 五十音順 / キャラクター順 /
///     初出演順 / 出演話数が多い順 の 4 タブで声優を並べる。</description></item>
/// </list>
/// 集計の骨格：
/// <list type="bullet">
///   <item><description>役職詳細：(エンティティ × RoleCluster × EpisodeId) で重複排除。
///     RoleCluster は系譜（<c>role_successions</c>）でまとまる役職群を 1 単位とする。
///     同一エピソードで同一役職に OP / ED 両方クレジットされていても 1 回扱い。</description></item>
///   <item><description>スタッフ一覧（五十音順以降のタブ）：(エンティティ × EpisodeId) で
///     重複排除。複数役職を兼任していても 1 回扱い。VOICE_CAST 役職は対象外。</description></item>
///   <item><description>企業・団体は COMPANY エントリ + LOGO エントリ +
///     leading_company_alias_id の 3 ルートを合算。</description></item>
/// </list>
/// 「順位」「ランキング」という語・順位列は人物・企業/団体に対しては用いない。
/// 並べ替えはタブによるソート手段であり、担当話数の多寡を優劣として扱わない。上限件数なし（全件出力）。
/// </summary>
public sealed class CreatorsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly CreditInvolvementIndex _index;
    private readonly RoleSuccessorResolver _resolver;

    private readonly RolesRepository _rolesRepo;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly CharactersRepository _charactersRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;

    public CreatorsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index,
        RoleSuccessorResolver resolver)
    {
        _ctx = ctx;
        _page = page;
        _index = index;
        _resolver = resolver;

        _rolesRepo = new RolesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _companiesRepo = new CompaniesRepository(factory);
        _companyAliasesRepo = new CompanyAliasesRepository(factory);
        _logosRepo = new LogosRepository(factory);
        _charactersRepo = new CharactersRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating creators");

        // マスタ全件をロード。
        var allRoles = (await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersons = (await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCompanies = (await _companiesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCompanyAliases = (await _companyAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allLogos = (await _logosRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        // 人物と紐付く全 alias_id は SiteDataLoader が BuildContext.AliasIdsByPerson に
        // 全件辞書化済み。旧コードは人物数（~5,000）分の GetByPersonAsync を順次発火する
        // N+1 クエリだったが、本パスで共有辞書を直接参照する形に統一する。
        var aliasIdsByPersonId = _ctx.AliasIdsByPerson;

        // 企業 → 屋号 → ロゴ の構造を辞書化。
        var companyAliasesByCompany = allCompanyAliases.GroupBy(a => a.CompanyId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.AliasId).ToList());
        var logosByCompanyAlias = allLogos.GroupBy(l => l.CompanyAliasId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.LogoId).ToList());

        // 役職マスタから VOICE_CAST 区分を除外（声の出演は専用ページ）。
        // 系譜（role_successions）でまとまるクラスタの「代表」役職のみを残す。
        // クラスタ代表でない（= 過去の名前）役職は索引にも詳細ページにも出さない。
        // 並べ替えは roles マスタの display_order（管理画面の表示順にすぎず、
        // 閲覧者向けの意味を持たない）には依存させない。実際の役職順は後段で
        // 「その役職が最も早くクレジットされた (放送開始, 話数, クレジット出現位置)」
        // に基づいて決める。ここでは安定した初期列だけ作る（role_code 昇順）。
        var roleByCode = allRoles.ToDictionary(r => r.RoleCode, r => r, StringComparer.Ordinal);
        var rankableRoles = allRoles
            .Where(r => !string.Equals(r.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal))
            .Where(r => string.Equals(_resolver.GetRepresentative(r.RoleCode), r.RoleCode, StringComparison.Ordinal))
            .OrderBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToList();

        var personById = allPersons.ToDictionary(p => p.PersonId);
        var companyById = allCompanies.ToDictionary(c => c.CompanyId);

        // ── 役職詳細ページ群を生成し、あわせて「役職順」タブ用の索引エントリも構築 ──
        var roleIndexEntries = new List<RoleIndexEntry>();
        foreach (var role in rankableRoles)
        {
            // クラスタ全 role_code（自分を含む）を集計対象とする。
            // VOICE_CAST はクラスタ内に混在する想定はないが念のため除外する。
            var memberCodes = _resolver.GetClusterMembers(role.RoleCode)
                .Where(c => roleByCode.TryGetValue(c, out var rr)
                            && !string.Equals(rr.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            var rows = BuildRoleEntityRows(
                memberCodes, aliasIdsByPersonId, allPersons,
                companyAliasesByCompany, logosByCompanyAlias, allCompanies);

            // 一度もクレジットのない役職は出さない方針：関与エンティティが 0 件なら
            // 役職詳細ページも生成せず、「役職順」タブの索引（roleIndexEntries）にも積まない。
            if (rows.Count == 0) continue;

            int personCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal));
            int companyCount = rows.Count - personCount;

            GenerateRoleDetail(role, memberCodes, roleByCode, rows);

            // 役職順タブの並べ替えキー：この役職が最も早くクレジットされた
            long roleSortStart = long.MaxValue;
            int roleSortEpNo = int.MaxValue;
            long roleSortPos = long.MaxValue;
            foreach (var er in rows)
            {
                if (er.FirstSortStart < roleSortStart
                    || (er.FirstSortStart == roleSortStart && er.FirstSortEpNo < roleSortEpNo)
                    || (er.FirstSortStart == roleSortStart && er.FirstSortEpNo == roleSortEpNo
                        && er.FirstSortPos < roleSortPos))
                {
                    roleSortStart = er.FirstSortStart;
                    roleSortEpNo = er.FirstSortEpNo;
                    roleSortPos = er.FirstSortPos;
                }
            }

            roleIndexEntries.Add(new RoleIndexEntry
            {
                RoleNameJa = role.NameJa,
                // 役職詳細ページへのリンクは PathUtil 経由で組み立て、URL パス上のコードを
                // 小文字化する。テンプレ側はこの組み立て済み URL のみ参照する。
                RoleUrl = PathUtil.RoleStatsUrl(role.RoleCode),
                PersonCount = personCount,
                CompanyCount = companyCount,
                SortStart = roleSortStart,
                SortEpNo = roleSortEpNo,
                SortPos = roleSortPos,
                RoleNameKey = role.RoleCode
            });
        }

        // 役職順：最も早くクレジットされた (放送開始, 話数, クレジット階層位置) の昇順。
        roleIndexEntries = roleIndexEntries
            .OrderBy(e => e.SortStart)
            .ThenBy(e => e.SortEpNo)
            .ThenBy(e => e.SortPos)
            .ThenBy(e => e.RoleNameKey, StringComparer.Ordinal)
            .ToList();

        // ── スタッフ一覧（/creators/staff/） ──
        GenerateStaff(roleIndexEntries, aliasIdsByPersonId, allPersons, personById,
            companyAliasesByCompany, logosByCompanyAlias, allCompanies, companyById,
            rankableRoles, roleByCode, out int staffEntityCount);

        // ── 声の出演（/creators/voice-cast/） ──
        var allCharacters = (await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        GenerateVoiceCast(aliasIdsByPersonId, allPersons, allCharacters, allCharacterAliases,
            out int voiceCastCount);

        // ── ランディング（/creators/） ──
        GenerateLanding(staffEntityCount, voiceCastCount);

        _ctx.Logger.Success(
            $"creators: {rankableRoles.Count} 役職詳細 + スタッフ + 声の出演 + ランディング");
    }

    // 役職詳細

    /// <summary>1 役職クラスタに関わった人物・企業/団体を 1 リストに混在させた行群を作る。</summary>
    private List<EntityRow> BuildRoleEntityRows(
        IReadOnlySet<string> memberCodes,
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias,
        IReadOnlyList<Company> allCompanies)
    {
        var rows = new List<EntityRow>();

        // 人物。
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            var keys = new HashSet<(int seriesId, int episodeId)>();
            var seriesIds = new HashSet<int>();
            var firstKey = new FirstCreditAccumulator(_ctx);
            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (!memberCodes.Contains(inv.RoleCode)) continue;
                    keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    seriesIds.Add(inv.SeriesId);
                    firstKey.Offer(inv);
                }
            }
            if (keys.Count == 0) continue;

            rows.Add(MakeEntityRow("person", p.PersonId, p.FullName, p.FullNameKana ?? "",
                PathUtil.PersonUrl(p.PersonId), keys.Count, seriesIds.Count, firstKey));
        }

        // 企業・団体（COMPANY + LOGO + leading_company の 3 ルート合算）。
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            var keys = new HashSet<(int seriesId, int episodeId)>();
            var seriesIds = new HashSet<int>();
            var firstKey = new FirstCreditAccumulator(_ctx);
            foreach (var aid in aliasIds)
            {
                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs)
                    {
                        if (!memberCodes.Contains(inv.RoleCode)) continue;
                        keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                        seriesIds.Add(inv.SeriesId);
                        firstKey.Offer(inv);
                    }
                }
                if (logosByCompanyAlias.TryGetValue(aid, out var logoIds))
                {
                    foreach (var logoId in logoIds)
                    {
                        if (!_index.ByLogo.TryGetValue(logoId, out var logoInvs)) continue;
                        foreach (var inv in logoInvs)
                        {
                            if (!memberCodes.Contains(inv.RoleCode)) continue;
                            keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                            seriesIds.Add(inv.SeriesId);
                            firstKey.Offer(inv);
                        }
                    }
                }
            }
            if (keys.Count == 0) continue;

            rows.Add(MakeEntityRow("company", c.CompanyId, c.Name, c.NameKana ?? "",
                PathUtil.CompanyUrl(c.CompanyId), keys.Count, seriesIds.Count, firstKey));
        }

        return rows;
    }

    /// <summary>/creators/roles/{rep_role_code}/ を 3 タブ（五十音順 / 初参加順 / 担当話数が多い順）で書き出す。</summary>
    private void GenerateRoleDetail(
        Role role,
        IReadOnlySet<string> memberCodes,
        IReadOnlyDictionary<string, Role> roleByCode,
        List<EntityRow> rows)
    {
        // クラスタ歴代名（自分自身を除く別役職名、display_order 昇順）。
        // 閲覧者向けに日本語の役職名のみを並べる（内部の役職コードは出さない）。
        var alternateNames = memberCodes
            .Where(c => !string.Equals(c, role.RoleCode, StringComparison.Ordinal))
            .Where(c => roleByCode.ContainsKey(c))
            .Select(c => roleByCode[c])
            .OrderBy(r => r.DisplayOrder ?? ushort.MaxValue)
            .ThenBy(r => r.RoleCode, StringComparer.Ordinal)
            .Select(r => new AlternateNameItem { RoleNameJa = r.NameJa })
            .ToList();

        var content = new RoleDetailModel
        {
            RoleNameJa = role.NameJa,
            KanaRows = SortByKana(rows),
            DebutSections = SectionByDebut(rows),
            CountRows = SortByCount(rows),
            AlternateNames = alternateNames,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}（クリエーター）",
            MetaDescription = $"役職「{role.NameJa}」に関わった人物・企業・団体の一覧。五十音順・初参加順・担当話数が多い順で並べ替えできます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
                new BreadcrumbItem { Label = role.NameJa, Url = "" }
            }
        };
        // 出力先パスもリンク生成と同一の PathUtil.RoleStatsUrl を通すことで、
        // URL パス上のコード小文字化と出力ディレクトリ名を必ず一致させる。
        _page.RenderAndWrite(PathUtil.RoleStatsUrl(role.RoleCode), "creators",
            "creators-role-detail.sbn", content, layout);
    }

    // スタッフ一覧

    /// <summary><c>/creators/staff/</c> を 4 タブで書き出す。 役職順タブは役職名 + 人数/社数の索引（各役職詳細への入口）。 それ以外の 3 タブは全役職横断の人物・企業/団体の混在一覧。</summary>
    private void GenerateStaff(
        IReadOnlyList<RoleIndexEntry> roleIndexEntries,
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyDictionary<int, Person> personById,
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias,
        IReadOnlyList<Company> allCompanies,
        IReadOnlyDictionary<int, Company> companyById,
        IReadOnlyList<Role> rankableRoles,
        IReadOnlyDictionary<string, Role> roleByCode,
        out int staffEntityCount)
    {
        // 内訳・役職ラベルに使う「代表 role_code → 代表 NameJa」マップ。
        var repNameMap = rankableRoles.ToDictionary(r => r.RoleCode, r => r.NameJa, StringComparer.Ordinal);

        var rows = new List<EntityRow>();

        // 人物（全 non-VOICE_CAST 役職を横断、エピソード単位で重複排除）。
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            var keys = new HashSet<(int seriesId, int episodeId)>();
            var seriesIds = new HashSet<int>();
            var firstKey = new FirstCreditAccumulator(_ctx);
            // 役職ラベル用：代表 role_code → その役職で最も早い (Start, EpNo)。
            var earliestByRep = new Dictionary<string, (DateOnly Start, int EpNo, long Pos)>(StringComparer.Ordinal);

            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    string rep = _resolver.GetRepresentative(inv.RoleCode);
                    if (!repNameMap.ContainsKey(rep)) continue; // VOICE_CAST 等は対象外
                    keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    seriesIds.Add(inv.SeriesId);
                    firstKey.Offer(inv);
                    OfferEarliestRole(earliestByRep, rep, inv);
                }
            }
            if (keys.Count == 0) continue;

            var row = MakeEntityRow("person", p.PersonId, p.FullName, p.FullNameKana ?? "",
                PathUtil.PersonUrl(p.PersonId), keys.Count, seriesIds.Count, firstKey);
            row.RolesLabel = BuildRolesLabel(earliestByRep, repNameMap);
            rows.Add(row);
        }

        // 企業・団体（COMPANY + LOGO + leading_company を合算、全役職横断）。
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            var keys = new HashSet<(int seriesId, int episodeId)>();
            var seriesIds = new HashSet<int>();
            var firstKey = new FirstCreditAccumulator(_ctx);
            var earliestByRep = new Dictionary<string, (DateOnly Start, int EpNo, long Pos)>(StringComparer.Ordinal);

            void Accumulate(Involvement inv)
            {
                string rep = _resolver.GetRepresentative(inv.RoleCode);
                if (!repNameMap.ContainsKey(rep)) return;
                keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                seriesIds.Add(inv.SeriesId);
                firstKey.Offer(inv);
                OfferEarliestRole(earliestByRep, rep, inv);
            }

            foreach (var aid in aliasIds)
            {
                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs) Accumulate(inv);
                }
                if (logosByCompanyAlias.TryGetValue(aid, out var logoIds))
                {
                    foreach (var logoId in logoIds)
                    {
                        if (!_index.ByLogo.TryGetValue(logoId, out var logoInvs)) continue;
                        foreach (var inv in logoInvs) Accumulate(inv);
                    }
                }
            }
            if (keys.Count == 0) continue;

            var row = MakeEntityRow("company", c.CompanyId, c.Name, c.NameKana ?? "",
                PathUtil.CompanyUrl(c.CompanyId), keys.Count, seriesIds.Count, firstKey);
            row.RolesLabel = BuildRolesLabel(earliestByRep, repNameMap);
            rows.Add(row);
        }

        staffEntityCount = rows.Count;

        var content = new StaffModel
        {
            Roles = roleIndexEntries,
            TotalRoles = roleIndexEntries.Count,
            KanaRows = SortByKana(rows),
            DebutSections = SectionByDebut(rows),
            CountRows = SortByCount(rows),
            PersonCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal)),
            CompanyCount = rows.Count(r => string.Equals(r.EntityKind, "company", StringComparison.Ordinal)),
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュアスタッフ",
            MetaDescription = "プリキュアシリーズに関わったスタッフ（人物・企業・団体）の一覧。役職順・五十音順・初参加順・参加話数が多い順で並べ替えできます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsStaffUrl(), "creators",
            "creators-staff.sbn", content, layout);
    }

    // 声の出演

    /// <summary>
    /// <c>/creators/voice-cast/</c> を 4 タブ（五十音順 / キャラクター順 /
    /// 初出演順 / 出演話数が多い順）で書き出す。
    /// 1 行 = (声優 × シリーズ × キャラ) 粒度。別シリーズで同じ声優が同じ／別のキャラを
    /// 演じていれば、それぞれ別の行として、その都度キャラ名が出る。
    /// CHARACTER_VOICE 経由の関与のうち character_alias_id が解決できるものを対象とする。
    /// raw_character_text のみで character_alias_id 未設定のエントリ（モブ等）は対象外。
    /// 1 行の「出演話数」は当該 (声優 × シリーズ × キャラ) の重複排除済みエピソード数。
    /// シリーズ全体スコープ（episode_id=null）のみのクレジットも 1 行として残す（話数は «—» 表示）。
    /// </summary>
    private void GenerateVoiceCast(
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyList<Character> allCharacters,
        IReadOnlyList<CharacterAlias> allCharacterAliases,
        out int voiceCastCount)
    {
        var characterById = allCharacters.ToDictionary(c => c.CharacterId);
        var aliasToCharId = new Dictionary<int, int>();
        foreach (var a in allCharacterAliases) aliasToCharId[a.AliasId] = a.CharacterId;

        var rows = new List<VoiceCastRow>();
        var distinctPersons = new HashSet<int>();

        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            // (SeriesId, CharacterId) ごとに出演エピソード番号集合と、
            // 「最早エピソード内で最初にクレジットされた階層位置」を畳み込む。
            // シリーズ全体スコープ（episode_id=null）のみのクレジットは空集合のまま残り、
            // 後段で出演話数 0（«—» 表示）の 1 行になる。
            // EpNos=重複排除した話数集合 / BestEpNo=最早話数 /
            // BestPos=その最早話数内での最小クレジット階層位置 (CreditSeq,CreditSubSeq) 合成キー。
            var bucket = new Dictionary<(int SeriesId, int CharacterId),
                (HashSet<int> EpNos, int BestEpNo, long BestPos)>();

            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (inv.Kind != InvolvementKind.CharacterVoice) continue;
                    if (inv.CharacterAliasId is not int caId) continue;
                    if (!aliasToCharId.TryGetValue(caId, out var charId)) continue;

                    var key = (inv.SeriesId, charId);
                    if (!bucket.TryGetValue(key, out var acc))
                    {
                        acc = (new HashSet<int>(), int.MaxValue, long.MaxValue);
                        bucket[key] = acc;
                    }

                    if (inv.EpisodeId is int eid)
                    {
                        var ep = _ctx.LookupEpisode(inv.SeriesId, eid);
                        if (ep is not null)
                        {
                            acc.EpNos.Add(ep.SeriesEpNo);
                            // 最早話数と、その話数内での最小クレジット階層位置を更新する。
                            long pos = CombinedCreditPos(inv);
                            if (ep.SeriesEpNo < acc.BestEpNo
                                || (ep.SeriesEpNo == acc.BestEpNo && pos < acc.BestPos))
                            {
                                acc.BestEpNo = ep.SeriesEpNo;
                                acc.BestPos = pos;
                            }
                        }
                    }
                    bucket[key] = acc;
                }
            }

            foreach (var kv in bucket)
            {
                int seriesId = kv.Key.SeriesId;
                int charId = kv.Key.CharacterId;
                if (!_ctx.SeriesById.TryGetValue(seriesId, out var series)) continue;
                if (!characterById.TryGetValue(charId, out var ch)) continue;

                var epNos = kv.Value.EpNos;
                int earliestEpNo = epNos.Count > 0 ? epNos.Min() : 0;
                // 最早話数内のクレジット階層位置（話数が無いシリーズスコープのみは末尾送り）。
                long earliestPos = kv.Value.BestPos;

                rows.Add(new VoiceCastRow
                {
                    PersonName = p.FullName,
                    PersonNameKana = p.FullNameKana ?? "",
                    PersonUrl = PathUtil.PersonUrl(p.PersonId),
                    SeriesTitle = series.Title,
                    SeriesUrl = PathUtil.SeriesUrl(series.Slug),
                    SeriesYearLabel = series.StartDate.Year.ToString(),
                    SeriesSortStart = series.StartDate.DayNumber,
                    CharacterName = ch.Name,
                    CharacterNameKana = ch.NameKana ?? "",
                    CharacterUrl = PathUtil.CharacterUrl(ch.CharacterId),
                    CharacterId = ch.CharacterId,
                    EpisodeCount = epNos.Count,
                    EarliestEpNo = earliestEpNo,
                    EarliestPos = earliestPos
                });
                distinctPersons.Add(p.PersonId);
            }
        }

        // ランディングカードの «N 名» は声優の実人数（行数ではない）。
        voiceCastCount = distinctPersons.Count;

        // 五十音順（既定タブ）：声優の読み → 名前 → シリーズ放送開始 → キャラ読み。
        // 五十音順（セクション無し）：声優読み → 名前 → シリーズ放送開始 → キャラ読み。
        // 五十音順はルールが完全に一意なのでクレジット位置キーは挟まない。
        // 表にシリーズ列は出さない方針（行は声優・キャラ・出演話数のみ）。
        var kanaRows = rows
            .OrderBy(r => string.IsNullOrEmpty(r.PersonNameKana) ? 1 : 0)
            .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
            .ThenBy(r => r.PersonName, StringComparer.Ordinal)
            .ThenBy(r => r.SeriesSortStart)
            .ThenBy(r => r.CharacterNameKana, StringComparer.Ordinal)
            .ThenBy(r => r.CharacterName, StringComparer.Ordinal)
            .ToList();

        // キャラクター順（既定タブ・シリーズセクション）：
        // セクション＝シリーズ（放送開始日順）。セクション内は「キャラクターを
        // 主たる単位」とし、各キャラをそのキャラが最初にクレジットされた位置
        // （= そのキャラの全行のうち最小の (最早話数, クレジット階層位置)）の順で並べる。
        // 同一キャラを複数声優が演じる場合（交代・代役）は、その声優行を当該
        // キャラの位置にまとめて連続表示する（キャラ内は最早話数→クレジット
        // 位置→声優読みの順）。キャラ読み五十音ではなくクレジット出現順で並べる。
        var charSections = rows
            .GroupBy(r => (r.SeriesSortStart, r.SeriesTitle, r.SeriesUrl, r.SeriesYearLabel))
            .OrderBy(g => g.Key.SeriesSortStart)
            .ThenBy(g => g.Key.SeriesTitle, StringComparer.Ordinal)
            .Select(g =>
            {
                // シリーズ内をキャラ単位にまとめ、各キャラの「最初にクレジット
                // された位置」を (最早話数, クレジット階層位置) で求める。
                var perCharacter = g
                    .GroupBy(r => r.CharacterId)
                    .Select(cg => new
                    {
                        // キャラの登場位置 = そのキャラの全声優行のうち最小キー。
                        FirstEpNo = cg.Min(r => r.EarliestEpNo),
                        FirstPos = cg.Where(r => r.EarliestEpNo == cg.Min(x => x.EarliestEpNo))
                                     .Min(r => r.EarliestPos),
                        // キャラ内の声優行：最早話数 → クレジット位置 → 声優読み。
                        Members = cg
                            .OrderBy(r => r.EarliestEpNo)
                            .ThenBy(r => r.EarliestPos)
                            .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
                            .ToList()
                    })
                    // キャラの並び＝登場位置順（クレジットに出てくる順）。
                    .OrderBy(c => c.FirstEpNo)
                    .ThenBy(c => c.FirstPos)
                    .ToList();

                return new VoiceSeriesSection
                {
                    SeriesTitle = g.Key.SeriesTitle,
                    SeriesUrl = g.Key.SeriesUrl,
                    SeriesHeadingLabel = string.IsNullOrEmpty(g.Key.SeriesYearLabel)
                        ? g.Key.SeriesTitle
                        : $"{g.Key.SeriesTitle}（{g.Key.SeriesYearLabel}）",
                    SortStart = g.Key.SeriesSortStart,
                    // キャラ単位の並びを保ったままフラット展開（同一キャラの
                    // 声優行が連続する）。
                    Members = perCharacter.SelectMany(c => c.Members).ToList()
                };
            })
            .ToList();

        // 初出演順（シリーズセクション）：
        var debutSections = rows
            .GroupBy(r => (r.SeriesSortStart, r.SeriesTitle, r.SeriesUrl, r.SeriesYearLabel))
            .OrderBy(g => g.Key.SeriesSortStart)
            .ThenBy(g => g.Key.SeriesTitle, StringComparer.Ordinal)
            .Select(g => new VoiceSeriesSection
            {
                SeriesTitle = g.Key.SeriesTitle,
                SeriesUrl = g.Key.SeriesUrl,
                SeriesHeadingLabel = string.IsNullOrEmpty(g.Key.SeriesYearLabel)
                    ? g.Key.SeriesTitle
                    : $"{g.Key.SeriesTitle}（{g.Key.SeriesYearLabel}）",
                SortStart = g.Key.SeriesSortStart,
                Members = g
                    .OrderBy(r => r.EarliestEpNo)
                    .ThenBy(r => r.EarliestPos)
                    .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
                    .ThenBy(r => r.CharacterNameKana, StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();

        // 出演話数が多い順（セクション無し）：話数降順 → シリーズ放送開始
        //   → 最早話数 → クレジット出現位置 → 声優読み → キャラ読み。
        var countRows = rows
            .OrderByDescending(r => r.EpisodeCount)
            .ThenBy(r => r.SeriesSortStart)
            .ThenBy(r => r.EarliestEpNo)
            .ThenBy(r => r.EarliestPos)
            .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
            .ThenBy(r => r.CharacterNameKana, StringComparer.Ordinal)
            .ToList();

        var content = new VoiceCastModel
        {
            CharacterSections = charSections,
            KanaRows = kanaRows,
            DebutSections = debutSections,
            CountRows = countRows,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュア声優",
            MetaDescription = "プリキュアシリーズに声の出演をした声優さんの一覧。キャラクター順・五十音順・初出演順・出演話数が多い順で並べ替えできます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュア声優", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsVoiceCastUrl(), "creators",
            "creators-voice-cast.sbn", content, layout);
    }

    // ランディング

    /// <summary><c>/creators/</c> ランディング。スタッフ / 声の出演 の 2 カードを案内する （音楽カテゴリランディング <c>/music/</c> と同型の意匠）。</summary>
    private void GenerateLanding(int staffEntityCount, int voiceCastCount)
    {
        var content = new LandingModel
        {
            StaffCount = staffEntityCount,
            VoiceCastCount = voiceCastCount
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代クリエーター",
            MetaDescription = "プリキュアシリーズを支えたスタッフ（人物・企業・団体）と声の出演（声優）の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsLandingUrl(), "creators",
            "creators-landing.sbn", content, layout);
    }

    // 共有ヘルパ

    /// <summary>クレジット階層の位置 (CreditSeq, CreditSubSeq) を単一 long に畳む。</summary>
    private const long CreditPosStride = 1_000_000L;
    private static long CombinedCreditPos(Involvement inv)
        => (long)inv.CreditSeq * CreditPosStride + inv.CreditSubSeq;

    /// <summary>エンティティ 1 行分を組み立てる。初参加ソート用に <see cref="FirstCreditAccumulator"/> から最早シリーズ情報を移す。</summary>
    private static EntityRow MakeEntityRow(
        string entityKind, int entityId, string name, string nameKana,
        string url, int episodeCount, int seriesCount, FirstCreditAccumulator first)
    {
        return new EntityRow
        {
            EntityKind = entityKind,
            EntityId = entityId,
            EntityName = name,
            EntityNameKana = nameKana,
            EntityUrl = url,
            EpisodeCount = episodeCount,
            SeriesCount = seriesCount,
            FirstSeriesTitle = first.SeriesTitle,
            FirstSeriesUrl = first.SeriesUrl,
            FirstSeriesYearLabel = first.SeriesYearLabel,
            FirstSortStart = first.SortStartTicks,
            FirstSortEpNo = first.SortEpNo,
            FirstSortPos = first.SortCreditPos
        };
    }

    /// <summary>代表 role_code ごとに、その役職で最も早い (Start, EpNo, CreditSeq) を更新する。 同じ話数内で同点のときはクレジット出現位置が早い役職を上位に扱う。</summary>
    private void OfferEarliestRole(
        Dictionary<string, (DateOnly Start, int EpNo, long Pos)> earliestByRep, string rep, Involvement inv)
    {
        var start = _ctx.SeriesStartDate(inv.SeriesId);
        int epNo = inv.EpisodeId is int eid
            ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
            : 0; // シリーズスコープは最早扱い
        long pos = CombinedCreditPos(inv);
        if (!earliestByRep.TryGetValue(rep, out var cur)
            || start < cur.Start
            || (start == cur.Start && epNo < cur.EpNo)
            || (start == cur.Start && epNo == cur.EpNo && pos < cur.Pos))
        {
            earliestByRep[rep] = (start, epNo, pos);
        }
    }

    /// <summary>クレジットされた代表役職を最早出現順に「・」で全列挙した役職ラベルを作る。</summary>
    private static string BuildRolesLabel(
        Dictionary<string, (DateOnly Start, int EpNo, long Pos)> earliestByRep,
        IReadOnlyDictionary<string, string> repNameMap)
    {
        if (earliestByRep.Count == 0) return "";
        var ordered = earliestByRep
            .OrderBy(kv => kv.Value.Start)
            .ThenBy(kv => kv.Value.EpNo)
            .ThenBy(kv => kv.Value.Pos)
            .Select(kv => repNameMap.TryGetValue(kv.Key, out var nm) ? nm : kv.Key)
            .ToList();

        return string.Join("・", ordered);
    }

    /// <summary>五十音順：読み昇順（空読みは末尾） → 名前。</summary>
    private static List<EntityRow> SortByKana(IEnumerable<EntityRow> rows) => rows
        .OrderBy(r => string.IsNullOrEmpty(r.EntityNameKana) ? 1 : 0)
        .ThenBy(r => r.EntityNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.EntityName, StringComparer.Ordinal)
        .ToList();

    /// <summary>初参加順：最早 (シリーズ放送開始, 話数, クレジット出現位置) → 読み → 名前。 同じ話数内で同点のときは、そのエピソードで最初にクレジットされた位置順に並ぶ。</summary>
    private static List<EntityRow> SortByDebut(IEnumerable<EntityRow> rows) => rows
        .OrderBy(r => r.FirstSortStart)
        .ThenBy(r => r.FirstSortEpNo)
        .ThenBy(r => r.FirstSortPos)
        .ThenBy(r => r.EntityNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.EntityName, StringComparer.Ordinal)
        .ToList();

    /// <summary>担当話数が多い順：話数降順 → 最早クレジット (放送開始, 話数, クレジット出現位置) → 読み → 名前（順位は付けない）。 五十音順以外（並びのルールが完全には一意に決まらないタブ）では、クレジット 出現位置を暗黙の副ソートキーとして効かせ、同点行の並びを安定させる方針。 ここでは話数が同数の行を、初出が早い順 → そのエピソード内のクレジット 記載位置順に整える。</summary>
    private static List<EntityRow> SortByCount(IEnumerable<EntityRow> rows) => rows
        .OrderByDescending(r => r.EpisodeCount)
        .ThenBy(r => r.FirstSortStart)
        .ThenBy(r => r.FirstSortEpNo)
        .ThenBy(r => r.FirstSortPos)
        .ThenBy(r => r.EntityNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.EntityName, StringComparer.Ordinal)
        .ToList();

    /// <summary>初参加順を「初参加シリーズ」ごとのセクションに束ねる。 セクションはシリーズ放送開始日順、セクション内は SortByDebut と同じ クレジット順（話数 → クレジット出現位置 → 読み → 名前）。 各行のシリーズ名・年は重複するためセクション見出しへ移し、行からは出さない。</summary>
    private static List<EntitySeriesSection> SectionByDebut(IEnumerable<EntityRow> rows)
    {
        var ordered = SortByDebut(rows);
        var sections = new List<EntitySeriesSection>();
        EntitySeriesSection? current = null;
        foreach (var r in ordered)
        {
            // 初参加シリーズの識別は (放送開始シリアル, シリーズ名) で十分
            // （同日開始の別シリーズが理論上あり得るためタイトルも併用）。
            string headLabel = r.FirstSeriesTitle;
            if (!string.IsNullOrEmpty(r.FirstSeriesYearLabel))
                headLabel += $"（{r.FirstSeriesYearLabel}）";

            if (current is null
                || current.SortStart != r.FirstSortStart
                || !string.Equals(current.SeriesTitle, r.FirstSeriesTitle, StringComparison.Ordinal))
            {
                current = new EntitySeriesSection
                {
                    SeriesTitle = r.FirstSeriesTitle,
                    SeriesUrl = r.FirstSeriesUrl,
                    SeriesHeadingLabel = headLabel,
                    SortStart = r.FirstSortStart,
                    Members = new List<EntityRow>()
                };
                sections.Add(current);
            }
            ((List<EntityRow>)current.Members).Add(r);
        }
        return sections;
    }

    /// <summary>
    /// 関与の最早 (シリーズ放送開始日, シリーズ内話数, クレジット出現位置) を畳み込みで保持し、
    /// 「初参加」表示用のシリーズタイトル・年・リンクと、ソート用キーを提供する補助型。
    /// シリーズスコープ（episode_id=null）は話数 0 として最優先に扱う。
    /// 第 3 キーの <see cref="Involvement.CreditSeq"/> により、同じ話数内で同点になった
    /// ときは「そのエピソードで最初にクレジットされた位置」が早い順に並ぶ
    /// （roles マスタの display_order には依存しない）。
    /// </summary>
    private sealed class FirstCreditAccumulator
    {
        private readonly BuildContext _ctx;
        private DateOnly _bestStart = DateOnly.MaxValue;
        private int _bestEpNo = int.MaxValue;
        private long _bestPos = long.MaxValue;
        private int? _bestSeriesId;

        public FirstCreditAccumulator(BuildContext ctx) => _ctx = ctx;

        public void Offer(Involvement inv)
        {
            var start = _ctx.SeriesStartDate(inv.SeriesId);
            int epNo = inv.EpisodeId is int eid
                ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
                : 0;
            // クレジット階層の位置は (CreditSeq, CreditSubSeq) の辞書順。
            long pos = CombinedCreditPos(inv);
            if (start < _bestStart
                || (start == _bestStart && epNo < _bestEpNo)
                || (start == _bestStart && epNo == _bestEpNo && pos < _bestPos))
            {
                _bestStart = start;
                _bestEpNo = epNo;
                _bestPos = pos;
                _bestSeriesId = inv.SeriesId;
            }
        }

        private Series? BestSeries
            => _bestSeriesId is int id && _ctx.SeriesById.TryGetValue(id, out var s) ? s : null;

        public string SeriesTitle => BestSeries?.Title ?? "";
        public string SeriesUrl => BestSeries is { } s ? PathUtil.SeriesUrl(s.Slug) : "";
        public string SeriesYearLabel
            => BestSeries is { } s ? s.StartDate.Year.ToString() : "";

        /// <summary>ソート用：放送開始日のシリアル値（最大値で未登録を末尾送り）。</summary>
        public long SortStartTicks
            => _bestSeriesId is null ? long.MaxValue : _bestStart.DayNumber;

        public int SortEpNo => _bestSeriesId is null ? int.MaxValue : _bestEpNo;

        /// <summary>ソート用：最早エピソード内でそのエンティティが最初にクレジットされた 階層位置 (CreditSeq, CreditSubSeq) を畳んだ合成キー。 「同じ話数内ではクレジット記載位置順」を厳密に表す。</summary>
        public long SortCreditPos => _bestSeriesId is null ? long.MaxValue : _bestPos;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class LandingModel
    {
        public int StaffCount { get; set; }
        public int VoiceCastCount { get; set; }
    }

    private sealed class StaffModel
    {
        public IReadOnlyList<RoleIndexEntry> Roles { get; set; } = Array.Empty<RoleIndexEntry>();
        public int TotalRoles { get; set; }
        public IReadOnlyList<EntityRow> KanaRows { get; set; } = Array.Empty<EntityRow>();
        /// <summary>初参加順は初参加シリーズごとのセクションに束ねる。</summary>
        public IReadOnlyList<EntitySeriesSection> DebutSections { get; set; } = Array.Empty<EntitySeriesSection>();
        public IReadOnlyList<EntityRow> CountRows { get; set; } = Array.Empty<EntityRow>();
        public int PersonCount { get; set; }
        public int CompanyCount { get; set; }
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class RoleDetailModel
    {
        public string RoleNameJa { get; set; } = "";
        public IReadOnlyList<EntityRow> KanaRows { get; set; } = Array.Empty<EntityRow>();
        /// <summary>初参加順は初参加シリーズごとのセクションに束ねる。</summary>
        public IReadOnlyList<EntitySeriesSection> DebutSections { get; set; } = Array.Empty<EntitySeriesSection>();
        public IReadOnlyList<EntityRow> CountRows { get; set; } = Array.Empty<EntityRow>();
        /// <summary>クラスタ内の歴代の役職名（自分自身を除く）。0 件ならテンプレ側で非表示。</summary>
        public IReadOnlyList<AlternateNameItem> AlternateNames { get; set; } = Array.Empty<AlternateNameItem>();
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class RoleIndexEntry
    {
        public string RoleNameJa { get; set; } = "";
        /// <summary>役職詳細ページへの組み立て済み URL（テンプレ側はこれのみ参照）。</summary>
        public string RoleUrl { get; set; } = "";
        public int PersonCount { get; set; }
        public int CompanyCount { get; set; }
        /// <summary>役職順ソート用：この役職が最も早くクレジットされた放送開始シリアル。</summary>
        public long SortStart { get; set; }
        /// <summary>役職順ソート用：上記の最早シリーズ内話数。</summary>
        public int SortEpNo { get; set; }
        /// <summary>役職順ソート用：上記の最早話数内でのクレジット階層位置 (CreditSeq,CreditSubSeq) 合成キー。</summary>
        public long SortPos { get; set; }
        /// <summary>完全同点時の安定化キー（内部 role_code。表示には用いない）。</summary>
        public string RoleNameKey { get; set; } = "";
    }

    private sealed class AlternateNameItem
    {
        public string RoleNameJa { get; set; } = "";
    }

    /// <summary>人物・企業/団体を 1 リストに混在させるための共通行。 <see cref="EntityKind"/> は "person" / "company"（テンプレ側のバッジ・絞り込み用）。</summary>
    private sealed class EntityRow
    {
        public string EntityKind { get; set; } = "";
        public int EntityId { get; set; }
        public string EntityName { get; set; } = "";
        public string EntityNameKana { get; set; } = "";
        public string EntityUrl { get; set; } = "";
        public int EpisodeCount { get; set; }
        public int SeriesCount { get; set; }
        /// <summary>役職ラベル（スタッフ一覧でのみ使用。役職詳細では空のまま）。</summary>
        public string RolesLabel { get; set; } = "";
        public string FirstSeriesTitle { get; set; } = "";
        public string FirstSeriesUrl { get; set; } = "";
        public string FirstSeriesYearLabel { get; set; } = "";
        public long FirstSortStart { get; set; }
        public int FirstSortEpNo { get; set; }
        /// <summary>最早エピソード内でこのエンティティが最初にクレジットされた階層位置を (CreditSeq, CreditSubSeq) で畳んだ合成キー。</summary>
        public long FirstSortPos { get; set; }
    }

    /// <summary>初参加順タブを「初参加シリーズ」ごとに束ねるセクション。 シリーズ名・年は見出しに集約し、配下行（<see cref="Members"/>）からは出さない。</summary>
    private sealed class EntitySeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        /// <summary>「シリーズ名（年）」整形済み見出しラベル。</summary>
        public string SeriesHeadingLabel { get; set; } = "";
        /// <summary>セクション並び替え用（放送開始日シリアル）。</summary>
        public long SortStart { get; set; }
        public IReadOnlyList<EntityRow> Members { get; set; } = Array.Empty<EntityRow>();
    }

    private sealed class VoiceCastModel
    {
        /// <summary>キャラクター順（既定タブ）：シリーズごとのセクション。</summary>
        public IReadOnlyList<VoiceSeriesSection> CharacterSections { get; set; } = Array.Empty<VoiceSeriesSection>();
        public IReadOnlyList<VoiceCastRow> KanaRows { get; set; } = Array.Empty<VoiceCastRow>();
        /// <summary>初出演順：シリーズごとのセクション。</summary>
        public IReadOnlyList<VoiceSeriesSection> DebutSections { get; set; } = Array.Empty<VoiceSeriesSection>();
        public IReadOnlyList<VoiceCastRow> CountRows { get; set; } = Array.Empty<VoiceCastRow>();
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>声の出演のシリーズ別セクション（キャラクター順・初出演順タブで使用）。 シリーズ名・年は見出しに集約し、配下行からはシリーズ情報を出さない。</summary>
    private sealed class VoiceSeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string SeriesHeadingLabel { get; set; } = "";
        public long SortStart { get; set; }
        public IReadOnlyList<VoiceCastRow> Members { get; set; } = Array.Empty<VoiceCastRow>();
    }

    /// <summary>(声優 × シリーズ × キャラ) 1 組分の表示行。別シリーズ・別キャラはそれぞれ別行になり、 その都度キャラ名・シリーズ名が出る。</summary>
    private sealed class VoiceCastRow
    {
        public string PersonName { get; set; } = "";
        public string PersonNameKana { get; set; } = "";
        public string PersonUrl { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string SeriesYearLabel { get; set; } = "";
        /// <summary>シリーズ放送開始日のシリアル値（並べ替えキー、表示には用いない）。</summary>
        public long SeriesSortStart { get; set; }
        public string CharacterName { get; set; } = "";
        public string CharacterNameKana { get; set; } = "";
        public string CharacterUrl { get; set; } = "";
        /// <summary>キャラクター順タブでキャラを主単位にグルーピングするための ID。</summary>
        public int CharacterId { get; set; }
        /// <summary>当該 (声優 × シリーズ × キャラ) の重複排除済み出演話数（0 = シリーズ全体スコープのみ）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>初出演順タブのタイブレーク用、シリーズ内最早話数（話数不明・全体スコープは 0）。</summary>
        public int EarliestEpNo { get; set; }
        /// <summary>最早話数内でこの (声優 × シリーズ × キャラ) が最初にクレジットされた 階層位置 (CreditSeq, CreditSubSeq) の合成キー。</summary>
        public long EarliestPos { get; set; }
    }
}