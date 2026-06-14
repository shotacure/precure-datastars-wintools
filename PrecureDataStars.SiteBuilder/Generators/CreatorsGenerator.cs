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
    // 歌系 4 役職は episode_theme_songs を介さずに song_credits / song_recording_singers
    // から直接集計するため、専用のリポジトリを別途注入する。本編クレジットに登場しない
    // 楽曲スタッフ（劇中歌・キャラソンの作詞家など）もカバーするのが趣旨。
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecSingersRepo;

    /// <summary>
    /// 歌系の 4 役職コード集合。これらの役職の /creators/roles/{code}/ ページは、
    /// episode_theme_songs を経由するクレジット階層集計（<see cref="_index"/>）ではなく、
    /// song_credits / song_recording_singers を直接集計する別ルートで生成する。
    /// 既存ルートだと「本編クレジットに登場しない楽曲だけに関わったスタッフ」が抜け落ちるため。
    /// </summary>
    private static readonly HashSet<string> SongRoleCodes = new(StringComparer.Ordinal)
    {
        SongCreditRoles.Lyrics,
        SongCreditRoles.Composition,
        SongCreditRoles.Arrangement,
        SongRecordingSingerRoles.Vocals,
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
            .Where(r => string.Equals(_resolver.GetRepresentative(r.RoleCode), r.RoleCode, StringComparison.Ordinal))
            .OrderBy(r => r.RoleCode, StringComparer.Ordinal)
            .ToList();

        var personById = allPersons.ToDictionary(p => p.PersonId);
        var companyById = allCompanies.ToDictionary(c => c.CompanyId);

        // ── 役職詳細ページ群を生成し、あわせて「役職順」タブ用の索引エントリも構築 ──
        var roleIndexEntries = new List<RoleIndexEntry>();

        // 歌系 4 役職は別ルート集計のため、song_credits / song_recording_singers を 1 度だけ全件ロード。
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

            // 歌系 4 役職は専用集計に分岐。本編クレジット階層を介さず、song_credits /
            // song_recording_singers から人物別の「担当曲数」を直接数える。
            // ただしスタッフ一覧の「役職順」タブで自然な並び位置に置きたいので、ソート用の
            // 最早クレジット位置 (SortStart / SortEpNo / SortPos) は episode_theme_songs 経由で
            // _index に登録済みの involvement から拾う（本編に登場する楽曲があれば必ず当たる）。
            // 楽曲がどのエピソードにも紐付かないレア役職は long.MaxValue のまま末尾扱いになる。
            if (SongRoleCodes.Contains(role.RoleCode))
            {
                var songRows = BuildSongRoleRows(role.RoleCode, allSongCredits, allSingers,
                    personIdByAlias, personById);
                if (songRows.Count == 0) continue;
                GenerateSongRoleDetail(role, songRows);
                var (songSortStart, songSortEpNo, songSortPos) =
                    FindEarliestRoleAnchor(role.RoleCode);
                roleIndexEntries.Add(new RoleIndexEntry
                {
                    RoleNameJa = role.NameJa,
                    RoleUrl = PathUtil.CreatorsRoleUrl(role.RoleCode),
                    PersonCount = songRows.Count,
                    CompanyCount = 0,
                    SortStart = songSortStart,
                    SortEpNo = songSortEpNo,
                    SortPos = songSortPos,
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

            var rows = BuildRoleEntityRows(
                memberCodes, aliasIdsByPersonId, allPersons,
                companyAliasesByCompany, logosByCompanyAlias, allCompanies, companyAliasById);

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
                RoleUrl = PathUtil.CreatorsRoleUrl(role.RoleCode),
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
            companyAliasesByCompany, logosByCompanyAlias, allCompanies, companyAliasById, companyById,
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
        IReadOnlyList<Company> allCompanies,
        IReadOnlyDictionary<int, CompanyAlias> companyAliasById)
    {
        var rows = new List<EntityRow>();

        // 人物。
        foreach (var p in allPersons)
        {
            if (!aliasIdsByPersonId.TryGetValue(p.PersonId, out var aliasIds)) continue;

            // TV 系シリーズの参加は (seriesId, episodeId) ペアで重複排除 → 話数。
            // 映画系シリーズの参加は seriesId だけで重複排除 → 本数（1 シリーズ = 1 本）。
            var episodeKeys = new HashSet<(int seriesId, int episodeId)>();
            var movieSeriesIds = new HashSet<int>();
            var seriesIds = new HashSet<int>();
            var firstKey = new FirstCreditAccumulator(_ctx);
            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    if (!memberCodes.Contains(inv.RoleCode)) continue;
                    if (IsMovieKindSeries(inv.SeriesId))
                        movieSeriesIds.Add(inv.SeriesId);
                    else
                        episodeKeys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    seriesIds.Add(inv.SeriesId);
                    firstKey.Offer(inv);
                }
            }
            if (episodeKeys.Count == 0 && movieSeriesIds.Count == 0) continue;

            rows.Add(MakeEntityRow("person", p.PersonId, p.FullName, p.FullNameKana ?? "",
                PathUtil.PersonUrl(p.PersonId), episodeKeys.Count, movieSeriesIds.Count, seriesIds.Count, firstKey,
                BuildWorksTooltip(episodeKeys, movieSeriesIds)));
        }

        // 企業・団体（COMPANY + LOGO + leading_company の 3 ルート合算）。
        // 行の単位は企業（正式名称）ではなく「クレジットされた屋号（alias）」。同一企業でも
        // 屋号が変われば別行になり、初参加順のシリーズセクションには「その時の屋号」が並ぶ。
        // ロゴ経由の関与はロゴを保有する屋号に帰属。リンク先はいずれも親企業の詳細ページ。
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            foreach (var aid in aliasIds)
            {
                var episodeKeys = new HashSet<(int seriesId, int episodeId)>();
                var movieSeriesIds = new HashSet<int>();
                var seriesIds = new HashSet<int>();
                var firstKey = new FirstCreditAccumulator(_ctx);

                if (_index.ByCompanyAlias.TryGetValue(aid, out var invs))
                {
                    foreach (var inv in invs)
                    {
                        if (!memberCodes.Contains(inv.RoleCode)) continue;
                        if (IsMovieKindSeries(inv.SeriesId))
                            movieSeriesIds.Add(inv.SeriesId);
                        else
                            episodeKeys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
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
                            if (IsMovieKindSeries(inv.SeriesId))
                                movieSeriesIds.Add(inv.SeriesId);
                            else
                                episodeKeys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                            seriesIds.Add(inv.SeriesId);
                            firstKey.Offer(inv);
                        }
                    }
                }
                if (episodeKeys.Count == 0 && movieSeriesIds.Count == 0) continue;

                var alias = companyAliasById[aid];
                rows.Add(MakeEntityRow("company", c.CompanyId, alias.Name, alias.NameKana ?? "",
                    PathUtil.CompanyUrl(c.CompanyId), episodeKeys.Count, movieSeriesIds.Count, seriesIds.Count, firstKey,
                    BuildWorksTooltip(episodeKeys, movieSeriesIds)));
            }
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
            // 五十音順タブは読み（kana）データ未整備のため一旦無効化（テンプレも初参加順を既定に繰り上げ済み）。
            // データが揃ったら下行のコメントを外して復活させる。
            // KanaRows = SortByKana(rows),
            DebutSections = SectionByDebut(rows),
            CountRows = SortByCount(rows),
            AlternateNames = alternateNames,
            CoverageLabel = _ctx.CreditCoverageLabel,
            // 個人と団体が両方そろっているときだけ entity-filter を出すための件数（片方だけの役職では絞り込みが無意味）。
            PersonCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal)),
            CompanyCount = rows.Count(r => string.Equals(r.EntityKind, "company", StringComparison.Ordinal))
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}（クリエーター）",
            MetaDescription = $"歴代プリキュアシリーズで役職「{role.NameJa}」を担当した人物・企業・団体を一覧にしました。初参加順・担当話数が多い順で並べ替えられます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
                new BreadcrumbItem { Label = role.NameJa, Url = "" }
            }
        };
        // 出力先パスもリンク生成と同一の PathUtil.CreatorsRoleUrl を通すことで、
        // URL パス上のコード小文字化と出力ディレクトリ名を必ず一致させる。
        _page.RenderAndWrite(PathUtil.CreatorsRoleUrl(role.RoleCode), "creators",
            "creators-role-detail.sbn", content, layout);
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
                Add(s.PersonAliasId, songId, s.SongRecordingId);
                Add(s.SlashPersonAliasId, songId, s.SongRecordingId);
                Add(s.VoicePersonAliasId, songId, s.SongRecordingId);
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
                PersonName = p.FullName,
                PersonNameKana = p.FullNameKana ?? "",
                PersonUrl = PathUtil.PersonUrl(kv.Key),
                SongCount = kv.Value.Count,
                // 初参加順の代理キー。未取得は末尾に送るため int.MaxValue。
                DebutRecordingId = debutRecIdByPerson.TryGetValue(kv.Key, out var dr) ? dr : int.MaxValue
            });
        }
        return rows;
    }

    /// <summary>歌系役職 1 種の専用ページ <c>/creators/roles/{code}/</c> を「五十音順 / 担当曲数が多い順」の 2 タブで書き出す。</summary>
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
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = $"{role.NameJa}（クリエーター）",
            MetaDescription = $"歴代プリキュアの楽曲で役職「{role.NameJa}」を担当した人物を一覧にしました。初参加順・担当曲数が多い順で並べ替えられます。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代クリエーター", Url = PathUtil.CreatorsLandingUrl() },
                new BreadcrumbItem { Label = "歴代プリキュアスタッフ", Url = PathUtil.CreatorsStaffUrl() },
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

    /// <summary>担当曲数が多い順：曲数降順 → 読み → 名前（順位は付けない）。</summary>
    private static List<SongRoleRow> SortSongRowsByCount(IEnumerable<SongRoleRow> rows) => rows
        .OrderByDescending(r => r.SongCount)
        .ThenBy(r => r.PersonNameKana, StringComparer.Ordinal)
        .ThenBy(r => r.PersonName, StringComparer.Ordinal)
        .ToList();

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

            // TV 系シリーズの参加は (seriesId, episodeId) ペアで重複排除 → 話数。
            // 映画系シリーズの参加は seriesId だけで重複排除 → 本数。
            var episodeKeys = new HashSet<(int seriesId, int episodeId)>();
            var movieSeriesIds = new HashSet<int>();

            foreach (var aid in aliasIds)
            {
                if (!_index.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                foreach (var inv in invs)
                {
                    string rep = _resolver.GetRepresentative(inv.RoleCode);
                    if (!repNameMap.ContainsKey(rep)) continue; // VOICE_CAST 等は対象外
                    if (IsMovieKindSeries(inv.SeriesId))
                        movieSeriesIds.Add(inv.SeriesId);
                    else
                        episodeKeys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    seriesIds.Add(inv.SeriesId);
                    firstKey.Offer(inv);
                    OfferEarliestRole(earliestByRep, rep, inv);
                }
            }
            if (keys.Count == 0) continue;

            var row = MakeEntityRow("person", p.PersonId, p.FullName, p.FullNameKana ?? "",
                PathUtil.PersonUrl(p.PersonId), episodeKeys.Count, movieSeriesIds.Count, seriesIds.Count, firstKey);
            row.RolesLabel = BuildRolesLabel(earliestByRep, repNameMap);
            rows.Add(row);
        }

        // 企業・団体（COMPANY + LOGO + leading_company を合算、全役職横断）。
        // 行の単位は企業（正式名称）ではなく「クレジットされた屋号（alias）」。同一企業でも
        // 屋号が変われば別行になり、初参加順のシリーズセクションには「その時の屋号」が並ぶ。
        // ロゴ経由の関与はロゴを保有する屋号に帰属。リンク先はいずれも親企業の詳細ページ。
        foreach (var c in allCompanies)
        {
            if (!companyAliasesByCompany.TryGetValue(c.CompanyId, out var aliasIds)) continue;

            foreach (var aid in aliasIds)
            {
                var keys = new HashSet<(int seriesId, int episodeId)>();
                var seriesIds = new HashSet<int>();
                var firstKey = new FirstCreditAccumulator(_ctx);
                var earliestByRep = new Dictionary<string, (DateOnly Start, int EpNo, long Pos)>(StringComparer.Ordinal);
                var episodeKeys = new HashSet<(int seriesId, int episodeId)>();
                var movieSeriesIds = new HashSet<int>();

                void Accumulate(Involvement inv)
                {
                    string rep = _resolver.GetRepresentative(inv.RoleCode);
                    if (!repNameMap.ContainsKey(rep)) return;
                    if (IsMovieKindSeries(inv.SeriesId))
                        movieSeriesIds.Add(inv.SeriesId);
                    else
                        episodeKeys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    keys.Add((inv.SeriesId, inv.EpisodeId ?? 0));
                    seriesIds.Add(inv.SeriesId);
                    firstKey.Offer(inv);
                    OfferEarliestRole(earliestByRep, rep, inv);
                }

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
                if (keys.Count == 0) continue;

                var alias = companyAliasById[aid];
                var row = MakeEntityRow("company", c.CompanyId, alias.Name, alias.NameKana ?? "",
                    PathUtil.CompanyUrl(c.CompanyId), episodeKeys.Count, movieSeriesIds.Count, seriesIds.Count, firstKey);
                row.RolesLabel = BuildRolesLabel(earliestByRep, repNameMap);
                rows.Add(row);
            }
        }

        staffEntityCount = rows.Count;

        var content = new StaffModel
        {
            Roles = roleIndexEntries,
            TotalRoles = roleIndexEntries.Count,
            // 五十音順タブは読み（kana）データ未整備のため一旦無効化。テンプレ側もコメントアウト済み。
            // データが揃ったら下行のコメントを外して復活させる（KanaRows は未設定＝空のまま）。
            // KanaRows = SortByKana(rows),
            DebutSections = SectionByDebut(rows),
            CountRows = SortByCount(rows),
            PersonCount = rows.Count(r => string.Equals(r.EntityKind, "person", StringComparison.Ordinal)),
            CompanyCount = rows.Count(r => string.Equals(r.EntityKind, "company", StringComparison.Ordinal)),
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュアスタッフ",
            MetaDescription = "プリキュアを支えたスタッフ（人物・企業・団体）を一覧。役職や参加話数で並べ替えて、「あの人はどの作品に関わった？」をたどれます。",
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
        var aliasById = allCharacterAliases.ToDictionary(a => a.AliasId);
        var aliasToCharId = new Dictionary<int, int>();
        foreach (var a in allCharacterAliases) aliasToCharId[a.AliasId] = a.CharacterId;

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
                    if (inv.Kind != InvolvementKind.CharacterVoice) continue;
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

        var rows = new List<VoiceCastRow>();
        // 初出演順タブ用：声優 1 人 = 1 行（初参加シリーズのセクションにのみ載せる）。
        // 話数は全シリーズ・全キャラ通算の重複排除エピソード数。
        var debutRows = new List<VoiceCastRow>();
        // 出演話数が多い順タブ用：声優 1 人 = 1 行。代表キャラ＝クレジット話数が最も多いキャラ
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
                bool isMovie = IsMovieKindSeries(seriesId);

                var row = new VoiceCastRow
                {
                    PersonName = p.FullName,
                    PersonNameKana = p.FullNameKana ?? "",
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

                // 出演話数が多い順タブ用の 1 行：声優単位の通算（重複排除）話数。
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

        // 出演話数が多い順（セクション無し）：声優 1 人 = 1 行（countAggRows に集約済み）。
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
            MetaDescription = "脚本・演出・作画から制作会社まで、プリキュアを作り上げたスタッフと、キャラクターを演じた声優。作品の「裏側」を担った作り手をたどれます。",
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

    /// <summary>当該シリーズが「映画系（series_kinds.credit_attach_to='SERIES'）」かを判定する。 該当 = MOVIE / MOVIE_SHORT / SPRING / EVENT。 EntityRow 構築側で「TV 系のエピソード参加（話）」と「映画系のシリーズ参加（本）」を分けて集計する用途で使う。</summary>
    private bool IsMovieKindSeries(int seriesId)
    {
        if (!_ctx.SeriesById.TryGetValue(seriesId, out var s)) return false;
        return _ctx.SeriesKindByCode.TryGetValue(s.KindCode, out var sk)
            && string.Equals(sk.CreditAttachTo, "SERIES", StringComparison.Ordinal);
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

    /// <summary>
    /// 指定役職コードについて、クレジット階層上の最早位置 (SortStart, SortEpNo, SortPos) を
    /// <see cref="_index"/> の人物・企業エイリアス・ロゴ involvement から横断的に拾う。
    /// 歌系 4 役職の「役職順」タブの並び順を、専用集計（episode_theme_songs に依存しない曲スタッフ
    /// の取りこぼし救済）と別軸で、本編クレジットでの自然な出現位置に揃えるために使う。
    /// どの involvement も該当しなければ <c>(long.MaxValue, int.MaxValue, long.MaxValue)</c> を返し、
    /// テンプレ側で末尾扱いになる。
    /// </summary>
    private (long Start, int EpNo, long Pos) FindEarliestRoleAnchor(string roleCode)
    {
        long bestStart = long.MaxValue;
        int bestEpNo = int.MaxValue;
        long bestPos = long.MaxValue;

        void Offer(Involvement inv)
        {
            if (!string.Equals(inv.RoleCode, roleCode, StringComparison.Ordinal)) return;
            var start = _ctx.SeriesStartDate(inv.SeriesId);
            long startTicks = start.DayNumber;
            int epNo = inv.EpisodeId is int eid
                ? (_ctx.LookupEpisode(inv.SeriesId, eid)?.SeriesEpNo ?? int.MaxValue)
                : 0;
            long pos = CombinedCreditPos(inv);
            if (startTicks < bestStart
                || (startTicks == bestStart && epNo < bestEpNo)
                || (startTicks == bestStart && epNo == bestEpNo && pos < bestPos))
            {
                bestStart = startTicks;
                bestEpNo = epNo;
                bestPos = pos;
            }
        }

        foreach (var kv in _index.ByPersonAlias)
        {
            foreach (var inv in kv.Value) Offer(inv);
        }
        foreach (var kv in _index.ByCompanyAlias)
        {
            foreach (var inv in kv.Value) Offer(inv);
        }
        foreach (var kv in _index.ByLogo)
        {
            foreach (var inv in kv.Value) Offer(inv);
        }
        return (bestStart, bestEpNo, bestPos);
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

    /// <summary>担当話数が多い順：担当量降順（TV 話 + 映画本の単純合算 <see cref="EntityRow.TotalCount"/>） → 最早クレジット (放送開始, 話数, クレジット出現位置) → 読み → 名前（順位は付けない）。 五十音順以外（並びのルールが完全には一意に決まらないタブ）では、クレジット 出現位置を暗黙の副ソートキーとして効かせ、同点行の並びを安定させる方針。 ここでは担当量が同数の行を、初出が早い順 → そのエピソード内のクレジット 記載位置順に整える。</summary>
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
        /// <summary>出演話数が多い順タブ（声優単位の集約行）専用：代表キャラ以外にも演じたキャラが
        /// 居るとき true。テンプレ側で「（キャラ 役 他）」の「他」を付ける。</summary>
        public bool HasOtherCharacters { get; set; }
    }
}