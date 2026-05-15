using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// シリーズ系ページ（一覧 + 個別）の生成。
/// <para>
/// <c>/series/</c> は全シリーズの索引、<c>/series/{slug}/</c> は各シリーズの詳細。
/// </para>
/// <para>
/// 一覧の構成（v1.3.0 公開直前のデザイン整理）：
/// </para>
/// <list type="number">
///   <item><description>TV シリーズセクション：放送開始順に連番付きで純粋な TV シリーズだけを並べる。
///     旧仕様で子作品（併映短編・子映画）を字下げ表示していたのは廃止した。映画系は下の映画セクションへ。</description></item>
///   <item><description>映画セクション：親 TV を持たない単独映画系（秋映画・春映画など、<c>parent_series_id</c> が NULL）
///     を公開順に親作品として並べ、その下に親映画にぶら下がる子作品（併映短編・子映画）を字下げ表示する。
///     親映画タイトル先頭に「秋映画／春映画」のシーズンバッジを置く。子作品はリンクなしテキスト表示。</description></item>
///   <item><description>スピンオフセクション：<c>SPIN-OFF</c> 種別だけを放送順に連番付きで並べる。
///     セクション見出しから自明なので [スピンオフ] のテキストラベルは出さない。</description></item>
/// </list>
/// <para>
/// 子作品（<c>parent_series_id</c> が NULL でない映画系）は単独詳細ページを生成しない。
/// 親映画詳細の中の「併映・子作品」セクションに一覧表示するだけにとどめる。
/// これにより sitemap・search-index・ナビからも自動的に除外される（生成しないため）。
/// </para>
/// <para>
/// 個別シリーズページのエピソード一覧は <see cref="SeriesKind.CreditAttachTo"/> が EPISODE のときだけ表示する。
/// 表構造ではなく <c>&lt;dl class="ep-list"&gt;</c> + <c>&lt;dt&gt;</c>（話数 + サブタイトル）+ <c>&lt;dd&gt;</c>
/// （字下げでスタッフ群）の縦並びレイアウト。
/// </para>
/// <para>
/// メインスタッフセクションは PRODUCER / SERIES_COMPOSITION / SERIES_DIRECTOR / CHARACTER_DESIGN / ART_DESIGN
/// の 5 役職を出し、全話担当者は名前のみ、部分担当者は「名前 (#1～4, 8)」表記。
/// </para>
/// <para>
/// 劇伴一覧はシリーズ詳細から除外し、別ページ <c>/bgms/{slug}/</c> へのリンク 1 本に置き換える。
/// </para>
/// </summary>
public sealed class SeriesGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    // ── スタッフ抽出用のクレジット階層 Repository（各エピソードに対して走査する） ──
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditCardsRepository _staffCardsRepo;
    private readonly CreditCardTiersRepository _staffTiersRepo;
    private readonly CreditCardGroupsRepository _staffGroupsRepo;
    private readonly CreditCardRolesRepository _staffCardRolesRepo;
    private readonly CreditRoleBlocksRepository _staffBlocksRepo;
    private readonly CreditBlockEntriesRepository _staffEntriesRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

    // ── スタッフ名リンク化（エピソード「行」のスタッフ群リンク化に使用） ──
    private readonly StaffNameLinkResolver _staffLinkResolver;

    // ── 役職統計詳細ページへの URL 組み立て用（v1.3.0 続編 追加）。
    //    エピソード一覧のスタッフラインの役職ラベル（脚本／絵コンテ／演出 等）を
    //    /stats/roles/{rep_role_code}/ にリンク化するときに使う。 ──
    private readonly RoleSuccessorResolver _roleSuccessorResolver;

    // ── メインスタッフ集計用 ──
    private readonly CreditInvolvementIndex _involvementIndex;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;

    // ── シリーズ × プリキュア紐付け（v1.3.0 公開直前のデザイン整理で追加）。
    //    シリーズ詳細のプリキュアセクション、シリーズ一覧の複合行サブ情報の両方で使う。 ──
    private readonly SeriesPrecuresRepository _seriesPrecuresRepo;
    private readonly PrecuresRepository _precuresRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;

    // ── 屋号解決（v1.3.0 続編 第 N+3 弾で追加）。
    //    シリーズ一覧 TV サブ行のスタッフ欄で「人物名（東映アニメーション）」のように
    //    所属屋号を muted 表記で添えるため、CompanyAlias 全件を 1 度だけロードしてキャッシュする。 ──
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    // ── 役職マスタ・name 解決の共通キャッシュ ──
    private IReadOnlyDictionary<string, Role>? _roleMap;
    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();

    // ── メインスタッフ集計用キャッシュ ──
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasIdsByPersonIdCache;
    private IReadOnlyList<Person>? _allPersonsCache;
    private IReadOnlyDictionary<string, SeriesKind>? _seriesKindMapCache;

    // ── プリキュア集計用キャッシュ（v1.3.0 公開直前のデザイン整理で追加）。
    //    series_id → そのシリーズに紐付くプリキュア表示行リスト（display_order 昇順）。
    //    シリーズ詳細・シリーズ一覧の両方からアクセスするので 1 度だけ構築する。 ──
    private IReadOnlyDictionary<int, IReadOnlyList<SeriesPrecureDisplay>>? _precureRowsBySeriesCache;

    // ── シリーズ一覧サブ行用：メインスタッフ簡易サマリ集計キャッシュ
    //    （v1.3.0 公開直前のデザイン整理第 4 弾で追加、v1.3.0 続編 第 N+3 弾で型を構造化）。
    //    series_id → 役職グループのリスト。
    //    各役職グループは「PRODUCER」「SERIES_COMPOSITION」「SERIES_DIRECTOR」「CHARACTER_DESIGN」「ART_DESIGN」のいずれかで、
    //    連名全員と所属屋号を含む構造化データを持つ。テンプレ側で色付きバッジ + 人物リンクとして描画する。
    //    TV シリーズのみ集計対象。 ──
    private IReadOnlyDictionary<int, IReadOnlyList<KeyStaffRoleGroup>>? _keyStaffSummaryBySeriesCache;

    // ── 屋号 alias_id → 名前 マップ（v1.3.0 続編 第 N+3 弾で追加）。
    //    シリーズ一覧 TV サブ行のスタッフ所属屋号ラベル解決に使用する。
    //    全 CompanyAlias を 1 度だけロードしてキャッシュ。 ──
    private IReadOnlyDictionary<int, string>? _companyAliasNameMapCache;

    // ── エピソード単位 staff サマリの memoize（v1.3.0 続編 第 N+3 弾で追加）。
    //    シリーズ詳細ページ生成（GenerateDetailAsync 内）で各エピソードについて ExtractStaffSummaryAsync を
    //    呼び出すが、その結果を本キャッシュに溜め、後段 /episodes/ ランディング生成側
    //    （EpisodesIndexGenerator）からも参照できるようにする。
    //    キーは episode_id。エピソードが存在しないキー（=未抽出）は呼び出し側で対応。 ──
    private readonly Dictionary<int, EpisodeStaffSummary> _episodeStaffByIdCache = new();

    public SeriesGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        StaffNameLinkResolver staffLinkResolver,
        CreditInvolvementIndex involvementIndex,
        RoleSuccessorResolver roleSuccessorResolver)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _staffLinkResolver = staffLinkResolver;
        _involvementIndex = involvementIndex;
        _roleSuccessorResolver = roleSuccessorResolver;

        _creditsRepo = new CreditsRepository(factory);
        _staffCardsRepo = new CreditCardsRepository(factory);
        _staffTiersRepo = new CreditCardTiersRepository(factory);
        _staffGroupsRepo = new CreditCardGroupsRepository(factory);
        _staffCardRolesRepo = new CreditCardRolesRepository(factory);
        _staffBlocksRepo = new CreditRoleBlocksRepository(factory);
        _staffEntriesRepo = new CreditBlockEntriesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _personsRepo = new PersonsRepository(factory);
        _personAliasPersonsRepo = new PersonAliasPersonsRepository(factory);
        _seriesKindsRepo = new SeriesKindsRepository(factory);
        _seriesPrecuresRepo = new SeriesPrecuresRepository(factory);
        _precuresRepo = new PrecuresRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _companyAliasesRepo = new CompanyAliasesRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating series");

        // シリーズ詳細・シリーズ一覧（複合行サブ情報）の両方でプリキュア紐付け / メインスタッフサマリを使うため、
        // 最初に 1 度だけ全件ロードしてキャッシュに詰めておく
        // （v1.3.0 公開直前のデザイン整理で追加）。
        await BuildPrecureRowsBySeriesCacheAsync(ct).ConfigureAwait(false);
        await BuildKeyStaffSummaryBySeriesCacheAsync(ct).ConfigureAwait(false);

        GenerateIndex();

        // 子作品（parent_series_id != NULL の映画系）は単独詳細ページを生成しない。
        // 親映画詳細の「併映・子作品」セクションに表示されるだけにする。
        // SPIN-OFF は親を持っても単独ページが必要（parent_series_id を持つことは想定していないが念のため）。
        int generated = 0;
        foreach (var s in _ctx.Series)
        {
            if (IsChildOfMovie(s)) continue;
            await GenerateDetailAsync(s, ct).ConfigureAwait(false);
            generated++;
        }

        _ctx.Logger.Success($"series: {generated + 1} ページ");
    }

    /// <summary>
    /// シリーズ × プリキュア紐付け（<c>series_precures</c> テーブル）を全件ロードし、
    /// 表示用の行情報（変身前名・変身後名・声優名）に解決して
    /// <c>series_id → SeriesPrecureDisplay のリスト</c> にグルーピングしてキャッシュする
    /// （v1.3.0 公開直前のデザイン整理で追加）。
    /// <para>
    /// 解決には <c>precures</c> / <c>character_aliases</c> / <c>persons</c> の 3 マスタを使う。
    /// 各 alias / person の名前を最初に Dictionary 化して、紐付け 1 件ごとの引きを O(1) で済ませる。
    /// 並び順は <c>display_order ASC, precure_id ASC</c> でタイブレーク。
    /// </para>
    /// </summary>
    private async Task BuildPrecureRowsBySeriesCacheAsync(CancellationToken ct)
    {
        // マスタ群を 1 度だけ取得して in-memory にバインド。
        var allPairs = await _seriesPrecuresRepo.GetAllAsync(ct).ConfigureAwait(false);
        if (allPairs.Count == 0)
        {
            _precureRowsBySeriesCache = new Dictionary<int, IReadOnlyList<SeriesPrecureDisplay>>();
            return;
        }
        var allPrecures = await _precuresRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var allAliases = await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        // persons は KeyStaff 集計でも使うキャッシュを流用（無ければここで埋める）。
        _allPersonsCache ??= await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);

        var precureById = allPrecures.ToDictionary(p => p.PrecureId);
        var aliasById = allAliases.ToDictionary(a => a.AliasId);
        var personById = _allPersonsCache.ToDictionary(p => p.PersonId);

        // シリーズ別にグルーピングしながら、各エントリを表示行に解決していく。
        // GetAllAsync は (series_id, display_order, precure_id) 昇順で取れているのでそのまま素直に並べればよい。
        var dict = new Dictionary<int, List<SeriesPrecureDisplay>>();
        foreach (var sp in allPairs)
        {
            if (!precureById.TryGetValue(sp.PrecureId, out var precure)) continue;

            string transformName = aliasById.TryGetValue(precure.TransformAliasId, out var trans)
                ? trans.Name : "";
            string preTransformName = aliasById.TryGetValue(precure.PreTransformAliasId, out var pre)
                ? pre.Name : "";
            string voiceActorName = (precure.VoiceActorPersonId is int vid && personById.TryGetValue(vid, out var v))
                ? (v.FullName ?? "") : "";

            var row = new SeriesPrecureDisplay
            {
                PrecureId = precure.PrecureId,
                TransformName = transformName,
                PreTransformName = preTransformName,
                VoiceActorName = voiceActorName,
                VoiceActorPersonId = precure.VoiceActorPersonId
            };

            if (!dict.TryGetValue(sp.SeriesId, out var list))
            {
                list = new List<SeriesPrecureDisplay>();
                dict[sp.SeriesId] = list;
            }
            list.Add(row);
        }

        _precureRowsBySeriesCache = dict.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<SeriesPrecureDisplay>)kv.Value);
    }

    /// <summary>
    /// 指定シリーズに紐付くプリキュア表示行リストを返す
    /// （無ければ空リスト）。
    /// </summary>
    private IReadOnlyList<SeriesPrecureDisplay> GetPrecureRows(int seriesId)
    {
        if (_precureRowsBySeriesCache is null) return Array.Empty<SeriesPrecureDisplay>();
        return _precureRowsBySeriesCache.TryGetValue(seriesId, out var list)
            ? list
            : Array.Empty<SeriesPrecureDisplay>();
    }

    /// <summary>
    /// シリーズ一覧 TV サブ行用のメインスタッフ簡易サマリを全 TV シリーズについて集計し、
    /// <c>series_id → 役職グループのリスト</c> としてキャッシュする
    /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加、v1.3.0 続編 第 N+3 弾で
    /// 単一文字列 → 構造化 DTO リストに大幅変更）。
    /// <para>
    /// 旧仕様は「担当エピソード数が最多の 1 名」だけを抽出し「プロデューサー ○○／シリーズ構成 ○○／...」の
    /// 単一文字列として返していた。テンプレ側で <c>html.escape</c> されるためリンク不可、所属屋号も削られ、
    /// 連名スタッフが消えるなど情報量が大幅に欠落していた。
    /// </para>
    /// <para>
    /// 新仕様：シリーズ詳細の <see cref="BuildMainStaffSectionsAsync"/> と同じ役職セット 5 種について、
    /// 各役職に該当する全人物を「担当エピソード数 desc → kana asc」順で集めて
    /// <see cref="KeyStaffMember"/> として並べる。所属屋号（<c>Involvement.AffiliationCompanyAliasId</c>）も
    /// 当該シリーズ内最頻のものを引き当てて添える。テンプレ側では役職バッジ（色付き）+ 名前リンク + 所属屋号
    /// の構造で描画される。
    /// </para>
    /// <para>役職コードと色マッピング（CSS 側 <c>data-role-code</c> セレクタと対応）：</para>
    /// <list type="bullet">
    ///   <item><description><c>PRODUCER</c>           → 紫</description></item>
    ///   <item><description><c>SERIES_COMPOSITION</c> → 青</description></item>
    ///   <item><description><c>SERIES_DIRECTOR</c>    → ピンク</description></item>
    ///   <item><description><c>CHARACTER_DESIGN</c>   → 緑</description></item>
    ///   <item><description><c>ART_DESIGN</c>         → 黄</description></item>
    /// </list>
    /// </summary>
    private async Task BuildKeyStaffSummaryBySeriesCacheAsync(CancellationToken ct)
    {
        // 集計対象の役職 5 種。シリーズ詳細メインスタッフセクション順と同じ並び。
        var roleSpecs = new (string Code, string Label)[]
        {
            ("PRODUCER",            "プロデューサー"),
            ("SERIES_COMPOSITION",  "シリーズ構成"),
            ("SERIES_DIRECTOR",     "シリーズディレクター"),
            ("CHARACTER_DESIGN",    "キャラクターデザイン"),
            ("ART_DESIGN",          "美術デザイン")
        };

        // 人物マスタと alias 群キャッシュ（シリーズ詳細用の BuildMainStaffSectionsAsync と共通）。
        _allPersonsCache ??= await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        if (_aliasIdsByPersonIdCache is null)
        {
            var dict0 = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var p in _allPersonsCache)
            {
                var rows = await _personAliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
                dict0[p.PersonId] = rows.Select(r => r.AliasId).ToList();
            }
            _aliasIdsByPersonIdCache = dict0;
        }

        // 屋号 alias_id → 名前 マップを 1 度だけロード（所属屋号ラベル解決用）。
        if (_companyAliasNameMapCache is null)
        {
            var allAliases = await _companyAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
            _companyAliasNameMapCache = allAliases
                .ToDictionary(a => a.AliasId, a => a.Name);
        }

        var summaryDict = new Dictionary<int, IReadOnlyList<KeyStaffRoleGroup>>();
        foreach (var s in _ctx.Series)
        {
            // TV シリーズのみ対象（映画・スピンオフ系はシリーズ一覧の見出しから自明）。
            if (!string.Equals(s.KindCode, "TV", StringComparison.Ordinal)) continue;
            if (!_ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            var epNoByEpId = eps.ToDictionary(e => e.EpisodeId, e => e.SeriesEpNo);

            var groups = new List<KeyStaffRoleGroup>();
            foreach (var spec in roleSpecs)
            {
                // 当該役職に該当する人物を全員集める。
                // 各人物について：担当エピソード集合・所属屋号 alias_id 出現回数辞書 を作る。
                var members = new List<KeyStaffMember>();
                foreach (var p in _allPersonsCache)
                {
                    if (!_aliasIdsByPersonIdCache.TryGetValue(p.PersonId, out var aliasIds)) continue;

                    var nos = new HashSet<int>();
                    var affiliationCounts = new Dictionary<int, int>();
                    foreach (var aid in aliasIds)
                    {
                        if (!_involvementIndex.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                        foreach (var inv in invs)
                        {
                            if (inv.SeriesId != s.SeriesId) continue;
                            if (!string.Equals(inv.RoleCode, spec.Code, StringComparison.Ordinal)) continue;
                            // 担当エピソード集合
                            if (inv.EpisodeId is int eid && epNoByEpId.TryGetValue(eid, out var n))
                                nos.Add(n);
                            // 所属屋号 alias_id の出現回数
                            if (inv.AffiliationCompanyAliasId is int affId)
                            {
                                affiliationCounts.TryGetValue(affId, out var c);
                                affiliationCounts[affId] = c + 1;
                            }
                        }
                    }
                    if (nos.Count == 0) continue;

                    // 最頻所属屋号（同点は alias_id 昇順でタイブレーク）。
                    string affiliationLabel = "";
                    if (affiliationCounts.Count > 0)
                    {
                        var bestAffId = affiliationCounts
                            .OrderByDescending(kv => kv.Value)
                            .ThenBy(kv => kv.Key)
                            .First().Key;
                        if (_companyAliasNameMapCache.TryGetValue(bestAffId, out var nm))
                            affiliationLabel = nm;
                    }

                    members.Add(new KeyStaffMember
                    {
                        PersonId = p.PersonId,
                        DisplayName = p.FullName ?? "",
                        AffiliationLabel = affiliationLabel,
                        EpisodeCount = nos.Count,
                        SortKey = p.FullNameKana ?? p.FullName ?? ""
                    });
                }

                if (members.Count == 0) continue;

                // 担当エピソード数 desc → kana asc → DisplayName Ordinal asc でソート。
                members.Sort((a, b) =>
                {
                    int c = b.EpisodeCount.CompareTo(a.EpisodeCount);
                    if (c != 0) return c;
                    int k = string.CompareOrdinal(a.SortKey, b.SortKey);
                    if (k != 0) return k;
                    return string.CompareOrdinal(a.DisplayName, b.DisplayName);
                });

                groups.Add(new KeyStaffRoleGroup
                {
                    RoleCode = spec.Code,
                    RepRoleCode = _roleSuccessorResolver.GetRepresentative(spec.Code),
                    RoleLabel = spec.Label,
                    Members = members
                });
            }

            if (groups.Count > 0)
                summaryDict[s.SeriesId] = groups;
        }
        _keyStaffSummaryBySeriesCache = summaryDict;
    }

    /// <summary>
    /// 指定シリーズのメインスタッフサマリ（役職グループのリスト）を返す。
    /// データ無しのときは空リストを返す（v1.3.0 続編 第 N+3 弾で戻り値型を変更）。
    /// </summary>
    private IReadOnlyList<KeyStaffRoleGroup> GetKeyStaffSummary(int seriesId)
    {
        if (_keyStaffSummaryBySeriesCache is null) return Array.Empty<KeyStaffRoleGroup>();
        return _keyStaffSummaryBySeriesCache.TryGetValue(seriesId, out var g)
            ? g
            : Array.Empty<KeyStaffRoleGroup>();
    }

    /// <summary>
    /// /episodes/ ランディングページ生成（<see cref="EpisodesIndexGenerator"/>）から参照される、
    /// エピソード単位 staff サマリの memoize キャッシュ
    /// （v1.3.0 続編 第 N+3 弾で追加）。
    /// <para>
    /// 本キャッシュは <see cref="ExtractStaffSummaryAsync"/> を経由したときだけ詰められる。
    /// <see cref="GenerateDetailAsync"/> が各 TV シリーズの全エピソードについて
    /// <c>ExtractStaffSummaryAsync</c> を呼ぶため、<see cref="GenerateAsync"/> 完了後は
    /// クレジット添付対象（credit_attach_to=EPISODE）のシリーズ配下エピソード全件が揃っている。
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<int, EpisodeStaffSummary> GetEpisodeStaffSummaries()
        => _episodeStaffByIdCache;

    /// <summary>
    /// 指定 kind_code の単純な行リスト（TV / OTONA / SHORT / EVENT / SPIN-OFF 等の
    /// 1 行 1 シリーズの素直なテーブル用）を組み立てる共通ヘルパ
    /// （v1.3.0 公開直前のデザイン整理第 3 弾で追加、スピンオフ 4 種別の細分化と TV 用に共通化）。
    /// 並び順は放送開始日（または公開日）昇順 → series_id 昇順でタイブレーク。
    /// <para>
    /// 各行に「複合行サブ情報」のプリキュア欄を併せて詰める
    /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
    /// シリーズに紐付くプリキュアが居れば、変身後名と声優名を併記したコンパクト表示を作る。
    /// 居なければサブ情報は空文字でテンプレ側がサブ行自体を出さない。
    /// </para>
    /// </summary>
    /// <param name="kindCode">対象シリーズ種別コード（例 "OTONA"）。完全一致のみ。</param>
    private IReadOnlyList<TvSeriesRow> BuildSimpleRowsByKind(string kindCode)
    {
        return _ctx.Series
            .Where(s => string.Equals(s.KindCode, kindCode, StringComparison.Ordinal))
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .Select(s =>
            {
                var precureRows = GetPrecureRows(s.SeriesId);
                return new TvSeriesRow
                {
                    Slug = s.Slug,
                    Title = s.Title,
                    Period = FormatPeriod(s.StartDate, s.EndDate),
                    EpisodesLabel = s.Episodes.HasValue ? $"全 {s.Episodes.Value} 話" : "",
                    PrecureSummary = BuildPrecureSummaryLabel(precureRows),
                    KeyStaffSummary = GetKeyStaffSummary(s.SeriesId)
                };
            })
            .ToList();
    }

    /// <summary>
    /// シリーズ一覧の複合行サブ情報用：プリキュア群を「キュアブラック (CV: 本名陽子) / キュアホワイト (CV: ゆかな)」
    /// 形式の 1 文字列にまとめる。0 件のときは空文字を返す（テンプレ側でサブ行非表示の判定に使う）。
    /// </summary>
    private static string BuildPrecureSummaryLabel(IReadOnlyList<SeriesPrecureDisplay> rows)
    {
        if (rows.Count == 0) return "";
        var parts = new List<string>(rows.Count);
        foreach (var r in rows)
        {
            // 変身後名（キュア○○）を主表示、声優は括弧書きで補足。声優が未登録なら名前のみ。
            if (!string.IsNullOrEmpty(r.VoiceActorName))
                parts.Add($"{r.TransformName} (CV: {r.VoiceActorName})");
            else
                parts.Add(r.TransformName);
        }
        return string.Join(" / ", parts);
    }

    /// <summary>
    /// 子作品判定：<c>kind_code == 'MOVIE_SHORT'</c> のものを子作品扱いとする
    /// （v1.3.0 公開直前の整理第 2 弾で仕様明確化）。
    /// 子作品は単独詳細ページを生成せず、親映画詳細の「併映・子作品」セクションに表示するのみ。
    /// 'MOVIE_SHORT' 以外（'TV' / 'MOVIE' / 'SPRING' / 'OTONA' / 'SHORT' / 'EVENT' / 'SPIN-OFF'）は
    /// すべて親として扱い、それぞれ単独詳細ページを持つ。
    /// </summary>
    private static bool IsChildOfMovie(Series s)
        => string.Equals(s.KindCode, "MOVIE_SHORT", StringComparison.Ordinal);

    /// <summary>
    /// 映画系シリーズ判定。MOVIE（秋映画）／SPRING（春映画）／MOVIE_SHORT（秋映画併映短編）の 3 種。
    /// 一覧の映画セクションでまとめて扱う対象になる
    /// （親としての映画＝MOVIE/SPRING、子としての映画＝MOVIE_SHORT）。
    /// </summary>
    private static bool IsMovieKind(string kindCode)
        => string.Equals(kindCode, "MOVIE",       StringComparison.Ordinal)
        || string.Equals(kindCode, "SPRING",      StringComparison.Ordinal)
        || string.Equals(kindCode, "MOVIE_SHORT", StringComparison.Ordinal);

    /// <summary>
    /// シーズンバッジの CSS クラス名を返す。
    /// MOVIE（秋映画）→ "movie-badge-fall"、SPRING（春映画）→ "movie-badge-spring"。
    /// MOVIE_SHORT 等は親映画の下に出る子作品扱いなので親側のロジックでは判定されない。
    /// 該当無しのときは空文字（バッジを出さない）。
    /// </summary>
    private static string GetSeasonBadgeClass(string kindCode) => kindCode switch
    {
        "MOVIE"  => "movie-badge-fall",
        "SPRING" => "movie-badge-spring",
        _        => string.Empty
    };

    /// <summary>シーズンバッジに表示するラベル文字列。MOVIE→「秋映画」、SPRING→「春映画」。</summary>
    private static string GetSeasonBadgeLabel(string kindCode) => kindCode switch
    {
        "MOVIE"  => "秋映画",
        "SPRING" => "春映画",
        _        => string.Empty
    };

    /// <summary>
    /// <c>/series/</c> の索引ページ。TV / 映画 / スピンオフ の 3 セクション構成。
    /// </summary>
    private void GenerateIndex()
    {
        // TV シリーズだけを抽出（放送順）。
        // 旧仕様では TV 行の下に併映短編や子映画を字下げ表示していたが、現仕様では純粋な TV のみとし、
        // 子作品は映画セクションで親映画にぶら下げる方式に変更。
        // 第 3 弾で OTONA / SHORT / EVENT / SPIN-OFF と共通の組み立て処理に揃えた。
        var tvRows = BuildSimpleRowsByKind("TV");

        // 映画セクションの「子作品」候補は kind_code='MOVIE_SHORT' のみ
        // （v1.3.0 公開直前のデザイン整理第 2 弾で仕様確定）。
        // 親 ID（ParentSeriesId）でグルーピングし、seq_in_parent 昇順で並べる
        // （seq_in_parent が NULL の場合は末尾に回す）。
        // 旧仕様では「parent_series_id を持つ MOVIE / SPRING」も子作品として字下げ表示していたが、
        // 新仕様では MOVIE / SPRING はすべて親作品として独立に並べ、子作品は MOVIE_SHORT に限定する。
        var movieShortByParent = _ctx.Series
            .Where(s => s.KindCode == "MOVIE_SHORT" && s.ParentSeriesId.HasValue)
            .GroupBy(s => s.ParentSeriesId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(c => c.SeqInParent ?? byte.MaxValue)
                    .ThenBy(c => c.SeriesId)
                    .ToList());

        // 映画セクションの「親作品」候補は kind_code IN ('MOVIE','SPRING') 全て
        // （v1.3.0 公開直前のデザイン整理第 2 弾で仕様確定）。
        // 親シリーズ（ParentSeriesId）の有無に関係なく、MOVIE / SPRING はすべて親として独立に並べる。
        // 旧仕様では「TV を親とする MOVIE は TV の下に字下げ表示」していたが、これは廃止し、
        // 映画は常にこのセクション内で独立した親作品として登場する。
        var movieRows = _ctx.Series
            .Where(s => s.KindCode == "MOVIE" || s.KindCode == "SPRING")
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .Select(m =>
            {
                var children = movieShortByParent.TryGetValue(m.SeriesId, out var kids)
                    ? kids
                    : new List<Series>();

                // 親 + 全 子（MOVIE_SHORT）の run_time_seconds 合計を「m分ss秒」で表示する。
                // 親自身または子のいずれかに NULL（未登録）が混じる場合は合計を出さない
                // （列を空文字で返してテンプレ側で空欄表示）。TV 行の「全N話」と同じ列位置に置く。
                string runtimeLabel = "";
                bool anyNull = !m.RunTimeSeconds.HasValue
                    || children.Any(c => !c.RunTimeSeconds.HasValue);
                if (!anyNull)
                {
                    int totalSec = (int)m.RunTimeSeconds!.Value
                        + children.Sum(c => (int)c.RunTimeSeconds!.Value);
                    int min = totalSec / 60;
                    int sec = totalSec % 60;
                    runtimeLabel = $"{min}分{sec}秒";
                }

                return new MovieSeriesRow
                {
                    Slug = m.Slug,
                    Title = m.Title,
                    Period = FormatPeriod(m.StartDate, m.EndDate),
                    SeasonBadgeClass = GetSeasonBadgeClass(m.KindCode),
                    SeasonBadgeLabel = GetSeasonBadgeLabel(m.KindCode),
                    RuntimeLabel = runtimeLabel,
                    Children = children
                        .Select(c => new RelatedSeriesRow
                        {
                            Slug = c.Slug,
                            Title = c.Title,
                            KindLabel = LookupKindLabel(c.KindCode),
                            // 公開日は親映画と同じ運用のため、子作品行では出さない（カラム自体は空）。
                            Period = "",
                            HasOwnPage = false
                        })
                        .ToList()
                };
            })
            .ToList();

        // スピンオフ系セクション（v1.3.0 公開直前のデザイン整理第 3 弾でスピンオフを 4 種別に細分化）。
        // 映画セクションの下に OTONA → SHORT → EVENT → SPIN-OFF の順で並べる。
        // 各セクション内では放送開始日（または公開日）昇順 → series_id 昇順でタイブレーク。
        // 行 DTO は TV と共通の TvSeriesRow を流用（表形式・連番付与スタイルが同じため）。
        // 空のセクションはテンプレ側で表示自体を省略するので、ここでは件数を絞らず素直に kind_code で分けるだけでよい。
        var otonaRows    = BuildSimpleRowsByKind("OTONA");
        var shortRows    = BuildSimpleRowsByKind("SHORT");
        var eventRows    = BuildSimpleRowsByKind("EVENT");
        var spinOffRows  = BuildSimpleRowsByKind("SPIN-OFF");

        var content = new SeriesIndexModel
        {
            TvSeries = tvRows,
            MovieSeries = movieRows,
            OtonaSeries = otonaRows,
            ShortSeries = shortRows,
            EventSeries = eventRows,
            SpinOffSeries = spinOffRows,
            TotalCount = _ctx.Series.Count,
            CoverageLabel = _ctx.CreditCoverageLabel
        };
        var layout = new LayoutModel
        {
            PageTitle = "シリーズ一覧",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "シリーズ一覧", Url = "" }
            }
        };
        _page.RenderAndWrite("/series/", "series", "series-index.sbn", content, layout);
    }

    /// <summary>
    /// <c>/series/{slug}/</c> 個別シリーズページ。
    /// </summary>
    private async Task GenerateDetailAsync(Series s, CancellationToken ct)
    {
        bool hasEpisodes = false;
        if (_ctx.SeriesKindByCode.TryGetValue(s.KindCode, out var kind))
        {
            hasEpisodes = string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal);
        }

        var eps = _ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var list) ? list : Array.Empty<Episode>();
        var epRows = new List<EpisodeIndexRow>();
        if (hasEpisodes)
        {
            if (_roleMap is null)
            {
                var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
                _roleMap = allRoles.ToDictionary(r => r.RoleCode, r => r, StringComparer.Ordinal);
            }

            foreach (var e in eps.OrderBy(x => x.SeriesEpNo))
            {
                var staff = await ExtractStaffSummaryAsync(e.EpisodeId, ct).ConfigureAwait(false);
                epRows.Add(new EpisodeIndexRow
                {
                    SeriesEpNo = e.SeriesEpNo,
                    TitleText = e.TitleText,
                    OnAirDate = FormatJpDate(e.OnAirAt),
                    Screenplay = staff.Screenplay,
                    Storyboard = staff.Storyboard,
                    EpisodeDirector = staff.EpisodeDirector,
                    AnimationDirector = staff.AnimationDirector,
                    ArtDirector = staff.ArtDirector,
                    // v1.3.0 続編：絵コンテと演出が同一人物（PERSON エントリのキー集合が一致 + どちらも非空）のとき、
                    // テンプレ側で「絵コンテ・演出 ○○」の 1 表記に統合する。同一性判定は集合比較で行う。
                    StoryboardDirectorMerged = staff.StoryboardDirectorMerged
                });
            }
        }

        // 関連シリーズ（自分が親で、配下にいる作品）を 2 つのカテゴリに分ける（v1.3.0 続編で分離）。
        // ・「併映・子作品」(<see cref="RelatedAsChildren"/>) ：IsChildOfMovie が true のもの。
        //   主に映画系（MOVIE_SHORT＝秋映画併映短編）が該当。単独ページを持たないリンクなしテキスト表示。
        // ・「関連作品」(<see cref="RelatedAsSiblings"/>) ：それ以外の親子関係子作品。
        //   TV シリーズの続編・スピンオフ（SPIN-OFF）・大人向け（OTONA）など、単独ページを持つ作品。
        //   読者はこちらを「シリーズの広がり」として読みたいので、別セクションに分けて見せる。
        var allRelated = _ctx.Series
            .Where(x => x.ParentSeriesId == s.SeriesId)
            .OrderBy(x => x.StartDate)
            .ToList();
        var relatedChildren = allRelated
            .Where(IsChildOfMovie)
            .Select(x => new RelatedSeriesRow
            {
                Slug = x.Slug,
                Title = x.Title,
                KindLabel = LookupKindLabel(x.KindCode),
                Period = FormatPeriod(x.StartDate, x.EndDate),
                HasOwnPage = false
            })
            .ToList();
        var relatedSiblings = allRelated
            .Where(x => !IsChildOfMovie(x))
            .Select(x => new RelatedSeriesRow
            {
                Slug = x.Slug,
                Title = x.Title,
                KindLabel = LookupKindLabel(x.KindCode),
                Period = FormatPeriod(x.StartDate, x.EndDate),
                HasOwnPage = true
            })
            .ToList();

        // 親シリーズへのリンク。自分が子作品の場合は親への戻るリンクとして使う想定だが、
        // そもそも子作品は単独ページを生成しないのでここに到達するのは SPIN-OFF などのみ。
        RelatedSeriesRow? parent = null;
        if (s.ParentSeriesId is int pid && _ctx.SeriesById.TryGetValue(pid, out var p))
        {
            parent = new RelatedSeriesRow
            {
                Slug = p.Slug,
                Title = p.Title,
                KindLabel = LookupKindLabel(p.KindCode),
                Period = FormatPeriod(p.StartDate, p.EndDate),
                HasOwnPage = !IsChildOfMovie(p)
            };
        }

        var seriesView = new SeriesDetailView
        {
            Slug = s.Slug,
            Title = s.Title,
            TitleKana = s.TitleKana ?? "",
            TitleEn = s.TitleEn ?? "",
            KindLabel = LookupKindLabel(s.KindCode),
            Period = FormatPeriod(s.StartDate, s.EndDate),
            Episodes = s.Episodes?.ToString() ?? "",
            RunTimeSeconds = s.RunTimeSeconds?.ToString() ?? "",
            ToeiAnimOfficialSiteUrl = s.ToeiAnimOfficialSiteUrl ?? "",
            ToeiAnimLineupUrl = s.ToeiAnimLineupUrl ?? "",
            AbcOfficialSiteUrl = s.AbcOfficialSiteUrl ?? "",
            AmazonPrimeDistributionUrl = s.AmazonPrimeDistributionUrl ?? "",
            HasEpisodeList = hasEpisodes
        };
        seriesView.HasExternalUrls =
            seriesView.ToeiAnimOfficialSiteUrl.Length > 0 ||
            seriesView.ToeiAnimLineupUrl.Length > 0 ||
            seriesView.AbcOfficialSiteUrl.Length > 0 ||
            seriesView.AmazonPrimeDistributionUrl.Length > 0;

        // メインスタッフセクション（v1.3.0 ブラッシュアップ）。
        var keyStaffSections = await BuildMainStaffSectionsAsync(s, eps, ct).ConfigureAwait(false);

        // 当該シリーズに紐付くプリキュア一覧
        // （v1.3.0 公開直前のデザイン整理第 4 弾で追加、series_precures テーブルから取得）。
        // 表示行は事前にキャッシュ化された SeriesPrecureDisplay を SeriesPrecureRow に詰め替える
        // （DTO 分離はテンプレ用と内部用で同じ形でも責務を明確に分けるため）。
        var precureRows = GetPrecureRows(s.SeriesId)
            .Select(p => new SeriesPrecureRow
            {
                PrecureId = p.PrecureId,
                TransformName = p.TransformName,
                PreTransformName = p.PreTransformName,
                VoiceActorName = p.VoiceActorName,
                VoiceActorPersonId = p.VoiceActorPersonId
            })
            .ToList();

        var content = new SeriesDetailModel
        {
            Series = seriesView,
            Episodes = epRows,
            RelatedChildren = relatedChildren,
            RelatedSiblings = relatedSiblings,
            Parent = parent,
            KeyStaffSections = keyStaffSections,
            Precures = precureRows,
            CoverageLabel = _ctx.CreditCoverageLabel
        };

        // 説明文を実データから動的構築する（v1.3.1 stage3 改修）。
        // 「シリーズ名・放送開始年・話数・主役プリキュア声優」を含めて、SERP / OGP / Twitter Card で
        // シリーズ単位の個別性が立つようにする。140 字目安。
        var metaDescription = BuildSeriesMetaDescription(s, precureRows);

        // JSON-LD（TVSeries / Movie）
        // v1.3.0 stage22 後段：略称（series.title_short）は生成・UI ともに一切使わない方針。
        // 旧来は alternateName に title_short を出していたが、本工程で出力を撤去した。
        // v1.3.1 stage3：description を MetaDescription と揃え、actor 配列に主役プリキュア声優を、
        // genre に「アニメ」を載せて TVSeries / Movie の構造化データを拡充する。
        string baseUrl = _ctx.Config.BaseUrl;
        string seriesUrl = PathUtil.SeriesUrl(s.Slug);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = s.KindCode == "MOVIE" ? "Movie" : "TVSeries",
            ["name"] = s.Title,
            ["description"] = metaDescription,
            ["startDate"] = s.StartDate.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja",
            ["genre"] = "アニメ"
        };
        if (s.EndDate.HasValue) jsonLdDict["endDate"] = s.EndDate.Value.ToString("yyyy-MM-dd");
        if (s.Episodes.HasValue) jsonLdDict["numberOfEpisodes"] = s.Episodes.Value;
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + seriesUrl;

        // 主役プリキュアの声優を Person 型配列で actor プロパティに乗せる（v1.3.1 stage3 追加）。
        // 声優名が登録されている行のみ採用し、空・null は除外。重複（兼役）は1人に集約する。
        // 個別人物詳細ページの URL が引けるなら sameAs として併記する想定もあるが、
        // 本リビジョンでは name と @type のみのシンプル構成に留める。
        var actors = precureRows
            .Select(p => p.VoiceActorName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .Select(n => new Dictionary<string, object?>
            {
                ["@type"] = "Person",
                ["name"] = n
            })
            .ToArray();
        if (actors.Length > 0) jsonLdDict["actor"] = actors;

        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = s.Title,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "シリーズ一覧", Url = "/series/" },
                new BreadcrumbItem { Label = s.Title, Url = "" }
            },
            OgType = s.KindCode == "MOVIE" ? "video.movie" : "video.tv_show",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite(seriesUrl, "series", "series-detail.sbn", content, layout);
    }

    /// <summary>
    /// シリーズ詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる
    /// （v1.3.1 stage3 追加）。
    /// <para>
    /// 構成：「『{シリーズ}』({YYYY年}放送開始、全N話)。主役プリキュア：{変身名}({CV})、{変身名}({CV})ほか。
    /// プリキュアシリーズのエピソード・スタッフ・楽曲を網羅したデータベース。」を骨格に、
    /// 各セグメント追加前に <c>targetMaxChars=140</c> を超えないかを確認、超えそうな段で打ち切る。
    /// 映画作品は放送年表記を「公開」に切り替える。
    /// </para>
    /// </summary>
    private static string BuildSeriesMetaDescription(
        Series s,
        IReadOnlyList<SeriesPrecureRow> precureRows)
    {
        const int targetMaxChars = 140;

        var sb = new System.Text.StringBuilder();
        // ① 基本：『シリーズ名』(YYYY年放送開始/公開、全N話)
        // 映画系（KindCode が "MOVIE" / "MOVIE_SHORT" / "SPRING" 等）は「公開」表記、それ以外は「放送開始」。
        bool isMovie = s.KindCode == "MOVIE" || s.KindCode == "MOVIE_SHORT" || s.KindCode == "SPRING";
        sb.Append('『').Append(s.Title).Append("』(")
          .Append(s.StartDate.Year).Append('年')
          .Append(isMovie ? "公開" : "放送開始");
        if (s.Episodes.HasValue && s.Episodes.Value > 0 && !isMovie)
        {
            sb.Append("、全").Append(s.Episodes.Value).Append('話');
        }
        sb.Append(")。");

        // ② 主役プリキュア声優（最大 2 名）。
        // precureRows は SeriesGenerator がプリキュア紐付けの順序で詰めている前提（主役→脇役の順）。
        // VoiceActorName が空の行はスキップ。TransformName + VoiceActorName のペアを「変身名(CV)」で並べる。
        var precuresForDesc = precureRows
            .Where(p => !string.IsNullOrWhiteSpace(p.TransformName) && !string.IsNullOrWhiteSpace(p.VoiceActorName))
            .Take(2)
            .ToList();
        if (precuresForDesc.Count > 0)
        {
            sb.Append("主役プリキュア：");
            for (int i = 0; i < precuresForDesc.Count; i++)
            {
                var p = precuresForDesc[i];
                var fragment = $"{p.TransformName}({p.VoiceActorName})";
                // 末尾「ほか。」分（4 字）+ 既存末尾の区切り分も考慮して、超過しそうなら打ち切り。
                if (sb.Length + fragment.Length + 4 > targetMaxChars) break;
                if (i > 0) sb.Append('、');
                sb.Append(fragment);
            }
            // 一覧から削った主役がいるならば「ほか」を、無いならピリオドのみ。
            if (precureRows.Count(p => !string.IsNullOrWhiteSpace(p.VoiceActorName)) > precuresForDesc.Count)
            {
                if (sb.Length + 3 <= targetMaxChars) sb.Append("ほか");
            }
            sb.Append('。');
        }

        // ③ 締めの定型文（サイトの位置付け文。140 字に収まる限りで足す）。
        const string suffix = "プリキュアシリーズのエピソード・スタッフ・楽曲を網羅したデータベース。";
        if (sb.Length + suffix.Length <= targetMaxChars)
        {
            sb.Append(suffix);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 指定エピソードのクレジット階層から、脚本・絵コンテ・演出・作画監督・美術の人物名を引く。
    /// <para>
    /// v1.3.0 続編 第 N+3 弾：本メソッドの戻り値を <see cref="_episodeStaffByIdCache"/> に memoize するように変更。
    /// 同一エピソードで複数回呼ばれた場合はキャッシュから返す（実際にはシリーズ詳細ページ生成での 1 回のみだが、
    /// パイプライン後段 <see cref="EpisodesIndexGenerator"/> から <see cref="GetEpisodeStaffSummaries"/> 経由で
    /// 全エピソード分のサマリを参照させるための副次効果が主目的）。
    /// </para>
    /// </summary>
    private async Task<EpisodeStaffSummary> ExtractStaffSummaryAsync(int episodeId, CancellationToken ct)
    {
        // 同一 episode_id について 2 回目以降の呼び出しはキャッシュから返す（メモ化）。
        if (_episodeStaffByIdCache.TryGetValue(episodeId, out var cached)) return cached;

        var screenplay = new List<string>();
        var storyboard = new List<string>();
        var director = new List<string>();
        var animDirector = new List<string>();
        var artDirector = new List<string>();
        var seenSc = new HashSet<string>(StringComparer.Ordinal);
        var seenSb = new HashSet<string>(StringComparer.Ordinal);
        var seenDr = new HashSet<string>(StringComparer.Ordinal);
        var seenAd = new HashSet<string>(StringComparer.Ordinal);
        var seenAt = new HashSet<string>(StringComparer.Ordinal);

        int? ClassifyRole(CreditCardRole cr)
        {
            if (cr.RoleCode is null) return null;
            if (!_roleMap!.TryGetValue(cr.RoleCode, out var role)) return null;
            string code = cr.RoleCode;
            string nm = role.NameJa ?? "";
            if (code == "SCREENPLAY"          || nm == "脚本")     return 1;
            if (code == "STORYBOARD"          || nm == "絵コンテ") return 2;
            if (code == "EPISODE_DIRECTOR"    || nm == "演出")     return 3;
            if (code == "ANIMATION_DIRECTOR"  || nm == "作画監督") return 4;
            if (code == "ART_DIRECTOR"        || nm == "美術")     return 5;
            return null;
        }

        var credits = (await _creditsRepo.GetByEpisodeAsync(episodeId, ct).ConfigureAwait(false))
            .Where(c => !c.IsDeleted)
            .ToList();

        foreach (var credit in credits)
        {
            var cards = (await _staffCardsRepo.GetByCreditAsync(credit.CreditId, ct).ConfigureAwait(false))
                .OrderBy(c => c.CardSeq);
            foreach (var card in cards)
            {
                var tiers = (await _staffTiersRepo.GetByCardAsync(card.CardId, ct).ConfigureAwait(false))
                    .OrderBy(t => t.TierNo);
                foreach (var tier in tiers)
                {
                    var groups = (await _staffGroupsRepo.GetByTierAsync(tier.CardTierId, ct).ConfigureAwait(false))
                        .OrderBy(g => g.GroupNo);
                    foreach (var grp in groups)
                    {
                        var cardRoles = (await _staffCardRolesRepo.GetByGroupAsync(grp.CardGroupId, ct).ConfigureAwait(false))
                            .OrderBy(r => r.OrderInGroup);
                        foreach (var cr in cardRoles)
                        {
                            int? bucket = ClassifyRole(cr);
                            if (bucket is null) continue;

                            var blocks = (await _staffBlocksRepo.GetByCardRoleAsync(cr.CardRoleId, ct).ConfigureAwait(false))
                                .OrderBy(b => b.BlockSeq);
                            foreach (var b in blocks)
                            {
                                var entries = (await _staffEntriesRepo.GetByBlockAsync(b.BlockId, ct).ConfigureAwait(false))
                                    .Where(e => !e.IsBroadcastOnly)
                                    .OrderBy(e => e.EntrySeq);
                                foreach (var e in entries)
                                {
                                    var (key, html) = await ResolveStaffEntryAsync(e, ct).ConfigureAwait(false);
                                    if (string.IsNullOrEmpty(html)) continue;
                                    switch (bucket)
                                    {
                                        case 1: if (seenSc.Add(key)) screenplay.Add(html); break;
                                        case 2: if (seenSb.Add(key)) storyboard.Add(html); break;
                                        case 3: if (seenDr.Add(key)) director.Add(html); break;
                                        case 4: if (seenAd.Add(key)) animDirector.Add(html); break;
                                        case 5: if (seenAt.Add(key)) artDirector.Add(html); break;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        var result = new EpisodeStaffSummary
        {
            Screenplay        = string.Join("、", screenplay),
            Storyboard        = string.Join("、", storyboard),
            EpisodeDirector   = string.Join("、", director),
            AnimationDirector = string.Join("、", animDirector),
            ArtDirector       = string.Join("、", artDirector),
            // v1.3.0 続編：絵コンテ／演出のキー集合が一致（両方非空＋同一）のとき、テンプレで統合表示を許可するフラグ。
            // 同じ人物が両方を兼任しているクレジット表現に対して「絵コンテ・演出 ○○」の 1 表記にまとめる。
            StoryboardDirectorMerged = seenSb.Count > 0 && seenDr.Count > 0 && seenSb.SetEquals(seenDr)
        };
        // v1.3.0 続編 第 N+3 弾：本メソッドの結果を episode_id キーでキャッシュ。
        // 後段 EpisodesIndexGenerator で GetEpisodeStaffSummaries() 経由参照される。
        _episodeStaffByIdCache[episodeId] = result;
        return result;
    }

    /// <summary>
    /// PERSON / TEXT エントリから (重複判定キー, 表示用 HTML 文字列) を取り出す。
    /// </summary>
    private async Task<(string Key, string Html)> ResolveStaffEntryAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    if (!_personAliasCache.TryGetValue(pid, out var pa))
                    {
                        pa = await _personAliasesRepo.GetByIdAsync(pid, ct).ConfigureAwait(false);
                        _personAliasCache[pid] = pa;
                    }
                    string? displayText = pa?.DisplayTextOverride ?? pa?.Name;
                    if (string.IsNullOrEmpty(displayText)) return ("", "");
                    string html = _staffLinkResolver.ResolveAsHtml(pid, displayText);
                    return ($"P:{pid}", html);
                }
                return ("", "");
            case "TEXT":
                {
                    string raw = e.RawText ?? "";
                    if (string.IsNullOrEmpty(raw)) return ("", "");
                    string html = _staffLinkResolver.ResolveAsHtml(null, raw);
                    return ($"T:{raw}", html);
                }
            default:
                return ("", "");
        }
    }

    /// <summary>kind_code → 表示用ラベル（name_ja）。</summary>
    private string LookupKindLabel(string code)
        => _ctx.SeriesKindByCode.TryGetValue(code, out var kind) ? kind.NameJa : code;

    /// <summary>放送・公開期間を「2004年2月1日 〜 2005年1月30日」で返す。</summary>
    private static string FormatPeriod(DateOnly start, DateOnly? end)
    {
        string startStr = $"{start.Year}年{start.Month}月{start.Day}日";
        if (end.HasValue)
        {
            var e = end.Value;
            return $"{startStr} 〜 {e.Year}年{e.Month}月{e.Day}日";
        }
        return startStr;
    }

    /// <summary>
    /// 放送日を「2024.2.4」形式で返す（v1.3.0 続編 第 N+3 弾で短縮形式に変更）。
    /// シリーズ詳細 dl.ep-list が ep-row レイアウト共通化に合わせて、密表示用の短い表記に統一。
    /// /episodes/ ランディングの <c>EpisodesIndexGenerator.FormatCompactDate</c> と表記揃え。
    /// </summary>
    private static string FormatJpDate(DateTime dt)
        => $"{dt.Year}.{dt.Month}.{dt.Day}";

    /// <summary>
    /// メインスタッフセクション群を構築する。
    /// 5 役職：PRODUCER / SERIES_COMPOSITION / SERIES_DIRECTOR / CHARACTER_DESIGN / ART_DESIGN。
    /// 担当話数による足切りは無し（1 話でも掲載）。
    /// 全話担当者は名前のみ、部分担当者は「名前 (#1〜4, 8)」表記。
    /// </summary>
    private async Task<IReadOnlyList<KeyStaffSection>> BuildMainStaffSectionsAsync(
        Series series, IReadOnlyList<Episode> eps, CancellationToken ct)
    {
        _seriesKindMapCache ??= (await _seriesKindsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        if (!_seriesKindMapCache.TryGetValue(series.KindCode, out var kind)) return Array.Empty<KeyStaffSection>();
        if (!string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal)) return Array.Empty<KeyStaffSection>();

        if (_allPersonsCache is null)
            _allPersonsCache = await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        if (_aliasIdsByPersonIdCache is null)
        {
            var dict = new Dictionary<int, IReadOnlyList<int>>();
            foreach (var p in _allPersonsCache)
            {
                var rows = await _personAliasPersonsRepo.GetByPersonAsync(p.PersonId, ct).ConfigureAwait(false);
                dict[p.PersonId] = rows.Select(r => r.AliasId).ToList();
            }
            _aliasIdsByPersonIdCache = dict;
        }

        var roleSpecs = new (string Code, string Label)[]
        {
            ("PRODUCER",            "プロデューサー"),
            ("SERIES_COMPOSITION",  "シリーズ構成"),
            ("SERIES_DIRECTOR",     "シリーズディレクター"),
            ("CHARACTER_DESIGN",    "キャラクターデザイン"),
            ("ART_DESIGN",          "美術デザイン")
        };

        var allEpisodeNos = eps.Select(e => e.SeriesEpNo).Distinct().OrderBy(x => x).ToList();
        var epNoByEpId = eps.ToDictionary(e => e.EpisodeId, e => e.SeriesEpNo);

        var sections = new List<KeyStaffSection>();

        foreach (var spec in roleSpecs)
        {
            var rows = new List<MainStaffRow>();
            foreach (var p in _allPersonsCache)
            {
                if (!_aliasIdsByPersonIdCache.TryGetValue(p.PersonId, out var aliasIds)) continue;

                var episodeNos = new HashSet<int>();
                foreach (var aid in aliasIds)
                {
                    if (!_involvementIndex.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                    foreach (var inv in invs)
                    {
                        if (inv.SeriesId != series.SeriesId) continue;
                        if (!string.Equals(inv.RoleCode, spec.Code, StringComparison.Ordinal)) continue;
                        if (inv.EpisodeId is int eid
                            && epNoByEpId.TryGetValue(eid, out var epNo))
                        {
                            episodeNos.Add(epNo);
                        }
                    }
                }
                if (episodeNos.Count == 0) continue;

                bool isAllEpisodes = allEpisodeNos.Count > 0
                    && episodeNos.SetEquals(allEpisodeNos);
                string rangeLabel = isAllEpisodes
                    ? string.Empty
                    : EpisodeRangeCompressor.Compress(episodeNos);

                rows.Add(new MainStaffRow
                {
                    PersonId = p.PersonId,
                    // Person.FullName は string? 型のため空文字へフォールバック（NULL 警告の抑制）。
                    FullName = p.FullName ?? "",
                    RangeLabel = rangeLabel,
                    EpisodeCount = episodeNos.Count,
                    SortKey = p.FullNameKana ?? p.FullName ?? ""
                });
            }

            if (rows.Count == 0) continue;

            rows.Sort((a, b) =>
            {
                int c = b.EpisodeCount.CompareTo(a.EpisodeCount);
                if (c != 0) return c;
                int k = string.CompareOrdinal(a.SortKey, b.SortKey);
                if (k != 0) return k;
                return string.CompareOrdinal(a.FullName, b.FullName);
            });

            sections.Add(new KeyStaffSection
            {
                RoleLabel = spec.Label,
                Members = rows
            });
        }

        return sections;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class SeriesIndexModel
    {
        public IReadOnlyList<TvSeriesRow> TvSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>
        /// 映画セクション用：親映画 + ぶら下がる子作品 + シーズンバッジ情報。
        /// 旧仕様で <c>RelatedSeriesRow</c> のフラットリストとして渡していた領域を、
        /// 親子配置に対応させた <see cref="MovieSeriesRow"/> のリストに置き換えた。
        /// </summary>
        public IReadOnlyList<MovieSeriesRow> MovieSeries { get; set; } = Array.Empty<MovieSeriesRow>();
        /// <summary>
        /// 大人向けスピンオフ（<c>kind_code='OTONA'</c>）セクション。
        /// v1.3.0 公開直前のデザイン整理第 3 弾でスピンオフを 4 種別に細分化。
        /// 行 DTO は TV と共通の <see cref="TvSeriesRow"/> を流用。
        /// </summary>
        public IReadOnlyList<TvSeriesRow> OtonaSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>
        /// ショートアニメ（<c>kind_code='SHORT'</c>）セクション。
        /// </summary>
        public IReadOnlyList<TvSeriesRow> ShortSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>
        /// イベント（<c>kind_code='EVENT'</c>）セクション。3D シアター等の上映イベント枠。
        /// </summary>
        public IReadOnlyList<TvSeriesRow> EventSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>
        /// スピンオフセクション（<c>kind_code='SPIN-OFF'</c>）。狭義のスピンオフ作品のみ。
        /// 旧仕様では「スピンオフ系すべて」を含めていたが、第 3 弾で OTONA / SHORT / EVENT を別セクションに分離し、
        /// ここは純粋な SPIN-OFF のみに範囲縮小。行 DTO は TV と共通の <see cref="TvSeriesRow"/> を流用。
        /// </summary>
        public IReadOnlyList<TvSeriesRow> SpinOffSeries { get; set; } = Array.Empty<TvSeriesRow>();
        public int TotalCount { get; set; }
        /// <summary>
        /// クレジット横断カバレッジラベル（v1.3.0 ブラッシュアップ続編で追加）。
        /// テンプレ側の lead 段落末尾に表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>
    /// TV シリーズ／スピンオフ一覧の 1 行分。連番付きの表形式で描画される。
    /// 旧仕様で持っていた <c>Children</c> プロパティは廃止（TV の下に子作品を字下げ表示しない方針へ変更）。
    /// </summary>
    private sealed class TvSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Period { get; set; } = "";
        public string EpisodesLabel { get; set; } = "";
        /// <summary>
        /// シリーズ一覧の複合行サブ情報用：プリキュア一覧のコンパクト表示
        /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
        /// 「キュアブラック (CV: 本名陽子) / キュアホワイト (CV: ゆかな)」形式の単一文字列。
        /// 紐付けが 0 件のシリーズでは空文字（テンプレ側でサブ行自体を出さない判定に使う）。
        /// </summary>
        public string PrecureSummary { get; set; } = "";
        /// <summary>
        /// シリーズ一覧の複合行サブ情報用：メインスタッフ簡易サマリ
        /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加、v1.3.0 続編 第 N+3 弾で構造化）。
        /// 5 役職分の <see cref="KeyStaffRoleGroup"/> リスト。
        /// テンプレ側ではこれを基に色付き役職バッジ + 連名人物リンク + 所属屋号 muted の構造で描画する。
        /// TV シリーズのみ集計され、それ以外や担当者ゼロのときは空リスト
        /// （テンプレ側でサブ行のスタッフ欄を出さない判定に使う）。
        /// </summary>
        public IReadOnlyList<KeyStaffRoleGroup> KeyStaffSummary { get; set; } = Array.Empty<KeyStaffRoleGroup>();
    }

    /// <summary>
    /// シリーズ一覧 TV サブ行用：1 役職分の集計結果（v1.3.0 続編 第 N+3 弾で追加）。
    /// </summary>
    private sealed class KeyStaffRoleGroup
    {
        /// <summary>役職コード（バッジ色マッピング用、CSS の <c>data-role-code</c> 属性として出力）。</summary>
        public string RoleCode { get; set; } = "";
        /// <summary>代表 role_code（バッジリンク先 <c>/stats/roles/{repCode}/</c> 用）。</summary>
        public string RepRoleCode { get; set; } = "";
        /// <summary>表示ラベル（「プロデューサー」「シリーズ構成」等）。</summary>
        public string RoleLabel { get; set; } = "";
        /// <summary>当該役職に該当する人物群（担当エピソード数 desc → kana asc 順）。</summary>
        public IReadOnlyList<KeyStaffMember> Members { get; set; } = Array.Empty<KeyStaffMember>();
    }

    /// <summary>
    /// シリーズ一覧 TV サブ行用：1 役職グループ内の 1 名分（v1.3.0 続編 第 N+3 弾で追加）。
    /// </summary>
    private sealed class KeyStaffMember
    {
        public int PersonId { get; set; }
        /// <summary>表示用人物名（<c>persons.full_name</c>）。</summary>
        public string DisplayName { get; set; } = "";
        /// <summary>
        /// 所属屋号の表示ラベル（当該シリーズ内最頻、<c>company_aliases.name</c> をそのまま使用）。
        /// 屋号未指定なら空文字でテンプレ側はカッコ含めて出さない。
        /// </summary>
        public string AffiliationLabel { get; set; } = "";
        /// <summary>担当エピソード数（ソート用、テンプレでは未表示）。</summary>
        public int EpisodeCount { get; set; }
        /// <summary>ソートキー（kana、テンプレでは未表示）。</summary>
        public string SortKey { get; set; } = "";
    }

    /// <summary>
    /// シリーズ詳細・シリーズ一覧サブ行で使うプリキュア表示行
    /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
    /// <c>series_precures</c> の 1 紐付けを、変身前名・変身後名・声優名に解決した表示用 DTO。
    /// </summary>
    private sealed class SeriesPrecureDisplay
    {
        public int PrecureId { get; set; }
        public string TransformName { get; set; } = "";
        public string PreTransformName { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
    }

    /// <summary>
    /// 映画セクションの親映画行 DTO（v1.3.0 公開直前のデザイン整理で新設、第 2 弾で `RuntimeLabel` を追加）。
    /// 親映画 1 作品 + その下にぶら下がる子作品（'MOVIE_SHORT'）+ シーズンバッジ情報 + 尺合計ラベルを持つ。
    /// </summary>
    private sealed class MovieSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Period { get; set; } = "";
        /// <summary>シーズンバッジ用 CSS クラス名（"movie-badge-fall" / "movie-badge-spring"）。空文字なら無印。</summary>
        public string SeasonBadgeClass { get; set; } = "";
        /// <summary>シーズンバッジに表示するラベル文字列（「秋映画」「春映画」）。</summary>
        public string SeasonBadgeLabel { get; set; } = "";
        /// <summary>
        /// 親 + 子（MOVIE_SHORT）合計の上映時間ラベル（「m分ss秒」形式）。
        /// 親または子のいずれかに <c>run_time_seconds</c> が NULL のものが 1 件でもあれば空文字。
        /// TV の「全N話」と同じ列位置に表示する。
        /// </summary>
        public string RuntimeLabel { get; set; } = "";
        /// <summary>親映画にぶら下がる子作品（'MOVIE_SHORT' のみ、seq_in_parent 昇順）。HasOwnPage=false で表示テキストのみ。</summary>
        public IReadOnlyList<RelatedSeriesRow> Children { get; set; } = Array.Empty<RelatedSeriesRow>();
    }

    private sealed class RelatedSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Period { get; set; } = "";
        /// <summary>子作品（HasOwnPage=false）はリンク化せず表示のみ行う。</summary>
        public bool HasOwnPage { get; set; } = true;
    }

    private sealed class SeriesDetailModel
    {
        public SeriesDetailView Series { get; set; } = new();
        public IReadOnlyList<EpisodeIndexRow> Episodes { get; set; } = Array.Empty<EpisodeIndexRow>();
        /// <summary>
        /// 「併映・子作品」セクション用（v1.3.0 続編で分離）。
        /// 親シリーズに従属する MOVIE_SHORT（秋映画併映短編）など、単独詳細ページを持たない作品のみ。
        /// 主に映画系の併映情報として表示する。
        /// </summary>
        public IReadOnlyList<RelatedSeriesRow> RelatedChildren { get; set; } = Array.Empty<RelatedSeriesRow>();
        /// <summary>
        /// 「関連作品」セクション用（v1.3.0 続編で分離）。
        /// TV シリーズの続編・スピンオフ（SPIN-OFF）・大人向け（OTONA）など、
        /// 親子関係にあるが単独詳細ページを持つ作品。シリーズの広がりを示すリンク集として使う。
        /// </summary>
        public IReadOnlyList<RelatedSeriesRow> RelatedSiblings { get; set; } = Array.Empty<RelatedSeriesRow>();
        public RelatedSeriesRow? Parent { get; set; }
        public IReadOnlyList<KeyStaffSection> KeyStaffSections { get; set; } = Array.Empty<KeyStaffSection>();
        /// <summary>
        /// このシリーズに登場するプリキュア一覧（v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
        /// <c>series_precures</c> テーブルから取得。display_order 昇順、同値時 precure_id 昇順。
        /// 紐付けが 0 件のシリーズではテンプレ側でセクション自体を非表示にする。
        /// </summary>
        public IReadOnlyList<SeriesPrecureRow> Precures { get; set; } = Array.Empty<SeriesPrecureRow>();
        /// <summary>
        /// クレジット横断カバレッジラベル（v1.3.0 ブラッシュアップ続編で追加）。
        /// テンプレ側の h1 ブロック直後に独立段落で表示する。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>
    /// シリーズ詳細のプリキュアセクション行 DTO
    /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
    /// 変身前名 / 変身後名 / 声優を 1 行で持ち、テンプレ側で表組みする。
    /// </summary>
    private sealed class SeriesPrecureRow
    {
        public int PrecureId { get; set; }
        public string TransformName { get; set; } = "";
        public string PreTransformName { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
    }

    private sealed class KeyStaffSection
    {
        public string RoleLabel { get; set; } = "";
        public IReadOnlyList<MainStaffRow> Members { get; set; } = Array.Empty<MainStaffRow>();
    }

    private sealed class MainStaffRow
    {
        public int PersonId { get; set; }
        public string FullName { get; set; } = "";
        public string RangeLabel { get; set; } = "";
        public int EpisodeCount { get; set; }
        public string SortKey { get; set; } = "";
    }

    private sealed class SeriesDetailView
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleKana { get; set; } = "";
        public string TitleEn { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Period { get; set; } = "";
        public string Episodes { get; set; } = "";
        public string RunTimeSeconds { get; set; } = "";
        public string ToeiAnimOfficialSiteUrl { get; set; } = "";
        public string ToeiAnimLineupUrl { get; set; } = "";
        public string AbcOfficialSiteUrl { get; set; } = "";
        public string AmazonPrimeDistributionUrl { get; set; } = "";
        public bool HasExternalUrls { get; set; }
        public bool HasEpisodeList { get; set; }
    }

    private sealed class EpisodeIndexRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
        /// <summary>
        /// 絵コンテと演出が同じ人物（PERSON エントリの重複キー集合が一致 + 両方非空）かどうか（v1.3.0 続編で追加）。
        /// true の場合、テンプレ側でエピソード一覧の当該行を「絵コンテ・演出 ○○」の 1 表記に統合する。
        /// false の場合は従来通り「絵コンテ ○○ / 演出 ○○」と 2 つ独立して並べる。
        /// </summary>
        public bool StoryboardDirectorMerged { get; set; }
    }

    // v1.3.0 続編 第 N+3 弾：旧 private sealed class EpisodeStaffSummary は
    // PrecureDataStars.SiteBuilder/Utilities/EpisodeStaffSummary.cs に独立公開クラスとして外出し済み。
    // /episodes/ ランディング生成（EpisodesIndexGenerator）からも参照できるようにするため。
}