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

    /// <summary>取り込み結果をプレビューする（DB へは書き込まない）。 プレイリストを展開し、商品の全トラックと並び順を保ったまま対応付けて返す。 配信されていないトラックは相手なしのまま残る。埋め込み可否もこの段階で取得する。</summary>
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
        var embeddable = await _api.GetEmbeddableAsync(videos.Select(v => v.VideoId), ct).ConfigureAwait(false);

        var rows = BuildAlignedRows(dbRows, videos, embeddable);

        return new ArtTrackImportPreview
        {
            ProductCatalogNo = productCatalogNo,
            PlaylistId = playlistId,
            DbTrackCount = dbRows.Count,
            PlaylistItemCount = videos.Count,
            Rows = rows
        };
    }

    /// <summary>プレビュー結果を DB へ反映する。 対応が付いた行は動画 ID を書き込み、対応が付かなかった DB トラックは動画 ID を消す。 消すのは、以前の取り込みで付いた誤った割り当てを残さないため（配信されていないトラックは 動画 ID を持たない状態が正しく、サイト側でも再生ボタンが出なくなる）。 商品側には取り込み状態を記録する：プレイリストの全曲が対応先を見つけ、かつ対応した全行で タイトルも一致していれば <c>MATCHED</c>、そうでなければ <c>AMBIGUOUS</c>（人の確認が要る状態）。</summary>
    /// <param name="preview">プレビュー結果。</param>
    /// <param name="ct">キャンセルトークン。</param>
    public async Task ApplyAsync(ArtTrackImportPreview preview, CancellationToken ct = default)
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
                YoutubeEmbeddable = r.VideoId.Length > 0 ? r.Embeddable : null
            })
            .ToList();

        var now = DateTime.Now;
        await _tracksRepo.UpdateArtTrackAssignmentsAsync(assignments, now, ct).ConfigureAwait(false);
        await _productsRepo.UpdateArtTrackPlaylistAsync(
            preview.ProductCatalogNo, preview.PlaylistId,
            preview.IsFullyMatched ? "MATCHED" : "AMBIGUOUS", now, ct).ConfigureAwait(false);
    }


    /// <summary>DB のトラック列とプレイリストの動画列を、並び順を保ったまま対応付ける。
    /// <para>
    /// タイトルの完全一致は期待できない（配信側とデータベース側で表記の揺れが常にある）ため、
    /// 一致・不一致の二値ではなく<b>類似度</b>を使った系列アライメントで対応を決める。
    /// 隣接関係を壊さずに全体の類似度合計が最大になる対応を求めるので、配信されていない
    /// トラックが途中に挟まっても、そこだけが「相手なし」になり以降のずれは起きない。
    /// </para>
    /// <para>
    /// 似ていない組み合わせを無理に対応付けるより両側を空けた方が得点が高くなるよう重みを
    /// 決めてあり、結果として「配信されていないトラック」と「DB に無い配信限定トラック」は
    /// 自然に相手なしとして残る。対応が付いた組のうちタイトルが完全一致しなかったものは
    /// 「要確認」として印を付け、人が最終的に見られるようにする。
    /// </para>
    /// </summary>
    private static List<ArtTrackImportRow> BuildAlignedRows(
        IReadOnlyList<ArtTrackMatchRow> dbRows,
        IReadOnlyList<YouTubePlaylistVideo> videos,
        IReadOnlyDictionary<string, bool> embeddable)
    {
        var pairs = AlignBySimilarity(
            dbRows.Select(r => NormalizeTitle(r.DisplayTitle)).ToArray(),
            videos.Select(v => NormalizeTitle(v.Title)).ToArray());

        var rows = new List<ArtTrackImportRow>();
        foreach (var (dbIndex, videoIndex) in pairs)
        {
            if (dbIndex >= 0 && videoIndex >= 0)
            {
                var db = dbRows[dbIndex];
                var video = videos[videoIndex];
                rows.Add(MakePairedRow(db, video, embeddable,
                    titleMatches: TitlesEquivalent(db.DisplayTitle, video.Title)));
            }
            else if (dbIndex >= 0) rows.Add(MakeDbOnlyRow(dbRows[dbIndex]));
            else rows.Add(MakeVideoOnlyRow(videos[videoIndex], embeddable));
        }

        for (int i = 0; i < rows.Count; i++) rows[i].Position = i + 1;
        return rows;
    }

    /// <summary>正規化済みタイトル列どうしを、順序を保ったまま類似度合計が最大になるよう対応付ける。 戻り値は先頭から順に並んだ (DB 側添字, プレイリスト側添字) の列で、相手がいない側は -1。</summary>
    /// <remarks>
    /// 対応 1 組の得点を <c>類似度 - 0.5</c>、片側を空ける（ギャップ）ときの得点を <c>-0.05</c> と置いている。
    /// この重みだと、類似度がおよそ 0.4 を下回る組み合わせは「対応させる」より「両側を空ける」方が
    /// 得点が高くなる。つまり表記揺れ程度の差なら拾い、別物のタイトルどうしは繋がない。
    /// ギャップの罰を小さくしてあるのは、配信されていないトラックが何曲あっても
    /// 素直に飛ばせるようにするため。
    /// </remarks>
    private static List<(int DbIndex, int VideoIndex)> AlignBySimilarity(
        IReadOnlyList<string> dbKeys, IReadOnlyList<string> videoKeys)
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
                double pair = dp[i + 1, j + 1] + Similarity(dbKeys[i], videoKeys[j]) - MatchBias;
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
                double pair = dp[i + 1, j + 1] + Similarity(dbKeys[i], videoKeys[j]) - MatchBias;
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

    /// <summary>正規化済み文字列どうしの類似度を 0〜1 で返す。 編集距離を長い方の文字数で割った値を 1 から引いたもの（1 が完全一致）。 表記揺れ（記号や送り仮名の差）は高い値に、別の曲名どうしは低い値になる。</summary>
    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0.0;
        if (string.Equals(a, b, StringComparison.Ordinal)) return 1.0;

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
        IReadOnlyDictionary<string, bool> embeddable, bool titleMatches)
        => new()
        {
            CatalogNo = db.CatalogNo,
            TrackNo = db.TrackNo,
            SubOrder = db.SubOrder,
            DbTitle = db.DisplayTitle,
            YouTubeTitle = video.Title,
            VideoId = video.VideoId,
            Embeddable = embeddable.TryGetValue(video.VideoId, out var e) && e,
            TitleMatches = titleMatches
        };

    private static ArtTrackImportRow MakeDbOnlyRow(ArtTrackMatchRow db)
        => new()
        {
            CatalogNo = db.CatalogNo,
            TrackNo = db.TrackNo,
            SubOrder = db.SubOrder,
            DbTitle = db.DisplayTitle
        };

    private static ArtTrackImportRow MakeVideoOnlyRow(
        YouTubePlaylistVideo video, IReadOnlyDictionary<string, bool> embeddable)
        => new()
        {
            YouTubeTitle = video.Title,
            VideoId = video.VideoId,
            Embeddable = embeddable.TryGetValue(video.VideoId, out var e) && e
        };

    /// <summary>2 つのタイトルが実質同じかを判定する。 全角半角・大小文字・波ダッシュの字種違い、および空白と一部の記号の有無は同一とみなす。 DB 側とプレイリスト側で表記の揺れが出るのはこれらに限られることを実データで確認している。</summary>
    internal static bool TitlesEquivalent(string a, string b)
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

    /// <summary>対応が付いた行のうちタイトルも完全一致した行数。</summary>
    public int TitleMatchedCount => Rows.Count(r => r.TitleMatches);

    /// <summary>埋め込み不可と判定された行数。再生ボタンが出ないトラックになる。</summary>
    public int NotEmbeddableCount => Rows.Count(r => r.VideoId.Length > 0 && !r.Embeddable);

    /// <summary>配信されている曲がすべて DB のトラックに収まった件数。 配信されていないトラック（DB 側だけの行）は数えない。</summary>
    public int UnmatchedDbCount => Rows.Count(r => r.CatalogNo.Length > 0 && r.VideoId.Length == 0);

    /// <summary>DB に対応先が見つからなかったプレイリスト側の件数。 0 でないときは商品の取り違えか、DB に未登録のトラックがあることを示す。</summary>
    public int SurplusPlaylistCount => Rows.Count(r => r.VideoId.Length > 0 && r.CatalogNo.Length == 0);

    /// <summary>プレイリストの全曲が対応先を見つけ、対応した全行でタイトルも一致しているか。 これを満たすときだけ商品の取り込み状態を <c>MATCHED</c> にする。 DB 側に相手のないトラック（配信されていない曲）が残るのは想定内なので判定に含めない。</summary>
    public bool IsFullyMatched
        => PairedCount > 0 && SurplusPlaylistCount == 0 && TitleMatchedCount == PairedCount;

    /// <summary>プレビュー結果の 1 行要約（UI の結果欄に出す）。</summary>
    public string SummaryText
        => $"DB {DbTrackCount} 件 / プレイリスト {PlaylistItemCount} 件 ／ "
         + $"対応 {PairedCount} 件（タイトル一致 {TitleMatchedCount}）"
         + (UnmatchedDbCount > 0 ? $" ／ 配信なし {UnmatchedDbCount} 件" : "")
         + (SurplusPlaylistCount > 0 ? $" ／ DB に無い {SurplusPlaylistCount} 件" : "")
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

    /// <summary>プレイリスト側のタイトル。相手がいない行は空。</summary>
    public string YouTubeTitle { get; set; } = "";

    /// <summary>動画 ID。相手がいない行は空。</summary>
    public string VideoId { get; set; } = "";

    /// <summary>埋め込み再生が許可されているか。</summary>
    public bool Embeddable { get; set; }

    /// <summary>タイトルが実質一致したか。対応付けは類似度で決めており、ここは確認用の印。</summary>
    public bool TitleMatches { get; set; }

    /// <summary>UI のグリッドに出す状態ラベル。</summary>
    public string StatusLabel
        => VideoId.Length == 0 ? "配信なし"
         : CatalogNo.Length == 0 ? "DB トラックなし"
         : !Embeddable ? "埋め込み不可"
         : TitleMatches ? "一致"
         : "要確認";
}
