using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PrecureDataStars.Data.Repositories;

namespace PrecureDataStars.Catalog.Common.Services;

/// <summary>
/// 配信音源（YouTube アートトラック）をアルバム単位で取り込むサービス。
/// <para>
/// 取り込みは「並び順を保ったままの対応付け（アライメント）」で行う。単純な位置固定では
/// 配信されていないトラックが 1 件あるだけで以降が全部ずれるため、タイトルが一致する行を
/// 手がかりに順序を保った最長の対応を求め、その間を埋める方式を採る。
/// 配信されていないトラックは対応先なしとして残し、動画 ID を持たない（再生ボタンが出ない）。
/// </para>
/// <para>
/// タイトルだけで対応付けないのは、同じアルバムに同名トラックが複数入ること
/// （原曲と Remix 違いなど）があり、文字列では一意に割れないため。タイトルはあくまで
/// 並びの中の目印として使い、目印と目印の間は順序で埋める。
/// </para>
/// <para>
/// 複数枚組は YouTube 側で 1 本のプレイリストに平坦化されるため、DB 側も
/// 「ディスク番号 → トラック番号」の通し順に平坦化して突き合わせる。
/// </para>
/// </summary>
public sealed class ArtTrackImportService
{
    private readonly YouTubeDataApiClient _api;
    /// <summary>再生可否の判定。Data API では Premium 限定かどうかが取れないための補助。</summary>
    private readonly YouTubePlayabilityProbe _probe = new();
    private readonly TracksRepository _tracksRepo;
    private readonly ProductsRepository _productsRepo;

    /// <summary><see cref="ArtTrackImportService"/> の新しいインスタンスを生成する。</summary>
    public ArtTrackImportService(
        YouTubeDataApiClient api, TracksRepository tracksRepo, ProductsRepository productsRepo)
    {
        _api = api;
        _tracksRepo = tracksRepo;
        _productsRepo = productsRepo;
    }

    /// <summary>プレイリスト ID の入力を正規化する。 YouTube の watch / playlist URL を丸ごと貼り付けられても使えるよう、 <c>list=</c> パラメータが含まれていればその値だけを取り出す。</summary>
    /// <param name="input">入力文字列（ID そのもの、または URL）。</param>
    public static string NormalizePlaylistId(string? input)
    {
        string s = (input ?? "").Trim();
        if (s.Length == 0) return "";

        var m = Regex.Match(s, @"[?&]list=([A-Za-z0-9_-]+)");
        return m.Success ? m.Groups[1].Value : s;
    }

