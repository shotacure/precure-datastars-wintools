using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.Models;
using PrecureDataStars.SiteBuilder.Generators;
using PrecureDataStars.SiteBuilder.Pipeline;

namespace PrecureDataStars.SiteBuilder.Utilities;

/// <summary>
/// 劇伴の曲（bgm_cues）ごとの収録盤の読み込み。劇伴詳細の曲のカードと、エピソード詳細の BGM リストで使う。
/// </summary>
public static class BgmCueRecordingsLoader
{
    /// <summary>全 bgm_cue の収録盤情報を 1 度の JOIN クエリで取得し、(series_id, m_no_detail) で グルーピングして返す。</summary>
    public static async Task<IReadOnlyDictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>>>
        LoadAsync(IConnectionFactory factory, CancellationToken ct)
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
        // 1 トラックに複数の曲・版が入っている盤で、トラックの中の区切り（sub_length_frames）が入っていれば、
        // その曲・版だけの長さを使う（枝番の行にも尺が出る）。
        // 流通元を替えて同じ中身のまま再発売された盤（Reissues）のトラックは、行にすると初回盤と二重に並ぶので
        // 同じトラックの初回盤の行に、本行と同じ「{盤の略称}-Tr.{N}」の書き方で添える（歌の詳細も同じ）。
        // ORDER BY は安定タイブレーク前提（発売日 → 品番 → トラック番号 → サブ順）。
        //
        // UNION ALL の 2 系統で収録盤行を取る：
        //   - 第 1 系統：tracks 本体の bgm_series_id / bgm_m_no_detail 参照（旧来からの BGM トラック）
        //   - 第 2 系統：song_recording_bgm_assignments 経由（SONG トラックでありながら BGM 性も持つ録音）
        // 既存トリガー trg_tracks_bi/bu_fk_consistency により tracks 本体は SONG/BGM 排他、
        // trg_tracks_bu_block_kind_change_when_srba により中間テーブル紐付き SONG は別種別へ変更不可なので、
        // 同一 (DiscCatalogNo, TrackNo, SubOrder) が両系統から重複して拾われることは構造上ない。
        const string sql = """
            SELECT t.bgm_series_id     AS SeriesId,
                   t.bgm_m_no_detail   AS MNoDetail,
                   p.product_catalog_no AS ProductCatalogNo,
                   p.title             AS ProductTitle,
                   t.catalog_no        AS DiscCatalogNo,
                   COALESCE(d.title_short, '') AS DiscTitleShort,
                   COALESCE(d.title, '') AS DiscTitle,
                   d.disc_no_in_set    AS DiscNoInSet,
                   p.release_date      AS ReleaseDate,
                   t.track_no          AS TrackNo,
                   t.sub_order         AS SubOrder,
                   COALESCE(t.track_title_override, '') AS TrackTitle,
                   COALESCE(t.sub_length_frames, t.length_frames) AS LengthFrames,
                   t.youtube_art_track_id AS YoutubeArtTrackId,
                   t.youtube_embeddable   AS YoutubeEmbeddable,
                   t.youtube_playability  AS YoutubePlayability,
                   t.youtube_checked_at   AS YoutubeCheckedAt
              FROM tracks t
              JOIN discs d    ON d.catalog_no = t.catalog_no
              JOIN products p ON p.product_catalog_no = d.product_catalog_no
             WHERE t.bgm_series_id IS NOT NULL
               AND t.bgm_m_no_detail IS NOT NULL
               AND p.is_deleted = 0

            UNION ALL

            SELECT a.bgm_series_id     AS SeriesId,
                   a.bgm_m_no_detail   AS MNoDetail,
                   p.product_catalog_no AS ProductCatalogNo,
                   p.title             AS ProductTitle,
                   t.catalog_no        AS DiscCatalogNo,
                   COALESCE(d.title_short, '') AS DiscTitleShort,
                   COALESCE(d.title, '') AS DiscTitle,
                   d.disc_no_in_set    AS DiscNoInSet,
                   p.release_date      AS ReleaseDate,
                   t.track_no          AS TrackNo,
                   t.sub_order         AS SubOrder,
                   COALESCE(t.track_title_override, '') AS TrackTitle,
                   COALESCE(t.sub_length_frames, t.length_frames) AS LengthFrames,
                   t.youtube_art_track_id AS YoutubeArtTrackId,
                   t.youtube_embeddable   AS YoutubeEmbeddable,
                   t.youtube_playability  AS YoutubePlayability,
                   t.youtube_checked_at   AS YoutubeCheckedAt
              FROM song_recording_bgm_assignments a
              JOIN tracks   t ON t.song_recording_id = a.song_recording_id
                              -- パート・サイズとも完全一致でのみマッチ。'_ANY' は sentinel で全パート／全サイズを
                              -- 覆うため、tracks 側の値（NULL を含む）に関わらず常にマッチさせる。
                              AND (a.song_part_variant_code = '_ANY'
                                OR a.song_part_variant_code = t.song_part_variant_code)
                              AND (a.song_size_variant_code = '_ANY'
                                OR a.song_size_variant_code = t.song_size_variant_code)
              JOIN discs    d ON d.catalog_no = t.catalog_no
              JOIN products p ON p.product_catalog_no = d.product_catalog_no
             WHERE p.is_deleted = 0

             ORDER BY ReleaseDate ASC,
                      ProductCatalogNo ASC,
                      TrackNo ASC,
                      SubOrder ASC;
            """;

