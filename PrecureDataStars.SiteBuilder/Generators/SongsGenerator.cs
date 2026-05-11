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

    public SongsGenerator(
        BuildContext ctx,
        PageRenderer page,
        IConnectionFactory factory)
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

        // 全トラックは GetAllAsync が無いため、ディスクごとに個別ロードして集約。
        // SiteBuilder の起動時 1 回限りなので、ディスク数分の SELECT を許容。
        var allTracks = new List<Track>();
        foreach (var d in allDiscs)
        {
            var trs = await _tracksRepo.GetByCatalogNoAsync(d.CatalogNo, ct).ConfigureAwait(false);
            allTracks.AddRange(trs);
        }

        var musicClassMap = musicClasses.ToDictionary(c => c.ClassCode, StringComparer.Ordinal);
        var sizeVariantMap = sizeVariants.ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        var partVariantMap = partVariants.ToDictionary(v => v.VariantCode, StringComparer.Ordinal);
        var discMap = allDiscs.ToDictionary(d => d.CatalogNo, StringComparer.Ordinal);
        var productMap = allProducts.ToDictionary(p => p.ProductCatalogNo, StringComparer.Ordinal);
        var recordingsBySong = allRecordings.GroupBy(r => r.SongId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.SongRecordingId).ToList());

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
                discMap, productMap, sizeVariantMap, partVariantMap, musicClassMap);
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
        IReadOnlyDictionary<string, SongMusicClass> musicClassMap)
    {
        // 出自シリーズ。
        string seriesLink = "";
        string seriesTitle = "";
        if (song.SeriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var series))
        {
            seriesLink = PathUtil.SeriesUrl(series.Slug);
            seriesTitle = series.Title;
        }

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
                        ProductReleaseDate = FormatJpDate(prod.ReleaseDate),
                        DiscCatalogNo = disc.CatalogNo,
                        DiscNoInSet = disc.DiscNoInSet,
                        TrackNo = t.TrackNo,
                        SizeLabel = sizeLabel,
                        PartLabel = partLabel,
                        ProductUrl = PathUtil.ProductUrl(prod.ProductCatalogNo)
                    });
                }
                tracksRows = tracksRows
                    .OrderBy(x => x.ProductReleaseDate, StringComparer.Ordinal)
                    .ThenBy(x => x.ProductCatalogNo, StringComparer.Ordinal)
                    .ThenBy(x => x.DiscNoInSet ?? 1u)
                    .ThenBy(x => x.TrackNo)
                    .ToList();
            }

            // 主題歌として使用されたエピソード。
            // v1.3.0 ブラッシュアップ続編：episode_theme_songs.usage_actuality に応じて
            //   - 'BROADCAST_NOT_CREDITED' （クレジットなしで流れた）はクレジット集約の本ページでは出さない
            //   - 'CREDITED_NOT_BROADCAST' （クレジットあって実際は流れていない）は出すが
            //                                themeKindLabel に「（実際には不使用）」を追記する
            // を反映する。
            var themeRows = new List<RecordingThemeRow>();
            if (themeSongsByRecording.TryGetValue(r.SongRecordingId, out var themes))
            {
                foreach (var th in themes)
                {
                    // v1.3.0 ブラッシュアップ続編：BROADCAST_NOT_CREDITED は曲ページからは見せない。
                    if (string.Equals(th.UsageActuality, EpisodeThemeSongUsageActualities.BroadcastNotCredited, StringComparison.Ordinal))
                        continue;

                    var ep = LookupEpisode(th.EpisodeId);
                    if (ep is null) continue;
                    if (!_ctx.SeriesById.TryGetValue(ep.SeriesId, out var epSeries)) continue;

                    // 主題歌種別ラベル：v1.3.0 で seq 列が劇中順を表す汎用カラムに変わったため、
                    // 旧仕様の「挿入歌 N」（INSERT 内通番）は意味的に該当しなくなった。
                    // 楽曲詳細ページでは「OP / ED / 挿入歌」の 3 区分のみで表示する。
                    string themeKindLabel = th.ThemeKind switch
                    {
                        "OP" => "OP",
                        "ED" => "ED",
                        "INSERT" => "挿入歌",
                        _ => th.ThemeKind
                    };
                    if (th.IsBroadcastOnly) themeKindLabel += "（本放送のみ）";
                    // v1.3.0 ブラッシュアップ続編：CREDITED_NOT_BROADCAST は「クレジットされて
                    // いるが実際には流れていない」状態。曲ページの主題歌使用一覧に出しつつ、
                    // 末尾に「（実際には不使用）」と注記して事実関係を明示する。
                    if (string.Equals(th.UsageActuality, EpisodeThemeSongUsageActualities.CreditedNotBroadcast, StringComparison.Ordinal))
                    {
                        themeKindLabel += "（実際には不使用）";
                    }

                    themeRows.Add(new RecordingThemeRow
                    {
                        SeriesTitle = epSeries.Title,
                        SeriesSlug = epSeries.Slug,
                        SeriesEpNo = ep.SeriesEpNo,
                        EpisodeTitle = ep.TitleText,
                        EpisodeUrl = PathUtil.EpisodeUrl(epSeries.Slug, ep.SeriesEpNo),
                        ThemeKindLabel = themeKindLabel
                    });
                }
                themeRows = themeRows
                    .OrderBy(x => x.SeriesSlug, StringComparer.Ordinal)
                    .ThenBy(x => x.SeriesEpNo)
                    .ToList();
            }

            recordingViews.Add(new RecordingView
            {
                SongRecordingId = r.SongRecordingId,
                SingerName = r.SingerName ?? "",
                VariantLabel = r.VariantLabel ?? "",
                Notes = r.Notes ?? "",
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
                SeriesTitle = seriesTitle,
                SeriesLink = seriesLink,
                Notes = song.Notes ?? ""
            },
            Recordings = recordingViews
        };
        // 楽曲詳細の構造化データは Schema.org の MusicComposition 型。
        // 作詞・作曲・編曲は lyricist / composer の Person ノードとして埋め込む（テキストフィールド前提）。
        string baseUrl = _ctx.Config.BaseUrl;
        string songUrl = PathUtil.SongUrl(song.SongId);
        var jsonLdDict = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "MusicComposition",
            ["name"] = song.Title,
            ["inLanguage"] = "ja"
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
            MetaDescription = $"{song.Title} の録音バージョンと収録商品・主題歌使用エピソード。",
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
        public string LyricistName { get; set; } = "";
        public string ComposerName { get; set; } = "";
        public string ArrangerName { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesLink { get; set; } = "";
        public string Notes { get; set; } = "";
    }

    private sealed class RecordingView
    {
        public int SongRecordingId { get; set; }
        public string SingerName { get; set; } = "";
        public string VariantLabel { get; set; } = "";
        public string Notes { get; set; } = "";
        public IReadOnlyList<RecordingTrackRow> Tracks { get; set; } = Array.Empty<RecordingTrackRow>();
        public IReadOnlyList<RecordingThemeRow> ThemeUsages { get; set; } = Array.Empty<RecordingThemeRow>();
    }

    private sealed class RecordingTrackRow
    {
        public string ProductCatalogNo { get; set; } = "";
        public string ProductTitle { get; set; } = "";
        public string ProductReleaseDate { get; set; } = "";
        public string ProductUrl { get; set; } = "";
        public string DiscCatalogNo { get; set; } = "";
        public uint? DiscNoInSet { get; set; }
        public byte TrackNo { get; set; }
        public string SizeLabel { get; set; } = "";
        public string PartLabel { get; set; } = "";
    }

    private sealed class RecordingThemeRow
    {
        public string SeriesTitle { get; set; } = "";
        public string SeriesSlug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string EpisodeTitle { get; set; } = "";
        public string EpisodeUrl { get; set; } = "";
        public string ThemeKindLabel { get; set; } = "";
    }
}
