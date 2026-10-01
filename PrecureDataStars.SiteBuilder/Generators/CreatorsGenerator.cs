using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 「クリエイター」セクション一式（人物・企業/団体・声優のハブ）の生成。
/// 生成ページ：
/// <list type="bullet">
///   <item><description><c>/creators/</c> … スタッフ / 声の出演の 2 カードを案内するランディング。</description></item>
///   <item><description><c>/creators/staff/</c> … 役職順 / 五十音順 / 初参加順 /
///     参加回数順 の 4 タブ。五十音順以降のタブは人物と企業・団体を 1 リストに混在させ、
///     行ごとに「個人 / 団体」バッジで区別し、上部トグルで個人のみ・団体のみに絞れる。</description></item>
///   <item><description><c>/creators/roles/{rep_role_code}/</c> … 当該役職クラスタに
///     関わった人物・企業/団体を 1 リストに混在させ、五十音順 / 初参加順 / 担当回数順
///     のタブで切り替える役職詳細。</description></item>
///   <item><description><c>/creators/voice-cast/</c> … 五十音順 / キャラクター順 /
///     初出演順 / 出演回数順 の 4 タブで声優を並べる。</description></item>
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
    // 歌系 4 役職は episode_theme_songs を介さずに song_credits / song_recording_singers
    // から直接集計するため、専用のリポジトリを別途注入する。本編クレジットに登場しない
    // 楽曲スタッフ（劇中歌・キャラソンの作詞家など）もカバーするのが趣旨。
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecSingersRepo;
    private readonly PrecuresRepository _precuresRepo;


    /// <summary>company_id → 全クレジット横断で最後に使われた company_alias_id（<see cref="BuildLatestAliasMaps"/> で確定）。</summary>
    private readonly Dictionary<int, int> _latestCompanyAliasId = new();

    /// <summary>
    /// 楽曲の作家 3 役職（作詞・作曲・編曲）の表示順。これらの役職の /creators/roles/{code}/ ページは、
    /// episode_theme_songs を経由するクレジット階層集計（<see cref="_index"/>）ではなく、
    /// song_credits を直接集計する別ルートで生成する（本編クレジットに登場しない楽曲だけに関わった
    /// 作家も拾うため）。スタッフ一覧には載せず、音楽制作ページ（/creators/music-production/）の役職タブに並べる。
    /// 歌唱系の役職（歌・コーラス・台詞）は役職詳細を持たず、歌唱ページ（/creators/singers/）に集約する
    /// （<see cref="PathUtil.IsSingerRole"/>）。
    /// </summary>
    private static readonly string[] SongCreditRoleOrder =
    {
        SongCreditRoles.Lyrics,
        SongCreditRoles.Composition,
        SongCreditRoles.Arrangement,
    };

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
        _songCreditsRepo = new SongCreditsRepository(factory);
        _songRecSingersRepo = new SongRecordingSingersRepository(factory);
        _precuresRepo = new PrecuresRepository(factory);
    }

    /// <summary>各一覧に載せた人物・企業/団体の記録。生成の最後に <see cref="BuildContext.CreatorLists"/> へ差し込む。</summary>
    private readonly CreatorListMembership _lists = new();

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
        // 屋号 ID → 屋号本体。企業の表示行を「正式名称」ではなく「その時の屋号」で出すための引き当て。
        var companyAliasById = allCompanyAliases.ToDictionary(a => a.AliasId);

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
            // 表記のみの役職（著作権表記・タイトル）はスタッフの役職ではないので、役職詳細も索引も作らない。
            .Where(r => !string.Equals(r.RoleFormatKind, "NOTICE", StringComparison.Ordinal))
            .Where(r => string.Equals(_resolver.GetRepresentative(r.RoleCode), r.RoleCode, StringComparison.Ordinal))
            .OrderBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToList();

        var personById = allPersons.ToDictionary(p => p.PersonId);
        var companyById = allCompanies.ToDictionary(c => c.CompanyId);

        // 「担当回数順」の行表記に使う、人物・企業ごとの最後に使われた名義を先に確定させる。
        BuildLatestAliasMaps(aliasIdsByPersonId, companyAliasesByCompany, logosByCompanyAlias);

        // ── 役職詳細ページ群を生成し、あわせて「役職順」タブ用の索引エントリも構築 ──
        var roleIndexEntries = new List<RoleIndexEntry>();
        // 音楽制作ページの役職タブ用（作詞・作曲・編曲の役職詳細への入口）。
        var musicRoleEntries = new List<RoleIndexEntry>();

        // 楽曲の作家・歌唱は別ルート集計のため、song_credits / song_recording_singers を 1 度だけ全件ロード。
        // person_alias_id → person_id の逆引き辞書も先に作っておく（人物単位での「担当曲数」集計に使う）。
        var allSongCredits = (await _songCreditsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allSingers = (await _songRecSingersRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var personIdByAlias = new Dictionary<int, int>(capacity: allPersons.Count * 2);
        foreach (var kv in aliasIdsByPersonId)
        {
            foreach (var aid in kv.Value) personIdByAlias[aid] = kv.Key;
        }

        foreach (var role in rankableRoles)
        {
            // 主題歌・挿入歌（THEME_SONG 形式）の役職は専用の人物集計ページを持たない。
            // クレジット階層では {THEME_SONGS} として曲そのものを出し（曲詳細へリンク）、歌スタッフの
            // 集計は歌系 4 役職（作詞/作曲/編曲/歌唱）の専用ページが担うため、役職詳細ページも
            // 「役職順」索引エントリも作らない（CreditTreeRenderer 側でも役職ラベルはリンク化しない）。
            if (string.Equals(role.RoleFormatKind, "THEME_SONG", StringComparison.Ordinal))
                continue;

            // 歌唱系の役職（歌・コーラス・台詞）は役職詳細を作らず、歌唱ページに集約する。
            if (PathUtil.IsSingerRole(role.RoleCode))
                continue;

            // 楽曲の作家 3 役職は専用集計に分岐。本編クレジット階層を介さず、song_credits から
            // 人物別の「担当曲数」を直接数える。スタッフ一覧の役職順タブには載せず、音楽制作ページの役職タブに並べる。
            if (Array.IndexOf(SongCreditRoleOrder, role.RoleCode) >= 0)
            {
                var songRows = BuildSongRoleRows(role.RoleCode, allSongCredits, allSingers,
                    personIdByAlias, personById);
                if (songRows.Count == 0) continue;
                GenerateSongRoleDetail(role, songRows);
                musicRoleEntries.Add(new RoleIndexEntry
                {
                    RoleNameJa = role.NameJa,
                    RoleUrl = PathUtil.CreatorsRoleUrl(role.RoleCode),
                    PersonCount = songRows.Count,
                    RoleNameKey = role.RoleCode
                });
                continue;
            }

            // クラスタ全 role_code（自分を含む）を集計対象とする。
            // VOICE_CAST はクラスタ内に混在する想定はないが念のため除外する。
            var memberCodes = _resolver.GetClusterMembers(role.RoleCode)
                .Where(c => roleByCode.TryGetValue(c, out var rr)
                            && !string.Equals(rr.RoleFormatKind, "VOICE_CAST", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            var rowSet = BuildRoleEntityRows(
                role.RoleCode, memberCodes, roleByCode, aliasIdsByPersonId, allPersons,
                companyAliasesByCompany, logosByCompanyAlias, allCompanies, companyAliasById);
            // 件数・並べ替えキーはエンティティ単位（人物 1 人 / 企業 1 社 = 1 行）の行で数える。
            var rows = rowSet.CountRows;

            // 一度もクレジットのない役職は出さない方針：関与エンティティが 0 件なら
            // 役職詳細ページも生成せず、「役職順」タブの索引（roleIndexEntries）にも積まない。
            if (rows.Count == 0) continue;

            int personCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal));
            int companyCount = rows.Count - personCount;

            GenerateRoleDetail(role, memberCodes, roleByCode, rowSet);

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
                RoleUrl = PathUtil.CreatorsRoleUrl(role.RoleCode),
                PersonCount = personCount,
                CompanyCount = companyCount,
                SortStart = roleSortStart,
                SortEpNo = roleSortEpNo,
                SortPos = roleSortPos,
                RoleNameKey = role.RoleCode,
                // TV 系シリーズ（credit_attach_to='EPISODE'）のクレジットが 1 件も無い役職は「映画のみ」。
                IsMovieOnly = rows.All(r => r.EpisodeCount == 0)
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
            companyAliasesByCompany, logosByCompanyAlias, allCompanies, companyAliasById, companyById,
            rankableRoles, roleByCode, out int staffPersonCount, out int staffCompanyCount);

        // ── 声の出演（/creators/voice-cast/） ──
        var allCharacters = (await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        GenerateVoiceCast(aliasIdsByPersonId, allPersons, allCharacters, allCharacterAliases,
            out int voiceCastCount);

        // ── 音楽制作（/creators/music-production/）・歌唱（/creators/singers/） ──
        musicRoleEntries = musicRoleEntries
            .OrderBy(e => Array.IndexOf(SongCreditRoleOrder, e.RoleNameKey))
            .ToList();
        // 本人名義で歌・台詞を担当した人だけを歌唱ページの歌手に載せ、コーラスだけ・名前の出ないユニットだけの人は
        // 音楽制作ページの「歌（演奏）」へ回す。
        var leadSingers = LeadSingerPersons(allSingers, personIdByAlias);
        GenerateMusicProduction(musicRoleEntries, allSongCredits, allSingers, leadSingers, personIdByAlias, personById, allRoles,
            out int musicProductionPersonCount, out int musicProductionCompanyCount);
        var characterById = allCharacters.ToDictionary(c => c.CharacterId);
        // 変身するキャラ（プリキュア）は歌唱ページのキャラクタータブで「変身前 / 変身後」の名義を並べる。
        var transformNameByCharacter = new Dictionary<int, string>();
        foreach (var pc in await _precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false))
        {
            if (_ctx.CharacterAliasById.TryGetValue(pc.PreTransformAliasId, out var pre)
                && _ctx.CharacterAliasById.TryGetValue(pc.TransformAliasId, out var post)
                && !transformNameByCharacter.ContainsKey(pre.CharacterId))
            {
                transformNameByCharacter[pre.CharacterId] = $"{pre.Name} / {post.Name}";
            }
        }
        GenerateSingers(allSingers, leadSingers, personIdByAlias, personById, characterById, transformNameByCharacter, out int singerCount);

        // ── ランディング（/creators/） ──
        GenerateLanding(staffPersonCount, staffCompanyCount, voiceCastCount,
            musicProductionPersonCount, musicProductionCompanyCount, singerCount);

        // 人物・企業詳細のパンくずが「本人が載っている一覧」を経由できるよう、各一覧に載せた顔ぶれを渡す。
        _ctx.CreatorLists = _lists;

        _ctx.Logger.Success(
            $"creators: {rankableRoles.Count} 役職詳細 + スタッフ + 声の出演 + 音楽制作 + 歌唱 + ランディング");
    }

    // 役職詳細

    /// <summary>1 役職クラスタに関わった人物・企業/団体を 1 リストに混在させた行群を作る。</summary>
    private EntityRowSet BuildRoleEntityRows(
        string pageRoleCode,
        IReadOnlySet<string> memberCodes,
        IReadOnlyDictionary<string, Role> roleByCode,
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias,
        IReadOnlyList<Company> allCompanies,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasById)
        => BuildEntityRowSet(
            inv => memberCodes.Contains(inv.RoleCode) ? inv.RoleCode : null,
            aliasIdsByPersonId, allPersons, companyAliasesByCompany, logosByCompanyAlias,
            allCompanies, companyAliasById, repNameMap: null, withWorksTooltip: true,
            roleUsageNote: agg => BuildRoleUsageNote(agg, pageRoleCode, roleByCode));

    /// <summary>
    /// 役職詳細の行に添える「表記ごとの担当数」（例：「デジタル撮影監督 1・撮影監督 1」）。系譜でつながった役職の
    /// うち、ページの役職名と違う表記でクレジットされたことがあるエンティティにだけ添え、表記はそのエンティティが
    /// 初めてその表記でクレジットされた順に並べる。担当数は話数と本数の合計。添えない行は空文字。
    /// </summary>
    private static string BuildRoleUsageNote(
        EntityAggregate agg, string pageRoleCode, IReadOnlyDictionary<string, Role> roleByCode)
    {
        if (agg.UsageByRoleCode.Keys.All(c => string.Equals(c, pageRoleCode, StringComparison.Ordinal))) return "";
        return string.Join("・", agg.UsageByRoleCode
            .OrderBy(kv => kv.Value.First)
            .Select(kv => $"{(roleByCode.TryGetValue(kv.Key, out var r) ? r.NameJa : kv.Key)} {kv.Value.Count}"));
    }

    /// <summary>
    /// 人物・企業/団体の行群を「初参加順用（名義ごとの行）」と「担当回数順用（エンティティごとの行）」の
    /// 2 系統で作る。担当量（話数・本数・シリーズ数・作品 tooltip・役職ラベル）はどちらの系統でも
    /// エンティティ（人物 1 人 / 企業 1 社）の全名義を合算した値。
    /// <list type="bullet">
    ///   <item><description>初参加順用：名義ごとに、その名義が最初にクレジットされた位置へ 1 行ずつ置く。
    ///     行の表記はその名義（その時にクレジットされた名前）。改名・屋号変更があれば、
    ///     新しい名義が初めて出たシリーズのセクションにも改めて並ぶ。</description></item>
    ///   <item><description>担当回数順用：エンティティ 1 行。表記は全クレジット横断で最後に使われた名義
    ///     （<see cref="_latestPersonAliasId"/> / <see cref="_latestCompanyAliasId"/>）。</description></item>
    /// </list>
    /// 企業・団体は COMPANY エントリ + LOGO エントリ + leading_company の 3 ルートを合算し、
    /// ロゴ経由の関与はロゴを保有する屋号の関与として扱う。リンク先はいずれも人物・親企業の詳細ページ。
    /// </summary>
    /// <param name="accept">関与を集計対象にするかの判定。対象なら役職ラベル用の代表 role_code、対象外なら null。</param>
    /// <param name="repNameMap">役職ラベル（スタッフ一覧用）を作るときの「代表 role_code → 役職名」。null なら役職ラベルを作らない。</param>
    /// <param name="withWorksTooltip">行リンクに担当作品一覧の tooltip を付けるか（役職詳細用）。</param>
    private EntityRowSet BuildEntityRowSet(
        Func<Involvement, string?> accept,
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias,
        IReadOnlyList<Company> allCompanies,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasById,
        IReadOnlyDictionary<string, string>? repNameMap,
        bool withWorksTooltip,
        Func<EntityAggregate, string>? roleUsageNote = null)
    {
        var set = new EntityRowSet();

        // 人物。
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            var agg = new EntityAggregate(this);
            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs) agg.Offer(aid, inv, accept(inv));
            }
            if (agg.IsEmpty) continue;

            (string Name, string Kana) AliasLabel(int aid)
                => _ctx.PersonAliasById.TryGetValue(aid, out var a)
                    ? (a.Name, a.NameKana ?? "")
                    : (p.FullName, p.FullNameKana ?? "");

            var latest = _ctx.EntityUrls.DisplayPersonAliasId(p.PersonId) is int latestAid
                ? AliasLabel(latestAid)
                : (p.FullName, p.FullNameKana ?? "");
            // 初参加行に表示名義を括弧で添える名義か。
            //   - TV 系のクレジットで使われた旧名義（改名など）は添える。
            //   - 映画だけで使われた名義は基本は名義変更ではないので添えないが、映画が初出でラテン文字で書かれた名義
            //     （例：TAP スタッフの「FRANCIS P.CANEDA」）は、TV 系に別の名義（カタカナ表記など）があれば添えて同一人物と分かるようにする。
            //   - 判定は本編のクレジットだけで行う（主題歌・挿入歌の作家・歌唱や劇伴の作曲・編曲は見ない）。
            bool personHasTv = aliasIds.Any(a => _index.ByPersonAlias.TryGetValue(a, out var ai)
                                                 && ai.Any(inv => inv.IsMainCredit && !_ctx.IsMovieKindSeries(inv.SeriesId)));
            bool NoteCurrentName(int aid)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var all)) return false;
                var invs = all.Where(inv => inv.IsMainCredit).ToList();
                if (invs.Count == 0) return false;
                if (invs.Any(inv => !_ctx.IsMovieKindSeries(inv.SeriesId))) return true;
                if (!personHasTv || !_ctx.PersonAliasById.TryGetValue(aid, out var a) || !IsLatinName(a.Name)) return false;
                var first = invs.MinBy(inv => CreditOrderKey(inv))!;
                return _ctx.IsMovieKindSeries(first.SeriesId);
            }
            AppendEntityRows(set, agg, "person", p.PersonId, PathUtil.PersonUrl(p.PersonId),
                latest, AliasLabel, NoteCurrentName, repNameMap, withWorksTooltip, roleUsageNote);
        }

        // 企業・団体。
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            var agg = new EntityAggregate(this);
            foreach (var aid in aliasIds)
            {
                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs) agg.Offer(aid, inv, accept(inv));
                }
                if (logosByCompanyAlias.TryGetValue(aid, out var logoIds))
                {
                    foreach (var logoId in logoIds)
                    {
                        if (!_index.ByLogo.TryGetValue(logoId, out var logoInvs)) continue;
                        foreach (var inv in logoInvs) agg.Offer(aid, inv, accept(inv));
                    }
                }
            }
            if (agg.IsEmpty) continue;

            (string Name, string Kana) AliasLabel(int aid)
                => companyAliasById.TryGetValue(aid, out var a)
                    ? (a.Name, a.NameKana ?? "")
                    : (c.Name, c.NameKana ?? "");

            var latest = _latestCompanyAliasId.TryGetValue(c.CompanyId, out var latestAid)
                ? AliasLabel(latestAid)
                : (c.Name, c.NameKana ?? "");
            // 企業の屋号は改名に限らず、雑誌名・部門名など並立する別名義も多いので、初参加行に最新屋号は添えない。
            AppendEntityRows(set, agg, "company", c.CompanyId, PathUtil.CompanyUrl(c.CompanyId),
                latest, AliasLabel, _ => false, repNameMap, withWorksTooltip, roleUsageNote);
        }

        return set;
    }

    /// <summary>
    /// 1 エンティティ分の集計結果から、担当回数順用の 1 行と、初参加順用の名義ごとの行を
    /// <paramref name="set"/> に積む。担当量・tooltip・役職ラベルは全行でエンティティ合算値を共有する。
    /// </summary>
    private void AppendEntityRows(
        EntityRowSet set, EntityAggregate agg, string entityKind, int entityId, string url,
        (string Name, string Kana) latest, Func<int, (string Name, string Kana)> aliasLabel,
        Func<int, bool> noteCurrentName,
        IReadOnlyDictionary<string, string>? repNameMap, bool withWorksTooltip,
        Func<EntityAggregate, string>? roleUsageNote)
    {
        string tooltip = withWorksTooltip ? BuildWorksTooltip(agg.EpisodeKeys, agg.MovieSeriesIds) : "";
        string rolesLabel = repNameMap is null ? "" : BuildRolesLabel(agg.EarliestByRep, repNameMap);
        string usageNote = roleUsageNote?.Invoke(agg) ?? "";

        var countRow = MakeEntityRow(entityKind, entityId, latest.Name, latest.Kana, url,
            agg.EpisodeKeys.Count, agg.MovieSeriesIds.Count, agg.SeriesIds.Count, agg.First, tooltip);
        countRow.RolesLabel = rolesLabel;
        countRow.RoleUsageNote = usageNote;
        set.CountRows.Add(countRow);

        foreach (var (aid, first) in agg.FirstByAlias)
        {
            var label = aliasLabel(aid);
            var debutRow = MakeEntityRow(entityKind, entityId, label.Name, label.Kana, url,
                agg.EpisodeKeys.Count, agg.MovieSeriesIds.Count, agg.SeriesIds.Count, first, tooltip);
            debutRow.RolesLabel = rolesLabel;
            debutRow.RoleUsageNote = usageNote;
            // TV 系のクレジットで使われた旧名義で置いた初参加行には、いまの名乗り（表示名義）を括弧で添えて
            // 同一人物と分かるようにする（添えるかは noteCurrentName が決める。企業は常に添えない）。
            // 逆向き（表示名義の行に旧名義を添える）はしない。
            if (!string.Equals(label.Name, latest.Name, StringComparison.Ordinal) && noteCurrentName(aid))
                debutRow.CurrentNameNote = latest.Name;
            set.DebutRows.Add(debutRow);
        }
    }

    /// <summary>
    /// 企業 → 全クレジット横断で最後に使われた屋号 を引く辞書を作る
    /// （人物の名乗りは人物詳細の見出し・URL と同じ表示名義を <see cref="EntityUrlRegistry.DisplayPersonAliasId"/> から引く）。
    /// 「最後」は関与の (シリーズ放送開始日, 話数, クレジット出現位置) が最も遅いもの（役職・種別を問わない）。
    /// ロゴ経由の関与はロゴを保有する屋号の使用として数える。
    /// </summary>
    private void BuildLatestAliasMaps(
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyDictionary<int, List<int>> companyAliasesByCompany,
        IReadOnlyDictionary<int, List<int>> logosByCompanyAlias)
    {
        _latestCompanyAliasId.Clear();
        foreach (var kv in companyAliasesByCompany)
        {
            var best = (Start: long.MinValue, EpNo: int.MinValue, Pos: long.MinValue);
            int? bestAid = null;
            void Offer(int aid, Involvement inv)
            {
                var key = CreditOrderKey(inv);
                if (bestAid is null || key.CompareTo(best) > 0) { best = key; bestAid = aid; }
            }
            foreach (var aid in kv.Value)
            {
                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs) Offer(aid, inv);
                }
                if (logosByCompanyAlias.TryGetValue(aid, out var logoIds))
                {
                    foreach (var logoId in logoIds)
                    {
                        if (!_index.ByLogo.TryGetValue(logoId, out var logoInvs)) continue;
                        foreach (var inv in logoInvs) Offer(aid, inv);
                    }
                }
            }
            if (bestAid is int b) _latestCompanyAliasId[kv.Key] = b;
        }
    }

    /// <summary>
    /// ラテン文字で書かれた名前か（ラテン文字を 1 字以上含み、ほかは空白・数字・句読点・記号だけ）。
    /// 映画のクレジットでアルファベット表記された名義（例：「FRANCIS P.CANEDA」）の判定に使う。
    /// </summary>
    private static bool IsLatinName(string name)
    {
        bool hasLetter = false;
        foreach (char ch in name)
        {
            if (char.IsLetter(ch))
            {
                // 基本ラテン・ラテン 1 補助・ラテン拡張 A/B・ラテン拡張追加・全角英字。
                bool latin = ch <= 'ɏ' || (ch >= 'Ḁ' && ch <= 'ỿ')
                             || (ch >= 'Ａ' && ch <= 'Ｚ') || (ch >= 'ａ' && ch <= 'ｚ');
                if (!latin) return false;
                hasLetter = true;
            }
            else if (!(char.IsWhiteSpace(ch) || char.IsDigit(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch)))
            {
                return false;
            }
        }
        return hasLetter;
    }

    /// <summary>関与のクレジット上の並び順キー (シリーズ放送開始日シリアル, 話数, クレジット出現位置)。 シリーズスコープ（episode_id=null）は話数 0。</summary>
    private (long Start, int EpNo, long Pos) CreditOrderKey(Involvement inv)
        => LatestAliasResolver.CreditOrderKey(_ctx, inv);

    /// <summary>/creators/roles/{rep_role_code}/ を 3 タブ（五十音順 / 初参加順 / 担当回数順）で書き出す。</summary>
    private void GenerateRoleDetail(
        Role role,
        IReadOnlySet<string> memberCodes,
        IReadOnlyDictionary<string, Role> roleByCode,
        EntityRowSet rowSet)
    {
        var rows = rowSet.CountRows;

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
            // 五十音順タブは読み（kana）データ未整備のため一旦無効化（テンプレも初参加順を既定に繰り上げ済み）。
            // データが揃ったら下行のコメントを外して復活させる。
            // KanaRows = SortByKana(rows),
            // 初参加順は名義ごとの行（改名・屋号変更ごとに、その名義が初めて出たシリーズへ置く）。
            DebutSections = SectionByDebut(rowSet.DebutRows),
            CountRows = SortByCount(rows),
            AlternateNames = alternateNames,
            NameHistory = BuildRoleNameHistory(memberCodes, roleByCode),
            CoverageLabel = _ctx.CreditCoverageLabel,
            // 個人と団体が両方そろっているときだけ entity-filter を出すための件数（片方だけの役職では絞り込みが無意味）。
            PersonCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal)),
            CompanyCount = rows.Count(r => string.Equals(r.EntityKind, "company", StringComparison.Ordinal))
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}（クリエイター）",
            MetaDescription = $"歴代プリキュアシリーズで役職「{role.NameJa}」を担当した人物・企業・団体を一覧にしました。初参加順・担当回数順で並べ替えられます。",
            OgCard = BuildCreatorsOgCard(
                role.NameJa,
                BuildEntityBadges(content.PersonCount, content.CompanyCount),
                alternateNames.Count > 0
                    ? new[] { new OgCardFactLine("別称", string.Join("・", alternateNames.Select(a => a.RoleNameJa))) }
                    : Array.Empty<OgCardFactLine>()),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
                new BreadcrumbItem { Label = role.NameJa, Url = "" }
            }
        };
        // 出力先パスもリンク生成と同一の PathUtil.CreatorsRoleUrl を通すことで、
        // URL パス上のコード小文字化と出力ディレクトリ名を必ず一致させる。
        _page.RenderAndWrite(PathUtil.CreatorsRoleUrl(role.RoleCode), "creators",
            "creators-role-detail.sbn", content, layout);
    }

    /// <summary>
    /// 系譜でまとめた役職の「役職名の変遷」。系譜の各表記（role_code）について、その表記でクレジットされた
    /// 作品（TV 系は話数の範囲つき）と担当数を集め、表記を初めてクレジットされた順に並べる。
    /// 実際にクレジットされた表記が 2 つ以上あるときだけ返し、1 つ以下なら空（テンプレは節を出さない）。
    /// 人物・企業・ロゴのどの経路の関与も、(シリーズ, 話) で重複を除いて数える。
    /// </summary>
    private IReadOnlyList<RoleNameHistoryItem> BuildRoleNameHistory(
        IReadOnlySet<string> memberCodes, IReadOnlyDictionary<string, Role> roleByCode)
    {
        if (memberCodes.Count < 2) return Array.Empty<RoleNameHistoryItem>();

        var usageByCode = new Dictionary<string, (RoleCodeUsage Usage, Dictionary<int, HashSet<int>> EpNosBySeries)>(StringComparer.Ordinal);
        void Offer(Involvement inv)
        {
            if (!memberCodes.Contains(inv.RoleCode)) return;
            if (!usageByCode.TryGetValue(inv.RoleCode, out var u))
            {
                u = (new RoleCodeUsage(), new Dictionary<int, HashSet<int>>());
                usageByCode[inv.RoleCode] = u;
            }
            bool isMovie = _ctx.IsMovieKindSeries(inv.SeriesId);
            u.Usage.Offer(inv, isMovie, CreditOrderKey(inv));
            if (!u.EpNosBySeries.TryGetValue(inv.SeriesId, out var epNos))
            {
                epNos = new HashSet<int>();
                u.EpNosBySeries[inv.SeriesId] = epNos;
            }
            if (!isMovie && inv.EpisodeId is int eid)
                epNos.Add(_ctx.EpisodeSeriesEpNo(inv.SeriesId, eid));
        }
        foreach (var invs in _index.ByPersonAlias.Values) foreach (var inv in invs) Offer(inv);
        foreach (var invs in _index.ByCompanyAlias.Values) foreach (var inv in invs) Offer(inv);
        foreach (var invs in _index.ByLogo.Values) foreach (var inv in invs) Offer(inv);

        if (usageByCode.Count < 2) return Array.Empty<RoleNameHistoryItem>();

        return usageByCode
            .OrderBy(kv => kv.Value.Usage.First)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new RoleNameHistoryItem
            {
                RoleNameJa = roleByCode.TryGetValue(kv.Key, out var r) ? r.NameJa : kv.Key,
                EpisodeCount = kv.Value.Usage.EpisodeCount,
                MovieCount = kv.Value.Usage.MovieCount,
                Works = kv.Value.EpNosBySeries
                    .Where(w => _ctx.SeriesById.ContainsKey(w.Key))
                    .Select(w => (Series: _ctx.SeriesById[w.Key], EpNos: w.Value))
                    .OrderBy(w => w.Series.StartDate)
                    .ThenBy(w => w.Series.SeriesId)
                    .Select(w => new RoleNameHistoryWork
                    {
                        SeriesTitle = w.Series.Title,
                        SeriesUrl = PathUtil.SeriesUrl(w.Series.Slug),
                        SeriesYearLabel = w.Series.StartDate.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        EpisodeRangeLabel = EpisodeRangeCompressor.Compress(w.EpNos)
                    })
                    .ToList()
            })
            .ToList();
    }

    // ────────────────────────────────────────────────────────────────────
    // 歌系 4 役職（LYRICS / COMPOSITION / ARRANGEMENT / VOCALS）の専用集計
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 歌系役職 1 種について、人物別に「関与楽曲数（distinct song_id）」を集計した行群を作る。
    /// LYRICS / COMPOSITION / ARRANGEMENT は <c>song_credits</c> 由来、
    /// VOCALS は <c>song_recording_singers</c> 由来。
    /// VOCALS は主歌唱者 (person_alias_id) / スラッシュ並列の相方 (slash_person_alias_id) /
    /// CHARACTER_WITH_CV の声優 (voice_person_alias_id) の 3 系統を合算する
    /// （いずれも person_alias_id 系統で、character_alias は集計対象外。
    /// キャラ側の歌唱履歴はキャラクター詳細ページ側で扱う想定）。
    /// 集計は person_id 単位（person_aliases 経由）で重複排除する。
    /// </summary>
    private List<SongRoleRow> BuildSongRoleRows(
        string roleCode,
        IReadOnlyList<SongCredit> allSongCredits,
        IReadOnlyList<SongRecordingSinger> allSingers,
        IReadOnlyDictionary<int, int> personIdByAlias,
        IReadOnlyDictionary<int, Person> personById)
    {
        // 曲ごとの最小 recording_id。初参加順（recording_id 順）の代理キーに使う。
        // song_credits（作詞・作曲・編曲）は曲単位の紐付けで recording_id を直接持たないため、
        // その曲の録音群のうち最小 recording_id を「その曲の初出」とみなす。recording_id はほぼ登録＝時系列順。
        var minRecIdBySong = new Dictionary<int, int>();
        foreach (var rec in _ctx.SongRecordingById.Values)
        {
            if (!minRecIdBySong.TryGetValue(rec.SongId, out var cur) || rec.SongRecordingId < cur)
                minRecIdBySong[rec.SongId] = rec.SongRecordingId;
        }

        var songsByPerson = new Dictionary<int, HashSet<int>>();
        // 人物ごとの初参加 recording_id（関与した録音／曲の最小 recording_id）。
        var debutRecIdByPerson = new Dictionary<int, int>();

        void Add(int? aliasId, int songId, int recordingId)
        {
            if (aliasId is not int aid) return;
            if (!personIdByAlias.TryGetValue(aid, out var pid)) return;
            if (!songsByPerson.TryGetValue(pid, out var set))
            {
                set = new HashSet<int>();
                songsByPerson[pid] = set;
            }
            set.Add(songId);
            if (recordingId > 0
                && (!debutRecIdByPerson.TryGetValue(pid, out var cur) || recordingId < cur))
            {
                debutRecIdByPerson[pid] = recordingId;
            }
        }

        if (string.Equals(roleCode, SongRecordingSingerRoles.Vocals, StringComparison.Ordinal))
        {
            foreach (var s in allSingers)
            {
                if (!string.Equals(s.RoleCode, SongRecordingSingerRoles.Vocals, StringComparison.Ordinal)) continue;
                if (!_ctx.SongRecordingById.TryGetValue(s.SongRecordingId, out var rec)) continue;
                int songId = rec.SongId;
                // 歌唱は録音単位なので recording_id を直接使う。
                // 主名義・スラッシュ相方・キャラ歌唱の声優に加え、ユニット名義のメンバー（人物 / キャラの声優）まで展開する。
                foreach (var p in _ctx.ExpandSingerParticipants(s))
                    Add(p.PersonAliasId, songId, s.SongRecordingId);
            }
        }
        else
        {
            foreach (var c in allSongCredits)
            {
                if (!string.Equals(c.CreditRole, roleCode, StringComparison.Ordinal)) continue;
                int recId = minRecIdBySong.TryGetValue(c.SongId, out var r) ? r : 0;
                Add(c.PersonAliasId, c.SongId, recId);
            }
        }

        var rows = new List<SongRoleRow>(songsByPerson.Count);
        foreach (var kv in songsByPerson)
        {
            if (!personById.TryGetValue(kv.Key, out var p)) continue;
            rows.Add(new SongRoleRow
            {
                PersonId = kv.Key,
                // 人物詳細の見出し・URL と同じ表示名義で出す（クレジットの無い人物は正式名）。
                PersonName = _ctx.EntityUrls.PersonDisplayName(p.PersonId) ?? p.FullName,
                PersonNameKana = _ctx.EntityUrls.PersonDisplayKana(p.PersonId) ?? (p.FullNameKana ?? ""),
                PersonUrl = PathUtil.PersonUrl(kv.Key),
                SongCount = kv.Value.Count,
                // 初参加順の代理キー。未取得は末尾に送るため int.MaxValue。
                DebutRecordingId = debutRecIdByPerson.TryGetValue(kv.Key, out var dr) ? dr : int.MaxValue
            });
        }
        return rows;
    }

    /// <summary>歌系役職 1 種の専用ページ <c>/creators/roles/{code}/</c> を「五十音順 / 担当曲数順」の 2 タブで書き出す。</summary>
    private void GenerateSongRoleDetail(Role role, List<SongRoleRow> rows)
    {
        var content = new SongRoleDetailModel
        {
            RoleNameJa = role.NameJa,
            // 五十音順タブは読み（kana）データ未整備のため一旦無効化。代わりに初参加順（recording_id 順）を既定タブにする。
            // 読みデータが揃ったら KanaRows の行のコメントを外して五十音順タブを復活させられる。
            // KanaRows = SortSongRowsByKana(rows),
            DebutRows = SortSongRowsByDebut(rows),
            CountRows = SortSongRowsByCount(rows),
            CoverageLabel = MusicCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}（クリエイター）",
            MetaDescription = $"歴代プリキュアの楽曲で役職「{role.NameJa}」を担当した人物を一覧にしました。初参加順・担当曲数順で並べ替えられます。",
            OgCard = BuildCreatorsOgCard(
                role.NameJa,
                new[] { new OgCardBadge("人物", $"{rows.Count}人") },
                new[] { new OgCardFactLine("集計元", "楽曲のクレジット（劇中歌・キャラクターソングを含む）") }, MusicCoverageLabel),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュア音楽制作", Url = PathUtil.CreatorsMusicProductionUrl() },
                new BreadcrumbItem { Label = role.NameJa, Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsRoleUrl(role.RoleCode), "creators",
            "creators-song-role-detail.sbn", content, layout);
    }

    /// <summary>五十音順：読み昇順（空読みは末尾） → 名前。</summary>
    private static List<SongRoleRow> SortSongRowsByKana(IEnumerable<SongRoleRow> rows) => rows
        .OrderBy(r => string.IsNullOrEmpty(r.PersonNameKana) ? 1 : 0)
        .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.PersonName, StringComparer.Ordinal)
        .ToList();

    /// <summary>初参加順：最小 recording_id 昇順（recording_id はほぼ時系列の代理）→ 読み → 名前。 読みデータ未整備の暫定で五十音順の代替に使う既定タブ。</summary>
    private static List<SongRoleRow> SortSongRowsByDebut(IEnumerable<SongRoleRow> rows) => rows
        .OrderBy(r => r.DebutRecordingId)
        .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.PersonName, StringComparer.Ordinal)
        .ToList();

    /// <summary>担当曲数順：曲数降順 → 読み → 名前（順位は付けない）。</summary>
    private static List<SongRoleRow> SortSongRowsByCount(IEnumerable<SongRoleRow> rows) => rows
        .OrderByDescending(r => r.SongCount)
        .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.PersonName, StringComparer.Ordinal)
        .ToList();

    // 音楽制作

    /// <summary>
    /// <c>/creators/music-production/</c> を 3 タブ（役職 / 歌 / 劇伴）で書き出す。
    /// <list type="bullet">
    ///   <item><description>役職：音楽クレジットの区分（作詞・作曲・編曲 / 演奏・コーラス等 / レコーディング / 音盤製作）ごとに役職を並べ、
    ///     各役職の詳細ページへ送る。歌唱系の役職（歌・コーラス・台詞）は歌唱ページが担うので出さない。</description></item>
    ///   <item><description>歌：主題歌・挿入歌・キャラクターソングに関わった人・団体（作家 song_credits と、曲・録音に付いた music_credits）。
    ///     参加曲数は曲単位で数え、初参加は関わった曲の最小録音 ID（録音 ID は初出順）。
    ///     歌唱ページの歌手に載らない人（<see cref="LeadSingerPersons"/> に入らない人）の本人名義での歌唱
    ///     （コーラスだけ・名前の出ないユニットのメンバーだけ）も、その歌唱役職（歌・コーラス等）で演奏に載せる。</description></item>
    ///   <item><description>劇伴：劇伴に関わった人・団体（作曲・編曲 bgm_cue_credits と、劇伴セッションに付いた music_credits）。
    ///     演奏者はセッション単位でしか関わりが分からないので、作品（シリーズ・映画）単位で数える。初参加は作品の放送開始日。</description></item>
    /// </list>
    /// 盤（商品）だけに付くスタッフは歌・劇伴のどちらにも数えず、役職タブとその詳細ページからたどる。
    /// 歌・劇伴のタブは、タブの中の切り替えで「初参加順」「多い順」に並べ替える。
    /// </summary>
    private void GenerateMusicProduction(
        IReadOnlyList<RoleIndexEntry> songCreditRoleEntries,
        IReadOnlyList<SongCredit> allSongCredits,
        IReadOnlyList<SongRecordingSinger> allSingers,
        IReadOnlySet<int> leadSingers,
        IReadOnlyDictionary<int, int> personIdByAlias,
        IReadOnlyDictionary<int, Person> personById,
        IReadOnlyList<Role> allRoles,
        out int personCount,
        out int companyCount)
    {
        var minRecIdBySong = MinRecordingIdBySong();
        bool IsGroup(string roleCode, string group) => MusicCreditViewBuilder.GroupOf(_ctx, roleCode) == group;

        // 曲・録音に付いた音楽クレジット行を (曲 ID, 行) に展開する。
        var songTargetRows = _ctx.MusicCredits.BySong.SelectMany(kv => kv.Value.Select(r => (SongId: kv.Key, Row: r)))
            .Concat(_ctx.MusicCredits.ByRecording
                .Where(kv => _ctx.SongRecordingById.ContainsKey(kv.Key))
                .SelectMany(kv => kv.Value.Select(r => (SongId: _ctx.SongRecordingById[kv.Key].SongId, Row: r))))
            .ToList();

        // ── 歌（詞曲）：作詞・作曲・編曲（song_credits と、曲に付いた音楽クレジットの作詞・作曲・編曲） ──
        var songWritingAcc = new MusicEntityAccumulator();
        foreach (var c in allSongCredits)
            if (IsGroup(c.CreditRole, MusicCreditGroups.Writing) && PersonKey(c.PersonAliasId, personIdByAlias) is { } key)
                AddSong(songWritingAcc, key, c.SongId, c.CreditRole, minRecIdBySong);
        foreach (var (songId, r) in songTargetRows)
            if (IsGroup(r.RoleCode, MusicCreditGroups.Writing) && EntityKeyOf(r, personIdByAlias) is { } key)
                AddSong(songWritingAcc, key, songId, r.RoleCode, minRecIdBySong);

        // ── 歌（演奏）：曲に付いた音楽クレジットの演奏・コーラス等 ──
        var songPerformanceAcc = new MusicEntityAccumulator();
        foreach (var (songId, r) in songTargetRows)
            if (IsGroup(r.RoleCode, MusicCreditGroups.Performance) && EntityKeyOf(r, personIdByAlias) is { } key)
                AddSong(songPerformanceAcc, key, songId, r.RoleCode, minRecIdBySong);
        // 歌唱ページの歌手に載らない人の本人名義での歌唱（コーラスだけ・名前の出ないユニットのメンバーだけ）は、
        // その歌唱役職（歌・コーラス等）で演奏側に載せる。キャラクターとしての歌唱は歌唱ページが受け持つ。
        foreach (var s in allSingers)
        {
            if (!_ctx.SongRecordingById.TryGetValue(s.SongRecordingId, out var rec)) continue;
            foreach (var p in _ctx.ExpandSingerParticipants(s))
                if (p.CharacterAliasId is null && p.PersonAliasId is int paid
                    && personIdByAlias.TryGetValue(paid, out var pid) && !leadSingers.Contains(pid))
                    AddSong(songPerformanceAcc, ('P', pid), rec.SongId, s.RoleCode, minRecIdBySong);
        }

        // ── 劇伴（作編曲）：bgm_cue_credits と、劇伴セッションに付いた音楽クレジットの作詞・作曲・編曲 ──
        var bgmWritingAcc = new MusicEntityAccumulator();
        foreach (var (cueKey, credits) in _ctx.BgmCueCreditsByCue)
            foreach (var c in credits)
                if (PersonKey(c.PersonAliasId, personIdByAlias) is { } key)
                    AddSeries(bgmWritingAcc, key, cueKey.SeriesId, c.CreditRole, bgm: true);
        foreach (var (sessionKey, rows) in _ctx.MusicCredits.BySession)
            foreach (var r in rows)
                if (IsGroup(r.RoleCode, MusicCreditGroups.Writing) && EntityKeyOf(r, personIdByAlias) is { } key)
                    AddSeries(bgmWritingAcc, key, sessionKey.SeriesId, r.RoleCode, bgm: true);

        // ── 劇伴（演奏）：劇伴セッションに付いた音楽クレジットの演奏・コーラス等 ──
        var bgmPerformanceAcc = new MusicEntityAccumulator();
        foreach (var (sessionKey, rows) in _ctx.MusicCredits.BySession)
            foreach (var r in rows)
                if (IsGroup(r.RoleCode, MusicCreditGroups.Performance) && EntityKeyOf(r, personIdByAlias) is { } key)
                    AddSeries(bgmPerformanceAcc, key, sessionKey.SeriesId, r.RoleCode, bgm: true);

        // ── 制作：レコーディング・音盤製作。曲・劇伴セッションに付いたものは作品単位、盤に付いたものは盤（商品）単位で数える。
        //    盤の分の初参加は、ディスクに登録されたシリーズで決める。 ──
        var productionAcc = new MusicEntityAccumulator();
        foreach (var r in _ctx.MusicCredits.BySong.Values.Concat(_ctx.MusicCredits.ByRecording.Values)
                     .Concat(_ctx.MusicCredits.BySession.Values).Concat(_ctx.MusicCredits.ByProduct.Values).SelectMany(x => x))
        {
            if (!IsGroup(r.RoleCode, MusicCreditGroups.Recording) && !IsGroup(r.RoleCode, MusicCreditGroups.Release)) continue;
            if (EntityKeyOf(r, personIdByAlias) is not { } key) continue;
            int? sid = SeriesOfMusicCredit(r, minRecIdBySong);
            if (r.TargetKind == MusicCreditTargetKinds.Product && r.ProductCatalogNo is string catalogNo)
            {
                if (sid is int psid) AddSeries(productionAcc, key, psid, r.RoleCode, bgm: false, productCatalogNo: catalogNo);
                else productionAcc.Add(key, 0, r.RoleCode, long.MaxValue, "", "", null, product: catalogNo);
            }
            else if (sid is int wsid) AddSeries(productionAcc, key, wsid, r.RoleCode, bgm: false);
        }

        var allEntities = new[] { songWritingAcc, songPerformanceAcc, bgmWritingAcc, bgmPerformanceAcc, productionAcc }
            .SelectMany(a => a.ByEntity.Keys).Distinct().ToList();
        foreach (var k in allEntities)
        {
            if (k.Kind == 'P') _lists.MusicProductionPersons.Add(k.Id);
            else if (k.Kind == 'C') _lists.MusicProductionCompanies.Add(k.Id);
        }
        personCount = allEntities.Count(k => k.Kind == 'P');
        companyCount = allEntities.Count(k => k.Kind == 'C');

        // ── 役職 ──
        var roleSections = BuildMusicRoleSections(songCreditRoleEntries, allSongCredits, personIdByAlias, personById, allRoles, minRecIdBySong);

        var content = new SongPersonListModel
        {
            RoleSections = roleSections,
            Tabs = new[]
            {
                BuildMusicListTab("song-writing", "歌（詞曲）", songWritingAcc, personById, byWork: false),
                BuildMusicListTab("song-performance", "歌（演奏）", songPerformanceAcc, personById, byWork: false),
                BuildMusicListTab("bgm-writing", "劇伴（作編曲）", bgmWritingAcc, personById, byWork: true),
                BuildMusicListTab("bgm-performance", "劇伴（演奏）", bgmPerformanceAcc, personById, byWork: true),
                BuildMusicListTab("production", "制作", productionAcc, personById, byWork: true),
            },
            CoverageLabel = MusicCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュア音楽制作",
            MetaDescription = "プリキュアの主題歌・挿入歌・キャラクターソングと劇伴の制作に携わった人々を一覧。役職、初参加、参加数から探せます。",
            OgCard = BuildCreatorsOgCard(
                "歴代プリキュア音楽制作",
                BuildEntityBadges(personCount, companyCount),
                new[] { new OgCardFactLine("集計元", "楽曲・劇伴・音盤のクレジット") }, MusicCoverageLabel),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュア音楽制作", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsMusicProductionUrl(), "creators",
            "creators-music-production.sbn", content, layout);

        void AddSong(MusicEntityAccumulator acc, (char Kind, int Id) key, int songId, string roleCode, IReadOnlyDictionary<int, int> minRec)
        {
            long sort = minRec.TryGetValue(songId, out var rid) ? rid : long.MaxValue;
            string label = _ctx.SongById.TryGetValue(songId, out var song) ? song.Title : "";
            int? seriesId = minRec.TryGetValue(songId, out var firstRec) && _ctx.SongRecordingById.TryGetValue(firstRec, out var rec)
                ? rec.SeriesId : null;
            acc.Add(key, songId, roleCode, sort, label, PathUtil.SongUrl(songId), seriesId);
        }

        // productCatalogNo を渡すと、作品ではなくその盤（商品）の参加として数える（初参加は作品で決める）。
        void AddSeries(MusicEntityAccumulator acc, (char Kind, int Id) key, int seriesId, string roleCode, bool bgm, string? productCatalogNo = null)
        {
            if (!_ctx.SeriesById.TryGetValue(seriesId, out var series)) return;
            // 劇伴のタブは初参加の作品を劇伴詳細へ、制作のタブはシリーズ詳細へリンクする。
            string url = bgm ? PathUtil.BgmsForSeriesUrl(series.Slug) : PathUtil.SeriesUrl(series.Slug);
            acc.Add(key, seriesId, roleCode, _ctx.SeriesStartDate(seriesId).DayNumber, series.Title, url, seriesId, productCatalogNo);
        }
    }

    /// <summary>
    /// 音楽クレジット 1 行の作品（シリーズ）。曲は初出録音の出典シリーズ、録音はその出典シリーズ、劇伴セッションはそのシリーズ、
    /// 盤はディスクに登録されたシリーズ。解決できなければ null。
    /// </summary>
    private int? SeriesOfMusicCredit(MusicCredit r, IReadOnlyDictionary<int, int> minRecIdBySong) => r.TargetKind switch
    {
        MusicCreditTargetKinds.Song when r.SongId is int sid && minRecIdBySong.TryGetValue(sid, out var rid)
                                         && _ctx.SongRecordingById.TryGetValue(rid, out var rec) => rec.SeriesId,
        MusicCreditTargetKinds.SongRecording when r.SongRecordingId is int rid2
                                                  && _ctx.SongRecordingById.TryGetValue(rid2, out var rec2) => rec2.SeriesId,
        MusicCreditTargetKinds.BgmSession => r.BgmSeriesId,
        MusicCreditTargetKinds.Product when r.ProductCatalogNo is string pc
                                            && _ctx.MusicCredits.SeriesIdByProduct.TryGetValue(pc, out var psid) => psid,
        _ => null
    };

    /// <summary>
    /// 音楽制作ページの一覧タブ 1 つ分（初参加順のシリーズ別セクションと、多い順の一覧）を組み立てる。
    /// <paramref name="byWork"/> が true なら参加数を作品数、false なら曲数として数える。
    /// </summary>
    private MusicListTab BuildMusicListTab(string key, string label, MusicEntityAccumulator acc, IReadOnlyDictionary<int, Person> personById, bool byWork)
    {
        var rows = BuildMusicEntityRows(acc, personById);
        return new MusicListTab
        {
            Key = key,
            Label = label,
            IsByWork = byWork,
            CountSortLabel = byWork ? "参加作品数順" : "参加曲数順",
            DebutSections = BuildDebutSeriesSections(
                rows.OrderBy(r => r.DebutSort).ThenBy(r => r.NameKana, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ToList(),
                r => r.DebutSeriesId),
            CountRows = rows.OrderByDescending(r => r.Count).ThenBy(r => r.NameKana, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.Ordinal).ToList()
        };
    }

    /// <summary>
    /// 音楽制作ページの役職タブ：音楽クレジットの区分ごとに役職を並べる（区分内は役職マスタの表示順）。
    /// 作詞・作曲・編曲は既存の役職詳細（楽曲の作家の一覧）へ、それ以外の役職は音楽クレジットの役職詳細を書き出して送る。
    /// 歌唱系の役職（歌・コーラス・台詞）は歌唱ページが担うので載せない。関わった人・団体が居ない役職も載せない。
    /// </summary>
    private IReadOnlyList<MusicRoleSection> BuildMusicRoleSections(
        IReadOnlyList<RoleIndexEntry> songCreditRoleEntries,
        IReadOnlyList<SongCredit> allSongCredits,
        IReadOnlyDictionary<int, int> personIdByAlias,
        IReadOnlyDictionary<int, Person> personById,
        IReadOnlyList<Role> allRoles,
        IReadOnlyDictionary<int, int> minRecIdBySong)
    {
        var entryByCode = songCreditRoleEntries.ToDictionary(e => e.RoleNameKey, StringComparer.Ordinal);
        var musicRowsByRole = _ctx.MusicCredits.BySong.Values
            .Concat(_ctx.MusicCredits.ByRecording.Values)
            .Concat(_ctx.MusicCredits.BySession.Values)
            .Concat(_ctx.MusicCredits.ByProduct.Values)
            .SelectMany(x => x)
            .GroupBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var sections = new List<MusicRoleSection>();
        foreach (var group in MusicCreditGroups.All)
        {
            var entries = new List<RoleIndexEntry>();
            foreach (var role in allRoles
                .Where(r => r.MusicCreditGroup == group && !PathUtil.IsSingerRole(r.RoleCode))
                .OrderBy(r => r.DisplayOrder ?? ushort.MaxValue).ThenBy(r => r.RoleCode, StringComparer.Ordinal))
            {
                if (entryByCode.TryGetValue(role.RoleCode, out var songEntry))
                {
                    entries.Add(songEntry);
                    continue;
                }
                var rows = musicRowsByRole.TryGetValue(role.RoleCode, out var mr) ? mr : new List<MusicCredit>();
                var songCreditRows = allSongCredits.Where(c => string.Equals(c.CreditRole, role.RoleCode, StringComparison.Ordinal)).ToList();
                var detail = BuildMusicRoleDetailRows(rows, songCreditRows, personIdByAlias, personById, minRecIdBySong);
                if (detail.Count == 0) continue;
                GenerateMusicRoleDetail(role, detail);
                entries.Add(new RoleIndexEntry
                {
                    RoleNameJa = role.NameJa,
                    RoleUrl = PathUtil.CreatorsRoleUrl(role.RoleCode),
                    PersonCount = detail.Count(d => d.EntityKind == "person"),
                    CompanyCount = detail.Count(d => d.EntityKind == "company"),
                    RoleNameKey = role.RoleCode
                });
            }
            if (entries.Count > 0)
                sections.Add(new MusicRoleSection { Label = MusicCreditGroups.Label(group), Roles = entries });
        }
        return sections;
    }

    /// <summary>
    /// 音楽クレジットの役職 1 つについて、関わった人・団体ごとに担当先を「劇伴」「歌」「盤」に分けて並べた行を作る。
    /// 劇伴は 1 作品 1 行（その作品で担当した録音回を添える）、歌は曲・録音ごとに 1 行、盤は 1 点 1 行。
    /// 件数は劇伴を TV 系の作品数と映画系の本数、歌を曲数、盤を点数で数える。
    /// 行の並びは最初の担当先の日付（根拠の盤の発売日）順。名義が人物・団体でない行（自由記述）は載せない。
    /// </summary>
    private List<MusicRoleDetailRow> BuildMusicRoleDetailRows(
        IReadOnlyList<MusicCredit> rows,
        IReadOnlyList<SongCredit> songCreditRows,
        IReadOnlyDictionary<int, int> personIdByAlias,
        IReadOnlyDictionary<int, Person> personById,
        IReadOnlyDictionary<int, int> minRecIdBySong)
    {
        var accByEntity = new Dictionary<(char Kind, int Id), MusicRoleTargetAccumulator>();
        MusicRoleTargetAccumulator AccOf((char Kind, int Id) key)
        {
            if (!accByEntity.TryGetValue(key, out var acc))
            {
                acc = new MusicRoleTargetAccumulator();
                accByEntity[key] = acc;
            }
            return acc;
        }
        int? SeriesOfSong(int songId) => minRecIdBySong.TryGetValue(songId, out var rid)
                                         && _ctx.SongRecordingById.TryGetValue(rid, out var rec) ? rec.SeriesId : null;

        foreach (var r in rows)
        {
            if (EntityKeyOf(r, personIdByAlias) is not { } key) continue;
            if (MusicCreditViewBuilder.DescribeTarget(_ctx, r) is not { } t) continue;
            var acc = AccOf(key);
            switch (r.TargetKind)
            {
                case MusicCreditTargetKinds.BgmSession when r.BgmSeriesId is int bsid:
                    acc.AddBgm(bsid, r.BgmSessionNo ?? 0,
                        _ctx.MusicCredits.SessionByKey.TryGetValue((bsid, r.BgmSessionNo ?? 0), out var session) ? session.SessionName : "",
                        t.Sort);
                    break;
                case MusicCreditTargetKinds.Song when r.SongId is int sid:
                    acc.AddSong(sid, t.Title, t.Url, t.Sort, SeriesOfSong(sid));
                    break;
                case MusicCreditTargetKinds.SongRecording when r.SongRecordingId is int rid && _ctx.SongRecordingById.TryGetValue(rid, out var rec):
                    acc.AddSong(rec.SongId, t.Title, t.Url, t.Sort, rec.SeriesId);
                    break;
                case MusicCreditTargetKinds.Product when r.ProductCatalogNo is string pc:
                    acc.AddProduct(pc, t.Title, t.Url, t.Sort,
                        _ctx.MusicCredits.SeriesIdByProduct.TryGetValue(pc, out var psid) ? psid : null);
                    break;
            }
        }
        foreach (var c in songCreditRows)
        {
            if (PersonKey(c.PersonAliasId, personIdByAlias) is not { } key) continue;
            if (!_ctx.SongById.TryGetValue(c.SongId, out var song)) continue;
            AccOf(key).AddSong(c.SongId, song.Title, PathUtil.SongUrl(c.SongId), DateTime.MaxValue, SeriesOfSong(c.SongId));
        }

        var result = new List<MusicRoleDetailRow>();
        foreach (var (key, acc) in accByEntity)
        {
            if (ResolveMusicEntity(key, personById) is not { } ent) continue;
            // 劇伴は作品の放送・公開順、歌と盤は根拠の盤の発売日順に並べる。
            var bgm = acc.Bgm
                .Where(kv => _ctx.SeriesById.ContainsKey(kv.Key))
                .OrderBy(kv => _ctx.SeriesStartDate(kv.Key).DayNumber)
                .Select(kv => new MusicRoleTarget
                {
                    Title = _ctx.SeriesById[kv.Key].Title,
                    Url = PathUtil.BgmsForSeriesUrl(_ctx.SeriesById[kv.Key].Slug),
                    Sub = string.Join("・", kv.Value.Sessions.OrderBy(s => s.Key).Select(s => s.Value).Where(n => n.Length > 0))
                })
                .ToList();
            static List<MusicRoleTarget> Ordered(IEnumerable<MusicRoleTargetAccumulator.Item> items) => items
                .OrderBy(i => i.Sort).ThenBy(i => i.Title, StringComparer.Ordinal)
                .Select(i => new MusicRoleTarget { Title = i.Title, Url = i.Url })
                .ToList();
            var bgmSeries = acc.Bgm.Keys.Where(_ctx.SeriesById.ContainsKey).ToList();
            var row = new MusicRoleDetailRow
            {
                EntityKind = ent.Kind,
                Name = ent.Name,
                NameKana = ent.Kana,
                Url = ent.Url,
                BgmItems = bgm,
                SongItems = Ordered(acc.Songs.Values),
                ProductItems = Ordered(acc.Products.Values),
                TvWorkCount = bgmSeries.Count(sid => !_ctx.IsMovieKindSeries(sid)),
                MovieWorkCount = bgmSeries.Count(sid => _ctx.IsMovieKindSeries(sid)),
                SongCount = acc.SongIds.Count,
                ProductCount = acc.Products.Count
            };
            row.Count = row.TvWorkCount + row.MovieWorkCount + row.SongCount + row.ProductCount;
            if (row.Count == 0) continue;
            // 初参加：日付が最も早い担当先（同日・日付不明はシリーズの放送開始が早い方）。
            var debut = acc.Candidates
                .OrderBy(c => c.Sort)
                .ThenBy(c => c.SeriesId is int sid && _ctx.SeriesById.ContainsKey(sid) ? _ctx.SeriesStartDate(sid).DayNumber : int.MaxValue)
                .First();
            row.FirstSort = debut.Sort;
            row.DebutSeriesId = debut.SeriesId;
            result.Add(row);
        }
        return result
            .OrderBy(r => r.FirstSort)
            .ThenBy(r => r.DebutSeriesId is int sid && _ctx.SeriesById.ContainsKey(sid) ? _ctx.SeriesStartDate(sid).DayNumber : int.MaxValue)
            .ThenBy(r => r.NameKana, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>音楽クレジットの役職詳細で、人物・団体 1 つ分の担当先（劇伴・歌・盤）を集める。</summary>
    private sealed class MusicRoleTargetAccumulator
    {
        /// <summary>担当先 1 件（歌・盤）。同じリンク先は日付の早い方を残す。</summary>
        public sealed record Item(string Title, string Url, DateTime Sort);

        /// <summary>劇伴の作品（series_id）ごとの録音回（session_no → 録音回の名前）。</summary>
        public sealed class BgmWork
        {
            public readonly SortedDictionary<int, string> Sessions = new();
        }

        public readonly Dictionary<int, BgmWork> Bgm = new();
        public readonly Dictionary<string, Item> Songs = new(StringComparer.Ordinal);
        public readonly HashSet<int> SongIds = new();
        public readonly Dictionary<string, Item> Products = new(StringComparer.Ordinal);
        /// <summary>初参加の候補（担当先ごとの日付と作品）。</summary>
        public readonly List<(DateTime Sort, int? SeriesId)> Candidates = new();

        public void AddBgm(int seriesId, int sessionNo, string sessionName, DateTime sort)
        {
            if (!Bgm.TryGetValue(seriesId, out var work))
            {
                work = new BgmWork();
                Bgm[seriesId] = work;
            }
            work.Sessions[sessionNo] = sessionName;
            Candidates.Add((sort, seriesId));
        }

        public void AddSong(int songId, string title, string url, DateTime sort, int? seriesId)
        {
            SongIds.Add(songId);
            AddItem(Songs, title, url, sort);
            Candidates.Add((sort, seriesId));
        }

        public void AddProduct(string catalogNo, string title, string url, DateTime sort, int? seriesId)
        {
            if (!Products.TryGetValue(catalogNo, out var cur) || sort < cur.Sort) Products[catalogNo] = new Item(title, url, sort);
            Candidates.Add((sort, seriesId));
        }

        private static void AddItem(Dictionary<string, Item> map, string title, string url, DateTime sort)
        {
            string k = url + "|" + title;
            if (!map.TryGetValue(k, out var cur) || sort < cur.Sort) map[k] = new Item(title, url, sort);
        }
    }

    /// <summary>音楽クレジットの役職詳細 <c>/creators/roles/{code}/</c>（関わった人・団体と担当先の一覧）を書き出す。</summary>
    private void GenerateMusicRoleDetail(Role role, IReadOnlyList<MusicRoleDetailRow> rows)
    {
        var content = new MusicRoleDetailModel
        {
            RoleNameJa = role.NameJa,
            GroupLabel = MusicCreditGroups.Label(role.MusicCreditGroup ?? ""),
            DebutSections = BuildDebutSeriesSections(rows, r => r.DebutSeriesId),
            CountRows = rows
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.NameKana, StringComparer.Ordinal)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ToList(),
            CoverageLabel = MusicCoverageLabel
        };
        int persons = rows.Count(r => r.EntityKind == "person");
        int companies = rows.Count - persons;
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}（クリエイター）",
            MetaDescription = $"歴代プリキュアの楽曲・劇伴・音盤で「{role.NameJa}」を担当した人物・団体を一覧にしました。",
            OgCard = BuildCreatorsOgCard(
                role.NameJa,
                BuildEntityBadges(persons, companies),
                new[] { new OgCardFactLine("集計元", "楽曲・劇伴・音盤のクレジット") }, MusicCoverageLabel),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュア音楽制作", Url = PathUtil.CreatorsMusicProductionUrl() },
                new BreadcrumbItem { Label = role.NameJa, Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsRoleUrl(role.RoleCode), "creators",
            "creators-music-role-detail.sbn", content, layout);
    }

    /// <summary>人物名義 → 人物キー（'P', person_id）。人物に紐付かない名義は null。</summary>
    private static (char Kind, int Id)? PersonKey(int personAliasId, IReadOnlyDictionary<int, int> personIdByAlias)
        => personIdByAlias.TryGetValue(personAliasId, out var pid) ? ('P', pid) : null;

    /// <summary>音楽クレジット行の名義 → 人物キー（'P', person_id）または団体キー（'C', company_id）。キャラ・自由記述は null。</summary>
    private (char Kind, int Id)? EntityKeyOf(MusicCredit r, IReadOnlyDictionary<int, int> personIdByAlias)
    {
        if (r.PersonAliasId is int pa) return PersonKey(pa, personIdByAlias);
        if (r.CompanyAliasId is int ca && _ctx.CompanyAliasById.TryGetValue(ca, out var alias) && alias.CompanyId > 0) return ('C', alias.CompanyId);
        return null;
    }

    /// <summary>人物・団体キー → 表示名・読み・詳細 URL。人物は人物詳細と同じ表示名義、団体は最後に使われた屋号。</summary>
    private (string Kind, string Name, string Kana, string Url)? ResolveMusicEntity((char Kind, int Id) key, IReadOnlyDictionary<int, Person> personById)
    {
        if (key.Kind == 'P')
        {
            if (!personById.TryGetValue(key.Id, out var p)) return null;
            return ("person",
                _ctx.EntityUrls.PersonDisplayName(key.Id) ?? p.FullName,
                _ctx.EntityUrls.PersonDisplayKana(key.Id) ?? (p.FullNameKana ?? ""),
                PathUtil.PersonUrl(key.Id));
        }
        var companyAlias = _latestCompanyAliasId.TryGetValue(key.Id, out var latest) && _ctx.CompanyAliasById.TryGetValue(latest, out var la)
            ? la
            : _ctx.CompanyAliasById.Values.FirstOrDefault(a => a.CompanyId == key.Id);
        if (companyAlias is null || string.IsNullOrEmpty(companyAlias.Name)) return null;
        return ("company", companyAlias.Name, companyAlias.NameKana ?? "", PathUtil.CompanyUrl(key.Id));
    }

    /// <summary>歌・劇伴タブの行を、人物・団体ごとの参加数・初参加・役職で組み立てる。</summary>
    private List<MusicEntityRow> BuildMusicEntityRows(MusicEntityAccumulator acc, IReadOnlyDictionary<int, Person> personById)
    {
        var rows = new List<MusicEntityRow>(acc.ByEntity.Count);
        foreach (var (key, v) in acc.ByEntity)
        {
            if (ResolveMusicEntity(key, personById) is not { } ent) continue;
            rows.Add(new MusicEntityRow
            {
                EntityKind = ent.Kind,
                Name = ent.Name,
                NameKana = ent.Kana,
                Url = ent.Url,
                Count = v.Units.Count + v.Products.Count,
                // 作品単位のタブでは作品を TV 系と映画系に分けて数える（歌のタブでは使わない）。
                TvWorkCount = v.Units.Count(sid => !_ctx.IsMovieKindSeries(sid)),
                MovieWorkCount = v.Units.Count(sid => _ctx.IsMovieKindSeries(sid)),
                ProductCount = v.Products.Count,
                DebutSort = v.DebutSort,
                DebutLabel = v.DebutLabel,
                DebutUrl = v.DebutUrl,
                DebutSeriesId = v.DebutSeriesId,
                Roles = v.Roles
                    .Select(code => (Code: code, Order: _ctx.RoleByCode.TryGetValue(code, out var role) ? role.DisplayOrder ?? ushort.MaxValue : ushort.MaxValue))
                    .OrderBy(x => x.Order)
                    .Select(x => new MusicRoleBadge { Code = x.Code, Label = MusicCreditViewBuilder.BadgeLabel(_ctx, x.Code) })
                    .ToList()
            });
        }
        return rows;
    }

    /// <summary>人物・団体ごとの参加（参加単位＝曲または作品の集合・役職・初参加）を集める。</summary>
    private sealed class MusicEntityAccumulator
    {
        public readonly Dictionary<(char Kind, int Id), Participation> ByEntity = new();

        public sealed class Participation
        {
            /// <summary>参加単位（歌のタブは song_id、劇伴・制作のタブは series_id）。</summary>
            public readonly HashSet<int> Units = new();
            /// <summary>盤（商品）単位の参加（制作のタブの盤に付いたクレジット。代表品番）。</summary>
            public readonly HashSet<string> Products = new(StringComparer.Ordinal);
            public readonly HashSet<string> Roles = new(StringComparer.Ordinal);
            public long DebutSort = long.MaxValue;
            public string DebutLabel = "";
            public string DebutUrl = "";
            public int? DebutSeriesId;
        }

        /// <summary>参加を 1 件足す。<paramref name="product"/> を渡すと <paramref name="unit"/> ではなくその盤の参加として数える。</summary>
        public void Add((char Kind, int Id) key, int unit, string roleCode, long sort, string label, string url, int? seriesId, string? product = null)
        {
            if (!ByEntity.TryGetValue(key, out var p))
            {
                p = new Participation();
                ByEntity[key] = p;
            }
            if (product is null) p.Units.Add(unit);
            else p.Products.Add(product);
            p.Roles.Add(roleCode);
            if (sort < p.DebutSort)
            {
                p.DebutSort = sort;
                p.DebutLabel = label;
                p.DebutUrl = url;
                p.DebutSeriesId = seriesId;
            }
        }
    }

    // 歌唱

    /// <summary>
    /// 歌唱者行の展開参加者が「メンバー名を出さないユニット名義の人物メンバー」としての参加かを判定する。
    /// PERSON 行でメンバー展開（<see cref="SongRecordingSinger.ExpandUnitMembers"/>）が立っておらず、
    /// 参加者が行の主名義・相方名義そのものではない人物（キャラ無し）のときに true。
    /// </summary>
    private static bool IsHiddenUnitMember(SongRecordingSinger s, SingerParticipant p)
        => s.BillingKind == SingerBillingKind.Person
           && !s.ExpandUnitMembers
           && p.CharacterAliasId is null
           && p.PersonAliasId is int paid
           && paid != s.PersonAliasId
           && paid != s.SlashPersonAliasId;

    /// <summary>
    /// 歌唱ページに「歌手」として載せる人物の集合。本人名義（キャラ無し）で歌・台詞を 1 回でも担当した人で、
    /// メンバー名を出さないユニット名義（例：DarkSingers）のメンバーとしての参加は数えない。
    /// ここに入らない人の本人名義での参加（コーラスだけ・名前の出ないユニットのメンバーだけ）は、
    /// 見慣れない名前が歌手として並ばないよう歌唱ページに載せず、音楽制作ページの「歌（演奏）」に回す。
    /// </summary>
    private HashSet<int> LeadSingerPersons(
        IReadOnlyList<SongRecordingSinger> allSingers,
        IReadOnlyDictionary<int, int> personIdByAlias)
    {
        var lead = new HashSet<int>();
        foreach (var s in allSingers)
        {
            if (string.Equals(s.RoleCode, SongRecordingSingerRoles.Chorus, StringComparison.Ordinal)) continue;
            foreach (var p in _ctx.ExpandSingerParticipants(s))
            {
                if (p.CharacterAliasId is not null || IsHiddenUnitMember(s, p)) continue;
                if (p.PersonAliasId is int paid && personIdByAlias.TryGetValue(paid, out var pid)) lead.Add(pid);
            }
        }
        return lead;
    }

    /// <summary>
    /// <c>/creators/singers/</c> を 2 タブ（初参加順 / 参加曲数順）で書き出す。
    /// 歌・コーラス・台詞の別を問わず、録音の歌唱者行（song_recording_singers）をユニットのメンバーまで展開し
    /// （<see cref="BuildContextLookupExtensions.ExpandSingerParticipants(BuildContext, SongRecordingSinger)"/>）、
    /// 「歌手」（人物単位。本人名義での参加）と「キャラクター」（キャラ × 声優の組ごと）の行を 1 つのリストに並べる。
    /// 行には種別（data-entity-type = singer / character）を持たせ、タブの下の絞り込みで出し分ける。
    /// 歌手の行は <paramref name="leadSingers"/>（<see cref="LeadSingerPersons"/>）の人だけで、
    /// その人のコーラスや名前の出ないユニットでの参加も曲数・初参加に数える。参加曲数は song_id 単位で重複排除する。
    /// </summary>
    private void GenerateSingers(
        IReadOnlyList<SongRecordingSinger> allSingers,
        IReadOnlySet<int> leadSingers,
        IReadOnlyDictionary<int, int> personIdByAlias,
        IReadOnlyDictionary<int, Person> personById,
        IReadOnlyDictionary<int, Character> characterById,
        IReadOnlyDictionary<int, string> transformNameByCharacter,
        out int personCount)
    {
        var singerAcc = new SongParticipationAccumulator();   // 本人名義での参加（歌手の行）
        // (character_id, 声優 person_id) → (最初に参加した名義, 最小 recording_id, その曲, 曲集合)
        var charAcc = new Dictionary<(int CharId, int PersonId), (int FirstAliasId, int FirstRecId, int FirstSongId, HashSet<int> Songs)>();

        foreach (var s in allSingers.OrderBy(x => x.SongRecordingId).ThenBy(x => x.RoleCode, StringComparer.Ordinal).ThenBy(x => x.SingerSeq))
        {
            if (!_ctx.SongRecordingById.TryGetValue(s.SongRecordingId, out var rec)) continue;
            foreach (var p in _ctx.ExpandSingerParticipants(s))
            {
                if (p.PersonAliasId is not int paid || !personIdByAlias.TryGetValue(paid, out var pid)) continue;
                if (p.CharacterAliasId is not int caid)
                {
                    if (leadSingers.Contains(pid)) singerAcc.Add(pid, rec.SongId, s.SongRecordingId, s.RoleCode);
                    continue;
                }
                if (!_ctx.CharacterAliasById.TryGetValue(caid, out var ca)) continue;
                var key = (ca.CharacterId, pid);
                if (!charAcc.TryGetValue(key, out var cur))
                    cur = (caid, s.SongRecordingId, rec.SongId, new HashSet<int>());
                else if (s.SongRecordingId < cur.FirstRecId)
                    cur = (caid, s.SongRecordingId, rec.SongId, cur.Songs);
                cur.Songs.Add(rec.SongId);
                charAcc[key] = cur;
            }
        }

        var rows = new List<SingerListRow>();
        foreach (var r in BuildSongPersonRows(singerAcc, personById, roleNameByCode: null))
        {
            rows.Add(new SingerListRow
            {
                EntityKind = "singer",
                Name = r.PersonName,
                NameKana = r.PersonNameKana,
                Url = r.PersonUrl,
                SongCount = r.SongCount,
                DebutRecordingId = r.DebutRecordingId,
                DebutSeriesId = r.DebutSeriesId,
                DebutSongTitle = r.DebutSongTitle,
                DebutSongUrl = r.DebutSongUrl
            });
        }
        int characterCount = 0;
        foreach (var ((charId, pid), v) in charAcc)
        {
            if (!characterById.ContainsKey(charId) || !personById.TryGetValue(pid, out var person)) continue;
            _ctx.CharacterAliasById.TryGetValue(v.FirstAliasId, out var fa);
            characterCount++;
            rows.Add(new SingerListRow
            {
                EntityKind = "character",
                // 変身するキャラは「変身前 / 変身後」（例：美墨なぎさ / キュアブラック）、それ以外は最初に歌ったときの名義。
                Name = transformNameByCharacter.TryGetValue(charId, out var transformName) ? transformName : fa?.Name ?? "",
                NameKana = fa?.NameKana ?? "",
                Url = PathUtil.CharacterUrl(charId),
                VoiceName = _ctx.EntityUrls.PersonDisplayName(pid) ?? person.FullName,
                VoiceUrl = PathUtil.PersonUrl(pid),
                SongCount = v.Songs.Count,
                DebutRecordingId = v.FirstRecId,
                DebutSeriesId = _ctx.SongRecordingById.TryGetValue(v.FirstRecId, out var fr) ? fr.SeriesId : null,
                DebutSongTitle = _ctx.SongById.TryGetValue(v.FirstSongId, out var song) ? song.Title : "",
                DebutSongUrl = PathUtil.SongUrl(v.FirstSongId)
            });
        }
        // 歌手の行に載せた人物（キャラクターの行の声優は声の出演一覧の側に載る）。
        foreach (var pid in singerAcc.ByPerson.Keys.Where(personById.ContainsKey)) _lists.SingerPersons.Add(pid);

        // 人数は、歌手の行の人と、キャラクターの行の声優を合わせた人物の数。
        personCount = singerAcc.ByPerson.Keys.Where(personById.ContainsKey)
            .Concat(charAcc.Keys.Where(k => characterById.ContainsKey(k.CharId) && personById.ContainsKey(k.PersonId)).Select(k => k.PersonId))
            .Distinct()
            .Count();

        // 同じ録音で初参加した行は、歌手 → キャラクターの順に並べる。
        var debutRows = rows
            .OrderBy(r => r.DebutRecordingId)
            .ThenBy(r => r.EntityKind == "singer" ? 0 : 1)
            .ThenBy(r => r.NameKana, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        var countRows = rows
            .OrderByDescending(r => r.SongCount)
            .ThenBy(r => r.DebutRecordingId)
            .ThenBy(r => r.NameKana, StringComparer.Ordinal)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        var content = new SingersModel
        {
            DebutSections = BuildDebutSeriesSections(debutRows, r => r.DebutSeriesId),
            CountRows = countRows,
            CoverageLabel = MusicCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュア歌唱",
            MetaDescription = "プリキュアの主題歌・挿入歌・キャラクターソングを歌った人々を一覧。歌手とキャラクターを、初参加曲と参加曲数から探せます。",
            OgCard = BuildCreatorsOgCard(
                "歴代プリキュア歌唱",
                new[]
                {
                    new OgCardBadge("人物", $"{personCount}人"),
                    new OgCardBadge("キャラクター", $"{characterCount}組")
                },
                new[] { new OgCardFactLine("集計元", "楽曲のクレジット（劇中歌・キャラクターソングを含む）") }, MusicCoverageLabel),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュア歌唱", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsSingersUrl(), "creators",
            "creators-singers.sbn", content, layout);
    }

    /// <summary>
    /// 音楽系ページ（音楽制作・歌唱・作詞作曲編曲や音楽の役職詳細）の基準点ラベル。クレジット確認済みの盤のうち
    /// 最新のものの発売日と商品名で示す。確認済みの盤が無いときは本編クレジットの収録範囲に戻す。
    /// </summary>
    private string MusicCoverageLabel => string.IsNullOrEmpty(_ctx.MusicCredits.CoverageLabel)
        ? _ctx.CreditCoverageLabel
        : _ctx.MusicCredits.CoverageLabel;

    /// <summary>「シリーズ名（年）」の見出し（スタッフ一覧・声の出演一覧の初参加順セクションと同じ書式）。</summary>
    private string SeriesHeadingLabel(int seriesId)
    {
        var series = _ctx.SeriesById[seriesId];
        return $"{series.Title}（{series.StartDate.Year}）";
    }

    /// <summary>
    /// 初参加順の行を、初参加シリーズごとのセクション（「シリーズ名（年）」見出し、シリーズ詳細へのリンク）にまとめる。
    /// セクションはシリーズの放送開始日順、セクション内は渡された行の順。シリーズが分からない行は末尾の「その他」にまとめる。
    /// </summary>
    private List<DebutSeriesSection> BuildDebutSeriesSections<T>(IReadOnlyList<T> orderedRows, Func<T, int?> seriesIdOf) where T : class
        => orderedRows
            .Select((row, index) => (Row: row, Index: index, SeriesId: seriesIdOf(row) is int sid && _ctx.SeriesById.ContainsKey(sid) ? sid : (int?)null))
            .GroupBy(x => x.SeriesId)
            .OrderBy(g => g.Key is int sid ? _ctx.SeriesStartDate(sid).DayNumber : int.MaxValue)
            .ThenBy(g => g.Min(x => x.Index))
            .Select(g => new DebutSeriesSection
            {
                SeriesHeadingLabel = g.Key is int sid ? SeriesHeadingLabel(sid) : "その他",
                SeriesUrl = g.Key is int sid2 ? PathUtil.SeriesUrl(_ctx.SeriesById[sid2].Slug) : "",
                Members = g.OrderBy(x => x.Index).Select(x => (object)x.Row).ToList()
            })
            .ToList();

    /// <summary>初参加順のシリーズ別セクション 1 つ分（音楽制作・歌唱ページ）。</summary>
    private sealed class DebutSeriesSection
    {
        public string SeriesHeadingLabel { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public IReadOnlyList<object> Members { get; set; } = Array.Empty<object>();
    }

    /// <summary>曲ごとの最小 recording_id（録音 ID は初出順なので「その曲の初出」の代理）。</summary>
    private Dictionary<int, int> MinRecordingIdBySong()
    {
        var map = new Dictionary<int, int>();
        foreach (var rec in _ctx.SongRecordingById.Values)
        {
            if (!map.TryGetValue(rec.SongId, out var cur) || rec.SongRecordingId < cur)
                map[rec.SongId] = rec.SongRecordingId;
        }
        return map;
    }

    /// <summary>人物ごとの楽曲参加（曲集合・最小 recording_id・初参加曲・関わった役職）を集める。</summary>
    private sealed class SongParticipationAccumulator
    {
        public readonly Dictionary<int, (HashSet<int> Songs, int DebutRecId, int DebutSongId, HashSet<string> Roles)> ByPerson = new();

        public void Add(int personId, int songId, int recordingId, string roleCode)
        {
            if (!ByPerson.TryGetValue(personId, out var cur))
                cur = (new HashSet<int>(), int.MaxValue, 0, new HashSet<string>(StringComparer.Ordinal));
            cur.Songs.Add(songId);
            cur.Roles.Add(roleCode);
            if (recordingId < cur.DebutRecId) cur = (cur.Songs, recordingId, songId, cur.Roles);
            ByPerson[personId] = cur;
        }
    }

    /// <summary>
    /// 参加の集計から人物単位の行を作る。表記は人物詳細と同じ表示名義。初参加曲（曲詳細へのリンク）を添える。
    /// <paramref name="roleNameByCode"/> を渡すと、関わった役職名（作詞・作曲・編曲の順）も添える。
    /// </summary>
    private List<SongRoleRow> BuildSongPersonRows(
        SongParticipationAccumulator acc,
        IReadOnlyDictionary<int, Person> personById,
        IReadOnlyDictionary<string, string>? roleNameByCode)
    {
        var rows = new List<SongRoleRow>(acc.ByPerson.Count);
        foreach (var (pid, v) in acc.ByPerson)
        {
            if (!personById.TryGetValue(pid, out var p)) continue;
            string rolesLabel = roleNameByCode is null ? "" : string.Join("・",
                SongCreditRoleOrder.Where(v.Roles.Contains)
                    .Select(c => roleNameByCode.TryGetValue(c, out var n) ? n : c));
            rows.Add(new SongRoleRow
            {
                PersonId = pid,
                PersonName = _ctx.EntityUrls.PersonDisplayName(pid) ?? p.FullName,
                PersonNameKana = _ctx.EntityUrls.PersonDisplayKana(pid) ?? (p.FullNameKana ?? ""),
                PersonUrl = PathUtil.PersonUrl(pid),
                SongCount = v.Songs.Count,
                DebutRecordingId = v.DebutRecId,
                DebutSeriesId = _ctx.SongRecordingById.TryGetValue(v.DebutRecId, out var debutRec) ? debutRec.SeriesId : null,
                DebutSongTitle = _ctx.SongById.TryGetValue(v.DebutSongId, out var song) ? song.Title : "",
                DebutSongUrl = v.DebutSongId > 0 ? PathUtil.SongUrl(v.DebutSongId) : "",
                RolesLabel = rolesLabel
            });
        }
        return rows;
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
        IReadOnlyDictionary<int, CompanyAlias> companyAliasById,
        IReadOnlyDictionary<int, Company> companyById,
        IReadOnlyList<Role> rankableRoles,
        IReadOnlyDictionary<string, Role> roleByCode,
        out int staffPersonCount,
        out int staffCompanyCount)
    {
        // 内訳・役職ラベルに使う「代表 role_code → 代表 NameJa」マップ。
        var repNameMap = rankableRoles.ToDictionary(r => r.RoleCode, r => r.NameJa, StringComparer.Ordinal);

        // 全 non-VOICE_CAST 役職を横断し、エピソード単位で重複排除（複数役職の兼任も 1 回扱い）。
        // 役職ラベルは代表 role_code で束ね、最早出現順に全列挙する。
        // 主題歌・挿入歌の使用を経由した関与（楽曲の作家・歌唱）は数えない。本編クレジットに載ったスタッフだけの一覧にし、
        // 楽曲の作家・歌唱は音楽制作・歌唱ページに分ける。
        var rowSet = BuildEntityRowSet(
            inv =>
            {
                if (inv.EntryKind is "SONG_CREDIT" or "RECORDING_SINGER") return null;
                string rep = _resolver.GetRepresentative(inv.RoleCode);
                return repNameMap.ContainsKey(rep) ? rep : null; // VOICE_CAST 等は対象外
            },
            aliasIdsByPersonId, allPersons, companyAliasesByCompany, logosByCompanyAlias,
            allCompanies, companyAliasById, repNameMap, withWorksTooltip: false);
        var rows = rowSet.CountRows;
        foreach (var r in rows)
        {
            if (string.Equals(r.EntityKind, "person", StringComparison.Ordinal)) _lists.StaffPersons.Add(r.EntityId);
            else _lists.StaffCompanies.Add(r.EntityId);
        }

        staffPersonCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal));
        staffCompanyCount = rows.Count - staffPersonCount;

        var content = new StaffModel
        {
            Roles = roleIndexEntries,
            TvRoles = roleIndexEntries.Where(e => !e.IsMovieOnly).ToList(),
            MovieOnlyRoles = roleIndexEntries.Where(e => e.IsMovieOnly).ToList(),
            TotalRoles = roleIndexEntries.Count,
            // 五十音順タブは読み（kana）データ未整備のため一旦無効化。テンプレ側もコメントアウト済み。
            // データが揃ったら下行のコメントを外して復活させる（KanaRows は未設定＝空のまま）。
            // KanaRows = SortByKana(rows),
            // 初参加順は名義ごとの行（改名・屋号変更ごとに、その名義が初めて出たシリーズへ置く）。
            DebutSections = SectionByDebut(rowSet.DebutRows),
            CountRows = SortByCount(rows),
            PersonCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal)),
            CompanyCount = rows.Count(r => string.Equals(r.EntityKind, "company", StringComparison.Ordinal)),
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュアスタッフ",
            MetaDescription = "プリキュアを支えたスタッフ（人物・企業・団体）を一覧。役職や参加話数で並べ替えて、「あの人はどの作品に関わった？」をたどれます。",
            OgCard = BuildCreatorsOgCard(
                "歴代プリキュアスタッフ",
                BuildEntityBadges(content.PersonCount, content.CompanyCount)
                    .Append(new OgCardBadge("役職", $"{roleIndexEntries.Count}種")).ToArray(),
                Array.Empty<OgCardFactLine>()),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsStaffUrl(), "creators",
            "creators-staff.sbn", content, layout);
    }

    // 声の出演

    /// <summary>
    /// <c>/creators/voice-cast/</c> を 4 タブ（五十音順 / キャラクター順 /
    /// 初出演順 / 出演回数順）で書き出す。
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
        var aliasById = allCharacterAliases.ToDictionary(a => a.AliasId);
        var aliasToCharId = new Dictionary<int, int>();
        foreach (var a in allCharacterAliases) aliasToCharId[a.AliasId] = a.CharacterId;

        var repAliasNameByChar = BuildVoiceCastRepAliasNames(
            aliasIdsByPersonId, allPersons, aliasToCharId, aliasById);

        var (rows, debutRows, countAggRows, distinctPersons) = BuildVoiceCastRows(
            aliasIdsByPersonId, allPersons, aliasToCharId, characterById, repAliasNameByChar);

        // ランディングカードの «N 名» は声優の実人数（行数ではない）。
        voiceCastCount = distinctPersons.Count;

        // 五十音順タブは読み（kana）データ未整備のため一旦無効化。テンプレ側もコメントアウト済み。
        // データが揃ったら下の kanaRows 構築と VoiceCastModel.KanaRows 代入のコメントを外して復活させる。
        // 五十音順（既定タブ）：声優の読み → 名前 → シリーズ放送開始 → キャラ読み。
        // 五十音順はルールが完全に一意なのでクレジット位置キーは挟まない。
        // 表にシリーズ列は出さない方針（行は声優・キャラ・出演話数のみ）。
        // var kanaRows = rows
        //     .OrderBy(r => string.IsNullOrEmpty(r.PersonNameKana) ? 1 : 0)
        //     .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
        //     .ThenBy(r => r.PersonName, StringComparer.Ordinal)
        //     .ThenBy(r => r.SeriesSortStart)
        //     .ThenBy(r => r.CharacterNameKana, StringComparer.Ordinal)
        //     .ThenBy(r => r.CharacterName, StringComparer.Ordinal)
        //     .ToList();

        var charSections = BuildCharacterSections(rows);

        // 初出演順（シリーズセクション）：声優 1 人につき「初めて参加したシリーズ」のセクションに
        // 1 回だけ載せる（debutRows は人単位の 1 行に集約済み）。行の添え書きは初出演時のキャラ、
        // 話数は全シリーズ・全キャラ通算（重複排除）。
        var debutSections = debutRows
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
                    .OrderBy(r => r.EarliestEpNo == 0 ? int.MaxValue : r.EarliestEpNo)
                    .ThenBy(r => r.EarliestPos)
                    .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
                    .ThenBy(r => r.CharacterNameKana, StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();

        // 出演回数順（セクション無し）：声優 1 人 = 1 行（countAggRows に集約済み）。
        // 話数は全シリーズ・全キャラ通算（重複排除）。添え書きは代表キャラ（クレジット話数最多）で、
        // 他のキャラもあるときはテンプレ側で「他」が付く。
        // 並びは話数降順 → 初登場シリーズ → 最早話数 → クレジット出現位置 → 声優読み。
        var countRows = countAggRows
            .OrderByDescending(r => r.EpisodeCount)
            .ThenBy(r => r.SeriesSortStart)
            .ThenBy(r => r.EarliestEpNo)
            .ThenBy(r => r.EarliestPos)
            .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
            .ThenBy(r => r.PersonName, StringComparer.Ordinal)
            .ToList();
        foreach (var r in countRows) _lists.VoiceCastPersons.Add(r.PersonId);

        var content = new VoiceCastModel
        {
            CharacterSections = charSections,
            // 五十音順タブは一旦無効化（上の kanaRows 構築コメントと対）。復活時はこのコメントを外す。
            // KanaRows = kanaRows,
            DebutSections = debutSections,
            CountRows = countRows,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュア声優",
            MetaDescription = "プリキュアのキャラクターを演じた声優を一覧。キャラクター・初出演・出演話数で並べ替えて、「このキャラの声は誰？」がすぐわかります。",
            OgCard = BuildCreatorsOgCard(
                "歴代プリキュア声優",
                new[]
                {
                    new OgCardBadge("声優", $"{countRows.Count}人"),
                    new OgCardBadge("シリーズ", $"{charSections.Count}作品")
                },
                Array.Empty<OgCardFactLine>()),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュア声優", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsVoiceCastUrl(), "creators",
            "creators-voice-cast.sbn", content, layout);
    }

    /// <summary>各キャラクターの役名の代表名義（最も多くクレジットされた character_alias 名）を求める。</summary>
    private Dictionary<int, string> BuildVoiceCastRepAliasNames(
        IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
        IReadOnlyList<Person> allPersons,
        IReadOnlyDictionary<int, int> aliasToCharId,
        IReadOnlyDictionary<int, CharacterAlias> aliasById)
    {
        // 役名の代表名義：各キャラクターについて「最も多くクレジットされた character_alias」を代表名義とする。
        // 表示はキャラの正式名（master の Name）ではなく、この代表名義を使う（同姓同名でなく、同一キャラの
        // 表記揺れ＝別名義のうち最頻のものを役名として出す）。多寡は distinct な (シリーズ, 話数) 数で測り、
        // 同数なら alias_id の小さい方（登録が早い方）を採る。
        var charAliasKeys = new Dictionary<int, Dictionary<int, HashSet<(int SeriesId, int EpNo)>>>();
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var pAliasIds)) continue;
            foreach (var aid in pAliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    // 声の出演のクレジットだけを数える（キャラ名義での主題歌・挿入歌の歌唱は除く）。
                    if (!inv.IsVoiceCast) continue;
                    if (inv.CharacterAliasId is not int caId) continue;
                    if (!aliasToCharId.TryGetValue(caId, out var charId)) continue;
                    int epNo = 0;
                    if (inv.EpisodeId is int eid)
                    {
                        var ep = _ctx.LookupEpisode(inv.SeriesId, eid);
                        if (ep is not null) epNo = ep.SeriesEpNo;
                    }
                    if (!charAliasKeys.TryGetValue(charId, out var perAlias))
                    {
                        perAlias = new Dictionary<int, HashSet<(int, int)>>();
                        charAliasKeys[charId] = perAlias;
                    }
                    if (!perAlias.TryGetValue(caId, out var keys))
                    {
                        keys = new HashSet<(int, int)>();
                        perAlias[caId] = keys;
                    }
                    keys.Add((inv.SeriesId, epNo));
                }
            }
        }
        var repAliasNameByChar = new Dictionary<int, string>();
        foreach (var (charId, perAlias) in charAliasKeys)
        {
            int bestAlias = -1, bestCount = -1;
            foreach (var (caId, keys) in perAlias)
            {
                int c = keys.Count;
                if (c > bestCount || (c == bestCount && (bestAlias < 0 || caId < bestAlias)))
                {
                    bestCount = c;
                    bestAlias = caId;
                }
            }
            if (bestAlias >= 0 && aliasById.TryGetValue(bestAlias, out var ba))
                repAliasNameByChar[charId] = ba.Name;
        }
        return repAliasNameByChar;
    }

    /// <summary>声優ごとの (シリーズ × キャラ) 行群と、初出演順・出演回数順タブ用の人単位集約行を構築する。</summary>
    private (List<VoiceCastRow> Rows, List<VoiceCastRow> DebutRows, List<VoiceCastRow> CountAggRows, HashSet<int> DistinctPersons)
        BuildVoiceCastRows(
            IReadOnlyDictionary<int, IReadOnlyList<int>> aliasIdsByPersonId,
            IReadOnlyList<Person> allPersons,
            IReadOnlyDictionary<int, int> aliasToCharId,
            IReadOnlyDictionary<int, Character> characterById,
            IReadOnlyDictionary<int, string> repAliasNameByChar)
    {
        var rows = new List<VoiceCastRow>();
        // 初出演順タブ用：声優 1 人 = 1 行（初参加シリーズのセクションにのみ載せる）。
        // 話数は全シリーズ・全キャラ通算の重複排除エピソード数。
        var debutRows = new List<VoiceCastRow>();
        // 出演回数順タブ用：声優 1 人 = 1 行。代表キャラ＝クレジット話数が最も多いキャラ
        //（同数なら初登場が早い方）。他のキャラもあるときはテンプレ側で「他」を付ける。
        var countAggRows = new List<VoiceCastRow>();
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
                    // 声の出演のクレジットだけを数える（キャラ名義での主題歌・挿入歌の歌唱は除く）。
                    if (!inv.IsVoiceCast) continue;
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
                            long pos = inv.CreditPos;
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

            var personRows = new List<VoiceCastRow>();
            var personEpisodeKeys = new HashSet<(int SeriesId, int EpNo)>();
            // 人単位の映画本数（映画系シリーズの distinct 数）。初出演順・出演話数タブの 🎥 ピルに使う。
            var personMovieSeries = new HashSet<int>();

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
                // 映画系シリーズ（series_kinds.credit_attach_to='SERIES'）は 1 シリーズ = 1 本として数える。
                bool isMovie = _ctx.IsMovieKindSeries(seriesId);

                var row = new VoiceCastRow
                {
                    // 人物詳細の見出し・URL と同じ表示名義で出す（クレジットの無い人物は正式名）。
                    PersonName = _ctx.EntityUrls.PersonDisplayName(p.PersonId) ?? p.FullName,
                    PersonNameKana = _ctx.EntityUrls.PersonDisplayKana(p.PersonId) ?? (p.FullNameKana ?? ""),
                    PersonUrl = PathUtil.PersonUrl(p.PersonId),
                    PersonId = p.PersonId,
                    SeriesTitle = series.Title,
                    SeriesUrl = PathUtil.SeriesUrl(series.Slug),
                    SeriesYearLabel = series.StartDate.Year.ToString(),
                    SeriesSortStart = series.StartDate.DayNumber,
                    SeriesId = seriesId,
                    // 役名は代表名義（最頻 alias）。未集計のキャラだけ master の Name にフォールバック。
                    CharacterName = repAliasNameByChar.TryGetValue(charId, out var repName) ? repName : ch.Name,
                    CharacterNameKana = ch.NameKana ?? "",
                    CharacterUrl = PathUtil.CharacterUrl(ch.CharacterId),
                    CharacterId = ch.CharacterId,
                    EpisodeCount = epNos.Count,
                    MovieCount = isMovie ? 1 : 0,
                    EarliestEpNo = earliestEpNo,
                    EarliestPos = earliestPos
                };
                rows.Add(row);
                personRows.Add(row);
                foreach (var no in epNos) personEpisodeKeys.Add((seriesId, no));
                if (isMovie) personMovieSeries.Add(seriesId);
                distinctPersons.Add(p.PersonId);
            }

            AppendPersonDebutAndCountRows(personRows, personEpisodeKeys, personMovieSeries,
                debutRows, countAggRows);
        }

        return (rows, debutRows, countAggRows, distinctPersons);
    }

    /// <summary>声優 1 人分の「初出演順」集約 1 行と「出演回数順」集約 1 行を追加する。</summary>
    private static void AppendPersonDebutAndCountRows(
        List<VoiceCastRow> personRows,
        HashSet<(int SeriesId, int EpNo)> personEpisodeKeys,
        HashSet<int> personMovieSeries,
        List<VoiceCastRow> debutRows,
        List<VoiceCastRow> countAggRows)
    {
        // 初出演順タブ用の 1 行：この声優が最初に参加したシリーズ・キャラの行をベースに、
        // 話数だけを全シリーズ・全キャラ通算（重複排除）へ差し替えたコピーを作る。
        // 「初出演順に載るべきはその声優が初めて参加したシリーズ」のため、人単位で 1 回だけ載せる。
        if (personRows.Count > 0)
        {
            var debutSource = personRows
                .OrderBy(r => r.SeriesSortStart)
                .ThenBy(r => r.EarliestEpNo == 0 ? int.MaxValue : r.EarliestEpNo)
                .ThenBy(r => r.EarliestPos)
                .First();
            debutRows.Add(new VoiceCastRow
            {
                PersonName = debutSource.PersonName,
                PersonNameKana = debutSource.PersonNameKana,
                PersonUrl = debutSource.PersonUrl,
                SeriesTitle = debutSource.SeriesTitle,
                SeriesUrl = debutSource.SeriesUrl,
                SeriesYearLabel = debutSource.SeriesYearLabel,
                SeriesSortStart = debutSource.SeriesSortStart,
                CharacterName = debutSource.CharacterName,
                CharacterNameKana = debutSource.CharacterNameKana,
                CharacterUrl = debutSource.CharacterUrl,
                CharacterId = debutSource.CharacterId,
                EpisodeCount = personEpisodeKeys.Count,
                MovieCount = personMovieSeries.Count,
                EarliestEpNo = debutSource.EarliestEpNo,
                EarliestPos = debutSource.EarliestPos
            });

            // 出演回数順タブ用の 1 行：声優単位の通算（重複排除）話数。
            // 代表キャラ＝クレジット話数が最も多いキャラ（同数なら初登場が早い方）。
            var byChar = personRows
                .GroupBy(r => r.CharacterId)
                .Select(cg => new
                {
                    Total = cg.Sum(r => r.EpisodeCount),
                    First = cg
                        .OrderBy(r => r.SeriesSortStart)
                        .ThenBy(r => r.EarliestEpNo == 0 ? int.MaxValue : r.EarliestEpNo)
                        .ThenBy(r => r.EarliestPos)
                        .First()
                })
                .OrderByDescending(c => c.Total)
                .ThenBy(c => c.First.SeriesSortStart)
                .ThenBy(c => c.First.EarliestEpNo == 0 ? int.MaxValue : c.First.EarliestEpNo)
                .ThenBy(c => c.First.EarliestPos)
                .ToList();
            var rep = byChar[0].First;
            countAggRows.Add(new VoiceCastRow
            {
                PersonId = rep.PersonId,
                PersonName = rep.PersonName,
                PersonNameKana = rep.PersonNameKana,
                PersonUrl = rep.PersonUrl,
                SeriesTitle = rep.SeriesTitle,
                SeriesUrl = rep.SeriesUrl,
                SeriesYearLabel = rep.SeriesYearLabel,
                SeriesSortStart = rep.SeriesSortStart,
                CharacterName = rep.CharacterName,
                CharacterNameKana = rep.CharacterNameKana,
                CharacterUrl = rep.CharacterUrl,
                CharacterId = rep.CharacterId,
                EpisodeCount = personEpisodeKeys.Count,
                MovieCount = personMovieSeries.Count,
                EarliestEpNo = rep.EarliestEpNo,
                EarliestPos = rep.EarliestPos,
                HasOtherCharacters = byChar.Count > 1
            });
        }
    }

    /// <summary>キャラクター順タブ：キャラクター 1 体 = 1 行に集約し、初出シリーズごとのセクションに束ねる。</summary>
    private static List<VoiceSeriesSection> BuildCharacterSections(List<VoiceCastRow> rows)
    {
        // キャラクター別（既定タブ・シリーズセクション）：キャラクター 1 体 = 1 行。
        // 各キャラは「最初にクレジットされたシリーズ」のセクションに 1 回だけ載せる
        // （映画などで再登場しても重複表示しない）。役名は代表名義（rows.CharacterName に
        // 格納済みの最頻 alias）。CV はそのキャラを最も多く演じた声優を代表とし、他の声優も
        // 居れば「他」を付ける。担当数はキャラ通算で TV 話（📺）と映画 本（🎥）を併記する。
        var perCharRows = rows
            .GroupBy(r => r.CharacterId)
            .Select(cg =>
            {
                var charRows = cg.ToList();
                // 代表 CV：そのキャラでの担当量（話＋本）が最多の声優。同数は初出が早い方。
                var byPerson = charRows
                    .GroupBy(r => r.PersonId)
                    .Select(pg => new
                    {
                        Rep = pg.OrderBy(r => r.SeriesSortStart)
                                .ThenBy(r => r.EarliestEpNo == 0 ? int.MaxValue : r.EarliestEpNo)
                                .ThenBy(r => r.EarliestPos)
                                .First(),
                        Weight = pg.Sum(r => r.EpisodeCount + r.MovieCount)
                    })
                    .OrderByDescending(x => x.Weight)
                    .ThenBy(x => x.Rep.SeriesSortStart)
                    .ThenBy(x => x.Rep.EarliestEpNo == 0 ? int.MaxValue : x.Rep.EarliestEpNo)
                    .ThenBy(x => x.Rep.EarliestPos)
                    .ToList();
                var repPerson = byPerson[0].Rep;
                // キャラの初出（セクション配置・並び順の基準）。
                var debut = charRows
                    .OrderBy(r => r.SeriesSortStart)
                    .ThenBy(r => r.EarliestEpNo == 0 ? int.MaxValue : r.EarliestEpNo)
                    .ThenBy(r => r.EarliestPos)
                    .First();
                int tvTotal = charRows.Sum(r => r.EpisodeCount);
                int movieTotal = charRows.Where(r => r.MovieCount > 0)
                    .Select(r => r.SeriesId).Distinct().Count();
                return new VoiceCastRow
                {
                    PersonName = repPerson.PersonName,
                    PersonUrl = repPerson.PersonUrl,
                    PersonId = repPerson.PersonId,
                    SeriesTitle = debut.SeriesTitle,
                    SeriesUrl = debut.SeriesUrl,
                    SeriesYearLabel = debut.SeriesYearLabel,
                    SeriesSortStart = debut.SeriesSortStart,
                    CharacterName = debut.CharacterName,
                    CharacterUrl = debut.CharacterUrl,
                    CharacterId = debut.CharacterId,
                    EpisodeCount = tvTotal,
                    MovieCount = movieTotal,
                    EarliestEpNo = debut.EarliestEpNo,
                    EarliestPos = debut.EarliestPos,
                    HasOtherPersons = byPerson.Count > 1
                };
            })
            .ToList();

        var charSections = perCharRows
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
                // キャラの並び＝そのキャラが最初にクレジットされた位置（クレジット出現順）。
                Members = g
                    .OrderBy(r => r.EarliestEpNo == 0 ? int.MaxValue : r.EarliestEpNo)
                    .ThenBy(r => r.EarliestPos)
                    .ThenBy(r => r.CharacterName, StringComparer.Ordinal)
                    .ToList()
            })
            .ToList();
        return charSections;
    }

    // ランディング

    /// <summary><c>/creators/</c> ランディング。スタッフ / 声の出演 / 音楽制作 / 歌唱 の 4 カードを案内する （音楽カテゴリランディング <c>/music/</c> と同型の意匠）。</summary>
    private void GenerateLanding(int staffPersonCount, int staffCompanyCount, int voiceCastCount,
        int musicProductionPersonCount, int musicProductionCompanyCount, int singerCount)
    {
        var content = new LandingModel
        {
            StaffPersonCount = staffPersonCount,
            StaffCompanyCount = staffCompanyCount,
            VoiceCastCount = voiceCastCount,
            MusicProductionPersonCount = musicProductionPersonCount,
            MusicProductionCompanyCount = musicProductionCompanyCount,
            SingerCount = singerCount
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代クリエイター",
            MetaDescription = "脚本・演出・作画から制作会社まで、プリキュアを作り上げたスタッフと、キャラクターを演じた声優、楽曲を作り歌った人々。作品の「裏側」を担った作り手をたどれます。",
            OgCard = BuildCreatorsOgCard(
                "歴代クリエイター",
                new[]
                {
                    new OgCardBadge("スタッフ", $"{staffPersonCount}名・{staffCompanyCount}団体"),
                    new OgCardBadge("声優", $"{voiceCastCount}人"),
                    new OgCardBadge("音楽制作", $"{musicProductionPersonCount}名・{musicProductionCompanyCount}団体"),
                    new OgCardBadge("歌唱", $"{singerCount}人")
                },
                Array.Empty<OgCardFactLine>()),
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエイター", Url = "" }
            }
        };
        _page.RenderAndWrite(PathUtil.CreatorsLandingUrl(), "creators",
            "creators-landing.sbn", content, layout);
    }

    // 共有ヘルパ

    /// <summary>
    /// クリエイター系一覧ページ共通の OGP カード。
    /// これらのページが出す数はすべてクレジット登録済みの範囲での集計なので、
    /// 数の直下に必ず基準点（クレジット収録範囲）を添える。
    /// 母数を書かずに数だけ流すと「歴代の全数」と読まれてしまうため、カード単体で完結させる。
    /// </summary>
    /// <param name="coverageLabel">基準点ラベル。null なら本編クレジットの収録範囲（<see cref="BuildContext.CreditCoverageLabel"/>）。</param>
    private OgCardSpec BuildCreatorsOgCard(string title, IReadOnlyList<OgCardBadge> badges, IReadOnlyList<OgCardFactLine> facts, string? coverageLabel = null) =>
        new(Kicker: "", Title: title)
        {
            MetaLeft = OgCoverageLabel.Compact(coverageLabel ?? _ctx.CreditCoverageLabel),
            Badges = badges,
            Facts = facts
        };

    /// <summary>人物・企業の内訳バッジ。片方しか居ない役職では 0 のバッジを出さない。</summary>
    private static OgCardBadge[] BuildEntityBadges(int personCount, int companyCount)
    {
        var badges = new List<OgCardBadge>();
        if (personCount > 0) badges.Add(new OgCardBadge("人物", $"{personCount}人"));
        if (companyCount > 0) badges.Add(new OgCardBadge("企業・団体", $"{companyCount}組"));
        return badges.ToArray();
    }

    /// <summary>エンティティ 1 行分を組み立てる。初参加ソート用に <see cref="FirstCreditAccumulator"/> から最早シリーズ情報を移す。 担当量は TV 系（話）と映画系（本）を分けて持つ（テンプレ側で「N 話・M 本」併記）。</summary>
    private static EntityRow MakeEntityRow(
        string entityKind, int entityId, string name, string nameKana,
        string url, int episodeCount, int movieCount, int seriesCount, FirstCreditAccumulator first,
        string worksTooltip = "")
    {
        return new EntityRow
        {
            EntityKind = entityKind,
            EntityId = entityId,
            EntityName = name,
            EntityNameKana = nameKana,
            EntityUrl = url,
            EpisodeCount = episodeCount,
            MovieCount = movieCount,
            SeriesCount = seriesCount,
            WorksTooltip = worksTooltip,
            FirstSeriesTitle = first.SeriesTitle,
            FirstSeriesUrl = first.SeriesUrl,
            FirstSeriesYearLabel = first.SeriesYearLabel,
            FirstSortStart = first.SortStartTicks,
            FirstSortEpNo = first.SortEpNo,
            FirstSortPos = first.SortCreditPos
        };
    }

    /// <summary>
    /// 役職詳細ページの行リンク tooltip 用に、エンティティが当該役職で担当した作品一覧を
    /// 「シリーズ名（N話）／映画 …（映画）」形式で放送開始日順に組み立てる。
    /// TV 系シリーズは <paramref name="episodeKeys"/>（(seriesId, episodeId) の重複排除集合）から
    /// シリーズごとの担当話数を数え、映画系シリーズは <paramref name="movieSeriesIds"/> を「映画」表記で並べる。
    /// シリーズ名は略称（series.title_short）を使わず常にフルタイトル。区切りは全角「／」。
    /// </summary>
    private string BuildWorksTooltip(
        IReadOnlySet<(int seriesId, int episodeId)> episodeKeys,
        IReadOnlySet<int> movieSeriesIds)
    {
        // TV 系：シリーズ ID ごとに担当話数（distinct episode 数）を集計。
        var tvCountBySeries = new Dictionary<int, int>();
        foreach (var (sid, _) in episodeKeys)
        {
            tvCountBySeries.TryGetValue(sid, out int n);
            tvCountBySeries[sid] = n + 1;
        }

        // (seriesId, ラベル) のリストを作り、放送開始日 → シリーズ名で安定ソート。
        var works = new List<(long sortStart, string title, string label)>();
        foreach (var kv in tvCountBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(kv.Key, out var s)) continue;
            works.Add((_ctx.SeriesStartDate(kv.Key).DayNumber, s.Title, $"{s.Title}（{kv.Value}話）"));
        }
        foreach (var sid in movieSeriesIds)
        {
            if (!_ctx.SeriesById.TryGetValue(sid, out var s)) continue;
            works.Add((_ctx.SeriesStartDate(sid).DayNumber, s.Title, $"{s.Title}（映画）"));
        }

        if (works.Count == 0) return "";
        return string.Join("／", works
            .OrderBy(w => w.sortStart)
            .ThenBy(w => w.title, StringComparer.Ordinal)
            .Select(w => w.label));
    }

    /// <summary>代表 role_code ごとに、その役職で最も早い (Start, EpNo, CreditSeq) を更新する。 同じ話数内で同点のときはクレジット出現位置が早い役職を上位に扱う。</summary>
    private void OfferEarliestRole(
        Dictionary<string, (DateOnly Start, int EpNo, long Pos)> earliestByRep, string rep, Involvement inv)
    {
        var start = _ctx.SeriesStartDate(inv.SeriesId);
        int epNo = inv.EpisodeId is int eid
            ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
            : 0; // シリーズスコープは最早扱い
        long pos = inv.CreditPos;
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

    /// <summary>担当回数順：担当量降順（TV 話 + 映画本の単純合算 <see cref="EntityRow.TotalCount"/>） → 最早クレジット (放送開始, 話数, クレジット出現位置) → 読み → 名前（順位は付けない）。 五十音順以外（並びのルールが完全には一意に決まらないタブ）では、クレジット 出現位置を暗黙の副ソートキーとして効かせ、同点行の並びを安定させる方針。 ここでは担当量が同数の行を、初出が早い順 → そのエピソード内のクレジット 記載位置順に整える。</summary>
    private static List<EntityRow> SortByCount(IEnumerable<EntityRow> rows) => rows
        .OrderByDescending(r => r.TotalCount)
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
            long pos = inv.CreditPos;
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

    /// <summary>
    /// 人物 1 人 / 企業 1 社の全名義にまたがる関与を畳み込む集計器。
    /// 担当量（TV 話数・映画本数・シリーズ数）と最早関与はエンティティ全体で、
    /// 最早関与はあわせて名義ごとにも（<see cref="FirstByAlias"/>）保持する。
    /// </summary>
    private sealed class EntityAggregate
    {
        private readonly CreatorsGenerator _owner;
        private readonly Dictionary<int, FirstCreditAccumulator> _firstByAlias = new();
        private readonly List<int> _aliasOrder = new();

        public EntityAggregate(CreatorsGenerator owner)
        {
            _owner = owner;
            First = new FirstCreditAccumulator(owner._ctx);
        }

        /// <summary>TV 系シリーズの参加（(seriesId, episodeId) で重複排除 → 話数）。</summary>
        public HashSet<(int seriesId, int episodeId)> EpisodeKeys { get; } = new();

        /// <summary>映画系シリーズの参加（seriesId で重複排除 → 本数、1 シリーズ = 1 本）。</summary>
        public HashSet<int> MovieSeriesIds { get; } = new();

        public HashSet<int> SeriesIds { get; } = new();

        /// <summary>エンティティ全体の最早関与。</summary>
        public FirstCreditAccumulator First { get; }

        /// <summary>役職ラベル用：代表 role_code → その役職で最も早い (Start, EpNo, Pos)。</summary>
        public Dictionary<string, (DateOnly Start, int EpNo, long Pos)> EarliestByRep { get; } = new(StringComparer.Ordinal);

        /// <summary>クレジットされた表記（系譜でまとめる前の role_code）ごとの担当数と最早位置。役職詳細の行の添え書き用。</summary>
        public Dictionary<string, RoleCodeUsage> UsageByRoleCode { get; } = new(StringComparer.Ordinal);

        /// <summary>集計対象の関与を 1 件でも持った名義ごとの最早関与（名義を初めて受け取った順）。</summary>
        public IEnumerable<(int AliasId, FirstCreditAccumulator First)> FirstByAlias
            => _aliasOrder.Select(aid => (aid, _firstByAlias[aid]));

        public bool IsEmpty => EpisodeKeys.Count == 0 && MovieSeriesIds.Count == 0;

        /// <summary>名義 <paramref name="aliasId"/> の関与を 1 件積む。<paramref name="rep"/> が null の関与は集計対象外として捨てる。</summary>
        public void Offer(int aliasId, Involvement inv, string? rep)
        {
            if (rep is null) return;
            if (_owner._ctx.IsMovieKindSeries(inv.SeriesId))
                MovieSeriesIds.Add(inv.SeriesId);
            else
                EpisodeKeys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
            SeriesIds.Add(inv.SeriesId);
            First.Offer(inv);
            if (!_firstByAlias.TryGetValue(aliasId, out var aliasFirst))
            {
                aliasFirst = new FirstCreditAccumulator(_owner._ctx);
                _firstByAlias[aliasId] = aliasFirst;
                _aliasOrder.Add(aliasId);
            }
            aliasFirst.Offer(inv);
            _owner.OfferEarliestRole(EarliestByRep, rep, inv);
            if (!UsageByRoleCode.TryGetValue(inv.RoleCode, out var usage))
            {
                usage = new RoleCodeUsage();
                UsageByRoleCode[inv.RoleCode] = usage;
            }
            usage.Offer(inv, _owner._ctx.IsMovieKindSeries(inv.SeriesId), _owner.CreditOrderKey(inv));
        }
    }

    /// <summary>1 つの表記（role_code）での担当数（TV 系は話、映画系は本で重複排除）と、最も早くクレジットされた位置。</summary>
    private sealed class RoleCodeUsage
    {
        private readonly HashSet<(int SeriesId, int EpisodeId)> _episodes = new();
        private readonly HashSet<int> _movies = new();

        /// <summary>最も早くクレジットされた (シリーズ放送開始日, 話数, クレジット出現位置)。</summary>
        public (long Start, int EpNo, long Pos) First { get; private set; } = (long.MaxValue, int.MaxValue, long.MaxValue);

        /// <summary>担当数（話数 + 本数）。</summary>
        public int Count => _episodes.Count + _movies.Count;

        /// <summary>TV 系シリーズでの担当話数。</summary>
        public int EpisodeCount => _episodes.Count;

        /// <summary>映画系シリーズでの担当本数。</summary>
        public int MovieCount => _movies.Count;

        public void Offer(Involvement inv, bool isMovie, (long Start, int EpNo, long Pos) key)
        {
            if (isMovie) _movies.Add(inv.SeriesId);
            else _episodes.Add((inv.SeriesId, inv.EpisodeId ?? 0));
            if (key.CompareTo(First) < 0) First = key;
        }
    }

    // ─── テンプレ用 DTO 群 ───

    /// <summary>
    /// 人物・企業/団体の一覧行の 2 系統。<see cref="DebutRows"/> は名義ごと（初参加順タブ用）、
    /// <see cref="CountRows"/> はエンティティごと（担当回数順タブ・件数集計用）。
    /// </summary>
    private sealed class EntityRowSet
    {
        public List<EntityRow> DebutRows { get; } = new();
        public List<EntityRow> CountRows { get; } = new();
    }

    private sealed class LandingModel
    {
        /// <summary>スタッフの人物数・団体数（人と団体は合算しない）。</summary>
        public int StaffPersonCount { get; set; }
        public int StaffCompanyCount { get; set; }
        public int VoiceCastCount { get; set; }
        /// <summary>音楽制作に関わった人物数・団体数（人と団体は合算しない）。</summary>
        public int MusicProductionPersonCount { get; set; }
        public int MusicProductionCompanyCount { get; set; }
        /// <summary>楽曲の歌唱に関わった人物数（キャラとしての歌唱は声優として数える）。</summary>
        public int SingerCount { get; set; }
    }

    /// <summary>音楽制作ページの表示モデル。</summary>
    private sealed class SongPersonListModel
    {
        /// <summary>役職タブ：音楽クレジットの区分ごとの役職（各役職詳細への入口、人数つき）。</summary>
        public IReadOnlyList<MusicRoleSection> RoleSections { get; set; } = Array.Empty<MusicRoleSection>();
        /// <summary>一覧タブ（歌（詞曲）/ 歌（演奏）/ 劇伴（作編曲）/ 劇伴（演奏）/ 制作）。</summary>
        public IReadOnlyList<MusicListTab> Tabs { get; set; } = Array.Empty<MusicListTab>();
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>音楽制作ページの一覧タブ 1 つ分。</summary>
    private sealed class MusicListTab
    {
        /// <summary>タブの識別子（data-tab の値）。</summary>
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>参加数を作品数で数えるタブなら true（曲数なら false）。</summary>
        public bool IsByWork { get; set; }
        public string CountSortLabel { get; set; } = "";
        /// <summary>初参加順（初参加のシリーズごとのセクション）。</summary>
        public IReadOnlyList<DebutSeriesSection> DebutSections { get; set; } = Array.Empty<DebutSeriesSection>();
        /// <summary>多い順。</summary>
        public IReadOnlyList<MusicEntityRow> CountRows { get; set; } = Array.Empty<MusicEntityRow>();
    }

    /// <summary>音楽制作ページの役職タブの区分 1 つ分。</summary>
    private sealed class MusicRoleSection
    {
        public string Label { get; set; } = "";
        public IReadOnlyList<RoleIndexEntry> Roles { get; set; } = Array.Empty<RoleIndexEntry>();
    }

    /// <summary>音楽制作ページの歌・劇伴タブの 1 行（人物または団体）。</summary>
    private sealed class MusicEntityRow
    {
        /// <summary>"person" / "company"。</summary>
        public string EntityKind { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>参加数（歌タブは曲数、劇伴タブは作品数、制作タブは作品数 + 盤の数）。多い順の並べ替えに使う。</summary>
        public int Count { get; set; }
        /// <summary>作品単位のタブ（劇伴・制作）での TV 系シリーズの数（📺）と映画系の本数（🎥）。</summary>
        public int TvWorkCount { get; set; }
        public int MovieWorkCount { get; set; }
        /// <summary>制作タブで、盤（商品）に付いたクレジットの盤の数（💿）。</summary>
        public int ProductCount { get; set; }
        public long DebutSort { get; set; }
        /// <summary>初参加の曲名・作品名とリンク先。</summary>
        public string DebutLabel { get; set; } = "";
        public string DebutUrl { get; set; } = "";
        /// <summary>初参加のシリーズ（初参加順のシリーズ別セクション用。不明なら null）。</summary>
        public int? DebutSeriesId { get; set; }
        public IReadOnlyList<MusicRoleBadge> Roles { get; set; } = Array.Empty<MusicRoleBadge>();
    }

    private sealed class MusicRoleBadge
    {
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
    }

    /// <summary>音楽クレジットの役職詳細ページの表示モデル。</summary>
    private sealed class MusicRoleDetailModel
    {
        public string RoleNameJa { get; set; } = "";
        /// <summary>役職の区分名（演奏・コーラス等 など）。</summary>
        public string GroupLabel { get; set; } = "";
        /// <summary>初参加順：初参加の作品ごとのセクション（行は <see cref="MusicRoleDetailRow"/>）。</summary>
        public IReadOnlyList<DebutSeriesSection> DebutSections { get; set; } = Array.Empty<DebutSeriesSection>();
        /// <summary>参加数順。</summary>
        public IReadOnlyList<MusicRoleDetailRow> CountRows { get; set; } = Array.Empty<MusicRoleDetailRow>();
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>音楽クレジットの役職詳細の 1 行（人物または団体と、その担当先）。</summary>
    private sealed class MusicRoleDetailRow
    {
        public string EntityKind { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>初参加の担当先の日付（根拠の盤の発売日）と作品。</summary>
        public DateTime FirstSort { get; set; }
        public int? DebutSeriesId { get; set; }
        /// <summary>劇伴を担当した TV 系の作品数（📺）と映画系の本数（🎥）。</summary>
        public int TvWorkCount { get; set; }
        public int MovieWorkCount { get; set; }
        /// <summary>担当した歌の曲数（🎵。録音違いは 1 曲に数える）。</summary>
        public int SongCount { get; set; }
        /// <summary>担当した盤の点数（💿）。</summary>
        public int ProductCount { get; set; }
        /// <summary>件数の合計（📺 + 🎥 + 🎵 + 💿）。参加数順の並べ替えに使う。</summary>
        public int Count { get; set; }
        /// <summary>劇伴：1 作品 1 行（Sub は担当した録音回を「・」でつないだもの）。</summary>
        public IReadOnlyList<MusicRoleTarget> BgmItems { get; set; } = Array.Empty<MusicRoleTarget>();
        /// <summary>歌：曲・録音ごとに 1 行。</summary>
        public IReadOnlyList<MusicRoleTarget> SongItems { get; set; } = Array.Empty<MusicRoleTarget>();
        /// <summary>盤：1 点 1 行。</summary>
        public IReadOnlyList<MusicRoleTarget> ProductItems { get; set; } = Array.Empty<MusicRoleTarget>();
    }

    /// <summary>担当先 1 つ（劇伴の作品・曲・盤）。</summary>
    private sealed class MusicRoleTarget
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Sub { get; set; } = "";
    }

    /// <summary>歌唱ページの表示モデル。</summary>
    private sealed class SingersModel
    {
        /// <summary>初参加順タブ：初参加の録音の出典シリーズごとのセクション（行は <see cref="SingerListRow"/>）。</summary>
        public IReadOnlyList<DebutSeriesSection> DebutSections { get; set; } = Array.Empty<DebutSeriesSection>();
        /// <summary>参加曲数順タブ。</summary>
        public IReadOnlyList<SingerListRow> CountRows { get; set; } = Array.Empty<SingerListRow>();
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>歌唱ページの 1 行。歌手（人物単位）またはキャラクター（キャラ × 声優の組）。</summary>
    private sealed class SingerListRow
    {
        /// <summary>"singer" / "character"（絞り込み用の data-entity-type と行頭アイコンの出し分け）。</summary>
        public string EntityKind { get; set; } = "";
        /// <summary>歌手は人物名（表示名義）、キャラクターは「変身前 / 変身後」または最初に歌ったときの名義。</summary>
        public string Name { get; set; } = "";
        public string NameKana { get; set; } = "";
        public string Url { get; set; } = "";
        /// <summary>キャラクターの行の声優名と人物詳細 URL（歌手の行では空）。</summary>
        public string VoiceName { get; set; } = "";
        public string VoiceUrl { get; set; } = "";
        public int SongCount { get; set; }
        /// <summary>初参加の録音（最小 recording_id）と、その出典シリーズ・曲。</summary>
        public int DebutRecordingId { get; set; }
        public int? DebutSeriesId { get; set; }
        public string DebutSongTitle { get; set; } = "";
        public string DebutSongUrl { get; set; } = "";
    }

    private sealed class StaffModel
    {
        public IReadOnlyList<RoleIndexEntry> Roles { get; set; } = Array.Empty<RoleIndexEntry>();
        /// <summary>役職順タブの前半：TV 系シリーズで 1 度でもクレジットされた役職。</summary>
        public IReadOnlyList<RoleIndexEntry> TvRoles { get; set; } = Array.Empty<RoleIndexEntry>();
        /// <summary>役職順タブの後半：映画系シリーズでのみクレジットされた役職（別セクション）。</summary>
        public IReadOnlyList<RoleIndexEntry> MovieOnlyRoles { get; set; } = Array.Empty<RoleIndexEntry>();
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
        /// <summary>役職名の変遷（実際にクレジットされた表記が 2 つ以上あるときだけ入る）。入っているときは
        /// テンプレが <see cref="AlternateNames"/> の 1 行の代わりにこの節を出す。</summary>
        public IReadOnlyList<RoleNameHistoryItem> NameHistory { get; set; } = Array.Empty<RoleNameHistoryItem>();
        public string CoverageLabel { get; set; } = "";
        /// <summary>個人・団体の件数。両方 &gt; 0 のときだけ entity-filter（すべて / 個人のみ / 団体のみ）をテンプレで表示する（片方だけの役職では絞り込みが無意味なため）。</summary>
        public int PersonCount { get; set; }
        public int CompanyCount { get; set; }
    }

    /// <summary>歌系役職詳細ページ用の表示モデル。 既存 <see cref="RoleDetailModel"/> と並列に置く別 DTO。初参加順タブは持たない。</summary>
    private sealed class SongRoleDetailModel
    {
        public string RoleNameJa { get; set; } = "";
        public IReadOnlyList<SongRoleRow> KanaRows { get; set; } = Array.Empty<SongRoleRow>();
        /// <summary>初参加順（recording_id 順）の行。五十音順の代替として既定タブに使う。</summary>
        public IReadOnlyList<SongRoleRow> DebutRows { get; set; } = Array.Empty<SongRoleRow>();
        public IReadOnlyList<SongRoleRow> CountRows { get; set; } = Array.Empty<SongRoleRow>();
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>歌系役職詳細ページの 1 行（人物単位）。 「担当曲数」は当該役職で関与した distinct song_id の数。</summary>
    private sealed class SongRoleRow
    {
        public int PersonId { get; set; }
        public string PersonName { get; set; } = "";
        public string PersonNameKana { get; set; } = "";
        public string PersonUrl { get; set; } = "";
        public int SongCount { get; set; }
        /// <summary>初参加順の代理ソートキー。当該役職で関与した録音／曲の最小 recording_id（未取得は int.MaxValue）。</summary>
        public int DebutRecordingId { get; set; }
        /// <summary>初参加の録音の出典シリーズ（初参加順のシリーズ別セクション用。不明なら null）。</summary>
        public int? DebutSeriesId { get; set; }
        /// <summary>初参加の曲名と曲詳細 URL（音楽制作・歌唱ページで添える。役職詳細では空）。</summary>
        public string DebutSongTitle { get; set; } = "";
        public string DebutSongUrl { get; set; } = "";
        /// <summary>関わった役職名（音楽制作ページ用。「作詞・作曲」など。他では空）。</summary>
        public string RolesLabel { get; set; } = "";
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
        /// <summary>映画系シリーズでのみクレジットされた役職なら true（役職順タブで別セクションに分ける）。</summary>
        public bool IsMovieOnly { get; set; }
    }

    private sealed class AlternateNameItem
    {
        public string RoleNameJa { get; set; } = "";
    }

    /// <summary>「役職名の変遷」の 1 表記分（<see cref="BuildRoleNameHistory"/>）。</summary>
    private sealed class RoleNameHistoryItem
    {
        public string RoleNameJa { get; set; } = "";
        /// <summary>この表記での TV 系の担当話数（全担当者の (シリーズ, 話) で重複を除いた数）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>この表記での映画系の担当本数。</summary>
        public int MovieCount { get; set; }
        /// <summary>この表記でクレジットされた作品（放送・公開順）。</summary>
        public IReadOnlyList<RoleNameHistoryWork> Works { get; set; } = Array.Empty<RoleNameHistoryWork>();
    }

    /// <summary>「役職名の変遷」の作品 1 つ分。TV 系は話数の範囲（「#1～12, 15」）を持ち、映画系は空。</summary>
    private sealed class RoleNameHistoryWork
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string SeriesYearLabel { get; set; } = "";
        public string EpisodeRangeLabel { get; set; } = "";
    }

    /// <summary>人物・企業/団体を 1 リストに混在させるための共通行。 <see cref="EntityKind"/> は "person" / "company"（テンプレ側のバッジ・絞り込み用）。</summary>
    private sealed class EntityRow
    {
        public string EntityKind { get; set; } = "";
        public int EntityId { get; set; }
        public string EntityName { get; set; } = "";
        public string EntityNameKana { get; set; } = "";
        /// <summary>初参加順の行を旧名義で置いたときの、いまの名乗り（人物の表示名義）。添えない行は空文字。 テンプレ側で名前の後ろに括弧書きで添える。</summary>
        public string CurrentNameNote { get; set; } = "";
        public string EntityUrl { get; set; } = "";
        /// <summary>TV 系シリーズ（series_kinds.credit_attach_to='EPISODE'）での担当エピソード合計数。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>映画系シリーズ（series_kinds.credit_attach_to='SERIES'、MOVIE / MOVIE_SHORT / SPRING / EVENT）での担当本数（1 シリーズ = 1 本）。</summary>
        public int MovieCount { get; set; }
        /// <summary>担当の総量（<see cref="EpisodeCount"/> + <see cref="MovieCount"/>）。担当多い順タブのソートキー兼テンプレ存在判定用。</summary>
        public int TotalCount => EpisodeCount + MovieCount;
        /// <summary>"担当 N 話・M 本" / "担当 N 話" / "担当 M 本" の単位付き表記。両方ゼロなら空文字。
        /// 「担当」の動詞を冠して、エピソードの話数（#N・第N話）と数量の「N 話」を読み分けられるようにする。</summary>
        public string CountLabel => (EpisodeCount, MovieCount) switch
        {
            ( > 0, > 0) => $"担当 {EpisodeCount} 話・{MovieCount} 本",
            ( > 0, 0)   => $"担当 {EpisodeCount} 話",
            (0,   > 0) => $"担当 {MovieCount} 本",
            _           => ""
        };
        /// <summary>担当数バッジ（📺話・🎥本のピル）の前に冠する動詞。スタッフ系は常に「担当」。</summary>
        public string CountVerb => "担当";
        /// <summary>役職詳細ページ（/creators/roles/{code}/）の行リンクにかける tooltip。
        /// このエンティティがこの役職で担当した作品を放送開始日順に「シリーズ名（N話）／映画 …（映画）」で
        /// 「／」連結した文字列（プレーンテキスト。テンプレ側で title 属性に流すため html.escape 済み前提ではなく生値）。
        /// 役職詳細以外（スタッフ一覧など）の経路では空のまま。</summary>
        public string WorksTooltip { get; set; } = "";
        public int SeriesCount { get; set; }
        /// <summary>役職ラベル（スタッフ一覧でのみ使用。役職詳細では空のまま）。</summary>
        public string RolesLabel { get; set; } = "";
        /// <summary>役職詳細の行に添える「表記ごとの担当数」（<see cref="BuildRoleUsageNote"/>）。添えない行は空文字。</summary>
        public string RoleUsageNote { get; set; } = "";
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
        /// <summary>声優の person_id（キャラクター別タブで代表 CV をまとめるためのグルーピングキー）。</summary>
        public int PersonId { get; set; }
        public string SeriesTitle { get; set; } = "";
        public string SeriesUrl { get; set; } = "";
        public string SeriesYearLabel { get; set; } = "";
        /// <summary>シリーズ放送開始日のシリアル値（並べ替えキー、表示には用いない）。</summary>
        public long SeriesSortStart { get; set; }
        /// <summary>当該行のシリーズ id（キャラクター別タブで映画本数を distinct 集計するためのキー）。</summary>
        public int SeriesId { get; set; }
        public string CharacterName { get; set; } = "";
        public string CharacterNameKana { get; set; } = "";
        public string CharacterUrl { get; set; } = "";
        /// <summary>キャラクター順タブでキャラを主単位にグルーピングするための ID。</summary>
        public int CharacterId { get; set; }
        /// <summary>当該 (声優 × シリーズ × キャラ) の重複排除済み出演話数（0 = シリーズ全体スコープのみ）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>映画系シリーズ（series_kinds.credit_attach_to='SERIES'）での担当本数（1 シリーズ = 1 本）。</summary>
        public int MovieCount { get; set; }
        /// <summary>キャラクター別タブ（キャラ単位の集約行）専用：代表 CV 以外にも演じた声優が居るとき true。
        /// テンプレ側で「(CV: ○○ 他)」の「他」を付ける。</summary>
        public bool HasOtherPersons { get; set; }
        /// <summary>初出演順タブのタイブレーク用、シリーズ内最早話数（話数不明・全体スコープは 0）。</summary>
        public int EarliestEpNo { get; set; }
        /// <summary>最早話数内でこの (声優 × シリーズ × キャラ) が最初にクレジットされた 階層位置 (CreditSeq, CreditSubSeq) の合成キー。</summary>
        public long EarliestPos { get; set; }
        /// <summary>出演回数順タブ（声優単位の集約行）専用：代表キャラ以外にも演じたキャラが
        /// 居るとき true。テンプレ側で「（キャラ 役 他）」の「他」を付ける。</summary>
        public bool HasOtherCharacters { get; set; }
    }
}