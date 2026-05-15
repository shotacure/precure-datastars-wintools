using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// エピソード詳細ページ <c>/series/{slug}/{seriesEpNo}/</c> の生成。
/// <para>
/// 本ジェネレータがサイト全体の中核。以下を 1 ページに集約する:
/// </para>
/// <list type="bullet">
///   <item>サブタイトル（プレーン + ルビ + かな）</item>
///   <item>外部 URL（東映あらすじ／ラインナップ／YouTube 予告）</item>
///   <item>放送日時（duration_minutes が登録されていれば「8:30〜9:00」形式の終了時刻併記）</item>
///   <item>フォーマット表（OA / 配信(Amazon Prime) / Blu-ray・DVD の累積タイムコード）</item>
///   <item>サブタイトル文字情報（初出 / 唯一 / N年Mか月ぶり、いま現在の参照点キャプション付き）</item>
///   <item>サブタイトル文字統計（title_char_stats JSON のカテゴリ別件数表示）</item>
///   <item>パート尺偏差値（AVANT/PART_A/PART_B シリーズ内・歴代、2 段ヘッダ表）</item>
///   <item>主題歌（OP / ED / 挿入歌、本放送限定行も区別表示。テーブルではなく縦リスト 1 行表現）</item>
///   <item>クレジット階層（OP / ED、役職／名義／屋号／ロゴをそれぞれの詳細ページにリンク）</item>
///   <item>前後話ページネーション（端ボタンに「#N サブタイトル」のラベル付き）</item>
/// </list>
/// </summary>
public sealed class EpisodeGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    // ── 既存リポジトリ群 ──
    private readonly EpisodesRepository _episodesRepo;
    private readonly EpisodePartsRepository _partsRepo;
    private readonly EpisodeThemeSongsRepository _themeRepo;
    private readonly SongRecordingsRepository _songRecRepo;
    private readonly SongsRepository _songsRepo;
    private readonly CreditsRepository _creditsRepo;
    private readonly CreditKindsRepository _creditKindsRepo;
    // v1.3.0 公開直前のデザイン整理 第 N+2 弾：エピソード詳細の主題歌・挿入歌セクションで
    // 「作詞・作曲・編曲・歌」を構造化クレジット由来でリンク付き表示するために追加。
    // 楽曲詳細ページと同じ仕組み（song_credits / song_recording_singers）を共有する。
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecSingersRepo;
    private readonly PersonAliasesRepository _personAliasesRepoForSongs;
    private readonly CharacterAliasesRepository _characterAliasesRepoForSongs;

    // ── スタッフ抽出用（クレジット階層を辿って役職コード → 配下エントリを引く） ──
    private readonly CreditCardsRepository _staffCardsRepo;
    private readonly CreditCardTiersRepository _staffTiersRepo;
    private readonly CreditCardGroupsRepository _staffGroupsRepo;
    private readonly CreditCardRolesRepository _staffCardRolesRepo;
    private readonly CreditRoleBlocksRepository _staffBlocksRepo;
    private readonly CreditBlockEntriesRepository _staffEntriesRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;

    // ── クレジット種別マスタの一括キャッシュ（kind_code → 表示名）。コンストラクタでは
    // 構築しない（GetAllAsync が非同期のため）。最初に必要になったタイミングで遅延ロードする。
    private IReadOnlyDictionary<string, string>? _creditKindLabelMap;

    // ── 役職マスタキャッシュ（role_code → Role）。スタッフ抽出ロジックで使う。
    private IReadOnlyDictionary<string, Role>? _roleMap;

    // ── スタッフ名リンク化（人物名義 → 人物詳細ページへのリンク化用） ──
    private readonly StaffNameLinkResolver _staffLinkResolver;

    // ── 役職コードリンク化（v1.3.0 続編 追加）：エピソード詳細のスタッフセクションで
    //    脚本／絵コンテ／演出／作画監督／美術 の各役職ラベルを役職統計ページ
    //    /stats/roles/{rep_role_code}/ にリンクするのに使う。
    //    系譜代表の role_code を引くだけのため、Persons/CompaniesGenerator と同じ Resolver を共有する。
    private readonly RoleSuccessorResolver _roleSuccessorResolver;

    // ── 使用音声（episode_uses）解決用リポジトリ群 ──
    private readonly EpisodeUsesRepository _episodeUsesRepo;
    private readonly BgmCuesRepository _bgmCuesRepo;
    private readonly TrackContentKindsRepository _trackContentKindsRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    private readonly PartTypesRepository _partTypesRepo;

    // ── 描画ヘルパ ──
    private readonly TitleCharInfoRenderer _titleCharInfo;
    private readonly CreditTreeRenderer _creditRenderer;

    public EpisodeGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        StaffNameLinkResolver staffLinkResolver,
        RoleSuccessorResolver roleSuccessorResolver)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _staffLinkResolver = staffLinkResolver;
        _roleSuccessorResolver = roleSuccessorResolver;

        _episodesRepo = new EpisodesRepository(factory);
        _partsRepo = new EpisodePartsRepository(factory);
        _themeRepo = new EpisodeThemeSongsRepository(factory);
        _songRecRepo = new SongRecordingsRepository(factory);
        _songsRepo = new SongsRepository(factory);
        _creditsRepo = new CreditsRepository(factory);
        _creditKindsRepo = new CreditKindsRepository(factory);
        // 主題歌・挿入歌セクションの構造化クレジット表示用（v1.3.0 公開直前のデザイン整理 第 N+2 弾）。
        _songCreditsRepo = new SongCreditsRepository(factory);
        _songRecSingersRepo = new SongRecordingSingersRepository(factory);
        _personAliasesRepoForSongs = new PersonAliasesRepository(factory);
        _characterAliasesRepoForSongs = new CharacterAliasesRepository(factory);

        // スタッフ抽出用にクレジット階層 Repository を再利用（CreditTreeRenderer と同じ方法で参照）。
        _staffCardsRepo = new CreditCardsRepository(factory);
        _staffTiersRepo = new CreditCardTiersRepository(factory);
        _staffGroupsRepo = new CreditCardGroupsRepository(factory);
        _staffCardRolesRepo = new CreditCardRolesRepository(factory);
        _staffBlocksRepo = new CreditRoleBlocksRepository(factory);
        _staffEntriesRepo = new CreditBlockEntriesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);

        // 使用音声セクション用の Repository。
        _episodeUsesRepo = new EpisodeUsesRepository(factory);
        _bgmCuesRepo = new BgmCuesRepository(factory);
        _trackContentKindsRepo = new TrackContentKindsRepository(factory);
        _songSizeVariantsRepo = new SongSizeVariantsRepository(factory);
        _songPartVariantsRepo = new SongPartVariantsRepository(factory);
        _partTypesRepo = new PartTypesRepository(factory);

        _titleCharInfo = new TitleCharInfoRenderer(_episodesRepo);

        // クレジットレンダラ：Catalog 側 CreditPreviewRenderer と同一仕様。
        // role_templates を引いてテンプレ展開するため RoleTemplatesRepository を渡し、
        // 名義／屋号／ロゴ／キャラの ID → 名前解決を担う LookupCache を別途構築する。
        // クレジット内人物名義を /persons/{id}/ にリンク化するため StaffNameLinkResolver も渡す。
        // v1.3.1 stage 21: テンプレ DSL {ROLE_LINK:code=...} プレースホルダ実装のため、
        // LookupCache に RolesRepository も注入する（役職コード → 表示名 + 統計ページリンク解決用）。
        // _rolesRepo は本ジェネレータが他用途（スタッフセクション抽出）で既に保持しているものを共有。
        var lookup = new LookupCache(
            new PersonAliasesRepository(factory),
            new CompanyAliasesRepository(factory),
            new LogosRepository(factory),
            new CharacterAliasesRepository(factory),
            _rolesRepo,
            factory);
        // v1.3.0 続編：テンプレ展開時の {PERSONS} プレースホルダ等もリンク化したいので、
        // 構築後の LookupCache に StaffNameLinkResolver を後注入する。LookupCache 内部の
        // LookupPersonAliasHtmlAsync がこの resolver を使って「<a href="/persons/{id}/">名義</a>」を返す。
        lookup.SetStaffLinkResolver(staffLinkResolver);

        _creditRenderer = new CreditTreeRenderer(
            factory,
            new RolesRepository(factory),
            new RoleTemplatesRepository(factory),
            new CreditCardsRepository(factory),
            new CreditCardTiersRepository(factory),
            new CreditCardGroupsRepository(factory),
            new CreditCardRolesRepository(factory),
            new CreditRoleBlocksRepository(factory),
            new CreditBlockEntriesRepository(factory),
            lookup,
            staffLinkResolver);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating episodes");

        int total = 0;
        foreach (var s in _ctx.Series)
        {
            // v1.3.0：子作品（parent_series_id != NULL の映画系、SPIN-OFF を除く）は単独詳細ページを
            // 持たないため、配下のエピソードページも生成しない（仕様上 credit_attach_to=SERIES なので
            // エピソード自体を持たないはずだが念のためスキップ）。
            if (IsChildOfMovie(s)) continue;
            if (!_ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            foreach (var e in eps)
            {
                await GenerateOneAsync(s, e, ct).ConfigureAwait(false);
                total++;
            }
        }
        _ctx.Logger.Success($"episodes: {total} ページ");
    }

    /// <summary>
    /// 子作品判定：親シリーズが存在し、かつ自分が SPIN-OFF ではない場合は子作品扱い。
    /// SeriesGenerator.IsChildOfMovie と同じロジック。子作品（秋映画併映短編・子映画など）は
    /// 単独詳細ページを生成しないため、配下のエピソードページも生成しない。
    /// </summary>
    private static bool IsChildOfMovie(Series s)
    {
        if (!s.ParentSeriesId.HasValue) return false;
        if (string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal)) return false;
        return true;
    }

    private async Task GenerateOneAsync(Series series, Episode ep, CancellationToken ct)
    {
        // 同シリーズ内の前後話を引き当てる（一覧から自分のインデックスを探す）。
        var siblings = _ctx.EpisodesBySeries[series.SeriesId];
        var idx = -1;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (siblings[i].EpisodeId == ep.EpisodeId) { idx = i; break; }
        }
        Episode? prev = (idx > 0) ? siblings[idx - 1] : null;
        Episode? next = (idx >= 0 && idx + 1 < siblings.Count) ? siblings[idx + 1] : null;

        // パート群とフォーマット表（本放送は ep.OnAirAt を起点に絶対時刻、配信は series.vod_intro を起点に累積秒）。
        var parts = await _partsRepo.GetByEpisodeAsync(ep.EpisodeId, ct).ConfigureAwait(false);
        var formatTable = FormatTableBuilder.Build(parts, ep.OnAirAt, series.VodIntro, _ctx);

        // パート尺偏差値（AVANT/PART_A/PART_B のみ。対象パートが無い場合は空リスト）。
        // 表内の値表記から接頭辞「○○プリキュア内」「歴代」を取り除き、ヘッダ側で
        // 「『○○プリキュア』 / 歴代プリキュア全体」を 2 段ヘッダで示す方針。
        var partLengthStats = await _partsRepo.GetPartLengthStatsAsync(ep.EpisodeId, ct).ConfigureAwait(false);
        var partLengthStatRows = partLengthStats.Select(s => new PartLengthStatRow
        {
            PartName = s.PartTypeNameJa,
            SeriesRank = s.SeriesRank,
            SeriesTotal = s.SeriesTotal,
            SeriesHensachi = s.SeriesHensachi.ToString("0.00"),
            GlobalRank = s.GlobalRank,
            GlobalTotal = s.GlobalTotal,
            GlobalHensachi = s.GlobalHensachi.ToString("0.00")
        }).ToList();

        // パート尺統計表のヘッダ用に、当該シリーズの正式タイトル（series.title）をテンプレに渡す。
        // v1.3.0 stage22 後段：略称（series.title_short）は生成・UI ともに一切使わない方針に変更し、
        // 旧来の `TitleShort ?? Title` フォールバックは廃止。プロパティ名は SeriesTitleShortQuoted の
        // ままだが（既存テンプレ参照との互換のため）、中身は常に正式タイトルの『〜』囲み文字列となる。
        string seriesTitleShortQuoted = $"『{series.Title}』";

        // 文字情報 HTML を作る（既存 BuildTitleInformationPerCharAsync の移植）。
        // title_char_stats が NULL のエピソードは、文字統計テーブルが組まれていないので
        // 復活分析もスキップされる。HTML としては空で何も出さない。
        string titleCharInfoHtml = "";
        if (!string.IsNullOrEmpty(ep.TitleCharStats))
        {
            titleCharInfoHtml = await _titleCharInfo.RenderAsync(ep, ct).ConfigureAwait(false);
        }
        else
        {
            _ctx.Logger.Warn(
                $"title_char_stats が未生成: episode_id={ep.EpisodeId} ({series.Slug} #{ep.SeriesEpNo})。" +
                "PrecureDataStars.TitleCharStatsJson で再計算してください。");
        }

        // 主題歌（OP / ED / 挿入歌）。
        // v1.3.0 で episode_theme_songs.seq 列が「エピソード内の劇中順」を表す汎用カラムに
        // 変わったため、ソートは (is_broadcast_only, seq) の単純昇順だけで劇中流れる順番
        // どおりに並ぶ。OP/ED が冒頭・末尾とは限らない作品でも、運用者が seq に任意の順を
        // 入れていれば自然に再現される。
        // 本放送限定行（is_broadcast_only=1）は通常行の後ろに並ぶ扱い。
        var themes = (await _themeRepo.GetByEpisodeAsync(ep.EpisodeId, ct).ConfigureAwait(false))
            .OrderBy(x => x.IsBroadcastOnly)
            .ThenBy(x => x.Seq)
            .ToList();
        var themeRows = await BuildThemeRowsAsync(themes, ct).ConfigureAwait(false);

        // クレジット階層（エピソードスコープのもののみ）。
        // 表示順は Catalog 側 CreditPreviewRenderer の KindOrder 関数に合わせて
        // OP=1, ED=2, それ以外=999 の固定順で並べる。
        static int KindOrder(string k) => k switch { "OP" => 1, "ED" => 2, _ => 999 };
        var credits = (await _creditsRepo.GetByEpisodeAsync(ep.EpisodeId, ct).ConfigureAwait(false))
            .Where(c => !c.IsDeleted)
            .OrderBy(c => KindOrder(c.CreditKind))
            .ThenBy(c => c.CreditKind, StringComparer.Ordinal)
            .ToList();

        // credit_kinds マスタから日本語名（"オープニングクレジット" / "エンディングクレジット" 等）を取り出す。
        // プレビュー側の RenderOneCreditFromDbAsync と同じ参照ルート。
        if (_creditKindLabelMap is null)
        {
            var allKinds = await _creditKindsRepo.GetAllAsync(ct).ConfigureAwait(false);
            _creditKindLabelMap = allKinds.ToDictionary(k => k.KindCode, k => k.NameJa, StringComparer.Ordinal);
        }

        var creditBlocks = new List<CreditBlockView>();
        foreach (var c in credits)
        {
            var html = await _creditRenderer.RenderAsync(c, _ctx.Logger, ct).ConfigureAwait(false);
            creditBlocks.Add(new CreditBlockView
            {
                CreditKindLabel = _creditKindLabelMap.TryGetValue(c.CreditKind, out var nm) ? nm : c.CreditKind,
                Html = html
            });
        }

        // スタッフ情報（クレジット階層から脚本／絵コンテ／演出／作画監督／美術監督を抽出）。
        // クレジットセクションとは別に「主要スタッフ」セクションとして上部基本情報の近くに出す。
        var staffRows = await BuildStaffRowsAsync(credits, ct).ConfigureAwait(false);

        // 使用音声（episode_uses）セクションをパート別に構築。
        var episodeUseSections = await BuildEpisodeUsesViewAsync(ep.EpisodeId, ct).ConfigureAwait(false);

        // 通算情報を 1 行にまとめる（基本情報を整理して行数を抑える）。
        // v1.3.0 続編 でラベル名を「ぱっと見でなにを数えているか」が分かる長めの表現に整える。
        // ・シリーズ内話数         = 当該シリーズ内で何話目か（基本軸）
        // ・全プリキュアTV通算話数  = 「ふたりはプリキュア」第 1 話を起点とする TV シリーズ全体での通算話数
        // ・全プリキュアTV通算放送回数 = 同じく TV シリーズ全体での通算「放送回」（休止・特番含めた通し番号）
        // ・全ニチアサ通算放送回数  = 朝日放送 日曜朝のアニメ枠（ニチアサ）全体での通算放送回数
        // 通算情報そのものは TotalsItem の連なりとしてテンプレに渡し、テンプレ側で枠線なしの簡潔な表組として描画する。
        var totalsItems = new List<TotalsItem>
        {
            new TotalsItem { Label = "シリーズ内話数", Value = $"第{ep.SeriesEpNo}話" }
        };
        if (ep.TotalEpNo is int tep) totalsItems.Add(new TotalsItem { Label = "全プリキュアTV通算話数", Value = $"第{tep}話" });
        if (ep.TotalOaNo is int toa) totalsItems.Add(new TotalsItem { Label = "全プリキュアTV通算放送回数", Value = $"第{toa}回" });
        if (ep.NitiasaOaNo is int nio) totalsItems.Add(new TotalsItem { Label = "全ニチアサ通算放送回数", Value = $"第{nio}回" });

        // 「いま現在の参照点」キャプション。
        // 毎週変動するセクション（サブタイトル文字情報・パート尺統計情報）の説明文末尾に
        // 「（yyyy年m月d日現在、『○○プリキュア』第N話時点）」を付ける。
        // BuildContext.LatestAiredTvEpisode が null（TV 放送が 1 件も無い DB）なら空文字。
        string buildPointCaption = BuildLatestAiredCaption(_ctx.LatestAiredTvEpisode);

        // ページネーション端ボタン用ラベル：上下ページネーションの「« 前話」「次話 »」を
        // 「« #N サブタイトル」「#N サブタイトル »」に置き換えるため。
        string prevPagerLabel = prev is not null ? $"#{prev.SeriesEpNo} {prev.TitleText}" : "";
        string nextPagerLabel = next is not null ? $"#{next.SeriesEpNo} {next.TitleText}" : "";

        // テンプレートに渡すモデル。
        var content = new EpisodeContentModel
        {
            Series = new SeriesRefView
            {
                Slug = series.Slug,
                Title = series.Title,
                // v1.3.0 stage22 後段：略称（title_short）は使わない方針のため常に正式タイトルを詰める。
                // プロパティ名 TitleShort は既存テンプレ参照との互換のため温存。
                TitleShort = series.Title
            },
            Episode = new EpisodeView
            {
                SeriesEpNo = ep.SeriesEpNo,
                TotalEpNo = ep.TotalEpNo?.ToString() ?? "",
                TotalOaNo = ep.TotalOaNo?.ToString() ?? "",
                NitiasaOaNo = ep.NitiasaOaNo?.ToString() ?? "",
                TitleText = ep.TitleText,
                TitleRichHtml = ep.TitleRichHtml ?? "",  // ルビ付き HTML はそのまま流す
                TitleKana = ep.TitleKana ?? "",
                // 放送日時は「2004年2月1日 8:30〜9:00」フォーマット。
                // duration_minutes が NULL（尺未登録）のエピソードは「2004年2月1日 8:30」で出す。
                OnAirDateTime = FormatJpDateTimeWithDuration(ep.OnAirAt, ep.DurationMinutes),
                ToeiAnimSummaryUrl = ep.ToeiAnimSummaryUrl ?? "",
                ToeiAnimLineupUrl = ep.ToeiAnimLineupUrl ?? "",
                YoutubeTrailerUrl = ep.YoutubeTrailerUrl ?? "",
                YoutubeId = ExtractYoutubeId(ep.YoutubeTrailerUrl),
                Notes = ep.Notes ?? ""
            },
            FormatTable = formatTable,
            TitleCharInfoHtml = titleCharInfoHtml,
            PartLengthStats = partLengthStatRows,
            SeriesTitleShortQuoted = seriesTitleShortQuoted,
            ThemeSongs = themeRows,
            CreditBlocks = creditBlocks,
            Staff = staffRows,
            EpisodeUseSections = episodeUseSections,
            Totals = totalsItems,
            BuildPointCaption = buildPointCaption,
            CoverageLabel = _ctx.CreditCoverageLabel,
            PrevUrl = prev != null ? PathUtil.EpisodeUrl(series.Slug, prev.SeriesEpNo) : "",
            PrevLabel = prev != null ? $"第{prev.SeriesEpNo}話 {prev.TitleText}" : "",
            NextUrl = next != null ? PathUtil.EpisodeUrl(series.Slug, next.SeriesEpNo) : "",
            NextLabel = next != null ? $"第{next.SeriesEpNo}話 {next.TitleText}" : "",
            PrevPagerLabel = prevPagerLabel,
            NextPagerLabel = nextPagerLabel,
            // 同シリーズ全話分の話数ページネーションを「圧縮表示」用に整形しておく
            // （現在話の前後 ±2 件 + 先頭・末尾 + 省略記号「…」、典型的な Web ページネーション風）。
            Pagination = BuildPagination(siblings, ep, series.Slug)
        };

        // MetaDescription を実データから動的に組み立てる（v1.3.1 改修）。
        // 単純な定型文「N話のフォーマット表・スタッフ・主題歌情報」だと全エピソードで重複コンテンツ化し、
        // SERP の CTR にも反映されにくいため、放送日・主要スタッフ 2 役職・OP/ED の楽曲名まで含めて
        // 個別性の高い 140 字目安の説明文を作る。
        var metaDescription = BuildMetaDescription(series, ep, staffRows, themeRows);

        // エピソード詳細の構造化データは Schema.org の TVEpisode 型。
        // 親シリーズ partOfSeries、エピソード番号、放送日、エピソード名を主要プロパティとして埋め込む。
        // v1.3.1：description / director / creator を追加して、検索結果の rich snippet と
        // ナレッジパネル生成のヒントを増やす。
        string baseUrl = _ctx.Config.BaseUrl;
        string episodeUrl = PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "TVEpisode",
            ["name"] = ep.TitleText,
            ["episodeNumber"] = ep.SeriesEpNo,
            ["datePublished"] = ep.OnAirAt.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja",
            // 構造化データの description は MetaDescription と同じ文面を流用。これにより検索エンジンが
            // 同じエピソードに関する記述として OGP と JSON-LD を整合的に解釈できる。
            ["description"] = metaDescription
        };
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + episodeUrl;
        // 親シリーズ参照を partOfSeries に埋め込み（TVEpisode → TVSeries の入れ子）。
        var partOfSeries = new Dictionary<string, object?>
        {
            ["@type"] = "TVSeries",
            ["name"] = series.Title
        };
        if (!string.IsNullOrEmpty(baseUrl)) partOfSeries["url"] = baseUrl + PathUtil.SeriesUrl(series.Slug);
        jsonLdDict["partOfSeries"] = partOfSeries;

        // 演出役職の人物（"演出" 単独行または「絵コンテ・演出」統合行から取り出す）を director プロパティに、
        // 脚本役職の人物を creator プロパティに、それぞれ Person 型の配列として埋め込む（v1.3.1 追加）。
        // 単独役職に複数人いる場合は配列、1 人だけでも配列のまま渡す（Schema.org 仕様上いずれも有効）。
        // 人物が居ない場合は当該プロパティを出力しない。
        var directorPersons = ExtractDirectorPersons(staffRows);
        if (directorPersons.Count > 0)
        {
            jsonLdDict["director"] = directorPersons
                .Select(name => new Dictionary<string, object?>
                {
                    ["@type"] = "Person",
                    ["name"] = name
                })
                .ToArray();
        }
        var screenplayPersons = ExtractScreenplayPersons(staffRows);
        if (screenplayPersons.Count > 0)
        {
            // 脚本は creator ロールでラップ（"脚本" という日本語の役職名を roleName に明示）。
            // Role + creator(Person) の入れ子は Google 公式ドキュメントが TVEpisode の典型例として
            // 示している構造で、SERP 上で「脚本: 〇〇」のラベル付き表示候補となる。
            jsonLdDict["creator"] = screenplayPersons
                .Select(name => new Dictionary<string, object?>
                {
                    ["@type"] = "Role",
                    ["roleName"] = "脚本",
                    ["creator"] = new Dictionary<string, object?>
                    {
                        ["@type"] = "Person",
                        ["name"] = name
                    }
                })
                .ToArray();
        }

        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = $"{series.Title} 第{ep.SeriesEpNo}話「{ep.TitleText}」",
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "シリーズ一覧", Url = "/series/" },
                new BreadcrumbItem { Label = series.Title, Url = PathUtil.SeriesUrl(series.Slug) },
                new BreadcrumbItem { Label = $"第{ep.SeriesEpNo}話", Url = "" }
            },
            OgType = "video.episode",
            JsonLd = jsonLd
        };

        _page.RenderAndWrite(
            episodeUrl,
            "episodes",
            "episode-detail.sbn",
            content,
            layout);
    }

    /// <summary>
    /// 「いま現在」キャプションを組み立てる。例: 「2026年5月3日現在、『キミとアイドルプリキュア♪』第14話時点」。
    /// 直近放送 TV エピソードが存在しない場合は空文字を返す（テンプレ側で表示自体を抑止する）。
    /// v1.3.0 続編：シリーズ名は正式名称（<see cref="Series.Title"/>）を使う。
    /// 旧仕様の TitleShort フォールバックは「『プリキュア』第N話時点」のような曖昧な表記を生むため廃止。
    /// </summary>
    private static string BuildLatestAiredCaption((Series Series, Episode Episode)? latest)
    {
        if (latest is not { } la) return "";
        var d = la.Episode.OnAirAt;
        string seriesLabel = la.Series.Title;
        return $"{d.Year}年{d.Month}月{d.Day}日現在、『{seriesLabel}』第{la.Episode.SeriesEpNo}話時点";
    }

    /// <summary>
    /// 主題歌行を表示用 DTO に変換する（縦リスト 1 行表現）。
    /// テンプレ側で「OP「タイトル」　うた：歌唱者」のように 1 行ずつ並べる前提。
    /// 楽曲タイトルは詳細ページへのリンクを張れるよう、SongLink プロパティで URL を渡す。
    /// </summary>
    // ── 主題歌・挿入歌セクション専用：構造化クレジット表示でマスタを参照するためのキャッシュ
    //    （v1.3.0 公開直前のデザイン整理 第 N+2 弾で追加）。
    //    全エピソードのループの最初に 1 度だけロードして、以後はメモリ参照で済ませる。
    private IReadOnlyDictionary<string, Role>? _themeRolesMap;
    private IReadOnlyDictionary<int, PersonAlias>? _themePersonAliasMap;
    private IReadOnlyDictionary<int, CharacterAlias>? _themeCharacterAliasMap;

    private async Task EnsureThemeMastersLoadedAsync(CancellationToken ct)
    {
        if (_themeRolesMap is null)
        {
            var roles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _themeRolesMap = roles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        }
        if (_themePersonAliasMap is null)
        {
            var personAliases = await _personAliasesRepoForSongs.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
            _themePersonAliasMap = personAliases.ToDictionary(a => a.AliasId);
        }
        if (_themeCharacterAliasMap is null)
        {
            var characterAliases = await _characterAliasesRepoForSongs.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
            _themeCharacterAliasMap = characterAliases.ToDictionary(a => a.AliasId);
        }
    }

    private async Task<IReadOnlyList<ThemeSongRow>> BuildThemeRowsAsync(
        IReadOnlyList<EpisodeThemeSong> themes,
        CancellationToken ct)
    {
        // 録音 → 曲を引いて表示文字列を組む。同じ録音 ID が複数行で参照されることもあるので
        // 一回 cache に入れておく（OP/ED/INSERT の 3 行同時取得を想定）。
        var recCache = new Dictionary<int, SongRecording?>();
        var songCache = new Dictionary<int, Song?>();
        // 構造化クレジット系もエピソード内で同一 song/recording が複数行参照されることがあるので、
        // 1 エピソード分を 1 度だけ読んでローカルキャッシュする。
        var songCreditsCache = new Dictionary<int, IReadOnlyList<SongCredit>>();
        var singersCache = new Dictionary<int, IReadOnlyList<SongRecordingSinger>>();

        // 役職マスタ・名義マスタはエピソード横断で共有する（インスタンス変数キャッシュ）。
        await EnsureThemeMastersLoadedAsync(ct).ConfigureAwait(false);
        var roleMap = _themeRolesMap!;
        var personAliasMap = _themePersonAliasMap!;
        var characterAliasMap = _themeCharacterAliasMap!;

        async Task<(SongRecording? rec, Song? song)> ResolveAsync(int srId)
        {
            if (!recCache.TryGetValue(srId, out var rec))
            {
                rec = await _songRecRepo.GetByIdAsync(srId, ct).ConfigureAwait(false);
                recCache[srId] = rec;
            }
            Song? song = null;
            if (rec is not null)
            {
                if (!songCache.TryGetValue(rec.SongId, out song))
                {
                    song = await _songsRepo.GetByIdAsync(rec.SongId, ct).ConfigureAwait(false);
                    songCache[rec.SongId] = song;
                }
            }
            return (rec, song);
        }

        async Task<IReadOnlyList<SongCredit>> GetSongCreditsAsync(int songId)
        {
            if (songCreditsCache.TryGetValue(songId, out var cached)) return cached;
            var loaded = await _songCreditsRepo.GetBySongAsync(songId, ct).ConfigureAwait(false);
            songCreditsCache[songId] = loaded;
            return loaded;
        }

        async Task<IReadOnlyList<SongRecordingSinger>> GetSingersAsync(int songRecordingId)
        {
            if (singersCache.TryGetValue(songRecordingId, out var cached)) return cached;
            var loaded = await _songRecSingersRepo.GetByRecordingAsync(songRecordingId, ct).ConfigureAwait(false);
            singersCache[songRecordingId] = loaded;
            return loaded;
        }

        var rows = new List<ThemeSongRow>(themes.Count);
        // v1.3.0：seq 列が劇中順を表すため、(IsBroadcastOnly, Seq) の単純昇順だけで
        // 劇中で流れる順番に並ぶ。OP/ED/INSERT を区別する独自ソートはもはや不要。
        // 本放送限定行は通常行の後ろに並ぶ扱い。
        // v1.3.0 ブラッシュアップ続編：usage_actuality='CREDITED_NOT_BROADCAST' は
        // 「クレジットされているが実際には流れていない」ので、エピソード主題歌セクションには
        // 表示しない（クレジット側だけが事実として残る）。
        // 'BROADCAST_NOT_CREDITED' は逆に「クレジットなしで流れた」なので
        // エピソード側には表示する（クレジット側は CreditInvolvementIndex 巡回で除外済み）。
        foreach (var t in themes
            .Where(x => !string.Equals(x.UsageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
            .OrderBy(x => x.IsBroadcastOnly)
            .ThenBy(x => x.Seq))
        {
            var (rec, song) = await ResolveAsync(t.SongRecordingId).ConfigureAwait(false);
            // 種別ラベル：劇中順を seq に持たせた以上、INSERT 内通番（旧 insert_seq）は
            // もはや「N 番目の挿入歌」を意味しない。区分は OP / ED / 挿入歌 の 3 表記に統一。
            string kindLabel = t.ThemeKind switch
            {
                "OP" => "OP",
                "ED" => "ED",
                "INSERT" => "挿入歌",
                _ => t.ThemeKind
            };

            // 楽曲詳細ページへのリンク URL を組み立てる（song_id が引けたときだけ）。
            int? songId = song?.SongId;
            string songLink = songId.HasValue ? PathUtil.SongUrl(songId.Value) : "";

            // v1.3.0 公開直前のデザイン整理 第 N+2 弾：構造化クレジット由来の役職別 HTML を組む。
            // song_credits（作詞・作曲・編曲）と song_recording_singers（歌唱者）の両方を見て、
            // 構造化が無ければ Song.LyricistName / Song.ComposerName / Song.ArrangerName /
            // SongRecording.SingerName のフリーテキストにフォールバックする。
            string lyricsHtml = "";
            string lyricsRoleLabelHtml = "";
            string compositionHtml = "";
            string compositionRoleLabelHtml = "";
            string arrangementHtml = "";
            string arrangementRoleLabelHtml = "";
            if (song is not null)
            {
                var credits = await GetSongCreditsAsync(song.SongId).ConfigureAwait(false);
                lyricsHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Lyrics, song.LyricistName, personAliasMap);
                compositionHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Composition, song.ComposerName, personAliasMap);
                arrangementHtml = BuildCreditRoleHtml(credits, SongCreditRoles.Arrangement, song.ArrangerName, personAliasMap);
                // 役職ラベルは roles マスタから引いてリンク化。未登録時はフォールバック固定文字列。
                lyricsRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongCreditRoles.Lyrics, roleMap, "作詞");
                compositionRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongCreditRoles.Composition, roleMap, "作曲");
                arrangementRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongCreditRoles.Arrangement, roleMap, "編曲");
            }

            string vocalistsHtml = "";
            // v1.3.1 stage B-5：「歌」役職ラベルもリンク化。役職統計ページに飛ばすことで、
            // 他の作詞・作曲・編曲と同じ扱いに揃える。テンプレ側ではハードコード文字列「歌」の代わりに
            // 本フィールドを描画する。rec が null（録音情報なし）でもラベル自体は出すケースは無いので
            // ここでは rec != null のときだけ解決する。
            string vocalistsRoleLabelHtml = "";
            if (rec is not null)
            {
                var singers = await GetSingersAsync(rec.SongRecordingId).ConfigureAwait(false);
                vocalistsHtml = BuildVocalistsHtml(singers, rec.SingerName, personAliasMap, characterAliasMap);
                vocalistsRoleLabelHtml = BuildSongRoleLabelLinkHtml(SongRecordingSingerRoles.Vocals, roleMap, "歌");
            }

            rows.Add(new ThemeSongRow
            {
                KindLabel = kindLabel,
                Title = song?.Title ?? "(曲名未登録)",
                SongLink = songLink,
                VariantLabel = rec?.VariantLabel ?? "",
                SingerName = rec?.SingerName ?? "",
                LyricsHtml = lyricsHtml,
                LyricsRoleLabelHtml = lyricsRoleLabelHtml,
                CompositionHtml = compositionHtml,
                CompositionRoleLabelHtml = compositionRoleLabelHtml,
                ArrangementHtml = arrangementHtml,
                ArrangementRoleLabelHtml = arrangementRoleLabelHtml,
                VocalistsHtml = vocalistsHtml,
                VocalistsRoleLabelHtml = vocalistsRoleLabelHtml,
                Notes = t.Notes ?? "",
                IsBroadcastOnly = t.IsBroadcastOnly
            });
        }
        return rows;
    }

    /// <summary>
    /// 構造化 song_credits 行を「PrecedingSeparator + 名義リンク」の連結 HTML に整形する。
    /// 行が無く <paramref name="fallbackText"/> が非空ならフリーテキストの HTML エスケープ平文を返す。
    /// v1.3.0 公開直前のデザイン整理 第 N+2 弾：SongsGenerator の同名ヘルパと同等のロジック。
    /// </summary>
    private string BuildCreditRoleHtml(
        IReadOnlyList<SongCredit> rows,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var roleRows = rows
            .Where(r => string.Equals(r.CreditRole, roleCode, StringComparison.Ordinal))
            .OrderBy(r => r.CreditSeq)
            .ToList();
        if (roleRows.Count == 0)
        {
            return string.IsNullOrEmpty(fallbackText) ? "" : HtmlEscape(fallbackText);
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < roleRows.Count; i++)
        {
            var row = roleRows[i];
            if (i > 0) sb.Append(HtmlEscape(row.PrecedingSeparator ?? ""));
            if (personAliasMap.TryGetValue(row.PersonAliasId, out var alias))
            {
                sb.Append(_staffLinkResolver.ResolveAsHtml(row.PersonAliasId, alias.GetDisplayName()));
            }
            else
            {
                sb.Append("[alias#").Append(row.PersonAliasId).Append("]");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 役職ラベルを <c>/stats/roles/{rep_role_code}/</c> リンク付き HTML に整形する。
    /// v1.3.0 公開直前のデザイン整理 第 N+2 弾：SongsGenerator の同名ヘルパと同等のロジック。
    /// </summary>
    private string BuildSongRoleLabelLinkHtml(string roleCode, IReadOnlyDictionary<string, Role> roleMap, string fallbackLabel)
    {
        if (roleMap.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.NameJa))
        {
            string rep = _roleSuccessorResolver.GetRepresentative(roleCode);
            string href = PathUtil.RoleStatsUrl(string.IsNullOrEmpty(rep) ? roleCode : rep);
            return $"<a href=\"{HtmlEscape(href)}\">{HtmlEscape(role.NameJa)}</a>";
        }
        return HtmlEscape(fallbackLabel);
    }

    /// <summary>
    /// 録音の歌唱者群を「キャラ(CV:声優) / 個人名義」のリンク付き HTML に整形する。
    /// v1.3.0 公開直前のデザイン整理 第 N+2 弾：SongsGenerator の同名ヘルパと同等のロジック。
    /// </summary>
    private string BuildVocalistsHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string? fallbackSingerName,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        var vocalsRows = singers
            .Where(s => string.Equals(s.RoleCode, SongRecordingSingerRoles.Vocals, StringComparison.Ordinal))
            .OrderBy(s => s.SingerSeq)
            .ToList();
        if (vocalsRows.Count == 0)
        {
            return string.IsNullOrEmpty(fallbackSingerName) ? "" : HtmlEscape(fallbackSingerName);
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < vocalsRows.Count; i++)
        {
            var s = vocalsRows[i];
            if (i > 0) sb.Append(HtmlEscape(s.PrecedingSeparator ?? ""));
            sb.Append(RenderSingerEntry(s, personAliasMap, characterAliasMap));
            if (!string.IsNullOrEmpty(s.AffiliationText))
            {
                sb.Append(' ').Append(HtmlEscape(s.AffiliationText));
            }
        }
        return sb.ToString();
    }

    private string RenderSingerEntry(
        SongRecordingSinger s,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (s.BillingKind == SingerBillingKind.Person)
        {
            string main = ResolvePersonAliasLink(s.PersonAliasId, personAliasMap);
            if (s.SlashPersonAliasId.HasValue)
            {
                string slash = ResolvePersonAliasLink(s.SlashPersonAliasId, personAliasMap);
                return $"{main} / {slash}";
            }
            return main;
        }
        else
        {
            string mainChar = ResolveCharacterAliasLink(s.CharacterAliasId, characterAliasMap);
            string charPart = mainChar;
            if (s.SlashCharacterAliasId.HasValue)
            {
                string slashChar = ResolveCharacterAliasLink(s.SlashCharacterAliasId, characterAliasMap);
                charPart = $"{mainChar}/{slashChar}";
            }
            string cv = ResolvePersonAliasLink(s.VoicePersonAliasId, personAliasMap);
            return $"{charPart}(CV:{cv})";
        }
    }

    private string ResolvePersonAliasLink(int? aliasId, IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        if (!aliasId.HasValue) return "";
        if (!personAliasMap.TryGetValue(aliasId.Value, out var alias))
            return $"[alias#{aliasId.Value}]";
        return _staffLinkResolver.ResolveAsHtml(aliasId, alias.GetDisplayName());
    }

    private static string ResolveCharacterAliasLink(int? aliasId, IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (!aliasId.HasValue) return "";
        if (!characterAliasMap.TryGetValue(aliasId.Value, out var alias))
            return $"[char-alias#{aliasId.Value}]";
        // CharacterAlias は PersonAlias と違い DisplayTextOverride / GetDisplayName() を持たないため、
        // 表示テキストは常に Name そのもの。
        return $"<a href=\"/characters/{alias.CharacterId}/\">{HtmlEscape(alias.Name)}</a>";
    }

    /// <summary>HTML 5 における &amp;・&lt;・&gt;・&quot;・&#39; の最小限のエスケープ。</summary>
    private static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");

    /// <summary>
    /// 当該エピソードの <c>episode_uses</c> 行群をパート別にグルーピングして表示用 DTO に変換する。
    /// パート種別は <c>part_types.display_order</c> 昇順で並べ、同パート内では (use_order, sub_order) 昇順。
    /// </summary>
    private async Task<IReadOnlyList<EpisodeUseSection>> BuildEpisodeUsesViewAsync(int episodeId, CancellationToken ct)
    {
        var uses = await _episodeUsesRepo.GetByEpisodeAsync(episodeId, ct).ConfigureAwait(false);
        if (uses.Count == 0) return Array.Empty<EpisodeUseSection>();

        var trackKindMap = (await _trackContentKindsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(k => k.KindCode, StringComparer.Ordinal);
        var sizeVariantMap = (await _songSizeVariantsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        var partVariantMap = (await _songPartVariantsRepo.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        var partTypes = (await _partTypesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        // PartType モデルのコード値プロパティは PartTypeCode（DB 列は part_type）。
        var partTypeMap = partTypes.ToDictionary(p => p.PartTypeCode, StringComparer.Ordinal);

        // 楽曲・劇伴の参照を一括解決（同じ song_recording / bgm_cue が複数箇所で使われる前提で重複ロード回避）。
        var songRecIds = uses.Where(u => u.SongRecordingId.HasValue).Select(u => u.SongRecordingId!.Value).Distinct().ToList();
        var songRecCache = new Dictionary<int, SongRecording>();
        var songCache = new Dictionary<int, Song>();
        foreach (var rid in songRecIds)
        {
            var rec = await _songRecRepo.GetByIdAsync(rid, ct).ConfigureAwait(false);
            if (rec is null) continue;
            songRecCache[rid] = rec;
            if (!songCache.ContainsKey(rec.SongId))
            {
                var song = await _songsRepo.GetByIdAsync(rec.SongId, ct).ConfigureAwait(false);
                if (song is not null) songCache[rec.SongId] = song;
            }
        }

        // BGM cue 参照は (series_id, m_no_detail) で複合。シリーズ単位でロードして辞書化。
        var bgmSeriesIds = uses
            .Where(u => u.BgmSeriesId.HasValue && !string.IsNullOrEmpty(u.BgmMNoDetail))
            .Select(u => u.BgmSeriesId!.Value)
            .Distinct()
            .ToList();
        var bgmCueMap = new Dictionary<(int seriesId, string mNoDetail), BgmCue>();
        foreach (var sid in bgmSeriesIds)
        {
            var cues = await _bgmCuesRepo.GetBySeriesAsync(sid, ct).ConfigureAwait(false);
            foreach (var cue in cues)
                bgmCueMap[(cue.SeriesId, cue.MNoDetail)] = cue;
        }

        // パート種別ごとにグルーピングして DTO 化。
        var sections = uses
            .GroupBy(u => u.PartKind)
            .Select(g =>
            {
                string label = partTypeMap.TryGetValue(g.Key, out var pt) ? pt.NameJa : g.Key;
                int order = partTypeMap.TryGetValue(g.Key, out var pt2) ? (pt2.DisplayOrder ?? byte.MaxValue) : byte.MaxValue;

                var rows = g.OrderBy(u => u.UseOrder)
                            .ThenBy(u => u.SubOrder)
                            .Select(u => BuildEpisodeUseRow(u, trackKindMap, sizeVariantMap, partVariantMap, songRecCache, songCache, bgmCueMap))
                            .ToList();

                return new { Order = order, Section = new EpisodeUseSection { PartLabel = label, Uses = rows } };
            })
            .OrderBy(x => x.Order)
            .Select(x => x.Section)
            .ToList();

        return sections;
    }

    /// <summary>
    /// 1 つの <see cref="EpisodeUse"/> 行を表示用 <see cref="EpisodeUseRow"/> に変換する。
    /// </summary>
    private static EpisodeUseRow BuildEpisodeUseRow(
        EpisodeUse u,
        IReadOnlyDictionary<string, TrackContentKind> trackKindMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<int, SongRecording> songRecCache,
        IReadOnlyDictionary<int, Song> songCache,
        IReadOnlyDictionary<(int seriesId, string mNoDetail), BgmCue> bgmCueMap)
    {
        string contentKindLabel = trackKindMap.TryGetValue(u.ContentKindCode, out var ck) ? ck.NameJa : u.ContentKindCode;
        string title = "";
        string subTitle = "";
        string songLink = "";

        switch (u.ContentKindCode)
        {
            case "SONG":
                if (u.SongRecordingId is int rid && songRecCache.TryGetValue(rid, out var rec)
                    && songCache.TryGetValue(rec.SongId, out var song))
                {
                    // タイトル：use_title_override があればそちら優先（特殊表記用）、なければ歌のタイトル。
                    title = !string.IsNullOrEmpty(u.UseTitleOverride) ? u.UseTitleOverride! : song.Title;
                    songLink = PathUtil.SongUrl(song.SongId);
                    var subParts = new List<string>();
                    if (!string.IsNullOrEmpty(rec.SingerName)) subParts.Add(rec.SingerName!);
                    if (!string.IsNullOrEmpty(u.SongSizeVariantCode)
                        && sizeVariantMap.TryGetValue(u.SongSizeVariantCode!, out var sv))
                        subParts.Add(sv.NameJa);
                    if (!string.IsNullOrEmpty(u.SongPartVariantCode)
                        && partVariantMap.TryGetValue(u.SongPartVariantCode!, out var pv))
                        subParts.Add(pv.NameJa);
                    if (!string.IsNullOrEmpty(rec.VariantLabel)) subParts.Add(rec.VariantLabel!);
                    subTitle = string.Join(" / ", subParts);
                }
                else
                {
                    title = u.UseTitleOverride ?? "(歌情報未登録)";
                }
                break;

            case "BGM":
                if (u.BgmSeriesId is int bsid && u.BgmMNoDetail is string mnd
                    && bgmCueMap.TryGetValue((bsid, mnd), out var cue))
                {
                    string mNoLabel = cue.IsTempMNo ? "(番号不明)" : cue.MNoDetail;
                    title = !string.IsNullOrEmpty(u.UseTitleOverride)
                        ? u.UseTitleOverride!
                        : (cue.MenuTitle ?? "(タイトル未登録)");
                    var subParts = new List<string> { mNoLabel };
                    if (!string.IsNullOrEmpty(cue.ComposerName)) subParts.Add($"作曲: {cue.ComposerName}");
                    subTitle = string.Join(" / ", subParts);
                }
                else
                {
                    title = u.UseTitleOverride ?? "(劇伴情報未登録)";
                }
                break;

            default:
                // DRAMA / RADIO / JINGLE / OTHER 等。
                title = u.UseTitleOverride ?? "";
                break;
        }

        return new EpisodeUseRow
        {
            UseOrder = u.UseOrder,
            SubOrder = u.SubOrder,
            ContentKindLabel = contentKindLabel,
            Title = title,
            SubTitle = subTitle,
            SceneLabel = u.SceneLabel ?? "",
            DurationLabel = FormatDurationSeconds(u.DurationSeconds),
            SongLink = songLink,
            IsBroadcastOnly = u.IsBroadcastOnly
        };
    }

    /// <summary>使用尺の秒数を「m:ss」表記に整形。NULL は空文字。</summary>
    private static string FormatDurationSeconds(ushort? seconds)
    {
        if (!seconds.HasValue) return "";
        ushort s = seconds.Value;
        int min = s / 60;
        int sec = s % 60;
        return $"{min}:{sec:00}";
    }

    /// <summary>
    /// エピソード詳細ページの <c>&lt;meta name="description"&gt;</c> 用の説明文を、実データから組み立てる
    /// （v1.3.1 追加）。
    /// <para>
    /// 構成は下記の優先度で「シリーズ名・話数・サブタイトル・放送日 → 主要スタッフ 2 行 →
    /// 主題歌 (OP / ED) 2 曲」の順。<c>targetMaxChars</c>（140 字）を超えそうな段で打ち切り、
    /// 短く済むエピソードは尻切れにならずに自然に終わる設計とする。説明文は OG / Twitter Card にも
    /// 流用されるため、検索結果と SNS 共有プレビューの両方で読みやすい長さに収める。
    /// </para>
    /// <para>
    /// スタッフ抽出は <see cref="BuildStaffRowsAsync"/> の結果をそのまま再利用する（重複クエリを避けるため）。
    /// 主題歌行は OP / ED のみ採用し、挿入歌は字数節約のため description には含めない。
    /// </para>
    /// </summary>
    private static string BuildMetaDescription(
        Series series,
        Episode ep,
        IReadOnlyList<StaffRow> staffRows,
        IReadOnlyList<ThemeSongRow> themeRows)
    {
        // meta description / og:description / twitter:description は概ね 120〜160 字程度で
        // 切り詰められるため、保守的に 140 字を目標値に置く（厳密上限ではなく、超えそうな段で
        // 追加を打ち切るためのガード値）。日本語 1 文字 = 1 char カウントで運用。
        const int targetMaxChars = 140;

        var sb = new System.Text.StringBuilder();

        // ① 基本（「シリーズ」第N話「サブタイトル」(YYYY年M月D日放送)。）
        // シリーズタイトルは『』囲み、サブタイトルは「」囲みの慣例で書き分けるとくどいため、
        // ここでは描画の簡潔さを優先して両方とも「」で揃える。
        sb.Append('「').Append(series.Title).Append("」第").Append(ep.SeriesEpNo)
          .Append("話「").Append(ep.TitleText).Append("」(")
          .Append(ep.OnAirAt.ToString("yyyy年M月d日"))
          .Append("放送)。");

        // ② 主要スタッフ（最大 2 役職、優先順は staffRows の並び＝脚本→絵コンテ・演出系→…）。
        int staffAppended = 0;
        foreach (var staff in staffRows)
        {
            if (staffAppended >= 2) break;
            if (string.IsNullOrWhiteSpace(staff.NamesLine)) continue;
            // ラベルと人名を 1 セットで足す（足したら「、」で区切る方針、最終的に末尾「、」をトリム）。
            // staff.NamesLine は <a href="..."> タグでラップされた HTML 断片を含むため、
            // meta description 値として埋め込む前に HTML タグを除去してプレーンテキスト化する
            // （v1.3.1 stage5 修正：HTML 含みのまま属性値に入ると " で属性中断する事故が発生していた）。
            var plainNames = StripHtmlTags(staff.NamesLine);
            var fragment = $"{staff.RoleLabel}:{plainNames}";
            // 次の「、」も含めて目標超過なら採用しない（直前項目で打ち切り）。
            if (sb.Length + fragment.Length + 1 > targetMaxChars) break;
            if (staffAppended > 0) sb.Append('、');
            sb.Append(fragment);
            staffAppended++;
        }
        if (staffAppended > 0) sb.Append('。');

        // ③ 主題歌（OP / ED の最大 2 曲。挿入歌は字数節約のため除外）。
        int themeAppended = 0;
        foreach (var theme in themeRows)
        {
            if (themeAppended >= 2) break;
            if (theme.KindLabel != "OP" && theme.KindLabel != "ED") continue;
            if (string.IsNullOrWhiteSpace(theme.Title)) continue;
            var fragment = $"{theme.KindLabel}「{theme.Title}」";
            if (sb.Length + fragment.Length + 1 > targetMaxChars) break;
            if (themeAppended > 0) sb.Append('、');
            sb.Append(fragment);
            themeAppended++;
        }
        if (themeAppended > 0) sb.Append('。');

        return sb.ToString();
    }

    /// <summary>
    /// スタッフ行群から「演出」役職の人物名一覧を取り出す（v1.3.1 追加）。
    /// <para>
    /// 単独の "演出" 行か、「絵コンテ・演出」統合行（両役職を兼ねる人物がリストされている）
    /// のどちらかを対象とする。マッチした最初の行から <see cref="StaffRow.NamesLine"/>（「、」連結）を
    /// 分割して返す。該当行が無ければ空リスト。
    /// </para>
    /// <para>
    /// 用途：TVEpisode JSON-LD の <c>director</c> プロパティ（Person 型配列）構築。
    /// </para>
    /// </summary>
    private static List<string> ExtractDirectorPersons(IReadOnlyList<StaffRow> staffRows)
    {
        foreach (var row in staffRows)
        {
            if (row.RoleLabel == "演出" || row.RoleLabel == "絵コンテ・演出")
            {
                return SplitNamesLine(row.NamesLine);
            }
        }
        return new List<string>();
    }

    /// <summary>
    /// スタッフ行群から「脚本」役職の人物名一覧を取り出す（v1.3.1 追加）。
    /// 単独の "脚本" 行のみを対象とする（「絵コンテ・演出」統合行に脚本は含まれない）。
    /// 用途：TVEpisode JSON-LD の <c>creator</c> プロパティ（Role + Person の入れ子）構築。
    /// </summary>
    private static List<string> ExtractScreenplayPersons(IReadOnlyList<StaffRow> staffRows)
    {
        foreach (var row in staffRows)
        {
            if (row.RoleLabel == "脚本")
            {
                return SplitNamesLine(row.NamesLine);
            }
        }
        return new List<string>();
    }

    /// <summary>
    /// <see cref="StaffRow.NamesLine"/> のように「、」連結された日本語人名文字列を、個別の人名リストに割る
    /// （v1.3.1 追加）。空白要素は除外、各人名の前後空白も除去する。
    /// <para>
    /// 注意：<see cref="StaffRow.NamesLine"/> は実体としては <c>&lt;a href="..."&gt;人名&lt;/a&gt;</c> でラップされた
    /// HTML 断片の連結である。本メソッドの結果は JSON-LD の Person.name 値として使われるため、
    /// 各エントリから HTML タグを除去してプレーンテキスト化する（v1.3.1 stage5 修正）。
    /// </para>
    /// </summary>
    private static List<string> SplitNamesLine(string namesLine)
    {
        if (string.IsNullOrEmpty(namesLine)) return new List<string>();
        return namesLine.Split('、', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => StripHtmlTags(n).Trim())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    /// <summary>
    /// 入力文字列から HTML タグ（<c>&lt;...&gt;</c>）を素朴に取り除いてプレーンテキスト化する
    /// （v1.3.1 stage5 追加）。
    /// <para>
    /// HTML エンティティ（<c>&amp;amp;</c> 等）のデコードは行わない。スタッフ名・楽曲名のような
    /// 短い人手入力フィールドが想定で、<c>&lt;</c> のような特殊文字を含まない前提とする。
    /// 正規表現は素朴な「<c>&lt;</c> から <c>&gt;</c> までを 1 タグとみなす」非貪欲マッチで、
    /// 属性値内に <c>&gt;</c> を含むタグ（HTML としては不正だが理論上可能）は対象外。
    /// </para>
    /// </summary>
    private static string StripHtmlTags(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "");
    }

    /// <summary>
    /// 主要スタッフ（脚本／絵コンテ／演出／作画監督／美術監督）の表示行を構築する。
    /// クレジット階層から該当役職のエントリを抽出し、PERSON エントリの名義を集めて 1 行にまとめる。
    /// <para>
    /// 役職判定は、(1) <c>role_code</c> 候補との完全一致、(2) <c>name_ja</c>（日本語表示名）との完全一致、
    /// のいずれかでヒットすればその役職とみなす。運用者が独自の <c>role_code</c> を採用していても
    /// 日本語表示名さえ合っていれば抽出されるよう、両側のマッチを許容する。
    /// </para>
    /// <para>
    /// 表示順は「脚本 → 絵コンテ → 演出 → 作画監督 → 美術監督」固定。
    /// 該当役職が見つからない／配下にエントリが無い場合はその行を出さない。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyList<StaffRow>> BuildStaffRowsAsync(
        IReadOnlyList<Credit> credits,
        CancellationToken ct)
    {
        // 役職マスタを 1 度だけ引く（複数エピソード生成中に使い回す）。
        if (_roleMap is null)
        {
            var allRoles = await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false);
            _roleMap = allRoles.ToDictionary(r => r.RoleCode, r => r, StringComparer.Ordinal);
        }

        // v1.3.0 続編：スタッフセクションは脚本／絵コンテ／演出／作画監督／美術 の 5 役職を
        // 別々のラインで出すが、絵コンテと演出が同じ人物（同じ集合）になった場合だけ
        // 「絵コンテ・演出」の 1 ラインに統合する。そのため一旦は役職コード単位で
        // (重複判定キー → 表示用 HTML) のペアリストとして集めておき、最後に行 DTO を組み立てる。
        // 抽出対象役職と、役職コード／表示名の候補。
        // role_code がリポジトリ内の役職マスタに実在する値を直接指定する。
        // 加えて name_ja 側でもマッチを許容するので、マスタが今後追加・改名されても拾いやすい。
        var staffSpecs = new[]
        {
            new StaffSpec("脚本",     new[] { "SCREENPLAY" },         new[] { "脚本" }),
            new StaffSpec("絵コンテ", new[] { "STORYBOARD" },         new[] { "絵コンテ" }),
            new StaffSpec("演出",     new[] { "EPISODE_DIRECTOR" },   new[] { "演出" }),
            new StaffSpec("作画監督", new[] { "ANIMATION_DIRECTOR" }, new[] { "作画監督" }),
            new StaffSpec("美術",     new[] { "ART_DIRECTOR" },       new[] { "美術" })
        };

        // 役職コード → スタッフ仕様の逆引きを 1 度だけ作る。
        // role_code がコード候補に一致、または当該 role の name_ja が表示名候補のいずれかに一致するとき採用。
        var roleCodeToSpec = new Dictionary<string, StaffSpec>(StringComparer.Ordinal);
        foreach (var (code, role) in _roleMap)
        {
            foreach (var spec in staffSpecs)
            {
                if (spec.RoleCodeCandidates.Contains(code, StringComparer.Ordinal)
                    || (role.NameJa is { } nm && spec.RoleNameCandidates.Contains(nm, StringComparer.Ordinal)))
                {
                    if (!roleCodeToSpec.ContainsKey(code))
                        roleCodeToSpec[code] = spec;
                }
            }
        }

        // 仕様ラベル → 集めた人物名（HTML 断片）のリスト。
        // 重複判定キーは PERSON エントリなら "P:{alias_id}"、TEXT エントリなら "T:{raw_text}" とし、
        // リンク化の有無に関わらず同一エントリを 1 度だけ表示するようにする。
        // v1.3.0 続編：絵コンテと演出の同一性判定にも使うため、キー集合（HashSet<string>）も保持しておく。
        var collected = staffSpecs.ToDictionary(s => s.Label, _ => new List<string>(), StringComparer.Ordinal);
        var seen = staffSpecs.ToDictionary(s => s.Label, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        // クレジット → カード → tier → group → cardRole の順で走査して、
        // ヒット役職配下の PERSON エントリの名義を引く。
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
                            if (cr.RoleCode is null) continue;
                            if (!roleCodeToSpec.TryGetValue(cr.RoleCode, out var spec)) continue;

                            // 配下のブロックとエントリを引いて、PERSON エントリの名義のみを集める。
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
                                    if (seen[spec.Label].Add(key))
                                        collected[spec.Label].Add(html);
                                }
                            }
                        }
                    }
                }
            }
        }

        // v1.3.0 続編：絵コンテ・演出が同一集合（同じ重複キー集合）の場合、1 ラインに統合する。
        // 例：絵コンテ＝伊藤 尚往、演出＝伊藤 尚往 → 「絵コンテ・演出 伊藤 尚往」
        // 異なる場合は従来通り 2 行に分ける（絵コンテ A、演出 B）。
        // 統合判定はキー集合の集合比較で行う（HTML 表現の文字列比較だと alias の表示揺れに弱いため）。
        bool storyboardDirectorMerged =
            seen["絵コンテ"].Count > 0
            && seen["演出"].Count > 0
            && seen["絵コンテ"].SetEquals(seen["演出"]);

        // 役職コード解決：表示順は仕様の配列順を踏襲。各行の RoleUrl は系譜代表 role_code の役職統計ページ。
        // 統合行 (絵コンテ・演出) のときは特例的に「絵コンテ用 URL」と「演出用 URL」の両方を保持し、
        // テンプレ側で「絵コンテ」「演出」それぞれを別リンクとして描画できるようにする。
        string? RoleUrl(string label) => label switch
        {
            "脚本" => RoleStatsUrlFor("SCREENPLAY", "脚本"),
            "絵コンテ" => RoleStatsUrlFor("STORYBOARD", "絵コンテ"),
            "演出" => RoleStatsUrlFor("EPISODE_DIRECTOR", "演出"),
            "作画監督" => RoleStatsUrlFor("ANIMATION_DIRECTOR", "作画監督"),
            "美術" => RoleStatsUrlFor("ART_DIRECTOR", "美術"),
            _ => null
        };

        // 仕様の並び順で、エントリのある役職だけ DTO 化。
        // HTML 断片を「、」で連結した文字列を NamesLine に詰める。テンプレ側では html.escape を
        // かけずにそのまま出力する（PERSON エントリは既に <a> タグでラップ済み、TEXT は escape 済み）。
        var rows = new List<StaffRow>();
        foreach (var spec in staffSpecs)
        {
            var names = collected[spec.Label];
            if (names.Count == 0) continue;

            // 統合モード中は絵コンテ単体／演出単体ではなく、絵コンテ位置で 1 行だけ出す（演出はスキップ）。
            if (storyboardDirectorMerged)
            {
                if (spec.Label == "演出") continue;
                if (spec.Label == "絵コンテ")
                {
                    rows.Add(new StaffRow
                    {
                        // 表示ラベル文字列は「絵コンテ・演出」。テンプレ側でリンク分割するため、
                        // 構成役職それぞれの URL を SubRoleLinks にも詰める。
                        RoleLabel = "絵コンテ・演出",
                        // 統合行では RoleCode は空文字（テンプレ側は SubRoleLinks の各 Code を使う）。
                        RoleCode = "",
                        RoleUrl = "",
                        SubRoleLinks = new List<StaffRoleLink>
                        {
                            new StaffRoleLink { Code = "STORYBOARD",        Label = "絵コンテ", Url = RoleUrl("絵コンテ") ?? "" },
                            new StaffRoleLink { Code = "EPISODE_DIRECTOR",  Label = "演出",     Url = RoleUrl("演出")     ?? "" }
                        },
                        NamesLine = string.Join("、", names)
                    });
                    continue;
                }
            }

            // 通常モード：1 役職 1 行で素直に出す。
            rows.Add(new StaffRow
            {
                RoleLabel = spec.Label,
                // RoleCode はバッジの data-role-code に使う。staffSpecs では RoleCodeCandidates の
                // 先頭が代表コード（"SCREENPLAY" / "STORYBOARD" / 等）になる。
                RoleCode = spec.RoleCodeCandidates.Length > 0 ? spec.RoleCodeCandidates[0] : "",
                RoleUrl = RoleUrl(spec.Label) ?? "",
                SubRoleLinks = Array.Empty<StaffRoleLink>(),
                NamesLine = string.Join("、", names)
            });
        }
        return rows;
    }

    /// <summary>
    /// 指定役職コード（or 表示名フォールバック）から、役職統計詳細ページ <c>/stats/roles/{rep}/</c> の
    /// URL を組み立てる（v1.3.0 続編 追加）。
    /// <para>
    /// 1) コード候補そのままが <see cref="RoleSuccessorResolver"/> のクラスタに含まれていればそれを採用。
    /// 2) 含まれていなければ表示名候補（"脚本" 等）でマスタを走査し、ヒットしたコードのクラスタ代表を採用。
    /// 3) どちらでも引けないときは <c>null</c>（テンプレ側でリンク化を抑止）。
    /// </para>
    /// </summary>
    private string? RoleStatsUrlFor(string preferredRoleCode, string fallbackNameJa)
    {
        // 役職マスタが未ロードならリンク化スキップ（直前段で必ずロードしているはずだが念のため）。
        if (_roleMap is null) return null;

        // 1) 推奨コードが存在すれば、その系譜代表 → URL。
        if (_roleMap.ContainsKey(preferredRoleCode))
        {
            string rep = _roleSuccessorResolver.GetRepresentative(preferredRoleCode);
            if (!string.IsNullOrEmpty(rep)) return PathUtil.RoleStatsUrl(rep);
        }

        // 2) name_ja フォールバック検索：表示名が一致する役職コードを 1 件採用。
        foreach (var (code, role) in _roleMap)
        {
            if (string.Equals(role.NameJa, fallbackNameJa, StringComparison.Ordinal))
            {
                string rep = _roleSuccessorResolver.GetRepresentative(code);
                if (!string.IsNullOrEmpty(rep)) return PathUtil.RoleStatsUrl(rep);
            }
        }

        return null;
    }

    /// <summary>
    /// スタッフ役職配下のエントリ 1 件から (重複判定キー, 表示用 HTML 文字列) を取り出す。
    /// PERSON エントリは <see cref="StaffNameLinkResolver"/> 経由で人物詳細ページへの &lt;a&gt; リンク HTML に
    /// 変換し、TEXT エントリは HTML エスケープのみ施したプレーンテキストにする。
    /// 重複判定キーは PERSON なら <c>"P:{alias_id}"</c>、TEXT なら <c>"T:{raw_text}"</c>。
    /// それ以外（CHARACTER_VOICE / COMPANY / LOGO）は空文字 + 空 HTML を返して呼び出し元で除外する。
    /// 所属（屋号）は表示しない（スタッフ一覧は素朴に「役職 — 名前、名前、名前」で出す方針）。
    /// </summary>
    private async Task<(string Key, string Html)> ResolveStaffEntryAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    var pa = await _personAliasesRepo.GetByIdAsync(pid, ct).ConfigureAwait(false);
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

    /// <summary>
    /// スタッフ役職配下のエントリ 1 件から表示用の人物名を取り出す（プレーンテキスト版、v1.2 系から残存）。
    /// 現状は本ファイル内では参照されないが、将来別文脈での利用を想定して保持。
    /// PERSON / TEXT のときだけ採用し、CHARACTER_VOICE / COMPANY / LOGO は null を返す。
    /// 所属（屋号）は表示しない（スタッフ一覧は素朴に「役職 — 名前、名前、名前」で出す方針）。
    /// </summary>
    private async Task<string?> ResolveStaffEntryNameAsync(CreditBlockEntry e, CancellationToken ct)
    {
        switch (e.EntryKind)
        {
            case "PERSON":
                if (e.PersonAliasId is int pid)
                {
                    var pa = await _personAliasesRepo.GetByIdAsync(pid, ct).ConfigureAwait(false);
                    return pa?.DisplayTextOverride ?? pa?.Name;
                }
                return null;
            case "TEXT":
                return e.RawText;
            default:
                return null;
        }
    }

    /// <summary>スタッフ役職の判定スペック。</summary>
    private sealed record StaffSpec(string Label, string[] RoleCodeCandidates, string[] RoleNameCandidates);

    /// <summary>
    /// YouTube URL から動画 ID を抽出する。失敗時は空文字を返す。
    /// 埋め込み iframe を生成するため。
    /// </summary>
    private static string ExtractYoutubeId(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        // 典型的な 4 パターンを直接見る:
        //   https://www.youtube.com/watch?v=XXXX
        //   https://youtu.be/XXXX
        //   https://www.youtube.com/embed/XXXX
        //   https://m.youtube.com/watch?v=XXXX
        // 11 文字の英数字 + アンダースコア + ハイフンが ID。
        var m = System.Text.RegularExpressions.Regex.Match(
            url,
            @"(?:youtube\.com/watch\?v=|youtu\.be/|youtube\.com/embed/)([A-Za-z0-9_\-]{11})");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>
    /// 放送日時を「2004年2月1日 8:30〜9:00」または「2004年2月1日 8:30」フォーマットで返す。
    /// <paramref name="durationMinutes"/> が NULL（尺未登録）の場合は終了時刻を表示しない。
    /// 尺登録済みの場合は分単位で加算した終了時刻も併記する。
    /// 時刻部分は <c>H:mm</c>（先頭ゼロなし、分は 2 桁）。
    /// </summary>
    private static string FormatJpDateTimeWithDuration(DateTime dt, byte? durationMinutes)
    {
        string head = $"{dt.Year}年{dt.Month}月{dt.Day}日 {dt.Hour}:{dt.Minute:D2}";
        if (!durationMinutes.HasValue || durationMinutes.Value == 0) return head;

        var endDt = dt.AddMinutes(durationMinutes.Value);
        return $"{head}〜{endDt.Hour}:{endDt.Minute:D2}";
    }

    /// <summary>
    /// 同シリーズ全話の中から「現在話の前後 ±2 件 + 先頭 + 末尾」のページネーション項目を組み立てる。
    /// 隣接しないインデックス間には省略記号（<c>IsEllipsis = true</c>）の仮想項目を挟む。
    /// 結果はそのままテンプレ側で「1 … 10 11 [12] 13 14 … 50」のような典型的な
    /// Web ページネーションに展開できる。
    /// </summary>
    /// <param name="siblings">同シリーズ全話（順序不問）。</param>
    /// <param name="current">現在話。</param>
    /// <param name="seriesSlug">URL 組み立て用のシリーズ slug。</param>
    /// <param name="window">現在話の前後に表示する話数（既定 2 = 計 5 話を中央に表示）。</param>
    private static IReadOnlyList<PaginationItem> BuildPagination(
        IReadOnlyList<Episode> siblings,
        Episode current,
        string seriesSlug,
        int window = 2)
    {
        // 並びを SeriesEpNo 昇順に固定。
        var ordered = siblings.OrderBy(x => x.SeriesEpNo).ToList();
        if (ordered.Count == 0) return Array.Empty<PaginationItem>();

        int currentIdx = ordered.FindIndex(x => x.EpisodeId == current.EpisodeId);
        if (currentIdx < 0) currentIdx = 0;

        // 表示すべきインデックス（昇順、重複除去）を集める。
        // - 先頭（0）
        // - 末尾（Count-1）
        // - 現在の前後 ±window
        var indices = new SortedSet<int>();
        indices.Add(0);
        indices.Add(ordered.Count - 1);
        for (int i = currentIdx - window; i <= currentIdx + window; i++)
        {
            if (i >= 0 && i < ordered.Count) indices.Add(i);
        }

        // 隣接しない（差が 2 以上）箇所に省略記号を挟みつつアイテム化。
        var result = new List<PaginationItem>();
        int? prev = null;
        foreach (var idx in indices)
        {
            if (prev.HasValue && idx - prev.Value >= 2)
            {
                result.Add(new PaginationItem { IsEllipsis = true });
            }
            var ep = ordered[idx];
            result.Add(new PaginationItem
            {
                SeriesEpNo = ep.SeriesEpNo,
                Url = PathUtil.EpisodeUrl(seriesSlug, ep.SeriesEpNo),
                IsCurrent = ep.EpisodeId == current.EpisodeId
            });
            prev = idx;
        }
        return result;
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class EpisodeContentModel
    {
        public SeriesRefView Series { get; set; } = new();
        public EpisodeView Episode { get; set; } = new();
        public FormatTableModel FormatTable { get; set; } = new();
        public string TitleCharInfoHtml { get; set; } = "";
        public IReadOnlyList<PartLengthStatRow> PartLengthStats { get; set; } = Array.Empty<PartLengthStatRow>();
        /// <summary>
        /// パート尺統計表のヘッダで使う「『○○プリキュア』」（引用符込み）。
        /// テンプレ側で 2 段ヘッダの上段ラベルとして展開する。
        /// </summary>
        public string SeriesTitleShortQuoted { get; set; } = "";
        public IReadOnlyList<ThemeSongRow> ThemeSongs { get; set; } = Array.Empty<ThemeSongRow>();
        public IReadOnlyList<CreditBlockView> CreditBlocks { get; set; } = Array.Empty<CreditBlockView>();
        /// <summary>主要スタッフ情報（脚本／絵コンテ／演出／作画監督／美術）。クレジット階層から抽出した抜粋。</summary>
        public IReadOnlyList<StaffRow> Staff { get; set; } = Array.Empty<StaffRow>();
        /// <summary>
        /// 使用音声セクション。episode_uses をパート別にグルーピングしたもの。
        /// 0 件のエピソードでは空配列で、テンプレ側でセクション自体を非表示にする。
        /// </summary>
        public IReadOnlyList<EpisodeUseSection> EpisodeUseSections { get; set; } = Array.Empty<EpisodeUseSection>();
        /// <summary>通算情報の項目列（シリーズ内話数 + 全シリーズ通算 + ニチアサ通算 等）。テンプレ側で枠線なし表組として描画。</summary>
        public IReadOnlyList<TotalsItem> Totals { get; set; } = Array.Empty<TotalsItem>();
        /// <summary>
        /// ビルド時刻時点の参照点キャプション（例：「2026年5月3日現在、『キミとアイドルプリキュア♪』第14話時点」）。
        /// 毎週変動するセクションの説明文末尾に付ける。
        /// </summary>
        public string BuildPointCaption { get; set; } = "";
        /// <summary>
        /// クレジット横断のサイト全体カバレッジラベル（v1.3.0 ブラッシュアップ続編で追加）。
        /// 「YYYY年M月D日現在 『○○プリキュア』第N話時点の情報を表示しています」表記。
        /// テンプレ側の h1 ブロック直後に独立段落で表示する。
        /// 上記 <see cref="BuildPointCaption"/> はパート尺統計など個別セクションの参照点表記であり、
        /// ページ全体のカバレッジ宣言とは別物。
        /// </summary>
        public string CoverageLabel { get; set; } = "";
        public string PrevUrl { get; set; } = "";
        public string PrevLabel { get; set; } = "";
        public string NextUrl { get; set; } = "";
        public string NextLabel { get; set; } = "";
        /// <summary>ページネーション端ボタンに表示する前話ラベル（例：「#3 〇〇〇」）。前話が無いときは空文字。</summary>
        public string PrevPagerLabel { get; set; } = "";
        /// <summary>ページネーション端ボタンに表示する次話ラベル（例：「#5 〇〇〇」）。次話が無いときは空文字。</summary>
        public string NextPagerLabel { get; set; } = "";
        /// <summary>同シリーズ全話分の話数ページネーション。テンプレ側で上下 2 か所に展開する。</summary>
        public IReadOnlyList<PaginationItem> Pagination { get; set; } = Array.Empty<PaginationItem>();
    }

    /// <summary>使用音声セクションのパート別グループ 1 件分。</summary>
    private sealed class EpisodeUseSection
    {
        /// <summary>パート種別の表示ラベル（例: "アバン"、"Aパート"）。</summary>
        public string PartLabel { get; set; } = "";
        /// <summary>このパート内の使用音声行（use_order, sub_order 昇順）。</summary>
        public IReadOnlyList<EpisodeUseRow> Uses { get; set; } = Array.Empty<EpisodeUseRow>();
    }

    /// <summary>使用音声 1 行分。SONG なら歌詳細リンク付き、BGM なら M 番号 + メニュータイトル、その他はテキスト。</summary>
    private sealed class EpisodeUseRow
    {
        public byte UseOrder { get; set; }
        public byte SubOrder { get; set; }
        /// <summary>内容種別の表示ラベル（"歌"、"劇伴"、"ドラマ" 等）。</summary>
        public string ContentKindLabel { get; set; } = "";
        /// <summary>主表示テキスト（歌のタイトル、劇伴のメニュータイトル、テキスト系の override 文字列など）。</summary>
        public string Title { get; set; } = "";
        /// <summary>補助情報（歌唱者・サイズ・パート・M 番号・作曲など）。</summary>
        public string SubTitle { get; set; } = "";
        /// <summary>シーン説明（任意）。</summary>
        public string SceneLabel { get; set; } = "";
        /// <summary>使用尺ラベル（"m:ss" 形式）。秒数情報がなければ空文字。</summary>
        public string DurationLabel { get; set; } = "";
        /// <summary>SONG 行のときの楽曲詳細ページへのリンク（それ以外は空）。</summary>
        public string SongLink { get; set; } = "";
        /// <summary>本放送限定フラグ（「本放送のみ」を末尾に表示）。</summary>
        public bool IsBroadcastOnly { get; set; }
    }

    /// <summary>
    /// 主要スタッフ 1 行（役職名 + 人物名のリスト）。
    /// <para>
    /// v1.3.0 続編：役職ラベルを役職統計詳細ページにリンクできるよう <see cref="RoleUrl"/> を追加。
    /// 絵コンテ・演出が同一スタッフのとき「絵コンテ・演出」の統合ラベルになるケースは
    /// <see cref="SubRoleLinks"/> に絵コンテと演出それぞれのリンクを詰めて、テンプレ側で
    /// 「絵コンテ」「演出」のリンクを別々の anchor として描画する。
    /// </para>
    /// </summary>
    private sealed class StaffRow
    {
        /// <summary>表示用役職名（"脚本" / "絵コンテ" / "演出" / "作画監督" / "美術" / "絵コンテ・演出"）。</summary>
        public string RoleLabel { get; set; } = "";

        /// <summary>
        /// 役職代表コード（v1.3.1 stage B-3 追加）。
        /// "SCREENPLAY" / "STORYBOARD" / "EPISODE_DIRECTOR" / "ANIMATION_DIRECTOR" / "ART_DIRECTOR"。
        /// バッジの <c>data-role-code</c> 属性に詰めて、シリーズ一覧と同じバッジ色付けに使う。
        /// 統合行「絵コンテ・演出」では空文字（テンプレ側は SubRoleLinks の各 Code を使う）。
        /// </summary>
        public string RoleCode { get; set; } = "";

        /// <summary>
        /// 役職統計詳細ページの URL（v1.3.0 続編 追加）。<c>"/stats/roles/{rep_role_code}/"</c> 形式。
        /// 空文字のときはテンプレ側でリンク化せずプレーンテキスト表示。
        /// 「絵コンテ・演出」統合行ではこの値ではなく <see cref="SubRoleLinks"/> を使う。
        /// </summary>
        public string RoleUrl { get; set; } = "";

        /// <summary>
        /// 統合ラベル時の構成役職リンク群（v1.3.0 続編 追加）。
        /// 通常モードでは空。「絵コンテ・演出」統合時のみ「絵コンテ」と「演出」のリンク 2 件が並ぶ。
        /// テンプレ側で <c>SubRoleLinks.Count &gt; 0</c> なら各リンクを「・」区切りで描画する分岐ロジックに使う。
        /// </summary>
        public IReadOnlyList<StaffRoleLink> SubRoleLinks { get; set; } = Array.Empty<StaffRoleLink>();

        /// <summary>人物名（複数なら「、」で連結された文字列）。</summary>
        public string NamesLine { get; set; } = "";
    }

    /// <summary>
    /// 統合ラベル「絵コンテ・演出」を分割描画するためのリンク 1 件分（v1.3.0 続編 追加）。
    /// テンプレ側では <c>&lt;a href="{Url}"&gt;{Label}&lt;/a&gt;</c> として埋め込む。
    /// </summary>
    private sealed class StaffRoleLink
    {
        /// <summary>役職代表コード（v1.3.1 stage B-3 追加。バッジの data-role-code に使う）。</summary>
        public string Code { get; set; } = "";
        public string Label { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>通算情報 1 項目（ラベル + 値）。テンプレ側で「ラベル 値」の 2 列で横に並べ、枠線なしで描画する。</summary>
    private sealed class TotalsItem
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>話数ページネーションの 1 項目。</summary>
    private sealed class PaginationItem
    {
        public int SeriesEpNo { get; set; }
        public string Url { get; set; } = "";
        public bool IsCurrent { get; set; }
        /// <summary>省略記号（…）を出すための仮想項目。SeriesEpNo / Url は無効。</summary>
        public bool IsEllipsis { get; set; }
    }

    private sealed class SeriesRefView
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string TitleShort { get; set; } = "";
    }

    private sealed class EpisodeView
    {
        public int SeriesEpNo { get; set; }
        public string TotalEpNo { get; set; } = "";
        public string TotalOaNo { get; set; } = "";
        public string NitiasaOaNo { get; set; } = "";
        public string TitleText { get; set; } = "";
        public string TitleRichHtml { get; set; } = "";
        public string TitleKana { get; set; } = "";
        /// <summary>放送日時を「2004年2月1日 8:30〜9:00」形式で。尺未登録時は終了時刻なし。</summary>
        public string OnAirDateTime { get; set; } = "";
        public string ToeiAnimSummaryUrl { get; set; } = "";
        public string ToeiAnimLineupUrl { get; set; } = "";
        public string YoutubeTrailerUrl { get; set; } = "";
        public string YoutubeId { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class PartLengthStatRow
    {
        public string PartName { get; set; } = "";
        public int SeriesRank { get; set; }
        public int SeriesTotal { get; set; }
        public string SeriesHensachi { get; set; } = "";
        public int GlobalRank { get; set; }
        public int GlobalTotal { get; set; }
        public string GlobalHensachi { get; set; } = "";
    }

    /// <summary>主題歌 1 行（テーブルではなく縦リスト 1 行表現）の DTO。</summary>
    private sealed class ThemeSongRow
    {
        /// <summary>区分ラベル（"OP" / "ED" / "挿入歌"）。</summary>
        public string KindLabel { get; set; } = "";
        /// <summary>楽曲タイトル。テンプレ側で <c>「タイトル」</c> のようにカギ括弧で括る。</summary>
        public string Title { get; set; } = "";
        /// <summary>楽曲詳細ページへのリンク URL（song_id が引けたときだけセット）。</summary>
        public string SongLink { get; set; } = "";
        /// <summary>録音バージョン表記（例: "TV size"）。空文字なら表示しない。</summary>
        public string VariantLabel { get; set; } = "";
        /// <summary>
        /// 歌唱者のフリーテキスト（旧仕様 <c>song_recordings.singer_name</c>）。
        /// v1.3.0 公開直前のデザイン整理 第 N+2 弾以降は <see cref="VocalistsHtml"/> の構造化表示が
        /// 優先される（Generator 内でフォールバック処理済み、テンプレは VocalistsHtml だけを見ればよい）。
        /// </summary>
        public string SingerName { get; set; } = "";
        /// <summary>備考（任意）。</summary>
        public string Notes { get; set; } = "";
        /// <summary>本放送限定フラグ（「（本放送のみ）」を末尾に併記する）。</summary>
        public bool IsBroadcastOnly { get; set; }

        // ── v1.3.0 公開直前のデザイン整理 第 N+2 弾：構造化クレジット由来の HTML 群 ──
        /// <summary>
        /// 作詞の表示用 HTML。構造化 <c>song_credits</c> 行があれば名義リンク（/persons/{id}/）を
        /// PrecedingSeparator で連結した HTML、行が無く <see cref="SingerName"/> 同等のフリーテキスト
        /// （<c>songs.lyricist_name</c>）が非空なら HTML エスケープした平文。どちらも無ければ空文字。
        /// </summary>
        public string LyricsHtml { get; set; } = "";
        /// <summary>「作詞」役職ラベル HTML（/stats/roles/{rep}/ リンク化済み、未登録時は平文）。</summary>
        public string LyricsRoleLabelHtml { get; set; } = "";
        /// <summary>作曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string CompositionHtml { get; set; } = "";
        /// <summary>「作曲」役職ラベル HTML。</summary>
        public string CompositionRoleLabelHtml { get; set; } = "";
        /// <summary>編曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string ArrangementHtml { get; set; } = "";
        /// <summary>「編曲」役職ラベル HTML。</summary>
        public string ArrangementRoleLabelHtml { get; set; } = "";
        /// <summary>
        /// 歌唱者の表示用 HTML。構造化 <c>song_recording_singers</c>（VOCALS 役）があれば
        /// キャラ/人物リンク化した HTML、無ければ <see cref="SingerName"/> の HTML エスケープ平文。
        /// テンプレ側で空判定して「歌：」行を出し分ける。
        /// </summary>
        public string VocalistsHtml { get; set; } = "";
        /// <summary>
        /// 「歌」役職ラベル HTML（v1.3.1 stage B-5 追加）。
        /// 他の作詞・作曲・編曲ラベルと同様に <c>/stats/roles/VOCALS/</c> へのリンク付き HTML。
        /// 未登録時はフォールバック固定文字列「歌」が入る。
        /// </summary>
        public string VocalistsRoleLabelHtml { get; set; } = "";
    }

    private sealed class CreditBlockView
    {
        public string CreditKindLabel { get; set; } = "";
        public string Html { get; set; } = "";
    }
}