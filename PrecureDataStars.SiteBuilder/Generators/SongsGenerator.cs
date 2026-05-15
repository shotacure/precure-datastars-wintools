using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 楽曲索引（<c>/songs/</c>）と楽曲詳細（<c>/songs/{song_id}/</c>）の生成（v1.3.0 タスク追加）。
/// <para>
/// 楽曲詳細ページは下記の情報を 1 ページに集約：
/// </para>
/// <list type="bullet">
///   <item><description>歌の基本情報（タイトル / 読み / 作詞・作曲・編曲 / 種別 / 出自シリーズ）</description></item>
///   <item><description>録音バージョン一覧（<c>song_recordings</c>、歌唱者違い・バリアント違い）</description></item>
///   <item><description>各録音バージョンの収録トラック・商品（tracks 経由で逆引き）</description></item>
///   <item><description>主題歌としての使用エピソード（<c>episode_theme_songs</c> 経由で逆引き）</description></item>
/// </list>
/// </summary>
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
    private readonly SongMusicClassesRepository _musicClassesRepo;
    private readonly SongSizeVariantsRepository _songSizeVariantsRepo;
    private readonly SongPartVariantsRepository _songPartVariantsRepo;
    // v1.3.0 公開直前のデザイン整理 第 N 弾：楽曲詳細ページで作詞・作曲・編曲・歌唱者の
    // 構造化クレジット（song_credits / song_recording_singers）を読み、役職名・名義名を
    // ともにリンク化して表示するために追加した依存。
    // 旧仕様は Song.LyricistName 等のフリーテキストのみを参照していたが、
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
        var musicClasses = (await _musicClassesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var sizeVariants = (await _songSizeVariantsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var partVariants = (await _songPartVariantsRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();

        // v1.3.0 公開直前のデザイン整理 第 N 弾：作詞・作曲・編曲・歌唱者の構造化マスタを起動時に
        // 一括ロードする。曲数分のクエリを避け、メモリ上で song_id / song_recording_id 毎にグルーピングする。
        var allRoles = (await _rolesRepo.GetAllAsync(ct).ConfigureAwait(false)).ToList();
        var allPersonAliases = (await _personAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();
        var allCharacterAliases = (await _characterAliasesRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false)).ToList();

        // 全トラックは GetAllAsync が無いため、ディスクごとに個別ロードして集約。
        // SiteBuilder の起動時 1 回限りなので、ディスク数分の SELECT を許容。
        var allTracks = new List<Track>();
        foreach (var d in allDiscs)
        {
            var trs = await _tracksRepo.GetByCatalogNoAsync(d.CatalogNo, ct).ConfigureAwait(false);
            allTracks.AddRange(trs);
        }

        // 作詞・作曲・編曲の構造化クレジット行。曲ごとにグルーピングしておく。
        // テンプレ用 HTML 文字列の組み立ては GenerateDetail 内のヘルパで実施する。
        var allSongCredits = new List<SongCredit>();
        foreach (var song in allSongs)
        {
            var rows = await _songCreditsRepo.GetBySongAsync(song.SongId, ct).ConfigureAwait(false);
            allSongCredits.AddRange(rows);
        }
        var songCreditsBySong = allSongCredits
            .GroupBy(c => c.SongId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 歌唱者の構造化クレジット行。録音ごとにグルーピングしておく。
        var allRecordingSingers = new List<SongRecordingSinger>();
        foreach (var r in allRecordings)
        {
            var rows = await _songRecordingSingersRepo.GetByRecordingAsync(r.SongRecordingId, ct).ConfigureAwait(false);
            allRecordingSingers.AddRange(rows);
        }
        var singersByRecording = allRecordingSingers
            .GroupBy(s => s.SongRecordingId)
            .ToDictionary(g => g.Key, g => g.ToList());

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

        GenerateIndex(allSongs, recordingsBySong, musicClassMap);

        foreach (var s in allSongs)
        {
            GenerateDetail(s, recordingsBySong, tracksByRecording, themeSongsByRecording,
                discMap, productMap, sizeVariantMap, partVariantMap, musicClassMap,
                songCreditsBySong, singersByRecording, roleMap, personAliasMap, characterAliasMap);
        }

        _ctx.Logger.Success($"songs: {allSongs.Count + 1} ページ");
    }

    /// <summary>
    /// 楽曲索引 <c>/songs/</c> をレンダリングする
    /// （v1.3.0 ブラッシュアップ続編：
    ///  - 表示単位を「楽曲（song_id）」から「録音バリエーション（song_recording_id）」に変更。
    ///    1 楽曲が TV サイズ / フルサイズ / インスト等で複数録音されている場合、それぞれを別行として出す。
    ///  - セクション順をシリーズ ID 昇順に変更（旧仕様：シリーズの StartDate 昇順）。
    ///  - セクション内の並びを song_recording_id 昇順に変更（旧仕様：song_id 昇順）。
    ///  - 録音が 1 件も無い楽曲は本一覧には載せない（recording 単位なので必然）。
    /// ）。
    /// </summary>
    private void GenerateIndex(
        IReadOnlyList<Song> songs,
        IReadOnlyDictionary<int, List<SongRecording>> recordingsBySong,
        IReadOnlyDictionary<string, SongMusicClass> musicClassMap)
    {
        // song_id → Song の逆引き（recording 行から所属楽曲を取り出すため）。
        var songById = songs.ToDictionary(s => s.SongId);

        // 全 recording をフラット化し、所属楽曲経由で SeriesId を引き当てた行に展開。
        // 録音単位の Sections を組むため、(SeriesId, SongRecordingId) の 2 階層で並べ替える。
        var rows = new List<(int? SeriesId, SongRecordingIndexRow Row)>();
        foreach (var (songId, recs) in recordingsBySong)
        {
            if (!songById.TryGetValue(songId, out var song)) continue;
            string musicClassLabel = (song.MusicClassCode != null && musicClassMap.TryGetValue(song.MusicClassCode, out var mc))
                ? mc.NameJa : "";
            foreach (var r in recs)
            {
                rows.Add((song.SeriesId, new SongRecordingIndexRow
                {
                    SongRecordingId = r.SongRecordingId,
                    SongId = song.SongId,
                    SongTitle = song.Title,
                    VariantLabel = r.VariantLabel ?? "",
                    SingerName = r.SingerName ?? "",
                    MusicClassLabel = musicClassLabel
                }));
            }
        }

        // セクションキー = SeriesId（null は「シリーズ未設定」セクションとして末尾）。
        // セクション内 = song_recording_id 昇順。
        var sections = rows
            .GroupBy(x => x.SeriesId)
            .Select(g =>
            {
                int? seriesId = g.Key;
                string seriesTitle;
                string seriesSlug = "";
                string seriesStartYearLabel = "";
                if (seriesId.HasValue && _ctx.SeriesById.TryGetValue(seriesId.Value, out var series))
                {
                    seriesTitle = series.Title;
                    seriesSlug = series.Slug;
                    // v1.3.0 stage22 後段：シリーズタイトルの隣に添える西暦 4 桁。
                    // シリーズ未設定セクションでは空文字のまま。
                    seriesStartYearLabel = series.StartDate.Year.ToString();
                }
                else
                {
                    seriesTitle = "（シリーズ未設定）";
                }

                var ordered = g
                    .OrderBy(x => x.Row.SongRecordingId)
                    .Select(x => x.Row)
                    .ToList();

                return new
                {
                    SeriesId = seriesId,
                    SeriesTitle = seriesTitle,
                    SeriesSlug = seriesSlug,
                    SeriesStartYearLabel = seriesStartYearLabel,
                    Recordings = ordered
                };
            })
            // シリーズ ID 昇順、null は末尾。
            .OrderBy(s => s.SeriesId.HasValue ? 0 : 1)
            .ThenBy(s => s.SeriesId ?? int.MaxValue)
            .Select(s => new SongSeriesSection
            {
                SeriesTitle = s.SeriesTitle,
                SeriesSlug = s.SeriesSlug,
                SeriesStartYearLabel = s.SeriesStartYearLabel,
                Recordings = s.Recordings
            })
            .ToList();

        var content = new SongsIndexModel
        {
            Sections = sections,
            // TotalCount は recording 件数。ホーム画面の「歌」統計（song_recordings 件数）と整合。
            TotalCount = rows.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "楽曲一覧",
            MetaDescription = "プリキュアシリーズに関連する楽曲（主題歌・挿入歌・イメージソング等）の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "楽曲", Url = "" }
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
        IReadOnlyDictionary<string, Disc> discMap,
        IReadOnlyDictionary<string, Product> productMap,
        IReadOnlyDictionary<string, SongSizeVariant> sizeVariantMap,
        IReadOnlyDictionary<string, SongPartVariant> partVariantMap,
        IReadOnlyDictionary<string, SongMusicClass> musicClassMap,
        IReadOnlyDictionary<int, List<SongCredit>> songCreditsBySong,
        IReadOnlyDictionary<int, List<SongRecordingSinger>> singersByRecording,
        IReadOnlyDictionary<string, Role> roleMap,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        // 出典シリーズ（旧「出自シリーズ」、v1.3.0 公開直前のデザイン整理 第 N 弾で表記変更）。
        string seriesLink = "";
        string seriesTitle = "";
        if (song.SeriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var series))
        {
            seriesLink = PathUtil.SeriesUrl(series.Slug);
            seriesTitle = series.Title;
        }

        // 作詞・作曲・編曲：構造化 song_credits を優先、無ければ Song のフリーテキスト列にフォールバック。
        // BuildCreditRoleHtml は名義群（/persons/{id}/ リンク）の HTML を返す。
        // 行のラベル（「作詞」「作曲」「編曲」）も Role マスタを引いて
        // /stats/roles/{rep}/ にリンク化する（テンプレ側に渡す ...RoleLabelHtml がそれ）。
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

                    string sizeLabel = (t.SongSizeVariantCode != null && sizeVariantMap.TryGetValue(t.SongSizeVariantCode, out var sv)) ? sv.NameJa : "";
                    string partLabel = (t.SongPartVariantCode != null && partVariantMap.TryGetValue(t.SongPartVariantCode, out var pv)) ? pv.NameJa : "";

                    tracksRows.Add(new RecordingTrackRow
                    {
                        ProductCatalogNo = prod.ProductCatalogNo,
                        ProductTitle = prod.Title,
                        // 表示用は日本語フォーマット、ソート用に DateTime も別途保持する。
                        // v1.3.0 公開直前のデザイン整理 第 N 弾：従来は ProductReleaseDate（日本語フォーマット文字列）
                        // を StringComparer.Ordinal でソートしていたため、「2004年10月」が「2004年2月」より先に
                        // 並ぶ不具合があった。DateTime 原値を保持してそちらで並べる。
                        ProductReleaseDate = FormatJpDate(prod.ReleaseDate),
                        ProductReleaseDateRaw = prod.ReleaseDate,
                        DiscCatalogNo = disc.CatalogNo,
                        DiscNoInSet = disc.DiscNoInSet,
                        TrackNo = t.TrackNo,
                        SizeLabel = sizeLabel,
                        PartLabel = partLabel,
                        ProductUrl = PathUtil.ProductUrl(prod.ProductCatalogNo)
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

            // 主題歌としての使用。
            // v1.3.0 ブラッシュアップ続編：episode_theme_songs.usage_actuality に応じて
            //   - 'BROADCAST_NOT_CREDITED' （クレジットなしで流れた）はクレジット集約の本ページでは出さない
            //   - 'CREDITED_NOT_BROADCAST' （クレジットあって実際は流れていない）は出すが注記する
            // を反映する。
            // v1.3.0 公開直前のデザイン整理 第 N 弾：1 話 1 行ではなく
            // (シリーズ, 区分, BroadcastOnly, UsageActuality) で集約し、連続話番号は範囲表記に縮約する
            // （例：「ふたりはプリキュア 第1〜49話 オープニング主題歌」）。
            var themeRowsForGrouping = new List<(Series Series, Episode Episode, EpisodeThemeSong Theme)>();
            if (themeSongsByRecording.TryGetValue(r.SongRecordingId, out var themes))
            {
                foreach (var th in themes)
                {
                    if (string.Equals(th.UsageActuality, EpisodeThemeSongUsageActualities.BroadcastNotCredited, StringComparison.Ordinal))
                        continue;

                    var ep = LookupEpisode(th.EpisodeId);
                    if (ep is null) continue;
                    if (!_ctx.SeriesById.TryGetValue(ep.SeriesId, out var epSeries)) continue;

                    themeRowsForGrouping.Add((epSeries, ep, th));
                }
            }
            // (シリーズ, 区分, broadcast_only, usage_actuality) で集約し、エピソード番号を範囲化。
            var themeRows = BuildThemeUsageRows(themeRowsForGrouping);

            // 歌唱者：song_recording_singers を優先、無ければ SongRecording.SingerName のフリーテキスト。
            // 歌唱者は「歌：」プレフィックスを付けた目立つ表示にするため、HTML（リンク化済み）と
            // フォールバック平文の両方をテンプレに渡す。
            var recordingSingers = singersByRecording.TryGetValue(r.SongRecordingId, out var singerList) ? singerList : new List<SongRecordingSinger>();
            string vocalistsHtml = BuildVocalistsHtml(recordingSingers, r.SingerName, personAliasMap, characterAliasMap);

            recordingViews.Add(new RecordingView
            {
                SongRecordingId = r.SongRecordingId,
                SingerName = r.SingerName ?? "",
                VariantLabel = r.VariantLabel ?? "",
                Notes = r.Notes ?? "",
                VocalistsHtml = vocalistsHtml,
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
                MusicClassLabel = (song.MusicClassCode != null && musicClassMap.TryGetValue(song.MusicClassCode, out var mc))
                    ? mc.NameJa : "",
                LyricistName = song.LyricistName ?? "",
                ComposerName = song.ComposerName ?? "",
                ArrangerName = song.ArrangerName ?? "",
                LyricsHtml = lyricsHtml,
                CompositionHtml = compositionHtml,
                ArrangementHtml = arrangementHtml,
                LyricsRoleLabelHtml = lyricsRoleLabelHtml,
                CompositionRoleLabelHtml = compositionRoleLabelHtml,
                ArrangementRoleLabelHtml = arrangementRoleLabelHtml,
                SeriesTitle = seriesTitle,
                SeriesLink = seriesLink,
                Notes = song.Notes ?? ""
            },
            Recordings = recordingViews
        };
        // MetaDescription を実データから動的構築する（v1.3.1 stage4 追加）。
        // 「{シリーズ}の{楽曲種別}「{曲名}」。歌唱:{歌手}。作詞:{X}、作曲:{Y}。」を骨格に、各セグメント追加前に
        // targetMaxChars=140 を超えないかを確認しつつ追記する。
        string musicClassLabel = (song.MusicClassCode != null && musicClassMap.TryGetValue(song.MusicClassCode, out var mcLabel))
            ? mcLabel.NameJa : "";
        var metaDescription = BuildSongMetaDescription(
            songTitle: song.Title,
            seriesTitle: seriesTitle,
            musicClassLabel: musicClassLabel,
            recordingViews: recordingViews,
            lyricistName: song.LyricistName ?? "",
            composerName: song.ComposerName ?? "");

        // 楽曲詳細の構造化データは Schema.org の MusicComposition 型。
        // 作詞・作曲・編曲は lyricist / composer の Person ノードとして埋め込む（テキストフィールド前提）。
        // v1.3.1 stage4：description と genre を追加して、リッチスニペットの候補要素を増やす。
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
                new BreadcrumbItem { Label = "楽曲", Url = "/songs/" },
                new BreadcrumbItem { Label = song.Title, Url = "" }
            },
            OgType = "music.song",
            JsonLd = jsonLd
        };
        _page.RenderAndWrite(songUrl, "songs", "songs-detail.sbn", content, layout);
    }

    /// <summary>
    /// 楽曲詳細ページの <c>&lt;meta name="description"&gt;</c> 用説明文を実データから組み立てる
    /// （v1.3.1 stage4 追加）。
    /// <para>
    /// 構成：「『{シリーズ}』の{楽曲種別}「{曲名}」。歌唱:{歌手}。作詞:{X}、作曲:{Y}。」を骨格に、
    /// 各セグメント追加前に targetMaxChars=140 を超えないかを確認しつつ追記する。
    /// 歌手名は <see cref="RecordingView.SingerName"/> から最大 2 名（先頭録音バージョン優先）。
    /// シリーズタイトルが空のときは「プリキュアシリーズの{楽曲種別}…」にフォールバック。
    /// </para>
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
    /// <para>
    /// roles マスタに行があれば <see cref="Role.NameJa"/> を <see cref="RoleSuccessorResolver"/>
    /// 経由で求めた系譜代表 role_code を URL に使って /stats/roles/{rep}/ にリンク化する。
    /// マスタに無い、または NameJa が空のときは <paramref name="fallbackLabel"/> をリンクなし平文で返す
    /// （表組みの th が必ず何かしらラベルを必要とするため）。
    /// </para>
    /// </summary>
    private string BuildRoleLabelLinkHtml(string roleCode, IReadOnlyDictionary<string, Role> roleMap, string fallbackLabel)
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
    /// 指定役職の構造化クレジット行を HTML 化する。
    /// <para>
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
    /// </para>
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
    /// <para>
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
    /// </para>
    /// </summary>
    private string BuildVocalistsHtml(
        IReadOnlyList<SongRecordingSinger> singers,
        string? fallbackSingerName,
        IReadOnlyDictionary<int, PersonAlias> personAliasMap,
        IReadOnlyDictionary<int, CharacterAlias> characterAliasMap)
    {
        // VOCALS のみを抽出（CHORUS 等は本表示の対象外）。
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
    /// <para>
    /// 例：1, 2, 3, 5, 6, 7 → 「第1〜3, 5〜7話」、1 のみ → 「第1話」、1 と 3 → 「第1, 3話」。
    /// 区分ラベルは「オープニング主題歌」「エンディング主題歌」「挿入歌」のように展開する
    /// （旧コード丸出しの "OP"/"ED"/"INSERT" 表示を改善）。
    /// </para>
    /// </summary>
    private static IReadOnlyList<RecordingThemeRow> BuildThemeUsageRows(
        IReadOnlyList<(Series Series, Episode Episode, EpisodeThemeSong Theme)> source)
    {
        var groups = source
            .GroupBy(x => (
                x.Series.SeriesId,
                x.Theme.ThemeKind,
                x.Theme.IsBroadcastOnly,
                x.Theme.UsageActuality
            ));

        var result = new List<RecordingThemeRow>();
        foreach (var g in groups)
        {
            // グループ内の最初の Series 情報をそのまま採用（GroupBy キーで同一保証あり）。
            var any = g.First();
            var epNos = g.Select(x => (int)x.Episode.SeriesEpNo).Distinct().OrderBy(n => n).ToList();
            string rangeLabel = CompressEpisodeNumbers(epNos);

            // 区分ラベル：OP/ED/INSERT を「オープニング主題歌」等に展開。
            string kindLabel = any.Theme.ThemeKind switch
            {
                "OP" => "オープニング主題歌",
                "ED" => "エンディング主題歌",
                "INSERT" => "挿入歌",
                _ => any.Theme.ThemeKind
            };
            // 注記：本放送限定 / クレジット only。
            if (any.Theme.IsBroadcastOnly) kindLabel += "（本放送のみ）";
            if (string.Equals(any.Theme.UsageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
                kindLabel += "（実際には不使用）";

            result.Add(new RecordingThemeRow
            {
                SeriesTitle = any.Series.Title,
                SeriesSlug = any.Series.Slug,
                // ソート用に「グループ内の最小エピソード番号」を保持しておく（複数グループの並び順に使用）。
                SortStartEpNo = epNos.Count > 0 ? epNos[0] : 0,
                EpisodeRangeLabel = rangeLabel,
                ThemeKindLabel = kindLabel,
                // 旧仕様の単一エピソード参照プロパティは廃止（範囲集約のため）。
            });
        }

        // ソート：シリーズ slug 昇順 → グループの最初の話番号 昇順 → 区分名 昇順。
        return result
            .OrderBy(x => x.SeriesSlug, StringComparer.Ordinal)
            .ThenBy(x => x.SortStartEpNo)
            .ThenBy(x => x.ThemeKindLabel, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 整数リスト（昇順、重複なし前提）を連続区間に圧縮して「第1〜3, 5〜7話」のような表記を返す。
    /// 単独要素は「第1話」、単一連続は「第1〜49話」、複数区間は「第1〜3, 5〜7話」のように整形する。
    /// 空リストは空文字を返す。
    /// </summary>
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

    private static string FormatJpDate(DateTime dt)
        => $"{dt.Year}年{dt.Month}月{dt.Day}日";

    // ─── テンプレ用 DTO 群 ───

    private sealed class SongsIndexModel
    {
        public IReadOnlyList<SongSeriesSection> Sections { get; set; } = Array.Empty<SongSeriesSection>();
        public int TotalCount { get; set; }
    }

    /// <summary>
    /// 楽曲索引のセクション（シリーズ単位）。
    /// v1.3.0 ブラッシュアップ続編：表示単位が「楽曲（song）」から「録音バリエーション（song_recording）」
    /// に変わったため、メンバープロパティを <c>Members</c> → <c>Recordings</c> にリネーム。
    /// </summary>
    private sealed class SongSeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        /// <summary>
        /// シリーズページへのリンクに使う slug（v1.3.0 ブラッシュアップ続編で追加）。
        /// 「シリーズ未設定」セクションでは空文字。
        /// </summary>
        public string SeriesSlug { get; set; } = "";
        /// <summary>
        /// シリーズ開始年の西暦 4 桁文字列（例: "2004"）。「シリーズ未設定」セクションでは空文字
        /// （v1.3.0 stage22 後段で追加。略称（title_short）は生成・UI ともに使わず、
        /// シリーズタイトルの隣に薄色の括弧で年を添える表現に統一）。
        /// </summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>セクション内の録音バリエーション一覧（song_recording_id 昇順）。</summary>
        public IReadOnlyList<SongRecordingIndexRow> Recordings { get; set; } = Array.Empty<SongRecordingIndexRow>();
    }

    /// <summary>
    /// 楽曲索引の 1 行 = 1 録音バリエーション（v1.3.0 ブラッシュアップ続編で recording 単位化）。
    /// 旧 <c>SongIndexRow</c>（楽曲単位）+ 旧 <c>SongRecordingRow</c>（録音サブ行）を統合した。
    /// 楽曲タイトル → /songs/{SongId}/ への詳細リンク、recording_id は内部識別子として保持
    /// （URL には出さない）。VariantLabel / SingerName / MusicClassLabel はテンプレ側で空判定して出し分ける。
    /// </summary>
    private sealed class SongRecordingIndexRow
    {
        public int SongRecordingId { get; set; }
        public int SongId { get; set; }
        public string SongTitle { get; set; } = "";
        public string VariantLabel { get; set; } = "";
        public string SingerName { get; set; } = "";
        public string MusicClassLabel { get; set; } = "";
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
        public string MusicClassLabel { get; set; } = "";
        /// <summary>
        /// 作詞のフリーテキスト（旧仕様の <c>songs.lyricist_name</c>）。
        /// v1.3.0 公開直前のデザイン整理 第 N 弾以降は構造化クレジット
        /// （<see cref="LyricsHtml"/>）が優先表示されるため、本フィールドは構造化が無い曲の
        /// フォールバック表示でだけ参照される（実際の処理は Generator 側で済ませ、
        /// テンプレ側は <see cref="LyricsHtml"/> をそのまま使う）。
        /// </summary>
        public string LyricistName { get; set; } = "";
        public string ComposerName { get; set; } = "";
        public string ArrangerName { get; set; } = "";
        /// <summary>
        /// 作詞の表示用 HTML（v1.3.0 公開直前のデザイン整理 第 N 弾で追加）。
        /// 構造化 <c>song_credits</c> 行があれば名義リンク（/persons/{id}/）を区切り文字で連結した HTML、
        /// 行が無く <see cref="LyricistName"/> が非空なら HTML エスケープした平文、
        /// どちらも無ければ空文字。テンプレ側で空判定して行ごと出し分ける。
        /// </summary>
        public string LyricsHtml { get; set; } = "";
        /// <summary>作曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string CompositionHtml { get; set; } = "";
        /// <summary>編曲の表示用 HTML（仕様は <see cref="LyricsHtml"/> と同様）。</summary>
        public string ArrangementHtml { get; set; } = "";
        /// <summary>
        /// 「作詞」役職ラベル HTML（v1.3.0 公開直前のデザイン整理 第 N 弾で追加）。
        /// roles マスタ参照 + RoleSuccessorResolver による系譜代表解決を経て、
        /// /stats/roles/{rep}/ へのアンカーになる。マスタ未登録時は素のフォールバック文字列。
        /// </summary>
        public string LyricsRoleLabelHtml { get; set; } = "";
        /// <summary>「作曲」役職ラベル HTML（仕様は <see cref="LyricsRoleLabelHtml"/> と同様）。</summary>
        public string CompositionRoleLabelHtml { get; set; } = "";
        /// <summary>「編曲」役職ラベル HTML（仕様は <see cref="LyricsRoleLabelHtml"/> と同様）。</summary>
        public string ArrangementRoleLabelHtml { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesLink { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class RecordingView
    {
        public int SongRecordingId { get; set; }
        /// <summary>歌唱者のフリーテキスト（旧仕様の <c>song_recordings.singer_name</c>、フォールバック用）。</summary>
        public string SingerName { get; set; } = "";
        public string VariantLabel { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>
        /// 歌唱者の表示用 HTML（v1.3.0 公開直前のデザイン整理 第 N 弾で追加）。
        /// 構造化 <c>song_recording_singers</c> 行（VOCALS 役）があればキャラ/人物リンク化した HTML、
        /// 行が無ければ <see cref="SingerName"/> の HTML エスケープ平文。
        /// テンプレ側で空判定して「歌：」行を出し分ける。
        /// </summary>
        public string VocalistsHtml { get; set; } = "";
        public IReadOnlyList<RecordingTrackRow> Tracks { get; set; } = Array.Empty<RecordingTrackRow>();
        public IReadOnlyList<RecordingThemeRow> ThemeUsages { get; set; } = Array.Empty<RecordingThemeRow>();
    }

    private sealed class RecordingTrackRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string ProductTitle { get; set; } = "";
        /// <summary>発売日（テンプレ表示用、日本語フォーマット文字列「2004年2月18日」）。</summary>
        public string ProductReleaseDate { get; set; } = "";
        /// <summary>
        /// 発売日の DateTime 原値（v1.3.0 公開直前のデザイン整理 第 N 弾で追加、ソート専用）。
        /// 旧仕様は日本語フォーマット済み文字列で文字列ソートしていたため、
        /// 「2004年10月」が「2004年2月」より先に並ぶ不具合があった（lex 比較 "1" &lt; "2"）。
        /// </summary>
        public DateTime ProductReleaseDateRaw { get; set; }
        public string ProductUrl { get; set; } = "";
        public string DiscCatalogNo { get; set; } = "";
        public uint? DiscNoInSet { get; set; }
        public byte TrackNo { get; set; }
        public string SizeLabel { get; set; } = "";
        public string PartLabel { get; set; } = "";
    }

    /// <summary>
    /// 主題歌使用エピソード行（v1.3.0 公開直前のデザイン整理 第 N 弾で「1 話 1 行」から
    /// 「シリーズ × 区分 × broadcast_only × usage_actuality 単位で集約、エピソード番号を範囲圧縮」に再設計）。
    /// </summary>
    private sealed class RecordingThemeRow
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        /// <summary>
        /// エピソード番号の集約表示（例：「第1〜49話」「第3, 5, 7話」「第10〜12, 14話」）。
        /// </summary>
        public string EpisodeRangeLabel { get; set; } = "";
        /// <summary>
        /// グループ内の最小エピソード番号（ソート専用）。テンプレからは参照しない。
        /// </summary>
        public int SortStartEpNo { get; set; }
        /// <summary>
        /// 区分ラベル（「オープニング主題歌」「エンディング主題歌」「挿入歌」、
        /// 必要に応じて「（本放送のみ）」「（実際には不使用）」が追記される）。
        /// </summary>
        public string ThemeKindLabel { get; set; } = "";
    }
}