    /// <summary>取り込み結果をプレビューする（DB へは書き込まない）。 プレイリストを展開し、商品の全トラックと並び順を保ったまま対応付けて返す。 配信されていないトラックは相手なしのまま残る。埋め込み可否もこの段階で取得する。 再生可否（Premium 限定かどうか）はここでは調べない。動画 1 件ずつページを見る必要があって 曲数に比例して待たされるため、先に対応表を見せてから <see cref="ProbePlayabilityAsync"/> で 進捗を出しつつ追いかける。</summary>
    /// <param name="productCatalogNo">対象商品の代表品番。</param>
    /// <param name="playlistIdOrUrl">プレイリスト ID または URL。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task<ArtTrackImportPreview> PreviewAsync(
        string productCatalogNo, string playlistIdOrUrl, CancellationToken ct = default)
    {
        string playlistId = NormalizePlaylistId(playlistIdOrUrl);
        if (playlistId.Length == 0)
            throw new ArgumentException("プレイリスト ID が空です。", nameof(playlistIdOrUrl));

        var dbRows = await _tracksRepo.GetArtTrackMatchRowsAsync(productCatalogNo, ct).ConfigureAwait(false);
        var videos = await _api.GetPlaylistItemsAsync(playlistId, ct).ConfigureAwait(false);
        var details = await _api.GetVideoDetailsAsync(videos.Select(v => v.VideoId), ct).ConfigureAwait(false);

        var rows = BuildAlignedRows(dbRows, videos, details);


        return new ArtTrackImportPreview
        {
            ProductCatalogNo = productCatalogNo,
            PlaylistId = playlistId,
            DbTrackCount = dbRows.Count,
            PlaylistItemCount = videos.Count,
            Rows = rows
        };
    }

    /// <summary>プレビュー結果に再生可否（Premium 限定かどうか）を埋める。
    /// <para>
    /// 対応が付いた動画だけを対象に、動画ページの playabilityStatus を 1 件ずつ確認する。
    /// 埋め込み可でも YouTube Music Premium 会員限定のことがあり、それは Data API では
    /// 分からないため。1 件ごとに短い間隔を空ける都合で曲数に比例した時間がかかるので、
    /// 対応表を表示したあとに進捗を見せながら走らせる想定。
    /// </para>
    /// </summary>
    /// <param name="preview">対象のプレビュー結果。行の Playability が埋まる。</param>
    /// <param name="progress">1 件終わるごとに (完了数, 総数) を通知する。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ProbePlayabilityAsync(
        ArtTrackImportPreview preview,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var targets = preview.Rows.Where(r => r.VideoId.Length > 0).Select(r => r.VideoId).ToList();
        if (targets.Count == 0) return;

        var playable = await _probe.GetPlayabilityAsync(targets, progress, ct).ConfigureAwait(false);
        foreach (var r in preview.Rows)
        {
            if (r.VideoId.Length > 0 && playable.TryGetValue(r.VideoId, out var status))
                r.Playability = status;
        }
    }

    /// <summary>プレビュー結果を DB へ反映する。 対応が付いた行は動画 ID を書き込み、対応が付かなかった DB トラックは動画 ID を消す。 消すのは、以前の取り込みで付いた誤った割り当てを残さないため（配信されていないトラックは 動画 ID を持たない状態が正しく、サイト側でも再生ボタンが出なくなる）。</summary>
    /// <param name="preview">プレビュー結果。</param>
    /// <param name="confirmedByUser">対応表を人が確認したうえでの書き込みなら true。 その場合は商品の取り込み状態を無条件に <c>MATCHED</c> とする。タイトルの表記差は配信側と DB 側で 常に出るもので、人が見て問題ないと判断した以上あとに残す意味がないため。 false（一括取り込みなど、行ごとの目視を伴わない経路）では自動判定に従い、プレイリストの全曲が 対応先を見つけ、かつ対応した全行でタイトルも一致していれば <c>MATCHED</c>、そうでなければ <c>AMBIGUOUS</c>（あとで人が確認する目印）とする。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ApplyAsync(
        ArtTrackImportPreview preview, bool confirmedByUser = false, CancellationToken ct = default)
    {
        var assignments = preview.Rows
            .Where(r => r.CatalogNo.Length > 0)
            .Select(r => new ArtTrackAssignment
            {
                CatalogNo = r.CatalogNo,
                TrackNo = r.TrackNo,
                SubOrder = r.SubOrder,
                // 対応が付かなかったトラックは null を書いて割り当てを解除する。
                YoutubeArtTrackId = r.VideoId.Length > 0 ? r.VideoId : null,
                YoutubeEmbeddable = r.VideoId.Length > 0 ? r.Embeddable : null,
                YoutubePlayability = r.VideoId.Length > 0 ? r.Playability : null
            })
            .ToList();

        var now = DateTime.Now;
        await _tracksRepo.UpdateArtTrackAssignmentsAsync(assignments, now, ct).ConfigureAwait(false);
        string status = confirmedByUser || preview.IsFullyMatched ? "MATCHED" : "AMBIGUOUS";
        await _productsRepo.UpdateArtTrackPlaylistAsync(
            preview.ProductCatalogNo, preview.PlaylistId, status, now, ct).ConfigureAwait(false);
    }


    /// <summary>DB のトラック列とプレイリストの動画列を、並び順を保ったまま対応付ける。
    /// <para>
    /// 手がかりは<b>タイトルの類似度</b>と<b>再生時間</b>の 2 つ。タイトルの完全一致は期待できない
    /// （配信側とデータベース側で表記の揺れが常にある）ため、一致・不一致の二値ではなく
    /// 連続値のスコアにして、全体の合計が最大になる対応を求める。
    /// </para>
    /// <para>
    /// 尺は表記に左右されない強い手がかりで、配信側でタイトルが多少変わっても尺が合えば同じ曲とみなせる。
    /// そのため 2 つの手がかりは足し合わせず「良い方を採る」。タイトルが別物でも尺がぴたりと合えば対応し、
    /// 逆にタイトルが一致していれば尺が取れていなくても対応する。
    /// </para>
    /// <para>
    /// 似ていない組み合わせを無理に対応付けるより両側を空けた方が得点が高くなるよう重みを
    /// 決めてあり、結果として「配信されていないトラック」と「DB に無い配信限定トラック」は
    /// 自然に相手なしとして残る。
    /// </para>
    /// </summary>
    private static List<ArtTrackImportRow> BuildAlignedRows(
        IReadOnlyList<ArtTrackMatchRow> dbRows,
        IReadOnlyList<YouTubePlaylistVideo> videos,
        IReadOnlyDictionary<string, YouTubeVideoDetail> details)
    {
        var dbKeys = dbRows.Select(r => NormalizeTitle(r.DisplayTitle)).ToArray();
        var videoKeys = videos.Select(v => NormalizeTitle(v.Title)).ToArray();
        var dbLengths = dbRows.Select(r => r.LengthSeconds).ToArray();
        var videoLengths = videos
            .Select(v => details.TryGetValue(v.VideoId, out var d) ? d.DurationSeconds : null)
            .ToArray();

        var pairs = AlignBySimilarity(dbKeys, videoKeys, dbLengths, videoLengths);

        var rows = new List<ArtTrackImportRow>();
        foreach (var (dbIndex, videoIndex) in pairs)
        {
            if (dbIndex >= 0 && videoIndex >= 0)
                rows.Add(MakePairedRow(dbRows[dbIndex], videos[videoIndex], details));
            else if (dbIndex >= 0) rows.Add(MakeDbOnlyRow(dbRows[dbIndex]));
            else rows.Add(MakeVideoOnlyRow(videos[videoIndex], details));
        }

        for (int i = 0; i < rows.Count; i++) rows[i].Position = i + 1;
        return rows;
    }

    /// <summary>この秒数までの差は同じ音源とみなす。</summary>
    private const int DurationToleranceSeconds = 2;

    /// <summary>許容幅を超えたあと、この秒数かけて一致度を 0 まで落とす。</summary>
    private const double DurationFalloffSeconds = 6.0;

    /// <summary>尺の差から求めた一致度を 0〜1 で返す。どちらかが不明なら null（尺を手がかりにできない）。</summary>
    /// <remarks>
    /// CD のトラック尺と配信の再生時間は、同じ音源でも曲間の無音やフェードの切り方で数秒ずれる。
    /// 実データでは同一 ISRC どうしでも 1 秒弱の差が出ているため、<see cref="DurationToleranceSeconds"/>
    /// 秒までは「合っている」とみなし、そこから離れるにつれて滑らかに 0 へ落とす。
    /// </remarks>
    private static double? DurationSimilarity(int? dbSeconds, int? videoSeconds)
    {
        if (dbSeconds is not int a || videoSeconds is not int b) return null;

        int diff = Math.Abs(a - b);
        if (diff <= DurationToleranceSeconds) return 1.0;

        double over = diff - DurationToleranceSeconds;
        return Math.Max(0.0, 1.0 - over / DurationFalloffSeconds);
    }

    /// <summary>1 組の対応の確からしさ。タイトル類似度と尺一致度の良い方を採る。</summary>
    private static double PairScore(string dbKey, string videoKey, int? dbSeconds, int? videoSeconds)
    {
        double titleScore = Similarity(dbKey, videoKey);
        double? durationScore = DurationSimilarity(dbSeconds, videoSeconds);

        return durationScore is double d ? Math.Max(titleScore, d) : titleScore;
    }

    /// <summary>タイトルと尺を手がかりに、順序を保ったままスコア合計が最大になるよう対応付ける。 戻り値は先頭から順に並んだ (DB 側添字, プレイリスト側添字) の列で、相手がいない側は -1。</summary>
    /// <remarks>
    /// 対応 1 組の得点を「スコア - 0.5」、片側を空ける（ギャップ）ときの得点を -0.05 と置いている。
    /// この重みだと、スコアがおよそ 0.4 を下回る組み合わせは「対応させる」より「両側を空ける」方が
    /// 得点が高くなる。つまり表記揺れ程度の差や数秒の尺差なら拾い、別物どうしは繋がない。
    /// ギャップの罰を小さくしてあるのは、配信されていないトラックが何曲あっても
    /// 素直に飛ばせるようにするため。
    /// </remarks>
    private static List<(int DbIndex, int VideoIndex)> AlignBySimilarity(
        IReadOnlyList<string> dbKeys, IReadOnlyList<string> videoKeys,
        IReadOnlyList<int?> dbLengths, IReadOnlyList<int?> videoLengths)
    {
        const double GapScore = -0.05;
        const double MatchBias = 0.5;

        int n = dbKeys.Count, m = videoKeys.Count;
        var dp = new double[n + 1, m + 1];

        for (int i = n - 1; i >= 0; i--) dp[i, m] = dp[i + 1, m] + GapScore;
        for (int j = m - 1; j >= 0; j--) dp[n, j] = dp[n, j + 1] + GapScore;

        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                double pair = dp[i + 1, j + 1]
                    + PairScore(dbKeys[i], videoKeys[j], dbLengths[i], videoLengths[j]) - MatchBias;
                double skipDb = dp[i + 1, j] + GapScore;
                double skipVideo = dp[i, j + 1] + GapScore;
                dp[i, j] = Math.Max(pair, Math.Max(skipDb, skipVideo));
            }
        }

        var result = new List<(int, int)>();
        for (int i = 0, j = 0; i < n || j < m;)
        {
            if (i < n && j < m)
            {
                double pair = dp[i + 1, j + 1]
                    + PairScore(dbKeys[i], videoKeys[j], dbLengths[i], videoLengths[j]) - MatchBias;
                double skipDb = dp[i + 1, j] + GapScore;
                double skipVideo = dp[i, j + 1] + GapScore;

                if (pair >= skipDb && pair >= skipVideo) { result.Add((i, j)); i++; j++; }
                else if (skipDb >= skipVideo) { result.Add((i, -1)); i++; }
                else { result.Add((-1, j)); j++; }
            }
            else if (i < n) { result.Add((i, -1)); i++; }
            else { result.Add((-1, j)); j++; }
        }

        return result;
    }

    /// <summary>片方がもう片方を丸ごと含んでいるときの、基礎となる類似度。</summary>
    private const double ContainmentBaseScore = 0.75;

    /// <summary>正規化済み文字列どうしの類似度を 0〜1 で返す。
    /// <para>
    /// 基本は編集距離を長い方の文字数で割った値（1 が完全一致）。ただしそれだけだと、
    /// 配信側のタイトルが曲名の前後に情報を足した長い形のとき
    /// （「いきものがかり『うれしくて』(『映画 …』) Music Video」のような MV 名義）に
    /// 長さの差だけで類似度がほぼ 0 まで落ちてしまい、同じ曲だと判定できない。
    /// </para>
    /// <para>
    /// そこで、片方がもう片方を丸ごと含んでいる場合は「曲名としては一致している」とみなして
    /// 高い値を返す。余計な語がどれだけ付いているかで少しだけ割り引くので、
    /// 曲名そのものが一致しているほど高くなる。
    /// 短すぎる文字列は偶然の包含が起きるため対象外にする。
    /// </para>
    /// </summary>
    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0.0;
        if (string.Equals(a, b, StringComparison.Ordinal)) return 1.0;

        string shorter = a.Length <= b.Length ? a : b;
        string longer = a.Length <= b.Length ? b : a;
        if (shorter.Length >= 2 && longer.Contains(shorter, StringComparison.Ordinal))
        {
            return ContainmentBaseScore
                + (1.0 - ContainmentBaseScore) * ((double)shorter.Length / longer.Length);
        }

        int distance = LevenshteinDistance(a, b);
        return 1.0 - (double)distance / Math.Max(a.Length, b.Length);
    }

    /// <summary>編集距離（挿入・削除・置換の最小回数）。 直前の 1 行だけを保持して計算する。</summary>
    private static int LevenshteinDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static ArtTrackImportRow MakePairedRow(
        ArtTrackMatchRow db, YouTubePlaylistVideo video,
        IReadOnlyDictionary<string, YouTubeVideoDetail> details)
    {
        details.TryGetValue(video.VideoId, out var detail);
        int? videoSeconds = detail?.DurationSeconds;

        return new ArtTrackImportRow
        {
            CatalogNo = db.CatalogNo,
            TrackNo = db.TrackNo,
            SubOrder = db.SubOrder,
            DbTitle = db.DisplayTitle,
            DbLengthSeconds = db.LengthSeconds,
            YouTubeTitle = video.Title,
            VideoId = video.VideoId,
            VideoLengthSeconds = videoSeconds,
            Embeddable = detail?.Embeddable ?? false,
            TitleMatches = TitlesEquivalent(db.DisplayTitle, video.Title),
            DurationMatches = DurationSimilarity(db.LengthSeconds, videoSeconds) >= 1.0
        };
    }

    private static ArtTrackImportRow MakeDbOnlyRow(ArtTrackMatchRow db)
        => new()
        {
            CatalogNo = db.CatalogNo,
            TrackNo = db.TrackNo,
            SubOrder = db.SubOrder,
            DbTitle = db.DisplayTitle,
            DbLengthSeconds = db.LengthSeconds
        };

    private static ArtTrackImportRow MakeVideoOnlyRow(
        YouTubePlaylistVideo video, IReadOnlyDictionary<string, YouTubeVideoDetail> details)
    {
        details.TryGetValue(video.VideoId, out var detail);
        return new ArtTrackImportRow
        {
            YouTubeTitle = video.Title,
            VideoId = video.VideoId,
            VideoLengthSeconds = detail?.DurationSeconds,
            Embeddable = detail?.Embeddable ?? false
        };
    }

    /// <summary>2 つのタイトルが実質同じかを判定する。 全角半角・大小文字・波ダッシュの字種違い、および空白と一部の記号の有無は同一とみなす。 DB 側とプレイリスト側で表記の揺れが出るのはこれらに限られることを実データで確認している。</summary>
    public static bool TitlesEquivalent(string a, string b)
        => string.Equals(NormalizeTitle(a), NormalizeTitle(b), StringComparison.Ordinal);

    /// <summary>タイトル比較用の正規化。</summary>
    private static string NormalizeTitle(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // NFKC で全角英数・全角記号を半角へ寄せる。
        string n = s.Normalize(NormalizationForm.FormKC).ToLower(CultureInfo.InvariantCulture);

        // 波ダッシュ（U+301C）と全角チルダ（U+FF5E）は入力経路によって揺れるため半角チルダへ統一する。
        // NFKC は U+FF5E を U+007E へ寄せるが U+301C は変換しないので、明示的に潰す。
        n = n.Replace('〜', '~').Replace('～', '~');

        // 空白・中黒・読点類・感嘆符類は有無が揺れるので比較対象から外す。
        return Regex.Replace(n, @"[\s&・,\.!?？！]", "");
    }
}

