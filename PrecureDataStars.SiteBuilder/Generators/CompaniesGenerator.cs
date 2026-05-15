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
    // メンバー履歴セクションで「人物名義 → person_id 解決」をするために
    // person_alias_persons と person_aliases / persons のリポジトリを参照する。
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly PersonsRepository _personsRepo;

    private readonly CreditInvolvementIndex _index;

    private IReadOnlyDictionary<string, Role>? _roleMap;

    /// <summary>
    /// person_alias_id → 代表 person_id。
    /// メンバー履歴セクションで人物詳細ページへリンクするために、
    /// PersonsGenerator と同じ仕様で alias → person 解決を行う。
    /// </summary>
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

        // 索引ページの行を組み立てる。
        // 索引は「初クレジット順セクション」形式（タブ無し、シリーズ別 1 軸）。
        // 関与 0 件の企業はそもそも索引に出さない方針なので、対象を集めながら同時に判定する。
        var indexRows = new List<CompanyIndexRow>(companies.Count);
        foreach (var co in companies)
        {
            var aliases = aliasesByCompany.TryGetValue(co.CompanyId, out var lst) ? lst : new List<CompanyAlias>();

            // 代表屋号: 一番新しい（successor が無い）alias、無ければ先頭。
            var current = aliases.FirstOrDefault(a => a.SuccessorAliasId is null) ?? aliases.FirstOrDefault();
            string displayName = current?.Name ?? co.Name;
            string displayKana = current?.NameKana ?? co.NameKana ?? "";

            // 全関与を一度物理化しておく（エピソード数集計と初クレジット判定の双方で使う）。
            var allInvolvementsForIndex = CollectAllInvolvements(aliases, logosByAlias).ToList();

            // 関与エピソード数 = 当該会社の全 alias と、配下ロゴの合算（unique episode）。
            int episodeCount = allInvolvementsForIndex
                .Where(i => i.EpisodeId.HasValue)
                .Select(i => i.EpisodeId!.Value)
                .Distinct()
                .Count();

            // 関与 0 件の企業は索引から完全除外する（無関与企業の placeholder 行は出さない方針）。
            // 詳細ページ /companies/{companyId}/ は引き続き生成するため、直リンクが切れることはない。
            if (allInvolvementsForIndex.Count == 0) continue;

            // クレジットされた役職を「早い順」で最大 3 件並べた表示ラベルを組み立てる。
            // PersonsGenerator.BuildPersonRolesLabel と同じ判定ロジック。
            // 屋号直接参照（Company）・先頭企業（LeadingCompany）・ロゴ経由（Logo）を全部対象にする
            // （企業視点では「自社の名前が出た役職」がすべて該当）。Member 種別（人物の所属屋号）は除外。
            string rolesLabel = BuildCompanyRolesLabel(aliases, logosByAlias);

            // 初クレジット順セクション分け用に、当該企業が最も早くクレジットされたシリーズと
            // そのシリーズ内の最早話数を求める。シリーズ全体スコープは話数 0 として優先扱い。
            // 集計対象は CollectAllInvolvements が返した Involvement そのもの（Member 種別はそこで除外済み）。
            int? firstSeriesId = null;
            DateOnly bestStart = DateOnly.MaxValue;
            int bestEpNo = int.MaxValue;
            foreach (var inv in allInvolvementsForIndex)
            {
                var start = SeriesStartDate(inv.SeriesId);
                int epNo;
                if (inv.EpisodeId is int eid)
                {
                    var ep = LookupEpisode(inv.SeriesId, eid);
                    epNo = ep?.SeriesEpNo ?? int.MaxValue;
                }
                else
                {
                    epNo = 0;
                }
                if (start < bestStart || (start == bestStart && epNo < bestEpNo))
                {
                    firstSeriesId = inv.SeriesId;
                    bestStart = start;
                    bestEpNo = epNo;
                }
            }

            indexRows.Add(new CompanyIndexRow
            {
                CompanyId = co.CompanyId,
                DisplayName = displayName,
                DisplayKana = displayKana,
                EpisodeCount = episodeCount,
                HasInvolvement = episodeCount > 0,
                RolesLabel = rolesLabel,
                FirstCreditSeriesId = firstSeriesId,
                FirstCreditSeriesEpNo = firstSeriesId.HasValue ? bestEpNo : 0
            });
        }

        // シリーズ別セクション分け。セクション順はシリーズ放送開始日昇順。
        // 各セクション内は「初登場話数 → kana → 名前」の順（PersonsGenerator の初クレジット順と同方針）。
        var debutSections = BuildCompanyDebutSections(indexRows);

        var indexContent = new CompanyIndexModel
        {
            DebutSections = debutSections,
            TotalCount = indexRows.Count,
            ActiveCount = indexRows.Count(r => r.HasInvolvement),
            CoverageLabel = _ctx.CreditCoverageLabel
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

        // 各ロゴについて、当該ロゴがクレジットされたシリーズと話数範囲を
        // 小書きキャプション（CreditRangeLabel）として組み立てる。
        // _index.ByLogo から関与を引いて、シリーズ単位に集約 → シリーズ内話数を圧縮表記。
        foreach (var av in aliasViews)
        {
            foreach (var lv in av.Logos)
            {
                lv.CreditRangeLabel = BuildLogoCreditRangeLabel(lv.LogoId);
            }
        }

        // 全関与（alias 経由 + ロゴ経由）を集めて役職別グループ化。
        var allInvolvements = CollectAllInvolvements(aliases, logosByAlias).ToList();
        var groups = BuildCompanyInvolvementGroups(allInvolvements);

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
                FoundedDate = FormatDate(company.FoundedDate),
                DissolvedDate = FormatDate(company.DissolvedDate),
                Notes = company.Notes ?? ""
            },
            Aliases = aliasViews,
            InvolvementGroups = groups,
            MemberHistory = memberHistory,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        // 企業詳細の構造化データは Schema.org の Organization 型。
        // alternateName に屋号（alias.name）を配列で並べる。foundingDate / dissolutionDate は持っていれば埋め込む。
        // description を追加して、検索結果のリッチスニペット候補要素を増やす。
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
            PageTitle = displayName,
            MetaDescription = metaDescription,
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
    /// 企業・団体詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// <para>
    /// 構成：「{会社名}は、プリキュアシリーズで{役職1}({N作品})・{役職2}({N作品})などを担当した企業・団体。」を骨格に、
    /// 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。
    /// 役職は <see cref="InvolvementGroup.Count"/> 降順（担当エピソード数の多い順）で最大 3 件。
    /// 関与役職が 1 件も無い場合は、定型文「{会社名} のプリキュア関連クレジット一覧。」にフォールバック。
    /// </para>
    /// </summary>
    private static string BuildCompanyMetaDescription(
        string displayName,
        IReadOnlyList<InvolvementGroup> involvementGroups)
    {
        const int targetMaxChars = 140;

        if (involvementGroups.Count == 0)
        {
            return $"{displayName} のプリキュア関連クレジット一覧。";
        }

        // 担当話数の多い順で上位役職を取り出し、最大 3 件まで採用する。
        var ordered = involvementGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.RoleLabel) && g.RoleLabel != "(役職未設定)")
            .OrderByDescending(g => g.Count)
            .Take(3)
            .ToList();

        if (ordered.Count == 0)
        {
            return $"{displayName} のプリキュア関連クレジット一覧。";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(displayName).Append("は、プリキュアシリーズで");

        int appended = 0;
        foreach (var g in ordered)
        {
            if (g.Count <= 0) continue;
            var fragment = $"{g.RoleLabel}({g.Count}話)";
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
            return $"{displayName} のプリキュア関連クレジット一覧。";
        }

        sb.Append("などを担当した企業・団体。");
        return sb.ToString();
    }

    /// <summary>
    /// 企業に紐付く全 alias の関与（直接 + leading）+ 配下ロゴの関与（LOGO エントリ）を 1 シーケンスに合算。
    /// <para>
    /// <see cref="InvolvementKind.Member"/>（所属屋号としての参照、本来は人物名義側のクレジット）
    /// は本企業詳細の「クレジット履歴」セクションには含めない。メンバー所属はクレジットの主体ではなく
    /// 「人物の所属先として登場した記録」なので、クレジット履歴と一緒に表示すると意味が混在する。
    /// メンバー所属の集計は <see cref="CollectMemberInvolvements"/> で別途行い、専用「メンバー履歴」
    /// セクションで一覧化する。
    /// </para>
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

    /// <summary>
    /// 当該企業（その全屋号）を所属としてクレジットされた人物名義の Involvement だけを集める。
    /// </summary>
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

    /// <summary>
    /// 指定ロゴ ID がクレジットされたシリーズ・話数範囲の小書きキャプションを組み立てる。
    /// <para>
    /// 例: 「キミとアイドルプリキュア♪ #1〜14、Yes!プリキュア5GoGo!（シリーズ全体）」。
    /// <see cref="CreditInvolvementIndex.ByLogo"/> から関与レコードを引き、
    /// シリーズ単位に集約してから話数を <see cref="EpisodeRangeCompressor"/> で圧縮表示する。
    /// シリーズの並びは放送開始日昇順。1 件も無い場合は空文字を返す（テンプレ側で非表示）。
    /// </para>
    /// </summary>
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
                var ep = LookupEpisode(inv.SeriesId, eid);
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
        foreach (var (seriesId, entry) in bySeries.OrderBy(kv => SeriesStartDate(kv.Key)))
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
    /// 当該企業のメンバー履歴行を組み立てる。
    /// <para>
    /// 走査の流れ：
    /// 1) <see cref="CollectMemberInvolvements"/> で当該企業の全屋号を所属とした Involvement を集める。
    /// 2) (person_id × company_alias_id × series_id) の 3 軸でグルーピング。
    ///    同じ人物が複数シリーズで同じ屋号所属 → シリーズごとに別行。
    ///    同じ人物が同シリーズで複数屋号所属 → 屋号ごとに別行（屋号変更の履歴を可視化）。
    /// 3) 話数集合を圧縮表記（全話なら "(全話)" マーク）。
    /// 4) ソートはシリーズ放送開始日昇順 → 人物読み昇順 → 屋号名昇順。
    /// </para>
    /// </summary>
    private IReadOnlyList<MemberHistoryRow> BuildMemberHistory(IReadOnlyList<CompanyAlias> aliases)
    {
        var members = CollectMemberInvolvements(aliases).ToList();
        if (members.Count == 0) return Array.Empty<MemberHistoryRow>();
        if (_personIdByAlias is null || _personAliasById is null || _personById is null)
            return Array.Empty<MemberHistoryRow>();

        // (PersonAliasId, AffiliationCompanyAliasId, SeriesId) → 話数集合（シリーズ全体スコープなら IsSeriesScope=true）。
        var grouped = new Dictionary<(int PersonAliasId, int AffId, int SeriesId), (bool IsSeriesScope, HashSet<int> EpNos)>();
        foreach (var inv in members)
        {
            if (inv.PersonAliasId is not int paid) continue;
            if (inv.AffiliationCompanyAliasId is not int affId) continue;

            var key = (paid, affId, inv.SeriesId);
            if (!grouped.TryGetValue(key, out var entry))
            {
                entry = (false, new HashSet<int>());
                grouped[key] = entry;
            }
            if (inv.EpisodeId is int eid)
            {
                var ep = LookupEpisode(inv.SeriesId, eid);
                if (ep is not null) entry.EpNos.Add(ep.SeriesEpNo);
                grouped[key] = entry;
            }
            else
            {
                entry.IsSeriesScope = true;
                grouped[key] = entry;
            }
        }

        // alias_id → 屋号名（自社内の屋号のみ参照する想定なので、aliases リストから直接マップを作る）。
        var aliasNameById = aliases.ToDictionary(a => a.AliasId, a => a.Name ?? "");

        var rows = new List<MemberHistoryRow>();
        foreach (var ((paid, affId, seriesId), entry) in grouped)
        {
            // 名義 → 人物 ID の解決。共有名義は代表 1 名へリンク。
            if (!_personIdByAlias.TryGetValue(paid, out var personId)) continue;
            if (!_ctx.SeriesById.TryGetValue(seriesId, out var series)) continue;

            // 名義の表示テキストと、ソート用の読み（人物本体の FullNameKana）。
            var alias = _personAliasById.TryGetValue(paid, out var a) ? a : null;
            string personName = alias?.Name ?? "(名義不明)";
            string personNameKana = _personById.TryGetValue(personId, out var p) ? (p.FullNameKana ?? p.FullName ?? "") : "";
            string aliasName = aliasNameById.TryGetValue(affId, out var an) ? an : "";

            // 話数範囲を圧縮表記（全話 / シリーズ全体 / 部分）。
            string rangeLabel;
            if (entry.IsSeriesScope)
            {
                rangeLabel = "（シリーズ全体）";
            }
            else if (entry.EpNos.Count == 0)
            {
                rangeLabel = "";
            }
            else
            {
                var allEpNos = _ctx.EpisodesBySeries.TryGetValue(seriesId, out var allEps)
                    ? allEps.Select(e => e.SeriesEpNo).ToList()
                    : new List<int>();
                bool isAll = allEpNos.Count > 0 && entry.EpNos.SetEquals(allEpNos);
                rangeLabel = isAll ? "(全話)" : EpisodeRangeCompressor.Compress(entry.EpNos);
            }

            rows.Add(new MemberHistoryRow
            {
                PersonId = personId,
                PersonName = personName,
                PersonNameKana = personNameKana,
                AliasName = aliasName,
                SeriesTitle = series.Title,
                SeriesStartYearLabel = series.StartDate.Year.ToString(),
                SeriesSlug = series.Slug,
                SeriesId = seriesId,
                RangeLabel = rangeLabel
            });
        }

        // ソート：シリーズ放送開始日昇順 → 人物読み昇順 → 屋号名昇順。
        return rows
            .OrderBy(r => SeriesStartDate(r.SeriesId))
            .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
            .ThenBy(r => r.AliasName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 当該企業がクレジットされた役職を「早い順」で最大 3 件並べた表示ラベルを作る。
    /// <see cref="PersonsGenerator"/> 側の同名ヘルパと同じ判定ロジック。Member 種別（人物所属としての参照）
    /// は除外して、企業視点の「自社名が出た役職」だけを対象にする。
    /// </summary>
    private string BuildCompanyRolesLabel(
        IReadOnlyList<CompanyAlias> aliases,
        IReadOnlyDictionary<int, IReadOnlyList<Logo>> logosByAlias)
    {
        if (aliases.Count == 0 || _roleMap is null) return "";

        var earliestByRole = new Dictionary<string, (DateOnly Start, int EpNo)>(StringComparer.Ordinal);

        foreach (var inv in CollectAllInvolvements(aliases, logosByAlias))
        {
            // CollectAllInvolvements は既に Member 種別を除外して返してくる。
            // 残りの Company / Logo / LeadingCompany すべてを役職集計の対象に含める。
            var roleCode = inv.RoleCode;
            if (string.IsNullOrEmpty(roleCode)) continue;

            var start = SeriesStartDate(inv.SeriesId);
            int epNo;
            if (inv.EpisodeId is int eid)
            {
                var ep = LookupEpisode(inv.SeriesId, eid);
                epNo = ep?.SeriesEpNo ?? int.MaxValue;
            }
            else
            {
                epNo = 0;
            }

            if (!earliestByRole.TryGetValue(roleCode, out var cur)
                || start < cur.Start
                || (start == cur.Start && epNo < cur.EpNo))
            {
                earliestByRole[roleCode] = (start, epNo);
            }
        }

        if (earliestByRole.Count == 0) return "";

        var ordered = earliestByRole
            .OrderBy(kv => kv.Value.Start)
            .ThenBy(kv => kv.Value.EpNo)
            .Select(kv => kv.Key)
            .ToList();

        const int Top = 3;
        var topRoles = ordered.Take(Top)
            .Select(code => _roleMap!.TryGetValue(code, out var r) ? (r.NameJa ?? code) : code)
            .ToList();
        string main = string.Join("・", topRoles);

        int rest = ordered.Count - topRoles.Count;
        if (rest > 0)
        {
            return $"{main} 他 {rest} 役職";
        }
        return main;
    }

    /// <summary>
    /// 初クレジット順セクション群を組み立てる。
    /// <para>
    /// 対象は <c>FirstCreditSeriesId</c> が非 null の企業のみ（無関与企業はそもそも索引から除外済みなので、
    /// 実質的に全行が該当する）。セクションキー = シリーズ ID、見出し = シリーズタイトル + 開始年。
    /// セクション順はシリーズ放送開始日昇順。各セクション内は「初登場話数 → kana → 名前」の順。
    /// シリーズが <c>BuildContext.SeriesById</c> に存在しないシリーズ ID は無視する（不整合データ対策）。
    /// </para>
    /// </summary>
    private IReadOnlyList<CompanyIndexDebutSection> BuildCompanyDebutSections(IReadOnlyList<CompanyIndexRow> rows)
    {
        var bySeries = rows
            .Where(r => r.FirstCreditSeriesId.HasValue)
            .GroupBy(r => r.FirstCreditSeriesId!.Value)
            .ToList();

        var sections = new List<CompanyIndexDebutSection>();
        int idx = 0;
        foreach (var g in bySeries.OrderBy(g => SeriesStartDate(g.Key)))
        {
            if (!_ctx.SeriesById.TryGetValue(g.Key, out var series)) continue;
            idx++;
            var members = g
                .OrderBy(r => r.FirstCreditSeriesEpNo)
                .ThenBy(r => string.IsNullOrEmpty(r.DisplayKana) ? 1 : 0)
                .ThenBy(r => r.DisplayKana, StringComparer.Ordinal)
                .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
                .ToList();
            sections.Add(new CompanyIndexDebutSection
            {
                SeriesTitle = series.Title,
                SeriesSlug = series.Slug,
                SeriesStartYearLabel = series.StartDate.Year.ToString(),
                SectionId = $"companies-debut-{idx}",
                Members = members
            });
        }
        return sections;
    }

    /// <summary>会社の全関与を役職別にグルーピング。</summary>
    /// <summary>
    /// 企業・団体に紐付く関与情報を、役職別 → シリーズ単位の話数圧縮表記に編成する。
    /// 役職別 → シリーズ単位 1 行 + 話数を「#1〜4, 8」のように圧縮表示する。
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
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
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
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
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
        /// <summary>
        /// 初クレジット順タブ用セクション群。
        /// 各セクションは「当該企業が初めてクレジットされたシリーズ」を見出しに持ち、配下にその
        /// シリーズで初クレジットされた企業の <see cref="CompanyIndexRow"/> を初登場話数昇順で並べる。
        /// セクションの並びはシリーズ放送開始日昇順。クレジット 0 件の企業はそもそも索引に載せない。
        /// </summary>
        public IReadOnlyList<CompanyIndexDebutSection> DebutSections { get; set; } = Array.Empty<CompanyIndexDebutSection>();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        /// <summary>
        /// クレジット横断カバレッジラベル。
        /// テンプレ側の lead 段落末尾に表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>
    /// 初クレジット順セクションの 1 ブロック。
    /// </summary>
    private sealed class CompanyIndexDebutSection
    {
        /// <summary>セクション見出し（シリーズタイトル）。</summary>
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズスラッグ（h2 のシリーズ詳細リンク用）。</summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>シリーズ開始年（4 桁、見出し脇の小書きで表示）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>セクション ID（テンプレ側でアンカーリンク・section-nav.js の dt 用）。"companies-debut-{idx}" 形式。</summary>
        public string SectionId { get; set; } = "";
        /// <summary>当該シリーズで初クレジットされた企業行（最早話数 → kana → 名前 の順）。</summary>
        public IReadOnlyList<CompanyIndexRow> Members { get; set; } = Array.Empty<CompanyIndexRow>();
    }

    private sealed class CompanyIndexRow
    {
        public int CompanyId { get; set; }
        public string DisplayName { get; set; } = "";
        public string DisplayKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public bool HasInvolvement { get; set; }
        /// <summary>
        /// クレジットされた役職の表示ラベル。
        /// PersonsGenerator と同じ仕様：最も早い時期にクレジットされた役職から順に最大 3 件、
        /// 超える場合は末尾に「他 N 役職」を付ける。
        /// </summary>
        public string RolesLabel { get; set; } = "";
        /// <summary>
        /// 初クレジット順タブで使うソート/セクション分けキー：当該企業が初めてクレジットされた
        /// シリーズの ID。クレジット 0 件の企業ではそもそも索引から除外されるため
        /// 実質的に必ず非 null。
        /// </summary>
        public int? FirstCreditSeriesId { get; set; }
        /// <summary>
        /// 上記シリーズ内での初登場話数。
        /// シリーズ全体スコープからの関与は 0 として優先扱い。
        /// </summary>
        public int FirstCreditSeriesEpNo { get; set; }
    }

    private sealed class CompanyDetailModel
    {
        public CompanyView Company { get; set; } = new();
        public IReadOnlyList<CompanyAliasView> Aliases { get; set; } = Array.Empty<CompanyAliasView>();
        public IReadOnlyList<InvolvementGroup> InvolvementGroups { get; set; } = Array.Empty<InvolvementGroup>();
        /// <summary>
        /// メンバー履歴。
        /// 当該企業の屋号を所属としてクレジットされた人物名義の一覧。
        /// シリーズの放送開始日昇順、当該シリーズでの所属屋号、最初〜最後の話数で並べる。
        /// 0 件の場合はテンプレ側でセクション自体を非表示にする。
        /// </summary>
        public IReadOnlyList<MemberHistoryRow> MemberHistory { get; set; } = Array.Empty<MemberHistoryRow>();
        /// <summary>
        /// クレジット横断カバレッジラベル。
        /// テンプレ側の h1 ブロック直後に独立段落で表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>
    /// メンバー履歴 1 行。
    /// 「当該企業の屋号を所属とした人物名義 1 件 = 1 行」の単位で表示する。
    /// 同じ人物が複数シリーズで異なる屋号所属になっている場合は複数行になる
    /// （シリーズ × 所属屋号 × 名義の 3 軸で行を分ける）。
    /// </summary>
    private sealed class MemberHistoryRow
    {
        /// <summary>人物 ID（リンク先 /persons/{id}/）。共有名義時は最初の person を採用。</summary>
        public int PersonId { get; set; }
        /// <summary>名義の表示テキスト（人物リンクのアンカーテキスト）。</summary>
        public string PersonName { get; set; } = "";
        /// <summary>名義の読み（ソート用）。</summary>
        public string PersonNameKana { get; set; } = "";
        /// <summary>所属屋号名（自社内のどの屋号として所属していたか）。</summary>
        public string AliasName { get; set; } = "";
        /// <summary>シリーズタイトル（リンクテキスト）。</summary>
        public string SeriesTitle { get; set; } = "";
        /// <summary>
        /// シリーズ開始年の西暦 4 桁文字列（例: "2004"）。
        /// メンバー履歴テーブルの「シリーズ」列の直後に「年度」列として独立表示する用途。
        /// </summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>シリーズスラッグ（リンク先 /series/{slug}/）。</summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>シリーズ ID（ソート時の放送開始日参照用、テンプレでは未使用）。</summary>
        public int SeriesId { get; set; }
        /// <summary>当該シリーズでクレジットされた話数の圧縮表記（例「#1〜4, 8」、全話なら "(全話)"）。</summary>
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
        /// <summary>
        /// 当該ロゴがクレジットされたシリーズ・話数範囲の小書き注記。
        /// 例：「キミとアイドルプリキュア♪ #1〜14」「Yes!プリキュア5GoGo!（シリーズ全体）」など。
        /// 複数シリーズにまたがる場合は「、」で連結。
        /// 1 件も関与が無いロゴでは空文字（テンプレ側で非表示）。
        /// </summary>
        public string CreditRangeLabel { get; set; } = "";
    }
}