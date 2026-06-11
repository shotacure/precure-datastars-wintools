using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>人物索引（/persons/）と人物詳細（/persons/{person_id}/）の生成。</summary>
public sealed class PersonsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasesRepository _aliasesRepo;
    private readonly PersonAliasPersonsRepository _aliasPersonsRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    private readonly RolesRepository _rolesRepo;
    /// <summary>屋号 alias_id → 屋号名 解決用。 クレジット履歴行に所属屋号併記を出すときに、Involvement に詰めた AffiliationCompanyAliasId から屋号名を引くために使う。</summary>
    private readonly CompanyAliasesRepository _companyAliasesRepo;
    /// <summary>
    /// 主題歌種別マスタ（song_music_classes）を読むためのリポジトリ。
    /// クレジット履歴で主題歌系の役職に「オープニング主題歌」「エンディング主題歌」「挿入歌」の
    /// プレフィックスを付与する際、コード値（OP / ED / INSERT）から日本語表示名を引くために使う。
    /// episode_theme_songs.theme_kind は形式上 ENUM だが、コード値が song_music_classes.class_code と
    /// 一致する運用なので、マスタの name_ja をそのまま流用する。
    /// </summary>
    private readonly SongMusicClassesRepository _songMusicClassesRepo;

    private readonly CreditInvolvementIndex _index;

    /// <summary>person_id → 当該人物に紐付く全 alias_id のリスト（person_alias_persons の逆引き）。 1 度ロードしたら使い回す。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasesByPerson;

    /// <summary>役職コード → Role モデル。役職の表示名解決と display_order の取得に使う。</summary>
    private IReadOnlyDictionary<string, Role>? _roleMap;

    /// <summary>character_alias_id → CharacterAlias。声優関与のときキャラ名表示に使う。</summary>
    private readonly Dictionary<int, CharacterAlias?> _characterAliasCache = new();

    /// <summary>company_alias_id → 屋号名 のキャッシュ。 クレジット履歴の所属屋号併記で同じ alias を何度も解決するため。 値が <c>null</c> のときは「未登録」を意味する（負の結果もキャッシュ）。</summary>
    private readonly Dictionary<int, string?> _companyAliasNameCache = new();

    /// <summary>主題歌種別コード（OP / ED / INSERT 等）→ SongMusicClass モデル のマップ。 クレジット履歴で「オープニング主題歌 作曲」のようなラベルを組み立てるための辞書。 <c>GenerateAsync</c> で 1 度だけロードして使い回す。</summary>
    private IReadOnlyDictionary<string, SongMusicClass>? _songMusicClassMap;

    /// <summary>person_alias_id → (song_id, role_code) の前計算索引。「楽曲」セクションで人物が担当した曲一覧を一発引きするために、 song_credits と song_recording_singers の両ソースを 1 度だけスキャンして alias_id 別にバケットしておく。 <c>GenerateAsync</c> で 1 度だけ詰めて使い回す。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<(int SongId, string RoleCode)>>? _songRolesByAlias;

    /// <summary>song_id → 当該曲のすべての song_recordings（song_recording_id 昇順）。「楽曲」セクションのカード表記で出典シリーズと音楽種別を引き当てる際の代表 recording 解決に使う。</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<SongRecording>>? _recordingsBySong;

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

        // 主題歌種別マスタ（OP / ED / INSERT 等のコード値 → 日本語表示名）も引く。
        // クレジット履歴で「オープニング主題歌 作曲」のような展開を組み立てるための辞書。
        // 形式上は episode_theme_songs.theme_kind が ENUM だが、コード値が song_music_classes.class_code と
        // 一致する運用なので、マスタ側の name_ja を流用して自然な日本語ラベルに展開する。
        if (_songMusicClassMap is null)
        {
            var allClasses = await _songMusicClassesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _songMusicClassMap = allClasses.ToDictionary(c => c.ClassCode, StringComparer.Ordinal);
        }

        // person_id → alias_id 群の逆引きは SiteDataLoader が BuildContext.AliasIdsByPerson に
        // 全件辞書化済み。本ジェネレータ内でローカル辞書を持たず、共有辞書を直接参照する。
        _aliasesByPerson ??= _ctx.AliasIdsByPerson;

        // 「楽曲」セクションの person_alias_id → (song_id, role_code) 索引を 1 度だけ前計算。
        // song_credits（作詞・作曲・編曲）と song_recording_singers（歌・コーラス）の両ソースから
        // person_alias_id をキーに集約する。後者は recording → song の解決を挟む。
        if (_songRolesByAlias is null)
        {
            var bucket = new Dictionary<int, List<(int SongId, string RoleCode)>>();
            foreach (var (songId, credits) in _ctx.SongCreditsBySong)
            {
                foreach (var c in credits)
                {
                    if (!bucket.TryGetValue(c.PersonAliasId, out var list))
                    {
                        list = new List<(int, string)>();
                        bucket[c.PersonAliasId] = list;
                    }
                    list.Add((songId, c.CreditRole));
                }
            }
            foreach (var (recId, singers) in _ctx.SingersByRecording)
            {
                if (!_ctx.SongRecordingById.TryGetValue(recId, out var rec)) continue;
                foreach (var s in singers)
                {
                    if (!s.PersonAliasId.HasValue) continue;
                    int aliasId = s.PersonAliasId.Value;
                    if (!bucket.TryGetValue(aliasId, out var list))
                    {
                        list = new List<(int, string)>();
                        bucket[aliasId] = list;
                    }
                    list.Add((rec.SongId, s.RoleCode));
                }
            }
            _songRolesByAlias = bucket.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<(int SongId, string RoleCode)>)kv.Value);
        }

        // song_id → recordings の索引も 1 度だけ組み立てておく（カード行の出典シリーズ解決用）。
        if (_recordingsBySong is null)
        {
            _recordingsBySong = _ctx.SongRecordingById.Values
                .GroupBy(r => r.SongId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<SongRecording>)g.OrderBy(r => r.SongRecordingId).ToList());
        }

        // 人物索引は「クリエーター > スタッフ」（/creators/staff/）に集約。
        // 本ジェネレータは人物単体の詳細ページ（/persons/{id}/）生成に専念する。

        // 詳細ページ。関与が 1 件もない人物もページは作る（直リンク用）。
        // 2 相生成：レンダリング＋ファイル書き出し（出力先はページごとに別パス）は並列、
        // サマリ・進捗・sitemap 記録だけを元順序で逐次に行う。
        // 詳細ページ生成経路は本メソッド前半で確定済みの読み取り専用辞書（_aliasesByPerson /
        // _songRolesByAlias / _recordingsBySong 等）とスレッドセーフな描画ヘルパしか触らないため、
        // 人物単位で安全に並列化できる（sitemap.xml の URL 並びは逐次記録で決定論を維持）。
        var urlPaths = new string[persons.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, persons.Count),
            new ParallelOptions { CancellationToken = ct },
            async (i, token) =>
            {
                urlPaths[i] = await RenderDetailAsync(persons[i], aliasById, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        foreach (var urlPath in urlPaths)
        {
            _page.RecordWritten(urlPath, "persons");
        }

        _ctx.Logger.Success($"persons: {persons.Count} ページ");
    }

    /// <summary>人物詳細ページ <c>/persons/{person_id}/</c> をレンダリングしてファイルへ書き出し、URL パスを返す。
    /// 並列レンダリングフェーズから複数スレッドで同時に呼ばれるため共有状態への書き込みは行わない
    /// （出力ファイルパスはページごとに異なるため書き出しは安全。サマリ・sitemap 記録は
    /// 呼び出し側が逐次フェーズで行う）。</summary>
    private async Task<string> RenderDetailAsync(
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
        // 「楽曲」セクションのカード行（構造化エントリ song_credits / song_recording_singers から）。
        var songCards = BuildPersonSongCards(aliasIds);
        // 誕生日表記：BirthYearVisibility=PUBLIC かつ BirthYear ありなら「YYYY年M月D日」、
        // 非公開もしくは未設定なら年抜きの「M月D日」。BirthMonth / BirthDay の片方でも未設定なら空文字。
        string birthday = FormatBirthday(person);

        var content = new PersonDetailModel
        {
            Person = new PersonView
            {
                PersonId = person.PersonId,
                DisplayName = displayName,
                FullName = person.FullName,
                FullNameKana = person.FullNameKana ?? "",
                NameEn = person.NameEn ?? "",
                Notes = person.Notes ?? "",
                Birthday = birthday,
                OfficialUrl = person.OfficialUrl ?? "",
                XUrl = person.XUrl ?? "",
                InstagramUrl = person.InstagramUrl ?? "",
                YoutubeUrl = person.YoutubeUrl ?? ""
            },
            Aliases = aliasViews,
            InvolvementGroups = involvementGroups,
            SongCards = songCards,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        // 人物詳細の構造化データは Schema.org の Person 型。
        string baseUrl = _ctx.Config.BaseUrl;
        string personUrl = PathUtil.PersonUrl(person.PersonId);
        var alternateNames = aliasViews
            .Select(a => a.Name)
            .Where(n => !string.IsNullOrEmpty(n) && !string.Equals(n, person.FullName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // MetaDescription を実データから組み立てる。
        // 「{人名}は、プリキュアシリーズで{役職1}({N話})・{役職2}({N話})などを担当。」を骨格にする。
        var metaDescription = BuildPersonMetaDescription(displayName, involvementGroups);

        // jobTitle は involvementGroups の RoleLabel（役職名 / 例：「監督」「脚本」）から
        // 担当話数の多い順で上位 3 件のラベルを取り出す。Count は当該役職での担当エピソード数。
        var topJobTitles = involvementGroups
            .OrderByDescending(g => g.Count)
            .Select(g => g.RoleLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label) && label != "(役職未設定)")
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();

        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Person",
            ["name"] = displayName,
            // description は SERP のリッチスニペットや AI 検索エンジンの要約候補として参照される。
            // MetaDescription と同じ文面を入れて二重整合性を担保する。
            ["description"] = metaDescription
        };
        if (alternateNames.Count > 0) jsonLdDict["alternateName"] = alternateNames;
        if (!string.IsNullOrEmpty(person.NameEn)) jsonLdDict["givenName"] = person.NameEn;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + personUrl;
        if (topJobTitles.Count > 0)
        {
            // jobTitle は単一文字列でも配列でも有効（Schema.org 仕様）。Person の主要な役職を
            // 配列で複数並べる形式は職能横断を素直に表現できる（アニメスタッフは複数の役職を兼ねるため）。
            jsonLdDict["jobTitle"] = topJobTitles;
        }

        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = displayName,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
                new BreadcrumbItem { Label = displayName, Url = "" }
            },
            OgType = "profile",
            JsonLd = jsonLd
        };

        _page.RenderAndWriteFile(personUrl, "persons-detail.sbn", content, layout);
        return personUrl;
    }

    /// <summary>
    /// 人物詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// 構成：「{人名}は、プリキュアシリーズで{役職1}({N話})・{役職2}({N話})・{役職3}({N話})などを担当。」を骨格に、
    /// 各セグメント追加前に <c>targetMaxChars=140</c> を超えないかを確認しつつ追記する。
    /// 役職は <see cref="InvolvementGroup.Count"/> 降順（担当話数の多い順）でソートして
    /// 上位を採用する。声優役は <see cref="InvolvementGroup.HasCharacterColumn"/> が true なので
    /// 「演じた役（声優）」を簡略表現で別途付ける手もあるが、本リビジョンでは役職ラベルで統一する。
    /// 関与役職が 1 件も無い人物（呼ばれない想定だが安全網として）は、定型文「{人名} のプリキュア関連クレジット一覧。」に
    /// フォールバックする。
    /// </summary>
    private static string BuildPersonMetaDescription(
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
            // 「役職(N話・M本)」のフラグメントを組む。担当ゼロ（話数 + 本数とも 0）は弾く。
            // TV 系シリーズへの担当は「話」、映画系シリーズへの担当は「本」で表記し、両方あれば「N話・M本」併記。
            if (g.Count <= 0) continue;
            var fragment = $"{g.RoleLabel}({g.CountLabel.Replace(" ", "")})";
            // 末尾「などを担当。」(7 字) ぶんを残せるかの判定を含めて追加可否を決める。
            int suffixLen = 7;
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

        sb.Append("などを担当。");
        return sb.ToString();
    }

    /// <summary>人物の名義群を時系列に並べる（predecessor チェーンを上に辿って root を見つけ、 successor チェーンで下降）。チェーンに含まれなかった alias は末尾に並べる。</summary>
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
            ValidFrom = JpDateFormat.NullableDate(a.ValidFrom),
            ValidTo = JpDateFormat.NullableDate(a.ValidTo),
            Notes = a.Notes ?? ""
        }).ToList();
    }

    /// <summary>
    /// 人物に紐付く alias_id 群から関与情報を集約し、役職別 → シリーズ単位の話数圧縮表記に編成する。
    /// 役職別 → シリーズ単位 1 行 + 話数を「#1〜4, 8」のように圧縮表示する。
    /// 全話担当のときは話数表記を省略し、代わりに「(全話)」マークを付ける。
    /// 声優役（CHARACTER_VOICE）のときは演じたキャラ名（シリーズ内全話分の連名）も併記する。
    /// シリーズ全体スコープ（episode_id NULL）の関与は別行として「（シリーズ全体）」で残す。
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

        // グループ分けキーは「カテゴリプレフィックスコード × 役職コード」の複合。
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

                // 同一シリーズで「シリーズ全体スコープ」と「エピソード単位」が混在しうる。
                // シリーズ全体スコープは別行として残し、エピソード単位は話数集合に集約する。
                var episodeNos = new HashSet<int>();
                bool hasSeriesScope = false;
                var seriesScopeCharacterNames = new List<string>();
                var perEpisodeCharacterNames = new List<string>();
                // 所属屋号 ID の集合をシリーズスコープ別・エピソード単位別に分けて収集。
                // 同一シリーズ内で複数の屋号で所属クレジットされる例（移籍など）があるため、
                // HashSet で重複排除し、後で名前解決して列挙する。
                // OrderedSet 相当の挙動が欲しいので「初出順を保つために」List + Contains で管理する。
                var seriesScopeAffiliationIds = new List<int>();
                var perEpisodeAffiliationIds = new List<int>();

                foreach (var inv in bySeries)
                {
                    if (inv.EpisodeId is int eid)
                    {
                        var ep = _ctx.LookupEpisode(bySeries.Key, eid);
                        if (ep is not null) episodeNos.Add(ep.SeriesEpNo);
                        // 声優関与のとき演じたキャラ名を集める（シリーズ単位で重複排除）。
                        if (inv.Kind == InvolvementKind.CharacterVoice && inv.CharacterAliasId.HasValue)
                        {
                            string? name = await ResolveCharacterNameAsync(inv.CharacterAliasId.Value, ct).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(name) && !perEpisodeCharacterNames.Contains(name))
                                perEpisodeCharacterNames.Add(name);
                        }
                        // 所属屋号 ID を初出順で記録（人物詳細での所属併記用）。
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

                // 所属屋号 ID 集合を表示名（テンプレ用ラベル）に解決する。
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
                // 映画系シリーズ（credit_attach_to='SERIES'）はそもそも全クレジットが series 直付けの
                // 「シリーズ全体」相当なので、わざわざ「（シリーズ全体）」ラベルを併記する意味がない
                // （見出しの「N 本」表記＋シリーズ名で十分自明）。TV 系シリーズに稀に出る series-scope
                // クレジットだけ「（シリーズ全体）」を出して、エピソード単位の行と区別する。
                if (hasSeriesScope)
                {
                    seriesRows.Add(new InvolvementSeriesRow
                    {
                        SeriesSlug = series.Slug,
                        SeriesTitle = series.Title,
                        SeriesStartYearLabel = series.StartDate.Year.ToString(),
                        RangeLabel = isMovieKindSeries ? "" : "（シリーズ全体）",
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
                }

                // (c) 担当量カウント：シリーズ種別で「話」と「本」を分けて加算。
                // 映画系（credit_attach_to='SERIES'）：当該シリーズに関与が 1 件以上あれば 1 本としてカウント
                //   （映画 1 本に OP / ED / INSERT / SOUND_TRACK が同一カードに同居しても 1 本扱い）。
                // TV 系（credit_attach_to='EPISODE'）：エピソード単位の関与話数を加算（重複話数は HashSet で排除済み）。
                // SERIES スコープのみで episode 関与が無い TV 系（稀ケース）は本カウントには寄与せず 0 計上。
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

            // 役職別グループ見出しに付ける役職統計ページの URL。役職コードが空（マスタ未登録）なら空文字。
            string roleUrl = string.IsNullOrEmpty(roleCode) ? "" : PathUtil.RoleStatsUrl(roleCode);
            groups.Add(new InvolvementGroup
            {
                RoleCode = roleCode,
                RoleLabel = roleLabel,
                RoleUrl = roleUrl,
                SeriesRows = seriesRows,
                EpisodeCount = episodeCountTotal,
                MovieCount = movieCountTotal,
                HasCharacterColumn = seriesRows.Any(r => !string.IsNullOrEmpty(r.CharacterNames))
            });
        }
        return groups;
    }

    /// <summary>誕生日表記を組み立てる。 BirthYearVisibility=PUBLIC かつ BirthYear ありなら「YYYY年M月D日」、 非公開もしくは未設定なら年抜きの「M月D日」。 BirthMonth / BirthDay の片方でも未設定なら空文字を返す（誕生日行を出さない）。</summary>
    private static string FormatBirthday(Person p)
    {
        if (p.BirthMonth is not byte m || p.BirthDay is not byte d) return "";
        if (p.BirthYear is ushort y
            && string.Equals(p.BirthYearVisibility, "PUBLIC", StringComparison.Ordinal))
        {
            return $"{y}年{m}月{d}日";
        }
        return $"{m}月{d}日";
    }

    /// <summary>
    /// 構造化エントリ（song_credits / song_recording_singers）に紐付いた当該人物の担当楽曲をカード行群に集約する。
    /// 1 カード = 1 曲。同じ曲で複数役職（作詞 + 作曲 等）を持つ場合は同カード内に役職バッジを並べる。
    /// 出典シリーズは当該曲の最古 recording の <c>SeriesId</c> から解決し、無ければシリーズ情報なしのカードになる。
    /// 並びは「シリーズ開始年昇順 → 曲タイトル昇順」。
    /// </summary>
    private IReadOnlyList<PersonSongCard> BuildPersonSongCards(IReadOnlyList<int> aliasIds)
    {
        if (aliasIds.Count == 0 || _songRolesByAlias is null) return Array.Empty<PersonSongCard>();

        // 担当楽曲を song_id 単位で集約。同一曲で複数役職を持つときは role_code 集合を統合する。
        var rolesBySong = new Dictionary<int, HashSet<string>>();
        foreach (var aliasId in aliasIds)
        {
            if (!_songRolesByAlias.TryGetValue(aliasId, out var rows)) continue;
            foreach (var (songId, roleCode) in rows)
            {
                if (!rolesBySong.TryGetValue(songId, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    rolesBySong[songId] = set;
                }
                set.Add(roleCode);
            }
        }
        if (rolesBySong.Count == 0) return Array.Empty<PersonSongCard>();

        var cards = new List<PersonSongCard>(rolesBySong.Count);
        foreach (var (songId, roleSet) in rolesBySong)
        {
            if (!_ctx.SongById.TryGetValue(songId, out var song)) continue;

            // 代表 recording から出典シリーズを解決（無ければ空）。
            Series? series = null;
            if (_recordingsBySong is not null && _recordingsBySong.TryGetValue(songId, out var recs))
            {
                foreach (var r in recs)
                {
                    if (r.SeriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var s))
                    {
                        series = s;
                        break;
                    }
                }
            }

            // 役職バッジ群：role_map の display_order 昇順、ラベル・URL は roles マスタから引く
            //（マスタ未登録時はコード値をフォールバック表示する）。
            var roleBadges = roleSet
                .Select(code =>
                {
                    string label = _roleMap!.TryGetValue(code, out var r) ? (r.NameJa ?? code) : code;
                    int order = _roleMap!.TryGetValue(code, out var r2) && r2.DisplayOrder is ushort d
                        ? d : int.MaxValue;
                    return new RoleBadgeView
                    {
                        Code = code,
                        Label = label,
                        Url = PathUtil.RoleStatsUrl(code),
                        DisplayOrder = order
                    };
                })
                .OrderBy(b => b.DisplayOrder)
                .ThenBy(b => b.Code, StringComparer.Ordinal)
                .ToList();

            cards.Add(new PersonSongCard
            {
                SongId = songId,
                SongUrl = PathUtil.SongUrl(songId),
                Title = song.Title,
                SeriesTitle = series?.Title ?? "",
                SeriesUrl = series is null ? "" : PathUtil.SeriesUrl(series.Slug),
                SeriesStartYearLabel = series?.StartDate.Year.ToString() ?? "",
                SeriesStartDateRaw = series?.StartDate,
                Roles = roleBadges
            });
        }

        // ソート：シリーズ開始年昇順 → 曲タイトル昇順。シリーズ無しは末尾。
        return cards
            .OrderBy(c => c.SeriesStartDateRaw is null ? 1 : 0)
            .ThenBy(c => c.SeriesStartDateRaw)
            .ThenBy(c => c.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>character_alias_id からキャラ名を引く。 BuildContext.CharacterAliasById に全件辞書化済みのため同期 lookup で完結する。 シグネチャは呼び出し側互換のため Task ベースを維持。</summary>
    private Task<string?> ResolveCharacterNameAsync(int aliasId, CancellationToken ct)
        => Task.FromResult(_ctx.CharacterAliasById.TryGetValue(aliasId, out var ca) ? ca.Name : null);

    /// <summary>company_alias_id から屋号名を引く。 BuildContext.CompanyAliasById に全件辞書化済みのため同期 lookup で完結する。 シグネチャは呼び出し側互換のため Task ベースを維持。</summary>
    private Task<string?> GetCompanyAliasNameAsync(int aliasId, CancellationToken ct)
        => Task.FromResult(_ctx.CompanyAliasById.TryGetValue(aliasId, out var ca) ? ca.Name : null);

    // ─── テンプレ用 DTO 群 ───

    private sealed class PersonDetailModel
    {
        public PersonView Person { get; set; } = new();
        public IReadOnlyList<PersonAliasView> Aliases { get; set; } = Array.Empty<PersonAliasView>();
        public IReadOnlyList<InvolvementGroup> InvolvementGroups { get; set; } = Array.Empty<InvolvementGroup>();
        /// <summary>構造化エントリ（song_credits / song_recording_singers）由来の担当楽曲カード行群。</summary>
        public IReadOnlyList<PersonSongCard> SongCards { get; set; } = Array.Empty<PersonSongCard>();
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の h1 ブロック直後に独立段落で表示する。</summary>
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
        /// <summary>誕生日表記（「YYYY年M月D日」または「M月D日」、未設定時は空文字）。</summary>
        public string Birthday { get; set; } = "";
        /// <summary>事務所等の公式ページ URL。詳細ページ末尾「外部リンク」セクションに出す。 Wikipedia は内部値として保持はするがサイト UI からはリンクしない方針なので、 ここでは敢えて出していない。</summary>
        public string OfficialUrl { get; set; } = "";
        public string XUrl { get; set; } = "";
        public string InstagramUrl { get; set; } = "";
        public string YoutubeUrl { get; set; } = "";
    }

    /// <summary>担当楽曲カード 1 行。1 曲につき 1 行で、複数役職は <see cref="Roles"/> に並べる。</summary>
    private sealed class PersonSongCard
    {
        public int SongId { get; set; }
        public string SongUrl { get; set; } = "";
        public string Title { get; set; } = "";
        /// <summary>代表 recording 由来の出典シリーズ名（解決できない場合は空文字）。</summary>
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        /// <summary>出典シリーズの開始年（4 桁、未解決時は空文字）。テンプレで「(2004)」のように添える。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>並び替え用のシリーズ開始日原値（テンプレでは未参照）。</summary>
        public DateOnly? SeriesStartDateRaw { get; set; }
        /// <summary>当該曲での担当役職バッジ群（role_map.display_order 昇順）。</summary>
        public IReadOnlyList<RoleBadgeView> Roles { get; set; } = Array.Empty<RoleBadgeView>();
    }

    /// <summary>役職バッジ 1 個。役職コード（CSS の <c>data-role-code</c> に渡す）、表示ラベル、 役職統計ページ URL を持つ。担当楽曲カードのほか、必要に応じて他セクションでも流用できる素直な DTO。</summary>
    private sealed class RoleBadgeView
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>並び替え用の表示順（role_map.display_order）。テンプレでは未参照。</summary>
        public int DisplayOrder { get; set; }
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

/// <summary>役職別の関与グループ（行構造はシリーズ単位 + 話数圧縮）。</summary>
internal sealed class InvolvementGroup
{
    public string RoleCode { get; set; } = "";
    public string RoleLabel { get; set; } = "";
    /// <summary>役職統計ページ（/creators/roles/{code}/）への URL。役職コードが空（カテゴリプレフィックス + 役職なし）のときは空文字。</summary>
    public string RoleUrl { get; set; } = "";
    /// <summary>シリーズ単位の集約行群。各行はそのシリーズ内での話数集合を圧縮表記で持つ。</summary>
    public IReadOnlyList<InvolvementSeriesRow> SeriesRows { get; set; } = Array.Empty<InvolvementSeriesRow>();
    /// <summary>TV 系シリーズ（series_kinds.credit_attach_to='EPISODE'）での担当エピソード合計数。</summary>
    public int EpisodeCount { get; set; }
    /// <summary>映画系シリーズ（series_kinds.credit_attach_to='SERIES'、MOVIE / MOVIE_SHORT / SPRING / EVENT）での担当本数（1 シリーズ = 1 本）。</summary>
    public int MovieCount { get; set; }
    /// <summary>担当の総量（<see cref="EpisodeCount"/> + <see cref="MovieCount"/>）。降順ソートのキーとして使う。</summary>
    public int Count => EpisodeCount + MovieCount;
    /// <summary>"N 話・M 本" / "N 話" / "M 本" の単位付き表記。テンプレ・リード文の小見出しで使う。両方ゼロなら空文字。</summary>
    public string CountLabel => (EpisodeCount, MovieCount) switch
    {
        ( > 0, > 0) => $"{EpisodeCount} 話・{MovieCount} 本",
        ( > 0, 0)   => $"{EpisodeCount} 話",
        (0,   > 0) => $"{MovieCount} 本",
        _           => ""
    };
    /// <summary>このグループ内に CharacterNames が設定された行が 1 件以上あるか（声優役判定）。</summary>
    public bool HasCharacterColumn { get; set; }
}

/// <summary>シリーズ単位の関与 1 行。 行はシリーズ単位 + 話数圧縮で構成する（エピソードごと 1 行にはしない）。</summary>
internal sealed class InvolvementSeriesRow
{
    public string SeriesSlug { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。 クレジット履歴・声の出演履歴の各シリーズ行の表記で、シリーズ名直後に 薄色括弧で添える表現に使う（略称（series.title_short）は一切使わない）。</summary>
    public string SeriesStartYearLabel { get; set; } = "";
    /// <summary>話数圧縮表記。例：「#1〜4, 8」。全話担当なら空文字（テンプレ側で「(全話)」マークを別途出す）。 シリーズ全体スコープのときは「（シリーズ全体）」のような任意ラベルを入れる。</summary>
    public string RangeLabel { get; set; } = "";
    /// <summary>シリーズ内の全話を担当しているフラグ。テンプレで「(全話)」マークを出すかの判定に使う。</summary>
    public bool IsAllEpisodes { get; set; }
    /// <summary>声優関与のとき演じたキャラ名（シリーズ内連名、「、」連結）。それ以外は空。</summary>
    public string CharacterNames { get; set; } = "";
    /// <summary>当該シリーズで当該人物がクレジットされた所属屋号の表示ラベル。</summary>
    public string AffiliationsLabel { get; set; } = "";
}