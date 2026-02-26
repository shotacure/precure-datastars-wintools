using System.Configuration;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using PrecureDataStars.Data.Db;

namespace PrecureDataStars.YouTubeCrawler;

/// <summary>
/// 東映アニメーション公式サイトのあらすじページから YouTube 予告動画 URL を自動抽出するクローラー。
/// <para>
/// 対象条件: series_id &gt;= 30（スイートプリキュア♪以降）かつ
/// toei_anim_summary_url が設定済みで youtube_trailer_url が未設定のエピソード。
/// 1 秒/件のスロットリング付き。
/// </para>
/// <remarks>
/// 抽出パターン（優先順）: watch?v= / data-mvid= / embed/ / youtu.be/ / img.youtube.com/vi/ / JSON videoId。
/// ブロック対象の動画 ID (iqGPVmdr-3A = キミプリ番宣) は全パターンの否定先読みで除外される。
/// </remarks>
/// </summary>
internal static class Program
{
    /// <summary>監査列 (updated_by) に記録するユーザー識別子。</summary>
    private const string AuditUser = "youtube_crawler";

    /// <summary>除外対象の YouTube 動画 ID（キミプリ番組宣伝動画）。全パターンの否定先読みで使用。</summary>
    private const string BlockedVideoId = "iqGPVmdr-3A";

    /// <summary>YouTube 動画 ID パターン（11 文字の英数字/ハイフン/アンダースコア）。BlockedVideoId は否定先読みで除外。</summary>
    private const string IdPat = @"(?!(?:" + BlockedVideoId + @"))([A-Za-z0-9_-]{11})";

