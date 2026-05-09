
using Dapper;
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
    /// <summary>
    /// 歌の件数表示用（v1.3.0 ブラッシュアップ続編で /music/ ランディングのバッジを「曲」単位に統一）。
    /// 旧仕様では SongsRepository の楽曲件数と SongRecordingsRepository の録音件数を別バッジで出していたが、
    /// ホーム画面の統計と整合させるため song_recordings 件数 1 本に絞った。
    /// </summary>
    private readonly SongRecordingsRepository _recRepo;
    /// <summary>
    /// 商品件数（/music/ ランディングの「音楽商品」カードの「N点 M枚」点数側）取得用。
    /// 全件取得して Count するだけなので軽量。
    /// </summary>
    private readonly ProductsRepository _productsRepo;
    /// <summary>
    /// 劇伴使用回数（/bgms/ 一覧の「使用回数」列、/bgms/{slug}/ 詳細の cue 表「使用回数」列）取得用。
    /// v1.3.0 ブラッシュアップ続編で追加。GetAllAsync で全件をいったんメモリに載せて、
    /// (bgm_series_id, bgm_m_no_detail) 単位でグルーピングして集計する。
    /// </summary>
    private readonly EpisodeUsesRepository _episodeUsesRepo;

    public MusicGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _cuesRepo = new BgmCuesRepository(factory);
        _sessionsRepo = new BgmSessionsRepository(factory);
        _recRepo = new SongRecordingsRepository(factory);
        _productsRepo = new ProductsRepository(factory);
        _episodeUsesRepo = new EpisodeUsesRepository(factory);
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

        // 歌録音は集計件数表示用にロード（軽量、song_recordings の全件 = /music/ の「歌」バッジ）。
        var allRecs = await _recRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        // 音楽商品（/music/ ランディングの「音楽商品」カード「N点 M枚」表記用）。
        // 商品 = 音楽商品 のみ運用方針なのでフィルタは不要。
        var allProducts = await _productsRepo.GetAllAsync(includeDeleted: false, ct).ConfigureAwait(false);
        // 枚数（discs 件数）は Repository に GetAllAsync が無いので SQL で直接 COUNT(*)。
        // ホームの統計取得（HomeGenerator）と同じパターン。
        int discsCount;
        await using (var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false))
        {
            discsCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM discs WHERE is_deleted = 0;",
                cancellationToken: ct)).ConfigureAwait(false);
        }

        // v1.3.0 ブラッシュアップ続編：episode_uses から劇伴の使用回数を集計。
        // (bgm_series_id, bgm_m_no_detail) → 行数 の Dictionary を 1 度だけ作って、
        // /bgms/ 一覧の合計と /bgms/{slug}/ 詳細の cue 単位の両方で参照する。
        // カウントルールは「行数（重複カウント）」。同一エピソードで同一 cue が複数回使われれば
        // その回数分だけカウント（DISTINCT episode_id ではない）。
        var allEpisodeUses = await _episodeUsesRepo.GetAllAsync(ct).ConfigureAwait(false);
        var useCountByBgmCue = allEpisodeUses
            .Where(u => u.BgmSeriesId.HasValue && !string.IsNullOrEmpty(u.BgmMNoDetail))
            .GroupBy(u => (SeriesId: u.BgmSeriesId!.Value, MNoDetail: u.BgmMNoDetail!))
            .ToDictionary(g => g.Key, g => g.Count());

        GenerateMusicLanding(allRecs.Count, allProducts.Count, discsCount, cuesBySeries, ct);
        GenerateBgmIndex(cuesBySeries, sessionsBySeries, useCountByBgmCue);
        await GenerateBgmDetailPagesAsync(cuesBySeries, sessionsBySeries, useCountByBgmCue, ct).ConfigureAwait(false);

        _ctx.Logger.Success($"music landing + bgms index + {cuesBySeries.Count} シリーズ詳細");
    }

    /// <summary>
    /// <c>/music/</c> 音楽ランディング。歌（/songs/）・劇伴（/bgms/）・音楽商品（/products/）の 3 入口を案内する。
    /// v1.3.0 ブラッシュアップ続編：各カードを 1 バッジ構成に統一、商品カードは「N点 M枚」表記、
    /// 「歌」バッジは song_recordings 件数（ホーム統計と整合）。
    /// </summary>
    private void GenerateMusicLanding(
        int recordingsCount,
        int productsCount,
        int discsCount,
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        CancellationToken ct)
    {
        // 全劇伴音源件数（仮 M 番号は内部用なので集計から除外）。
        int bgmCueTotal = cuesBySeries.Values
            .SelectMany(list => list)
            .Count(c => !c.IsTempMNo);

        var content = new MusicLandingModel
        {
            // 「歌」バッジは song_recordings 件数。SongsRepository の楽曲件数（song_id 単位）ではなく
            // レコーディング単位（recording 単位）を採用するのは、ホーム画面のデータベース統計と整合させるため。
            SongsCount = recordingsCount,
            BgmCueTotal = bgmCueTotal,
            // 「N点 M枚」事前整形（テンプレ側で再組立しなくて済むように）。半角スペース 1 個。
            MusicProductsLabel = $"{productsCount}点 {discsCount}枚"
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
    /// v1.3.0 ブラッシュアップ続編：episode_uses 経由の「使用回数」列を追加。
    /// シリーズの使用回数合計 = 当該シリーズの閲覧可能な cue（仮 M 番号除外）の使用回数の総和。
    /// </summary>
    private void GenerateBgmIndex(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), int> useCountByBgmCue)
    {
        var rows = new List<BgmIndexRow>();
        foreach (var s in _ctx.Series.OrderBy(x => x.StartDate).ThenBy(x => x.SeriesId))
        {
            // 子作品（単独詳細ページなし）はそもそも単独ページが無いので、劇伴一覧でも表に出さない。
            // 仮に劇伴データが紐付いていても親シリーズ側の劇伴ページに統合される運用を想定。
            if (IsChildOfMovie(s)) continue;
            if (!cuesBySeries.TryGetValue(s.SeriesId, out var cues)) continue;

            // 仮 M 番号は閲覧 UI から見えないので件数集計から除外。
            var visibleCues = cues.Where(c => !c.IsTempMNo).ToList();
            int cueCount = visibleCues.Count;
            if (cueCount == 0) continue;

            int sessionCount = sessionsBySeries.TryGetValue(s.SeriesId, out var sess)
                ? sess.Count(x => x.SessionNo > 0)  // 0 番は「未設定」用なのでカウントしない
                : 0;

            // 当該シリーズの使用回数合計：閲覧可能な cue（仮 M 番号除外）に紐付く episode_uses の行数を合算。
            // useCountByBgmCue は (series_id, m_no_detail) → 行数 の事前集計テーブル。
            int useCount = visibleCues.Sum(c =>
                useCountByBgmCue.TryGetValue((s.SeriesId, c.MNoDetail), out var n) ? n : 0);

            rows.Add(new BgmIndexRow
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                SeriesPeriod = FormatPeriod(s.StartDate, s.EndDate),
                CueCount = cueCount,
                SessionCount = sessionCount,
                UseCount = useCount
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
    /// v1.3.0 ブラッシュアップ続編：cue 表に episode_uses 経由の「使用回数」列を追加。
    /// </summary>
    private async Task GenerateBgmDetailPagesAsync(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), int> useCountByBgmCue,
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
                            Notes = c.Notes ?? "",
                            // v1.3.0 ブラッシュアップ続編：cue 単位の使用回数。
                            // (series_id, m_no_detail) で事前集計テーブルを引き、ヒットしなければ 0。
                            UseCount = useCountByBgmCue.TryGetValue((seriesId, c.MNoDetail), out var n) ? n : 0
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

    /// <summary>
    /// /music/ 音楽ランディングのテンプレ用モデル（v1.3.0 ブラッシュアップ続編で 4 プロパティに整理）。
    /// 歌・劇伴・音楽商品の 3 カードに 1 バッジずつを表示するためのデータ。
    /// </summary>
    private sealed class MusicLandingModel
    {
        /// <summary>歌の件数（song_recordings 行数、楽曲のレコーディング単位）。</summary>
        public int SongsCount { get; set; }
        /// <summary>劇伴の件数（bgm_cues 行数、仮 M 番号を除外した閲覧可能件数）。</summary>
        public int BgmCueTotal { get; set; }
        /// <summary>「N点 M枚」整形済み文字列（点数 = products、枚数 = discs を 1 セルに集約）。</summary>
        public string MusicProductsLabel { get; set; } = "";
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
        /// <summary>
        /// シリーズ全 cue（仮 M 番号除外）の使用回数合計（episode_uses の行数ベース、重複カウント）。
        /// v1.3.0 ブラッシュアップ続編で追加。
        /// </summary>
        public int UseCount { get; set; }
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
        /// <summary>
        /// この cue（M 番号）の使用回数（episode_uses の (bgm_series_id, bgm_m_no_detail) 一致行の件数、
        /// 重複カウント）。v1.3.0 ブラッシュアップ続編で追加。
        /// </summary>
        public int UseCount { get; set; }
    }
}
