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
    /// <summary>
    /// 主題歌種別マスタ（song_music_classes）を読むためのリポジトリ（v1.3.0 続編で追加）。
    /// クレジット履歴で主題歌系の役職に「オープニング主題歌」「エンディング主題歌」「挿入歌」の
    /// プレフィックスを付与する際、コード値（OP / ED / INSERT）から日本語表示名を引くために使う。
    /// episode_theme_songs.theme_kind は形式上 ENUM だが、コード値が song_music_classes.class_code と
    /// 一致する運用なので、マスタの name_ja をそのまま流用する。
    /// </summary>
    private readonly SongMusicClassesRepository _songMusicClassesRepo;

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

    /// <summary>
    /// 主題歌種別コード（OP / ED / INSERT 等）→ SongMusicClass モデル のマップ（v1.3.0 続編で追加）。
    /// クレジット履歴で「オープニング主題歌 作曲」のようなラベルを組み立てるための辞書。
    /// <c>GenerateAsync</c> で 1 度だけロードして使い回す。
    /// </summary>
    private IReadOnlyDictionary<string, SongMusicClass>? _songMusicClassMap;

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
        _songMusicClassesRepo = new SongMusicClassesRepository(factory);
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

        // v1.3.0 続編：主題歌種別マスタ（OP / ED / INSERT 等のコード値 → 日本語表示名）も引く。
        // クレジット履歴で「オープニング主題歌 作曲」のような展開を組み立てるための辞書。
        // 形式上は episode_theme_songs.theme_kind が ENUM だが、コード値が song_music_classes.class_code と
        // 一致する運用なので、マスタ側の name_ja を流用して自然な日本語ラベルに展開する。
        if (_songMusicClassMap is null)
        {
            var allClasses = await _songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _songMusicClassMap = allClasses.ToDictionary(c => c.ClassCode, StringComparer.Ordinal);
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

        // 索引ページ用 DTO。各人物に「代表名義」「関与エピソード数」「初クレジットシリーズ」を載せる。
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

            // v1.3.0 続編：初クレジット順タブのソート/セクション分け用に、当該人物が
            // 最も早くクレジットされたシリーズと、その中での最早話数を求める。
            // 役職ラベルと同じく Person / CharacterVoice 種別の Involvement だけを対象にする。
            // クレジット 0 件の人物は (null, 0) を返し、初クレジット順タブから自動的に除外される。
            (int? firstSeriesId, int firstEpNoInSeries) = ComputeFirstCreditSeries(aliasIds);

            indexRows.Add(new PersonIndexRow
            {
                PersonId = p.PersonId,
                DisplayName = displayName,
                DisplayKana = displayKana ?? "",
                EpisodeCount = episodeCount,
                HasInvolvement = episodeCount > 0,
                RolesLabel = rolesLabel,
                FirstCreditSeriesId = firstSeriesId,
                FirstCreditSeriesEpNo = firstEpNoInSeries
            });
        }

        // 50音順タブ用の基本ソート：kana 昇順 → 名前 Ordinal 昇順。kana 空は末尾。
        // 後段の KanaSections は本リストをそのままサブグループ化するだけ。
        indexRows = indexRows
            .OrderBy(r => string.IsNullOrEmpty(r.DisplayKana) ? 1 : 0)
            .ThenBy(r => r.DisplayKana, StringComparer.Ordinal)
            .ThenBy(r => r.DisplayName, StringComparer.Ordinal)
            .ToList();

        // 50音セクション分け：先頭文字を「あ・か・さ・た・な・は・ま・や・ら・わ・その他」に正規化。
        // 濁音・半濁音・カタカナは清音ひらがなに寄せた上でセクションキーを決める。
        // kana 空（読み未登録）の人物は「その他」セクション末尾に並ぶ。
        var kanaSections = BuildKanaSections(indexRows);

        // 初クレジット順セクション：FirstCreditSeriesId が null（=クレジット 0 件）の人物は出さない。
        // セクション順はシリーズ放送開始日昇順、各セクション内は最早話数 → kana → 名前 の順。
        var debutSections = BuildDebutSections(indexRows);

        // 索引ページ書き出し。
        var indexContent = new PersonIndexModel
        {
            KanaSections = kanaSections,
            DebutSections = debutSections,
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

        // v1.3.0 続編：グループ分けキーを「カテゴリプレフィックスコード × 役職コード」の複合に拡張。
        // カテゴリプレフィックスコードは以下のルールで導出する：
        //   - EntryKind = "SONG_CREDIT" / "RECORDING_SINGER" → ThemeKind（OP / ED / INSERT）
        //   - EntryKind = "BGM_CUE_CREDIT" → "BGM"
        //   - それ以外（credit_block_entries 由来の脚本・演出・声優キャストなど）→ ""（プレフィックス無し）
        // これにより、たとえば同一人物が OP の作曲と ED の作曲を両方担当している場合、
        // 「オープニング主題歌 作曲」と「エンディング主題歌 作曲」が独立した役職セクションとして
        // 別個に表示される（旧仕様では両方が「作曲」セクションに合流していた）。
        static string CategoryPrefixOf(Involvement inv)
        {
            if (string.Equals(inv.EntryKind, "SONG_CREDIT", StringComparison.Ordinal)
                || string.Equals(inv.EntryKind, "RECORDING_SINGER", StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(inv.ThemeKind) ? "" : inv.ThemeKind!;
            }
            if (string.Equals(inv.EntryKind, "BGM_CUE_CREDIT", StringComparison.Ordinal))
            {
                return "BGM";
            }
            return "";
        }

        // カテゴリプレフィックスの表示順：プレフィックス無し → OP → ED → INSERT → BGM。
        // song_music_classes.display_order を踏襲し、BGM はマスタ外なので末尾固定。
        int CategoryOrder(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return 0;
            if (string.Equals(prefix, "BGM", StringComparison.Ordinal)) return 99;
            if (_songMusicClassMap is not null && _songMusicClassMap.TryGetValue(prefix, out var m))
            {
                return m.DisplayOrder is byte d ? d : 100;
            }
            return 100;
        }

        // 役職ラベルに付与するカテゴリプレフィックスの日本語表現。
        //   - "OP"/"ED"/"INSERT" → song_music_classes.name_ja（オープニング主題歌／エンディング主題歌／挿入歌）
        //   - "BGM" → "劇伴"（マスタ外なのでハードコード）
        //   - 空文字 → 空文字
        // マスタ未登録の不明コードはコード値そのままで暫定表示し、データ不整合を視覚的に検知できるようにする。
        string CategoryPrefixLabel(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return "";
            if (string.Equals(prefix, "BGM", StringComparison.Ordinal)) return "劇伴";
            if (_songMusicClassMap is not null && _songMusicClassMap.TryGetValue(prefix, out var m)
                && !string.IsNullOrEmpty(m.NameJa))
            {
                return m.NameJa;
            }
            return prefix;
        }

        var groups = new List<InvolvementGroup>();
        foreach (var roleGroup in all
            .GroupBy(i => (Prefix: CategoryPrefixOf(i), Role: i.RoleCode))
            .OrderBy(g => CategoryOrder(g.Key.Prefix))
            .ThenBy(g => RoleOrder(g.Key.Role)))
        {
            string categoryPrefix = roleGroup.Key.Prefix;
            string roleCode = roleGroup.Key.Role;
            string baseRoleLabel = string.IsNullOrEmpty(roleCode) ? "(役職未設定)"
                : (_roleMap!.TryGetValue(roleCode, out var r) ? (r.NameJa ?? roleCode) : roleCode);
            // カテゴリプレフィックスがある場合は「オープニング主題歌 作曲」のように半角スペース区切りで連結。
            string prefixLabel = CategoryPrefixLabel(categoryPrefix);
            string roleLabel = string.IsNullOrEmpty(prefixLabel)
                ? baseRoleLabel
                : $"{prefixLabel} {baseRoleLabel}";

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
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
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
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
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
    /// 当該人物（紐付く全 alias_id）について、最も早くクレジットされたシリーズと
    /// そのシリーズ内の最早話数を求める（v1.3.0 続編で追加、初クレジット順タブのソート用）。
    /// <para>
    /// 評価対象は <see cref="InvolvementKind.Person"/> / <see cref="InvolvementKind.CharacterVoice"/> のみ。
    /// シリーズ全体スコープ（EpisodeId=null）の関与は話数 0 として優先扱い。
    /// クレジット 0 件の人物は (null, 0) を返す（初クレジット順タブから自動的に除外される）。
    /// </para>
    /// </summary>
    private (int? SeriesId, int EpNo) ComputeFirstCreditSeries(IReadOnlyList<int> aliasIds)
    {
        int? bestSeriesId = null;
        DateOnly bestStart = DateOnly.MaxValue;
        int bestEpNo = int.MaxValue;

        foreach (var aliasId in aliasIds)
        {
            if (!_index.ByPersonAlias.TryGetValue(aliasId, out var invs)) continue;
            foreach (var inv in invs)
            {
                if (inv.Kind != InvolvementKind.Person && inv.Kind != InvolvementKind.CharacterVoice) continue;
                var start = SeriesStartDate(inv.SeriesId);
                int epNo;
                if (inv.EpisodeId is int eid)
                {
                    var ep = LookupEpisode(inv.SeriesId, eid);
                    epNo = ep?.SeriesEpNo ?? int.MaxValue;
                }
                else
                {
                    // シリーズスコープは最早扱い。
                    epNo = 0;
                }

                if (start < bestStart || (start == bestStart && epNo < bestEpNo))
                {
                    bestSeriesId = inv.SeriesId;
                    bestStart = start;
                    bestEpNo = epNo;
                }
            }
        }
        return (bestSeriesId, bestSeriesId.HasValue ? bestEpNo : 0);
    }

    /// <summary>
    /// 50音順タブの 11 セクション（あ・か・さ・た・な・は・ま・や・ら・わ・その他）を組み立てる
    /// （v1.3.0 続編で追加）。
    /// <para>
    /// 入力 <paramref name="rows"/> は kana 昇順で既に整列済みの前提。各人物の <c>DisplayKana</c> の
    /// 先頭文字を <see cref="GetKanaRowKey"/> で正規化してセクションキーを決め、対応するセクションに
    /// 振り分ける。読み未登録（kana 空）の人物は「その他」セクション末尾に並ぶ。
    /// 0 件のセクション（メンバーなし）は出力から除外する。
    /// </para>
    /// </summary>
    private static IReadOnlyList<PersonIndexKanaSection> BuildKanaSections(IReadOnlyList<PersonIndexRow> rows)
    {
        // セクションキーの順序定義。
        string[] order = ["あ", "か", "さ", "た", "な", "は", "ま", "や", "ら", "わ", "その他"];
        var buckets = new Dictionary<string, List<PersonIndexRow>>(StringComparer.Ordinal);
        foreach (var k in order) buckets[k] = new List<PersonIndexRow>();

        foreach (var r in rows)
        {
            string key = GetKanaRowKey(r.DisplayKana);
            if (!buckets.TryGetValue(key, out var list)) buckets["その他"].Add(r);
            else list.Add(r);
        }

        var sections = new List<PersonIndexKanaSection>(order.Length);
        foreach (var key in order)
        {
            var list = buckets[key];
            if (list.Count == 0) continue;
            sections.Add(new PersonIndexKanaSection
            {
                Label = key,
                SectionId = $"persons-kana-{key}",
                Members = list
            });
        }
        return sections;
    }

    /// <summary>
    /// 初クレジット順タブのセクション群を組み立てる（v1.3.0 続編で追加）。
    /// <para>
    /// 対象は <c>FirstCreditSeriesId</c> が非 null の人物のみ（クレジット 0 件は除外）。
    /// セクションキー = シリーズ ID、見出し = シリーズタイトル + 開始年。
    /// セクション順はシリーズ放送開始日昇順。各セクション内は「初登場話数 → kana → 名前」の順。
    /// シリーズが <c>BuildContext.SeriesById</c> に存在しないシリーズ ID は無視する（不整合データ対策）。
    /// </para>
    /// </summary>
    private IReadOnlyList<PersonIndexDebutSection> BuildDebutSections(IReadOnlyList<PersonIndexRow> rows)
    {
        var bySeries = rows
            .Where(r => r.FirstCreditSeriesId.HasValue)
            .GroupBy(r => r.FirstCreditSeriesId!.Value)
            .ToList();

        var sections = new List<PersonIndexDebutSection>();
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
            sections.Add(new PersonIndexDebutSection
            {
                SeriesTitle = series.Title,
                SeriesSlug = series.Slug,
                SeriesStartYearLabel = series.StartDate.Year.ToString(),
                SectionId = $"persons-debut-{idx}",
                Members = members
            });
        }
        return sections;
    }

    /// <summary>
    /// 読み（kana）の先頭文字を「あ・か・さ・た・な・は・ま・や・ら・わ・その他」のいずれかに正規化する
    /// （v1.3.0 続編で追加、50音順タブのセクション分け用）。
    /// <para>
    /// カタカナはひらがなに、濁音・半濁音は清音に寄せて判定する。
    /// 「ヴ」「ぁ・ぃ・ぅ・ぇ・ぉ」「っ」などの小書き仮名・特殊仮名は対応する清音の行に振り分ける。
    /// 「ん／ン」は「わ」行に含める運用（一般的な索引と同じ流儀）。kana 空または対象外文字は「その他」。
    /// </para>
    /// </summary>
    internal static string GetKanaRowKey(string kana)
    {
        if (string.IsNullOrEmpty(kana)) return "その他";
        char c0 = kana[0];

        // カタカナ範囲（U+30A1〜U+30F6）はひらがな（U+3041〜U+3096）に寄せる。
        // ヴ（U+30F4）はひらがな寄せ後 'ゔ' になり、後段で「わ」行扱い（後述）。
        if (c0 >= '\u30A1' && c0 <= '\u30F6')
        {
            c0 = (char)(c0 - 0x60);
        }

        // 「ゔ」「ゐ」「ゑ」「を」など特殊仮名はまとめて事前判定。
        // 'ゔ'（U+3094）は「わ」行（ヴァ行）に寄せる慣習に従う。
        if (c0 == '\u3094') return "わ"; // ゔ
        if (c0 == '\u3090') return "あ"; // ゐ（い段）
        if (c0 == '\u3091') return "あ"; // ゑ（え段）

        // ひらがな範囲外の場合は「その他」（数字・アルファベット・記号など）。
        if (c0 < '\u3041' || c0 > '\u3093') return "その他";

        // ひらがなコードポイントから「あ・か・さ・た・な・は・ま・や・ら・わ」のどれに属するかを判定。
        // U+3041〜U+304A: ぁ-お → あ
        // U+304B〜U+3054: か-ご → か
        // U+3055〜U+305E: さ-ぞ → さ
        // U+305F〜U+3069: た-ど → た（っ U+3063 含む）
        // U+306A〜U+306E: な-の → な
        // U+306F〜U+307D: は-ぽ → は
        // U+307E〜U+3082: ま-も → ま
        // U+3083〜U+3088: ゃ-よ → や
        // U+3089〜U+308D: ら-ろ → ら
        // U+308E〜U+3093: ゎ・わ・ゐ・ゑ・を・ん → わ（ゐ・ゑ は上で別判定）
        if (c0 <= '\u304A') return "あ";
        if (c0 <= '\u3054') return "か";
        if (c0 <= '\u305E') return "さ";
        if (c0 <= '\u3069') return "た";
        if (c0 <= '\u306E') return "な";
        if (c0 <= '\u307D') return "は";
        if (c0 <= '\u3082') return "ま";
        if (c0 <= '\u3088') return "や";
        if (c0 <= '\u308D') return "ら";
        return "わ";
    }

    /// <summary>
    /// 当該人物がクレジットされた役職を「早い順」で最大 3 件並べた表示ラベルを作る（v1.3.0 続編で追加）。
    /// <para>
    /// 順序判定の基準：各「役職（カテゴリプレフィックス × RoleCode）」について、その役職で当該人物が
    /// 登場した最も早い (シリーズ放送開始日, シリーズ内話数) のペア。シリーズ全体スコープ
    /// （episode_id=null）は話数を 0 として優先扱い（シリーズ単位で「最初から」関与した役職を上位に
    /// 出すため）。
    /// </para>
    /// <para>
    /// v1.3.0 続編改修：人物詳細クレジット履歴と同様に、主題歌系（SONG_CREDIT / RECORDING_SINGER）と
    /// 劇伴系（BGM_CUE_CREDIT）の役職には、それぞれ「オープニング主題歌」「エンディング主題歌」「挿入歌」
    /// 「劇伴」のカテゴリプレフィックスを付与する。これによって、たとえば OP の作曲と ED の作曲を
    /// 兼任した作曲家は、人物一覧の役職カラムでも「オープニング主題歌 作曲・エンディング主題歌 作曲」
    /// のように両者が区別されて並ぶ。一覧と詳細でラベル表記を合わせる目的。
    /// </para>
    /// <para>
    /// 表示順は早い順、表示は役職表示名（NameJa 優先、プレフィックスがあれば連結）を「・」で連結。
    /// 4 件以上ある場合は先頭 3 件 + 「他 N 役職」を末尾に付ける（例「シリーズディレクター・脚本・絵コンテ 他 2 役職」）。
    /// 役職コードが空のケース・マスタに無いケースは除外する。
    /// </para>
    /// </summary>
    private string BuildPersonRolesLabel(IReadOnlyList<int> aliasIds)
    {
        if (aliasIds.Count == 0 || _roleMap is null) return "";

        // (カテゴリプレフィックス, RoleCode) → (シリーズ放送開始日, シリーズ内話数) の最小値。
        // 主題歌の OP / ED / INSERT / 劇伴 / プレフィックス無し を別個のキーで扱うため、複合キー化。
        var earliestByRole = new Dictionary<(string Prefix, string Role), (DateOnly Start, int EpNo)>();

        // BuildPersonInvolvementGroupsAsync と同じ判定ロジックでプレフィックスを導出する
        // （主題歌の OP/ED/INSERT、bgm の BGM、それ以外は空文字）。
        static string CategoryPrefixOf(Involvement inv)
        {
            if (string.Equals(inv.EntryKind, "SONG_CREDIT", StringComparison.Ordinal)
                || string.Equals(inv.EntryKind, "RECORDING_SINGER", StringComparison.Ordinal))
            {
                return string.IsNullOrEmpty(inv.ThemeKind) ? "" : inv.ThemeKind!;
            }
            if (string.Equals(inv.EntryKind, "BGM_CUE_CREDIT", StringComparison.Ordinal))
            {
                return "BGM";
            }
            return "";
        }

        foreach (var aliasId in aliasIds)
        {
            if (!_index.ByPersonAlias.TryGetValue(aliasId, out var invs)) continue;
            foreach (var inv in invs)
            {
                // 人物そのものの役職に限定（屋号やロゴ経由のものは混ぜない）。
                if (inv.Kind != InvolvementKind.Person && inv.Kind != InvolvementKind.CharacterVoice) continue;
                var roleCode = inv.RoleCode;
                if (string.IsNullOrEmpty(roleCode)) continue;
                var prefix = CategoryPrefixOf(inv);

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

                var key = (prefix, roleCode);
                if (!earliestByRole.TryGetValue(key, out var cur)
                    || start < cur.Start
                    || (start == cur.Start && epNo < cur.EpNo))
                {
                    earliestByRole[key] = (start, epNo);
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

        // (Prefix, Role) → 表示ラベル。プレフィックスがあれば「{プレフィックス} {役職名}」で組み立てる。
        string LabelFor((string Prefix, string Role) k)
        {
            string baseLabel = _roleMap!.TryGetValue(k.Role, out var r) ? (r.NameJa ?? k.Role) : k.Role;
            if (string.IsNullOrEmpty(k.Prefix)) return baseLabel;
            if (string.Equals(k.Prefix, "BGM", StringComparison.Ordinal)) return $"劇伴 {baseLabel}";
            if (_songMusicClassMap is not null && _songMusicClassMap.TryGetValue(k.Prefix, out var m)
                && !string.IsNullOrEmpty(m.NameJa))
            {
                return $"{m.NameJa} {baseLabel}";
            }
            return $"{k.Prefix} {baseLabel}";
        }

        const int Top = 3;
        var topRoles = ordered.Take(Top).Select(LabelFor).ToList();
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
        /// <summary>
        /// 50音順タブ用セクション群（v1.3.0 続編で追加）。
        /// 各セクションは「あ・か・さ・た・な・は・ま・や・ら・わ・その他」のいずれかの見出し文字を持ち、
        /// 配下に対応する人物の <see cref="PersonIndexRow"/> を kana 昇順で並べる。
        /// クレジット 0 件の人物も含めて全人物が必ずどこかのセクションに属する。
        /// </summary>
        public IReadOnlyList<PersonIndexKanaSection> KanaSections { get; set; } = Array.Empty<PersonIndexKanaSection>();
        /// <summary>
        /// 初クレジット順タブ用セクション群（v1.3.0 続編で追加）。
        /// 各セクションは「当該人物が初めてクレジットされたシリーズ」を見出しに持ち、配下にその
        /// シリーズで初クレジットされた人物の <see cref="PersonIndexRow"/> を初登場話数昇順で並べる。
        /// セクションの並びはシリーズ放送開始日昇順。クレジット 0 件の人物はそもそもこのタブに
        /// 載せない（「クレジット未登録」セクションを作る運用はしない）。
        /// </summary>
        public IReadOnlyList<PersonIndexDebutSection> DebutSections { get; set; } = Array.Empty<PersonIndexDebutSection>();
        public int TotalCount { get; set; }
        public int ActiveCount { get; set; }
        /// <summary>
        /// クレジット横断カバレッジラベル（v1.3.0 ブラッシュアップ続編で追加）。
        /// テンプレ側の lead 段落末尾に表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>
    /// 50音順タブの 1 セクション（v1.3.0 続編で追加）。
    /// </summary>
    private sealed class PersonIndexKanaSection
    {
        /// <summary>セクション見出し（あ・か・さ・た・な・は・ま・や・ら・わ・その他）。</summary>
        public string Label { get; set; } = "";
        /// <summary>セクション ID（テンプレ側でアンカーリンク・section-nav.js の dt 用）。"persons-kana-{Label}" 形式。</summary>
        public string SectionId { get; set; } = "";
        /// <summary>当該セクションに属する人物行（kana 昇順 → 名前 Ordinal 昇順）。</summary>
        public IReadOnlyList<PersonIndexRow> Members { get; set; } = Array.Empty<PersonIndexRow>();
    }

    /// <summary>
    /// 初クレジット順タブの 1 セクション（v1.3.0 続編で追加）。
    /// </summary>
    private sealed class PersonIndexDebutSection
    {
        /// <summary>セクション見出し（シリーズタイトル）。</summary>
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズスラッグ（h2 のシリーズ詳細リンク用）。</summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>シリーズ開始年（4 桁、見出し脇の小書きで表示）。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>セクション ID（テンプレ側でアンカーリンク・section-nav.js の dt 用）。"persons-debut-{idx}" 形式。</summary>
        public string SectionId { get; set; } = "";
        /// <summary>当該シリーズで初クレジットされた人物行（最早話数 → kana → 名前 の順）。</summary>
        public IReadOnlyList<PersonIndexRow> Members { get; set; } = Array.Empty<PersonIndexRow>();
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
        /// <summary>
        /// 初クレジット順タブで使うソート/セクション分けキー：当該人物が初めてクレジットされた
        /// シリーズの ID（v1.3.0 続編で追加）。クレジット 0 件の人物では null。
        /// </summary>
        public int? FirstCreditSeriesId { get; set; }
        /// <summary>
        /// 上記シリーズ内での初登場話数（v1.3.0 続編で追加、初クレジット順タブのセクション内ソート用）。
        /// シリーズ全体スコープからの関与は 0 として優先扱い。クレジット 0 件の人物では 0。
        /// </summary>
        public int FirstCreditSeriesEpNo { get; set; }
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
    /// シリーズ開始年の西暦 4 桁文字列（例: "2004"）。v1.3.0 stage22 後段で追加。
    /// クレジット履歴・声の出演履歴の各シリーズ行の表記で、シリーズ名直後に
    /// 薄色括弧で添える表現に使う（略称（series.title_short）は一切使わない）。
    /// </summary>
    public string SeriesStartYearLabel { get; set; } = "";
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
