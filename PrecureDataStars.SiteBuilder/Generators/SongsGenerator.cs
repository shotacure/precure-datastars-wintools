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

    /// <summary><c>/songs/</c>（楽曲索引）。シリーズでセクション分け、各セクション内は 50 音順。</summary>
    private void GenerateIndex(
        IReadOnlyList<Song> songs,
        IReadOnlyDictionary<int, List<SongRecording>> recordingsBySong,
        IReadOnlyDictionary<string, SongMusicClass> musicClassMap)
    {
        // セクションキー：series_id（NULL は「シリーズ未設定」セクションとして末尾に）。
        var sections = songs
            .GroupBy(s => s.SeriesId)
            .Select(g =>
            {
                int? seriesId = g.Key;
                string seriesTitle;
                DateOnly orderKey;
                if (seriesId.HasValue && _ctx.SeriesById.TryGetValue(seriesId.Value, out var series))
                {
                    seriesTitle = series.Title;
                    orderKey = series.StartDate;
                }
                else
                {
                    seriesTitle = "（シリーズ未設定）";
                    orderKey = DateOnly.MaxValue;
                }

                var members = g.OrderBy(s => string.IsNullOrEmpty(s.TitleKana) ? 1 : 0)
                               .ThenBy(s => s.TitleKana, StringComparer.Ordinal)
                               .ThenBy(s => s.Title, StringComparer.Ordinal)
                               .Select(s => new SongIndexRow
                               {
                                   SongId = s.SongId,
                                   Title = s.Title,
                                   TitleKana = s.TitleKana ?? "",
                                   MusicClassLabel = (s.MusicClassCode != null && musicClassMap.TryGetValue(s.MusicClassCode, out var mc))
                                       ? mc.NameJa : "",
                                   RecordingCount = recordingsBySong.TryGetValue(s.SongId, out var recs) ? recs.Count : 0
                               })
                               .ToList();
                return new { OrderKey = orderKey, SeriesTitle = seriesTitle, Members = members };
            })
            .OrderBy(s => s.OrderKey)
            .Select(s => new SongSeriesSection
            {
                SeriesTitle = s.SeriesTitle,
                Members = s.Members
            })
            .ToList();

        var content = new SongsIndexModel
        {
            Sections = sections,
            TotalCount = songs.Count
        };
        var layout = new LayoutModel
        {
            PageTitle = "楽曲一覧",
            MetaDescription = "プリキュアシリーズに関連する楽曲（主題歌・挿入歌・イメージソング等）の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
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
            var themeRows = new List<RecordingThemeRow>();
            if (themeSongsByRecording.TryGetValue(r.SongRecordingId, out var themes))
            {
                foreach (var th in themes)
                {
                    var ep = LookupEpisode(th.EpisodeId);
                    if (ep is null) continue;
                    if (!_ctx.SeriesById.TryGetValue(ep.SeriesId, out var epSeries)) continue;

                    string themeKindLabel = th.ThemeKind switch
                    {
                        "OP" => "OP",
                        "ED" => "ED",
                        "INSERT" => $"挿入歌 {th.InsertSeq}",
                        _ => th.ThemeKind
                    };
                    if (th.IsBroadcastOnly) themeKindLabel += "（本放送のみ）";

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

    private sealed class SongSeriesSection
    {
        public string SeriesTitle { get; set; } = "";
        public IReadOnlyList<SongIndexRow> Members { get; set; } = Array.Empty<SongIndexRow>();
    }

    private sealed class SongIndexRow
    {
        public int SongId { get; set; }
        public string Title { get; set; } = "";
        public string TitleKana { get; set; } = "";
        public string MusicClassLabel { get; set; } = "";
        public int RecordingCount { get; set; }
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
