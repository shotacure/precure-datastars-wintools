using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 音楽の年表（歌唱・作詞作曲編曲や音楽の役職詳細の年表タブ）で、歌・劇伴・盤の参加を時期に置くための日付。
/// <list type="bullet">
///   <item><description>歌（録音）：その録音が初めて収められた盤（商品）の発売日。盤に無い録音は出典シリーズの開始日。</description></item>
///   <item><description>歌（曲）：その曲の録音のうち最も早い日。</description></item>
///   <item><description>劇伴（録音回）：その録音回の曲が初めて収められた盤の発売日。盤に無い回はシリーズの開始日。</description></item>
///   <item><description>盤：発売日。</description></item>
/// </list>
/// 削除済みの盤・商品は数えない。ビルドで 1 度だけ作り、各ページで共有する。
/// </summary>
internal sealed class MusicTimelineDates
{
    private readonly BuildContext _ctx;
    private readonly Dictionary<int, DateOnly> _byRecording;
    private readonly Dictionary<int, DateOnly> _bySong = new();
    private readonly Dictionary<(int SeriesId, byte SessionNo), DateOnly> _bySession = new();
    private readonly Dictionary<(int SeriesId, string MNoDetail), byte> _sessionOfCue = new();
    private readonly Dictionary<int, List<int>> _recordingsBySong = new();

    private MusicTimelineDates(BuildContext ctx, Dictionary<string, DateOnly> releaseByDisc)
    {
        _ctx = ctx;
        foreach (var (sid, cues) in ctx.BgmCuesBySeries)
            foreach (var cue in cues)
                _sessionOfCue[(sid, cue.MNoDetail)] = cue.SessionNo;

        _byRecording = new Dictionary<int, DateOnly>();
        foreach (var (catalogNo, tracks) in ctx.TracksByCatalogNo)
        {
            if (!releaseByDisc.TryGetValue(catalogNo, out var date)) continue;
            foreach (var t in tracks)
            {
                if (t.SongRecordingId is int recId)
                {
                    if (!_byRecording.TryGetValue(recId, out var cur) || date < cur) _byRecording[recId] = date;
                }
                else if (t.BgmSeriesId is int bsid && t.BgmMNoDetail is string mNo
                         && _sessionOfCue.TryGetValue((bsid, mNo), out var sessionNo))
                {
                    var key = (bsid, sessionNo);
                    if (!_bySession.TryGetValue(key, out var cur) || date < cur) _bySession[key] = date;
                }
            }
        }

        foreach (var rec in ctx.SongRecordingById.Values)
        {
            if (!_recordingsBySong.TryGetValue(rec.SongId, out var list))
            {
                list = new List<int>();
                _recordingsBySong[rec.SongId] = list;
            }
            list.Add(rec.SongRecordingId);
            if (_byRecording.TryGetValue(rec.SongRecordingId, out var d)
                && (!_bySong.TryGetValue(rec.SongId, out var cur) || d < cur))
            {
                _bySong[rec.SongId] = d;
            }
        }
    }

    /// <summary>盤（ディスク）と商品を読み込み、日付の表を作る。</summary>
    public static async Task<MusicTimelineDates> LoadAsync(
        BuildContext ctx, DiscsRepository discsRepo, ProductsRepository productsRepo, CancellationToken ct)
    {
        var discs = await discsRepo.GetByProductReleaseOrderAsync(ct).ConfigureAwait(false);
        var products = (await productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false))
            .ToDictionary(p => p.ProductCatalogNo, StringComparer.Ordinal);
        var releaseByDisc = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var d in discs)
        {
            if (products.TryGetValue(d.ProductCatalogNo, out var p))
                releaseByDisc[d.CatalogNo] = DateOnly.FromDateTime(p.ReleaseDate);
        }
        return new MusicTimelineDates(ctx, releaseByDisc);
    }

    /// <summary>録音が初めて盤に収められた日（盤に無ければ出典シリーズの開始日、それも無ければ null）。</summary>
    public DateOnly? Recording(int recordingId)
    {
        if (_byRecording.TryGetValue(recordingId, out var d)) return d;
        return _ctx.SongRecordingById.TryGetValue(recordingId, out var rec) ? SeriesStart(rec.SeriesId) : null;
    }

    /// <summary>曲が初めて盤に収められた日（録音のうち最も早い日。盤に無ければ最初の録音の出典シリーズの開始日）。</summary>
    public DateOnly? Song(int songId)
    {
        if (_bySong.TryGetValue(songId, out var d)) return d;
        if (!_recordingsBySong.TryGetValue(songId, out var recs) || recs.Count == 0) return null;
        return Recording(recs.Min());
    }

    /// <summary>劇伴の録音回の曲が初めて盤に収められた日（盤に無ければシリーズの開始日）。</summary>
    public DateOnly? Session(int seriesId, byte sessionNo)
        => _bySession.TryGetValue((seriesId, sessionNo), out var d) ? d : SeriesStart(seriesId);

    /// <summary>劇伴の曲（シリーズ・M ナンバー）が属する録音回。分からなければ null。</summary>
    public byte? SessionOfCue(int seriesId, string mNoDetail)
        => _sessionOfCue.TryGetValue((seriesId, mNoDetail), out var s) ? s : null;

    /// <summary>盤（商品）の発売日。商品が分からなければ null。</summary>
    public DateOnly? Product(string productCatalogNo)
        => _ctx.MusicCredits.ProductByCatalogNo.TryGetValue(productCatalogNo, out var p) ? DateOnly.FromDateTime(p.ReleaseDate) : null;

    private DateOnly? SeriesStart(int? seriesId)
        => seriesId is int sid && _ctx.SeriesById.TryGetValue(sid, out var s) ? s.StartDate : null;
}