/// <summary>1 アルバム分の取り込みプレビュー。</summary>
public sealed class ArtTrackImportPreview
{
    public string ProductCatalogNo { get; set; } = "";
    public string PlaylistId { get; set; } = "";

    /// <summary>DB 側のトラック数（商品内の全ディスク合計）。</summary>
    public int DbTrackCount { get; set; }

    /// <summary>プレイリスト側の収録件数。</summary>
    public int PlaylistItemCount { get; set; }

    public IReadOnlyList<ArtTrackImportRow> Rows { get; set; } = Array.Empty<ArtTrackImportRow>();

    /// <summary>対応が付いた行数。</summary>
    public int PairedCount => Rows.Count(r => r.VideoId.Length > 0 && r.CatalogNo.Length > 0);

    /// <summary>対応が付いた行のうち、タイトルか尺のどちらかが合っている行数。</summary>
    public int TitleMatchedCount => Rows.Count(r => r.LooksCorrect);

    /// <summary>YouTube Music Premium 会員限定と判定された行数。再生ボタンは出すが警告色にする。</summary>
    public int PremiumOnlyCount => Rows.Count(r => r.IsPremiumOnly);

    /// <summary>埋め込み不可と判定された行数。再生ボタンが出ないトラックになる。</summary>
    public int NotEmbeddableCount => Rows.Count(r => r.VideoId.Length > 0 && !r.Embeddable);

