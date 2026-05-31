using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>楽曲索引（/songs/）と楽曲詳細（/songs/{song_id}/）の生成。</summary>
public sealed class SongsGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;

    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _songRecordingsRepo;
    private readonly TracksRepository _tracksRepo;
    private readonly DiscsRepository _discsRepo;
    private readonly ProductsRepository _productsRepo;
    private readonly EpisodeThemeSongsRepository _themeSongsRepo;
    // SERIES スコープ（映画系列）の主題歌・挿入歌を引くリポジトリ。
    // 楽曲詳細の「本編使用」セクションで episode_theme_songs（TV 系）と同じ枠で並べて表示する用途。
    private readonly SeriesThemeSongsRepository _seriesThemeSongsRepo;
    private readonly SongMusicClassesRepository _musicClassesRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    // 楽曲詳細ページで作詞・作曲・編曲・歌唱者の
    // 構造化クレジット（song_credits / song_recording_singers）を読み、役職名・名義名を
    // ともにリンク化して表示するために追加した依存。
    // 構造化クレジットを優先し、
    // 構造化があれば常にそちらを優先し、フリーテキストは行が無いときのフォールバックに格下げする。
    private readonly SongCreditsRepository _songCreditsRepo;
    private readonly SongRecordingSingersRepository _songRecordingSingersRepo;
    private readonly RolesRepository _rolesRepo;
    private readonly PersonAliasesRepository _personAliasesRepo;
    private readonly CharacterAliasesRepository _characterAliasesRepo;
    // 同 alias を複数人物が共有するケースの添字付きリンク化に必要（StaffNameLinkResolver と同じ仕組）。
    private readonly StaffNameLinkResolver _staffLinkResolver;
    // 役職コード → 統計ページ用の代表 role_code 解決（/stats/roles/{rep}/）。
    private readonly RoleSuccessorResolver _roleSuccessorResolver;

    public SongsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory,
        StaffNameLinkResolver staffLinkResolver,
        RoleSuccessorResolver roleSuccessorResolver)
    {
        _ctx = ctx;
        _page = page;

        _songsRepo = new SongsRepository(factory);
        _songRecordingsRepo = new SongRecordingsRepository(factory);
        _tracksRepo = new TracksRepository(factory);
        _discsRepo = new DiscsRepository(factory);
        _productsRepo = new ProductsRepository(factory);
        _themeSongsRepo = new EpisodeThemeSongsRepository(factory);
        _seriesThemeSongsRepo = new SeriesThemeSongsRepository(factory);
        _musicClassesRepo = new SongMusicClassesRepository(factory);
        _songSizeVariantsRepo = new SongSizeVariantsRepository(factory);
        _songPartVariantsRepo = new SongPartVariantsRepository(factory);
        _songCreditsRepo = new SongCreditsRepository(factory);
        _songRecordingSingersRepo = new SongRecordingSingersRepository(factory);
        _rolesRepo = new RolesRepository(factory);
        _personAliasesRepo = new PersonAliasesRepository(factory);
        _characterAliasesRepo = new CharacterAliasesRepository(factory);
        _staffLinkResolver = staffLinkResolver;
        _roleSuccessorResolver = roleSuccessorResolver;
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating songs");

        var allSongs = (await _songsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allRecordings = (await _songRecordingsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allDiscs = (await _discsRepo.GetByProductReleaseOrderAsync(ct).ConfigureAwait(false)).ToList();
        var allProducts = (await _productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allThemeSongs = (await _themeSongsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allSeriesThemeSongs = (await _seriesThemeSongsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var musicClasses = (await _musicClassesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var sizeVariants = (await _songSizeVariantsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var partVariants = (await _songPartVariantsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();

        // 作詞・作曲・編曲・歌唱者の構造化マスタを起動時に
        // 一括ロードする。曲数分のクエリを避け、メモリ上で song_id / song_recording_id 毎にグルーピングする。
        var allRoles = (await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        // 全トラック / 全 song_credits / 全 song_recording_singers は BuildContext で事前展開済み。
        // SiteDataLoader が GetAllAsync を 1 度ずつ呼んでメモリ辞書化しているため、本ジェネレータでは
        // 共有辞書をそのまま参照する（同テーブルを ProductsGenerator もすぐ後段で必要とするため、
        // 各ジェネレータが個別に GetAllAsync を発火しないよう中央集約に揃える方針）。
        var allTracks = _ctx.TracksByCatalogNo.Values.SelectMany(t => t).ToList();
        var songCreditsBySong = _ctx.SongCreditsBySong;
        var singersByRecording = _ctx.SingersByRecording;

        var musicClassMap = musicClasses.ToDictionary(c => c.ClassCode, StringComparer.Ordinal);
        var sizeVariantMap = sizeVariants.ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        var partVariantMap = partVariants.ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        var discMap = allDiscs.ToDictionary(d => d.CatalogNo, StringComparer.Ordinal);
        var productMap = allProducts.ToDictionary(p => p.ProductCatalogNo, StringComparer.Ordinal);
        var recordingsBySong = allRecordings.GroupBy(r => r.SongId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.SongRecordingId).ToList());
        var roleMap = allRoles.ToDictionary(r => r.RoleCode, StringComparer.Ordinal);
        var personAliasMap = allPersonAliases.ToDictionary(a => a.AliasId);
        var characterAliasMap = allCharacterAliases.ToDictionary(a => a.AliasId);

        // SongRecordingId → 収録トラック群（複数商品にまたがる場合あり）。
        var tracksByRecording = allTracks
            .Where(t => t.SongRecordingId.HasValue)
            .GroupBy(t => t.SongRecordingId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // SongRecordingId → 主題歌として使用されたエピソード群。
        var themeSongsByRecording = allThemeSongs
            .GroupBy(ts => ts.SongRecordingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // SongRecordingId → SERIES スコープ（映画系列）主題歌として使用されたシリーズ群。
        var seriesThemeSongsByRecording = allSeriesThemeSongs
            .GroupBy(ts => ts.SongRecordingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 楽曲索引 /songs/（シリーズ別フラット表示。左サイドナビは section-nav.js が自動構築）。
        // 各 recording の出典シリーズは song_recordings.series_id を直接参照する。
        GenerateIndex(allSongs, recordingsBySong, musicClassMap,
            songCreditsBySong, singersByRecording, personAliasMap, characterAliasMap);

        foreach (var s in allSongs)
        {
            GenerateDetail(s, recordingsBySong, tracksByRecording, themeSongsByRecording, seriesThemeSongsByRecording,
                discMap, productMap, sizeVariantMap, partVariantMap, musicClassMap,
                songCreditsBySong, singersByRecording, roleMap, personAliasMap, characterAliasMap);
        }

        _ctx.Logger.Success($"songs: {allSongs.Count + 1} ページ");
    }

    /// <summary>
    /// 楽曲索引 /songs/ をレンダリングする。
    /// <para>仕様:
    ///  - 表示単位は「録音バリエーション（song_recording_id）」。
    ///  - 各 recording の出典シリーズ <c>song_recordings.series_id</c> を直接参照してセクション化
    ///    （episodes-index.sbn と同型のフラット 1 ページ運用）。
    ///    series_id が NULL の recording は「その他」バケット（末尾固定）。
    ///  - シリーズ並び順は series.start_date 昇順 → SeriesId 昇順。
    ///  - 各セクション内は song_recording_id 昇順。
    ///  - 行タイトルは variant_label 優先、空なら song.Title。リンクは /songs/{song_id}/。
    ///  - 左サイドナビは section-nav.js が <section id="songs-series-{n}"> を自動検出して
    ///    縦タイムライン形状で構築する。
    /// </para>
    /// </summary>
    private void GenerateIndex(
        IReadOnlyList<Song> songs,
        IReadOnlyDictionary<int, List<SongRecording>> recordingsBySong,
        IReadOnlyDictionary<string, SongMusicClass> musicClassMap,
        IReadOnlyDictionary<int, IReadOnlyList<SongCredit>> songCreditsBySong,
        IReadOnlyDictionary<int, IReadOnlyList<SongRecordingSinger>> singersByRecording,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        var songById = songs.ToDictionary(s => s.SongId);

        // (SeriesId(nullable), Row) のフラット行を全 recording から組み立てる。
        var allRows = new List<(int? SeriesId, SongRecordingIndexRow Row)>();
        foreach (var (songId, recs) in recordingsBySong)
        {
            if (!songById.TryGetValue(songId, out var song)) continue;
            var songCreditRows = songCreditsBySong.TryGetValue(song.SongId, out var creditList) ? creditList : new List<SongCredit>();
            foreach (var r in recs)
            {
                // 出典シリーズは録音モデル直下の SeriesId をそのまま採用（NULL なら「その他」バケット）。
                int? seriesId = r.SeriesId;
                var recordingSingers = singersByRecording.TryGetValue(r.SongRecordingId, out var singerList) ? singerList : new List<SongRecordingSinger>();
                string displayTitle = !string.IsNullOrEmpty(r.VariantLabel) ? r.VariantLabel : song.Title;
                string musicClassLabel = (r.MusicClassCode != null && musicClassMap.TryGetValue(r.MusicClassCode, out var mc)) ? mc.NameJa : "";
                string creditMetaHtml = BuildCreditMetaHtml(
                    songCreditRows, song.LyricistName, song.ComposerName, song.ArrangerName,
                    recordingSingers, r.SingerName, personAliasMap, characterAliasMap);

                allRows.Add((seriesId, new SongRecordingIndexRow
                {
                    SongRecordingId = r.SongRecordingId,
                    SongId = song.SongId,
                    DisplayTitle = displayTitle,
                    MusicClassLabel = musicClassLabel,
                    // CSS クラス末尾は code を小文字化＋アンダースコアをハイフンに（"MOVIE_OP" → "movie-op"）。
                    BadgeClassSuffix = string.IsNullOrEmpty(r.MusicClassCode) ? "" : r.MusicClassCode.ToLowerInvariant().Replace('_', '-'),
                    CreditMetaHtml = creditMetaHtml
                }));
            }
        }

        // シリーズ別にグルーピング。シリーズ並び順は series.start_date 昇順、その他は末尾。
        var seriesSections = allRows
            .GroupBy(x => x.SeriesId)
            .Select(g =>
            {
                int? sid = g.Key;
                string seriesTitle = "その他";
                string seriesSlug = "";
                string seriesStartYearLabel = "";
                DateOnly sortKey = DateOnly.MaxValue;
                if (sid is int sidVal && _ctx.SeriesById.TryGetValue(sidVal, out var series))
                {
                    seriesTitle = series.Title;
                    seriesSlug = series.Slug;
                    seriesStartYearLabel = series.StartDate.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    sortKey = series.StartDate;
                }
                return new SongSeriesSection
                {
                    SeriesTitle = seriesTitle,
                    SeriesSlug = seriesSlug,
                    SeriesStartYearLabel = seriesStartYearLabel,
                    SortKey = sortKey,
                    SortSeriesId = sid ?? int.MaxValue,
                    Recordings = g.OrderBy(x => x.Row.SongRecordingId).Select(x => x.Row).ToList()
                };
            })
            .OrderBy(s => s.SortKey)
            .ThenBy(s => s.SortSeriesId)
            .ToList();

        var content = new SongsIndexModel
        {
            SeriesSections = seriesSections,
            TotalCount = allRows.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュアソング(歌)",
            MetaDescription = "歴代すべてのプリキュアソング。あなたの好きな歌は、どれですか？",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "歴代プリキュアソング(歌)", Url = "" }
            }
        };
        _page.RenderAndWrite("/songs/", "songs", "songs-index.sbn", content, layout);
    }

    /// <summary>楽曲詳細：歌の基本情報 + 録音バージョン + 収録商品 + 主題歌として使用されたエピソード。</summary>
    private void GenerateDetail(
        Song song,
        IReadOnlyDictionary<int, List<SongRecording>> recordingsBySong,
        IReadOnlyDictionary<int, List<Track>> tracksByRecording,
        IReadOnlyDictionary<int, List<EpisodeThemeSong>> themeSongsByRecording,
        IReadOnlyDictionary<int, List<SeriesThemeSong>> seriesThemeSongsByRecording,
        IReadOnlyDictionary<string, Disc> discMap,
        IReadOnlyDictionary<string, Product> productMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<string, SongMusicClass> musicClassMap,
        IReadOnlyDictionary<int, IReadOnlyList<SongCredit>> songCreditsBySong,
        IReadOnlyDictionary<int, IReadOnlyList<SongRecordingSinger>> singersByRecording,
        IReadOnlyDictionary<string, Role> roleMap,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        // 作詞・作曲・編曲：構造化 song_credits を優先、無ければ Song のフリーテキスト列にフォールバック。
        // BuildCreditRoleHtml は名義群（/persons/{id}/ リンク）の HTML を返す。
        // 行のラベル（「作詞」「作曲」「編曲」）も Role マスタを引いて
        // /stats/roles/{rep}/ にリンク化する（テンプレ側に渡す ...RoleLabelHtml がそれ）。
        // 出典シリーズは録音単位で持つようになったため、SongView レベルでは持たず、
        // 各 RecordingView の SeriesTitle / SeriesLink で表現する（後段で組み立て）。
        var songCreditRows = songCreditsBySong.TryGetValue(song.SongId, out var creditRowList) ? creditRowList : new List<SongCredit>();
        string lyricsHtml = BuildCreditRoleHtml(songCreditRows, SongCreditRoles.Lyrics, song.LyricistName, roleMap, personAliasMap);
        string compositionHtml = BuildCreditRoleHtml(songCreditRows, SongCreditRoles.Composition, song.ComposerName, roleMap, personAliasMap);
        string arrangementHtml = BuildCreditRoleHtml(songCreditRows, SongCreditRoles.Arrangement, song.ArrangerName, roleMap, personAliasMap);
        // 役職ラベル：常に roles マスタの NameJa を採用してリンク化する。マスタに行が無い場合は
        // フォールバックの素朴な日本語ラベル（「作詞」など）を出すが、リンクは付けない。
        string lyricsRoleLabelHtml = BuildRoleLabelLinkHtml(SongCreditRoles.Lyrics, roleMap, fallbackLabel: "作詞");
        string compositionRoleLabelHtml = BuildRoleLabelLinkHtml(SongCreditRoles.Composition, roleMap, fallbackLabel: "作曲");
        string arrangementRoleLabelHtml = BuildRoleLabelLinkHtml(SongCreditRoles.Arrangement, roleMap, fallbackLabel: "編曲");

        // 録音バージョン群。
        var recordings = recordingsBySong.TryGetValue(song.SongId, out var recs)
            ? recs : new List<SongRecording>();

        var recordingViews = new List<RecordingView>();
        foreach (var r in recordings)
        {
            // 収録トラック・商品。
            var tracksRows = new List<RecordingTrackRow>();
            if (tracksByRecording.TryGetValue(r.SongRecordingId, out var tracks))
            {
                foreach (var t in tracks)
                {
                    if (!discMap.TryGetValue(t.CatalogNo, out var disc)) continue;
                    if (!productMap.TryGetValue(disc.ProductCatalogNo, out var prod)) continue;
                    // 特例：MJCG-80146（プリキュア「全曲集 1」）、MJCG-83027（同 2）は寄せ集めの
                    // 曲集で、各楽曲の収録盤として並べると煩雑になるため、歌詳細ページの
                    // 収録盤一覧から除外する（劇伴詳細でも同じ品番を除外している）。
                    if (disc.ProductCatalogNo == "MJCG-80146" || disc.ProductCatalogNo == "MJCG-83027") continue;

                    string sizeLabel = (t.SongSizeVariantCode != null && sizeVariantMap.TryGetValue(t.SongSizeVariantCode, out var sv)) ? sv.NameJa : "";
                    string partLabel = (t.SongPartVariantCode != null && partVariantMap.TryGetValue(t.SongPartVariantCode, out var pv)) ? pv.NameJa : "";
                    // 短縮発売日「2024.2.4」形式。商品セル 2 行目に表示する。
                    string releaseShort = $"{prod.ReleaseDate.Year}.{prod.ReleaseDate.Month}.{prod.ReleaseDate.Day}";
                    // Disc/Track 簡略表記。Disc 1 枚しか無い（DiscNoInSet が null）なら「Tr01」、
                    // 複数枚組（DiscNoInSet 値あり）なら「Disc3-Tr23」。Track 番は 2 桁ゼロパディング。
                    string discTrackLabel = disc.DiscNoInSet.HasValue
                        ? $"Disc{disc.DiscNoInSet.Value}-Tr{t.TrackNo:D2}"
                        : $"Tr{t.TrackNo:D2}";
                    // 種別バッジ HTML を組み立て。
                    // 仕様：
                    //  - サイズ（=曲尺、フル/TV size 等）は淡い緑、パート（=歌入り/カラオケ等）は淡い青で
                    //    全件共通色（バリエーション間でランダムに色を散らさない）。
                    //  - 次回予告トラック（content_kind='NEXT'）はサイズバッジを「次回予告」専用の
                    //    青系クラス（recording-tracks-kind-next）に切替、パートは INST 固定で UI 冗長な
                    //    ためバッジ自体を出さない（商品詳細トラックカードの NEXT 表示と揃える）。
                    //  - パートが「VOCAL（歌入り）」のときは「録音物の既定状態」なのでバッジを出さない
                    //    （カラオケ・パート歌入り等の特殊版だけが目印として残るようにする）。
                    //  - サイズコード未設定の行はサイズバッジを出さない。
                    //  - 両方とも出ない場合はセルが空（テンプレ側は空のセルとして描画）。
                    bool isNextTrack = string.Equals(t.ContentKindCode, "NEXT", StringComparison.Ordinal);
                    var badgeHtmlBuilder = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(sizeLabel))
                    {
                        string sizeBadgeClass = isNextTrack
                            ? "recording-tracks-kind-badge recording-tracks-kind-next"
                            : "recording-tracks-kind-badge recording-tracks-kind-size";
                        badgeHtmlBuilder.Append("<span class=\"").Append(sizeBadgeClass).Append("\">")
                                        .Append(HtmlEscape(sizeLabel))
                                        .Append("</span>");
                    }
                    // 「VOCAL」（歌入り）はデフォルト扱いとしてバッジ非表示。NEXT は INST 固定で出さない。
                    bool showPartBadge = !string.IsNullOrEmpty(partLabel)
                        && !string.Equals(t.SongPartVariantCode, "VOCAL", StringComparison.Ordinal)
                        && !isNextTrack;
                    if (showPartBadge)
                    {
                        badgeHtmlBuilder.Append("<span class=\"recording-tracks-kind-badge recording-tracks-kind-part\">")
                                        .Append(HtmlEscape(partLabel))
                                        .Append("</span>");
                    }
                    string kindBadgesHtml = badgeHtmlBuilder.ToString();

                    tracksRows.Add(new RecordingTrackRow
                    {
                        ProductCatalogNo = prod.ProductCatalogNo,
                        ProductTitle = prod.Title,
                        // 表示用は日本語フォーマット、ソート用に DateTime も別途保持する。
                        // ソートは DateTime 原値で行う（日本語フォーマット文字列の
                        // 文字列比較だと「2004年10月」が「2004年2月」より先に並ぶため）。
                        ProductReleaseDate = JpDateFormat.Date(prod.ReleaseDate),
                        ProductReleaseDateShort = releaseShort,
                        ProductReleaseDateRaw = prod.ReleaseDate,
                        DiscCatalogNo = disc.CatalogNo,
                        DiscNoInSet = disc.DiscNoInSet,
                        TrackNo = t.TrackNo,
                        // 商品詳細ページのトラック行アンカー（id="track-{discCatalogNo}-{trackNo}-{subOrder}"）
                        // を生成するために sub_order を保持する。同一 disc+track に複数 song_recordings が
                        // 紐付くケース（同曲のサイズ違いを同一トラック扱いで別行表現する運用）のため、
                        // sub_order を含めることでアンカー先のトラック行を一意に特定できる。
                        SubOrder = t.SubOrder,
                        DiscTrackLabel = discTrackLabel,
                        KindBadgesHtml = kindBadgesHtml,
                        ProductUrl = PathUtil.ProductUrl(prod.ProductCatalogNo),
                        CoverImageUrl = prod.CoverImageUrl ?? ""
                    });
                }
                // ソート基準：発売日（昇順、DateTime 原値）→ 品番（昇順、文字列順）→ Disc 番（昇順）→ Track 番（昇順）。
                tracksRows = tracksRows
                    .OrderBy(x => x.ProductReleaseDateRaw)
                    .ThenBy(x => x.ProductCatalogNo, StringComparer.Ordinal)
                    .ThenBy(x => x.DiscNoInSet ?? 1u)
                    .ThenBy(x => x.TrackNo)
                    .ToList();
            }

            // 本編での使用。楽曲視点では「使われたか」が事実情報なので、
            // episode_theme_songs.usage_actuality の全ケースを拾う：
            //   - 'NORMAL'                    クレジット記載通りに流れた（注記なし）
            //   - 'BROADCAST_NOT_CREDITED'    クレジットなしで流れた（注記「クレジットなし」）
            //   - 'CREDITED_NOT_BROADCAST'    クレジットあって実際は流れていない（注記「実際には不使用」）
            // エピソード詳細の「クレジット」セクションは本編クレジットの忠実な反映であり、
            // BROADCAST_NOT_CREDITED 行を補完して載せない方針なのでこちらだけが拾い得る情報になる。
            // 1 話 1 行ではなく
            // (シリーズ, 区分, BroadcastOnly, UsageActuality) で集約し、連続話番号は範囲表記に縮約する
            // （例：「ふたりはプリキュア 第1〜49話 オープニング主題歌」）。
            var themeRowsForGrouping = new List<(Series Series, Episode Episode, EpisodeThemeSong Theme)>();
            if (themeSongsByRecording.TryGetValue(r.SongRecordingId, out var themes))
            {
                foreach (var th in themes)
                {
                    var ep = LookupEpisode(th.EpisodeId);
                    if (ep is null) continue;
                    if (!_ctx.SeriesById.TryGetValue(ep.SeriesId, out var epSeries)) continue;

                    themeRowsForGrouping.Add((epSeries, ep, th));
                }
            }
            // SERIES スコープ（映画系列）の主題歌・挿入歌は episode を介さずシリーズ直付け。
            // 集約・エピソード範囲化は不要で、各 (series, kind, broadcast_only, actuality) で 1 行ずつ立てる。
            var seriesRowsForGrouping = new List<(Series Series, SeriesThemeSong Theme)>();
            if (seriesThemeSongsByRecording.TryGetValue(r.SongRecordingId, out var seriesThemes))
            {
                foreach (var sth in seriesThemes)
                {
                    if (!_ctx.SeriesById.TryGetValue(sth.SeriesId, out var sSeries)) continue;
                    seriesRowsForGrouping.Add((sSeries, sth));
                }
            }
            // (シリーズ, 区分, broadcast_only, usage_actuality) で集約し、エピソード番号を範囲化。
            // 映画系のシリーズ直付け行も合算してシリーズ放送開始日昇順で並べる。
            var themeRows = BuildThemeUsageRows(themeRowsForGrouping, seriesRowsForGrouping);

            // 歌唱者：song_recording_singers を優先、無ければ SongRecording.SingerName のフリーテキスト。
            // 歌唱者は「歌：」プレフィックスを付けた目立つ表示にするため、HTML（リンク化済み）と
            // フォールバック平文の両方をテンプレに渡す。
            var recordingSingers = singersByRecording.TryGetValue(r.SongRecordingId, out var singerList) ? singerList : new List<SongRecordingSinger>();
            string vocalistsHtml = BuildVocalistsHtml(recordingSingers, r.SingerName, personAliasMap, characterAliasMap);
            string chorusHtml = BuildChorusHtml(recordingSingers, personAliasMap, characterAliasMap);

            // 表示タイトル（variant_label 優先、空なら親曲名）と録音単位の音楽種別ラベル。
            string recDisplayTitle = !string.IsNullOrEmpty(r.VariantLabel) ? r.VariantLabel : song.Title;
            string recMusicClassLabel = (r.MusicClassCode != null && musicClassMap.TryGetValue(r.MusicClassCode, out var recMc))
                ? recMc.NameJa : "";
            // 音楽種別バッジの CSS クラス末尾（"OP" → "op"、"MOVIE_OP" → "movie-op"）。
            // 楽曲索引と同じ .songs-badge-{ここ} に対応する固定 8 色マッピングを参照する。
            string recBadgeClassSuffix = string.IsNullOrEmpty(r.MusicClassCode)
                ? ""
                : r.MusicClassCode.ToLowerInvariant().Replace('_', '-');
            // 録音単位の出典シリーズ（録音モデル直下の SeriesId）。テンプレ表示は
            // 「歌：」と同じ .song-credits / .key-staff-line レイアウトで「出典」バッジ + シリーズ名リンク
            // + 開始年「(2023)」の薄色補助で出す。
            string recSeriesTitle = "";
            string recSeriesLink = "";
            string recSeriesStartYearLabel = "";
            if (r.SeriesId is int rsid && _ctx.SeriesById.TryGetValue(rsid, out var rSeries))
            {
                recSeriesTitle = rSeries.Title;
                recSeriesLink = PathUtil.SeriesUrl(rSeries.Slug);
                recSeriesStartYearLabel = rSeries.StartDate.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            recordingViews.Add(new RecordingView
            {
                SongRecordingId = r.SongRecordingId,
                SingerName = r.SingerName ?? "",
                VariantLabel = r.VariantLabel ?? "",
                DisplayTitle = recDisplayTitle,
                MusicClassLabel = recMusicClassLabel,
                BadgeClassSuffix = recBadgeClassSuffix,
                SeriesTitle = recSeriesTitle,
                SeriesLink = recSeriesLink,
                SeriesStartYearLabel = recSeriesStartYearLabel,
                Notes = r.Notes ?? "",
                VocalistsHtml = vocalistsHtml,
                ChorusHtml = chorusHtml,
                Tracks = tracksRows,
                ThemeUsages = themeRows
            });
        }

        var content = new SongDetailModel
        {
            Song = new SongView
            {
                SongId = song.SongId,
                Title = song.Title,
                TitleKana = song.TitleKana ?? "",
                // 音楽種別・出典シリーズは録音単位で持つため SongView には持たず、RecordingView 側に置く。
                LyricistName = song.LyricistName ?? "",
                ComposerName = song.ComposerName ?? "",
                ArrangerName = song.ArrangerName ?? "",
                LyricsHtml = lyricsHtml,
                CompositionHtml = compositionHtml,
                ArrangementHtml = arrangementHtml,
                LyricsRoleLabelHtml = lyricsRoleLabelHtml,
                CompositionRoleLabelHtml = compositionRoleLabelHtml,
                ArrangementRoleLabelHtml = arrangementRoleLabelHtml,
                Notes = song.Notes ?? ""
            },
            Recordings = recordingViews
        };
        // MetaDescription を実データから動的構築する。
        // 音楽種別・出典シリーズは録音単位のため、説明文には先頭録音の値を代表値として採用する
        // （複数録音で異なるケースは 1 個に絞る）。
        string musicClassLabel = recordingViews.Count > 0 ? recordingViews[0].MusicClassLabel : "";
        string repSeriesTitle = recordingViews.Count > 0 ? recordingViews[0].SeriesTitle : "";
        var metaDescription = BuildSongMetaDescription(
            songTitle: song.Title,
            seriesTitle: repSeriesTitle,
            musicClassLabel: musicClassLabel,
            recordingViews: recordingViews,
            lyricistName: song.LyricistName ?? "",
            composerName: song.ComposerName ?? "");

        // 楽曲詳細の構造化データは Schema.org の MusicComposition 型。
        // 作詞・作曲・編曲は lyricist / composer の Person ノードとして埋め込む（テキストフィールド前提）。
        // description と genre を追加して、リッチスニペットの候補要素を増やす。
        string baseUrl = _ctx.Config.BaseUrl;
        string songUrl = PathUtil.SongUrl(song.SongId);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "MusicComposition",
            ["name"] = song.Title,
            ["description"] = metaDescription,
            ["inLanguage"] = "ja",
            // genre は固定で「アニメソング」を付与。MusicComposition の genre は文字列か MusicGenre のどちらも
            // 受け付ける仕様で、シンプル化の観点から文字列リテラルで運用する。
            ["genre"] = "アニメソング"
        };
        if (!string.IsNullOrEmpty(song.LyricistName))
            jsonLdDict["lyricist"] = new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = song.LyricistName };
        if (!string.IsNullOrEmpty(song.ComposerName))
            jsonLdDict["composer"] = new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = song.ComposerName };
        if (!string.IsNullOrEmpty(baseUrl)) jsonLdDict["url"] = baseUrl + songUrl;
        var jsonLd = JsonLdBuilder.Serialize(jsonLdDict);

        var layout = new LayoutModel
        {
            PageTitle = song.Title,
            MetaDescription = metaDescription,
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "歴代プリキュアソング(歌)", Url = "/songs/" },
                new BreadcrumbItem { Label = song.Title, Url = "" }
            },
            OgType = "music.song",
            JsonLd = jsonLd
        };
        _page.RenderAndWrite(songUrl, "songs", "songs-detail.sbn", content, layout);
    }

    /// <summary>
    /// 楽曲詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる。
    /// 構成：「『{シリーズ}』の{楽曲種別}「{曲名}」。歌唱:{歌手}。作詞:{X}、作曲:{Y}。」を骨格に、
    /// 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。
    /// 歌手名は <see cref="RecordingView.SingerName"/> から最大 2 名（先頭録音バージョン優先）。
    /// シリーズタイトルが空のときは「プリキュアシリーズの{楽曲種別}…」にフォールバック。
    /// </summary>
    private static string BuildSongMetaDescription(
        string songTitle,
        string seriesTitle,
        string musicClassLabel,
        IReadOnlyList<RecordingView> recordingViews,
        string lyricistName,
        string composerName)
    {
        const int targetMaxChars = 140;
        var sb = new System.Text.StringBuilder();

        // ① 基本：(『シリーズ』の|プリキュアシリーズの)(楽曲種別「曲名」|「曲名」)。
        if (!string.IsNullOrWhiteSpace(seriesTitle))
        {
            sb.Append('『').Append(seriesTitle).Append("』の");
        }
        else
        {
            sb.Append("プリキュアシリーズの");
        }
        if (!string.IsNullOrWhiteSpace(musicClassLabel))
        {
            sb.Append(musicClassLabel);
        }
        sb.Append('「').Append(songTitle).Append("」。");

        // ② 歌唱者（最大 2 名）。録音バージョン横断で重複を排除しつつ先頭から拾う。
        // SingerName が空の録音はスキップ。「、」連結された複数名のフリーテキストはそのまま単一トークン扱い。
        var singers = recordingViews
            .Select(r => r.SingerName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        if (singers.Count > 0)
        {
            var singersFragment = "歌唱:" + string.Join("、", singers) + "。";
            if (sb.Length + singersFragment.Length <= targetMaxChars)
                sb.Append(singersFragment);
        }

        // ③ 作詞・作曲（あれば「作詞:{X}、作曲:{Y}。」、片方だけなら片方だけ）。
        var creditParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(lyricistName)) creditParts.Add($"作詞:{lyricistName}");
        if (!string.IsNullOrWhiteSpace(composerName)) creditParts.Add($"作曲:{composerName}");
        if (creditParts.Count > 0)
        {
            var creditsFragment = string.Join("、", creditParts) + "。";
            if (sb.Length + creditsFragment.Length <= targetMaxChars)
                sb.Append(creditsFragment);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 指定役職コードを「役職名（リンク付き）」の HTML に整形する。
    /// roles マスタに行があれば <see cref="Role.NameJa"/> を <see cref="RoleSuccessorResolver"/>
    /// 経由で求めた系譜代表 role_code を URL に使って /stats/roles/{rep}/ にリンク化する。
    /// マスタに無い、または NameJa が空のときは <paramref name="fallbackLabel"/> をリンクなしバッジ風 span で返す
    /// （/songs/{song_id}/ の基本情報セクションと録音セクションで .key-staff-line レイアウトに直接流し込むため、
    /// 常に <c>.role-badge.role-badge-sm</c> クラスと <c>data-role-code</c> 属性を付けた要素を返す）。
    /// </summary>
    private string BuildRoleLabelLinkHtml(string roleCode, IReadOnlyDictionary<string, Role> roleMap, string fallbackLabel)
    {
        if (roleMap.TryGetValue(roleCode, out var role) && !string.IsNullOrEmpty(role.NameJa))
        {
            string rep = _roleSuccessorResolver.GetRepresentative(roleCode);
            string href = PathUtil.RoleStatsUrl(string.IsNullOrEmpty(rep) ? roleCode : rep);
            return $"<a class=\"role-badge role-badge-sm\" data-role-code=\"{HtmlEscape(roleCode)}\" href=\"{HtmlEscape(href)}\">{HtmlEscape(role.NameJa)}</a>";
        }
        return $"<span class=\"role-badge role-badge-sm\" data-role-code=\"{HtmlEscape(roleCode)}\">{HtmlEscape(fallbackLabel)}</span>";
    }

    /// <summary>
    /// 録音 1 件分の「作詞・作曲・編曲・歌」を、役職バッジ + 名義テキストで構成する 1 行 HTML として
    /// 組み立てる（楽曲索引のカード用）。
    /// <para>仕様：
    ///  - 出力ルートは <c>&lt;div class="staff-badges-row"&gt;</c>（エピソード一覧スタッフ行と同型）。
    ///  - 各「グループ」は <c>&lt;span class="staff-badge-group"&gt;</c>。バッジ 1 個以上のあとに名義テキスト。
    ///  - 作詞→作曲→編曲の順で各役職の名義テキストを確定する。
    ///    構造化クレジット行があれば <see cref="SongCredit.PrecedingSeparator"/> を挟んで名義を順次連結した
    ///    プレーンテキスト（HTML エスケープ済み）、無ければフォールバックのフリーテキスト（同じくエスケープ）、
    ///    どちらも空なら出力しない。
    ///  - 「単独名義」（連名でない、すなわち alias 1 個または平文に区切り記号無し）の役職が連続して
    ///    完全一致するなら、ひとつのグループにバッジを連ねる形で併合する。
    ///    例：作詞=A・作曲=A・編曲=A → [作詞][作曲][編曲] A、
    ///        作詞=A・作曲=B・編曲=B → [作詞] A、[作曲][編曲] B。
    ///  - 歌は VOCALS のグループとして末尾に独立して出す（併合対象外）。
    ///  - 名義はリンク化せずプレーンテキスト化する（カード全体が <c>/songs/{id}/</c> リンクのため
    ///    内部に入れ子の <c>&lt;a&gt;</c> を持たないポリシー）。</para>
    /// </summary>
    private string BuildCreditMetaHtml(
        IReadOnlyList<SongCredit> songCreditRows,
        string? lyricistFallback,
        string? composerFallback,
        string? arrangerFallback,
        IReadOnlyList<SongRecordingSinger> singers,
        string? singerFallback,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        // 各役職の名義を HTML（構造化エントリは <a class="staff-name">、フリーテキストは
        // <span class="staff-name"> で wrap）として解決する。カード全体がオーバーレイ <a>
        // でクリッカブルなので、ここで生成された <a> は pointer-events: auto で個別クリックを
        // 拾うパターン（series-card-sub / bgms-card と同じ仕組み）。
        var lyrics = ResolveSongCreditHtml(songCreditRows, SongCreditRoles.Lyrics, lyricistFallback, personAliasMap);
        var composition = ResolveSongCreditHtml(songCreditRows, SongCreditRoles.Composition, composerFallback, personAliasMap);
        var arrangement = ResolveSongCreditHtml(songCreditRows, SongCreditRoles.Arrangement, arrangerFallback, personAliasMap);

        var groups = new List<(List<(string Code, string Label)> Badges, string NameHtml, bool IsSingle)>();

        void AddOrMerge(string roleCode, string label, (string Html, bool IsSingle) entry)
        {
            if (string.IsNullOrEmpty(entry.Html)) return;
            if (groups.Count > 0
                && entry.IsSingle
                && groups[^1].IsSingle
                && string.Equals(groups[^1].NameHtml, entry.Html, StringComparison.Ordinal))
            {
                groups[^1].Badges.Add((roleCode, label));
            }
            else
            {
                groups.Add((new List<(string, string)> { (roleCode, label) }, entry.Html, entry.IsSingle));
            }
        }
        AddOrMerge(SongCreditRoles.Lyrics, "作詞", lyrics);
        AddOrMerge(SongCreditRoles.Composition, "作曲", composition);
        AddOrMerge(SongCreditRoles.Arrangement, "編曲", arrangement);

        // 歌は VOCALS グループとして末尾に独立追加。BuildVocalistsHtml は構造化 singers から
        // 人物・キャラへの <a> リンクを含む HTML を返す。VOCALS 行が無いフォールバック単独時は
        // HtmlEscape(singerName) だけが返るので、その場合は <span class="staff-name"> でラップする。
        string vocalistsHtml = BuildVocalistsHtml(singers, singerFallback, personAliasMap, characterAliasMap);
        if (!string.IsNullOrEmpty(vocalistsHtml))
        {
            bool vocalsIsStructured = singers.Any(s => string.Equals(s.RoleCode, SongRecordingSingerRoles.Vocals, StringComparison.Ordinal));
            string vocalistsBlock = vocalsIsStructured
                ? vocalistsHtml
                : $"<span class=\"staff-name\">{vocalistsHtml}</span>";
            groups.Add((new List<(string, string)> { ("VOCALS", "歌") }, vocalistsBlock, false));
        }

        // コーラス（BACKING_VOCALS）は同じ青系バッジで末尾に独立追加。常に構造化 singers 経由のため、
        // フリーテキストフォールバックは持たない（行が無ければ何も出さない）。
        string chorusHtml = BuildChorusHtml(singers, personAliasMap, characterAliasMap);
        if (!string.IsNullOrEmpty(chorusHtml))
        {
            groups.Add((new List<(string, string)> { ("BACKING_VOCALS", "コーラス") }, chorusHtml, false));
        }

        if (groups.Count == 0) return "";

        // エピソード一覧スタッフ行と同型の構造で組み立てる。
        // 役職バッジは <a> リンク（/creators/roles/{code}/）にして、個別クリック可能にする。
        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"staff-badges-row\">");
        foreach (var g in groups)
        {
            sb.Append("<span class=\"staff-badge-group\">");
            foreach (var (code, label) in g.Badges)
            {
                sb.Append("<a class=\"role-badge role-badge-sm\" data-role-code=\"")
                  .Append(HtmlEscape(code))
                  .Append("\" href=\"")
                  .Append(HtmlEscape(PathUtil.RoleStatsUrl(code)))
                  .Append("\">")
                  .Append(HtmlEscape(label))
                  .Append("</a>");
            }
            sb.Append(g.NameHtml);
            sb.Append("</span>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// 指定役職の <see cref="SongCredit"/> 行を HTML に解決する。
    /// 構造化行があれば <see cref="StaffNameLinkResolver.ResolveAsHtml"/> 経由で <c>/persons/{id}/</c>
    /// への <c>&lt;a&gt;</c> リンクを生成し、<c>PrecedingSeparator</c> を挟んで連結する。
    /// 構造化行が無くフリーテキストのみのときはリンク化せず素のテキストを返す。
    /// 全体を <c>&lt;span class="staff-name"&gt;</c> でラップして、 CSS の <c>.staff-name</c> スタイル
    /// （色／余白）を適用しつつ、内部の <c>&lt;a&gt;</c> がカード overlay リンクより上位で
    /// 個別クリックを拾えるようにする。
    /// 単独判定（複数 alias を含まないか）は呼び出し側の併合ロジックで利用する。
    /// </summary>
    private (string Html, bool IsSingle) ResolveSongCreditHtml(
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
            if (string.IsNullOrEmpty(fallbackText)) return ("", false);
            bool single = !ContainsSeparator(fallbackText);
            return ($"<span class=\"staff-name\">{HtmlEscape(fallbackText)}</span>", single);
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("<span class=\"staff-name\">");
        for (int i = 0; i < roleRows.Count; i++)
        {
            var row = roleRows[i];
            if (i > 0) sb.Append(HtmlEscape(row.PrecedingSeparator ?? ""));
            string displayName = personAliasMap.TryGetValue(row.PersonAliasId, out var alias)
                ? alias.GetDisplayName()
                : "[alias#" + row.PersonAliasId + "]";
            sb.Append(_staffLinkResolver.ResolveAsHtml(row.PersonAliasId, displayName));
        }
        sb.Append("</span>");
        return (sb.ToString(), roleRows.Count == 1);
    }

    /// <summary>区切り記号（連名を示すもの）を含むか判定する。</summary>
    private static bool ContainsSeparator(string text) =>
        text.Contains('／') || text.Contains('・') || text.Contains('、') || text.Contains(',') || text.Contains('/');

    /// <summary>
    /// 指定役職の構造化クレジット行を HTML 化する。
    /// 仕様：
    /// <list type="bullet">
    ///   <item>該当役の <see cref="SongCredit"/> 行が 1 件以上あれば、<see cref="StaffNameLinkResolver"/>
    ///     で名義を /persons/{id}/ にリンク化し、<see cref="SongCredit.PrecedingSeparator"/>
    ///     で連結した文字列を返す。役職名は表示しない（テンプレ側の th セルが
    ///     「作詞」「作曲」「編曲」を持つため）。</item>
    ///   <item>行が 1 件も無く、フリーテキストフォールバック（<paramref name="fallbackText"/>）が
    ///     非空のときは、HTML エスケープしただけの平文を返す。</item>
    ///   <item>どちらも無いときは空文字を返す（テンプレ側で空判定して行を出さない）。</item>
    /// </list>
    /// </summary>
    private string BuildCreditRoleHtml(
        IReadOnlyList<SongCredit> rows,
        string roleCode,
        string? fallbackText,
        IReadOnlyDictionary<string, Role> roleMap,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap)
    {
        var roleRows = rows
            .Where(r => string.Equals(r.CreditRole, roleCode, StringComparison.Ordinal))
            .OrderBy(r => r.CreditSeq)
            .ToList();

        // 構造化行が無ければフォールバック平文（HTML エスケープのみ）。
        if (roleRows.Count == 0)
        {
            return string.IsNullOrEmpty(fallbackText) ? "" : HtmlEscape(fallbackText);
        }

        // 各 seq 行を「PrecedingSeparator + 名義リンク」の形で連結。
        // seq=1 は PrecedingSeparator が NULL なので素直に先頭に置く。
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < roleRows.Count; i++)
        {
            var row = roleRows[i];
            if (i > 0)
            {
                // 区切り文字も HTML エスケープしてから出力する。
                sb.Append(HtmlEscape(row.PrecedingSeparator ?? ""));
            }
            if (personAliasMap.TryGetValue(row.PersonAliasId, out var alias))
            {
                string display = alias.GetDisplayName();
                sb.Append(_staffLinkResolver.ResolveAsHtml(row.PersonAliasId, display));
            }
            else
            {
                // alias が見つからない（FK 整合性想定外）：ID を平文で出す。
                sb.Append("[alias#").Append(row.PersonAliasId).Append("]");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 録音の歌唱者群（<see cref="SongRecordingSinger"/>）を HTML 化する。
    /// 仕様：
    /// <list type="bullet">
    ///   <item>VOCALS 役の行を <see cref="SongRecordingSinger.SingerSeq"/> 順に並べ、
    ///     PERSON 名義は /persons/{id}/、CHARACTER_WITH_CV 名義はキャラ /characters/{id}/ ＋
    ///     CV 名義 /persons/{id}/ で構成する「キャラ名(CV:声優)」形式で出す。</item>
    ///   <item>スラッシュ並列（<see cref="SongRecordingSinger.SlashCharacterAliasId"/> 等）は
    ///     主名義側と同じ書式で「/」連結して出す。</item>
    ///   <item><see cref="SongRecordingSinger.AffiliationText"/> が非空なら末尾に半角スペース＋テキスト平文で添える。</item>
    ///   <item>行が 1 件も無ければフォールバックとして <see cref="SongRecording.SingerName"/> の HTML エスケープ平文を返す。</item>
    /// </list>
    /// </summary>
    private string BuildVocalistsHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string? fallbackSingerName,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        string html = BuildSingersByRoleHtml(singers, SongRecordingSingerRoles.Vocals, personAliasMap, characterAliasMap);
        if (!string.IsNullOrEmpty(html)) return html;
        return string.IsNullOrEmpty(fallbackSingerName) ? "" : HtmlEscape(fallbackSingerName);
    }

    /// <summary>BACKING_VOCALS（コーラス）役の歌唱者群を HTML 化する。 BACKING_VOCALS 行が無ければ空文字列を返す（VOCALS と違いフリーテキストのフォールバックは無い）。</summary>
    private string BuildChorusHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
        => BuildSingersByRoleHtml(singers, SongRecordingSingerRoles.Chorus, personAliasMap, characterAliasMap);

    /// <summary>指定 <paramref name="roleCode"/>（VOCALS / BACKING_VOCALS 等）の歌唱者行のみを抽出して HTML 化する内部ヘルパ。</summary>
    private string BuildSingersByRoleHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string roleCode,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        var rows = singers
            .Where(s => string.Equals(s.RoleCode, roleCode, StringComparison.Ordinal))
            .OrderBy(s => s.SingerSeq)
            .ToList();
        if (rows.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            var s = rows[i];
            if (i > 0)
            {
                sb.Append(HtmlEscape(s.PrecedingSeparator ?? ""));
            }
            sb.Append(RenderSingerEntry(s, personAliasMap, characterAliasMap));
            if (!string.IsNullOrEmpty(s.AffiliationText))
            {
                sb.Append(' ').Append(HtmlEscape(s.AffiliationText));
            }
        }
        return sb.ToString();
    }

    /// <summary>1 つの歌唱者行（主名義 + 任意でスラッシュ並列の相方）を HTML に整形する。</summary>
    private string RenderSingerEntry(
        SongRecordingSinger s,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        if (s.BillingKind == SingerBillingKind.Person)
        {
            // PERSON：主名義 + （あれば）スラッシュ並列の相方。両方とも person_alias。
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
            // CHARACTER_WITH_CV：「キャラ(CV:声優)」、相方ありなら「キャラ/相方キャラ(CV:声優)」。
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
        // キャラ詳細ページへの単一リンク。複数キャラを束ねる仕組（StaffNameLinkResolver 相当）は
        // character_aliases が CharacterId を直接持つため不要。
        // CharacterAlias は PersonAlias と違い DisplayTextOverride / GetDisplayName() を持たない
        // （表記揺れごとに別 alias 行を並存させる運用のため、表示テキストは常に Name そのもの）。
        return $"<a href=\"/characters/{alias.CharacterId}/\">{HtmlEscape(alias.Name)}</a>";
    }

    /// <summary>
    /// 主題歌使用エピソード行群を、(シリーズ × 区分 × 本放送限定フラグ × 使用実態)
    /// で集約し、エピソード番号を連続区間に圧縮した <see cref="RecordingThemeRow"/> 群を返す。
    /// 例：1, 2, 3, 5, 6, 7 → 「第1〜3, 5〜7話」、1 のみ → 「第1話」、1 と 3 → 「第1, 3話」。
    /// 区分ラベルは「オープニング主題歌」「エンディング主題歌」「挿入歌」のように展開する
    /// （旧コード丸出しの "OP"/"ED"/"INSERT" 表示を改善）。
    /// </summary>
    private static IReadOnlyList<RecordingThemeRow> BuildThemeUsageRows(
        IReadOnlyList<(Series Series, Episode Episode, EpisodeThemeSong Theme)> episodeSource,
        IReadOnlyList<(Series Series, SeriesThemeSong Theme)> seriesSource)
    {
        var result = new List<RecordingThemeRow>();

        // ── EPISODE 紐付け（TV 系）：(シリーズ, 区分, broadcast_only, usage_actuality) で集約しエピソード範囲化 ──
        var episodeGroups = episodeSource
            .GroupBy(x => (
                x.Series.SeriesId,
                x.Theme.ThemeKind,
                x.Theme.IsBroadcastOnly,
                x.Theme.UsageActuality
            ));

        foreach (var g in episodeGroups)
        {
            // グループ内の最初の Series 情報をそのまま採用（GroupBy キーで同一保証あり）。
            var any = g.First();
            var epNos = g.Select(x => (int)x.Episode.SeriesEpNo).Distinct().OrderBy(n => n).ToList();
            string rangeLabel = CompressEpisodeNumbers(epNos);

            result.Add(new RecordingThemeRow
            {
                SeriesTitle = any.Series.Title,
                SeriesSlug = any.Series.Slug,
                SeriesStartSerial = any.Series.StartDate.DayNumber,
                SortStartEpNo = epNos.Count > 0 ? epNos[0] : 0,
                EpisodeRangeLabel = rangeLabel,
                ThemeKindLabel = BuildThemeKindLabel(any.Theme.ThemeKind, any.Theme.IsBroadcastOnly, any.Theme.UsageActuality),
            });
        }

        // ── SERIES 紐付け（映画系列）：エピソード集約は不要、各 SeriesThemeSong 行から 1 行ずつ立てる ──
        // 同一 (series, kind, broadcast_only, actuality) で seq が複数あっても 1 行にまとめる
        // （楽曲詳細の本編使用セクションでは劇中順 seq まで区別する必要はないため）。
        var seriesGroups = seriesSource
            .GroupBy(x => (
                x.Series.SeriesId,
                x.Theme.ThemeKind,
                x.Theme.IsBroadcastOnly,
                x.Theme.UsageActuality
            ));

        foreach (var g in seriesGroups)
        {
            var any = g.First();
            result.Add(new RecordingThemeRow
            {
                SeriesTitle = any.Series.Title,
                SeriesSlug = any.Series.Slug,
                SeriesStartSerial = any.Series.StartDate.DayNumber,
                SortStartEpNo = 0,
                EpisodeRangeLabel = "",
                ThemeKindLabel = BuildThemeKindLabel(any.Theme.ThemeKind, any.Theme.IsBroadcastOnly, any.Theme.UsageActuality),
            });
        }

        // ソート：シリーズ放送開始日昇順 → グループの最初の話番号 昇順 → 区分名 昇順。
        // TV 系・映画系を 1 リストにマージしたうえで時系列に並べる（シリーズ間の前後関係を維持）。
        return result
            .OrderBy(x => x.SeriesStartSerial)
            .ThenBy(x => x.SortStartEpNo)
            .ThenBy(x => x.ThemeKindLabel, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>主題歌区分コード（OP/ED/INSERT）と注記フラグから表示ラベルを組み立てる。 EPISODE 紐付け / SERIES 紐付け両方で同形式を共有する。</summary>
    private static string BuildThemeKindLabel(string themeKind, bool isBroadcastOnly, string usageActuality)
    {
        string kindLabel = themeKind switch
        {
            "OP" => "オープニング主題歌",
            "ED" => "エンディング主題歌",
            "INSERT" => "挿入歌",
            _ => themeKind
        };
        if (isBroadcastOnly) kindLabel += "（本放送のみ）";
        if (string.Equals(usageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
            kindLabel += "（実際には不使用）";
        else if (string.Equals(usageActuality, EpisodeThemeSongUsageActualities.BroadcastNotCredited, StringComparison.Ordinal))
            kindLabel += "（クレジットなし）";
        return kindLabel;
    }

    /// <summary>整数リスト（昇順、重複なし前提）を連続区間に圧縮して「第1〜3, 5〜7話」のような表記を返す。 単独要素は「第1話」、単一連続は「第1〜49話」、複数区間は「第1〜3, 5〜7話」のように整形する。 空リストは空文字を返す。</summary>
    private static string CompressEpisodeNumbers(IReadOnlyList<int> sortedDistinctNos)
    {
        if (sortedDistinctNos.Count == 0) return "";

        // 連続区間を [start, end] 群に分解する。
        var ranges = new List<(int Start, int End)>();
        int rangeStart = sortedDistinctNos[0];
        int prev = rangeStart;
        for (int i = 1; i < sortedDistinctNos.Count; i++)
        {
            int n = sortedDistinctNos[i];
            if (n == prev + 1)
            {
                prev = n;
                continue;
            }
            ranges.Add((rangeStart, prev));
            rangeStart = n;
            prev = n;
        }
        ranges.Add((rangeStart, prev));

        var sb = new System.Text.StringBuilder();
        sb.Append("第");
        for (int i = 0; i < ranges.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var (s, e) = ranges[i];
            if (s == e) sb.Append(s);
            else sb.Append(s).Append('〜').Append(e);
        }
        sb.Append("話");
        return sb.ToString();
    }

    /// <summary>HTML 5 における &amp;・&lt;・&gt;・&quot;・&#39; の最小限のエスケープ。</summary>
    private static string HtmlEscape(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&#39;");

    private Episode? LookupEpisode(int episodeId)
    {
        foreach (var (_, eps) in _ctx.EpisodesBySeries)
            for (int i = 0; i < eps.Count; i++)
                if (eps[i].EpisodeId == episodeId) return eps[i];
        return null;
    }

    // ─── テンプレ用 DTO 群 ───

    /// <summary>/songs/ 楽曲索引のテンプレモデル（シリーズ別フラット表示）。</summary>
    private sealed class SongsIndexModel
    {
        /// <summary>シリーズ別セクション群（series.start_date 昇順、その他は末尾固定）。</summary>
        public IReadOnlyList<SongSeriesSection> SeriesSections { get; set; } = Array.Empty<SongSeriesSection>();
        /// <summary>総 recording 件数。</summary>
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// 楽曲索引のセクション（シリーズ単位、「初出盤シリーズ」で分類）。
    /// メンバーは録音バリエーション一覧（song_recording_id 昇順）。
    /// </summary>
    private sealed class SongSeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        /// <summary>シリーズページへのリンクに使う slug。「その他」セクションでは空文字。</summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。「その他」セクションでは空文字。 series.title_short は使わず、シリーズタイトルの隣に薄色の括弧で年を添える表現に統一。 また data-section-nav-year 属性経由でサイドナビにも流す。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>並び替え用のシリーズ開始日（テンプレ側からは参照しない）。</summary>
        public DateOnly SortKey { get; set; }
        /// <summary>並び替えタイブレーク用の SeriesId（テンプレ側からは参照しない）。</summary>
        public int SortSeriesId { get; set; }
        /// <summary>セクション内の録音バリエーション一覧（song_recording_id 昇順）。</summary>
        public IReadOnlyList<SongRecordingIndexRow> Recordings { get; set; } = Array.Empty<SongRecordingIndexRow>();
    }

    /// <summary>
    /// 楽曲索引の 1 行 = 1 録音バリエーション。
    /// 行タイトルは <c>DisplayTitle</c>（variant_label 優先、空なら song.Title）で統一し、
    /// 親曲名のサブ表示は持たない。
    /// </summary>
    private sealed class SongRecordingIndexRow
    {
        public int SongRecordingId { get; set; }
        public int SongId { get; set; }
        /// <summary>表示タイトル。variant_label を優先、空なら親曲タイトル。リンク先は /songs/{SongId}/。</summary>
        public string DisplayTitle { get; set; } = "";
        /// <summary>音楽種別ラベル（録音単位の music_class_code 由来。バッジ表記）。</summary>
        public string MusicClassLabel { get; set; } = "";
        /// <summary>バッジ用クラス名末尾（"op" / "ed" / "movie-op" 等、CSS の .songs-badge-{ここ} に対応）。 music_class_code 未設定時は空文字。</summary>
        public string BadgeClassSuffix { get; set; } = "";
        /// <summary>役職バッジ群と名義テキストで構成される 1 行 HTML（カード全体が /songs/ リンクのため内部リンクは持たない）。</summary>
        public string CreditMetaHtml { get; set; } = "";
    }

    private sealed class SongDetailModel
    {
        public SongView Song { get; set; } = new();
        public IReadOnlyList<RecordingView> Recordings { get; set; } = Array.Empty<RecordingView>();
    }

    private sealed class SongView
    {
        public int SongId { get; set; }
        public string Title { get; set; } = "";
        public string TitleKana { get; set; } = "";
        // 音楽種別・出典シリーズは録音単位で持つため SongView には持たない。
        // 録音セクションの <see cref="RecordingView.MusicClassLabel"/> および
        // <see cref="RecordingView.SeriesTitle"/> / <see cref="RecordingView.SeriesLink"/> を参照する。
        /// <summary>作詞のフリーテキスト（<c>songs.lyricist_name</c>、フォールバック用）。 構造化クレジット （<see cref="LyricsHtml"/>）が優先表示されるため、本フィールドは構造化が無い曲の フォールバック表示でだけ参照される（実際の処理は Generator 側で済ませ、 テンプレ側は <see cref="LyricsHtml"/> をそのまま使う）。</summary>
        public string LyricistName { get; set; } = "";
        public string ComposerName { get; set; } = "";
        public string ArrangerName { get; set; } = "";
        /// <summary>作詞の表示用 HTML。</summary>
        public string LyricsHtml { get; set; } = "";
        /// <summary>作曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string CompositionHtml { get; set; } = "";
        /// <summary>編曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string ArrangementHtml { get; set; } = "";
        /// <summary>「作詞」役職ラベル HTML。 roles マスタ参照 + RoleSuccessorResolver による系譜代表解決を経て、 /stats/roles/{rep}/ へのアンカーになる。マスタ未登録時は素のフォールバック文字列。</summary>
        public string LyricsRoleLabelHtml { get; set; } = "";
        /// <summary>「作曲」役職ラベル HTML（仕様は <see cref="LyricsRoleLabelHtml"/> と同様）。</summary>
        public string CompositionRoleLabelHtml { get; set; } = "";
        /// <summary>「編曲」役職ラベル HTML（仕様は <see cref="LyricsRoleLabelHtml"/> と同様）。</summary>
        public string ArrangementRoleLabelHtml { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class RecordingView
    {
        public int SongRecordingId { get; set; }
        /// <summary>歌唱者のフリーテキスト（<c>song_recordings.singer_name</c>、フォールバック用）。</summary>
        public string SingerName { get; set; } = "";
        public string VariantLabel { get; set; } = "";
        /// <summary>
        /// 録音セクション見出しに使う表示タイトル。
        /// variant_label を優先、空なら親曲タイトル。
        /// 索引と詳細で「親曲を別表示しない」方針に整合させる。
        /// </summary>
        public string DisplayTitle { get; set; } = "";
        /// <summary>
        /// 録音単位の音楽種別ラベル。
        /// 同一曲の TV size / フルサイズ / カバーで種別が変わるケースに対応する。
        /// </summary>
        public string MusicClassLabel { get; set; } = "";
        /// <summary>音楽種別バッジの CSS クラス末尾（"op" / "ed" / "movie-op" 等、楽曲索引と共通の .songs-badge-{ここ} に対応）。 music_class_code 未設定時は空文字。</summary>
        public string BadgeClassSuffix { get; set; } = "";
        /// <summary>録音単位の出典シリーズ名（テンプレ表示用、空文字なら出典行は出さない）。</summary>
        public string SeriesTitle { get; set; } = "";
        /// <summary>録音単位の出典シリーズへのリンク URL（テンプレ表示用、空文字なら出典行は出さない）。</summary>
        public string SeriesLink { get; set; } = "";
        /// <summary>録音単位の出典シリーズの開始年（西暦 4 桁）。シリーズ名の隣に「(2023)」のように薄色で添える補助表示用。 シリーズ未解決時は空文字。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>歌唱者の表示用 HTML。</summary>
        public string VocalistsHtml { get; set; } = "";
        /// <summary>コーラス（BACKING_VOCALS）の表示用 HTML。 該当録音にコーラス歌唱者が居なければ空文字列。空でなければ songs-detail で「コーラス」バッジ + 名義を 1 行表示する。</summary>
        public string ChorusHtml { get; set; } = "";
        public IReadOnlyList<RecordingTrackRow> Tracks { get; set; } = Array.Empty<RecordingTrackRow>();
        public IReadOnlyList<RecordingThemeRow> ThemeUsages { get; set; } = Array.Empty<RecordingThemeRow>();
    }

    private sealed class RecordingTrackRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string ProductTitle { get; set; } = "";
        /// <summary>発売日（テンプレ表示用、日本語フォーマット文字列「2004年2月18日」）。 楽曲詳細の収録商品表ではセル 2 行目に短縮形（<see cref="ProductReleaseDateShort"/>）を使い、 こちらは将来の代替表示用に残す。</summary>
        public string ProductReleaseDate { get; set; } = "";
        /// <summary>発売日の短縮形（"2024.2.4" 形式）。 楽曲詳細の収録商品表で商品セル 2 行目に DiscCatalogNo と並べて表示する。</summary>
        public string ProductReleaseDateShort { get; set; } = "";
        /// <summary>発売日の DateTime 原値。 ソートキーは数値で持つ（日本語フォーマット済み文字列だと "2004年10月" が "2004年2月" より先に並ぶ lex 比較になるのを避けるため）。</summary>
        public DateTime ProductReleaseDateRaw { get; set; }
        public string ProductUrl { get; set; } = "";
        public string DiscCatalogNo { get; set; } = "";
        public uint? DiscNoInSet { get; set; }
        public byte TrackNo { get; set; }
        /// <summary>同一 disc+track に複数 song_recordings が紐付く場合の枝番（<c>tracks.sub_order</c> 由来、既定 0）。 商品詳細ページのトラック行アンカー <c>id="track-{discCatalogNo}-{trackNo}-{subOrder}"</c> を組み立てるため保持する。 sub_order が 0 のトラックでも一律「-0」付きの安定形式で出力する（products-detail.sbn 側と取り決めを揃える）。</summary>
        public byte SubOrder { get; set; }
        /// <summary>Disc/Track の簡略表記（"Tr01" もしくは "Disc3-Tr23"）。 単一 disc 商品（DiscNoInSet 未設定）は「Tr{NN}」、複数枚組（DiscNoInSet あり）は 「Disc{N}-Tr{NN}」。Track 番は 2 桁ゼロパディング。</summary>
        public string DiscTrackLabel { get; set; } = "";
        /// <summary>
        /// 種別バッジ HTML。サイズ（曲尺）とパート（歌入り/カラオケ等）を 1 セルに統合してバッジ並びで表示する。
        /// 仕様：
        /// <list type="bullet">
        ///   <item>サイズは <c>.recording-tracks-kind-badge.recording-tracks-kind-size</c>（淡い緑）。</item>
        ///   <item>パートは <c>.recording-tracks-kind-badge.recording-tracks-kind-part</c>（淡い青）。</item>
        ///   <item>パート <c>VOCAL</c>（歌入り）は録音物の既定状態としてバッジを出さない
        ///     （カラオケ・パート歌入り等の特殊版だけが目印として残る）。</item>
        ///   <item>両方とも出ない行は空文字（セルが空のまま描画される）。</item>
        /// </list>
        /// </summary>
        public string KindBadgesHtml { get; set; } = "";
        /// <summary>収録商品のジャケット画像 URL（Amazon CDN ホットリンク）。空ならカード左端のサムネ枠はグレーのプレースホルダ表示にする。</summary>
        public string CoverImageUrl { get; set; } = "";
    }

    /// <summary>主題歌使用 1 行。EPISODE 紐付け（TV 系）はシリーズ × 区分 × broadcast_only × usage_actuality 単位で集約してエピソード番号を範囲圧縮、 SERIES 紐付け（映画系列）は (シリーズ, 区分, broadcast_only, usage_actuality) 単位で 1 行ずつ立てて EpisodeRangeLabel は空のまま運用する。</summary>
    private sealed class RecordingThemeRow
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        /// <summary>シリーズ放送開始日のシリアル値（series 順ソート用）。テンプレからは参照しない。</summary>
        public int SeriesStartSerial { get; set; }
        /// <summary>エピソード番号の集約表示（例：「第1〜49話」「第3, 5, 7話」「第10〜12, 14話」）。 SERIES 紐付け行（映画系列）では空文字。</summary>
        public string EpisodeRangeLabel { get; set; } = "";
        /// <summary>グループ内の最小エピソード番号（ソート専用、SERIES 紐付け行は 0）。テンプレからは参照しない。</summary>
        public int SortStartEpNo { get; set; }
        /// <summary>区分ラベル（「オープニング主題歌」「エンディング主題歌」「挿入歌」、 必要に応じて「（本放送のみ）」「（実際には不使用）」が追記される）。</summary>
        public string ThemeKindLabel { get; set; } = "";
    }
}
