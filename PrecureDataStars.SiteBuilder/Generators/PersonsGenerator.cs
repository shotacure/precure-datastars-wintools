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

            indexRows.Add(new PersonIndexRow
            {
                PersonId = p.PersonId,
                DisplayName = displayName,
                DisplayKana = displayKana ?? "",
                EpisodeCount = episodeCount,
                HasInvolvement = episodeCount > 0
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
            ActiveCount = indexRows.Count(r => r.HasInvolvement)
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
            InvolvementGroups = involvementGroups
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
    /// 人物に紐付く alias_id 群から関与情報を集約し、役職別のグループに編成する。
    /// </summary>
    private async Task<IReadOnlyList<InvolvementGroup>> BuildPersonInvolvementGroupsAsync(
        IReadOnlyList<int> aliasIds,
        CancellationToken ct)
    {
        // 全関与を 1 リストに合算（重複は (SeriesId, EpisodeId, RoleCode) で集約）。
        var all = aliasIds
            .Where(_index.ByPersonAlias.ContainsKey)
            .SelectMany(id => _index.ByPersonAlias[id])
            .ToList();
        if (all.Count == 0) return Array.Empty<InvolvementGroup>();

        // 役職コード単位でグルーピング（声優関与は CHARACTER_VOICE と PERSON で同じ role_code でも
        // 種別が違うので分けたいが、当面は role_code でまとめる方針。
        // 各役職セクション内で「シリーズ → エピソード」順）。
        var byRole = all
            .GroupBy(i => i.RoleCode)
            .ToList();

        // 役職表示順は role_map.DisplayOrder 昇順、未登録 / 空コードは末尾。
        int RoleOrder(string code)
        {
            if (string.IsNullOrEmpty(code)) return int.MaxValue;
            if (_roleMap!.TryGetValue(code, out var r) && r.DisplayOrder is ushort d) return d;
            return int.MaxValue - 1;
        }

        var groups = new List<InvolvementGroup>();
        foreach (var g in byRole.OrderBy(g => RoleOrder(g.Key)))
        {
            string roleCode = g.Key;
            string roleLabel = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                : (_roleMap!.TryGetValue(roleCode, out var r) ? (r.NameJa ?? roleCode) : roleCode);

            // (SeriesId, EpisodeId) ごとに 1 行。声優関与のとき CharacterAliasId をエピソード単位で
            // 1 つ採用する（同一エピソードで複数キャラを演じた場合は「、」で連結）。
            var rows = new List<InvolvementRow>();
            foreach (var bySeries in g
                .GroupBy(i => i.SeriesId)
                .OrderBy(sg => SeriesStartDate(sg.Key)))
            {
                if (!_ctx.SeriesById.TryGetValue(bySeries.Key, out var series)) continue;
                foreach (var byEp in bySeries
                    .GroupBy(i => i.EpisodeId ?? 0)
                    .OrderBy(eg => EpisodeSeriesEpNo(bySeries.Key, eg.Key)))
                {
                    string episodeLabel;
                    string episodeUrl;
                    int? episodeNo = null;
                    if (byEp.Key == 0)
                    {
                        episodeLabel = "（シリーズ全体）";
                        episodeUrl = PathUtil.SeriesUrl(series.Slug);
                    }
                    else
                    {
                        var ep = LookupEpisode(bySeries.Key, byEp.Key);
                        if (ep is null) continue;
                        episodeNo = ep.SeriesEpNo;
                        episodeLabel = $"第{ep.SeriesEpNo}話　{ep.TitleText}";
                        episodeUrl = PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo);
                    }

                    // CHARACTER_VOICE のとき演じたキャラ名を集めて文字列化。
                    var characterNames = new List<string>();
                    foreach (var inv in byEp.Where(x => x.Kind == InvolvementKind.CharacterVoice && x.CharacterAliasId.HasValue))
                    {
                        string? name = await ResolveCharacterNameAsync(inv.CharacterAliasId!.Value, ct).ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(name) && !characterNames.Contains(name))
                            characterNames.Add(name);
                    }

                    rows.Add(new InvolvementRow
                    {
                        SeriesSlug = series.Slug,
                        SeriesTitle = series.Title,
                        EpisodeLabel = episodeLabel,
                        EpisodeUrl = episodeUrl,
                        EpisodeSeriesEpNo = episodeNo,
                        CharacterNames = string.Join("、", characterNames)
                    });
                }
            }

            groups.Add(new InvolvementGroup
            {
                RoleCode = roleCode,
                RoleLabel = roleLabel,
                Rows = rows,
                Count = rows.Count,
                HasCharacterColumn = rows.Any(r => !string.IsNullOrEmpty(r.CharacterNames))
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
    }

    private sealed class PersonIndexRow
    {
        public int PersonId { get; set; }
        public string DisplayName { get; set; } = "";
        public string DisplayKana { get; set; } = "";
        public int EpisodeCount { get; set; }
        public bool HasInvolvement { get; set; }
    }

    private sealed class PersonDetailModel
    {
        public PersonView Person { get; set; } = new();
        public IReadOnlyList<PersonAliasView> Aliases { get; set; } = Array.Empty<PersonAliasView>();
        public IReadOnlyList<InvolvementGroup> InvolvementGroups { get; set; } = Array.Empty<InvolvementGroup>();
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

/// <summary>役職別グループ。</summary>
internal sealed class InvolvementGroup
{
    public string RoleCode { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    public IReadOnlyList<InvolvementRow> Rows { get; set; } = Array.Empty<InvolvementRow>();
    public int Count { get; set; }
    /// <summary>このグループ内に CharacterNames が設定された行が 1 件以上あるか（キャラクター列の表示判定）。</summary>
    public bool HasCharacterColumn { get; set; }
}

/// <summary>関与 1 行。</summary>
internal sealed class InvolvementRow
{
    public string SeriesSlug { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    public string EpisodeLabel { get; set; } = "";
    public string EpisodeUrl { get; set; } = "";
    public int? EpisodeSeriesEpNo { get; set; }
    /// <summary>声優関与のとき演じたキャラ名（連名は「、」連結）。それ以外は空。</summary>
    public string CharacterNames { get; set; } = "";
}
