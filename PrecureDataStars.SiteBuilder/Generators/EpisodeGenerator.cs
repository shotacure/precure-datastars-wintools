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
///   <item>フォーマット表（OA / 配信 / 円盤 の累積タイムコード）</item>
///   <item>サブタイトル文字情報（初出 / 唯一 / N年Mか月ぶり）</item>
///   <item>サブタイトル文字統計（title_char_stats JSON のカテゴリ別件数表示）</item>
///   <item>パート尺偏差値（AVANT/PART_A/PART_B シリーズ内・歴代）</item>
///   <item>主題歌（OP / ED / 挿入歌、本放送限定行があれば併記）</item>
///   <item>クレジット階層（OP / ED）</item>
///   <item>前後話ナビ</item>
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

    // ── スタッフ名リンク化（v1.3.0 追加：エピソード詳細のスタッフセクションに人物詳細リンクを張る） ──
    private readonly StaffNameLinkResolver _staffLinkResolver;

    // ── 使用音声（episode_uses）解決用リポジトリ群（v1.3.0 後半追加：エピソード詳細に「使用音声」セクションを付与するため） ──
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
        StaffNameLinkResolver staffLinkResolver)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _staffLinkResolver = staffLinkResolver;

        _episodesRepo = new EpisodesRepository(factory);
        _partsRepo = new EpisodePartsRepository(factory);
        _themeRepo = new EpisodeThemeSongsRepository(factory);
        _songRecRepo = new SongRecordingsRepository(factory);
        _songsRepo = new SongsRepository(factory);
        _creditsRepo = new CreditsRepository(factory);
        _creditKindsRepo = new CreditKindsRepository(factory);

        // スタッフ抽出用にクレジット階層 Repository を再利用（CreditTreeRenderer と同じ方法で参照）。
        _staffCardsRepo = new CreditCardsRepository(factory);
        _staffTiersRepo = new CreditCardTiersRepository(factory);
        _staffGroupsRepo = new CreditCardGroupsRepository(factory);
        _staffCardRolesRepo = new CreditCardRolesRepository(factory);
        _staffBlocksRepo = new CreditRoleBlocksRepository(factory);
        _staffEntriesRepo = new CreditBlockEntriesRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);

        // 使用音声セクション用の Repository（v1.3.0 後半追加）。
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
        var lookup = new LookupCache(
            new PersonAliasesRepository(factory),
            new CompanyAliasesRepository(factory),
            new LogosRepository(factory),
            new CharacterAliasesRepository(factory),
            factory);

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
            lookup);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating episodes");

        int total = 0;
        foreach (var s in _ctx.Series)
        {
            if (!_ctx.EpisodesBySeries.TryGetValue(s.SeriesId, out var eps)) continue;
            foreach (var e in eps)
            {
                await GenerateOneAsync(s, e, ct).ConfigureAwait(false);
                total++;
            }
        }
        _ctx.Logger.Success($"episodes: {total} ページ");
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
        var partLengthStats = await _partsRepo.GetPartLengthStatsAsync(ep.EpisodeId, ct).ConfigureAwait(false);
        var partLengthStatRows = partLengthStats.Select(s => new PartLengthStatRow
        {
            PartName = s.PartTypeNameJa,
            SeriesShort = s.SeriesTitleShort,
            SeriesRank = s.SeriesRank,
            SeriesTotal = s.SeriesTotal,
            SeriesHensachi = s.SeriesHensachi.ToString("0.00"),
            GlobalRank = s.GlobalRank,
            GlobalTotal = s.GlobalTotal,
            GlobalHensachi = s.GlobalHensachi.ToString("0.00")
        }).ToList();

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

        // 主題歌（OP/挿入歌/ED）。
        // EpisodeThemeSongsRepository.GetByEpisodeAsync の SQL は
        // ORDER BY is_broadcast_only, theme_kind, insert_seq だが、
        // theme_kind は VARCHAR で辞書順だと ED < INSERT < OP になってしまうため、
        // SiteBuilder 側で劇中使用順 OP=1 → INSERT=2 → ED=3 に並べ直す（OP は冒頭、ED は最後）。
        // INSERT 内の順序は insert_seq（DB 上の挿入歌順）を尊重。
        // 本放送限定行は通常行の後ろにまとめる扱い。
        // 本ソートは BuildThemeRowsAsync 内でも再適用しているため二重だが、
        // 念のため事前ソートしておくことで、テーマ行を直接利用する他のロジックでも
        // 順序が保たれる。
        static int ThemeKindOrder(string k) => k switch { "OP" => 1, "INSERT" => 2, "ED" => 3, _ => 9 };
        var themes = (await _themeRepo.GetByEpisodeAsync(ep.EpisodeId, ct).ConfigureAwait(false))
            .OrderBy(x => x.IsBroadcastOnly)
            .ThenBy(x => ThemeKindOrder(x.ThemeKind))
            .ThenBy(x => x.InsertSeq)
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

        // 使用音声（episode_uses）セクションをパート別に構築（v1.3.0 後半追加）。
        // 該当エピソードに登録されている全 episode_uses 行をパート種別ごとにグルーピングし、
        // SONG / BGM / テキスト系（DRAMA/RADIO/JINGLE/OTHER）それぞれ表示用にラベルを解決する。
        // 0 件のエピソードでは空配列が返り、テンプレ側でセクション自体を非表示にする。
        var episodeUseSections = await BuildEpisodeUsesViewAsync(ep.EpisodeId, ct).ConfigureAwait(false);

        // 通算情報を 1 行にまとめる（基本情報を整理して行数を抑える）。
        // 先頭に「シリーズ内 第N話」を含め、続けて全シリーズ通算 / ニチアサ通算を ' / ' で並べる。
        // 例: "シリーズ内 第1話 / 全シリーズ通算 第123話 / 通算第125回 / ニチアサ通算第891回"
        // 通算情報そのものは TotalsItem の連なりとしてテンプレに渡し、
        // テンプレ側で枠線なしの簡潔な表組として描画する。
        var totalsItems = new List<TotalsItem>
        {
            new TotalsItem { Label = "シリーズ内", Value = $"第{ep.SeriesEpNo}話" }
        };
        if (ep.TotalEpNo is int tep) totalsItems.Add(new TotalsItem { Label = "全シリーズ通算", Value = $"第{tep}話" });
        if (ep.TotalOaNo is int toa) totalsItems.Add(new TotalsItem { Label = "通算放送回", Value = $"第{toa}回" });
        if (ep.NitiasaOaNo is int nio) totalsItems.Add(new TotalsItem { Label = "ニチアサ通算放送回", Value = $"第{nio}回" });

        // テンプレートに渡すモデル。
        var content = new EpisodeContentModel
        {
            Series = new SeriesRefView
            {
                Slug = series.Slug,
                Title = series.Title,
                TitleShort = series.TitleShort ?? series.Title
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
                // 放送日時は「2004年2月1日 8:30」フォーマット。
                // テンプレ側で OnAirDate と OnAirTime に分けず 1 文字列にしておく方が
                // 表記の一貫性が保ちやすいので OnAirDateTime に統合。
                OnAirDateTime = FormatJpDateTimeShort(ep.OnAirAt),
                ToeiAnimSummaryUrl = ep.ToeiAnimSummaryUrl ?? "",
                ToeiAnimLineupUrl = ep.ToeiAnimLineupUrl ?? "",
                YoutubeTrailerUrl = ep.YoutubeTrailerUrl ?? "",
                YoutubeId = ExtractYoutubeId(ep.YoutubeTrailerUrl),
                Notes = ep.Notes ?? ""
            },
            FormatTable = formatTable,
            TitleCharInfoHtml = titleCharInfoHtml,
            PartLengthStats = partLengthStatRows,
            ThemeSongs = themeRows,
            CreditBlocks = creditBlocks,
            Staff = staffRows,
            EpisodeUseSections = episodeUseSections,
            Totals = totalsItems,
            PrevUrl = prev != null ? PathUtil.EpisodeUrl(series.Slug, prev.SeriesEpNo) : "",
            PrevLabel = prev != null ? $"第{prev.SeriesEpNo}話 {prev.TitleText}" : "",
            NextUrl = next != null ? PathUtil.EpisodeUrl(series.Slug, next.SeriesEpNo) : "",
            NextLabel = next != null ? $"第{next.SeriesEpNo}話 {next.TitleText}" : "",
            // 同シリーズ全話分の話数ページネーションを「圧縮表示」用に整形しておく
            // （現在話の前後 ±2 件 + 先頭・末尾 + 省略記号「…」、典型的な Web ページネーション風）。
            Pagination = BuildPagination(siblings, ep, series.Slug)
        };

        // エピソード詳細の構造化データは Schema.org の TVEpisode 型。
        // 親シリーズ partOfSeries、エピソード番号、放送日、エピソード名を主要プロパティとして埋め込む。
        string baseUrl = _ctx.Config.BaseUrl;
        string episodeUrl = PathUtil.EpisodeUrl(series.Slug, ep.SeriesEpNo);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "TVEpisode",
            ["name"] = ep.TitleText,
            ["episodeNumber"] = ep.SeriesEpNo,
            ["datePublished"] = ep.OnAirAt.ToString("yyyy-MM-dd"),
            ["inLanguage"] = "ja"
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
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = $"{series.Title} 第{ep.SeriesEpNo}話「{ep.TitleText}」",
            MetaDescription = $"{series.Title} 第{ep.SeriesEpNo}話「{ep.TitleText}」のフォーマット表・スタッフ・主題歌情報。",
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
    /// 主題歌行を表示用 DTO に変換する。
    /// </summary>
    private async Task<IReadOnlyList<ThemeSongRow>> BuildThemeRowsAsync(
        IReadOnlyList<EpisodeThemeSong> themes,
        CancellationToken ct)
    {
        // 録音 → 曲を引いて表示文字列を組む。同じ録音 ID が複数行で参照されることもあるので
        // 一回 cache に入れておく（OP/ED/INSERT の 3 行同時取得を想定）。
        var recCache = new Dictionary<int, SongRecording?>();
        var songCache = new Dictionary<int, Song?>();

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

        var rows = new List<ThemeSongRow>(themes.Count);
        // 劇中使用順に並べる: OP（冒頭）→ 挿入歌（本編中、insert_seq 順）→ ED（最後）。
        // 本放送限定行は通常行の後ろにまとめる扱い。
        // この順序を SiteBuilder 側で固定することで、GetByEpisodeAsync の SQL が
        // theme_kind を辞書順（ED < INSERT < OP）で返してくる問題を吸収する。
        static int InProgramOrder(string k) => k switch { "OP" => 1, "INSERT" => 2, "ED" => 3, _ => 9 };
        foreach (var t in themes
            .OrderBy(x => x.IsBroadcastOnly)
            .ThenBy(x => InProgramOrder(x.ThemeKind))
            .ThenBy(x => x.InsertSeq))
        {
            var (rec, song) = await ResolveAsync(t.SongRecordingId).ConfigureAwait(false);
            string kindLabel = t.ThemeKind switch
            {
                "OP" => "OP",
                "ED" => "ED",
                "INSERT" => $"挿入歌 {t.InsertSeq}",
                _ => t.ThemeKind
            };
            if (t.IsBroadcastOnly) kindLabel += "（本放送のみ）";

            rows.Add(new ThemeSongRow
            {
                KindLabel = kindLabel,
                Title = song?.Title ?? "(曲名未登録)",
                VariantLabel = rec?.VariantLabel ?? "",
                SingerName = rec?.SingerName ?? "",
                Notes = t.Notes ?? ""
            });
        }
        return rows;
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
    /// <summary>
    /// 当該エピソードの <c>episode_uses</c> 行群をパート別にグルーピングして表示用 DTO に変換する（v1.3.0 後半追加）。
    /// <para>
    /// パート種別は <c>part_types.display_order</c> 昇順で並べ、同パート内では (use_order, sub_order) 昇順。
    /// 各行は内容種別（SONG / BGM / DRAMA / RADIO / JINGLE / OTHER）に応じてタイトル・補助情報を解決する：
    /// </para>
    /// <list type="bullet">
    ///   <item><description>SONG: <c>song_recordings</c> + <c>songs</c> から歌タイトル + 歌唱者 + サイズ・パートのバリアントを併記。
    ///     <c>use_title_override</c> があればタイトル表示はそちらを優先（特殊な楽曲表示用）。歌詳細ページへのリンク付き。</description></item>
    ///   <item><description>BGM: <c>(bgm_series_id, bgm_m_no_detail)</c> から M 番号 + メニュータイトル + 作曲を併記。
    ///     仮 M 番号フラグ（<c>is_temp_m_no=1</c>）の行は M 番号表示を「(番号不明)」に差し替える。</description></item>
    ///   <item><description>DRAMA / RADIO / JINGLE / OTHER: <c>use_title_override</c> をそのまま表示。</description></item>
    /// </list>
    /// </summary>
    private async Task<IReadOnlyList<EpisodeUseSection>> BuildEpisodeUsesViewAsync(int episodeId, CancellationToken ct)
    {
        var uses = await _episodeUsesRepo.GetByEpisodeAsync(episodeId, ct).ConfigureAwait(false);
        if (uses.Count == 0) return Array.Empty<EpisodeUseSection>();

        // 表示解決用のマスタを引く。
        // - track_content_kinds: SONG / BGM / DRAMA / ... の表示ラベル
        // - song_size_variants / song_part_variants: サイズ・パート違いのラベル
        // - part_types: パートの表示ラベルと表示順
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

        // 抽出対象の役職定義。表示順は配列の並びそのもの。
        // role_code はリポジトリ内の役職マスタ（roles テーブル）に実在する値を直接指定する。
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

        // 仕様の並び順で、エントリのある役職だけ DTO 化。
        // HTML 断片を「、」で連結した文字列を NamesLine に詰める。テンプレ側では html.escape を
        // かけずにそのまま出力する（PERSON エントリは既に <a> タグでラップ済み、TEXT は escape 済み）。
        var rows = new List<StaffRow>();
        foreach (var spec in staffSpecs)
        {
            var names = collected[spec.Label];
            if (names.Count == 0) continue;
            rows.Add(new StaffRow
            {
                RoleLabel = spec.Label,
                NamesLine = string.Join("、", names)
            });
        }
        return rows;
    }

    /// <summary>
    /// スタッフ役職配下のエントリ 1 件から (重複判定キー, 表示用 HTML 文字列) を取り出す（v1.3.0 追加）。
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
    /// 放送日時を「2004年2月1日 8:30」フォーマットで返す。エディタ表示と揃えるため、
    /// 時刻は <c>H:mm</c>（先頭ゼロなし、分は 2 桁）にする。
    /// </summary>
    private static string FormatJpDateTimeShort(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日 {dt.Hour}:{dt.Minute:D2}";

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
        public IReadOnlyList<ThemeSongRow> ThemeSongs { get; set; } = Array.Empty<ThemeSongRow>();
        public IReadOnlyList<CreditBlockView> CreditBlocks { get; set; } = Array.Empty<CreditBlockView>();
        /// <summary>主要スタッフ情報（脚本／絵コンテ／演出／作画監督／美術）。クレジット階層から抽出した抜粋。</summary>
        public IReadOnlyList<StaffRow> Staff { get; set; } = Array.Empty<StaffRow>();
        /// <summary>
        /// 使用音声セクション（v1.3.0 後半追加）。episode_uses をパート別にグルーピングしたもの。
        /// 0 件のエピソードでは空配列で、テンプレ側でセクション自体を非表示にする。
        /// </summary>
        public IReadOnlyList<EpisodeUseSection> EpisodeUseSections { get; set; } = Array.Empty<EpisodeUseSection>();
        /// <summary>通算情報の項目列（シリーズ内話数 + 全シリーズ通算 + ニチアサ通算 等）。テンプレ側で枠線なし表組として描画。</summary>
        public IReadOnlyList<TotalsItem> Totals { get; set; } = Array.Empty<TotalsItem>();
        public string PrevUrl { get; set; } = "";
        public string PrevLabel { get; set; } = "";
        public string NextUrl { get; set; } = "";
        public string NextLabel { get; set; } = "";
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

    /// <summary>主要スタッフ 1 行（役職名 + 人物名のリスト）。</summary>
    private sealed class StaffRow
    {
        /// <summary>表示用役職名（"脚本" / "絵コンテ" / "演出" / "作画監督" / "美術" のいずれか）。</summary>
        public string RoleLabel { get; set; } = "";
        /// <summary>人物名（複数なら「、」で連結された文字列）。</summary>
        public string NamesLine { get; set; } = "";
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
        /// <summary>放送日時を「2004年2月1日 8:30」形式で。日付と時刻は分けず 1 文字列。</summary>
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
        public string SeriesShort { get; set; } = "";
        public int SeriesRank { get; set; }
        public int SeriesTotal { get; set; }
        public string SeriesHensachi { get; set; } = "";
        public int GlobalRank { get; set; }
        public int GlobalTotal { get; set; }
        public string GlobalHensachi { get; set; } = "";
    }

    private sealed class ThemeSongRow
    {
        public string KindLabel { get; set; } = "";
        public string Title { get; set; } = "";
        public string VariantLabel { get; set; } = "";
        public string SingerName { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class CreditBlockView
    {
        public string CreditKindLabel { get; set; } = "";
        public string Html { get; set; } = "";
    }
}
