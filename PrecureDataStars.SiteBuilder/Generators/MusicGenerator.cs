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
/// 仮 M 番号（<c>is_temp_m_no=1</c>）の音源の取り扱い
/// （v1.3.0 公開直前のデザイン整理第 2 弾で挙動変更）：
/// </para>
/// <list type="bullet">
///   <item><description>cue 自体は閲覧 UI に **表示する**（旧仕様の全削除を撤回）</description></item>
///   <item><description>M 番号セル・メニューセルは空欄（DB の <c>menu_title</c> は表示しない）</description></item>
///   <item><description>その代替として「収録盤情報のうち最初の盤のトラックタイトル」をメニュー位置に小さく表示</description></item>
///   <item><description>件数集計（一覧の音源数・/music/ ランディング・ホーム統計）にも 仮 M 番号 を含める</description></item>
/// </list>
/// <para>
/// 全 cue（仮・通常を問わず）について、メニューセル下段に小さい字で
/// 「収録盤タイトル | Tr.N | トラックタイトル」のリストを発売日昇順で列挙する。
/// 取得元は <c>tracks × discs × products</c> の JOIN を 1 度だけ実行して
/// (series_id, m_no_detail) で in-memory グルーピングする方式。
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

        // v1.3.0 公開直前のデザイン整理第 2 弾：劇伴 cue ごとの「収録盤情報」を 1 度だけ
        // tracks × discs × products の JOIN クエリで取得し、(series_id, m_no_detail) で
        // in-memory グルーピングする。/bgms/{slug}/ 詳細の各 cue 行で、メニューセル下段に
        // 「収録盤タイトル | Tr.N | トラックタイトル」のリストを発売日昇順で列挙するために使う。
        // 仮 M 番号 cue では先頭の「トラックタイトル」をメニューの代替として表示するので、
        // ここで取った発売日昇順の並びがそのまま代替表示の優先順位となる。
        var recordingsByBgmCue = await LoadBgmCueRecordingsAsync(ct).ConfigureAwait(false);

        GenerateMusicLanding(allRecs.Count, allProducts.Count, discsCount, cuesBySeries, ct);
        GenerateBgmIndex(cuesBySeries, sessionsBySeries, useCountByBgmCue);
        await GenerateBgmDetailPagesAsync(cuesBySeries, sessionsBySeries, useCountByBgmCue, recordingsByBgmCue, ct).ConfigureAwait(false);

        _ctx.Logger.Success($"music landing + bgms index + {cuesBySeries.Count} シリーズ詳細");
    }

    /// <summary>
    /// 全 bgm_cue の収録盤情報を 1 度の JOIN クエリで取得し、(series_id, m_no_detail) で
    /// グルーピングして返す（v1.3.0 公開直前のデザイン整理第 2 弾で追加）。
    /// <para>
    /// 並び順は発売日昇順（同一発売日は product_catalog_no、track_no、sub_order で安定タイブレーク）。
    /// 仮 M 番号 cue 用の代替表示でもこの並び順が「最初に収録された盤」決定の根拠となる。
    /// </para>
    /// <para>
    /// 紐付き元は <c>tracks.bgm_series_id</c> + <c>tracks.bgm_m_no_detail</c> の複合 FK（discs 経由で products へ）。
    /// products.is_deleted=0 のレコードのみ拾う（論理削除済み商品の収録は表示対象外）。
    /// </para>
    /// </summary>
    private async Task<IReadOnlyDictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>>>
        LoadBgmCueRecordingsAsync(CancellationToken ct)
    {
        // tracks.track_title_override が NULL のときは空文字に倒す（表示はテンプレ側で吸収）。
        // ORDER BY は安定タイブレーク前提（発売日 → 品番 → トラック番号 → サブ順）。
        const string sql = """
            SELECT t.bgm_series_id     AS SeriesId,
                   t.bgm_m_no_detail   AS MNoDetail,
                   p.product_catalog_no AS ProductCatalogNo,
                   p.title             AS ProductTitle,
                   p.release_date      AS ReleaseDate,
                   t.track_no          AS TrackNo,
                   t.sub_order         AS SubOrder,
                   COALESCE(t.track_title_override, '') AS TrackTitle
              FROM tracks t
              JOIN discs d    ON d.catalog_no = t.catalog_no
              JOIN products p ON p.product_catalog_no = d.product_catalog_no
             WHERE t.bgm_series_id IS NOT NULL
               AND t.bgm_m_no_detail IS NOT NULL
               AND p.is_deleted = 0
             ORDER BY p.release_date ASC,
                      p.product_catalog_no ASC,
                      t.track_no ASC,
                      t.sub_order ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<BgmCueRecordingRaw>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var dict = new Dictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>>();
        foreach (var r in rows)
        {
            var key = (r.SeriesId, r.MNoDetail);
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<BgmCueRecording>();
                dict[key] = list;
            }
            list.Add(new BgmCueRecording
            {
                ProductCatalogNo = r.ProductCatalogNo,
                // 商品タイトルは表示用に短縮形に整形する
                // （v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
                // 長い「○○プリキュア オリジナル・サウンドトラックN サブタイトル」を、
                // 「サントラN『サブタイトル』」相当に詰める。フル表記は ProductTitleFull に保持。
                ProductTitle = ShortenProductTitle(r.ProductTitle),
                ProductTitleFull = r.ProductTitle,
                TrackNo = r.TrackNo,
                SubOrder = r.SubOrder,
                TrackTitle = r.TrackTitle
            });
        }
        return dict;
    }

    /// <summary>
    /// 商品タイトルを表示用に短縮する
    /// （v1.3.0 公開直前のデザイン整理第 4 弾で追加）。
    /// <para>
    /// 想定される長尺パターンと変換例：
    /// </para>
    /// <list type="bullet">
    ///   <item><description>「トロピカル～ジュ!プリキュア オリジナル・サウンドトラック1 プリキュア・トロピカル・サウンド!!」
    ///     → 「サントラ1『プリキュア・トロピカル・サウンド!!』」</description></item>
    ///   <item><description>「ふたりはプリキュア オリジナル・サウンドトラック」
    ///     → 「サントラ」</description></item>
    ///   <item><description>「○○プリキュア オリジナル・サウンドトラック2」
    ///     → 「サントラ2」</description></item>
    /// </list>
    /// <para>
    /// 正規表現で 3 段階に処理する：
    /// (1) 先頭「○○プリキュア オリジナル・」（プリキュアシリーズ識別子）を削除、
    /// (2) 「サウンドトラック」を「サントラ」に置換、
    /// (3) 後続のサブタイトル（空白区切りの残り）を『』で囲む。
    /// </para>
    /// <para>
    /// パターンに一致しない商品タイトル（シングル / マキシ / イメージアルバム等）は
    /// 元の文字列をそのまま返す（変な省略をして意味を壊さないため）。
    /// </para>
    /// </summary>
    private static string ShortenProductTitle(string fullTitle)
    {
        if (string.IsNullOrEmpty(fullTitle)) return fullTitle;

        // 「○○プリキュア オリジナル・サウンドトラックN」の正規パターン。
        // プリキュアシリーズ名は「プリキュア」を含む長い前置詞、空白 1 つ以上、「オリジナル・サウンドトラック」、
        // 数字（任意）、空白 + サブタイトル（任意）の構造。
        // 末尾の「!!」「♪」「☆」などの装飾文字は通すために [\s\S]* で受け流す。
        var m = System.Text.RegularExpressions.Regex.Match(
            fullTitle,
            @"^.*?プリキュア\s+オリジナル・サウンドトラック(?<num>\d*)(?:\s+(?<sub>.+))?$");
        if (!m.Success) return fullTitle;

        string num = m.Groups["num"].Value;
        string sub = m.Groups["sub"].Value.Trim();
        string head = "サントラ" + num;
        if (string.IsNullOrEmpty(sub)) return head;
        return $"{head}『{sub}』";
    }

    /// <summary>
    /// <c>/music/</c> 音楽ランディング。歌（/songs/）・劇伴（/bgms/）・音楽商品（/products/）の 3 入口を案内する。
    /// v1.3.0 ブラッシュアップ続編：各カードを 1 バッジ構成に統一、商品カードは「N点 M枚」表記、
    /// 「歌」バッジは song_recordings 件数（ホーム統計と整合）。
    /// v1.3.0 公開直前のデザイン整理第 2 弾：劇伴件数は仮 M 番号も含めた全件カウントに変更
    /// （仮 M 番号 cue も閲覧 UI に表示する方針に統一したため）。
    /// </summary>
    private void GenerateMusicLanding(
        int recordingsCount,
        int productsCount,
        int discsCount,
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        CancellationToken ct)
    {
        // 全劇伴音源件数。仮 M 番号も含める（閲覧 UI には表示するが、メニュー欄を空欄にして
        // 代替表示する方針なので、件数集計にも含めるのが整合する）。
        int bgmCueTotal = cuesBySeries.Values
            .SelectMany(list => list)
            .Count();

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
    /// v1.3.0 公開直前のデザイン整理第 2 弾：仮 M 番号 cue を集計から除外していた処理を撤廃
    /// （仮 M 番号 cue も閲覧 UI に表示するため、件数も整合させる）。
    /// </summary>
    private void GenerateBgmIndex(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), int> useCountByBgmCue)
    {
        var rows = new List<BgmIndexRow>();
        foreach (var s in _ctx.Series.OrderBy(x => x.StartDate).ThenBy(x => x.SeriesId))
        {
            // 子作品（'MOVIE_SHORT'）は単独詳細ページを持たない運用なので、劇伴一覧でも表に出さない。
            // 劇伴データが紐付いていても親シリーズ側の劇伴ページに統合される運用を想定。
            if (IsChildOfMovie(s)) continue;
            if (!cuesBySeries.TryGetValue(s.SeriesId, out var cues)) continue;

            // 仮 M 番号も表示対象に含めるので、件数は全 cue 数。
            int cueCount = cues.Count;
            if (cueCount == 0) continue;

            int sessionCount = sessionsBySeries.TryGetValue(s.SeriesId, out var sess)
                ? sess.Count(x => x.SessionNo > 0)  // 0 番は「未設定」用なのでカウントしない
                : 0;

            // 当該シリーズの使用回数合計：全 cue（仮 M 番号も含む）に紐付く episode_uses の行数を合算。
            // useCountByBgmCue は (series_id, m_no_detail) → 行数 の事前集計テーブル。
            int useCount = cues.Sum(c =>
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
    /// v1.3.0 公開直前のデザイン整理第 2 弾：
    /// <list type="bullet">
    ///   <item><description>仮 M 番号 cue を非表示にしていた処理を撤廃（cue 自体は表示するが M 番号・メニューは空欄）</description></item>
    ///   <item><description>仮 M 番号 cue のメニュー位置に「最初の収録盤のトラックタイトル」を代替表示</description></item>
    ///   <item><description>全 cue にメニューセル下段で収録盤情報（収録盤・トラック・タイトル）を発売日昇順で列挙</description></item>
    /// </list>
    /// </summary>
    private async Task GenerateBgmDetailPagesAsync(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), int> useCountByBgmCue,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>> recordingsByBgmCue,
        CancellationToken ct)
    {
        await Task.Yield();  // メソッドを async に保つためのダミー（将来 DB 追加クエリを足したときに困らないように）

        foreach (var (seriesId, cues) in cuesBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(seriesId, out var s)) continue;
            if (IsChildOfMovie(s)) continue;

            // 仮 M 番号 cue も含めて全 cue を表示対象とする
            // （旧仕様の .Where(c => !c.IsTempMNo) は撤廃）。
            if (cues.Count == 0) continue;

            // セッションマスタ（session_no → SessionName のマップ）
            var sessionMap = sessionsBySeries.TryGetValue(seriesId, out var sessList)
                ? sessList.ToDictionary(x => x.SessionNo, x => x)
                : new Dictionary<byte, BgmSession>();

            // セッションごとにグルーピング → セッション内では SeqInSession 昇順 → 同値時は m_no_detail 自然順
            var sessionGroups = cues
                .GroupBy(c => c.SessionNo)
                .OrderBy(g => g.Key)
                .Select(g => new BgmSessionSection
                {
                    SessionNo = g.Key,
                    SessionName = sessionMap.TryGetValue(g.Key, out var session) ? session.SessionName : "(未設定)",
                    Cues = g
                        .OrderBy(c => c.SeqInSession)
                        .ThenBy(c => c.MNoDetail, MNoNaturalComparer.Instance)
                        .Select(c =>
                        {
                            // 当該 cue の収録盤情報リスト（発売日昇順、なければ空リスト）。
                            var recs = recordingsByBgmCue.TryGetValue((seriesId, c.MNoDetail), out var list)
                                ? list
                                : new List<BgmCueRecording>();

                            // 仮 M 番号 cue の場合は M 番号セル・メニューセルともに空欄。
                            // メニューの代替として、最先頭の収録盤のトラックタイトルを使う
                            // （recs は既に発売日昇順で並んでいるので最初の要素が「最初に収録された盤」）。
                            string mNoCell = c.IsTempMNo ? "" : c.MNoDetail;
                            string menuCell;
                            string menuFallback = "";
                            if (c.IsTempMNo)
                            {
                                // 仮 M 番号：メニューは空欄、代替表示用に最初の収録盤トラックタイトルを別フィールドへ。
                                menuCell = "";
                                if (recs.Count > 0) menuFallback = recs[0].TrackTitle;
                            }
                            else
                            {
                                // 通常 cue：DB の menu_title をそのまま採用。
                                menuCell = c.MenuTitle ?? "";
                            }

                            return new BgmCueRow
                            {
                                MNoDetail = mNoCell,
                                MNoClass = c.MNoClass ?? "",
                                IsTempMNo = c.IsTempMNo,
                                MenuTitle = menuCell,
                                MenuFallbackTitle = menuFallback,
                                ComposerName = c.ComposerName ?? "",
                                ArrangerName = c.ArrangerName ?? "",
                                LengthLabel = FormatLengthSeconds(c.LengthSeconds),
                                Notes = c.Notes ?? "",
                                // v1.3.0 ブラッシュアップ続編：cue 単位の使用回数。
                                // (series_id, m_no_detail) で事前集計テーブルを引き、ヒットしなければ 0。
                                UseCount = useCountByBgmCue.TryGetValue((seriesId, c.MNoDetail), out var n) ? n : 0,
                                Recordings = recs
                            };
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
                TotalCueCount = cues.Count
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

    /// <summary>
    /// 子作品判定（SeriesGenerator と同じロジック、v1.3.0 公開直前のデザイン整理第 2 弾で仕様確定）。
    /// 'MOVIE_SHORT' のみが子作品扱いとなる（単独詳細ページを生成しない）。
    /// </summary>
    private static bool IsChildOfMovie(Series s)
        => string.Equals(s.KindCode, "MOVIE_SHORT", StringComparison.Ordinal);

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
        /// <summary>劇伴の件数（bgm_cues 行数、仮 M 番号も含めた全件）。</summary>
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
        /// シリーズ全 cue（仮 M 番号も含む）の使用回数合計（episode_uses の行数ベース、重複カウント）。
        /// v1.3.0 ブラッシュアップ続編で追加、v1.3.0 公開直前のデザイン整理第 2 弾で仮 M 番号も含めるよう変更。
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
        /// <summary>M 番号セルの表示値。仮 M 番号 cue の場合は空文字。</summary>
        public string MNoDetail { get; set; } = "";
        public string MNoClass { get; set; } = "";
        /// <summary>
        /// 仮 M 番号フラグ（v1.3.0 公開直前のデザイン整理第 2 弾で追加）。
        /// テンプレ側で行スタイルやメニュー欄の表示分岐に使う。
        /// </summary>
        public bool IsTempMNo { get; set; }
        /// <summary>メニュー（曲名）セルの表示値。仮 M 番号 cue では空文字。</summary>
        public string MenuTitle { get; set; } = "";
        /// <summary>
        /// 仮 M 番号 cue のメニュー代替表示（最初の収録盤のトラックタイトル）。
        /// 通常 cue では空文字（MenuTitle 側で表示済みのため）。
        /// </summary>
        public string MenuFallbackTitle { get; set; } = "";
        public string ComposerName { get; set; } = "";
        public string ArrangerName { get; set; } = "";
        public string LengthLabel { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>
        /// この cue（M 番号）の使用回数（episode_uses の (bgm_series_id, bgm_m_no_detail) 一致行の件数、
        /// 重複カウント）。v1.3.0 ブラッシュアップ続編で追加。
        /// </summary>
        public int UseCount { get; set; }
        /// <summary>
        /// 収録盤情報のリスト（発売日昇順、v1.3.0 公開直前のデザイン整理第 2 弾で追加）。
        /// メニューセル下段に小さい字で「収録盤タイトル | Tr.N | トラックタイトル」を列挙する。
        /// 0 件のときはテンプレ側で表示自体を省略する。
        /// </summary>
        public IReadOnlyList<BgmCueRecording> Recordings { get; set; } = Array.Empty<BgmCueRecording>();
    }

    /// <summary>
    /// 劇伴 cue × 収録盤の 1 行（v1.3.0 公開直前のデザイン整理第 2 弾で新設、
    /// 第 4 弾で <see cref="ProductTitleFull"/> を追加）。
    /// tracks × discs × products の JOIN 結果から、cue ごとに発売日昇順で列挙される。
    /// </summary>
    private sealed class BgmCueRecording
    {
        public string ProductCatalogNo { get; set; } = "";
        /// <summary>
        /// 表示用に短縮された商品タイトル（例「サントラ1『プリキュア・トロピカル・サウンド!!』」）。
        /// 短縮ロジック非適用の商品（シングル等）は元のタイトルがそのまま入る。
        /// </summary>
        public string ProductTitle { get; set; } = "";
        /// <summary>
        /// products.title の元の値（短縮前のフル表記）。
        /// テンプレ側で <c>&lt;a title="..."&gt;</c> 属性に詰めてホバー時にフル名を見せる用途。
        /// </summary>
        public string ProductTitleFull { get; set; } = "";
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        public string TrackTitle { get; set; } = "";
    }

    /// <summary>Dapper マッピング用の生 SELECT 行（<see cref="LoadBgmCueRecordingsAsync"/> 用）。</summary>
    private sealed class BgmCueRecordingRaw
    {
        public int SeriesId { get; set; }
        public string MNoDetail { get; set; } = "";
        public string ProductCatalogNo { get; set; } = "";
        public string ProductTitle { get; set; } = "";
        public DateTime ReleaseDate { get; set; }
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        public string TrackTitle { get; set; } = "";
    }
}