    /// <summary>配信されている曲がすべて DB のトラックに収まった件数。 配信されていないトラック（DB 側だけの行）は数えない。</summary>
    public int UnmatchedDbCount => Rows.Count(r => r.CatalogNo.Length > 0 && r.VideoId.Length == 0);

    /// <summary>DB に対応先が見つからなかったプレイリスト側の件数。 0 でないときは商品の取り違えか、DB に未登録のトラックがあることを示す。</summary>
    public int SurplusPlaylistCount => Rows.Count(r => r.VideoId.Length > 0 && r.CatalogNo.Length == 0);

    /// <summary>プレイリストの全曲が対応先を見つけ、対応した全行でタイトルか尺のどちらかが合っているか。 これを満たすときだけ商品の取り込み状態を <c>MATCHED</c> にする。 DB 側に相手のないトラック（配信されていない曲）が残るのは想定内なので判定に含めない。</summary>
    public bool IsFullyMatched
        => PairedCount > 0 && SurplusPlaylistCount == 0 && TitleMatchedCount == PairedCount;

    /// <summary>プレビュー結果の 1 行要約（UI の結果欄に出す）。</summary>
    public string SummaryText
        => $"DB {DbTrackCount} 件 / プレイリスト {PlaylistItemCount} 件 ／ "
         + $"対応 {PairedCount} 件（確からしい {TitleMatchedCount}）"
         + (UnmatchedDbCount > 0 ? $" ／ 配信なし {UnmatchedDbCount} 件" : "")
         + (SurplusPlaylistCount > 0 ? $" ／ DB に無い {SurplusPlaylistCount} 件" : "")
         + (PremiumOnlyCount > 0 ? $" ／ Premium 限定 {PremiumOnlyCount} 件" : "")
         + (NotEmbeddableCount > 0 ? $" ／ 埋め込み不可 {NotEmbeddableCount} 件" : "");
}

