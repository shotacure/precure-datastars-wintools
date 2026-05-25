using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// シリーズ系ページ（一覧 + 個別）の生成。
/// <c>/series/</c> は全シリーズの索引、<c>/series/{slug}/</c> は各シリーズの詳細。
/// 一覧の構成：
/// <list type="number">
///   <item><description>TV シリーズセクション：放送開始順に連番付きで純粋な TV シリーズだけを並べる。
///     子作品（併映短編・子映画）は字下げ表示せず、映画系は下の映画セクションに出す。</description></item>
///   <item><description>映画セクション：親 TV を持たない単独映画系（秋映画・春映画など、<c>parent_series_id</c> が NULL）
///     を公開順に親作品として並べ、その下に親映画にぶら下がる子作品（併映短編・子映画）を字下げ表示する。
///     親映画タイトル先頭に「秋映画／春映画」のシーズンバッジを置く。子作品はリンクなしテキスト表示。</description></item>
///   <item><description>スピンオフセクション：<c>SPIN-OFF</c> 種別だけを放送順に連番付きで並べる。
///     セクション見出しから自明なので [スピンオフ] のテキストラベルは出さない。</description></item>
/// </list>
/// 子作品（<c>parent_series_id</c> が NULL でない映画系）は単独詳細ページを生成しない。
/// 親映画詳細の中の「併映・子作品」セクションに一覧表示するだけにとどめる。
/// これにより sitemap・search-index・ナビからも自動的に除外される（生成しないため）。
/// 個別シリーズページのエピソード一覧は <see cref="SeriesKind.CreditAttachTo"/> が EPISODE のときだけ表示する。
/// 表構造ではなく <c>&lt;dl class="ep-list"&gt;</c> + <c>&lt;dt&gt;</c>（話数 + サブタイトル）+ <c>&lt;dd&gt;</c>
/// （字下げでスタッフ群）の縦並びレイアウト。
/// メインスタッフセクションは PRODUCER / SERIES_COMPOSITION / SERIES_DIRECTOR / CHARACTER_DESIGN / ART_DESIGN
/// の 5 役職を出し、全話担当者は名前のみ、部分担当者は「名前 (#1～4, 8)」表記。
/// 劇伴一覧はシリーズ詳細から除外し、別ページ <c>/bgms/{slug}/</c> へのリンク 1 本に置き換える。
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

    // ── 役職統計詳細ページへの URL 組み立て用。
    private readonly RoleSuccessorResolver _roleSuccessorResolver;

    // ── メインスタッフ集計用 ──
    private readonly CreditInvolvementIndex _involvementIndex;
    private readonly PersonsRepository _personsRepo;
    private readonly PersonAliasPersonsRepository _personAliasPersonsRepo;
    private readonly SeriesKindsRepository _seriesKindsRepo;

    // ── シリーズ × プリキュア紐付け。
    //    シリーズ詳細のプリキュアセクション、シリーズ一覧の複合行サブ情報の両方で使う。 ──
    private readonly SeriesPrecuresRepository _seriesPrecuresRepo;
    private readonly PrecuresRepository _precuresRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;

    // ── キャラクター正式名称解決。
    //    シリーズ一覧プリキュアバッジの表記は characters.name を優先するため、
    //    character_aliases.character_id 経由で参照する characters マスタを全件キャッシュする。 ──
    private readonly CharactersRepository _charactersRepo;

    // ── 屋号解決。
    //    シリーズ一覧 TV サブ行のスタッフ欄で「人物名（東映アニメーション）」のように
    //    所属屋号を muted 表記で添えるため、CompanyAlias 全件を 1 度だけロードしてキャッシュする。 ──
    private readonly CompanyAliasesRepository _companyAliasesRepo;

    // ── シリーズ関係種別マスタ。
    private readonly SeriesRelationKindsRepository _seriesRelationKindsRepo;
    // 映画作品の BGM リスト（movie_bgm_cues。映画系シリーズ詳細でのみ使用）
    private readonly MovieBgmCuesRepository _movieBgmCuesRepo;
    // 映画 BGM の区分コード→和名解決に使う（track_content_kinds 共用）
    private readonly TrackContentKindsRepository _trackContentKindsRepo;

    // ── 役職マスタ・name 解決の共通キャッシュ ──
    private IReadOnlyDictionary<string, Role>? _roleMap;
    private readonly Dictionary<int, PersonAlias?> _personAliasCache = new();

    // ── メインスタッフ集計用キャッシュ ──
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _aliasIdsByPersonIdCache;
    private IReadOnlyList<Person>? _allPersonsCache;
    private IReadOnlyDictionary<string, SeriesKind>? _seriesKindMapCache;

    // ── プリキュア集計用キャッシュ。
    //    series_id → そのシリーズに紐付くプリキュア表示行リスト（display_order 昇順）。
    //    シリーズ詳細・シリーズ一覧の両方からアクセスするので 1 度だけ構築する。 ──
    private IReadOnlyDictionary<int, IReadOnlyList<SeriesPrecureDisplay>>? _precureRowsBySeriesCache;

    // ── シリーズ一覧サブ行用：メインスタッフ簡易サマリ集計キャッシュ。
    //    series_id → 役職グループのリスト。
    //    各役職グループは「PRODUCER」「SERIES_COMPOSITION」「SERIES_DIRECTOR」「CHARACTER_DESIGN」「ART_DESIGN」のいずれかで、
    //    連名全員と所属屋号を含む構造化データを持つ。テンプレ側で色付きバッジ + 人物リンクとして描画する。
    //    TV シリーズのみ集計対象。 ──
    private IReadOnlyDictionary<int, IReadOnlyList<KeyStaffRoleGroup>>? _keyStaffSummaryBySeriesCache;

    // ── 屋号 alias_id → 名前 マップ。
    //    シリーズ一覧 TV サブ行のスタッフ所属屋号ラベル解決に使用する。
    //    全 CompanyAlias を 1 度だけロードしてキャッシュ。 ──
    private IReadOnlyDictionary<int, string>? _companyAliasNameMapCache;

    // ── 関係種別マスタキャッシュ。
    //    関連作品セクションのバッジ表示で参照する：
    //      forward = 子→親方向（自身の親を関連作品リストに含めるときの親バッジ用、name_ja）
    //      reverse = 親→子方向（自身の子を関連作品リストに含めるときの子バッジ用、name_ja_reverse）
    //    両方向を 1 度に全件ロードして辞書として保持する。 ──
    private IReadOnlyDictionary<string, string>? _relationKindForwardLabelMapCache;
    private IReadOnlyDictionary<string, string>? _relationKindReverseLabelMapCache;

    // ── エピソード単位 staff サマリの memoize。
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
        _charactersRepo = new CharactersRepository(factory);
        _companyAliasesRepo = new CompanyAliasesRepository(factory);
        _seriesRelationKindsRepo = new SeriesRelationKindsRepository(factory);
        _movieBgmCuesRepo = new MovieBgmCuesRepository(factory);
        _trackContentKindsRepo = new TrackContentKindsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating series");

        // シリーズ詳細・シリーズ一覧（複合行サブ情報）の両方でプリキュア紐付け / メインスタッフサマリを使うため、
        // 最初に 1 度だけ全件ロードしてキャッシュに詰めておく。
        await BuildPrecureRowsBySeriesCacheAsync(ct).ConfigureAwait(false);
        await BuildKeyStaffSummaryBySeriesCacheAsync(ct).ConfigureAwait(false);

        GenerateIndex();

        // 子作品（parent_series_id != NULL の映画系）は単独詳細ページを生成しない。
        // 親映画詳細の「併映・子作品」セクションに表示されるだけにする。
        // SPIN-OFF は親を持っても単独ページが必要（parent_series_id を持つことは想定していないが念のため）。
        int generated = 0;
        foreach (var s in _ctx.Series)
        {
            if (SeriesClassifier.IsMovieShortChild(s)) continue;
            await GenerateDetailAsync(s, ct).ConfigureAwait(false);
            generated++;
        }

        _ctx.Logger.Success($"series: {generated + 1} ページ");
    }

    /// <summary>
    /// シリーズ × プリキュア紐付け（<c>series_precures</c> テーブル）を全件ロードし、
    /// 表示用の行情報（変身前名・変身後名・声優名）に解決して
    /// <c>series_id → SeriesPrecureDisplay のリスト</c> にグルーピングしてキャッシュする。
    /// 解決には <c>precures</c> / <c>character_aliases</c> / <c>persons</c> の 3 マスタを使う。
    /// 各 alias / person の名前を最初に Dictionary 化して、紐付け 1 件ごとの引きを O(1) で済ませる。
    /// 並び順は <c>display_order ASC, precure_id ASC</c> でタイブレーク。
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
        // characters は表記の第一候補（characters.name 優先）に使う。alias から character_id を辿って引く。
        var allCharacters = await _charactersRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        // persons は KeyStaff 集計でも使うキャッシュを流用（無ければここで埋める）。
        _allPersonsCache ??= await _personsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);

        var precureById = allPrecures.ToDictionary(p => p.PrecureId);
        var aliasById = allAliases.ToDictionary(a => a.AliasId);
        var characterById = allCharacters.ToDictionary(c => c.CharacterId);
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
            string transform2Name = precure.Transform2AliasId is int t2AliasId
                && aliasById.TryGetValue(t2AliasId, out var t2Alias) ? t2Alias.Name : "";
            string voiceActorName = (precure.VoiceActorPersonId is int vid && personById.TryGetValue(vid, out var v))
                ? (v.FullName ?? "") : "";
            // 表記の第一候補：変身後名義が属するキャラクターの正式名称（characters.name）。
            // alias → character_id → characters の 2 段引き。解決できなければ空文字のままにし、
            // 後段（バッジ生成）で変身後名義へフォールバックさせる。
            string characterName = "";
            if (trans is not null && characterById.TryGetValue(trans.CharacterId, out var ch))
                characterName = ch.Name ?? "";

            var row = new SeriesPrecureDisplay
            {
                PrecureId = precure.PrecureId,
                TransformName = transformName,
                Transform2Name = transform2Name,
                PreTransformName = preTransformName,
                CharacterName = characterName,
                VoiceActorName = voiceActorName,
                VoiceActorPersonId = precure.VoiceActorPersonId,
                // バッジ地色（#RRGGBB）。未設定/不正値は後段でフォールバックバッジにする。
                KeyColor = precure.KeyColor ?? ""
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

    /// <summary>指定シリーズに紐付くプリキュア表示行リストを返す （無ければ空リスト）。</summary>
    private IReadOnlyList<SeriesPrecureDisplay> GetPrecureRows(int seriesId)
    {
        if (_precureRowsBySeriesCache is null) return Array.Empty<SeriesPrecureDisplay>();
        return _precureRowsBySeriesCache.TryGetValue(seriesId, out var list)
            ? list
            : Array.Empty<SeriesPrecureDisplay>();
    }

    /// <summary>
    /// シリーズ一覧 TV サブ行用のメインスタッフ簡易サマリを全 TV シリーズについて集計し、
    /// <c>series_id → 役職グループのリスト</c> としてキャッシュする。
    /// 役職ごとに構造化 DTO リストを返す。文字列ではなく構造化することで、
    /// リンク化・所属屋号付きでの表示や連名スタッフの保持ができる。
    /// シリーズ詳細の <see cref="BuildMainStaffSectionsAsync"/> と同じ役職セット 5 種について、
    /// 各役職に該当する全人物をクレジット順（当該シリーズ・役職での
    /// (EpisodeNo, CreditSeq, CreditSubSeq) lex min 昇順）で集めて <see cref="KeyStaffMember"/> として並べる。
    /// 同一エピソード内では各エントリの CreditSeq が一意なので、異なる人物がこの三つ組で完全に一致することはなく
    /// クレジット順だけで序列が確定する。担当エピソード数・kana 等の補助キーは使わない。
    /// 所属屋号（<c>Involvement.AffiliationCompanyAliasId</c>）も当該シリーズ内最頻のものを引き当てて添える。
    /// テンプレ側では役職バッジ（色付き）+ 名前リンク + 所属屋号 の構造で描画される。
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
                // 各人物について：所属屋号 alias_id 出現回数辞書 と
                // クレジット順ソート用の (EpisodeNo, CreditSeq, CreditSubSeq) lex min を求める。
                var members = new List<KeyStaffMember>();
                foreach (var p in _allPersonsCache)
                {
                    if (!_aliasIdsByPersonIdCache.TryGetValue(p.PersonId, out var aliasIds)) continue;

                    var affiliationCounts = new Dictionary<int, int>();
                    // クレジット順ソートキーの lex min。1 件でも当該シリーズ・役職の
                    // エピソードスコープ Involvement があればここが更新され、
                    // 更新が無いまま終わった人物は当該役職に登場しないとして除外する。
                    int sortEpNo = int.MaxValue;
                    int sortCreditSeq = int.MaxValue;
                    int sortCreditSubSeq = int.MaxValue;
                    foreach (var aid in aliasIds)
                    {
                        if (!_involvementIndex.ByPersonAlias.TryGetValue(aid, out var invs)) continue;
                        foreach (var inv in invs)
                        {
                            if (inv.SeriesId != s.SeriesId) continue;
                            if (!string.Equals(inv.RoleCode, spec.Code, StringComparison.Ordinal)) continue;
                            // エピソードスコープのみクレジット順ソート対象に含める。
                            // SERIES スコープ Involvement はエピソード番号を持たないため対象外。
                            if (inv.EpisodeId is int eid && epNoByEpId.TryGetValue(eid, out var n))
                            {
                                // (EpisodeNo, CreditSeq, CreditSubSeq) 三つ組の辞書順比較で lex min を更新。
                                if (n < sortEpNo
                                    || (n == sortEpNo && inv.CreditSeq < sortCreditSeq)
                                    || (n == sortEpNo && inv.CreditSeq == sortCreditSeq && inv.CreditSubSeq < sortCreditSubSeq))
                                {
                                    sortEpNo = n;
                                    sortCreditSeq = inv.CreditSeq;
                                    sortCreditSubSeq = inv.CreditSubSeq;
                                }
                            }
                            // 所属屋号 alias_id の出現回数
                            if (inv.AffiliationCompanyAliasId is int affId)
                            {
                                affiliationCounts.TryGetValue(affId, out var c);
                                affiliationCounts[affId] = c + 1;
                            }
                        }
                    }
                    if (sortEpNo == int.MaxValue) continue;

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
                        SortEpNo = sortEpNo,
                        SortCreditSeq = sortCreditSeq,
                        SortCreditSubSeq = sortCreditSubSeq
                    });
                }

                if (members.Count == 0) continue;

                // クレジット順（EpisodeNo → CreditSeq → CreditSubSeq の辞書順）昇順でソート。
                // 三つ組は人物ごとに lex min が異なる前提なのでこれだけで序列は確定する。
                members.Sort((a, b) =>
                {
                    int c = a.SortEpNo.CompareTo(b.SortEpNo);
                    if (c != 0) return c;
                    c = a.SortCreditSeq.CompareTo(b.SortCreditSeq);
                    if (c != 0) return c;
                    return a.SortCreditSubSeq.CompareTo(b.SortCreditSubSeq);
                });

                // 系譜代表の役職コード。リンク URL はこの代表コードを PathUtil 経由で
                // 小文字化して組み立てる（現行挙動どおり代表ページへ飛ばす）。
                var repRoleCode = _roleSuccessorResolver.GetRepresentative(spec.Code);
                groups.Add(new KeyStaffRoleGroup
                {
                    RoleCode = spec.Code,
                    RepRoleCode = repRoleCode,
                    // テンプレ側はこの組み立て済み URL のみリンク href に使う。
                    // data-role-code 属性はバッジ色分け CSS の都合で RoleCode（実コード）を使う。
                    RoleUrl = PathUtil.RoleStatsUrl(repRoleCode),
                    RoleLabel = spec.Label,
                    Members = members
                });
            }

            if (groups.Count > 0)
                summaryDict[s.SeriesId] = groups;
        }
        _keyStaffSummaryBySeriesCache = summaryDict;
    }

    /// <summary>指定シリーズのメインスタッフサマリ（役職グループのリスト）を返す。 データ無しのときは空リストを返す。</summary>
    private IReadOnlyList<KeyStaffRoleGroup> GetKeyStaffSummary(int seriesId)
    {
        if (_keyStaffSummaryBySeriesCache is null) return Array.Empty<KeyStaffRoleGroup>();
        return _keyStaffSummaryBySeriesCache.TryGetValue(seriesId, out var g)
            ? g
            : Array.Empty<KeyStaffRoleGroup>();
    }

    /// <summary>
    /// /episodes/ ランディングページ生成（<see cref="EpisodesIndexGenerator"/>）から参照される、
    /// エピソード単位 staff サマリの memoize キャッシュ。
    /// 本キャッシュは <see cref="ExtractStaffSummaryAsync"/> を経由したときだけ詰められる。
    /// <see cref="GenerateDetailAsync"/> が各 TV シリーズの全エピソードについて
    /// <c>ExtractStaffSummaryAsync</c> を呼ぶため、<see cref="GenerateAsync"/> 完了後は
    /// クレジット添付対象（credit_attach_to=EPISODE）のシリーズ配下エピソード全件が揃っている。
    /// </summary>
    public IReadOnlyDictionary<int, EpisodeStaffSummary> GetEpisodeStaffSummaries()
        => _episodeStaffByIdCache;

    /// <summary>指定 kind_code の単純な行リスト（TV / OTONA / SHORT / EVENT / SPIN-OFF 等の 1 行 1 シリーズの素直なテーブル用）を組み立てる共通ヘルパ。</summary>
    /// <param name="kindCode">対象シリーズ種別コード（例 "OTONA"）。完全一致のみ。</param>
    private IReadOnlyList<TvSeriesRow> BuildSimpleRowsByKind(string kindCode)
    {
        // credit_attach_to=EPISODE のシリーズ種別（TV / SPIN-OFF / OTONA / SHORT）は
        // 終了日未確定なら「〜」止め期間表記、終了日確定なら通常両端表記。
        // credit_attach_to=SERIES（EVENT 等）は継続概念を持たないので開始日単独表記。
        bool episodeAttaching = _ctx.SeriesKindByCode.TryGetValue(kindCode, out var kind)
            && string.Equals(kind.CreditAttachTo, "EPISODE", StringComparison.Ordinal);
        return _ctx.Series
            .Where(s => string.Equals(s.KindCode, kindCode, StringComparison.Ordinal))
            .OrderBy(s => s.StartDate)
            .ThenBy(s => s.SeriesId)
            .Select(s =>
            {
                var precureRows = GetPrecureRows(s.SeriesId);
                string period = episodeAttaching
                    ? JpDateFormat.PeriodOrOngoing(s.StartDate, s.EndDate)
                    : JpDateFormat.Period(s.StartDate, s.EndDate);
                bool estimated = episodeAttaching && IsEpisodesEstimated(s);
                return new TvSeriesRow
                {
                    Slug = s.Slug,
                    Title = s.Title,
                    Period = period,
                    // 実話数が総話数に満たない継続中シリーズは「（見込）」を別 span で付与できるよう、
                    // 終了日が確定済みなら期間側にも見込み注記を出す（継続中＝EndDate=null は
                    // 期間が「〜」止めなので注記は総話数側のみ）。
                    PeriodEstimateNote = (estimated && s.EndDate.HasValue) ? EstimateNote : "",
                    EpisodesLabel = s.Episodes.HasValue ? $"全 {s.Episodes.Value} 話" : "",
                    EpisodesEstimateNote = (estimated && s.Episodes.HasValue) ? EstimateNote : "",
                    PrecureBadges = BuildPrecureBadges(precureRows),
                    KeyStaffSummary = GetKeyStaffSummary(s.SeriesId)
                };
            })
            .ToList();
    }

    /// <summary>放送見込み注記の表示文字列（「（見込）」）。テンプレ側で nowrap の別 span に入れて列幅のガタつきを防ぐ。</summary>
    private const string EstimateNote = "（見込）";

    /// <summary>
    /// 「総話数見込み」判定。ビルド時点で終了していない（<c>end_date</c> が NULL もしくは
    /// ビルド時刻より未来）かつ総話数マスタ値（<see cref="Series.Episodes"/>）が入っている
    /// シリーズを見込み扱いにする。継続中のシリーズはマスタ値が後から増減する余地が残るため
    /// 「（見込）」を添えて確定値ではないことを明示する。
    /// 終了済みシリーズ（<c>end_date</c> がビルド時点以前）は実話数が総話数マスタ値に満たなくても
    /// 確定扱いとする（実話数とのギャップは単なるデータ入力残であって見込みとは別問題）。
    /// 総話数マスタ値が未設定（<c>null</c>）のシリーズは比較不能なので見込み扱いにしない。
    /// 呼び出し側で credit_attach_to=EPISODE を保証すること（本メソッドは種別を判定しない）。
    /// </summary>
    private bool IsEpisodesEstimated(Series s)
    {
        if (!s.Episodes.HasValue) return false;
        var today = DateOnly.FromDateTime(DateTime.Now);
        return !s.EndDate.HasValue || s.EndDate.Value > today;
    }

    /// <summary>
    /// 関連シリーズ行（親 / 子・続編・併映）の期間表記を組み立てる共通ヘルパ。
    /// credit_attach_to=EPISODE のシリーズは継続中（end_date=null）に「〜」止め表記、
    /// SERIES のシリーズは単独 or 両端表記を返す。
    /// </summary>
    private string FormatRelatedPeriod(Series s)
    {
        return SeriesClassifier.IsEpisodeAttaching(s, _ctx.SeriesKindByCode)
            ? JpDateFormat.PeriodOrOngoing(s.StartDate, s.EndDate)
            : JpDateFormat.Period(s.StartDate, s.EndDate);
    }

    /// <summary>
    /// シリーズ一覧の複合行サブ情報用：プリキュア群を色付きバッジのリストに整形する。
    /// 表記はキャラクター正式名称（<c>characters.name</c>）を優先し、解決できないときのみ
    /// 変身後名義へフォールバックする。声優が登録されていれば「 (CV: ○○)」を後置する。
    /// バッジの地色はプリキュアマスタの <c>key_color</c>。文字色は地色の相対輝度（WCAG 定義）から
    /// 暗グレー／明グレーを自動で選び、どんな地色でも本文が読めるようにする。地色が未設定または
    /// 不正値（<c>#RRGGBB</c> 形式でない）のプリキュアは、インライン色を持たない中立バッジにする
    /// （CSS 既定の淡色フォールバックで描画される）。
    /// 0 件のときは空リストを返す（テンプレ側でプリキュア欄を出さない判定に使う）。
    /// </summary>
    private static IReadOnlyList<PrecureBadge> BuildPrecureBadges(IReadOnlyList<SeriesPrecureDisplay> rows)
    {
        if (rows.Count == 0) return Array.Empty<PrecureBadge>();
        var badges = new List<PrecureBadge>(rows.Count);
        foreach (var r in rows)
        {
            // 表記：プリキュア観点で「変身後 / 変身後 2 / 変身前」の名義名を「 / 」連結
            // （NULL・空の名義は除外）。すべて空のときのみ変身後名義へフォールバック。
            // 声優ありなら「 (CV: ○○)」を後置。characters.name は表記には用いない。
            string baseName = PrecureNaming.JoinAliasNames(
                r.TransformName, r.Transform2Name, r.PreTransformName);
            if (string.IsNullOrEmpty(baseName)) baseName = r.TransformName;
            string label = string.IsNullOrEmpty(r.VoiceActorName)
                ? baseName
                : $"{baseName} (CV: {r.VoiceActorName})";

            // 地色 → 文字色・ボーダー色を解決。未設定/不正値は空文字（テンプレ側で無装飾バッジ）。
            var (bg, fg, border) = ResolveBadgeColors(r.KeyColor);

            badges.Add(new PrecureBadge
            {
                PrecureId = r.PrecureId,
                Label = label,
                BackgroundColor = bg,
                TextColor = fg,
                BorderColor = border
            });
        }
        return badges;
    }

    /// <summary>
    /// バッジ地色（<c>#RRGGBB</c>）から、地色・文字色・ボーダー色の 3 値を解決する。
    /// 文字色は地色の相対輝度（WCAG 2.x 定義の linearized sRGB 加重和）を求め、
    /// しきい値 0.179（黒文字と白文字のコントラストが拮抗する境界）で
    /// 暗グレー（<c>#1a1a1a</c>）／明グレー（<c>#f5f5f5</c>）を出し分ける。
    /// ボーダーは文字色側に寄せた半透明色で、地色がページ背景に近いときでも輪郭を保つ。
    /// 入力が <c>#RRGGBB</c> 形式でなければ 3 値とも空文字を返し、呼び出し側で
    /// インライン色を付けない（CSS 既定の淡色バッジになる）。
    /// </summary>
    private static (string Background, string Text, string Border) ResolveBadgeColors(string keyColor)
    {
        if (string.IsNullOrEmpty(keyColor)
            || keyColor.Length != 7
            || keyColor[0] != '#')
        {
            return ("", "", "");
        }

        int r, g, b;
        try
        {
            r = Convert.ToInt32(keyColor.Substring(1, 2), 16);
            g = Convert.ToInt32(keyColor.Substring(3, 2), 16);
            b = Convert.ToInt32(keyColor.Substring(5, 2), 16);
        }
        catch (FormatException)
        {
            // 16 進として解釈できない文字が混じっていた場合は無装飾フォールバック。
            return ("", "", "");
        }

        // sRGB 1 チャンネルを相対輝度計算用にリニアライズする（WCAG 2.x 定義）。
        static double Linearize(int channel)
        {
            double c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        double luminance = 0.2126 * Linearize(r)
                         + 0.7152 * Linearize(g)
                         + 0.0722 * Linearize(b);

        // しきい値 0.179 より明るい地色 → 暗い文字、暗い地色 → 明るい文字。
        bool darkText = luminance > 0.179;
        string text = darkText ? "#1a1a1a" : "#f5f5f5";
        // ボーダーは文字色側へ寄せた半透明。地色がページ地と近くても輪郭が出る。
        string border = darkText ? "rgba(0, 0, 0, 0.22)" : "rgba(255, 255, 255, 0.30)";

        return (keyColor, text, border);
    }

    /// <summary>映画系シリーズ判定。MOVIE（秋映画）／SPRING（春映画）／MOVIE_SHORT（秋映画併映短編）の 3 種。 一覧の映画セクションでまとめて扱う対象になる （親としての映画＝MOVIE/SPRING、子としての映画＝MOVIE_SHORT）。</summary>
    private static bool IsMovieKind(string kindCode)
        => string.Equals(kindCode, "MOVIE",       StringComparison.Ordinal)
        || string.Equals(kindCode, "SPRING",      StringComparison.Ordinal)
        || string.Equals(kindCode, "MOVIE_SHORT", StringComparison.Ordinal);

    /// <summary>シーズンバッジの CSS クラス名を返す。</summary>
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

    /// <summary><c>/series/</c> の索引ページ。TV / 映画 / スピンオフ の 3 セクション構成。</summary>
    private void GenerateIndex()
    {
        // TV シリーズだけを抽出（放送順）。
        var tvRows = BuildSimpleRowsByKind("TV");

        // 映画セクションの「子作品」候補は kind_code='MOVIE_SHORT' のみ。
        var movieShortByParent = _ctx.Series
            .Where(s => s.KindCode == "MOVIE_SHORT" && s.ParentSeriesId.HasValue)
            .GroupBy(s => s.ParentSeriesId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderBy(c => c.SeqInParent ?? byte.MaxValue)
                    .ThenBy(c => c.SeriesId)
                    .ToList());

        // 映画セクションの「親作品」候補は kind_code IN ('MOVIE','SPRING') 全て。
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
                    Period = JpDateFormat.Period(m.StartDate, m.EndDate),
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
                            // 子作品単体の尺を親と同じ尺カラム位置に出す。
                            // run_time_seconds 未登録（NULL）の子は空文字でセル空表示。
                            RuntimeLabel = c.RunTimeSeconds.HasValue
                                ? $"{(int)c.RunTimeSeconds.Value / 60}分{(int)c.RunTimeSeconds.Value % 60}秒"
                                : "",
                            HasOwnPage = false
                        })
                        .ToList()
                };
            })
            .ToList();

        // スピンオフ系セクション。
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
            PageTitle = "歴代プリキュアシリーズ",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュアシリーズ", Url = "" }
            }
        };
        _page.RenderAndWrite("/series/", "series", "series-index.sbn", content, layout);
    }

    /// <summary><c>/series/{slug}/</c> 個別シリーズページ。</summary>
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
                // stage B-6：ルビ付きサブタイトル HTML を流すが、シリーズ詳細のエピソード一覧では
                // 1 行表示にしたいので、データ内の改行表現 <br>（および念のため <br/>・<br />）を
                // 半角スペースへ置換する。ルビ要素（<ruby><rt>...</rt></ruby>）はインライン要素なので、
                // 改行を抜いても表示は崩れず、サブタイトル全体が 1 行で並ぶ。
                var titleRichRaw = e.TitleRichHtml ?? "";
                var titleRichInline = System.Text.RegularExpressions.Regex.Replace(
                    titleRichRaw,
                    @"<br\s*/?\s*>",
                    " ",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                epRows.Add(new EpisodeIndexRow
                {
                    SeriesEpNo = e.SeriesEpNo,
                    TitleText = e.TitleText,
                    TitleRichHtml = titleRichInline,
                    OnAirDate = JpDateFormat.DotDate(e.OnAirAt),
                    EpisodeUrl = PathUtil.EpisodeUrl(s.Slug, e.SeriesEpNo),
                    Screenplay = staff.Screenplay,
                    Storyboard = staff.Storyboard,
                    EpisodeDirector = staff.EpisodeDirector,
                    AnimationDirector = staff.AnimationDirector,
                    ArtDirector = staff.ArtDirector,
                    // 絵コンテと演出が同一人物（PERSON エントリのキー集合が一致 + どちらも非空）のとき、
                    // テンプレ側で「絵コンテ・演出 ○○」の 1 表記に統合する。同一性判定は集合比較で行う。
                    StoryboardDirectorMerged = staff.StoryboardDirectorMerged
                });
            }
        }

        // 関連シリーズ（自分が親で、配下にいる作品）を 2 つのカテゴリに分ける。
        if (_relationKindReverseLabelMapCache is null || _relationKindForwardLabelMapCache is null)
        {
            var allRelKinds = await _seriesRelationKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
            _relationKindForwardLabelMapCache = allRelKinds.ToDictionary(
                k => k.RelationCode,
                k => k.NameJa ?? "",
                StringComparer.Ordinal);
            _relationKindReverseLabelMapCache = allRelKinds.ToDictionary(
                k => k.RelationCode,
                k => k.NameJaReverse ?? "",
                StringComparer.Ordinal);
        }
        var relatedWorks = new List<RelatedSeriesRow>();
        // 1) 自身の親があれば、先頭に親を 1 件加える。バッジは name_ja（子→親方向）で表示。
        //    自身の RelationToParent コードを使って親に対する「子から見た関係名」を引く。
        if (s.ParentSeriesId is int pidForRelated
            && _ctx.SeriesById.TryGetValue(pidForRelated, out var parentForRelated))
        {
            relatedWorks.Add(new RelatedSeriesRow
            {
                Slug = parentForRelated.Slug,
                Title = parentForRelated.Title,
                KindLabel = LookupKindLabel(parentForRelated.KindCode),
                Period = FormatRelatedPeriod(parentForRelated),
                HasOwnPage = !SeriesClassifier.IsMovieShortChild(parentForRelated),
                RelationCode = s.RelationToParent ?? "",
                RelationLabelJa = (!string.IsNullOrEmpty(s.RelationToParent)
                    && _relationKindReverseLabelMapCache.TryGetValue(s.RelationToParent, out var parentLbl))
                    ? parentLbl
                    : ""
            });
        }
        // 2) 自身の子（自分が親に当たる全件）を公開日昇順で追加。バッジは name_ja_reverse（親→子方向）。
        var allRelated = _ctx.Series
            .Where(x => x.ParentSeriesId == s.SeriesId)
            .OrderBy(x => x.StartDate)
            .ToList();
        relatedWorks.AddRange(allRelated.Select(x => new RelatedSeriesRow
        {
            Slug = x.Slug,
            Title = x.Title,
            KindLabel = LookupKindLabel(x.KindCode),
            Period = FormatRelatedPeriod(x),
            HasOwnPage = !SeriesClassifier.IsMovieShortChild(x),
            RelationCode = x.RelationToParent ?? "",
            RelationLabelJa = (!string.IsNullOrEmpty(x.RelationToParent)
                && _relationKindForwardLabelMapCache.TryGetValue(x.RelationToParent, out var childLbl))
                ? childLbl
                : ""
        }));

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
                Period = FormatRelatedPeriod(p),
                HasOwnPage = !SeriesClassifier.IsMovieShortChild(p)
            };
        }

        // シリーズ詳細の基本情報・エピソード一覧見出し用の期間／話数。
        // credit_attach_to=EPISODE（TV/SPIN-OFF/OTONA/SHORT）は終了日未確定なら「〜」止め期間表記、
        // credit_attach_to=SERIES（MOVIE/MOVIE_SHORT/SPRING/EVENT）は開始日単独 or 両端表記。
        bool episodeAttaching = SeriesClassifier.IsEpisodeAttaching(s, _ctx.SeriesKindByCode);
        string seriesPeriod = episodeAttaching
            ? JpDateFormat.PeriodOrOngoing(s.StartDate, s.EndDate)
            : JpDateFormat.Period(s.StartDate, s.EndDate);
        // 「期間」見出しラベルもクレジット添付方針で切り替え：エピソード単位なら「期間」、
        // シリーズ単位（映画系・イベント）なら単一時点なので「公開」とだけ出す。
        string periodLabel = episodeAttaching ? "期間" : "公開";
        // 実話数が総話数に満たない継続中シリーズは「（見込）」を別 span 用注記として保持。
        // 終了日が確定済みのときのみ期間側へ注記（継続中は期間が「〜」止めなので総話数側のみ）。
        bool seriesEstimated = episodeAttaching && IsEpisodesEstimated(s);
        // EpisodeSection の総話数ラベル：マスタ値があれば「全 N 話」、無い場合は
        // 「継続中で総話数未確定」（episodeAttaching かつ EndDate=null）ならブランク、
        // それ以外（完結だが総話数マスタ未入力等）は登録済み実話数のフォールバック。
        string episodeSectionTotalLabel;
        if (s.Episodes.HasValue)
        {
            episodeSectionTotalLabel = $"全 {s.Episodes.Value} 話";
        }
        else if (episodeAttaching && !s.EndDate.HasValue)
        {
            episodeSectionTotalLabel = "";
        }
        else
        {
            episodeSectionTotalLabel = $"{epRows.Count} 話";
        }

        var seriesView = new SeriesDetailView
        {
            Slug = s.Slug,
            Title = s.Title,
            TitleKana = s.TitleKana ?? "",
            TitleEn = s.TitleEn ?? "",
            KindLabel = LookupKindLabel(s.KindCode),
            Period = seriesPeriod,
            PeriodLabel = periodLabel,
            PeriodEstimateNote = (seriesEstimated && s.EndDate.HasValue) ? EstimateNote : "",
            Episodes = s.Episodes?.ToString() ?? "",
            EpisodesEstimateNote = (seriesEstimated && s.Episodes.HasValue) ? EstimateNote : "",
            RunTimeSeconds = s.RunTimeSeconds?.ToString() ?? "",
            ToeiAnimOfficialSiteUrl = s.ToeiAnimOfficialSiteUrl ?? "",
            ToeiAnimLineupUrl = s.ToeiAnimLineupUrl ?? "",
            AbcOfficialSiteUrl = s.AbcOfficialSiteUrl ?? "",
            AmazonPrimeDistributionUrl = s.AmazonPrimeDistributionUrl ?? "",
            HasEpisodeList = hasEpisodes,
            // エピソード一覧を /episodes/ ランディングと同一の episodes-index-section
            // 構造で描画するための見出し情報（単一シリーズなのでセクションは 1 個）。
            EpisodeSectionStartYearLabel = s.StartDate.Year.ToString(),
            EpisodeSectionPeriod = seriesPeriod,
            EpisodeSectionPeriodEstimateNote = (seriesEstimated && s.EndDate.HasValue) ? EstimateNote : "",
            EpisodeSectionTotalLabel = episodeSectionTotalLabel,
            EpisodeSectionTotalEstimateNote = (seriesEstimated && s.Episodes.HasValue) ? EstimateNote : ""
        };
        seriesView.HasExternalUrls =
            seriesView.ToeiAnimOfficialSiteUrl.Length > 0 ||
            seriesView.ToeiAnimLineupUrl.Length > 0 ||
            seriesView.AbcOfficialSiteUrl.Length > 0 ||
            seriesView.AmazonPrimeDistributionUrl.Length > 0;

        // メインスタッフセクション。
        var keyStaffSections = await BuildMainStaffSectionsAsync(s, eps, ct).ConfigureAwait(false);

        // 当該シリーズに紐付くプリキュア一覧。
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

        // 映画作品の BGM リスト。映画系シリーズ（MOVIE / MOVIE_SHORT / SPRING / EVENT）の
        var movieBgmRows = new List<MovieBgmCueRow>();
        if (IsMovieKind(s.KindCode) || string.Equals(s.KindCode, "EVENT", StringComparison.Ordinal))
        {
            var cues = await _movieBgmCuesRepo.GetBySeriesAsync(s.SeriesId, ct).ConfigureAwait(false);
            if (cues.Count > 0)
            {
                // track_content_kinds の和名辞書（kind_code → name_ja）。
                var kindNameByCode = new Dictionary<string, string>(StringComparer.Ordinal);
                var kinds = await _trackContentKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
                foreach (var k in kinds) kindNameByCode[k.KindCode] = k.NameJa;

                foreach (var c in cues)
                {
                    movieBgmRows.Add(new MovieBgmCueRow
                    {
                        Seq = c.Seq,
                        SubSeq = c.SubSeq,
                        MNo = c.MNo ?? "",
                        KindLabel = kindNameByCode.TryGetValue(c.ContentKindCode, out var kn)
                            ? kn : c.ContentKindCode,
                        Title = c.Title ?? "",
                        Notes = c.Notes ?? "",
                        IsUnused = c.IsUnused,
                        IsMissing = c.IsMissing,
                    });
                }
            }
        }

        var content = new SeriesDetailModel
        {
            Series = seriesView,
            Episodes = epRows,
            RelatedWorks = relatedWorks,
            Parent = parent,
            KeyStaffSections = keyStaffSections,
            Precures = precureRows,
            MovieBgmCues = movieBgmRows,
            CoverageLabel = _ctx.CreditCoverageLabel
        };

        // 説明文を実データから動的構築する。
        // 「シリーズ名・放送開始年・話数・主役プリキュア声優」を含めて、SERP / OGP / Twitter Card で
        // シリーズ単位の個別性が立つようにする。140 字目安。
        var metaDescription = BuildSeriesMetaDescription(s, precureRows);

        // JSON-LD（TVSeries / Movie）
        // 後段：略称（series.title_short）は生成・UI ともに一切使わない方針。
        // alternateName に title_short は出力しない。
        // description を MetaDescription と揃え、actor 配列に主役プリキュア声優を、
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

        // 主役プリキュアの声優を Person 型配列で actor プロパティに乗せる。
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

    /// <summary>シリーズ詳細ページの &lt;meta name="description"&gt; 用説明文を実データから組み立てる。</summary>
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
    /// 本メソッドの戻り値を <see cref="_episodeStaffByIdCache"/> に memoize するように変更。
    /// 同一エピソードで複数回呼ばれた場合はキャッシュから返す（実際にはシリーズ詳細ページ生成での 1 回のみだが、
    /// パイプライン後段 <see cref="EpisodesIndexGenerator"/> から <see cref="GetEpisodeStaffSummaries"/> 経由で
    /// 全エピソード分のサマリを参照させるための副次効果が主目的）。
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

        // クレジットと階層 6 段はすべて SiteDataLoader が事前展開済み（BuildContext.CreditsByEpisode /
        // CreditTree.CardsByCreditId）。本メソッドは per-episode に呼ばれるが、内部の DB アクセスは
        // 完全に消えて辞書 lookup と foreach だけで完結する。CreditTreeIndex は 5 段ネスト構造で
        // cards → tiers → groups → cardRoles → blocks → entries を直接辿れるため、6 階層 Repository への
        // GetBy*Async は不要。エントリの is_broadcast_only=0 フィルタは旧 per-block ロード時と同じ条件で適用。
        var credits = _ctx.CreditsByEpisode.TryGetValue(episodeId, out var creditList)
            ? creditList.Where(c => !c.IsDeleted)
            : Enumerable.Empty<Credit>();

        foreach (var credit in credits)
        {
            if (!_ctx.CreditTree.CardsByCreditId.TryGetValue(credit.CreditId, out var cardSnapshots)) continue;
            foreach (var cardSnap in cardSnapshots.OrderBy(c => c.Card.CardSeq))
            {
                foreach (var tierSnap in cardSnap.Tiers.OrderBy(t => t.Tier.TierNo))
                {
                    foreach (var groupSnap in tierSnap.Groups.OrderBy(g => g.Group.GroupNo))
                    {
                        foreach (var roleSnap in groupSnap.Roles.OrderBy(r => r.Role.OrderInGroup))
                        {
                            int? bucket = ClassifyRole(roleSnap.Role);
                            if (bucket is null) continue;

                            foreach (var blockSnap in roleSnap.Blocks.OrderBy(b => b.Block.BlockSeq))
                            {
                                foreach (var e in blockSnap.Entries
                                    .Where(en => !en.IsBroadcastOnly)
                                    .OrderBy(en => en.EntrySeq))
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
            // 絵コンテ／演出のキー集合が一致（両方非空＋同一）のとき、テンプレで統合表示を許可するフラグ。
            // 同じ人物が両方を兼任しているクレジット表現に対して「絵コンテ・演出 ○○」の 1 表記にまとめる。
            StoryboardDirectorMerged = seenSb.Count > 0 && seenDr.Count > 0 && seenSb.SetEquals(seenDr)
        };
        // 本メソッドの結果を episode_id キーでキャッシュ。
        // 後段 EpisodesIndexGenerator で GetEpisodeStaffSummaries() 経由参照される。
        _episodeStaffByIdCache[episodeId] = result;
        return result;
    }

    /// <summary>PERSON / TEXT エントリから (重複判定キー, 表示用 HTML 文字列) を取り出す。</summary>
    private async Task<(string Key, string Html)> ResolveStaffEntryAsync(CreditBlockEntry e, CancellationToken ct)
    {
        // DB アクセスは BuildContext 由来の辞書 lookup に置き換わったため本体に await は残らないが、
        // async シグネチャは将来の DB アクセス追加余地として温存する。
        await Task.CompletedTask;
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    // person_alias は BuildContext.PersonAliasById で全件辞書化済み。
                    if (!_personAliasCache.TryGetValue(pid, out var pa))
                    {
                        pa = _ctx.PersonAliasById.TryGetValue(pid, out var found) ? found : null;
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

    /// <summary>メインスタッフセクション群を構築する。</summary>
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
        // 屋号 alias_id → 名前 マップ。
        // 一覧サブ行の集計（BuildKeyStaffSummaryBySeriesCacheAsync）でも同じキャッシュを使うが、
        // 詳細ページ生成パスから直接呼ばれるケースに備えてここでも遅延ロードしておく。
        if (_companyAliasNameMapCache is null)
        {
            var allAliases = await _companyAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
            _companyAliasNameMapCache = allAliases.ToDictionary(a => a.AliasId, a => a.Name);
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
                // 所属屋号 alias_id の出現回数辞書（最頻 ID を「(屋号)」併記の表示元として採用）。
                var affiliationCounts = new Dictionary<int, int>();
                // クレジット順ソートキーの lex min。1 件でも当該シリーズ・役職の
                // エピソードスコープ Involvement があれば更新される。
                int sortEpNo = int.MaxValue;
                int sortCreditSeq = int.MaxValue;
                int sortCreditSubSeq = int.MaxValue;
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
                            // (EpisodeNo, CreditSeq, CreditSubSeq) の辞書順比較で lex min を更新。
                            if (epNo < sortEpNo
                                || (epNo == sortEpNo && inv.CreditSeq < sortCreditSeq)
                                || (epNo == sortEpNo && inv.CreditSeq == sortCreditSeq && inv.CreditSubSeq < sortCreditSubSeq))
                            {
                                sortEpNo = epNo;
                                sortCreditSeq = inv.CreditSeq;
                                sortCreditSubSeq = inv.CreditSubSeq;
                            }
                        }
                        // 所属屋号 alias_id の出現回数（エピソードスコープ・SERIES スコープを問わず数える。
                        // シリーズ詳細のメインスタッフは数話単位の所属変更を捕捉する場ではないため、
                        // 当該役職に紐付いた全 Involvement の所属を母集団にして最頻 1 件を選ぶ）。
                        if (inv.AffiliationCompanyAliasId is int affId)
                        {
                            affiliationCounts.TryGetValue(affId, out var c);
                            affiliationCounts[affId] = c + 1;
                        }
                    }
                }
                if (episodeNos.Count == 0) continue;

                bool isAllEpisodes = allEpisodeNos.Count > 0
                    && episodeNos.SetEquals(allEpisodeNos);
                string rangeLabel = isAllEpisodes
                    ? string.Empty
                    : EpisodeRangeCompressor.Compress(episodeNos);

                // 最頻所属屋号（同点は alias_id 昇順でタイブレーク）。
                // 一覧サブ行の KeyStaffMember.AffiliationLabel と同じ規律で揃える。
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

                rows.Add(new MainStaffRow
                {
                    PersonId = p.PersonId,
                    // Person.FullName は string? 型のため空文字へフォールバック（NULL 警告の抑制）。
                    FullName = p.FullName ?? "",
                    RangeLabel = rangeLabel,
                    AffiliationLabel = affiliationLabel,
                    SortEpNo = sortEpNo,
                    SortCreditSeq = sortCreditSeq,
                    SortCreditSubSeq = sortCreditSubSeq
                });
            }

            if (rows.Count == 0) continue;

            // クレジット順（EpisodeNo → CreditSeq → CreditSubSeq の辞書順）昇順でソート。
            // 三つ組は人物ごとに lex min が異なる前提なのでこれだけで序列は確定する。
            rows.Sort((a, b) =>
            {
                int c = a.SortEpNo.CompareTo(b.SortEpNo);
                if (c != 0) return c;
                c = a.SortCreditSeq.CompareTo(b.SortCreditSeq);
                if (c != 0) return c;
                return a.SortCreditSubSeq.CompareTo(b.SortCreditSubSeq);
            });

            sections.Add(new KeyStaffSection
            {
                RoleCode = spec.Code,
                // バッジリンク先は PathUtil 経由で組み立て、URL パス上の役職コードを
                // 小文字化する。現行挙動どおり spec.Code をそのまま対象にする
                // （系譜代表への置換は行わない）。data-role-code 属性側は
                // バッジ色分け CSS の都合で実コード（大文字）のまま使う。
                RoleUrl = PathUtil.RoleStatsUrl(spec.Code),
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
        /// <summary>映画セクション用：親映画 + ぶら下がる子作品 + シーズンバッジ情報。 映画セクションは親子配置に対応した <see cref="MovieSeriesRow"/> のリストで渡す。</summary>
        public IReadOnlyList<MovieSeriesRow> MovieSeries { get; set; } = Array.Empty<MovieSeriesRow>();
        /// <summary>大人向けスピンオフ（<c>kind_code='OTONA'</c>）セクション。 スピンオフは 4 種別に細分化して扱う。 行 DTO は TV と共通の <see cref="TvSeriesRow"/> を流用。</summary>
        public IReadOnlyList<TvSeriesRow> OtonaSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>ショートアニメ（<c>kind_code='SHORT'</c>）セクション。</summary>
        public IReadOnlyList<TvSeriesRow> ShortSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>イベント（<c>kind_code='EVENT'</c>）セクション。3D シアター等の上映イベント枠。</summary>
        public IReadOnlyList<TvSeriesRow> EventSeries { get; set; } = Array.Empty<TvSeriesRow>();
        /// <summary>スピンオフセクション（<c>kind_code='SPIN-OFF'</c>）。狭義のスピンオフ作品のみ。 スピンオフ系のうち OTONA / SHORT / EVENT は別セクションに分離し、 ここは純粋な SPIN-OFF のみに範囲縮小。行 DTO は TV と共通の <see cref="TvSeriesRow"/> を流用。</summary>
        public IReadOnlyList<TvSeriesRow> SpinOffSeries { get; set; } = Array.Empty<TvSeriesRow>();
        public int TotalCount { get; set; }
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の lead 段落末尾に表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>TV シリーズ／スピンオフ一覧の 1 行分。連番付きの表形式で描画される。 <c>Children</c> プロパティは持たない（TV の下に子作品を字下げ表示しないため）。</summary>
    private sealed class TvSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Period { get; set; } = "";
        /// <summary>放送期間の見込み注記（「（見込）」または空文字）。 TV シリーズで終了日が確定済みかつ実話数が総話数未満のとき「（見込）」が入る。 テンプレ側で nowrap の別 span に入れ、列幅のガタつきを防ぐ。</summary>
        public string PeriodEstimateNote { get; set; } = "";
        public string EpisodesLabel { get; set; } = "";
        /// <summary>総話数の見込み注記（「（見込）」または空文字）。 TV シリーズで実話数が総話数マスタ値に満たないとき「（見込）」が入る。 テンプレ側で nowrap の別 span に入れ、列幅のガタつきを防ぐ。</summary>
        public string EpisodesEstimateNote { get; set; } = "";
        /// <summary>シリーズ一覧の複合行サブ情報用：プリキュアバッジ群。</summary>
        public IReadOnlyList<PrecureBadge> PrecureBadges { get; set; } = Array.Empty<PrecureBadge>();
        /// <summary>シリーズ一覧の複合行サブ情報用：メインスタッフ簡易サマリ。</summary>
        public IReadOnlyList<KeyStaffRoleGroup> KeyStaffSummary { get; set; } = Array.Empty<KeyStaffRoleGroup>();
    }

    /// <summary>シリーズ一覧 TV サブ行用：1 役職分の集計結果。</summary>
    private sealed class KeyStaffRoleGroup
    {
        /// <summary>役職コード（バッジ色マッピング用、CSS の <c>data-role-code</c> 属性として出力）。</summary>
        public string RoleCode { get; set; } = "";
        /// <summary>代表 role_code（系譜クラスタ代表。集計・参照用に保持）。</summary>
        public string RepRoleCode { get; set; } = "";
        /// <summary>バッジリンク先の組み立て済み URL（<c>/stats/roles/{小文字代表コード}/</c>）。 テンプレ側はこの値のみリンク href に使い、生の役職コードを URL に直接埋めない。</summary>
        public string RoleUrl { get; set; } = "";
        /// <summary>表示ラベル（「プロデューサー」「シリーズ構成」等）。</summary>
        public string RoleLabel { get; set; } = "";
        /// <summary>当該役職に該当する人物群（クレジット順 = (EpisodeNo, CreditSeq, CreditSubSeq) lex min 昇順）。</summary>
        public IReadOnlyList<KeyStaffMember> Members { get; set; } = Array.Empty<KeyStaffMember>();
    }

    /// <summary>シリーズ一覧 TV サブ行用：1 役職グループ内の 1 名分。</summary>
    private sealed class KeyStaffMember
    {
        public int PersonId { get; set; }
        /// <summary>表示用人物名（<c>persons.full_name</c>）。</summary>
        public string DisplayName { get; set; } = "";
        /// <summary>所属屋号の表示ラベル（当該シリーズ内最頻、<c>company_aliases.name</c> をそのまま使用）。 屋号未指定なら空文字でテンプレ側はカッコ含めて出さない。</summary>
        public string AffiliationLabel { get; set; } = "";
        /// <summary>クレジット順ソートキー第 1：当該シリーズ・役職での最小エピソード番号（テンプレでは未表示）。</summary>
        public int SortEpNo { get; set; }
        /// <summary>クレジット順ソートキー第 2：その最小エピソード番号内での最小 <c>CreditSeq</c>（テンプレでは未表示）。</summary>
        public int SortCreditSeq { get; set; }
        /// <summary>クレジット順ソートキー第 3：同 <c>CreditSeq</c> 内での最小 <c>CreditSubSeq</c>（テンプレでは未表示）。</summary>
        public int SortCreditSubSeq { get; set; }
    }

    /// <summary>シリーズ詳細・シリーズ一覧サブ行で使うプリキュア表示行。 <c>series_precures</c> の 1 紐付けを、変身前名・変身後名・正式名称・声優名・バッジ地色に 解決した表示用 DTO。</summary>
    private sealed class SeriesPrecureDisplay
    {
        public int PrecureId { get; set; }
        public string TransformName { get; set; } = "";
        /// <summary>変身後 2（強化形態など）の名義名。無ければ空文字。</summary>
        public string Transform2Name { get; set; } = "";
        public string PreTransformName { get; set; } = "";
        /// <summary>変身後名義が属するキャラクターの正式名称（characters.name）。</summary>
        public string CharacterName { get; set; } = "";
        public string VoiceActorName { get; set; } = "";
        public int? VoiceActorPersonId { get; set; }
        /// <summary>シリーズ一覧プリキュアバッジの地色（<c>#RRGGBB</c>。未設定または不正値は空文字）。</summary>
        public string KeyColor { get; set; } = "";
    }

    /// <summary>シリーズ一覧 TV サブ行用：プリキュア 1 体分のバッジ表示データ。</summary>
    private sealed class PrecureBadge
    {
        /// <summary>プリキュア詳細 <c>/precures/{id}/</c> へのリンク用 ID。</summary>
        public int PrecureId { get; set; }
        /// <summary>バッジ表示テキスト（「美墨なぎさ (CV: 本名陽子)」等）。</summary>
        public string Label { get; set; } = "";
        /// <summary>地色（<c>#RRGGBB</c>）。空文字なら CSS 既定の淡色。</summary>
        public string BackgroundColor { get; set; } = "";
        /// <summary>文字色（地色輝度から自動算出した暗/明グレー）。空文字なら CSS 既定。</summary>
        public string TextColor { get; set; } = "";
        /// <summary>ボーダー色（文字色側に寄せた半透明）。空文字なら CSS 既定。</summary>
        public string BorderColor { get; set; } = "";
    }

    /// <summary>映画セクションの親映画行 DTO。 親映画 1 作品 + その下にぶら下がる子作品（'MOVIE_SHORT'）+ シーズンバッジ情報 + 尺合計ラベルを持つ。</summary>
    private sealed class MovieSeriesRow
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Period { get; set; } = "";
        /// <summary>シーズンバッジ用 CSS クラス名（"movie-badge-fall" / "movie-badge-spring"）。空文字なら無印。</summary>
        public string SeasonBadgeClass { get; set; } = "";
        /// <summary>シーズンバッジに表示するラベル文字列（「秋映画」「春映画」）。</summary>
        public string SeasonBadgeLabel { get; set; } = "";
        /// <summary>親 + 子（MOVIE_SHORT）合計の上映時間ラベル（「m分ss秒」形式）。</summary>
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
        /// <summary>子作品（MOVIE_SHORT・併映短編）単体の上映時間ラベル（「m分ss秒」形式）。</summary>
        public string RuntimeLabel { get; set; } = "";
        /// <summary>子作品（HasOwnPage=false）はリンク化せず表示のみ行う。</summary>
        public bool HasOwnPage { get; set; } = true;
        /// <summary>親に対する関係種別コード。</summary>
        public string RelationCode { get; set; } = "";
        /// <summary>
        /// 親 → 子方向の関係表示名。
        /// 例：自身が無印 TV シリーズ、子が映画版なら「TVシリーズ」ではなく「映画」が逆向きで入る
        /// （「自分の映画版」という親側からの見え方）。SEQUEL の逆向きは「前作」ではなく
        /// 「続編」のままで、SEGMENT は「パート作品」、MOVIE は「映画」、COFEATURE は「併映」。
        /// マスタ未登録時や reverse 値空文字のフォールバック後で空のときはバッジを描画しない。
        /// </summary>
        public string RelationLabelJa { get; set; } = "";
    }

    private sealed class SeriesDetailModel
    {
        public SeriesDetailView Series { get; set; } = new();
        public IReadOnlyList<EpisodeIndexRow> Episodes { get; set; } = Array.Empty<EpisodeIndexRow>();
        /// <summary>
        /// 「関連作品」セクション用。
        /// 単独ページを持たない作品（MOVIE_SHORT 等）と単独ページを持つ作品（続編・スピンオフ等）を
        /// 1 つのリストにまとめて保持する。ソート順は公開日（StartDate）昇順、同日内は seq_in_parent → series_id 昇順。
        /// 各行は <see cref="RelatedSeriesRow.HasOwnPage"/> でリンク化要否を、<see cref="RelatedSeriesRow.RelationLabelJa"/> で
        /// バッジ表示文字列（series_relation_kinds.name_ja_reverse）を持つ。
        /// </summary>
        public IReadOnlyList<RelatedSeriesRow> RelatedWorks { get; set; } = Array.Empty<RelatedSeriesRow>();
        public RelatedSeriesRow? Parent { get; set; }
        public IReadOnlyList<KeyStaffSection> KeyStaffSections { get; set; } = Array.Empty<KeyStaffSection>();
        /// <summary>このシリーズに登場するプリキュア一覧。</summary>
        public IReadOnlyList<SeriesPrecureRow> Precures { get; set; } = Array.Empty<SeriesPrecureRow>();
        /// <summary>映画作品の BGM リスト。映画系シリーズ（MOVIE / MOVIE_SHORT / SPRING / EVENT）のときのみ <c>movie_bgm_cues</c> から取得した行が入る。TV シリーズや 紐付けが 0 件のときは空で、テンプレ側はセクション自体を描画しない。 並び順は (seq, sub_seq, movie_bgm_cue_id) 昇順（リポジトリ側で確定済み）。</summary>
        public IReadOnlyList<MovieBgmCueRow> MovieBgmCues { get; set; } = Array.Empty<MovieBgmCueRow>();
        /// <summary>クレジット横断カバレッジラベル。 テンプレ側の h1 ブロック直後に独立段落で表示する。</summary>
        public string CoverageLabel { get; set; } = "";
    }

    /// <summary>映画 BGM リストの 1 行 DTO（テンプレ描画用）。</summary>
    private sealed class MovieBgmCueRow
    {
        public int Seq { get; set; }
        public int SubSeq { get; set; }
        public string MNo { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Title { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool IsUnused { get; set; }
        public bool IsMissing { get; set; }
    }

    /// <summary>シリーズ詳細のプリキュアセクション行 DTO。 変身前名 / 変身後名 / 声優を 1 行で持ち、テンプレ側で表組みする。</summary>
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
        /// <summary>役職コード（バッジ色分け CSS の data-role-code 属性用、実コードのまま）。</summary>
        public string RoleCode { get; set; } = "";
        /// <summary>役職統計ページへの組み立て済み URL（<c>/stats/roles/{小文字コード}/</c>）。 テンプレ側はこの値のみリンク href に使い、生の役職コードを URL に直接埋めない。</summary>
        public string RoleUrl { get; set; } = "";
        public string RoleLabel { get; set; } = "";
        public IReadOnlyList<MainStaffRow> Members { get; set; } = Array.Empty<MainStaffRow>();
    }

    private sealed class MainStaffRow
    {
        public int PersonId { get; set; }
        public string FullName { get; set; } = "";
        public string RangeLabel { get; set; } = "";
        /// <summary>所属屋号の表示ラベル（当該シリーズ・役職内最頻、<c>company_aliases.name</c> をそのまま使用）。 屋号未指定なら空文字でテンプレ側はカッコ含めて出さない。</summary>
        public string AffiliationLabel { get; set; } = "";
        /// <summary>クレジット順ソートキー第 1：当該シリーズ・役職での最小エピソード番号（テンプレでは未表示）。</summary>
        public int SortEpNo { get; set; }
        /// <summary>クレジット順ソートキー第 2：その最小エピソード番号内での最小 <c>CreditSeq</c>（テンプレでは未表示）。</summary>
        public int SortCreditSeq { get; set; }
        /// <summary>クレジット順ソートキー第 3：同 <c>CreditSeq</c> 内での最小 <c>CreditSubSeq</c>（テンプレでは未表示）。</summary>
        public int SortCreditSubSeq { get; set; }
    }

    private sealed class SeriesDetailView
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleKana { get; set; } = "";
        public string TitleEn { get; set; } = "";
        public string KindLabel { get; set; } = "";
        public string Period { get; set; } = "";
        /// <summary>「期間」見出しラベル。credit_attach_to=EPISODE なら「期間」、SERIES なら「公開」。テンプレの基本情報テーブル左セル文字列。</summary>
        public string PeriodLabel { get; set; } = "";
        /// <summary>期間の見込み注記（「（見込）」または空文字）。基本情報テーブルで nowrap span に入れる。</summary>
        public string PeriodEstimateNote { get; set; } = "";
        public string Episodes { get; set; } = "";
        /// <summary>総話数の見込み注記（「（見込）」または空文字）。基本情報テーブルで nowrap span に入れる。</summary>
        public string EpisodesEstimateNote { get; set; } = "";
        public string RunTimeSeconds { get; set; } = "";
        public string ToeiAnimOfficialSiteUrl { get; set; } = "";
        public string ToeiAnimLineupUrl { get; set; } = "";
        public string AbcOfficialSiteUrl { get; set; } = "";
        public string AmazonPrimeDistributionUrl { get; set; } = "";
        public bool HasExternalUrls { get; set; }
        public bool HasEpisodeList { get; set; }
        /// <summary>エピソード一覧 episodes-index-section 見出し用：シリーズ開始年（西暦 4 桁）。</summary>
        public string EpisodeSectionStartYearLabel { get; set; } = "";
        /// <summary>エピソード一覧 episodes-index-section 見出し用：放送・公開期間（TV は「〜」止め対応済み）。</summary>
        public string EpisodeSectionPeriod { get; set; } = "";
        /// <summary>同見出しの放送期間見込み注記（「（見込）」または空文字）。</summary>
        public string EpisodeSectionPeriodEstimateNote { get; set; } = "";
        /// <summary>同見出しの話数ラベル（「全 N 話」、総話数未設定時は実話数「N 話」）。</summary>
        public string EpisodeSectionTotalLabel { get; set; } = "";
        /// <summary>同見出しの話数見込み注記（「（見込）」または空文字）。</summary>
        public string EpisodeSectionTotalEstimateNote { get; set; } = "";
    }

    private sealed class EpisodeIndexRow
    {
        public int SeriesEpNo { get; set; }
        public string TitleText { get; set; } = "";
        /// <summary>
        /// ルビ付きサブタイトル HTML。
        /// DB の <c>episodes.title_rich_html</c> をそのまま流す。テンプレ側で空判定して
        /// 非空なら本 HTML を、空なら <see cref="TitleText"/> のエスケープ平文を表示する。
        /// 行内の <c>\r\n</c> / <c>\n</c> は事前にスペースへ置換済み（series-detail のエピソード一覧では
        /// サブタイトルを 1 行で見せるため、ルビ HTML 内の改行はスペースに置換する）。
        /// </summary>
        public string TitleRichHtml { get; set; } = "";
        public string OnAirDate { get; set; } = "";
        /// <summary>エピソード詳細ページへの URL（<c>/series/{slug}/{seriesEpNo}/</c>）。 /episodes/ ランディングと同一の episodes-index-section 構造で サブタイトルをリンク化するために保持する。</summary>
        public string EpisodeUrl { get; set; } = "";
        public string Screenplay { get; set; } = "";
        public string Storyboard { get; set; } = "";
        public string EpisodeDirector { get; set; } = "";
        public string AnimationDirector { get; set; } = "";
        public string ArtDirector { get; set; } = "";
        /// <summary>絵コンテと演出が同じ人物（PERSON エントリの重複キー集合が一致 + 両方非空）かどうか。
        /// true のときテンプレ側でエピソード一覧の当該行を「絵コンテ・演出 ○○」の 1 表記に統合する。
        /// false のときは「絵コンテ ○○ / 演出 ○○」と 2 つ独立して並べる。</summary>
        public bool StoryboardDirectorMerged { get; set; }
    }

    // EpisodeStaffSummary は PrecureDataStars.SiteBuilder/Utilities/EpisodeStaffSummary.cs に
    // 公開クラスとして外出し（/episodes/ ランディング生成 EpisodesIndexGenerator からも参照する）。
}