        await using var conn = await factory.CreateOpenedAsync(ct).ConfigureAwait(false);
        var rows = (await conn.QueryAsync<BgmCueRecordingRaw>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false)).ToList();

        var dict = new Dictionary<(int SeriesId, string MNoDetail), List<BgmCueRecording>>();
        var now = DateTime.Now;
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
                DiscNoInSet = r.DiscNoInSet,
                DiscTitle = r.DiscTitle,
                // disc 単位 catalog_no。アンカー URL のトラック行特定キーに使う
                // （/products/{ProductCatalogNo}/#track-{DiscCatalogNo}-{TrackNo}-{SubOrder}）。
                DiscCatalogNo = r.DiscCatalogNo,
                TrackNo = r.TrackNo,
                SubOrder = r.SubOrder,
                TrackTitle = r.TrackTitle,
                LengthSeconds = lengthSeconds,
                LengthFrames = (r.LengthFrames is uint f && f > 0) ? f : null,
                // 配信音源は「埋め込み可と確認済み・日本で再生できる・確認が新しい」ときだけ通す
                // （SiteDataLoader が tracks に掛けるのと同じ ArtTrackPlayability.IsPublishable）。
                // 未確認（NULL）と不可（false）はどちらも再生対象外に倒す。どの盤を実際に鳴らすかは cue 組み立て側で決める。
                ArtTrackId = ArtTrackPlayability.IsPublishable(r.YoutubeEmbeddable, r.YoutubePlayability, r.YoutubeCheckedAt, now)
                    ? (r.YoutubeArtTrackId ?? "") : "",
                ArtTrackPremiumOnly = string.Equals(r.YoutubePlayability, "PREMIUM_ONLY", StringComparison.Ordinal)
            });
        }
        // 再発売盤のトラックを、同じ cue・同じトラック番号の初回盤の行に添えて、行からは外す。
        // 初回盤の行が見つからないときは、そのまま行として残す。
        foreach (var list in dict.Values)
        {
            foreach (var rec in list.ToList())
            {
                var firstPressNo = Reissues.FirstPressOf(rec.ProductCatalogNo);
                if (firstPressNo is null) continue;
                var firstPressRow = list.FirstOrDefault(x => x.ProductCatalogNo == firstPressNo && x.TrackNo == rec.TrackNo && x.SubOrder == rec.SubOrder);
                if (firstPressRow is null) continue;
                firstPressRow.ReissueDiscTitle = rec.DiscTitleShort.Length > 0 ? rec.DiscTitleShort : rec.ProductTitleFull;
                firstPressRow.ReissueUrl = $"/products/{rec.ProductCatalogNo}/#track-{rec.DiscCatalogNo}-{rec.TrackNo}-{rec.SubOrder}";
                list.Remove(rec);
            }
        }
        return dict;
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

    /// <summary>Dapper マッピング用の生 SELECT 行（<see cref="LoadAsync"/> 用）。</summary>
    private sealed class BgmCueRecordingRaw
    {
        public int SeriesId { get; set; }
        public string MNoDetail { get; set; } = "";
        public string ProductCatalogNo { get; set; } = "";
        public string ProductTitle { get; set; } = "";
        public string DiscCatalogNo { get; set; } = "";
        public string DiscTitleShort { get; set; } = "";
        public string DiscTitle { get; set; } = "";
        public uint? DiscNoInSet { get; set; }
        public DateTime ReleaseDate { get; set; }
        public byte TrackNo { get; set; }
        public byte SubOrder { get; set; }
        public string TrackTitle { get; set; } = "";
        /// <summary>tracks.length_frames（CD 75 frames/sec 単位）。NULL の可能性あり。</summary>
        public uint? LengthFrames { get; set; }
        /// <summary>tracks.youtube_art_track_id。配信音源が未登録なら NULL。</summary>
        public string? YoutubeArtTrackId { get; set; }
        /// <summary>tracks.youtube_embeddable。未確認は NULL。</summary>
        public bool? YoutubeEmbeddable { get; set; }
        /// <summary>tracks.youtube_playability。未確認は NULL。</summary>
        public string? YoutubePlayability { get; set; }
        /// <summary>tracks.youtube_checked_at。埋め込み可否の最終確認時刻。</summary>
        public DateTime? YoutubeCheckedAt { get; set; }
    }
}

