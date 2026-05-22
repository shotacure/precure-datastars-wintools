using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.Data.Repositories;
using PrecureDataStars.SiteBuilder.Pipeline;
using PrecureDataStars.SiteBuilder.Rendering;
using PrecureDataStars.SiteBuilder.Utilities;

namespace PrecureDataStars.SiteBuilder.Generators;

/// <summary>音楽カテゴリのページ群を生成する。</summary>
public sealed class MusicGenerator
{
    private readonly BuildContext _ctx;
    private readonly PageRenderer _page;
    private readonly IConnectionFactory _factory;

    private readonly BgmCuesRepository _cuesRepo;
    private readonly BgmSessionsRepository _sessionsRepo;
    /// <summary>歌の件数表示用（/music/ ランディングのバッジを「曲」単位に統一）。 件数バッジについて、 ホーム画面の統計と整合させるため song_recordings 件数 1 本に絞った。</summary>
    private readonly SongRecordingsRepository _recRepo;
    /// <summary>商品件数（/music/ ランディングの「音楽商品」カードの「N点 M枚」点数側）取得用。 全件取得して Count するだけなので軽量。</summary>
    private readonly ProductsRepository _productsRepo;

    public MusicGenerator(BuildContext ctx, PageRenderer page, IConnectionFactory factory)
    {
        _ctx = ctx;
        _page = page;
        _factory = factory;
        _cuesRepo = new BgmCuesRepository(factory);
        _sessionsRepo = new BgmSessionsRepository(factory);
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

        // 劇伴 cue ごとの「収録盤情報」を 1 度だけ
        // tracks × discs × products の JOIN クエリで取得し、(series_id, m_no_detail) で
        // in-memory グルーピングする。/bgms/{slug}/ 詳細の各 cue カードで、収録盤リスト（disc.title_short
        // ｜Tr.N｜トラックタイトル）を発売日昇順で列挙するために使う。
        // 仮 M 番号 cue では先頭の「トラックタイトル」をメニューの代替として表示するので、
        // ここで取った発売日昇順の並びがそのまま代替表示の優先順位となる。
        // また、カードヘッダに出す「尺」はこのリストの先頭（初出盤）トラックの LengthSeconds を採用する。
        var recordingsByBgmCue = await LoadBgmCueRecordingsAsync(ct).ConfigureAwait(false);

        // 劇伴 cue × 役職（COMPOSITION / ARRANGEMENT）ごとの「名義クレジット」を 1 度の
        // JOIN クエリで取得し、(series_id, m_no_detail, role) でグルーピングする。
        // /bgms/ 一覧のスタッフバッジ集計と、/bgms/{slug}/ 詳細カードのスタッフバッジ表示の両方で使う。
        // 名義 → 人物 が 1:1 に絞れる場合に限り PersonId を持たせて、人物詳細ページへのリンクを張る。
        var creditAliasesByBgmCue = await LoadBgmCueCreditAliasesAsync(ct).ConfigureAwait(false);

        GenerateMusicLanding(allRecs.Count, allProducts.Count, discsCount, cuesBySeries, ct);
        GenerateBgmIndex(cuesBySeries, creditAliasesByBgmCue);
        await GenerateBgmDetailPagesAsync(cuesBySeries, sessionsBySeries, recordingsByBgmCue, creditAliasesByBgmCue, ct).ConfigureAwait(false);

        _ctx.Logger.Success($"music landing + bgms index + {cuesBySeries.Count} シリーズ詳細");
    }

