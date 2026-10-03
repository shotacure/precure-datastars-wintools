using System.Configuration;
using System.Text.Json;
using Dapper;
using PrecureDataStars.Data.Db;
using PrecureDataStars.Data.TitleCharStatsJson;

namespace PrecureDataStars.TitleCharStatsRefresh;

/// <summary>
/// サブタイトル文字統計（<c>episodes.title_char_stats</c>）の作り直しツールのエントリポイント。
/// <para>
/// 指定したエピソードの <c>title_text</c> を Episodes の保存時と同じ <see cref="TitleCharStatsBuilder"/> にかけ、
/// いま DB にある統計との違いを表示する。<c>--apply</c> を付けたときだけ、違いのある話を 1 トランザクションで書き換える。
/// 書き換えるのは <c>title_char_stats</c> 列だけで、<c>updated_by</c> などほかの列には触れない。
/// </para>
/// </summary>
/// <remarks>
/// 使い方:
/// <code>
///   TitleCharStatsRefresh --episode 2012tv:13 [--episode 2011tv:23 ...] [--apply]
///   TitleCharStatsRefresh --episode-id 402 [--episode-id 364 ...] [--apply]
///   TitleCharStatsRefresh --all [--apply]
/// </code>
/// 終了コード: 0 = 正常 / 1 = 実行時エラー / 2 = 引数エラー・対象の話が見つからない。
/// </remarks>
internal static class Program
{
    /// <summary>対象エピソード 1 件分の DB 上の値。</summary>
    private sealed class EpisodeRow
    {
        public int EpisodeId { get; set; }
        public string Slug { get; set; } = "";
        public int SeriesEpNo { get; set; }
        public string? TitleText { get; set; }
        public string? TitleCharStats { get; set; }
    }

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ── 引数の解釈 ──
        var slugEps = new List<(string Slug, int EpNo)>();
        var episodeIds = new List<int>();
        bool all = false, apply = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--episode" when i + 1 < args.Length:
                    // "2012tv:13" 形式。カンマ区切りで複数並べてもよい
                    foreach (var token in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var p = token.Split(':');
                        if (p.Length != 2 || !int.TryParse(p[1], out var no))
                            return Usage($"--episode の形式が不正です: {token}（例: 2012tv:13）");
                        slugEps.Add((p[0], no));
                    }
                    break;
                case "--episode-id" when i + 1 < args.Length:
                    foreach (var token in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!int.TryParse(token, out var id)) return Usage($"--episode-id の値が不正です: {token}");
                        episodeIds.Add(id);
                    }
                    break;
                case "--all": all = true; break;
                case "--apply": apply = true; break;
                default: return Usage($"不明な引数です: {args[i]}");
            }
        }
        if (!all && slugEps.Count == 0 && episodeIds.Count == 0)
            return Usage("対象の話を --episode / --episode-id / --all のいずれかで指定してください。");
        if (all && (slugEps.Count > 0 || episodeIds.Count > 0))
            return Usage("--all と個別指定は同時に使えません。");

        var cs = ConfigurationManager.ConnectionStrings[DbConfig.DefaultConnectionStringName]?.ConnectionString;
        if (string.IsNullOrWhiteSpace(cs))
        {
            Console.Error.WriteLine("ERROR: App.config に DatastarsMySql 接続文字列が設定されていません。");
            return 1;
        }

        try
        {
            var factory = MySqlConnectionFactory.FromConnectionString(cs);
            await using var conn = await factory.CreateOpenedAsync();

            // ── 対象の話の読み込み（見つからない指定が 1 つでもあれば何も書かずに止める） ──
            const string selectSql = """
                SELECT e.episode_id AS EpisodeId, s.slug AS Slug, e.series_ep_no AS SeriesEpNo,
                       e.title_text AS TitleText, CAST(e.title_char_stats AS CHAR) AS TitleCharStats
                  FROM episodes e JOIN series s ON s.series_id = e.series_id
                """;
            var targets = new List<EpisodeRow>();
            if (all)
            {
                targets.AddRange(await conn.QueryAsync<EpisodeRow>(selectSql + " ORDER BY s.start_date, s.series_id, e.series_ep_no"));
            }
            else
            {
                foreach (var (slug, no) in slugEps)
                {
                    var row = await conn.QuerySingleOrDefaultAsync<EpisodeRow>(
                        selectSql + " WHERE s.slug = @slug AND e.series_ep_no = @no", new { slug, no });
                    if (row is null) return NotFound($"{slug} #{no}");
                    targets.Add(row);
                }
                foreach (var id in episodeIds)
                {
                    var row = await conn.QuerySingleOrDefaultAsync<EpisodeRow>(
                        selectSql + " WHERE e.episode_id = @id", new { id });
                    if (row is null) return NotFound($"episode_id {id}");
                    targets.Add(row);
                }
                targets = targets.GroupBy(t => t.EpisodeId).Select(g => g.First()).ToList();
            }

            // ── 作り直しと比較 ──
            var changed = new List<(EpisodeRow Row, string? NewJson)>();
            foreach (var row in targets)
            {
                // Episodes の保存と同じく、サブタイトルが空（未確定）なら統計も NULL
                string? newJson = string.IsNullOrWhiteSpace(row.TitleText) ? null : TitleCharStatsBuilder.BuildJson(row.TitleText);
                var diffs = Diff(row.TitleCharStats, newJson);
                string label = $"{row.Slug} #{row.SeriesEpNo}（episode_id {row.EpisodeId}）";
                if (diffs.Count == 0)
                {
                    // --all のときは一致した話を出さず、違いのある話だけを並べる
                    if (!all) Console.WriteLine($"{label}: 変更なし");
                    continue;
                }
                Console.WriteLine($"{label}: {diffs.Count} 項目が変わる");
                foreach (var d in diffs) Console.WriteLine($"    {d}");
                changed.Add((row, newJson));
            }

            Console.WriteLine();
            Console.WriteLine($"対象 {targets.Count} 話のうち、統計が変わる話 {changed.Count} 話");
            if (changed.Count == 0) return 0;
            if (!apply)
            {
                Console.WriteLine("表示のみ（書き込むには --apply を付けて実行する）");
                return 0;
            }

            // ── 書き込み（違いのある話だけ、1 トランザクションで） ──
            await using var tx = await conn.BeginTransactionAsync();
            foreach (var (row, newJson) in changed)
            {
                await conn.ExecuteAsync(
                    "UPDATE episodes SET title_char_stats = @json WHERE episode_id = @id",
                    new { json = newJson, id = row.EpisodeId }, tx);
            }
            await tx.CommitAsync();
            Console.WriteLine($"{changed.Count} 話の title_char_stats を書き換えた");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// 2 つの統計 JSON を「パス → 値の生テキスト」に平らにして比べ、違いを 1 行ずつの説明で返す。
    /// 数値は生テキストで比べるので、<c>3.0</c> と <c>3</c> のような型の違いも差分として拾う。
    /// キーの並び順（MySQL の JSON 列は保存時に並べ替える）は比較に影響しない。
    /// </summary>
    private static List<string> Diff(string? currentJson, string? newJson)
    {
        var cur = Flatten(currentJson);
        var neo = Flatten(newJson);
        var result = new List<string>();
        foreach (var key in cur.Keys.Union(neo.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            cur.TryGetValue(key, out var a);
            neo.TryGetValue(key, out var b);
            if (a != b) result.Add($"{key}: {a ?? "(なし)"} → {b ?? "(なし)"}");
        }
        return result;
    }

    /// <summary>JSON をドット区切りのパスと値の生テキストの辞書にする。NULL は「(NULL)」1 項目として扱う。</summary>
    private static Dictionary<string, string> Flatten(string? json)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (json is null) { dict["(全体)"] = "(NULL)"; return dict; }
        using var doc = JsonDocument.Parse(json);
        Walk(doc.RootElement, "");
        return dict;

        void Walk(JsonElement el, string path)
        {
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in el.EnumerateObject())
                    Walk(p.Value, path.Length == 0 ? p.Name : $"{path}.{p.Name}");
            }
            else
            {
                dict[path] = el.GetRawText();
            }
        }
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine($"ERROR: {message}");
        Console.Error.WriteLine("使い方: TitleCharStatsRefresh (--episode <slug>:<話数> | --episode-id <id> | --all) [--apply]");
        return 2;
    }

    private static int NotFound(string what)
    {
        Console.Error.WriteLine($"ERROR: 対象の話が見つかりません: {what}（何も書き込んでいない）");
        return 2;
    }
}