/// <summary>劇伴 cue × 収録盤の 1 行。 商品単位の識別子（ProductCatalogNo / ProductTitle）に加えて、
/// 盤単位の短縮タイトル（DiscTitleShort、表示用）、盤単位 catalog_no（DiscCatalogNo、URL アンカー組み立て用）、
/// 当該トラックの尺（LengthSeconds）も持つ。</summary>
public sealed class BgmCueRecording
{
    public string ProductCatalogNo { get; set; } = "";
    /// <summary>表示用に短縮された商品タイトル（例「サントラ1『プリキュア・トロピカル・サウンド!!』」）。 短縮ロジック非適用の商品（シングル等）は元のタイトルがそのまま入る。</summary>
    public string ProductTitle { get; set; } = "";
    /// <summary>products.title の元の値（短縮前のフル表記）。 テンプレ側で <c>&lt;a title="..."&gt;</c> 属性に詰めてホバー時にフル名を見せる用途。</summary>
    public string ProductTitleFull { get; set; } = "";
    /// <summary>discs.title_short。盤単位の簡潔な識別タイトル。劇伴詳細カードの収録盤行で表示テキストとして使う。</summary>
    public string DiscTitleShort { get; set; } = "";
    /// <summary>discs.disc_no_in_set。単品は NULL。盤の名前が未登録のときの代替表記に使う。</summary>
    public uint? DiscNoInSet { get; set; }
    /// <summary>discs.title。盤そのものの名前。プレイヤーに出すアルバム表記に使う。</summary>
    public string DiscTitle { get; set; } = "";
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
    /// <summary>このトラックの尺（CD フレーム、75 frames = 1 秒）。合計を端数まで正確に出すために生値で持つ。
    /// NULL/0 のときは null。</summary>
    public uint? LengthFrames { get; set; }
    /// <summary>
    /// この収録盤トラックの配信音源（YouTube アートトラック）の動画 ID。
    /// 登録済みかつ埋め込み可と確認済みのときだけ値が入り、それ以外は空。
    /// </summary>
    public string ArtTrackId { get; set; } = "";
    /// <summary>配信音源が YouTube Music Premium 会員限定か。</summary>
    public bool ArtTrackPremiumOnly { get; set; }
    /// <summary>初回盤の行に添える再発売盤（同じトラック番号・同じトラックタイトル）の盤の名前（「無印 OST1(再)」）。それ以外は空文字。</summary>
    public string ReissueDiscTitle { get; set; } = "";
    /// <summary>再発売盤の商品詳細の該当トラックへの URL（<see cref="ReissueDiscTitle"/> があるときだけ）。</summary>
    public string ReissueUrl { get; set; } = "";
    /// <summary>
    /// この行が cue の再生音源として採用された盤かどうか。収録盤リストで ♪ 印を出す判定に使う。
    /// 同じ cue でも盤によって尺が違うため、どの盤の音源が鳴るかは必ず明示する。
    /// </summary>
    public bool IsArtTrackSource { get; set; }
}