/// <summary>
/// 音楽の年表の入力を、人物・団体（キー）ごとに集める。歌は曲ごと（録音に付いたクレジットはその録音の日、
/// 曲に付いたものは曲の日。同じ曲は早い方の日）、劇伴は録音回ごと、盤は商品ごとに 1 件。
/// </summary>
internal sealed class MusicTimelineCollector
{
    private readonly BuildContext _ctx;
    private readonly MusicTimelineDates _dates;
    private readonly Dictionary<(char Kind, int Id), Points> _byEntity = new();

    private sealed class Points
    {
        public readonly Dictionary<int, DateOnly> Songs = new();
        public readonly HashSet<(int SeriesId, byte SessionNo)> Sessions = new();
        public readonly HashSet<string> Products = new(StringComparer.Ordinal);
    }

    public MusicTimelineCollector(BuildContext ctx, MusicTimelineDates dates)
    {
        _ctx = ctx;
        _dates = dates;
    }

    /// <summary>集めた人物・団体のキー。</summary>
    public IEnumerable<(char Kind, int Id)> Keys => _byEntity.Keys;

    private Points Of((char Kind, int Id) key)
    {
        if (!_byEntity.TryGetValue(key, out var p))
        {
            p = new Points();
            _byEntity[key] = p;
        }
        return p;
    }

    /// <summary>歌の参加を足す。<paramref name="recordingId"/> があればその録音の日、無ければ曲の日に置く。</summary>
    public void AddSong((char Kind, int Id) key, int songId, int? recordingId = null)
    {
        var date = recordingId is int rid ? _dates.Recording(rid) : _dates.Song(songId);
        if (date is not DateOnly d) return;
        var songs = Of(key).Songs;
        if (!songs.TryGetValue(songId, out var cur) || d < cur) songs[songId] = d;
    }

    /// <summary>劇伴の録音回の参加を足す。</summary>
    public void AddSession((char Kind, int Id) key, int seriesId, byte sessionNo)
        => Of(key).Sessions.Add((seriesId, sessionNo));

    /// <summary>劇伴の曲（シリーズ・M ナンバー）の参加を、その曲が属する録音回の参加として足す。</summary>
    public void AddCue((char Kind, int Id) key, int seriesId, string mNoDetail)
        => AddSession(key, seriesId, _dates.SessionOfCue(seriesId, mNoDetail) ?? 0);

    /// <summary>盤（商品）の参加を足す。</summary>
    public void AddProduct((char Kind, int Id) key, string productCatalogNo)
        => Of(key).Products.Add(productCatalogNo);

    /// <summary>
    /// キーの参加を年表の入力にする。名前・リンク先・並びのキーは呼び出し側が渡す。参加が無ければ null。
    /// </summary>
    public RoleTimelineEntity? ToEntity((char Kind, int Id) key, string entityKind, string name, string url, long firstSortPos)
    {
        if (!_byEntity.TryGetValue(key, out var p)) return null;
        var songs = p.Songs
            .Select(kv => new RoleTimelinePoint(kv.Value, _ctx.SongById.TryGetValue(kv.Key, out var s) ? s.Title : ""))
            .ToList();
        var bgms = new List<RoleTimelinePoint>();
        foreach (var (sid, sessionNo) in p.Sessions)
        {
            if (_dates.Session(sid, sessionNo) is not DateOnly d || !_ctx.SeriesById.TryGetValue(sid, out var series)) continue;
            string sessionName = _ctx.MusicCredits.SessionByKey.TryGetValue((sid, sessionNo), out var session) ? session.SessionName : "";
            bgms.Add(new RoleTimelinePoint(d, sessionName.Length > 0 ? $"{series.Title}（{sessionName}）" : series.Title));
        }
        var products = new List<RoleTimelinePoint>();
        foreach (var catalogNo in p.Products)
        {
            if (_dates.Product(catalogNo) is not DateOnly d) continue;
            products.Add(new RoleTimelinePoint(d,
                _ctx.MusicCredits.ProductByCatalogNo.TryGetValue(catalogNo, out var product) ? product.Title : catalogNo));
        }
        if (songs.Count + bgms.Count + products.Count == 0) return null;
        return new RoleTimelineEntity
        {
            EntityKind = entityKind,
            EntityName = name,
            EntityUrl = url,
            FirstSortPos = firstSortPos,
            Songs = songs,
            Bgms = bgms,
            Products = products
        };
    }
}
