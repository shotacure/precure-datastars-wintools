using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>
/// 音楽カテゴリのページ群を生成する（v1.3.0 ブラッシュアップ続編）。
/// <para>
/// 生成対象：
/// <list type="bullet">
///   <item><description><c>/music/</c>：音楽ランディング（歌・劇伴の入り口）</description></item>
///   <item><description><c>/bgms/</c>：シリーズ別の劇伴データ一覧（劇伴を持つシリーズだけ表示）</description></item>
///   <item><description><c>/bgms/{slug}/</c>：1 シリーズの劇伴音源一覧（録音セッション別）</description></item>
/// </list>
/// </para>
/// <para>
/// 旧来 <c>/songs/</c> 配下にあった「楽曲一覧 + 楽曲詳細」は <see cref="SongsGenerator"/> が引き続き担当。
/// 本ジェネレータが追加するのは「カテゴリのまとめ役 /music/」と「劇伴 /bgms/」のみ。
/// </para>
/// <para>
/// 劇伴一覧表は v1.3.0 ブラッシュアップでシリーズ詳細ページから切り離し、本ページに集約した。
/// シリーズ詳細からは「劇伴一覧へ →」のリンク 1 本で誘導する。
/// </para>
/// <para>
/// 仮 M 番号（<c>is_temp_m_no=1</c>）の音源は閲覧 UI 上では出さない（マスタメンテ専用情報）。
/// 但し集計件数（シリーズの「曲数」）には含める設計。
/// </para>
/// </summary>
public sealed class MusicGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    private readonly BgmCuesRepository _cuesRepo;
    private readonly BgmSessionsRepository _sessionsRepo;
    private readonly SongsRepository _songsRepo;
    private readonly SongRecordingsRepository _recRepo;
    /// <summary>
    /// 商品件数（/music/ ランディングの「商品」カードに表示）取得用。
    /// 全件取得して Count するだけなので軽量。
    /// </summary>
    private readonly ProductsRepository _productsRepo;

    public MusicGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _cuesRepo = new BgmCuesRepository(factory);
        _sessionsRepo = new BgmSessionsRepository(factory);
        _songsRepo = new SongsRepository(factory);
        _recRepo = new SongRecordingsRepository(factory);
        _productsRepo = new ProductsRepository(factory);
    }

    public async Task GenerateAsync(CancellationToken ct = default)
    {
        _ctx.Logger.Section("Generating music");

        // 全劇伴セッション・全劇伴音源を一括ロードしてシリーズごとに分けて使う。
        var allSessions = await _sessionsRepo.GetAllAsync(ct).ConfigureAwait(false);
        var sessionsBySeries = allSessions
            .GroupBy(s => s.SeriesId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SessionNo).ToList());

        // 全シリーズの cues をシリーズ単位でロード（シリーズ毎に別クエリにすると 60+ クエリになるため
        // 全件一発で取りたいところだが、現状 BgmCuesRepository には GetAllAsync が無いのでシリーズ別に取る。
        // ジェネレータ起動は 1 ビルド 1 回なので妥協。後で拡張余地）。
        var cuesBySeries = new Dictionary<int, IReadOnlyList<BgmCue>>();
        foreach (var s in _ctx.Series)
        {
            var rows = await _cuesRepo.GetBySeriesAsync(s.SeriesId, ct).ConfigureAwait(false);
            if (rows.Count > 0) cuesBySeries[s.SeriesId] = rows;
        }

        // 楽曲・歌録音は集計件数表示用にロード（軽量）。
        var allSongs = await _songsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        var allRecs = await _recRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        // 商品（/music/ ランディングの「商品」カード件数表示用）。
        var allProducts = await _productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);

        GenerateMusicLanding(allSongs.Count, allRecs.Count, allProducts.Count, cuesBySeries, ct);
        GenerateBgmIndex(cuesBySeries, sessionsBySeries);
        await GenerateBgmDetailPagesAsync(cuesBySeries, sessionsBySeries, ct).ConfigureAwait(false);

        _ctx.Logger.Success($"music landing + bgms index + {cuesBySeries.Count} シリーズ詳細");
    }

    /// <summary>
    /// <c>/music/</c> 音楽ランディング。歌（/songs/）・劇伴（/bgms/）・商品（/products/）の 3 入口を案内する。
    /// </summary>
    private void GenerateMusicLanding(
        int songsCount,
        int recordingsCount,
        int productsCount,
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        CancellationToken ct)
    {
        // 劇伴を持つシリーズ数 + 全劇伴音源件数（仮 M 番号は内部用なので集計から除外）
        int bgmSeriesCount = cuesBySeries.Count;
        int bgmCueTotal = cuesBySeries.Values
            .SelectMany(list => list)
            .Count(c => !c.IsTempMNo);

        var content = new MusicLandingModel
        {
            SongsCount = songsCount,
            RecordingsCount = recordingsCount,
            BgmSeriesCount = bgmSeriesCount,
            BgmCueTotal = bgmCueTotal,
            ProductsCount = productsCount
        };
        var layout = new LayoutModel
        {
            PageTitle = "音楽",
            MetaDescription = "プリキュアシリーズの楽曲・劇伴の索引。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "音楽", Url = "" }
            }
        };
        _page.RenderAndWrite("/music/", "music", "music-landing.sbn", content, layout);
    }

    /// <summary>
    /// <c>/bgms/</c> 劇伴シリーズ一覧。劇伴データを持つシリーズだけ並べる。
    /// </summary>
    private void GenerateBgmIndex(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries)
    {
        var rows = new List<BgmIndexRow>();
        foreach (var s in _ctx.Series.OrderBy(x => x.StartDate).ThenBy(x => x.SeriesId))
        {
            // 子作品（単独詳細ページなし）はそもそも単独ページが無いので、劇伴一覧でも表に出さない。
            // 仮に劇伴データが紐付いていても親シリーズ側の劇伴ページに統合される運用を想定。
            if (IsChildOfMovie(s)) continue;
            if (!cuesBySeries.TryGetValue(s.SeriesId, out var cues)) continue;

            // 仮 M 番号は閲覧 UI から見えないので件数集計から除外。
            int cueCount = cues.Count(c => !c.IsTempMNo);
            if (cueCount == 0) continue;

            int sessionCount = sessionsBySeries.TryGetValue(s.SeriesId, out var sess)
                ? sess.Count(x => x.SessionNo > 0)  // 0 番は「未設定」用なのでカウントしない
                : 0;

            rows.Add(new BgmIndexRow
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                SeriesPeriod = FormatPeriod(s.StartDate, s.EndDate),
                CueCount = cueCount,
                SessionCount = sessionCount
            });
        }

        var content = new BgmIndexModel { Rows = rows };
        var layout = new LayoutModel
        {
            PageTitle = "劇伴一覧",
            MetaDescription = "プリキュアシリーズの劇伴音源を作品別に一覧。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "劇伴", Url = "" }
            }
        };
        _page.RenderAndWrite("/bgms/", "music", "bgms-index.sbn", content, layout);
    }

    /// <summary>
    /// <c>/bgms/{slug}/</c> 1 シリーズあたりの劇伴詳細。録音セッション別に区切る。
    /// セッション内では <see cref="BgmCue.SeqInSession"/> 昇順、同値なら <see cref="MNoNaturalComparer"/> でタイブレーク。
    /// </summary>
    private async Task GenerateBgmDetailPagesAsync(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries,
        CancellationToken ct)
    {
        await Task.Yield();  // メソッドを async に保つためのダミー（将来 DB 追加クエリを足したときに困らないように）

        foreach (var (seriesId, cues) in cuesBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(seriesId, out var s)) continue;
            if (IsChildOfMovie(s)) continue;

            // 仮 M 番号は閲覧 UI に出さない。
            var visibleCues = cues.Where(c => !c.IsTempMNo).ToList();
            if (visibleCues.Count == 0) continue;

            // セッションマスタ（session_no → SessionName のマップ）
            var sessionMap = sessionsBySeries.TryGetValue(seriesId, out var sessList)
                ? sessList.ToDictionary(x => x.SessionNo, x => x)
                : new Dictionary<byte, BgmSession>();

            // セッションごとにグルーピング → セッション内では SeqInSession 昇順 → 同値時は m_no_detail 自然順
            var sessionGroups = visibleCues
                .GroupBy(c => c.SessionNo)
                .OrderBy(g => g.Key)
                .Select(g => new BgmSessionSection
                {
                    SessionNo = g.Key,
                    SessionName = sessionMap.TryGetValue(g.Key, out var session) ? session.SessionName : "(未設定)",
                    Cues = g
                        .OrderBy(c => c.SeqInSession)
                        .ThenBy(c => c.MNoDetail, MNoNaturalComparer.Instance)
                        .Select(c => new BgmCueRow
                        {
                            MNoDetail = c.MNoDetail,
                            MNoClass = c.MNoClass ?? "",
                            MenuTitle = c.MenuTitle ?? "",
                            ComposerName = c.ComposerName ?? "",
                            ArrangerName = c.ArrangerName ?? "",
                            LengthLabel = FormatLengthSeconds(c.LengthSeconds),
                            Notes = c.Notes ?? ""
                        })
                        .ToList()
                })
                .ToList();

            var content = new BgmDetailModel
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                SeriesPeriod = FormatPeriod(s.StartDate, s.EndDate),
                Sessions = sessionGroups,
                TotalCueCount = visibleCues.Count
            };
            var layout = new LayoutModel
            {
                PageTitle = $"劇伴 - {s.Title}",
                MetaDescription = $"{s.Title} の劇伴音源一覧。",
                Breadcrumbs = new[]
                {
                    new BreadcrumbItem { Label = "ホーム", Url = "/" },
                    new BreadcrumbItem { Label = "音楽", Url = "/music/" },
                    new BreadcrumbItem { Label = "劇伴", Url = "/bgms/" },
                    new BreadcrumbItem { Label = s.Title, Url = "" }
                }
            };
            _page.RenderAndWrite($"/bgms/{s.Slug}/", "music", "bgms-detail.sbn", content, layout);
        }
    }

    /// <summary>子作品判定（SeriesGenerator と同じロジック）。</summary>
    private static bool IsChildOfMovie(Series s)
    {
        if (!s.ParentSeriesId.HasValue) return false;
        if (string.Equals(s.KindCode, "SPIN-OFF", StringComparison.Ordinal)) return false;
        return true;
    }

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

    /// <summary>長さ（秒）を「m:ss」形式に整形。NULL のときは空文字。</summary>
    private static string FormatLengthSeconds(ushort? lengthSeconds)
    {
        if (!lengthSeconds.HasValue) return "";
        int total = lengthSeconds.Value;
        int min = total / 60;
        int sec = total % 60;
        return $"{min}:{sec:D2}";
    }

    // ─── テンプレ用 DTO 群 ───

    private sealed class MusicLandingModel
    {
        public int SongsCount { get; set; }
        public int RecordingsCount { get; set; }
        public int BgmSeriesCount { get; set; }
        public int BgmCueTotal { get; set; }
        /// <summary>
        /// 関連商品の総件数（v1.3.0 ブラッシュアップ続編で /music/ ランディングに 3 カテゴリ目として追加）。
        /// </summary>
        public int ProductsCount { get; set; }
    }

    private sealed class BgmIndexModel
    {
        public IReadOnlyList<BgmIndexRow> Rows { get; set; } = Array.Empty<BgmIndexRow>();
    }

    private sealed class BgmIndexRow
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesPeriod { get; set; } = "";
        public int CueCount { get; set; }
        public int SessionCount { get; set; }
    }

    private sealed class BgmDetailModel
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesPeriod { get; set; } = "";
        public IReadOnlyList<BgmSessionSection> Sessions { get; set; } = Array.Empty<BgmSessionSection>();
        public int TotalCueCount { get; set; }
    }

    private sealed class BgmSessionSection
    {
        public byte SessionNo { get; set; }
        public string SessionName { get; set; } = "";
        public IReadOnlyList<BgmCueRow> Cues { get; set; } = Array.Empty<BgmCueRow>();
    }

    private sealed class BgmCueRow
    {
        public string MNoDetail { get; set; } = "";
        public string MNoClass { get; set; } = "";
        public string MenuTitle { get; set; } = "";
        public string ComposerName { get; set; } = "";
        public string ArrangerName { get; set; } = "";
        public string LengthLabel { get; set; } = "";
        public string Notes { get; set; } = "";
    }
}