/// <summary>取り込みプレビューの 1 行。DB トラックとプレイリスト動画の対応を表す。</summary>
public sealed class ArtTrackImportRow
{
    /// <summary>通し位置（1 始まり）。</summary>
    public int Position { get; set; }

    /// <summary>対応する DB トラックのディスク品番。プレイリスト側だけの行は空。</summary>
    public string CatalogNo { get; set; } = "";
    public byte TrackNo { get; set; }
    public byte SubOrder { get; set; }

    /// <summary>DB 側の表示タイトル。</summary>
    public string DbTitle { get; set; } = "";

    /// <summary>DB 側のトラック尺（秒）。未取得は null。</summary>
    public int? DbLengthSeconds { get; set; }

    /// <summary>プレイリスト側のタイトル。相手がいない行は空。</summary>
    public string YouTubeTitle { get; set; } = "";

    /// <summary>動画 ID。相手がいない行は空。人が手で直せるよう、確認ダイアログから書き換えられる。</summary>
    public string VideoId { get; set; } = "";

    /// <summary>配信側の再生時間（秒）。取得できなければ null。</summary>
    public int? VideoLengthSeconds { get; set; }

    /// <summary>埋め込み再生が許可されているか。</summary>
    public bool Embeddable { get; set; }

    /// <summary>再生可否（OK / PREMIUM_ONLY / UNPLAYABLE）。未確認は null。 埋め込み可でも Premium 会員限定のことがあるため <see cref="Embeddable"/> とは別軸。</summary>
    public string? Playability { get; set; }