    /// <summary>全 bgm_cue の収録盤情報を 1 度の JOIN クエリで取得し、(series_id, m_no_detail) で グルーピングして返す。</summary>
    private async Task<IReadOnlyDictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>>>
        LoadBgmCueRecordingsAsync(CancellationToken ct)
    {
        // tracks.track_title_override が NULL のときは空文字に倒す（表示はテンプレ側で吸収）。
        // discs.title_short は収録盤の短縮表示用タイトル（劇伴詳細ページのカード内収録盤行で使う）。
        // 複数枚商品でディスクの識別子は title_short（例「OST1」「OST1(旧)」）の中に既に含まれている
        // 想定なので、表示テキストへのディスク序数の付加は行わない。一方 URL アンカーの一意性確保には
        // disc 単位 catalog_no が必要（同一商品の複数 disc で track_no は独立採番されるため、
        // ページ全体での一意な id を組み立てるには disc を識別する自然キーが要る）ので、
        // t.catalog_no を DiscCatalogNo として取り出す。アンカー URL は
        // /products/{ProductCatalogNo}/#track-{DiscCatalogNo}-{TrackNo}-{SubOrder} 形式で組み立てる。
        // tracks.length_frames は CD のフレーム単位尺（75 frames = 1 秒）。劇伴詳細ページの
        // cue カードヘッダに出す「尺」は、リスト先頭（発売日昇順で最も古い = 初出盤）の
        // トラック length_frames を秒に換算して表示する。
        // 特例：MJCG-80146（プリキュア「全曲集 1」）、MJCG-83027（同 2）は寄せ集めの曲集で
        // 各シリーズの収録盤として案内すると煩雑になるため、歌・劇伴の詳細ページの
        // 収録盤一覧から除外する（歌側の収録盤集計ロジックでも同じ品番を除外する）。
        // ORDER BY は安定タイブレーク前提（発売日 → 品番 → トラック番号 → サブ順）。
        const string sql = """
            SELECT t.bgm_series_id     AS SeriesId,
                   t.bgm_m_no_detail   AS MNoDetail,
                   p.product_catalog_no AS ProductCatalogNo,
                   p.title             AS ProductTitle,
                   t.catalog_no        AS DiscCatalogNo,
                   COALESCE(d.title_short, '') AS DiscTitleShort,
                   p.release_date      AS ReleaseDate,
                   t.track_no          AS TrackNo,
                   t.sub_order         AS SubOrder,
                   COALESCE(t.track_title_override, '') AS TrackTitle,
                   t.length_frames     AS LengthFrames
              FROM tracks t
              JOIN discs d    ON d.catalog_no = t.catalog_no
              JOIN products p ON p.product_catalog_no = d.product_catalog_no
             WHERE t.bgm_series_id IS NOT NULL
               AND t.bgm_m_no_detail IS NOT NULL
               AND p.is_deleted = 0
               AND p.product_catalog_no NOT IN ('MJCG-80146', 'MJCG-83027')
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
            // length_frames を秒に換算（75 frames = 1 秒）。NULL や 0 は LengthSeconds = null とする。
            int? lengthSeconds = null;
            if (r.LengthFrames is uint lf && lf > 0)
            {
                lengthSeconds = (int)Math.Round(lf / 75.0);
            }
            list.Add(new BgmCueRecording
            {
                ProductCatalogNo = r.ProductCatalogNo,
                // 商品タイトルは表示用に短縮形に整形する（旧来表示用、後方互換）。
                ProductTitle = ShortenProductTitle(r.ProductTitle),
                ProductTitleFull = r.ProductTitle,
                // 収録盤（ディスク）短縮タイトル。劇伴詳細のカード内では商品タイトルではなく
                // こちらを表示テキストにする（盤単位の簡潔な識別子になる）。
                DiscTitleShort = r.DiscTitleShort,
                // disc 単位 catalog_no。アンカー URL のトラック行特定キーに使う
                // （/products/{ProductCatalogNo}/#track-{DiscCatalogNo}-{TrackNo}-{SubOrder}）。
                DiscCatalogNo = r.DiscCatalogNo,
                TrackNo = r.TrackNo,
                SubOrder = r.SubOrder,
                TrackTitle = r.TrackTitle,
                LengthSeconds = lengthSeconds
            });
        }
        return dict;
    }

    /// <summary>
    /// 全 bgm_cue_credits を 1 度の JOIN クエリで取得し、(series_id, m_no_detail, role) ごとに
    /// 名義クレジット列を返す。各エントリは AliasId、表示テキスト（DisplayTextOverride 優先、
    /// 無ければ Name）、そして名義の所属人物が 1 人に絞れる場合のみ PersonId を持つ。
    /// 共同名義（person_alias_persons 上で複数 person を持つ alias）の場合は PersonId は null になり、
    /// /bgms/ 一覧では人物詳細リンクを張らずに表示テキストだけを出す。
    /// </summary>
    private async Task<IReadOnlyDictionary<(int SeriesId, string MNoDetail, string Role), List<BgmCueCreditAlias>>>
        LoadBgmCueCreditAliasesAsync(CancellationToken ct)
    {
        // person_alias_persons を LEFT JOIN するため、共同名義の alias は同じ (series_id, m_no_detail, role, credit_seq)
        // に対して複数行が出る。Generator 側で credit_seq でグルーピングしつつ person_id を集約する。
        const string sql = """
            SELECT bcc.series_id        AS SeriesId,
                   bcc.m_no_detail      AS MNoDetail,
                   bcc.credit_role      AS CreditRole,
                   bcc.credit_seq       AS CreditSeq,
                   bcc.person_alias_id  AS AliasId,
                   COALESCE(pa.display_text_override, pa.name) AS DisplayText,
                   pap.person_id        AS PersonId
              FROM bgm_cue_credits bcc
              JOIN person_aliases pa
                ON pa.alias_id = bcc.person_alias_id
              LEFT JOIN person_alias_persons pap
                ON pap.alias_id = bcc.person_alias_id
             ORDER BY bcc.series_id ASC,
                      bcc.m_no_detail ASC,
                      bcc.credit_role ASC,
                      bcc.credit_seq ASC;
            """;

        await using var conn = await _factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<BgmCueCreditAliasRaw>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // (key + credit_seq) 単位で person_id を集約：person 1 人に絞れる alias は PersonId 確定、
        // 複数 person が出る alias は PersonId = null（リンク不可）として扱う。
        var aggregate = new Dictionary<(int, string, string, byte), BgmCueCreditAlias>();
        foreach (var r in rows)
        {
            var key = (r.SeriesId, r.MNoDetail, r.CreditRole, r.CreditSeq);
            if (!aggregate.TryGetValue(key, out var entry))
            {
                entry = new BgmCueCreditAlias
                {
                    AliasId = r.AliasId,
                    DisplayText = r.DisplayText,
                    PersonId = r.PersonId,           // 最初の person_id を仮セット
                    PersonCount = r.PersonId.HasValue ? 1 : 0
                };
                aggregate[key] = entry;
            }
            else
            {
                // 同じ credit_seq で別 person_id が来た = 共同名義。PersonId を null に倒し、
                // PersonCount をインクリメント（ログ用途）。
                if (r.PersonId.HasValue)
                {
                    entry.PersonCount++;
                    if (entry.PersonCount >= 2) entry.PersonId = null;
                }
            }
        }

        // (series_id, m_no_detail, role) → List<BgmCueCreditAlias>（credit_seq 昇順）にまとめ直す。
        var dict = new Dictionary<(int SeriesId, string MNoDetail, string Role), List<BgmCueCreditAlias>>();
        foreach (var kv in aggregate.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2).ThenBy(kv => kv.Key.Item3).ThenBy(kv => kv.Key.Item4))
        {
            var listKey = (kv.Key.Item1, kv.Key.Item2, kv.Key.Item3);
            if (!dict.TryGetValue(listKey, out var list))
            {
                list = new List<BgmCueCreditAlias>();
                dict[listKey] = list;
            }
            list.Add(kv.Value);
        }
        return dict;
    }

    /// <summary>Dapper マッピング用の生 SELECT 行（<see cref="LoadBgmCueCreditAliasesAsync"/> 用）。</summary>
    private sealed class BgmCueCreditAliasRaw
    {
        public int SeriesId { get; set; }
        public string MNoDetail { get; set; } = "";
        public string CreditRole { get; set; } = "";
        public byte CreditSeq { get; set; }
        public int AliasId { get; set; }
        public string DisplayText { get; set; } = "";
        public int? PersonId { get; set; }
    }

    /// <summary>1 名義クレジット分の情報（/bgms/ 一覧用）。</summary>
    private sealed class BgmCueCreditAlias
    {
        public int AliasId { get; set; }
        public string DisplayText { get; set; } = "";
        /// <summary>名義の所属人物が 1 人に絞れた場合の人物 ID。共同名義（複数 person 紐付け）では null。</summary>
        public int? PersonId { get; set; }
        /// <summary>名義に紐付く person の数（集計用。1 で PersonId 確定、2 以上で PersonId は null に倒される）。</summary>
        public int PersonCount { get; set; }
    }

    /// <summary>
    /// 商品タイトルを表示用に短縮する。
    /// 想定される長尺パターンと変換例：
    /// <list type="bullet">
    ///   <item><description>「トロピカル～ジュ!プリキュア オリジナル・サウンドトラック1 プリキュア・トロピカル・サウンド!!」
    ///     → 「サントラ1『プリキュア・トロピカル・サウンド!!』」</description></item>
    ///   <item><description>「ふたりはプリキュア オリジナル・サウンドトラック」
    ///     → 「サントラ」</description></item>
    ///   <item><description>「○○プリキュア オリジナル・サウンドトラック2」
    ///     → 「サントラ2」</description></item>
    /// </list>
    /// 正規表現で 3 段階に処理する：
    /// (1) 先頭「○○プリキュア オリジナル・」（プリキュアシリーズ識別子）を削除、
    /// (2) 「サウンドトラック」を「サントラ」に置換、
    /// (3) 後続のサブタイトル（空白区切りの残り）を『』で囲む。
    /// パターンに一致しない商品タイトル（シングル / マキシ / イメージアルバム等）は
    /// 元の文字列をそのまま返す（変な省略をして意味を壊さないため）。
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

    /// <summary><c>/music/</c> 音楽ランディング。歌（/songs/）・劇伴（/bgms/）・音楽商品（/products/）の 3 入口を案内する。 各カードを 1 バッジ構成に統一、商品カードは「N点 M枚」表記、 「歌」バッジは song_recordings 件数（ホーム統計と整合）。 劇伴件数は仮 M 番号も含めた全件カウント （仮 M 番号 cue も閲覧 UI に表示する）。</summary>
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
            PageTitle = "歴代プリキュア音楽",
            MetaDescription = "歴代プリキュアシリーズの歌と劇伴音楽(BGM)、音楽商品(CD/配信)の情報を体系的かつ有機的に集積しています。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "" }
            }
        };
        _page.RenderAndWrite("/music/", "music", "music-landing.sbn", content, layout);
    }

    /// <summary>
    /// <c>/bgms/</c> 劇伴シリーズ一覧。劇伴データを持つシリーズだけ並べる。
    /// 表示は <c>bgms-card-list</c> のカード型リスト：1 シリーズ = 1 カードで、カード内に
    /// タイトル・放送期間・「N 曲 M ver.」メタ・主要スタッフ行が積まれる。
    /// 曲数は <c>m_no_class</c> 単位（同一 class 内の枝番違いは 1 曲扱い、class 未設定 cue は
    /// <c>m_no_detail</c> を独立キーにして 1 曲としてカウント）、バージョン数は cue 総数（仮 M 番号含む）。
    /// 主要スタッフは役職ごと（作曲・編曲）に、その役職に何らかのクレジット情報が入っている cue の
    /// 総数を母数として担当割合 20% 以上の人物（名義 or フリーテキスト名）を頻度降順で抽出する。
    /// 名義側は <c>bgm_cue_credits</c> 経由で <c>person_aliases</c> を引き当て、所属人物が 1 人に絞れる
    /// 名義は人物詳細ページへのリンク用 PersonId を持つ。共同名義やフリーテキストのみのものは
    /// PersonId を持たずテキスト表示のみとなる。作曲と編曲の集合が同順序で完全一致するときは
    /// 同じ人物カード（メンバー名列）に作曲・編曲バッジが連続して並ぶ統合表示にする。
    /// </summary>
    private void GenerateBgmIndex(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail, string Role), List<BgmCueCreditAlias>> creditAliasesByBgmCue)
    {
        var rows = new List<BgmIndexRow>();
        foreach (var s in _ctx.Series.OrderBy(x => x.StartDate).ThenBy(x => x.SeriesId))
        {
            // 子作品（'MOVIE_SHORT'）は単独詳細ページを持たない運用なので、劇伴一覧でも表に出さない。
            // 劇伴データが紐付いていても親シリーズ側の劇伴ページに統合される運用を想定。
            if (SeriesClassifier.IsMovieShortChild(s)) continue;
            if (!cuesBySeries.TryGetValue(s.SeriesId, out var cues)) continue;

            // 仮 M 番号も表示対象に含めるので、バージョン数（cue 総数）は全 cue 数。
            int cueCount = cues.Count;
            if (cueCount == 0) continue;

            // 曲数は m_no_class でグループ化した数。class が NULL/空の cue は m_no_detail を
            // 独立キーにしてそれぞれ 1 曲としてカウント（bgms-detail.sbn の SongCount と同方式）。
            int songCount = cues
                .GroupBy(c => string.IsNullOrEmpty(c.MNoClass) ? $"__detail__:{c.MNoDetail}" : c.MNoClass)
                .Count();

            // 主要作曲家・編曲家。担当割合 20% 以上の人物を頻度降順で抽出し、
            // 作曲・編曲の集合が同順序で完全一致するなら作曲・編曲バッジが連続する 1 グループに統合する。
            var composers = BuildBgmKeyStaffEntries(cues, "COMPOSITION", c => c.ComposerName, creditAliasesByBgmCue);
            var arrangers = BuildBgmKeyStaffEntries(cues, "ARRANGEMENT", c => c.ArrangerName, creditAliasesByBgmCue);
            var staffGroups = BuildBgmStaffGroups(composers, arrangers);

            // 件数ラベル：曲数とバージョン数が同じシリーズ（cue 1 つ = 1 曲、別バージョン無し）では
            // 「N 曲」だけを表示し、異なるシリーズでは「N 曲 M ver.」を表示する。
            string countsLabel = (songCount == cueCount)
                ? $"{songCount} 曲"
                : $"{songCount} 曲 {cueCount} ver.";

            rows.Add(new BgmIndexRow
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                SeriesPeriod = JpDateFormat.Period(s.StartDate, s.EndDate),
                SeriesStartYearLabel = s.StartDate.Year.ToString(),
                SongCount = songCount,
                CueCount = cueCount,
                CountsLabel = countsLabel,
                StaffGroups = staffGroups
            });
        }

        var content = new BgmIndexModel { Rows = rows };
        var layout = new LayoutModel
        {
            PageTitle = "歴代プリキュア劇伴",
            MetaDescription = "プリキュアシリーズの劇伴音源を作品別に一覧。",
            Breadcrumbs = new[]
            {
                new BreadcrumbItem { Label = "ホーム", Url = "/" },
                new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "/music/" },
                new BreadcrumbItem { Label = "歴代プリキュア劇伴音楽(BGM)", Url = "" }
            }
        };
        _page.RenderAndWrite("/bgms/", "music", "bgms-index.sbn", content, layout);
    }

    /// <summary>
    /// 作曲・編曲のスタッフエントリ列から、カード内に出すスタッフ「グループ」を組み立てる。
    /// 1 つのグループは「役職バッジ列 + メンバー名列」のセット。カード内では複数グループを
    /// 1 行内で横並びに描画する（flex-wrap で折り返し）。
    /// <list type="bullet">
    ///   <item>両者の人物集合が「同じ順序で完全一致」（名前列が等しい）なら、メンバー側に同じ人物を載せ、役職バッジを「作曲」「編曲」の 2 つ並べた 1 グループに統合（例: [作曲][編曲] 佐藤直紀）</item>
    ///   <item>そうでなければ「作曲」グループと「編曲」グループの 2 グループに分割。空の役職は出力しない</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<BgmStaffGroup> BuildBgmStaffGroups(
        IReadOnlyList<BgmKeyStaffEntry> composers,
        IReadOnlyList<BgmKeyStaffEntry> arrangers)
    {
        bool composerHas = composers.Count > 0;
        bool arrangerHas = arrangers.Count > 0;

        // 両方空ならスタッフ表示は出さない。
        if (!composerHas && !arrangerHas) return Array.Empty<BgmStaffGroup>();

        // 「同じ順序で同じ集合」判定：Name と PersonId の両方を順序付きで比較。
        // 担当件数自体は揃わなくても良い（閾値 20% で抽出した結果として「作曲・編曲の主要人物が同じ顔ぶれ」が本質）。
        // PersonId 込みで比較するのは、たまたま表示テキストが同じでも別エントリ（フリーテキストと名義）の
        // ケースを区別したいため。両方 null（フリーテキスト同士）の場合は Name 一致だけで揃う。
        bool sameRoster =
            composerHas && arrangerHas
            && composers.Count == arrangers.Count
            && composers.Zip(arrangers, (c, a) => c.Name == a.Name && c.PersonId == a.PersonId).All(eq => eq);

        if (sameRoster)
        {
            // 統合表示：1 グループ内に作曲・編曲の 2 バッジを並べ、その後ろに人物名を並べる。
            // 既存の staff-badge-group の「同一人物に role-badge を 2 つ並べる」パターンと同じ意匠。
            return new[]
            {
                new BgmStaffGroup
                {
                    Roles = new[]
                    {
                        new BgmRoleBadge { RoleCode = "COMPOSITION",  RoleLabel = "作曲" },
                        new BgmRoleBadge { RoleCode = "ARRANGEMENT", RoleLabel = "編曲" }
                    },
                    Members = composers
                }
            };
        }

        // 役職ごとに別グループ。両方持つときは作曲→編曲の順で並べる（カード内では横並びになる）。
        var groups = new List<BgmStaffGroup>();
        if (composerHas)
        {
            groups.Add(new BgmStaffGroup
            {
                Roles = new[] { new BgmRoleBadge { RoleCode = "COMPOSITION", RoleLabel = "作曲" } },
                Members = composers
            });
        }
        if (arrangerHas)
        {
            groups.Add(new BgmStaffGroup
            {
                Roles = new[] { new BgmRoleBadge { RoleCode = "ARRANGEMENT", RoleLabel = "編曲" } },
                Members = arrangers
            });
        }
        return groups;
    }

    /// <summary>
    /// シリーズ内の cue リストから 1 役職（作曲 or 編曲）について、担当割合 20% 以上の
    /// 名義／フリーテキスト名を頻度降順で抽出する。母数は「その役職に何らかのクレジット情報が
    /// 入っている cue 数」。cue ごとに次の優先順位で集計対象を決める：
    /// <list type="number">
    ///   <item>bgm_cue_credits に当該役職の行がある cue：行の名義（alias_id）を全て集計対象とする
    ///     （連名の場合は各 seq を個別に 1 件としてカウント）。表示テキストは名義の
    ///     DisplayTextOverride > Name、リンク先 PersonId は alias の所属人物が 1 人に絞れる場合のみ持たせる。</item>
    ///   <item>credit 行が無く、フリーテキスト（composer_name / arranger_name）が非空：そのテキストを
    ///     1 件としてカウント。PersonId は持たない（リンクなし表示）。</item>
    ///   <item>どちらも無い：母数からも除外（無記入の多寡で割合がぶれないようにするため）。</item>
    /// </list>
    /// 同じ alias_id 同士、同じテキスト同士は集計でまとめる。alias と同テキストのフリーテキストは
    /// 別キーとして扱う（リンク有無を保ったまま頻度を見せたいため）。
    /// </summary>
    private static IReadOnlyList<BgmKeyStaffEntry> BuildBgmKeyStaffEntries(
        IReadOnlyList<BgmCue> cues,
        string role,
        Func<BgmCue, string?> freeTextSelector,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail, string Role), List<BgmCueCreditAlias>> creditAliasesByBgmCue)
    {
        // (alias_id?, normalized_text) をキーにした集計。alias 有り行は alias_id をキー、
        // フリーテキスト行は text をキーにして、別名義として並立させる。
        var buckets = new Dictionary<string, BgmKeyStaffEntry>();
        int denom = 0;  // 役職に何らかのクレジット情報が入っている cue の数。

        foreach (var cue in cues)
        {
            var key = (cue.SeriesId, cue.MNoDetail, role);
            bool counted = false;

            if (creditAliasesByBgmCue.TryGetValue(key, out var credits) && credits.Count > 0)
            {
                // 名義クレジット行あり：各 seq を個別の出現としてカウント。
                foreach (var cred in credits)
                {
                    string bucketKey = $"alias:{cred.AliasId}";
                    if (!buckets.TryGetValue(bucketKey, out var entry))
                    {
                        entry = new BgmKeyStaffEntry
                        {
                            Name = cred.DisplayText,
                            PersonId = cred.PersonId,
                            Count = 0,
                            SharePercent = 0
                        };
                        buckets[bucketKey] = entry;
                    }
                    entry.Count++;
                }
                counted = true;
            }
            else
            {
                // フリーテキストフォールバック（旧フォーマット運用 cue 用）。
                string text = (freeTextSelector(cue) ?? "").Trim();
                if (text.Length > 0)
                {
                    string bucketKey = $"text:{text}";
                    if (!buckets.TryGetValue(bucketKey, out var entry))
                    {
                        entry = new BgmKeyStaffEntry
                        {
                            Name = text,
                            PersonId = null,
                            Count = 0,
                            SharePercent = 0
                        };
                        buckets[bucketKey] = entry;
                    }
                    entry.Count++;
                    counted = true;
                }
            }

            if (counted) denom++;
        }

        if (denom == 0) return Array.Empty<BgmKeyStaffEntry>();

        // 担当割合 20% 以上の閾値。母数の 20%（double 比較で floor 相当）を最小頻度とする。
        // 例: denom = 50 → 10 件以上、denom = 7 → 1.4 件以上（=2 件以上）。
        double thresholdRatio = 0.20;

        foreach (var entry in buckets.Values)
        {
            entry.SharePercent = (int)Math.Round((double)entry.Count / denom * 100);
        }

        return buckets.Values
            .Where(e => (double)e.Count / denom >= thresholdRatio)
            .OrderByDescending(e => e.Count)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>/bgms/{slug}/ 1 シリーズあたりの劇伴詳細。各 cue を 1 枚のカードとして
    /// 縦に並べる。カードヘッダには M 番号・メニュータイトル・尺（初出盤トラックの長さ）、
    /// その下にスタッフバッジ行（作曲・編曲、bgm_cue_credits 由来の名義をリンク化）、
    /// 末尾に収録盤リスト（disc.title_short ｜ Tr.N ｜ トラックタイトル を発売日昇順で）を出す。</summary>
    private async Task GenerateBgmDetailPagesAsync(
        IReadOnlyDictionary<int, IReadOnlyList<BgmCue>> cuesBySeries,
        IReadOnlyDictionary<int, List<BgmSession>> sessionsBySeries,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>> recordingsByBgmCue,
        IReadOnlyDictionary<(int SeriesId, string MNoDetail, string Role), List<BgmCueCreditAlias>> creditAliasesByBgmCue,
        CancellationToken ct)
    {
        await Task.Yield();  // メソッドを async に保つためのダミー（将来 DB 追加クエリを足したときに困らないように）

        foreach (var (seriesId, cues) in cuesBySeries)
        {
            if (!_ctx.SeriesById.TryGetValue(seriesId, out var s)) continue;
            if (SeriesClassifier.IsMovieShortChild(s)) continue;

            // 仮 M 番号 cue も含めて全 cue を表示対象とする
            // 仮 M 番号 cue も対象に含める。
            if (cues.Count == 0) continue;

            // セッションマスタ（session_no → SessionName / Caption のマップ）。
            // SessionName は閲覧 UI のセッション見出しに、Caption は「セッション見出し横の
            // 小さな補足説明」に使う。Caption が NULL ないし空文字ならテンプレ側で span 自体を
            // 出さず、見出しは SessionName のみで構成される。
            var sessionMap = sessionsBySeries.TryGetValue(seriesId, out var sessList)
                ? sessList.ToDictionary(x => x.SessionNo, x => x)
                : new Dictionary<byte, BgmSession>();

            // セッションごとにグルーピング → セッション内では SeqInSession 昇順 → 同値時は m_no_detail 自然順
            var sessionGroups = cues
                .GroupBy(c => c.SessionNo)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    // SessionName と Caption を同じ session 行から拾うため、いったんローカル変数に取り出す。
                    sessionMap.TryGetValue(g.Key, out var session);
                    return new BgmSessionSection
                    {
                        SessionNo = g.Key,
                        SessionName = session?.SessionName ?? "(未設定)",
                        Caption = session?.Caption ?? "",
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

                            // 尺は初出盤（recs の先頭、発売日昇順で最古の収録盤）の当該トラック長を採用。
                            // 収録盤が無い、または length_frames が NULL の場合は LengthLabel を空文字に倒す。
                            string lengthLabel = "";
                            if (recs.Count > 0 && recs[0].LengthSeconds is int lenSec && lenSec > 0)
                            {
                                lengthLabel = FormatLengthSeconds(lenSec);
                            }

                            // スタッフバッジ。/bgms/ 一覧で使うのと同じ BuildBgmKeyStaffEntries を、
                            // 当該 cue 1 件だけのリストに対して呼ぶ。結果として「この cue の作曲・編曲」
                            // 名義（PersonId 込）が得られる。作曲・編曲の集合が同順序で完全一致するなら
                            // 1 グループに統合される（劇伴一覧のマージルールと同じ）。
                            var singleCueList = new[] { c };
                            var composers = BuildBgmKeyStaffEntries(singleCueList, "COMPOSITION",
                                cue => cue.ComposerName, creditAliasesByBgmCue);
                            var arrangers = BuildBgmKeyStaffEntries(singleCueList, "ARRANGEMENT",
                                cue => cue.ArrangerName, creditAliasesByBgmCue);
                            var staffGroups = BuildBgmStaffGroups(composers, arrangers);

                            return new BgmCueRow
                            {
                                MNoDetail = mNoCell,
                                MNoClass = c.MNoClass ?? "",
                                IsTempMNo = c.IsTempMNo,
                                MenuTitle = menuCell,
                                MenuFallbackTitle = menuFallback,
                                StaffGroups = staffGroups,
                                LengthLabel = lengthLabel,
                                Notes = c.Notes ?? "",
                                // 商品詳細トラック行からアンカーリンクされる先の id 属性値。
                                // m_no_detail を URL-safe 化したものを「cue-{...}」の形で組み立てる
                                // （生値を id 属性に流すと CJK や記号で URL エンコードが必要になるため、
                                // 統一的に PathUtil.SlugifyMNoDetail で正規化する）。
                                // 仮 M 番号（IsTempMNo）でも cue 自体は存在するためアンカー可能。
                                AnchorId = "cue-" + PathUtil.SlugifyMNoDetail(c.MNoDetail),
                                Recordings = recs
                            };
                        })
                        .ToList()
                    };
                })
                .ToList();

            // 「曲」のカウントは bgm_cues.m_no_class でグループ化した数。
            // 同一 m_no_class を共有する複数 cue（M220 / M220b / M220 ShortVer 等）は 1 曲・複数バージョンと数える。
            // m_no_class が NULL ないし空文字の cue（仮 M 番号や class 未設定）は m_no_detail を独立キーにして
            // それぞれ 1 曲としてカウントする。「バージョン」は cue 総数（TotalCueCount）と一致する。
            int songCount = cues
                .GroupBy(c => string.IsNullOrEmpty(c.MNoClass) ? $"__detail__:{c.MNoDetail}" : c.MNoClass)
                .Count();

            var content = new BgmDetailModel
            {
                SeriesSlug = s.Slug,
                SeriesTitle = s.Title,
                SeriesPeriod = JpDateFormat.Period(s.StartDate, s.EndDate),
                Sessions = sessionGroups,
                SongCount = songCount,
                TotalCueCount = cues.Count
            };
            var layout = new LayoutModel
            {
                PageTitle = $"劇伴 - {s.Title}",
                MetaDescription = $"{s.Title} の劇伴音源一覧。",
                Breadcrumbs = new[]
                {
                    new BreadcrumbItem { Label = "ホーム", Url = "/" },
                    new BreadcrumbItem { Label = "歴代プリキュア音楽", Url = "/music/" },
                    new BreadcrumbItem { Label = "歴代プリキュア劇伴音楽(BGM)", Url = "/bgms/" },
                    new BreadcrumbItem { Label = s.Title, Url = "" }
                }
            };
            _page.RenderAndWrite($"/bgms/{s.Slug}/", "music", "bgms-detail.sbn", content, layout);
        }
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

    /// <summary>長さ（秒）を「m:ss」形式に整形（int オーバーロード）。
    /// tracks.length_frames を 75 で割って秒に換算した結果（int）を直接渡すケース用。</summary>
    private static string FormatLengthSeconds(int lengthSeconds)
    {
        if (lengthSeconds <= 0) return "";
        int min = lengthSeconds / 60;
        int sec = lengthSeconds % 60;
        return $"{min}:{sec:D2}";
    }

    // ─── テンプレ用 DTO 群 ───

    /// <summary>/music/ 音楽ランディングのテンプレ用モデル（4 プロパティに整理）。 歌・劇伴・音楽商品の 3 カードに 1 バッジずつを表示するためのデータ。</summary>
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
        /// <summary>シリーズ開始年の西暦 4 桁文字列（例: "2004"）。年度注記が必要な箇所に使う。</summary>
        public string SeriesStartYearLabel { get; set; } = "";
        /// <summary>曲数。<c>m_no_class</c> でグループ化した数。同一 class を共有する複数 cue は 1 曲扱い。class が NULL/空の cue はそれぞれ 1 曲としてカウントする。</summary>
        public int SongCount { get; set; }
        /// <summary>バージョン数。cue 総数（仮 M 番号含む）。</summary>
        public int CueCount { get; set; }
        /// <summary>カード内のメタ行に出す件数ラベル。曲数とバージョン数が一致するときは「N 曲」のみ、異なるときは「N 曲 M ver.」を返す。テンプレ側は本値をそのまま出力する。</summary>
        public string CountsLabel { get; set; } = "";
        /// <summary>カード内に並べるスタッフグループ。複数の役職バッジを連続して持てる（作曲・編曲が同一人物のとき統合表示するため）。空のときはスタッフ表示を出さない。</summary>
        public IReadOnlyList<BgmStaffGroup> StaffGroups { get; set; } = Array.Empty<BgmStaffGroup>();
    }

    /// <summary>
    /// /bgms/ 一覧のカード内に出すスタッフ 1 グループ分。1 つ以上の役職バッジを連続して並べ、
    /// その後ろにメンバー名を並べる形で描画される（例: [作曲][編曲] 佐藤直紀）。
    /// 複数グループは <c>flex-wrap</c> で横並び（必要に応じて行折り返し）。
    /// </summary>
    private sealed class BgmStaffGroup
    {
        /// <summary>役職バッジ列。通常 1 件、作曲と編曲のメンバーが完全一致するときは 2 件並ぶ。</summary>
        public IReadOnlyList<BgmRoleBadge> Roles { get; set; } = Array.Empty<BgmRoleBadge>();
        /// <summary>このグループに属するメンバー（人物）の列。頻度降順。</summary>
        public IReadOnlyList<BgmKeyStaffEntry> Members { get; set; } = Array.Empty<BgmKeyStaffEntry>();
    }

    /// <summary>役職バッジ 1 個分（コードと表示ラベルのみ）。</summary>
    private sealed class BgmRoleBadge
    {
        /// <summary>役職コード。"COMPOSITION" / "ARRANGEMENT"。CSS の役職バッジ色を当てる data-role-code に渡す。</summary>
        public string RoleCode { get; set; } = "";
        /// <summary>役職表示ラベル。「作曲」「編曲」。</summary>
        public string RoleLabel { get; set; } = "";
    }

    /// <summary>
    /// /bgms/ 一覧のスタッフ表示に出す「主要スタッフ」1 エントリ。役職（作曲・編曲）ごとに
    /// 複数並ぶ。<see cref="Name"/> は名義（person_aliases.display_text_override > .name）または
    /// フリーテキスト（bgm_cues.composer_name / arranger_name）由来の表示文字列。
    /// <see cref="PersonId"/> が非 null のとき、名義の所属人物が 1 人に絞れる新フォーマット
    /// （bgm_cue_credits → person_aliases → person_alias_persons）由来で人物詳細ページへの
    /// リンクが可能。null のときは共同名義またはフリーテキスト由来で、リンクは出さずテキストのみで表示。
    /// </summary>
    private sealed class BgmKeyStaffEntry
    {
        public string Name { get; set; } = "";
        /// <summary>名義 → 人物が 1 人に絞れる場合の人物 ID。共同名義やフリーテキスト由来は null。</summary>
        public int? PersonId { get; set; }
        /// <summary>当該役職にクレジット情報が入っている cue 数のうち、本エントリが担当した件数。降順並べ替えキー。</summary>
        public int Count { get; set; }
        /// <summary>担当割合（％、整数四捨五入）。テンプレ側でツールチップ等に活用する余地を残す値。</summary>
        public int SharePercent { get; set; }
    }

    private sealed class BgmDetailModel
    {
        public string SeriesSlug { get; set; } = "";
        public string SeriesTitle { get; set; } = "";
        public string SeriesPeriod { get; set; } = "";
        public IReadOnlyList<BgmSessionSection> Sessions { get; set; } = Array.Empty<BgmSessionSection>();
        /// <summary>シリーズ内の楽曲数（cue を <c>m_no_class</c> でグループ化した数）。同一 class を共有する複数 cue は 1 曲扱い。class が NULL/空の cue はそれぞれ 1 曲としてカウントする。</summary>
        public int SongCount { get; set; }
        /// <summary>シリーズ内の cue 総数（バージョン数）。仮 M 番号 cue を含む。</summary>
        public int TotalCueCount { get; set; }
    }

    private sealed class BgmSessionSection
    {
        public byte SessionNo { get; set; }
        public string SessionName { get; set; } = "";
        /// <summary>
        /// 公開サイトのセッション見出し横に小さく添える補足説明テキスト。
        /// <c>bgm_sessions.caption</c> の値を反映する。空文字のときはテンプレ側でそもそも span を
        /// 出さない（見出しは SessionName だけになる）。
        /// </summary>
        public string Caption { get; set; } = "";
        public IReadOnlyList<BgmCueRow> Cues { get; set; } = Array.Empty<BgmCueRow>();
    }

    private sealed class BgmCueRow
    {
        /// <summary>M 番号セルの表示値。仮 M 番号 cue の場合は空文字。</summary>
        public string MNoDetail { get; set; } = "";
        public string MNoClass { get; set; } = "";
        /// <summary>仮 M 番号フラグ。 テンプレ側で行スタイルやメニュー欄の表示分岐に使う。</summary>
        public bool IsTempMNo { get; set; }
        /// <summary>メニュー（曲名）セルの表示値。仮 M 番号 cue では空文字。</summary>
        public string MenuTitle { get; set; } = "";
        /// <summary>仮 M 番号 cue のメニュー代替表示（最初の収録盤のトラックタイトル）。 通常 cue では空文字（MenuTitle 側で表示済みのため）。</summary>
        public string MenuFallbackTitle { get; set; } = "";
        /// <summary>当該 cue のスタッフバッジ群（作曲・編曲）。劇伴一覧と同じ形式で、bgm_cue_credits 由来の
        /// 名義（PersonId 解決済み）を優先、フリーテキストフォールバックも含む。作曲・編曲の名義集合が
        /// 完全一致する cue ではバッジ 2 つを連続で出す統合グループになる。</summary>
        public IReadOnlyList<BgmStaffGroup> StaffGroups { get; set; } = Array.Empty<BgmStaffGroup>();
        /// <summary>カードヘッダに出す「尺」（M:SS 形式）。 初出盤（recs[0]、発売日昇順で最古）の当該トラック
        /// length_seconds を 75 frames/sec から逆算して整形済み。収録盤無し or length_frames NULL のときは空文字。</summary>
        public string LengthLabel { get; set; } = "";
        public string Notes { get; set; } = "";
        /// <summary>収録盤情報のリスト（発売日昇順）。 カード末尾に小さく「discs.title_short | Tr.N | トラックタイトル」を列挙する。 0 件のときはテンプレ側で「（未収録）」と表示する。</summary>
        public IReadOnlyList<BgmCueRecording> Recordings { get; set; } = Array.Empty<BgmCueRecording>();
        /// <summary>
        /// HTML id 属性として使うアンカー識別子。商品詳細ページのトラック行から
        /// <c>/bgms/{slug}/#{AnchorId}</c> でこの cue 行へ直接ジャンプできるようにする。
        /// 値は <c>cue-{PathUtil.SlugifyMNoDetail(MNoDetail)}</c> 形式（URL-safe 文字列）。
        /// </summary>
        public string AnchorId { get; set; } = "";
    }

    /// <summary>劇伴 cue × 収録盤の 1 行。 商品単位の識別子（ProductCatalogNo / ProductTitle）に加えて、
    /// 盤単位の短縮タイトル（DiscTitleShort、表示用）、盤単位 catalog_no（DiscCatalogNo、URL アンカー組み立て用）、
    /// 当該トラックの尺（LengthSeconds）も持つ。</summary>
    private sealed class BgmCueRecording
    {
        public string ProductCatalogNo { get; set; } = "";
        /// <summary>表示用に短縮された商品タイトル（例「サントラ1『プリキュア・トロピカル・サウンド!!』」）。 短縮ロジック非適用の商品（シングル等）は元のタイトルがそのまま入る。</summary>
        public string ProductTitle { get; set; } = "";
        /// <summary>products.title の元の値（短縮前のフル表記）。 テンプレ側で <c>&lt;a title="..."&gt;</c> 属性に詰めてホバー時にフル名を見せる用途。</summary>
        public string ProductTitleFull { get; set; } = "";
        /// <summary>discs.title_short。盤単位の簡潔な識別タイトル。劇伴詳細カードの収録盤行で表示テキストとして使う。</summary>
        public string DiscTitleShort { get; set; } = "";
        /// <summary>tracks.catalog_no（= discs.catalog_no、盤単位の自然キー）。
        /// 商品詳細ページのトラック行に振られた <c>id="track-{DiscCatalogNo}-{TrackNo}-{SubOrder}"</c> へ
        /// 厳密にアンカーリンクを張るために使う。</summary>
        public string DiscCatalogNo { get; set; } = "";
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        public string TrackTitle { get; set; } = "";
        /// <summary>このトラックの尺（秒）。tracks.length_frames を 75 で割って四捨五入。NULL/0 のときは null。
        /// 劇伴詳細カードヘッダの「尺」は recordings の先頭（発売日昇順で最古 = 初出盤）の LengthSeconds を採用する。</summary>
        public int? LengthSeconds { get; set; }
    }

    /// <summary>Dapper マッピング用の生 SELECT 行（<see cref="LoadBgmCueRecordingsAsync"/> 用）。</summary>
    private sealed class BgmCueRecordingRaw
    {
        public int SeriesId { get; set; }
        public string MNoDetail { get; set; } = "";
        public string ProductCatalogNo { get; set; } = "";
        public string ProductTitle { get; set; } = "";
        public string DiscCatalogNo { get; set; } = "";
        public string DiscTitleShort { get; set; } = "";
        public DateTime ReleaseDate { get; set; }
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        public string TrackTitle { get; set; } = "";
        /// <summary>tracks.length_frames（CD 75 frames/sec 単位）。NULL の可能性あり。</summary>
        public uint? LengthFrames { get; set; }
    }
}
