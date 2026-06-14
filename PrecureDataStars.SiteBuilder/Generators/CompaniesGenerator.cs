using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>企業索引（/companies/）と企業詳細（/companies/{company_id}/）の生成。</summary>
public sealed class CompaniesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly CompaniesRepository _companiesRepo;
    private readonly CompanyAliasesRepository _aliasesRepo;
    private readonly LogosRepository _logosRepo;
    private readonly RolesRepository _rolesRepo;
    // メンバー履歴セクションで「人物名義 → person_id 解決」をするために
    // person_alias_persons と person_aliases / persons のリポジトリを参照する。
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonsRepository _personsRepo;

    private readonly CreditInvolvementIndex _index;

    private IReadOnlyDictionary<string, Role>? _roleMap;

    /// <summary>person_alias_id → 代表 person_id。 メンバー履歴セクションで人物詳細ページへリンクするために、 PersonsGenerator と同じ仕様で alias → person 解決を行う。</summary>
    private IReadOnlyDictionary<int, int>? _personIdByAlias;
    /// <summary>person_alias_id → PersonAlias（名義の表示名・読み解決用）。</summary>
    private IReadOnlyDictionary<int, PersonAlias>? _personAliasById;
    /// <summary>person_id → Person（メンバー履歴行のソート用に人物読みを参照）。</summary>
    private IReadOnlyDictionary<int, Person>? _personById;

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
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
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

        // メンバー履歴セクション用に、人物名義 → 人物 ID の逆引き辞書と
        // 人物読み・名義表示テキストの参照辞書を 1 回だけ全件ロードする。
        // 共有名義（1 alias → 複数 person）は最初の person を採用（メンバー履歴で複数人物に
        // 別個リンクすると視覚的にうるさいため、メイン人物 1 名にだけリンクする方針）。
        var allPersonAliases = await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        _personAliasById = allPersonAliases.ToDictionary(a => a.AliasId);
        var allPersons = await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        _personById = allPersons.ToDictionary(p => p.PersonId);
        // person_alias_persons.GetAllAsync で全件を読み、alias_id → 先頭 person_id の辞書化。
        // 共有名義の場合は PersonSeq 昇順で先頭を採用（メンバー履歴では代表 1 名にだけリンクする運用）。
        var allAliasPersons = await _personAliasPersonsRepo.GetAllAsync(ct).ConfigureAwait(false);
        _personIdByAlias = allAliasPersons
            .GroupBy(ap => ap.AliasId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.PersonSeq).First().PersonId);

        // 企業索引は「クリエーター > スタッフ」（/creators/staff/）に集約。
        // 本ジェネレータは企業・団体単体の詳細ページ（/companies/{id}/）生成に専念する。

        // 詳細ページ。
        foreach (var co in companies)
        {
            var aliases = aliasesByCompany.TryGetValue(co.CompanyId, out var lst) ? lst : new List<CompanyAlias>();
            await GenerateDetailAsync(co, aliases, logosByAlias, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"companies: {companies.Count} ページ");
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

        // 各ロゴについて、当該ロゴがクレジットされたシリーズと話数範囲を
        foreach (var av in aliasViews)
        {
            foreach (var lv in av.Logos)
            {
                lv.CreditRangeLabel = BuildLogoCreditRangeLabel(lv.LogoId);
            }
        }

        // 全関与（alias 経由 + ロゴ経由）を集めて役職別グループ化（メタ説明の組み立て用）。
        var allInvolvements = CollectAllInvolvements(aliases, logosByAlias).ToList();
        var groups = BuildCompanyInvolvementGroups(allInvolvements);

        // クレジット履歴の表示用には「その時の屋号」単位のセクション（初登場順）に分けて組み立てる。
        var involvementSections = BuildAliasInvolvementSections(aliases, logosByAlias);

        // メンバー履歴セクションのデータを組み立てる。
        // 当該企業の全屋号を所属としてクレジットされた人物 Involvement を集め、
        // (人物 × 所属屋号 × シリーズ) の 3 軸で 1 行ずつまとめて、シリーズ放送開始日 → 人物読み の順で並べる。
        var memberHistory = BuildMemberHistory(aliases);

        var content = new CompanyDetailModel
        {
            Company = new CompanyView
            {
                CompanyId = company.CompanyId,
                DisplayName = displayName,
                Name = company.Name,
                NameKana = company.NameKana ?? "",
                NameEn = company.NameEn ?? "",
                FoundedDate = JpDateFormat.NullableDate(company.FoundedDate),
                DissolvedDate = JpDateFormat.NullableDate(company.DissolvedDate),
                Notes = company.Notes ?? "",
                OfficialUrl = company.OfficialUrl ?? "",
                XUrl = company.XUrl ?? "",
                InstagramUrl = company.InstagramUrl ?? "",
                YoutubeUrl = company.YoutubeUrl ?? ""
            },
            Aliases = aliasViews,
            InvolvementSections = involvementSections,
            MemberHistory = memberHistory,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        // 企業詳細の構造化データは Schema.org の Organization 型。
        string baseUrl = _ctx.Config.BaseUrl;
        string companyUrl = PathUtil.CompanyUrl(company.CompanyId);
        var alternateNames = aliasViews
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !string.Equals(n, company.Name, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // MetaDescription を実データから動的構築する。
        // 「{会社名}は、プリキュアシリーズで{役職1}({N作品})・{役職2}({N作品})などを担当した企業・団体。」を骨格にする。
        var metaDescription = BuildCompanyMetaDescription(displayName, groups);

        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["name"] = displayName,
            ["description"] = metaDescription
        };
        if (alternateNames.Count > 0) jsonLdDict["alternateName"] = alternateNames;
        if (company.FoundedDate.HasValue) jsonLdDict["foundingDate"] = company.FoundedDate.Value.ToString("yyyy-MM-dd");
        if (company.DissolvedDate.HasValue) jsonLdDict["dissolutionDate"] = company.DissolvedDate.Value.ToString("yyyy-MM-dd");
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + companyUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = company.Name,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
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
    /// 企業・団体詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// 構成：「{会社名}は、プリキュアシリーズで{役職1}({N作品})・{役職2}({N作品})などを担当した企業・団体。」を骨格に、
    /// 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。
    /// 役職は <see cref="InvolvementGroup.Count"/> 降順（担当エピソード数の多い順）で最大 3 件。
    /// 関与役職が 1 件も無い場合は、定型文「{会社名}のプリキュア関連クレジット一覧です。」にフォールバック。
    /// </summary>
    private static string BuildCompanyMetaDescription(
        string displayName,
        IReadOnlyList<InvolvementGroup> involvementGroups)
    {
        const int targetMaxChars = 140;

        if (involvementGroups.Count == 0)
        {
            return $"{displayName}のプリキュア関連クレジット一覧です。";
        }

        // 担当話数の多い順で上位役職を取り出し、最大 3 件まで採用する。
        var ordered = involvementGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.RoleLabel) && g.RoleLabel != "(役職未設定)")
            .OrderByDescending(g => g.Count)
            .Take(3)
            .ToList();

        if (ordered.Count == 0)
        {
            return $"{displayName}のプリキュア関連クレジット一覧です。";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(displayName).Append("は、プリキュアシリーズで");

        int appended = 0;
        foreach (var g in ordered)
        {
            // TV 系の担当は「話」、映画系の担当は「本」で表記し、両方あれば「N話・M本」併記。
            if (g.Count <= 0) continue;
            var fragment = $"{g.RoleLabel}({g.CountLabel.Replace(" ", "")})";
            // 末尾「などを担当した企業・団体。」(13 字) を残せるかを判定する。
            int suffixLen = 13;
            int joinerLen = appended > 0 ? 1 : 0;
            if (sb.Length + joinerLen + fragment.Length + suffixLen > targetMaxChars) break;
            if (appended > 0) sb.Append('・');
            sb.Append(fragment);
            appended++;
        }

        if (appended == 0)
        {
            return $"{displayName}のプリキュア関連クレジット一覧です。";
        }

        sb.Append("などを担当した企業・団体。");
        return sb.ToString();
    }

    /// <summary>
    /// 企業に紐付く全 alias の関与（直接 + leading）+ 配下ロゴの関与（LOGO エントリ）を 1 シーケンスに合算。
    /// <see cref="InvolvementKind.Member"/>（所属屋号としての参照）は本企業詳細の「クレジット履歴」セクションには
    /// 含めない。メンバー所属はクレジットの主体ではなく「人物の所属先として登場した記録」のため、
    /// クレジット履歴と一緒に表示すると意味が混在する。メンバー所属は <see cref="CollectMemberInvolvements"/>
    /// で別途集計し、「メンバー履歴」セクションで一覧化する。
    /// </summary>
    private IEnumerable<Involvement> CollectAllInvolvements(
        IReadOnlyList<CompanyAlias> aliases,
        IReadOnlyDictionary<int, IReadOnlyList<Logo>> logosByAlias)
    {
        foreach (var a in aliases)
        {
            if (_index.ByCompanyAlias.TryGetValue(a.AliasId, out var direct))
            {
                foreach (var inv in direct)
                {
                    // Member 種別は「メンバー履歴」セクション専用なので、ここでは除外する。
                    if (inv.Kind == InvolvementKind.Member) continue;
                    yield return inv;
                }
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

    /// <summary>当該企業（その全屋号）を所属としてクレジットされた人物名義の Involvement だけを集める。</summary>
    private IEnumerable<Involvement> CollectMemberInvolvements(IReadOnlyList<CompanyAlias> aliases)
    {
        foreach (var a in aliases)
        {
            if (_index.ByCompanyAlias.TryGetValue(a.AliasId, out var invs))
            {
                foreach (var inv in invs)
                {
                    if (inv.Kind == InvolvementKind.Member) yield return inv;
                }
            }
        }
    }

    /// <summary>指定ロゴ ID がクレジットされたシリーズ・話数範囲の小書きキャプションを組み立てる。</summary>
    private string BuildLogoCreditRangeLabel(int logoId)
    {
        if (!_index.ByLogo.TryGetValue(logoId, out var invs) || invs.Count == 0)
            return "";

        // シリーズ単位に集約：シリーズ ID → (シリーズ全体スコープか, 話数集合)。
        var bySeries = new Dictionary<int, (bool HasSeriesScope, HashSet<int> EpisodeNos)>();
        foreach (var inv in invs)
        {
            if (!bySeries.TryGetValue(inv.SeriesId, out var entry))
            {
                entry = (false, new HashSet<int>());
                bySeries[inv.SeriesId] = entry;
            }
            if (inv.EpisodeId is int eid)
            {
                var ep = _ctx.LookupEpisode(inv.SeriesId, eid);
                if (ep is not null) entry.EpisodeNos.Add(ep.SeriesEpNo);
                bySeries[inv.SeriesId] = entry;
            }
            else
            {
                entry.HasSeriesScope = true;
                bySeries[inv.SeriesId] = entry;
            }
        }

        // 放送開始日昇順でシリーズを並べ、各シリーズの表示文字列を作る。
        // 後段：略称（series.title_short）は生成・UI ともに使わない。常に正式タイトル。
        var parts = new List<string>();
        foreach (var (seriesId, entry) in bySeries.OrderBy(kv => _ctx.SeriesStartDate(kv.Key)))
        {
            if (!_ctx.SeriesById.TryGetValue(seriesId, out var series)) continue;
            string title = series.Title;

            // シリーズ全体スコープが含まれていれば優先（最も広い範囲表現）。
            if (entry.HasSeriesScope)
            {
                parts.Add($"{title}（シリーズ全体）");
                continue;
            }

            if (entry.EpisodeNos.Count == 0) continue;

            // シリーズ内全話か判定。
            var allEpNos = _ctx.EpisodesBySeries.TryGetValue(seriesId, out var allEps)
                ? allEps.Select(e => e.SeriesEpNo).ToList()
                : new List<int>();
            bool isAll = allEpNos.Count > 0 && entry.EpisodeNos.SetEquals(allEpNos);

            if (isAll)
            {
                parts.Add($"{title}（全話）");
            }
            else
            {
                string range = EpisodeRangeCompressor.Compress(entry.EpisodeNos);
                parts.Add($"{title} {range}");
            }
        }

        return string.Join("、", parts);
    }

    /// <summary>
    /// 当該企業のメンバー履歴を「屋号（所属ブランド）→ 役職 → 人物 → 作品」の入れ子で組み立てる。
    /// クレジット履歴セクション（屋号 → 役職）と視覚的に揃え、人物行ごとに屋号プレフィクスを
    /// 繰り返さない構造にする。屋号セクションの並びはその屋号での最初の関与日昇順。
    /// </summary>
    private IReadOnlyList<MemberHistoryAliasSection> BuildMemberHistory(IReadOnlyList<CompanyAlias> aliases)
    {
        var members = CollectMemberInvolvements(aliases).ToList();
        if (members.Count == 0) return Array.Empty<MemberHistoryAliasSection>();
        if (_personIdByAlias is null || _personAliasById is null || _personById is null)
            return Array.Empty<MemberHistoryAliasSection>();

        // (AffiliationCompanyAliasId, RoleCode, PersonAliasId, SeriesId) → 話数集合（シリーズ全体スコープなら IsSeriesScope=true）。
        var grouped = new Dictionary<(int AffId, string RoleCode, int PersonAliasId, int SeriesId), (bool IsSeriesScope, HashSet<int> EpNos)>();
        // (AffiliationCompanyAliasId, RoleCode) → その役職グループ内の最早クレジット位置キー。
        // 屋号内の役職並びを「初参加」昇順にするためのアキュムレータ。生 Involvement から
        // CreditSeq / CreditSubSeq を保持したまま畳み込む（grouped 化すると階層位置が落ちるため別管理）。
        var earliestByRole = new Dictionary<(int AffId, string RoleCode), (long StartDay, int EpNo, long Pos)>();
        // (AffiliationCompanyAliasId, RoleCode, PersonId) → その人物の最早クレジット位置キー。
        // メンバー履歴の人物行を「初クレジット順（シリーズ → 話数 → クレジット階層位置）」に並べるために使う。
        // person 単位（名義違いは同一人物に畳む）で集計する。
        var earliestByPerson = new Dictionary<(int AffId, string RoleCode, int PersonId), (long StartDay, int EpNo, long Pos)>();
        foreach (var inv in members)
        {
            if (inv.PersonAliasId is not int paid) continue;
            if (inv.AffiliationCompanyAliasId is not int affId) continue;

            var key = (affId, inv.RoleCode ?? "", paid, inv.SeriesId);
            if (!grouped.TryGetValue(key, out var entry))
            {
                entry = (false, new HashSet<int>());
                grouped[key] = entry;
            }
            if (inv.EpisodeId is int eid)
            {
                var ep = _ctx.LookupEpisode(inv.SeriesId, eid);
                if (ep is not null) entry.EpNos.Add(ep.SeriesEpNo);
                grouped[key] = entry;
            }
            else
            {
                entry.IsSeriesScope = true;
                grouped[key] = entry;
            }

            // (屋号, 役職) 単位の最早クレジット位置を畳み込む。FirstCreditAccumulator と同じ合成基準
            // （シリーズ放送開始日 → シリーズ内話数[series-scope は 0 で最優先] → クレジット階層位置）。
            var roleKey = (affId, inv.RoleCode ?? "");
            long day = _ctx.SeriesStartDate(inv.SeriesId).DayNumber;
            int epNo = inv.EpisodeId is int eid2
                ? (_ctx.LookupEpisode(inv.SeriesId, eid2)?.SeriesEpNo ?? int.MaxValue)
                : 0;
            long pos = (long)inv.CreditSeq * 1_000_000L + inv.CreditSubSeq;
            var cand = (day, epNo, pos);
            if (!earliestByRole.TryGetValue(roleKey, out var cur) || CompareCreditKey(cand, cur) < 0)
            {
                earliestByRole[roleKey] = cand;
            }

            // (屋号, 役職, 人物) 単位の最早クレジット位置（人物行の初クレジット順並べ替え用）。
            if (_personIdByAlias.TryGetValue(paid, out var pid))
            {
                var personKey = (affId, inv.RoleCode ?? "", pid);
                if (!earliestByPerson.TryGetValue(personKey, out var pcur) || CompareCreditKey(cand, pcur) < 0)
                {
                    earliestByPerson[personKey] = cand;
                }
            }
        }

        // alias_id → 屋号名（自社内の屋号のみ参照する想定なので、aliases リストから直接マップを作る）。
        var aliasNameById = aliases.ToDictionary(a => a.AliasId, a => a.Name ?? "");

        // 屋号 → 人物 → 役職 → 作品 の入れ子を一度に組み立てるための中間バッファ。人物は person 単位
        // （名義違いは同一人物に畳む）。屋号セクションの並べ替え用に最初の関与日も併せて記録する。
        var byAlias = new Dictionary<int, AliasBucket>();
        foreach (var ((affId, roleCode, paid, seriesId), entry) in grouped)
        {
            // 名義 → 人物 ID の解決。共有名義は代表 1 名へリンク。
            if (!_personIdByAlias.TryGetValue(paid, out var personId)) continue;
            if (!_ctx.SeriesById.ContainsKey(seriesId)) continue;

            // 屋号バケット。
            if (!byAlias.TryGetValue(affId, out var aliasBucket))
            {
                aliasBucket = new AliasBucket
                {
                    AliasName = aliasNameById.TryGetValue(affId, out var an) ? an : ""
                };
                byAlias[affId] = aliasBucket;
            }

            // 人物バケット（person 単位。名義違いはここで 1 人にまとめる）。
            if (!aliasBucket.Persons.TryGetValue(personId, out var personBucket))
            {
                string personNameKana = _personById.TryGetValue(personId, out var p) ? (p.FullNameKana ?? p.FullName ?? "") : "";
                personBucket = new PersonBucket { PersonId = personId, PersonNameKana = personNameKana };
                aliasBucket.Persons[personId] = personBucket;
            }

            // 代表名義の選定用：名義（person_alias）ごとのクレジット実績（話数）を積む。
            int aliasWeight = entry.EpNos.Count > 0 ? entry.EpNos.Count : (entry.IsSeriesScope ? 1 : 0);
            personBucket.AliasWeights[paid] = personBucket.AliasWeights.TryGetValue(paid, out var aw) ? aw + aliasWeight : aliasWeight;

            // 役職バケット（人物の下）。役職並べ替え用の最早クレジット位置キーも併せて確定する。
            if (!personBucket.Roles.TryGetValue(roleCode, out var roleBucket))
            {
                string roleNameJa = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                    : (_roleMap is not null && _roleMap.TryGetValue(roleCode, out var rr) ? (rr.NameJa ?? roleCode) : roleCode);
                var earliest = earliestByPerson.TryGetValue((affId, roleCode, personId), out var ek)
                    ? ek
                    : (long.MaxValue, int.MaxValue, long.MaxValue);
                roleBucket = new PersonRoleBucket { RoleCode = roleCode, RoleNameJa = roleNameJa, EarliestCreditKey = earliest };
                personBucket.Roles[roleCode] = roleBucket;
            }

            // 作品をシリーズ単位でマージ（名義違いの同一シリーズは話数を合算）。表示 DTO 化は確定時に行う。
            if (!roleBucket.WorksBySeries.TryGetValue(seriesId, out var ws))
            {
                ws = (false, new HashSet<int>());
            }
            if (entry.IsSeriesScope) ws.IsSeriesScope = true;
            foreach (var no in entry.EpNos) ws.EpNos.Add(no);
            roleBucket.WorksBySeries[seriesId] = ws;

            // 屋号セクションの並べ替え用：その屋号での最初の関与日（エピソード放送日 / シリーズ開始日）。
            DateTime at;
            if (!entry.IsSeriesScope && entry.EpNos.Count > 0
                && _ctx.EpisodesBySeries.TryGetValue(seriesId, out var epsForAt)
                && epsForAt.Where(e => entry.EpNos.Contains(e.SeriesEpNo)).Select(e => (DateTime?)e.OnAirAt).Min() is { } minOnAir)
            {
                at = minOnAir;
            }
            else
            {
                at = _ctx.SeriesStartDate(seriesId).ToDateTime(TimeOnly.MinValue);
            }
            if (at < aliasBucket.FirstAt) aliasBucket.FirstAt = at;
        }

        // 入れ子 DTO へ確定。屋号＝最初の関与日昇順 / 人物＝初クレジット順（役職横断の最早クレジット位置）/
        // 役職＝初クレジット順（出世・変遷が追える）/ 作品＝シリーズ開始日昇順（1 行ずつ・話数を可視表示）。
        // 担当回数ピル：人物＝役職横断の distinct（シリーズ×話数）と映画本数 / 役職＝その役職での同集計。
        var sections = new List<(DateTime FirstAt, MemberHistoryAliasSection Section)>(byAlias.Count);
        foreach (var aliasBucket in byAlias.Values)
        {
            var persons = aliasBucket.Persons.Values
                .Select(pb =>
                {
                    // 役職を初クレジット順に。各役職の作品（シリーズ単位）を開始日順で 1 行ずつ。
                    var roleGroups = pb.Roles.Values
                        .OrderBy(rb => rb.EarliestCreditKey)
                        .Select(rb =>
                        {
                            var works = rb.WorksBySeries
                                .OrderBy(kv => _ctx.SeriesStartDate(kv.Key))
                                .Select(kv => BuildMemberHistoryWork(kv.Key, kv.Value.IsSeriesScope, kv.Value.EpNos))
                                .Where(w => w is not null)
                                .Select(w => w!)
                                .ToList();
                            // 役職の担当回数：TV＝その役職での distinct 話数合計、映画＝本数。
                            int roleEp = 0, roleMv = 0;
                            foreach (var kv in rb.WorksBySeries)
                            {
                                if (IsMovieKindSeries(kv.Key)) roleMv++;
                                else roleEp += kv.Value.EpNos.Count;
                            }
                            return (rb.EarliestCreditKey, Group: new MemberHistoryRoleGroup
                            {
                                RoleNameJa = rb.RoleNameJa,
                                EpisodeCount = roleEp,
                                MovieCount = roleMv,
                                Works = works
                            });
                        })
                        .ToList();

                    // 人物の総担当回数：全役職を横断した distinct（シリーズ×話数）と映画本数。
                    var tvEps = new HashSet<(int SeriesId, int EpNo)>();
                    var mvSeries = new HashSet<int>();
                    foreach (var rb in pb.Roles.Values)
                        foreach (var kv in rb.WorksBySeries)
                        {
                            if (IsMovieKindSeries(kv.Key)) mvSeries.Add(kv.Key);
                            else foreach (var no in kv.Value.EpNos) tvEps.Add((kv.Key, no));
                        }

                    // 代表名義＝クレジット実績（話数）最多の名義。同数は alias_id の小さい方（登録が早い方）。
                    int repPaid = pb.AliasWeights
                        .OrderByDescending(kv => kv.Value)
                        .ThenBy(kv => kv.Key)
                        .First().Key;
                    string repName = _personAliasById.TryGetValue(repPaid, out var ra) ? (ra.Name ?? "(名義不明)") : "(名義不明)";

                    // 人物の並べ替えキー＝役職横断の最早クレジット位置（初クレジット順）。
                    var personEarliest = roleGroups.Count > 0
                        ? roleGroups.Min(x => x.EarliestCreditKey)
                        : (long.MaxValue, int.MaxValue, long.MaxValue);

                    return (Earliest: personEarliest, Row: new MemberHistoryPersonRow
                    {
                        PersonId = pb.PersonId,
                        PersonName = repName,
                        EpisodeCount = tvEps.Count,
                        MovieCount = mvSeries.Count,
                        Roles = roleGroups.Select(x => x.Group).ToList()
                    });
                })
                .OrderBy(x => x.Earliest)
                .Select(x => x.Row)
                .ToList();

            sections.Add((aliasBucket.FirstAt, new MemberHistoryAliasSection
            {
                AliasName = aliasBucket.AliasName,
                Persons = persons
            }));
        }

        return sections.OrderBy(s => s.FirstAt).Select(s => s.Section).ToList();
    }

    /// <summary>メンバー履歴の作品 1 行（シリーズ単位でマージ済み）を表示用 DTO に変換する。
    /// 1 行ずつ出し、TV は担当話数の圧縮表記（例「#1〜4, 8」、全話なら "(全話)"）を可視表示、映画は話数なし。
    /// シリーズ全体スコープ（episode_id なし）は「（シリーズ全体）」。シリーズ未解決時は null（呼び出し側で除外）。</summary>
    private MemberHistoryWork? BuildMemberHistoryWork(int seriesId, bool isSeriesScope, HashSet<int> epNos)
    {
        if (!_ctx.SeriesById.TryGetValue(seriesId, out var series)) return null;
        bool isMovie = IsMovieKindSeries(seriesId);
        string rangeLabel;
        if (isMovie)
        {
            rangeLabel = "";
        }
        else if (isSeriesScope || epNos.Count == 0)
        {
            rangeLabel = "（シリーズ全体）";
        }
        else
        {
            var allEpNos = _ctx.EpisodesBySeries.TryGetValue(seriesId, out var allEps)
                ? allEps.Select(e => e.SeriesEpNo).ToList()
                : new List<int>();
            bool isAll = allEpNos.Count > 0 && epNos.SetEquals(allEpNos);
            rangeLabel = isAll ? "(全話)" : EpisodeRangeCompressor.Compress(epNos);
        }
        return new MemberHistoryWork
        {
            SeriesTitle = series.Title,
            SeriesSlug = series.Slug,
            SeriesId = seriesId,
            SeriesStartYearLabel = series.StartDate.Year.ToString(),
            IsMovie = isMovie,
            RangeLabel = rangeLabel
        };
    }

    /// <summary>当該シリーズが映画系（series_kinds.credit_attach_to='SERIES'。MOVIE / MOVIE_SHORT / SPRING / EVENT）かを判定する。
    /// メンバー履歴の担当回数集計で「TV 話（📺）」と「映画 本（🎥）」を分けるのに使う。</summary>
    private bool IsMovieKindSeries(int seriesId)
        => _ctx.SeriesById.TryGetValue(seriesId, out var s)
           && _ctx.SeriesKindByCode.TryGetValue(s.KindCode, out var sk)
           && string.Equals(sk.CreditAttachTo, "SERIES", StringComparison.Ordinal);

    // メンバー履歴の入れ子を一時的に組み立てるための可変バケット（DTO 確定前の中間構造）。
    private sealed class AliasBucket
    {
        public string AliasName = "";
        public DateTime FirstAt = DateTime.MaxValue;
        /// <summary>屋号配下の人物（person 単位。名義違いは 1 人に畳む）。</summary>
        public Dictionary<int, PersonBucket> Persons { get; } = new();
    }

    private sealed class PersonBucket
    {
        public int PersonId;
        public string PersonNameKana = "";
        /// <summary>名義（person_alias_id）ごとのクレジット実績（話数の重み）。代表名義の選定に使う。</summary>
        public Dictionary<int, int> AliasWeights { get; } = new();
        /// <summary>この人物が当該屋号で担当した役職（役職コード → 役職バケット）。</summary>
        public Dictionary<string, PersonRoleBucket> Roles { get; } = new(StringComparer.Ordinal);
    }

    private sealed class PersonRoleBucket
    {
        public string RoleCode = "";
        public string RoleNameJa = "";
        /// <summary>この (屋号, 人物, 役職) の最早クレジット位置キー（役職の初クレジット順並べ替え用）。 (シリーズ放送開始日のシリアル値, シリーズ内話数, クレジット階層位置) の辞書順。</summary>
        public (long StartDay, int EpNo, long Pos) EarliestCreditKey;
        /// <summary>シリーズ単位でマージした担当作品（名義違いの同一シリーズは話数を合算）。 (IsSeriesScope, 話数集合)。</summary>
        public Dictionary<int, (bool IsSeriesScope, HashSet<int> EpNos)> WorksBySeries { get; } = new();
    }

    /// <summary>会社の全関与を役職別にグルーピング。</summary>
    /// <summary>企業・団体に紐付く関与情報を、役職別 → シリーズ単位の話数圧縮表記に編成する。 役職別 → シリーズ単位 1 行 + 話数を「#1〜4, 8」のように圧縮表示する。 全話担当のときは「(全話)」マークを付加。シリーズ全体スコープは別行として残す。 企業・団体に声優役は通常存在しないので CharacterNames は常に空。</summary>
    /// <summary>
    /// クレジット履歴を「その時の屋号（alias）」単位のセクションに分けて組み立てる。
    /// セクションの並びは屋号の初登場（最も早いクレジットの放送日 / シリーズ開始日）順。
    /// 各セクション内は従来どおり役職別グループ。ロゴ経由の関与はロゴを保有する屋号に帰属させる。
    /// 正式名称（companies.name）ではなく実際にクレジットされた屋号で見せるための分割で、
    /// 同一企業でも屋号が変われば別セクションになる。
    /// </summary>
    private IReadOnlyList<AliasInvolvementSection> BuildAliasInvolvementSections(
        IReadOnlyList<CompanyAlias> aliases,
        IReadOnlyDictionary<int, IReadOnlyList<Logo>> logosByAlias)
    {
        var sections = new List<(DateTime FirstAt, AliasInvolvementSection Section)>();
        foreach (var a in aliases)
        {
            var invs = new List<Involvement>();
            if (_index.ByCompanyAlias.TryGetValue(a.AliasId, out var direct))
            {
                // Member 種別（所属屋号としての参照）はメンバー履歴セクション専用なので除外。
                invs.AddRange(direct.Where(i => i.Kind != InvolvementKind.Member));
            }
            if (logosByAlias.TryGetValue(a.AliasId, out var logos))
            {
                foreach (var lg in logos)
                {
                    if (_index.ByLogo.TryGetValue(lg.LogoId, out var logoInvs)) invs.AddRange(logoInvs);
                }
            }
            if (invs.Count == 0) continue;

            var aliasGroups = BuildCompanyInvolvementGroups(invs);
            if (aliasGroups.Count == 0) continue;

            // 初登場時刻：エピソード単位の関与は放送日時、シリーズ全体スコープはシリーズ開始日。
            DateTime firstAt = DateTime.MaxValue;
            foreach (var inv in invs)
            {
                DateTime at;
                if (inv.EpisodeId is int eid && _ctx.LookupEpisode(inv.SeriesId, eid) is { } ep)
                {
                    at = ep.OnAirAt;
                }
                else
                {
                    at = _ctx.SeriesStartDate(inv.SeriesId).ToDateTime(TimeOnly.MinValue);
                }
                if (at < firstAt) firstAt = at;
            }

            sections.Add((firstAt, new AliasInvolvementSection
            {
                AliasName = a.Name,
                Groups = aliasGroups
            }));
        }
        return sections.OrderBy(s => s.FirstAt).Select(s => s.Section).ToList();
    }

    /// <summary>
    /// 関与群の「最早クレジット位置」キーを畳み込んで返す（初参加順ソート用）。
    /// CreatorsGenerator.FirstCreditAccumulator と同じ合成基準：
    /// シリーズ放送開始日のシリアル値 → シリーズ内話数（series-scope = 0 で最優先） → クレジット階層位置
    /// （CreditSeq, CreditSubSeq の辞書順）。roles.display_order には依存しない。
    /// </summary>
    private (long StartDay, int EpNo, long Pos) EarliestCreditKey(IEnumerable<Involvement> invs)
    {
        var best = (StartDay: long.MaxValue, EpNo: int.MaxValue, Pos: long.MaxValue);
        foreach (var inv in invs)
        {
            long day = _ctx.SeriesStartDate(inv.SeriesId).DayNumber;
            int epNo = inv.EpisodeId is int eid
                ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
                : 0;
            long pos = (long)inv.CreditSeq * 1_000_000L + inv.CreditSubSeq;
            var cand = (day, epNo, pos);
            if (CompareCreditKey(cand, best) < 0) best = cand;
        }
        return best;
    }

    /// <summary>最早クレジット位置キーの辞書順比較（StartDay → EpNo → Pos）。</summary>
    private static int CompareCreditKey(
        (long StartDay, int EpNo, long Pos) a,
        (long StartDay, int EpNo, long Pos) b)
    {
        int c = a.StartDay.CompareTo(b.StartDay);
        if (c != 0) return c;
        c = a.EpNo.CompareTo(b.EpNo);
        if (c != 0) return c;
        return a.Pos.CompareTo(b.Pos);
    }

    private IReadOnlyList<InvolvementGroup> BuildCompanyInvolvementGroups(IReadOnlyList<Involvement> all)
    {
        if (all.Count == 0) return Array.Empty<InvolvementGroup>();

        var groups = new List<InvolvementGroup>();
        // 役職グループの並びは「その役職で当該屋号が最初にクレジットされた位置」（初参加）昇順。
        // 屋号セクション自体の初登場順（BuildAliasInvolvementSections）と同じ思想で、
        // roles.display_order ではなくクレジット出現の早さで役職を並べる。
        foreach (var roleGroup in all.GroupBy(i => i.RoleCode).OrderBy(g => EarliestCreditKey(g)))
        {
            string roleCode = roleGroup.Key;
            string roleLabel = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                : (_roleMap!.TryGetValue(roleCode, out var r) ? (r.NameJa ?? roleCode) : roleCode);

            var seriesRows = new List<InvolvementSeriesRow>();
            int episodeCountTotal = 0;
            int movieCountTotal = 0;

            foreach (var bySeries in roleGroup
                .GroupBy(i => i.SeriesId)
                .OrderBy(sg => _ctx.SeriesStartDate(sg.Key)))
            {
                if (!_ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;

                // このシリーズが「映画系（series_kinds.credit_attach_to='SERIES'）」か判定。
                // MOVIE / MOVIE_SHORT / SPRING / EVENT が該当。当該シリーズへの関与は何件あっても 1 本としてカウント。
                bool isMovieKindSeries = _ctx.SeriesKindByCode.TryGetValue(series.KindCode, out var sk)
                                         && string.Equals(sk.CreditAttachTo, "SERIES", StringComparison.Ordinal);

                var episodeNos = new HashSet<int>();
                bool hasSeriesScope = false;
                foreach (var inv in bySeries)
                {
                    if (inv.EpisodeId is int eid)
                    {
                        var ep = _ctx.LookupEpisode(bySeries.Key, eid);
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

                // 映画系シリーズ（credit_attach_to='SERIES'）は全クレジットが series 直付けの
                // 「シリーズ全体」相当なので「（シリーズ全体）」ラベルを併記しない（見出しの「N 本」表記＋
                // シリーズ名で自明）。TV 系で稀に出る series-scope だけラベルを出してエピソード単位行と区別する。
                if (hasSeriesScope)
                {
                    seriesRows.Add(new InvolvementSeriesRow
                    {
                        SeriesSlug = series.Slug,
                        SeriesTitle = series.Title,
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
                        RangeLabel = isMovieKindSeries ? "" : "（シリーズ全体）",
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
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
                        RangeLabel = rangeLabel,
                        IsAllEpisodes = isAll,
                        CharacterNames = ""
                    });
                }

                // 担当量カウント：シリーズ種別で「話」と「本」を分けて加算。
                // 映画系（credit_attach_to='SERIES'）：当該シリーズに関与が 1 件以上あれば 1 本としてカウント。
                // TV 系（credit_attach_to='EPISODE'）：エピソード単位の関与話数を加算。
                if (isMovieKindSeries)
                {
                    if (hasSeriesScope || episodeNos.Count > 0) movieCountTotal += 1;
                }
                else
                {
                    episodeCountTotal += episodeNos.Count;
                }
            }

            if (seriesRows.Count == 0) continue;

            groups.Add(new InvolvementGroup
            {
                RoleCode = roleCode,
                RoleLabel = roleLabel,
                SeriesRows = seriesRows,
                EpisodeCount = episodeCountTotal,
                MovieCount = movieCountTotal,
                HasCharacterColumn = false
            });
        }
        return groups;
    }

    /// <summary>企業の屋号（alias）を時系列順に並べ、各 alias 配下のロゴリストも添える。</summary>
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
            ValidFrom = JpDateFormat.NullableDate(a.ValidFrom),
            ValidTo = JpDateFormat.NullableDate(a.ValidTo),
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

    // ─── テンプレ用 DTO 群 ───

    private sealed class CompanyDetailModel
    {
        public CompanyView Company { get; set; } = new();
        public IReadOnlyList<CompanyAliasView> Aliases { get; set; } = Array.Empty<CompanyAliasView>();
        /// <summary>クレジット履歴。「その時の屋号」単位のセクション（初登場順）に分かれ、
        /// 各セクション内は役職別グループ → シリーズ行の従来構造。</summary>
        public IReadOnlyList<AliasInvolvementSection> InvolvementSections { get; set; } = Array.Empty<AliasInvolvementSection>();
        /// <summary>メンバー履歴。 当該企業の屋号を所属としてクレジットされた人物名義の一覧を 屋号 → 役職 → 人物 → 作品 の入れ子で持つ。 屋号セクションはその屋号での最初の関与日昇順。 0 件の場合はテンプレ側でセクション自体を非表示にする。</summary>
        public IReadOnlyList<MemberHistoryAliasSection> MemberHistory { get; set; } = Array.Empty<MemberHistoryAliasSection>();
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の h1 ブロック直後に独立段落で表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>クレジット履歴の屋号別セクション 1 件（屋号名 + その屋号での役職別グループ群）。</summary>
    private sealed class AliasInvolvementSection
    {
        /// <summary>クレジットされた屋号名（その時代の名乗り）。</summary>
        public string AliasName { get; set; } = "";
        /// <summary>当該屋号での役職別グループ（その役職での初参加＝最早クレジット位置順）。</summary>
        public IReadOnlyList<InvolvementGroup> Groups { get; set; } = Array.Empty<InvolvementGroup>();
    }

    /// <summary>メンバー履歴：屋号（所属ブランド）1 件分のセクション。見出し＝屋号名、配下に人物ブロック。</summary>
    private sealed class MemberHistoryAliasSection
    {
        /// <summary>所属屋号名（その時代の名乗り）。クレジット履歴の屋号見出しと同じ意匠で出す。</summary>
        public string AliasName { get; set; } = "";
        /// <summary>当該屋号に属する人物ブロック（初クレジット順）。</summary>
        public IReadOnlyList<MemberHistoryPersonRow> Persons { get; set; } = Array.Empty<MemberHistoryPersonRow>();
    }

    /// <summary>メンバー履歴：屋号内の人物 1 名分のブロック。見出し＝代表名義＋担当回数ピル、配下に役職別グループ。</summary>
    private sealed class MemberHistoryPersonRow
    {
        /// <summary>人物 ID（リンク先 /persons/{id}/）。共有名義時は代表 person を採用。</summary>
        public int PersonId { get; set; }
        /// <summary>表示名＝この屋号でのクレジット実績が最多の名義（名義違いは 1 人に統合）。</summary>
        public string PersonName { get; set; } = "";
        /// <summary>この屋号での総担当回数（TV＝役職横断の distinct 話数、📺 ピル）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>この屋号での総担当本数（映画系シリーズの distinct 本数、🎥 ピル）。</summary>
        public int MovieCount { get; set; }
        /// <summary>この人物が当該屋号で担当した役職グループ（初クレジット順＝出世・変遷順）。</summary>
        public IReadOnlyList<MemberHistoryRoleGroup> Roles { get; set; } = Array.Empty<MemberHistoryRoleGroup>();
    }

    /// <summary>メンバー履歴：人物の下の役職 1 件分のグループ。見出し＝役職名＋担当回数ピル、配下に作品行（1 行ずつ）。</summary>
    private sealed class MemberHistoryRoleGroup
    {
        /// <summary>役職の和名（roles.name_ja。未解決時は role_code）。</summary>
        public string RoleNameJa { get; set; } = "";
        /// <summary>この役職での担当回数（TV＝distinct 話数、📺 ピル）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>この役職での担当本数（映画系シリーズの distinct 本数、🎥 ピル）。</summary>
        public int MovieCount { get; set; }
        /// <summary>この役職の担当作品（シリーズ放送開始日昇順。1 行ずつ・話数を可視表示）。</summary>
        public IReadOnlyList<MemberHistoryWork> Works { get; set; } = Array.Empty<MemberHistoryWork>();
    }

    /// <summary>メンバー履歴：作品 1 行（1 シリーズ + 話数範囲）。</summary>
    private sealed class MemberHistoryWork
    {
        /// <summary>シリーズタイトル（リンクテキスト）。</summary>
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズスラッグ（リンク先 /series/{slug}/）。</summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>シリーズ ID（ソート時の放送開始日参照用）。</summary>
        public int SeriesId { get; set; }
        /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>映画系シリーズか（作品行アイコンの 🎥 / 📺 切替に使用）。</summary>
        public bool IsMovie { get; set; }
        /// <summary>担当話数の可視表記（TV：圧縮表記「#1〜4, 8」/「(全話)」/「（シリーズ全体）」。映画は空文字）。</summary>
        public string RangeLabel { get; set; } = "";
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
        /// <summary>公式ページ URL。詳細ページ末尾「外部リンク」セクションに出す。 Wikipedia は内部値として保持はするがサイト UI からはリンクしない方針なので、 ここでは敢えて出していない。</summary>
        public string OfficialUrl { get; set; } = "";
        public string XUrl { get; set; } = "";
        public string InstagramUrl { get; set; } = "";
        public string YoutubeUrl { get; set; } = "";
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
        /// <summary>当該ロゴがクレジットされたシリーズ・話数範囲の小書き注記。</summary>
        public string CreditRangeLabel { get; set; } = "";
    }
}