    /// <summary>YouTube Music Premium 会員でないと再生できない音源か。</summary>
    public bool IsPremiumOnly
        => string.Equals(Playability, YouTubePlayabilityProbe.StatusPremiumOnly, StringComparison.Ordinal);

    /// <summary>タイトルが実質一致したか。対応付けはタイトルと尺の両方で決めており、ここは確認用の印。</summary>
    public bool TitleMatches { get; set; }

    /// <summary>尺が許容差の範囲で一致したか。タイトルの表記が揺れていても、ここが立っていれば同じ音源とみなせる。</summary>
    public bool DurationMatches { get; set; }

    /// <summary>人が確認ダイアログで動画 ID を手入力・消去した行。自動判定より人の指定を優先する印。</summary>
    public bool ManuallyEdited { get; set; }

    /// <summary>尺の差（秒）。どちらかが不明なら null。</summary>
    public int? LengthDiffSeconds
        => DbLengthSeconds is int a && VideoLengthSeconds is int b ? Math.Abs(a - b) : null;

    /// <summary>タイトルか尺のどちらかが合っていれば、対応として確からしいとみなす。 配信側はタイトルの表記がしばしば変わる一方、尺は音源が同じなら変わらないため。</summary>
    public bool LooksCorrect => TitleMatches || DurationMatches;

    /// <summary>UI のグリッドに出す状態ラベル。</summary>
    public string StatusLabel
        => VideoId.Length == 0 ? (ManuallyEdited ? "手動で解除" : "配信なし")
         : CatalogNo.Length == 0 ? "DB トラックなし"
         : !Embeddable ? "埋め込み不可"
         : IsPremiumOnly ? "Premium限定"
         : string.Equals(Playability, YouTubePlayabilityProbe.StatusUnplayable, StringComparison.Ordinal) ? "再生不可"
         : ManuallyEdited ? "手動指定"
         : TitleMatches && DurationMatches ? "一致"
         : DurationMatches ? "一致(尺)"
         : TitleMatches ? "一致(題名)"
         : "要確認";
}