    // --- 各埋め込み形式に対応する正規表現（全て IdPat で BlockedVideoId を除外）---
    /// <summary>標準 watch URL（https://www.youtube.com/watch?v=XXXXXXXXXXX）。</summary>
    private static readonly Regex RxWatch =
        new(@"https?://(?:www\.)?youtube\.com/watch\?[^""'\s>]*\bv=" + IdPat,
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>embed URL（https://www.youtube.com/embed/XXXXXXXXXXX）。</summary>
    private static readonly Regex RxEmbed =
        new(@"https?://(?:www\.)?youtube(?:-nocookie)?\.com/embed/" + IdPat,
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>短縮 URL（https://youtu.be/XXXXXXXXXXX）。</summary>
    private static readonly Regex RxShort =
        new(@"https?://youtu\.be/" + IdPat,
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>JSON 内の videoId フィールド（"videoId":"XXXXXXXXXXX"）。</summary>
    private static readonly Regex RxJsonVideoId =
        new(@"""videoId""\s*:\s*""" + IdPat + @"""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>data-mvid 属性（東映サイト固有の動画 ID 埋め込み形式）。</summary>
    private static readonly Regex RxDataMvid =
        new(@"data-mvid\s*=\s*[""']?" + IdPat + @"\??[""']?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>サムネイル画像 URL（https://img.youtube.com/vi/XXXXXXXXXXX/）。</summary>
    private static readonly Regex RxImgThumb =
        new(@"https?://img\.youtube\.com/vi/" + IdPat + @"/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// エントリポイント。対象エピソードを走査し、東映あらすじページから YouTube 動画 URL を抽出・保存する。
    /// </summary>
    private static async Task<int> Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 接続準備
        var cs = ConfigurationManager.ConnectionStrings["DatastarsMySql"]?.ConnectionString
                 ?? throw new InvalidOperationException(
                     "Connection string 'DatastarsMySql' not found in App.config.");
        var factory = new MySqlConnectionFactory(new DbConfig(cs));

        Console.WriteLine("PrecureDataStars.YouTubeCrawler");
        Console.WriteLine("- 条件: series_id >= 30 AND toei_anim_summary_url IS NOT NULL/EMPTY AND youtube_trailer_url IS NULL");
        Console.WriteLine();

        // 対象件数
        int total;
        await using (var conn = await factory.CreateOpenedAsync())
        {
            total = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM episodes
                    WHERE is_deleted = 0
                      AND series_id >= 30
                      AND toei_anim_summary_url IS NOT NULL
                      AND toei_anim_summary_url <> ''
                      AND youtube_trailer_url IS NULL
                    """));
        }

        if (total == 0)
        {
            Console.WriteLine("対象レコードはありません。");
            return 0;
        }

        Console.WriteLine($"対象件数: {total} 件");
        Console.WriteLine();

        // 対象一覧
        List<(int EpisodeId, int SeriesId, string ToeiUrl)> targets;
        await using (var conn = await factory.CreateOpenedAsync())
        {
            var rows = await conn.QueryAsync<(int EpisodeId, int SeriesId, string ToeiUrl)>(
                new CommandDefinition(
                    """
                    SELECT episode_id AS EpisodeId,
                           series_id  AS SeriesId,
                           toei_anim_summary_url AS ToeiUrl
                    FROM episodes
                    WHERE is_deleted = 0
                      AND series_id >= 30
                      AND toei_anim_summary_url IS NOT NULL
                      AND toei_anim_summary_url <> ''
                      AND youtube_trailer_url IS NULL
                    ORDER BY on_air_at, episode_id;
                    """));

            targets = rows.ToList();
        }

        // HTTP クライアント準備（Chrome を模倣する User-Agent で東映サイトにアクセス）
        using var http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        });
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(Windows NT 10.0; Win64; x64)"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppleWebKit", "537.36"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Chrome", "124.0.0.0"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Safari", "537.36"));

        // 進捗管理（プログレスバー表示用）
        var sw = Stopwatch.StartNew();
        int done = 0, ok = 0, skipped = 0, failed = 0;

        Console.WriteLine("クロール開始（およそ 1秒/件）");
        WriteProgress(done, total, sw.Elapsed, suffix: "");

        // 1 件ずつ東映あらすじページを取得し、YouTube 動画 ID を抽出
        foreach (var t in targets)
        {
            string? watchUrl = null;

            try
            {
                // 東映あらすじページの HTML を取得
                var html = await FetchAsync(http, t.ToeiUrl);

                // HTML から YouTube 動画 ID を抽出（6 パターンの優先順位で試行）
                watchUrl = ExtractWatchUrl(html);

                if (watchUrl is null)
                {
                    skipped++;
                }
                else
                {
                    await UpdateYoutubeAsync(factory, t.EpisodeId, watchUrl);
                    ok++;
                }
            }
            catch
            {
                failed++;
            }
            finally
            {
                done++;
                var suffix = $" OK:{ok}  Skipped:{skipped}  Error:{failed}";
                WriteProgress(done, total, sw.Elapsed, suffix);
                await Task.Delay(1000); // 東映サーバーへの負荷軽減のため 1 秒/件のスロットリング
            }
        }

        Console.WriteLine();
        Console.WriteLine("完了。");
        Console.WriteLine($"OK={ok}, Skipped(見つからず)={skipped}, Error={failed}");
        return 0;
    }

    /// <summary>指定 URL の HTML を GET で取得して文字列として返す。</summary>
    /// <param name="http">HTTP クライアント。</param>
    /// <param name="url">取得先 URL。</param>
    /// <returns>レスポンス本文の文字列。</returns>
    private static async Task<string> FetchAsync(HttpClient http, string url)
    {
        using var res = await http.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// HTML 文字列から YouTube 動画 ID を複数パターンで抽出し、watch URL に変換して返す。
    /// 優先順: watch → data-mvid → embed → short → img thumb → JSON videoId。
    /// いずれにも該当しない場合は <c>null</c>。
    /// </summary>
    /// <param name="html">検索対象の HTML 文字列。</param>
    /// <returns>YouTube watch URL、または抽出失敗時は <c>null</c>。</returns>
    private static string? ExtractWatchUrl(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        // 各パターンを優先順位順に試行（全て IdPat の否定先読みにより BlockedVideoId は除外済み）
        string? id = FindId(RxWatch.Matches(html));
        if (id is null) id = FindId(RxDataMvid.Matches(html));
        if (id is null) id = FindId(RxEmbed.Matches(html));
        if (id is null) id = FindId(RxShort.Matches(html));
        if (id is null) id = FindId(RxImgThumb.Matches(html));
        if (id is null) id = FindId(RxJsonVideoId.Matches(html));

        return id is null ? null : $"https://www.youtube.com/watch?v={id}";
    }

    /// <summary>
    /// 正規表現マッチ結果のコレクションから、最初の有効な 11 文字 YouTube 動画 ID を返す。
    /// </summary>
    /// <param name="matches">正規表現マッチ結果。Group[1] に動画 ID が格納されている想定。</param>
    /// <returns>11 文字の動画 ID、または見つからない場合は <c>null</c>。</returns>
    private static string? FindId(MatchCollection matches)
    {
        foreach (Match m in matches)
        {
            if (!m.Success || m.Groups.Count < 2) continue;
            var id = m.Groups[1].Value;
            if (id.Length == 11) return id;
        }
        return null;
    }

    /// <summary>
    /// YouTube URL更新(非同期)
    /// </summary>
    /// <param name="factory">MySQL Connection Factory</param>
    /// <param name="episodeId">話数</param>
    /// <param name="url">URL</param>
    /// <returns>影響行数</returns>
    private static async Task UpdateYoutubeAsync(MySqlConnectionFactory factory, int episodeId, string url)
    {
        await using var conn = await factory.CreateOpenedAsync();
        await conn.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE episodes
                SET youtube_trailer_url = @url,
                    updated_by = @updatedBy
                WHERE episode_id = @episodeId;
                """,
                new { url, updatedBy = AuditUser, episodeId }));
    }

    /// <summary>
    /// プログレスバー表示
    /// </summary>
    /// <param name="done">完了分</param>
    /// <param name="total">総数</param>
    /// <param name="elapsed">経過時間</param>
    /// <param name="suffix">結果カウント文字列</param>
    private static void WriteProgress(int done, int total, TimeSpan elapsed, string suffix)
    {
        double ratio = total == 0 ? 1.0 : (double)done / total;
        int barWidth = 30;
        int filled = (int)Math.Round(barWidth * ratio);

        double avgSec = done == 0 ? 0 : elapsed.TotalSeconds / done;
        double remainSec = (total - done) * avgSec;
        var eta = TimeSpan.FromSeconds(remainSec);

        var bar = "[" + new string('#', filled) + new string('.', Math.Max(0, barWidth - filled)) + "]";
        Console.Write($"\r{bar} {done}/{total}  {ratio:P0}  ETA {eta:mm\\:ss}  {suffix}   ");
    }
}
