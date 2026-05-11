using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 人物索引（<c>/persons/</c>）と人物詳細（<c>/persons/{person_id}/</c>）の生成。
/// <para>
/// 関与情報は <see cref="CreditInvolvementIndex"/> を経由して逆引きする。
/// 同一人物に複数の名義（旧姓・別名義・ユニット名義など）がある場合、
/// <c>person_alias_persons</c> 経由で当該 person に紐付く全 alias_id を集め、
/// それぞれの逆引き結果を合算する。
/// </para>
/// </summary>
public sealed class PersonsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _aliasesRepo;
    private readonly PersonAliasPersonsRepository _aliasPersonsRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly RolesRepository _rolesRepo;
    /// <summary>
    /// 屋号 alias_id → 屋号名 解決用（v1.3.0 続編で追加）。
    /// クレジット履歴行に所属屋号併記を出すときに、Involvement に詰めた
    /// AffiliationCompanyAliasId から屋号名を引くために使う。
    /// </summary>
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    private readonly CreditInvolvementIndex _index;

    /// <summary>
    /// person_id → 当該人物に紐付く全 alias_id のリスト（person_alias_persons の逆引き）。
    /// 1 度ロードしたら使い回す。
    /// </summary>
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasesByPerson;

    /// <summary>役職コード → Role モデル。役職の表示名解決と display_order の取得に使う。</summary>
    private IReadOnlyDictionary<string, Role>? _roleMap;

    /// <summary>character_alias_id → CharacterAlias。声優関与のときキャラ名表示に使う。</summary>
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();

    /// <summary>
    /// company_alias_id → 屋号名 のキャッシュ（v1.3.0 続編で追加）。
    /// クレジット履歴の所属屋号併記で同じ alias を何度も解決するため。
    /// 値が <c>null</c> のときは「未登録」を意味する（負の結果もキャッシュ）。
    /// </summary>
    private readonly Dictionary<int, string?> _companyAliasNameCache = new();

    public PersonsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        CreditInvolvementIndex index)
    {
        _ctx = ctx;
        _page = page;
        _index = index;

        _personsRepo = new PersonsRepository(factory);
        _aliasesRepo = new PersonAliasesRepository(factory);
        _aliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _companyAliasesRepo = new CompanyAliasesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating persons");

        // 全人物・全名義を一括ロード。
        var persons = await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var allAliases = await _aliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var aliasById = allAliases.ToDictionary(a => a.AliasId);

        // 役職マスタ（表示名 + display_order）を引いておく。
        if (_roleMap is null)
        {
            var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _roleMap = allRoles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        }

        // person_id → alias_id 群の逆引きを 1 度だけ作る（person_alias_persons は通常 1:1 + 稀に 1:N）。
        if (_aliasesByPerson is null)
        {
            var dict = new Dictionary<int, List<int>>();
            foreach (var p in persons)
            {
                var rows = await _aliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
                dict[p.PersonId] = rows.Select(r => r.AliasId).ToList();
            }
            _aliasesByPerson = dict.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<int>)kv.Value);
        }

        // 索引ページ用 DTO。各人物に「代表名義」と「関与エピソード数」を載せる。
        var indexRows = new List<PersonIndexRow>(persons.Count);
        foreach (var p in persons)
        {
            // 代表名義: 紐付く alias の中で「後継者がいない＝最新の名義」を 1 つ選ぶ。
            // 全部に後継者がいる（リング構造）等の異常があれば、name_kana 優先で先頭を採る。
            var aliasIds = _aliasesByPerson.TryGetValue(p.PersonId, out var ids) ? ids : Array.Empty<int>();
            var aliases = aliasIds
                .Select(id => aliasById.TryGetValue(id, out var a) ? a : null)
                .Where(a => a is not null)
                .Cast<PersonAlias>()
                .ToList();
            string displayName = p.FullName;
            string? displayKana = p.FullNameKana;
            if (aliases.Count > 0)
            {
                var current = aliases.FirstOrDefault(a => a.SuccessorAliasId is null) ?? aliases[0];
                displayName = current.DisplayTextOverride ?? current.Name;
                displayKana = current.NameKana ?? p.FullNameKana;
            }

            // 関与回数（ユニーク episode 数）を集計。複数 alias を合算。
            int episodeCount = aliasIds
                .Where(_index.ByPersonAlias.ContainsKey)
                .SelectMany(id => _index.ByPersonAlias[id])
                .Where(i => i.EpisodeId.HasValue)
                .Select(i => i.EpisodeId!.Value)
                .Distinct()
                .Count();

            // v1.3.0 続編：クレジットされた役職を「最も早い時期にクレジットされた順」で
            // 最大 3 件まで列挙し、超える場合は末尾に「他 N 役職」を付ける。
            // 「早い時期」の評価は「当該役職で最初に登場したシリーズの放送開始日 → 同シリーズ内の最早話数」。
            // ロゴ・屋号関与とは関係ない、純粋に「人物として担った役職」だけを対象にする
            // （Person / CharacterVoice 種別の Involvement のみ）。
            string rolesLabel = BuildPersonRolesLabel(aliasIds);

            indexRows.Add(new PersonIndexRow
            {
                PersonId = p.PersonId,
                DisplayName = displayName,
                DisplayKana = displayKana ?? "",
                EpisodeCount = episodeCount,
                HasInvolvement = episodeCount > 0,
                RolesLabel = rolesLabel
            });
        }

        // 並び順: 50音順（kana 昇順）。kana が空のものは末尾（DisplayName Ordinal 順）。
        indexRows = indexRows
            .OrderBy(r => string.IsNullOrEmpty(r.DisplayKana) ? 1 : 0)
            .ThenBy(r => r.DisplayKana, StringComparer.Ordinal)
            .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .ToList();

        // 索引ページ書き出し。
        var indexContent = new PersonIndexModel
        {
            Persons = indexRows,
            TotalCount = indexRows.Count,
            ActiveCount = indexRows.Count(r => r.HasInvolvement),
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var indexLayout = new LayoutModel
        {
            PageTitle = "人物一覧",
            MetaDescription = "プリキュアシリーズに関わった人物の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "人物", Url = "" }
            }
        };
        _page.RenderAndWrite("/persons/", "persons", "persons-index.sbn", indexContent, indexLayout);

        // 詳細ページ。関与が 1 件もない人物もページは作る（直リンク用）。
        foreach (var p in persons)
        {
            await GenerateDetailAsync(p, aliasById, ct).ConfigureAwait(false);
        }

        _ctx.Logger.Success($"persons: {persons.Count + 1} ページ");
    }

    /// <summary>人物詳細ページ <c>/persons/{person_id}/</c> を生成する。</summary>
    private async Task GenerateDetailAsync(
        Person person,
        IReadOnlyDictionary<int, PersonAlias> aliasById,
        CancellationToken ct)
    {
        var aliasIds = _aliasesByPerson!.TryGetValue(person.PersonId, out var ids)
            ? ids
            : Array.Empty<int>();
        var aliases = aliasIds
            .Select(id => aliasById.TryGetValue(id, out var a) ? a : null)
            .Where(a => a is not null)
            .Cast<PersonAlias>()
            .ToList();

        // 名義の時系列順序付け：predecessor を逆方向にたどって最古の alias を root にし、
        // そこから successor チェーンで並べる。リング構造や複線がある場合は、
        // 解けたチェーンに含まれない alias を末尾にまとめて出す。
        var aliasViews = OrderAliasesChronologically(aliases);

        // 代表名義（successor が無い alias を優先、無ければ先頭）。
        PersonAlias? currentAlias = aliases.FirstOrDefault(a => a.SuccessorAliasId is null) ?? aliases.FirstOrDefault();
        string displayName = currentAlias is null
            ? person.FullName
            : (currentAlias.DisplayTextOverride ?? currentAlias.Name);

        // 役職別グループ化された関与一覧を組み立て。
        var involvementGroups = await BuildPersonInvolvementGroupsAsync(aliasIds, ct).ConfigureAwait(false);

        var content = new PersonDetailModel
        {
            Person = new PersonView
            {
                PersonId = person.PersonId,
                DisplayName = displayName,
                FullName = person.FullName,
                FullNameKana = person.FullNameKana ?? "",
                NameEn = person.NameEn ?? "",
                Notes = person.Notes ?? ""
            },
            Aliases = aliasViews,
            InvolvementGroups = involvementGroups,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        // 人物詳細の構造化データは Schema.org の Person 型。
        // alternateName に名義（alias の name）を配列で並べる。
        string baseUrl = _ctx.Config.BaseUrl;
        string personUrl = PathUtil.PersonUrl(person.PersonId);
        var alternateNames = aliasViews
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !string.Equals(n, person.FullName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Person",
            ["name"] = displayName
        };
        if (alternateNames.Count > 0) jsonLdDict["alternateName"] = alternateNames;
        if (!string.IsNullOrEmpty(person.NameEn)) jsonLdDict["givenName"] = person.NameEn;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + personUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = displayName,
            MetaDescription = $"{displayName} のプリキュア関連クレジット一覧。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "人物", Url = "/persons/" },
                new BreadcrumbItem { Label = displayName, Url = "" }
            },
            OgType = "profile",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite(
            personUrl,
            "persons",
            "persons-detail.sbn",
            content,
            layout);
    }

    /// <summary>
    /// 人物の名義群を時系列に並べる（predecessor チェーンを上に辿って root を見つけ、
    /// successor チェーンで下降）。チェーンに含まれなかった alias は末尾に並べる。
    /// </summary>
    private static IReadOnlyList<PersonAliasView> OrderAliasesChronologically(IReadOnlyList<PersonAlias> aliases)
    {
        if (aliases.Count == 0) return Array.Empty<PersonAliasView>();

        var byId = aliases.ToDictionary(a => a.AliasId);
        var visited = new HashSet<int>();
        var ordered = new List<PersonAlias>();

        // predecessor を持たない alias（チェーンの先頭候補）から開始。複数あればそれぞれの鎖を順に。
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

        // 上で訪問できなかった alias（チェーン構造異常時の取りこぼし）を末尾に追加。
        foreach (var a in aliases)
        {
            if (visited.Add(a.AliasId)) ordered.Add(a);
        }

        return ordered.Select(a => new PersonAliasView
        {
            AliasId = a.AliasId,
            Name = a.DisplayTextOverride ?? a.Name,
            NameKana = a.NameKana ?? "",
            NameEn = a.NameEn ?? "",
            ValidFrom = FormatDate(a.ValidFrom),
            ValidTo = FormatDate(a.ValidTo),
            Notes = a.Notes ?? ""
        }).ToList();
    }

    /// <summary>
    /// 人物に紐付く alias_id 群から関与情報を集約し、役職別 → シリーズ単位の話数圧縮表記に編成する
    /// （v1.3.0 ブラッシュアップ続編で大幅変更）。
    /// <para>
    /// 旧仕様：役職別 → エピソードごと 1 行のテーブル形式（行が大量に出る）。
    /// 新仕様：役職別 → シリーズ単位 1 行 + 話数を「#1〜4, 8」のように圧縮表示。
    /// 全話担当のときは話数表記を省略し、代わりに「(全話)」マークを付ける。
    /// 声優役（CHARACTER_VOICE）のときは演じたキャラ名（シリーズ内全話分の連名）も併記する。
    /// シリーズ全体スコープ（episode_id NULL）の関与は別行として「（シリーズ全体）」で残す。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<InvolvementGroup>> BuildPersonInvolvementGroupsAsync(
        IReadOnlyList<int> aliasIds,
        CancellationToken ct)
    {
        var all = aliasIds
            .Where(_index.ByPersonAlias.ContainsKey)
            .SelectMany(id => _index.ByPersonAlias[id])
            .ToList();
        if (all.Count == 0) return Array.Empty<InvolvementGroup>();

        // 役職コード単位でグルーピング → 表示順は role_map.DisplayOrder 昇順。
        int RoleOrder(string code)
        {
            if (string.IsNullOrEmpty(code)) return int.MaxValue;
            if (_roleMap!.TryGetValue(code, out var r) && r.DisplayOrder is ushort d) return d;
            return int.MaxValue - 1;
        }

        var groups = new List<InvolvementGroup>();
        foreach (var roleGroup in all
            .GroupBy(i => i.RoleCode)
            .OrderBy(g => RoleOrder(g.Key)))
        {
            string roleCode = roleGroup.Key;
            string roleLabel = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                : (_roleMap!.TryGetValue(roleCode, out var r) ? (r.NameJa ?? roleCode) : roleCode);

            // 役職グループ内をさらにシリーズ単位で集約。
            var seriesRows = new List<InvolvementSeriesRow>();
            int episodeCountTotal = 0;

            foreach (var bySeries in roleGroup
                .GroupBy(i => i.SeriesId)
                .OrderBy(sg => SeriesStartDate(sg.Key)))
            {
                if (!_ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;

                // 同一シリーズで「シリーズ全体スコープ」と「エピソード単位」が混在しうる。
                // シリーズ全体スコープは別行として残し、エピソード単位は話数集合に集約する。
                var episodeNos = new HashSet<int>();
                bool hasSeriesScope = false;
                var seriesScopeCharacterNames = new List<string>();
                var perEpisodeCharacterNames = new List<string>();
                // v1.3.0 続編：所属屋号 ID の集合をシリーズスコープ別・エピソード単位別に分けて収集。
                // 同一シリーズ内で複数の屋号で所属クレジットされる例（移籍など）があるため、
                // HashSet で重複排除し、後で名前解決して列挙する。
                // OrderedSet 相当の挙動が欲しいので「初出順を保つために」List + Contains で管理する。
                var seriesScopeAffiliationIds = new List<int>();
                var perEpisodeAffiliationIds = new List<int>();

                foreach (var inv in bySeries)
                {
                    if (inv.EpisodeId is int eid)
                    {
                        var ep = LookupEpisode(bySeries.Key, eid);
                        if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
                        // 声優関与のとき演じたキャラ名を集める（シリーズ単位で重複排除）。
                        if (inv.Kind == InvolvementKind.CharacterVoice && inv.CharacterAliasId.HasValue)
                        {
                            string? name = await ResolveCharacterNameAsync(inv.CharacterAliasId.Value, ct).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(name) && !perEpisodeCharacterNames.Contains(name))
                                perEpisodeCharacterNames.Add(name);
                        }
                        // v1.3.0 続編：所属屋号 ID を初出順で記録（人物詳細での所属併記用）。
                        if (inv.AffiliationCompanyAliasId is int affId
                            && !perEpisodeAffiliationIds.Contains(affId))
                        {
                            perEpisodeAffiliationIds.Add(affId);
                        }
                    }
                    else
                    {
                        hasSeriesScope = true;
                        if (inv.Kind == InvolvementKind.CharacterVoice && inv.CharacterAliasId.HasValue)
                        {
                            string? name = await ResolveCharacterNameAsync(inv.CharacterAliasId.Value, ct).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(name) && !seriesScopeCharacterNames.Contains(name))
                                seriesScopeCharacterNames.Add(name);
                        }
                        if (inv.AffiliationCompanyAliasId is int affIdS
                            && !seriesScopeAffiliationIds.Contains(affIdS))
                        {
                            seriesScopeAffiliationIds.Add(affIdS);
                        }
                    }
                }

                // シリーズ内の全話数（圧縮表記の「(全話)」判定用）。
                var allSeriesEpNos = _ctx.EpisodesBySeries.TryGetValue(bySeries.Key, out var allEps)
                    ? allEps.Select(e => e.SeriesEpNo).ToList()
                    : new List<int>();

                // v1.3.0 続編：所属屋号 ID 集合を表示名（テンプレ用ラベル）に解決する。
                // 屋号名は company_aliases.name 由来（display_text_override は使わない、当該人物の所属としての
                // 自然な屋号名を出すため）。複数屋号がある場合は「、」で連結。
                // 1 件も無いシリーズ行では空文字を返す（テンプレ側で「(屋号名)」全体を非表示にする）。
                async Task<string> ResolveAffLabelAsync(IReadOnlyList<int> ids)
                {
                    if (ids.Count == 0) return "";
                    var names = new List<string>(ids.Count);
                    foreach (var id in ids)
                    {
                        var name = await GetCompanyAliasNameAsync(id, ct).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(name)) names.Add(name!);
                    }
                    return string.Join("、", names);
                }

                // (a) シリーズ全体スコープの 1 行（あれば先に出す）。
                if (hasSeriesScope)
                {
                    seriesRows.Add(new InvolvementSeriesRow
                    {
                        SeriesSlug = series.Slug,
                        SeriesTitle = series.Title,
                        RangeLabel = "（シリーズ全体）",
                        IsAllEpisodes = false,
                        CharacterNames = string.Join("、", seriesScopeCharacterNames),
                        AffiliationsLabel = await ResolveAffLabelAsync(seriesScopeAffiliationIds).ConfigureAwait(false)
                    });
                }

                // (b) エピソード単位の集約 1 行（話数があれば）。
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
                        CharacterNames = string.Join("、", perEpisodeCharacterNames),
                        AffiliationsLabel = await ResolveAffLabelAsync(perEpisodeAffiliationIds).ConfigureAwait(false)
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
                HasCharacterColumn = seriesRows.Any(r => !string.IsNullOrEmpty(r.CharacterNames))
            });
        }
        return groups;
    }

    private async Task<string?> ResolveCharacterNameAsync(int aliasId, CancellationToken ct)
    {
        if (_characterAliasCache.TryGetValue(aliasId, out var hit))
            return hit?.Name;
        var ca = await _characterAliasesRepo.GetByIdAsync(aliasId, ct).ConfigureAwait(false);
        _characterAliasCache[aliasId] = ca;
        return ca?.Name;
    }

    /// <summary>
    /// 当該人物がクレジットされた役職を「早い順」で最大 3 件並べた表示ラベルを作る（v1.3.0 続編で追加）。
    /// <para>
    /// 順序判定の基準：各役職 RoleCode について、その役職で当該人物が登場した最も早い
    /// (シリーズ放送開始日, シリーズ内話数) のペア。シリーズ全体スコープ（episode_id=null）は
    /// 話数を 0 として優先扱い（シリーズ単位で「最初から」関与した役職を上位に出すため）。
    /// </para>
    /// <para>
    /// 表示順は早い順、表示は役職表示名（NameJa 優先）を「・」で連結。
    /// 4 件以上ある場合は先頭 3 件 + 「他 N 役職」を末尾に付ける（例「シリーズディレクター・脚本・絵コンテ 他 2 役職」）。
    /// 役職コードが空のケース・マスタに無いケースは除外する。
    /// </para>
    /// </summary>
    private string BuildPersonRolesLabel(IReadOnlyList<int> aliasIds)
    {
        if (aliasIds.Count == 0 || _roleMap is null) return "";

        // RoleCode → (シリーズ放送開始日, シリーズ内話数) の最小値。同役職の最早登場を見るための辞書。
        var earliestByRole = new Dictionary<string, (DateOnly Start, int EpNo)>(StringComparer.Ordinal);

        foreach (var aliasId in aliasIds)
        {
            if (!_index.ByPersonAlias.TryGetValue(aliasId, out var invs)) continue;
            foreach (var inv in invs)
            {
                // 人物そのものの役職に限定（屋号やロゴ経由のものは混ぜない）。
                if (inv.Kind != InvolvementKind.Person && inv.Kind != InvolvementKind.CharacterVoice) continue;
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
                    // シリーズスコープは最早扱い（シリーズ単位のクレジットを上位に）。
                    epNo = 0;
                }

                if (!earliestByRole.TryGetValue(roleCode, out var cur)
                    || start < cur.Start
                    || (start == cur.Start && epNo < cur.EpNo))
                {
                    earliestByRole[roleCode] = (start, epNo);
                }
            }
        }

        if (earliestByRole.Count == 0) return "";

        // 早い順に並べ、最大 3 件を表示名で取り出す。
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
    /// company_alias_id から屋号名を引く（v1.3.0 続編で追加、内部キャッシュ付き）。
    /// クレジット履歴の所属屋号併記用。未登録 ID には null をキャッシュして再問合せを避ける。
    /// </summary>
    private async Task<string?> GetCompanyAliasNameAsync(int aliasId, CancellationToken ct)
    {
        if (_companyAliasNameCache.TryGetValue(aliasId, out var hit)) return hit;
        var ca = await _companyAliasesRepo.GetByIdAsync(aliasId).ConfigureAwait(false);
        var name = ca?.Name;
        _companyAliasNameCache[aliasId] = name;
        return name;
    }

    /// <summary>シリーズ ID から放送開始日を引く（並び替え用、未登録時は MaxValue）。</summary>
    private DateOnly SeriesStartDate(int seriesId)
        => _ctx.SeriesById.TryGetValue(seriesId, out var s) ? s.StartDate : DateOnly.MaxValue;

    /// <summary>シリーズ ID + エピソード ID から SeriesEpNo を引く（並び替え用、未登録時は int.MaxValue）。</summary>
    private int EpisodeSeriesEpNo(int seriesId, int episodeId)
    {
        if (episodeId == 0) return -1; // シリーズスコープは先頭に
        var ep = LookupEpisode(seriesId, episodeId);
        return ep?.SeriesEpNo ?? int.MaxValue;
    }

    /// <summary>シリーズ × エピソード ID からエピソード本体を引く。</summary>
    private Episode? LookupEpisode(int seriesId, int episodeId)
    {
        if (!_ctx.EpisodesBySeries.TryGetValue(seriesId, out var eps)) return null;
        for (int i = 0; i < eps.Count; i++)
            if (eps[i].EpisodeId == episodeId) return eps[i];
        return null;
    }

    /// <summary>DateOnly?を「2004年2月1日」形式にフォーマット。null は空文字。</summary>
    private static string FormatDate(DateOnly? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    /// <summary>DateTime? 版（PersonAlias.ValidFrom/ValidTo が DateTime? のため）。</summary>
    private static string FormatDate(DateTime? d)
        => d.HasValue ? $"{d.Value.Year}年{d.Value.Month}月{d.Value.Day}日" : "";

    // ─── テンプレ用 DTO 群 ───

    private sealed class PersonIndexModel
    {
        public IReadOnlyList<PersonIndexRow> Persons { get; set; } = Array.Empty<PersonIndexRow>();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        /// <summary>
        /// クレジット横断カバレッジラベル（v1.3.0 ブラッシュアップ続編で追加）。
        /// テンプレ側の lead 段落末尾に表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class PersonIndexRow
    {
        public int PersonId { get; set; }
        public string DisplayName { get; set; } = "";
        public string DisplayKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public bool HasInvolvement { get; set; }
        /// <summary>
        /// クレジットされた役職の表示ラベル（v1.3.0 続編で追加）。
        /// 「最も早い時期にクレジットされた役職」から順に最大 3 件、超える場合は「他 N 役職」を末尾に付ける。
        /// 例：「シリーズディレクター・脚本・絵コンテ 他 2 役職」。
        /// クレジット 0 件の人物では空文字。
        /// </summary>
        public string RolesLabel { get; set; } = "";
    }

    private sealed class PersonDetailModel
    {
        public PersonView Person { get; set; } = new();
        public IReadOnlyList<PersonAliasView> Aliases { get; set; } = Array.Empty<PersonAliasView>();
        public IReadOnlyList<InvolvementGroup> InvolvementGroups { get; set; } = Array.Empty<InvolvementGroup>();
        /// <summary>
        /// クレジット横断カバレッジラベル（v1.3.0 ブラッシュアップ続編で追加）。
        /// テンプレ側の h1 ブロック直後に独立段落で表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    private sealed class PersonView
    {
        public int PersonId { get; set; }
        public string DisplayName { get; set; } = "";
        public string FullName { get; set; } = "";
        public string FullNameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class PersonAliasView
    {
        public int AliasId { get; set; }
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string NameEn { get; set; } = "";
        public string ValidFrom { get; set; } = "";
        public string ValidTo { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}

/// <summary>
/// 役職別の関与グループ（v1.3.0 ブラッシュアップ続編で行構造を「シリーズ単位 + 話数圧縮」に再設計）。
/// </summary>
internal sealed class InvolvementGroup
{
    public string RoleCode { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    /// <summary>
    /// シリーズ単位の集約行群。各行はそのシリーズ内での話数集合を圧縮表記で持つ。
    /// </summary>
    public IReadOnlyList<InvolvementSeriesRow> SeriesRows { get; set; } = Array.Empty<InvolvementSeriesRow>();
    /// <summary>役職グループ内の合計担当エピソード数（補助情報、"N 話" の小見出し用）。</summary>
    public int Count { get; set; }
    /// <summary>このグループ内に CharacterNames が設定された行が 1 件以上あるか（声優役判定）。</summary>
    public bool HasCharacterColumn { get; set; }
}

/// <summary>
/// シリーズ単位の関与 1 行（v1.3.0 ブラッシュアップ続編で新設）。
/// 旧 InvolvementRow（エピソードごと 1 行）はテンプレ移行に伴い廃止。
/// </summary>
internal sealed class InvolvementSeriesRow
{
    public string SeriesSlug { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    /// <summary>
    /// 話数圧縮表記。例：「#1〜4, 8」。全話担当なら空文字（テンプレ側で「(全話)」マークを別途出す）。
    /// シリーズ全体スコープのときは「（シリーズ全体）」のような任意ラベルを入れる。
    /// </summary>
    public string RangeLabel { get; set; } = "";
    /// <summary>シリーズ内の全話を担当しているフラグ。テンプレで「(全話)」マークを出すかの判定に使う。</summary>
    public bool IsAllEpisodes { get; set; }
    /// <summary>声優関与のとき演じたキャラ名（シリーズ内連名、「、」連結）。それ以外は空。</summary>
    public string CharacterNames { get; set; } = "";
    /// <summary>
    /// 当該シリーズで当該人物がクレジットされた所属屋号の表示ラベル（v1.3.0 続編で追加）。
    /// 例：「東映アニメーション」。複数屋号にまたがる場合は「、」連結（例「東映アニメーション、ぴえろ」）。
    /// 屋号付きクレジットが 0 件のシリーズ行では空文字。テンプレ側で空チェックして
    /// 「○○（屋号名）」の () 付き併記を出すかどうかを切り替える。
    /// </summary>
    public string AffiliationsLabel { get; set; } = "";
}
